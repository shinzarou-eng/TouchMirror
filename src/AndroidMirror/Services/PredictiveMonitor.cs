using System.IO;
using LibreHardwareMonitor.Hardware;
using Vortice.DXGI;

namespace TouchMirror.Services;

public sealed class PredictiveMonitor : IDisposable
{
    public sealed record WifiProbe(string DeviceName, string Ip);

    private const int IntervalMs = 20_000;
    private const double VramWarnRatio = 0.85;
    private const double DiskWarnFreeGB = 3;
    private const float GpuTempWarnC = 83;
    private const int WifiLossWarnPct = 34;
    private const double WifiAvgWarnMs = 80;
    private const int WifiSignalWarn = 35;

    private Func<int>? _mirrorCount;
    private Func<IReadOnlyList<WifiProbe>>? _wifiProbes;
    private CancellationTokenSource? _cts;
    private Computer? _hw;
    private bool _hwBroken;
    private readonly HashSet<string> _degradedIps = new();
    private IReadOnlyList<string> _warnings = Array.Empty<string>();

    public event Action? Changed;
    public event Action<WifiProbe>? WifiDegraded;
    public IReadOnlyList<string> Warnings => _warnings;

    public void Start(Func<int> mirrorCount, Func<IReadOnlyList<WifiProbe>> wifiProbes)
    {
        Stop();
        _mirrorCount = mirrorCount;
        _wifiProbes = wifiProbes;
        _degradedIps.Clear();
        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        SetWarnings(Array.Empty<string>());
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var w = new List<string>();
            try
            {
                CheckVram(w);
                CheckDisk(w);
                CheckGpuTemp(w);
                await CheckWifiAsync(w, ct);
            }
            catch { }
            SetWarnings(w);
            try
            {
                await Task.Delay(IntervalMs, ct);
            }
            catch { break; }
        }
    }

    private void SetWarnings(IReadOnlyList<string> w)
    {
        if (w.SequenceEqual(_warnings))
            return;
        _warnings = w;
        Changed?.Invoke();
    }

    private void CheckVram(List<string> w)
    {
        var ratio = VramRatio();
        if (ratio >= VramWarnRatio)
            w.Add(string.Format(L("health.vram"), _mirrorCount?.Invoke() ?? 1));
    }

    private static double VramRatio()
    {
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory4>();
            if (factory.EnumAdapters1(0, out var adapter).Failure || adapter == null)
                return -1;
            using (adapter)
            {
                using var a3 = adapter.QueryInterfaceOrNull<IDXGIAdapter3>();
                if (a3 == null)
                    return -1;
                var info = a3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
                return info.Budget > 0 ? (double)info.CurrentUsage / info.Budget : -1;
            }
        }
        catch { return -1; }
    }

    private void CheckDisk(List<string> w)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "TouchMirror");
            var drive = new DriveInfo(Path.GetPathRoot(dir)!);
            var freeGb = drive.AvailableFreeSpace / 1073741824.0;
            if (freeGb < DiskWarnFreeGB)
                w.Add(string.Format(L("health.disk"), freeGb.ToString("0.#")));
        }
        catch { }
    }

    private void CheckGpuTemp(List<string> w)
    {
        if (_hwBroken)
            return;
        try
        {
            if (_hw == null)
            {
                _hw = new Computer { IsGpuEnabled = true };
                _hw.Open();
            }
            float max = 0;
            foreach (var hw in _hw.Hardware)
            {
                hw.Update();
                foreach (var s in hw.Sensors)
                    if (s.SensorType == SensorType.Temperature && s.Value is float v && v > max)
                        max = v;
            }
            if (max >= GpuTempWarnC)
                w.Add(string.Format(L("health.gpu"), (int)Math.Round(max)));
        }
        catch { _hwBroken = true; }
    }

    private async Task CheckWifiAsync(List<string> w, CancellationToken ct)
    {
        var probes = _wifiProbes?.Invoke() ?? Array.Empty<WifiProbe>();
        var warned = false;
        foreach (var p in probes)
        {
            ct.ThrowIfCancellationRequested();
            var r = await WifiDiag.PingAsync(p.Ip, 3, ct);
            if (r is not { } t)
                continue;
            var lossPct = t.Sent > 0 ? t.Lost * 100.0 / t.Sent : 0;
            if (lossPct >= WifiLossWarnPct || (t.Avg > 0 && t.Avg >= WifiAvgWarnMs))
            {
                w.Add(string.Format(L("health.wifi"), p.DeviceName));
                warned = true;
                if (_degradedIps.Add(p.Ip))
                    WifiDegraded?.Invoke(p);
            }
            else
                _degradedIps.Remove(p.Ip);
        }
        if (!warned && probes.Count > 0 && WifiDiag.QueryPcWifi() is { Connected: true } pc
            && pc.Signal < WifiSignalWarn)
            w.Add(string.Format(L("health.wifi_pc"), pc.Signal));
    }

    public void Dispose()
    {
        Stop();
        try { _hw?.Close(); } catch { }
    }

    private static string L(string key) => LocalizationService.Get(key);
}
