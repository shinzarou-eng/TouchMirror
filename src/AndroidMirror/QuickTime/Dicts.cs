using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TouchMirror.QuickTime;

internal static class DictMagic
{
    public const uint KeyValuePair = 0x6B657976;
    public const uint StringKey = 0x7374726B;
    public const uint IntKey = 0x6964786B;
    public const uint BooleanValue = 0x62756C76;
    public const uint Dictionary = 0x64696374;
    public const uint DataValue = 0x64617476;
    public const uint StringValue = 0x73747276;
}

internal sealed class StringKeyDict
{
    public readonly List<KeyValuePair<string, object?>> Entries = new();

    public static StringKeyDict Parse(ReadOnlySpan<byte> data)
    {
        QtBin.ParseLengthAndMagic(data, DictMagic.Dictionary);
        var slice = data[8..];
        var dict = new StringKeyDict();
        while (slice.Length > 0)
        {
            int kvLen = QtBin.ParseLengthAndMagic(slice, DictMagic.KeyValuePair);
            dict.Entries.Add(ParseEntry(slice[8..kvLen]));
            slice = slice[kvLen..];
        }
        return dict;
    }

    public static KeyValuePair<string, object?> ParseKeyValueEntry(ReadOnlySpan<byte> data)
    {
        int kvLen = QtBin.ParseLengthAndMagic(data, DictMagic.KeyValuePair);
        return ParseEntry(data[8..kvLen]);
    }

    private static KeyValuePair<string, object?> ParseEntry(ReadOnlySpan<byte> bytes)
    {
        int keyLen = QtBin.ParseLengthAndMagic(bytes, DictMagic.StringKey);
        string key = Encoding.ASCII.GetString(bytes[8..keyLen]);
        var value = ParseValue(bytes[keyLen..]);
        return new(key, value);
    }

    internal static object? ParseValue(ReadOnlySpan<byte> bytes)
    {
        int valueLength = (int)QtBin.U32(bytes);
        if (bytes.Length < valueLength)
            throw new InvalidDataException("invalid value data length");
        uint magic = QtBin.U32(bytes[4..]);
        return magic switch
        {
            DictMagic.StringValue => Encoding.ASCII.GetString(bytes[8..valueLength]),
            DictMagic.DataValue => bytes[8..valueLength].ToArray(),
            DictMagic.BooleanValue => bytes[8] == 1,
            NSNumber.Magic => NSNumber.Parse(bytes[8..valueLength]),
            DictMagic.Dictionary => ParseNestedDict(bytes[..valueLength]),
            FormatDescriptor.Magic => FormatDescriptor.Parse(bytes[..valueLength]),
            _ => throw new InvalidDataException($"unknown dict value magic '{QtBin.TagName(magic)}'"),
        };
    }

    private static object? ParseNestedDict(ReadOnlySpan<byte> bytes)
    {
        try { return Parse(bytes); }
        catch { return IndexKeyDict.Parse(bytes); }
    }

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        Span<byte> head = stackalloc byte[8];
        foreach (var (key, value) in Entries)
        {
            var keyBytes = Encoding.ASCII.GetBytes(key);
            var valueBytes = SerializeValue(value);
            QtBin.WriteLengthAndMagic(head, keyBytes.Length + 8 + valueBytes.Length + 8, DictMagic.KeyValuePair);
            ms.Write(head);
            QtBin.WriteLengthAndMagic(head, keyBytes.Length + 8, DictMagic.StringKey);
            ms.Write(head);
            ms.Write(keyBytes);
            ms.Write(valueBytes);
        }
        var body = ms.ToArray();
        var result = new byte[body.Length + 8];
        QtBin.WriteLengthAndMagic(result, result.Length, DictMagic.Dictionary);
        body.CopyTo(result, 8);
        return result;
    }

    private static byte[] SerializeValue(object? value) => value switch
    {
        bool b => Wrap(new[] { (byte)(b ? 1 : 0) }, null, DictMagic.BooleanValue),
        NSNumber n => Wrap(n.ToBytes(), null, NSNumber.Magic),
        string s => Wrap(Encoding.ASCII.GetBytes(s), null, DictMagic.StringValue),
        byte[] d => Wrap(d, null, DictMagic.DataValue),
        StringKeyDict dict => dict.Serialize(),
        _ => throw new InvalidDataException($"cannot serialize dict value {value}"),
    };

    private static byte[] Wrap(byte[] content, int? forcedLength, uint magic)
    {
        var result = new byte[content.Length + 8];
        QtBin.WriteLengthAndMagic(result, forcedLength ?? result.Length, magic);
        content.CopyTo(result, 8);
        return result;
    }

    public override string ToString()
        => $"StringKeyDict:[{string.Concat(Entries.Select(e => $"{{{e.Key} : {Fmt(e.Value)}}},"))}]";

    internal static string Fmt(object? v) => v switch
    {
        byte[] b => $"0x{Convert.ToHexString(b).ToLowerInvariant()}",
        _ => v?.ToString() ?? "null",
    };
}

internal sealed class IndexKeyDict
{
    public readonly List<KeyValuePair<ushort, object?>> Entries = new();

    public object? Get(ushort key)
        => Entries.FirstOrDefault(e => e.Key == key).Value
           ?? throw new InvalidDataException($"key {key} not found");

    public static IndexKeyDict Parse(ReadOnlySpan<byte> data)
        => ParseWithMarker(data, DictMagic.Dictionary);

    public static IndexKeyDict ParseWithMarker(ReadOnlySpan<byte> data, uint magic)
    {
        QtBin.ParseLengthAndMagic(data, magic);
        var slice = data[8..];
        var dict = new IndexKeyDict();
        while (slice.Length > 0)
        {
            int kvLen = QtBin.ParseLengthAndMagic(slice, DictMagic.KeyValuePair);
            dict.Entries.Add(ParseEntry(slice[8..kvLen]));
            slice = slice[kvLen..];
        }
        return dict;
    }

    private static KeyValuePair<ushort, object?> ParseEntry(ReadOnlySpan<byte> bytes)
    {
        int keyLen = QtBin.ParseLengthAndMagic(bytes, DictMagic.IntKey);
        ushort key = QtBin.U16(bytes[8..]);
        var value = StringKeyDict.ParseValue(bytes[keyLen..]);
        return new(key, value);
    }

    public override string ToString()
        => $"IndexKeyDict:[{string.Concat(Entries.Select(e => $"{{{e.Key} : {StringKeyDict.Fmt(e.Value)}}},"))}]";
}
