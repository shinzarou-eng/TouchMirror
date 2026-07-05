using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TouchMirror.Scrcpy;
using TouchMirror.Video;
using TouchMirror.ViewModels;

namespace TouchMirror.Views;

public partial class MirrorView : UserControl
{
    private VideoDecoder? _decoder;
    private WriteableBitmap? _bitmap;
    private ControlChannel? _control;

    private int _videoW, _videoH;
    private int _displayRotation;
    private uint _pressedButtons;
    private bool _mouseCaptured;

    private int _frameCounter;
    private readonly Stopwatch _fpsWatch = Stopwatch.StartNew();
    private double _fps;

    public int VideoWidth => _videoW;
    public int VideoHeight => _videoH;

    public event Action<int, int>? VideoSizeChanged;

    public event Action<MirrorView>? Activated;

    public MirrorView()
    {
        InitializeComponent();
        CompositionTarget.Rendering += OnRendering;
        Focusable = true;
    }

    public void AttachDecoder(VideoDecoder decoder) => _decoder = decoder;

    public void AttachControl(ControlChannel control) => _control = control;

    private bool _statsVisible = true;

    public void OnVideoSize(int w, int h)
    {
        _videoW = w;
        _videoH = h;
        _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        VideoImage.Source = _bitmap;
        StatsBadge.Visibility = _statsVisible ? Visibility.Visible : Visibility.Collapsed;
        VideoSizeChanged?.Invoke(w, h);
    }

    public void SetStatsVisible(bool visible)
    {
        _statsVisible = visible;
        StatsBadge.Visibility = _bitmap != null && visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_decoder == null)
            return;
        if (!_decoder.TryTakeLatest(out var buffer, out var w, out var h) || buffer == null)
            return;
        try
        {
            if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
                OnVideoSize(w, h);

            _bitmap!.Lock();
            unsafe
            {
                fixed (byte* src = buffer)
                {
                    var dst = (byte*)_bitmap.BackBuffer;
                    var srcStride = w * 4;
                    if (_bitmap.BackBufferStride == srcStride)
                    {
                        Buffer.MemoryCopy(src, dst, (long)srcStride * h, (long)srcStride * h);
                    }
                    else
                    {
                        for (var row = 0; row < h; row++)
                            Buffer.MemoryCopy(src + row * srcStride,
                                dst + row * _bitmap.BackBufferStride,
                                _bitmap.BackBufferStride, srcStride);
                    }
                }
            }
            _bitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
            _bitmap.Unlock();

            _frameCounter++;
            if (_fpsWatch.ElapsedMilliseconds >= 1000)
            {
                _fps = _frameCounter * 1000.0 / _fpsWatch.ElapsedMilliseconds;
                _frameCounter = 0;
                _fpsWatch.Restart();
                StatsText.Text = $"{w}×{h}  {_fps:F0} fps";
            }
        }
        finally
        {
            _decoder.Release(buffer);
        }
    }

    public void CycleDisplayRotation()
    {
        _displayRotation = (_displayRotation + 90) % 360;
        VideoImage.LayoutTransform = _displayRotation == 0
            ? Transform.Identity
            : new RotateTransform(_displayRotation);
    }

    private void Unrotate(double rx, double ry, out uint x, out uint y)
    {
        double vx, vy;
        switch (_displayRotation)
        {
            case 90:  vx = ry; vy = _videoH - 1 - rx; break;
            case 180: vx = _videoW - 1 - rx; vy = _videoH - 1 - ry; break;
            case 270: vx = _videoW - 1 - ry; vy = rx; break;
            default:  vx = rx; vy = ry; break;
        }
        x = (uint)Math.Clamp(vx, 0, _videoW - 1);
        y = (uint)Math.Clamp(vy, 0, _videoH - 1);
    }

    private bool TryMapPoint(Point p, out uint x, out uint y, bool strict = false)
    {
        x = y = 0;
        if (_videoW <= 0 || _videoH <= 0)
            return false;
        var cw = InputSurface.ActualWidth;
        var ch = InputSurface.ActualHeight;
        if (cw <= 0 || ch <= 0)
            return false;

        var vw = _displayRotation is 90 or 270 ? _videoH : _videoW;
        var vh = _displayRotation is 90 or 270 ? _videoW : _videoH;

        var scale = Math.Min(cw / vw, ch / vh);
        var drawW = vw * scale;
        var drawH = vh * scale;
        var ox = (cw - drawW) / 2;
        var oy = (ch - drawH) / 2;

        var rx = (p.X - ox) / scale;
        var ry = (p.Y - oy) / scale;
        if (strict && (rx < 0 || ry < 0 || rx >= vw || ry >= vh))
            return false;

        rx = Math.Clamp(rx, 0, vw - 1);
        ry = Math.Clamp(ry, 0, vh - 1);
        Unrotate(rx, ry, out x, out y);
        return true;
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

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var wasInactive = DataContext is MirrorInstance { IsActive: false };
        Activated?.Invoke(this);
        InputSurface.Focus();
        Keyboard.Focus(InputSurface);

        if (wasInactive)
        {
            e.Handled = true;
            return;
        }



        if (e.ChangedButton == MouseButton.Right)
        {
            _control?.InjectKeyPress(AndroidKeyCode.Back);
            return;
        }
        if (e.ChangedButton == MouseButton.Middle)
        {
            _control?.InjectKeyPress(AndroidKeyCode.Home);
            return;
        }

        if (_control == null || !TryMapPoint(e.GetPosition(InputSurface), out var x, out var y, strict: true))
            return;

        var flag = ButtonFlag(e.ChangedButton);
        _pressedButtons |= flag;
        _control.InjectTouch(AndroidMotionEvent.ActionDown, AndroidMotionEvent.PointerIdMouse,
            x, y, (ushort)_videoW, (ushort)_videoH, 1f, flag, _pressedButtons);
        InputSurface.CaptureMouse();
        _mouseCaptured = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_control == null || _pressedButtons == 0)
            return;
        if (!TryMapPoint(e.GetPosition(InputSurface), out var x, out var y))
            return;
        _control.InjectTouch(AndroidMotionEvent.ActionMove, AndroidMotionEvent.PointerIdMouse,
            x, y, (ushort)_videoW, (ushort)_videoH, 1f, 0, _pressedButtons);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var flag = ButtonFlag(e.ChangedButton);
        if (_control != null && (_pressedButtons & flag) != 0
            && TryMapPoint(e.GetPosition(InputSurface), out var x, out var y))
        {
            _pressedButtons &= ~flag;
            _control.InjectTouch(AndroidMotionEvent.ActionUp, AndroidMotionEvent.PointerIdMouse,
                x, y, (ushort)_videoW, (ushort)_videoH, _pressedButtons != 0 ? 1f : 0f, flag, _pressedButtons);
        }
        if (_pressedButtons == 0 && _mouseCaptured)
        {
            InputSurface.ReleaseMouseCapture();
            _mouseCaptured = false;
        }
    }

    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (_pressedButtons != 0 && _control != null)
        {
            _control.InjectTouch(AndroidMotionEvent.ActionUp, AndroidMotionEvent.PointerIdMouse,
                0, 0, (ushort)_videoW, (ushort)_videoH, 0f, 0, 0);
            _pressedButtons = 0;
        }
        _mouseCaptured = false;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_control == null)
            return;

        if (Keyboard.Modifiers == ModifierKeys.Control && _videoW > 0)
        {
            _control.Pinch((uint)(_videoW / 2), (uint)(_videoH / 2),
                (ushort)_videoW, (ushort)_videoH, e.Delta > 0);
            return;
        }

        if (!TryMapPoint(e.GetPosition(InputSurface), out var x, out var y))
            return;
        var vscroll = e.Delta / 120f;
        _control.InjectScroll(x, y, (ushort)_videoW, (ushort)_videoH, 0, vscroll, _pressedButtons);
    }

    public void SaveScreenshot(string path)
    {
        if (_bitmap == null)
            return;
        _bitmap.Lock();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(_bitmap));
        _bitmap.Unlock();
        using var fs = System.IO.File.Create(path);
        encoder.Save(fs);
    }

    public bool HandleKey(Key key, bool isDown)
    {
        if (_control == null)
            return false;
        var code = MapKey(key);
        if (code < 0)
            return false;
        _control.InjectKey(isDown ? AndroidKeyEvent.ActionDown : AndroidKeyEvent.ActionUp, code);
        return true;
    }

    public void InjectText(string text) => _control?.InjectText(text);

    public void PasteToDevice(string text) => _control?.SetClipboard(text, true);

    private static int MapKey(Key key) => key switch
    {
        Key.Back => AndroidKeyCode.Back,
        Key.Enter => AndroidKeyCode.Enter,
        Key.Escape => AndroidKeyCode.Escape,
        Key.Delete => AndroidKeyCode.ForwardDel,
        Key.Tab => AndroidKeyCode.Tab,
        Key.Space => AndroidKeyCode.Space,
        Key.Up => AndroidKeyCode.DpadUp,
        Key.Down => AndroidKeyCode.DpadDown,
        Key.Left => AndroidKeyCode.DpadLeft,
        Key.Right => AndroidKeyCode.DpadRight,
        Key.Home => AndroidKeyCode.MoveHome,
        Key.End => AndroidKeyCode.MoveEnd,
        Key.PageUp => 92,
        Key.PageDown => 93,
        Key.VolumeUp => AndroidKeyCode.VolumeUp,
        Key.VolumeDown => AndroidKeyCode.VolumeDown,
        _ => KeyToAndroidCode(key)
    };

    private static int KeyToAndroidCode(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
            return AndroidKeyCode.A + (key - Key.A);
        if (key >= Key.D0 && key <= Key.D9)
            return AndroidKeyCode.Num0 + (key - Key.D0);
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return 144 + (key - Key.NumPad0);
        if (key >= Key.F1 && key <= Key.F12)
            return 131 + (key - Key.F1);
        return -1;
    }

    public void Detach()
    {
        _decoder = null;
        _control = null;
        _bitmap = null;
        VideoImage.Source = null;
        StatsBadge.Visibility = Visibility.Collapsed;
    }
}