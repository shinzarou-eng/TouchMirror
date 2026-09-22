using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace TouchMirror.Services;

public static class DeviceThumbs
{
    private static readonly ConcurrentDictionary<string, BitmapImage> Thumbs = new();
    private static readonly ConcurrentDictionary<string, byte> Inflight = new();
    private static readonly List<Image> Views = new();
    private static CancellationTokenSource? _cts;

    public static Func<AdbDevice, bool>? IsBusy { get; set; }

    public static readonly DependencyProperty DeviceProperty = DependencyProperty.RegisterAttached(
        "Device", typeof(AdbDevice), typeof(DeviceThumbs),
        new FrameworkPropertyMetadata(null, OnDeviceChanged));

    public static void SetDevice(DependencyObject d, AdbDevice? v) => d.SetValue(DeviceProperty, v);
    public static AdbDevice? GetDevice(DependencyObject d) => (AdbDevice?)d.GetValue(DeviceProperty);

    public static readonly DependencyProperty HasThumbProperty = DependencyProperty.RegisterAttached(
        "HasThumb", typeof(bool), typeof(DeviceThumbs), new FrameworkPropertyMetadata(false));

    public static void SetHasThumb(DependencyObject d, bool v) => d.SetValue(HasThumbProperty, v);
    public static bool GetHasThumb(DependencyObject d) => (bool)d.GetValue(HasThumbProperty);

    public static readonly DependencyProperty LandscapeProperty = DependencyProperty.RegisterAttached(
        "Landscape", typeof(bool), typeof(DeviceThumbs), new FrameworkPropertyMetadata(false));

    public static void SetLandscape(DependencyObject d, bool v) => d.SetValue(LandscapeProperty, v);
    public static bool GetLandscape(DependencyObject d) => (bool)d.GetValue(LandscapeProperty);

    private static void OnDeviceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image img)
            return;
        img.Loaded -= OnImgLoaded;
        img.Unloaded -= OnImgUnloaded;
        img.Loaded += OnImgLoaded;
        img.Unloaded += OnImgUnloaded;
        lock (Views)
        {
            Views.Remove(img);
            if (e.NewValue != null && img.IsLoaded)
                Views.Add(img);
        }
        Apply(img, e.NewValue as AdbDevice);
        if (e.NewValue != null)
            EnsureLoop();
    }

    private static void OnImgLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image img && GetDevice(img) != null)
            lock (Views)
            {
                if (!Views.Contains(img))
                    Views.Add(img);
            }
    }

    private static void OnImgUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image img)
            lock (Views)
                Views.Remove(img);
    }

    private static void Apply(Image img, AdbDevice? dev)
    {
        BitmapImage? s = null;
        if (dev != null && !Thumbs.TryGetValue(dev.DeviceKey, out s))
            s = LoadCached(dev.DeviceKey);
        img.Source = s;
        SetHasThumb(img, s != null);
        SetLandscape(img, s is { PixelWidth: var w, PixelHeight: var h } && w > h);
    }

    private static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TouchMirror", "thumbs");

    private static string CachePath(string key) => Path.Combine(CacheDir,
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant() + ".png");

    private static BitmapImage? LoadCached(string key)
    {
        try
        {
            var p = CachePath(key);
            if (!File.Exists(p))
                return null;
            var bi = Decode(File.ReadAllBytes(p));
            if (bi != null)
                Thumbs[key] = bi;
            return bi;
        }
        catch { return null; }
    }

    private static void RefreshKey(string key)
    {
        lock (Views)
            foreach (var img in Views)
                if (GetDevice(img)?.DeviceKey == key)
                    Apply(img, GetDevice(img));
    }

    private static void EnsureLoop()
    {
        if (_cts != null)
            return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            var disp = Application.Current.Dispatcher;
            while (!ct.IsCancellationRequested)
            {
                List<AdbDevice> targets;
                try
                {
                    targets = await disp.InvokeAsync(() =>
                    {
                        lock (Views)
                            return Views.Select(GetDevice)
                                .Where(d => d is { IsReady: true })
                                .GroupBy(d => d!.DeviceKey)
                                .Select(g => g.First()!)
                                .ToList();
                    });
                }
                catch { break; }
                var tasks = targets.Select(async dev =>
                {
                    if (ct.IsCancellationRequested)
                        return;
                    if (IsBusy?.Invoke(dev) == true)
                        return;
                    if (!Inflight.TryAdd(dev.DeviceKey, 0))
                        return;
                    try
                    {
                        var png = await AdbService.ScreencapAsync(dev.Serial, ct);
                        var bi = png == null ? null : Decode(png);
                        if (bi != null)
                        {
                            Thumbs[dev.DeviceKey] = bi;
                            try
                            {
                                Directory.CreateDirectory(CacheDir);
                                File.WriteAllBytes(CachePath(dev.DeviceKey), png!);
                            }
                            catch { }
                            var key = dev.DeviceKey;
                            await disp.InvokeAsync(() => RefreshKey(key));
                        }
                    }
                    finally { Inflight.TryRemove(dev.DeviceKey, out _); }
                });
                await Task.WhenAll(tasks);
                await Task.Delay(4000, ct);
            }
        });
    }

    public static void ClearAll()
    {
        Thumbs.Clear();
        try
        {
            if (Directory.Exists(CacheDir))
                foreach (var f in Directory.EnumerateFiles(CacheDir, "*.png"))
                    try { File.Delete(f); } catch { }
        }
        catch { }
        var disp = Application.Current?.Dispatcher;
        if (disp is { HasShutdownFinished: false })
            disp.BeginInvoke(() =>
            {
                lock (Views)
                    foreach (var img in Views.ToArray())
                        Apply(img, GetDevice(img));
            });
    }

    public static void Shutdown() => _cts?.Cancel();

    private static BitmapImage? Decode(byte[] png)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(png);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }
}
