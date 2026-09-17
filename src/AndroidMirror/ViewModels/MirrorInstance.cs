using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using TouchMirror.Scrcpy;
using TouchMirror.Services;
using TouchMirror.Video;
using TouchMirror.Views;

namespace TouchMirror.ViewModels;

public partial class MirrorInstance : ObservableObject, IDisposable
{
    protected static string L(string key) => LocalizationService.Get(key);
    public AdbDevice Device { get; }
    public MirrorView View { get; } = new();

    public ScrcpySession? Session { get; private set; }
    public VideoDecoder? Decoder { get; private set; }
    public AudioPlayer? Audio { get; private set; }

    /// <summary>Vrai pour les miroirs iOS/AirPlay (affichage seul).</summary>
    public virtual bool IsIos => false;

    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private int _slot;
    /// <summary>Couleur d'accent hex de l'appareil (nulle = accent par défaut).</summary>
    [ObservableProperty] private string? _accentHex;
    /// <summary>Codec vidéo réellement négocié + « · GPU » quand le décodage matériel est actif.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CodecBadgeVisibility))]
    private string? _codecBadge;
    public Visibility CodecBadgeVisibility =>
        string.IsNullOrEmpty(CodecBadge) ? Visibility.Collapsed : Visibility.Visible;
    private bool _codecHwSeen;

    /// <summary>Réglages propres de l'appareil dans l'espace de travail actif (nul = globaux).</summary>
    public WorkspaceDevice? Prefs { get; set; }

    /// <summary>Profil Android secondaire affiché sur écran virtuel (nul = utilisateur principal).</summary>
    public int? AccountUserId { get; set; }
    /// <summary>Nom du profil secondaire affiché dans la tuile.</summary>
    public string? AccountName { get; set; }
    /// <summary>Clé d'identité du miroir : DeviceKey, ou DeviceKey#userId pour un profil secondaire.</summary>
    public string IdentityKey => AccountUserId is { } id ? $"{Device.DeviceKey}#{id}" : Device.DeviceKey;

    /// <summary>Vrai quand la déconnexion vient d'un geste utilisateur (pas d'une coupure session).</summary>
    public bool ManualDisconnect { get; set; }

    /// <summary>Raccourcis clavier plaqués sur la vidéo (touche → tap, contrôle manuel).</summary>
    public ObservableCollection<KeybindItem> Keybinds { get; } = new();
    /// <summary>Mode édition des raccourcis (placement / assignation sur la vidéo).</summary>
    [ObservableProperty] private bool _keybindEditMode;
    /// <summary>Style des raccourcis : 0 = pastille, 1 = cercle, 2 = minimal.</summary>
    [ObservableProperty] private int _keybindStyle;
    /// <summary>Opacité des raccourcis (0.3–1).</summary>
    [ObservableProperty] private double _keybindOpacity = 0.92;
    /// <summary>Taille des raccourcis en px (22–44).</summary>
    [ObservableProperty] private double _keybindSize = 30;
    /// <summary>Levée quand les raccourcis changent — à persister.</summary>
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
    /// <summary>Levée quand la vue demande à quitter le mode édition (Échap).</summary>
    public event Action? EditModeExitRequested;

    /// <summary>Clic sur une ligne d'un widget overlay — (miroir, id widget, index).</summary>
    public event Action<MirrorInstance, string, int>? OverlayLineClicked;

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
    private string? _recordPath;
    public DateTime? RecordingSince { get; private set; }
    private readonly object _decoderLock = new();
    private bool _screenDimmed;
    private int _savedBrightness = -1;
    private int _savedStayOn = -1;

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
        DeviceName = device.DisplayName;
        View.BindKeybinds(Keybinds);
        View.EditModeExitRequested += () => EditModeExitRequested?.Invoke();
        View.OverlayLineClicked += (id, idx) => OverlayLineClicked?.Invoke(this, id, idx);
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

    public async Task StartAsync(ScrcpyOptions options)
    {
        var session = new ScrcpySession(Device, options);
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
            View.Dispatcher.Invoke(() => View.OnVideoSize(w, h));
        session.DeviceClipboard += text =>
        {
            if (ShouldSyncClipboard?.Invoke() == false)
                return;
            try { Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text)); } catch { }
        };
        session.Disconnected += () =>
        {
            try { Application.Current.Dispatcher.Invoke(() => _ = DisconnectAsync()); }
            catch (InvalidOperationException) { } // dispatcher arrêté (fermeture de l'app)
        };

        session.VideoPacketReceived += packet =>
        {
            TrackStreamMetrics(packet);
            // Miroir inactif : on saute le décodage (CPU/GPU économisés,
            // l'enregistrement écrit les paquets bruts et continue).
            if (!_videoHidden)
            {
                lock (_decoderLock)
                {
                    if (Decoder == null)
                    {
                        GpuPresenter? presenter = null;
                        if (options.VideoDecoder == "gpu")
                        {
                            try { presenter = new GpuPresenter(); }
                            catch (Exception ex) { Log?.Invoke($"gpu presenter: {ex.Message}"); }
                        }
                        Decoder = new VideoDecoder(session.VideoCodecId ?? "h264",
                            preferHardware: options.VideoDecoder != "cpu",
                            gpuPresenter: presenter);
                        if (presenter != null)
                        {
                            Decoder.GpuFrame += presenter.Present;
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
                        View.Dispatcher.Invoke(() => View.AttachDecoder(Decoder, p));
                        _codecHwSeen = false;
                        CodecBadge = (session.VideoCodecId ?? "h264").ToUpperInvariant();
                    }
                    Decoder.Feed(packet.Data);
                    if (!_codecHwSeen && Decoder.HardwareDecoding)
                    {
                        _codecHwSeen = true;
                        CodecBadge += " · GPU";
                    }
                }
            }
            try
            {
                if (_recorder != null)
                {
                    if (packet.IsConfig) _recorder.WriteConfig(packet.Data);
                    else _recorder.WritePacket(packet.Data, packet.Pts, packet.IsKeyFrame);
                }
            }
            catch { }
        };
        session.AudioPacketReceived += packet =>
        {
            lock (_decoderLock)
            {
                if (Audio == null)
                {
                    Audio = new AudioPlayer(session.AudioCodecId ?? "opus");
                    Audio.Error += m => Log?.Invoke($"audio: {m}");
                    Audio.Volume = _audioMuted ? 0f : 1f;
                }
                Audio.Feed(packet.Data, packet.IsConfig);
            }
        };

        await session.StartAsync();

        IsConnected = true;
        DeviceName = AccountName != null
            ? $"{Device.CustomName ?? session.DeviceName ?? Device.ShortName} · {AccountName}"
            : Device.CustomName ?? session.DeviceName ?? Device.DisplayName;
        View.Dispatcher.Invoke(() => View.AttachControl(session.Control!));
        Connected?.Invoke(this);

        if (options.TurnScreenOff)
            _ = SetScreenDimmedAsync(true);
    }

    public async Task SetScreenDimmedAsync(bool dimmed)
    {
        if (Session == null)
            return; // iOS : pas de session scrcpy, rien à atténuer
        try
        {
            if (dimmed && !_screenDimmed)
            {
                _screenDimmed = true;
                _savedBrightness = await AdbService.GetBrightnessAsync(Device.Serial);
                _savedStayOn = await AdbService.GetStayOnWhilePluggedInAsync(Device.Serial);
                // L'écran doit rester logiquement ON pour que le compositor produise des frames ;
                // luminosité 0 rend le panneau AMOLED visuellement noir sans couper le flux.
                await AdbService.SetStayOnWhilePluggedInAsync(Device.Serial, 7);
                try { Session?.Control?.SetDisplayPower(true); } catch { }
                await AdbService.WakeScreenAsync(Device.Serial);
                await AdbService.SetBrightnessAsync(Device.Serial, 0);
                Log?.Invoke(L("log.screen_dimmed"));
            }
            else if (!dimmed && _screenDimmed)
            {
                _screenDimmed = false;
                if (_savedStayOn >= 0)
                    await AdbService.SetStayOnWhilePluggedInAsync(Device.Serial, _savedStayOn);
                if (_savedBrightness >= 0)
                    await AdbService.SetBrightnessAsync(Device.Serial, _savedBrightness);
                _savedBrightness = -1;
                _savedStayOn = -1;
                Log?.Invoke(L("log.screen_restored"));
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke(string.Format(L("log.brightness_fail"), ex.Message));
        }
    }

    /// <summary>Vrai quand la sortie audio locale est coupée — conservé même
    /// avant la création du lecteur (mute demandé avant le 1er paquet audio).</summary>
    protected volatile bool _audioMuted;
    public bool AudioMuted => _audioMuted;

    public virtual void SetAudioMuted(bool muted)
    {
        _audioMuted = muted;
        try { if (Audio != null) Audio.Volume = muted ? 0f : 1f; } catch { }
    }

    private volatile bool _videoHidden;
    private int _videoBitRate = 8_000_000;

    /// <summary>Débit courant demandé à l'encodeur (réduit par le mode adaptatif).</summary>
    public int CurrentBitRate => _currentBitRate;

    /// <summary>Retard de lecture vs temps réel, ms (dérive arrivée − pts).</summary>
    public double StreamLagMs => _mLagEma;
    /// <summary>Gigue des paquets vs cadence encodeur, ms.</summary>
    public double StreamJitterMs => _mJitterEma;
    /// <summary>Débit adaptatif : réduit à chaud quand le retard de lecture croît.</summary>
    public bool AdaptiveBitrate { get => _adaptiveBitrate; set => _adaptiveBitrate = value; }

    private long _mBasePts = -1, _mBaseArrival, _mLastPts, _mLastArrival;
    private double _mLagEma, _mJitterEma;

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
        var now = Environment.TickCount64;
        var ptsMs = p.Pts / 1000;
        if (_mBasePts < 0)
        {
            _mBasePts = _mLastPts = ptsMs;
            _mBaseArrival = _mLastArrival = now;
            return;
        }
        var lag = (double)(now - _mBaseArrival) - (ptsMs - _mBasePts);
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

    /// <summary>Débit plancher d'une tuile en miniature (encodeur quasi au repos).</summary>
    private const int ThrottleBitRate = 500_000;

    /// <summary>
    /// Met le décodage vidéo en pause tant que le miroir est en miniature, et
    /// suspend l'encodeur côté téléphone (débit plancher) pour économiser
    /// batterie, CPU et bande passante. À la reprise : débit restauré +
    /// dé-suspension + ResetVideo — le codec est recréé, config et keyframe
    /// renvoyées, l'image repart nette sans artefacts.
    /// Un enregistrement en cours consomme les paquets bruts : pas de throttle.
    /// </summary>
    public virtual void SetVideoHidden(bool hidden)
    {
        if (_videoHidden == hidden)
            return;
        _videoHidden = hidden;
        if (hidden)
        {
            if (_recorder == null)
            {
                try { Session?.Control?.SetVideoParams(ThrottleBitRate, suspend: true); } catch { }
                RaiseLog(L("log.throttled"));
            }
            return;
        }
        _currentBitRate = _videoBitRate;
        try { Session?.Control?.SetVideoParams(_videoBitRate, suspend: false); } catch { }
        RaiseLog(L("log.unthrottled"));
        lock (_decoderLock)
        {
            Decoder?.Dispose();
            Decoder = null;
        }
        try { Session?.Control?.SendSimple(ControlMsgType.ResetVideo); } catch { }
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
        var stamp = $"rec_{Device.Model}_{DateTime.Now:yyyyMMdd_HHmmss}_{Math.Abs(IdentityKey.GetHashCode()) % 1000:D3}";
        _recordPath = Path.Combine(dir, stamp + ".mp4");
        _recorder = new Mp4Recorder(_recordPath, Session?.VideoWidth ?? 0, Session?.VideoHeight ?? 0,
            videoCodec);
        IsRecording = true;
        RecordingSince = DateTime.Now;
        try { Session?.Control?.SendSimple(ControlMsgType.ResetVideo); } catch { }
        return string.Format(L("rec.started"), _recordPath);
    }

    private void StopRecordingInternal()
    {
        RecordingSince = null;
        if (_recorder is { HeaderWritten: false })
            RaiseLog("enregistrement vide — config codec jamais reçue");
        try { _recorder?.Dispose(); } catch { }
        _recorder = null;
    }

    public virtual async Task DisconnectAsync()
    {
        if (_screenDimmed)
            await SetScreenDimmedAsync(false);
        var session = Session;
        Session = null;
        if (session != null)
            await session.DisposeAsync();
        Decoder?.Dispose();
        Decoder = null;
        Audio?.Dispose();
        Audio = null;
        StopRecordingInternal();
        IsRecording = false;
        IsConnected = false;
        CodecBadge = null;
        View.Dispatcher.Invoke(View.Detach);
        Disconnected?.Invoke(this);
    }

    public void Dispose() => _ = DisconnectAsync();
}