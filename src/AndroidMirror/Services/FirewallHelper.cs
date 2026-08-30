using System.Diagnostics;
using System.IO;
using System.Text;

namespace TouchMirror.Services;

/// <summary>
/// Pare-feu Windows : vérifie qu'une règle entrante « allow » existe pour un
/// exécutable et la crée au besoin. La lecture passe par le COM FwPolicy2
/// (pas d'admin requis) ; la création lance netsh élevé — une seule invite
/// UAC regroupant toutes les règles manquantes.
/// </summary>
public static class FirewallHelper
{
    public enum State { None, Allow, Block }

    /// <summary>État de la règle entrante visant exactement cet exécutable.</summary>
    public static State InboundState(string exePath)
    {
        try
        {
            var t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (t == null) return State.None;
            dynamic policy = Activator.CreateInstance(t)!;
            var state = State.None;
            foreach (dynamic rule in policy.Rules)
            {
                string? app = null;
                try { app = rule.ApplicationName as string; } catch { }
                if (app == null ||
                    !string.Equals(app, exePath, StringComparison.OrdinalIgnoreCase))
                    continue;
                int dir = 1;
                try { dir = (int)rule.Direction; } catch { }
                if (dir != 1) continue; // entrante seulement
                bool enabled = true;
                try { enabled = (bool)rule.Enabled; } catch { }
                if (!enabled) continue;
                int action = 1;
                try { action = (int)rule.Action; } catch { }
                state = action == 0 ? State.Block : State.Allow;
            }
            return state;
        }
        catch { return State.None; }
    }

    /// <summary>
    /// Crée les règles entrantes manquantes (et remplace les règles « block »).
    /// Une seule élévation UAC pour tout le lot ; sans accord utilisateur,
    /// on loggue et on continue — Windows affichera son propre prompt au bind.
    /// </summary>
    public static async Task EnsureRulesAsync(
        IReadOnlyList<(string Exe, string Name)> items, Action<string>? log = null)
    {
        var todo = items.Where(i => InboundState(i.Exe) != State.Allow).ToList();
        if (todo.Count == 0)
            return;

        var script = Path.Combine(Path.GetTempPath(), $"tm-fw-{Environment.ProcessId}.cmd");
        var sb = new StringBuilder("@echo off\r\n");
        foreach (var i in todo)
        {
            // Supprime d'abord toute règle existante (un « block » gagnerait
            // sinon sur l'allow qu'on ajoute), puis crée l'allow.
            sb.AppendLine($"netsh advfirewall firewall delete rule name=all program=\"{i.Exe}\" >nul 2>&1");
            sb.AppendLine($"netsh advfirewall firewall add rule name=\"{i.Name}\" dir=in action=allow program=\"{i.Exe}\" enable=yes profile=any");
        }
        await File.WriteAllTextAsync(script, sb.ToString());
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{script}\"",
                UseShellExecute = true,
                Verb = "runas", // UAC une seule fois ; sans effet si déjà admin
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            await p!.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            log?.Invoke($"pare-feu : règles non créées ({ex.Message})");
        }
        finally
        {
            try { File.Delete(script); } catch { }
        }

        foreach (var i in todo)
        {
            if (InboundState(i.Exe) == State.Allow)
                log?.Invoke($"pare-feu : règle « {i.Name} » créée");
            else
                log?.Invoke($"pare-feu : règle « {i.Name} » toujours absente — autorise-le manuellement");
        }
    }
}
