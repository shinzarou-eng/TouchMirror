using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TouchMirror.AirPlay;

public sealed class PairingIdentityStore
{
    public byte[] PrivateKey { get; private set; } = Array.Empty<byte>();
    public byte[] PublicKey { get; private set; } = Array.Empty<byte>();
    public string PairingId { get; private set; } = "";

    private static string StorePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TouchMirror", "airplay-identity.json");

    public static PairingIdentityStore Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var doc = JsonDocument.Parse(File.ReadAllText(StorePath)).RootElement;
                var priv = Convert.FromHexString(doc.GetProperty("priv").GetString()!);
                var id = doc.GetProperty("id").GetString()!;
                if (priv.Length == 32)
                    return new PairingIdentityStore { PrivateKey = priv, PublicKey = Curve25519.Ed25519PublicKey(priv), PairingId = id };
            }
        }
        catch { }

        var store = new PairingIdentityStore
        {
            PrivateKey = RandomNumberGenerator.GetBytes(32),
            PairingId = Guid.NewGuid().ToString()
        };
        store.PublicKey = Curve25519.Ed25519PublicKey(store.PrivateKey);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(new
            {
                priv = Convert.ToHexString(store.PrivateKey).ToLowerInvariant(),
                id = store.PairingId
            }));
        }
        catch { }
        return store;
    }
}

public sealed class Pairing
{
    private const string Pin = "3939";

    private readonly PairingIdentityStore _identity;
    private Srp6a? _srp;
    private byte[]? _verifyPriv;
    private byte[]? _verifyPub;
    private byte[]? _clientEcdh;
    private byte[]? _clientLtpk;
    private AesCtr? _ctr;
    private byte[]? _srpSessionKey;
    private byte[]? _ecdhSecret;
    private Srp6aHap? _srpHap;
    private byte[]? _verifyShared;

    public byte[] PublicKey => _identity.PublicKey;
    public string PairingId => _identity.PairingId;

    public Pairing(PairingIdentityStore identity) => _identity = identity;

    public byte[]? HandlePairSetup(byte[] body)
    {
        if (body.Length == 32)
        {
            return _identity.PublicKey;
        }

        var tlv = Tlv8.Parse(body);
        if (tlv.TryGetValue(Tlv8.State, out var stateV) && stateV.Length == 1)
            return HandleTlvPairSetup(tlv, stateV[0]);

        var plist = PlistCodec.Read(body) as Dictionary<string, object?>;
        if (plist == null)
            return null;

        if (plist.TryGetValue("method", out var m) && m is string method && method == "pin"
            && plist.TryGetValue("user", out var u) && u is string user)
        {
            _srp = new Srp6a(user, Pin);
            return PlistCodec.Write(new Dictionary<string, object?>
            {
                ["pk"] = _srp.PublicKey,
                ["salt"] = _srp.Salt
            });
        }

        if (_srp != null && plist.TryGetValue("pk", out var pkObj) && pkObj is byte[] clientPk
            && plist.TryGetValue("proof", out var proofObj) && proofObj is byte[] clientProof)
        {
            if (!_srp.VerifyClientProof(clientPk, clientProof, out var m2))
                return null;
            _srpSessionKey = _srp.SessionKey;
            return PlistCodec.Write(new Dictionary<string, object?>
            {
                ["proof"] = m2
            });
        }

        if (_srpSessionKey != null && plist.TryGetValue("epk", out var epkObj) && epkObj is byte[] epk
            && plist.TryGetValue("authTag", out var tagObj) && tagObj is byte[] authTag
            && epk.Length == 32 && authTag.Length == 16)
        {
            var aesKey = SHA512.HashData(Encoding.ASCII.GetBytes("Pair-Setup-AES-Key").Concat(_srpSessionKey).ToArray())[..16];
            var aesIv = SHA512.HashData(Encoding.ASCII.GetBytes("Pair-Setup-AES-IV").Concat(_srpSessionKey).ToArray())[..16];
            aesIv[15]++;
            try
            {
                var clientLtpk = new byte[32];
                Gcm.Decrypt(aesKey, aesIv, epk, authTag, clientLtpk);
                _clientLtpk = clientLtpk;
            }
            catch (CryptographicException)
            {
                return null;
            }
            aesIv[15]++;
            var serverEpk = new byte[32];
            var serverTag = new byte[16];
            Gcm.Encrypt(aesKey, aesIv, _identity.PublicKey, serverEpk, serverTag);
            return PlistCodec.Write(new Dictionary<string, object?>
            {
                ["epk"] = serverEpk,
                ["authTag"] = serverTag
            });
        }

        return null;
    }

    private byte[]? HandleTlvPairSetup(Dictionary<byte, byte[]> tlv, byte state)
    {
        switch (state)
        {
            case 1:
                if (!tlv.TryGetValue(Tlv8.Method, out var m) || m.Length != 1 || m[0] != 0)
                    return null;
                _srpHap = new Srp6aHap(Pin);
                return Tlv8.Format(
                    (Tlv8.State, new byte[] { 2 }),
                    (Tlv8.Salt, _srpHap.Salt),
                    (Tlv8.PublicKey, _srpHap.PublicKey));

            case 3:
                if (_srpHap == null
                    || !tlv.TryGetValue(Tlv8.PublicKey, out var pkA)
                    || !tlv.TryGetValue(Tlv8.Proof, out var proof))
                    return null;
                if (!_srpHap.VerifyClientProof(pkA, proof, out var m2))
                    return Tlv8.Format((Tlv8.State, new byte[] { 4 }), (Tlv8.Error, new byte[] { 2 }));
                _srpSessionKey = _srpHap.SessionKey;
                return Tlv8.Format((Tlv8.State, new byte[] { 4 }), (Tlv8.Proof, m2!));

            case 5:
                if (_srpSessionKey == null || !tlv.TryGetValue(Tlv8.EncryptedData, out var ed) || ed.Length < 16)
                    return null;
                var key = Hkdf(_srpSessionKey, "Pair-Setup-Encrypt-Salt", "Pair-Setup-Encrypt-Info");
                var plain = new byte[ed.Length - 16];
                try
                {
                    using var ch = new ChaCha20Poly1305(key);
                    ch.Decrypt(Nonce("PS-Msg05"), ed.AsSpan(0, ed.Length - 16), ed.AsSpan(ed.Length - 16), plain);
                }
                catch (CryptographicException)
                {
                    return Tlv8.Format((Tlv8.State, new byte[] { 6 }), (Tlv8.Error, new byte[] { 2 }));
                }
                var inner = Tlv8.Parse(plain);
                if (inner.TryGetValue(Tlv8.PublicKey, out var clientLtpk) && clientLtpk.Length == 32)
                    _clientLtpk = clientLtpk;

                var accX = Hkdf(_srpSessionKey, "Pair-Setup-Accessory-Sign-Salt", "Pair-Setup-Accessory-Sign-Info");
                var idBytes = Encoding.ASCII.GetBytes(Services.AirPlayAdvertiser.DeviceIdPublic);
                var sig = Curve25519.Ed25519Sign(_identity.PrivateKey,
                    accX.Concat(idBytes).Concat(_identity.PublicKey).ToArray());
                var payload = Tlv8.Format(
                    (Tlv8.Identifier, idBytes),
                    (Tlv8.Signature, sig),
                    (Tlv8.PublicKey, _identity.PublicKey));
                var cipher = new byte[payload.Length];
                var tag = new byte[16];
                using (var ch = new ChaCha20Poly1305(key))
                    ch.Encrypt(Nonce("PS-Msg06"), payload, cipher, tag);
                return Tlv8.Format(
                    (Tlv8.State, new byte[] { 6 }),
                    (Tlv8.EncryptedData, cipher.Concat(tag).ToArray()));
        }
        return null;
    }

    private byte[]? HandleTlvPairVerify(Dictionary<byte, byte[]> tlv, byte state)
    {
        switch (state)
        {
            case 1:
                if (!tlv.TryGetValue(Tlv8.PublicKey, out var clientEph) || clientEph.Length != 32)
                    return null;
                _clientEcdh = clientEph;
                _verifyPriv = RandomNumberGenerator.GetBytes(32);
                _verifyPub = Curve25519.X25519(_verifyPriv, BasePoint());
                _verifyShared = Curve25519.X25519(_verifyPriv, clientEph);
                _ecdhSecret = _verifyShared;

                var idBytes = Encoding.ASCII.GetBytes(Services.AirPlayAdvertiser.DeviceIdPublic);
                var sig = Curve25519.Ed25519Sign(_identity.PrivateKey,
                    _verifyPub.Concat(idBytes).Concat(clientEph).ToArray());
                var payload = Tlv8.Format((Tlv8.Identifier, idBytes), (Tlv8.Signature, sig));
                var key = Hkdf(_verifyShared, "Pair-Verify-Encrypt-Salt", "Pair-Verify-Encrypt-Info");
                var cipher = new byte[payload.Length];
                var tag = new byte[16];
                using (var ch = new ChaCha20Poly1305(key))
                    ch.Encrypt(Nonce("PV-Msg02"), payload, cipher, tag);
                return Tlv8.Format(
                    (Tlv8.State, new byte[] { 2 }),
                    (Tlv8.PublicKey, _verifyPub),
                    (Tlv8.EncryptedData, cipher.Concat(tag).ToArray()));

            case 3:
                if (_verifyShared == null || !tlv.TryGetValue(Tlv8.EncryptedData, out var ed) || ed.Length < 16)
                    return null;
                var key3 = Hkdf(_verifyShared, "Pair-Verify-Encrypt-Salt", "Pair-Verify-Encrypt-Info");
                var plain3 = new byte[ed.Length - 16];
                try
                {
                    using var ch = new ChaCha20Poly1305(key3);
                    ch.Decrypt(Nonce("PV-Msg03"), ed.AsSpan(0, ed.Length - 16), ed.AsSpan(ed.Length - 16), plain3);
                }
                catch (CryptographicException)
                {
                    return Tlv8.Format((Tlv8.State, new byte[] { 4 }), (Tlv8.Error, new byte[] { 2 }));
                }
                var inner3 = Tlv8.Parse(plain3);
                Verified = true;
                return Tlv8.Format((Tlv8.State, new byte[] { 4 }));
        }
        return null;
    }

    private static byte[] Hkdf(byte[] ikm, string salt, string info)
        => System.Security.Cryptography.HKDF.DeriveKey(
            HashAlgorithmName.SHA512, ikm, 32,
            Encoding.ASCII.GetBytes(salt), Encoding.ASCII.GetBytes(info));

    private static byte[] Nonce(string suffix)
    {
        var nonce = new byte[12];
        Encoding.ASCII.GetBytes(suffix).CopyTo(nonce, 4);
        return nonce;
    }

    public byte[]? HandlePairVerify(byte[] body)
    {
        if (body.Length >= 3 && body[0] == Tlv8.State)
        {
            var tlv = Tlv8.Parse(body);
            if (tlv.TryGetValue(Tlv8.State, out var stateV) && stateV.Length == 1)
                return HandleTlvPairVerify(tlv, stateV[0]);
        }

        if (body.Length == 4 + 32 + 32 && body[0] == 1)
        {
            _clientEcdh = body[4..36];
            _clientLtpk = body[36..68];
            _verifyPriv = RandomNumberGenerator.GetBytes(32);
            _verifyPub = Curve25519.X25519(_verifyPriv, BasePoint());
            var shared = Curve25519.X25519(_verifyPriv, _clientEcdh);
            _ecdhSecret = shared;

            var aesKey = SHA512.HashData(Encoding.ASCII.GetBytes("Pair-Verify-AES-Key").Concat(shared).ToArray())[..16];
            var aesIv = SHA512.HashData(Encoding.ASCII.GetBytes("Pair-Verify-AES-IV").Concat(shared).ToArray())[..16];
            _ctr = new AesCtr(aesKey, aesIv);

            var sigMsg = _verifyPub.Concat(_clientEcdh).ToArray();
            var signature = Curve25519.Ed25519Sign(_identity.PrivateKey, sigMsg);
            var encSig = _ctr.Apply(signature);

            var resp = new byte[32 + encSig.Length];
            Array.Copy(_verifyPub, resp, 32);
            Array.Copy(encSig, 0, resp, 32, encSig.Length);
            return resp;
        }

        if (body.Length == 4 + 64 && body[0] == 0 && _ctr != null && _verifyPub != null && _clientEcdh != null && _clientLtpk != null)
        {
            var encSig = body[4..68];
            var clientSig = _ctr.Apply(encSig);
            var expected = _clientEcdh.Concat(_verifyPub).ToArray();
            if (!Curve25519.Ed25519Verify(_clientLtpk, expected, clientSig))
                return null;
            Verified = true;
            return Array.Empty<byte>();
        }

        return null;
    }

    public bool Verified { get; private set; }
    public byte[]? EcdhSecret => _ecdhSecret;
    public byte[]? SrpSessionKey => _srpSessionKey;

    private static byte[] BasePoint()
    {
        var b = new byte[32];
        b[0] = 9;
        return b;
    }
}
