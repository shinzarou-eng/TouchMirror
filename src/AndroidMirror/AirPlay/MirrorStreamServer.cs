using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TouchMirror.Services;
using TouchMirror.Video;

namespace TouchMirror.AirPlay;

internal sealed class MirrorStreamServer : IDisposable
{
    private static readonly byte[] StartCode = { 0, 0, 0, 1 };

    private readonly byte[]? _aesKeyAudio;
    private readonly TcpListener _listener;
    private readonly VideoDecoder _decoder;
    private byte[]? _ctrKey;
    private byte[]? _ctrIv;
    private List<byte[]>? _dataSecrets;
    private string _dataSalt = "";
    private bool _hapFraming;
    private CancellationTokenSource? _cts;

    public int LocalPort { get; }
    public IFrameSource Video => _decoder;
    public event Action<string>? Log;
    public event Action? StreamDisconnected;

    public MirrorStreamServer(byte[]? aesKeyAudio)
    {
        _aesKeyAudio = aesKeyAudio;
        _decoder = new VideoDecoder("h264");
        _listener = new TcpListener(IPAddress.Any, 0);
        _listener.Start();
        LocalPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public void InitAes(ulong streamConnectionId)
    {
        var keySeed = Encoding.ASCII.GetBytes($"AirPlayStreamKey{streamConnectionId}");
        var ivSeed = Encoding.ASCII.GetBytes($"AirPlayStreamIV{streamConnectionId}");
        _ctrKey = SHA512.HashData(keySeed.Concat(_aesKeyAudio!).ToArray())[..16];
        _ctrIv = SHA512.HashData(ivSeed.Concat(_aesKeyAudio!).ToArray())[..16];
    }

    public void InitDataChannel(byte[] ecdhSecret, ulong streamConnectionId)
    {
        _dataSecrets = new List<byte[]> { ecdhSecret };
        _dataSalt = $"DataStream-Salt{streamConnectionId}";
        _hapFraming = true;
    }

    public void AddDataChannelSecret(byte[] secret)
    {
        _dataSecrets ??= new List<byte[]>();
        _dataSecrets.Add(secret);
    }

    public void Start(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task.Run(() => AcceptLoop(_cts.Token));
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => Pump(client, ct));
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                break;
            }
        }
    }

    private async Task Pump(TcpClient client, CancellationToken ct)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        Log?.Invoke($"airplay mirror: connexion video de {remote}");
        try
        {
            using (client)
            using (var ctr = _ctrKey != null ? new AesCtr(_ctrKey, _ctrIv!) : null)
            {
                var stream = client.GetStream();
                var raw = new List<byte>();
                var plain = new List<byte>();
                var plainPos = 0;
                ChannelCipher? dataCipher = null;
                var dataProbed = false;
                var dataPlain = !_hapFraming;
                var buf = new byte[65536];
                byte[]? outBuf = null;

                async Task<bool> FillAsync()
                {
                    var n = await stream.ReadAsync(buf, ct);
                    if (n == 0)
                        return false;
                    if (dataPlain)
                    {
                        plain.AddRange(new ArraySegment<byte>(buf, 0, n));
                        return true;
                    }
                    raw.AddRange(new ArraySegment<byte>(buf, 0, n));
                    if (dataCipher == null && !dataProbed && raw.Count >= 2)
                    {
                        var blen = (raw[1] << 8) | raw[0];
                        if (blen > 0x400)
                        {
                            dataProbed = true;
                            dataPlain = true;
                            Log?.Invoke($"airplay mirror: flux non-frame (len16={blen}) — traitement en clair");
                            plain.AddRange(raw);
                            raw.Clear();
                            return true;
                        }
                        if (raw.Count >= 2 + blen + 16 && _dataSecrets != null)
                        {
                            dataProbed = true;
                            foreach (var secret in _dataSecrets)
                            {
                                var trial = new ChannelCipher(secret, _dataSalt, _dataSalt,
                                    "DataStream-Output-Encryption-Key", "DataStream-Input-Encryption-Key");
                                var tmp = new byte[blen];
                                if (trial.TryDecryptBlock(CollectionsMarshal.AsSpan(raw), tmp, out var c2) < 0)
                                    continue;
                                dataCipher = trial;
                                plain.AddRange(tmp);
                                raw.RemoveRange(0, c2);
                                break;
                            }
                            if (dataCipher == null)
                            {
                                dataPlain = true;
                                Log?.Invoke($"airplay mirror: dechiffrement impossible — hex {Convert.ToHexString(raw.ToArray()[..Math.Min(48, raw.Count)])}");
                                plain.AddRange(raw);
                                raw.Clear();
                                return true;
                            }
                            Log?.Invoke("airplay mirror: canal data dechiffre");
                        }
                    }
                    if (dataCipher == null)
                        return true;
                    outBuf ??= new byte[0x400 * 8];
                    var off = 0;
                    while (raw.Count > 0)
                    {
                        var len = dataCipher.TryDecryptBlock(CollectionsMarshal.AsSpan(raw), outBuf.AsSpan(off), out var consumed);
                        if (len <= 0)
                            break;
                        off += len;
                        raw.RemoveRange(0, consumed);
                        if (off + 0x400 > outBuf.Length)
                        {
                            plain.AddRange(new ArraySegment<byte>(outBuf, 0, off));
                            off = 0;
                        }
                    }
                    if (off > 0)
                        plain.AddRange(new ArraySegment<byte>(outBuf, 0, off));
                    return true;
                }

                async Task<bool> ReadPlainAsync(byte[] buffer, int count)
                {
                    var off = 0;
                    while (off < count)
                    {
                        if (plainPos >= plain.Count)
                        {
                            plain.Clear();
                            plainPos = 0;
                            if (!await FillAsync())
                                return false;
                        }
                        var n = Math.Min(count - off, plain.Count - plainPos);
                        plain.CopyTo(plainPos, buffer, off, n);
                        plainPos += n;
                        off += n;
                    }
                    return true;
                }

                var header = new byte[128];
                var payloadBuf = new byte[64 * 1024];
                var annexBBuf = new byte[64 * 1024];
                while (!ct.IsCancellationRequested)
                {
                    if (!await ReadPlainAsync(header, header.Length))
                        break;
                    var payloadSize = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
                    var type = header[4];
                    if (payloadSize < 0 || payloadSize > 16 * 1024 * 1024)
                    {
                        Log?.Invoke($"airplay mirror: en-tete invalide (size={payloadSize} type={type}) — hex {Convert.ToHexString(header, 0, 24)}");
                        break;
                    }

                    if (payloadSize > 0)
                    {
                        if (payloadBuf.Length < payloadSize)
                        {
                            payloadBuf = new byte[payloadSize];
                            annexBBuf = new byte[payloadSize];
                        }
                        if (!await ReadPlainAsync(payloadBuf, payloadSize))
                            break;
                    }

                    switch (type)
                    {
                        case 0x00:
                        case 0x10:
                            if (payloadSize > 0)
                                HandleVideoPayload(payloadBuf, payloadSize, ctr, annexBBuf);
                            break;
                        case 0x01:
                            if (payloadSize > 0)
                                HandleCodecConfig(payloadBuf, payloadSize);
                            break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
        catch (Exception ex)
        {
            Log?.Invoke($"airplay mirror: erreur — {ex.Message}");
        }
        Log?.Invoke($"airplay mirror: flux video termine ({remote})");
        StreamDisconnected?.Invoke();
    }

    private void HandleVideoPayload(byte[] payload, int payloadLen, AesCtr? ctr, byte[] annexB)
    {
        ctr?.Apply(payload.AsSpan(0, payloadLen), payload.AsSpan(0, payloadLen));
        var len = ToAnnexB(payload, payloadLen, annexB);
        if (len > 0)
            _decoder.Feed(annexB, len);
        else
            Log?.Invoke("airplay mirror: paquet NAL invalide — ignore");
    }

    private void HandleCodecConfig(byte[] payload, int payloadLen)
    {
        if (payloadLen < 8)
            return;
        var spsLen = (payload[6] << 8) | payload[7];
        var ppsIdx = 8 + spsLen;
        if (spsLen <= 0 || ppsIdx + 3 > payloadLen)
        {
            Log?.Invoke($"airplay mirror: avcC invalide ({payloadLen}b)");
            return;
        }
        var ppsLen = (payload[ppsIdx + 1] << 8) | payload[ppsIdx + 2];
        if (ppsLen <= 0 || ppsIdx + 3 + ppsLen > payloadLen)
            ppsLen = payloadLen - ppsIdx - 3;
        if (ppsLen <= 0)
            return;

        var data = new byte[spsLen + ppsLen + 8];
        data[3] = 1;
        Array.Copy(payload, 8, data, 4, spsLen);
        data[spsLen + 7] = 1;
        Array.Copy(payload, ppsIdx + 3, data, spsLen + 8, ppsLen);
        _decoder.Feed(data, data.Length);
    }

    private static int ToAnnexB(byte[] data, int dataLen, byte[] dest)
    {
        var pos = 0;
        var outPos = 0;
        while (pos + 4 <= dataLen)
        {
            var nalLen = ReadBe32(data, pos);
            if (nalLen <= 0 || pos + 4 + nalLen > dataLen)
                return -1;
            Array.Copy(StartCode, 0, dest, outPos, 4);
            Array.Copy(data, pos + 4, dest, outPos + 4, nalLen);
            outPos += 4 + nalLen;
            pos += 4 + nalLen;
        }
        return pos == dataLen && outPos > 0 ? outPos : -1;
    }

    private static int ReadBe32(byte[] b, int off) =>
        (b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3];

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener.Stop(); } catch { }
        _decoder.Dispose();
    }
}
