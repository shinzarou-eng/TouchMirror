using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace TouchMirror.Services;

public static class UpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/shinzarou-eng/TouchMirror/releases/latest";

    public static Version CurrentVersion { get; } =
        Version.TryParse(
            Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion?.Split('+')[0],
            out var v)
            ? v
            : new Version(0, 0, 0);

    public static async Task<(Version Version, string Url)?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TouchMirror");
            http.Timeout = TimeSpan.FromSeconds(8);
            var json = await http.GetStringAsync(LatestReleaseApi, ct);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var url = doc.RootElement.GetProperty("html_url").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                return null;
            return latest > CurrentVersion ? (latest, url) : null;
        }
        catch
        {
            return null;
        }
    }
}
