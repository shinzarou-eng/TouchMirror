using System;

namespace TouchMirror.QuickTime;

internal struct AudioStreamBasicDescription
{
    public const uint FormatIDLpcm = 0x6C70636D;
    public const int LengthInBytes = 56;

    public double SampleRate;
    public uint FormatId;
    public uint FormatFlags;
    public uint BytesPerPacket;
    public uint FramesPerPacket;
    public uint BytesPerFrame;
    public uint ChannelsPerFrame;
    public uint BitsPerChannel;
    public uint Reserved;

    public static AudioStreamBasicDescription Default() => new()
    {
        FormatFlags = 12,
        BytesPerPacket = 4,
        FramesPerPacket = 1,
        BytesPerFrame = 4,
        ChannelsPerFrame = 2,
        BitsPerChannel = 16,
        Reserved = 0,
        SampleRate = 48000,
        FormatId = FormatIDLpcm,
    };

    public static AudioStreamBasicDescription Parse(ReadOnlySpan<byte> data) => new()
    {
        SampleRate = QtBin.F64(data),
        FormatId = QtBin.U32(data[8..]),
        FormatFlags = QtBin.U32(data[12..]),
        BytesPerPacket = QtBin.U32(data[16..]),
        FramesPerPacket = QtBin.U32(data[20..]),
        BytesPerFrame = QtBin.U32(data[24..]),
        ChannelsPerFrame = QtBin.U32(data[28..]),
        BitsPerChannel = QtBin.U32(data[32..]),
        Reserved = QtBin.U32(data[36..]),
    };

    public void Serialize(Span<byte> bytes)
    {
        QtBin.W64(bytes, BitConverter.DoubleToUInt64Bits(SampleRate));
        QtBin.W32(bytes[8..], FormatIDLpcm);
        QtBin.W32(bytes[12..], FormatFlags);
        QtBin.W32(bytes[16..], BytesPerPacket);
        QtBin.W32(bytes[20..], FramesPerPacket);
        QtBin.W32(bytes[24..], BytesPerFrame);
        QtBin.W32(bytes[28..], ChannelsPerFrame);
        QtBin.W32(bytes[32..], BitsPerChannel);
        QtBin.W32(bytes[36..], Reserved);
        QtBin.W64(bytes[40..], BitConverter.DoubleToUInt64Bits(SampleRate));
        QtBin.W64(bytes[48..], BitConverter.DoubleToUInt64Bits(SampleRate));
    }

    public override readonly string ToString()
        => $"{{SampleRate:{SampleRate:F6},FormatFlags:{FormatFlags},BytesPerPacket:{BytesPerPacket},FramesPerPacket:{FramesPerPacket},BytesPerFrame:{BytesPerFrame},ChannelsPerFrame:{ChannelsPerFrame},BitsPerChannel:{BitsPerChannel},Reserved:{Reserved}}}";
}
