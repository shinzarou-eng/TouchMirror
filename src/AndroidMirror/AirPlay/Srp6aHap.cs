using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace TouchMirror.AirPlay;

public sealed class Srp6aHap
{
    private const int NLen = 384;
    private const string User = "Pair-Setup";

    private static readonly BigInteger N = ParseHex(
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E088A67CC74020BBEA63B" +
        "139B22514A08798E3404DDEF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245E485" +
        "B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7EDEE386BFB5A899FA5AE9F24117C4B1F" +
        "E649286651ECE45B3DC2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F83655D23" +
        "DCA3AD961C62F356208552BB9ED529077096966D670C354E4ABC9804F1746C08CA18217C32" +
        "905E462E36CE3BE39E772C180E86039B2783A2EC07A28FB5C55DF06F4C52C9DE2BCBF69558" +
        "17183995497CEA956AE515D2261898FA051015728E5A8AAAC42DAD33170D04507A33A85521" +
        "ABDF1CBA64ECFB850458DBEF0A8AEA71575D060C7DB3970F85A6E1E4C7ABF5AE8CDB0933D7" +
        "1E8C94E04A25619DCEE3D2261AD2EE6BF12FFA06D98A0864D87602733EC86A64521F2B1817" +
        "7B200CBBE117577A615D6C770988C0BAD946E208E24FA074E5AB3143DB5BFCE0FD108E4B82" +
        "D120A93AD2CAFFFFFFFFFFFFFFFF");
    private static readonly BigInteger G = 5;

    private readonly BigInteger _v;
    private readonly BigInteger _b;
    private readonly BigInteger _B;
    private readonly BigInteger _saltBn;

    public byte[] Salt { get; }
    public byte[] PublicKey => Min(_B);
    public byte[]? SessionKey { get; private set; }

    public Srp6aHap(string password)
    {
        Salt = RandomNumberGenerator.GetBytes(16);
        _saltBn = ToBig(Salt);

        var ucp = SHA512.HashData(Encoding.ASCII.GetBytes($"{User}:{password}"));
        var x = ToBig(HashNs(_saltBn, ucp));
        _v = BigInteger.ModPow(G, x, N);
        var k = ToBig(HashNnPad(N, G));
        _b = ToBig(RandomNumberGenerator.GetBytes(32));
        _B = (k * _v + BigInteger.ModPow(G, _b, N)) % N;
    }

    public bool VerifyClientProof(byte[] aBytes, byte[] m1, out byte[]? m2)
    {
        m2 = null;
        var A = ToBig(aBytes);
        if (A.IsZero || A % N == 0)
            return false;

        var u = ToBig(HashNnPad(A, _B));
        var S = BigInteger.ModPow(A * BigInteger.ModPow(_v, u, N), _b, N);
        var K = SHA512.HashData(Min(S));
        SessionKey = K;

        var hN = SHA512.HashData(Min(N));
        var hG = SHA512.HashData(Min(G));
        var hI = SHA512.HashData(Encoding.ASCII.GetBytes(User));
        var expected = SHA512.HashData(Xor(hN, hG)
            .Concat(hI)
            .Concat(Min(_saltBn))
            .Concat(Min(A))
            .Concat(Min(_B))
            .Concat(K)
            .ToArray());
        if (!expected.AsSpan().SequenceEqual(m1))
            return false;

        m2 = SHA512.HashData(Min(A).Concat(m1).Concat(K).ToArray());
        return true;
    }

    private static byte[] HashNnPad(BigInteger a, BigInteger b)
    {
        var buf = new byte[2 * NLen];
        WritePadded(a, buf, 0);
        WritePadded(b, buf, NLen);
        return SHA512.HashData(buf);
    }

    private static byte[] HashNs(BigInteger n, byte[] bytes)
        => SHA512.HashData(Min(n).Concat(bytes).ToArray());

    private static void WritePadded(BigInteger n, byte[] buf, int offset)
    {
        var b = Min(n);
        b.CopyTo(buf, offset + NLen - b.Length);
    }

    private static byte[] Min(BigInteger n) => n.ToByteArray(isUnsigned: true, isBigEndian: true);

    private static BigInteger ToBig(byte[] bytes)
    {
        var copy = (byte[])bytes.Clone();
        Array.Reverse(copy);
        return new BigInteger(copy, isUnsigned: true);
    }

    private static BigInteger ParseHex(string hex) => ToBig(Convert.FromHexString(hex));

    private static byte[] Xor(byte[] a, byte[] b)
    {
        var r = new byte[a.Length];
        for (var i = 0; i < a.Length; i++)
            r[i] = (byte)(a[i] ^ b[i]);
        return r;
    }
}
