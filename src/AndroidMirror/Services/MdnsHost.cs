using System.Net.NetworkInformation;
using Makaretu.Dns;

namespace TouchMirror.Services;

public static class MdnsHost
{
    private static readonly Lazy<MulticastService> Shared = new(() =>
    {
        var mdns = new MulticastService();
        mdns.Start();
        return mdns;
    });

    public static MulticastService Instance => Shared.Value;

    public static async Task<int> ProbeAsync(TimeSpan window, CancellationToken ct = default)
    {
        var mdns = Instance;
        var count = 0;
        void OnAnswer(object? s, MessageEventArgs e) => Interlocked.Increment(ref count);
        mdns.AnswerReceived += OnAnswer;
        try
        {
            mdns.SendQuery(ServiceDiscovery.ServiceName, type: DnsType.PTR);
            await Task.Delay(window, ct);
            return count;
        }
        finally
        {
            mdns.AnswerReceived -= OnAnswer;
        }
    }

    public static async Task<int> ProbeAdbAsync(TimeSpan window, CancellationToken ct = default)
    {
        var mdns = Instance;
        var count = 0;
        void OnAnswer(object? s, MessageEventArgs e)
        {
            if (e.Message.Answers.Any(r =>
                    r.ToString().Contains("_adb-tls", StringComparison.OrdinalIgnoreCase)))
                Interlocked.Increment(ref count);
        }
        mdns.AnswerReceived += OnAnswer;
        try
        {
            mdns.SendQuery("_adb-tls-pairing._tcp.local", type: DnsType.PTR);
            mdns.SendQuery("_adb-tls-connect._tcp.local", type: DnsType.PTR);
            await Task.Delay(window, ct);
            return count;
        }
        finally
        {
            mdns.AnswerReceived -= OnAnswer;
        }
    }

    public static List<string> LanAddresses()
    {
        try
        {
            return MulticastService.GetIPAddresses()
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .ToList();
        }
        catch { return new List<string>(); }
    }
}
