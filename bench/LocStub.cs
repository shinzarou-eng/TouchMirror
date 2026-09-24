namespace TouchMirror.Services;

/// <summary>Stub minimal pour le bench — pas de WPF ici.</summary>
public static class LocalizationService
{
    public static string Get(string key) => key;
}

public static class AppLogger
{
    public static void Write(string msg) => Console.WriteLine("[log] " + msg);
}
