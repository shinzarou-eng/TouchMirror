using System.IO;

namespace TouchMirror.Services;

public static class AppLogger
{
    private static readonly string _path =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TouchMirror", "app.log");
    private static readonly object _lock = new();
    private static StreamWriter? _writer;

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

    private static StreamWriter CreateWriter()
        => new(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        { AutoFlush = true };

    public static void Write(string message)
    {
        try
        {
            lock (_lock)
            {
                _writer ??= CreateWriter();
                _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            }
        }
        catch { }
    }

    public static void Write(Exception ex) => Write(ex.ToString());

    public static void Forget(Task task)
        => task.ContinueWith(t => Write($"async en arrière-plan : {t.Exception?.GetBaseException()}"),
            TaskContinuationOptions.OnlyOnFaulted);
}
