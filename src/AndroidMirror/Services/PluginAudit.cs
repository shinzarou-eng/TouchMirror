using System.Text.RegularExpressions;

namespace TouchMirror.Services;

/// <summary>Lit les appels tm.* d'un plugin pour afficher ce qu'il peut vraiment faire.</summary>
public static class PluginAudit
{
    private static readonly Regex Calls = new(@"\btm\.(\w+)", RegexOptions.Compiled);
    private static readonly Regex Events = new(@"\btm\.on\(\s*['""]([\w.]+)", RegexOptions.Compiled);

    public static List<string> Extract(string code)
    {
        var labels = new List<string>();
        var seen = new HashSet<string>();
        var onIndex = -1;

        void Add(string label)
        {
            if (seen.Add(label))
                labels.Add(label);
        }

        foreach (Match m in Calls.Matches(code))
        {
            switch (m.Groups[1].Value)
            {
                case "getStatus" or "getMirrors" or "getDevices":
                    Add(LocalizationService.Get("audit.read_state")); break;
                case "activate": Add(LocalizationService.Get("audit.activate")); break;
                case "connect": Add(LocalizationService.Get("audit.connect")); break;
                case "disconnect": Add(LocalizationService.Get("audit.disconnect")); break;
                case "record": Add(LocalizationService.Get("audit.record")); break;
                case "screenshot": Add(LocalizationService.Get("audit.screenshot")); break;
                case "on":
                    if (onIndex < 0)
                        onIndex = labels.Count;
                    break;
                case "setTimeout" or "setInterval" or "clearTimeout" or "clearInterval":
                    Add(LocalizationService.Get("audit.timers")); break;
                case "log": Add(LocalizationService.Get("audit.log")); break;
                default: Add(string.Format(LocalizationService.Get("audit.other"), m.Groups[1].Value)); break;
            }
        }

        if (onIndex >= 0)
        {
            var events = new List<string>();
            var seenEvents = new HashSet<string>();
            foreach (Match m in Events.Matches(code))
                if (seenEvents.Add(m.Groups[1].Value))
                    events.Add(m.Groups[1].Value);
            var label = events.Count > 0
                ? string.Format(LocalizationService.Get("audit.events_of"), string.Join(", ", events))
                : LocalizationService.Get("audit.events");
            if (seen.Add(label))
                labels.Insert(onIndex, label);
        }

        if (labels.Count == 0)
            labels.Add(LocalizationService.Get("audit.none"));
        return labels;
    }
}
