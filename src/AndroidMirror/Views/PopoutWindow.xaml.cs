using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TouchMirror.Engine;
using TouchMirror.ViewModels;

namespace TouchMirror.Views;

public partial class PopoutWindow : Window
{
    private MirrorInstance? _mirror;
    private Action? _activate;
    private uint _pressedButtons;
    private bool _mouseCaptured;

    public PopoutWindow()
    {
        InitializeComponent();
        InputSurface.MouseEnter += (_, _) => Fade(Header, 1);
        InputSurface.MouseEnter += (_, _) => Fade(Hud, 0.92);
        InputSurface.MouseLeave += (_, _) => Fade(Header, 0);
        InputSurface.MouseLeave += (_, _) => Fade(Hud, 0);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    private static void Fade(UIElement el, double to)
        => el.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(180)));

    public void Bind(MirrorInstance mirror, Action activate)
    {
        _mirror = mirror;
        _activate = activate;
        DataContext = mirror;
        Title = $"{mirror.DeviceName} — TouchMirror";
        TitleText.Text = mirror.DeviceName;
        Video.Source = mirror.View.VideoSource;
        NoCtl.Visibility = mirror.View.ExternalControl
            ? Visibility.Collapsed : Visibility.Visible;
    }

    public void PlaceOnOtherMonitor(Window anchor)
    {
        try
        {
            var anchorHwnd = new WindowInteropHelper(anchor).Handle;
            var anchorMon = MonitorFromWindow(anchorHwnd, MonitorDefaultToNearest);
            var candidates = new List<(IntPtr H, RECT Work)>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _hdc, ref RECT _rc, IntPtr _data) =>
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(h, ref info))
                    candidates.Add((h, info.rcWork));
                return true;
            }, IntPtr.Zero);
            var target = candidates.FirstOrDefault(c => c.H != anchorMon);
            if (target.H == IntPtr.Zero && candidates.Count > 0)
                target = candidates[0];
            if (target.H == IntPtr.Zero)
                return;
            var w = target.Work;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(anchor);
            var sx = 1.0 / dpi.DpiScaleX;
            var sy = 1.0 / dpi.DpiScaleY;
            var mw = (w.Right - w.Left) * sx;
            var mh = (w.Bottom - w.Top) * sy;
            Width = Math.Max(MinWidth, mw * 0.66);
            Height = Math.Max(MinHeight, mh * 0.66);
            Left = w.Left * sx + (mw - Width) / 2;
            Top = w.Top * sy + (mh - Height) / 2;
        }
        catch { WindowStartupLocation = WindowStartupLocation.CenterOwner; }
    }

    private Views.MirrorView? V => _mirror?.View;

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _activate?.Invoke();
        InputSurface.Focus();
        if (V is not { } v || !v.ExternalControl)
            return;
        if (e.ChangedButton == MouseButton.Middle)
        {
            v.InjectExternalKey(AndroidKeyCode.Home);
            return;
        }
        if (e.ChangedButton == MouseButton.Right)
        {
            v.InjectExternalKey(AndroidKeyCode.Back);
            return;
        }
        if (!v.TryMapExternalPoint(e.GetPosition(InputSurface), InputSurface.RenderSize,
                out var x, out var y, strict: true))
            return;
        var flag = ButtonFlag(e.ChangedButton);
        _pressedButtons |= flag;
        v.InjectExternalTouch(AndroidMotionEvent.ActionDown, x, y, 1f, flag, _pressedButtons);
        InputSurface.CaptureMouse();
        _mouseCaptured = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedButtons == 0 || V is not { } v)
            return;
        if (!v.TryMapExternalPoint(e.GetPosition(InputSurface), InputSurface.RenderSize,
                out var x, out var y))
            return;
        v.InjectExternalTouch(AndroidMotionEvent.ActionMove, x, y, 1f, 0, _pressedButtons);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var flag = ButtonFlag(e.ChangedButton);
        if (V is { } v && (_pressedButtons & flag) != 0
            && v.TryMapExternalPoint(e.GetPosition(InputSurface), InputSurface.RenderSize,
                out var x, out var y))
        {
            _pressedButtons &= ~flag;
            v.InjectExternalTouch(AndroidMotionEvent.ActionUp, x, y,
                _pressedButtons != 0 ? 1f : 0f, flag, _pressedButtons);
        }
        else
            _pressedButtons &= ~flag;
        if (_pressedButtons == 0)
            ReleaseCapture();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (V is not { } v || !v.ExternalControl)
            return;
        if (!v.TryMapExternalPoint(e.GetPosition(InputSurface), InputSurface.RenderSize,
                out var x, out var y))
            return;
        v.InjectExternalScroll(x, y, e.Delta / 120f, _pressedButtons);
    }

    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (_pressedButtons != 0)
        {
            V?.InjectExternalTouch(AndroidMotionEvent.ActionUp, 0, 0, 0f, 0, 0);
            _pressedButtons = 0;
        }
        _mouseCaptured = false;
    }

    private void ReleaseCapture()
    {
        if (_mouseCaptured)
        {
            InputSurface.ReleaseMouseCapture();
            _mouseCaptured = false;
        }
    }

    private static uint ButtonFlag(MouseButton b) => b switch
    {
        MouseButton.Left => AndroidMotionEvent.ButtonPrimary,
        MouseButton.Right => AndroidMotionEvent.ButtonSecondary,
        MouseButton.Middle => AndroidMotionEvent.ButtonTertiary,
        MouseButton.XButton1 => AndroidMotionEvent.ButtonBack,
        MouseButton.XButton2 => AndroidMotionEvent.ButtonForward,
        _ => 0
    };

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor,
        ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
