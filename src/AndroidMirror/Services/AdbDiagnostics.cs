using System.Collections.Concurrent;

namespace TouchMirror.Services;

public static class AdbDiagnostics
{
    private const int FlapWindowSeconds = 60;
    private const int FlapThreshold = 4;

    private sealed class History
    {
        public string? LastState;
        public readonly List<DateTime> Transitions = new();
    }

    private static readonly ConcurrentDictionary<string, History> _history = new();

    public static void Record(string serial, string state)
    {
        var h = _history.GetOrAdd(serial, _ => new History());
        lock (h)
        {
            if (h.LastState == state)
                return;
            h.LastState = state;
            var now = DateTime.UtcNow;
            h.Transitions.Add(now);
            h.Transitions.RemoveAll(t => now - t > TimeSpan.FromMinutes(5));
        }
    }

    public static string? Verdict(string serial, string state)
    {
        if (!_history.TryGetValue(serial, out var h))
            return null;
        lock (h)
        {
            var now = DateTime.UtcNow;
            var recent = h.Transitions.Count(t => now - t < TimeSpan.FromSeconds(FlapWindowSeconds));
            if (recent >= FlapThreshold)
                return LocalizationService.Get("diag.unstable");
            if (state == "unauthorized")
                return LocalizationService.Get("diag.unauthorized");
            if (state == "offline")
                return LocalizationService.Get("diag.offline");
            return null;
        }
    }
}
