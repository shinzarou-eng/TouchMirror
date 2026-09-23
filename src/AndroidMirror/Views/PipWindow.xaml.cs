using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TouchMirror.ViewModels;

namespace TouchMirror.Views;

public partial class PipWindow : Window
{
    private MirrorInstance? _mirror;
    private Action? _activate;

    public PipWindow()
    {
        InitializeComponent();
    }

    public void Bind(MirrorInstance mirror, Action activate)
    {
        if (_mirror != null)
            _mirror.View.VideoSizeChanged -= OnVideoSizeChanged;
        _mirror = mirror;
        _activate = activate;
        TitleText.Text = mirror.DeviceName;
        AvatarColor.Background = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(mirror.AccountAvatarHex));
        AvatarLetter.Text = mirror.AccountInitial;
        if (mirror.AccountAvatarPath is { } avatar && System.IO.File.Exists(avatar))
        {
            AvatarBrush.ImageSource = new BitmapImage(new Uri(avatar));
            AvatarImg.Visibility = Visibility.Visible;
        }
        else
            AvatarImg.Visibility = Visibility.Collapsed;
        Rebind();
        mirror.View.VideoSizeChanged += OnVideoSizeChanged;
    }

    private void OnVideoSizeChanged(int w, int h)
    {
        _aspect = h > 0 ? (double)w / h : 0;
        Rebind();
    }

    private double _aspect;

    private void Rebind()
    {
        if (_mirror == null)
            return;
        Video.Source = _mirror.View.VideoSource;
        if (Video.Source is { } src && src.Width > 0 && src.Height > 0)
            _aspect = src.Width / src.Height;
        if (_aspect > 0)
        {
            MinWidth = 160;
            MinHeight = MinWidth / _aspect;
            Height = Width / _aspect;
        }
    }

    private bool _resizing;
    private Point _resizeStart;
    private double _resizeStartW;

    private void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        _resizing = true;
        _resizeStart = PointToScreen(e.GetPosition(this));
        _resizeStartW = Width;
        ((UIElement)sender).CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_resizing && e.LeftButton == MouseButtonState.Pressed)
        {
            var cur = PointToScreen(e.GetPosition(this));
            Width = Math.Max(MinWidth, _resizeStartW + cur.X - _resizeStart.X);
            if (_aspect > 0)
                Height = Width / _aspect;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _resizing = false;
        base.OnMouseLeftButtonUp(e);
    }

    private void OnVideoClick(object sender, MouseButtonEventArgs e) => _activate?.Invoke();

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (_mirror != null)
            _mirror.View.VideoSizeChanged -= OnVideoSizeChanged;
        base.OnClosed(e);
    }
}
