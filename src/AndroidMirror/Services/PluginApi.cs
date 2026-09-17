using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace TouchMirror.Services;

/// <summary>
/// Surface offerte aux plugins JS (pont __call -> JSON).
/// Control-plane uniquement : jamais de tactile, clavier, texte ou
/// presse-papiers vers le téléphone. Les appels bloquent le thread du
/// plugin en attendant le dispatcher UI pour rester synchrone.
/// </summary>
public sealed class PluginApi
{
    private static readonly JsonSerializerOptions JsonOpts = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly LocalApiHost _host;
    private readonly Action<string> _log;
    /// <summary>Id du plugin servi — scope par défaut des widgets overlay.</summary>
    private readonly string _pluginId;
    /// <summary>Dossier confiné du plugin — seule zone du disque accessible
    /// via tm.read / tm.write (chemin absolu normalisé, "" = pas d'accès).</summary>
    private readonly string _dir;

    // Rate-limit : un plugin ne peut pas appeler l'API plus de 30×/s.
    private const int MaxCallsPerSecond = 30;
    private int _windowCalls;
    private DateTime _windowStart = DateTime.UtcNow;

    /// <summary>Widgets (slot, id) affichés par ce plugin — masqués à l'arrêt.</summary>
    private readonly HashSet<(int Slot, string Id)> _overlays = new();

    public PluginApi(LocalApiHost host, Action<string> log, string pluginId = "",
        string pluginDir = "")
    {
        _host = host;
        _log = log;
        _pluginId = pluginId;
        _dir = string.IsNullOrEmpty(pluginDir) ? "" : Path.GetFullPath(pluginDir);
    }

    private bool TryAcquireCall()
    {
        var now = DateTime.UtcNow;
        if (now - _windowStart > TimeSpan.FromSeconds(1))
        {
            _windowStart = now;
            _windowCalls = 0;
        }
        return ++_windowCalls <= MaxCallsPerSecond;
    }

    public string? Call(string method, string? arg)
    {
        if (!TryAcquireCall())
            return JsonSerializer.Serialize(
                new LocalApiHost.ApiResult(false, "limite d'appels dépassée"), JsonOpts);
        // Journal des actions mutantes : l'utilisateur voit ce que fait le plugin.
        if (method is "activate" or "record" or "screenshot" or "disconnect" or "connect" or "mute")
            _log($"[api] {method} {arg}");
        try
        {
            var r = method switch
            {
                "log" => Log(arg),
                "status" => Wait(_host.GetStatusAsync()),
                "mirrors" => Wait(_host.GetMirrorsAsync()),
                "devices" => Wait(_host.GetDevicesAsync()),
                "activate" => Wait(_host.ActivateAsync(Int(arg))),
                "record" => Wait(_host.ToggleRecordingAsync(Int(arg))),
                "screenshot" => Wait(_host.ScreenshotAsync(Int(arg))),
                "disconnect" => Wait(_host.DisconnectMirrorAsync(Int(arg))),
                "connect" => Wait(_host.ConnectAsync(arg ?? "")),
                "mute" => Mute(arg),
                "overlay" => Wait(_host.SetOverlayAsync(WithId(arg))),
                "push" => Wait(_host.PushOverlayValueAsync(WithId(arg))),
                "read" => ReadFile(arg),
                "write" => WriteFile(arg),
                _ => new LocalApiHost.ApiResult(false, $"méthode inconnue : {method}")
            };
            if (method == "overlay" && r.Ok)
                TrackOverlay(arg);
            return method == "log" ? null : JsonSerializer.Serialize(r, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new LocalApiHost.ApiResult(false, ex.Message), JsonOpts);
        }
    }

    // Le dispatcher UI ne bloque jamais sur le thread plugin → pas de deadlock.
    private static T Wait<T>(Task<T> t) => t.GetAwaiter().GetResult();

    private static int Int(string? s) => int.TryParse(s, out var v) ? v : 0;

    private sealed record MuteArg(int Slot, bool Muted);

    private LocalApiHost.ApiResult Mute(string? arg)
    {
        try
        {
            var o = JsonSerializer.Deserialize<MuteArg>(arg ?? "{}", JsonOpts);
            return o == null
                ? new LocalApiHost.ApiResult(false, "options invalides")
                : Wait(_host.SetAudioMutedAsync(o.Slot, o.Muted));
        }
        catch (JsonException) { return new LocalApiHost.ApiResult(false, "options invalides"); }
    }

    // ── Fichiers confinés au dossier du plugin ─────────────────────
    // Config / données seulement : extensions whitelistées (pas de .js —
    // le code d'un plugin ne peut pas se réécrire), pas de « .. » ni de
    // chemin absolu, 256 Ko max. Jamais exposé sur l'API HTTP.

    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".json", ".csv", ".log" };
    private const int MaxFileBytes = 256 * 1024;

    private string? SafePath(string? name)
    {
        if (_dir.Length == 0 || string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return null;
        var full = Path.GetFullPath(Path.Combine(_dir, name));
        if (!full.StartsWith(_dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;
        return AllowedExt.Contains(Path.GetExtension(full)) ? full : null;
    }

    private LocalApiHost.ApiResult ReadFile(string? arg)
    {
        var path = SafePath(arg);
        if (path == null)
            return new LocalApiHost.ApiResult(false, "chemin refusé");
        try
        {
            if (!File.Exists(path))
                return new LocalApiHost.ApiResult(true, Data: null); // absent ≠ erreur
            if (new FileInfo(path).Length > MaxFileBytes)
                return new LocalApiHost.ApiResult(false, "fichier trop gros");
            return new LocalApiHost.ApiResult(true, Data: File.ReadAllText(path));
        }
        catch (Exception ex) { return new LocalApiHost.ApiResult(false, ex.Message); }
    }

    private sealed record WriteArg(string? Name, string? Data);

    private LocalApiHost.ApiResult WriteFile(string? arg)
    {
        try
        {
            var o = JsonSerializer.Deserialize<WriteArg>(arg ?? "{}", JsonOpts);
            var path = o == null ? null : SafePath(o.Name);
            if (path == null || o?.Data == null)
                return new LocalApiHost.ApiResult(false, "options invalides");
            if (Encoding.UTF8.GetByteCount(o.Data) > MaxFileBytes)
                return new LocalApiHost.ApiResult(false, "fichier trop gros");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, o.Data);
            return new LocalApiHost.ApiResult(true);
        }
        catch (Exception ex) { return new LocalApiHost.ApiResult(false, ex.Message); }
    }

    private LocalApiHost.ApiResult Log(string? msg)
    {
        _log(msg is { Length: > 1000 } ? msg[..1000] + "…" : msg ?? "");
        return new LocalApiHost.ApiResult(true);
    }

    /// <summary>Un plugin qui ne précise pas d'id overlay reçoit le sien —
    /// chaque plugin obtient ainsi son propre widget par miroir.</summary>
    private string WithId(string? arg)
    {
        try
        {
            var node = JsonNode.Parse(arg ?? "{}") as JsonObject;
            if (node == null)
                return arg ?? "{}";
            if (!node.ContainsKey("id"))
                node["id"] = _pluginId;
            return node.ToJsonString();
        }
        catch (JsonException) { return arg ?? "{}"; }
    }

    private void TrackOverlay(string? arg)
    {
        try
        {
            using var doc = JsonDocument.Parse(WithId(arg));
            var root = doc.RootElement;
            if (!root.TryGetProperty("slot", out var s) || !s.TryGetInt32(out var slot))
                return;
            if (!root.TryGetProperty("visible", out var v))
                return;
            var id = root.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            if (v.ValueKind != JsonValueKind.False) _overlays.Add((slot, id));
            else _overlays.Remove((slot, id));
        }
        catch (JsonException) { }
    }

    /// <summary>Masque les overlays créés par ce plugin — appelé à l'arrêt.
    /// Fire-and-forget : Stop() peut tourner sur le thread UI.</summary>
    public void Cleanup()
    {
        foreach (var (slot, id) in _overlays)
            _ = _host.SetOverlayAsync(
                $"{{\"slot\":{slot},\"id\":\"{id}\",\"visible\":false}}");
        _overlays.Clear();
    }
}
