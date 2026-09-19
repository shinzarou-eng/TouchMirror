using System;
using System.Diagnostics;

namespace TouchMirror.QuickTime;

internal sealed class CMClock
{
    public const uint NanoSecondScale = 1_000_000_000;

    public ulong Id { get; }
    public uint TimeScale { get; }

    private readonly double _factor;
    private readonly long _startTicks;

    private CMClock(ulong id, uint timeScale)
    {
        Id = id;
        TimeScale = timeScale;
        _factor = (double)timeScale / NanoSecondScale;
        _startTicks = Stopwatch.GetTimestamp();
    }

    public static CMClock WithHostTime(ulong id) => new(id, NanoSecondScale);
    public static CMClock WithHostTimeAndScale(ulong id, uint timeScale) => new(id, timeScale);

    public CMTime GetTime()
    {
        long nanos = (Stopwatch.GetTimestamp() - _startTicks) * 1_000_000_000L / Stopwatch.Frequency;
        return new CMTime
        {
            Value = TimeScale == NanoSecondScale ? (ulong)nanos : (ulong)(_factor * nanos),
            Scale = TimeScale,
            Flags = CMTime.FlagsHasBeenRounded,
            Epoch = 0,
        };
    }

    public static double CalculateSkew(CMTime start1, CMTime end1, CMTime start2, CMTime end2)
    {
        ulong diff1 = end1.Value - start1.Value;
        ulong diff2 = end2.Value - start2.Value;
        var scaled = new CMTime { Value = diff1, Scale = start1.Scale }.GetTimeForScale(start2);
        return start2.Scale * scaled / diff2;
    }
}
