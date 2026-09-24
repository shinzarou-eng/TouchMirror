using TouchMirror.Services;
using Xunit;

namespace TouchMirror.Tests;

public sealed class ReportSanitizerTests
{
    private static string Sanitize(string report, params string[] knownIds)
        => ReportSanitizer.Sanitize(report, knownIds);

    [Fact]
    public void Masks_Ipv4_KeepingPrefix()
    {
        var s = Sanitize("ip locale : 192.168.1.42 — même réseau");
        Assert.Contains("192.168.", s);
        Assert.DoesNotContain("192.168.1.42", s);
    }

    [Fact]
    public void Masks_Ipv6()
    {
        var s = Sanitize("fe80::1ff:fe23:4567:890a répond");
        Assert.DoesNotContain("fe80::1ff:fe23:4567:890a", s);
    }

    [Fact]
    public void Masks_Mac()
    {
        var s = Sanitize("mac AA:BB:CC:11:22:33 détecté");
        Assert.Contains("xx:xx:xx:xx:xx:xx", s);
        Assert.DoesNotContain("AA:BB:CC:11:22:33", s);
    }

    [Fact]
    public void Masks_UserPath()
    {
        var s = Sanitize(@"log dans C:\Users\fbdyl\AppData\Local\TouchMirror\app.log");
        Assert.Contains(@"C:\Users\…", s);
        Assert.DoesNotContain("fbdyl", s);
    }

    [Fact]
    public void Masks_UserProfile_Anywhere()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            return;
        var s = Sanitize($"fichier {home}\\secret.txt");
        Assert.DoesNotContain(home, s);
    }

    [Fact]
    public void Masks_LongHex()
    {
        var ltpk = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";
        var s = Sanitize($"ltpk={ltpk}");
        Assert.DoesNotContain(ltpk, s);
    }

    [Fact]
    public void Masks_LongBase64()
    {
        var s = Sanitize("clé publique : QWxhZGRpbk9wZW5TZXNhbWUxMjM0NTY=");
        Assert.DoesNotContain("QWxhZGRpbk9wZW5TZXNhbWUxMjM0NTY=", s);
    }

    [Theory]
    [InlineData("token=abcdef123456", "abcdef123456")]
    [InlineData("key: abcdef123456", "abcdef123456")]
    [InlineData("Authorization: Bearer abcdef123456", "abcdef123456")]
    public void Masks_SecretValues(string line, string secret)
    {
        var s = Sanitize(line);
        Assert.DoesNotContain(secret, s);
    }

    [Fact]
    public void Masks_Email()
    {
        var s = Sanitize("compte foo.bar@example.com enregistré");
        Assert.DoesNotContain("foo.bar@example.com", s);
        Assert.Contains("[email]", s);
    }

    [Fact]
    public void Masks_MachineName()
    {
        var name = Environment.MachineName;
        if (string.IsNullOrEmpty(name))
            return;
        var s = Sanitize($"hôte {name} démarré");
        Assert.DoesNotContain(name, s);
    }

    [Fact]
    public void Masks_AdbSerialLine()
    {
        var s = Sanitize("List of devices attached\nRQ8N30ABCDEF\tdevice product:x model:y\n");
        Assert.DoesNotContain("RQ8N30ABCDEF", s);
        Assert.Contains("device", s);
    }

    [Fact]
    public void Masks_WirelessSerialLine()
    {
        var s = Sanitize("192.168.1.50:5555\tdevice product:x\n");
        Assert.DoesNotContain("192.168.1.50", s);
    }

    [Fact]
    public void Masks_KnownIds()
    {
        var s = Sanitize("client PAIR-CLIENT-XYZ appairé", "PAIR-CLIENT-XYZ");
        Assert.DoesNotContain("PAIR-CLIENT-XYZ", s);
    }

    [Fact]
    public void Idempotent()
    {
        var report = "ip 10.0.0.1 mac 11:22:33:44:55:66 serie ABC123 dans C:\\Users\\x\\y";
        var once = Sanitize(report, "ABC123");
        Assert.Equal(once, Sanitize(once, "ABC123"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void NullSafe(string? report)
        => Assert.Equal(report ?? "", ReportSanitizer.Sanitize(report!, Array.Empty<string>()));

    [Fact]
    public void Keeps_NonSensitiveText()
    {
        var report = "TouchMirror v0.8.6\n[OK] adb : présent\nétat=device prêt";
        Assert.Equal(report, Sanitize(report));
    }

    [Fact]
    public void RealisticReport_NoIdentifierSurvives()
    {
        var report = string.Join('\n',
            "TouchMirror v0.8.6",
            "== adb devices -l ==",
            "ZY22AABBCC             device usb:1-2 product:model_x",
            "== journal ==",
            "[OK] adb : présent",
            "[12:00:01.000] airplay: pair-verify de 192.168.1.232 deviceID=5C:96:56:AA:BB:CC",
            "[12:00:02.000] erreur dans C:\\Users\\bob\\x.log token=deadbeef1234",
            "airplay: client iphone-de-bob pairingID=7e6f5a4b3c2d1e0f7e6f5a4b3c2d1e0f");
        var s = Sanitize(report, "iphone-de-bob", "7e6f5a4b3c2d1e0f7e6f5a4b3c2d1e0f");
        Assert.DoesNotContain("ZY22AABBCC", s);
        Assert.DoesNotContain("192.168.1.232", s);
        Assert.DoesNotContain("5C:96:56:AA:BB:CC", s);
        Assert.DoesNotContain("bob", s);
        Assert.DoesNotContain("deadbeef1234", s);
        Assert.DoesNotContain("iphone-de-bob", s);
        Assert.Contains("[OK] adb : présent", s);
        Assert.Contains("== journal ==", s);
    }
}
