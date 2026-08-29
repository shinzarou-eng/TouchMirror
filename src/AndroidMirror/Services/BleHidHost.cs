using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace TouchMirror.Services;

/// <summary>
/// Souris Bluetooth LE HID émulée par le PC (HID over GATT, rôle périphérique).
/// L'iPhone s'y jumelle via Réglages → Accessibilité → Toucher → AssistiveTouch
/// → Appareils. Rapports en coordonnées absolues 0..32767 → clic à une position.
/// </summary>
public sealed class BleHidHost : IDisposable
{
    private static readonly Guid HidService = BluetoothUuidHelper.FromShortId(0x1812);
    private static readonly Guid ProtocolModeUuid = BluetoothUuidHelper.FromShortId(0x2A4E);
    private static readonly Guid HidInfoUuid = BluetoothUuidHelper.FromShortId(0x2A4A);
    private static readonly Guid HidControlUuid = BluetoothUuidHelper.FromShortId(0x2A4C);
    private static readonly Guid ReportMapUuid = BluetoothUuidHelper.FromShortId(0x2A4B);
    private static readonly Guid ReportUuid = BluetoothUuidHelper.FromShortId(0x2A4D);
    private static readonly Guid ReportRefUuid = BluetoothUuidHelper.FromShortId(0x2908);

    /// <summary>Descripteur HID : souris à coordonnées ABSOLUES (0..32767).</summary>
    private static readonly byte[] ReportMap =
    {
        0x05, 0x01,       // USAGE_PAGE (Generic Desktop)
        0x09, 0x02,       // USAGE (Mouse)
        0xA1, 0x01,       // COLLECTION (Application)
        0x85, 0x01,       //   REPORT_ID (1)
        0x09, 0x01,       //   USAGE (Pointer)
        0xA1, 0x00,       //   COLLECTION (Physical)
        0x05, 0x09,       //     USAGE_PAGE (Button)
        0x19, 0x01,       //     USAGE_MINIMUM (Button 1)
        0x29, 0x03,       //     USAGE_MAXIMUM (Button 3)
        0x15, 0x00,       //     LOGICAL_MINIMUM (0)
        0x25, 0x01,       //     LOGICAL_MAXIMUM (1)
        0x95, 0x03,       //     REPORT_COUNT (3)
        0x75, 0x01,       //     REPORT_SIZE (1)
        0x81, 0x02,       //     INPUT (Data,Var,Abs) — boutons
        0x95, 0x01,       //     REPORT_COUNT (1)
        0x75, 0x05,       //     REPORT_SIZE (5)
        0x81, 0x03,       //     INPUT (Cnst,Var,Abs) — padding
        0x05, 0x01,       //     USAGE_PAGE (Generic Desktop)
        0x09, 0x30,       //     USAGE (X)
        0x09, 0x31,       //     USAGE (Y)
        0x16, 0x00, 0x00, //     LOGICAL_MINIMUM (0)
        0x26, 0xFF, 0x7F, //     LOGICAL_MAXIMUM (32767)
        0x75, 0x10,       //     REPORT_SIZE (16)
        0x95, 0x02,       //     REPORT_COUNT (2)
        0x81, 0x02,       //     INPUT (Data,Var,Abs) — X/Y absolus
        0x09, 0x38,       //     USAGE (Wheel)
        0x15, 0x81,       //     LOGICAL_MINIMUM (-127)
        0x25, 0x7F,       //     LOGICAL_MAXIMUM (127)
        0x75, 0x08,       //     REPORT_SIZE (8)
        0x95, 0x01,       //     REPORT_COUNT (1)
        0x81, 0x06,       //     INPUT (Data,Var,Rel) — molette
        0xC0,             //   END_COLLECTION
        0xC0              // END_COLLECTION
    };

    private GattServiceProvider? _provider;
    private GattLocalCharacteristic? _report;
    private byte _buttons;
    private ushort _x, _y;

    public bool IsRunning => _provider != null;
    /// <summary>Un appareil (l'iPhone) a souscrit aux notifications HID.</summary>
    public bool IsLinked => (_report?.SubscribedClients.Count ?? 0) > 0;

    public event Action<string>? Log;
    /// <summary>Levée quand l'iPhone souscrit/se désabonne (connexion BLE effective).</summary>
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

        // Protocol Mode : mode rapport fixe (1), en lecture seule côté valeur.
        await CreateAsync(service, ProtocolModeUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read
                                       | GattCharacteristicProperties.WriteWithoutResponse,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = (new byte[] { 0x01 }).AsBuffer()
        });

        // HID Information : bcdHID 1.11, pays 0, flags RemoteWake|NormallyConnectable.
        await CreateAsync(service, HidInfoUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = (new byte[] { 0x11, 0x01, 0x00, 0x03 }).AsBuffer()
        });

        // HID Control Point : suspend/resume (ignoré, écriture sans réponse).
        await CreateAsync(service, HidControlUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.WriteWithoutResponse,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired
        });

        // Report Map : le descripteur HID ci-dessus.
        await CreateAsync(service, ReportMapUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = ReportMap.AsBuffer()
        });

        // Report (id 1, entrée) : notifiable + Report Reference obligatoire.
        var report = await CreateAsync(service, ReportUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read
                                       | GattCharacteristicProperties.Notify,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = (new byte[] { 0x01, 0, 0, 0, 0, 0, 0 }).AsBuffer()
        });
        _report = report.Characteristic;
        var dres = await _report.CreateDescriptorAsync(ReportRefUuid, new GattLocalDescriptorParameters
        {
            StaticValue = (new byte[] { 0x01, 0x01 }).AsBuffer(), // id 1, input
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired
        });
        if (dres.Error != BluetoothError.Success)
            throw new InvalidOperationException($"descripteur HID refusé ({dres.Error})");

        _report.SubscribedClientsChanged += (_, _) =>
            LinkChanged?.Invoke(IsLinked);

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
            return;
        var data = new byte[]
        {
            0x01, _buttons,
            (byte)(_x & 0xFF), (byte)(_x >> 8),
            (byte)(_y & 0xFF), (byte)(_y >> 8),
            0
        };
        try { _ = r.NotifyValueAsync(data.AsBuffer()); }
        catch (Exception ex) { Log?.Invoke($"ble: {ex.Message}"); }
    }

    public void PointerMove(ushort x, ushort y) { _x = x; _y = y; SendReport(); }

    public void PointerDown(ushort x, ushort y)
    {
        _x = x; _y = y; _buttons |= 0x01; SendReport();
    }

    public void PointerUp(ushort x, ushort y)
    {
        _x = x; _y = y; _buttons = 0; SendReport();
    }

    /// <summary>Clic : down puis up après un court délai.</summary>
    public void Click(ushort x, ushort y)
    {
        PointerDown(x, y);
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            PointerUp(x, y);
        });
    }

    public void Wheel(ushort x, ushort y, sbyte steps)
    {
        _x = x; _y = y;
        var r = _report;
        if (r == null || r.SubscribedClients.Count == 0)
            return;
        var data = new byte[]
        {
            0x01, _buttons,
            (byte)(_x & 0xFF), (byte)(_x >> 8),
            (byte)(_y & 0xFF), (byte)(_y >> 8),
            (byte)steps
        };
        try { _ = r.NotifyValueAsync(data.AsBuffer()); } catch { }
    }

    public void Stop()
    {
        try { _provider?.StopAdvertising(); } catch { }
        _report = null;
        _provider = null;
        LinkChanged?.Invoke(false);
        Log?.Invoke("ble: diffusion arrêtée");
    }

    public void Dispose() => Stop();
}
