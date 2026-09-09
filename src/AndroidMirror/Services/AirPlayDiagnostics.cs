using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace TouchMirror.Services;

/// <summary>
/// Diagnostic réseau AirPlay : détecte ce qui empêcherait un iPhone de
/// joindre le récepteur — règle pare-feu entrante absente, port déjà occupé
/// par un autre process, profil réseau public, IP locale à indiquer.
/// </summary>
public static class AirPlayDiagnostics
{
    public sealed record Result(
        string? LocalIPv4,
        string? ProfileKind,
        IReadOnlyList<string> PortConflicts,
        bool FirewallRuleMissing,
        bool FirewallRuleBlocking);

    public static bool HasProblem(Result r) =>
        r.PortConflicts.Count > 0 || r.FirewallRuleBlocking || r.FirewallRuleMissing;

    public static Result Run(string hostExePath, int ownHostPid, params int[] ports)
    {
        var fw = FirewallRuleState(hostExePath);
        return new Result(
            LocalIPv4: PrimaryIPv4(),
            ProfileKind: ActiveProfileKind(),
            PortConflicts: PortOwners(ports, ownHostPid),
            FirewallRuleMissing: fw == RuleState.None,
            FirewallRuleBlocking: fw == RuleState.Block);
    }

    /// <summary>Phrase courte pour le statut utilisateur (null si tout va bien).</summary>
    public static string? Summarize(Result r)
    {
        if (r.FirewallRuleBlocking)
            return LocalizationService.Get("air.fw_blocked");
        if (r.FirewallRuleMissing)
            return LocalizationService.Get("air.fw_missing");
        if (r.PortConflicts.Count > 0)
            return string.Format(LocalizationService.Get("air.ports_used"), string.Join(", ", r.PortConflicts));
        if (string.Equals(r.ProfileKind, LocalizationService.Get("profile.public"), StringComparison.OrdinalIgnoreCase))
            return LocalizationService.Get("air.public_profile");
        return null;
    }

    private static string? PrimaryIPv4()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            var ip = ni.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            if (ip != null)
                return ip.Address.ToString();
        }
        return null;
    }

    /// <summary>Profil du réseau actif via Network List Manager (COM, pas d'admin).</summary>
    private static string? ActiveProfileKind()
    {
        try
        {
            // CLSID de NetworkListManager (pas de ProgID enregistré).
            var t = Type.GetTypeFromCLSID(
                new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
            if (t == null) return null;
            dynamic nlm = Activator.CreateInstance(t)!;
            dynamic networks = nlm.GetNetworks(1); // NLM_ENUM_NETWORK_CONNECTED
            foreach (var net in networks)
            {
                int cat = net.GetCategory();
                return cat switch { 0 => LocalizationService.Get("profile.public"), 1 => LocalizationService.Get("profile.private"), 2 => LocalizationService.Get("profile.domain"), _ => null };
            }
        }
        catch { }
        return null;
    }

    private enum RuleState { None, Allow, Block }

    /// <summary>Règle pare-feu entrante visant l'exécutable du host (COM FwPolicy2).</summary>
    private static RuleState FirewallRuleState(string exePath)
    {
        try
        {
            var t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (t == null) return RuleState.None;
            dynamic policy = Activator.CreateInstance(t)!;
            var state = RuleState.None;
            foreach (dynamic rule in policy.Rules)
            {
                string? app = null;
                try { app = rule.ApplicationName as string; } catch { }
                if (app == null ||
                    !app.EndsWith("airplayhost.exe", StringComparison.OrdinalIgnoreCase))
                    continue;
                // Direction entrante seulement
                int dir = 1;
                try { dir = (int)rule.Direction; } catch { }
                if (dir != 1) continue;
                bool enabled = true;
                try { enabled = (bool)rule.Enabled; } catch { }
                if (!enabled) continue;
                int action = 1;
                try { action = (int)rule.Action; } catch { }
                state = action == 0 ? RuleState.Block : RuleState.Allow;
            }
            return state;
        }
        catch { return RuleState.None; }
    }

    /// <summary>Ports TCP déjà écoutés par un AUTRE processus (GetExtendedTcpTable).</summary>
    private static IReadOnlyList<string> PortOwners(int[] ports, int ownHostPid)
    {
        var conflicts = new List<string>();
        var wanted = new HashSet<int>(ports);
        foreach (var (port, pid) in Listeners())
        {
            if (!wanted.Contains(port) || pid == Environment.ProcessId ||
                pid == ownHostPid)
                continue;
            var name = $"pid {pid}";
            try { name = System.Diagnostics.Process.GetProcessById(pid).ProcessName; } catch { }
            conflicts.Add($"{port} ({name})");
        }
        return conflicts;
    }

    private static IEnumerable<(int port, int pid)> Listeners()
    {
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, true, 2, TcpTableType.OwnerPidAll, 0);
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, true, 2, TcpTableType.OwnerPidAll, 0) != 0)
                yield break;
            int count = Marshal.ReadInt32(buf);
            var row = buf + 4;
            int rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            for (var i = 0; i < count; i++, row += rowSize)
            {
                var r = Marshal.PtrToStructure<MibTcpRowOwnerPid>(row);
                if (r.dwState != 2) continue; // MIB_TCP_STATE_LISTEN
                var port = (int)(((r.dwLocalPort & 0xFF) << 8) | ((r.dwLocalPort >> 8) & 0xFF));
                yield return (port, (int)r.dwOwningPid);
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private enum TcpTableType { OwnerPidAll = 5 }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int dwOutBufLen, bool sort,
        int ipVersion, TcpTableType tblClass, uint reserved);
}
