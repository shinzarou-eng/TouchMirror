using System.Buffers.Binary;
using System.IO;

namespace TouchMirror.Engine;

internal sealed class MuxFramer
{
    internal const int HeaderLength = 9;
    internal const int MaxPayload = 64 << 20;
    internal const byte MaxChannel = 4;
    private static ReadOnlySpan<byte> Magic => "TMIR"u8;

    private readonly Stream _stream;
    private readonly byte[] _buf = new byte[8192];
    private readonly byte[] _pending = new byte[16];
    private int _pos;
    private int _len;
    private int _pendingLen;

    internal MuxFramer(Stream stream) => _stream = stream;

    internal int Resyncs { get; private set; }
    internal long ResyncBytes { get; private set; }

    internal static bool HasMagic(ReadOnlySpan<byte> header)
        => header.Length >= 4 && header[..4].SequenceEqual(Magic);

    internal static bool IsValidHeader(byte channel, int size)
        => channel <= MaxChannel && size > 0 && size <= MaxPayload;

    internal async ValueTask<bool> FillExactAsync(Memory<byte> dst, CancellationToken ct)
    {
        var off = 0;
        while (off < dst.Length)
        {
            if (_pendingLen > 0)
            {
                dst.Span[off++] = _pending[--_pendingLen];
                continue;
            }
            if (_pos == _len)
            {
                _len = await _stream.ReadAsync(_buf, ct);
                _pos = 0;
                if (_len == 0)
                    return false;
            }
            var m = Math.Min(_len - _pos, dst.Length - off);
            _buf.AsMemory(_pos, m).CopyTo(dst.Slice(off, m));
            _pos += m;
            off += m;
        }
        return true;
    }

    internal async ValueTask<(byte Channel, int Size)?> ReadHeaderAsync(CancellationToken ct)
    {
        var h = new byte[HeaderLength];
        if (!await FillExactAsync(h, ct))
            return null;
        var channel = h[4];
        var size = BinaryPrimitives.ReadInt32BigEndian(h.AsSpan(5));
        if (HasMagic(h) && IsValidHeader(channel, size))
            return (channel, size);
        Resyncs++;
        return await ResyncAsync(h, ct);
    }

    private async ValueTask<(byte Channel, int Size)?> ResyncAsync(byte[] seed, CancellationToken ct)
    {
        Unget(seed);
        var hdr = new byte[5];
        var match = 0;
        while (true)
        {
            if (match < 4)
            {
                var b = await ReadByteAsync(ct);
                if (b < 0)
                    return null;
                ResyncBytes++;
                match = b == Magic[match] ? match + 1 : b == Magic[0] ? 1 : 0;
                continue;
            }
            match = 0;
            if (!await FillExactAsync(hdr, ct))
                return null;
            var channel = hdr[0];
            var size = BinaryPrimitives.ReadInt32BigEndian(hdr.AsSpan(1));
            if (IsValidHeader(channel, size))
                return (channel, size);
            ResyncBytes += 5;
            Unget(hdr);
        }
    }

    private void Unget(byte[] data)
    {
        for (var i = data.Length - 1; i >= 0; i--)
            _pending[_pendingLen++] = data[i];
    }

    private async ValueTask<int> ReadByteAsync(CancellationToken ct)
    {
        if (_pendingLen > 0)
            return _pending[--_pendingLen];
        if (_pos == _len)
        {
            _len = await _stream.ReadAsync(_buf, ct);
            _pos = 0;
            if (_len == 0)
                return -1;
        }
        return _buf[_pos++];
    }
}
