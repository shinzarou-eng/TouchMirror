using System.IO;
using System.Text.Json;

namespace TouchMirror.Services;

public sealed class DevicePrefs
{
    public string? CustomName { get; set; }
    public string? Model { get; set; }
    public string? LastSerial { get; set; }
    /// <summary>Couleur d'accent de l'appareil (hex « #RRGGBB »), nulle = couleur par défaut.</summary>
    public string? Color { get; set; }
}

/// <summary>Appareil membre d'un espace de travail, avec ses réglages propres.
/// Un champ nul = utilise le réglage global.</summary>
public sealed class WorkspaceDevice
{
    public string DeviceKey { get; set; } = "";
    public string? Model { get; set; }
    public string? LastSerial { get; set; }
    public int? MaxSize { get; set; }
    public int? MaxFps { get; set; }
    public int? VideoBitRate { get; set; }
    public string? VideoCodec { get; set; }
    public bool? EnableAudio { get; set; }
    public bool? TurnScreenOff { get; set; }
}

/// <summary>Disposition mémorisée : membres (dans l'ordre des tuiles) + miroir actif.</summary>
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
    public string VideoCodec { get; set; } = "h264";
    public bool StayAwake { get; set; }
    public bool EnableAudio { get; set; } = true;
    public bool AutoFullscreen { get; set; }
    public bool SyncDeviceClipboard { get; set; } = true;
    public bool TurnScreenOff { get; set; }
    public bool Topmost { get; set; }
    public bool ShowSettings { get; set; }
    public bool LocalApiEnabled { get; set; }
    public int LocalApiPort { get; set; } = 47613;
    public string? LocalApiToken { get; set; }
    public string? LastSelectedDeviceKey { get; set; }
    public List<string> EnabledPlugins { get; set; } = new();
    /// <summary>Plugins non officiels approuvés par l'utilisateur : id → hash SHA-256 validé.</summary>
    public Dictionary<string, string> ApprovedPlugins { get; set; } = new();
    /// <summary>Ordre des tuiles miroir, par clé d'appareil.</summary>
    public List<string> MirrorOrder { get; set; } = new();
    public Dictionary<string, DevicePrefs> Devices { get; set; } = new();
    /// <summary>Espaces de travail mémorisés ; vide = fonctionnalité non utilisée.</summary>
    public List<Workspace> Workspaces { get; set; } = new();
    /// <summary>Espace actuellement appliqué ; nul = mode libre (réglages globaux).</summary>
    public string? ActiveWorkspaceId { get; set; }
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
                // Renommage watchdog → reconnect : conserve l'activation et l'approbation.
                if (s.EnabledPlugins.Remove("watchdog"))
                    s.EnabledPlugins.Add("reconnect");
                if (s.ApprovedPlugins.Remove("watchdog", out var h))
                    s.ApprovedPlugins["reconnect"] = h;
                return s;
            }
        }
        catch { }
        return new();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, _json));
        }
        catch { }
    }
}
