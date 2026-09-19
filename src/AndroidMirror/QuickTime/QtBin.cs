using System;
using System.IO;
using System.Buffers.Binary;

namespace TouchMirror.QuickTime;

internal static class QtBin
{
    public static uint U32(ReadOnlySpan<byte> d) => BinaryPrimitives.ReadUInt32LittleEndian(d);
    public static ulong U64(ReadOnlySpan<byte> d) => BinaryPrimitives.ReadUInt64LittleEndian(d);
    public static ushort U16(ReadOnlySpan<byte> d) => BinaryPrimitives.ReadUInt16LittleEndian(d);
    public static float F32(ReadOnlySpan<byte> d) => BinaryPrimitives.ReadSingleLittleEndian(d);
    public static double F64(ReadOnlySpan<byte> d) => BinaryPrimitives.ReadDoubleLittleEndian(d);

    public static void W32(Span<byte> d, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(d, v);
    public static void W64(Span<byte> d, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(d, v);

    public static uint Tag(string s) => U32(System.Text.Encoding.ASCII.GetBytes(s));

    public static int ParseLengthAndMagic(ReadOnlySpan<byte> data, uint magic)
    {
        if (data.Length < 8)
            throw new InvalidDataException($"truncated block, expected {TagName(magic)}");
        int length = (int)U32(data);
        uint actual = U32(data[4..]);
        if (actual != magic)
            throw new InvalidDataException($"invalid magic '{TagName(actual)}', expected '{TagName(magic)}'");
        return length;
    }

    public static void WriteLengthAndMagic(Span<byte> data, int length, uint magic)
    {
        W32(data, (uint)length);
        W32(data[4..], magic);
    }

    public static string TagName(uint magic)
        => System.Text.Encoding.ASCII.GetString(BitConverter.GetBytes(magic));
}
