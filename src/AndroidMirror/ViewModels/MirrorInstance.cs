using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
                    }
                    Decoder.Feed(packet.Data);
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

    public virtual void SetAudioMuted(bool muted)
    {
        try { if (Audio != null) Audio.Volume = muted ? 0f : 1f; } catch { }
    }

    private volatile bool _videoHidden;
    private int _videoBitRate = 8_000_000;

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
        View.Dispatcher.Invoke(View.Detach);
        Disconnected?.Invoke(this);
    }

    public void Dispose() => _ = DisconnectAsync();
}