using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;
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
            View.VideoSizeChanged += OnVideoSize;
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
            await StartBleAsync();
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

    private async Task StartBleAsync()
    {
        var ble = new BleHidHost();
        ble.Log += m => RaiseLog(m);
        ble.LinkChanged += linked => View.Dispatcher.Invoke(() =>
        {
            BleLinked = linked;
            BleStatus = linked
                ? L("ios.ble_connected")
                : L("ios.ble_waiting");
            View.SetIosBadgeText(linked ? L("ios.ble_badge") : L("ios_affichage_seul"));
            BleStatusChanged?.Invoke(BleStatus);
        });
        await ble.StartAsync();
        _ble = ble;
        _pointer = new BlePointerAdapter(ble);
        View.SetIosPointer(_pointer);
    }

    private bool? _lastLandscape;
    private int _bleRecycleRunning;
    private CancellationTokenSource? _orientSettleCts;

    private void OnVideoSize(int w, int h)
    {
        if (w <= 0 || h <= 0)
            return;
        var landscape = w > h;
        if (_lastLandscape == landscape)
            return;
        var had = _lastLandscape;
        _lastLandscape = landscape;
        if (had == null || _ble == null)
            return;
        _orientSettleCts?.Cancel();
        var cts = _orientSettleCts = new CancellationTokenSource();
        AppLogger.Forget(RecycleBleSettledAsync(landscape, cts.Token));
    }

    private async Task RecycleBleSettledAsync(bool landscape, CancellationToken ct)
    {
        try
        {
            await Task.Delay(600, ct);
        }
        catch (TaskCanceledException) { return; }
        if (ct.IsCancellationRequested || _ble == null || !BleActive)
            return;
        RaiseLog($"ios: orientation → {(landscape ? "paysage" : "portrait")} — {L("ios.ble_recycling")}");
        await RecycleBleAsync();
    }

    private async Task RecycleBleAsync()
    {
        if (Interlocked.Exchange(ref _bleRecycleRunning, 1) != 0)
            return;
        try
        {
            var old = _ble;
            _ble = null;
            _pointer = null;
            View.SetIosPointer(null);
            old?.Dispose();
            await Task.Delay(800);
            if (!BleActive)
                return;
            await StartBleAsync();
            RaiseLog("ble: session relancée — l'iPhone se reconnecte tout seul");
        }
        catch (Exception ex)
        {
            RaiseLog($"ble: relance impossible ({ex.Message})");
        }
        finally
        {
            _bleRecycleRunning = 0;
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
        public void MoveTo(double rx, double ry) => _host.MoveTo(rx, ry);
        public void Down(double rx, double ry, int button) => _host.PointerDown((byte)button);
        public void Up(double rx, double ry, int button) => _host.PointerUp((byte)button);
        public void Click(double rx, double ry)
        {
            _host.PointerDown(1);
            AppLogger.Forget(Task.Run(async () =>
            {
                await Task.Delay(50);
                _host.PointerUp(1);
            }));
        }
        public void Wheel(double rx, double ry, int steps) =>
            _host.WheelDelta(Math.Clamp(steps, -127, 127));

        public bool Key(Key key, bool isDown)
        {
            var usage = HidKeyMap.ToUsage(key);
            if (usage == 0)
                return false;
            if (isDown) _host.KeyDown(usage); else _host.KeyUp(usage);
            return true;
        }

        public void ReleaseAll() => _host.ReleaseAll();
    }

    private static class HidKeyMap
    {
        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        public static byte ToUsage(Key key)
        {
            var mod = key switch
            {
                Key.LeftCtrl => 0xE0, Key.LeftShift => 0xE1, Key.LeftAlt => 0xE2,
                Key.LWin => 0xE3, Key.RightCtrl => 0xE4, Key.RightShift => 0xE5,
                Key.RightAlt => 0xE6, Key.RWin => 0xE7,
                _ => 0
            };
            if (mod != 0)
                return (byte)mod;
            var vk = KeyInterop.VirtualKeyFromKey(key);
            var u = ByScan((int)(MapVirtualKey((uint)vk, 0) & 0xFF));
            return u != 0 ? u : Direct(key);
        }

        private static byte ByScan(int sc) => sc switch
        {
            0x01 => 0x29, 0x02 => 0x1E, 0x03 => 0x1F, 0x04 => 0x20, 0x05 => 0x21,
            0x06 => 0x22, 0x07 => 0x23, 0x08 => 0x24, 0x09 => 0x25, 0x0A => 0x26,
            0x0B => 0x27, 0x0C => 0x2D, 0x0D => 0x2E, 0x0E => 0x2A, 0x0F => 0x2B,
            0x10 => 0x14, 0x11 => 0x1A, 0x12 => 0x08, 0x13 => 0x15, 0x14 => 0x17,
            0x15 => 0x1C, 0x16 => 0x18, 0x17 => 0x0C, 0x18 => 0x12, 0x19 => 0x13,
            0x1A => 0x2F, 0x1B => 0x30, 0x1C => 0x28,
            0x1E => 0x04, 0x1F => 0x16, 0x20 => 0x07, 0x21 => 0x09, 0x22 => 0x0A,
            0x23 => 0x0B, 0x24 => 0x0D, 0x25 => 0x0E, 0x26 => 0x0F,
            0x27 => 0x33, 0x28 => 0x34, 0x29 => 0x35, 0x2B => 0x31,
            0x2C => 0x1D, 0x2D => 0x1B, 0x2E => 0x06, 0x2F => 0x19, 0x30 => 0x05,
            0x31 => 0x11, 0x32 => 0x10, 0x33 => 0x36, 0x34 => 0x37, 0x35 => 0x38,
            0x39 => 0x2C, 0x3A => 0x39,
            0x3B => 0x3A, 0x3C => 0x3B, 0x3D => 0x3C, 0x3E => 0x3D, 0x3F => 0x3E,
            0x40 => 0x3F, 0x41 => 0x40, 0x42 => 0x41, 0x43 => 0x42, 0x44 => 0x43,
            0x45 => 0x53, 0x46 => 0x47, 0x56 => 0x64, 0x57 => 0x44, 0x58 => 0x45,
            _ => 0
        };

        private static byte Direct(Key key) => key switch
        {
            Key.Enter => 0x28, Key.Escape => 0x29, Key.Back => 0x2A, Key.Tab => 0x2B,
            Key.Space => 0x2C, Key.CapsLock => 0x39,
            Key.F1 => 0x3A, Key.F2 => 0x3B, Key.F3 => 0x3C, Key.F4 => 0x3D,
            Key.F5 => 0x3E, Key.F6 => 0x3F, Key.F7 => 0x40, Key.F8 => 0x41,
            Key.F9 => 0x42, Key.F10 => 0x43, Key.F11 => 0x44, Key.F12 => 0x45,
            Key.Home => 0x4A, Key.PageUp => 0x4B, Key.Delete => 0x4C,
            Key.End => 0x4D, Key.PageDown => 0x4E,
            Key.Right => 0x4F, Key.Left => 0x50, Key.Down => 0x51, Key.Up => 0x52,
            Key.NumLock => 0x53,
            Key.Divide => 0x54, Key.Multiply => 0x55, Key.Subtract => 0x56,
            Key.Add => 0x57, Key.Separator => 0x58,
            Key.NumPad1 => 0x59, Key.NumPad2 => 0x5A, Key.NumPad3 => 0x5B,
            Key.NumPad4 => 0x5C, Key.NumPad5 => 0x5D, Key.NumPad6 => 0x5E,
            Key.NumPad7 => 0x5F, Key.NumPad8 => 0x60, Key.NumPad9 => 0x61,
            Key.NumPad0 => 0x62, Key.Decimal => 0x63,
            _ => 0
        };
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
