using System.IO;
using System.Windows;
using System.Windows.Controls;
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

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => Binding.DoNothing;
}

/// <summary>Hex « #RRGGBB » → brush ; valeur nulle/invalide → accent par défaut.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush Default =
        new(System.Windows.Media.Color.FromRgb(0x4E, 0xC9, 0x8E));

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
                $"« {p.Name} » n'est pas un plugin officiel — il tourne dans le sandbox JavaScript et peut piloter l'app (connexion, capture, miroirs).\n\nLis le code avant de l'activer. Continuer ?",
                "Plugin non vérifié",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes);
        // « Détails » depuis une carte : bascule le dock sur la fiche marketplace.
        // ShowSettings piloté par le VM (restauration, plein écran) synchronise le dock.
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
        };

        VersionText.Text = $"TouchMirror v{GetType().Assembly.GetName().Version?.ToString(3)}";
        Loaded += async (_, _) =>
        {
            // Le réglage « panneau réglages ouvert » est persisté : rouvre le dock.
            if (_vm.ShowSettings)
                ShowDock("settings");
            await _vm.InitializeAsync();
        };
        Closed += async (_, _) =>
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
        };
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();

    // ═══ Barre de titre custom ═══
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

    private void OnOpenCapturesClick(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "TouchMirror");
        Directory.CreateDirectory(dir);
        System.Diagnostics.Process.Start("explorer.exe", dir);
    }
    private void OnRotateDisplayClick(object sender, RoutedEventArgs e)
        => _vm.ActiveMirror?.View.CycleDisplayRotation();
    // ═══ Rail + panneau docké ═══

    /// <summary>Panneau actuellement docké : "guides" | "plugins" | "market" | "settings" | null.</summary>
    private string? _activeDock;

    private void OnSettingsClick(object sender, RoutedEventArgs e)
        => ShowDock(_activeDock == "settings" ? null : "settings");

    private void ShowDock(string? panel)
    {
        _activeDock = panel;
        DockPanel.Visibility = panel == null ? Visibility.Collapsed : Visibility.Visible;
        HelpPanel.Visibility = panel == "guides" ? Visibility.Visible : Visibility.Collapsed;
        PluginsPanel.Visibility = panel == "plugins" ? Visibility.Visible : Visibility.Collapsed;
        MarketPanel.Visibility = panel == "market" ? Visibility.Visible : Visibility.Collapsed;
        SettingsDock.Visibility = panel == "settings" ? Visibility.Visible : Visibility.Collapsed;

        SetRailState(RailGuides, RailGuidesIndicator, panel == "guides");
        SetRailState(RailPlugins, RailPluginsIndicator, panel == "plugins");
        SetRailState(RailMarket, RailMarketIndicator, panel == "market");
        SetRailState(RailSettings, RailSettingsIndicator, panel == "settings");

        // Le réglage persisté « panneau réglages ouvert » suit l'état du dock.
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

    private static void SetRailState(Wpf.Ui.Controls.Button btn, Border indicator, bool active)
    {
        btn.Appearance = active
            ? Wpf.Ui.Controls.ControlAppearance.Secondary
            : Wpf.Ui.Controls.ControlAppearance.Transparent;
        indicator.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
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

    private void OnDockClose(object sender, RoutedEventArgs e)
    {
        _vm.SelectedPlugin = null;
        ShowDock(null);
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

    // ═══ Espaces de travail ═══

    /// <summary>Vrai si la source du clic est dans un bouton ou un champ texte
    /// (évite de déclencher l'action du conteneur parent).</summary>
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
            _ = _vm.SelectWorkspaceAsync(item);
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

    private void OnWorkspaceDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkspaceItem item }
            && System.Windows.MessageBox.Show(this,
                $"Supprimer l'espace « {item.Name} » ? Les téléphones ne seront pas déconnectés.",
                "Supprimer l'espace",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
            _vm.DeleteWorkspaceCommand.Execute(item);
    }

    private void OnWorkspaceExitClick(object sender, RoutedEventArgs e) => _vm.ExitWorkspace();

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

    private void OnCaptureModeClick(object sender, RoutedEventArgs e)
    {
        _captureMode = !_captureMode;
        foreach (var m in _vm.Mirrors)
            m.View.SetStatsVisible(!_captureMode);
    }

    private readonly System.Windows.Threading.DispatcherTimer _fsHideTimer = new()
        { Interval = TimeSpan.FromSeconds(2.5) };

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
            VideoFrame.Margin = new Thickness(12);
            VideoFrame.CornerRadius = new CornerRadius(6);
            VideoFrame.BorderThickness = new Thickness(1);
            TitleBarRow.Height = GridLength.Auto;
            ToolbarRow.Height = GridLength.Auto;
            StatusBarRow.Height = GridLength.Auto;
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
            var index = e.Key <= Key.D9 ? e.Key - Key.D1 : e.Key - Key.NumPad1;
            // Ctrl+Maj+N : espace de travail · Ctrl+N : tuile miroir (inchangé).
            if (mods.HasFlag(ModifierKeys.Shift))
                _vm.ActivateWorkspaceAt(index);
            else
                _vm.ActivateAt(index);
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

        if (view.HandleKey(e.Key, true, e.IsRepeat))
            e.Handled = true;
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        var view = _vm.ActiveMirror?.View;
        if (view == null || IsTextInputTarget(e.OriginalSource))
            return;
        if (view.HandleKey(e.Key, false, e.IsRepeat))
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