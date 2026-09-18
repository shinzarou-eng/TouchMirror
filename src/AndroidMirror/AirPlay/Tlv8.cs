using System.IO;
using System.Text;

namespace TouchMirror.AirPlay;

public static class Tlv8
{
    public const byte Method = 0;
    public const byte Identifier = 1;
    public const byte Salt = 2;
    public const byte PublicKey = 3;
    public const byte Proof = 4;
    public const byte EncryptedData = 5;
    public const byte State = 6;
    public const byte Error = 7;
    public const byte Signature = 10;
    public const byte Flags = 19;

    public static Dictionary<byte, byte[]> Parse(byte[] data)
    {
        var dict = new Dictionary<byte, byte[]>();
        var i = 0;
        while (i + 2 <= data.Length)
        {
            var type = data[i];
            var len = data[i + 1];
            i += 2;
            if (i + len > data.Length)
                break;
            var val = data.AsSpan(i, len).ToArray();
            if (dict.TryGetValue(type, out var existing))
                dict[type] = existing.Concat(val).ToArray();
            else
                dict[type] = val;
            i += len;
        }
        return dict;
    }

    public static byte[] Format(params (byte Type, byte[] Value)[] items)
    {
        using var ms = new MemoryStream();
        foreach (var (type, value) in items)
        {
            var off = 0;
            do
            {
                var chunk = Math.Min(255, value.Length - off);
                ms.WriteByte(type);
                ms.WriteByte((byte)chunk);
                ms.Write(value, off, chunk);
                off += chunk;
            }
            while (off < value.Length);
        }
        return ms.ToArray();
    }

    public static string Str(byte[] v) => Encoding.UTF8.GetString(v);
}
