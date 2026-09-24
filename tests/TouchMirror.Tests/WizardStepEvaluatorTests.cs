using TouchMirror.Services;
using Xunit;

namespace TouchMirror.Tests;

public sealed class WizardStepEvaluatorTests
{
    private static WizardInput Input(
        bool adb = false, bool unauth = false, bool offline = false,
        bool ready = false, int pnpOnly = 0, bool foreign = false,
        string? brand = null, bool wifiDone = false)
        => new(adb, unauth, offline, ready, pnpOnly, foreign, brand, wifiDone);

    [Fact]
    public void Nothing_Detected_Starts_At_EnableDebug()
    {
        var r = WizardStepEvaluator.Evaluate(Input());
        Assert.Equal(WizardStep.EnableDebug, r.Step);
        Assert.Null(r.BlockingNoteKey);
    }

    [Fact]
    public void Pnp_Only_Means_Windows_Sees_But_Adb_Not()
    {
        var r = WizardStepEvaluator.Evaluate(Input(pnpOnly: 1, brand: "Samsung"));
        Assert.Equal(WizardStep.PlugCable, r.Step);
        Assert.Equal("wiz.block.driver", r.BlockingNoteKey);
        Assert.Equal("Samsung", r.Brand);
    }

    [Fact]
    public void Unauthorized_Goes_To_AcceptAuth()
    {
        var r = WizardStepEvaluator.Evaluate(Input(adb: true, unauth: true));
        Assert.Equal(WizardStep.AcceptAuth, r.Step);
    }

    [Fact]
    public void Offline_Without_Ready_Goes_Back_To_PlugCable()
    {
        var r = WizardStepEvaluator.Evaluate(Input(adb: true, offline: true));
        Assert.Equal(WizardStep.PlugCable, r.Step);
        Assert.Equal("wiz.block.offline", r.BlockingNoteKey);
    }

    [Fact]
    public void Ready_Reaches_Ready_Step()
    {
        var r = WizardStepEvaluator.Evaluate(Input(adb: true, ready: true));
        Assert.Equal(WizardStep.Ready, r.Step);
    }

    [Fact]
    public void Ready_With_WifiDone_Concludes()
    {
        var r = WizardStepEvaluator.Evaluate(Input(adb: true, ready: true, wifiDone: true));
        Assert.Equal(WizardStep.Done, r.Step);
    }

    [Fact]
    public void Foreign_Adb_Sets_Blocking_Note()
    {
        var r = WizardStepEvaluator.Evaluate(Input(foreign: true));
        Assert.Equal("wiz.block.adb", r.BlockingNoteKey);
    }

    [Fact]
    public void Foreign_Adb_Note_Wins_Over_Driver_Note()
    {
        var r = WizardStepEvaluator.Evaluate(Input(pnpOnly: 2, foreign: true));
        Assert.Equal(WizardStep.PlugCable, r.Step);
        Assert.Equal("wiz.block.adb", r.BlockingNoteKey);
    }

    [Fact]
    public void Regression_Ready_To_Unplugged_Returns_To_Start()
    {
        var r = WizardStepEvaluator.Evaluate(Input());
        Assert.Equal(WizardStep.EnableDebug, r.Step);
    }

    [Fact]
    public void Regression_Offline_With_Other_Ready_Device_Stays_Ready()
    {
        var r = WizardStepEvaluator.Evaluate(Input(adb: true, offline: true, ready: true));
        Assert.Equal(WizardStep.Ready, r.Step);
        Assert.Null(r.BlockingNoteKey);
    }

    [Fact]
    public void Unauthorized_Wins_Over_Offline()
    {
        var r = WizardStepEvaluator.Evaluate(Input(adb: true, unauth: true, offline: true));
        Assert.Equal(WizardStep.AcceptAuth, r.Step);
    }

    [Fact]
    public void Evaluate_Is_Total_For_All_Combinations()
    {
        foreach (var adb in new[] { false, true })
        foreach (var unauth in new[] { false, true })
        foreach (var offline in new[] { false, true })
        foreach (var ready in new[] { false, true })
        foreach (var pnp in new[] { 0, 2 })
        foreach (var foreign in new[] { false, true })
        foreach (var wifi in new[] { false, true })
        {
            var r = WizardStepEvaluator.Evaluate(Input(adb, unauth, offline, ready, pnp, foreign, "Xiaomi", wifi));
            Assert.True(Enum.IsDefined(r.Step));
        }
    }
}
