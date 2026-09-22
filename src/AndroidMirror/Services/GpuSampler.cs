using System.Diagnostics;

namespace TouchMirror.Services;

public sealed class GpuSampler : IDisposable
{
    private readonly List<PerformanceCounter> _util = new();
    private readonly List<PerformanceCounter> _mem = new();

    private GpuSampler() { }

    public static GpuSampler? TryCreate(int pid)
    {
        var s = new GpuSampler();
        try
        {
            foreach (var inst in new PerformanceCounterCategory("GPU Engine").GetInstanceNames())
                if (inst.StartsWith($"pid_{pid}_", StringComparison.Ordinal))
                    s._util.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true));
            foreach (var inst in new PerformanceCounterCategory("GPU Process Memory").GetInstanceNames())
                if (inst.Equals($"pid_{pid}", StringComparison.Ordinal)
                    || inst.StartsWith($"pid_{pid}_", StringComparison.Ordinal))
                    s._mem.Add(new PerformanceCounter("GPU Process Memory", "Local Usage", inst, true));
        }
        catch { }
        if (s._util.Count == 0 && s._mem.Count == 0)
        {
            s.Dispose();
            return null;
        }
        return s;
    }

    public double NextUtil()
    {
        double v = 0;
        foreach (var c in _util)
            try
            {
                var x = c.NextValue();
                if (!float.IsNaN(x))
                    v += x;
            }
            catch { }
        return v;
    }

    public double VramMB()
    {
        double b = 0;
        foreach (var c in _mem)
            try
            {
                var x = c.NextValue();
                if (!float.IsNaN(x))
                    b += x;
            }
            catch { }
        return b / 1048576.0;
    }

    public void Dispose()
    {
        foreach (var c in _util) c.Dispose();
        foreach (var c in _mem) c.Dispose();
    }
}
