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

    /// <summary>Espaces de travail mémorisés (dispositions multi-téléphones).</summary>
    public ObservableCollection<WorkspaceItem> Workspaces { get; } = new();
    [ObservableProperty] private WorkspaceItem? _activeWorkspace;
    /// <summary>Membres de l'espace actif non connectés — tuiles fantômes.</summary>
    public ObservableCollection<MissingDeviceItem> MissingDevices { get; } = new();
    public bool HasMissingDevices => MissingDevices.Count > 0;
    public bool HasStripContent => HasInactiveMirrors || HasMissingDevices;
    /// <summary>Portée des réglages affichés : appareil actif (espace) ou globaux.</summary>
    public string ActiveSettingsScope =>
        ActiveMirror?.Prefs != null
            ? $"Réglages de « {ActiveMirror.DeviceName} » — propres à cet espace"
            : "Réglages globaux";

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
        foreach (var w in _settings.Workspaces)
            Workspaces.Add(new WorkspaceItem(w));
        if (_settings.ActiveWorkspaceId is { } wid)
        {
            ActiveWorkspace = Workspaces.FirstOrDefault(w => w.Model.Id == wid);
            if (ActiveWorkspace != null)
                ActiveWorkspace.IsActive = true;
        }
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
        // En mode espace, les propriétés vidéo/audio reflètent l'appareil actif :
        // les valeurs globales ne sont réécrites que hors espace (les handlers
        // de changement écrivent déjà directement dans _settings ou l'override).
        if (ActiveWorkspace == null)
        {
            _settings.MaxSize = MaxSize;
            _settings.MaxFps = MaxFps;
            _settings.VideoBitRate = VideoBitRate;
            _settings.VideoCodec = VideoCodec;
            _settings.EnableAudio = EnableAudio;
            _settings.TurnScreenOff = TurnScreenOff;
        }
        _settings.StayAwake = StayAwake;
        _settings.AutoFullscreen = AutoFullscreen;
        _settings.SyncDeviceClipboard = SyncDeviceClipboard;
        _settings.Topmost = Topmost;
        _settings.ShowSettings = ShowSettings;
        _settings.LocalApiEnabled = LocalApiEnabled;
        _settings.LocalApiPort = LocalApiPort;
        _settings.LocalApiToken = string.IsNullOrEmpty(LocalApiToken) ? null : LocalApiToken;
        _settings.LastSelectedDeviceKey = SelectedDevice?.DeviceKey;
        _settings.EnabledPlugins = Plugins.Where(p => p.Running).Select(p => p.Id).ToList();
        _settings.MirrorOrder = Mirrors.Select(x => x.Device.DeviceKey).ToList();
        _settings.Workspaces = Workspaces.Select(w => w.Model).ToList();
        _settings.ActiveWorkspaceId = ActiveWorkspace?.Id;
        SyncActiveWorkspace();
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
        LoadEffectiveSettings();
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

    public Task ConnectExistingDeviceAsync(AdbDevice device) => ConnectDeviceAsync(device);
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

    // ═══ Espaces de travail — dispositions multi-téléphones mémorisées ═══

    /// <summary>Entrée d'espace correspondant à cet appareil (clé matérielle ou dernier serial).</summary>
    private WorkspaceDevice? FindWorkspaceDevice(AdbDevice device)
        => ActiveWorkspace?.Model.Devices.FirstOrDefault(d =>
            d.DeviceKey == device.DeviceKey
            || (d.LastSerial != null && device.MatchesSerial(d.LastSerial)));

    /// <summary>Capture l'état courant (membres, ordre, actif) dans l'espace actif.</summary>
    private void SyncActiveWorkspace()
    {
        var item = ActiveWorkspace;
        if (item == null)
            return;
        var ws = item.Model;
        var stale = ws.Devices.ToDictionary(d => d.DeviceKey);
        var next = new List<WorkspaceDevice>();
        foreach (var m in Mirrors)
        {
            if (stale.Remove(m.Device.DeviceKey, out var e))
            {
                e.Model = m.Device.Model;
                e.LastSerial = m.Device.Serial;
                next.Add(e);
            }
            else
            {
                next.Add(new WorkspaceDevice
                {
                    DeviceKey = m.Device.DeviceKey,
                    Model = m.Device.Model,
                    LastSerial = m.Device.Serial
                });
            }
            m.Prefs = next[^1];
        }
        // Les membres non connectés restent dans l'espace (tuiles « absentes »).
        next.AddRange(stale.Values);
        ws.Devices = next;
        ws.ActiveDeviceKey = ActiveMirror?.Device.DeviceKey;
        item.Refresh();
    }

    [RelayCommand]
    private void NewWorkspace()
    {
        var item = new WorkspaceItem(new Workspace { Name = $"Espace {Workspaces.Count + 1}" });
        Workspaces.Add(item);
        _ = SelectWorkspaceAsync(item);
    }

    /// <summary>Bascule vers un espace : sauvegarde le courant puis applique la cible.</summary>
    public async Task SelectWorkspaceAsync(WorkspaceItem? item)
    {
        if (item == null || ReferenceEquals(item, ActiveWorkspace))
            return;
        if (IsBusy)
        {
            Status = "Une connexion est en cours — réessaie dans un instant";
            return;
        }
        SaveNow();
        if (ActiveWorkspace != null)
            ActiveWorkspace.IsActive = false;
        ActiveWorkspace = item;
        item.IsActive = true;
        await RestoreWorkspaceAsync(item);
    }

    /// <summary>Applique un espace : déconnecte les hors-espace, connecte les membres
    /// présents, restaure ordre, miroir actif et réglages par appareil.</summary>
    private async Task RestoreWorkspaceAsync(WorkspaceItem item)
    {
        var ws = item.Model;
        var keys = ws.Devices.Select(d => d.DeviceKey).ToHashSet();
        foreach (var m in Mirrors.ToList())
            if (!keys.Contains(m.Device.DeviceKey))
                await RemoveMirrorInternalAsync(m);

        await RefreshDevicesAsync();
        foreach (var wd in ws.Devices)
        {
            var existing = Mirrors.FirstOrDefault(m => m.Device.DeviceKey == wd.DeviceKey);
            if (existing != null)
            {
                existing.Prefs = wd;
                continue;
            }
            var dev = Devices.FirstOrDefault(d => d.DeviceKey == wd.DeviceKey)
                      ?? (wd.LastSerial != null
                          ? Devices.FirstOrDefault(d => d.MatchesSerial(wd.LastSerial))
                          : null);
            if (dev is { IsReady: true })
                await ConnectDeviceAsync(dev, wd);
        }

        _settings.MirrorOrder = ws.Devices.Select(d => d.DeviceKey).ToList();
        ApplyMirrorOrder();
        RefreshInactiveMirrors();
        var active = Mirrors.FirstOrDefault(m => m.Device.DeviceKey == ws.ActiveDeviceKey)
                     ?? Mirrors.FirstOrDefault();
        if (active != null)
            SetActive(active);
        RefreshMissingDevices();
        LoadEffectiveSettings();
        SaveNow();
        var missing = MissingDevices.Count;
        Status = missing == 0
            ? $"Espace « {item.Name} » — {Mirrors.Count} téléphone(s)"
            : $"Espace « {item.Name} » — {Mirrors.Count} connecté(s), {missing} absent(s)";
    }

    /// <summary>Sort de l'espace actif : les réglages redeviennent globaux.</summary>
    public void ExitWorkspace()
    {
        if (ActiveWorkspace == null)
            return;
        SaveNow();
        ActiveWorkspace.IsActive = false;
        ActiveWorkspace = null;
        MissingDevices.Clear();
        OnPropertyChanged(nameof(HasMissingDevices));
        OnPropertyChanged(nameof(HasStripContent));
        LoadEffectiveSettings();
        SaveNow();
    }

    public void RenameWorkspace(WorkspaceItem item, string? name)
    {
        var trimmed = name?.Trim();
        item.IsEditing = false;
        if (string.IsNullOrEmpty(trimmed) || trimmed == item.Model.Name)
        {
            item.Name = item.Model.Name;
            return;
        }
        item.Model.Name = trimmed;
        item.Name = trimmed;
        ScheduleSave();
    }

    [RelayCommand]
    private void DuplicateWorkspace(WorkspaceItem? item)
    {
        if (item == null)
            return;
        var copy = new Workspace
        {
            Name = item.Model.Name + " (copie)",
            ActiveDeviceKey = item.Model.ActiveDeviceKey,
            Devices = item.Model.Devices.Select(d => new WorkspaceDevice
            {
                DeviceKey = d.DeviceKey,
                Model = d.Model,
                LastSerial = d.LastSerial,
                MaxSize = d.MaxSize,
                MaxFps = d.MaxFps,
                VideoBitRate = d.VideoBitRate,
                VideoCodec = d.VideoCodec,
                EnableAudio = d.EnableAudio,
                TurnScreenOff = d.TurnScreenOff
            }).ToList()
        };
        Workspaces.Insert(Workspaces.IndexOf(item) + 1, new WorkspaceItem(copy));
        ScheduleSave();
    }

    [RelayCommand]
    private void DeleteWorkspace(WorkspaceItem? item)
    {
        if (item == null)
            return;
        if (ReferenceEquals(item, ActiveWorkspace))
            ExitWorkspace();
        Workspaces.Remove(item);
        SaveNow();
        Log($"espace supprimé : {item.Name}");
    }

    /// <summary>Ctrl+Maj+N : bascule vers l'espace n° index+1.</summary>
    public void ActivateWorkspaceAt(int index)
    {
        if (index >= 0 && index < Workspaces.Count)
            _ = SelectWorkspaceAsync(Workspaces[index]);
    }

    /// <summary>Membres de l'espace actif sans miroir — tuiles fantômes « absentes ».</summary>
    private void RefreshMissingDevices()
    {
        MissingDevices.Clear();
        var ws = ActiveWorkspace?.Model;
        if (ws != null)
            foreach (var wd in ws.Devices)
            {
                if (Mirrors.Any(m => m.Device.DeviceKey == wd.DeviceKey))
                    continue;
                _settings.Devices.TryGetValue(wd.DeviceKey, out var p);
                MissingDevices.Add(new MissingDeviceItem(
                    wd, p?.CustomName ?? wd.Model ?? wd.DeviceKey, p?.Color));
            }
        OnPropertyChanged(nameof(HasMissingDevices));
        OnPropertyChanged(nameof(HasStripContent));
    }

    /// <summary>Reconnecte les membres de l'espace redevenus prêts (décos volontaires respectées).</summary>
    private bool _connectingMissing;
    private async Task TryConnectMissingAsync()
    {
        if (_connectingMissing || ActiveWorkspace == null || IsBusy)
            return;
        _connectingMissing = true;
        try
        {
            foreach (var wd in ActiveWorkspace.Model.Devices)
            {
                if (Mirrors.Any(m => m.Device.DeviceKey == wd.DeviceKey))
                    continue;
                var dev = Devices.FirstOrDefault(d => d.DeviceKey == wd.DeviceKey
                    || (wd.LastSerial != null && d.MatchesSerial(wd.LastSerial)));
                if (dev is { IsReady: true } && !_voluntaryDisconnects.Contains(dev.Serial))
                    await ConnectDeviceAsync(dev, wd);
            }
            RefreshMissingDevices();
        }
        finally
        {
            _connectingMissing = false;
        }
    }

    [RelayCommand]
    private async Task ConnectMissingAsync(MissingDeviceItem? item)
    {
        if (item == null)
            return;
        await RefreshDevicesAsync();
        var wd = item.Prefs;
        var dev = Devices.FirstOrDefault(d => d.DeviceKey == wd.DeviceKey)
                  ?? (wd.LastSerial != null
                      ? Devices.FirstOrDefault(d => d.MatchesSerial(wd.LastSerial))
                      : null);
        if (dev is { IsReady: true })
            await ConnectDeviceAsync(dev, wd);
        else
            Status = $"« {item.Name} » n'est pas détecté — rebranche-le";
    }

    [RelayCommand]
    private void RemoveMissing(MissingDeviceItem? item)
    {
        var ws = ActiveWorkspace?.Model;
        if (item == null || ws == null)
            return;
        ws.Devices.Remove(item.Prefs);
        RefreshMissingDevices();
        ActiveWorkspace?.Refresh();
        ScheduleSave();
    }

    /// <summary>Couleur d'accent d'un appareil (nulle = couleur par défaut).</summary>
    public void SetDeviceColor(AdbDevice device, string? hex)
    {
        var key = device.DeviceKey;
        if (!_settings.Devices.TryGetValue(key, out var prefs))
            _settings.Devices[key] = prefs = new DevicePrefs();
        prefs.Color = hex;
        prefs.Model = device.Model;
        prefs.LastSerial = device.Serial;
        for (var i = 0; i < Devices.Count; i++)
            if (Devices[i].SharesIdentity(device))
                Devices[i] = Devices[i] with { Color = hex };
        foreach (var m in Mirrors.Where(m => m.Device.SharesIdentity(device)))
            m.AccentHex = hex;
        RefreshMissingDevices();
        SaveNow();
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
            // Garde-fou : un script >256 Ko est suspect pour un plugin control-plane.
            if (new FileInfo(f).Length > 256 * 1024)
            {
                Log($"plugin ignoré : {Path.GetFileName(f)} — fichier trop volumineux");
                continue;
            }
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
        foreach (var c in Catalog)
            c.Refresh(Plugins);
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

    // ═══ Catalogue — plugins publiés sur le dépôt GitHub ═══

    public ObservableCollection<MarketplaceItem> Catalog { get; } = new();

    [ObservableProperty] private string _catalogStatus = "";
    [ObservableProperty] private bool _catalogBusy;

    private bool _catalogLoaded;

    [RelayCommand]
    private async Task LoadCatalog()
    {
        if (CatalogBusy)
            return;
        CatalogBusy = true;
        try
        {
            var entries = await MarketplaceService.FetchAsync();
            Catalog.Clear();
            foreach (var e in entries)
            {
                var item = new MarketplaceItem { Entry = e };
                item.Refresh(Plugins);
                Catalog.Add(item);
            }
            CatalogStatus = Catalog.Count == 0 ? "catalogue vide" : "";
            _catalogLoaded = true;
        }
        catch
        {
            CatalogStatus = "catalogue indisponible — vérifie la connexion";
        }
        finally
        {
            CatalogBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallPlugin(MarketplaceItem? item)
    {
        if (item == null || !item.CanInstall)
            return;
        item.CanInstall = false;
        item.ActionLabel = "Installation…";
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "plugins");
            await MarketplaceService.InstallAsync(item.Entry, dir);
            // L'installation est le consentement : le hash du catalogue est approuvé tel quel.
            _settings.ApprovedPlugins[item.Id] = item.Entry.Hash;
            if (!_settings.EnabledPlugins.Contains(item.Id))
                _settings.EnabledPlugins.Add(item.Id);
            RescanPlugins();
            ScheduleSave();
            item.Refresh(Plugins);
            Log($"plugin installé : {item.Name}");
        }
        catch (Exception ex)
        {
            Log($"installation de {item.Name} impossible : {ex.Message}");
            item.Refresh(Plugins);
        }
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
        RefreshMissingDevices();
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

    /// <summary>Options scrcpy : réglages propres de l'appareil (espace) sinon globaux.</summary>
    private ScrcpyOptions BuildOptions(WorkspaceDevice? o = null) => new()
    {
        MaxSize = o?.MaxSize ?? _settings.MaxSize,
        MaxFps = o?.MaxFps ?? _settings.MaxFps,
        VideoBitRate = o?.VideoBitRate ?? _settings.VideoBitRate,
        VideoCodec = o?.VideoCodec ?? _settings.VideoCodec,
        StayAwake = StayAwake,
        Audio = o?.EnableAudio ?? _settings.EnableAudio,
        TurnScreenOff = o?.TurnScreenOff ?? _settings.TurnScreenOff,
    };

    /// <summary>Réglages propres de l'appareil actif dans l'espace courant (nul hors espace/membre).</summary>
    private WorkspaceDevice? ActivePrefs() => ActiveMirror?.Prefs;

    /// <summary>Recharge les propriétés affichées avec les réglages effectifs du miroir actif.</summary>
    private void LoadEffectiveSettings()
    {
        var o = ActivePrefs();
        _suppressSave = true;
        _suppressReconnect = true;
        MaxSize = o?.MaxSize ?? _settings.MaxSize;
        MaxFps = o?.MaxFps ?? _settings.MaxFps;
        VideoBitRate = o?.VideoBitRate ?? _settings.VideoBitRate;
        VideoCodec = o?.VideoCodec ?? _settings.VideoCodec;
        EnableAudio = o?.EnableAudio ?? _settings.EnableAudio;
        TurnScreenOff = o?.TurnScreenOff ?? _settings.TurnScreenOff;
        _suppressSave = false;
        _suppressReconnect = false;
        OnPropertyChanged(nameof(ActiveSettingsScope));
    }

    private bool _suppressReconnect;

    partial void OnMaxSizeChanged(int value)
    {
        if (_suppressSave) return;
        if (ActivePrefs() is { } o) o.MaxSize = value; else _settings.MaxSize = value;
        ScheduleSave();
        if (!_suppressReconnect) _ = ReconnectActiveAsync();
    }
    partial void OnMaxFpsChanged(int value)
    {
        if (_suppressSave) return;
        if (ActivePrefs() is { } o) o.MaxFps = value; else _settings.MaxFps = value;
        ScheduleSave();
        if (!_suppressReconnect) _ = ReconnectActiveAsync();
    }
    partial void OnVideoBitRateChanged(int value)
    {
        if (_suppressSave) return;
        if (ActivePrefs() is { } o) o.VideoBitRate = value; else _settings.VideoBitRate = value;
        ScheduleSave();
        if (!_suppressReconnect) _ = ReconnectActiveAsync();
    }
    partial void OnVideoCodecChanged(string value)
    {
        if (_suppressSave) return;
        if (ActivePrefs() is { } o) o.VideoCodec = value; else _settings.VideoCodec = value;
        ScheduleSave();
        if (!_suppressReconnect) _ = ReconnectActiveAsync();
    }
    partial void OnEnableAudioChanged(bool value)
    {
        if (_suppressSave) return;
        if (ActivePrefs() is { } o) o.EnableAudio = value; else _settings.EnableAudio = value;
        ScheduleSave();
        if (!_suppressReconnect) _ = ReconnectActiveAsync();
    }
    partial void OnStayAwakeChanged(bool value) => ScheduleSave();
    partial void OnAutoFullscreenChanged(bool value) => ScheduleSave();
    partial void OnSyncDeviceClipboardChanged(bool value) => ScheduleSave();
    partial void OnTopmostChanged(bool value) => ScheduleSave();
    partial void OnShowSettingsChanged(bool value)
    {
        ScheduleSave();
        if (value)
        {
            RescanPlugins();
            if (!_catalogLoaded)
                _ = LoadCatalog();
        }
    }
    partial void OnSelectedDeviceChanged(AdbDevice? value) => ScheduleSave();
    partial void OnTurnScreenOffChanged(bool value)
    {
        if (_suppressSave) return;
        if (ActivePrefs() is { } o)
        {
            o.TurnScreenOff = value;
            if (ActiveMirror != null)
                _ = ActiveMirror.SetScreenDimmedAsync(value);
        }
        else
        {
            _settings.TurnScreenOff = value;
            foreach (var m in Mirrors)
                _ = m.SetScreenDimmedAsync(value);
        }
        ScheduleSave();
    }

    private async Task ReconnectActiveAsync()
    {
        var m = ActiveMirror;
        if (m == null || !m.IsConnected || IsBusy)
            return;
        Log("Réglage modifié — reconnexion de la tuile active…");
        var device = m.Device;
        var prefs = m.Prefs;
        await RemoveMirrorInternalAsync(m);
        await ConnectDeviceAsync(device, prefs);
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
        if (ActiveWorkspace != null)
            _ = RestoreWorkspaceAsync(ActiveWorkspace);
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
                if ((prefs.CustomName != null && prefs.CustomName != d.CustomName)
                    || prefs.Color != d.Color)
                    list[i] = d with { CustomName = prefs.CustomName ?? d.CustomName, Color = prefs.Color };
            }

            foreach (var (key, prefs) in _settings.Devices)
            {
                var present = list.Any(d => d.DeviceKey == key
                    || (prefs.LastSerial != null && d.MatchesSerial(prefs.LastSerial)));
                if (!present)
                    list.Add(new AdbDevice(prefs.LastSerial ?? key, prefs.Model ?? "",
                        "remembered", CustomName: prefs.CustomName, Color: prefs.Color));
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
            RefreshMissingDevices();
            _ = TryConnectMissingAsync();
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

    private async Task ConnectDeviceAsync(AdbDevice device, WorkspaceDevice? prefs = null)
    {
        Services.AppLogger.Write($"connect start: {device.Serial}");
        _voluntaryDisconnects.Remove(device.Serial);
        var existing = Mirrors.FirstOrDefault(m => m.Device.SharesIdentity(device));
        if (existing != null)
        {
            SetActive(existing);
            return;
        }

        // Membre de l'espace actif ? → ses réglages propres s'appliquent.
        prefs ??= FindWorkspaceDevice(device);
        IsBusy = true;
        _hasError = false;
        OnPropertyChanged(nameof(StatusDotColor));
        Status = $"Connexion — {device.DisplayName}…";
        var instance = new MirrorInstance(device)
        {
            ShouldSyncClipboard = () => SyncDeviceClipboard,
            Prefs = prefs,
            AccentHex = _settings.Devices.TryGetValue(device.DeviceKey, out var dp) ? dp.Color : null
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
            await instance.StartAsync(BuildOptions(prefs));
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
        // Déconnexion volontaire : un plugin/l'API ne doit pas reconnecter cet
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
        _suppressReconnect = false;

        if (ActiveMirror is { IsConnected: true })
        {
            Status = "Preset appliqué — reconnexion du miroir…";
            await ReconnectActiveAsync();
        }
        else
        {
            Status = "Preset appliqué — 1080p · 60 fps · 24 Mbps · écran atténué";
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