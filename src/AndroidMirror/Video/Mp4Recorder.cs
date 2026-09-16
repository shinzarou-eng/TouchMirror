using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace TouchMirror.Video;

public unsafe sealed class Mp4Recorder : IDisposable
{
    private readonly AVCodecID _codecId;
    private AVFormatContext* _fmt;
    private AVStream* _stream;
    private bool _headerWritten;
    public bool HeaderWritten => _headerWritten;
    private bool _disposed;
    private long _ptsOffset = -1;

    public Mp4Recorder(string path, int width, int height, string codecId)
    {
        _codecId = codecId switch
        {
            "h265" => AVCodecID.AV_CODEC_ID_HEVC,
            "av1" => AVCodecID.AV_CODEC_ID_AV1,
            _ => AVCodecID.AV_CODEC_ID_H264,
        };

        fixed (AVFormatContext** ctx = &_fmt)
            if (ffmpeg.avformat_alloc_output_context2(ctx, null, "mp4", path) < 0 || _fmt == null)
                throw new InvalidOperationException("avformat_alloc_output_context2 a échoué");

        _stream = ffmpeg.avformat_new_stream(_fmt, null);
        _stream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        _stream->codecpar->codec_id = _codecId;
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

        byte[]? extra = _codecId switch
        {
            AVCodecID.AV_CODEC_ID_HEVC => BuildHvcC(annexb),
            AVCodecID.AV_CODEC_ID_AV1 => BuildAv1C(annexb),
            _ => BuildAvcC(annexb),
        };
        if (extra == null)
            return;

        var ptr = (byte*)ffmpeg.av_mallocz((ulong)(extra.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE));
        Marshal.Copy(extra, 0, (IntPtr)ptr, extra.Length);
        _stream->codecpar->extradata = ptr;
        _stream->codecpar->extradata_size = extra.Length;

        var r = ffmpeg.avformat_write_header(_fmt, null);
        _headerWritten = r >= 0;
        Services.AppLogger.Write($"mp4: header codec={_codecId} write_header={r}");
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

        var data = _codecId == AVCodecID.AV_CODEC_ID_AV1
            ? ToAv1Sample(annexb)
            : ToLengthPrefixed(annexb);
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

    private static byte[] ToLengthPrefixed(byte[] annexb)
    {
        var ms = new MemoryStream();
        foreach (var nal in SplitAnnexB(annexb))
        {
            ms.WriteByte((byte)(nal.Length >> 24));
            ms.WriteByte((byte)(nal.Length >> 16));
            ms.WriteByte((byte)(nal.Length >> 8));
            ms.WriteByte((byte)nal.Length);
            ms.Write(nal, 0, nal.Length);
        }
        return ms.ToArray();
    }

    private static byte[] ToAv1Sample(byte[] data)
    {
        var start = FindFirstObu(data);
        if (start < 0)
            return Array.Empty<byte>();
        var ms = new MemoryStream();
        var i = start;
        while (i < data.Length)
        {
            if (!TryReadObu(data, i, out int type, out int len))
                break;
            if (type != 2)
                ms.Write(data, i, len);
            i += len;
        }
        return ms.ToArray();
    }

    private static int FindFirstObu(byte[] d)
    {
        for (var i = 0; i < d.Length; i++)
        {
            if (!TryReadObu(d, i, out _, out int len))
                continue;
            var next = i + len;
            if (next >= d.Length || TryReadObu(d, next, out _, out _))
                return i;
        }
        return -1;
    }

    private static bool TryReadObu(byte[] d, int i, out int type, out int len)
    {
        type = -1; len = 0;
        if (i >= d.Length || (d[i] & 0x80) != 0)
            return false;
        type = (d[i] >> 3) & 0xF;
        bool ext = (d[i] & 0x04) != 0;
        bool hasSize = (d[i] & 0x02) != 0;
        int p = i + 1 + (ext ? 1 : 0);
        if (!hasSize)
            return false;
        long size = 0; int sh = 0;
        while (p < d.Length)
        {
            size |= (long)(d[p] & 0x7f) << sh;
            if ((d[p++] & 0x80) == 0) break;
            sh += 7;
        }
        len = p - i + (int)size;
        return i + len <= d.Length;
    }

    private static byte[]? BuildAv1C(byte[] data)
    {
        byte[]? seqHeader = null;
        var i = FindFirstObu(data);
        if (i < 0)
            return null;
        while (i < data.Length)
        {
            if (!TryReadObu(data, i, out int type, out int len))
                break;
            if (type == 1)
            {
                seqHeader = data[i..(i + len)];
                break;
            }
            i += len;
        }
        if (seqHeader == null)
            return null;

        var br = new BitReader(seqHeader, 8);
        var profile = br.Read(3);
        br.Read(1); // still_picture
        var reduced = br.Read(1);
        int level = 0, tier = 0;
        if (reduced == 1)
        {
            level = br.Read(5);
            if (level > 7) tier = br.Read(1);
        }
        else
        {
            if (br.Read(1) == 1) // timing_info_present_flag
            {
                br.Read(32); br.Read(32);
                if (br.Read(1) == 1) br.ReadUv();
            }
            if (br.Read(1) == 1) // decoder_model_info_present_flag
            {
                br.Read(5); br.Read(32); br.Read(32); br.Read(32);
                br.Read(1); br.Read(1);
            }
            br.Read(1); // initial_display_delay_present_flag
            var opCount = br.Read(5) + 1;
            for (var op = 0; op < opCount; op++)
            {
                br.Read(12); // operating_point_idc
                var lvl = br.Read(5);
                if (op == 0) level = lvl;
                if (lvl > 7)
                {
                    var t = br.Read(1);
                    if (op == 0) tier = t;
                }
            }
        }
        if (profile < 0 || level < 0)
            return null;

        var ms = new MemoryStream();
        ms.WriteByte(0x81);
        ms.WriteByte((byte)((profile << 5) | level));
        ms.WriteByte((byte)((tier << 7) | 0x0C));
        ms.WriteByte(0);
        ms.Write(seqHeader, 0, seqHeader.Length);
        return ms.ToArray();
    }

    private static byte[]? BuildAvcC(byte[] annexb)
    {
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
            return null;

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
        return extra.ToArray();
    }

    private static byte[]? BuildHvcC(byte[] annexb)
    {
        var vps = new List<byte[]>();
        var sps = new List<byte[]>();
        var pps = new List<byte[]>();
        foreach (var nal in SplitAnnexB(annexb))
        {
            if (nal.Length < 2)
                continue;
            var type = (nal[0] >> 1) & 0x3F;
            if (type == 32) vps.Add(nal);
            else if (type == 33) sps.Add(nal);
            else if (type == 34) pps.Add(nal);
        }
        if (vps.Count == 0 || sps.Count == 0 || pps.Count == 0)
            return null;

        var rbsp = RemoveEmulationPrevention(sps[0]);
        // nal header 16 bits + sps_video_parameter_set_id 4 + max_sub_layers_minus1 3
        // + temporal_id_nesting 1 → profile_tier_level démarre à l'octet 3
        if (rbsp.Length < 15)
            return null;
        var ptl = rbsp[3..15];

        var ms = new MemoryStream();
        ms.WriteByte(1);          // configurationVersion
        ms.WriteByte(ptl[0]);     // profile_space + tier + profile_idc
        ms.Write(ptl, 1, 4);      // profile_compatibility_flags
        ms.Write(ptl, 5, 6);      // constraint_indicator_flags
        ms.WriteByte(ptl[11]);    // level_idc
        ms.WriteByte(0xF0); ms.WriteByte(0x00); // min_spatial_segmentation_idc
        ms.WriteByte(0xFC);       // parallelismType
        ms.WriteByte(0xFD);       // chromaFormat = 1 (4:2:0)
        ms.WriteByte(0xF8);       // bitDepthLumaMinus8
        ms.WriteByte(0xF8);       // bitDepthChromaMinus8
        ms.WriteByte(0); ms.WriteByte(0);       // avgFrameRate
        ms.WriteByte(0x0B);       // numTemporalLayers=1, lengthSizeMinusOne=3
        ms.WriteByte(3);          // numOfArrays
        foreach (var (type, list) in new (byte, List<byte[]>)[] { ((byte)32, vps), (33, sps), (34, pps) })
        {
            ms.WriteByte((byte)(0x80 | type));
            ms.WriteByte((byte)(list.Count >> 8));
            ms.WriteByte((byte)list.Count);
            foreach (var n in list)
            {
                ms.WriteByte((byte)(n.Length >> 8));
                ms.WriteByte((byte)n.Length);
                ms.Write(n, 0, n.Length);
            }
        }
        return ms.ToArray();
    }

    private static byte[] RemoveEmulationPrevention(byte[] nal)
    {
        var ms = new MemoryStream(nal.Length);
        var zeros = 0;
        foreach (var b in nal)
        {
            if (zeros == 2 && b == 0x03)
            {
                zeros = 0;
                continue;
            }
            zeros = b == 0 ? zeros + 1 : 0;
            ms.WriteByte(b);
        }
        return ms.ToArray();
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

    private sealed class BitReader
    {
        private readonly byte[] _d;
        private int _pos;
        public BitReader(byte[] data, int skipBits) { _d = data; _pos = skipBits; }
        public int Read(int bits)
        {
            var v = 0;
            for (var i = 0; i < bits; i++)
            {
                var byteIdx = _pos >> 3;
                if (byteIdx >= _d.Length) return -1;
                v = (v << 1) | ((_d[byteIdx] >> (7 - (_pos & 7))) & 1);
                _pos++;
            }
            return v;
        }
        public int ReadUv()
        {
            var zeros = 0;
            while (Read(1) == 0 && zeros < 32) zeros++;
            return zeros == 0 ? 0 : (1 << zeros) - 1 + Read(zeros);
        }
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
