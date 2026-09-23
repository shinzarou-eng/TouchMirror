using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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

    private static readonly Regex IdOk = new(@"^[A-Za-z0-9_\-]{1,64}$", RegexOptions.Compiled);

    public static bool IsValidId(string? id) => id != null && IdOk.IsMatch(id);

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

    private static string? LocalCatalogDir()
    {
#if DEBUG
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var cand = Path.Combine(dir.FullName, "marketplace");
            if (File.Exists(Path.Combine(cand, "index.json")))
                return cand;
            dir = dir.Parent;
        }
#endif
        return null;
    }

    public static async Task<List<MarketplaceEntry>> FetchAsync(CancellationToken ct = default)
    {
        var local = LocalCatalogDir();
        string json;
        if (local != null)
            json = await File.ReadAllTextAsync(Path.Combine(local, "index.json"), ct);
        else
        {
            using var http = NewHttp();
            json = await http.GetStringAsync(BaseUrl + "index.json", ct);
        }
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("plugins", out var arr))
            return new List<MarketplaceEntry>();
        var list = JsonSerializer.Deserialize<List<MarketplaceEntry>>(arr.GetRawText(), JsonOpts)
                   ?? new List<MarketplaceEntry>();
        list.RemoveAll(e => !IsValidId(e.Id) || string.IsNullOrWhiteSpace(e.Hash));
        return list;
    }

    public static async Task<string> FetchCodeAsync(string id, CancellationToken ct = default)
    {
        if (!IsValidId(id))
            throw new InvalidOperationException("id de catalogue invalide");
        var local = LocalCatalogDir();
        if (local != null)
            return await File.ReadAllTextAsync(Path.Combine(local, id, "plugin.js"), ct);
        using var http = NewHttp();
        return await http.GetStringAsync($"{BaseUrl}{id}/plugin.js", ct);
    }

    public static async Task InstallAsync(MarketplaceEntry entry, string pluginsDir, CancellationToken ct = default)
    {
        if (!IsValidId(entry.Id))
            throw new InvalidOperationException("id de catalogue invalide");
        var local = LocalCatalogDir();
        byte[] js, manifest;
        if (local != null)
        {
            js = await File.ReadAllBytesAsync(Path.Combine(local, entry.Id, "plugin.js"), ct);
            manifest = await File.ReadAllBytesAsync(Path.Combine(local, entry.Id, "plugin.json"), ct);
        }
        else
        {
            using var http = NewHttp();
            js = await http.GetByteArrayAsync($"{BaseUrl}{entry.Id}/plugin.js", ct);
            manifest = await http.GetByteArrayAsync($"{BaseUrl}{entry.Id}/plugin.json", ct);
        }

        var hash = Convert.ToHexString(SHA256.HashData(js.Concat(manifest).ToArray()))
            .ToLowerInvariant();
        if (!string.Equals(hash, entry.Hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("hash non conforme au catalogue — installation refusée");

        var dir = Path.Combine(pluginsDir, entry.Id);
        var staging = Path.Combine(pluginsDir, $".staging-{entry.Id}-{Guid.NewGuid():N}");
        var backup = staging + ".bak";
        Directory.CreateDirectory(staging);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(staging, "plugin.js"), js, ct);
            await File.WriteAllBytesAsync(Path.Combine(staging, "plugin.json"), manifest, ct);
            if (Directory.Exists(dir))
                Directory.Move(dir, backup);
            Directory.Move(staging, dir);
            if (Directory.Exists(backup))
                Directory.Delete(backup, true);
        }
        catch
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
                if (!Directory.Exists(dir) && Directory.Exists(backup))
                    Directory.Move(backup, dir);
            }
            catch { }
            throw;
        }
    }
}
