using System.IO;
using System.Text.Json;

namespace TouchMirror.Services;

public sealed class DevicePrefs
{
    public string? CustomName { get; set; }
    public string? Model { get; set; }
    public string? LastSerial { get; set; }
}

public sealed class AppSettings
{
    public int MaxSize { get; set; }
    public int MaxFps { get; set; } = 60;
    public int VideoBitRate { get; set; } = 16_000_000;
    public string VideoCodec { get; set; } = "h264";
    public bool StayAwake { get; set; }
    public bool EnableAudio { get; set; } = true;
    public bool AutoLaunchDofus { get; set; }
    public bool AutoFullscreen { get; set; }
    public bool SyncDeviceClipboard { get; set; } = true;
    public bool TurnScreenOff { get; set; }
    public bool Topmost { get; set; }
    public bool ShowSettings { get; set; }
    public string? LastSelectedDeviceKey { get; set; }
    public Dictionary<string, DevicePrefs> Devices { get; set; } = new();
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
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
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
