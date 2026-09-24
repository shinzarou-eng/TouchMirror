namespace TouchMirror.Services;

public enum WizardStep { EnableDebug, PlugCable, AcceptAuth, Ready, Wifi, Done }

public sealed record WizardInput(
    bool HasAdbDevice, bool HasUnauthorized, bool HasOffline,
    bool HasReady, int PnpOnlyCount, bool HasForeignAdb,
    string? Brand, bool WifiDone);

public sealed record WizardResult(WizardStep Step, string? BlockingNoteKey, string? Brand);

public static class WizardStepEvaluator
{
    public static WizardResult Evaluate(WizardInput i)
    {
        var note = i.HasForeignAdb ? "wiz.block.adb" : null;
        if (i.HasUnauthorized)
            return new(WizardStep.AcceptAuth, note, i.Brand);
        if (!i.HasAdbDevice)
            return new(i.PnpOnlyCount > 0 ? WizardStep.PlugCable : WizardStep.EnableDebug,
                note ?? (i.PnpOnlyCount > 0 ? "wiz.block.driver" : null), i.Brand);
        if (i.HasOffline && !i.HasReady)
            return new(WizardStep.PlugCable, note ?? "wiz.block.offline", i.Brand);
        if (i.HasReady)
            return new(i.WifiDone ? WizardStep.Done : WizardStep.Ready, note, i.Brand);
        return new(WizardStep.EnableDebug, note, i.Brand);
    }
}
