using System.Diagnostics;
using System.IO;
using System.Text;

namespace TouchMirror.Services;

public static class FirewallHelper
{
    public enum State { None, Allow, Block }

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

    public static async Task EnsureRulesAsync(
        IReadOnlyList<(string Exe, string Name)> items, Action<string>? log = null)
    {
        var todo = items.Where(i => InboundState(i.Exe) != State.Allow).ToList();
        if (todo.Count == 0)
            return;

        var sb = new StringBuilder();
        foreach (var i in todo)
        {
            var exe = i.Exe.Replace("'", "''");
            var name = i.Name.Replace("'", "''");
            sb.AppendLine($"netsh advfirewall firewall delete rule name=all program='{exe}' | Out-Null");
            sb.AppendLine($"netsh advfirewall firewall add rule name='{name}' dir=in action=allow program='{exe}' enable=yes profile=any | Out-Null");
        }
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(sb.ToString()));
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
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

        foreach (var i in todo)
        {
            if (InboundState(i.Exe) == State.Allow)
                log?.Invoke($"pare-feu : règle « {i.Name} » créée");
            else
                log?.Invoke($"pare-feu : règle « {i.Name} » toujours absente — autorise-le manuellement");
        }
    }
}
