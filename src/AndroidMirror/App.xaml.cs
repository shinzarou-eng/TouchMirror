using System.IO;
using System.IO.Compression;
using System.Windows;
using TouchMirror.Services;

namespace TouchMirror;

public partial class App : Application
{
    private static Mutex? _singleInstance;
    private string _themeId = "sombre";

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    protected override void OnStartup(StartupEventArgs e)
    {
        Velopack.VelopackApp.Build().Run();
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
        _themeId = SettingsStore.Load().Theme;
        ApplyTheme(_themeId);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, ev) =>
        {
            if (ev.Category == Microsoft.Win32.UserPreferenceCategory.General)
                Dispatcher.Invoke(() => ApplyTheme(_themeId));
        };
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

    public void ApplyTheme(string? id)
    {
        _themeId = string.IsNullOrEmpty(id) ? "sombre" : id;
        try
        {
            var source = _themeId switch
            {
                "clair" => "Themes/Light.xaml",
                "halloween" => "Themes/Halloween.xaml",
                _ => "Themes/Dark.xaml",
            };
            var accent = _themeId switch
            {
                "clair" => System.Windows.Media.Color.FromRgb(0x5A, 0x64, 0x70),
                "halloween" => System.Windows.Media.Color.FromRgb(0xE0, 0x7B, 0x2C),
                _ => System.Windows.Media.Color.FromRgb(0xC9, 0xD1, 0xD9),
            };
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                _themeId == "clair" ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark);
            Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(accent);
            var dicts = Resources.MergedDictionaries;
            for (var i = dicts.Count - 1; i >= 0; i--)
                if (dicts[i].Source?.OriginalString.Contains("/Themes/") == true)
                    dicts.RemoveAt(i);
            dicts.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });
        }
        catch { }
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
