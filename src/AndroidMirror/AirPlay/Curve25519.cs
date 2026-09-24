using System.Numerics;
using System.Security.Cryptography;

namespace TouchMirror.AirPlay;

public static class Curve25519
{
    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    private static readonly BigInteger L = BigInteger.Parse("7237005577332262213973186563042994240857116359379907606001950938285454250989");
    private static readonly BigInteger D = BigInteger.Parse("37095705934669439343138083508754565189542113879843219016388785533085940283555");
    private static readonly BigInteger I = ModPow(BigInteger.Parse("2"), (P - 1) / 4, P);

    private static BigInteger Mod(BigInteger x) { var r = x % P; return r < 0 ? r + P : r; }

    private static BigInteger ModPow(BigInteger b, BigInteger e, BigInteger m)
    {
        BigInteger r = BigInteger.One;
        b %= m;
        while (e > 0)
        {
            if (!e.IsEven)
                r = r * b % m;
            b = b * b % m;
            e >>= 1;
        }
        return r;
    }

    private static BigInteger LoadScalar(byte[] data)
    {
        var v = new byte[32];
        Array.Copy(data, v, 32);
        v[31] &= 0x7f;
        return new BigInteger(v, true, false);
    }

    public static byte[] X25519(byte[] scalarIn, byte[] pointIn)
    {
        var k = (byte[])scalarIn.Clone();
        k[0] &= 248;
        k[31] = (byte)((k[31] & 0x7f) | 0x40);
        var scalar = new BigInteger(k, true, false);
        var u = LoadScalar(pointIn);

        BigInteger x1 = u, x2 = 1, z2 = 0, x3 = u, z3 = 1;
        var swap = 0;
        for (var t = 254; t >= 0; t--)
        {
            var kt = (int)(scalar >> t & 1);
            swap ^= kt;
            if (swap == 1) { (x2, x3) = (x3, x2); (z2, z3) = (z3, z2); }
            swap = kt;

            var a = Mod(x2 + z2);
            var aa = Mod(a * a);
            var b = Mod(x2 - z2);
            var bb = Mod(b * b);
            var e = Mod(aa - bb);
            var c = Mod(x3 + z3);
            var d = Mod(x3 - z3);
            var da = Mod(d * a);
            var cb = Mod(c * b);
            x3 = Mod((da + cb) * (da + cb));
            z3 = Mod(x1 * Mod(da - cb) * Mod(da - cb));
            x2 = Mod(aa * bb);
            z2 = Mod(e * (aa + Mod(e * 121665)));
        }
        if (swap == 1) { (x2, x3) = (x3, x2); (z2, z3) = (z3, z2); }

        var result = Mod(x2 * ModPow(z2, P - 2, P));
        var bytes = result.ToByteArray(true, false);
        var outBuf = new byte[32];
        Array.Copy(bytes, 0, outBuf, 0, Math.Min(32, bytes.Length));
        return outBuf;
    }

    private static readonly BigInteger BaseX = Mod(BigInteger.Parse("15112221349535400772501151409588531511454012693041857206046113283949847762202"));
    private static readonly BigInteger BaseY = Mod(BigInteger.Parse("46316835694926478169428394003475163141307993866256225615783033603165251855960"));

    private static (BigInteger X, BigInteger Y) EdwardsAdd((BigInteger X, BigInteger Y) p, (BigInteger X, BigInteger Y) q)
    {
        var x1y2 = Mod(p.X * q.Y);
        var y1x2 = Mod(p.Y * q.X);
        var y1y2 = Mod(p.Y * q.Y);
        var x1x2 = Mod(p.X * q.X);
        var dxxyy = Mod(D * x1x2 * y1y2);
        var denomX = ModPow(Mod(1 + dxxyy), P - 2, P);
        var denomY = ModPow(Mod(1 - dxxyy), P - 2, P);
        return (Mod((x1y2 + y1x2) * denomX), Mod((y1y2 + x1x2) * denomY));
    }

    private static (BigInteger X, BigInteger Y) EdwardsMul((BigInteger X, BigInteger Y) p, BigInteger n)
    {
        (BigInteger X, BigInteger Y) r = (0, 1);
        var add = p;
        while (n > 0)
        {
            if (!n.IsEven)
                r = EdwardsAdd(r, add);
            add = EdwardsAdd(add, add);
            n >>= 1;
        }
        return r;
    }

    private static byte[] EncodePoint((BigInteger X, BigInteger Y) p)
    {
        var y = p.Y;
        if (!p.X.IsEven)
            y += BigInteger.Pow(2, 255);
        var bytes = y.ToByteArray(true, false);
        var outBuf = new byte[32];
        Array.Copy(bytes, 0, outBuf, 0, Math.Min(32, bytes.Length));
        return outBuf;
    }

    private static (BigInteger X, BigInteger Y) DecodePoint(byte[] enc)
    {
        var v = (byte[])enc.Clone();
        var sign = (v[31] & 0x80) != 0;
        v[31] &= 0x7f;
        var y = new BigInteger(v, true, false);
        var y2 = Mod(y * y);
        var u = Mod(y2 - 1);
        var w = Mod(D * y2 + 1);
        var x = ModPow(Mod(u * ModPow(w, P - 2, P)), (P + 3) / 8, P);
        if (Mod(x * x - u * ModPow(w, P - 2, P)) != 0)
            x = Mod(x * I);
        if (x.IsEven == sign)
            x = Mod(-x);
        return (x, y);
    }

    private static byte[] Sha512(byte[] data) => SHA512.HashData(data);

    public static byte[] Ed25519PublicKey(byte[] privateKey32)
    {
        var h = Sha512(privateKey32);
        h[0] &= 248;
        h[31] = (byte)((h[31] & 0x3f) | 0x40);
        var a = new BigInteger(h[..32], true, false);
        return EncodePoint(EdwardsMul((BaseX, BaseY), a));
    }

    public static byte[] Ed25519Sign(byte[] privateKey32, byte[] message)
    {
        var h = Sha512(privateKey32);
        var secret = (byte[])h[..32].Clone();
        secret[0] &= 248;
        secret[31] = (byte)((secret[31] & 0x3f) | 0x40);
        var a = new BigInteger(secret, true, false);
        var prefix = h[32..];

        var A = EncodePoint(EdwardsMul((BaseX, BaseY), a));

        var r = new BigInteger(Sha512(Concat(prefix, message)), true, false) % L;
        var R = EncodePoint(EdwardsMul((BaseX, BaseY), r));

        var k = new BigInteger(Sha512(Concat(R, A, message)), true, false) % L;
        var s = (r + k * a) % L;
        var sBytes = s.ToByteArray(true, false);
        var sPadded = new byte[32];
        Array.Copy(sBytes, 0, sPadded, 0, Math.Min(32, sBytes.Length));

        var sig = new byte[64];
        Array.Copy(R, sig, 32);
        Array.Copy(sPadded, 0, sig, 32, 32);
        return sig;
    }

    public static bool Ed25519Verify(byte[] publicKey32, byte[] message, byte[] signature64)
    {
        try
        {
            var A = DecodePoint(publicKey32);
            var R = signature64[..32];
            var s = new BigInteger(signature64[32..], true, false);
            if (s >= L)
                return false;

            var k = new BigInteger(Sha512(Concat(R, publicKey32, message)), true, false) % L;
            var left = EdwardsMul((BaseX, BaseY), s);
            var right = EdwardsAdd(DecodePoint(R), EdwardsMul(A, k));
            return EncodePoint(left).SequenceEqual(EncodePoint(right));
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var r = new byte[total];
        var off = 0;
        foreach (var p in parts)
        {
            Array.Copy(p, 0, r, off, p.Length);
            off += p.Length;
        }
        return r;
    }
}
