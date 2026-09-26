using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TouchMirror.Services;
using TouchMirror.ViewModels;
using Wpf.Ui.Controls;

namespace TouchMirror;

public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
        => value is int n && n > 0 ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => Binding.DoNothing;
}

public sealed class NonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
        => value is int n ? n > 0 ? Visibility.Visible : Visibility.Collapsed
            : value is string s ? s.Length > 0 ? Visibility.Visible : Visibility.Collapsed
            : value != null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => Binding.DoNothing;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => Binding.DoNothing;
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
        => value is null || (value is string s && s.Length == 0)
            ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => Binding.DoNothing;
}

public sealed class IndexPlusOneConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
        => value is int n ? (n + 1).ToString("D2") : "01";
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => Binding.DoNothing;
}

public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
        => string.Equals(value?.ToString(), p?.ToString(), StringComparison.OrdinalIgnoreCase);
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => Binding.DoNothing;
}

public static class UiIcons
{
    public static Geometry Get(SymbolRegular s)
        => Application.Current.TryFindResource("Ic" + s) as Geometry ?? Geometry.Empty;
}

public sealed class SymToIconConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
        => value is SymbolRegular s ? UiIcons.Get(s) : Geometry.Empty;
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => Binding.DoNothing;
}

public sealed class AnyBoolToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, System.Globalization.CultureInfo c)
        => values.Any(v => v is true) ? Visibility.Visible : Visibility.Collapsed;
    public object[] ConvertBack(object v, Type[] t, object p, System.Globalization.CultureInfo c)
        => throw new NotSupportedException();
}

public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush Default =
        new(System.Windows.Media.Color.FromRgb(0xC9, 0xD1, 0xD9));

    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
    {
        if (value is string { Length: >= 4 } hex)
            try { return new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)); }
            catch { }
        return Default;
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => Binding.DoNothing;
}

public sealed class HexToSoftConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
    {
        var wantColor = t == typeof(System.Windows.Media.Color);
        if (value is not string { Length: >= 4 } hex)
            return wantColor ? System.Windows.Media.Colors.Black : System.Windows.Media.Brushes.Transparent;
        try
        {
            var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            if (p is string s && byte.TryParse(s, out var a))
                col = System.Windows.Media.Color.FromArgb(a, col.R, col.G, col.B);
            return wantColor ? (object)col : new System.Windows.Media.SolidColorBrush(col);
        }
        catch { return wantColor ? System.Windows.Media.Colors.Black : System.Windows.Media.Brushes.Transparent; }
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => Binding.DoNothing;
}

public sealed class BytesToImageConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
    {
        if (value is not byte[] { Length: > 0 } bytes)
            return Binding.DoNothing;
        var img = new System.Windows.Media.Imaging.BitmapImage();
        img.BeginInit();
        img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        img.StreamSource = new System.IO.MemoryStream(bytes);
        img.EndInit();
        img.Freeze();
        return img;
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => Binding.DoNothing;
}

public partial class MainWindow : FluentWindow
{
    private static string L(string key) => LocalizationService.Get(key);
    private readonly MainViewModel _vm = new();
    private string? _lastSelDeviceKey;
    private bool _isFullscreen;
    private bool _captureMode;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        SetRailState(RailHub, RailHubIndicator, RailHubTile, RailHubIcon, true);

        _vm.MirrorAdded += instance =>
            instance.View.Activated += _ => _vm.SetActive(instance);
        _vm.PipChanged += () => Dispatcher.Invoke(OnPipChanged);
        _vm.ScreenshotRequested += instance =>
            Dispatcher.Invoke(() =>
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "TouchMirror");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"mirror_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                instance.View.SaveScreenshot(file);
                _vm.Status = string.Format(L("cap.saved"), file);
                return file;
            });
        _vm.AnyConnected += () =>
            Dispatcher.Invoke(() =>
            {
                if (_vm.AutoFullscreen && !_isFullscreen)
                    ToggleFullscreen();
            });
        _vm.ConfirmUnverified = p => ConfirmAsync(
            L("dlg.unverified_title"),
            string.Format(L("dlg.unverified_body"), p.Name),
            L("continuer"));
        _vm.ConfirmInstall = item => ConfirmAsync(
            L("dlg.install_title"),
            string.Format(L("dlg.install_body"), item.Name, item.Entry.Author, item.Entry.Version),
            L("installer"));

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedPlugin)
                && _vm.SelectedPlugin != null
                && _activeDock != "market")
            {
                ShowDock("market");
            }
            else if (e.PropertyName == nameof(MainViewModel.ShowSettings))
            {
                if (_vm.ShowSettings && _activeDock != "settings")
                    ShowDock("settings");
                else if (!_vm.ShowSettings && _activeDock == "settings")
                    ShowDock(null);
            }
            else if (e.PropertyName == nameof(MainViewModel.IsConnected))
            {
                UpdateBotConnection();
                UpdateHubInfo();
            }
            else if (e.PropertyName == nameof(MainViewModel.ShowMirrorSurface))
            {
                NavDrawer.BeginAnimation(WidthProperty,
                    new DoubleAnimation(_vm.ShowMirrorSurface ? 56 : 196, TimeSpan.FromMilliseconds(180))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else if (e.PropertyName == nameof(MainViewModel.SelectedDevice))
            {
                var key = _vm.SelectedDevice?.DeviceKey;
                var changed = key != _lastSelDeviceKey;
                _lastSelDeviceKey = key;
                LayoutHubCarousel(changed);
            }
            else if (e.PropertyName is nameof(MainViewModel.AdbStatus)
                or nameof(MainViewModel.CodecShort)
                or nameof(MainViewModel.QualityShort)
                or nameof(MainViewModel.BitRateShort)
                or nameof(MainViewModel.IsBusy)
                or nameof(MainViewModel.ActiveMirror))
            {
                UpdateHubInfo();
            }
        };

        HubConnectBtn.Command = _vm.ConnectToDeviceCommand;

        _vm.Devices.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(new Action(() => LayoutHubCarousel(false)));

        HubCarousel.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (HubCarousel.ItemContainerGenerator.Status
                == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                Dispatcher.BeginInvoke(new Action(() => LayoutHubCarousel(false)));
        };

        HubScroll.SizeChanged += (_, _) =>
            Dispatcher.BeginInvoke(new Action(RecalcHubStage));

        VersionText.Text = $"TouchMirror v{GetType().Assembly.GetName().Version?.ToString(3)}{UpdateService.DevSuffix}";

        UpdateBotConnection();

        _edgeTimer.Tick += (_, _) => UpdateEdgeOverlays();
        _edgeTimer.Start();

        Loaded += async (_, _) =>
        {
            if (_vm.ShowSettings)
                ShowDock("settings");
            await _vm.InitializeAsync();
        };
        Closing += OnClosing;
        Deactivated += (_, _) => ReleaseAllKeys();
        _vm.ConfirmKill = targets => ConfirmAsync(
            L("dbg.kill_title"),
            string.Format(L("dbg.kill_body"), string.Join("\n", targets)),
            L("dbg.kill_confirm"), danger: true);
    }

    private Views.PipWindow? _pipWindow;

    private void OnPipChanged()
    {
        var m = _vm.PipMirror;
        if (m == null || !_vm.Mirrors.Contains(m))
        {
            _pipWindow?.Close();
            _pipWindow = null;
            return;
        }
        if (_pipWindow == null)
        {
            _pipWindow = new Views.PipWindow { Owner = this };
            _pipWindow.Closed += (_, _) =>
            {
                _pipWindow = null;
                _vm.ClosePip();
            };
            _pipWindow.Show();
        }
        _pipWindow.Bind(m, () => _vm.SetActive(m));
    }

    private bool _closing;

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closing) return;
        _closing = true;
        e.Cancel = true;
        _ = FinishClosingAsync();
    }

    private async Task FinishClosingAsync()
    {
        _vm.StopTracking();
        _vm.StopPlugins();
        _vm.SaveNow();
        _vm.StopAirPlay();
        await _vm.ShutdownApiAsync();
        foreach (var m in _vm.Mirrors.ToList())
        {
            m.ManualDisconnect = true;
            await m.DisconnectAsync();
        }
        Application.Current.Shutdown();
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed)
            try { DragMove(); } catch { }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private Point? _botFocus;

    private void UpdateBotGaze(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        var c = BotMascot.TransformToAncestor(this).Transform(new Point(23, 22));
        double gx, gy;
        if (_botFocus is { } f)
        {
            gx = f.X; gy = f.Y;
        }
        else
        {
            gx = Math.Clamp((p.X - c.X) / 300.0, -1, 1);
            gy = Math.Clamp((p.Y - c.Y) / 160.0, -1, 1);
        }
        var nx = gx * 7;
        var ny = gy * 4;
        EyeLT.X += (nx - EyeLT.X) * .3;
        EyeLT.Y += (ny - EyeLT.Y) * .3;
        EyeDT.X += (nx - EyeDT.X) * .3;
        EyeDT.Y += (ny - EyeDT.Y) * .3;
        PumpkinEyeLT.X += (nx * .5 - PumpkinEyeLT.X) * .3;
        PumpkinEyeLT.Y += (ny * .6 - PumpkinEyeLT.Y) * .3;
    }

    private void SetBotMouth(string d) => MouthPath.Data = Geometry.Parse(d);

    private void SetPumpkinMouth(string d) => PumpkinMouth.Data = Geometry.Parse(d);

    private const string PumpkinMouthCalm = "M35 52 L39 56 L43 52 L47 57 L50 53 L53 57 L57 52 L61 56 L65 52 L65 60 L35 60 Z";
    private const string PumpkinMouthHappy = "M32 50 L37 55 L42 50 L46 56 L50 51 L54 56 L58 50 L63 55 L68 50 L68 62 L32 62 Z";
    private const string PumpkinMouthEager = "M33 51 L38 57 L43 51 L47 57 L50 52 L53 57 L57 51 L62 57 L67 51 L67 61 L33 61 Z";
    private const string PumpkinMouthWorried = "M38 56 L42 52 L46 55 L50 52 L54 55 L58 52 L62 56 L62 59 L38 59 Z";

    private void BotHopAnim()
    {
        var hop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(480) };
        hop.KeyFrames.Add(new SplineDoubleKeyFrame(-7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)))
        { KeySpline = new KeySpline(.2, .7, .3, 1) });
        hop.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360))));
        hop.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(480))));
        BotHop.BeginAnimation(TranslateTransform.YProperty, hop);
    }

    private void UpdateBotConnection()
    {
        var idle = (Storyboard)FindResource("BotIdle");
        if (_vm.IsConnected)
        {
            idle.Begin(this, true);
            BotCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
            SetBotMouth("M38 58 Q50 68 62 58");
            BlushL.BeginAnimation(OpacityProperty, new DoubleAnimation(.7, TimeSpan.FromMilliseconds(200)));
            BlushR.BeginAnimation(OpacityProperty, new DoubleAnimation(.7, TimeSpan.FromMilliseconds(200)));
            PumpkinCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
            SetPumpkinMouth(PumpkinMouthHappy);
            PumpkinBlushL.BeginAnimation(OpacityProperty, new DoubleAnimation(.5, TimeSpan.FromMilliseconds(200)));
            PumpkinBlushR.BeginAnimation(OpacityProperty, new DoubleAnimation(.5, TimeSpan.FromMilliseconds(200)));
            BotHopAnim();
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
            t.Tick += (_, _) => { t.Stop(); OnBotCalm(this, null); };
            t.Start();
        }
        else
        {
            _botFocus = null;
            idle.Stop(this);
            SetBotMouth("M45 61 Q50 62 55 61");
            LidLS.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(280)));
            LidDS.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(280)));
            BotCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(.6, TimeSpan.FromMilliseconds(300)));
            BlushL.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
            BlushR.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
            SetPumpkinMouth(PumpkinMouthCalm);
            PumpkinLidLS.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(.62, TimeSpan.FromMilliseconds(280)));
            PumpkinCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(.6, TimeSpan.FromMilliseconds(300)));
            PumpkinBlushL.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
            PumpkinBlushR.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
        }
    }

    private void OnBotClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        BotHopAnim();
    }

    private void OnBotEager(object sender, MouseEventArgs e)
    {
        if (!_vm.IsConnected)
        {
            _botFocus = new Point(1, .15);
            SetBotMouth("M41 60 Q50 66.5 59 60");
            BlushL.BeginAnimation(OpacityProperty, new DoubleAnimation(.55, TimeSpan.FromMilliseconds(200)));
            BlushR.BeginAnimation(OpacityProperty, new DoubleAnimation(.55, TimeSpan.FromMilliseconds(200)));
            SetPumpkinMouth(PumpkinMouthEager);
            PumpkinBlushL.BeginAnimation(OpacityProperty, new DoubleAnimation(.4, TimeSpan.FromMilliseconds(200)));
            PumpkinBlushR.BeginAnimation(OpacityProperty, new DoubleAnimation(.4, TimeSpan.FromMilliseconds(200)));
        }
    }

    private void OnBotWorried(object sender, MouseEventArgs e)
    {
        _botFocus = new Point(.6, .8);
        SetBotMouth("M42 63 Q50 58 58 63");
        EyeLS.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.82, TimeSpan.FromMilliseconds(180)));
        EyeDS.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.82, TimeSpan.FromMilliseconds(180)));
        SetPumpkinMouth(PumpkinMouthWorried);
        PumpkinEyeLS.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.82, TimeSpan.FromMilliseconds(180)));
    }

    private void OnBotCalm(object? sender, MouseEventArgs? e)
    {
        _botFocus = null;
        SetBotMouth(_vm.IsConnected ? "M44 62 Q50 65 56 62" : "M45 61 Q50 62 55 61");
        BlushL.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        BlushR.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        EyeLS.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        EyeDS.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        SetPumpkinMouth(_vm.IsConnected ? PumpkinMouthHappy : PumpkinMouthCalm);
        PumpkinBlushL.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        PumpkinBlushR.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        PumpkinEyeLS.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
    }

    private void OnOpenCapturesClick(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "TouchMirror");
        Directory.CreateDirectory(dir);
        System.Diagnostics.Process.Start("explorer.exe", dir);
    }
    private void OnRotateDisplayClick(object sender, RoutedEventArgs e)
        => _vm.ActiveMirror?.View.CycleDisplayRotation();

    private void OnColorSliderReset(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider s && double.TryParse(s.Tag as string,
                System.Globalization.CultureInfo.InvariantCulture, out var def))
            s.Value = def;
    }

    private void OnScopeGlobalClick(object sender, RoutedEventArgs e) => _vm.SettingsScopeGlobal = true;
    private void OnScopeDeviceClick(object sender, RoutedEventArgs e) => _vm.SettingsScopeGlobal = false;

    private void OnPresetSegClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string t } && int.TryParse(t, out var i) && i < _vm.QualityPresets.Length)
            _vm.SelectedQualityPreset = _vm.QualityPresets[i];
    }

    private void OnVideoCodecSegClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string v }) _vm.VideoCodec = v;
    }

    private void OnVideoDecoderSegClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string v }) _vm.VideoDecoder = v;
    }

    private void OnColorFxHeaderClick(object sender, MouseButtonEventArgs e)
    {
        var show = ColorFxPanel.Visibility != Visibility.Visible;
        ColorFxPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ColorFxChevron.Data = UiIcons.Get(show ? SymbolRegular.ChevronDown24 : SymbolRegular.ChevronRight24);
    }

    private string? _activeDock;

    private void OnSettingsClick(object sender, RoutedEventArgs e)
        => ShowDock(_activeDock == "settings" ? null : "settings");

    private void OnDiagClick(object sender, RoutedEventArgs e)
    {
        var show = _activeDock != "diag";
        ShowDock(show ? "diag" : null);
        if (show)
            _vm.RefreshDebugCommand.Execute(null);
    }

    private void ShowDock(string? panel)
    {
        _activeDock = panel;
        DockPanel.Visibility = panel == null ? Visibility.Collapsed : Visibility.Visible;
        HelpPanel.Visibility = panel == "guides" ? Visibility.Visible : Visibility.Collapsed;
        PluginsPanel.Visibility = panel == "plugins" ? Visibility.Visible : Visibility.Collapsed;
        MarketPanel.Visibility = panel == "market" ? Visibility.Visible : Visibility.Collapsed;
        SettingsDock.Visibility = panel == "settings" ? Visibility.Visible : Visibility.Collapsed;
        DiagPanel.Visibility = panel == "diag" ? Visibility.Visible : Visibility.Collapsed;

        SetRailState(RailHub, RailHubIndicator, RailHubTile, RailHubIcon, panel == null);
        SetRailState(RailGuides, RailGuidesIndicator, RailGuidesTile, RailGuidesIcon, panel == "guides");
        SetRailState(RailPlugins, RailPluginsIndicator, RailPluginsTile, RailPluginsIcon, panel == "plugins");
        SetRailState(RailMarket, RailMarketIndicator, RailMarketTile, RailMarketIcon, panel == "market");
        SetRailState(RailDiag, RailDiagIndicator, RailDiagTile, RailDiagIcon, panel == "diag");
        SetRailState(RailSettings, RailSettingsIndicator, RailSettingsTile, RailSettingsIcon, panel == "settings");

        var wantSettings = panel == "settings";
        if (_vm.ShowSettings != wantSettings)
            _vm.ShowSettings = wantSettings;

        if (panel == null)
            return;
        Border target = panel switch
        {
            "guides" => HelpPanel,
            "plugins" => PluginsPanel,
            "market" => MarketPanel,
            "diag" => DiagPanel,
            _ => SettingsDock
        };
        var sb = new System.Windows.Media.Animation.Storyboard();
        var slide = new System.Windows.Media.Animation.DoubleAnimation(-24, 0, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
        System.Windows.Media.Animation.Storyboard.SetTarget(slide, target);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(slide, new PropertyPath("RenderTransform.X"));
        System.Windows.Media.Animation.Storyboard.SetTarget(fade, target);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
        sb.Children.Add(slide);
        sb.Children.Add(fade);
        sb.Begin();
    }

    private void SetRailState(Wpf.Ui.Controls.Button btn, Border indicator,
        Border tile, System.Windows.Shapes.Path icon, bool active)
    {
        btn.Appearance = active
            ? Wpf.Ui.Controls.ControlAppearance.Secondary
            : Wpf.Ui.Controls.ControlAppearance.Transparent;
        indicator.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        tile.Background = active
            ? (System.Windows.Media.Brush)FindResource("AccentSubtleBrush")
            : (System.Windows.Media.Brush)FindResource("IconTileBrush");
        icon.Fill = active
            ? (System.Windows.Media.Brush)FindResource("DofusAccentBrush")
            : (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush");
    }

    private void OnHubClick(object sender, RoutedEventArgs e)
    {
        _vm.ShowHub = true;
        ShowDock(null);
    }

    private void OnPluginsClick(object sender, RoutedEventArgs e)
    {
        _vm.RescanPluginsCommand.Execute(null);
        ShowDock(_activeDock == "plugins" ? null : "plugins");
    }

    private void OnCatalogClick(object sender, RoutedEventArgs e)
    {
        _vm.SelectedPlugin = null;
        _vm.LoadCatalogCommand.Execute(null);
        ShowDock(_activeDock == "market" ? null : "market");
    }

    private void OnPluginDetailClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PluginInstance p)
            return;
        _vm.OpenInstalledPluginCommand.Execute(p);
        ShowDock("market");
    }

    private void OnDockClose(object sender, RoutedEventArgs e)
    {
        _vm.SelectedPlugin = null;
        ShowDock(null);
    }

    private void OnSidebarDeviceClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not Services.AdbDevice d)
            return;
        var mirror = _vm.Mirrors.FirstOrDefault(m => m.Device.SharesIdentity(d));
        _vm.SelectedDevice = d;
        if (mirror != null)
            _vm.SetActive(mirror);
        else
            _vm.ShowHub = true;
    }



    private void OnCatalogItemClick(object sender, MouseButtonEventArgs e)
    {
        var item = (sender as FrameworkElement)?.DataContext as MarketplaceItem ?? _vm.FeaturedPlugin;
        if (item != null)
            _vm.SelectPluginCommand.Execute(item);
    }

    private void OnCopyPluginHash(object sender, RoutedEventArgs e)
    {
        var hash = _vm.SelectedPlugin?.Entry.Hash;
        if (!string.IsNullOrEmpty(hash))
            System.Windows.Clipboard.SetText(hash);
    }

    private void OnDeviceNameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb || tb.DataContext is not Services.AdbDevice d)
            return;
        if (e.Key is Key.Enter or Key.Return)
        {
            CommitDeviceName(tb, d);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            tb.Text = d.ShortName;
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnDeviceNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox tb && tb.DataContext is Services.AdbDevice d)
            CommitDeviceName(tb, d);
    }

    private void CommitDeviceName(System.Windows.Controls.TextBox tb, Services.AdbDevice device)
    {
        var text = tb.Text.Trim();
        if (text != device.ShortName)
            _vm.RenameDevice(device, text);
    }

    private void UpdateHubInfo()
    {
        var d = _vm.SelectedDevice;
        if (d != null)
            d = _vm.Devices.FirstOrDefault(x => x.DeviceKey == d.DeviceKey) ?? d;
        d ??= _vm.ActiveMirror?.Device ?? _vm.Devices.FirstOrDefault();
        var vis = d != null ? Visibility.Visible : Visibility.Collapsed;
        var railsVis = vis == Visibility.Visible && HubScroll.ViewportWidth >= 1000
            ? Visibility.Visible : Visibility.Collapsed;
        var railW = railsVis == Visibility.Visible ? new GridLength(236) : new GridLength(0);
        if (RailColL.Width != railW)
            RailColL.Width = RailColR.Width = railW;
        HubStatus.Visibility = HubBottom.Visibility = vis;
        RailLeft.Visibility = RailRight.Visibility = railsVis;
        if (d == null)
            return;
        var accent = (Brush)FindResource("DofusAccentBrush");
        var dim = (Brush)FindResource("TextFillColorTertiaryBrush");
        var sec = (Brush)FindResource("TextFillColorSecondaryBrush");
        HubStatusDot.Fill = d.IsReady
            ? accent
            : d.NeedsAuthorization
                ? (Brush)FindResource("WarnBrush")
                : new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
        HubStatusText.Text = $"{d.ShortName} · {d.StateText}";
        HubStatusText.Foreground = d.IsReady ? accent : sec;
        RailName.Text = d.ShortName;
        RailModel.Text = $"{d.Model} · {d.TransportText}";
        RailState.Text = d.StateText;
        RailState.Foreground = d.IsReady ? accent : sec;
        RailBatteryRow.Visibility = d.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        RailBattery.Text = d.BatteryText;
        RailBattery.Foreground = d.Battery is <= 20
            ? (Brush)FindResource("WarnBrush") : (Brush)FindResource("SageBrush");
        RailTransport.Text = d.TransportText;
        RailSerial.Text = d.MaskedSerial;
        RailAdb.Text = _vm.AdbStatus;
        RailAdb.Foreground = d.IsReady ? accent : dim;
        RailCodec.Text = _vm.CodecShort;
        RailQuality.Text = _vm.QualityShort;
        RailBitrate.Text = _vm.BitRateShort;
        HubCapName.DataContext = d;
        if (!HubCapName.IsKeyboardFocused && HubCapName.Text != d.ShortName)
            HubCapName.Text = d.ShortName;
        HubCapState.Text = $"{d.StateText} · {d.TransportText}";
        HubAccountsBtn.DataContext = d;
        if (!d.IsRememberedOnly)
            AppLogger.Forget(_vm.RefreshHubAccountsAsync(d));
        HubConnectBtn.Visibility = HubAccountsBtn.Visibility =
            d.IsRememberedOnly ? Visibility.Collapsed : Visibility.Visible;
        HubConnectBtn.CommandParameter = d;
        HubConnectBtn.IsEnabled = d.IsReady && !_vm.IsBusy;
    }

    private void RecalcHubStage()
    {
        HubBlock.MinHeight = Math.Max(0, HubScroll.ViewportHeight - 50);
        var above = HubHead.ActualHeight + HubWsBanner.ActualHeight;
        HubStage.MinHeight = Math.Clamp(HubScroll.ViewportHeight - above - 46, 320, 570);
        LayoutHubCarousel(false);
    }

    private void LayoutHubCarousel(bool animate)
    {
        if (HubCarousel == null)
            return;
        UpdateHubInfo();
        var devs = _vm.Devices;
        var selKey = _vm.SelectedDevice?.DeviceKey;
        var sel = -1;
        for (var i = 0; i < devs.Count; i++)
            if (devs[i].DeviceKey == selKey) { sel = i; break; }
        if (sel < 0)
            sel = 0;

        var multi = devs.Count > 1;
        HubPrev.Visibility = HubNext.Visibility = HubDots.Visibility =
            multi ? Visibility.Visible : Visibility.Collapsed;
        RebuildHubDots(devs.Count, sel);
        if (devs.Count == 0)
            return;

        var widths = new double[devs.Count];
        var cps = new ContentPresenter?[devs.Count];
        for (var i = 0; i < devs.Count; i++)
        {
            if (HubCarousel.ItemContainerGenerator.ContainerFromIndex(i)
                is ContentPresenter cp)
            {
                cps[i] = cp;
                var bw = FindNamed(cp, "Bez") is { } bez ? bez.Width : 0;
                widths[i] = double.IsNaN(bw) || bw <= 0 ? 206.0 : bw;
            }
        }
        var centerHalf = widths[sel] / 2;
        if (Environment.GetEnvironmentVariable("TM_HUB_DEBUG") == "1")
            Console.WriteLine($"hub-layout sel={sel} widths=[{string.Join(",", widths.Select(w => w.ToString("F0")))}] cps={cps.Count(c => c != null)}");

        var dur = TimeSpan.FromMilliseconds(animate ? 420 : 0);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        for (var i = 0; i < devs.Count; i++)
        {
            if (cps[i] is not { } cp)
                continue;
            var off = i - sel;
            var ao = Math.Abs(off);
            if (cp.RenderTransform is not TransformGroup tg || tg.Children.Count != 2
                || tg.Children[0] is not ScaleTransform)
            {
                tg = new TransformGroup();
                tg.Children.Add(new ScaleTransform());
                tg.Children.Add(new TranslateTransform());
                cp.RenderTransform = tg;
            }
            var sc = (ScaleTransform)tg.Children[0];
            var tx = (TranslateTransform)tg.Children[1];
            var s = off == 0 ? 1.0 : ao == 1 ? 0.55 : 0.42;
            var x = HubCardOffset(off, centerHalf, widths[i], s);
            var y = off == 0 ? 0.0 : 44.0;
            var o = off == 0 ? 1.0 : ao == 1 ? 0.38 : 0.0;
            Panel.SetZIndex(cp, -ao);
            cp.IsHitTestVisible = ao <= 1;
            if (animate)
            {
                tx.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(x, dur) { EasingFunction = ease });
                tx.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(y, dur) { EasingFunction = ease });
                sc.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(s, dur) { EasingFunction = ease });
                sc.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(s, dur) { EasingFunction = ease });
                cp.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(o, dur) { EasingFunction = ease });
            }
            else
            {
                tx.BeginAnimation(TranslateTransform.XProperty, null);
                tx.BeginAnimation(TranslateTransform.YProperty, null);
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                cp.BeginAnimation(OpacityProperty, null);
                tx.X = x; tx.Y = y;
                sc.ScaleX = s; sc.ScaleY = s;
                cp.Opacity = o;
            }
        }
    }

    public static double HubCardOffset(int off, double centerHalf, double cardW, double scale)
    {
        if (off == 0)
            return 0;
        var sideHalf = cardW * scale / 2;
        return Math.Sign(off) * (centerHalf + sideHalf - 56.0 + (Math.Abs(off) - 1) * 150.0);
    }

    private static FrameworkElement? FindNamed(DependencyObject root, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name)
                return fe;
            if (FindNamed(child, name) is { } found)
                return found;
        }
        return null;
    }

    private void RebuildHubDots(int count, int sel)
    {
        if (HubDots.Children.Count != count)
        {
            HubDots.Children.Clear();
            for (var i = 0; i < count; i++)
                HubDots.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 6, Height = 6,
                    Margin = new Thickness(4, 0, 4, 0),
                });
        }
        var on = (Brush)FindResource("DofusAccentBrush");
        var off = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        for (var i = 0; i < HubDots.Children.Count; i++)
            ((System.Windows.Shapes.Ellipse)HubDots.Children[i]).Fill =
                i == sel ? on : off;
    }

    private void CycleHubSelection(int dir)
    {
        var devs = _vm.Devices;
        if (devs.Count < 2)
            return;
        var key = _vm.SelectedDevice?.DeviceKey;
        var idx = -1;
        for (var i = 0; i < devs.Count; i++)
            if (devs[i].DeviceKey == key) { idx = i; break; }
        if (idx < 0)
            idx = 0;
        _vm.SelectedDevice = devs[(idx + dir + devs.Count) % devs.Count];
    }

    private void OnHubPrev(object sender, MouseButtonEventArgs e)
    {
        CycleHubSelection(-1);
        e.Handled = true;
    }

    private void OnHubNext(object sender, MouseButtonEventArgs e)
    {
        CycleHubSelection(1);
        e.Handled = true;
    }

    private void OnHubCardSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
            Dispatcher.BeginInvoke(new Action(() => LayoutHubCarousel(false)));
    }

    private void OnHubCardPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe
            && fe.DataContext is Services.AdbDevice d
            && d.DeviceKey != _vm.SelectedDevice?.DeviceKey)
        {
            _vm.SelectedDevice = d;
            e.Handled = true;
        }
    }

    private void OnMirrorNameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb)
            return;
        if (e.Key is Key.Enter or Key.Return)
        {
            CommitMirrorName(tb);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnMirrorNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox tb)
            CommitMirrorName(tb);
    }

    private void CommitMirrorName(System.Windows.Controls.TextBox tb)
    {
        var device = tb.DataContext switch
        {
            MirrorInstance m => m.Device,
            MainViewModel => _vm.ActiveMirror?.Device,
            _ => null
        };
        if (device != null)
            _vm.RenameDevice(device, tb.Text);
    }

    private bool _browserReady;

    private async void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var show = _activeDock != "guides";
        ShowDock(show ? "guides" : null);
        if (!show || _browserReady)
            return;
        try
        {
            await HelpBrowser.EnsureCoreWebView2Async();
            HelpBrowser.CoreWebView2.Navigate("https://www.dofus-touch.com/fr/");
            HelpBrowser.SourceChanged += (_, _) =>
                AddressBar.Text = HelpBrowser.Source?.ToString() ?? "";
            HelpBrowser.CoreWebView2.DocumentTitleChanged += (_, _) =>
                PageTitle.Text = string.IsNullOrWhiteSpace(HelpBrowser.CoreWebView2.DocumentTitle)
                    ? L("help.browser_title") : HelpBrowser.CoreWebView2.DocumentTitle;
            HelpBrowser.CoreWebView2.NavigationStarting += (_, _) =>
                NavProgress.Visibility = Visibility.Visible;
            HelpBrowser.CoreWebView2.NavigationCompleted += (_, _) =>
                NavProgress.Visibility = Visibility.Collapsed;
            _browserReady = true;
        }
        catch (Exception ex)
        {
            _vm.Status = string.Format(L("st.webview_fail"), ex.Message);
            ShowDock(null);
        }
    }

    private void OnSiteClick(object sender, RoutedEventArgs e)
    {
        if (_browserReady && sender is FrameworkElement { Tag: string url })
            HelpBrowser.CoreWebView2.Navigate(url);
    }

    private void OnNavBack(object sender, RoutedEventArgs e)
    {
        if (_browserReady && HelpBrowser.CanGoBack) HelpBrowser.GoBack();
    }

    private void OnNavForward(object sender, RoutedEventArgs e)
    {
        if (_browserReady && HelpBrowser.CanGoForward) HelpBrowser.GoForward();
    }

    private void OnNavReload(object sender, RoutedEventArgs e)
    {
        if (_browserReady) HelpBrowser.Reload();
    }

    private void OnNavHome(object sender, RoutedEventArgs e)
    {
        if (_browserReady) HelpBrowser.CoreWebView2.Navigate("https://www.dofus-touch.com/fr/");
    }

    private void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !_browserReady)
            return;
        var url = AddressBar.Text.Trim();
        if (url.Length == 0)
            return;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        HelpBrowser.CoreWebView2.Navigate(url);
    }

    private static bool IsInteractiveSource(object? source)
    {
        for (var d = source as DependencyObject; d != null;
             d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.TextBox)
                return true;
        return false;
    }

    private void OnWorkspaceTabClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkspaceItem { IsEditing: false } item }
            && !IsInteractiveSource(e.OriginalSource))
            AppLogger.Forget(_vm.SelectWorkspaceAsync(item));
    }

    private void OnWorkspaceEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { DataContext: WorkspaceItem item })
            item.IsEditing = true;
    }

    private void OnWorkspaceDuplicateClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { DataContext: WorkspaceItem item })
            _vm.DuplicateWorkspaceCommand.Execute(item);
    }

    private async void OnWorkspaceDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkspaceItem item }
            && await ConfirmAsync(L("dlg.del_ws_title"),
                string.Format(L("dlg.del_ws"), item.Name), L("supprimer"), danger: true))
            _vm.DeleteWorkspaceCommand.Execute(item);
    }

    private TaskCompletionSource<bool>? _confirmTcs;

    private Task<bool> ConfirmAsync(string title, string body, string ok, bool danger = false)
    {
        ConfirmTitle.Text = title;
        ConfirmBody.Text = body;
        ConfirmOk.Content = ok;
        ConfirmOk.ClearValue(BackgroundProperty);
        if (danger)
            ConfirmOk.Background = (Brush)FindResource("DangerBrush");
        ConfirmOverlay.Visibility = Visibility.Visible;
        ConfirmOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        _confirmTcs = new TaskCompletionSource<bool>();
        return _confirmTcs.Task;
    }

    private void OnConfirmOk(object sender, RoutedEventArgs e) => ResolveConfirm(true);

    private void OnConfirmDismiss(object sender, RoutedEventArgs e) => ResolveConfirm(false);

    private void ResolveConfirm(bool result)
    {
        _confirmTcs?.TrySetResult(result);
        _confirmTcs = null;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
        fade.Completed += (_, _) => ConfirmOverlay.Visibility = Visibility.Collapsed;
        ConfirmOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private void OnWorkspaceExitClick(object sender, RoutedEventArgs e) => _vm.ExitWorkspace();

    private async void OnDeviceAccountsClick(object sender, RoutedEventArgs e)
        => await OpenAccountsPopupAsync(sender as FrameworkElement);

    private async void OnHubStackClick(object sender, MouseButtonEventArgs e)
        => await OpenAccountsPopupAsync(sender as FrameworkElement);

    private async Task OpenAccountsPopupAsync(FrameworkElement? anchor)
    {
        var device = anchor?.DataContext switch
        {
            Services.AdbDevice d => d,
            ViewModels.MirrorInstance m => m.Device,
            _ => _vm.SelectedDevice ?? _vm.ActiveMirror?.Device
        };
        if (device == null || device.IsRememberedOnly)
            return;
        AccountsPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        AccountsPopup.PlacementTarget = anchor;
        AccountsPopup.IsOpen = true;
        _vm.AccountNotice = null;
        await _vm.OpenAccountsPopupAsync(device);
    }

    private async void OnAccountsFlyoutClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem mi)
            return;
        var device = mi.DataContext as Services.AdbDevice ?? _vm.SelectedDevice;
        if (device == null || device.IsRememberedOnly)
            return;
        AccountsPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        AccountsPopup.PlacementTarget = null;
        AccountsPopup.IsOpen = true;
        _vm.AccountNotice = null;
        await _vm.OpenAccountsPopupAsync(device);
    }

    private async void OnAccountOpenClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ViewModels.AccountItem item)
            return;
        AccountsPopup.IsOpen = false;
        if (item.IsPrimary)
            await _vm.ConnectExistingDeviceAsync(item.Device);
        else if (item.Profile != null)
            await _vm.OpenAccountAsync(item.Device, item.Profile);
    }

    private void OnAccountDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.AccountItem { Profile: not null } item)
            item.IsConfirming = true;
    }

    private void OnAccountDeleteCancelClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.AccountItem item)
            item.IsConfirming = false;
    }

    private async void OnAccountDeleteConfirmClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ViewModels.AccountItem item
            || item.Profile == null)
            return;
        item.IsConfirming = false;
        await _vm.RemoveAccountAsync(item.Device, item.Profile);
    }

    private void OnAvatarPickerToggle(object sender, MouseButtonEventArgs e)
    {
        _vm.ToggleAvatarPicker();
        e.Handled = true;
    }

    private void OnAvatarPickerCloseClick(object sender, RoutedEventArgs e)
        => _vm.CloseAvatarPicker();

    private void OnAccountAvatarClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.AccountItem item)
            _vm.OpenAvatarEdit(item);
        e.Handled = true;
    }

    private void OnAccountNameMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2
            || (sender as FrameworkElement)?.DataContext is not ViewModels.AccountItem item)
            return;
        item.IsRenaming = true;
        e.Handled = true;
    }

    private void OnAccountNameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb
            || tb.DataContext is not ViewModels.AccountItem item)
            return;
        if (e.Key is Key.Enter or Key.Return)
        {
            _vm.CommitAccountRename(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.IsRenaming = false;
            e.Handled = true;
        }
    }

    private void OnAccountNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox tb
            && tb.DataContext is ViewModels.AccountItem { IsRenaming: true } item)
            _vm.CommitAccountRename(item);
    }

    private async void OnAvatarPickClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.BreedAvatar av)
            await _vm.PickAvatarAsync(av);
        e.Handled = true;
    }

    private async void OnAccountCreateClick(object sender, RoutedEventArgs e)
    {
        var device = _vm.AccountsDevice;
        if (device == null || _vm.IsBusy)
            return;
        var name = _vm.NewAccountName.Trim();
        if (name.Length == 0)
            name = "Compte";
        await _vm.CreateAccountAsync(device, name);
        if (_vm.AccountNotice == null)
            AccountsPopup.IsOpen = false;
    }

    private void OnWorkspaceNameVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is System.Windows.Controls.TextBox tb)
        {
            tb.Focus();
            tb.SelectAll();
        }
    }

    private void OnWorkspaceNameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb
            || tb.DataContext is not WorkspaceItem item)
            return;
        if (e.Key is Key.Enter or Key.Return)
        {
            _vm.RenameWorkspace(item, tb.Text);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.IsEditing = false;
            tb.Text = item.Name;
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnWorkspaceNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox tb
            && tb.DataContext is WorkspaceItem { IsEditing: true } item)
            _vm.RenameWorkspace(item, tb.Text);
    }

    private void OnMissingClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MissingDeviceItem item }
            && !IsInteractiveSource(e.OriginalSource))
            _vm.ConnectMissingCommand.Execute(item);
    }

    private void OnDevicePinClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Services.AdbDevice d })
            _vm.SetDevicePinned(d, !d.Pinned);
    }

    private void OnDeviceColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { DataContext: Services.AdbDevice d } mi)
            _vm.SetDeviceColor(d, mi.Tag is string s && s.Length > 0 ? s : null);
    }

    private void OnDeviceForgetClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { DataContext: Services.AdbDevice d })
            _vm.ForgetDeviceCommand.Execute(d);
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        var btn = (FrameworkElement)sender;
        if (btn.ContextMenu is { } menu)
        {
            menu.PlacementTarget = btn;
            menu.IsOpen = true;
        }
    }

    private void OnCalcClick(object sender, RoutedEventArgs e)
        => CalcPopup.IsOpen = !CalcPopup.IsOpen;

    private void OnCalcOpened(object sender, EventArgs e)
    {
        CalcInput.Focus();
        CalcInput.CaretIndex = CalcInput.Text.Length;
    }

    private void OnCalcInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CalcEval();
            e.Handled = true;
        }
    }

    private void OnCalcKeyClick(object sender, RoutedEventArgs e)
    {
        CalcInput.Text += (string)((FrameworkElement)sender).Tag;
        CalcInput.Focus();
        CalcInput.CaretIndex = CalcInput.Text.Length;
    }

    private void OnCalcQuickClick(object sender, RoutedEventArgs e)
    {
        var tag = (string)((FrameworkElement)sender).Tag;
        var t = CalcInput.Text;
        switch (tag)
        {
            case "hdv": if (t.Length > 0) CalcInput.Text = $"({t}) * 0.98"; break;
            case "x100": if (t.Length > 0) CalcInput.Text = $"({t}) * 100"; break;
            case "x1k": if (t.Length > 0) CalcInput.Text = $"({t}) * 1000"; break;
            case "bk": CalcInput.Text = t.Length > 0 ? t[..^1] : t; break;
            case "clr": CalcInput.Text = ""; CalcResult.Text = ""; break;
        }
        CalcInput.Focus();
        CalcInput.CaretIndex = CalcInput.Text.Length;
    }

    private void OnCalcEvalClick(object sender, RoutedEventArgs e) => CalcEval();

    private void CalcEval()
    {
        var expr = CalcInput.Text;
        if (string.IsNullOrWhiteSpace(expr))
        {
            CalcResult.Text = "";
            return;
        }
        try
        {
            var p = new CalcParser(expr);
            var v = p.Parse();
            CalcResult.Text = double.IsFinite(v) ? "= " + FormatKamas(v) : "?";
        }
        catch { CalcResult.Text = "?"; }
    }

    private static string FormatKamas(double v)
    {
        var r = Math.Round(v, 2);
        var parts = r.ToString(r == Math.Floor(r) ? "0" : "0.##",
            System.Globalization.CultureInfo.InvariantCulture).Split('.');
        var s = parts[0];
        var neg = s.StartsWith('-');
        if (neg) s = s[1..];
        for (var i = s.Length - 3; i > 0; i -= 3)
            s = s.Insert(i, " ");
        return (neg ? "-" : "") + s + (parts.Length > 1 ? "," + parts[1] : "");
    }

    private sealed class CalcParser
    {
        private readonly string _s;
        private int _i;

        public CalcParser(string s) => _s = s.Replace(',', '.');

        public double Parse()
        {
            var v = Expr();
            Skip();
            if (_i < _s.Length)
                throw new FormatException();
            return v;
        }

        private void Skip() { while (_i < _s.Length && _s[_i] == ' ') _i++; }
        private bool Eat(char c) { Skip(); if (_i < _s.Length && _s[_i] == c) { _i++; return true; } return false; }

        private double Expr()
        {
            var v = Term();
            while (true)
            {
                if (Eat('+')) v += Term();
                else if (Eat('-')) v -= Term();
                else return v;
            }
        }

        private double Term()
        {
            var v = Factor();
            while (true)
            {
                if (Eat('*')) v *= Factor();
                else if (Eat('/')) v /= Factor();
                else return v;
            }
        }

        private double Factor()
        {
            Skip();
            if (Eat('-')) return -Factor();
            if (Eat('+')) return Factor();
            double v;
            if (Eat('('))
            {
                v = Expr();
                if (!Eat(')')) throw new FormatException();
            }
            else
            {
                var start = _i;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                if (_i == start || !double.TryParse(_s[start.._i],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v))
                    throw new FormatException();
            }
            while (Eat('%')) v /= 100;
            return v;
        }
    }

    private void OnCaptureModeClick(object sender, RoutedEventArgs e)
    {
        _captureMode = !_captureMode;
        foreach (var m in _vm.Mirrors)
            m.View.SetStatsVisible(!_captureMode);
    }

    private readonly System.Windows.Threading.DispatcherTimer _fsHideTimer = new()
        { Interval = TimeSpan.FromSeconds(2.5) };

    private readonly System.Windows.Threading.DispatcherTimer _edgeTimer = new()
        { Interval = TimeSpan.FromMilliseconds(220) };

    private string? _fsDockPanel;
    private Rect _fsBounds;
    private bool _fsWasMaximized;

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            _fsWasMaximized = WindowState == WindowState.Maximized;
            _fsBounds = RestoreBounds;
            _fsDockPanel = _activeDock;
            ShowDock(null);
            ExtendsContentIntoTitleBar = false;
            TitleBarElement.Visibility = Visibility.Collapsed;
            ChromeRow.Height = new GridLength(0);
            ContextRow.Height = new GridLength(0);
            StatusBarRow.Height = new GridLength(0);
            NavCol.Width = new GridLength(0);
            VideoFrame.Margin = new Thickness(0);
            VideoFrame.CornerRadius = new CornerRadius(0);
            VideoFrame.BorderThickness = new Thickness(0);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            FullscreenBar.Visibility = Visibility.Visible;
            _fsHideTimer.Tick += OnFsHideTick;
            _fsHideTimer.Start();
        }
        else
        {
            _fsHideTimer.Stop();
            _fsHideTimer.Tick -= OnFsHideTick;
            FullscreenBar.Visibility = Visibility.Collapsed;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            if (_fsWasMaximized)
            {
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowState = WindowState.Normal;
                Left = _fsBounds.Left;
                Top = _fsBounds.Top;
                Width = _fsBounds.Width;
                Height = _fsBounds.Height;
            }
            VideoFrame.ClearValue(MarginProperty);
            VideoFrame.CornerRadius = new CornerRadius(14);
            VideoFrame.BorderThickness = new Thickness(1);
            ChromeRow.Height = GridLength.Auto;
            ContextRow.Height = GridLength.Auto;
            StatusBarRow.Height = GridLength.Auto;
            NavCol.Width = GridLength.Auto;
            TitleBarElement.Visibility = Visibility.Visible;
            ExtendsContentIntoTitleBar = true;
            ShowDock(_fsDockPanel);
        }
    }

    private void OnFsHideTick(object? sender, EventArgs e)
    {
        _fsHideTimer.Stop();
        FullscreenBar.Visibility = Visibility.Collapsed;
    }

    private void UpdateEdgeOverlays()
    {
        if (!IsLoaded)
            return;
        var p = Mouse.GetPosition(ContentRoot);
        var inside = p.X >= 0 && p.Y >= 0 && p.X < ContentRoot.ActualWidth && p.Y < ContentRoot.ActualHeight;
        NavDrawer.Visibility = _isFullscreen ? Visibility.Collapsed : Visibility.Visible;
        SetOverlay(CmdPill, inside && _vm.IsConnected && (p.Y < 74 || CmdPill.IsMouseOver), 0, -10);
        SetOverlay(StatusChip, inside && _vm.IsConnected && (p.Y > ContentRoot.ActualHeight - 14 || StatusChip.IsMouseOver), 0, 8);
    }

    private static void SetOverlay(Border el, bool show, double ox, double oy)
    {
        if (el.Tag is bool b && b == show)
            return;
        el.Tag = show;
        el.IsHitTestVisible = show;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var t = (TranslateTransform)el.RenderTransform;
        if (ox != 0)
            t.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(show ? 0 : ox, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        if (oy != 0)
            t.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(show ? 0 : oy, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        el.BeginAnimation(OpacityProperty,
            new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(170)));
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        UpdateBotGaze(e);
        if (!_isFullscreen)
            return;
        var y = e.GetPosition(this).Y;
        if (y < 24 && FullscreenBar.Visibility != Visibility.Visible)
        {
            FullscreenBar.Visibility = Visibility.Visible;
            _fsHideTimer.Stop();
            _fsHideTimer.Start();
        }
        else if (FullscreenBar.Visibility == Visibility.Visible && y <= 80)
        {
            _fsHideTimer.Stop();
        }
        else if (FullscreenBar.Visibility == Visibility.Visible)
        {
            _fsHideTimer.Stop();
            _fsHideTimer.Start();
        }
    }

    private static bool IsTextInputTarget(object? source)
        => source is System.Windows.Controls.TextBox or System.Windows.Controls.ComboBox;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && !IsTextInputTarget(e.OriginalSource))
        {
            if (_confirmTcs != null)
            {
                ResolveConfirm(false);
                e.Handled = true;
                return;
            }
            if (_activeDock != null)
            {
                ShowDock(null);
                e.Handled = true;
                return;
            }
            if (_vm.ShowHub && _vm.Mirrors.Count > 0)
            {
                _vm.ShowHub = false;
                e.Handled = true;
                return;
            }
            if (_isFullscreen)
            {
                ToggleFullscreen();
                e.Handled = true;
                return;
            }
        }

        var mods = Keyboard.Modifiers;
        if (e.Key == Key.Tab && mods is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _vm.ActivateAdjacent(mods.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.G && mods == ModifierKeys.Control)
        {
            _vm.GridMode = !_vm.GridMode;
            e.Handled = true;
            return;
        }
        if (mods.HasFlag(ModifierKeys.Control) && !mods.HasFlag(ModifierKeys.Alt)
            && e.Key is >= Key.D1 and <= Key.D9 or >= Key.NumPad1 and <= Key.NumPad9)
        {
            var index = e.Key <= Key.D9 ? e.Key - Key.D1 : e.Key - Key.NumPad1;
            if (mods.HasFlag(ModifierKeys.Shift))
                _vm.ActivateWorkspaceAt(index);
            else
                _vm.ActivateAt(index);
            e.Handled = true;
            return;
        }

        var view = _vm.ShowMirrorSurface && _confirmTcs == null ? _vm.ActiveMirror?.View : null;
        if (view == null || IsTextInputTarget(e.OriginalSource))
            return;

        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && view.HasControl)
        {
            try
            {
                var text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                    view.PasteToDevice(text);
            }
            catch { }
            e.Handled = true;
            return;
        }

        var downKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (view.HandleKey(downKey, true, e.IsRepeat))
        {
            _keyTargets[downKey] = view;
            e.Handled = true;
        }
    }

    private readonly Dictionary<Key, Views.MirrorView> _keyTargets = new();

    private void ReleaseAllKeys()
    {
        foreach (var v in _keyTargets.Values.Distinct())
            v.ReleaseHeldKeys();
        _keyTargets.Clear();
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        var upKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_keyTargets.Remove(upKey, out var target))
        {
            if (target.HandleKey(upKey, false, e.IsRepeat))
                e.Handled = true;
            return;
        }
        var view = _vm.ShowMirrorSurface && _confirmTcs == null ? _vm.ActiveMirror?.View : null;
        if (view == null || IsTextInputTarget(e.OriginalSource))
            return;
        if (view.HandleKey(upKey, false, e.IsRepeat))
            e.Handled = true;
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var view = _vm.ShowMirrorSurface && _confirmTcs == null ? _vm.ActiveMirror?.View : null;
        if (view == null || IsTextInputTarget(e.OriginalSource))
            return;
        if (!string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
        {
            view.InjectText(e.Text);
            e.Handled = true;
        }
    }
}
