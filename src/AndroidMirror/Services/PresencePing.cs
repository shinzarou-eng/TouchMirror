using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace TouchMirror.Services;

public sealed class PresencePing : IDisposable
{
    private const string Endpoint = "https://touchmirror-ping-player-mode.vercel.app/api/ping";
    private const int IntervalMs = 5 * 60 * 1000;

    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly string _version;
    private readonly Action<string> _log;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private Thread? _thread;
    private CancellationTokenSource? _cts;

    public PresencePing(Action<string> log)
    {
        _log = log;
        _version = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?.Split('+')[0] ?? "0.0.0";
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
            _thread = new Thread(() => Loop(_cts.Token)) { IsBackground = true, Name = "presence-ping" };
            _thread.Start();
        }
        else
        {
            try { _cts?.Cancel(); } catch { }
            var t = _thread;
            _thread = null;
            t?.Join(1500);
        }
    }

    private void Loop(CancellationToken ct)
    {
        var announced = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var body = new JsonObject
                {
                    ["id"] = _sessionId,
                    ["v"] = _version,
                }.ToJsonString();
                var res = _http.PostAsync(Endpoint,
                    new StringContent(body, Encoding.UTF8, "application/json"), ct)
                    .GetAwaiter().GetResult();
                if (res.IsSuccessStatusCode && !announced)
                {
                    _log("statistiques anonymes : actives");
                    announced = true;
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            try { Task.Delay(IntervalMs, ct).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
        }
    }

    public void Dispose()
    {
        SetEnabled(false);
        _http.Dispose();
    }
}
