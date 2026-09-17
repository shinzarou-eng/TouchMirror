using System.IO;

namespace TouchMirror.Services;

public static class AppLogger
{
    private static readonly string _path =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TouchMirror", "app.log");
    private static readonly object _lock = new();

    public static string LogFilePath => _path;

    static AppLogger()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, $"=== TouchMirror {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        }
        catch { }
    }

    public static void Write(string message)
    {
        try
        {
            lock (_lock)
                File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    public static void Write(Exception ex) => Write(ex.ToString());

    public static void Forget(Task task)
        => task.ContinueWith(t => Write($"async en arrière-plan : {t.Exception?.GetBaseException()}"),
            TaskContinuationOptions.OnlyOnFaulted);
}