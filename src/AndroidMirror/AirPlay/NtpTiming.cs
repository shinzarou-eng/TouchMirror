using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TouchMirror.Services;

namespace TouchMirror.AirPlay;

internal sealed class NtpTiming : IDisposable
{
    private const long Seconds1900To1970 = 2208988800L;
    private readonly UdpClient _udp;
    private readonly IPEndPoint _clientTiming;
    private CancellationTokenSource? _cts;

    public int LocalPort { get; }

    public NtpTiming(IPAddress clientAddress, int clientTimingPort)
    {
        _clientTiming = new IPEndPoint(clientAddress, clientTimingPort);
        _udp = new UdpClient(0);
        LocalPort = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Task.Run(() => Loop(ct), ct);
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var req = new byte[32];
                req[0] = 0x80;
                req[1] = 0xD2;
                req[3] = 0x07;
                PutNtpTimestamp(req, 24, NowNtp());
                await _udp.SendAsync(req, _clientTiming, ct);

                var recv = await _udp.ReceiveAsync(ct);
                if (recv.Buffer.Length >= 32 && recv.Buffer[1] == 0xD3)
                    AppLogger.Write("AirPlay NTP: reponse timing recue");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                AppLogger.Write($"AirPlay NTP: {ex.Message}");
            }

            try { await Task.Delay(3000, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static ulong NowNtp()
    {
        var us = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);
        var secs = us / 1_000_000 + (ulong)Seconds1900To1970;
        var frac = (us % 1_000_000) * 4294967296UL / 1_000_000;
        return (secs << 32) | frac;
    }

    private static void PutNtpTimestamp(byte[] buf, int off, ulong ts)
    {
        for (var i = 7; i >= 0; i--)
        {
            buf[off + i] = (byte)(ts & 0xFF);
            ts >>= 8;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udp.Dispose();
    }
}
