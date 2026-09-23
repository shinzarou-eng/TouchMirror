using System.Text.Json;
using TouchMirror.Services;
using Xunit;

namespace TouchMirror.Tests;

public sealed class EventBusTests
{
    [Fact]
    public async Task Publish_SubscriberReceivesJson()
    {
        var host = new LocalApiHost(null!);
        using var sub = host.SubscribeEvents(out var reader);
        host.Publish("mirror.connected", new { slot = 2 });
        var json = await reader.ReadAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("mirror.connected", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("data").GetProperty("slot").GetInt32());
    }

    [Fact]
    public async Task Flood_BoundedChannelSurvives()
    {
        var host = new LocalApiHost(null!);
        using var sub = host.SubscribeEvents(out var reader);
        for (var i = 0; i < 500; i++)
            host.Publish("tick", new { i });
        var got = 0;
        while (reader.TryRead(out _)) got++;
        Assert.InRange(got, 1, 64);
    }

    [Fact]
    public void Unsubscribe_DisposesCleanly()
    {
        var host = new LocalApiHost(null!);
        var sub = host.SubscribeEvents(out _);
        sub.Dispose();
        var ex = Record.Exception(() => host.Publish("x", new { }));
        Assert.Null(ex);
    }
}
