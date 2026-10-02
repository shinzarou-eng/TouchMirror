namespace TouchMirror.Services;

public static class WatchdogGate
{
    public const int SilenceThresholdMs = 12_000;

    public static bool IsSuppressed(long nowMs, long suppressUntilMs)
        => nowMs < suppressUntilMs;

    public static bool SilenceExceeded(long silentSinceMs, long nowMs)
        => silentSinceMs != 0 && nowMs - silentSinceMs > SilenceThresholdMs;
}
