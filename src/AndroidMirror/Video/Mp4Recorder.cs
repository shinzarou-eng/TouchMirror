using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace TouchMirror.Video;

public unsafe sealed class Mp4Recorder : IDisposable
{
    private AVFormatContext* _fmt;
    private AVStream* _stream;
    private bool _headerWritten;
    private bool _disposed;
    private long _ptsOffset = -1;

    public Mp4Recorder(string path, int width, int height)
    {
        fixed (AVFormatContext** ctx = &_fmt)
            if (ffmpeg.avformat_alloc_output_context2(ctx, null, "mp4", path) < 0 || _fmt == null)
                throw new InvalidOperationException("avformat_alloc_output_context2 a échoué");

        _stream = ffmpeg.avformat_new_stream(_fmt, null);
        _stream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        _stream->codecpar->codec_id = AVCodecID.AV_CODEC_ID_H264;
        _stream->codecpar->width = width;
        _stream->codecpar->height = height;
        _stream->time_base = new AVRational { num = 1, den = 1_000_000 };

        AVIOContext* io = null;
        if (ffmpeg.avio_open(&io, path, ffmpeg.AVIO_FLAG_WRITE) < 0 || io == null)
            throw new IOException($"avio_open a échoué : {path}");
        _fmt->pb = io;
    }

    public void WriteConfig(byte[] annexb)
    {
        if (_headerWritten || _disposed)
            return;

        var sps = new List<byte[]>();
        var pps = new List<byte[]>();
        foreach (var nal in SplitAnnexB(annexb))
        {
            if (nal.Length < 2)
                continue;
            var type = nal[0] & 0x1F;
            if (type == 7) sps.Add(nal);
            else if (type == 8) pps.Add(nal);
        }
        if (sps.Count == 0 || pps.Count == 0)
            return;

        var s = sps[0];
        var extra = new List<byte> { 1, s[1], s[2], s[3], 0xFF, (byte)(0xE0 | sps.Count) };
        foreach (var n in sps)
        {
            extra.Add((byte)(n.Length >> 8));
            extra.Add((byte)n.Length);
            extra.AddRange(n);
        }
        extra.Add((byte)pps.Count);
        foreach (var n in pps)
        {
            extra.Add((byte)(n.Length >> 8));
            extra.Add((byte)n.Length);
            extra.AddRange(n);
        }

        var buf = extra.ToArray();
        var ptr = (byte*)ffmpeg.av_malloc((ulong)buf.Length);
        Marshal.Copy(buf, 0, (IntPtr)ptr, buf.Length);
        _stream->codecpar->extradata = ptr;
        _stream->codecpar->extradata_size = buf.Length;

        var r = ffmpeg.avformat_write_header(_fmt, null);
        _headerWritten = r >= 0;
        Services.AppLogger.Write($"mp4: header sps={sps.Count} pps={pps.Count} write_header={r}");
    }

    private int _dbgPackets;
    public void WritePacket(byte[] annexb, long ptsUs, bool keyframe)
    {
        if (!_headerWritten || _disposed)
        {
            if (_dbgPackets++ == 0) Services.AppLogger.Write($"mp4: packet ignoré (header={_headerWritten})");
            return;
        }
        if (_ptsOffset < 0)
            _ptsOffset = ptsUs;

        var avcc = new MemoryStream();
        foreach (var nal in SplitAnnexB(annexb))
        {
            avcc.WriteByte((byte)(nal.Length >> 24));
            avcc.WriteByte((byte)(nal.Length >> 16));
            avcc.WriteByte((byte)(nal.Length >> 8));
            avcc.WriteByte((byte)nal.Length);
            avcc.Write(nal, 0, nal.Length);
        }
        var data = avcc.ToArray();
        if (data.Length == 0)
            return;

        var pkt = ffmpeg.av_packet_alloc();
        try
        {
            if (ffmpeg.av_new_packet(pkt, data.Length) < 0)
                return;
            Marshal.Copy(data, 0, (IntPtr)pkt->data, data.Length);
            pkt->stream_index = _stream->index;
            var t = ptsUs - _ptsOffset;
            pkt->pts = t;
            pkt->dts = t;
            if (keyframe)
                pkt->flags |= ffmpeg.AV_PKT_FLAG_KEY;
            ffmpeg.av_interleaved_write_frame(_fmt, pkt);
        }
        finally
        {
            var p = pkt;
            ffmpeg.av_packet_free(&p);
        }
    }

    private static IEnumerable<byte[]> SplitAnnexB(byte[] buf)
    {
        var i = 0;
        var start = -1;
        while (i < buf.Length - 2)
        {
            var isStart = buf[i] == 0 && buf[i + 1] == 0 &&
                          (buf[i + 2] == 1 || (buf[i + 2] == 0 && i + 3 < buf.Length && buf[i + 3] == 1));
            if (isStart)
            {
                if (start >= 0)
                    yield return buf[start..i];
                i += buf[i + 2] == 1 ? 3 : 4;
                start = i;
            }
            else
            {
                i++;
            }
        }
        if (start >= 0 && start < buf.Length)
            yield return buf[start..];
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { if (_headerWritten) ffmpeg.av_write_trailer(_fmt); } catch { }
        try
        {
            if (_fmt != null && _fmt->pb != null)
            {
                var pb = _fmt->pb;
                ffmpeg.avio_closep(&pb);
                _fmt->pb = null;
            }
        }
        catch { }
        try
        {
            if (_fmt != null)
            {
                var c = _fmt;
                ffmpeg.avformat_free_context(c);
                _fmt = null;
            }
        }
        catch { }
    }
}