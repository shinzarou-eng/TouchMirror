using System.IO;
using System.IO.Compression;
using System.Windows;
using TouchMirror.Services;

namespace TouchMirror;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppLogger.Write("Application démarrée");
        try
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
                System.Windows.Media.Color.FromRgb(0xE8, 0xA3, 0x3D));
        }
        catch { }
        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Write(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLogger.Write($"FATAL: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Write($"Unobserved: {args.Exception}");
            args.SetObserved();
        };
        EnsureFfmpegExtracted();
        base.OnStartup(e);
    }

    private static void EnsureFfmpegExtracted()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "assets", "ffmpeg");
        if (File.Exists(Path.Combine(dir, "avcodec-63.dll")))
            return;
        var zip = Path.Combine(AppContext.BaseDirectory, "assets", "ffmpeg.zip");
        if (!File.Exists(zip))
            return;
        try
        {
            Directory.CreateDirectory(dir);
            ZipFile.ExtractToDirectory(zip, dir, overwriteFiles: true);
            AppLogger.Write("FFmpeg extrait depuis assets/ffmpeg.zip");
        }
        catch (Exception ex)
        {
            AppLogger.Write($"Extraction FFmpeg échouée : {ex.Message}");
        }
    }
}
