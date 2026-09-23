using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TouchMirror.Engine;
using TouchMirror.Services;
using TouchMirror.Video;
using TouchMirror.ViewModels;

namespace TouchMirror.Views;

public partial class MirrorView : UserControl
{
    private IFrameSource? _decoder;
    private WriteableBitmap? _bitmap;
    private D3DImage? _gpuImage;
    private GpuPresenter? _presenter;
    private ControlChannel? _control;

    private int _videoW, _videoH;
    private int _displayRotation;
    private uint _pressedButtons;
    private bool _mouseCaptured;

    private double _zoom = 1.0;
    private double _zoomCx = 0.5, _zoomCy = 0.5;
    private bool _viewPanning;
    private Point _panLast;

    private readonly DispatcherTimer _moveFlush = new() { Interval = TimeSpan.FromMilliseconds(8) };
    private int _pendingMoveX, _pendingMoveY;
    private int _lastSentMoveX = -1, _lastSentMoveY = -1;
    private bool _hasPendingMove, _iosPendingMove;

    private int _frameCounter;
    private readonly Stopwatch _fpsWatch = Stopwatch.StartNew();
    private double _fps;

    public int VideoWidth => _videoW;
    public int VideoHeight => _videoH;

    public event Action<int, int>? VideoSizeChanged;

    public event Action<MirrorView>? Activated;

    private bool _renderingHooked;

    public MirrorView()
    {
        InitializeComponent();
        Focusable = true;
        SizeChanged += (_, _) => LayoutKeybinds();
        _moveFlush.Tick += (_, _) => FlushPendingMove();
    }

    private void HookRendering()
    {
        if (_renderingHooked)
            return;
        _renderingHooked = true;
        CompositionTarget.Rendering += OnRendering;
    }

    public ImageSource? VideoSource => VideoImage.Source;

    public void AttachDecoder(IFrameSource decoder)
    {
        _decoder = decoder;
        HookRendering();
    }

    public void AttachDecoder(IFrameSource decoder, GpuPresenter? presenter)
    {
        _decoder = decoder;
        HookRendering();
        _presenter = presenter;
        if (presenter == null)
            return;
        _gpuImage = new D3DImage();
        VideoImage.Source = _gpuImage;
        var hwnd = new WindowInteropHelper(Window.GetWindow(this)
            ?? Application.Current.MainWindow).Handle;
        presenter.Attach(_gpuImage, hwnd);
        ApplyZoom();
        presenter.SizeChanged += (w, h) => Dispatcher.BeginInvoke(() =>
        {
            presenter.Rebind();
            _videoW = w;
            _videoH = h;
            SetWaitingOverlay(false);
            VideoSizeChanged?.Invoke(w, h);
            LayoutKeybinds();
        });
    }

    public void OnGpuFrame()
    {
        _presenter?.Invalidate();
        _frameCounter++;
        if (_fpsWatch.ElapsedMilliseconds >= 1000)
        {
            _fps = _frameCounter * 1000.0 / _fpsWatch.ElapsedMilliseconds;
            _frameCounter = 0;
            _fpsWatch.Restart();
        }
    }

    public void SetIosReadOnly(bool readOnly)
    {
        IosBadge.Visibility = readOnly ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetWaitingOverlay(bool waiting)
    {
        WaitingOverlay.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetWaitingHint(string? hint)
    {
        WaitingHint.Text = hint ?? "";
        WaitingHint.Visibility = string.IsNullOrEmpty(hint)
            ? Visibility.Collapsed : Visibility.Visible;
    }

    public void AttachControl(ControlChannel control) => _control = control;

    public interface IIosPointer
    {
        void MoveTo(double rx, double ry);
        void Down(double rx, double ry);
        void Up(double rx, double ry);
        void Click(double rx, double ry);
        void Wheel(double rx, double ry, int steps);
    }

    private IIosPointer? _iosPointer;
    private bool _iosMouseDown;
    private double _iosRx, _iosRy;

    public void SetIosPointer(IIosPointer? pointer)
    {
        if (_iosMouseDown)
        {
            _iosMouseDown = false;
            _iosPendingMove = false;
            _iosPointer?.Up(_iosRx, _iosRy);
        }
        _iosPointer = pointer;
    }

    public void SetIosBadgeText(string text)
    {
        if (IosBadge.Child is StackPanel sp && sp.Children.Count > 1
            && sp.Children[1] is TextBlock tb)
            tb.Text = text;
    }

    private ObservableCollection<KeybindItem>? _keybinds;
    private readonly Dictionary<KeybindItem, Border> _keybindEls = new();
    private readonly Dictionary<Key, KeybindItem> _keybindByKey = new();
    private bool _editMode;
    private int _kbStyle;
    private double _kbOpacity = 0.92;
    private double _kbSize = 30;
    private KeybindItem? _pending;
    private KeybindItem? _dragging;
    private bool _dragMoved;
    private Point _dragStart;

    public event Action? EditModeExitRequested;

    public void BindKeybinds(ObservableCollection<KeybindItem> keybinds)
    {
        _keybinds = keybinds;
        _keybinds.CollectionChanged += OnKeybindsChanged;
        RebuildKeybindVisuals();
    }

    private void OnKeybindsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            if (_pending != null && (_keybinds == null || !_keybinds.Contains(_pending)))
                _pending = null;
            if (_dragging != null && (_keybinds == null || !_keybinds.Contains(_dragging)))
                _dragging = null;
            RebuildKeybindVisuals();
            RebuildKeybindMap();
            return;
        }
        if (e.NewItems != null)
            foreach (KeybindItem k in e.NewItems)
            {
                k.PropertyChanged += OnKeybindItemChanged;
                AddKeybindVisual(k);
            }
        if (e.OldItems != null)
            foreach (KeybindItem k in e.OldItems)
            {
                k.PropertyChanged -= OnKeybindItemChanged;
                if (_keybindEls.Remove(k, out var el))
                    KeybindLayer.Children.Remove(el);
                if (_pending == k) _pending = null;
                if (_dragging == k) _dragging = null;
            }
        RebuildKeybindMap();
        LayoutKeybinds();
    }

    private void OnKeybindItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeybindItem.Key))
            RebuildKeybindMap();
    }

    private void RebuildKeybindMap()
    {
        _keybindByKey.Clear();
        if (_keybinds == null)
            return;
        foreach (var kb in _keybinds)
            if (!string.IsNullOrEmpty(kb.Key) && Enum.TryParse<Key>(kb.Key, out var k))
                _keybindByKey[k] = kb;
    }

    public void SetKeybindEditMode(bool edit)
    {
        _editMode = edit;
        KeybindLayer.IsHitTestVisible = edit;
        KeybindHint.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
        KeybindHint.IsHitTestVisible = edit;
        if (!edit)
        {
            _pending = null;
            foreach (var k in _keybindEls.Keys) k.IsEditing = false;
        }
    }

    public void SetKeybindAppearance(int style, double opacity, double size)
    {
        _kbStyle = style;
        _kbOpacity = opacity;
        _kbSize = size;
        RebuildKeybindVisuals();
    }

    private void AddKeybindVisual(KeybindItem kb)
    {
        var accent = Color.FromRgb(0xC9, 0xD1, 0xD9);
        var label = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(KeybindItem.Label)) { Source = kb });

        var el = new Border
        {
            Child = label,
            DataContext = kb,
            Cursor = Cursors.Hand,
            Opacity = _kbOpacity,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 8, ShadowDepth = 1, Opacity = 0.55
            }
        };

        var s = _kbSize;
        switch (_kbStyle)
        {
            case 1:
                el.Width = s; el.Height = s;
                el.CornerRadius = new CornerRadius(s / 2);
                el.Background = new SolidColorBrush(Color.FromArgb(0xB3, accent.R, accent.G, accent.B));
                el.BorderBrush = new SolidColorBrush(accent);
                el.BorderThickness = new Thickness(1.5);
                label.Foreground = Brushes.White;
                label.FontSize = Math.Max(9, s * 0.37);
                break;
            case 2:
                el.Background = Brushes.Transparent;
                el.Padding = new Thickness(4, 0, 4, 0);
                label.Foreground = new SolidColorBrush(accent);
                label.FontSize = Math.Max(11, s * 0.55);
                break;
            default:
                el.MinWidth = s; el.Height = s;
                el.Padding = new Thickness(s * 0.23, 0, s * 0.23, 0);
                el.CornerRadius = new CornerRadius(s * 0.23);
                el.Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x0C, 0x0E, 0x11));
                el.BorderBrush = new SolidColorBrush(Color.FromArgb(0x8C, accent.R, accent.G, accent.B));
                el.BorderThickness = new Thickness(1);
                label.Foreground = new SolidColorBrush(accent);
                label.FontSize = Math.Max(9, s * 0.37);
                break;
        }
        var style = new Style(typeof(Border));
        var trig = new DataTrigger
        {
            Binding = new Binding(nameof(KeybindItem.IsEditing)),
            Value = true
        };
        trig.Setters.Add(new Setter(Border.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(0xE0, 0xA2, 0x4C))));
        trig.Setters.Add(new Setter(Border.OpacityProperty, 1.0));
        style.Triggers.Add(trig);
        el.Style = style;
        var labelStyle = new Style(typeof(TextBlock));
        var labelTrig = new DataTrigger
        {
            Binding = new Binding(nameof(KeybindItem.IsEditing)),
            Value = true
        };
        labelTrig.Setters.Add(new Setter(TextBlock.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(0xE0, 0xA2, 0x4C))));
        labelStyle.Triggers.Add(labelTrig);
        label.Style = labelStyle;

        el.MouseLeftButtonDown += (s, e) =>
        {
            if (!_editMode) return;
            _dragging = kb;
            _dragMoved = false;
            _dragStart = e.GetPosition(this);
            el.CaptureMouse();
            e.Handled = true;
        };
        el.MouseMove += (s, e) =>
        {
            if (_dragging != kb || e.LeftButton != MouseButtonState.Pressed)
                return;
            var p = e.GetPosition(this);
            if (!_dragMoved && (Math.Abs(p.X - _dragStart.X) + Math.Abs(p.Y - _dragStart.Y)) < 4)
                return;
            _dragMoved = true;
            if (TryMapPoint(e.GetPosition(InputSurface), out var x, out var y))
            {
                kb.Rx = Math.Clamp((double)x / Math.Max(1, _videoW - 1), 0, 1);
                kb.Ry = Math.Clamp((double)y / Math.Max(1, _videoH - 1), 0, 1);
                LayoutKeybinds();
            }
        };
        el.MouseLeftButtonUp += (s, e) =>
        {
            if (_dragging != kb) return;
            el.ReleaseMouseCapture();
            _dragging = null;
            if (!_dragMoved)
            {
                foreach (var k in _keybindEls.Keys) k.IsEditing = false;
                kb.IsEditing = true;
                _pending = kb;
                Keyboard.Focus(InputSurface);
            }
            e.Handled = true;
        };
        el.MouseRightButtonDown += (s, e) =>
        {
            if (_editMode)
            {
                _keybinds?.Remove(kb);
                e.Handled = true;
            }
        };

        _keybindEls[kb] = el;
        KeybindLayer.Children.Add(el);
    }

    private void RebuildKeybindVisuals()
    {
        KeybindLayer.Children.Clear();
        _keybindEls.Clear();
        if (_keybinds == null)
            return;
        foreach (var kb in _keybinds)
        {
            kb.PropertyChanged -= OnKeybindItemChanged;
            kb.PropertyChanged += OnKeybindItemChanged;
            AddKeybindVisual(kb);
        }
        LayoutKeybinds();
    }

    private void LayoutKeybinds()
    {
        if (!GetVideoDrawRect(out var ox, out var oy, out var scale, out var vw, out var vh))
            return;
        var zx0 = (_zoomCx - 0.5 / _zoom) * vw;
        var zy0 = (_zoomCy - 0.5 / _zoom) * vh;
        var zx1 = zx0 + vw / _zoom;
        var zy1 = zy0 + vh / _zoom;
        foreach (var (kb, el) in _keybindEls)
        {
            var (dx, dy) = VideoToDisplay(kb.Rx, kb.Ry);
            el.Visibility = dx >= zx0 && dx <= zx1 && dy >= zy0 && dy <= zy1
                ? Visibility.Visible : Visibility.Collapsed;
            var p = VideoToView(kb.Rx, kb.Ry, ox, oy, scale);
            el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(el, p.X - el.DesiredSize.Width / 2);
            Canvas.SetTop(el, p.Y - el.DesiredSize.Height / 2);
        }
    }

    private (double dx, double dy) VideoToDisplay(double rx, double ry)
    {
        var vx = rx * (_videoW - 1);
        var vy = ry * (_videoH - 1);
        return _displayRotation switch
        {
            90 => (_videoH - 1 - vy, vx),
            180 => (_videoW - 1 - vx, _videoH - 1 - vy),
            270 => (vy, _videoW - 1 - vx),
            _ => (vx, vy),
        };
    }

    private Point VideoToView(double rx, double ry, double ox, double oy, double scale)
    {
        var (dx, dy) = VideoToDisplay(rx, ry);
        var fw = _displayRotation is 90 or 270 ? _videoH : _videoW;
        var fh = _displayRotation is 90 or 270 ? _videoW : _videoH;
        var zoX = _zoomCx - 0.5 / _zoom;
        var zoY = _zoomCy - 0.5 / _zoom;
        return new Point(ox + (dx - zoX * fw) * _zoom * scale,
            oy + (dy - zoY * fh) * _zoom * scale);
    }

    private bool GetVideoDrawRect(out double ox, out double oy, out double scale,
        out double vw, out double vh)
    {
        ox = oy = scale = vw = vh = 0;
        if (_videoW <= 0 || _videoH <= 0)
            return false;
        var cw = InputSurface.ActualWidth;
        var ch = InputSurface.ActualHeight;
        if (cw <= 0 || ch <= 0)
            return false;
        vw = _displayRotation is 90 or 270 ? _videoH : _videoW;
        vh = _displayRotation is 90 or 270 ? _videoW : _videoH;
        scale = Math.Min(cw / vw, ch / vh);
        ox = (cw - vw * scale) / 2;
        oy = (ch - vh * scale) / 2;
        return true;
    }

    public double ViewZoom => _zoom;

    private void ZoomAt(Point viewPt, double factor)
    {
        if (!GetVideoDrawRect(out var ox, out var oy, out var scale, out var vw, out var vh))
            return;
        var u = Math.Clamp((viewPt.X - ox) / (vw * scale), 0, 1);
        var v = Math.Clamp((viewPt.Y - oy) / (vh * scale), 0, 1);
        var tx = _zoomCx - 0.5 / _zoom + u / _zoom;
        var ty = _zoomCy - 0.5 / _zoom + v / _zoom;
        _zoom = Math.Clamp(_zoom * factor, 1.0, 8.0);
        _zoomCx = tx - u / _zoom + 0.5 / _zoom;
        _zoomCy = ty - v / _zoom + 0.5 / _zoom;
        if (_zoom == 1.0)
            _zoomCx = _zoomCy = 0.5;
        ClampZoom();
        ApplyZoom();
    }

    private void ResetZoom()
    {
        if (_zoom == 1.0)
            return;
        _zoom = 1.0;
        _zoomCx = _zoomCy = 0.5;
        ApplyZoom();
    }

    private void ClampZoom()
    {
        var half = 0.5 / _zoom;
        _zoomCx = Math.Clamp(_zoomCx, half, 1 - half);
        _zoomCy = Math.Clamp(_zoomCy, half, 1 - half);
    }

    private (double, double) DisplayToTex(double u, double v) => _displayRotation switch
    {
        90 => (v, 1 - u),
        180 => (1 - u, 1 - v),
        270 => (1 - v, u),
        _ => (u, v),
    };

    private void ApplyZoom()
    {
        var w = 1.0 / _zoom;
        var (tx0, ty0) = DisplayToTex(_zoomCx - w / 2, _zoomCy - w / 2);
        var (tx1, ty1) = DisplayToTex(_zoomCx + w / 2, _zoomCy + w / 2);
        _presenter?.SetViewRect((float)Math.Min(tx0, tx1), (float)Math.Min(ty0, ty1),
            (float)Math.Abs(tx1 - tx0));
        _presenter?.Redraw();
        LayoutKeybinds();
        ZoomBadge.Visibility = _zoom > 1.0 ? Visibility.Visible : Visibility.Collapsed;
        if (_zoom > 1.0)
            ZoomText.Text = $"×{_zoom:0.0}";
    }

    private void AddKeybindAt(uint x, uint y)
    {
        var kb = new KeybindItem
        {
            Rx = Math.Clamp((double)x / Math.Max(1, _videoW - 1), 0, 1),
            Ry = Math.Clamp((double)y / Math.Max(1, _videoH - 1), 0, 1),
            IsEditing = true
        };
        foreach (var k in _keybindEls.Keys) k.IsEditing = false;
        _pending = kb;
        _keybinds?.Add(kb);
    }

    private void TapKeybind(KeybindItem kb)
    {
        if (_videoW <= 0)
            return;
        if (_control == null)
        {
            _iosPointer?.Click(kb.Rx, kb.Ry);
            if (_keybindEls.TryGetValue(kb, out var iosEl))
                iosEl.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0.35, _kbOpacity, TimeSpan.FromMilliseconds(220)));
            return;
        }
        var x = (uint)Math.Clamp(kb.Rx * (_videoW - 1), 0, _videoW - 1);
        var y = (uint)Math.Clamp(kb.Ry * (_videoH - 1), 0, _videoH - 1);
        _control.InjectTouch(AndroidMotionEvent.ActionDown, AndroidMotionEvent.PointerIdVirtualFinger,
            x, y, (ushort)_videoW, (ushort)_videoH, 1f,
            AndroidMotionEvent.ButtonPrimary, AndroidMotionEvent.ButtonPrimary);
        _control.InjectTouch(AndroidMotionEvent.ActionUp, AndroidMotionEvent.PointerIdVirtualFinger,
            x, y, (ushort)_videoW, (ushort)_videoH, 0f,
            AndroidMotionEvent.ButtonPrimary, 0);
        if (_keybindEls.TryGetValue(kb, out var el))
            el.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0.35, _kbOpacity, TimeSpan.FromMilliseconds(220)));
    }

    private bool _statsVisible = true;

    private readonly Dictionary<string, GraphWidget> _overlays = new();

    public event Action<string, int>? OverlayLineClicked;
    public event Action<string, double, double>? OverlayMoved;
    public Func<string, (double X, double Y)?>? OverlayPositionOf { get; set; }

    public double CurrentFps => _fps;

    public void SetGraphOverlay(string id, bool? visible, string? title,
        string? colorHex, bool? compact = null, string? pos = null,
        IReadOnlyList<OverlayLine>? lines = null)
    {
        if (!_overlays.TryGetValue(id, out var w))
        {
            w = new GraphWidget { Host = OverlayLayer };
            w.DragBegan += () => Activated?.Invoke(this);
            w.LineClicked += idx => OverlayLineClicked?.Invoke(id, idx);
            w.DragEnded += () =>
            {
                if (w.Host == null)
                    return;
                var maxX = Math.Max(1, w.Host.ActualWidth - w.ActualWidth);
                var maxY = Math.Max(1, w.Host.ActualHeight - w.ActualHeight);
                var x = Canvas.GetLeft(w); if (double.IsNaN(x)) x = 0;
                var y = Canvas.GetTop(w); if (double.IsNaN(y)) y = 0;
                OverlayMoved?.Invoke(id, Math.Clamp(x / maxX, 0, 1), Math.Clamp(y / maxY, 0, 1));
            };
            AnchorOverlay(w, pos ?? "bl");
            OverlayLayer.Children.Add(w);
            if (OverlayPositionOf?.Invoke(id) is { } saved)
                w.RestorePosition(saved.X, saved.Y);
            _overlays[id] = w;
        }
        w.Configure(title, colorHex, compact);
        if (lines != null)
            w.SetLines(lines);
        if (!string.IsNullOrEmpty(pos) && !w.Dragged)
            AnchorOverlay(w, pos);
        if (visible is bool v)
            w.On = v;
        w.Visibility = w.On && _statsVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void AnchorOverlay(GraphWidget w, string pos)
    {
        w.ClearValue(Canvas.LeftProperty);
        w.ClearValue(Canvas.RightProperty);
        w.ClearValue(Canvas.TopProperty);
        w.ClearValue(Canvas.BottomProperty);
        switch (pos)
        {
            case "tl": Canvas.SetLeft(w, 12); Canvas.SetTop(w, 12); break;
            case "tr": Canvas.SetRight(w, 12); Canvas.SetTop(w, 12); break;
            case "br": Canvas.SetRight(w, 12); Canvas.SetBottom(w, 12); break;
            default:   Canvas.SetLeft(w, 12); Canvas.SetBottom(w, 12); break;
        }
    }

    public void PushGraphValue(string id, double v, string? label = null)
    {
        if (_overlays.TryGetValue(id, out var w))
            w.Push(v, label);
    }

    public void OnVideoSize(int w, int h)
    {
        _videoW = w;
        _videoH = h;
        if (_presenter == null)
        {
            _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            VideoImage.Source = _bitmap;
        }
        SetWaitingOverlay(false);
        VideoSizeChanged?.Invoke(w, h);
        LayoutKeybinds();
    }

    public void SetStatsVisible(bool visible)
    {
        _statsVisible = visible;
        foreach (var w in _overlays.Values)
            w.Visibility = w.On && visible ? Visibility.Visible : Visibility.Collapsed;
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
        LayoutKeybinds();
        ApplyZoom();
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
        if (!GetVideoDrawRect(out var ox, out var oy, out var scale, out var vw, out var vh))
            return false;

        var rx = (p.X - ox) / scale;
        var ry = (p.Y - oy) / scale;
        rx = rx / _zoom + (_zoomCx - 0.5 / _zoom) * vw;
        ry = ry / _zoom + (_zoomCy - 0.5 / _zoom) * vh;
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

        if (_editMode)
        {
            if (e.ChangedButton == MouseButton.Left
                && TryMapPoint(e.GetPosition(InputSurface), out var ex, out var ey, strict: true))
                AddKeybindAt(ex, ey);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Alt && _zoom > 1)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _viewPanning = true;
                _panLast = e.GetPosition(InputSurface);
                InputSurface.CaptureMouse();
                _mouseCaptured = true;
            }
            else if (e.ChangedButton == MouseButton.Right)
                ResetZoom();
            e.Handled = true;
            return;
        }

        if (_control == null)
        {
            if (_iosPointer != null && e.ChangedButton == MouseButton.Left
                && TryMapPoint(e.GetPosition(InputSurface), out var ix, out var iy, strict: true))
            {
                _iosRx = (double)ix / Math.Max(1, _videoW - 1);
                _iosRy = (double)iy / Math.Max(1, _videoH - 1);
                _iosPointer.Down(_iosRx, _iosRy);
                _iosMouseDown = true;
                _moveFlush.Start();
                InputSurface.CaptureMouse();
                _mouseCaptured = true;
            }
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

        if (!TryMapPoint(e.GetPosition(InputSurface), out var x, out var y, strict: true))
            return;

        var flag = ButtonFlag(e.ChangedButton);
        _pressedButtons |= flag;
        _control.InjectTouch(AndroidMotionEvent.ActionDown, AndroidMotionEvent.PointerIdMouse,
            x, y, (ushort)_videoW, (ushort)_videoH, 1f, flag, _pressedButtons);
        _lastSentMoveX = (int)x;
        _lastSentMoveY = (int)y;
        _moveFlush.Start();
        InputSurface.CaptureMouse();
        _mouseCaptured = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_viewPanning)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _viewPanning = false;
                return;
            }
            var p = e.GetPosition(InputSurface);
            if (GetVideoDrawRect(out _, out _, out var sc, out var vw, out var vh))
            {
                _zoomCx -= (p.X - _panLast.X) / (vw * sc);
                _zoomCy -= (p.Y - _panLast.Y) / (vh * sc);
                _panLast = p;
                ClampZoom();
                ApplyZoom();
            }
            return;
        }
        if (_iosMouseDown && _iosPointer != null)
        {
            if (TryMapPoint(e.GetPosition(InputSurface), out var ix, out var iy))
            {
                _iosRx = (double)ix / Math.Max(1, _videoW - 1);
                _iosRy = (double)iy / Math.Max(1, _videoH - 1);
                _iosPendingMove = true;
            }
            return;
        }
        if (_editMode || _control == null || _pressedButtons == 0)
            return;
        if (!TryMapPoint(e.GetPosition(InputSurface), out var x, out var y))
            return;
        _pendingMoveX = (int)x;
        _pendingMoveY = (int)y;
        _hasPendingMove = true;
    }

    private void FlushPendingMove()
    {
        if (_iosPendingMove && _iosMouseDown && _iosPointer != null)
        {
            _iosPendingMove = false;
            _iosPointer.MoveTo(_iosRx, _iosRy);
        }
        if (!_hasPendingMove)
            return;
        _hasPendingMove = false;
        if (_control == null || _pressedButtons == 0)
            return;
        if (_pendingMoveX == _lastSentMoveX && _pendingMoveY == _lastSentMoveY)
            return;
        _lastSentMoveX = _pendingMoveX;
        _lastSentMoveY = _pendingMoveY;
        _control.InjectTouch(AndroidMotionEvent.ActionMove, AndroidMotionEvent.PointerIdMouse,
            (uint)_pendingMoveX, (uint)_pendingMoveY,
            (ushort)_videoW, (ushort)_videoH, 1f, 0, _pressedButtons);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_viewPanning)
        {
            _viewPanning = false;
            if (_mouseCaptured)
            {
                InputSurface.ReleaseMouseCapture();
                _mouseCaptured = false;
            }
            return;
        }
        if (_iosMouseDown)
        {
            _iosMouseDown = false;
            if (TryMapPoint(e.GetPosition(InputSurface), out var ix, out var iy))
            {
                _iosRx = (double)ix / Math.Max(1, _videoW - 1);
                _iosRy = (double)iy / Math.Max(1, _videoH - 1);
            }
            _iosPointer?.Up(_iosRx, _iosRy);
            _iosPendingMove = false;
            _moveFlush.Stop();
            if (_mouseCaptured)
            {
                InputSurface.ReleaseMouseCapture();
                _mouseCaptured = false;
            }
            return;
        }
        var flag = ButtonFlag(e.ChangedButton);
        if (_control != null && (_pressedButtons & flag) != 0
            && TryMapPoint(e.GetPosition(InputSurface), out var x, out var y))
        {
            _pressedButtons &= ~flag;
            _control.InjectTouch(AndroidMotionEvent.ActionUp, AndroidMotionEvent.PointerIdMouse,
                x, y, (ushort)_videoW, (ushort)_videoH, _pressedButtons != 0 ? 1f : 0f, flag, _pressedButtons);
        }
        if (_pressedButtons == 0)
        {
            _hasPendingMove = false;
            _moveFlush.Stop();
            if (_mouseCaptured)
            {
                InputSurface.ReleaseMouseCapture();
                _mouseCaptured = false;
            }
        }
    }

    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        _viewPanning = false;
        if (_iosMouseDown)
        {
            _iosMouseDown = false;
            _iosPendingMove = false;
            _iosPointer?.Up(_iosRx, _iosRy);
        }
        if (_pressedButtons != 0 && _control != null)
        {
            _control.InjectTouch(AndroidMotionEvent.ActionUp, AndroidMotionEvent.PointerIdMouse,
                0, 0, (ushort)_videoW, (ushort)_videoH, 0f, 0, 0);
            _pressedButtons = 0;
        }
        _hasPendingMove = false;
        _moveFlush.Stop();
        _mouseCaptured = false;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Alt && _presenter != null)
        {
            ZoomAt(e.GetPosition(InputSurface), e.Delta > 0 ? 1.3 : 1.0 / 1.3);
            e.Handled = true;
            return;
        }
        if (_iosPointer != null && _control == null)
        {
            if (TryMapPoint(e.GetPosition(InputSurface), out var wx, out var wy))
                _iosPointer.Wheel((double)wx / Math.Max(1, _videoW - 1),
                    (double)wy / Math.Max(1, _videoH - 1), e.Delta / 120);
            return;
        }
        if (_editMode || _control == null)
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
        if (_presenter != null)
        {
            var px = _presenter.CaptureBgra(out var gw, out var gh);
            if (px == null)
                return;
            var gpuBmp = new WriteableBitmap(gw, gh, 96, 96, PixelFormats.Bgra32, null);
            gpuBmp.WritePixels(new Int32Rect(0, 0, gw, gh), px, gw * 4, 0);
            using (var gfs = System.IO.File.Create(path))
            {
                var gpuEnc = new PngBitmapEncoder();
                gpuEnc.Frames.Add(BitmapFrame.Create(gpuBmp));
                gpuEnc.Save(gfs);
            }
            return;
        }
        if (_bitmap == null)
            return;
        _bitmap.Lock();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(_bitmap));
        _bitmap.Unlock();
        using var fs = System.IO.File.Create(path);
        encoder.Save(fs);
    }

    public bool HandleKey(Key key, bool isDown, bool isRepeat = false)
    {
        if (_editMode)
        {
            if (!isDown)
                return true;
            if (_pending != null)
            {
                _pending.Key = key.ToString();
                _pending.IsEditing = false;
                _pending = null;
            }
            else if (key == Key.Escape)
            {
                EditModeExitRequested?.Invoke();
            }
            return true;
        }

        _keybindByKey.TryGetValue(key, out var kb);
        if (kb != null)
        {
            if (isDown && !isRepeat)
                TapKeybind(kb);
            return true;
        }

        if (_control == null)
            return false;
        var code = MapKey(key);
        if (code < 0)
            return false;
        if (isDown) _heldCodes.Add(code); else _heldCodes.Remove(code);
        _control.InjectKey(isDown ? AndroidKeyEvent.ActionDown : AndroidKeyEvent.ActionUp, code);
        return true;
    }

    private readonly HashSet<int> _heldCodes = new();

    public void ReleaseHeldKeys()
    {
        if (_heldCodes.Count == 0)
            return;
        var codes = _heldCodes.ToArray();
        _heldCodes.Clear();
        foreach (var c in codes)
            _control?.InjectKey(AndroidKeyEvent.ActionUp, c);
    }

    public void InjectText(string text) => _control?.InjectText(text);

    public void PasteToDevice(string text) => _control?.SetClipboard(text, true);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MirrorInstance mi || _control == null)
            return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            AppLogger.Forget(mi.HandleFileDropAsync(paths));
    }

    private static int MapKey(Key key) => key switch
    {
        Key.Back => AndroidKeyCode.Del,
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
        ReleaseHeldKeys();
        _decoder = null;
        _control = null;
        _pressedButtons = 0;
        _mouseCaptured = false;
        _hasPendingMove = _iosPendingMove = false;
        _lastSentMoveX = _lastSentMoveY = -1;
        InputSurface.ReleaseMouseCapture();
        _presenter = null;
        _gpuImage = null;
        _bitmap = null;
        VideoImage.Source = null;
        _moveFlush.Stop();
        _zoom = 1.0;
        _zoomCx = _zoomCy = 0.5;
        _viewPanning = false;
        if (_renderingHooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
        }
        foreach (var w in _overlays.Values)
        {
            w.Visibility = Visibility.Collapsed;
            w.Clear();
        }
    }
}
