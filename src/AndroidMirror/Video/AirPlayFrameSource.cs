using System.Collections.Concurrent;
using FFmpeg.AutoGen;

namespace TouchMirror.Video;

/// <summary>
/// Frames YUV420P décodées poussées par le process AirPlayHost.
/// Convertit en BGRA via swscale et expose le dernier frame, comme <see cref="VideoDecoder"/>.
/// </summary>
public sealed unsafe class AirPlayFrameSource : IFrameSource, IDisposable
{
    private SwsContext* _sws;
    private int _swsW = -1, _swsH = -1;

    private readonly ConcurrentQueue<byte[]> _pool = new();
    private byte[]? _latest;
    private int _frameW, _frameH;
    private readonly object _sync = new();
    private bool _disposed;

    public int Width => _frameW;
    public int Height => _frameH;

    /// <summary>
    /// data = plans Y|U|V contigus ; dataLen[i] inclut le padding de pitch.
    /// Appelé depuis le thread de lecture du pipe.
    /// </summary>
    public void Publish(int width, int height,
                        uint pitch0, uint pitch1, uint pitch2,
                        uint len0, uint len1, uint len2,
                        byte[] data)
    {
        lock (_sync)
        {
            if (_disposed || width <= 0 || height <= 0)
                return;

            if (width != _swsW || height != _swsH)
            {
                if (_sws != null)
                    ffmpeg.sws_freeContext(_sws);
                _sws = ffmpeg.sws_getContext(width, height, AVPixelFormat.AV_PIX_FMT_YUV420P,
                    width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
                    (int)SwsFlags.SWS_BILINEAR, null, null, null);
                if (_sws == null)
                    return;
                _swsW = width;
                _swsH = height;
                _frameW = width;
                _frameH = height;
            }

            var needed = width * height * 4;
            if (!_pool.TryDequeue(out var buffer) || buffer.Length < needed)
                buffer = new byte[needed + 65536];

            fixed (byte* src = data)
            fixed (byte* dst = buffer)
            {
                var srcSlice = new byte*[]
                {
                    src,
                    src + len0,
                    src + len0 + len1,
                    null
                };
                var srcStride = new[]
                {
                    (int)pitch0, (int)pitch1, (int)pitch2, 0
                };
                var dstSlice = new byte*[] { dst, null, null, null };
                var dstStride = new[] { width * 4, 0, 0, 0 };
                ffmpeg.sws_scale(_sws, srcSlice, srcStride, 0, height, dstSlice, dstStride);
            }

            var previous = Interlocked.Exchange(ref _latest, buffer);
            if (previous != null)
                _pool.Enqueue(previous);
        }
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
        }
    }
}
