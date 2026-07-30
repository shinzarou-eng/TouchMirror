using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TouchMirror.Scrcpy;
using TouchMirror.Services;

namespace TouchMirror.ViewModels;

public sealed record SettingOption(string Label, int Value);
public sealed record CodecOption(string Label, string Value);

public partial class MainViewModel : ObservableObject
{
    public SettingOption[] MaxSizeOptions { get; } =
        { new("720p", 720), new("1080p", 1080), new("1440p", 1440), new("2160p", 2160), new("Natif (max)", 0) };
    public SettingOption[] FpsOptions { get; } =
        { new("30 fps", 30), new("60 fps", 60), new("90 fps", 90), new("120 fps", 120) };
    public SettingOption[] BitRateOptions { get; } =
        { new("4 Mbps", 4_000_000), new("8 Mbps", 8_000_000), new("16 Mbps", 16_000_000),
          new("24 Mbps", 24_000_000), new("40 Mbps", 40_000_000) };
    public CodecOption[] CodecOptions { get; } =
        { new("H.264 (compatible)", "h264"), new("H.265 (qualité+)", "h265"), new("AV1 (expérimental)", "av1") };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(StatusDotColor))]
    private ObservableCollection<MirrorInstance> _mirrors = new();

    public ObservableCollection<MirrorInstance> InactiveMirrors { get; } = new();

    public ObservableCollection<PluginInstance> Plugins { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveMirrorName))]
    private MirrorInstance? _activeMirror;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetectedDeviceCount))]
    private ObservableCollection<AdbDevice> _devices = new();

    public int DetectedDeviceCount => Devices.Count(d => !d.IsRememberedOnly);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    private AdbDevice? _selectedDevice;
    [ObservableProperty] private string _status = "Sélectionne un appareil et connecte-toi";
    [ObservableProperty] private string _adbStatus = "";
    [ObservableProperty] private string _wifiStatus = "";
    [ObservableProperty] private bool _isBusy;

    public bool IsConnected => Mirrors.Count > 0;
    public bool IsDisconnected => !IsConnected;
    public bool CanConnect => !IsBusy && SelectedDevice is { IsReady: true };
    public string ActiveMirrorName => ActiveMirror?.DeviceName ?? "";

    public Visibility RecordingVisibility =>
        ActiveMirror?.IsRecording == true ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty] private string _recordingElapsed = "REC";

    private readonly DispatcherTimer _recTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _refreshing;
    private readonly AppSettings _settings;
    private bool _suppressSave;
    private readonly LocalApiHost _apiHost;
    private LocalApiServer? _apiServer;
    private bool _apiBusy;

    public MainViewModel()
    {
        _recTimer.Tick += (_, _) =>
        {
            var since = ActiveMirror?.RecordingSince;
            RecordingElapsed = since.HasValue ? $"REC {(DateTime.Now - since.Value):m\\:ss}" : "REC";
        };
        _recTimer.Start();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _pollTimer.Tick += async (_, _) =>
        {
            if (!IsBusy && !_refreshing)
                await RefreshDevicesAsync();
        };

        _settings = SettingsStore.Load();
        // migration : anciens plugins .ps1 → id sans extension
        _settings.EnabledPlugins = _settings.EnabledPlugins
            .Select(e => e.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(e) : e).ToList();
        _apiHost = new LocalApiHost(this);
        _apiHost.PluginEvent += json =>
        {
            foreach (var p in Plugins)
                if (p.Running)
                    p.DispatchEvent(json);
        };
        _suppressReconnect = true;
        _suppressSave = true;
        MaxSize = _settings.MaxSize;
        MaxFps = _settings.MaxFps;
        VideoBitRate = _settings.VideoBitRate;
        VideoCodec = _settings.VideoCodec;
        StayAwake = _settings.StayAwake;
        EnableAudio = _settings.EnableAudio;
        AutoLaunchDofus = _settings.AutoLaunchDofus;
        AutoFullscreen = _settings.AutoFullscreen;
        SyncDeviceClipboard = _settings.SyncDeviceClipboard;
        Topmost = _settings.Topmost;
        TurnScreenOff = _settings.TurnScreenOff;
        ShowSettings = _settings.ShowSettings;
        LocalApiPort = _settings.LocalApiPort;
        LocalApiToken = _settings.LocalApiToken ?? "";
        LocalApiEnabled = _settings.LocalApiEnabled;
        _suppressSave = false;
        _suppressReconnect = false;
    }

    private void ScheduleSave()
    {
        if (_suppressSave) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        _settings.MaxSize = MaxSize;
        _settings.MaxFps = MaxFps;
        _settings.VideoBitRate = VideoBitRate;
        _settings.VideoCodec = VideoCodec;
        _settings.StayAwake = StayAwake;
        _settings.EnableAudio = EnableAudio;
        _settings.AutoLaunchDofus = AutoLaunchDofus;
        _settings.AutoFullscreen = AutoFullscreen;
        _settings.SyncDeviceClipboard = SyncDeviceClipboard;
        _settings.Topmost = Topmost;
        _settings.TurnScreenOff = TurnScreenOff;
        _settings.ShowSettings = ShowSettings;
        _settings.LocalApiEnabled = LocalApiEnabled;
        _settings.LocalApiPort = LocalApiPort;
        _settings.LocalApiToken = string.IsNullOrEmpty(LocalApiToken) ? null : LocalApiToken;
        _settings.LastSelectedDeviceKey = SelectedDevice?.DeviceKey;
        _settings.EnabledPlugins = Plugins.Where(p => p.Running).Select(p => p.Name).ToList();
        SettingsStore.Save(_settings);
        if (LocalApiEnabled && _apiServer is { Port: { } p } && p != LocalApiPort)
            _ = RestartApiAsync();
    }

    [ObservableProperty] private int _maxSize = 0;
    [ObservableProperty] private int _maxFps = 60;
    [ObservableProperty] private int _videoBitRate = 16_000_000;
    [ObservableProperty] private string _videoCodec = "h264";
    [ObservableProperty] private bool _stayAwake;
    [ObservableProperty] private bool _enableAudio = true;
    [ObservableProperty] private bool _autoLaunchDofus;
    [ObservableProperty] private bool _autoFullscreen;
    [ObservableProperty] private bool _syncDeviceClipboard = true;
    [ObservableProperty] private bool _topmost;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TurnScreenOffText))]
    private bool _turnScreenOff;
    public string TurnScreenOffText => TurnScreenOff ? "Activé" : "Désactivé";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsVisibility))]
    private bool _showSettings;
    public Visibility SettingsVisibility => ShowSettings ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateBannerVisibility))]
    private string? _updateVersion;
    public string UpdateUrl { get; private set; } = "";
    public Visibility UpdateBannerVisibility =>
        UpdateVersion != null ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty] private ObservableCollection<string> _logs = new();

    private bool _hasError;
    public System.Windows.Media.Brush StatusDotColor => _hasError
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36))
        : IsConnected ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Gray;

    public event Action<MirrorInstance>? MirrorAdded;
    public event Func<MirrorInstance, string?>? ScreenshotRequested;
    public event Action? AnyConnected;

    public void SetActive(MirrorInstance instance)
    {
        InactiveMirrors.Remove(instance);
        ActiveMirror = instance;
        foreach (var m in Mirrors)
        {
            m.IsActive = m == instance;
            m.SetAudioMuted(m != instance);
        }
        RefreshInactiveMirrors();
        UpdateStatus();
        _apiHost.Publish("mirror.active", new { slot = instance.Slot, name = instance.DeviceName });
    }

    public void ActivateAdjacent(int delta)
    {
        if (Mirrors.Count < 2 || ActiveMirror == null)
            return;
        var i = Mirrors.IndexOf(ActiveMirror);
        if (i < 0)
            return;
        SetActive(Mirrors[(i + delta + Mirrors.Count) % Mirrors.Count]);
    }

    public void ActivateAt(int index)
    {
        if (index >= 0 && index < Mirrors.Count && !ReferenceEquals(Mirrors[index], ActiveMirror))
            SetActive(Mirrors[index]);
    }

    // API locale — control-plane uniquement : rien ici ne doit atteindre
    // Session.Control (tactile, clavier, clipboard). Voir LocalApiHost.

    [ObservableProperty] private bool _localApiEnabled;
    [ObservableProperty] private int _localApiPort = 47613;
    [ObservableProperty] private string _localApiToken = "";

    public MirrorInstance? MirrorAtSlot(int slot)
        => slot >= 1 && slot <= Mirrors.Count ? Mirrors[slot - 1] : null;

    public bool TryActivateSlot(int slot)
    {
        var m = MirrorAtSlot(slot);
        if (m == null)
            return false;
        SetActive(m);
        return true;
    }

    public string? ToggleRecordingFor(MirrorInstance? m)
    {
        if (m == null)
            return null;
        Status = m.ToggleRecording(VideoCodec);
        OnPropertyChanged(nameof(RecordingVisibility));
        RecordingElapsed = m.RecordingSince.HasValue
            ? $"REC {(DateTime.Now - m.RecordingSince.Value):m\\:ss}" : "REC";
        _apiHost.Publish("mirror.recording", new { slot = m.Slot, recording = m.IsRecording });
        return Status;
    }

    public string? RequestScreenshot(MirrorInstance m) => ScreenshotRequested?.Invoke(m);

    public async Task<AdbDevice?> FindDeviceBySerialAsync(string serial)
    {
        var d = Devices.FirstOrDefault(x => x.MatchesSerial(serial));
        if (d == null)
        {
            await RefreshDevicesAsync();
            d = Devices.FirstOrDefault(x => x.MatchesSerial(serial));
        }
        return d;
    }

    public Task ConnectExistingDeviceAsync(AdbDevice device, bool allowAppLaunch = true)
        => ConnectDeviceAsync(device, allowAppLaunch);
    public Task DisconnectMirrorAsync(MirrorInstance m) => RemoveMirrorInternalAsync(m);

    partial void OnLocalApiEnabledChanged(bool value)
    {
        ScheduleSave();
        _ = RestartApiAsync();
    }

    partial void OnLocalApiPortChanged(int value) => ScheduleSave();

    [RelayCommand]
    private void RegenerateApiToken()
    {
        _settings.LocalApiToken = Guid.NewGuid().ToString("N");
        LocalApiToken = _settings.LocalApiToken;
        SaveNow();
        Log("Token API régénéré");
    }

    private async Task RestartApiAsync()
    {
        if (_apiBusy)
            return;
        _apiBusy = true;
        try
        {
            if (!LocalApiEnabled)
            {
                if (_apiServer != null)
                {
                    await _apiServer.DisposeAsync();
                    _apiServer = null;
                    Log("API locale arrêtée");
                }
                return;
            }
            if (string.IsNullOrEmpty(_settings.LocalApiToken))
                _settings.LocalApiToken = Guid.NewGuid().ToString("N");
            LocalApiToken = _settings.LocalApiToken;
            if (LocalApiPort is < 1024 or > 65535)
            {
                LocalApiPort = 47613;
                return;
            }
            var server = new LocalApiServer();
            try
            {
                await server.StartAsync(_apiHost, LocalApiPort, () => _settings.LocalApiToken);
                var old = _apiServer;
                _apiServer = server;
                if (old != null)
                    await old.DisposeAsync();
                Log($"API locale : http://127.0.0.1:{LocalApiPort}/api — token Bearer requis");
            }
            catch (Exception ex)
            {
                Log($"API locale impossible : {ex.Message}");
                if (_apiServer == null)
                {
                    _apiBusy = false;
                    LocalApiEnabled = false;
                }
            }
        }
        finally
        {
            _apiBusy = false;
        }
    }

    public async Task ShutdownApiAsync()
    {
        if (_apiServer != null)
            await _apiServer.DisposeAsync();
        _apiServer = null;
        foreach (var p in Plugins)
            p.Stop();
    }

    // ═══ Plugins — scripts utilisateurs du dossier plugins/ ═══

    private PluginApi ApiFor(PluginInstance p) => new(_apiHost, msg => p.Emit(msg));

    [RelayCommand]
    private void RescanPlugins()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "plugins");
        Directory.CreateDirectory(dir);
        // plugins/<id>.js ou plugins/<id>/plugin.js (+ plugin.json optionnel)
        var files = Directory.EnumerateFiles(dir, "*.js")
            .Concat(Directory.EnumerateDirectories(dir)
                .Select(d => Path.Combine(d, "plugin.js"))
                .Where(File.Exists))
            .OrderBy(f => f)
            .ToList();

        for (var i = Plugins.Count - 1; i >= 0; i--)
            if (!files.Contains(Plugins[i].FilePath))
            {
                Plugins[i].Stop();
                Plugins.RemoveAt(i);
            }
        foreach (var f in files.Where(f => Plugins.All(p => p.FilePath != f)))
        {
            var plugin = new PluginInstance(f);
            plugin.Output += line => Log($"[{plugin.Name}] {line}");
            Plugins.Add(plugin);
        }
        foreach (var p in Plugins)
        {
            p.VerifyNow();
            // Un plugin modifié en cours de route est arrêté : il devra être reconfirmé.
            if (p.Running && !p.IsVerified && !IsApproved(p))
            {
                p.Stop();
                _settings.EnabledPlugins.Remove(p.Id);
                Log($"plugin arrêté : {p.Name} — fichier modifié, reconfirmation requise");
                continue;
            }
            if (p.Running || !_settings.EnabledPlugins.Contains(p.Id))
                continue;
            // Un plugin non officiel modifié depuis sa validation ne redémarre pas seul.
            if (!p.IsVerified && !IsApproved(p))
            {
                Log($"plugin {p.Name} non démarré — contenu non vérifié, confirmation requise");
                continue;
            }
            p.Start(ApiFor(p));
        }
    }

    /// <summary>Hash déjà validé par l'utilisateur pour ce contenu exact.</summary>
    private bool IsApproved(PluginInstance p)
        => p.ContentHash != null
           && _settings.ApprovedPlugins.TryGetValue(p.Id, out var h)
           && h == p.ContentHash;

    /// <summary>Demande de confirmation avant d'activer un plugin non officiel. Posée par la vue.</summary>
    public Func<PluginInstance, Task<bool>>? ConfirmUnverified;

    [RelayCommand]
    private async Task TogglePlugin(PluginInstance? plugin)
    {
        if (plugin == null)
            return;
        if (plugin.Running)
        {
            plugin.Stop();
            _settings.EnabledPlugins.Remove(plugin.Id);
            Log($"plugin arrêté : {plugin.Name}");
        }
        else
        {
            plugin.VerifyNow();
            if (!plugin.IsVerified && !IsApproved(plugin))
            {
                if (ConfirmUnverified == null || !await ConfirmUnverified(plugin))
                    return;
                if (plugin.ContentHash != null)
                    _settings.ApprovedPlugins[plugin.Id] = plugin.ContentHash;
            }
            plugin.Start(ApiFor(plugin));
            if (plugin.Running && !_settings.EnabledPlugins.Contains(plugin.Id))
                _settings.EnabledPlugins.Add(plugin.Id);
            Log($"plugin lancé : {plugin.Name}");
        }
        ScheduleSave();
    }

    [RelayCommand]
    private void OpenPluginsFolder()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "plugins");
        Directory.CreateDirectory(dir);
        System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    [RelayCommand]
    private void MoveMirrorLeft(MirrorInstance? m) => MoveMirror(m, -1);

    [RelayCommand]
    private void MoveMirrorRight(MirrorInstance? m) => MoveMirror(m, 1);

    private void MoveMirror(MirrorInstance? m, int dir)
    {
        if (m == null)
            return;
        var i = Mirrors.IndexOf(m);
        var j = i + dir;
        if (i < 0 || j < 0 || j >= Mirrors.Count)
            return;
        Mirrors.Move(i, j);
        RefreshInactiveMirrors();
        _settings.MirrorOrder = Mirrors.Select(x => x.Device.DeviceKey).ToList();
        ScheduleSave();
    }

    /// <summary>Réordonne les tuiles selon l'ordre mémorisé (clés d'appareil).</summary>
    private void ApplyMirrorOrder()
    {
        var order = _settings.MirrorOrder;
        if (order.Count == 0)
            return;
        var sorted = Mirrors
            .OrderBy(m => order.IndexOf(m.Device.DeviceKey) is var k && k >= 0 ? k : int.MaxValue)
            .ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var cur = Mirrors.IndexOf(sorted[i]);
            if (cur > i)
                Mirrors.Move(cur, i);
        }
    }

    public void StopPlugins()
    {
        foreach (var p in Plugins)
            p.Stop();
    }

    private void RefreshInactiveMirrors()
    {
        for (var i = 0; i < Mirrors.Count; i++)
            Mirrors[i].Slot = i + 1;
        for (var i = InactiveMirrors.Count - 1; i >= 0; i--)
        {
            var m = InactiveMirrors[i];
            if (!Mirrors.Contains(m) || ReferenceEquals(m, ActiveMirror))
                InactiveMirrors.RemoveAt(i);
        }
        foreach (var m in Mirrors)
            if (!ReferenceEquals(m, ActiveMirror) && !InactiveMirrors.Contains(m))
                InactiveMirrors.Add(m);
        OnPropertyChanged(nameof(HasInactiveMirrors));
    }

    public bool HasInactiveMirrors => InactiveMirrors.Count > 0;

    private void UpdateStatus()
    {
        Status = Mirrors.Count switch
        {
            0 => "Déconnecté",
            1 => $"Connecté — {Mirrors[0].DeviceName}",
            _ => $"{Mirrors.Count} miroirs — actif : {ActiveMirror?.DeviceName}"
        };
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsDisconnected));
        OnPropertyChanged(nameof(StatusDotColor));
        OnPropertyChanged(nameof(RecordingVisibility));
    }

    private ScrcpyOptions BuildOptions() => new()
    {
        MaxSize = MaxSize,
        MaxFps = MaxFps,
        VideoBitRate = VideoBitRate,
        VideoCodec = VideoCodec,
        StayAwake = StayAwake,
        Audio = EnableAudio,
        TurnScreenOff = TurnScreenOff,
    };

    private bool _suppressReconnect;

    partial void OnMaxSizeChanged(int value) { ScheduleSave(); if (!_suppressReconnect) _ = ReconnectActiveAsync(); }
    partial void OnMaxFpsChanged(int value) { ScheduleSave(); if (!_suppressReconnect) _ = ReconnectActiveAsync(); }
    partial void OnVideoBitRateChanged(int value) { ScheduleSave(); if (!_suppressReconnect) _ = ReconnectActiveAsync(); }
    partial void OnVideoCodecChanged(string value) { ScheduleSave(); if (!_suppressReconnect) _ = ReconnectActiveAsync(); }
    partial void OnEnableAudioChanged(bool value) { ScheduleSave(); if (!_suppressReconnect) _ = ReconnectActiveAsync(); }
    partial void OnStayAwakeChanged(bool value) => ScheduleSave();
    partial void OnAutoLaunchDofusChanged(bool value) => ScheduleSave();
    partial void OnAutoFullscreenChanged(bool value) => ScheduleSave();
    partial void OnSyncDeviceClipboardChanged(bool value) => ScheduleSave();
    partial void OnTopmostChanged(bool value) => ScheduleSave();
    partial void OnShowSettingsChanged(bool value)
    {
        ScheduleSave();
        if (value)
            RescanPlugins();
    }
    partial void OnSelectedDeviceChanged(AdbDevice? value) => ScheduleSave();
    partial void OnTurnScreenOffChanged(bool value)
    {
        ScheduleSave();
        foreach (var m in Mirrors)
            _ = m.SetScreenDimmedAsync(value);
    }

    private async Task ReconnectActiveAsync()
    {
        var m = ActiveMirror;
        if (m == null || !m.IsConnected || IsBusy)
            return;
        Log("Réglage modifié — reconnexion de la tuile active…");
        var device = m.Device;
        await RemoveMirrorInternalAsync(m);
        await ConnectDeviceAsync(device);
    }

    public async Task InitializeAsync()
    {
        var adb = AdbService.FindAdb();
        var bundled = Path.Combine(AppContext.BaseDirectory, "assets", "platform-tools", "adb.exe");
        AdbStatus = adb == null
            ? "adb introuvable — installe les platform-tools du SDK Android"
            : string.Equals(adb, bundled, StringComparison.OrdinalIgnoreCase)
                ? "adb embarqué — rien à installer"
                : $"adb : {adb}";
        await RefreshDevicesAsync();
        RescanPlugins();
        _ = CheckUpdateAsync();
    }

    private async Task CheckUpdateAsync()
    {
        var update = await UpdateService.CheckAsync();
        if (update is { } u)
        {
            UpdateVersion = u.Version.ToString(3);
            UpdateUrl = u.Url;
            OnPropertyChanged(nameof(UpdateUrl));
            Log($"Mise à jour disponible : v{UpdateVersion}");
        }
    }

    [RelayCommand]
    private void OpenUpdate()
    {
        if (!string.IsNullOrEmpty(UpdateUrl))
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(UpdateUrl) { UseShellExecute = true });
    }

    [RelayCommand]
    private void DismissUpdate() => UpdateVersion = null;

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            var list = (await AdbService.GetDevicesAsync()).ToList();

            // Migre les entrées mémorisées par un serial transitoire (ex. ip:5555)
            // vers la clé matérielle quand l'appareil réapparaît.
            foreach (var d in list)
            {
                if (_settings.Devices.ContainsKey(d.DeviceKey))
                    continue;
                var stale = _settings.Devices.FirstOrDefault(kv =>
                    kv.Value.LastSerial != null && d.MatchesSerial(kv.Value.LastSerial));
                if (stale.Key != null)
                {
                    _settings.Devices.Remove(stale.Key);
                    _settings.Devices[d.DeviceKey] = stale.Value;
                }
            }

            // Un appareil déconnecté volontairement puis débranché est oublié :
            // à son retour, la reconnexion auto est de nouveau permise.
            _voluntaryDisconnects.RemoveWhere(s => !list.Any(d => d.Serial == s));

            for (var i = 0; i < list.Count; i++)
            {
                var d = list[i];
                if (!_settings.Devices.TryGetValue(d.DeviceKey, out var prefs))
                    continue;
                prefs.Model = d.Model;
                prefs.LastSerial = d.Serial;
                if (prefs.CustomName != null && prefs.CustomName != d.CustomName)
                    list[i] = d with { CustomName = prefs.CustomName };
            }

            foreach (var (key, prefs) in _settings.Devices)
            {
                var present = list.Any(d => d.DeviceKey == key
                    || (prefs.LastSerial != null && d.MatchesSerial(prefs.LastSerial)));
                if (!present)
                    list.Add(new AdbDevice(prefs.LastSerial ?? key, prefs.Model ?? "",
                        "remembered", CustomName: prefs.CustomName));
            }

            Devices = new ObservableCollection<AdbDevice>(list);
            var current = SelectedDevice != null
                ? list.FirstOrDefault(d => d.SharesIdentity(SelectedDevice) || d.DeviceKey == SelectedDevice.DeviceKey)
                : null;
            SelectedDevice = current
                ?? list.FirstOrDefault(d => d.DeviceKey == _settings.LastSelectedDeviceKey)
                ?? list.FirstOrDefault(d => d.IsReady)
                ?? list.FirstOrDefault();
            var detected = list.Count(d => !d.IsRememberedOnly);
            if (detected == 0)
                Status = "Aucun appareil détecté — active le débogage USB et branche ton téléphone";
            else if (!IsConnected)
                Status = $"{detected} appareil(s) détecté(s)";
            _apiHost.Publish("devices", new { detected });

            // Relance la détection tant qu'un appareil attend une action
            // (autorisation, hors ligne) ou qu'un appareil mémorisé est absent.
            _pollTimer.IsEnabled = list.Any(d => !d.IsReady);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        Services.AppLogger.Write($"ConnectAsync cmd: sel={SelectedDevice?.Serial}");
        var device = SelectedDevice;
        if (device is not { IsReady: true })
        {
            Status = NotReadyMessage(device);
            return;
        }
        await ConnectDeviceAsync(device);
    }

    [RelayCommand]
    private async Task ConnectToDeviceAsync(AdbDevice device)
    {
        Services.AppLogger.Write($"ConnectToDevice cmd: {device?.Serial} ready={device?.IsReady}");
        if (device is not { IsReady: true })
        {
            Status = NotReadyMessage(device);
            return;
        }
        SelectedDevice = device;
        await ConnectDeviceAsync(device);
    }

    private static string NotReadyMessage(AdbDevice? device) => device switch
    {
        null => "Aucun appareil prêt (vérifie le débogage USB)",
        { NeedsAuthorization: true } =>
            $"Autorise le débogage USB sur « {device.ShortName} » — regarde l'écran de l'appareil",
        { IsOffline: true } => $"« {device.ShortName} » est hors ligne — rebranche-le",
        { IsRememberedOnly: true } => $"« {device.ShortName} » n'est pas détecté — rebranche-le",
        _ => $"« {device.ShortName} » n'est pas prêt"
    };

    private async Task ConnectDeviceAsync(AdbDevice device, bool allowAppLaunch = true)
    {
        Services.AppLogger.Write($"connect start: {device.Serial}");
        _voluntaryDisconnects.Remove(device.Serial);
        var existing = Mirrors.FirstOrDefault(m => m.Device.SharesIdentity(device));
        if (existing != null)
        {
            SetActive(existing);
            return;
        }

        IsBusy = true;
        _hasError = false;
        OnPropertyChanged(nameof(StatusDotColor));
        Status = $"Connexion — {device.DisplayName}…";
        var instance = new MirrorInstance(device)
        {
            ShouldSyncClipboard = () => SyncDeviceClipboard
        };
        try
        {
            instance.Log += Log;
            instance.Connected += m =>
            {
                SetActive(m);
                AnyConnected?.Invoke();
                _apiHost.Publish("mirror.connected",
                    new { slot = m.Slot, name = m.DeviceName, serial = m.Device.Serial });
            };
            instance.Disconnected += m =>
            {
                _apiHost.Publish("mirror.disconnected",
                    new { name = m.DeviceName, serial = m.Device.Serial, manual = m.ManualDisconnect });
                Mirrors.Remove(m);
                PromoteNextActive(m);
            };

            Mirrors.Add(instance);
            ApplyMirrorOrder();
            RefreshInactiveMirrors();
            SetActive(instance);
            MirrorAdded?.Invoke(instance);
            Services.AppLogger.Write("startasync begin");
            await instance.StartAsync(BuildOptions(), allowAppLaunch && AutoLaunchDofus);
            Services.AppLogger.Write("startasync done");
            RememberDevice(device);
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            _hasError = true;
            Mirrors.Remove(instance);
            PromoteNextActive(instance);
            instance.Dispose();
            Status = $"Échec de connexion : {ex.Message}";
            OnPropertyChanged(nameof(StatusDotColor));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (ActiveMirror != null)
            await RemoveMirrorInternalAsync(ActiveMirror);
    }

    [RelayCommand]
    private async Task RemoveMirrorAsync(MirrorInstance? instance)
    {
        if (instance != null)
            await RemoveMirrorInternalAsync(instance);
    }

    private async Task RemoveMirrorInternalAsync(MirrorInstance instance)
    {
        instance.ManualDisconnect = true;
        // Déconnexion volontaire : le watchdog/API ne doit pas reconnecter cet
        // appareil tant qu'il reste détecté. Effacé à la prochaine connexion
        // explicite ou quand l'appareil disparaît (débranché).
        _voluntaryDisconnects.Add(instance.Device.Serial);
        Mirrors.Remove(instance);
        PromoteNextActive(instance);
        await instance.DisconnectAsync();
    }

    /// <summary>Serials déconnectés volontairement — exclus de la reconnexion auto.</summary>
    private readonly HashSet<string> _voluntaryDisconnects = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Vrai si l'appareil a été déconnecté à la demande et reste présent.</summary>
    public bool IsVoluntarilyDisconnected(string serial) => _voluntaryDisconnects.Contains(serial);

    private void RememberDevice(AdbDevice device)
    {
        var key = device.DeviceKey;
        if (!_settings.Devices.TryGetValue(key, out var prefs))
            _settings.Devices[key] = prefs = new DevicePrefs();
        prefs.Model = device.Model;
        prefs.LastSerial = device.Serial;
        ScheduleSave();
    }

    public void RenameDevice(AdbDevice device, string? name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (trimmed == device.CustomName)
            return;
        var key = device.DeviceKey;
        if (!_settings.Devices.TryGetValue(key, out var prefs))
            _settings.Devices[key] = prefs = new DevicePrefs();
        prefs.CustomName = trimmed;
        prefs.Model = device.Model;
        prefs.LastSerial = device.Serial;
        for (var i = 0; i < Devices.Count; i++)
            if (Devices[i].SharesIdentity(device))
                Devices[i] = Devices[i] with { CustomName = trimmed };
        foreach (var m in Mirrors.Where(m => m.Device.SharesIdentity(device)))
            m.DeviceName = trimmed ?? device.DisplayName;
        SaveNow();
    }

    [RelayCommand]
    private void ForgetDevice(AdbDevice? device)
    {
        if (device == null)
            return;
        _settings.Devices.Remove(device.DeviceKey);
        var stale = _settings.Devices.FirstOrDefault(kv =>
            kv.Value.LastSerial != null && device.MatchesSerial(kv.Value.LastSerial));
        if (stale.Key != null)
            _settings.Devices.Remove(stale.Key);
        var i = Devices.IndexOf(device);
        if (i >= 0)
        {
            if (Devices[i].IsRememberedOnly)
                Devices.RemoveAt(i);
            else
                Devices[i] = Devices[i] with { CustomName = null };
        }
        SaveNow();
    }

    private void PromoteNextActive(MirrorInstance removed)
    {
        var next = Mirrors.LastOrDefault();
        if (ActiveMirror == removed)
        {
            if (next != null)
                SetActive(next);
            else
            {
                ActiveMirror = null;
                UpdateStatus();
            }
        }
        else
        {
            RefreshInactiveMirrors();
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task EnableWifiAsync()
    {
        if (SelectedDevice == null)
            return;
        try
        {
            WifiStatus = "Activation WiFi…";
            Status = "Bascule en WiFi…";

            var existing = Mirrors.FirstOrDefault(m => m.Device.SharesIdentity(SelectedDevice));
            var reconnect = existing != null;
            if (existing != null)
                await RemoveMirrorInternalAsync(existing);

            var ip = await AdbService.EnableWifiAsync(SelectedDevice.Serial);

            AdbDevice? wifiDevice = null;
            for (var i = 0; i < 10 && wifiDevice == null; i++)
            {
                await Task.Delay(800);
                await RefreshDevicesAsync();
                var found = Devices.FirstOrDefault(d =>
                    d.Serial.StartsWith(ip) || d.AltSerial?.StartsWith(ip) == true);
                wifiDevice = found?.Preferring($"{ip}:5555");
            }
            if (wifiDevice == null)
            {
                WifiStatus = $"WiFi activé ({ip}:5555) mais l'appareil n'apparaît pas — vérifie le réseau";
                Status = "WiFi activé — sélectionne l'appareil IP dans la liste";
                return;
            }

            SelectedDevice = wifiDevice;
            WifiStatus = $"WiFi activé — {ip}:5555, câble inutile";
            Status = reconnect ? "Reconnexion en WiFi…" : "WiFi prêt — clique Connecter";
            if (reconnect)
                await ConnectDeviceAsync(wifiDevice);
        }
        catch (Exception ex)
        {
            WifiStatus = $"Échec WiFi : {ex.Message}";
            _hasError = true;
            OnPropertyChanged(nameof(StatusDotColor));
        }
    }

    [RelayCommand] private void SendBack() => ActiveMirror?.Session?.Control?.InjectKeyPress(AndroidKeyCode.Back);
    [RelayCommand] private void SendHome() => ActiveMirror?.Session?.Control?.InjectKeyPress(AndroidKeyCode.Home);
    [RelayCommand] private void SendRecents() => ActiveMirror?.Session?.Control?.InjectKeyPress(AndroidKeyCode.AppSwitch);
    [RelayCommand] private void SendPower() => ActiveMirror?.Session?.Control?.InjectKeyPress(AndroidKeyCode.Power);
    [RelayCommand] private void RotateDevice() => ActiveMirror?.Session?.Control?.SendSimple(ControlMsgType.RotateDevice);
    [RelayCommand] private void LaunchDofusTouch() => ActiveMirror?.Session?.Control?.StartApp("com.ankama.dofustouch");
    [RelayCommand] private void Screenshot()
    {
        if (ActiveMirror != null)
            RequestScreenshot(ActiveMirror);
    }

    [RelayCommand]
    private async Task ApplyDofusPresetAsync()
    {
        _suppressReconnect = true;
        MaxSize = 1080;
        MaxFps = 60;
        VideoBitRate = 24_000_000;
        VideoCodec = "h264";
        EnableAudio = true;
        StayAwake = true;
        TurnScreenOff = true;
        AutoLaunchDofus = true;
        _suppressReconnect = false;

        if (ActiveMirror is { IsConnected: true })
        {
            Status = "Preset Dofus appliqué — reconnexion du miroir…";
            await ReconnectActiveAsync();
        }
        else
        {
            Status = "Preset Dofus appliqué — 1080p · 60 fps · 24 Mbps · écran atténué · lancement auto";
        }
    }

    [RelayCommand]
    private void ToggleScreenDim() => TurnScreenOff = !TurnScreenOff;

    [RelayCommand]
    private void ToggleRecording() => ToggleRecordingFor(ActiveMirror);

    private void Log(string message)
    {
        AppLogger.Write(message);
        Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add(message);
            if (Logs.Count > 300)
                Logs.RemoveAt(0);
        });
    }
}