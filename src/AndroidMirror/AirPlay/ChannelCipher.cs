using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace TouchMirror.AirPlay;

public sealed class ChannelCipher
{
    private const int TagLen = 16;
    private const int MaxBlock = 0x400;

    private readonly byte[] _readKey;
    private readonly byte[] _writeKey;
    private ulong _readCtr;
    private ulong _writeCtr;

    public ChannelCipher(byte[] sharedSecret)
        : this(sharedSecret, "Control-Salt", "Control-Salt",
            "Control-Write-Encryption-Key", "Control-Read-Encryption-Key")
    {
    }

    public ChannelCipher(byte[] sharedSecret, string readSalt, string writeSalt, string readInfo, string writeInfo)
    {
        _readKey = Hkdf(sharedSecret, readSalt, readInfo);
        _writeKey = Hkdf(sharedSecret, writeSalt, writeInfo);
    }

    public int TryDecryptBlock(ReadOnlySpan<byte> input, Span<byte> output, out int consumed)
    {
        consumed = 0;
        if (input.Length < 2 + TagLen)
            return 0;
        var len = BinaryPrimitives.ReadUInt16LittleEndian(input);
        if (len > MaxBlock || output.Length < len)
            return -1;
        if (input.Length < 2 + len + TagLen)
            return 0;

        try
        {
            using var ch = new ChaCha20Poly1305(_readKey);
            ch.Decrypt(Ctr(_readCtr), input.Slice(2, len), input.Slice(2 + len, TagLen), output.Slice(0, len), input[..2]);
        }
        catch (CryptographicException)
        {
            return -1;
        }
        _readCtr++;
        consumed = 2 + len + TagLen;
        return len;
    }

    public byte[] Encrypt(byte[] plain)
    {
        var nblocks = Math.Max(1, (plain.Length + MaxBlock - 1) / MaxBlock);
        var result = new byte[plain.Length + nblocks * (2 + TagLen)];
        var inOff = 0;
        var outOff = 0;
        while (inOff < plain.Length || (plain.Length == 0 && inOff == 0))
        {
            var len = Math.Min(MaxBlock, plain.Length - inOff);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(outOff), (ushort)len);
            using var ch = new ChaCha20Poly1305(_writeKey);
            ch.Encrypt(Ctr(_writeCtr), plain.AsSpan(inOff, len), result.AsSpan(outOff + 2, len),
                result.AsSpan(outOff + 2 + len, TagLen), result.AsSpan(outOff, 2));
            _writeCtr++;
            inOff += len;
            outOff += 2 + len + TagLen;
            if (plain.Length == 0)
                break;
        }
        return result;
    }

    private static byte[] Ctr(ulong counter)
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(nonce.AsSpan(4), counter);
        return nonce;
    }

    private static byte[] Hkdf(byte[] ikm, string salt, string info)
        => HKDF.DeriveKey(HashAlgorithmName.SHA512, ikm, 32,
            Encoding.ASCII.GetBytes(salt), Encoding.ASCII.GetBytes(info));
}
