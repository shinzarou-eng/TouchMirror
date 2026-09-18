using System.Security.Cryptography;

namespace TouchMirror.AirPlay;

public static class Gcm
{
    public static void Encrypt(byte[] key, byte[] nonce, byte[] plaintext, byte[] ciphertext, byte[] tag)
    {
        var h = HashSubkey(key);
        var j0 = InitialCounter(key, nonce, h);
        Crypt(key, j0, plaintext, ciphertext);
        var s = Ghash(h, ciphertext);
        var ej0 = EncryptBlock(key, j0);
        for (var i = 0; i < 16; i++)
            tag[i] = (byte)(s[i] ^ ej0[i]);
    }

    public static void Decrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag, byte[] plaintext)
    {
        var h = HashSubkey(key);
        var j0 = InitialCounter(key, nonce, h);
        var s = Ghash(h, ciphertext);
        var ej0 = EncryptBlock(key, j0);
        var expected = new byte[16];
        for (var i = 0; i < 16; i++)
            expected[i] = (byte)(s[i] ^ ej0[i]);
        if (!CryptographicOperations.FixedTimeEquals(expected, tag))
            throw new CryptographicException("authtag invalide");
        Crypt(key, j0, ciphertext, plaintext);
    }

    private static byte[] HashSubkey(byte[] key) => EncryptBlock(key, new byte[16]);

    private static byte[] InitialCounter(byte[] key, byte[] nonce, byte[] h)
    {
        if (nonce.Length == 12)
        {
            var j0 = new byte[16];
            Array.Copy(nonce, j0, 12);
            j0[15] = 1;
            return j0;
        }
        return Ghash(h, nonce);
    }

    private static byte[] Ghash(byte[] h, byte[] data)
    {
        var padded = Pad16(data).Concat(BitBytes(data.Length)).ToArray();
        var y = new byte[16];
        for (var off = 0; off < padded.Length; off += 16)
        {
            for (var i = 0; i < 16; i++)
                y[i] ^= padded[off + i];
            y = Multiply(y, h);
        }
        return y;
    }

    private static byte[] Pad16(byte[] data)
    {
        var rem = data.Length % 16;
        if (rem == 0)
            return (byte[])data.Clone();
        var padded = new byte[data.Length + 16 - rem];
        Array.Copy(data, padded, data.Length);
        return padded;
    }

    private static byte[] BitBytes(int byteLen)
    {
        var b = new byte[16];
        var bits = (ulong)byteLen * 8;
        for (var i = 0; i < 8; i++)
            b[15 - i] = (byte)(bits >> (8 * i));
        return b;
    }

    private static byte[] Multiply(byte[] x, byte[] y)
    {
        var z = new byte[16];
        var v = (byte[])y.Clone();
        for (var i = 0; i < 128; i++)
        {
            if (((x[i >> 3] >> (7 - (i & 7))) & 1) != 0)
                for (var j = 0; j < 16; j++)
                    z[j] ^= v[j];
            var lsb = (v[15] & 1) != 0;
            for (var j = 15; j > 0; j--)
                v[j] = (byte)((v[j] >> 1) | (v[j - 1] << 7));
            v[0] >>= 1;
            if (lsb)
                v[0] ^= 0xE1;
        }
        return z;
    }

    private static void Crypt(byte[] key, byte[] j0, byte[] input, byte[] output)
    {
        var counter = (byte[])j0.Clone();
        var off = 0;
        while (off < input.Length)
        {
            Inc32(counter);
            var ks = EncryptBlock(key, counter);
            var n = Math.Min(16, input.Length - off);
            for (var i = 0; i < n; i++)
                output[off + i] = (byte)(input[off + i] ^ ks[i]);
            off += n;
        }
    }

    private static void Inc32(byte[] counter)
    {
        for (var i = 15; i >= 12 && ++counter[i] == 0; i--) { }
    }

    private static byte[] EncryptBlock(byte[] key, byte[] block)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var enc = aes.CreateEncryptor(key, null);
        var output = new byte[16];
        enc.TransformBlock(block, 0, 16, output, 0);
        return output;
    }
}
