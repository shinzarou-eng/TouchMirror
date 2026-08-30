using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchMirror.Services;

public sealed class MarketplaceEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Official { get; set; }
    public bool Featured { get; set; }
    public List<string> Tags { get; set; } = new();
    public string Hash { get; set; } = "";
    public string? MinAppVersion { get; set; }
}

public static class MarketplaceService
{
    private const string BaseUrl =
        "https://raw.githubusercontent.com/shinzarou-eng/TouchMirror/main/marketplace/";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static HttpClient NewHttp()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TouchMirror");
        http.Timeout = TimeSpan.FromSeconds(15);
        return http;
    }

    public static async Task<List<MarketplaceEntry>> FetchAsync(CancellationToken ct = default)
    {
        using var http = NewHttp();
        var json = await http.GetStringAsync(BaseUrl + "index.json", ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("plugins", out var arr))
            return new List<MarketplaceEntry>();
        var list = JsonSerializer.Deserialize<List<MarketplaceEntry>>(arr.GetRawText(), JsonOpts)
                   ?? new List<MarketplaceEntry>();
        list.RemoveAll(e => string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.Hash));
        return list;
    }

    public static async Task<string> FetchCodeAsync(string id, CancellationToken ct = default)
    {
        using var http = NewHttp();
        return await http.GetStringAsync($"{BaseUrl}{id}/plugin.js", ct);
    }

    public static async Task InstallAsync(MarketplaceEntry entry, string pluginsDir, CancellationToken ct = default)
    {
        using var http = NewHttp();
        var js = await http.GetByteArrayAsync($"{BaseUrl}{entry.Id}/plugin.js", ct);
        var manifest = await http.GetByteArrayAsync($"{BaseUrl}{entry.Id}/plugin.json", ct);

        var hash = Convert.ToHexString(SHA256.HashData(js.Concat(manifest).ToArray()))
            .ToLowerInvariant();
        if (!string.Equals(hash, entry.Hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("hash non conforme au catalogue — installation refusée");

        var dir = Path.Combine(pluginsDir, entry.Id);
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "plugin.js"), js, ct);
        await File.WriteAllBytesAsync(Path.Combine(dir, "plugin.json"), manifest, ct);
    }
}
