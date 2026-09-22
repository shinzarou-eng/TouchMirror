using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using TouchMirror.Services;
using TouchMirror.ViewModels;

internal static class DiagnosticPreview
{
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);

    public static int Run(string repo, string output)
    {
        var app = Application.Current;
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/TouchMirror;component/Themes/Dark.xaml", UriKind.Relative)
        });
        app.Resources["FsCaption"] = 10.5d;
        app.Resources["DofusAccentBrush"] = new SolidColorBrush(Color.FromRgb(78, 201, 142));
        app.Resources["OnDarkTextBrush"] = Brushes.White;
        app.Resources["AppScrimBrush"] = new SolidColorBrush(Color.FromArgb(230, 16, 18, 22));
        app.Resources["NullToCollapsed"] = new TouchMirror.NullToCollapsedConverter();
        app.Resources["BoolToVis"] = new BooleanToVisibilityConverter();
        app.Resources["BytesToImage"] = new TouchMirror.BytesToImageConverter();
        app.Resources["FsMeta"] = 12d;
        app.Resources["FsSection"] = 15d;
        app.Resources["RadiusPanel"] = new CornerRadius(10);
        app.Resources["AppModalSolidBrush"] = new SolidColorBrush(Color.FromArgb(216, 16, 19, 25));
        app.Resources["BtnAction"] = new Style(typeof(Wpf.Ui.Controls.Button));
        app.Resources["BtnPrimary"] = new Style(typeof(Wpf.Ui.Controls.Button));
        LocalizationService.Instance.Load("fr");
        var xml = XDocument.Load(Path.Combine(repo, "src", "AndroidMirror", "MainWindow.xaml"));
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "Diagnostics — simulation sans téléphone", FontSize = 18, Margin = new Thickness(0, 0, 0, 16) });
        foreach (var brand in new[] { "xiaomi", "samsung" })
        {
            var node = xml.Descendants().Single(e => e.Name.LocalName == "Border"
                && e.Attribute("Visibility")?.Value.Contains("UsbDiagTitle") == true);
            var border = (Border)XamlReader.Parse(node.ToString());
            border.DataContext = new
            {
                UsbDiagTitle = string.Format(LocalizationService.Get("diag.usb_title"), brand == "xiaomi" ? "Redmi de test" : "Samsung de test"),
                UsbDiagDetail = LocalizationService.Get("diag.usb_" + brand)
            };
            panel.Children.Add(border);
        }
        var warningNode = xml.Descendants().Single(e => e.Name.LocalName == "Border"
            && e.Attribute("Visibility")?.Value.Contains("ConnectionWarning") == true);
        var warning = (Border)XamlReader.Parse(warningNode.ToString());
        warning.DataContext = new { ConnectionWarning = string.Format(LocalizationService.Get("diag.other_apps"), "scrcpy") };
        panel.Children.Add(warning);
        var mirror = new MirrorInstance(new AdbDevice("test-phone", "Test Phone", "device"))
        {
            IsReconnecting = true,
            ReconnectStatus = string.Format(LocalizationService.Get("log.session_retry"), 2)
        };
        mirror.View.Height = 130;
        panel.Children.Add(mirror.View);
        var pairNode = xml.Descendants().Single(e => e.Name.LocalName == "Border"
            && e.Attribute("Visibility")?.Value.Contains("PairOpen") == true);
        foreach (var attr in pairNode.DescendantsAndSelf().Attributes()
                     .Where(a => a.Value.Contains("{loc:Loc")).ToList())
            attr.Value = System.Text.RegularExpressions.Regex.Replace(attr.Value, @"\{loc:Loc ([^}]+)\}",
                m => LocalizationService.Get(m.Groups[1].Value));
        var pair = (Border)XamlReader.Parse(pairNode.ToString());
        var qrData = new QRCoder.QRCodeGenerator().CreateQrCode(
            "WIFI:T:ADB;S:studio-Preview123;P:Ab3dEf7HjK1m;;", QRCoder.QRCodeGenerator.ECCLevel.Q);
        pair.DataContext = new PairPreviewVm
        {
            PairQrPng = new QRCoder.PngByteQRCode(qrData).GetGraphic(9),
            PairStatus = string.Format(LocalizationService.Get("pair.found"), "192.168.1.7:45821"),
            PairBusy = true
        };
        panel.Children.Add(pair);
        var dbgNode = xml.Descendants().Single(e => e.Name.LocalName == "Border"
            && e.Attribute("Visibility")?.Value.Contains("DebugOpen") == true);
        foreach (var attr in dbgNode.DescendantsAndSelf().Attributes()
                     .Where(a => a.Value.Contains("{loc:Loc")).ToList())
            attr.Value = System.Text.RegularExpressions.Regex.Replace(attr.Value, @"\{loc:Loc ([^}]+)\}",
                m => LocalizationService.Get(m.Groups[1].Value));
        var dbg = (Border)XamlReader.Parse(dbgNode.ToString());
        dbg.DataContext = new DebugPreviewVm
        {
            DebugReport = "TouchMirror v0.8.0\nMicrosoft Windows 10.0.22631 64-bit\n.NET 10.0.0\nécran : 2560x1440\nréglages : 1080p · 60 fps · 24 Mbps · h264 · audio on · écran off off\nsanté globale : 🔴 problème bloquant — 1 point(s) à corriger\n\n== diagnostic ==\n[OK] adb : C:\\app\\platform-tools\\adb.exe\n[OK] le serveur adb répond\n[OK] 1 appareil(s) prêt(s)\n[!!] Windows voit « Redmi Note 12 » (Xiaomi) mais adb non — débogage USB/RSA à vérifier\n[!!] scrcpy tourne — peut réinitialiser la liaison adb\n[OK] mDNS : 6 réponse(s) — le pairing QR peut trouver le téléphone\n[i ] IP du PC : 192.168.129.213 — le téléphone doit être sur le même réseau\n\n== adb devices -l ==\nR5CT123ABC  device product:x model:SM_T220\n\n== adb mdns ==\nmdns daemon version [0.0.0]\n\n== périphériques Windows (PnP) ==\n- Redmi Note 12  [VID_2717] Xiaomi\n\n== processus adb / mirroring ==\n- scrcpy  C:\\tools\\scrcpy.exe\n\n== miroirs ==\n- SM-T220 [connecté] état=device\n\n== journal ==\n[22:48:53.872] Application démarrée\n[22:48:56.005] [HUD] hud actif"
        };
        panel.Children.Add(dbg);
        var window = new Window
        {
            Title = "TouchMirror diagnostic validation", Width = 900, Height = 1140,
            Background = (Brush)app.Resources["AppWindowBrush"], Foreground = Brushes.White,
            Content = panel, ShowActivated = false
        };
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(500);
                Capture(window, output);
                window.Width = 560;
                window.Height = 720;
                await Task.Delay(500);
                Capture(window, Path.ChangeExtension(output, "narrow.png"));
                Console.WriteLine("Diagnostic screenshots captured with PrintWindow(flags=2)");
            }
            catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
            finally { window.Close(); app.Shutdown(Environment.ExitCode); }
        };
        return app.Run(window);
    }

    public static int RunHub(string repo, string output)
    {
        var app = Application.Current;
        try
        {
        var appXml = XDocument.Load(Path.Combine(repo, "src", "AndroidMirror", "App.xaml"));
        var rdNode = appXml.Descendants().Single(e => e.Name.LocalName == "ResourceDictionary"
            && e.Parent?.Name.LocalName == "Application.Resources");
        rdNode.SetAttributeValue(XNamespace.Xmlns + "ui", "http://schemas.lepo.co/wpfui/2022/xaml");
        rdNode.SetAttributeValue(XNamespace.Xmlns + "sys", "clr-namespace:System;assembly=mscorlib");
        var rdXml = rdNode.ToString()
            .Replace("Source=\"Themes/", "Source=\"/TouchMirror;component/Themes/");
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(rdXml));
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/TouchMirror;component/Themes/Dark.xaml", UriKind.Relative)
        });
        LocalizationService.Instance.Load("fr");
        var window = new TouchMirror.MainWindow
        {
            Width = 1400, Height = 900,
            Left = 60, Top = 60,
            ShowActivated = false
        };
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(6500);
                DumpCarousel(window);
                DumpChrome(window);
                var m = typeof(TouchMirror.MainWindow).GetMethod("LayoutHubCarousel",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Console.WriteLine($"hub: relayout method={(m == null ? "introuvable" : "ok")}");
                m?.Invoke(window, new object[] { false });
                await Task.Delay(300);
                Console.WriteLine("hub: après relayout forcé");
                DumpCarousel(window);
                Capture(window, output);
                window.Width = 760;
                window.Height = 700;
                await Task.Delay(900);
                DumpChrome(window);
                Capture(window, Path.ChangeExtension(output, "narrow.png"));
                Console.WriteLine("Hub screenshots captured");
            }
            catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
            finally { window.Close(); app.Shutdown(Environment.ExitCode); }
        };
        return app.Run(window);
        }
        catch (Exception ex) { Console.WriteLine($"hub init: {ex}"); return 1; }
    }

    private sealed class PairPreviewVm
    {
        public bool PairOpen { get; set; } = true;
        public byte[]? PairQrPng { get; set; }
        public string PairStatus { get; set; } = "";
        public bool PairBusy { get; set; }
        public bool PairIdle => !PairBusy;
        public string PairAddress { get; set; } = "";
        public string PairCode { get; set; } = "";
    }

    private sealed class DebugPreviewVm
    {
        public bool DebugOpen { get; set; } = true;
        public string DebugReport { get; set; } = "";
        public bool DebugBusy { get; set; }
        public string DebugNote { get; set; } = "";
    }

    private static void DumpCarousel(Window window)
    {
        var carousel = FindByName(window, "HubCarousel") as ItemsControl;
        if (carousel == null) { Console.WriteLine("hub: HubCarousel introuvable"); return; }
        Console.WriteLine($"hub: {carousel.Items.Count} carte(s), stage width={carousel.ActualWidth:F0}");
        for (var i = 0; i < carousel.Items.Count; i++)
        {
            if (carousel.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter cp)
            {
                Console.WriteLine($"hub: carte {i} — conteneur absent");
                continue;
            }
            var bez = FindByName(cp, "Bez");
            var tg = cp.RenderTransform as TransformGroup;
            var tx = tg?.Children.OfType<TranslateTransform>().FirstOrDefault();
            var sc = tg?.Children.OfType<ScaleTransform>().FirstOrDefault();
            Console.WriteLine($"hub: carte {i} x={tx?.X:F1} y={tx?.Y:F1} s={sc?.ScaleX:F2} " +
                $"opacity={cp.Opacity:F2} bezW={bez?.Width:F0} bezActual={bez?.ActualWidth:F0}");
        }
    }

    private static void DumpChrome(Window window)
    {
        var stage = FindByName(window, "HubStage") as FrameworkElement;
        var bottom = FindByName(window, "HubBottom") as FrameworkElement;
        var scroll = FindByName(window, "HubScroll") as ScrollViewer;
        var railL = FindByName(window, "RailLeft") as FrameworkElement;
        var status = FindByName(window, "HubStatus") as FrameworkElement;
        var pos = bottom != null && scroll != null
            ? bottom.TransformToVisual(scroll).Transform(new System.Windows.Point(0, 0))
            : default;
        Console.WriteLine($"hub-chrome: stage={stage?.ActualWidth:F0}x{stage?.ActualHeight:F0} " +
            $"minH={stage?.MinHeight:F0} railL={railL?.Visibility} bottom={bottom?.Visibility} " +
            $"bottomPos=({pos.X:F0},{pos.Y:F0}) status={status?.Visibility} " +
            $"viewport={scroll?.ViewportWidth:F0}x{scroll?.ViewportHeight:F0} " +
            $"extent={scroll?.ExtentHeight:F0} offset={scroll?.VerticalOffset:F0}");
    }

    private static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is FrameworkElement fe && fe.Name == name)
                return fe;
            if (FindByName(c, name) is { } f)
                return f;
        }
        return null;
    }

    private static void Capture(Window window, string path)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var dpi = VisualTreeHelper.GetDpi(window);
        var dc = GetDC(hwnd);
        var memory = CreateCompatibleDC(dc);
        var bitmap = CreateCompatibleBitmap(dc, (int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY));
        var previous = SelectObject(memory, bitmap);
        try
        {
            if (!PrintWindow(hwnd, memory, 2)) throw new InvalidOperationException("PrintWindow failed");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions())));
            using var file = File.Create(path);
            encoder.Save(file);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(hwnd, dc);
        }
    }
}
