using Makaretu.Dns;
using TouchMirror.AirPlay;
using TouchMirror.Video;

namespace TouchMirror.Services;

public sealed class AirPlayService : IDisposable
{
    public const int RaopPort = 5001;
    public const int AirPlayPort = 7001;
    public const string ReceiverName = "TouchMirror";

    private CancellationTokenSource? _cts;
    private AirPlayAdvertiser? _advertiser;
    private RtspServer? _airplayServer;
    private RtspServer? _raopServer;

    private readonly SwitchableFrameSource _switchable = new();

    public IFrameSource Frames => _switchable;
    public bool IsRunning { get; private set; }

    public string? ConnectedDeviceName { get; private set; }
    public string? ConnectedDeviceId { get; private set; }

    public event Action<string, string>? DeviceConnected;
    public event Action<string, string>? DeviceDisconnected;
    public event Action<string>? Log;
    public event Action<string>? PairingCode;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (Environment.ProcessPath is { } self)
            _ = FirewallHelper.EnsureRulesAsync(new List<(string, string)> { (self, "TouchMirror") },
                s => Log?.Invoke(s));

        _airplayServer = new RtspServer(AirPlayPort, ReceiverName);
        _raopServer = new RtspServer(RaopPort, ReceiverName);
        foreach (var srv in new[] { _airplayServer, _raopServer })
        {
            srv.Log += s => Log?.Invoke(s);
            srv.DeviceConnected += (n, id) =>
            {
                ConnectedDeviceName = n;
                ConnectedDeviceId = id;
                DeviceConnected?.Invoke(n, id);
            };
            srv.DeviceDisconnected += (n, id) =>
            {
                ConnectedDeviceName = null;
                ConnectedDeviceId = null;
                DeviceDisconnected?.Invoke(n, id);
            };
            srv.StreamStarted += src =>
            {
                _switchable.Current = src;
                Log?.Invoke("airplay: flux vidéo natif décodé par TouchMirror");
            };
            srv.StreamStopped += () => _switchable.Current = null;
            srv.PairingCode += p => PairingCode?.Invoke(p);
            srv.Start(_cts.Token);
        }

        _advertiser = new AirPlayAdvertiser();
        try
        {
            _advertiser.Start(ReceiverName, RaopPort, AirPlayPort);
            Log?.Invoke($"airplay: service « {ReceiverName} » annoncé (raop {RaopPort}, airplay {AirPlayPort}) — serveur natif");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"airplay: annonce mDNS impossible — {ex.Message}");
        }

        IsRunning = true;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _advertiser?.Dispose(); } catch { }
        try { _airplayServer?.Dispose(); } catch { }
        try { _raopServer?.Dispose(); } catch { }
        _cts?.Dispose();
    }
}

public sealed class AirPlayAdvertiser : IDisposable
{
    private ServiceDiscovery? _sd;

    public const string DeviceIdPublic = "aa:54:01:af:c3:d9";
    private const string DeviceId = DeviceIdPublic;

    public void Start(string name, int raopPort, int airplayPort)
    {
        var macCompact = DeviceId.Replace(":", "").ToUpperInvariant();

        _sd = new ServiceDiscovery(MdnsHost.Instance);

        var pk = Convert.ToHexString(AirPlaySession.PairingIdentity.PublicKey).ToLowerInvariant();
        var pi = AirPlaySession.PairingIdentity.PairingId;

        var airplay = new ServiceProfile(name, "_airplay._tcp", (ushort)airplayPort);
        airplay.AddProperty("srcvers", "220.68");
        airplay.AddProperty("deviceid", DeviceId);
        airplay.AddProperty("features", "0x5A7FFEE6,0x0");
        airplay.AddProperty("model", "AppleTV3,2");
        airplay.AddProperty("flags", "0x84");
        airplay.AddProperty("vv", "2");
        airplay.AddProperty("pk", pk);
        airplay.AddProperty("pi", pi);
        _sd.Advertise(airplay);

        var raop = new ServiceProfile($"{macCompact}@{name}", "_raop._tcp", (ushort)raopPort);
        raop.AddProperty("txtvers", "1");
        raop.AddProperty("ch", "2");
        raop.AddProperty("cn", "0,1,2,3");
        raop.AddProperty("da", "true");
        raop.AddProperty("et", "0,3,5");
        raop.AddProperty("md", "0,1,2");
        raop.AddProperty("am", "AppleTV3,2");
        raop.AddProperty("pk", pk);
        raop.AddProperty("pw", "false");
        raop.AddProperty("sr", "44100");
        raop.AddProperty("ss", "16");
        raop.AddProperty("sf", "0x84");
        raop.AddProperty("tp", "UDP");
        raop.AddProperty("vn", "65537");
        raop.AddProperty("vs", "220.68");
        raop.AddProperty("vv", "2");
        raop.AddProperty("ft", "0x5A7FFEE6,0x0");
        _sd.Advertise(raop);
    }

    public void Dispose()
    {
        try { _sd?.Dispose(); } catch { }
        _sd = null;
    }
}

internal sealed class SwitchableFrameSource : IFrameSource
{
    private volatile IFrameSource? _current;

    public IFrameSource? Current
    {
        get => _current;
        set => _current = value;
    }

    public bool TryTakeLatest(out byte[]? buffer, out int width, out int height)
    {
        buffer = null;
        width = height = 0;
        return _current?.TryTakeLatest(out buffer, out width, out height) ?? false;
    }

    public void Release(byte[] buffer) => _current?.Release(buffer);
}
