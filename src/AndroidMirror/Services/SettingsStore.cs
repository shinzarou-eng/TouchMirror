using System.IO;
using System.Text.Json;

namespace TouchMirror.Services;

public sealed class KeybindData
{
    public string Key { get; set; } = "";
    public double Rx { get; set; }
    public double Ry { get; set; }
}

public sealed class DevicePrefs
{
    public string? CustomName { get; set; }
    public string? Model { get; set; }
    public string? LastSerial { get; set; }
    public string? Color { get; set; }
    public bool Pinned { get; set; }
    public List<KeybindData> Keybinds { get; set; } = new();
    public Dictionary<string, List<KeybindData>> KeybindProfiles { get; set; } = new();
    public string? ActiveKeybindProfile { get; set; }
    public int KeybindStyle { get; set; }
    public double KeybindOpacity { get; set; } = 0.92;
    public double KeybindSize { get; set; } = 30;
    public int? MaxSize { get; set; }
    public int? MaxFps { get; set; }
    public int? VideoBitRate { get; set; }
    public string? VideoCodec { get; set; }
    public string? VideoDecoder { get; set; }
    public bool? EnableAudio { get; set; }
    public bool? TurnScreenOff { get; set; }
    public string? NewDisplay { get; set; }
    public bool? AdaptiveBitrate { get; set; }
    public int? AdaptiveCeiling { get; set; }
    public bool? UhidInput { get; set; }
    public List<int> OwnedUserIds { get; set; } = new();
    public Dictionary<int, string> AccountAvatars { get; set; } = new();
    public Dictionary<int, string> AccountNames { get; set; } = new();
    public Dictionary<string, long> AccountWeekSeconds { get; set; } = new();
    public Dictionary<string, double[]> OverlayPositions { get; set; } = new();
}

public sealed class WorkspaceDevice
{
    public string DeviceKey { get; set; } = "";
    public string? Model { get; set; }
    public string? LastSerial { get; set; }
    public int? MaxSize { get; set; }
    public int? MaxFps { get; set; }
    public int? VideoBitRate { get; set; }
    public string? VideoCodec { get; set; }
    public string? VideoDecoder { get; set; }
    public bool? EnableAudio { get; set; }
    public bool? TurnScreenOff { get; set; }
    public string? NewDisplay { get; set; }
    public int? AccountUserId { get; set; }
    public string? AccountName { get; set; }
    public bool? AdaptiveBitrate { get; set; }
    public int? AdaptiveCeiling { get; set; }
    public bool? UhidInput { get; set; }
}

public sealed class Workspace
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<WorkspaceDevice> Devices { get; set; } = new();
    public string? ActiveDeviceKey { get; set; }
    public bool GridMode { get; set; }
}

public sealed class AppSettings
{
    public int MaxSize { get; set; }
    public int MaxFps { get; set; } = 60;
    public int VideoBitRate { get; set; } = 16_000_000;
    public string VideoCodec { get; set; } = "auto";
    public string VideoDecoder { get; set; } = "gpu";
    public long GpuBackoffUntil { get; set; }
    public bool VideoSharpen { get; set; }
    public bool VideoFxaa { get; set; }
    public double VideoBrightness { get; set; }
    public double VideoContrast { get; set; } = 1;
    public double VideoSaturation { get; set; } = 1;
    public double VideoVibrance { get; set; }
    public double VideoVignette { get; set; }
    public double VideoGamma { get; set; } = 1;
    public bool StayAwake { get; set; }
    public bool EnableAudio { get; set; } = true;
    public bool AutoFullscreen { get; set; }
    public bool SyncDeviceClipboard { get; set; } = true;
    public bool TurnScreenOff { get; set; }
    public bool AutoLaunchDofus { get; set; }
    public bool AdaptiveBitrate { get; set; } = true;
    public bool UhidInput { get; set; } = true;
    public bool WifiHandover { get; set; } = true;
    public List<string> SetupDismissed { get; set; } = new();
    public bool WizardSeen { get; set; }
    public string? NewDisplay { get; set; }
    public bool Topmost { get; set; }
    public bool ShowSettings { get; set; }
    public bool LocalApiEnabled { get; set; }
    public bool DiscordPresence { get; set; }
    public bool AnonymousStats { get; set; } = true;
    public int LocalApiPort { get; set; } =
#if DEBUG
        47614;
#else
        47613;
#endif
    public string? LocalApiToken { get; set; }
    public string? LastSelectedDeviceKey { get; set; }
    public List<string> EnabledPlugins { get; set; } = new();
    public Dictionary<string, string> ApprovedPlugins { get; set; } = new();
    public List<string> MirrorOrder { get; set; } = new();
    public Dictionary<string, DevicePrefs> Devices { get; set; } = new();
    public List<Workspace> Workspaces { get; set; } = new();
    public string? ActiveWorkspaceId { get; set; }
    public string Language { get; set; } = "fr";
    public string Theme { get; set; } = "sombre";
    public int? IosMapMode { get; set; }
    public string ShortcutMirrorNext { get; set; } = "Ctrl+Tab";
    public string ShortcutMirrorPrev { get; set; } = "Ctrl+Shift+Tab";
    public string ShortcutWorkspaceNext { get; set; } = "Alt+Right";
    public string ShortcutWorkspacePrev { get; set; } = "Alt+Left";
}

public static class SettingsStore
{
    private static readonly string _path = Path.Combine(
        AppPaths.DataDir, "settings.json");

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
                s.SetupDismissed ??= new();
                s.EnabledPlugins ??= new();
                s.ApprovedPlugins ??= new();
                s.MirrorOrder ??= new();
                s.Devices ??= new();
                s.Workspaces ??= new();
                foreach (var w in s.Workspaces)
                    w.Devices ??= new();
                foreach (var dp in s.Devices.Values)
                {
                    dp.Keybinds ??= new();
                    dp.KeybindProfiles ??= new();
                    dp.OwnedUserIds ??= new();
                    dp.AccountAvatars ??= new();
                    dp.AccountNames ??= new();
                    dp.AccountWeekSeconds ??= new();
                    dp.OverlayPositions ??= new();
                }
                if (s.EnabledPlugins.Remove("watchdog"))
                    s.EnabledPlugins.Add("reconnect");
                if (s.ApprovedPlugins.Remove("watchdog", out var h))
                    s.ApprovedPlugins["reconnect"] = h;
                bool hud = false;
                foreach (var old in new[] { "graphs", "stats", "perf" })
                {
                    hud |= s.EnabledPlugins.Remove(old);
                    s.ApprovedPlugins.Remove(old, out _);
                }
                if (hud && !s.EnabledPlugins.Contains("hud"))
                    s.EnabledPlugins.Add("hud");
                if (s.IosMapMode is < 0 or > 2)
                    s.IosMapMode = null;
                foreach (var dp in s.Devices.Values)
                    if (dp.AdaptiveCeiling is { } c && (c < AdaptEvaluator.MinBitRate || c > AdaptEvaluator.MaxBitRate))
                        dp.AdaptiveCeiling = null;
                foreach (var wd in s.Workspaces.SelectMany(w => w.Devices))
                    if (wd.AdaptiveCeiling is { } c && (c < AdaptEvaluator.MinBitRate || c > AdaptEvaluator.MaxBitRate))
                        wd.AdaptiveCeiling = null;
                return s;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Write($"settings: lecture impossible ({ex.Message})");
            try { File.Copy(_path, _path + ".corrupt", true); } catch { }
        }
        return new();
    }

    public static string? FindAliasKey(AppSettings s, AdbDevice d, IReadOnlyList<AdbDevice> live)
    {
        if (s.Devices.ContainsKey(d.DeviceKey))
            return null;
        var bySerial = s.Devices.FirstOrDefault(kv =>
            kv.Value.LastSerial != null && d.MatchesSerial(kv.Value.LastSerial)).Key;
        if (bySerial != null)
            return bySerial;
        if (d.HardwareSerial is { Length: > 0 })
        {
            if (d.Serial != d.DeviceKey && s.Devices.ContainsKey(d.Serial))
                return d.Serial;
            if (d.AltSerial != null && s.Devices.ContainsKey(d.AltSerial))
                return d.AltSerial;
        }
        if (d.Model.Length > 0 && live.Count(x => x.Model == d.Model) == 1)
        {
            var ipKeys = s.Devices
                .Where(kv => kv.Key.Contains(':')
                    && kv.Value.Model == d.Model
                    && !live.Any(x => x.DeviceKey == kv.Key))
                .Select(kv => kv.Key)
                .ToList();
            if (ipKeys.Count == 1)
                return ipKeys[0];
        }
        return null;
    }

    public static void MigrateDeviceKey(AppSettings s, string from, string to)
    {
        if (from == to)
            return;
        if (s.Devices.Remove(from, out var dp))
        {
            if (s.Devices.TryGetValue(to, out var cur))
                MergePrefs(cur, dp);
            else
                s.Devices[to] = dp;
        }
        var prefix = from + "#";
        foreach (var kv in s.Devices.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            s.Devices.Remove(kv.Key);
            var nk = to + kv.Key[from.Length..];
            if (s.Devices.TryGetValue(nk, out var cur))
                MergePrefs(cur, kv.Value);
            else
                s.Devices[nk] = kv.Value;
        }
        foreach (var w in s.Workspaces)
        {
            foreach (var wd in w.Devices)
                if (wd.DeviceKey == from || wd.DeviceKey.StartsWith(prefix, StringComparison.Ordinal))
                    wd.DeviceKey = to + wd.DeviceKey[from.Length..];
            if (w.ActiveDeviceKey is { } ak
                && (ak == from || ak.StartsWith(prefix, StringComparison.Ordinal)))
                w.ActiveDeviceKey = to + ak[from.Length..];
        }
        s.MirrorOrder = s.MirrorOrder
            .Select(k => k == from || k.StartsWith(prefix, StringComparison.Ordinal)
                ? to + k[from.Length..] : k)
            .ToList();
        if (s.LastSelectedDeviceKey == from
            || (s.LastSelectedDeviceKey?.StartsWith(prefix, StringComparison.Ordinal) ?? false))
            s.LastSelectedDeviceKey = to + s.LastSelectedDeviceKey![from.Length..];
    }

    private static void MergePrefs(DevicePrefs cur, DevicePrefs old)
    {
        cur.CustomName ??= old.CustomName;
        cur.Model ??= old.Model;
        cur.LastSerial ??= old.LastSerial;
        cur.Color ??= old.Color;
        cur.Pinned |= old.Pinned;
        if (cur.Keybinds.Count == 0) cur.Keybinds = old.Keybinds;
        foreach (var kv in old.KeybindProfiles) cur.KeybindProfiles.TryAdd(kv.Key, kv.Value);
        cur.ActiveKeybindProfile ??= old.ActiveKeybindProfile;
        cur.MaxSize ??= old.MaxSize;
        cur.MaxFps ??= old.MaxFps;
        cur.VideoBitRate ??= old.VideoBitRate;
        cur.VideoCodec ??= old.VideoCodec;
        cur.VideoDecoder ??= old.VideoDecoder;
        cur.EnableAudio ??= old.EnableAudio;
        cur.TurnScreenOff ??= old.TurnScreenOff;
        cur.NewDisplay ??= old.NewDisplay;
        cur.AdaptiveBitrate ??= old.AdaptiveBitrate;
        cur.AdaptiveCeiling ??= old.AdaptiveCeiling;
        cur.UhidInput ??= old.UhidInput;
        if (cur.OwnedUserIds.Count == 0) cur.OwnedUserIds = old.OwnedUserIds;
        foreach (var kv in old.AccountAvatars) cur.AccountAvatars.TryAdd(kv.Key, kv.Value);
        foreach (var kv in old.AccountNames) cur.AccountNames.TryAdd(kv.Key, kv.Value);
        foreach (var kv in old.AccountWeekSeconds) cur.AccountWeekSeconds.TryAdd(kv.Key, kv.Value);
        foreach (var kv in old.OverlayPositions) cur.OverlayPositions.TryAdd(kv.Key, kv.Value);
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, _json));
            File.Move(tmp, _path, true);
        }
        catch (Exception ex)
        {
            AppLogger.Write($"settings: sauvegarde impossible ({ex.Message})");
        }
    }
}
