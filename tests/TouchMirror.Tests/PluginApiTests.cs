using System.Text.Json;
using TouchMirror.Services;
using Xunit;

namespace TouchMirror.Tests;

public sealed class PluginApiTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "tm-tests-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<string> _logs = new();

    private PluginApi Api => new(null!, s => _logs.Add(s), "test-plugin", _dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static (bool Ok, string? Msg, JsonElement Data) Call(PluginApi api, string m, string? arg)
    {
        var json = api.Call(m, arg);
        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;
        var ok = root.GetProperty("ok").GetBoolean();
        var msg = root.TryGetProperty("message", out var me) ? me.GetString() : null;
        return (ok, msg, root.GetProperty("data").Clone());
    }

    [Fact]
    public void Read_MissingFile_OkWithNullData()
    {
        var (ok, _, data) = Call(Api, "read", "state.json");
        Assert.True(ok);
        Assert.Equal(JsonValueKind.Null, data.ValueKind);
    }

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        var api = Api;
        var (wOk, _, _) = Call(api, "write", """{"name":"archis.txt","data":"# zone\n[ ] mob · 42"}""");
        Assert.True(wOk);
        var (rOk, _, data) = Call(api, "read", "archis.txt");
        Assert.True(rOk);
        Assert.Equal("# zone\n[ ] mob · 42", data.GetString());
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("..\\escape.txt")]
    [InlineData("sub/../../escape.txt")]
    [InlineData("C:/Windows/evil.txt")]
    [InlineData("/etc/passwd")]
    public void PathTraversal_Rejected(string name)
    {
        var (ok, msg, _) = Call(Api, "read", name);
        Assert.False(ok);
        Assert.Equal("chemin refusé", msg);
    }

    [Theory]
    [InlineData("plugin.js")]
    [InlineData("evil.exe")]
    [InlineData("run.ps1")]
    [InlineData("x.dll")]
    public void NonDataExtension_Rejected(string name)
    {
        var (ok, _, _) = Call(Api, "read", name);
        Assert.False(ok);
        var (wOk, _, _) = Call(Api, "write",
            JsonSerializer.Serialize(new { name, data = "x" }));
        Assert.False(wOk);
    }

    [Fact]
    public void Write_TooLarge_Rejected()
    {
        var big = new string('a', 257 * 1024);
        var (ok, msg, _) = Call(Api, "write",
            JsonSerializer.Serialize(new { name = "big.txt", data = big }));
        Assert.False(ok);
        Assert.Equal("fichier trop gros", msg);
    }

    [Fact]
    public void Name_TooLong_Rejected()
    {
        var (ok, _, _) = Call(Api, "read", new string('a', 201) + ".txt");
        Assert.False(ok);
    }

    [Fact]
    public void SubdirectoryInsidePlugin_Allowed()
    {
        var api = Api;
        var (wOk, _, _) = Call(api, "write",
            """{"name":"img/notes.txt","data":"hi"}""");
        Assert.True(wOk);
        Assert.True(File.Exists(Path.Combine(_dir, "img", "notes.txt")));
    }

    [Fact]
    public void UnknownMethod_Refused()
    {
        var (ok, msg, _) = Call(Api, "teleport", null);
        Assert.False(ok);
        Assert.Contains("méthode inconnue", msg);
    }

    [Fact]
    public void RateLimit_ThirtyPerSecond()
    {
        var api = Api;
        string? lastMsg = null;
        var ok = true;
        for (var i = 0; i < 31; i++)
            (ok, lastMsg, _) = Call(api, "nope", null);
        Assert.False(ok);
        Assert.Equal("limite d'appels dépassée", lastMsg);
    }

    [Fact]
    public void Log_TruncatesBeyond1000()
    {
        var api = Api;
        api.Call("log", new string('x', 1500));
        Assert.Single(_logs);
        Assert.Equal(1001, _logs[0].Length);
    }

    [Fact]
    public void WithId_InjectsPluginId()
    {
        var api = new PluginApi(null!, _ => { }, "archis", _dir);
        var result = api.WithId("""{"slot":1,"visible":true}""");
        using var doc = JsonDocument.Parse(result);
        Assert.Equal("archis", doc.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public void WithId_OverridesExplicitId()
    {
        var api = new PluginApi(null!, _ => { }, "archis", _dir);
        var result = api.WithId("""{"slot":1,"id":"autre-plugin"}""");
        using var doc = JsonDocument.Parse(result);
        Assert.Equal("archis", doc.RootElement.GetProperty("id").GetString());
    }
}
