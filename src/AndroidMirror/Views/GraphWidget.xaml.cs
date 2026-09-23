using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TouchMirror.Views;

public sealed record OverlayLine(string Text, string? Kind, string? Color,
    bool? Done, string? Right, double? Value, string? Img = null);

public partial class GraphWidget : UserControl
{
    private const int Cap = 76;
    private const double PlotW = 152, PlotH = 40;
    private readonly List<double> _vals = new();
    private List<OverlayLine> _all = new();
    private bool _dragging;
    private bool _collapsed;
    private bool _compact;
    private bool _searchOpen;
    private Point _grab;

    private static readonly SolidColorBrush TxMain =
        new(Color.FromRgb(0xE8, 0xE9, 0xEC));
    private static readonly SolidColorBrush TxDim =
        new(Color.FromRgb(0x8A, 0x91, 0x9B));
    private static readonly SolidColorBrush Sage =
        new(Color.FromRgb(0x4E, 0xC9, 0x8E));
    private static readonly SolidColorBrush OnSage =
        new(Color.FromRgb(0x0B, 0x0E, 0x11));
    private static readonly SolidColorBrush HoverBg =
        new(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush TrackBg =
        new(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush ChipBg =
        new(Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush ChipTx =
        new(Color.FromRgb(0xAE, 0xB5, 0xBF));
    private static readonly SolidColorBrush RuleBg =
        new(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private static readonly Dictionary<string, ImageSource> ImgCache = new();
    private Brush _accent = TxMain;

    internal static bool ImgUrlAllowed(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && uri.Scheme is "http" or "https";

    private static ImageSource? ImgSource(string url)
    {
        if (ImgCache.TryGetValue(url, out var s)) return s;
        try
        {
            if (!ImgUrlAllowed(url))
                return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(url);
            bi.DecodePixelWidth = 40;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            if (ImgCache.Count > 512)
                ImgCache.Clear();
            ImgCache[url] = bi;
            return bi;
        }
        catch { return null; }
    }

    public Canvas? Host { get; set; }
    public event Action? DragBegan;
    public event Action? DragEnded;
    public bool Dragged { get; private set; }
    public bool On { get; set; } = true;
    public event Action<int>? LineClicked;

    private bool _moved;
    private (double X, double Y)? _pendingPos;

    public void RestorePosition(double rx, double ry)
    {
        Dragged = true;
        _pendingPos = (Math.Clamp(rx, 0, 1), Math.Clamp(ry, 0, 1));
        if (!TryApplyPending())
            ArmPending();
    }

    private void ArmPending()
    {
        if (Host != null) Host.SizeChanged += OnPendingSize;
        SizeChanged += OnPendingSize;
    }

    private void OnPendingSize(object? sender, SizeChangedEventArgs e)
    {
        if (!TryApplyPending())
            return;
        if (Host != null) Host.SizeChanged -= OnPendingSize;
        SizeChanged -= OnPendingSize;
    }

    private bool TryApplyPending()
    {
        if (_pendingPos is not { } p || Host == null
            || Host.ActualWidth <= 0 || ActualWidth <= 0)
            return false;
        _pendingPos = null;
        ClearValue(Canvas.RightProperty);
        ClearValue(Canvas.BottomProperty);
        Canvas.SetLeft(this, Math.Clamp(p.X * (Host.ActualWidth - ActualWidth), 0,
            Math.Max(0, Host.ActualWidth - ActualWidth)));
        Canvas.SetTop(this, Math.Clamp(p.Y * (Host.ActualHeight - ActualHeight), 0,
            Math.Max(0, Host.ActualHeight - ActualHeight)));
        return true;
    }

    public GraphWidget()
    {
        InitializeComponent();
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        CollapseBtn.MouseLeftButtonDown += (s, e) =>
        {
            _collapsed = !_collapsed;
            ApplyCollapse();
            e.Handled = true;
        };
        CollapseBtn.MouseEnter += (s, e) => CollapseBtn.Background = HoverBg;
        CollapseBtn.MouseLeave += (s, e) => CollapseBtn.Background = null;
        SearchBtn.MouseLeftButtonDown += (s, e) =>
        {
            _searchOpen = !_searchOpen;
            SearchRow.Visibility = _searchOpen ? Visibility.Visible : Visibility.Collapsed;
            if (_searchOpen) SearchBox.Focus();
            else { SearchBox.Text = ""; RenderLines(); }
            e.Handled = true;
        };
        SearchBtn.MouseEnter += (s, e) => SearchBtn.Background = HoverBg;
        SearchBtn.MouseLeave += (s, e) => SearchBtn.Background = null;
        SearchRow.MouseLeftButtonDown += (s, e) => e.Handled = true;
        SearchBox.TextChanged += (s, e) => RenderLines();
        SearchBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _searchOpen = false;
                SearchRow.Visibility = Visibility.Collapsed;
                SearchBox.Text = "";
                e.Handled = true;
            }
        };
    }

    internal static string Norm(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (var c in d)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private void ApplyCollapse()
    {
        LinesPanel.Visibility = !_collapsed && LinesPanel.Children.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
        Plot.Visibility = !_collapsed && !_compact
            ? Visibility.Visible : Visibility.Collapsed;
        CollapseGlyph.Text = _collapsed ? "+" : "−";
        if (_collapsed && _searchOpen)
        {
            _searchOpen = false;
            SearchRow.Visibility = Visibility.Collapsed;
            SearchBox.Text = "";
        }
    }

    public void Configure(string? title, string? colorHex, bool? compact)
    {
        if (!string.IsNullOrWhiteSpace(title))
            TitleText.Text = title;
        if (compact is bool c)
        {
            _compact = c;
            ApplyCollapse();
            Width = c ? double.NaN : 172;
        }
        if (!string.IsNullOrWhiteSpace(colorHex))
        {
            try
            {
                _accent = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(colorHex));
                Line.Stroke = _accent;
            }
            catch (FormatException) { }
        }
    }

    public void Push(double v, string? label)
    {
        if (double.IsFinite(v))
        {
            _vals.Add(v);
            if (_vals.Count > Cap)
                _vals.RemoveAt(0);
            Redraw();
        }
        ValueText.Text = label ?? (double.IsFinite(v) ? v.ToString("0.#") : "");
    }

    public void Clear()
    {
        _vals.Clear();
        Line.Points.Clear();
    }

    public void SetLines(IReadOnlyList<OverlayLine> lines)
    {
        _all = new List<OverlayLine>(lines);
        RenderLines();
    }

    private void RenderLines()
    {
        LinesPanel.Children.Clear();
        var q = _searchOpen ? Norm(SearchBox.Text.Trim()) : "";
        var shown = 0;
        for (var i = 0; i < _all.Count; i++)
        {
            var ln = _all[i];
            if (q.Length > 0 && !Norm(ln.Text).Contains(q)) continue;
            var idx = i;
            var host = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 1.5, 4, 1.5),
                Cursor = Cursors.Hand
            };
            host.MouseEnter += (s, e) => host.Background = HoverBg;
            host.MouseLeave += (s, e) => host.Background = null;
            host.MouseLeftButtonDown += (s, e) => { LineClicked?.Invoke(idx); e.Handled = true; };
            host.Child = BuildLine(ln, shown == 0);
            LinesPanel.Children.Add(host);
            shown++;
        }
        if (shown == 0 && q.Length > 0)
            LinesPanel.Children.Add(new TextBlock
            {
                Text = "aucun résultat", Foreground = TxDim, FontSize = 10,
                Margin = new Thickness(5, 3, 0, 2)
            });
        LinesPanel.Visibility = !_collapsed && LinesPanel.Children.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement BuildLine(OverlayLine ln, bool first)
    {
        if (ln.Kind == "bar") return BarLine(ln);
        FrameworkElement content = ln.Done is bool done
            ? CheckLine(ln.Text, done, ln.Img)
            : TextLine(ln, first);
        if (!string.IsNullOrEmpty(ln.Right))
        {
            var dp = new DockPanel();
            var rt = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = ChipBg,
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                { Text = ln.Right, Foreground = ChipTx, FontSize = 9.5 }
            };
            DockPanel.SetDock(rt, Dock.Right);
            dp.Children.Add(rt);
            dp.Children.Add(content);
            content = dp;
        }
        if (ln.Kind != "h") return content;
        var wrap = new StackPanel();
        wrap.Children.Add(content);
        wrap.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Height = 1, Fill = RuleBg,
            Margin = new Thickness(0, 4, 0, 1)
        });
        return wrap;
    }

    private FrameworkElement BarLine(OverlayLine ln)
    {
        var sp = new StackPanel { Margin = new Thickness(1, 4, 0, 2) };
        if (!string.IsNullOrEmpty(ln.Text))
            sp.Children.Add(new TextBlock
            { Text = ln.Text, Foreground = TxDim, FontSize = 10 });
        var g = new Grid
        {
            Height = 4, Width = 150,
            Margin = new Thickness(0, string.IsNullOrEmpty(ln.Text) ? 0 : 3, 0, 0)
        };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = TrackBg });
        var v = Math.Clamp(ln.Value ?? 0, 0, 1);
        g.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(2), Background = _accent,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(2, 150 * v)
        });
        sp.Children.Add(g);
        return sp;
    }

    private FrameworkElement CheckLine(string text, bool done, string? img)
    {
        FrameworkElement mark;
        if (!string.IsNullOrEmpty(img) && ImgSource(img) is ImageSource src)
        {
            var g = new Grid
            {
                Width = 22, Height = 22,
                Margin = new Thickness(0, 1, 7, 1)
            };
            g.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = ChipBg,
                Child = new TextBlock
                {
                    Text = text.Length > 0 ? text[..1] : "",
                    Foreground = TxDim, FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            var pic = new Image
            {
                Source = src, Stretch = Stretch.UniformToFill,
                Opacity = done ? 0.45 : 1
            };
            pic.Clip = new RectangleGeometry(new Rect(0, 0, 22, 22), 5, 5);
            g.Children.Add(pic);
            if (done)
                g.Children.Add(new Border
                {
                    Width = 10, Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = Sage,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, -2, -2),
                    Child = new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse("M2,4.2 L4,6.2 L8,1.8"),
                        Stroke = OnSage, StrokeThickness = 1.3,
                        Stretch = Stretch.None,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
            mark = g;
        }
        else
        {
            var box = new Border
            {
                Width = 11, Height = 11,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = done ? Sage : TxDim,
                Background = done ? Sage : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0, 7, 0)
            };
            if (done)
                box.Child = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M2.6,5.8 L4.8,8 L8.6,2.6"),
                    Stroke = OnSage, StrokeThickness = 1.4,
                    Stretch = Stretch.None,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            mark = box;
        }
        var tb = new TextBlock
        {
            Text = text, FontSize = 11,
            Foreground = done ? TxDim : TxMain,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (done) tb.TextDecorations = TextDecorations.Strikethrough;
        var dp = new DockPanel();
        DockPanel.SetDock(mark, Dock.Left);
        dp.Children.Add(mark);
        dp.Children.Add(tb);
        return dp;
    }

    private TextBlock TextLine(OverlayLine ln, bool first)
    {
        var tb = new TextBlock { Text = ln.Text, FontSize = 11 };
        switch (ln.Kind)
        {
            case "h":
                tb.Foreground = _accent;
                tb.FontWeight = FontWeights.SemiBold;
                if (!first) tb.Margin = new Thickness(0, 5, 0, 0);
                break;
            case "dim":
                tb.Foreground = TxDim;
                tb.FontSize = 10;
                break;
            case "nav":
                tb.Foreground = _accent;
                tb.Opacity = 0.85;
                tb.FontSize = 10.5;
                break;
            default:
                tb.Foreground = TxMain;
                break;
        }
        if (!string.IsNullOrEmpty(ln.Color))
        {
            try { tb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ln.Color)); }
            catch (FormatException) { }
        }
        return tb;
    }

    private void Redraw()
    {
        var n = _vals.Count;
        if (n == 0)
        {
            Line.Points.Clear();
            return;
        }
        double min = _vals.Min(), max = _vals.Max();
        var span = Math.Max(1e-9, max - min);
        var pts = new PointCollection(n);
        for (var i = 0; i < n; i++)
        {
            var x = i * (PlotW - 1) / (Cap - 1);
            var y = (PlotH - 3) - (_vals[i] - min) / span * (PlotH - 6);
            pts.Add(new Point(x, y));
        }
        Line.Points = pts;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        DragBegan?.Invoke();
        _dragging = true;
        _moved = false;
        _grab = e.GetPosition(this);
        if (Host != null)
        {
            if (ReadLocalValue(Canvas.LeftProperty) == DependencyProperty.UnsetValue)
            {
                var r = ReadLocalValue(Canvas.RightProperty) is double rd ? rd : 0;
                Canvas.SetLeft(this, Host.ActualWidth - ActualWidth - r);
                ClearValue(Canvas.RightProperty);
            }
            if (ReadLocalValue(Canvas.TopProperty) == DependencyProperty.UnsetValue)
            {
                var b = ReadLocalValue(Canvas.BottomProperty) is double bd ? bd : 0;
                Canvas.SetTop(this, Host.ActualHeight - ActualHeight - b);
                ClearValue(Canvas.BottomProperty);
            }
        }
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed || Host == null)
            return;
        Dragged = true;
        _moved = true;
        var p = e.GetPosition(Host);
        Canvas.SetLeft(this, Math.Clamp(p.X - _grab.X, 0,
            Math.Max(0, Host.ActualWidth - ActualWidth)));
        Canvas.SetTop(this, Math.Clamp(p.Y - _grab.Y, 0,
            Math.Max(0, Host.ActualHeight - ActualHeight)));
        e.Handled = true;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
        if (_moved)
            DragEnded?.Invoke();
        e.Handled = true;
    }
}
