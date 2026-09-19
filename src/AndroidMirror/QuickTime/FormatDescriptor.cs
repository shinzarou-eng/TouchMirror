using System;
using System.IO;

namespace TouchMirror.QuickTime;

internal sealed class FormatDescriptor
{
    public const uint Magic = 0x66647363;
    public const uint MediaTypeVideo = 0x76696465;
    public const uint MediaTypeSound = 0x736F756E;
    public const uint CodecAvc1 = 0x61766331;

    private const uint MediaTypeMagic = 0x6D646961;
    private const uint VideoDimensionMagic = 0x7664696D;
    private const uint CodecMagic = 0x636F6463;
    private const uint ExtensionMagic = 0x6578746E;
    private const uint AsbdMagic = 0x61736264;

    public uint MediaType { get; private set; }
    public uint VideoDimensionWidth { get; private set; }
    public uint VideoDimensionHeight { get; private set; }
    public uint Codec { get; private set; }
    public IndexKeyDict? Extensions { get; private set; }
    public byte[] Pps { get; private set; } = Array.Empty<byte>();
    public byte[] Sps { get; private set; } = Array.Empty<byte>();
    public AudioStreamBasicDescription Asbd { get; private set; }

    public static FormatDescriptor Parse(ReadOnlySpan<byte> data)
    {
        QtBin.ParseLengthAndMagic(data, Magic);
        var remaining = data[8..];

        int mtLen = QtBin.ParseLengthAndMagic(remaining, MediaTypeMagic);
        if (mtLen != 12)
            throw new InvalidDataException($"invalid media type length {mtLen}");
        uint mediaType = QtBin.U32(remaining[8..]);
        remaining = remaining[mtLen..];

        var fd = new FormatDescriptor { MediaType = mediaType };
        if (mediaType == MediaTypeSound)
        {
            int asbdLen = QtBin.ParseLengthAndMagic(remaining, AsbdMagic);
            fd.Asbd = AudioStreamBasicDescription.Parse(remaining[8..asbdLen]);
            return fd;
        }

        int dimLen = QtBin.ParseLengthAndMagic(remaining, VideoDimensionMagic);
        if (dimLen != 16)
            throw new InvalidDataException($"invalid video dimension length {dimLen}");
        fd.VideoDimensionWidth = QtBin.U32(remaining[8..]);
        fd.VideoDimensionHeight = QtBin.U32(remaining[12..]);
        remaining = remaining[dimLen..];

        int codecLen = QtBin.ParseLengthAndMagic(remaining, CodecMagic);
        if (codecLen != 12)
            throw new InvalidDataException($"invalid codec length {codecLen}");
        fd.Codec = QtBin.U32(remaining[8..]);
        remaining = remaining[codecLen..];

        fd.Extensions = IndexKeyDict.ParseWithMarker(remaining, ExtensionMagic);
        (fd.Pps, fd.Sps) = ExtractPpsSps(fd.Extensions);
        return fd;
    }

    private static (byte[], byte[]) ExtractPpsSps(IndexKeyDict dict)
    {
        try
        {
            if (dict.Get(49) is not IndexKeyDict inner)
                return (Array.Empty<byte>(), Array.Empty<byte>());
            if (inner.Get(105) is not byte[] data)
                return (Array.Empty<byte>(), Array.Empty<byte>());
            int ppsLen = data[7];
            var pps = data[8..(8 + ppsLen)];
            int spsLen = data[10 + ppsLen];
            var sps = data[(11 + ppsLen)..(11 + ppsLen + spsLen)];
            return (pps, sps);
        }
        catch { return (Array.Empty<byte>(), Array.Empty<byte>()); }
    }

    public override string ToString() => MediaType == MediaTypeVideo
        ? $"fdsc:{{MediaType:Video, VideoDimension:({VideoDimensionWidth}x{VideoDimensionHeight}), Codec:{CodecName}, PPS:{Convert.ToHexString(Pps).ToLowerInvariant()}, SPS:{Convert.ToHexString(Sps).ToLowerInvariant()}, Extensions:{Extensions}}}"
        : $"fdsc:{{MediaType:{MediaTypeName}, AudioStreamBasicDescription: {Asbd}}}";

    private string CodecName => Codec == CodecAvc1 ? "AVC-1" : $"Unknown({Codec:x})";
    private string MediaTypeName => MediaType == MediaTypeSound ? "Sound" : $"Unknown({MediaType:x})";
}
