using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using TouchMirror.Video;

namespace TouchMirror.AirPlay;

public sealed class AirPlayAudioStream : IDisposable
{
    private static byte[] AlacCookie(int frameLength)
    {
        var c = new byte[]
        {
            0x00, 0x00, 0x00, 0x24, 0x61, 0x6C, 0x61, 0x63, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x01, 0x60, 0x00, 0x10, 0x28, 0x0A, 0x0E, 0x02, 0x00, 0xFF,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xAC, 0x44,
        };
        c[12] = (byte)(frameLength >> 24);
        c[13] = (byte)(frameLength >> 16);
        c[14] = (byte)(frameLength >> 8);
        c[15] = (byte)frameLength;
        return c;
    }

    private static readonly byte[] AacEldConfig = { 0xF8, 0xE8, 0x50, 0x00 };

    private const int ReorderWindow = 16;

    private readonly UdpClient _data;
    private readonly byte[] _key;
    private readonly byte[] _iv;
    private readonly AudioPlayer _player;
    private readonly Dictionary<ushort, (uint Ts, byte[] Data)> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private ushort _nextSeq;
    private bool _seqInit;
    private bool _disposed;
    private int _pktCount;
    private int _dumped;
    private FileStream? _dump;
    private readonly string _dumpPath = Path.Combine(Path.GetTempPath(), "eld-frames.bin");

    public event Action<string>? Log;

    public int DataPort { get; }
    public int ControlPort { get; }

    public AirPlayAudioStream(byte[] key, byte[] iv, int codecType = 2, int samplesPerFrame = 352)
    {
        _key = key;
        _iv = iv;
        _data = new UdpClient(0);
        var ctrl = new UdpClient(0);
        DataPort = ((IPEndPoint)_data.Client.LocalEndPoint!).Port;
        ControlPort = ((IPEndPoint)ctrl.Client.LocalEndPoint!).Port;
        _control = ctrl;
        try
        {
            if (Environment.GetEnvironmentVariable("TM_DUMP_AUDIO") != null)
                _dump = File.Create(_dumpPath);
        }
        catch { }
        _player = new AudioPlayer(codecType == 8 ? "aaceld" : "alac");
        _player.Error += m => Log?.Invoke($"airplay audio: {m}");
        _player.Volume = 1f;
        var config = codecType == 8 ? AacEldConfig : AlacCookie(samplesPerFrame);
        _player.Feed(config, true, config.Length);
    }

    private readonly UdpClient _control;

    public void Start(CancellationToken ct)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        _ = Task.Run(() => ReceiveLoop(linked.Token), linked.Token);
        _ = Task.Run(() => DrainControl(linked.Token), linked.Token);
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult pkt;
            try { pkt = await _data.ReceiveAsync(ct); }
            catch { break; }
            var d = pkt.Buffer;
            if (d.Length < 12)
                continue;
            var headLen = 12 + 4 * (d[0] & 0x0F);
            if ((d[0] & 0x10) != 0 && d.Length > headLen + 4)
                headLen += 4 + 4 * ((d[headLen + 2] << 8) | d[headLen + 3]);
            if (d.Length <= headLen)
                continue;
            if (d.Length == 16 && d[12] == 0x00 && d[13] == 0x68 && d[14] == 0x34 && d[15] == 0x00)
                continue;

            var seq = (ushort)((d[2] << 8) | d[3]);
            var payloadLen = d.Length - headLen;
            var plain = new byte[payloadLen];
            var encLen = payloadLen / 16 * 16;
            try
            {
                if (encLen > 0)
                {
                    using var aes = Aes.Create();
                    aes.Key = _key;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.None;
                    var dec = aes.DecryptCbc(d.AsSpan(headLen, encLen), _iv, PaddingMode.None);
                    dec.AsSpan().CopyTo(plain);
                }
                Buffer.BlockCopy(d, headLen + encLen, plain, encLen, payloadLen - encLen);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"airplay audio: decrypt {ex.Message}");
                continue;
            }

            if (!_seqInit)
            {
                _nextSeq = seq;
                _seqInit = true;
                Log?.Invoke($"airplay audio: premier paquet seq={seq} {payloadLen}b tete={Convert.ToHexString(plain.AsSpan(0, Math.Min(8, plain.Length)))}");
            }
            if (_dump != null)
            {
                _dump.WriteByte((byte)(plain.Length & 0xFF));
                _dump.WriteByte((byte)(plain.Length >> 8));
                _dump.Write(plain);
                if (++_dumped >= 400)
                {
                    _dump.Dispose();
                    _dump = null;
                    Log?.Invoke($"airplay audio: {_dumped} trames dumpees dans {_dumpPath}");
                }
            }
            var delta = (short)(seq - _nextSeq);
            if (delta < 0)
                continue;
            var ts = (uint)((d[4] << 24) | (d[5] << 16) | (d[6] << 8) | d[7]);
            _pending[seq] = (ts, plain);
            if (++_pktCount % 500 == 0)
                Log?.Invoke($"airplay audio: {_pktCount} paquets recus");
            Drain();
        }
    }

    private void Drain()
    {
        while (_pending.Count > ReorderWindow)
        {
            ushort best = 0;
            short bestDelta = short.MaxValue;
            foreach (var s in _pending.Keys)
            {
                var dl = (short)(s - _nextSeq);
                if (dl < bestDelta)
                {
                    bestDelta = dl;
                    best = s;
                }
            }
            _nextSeq = best;
            break;
        }
        while (_pending.Remove(_nextSeq, out var frame))
        {
            if (_recentTs.Add(frame.Ts))
            {
                _player.Feed(frame.Data, false, frame.Data.Length);
                _tsOrder.Enqueue(frame.Ts);
                if (_tsOrder.Count > 64)
                    _recentTs.Remove(_tsOrder.Dequeue());
            }
            _nextSeq++;
        }
    }

    private readonly HashSet<uint> _recentTs = new();
    private readonly Queue<uint> _tsOrder = new();

    private async Task DrainControl(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await _control.ReceiveAsync(ct); }
            catch { break; }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        try { _data.Dispose(); } catch { }
        try { _control.Dispose(); } catch { }
        try { _dump?.Dispose(); _dump = null; } catch { }
        try { _player.Dispose(); } catch { }
        _cts.Dispose();
    }
}
