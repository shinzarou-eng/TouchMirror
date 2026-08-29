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
                    Add("Lire l'état des miroirs et appareils"); break;
                case "activate": Add("Changer le miroir actif"); break;
                case "connect": Add("Connecter un appareil"); break;
                case "disconnect": Add("Déconnecter un miroir"); break;
                case "record": Add("Démarrer / arrêter un enregistrement"); break;
                case "screenshot": Add("Prendre des captures d'écran"); break;
                case "on":
                    if (onIndex < 0)
                        onIndex = labels.Count;
                    break;
                case "setTimeout" or "setInterval" or "clearTimeout" or "clearInterval":
                    Add("Minuteries"); break;
                case "log": Add("Écrire dans le journal de l'app"); break;
                default: Add($"Autre : tm.{m.Groups[1].Value}"); break;
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
                ? $"Écouter les événements : {string.Join(", ", events)}"
                : "Écouter les événements";
            if (seen.Add(label))
                labels.Insert(onIndex, label);
        }

        if (labels.Count == 0)
            labels.Add("Aucun accès — le plugin ne touche à rien");
        return labels;
    }
}
