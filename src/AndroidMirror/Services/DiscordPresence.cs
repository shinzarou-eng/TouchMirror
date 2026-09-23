using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TouchMirror.Services;

public sealed record PresenceSnapshot(int Mirrors, string? Device, string? Account, int Plugins, string? Game);

public sealed class DiscordPresence : IDisposable
{
    private const string ClientId = "1549406208774115339";
    private const int OpHandshake = 0;
    private const int OpFrame = 1;
    private const int OpClose = 2;
    private const int OpPing = 3;

    private readonly Func<PresenceSnapshot> _snapshot;
    private readonly Action<string> _log;
    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private NamedPipeClientStream? _pipe;
    private long _startTs;
    private bool _announced;

    public DiscordPresence(Func<PresenceSnapshot> snapshot, Action<string> log)
    {
        _snapshot = snapshot;
        _log = log;
    }

    public bool Enabled { get; private set; }

    public void SetEnabled(bool enabled)
    {
        if (enabled == Enabled)
            return;
        Enabled = enabled;
        if (enabled)
        {
            _cts = new CancellationTokenSource();
            _thread = new Thread(() => Loop(_cts.Token)) { IsBackground = true, Name = "discord-presence" };
            _thread.Start();
        }
        else
        {
            StopThread();
        }
    }

    private void StopThread()
    {
        try { _cts?.Cancel(); } catch { }
        var t = _thread;
        _thread = null;
        t?.Join(1500);
        try { _pipe?.Dispose(); } catch { }
        _pipe = null;
    }

    private void Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!TryConnect(ct))
                {
                    Wait(ct, 20000);
                    continue;
                }
                _startTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                while (!ct.IsCancellationRequested && _pipe != null && _pipe.IsConnected)
                {
                    SendActivity();
                    while (ReadFrame(_pipe, ct, 60) != null) { }
                    Wait(ct, 15000);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!_announced)
                {
                    _log($"discord: {ex.Message}");
                    _announced = true;
                }
            }
            finally
            {
                try { _pipe?.Dispose(); } catch { }
                _pipe = null;
            }
            Wait(ct, 20000);
        }
    }

    private static void Wait(CancellationToken ct, int ms)
    {
        try { Task.Delay(ms, ct).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
    }

    private bool TryConnect(CancellationToken ct)
    {
        for (var i = 0; i < 10 && !ct.IsCancellationRequested; i++)
        {
            try
            {
                var p = new NamedPipeClientStream(".", $"discord-ipc-{i}",
                    PipeDirection.InOut, PipeOptions.Asynchronous);
                p.Connect(800);
                WriteFrame(p, OpHandshake, $"{{\"v\":1,\"client_id\":\"{ClientId}\"}}");
                var frame = ReadFrame(p, ct, 4000);
                if (frame != null && frame.Contains("\"READY\"", StringComparison.Ordinal))
                {
                    _pipe = p;
                    if (!_announced)
                    {
                        _log("discord rich presence : actif");
                        _announced = true;
                    }
                    return true;
                }
                p.Dispose();
            }
            catch { }
        }
        return false;
    }

    private void SendActivity()
    {
        var s = _snapshot();
        var n = Math.Max(0, s.Mirrors);
        var state = n == 0 ? "En attente d'un téléphone" : BuildState(s, n);
        var activity = new JsonObject
        {
            ["details"] = !string.IsNullOrWhiteSpace(s.Game)
                ? $"Joue à {s.Game}"
                : "Mirroring Android sur PC",
            ["state"] = state.Length > 128 ? state[..128] : state,
            ["timestamps"] = new JsonObject { ["start"] = _startTs },
            ["assets"] = new JsonObject
            {
                ["large_image"] = "logo",
                ["large_text"] = "TouchMirror",
            },
            ["instance"] = false,
        };
        var payload = new JsonObject
        {
            ["cmd"] = "SET_ACTIVITY",
            ["nonce"] = Guid.NewGuid().ToString("N"),
            ["args"] = new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["activity"] = activity,
            },
        };
        WriteFrame(_pipe!, OpFrame, payload.ToJsonString());
    }

    private static string BuildState(PresenceSnapshot s, int n)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.Device))
            parts.Add(s.Device!);
        if (!string.IsNullOrWhiteSpace(s.Account))
            parts.Add(s.Account!);
        if (parts.Count == 0)
            parts.Add(n == 1 ? "1 miroir actif" : $"{n} miroirs — multicompte");
        else if (n > 1)
            parts.Add($"{n} miroirs");
        if (s.Plugins > 0)
            parts.Add(s.Plugins == 1 ? "1 plugin actif" : $"{s.Plugins} plugins actifs");
        return string.Join(" · ", parts);
    }

    private static void WriteFrame(NamedPipeClientStream p, int op, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var head = new byte[8];
        BitConverter.GetBytes(op).CopyTo(head, 0);
        BitConverter.GetBytes(body.Length).CopyTo(head, 4);
        p.Write(head, 0, head.Length);
        p.Write(body, 0, body.Length);
        p.Flush();
    }

    private static string? ReadFrame(NamedPipeClientStream p, CancellationToken ct, int timeoutMs)
    {
        try
        {
            var head = ReadExact(p, 8, ct, timeoutMs);
            if (head == null)
                return null;
            var op = BitConverter.ToInt32(head, 0);
            var len = BitConverter.ToInt32(head, 4);
            if (len <= 0 || len > 1_000_000)
                return null;
            var body = ReadExact(p, len, ct, timeoutMs);
            if (body == null)
                return null;
            if (op == OpPing)
                WriteFrame(p, OpPing, Encoding.UTF8.GetString(body));
            return Encoding.UTF8.GetString(body);
        }
        catch { return null; }
    }

    private static byte[]? ReadExact(Stream s, int len, CancellationToken ct, int timeoutMs)
    {
        var buf = new byte[len];
        var off = 0;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (off < len)
        {
            if (ct.IsCancellationRequested || Environment.TickCount64 > deadline)
                return null;
            var task = s.ReadAsync(buf, off, len - off, ct);
            var waitMs = (int)Math.Max(1, Math.Min(500, deadline - Environment.TickCount64));
            if (!task.Wait(waitMs, ct))
            {
                if (Environment.TickCount64 > deadline)
                    return null;
                continue;
            }
            var got = task.Result;
            if (got <= 0)
                return null;
            off += got;
        }
        return buf;
    }

    public void Dispose()
    {
        SetEnabled(false);
    }
}

