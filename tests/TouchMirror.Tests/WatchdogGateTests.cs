using TouchMirror.Services;
using Xunit;

namespace TouchMirror.Tests;

public sealed class WatchdogGateTests
{
    [Fact]
    public void Suppression_Active_Inside_Window()
        => Assert.True(WatchdogGate.IsSuppressed(10_000, 20_000));

    [Fact]
    public void Suppression_Expires()
        => Assert.False(WatchdogGate.IsSuppressed(20_000, 20_000));

    [Fact]
    public void Silence_Under_Threshold_Never_Kills()
    {
        Assert.False(WatchdogGate.SilenceExceeded(1000, 1000 + WatchdogGate.SilenceThresholdMs - 1));
        Assert.False(WatchdogGate.SilenceExceeded(1000, 1000 + WatchdogGate.SilenceThresholdMs));
    }

    [Fact]
    public void Silence_Over_Threshold_Kills()
        => Assert.True(WatchdogGate.SilenceExceeded(1000, 1000 + WatchdogGate.SilenceThresholdMs + 1));

    [Fact]
    public void No_Silence_Never_Kills()
        => Assert.False(WatchdogGate.SilenceExceeded(0, long.MaxValue));
}
