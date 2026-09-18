using System.Net;
using System.Net.Sockets;
using TouchMirror.Services;

namespace TouchMirror.AirPlay;

public sealed class RtspRequest
{
    public string Method = "";
    public string Path = "";
    public string Protocol = "RTSP/1.0";
    public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
    public byte[] Body = Array.Empty<byte>();

    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
    public int CSeq => int.TryParse(Header("CSeq"), out var n) ? n : 0;
}

public sealed class RtspServer : IDisposable
{
    private readonly int _port;
    private readonly string _name;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<Task> _clients = new();

    public event Action<string>? Log;
    public event Action<string, string>? DeviceConnected;
    public event Action<string, string>? DeviceDisconnected;
    public event Action<TouchMirror.Video.IFrameSource>? StreamStarted;
    public event Action? StreamStopped;

    public RtspServer(int port, string name)
    {
        _port = port;
        _name = name;
    }

    public void Start(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _ = AcceptLoop();
    }

    private async Task AcceptLoop()
    {
        var ct = _cts!.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                var session = new AirPlaySession(client, _name);
                session.Log += s => Log?.Invoke(s);
                session.DeviceConnected += (n, id) => DeviceConnected?.Invoke(n, id);
                session.DeviceDisconnected += (n, id) => DeviceDisconnected?.Invoke(n, id);
                session.StreamStarted += src => StreamStarted?.Invoke(src);
                session.StreamStopped += () => StreamStopped?.Invoke();
                lock (_clients)
                    _clients.Add(Task.Run(() => session.RunAsync(ct)));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        Task[] pending;
        lock (_clients)
            pending = _clients.ToArray();
        try { Task.WaitAll(pending, TimeSpan.FromSeconds(2)); } catch { }
        _cts?.Dispose();
    }
}
