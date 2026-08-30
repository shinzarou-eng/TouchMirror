using System.Text.Json;
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

    // Rate-limit : un plugin ne peut pas appeler l'API plus de 30×/s.
    private const int MaxCallsPerSecond = 30;
    private int _windowCalls;
    private DateTime _windowStart = DateTime.UtcNow;

    public PluginApi(LocalApiHost host, Action<string> log)
    {
        _host = host;
        _log = log;
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
        if (method is "activate" or "record" or "screenshot" or "disconnect" or "connect")
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
                _ => new LocalApiHost.ApiResult(false, $"méthode inconnue : {method}")
            };
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

    private LocalApiHost.ApiResult Log(string? msg)
    {
        _log(msg is { Length: > 1000 } ? msg[..1000] + "…" : msg ?? "");
        return new LocalApiHost.ApiResult(true);
    }
}
