using System.IO;
using System.Text.Json;

namespace TouchMirror.Services;

public static class DimmedScreenStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TouchMirror", "dimmed");

    public sealed record State(string File, string Serial, string DeviceKey,
        int Brightness, int StayOn);

    private static string Safe(string key)
        => string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

    public static void Mark(string serial, string deviceKey, int brightness, int stayOn)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, Safe(deviceKey) + ".json"),
                JsonSerializer.Serialize(new { serial, deviceKey, brightness, stayOn }));
        }
        catch (Exception ex) { AppLogger.Write($"dimmed-store: {ex.Message}"); }
    }

    public static void Clear(string deviceKey)
    {
        try { File.Delete(Path.Combine(Dir, Safe(deviceKey) + ".json")); }
        catch { }
    }

    public static List<State> Pending()
    {
        var list = new List<State>();
        try
        {
            if (!Directory.Exists(Dir))
                return list;
            foreach (var f in Directory.EnumerateFiles(Dir, "*.json"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(f));
                    var r = doc.RootElement;
                    list.Add(new State(f,
                        r.GetProperty("serial").GetString() ?? "",
                        r.GetProperty("deviceKey").GetString() ?? "",
                        r.TryGetProperty("brightness", out var b) ? b.GetInt32() : -1,
                        r.TryGetProperty("stayOn", out var s) ? s.GetInt32() : -1));
                }
                catch { }
            }
        }
        catch { }
        return list;
    }

    public static void Remove(State s)
    {
        try { File.Delete(s.File); } catch { }
    }
}
