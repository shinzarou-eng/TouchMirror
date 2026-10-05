using System.IO;
using System.IO.Compression;
using System.Windows;
using TouchMirror.Services;

namespace TouchMirror;

public partial class App : Application
{
    public static Action? EmergencyCleanup;
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
            Environment.Exit(0);
            return;
        }
        AppLogger.Write("Application démarrée");
        _themeId = SettingsStore.Load().Theme;
        ApplyTheme(_themeId);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, ev) =>
        {
            if (ev.Category == Microsoft.Win32.UserPreferenceCategory.General)
                Dispatcher.BeginInvoke(() => ApplyTheme(_themeId));
        };
        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Write(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLogger.WriteFatal($"{args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Write($"Unobserved: {args.Exception}");
            args.SetObserved();
        };
        EnsureFfmpegExtracted();
        StartUiWatchdog();
        base.OnStartup(e);
    }

    private static long _watchdogSuppressUntil;

    public static void SuppressWatchdog(int seconds = 60)
        => System.Threading.Interlocked.Exchange(ref _watchdogSuppressUntil,
            Environment.TickCount64 + seconds * 1000L);

    private void StartUiWatchdog()
    {
        var disp = Dispatcher;
        _ = Task.Run(async () =>
        {
            var silentSince = 0L;
            while (true)
            {
                await Task.Delay(2000).ConfigureAwait(false);
                if (disp.HasShutdownStarted || disp.HasShutdownFinished)
                    return;
                if (WatchdogGate.IsSuppressed(Environment.TickCount64,
                        System.Threading.Interlocked.Read(ref _watchdogSuppressUntil)))
                {
                    silentSince = 0;
                    continue;
                }
                System.Windows.Threading.DispatcherOperation op;
                try
                {
                    op = disp.InvokeAsync(static () => { },
                        System.Windows.Threading.DispatcherPriority.Send);
                }
                catch { return; }
                if (await Task.WhenAny(op.Task, Task.Delay(4000)).ConfigureAwait(false) == op.Task)
                {
                    silentSince = 0;
                    continue;
                }
                if (silentSince == 0)
                    silentSince = Environment.TickCount64;
                else if (WatchdogGate.SilenceExceeded(silentSince, Environment.TickCount64))
                {
                    try { AppLogger.Write("ui: dispatcher bloqué — arrêt forcé"); } catch { }
                    try { EmergencyCleanup?.Invoke(); } catch { }
                    await Task.Delay(1500).ConfigureAwait(false);
                    Environment.Exit(2);
                }
            }
        });
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
            try
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                    _themeId == "clair" ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark);
                Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(accent);
            }
            catch (Exception ex) { AppLogger.Write($"theme wpfui: {ex.Message}"); }
            var dicts = Resources.MergedDictionaries;
            for (var i = dicts.Count - 1; i >= 0; i--)
                if (dicts[i].Source?.OriginalString.Contains("/Themes/") == true)
                    dicts.RemoveAt(i);
            dicts.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });
        }
        catch (Exception ex) { AppLogger.Write($"theme: {ex.Message}"); }
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
