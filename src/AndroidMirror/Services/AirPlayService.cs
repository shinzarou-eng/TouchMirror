using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Makaretu.Dns;
using TouchMirror.Video;

namespace TouchMirror.Services;

/// <summary>
/// Récepteur AirPlay iOS : pilote le process natif AirPlayHost (sockets RAOP +
/// AirPlay + décodage), annonce les services en mDNS, et lit les frames YUV
/// décodées poussées sur un named pipe.
/// Affichage seul — aucun canal d'input n'existe dans cette voie.
/// </summary>
public sealed class AirPlayService : IDisposable
{
    public const int RaopPort = 5001;
    public const int AirPlayPort = 7001;
    public const string ReceiverName = "TouchMirror";

    private const uint MsgVideo = 1;
    private const uint MsgAudio = 2;

    private Process? _host;
    private NamedPipeServerStream? _videoPipe;
    private NamedPipeServerStream? _eventPipe;
    private CancellationTokenSource? _cts;
    private Task? _videoTask;
    private Task? _eventTask;
    private AirPlayAdvertiser? _advertiser;

    private readonly AirPlayFrameSource _frames = new();

    public IFrameSource Frames => _frames;
    public bool IsRunning { get; private set; }

    /// <summary>Nom et deviceId de l'iPhone actuellement connecté (vide sinon).</summary>
    public string? ConnectedDeviceName { get; private set; }
    public string? ConnectedDeviceId { get; private set; }

    public event Action<string, string>? DeviceConnected;
    public event Action<string, string>? DeviceDisconnected;
    public event Action<string>? Log;
    public event Action? Exited;
    /// <summary>PCM décodé : (sampleRate, channels, bitsPerSample, data, length).</summary>
    public event Action<int, int, int, byte[], int>? AudioFrame;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
            return;

        var dir = Path.Combine(AppContext.BaseDirectory, "assets", "airplay");
        var exe = Path.Combine(dir, "AirPlayHost.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException($"Récepteur AirPlay introuvable : {exe}");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var vname = $"tm-airplay-v-{Environment.ProcessId}";
        var ename = $"tm-airplay-e-{Environment.ProcessId}";
        _videoPipe = new NamedPipeServerStream(vname, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        _eventPipe = new NamedPipeServerStream(ename, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        _host = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--video-pipe {vname} --event-pipe {ename} " +
                            $"--name \"{ReceiverName}\" --raop-port {RaopPort} --airplay-port {AirPlayPort}",
                WorkingDirectory = dir,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        _host.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                Log?.Invoke($"airplay: {e.Data}");
        };
        _host.Exited += (_, _) =>
        {
            IsRunning = false;
            Exited?.Invoke();
        };
        _host.Start();
        _host.BeginErrorReadLine();

        // Timeout + mort du host : sans ça, un host qui crashe avant de se
        // connecter laisse la commande pendue (menu grisé pour toujours).
        var hostDead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void onExit(object? s, EventArgs e) => hostDead.TrySetResult();
        _host.Exited += onExit;
        var connect = Task.WhenAll(
            _videoPipe.WaitForConnectionAsync(_cts.Token),
            _eventPipe.WaitForConnectionAsync(_cts.Token));
        var done = await Task.WhenAny(connect, hostDead.Task,
            Task.Delay(TimeSpan.FromSeconds(15), _cts.Token));
        _host.Exited -= onExit;
        if (done != connect)
        {
            var why = _host.HasExited
                ? $"AirPlayHost s'est arrêté (code {_host.ExitCode})"
                : "AirPlayHost n'a pas répondu en 15 s";
            Dispose();
            throw new InvalidOperationException(why);
        }
        await connect; // propage une éventuelle annulation

        IsRunning = true;
        _videoTask = Task.Run(PumpVideoAsync);
        _eventTask = Task.Run(PumpEventsAsync);

        _advertiser = new AirPlayAdvertiser();
        try
        {
            _advertiser.Start(ReceiverName, RaopPort, AirPlayPort);
            Log?.Invoke($"airplay: service « {ReceiverName} » annoncé (raop {RaopPort}, airplay {AirPlayPort})");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"airplay: annonce mDNS impossible — {ex.Message}");
        }
    }

    private async Task PumpVideoAsync()
    {
        var pipe = _videoPipe!;
        var ct = _cts!.Token;
        var lenBuf = new byte[4];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await pipe.ReadExactlyAsync(lenBuf, ct);
                var payloadLen = BitConverter.ToUInt32(lenBuf);
                if (payloadLen == 0 || payloadLen > 64 * 1024 * 1024)
                    break;
                var payload = new byte[payloadLen];
                await pipe.ReadExactlyAsync(payload, ct);
                var msg = BitConverter.ToUInt32(payload, 0);
                if (msg == MsgVideo)
                    ParseVideo(payload);
                else if (msg == MsgAudio)
                    ParseAudio(payload);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Log?.Invoke($"airplay: lecture vidéo interrompue — {ex.Message}");
        }
    }

    private void ParseVideo(byte[] p)
    {
        // [msg u32][pts u64][w u32][h u32][pitch u32×3][dataLen u32×3][isKey u8][idLen u32][id][data]
        var w = BitConverter.ToUInt32(p, 12);
        var h = BitConverter.ToUInt32(p, 16);
        var pitch0 = BitConverter.ToUInt32(p, 20);
        var pitch1 = BitConverter.ToUInt32(p, 24);
        var pitch2 = BitConverter.ToUInt32(p, 28);
        var len0 = BitConverter.ToUInt32(p, 32);
        var len1 = BitConverter.ToUInt32(p, 36);
        var len2 = BitConverter.ToUInt32(p, 40);
        var idLen = BitConverter.ToInt32(p, 45);
        var dataOff = 49 + idLen;
        if (idLen < 0 || dataOff >= p.Length || w == 0 || h == 0)
            return;

        var deviceId = idLen > 0 ? Encoding.UTF8.GetString(p, 49, idLen) : "";
        if (!string.IsNullOrEmpty(ConnectedDeviceId) && deviceId != ConnectedDeviceId)
            return; // un iPhone déjà retenu pour cette tuile

        var dataLen = (int)(len0 + len1 + len2);
        if (dataOff + dataLen > p.Length)
            return;
        var frame = new byte[dataLen];
        Buffer.BlockCopy(p, dataOff, frame, 0, dataLen);
        _frames.Publish((int)w, (int)h, pitch0, pitch1, pitch2, len0, len1, len2, frame);
    }

    private void ParseAudio(byte[] p)
    {
        // [msg u32][pts u64][sampleRate u32][channels u16][bits u16][dataLen u32][data]
        if (p.Length < 24)
            return;
        var rate = BitConverter.ToInt32(p, 12);
        var channels = BitConverter.ToUInt16(p, 16);
        var bits = BitConverter.ToUInt16(p, 18);
        var dataLen = BitConverter.ToInt32(p, 20);
        if (dataLen <= 0 || 24 + dataLen > p.Length)
            return;
        AudioFrame?.Invoke(rate, channels, bits, p[24..(24 + dataLen)], dataLen);
    }

    private async Task PumpEventsAsync()
    {
        var pipe = _eventPipe!;
        var ct = _cts!.Token;
        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8);
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null)
                    break;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var type = doc.RootElement.GetProperty("type").GetString() ?? "";
                    var name = doc.RootElement.GetProperty("name").GetString() ?? "";
                    var id = doc.RootElement.GetProperty("deviceId").GetString() ?? "";
                    switch (type)
                    {
                        case "ready":
                            Log?.Invoke("airplay: récepteur prêt");
                            break;
                        case "connected":
                            ConnectedDeviceName = name;
                            ConnectedDeviceId = id;
                            DeviceConnected?.Invoke(name, id);
                            break;
                        case "disconnected":
                            ConnectedDeviceName = null;
                            ConnectedDeviceId = null;
                            DeviceDisconnected?.Invoke(name, id);
                            break;
                    }
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _advertiser?.Dispose(); } catch { }
        try { _videoPipe?.Dispose(); } catch { }
        try { _eventPipe?.Dispose(); } catch { }
        try { if (_host is { HasExited: false }) _host.Kill(); } catch { }
        try { _host?.Dispose(); } catch { }
        _frames.Dispose();
        _cts?.Dispose();
    }
}

/// <summary>
/// Annonce mDNS des services AirPlay (_airplay._tcp + _raop._tcp) sans service
/// Bonjour ni élévation : multicast UDP 5353 géré par Makaretu.Dns.
/// </summary>
public sealed class AirPlayAdvertiser : IDisposable
{
    private ServiceDiscovery? _sd;

    // Identité fixe locale administrée — doit matcher le deviceID/macAddress
    // exposés par /info (airplay2dll). Une vraie MAC fuiterait sur le LAN.
    private const string DeviceId = "aa:54:01:af:c3:c1";
    private const string PairingIdentity = "2e388006-13ba-4041-9a67-25dd4a43d536";
    private const string PairingPublicKey =
        "b07727d6f6cd6e08b58ede525ec3cdeaa252ad9f683feb212ef8a205246554e7";

    public void Start(string name, int raopPort, int airplayPort)
    {
        var macCompact = DeviceId.Replace(":", "").ToUpperInvariant();

        _sd = new ServiceDiscovery();

        var airplay = new ServiceProfile(name, "_airplay._tcp", (ushort)airplayPort);
        airplay.AddProperty("srcvers", "220.68");
        airplay.AddProperty("deviceid", DeviceId);
        airplay.AddProperty("features", "0x5A7FFEE6,0x0");
        airplay.AddProperty("model", "AppleTV3,2");
        airplay.AddProperty("flags", "0x4");
        airplay.AddProperty("vv", "2");
        airplay.AddProperty("pi", PairingIdentity);
        airplay.AddProperty("pk", PairingPublicKey);
        airplay.AddProperty("pw", "false");
        _sd.Advertise(airplay);

        var raop = new ServiceProfile($"{macCompact}@{name}", "_raop._tcp", (ushort)raopPort);
        raop.AddProperty("txtvers", "1");
        raop.AddProperty("ch", "2");
        raop.AddProperty("cn", "0,1,3");
        raop.AddProperty("da", "true");
        raop.AddProperty("et", "0,3,5");
        raop.AddProperty("ek", "1");
        raop.AddProperty("md", "0,1,2");
        raop.AddProperty("pw", "false");
        raop.AddProperty("sf", "0x4");
        raop.AddProperty("sm", "false");
        raop.AddProperty("sr", "44100");
        raop.AddProperty("ss", "16");
        raop.AddProperty("sv", "false");
        raop.AddProperty("tp", "TCP,UDP");
        raop.AddProperty("vn", "3");
        raop.AddProperty("vs", "220.68");
        raop.AddProperty("ft", "0x5A7FFEE6,0x0");
        raop.AddProperty("am", "AppleTV3,2");
        _sd.Advertise(raop);
    }

    public void Dispose()
    {
        try { _sd?.Dispose(); } catch { }
        _sd = null;
    }
}
