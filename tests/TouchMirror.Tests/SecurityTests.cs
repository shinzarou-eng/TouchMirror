using System.Text.Json;
using TouchMirror.Services;
using TouchMirror.ViewModels;
using TouchMirror.Views;
using Xunit;

namespace TouchMirror.Tests;

public sealed class SecurityTests
{
    [Theory]
    [InlineData(@"..\..\x", "______x")]
    [InlineData("Galaxy S25", "Galaxy S25")]
    [InlineData("a/b\\c:d", "a_b_c_d")]
    [InlineData("", "device")]
    [InlineData("...", "___")]
    [InlineData("  ", "device")]
    public void SafeFileName_NeverEscapesDirectory(string model, string expected)
    {
        var s = MirrorInstance.SafeFileName(model);
        Assert.Equal(expected, s);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, s);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar, s);
    }

    [Theory]
    [InlineData("normal path.apk", "\"normal path.apk\"")]
    [InlineData("C:\\tmp\\x.apk", "\"C:\\tmp\\x.apk\"")]
    public void Q_QuotesSafePath(string path, string expected)
        => Assert.Equal(expected, AdbService.Q(path));

    [Theory]
    [InlineData("evil\"path")]
    [InlineData("line\nbreak")]
    [InlineData("carriage\rreturn")]
    public void Q_RejectsShellMetachars(string path)
        => Assert.Throws<ArgumentException>(() => AdbService.Q(path));

    [Theory]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("\\\\evil\\share\\x.png", false)]
    [InlineData("ftp://host/x.png", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://api.dofusdb.fr/img/x.png", true)]
    [InlineData("http://cdn.example.com/x.png", true)]
    [InlineData("not a uri", false)]
    public void ImgUrlAllowed_OnlyHttpHttps(string url, bool expected)
        => Assert.Equal(expected, GraphWidget.ImgUrlAllowed(url));

    [Fact]
    public void Call_AfterCleanup_Refused()
    {
        var api = new PluginApi(null!, _ => { }, "p", Path.GetTempPath());
        api.Cleanup();
        var json = api.Call("read", "x.txt");
        using var doc = JsonDocument.Parse(json!);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("plugin arrêté", doc.RootElement.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("archis", true)]
    [InlineData("../evil", false)]
    [InlineData("a b", false)]
    [InlineData("x.js", false)]
    public void MarketplaceId_Validated(string id, bool expected)
        => Assert.Equal(expected, MarketplaceService.IsValidId(id));

    [Fact]
    public void ParseMdnsEndpoints_OnlyConnectServices()
    {
        var output = string.Join('\n',
            "List of discovered mdns services",
            "adb-RFGL22M2JQM-abc123\t_adb-tls-connect._tcp\t192.168.1.42:38307",
            "studio-a1b2c3d4e5\t_adb-tls-pairing._tcp\t192.168.1.42:45555",
            "adb-other\t_adb-tls-connect._tcp\t192.168.1.77:37123",
            "garbage line");
        var eps = AdbService.ParseMdnsEndpoints(output, "_adb-tls-connect._tcp");
        Assert.Equal(new[] { "192.168.1.42:38307", "192.168.1.77:37123" }, eps);
    }

    [Fact]
    public void ParseMdnsEndpoints_EmptyOnNoise()
        => Assert.Empty(AdbService.ParseMdnsEndpoints("", "_adb-tls-connect._tcp"));
}
