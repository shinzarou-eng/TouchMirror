using System.Collections.Concurrent;
using System.IO;
using FFmpeg.AutoGen;

namespace TouchMirror.Video;

public sealed unsafe class VideoDecoder : IDisposable
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    private readonly AVCodecContext* _ctx;
    private readonly AVPacket* _packet;
    private readonly AVFrame* _frame;
    private SwsContext* _sws;

    private int _swsW = -1, _swsH = -1;
    private AVPixelFormat _swsFmt = AVPixelFormat.AV_PIX_FMT_NONE;

    private readonly ConcurrentQueue<byte[]> _pool = new();
    private byte[]? _latest;
    private int _frameW, _frameH;
    private readonly object _sync = new();
    private bool _disposed;

    public event Action? FrameAvailable;
    public event Action<string>? Error;

    public int Width => _frameW;
    public int Height => _frameH;

    public static void InitializeFFmpeg()
    {
        lock (_initLock)
        {
            if (_initialized)
                return;
            var dir = Path.Combine(AppContext.BaseDirectory, "assets", "ffmpeg");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException($"DLLs FFmpeg introuvables : {dir}");
            ffmpeg.RootPath = dir;
            _ = ffmpeg.av_version_info();
            _initialized = true;
        }
    }

    public VideoDecoder(string codecId)
    {
        InitializeFFmpeg();

        var avCodecId = codecId switch
        {
            "h265" => AVCodecID.AV_CODEC_ID_HEVC,
            "av1" => AVCodecID.AV_CODEC_ID_AV1,
            "vp8" => AVCodecID.AV_CODEC_ID_VP8,
            "vp9" => AVCodecID.AV_CODEC_ID_VP9,
            _ => AVCodecID.AV_CODEC_ID_H264,
        };

        var codec = ffmpeg.avcodec_find_decoder(avCodecId);
        if (codec == null)
            throw new InvalidOperationException($"Codec FFmpeg introuvable : {codecId}");

        _ctx = ffmpeg.avcodec_alloc_context3(codec);
        _ctx->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
        _ctx->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;
        _ctx->thread_count = Math.Min(Environment.ProcessorCount, 8);
        _ctx->thread_type = ffmpeg.FF_THREAD_SLICE;
        _ctx->delay = 0;

        var ret = ffmpeg.avcodec_open2(_ctx, codec, null);
        if (ret < 0)
            throw new InvalidOperationException($"avcodec_open2 a échoué ({ret})");

        _packet = ffmpeg.av_packet_alloc();
        _frame = ffmpeg.av_frame_alloc();
    }

    public void Feed(byte[] data)
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            try
            {
            var ret = ffmpeg.av_new_packet(_packet, data.Length);
            if (ret < 0)
                return;
            System.Runtime.InteropServices.Marshal.Copy(data, 0, (IntPtr)_packet->data, data.Length);

            ret = ffmpeg.avcodec_send_packet(_ctx, _packet);
            ffmpeg.av_packet_unref(_packet);
            if (ret < 0)
                return;

                while (true)
                {
                    ret = ffmpeg.avcodec_receive_frame(_ctx, _frame);
                    if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                        break;
                    if (ret < 0)
                        break;
                    ConvertAndPublish(_frame);
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex.Message);
            }
        }
    }

    private void ConvertAndPublish(AVFrame* f)
    {
        var w = f->width;
        var h = f->height;
        var fmt = (AVPixelFormat)f->format;
        if (w <= 0 || h <= 0)
            return;

        if (w != _swsW || h != _swsH || fmt != _swsFmt)
        {
            if (_sws != null)
                ffmpeg.sws_freeContext(_sws);
            _sws = ffmpeg.sws_getContext(w, h, fmt, w, h,
                AVPixelFormat.AV_PIX_FMT_BGRA,
                (int)SwsFlags.SWS_BILINEAR,
                null, null, null);
            if (_sws == null)
                return;
            _swsW = w;
            _swsH = h;
            _swsFmt = fmt;
            _frameW = w;
            _frameH = h;
        }

        var needed = w * h * 4;
        if (!_pool.TryDequeue(out var buffer) || buffer.Length < needed)
            buffer = new byte[needed + 65536];

        fixed (byte* dst = buffer)
        {
            var srcSlice = new byte*[] { f->data[0], f->data[1], f->data[2], f->data[3] };
            var srcStride = new[] { f->linesize[0], f->linesize[1], f->linesize[2], f->linesize[3] };
            var dstSlice = new byte*[] { dst, null, null, null };
            var dstStride = new[] { w * 4, 0, 0, 0 };
            ffmpeg.sws_scale(_sws, srcSlice, srcStride, 0, h, dstSlice, dstStride);
        }

        var previous = Interlocked.Exchange(ref _latest, buffer);
        if (previous != null)
            _pool.Enqueue(previous);

        FrameAvailable?.Invoke();
    }

    public bool TryTakeLatest(out byte[]? buffer, out int width, out int height)
    {
        buffer = Interlocked.Exchange(ref _latest, null);
        width = _frameW;
        height = _frameH;
        return buffer != null;
    }

    public void Release(byte[] buffer)
    {
        if (buffer.Length >= _frameW * _frameH * 4)
            _pool.Enqueue(buffer);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_sws != null)
            {
                ffmpeg.sws_freeContext(_sws);
                _sws = null;
            }
            fixed (AVPacket** p = &_packet)
                ffmpeg.av_packet_free(p);
            fixed (AVFrame** f = &_frame)
                ffmpeg.av_frame_free(f);
            fixed (AVCodecContext** c = &_ctx)
                ffmpeg.avcodec_free_context(c);
        }
    }
}