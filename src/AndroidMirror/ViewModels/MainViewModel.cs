using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
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

    [ObservableProperty] private int _maxSize = 0;
    [ObservableProperty] private int _maxFps = 60;
    [ObservableProperty] private int _videoBitRate = 16_000_000;
    [ObservableProperty] private string _videoCodec = "h264";
    [ObservableProperty] private bool _stayAwake;
    [ObservableProperty] private bool _enableAudio = true;
    [ObservableProperty] private bool _autoLaunchDofus;
    [ObservableProperty] private bool _autoFullscreen;
    [ObservableProperty] private bool _topmost;
    [ObservableProperty] private bool _turnScreenOff;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsVisibility))]
    private bool _showSettings;
    public Visibility SettingsVisibility => ShowSettings ? Visibility.Visible : Visibility.Collapsed;

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
        ActiveMirror = instance;
        foreach (var m in Mirrors)
        {
            m.IsActive = m == instance;
            m.SetAudioMuted(m != instance);
        }
        UpdateStatus();
    }

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

    partial void OnMaxSizeChanged(int value) => _ = ReconnectActiveAsync();
    partial void OnMaxFpsChanged(int value) => _ = ReconnectActiveAsync();
    partial void OnVideoBitRateChanged(int value) => _ = ReconnectActiveAsync();
    partial void OnVideoCodecChanged(string value) => _ = ReconnectActiveAsync();
    partial void OnEnableAudioChanged(bool value) => _ = ReconnectActiveAsync();
    partial void OnTurnScreenOffChanged(bool value) => _ = ReconnectAllAsync();

    private async Task ReconnectAllAsync()
    {
        if (IsBusy)
            return;
        foreach (var m in Mirrors.Where(m => m.IsConnected).ToList())
        {
            Log("Écran éteint — reconnexion via écran virtuel…");
            var device = m.Device;
            await RemoveMirrorInternalAsync(m);
            await ConnectDeviceAsync(device);
        }
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
    }

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
        if (SelectedDevice is not { IsReady: true })
        {
            Status = "Aucun appareil prêt (vérifie le débogage USB)";
            return;
        }
        await ConnectDeviceAsync(SelectedDevice);
    }

    private async Task ConnectDeviceAsync(AdbDevice device)
    {
        var existing = Mirrors.FirstOrDefault(m => m.Device.Serial == device.Serial);
        if (existing != null)
        {
            SetActive(existing);
            return;
        }

        IsBusy = true;
        _hasError = false;
        OnPropertyChanged(nameof(StatusDotColor));
        Status = $"Connexion — {device.DisplayName}…";
        var instance = new MirrorInstance(device);
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
                if (ActiveMirror == m)
                    ActiveMirror = Mirrors.LastOrDefault();
                UpdateStatus();
            };

            Mirrors.Add(instance);
            MirrorAdded?.Invoke(instance);
            await instance.StartAsync(BuildOptions(), AutoLaunchDofus);
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            _hasError = true;
            Mirrors.Remove(instance);
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
        if (ActiveMirror == instance)
            ActiveMirror = Mirrors.LastOrDefault();
        UpdateStatus();
        await instance.DisconnectAsync();
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

            var existing = Mirrors.FirstOrDefault(m => m.Device.Serial == SelectedDevice.Serial);
            var reconnect = existing != null;
            if (existing != null)
                await RemoveMirrorInternalAsync(existing);

            var ip = await AdbService.EnableWifiAsync(SelectedDevice.Serial);

            AdbDevice? wifiDevice = null;
            for (var i = 0; i < 10 && wifiDevice == null; i++)
            {
                await Task.Delay(800);
                await RefreshDevicesAsync();
                wifiDevice = Devices.FirstOrDefault(d => d.Serial.StartsWith(ip));
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
    private void ApplyDofusPreset()
    {
        MaxSize = 1080;
        MaxFps = 60;
        VideoBitRate = 16_000_000;
        VideoCodec = "h264";
        EnableAudio = true;
        StayAwake = true;
        AutoLaunchDofus = true;
        Status = "Preset Dofus appliqué — connecte-toi !";
    }

    [RelayCommand]
    private void ToggleRecording()
    {
        if (ActiveMirror == null)
            return;
        Status = ActiveMirror.ToggleRecording(VideoCodec);
        OnPropertyChanged(nameof(RecordingVisibility));
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