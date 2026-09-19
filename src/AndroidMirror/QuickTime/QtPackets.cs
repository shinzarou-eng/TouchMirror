using System;
using System.Collections.Generic;
using System.IO;

namespace TouchMirror.QuickTime;

internal static class QtMagic
{
    public const uint Ping = 0x70696E67;
    public const uint Sync = 0x73796E63;
    public const uint Asyn = 0x6173796E;
    public const uint Rply = 0x72706C79;

    public const uint Time = 0x74696D65;
    public const uint Cwpa = 0x63777061;
    public const uint Afmt = 0x61666D74;
    public const uint Cvrp = 0x63767270;
    public const uint Clok = 0x636C6F6B;
    public const uint Og = 0x676F2120;
    public const uint Skew = 0x736B6577;
    public const uint Stop = 0x73746F70;

    public const uint Feed = 0x66656564;
    public const uint Tjmp = 0x746A6D70;
    public const uint Srat = 0x73726174;
    public const uint Sprp = 0x73707270;
    public const uint Tbas = 0x74626173;
    public const uint Rels = 0x72656C73;
    public const uint Hpd1 = 0x68706431;
    public const uint Hpa1 = 0x68706131;
    public const uint Need = 0x6E656564;
    public const uint Eat = 0x65617421;
    public const uint Hpd0 = 0x68706430;
    public const uint Hpa0 = 0x68706130;

    public const ulong EmptyClock = 1;
}

internal static class QtPackets
{
    public static ulong ParseAsynHeader(ReadOnlySpan<byte> data, uint messageMagic, out int offset)
        => ParseHeader(data, QtMagic.Asyn, messageMagic, out offset);

    public static ulong ParseSyncHeader(ReadOnlySpan<byte> data, uint messageMagic, out int offset, out ulong correlationId)
    {
        ulong clockRef = ParseHeader(data, QtMagic.Sync, messageMagic, out offset);
        correlationId = QtBin.U64(data[offset..]);
        offset += 8;
        return clockRef;
    }

    private static ulong ParseHeader(ReadOnlySpan<byte> data, uint packetMagic, uint messageMagic, out int offset)
    {
        uint magic = QtBin.U32(data);
        if (magic != packetMagic)
            throw new InvalidDataException($"invalid packet magic '{QtBin.TagName(magic)}'");
        ulong clockRef = QtBin.U64(data[4..]);
        uint messageType = QtBin.U32(data[12..]);
        if (messageType != messageMagic)
            throw new InvalidDataException($"invalid packet type '{QtBin.TagName(messageType)}'");
        offset = 16;
        return clockRef;
    }

    public static byte[] PingBytes()
    {
        var packet = new byte[16];
        QtBin.W32(packet, 16);
        QtBin.W32(packet.AsSpan(4), QtMagic.Ping);
        QtBin.W64(packet.AsSpan(8), 0x0000000100000000);
        return packet;
    }

    public static byte[] ClockRefReply(ulong clockRef, ulong correlationId)
    {
        var data = new byte[28];
        QtBin.W32(data, 28);
        QtBin.W32(data.AsSpan(4), QtMagic.Rply);
        QtBin.W64(data.AsSpan(8), correlationId);
        QtBin.W32(data.AsSpan(16), 0);
        QtBin.W64(data.AsSpan(20), clockRef);
        return data;
    }

    public static byte[] AsynNeedBytes(ulong clockRef) => SimpleAsyn(QtMagic.Need, clockRef);
    public static byte[] AsynHpd0Bytes() => SimpleAsyn(QtMagic.Hpd0, QtMagic.EmptyClock);
    public static byte[] AsynHpa0Bytes(ulong clockRef) => SimpleAsyn(QtMagic.Hpa0, clockRef);

    private static byte[] SimpleAsyn(uint subtype, ulong clockRef)
    {
        var packet = new byte[20];
        QtBin.W32(packet, 20);
        QtBin.W32(packet.AsSpan(4), QtMagic.Asyn);
        QtBin.W64(packet.AsSpan(8), clockRef);
        QtBin.W32(packet.AsSpan(16), subtype);
        return packet;
    }

    public static byte[] AsynDictPacket(StringKeyDict dict, uint subtype, ulong asynTypeHeader)
    {
        var body = dict.Serialize();
        var packet = new byte[body.Length + 20];
        QtBin.W32(packet, (uint)packet.Length);
        QtBin.W32(packet.AsSpan(4), QtMagic.Asyn);
        QtBin.W64(packet.AsSpan(8), asynTypeHeader);
        QtBin.W32(packet.AsSpan(16), subtype);
        body.CopyTo(packet, 20);
        return packet;
    }

    public static byte[] AsynHpd1Bytes() => AsynDictPacket(CreateHpd1Dict(), QtMagic.Hpd1, QtMagic.EmptyClock);
    public static byte[] AsynHpa1Bytes(ulong clockRef) => AsynDictPacket(CreateHpa1Dict(), QtMagic.Hpa1, clockRef);

    private static StringKeyDict CreateHpd1Dict()
    {
        var dict = new StringKeyDict();
        dict.Entries.Add(new("Valeria", true));
        dict.Entries.Add(new("HEVCDecoderSupports444", true));
        var display = new StringKeyDict();
        display.Entries.Add(new("Width", NSNumber.FromFloat64(1920)));
        display.Entries.Add(new("Height", NSNumber.FromFloat64(1200)));
        dict.Entries.Add(new("DisplaySize", display));
        return dict;
    }

    private static StringKeyDict CreateHpa1Dict()
    {
        var asbd = new byte[AudioStreamBasicDescription.LengthInBytes];
        AudioStreamBasicDescription.Default().Serialize(asbd);
        var dict = new StringKeyDict();
        dict.Entries.Add(new("BufferAheadInterval", NSNumber.FromFloat64(0.07300000000000001)));
        dict.Entries.Add(new("deviceUID", "Valeria"));
        dict.Entries.Add(new("ScreenLatency", NSNumber.FromFloat64(0.04)));
        dict.Entries.Add(new("formats", asbd));
        dict.Entries.Add(new("EDIDAC3Support", NSNumber.FromUInt32(0)));
        dict.Entries.Add(new("deviceName", "Valeria"));
        return dict;
    }
}

internal sealed class SyncCwpaPacket
{
    public ulong ClockRef { get; private set; }
    public ulong CorrelationId { get; private set; }
    public ulong DeviceClockRef { get; private set; }

    public static SyncCwpaPacket Parse(ReadOnlySpan<byte> data)
    {
        QtPackets.ParseSyncHeader(data, QtMagic.Cwpa, out int off, out ulong correlationId);
        var remaining = data[off..];
        var packet = new SyncCwpaPacket { CorrelationId = correlationId };
        packet.ClockRef = QtBin.U64(data[4..]);
        if (packet.ClockRef != QtMagic.EmptyClock)
            throw new InvalidDataException($"CWPA should have empty ClockRef, has {packet.ClockRef:x}");
        packet.DeviceClockRef = QtBin.U64(remaining);
        return packet;
    }

    public byte[] NewReply(ulong clockRef) => QtPackets.ClockRefReply(clockRef, CorrelationId);
    public override string ToString() => $"SYNC_CWPA{{ClockRef:{ClockRef:x}, CorrelationID:{CorrelationId:x}, DeviceClockRef:{DeviceClockRef:x}}}";
}

internal sealed class SyncCvrpPacket
{
    public ulong ClockRef { get; private set; }
    public ulong CorrelationId { get; private set; }
    public ulong DeviceClockRef { get; private set; }
    public StringKeyDict? Payload { get; private set; }

    public static SyncCvrpPacket Parse(ReadOnlySpan<byte> data)
    {
        QtPackets.ParseSyncHeader(data, QtMagic.Cvrp, out int off, out ulong correlationId);
        var remaining = data[off..];
        var packet = new SyncCvrpPacket { CorrelationId = correlationId };
        packet.ClockRef = QtBin.U64(data[4..]);
        if (packet.ClockRef != QtMagic.EmptyClock)
            throw new InvalidDataException($"CVRP should have empty ClockRef, has {packet.ClockRef:x}");
        packet.DeviceClockRef = QtBin.U64(remaining);
        packet.Payload = StringKeyDict.Parse(remaining[8..]);
        return packet;
    }

    public byte[] NewReply(ulong clockRef) => QtPackets.ClockRefReply(clockRef, CorrelationId);
    public override string ToString() => $"SYNC_CVRP{{ClockRef:{ClockRef:x}, CorrelationID:{CorrelationId:x}, DeviceClockRef:{DeviceClockRef:x}, Payload:{Payload}}}";
}

internal sealed class SyncClokPacket
{
    public ulong ClockRef { get; private set; }
    public ulong CorrelationId { get; private set; }

    public static SyncClokPacket Parse(ReadOnlySpan<byte> data)
    {
        var clockRef = QtPackets.ParseSyncHeader(data, QtMagic.Clok, out _, out ulong correlationId);
        return new SyncClokPacket { ClockRef = clockRef, CorrelationId = correlationId };
    }

    public byte[] NewReply(ulong clockRef) => QtPackets.ClockRefReply(clockRef, CorrelationId);
    public override string ToString() => $"SYNC_CLOK{{ClockRef:{ClockRef:x}, CorrelationID:{CorrelationId:x}}}";
}

internal sealed class SyncOgPacket
{
    public ulong ClockRef { get; private set; }
    public ulong CorrelationId { get; private set; }
    public uint Unknown { get; private set; }

    public static SyncOgPacket Parse(ReadOnlySpan<byte> data)
    {
        var clockRef = QtPackets.ParseSyncHeader(data, QtMagic.Og, out int off, out ulong correlationId);
        return new SyncOgPacket { ClockRef = clockRef, CorrelationId = correlationId, Unknown = QtBin.U32(data[off..]) };
    }

    public byte[] NewReply()
    {
        var bytes = new byte[24];
        QtBin.W32(bytes, 24);
        QtBin.W32(bytes.AsSpan(4), QtMagic.Rply);
        QtBin.W64(bytes.AsSpan(8), CorrelationId);
        return bytes;
    }
}

internal sealed class SyncSkewPacket
{
    public ulong ClockRef { get; private set; }
    public ulong CorrelationId { get; private set; }

    public static SyncSkewPacket Parse(ReadOnlySpan<byte> data)
    {
        var clockRef = QtPackets.ParseSyncHeader(data, QtMagic.Skew, out _, out ulong correlationId);
        return new SyncSkewPacket { ClockRef = clockRef, CorrelationId = correlationId };
    }

    public byte[] NewReply(double skew)
    {
        var bytes = new byte[28];
        QtBin.W32(bytes, 28);
        QtBin.W32(bytes.AsSpan(4), QtMagic.Rply);
        QtBin.W64(bytes.AsSpan(8), CorrelationId);
        QtBin.W32(bytes.AsSpan(16), 0);
        QtBin.W64(bytes.AsSpan(20), BitConverter.DoubleToUInt64Bits(skew));
        return bytes;
    }
}

internal sealed class SyncStopPacket
{
    public ulong ClockRef { get; private set; }
    public ulong CorrelationId { get; private set; }

    public static SyncStopPacket Parse(ReadOnlySpan<byte> data)
    {
        var clockRef = QtPackets.ParseSyncHeader(data, QtMagic.Stop, out _, out ulong correlationId);
        return new SyncStopPacket { ClockRef = clockRef, CorrelationId = correlationId };
    }

    public byte[] NewReply()
    {
        var bytes = new byte[24];
        QtBin.W32(bytes, 24);
        QtBin.W32(bytes.AsSpan(4), QtMagic.Rply);
        QtBin.W64(bytes.AsSpan(8), CorrelationId);
        return bytes;
    }
}

internal sealed class SyncTimePacket
{
    public ulong ClockRef { get; private set; }
    public ulong CorrelationId { get; private set; }

    public static SyncTimePacket Parse(ReadOnlySpan<byte> data)
    {
        var clockRef = QtPackets.ParseSyncHeader(data, QtMagic.Time, out _, out ulong correlationId);
        return new SyncTimePacket { ClockRef = clockRef, CorrelationId = correlationId };
    }

    public byte[] NewReply(CMTime time)
    {
        var data = new byte[44];
        QtBin.W32(data, 44);
        QtBin.W32(data.AsSpan(4), QtMagic.Rply);
        QtBin.W64(data.AsSpan(8), CorrelationId);
        QtBin.W32(data.AsSpan(16), 0);
        time.Serialize(data.AsSpan(20));
        return data;
    }
}

internal sealed class SyncAfmtPacket
{
    public ulong ClockRef { get; private set; }
    public ulong CorrelationId { get; private set; }
    public AudioStreamBasicDescription Asbd { get; private set; }

    public static SyncAfmtPacket Parse(ReadOnlySpan<byte> data)
    {
        var clockRef = QtPackets.ParseSyncHeader(data, QtMagic.Afmt, out int off, out ulong correlationId);
        return new SyncAfmtPacket
        {
            ClockRef = clockRef,
            CorrelationId = correlationId,
            Asbd = AudioStreamBasicDescription.Parse(data[off..]),
        };
    }

    public byte[] NewReply()
    {
        var dict = new StringKeyDict();
        dict.Entries.Add(new("Error", NSNumber.FromUInt32(0)));
        var dictBytes = dict.Serialize();
        var bytes = new byte[dictBytes.Length + 20];
        QtBin.W32(bytes, (uint)bytes.Length);
        QtBin.W32(bytes.AsSpan(4), QtMagic.Rply);
        QtBin.W64(bytes.AsSpan(8), CorrelationId);
        QtBin.W32(bytes.AsSpan(16), 0);
        dictBytes.CopyTo(bytes, 20);
        return bytes;
    }

    public override string ToString()
        => $"SYNC_AFMT{{ClockRef:{ClockRef:x}, CorrelationID:{CorrelationId:x}, AudioStreamBasicDescription:{Asbd}}}";
}

internal sealed class AsynSampleBufPacket
{
    public ulong ClockRef { get; private set; }
    public CMSampleBuffer? SampleBuf { get; private set; }

    public static AsynSampleBufPacket Parse(ReadOnlySpan<byte> data)
    {
        uint magic = QtBin.U32(data[12..]);
        var clockRef = QtPackets.ParseAsynHeader(data, magic, out _);
        var sbuf = magic == QtMagic.Feed
            ? CMSampleBuffer.ParseVideo(data[16..])
            : CMSampleBuffer.ParseAudio(data[16..]);
        return new AsynSampleBufPacket { ClockRef = clockRef, SampleBuf = sbuf };
    }
}

internal sealed class AsynSprpPacket
{
    public ulong ClockRef { get; private set; }
    public KeyValuePair<string, object?> Property { get; private set; }

    public static AsynSprpPacket Parse(ReadOnlySpan<byte> data)
    {
        var clockRef = QtPackets.ParseAsynHeader(data, QtMagic.Sprp, out int off);
        return new AsynSprpPacket { ClockRef = clockRef, Property = StringKeyDict.ParseKeyValueEntry(data[off..]) };
    }
}

internal sealed class AsynSratPacket
{
    public ulong ClockRef { get; private set; }
    public float Rate1 { get; private set; }
    public float Rate2 { get; private set; }
    public CMTime Time { get; private set; }

    public static AsynSratPacket Parse(ReadOnlySpan<byte> data)
    {
        var clockRef = QtPackets.ParseAsynHeader(data, QtMagic.Srat, out int off);
        var remaining = data[off..];
        return new AsynSratPacket
        {
            ClockRef = clockRef,
            Rate1 = QtBin.F32(remaining),
            Rate2 = QtBin.F32(remaining[4..]),
            Time = CMTime.Parse(remaining[8..]),
        };
    }
}

internal sealed class AsynTbasPacket
{
    public ulong ClockRef { get; private set; }
    public ulong SomeOtherRef { get; private set; }

    public static AsynTbasPacket Parse(ReadOnlySpan<byte> data)
    {
        var clockRef = QtPackets.ParseAsynHeader(data, QtMagic.Tbas, out int off);
        return new AsynTbasPacket { ClockRef = clockRef, SomeOtherRef = QtBin.U64(data[off..]) };
    }
}

internal sealed class AsynTjmpPacket
{
    public ulong ClockRef { get; private set; }
    public byte[] Unknown { get; private set; } = Array.Empty<byte>();

    public static AsynTjmpPacket Parse(ReadOnlySpan<byte> data)
    {
        var clockRef = QtPackets.ParseAsynHeader(data, QtMagic.Tjmp, out int off);
        return new AsynTjmpPacket { ClockRef = clockRef, Unknown = data[off..].ToArray() };
    }
}

internal sealed class AsynRelsPacket
{
    public ulong ClockRef { get; private set; }

    public static AsynRelsPacket Parse(ReadOnlySpan<byte> data)
    {
        var clockRef = QtPackets.ParseAsynHeader(data, QtMagic.Rels, out _);
        return new AsynRelsPacket { ClockRef = clockRef };
    }
}
