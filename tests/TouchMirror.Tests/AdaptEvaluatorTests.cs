using System.Text.Json;
using TouchMirror.Services;
using Xunit;

namespace TouchMirror.Tests;

public sealed class AdaptEvaluatorTests
{
    private static AdaptEvaluator Eval(int start = 16_000_000, int ceiling = AdaptEvaluator.MaxBitRate)
        => new(start, ceiling);

    private static void Calm(AdaptEvaluator e, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            e.Evaluate(50, false);
    }

    [Fact]
    public void Start_Snaps_To_Tier_At_Or_Above()
    {
        Assert.Equal(16_000_000, Eval(16_000_000).Current);
        Assert.Equal(12_000_000, Eval(10_000_000).Current);
        Assert.Equal(40_000_000, Eval(60_000_000).Current);
        Assert.Equal(1_500_000, Eval(100_000).Current);
    }

    [Fact]
    public void Start_Respects_Remembered_Tier()
    {
        Assert.Equal(32_000_000, Eval(32_000_000).Current);
    }

    [Fact]
    public void Start_Capped_By_Ceiling()
    {
        var e = new AdaptEvaluator(32_000_000, 16_000_000);
        Assert.Equal(16_000_000, e.Current);
    }

    [Fact]
    public void Lag_Growth_Drops_One_Tier()
    {
        var e = Eval();
        var next = e.Evaluate(50, false);
        Assert.Null(next);
        next = e.Evaluate(120, false);
        Assert.Equal(12_000_000, next);
        Assert.Equal(12_000_000, e.Current);
        Assert.Equal(1, e.Moves);
    }

    [Fact]
    public void Absolute_Lag_Drops_One_Tier()
    {
        var e = Eval();
        Assert.Equal(12_000_000, e.Evaluate(450, false));
    }

    [Fact]
    public void Never_Drops_Below_Floor()
    {
        var e = new AdaptEvaluator(1_500_000, AdaptEvaluator.MaxBitRate);
        Assert.Null(e.Evaluate(900, false));
        Assert.Equal(1_500_000, e.Current);
    }

    [Fact]
    public void Five_Calm_Ticks_Climb_One_Tier()
    {
        var e = Eval();
        for (var i = 0; i < 4; i++)
            Assert.Null(e.Evaluate(50, false));
        Assert.Equal(20_000_000, e.Evaluate(50, false));
    }

    [Fact]
    public void Climb_Needs_Five_Consecutive_Ticks()
    {
        var e = Eval();
        Calm(e, 4);
        Assert.Equal(12_000_000, e.Evaluate(500, false));
        for (var i = 0; i < 4; i++)
            Assert.Null(e.Evaluate(50, false));
        Assert.Equal(16_000_000, e.Evaluate(50, false));
    }

    [Fact]
    public void Mid_Lag_Resets_Streak_Without_Moving()
    {
        var e = Eval();
        Assert.Null(e.Evaluate(200, false));
        Assert.Null(e.Evaluate(140, false));
        Assert.Null(e.Evaluate(200, false));
        Assert.Null(e.Evaluate(140, false));
        Assert.Null(e.Evaluate(80, false));
        Assert.Null(e.Evaluate(60, false));
        Assert.Null(e.Evaluate(55, false));
        Assert.Equal(20_000_000, e.Evaluate(50, false));
    }

    [Fact]
    public void Never_Exceeds_Ceiling()
    {
        var e = new AdaptEvaluator(32_000_000, 40_000_000);
        Calm(e, 5);
        Assert.Equal(40_000_000, e.Current);
        Calm(e, 10);
        Assert.Equal(40_000_000, e.Current);
        Assert.Equal(1, e.Moves);
    }

    [Fact]
    public void Lower_Ceiling_Bounds_Climb()
    {
        var e = new AdaptEvaluator(16_000_000, 20_000_000);
        Calm(e, 5);
        Assert.Equal(20_000_000, e.Current);
        Calm(e, 10);
        Assert.Equal(20_000_000, e.Current);
    }

    [Fact]
    public void Hidden_Video_Never_Moves()
    {
        var e = Eval();
        Assert.Null(e.Evaluate(900, true));
        Assert.Null(e.Evaluate(50, true));
        Assert.Equal(16_000_000, e.Current);
    }

    [Fact]
    public void No_Oscillation_After_Drop()
    {
        var e = Eval();
        Assert.Equal(12_000_000, e.Evaluate(500, false));
        for (var i = 0; i < 4; i++)
            Assert.Null(e.Evaluate(50, false));
        Assert.Equal(16_000_000, e.Evaluate(50, false));
    }

    [Fact]
    public void AdaptiveCeiling_RoundTrips_In_DevicePrefs()
    {
        var dp = new DevicePrefs { AdaptiveCeiling = 32_000_000 };
        var back = JsonSerializer.Deserialize<DevicePrefs>(JsonSerializer.Serialize(dp));
        Assert.Equal(32_000_000, back!.AdaptiveCeiling);
        var wd = new WorkspaceDevice { AdaptiveCeiling = 24_000_000 };
        var backWd = JsonSerializer.Deserialize<WorkspaceDevice>(JsonSerializer.Serialize(wd));
        Assert.Equal(24_000_000, backWd!.AdaptiveCeiling);
    }

    [Fact]
    public void Peak_And_StableTicks_Tracked()
    {
        var e = Eval();
        Calm(e, 5);
        Assert.Equal(20_000_000, e.PeakBitRate);
        Assert.Equal(0, e.StableTicks);
        Calm(e, 6);
        Assert.Equal(24_000_000, e.Current);
        Calm(e, 3);
        Assert.Equal(4, e.StableTicks);
        Assert.Equal(24_000_000, e.PeakBitRate);
        Assert.Equal(2, e.Moves);
    }
}
