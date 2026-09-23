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
    private static UpdateManager? _webMgr;
    private static UpdateManager? _pendingMgr;

    private static UpdateManager? Mgr => _mgr ??= CreateMgr(new GithubSource(RepoUrl, null, false));
    private static UpdateManager? WebMgr =>
        _webMgr ??= CreateMgr(new SimpleWebSource($"{RepoUrl}/releases/latest/download/"));

    private static UpdateManager? CreateMgr(IUpdateSource source)
    {
        try { return new UpdateManager(source); }
        catch { return null; }
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
            Pending = await TryCheckAsync(mgr, "github", ct);
            if (Pending == null)
            {
                var web = WebMgr;
                if (web?.IsInstalled == true)
                {
                    Pending = await TryCheckAsync(web, "feed-direct", ct);
                    _pendingMgr = web;
                }
            }
            else _pendingMgr = mgr;
            if (Pending != null
                && Version.TryParse(Pending.TargetFullRelease.Version.ToString(), out var pv))
                return (pv, $"{RepoUrl}/releases/latest", true);
            Log?.Invoke("update: aucune maj sur le canal Velopack");
            return null;
        }
        Log?.Invoke("update: app non installée — canal Velopack inactif");
        return await CheckGithubAsync(ct) is { } l ? (l.Version, l.Url, false) : null;
    }

    private static async Task<UpdateInfo?> TryCheckAsync(UpdateManager? mgr, string label, CancellationToken ct)
    {
        if (mgr == null) return null;
        try { return await mgr.CheckForUpdatesAsync().WaitAsync(ct); }
        catch (Exception ex) { Log?.Invoke($"update: check {label} en échec — {ex.Message}"); return null; }
    }

    private static UpdateManager? ActiveMgr => _pendingMgr ?? Mgr;

    public static Task DownloadAsync(IProgress<int> progress, CancellationToken ct = default)
        => ActiveMgr == null || Pending == null
            ? Task.CompletedTask
            : ActiveMgr.DownloadUpdatesAsync(Pending, p => progress.Report(p), ct);

    public static void ApplyAndRestart()
    {
        if (ActiveMgr != null && Pending != null)
            ActiveMgr.ApplyUpdatesAndRestart(Pending);
    }

    public static void ApplyOnExit()
    {
        if (ActiveMgr == null || Pending == null)
            return;
        try { ActiveMgr.WaitExitThenApplyUpdates(Pending.TargetFullRelease); }
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
            var url = $"{RepoUrl}/releases/latest/download/TouchMirror-win-Setup.exe";
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
