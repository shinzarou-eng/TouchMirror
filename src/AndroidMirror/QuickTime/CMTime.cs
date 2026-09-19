using System;
using System.IO;

namespace TouchMirror.QuickTime;

internal readonly struct CMTime
{
    public const uint FlagsValid = 0x0;
    public const uint FlagsHasBeenRounded = 0x1;
    public const uint FlagsPositiveInfinity = 0x2;
    public const uint FlagsNegativeInfinity = 0x4;
    public const uint FlagsIndefinite = 0x8;
    public const int LengthInBytes = 24;

    public ulong Value { get; init; }
    public uint Scale { get; init; }
    public uint Flags { get; init; }
    public ulong Epoch { get; init; }

    public static CMTime Parse(ReadOnlySpan<byte> data) => new()
    {
        Value = QtBin.U64(data),
        Scale = QtBin.U32(data[8..]),
        Flags = QtBin.U32(data[12..]),
        Epoch = QtBin.U64(data[16..]),
    };

    public void Serialize(Span<byte> target)
    {
        if (target.Length < LengthInBytes)
            throw new InvalidDataException("not enough space for CMTime");
        QtBin.W64(target, Value);
        QtBin.W32(target[8..], Scale);
        QtBin.W32(target[12..], Flags);
        QtBin.W64(target[16..], Epoch);
    }

    public double GetTimeForScale(CMTime other)
    {
        double factor = (double)other.Scale / Scale;
        return Value * factor;
    }

    public ulong Seconds() => Value == 0 ? 0 : Value / Scale;

    public override string ToString()
    {
        var flags = Flags switch
        {
            FlagsValid => "KCMTimeFlagsValid",
            FlagsHasBeenRounded => "KCMTimeFlagsHasBeenRounded",
            FlagsPositiveInfinity => "KCMTimeFlagsPositiveInfinity",
            FlagsNegativeInfinity => "KCMTimeFlagsNegativeInfinity",
            FlagsIndefinite => "KCMTimeFlagsIndefinite",
            _ => "unknown",
        };
        return $"CMTime{{{Value}/{Scale}, flags:{flags}, epoch:{Epoch}}}";
    }
}
