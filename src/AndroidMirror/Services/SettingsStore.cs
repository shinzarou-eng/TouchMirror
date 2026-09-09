using System.IO;
using System.Text.Json;

namespace TouchMirror.Services;

/// <summary>Raccourci clavier plaqué sur la vidéo : touche → tap à (Rx,Ry).</summary>
public sealed class KeybindData
{
    /// <summary>Nom de la touche WPF (« A », « D1 », « Space », « F1 »…).</summary>
    public string Key { get; set; } = "";
    /// <summary>Position relative 0..1 dans l'image vidéo (repère appareil).</summary>
    public double Rx { get; set; }
    public double Ry { get; set; }
}

public sealed class DevicePrefs
{
    public string? CustomName { get; set; }
    public string? Model { get; set; }
    public string? LastSerial { get; set; }
    /// <summary>Couleur d'accent de l'appareil (hex « #RRGGBB »), nulle = couleur par défaut.</summary>
    public string? Color { get; set; }
    /// <summary>Raccourcis plaqués sur la vidéo — touche clavier = tap à la position.</summary>
    public List<KeybindData> Keybinds { get; set; } = new();
    /// <summary>Style des raccourcis : 0 = pastille, 1 = cercle, 2 = minimal.</summary>
    public int KeybindStyle { get; set; }
    /// <summary>Opacité des raccourcis (0.3–1).</summary>
    public double KeybindOpacity { get; set; } = 0.92;
    /// <summary>Taille des raccourcis en px (22–44).</summary>
    public double KeybindSize { get; set; } = 30;
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
    /// <summary>Écran virtuel : null = hérite du global, "" = auto, "WxH/DPI" sinon.</summary>
    public string? NewDisplay { get; set; }
    /// <summary>Profil Android secondaire (userId) si ce membre est une tuile multi-compte.</summary>
    public int? AccountUserId { get; set; }
    /// <summary>Nom affiché du profil secondaire.</summary>
    public string? AccountName { get; set; }
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
    /// <summary>Lance Dofus Touch (com.ankama.dofustouch) au démarrage du mirroring.</summary>
    public bool AutoLaunchDofus { get; set; } = true;
    /// <summary>Écran virtuel : null = écran physique, "" = auto, "WxH/DPI" sinon.</summary>
    public string? NewDisplay { get; set; }
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
    /// <summary>Langue de l'interface : "fr" (défaut), "en", … (fichier lang/&lt;code&gt;.json).</summary>
    public string Language { get; set; } = "fr";
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
            // Écriture atomique : un crash en pleine sauvegarde ne corrompt pas le fichier.
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, _json));
            File.Move(tmp, _path, true);
        }
        catch { }
    }
}
