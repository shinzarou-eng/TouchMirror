using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using TouchMirror.Services;

namespace TouchMirror.Video;

public sealed unsafe class VideoDecoder : IDisposable, IFrameSource
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    private AVCodecContext* _ctx;
    private readonly AVPacket* _packet;
    private readonly AVFrame* _frame;
    private AVFrame* _swFrame;
    private AVBufferRef* _hwDeviceCtx;
    private int _hwFailCount;
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

    public event Action<IntPtr, int, int, int>? GpuFrame;

    public GpuPresenter? GpuPresenter { get; }

    private IntPtr ExternalD3D11Device => GpuPresenter != null ? GpuPresenter.SharedDevicePtr : IntPtr.Zero;

    public int Width => _frameW;
    public int Height => _frameH;

    /// <summary>Vrai dès qu'une frame a été décodée par le GPU (D3D11VA).</summary>
    public bool HardwareDecoding { get; private set; }

    public int HardwareFallbacks => _hwFailCount;

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

    public VideoDecoder(string codecId, bool preferHardware = true, GpuPresenter? gpuPresenter = null)
    {
        InitializeFFmpeg();
        GpuPresenter = gpuPresenter;

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
        _avCodecId = avCodecId;

        if (preferHardware)
            OpenContext(codec, hw: true);
        if (_ctx == null)
            OpenContext(codec, hw: false);
        if (_ctx == null)
            throw new InvalidOperationException("avcodec_open2 a échoué");

        _packet = ffmpeg.av_packet_alloc();
        _frame = ffmpeg.av_frame_alloc();
    }

    private readonly AVCodecID _avCodecId;

    /// <summary>Ouvre le contexte de décodage, GPU (D3D11VA) si hw=true.</summary>
    private void OpenContext(AVCodec* codec, bool hw)
    {
        var ctx = ffmpeg.avcodec_alloc_context3(codec);
        ctx->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
        ctx->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;
        ctx->thread_count = Math.Min(Environment.ProcessorCount, 8);
        ctx->thread_type = ffmpeg.FF_THREAD_SLICE;
        ctx->delay = 0;

        if (hw)
        {
            AVBufferRef* dev = null;
            if (ExternalD3D11Device != IntPtr.Zero)
            {
                dev = ffmpeg.av_hwdevice_ctx_alloc(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
                if (dev != null)
                {
                    var hwctx = (AVD3D11VADeviceContext*)((AVHWDeviceContext*)dev->data)->hwctx;
                    hwctx->device = (ID3D11Device*)ExternalD3D11Device;
                    if (ffmpeg.av_hwdevice_ctx_init(dev) < 0)
                        ffmpeg.av_buffer_unref(&dev);
                }
            }
            else if (ffmpeg.av_hwdevice_ctx_create(&dev,
                    AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0) < 0)
            {
                dev = null;
            }
            if (dev != null)
            {
                _hwDeviceCtx = dev;
                ctx->hw_device_ctx = ffmpeg.av_buffer_ref(dev);
                ctx->get_format = _getFormatCallback;
            }
        }

        var ret = ffmpeg.avcodec_open2(ctx, codec, null);
        if (ret < 0)
        {
            // ouverture GPU impossible → contexte propre pour le repli logiciel
            ffmpeg.avcodec_free_context(&ctx);
            _ctx = null;
            if (_hwDeviceCtx != null)
            {
                fixed (AVBufferRef** p = &_hwDeviceCtx)
                    ffmpeg.av_buffer_unref(p);
            }
            return;
        }
        _ctx = ctx;
        _hwFailCount = 0;
    }

    // static : le délégué doit rester raciné tant qu'un décodeur vit
    private static readonly AVCodecContext_get_format _getFormatCallback = SelectHwFormat;

    /// <summary>Préfère le format GPU quand le décodeur le propose ; sinon premier format (logiciel).</summary>
    private static AVPixelFormat SelectHwFormat(AVCodecContext* s, AVPixelFormat* fmts)
    {
        for (var p = fmts; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
            if (*p == AVPixelFormat.AV_PIX_FMT_D3D11)
                return AVPixelFormat.AV_PIX_FMT_D3D11;
        return fmts[0];
    }

    /// <summary>Repli définitif vers le décodage logiciel après échecs GPU répétés.</summary>
    private void ReopenSoftware()
    {
        GpuPresenter?.ClearSources();
        fixed (AVCodecContext** c = &_ctx)
            ffmpeg.avcodec_free_context(c);
        if (_hwDeviceCtx != null)
        {
            fixed (AVBufferRef** p = &_hwDeviceCtx)
                ffmpeg.av_buffer_unref(p);
        }
        var codec = ffmpeg.avcodec_find_decoder(_avCodecId);
        if (codec != null)
            OpenContext(codec, hw: false);
        Error?.Invoke(LocalizationService.Get("log.hw_fallback"));
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

                    var src = _frame;
                    if (_frame->format == (int)AVPixelFormat.AV_PIX_FMT_D3D11)
                    {
                        if (GpuFrame != null)
                        {
                            if (!HardwareDecoding)
                            {
                                HardwareDecoding = true;
                                Error?.Invoke(LocalizationService.Get("log.hw_decode"));
                            }
                            GpuFrame.Invoke((IntPtr)_frame->data[0],
                                (int)(IntPtr)_frame->data[1], _frame->width, _frame->height);
                            continue;
                        }
                        // frame en mémoire GPU → copie vers mémoire système
                        if (_swFrame == null)
                            _swFrame = ffmpeg.av_frame_alloc();
                        ffmpeg.av_frame_unref(_swFrame);
                        if (ffmpeg.av_hwframe_transfer_data(_swFrame, _frame, 0) < 0)
                        {
                            if (++_hwFailCount > 30)
                                ReopenSoftware();
                            continue;
                        }
                        _hwFailCount = 0;
                        if (!HardwareDecoding)
                        {
                            HardwareDecoding = true;
                            Error?.Invoke(LocalizationService.Get("log.hw_decode"));
                        }
                        src = _swFrame;
                    }
                    ConvertAndPublish(src);
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex.Message);
            }
        }
    }

    /// <summary>swscale multithread : la conversion YUV→BGRA est le poste CPU dominant du pipeline.</summary>
    private static SwsContext* CreateSws(int w, int h, AVPixelFormat fmt)
    {
        var ctx = ffmpeg.sws_alloc_context();
        if (ctx == null)
            return null;
        ffmpeg.av_opt_set_int(ctx, "srcw", w, 0);
        ffmpeg.av_opt_set_int(ctx, "srch", h, 0);
        ffmpeg.av_opt_set_int(ctx, "dstw", w, 0);
        ffmpeg.av_opt_set_int(ctx, "dsth", h, 0);
        ffmpeg.av_opt_set_int(ctx, "src_format", (long)fmt, 0);
        ffmpeg.av_opt_set_int(ctx, "dst_format", (long)AVPixelFormat.AV_PIX_FMT_BGRA, 0);
        ffmpeg.av_opt_set_int(ctx, "sws_flags", (long)SwsFlags.SWS_BILINEAR, 0);
        ffmpeg.av_opt_set_int(ctx, "threads", Math.Min(Environment.ProcessorCount, 4), 0);
        if (ffmpeg.sws_init_context(ctx, null, null) < 0)
        {
            // options non reconnues par ce build → contexte classique mono-thread
            ffmpeg.sws_freeContext(ctx);
            return ffmpeg.sws_getContext(w, h, fmt, w, h,
                AVPixelFormat.AV_PIX_FMT_BGRA, (int)SwsFlags.SWS_BILINEAR, null, null, null);
        }
        return ctx;
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
            _sws = CreateSws(w, h, fmt);
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
            fixed (AVFrame** f = &_swFrame)
                ffmpeg.av_frame_free(f);
            fixed (AVCodecContext** c = &_ctx)
                ffmpeg.avcodec_free_context(c);
            if (_hwDeviceCtx != null)
            {
                fixed (AVBufferRef** p = &_hwDeviceCtx)
                    ffmpeg.av_buffer_unref(p);
            }
        }
    }
}