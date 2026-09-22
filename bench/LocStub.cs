namespace TouchMirror.Services;

public static class LocalizationService
{
    public static string Get(string key) => key;
}

public static class AppLogger
{
    public static void Write(string msg) => Console.WriteLine("[log] " + msg);
}
