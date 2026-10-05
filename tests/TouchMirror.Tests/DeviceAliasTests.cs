using TouchMirror.Services;
using Xunit;

namespace TouchMirror.Tests;

public sealed class DeviceAliasTests
{
    private static AdbDevice Wifi(string ip = "192.168.1.10:5555", string? hw = null, string model = "SM_S928B")
        => new(ip, model, "device", HardwareSerial: hw);

    private static AdbDevice Usb(string serial = "R5CRA1B2C3D", string? hw = null, string model = "SM_S928B")
        => new(serial, model, "device", HardwareSerial: hw);

    [Fact]
    public void Wifi_Then_HardwareSerial_Migrates_Prefs()
    {
        var s = new AppSettings();
        s.Devices["192.168.1.10:5555"] = new DevicePrefs { CustomName = "S24 salon", Color = "#fff" };
        var d = Wifi(hw: "HWABC123");
        var key = SettingsStore.FindAliasKey(s, d, new[] { d });
        Assert.Equal("192.168.1.10:5555", key);
        SettingsStore.MigrateDeviceKey(s, key!, d.DeviceKey);
        Assert.True(s.Devices.ContainsKey("HWABC123"));
        Assert.Equal("S24 salon", s.Devices["HWABC123"].CustomName);
        Assert.False(s.Devices.ContainsKey("192.168.1.10:5555"));
    }

    [Fact]
    public void Usb_Appearance_With_AltSerial_Migrates_Wifi_Key()
    {
        var s = new AppSettings();
        s.Devices["192.168.1.10:5555"] = new DevicePrefs { CustomName = "wifi" };
        var d = Usb(hw: "HWABC123") with { AltSerial = "192.168.1.10:5555" };
        var key = SettingsStore.FindAliasKey(s, d, new[] { d });
        Assert.Equal("192.168.1.10:5555", key);
        SettingsStore.MigrateDeviceKey(s, key!, d.DeviceKey);
        Assert.True(s.Devices.ContainsKey("HWABC123"));
        Assert.Equal("wifi", s.Devices["HWABC123"].CustomName);
    }

    [Fact]
    public void Model_Match_Rescues_Orphaned_Ip_Key()
    {
        var s = new AppSettings();
        s.Devices["192.168.1.10:5555"] = new DevicePrefs { Model = "SM_S928B", CustomName = "seul" };
        var d = Usb(hw: "HWABC123");
        var key = SettingsStore.FindAliasKey(s, d, new[] { d });
        Assert.Equal("192.168.1.10:5555", key);
    }

    [Fact]
    public void Model_Match_Skipped_When_Two_Same_Models_Live()
    {
        var s = new AppSettings();
        s.Devices["192.168.1.10:5555"] = new DevicePrefs { Model = "SM_S928B" };
        var d1 = Usb("SER1", "HW1");
        var d2 = Usb("SER2", "HW2");
        var key = SettingsStore.FindAliasKey(s, d1, new[] { d1, d2 });
        Assert.Null(key);
    }

    [Fact]
    public void Existing_Key_Not_Migrated()
    {
        var s = new AppSettings();
        s.Devices["HWABC123"] = new DevicePrefs { CustomName = "actuel" };
        s.Devices["192.168.1.10:5555"] = new DevicePrefs { CustomName = "vieux" };
        var d = Wifi(hw: "HWABC123");
        Assert.Null(SettingsStore.FindAliasKey(s, d, new[] { d }));
    }

    [Fact]
    public void Migrate_Merges_Into_Existing_Target()
    {
        var s = new AppSettings();
        s.Devices["HW1"] = new DevicePrefs { CustomName = "garde" };
        s.Devices["ip:1"] = new DevicePrefs { Color = "#abc", Pinned = true };
        SettingsStore.MigrateDeviceKey(s, "ip:1", "HW1");
        var p = s.Devices["HW1"];
        Assert.Equal("garde", p.CustomName);
        Assert.Equal("#abc", p.Color);
        Assert.True(p.Pinned);
        Assert.False(s.Devices.ContainsKey("ip:1"));
    }

    [Fact]
    public void Migrate_Moves_Account_SubKeys_And_Workspaces()
    {
        var s = new AppSettings();
        s.Devices["ip:1"] = new DevicePrefs { CustomName = "tel" };
        s.Devices["ip:1#14"] = new DevicePrefs { CustomName = "compte14" };
        s.Workspaces.Add(new Workspace
        {
            Devices = { new WorkspaceDevice { DeviceKey = "ip:1#14" }, new WorkspaceDevice { DeviceKey = "ip:1" } },
            ActiveDeviceKey = "ip:1#14"
        });
        s.MirrorOrder.Add("ip:1#14");
        s.LastSelectedDeviceKey = "ip:1";
        SettingsStore.MigrateDeviceKey(s, "ip:1", "HW1");
        Assert.True(s.Devices.ContainsKey("HW1"));
        Assert.True(s.Devices.ContainsKey("HW1#14"));
        Assert.False(s.Devices.ContainsKey("ip:1#14"));
        Assert.Equal("HW1#14", s.Workspaces[0].Devices[0].DeviceKey);
        Assert.Equal("HW1", s.Workspaces[0].Devices[1].DeviceKey);
        Assert.Equal("HW1#14", s.Workspaces[0].ActiveDeviceKey);
        Assert.Equal("HW1#14", s.MirrorOrder[0]);
        Assert.Equal("HW1", s.LastSelectedDeviceKey);
    }

    [Fact]
    public void Migrate_Same_Key_Is_Noop()
    {
        var s = new AppSettings();
        s.Devices["HW1"] = new DevicePrefs { CustomName = "x" };
        SettingsStore.MigrateDeviceKey(s, "HW1", "HW1");
        Assert.Equal("x", s.Devices["HW1"].CustomName);
    }

    [Fact]
    public void No_Alias_When_Nothing_Known()
    {
        var s = new AppSettings();
        var d = Wifi(hw: "HW1");
        Assert.Null(SettingsStore.FindAliasKey(s, d, new[] { d }));
    }

    [Fact]
    public void Unauthorized_Serial_Migrates_To_Hardware()
    {
        var s = new AppSettings();
        s.Devices["usbser99"] = new DevicePrefs { CustomName = "avant auth" };
        var d = new AdbDevice("usbser99", "SM_S918B", "device", HardwareSerial: "HWREAL");
        var key = SettingsStore.FindAliasKey(s, d, new[] { d });
        Assert.Equal("usbser99", key);
        SettingsStore.MigrateDeviceKey(s, key!, d.DeviceKey);
        Assert.Equal("avant auth", s.Devices["HWREAL"].CustomName);
    }
}
