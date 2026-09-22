using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Jint;
using Jint.Native;
using Jint.Native.Function;

namespace TouchMirror.Services;

public sealed class PluginManifest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Icon { get; set; }
}

public partial class PluginInstance : ObservableObject
{
    public string FilePath { get; }
    public string Id { get; }
    public string Name { get; }
    public string? Description { get; }
    public string? Version { get; }
    public string? Author { get; }
    public string Icon { get; }
    public Wpf.Ui.Controls.SymbolRegular Symbol => PluginIcons.For(Id);
    [ObservableProperty] private bool _isVerified;
    public string? ContentHash { get; private set; }
    [ObservableProperty] private bool _running;
    public int GroupIndex { get; private set; } = 1;
    public string GroupLabel => GroupIndex == 0
        ? LocalizationService.Get("plugins_grp_image")
        : LocalizationService.Get("plugins_grp_fond");

    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private readonly object _queueLock = new();
    private readonly Queue<Action> _queue = new();
    private readonly List<JsTimer> _timers = new();
    private int _nextTimerId = 1;

    private sealed record JsTimer(int Id, JsValue Fn, int IntervalMs, bool Repeat, DateTime Due);

    public event Action<string>? Output;

    internal void Emit(string line) => Output?.Invoke(line);

    public PluginInstance(string path)
    {
        FilePath = path;
        var dir = Path.GetDirectoryName(path)!;
        var manifestPath = Path.Combine(dir, "plugin.json");
        PluginManifest? m = null;
        if (File.Exists(manifestPath))
        {
            try
            {
                m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { }
        }
        Id = Path.GetFileName(path).Equals("plugin.js", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(dir)
            : Path.GetFileNameWithoutExtension(path);
        Name = m?.Name ?? Id;
        Description = m?.Description;
        Version = m?.Version;
        Author = m?.Author;
        Icon = m?.Icon ?? "🧩";
        IsVerified = ComputeIsVerified();
    }

    private byte[]? _verifiedCode;

    public bool VerifyNow()
    {
        try
        {
            var bytes = File.ReadAllBytes(FilePath);
            var manifestPath = Path.Combine(Path.GetDirectoryName(FilePath)!, "plugin.json");
            var manifest = File.Exists(manifestPath)
                ? File.ReadAllBytes(manifestPath)
                : Array.Empty<byte>();
            ContentHash = Convert.ToHexString(
                SHA256.HashData(bytes.Concat(manifest).ToArray()));
            IsVerified = VerifiedPlugins.Hashes.Contains(ContentHash);
            GroupIndex = PluginAudit.UsesOverlay(Encoding.UTF8.GetString(bytes)) ? 0 : 1;
            _verifiedCode = bytes;
        }
        catch { ContentHash = null; IsVerified = false; _verifiedCode = null; }
        return IsVerified;
    }

    private bool ComputeIsVerified() => VerifyNow();

    private PluginApi? _api;
    private Jint.Engine? _engine;
    private readonly Dictionary<string, List<JsValue>> _handlers = new();

    public void Start(PluginApi api)
    {
        if (_thread != null)
            return;
        _api = api;
        _cts = new CancellationTokenSource();
        _thread = new Thread(() => Run(api, _cts.Token)) { IsBackground = true, Name = $"plugin-{Id}" };
        _thread.Start();
        Running = true;
    }

    public void Stop()
    {
        _cts?.Cancel();
        lock (_queueLock) { _queue.Clear(); Monitor.PulseAll(_queueLock); }
        _thread?.Join(1500);
        _thread = null;
        _engine = null;
        _handlers.Clear();
        _timers.Clear();
        _api?.Cleanup();
        _api = null;
        try { Application.Current?.Dispatcher.Invoke(() => Running = false); }
        catch { }
    }

    private const int MaxQueuedEvents = 256;

    public void DispatchEvent(string json)
    {
        lock (_queueLock)
        {
            if (_queue.Count >= MaxQueuedEvents)
                return;
            _queue.Enqueue(() => FireEvent(json));
            Monitor.Pulse(_queueLock);
        }
    }

    private void FireEvent(string json)
    {
        var e = _engine;
        if (e == null)
            return;
        try
        {
            var doc = JsonDocument.Parse(json);
            var type = doc.RootElement.TryGetProperty("type", out var tp)
                ? tp.GetString() ?? "" : "";
            if (!_handlers.TryGetValue(type, out var fns))
                return;
            var raw = doc.RootElement.TryGetProperty("data", out var dp)
                ? dp.GetRawText() : "null";
            var data = e.Evaluate("(" + raw + ")");
            foreach (var fn in fns.ToArray())
                ((Function)fn).Call(JsValue.Undefined, new[] { data });
        }
        catch (Exception ex) { Output?.Invoke($"event: {ex.Message}"); }
    }

    private const int MaxTimers = 64;
    private const int MaxHandlers = 128;

    private int AddTimer(JsValue fn, int ms, bool repeat)
    {
        if (_timers.Count >= MaxTimers)
        {
            Output?.Invoke($"limite de timers atteinte ({MaxTimers})");
            return -1;
        }
        var id = _nextTimerId++;
        _timers.Add(new JsTimer(id, fn, Math.Max(16, ms), repeat, DateTime.UtcNow.AddMilliseconds(ms)));
        lock (_queueLock) Monitor.Pulse(_queueLock);
        return id;
    }

    private void ClearTimer(int id) => _timers.RemoveAll(t => t.Id == id);

    private void RunTimers()
    {
        var now = DateTime.UtcNow;
        foreach (var t in _timers.Where(t => t.Due <= now).ToArray())
        {
            _timers.Remove(t);
            if (t.Repeat)
                _timers.Add(t with { Due = now.AddMilliseconds(t.IntervalMs) });
            try { ((Function)t.Fn).Call(JsValue.Undefined); }
            catch (Exception ex) { Output?.Invoke($"timer: {ex.Message}"); }
        }
    }

    private int NextDueMs()
    {
        if (_timers.Count == 0)
            return 60000;
        var ms = (int)(_timers.Min(t => t.Due) - DateTime.UtcNow).TotalMilliseconds;
        return Math.Clamp(ms, 5, 60000);
    }

    private void Run(PluginApi api, CancellationToken ct)
    {
        try
        {
            var engine = new Jint.Engine(o =>
            {
                o.LimitMemory(8_000_000);
                o.LimitRecursion(64);
                o.MaxStatements(1_000_000);
                o.TimeoutInterval(TimeSpan.FromSeconds(5));
                o.CancellationToken(ct);
                o.Constraints.MaxArraySize = 10_000;
                o.Constraints.RegexTimeout = TimeSpan.FromMilliseconds(250);
            });
            _engine = engine;

            engine.SetValue("__call", new Func<string, string?, string?>(api.Call));
            engine.SetValue("__schedule", new Func<JsValue, int, bool, int>(AddTimer));
            engine.SetValue("__clearTimer", new Action<int>(ClearTimer));
            engine.SetValue("__on", new Action<string, JsValue>((ev, fn) =>
            {
                if (!_handlers.TryGetValue(ev, out var l))
                    _handlers[ev] = l = new();
                if (_handlers.Values.Sum(x => x.Count) >= MaxHandlers)
                {
                    Output?.Invoke($"limite de handlers atteinte ({MaxHandlers})");
                    return;
                }
                l.Add(fn);
            }));

            engine.Execute(Prelude, "tm-prelude.js");
            var code = _verifiedCode ?? File.ReadAllBytes(FilePath);
            engine.Execute(System.Text.Encoding.UTF8.GetString(code), Path.GetFileName(FilePath));

            while (!ct.IsCancellationRequested)
            {
                Action? work = null;
                lock (_queueLock)
                {
                    if (_queue.Count > 0)
                        work = _queue.Dequeue();
                    else
                        Monitor.Wait(_queueLock, NextDueMs());
                }
                if (work != null)
                {
                    try { work(); }
                    catch (Exception ex) { Output?.Invoke(ex.Message); }
                }
                RunTimers();
            }
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Output?.Invoke($"moteur arrêté : {ex.Message}");
        }
        finally
        {
            try { Application.Current?.Dispatcher.Invoke(() => Running = false); }
            catch { }
        }
    }

    private const string Prelude = """
        const tm = {
          log:        (...a) => __call('log', a.map(String).join(' ')),
          getStatus:   ()    => JSON.parse(__call('status')),
          getMirrors:  ()    => JSON.parse(__call('mirrors')),
          getDevices:  ()    => JSON.parse(__call('devices')),
          activate:    s     => JSON.parse(__call('activate',    String(s))),
          record:      s     => JSON.parse(__call('record',      String(s))),
          screenshot:  s     => JSON.parse(__call('screenshot',  String(s))),
          disconnect:  s     => JSON.parse(__call('disconnect',  String(s))),
          connect:     s     => JSON.parse(__call('connect',     String(s))),
          mute:      (s, m)  => JSON.parse(__call('mute', JSON.stringify({ slot: s, muted: !!m }))),
          read:      name    => JSON.parse(__call('read', String(name))),
          write:   (name, d) => JSON.parse(__call('write', JSON.stringify(
                                  { name: String(name), data: String(d) }))),
          overlay:    (s, o) => JSON.parse(__call('overlay', JSON.stringify(Object.assign({ slot: s }, o || {})))),
          push:       (s, v) => JSON.parse(__call('push', JSON.stringify(
                                  typeof v === 'object' ? Object.assign({ slot: s }, v)
                                                        : { slot: s, value: +v }))),
          on:          (ev, fn) => __on(ev, fn),
          setTimeout:  (fn, ms) => __schedule(fn, ms, false),
          setInterval: (fn, ms) => __schedule(fn, ms, true),
          clearTimeout:  (id)   => __clearTimer(id),
          clearInterval: (id)   => __clearTimer(id),
        };
        """;
}
