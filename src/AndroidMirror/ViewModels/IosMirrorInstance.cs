using CommunityToolkit.Mvvm.ComponentModel;
using TouchMirror.Services;
using TouchMirror.Views;

namespace TouchMirror.ViewModels;

/// <summary>
/// Tuile iOS : miroir AirPlay + contrôle optionnel par souris Bluetooth HID
/// (AssistiveTouch). Réutilise la plomberie MirrorInstance sans ScrcpySession.
/// </summary>
public sealed partial class IosMirrorInstance : MirrorInstance
{
    private AirPlayService? _service;
    private Video.AirPlayAudioPlayer? _audio;
    private BleHidHost? _ble;
    private BlePointerAdapter? _pointer;

    /// <summary>Souris Bluetooth en diffusion.</summary>
    [ObservableProperty] private bool _bleActive;
    /// <summary>L'iPhone est jumelé et souscrit aux rapports HID.</summary>
    [ObservableProperty] private bool _bleLinked;
    /// <summary>Message d'état du contrôle Bluetooth.</summary>
    [ObservableProperty] private string _bleStatus = "";
    /// <summary>Levée quand le statut BLE change (pour la barre de statut).</summary>
    public event Action<string>? BleStatusChanged;

    public override bool IsIos => true;

    private static AdbDevice IosDevice => new(
        Serial: "ios:airplay",
        Model: "iPhone",
        State: "device",
        HardwareSerial: "ios-airplay");

    public IosMirrorInstance() : base(IosDevice)
    {
        DeviceName = "iPhone (AirPlay)";
    }

    public Task StartAsync(AirPlayService service)
    {
        _service = service;

        service.DeviceConnected += (name, id) =>
            View.Dispatcher.Invoke(() =>
            {
                View.SetWaitingOverlay(false);
                IsConnected = true;
                DeviceName = string.IsNullOrWhiteSpace(name) ? "iPhone (AirPlay)" : name;
                RaiseConnected();
            });
        service.DeviceDisconnected += (name, id) =>
            View.Dispatcher.Invoke(() =>
            {
                IsConnected = false;
                DeviceName = "iPhone (AirPlay)";
                _audio?.Dispose();
                _audio = null;
                View.SetWaitingOverlay(true);
            });
        service.AudioFrame += (rate, ch, bits, data, len) =>
        {
            _audio ??= CreateAudio();
            _audio?.Feed(rate, ch, bits, data, len);
        };
        service.Exited += () =>
            View.Dispatcher.Invoke(() =>
            {
                RaiseLog("airplay: récepteur arrêté");
                IsConnected = false;
                View.SetWaitingOverlay(true);
            });
        service.Log += m => RaiseLog(m);

        // L'iPhone peut s'être connecté avant la souscription — on resynchronise.
        var already = service.ConnectedDeviceName;
        return View.Dispatcher.InvokeAsync(() =>
        {
            View.AttachDecoder(service.Frames);
            View.SetIosReadOnly(true);
            if (already != null)
            {
                IsConnected = true;
                DeviceName = string.IsNullOrWhiteSpace(already) ? "iPhone (AirPlay)" : already;
            }
            View.SetWaitingOverlay(already == null);
        }).Task;
    }

    private Video.AirPlayAudioPlayer CreateAudio()
    {
        var a = new Video.AirPlayAudioPlayer();
        a.Error += m => RaiseLog($"audio: {m}");
        return a;
    }

    /// <summary>
    /// Active la souris Bluetooth HID. L'iPhone se jumelle dans
    /// Réglages → Accessibilité → Toucher → AssistiveTouch → Appareils.
    /// </summary>
    public async Task<bool> EnableBleControlAsync()
    {
        if (_ble != null)
            return true;
        try
        {
            _ble = new BleHidHost();
            _ble.Log += m => RaiseLog(m);
            _ble.LinkChanged += linked => View.Dispatcher.Invoke(() =>
            {
                BleLinked = linked;
                BleStatus = linked
                    ? L("ios.ble_connected")
                    : L("ios.ble_waiting");
                View.SetIosBadgeText(linked ? L("ios.ble_badge") : L("ios_affichage_seul"));
                BleStatusChanged?.Invoke(BleStatus);
            });
            await _ble.StartAsync();
            _pointer = new BlePointerAdapter(_ble);
            View.SetIosPointer(_pointer);
            BleActive = true;
            BleStatus = L("ios.ble_advertising");
            BleStatusChanged?.Invoke(BleStatus);
            return true;
        }
        catch (Exception ex)
        {
            _ble?.Dispose();
            _ble = null;
            _pointer = null;
            BleStatus = string.Format(L("ios.ble_fail"), ex.Message);
            BleStatusChanged?.Invoke(BleStatus);
            return false;
        }
    }

    public void DisableBleControl()
    {
        _ble?.Dispose();
        _ble = null;
        _pointer = null;
        View.SetIosPointer(null);
        BleActive = false;
        BleLinked = false;
        BleStatus = L("ios.ble_off");
        View.SetIosBadgeText(L("ios_affichage_seul"));
        BleStatusChanged?.Invoke(BleStatus);
    }

    /// <summary>Position normalisée vidéo → coordonnées absolues HID 0..32767.</summary>
    private sealed class BlePointerAdapter : MirrorView.IIosPointer
    {
        private readonly BleHidHost _host;
        public BlePointerAdapter(BleHidHost host) => _host = host;
        private static ushort C(double v) => (ushort)Math.Clamp(v * 32767, 0, 32767);
        public void MoveTo(double rx, double ry) => _host.PointerMove(C(rx), C(ry));
        public void Down(double rx, double ry) => _host.PointerDown(C(rx), C(ry));
        public void Up(double rx, double ry) => _host.PointerUp(C(rx), C(ry));
        public void Click(double rx, double ry) => _host.Click(C(rx), C(ry));
        public void Wheel(double rx, double ry, int steps) =>
            _host.Wheel(C(rx), C(ry), (sbyte)Math.Clamp(steps, -127, 127));
    }

    public override void SetAudioMuted(bool muted)
    {
        try { if (_audio != null) _audio.Volume = muted ? 0f : 1f; } catch { }
    }

    public override string ToggleRecording(string videoCodec)
        => L("ios.no_record");

    public override async Task DisconnectAsync()
    {
        _service = null;
        _ble?.Dispose();
        _ble = null;
        _pointer = null;
        _audio?.Dispose();
        _audio = null;
        await base.DisconnectAsync();
    }
}
