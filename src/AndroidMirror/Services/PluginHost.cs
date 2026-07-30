using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Jint;
using Jint.Native;
using Jint.Native.Function;

namespace TouchMirror.Services;

/// <summary>Métadonnées d'un plugin (plugin.json à côté de plugin.js).</summary>
public sealed class PluginManifest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Icon { get; set; }
}

/// <summary>
/// Plugin utilisateur en JavaScript, exécuté en sandbox (Jint) dans un thread
/// dédié. Le script ne voit que l'objet tm.* (control-plane : miroirs,
/// connexion, capture, enregistrement) — aucun accès fichier, processus,
/// réseau ou injection d'input vers le téléphone.
/// Structure : plugins/<id>/plugin.js (+ plugin.json optionnel) ou plugins/<id>.js
/// </summary>
public partial class PluginInstance : ObservableObject
{
    public string FilePath { get; }
    /// <summary>Identifiant stable : nom du dossier ou du fichier .js.</summary>
    public string Id { get; }
    public string Name { get; }
    public string? Description { get; }
    public string? Version { get; }
    public string? Author { get; }
    public string Icon { get; }
    [ObservableProperty] private bool _isVerified;
    /// <summary>Hash SHA-256 du plugin.js au dernier scan/vérification.</summary>
    public string? ContentHash { get; private set; }
    [ObservableProperty] private bool _running;

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
            try { m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath)); }
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

    /// <summary>Revérifie le hash du fichier à l'instant du lancement.</summary>
    public bool VerifyNow()
    {
        try
        {
            using var fs = File.OpenRead(FilePath);
            ContentHash = Convert.ToHexString(SHA256.HashData(fs));
            IsVerified = VerifiedPlugins.Hashes.Contains(ContentHash);
        }
        catch { ContentHash = null; IsVerified = false; }
        return IsVerified;
    }

    private bool ComputeIsVerified() => VerifyNow();

    // ── API exposée au script ────────────────────────────────────────

    private PluginApi? _api;
    private Engine? _engine;
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
        _api = null;
        try { Application.Current?.Dispatcher.Invoke(() => Running = false); }
        catch { }
    }

    /// <summary>Appelé par l'app quand un événement TM est publié.</summary>
    public void DispatchEvent(string json)
    {
        lock (_queueLock)
        {
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
            var data = e.Evaluate(raw);
            foreach (var fn in fns.ToArray())
                ((Function)fn).Call(JsValue.Undefined, data);
        }
        catch (Exception ex) { Output?.Invoke($"event: {ex.Message}"); }
    }

    private int AddTimer(JsValue fn, int ms, bool repeat)
    {
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

    // ── Moteur ───────────────────────────────────────────────────────

    private void Run(PluginApi api, CancellationToken ct)
    {
        try
        {
            var engine = new Engine(o => o
                .LimitMemory(8_000_000)
                .MaxStatements(1_000_000)
                .TimeoutInterval(TimeSpan.FromSeconds(5))
                .CancellationToken(ct));
            _engine = engine;

            // pont unique : __call(méthode, arg?) -> JSON string
            engine.SetValue("__call", new Func<string, string?, string?>(api.Call));
            engine.SetValue("__schedule", new Func<JsValue, int, bool, int>(AddTimer));
            engine.SetValue("__clearTimer", new Action<int>(ClearTimer));
            engine.SetValue("__on", new Action<string, JsValue>((ev, fn) =>
            {
                if (!_handlers.TryGetValue(ev, out var l))
                    _handlers[ev] = l = new();
                l.Add(fn);
            }));

            engine.Execute(Prelude, "tm-prelude.js");
            engine.Execute(File.ReadAllText(FilePath), Path.GetFileName(FilePath));

            // boucle d'événements : timers + events publiés
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
            // arrêt demandé — sortie silencieuse
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

    /// <summary>Surface JS : tm.* — control-plane uniquement.</summary>
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
          on:          (ev, fn) => __on(ev, fn),
          setTimeout:  (fn, ms) => __schedule(fn, ms, false),
          setInterval: (fn, ms) => __schedule(fn, ms, true),
          clearTimeout:  (id)   => __clearTimer(id),
          clearInterval: (id)   => __clearTimer(id),
        };
        """;
}
