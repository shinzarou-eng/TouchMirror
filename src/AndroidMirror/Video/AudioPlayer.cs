using FFmpeg.AutoGen;
using NAudio.Wave;

namespace TouchMirror.Video;

public sealed unsafe class AudioPlayer : IDisposable
{
    private readonly AVCodecContext* _ctx;
    private readonly AVPacket* _packet;
    private readonly AVFrame* _frame;
    private SwrContext* _swr;

    private readonly BufferedWaveProvider _provider;
    private readonly WaveOutEvent _output;

    private bool _opened;
    private byte[]? _pendingConfig;
    private readonly object _sync = new();
    private bool _disposed;

    public event Action<string>? Error;

    public float Volume
    {
        get => _output.Volume;
        set => _output.Volume = Math.Clamp(value, 0f, 1f);
    }

    public AudioPlayer(string codecId)
    {
        VideoDecoder.InitializeFFmpeg();

        var avCodecId = codecId switch
        {
            "aac" => AVCodecID.AV_CODEC_ID_AAC,
            "aaceld" => AVCodecID.AV_CODEC_ID_AAC,
            "alac" => AVCodecID.AV_CODEC_ID_ALAC,
            "flac" => AVCodecID.AV_CODEC_ID_FLAC,
            "raw" => AVCodecID.AV_CODEC_ID_PCM_S16LE,
            _ => AVCodecID.AV_CODEC_ID_OPUS,
        };

        var codec = ffmpeg.avcodec_find_decoder(avCodecId);
        if (codec == null)
            throw new InvalidOperationException($"Codec audio introuvable : {codecId}");

        _ctx = ffmpeg.avcodec_alloc_context3(codec);
        _ctx->request_sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLTP;

        _packet = ffmpeg.av_packet_alloc();
        _frame = ffmpeg.av_frame_alloc();

        _provider = new BufferedWaveProvider(new WaveFormat(48000, 16, 2))
        {
            BufferDuration = TimeSpan.FromMilliseconds(200),
            DiscardOnBufferOverflow = true
        };
        _output = new WaveOutEvent { DesiredLatency = 60 };
        _output.Init(_provider);
        _output.Play();
    }

    private void TryOpen()
    {
        if (_opened || _openFailed)
            return;

        if (_pendingConfig != null)
        {
            var cfg = _pendingConfig;
            _ctx->extradata = (byte*)ffmpeg.av_malloc((nuint)(cfg.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE));
            System.Runtime.InteropServices.Marshal.Copy(cfg, 0, (IntPtr)_ctx->extradata, cfg.Length);
            _ctx->extradata_size = cfg.Length;
            _ctx->sample_rate = 44100;
            AVChannelLayout cfgLayout;
            ffmpeg.av_channel_layout_default(&cfgLayout, 2);
            _ctx->ch_layout = cfgLayout;
        }
        else
        {
            _ctx->sample_rate = 48000;
            AVChannelLayout layout;
            ffmpeg.av_channel_layout_default(&layout, 2);
            _ctx->ch_layout = layout;
        }

        var ret = ffmpeg.avcodec_open2(_ctx, null, null);
        if (ret < 0)
        {
            _openFailed = true;
            Error?.Invoke($"avcodec_open2 audio a échoué ({ret})");
            return;
        }
        _opened = true;
    }

    private bool _openFailed;
    private int _sendErrors;
    private int _recvEmpty;
    private int _framesDecoded;

    public void Feed(byte[] data, bool isConfig, int len)
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            if (isConfig)
            {
                _pendingConfig = data.AsSpan(0, len).ToArray();
                TryOpen();
                return;
            }
            if (!_opened)
                TryOpen();
            if (!_opened)
                return;

            try
            {
                var ret = ffmpeg.av_new_packet(_packet, len);
                if (ret < 0)
                    return;
                System.Runtime.InteropServices.Marshal.Copy(data, 0, (IntPtr)_packet->data, len);

                ret = ffmpeg.avcodec_send_packet(_ctx, _packet);
                ffmpeg.av_packet_unref(_packet);
                if (ret < 0)
                {
                    if (++_sendErrors <= 3 || _sendErrors % 200 == 0)
                        Error?.Invoke($"send_packet audio ({ret}) x{_sendErrors}");
                    return;
                }

                while (true)
                {
                    ret = ffmpeg.avcodec_receive_frame(_ctx, _frame);
                    if (ret != 0)
                    {
                        if (ret != ffmpeg.AVERROR(ffmpeg.EAGAIN) && ret != ffmpeg.AVERROR_EOF
                            && (++_recvEmpty <= 3 || _recvEmpty % 200 == 0))
                            Error?.Invoke($"receive_frame audio ({ret}) x{_recvEmpty}");
                        break;
                    }
                    if (_framesDecoded++ == 0)
                        Error?.Invoke($"audio decode: {_frame->nb_samples}ech {_frame->sample_rate}Hz fmt={_frame->format}");
                    ResampleAndPlay(_frame);
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex.Message);
            }
        }
    }

    private byte[]? _resampleBuf;

    private void ResampleAndPlay(AVFrame* f)
    {
        if (_swr == null)
        {
            AVChannelLayout outLayout;
            ffmpeg.av_channel_layout_default(&outLayout, 2);
            SwrContext* swr;
            var ret = ffmpeg.swr_alloc_set_opts2(&swr,
                &outLayout, AVSampleFormat.AV_SAMPLE_FMT_S16, 48000,
                &f->ch_layout, (AVSampleFormat)f->format, f->sample_rate,
                0, null);
            if (ret < 0 || swr == null || ffmpeg.swr_init(swr) < 0)
            {
                Error?.Invoke($"swr_init a échoué ({ret}) in_ch={f->ch_layout.nb_channels} in_sr={f->sample_rate} in_fmt={f->format}");
                return;
            }
            _swr = swr;
            Error?.Invoke($"swr ok: {f->sample_rate}Hz/{f->ch_layout.nb_channels}ch fmt={f->format} -> 48000Hz/2ch s16");
        }

        var outSamples = ffmpeg.swr_get_out_samples(_swr, f->nb_samples) + 64;
        var bufSize = outSamples * 2 * 2;
        if (_resampleBuf == null || _resampleBuf.Length < bufSize + 256)
            _resampleBuf = new byte[bufSize + 256];
        var buffer = _resampleBuf;
        fixed (byte* dst = buffer)
        {
            var dstSlice = stackalloc byte*[] { dst };
            var converted = ffmpeg.swr_convert(_swr, dstSlice, outSamples, f->extended_data, f->nb_samples);
            if (converted > 0)
            {
                _provider.AddSamples(buffer, 0, converted * 4);
                if (_pcmDump != null)
                {
                    _pcmDump.Write(buffer, 0, converted * 4);
                    if (++_pcmDumpCount % 500 == 0)
                        Error?.Invoke($"pcm dump {_pcmDumpCount} blocs, provider={_provider.BufferedBytes}o");
                }
            }
            else if (++_swrEmpty <= 3 || _swrEmpty % 200 == 0)
                Error?.Invoke($"swr_convert audio ({converted}) x{_swrEmpty}");
        }
    }

    private int _swrEmpty;
    private readonly System.IO.FileStream? _pcmDump =
        System.Environment.GetEnvironmentVariable("TM_DUMP_AUDIO") != null
            ? System.IO.File.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pcm-out.raw"))
            : null;
    private int _pcmDumpCount;

    public void Dispose()
    {
        try { _output.Stop(); _output.Dispose(); } catch { }
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_swr != null)
            {
                SwrContext* swr = _swr;
                ffmpeg.swr_free(&swr);
                _swr = null;
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
