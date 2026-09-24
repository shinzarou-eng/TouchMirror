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
}

public sealed class Workspace
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<WorkspaceDevice> Devices { get; set; } = new();
    public string? ActiveDeviceKey { get; set; }
}

public sealed class AppSettings
{
    public int MaxSize { get; set; }
    public int MaxFps { get; set; } = 60;
    public int VideoBitRate { get; set; } = 16_000_000;
    public string VideoCodec { get; set; } = "auto";
    public string VideoDecoder { get; set; } = "gpu";
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
    public List<string> SetupDismissed { get; set; } = new();
    public bool WizardSeen { get; set; }
    public string? NewDisplay { get; set; }
    public bool Topmost { get; set; }
    public bool ShowSettings { get; set; }
    public bool LocalApiEnabled { get; set; }
    public bool DiscordPresence { get; set; }
    public bool AnonymousStats { get; set; } = true;
    public int LocalApiPort { get; set; } = 47613;
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
}

public static class SettingsStore
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TouchMirror", "settings.json");

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
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
