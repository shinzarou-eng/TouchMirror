using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TouchMirror.Engine;
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

    public sealed record DiagIssue(string Title, string Detail, string? FixKey = null, string? FixLabel = null, string? Tag = null);

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
    public ObservableCollection<ActivityEntry> ActivityLog { get; } = new();
    public bool HasMissingDevices => MissingDevices.Count > 0;
    public bool HasStripContent => HasInactiveMirrors || HasMissingDevices;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDeviceScope))]
    private bool _settingsScopeGlobal;

    public bool HasDeviceScope => ActiveMirror?.Prefs != null;
    public string ActiveSettingsScope =>
        ActivePrefs() != null
            ? string.Format(L("st.settings_of"), ActiveMirror!.DeviceName)
            : L("st.global_settings");

    partial void OnSettingsScopeGlobalChanged(bool value) => LoadEffectiveSettings();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveMirrorName))]
    [NotifyPropertyChangedFor(nameof(IsActiveMirrorIos))]
    [NotifyPropertyChangedFor(nameof(IosBleActive))]
    [NotifyPropertyChangedFor(nameof(IsSecondaryAccountMirror))]
    [NotifyPropertyChangedFor(nameof(DisplaySourceEnabled))]
    [NotifyPropertyChangedFor(nameof(BenchTitle))]
    private MirrorInstance? _activeMirror;

    public bool IsActiveMirrorIos => ActiveMirror?.IsIos == true;
    public string BenchTitle => ActiveMirror != null
        ? $"{L("bench.title")} — {ActiveMirror.Device.ShortName}"
        : L("bench.title");
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMirrorSurface))]
    [NotifyPropertyChangedFor(nameof(ShowHubSurface))]
    private bool _showHub;

    public bool ShowMirrorSurface => Mirrors.Count > 0 && !ShowHub;
    public bool ShowHubSurface => Mirrors.Count == 0 || ShowHub;

    [RelayCommand]
    private void ToggleHub() => ShowHub = !ShowHub;
    public bool CanConnect => !IsBusy && SelectedDevice is { IsReady: true };
    public string ActiveMirrorName => ActiveMirror?.DeviceName ?? "";

    public string CodecShort => VideoCodec switch
    {
        "h265" => "H.265",
        "h264" => "H.264",
        "av1" => "AV1",
        _ => "Auto",
    };

    public string QualityShort =>
        $"{(MaxSize == 0 ? L("opt.natif_max") : MaxSize + "p")} · {MaxFps} fps";

    public string BitRateShort => $"{VideoBitRate / 1_000_000} Mbps";

    public Visibility RecordingVisibility =>
        ActiveMirror?.IsRecording == true ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty] private string _recordingElapsed = "REC";
    [ObservableProperty] private string _sessionElapsed = "00:00:00";
    [ObservableProperty] private string _sessionStats = "";

    public bool StepPluggedDone => Devices.Any(d => !d.IsRememberedOnly);
    public bool StepAuthDone => Devices.Any(d => d.IsReady);

    private readonly DispatcherTimer _recTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    [ObservableProperty] private bool _refreshing;
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
        PluginsView = CollectionViewSource.GetDefaultView(Plugins);
        PluginsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PluginInstance.GroupLabel)));
        PluginsView.SortDescriptions.Add(new SortDescription(nameof(PluginInstance.GroupIndex), ListSortDirection.Ascending));
        PluginsView.SortDescriptions.Add(new SortDescription(nameof(PluginInstance.Name), ListSortDirection.Ascending));
        _recTimer.Tick += (_, _) =>
        {
            var since = ActiveMirror?.RecordingSince;
            RecordingElapsed = since.HasValue ? $"REC {(DateTime.Now - since.Value):m\\:ss}" : "REC";
            var cs = ActiveMirror?.ConnectedSince;
            SessionElapsed = cs.HasValue ? (DateTime.Now - cs.Value).ToString(@"hh\:mm\:ss") : "00:00:00";
            var view = ActiveMirror?.View;
            SessionStats = view != null && view.VideoWidth > 0
                ? $"{view.VideoWidth}×{view.VideoHeight} · {view.CurrentFps:0} fps · {BitRateShort} · {CodecShort}"
                : $"{QualityShort} · {BitRateShort} · {CodecShort}";
        };
        _recTimer.Start();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _pollTimer.Tick += async (_, _) =>
        {
            if (!IsBusy && !Refreshing)
                await RefreshDevicesAsync();
        };

        _settings = SettingsStore.Load();
        LocalizationService.Instance.Load(_settings.Language);
        Status = L("st.select_device");
        DeviceThumbs.IsBusy = d => Mirrors.Any(m => m.Device.SharesIdentity(d));
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
        VideoFxaa = _settings.VideoFxaa;
        VideoBrightness = _settings.VideoBrightness;
        VideoContrast = _settings.VideoContrast;
        VideoSaturation = _settings.VideoSaturation;
        VideoVibrance = _settings.VideoVibrance;
        VideoVignette = _settings.VideoVignette;
        VideoGamma = _settings.VideoGamma;
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
        if (ActivePrefs() == null)
        {
            _settings.MaxSize = MaxSize;
            _settings.MaxFps = MaxFps;
            _settings.VideoBitRate = VideoBitRate;
            _settings.VideoCodec = VideoCodec;
            _settings.VideoDecoder = VideoDecoder;
            _settings.EnableAudio = EnableAudio;
            _settings.TurnScreenOff = TurnScreenOff;
        }
        _settings.VideoSharpen = VideoSharpen;
        _settings.VideoFxaa = VideoFxaa;
        _settings.VideoBrightness = VideoBrightness;
        _settings.VideoContrast = VideoContrast;
        _settings.VideoSaturation = VideoSaturation;
        _settings.VideoVibrance = VideoVibrance;
        _settings.VideoVignette = VideoVignette;
        _settings.VideoGamma = VideoGamma;
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
        foreach (var m in Mirrors)
        {
            if (m.Prefs is { } wd && ActiveWorkspace?.Model.Devices.Contains(wd) != true)
            {
                if (!_settings.Devices.TryGetValue(m.IdentityKey, out var dp))
                    _settings.Devices[m.IdentityKey] = dp = new DevicePrefs();
                dp.MaxSize = wd.MaxSize;
                dp.MaxFps = wd.MaxFps;
                dp.VideoBitRate = wd.VideoBitRate;
                dp.VideoCodec = wd.VideoCodec;
                dp.VideoDecoder = wd.VideoDecoder;
                dp.EnableAudio = wd.EnableAudio;
                dp.TurnScreenOff = wd.TurnScreenOff;
                dp.NewDisplay = wd.NewDisplay;
                dp.AdaptiveBitrate = wd.AdaptiveBitrate;
            }
        }
        SettingsStore.Save(_settings);
        if (LocalApiEnabled && _apiServer is { Port: { } p } && p != LocalApiPort)
            AppLogger.Forget(RestartApiAsync());
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityShort))]
    [NotifyPropertyChangedFor(nameof(SessionStats))]
    private int _maxSize = 0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityShort))]
    [NotifyPropertyChangedFor(nameof(SessionStats))]
    private int _maxFps = 60;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitRateShort))]
    [NotifyPropertyChangedFor(nameof(SessionStats))]
    private int _videoBitRate = 16_000_000;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CodecShort))]
    [NotifyPropertyChangedFor(nameof(SessionStats))]
    private string _videoCodec = "auto";
    [ObservableProperty] private string _videoDecoder = "gpu";
    [ObservableProperty] private bool _videoSharpen;
    [ObservableProperty] private bool _videoFxaa;
    [ObservableProperty] private double _videoBrightness;
    [ObservableProperty] private double _videoContrast = 1;
    [ObservableProperty] private double _videoSaturation = 1;
    [ObservableProperty] private double _videoVibrance;
    [ObservableProperty] private double _videoVignette;
    [ObservableProperty] private double _videoGamma = 1;
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateActionLabel))]
    private bool _updateSelfUpdate;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateActionLabel))]
    [NotifyPropertyChangedFor(nameof(UpdateBusy))]
    private bool _updateReady;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateActionLabel))]
    [NotifyPropertyChangedFor(nameof(UpdateBusy))]
    [NotifyPropertyChangedFor(nameof(UpdateProgressText))]
    private int _updateProgress = -1;
    public bool UpdateBusy => UpdateProgress >= 0;
    public string UpdateProgressText => UpdateProgress >= 0 ? $"· {UpdateProgress} %" : "";
    public string UpdateActionLabel => UpdateReady ? L("maj_redemarrer")
        : UpdateSelfUpdate ? L("maj_installer") : L("telecharger");
    public string UpdateSubtitle =>
        L(UpdateSelfUpdate ? "maj_sous_delta" : "maj_sous_web");

    [ObservableProperty] private ObservableCollection<string> _logs = new();

    private bool _hasError;
    public System.Windows.Media.Brush StatusDotColor => _hasError
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C))
        : IsConnected
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x9A, 0x6F))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0x8A));

    public event Action<MirrorInstance>? MirrorAdded;
    public event Func<MirrorInstance, string?>? ScreenshotRequested;
    public event Action? AnyConnected;

    partial void OnActiveMirrorChanged(MirrorInstance? oldValue, MirrorInstance? newValue)
    {
        if (!ReferenceEquals(oldValue, newValue))
            oldValue?.View.ReleaseHeldKeys();
    }

    public void SetActive(MirrorInstance instance)
    {
        InactiveMirrors.Remove(instance);
        ActiveMirror = instance;
        ShowHub = false;
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
        var d = Devices.FirstOrDefault(x => x.MatchesSerial(serial)
            || x.DeviceKey == serial || x.HardwareSerial == serial);
        if (d == null)
        {
            await RefreshDevicesAsync();
            d = Devices.FirstOrDefault(x => x.MatchesSerial(serial)
                || x.DeviceKey == serial || x.HardwareSerial == serial);
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

    private WorkspaceDevice DetachedPrefs(AdbDevice device, int? accountUserId)
    {
        var key = IdentityKeyOf(device, accountUserId);
        _settings.Devices.TryGetValue(key, out var dp);
        return new WorkspaceDevice
        {
            DeviceKey = key,
            Model = device.Model,
            LastSerial = device.Serial,
            AccountUserId = accountUserId,
            MaxSize = dp?.MaxSize,
            MaxFps = dp?.MaxFps,
            VideoBitRate = dp?.VideoBitRate,
            VideoCodec = dp?.VideoCodec,
            VideoDecoder = dp?.VideoDecoder,
            EnableAudio = dp?.EnableAudio,
            TurnScreenOff = dp?.TurnScreenOff,
            NewDisplay = dp?.NewDisplay,
            AdaptiveBitrate = dp?.AdaptiveBitrate
        };
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
                var prev = m.Prefs;
                next.Add(new WorkspaceDevice
                {
                    DeviceKey = m.IdentityKey,
                    Model = m.Device.Model,
                    LastSerial = m.Device.Serial,
                    AccountUserId = m.AccountUserId,
                    AccountName = m.AccountName,
                    MaxSize = prev?.MaxSize,
                    MaxFps = prev?.MaxFps,
                    VideoBitRate = prev?.VideoBitRate,
                    VideoCodec = prev?.VideoCodec,
                    VideoDecoder = prev?.VideoDecoder,
                    EnableAudio = prev?.EnableAudio,
                    TurnScreenOff = prev?.TurnScreenOff,
                    NewDisplay = prev?.NewDisplay,
                    AdaptiveBitrate = prev?.AdaptiveBitrate
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
                    && !_voluntaryDisconnects.Contains(wd.DeviceKey) && !_failedReconnects.Contains(wd.DeviceKey))
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

    public void SetDevicePinned(AdbDevice device, bool pinned)
    {
        var key = device.DeviceKey;
        if (!_settings.Devices.TryGetValue(key, out var prefs))
            _settings.Devices[key] = prefs = new DevicePrefs();
        prefs.Pinned = pinned;
        prefs.Model = device.Model;
        prefs.LastSerial = device.Serial;
        for (var i = 0; i < Devices.Count; i++)
            if (Devices[i].SharesIdentity(device))
                Devices[i] = Devices[i] with { Pinned = pinned };
        var order = Devices.OrderByDescending(d => d.Pinned).ToList();
        for (var i = 0; i < order.Count; i++)
        {
            var cur = Devices.IndexOf(order[i]);
            if (cur != i)
                Devices.Move(cur, i);
        }
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
    public ICollectionView PluginsView { get; }

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

    public MarketplaceItem? FeaturedPlugin => Catalog.FirstOrDefault(c => c.Featured);

    private bool CatalogPredicate(object o)
    {
        if (o is not MarketplaceItem m)
            return false;
        if (m == FeaturedPlugin && string.IsNullOrWhiteSpace(CatalogFilter))
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
            OnPropertyChanged(nameof(FeaturedPlugin));
            CatalogView.Refresh();
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
        OnPropertyChanged(nameof(ShowMirrorSurface));
        OnPropertyChanged(nameof(ShowHubSurface));
        OnPropertyChanged(nameof(StatusDotColor));
        OnPropertyChanged(nameof(RecordingVisibility));
        for (var i = 0; i < Devices.Count; i++)
        {
            var mir = Mirrors.Any(m => m.Device.SharesIdentity(Devices[i]));
            if (Devices[i].IsMirrored != mir)
                Devices[i] = Devices[i] with { IsMirrored = mir };
        }
    }

    private async Task<string?> ResolveAccountDisplayAsync(AdbDevice device, WorkspaceDevice? prefs)
    {
        if (!string.IsNullOrEmpty(prefs?.NewDisplay))
            return prefs.NewDisplay;
        var (w, h) = await AdbService.GetScreenSizeAsync(device.Serial);
        if (w <= 0 || h <= 0)
            return null;
        var dpi = await AdbService.GetScreenDensityAsync(device.Serial);
        return $"{Math.Max(w, h)}x{Math.Min(w, h)}/{(dpi > 0 ? dpi : 420)}";
    }

    private EngineOptions BuildOptions(WorkspaceDevice? o = null, MirrorAccount? account = null,
        string? accountDisplay = null) => new()
    {
        MaxSize = o?.MaxSize ?? _settings.MaxSize,
        MaxFps = o?.MaxFps ?? _settings.MaxFps,
        VideoBitRate = o?.VideoBitRate ?? _settings.VideoBitRate,
        VideoCodec = o?.VideoCodec ?? _settings.VideoCodec,
        VideoDecoder = o?.VideoDecoder ?? _settings.VideoDecoder,
        VideoSharpen = VideoSharpen,
        VideoFxaa = VideoFxaa,
        VideoBrightness = VideoBrightness,
        VideoContrast = VideoContrast,
        VideoSaturation = VideoSaturation,
        VideoVibrance = VideoVibrance,
        VideoVignette = VideoVignette,
        VideoGamma = VideoGamma,
        StayAwake = StayAwake,
        Audio = o?.EnableAudio ?? _settings.EnableAudio,
        TurnScreenOff = o?.TurnScreenOff ?? _settings.TurnScreenOff,
        NewDisplay = account != null
            ? (string.IsNullOrEmpty(o?.NewDisplay) ? (accountDisplay ?? "1920x1200/280") : o.NewDisplay)
            : (o?.NewDisplay ?? _settings.NewDisplay),
        AutoLaunchPackage = account != null
            ? $"com.ankama.dofustouch@{account.UserId}"
            : AutoLaunchDofus ? "com.ankama.dofustouch" : null,
        AdaptiveBitrate = o?.AdaptiveBitrate ?? _settings.AdaptiveBitrate,
        ClipboardAutosync = SyncDeviceClipboard,
    };

    private WorkspaceDevice? ActivePrefs() => SettingsScopeGlobal ? null : ActiveMirror?.Prefs;

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
        if (o != null && ActiveMirror != null)
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
        OnPropertyChanged(nameof(HasDeviceScope));
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

    partial void OnVideoFxaaChanged(bool value)
    {
        foreach (var m in Mirrors)
            if (m.Decoder?.GpuPresenter is { } p)
            {
                p.Fxaa = value;
                p.Redraw();
            }
        ScheduleSave();
    }

    private void ApplyColorAdjust()
    {
        foreach (var m in Mirrors)
            if (m.Decoder?.GpuPresenter is { } p)
            {
                p.SetColorAdjust((float)VideoBrightness, (float)VideoContrast,
                    (float)VideoSaturation);
                p.SetEffects((float)VideoVibrance, (float)VideoVignette, (float)VideoGamma);
                p.Redraw();
            }
        ScheduleSave();
    }

    partial void OnVideoBrightnessChanged(double value) => ApplyColorAdjust();
    partial void OnVideoContrastChanged(double value) => ApplyColorAdjust();
    partial void OnVideoSaturationChanged(double value) => ApplyColorAdjust();
    partial void OnVideoVibranceChanged(double value) => ApplyColorAdjust();
    partial void OnVideoVignetteChanged(double value) => ApplyColorAdjust();
    partial void OnVideoGammaChanged(double value) => ApplyColorAdjust();

    [RelayCommand]
    private void ResetVideoColor()
    {
        VideoBrightness = 0;
        VideoContrast = 1;
        VideoSaturation = 1;
        VideoVibrance = 0;
        VideoVignette = 0;
        VideoGamma = 1;
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
    partial void OnSelectedDeviceChanged(AdbDevice? value)
    {
        ScheduleSave();
        SyncDeviceSelection();
        if (value != null && SetupTarget != null
            && SetupTarget.DeviceKey != value.DeviceKey
            && (SetupKind == "offer" || SetupKind == "busy"))
        {
            _setupOffered.Add(SetupTarget.DeviceKey);
            SetupTarget = null;
            SetupKind = "";
            UpdateSetupOffer(Devices.ToList());
        }
    }
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
        if (m == null || !m.IsConnected || IsBusy || SettingsScopeGlobal)
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
        AppLogger.Forget(RefreshConnectionWarningAsync());
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

    public void StopTracking()
    {
        _pollTimer.Stop();
        _trackCts.Cancel();
        _benchCts?.Cancel();
        DeviceThumbs.Shutdown();
    }

    private async Task CheckUpdateAsync()
    {
        var update = await UpdateService.CheckAsync();
        if (update is { } u)
        {
            UpdateVersion = u.Version.ToString(3);
            UpdateUrl = u.Url;
            UpdateSelfUpdate = u.SelfUpdate;
            OnPropertyChanged(nameof(UpdateUrl));
            OnPropertyChanged(nameof(UpdateSubtitle));
            Log($"Mise à jour disponible : v{UpdateVersion}");
        }
    }

    [RelayCommand]
    private async Task OpenUpdateAsync()
    {
        if (UpdateReady)
        {
            UpdateService.ApplyOnExit();
            Application.Current.MainWindow?.Close();
            return;
        }
        if (UpdateBusy)
            return;
        if (!UpdateSelfUpdate)
        {
            if (!string.IsNullOrEmpty(UpdateUrl))
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(UpdateUrl) { UseShellExecute = true });
            return;
        }
        UpdateProgress = 0;
        try
        {
            await UpdateService.DownloadAsync(new Progress<int>(p => UpdateProgress = p));
            UpdateProgress = -1;
            UpdateReady = true;
        }
        catch
        {
            UpdateProgress = -1;
        }
    }

    [RelayCommand]
    private void DismissUpdate() => UpdateVersion = null;

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        if (Refreshing)
            return;
        Refreshing = true;
        try
        {
            var list = (await AdbService.GetDevicesAsync()).ToList();

            foreach (var st in DimmedScreenStore.Pending())
            {
                var dev = list.FirstOrDefault(d =>
                    d.MatchesSerial(st.Serial)
                    || d.DeviceKey == st.DeviceKey
                    || d.DeviceKey == st.Serial);
                if (dev is not { IsReady: true } || Mirrors.Any(m => m.Device.SharesIdentity(dev)))
                    continue;
                try
                {
                    if (st.StayOn >= 0)
                        await AdbService.SetStayOnWhilePluggedInAsync(dev.Serial, st.StayOn);
                    if (st.Brightness >= 0)
                        await AdbService.SetBrightnessAsync(dev.Serial, st.Brightness);
                    if (st.BrightnessMode >= 0)
                        await AdbService.SetBrightnessModeAsync(dev.Serial, st.BrightnessMode);
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

            _voluntaryDisconnects.RemoveWhere(s =>
            {
                var (baseKey, _) = SplitIdentityKey(s);
                return !list.Any(d => d.DeviceKey == baseKey || d.MatchesSerial(baseKey));
            });

            for (var i = 0; i < list.Count; i++)
            {
                var d = list[i];
                if (!_settings.Devices.TryGetValue(d.DeviceKey, out var prefs))
                    continue;
                prefs.Model = d.Model;
                prefs.LastSerial = d.Serial;
                if ((prefs.CustomName != null && prefs.CustomName != d.CustomName)
                    || prefs.Color != d.Color || prefs.Pinned != d.Pinned)
                    list[i] = d with { CustomName = prefs.CustomName ?? d.CustomName, Color = prefs.Color, Pinned = prefs.Pinned };
            }

            foreach (var (key, prefs) in _settings.Devices)
            {
                var present = list.Any(d => d.DeviceKey == key
                    || (prefs.LastSerial != null && d.MatchesSerial(prefs.LastSerial)));
                if (!present)
                    list.Add(new AdbDevice(prefs.LastSerial ?? key, prefs.Model ?? "",
                        "remembered", CustomName: prefs.CustomName, Color: prefs.Color, Pinned: prefs.Pinned));
            }

            list = list.OrderByDescending(d => d.Pinned).ToList();

            var selKey = SelectedDevice?.DeviceKey;
            for (var i = 0; i < list.Count; i++)
                list[i] = list[i] with
                {
                    IsSelected = selKey != null && list[i].DeviceKey == selKey,
                    IsMirrored = Mirrors.Any(m => m.Device.SharesIdentity(list[i]))
                };

            var known = Devices.Where(d => d.IsReady).ToList();
            foreach (var d in list.Where(d => d.IsReady && !known.Any(k => k.SharesIdentity(d))))
                AddActivity("phone", L("act.device_detected"), d.ShortName);

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
            var current = SelectedDevice != null
                ? list.FirstOrDefault(d => d.SharesIdentity(SelectedDevice) || d.DeviceKey == SelectedDevice.DeviceKey)
                : null;
            SelectedDevice = current
                ?? list.FirstOrDefault(d => d.DeviceKey == _settings.LastSelectedDeviceKey)
                ?? list.FirstOrDefault(d => d.IsReady)
                ?? list.FirstOrDefault();
            SyncDeviceSelection();
            OnPropertyChanged(nameof(DetectedDeviceCount));
            OnPropertyChanged(nameof(StepPluggedDone));
            OnPropertyChanged(nameof(StepAuthDone));
            UpdateSetupOffer(list);
            var detected = list.Count(d => !d.IsRememberedOnly);
            if (!_hasError && !Mirrors.Any(m => m.IsReconnecting))
            {
                if (detected == 0)
                    Status = L("st.no_device");
                else if (!IsConnected)
                    Status = string.Format(L("st.devices_detected"), detected);
            }
            _apiHost.Publish("devices", new { detected });

            _pollTimer.IsEnabled = !_trackCts.IsCancellationRequested && (detected == 0 || list.Any(d => !d.IsReady));
            if (detected == 0)
            {
                if ((DateTime.Now - _usbDiagChecked).TotalSeconds > 4)
                {
                    _usbDiagChecked = DateTime.Now;
                    AppLogger.Forget(RefreshUsbDiagAsync());
                }
            }
            else
            {
                UsbDiagTitle = UsbDiagDetail = null;
            }
            RefreshMissingDevices();
            AppLogger.Forget(TryConnectMissingAsync());
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            _pollTimer.IsEnabled = !_trackCts.IsCancellationRequested;
            AppLogger.Forget(RefreshUsbDiagAsync());
        }
        finally
        {
            Refreshing = false;
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

    [ObservableProperty] private string? _usbDiagTitle;
    [ObservableProperty] private string? _usbDiagDetail;
    private DateTime _usbDiagChecked = DateTime.MinValue;
    private bool _usbDiagRunning;
    [ObservableProperty] private string? _connectionWarning;

    private async Task RefreshUsbDiagAsync()
    {
        if (_usbDiagRunning || _trackCts.IsCancellationRequested)
            return;
        _usbDiagRunning = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_trackCts.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var found = await AdbService.DetectPnpAndroidAsync(timeout.Token);
            if (found.Count == 0 || Devices.Any(d => !d.IsRememberedOnly))
            {
                UsbDiagTitle = UsbDiagDetail = null;
                return;
            }
            UsbDiagTitle = string.Format(L("diag.usb_title"), string.Join(", ", found.Select(d => d.Name).Distinct()));
            var guides = found.Select(d => d.Brand switch
            {
                "Xiaomi" => "diag.usb_xiaomi",
                "Samsung" => "diag.usb_samsung",
                "OnePlus" or "Oppo" or "Vivo" => "diag.usb_oneplus",
                _ => "diag.usb_generic"
            }).Distinct();
            UsbDiagDetail = string.Join("\n", guides.Select(L));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log($"USB diagnostic unavailable: {ex.GetType().Name}"); }
        finally { _usbDiagRunning = false; }
    }

    private async Task RefreshConnectionWarningAsync()
    {
        var names = await Task.Run(() => AdbService.FindCompetingProcesses().Select(p => p.Name).Distinct().ToArray());
        if (!_trackCts.IsCancellationRequested)
            ConnectionWarning = names.Length == 0 ? null : string.Format(L("diag.other_apps"), string.Join(", ", names));
    }

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

        var candidate = list.Where(d => d.IsReady
                && !_setupOffered.Contains(d.DeviceKey)
                && !_profilesChecked.Contains(d.DeviceKey)
                && !_settings.SetupDismissed.Contains(d.DeviceKey))
            .OrderByDescending(d => SelectedDevice != null && d.DeviceKey == SelectedDevice.DeviceKey)
            .FirstOrDefault();
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

    private void SyncDeviceSelection()
    {
        var key = SelectedDevice?.DeviceKey;
        for (var i = 0; i < Devices.Count; i++)
        {
            var d = Devices[i];
            var sel = key != null && d.DeviceKey == key;
            if (d.IsSelected != sel)
                Devices[i] = d with { IsSelected = sel };
            if (sel && !ReferenceEquals(Devices[i], SelectedDevice))
                SelectedDevice = Devices[i];
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
        _failedReconnects.Remove(IdentityKeyOf(device, account?.UserId));
        AppLogger.Forget(RefreshConnectionWarningAsync());
        var existing = Mirrors.FirstOrDefault(m => m.Device.SharesIdentity(device)
            && m.AccountUserId == account?.UserId);
        if (existing != null)
        {
            SetActive(existing);
            return;
        }

        prefs ??= FindWorkspaceDevice(device, account?.UserId)
            ?? DetachedPrefs(device, account?.UserId);
        IsBusy = true;
        _hasError = false;
        OnPropertyChanged(nameof(StatusDotColor));
        Status = account == null
            ? string.Format(L("st.connecting"), device.DisplayName)
            : string.Format(L("st.connecting_account"), device.DisplayName, account.Name);
        var instance = new MirrorInstance(device)
        {
            Prefs = prefs,
            AccountUserId = account?.UserId,
            AccountName = account?.Name,
            AccentHex = _settings.Devices.TryGetValue(device.DeviceKey, out var dp) ? dp.Color : null
        };
        instance.ShouldSyncClipboard = () => SyncDeviceClipboard && instance.IsActive;
        if (account != null)
            instance.DeviceName = $"{device.ShortName} · {account.Name}";
        try
        {
            WireMirror(instance);
            Services.AppLogger.Write("startasync begin");
            var accountDisplay = account != null
                ? await ResolveAccountDisplayAsync(device, prefs)
                : null;
            var options = BuildOptions(prefs, account, accountDisplay);
            if (account != null)
                await AdbService.StartUserAsync(device.Serial, account.UserId);
            await instance.StartAsync(options);
            Services.AppLogger.Write("startasync done");
            RememberDevice(device);
            BindKeybindPersistence(instance);
        }
        catch (OperationCanceledException) when (instance.ManualDisconnect) { }
        catch (Exception ex)
        {
            Log(ex.ToString());
            _hasError = true;
            _failedReconnects.Add(instance.IdentityKey);
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
        try
        {
            var owned = _settings.Devices.TryGetValue(device.DeviceKey, out var dp)
                ? dp.OwnedUserIds : new List<int>();
            return (await AdbService.ListProfilesAsync(device.Serial))
                .Select(p => p with { Owned = owned.Contains(p.Id) }).ToList();
        }
        catch (Exception ex)
        {
            Log($"profiles: {ex.Message}");
            return new List<AndroidProfile>();
        }
    }

    private void MarkOwnedProfile(AdbDevice device, int userId)
    {
        if (!_settings.Devices.TryGetValue(device.DeviceKey, out var dp))
            _settings.Devices[device.DeviceKey] = dp = new DevicePrefs();
        if (!dp.OwnedUserIds.Contains(userId))
        {
            dp.OwnedUserIds.Add(userId);
            SaveNow();
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
            MarkOwnedProfile(device, userId);
            AddActivity("person", L("act.account_created"), device.ShortName);
            try
            {
                await AdbService.InstallAppForUserAsync(device.Serial, userId, "com.ankama.dofustouch");
                await AdbService.StartUserAsync(device.Serial, userId);
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                Status = string.Format(L("st.account_partial"), name, ex.Message);
                return;
            }
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
        if (!profile.Owned)
        {
            Status = L("st.profile_foreign");
            return;
        }
        var tile = Mirrors.FirstOrDefault(m => m.AccountUserId == profile.Id
            && m.Device.SharesIdentity(device));
        if (tile != null)
            await RemoveMirrorInternalAsync(tile);
        try
        {
            await AdbService.RemoveUserProfileAsync(device.Serial, profile.Id);
            if (_settings.Devices.TryGetValue(device.DeviceKey, out var dp)
                && dp.OwnedUserIds.Remove(profile.Id))
                SaveNow();
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
            instance.BleStatusChanged += s =>
            {
                Status = s;
                OnPropertyChanged(nameof(IosBleActive));
            };
            WireMirror(instance, () => StopAirPlayIfUnused());
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
    private readonly HashSet<string> _failedReconnects = new(StringComparer.OrdinalIgnoreCase);

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
                LoadEffectiveSettings();
                UpdateStatus();
            }
        }
        else
        {
            RefreshInactiveMirrors();
            UpdateStatus();
        }
    }

    private void WireMirror(MirrorInstance instance, Action? onDisconnected = null)
    {
        instance.Log += Log;
        instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MirrorInstance.ReconnectStatus) && instance.ReconnectStatus is { } message)
            {
                if (ReferenceEquals(ActiveMirror, instance))
                    Status = $"{instance.Device.ShortName} : {message}";
                AppLogger.Forget(RefreshConnectionWarningAsync());
            }
        };
        instance.Connected += m =>
        {
            if (!m.IsReconnecting)
            {
                SetActive(m);
                AnyConnected?.Invoke();
            }
            else if (ReferenceEquals(ActiveMirror, m))
                UpdateStatus();
            AddActivity("play", L("act.mirror_started"), m.Device.ShortName);
            _apiHost.Publish("mirror.connected",
                new { slot = m.Slot, name = m.DeviceName, serial = m.Device.Serial });
        };
        instance.Disconnected += m =>
        {
            AddActivity("dismiss", L("act.mirror_stopped"), m.Device.ShortName);
            _apiHost.Publish("mirror.disconnected",
                new { name = m.DeviceName, serial = m.Device.Serial, manual = m.ManualDisconnect });
            Mirrors.Remove(m);
            PromoteNextActive(m);
            if (m.UnexpectedDeath)
            {
                _failedReconnects.Add(m.IdentityKey);
                Status = string.Format(L(m.LastDeviceState switch
                {
                    "unauthorized" => "st.authorize",
                    "offline" => "st.offline",
                    "missing" => "st.not_detected",
                    _ => "st.link_reset"
                }), m.Device.ShortName);
                AppLogger.Forget(RefreshConnectionWarningAsync());
                _hasError = true;
                OnPropertyChanged(nameof(StatusDotColor));
            }
            onDisconnected?.Invoke();
        };
        instance.OverlayLineClicked += (m, id, idx) =>
            _apiHost.Publish("overlay.line",
                new { slot = m.Slot, id, index = idx });

        Mirrors.Add(instance);
        ApplyMirrorOrder();
        RefreshInactiveMirrors();
        SetActive(instance);
        MirrorAdded?.Invoke(instance);
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
            AddActivity("wifi", L("act.wifi_enabled"), wifiDevice.ShortName);
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

    [ObservableProperty] private bool _pairOpen;
    [ObservableProperty] private byte[]? _pairQrPng;
    [ObservableProperty] private string _pairStatus = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PairIdle))]
    private bool _pairBusy;
    public bool PairIdle => !PairBusy;
    [ObservableProperty] private string _pairAddress = "";
    [ObservableProperty] private string _pairCode = "";
    [ObservableProperty] private bool _debugOpen;
    [ObservableProperty] private string _debugReport = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DebugIdle))]
    private bool _debugBusy;
    public bool DebugIdle => !DebugBusy;
    [ObservableProperty] private string _debugNote = "";
    [ObservableProperty] private bool _diagFixAdbVisible;
    [ObservableProperty] private bool _diagFixReauthVisible;
    [ObservableProperty] private bool _diagFixKillVisible;
    [ObservableProperty] private string _diagFixKillLabel = "";
    [ObservableProperty] private bool _diagFixFwVisible;
    [ObservableProperty] private bool _diagFixAllVisible;
    [ObservableProperty] private string _diagBenchText = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BenchIdle))]
    private bool _diagBenchBusy;
    public bool BenchIdle => !DiagBenchBusy;
    [ObservableProperty] private bool _diagGuideVisible;
    [ObservableProperty] private bool _diagGuideOpen;
    [ObservableProperty] private string _diagGuideTitle = "";
    [ObservableProperty] private string _diagGuideText = "";
    [ObservableProperty] private bool _diagDevmgmtVisible;

    private QrPairSession? _pairSession;

    [RelayCommand]
    private void OpenPairing()
    {
        PairOpen = true;
        StartQrSession();
    }

    [RelayCommand]
    private void ClosePairing()
    {
        _pairSession?.Dispose();
        _pairSession = null;
        PairOpen = false;
        PairBusy = false;
        PairStatus = "";
    }

    [RelayCommand]
    private void RegeneratePairQr() => StartQrSession();

    private void StartQrSession()
    {
        _pairSession?.Dispose();
        var session = new QrPairSession();
        _pairSession = session;
        PairQrPng = RenderQr(session.Payload);
        PairStatus = L("pair.waiting");
        _ = RunQrSessionAsync(session);
    }

    private static byte[] RenderQr(string payload)
    {
        var data = new QRCoder.QRCodeGenerator().CreateQrCode(payload, QRCoder.QRCodeGenerator.ECCLevel.Q);
        return new QRCoder.PngByteQRCode(data).GetGraphic(9);
    }

    private async Task RunQrSessionAsync(QrPairSession session)
    {
        try
        {
            await session.RunAsync(async (state, detail) =>
            {
                if (session != _pairSession)
                    return;
                AppLogger.Write($"pairing : {state} {detail}");
                switch (state)
                {
                    case QrPairState.Found:
                        PairStatus = string.Format(L("pair.found"), detail);
                        break;
                    case QrPairState.Pairing:
                        PairBusy = true;
                        PairStatus = L("pair.pairing");
                        break;
                    case QrPairState.Done:
                        PairBusy = false;
                        PairStatus = L("pair.done");
                        AddActivity("wifi", L("act.paired"), detail ?? "");
                        await RefreshDevicesAsync();
                        await Task.Delay(1600);
                        if (session == _pairSession)
                            ClosePairing();
                        break;
                    case QrPairState.Failed:
                        PairBusy = false;
                        PairStatus = string.Format(L("pair.fail"), detail);
                        break;
                    case QrPairState.Timeout:
                        PairBusy = false;
                        PairStatus = L("pair.timeout");
                        break;
                }
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            PairBusy = false;
            if (session == _pairSession)
                PairStatus = string.Format(L("pair.fail"), ex.Message);
        }
    }

    [RelayCommand]
    private async Task PairManualAsync()
    {
        if (PairBusy)
            return;
        PairBusy = true;
        try
        {
            var result = await AdbService.PairAsync(PairAddress.Trim(), PairCode.Trim());
            PairStatus = L("pair.done");
            AddActivity("wifi", L("act.paired"), PairAddress.Trim());
            await RefreshDevicesAsync();
            await Task.Delay(1600);
            if (PairOpen)
                ClosePairing();
            _ = result;
        }
        catch (Exception ex)
        {
            PairStatus = string.Format(L("pair.fail"), ex.Message);
        }
        finally
        {
            PairBusy = false;
        }
    }

    [RelayCommand]
    private async Task PairConnectAsync()
    {
        if (PairBusy)
            return;
        PairBusy = true;
        try
        {
            await AdbService.ConnectAsync(PairAddress.Trim());
            PairStatus = L("pair.connected");
            await RefreshDevicesAsync();
            await Task.Delay(1200);
            if (PairOpen)
                ClosePairing();
        }
        catch (Exception ex)
        {
            PairStatus = string.Format(L("pair.fail"), ex.Message);
        }
        finally
        {
            PairBusy = false;
        }
    }

    [RelayCommand]
    private void OpenDebug()
    {
        DebugOpen = true;
        _ = BuildDebugReportAsync();
    }

    [RelayCommand]
    private void CloseDebug() => DebugOpen = false;

    [RelayCommand]
    private async Task RefreshDebugAsync() => await BuildDebugReportAsync();

    [RelayCommand]
    private void CopyDebug()
    {
        if (string.IsNullOrEmpty(DebugReport))
            return;
        try
        {
            System.Windows.Clipboard.SetText(DebugReport);
            DebugNote = L("dbg.copied");
        }
        catch (Exception ex)
        {
            DebugNote = ex.Message;
        }
        _ = ClearDebugNoteAsync();
    }

    [RelayCommand]
    private void CopyDebugMasked()
    {
        if (string.IsNullOrEmpty(DebugReport))
            return;
        try
        {
            System.Windows.Clipboard.SetText(MaskReport(DebugReport));
            DebugNote = L("dbg.masked");
        }
        catch (Exception ex)
        {
            DebugNote = ex.Message;
        }
        _ = ClearDebugNoteAsync();
    }

    [RelayCommand]
    private void PurgeThumbs()
    {
        DeviceThumbs.ClearAll();
        DebugNote = L("dbg.thumbs_purged");
        _ = ClearDebugNoteAsync();
    }

    private string MaskReport(string report)
    {
        var masked = System.Text.RegularExpressions.Regex.Replace(
            report, @"\b(\d{1,3})\.(\d{1,3})\.\d{1,3}\.\d{1,3}\b", "$1.$2.×.×");
        masked = System.Text.RegularExpressions.Regex.Replace(
            masked, @"\b(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}\b", "xx:xx:xx:xx:xx:xx");
        var user = Environment.UserName;
        if (user.Length > 0)
            masked = masked.Replace($@"C:\Users\{user}", @"C:\Users\…",
                StringComparison.OrdinalIgnoreCase);
        foreach (var s in Devices.SelectMany(d => new[] { d.Serial, d.HardwareSerial })
                     .Concat(Mirrors.Select(m => m.Device.Serial))
                     .Concat(Mirrors.Select(m => m.Device.HardwareSerial))
                     .Where(x => !string.IsNullOrEmpty(x)).Distinct())
            masked = masked.Replace(s!, "«serial»");
        return masked;
    }

    private async Task ClearDebugNoteAsync()
    {
        await Task.Delay(2500);
        DebugNote = "";
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe",
                System.IO.Path.GetDirectoryName(AppLogger.LogFilePath)!);
        }
        catch (Exception ex) { DebugNote = ex.Message; }
    }

    private string GuideFor(string brand) => brand switch
    {
        "Xiaomi" => L("dbg.guide.xiaomi"),
        "Samsung" => L("dbg.guide.samsung"),
        "Oppo" or "OnePlus" => L("dbg.guide.oppo"),
        "Vivo" => L("dbg.guide.vivo"),
        "Huawei" => L("dbg.guide.huawei"),
        _ => L("dbg.guide.generic")
    };

    [RelayCommand]
    private async Task DiagFixAdbAsync()
    {
        try
        {
            await AdbService.RunTextAsync("kill-server");
            var check = await AdbService.RunTextAsync("devices");
            if (!check.Contains("List of devices", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("adb ne répond pas après redémarrage");
            DebugNote = L("dbg.fix_done");
        }
        catch (Exception ex) { DebugNote = ex.Message; }
        await BuildDebugReportAsync();
        _ = ClearDebugNoteAsync();
    }

    public Func<IReadOnlyList<string>, Task<bool>>? ConfirmKill { get; set; }

    [RelayCommand]
    private async Task DiagFixKillAsync()
    {
        var targets = AdbService.FindCompetingProcesses()
            .Where(p => p.Name.Equals("adb", StringComparison.OrdinalIgnoreCase)).ToList();
        if (targets.Count == 0)
        {
            DebugNote = L("dbg.no_kill_target");
            _ = ClearDebugNoteAsync();
            return;
        }
        if (ConfirmKill != null && !await ConfirmKill(
                targets.Select(t => $"{t.Name} — {t.Path ?? "?"} (pid {t.Pid})").ToList()))
            return;
        var n = 0;
        foreach (var p in targets)
            try { System.Diagnostics.Process.GetProcessById(p.Pid).Kill(); n++; } catch { }
        DebugNote = string.Format(L("dbg.killed"), n);
        await Task.Delay(800);
        await BuildDebugReportAsync();
        _ = ClearDebugNoteAsync();
    }

    [RelayCommand]
    private async Task DiagFixReauthAsync()
    {
        try { await AdbService.ProbeAsync("reconnect"); DebugNote = L("dbg.reauth_hint"); }
        catch (Exception ex) { DebugNote = ex.Message; }
        _ = ClearDebugNoteAsync();
    }

    [RelayCommand]
    private async Task DiagFixFwAsync()
    {
        var exe = Environment.ProcessPath;
        if (exe == null) return;
        await FirewallHelper.EnsureRulesAsync(new List<(string, string)> { (exe, "TouchMirror") });
        await BuildDebugReportAsync();
        DebugNote = L("dbg.fix_done");
        _ = ClearDebugNoteAsync();
    }

    [RelayCommand]
    private async Task DiagFixAllAsync()
    {
        var kill = DiagFixKillVisible; var adb = DiagFixAdbVisible;
        var reauth = DiagFixReauthVisible; var fw = DiagFixFwVisible;
        if (kill) await DiagFixKillAsync();
        if (adb) await DiagFixAdbAsync();
        if (reauth) await DiagFixReauthAsync();
        if (fw) await DiagFixFwAsync();
    }

    [ObservableProperty] private bool _benchOpen;
    [ObservableProperty] private int _benchSeconds;
    [ObservableProperty] private string _benchLive = "";
    [ObservableProperty] private string _benchBigFps = "";
    [ObservableProperty] private string _benchRange = "";
    [ObservableProperty] private string _benchAxisEnd = "30 s";
    [ObservableProperty] private string _benchMbps = "";
    [ObservableProperty] private string _benchLat = "";
    [ObservableProperty] private string _benchAdb = "";
    [ObservableProperty] private string _benchCpu = "";
    [ObservableProperty] private bool _benchHasResult;
    [ObservableProperty] private System.Windows.Media.PointCollection _benchFpsPoints = new();
    [ObservableProperty] private System.Windows.Media.PointCollection _benchFillPoints = new();
    private CancellationTokenSource? _benchCts;

    [RelayCommand]
    private void OpenBench()
    {
        DebugOpen = false;
        BenchOpen = true;
    }

    [RelayCommand]
    private void CloseBench()
    {
        _benchCts?.Cancel();
        BenchOpen = false;
    }

    [RelayCommand]
    private void CancelBench() => _benchCts?.Cancel();

    private void PushBenchCurve(List<double> fps)
    {
        var maxF = Math.Max(1, fps.Max());
        var line = new System.Windows.Media.PointCollection();
        var fill = new System.Windows.Media.PointCollection();
        for (var j = 0; j < fps.Count; j++)
        {
            var x = j * (392.0 / 29);
            var y = 66 - fps[j] / maxF * 56;
            line.Add(new System.Windows.Point(x, y));
            fill.Add(new System.Windows.Point(x, y));
        }
        if (fill.Count > 0)
        {
            fill.Add(new System.Windows.Point(fill[^1].X, 72));
            fill.Add(new System.Windows.Point(0, 72));
        }
        BenchFpsPoints = line;
        BenchFillPoints = fill;
    }

    [RelayCommand]
    private async Task RunBenchAsync()
    {
        var m = ActiveMirror;
        if (m?.Session == null || !m.IsConnected)
        {
            BenchLive = L("dbg.bench_need");
            return;
        }
        DiagBenchBusy = true;
        DiagBenchText = "";
        BenchLive = "";
        BenchSeconds = 0;
        BenchHasResult = false;
        BenchMbps = BenchLat = BenchAdb = BenchCpu = "";
        BenchFpsPoints = new();
        BenchFillPoints = new();
        BenchAxisEnd = "30 s";
        _benchCts = new CancellationTokenSource();
        var ct = _benchCts.Token;
        GpuSampler? gpu = null;
        try
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            var cores = Math.Max(1, Environment.ProcessorCount);
            double adbRtt = -1;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (var i = 0; i < 3; i++)
                    await AdbService.ProbeAsync($"-s {m.Device.Serial} get-state", ct);
                sw.Stop();
                adbRtt = sw.Elapsed.TotalMilliseconds / 3;
            }
            catch { }
            gpu = GpuSampler.TryCreate(proc.Id);
            gpu?.NextUtil();

            var fps = new List<double>();
            var mbps = new List<double>();
            var lag = new List<double>();
            var jit = new List<double>();
            var gpuU = new List<double>();
            var f0 = m.VideoFrames;
            var b0 = m.Session?.RxBytes ?? 0;
            var cpu0 = proc.TotalProcessorTime;
            var t0 = Environment.TickCount64;
            var tPrev = t0;
            for (var i = 0; i < 30; i++)
            {
                try { await Task.Delay(1000, ct); }
                catch (OperationCanceledException) { break; }
                var tn = Environment.TickCount64;
                var dt = Math.Max(0.001, (tn - tPrev) / 1000.0);
                tPrev = tn;
                var f1 = m.VideoFrames;
                var b1 = m.Session?.RxBytes ?? b0;
                fps.Add((f1 - f0) / dt);
                mbps.Add((b1 - b0) * 8.0 / 1e6 / dt);
                lag.Add(m.StreamLagMs);
                jit.Add(m.StreamJitterMs);
                if (gpu != null)
                    gpuU.Add(gpu.NextUtil());
                f0 = f1;
                b0 = b1;
                BenchSeconds = i + 1;
                BenchBigFps = $"{fps[^1]:0}";
                BenchRange = $"{BenchSeconds} s / 30 s";
                BenchLive = $"{mbps[^1]:0.0} Mbps";
                PushBenchCurve(fps);
                if (m.Session == null || !m.IsConnected)
                    break;
            }
            var secs = Math.Max(1, (Environment.TickCount64 - t0) / 1000.0);
            var cpu = (proc.TotalProcessorTime - cpu0).TotalMilliseconds / (secs * 1000 * cores) * 100;
            var secsR = Math.Round(secs);
            BenchAxisEnd = $"{secsR:0} s";

            var sb = new System.Text.StringBuilder();
            sb.Append("== bench ").Append(m.Device.ShortName).Append(" — ").Append(secsR).Append(" s ==").Append('\n');
            if (fps.Count > 0)
            {
                sb.Append($"fps : moy {fps.Average():0} · min {fps.Min():0} · max {fps.Max():0}").Append('\n');
                BenchBigFps = $"{fps.Average():0}";
                BenchRange = $"min {fps.Min():0} · max {fps.Max():0}\nsur {secsR:0} s";
            }
            if (mbps.Count > 0)
            {
                sb.Append($"débit reçu : moy {mbps.Average():0.0} Mbps · pic {mbps.Max():0.0} Mbps").Append('\n');
                BenchMbps = $"moy {mbps.Average():0.0} Mbps · pic {mbps.Max():0.0}";
            }
            if (lag.Count > 0)
            {
                var lagAvg = lag.Average();
                sb.Append(lagAvg < 0
                    ? $"latence flux : 0 ms (flux en avance) · jitter {jit.Average():0} ms"
                    : $"latence flux : {lagAvg:0} ms · jitter {jit.Average():0} ms").Append('\n');
                BenchLat = lagAvg < 0
                    ? $"0 ms (flux en avance) · jitter {jit.Average():0} ms"
                    : $"{lagAvg:0} ms · jitter {jit.Average():0} ms";
            }
            if (adbRtt >= 0)
            {
                sb.Append($"adb : {adbRtt:0} ms aller-retour (processus inclus)").Append('\n');
                BenchAdb = $"{adbRtt:0} ms";
            }
            sb.Append($"cpu app : {cpu:0.0}% de la machine").Append('\n');
            var gpuTxt = gpuU.Count > 1 && gpu != null
                ? $"gpu : moy {gpuU.Skip(1).DefaultIfEmpty(0).Average():0}% · vram {gpu.VramMB():0} Mo"
                : "gpu : n/d";
            sb.Append(gpuTxt);
            BenchCpu = gpuU.Count > 1 && gpu != null
                ? $"{cpu:0.0} % · {gpuU.Skip(1).DefaultIfEmpty(0).Average():0} % ({gpu.VramMB():0} Mo vram)"
                : $"{cpu:0.0} % · gpu n/d";
            BenchLive = "";
            DiagBenchText = sb.ToString();
            BenchHasResult = true;
        }
        catch (Exception ex) { BenchLive = ex.Message; }
        finally
        {
            gpu?.Dispose();
            _benchCts = null;
            DiagBenchBusy = false;
        }
    }

    [RelayCommand]
    private void DiagShowGuide() => DiagGuideOpen = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetIdle))]
    private bool _diagNetBusy;
    public bool NetIdle => !DiagNetBusy;
    [ObservableProperty] private string _diagScore = "";
    public event Action? DebugScrollToEnd;
    public ObservableCollection<DiagIssue> DiagIssues { get; } = new();

    [RelayCommand]
    private async Task DiagIssueFixAsync(DiagIssue? issue)
    {
        switch (issue?.FixKey)
        {
            case "adb": await DiagFixAdbAsync(); break;
            case "reauth": await DiagFixReauthAsync(); break;
            case "kill": await DiagFixKillAsync(); break;
            case "fw": await DiagFixFwAsync(); break;
            case "devmgmt": DiagOpenDevmgmt(); break;
            case "guide": DiagShowGuide(); break;
        }
    }

    [RelayCommand]
    private void DiagIssueLink(DiagIssue? issue)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://github.com/shinzarou-eng/TouchMirror/issues") { UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    private async Task DiagNetAsync()
    {
        DiagNetBusy = true;
        DebugNote = L("dbg.net_run");
        foreach (var i in DiagIssues.Where(x => x.Tag == "net").ToList())
            DiagIssues.Remove(i);
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("\n== réseau wifi ==\n");
            var pc = WifiDiag.QueryPcWifi();
            if (pc is { Connected: true })
            {
                var band = WifiDiag.Band(pc.Channel);
                sb.Append(pc.Signal >= 40
                    ? $"[OK] wifi PC : « {pc.Ssid} » · signal {pc.Signal} % · {pc.RxMbps} Mbps · {band}"
                    : $"[!!] wifi PC faible : « {pc.Ssid} » · signal {pc.Signal} % · {pc.RxMbps} Mbps · {band}").Append('\n');
                if (pc.Signal < 40)
                    DiagIssues.Add(new("signal wifi PC faible",
                        "Signal < 40 % : le flux va sauter en mirroring WiFi. → Rapproche le PC du routeur ou passe en ethernet.",
                        Tag: "net"));
            }
            else if (pc != null)
                sb.Append("[--] wifi PC non connecté — ethernet ou carte inactive\n");
            else
                sb.Append("[--] pas de carte wifi détectée sur le PC\n");

            var dev = ActiveMirror?.Device ?? Devices.FirstOrDefault(d => d.IsReady);
            if (dev == null)
            {
                sb.Append("[--] aucun appareil prêt — branche un tel pour tester la liaison\n");
            }
            else
            {
                var wifi = await WifiDiag.PhoneWifiAsync(dev.Serial, CancellationToken.None);
                if (wifi is { } w)
                {
                    sb.Append(w.Rssi < -70
                        ? $"[!!] wifi tel faible : rssi {w.Rssi} dBm · lien {w.Mbps} Mbps · {w.Band}\n"
                        : $"[OK] wifi tel : rssi {w.Rssi} dBm · lien {w.Mbps} Mbps · {w.Band}\n");
                    if (w.Rssi < -70)
                        DiagIssues.Add(new("signal wifi tel faible",
                            "RSSI < −70 dBm : la connexion lâche au moindre obstacle. → Rapproche le tel du routeur, évite les murs, force le 5 GHz.",
                            Tag: "net"));
                }
                var ip = await WifiDiag.PhoneIpAsync(dev, CancellationToken.None);
                var pcIp = ip != null ? WifiDiag.PcIpv4For(ip) : WifiDiag.PcPrimaryIpv4();
                if (ip != null && pcIp != null)
                {
                    var sameNet = WifiDiag.SameSubnet(pcIp.Value.Ip, ip, pcIp.Value.Mask);
                    sb.Append(sameNet
                        ? $"[OK] même sous-réseau : PC {pcIp.Value.Ip} · tel {ip}\n"
                        : $"[!!] sous-réseau différent : PC {pcIp.Value.Ip} · tel {ip} — le pairing wifi va échouer\n");
                    if (!sameNet)
                        DiagIssues.Add(new("PC et tel pas sur le même sous-réseau",
                            "Le pairing QR et la découverte mDNS ne traversent pas deux réseaux. → Connecte les deux au même WiFi (pas de wifi invité, pas de partage 4G).",
                            Tag: "net"));
                    var ping = await WifiDiag.PingAsync(ip, 8, CancellationToken.None);
                    if (ping is { } pn)
                    {
                        sb.Append(pn.Lost == 0 && pn.Avg <= 60
                            ? $"[OK] ping tel : moy {pn.Avg:0} ms · min {pn.Min:0} · max {pn.Max:0} · perte 0/{pn.Sent}\n"
                            : $"[!!] ping tel : moy {(pn.Avg < 0 ? 0 : pn.Avg):0} ms · perte {pn.Lost}/{pn.Sent} — wifi instable\n");
                        if (pn.Lost > 0 || pn.Avg > 60)
                            DiagIssues.Add(new("ping instable",
                                "Perte de paquets ou latence > 60 ms — le flux va saccader. → Rapproche le tel, coupe les téléchargements en cours, évite le wifi invité.",
                                Tag: "net"));
                    }
                    var mbps = await AdbService.MeasureAdbMbpsAsync(dev.Serial, 8, CancellationToken.None);
                    if (mbps > 0)
                    {
                        sb.Append(mbps >= 15
                            ? $"[OK] débit adb : {mbps:0} Mbps mesurés\n"
                            : $"[!!] débit adb : {mbps:0} Mbps — sous le bitrate par défaut, baisse la qualité\n");
                        if (mbps < 15)
                            DiagIssues.Add(new("débit adb faible",
                                $"{mbps:0} Mbps mesurés, sous le bitrate par défaut (16). → Baisse la qualité dans les réglages, ou passe en 5 GHz / USB.",
                                Tag: "net"));
                    }
                }
                else if (ip == null)
                    sb.Append("[--] ip du tel introuvable — le wifi du tel est peut-être coupé\n");
            }
            DebugReport += sb.ToString();
            DebugScrollToEnd?.Invoke();
            DebugNote = L("dbg.net_done");
        }
        catch (Exception ex) { DebugNote = ex.Message; }
        finally { DiagNetBusy = false; }
        _ = ClearDebugNoteAsync();
    }

    [RelayCommand]
    private void DiagCloseGuide() => DiagGuideOpen = false;

    [RelayCommand]
    private void DiagOpenDevmgmt()
    {
        try { System.Diagnostics.Process.Start("devmgmt.msc"); }
        catch (Exception ex) { DebugNote = ex.Message; }
    }

    private async Task BuildDebugReportAsync()
    {
        if (DebugBusy)
            return;
        DebugBusy = true;
        try
        {
            var nl = Environment.NewLine;
            var adbPath = AdbService.FindAdb();
            var adbVersion = await AdbService.ProbeAsync("version");
            var devicesRaw = await AdbService.ProbeAsync("devices -l");
            var mdnsServices = await AdbService.ProbeAsync("mdns services");
            var mdnsCheck = await AdbService.ProbeAsync("mdns check");
            List<AdbService.PnpAndroidDevice> pnp = new();
            string? pnpError = null;
            try { pnp = await AdbService.DetectPnpAndroidAsync(); }
            catch (Exception ex) { pnpError = ex.Message; }
            var foreign = AdbService.FindCompetingProcesses();
            var lanIps = MdnsHost.LanAddresses();
            var mdnsReplies = -1;
            try { mdnsReplies = await MdnsHost.ProbeAdbAsync(TimeSpan.FromSeconds(2)); }
            catch { }
            var detected = Devices.Where(d => !d.IsRememberedOnly).ToList();
            var ready = detected.Where(d => d.IsReady).ToList();

            var sb = new System.Text.StringBuilder();
            sb.Append("TouchMirror v").Append(typeof(App).Assembly.GetName().Version?.ToString(3)).Append(nl);
            sb.Append(System.Runtime.InteropServices.RuntimeInformation.OSDescription).Append(nl);
            sb.Append(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription).Append(nl);
            sb.Append("écran : ").Append(SystemParameters.PrimaryScreenWidth).Append("x")
                .Append(SystemParameters.PrimaryScreenHeight).Append(nl);
            sb.Append("réglages : ").Append(MaxSize == 0 ? "natif" : MaxSize + "p").Append(" · ")
                .Append(MaxFps).Append(" fps · ").Append(VideoBitRate / 1_000_000).Append(" Mbps · ")
                .Append(VideoCodec).Append(" · audio ").Append(EnableAudio ? "on" : "off")
                .Append(" · écran off ").Append(TurnScreenOff ? "on" : "off").Append(nl);

            var ds = new System.Text.StringBuilder();
            var issues = new List<DiagIssue>();
            int errs = 0, warns = 0;
            if (adbPath != null)
                ds.Append("[OK] adb : ").Append(adbPath).Append(nl);
            else
            {
                errs++;
                ds.Append("[!!] adb introuvable — platform-tools manquant").Append(nl);
                issues.Add(new("adb introuvable",
                    "Le binaire adb embarqué est absent ou cassé. → Réinstalle TouchMirror, ou replace le dossier assets\\platform-tools à côté de l'exécutable."));
            }
            var adbResponds = devicesRaw.Contains("List of devices", StringComparison.OrdinalIgnoreCase);
            if (adbResponds)
                ds.Append("[OK] le serveur adb répond").Append(nl);
            else
            {
                errs++;
                ds.Append("[!!] adb ne répond pas").Append(nl);
                issues.Add(new("adb ne répond pas",
                    "Le serveur adb est planté ou un autre serveur occupe le port 5037. → Relance-le ci-dessous ; si ça revient, tue les processus adb/scrcpy concurrents.",
                    "adb", L("dbg.fix_adb")));
            }
            if (ready.Count > 0)
                ds.Append("[OK] ").Append(ready.Count).Append(" appareil(s) prêt(s)").Append(nl);
            foreach (var d in detected.Where(d => d.NeedsAuthorization))
            {
                errs++;
                ds.Append("[!!] « ").Append(d.ShortName)
                    .Append(" » en attente d'autorisation — accepte la popup RSA sur le téléphone").Append(nl);
                issues.Add(new($"« {d.ShortName} » en attente d'autorisation",
                    "La popup « Autoriser ce PC » n'a jamais été acceptée sur le téléphone (Samsung : « Auto Blocker » peut la bloquer). → Déverrouille le tel, rebranche le câble, accepte la popup.",
                    "reauth", L("dbg.fix_reauth")));
            }
            foreach (var d in detected.Where(d => d.IsOffline))
            {
                errs++;
                ds.Append("[!!] « ").Append(d.ShortName).Append(" » hors ligne — rebranche le câble").Append(nl);
                issues.Add(new($"« {d.ShortName} » hors ligne",
                    "Câble ou port instable, ou le démon adb du tel a planté. → Rebranche sur un autre port USB ; si ça persiste, redémarre le tel.",
                    "adb", L("dbg.fix_adb")));
            }
            if (detected.Count == 0)
            {
                warns++;
                ds.Append("[--] aucun appareil adb").Append(nl);
                issues.Add(new("aucun appareil détecté",
                    "Pas branché, ou le débogage USB est désactivé sur le tel. → Active le débogage USB (guide par marque) puis branche le câble.",
                    "guide", L("dbg.guide")));
            }
            else if (ready.Count == 0
                && !detected.Any(d => d.NeedsAuthorization || d.IsOffline))
            {
                warns++;
                ds.Append("[--] appareil(s) détecté(s), aucun prêt").Append(nl);
                issues.Add(new("appareil détecté, aucun prêt",
                    "adb voit l'appareil mais il n'est pas dans l'état « device ». → Déverrouille le tel, rebranche le câble.",
                    "guide", L("dbg.guide")));
            }
            var pnpOnly = pnp.Where(p => p.UsbSerial != null && detected.All(d =>
                !d.MatchesSerial(p.UsbSerial) && d.HardwareSerial != p.UsbSerial
                && d.DeviceKey != p.UsbSerial)).ToList();
            foreach (var p in pnpOnly)
            {
                errs++;
                ds.Append("[!!] Windows voit « ").Append(p.Name).Append(" » (").Append(p.Brand ?? p.Vid)
                    .Append(") mais adb non — débogage USB/RSA à vérifier").Append(nl);
                issues.Add(new($"{p.Name} vu par Windows, pas par adb",
                    "Le driver USB est là mais adb ne voit pas l'appareil : débogage USB off, popup RSA refusée, ou pilote générique. → Ouvre le guide de débogage ; vérifie le pilote dans le gestionnaire.",
                    "devmgmt", L("dbg.devmgmt")));
            }
            foreach (var p in pnp.Where(p => p.UsbSerial == null))
                ds.Append("[--] Windows voit « ").Append(p.Name).Append(" » (").Append(p.Brand ?? p.Vid)
                    .Append(") — non corrélé à un appareil adb").Append(nl);
            if (pnpError != null) { warns++; ds.Append("[--] détection Windows : ").Append(pnpError).Append(nl); }
            var brands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in pnp)
                if (p.Brand != null)
                    brands.Add(p.Brand);
            var models = devicesRaw.ToLowerInvariant();
            if (models.Contains("redmi") || models.Contains("poco") || models.Contains("xiaomi"))
                brands.Add("Xiaomi");
            if (models.Contains("sm-") || models.Contains("samsung") || models.Contains("galaxy"))
                brands.Add("Samsung");
            if (models.Contains("cph") || models.Contains("oppo"))
                brands.Add("Oppo");
            if (models.Contains("rmx") || models.Contains("realme"))
                brands.Add("Oppo");
            if (models.Contains("oneplus"))
                brands.Add("Oppo");
            if (models.Contains("vivo"))
                brands.Add("Vivo");
            if (models.Contains("huawei") || models.Contains("honor"))
                brands.Add("Huawei");
            foreach (var b in brands.OrderBy(b => b))
            {
                var tip = b switch
                {
                    "Xiaomi" => "contrôle : active « Débogage USB (paramètres de sécurité) » — compte Mi + SIM souvent requis",
                    "Samsung" => "vérifie « Auto Blocker » (Paramètres → Sécurité) — il bloque les commandes USB ; déverrouille le tel pour la popup RSA",
                    "Oppo" or "OnePlus" => "contrôle : active « Désactiver la surveillance des autorisations » dans les options développeur",
                    "Vivo" => "contrôle : active « Entrée simulée USB » dans les options développeur",
                    "Huawei" => "ferme HiSuite — il peut occuper le port adb du téléphone",
                    _ => null
                };
                if (tip != null)
                    ds.Append("[i ] ").Append(b).Append(" : ").Append(tip).Append(nl);
            }
            var foreignAdb = foreign.Where(p =>
                p.Name.Equals("adb", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var g in foreign.Where(p =>
                             !p.Name.Equals("adb", StringComparison.OrdinalIgnoreCase))
                         .GroupBy(p => p.Name))
            {
                ds.Append("[--] ").Append(g.Key)
                    .Append(g.Count() > 1 ? $" tourne (×{g.Count()})" : " tourne")
                    .Append(" — adb propre, pas de conflit direct").Append(nl);
            }
            foreach (var g in foreignAdb.GroupBy(p => p.Name))
            {
                warns++;
                ds.Append("[!!] ").Append(g.Key).Append(g.Count() > 1 ? $" tourne (×{g.Count()})" : " tourne")
                    .Append(" — un autre serveur adb peut couper la liaison").Append(nl);
                issues.Add(new($"« {g.Key} » tourne en dehors de l'app",
                    "Un autre serveur adb (ancien build, Android Studio, Walky) peut couper la liaison pendant une session. → Arrête-le ci-dessous après vérification de la liste.",
                    "kill", L("dbg.fix_kill")));
            }
            var selfExe = Environment.ProcessPath;
            var fwBlocked = selfExe != null
                && FirewallHelper.InboundState(selfExe) == FirewallHelper.State.Block;
            if (fwBlocked)
            {
                warns++;
                ds.Append("[!!] le pare-feu bloque TouchMirror — connexions entrantes refusées").Append(nl);
                issues.Add(new("le pare-feu bloque TouchMirror",
                    "Une règle entrante « bloquer » existe pour l'exe — le mirroring WiFi et le pairing échouent. → Autorise l'app ci-dessous (UAC).",
                    "fw", L("dbg.fix_fw")));
            }
            if (mdnsReplies > 0)
                ds.Append($"[OK] mDNS : {mdnsReplies} service(s) adb visibles — le pairing QR peut trouver le téléphone").Append(nl);
            else if (mdnsReplies == 0)
                ds.Append("[--] mDNS : aucun service adb en écoute (normal hors mode « appairer » du téléphone)").Append(nl);
            else
                ds.Append("[--] mDNS non testable").Append(nl);
            ds.Append("[i ] IP du PC : ").Append(lanIps.Count > 0 ? string.Join(", ", lanIps) : "?")
                .Append(" — le téléphone doit être sur le même réseau").Append(nl);

            var score = errs > 0 ? $"🔴 problème bloquant — {errs} point(s) à corriger" :
                warns > 0 ? $"🟡 attention — {warns} point(s) à vérifier" :
                "🟢 prêt à jouer";
            DiagScore = score;
            sb.Append("santé globale : ").Append(score).Append(nl);
            sb.Append(nl).Append("== diagnostic ==").Append(nl).Append(ds);
            sb.Append(nl).Append("== adb devices -l ==").Append(nl).Append(devicesRaw).Append(nl);
            sb.Append(nl).Append("== adb mdns ==").Append(nl).Append(mdnsCheck).Append(nl).Append(mdnsServices).Append(nl);
            sb.Append(nl).Append("== périphériques Windows (PnP) ==").Append(nl);
            if (pnp.Count == 0)
                sb.Append("(aucun)").Append(nl);
            else
                foreach (var d in pnp)
                    sb.Append("- ").Append(d.Name).Append("  [").Append(d.Vid).Append("] ").Append(d.Brand ?? "").Append(nl);
            sb.Append(nl).Append("== processus adb / mirroring ==").Append(nl);
            if (foreign.Count == 0)
                sb.Append("(aucun)").Append(nl);
            else
                foreach (var p in foreign)
                    sb.Append("- ").Append(p.Name).Append("  ").Append(p.Path ?? "?").Append(nl);
            sb.Append(nl).Append("== miroirs ==").Append(nl);
            if (Mirrors.Count == 0)
                sb.Append("(aucun)").Append(nl);
            else
                foreach (var m in Mirrors)
                    sb.Append("- ").Append(m.DeviceName).Append(m.IsConnected ? " [connecté]" : "")
                        .Append(m.IsReconnecting ? $" [reconnexion: {m.ReconnectStatus}]" : "")
                        .Append(m.UnexpectedDeath ? " [mort inattendue]" : "")
                        .Append(" état=").Append(m.LastDeviceState).Append(nl);
            sb.Append(nl).Append("== journal ==").Append(nl);
            sb.Append(TailLog(250));
            DebugReport = sb.ToString();
            DiagFixAdbVisible = adbPath != null && !adbResponds;
            DiagFixReauthVisible = detected.Any(d => d.NeedsAuthorization);
            DiagFixKillVisible = foreignAdb.Count > 0;
            DiagFixKillLabel = foreignAdb.Count > 1
                ? string.Format(L("dbg.fix_kill_n"), foreignAdb.Count) : L("dbg.fix_kill");
            DiagDevmgmtVisible = pnpOnly.Count > 0;
            DiagFixFwVisible = fwBlocked;
            DiagFixAllVisible = DiagFixKillVisible || DiagFixAdbVisible
                || DiagFixReauthVisible || fwBlocked;
            DiagIssues.Clear();
            foreach (var i in issues)
                DiagIssues.Add(i);
            DiagGuideVisible = brands.Count > 0 || pnpOnly.Count > 0;
            if (DiagGuideVisible)
            {
                DiagGuideTitle = L("dbg.guide_title");
                DiagGuideText = string.Join(nl + nl, brands.OrderBy(b => b)
                    .Select(b => "• " + b + " — " + GuideFor(b))
                    .Concat(pnpOnly.Count > 0 && brands.Count == 0
                        ? new[] { L("dbg.guide.generic") } : Array.Empty<string>()));
            }
        }
        finally
        {
            DebugBusy = false;
        }
    }

    private static string TailLog(int maxLines)
    {
        try
        {
            using var fs = new FileStream(AppLogger.LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var lines = sr.ReadToEnd().Split('\n');
            return string.Join(Environment.NewLine, lines.TakeLast(maxLines));
        }
        catch (Exception ex) { return ex.Message; }
    }

    [RelayCommand] private void SendBack() => ActiveMirror?.Session?.Control?.InjectKeyPress(AndroidKeyCode.Back);
    [RelayCommand] private void SendHome() => ActiveMirror?.Session?.Control?.InjectKeyPress(AndroidKeyCode.Home);
    [RelayCommand] private void SendRecents() => ActiveMirror?.Session?.Control?.InjectKeyPress(AndroidKeyCode.AppSwitch);
    [RelayCommand] private void LaunchDofus() => ActiveMirror?.Session?.Control?.StartApp("com.ankama.dofustouch");
    [RelayCommand] private void SendPower() => ActiveMirror?.Session?.Control?.InjectKeyPress(AndroidKeyCode.Power);
    [RelayCommand] private void RotateDevice() => ActiveMirror?.Session?.Control?.SendSimple(ControlMsgType.RotateDevice);
    [RelayCommand] private void Screenshot()
    {
        var m = ActiveMirror;
        if (m != null && RequestScreenshot(m) != null)
            AddActivity("camera", L("act.capture"), m.Device.ShortName);
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

    private void AddActivity(string icon, string text, string? device = null)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.HasShutdownFinished)
            return;
        d.BeginInvoke(() =>
        {
            ActivityLog.Insert(0, new ActivityEntry(DateTime.Now.ToString("HH:mm"), icon, text, device));
            while (ActivityLog.Count > 8)
                ActivityLog.RemoveAt(ActivityLog.Count - 1);
        });
    }

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
