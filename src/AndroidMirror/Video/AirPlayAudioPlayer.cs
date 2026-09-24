using NAudio.Wave;

namespace TouchMirror.Video;

public sealed class AirPlayAudioPlayer : IDisposable
{
    private BufferedWaveProvider? _provider;
    private WaveOutEvent? _output;
    private int _rate = -1, _channels = -1, _bits = -1;
    private float _volume = 1f;
    private readonly object _sync = new();
    private bool _disposed;

    public event Action<string>? Error;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            try { if (_output != null) _output.Volume = _volume; } catch { }
        }
    }

    public void Feed(int sampleRate, int channels, int bitsPerSample, byte[] data, int length)
    {
        lock (_sync)
        {
            if (_disposed || data.Length == 0)
                return;
            try
            {
                if (sampleRate != _rate || channels != _channels || bitsPerSample != _bits)
                    Open(sampleRate, channels, bitsPerSample);
                _provider?.AddSamples(data, 0, Math.Min(length, data.Length));
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex.Message);
            }
        }
    }

    private void Open(int sampleRate, int channels, int bitsPerSample)
    {
        if (sampleRate <= 0 || channels <= 0 || bitsPerSample != 16)
        {
            _rate = sampleRate; _channels = channels; _bits = bitsPerSample;
            return;
        }
        try { _output?.Stop(); _output?.Dispose(); } catch { }
        _provider = new BufferedWaveProvider(new WaveFormat(sampleRate, bitsPerSample, channels))
        {
            BufferDuration = TimeSpan.FromMilliseconds(400),
            DiscardOnBufferOverflow = true
        };
        _output = new WaveOutEvent { DesiredLatency = 80, Volume = _volume };
        _output.Init(_provider);
        _output.Play();
        _rate = sampleRate; _channels = channels; _bits = bitsPerSample;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            try { _output?.Stop(); _output?.Dispose(); } catch { }
            _output = null;
            _provider = null;
        }
    }
}
