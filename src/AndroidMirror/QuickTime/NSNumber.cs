using System;
using System.IO;
using System.Buffers.Binary;

namespace TouchMirror.QuickTime;

internal readonly struct NSNumber
{
    public const uint Magic = 0x6E6D6276;

    public byte TypeSpecifier { get; }
    public uint IntValue { get; }
    public ulong LongValue { get; }
    public double FloatValue { get; }

    private NSNumber(byte type, uint i, ulong l, double f)
        => (TypeSpecifier, IntValue, LongValue, FloatValue) = (type, i, l, f);

    public static NSNumber FromUInt32(uint v) => new(3, v, 0, 0);
    public static NSNumber FromFloat64(double v) => new(6, 0, 0, v);

    public static NSNumber Parse(ReadOnlySpan<byte> data)
    {
        byte type = data[0];
        return type switch
        {
            6 when data.Length == 9 => new(type, 0, 0, QtBin.F64(data[1..])),
            5 when data.Length == 5 => new(type, QtBin.U32(data[1..]), 0, 0),
            4 when data.Length == 9 => new(type, 0, QtBin.U64(data[1..]), 0),
            3 when data.Length == 5 => new(type, QtBin.U32(data[1..]), 0, 0),
            _ => throw new InvalidDataException($"invalid NSNumber type {type} len {data.Length}"),
        };
    }

    public byte[] ToBytes()
    {
        var result = new byte[TypeSpecifier == 3 ? 5 : 9];
        result[0] = TypeSpecifier;
        switch (TypeSpecifier)
        {
            case 6: QtBin.W64(result.AsSpan(1), BitConverter.DoubleToUInt64Bits(FloatValue)); break;
            case 4: QtBin.W64(result.AsSpan(1), LongValue); break;
            case 3: QtBin.W32(result.AsSpan(1), IntValue); break;
            default: throw new InvalidDataException($"unknown NSNumber type {TypeSpecifier}");
        }
        return result;
    }

    public override string ToString() => TypeSpecifier switch
    {
        6 => $"Float64[{FloatValue:F6}]",
        3 => $"Int32[{IntValue}]",
        4 => $"UInt64[{LongValue}]",
        _ => "Invalid Type Specifier",
    };
}
