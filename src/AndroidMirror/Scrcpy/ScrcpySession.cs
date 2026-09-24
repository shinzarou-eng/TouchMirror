using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TouchMirror.Services;

namespace TouchMirror.Scrcpy;

public sealed class ScrcpyOptions
{
    public int MaxSize { get; init; } = 0;
    public int MaxFps { get; init; } = 60;
    public int VideoBitRate { get; init; } = 8_000_000;
    public string VideoCodec { get; init; } = "auto";
    public string VideoDecoder { get; init; } = "gpu";
    public bool VideoSharpen { get; init; }
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

public sealed class ScrcpySession : IAsyncDisposable
{
    private const string ServerVersion = "4.1-tm.2";
    private const string RemoteJarPath = "/data/local/tmp/touchmirror-engine.jar";

    private readonly AdbDevice _device;
    private readonly ScrcpyOptions _options;
    private readonly CancellationTokenSource _cts = new();

    private TcpListener? _listener;
    private Process? _serverProcess;
    private Socket? _videoSocket;
    private Socket? _audioSocket;
    private ControlChannel? _control;
    private string _socketName = "";
    private Task? _videoTask;
    private Task? _audioTask;

    public string? DeviceName { get; private set; }
    public string? VideoCodecId { get; private set; }
    public string? AudioCodecId { get; private set; }
    public int VideoWidth { get; private set; }
    public int VideoHeight { get; private set; }

    public event Action<VideoPacket>? VideoPacketReceived;
    public event Action<VideoPacket>? AudioPacketReceived;
    public event Action<int, int>? VideoSizeChanged;
    public event Action<string>? ServerLog;
    public event Action<string>? DeviceClipboard;
    public event Action? Disconnected;

    public ControlChannel? Control => _control;

    public ScrcpySession(AdbDevice device, ScrcpyOptions options)
    {
        _device = device;
        _options = options;
    }

    public async Task StartAsync()
    {
        var jarPath = Path.Combine(AppContext.BaseDirectory, "assets", "touchmirror-engine.jar");
        if (!File.Exists(jarPath))
            throw new FileNotFoundException("touchmirror-engine.jar manquant", jarPath);

        Services.AppLogger.Write("session: push begin");
        await AdbService.PushAsync(_device.Serial, jarPath, RemoteJarPath, _cts.Token);
        Services.AppLogger.Write("session: push done");

        var scid = Random.Shared.Next(0, 0x7fffffff);
        var scidHex = scid.ToString("x8");
        _socketName = $"touchmirror_{scidHex}";

        var port = FindFreePort();
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();

        Services.AppLogger.Write("session: reverse begin");
        await AdbService.ReverseAsync(_device.Serial, _socketName, port, _cts.Token);
        Services.AppLogger.Write("session: reverse done");

        var args = BuildServerArgs(scidHex);
        ServerLog?.Invoke($"server args: {args}");
        _serverProcess = AdbService.StartServerProcess(_device.Serial, RemoteJarPath, args);
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

        _videoSocket = await AcceptWithTimeout(acceptCts.Token);
        Services.AppLogger.Write("session: video socket accepted");
        if (_options.Audio)
            _audioSocket = await AcceptWithTimeout(acceptCts.Token);
        var controlSocket = await AcceptWithTimeout(acceptCts.Token);
        _listener.Stop();
        Services.AppLogger.Write("session: sockets accepted");

        var metaBuf = new byte[64];
        await ReadExactAsync(_videoSocket, metaBuf);
        DeviceName = Encoding.UTF8.GetString(metaBuf).TrimEnd('\0');

        var codecBuf = new byte[4];
        await ReadExactAsync(_videoSocket, codecBuf);
        VideoCodecId = Encoding.ASCII.GetString(codecBuf).Trim('\0');

        if (_audioSocket != null)
        {
            var audioCodecBuf = new byte[4];
            await ReadExactAsync(_audioSocket, audioCodecBuf);
            AudioCodecId = Encoding.ASCII.GetString(audioCodecBuf);
        }

        _control = new ControlChannel(controlSocket);
        _control.ClipboardReceived += t => DeviceClipboard?.Invoke(t);

        if (!string.IsNullOrWhiteSpace(_options.AutoLaunchPackage) && _options.NewDisplay != null)
            try { _control.StartApp(_options.AutoLaunchPackage); } catch { }

        _videoTask = Task.Run(VideoReadLoopAsync);
        if (_audioSocket != null)
            _audioTask = Task.Run(AudioReadLoopAsync);
    }

    private string BuildServerArgs(string scidHex)
    {
        var sb = new StringBuilder();
        sb.Append(ServerVersion);
        sb.Append($" scid={scidHex}");
        sb.Append(" log_level=info");
        sb.Append(_options.Audio ? " audio=true audio_codec=opus" : " audio=false");
        sb.Append(" control=true");
        sb.Append($" video_codec={_options.VideoCodec}");
        sb.Append(" cleanup=true");
        sb.Append(" power_on=true");
        sb.Append(" downsize_on_error=true");
        if (_options.ClipboardAutosync)
            sb.Append(" clipboard_autosync=true");
        if (_options.MaxSize > 0)
            sb.Append($" max_size={_options.MaxSize}");
        if (_options.MaxFps > 0)
            sb.Append($" max_fps={_options.MaxFps}");
        if (_options.VideoBitRate > 0)
            sb.Append($" video_bit_rate={_options.VideoBitRate}");
        if (_options.StayAwake)
            sb.Append(" stay_awake=true");
        if (_options.NewDisplay != null)
            sb.Append($" new_display={_options.NewDisplay}");
        if (!string.IsNullOrWhiteSpace(_options.AutoLaunchPackage) && _options.NewDisplay == null)
            sb.Append($" start_app={_options.AutoLaunchPackage}");
        return sb.ToString();
    }

    private static int FindFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
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

    private async Task VideoReadLoopAsync()
    {
        var header = new byte[12];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (!await ReadExactAsync(_videoSocket!, header, _cts.Token))
                    break;

                if ((header[0] & 0x80) != 0)
                {
                    VideoWidth = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
                    VideoHeight = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
                    VideoSizeChanged?.Invoke(VideoWidth, VideoHeight);
                    continue;
                }

                var flags = header[0];
                var pts = (long)(BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(0)) & 0x3FFFFFFFFFFFFFFF);
                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
                if (size is < 0 or > 64 << 20)
                    break;
                var payload = System.Buffers.ArrayPool<byte>.Shared.Rent(size);
                try
                {
                    if (!await ReadExactAsync(_videoSocket!, payload.AsMemory(0, size), _cts.Token))
                        break;

                    VideoPacketReceived?.Invoke(new VideoPacket
                    {
                        Data = payload,
                        Length = size,
                        IsConfig = (flags & 0x40) != 0,
                        IsKeyFrame = (flags & 0x20) != 0,
                        Pts = pts
                    });
                }
                finally { System.Buffers.ArrayPool<byte>.Shared.Return(payload); }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ServerLog?.Invoke($"video loop error: {ex.Message}");
        }
        Disconnected?.Invoke();
    }

    private async Task AudioReadLoopAsync()
    {
        var header = new byte[12];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (!await ReadExactAsync(_audioSocket!, header, _cts.Token))
                    break;
                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
                if (size is < 0 or > 16 << 20)
                    break;
                var payload = System.Buffers.ArrayPool<byte>.Shared.Rent(size);
                try
                {
                    if (!await ReadExactAsync(_audioSocket!, payload.AsMemory(0, size), _cts.Token))
                        break;
                    AudioPacketReceived?.Invoke(new VideoPacket
                    {
                        Data = payload,
                        Length = size,
                        IsConfig = (header[0] & 0x40) != 0,
                        IsKeyFrame = (header[0] & 0x20) != 0,
                        Pts = (long)(BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(0)) & 0x3FFFFFFFFFFFFFFF)
                    });
                }
                finally { System.Buffers.ArrayPool<byte>.Shared.Return(payload); }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ServerLog?.Invoke($"audio loop error: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _videoSocket?.Dispose(); } catch { }
        try { _audioSocket?.Dispose(); } catch { }
        try { _control?.Dispose(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { if (_serverProcess is { HasExited: false }) _serverProcess.Kill(); } catch { }
        _serverProcess?.Dispose();
        var tasks = new[] { _videoTask, _audioTask }.Where(t => t != null).Cast<Task>().ToArray();
        if (tasks.Length > 0)
            try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        if (!string.IsNullOrEmpty(_socketName))
            await AdbService.ReverseRemoveAsync(_device.Serial, _socketName);
        _cts.Dispose();
    }
}
