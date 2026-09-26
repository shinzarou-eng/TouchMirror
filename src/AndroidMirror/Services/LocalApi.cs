using System.Text.Json;
using System.Threading.Channels;
using System.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TouchMirror.ViewModels;
using TouchMirror.Views;

namespace TouchMirror.Services;

public sealed class LocalApiHost
{
    private static readonly JsonSerializerOptions JsonOpts = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public sealed record MirrorDto(int Slot, string Name, string Serial, string Model,
        bool Connected, bool Active, bool Recording, bool Wifi, double Fps,
        int W, int H, double Lag, double Jit, int Bitrate, bool Muted, double Volume,
        long Pkts, long Dec, bool Stalled, int Front, long Inv, string Diag);
    public sealed record DeviceDto(string Serial, string Name, string Model,
        bool Ready, bool Remembered, bool Wifi, bool Blocked);
    public sealed record ApiResult(bool Ok, string? Message = null, object? Data = null);

    private readonly MainViewModel _vm;
    private readonly object _subLock = new();
    private readonly List<Channel<string>> _subscribers = new();

    public LocalApiHost(MainViewModel vm) => _vm = vm;

    public IDisposable SubscribeEvents(out ChannelReader<string> reader)
    {
        var ch = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        lock (_subLock)
            _subscribers.Add(ch);
        reader = ch.Reader;
        return new Subscription(this, ch);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly LocalApiHost _host;
        private readonly Channel<string> _ch;
        public Subscription(LocalApiHost host, Channel<string> ch) { _host = host; _ch = ch; }
        public void Dispose()
        {
            lock (_host._subLock)
                _host._subscribers.Remove(_ch);
            _ch.Writer.TryComplete();
        }
    }

    public event Action<string>? PluginEvent;

    public void Publish(string type, object data)
    {
        var json = JsonSerializer.Serialize(new { type, data }, JsonOpts);
        lock (_subLock)
            foreach (var ch in _subscribers)
                ch.Writer.TryWrite(json);
        PluginEvent?.Invoke(json);
    }

    private static Task<T> Ui<T>(Func<T> f)
        => Application.Current.Dispatcher.InvokeAsync(f).Task;

    private static Task<T> UiAsync<T>(Func<Task<T>> f)
        => Application.Current.Dispatcher.InvokeAsync(f).Task.Unwrap();

    public Task<ApiResult> GetStatusAsync() => Ui(() => new ApiResult(true, Data: new
    {
        version = typeof(LocalApiHost).Assembly.GetName().Version?.ToString(3),
        status = _vm.Status,
        mirrors = _vm.Mirrors.Count,
        activeSlot = _vm.ActiveMirror?.Slot ?? 0,
    }));

    public Task<ApiResult> GetMirrorsAsync() => Ui(() => new ApiResult(true,
        Data: _vm.Mirrors.Select(m => new MirrorDto(
            m.Slot, m.DeviceName, m.ResolvedDevice.Serial, m.ResolvedDevice.Model, m.IsConnected,
            ReferenceEquals(m, _vm.ActiveMirror), m.IsRecording, m.ResolvedDevice.IsWifi,
            m.View.CurrentFps, m.View.VideoWidth, m.View.VideoHeight,
            Math.Max(0, m.StreamLagMs), m.StreamJitterMs, m.CurrentBitRate,
            m.AudioMuted, m.AudioVolume, m.Session?.VideoPackets ?? -1,
            m.Decoder?.DecodedFrames ?? -1, m.RendererStalled, m.FrontOk, m.InvCopied,
            m.ViewDiag)).ToList()));

    public Task<ApiResult> GetDevicesAsync() => Ui(() => new ApiResult(true,
        Data: _vm.Devices.Select(d => new DeviceDto(
            d.Serial, d.DisplayName, d.Model, d.IsReady, d.IsRememberedOnly, d.IsWifi,
            _vm.IsVoluntarilyDisconnected(d.Serial))).ToList()));

    public Task<ApiResult> ActivateAsync(int slot) => Ui(() =>
        _vm.TryActivateSlot(slot)
            ? new ApiResult(true, $"Miroir {slot} actif")
            : new ApiResult(false, $"slot {slot} inconnu"));

    public Task<ApiResult> ToggleRecordingAsync(int slot) => Ui(() =>
    {
        var m = _vm.MirrorAtSlot(slot);
        if (m == null)
            return new ApiResult(false, $"slot {slot} inconnu");
        var msg = _vm.ToggleRecordingFor(m);
        return new ApiResult(true, msg, new { recording = m.IsRecording });
    });

    public Task<ApiResult> ScreenshotAsync(int slot) => Ui(() =>
    {
        var m = _vm.MirrorAtSlot(slot);
        if (m == null)
            return new ApiResult(false, $"slot {slot} inconnu");
        var path = _vm.RequestScreenshot(m);
        return path == null
            ? new ApiResult(false, "capture impossible (pas de flux vidéo)")
            : new ApiResult(true, "capture enregistrée", new { path });
    });

    public Task<ApiResult> DisconnectMirrorAsync(int slot) => UiAsync(async () =>
    {
        var m = _vm.MirrorAtSlot(slot);
        if (m == null)
            return new ApiResult(false, $"slot {slot} inconnu");
        await _vm.DisconnectMirrorAsync(m);
        return new ApiResult(true, $"miroir {slot} déconnecté");
    });

    public Task<ApiResult> ConnectAsync(string serial) => UiAsync(async () =>
    {
        var d = await _vm.FindDeviceBySerialAsync(serial);
        if (d == null)
            return new ApiResult(false, $"appareil {serial} introuvable");
        if (!d.IsReady)
            return new ApiResult(false, $"appareil non prêt ({d.State})");
        if (_vm.Mirrors.Any(m => m.Device.SharesIdentity(d)))
            return new ApiResult(true, "déjà connecté");
        await _vm.ConnectExistingDeviceAsync(d);
        return new ApiResult(true, $"connecté — {d.DisplayName}");
    });

    public Task<ApiResult> SetAudioMutedAsync(int slot, bool muted) => Ui(() =>
    {
        var m = _vm.MirrorAtSlot(slot);
        if (m == null)
            return new ApiResult(false, $"slot {slot} inconnu");
        m.SetAudioMuted(muted);
        return new ApiResult(true,
            muted ? $"miroir {slot} muet" : $"miroir {slot} sonore",
            new { muted });
    });

    public Task<ApiResult> SetAudioVolumeAsync(int slot, double volume) => Ui(() =>
    {
        var m = _vm.MirrorAtSlot(slot);
        if (m == null)
            return new ApiResult(false, $"slot {slot} inconnu");
        m.SetAudioVolume((float)Math.Clamp(double.IsFinite(volume) ? volume : 0, 0, 1));
        return new ApiResult(true, $"miroir {slot} volume {(int)Math.Round(volume * 100)}%",
            new { volume = m.AudioVolume });
    });

    private sealed record OverlayOpts(int Slot, string? Id, bool? Visible,
        string? Title, string? Color, bool? Compact, string? Pos, JsonElement[]? Lines);
    private sealed record OverlayPush(int Slot, string? Id, double Value, string? Label);

    internal static List<OverlayLine>? ToLines(JsonElement[]? a)
    {
        if (a == null) return null;
        var l = new List<OverlayLine>(a.Length);
        foreach (var e in a)
        {
            if (e.ValueKind == JsonValueKind.String)
            {
                l.Add(new(e.GetString() ?? "", null, null, null, null, null));
                continue;
            }
            if (e.ValueKind != JsonValueKind.Object) continue;
            var text = e.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            var kind = e.TryGetProperty("k", out var k) ? k.GetString() : null;
            var col = e.TryGetProperty("color", out var c) ? c.GetString() : null;
            var right = e.TryGetProperty("right", out var r) ? r.GetString() : null;
            bool? dn = e.TryGetProperty("done", out var d) &&
                d.ValueKind is JsonValueKind.True or JsonValueKind.False ? d.GetBoolean() : null;
            double? val = e.TryGetProperty("v", out var v) &&
                v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
            var img = e.TryGetProperty("img", out var im) ? im.GetString() : null;
            l.Add(new(text, kind, col, dn, right, val, img));
        }
        return l;
    }

    public Task<ApiResult> SetOverlayAsync(string json) => Ui(() =>
    {
        OverlayOpts? o;
        try { o = JsonSerializer.Deserialize<OverlayOpts>(json, JsonOpts); }
        catch { return new ApiResult(false, "options invalides"); }
        if (o == null)
            return new ApiResult(false, "options invalides");
        var m = _vm.MirrorAtSlot(o.Slot);
        if (m == null)
            return new ApiResult(false, $"slot {o.Slot} inconnu");
        m.View.SetGraphOverlay(o.Id ?? "default", o.Visible, o.Title,
            o.Color, o.Compact, o.Pos, ToLines(o.Lines));
        return new ApiResult(true);
    });

    public Task<ApiResult> PushOverlayValueAsync(string json) => Ui(() =>
    {
        OverlayPush? o;
        try { o = JsonSerializer.Deserialize<OverlayPush>(json, JsonOpts); }
        catch { return new ApiResult(false, "options invalides"); }
        if (o == null)
            return new ApiResult(false, "options invalides");
        var m = _vm.MirrorAtSlot(o.Slot);
        if (m == null)
            return new ApiResult(false, $"slot {o.Slot} inconnu");
        m.View.PushGraphValue(o.Id ?? "default", o.Value, o.Label);
        return new ApiResult(true);
    });
}

public sealed class LocalApiServer : IAsyncDisposable
{
    private WebApplication? _app;
    public int? Port { get; private set; }

    public async Task StartAsync(LocalApiHost host, int port, Func<string?> token)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();

        app.Use(async (ctx, next) =>
        {
            if (!ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            var expected = token();
            string? provided = null;
            var auth = (string?)ctx.Request.Headers.Authorization;
            if (auth != null && auth.StartsWith("Bearer ", StringComparison.Ordinal))
                provided = auth["Bearer ".Length..];
            else if (ctx.Request.Query.TryGetValue("token", out var q))
                provided = q;
            var authorized = expected is { Length: > 0 } && provided != null
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(expected),
                    System.Text.Encoding.UTF8.GetBytes(provided));
            if (!authorized)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "token manquant ou invalide" });
                return;
            }
            await next(ctx);
        });

        static IResult Http(LocalApiHost.ApiResult r)
            => r.Ok ? Results.Ok(r) : Results.NotFound(r);

        app.MapGet("/api/status", async () => Results.Ok(await host.GetStatusAsync()));
        app.MapGet("/api/mirrors", async () => Results.Ok(await host.GetMirrorsAsync()));
        app.MapGet("/api/devices", async () => Results.Ok(await host.GetDevicesAsync()));
        app.MapPost("/api/mirrors/{slot:int}/activate", async (int slot) => Http(await host.ActivateAsync(slot)));
        app.MapPost("/api/mirrors/{slot:int}/record", async (int slot) => Http(await host.ToggleRecordingAsync(slot)));
        app.MapPost("/api/mirrors/{slot:int}/screenshot", async (int slot) => Http(await host.ScreenshotAsync(slot)));
        app.MapPost("/api/mirrors/{slot:int}/disconnect", async (int slot) => Http(await host.DisconnectMirrorAsync(slot)));
        app.MapPost("/api/mirrors/{slot:int}/mute/{muted:bool}", async (int slot, bool muted) => Http(await host.SetAudioMutedAsync(slot, muted)));
        app.MapPost("/api/mirrors/{slot:int}/volume/{level:double}", async (int slot, double level) => Http(await host.SetAudioVolumeAsync(slot, level)));
        app.MapPost("/api/devices/{serial}/connect", async (string serial) => Http(await host.ConnectAsync(serial)));
        app.MapGet("/api/events", ctx => StreamEventsAsync(host, ctx));

        _app = app;
        Port = port;
        await app.StartAsync();
    }

    private static async Task StreamEventsAsync(LocalApiHost host, HttpContext ctx)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        try
        {
            await ctx.Response.WriteAsync("data: {\"type\":\"ready\"}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            using var sub = host.SubscribeEvents(out var reader);
            await foreach (var msg in reader.ReadAllAsync(ctx.RequestAborted))
            {
                await ctx.Response.WriteAsync($"data: {msg}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app == null)
            return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
        Port = null;
    }
}
