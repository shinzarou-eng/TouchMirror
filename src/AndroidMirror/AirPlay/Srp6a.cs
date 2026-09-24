using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace TouchMirror.AirPlay;

public sealed class Srp6a
{
    private static readonly byte[] NBytes = Convert.FromHexString(
        "AC6BDB41324A9A9BF166DE5E1389582FAF72B6651987EE07FC3192943DB56050A37329CBB4" +
        "A099ED8193E0757767A13DD52312AB4B03310DCD7F48A9DA04FD50E8083969EDB767B0CF60" +
        "95179A163AB3661A05FBD5FAAAE82918A9962F0B93B855F97993EC975EEAA80D740ADBF4FF" +
        "747359D041D5C33EA71D281E446B14773BCA97B43A23FB801676BD207A436C6481F1D2B907" +
        "8717461A5B9D32E688F87748544523B524B0D57D5EA77A2775D2ECFA032CFBDBF52FB37861" +
        "60279004E57AE6AF874E7303CE53299CCC041C7BC308D82A5698F3A8D0C38271AE35F8E9DB" +
        "FBB694B5C803D89F7AE435DE236D525F54759B65E372FCD68EF20FA7111F9E4AFF73");

    private static readonly BigInteger N = ToBig(NBytes);
    private static readonly BigInteger G = 2;
    private static readonly BigInteger K = ToBig(SHA1.HashData(Pad256(NBytes).Concat(Pad256(ToBe(G, 256))).ToArray()));

    private static byte[] Pad256(byte[] be)
    {
        var r = new byte[256];
        Array.Copy(be, 0, r, 256 - be.Length, be.Length);
        return r;
    }

    private static BigInteger ToBig(byte[] bigEndian)
    {
        var b = (byte[])bigEndian.Clone();
        Array.Reverse(b);
        return new BigInteger(b, true, false);
    }

    private static byte[] ToBe(BigInteger v, int size)
    {
        var b = v.ToByteArray(true, true);
        var outBuf = new byte[size];
        var copyLen = Math.Min(size, b.Length);
        Array.Copy(b, b.Length - copyLen, outBuf, size - copyLen, copyLen);
        return outBuf;
    }

    private static byte[] ToBeMin(BigInteger v)
    {
        var b = v.ToByteArray(true, true);
        var off = 0;
        while (off < b.Length - 1 && b[off] == 0) off++;
        return b[off..];
    }

    private static BigInteger ModPow(BigInteger b, BigInteger e)
    {
        BigInteger r = BigInteger.One;
        b %= N;
        while (e > 0)
        {
            if (!e.IsEven)
                r = r * b % N;
            b = b * b % N;
            e >>= 1;
        }
        return r;
    }

    private readonly string _username;
    private readonly byte[] _salt;
    private readonly BigInteger _v;
    private readonly BigInteger _bPriv;
    private readonly BigInteger _bPub;

    public byte[] Salt => _salt;
    public byte[] PublicKey => ToBe(_bPub, 256);
    public byte[] SessionKey { get; private set; } = Array.Empty<byte>();

    public Srp6a(string username, string password)
    {
        _username = username;
        _salt = RandomNumberGenerator.GetBytes(16);
        var ucp = SHA1.HashData(Encoding.UTF8.GetBytes(username + ":" + password));
        var x = ToBig(SHA1.HashData(_salt.Concat(ucp).ToArray()));
        _v = ModPow(G, x);
        _bPriv = ToBig(RandomNumberGenerator.GetBytes(32));
        _bPub = (K * _v + ModPow(G, _bPriv)) % N;
    }

    public bool VerifyClientProof(byte[] clientPub, byte[] clientProof, out byte[] serverProof)
    {
        serverProof = Array.Empty<byte>();
        var A = ToBig(clientPub);
        if (A % N == 0)
            return false;

        var u = ToBig(SHA1.HashData(Pad256(clientPub).Concat(PublicKey).ToArray()));
        var s = ModPow(A * ModPow(_v, u), _bPriv);
        var sMin = ToBeMin(s);
        var k1 = SHA1.HashData(sMin.Concat(new byte[] { 0, 0, 0, 0 }).ToArray());
        var k2 = SHA1.HashData(sMin.Concat(new byte[] { 0, 0, 0, 1 }).ToArray());
        SessionKey = k1.Concat(k2).ToArray();

        var m = ComputeM(clientPub);
        if (!m.AsSpan().SequenceEqual(clientProof))
            return false;

        serverProof = SHA1.HashData(ToBeMin(A).Concat(clientProof).Concat(SessionKey).ToArray());
        return true;
    }

    private byte[] ComputeM(byte[] clientPub)
    {
        var nHash = SHA1.HashData(ToBeMin(N));
        var gHash = SHA1.HashData(ToBeMin(G));
        var uHash = SHA1.HashData(Encoding.UTF8.GetBytes(_username));
        var xo = new byte[20];
        for (var i = 0; i < 20; i++)
            xo[i] = (byte)(nHash[i] ^ gHash[i]);

        var buf = xo.Concat(uHash)
            .Concat(ToBeMin(ToBig(_salt)))
            .Concat(ToBeMin(ToBig(clientPub)))
            .Concat(ToBeMin(_bPub))
            .Concat(SessionKey)
            .ToArray();
        return SHA1.HashData(buf);
    }
}
