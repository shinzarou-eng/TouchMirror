using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using TouchMirror.Services;

namespace TouchMirror.AirPlay;

public sealed class AirPlaySession
{
    private const string ServerHeader = "AirTunes/377.40.00";

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly string _receiverName;
    private readonly Pairing _pairing;
    private readonly FairPlay _fairPlay = new();
    private static readonly IFairPlayDecrypt FairPlayDecrypt = HelperFairPlayDecrypt.Create();
    private byte[]? _aesKey;
    private NtpTiming? _ntp;
    private MirrorStreamServer? _mirror;
    private readonly List<UdpClient> _audioHolders = new();
    private string? _sessionId;
    private bool _streaming;
    private ChannelCipher? _cipher;
    private bool _cipherPending;
    private readonly List<byte> _raw = new();
    private readonly List<byte> _plain = new();
    private int _plainPos;

    public event Action<string>? Log;
    public event Action<string, string>? DeviceConnected;
    public event Action<string, string>? DeviceDisconnected;
    public event Action<TouchMirror.Video.IFrameSource>? StreamStarted;
    public event Action? StreamStopped;

    public AirPlaySession(TcpClient client, string receiverName)
    {
        _client = client;
        _stream = client.GetStream();
        _receiverName = receiverName;
        _pairing = new Pairing(PairingIdentity);
    }

    public static PairingIdentityStore PairingIdentity { get; } = PairingIdentityStore.Load();

    public async Task RunAsync(CancellationToken ct)
    {
        var remote = _client.Client.RemoteEndPoint?.ToString() ?? "?";
        try
        {
            while (!ct.IsCancellationRequested && _client.Connected)
            {
                var req = await ReadRequestAsync(ct);
                if (req == null)
                    break;
                Log?.Invoke($"airplay rtsp [{remote}] {req.Method} {req.Path} ({req.Body.Length}b)");
                await HandleAsync(req, ct);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
        catch (Exception ex)
        {
            Log?.Invoke($"airplay rtsp: erreur session — {ex.Message}");
        }
        finally
        {
            StopStreams();
            if (_streaming)
                DeviceDisconnected?.Invoke(remote, remote);
            try { _client.Dispose(); } catch { }
            Log?.Invoke($"airplay rtsp [{remote}] session fermée");
        }
    }

    private async Task HandleAsync(RtspRequest req, CancellationToken ct)
    {
        var path = req.Path;
        var qIdx = path.IndexOf('?');
        if (qIdx >= 0)
            path = path[..qIdx];

        switch (req.Method)
        {
            case "OPTIONS":
                await Respond(req, 200, new Dictionary<string, string>
                {
                    ["Public"] = "ANNOUNCE, SETUP, RECORD, PAUSE, FLUSH, TEARDOWN, GET_PARAMETER, SET_PARAMETER, POST, GET, OPTIONS"
                }, ct: ct);
                break;

            case "GET" when path is "/info" or "/server-info":
                await Respond(req, 200, PlistHeaders(), PlistCodec.WriteXml(BuildInfoPlist()), ct);
                break;

            case "POST" when path is "/pair-setup" or "/pair-setup-pin":
                var setupResp = _pairing.HandlePairSetup(req.Body);
                if (setupResp == null || setupResp.Length == 0)
                {
                    Log?.Invoke($"airplay: pair-setup rejeté ({req.Body.Length}b: {Convert.ToHexString(req.Body)})");
                    await Respond(req, 470, ct: ct);
                }
                else
                {
                    var isPlist = setupResp.Length > 8 && setupResp[0] == 'b' && setupResp[1] == 'p';
                    var ctHeaders = new Dictionary<string, string>
                    {
                        ["Content-Type"] = isPlist ? "application/x-apple-binary-plist" : "application/octet-stream"
                    };
                    Log?.Invoke($"airplay: {path} ({req.Body.Length}b → {setupResp.Length}b)");
                    await Respond(req, 200, ctHeaders, body: setupResp, ct: ct);
                }
                break;

            case "POST" when path is "/pair-verify" or "/pair-verify/start":
                var verifyResp = _pairing.HandlePairVerify(req.Body);
                if (verifyResp == null)
                {
                    Log?.Invoke("airplay: pair-verify rejeté");
                    await Respond(req, 470, ct: ct);
                }
                else
                {
                    Log?.Invoke($"airplay: pair-verify ({req.Body.Length}b → {verifyResp.Length}b, verified={_pairing.Verified})");
                    await Respond(req, 200, new Dictionary<string, string>
                    {
                        ["Content-Type"] = "application/octet-stream"
                    }, body: verifyResp.Length > 0 ? verifyResp : null, ct: ct);
                    if (_pairing.Verified && _cipher == null)
                        _cipherPending = true;
                }
                break;

            case "POST" when path == "/fp-setup":
                var fpResp = _fairPlay.HandleSetup(req.Body);
                if (fpResp == null)
                {
                    Log?.Invoke($"airplay: fp-setup non géré ({req.Body.Length}b): {Convert.ToHexString(req.Body, 0, Math.Min(16, req.Body.Length))}");
                    await Respond(req, 501, ct: ct);
                }
                else
                {
                    Log?.Invoke($"airplay: fp-setup ({req.Body.Length}b → {fpResp.Length}b)");
                    await Respond(req, 200, new Dictionary<string, string>
                    {
                        ["Content-Type"] = "application/octet-stream"
                    }, body: fpResp, ct: ct);
                }
                break;

            case "POST" when path == "/reverse":
                await Respond(req, 101, new Dictionary<string, string>
                {
                    ["Upgrade"] = "PTTH/1.0",
                    ["Connection"] = "Upgrade"
                }, ct: ct);
                break;

            case "POST" when path == "/feedback":
                await Respond(req, 200, ct: ct);
                break;

            case "ANNOUNCE":
                HandleAnnounce(req);
                _sessionId = req.Header("Session");
                DeviceConnected?.Invoke(remoteName(), remoteName());
                await Respond(req, 200, ct: ct);
                break;

            case "SETUP":
                await HandleSetup(req, ct);
                break;

            case "RECORD":
                _streaming = true;
                Log?.Invoke("airplay: RECORD — le flux commence");
                await Respond(req, 200, new Dictionary<string, string>
                {
                    ["Audio-Latency"] = "11025",
                    ["Audio-Jack-Status"] = "connected; type=analog"
                }, ct: ct);
                break;

            case "GET_PARAMETER":
                var param = req.Body.Length > 0 ? Encoding.UTF8.GetString(req.Body).Trim() : "";
                var body = param.Contains("volume") ? Encoding.UTF8.GetBytes("volume: 0.0\r\n") : Array.Empty<byte>();
                await Respond(req, 200, null, body, ct);
                break;

            case "SET_PARAMETER":
            case "PAUSE":
            case "FLUSH":
                await Respond(req, 200, ct: ct);
                break;

            case "TEARDOWN":
                _streaming = false;
                StopStreams();
                await Respond(req, 200, ct: ct);
                DeviceDisconnected?.Invoke(remoteName(), remoteName());
                return;

            default:
                Log?.Invoke($"airplay: {req.Method} {path} non géré — 501");
                await Respond(req, 501, ct: ct);
                break;
        }
    }

    private void StopStreams()
    {
        if (_mirror != null)
            StreamStopped?.Invoke();
        try { _mirror?.Dispose(); } catch { }
        _mirror = null;
        try { _ntp?.Dispose(); } catch { }
        _ntp = null;
        foreach (var u in _audioHolders)
        {
            try { u.Dispose(); } catch { }
        }
        _audioHolders.Clear();
    }

    private string remoteName() => _client.Client.RemoteEndPoint?.ToString() ?? "inconnu";

    private void HandleAnnounce(RtspRequest req)
    {
        try
        {
            var plist = PlistCodec.Read(req.Body);
            Log?.Invoke($"airplay: ANNOUNCE plist:\n{PlistCodec.Dump(plist)}");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"airplay: ANNOUNCE body non-plist ({req.Body.Length}b): {ex.Message}");
        }
    }

    private async Task HandleSetup(RtspRequest req, CancellationToken ct)
    {
        var resp = new Dictionary<string, object?>();
        var plist = PlistCodec.Read(req.Body) as Dictionary<string, object?>;
        if (plist != null)
            Log?.Invoke($"airplay: SETUP plist:\n{PlistCodec.Dump(plist)}");
        var clientIp = (_client.Client.RemoteEndPoint as IPEndPoint)?.Address;

        if (plist != null
            && plist.TryGetValue("ekey", out var ekObj) && ekObj is byte[] ekey
            && plist.TryGetValue("eiv", out var evObj) && evObj is byte[] eiv)
        {
            var clientName = plist.TryGetValue("name", out var n) ? n?.ToString() ?? "" : "";
            var clientId = plist.TryGetValue("deviceID", out var d) ? d?.ToString() ?? "" : "";
            var clientModel = plist.TryGetValue("model", out var m) ? m?.ToString() ?? "" : "";
            Log?.Invoke($"airplay: SETUP cles — client « {clientName} » ({clientModel}, {clientId})");
            if (!string.IsNullOrEmpty(clientName))
                DeviceConnected?.Invoke(clientName, clientId);

            if (_fairPlay.KeyMsg == null)
            {
                Log?.Invoke("airplay: ekey recu sans fp-setup prealable — dechiffrement impossible");
            }
            else
            {
                var aesKey = FairPlayDecrypt.Decrypt(_fairPlay.KeyMsg, ekey);
                if (aesKey != null && _pairing.EcdhSecret is { } ecdh)
                    aesKey = SHA512.HashData(aesKey.Concat(ecdh).ToArray())[..16];
                _aesKey = aesKey;
                if (_aesKey == null)
                    Log?.Invoke("airplay: extraction aeskey impossible — flux chiffre illisible");
            }

        }

        var timingPort = plist != null && plist.TryGetValue("timingPort", out var t) && t is long l ? (int)l : 0;
        if (clientIp != null && timingPort > 0 && _ntp == null)
        {
            _ntp = new NtpTiming(clientIp, timingPort);
            _ntp.Start();
        }
        if (plist != null)
        {
            resp["timingPort"] = (long)(_ntp?.LocalPort ?? 0);
            resp["eventPort"] = 0L;
        }

        if (plist != null && plist.TryGetValue("streams", out var sObj) && sObj is List<object?> streams)
        {
            var resStreams = new List<object?>();
            foreach (var s in streams)
            {
                if (s is not Dictionary<string, object?> sd
                    || !sd.TryGetValue("type", out var tObj) || tObj is not long type)
                    continue;

                if (type == 110)
                {
                    var connId = sd.TryGetValue("streamConnectionID", out var c) && c is long cid ? (ulong)cid : 0UL;
                    var hapMode = _aesKey == null && _pairing.EcdhSecret != null;
                    if (_aesKey == null && !hapMode)
                    {
                        Log?.Invoke("airplay: stream mirror refuse — pas de cle AES");
                        continue;
                    }
                    _mirror?.Dispose();
                    _mirror = new MirrorStreamServer(_aesKey);
                    _mirror.Log += s2 => Log?.Invoke(s2);
                    if (hapMode)
                    {
                        _mirror.InitDataChannel(_pairing.EcdhSecret!, connId);
                        if (_pairing.SrpSessionKey != null)
                            _mirror.AddDataChannelSecret(_pairing.SrpSessionKey);
                    }
                    else
                        _mirror.InitAes(connId);
                    _mirror.Start(ct);
                    StreamStarted?.Invoke(_mirror.Video);
                    Log?.Invoke($"airplay: mirror TCP pret port {_mirror.LocalPort} (streamConnectionID={connId}{(hapMode ? ", canal data HAP" : "")})");
                    resStreams.Add(new Dictionary<string, object?>
                    {
                        ["type"] = 110L,
                        ["dataPort"] = (long)_mirror.LocalPort,
                    });
                }
                else if (type == 96)
                {
                    var data = new UdpClient(0);
                    var ctrl = new UdpClient(0);
                    _audioHolders.AddRange(new[] { data, ctrl });
                    Log?.Invoke("airplay: stream audio (type 96) demande — reception non implementee, ports reserves");
                    resStreams.Add(new Dictionary<string, object?>
                    {
                        ["type"] = 96L,
                        ["dataPort"] = (long)((IPEndPoint)data.Client.LocalEndPoint!).Port,
                        ["controlPort"] = (long)((IPEndPoint)ctrl.Client.LocalEndPoint!).Port,
                    });
                }
            }
            if (resStreams.Count > 0)
                resp["streams"] = resStreams;
        }

        _sessionId ??= Guid.NewGuid().ToString("N")[..16];
        await Respond(req, 200, new Dictionary<string, string>
        {
            ["Session"] = _sessionId,
            ["Content-Type"] = "application/x-apple-binary-plist",
        }, body: PlistCodec.Write(resp), ct: ct);
    }

    private Dictionary<string, object?> BuildInfoPlist() => new()
    {
        ["deviceID"] = AirPlayAdvertiser.DeviceIdPublic,
        ["features"] = 0x038BC946007F8AD0L,
        ["macAddress"] = AirPlayAdvertiser.DeviceIdPublic,
        ["model"] = "TouchMirror",
        ["manufacturer"] = "TouchMirror",
        ["integrator"] = "TouchMirror",
        ["name"] = _receiverName,
        ["nameIsFactoryDefault"] = false,
        ["pi"] = PairingIdentity.PairingId,
        ["pk"] = PairingIdentity.PublicKey,
        ["protocolVersion"] = "1.1",
        ["sourceVersion"] = "377.40.00",
        ["statusFlags"] = 580,
        ["vv"] = 1,
        ["keepAliveLowPower"] = true,
        ["keepAliveSendStatsAsBody"] = true,
        ["audioFormats"] = new List<object?>
        {
            new Dictionary<string, object?> { ["type"] = 100, ["audioInputFormats"] = 67108860L, ["audioOutputFormats"] = 67108860L },
            new Dictionary<string, object?> { ["type"] = 101, ["audioInputFormats"] = 67108860L, ["audioOutputFormats"] = 67108860L },
        },
        ["audioLatencies"] = new List<object?>
        {
            new Dictionary<string, object?> { ["type"] = 100, ["inputLatencyMicros"] = 0L, ["outputLatencyMicros"] = 0L, ["audioType"] = "default" },
            new Dictionary<string, object?> { ["type"] = 101, ["inputLatencyMicros"] = 0L, ["outputLatencyMicros"] = 0L, ["audioType"] = "default" },
        },
        ["displays"] = new List<object?>
        {
            new Dictionary<string, object?>
            {
                ["uuid"] = "e0ff8a27-6738-3d56-8a16-cc53aacee925",
                ["widthPixels"] = 1920, ["heightPixels"] = 1080,
                ["widthPixelsMax"] = 1920, ["heightPixelsMax"] = 1080,
                ["maxFPS"] = 60,
                ["features"] = 14,
            },
        },
    };

    private static Dictionary<string, string> PlistHeaders() => new()
    {
        ["Content-Type"] = "application/x-apple-binary-plist"
    };

    private async Task Respond(RtspRequest req, int code, Dictionary<string, string>? headers = null,
        byte[]? body = null, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.Append("RTSP/1.0 ").Append(code).Append(' ').Append(StatusText(code)).Append("\r\n");
        sb.Append("CSeq: ").Append(req.Header("CSeq") ?? "0").Append("\r\n");
        sb.Append("Server: ").Append(ServerHeader).Append("\r\n");
        if (headers != null)
            foreach (var (k, v) in headers)
                sb.Append(k).Append(": ").Append(v).Append("\r\n");
        if (body is { Length: > 0 })
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        sb.Append("\r\n");
        var head = Encoding.ASCII.GetBytes(sb.ToString());
        var outBuf = body is { Length: > 0 } ? head.Concat(body).ToArray() : head;
        await _stream.WriteAsync(_cipher != null ? _cipher.Encrypt(outBuf) : outBuf, ct);
    }

    private static string StatusText(int code) => code switch
    {
        200 => "OK",
        101 => "Switching Protocols",
        400 => "Bad Request",
        501 => "Not Implemented",
        _ => "Response"
    };

    private async Task<RtspRequest?> ReadRequestAsync(CancellationToken ct)
    {
        var headerBytes = new List<byte>(1024);
        var matched = 0;
        while (matched < 4)
        {
            var b = await ReadPlainByteAsync(ct);
            if (b < 0)
                return null;
            headerBytes.Add((byte)b);
            var expected = matched switch { 0 or 2 => (byte)'\r', _ => (byte)'\n' };
            matched = b == expected ? matched + 1 : (b == (byte)'\r' ? 1 : 0);
        }

        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return null;

        var req = new RtspRequest();
        var parts = lines[0].Split(' ', 3);
        if (parts.Length >= 2)
        {
            req.Method = parts[0];
            req.Path = parts[1];
            if (parts.Length == 3)
                req.Protocol = parts[2];
        }
        else
        {
            return null;
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var sep = lines[i].IndexOf(':');
            if (sep > 0)
                req.Headers[lines[i][..sep].Trim()] = lines[i][(sep + 1)..].Trim();
        }

        if (int.TryParse(req.Header("Content-Length"), out var len) && len > 0)
        {
            if (len > 16 * 1024 * 1024)
                return null;
            req.Body = new byte[len];
            await ReadPlainExactAsync(req.Body, ct);
        }

        return req;
    }

    private async Task<int> ReadPlainByteAsync(CancellationToken ct)
    {
        while (_plainPos >= _plain.Count)
        {
            _plain.Clear();
            _plainPos = 0;
            if (!await FillPlainAsync(ct))
                return -1;
        }
        return _plain[_plainPos++];
    }

    private async Task ReadPlainExactAsync(byte[] buffer, CancellationToken ct)
    {
        var off = 0;
        while (off < buffer.Length)
        {
            if (_plainPos >= _plain.Count)
            {
                _plain.Clear();
                _plainPos = 0;
                if (!await FillPlainAsync(ct))
                    throw new EndOfStreamException();
            }
            var n = Math.Min(buffer.Length - off, _plain.Count - _plainPos);
            _plain.CopyTo(_plainPos, buffer, off, n);
            _plainPos += n;
            off += n;
        }
    }

    private readonly byte[] _fillBuf = new byte[65536];

    private async Task<bool> FillPlainAsync(CancellationToken ct)
    {
        var buf = _fillBuf;
        var n = await _stream.ReadAsync(buf, ct);
        if (n == 0)
            return false;
        _raw.AddRange(buf.AsSpan(0, n).ToArray());

        if (_cipher != null)
        {
            DrainCipher();
            return true;
        }

        if (_cipherPending)
        {
            if (_raw.Count < 2)
                return true;
            var blockLen = BinaryPrimitives.ReadUInt16LittleEndian(CollectionsMarshal.AsSpan(_raw));
            if (blockLen > 0x400)
            {
                _cipherPending = false;
                _plain.AddRange(_raw);
                _raw.Clear();
                return true;
            }
            if (_raw.Count < 2 + blockLen + 16)
                return true;

            foreach (var (secret, tag) in new (byte[]? secret, string tag)[]
                     { (_pairing.SrpSessionKey, "srp"), (_pairing.EcdhSecret, "ecdh") })
            {
                if (secret == null)
                    continue;
                var trial = new ChannelCipher(secret);
                var plain = new byte[blockLen];
                if (trial.TryDecryptBlock(CollectionsMarshal.AsSpan(_raw), plain, out var consumed) < 0)
                    continue;
                _plain.AddRange(plain);
                _raw.RemoveRange(0, consumed);
                _cipher = trial;
                _cipherPending = false;
                DrainCipher();
                Log?.Invoke($"airplay: canal chiffré actif (clé {tag})");
                return true;
            }

            _cipherPending = false;
            _plain.AddRange(_raw);
            _raw.Clear();
            Log?.Invoke("airplay: post-verify en clair");
            return true;
        }

        _plain.AddRange(_raw);
        _raw.Clear();
        return true;
    }

    private void DrainCipher()
    {
        var outBuf = new byte[0x400 * 4];
        var off = 0;
        while (_raw.Count > 0)
        {
            var len = _cipher!.TryDecryptBlock(CollectionsMarshal.AsSpan(_raw), outBuf.AsSpan(off), out var consumed);
            if (len <= 0)
                break;
            off += len;
            _raw.RemoveRange(0, consumed);
            if (off + 0x400 > outBuf.Length)
            {
                _plain.AddRange(outBuf.AsSpan(0, off).ToArray());
                off = 0;
            }
        }
        if (off > 0)
            _plain.AddRange(outBuf.AsSpan(0, off).ToArray());
    }
}
