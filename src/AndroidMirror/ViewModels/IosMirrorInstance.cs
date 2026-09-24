using CommunityToolkit.Mvvm.ComponentModel;
using TouchMirror.Services;
using TouchMirror.Views;

namespace TouchMirror.ViewModels;

public sealed partial class IosMirrorInstance : MirrorInstance
{
    private BleHidHost? _ble;
    private BlePointerAdapter? _pointer;

    [ObservableProperty] private bool _bleActive;
    [ObservableProperty] private bool _bleLinked;
    [ObservableProperty] private string _bleStatus = "";
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
        service.DeviceConnected += (name, id) =>
            View.Dispatcher.Invoke(() =>
            {
                View.SetWaitingOverlay(false);
                View.SetWaitingHint(null);
                IsConnected = true;
                DeviceName = string.IsNullOrWhiteSpace(name) ? "iPhone (AirPlay)" : name;
                RaiseConnected();
            });
        service.DeviceDisconnected += (name, id) =>
            View.Dispatcher.Invoke(() =>
            {
                IsConnected = false;
                DeviceName = "iPhone (AirPlay)";
                View.SetWaitingOverlay(true);
            });


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
            if (already == null)
                View.SetWaitingHint(L("ios.hint"));
        }).Task;
    }

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

    public override string ToggleRecording(string videoCodec)
        => L("ios.no_record");

    public override async Task DisconnectAsync()
    {
        _ble?.Dispose();
        _ble = null;
        _pointer = null;
        await base.DisconnectAsync();
    }
}
