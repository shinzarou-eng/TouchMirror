using System.Collections.Generic;
using System.IO;

namespace TouchMirror.QuickTime;

internal sealed class QtFramer
{
    private readonly MemoryStream _pending = new();

    public List<byte[]> Feed(ReadOnlySpan<byte> chunk)
    {
        var frames = new List<byte[]>();
        _pending.Write(chunk);
        var buf = _pending.GetBuffer();
        int offset = 0;
        while (true)
        {
            int available = (int)_pending.Length - offset;
            if (available < 4) break;
            int frameLen = (int)QtBin.U32(buf.AsSpan(offset));
            if (frameLen < 4 || available < frameLen) break;
            frames.Add(buf.AsSpan(offset + 4, frameLen - 4).ToArray());
            offset += frameLen;
        }
        if (offset > 0)
        {
            var rest = _pending.GetBuffer().AsSpan(offset, (int)_pending.Length - offset).ToArray();
            _pending.SetLength(0);
            _pending.Write(rest);
        }
        return frames;
    }
}
