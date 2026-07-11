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
        _settings.LastSelectedDeviceKey = SelectedDevice?.DeviceKey;
        SettingsStore.Save(_settings);
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
    public event Action<MirrorInstance>? ScreenshotRequested;
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
    }

    private void RefreshInactiveMirrors()
    {
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
    partial void OnShowSettingsChanged(bool value) => ScheduleSave();
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

    private async Task ConnectDeviceAsync(AdbDevice device)
    {
        Services.AppLogger.Write($"connect start: {device.Serial}");
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
            };
            instance.Disconnected += m =>
            {
                Mirrors.Remove(m);
                PromoteNextActive(m);
            };

            Mirrors.Add(instance);
            RefreshInactiveMirrors();
            SetActive(instance);
            MirrorAdded?.Invoke(instance);
            Services.AppLogger.Write("startasync begin");
            await instance.StartAsync(BuildOptions(), AutoLaunchDofus);
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
        Mirrors.Remove(instance);
        PromoteNextActive(instance);
        await instance.DisconnectAsync();
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
            ScreenshotRequested?.Invoke(ActiveMirror);
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
    private void ToggleRecording()
    {
        if (ActiveMirror == null)
            return;
        Status = ActiveMirror.ToggleRecording(VideoCodec);
        OnPropertyChanged(nameof(RecordingVisibility));
        RecordingElapsed = ActiveMirror.RecordingSince.HasValue
            ? $"REC {(DateTime.Now - ActiveMirror.RecordingSince.Value):m\\:ss}" : "REC";
    }

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