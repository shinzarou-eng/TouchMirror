using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TouchMirror.Engine;
using TouchMirror.Services;
using TouchMirror.Video;
using TouchMirror.Views;

namespace TouchMirror.ViewModels;

public partial class MirrorInstance : ObservableObject, IDisposable
{
    protected static string L(string key) => LocalizationService.Get(key);
    public AdbDevice Device { get; }
    public MirrorView View { get; } = new();

    public EngineSession? Session { get; private set; }
    public VideoDecoder? Decoder { get; private set; }
    public AudioPlayer? Audio { get; private set; }
    private GpuPresenter? _presenter;

    public virtual bool IsIos => false;

    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private int _slot;
    [ObservableProperty] private string? _accentHex;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CodecBadgeVisibility))]
    private string? _codecBadge;
    public Visibility CodecBadgeVisibility =>
        string.IsNullOrEmpty(CodecBadge) ? Visibility.Collapsed : Visibility.Visible;
    private bool _codecHwSeen;

    public WorkspaceDevice? Prefs { get; set; }

    public int? AccountUserId { get; set; }
    public string? AccountName { get; set; }
    public bool HasAccount => AccountName != null;
    public string AccountInitial => AvatarPalette.Initial(AccountName);
    public string AccountAvatarHex => AvatarPalette.For(AccountName);
    public string? AccountAvatarPath { get; set; }
    public bool HasAccountAvatar => AccountAvatarPath != null;
    public bool ShowAccountLetterAvatar => HasAccount && !HasAccountAvatar;
    public string IdentityKey => AccountUserId is { } id ? $"{Device.DeviceKey}#{id}" : Device.DeviceKey;

    public bool ManualDisconnect { get; set; }
    public bool UnexpectedDeath { get; private set; }
    public string LastDeviceState { get; private set; } = "unknown";
    [ObservableProperty] private bool _isReconnecting;
    [ObservableProperty] private string? _reconnectStatus;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSessionElapsed))]
    private string? _sessionElapsed;
    public bool HasSessionElapsed => SessionElapsed != null;
    private EngineOptions? _lastOptions;
    private int _reconnectAttempts;
    private int _incidents;
    private double _fpsMin = double.MaxValue, _fpsSum;
    private int _fpsSamples;
    private long _rxBytesFinal;
    private TimeSpan? _sessionDuration;
    private long _connectedAt;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private Task? _disconnectTask;
    private bool _stopping;
    private bool _disposed;

    public ObservableCollection<KeybindItem> Keybinds { get; } = new();
    [ObservableProperty] private bool _keybindEditMode;
    [ObservableProperty] private int _keybindStyle;
    [ObservableProperty] private double _keybindOpacity = 0.92;
    [ObservableProperty] private double _keybindSize = 30;
    public event Action? KeybindsChanged;

    private bool _suppressKeybindEvents;

    private void ApplyKeybindAppearance() =>
        View.Dispatcher.Invoke(() => View.SetKeybindAppearance(KeybindStyle, KeybindOpacity, KeybindSize));

    private void RaiseKeybindsChanged()
    {
        if (!_suppressKeybindEvents)
            KeybindsChanged?.Invoke();
    }

    partial void OnKeybindStyleChanged(int value) { ApplyKeybindAppearance(); RaiseKeybindsChanged(); }
    partial void OnKeybindOpacityChanged(double value) { ApplyKeybindAppearance(); RaiseKeybindsChanged(); }
    partial void OnKeybindSizeChanged(double value) { ApplyKeybindAppearance(); RaiseKeybindsChanged(); }
    public event Action? EditModeExitRequested;

    public event Action<MirrorInstance, string, int>? OverlayLineClicked;
    public event Action<MirrorInstance, string, double, double>? OverlayMoved;
    public Func<string, (double X, double Y)?>? OverlayPositionOf
    {
        get => View.OverlayPositionOf;
        set => View.OverlayPositionOf = value;
    }

    partial void OnKeybindEditModeChanged(bool value) =>
        View.Dispatcher.Invoke(() => View.SetKeybindEditMode(value));

    public void LoadKeybinds(IEnumerable<KeybindData> data,
        int style = 0, double opacity = 0.92, double size = 30)
    {
        _suppressKeybindEvents = true;
        try
        {
            KeybindStyle = style;
            KeybindOpacity = opacity;
            KeybindSize = size;
            Keybinds.Clear();
            foreach (var d in data)
                Keybinds.Add(new KeybindItem { Key = d.Key, Rx = d.Rx, Ry = d.Ry });
        }
        finally
        {
            _suppressKeybindEvents = false;
        }
        ApplyKeybindAppearance();
    }

    public List<KeybindData> SaveKeybinds() =>
        Keybinds.Select(k => new KeybindData { Key = k.Key, Rx = k.Rx, Ry = k.Ry }).ToList();

    private int _gpuNotifyPending;
    private Mp4Recorder? _recorder;
    private readonly object _recorderLock = new();
    private string? _recordPath;
    public DateTime? RecordingSince { get; private set; }
    public DateTime? ConnectedSince { get; private set; }
    public TimeSpan? SessionDuration => _sessionDuration;
    public void TickSessionElapsed()
    {
        var d = IsConnected && ConnectedSince is { } cs ? DateTime.Now - cs : _sessionDuration;
        SessionElapsed = d is { } t
            ? t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}" : $"{t.Minutes} min"
            : null;
    }
    public double FpsAvg => _fpsSamples > 0 ? _fpsSum / _fpsSamples : 0;
    public double FpsMin => _fpsMin == double.MaxValue ? 0 : _fpsMin;
    public long RxBytesFinal => _rxBytesFinal;
    public int Incidents => _incidents;
    private readonly object _decoderLock = new();
    private long _decoderBornAt;
    private readonly object _audioLock = new();
    private bool _screenDimmed;
    private bool _audioBroken;
    private AdbDevice _resolvedDevice;
    private DispatcherTimer? _watchdog;
    private string? _codecOverride;

    public Func<bool>? ShouldSyncClipboard { get; set; }

    public event Action<string>? Log;
    public event Action<MirrorInstance>? Disconnected;
    public event Action<MirrorInstance>? Connected;

    protected void RaiseLog(string message) => Log?.Invoke(message);
    protected void RaiseConnected() => Connected?.Invoke(this);
    protected void RaiseDisconnected() => Disconnected?.Invoke(this);

    public MirrorInstance(AdbDevice device)
    {
        Device = device;
        _resolvedDevice = device;
        DeviceName = device.DisplayName;
        View.DataContext = this;
        View.BindKeybinds(Keybinds);
        View.EditModeExitRequested += () => EditModeExitRequested?.Invoke();
        View.OverlayLineClicked += (id, idx) => OverlayLineClicked?.Invoke(this, id, idx);
        View.OverlayMoved += (id, rx, ry) => OverlayMoved?.Invoke(this, id, rx, ry);
        Keybinds.CollectionChanged += OnKeybindsCollectionChanged;
    }

    private void OnKeybindsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (KeybindItem k in e.NewItems)
                k.PropertyChanged += OnKeybindPropertyChanged;
        if (e.OldItems != null)
            foreach (KeybindItem k in e.OldItems)
                k.PropertyChanged -= OnKeybindPropertyChanged;
        RaiseKeybindsChanged();
    }

    private void OnKeybindPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(KeybindItem.Key) or nameof(KeybindItem.Rx) or nameof(KeybindItem.Ry))
            RaiseKeybindsChanged();
    }

    public async Task StartAsync(EngineOptions options)
    {
        await _sessionGate.WaitAsync(_lifetime.Token);
        try
        {
            _codecOverride = null;
            _reconnectAttempts = 0;
            await StartSessionAsync(options);
        }
        catch
        {
            await ClearSessionAsync();
            throw;
        }
        finally { _sessionGate.Release(); }
    }

    private async Task StartSessionAsync(EngineOptions options)
    {
        _lifetime.Token.ThrowIfCancellationRequested();
        if (_codecOverride is { } oc)
            options = options with { VideoCodec = oc };
        _lastOptions = options;
        var session = new EngineSession(_resolvedDevice, options, _lifetime.Token);
        Session = session;
        _videoBitRate = options.VideoBitRate;
        _currentBitRate = options.VideoBitRate;
        _adaptiveBitrate = options.AdaptiveBitrate;
        _mBasePts = -1;
        _mLagEma = _mJitterEma = _adaptLagRef = 0;
        _adaptGoodStreak = 0;
        _adaptWatch.Restart();
        session.ServerLog += m => Log?.Invoke(m);
        session.VideoSizeChanged += (w, h) =>
            View.Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(Session, session))
                    View.OnVideoSize(w, h);
            });
        session.DeviceClipboard += text =>
        {
            if (ShouldSyncClipboard?.Invoke() == false)
                return;
            try { Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text)); } catch { }
        };
        session.Disconnected += () =>
        {
            try { View.Dispatcher.BeginInvoke(() => AppLogger.Forget(OnSessionLostAsync(session))); }
            catch (InvalidOperationException) { }
        };

        session.VideoPacketReceived += packet =>
        {
            TrackStreamMetrics(packet);
            if (!_videoHidden || !_encoderSuspended)
            {
                lock (_decoderLock)
                {
                    if (Decoder == null)
                    {
                        GpuPresenter? presenter = null;
                        if (options.VideoDecoder == "gpu")
                        {
                            try
                            {
                                presenter = new GpuPresenter();
                                presenter.Sharpness = options.VideoSharpen ? GpuPresenter.DefaultSharpness : 0f;
                                presenter.Fxaa = options.VideoFxaa;
                                presenter.SetColorAdjust((float)options.VideoBrightness,
                                    (float)options.VideoContrast, (float)options.VideoSaturation);
                                presenter.SetEffects((float)options.VideoVibrance,
                                    (float)options.VideoVignette, (float)options.VideoGamma);
                            }
                            catch (Exception ex) { Log?.Invoke($"gpu presenter: {ex.Message}"); }
                        }
                        _presenter = presenter;
                        Decoder = new VideoDecoder(session.VideoCodecId ?? "h264",
                            preferHardware: options.VideoDecoder != "cpu",
                            gpuPresenter: presenter);
                        if (presenter != null)
                        {
                            Decoder.GpuFrame += presenter.Present;
                            Decoder.SwFrame += presenter.PresentSoftware;
                            presenter.FrameReady += () =>
                            {
                                if (Interlocked.Exchange(ref _gpuNotifyPending, 1) == 0)
                                    try
                                    {
                                        View.Dispatcher.BeginInvoke(() =>
                                        {
                                            _gpuNotifyPending = 0;
                                            View.OnGpuFrame();
                                        });
                                    }
                                    catch { _gpuNotifyPending = 0; }
                            };
                        }
                        Decoder.Error += m => Log?.Invoke($"decoder: {m}");
                        var p = presenter;
                        var d = Decoder;
                        View.Dispatcher.BeginInvoke(() =>
                        {
                            if (ReferenceEquals(Decoder, d) && ReferenceEquals(_presenter, p))
                                View.AttachDecoder(d, p);
                        });
                        _codecHwSeen = false;
                        _decoderBornAt = Environment.TickCount64;
                        CodecBadge = (session.VideoCodecId ?? "h264").ToUpperInvariant();
                    }
                    Decoder.Feed(packet.Data, packet.Length);
                    if (!_codecHwSeen && Decoder.HardwareDecoding)
                    {
                        _codecHwSeen = true;
                        CodecBadge += " · GPU";
                    }
                }
            }
            try
            {
                lock (_recorderLock)
                {
                    if (_recorder != null)
                    {
                        if (packet.IsConfig) _recorder.WriteConfig(packet.Data, packet.Length);
                        else _recorder.WritePacket(packet.Data, packet.Length, packet.Pts, packet.IsKeyFrame);
                    }
                }
            }
            catch { }
        };
        session.AudioEnded += _ =>
        {
            if (_audioBroken)
                return;
            _audioBroken = true;
            RaiseLog(L("log.audio_unavailable"));
            lock (_audioLock)
            {
                Audio?.Dispose();
                Audio = null;
            }
        };
        session.AudioPacketReceived += packet =>
        {
            if (_audioBroken)
                return;
            try
            {
                lock (_audioLock)
                {
                    if (Audio == null)
                    {
                        Audio = new AudioPlayer(session.AudioCodecId ?? "opus");
                        Audio.Error += m =>
                        {
                            Log?.Invoke($"audio: {m}");
                            _audioBroken = true;
                        };
                        Audio.Volume = _audioMuted ? 0f : _audioVolume;
                    }
                    Audio.Feed(packet.Data, packet.IsConfig, packet.Length);
                }
            }
            catch (Exception ex)
            {
                _audioBroken = true;
                lock (_audioLock)
                {
                    Audio?.Dispose();
                    Audio = null;
                }
                RaiseLog(string.Format(L("log.audio_fail"), ex.Message));
            }
        };

        await session.StartAsync();
        _lifetime.Token.ThrowIfCancellationRequested();
        if (session.HasEnded)
            throw new IOException("Stream ended during startup");

        IsConnected = true;
        ConnectedSince = DateTime.Now;
        _connectedAt = Environment.TickCount64;
        lock (_decoderLock)
            _decoderBornAt = _connectedAt;
        DeviceName = AccountName != null
            ? $"{Device.CustomName ?? session.DeviceName ?? Device.ShortName} · {AccountName}"
            : Device.CustomName ?? session.DeviceName ?? Device.DisplayName;
        View.Dispatcher.Invoke(() => View.AttachControl(session.Control!));
        if (_videoHidden)
        {
            session.Control?.SetVideoParams(ThrottleBitRate, suspend: true);
            _encoderSuspended = true;
        }
        Connected?.Invoke(this);

        if (_watchdog == null)
        {
            _watchdog = new DispatcherTimer(DispatcherPriority.Background, View.Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _watchdog.Tick += (_, _) => CheckStream();
        }
        _watchdog.Start();

        if (options.TurnScreenOff)
            AppLogger.Forget(SetScreenDimmedAsync(true));
    }

    private void CheckStream()
    {
        var s = Session;
        if (s == null || !IsConnected || _videoHidden || _stopping || _disposed)
            return;
        var now = Environment.TickCount64;
        if (now - s.ConnectedAt > 10000)
        {
            var vf = View.Dispatcher.Invoke(() => View.CurrentFps);
            if (vf > 0)
            {
                _fpsSum += vf;
                _fpsSamples++;
                if (vf < _fpsMin)
                    _fpsMin = vf;
            }
        }
        var last = s.LastPacketAt;
        var idle = now - (last == 0 ? s.ConnectedAt : last);
        long decoded;
        long decoderBorn;
        lock (_decoderLock)
        {
            decoded = Decoder?.DecodedFrames ?? 0;
            decoderBorn = _decoderBornAt;
        }
        if (decoded == 0 && s.VideoPackets >= 30 && now - decoderBorn > 8000)
        {
            var current = _codecOverride ?? s.VideoCodecId ?? "h264";
            var next = current switch { "av1" => "h265", "h265" => "h264", _ => null };
            if (next != null)
            {
                _codecOverride = next;
                RaiseLog(string.Format(L("log.codec_fallback"), current, next));
            }
            else
            {
                RaiseLog(L("log.stream_stalled"));
            }
            _incidents++;
            s.BreakConnection();
            return;
        }
        if (s.VideoPackets == 0 && now - s.ConnectedAt > 15000)
        {
            RaiseLog(L("log.no_frames"));
            _incidents++;
            s.BreakConnection();
            return;
        }
        if (idle > 25000)
        {
            RaiseLog(L("log.stream_stalled"));
            _incidents++;
            s.BreakConnection();
        }
    }

    public async Task SetScreenDimmedAsync(bool dimmed)
    {
        if (dimmed && (Session == null || _stopping))
            return;
        try
        {
            if (dimmed && !_screenDimmed)
            {
                _screenDimmed = true;
                try { Session?.Control?.SetDisplayPower(true); } catch { }
                var applied = await ScreenDimmer.DimAsync(Device, _resolvedDevice.Serial);
                if (applied)
                    Log?.Invoke(L("log.screen_dimmed"));
            }
            else if (!dimmed && _screenDimmed)
            {
                _screenDimmed = false;
                await ScreenDimmer.RestoreAsync(Device, _resolvedDevice.Serial);
                Log?.Invoke(L("log.screen_restored"));
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke(string.Format(L("log.brightness_fail"), ex.Message));
        }
    }

    protected volatile bool _audioMuted;
    public bool AudioMuted => _audioMuted;
    protected volatile float _audioVolume = 1f;
    public float AudioVolume => _audioVolume;

    public virtual void SetAudioMuted(bool muted)
    {
        _audioMuted = muted;
        try { if (Audio != null) Audio.Volume = muted ? 0f : _audioVolume; } catch { }
    }

    public virtual void SetAudioVolume(float volume)
    {
        _audioVolume = Math.Clamp(volume, 0f, 1f);
        try { if (Audio != null && !_audioMuted) Audio.Volume = _audioVolume; } catch { }
    }

    private volatile bool _videoHidden;
    private int _videoBitRate = 8_000_000;

    public int CurrentBitRate => _currentBitRate;

    public double StreamLagMs => _mLagEma;
    public double StreamJitterMs => _mJitterEma;
    public bool AdaptiveBitrate { get => _adaptiveBitrate; set => _adaptiveBitrate = value; }

    private long _mBasePts = -1, _mBaseArrival, _mLastPts, _mLastArrival;
    private double _mLagEma, _mJitterEma;
    private long _videoFrames;
    public long VideoFrames => Interlocked.Read(ref _videoFrames);

    private const int AdaptMinBitRate = 1_500_000;
    private int _currentBitRate = 8_000_000;
    private bool _adaptiveBitrate;
    private readonly Stopwatch _adaptWatch = new();
    private double _adaptLagRef;
    private int _adaptGoodStreak;

    private void TrackStreamMetrics(VideoPacket p)
    {
        if (p.IsConfig || p.Pts <= 0)
            return;
        Interlocked.Increment(ref _videoFrames);
        var now = Environment.TickCount64;
        var ptsMs = p.Pts / 1000;
        if (_mBasePts < 0 || ptsMs < _mLastPts - 500)
        {
            _mBasePts = _mLastPts = ptsMs;
            _mBaseArrival = _mLastArrival = now;
            _mLagEma = _mJitterEma = _adaptLagRef = 0;
            return;
        }
        var lag = (double)(now - _mBaseArrival) - (ptsMs - _mBasePts);
        if (Math.Abs(lag) > 30_000)
        {
            _mBasePts = _mLastPts = ptsMs;
            _mBaseArrival = _mLastArrival = now;
            _mLagEma = _mJitterEma = _adaptLagRef = 0;
            return;
        }
        _mLagEma = _mLagEma == 0 ? lag : _mLagEma * 0.92 + lag * 0.08;
        var dt = (now - _mLastArrival) - (double)(ptsMs - _mLastPts);
        _mJitterEma = _mJitterEma * 0.9 + Math.Abs(dt) * 0.1;
        _mLastPts = ptsMs;
        _mLastArrival = now;
        AdaptTick();
    }

    private void AdaptTick()
    {
        if (!_adaptiveBitrate || _videoHidden || _adaptWatch.ElapsedMilliseconds < 1500)
            return;
        _adaptWatch.Restart();
        var growth = _mLagEma - _adaptLagRef;
        _adaptLagRef = _mLagEma;
        if (growth > 60 || _mLagEma > 400)
        {
            var nb = Math.Max(AdaptMinBitRate, (int)(_currentBitRate * 0.7));
            if (nb >= _currentBitRate)
                return;
            _currentBitRate = nb;
            _adaptGoodStreak = 0;
            try { Session?.Control?.SetVideoParams(nb, suspend: false); } catch { }
            RaiseLog(string.Format(L("log.bitrate_down"), Math.Round(nb / 1e6, 1)));
        }
        else if (_mLagEma < 150)
        {
            if (_currentBitRate < _videoBitRate && ++_adaptGoodStreak >= 5)
            {
                _adaptGoodStreak = 0;
                _currentBitRate = Math.Min(_videoBitRate, (int)(_currentBitRate * 1.25));
                try { Session?.Control?.SetVideoParams(_currentBitRate, suspend: false); } catch { }
                RaiseLog(string.Format(L("log.bitrate_up"), Math.Round(_currentBitRate / 1e6, 1)));
            }
        }
        else
            _adaptGoodStreak = 0;
    }

    private const int ThrottleBitRate = 500_000;
    private CancellationTokenSource? _suspendCts;
    private bool _encoderSuspended;

    public virtual void SetVideoHidden(bool hidden)
    {
        if (_videoHidden == hidden)
            return;
        _videoHidden = hidden;
        if (hidden)
        {
            var cts = new CancellationTokenSource();
            Interlocked.Exchange(ref _suspendCts, cts)?.Cancel();
            _ = Task.Delay(1500, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled)
                    return;
                try { View.Dispatcher.Invoke(ApplySuspended); } catch { }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return;
        }
        Interlocked.Exchange(ref _suspendCts, null)?.Cancel();
        if (!_encoderSuspended)
            return;
        _encoderSuspended = false;
        _currentBitRate = _videoBitRate;
        try { Session?.Control?.SetVideoParams(_videoBitRate, suspend: false); } catch { }
        RaiseLog(L("log.unthrottled"));
        lock (_decoderLock)
        {
            Decoder?.Dispose();
            Decoder = null;
            _decoderBornAt = Environment.TickCount64;
            _presenter?.Dispose();
            _presenter = null;
        }
        try { Session?.Control?.SendSimple(ControlMsgType.ResetVideo); } catch { }
    }

    private void ApplySuspended()
    {
        if (!_videoHidden || _stopping || _disposed || _recorder != null)
            return;
        try { Session?.Control?.SetVideoParams(ThrottleBitRate, suspend: true); } catch { }
        _encoderSuspended = true;
        RaiseLog(L("log.throttled"));
    }

    public virtual string ToggleRecording(string videoCodec)
    {
        if (IsRecording)
        {
            StopRecordingInternal();
            IsRecording = false;
            return string.Format(L("rec.done"), _recordPath);
        }

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "TouchMirror");
        Directory.CreateDirectory(dir);
        var model = SafeFileName(Device.Model ?? "device");
        var stamp = $"rec_{(model.Length == 0 ? "device" : model)}_{DateTime.Now:yyyyMMdd_HHmmss}_{Math.Abs(IdentityKey.GetHashCode()) % 1000:D3}";
        _recordPath = Path.Combine(dir, stamp + ".mp4");
        var rec = new Mp4Recorder(_recordPath, Session?.VideoWidth ?? 0, Session?.VideoHeight ?? 0,
            videoCodec);
        lock (_recorderLock)
            _recorder = rec;
        IsRecording = true;
        RecordingSince = DateTime.Now;
        if (_videoHidden)
        {
            try { Session?.Control?.SetVideoParams(_videoBitRate, suspend: false); } catch { }
            _encoderSuspended = false;
        }
        try { Session?.Control?.SendSimple(ControlMsgType.ResetVideo); } catch { }
        return string.Format(L("rec.started"), _recordPath);
    }

    internal static string SafeFileName(string name)
    {
        var s = new string(name
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' ? c : '_')
            .ToArray()).Trim();
        return s.Length == 0 ? "device" : s;
    }

    private void StopRecordingInternal()
    {
        RecordingSince = null;
        lock (_recorderLock)
        {
            if (_recorder is { HeaderWritten: false })
                RaiseLog("enregistrement vide — config codec jamais reçue");
            try { _recorder?.Dispose(); } catch { }
            _recorder = null;
        }
        if (_videoHidden)
        {
            try { Session?.Control?.SetVideoParams(ThrottleBitRate, suspend: true); } catch { }
            _encoderSuspended = true;
        }
    }

    public virtual Task DisconnectAsync()
    {
        _stopping = true;
        _lifetime.Cancel();
        return _disconnectTask ??= DisconnectCoreAsync();
    }

    private async Task DisconnectCoreAsync()
    {
        await _sessionGate.WaitAsync();
        try
        {
            if (_screenDimmed)
                await SetScreenDimmedAsync(false);
            await ClearSessionAsync();
            IsReconnecting = false;
            ReconnectStatus = null;
            Disconnected?.Invoke(this);
        }
        finally { _sessionGate.Release(); }
    }

    private async Task ClearSessionAsync()
    {
        _watchdog?.Stop();
        Interlocked.Exchange(ref _suspendCts, null)?.Cancel();
        _encoderSuspended = false;
        var session = Session;
        Session = null;
        IsConnected = false;
        if (ConnectedSince.HasValue)
            _sessionDuration = DateTime.Now - ConnectedSince.Value;
        if (session != null)
            _rxBytesFinal = session.RxBytes;
        ConnectedSince = null;
        CodecBadge = null;
        View.Dispatcher.Invoke(View.Detach);
        if (session != null)
            await session.DisposeAsync();
        lock (_decoderLock)
        {
            Decoder?.Dispose();
            Decoder = null;
            _presenter?.Dispose();
            _presenter = null;
        }
        lock (_audioLock)
        {
            Audio?.Dispose();
            Audio = null;
        }
        StopRecordingInternal();
        IsRecording = false;
    }

    private async Task<string> WaitForDeviceAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var state = "unknown";
        try
        {
            do
            {
                state = await AdbService.GetDeviceStateAsync(_resolvedDevice.Serial, timeout.Token);
                if (state == "missing")
                {
                    var resolved = await AdbService.ResolveAsync(Device, timeout.Token);
                    if (resolved != null)
                    {
                        _resolvedDevice = resolved;
                        state = resolved.State;
                    }
                }
                if (state is "device" or "unauthorized")
                    return state;
                await Task.Delay(500, timeout.Token);
            } while (true);
        }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested) { return state; }
    }

    private async Task OnSessionLostAsync(EngineSession session)
    {
        await _sessionGate.WaitAsync();
        var exhausted = false;
        try
        {
            if (!ReferenceEquals(Session, session) || _stopping || ManualDisconnect || _disposed || _lastOptions == null)
                return;
            if (_connectedAt != 0 && Environment.TickCount64 - _connectedAt > 30_000)
                _reconnectAttempts = 0;
            IsReconnecting = true;
            if (IsRecording)
                RaiseLog(L("log.recording_interrupted"));
            await ClearSessionAsync();
            while (_reconnectAttempts < 3 && !_stopping && !ManualDisconnect)
            {
                ReconnectStatus = string.Format(L("log.session_retry"), ++_reconnectAttempts);
                RaiseLog(ReconnectStatus);
                try
                {
                    await Task.Delay(800 * _reconnectAttempts, _lifetime.Token);
                    LastDeviceState = await WaitForDeviceAsync();
                    RaiseLog($"reconnect: adb state={LastDeviceState}");
                    if (LastDeviceState == "unauthorized")
                        break;
                    if (LastDeviceState != "device")
                        continue;
                    await StartSessionAsync(_lastOptions);
                    return;
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    RaiseLog($"reconnect: {ex.Message}");
                    await ClearSessionAsync();
                }
            }
            exhausted = !_stopping && !ManualDisconnect;
            UnexpectedDeath = exhausted;
        }
        finally
        {
            IsReconnecting = false;
            ReconnectStatus = null;
            _sessionGate.Release();
        }
        if (exhausted)
            await DisconnectAsync();
    }

    public async Task HandleFileDropAsync(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            try
            {
                if (path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                {
                    RaiseLog($"drop: install {name}…");
                    var output = await AdbService.InstallApkAsync(_resolvedDevice.Serial, path);
                    var tail = output.Trim().Split('\n').LastOrDefault()?.Trim();
                    RaiseLog($"drop: {name} — {(string.IsNullOrEmpty(tail) ? "installé" : tail)}");
                }
                else
                {
                    var remote = $"/sdcard/Download/{name}";
                    RaiseLog($"drop: push {name}…");
                    await AdbService.PushAsync(_resolvedDevice.Serial, path, remote);
                    try { Session?.Control?.ScanFile(remote); } catch { }
                    RaiseLog($"drop: {name} → {remote}");
                }
            }
            catch (Exception ex)
            {
                RaiseLog($"drop: {name} — {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        AppLogger.Forget(DisconnectAsync());
    }
}
