using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TouchMirror.Scrcpy;
using TouchMirror.Services;

namespace TouchMirror.ViewModels;

public sealed record SettingOption(string LabelKey, int Value)
{
    public string Label => LocalizationService.Get(LabelKey);
}
public sealed record CodecOption(string LabelKey, string Value)
{
    public string Label => LocalizationService.Get(LabelKey);
}

public partial class MainViewModel : ObservableObject
{
    public SettingOption[] MaxSizeOptions { get; } =
        { new("720p", 720), new("1080p", 1080), new("1440p", 1440), new("2160p", 2160), new("opt.natif_max", 0) };
    public SettingOption[] FpsOptions { get; } =
        { new("30 fps", 30), new("60 fps", 60), new("90 fps", 90), new("120 fps", 120) };
    public SettingOption[] BitRateOptions { get; } =
        { new("4 Mbps", 4_000_000), new("8 Mbps", 8_000_000), new("16 Mbps", 16_000_000),
          new("24 Mbps", 24_000_000), new("40 Mbps", 40_000_000) };
    public CodecOption[] CodecOptions { get; } =
        { new("codec.auto", "auto"), new("codec.h264", "h264"), new("codec.h265", "h265"), new("codec.av1", "av1") };
    public CodecOption[] DecoderOptions { get; } =
        { new("dec.gpu", "gpu"), new("dec.cpu", "cpu") };

    public sealed record QualityPreset(string LabelKey, string DetailKey, int MaxSize, int MaxFps, int BitRate)
    {
        public string Label => LocalizationService.Get(LabelKey);
        public string Detail => LocalizationService.Get(DetailKey);
    }
    public QualityPreset[] QualityPresets { get; } =
    {
        new("preset.perf", "preset.perf.detail", 720, 30, 4_000_000),
        new("preset.balanced", "preset.balanced.detail", 1080, 60, 16_000_000),
        new("preset.quality", "preset.quality.detail", 1440, 60, 24_000_000),
        new("preset.max", "preset.max.detail", 0, 60, 40_000_000),
    };

    public sealed record DisplaySourceOption(string LabelKey, bool Virtual)
    {
        public string Label => LocalizationService.Get(LabelKey);
    }
    public DisplaySourceOption[] DisplaySourceOptions { get; } =
    {
        new("disp.src.phone", false),
        new("disp.src.virtual", true),
    };

    public sealed record DisplayFormatOption(string LabelKey, string Spec)
    {
        public string Label => LocalizationService.Get(LabelKey);
    }
    public DisplayFormatOption[] DisplayFormatOptions { get; } =
    {
        new("disp.auto", ""),
        new("disp.1920x1080", "1920x1080/240"),
        new("disp.1600x900", "1600x900/200"),
        new("disp.1280x720", "1280x720/160"),
        new("disp.1080x1920", "1080x1920/300"),
        new("disp.tab8", "1920x1200/280"),
        new("disp.tab10", "2560x1600/240"),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVirtualDisplay))]
    private DisplaySourceOption? _selectedDisplaySource;

    [ObservableProperty] private DisplayFormatOption? _selectedDisplayFormat;

    public bool IsSecondaryAccountMirror => ActiveMirror?.AccountUserId != null;
    public bool DisplaySourceEnabled => !IsSecondaryAccountMirror;
    public bool IsVirtualDisplay => SelectedDisplaySource?.Virtual == true;

    partial void OnSelectedDisplaySourceChanged(DisplaySourceOption? value)
    {
        if (value == null || _suppressSave)
            return;
        ApplyDisplaySpec(value.Virtual ? SelectedDisplayFormat?.Spec ?? "" : null);
    }

    partial void OnSelectedDisplayFormatChanged(DisplayFormatOption? value)
    {
        if (value == null || _suppressSave || !IsVirtualDisplay)
            return;
        ApplyDisplaySpec(value.Spec);
    }

    private void ApplyDisplaySpec(string? spec)
    {
        if (ActivePrefs() is { } o) o.NewDisplay = spec; else _settings.NewDisplay = spec;
        ScheduleSave();
        if (!_suppressReconnect)
            AppLogger.Forget(ReconnectActiveAsync());
    }

    [ObservableProperty] private QualityPreset? _selectedQualityPreset;

    partial void OnSelectedQualityPresetChanged(QualityPreset? value)
    {
        if (value == null || _suppressSave)
            return;
        _suppressReconnect = true;
        MaxSize = value.MaxSize;
        MaxFps = value.MaxFps;
        VideoBitRate = value.BitRate;
        _suppressReconnect = false;
        if (ActiveMirror != null)
            AppLogger.Forget(ReconnectActiveAsync());
        Status = string.Format(L("st.preset_applied"), value.Label) +
                 (ActiveMirror?.Prefs != null ? string.Format(L("st.to_device"), ActiveMirror.DeviceName) : L("st.global"));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(StatusDotColor))]
    private ObservableCollection<MirrorInstance> _mirrors = new();

    public ObservableCollection<MirrorInstance> InactiveMirrors { get; } = new();

    public ObservableCollection<PluginInstance> Plugins { get; } = new();

    public ObservableCollection<WorkspaceItem> Workspaces { get; } = new();
    [ObservableProperty] private WorkspaceItem? _activeWorkspace;
    public ObservableCollection<MissingDeviceItem> MissingDevices { get; } = new();
    public bool HasMissingDevices => MissingDevices.Count > 0;
    public bool HasStripContent => HasInactiveMirrors || HasMissingDevices;
    public string ActiveSettingsScope =>
        ActiveMirror?.Prefs != null
            ? string.Format(L("st.settings_of"), ActiveMirror.DeviceName)
            : L("st.global_settings");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveMirrorName))]
    [NotifyPropertyChangedFor(nameof(IsActiveMirrorIos))]
    [NotifyPropertyChangedFor(nameof(IosBleActive))]
    [NotifyPropertyChangedFor(nameof(IsSecondaryAccountMirror))]
    [NotifyPropertyChangedFor(nameof(DisplaySourceEnabled))]
    private MirrorInstance? _activeMirror;

    public bool IsActiveMirrorIos => ActiveMirror?.IsIos == true;
    public bool IosBleActive => (ActiveMirror as IosMirrorInstance)?.BleActive == true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetectedDeviceCount))]
    private ObservableCollection<AdbDevice> _devices = new();

    public int DetectedDeviceCount => Devices.Count(d => !d.IsRememberedOnly);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    private AdbDevice? _selectedDevice;
    [ObservableProperty] private bool _keybindEditMode;

    partial void OnKeybindEditModeChanged(bool value)
    {
        foreach (var m in Mirrors)
            m.KeybindEditMode = value && m == ActiveMirror;
    }

    [RelayCommand]
    private async Task ToggleIosBleAsync()
    {
        if (ActiveMirror is not IosMirrorInstance ios)
            return;
        if (!ios.BleActive)
            await ios.EnableBleControlAsync();
        else
            ios.DisableBleControl();
        OnPropertyChanged(nameof(IosBleActive));
        if (!string.IsNullOrEmpty(ios.BleStatus))
            Status = ios.BleStatus;
    }

    [ObservableProperty] private string _status = "";
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
    private static string L(string key) => LocalizationService.Get(key);
    private bool _suppressSave;
    private readonly LocalApiHost _apiHost;
    private LocalApiServer? _apiServer;
    private bool _apiBusy;

    public MainViewModel()
    {
        CatalogView = CollectionViewSource.GetDefaultView(Catalog);
        CatalogView.Filter = CatalogPredicate;
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
        LocalizationService.Instance.Load(_settings.Language);
        Status = L("st.select_device");
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
        VideoDecoder = _settings.VideoDecoder;
        VideoSharpen = _settings.VideoSharpen;
        StayAwake = _settings.StayAwake;
        EnableAudio = _settings.EnableAudio;
        AutoFullscreen = _settings.AutoFullscreen;
        SyncDeviceClipboard = _settings.SyncDeviceClipboard;
        Topmost = _settings.Topmost;
        TurnScreenOff = _settings.TurnScreenOff;
        Language = _settings.Language;
        AutoLaunchDofus = _settings.AutoLaunchDofus;
        ShowSettings = _settings.ShowSettings;
        LocalApiPort = _settings.LocalApiPort;
        LocalApiToken = _settings.LocalApiToken ?? "";
        LocalApiEnabled = _settings.LocalApiEnabled;
        SelectedDisplayFormat = DisplayFormatOptions.FirstOrDefault(f => f.Spec == _settings.NewDisplay)
            ?? DisplayFormatOptions[0];
        SelectedDisplaySource = DisplaySourceOptions[_settings.NewDisplay == null ? 0 : 1];
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
        if (ActiveWorkspace == null)
        {
            _settings.MaxSize = MaxSize;
            _settings.MaxFps = MaxFps;
            _settings.VideoBitRate = VideoBitRate;
            _settings.VideoCodec = VideoCodec;
            _settings.VideoDecoder = VideoDecoder;
            _settings.VideoSharpen = VideoSharpen;
            _settings.EnableAudio = EnableAudio;
            _settings.TurnScreenOff = TurnScreenOff;
        }
        _settings.AutoLaunchDofus = AutoLaunchDofus;
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
        _settings.MirrorOrder = Mirrors.Select(x => x.IdentityKey).ToList();
        _settings.Workspaces = Workspaces.Select(w => w.Model).ToList();
        _settings.ActiveWorkspaceId = ActiveWorkspace?.Id;
        SyncActiveWorkspace();
        SettingsStore.Save(_settings);
        if (LocalApiEnabled && _apiServer is { Port: { } p } && p != LocalApiPort)
            AppLogger.Forget(RestartApiAsync());
    }

    [ObservableProperty] private int _maxSize = 0;
    [ObservableProperty] private int _maxFps = 60;
    [ObservableProperty] private int _videoBitRate = 16_000_000;
    [ObservableProperty] private string _videoCodec = "auto";
    [ObservableProperty] private string _videoDecoder = "gpu";
    [ObservableProperty] private bool _videoSharpen;
    [ObservableProperty] private bool _stayAwake;
    [ObservableProperty] private bool _enableAudio = true;
    [ObservableProperty] private bool _autoFullscreen;
    [ObservableProperty] private bool _syncDeviceClipboard = true;
    [ObservableProperty] private bool _topmost;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TurnScreenOffText))]
    private bool _turnScreenOff;
    [ObservableProperty] private bool _autoLaunchDofus;
    [ObservableProperty] private bool _adaptiveBitrate = true;
    public string TurnScreenOffText => TurnScreenOff ? L("misc.on") : L("misc.off");

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
            m.SetVideoHidden(m != instance);
            m.KeybindEditMode = KeybindEditMode && m == instance;
        }
        RefreshInactiveMirrors();
        UpdateStatus();
        LoadEffectiveSettings();
        RefreshKeybindProfiles(instance);
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

    [ObservableProperty] private bool _localApiEnabled;
    [ObservableProperty] private int _localApiPort = 47613;
    [ObservableProperty] private string _localApiToken = "";
    [ObservableProperty] private string _language = "fr";

    partial void OnLanguageChanged(string value)
    {
        if (_suppressSave) return;
        _settings.Language = value;
        LocalizationService.Instance.Load(value);
        ScheduleSave();
    }

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
        Status = m.ToggleRecording(m.Session?.VideoCodecId ?? VideoCodec);
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
        AppLogger.Forget(RestartApiAsync());
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

    private static string IdentityKeyOf(AdbDevice device, int? accountUserId)
        => accountUserId is { } id ? $"{device.DeviceKey}#{id}" : device.DeviceKey;

    private static (string BaseKey, int? UserId) SplitIdentityKey(string key)
    {
        var i = key.IndexOf('#');
        return i < 0 ? (key, null)
            : (key[..i], int.TryParse(key[(i + 1)..], out var u) ? u : null);
    }

    private WorkspaceDevice? FindWorkspaceDevice(AdbDevice device, int? accountUserId = null)
    {
        var key = IdentityKeyOf(device, accountUserId);
        return ActiveWorkspace?.Model.Devices.FirstOrDefault(d =>
            d.DeviceKey == key
            || (accountUserId == null && d.LastSerial != null && device.MatchesSerial(d.LastSerial)));
    }

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
            if (stale.Remove(m.IdentityKey, out var e))
            {
                e.Model = m.Device.Model;
                e.LastSerial = m.Device.Serial;
                e.AccountUserId = m.AccountUserId;
                e.AccountName = m.AccountName;
                next.Add(e);
            }
            else
            {
                next.Add(new WorkspaceDevice
                {
                    DeviceKey = m.IdentityKey,
                    Model = m.Device.Model,
                    LastSerial = m.Device.Serial,
                    AccountUserId = m.AccountUserId,
                    AccountName = m.AccountName
                });
            }
            m.Prefs = next[^1];
        }
        next.AddRange(stale.Values);
        ws.Devices = next;
        ws.ActiveDeviceKey = ActiveMirror?.IdentityKey;
        item.Refresh();
    }

    [RelayCommand]
    private void NewWorkspace()
    {
        var item = new WorkspaceItem(new Workspace { Name = string.Format(L("ws.default_name"), Workspaces.Count + 1) });
        Workspaces.Add(item);
        AppLogger.Forget(SelectWorkspaceAsync(item));
    }

    public async Task SelectWorkspaceAsync(WorkspaceItem? item)
    {
        if (item == null || ReferenceEquals(item, ActiveWorkspace))
            return;
        if (IsBusy)
        {
            Status = L("st.connecting_wait");
            return;
        }
        SaveNow();
        if (ActiveWorkspace != null)
            ActiveWorkspace.IsActive = false;
        ActiveWorkspace = item;
        item.IsActive = true;
        await RestoreWorkspaceAsync(item);
    }

    private async Task RestoreWorkspaceAsync(WorkspaceItem item)
    {
        var ws = item.Model;
        var keys = ws.Devices.Select(d => d.DeviceKey).ToHashSet();
        foreach (var m in Mirrors.ToList())
            if (!keys.Contains(m.IdentityKey))
                await RemoveMirrorInternalAsync(m);

        await RefreshDevicesAsync();
        foreach (var wd in ws.Devices)
        {
            var existing = Mirrors.FirstOrDefault(m => m.IdentityKey == wd.DeviceKey);
            if (existing != null)
            {
                existing.Prefs = wd;
                continue;
            }
            var (baseKey, userId) = SplitIdentityKey(wd.DeviceKey);
            var dev = Devices.FirstOrDefault(d => d.DeviceKey == baseKey)
                      ?? (wd.LastSerial != null
                          ? Devices.FirstOrDefault(d => d.MatchesSerial(wd.LastSerial))
                          : null);
            if (dev is { IsReady: true })
                await ConnectDeviceAsync(dev, wd,
                    userId is { } u ? new MirrorAccount(u, wd.AccountName ?? string.Format(L("account.default_name"), u)) : null);
        }

        _settings.MirrorOrder = ws.Devices.Select(d => d.DeviceKey).ToList();
        ApplyMirrorOrder();
        RefreshInactiveMirrors();
        var active = Mirrors.FirstOrDefault(m => m.IdentityKey == ws.ActiveDeviceKey)
                     ?? Mirrors.FirstOrDefault();
        if (active != null)
            SetActive(active);
        RefreshMissingDevices();
        LoadEffectiveSettings();
        SaveNow();
        var missing = MissingDevices.Count;
        Status = missing == 0
            ? string.Format(L("ws.status_ok"), item.Name, Mirrors.Count)
            : string.Format(L("ws.status_missing"), item.Name, Mirrors.Count, missing);
    }

    public bool HasActiveWorkspace => ActiveWorkspace != null;

    partial void OnActiveWorkspaceChanged(WorkspaceItem? value)
        => OnPropertyChanged(nameof(HasActiveWorkspace));

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
        Status = L("st.select_device");
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
                VideoDecoder = d.VideoDecoder,
                EnableAudio = d.EnableAudio,
                TurnScreenOff = d.TurnScreenOff,
                NewDisplay = d.NewDisplay,
                AccountUserId = d.AccountUserId,
                AccountName = d.AccountName
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

    public void ActivateWorkspaceAt(int index)
    {
        if (index >= 0 && index < Workspaces.Count)
            AppLogger.Forget(SelectWorkspaceAsync(Workspaces[index]));
    }

    private void RefreshMissingDevices()
    {
        MissingDevices.Clear();
        var ws = ActiveWorkspace?.Model;
        if (ws != null)
            foreach (var wd in ws.Devices)
            {
                if (Mirrors.Any(m => m.IdentityKey == wd.DeviceKey))
                    continue;
                var (baseKey, _) = SplitIdentityKey(wd.DeviceKey);
                _settings.Devices.TryGetValue(baseKey, out var p);
                var name = p?.CustomName ?? wd.Model ?? baseKey;
                if (wd.AccountName != null)
                    name += $" · {wd.AccountName}";
                MissingDevices.Add(new MissingDeviceItem(wd, name, p?.Color));
            }
        OnPropertyChanged(nameof(HasMissingDevices));
        OnPropertyChanged(nameof(HasStripContent));
    }

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
                if (Mirrors.Any(m => m.IdentityKey == wd.DeviceKey))
                    continue;
                var (baseKey, userId) = SplitIdentityKey(wd.DeviceKey);
                var dev = Devices.FirstOrDefault(d => d.DeviceKey == baseKey
                    || (wd.LastSerial != null && d.MatchesSerial(wd.LastSerial)));
                if (dev is { IsReady: true } && !_voluntaryDisconnects.Contains(dev.Serial)
                    && !_voluntaryDisconnects.Contains(wd.DeviceKey))
                    await ConnectDeviceAsync(dev, wd,
                        userId is { } u ? new MirrorAccount(u, wd.AccountName ?? string.Format(L("account.default_name"), u)) : null);
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
        var (baseKey, userId) = SplitIdentityKey(wd.DeviceKey);
        _voluntaryDisconnects.Remove(wd.DeviceKey);
        var dev = Devices.FirstOrDefault(d => d.DeviceKey == baseKey)
                  ?? (wd.LastSerial != null
                      ? Devices.FirstOrDefault(d => d.MatchesSerial(wd.LastSerial))
                      : null);
        if (dev is { IsReady: true })
            await ConnectDeviceAsync(dev, wd,
                userId is { } u ? new MirrorAccount(u, wd.AccountName ?? string.Format(L("account.default_name"), u)) : null);
        else
            Status = string.Format(L("st.not_detected"), item.Name);
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

    private PluginApi ApiFor(PluginInstance p) => new(_apiHost, msg => p.Emit(msg), p.Id,
        Path.GetFileName(p.FilePath).Equals("plugin.js", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(p.FilePath)!
            : Path.Combine(Path.GetDirectoryName(p.FilePath)!, p.Id));

    private static readonly string BundledPluginsDir =
        Path.Combine(AppContext.BaseDirectory, "assets", "plugins");
    private static readonly string UserPluginsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TouchMirror", "plugins");
    private static readonly string LegacyPluginsDir =
        Path.Combine(AppContext.BaseDirectory, "plugins");

    private static string PluginKey(string path)
        => Path.GetFileName(path).Equals("plugin.js", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(Path.GetDirectoryName(path)!)
            : Path.GetFileNameWithoutExtension(path);

    private static bool IsBundledPath(string path)
        => path.StartsWith(BundledPluginsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private void MigrateLegacyPlugins()
    {
        try
        {
            if (!Directory.Exists(LegacyPluginsDir))
                return;
            Directory.CreateDirectory(UserPluginsDir);
            var moved = 0;
            foreach (var d in Directory.EnumerateDirectories(LegacyPluginsDir))
            {
                var name = Path.GetFileName(d);
                try
                {
                    var bundledPlugin = Path.Combine(BundledPluginsDir, name, "plugin.js");
                    var legacyPlugin = Path.Combine(d, "plugin.js");
                    var dest = Path.Combine(UserPluginsDir, name);
                    var hasPlugin = File.Exists(legacyPlugin);
                    if (!Directory.EnumerateFileSystemEntries(d).Any()
                        || Directory.Exists(dest)
                        || hasPlugin && File.Exists(bundledPlugin)
                            && File.ReadAllBytes(bundledPlugin).SequenceEqual(File.ReadAllBytes(legacyPlugin)))
                    {
                        Directory.Delete(d, true);
                    }
                    else
                    {
                        Directory.Move(d, dest);
                        if (hasPlugin)
                            moved++;
                    }
                }
                catch (Exception ex) { Log($"migration plugin {name} : {ex.Message}"); }
            }
            foreach (var f in Directory.EnumerateFiles(LegacyPluginsDir, "*.js"))
            {
                try
                {
                    var dest = Path.Combine(UserPluginsDir, Path.GetFileName(f));
                    if (File.Exists(dest)) File.Delete(f);
                    else { File.Move(f, dest); moved++; }
                }
                catch (Exception ex) { Log($"migration plugin {Path.GetFileName(f)} : {ex.Message}"); }
            }
            try { Directory.Delete(LegacyPluginsDir, true); } catch { }
            if (moved > 0)
                Log($"plugins migrés vers le profil utilisateur : {moved}");
        }
        catch (Exception ex) { Log($"migration des plugins : {ex.Message}"); }
    }

    [RelayCommand]
    private void RescanPlugins()
    {
        Directory.CreateDirectory(UserPluginsDir);
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { UserPluginsDir, BundledPluginsDir })
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var f in Directory.EnumerateFiles(root, "*.js"))
                if (seen.Add(PluginKey(f)))
                    files.Add(f);
            foreach (var d in Directory.EnumerateDirectories(root))
            {
                var pj = Path.Combine(d, "plugin.js");
                if (File.Exists(pj) && seen.Add(Path.GetFileName(d)))
                    files.Add(pj);
            }
        }
        files = files.OrderBy(PluginKey).ToList();

        for (var i = Plugins.Count - 1; i >= 0; i--)
            if (!files.Contains(Plugins[i].FilePath))
            {
                Plugins[i].Stop();
                Plugins.RemoveAt(i);
            }
        foreach (var f in files.Where(f => Plugins.All(p => p.FilePath != f)))
        {
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
            if (p.Running && !p.IsVerified && !IsApproved(p))
            {
                p.Stop();
                _settings.EnabledPlugins.Remove(p.Id);
                Log($"plugin arrêté : {p.Name} — fichier modifié, reconfirmation requise");
                continue;
            }
            if (p.Running || !_settings.EnabledPlugins.Contains(p.Id))
                continue;
            if (!p.IsVerified && !IsApproved(p))
            {
                Log($"plugin {p.Name} non démarré — contenu non vérifié, confirmation requise");
                continue;
            }
            p.Start(ApiFor(p));
        }
        foreach (var c in Catalog)
            c.Refresh(Plugins);
        CatalogView.Refresh();
        RefreshGuidesVisibility();
    }

    private bool IsApproved(PluginInstance p)
        => p.ContentHash != null
           && _settings.ApprovedPlugins.TryGetValue(p.Id, out var h)
           && string.Equals(h, p.ContentHash, StringComparison.OrdinalIgnoreCase);

    public Func<PluginInstance, Task<bool>>? ConfirmUnverified;
    public Func<MarketplaceItem, Task<bool>>? ConfirmInstall;

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
        RefreshGuidesVisibility();
        ScheduleSave();
    }

    [RelayCommand]
    private void OpenPluginsFolder()
    {
        Directory.CreateDirectory(UserPluginsDir);
        System.Diagnostics.Process.Start("explorer.exe", UserPluginsDir);
    }

    public ObservableCollection<MarketplaceItem> Catalog { get; } = new();
    public ICollectionView CatalogView { get; }

    [ObservableProperty] private string _catalogStatus = "";
    [ObservableProperty] private bool _catalogBusy;
    [ObservableProperty] private string _catalogFilter = "";
    [ObservableProperty] private Visibility _guidesVisibility = Visibility.Collapsed;
    [ObservableProperty] private int _guidesColumns = 3;

    private bool _catalogLoaded;

    private void RefreshGuidesVisibility()
    {
        var on = Plugins.Any(p => p.Running && p.Id == "guides");
        GuidesVisibility = on ? Visibility.Visible : Visibility.Collapsed;
        GuidesColumns = on ? 4 : 3;
    }

    partial void OnCatalogFilterChanged(string value) => CatalogView.Refresh();

    private bool CatalogPredicate(object o)
    {
        if (o is not MarketplaceItem m)
            return false;
        return string.IsNullOrWhiteSpace(CatalogFilter)
               || m.Name.Contains(CatalogFilter, StringComparison.OrdinalIgnoreCase)
               || m.Description.Contains(CatalogFilter, StringComparison.OrdinalIgnoreCase);
    }

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
        catch (Exception ex)
        {
            CatalogStatus = L("st.catalog_down");
            Log($"catalogue : {ex.Message}");
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
        if (ConfirmInstall != null && !await ConfirmInstall(item))
            return;
        item.CanInstall = false;
        item.ActionLabel = L("st.installing");
        try
        {
            await MarketplaceService.InstallAsync(item.Entry, UserPluginsDir);
            _settings.ApprovedPlugins[item.Id] = item.Entry.Hash;
            if (!_settings.EnabledPlugins.Contains(item.Id))
                _settings.EnabledPlugins.Add(item.Id);
            RescanPlugins();
            ScheduleSave();
            item.Refresh(Plugins);
            CatalogView.Refresh();
            Log($"plugin installé : {item.Name}");
        }
        catch (Exception ex)
        {
            Log($"installation de {item.Name} impossible : {ex.Message}");
            item.Refresh(Plugins);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPlugin))]
    private MarketplaceItem? _selectedPlugin;
    public bool HasSelectedPlugin => SelectedPlugin != null;

    [RelayCommand]
    private void SelectPlugin(MarketplaceItem? item)
    {
        SelectedPlugin = item;
        if (item != null)
            AppLogger.Forget(LoadDetailAsync(item));
    }

    [RelayCommand]
    private void OpenInstalledPlugin(PluginInstance? p)
    {
        if (p == null)
            return;
        var item = Catalog.FirstOrDefault(c => c.Id == p.Id);
        if (item == null)
        {
            item = new MarketplaceItem
            {
                Entry = new MarketplaceEntry
                {
                    Id = p.Id, Name = p.Name, Version = p.Version ?? "",
                    Author = p.Author ?? "", Icon = p.Icon,
                    Description = p.Description ?? "",
                    Hash = p.ContentHash ?? "", Official = p.IsVerified
                }
            };
            item.Refresh(Plugins);
        }
        SelectPlugin(item);
    }

    [RelayCommand]
    private void BackToCatalog() => SelectedPlugin = null;

    private async Task LoadDetailAsync(MarketplaceItem item)
    {
        if (item.CodeBusy)
            return;
        item.CodeBusy = true;
        item.CodeError = false;
        item.Code = "";
        try
        {
            var local = Plugins.FirstOrDefault(x => x.Id == item.Id);
            var code = local != null && File.Exists(local.FilePath)
                ? await File.ReadAllTextAsync(local.FilePath)
                : await MarketplaceService.FetchCodeAsync(item.Id);
            item.Code = code;
            item.Capabilities = PluginAudit.Extract(code);
        }
        catch
        {
            item.CodeError = true;
            item.Capabilities = new List<string>();
        }
        finally
        {
            item.CodeBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleCatalogPlugin(MarketplaceItem? item)
    {
        if (item == null)
            return;
        var p = Plugins.FirstOrDefault(x => x.Id == item.Id);
        if (p == null)
            return;
        await TogglePlugin(p);
        item.Refresh(Plugins);
    }

    [RelayCommand]
    private void UninstallCatalogPlugin(MarketplaceItem? item)
    {
        if (item == null)
            return;
        try
        {
            var p = Plugins.FirstOrDefault(x => x.Id == item.Id);
            if (p != null && IsBundledPath(p.FilePath))
            {
                if (p.Running)
                    p.Stop();
                _settings.EnabledPlugins.Remove(p.Id);
                ScheduleSave();
                RescanPlugins();
                foreach (var c in Catalog)
                    c.Refresh(Plugins);
                Log($"plugin livré avec l'app — {item.Name} désactivé");
                return;
            }
            if (p != null)
            {
                if (p.Running)
                    p.Stop();
                if (Path.GetFileName(p.FilePath).Equals("plugin.js", StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(Path.GetDirectoryName(p.FilePath)!, true);
                else
                    File.Delete(p.FilePath);
            }
            else if (MarketplaceService.IsValidId(item.Id))
            {
                var dir = Path.Combine(UserPluginsDir, item.Id);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
                var loose = Path.Combine(UserPluginsDir, item.Id + ".js");
                if (File.Exists(loose))
                    File.Delete(loose);
            }
            _settings.ApprovedPlugins.Remove(item.Id);
            _settings.EnabledPlugins.Remove(item.Id);
            ScheduleSave();
            RescanPlugins();
            foreach (var c in Catalog)
                c.Refresh(Plugins);
            Log($"plugin désinstallé : {item.Name}");
        }
        catch (Exception ex)
        {
            Log($"désinstallation de {item.Name} impossible : {ex.Message}");
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
        _settings.MirrorOrder = Mirrors.Select(x => x.IdentityKey).ToList();
        ScheduleSave();
    }

    private void ApplyMirrorOrder()
    {
        var order = _settings.MirrorOrder;
        if (order.Count == 0)
            return;
        var sorted = Mirrors
            .OrderBy(m => order.IndexOf(m.IdentityKey) is var k && k >= 0 ? k : int.MaxValue)
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
            0 => L("st.disconnected"),
            1 => string.Format(L("st.connected"), Mirrors[0].DeviceName),
            _ => string.Format(L("st.mirrors_active"), Mirrors.Count, ActiveMirror?.DeviceName)
        };
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsDisconnected));
        OnPropertyChanged(nameof(StatusDotColor));
        OnPropertyChanged(nameof(RecordingVisibility));
    }

    private ScrcpyOptions BuildOptions(WorkspaceDevice? o = null, MirrorAccount? account = null) => new()
    {
        MaxSize = o?.MaxSize ?? _settings.MaxSize,
        MaxFps = o?.MaxFps ?? _settings.MaxFps,
        VideoBitRate = o?.VideoBitRate ?? _settings.VideoBitRate,
        VideoCodec = o?.VideoCodec ?? _settings.VideoCodec,
        VideoDecoder = o?.VideoDecoder ?? _settings.VideoDecoder,
        VideoSharpen = VideoSharpen,
        StayAwake = StayAwake,
        Audio = o?.EnableAudio ?? _settings.EnableAudio,
        TurnScreenOff = o?.TurnScreenOff ?? _settings.TurnScreenOff,
        NewDisplay = account != null
            ? (string.IsNullOrEmpty(o?.NewDisplay) ? "1920x1200/280" : o.NewDisplay)
            : (o?.NewDisplay ?? _settings.NewDisplay),
        AutoLaunchPackage = account != null
            ? $"com.ankama.dofustouch@{account.UserId}"
            : AutoLaunchDofus ? "com.ankama.dofustouch" : null,
        AdaptiveBitrate = o?.AdaptiveBitrate ?? _settings.AdaptiveBitrate,
        ClipboardAutosync = SyncDeviceClipboard,
    };

    private WorkspaceDevice? ActivePrefs() => ActiveMirror?.Prefs;

    private void LoadEffectiveSettings()
    {
        var o = ActivePrefs();
        _suppressSave = true;
        _suppressReconnect = true;
        MaxSize = o?.MaxSize ?? _settings.MaxSize;
        MaxFps = o?.MaxFps ?? _settings.MaxFps;
        VideoBitRate = o?.VideoBitRate ?? _settings.VideoBitRate;
        VideoCodec = o?.VideoCodec ?? _settings.VideoCodec;
        VideoDecoder = o?.VideoDecoder ?? _settings.VideoDecoder;
        EnableAudio = o?.EnableAudio ?? _settings.EnableAudio;
        TurnScreenOff = o?.TurnScreenOff ?? _settings.TurnScreenOff;
        AdaptiveBitrate = o?.AdaptiveBitrate ?? _settings.AdaptiveBitrate;
        if (ActiveMirror != null)
            ActiveMirror.AdaptiveBitrate = AdaptiveBitrate;
        var spec = o?.NewDisplay ?? _settings.NewDisplay;
        SelectedDisplayFormat = DisplayFormatOptions.FirstOrDefault(f => f.Spec == spec)
            ?? (IsSecondaryAccountMirror && spec == null
                ? DisplayFormatOptions.First(f => f.Spec == "1920x1200/280")
                : DisplayFormatOptions[0]);
        SelectedDisplaySource = DisplaySourceOptions[spec == null && !IsSecondaryAccountMirror ? 0 : 1];
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
        if (!_suppressReconnect) AppLogger.Forget(ReconnectActiveAsync());
    }
    partial void OnMaxFpsChanged(int value)
    {
        if (_suppressSave) return;
        if (ActivePrefs() is { } o) o.MaxFps = value; else _settings.MaxFps = value;
        ScheduleSave();
        if (!_suppressReconnect) AppLogger.Forget(ReconnectActiveAsync());
    }
    partial void OnVideoBitRateChanged(int value)
    {
        if (_suppressSave) return;
        if (ActivePrefs() is { } o) o.VideoBitRate = value; else _settings.VideoBitRate = value;
        ScheduleSave();
        if (!_suppressReconnect) AppLogger.Forget(ReconnectActiveAsync());
    }
    partial void OnVideoCodecChanged(string value)
    {
        if (_suppressSave) return;
        if (ActivePrefs() is { } o) o.VideoCodec = value; else _settings.VideoCodec = value;
        ScheduleSave();
        if (!_suppressReconnect) AppLogger.Forget(ReconnectActiveAsync());
    }
    partial void OnVideoDecoderChanged(string value)
    {
        if (_suppressSave) return;
        if (ActivePrefs() is { } o) o.VideoDecoder = value; else _settings.VideoDecoder = value;
        ScheduleSave();
        if (!_suppressReconnect) AppLogger.Forget(ReconnectActiveAsync());
    }
    partial void OnEnableAudioChanged(bool value)
    {
        if (_suppressSave) return;
        if (ActivePrefs() is { } o) o.EnableAudio = value; else _settings.EnableAudio = value;
        ScheduleSave();
        if (!_suppressReconnect) AppLogger.Forget(ReconnectActiveAsync());
    }
    partial void OnAdaptiveBitrateChanged(bool value)
    {
        if (_suppressSave) return;
        if (ActivePrefs() is { } o) o.AdaptiveBitrate = value; else _settings.AdaptiveBitrate = value;
        ScheduleSave();
        if (ActiveMirror != null)
            ActiveMirror.AdaptiveBitrate = value;
    }
    partial void OnVideoSharpenChanged(bool value)
    {
        foreach (var m in Mirrors)
            if (m.Decoder?.GpuPresenter is { } p)
                p.Sharpness = value ? Video.GpuPresenter.DefaultSharpness : 0f;
        ScheduleSave();
    }

    partial void OnStayAwakeChanged(bool value) => ScheduleSave();
    partial void OnAutoLaunchDofusChanged(bool value) => ScheduleSave();
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
                AppLogger.Forget(LoadCatalog());
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
                AppLogger.Forget(ActiveMirror.SetScreenDimmedAsync(value));
        }
        else
        {
            _settings.TurnScreenOff = value;
            foreach (var m in Mirrors)
                AppLogger.Forget(m.SetScreenDimmedAsync(value));
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
        var account = m.AccountUserId is { } uid
            ? new MirrorAccount(uid, m.AccountName ?? string.Format(L("account.default_name"), uid))
            : null;
        await RemoveMirrorInternalAsync(m);
        await ConnectDeviceAsync(device, prefs, account);
    }

    public async Task InitializeAsync()
    {
        var adb = AdbService.FindAdb();
        var bundled = Path.Combine(AppContext.BaseDirectory, "assets", "platform-tools", "adb.exe");
        AdbStatus = adb == null
            ? "adb introuvable — installe les platform-tools du SDK Android"
            : string.Equals(adb, bundled, StringComparison.OrdinalIgnoreCase)
                ? L("st.adb_embedded")
                : $"adb : {adb}";
        await RefreshDevicesAsync();
        MigrateLegacyPlugins();
        RescanPlugins();
        AppLogger.Forget(CheckUpdateAsync());
        AppLogger.Forget(TrackDevicesLoopAsync());
        if (ActiveWorkspace != null)
            AppLogger.Forget(RestoreWorkspaceAsync(ActiveWorkspace));
    }

    private readonly CancellationTokenSource _trackCts = new();
    private bool _trackQueued;

    private async Task TrackDevicesLoopAsync()
    {
        await AdbService.TrackDevicesAsync(() =>
        {
            if (_trackQueued)
                return;
            _trackQueued = true;
            _ = Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(350);
                _trackQueued = false;
                try { await RefreshDevicesAsync(); } catch { }
            });
        }, _trackCts.Token);
    }

    public void StopTracking() => _trackCts.Cancel();

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

            foreach (var st in DimmedScreenStore.Pending())
            {
                var dev = list.FirstOrDefault(d =>
                    d.MatchesSerial(st.Serial)
                    || d.DeviceKey == st.DeviceKey
                    || d.DeviceKey == st.Serial);
                if (dev == null)
                    continue;
                try
                {
                    if (st.StayOn >= 0)
                        await AdbService.SetStayOnWhilePluggedInAsync(dev.Serial, st.StayOn);
                    if (st.Brightness >= 0)
                        await AdbService.SetBrightnessAsync(dev.Serial, st.Brightness);
                    DimmedScreenStore.Remove(st);
                    Log($"écran restauré sur {dev.DisplayName} (état retrouvé d'une session précédente)");
                }
                catch { }
            }

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

            for (var i = 0; i < list.Count; i++)
            {
                if (i < Devices.Count)
                {
                    if (!Equals(Devices[i], list[i]))
                        Devices[i] = list[i];
                }
                else
                {
                    Devices.Add(list[i]);
                }
            }
            while (Devices.Count > list.Count)
                Devices.RemoveAt(Devices.Count - 1);
            UpdateSetupOffer(list);
            var current = SelectedDevice != null
                ? list.FirstOrDefault(d => d.SharesIdentity(SelectedDevice) || d.DeviceKey == SelectedDevice.DeviceKey)
                : null;
            SelectedDevice = current
                ?? list.FirstOrDefault(d => d.DeviceKey == _settings.LastSelectedDeviceKey)
                ?? list.FirstOrDefault(d => d.IsReady)
                ?? list.FirstOrDefault();
            var detected = list.Count(d => !d.IsRememberedOnly);
            if (detected == 0)
                Status = L("st.no_device");
            else if (!IsConnected)
                Status = string.Format(L("st.devices_detected"), detected);
            _apiHost.Publish("devices", new { detected });

            _pollTimer.IsEnabled = list.Any(d => !d.IsReady);
            RefreshMissingDevices();
            AppLogger.Forget(TryConnectMissingAsync());
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

    private readonly HashSet<string> _profilesChecked = new();
    private readonly HashSet<string> _setupOffered = new();

    [ObservableProperty] private AdbDevice? _setupTarget;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetupIsAuth))]
    [NotifyPropertyChangedFor(nameof(SetupIsOffer))]
    [NotifyPropertyChangedFor(nameof(SetupIsBusy))]
    private string _setupKind = "";
    public bool SetupIsAuth => SetupKind == "auth";
    public bool SetupIsOffer => SetupKind == "offer";
    public bool SetupIsBusy => SetupKind == "busy";
    [ObservableProperty] private int _setupProfileCount;
    [ObservableProperty] private string _setupAccountName = "";

    private void UpdateSetupOffer(List<AdbDevice> list)
    {
        if (SetupTarget != null)
        {
            var cur = list.FirstOrDefault(d => d.DeviceKey == SetupTarget.DeviceKey);
            var keep = cur != null && SetupKind switch
            {
                "auth" => cur.NeedsAuthorization,
                "busy" or "offer" => cur.IsReady,
                _ => false,
            };
            if (keep)
            {
                if (cur!.Serial != SetupTarget.Serial)
                    SetupTarget = cur;
                return;
            }
            SetupTarget = null;
            SetupKind = "";
        }

        var unauthorized = list.FirstOrDefault(d => d.NeedsAuthorization
            && !_setupOffered.Contains(d.DeviceKey)
            && !_settings.SetupDismissed.Contains(d.DeviceKey));
        if (unauthorized != null)
        {
            SetupTarget = unauthorized;
            SetupKind = "auth";
            return;
        }

        var candidate = list.FirstOrDefault(d => d.IsReady
            && !_setupOffered.Contains(d.DeviceKey)
            && !_profilesChecked.Contains(d.DeviceKey)
            && !_settings.SetupDismissed.Contains(d.DeviceKey));
        if (candidate == null)
            return;

        _profilesChecked.Add(candidate.DeviceKey);
        SetupTarget = candidate;
        SetupKind = "busy";
        AppLogger.Forget(ProbeSetupTargetAsync(candidate));
    }

    private async Task ProbeSetupTargetAsync(AdbDevice device)
    {
        try
        {
            var profiles = await AdbService.ListProfilesAsync(device.Serial);
            if (SetupTarget != device)
                return;
            SetupProfileCount = profiles.Count;
            if (profiles.Count > 0)
            {
                SetupTarget = null;
                SetupKind = "";
            }
            else
            {
                SetupAccountName = string.Format(L("setup.account_name"), profiles.Count + 2);
                SetupKind = "offer";
            }
        }
        catch
        {
            if (SetupTarget == device)
            {
                SetupAccountName = string.Format(L("setup.account_name"), 2);
                SetupKind = "offer";
            }
        }
    }

    private AdbDevice? FreshDevice(AdbDevice? d) => d == null ? null
        : Devices.FirstOrDefault(x => x.DeviceKey == d.DeviceKey && x.IsReady) ?? d;

    [RelayCommand]
    private async Task SetupCreateAccountAsync()
    {
        var device = FreshDevice(SetupTarget);
        if (device == null)
            return;
        _setupOffered.Add(device.DeviceKey);
        _settings.SetupDismissed.Add(device.DeviceKey);
        ScheduleSave();
        SetupTarget = null;
        SetupKind = "";
        var name = SetupAccountName.Trim();
        if (name.Length == 0)
            name = string.Format(L("setup.account_name"), 2);
        await CreateAccountAsync(device, name);
    }

    [RelayCommand]
    private async Task SetupWifiAsync()
    {
        var device = FreshDevice(SetupTarget);
        if (device == null)
            return;
        SelectedDevice = device;
        await EnableWifiAsync();
    }

    [RelayCommand]
    private void SetupDismiss(string? forever)
    {
        var device = SetupTarget;
        if (device == null)
            return;
        _setupOffered.Add(device.DeviceKey);
        if (forever == "True" && !_settings.SetupDismissed.Contains(device.DeviceKey))
        {
            _settings.SetupDismissed.Add(device.DeviceKey);
            ScheduleSave();
        }
        SetupTarget = null;
        SetupKind = "";
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
        null => L("st.no_ready"),
        { NeedsAuthorization: true } =>
            string.Format(L("st.authorize"), device.ShortName),
        { IsOffline: true } => string.Format(L("st.offline"), device.ShortName),
        { IsRememberedOnly: true } => string.Format(L("st.not_detected"), device.ShortName),
        _ => string.Format(L("st.not_ready"), device.ShortName)
    };

    private async Task ConnectDeviceAsync(AdbDevice device, WorkspaceDevice? prefs = null,
        MirrorAccount? account = null)
    {
        Services.AppLogger.Write($"connect start: {device.Serial}");
        _voluntaryDisconnects.Remove(device.Serial);
        _voluntaryDisconnects.Remove(IdentityKeyOf(device, account?.UserId));
        var existing = Mirrors.FirstOrDefault(m => m.Device.SharesIdentity(device)
            && m.AccountUserId == account?.UserId);
        if (existing != null)
        {
            SetActive(existing);
            return;
        }

        prefs ??= FindWorkspaceDevice(device, account?.UserId);
        IsBusy = true;
        _hasError = false;
        OnPropertyChanged(nameof(StatusDotColor));
        Status = account == null
            ? string.Format(L("st.connecting"), device.DisplayName)
            : string.Format(L("st.connecting_account"), device.DisplayName, account.Name);
        var instance = new MirrorInstance(device)
        {
            ShouldSyncClipboard = () => SyncDeviceClipboard,
            Prefs = prefs,
            AccountUserId = account?.UserId,
            AccountName = account?.Name,
            AccentHex = _settings.Devices.TryGetValue(device.DeviceKey, out var dp) ? dp.Color : null
        };
        if (account != null)
            instance.DeviceName = $"{device.ShortName} · {account.Name}";
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
            instance.OverlayLineClicked += (m, id, idx) =>
                _apiHost.Publish("overlay.line",
                    new { slot = m.Slot, id, index = idx });

            Mirrors.Add(instance);
            ApplyMirrorOrder();
            RefreshInactiveMirrors();
            SetActive(instance);
            MirrorAdded?.Invoke(instance);
            Services.AppLogger.Write("startasync begin");
            var options = BuildOptions(prefs, account);
            if (account != null)
                await AdbService.StartUserAsync(device.Serial, account.UserId);
            await instance.StartAsync(options);
            Services.AppLogger.Write("startasync done");
            RememberDevice(device);
            BindKeybindPersistence(instance);
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            _hasError = true;
            Mirrors.Remove(instance);
            PromoteNextActive(instance);
            instance.Dispose();
            Status = string.Format(L("st.connect_failed"), ex.Message);
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
        _voluntaryDisconnects.Add(instance.AccountUserId == null
            ? instance.Device.Serial
            : instance.IdentityKey);
        Mirrors.Remove(instance);
        PromoteNextActive(instance);
        await instance.DisconnectAsync();
    }

    public async Task<List<AndroidProfile>> ListProfilesAsync(AdbDevice device)
    {
        try { return await AdbService.ListProfilesAsync(device.Serial); }
        catch (Exception ex)
        {
            Log($"profiles: {ex.Message}");
            return new List<AndroidProfile>();
        }
    }

    public async Task CreateAccountAsync(AdbDevice device, string name)
    {
        IsBusy = true;
        try
        {
            Status = string.Format(L("st.creating_account"), name);
            var userId = await AdbService.CreateCloneProfileAsync(device.Serial, name);
            Log($"profil clone créé : {name} (user {userId})");
            await AdbService.InstallAppForUserAsync(device.Serial, userId, "com.ankama.dofustouch");
            await AdbService.StartUserAsync(device.Serial, userId);
            await ConnectDeviceAsync(device, null, new MirrorAccount(userId, name));
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            Status = ex.Message.Contains("Maximum number", StringComparison.OrdinalIgnoreCase)
                ? L("st.one_account")
                : string.Format(L("st.create_failed"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OpenAccountAsync(AdbDevice device, AndroidProfile profile)
    {
        try
        {
            if (!profile.Running)
                await AdbService.StartUserAsync(device.Serial, profile.Id);
            await ConnectDeviceAsync(device, null, new MirrorAccount(profile.Id, profile.Name));
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            Status = string.Format(L("st.open_failed"), ex.Message);
        }
    }

    public async Task RemoveAccountAsync(AdbDevice device, AndroidProfile profile)
    {
        var tile = Mirrors.FirstOrDefault(m => m.AccountUserId == profile.Id
            && m.Device.SharesIdentity(device));
        if (tile != null)
            await RemoveMirrorInternalAsync(tile);
        try
        {
            await AdbService.RemoveUserProfileAsync(device.Serial, profile.Id);
            Log($"profil supprimé : {profile.Name} (user {profile.Id})");
            Status = string.Format(L("st.account_deleted"), profile.Name);
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            Status = string.Format(L("st.delete_failed"), ex.Message);
        }
    }

    private AirPlayService? _airPlay;

    [RelayCommand]
    private async Task AddIosMirrorAsync()
    {
        var existing = Mirrors.OfType<IosMirrorInstance>().FirstOrDefault();
        if (existing != null && _airPlay is { IsRunning: true })
        {
            SetActive(existing);
            return;
        }

        IsBusy = true;
        try
        {
            var needRebind = existing != null && _airPlay == null;
            _airPlay ??= new AirPlayService();
            if (!_airPlay.IsRunning)
                await _airPlay.StartAsync();

            if (existing != null)
            {
                SetActive(existing);
                if (needRebind)
                    await existing.StartAsync(_airPlay);
                Status = AirPlayStatusText(existing);
                return;
            }

            var instance = new IosMirrorInstance
            {
                ShouldSyncClipboard = () => false
            };
            instance.Log += Log;
            instance.BleStatusChanged += s =>
            {
                Status = s;
                OnPropertyChanged(nameof(IosBleActive));
            };
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
                StopAirPlayIfUnused();
            };
            instance.OverlayLineClicked += (m, id, idx) =>
                _apiHost.Publish("overlay.line",
                    new { slot = m.Slot, id, index = idx });

            Mirrors.Add(instance);
            ApplyMirrorOrder();
            RefreshInactiveMirrors();
            SetActive(instance);
            MirrorAdded?.Invoke(instance);
            await instance.StartAsync(_airPlay);
            BindKeybindPersistence(instance);
            Status = AirPlayStatusText(instance);
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            _airPlay?.Dispose();
            _airPlay = null;
            _hasError = true;
            Status = string.Format(L("st.airplay_fail"), ex.Message);
            OnPropertyChanged(nameof(StatusDotColor));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string AirPlayStatusText(IosMirrorInstance? tile = null)
    {
        var diag = AirPlayDiagnostics.Run(Environment.ProcessPath ?? "", Environment.ProcessId,
            AirPlayService.RaopPort, AirPlayService.AirPlayPort, 7100);
        if (diag.LocalIPv4 != null)
            Log($"airplay diag: ip={diag.LocalIPv4} profil={diag.ProfileKind ?? "?"}");
        foreach (var c in diag.PortConflicts)
            Log($"airplay diag: port occupé {c}");
        var warn = AirPlayDiagnostics.Summarize(diag);
        tile?.View.SetWaitingHint(warn);
        var howto = L("airplay.howto");
        return warn == null
            ? string.Format(L("airplay.ready"), diag.LocalIPv4, howto)
            : $"{warn}\n{howto}";
    }

    private void StopAirPlayIfUnused()
    {
        if (Mirrors.OfType<IosMirrorInstance>().Any())
            return;
        _airPlay?.Dispose();
        _airPlay = null;
    }

    public void StopAirPlay() => _airPlay?.Dispose();

    private readonly HashSet<string> _voluntaryDisconnects = new(StringComparer.OrdinalIgnoreCase);

    public bool IsVoluntarilyDisconnected(string serial) => _voluntaryDisconnects.Contains(serial);

    private void BindKeybindPersistence(MirrorInstance instance)
    {
        var key = instance.IdentityKey;
        if (_settings.Devices.TryGetValue(key, out var prefs))
            instance.LoadKeybinds(ProfileKeybinds(prefs),
                prefs.KeybindStyle, prefs.KeybindOpacity, prefs.KeybindSize);
        else
            instance.LoadKeybinds(Enumerable.Empty<KeybindData>());
        instance.KeybindsChanged += () =>
        {
            if (!_settings.Devices.TryGetValue(key, out var p))
                _settings.Devices[key] = p = new DevicePrefs();
            SaveKeybindsToProfile(instance, p);
            p.KeybindStyle = instance.KeybindStyle;
            p.KeybindOpacity = instance.KeybindOpacity;
            p.KeybindSize = instance.KeybindSize;
            ScheduleSave();
        };
        instance.EditModeExitRequested += () => KeybindEditMode = false;
        RefreshKeybindProfiles(instance);
    }

    public sealed record KeybindProfileOption(string? Key, string Name);

    [ObservableProperty] private ObservableCollection<KeybindProfileOption> _keybindProfiles = new();
    [ObservableProperty] private KeybindProfileOption? _selectedKeybindProfile;
    private bool _suppressProfileSwitch;

    private static List<KeybindData> ProfileKeybinds(DevicePrefs p) =>
        p.ActiveKeybindProfile != null
        && p.KeybindProfiles.TryGetValue(p.ActiveKeybindProfile, out var l)
            ? l : p.Keybinds;

    private static void SaveKeybindsToProfile(MirrorInstance m, DevicePrefs p)
    {
        var data = m.SaveKeybinds();
        if (p.ActiveKeybindProfile == null)
            p.Keybinds = data;
        else
            p.KeybindProfiles[p.ActiveKeybindProfile] = data;
    }

    private void LoadKeybindsFromProfile(MirrorInstance m, DevicePrefs p)
        => m.LoadKeybinds(ProfileKeybinds(p), p.KeybindStyle, p.KeybindOpacity, p.KeybindSize);

    private void RefreshKeybindProfiles(MirrorInstance m)
    {
        _suppressProfileSwitch = true;
        try
        {
            _settings.Devices.TryGetValue(m.IdentityKey, out var prefs);
            KeybindProfiles.Clear();
            KeybindProfiles.Add(new KeybindProfileOption(null, L("defaut")));
            if (prefs != null)
                foreach (var name in prefs.KeybindProfiles.Keys.OrderBy(k => k))
                    KeybindProfiles.Add(new KeybindProfileOption(name, name));
            SelectedKeybindProfile = KeybindProfiles.FirstOrDefault(o => o.Key == prefs?.ActiveKeybindProfile)
                ?? KeybindProfiles[0];
        }
        finally { _suppressProfileSwitch = false; }
    }

    partial void OnSelectedKeybindProfileChanged(KeybindProfileOption? value)
    {
        if (_suppressProfileSwitch || value == null || ActiveMirror == null)
            return;
        var prefs = PrefsFor(ActiveMirror);
        if (prefs.ActiveKeybindProfile == value.Key)
            return;
        SaveKeybindsToProfile(ActiveMirror, prefs);
        prefs.ActiveKeybindProfile = value.Key;
        LoadKeybindsFromProfile(ActiveMirror, prefs);
        ScheduleSave();
    }

    [RelayCommand]
    private void NewKeybindProfile()
    {
        if (ActiveMirror == null)
            return;
        var prefs = PrefsFor(ActiveMirror);
        var i = 1;
        string name;
        do { name = $"{L("profil")} {i++}"; } while (prefs.KeybindProfiles.ContainsKey(name));
        SaveKeybindsToProfile(ActiveMirror, prefs);
        prefs.KeybindProfiles[name] = ActiveMirror.SaveKeybinds();
        prefs.ActiveKeybindProfile = name;
        RefreshKeybindProfiles(ActiveMirror);
        ScheduleSave();
    }

    [RelayCommand]
    private void DeleteKeybindProfile()
    {
        if (ActiveMirror == null || SelectedKeybindProfile?.Key == null)
            return;
        var prefs = PrefsFor(ActiveMirror);
        prefs.KeybindProfiles.Remove(SelectedKeybindProfile.Key);
        prefs.ActiveKeybindProfile = null;
        LoadKeybindsFromProfile(ActiveMirror, prefs);
        RefreshKeybindProfiles(ActiveMirror);
        ScheduleSave();
    }

    private DevicePrefs PrefsFor(MirrorInstance m)
    {
        if (!_settings.Devices.TryGetValue(m.IdentityKey, out var p))
            _settings.Devices[m.IdentityKey] = p = new DevicePrefs();
        return p;
    }

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
            m.DeviceName = m.AccountName != null
                ? $"{trimmed ?? device.ShortName} · {m.AccountName}"
                : trimmed ?? device.DisplayName;
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
        foreach (var w in Workspaces)
        {
            var removed = w.Model.Devices.RemoveAll(d => d.DeviceKey == device.DeviceKey
                || (d.LastSerial != null && device.MatchesSerial(d.LastSerial)));
            if (removed > 0)
                w.Refresh();
        }
        var i = Devices.IndexOf(device);
        if (i >= 0)
        {
            if (Devices[i].IsRememberedOnly)
                Devices.RemoveAt(i);
            else
                Devices[i] = Devices[i] with { CustomName = null };
        }
        RefreshMissingDevices();
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
            WifiStatus = L("st.wifi_enabling");
            Status = L("st.wifi_switching");

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
                WifiStatus = string.Format(L("st.wifi_not_listed"), ip);
                Status = L("st.wifi_select");
                return;
            }

            SelectedDevice = wifiDevice;
            WifiStatus = string.Format(L("st.wifi_on"), ip);
            Status = reconnect ? L("st.wifi_reconnect") : L("st.wifi_ready");
            if (reconnect)
                await ConnectDeviceAsync(wifiDevice);
        }
        catch (Exception ex)
        {
            WifiStatus = string.Format(L("st.wifi_failed"), ex.Message);
            _hasError = true;
            OnPropertyChanged(nameof(StatusDotColor));
        }
    }

    [RelayCommand] private void SendBack() => ActiveMirror?.Session?.Control?.InjectKeyPress(AndroidKeyCode.Back);
    [RelayCommand] private void SendHome() => ActiveMirror?.Session?.Control?.InjectKeyPress(AndroidKeyCode.Home);
    [RelayCommand] private void SendRecents() => ActiveMirror?.Session?.Control?.InjectKeyPress(AndroidKeyCode.AppSwitch);
    [RelayCommand] private void LaunchDofus() => ActiveMirror?.Session?.Control?.StartApp("com.ankama.dofustouch");
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
        VideoBitRate = 20_000_000;
        VideoCodec = "auto";
        EnableAudio = true;
        StayAwake = true;
        TurnScreenOff = true;
        _suppressReconnect = false;

        if (ActiveMirror is { IsConnected: true })
        {
            Status = L("st.preset_reconnect");
            await ReconnectActiveAsync();
        }
        else
        {
            Status = L("st.dofus_applied");
        }
    }

    [RelayCommand]
    private void ToggleScreenDim() => TurnScreenOff = !TurnScreenOff;

    [RelayCommand]
    private void ToggleRecording() => ToggleRecordingFor(ActiveMirror);

    private readonly ConcurrentQueue<string> _logQueue = new();
    private int _logFlushPending;

    private void Log(string message)
    {
        AppLogger.Write(message);
        _logQueue.Enqueue(message);
        if (Interlocked.Exchange(ref _logFlushPending, 1) != 0)
            return;
        var d = Application.Current?.Dispatcher;
        if (d == null || d.HasShutdownFinished)
        {
            _logFlushPending = 0;
            return;
        }
        try
        {
            d.BeginInvoke(() =>
            {
                _logFlushPending = 0;
                while (_logQueue.TryDequeue(out var m))
                {
                    Logs.Add(m);
                    if (Logs.Count > 300)
                        Logs.RemoveAt(0);
                }
            });
        }
        catch (InvalidOperationException)
        {
            _logFlushPending = 0;
        }
    }
}
