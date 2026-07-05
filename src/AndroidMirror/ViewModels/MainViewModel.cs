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

    [ObservableProperty] private ObservableCollection<AdbDevice> _devices = new();
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

    public MainViewModel()
    {
        _recTimer.Tick += (_, _) =>
        {
            var since = ActiveMirror?.RecordingSince;
            RecordingElapsed = since.HasValue ? $"REC {(DateTime.Now - since.Value):m\\:ss}" : "REC";
        };
        _recTimer.Start();
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

    partial void OnMaxSizeChanged(int value) { if (!_suppressReconnect) _ = ReconnectActiveAsync(); }
    partial void OnMaxFpsChanged(int value) { if (!_suppressReconnect) _ = ReconnectActiveAsync(); }
    partial void OnVideoBitRateChanged(int value) { if (!_suppressReconnect) _ = ReconnectActiveAsync(); }
    partial void OnVideoCodecChanged(string value) { if (!_suppressReconnect) _ = ReconnectActiveAsync(); }
    partial void OnEnableAudioChanged(bool value) { if (!_suppressReconnect) _ = ReconnectActiveAsync(); }
    partial void OnTurnScreenOffChanged(bool value)
    {
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
        try
        {
            var list = await AdbService.GetDevicesAsync();
            Devices = new ObservableCollection<AdbDevice>(list);
            SelectedDevice ??= list.FirstOrDefault(d => d.IsReady) ?? list.FirstOrDefault();
            if (list.Count == 0)
                Status = "Aucun appareil détecté — active le débogage USB et branche ton téléphone";
            else if (!IsConnected)
                Status = $"{list.Count} appareil(s) détecté(s)";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        Services.AppLogger.Write($"ConnectAsync cmd: sel={SelectedDevice?.Serial}");
        if (SelectedDevice is not { IsReady: true })
        {
            Status = "Aucun appareil prêt (vérifie le débogage USB)";
            return;
        }
        await ConnectDeviceAsync(SelectedDevice);
    }

    [RelayCommand]
    private async Task ConnectToDeviceAsync(AdbDevice device)
    {
        Services.AppLogger.Write($"ConnectToDevice cmd: {device?.Serial} ready={device?.IsReady}");
        if (device is not { IsReady: true })
            return;
        SelectedDevice = device;
        await ConnectDeviceAsync(device);
    }

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