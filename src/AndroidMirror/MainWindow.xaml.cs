using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
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

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => Binding.DoNothing;
}

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _vm = new();
    private bool _isFullscreen;
    private bool _captureMode;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        _vm.MirrorAdded += instance =>
            instance.View.Activated += _ => _vm.SetActive(instance);
        _vm.ScreenshotRequested += instance =>
            Dispatcher.Invoke(() =>
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "TouchMirror");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"mirror_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                instance.View.SaveScreenshot(file);
                _vm.Status = $"Capture enregistrée → {file}";
                return file;
            });
        _vm.AnyConnected += () =>
            Dispatcher.Invoke(() =>
            {
                if (_vm.AutoFullscreen && !_isFullscreen)
                    ToggleFullscreen();
            });
        _vm.ConfirmUnverified = p => Task.FromResult(
            System.Windows.MessageBox.Show(this,
                $"« {p.Name} » n'est pas un plugin officiel — il peut exécuter n'importe quel code sur ce PC.\n\nL'activer quand même ?",
                "Plugin non vérifié",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes);

        Loaded += async (_, _) => await _vm.InitializeAsync();
        Closed += async (_, _) =>
        {
            _vm.SaveNow();
            await _vm.ShutdownApiAsync();
            foreach (var m in _vm.Mirrors.ToList())
                await m.DisconnectAsync();
        };
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void OnOpenCapturesClick(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "TouchMirror");
        Directory.CreateDirectory(dir);
        System.Diagnostics.Process.Start("explorer.exe", dir);
    }
    private void OnRotateDisplayClick(object sender, RoutedEventArgs e)
        => _vm.ActiveMirror?.View.CycleDisplayRotation();
    private void OnSettingsClick(object sender, RoutedEventArgs e) => _vm.ShowSettings = !_vm.ShowSettings;

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

    private bool _browserReady;

    private async void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var show = HelpPanel.Visibility != Visibility.Visible;
        if (show)
        {
            HelpPanel.Visibility = Visibility.Visible;
            var sb = new System.Windows.Media.Animation.Storyboard();
            var slide = new System.Windows.Media.Animation.DoubleAnimation(-40, 0, TimeSpan.FromMilliseconds(220))
                { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
            System.Windows.Media.Animation.Storyboard.SetTarget(slide, HelpPanel);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(slide, new PropertyPath("RenderTransform.X"));
            System.Windows.Media.Animation.Storyboard.SetTarget(fade, HelpPanel);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(slide);
            sb.Children.Add(fade);
            sb.Begin();
        }
        else
        {
            HelpPanel.Visibility = Visibility.Collapsed;
        }
        HelpButton.Appearance = show
            ? Wpf.Ui.Controls.ControlAppearance.Secondary
            : Wpf.Ui.Controls.ControlAppearance.Transparent;
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
                    ? "Navigateur intégré" : HelpBrowser.CoreWebView2.DocumentTitle;
            HelpBrowser.CoreWebView2.NavigationStarting += (_, _) =>
                NavProgress.Visibility = Visibility.Visible;
            HelpBrowser.CoreWebView2.NavigationCompleted += (_, _) =>
                NavProgress.Visibility = Visibility.Collapsed;
            _browserReady = true;
        }
        catch (Exception ex)
        {
            _vm.Status = $"WebView2 indisponible : {ex.Message}";
            HelpPanel.Visibility = Visibility.Collapsed;
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

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        var btn = (FrameworkElement)sender;
        if (btn.ContextMenu is { } menu)
        {
            menu.PlacementTarget = btn;
            menu.IsOpen = true;
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

    private bool _fsHelpWasVisible;
    private bool _fsSettingsWasVisible;
    private Rect _fsBounds;
    private bool _fsWasMaximized;

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            _fsWasMaximized = WindowState == WindowState.Maximized;
            _fsBounds = RestoreBounds;
            _fsHelpWasVisible = HelpPanel.Visibility == Visibility.Visible;
            _fsSettingsWasVisible = _vm.ShowSettings;
            HelpPanel.Visibility = Visibility.Collapsed;
            _vm.ShowSettings = false;
            ExtendsContentIntoTitleBar = false;
            TitleBarElement.Visibility = Visibility.Collapsed;
            TitleBarRow.Height = new GridLength(0);
            ToolbarRow.Height = new GridLength(0);
            StatusBarRow.Height = new GridLength(0);
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
            VideoFrame.Margin = new Thickness(20);
            VideoFrame.CornerRadius = new CornerRadius(14);
            VideoFrame.BorderThickness = new Thickness(1);
            TitleBarRow.Height = GridLength.Auto;
            ToolbarRow.Height = GridLength.Auto;
            StatusBarRow.Height = GridLength.Auto;
            TitleBarElement.Visibility = Visibility.Visible;
            ExtendsContentIntoTitleBar = true;
            if (_fsHelpWasVisible)
                HelpPanel.Visibility = Visibility.Visible;
            _vm.ShowSettings = _fsSettingsWasVisible;
        }
    }

    private void OnFsHideTick(object? sender, EventArgs e)
        => FullscreenBar.Visibility = Visibility.Collapsed;

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
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
        if (e.Key == Key.Escape && _isFullscreen && !IsTextInputTarget(e.OriginalSource))
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        var mods = Keyboard.Modifiers;
        if (e.Key == Key.Tab && mods is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _vm.ActivateAdjacent(mods.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
            return;
        }
        if (mods.HasFlag(ModifierKeys.Control) && !mods.HasFlag(ModifierKeys.Alt)
            && e.Key is >= Key.D1 and <= Key.D9 or >= Key.NumPad1 and <= Key.NumPad9)
        {
            _vm.ActivateAt(e.Key <= Key.D9 ? e.Key - Key.D1 : e.Key - Key.NumPad1);
            e.Handled = true;
            return;
        }

        var view = _vm.ActiveMirror?.View;
        if (view == null || IsTextInputTarget(e.OriginalSource))
            return;

        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
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

        if (view.HandleKey(e.Key, true))
            e.Handled = true;
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        var view = _vm.ActiveMirror?.View;
        if (view == null || IsTextInputTarget(e.OriginalSource))
            return;
        if (view.HandleKey(e.Key, false))
            e.Handled = true;
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var view = _vm.ActiveMirror?.View;
        if (view == null || IsTextInputTarget(e.OriginalSource))
            return;
        if (!string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
        {
            view.InjectText(e.Text);
            e.Handled = true;
        }
    }
}