using System;

namespace TouchMirror.QuickTime;

internal interface ISampleConsumer
{
    void Consume(CMSampleBuffer buf);
}

internal sealed class QtSession
{
    private readonly Action<byte[]> _write;
    private readonly Action<string>? _log;
    private readonly ISampleConsumer _consumer;
    private readonly bool _audioOnly;

    private CMClock? _clock;
    private CMClock? _localAudioClock;
    private ulong _deviceAudioClockRef;
    private byte[]? _needMessage;
    private int _audioSamples;
    private int _videoSamples;
    private bool _firstAudioTimeTaken;
    private CMTime _startDeviceAudioTime;
    private CMTime _startLocalAudioTime;
    private CMTime _lastEatDeviceTime;
    private CMTime _lastEatLocalTime;
    private int _releasesSeen;

    public event Action? Stopped;
    public event Action<int>? Released;

    public QtSession(Action<byte[]> write, ISampleConsumer consumer, bool audioOnly = false, Action<string>? log = null)
    {
        _write = write;
        _consumer = consumer;
        _audioOnly = audioOnly;
        _log = log;
    }

    public void ReceiveFrame(ReadOnlySpan<byte> data)
    {
        switch (QtBin.U32(data))
        {
            case QtMagic.Ping:
                _log?.Invoke("PING");
                _write(QtPackets.PingBytes());
                return;
            case QtMagic.Sync:
                HandleSync(data);
                return;
            case QtMagic.Asyn:
                HandleAsyn(data);
                return;
            default:
                _log?.Invoke($"unknown packet '{QtBin.TagName(QtBin.U32(data))}'");
                Stopped?.Invoke();
                return;
        }
    }

    private void HandleSync(ReadOnlySpan<byte> data)
    {
        switch (QtBin.U32(data[12..]))
        {
            case QtMagic.Og:
                var og = SyncOgPacket.Parse(data);
                _write(og.NewReply());
                break;
            case QtMagic.Cwpa:
                var cwpa = SyncCwpaPacket.Parse(data);
                ulong audioRef = cwpa.DeviceClockRef + 1000;
                _localAudioClock = CMClock.WithHostTime(audioRef);
                _deviceAudioClockRef = cwpa.DeviceClockRef;
                if (!_audioOnly)
                {
                    var hpd1 = QtPackets.AsynHpd1Bytes();
                    _write(hpd1);
                    _write(hpd1);
                }
                _write(cwpa.NewReply(audioRef));
                _write(QtPackets.AsynHpa1Bytes(cwpa.DeviceClockRef));
                break;
            case QtMagic.Cvrp:
                var cvrp = SyncCvrpPacket.Parse(data);
                _needMessage = QtPackets.AsynNeedBytes(cvrp.DeviceClockRef);
                _write(_needMessage);
                _write(cvrp.NewReply(cvrp.DeviceClockRef + 0x1000AF));
                break;
            case QtMagic.Clok:
                var clok = SyncClokPacket.Parse(data);
                ulong clokRef = clok.ClockRef + 0x10000;
                _clock = CMClock.WithHostTime(clokRef);
                _write(clok.NewReply(clokRef));
                break;
            case QtMagic.Time:
                var time = SyncTimePacket.Parse(data);
                _write(time.NewReply(_clock?.GetTime() ?? new CMTime()));
                break;
            case QtMagic.Afmt:
                var afmt = SyncAfmtPacket.Parse(data);
                _write(afmt.NewReply());
                break;
            case QtMagic.Skew:
                var skew = SyncSkewPacket.Parse(data);
                double skewValue = CMClock.CalculateSkew(
                    _startLocalAudioTime, _lastEatLocalTime,
                    _startDeviceAudioTime, _lastEatDeviceTime);
                _write(skew.NewReply(skewValue));
                break;
            case QtMagic.Stop:
                var stop = SyncStopPacket.Parse(data);
                _write(stop.NewReply());
                break;
            default:
                _log?.Invoke($"unknown sync packet '{QtBin.TagName(QtBin.U32(data[12..]))}'");
                Stopped?.Invoke();
                break;
        }
    }

    private void HandleAsyn(ReadOnlySpan<byte> data)
    {
        switch (QtBin.U32(data[12..]))
        {
            case QtMagic.Eat:
                var eat = AsynSampleBufPacket.Parse(data);
                _audioSamples++;
                TrackAudioClock(eat);
                _consumer.Consume(eat.SampleBuf!);
                break;
            case QtMagic.Feed:
                try
                {
                    var feed = AsynSampleBufPacket.Parse(data);
                    _videoSamples++;
                    _consumer.Consume(feed.SampleBuf!);
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"feed parse: {ex.Message}");
                }
                if (_needMessage != null)
                    _write(_needMessage);
                break;
            case QtMagic.Sprp:
                _ = AsynSprpPacket.Parse(data);
                break;
            case QtMagic.Tjmp:
                _ = AsynTjmpPacket.Parse(data);
                break;
            case QtMagic.Srat:
                _ = AsynSratPacket.Parse(data);
                break;
            case QtMagic.Tbas:
                _ = AsynTbasPacket.Parse(data);
                break;
            case QtMagic.Rels:
                var rels = AsynRelsPacket.Parse(data);
                _releasesSeen++;
                Released?.Invoke(_releasesSeen);
                break;
            default:
                _log?.Invoke($"unknown asyn packet '{QtBin.TagName(QtBin.U32(data[12..]))}'");
                Stopped?.Invoke();
                break;
        }
    }

    private void TrackAudioClock(AsynSampleBufPacket eat)
    {
        var pts = eat.SampleBuf!.OutputPresentationTimestamp;
        if (!_firstAudioTimeTaken)
        {
            _startDeviceAudioTime = pts;
            _startLocalAudioTime = _localAudioClock?.GetTime() ?? new CMTime();
            _lastEatDeviceTime = pts;
            _lastEatLocalTime = _startLocalAudioTime;
            _firstAudioTimeTaken = true;
        }
        else
        {
            _lastEatDeviceTime = pts;
            _lastEatLocalTime = _localAudioClock?.GetTime() ?? new CMTime();
        }
    }

    public void CloseSession()
    {
        _write(QtPackets.AsynHpa0Bytes(_deviceAudioClockRef));
        _write(QtPackets.AsynHpd0Bytes());
    }

    public void FinishClose()
        => _write(QtPackets.AsynHpd0Bytes());
}
