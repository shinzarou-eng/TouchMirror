using System;
using System.IO;

namespace TouchMirror.QuickTime;

internal struct CMSampleTimingInfo
{
    public CMTime Duration;
    public CMTime PresentationTimeStamp;
    public CMTime DecodeTimeStamp;

    public override readonly string ToString()
        => $"{{Duration:{Duration}, PresentationTS:{PresentationTimeStamp}, DecodeTS:{DecodeTimeStamp}}}";
}

internal sealed class CMSampleBuffer
{
    private const uint Sbuf = 0x73627566;
    private const uint Opts = 0x6F707473;
    private const uint Stia = 0x73746961;
    private const uint Sdat = 0x73646174;
    private const uint Satt = 0x73617474;
    private const uint SaryMagic = 0x73617279;
    private const uint Ssiz = 0x7373697A;
    private const uint Nsmp = 0x6E736D70;
    private const int TimingInfoLength = 3 * CMTime.LengthInBytes;

    public CMTime OutputPresentationTimestamp { get; private set; }
    public FormatDescriptor? FormatDesc { get; private set; }
    public bool HasFormatDescription => FormatDesc != null;
    public int NumSamples { get; private set; }
    public CMSampleTimingInfo[] SampleTimingInfoArray { get; private set; } = Array.Empty<CMSampleTimingInfo>();
    public byte[]? SampleData { get; private set; }
    public int[] SampleSizes { get; private set; } = Array.Empty<int>();
    public IndexKeyDict? Attachments { get; private set; }
    public IndexKeyDict? Sary { get; private set; }
    public uint MediaType { get; private set; }

    public bool HasSampleData => SampleData != null;

    public static CMSampleBuffer ParseVideo(ReadOnlySpan<byte> data) => Parse(data, FormatDescriptor.MediaTypeVideo);
    public static CMSampleBuffer ParseAudio(ReadOnlySpan<byte> data) => Parse(data, FormatDescriptor.MediaTypeSound);

    public static CMSampleBuffer Parse(ReadOnlySpan<byte> data, uint mediaType)
    {
        var buffer = new CMSampleBuffer { MediaType = mediaType };
        int totalLen = QtBin.ParseLengthAndMagic(data, Sbuf);
        if (totalLen > data.Length)
            throw new InvalidDataException($"less data ({data.Length}) than expected ({totalLen})");
        var remaining = data[8..];

        while (remaining.Length > 0)
        {
            switch (QtBin.U32(remaining[4..]))
            {
                case Opts:
                    buffer.OutputPresentationTimestamp = CMTime.Parse(remaining[8..]);
                    remaining = remaining[32..];
                    break;
                case Stia:
                    buffer.SampleTimingInfoArray = ParseStia(remaining, out int stiaLen);
                    remaining = remaining[stiaLen..];
                    break;
                case Sdat:
                    int sdatLen = QtBin.ParseLengthAndMagic(remaining, Sdat);
                    buffer.SampleData = remaining[8..sdatLen].ToArray();
                    remaining = remaining[sdatLen..];
                    break;
                case Nsmp:
                    int nsmpLen = QtBin.ParseLengthAndMagic(remaining, Nsmp);
                    if (nsmpLen != 12)
                        throw new InvalidDataException($"invalid nsmp length {nsmpLen}");
                    buffer.NumSamples = (int)QtBin.U32(remaining[8..]);
                    remaining = remaining[nsmpLen..];
                    break;
                case Ssiz:
                    buffer.SampleSizes = ParseSampleSizes(remaining, out int ssizLen);
                    remaining = remaining[ssizLen..];
                    break;
                case FormatDescriptor.Magic:
                    int fdscLen = (int)QtBin.U32(remaining);
                    buffer.FormatDesc = FormatDescriptor.Parse(remaining[..fdscLen]);
                    remaining = remaining[fdscLen..];
                    break;
                case Satt:
                    int sattLen = (int)QtBin.U32(remaining);
                    buffer.Attachments = IndexKeyDict.ParseWithMarker(remaining[..sattLen], Satt);
                    remaining = remaining[sattLen..];
                    break;
                case SaryMagic:
                    int saryLen = (int)QtBin.U32(remaining);
                    buffer.Sary = IndexKeyDict.Parse(remaining[8..saryLen]);
                    remaining = remaining[saryLen..];
                    break;
                default:
                    throw new InvalidDataException($"unknown sbuf magic '{QtBin.TagName(QtBin.U32(remaining[4..]))}'");
            }
        }
        return buffer;
    }

    private static int[] ParseSampleSizes(ReadOnlySpan<byte> data, out int consumed)
    {
        int len = QtBin.ParseLengthAndMagic(data, Ssiz) - 8;
        if (len % 4 != 0)
            throw new InvalidDataException("ssiz not a multiple of 4");
        var result = new int[len / 4];
        for (int i = 0; i < result.Length; i++)
            result[i] = (int)QtBin.U32(data[(8 + 4 * i)..]);
        consumed = len + 8;
        return result;
    }

    private static CMSampleTimingInfo[] ParseStia(ReadOnlySpan<byte> data, out int consumed)
    {
        int len = QtBin.ParseLengthAndMagic(data, Stia) - 8;
        if (len % TimingInfoLength != 0)
            throw new InvalidDataException("stia not a multiple of timing info length");
        var result = new CMSampleTimingInfo[len / TimingInfoLength];
        for (int i = 0; i < result.Length; i++)
        {
            var slice = data[(8 + i * TimingInfoLength)..];
            result[i] = new CMSampleTimingInfo
            {
                Duration = CMTime.Parse(slice),
                PresentationTimeStamp = CMTime.Parse(slice[CMTime.LengthInBytes..]),
                DecodeTimeStamp = CMTime.Parse(slice[(2 * CMTime.LengthInBytes)..]),
            };
        }
        consumed = len + 8;
        return result;
    }

    public override string ToString() => MediaType == FormatDescriptor.MediaTypeVideo
        ? $"{{OutputPresentationTS:{OutputPresentationTimestamp}, NumSamples:{NumSamples}, Nalus:{NaluDetails()}, fdsc:{FormatDesc?.ToString() ?? "none"}, attach:{Attachments}, sary:{Sary}, SampleTimingInfoArray:{(SampleTimingInfoArray.Length > 0 ? SampleTimingInfoArray[0].ToString() : "")}}}"
        : $"{{OutputPresentationTS:{OutputPresentationTimestamp}, NumSamples:{NumSamples}, SampleSize:{(SampleSizes.Length > 0 ? SampleSizes[0] : 0)}, fdsc:{FormatDesc?.ToString() ?? "none"}}}";

    private static readonly string[] NaluTypes =
    {
        "unspecified", "coded slice", "data partition A", "data partition B", "data partition C",
        "IDR", "SEI", "sequence parameter set", "picture parameter set", "access unit delim",
        "end of seq", "end of stream", "filler data", "extended", "extended", "extended",
        "extended", "extended", "extended", "extended", "extended", "extended", "extended",
        "undefined", "undefined", "undefined", "undefined", "undefined", "undefined",
        "undefined", "undefined",
    };

    private string NaluDetails()
    {
        if (SampleData == null) return "[]";
        var slice = SampleData.AsSpan();
        var sb = new System.Text.StringBuilder("[");
        while (slice.Length > 0)
        {
            uint naluLength = QtBin.U32(slice);
            var nalu = slice[4..];
            sb.Append($"{{len:{naluLength} type:{NaluTypes[nalu[0] & 0x1F]}}},");
            slice = slice[(int)(4 + naluLength)..];
        }
        return sb.Append(']').ToString();
    }
}
