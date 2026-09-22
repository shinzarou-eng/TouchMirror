using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
namespace TouchMirror.Services;

public static class WifiDiag
{
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint v, IntPtr r, out uint nv, out IntPtr h);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr h, IntPtr r);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr h, IntPtr r, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr h, ref Guid guid, uint op, IntPtr r, out uint size, out IntPtr data, IntPtr vt);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr p);

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanInterfaceInfoList { public uint Count, Index; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        public Guid Guid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public uint State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid { public uint Length; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionAttributes
    {
        public uint IsState, ConnectionMode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public Dot11Ssid Ssid;
        public uint BssType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
        public uint PhyType, PhyIndex, SignalQuality, RxRate, TxRate;
        public uint SecEnabled, OneXEnabled, AuthAlgo, CipherAlgo;
    }

    public sealed record PcWifi(bool Connected, string Ssid, int Signal, int RxMbps, int Channel);

    public static PcWifi? QueryPcWifi()
    {
        if (WlanOpenHandle(2, IntPtr.Zero, out _, out var h) != 0)
            return null;
        try
        {
            if (WlanEnumInterfaces(h, IntPtr.Zero, out var listPtr) != 0)
                return null;
            try
            {
                var list = Marshal.PtrToStructure<WlanInterfaceInfoList>(listPtr);
                for (var i = 0; i < list.Count; i++)
                {
                    var itemPtr = listPtr + 8 + i * Marshal.SizeOf<WlanInterfaceInfo>();
                    var iface = Marshal.PtrToStructure<WlanInterfaceInfo>(itemPtr);
                    if (iface.State != 1)
                        continue;
                    var guid = iface.Guid;
                    if (WlanQueryInterface(h, ref guid, 7, IntPtr.Zero, out var size, out var data, IntPtr.Zero) != 0)
                        continue;
                    try
                    {
                        var a = Marshal.PtrToStructure<WlanConnectionAttributes>(data);
                        var ssid = Encoding.UTF8.GetString(a.Ssid.Bytes, 0, (int)Math.Min(a.Ssid.Length, 32));
                        var channel = 0;
                        if (WlanQueryInterface(h, ref guid, 8, IntPtr.Zero, out var csz, out var cdata, IntPtr.Zero) == 0)
                        {
                            try { channel = Marshal.ReadInt32(cdata); } finally { WlanFreeMemory(cdata); }
                        }
                        return new PcWifi(true, ssid, (int)a.SignalQuality, (int)(a.RxRate / 1000), channel);
                    }
                    finally { WlanFreeMemory(data); }
                }
                return new PcWifi(false, "", 0, 0, 0);
            }
            finally { WlanFreeMemory(listPtr); }
        }
        catch { return null; }
        finally { WlanCloseHandle(h, IntPtr.Zero); }
    }

    public static string Band(int channel) => channel switch
    {
        >= 1 and <= 14 => "2,4 GHz",
        > 14 and <= 177 => "5 GHz",
        > 177 => "6 GHz",
        _ => "n/d"
    };

    public static async Task<(int Rssi, int Mbps, string Band)?> PhoneWifiAsync(string serial, CancellationToken ct)
    {
        string dump;
        try { dump = await AdbService.ProbeAsync($"-s {S(serial)} shell dumpsys wifi", ct); }
        catch { return null; }
        var rssi = Regex.Match(dump, @"RSSI:\s*(-?\d+)");
        var link = Regex.Match(dump, @"Link speed:\s*(\d+)\s*Mbps");
        var freq = Regex.Match(dump, @"[Ff]requency:\s*(\d+)\s*MHz");
        if (!rssi.Success && !link.Success)
            return null;
        var band = freq.Success
            ? int.Parse(freq.Groups[1].Value) switch
            {
                >= 5925 => "6 GHz",
                >= 4900 => "5 GHz",
                _ => "2,4 GHz"
            }
            : "n/d";
        return (rssi.Success ? int.Parse(rssi.Groups[1].Value) : 0,
                link.Success ? int.Parse(link.Groups[1].Value) : 0, band);
    }

    public static async Task<string?> PhoneIpAsync(AdbDevice dev, CancellationToken ct)
    {
        var m = Regex.Match(dev.Serial, @"^(\d+\.\d+\.\d+\.\d+):\d+$");
        if (m.Success)
            return m.Groups[1].Value;
        try
        {
            var o = await AdbService.ProbeAsync($"-s {S(dev.Serial)} shell ip -f inet addr show wlan0", ct);
            var mm = Regex.Match(o, @"inet\s+(\d+\.\d+\.\d+\.\d+)");
            if (mm.Success)
                return mm.Groups[1].Value;
        }
        catch { }
        return null;
    }

    public static (string Ip, string Mask)? PcIpv4For(string targetIp)
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect(System.Net.IPAddress.Parse(targetIp), 9);
            if (s.LocalEndPoint is not System.Net.IPEndPoint ep)
                return PcPrimaryIpv4();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.Equals(ep.Address) && ua.IPv4Mask != null)
                        return (ep.Address.ToString(), ua.IPv4Mask.ToString());
            return (ep.Address.ToString(), "255.255.255.0");
        }
        catch { return PcPrimaryIpv4(); }
    }

    public static (string Ip, string Mask)? PcPrimaryIpv4()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType is not (NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet))
                continue;
            var ip = ni.GetIPProperties();
            foreach (var ua in ip.UnicastAddresses)
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork && ua.IPv4Mask != null)
                    return (ua.Address.ToString(), ua.IPv4Mask.ToString());
        }
        return null;
    }

    public static bool SameSubnet(string a, string b, string mask)
    {
        var pa = a.Split('.').Select(byte.Parse).ToArray();
        var pb = b.Split('.').Select(byte.Parse).ToArray();
        var pm = mask.Split('.').Select(byte.Parse).ToArray();
        for (var i = 0; i < 4; i++)
            if ((pa[i] & pm[i]) != (pb[i] & pm[i]))
                return false;
        return true;
    }

    public static async Task<(int Sent, int Lost, double Min, double Avg, double Max)?> PingAsync(string ip, int count, CancellationToken ct)
    {
        var rtts = new List<double>();
        var lost = 0;
        using var ping = new Ping();
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var r = await ping.SendPingAsync(ip, 1200);
                if (r.Status == IPStatus.Success)
                    rtts.Add(r.RoundtripTime);
                else
                    lost++;
            }
            catch { lost++; }
        }
        if (rtts.Count == 0)
            return (count, lost, -1, -1, -1);
        return (count, lost, rtts.Min(), rtts.Average(), rtts.Max());
    }

    private static string S(string serial) => serial.Contains(' ') ? $"\"{serial}\"" : serial;
}
