using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace TouchMirror.Services;

public static class UpdateService
{
    private const string RepoUrl = "https://github.com/shinzarou-eng/TouchMirror";
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

    private static UpdateManager? _mgr;
    private static UpdateManager? Mgr
    {
        get
        {
            if (_mgr == null)
            {
                try { _mgr = new UpdateManager(new GithubSource(RepoUrl, null, false)); }
                catch { }
            }
            return _mgr;
        }
    }

    public static UpdateInfo? Pending { get; private set; }

    public static event Action<string>? Log;

#if DEBUG
    public const string DevSuffix = "-dev";
#else
    public const string DevSuffix = "";
#endif

    public static Task<(Version Version, string Url, bool SelfUpdate)?> CheckAsync(CancellationToken ct = default)
    {
#if DEBUG
        return Task.FromResult<(Version Version, string Url, bool SelfUpdate)?>(null);
#else
        return CheckCoreAsync(ct);
#endif
    }

    private static async Task<(Version Version, string Url, bool SelfUpdate)?> CheckCoreAsync(CancellationToken ct)
    {
        var mgr = Mgr;
        if (mgr?.IsInstalled == true)
        {
            try
            {
                Pending = await mgr.CheckForUpdatesAsync().WaitAsync(ct);
                if (Pending != null
                    && Version.TryParse(Pending.TargetFullRelease.Version.ToString(), out var pv))
                    return (pv, $"{RepoUrl}/releases/latest", true);
                Log?.Invoke("update: aucune maj sur le canal Velopack");
            }
            catch (Exception ex) { Log?.Invoke($"update: check Velopack en échec — {ex.Message}"); }
            return null;
        }
        Log?.Invoke("update: app non installée — canal Velopack inactif");
        return await CheckGithubAsync(ct) is { } l ? (l.Version, l.Url, false) : null;
    }

    public static Task DownloadAsync(IProgress<int> progress, CancellationToken ct = default)
        => Mgr == null || Pending == null
            ? Task.CompletedTask
            : Mgr.DownloadUpdatesAsync(Pending, p => progress.Report(p), ct);

    public static void ApplyAndRestart()
    {
        if (Mgr != null && Pending != null)
            Mgr.ApplyUpdatesAndRestart(Pending);
    }

    public static void ApplyOnExit()
    {
        if (Mgr == null || Pending == null)
            return;
        try { Mgr.WaitExitThenApplyUpdates(Pending.TargetFullRelease); }
        catch (Exception ex) { Log?.Invoke($"update: apply en échec — {ex.Message}"); }
    }

    private static async Task<(Version Version, string Url)?> CheckGithubAsync(CancellationToken ct)
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
