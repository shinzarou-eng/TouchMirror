using System.Net;
using System.Security.Cryptography;
using System.Text;
using Makaretu.Dns;

namespace TouchMirror.Services;

public enum QrPairState { Waiting, Found, Pairing, Done, Failed, Timeout }

public sealed class QrPairSession : IDisposable
{
    private const string PairingServiceType = "_adb-tls-pairing._tcp";

    private static readonly char[] SafeChars =
        "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789".ToCharArray();

    private static string RandomChars(int length)
    {
        var buf = new StringBuilder(length);
        var bytes = RandomNumberGenerator.GetBytes(length);
        foreach (var b in bytes)
            buf.Append(SafeChars[b % SafeChars.Length]);
        return buf.ToString();
    }

    private readonly CancellationTokenSource _cts = new();
    private readonly CancellationToken _sessionToken;
    private bool _disposed;

    public QrPairSession() => _sessionToken = _cts.Token;

    public string ServiceName { get; } = $"studio-{RandomChars(10)}";
    public string Password { get; } = RandomChars(12);
    public string Payload => $"WIFI:T:ADB;S:{ServiceName};P:{Password};;";

    public async Task RunAsync(Func<QrPairState, string?, Task> onState, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _sessionToken);
        var token = linked.Token;
        var deadline = DateTime.UtcNow.AddSeconds(120);
        string? found = null;

        await onState(QrPairState.Waiting, null);

        using var sd = new ServiceDiscovery(MdnsHost.Instance);
        sd.ServiceInstanceDiscovered += (_, e) =>
        {
            try
            {
                if (!e.ServiceInstanceName.ToString().StartsWith(ServiceName + ".", StringComparison.OrdinalIgnoreCase))
                    return;
                var records = e.Message.Answers.Concat(e.Message.AdditionalRecords).ToList();
                var srv = records.OfType<SRVRecord>()
                    .FirstOrDefault(r => r.Name.ToString().StartsWith(ServiceName + ".", StringComparison.OrdinalIgnoreCase));
                if (srv == null || srv.Port == 0)
                    return;
                var target = srv.Target?.ToString();
                var ip = records.OfType<ARecord>()
                    .FirstOrDefault(r => target != null && r.Name.ToString().Equals(target, StringComparison.OrdinalIgnoreCase))?.Address
                    ?? records.OfType<ARecord>().FirstOrDefault()?.Address;
                if (ip == null || IPAddress.Any.Equals(ip))
                    return;
                found = $"{ip}:{srv.Port}";
            }
            catch { }
        };

        while (found == null && DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            try { sd.QueryServiceInstances(PairingServiceType); } catch { }
            found ??= await AdbService.FindMdnsServiceAddressAsync(ServiceName, token);
            if (found != null)
                break;
            try { await Task.Delay(1200, token); }
            catch (OperationCanceledException) { return; }
        }

        if (found == null)
        {
            await onState(QrPairState.Timeout, null);
            return;
        }

        await onState(QrPairState.Found, found);
        await onState(QrPairState.Pairing, found);
        try
        {
            var result = await AdbService.PairAsync(found, Password, token);
            await onState(QrPairState.Done, result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            await onState(QrPairState.Failed, ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        _cts.Dispose();
    }
}
