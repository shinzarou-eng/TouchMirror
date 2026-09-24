using System.IO;
using System.IO.Compression;
using System.Windows;
using TouchMirror.Services;

namespace TouchMirror;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\TouchMirror.SingleInstance", out var created);
        if (!created)
        {
            var self = Environment.ProcessId;
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("TouchMirror"))
            {
                if (p.Id == self || p.MainWindowHandle == IntPtr.Zero) continue;
                var h = p.MainWindowHandle;
                if (IsIconic(h)) ShowWindow(h, 9);
                SetForegroundWindow(h);
                break;
            }
            Shutdown();
            return;
        }
        AppLogger.Write("Application démarrée");
        try
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
                System.Windows.Media.Color.FromRgb(0x4E, 0xC9, 0x8E));
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
