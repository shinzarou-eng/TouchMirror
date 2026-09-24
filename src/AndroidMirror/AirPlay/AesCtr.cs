using System;
using System.Security.Cryptography;

namespace TouchMirror.AirPlay;

internal sealed class AesCtr : IDisposable
{
    private readonly ICryptoTransform _encryptor;
    private readonly byte[] _counter;
    private readonly byte[] _keystream = new byte[16];
    private int _keyPos = 16;

    public AesCtr(byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        _encryptor = aes.CreateEncryptor(key, null);
        _counter = (byte[])iv.Clone();
    }

    public byte[] Apply(byte[] data) => Apply(data, 0, data.Length);

    public byte[] Apply(byte[] data, int offset, int length)
    {
        var output = new byte[length];
        Apply(data.AsSpan(offset, length), output);
        return output;
    }

    public void Apply(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var off = 0;
        while (off < input.Length)
        {
            if (_keyPos >= 16)
            {
                _encryptor.TransformBlock(_counter, 0, 16, _keystream, 0);
                Increment();
                _keyPos = 0;
            }
            var n = Math.Min(16 - _keyPos, input.Length - off);
            for (var i = 0; i < n; i++)
                output[off + i] = (byte)(input[off + i] ^ _keystream[_keyPos + i]);
            _keyPos += n;
            off += n;
        }
    }

    private void Increment()
    {
        for (var i = _counter.Length - 1; i >= 0 && ++_counter[i] == 0; i--) { }
    }

    public void Dispose() => _encryptor.Dispose();
}
