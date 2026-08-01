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
    public AdbDevice Device { get; }
    public MirrorView View { get; } = new();

    public ScrcpySession? Session { get; private set; }
    public VideoDecoder? Decoder { get; private set; }
    public AudioPlayer? Audio { get; private set; }

    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private int _slot;

    /// <summary>Vrai quand la déconnexion vient d'un geste utilisateur (pas d'une coupure session).</summary>
    public bool ManualDisconnect { get; set; }

    private FileStream? _recordStream;
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

    public MirrorInstance(AdbDevice device)
    {
        Device = device;
        DeviceName = device.DisplayName;
    }

    public async Task StartAsync(ScrcpyOptions options)
    {
        var session = new ScrcpySession(Device, options);
        Session = session;

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
            Application.Current.Dispatcher.Invoke(() => _ = DisconnectAsync());

        session.VideoPacketReceived += packet =>
        {
            lock (_decoderLock)
            {
                if (Decoder == null)
                {
                    Decoder = new VideoDecoder(session.VideoCodecId ?? "h264");
                    Decoder.Error += m => Log?.Invoke($"decoder: {m}");
                    View.Dispatcher.Invoke(() => View.AttachDecoder(Decoder));
                }
                Decoder.Feed(packet.Data);
            }
            try
            {
                if (_recorder != null)
                {
                    if (packet.IsConfig) _recorder.WriteConfig(packet.Data);
                    else _recorder.WritePacket(packet.Data, packet.Pts, packet.IsKeyFrame);
                }
                else
                {
                    _recordStream?.Write(packet.Data);
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
                }
                Audio.Feed(packet.Data, packet.IsConfig);
            }
        };

        await session.StartAsync();

        IsConnected = true;
        DeviceName = Device.CustomName ?? session.DeviceName ?? Device.DisplayName;
        View.Dispatcher.Invoke(() => View.AttachControl(session.Control!));
        Connected?.Invoke(this);

        if (options.TurnScreenOff)
            _ = SetScreenDimmedAsync(true);
    }

    public async Task SetScreenDimmedAsync(bool dimmed)
    {
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
                Log?.Invoke("écran du téléphone atténué (miroir actif)");
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
                Log?.Invoke("écran du téléphone restauré");
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"luminosité: {ex.Message}");
        }
    }

    public void SetAudioMuted(bool muted)
    {
        try { if (Audio != null) Audio.Volume = muted ? 0f : 1f; } catch { }
    }

    public string ToggleRecording(string videoCodec)
    {
        if (IsRecording)
        {
            StopRecordingInternal();
            IsRecording = false;
            return $"Enregistrement terminé → {_recordPath}";
        }

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "TouchMirror");
        Directory.CreateDirectory(dir);
        var stamp = $"rec_{Device.Model}_{DateTime.Now:yyyyMMdd_HHmmss}";
        if (videoCodec == "h264")
        {
            _recordPath = Path.Combine(dir, stamp + ".mp4");
            _recorder = new Mp4Recorder(_recordPath);
        }
        else
        {
            var ext = videoCodec == "h265" ? "h265" : "av1";
            _recordPath = Path.Combine(dir, $"{stamp}.{ext}");
            _recordStream = new FileStream(_recordPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20);
        }
        IsRecording = true;
        RecordingSince = DateTime.Now;
        return $"Enregistrement → {_recordPath}";
    }

    private void StopRecordingInternal()
    {
        RecordingSince = null;
        try { _recorder?.Dispose(); } catch { }
        _recorder = null;
        try { _recordStream?.Flush(); _recordStream?.Dispose(); } catch { }
        _recordStream = null;
    }

    public async Task DisconnectAsync()
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
        View.Dispatcher.Invoke(View.Detach);
        Disconnected?.Invoke(this);
    }

    public void Dispose() => _ = DisconnectAsync();
}