using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace TouchMirror.Services;

public sealed class BleHidHost : IDisposable
{
    private static readonly Guid HidService = BluetoothUuidHelper.FromShortId(0x1812);
    private static readonly Guid DisService = BluetoothUuidHelper.FromShortId(0x180A);
    private static readonly Guid BasService = BluetoothUuidHelper.FromShortId(0x180F);
    private static readonly Guid PnpIdUuid = BluetoothUuidHelper.FromShortId(0x2A50);
    private static readonly Guid ManufacturerUuid = BluetoothUuidHelper.FromShortId(0x2A29);
    private static readonly Guid BatteryUuid = BluetoothUuidHelper.FromShortId(0x2A19);
    private static readonly Guid ProtocolModeUuid = BluetoothUuidHelper.FromShortId(0x2A4E);
    private static readonly Guid HidInfoUuid = BluetoothUuidHelper.FromShortId(0x2A4A);
    private static readonly Guid HidControlUuid = BluetoothUuidHelper.FromShortId(0x2A4C);
    private static readonly Guid ReportMapUuid = BluetoothUuidHelper.FromShortId(0x2A4B);
    private static readonly Guid ReportUuid = BluetoothUuidHelper.FromShortId(0x2A4D);
    private static readonly Guid ReportRefUuid = BluetoothUuidHelper.FromShortId(0x2908);

    private static readonly byte[] ReportMap =
    {
        0x05, 0x01,
        0x09, 0x02,
        0xA1, 0x01,
        0x09, 0x01,
        0xA1, 0x00,
        0x85, 0x01,
        0x05, 0x09,
        0x19, 0x01,
        0x29, 0x03,
        0x15, 0x00,
        0x25, 0x01,
        0x95, 0x03,
        0x75, 0x01,
        0x81, 0x02,
        0x95, 0x01,
        0x75, 0x05,
        0x81, 0x01,
        0x05, 0x01,
        0x09, 0x30,
        0x09, 0x31,
        0x16, 0x00, 0x00,
        0x26, 0xFF, 0x7F,
        0x75, 0x10,
        0x95, 0x02,
        0x81, 0x02,
        0x05, 0x01,
        0x09, 0x38,
        0x15, 0x81,
        0x25, 0x7F,
        0x75, 0x08,
        0x95, 0x01,
        0x81, 0x06,
        0xC0,
        0xC0,
        0x05, 0x01,
        0x09, 0x06,
        0xA1, 0x01,
        0x85, 0x02,
        0x05, 0x07,
        0x19, 0xE0,
        0x29, 0xE7,
        0x15, 0x00,
        0x25, 0x01,
        0x75, 0x01,
        0x95, 0x08,
        0x81, 0x02,
        0x95, 0x01,
        0x75, 0x08,
        0x81, 0x01,
        0x95, 0x06,
        0x75, 0x08,
        0x15, 0x00,
        0x25, 0x65,
        0x05, 0x07,
        0x19, 0x00,
        0x29, 0x65,
        0x81, 0x00,
        0xC0
    };

    private GattServiceProvider? _provider;
    private GattServiceProvider? _dis;
    private GattServiceProvider? _bas;
    private GattLocalCharacteristic? _report;
    private GattLocalCharacteristic? _kbReport;
    private byte _buttons;
    private ushort _absX, _absY;
    private sbyte _wheel;
    private byte _mods;
    private readonly List<byte> _keys = new();

    public bool IsRunning => _provider != null;
    public bool IsLinked => (_report?.SubscribedClients.Count ?? 0)
                          + (_kbReport?.SubscribedClients.Count ?? 0) > 0;

    public event Action<string>? Log;
    public event Action<bool>? LinkChanged;

    public async Task StartAsync()
    {
        if (_provider != null)
            return;

        var result = await GattServiceProvider.CreateAsync(HidService);
        if (result.Error != BluetoothError.Success)
            throw new InvalidOperationException($"service BLE indisponible ({result.Error}) — Bluetooth activé ?");
        _provider = result.ServiceProvider;
        var service = _provider.Service;

        await CreateAsync(service, ProtocolModeUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read
                                       | GattCharacteristicProperties.WriteWithoutResponse,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = (new byte[] { 0x01 }).AsBuffer()
        });

        await CreateAsync(service, HidInfoUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = (new byte[] { 0x11, 0x01, 0x00, 0x03 }).AsBuffer()
        });

        await CreateAsync(service, HidControlUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.WriteWithoutResponse,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired
        });

        var map = await CreateAsync(service, ReportMapUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired
        });
        map.Characteristic.ReadRequested += async (_, e) =>
        {
            using var d = e.GetDeferral();
            var req = await e.GetRequestAsync();
            req?.RespondWithValue(ReportMap.AsBuffer());
            Log?.Invoke("ble: iOS lit le descripteur HID — connexion en cours");
        };

        var report = await CreateAsync(service, ReportUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read
                                       | GattCharacteristicProperties.Notify,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = (new byte[] { 0, 0, 0, 0, 0 }).AsBuffer()
        });
        _report = report.Characteristic;
        var dres = await _report.CreateDescriptorAsync(ReportRefUuid, new GattLocalDescriptorParameters
        {
            StaticValue = (new byte[] { 0x01, 0x01 }).AsBuffer(),
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired
        });
        if (dres.Error != BluetoothError.Success)
            throw new InvalidOperationException($"descripteur HID refusé ({dres.Error})");

        var kb = await CreateAsync(service, ReportUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read
                                       | GattCharacteristicProperties.Notify,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = (new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }).AsBuffer()
        });
        _kbReport = kb.Characteristic;
        var kdres = await _kbReport.CreateDescriptorAsync(ReportRefUuid, new GattLocalDescriptorParameters
        {
            StaticValue = (new byte[] { 0x02, 0x01 }).AsBuffer(),
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired
        });
        if (kdres.Error != BluetoothError.Success)
            throw new InvalidOperationException($"descripteur HID clavier refusé ({kdres.Error})");

        _report.SubscribedClientsChanged += (_, _) =>
        {
            Log?.Invoke($"ble: abonnés souris={_report.SubscribedClients.Count} clavier={_kbReport?.SubscribedClients.Count ?? 0}");
            LinkChanged?.Invoke(IsLinked);
        };
        _kbReport.SubscribedClientsChanged += (_, _) =>
            LinkChanged?.Invoke(IsLinked);

        var disRes = await GattServiceProvider.CreateAsync(DisService);
        if (disRes.Error == BluetoothError.Success)
        {
            _dis = disRes.ServiceProvider;
            await CreateAsync(_dis.Service, PnpIdUuid, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read,
                StaticValue = (new byte[] { 0x02, 0x09, 0x12, 0xE0, 0xB1, 0x00, 0x01 }).AsBuffer()
            });
            await CreateAsync(_dis.Service, ManufacturerUuid, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read,
                StaticValue = Encoding.UTF8.GetBytes("TouchMirror").AsBuffer()
            });
        }

        var basRes = await GattServiceProvider.CreateAsync(BasService);
        if (basRes.Error == BluetoothError.Success)
        {
            _bas = basRes.ServiceProvider;
            await CreateAsync(_bas.Service, BatteryUuid, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read
                                           | GattCharacteristicProperties.Notify,
                StaticValue = (new byte[] { 100 }).AsBuffer()
            });
        }

        _provider.StartAdvertising(new GattServiceProviderAdvertisingParameters
        {
            IsDiscoverable = true,
            IsConnectable = true
        });
        Log?.Invoke("ble: souris HID en diffusion — jumele l'iPhone dans AssistiveTouch");
    }

    private static async Task<GattLocalCharacteristicResult> CreateAsync(
        GattLocalService service, Guid uuid, GattLocalCharacteristicParameters p)
    {
        var r = await service.CreateCharacteristicAsync(uuid, p);
        if (r.Error != BluetoothError.Success)
            throw new InvalidOperationException($"caractéristique {uuid} refusée ({r.Error})");
        return r;
    }

    private void SendReport()
    {
        var r = _report;
        if (r == null || r.SubscribedClients.Count == 0)
        {
            _wheel = 0;
            return;
        }
        var data = new byte[]
        {
            _buttons,
            (byte)_absX, (byte)(_absX >> 8),
            (byte)_absY, (byte)(_absY >> 8),
            (byte)_wheel
        };
        _wheel = 0;
        try { _ = r.NotifyValueAsync(data.AsBuffer()); }
        catch (Exception ex) { Log?.Invoke($"ble: {ex.Message}"); }
    }

    public void MoveTo(double rx, double ry)
    {
        _absX = (ushort)Math.Clamp(Math.Round(rx * 32767), 0, 32767);
        _absY = (ushort)Math.Clamp(Math.Round(ry * 32767), 0, 32767);
        SendReport();
    }

    public void PointerDown(byte button = 1)
    {
        _buttons |= button;
        SendReport();
    }

    public void PointerUp(byte button = 0)
    {
        _buttons = button == 0 ? (byte)0 : (byte)(_buttons & ~button);
        SendReport();
    }

    public void WheelDelta(int steps)
    {
        _wheel = (sbyte)Math.Clamp(steps, -127, 127);
        SendReport();
    }

    private void SendKeys()
    {
        var r = _kbReport;
        if (r == null || r.SubscribedClients.Count == 0)
            return;
        var data = new byte[8];
        data[0] = _mods;
        for (var i = 0; i < _keys.Count && i < 6; i++)
            data[2 + i] = _keys[i];
        try { _ = r.NotifyValueAsync(data.AsBuffer()); }
        catch (Exception ex) { Log?.Invoke($"ble: {ex.Message}"); }
    }

    public void KeyDown(byte usage)
    {
        if (usage >= 0xE0)
            _mods |= (byte)(1 << (usage - 0xE0));
        else if (!_keys.Contains(usage))
            _keys.Add(usage);
        SendKeys();
    }

    public void KeyUp(byte usage)
    {
        if (usage >= 0xE0)
            _mods &= (byte)~(1 << (usage - 0xE0));
        else
            _keys.Remove(usage);
        SendKeys();
    }

    public void ReleaseAll()
    {
        _buttons = 0;
        _mods = 0;
        _keys.Clear();
        SendReport();
        SendKeys();
    }

    public void Stop()
    {
        try { _provider?.StopAdvertising(); } catch { }
        _report = null;
        _kbReport = null;
        _dis = null;
        _bas = null;
        _mods = 0;
        _keys.Clear();
        _provider = null;
        LinkChanged?.Invoke(false);
        Log?.Invoke("ble: diffusion arrêtée");
    }

    public void Dispose() => Stop();
}
