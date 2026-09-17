using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TouchMirror.Views;

/// <summary>
/// Petit panneau déplaçable affiché sur un miroir : titre + valeur + courbe.
/// Créé et piloté par les plugins (tm.overlay / tm.push) — un widget par id.
/// </summary>
public partial class GraphWidget : UserControl
{
    private const int Cap = 76;                  // ~2 px par point sur 152
    private const double PlotW = 152, PlotH = 40;
    private readonly List<double> _vals = new();
    private bool _dragging;
    private Point _grab;

    /// <summary>Canvas hôte pour les bornes de déplacement — posé par MirrorView.</summary>
    public Canvas? Host { get; set; }
    /// <summary>Début de glisser — MirrorView en profite pour activer la tuile.</summary>
    public event Action? DragBegan;
    /// <summary>Vrai dès que l'utilisateur a déplacé le widget (pos n'agit plus).</summary>
    public bool Dragged { get; private set; }
    /// <summary>Le plugin demande l'affichage (indépendant du mode capture).</summary>
    public bool On { get; set; } = true;
    /// <summary>Clic sur une ligne de la liste — index de la ligne.</summary>
    public event Action<int>? LineClicked;

    public GraphWidget()
    {
        InitializeComponent();
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
    }

    public void Configure(string? title, string? colorHex, bool? compact)
    {
        if (!string.IsNullOrWhiteSpace(title))
            TitleText.Text = title;
        if (compact is bool c)
        {
            Plot.Visibility = c ? Visibility.Collapsed : Visibility.Visible;
            Width = c ? double.NaN : 172;
        }
        if (!string.IsNullOrWhiteSpace(colorHex))
        {
            try
            {
                Line.Stroke = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(colorHex));
            }
            catch (FormatException) { }
        }
    }

    /// <summary>Ajoute un point à la courbe ; <paramref name="label"/> remplace
    /// l'affichage numérique si fourni.</summary>
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

    /// <summary>Remplace la liste de lignes cliquables sous le titre
    /// (<paramref name="lines"/> vide → panneau masqué).</summary>
    public void SetLines(string[] lines)
    {
        LinesPanel.Children.Clear();
        LinesPanel.Visibility = lines.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < lines.Length; i++)
        {
            var idx = i;
            var tb = new TextBlock
            {
                Text = lines[i],
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xEA)),
                FontSize = 11,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 1, 0, 1)
            };
            tb.MouseLeftButtonDown += (s, e) => { LineClicked?.Invoke(idx); e.Handled = true; };
            LinesPanel.Children.Add(tb);
        }
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
        _grab = e.GetPosition(this);
        // Ancre de coin → coordonnées absolues Left/Top au premier glisser.
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
        e.Handled = true;
    }
}
