using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TouchMirror.Services;

namespace TouchMirror.Engine;

public sealed record EngineOptions
{
    public int MaxSize { get; init; } = 0;
    public int MaxFps { get; init; } = 60;
    public int VideoBitRate { get; init; } = 8_000_000;
    public string VideoCodec { get; init; } = "auto";
    public string VideoDecoder { get; init; } = "gpu";
    public bool VideoSharpen { get; init; }
    public bool VideoFxaa { get; init; }
    public double VideoBrightness { get; init; }
    public double VideoContrast { get; init; } = 1;
    public double VideoSaturation { get; init; } = 1;
    public double VideoVibrance { get; init; }
    public double VideoVignette { get; init; }
    public double VideoGamma { get; init; } = 1;
    public bool StayAwake { get; init; }
    public bool Audio { get; init; } = true;
    public bool TurnScreenOff { get; init; }
    public string? NewDisplay { get; init; }
    public string? AutoLaunchPackage { get; init; }
    public bool AdaptiveBitrate { get; init; }
    public bool ClipboardAutosync { get; init; } = true;
}

public sealed class VideoPacket
{
    public required byte[] Data { get; init; }
    public int Length { get; init; }
    public bool IsConfig { get; init; }
    public bool IsKeyFrame { get; init; }
    public long Pts { get; init; }
}

public sealed class EngineSession : IAsyncDisposable
{
    private const string ServerVersion = "3.0";
    private const ushort ProtocolVersion = 3;
    private const uint HelloMagic = 0x544D4952;
    private string _remoteJarPath = "";

    private const byte ChanSession = 0;
    private const byte ChanVideo = 1;
    private const byte ChanAudio = 2;
    private const byte ChanDevice = 4;

    private const byte KindCodec = 0;
    private const byte KindPacket = 1;
    private const byte KindSession = 2;
    private const byte KindEnd = 3;

    private readonly AdbDevice _device;
    private readonly EngineOptions _options;
    private readonly CancellationTokenSource _cts;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private int _ended;
    public bool HasEnded => Volatile.Read(ref _ended) != 0;

    private TcpListener? _listener;
    private Process? _serverProcess;
    private Socket? _socket;
    private ControlChannel? _control;
    private string _socketName = "";
    private Task? _muxTask;

    public string? DeviceName { get; private set; }
    public uint EngineCaps { get; private set; }
    public string? VideoCodecId { get; private set; }
    public string? AudioCodecId { get; private set; }
    public int VideoWidth { get; private set; }
    public int VideoHeight { get; private set; }
    private long _rxBytes;
    public long RxBytes => Interlocked.Read(ref _rxBytes);
    private long _lastPacketAt;
    public long LastPacketAt => Interlocked.Read(ref _lastPacketAt);
    private long _videoPackets;
    public long VideoPackets => Interlocked.Read(ref _videoPackets);
    public long ConnectedAt { get; private set; }

    public event Action<VideoPacket>? VideoPacketReceived;
    public event Action<VideoPacket>? AudioPacketReceived;
    public event Action<bool>? AudioEnded;
    public event Action<int, int>? VideoSizeChanged;
    public event Action<string>? ServerLog;
    public event Action<string>? DeviceClipboard;
    public event Action? Disconnected;

    public void BreakConnection()
    {
        try { _socket?.Dispose(); } catch { }
    }

    public ControlChannel? Control => _control;

    public EngineSession(AdbDevice device, EngineOptions options, CancellationToken ct = default)
    {
        _device = device;
        _options = options;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    }

    public async Task StartAsync()
    {
        var jarPath = Path.Combine(AppContext.BaseDirectory, "assets", "touchmirror-engine.jar");
        if (!File.Exists(jarPath))
            throw new FileNotFoundException("touchmirror-engine.jar manquant", jarPath);

        var scidHex = Random.Shared.Next(0, 0x7fffffff).ToString("x8");
        _remoteJarPath = $"/data/local/tmp/touchmirror-{scidHex}.jar";
        var cached = await AdbService.PrepareServerAsync(_device.Serial, jarPath, scidHex, _cts.Token);
        Services.AppLogger.Write(cached ? "session: verified cache reused" : "session: server transferred");
        _socketName = $"touchmirror_{scidHex}";

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        Services.AppLogger.Write("session: reverse begin");
        await AdbService.ReverseAsync(_device.Serial, _socketName, port, _cts.Token);
        Services.AppLogger.Write("session: reverse done");

        var args = BuildServerArgs(scidHex);
        ServerLog?.Invoke($"server args: {args}");
        _serverProcess = AdbService.StartServerProcess(_device.Serial, _remoteJarPath, args);
        _ = Task.Run(async () =>
        {
            try
            {
                while (await _serverProcess.StandardError.ReadLineAsync() is { } line)
                    ServerLog?.Invoke(line);
            }
            catch { }
        });
        _ = Task.Run(async () =>
        {
            try { while (await _serverProcess.StandardOutput.ReadLineAsync() is { } line) ServerLog?.Invoke(line); }
            catch { }
        });

        using var acceptCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        acceptCts.CancelAfter(TimeSpan.FromSeconds(15));

        _socket = await AcceptWithTimeout(acceptCts.Token);
        _listener.Stop();
        Services.AppLogger.Write("session: socket accepted");

        var headerBuf = new byte[5];
        if (!await ReadExactAsync(_socket, headerBuf, acceptCts.Token))
            throw new EndOfStreamException("Connection closed before server handshake");
        if (headerBuf[0] != ChanSession)
            throw new InvalidDataException("moteur incompatible : hello attendu en première frame");
        var helloLen = (int)BinaryPrimitives.ReadUInt32BigEndian(headerBuf.AsSpan(1));
        if (helloLen is < 11 or > 1024)
            throw new InvalidDataException($"hello invalide ({helloLen} octets)");
        var helloBuf = new byte[helloLen];
        if (!await ReadExactAsync(_socket, helloBuf, acceptCts.Token))
            throw new EndOfStreamException("Truncated server handshake");
        if (helloBuf[10] > helloLen - 11)
            throw new InvalidDataException("Invalid server name length");
        if (BinaryPrimitives.ReadUInt32BigEndian(helloBuf) != HelloMagic)
            throw new InvalidDataException("moteur incompatible : en-tête TMIR absent");
        var proto = BinaryPrimitives.ReadUInt16BigEndian(helloBuf.AsSpan(4));
        if (proto != ProtocolVersion)
            throw new InvalidDataException($"protocole moteur v{proto} non supporté (attendu v{ProtocolVersion})");
        EngineCaps = BinaryPrimitives.ReadUInt32BigEndian(helloBuf.AsSpan(6));
        DeviceName = Encoding.UTF8.GetString(helloBuf, 11, helloBuf[10]);
        Services.AppLogger.Write($"session: hello TM/{proto} caps=0x{EngineCaps:x2}");

        _control = new ControlChannel(_socket);
        _control.ClipboardReceived += t => DeviceClipboard?.Invoke(t);
        _control.SendQueueFaulted += () =>
        {
            ServerLog?.Invoke("session: control queue saturated — forcing reconnect");
            BreakConnection();
        };
        _control.SendConfig(_options);

        if (!string.IsNullOrWhiteSpace(_options.AutoLaunchPackage) && _options.NewDisplay != null)
            try { _control.StartApp(_options.AutoLaunchPackage); } catch { }

        ConnectedAt = Environment.TickCount64;
        _muxTask = Task.Run(DemuxLoopAsync);
    }

    private string BuildServerArgs(string scidHex)
    {
        return $"{ServerVersion} {scidHex}";
    }

    private async Task<Socket> AcceptWithTimeout(CancellationToken ct)
    {
        var socket = await _listener!.AcceptSocketAsync(ct);
        socket.NoDelay = true;
        socket.ReceiveBufferSize = 4 << 20;
        return socket;
    }

    private static async Task<bool> ReadExactAsync(Socket socket, Memory<byte> buffer, CancellationToken ct = default)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await socket.ReceiveAsync(buffer.Slice(total), SocketFlags.None, ct);
            if (n == 0) return false;
            total += n;
        }
        return true;
    }

    private async Task DemuxLoopAsync()
    {
        var header = new byte[5];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (!await ReadExactAsync(_socket!, header, _cts.Token))
                    break;
                var channel = header[0];
                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1));
                if (size is <= 0 or > 64 << 20)
                    throw new InvalidDataException($"Invalid stream frame size: {size}");
                Interlocked.Add(ref _rxBytes, size + 5);
                var payload = System.Buffers.ArrayPool<byte>.Shared.Rent(size);
                try
                {
                    if (!await ReadExactAsync(_socket!, payload.AsMemory(0, size), _cts.Token))
                        break;
                    Dispatch(channel, payload, size);
                }
                finally { System.Buffers.ArrayPool<byte>.Shared.Return(payload); }
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            ServerLog?.Invoke($"mux loop error: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _ended, 1);
            _control?.Dispose();
            if (!_cts.IsCancellationRequested)
            {
                ServerLog?.Invoke("session: stream ended unexpectedly; cause undetermined");
                Disconnected?.Invoke();
            }
        }
    }

    private void Dispatch(byte channel, byte[] payload, int size)
    {
        switch (channel)
        {
            case ChanVideo:
                ReadStreamPacket(payload, size, true);
                break;
            case ChanAudio:
                ReadStreamPacket(payload, size, false);
                break;
            case ChanDevice:
                _control?.Feed(payload.AsSpan(0, size));
                break;
        }
    }

    private void ReadStreamPacket(byte[] payload, int size, bool video)
    {
        if (size < 1)
            return;

        switch (payload[0])
        {
            case KindCodec:
                if (size >= 5)
                {
                    var id = Encoding.ASCII.GetString(payload, 1, 4).Trim('\0');
                    if (video)
                        VideoCodecId = id;
                    else
                        AudioCodecId = id;
                }
                break;
            case KindSession:
                if (video && size >= 9)
                {
                    VideoWidth = (int)BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(1));
                    VideoHeight = (int)BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(5));
                    VideoSizeChanged?.Invoke(VideoWidth, VideoHeight);
                }
                break;
            case KindEnd:
                if (size >= 2)
                {
                    var isError = payload[1] != 0;
                    if (video)
                        ServerLog?.Invoke(isError
                            ? "session: flux vidéo terminé en erreur"
                            : "session: flux vidéo terminé");
                    else
                        AudioEnded?.Invoke(isError);
                }
                break;
            case KindPacket:
                if (size < 10)
                    return;
                Interlocked.Exchange(ref _lastPacketAt, Environment.TickCount64);
                if (video)
                    Interlocked.Increment(ref _videoPackets);
                var flags = payload[9];
                var packetSize = size - 10;
                var data = System.Buffers.ArrayPool<byte>.Shared.Rent(packetSize);
                try
                {
                    Array.Copy(payload, 10, data, 0, packetSize);
                    var packet = new VideoPacket
                    {
                        Data = data,
                        Length = packetSize,
                        IsConfig = (flags & 0x01) != 0,
                        IsKeyFrame = (flags & 0x02) != 0,
                        Pts = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(1))
                    };
                    if (video)
                        VideoPacketReceived?.Invoke(packet);
                    else
                        AudioPacketReceived?.Invoke(packet);
                }
                finally { System.Buffers.ArrayPool<byte>.Shared.Return(data); }
                break;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _ended, 1);
        _cts.Cancel();
        try { _socket?.Dispose(); } catch { }
        try { _control?.Dispose(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { if (_serverProcess is { HasExited: false }) _serverProcess.Kill(); } catch { }
        _serverProcess?.Dispose();
        if (_muxTask != null)
            try { await _muxTask.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        if (!string.IsNullOrEmpty(_socketName))
            await AdbService.ReverseRemoveAsync(_device.Serial, _socketName);
        _cts.Dispose();
    }
}
