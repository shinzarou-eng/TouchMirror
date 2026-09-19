using TouchMirror.QuickTime;

var fixtures = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "fixtures");
fixtures = Path.GetFullPath(Directory.Exists(fixtures)
    ? fixtures
    : Path.Combine(AppContext.BaseDirectory, "fixtures"));

int pass = 0, fail = 0;

void Check(string name, bool cond, string detail = "")
{
    if (cond) { pass++; Console.WriteLine($"PASS {name}"); }
    else { fail++; Console.WriteLine($"FAIL {name} {detail}"); }
}

void Eq<T>(string name, T expected, T actual) => Check(name, Equals(expected, actual), $"expected {expected} got {actual}");

void Bytes(string name, byte[] expected, byte[] actual)
{
    if (expected.AsSpan().SequenceEqual(actual)) { pass++; Console.WriteLine($"PASS {name}"); }
    else { fail++; Console.WriteLine($"FAIL {name} len {actual.Length} vs {expected.Length}"); }
}

byte[] Load(string name) => File.ReadAllBytes(Path.Combine(fixtures, name));
ReadOnlySpan<byte> P(string name) => Load(name).AsSpan()[4..];
ReadOnlySpan<byte> W(string name) => Load(name).AsSpan();

Console.WriteLine($"fixtures: {fixtures}\n");

var feed = AsynSampleBufPacket.Parse(P("asyn-feed"));
Eq("feed.clockref", 0x7ffb5cc32f60UL, feed.ClockRef);
Eq("feed.mediatype", FormatDescriptor.MediaTypeVideo, feed.SampleBuf!.MediaType);
Eq("feed.hasfdsc", true, feed.SampleBuf.HasFormatDescription);
Eq("feed.width", 1126U, feed.SampleBuf.FormatDesc!.VideoDimensionWidth);
Eq("feed.height", 2436U, feed.SampleBuf.FormatDesc.VideoDimensionHeight);
Eq("feed.codec", FormatDescriptor.CodecAvc1, feed.SampleBuf.FormatDesc.Codec);
Eq("feed.pps", "27640033ac5680470133e69e6e04040404", Convert.ToHexString(feed.SampleBuf.FormatDesc.Pps).ToLowerInvariant());
Eq("feed.sps", "28ee3cb0", Convert.ToHexString(feed.SampleBuf.FormatDesc.Sps).ToLowerInvariant());
Eq("feed.samples", 1, feed.SampleBuf.NumSamples);
Eq("feed.sampledata", 90750, feed.SampleBuf.SampleData!.Length);
Eq("feed.samplesizes", 1, feed.SampleBuf.SampleSizes.Length);
Eq("feed.ssiz0", 90750, feed.SampleBuf.SampleSizes[0]);
Eq("feed.attach", 4, feed.SampleBuf.Attachments!.Entries.Count);
Eq("feed.sary", 1, feed.SampleBuf.Sary!.Entries.Count);
Eq("feed.pts.flags", CMTime.FlagsHasBeenRounded, feed.SampleBuf.OutputPresentationTimestamp.Flags);
Eq("feed.pts.sec", 0x176a7UL, feed.SampleBuf.OutputPresentationTimestamp.Seconds());
Eq("feed.stia", 1, feed.SampleBuf.SampleTimingInfoArray.Length);
Eq("feed.stia.pts", 0x176a7UL, feed.SampleBuf.SampleTimingInfoArray[0].PresentationTimeStamp.Seconds());
Eq("feed.stia.dts", 0UL, feed.SampleBuf.SampleTimingInfoArray[0].DecodeTimeStamp.Seconds());

var feedNoFdsc = AsynSampleBufPacket.Parse(W("asyn-feed-nofdsc"));
Eq("feednofdsc.hasfdsc", false, feedNoFdsc.SampleBuf!.HasFormatDescription);
Eq("feednofdsc.pts.sec", 0x44b82fa09UL, feedNoFdsc.SampleBuf.OutputPresentationTimestamp.Seconds());
Eq("feednofdsc.sampledata", 56604, feedNoFdsc.SampleBuf.SampleData!.Length);
Eq("feednofdsc.sary", 2, feedNoFdsc.SampleBuf.Sary!.Entries.Count);

var feedTtas = AsynSampleBufPacket.Parse(P("asyn-feed-ttas-only"));
Eq("feedttas.hasfdsc", false, feedTtas.SampleBuf!.HasFormatDescription);

var feedUnknown = AsynSampleBufPacket.Parse(W("asyn-feed-unknown1"));
Check("feedunknown.parsed", feedUnknown.SampleBuf != null);

var eat = AsynSampleBufPacket.Parse(W("asyn-eat"));
Eq("eat.clockref", 0x133959728UL, eat.ClockRef);
Eq("eat.mediatype", FormatDescriptor.MediaTypeSound, eat.SampleBuf!.MediaType);
Eq("eat.hasfdsc", true, eat.SampleBuf.HasFormatDescription);
Eq("eat.samples", 1024, eat.SampleBuf.NumSamples);
Eq("eat.ssiz0", 4, eat.SampleBuf.SampleSizes[0]);
Eq("eat.sampledata", 4096, eat.SampleBuf.SampleData!.Length);

var eatNoFdsc = AsynSampleBufPacket.Parse(W("asyn-eat-nofdsc"));
Eq("eatnofdsc.hasfdsc", false, eatNoFdsc.SampleBuf!.HasFormatDescription);
Eq("eatnofdsc.samples", 1024, eatNoFdsc.SampleBuf.NumSamples);

Eq("rels.clockref", 0x7fba35608a00UL, AsynRelsPacket.Parse(P("asyn-rels")).ClockRef);

var sprp = AsynSprpPacket.Parse(W("asyn-sprp"));
Eq("sprp.clockref", 0x11123bc18UL, sprp.ClockRef);
Eq("sprp.key", "ObeyEmptyMediaMarkers", sprp.Property.Key);
Eq("sprp.value", true, sprp.Property.Value);
var sprp2 = AsynSprpPacket.Parse(W("asyn-sprp2"));
Check("sprp2.parsed", sprp2.Property.Key.Length > 0);

var srat = AsynSratPacket.Parse(W("asyn-srat"));
Eq("srat.clockref", 0x11123bc18UL, srat.ClockRef);
Eq("srat.rate1", 1.0f, srat.Rate1);
Eq("srat.rate2", 1.0f, srat.Rate2);
Eq("srat.timescale", 1000000000U, srat.Time.Scale);

var tbas = AsynTbasPacket.Parse(W("asyn-tbas"));
Eq("tbas.clockref", 0x11123bc18UL, tbas.ClockRef);
Eq("tbas.ref", 0x1024490c0UL, tbas.SomeOtherRef);

Eq("tjmp.clockref", 0x11123bc18UL, AsynTjmpPacket.Parse(W("asyn-tjmp")).ClockRef);

var cwpa = SyncCwpaPacket.Parse(P("cwpa-request1"));
Eq("cwpa.clockref", 1UL, cwpa.ClockRef);
Eq("cwpa.deviceref", 0x1135a74e0UL, cwpa.DeviceClockRef);
Eq("cwpa.correlation", 0x113573de0UL, cwpa.CorrelationId);
Bytes("cwpa.reply", Load("cwpa-reply1"), cwpa.NewReply(0x00007FA66CE20CB0));
Check("cwpa2.parsed", SyncCwpaPacket.Parse(P("cwpa-request2")).DeviceClockRef != 0);

var cvrp = SyncCvrpPacket.Parse(P("cvrp-request"));
Eq("cvrp.clockref", 1UL, cvrp.ClockRef);
Eq("cvrp.deviceref", 0x113538da0UL, cvrp.DeviceClockRef);
Eq("cvrp.correlation", 0x1135659d0UL, cvrp.CorrelationId);
Eq("cvrp.entries", 3, cvrp.Payload!.Entries.Count);
Bytes("cvrp.reply", Load("cvrp-reply"), cvrp.NewReply(0x00007FA66CD10250));

var clok = SyncClokPacket.Parse(P("clok-request"));
Eq("clok.clockref", 0x7fa66cd10250UL, clok.ClockRef);
Eq("clok.correlation", 0x113584970UL, clok.CorrelationId);
Bytes("clok.reply", Load("clok-reply"), clok.NewReply(0x00007FA67CC17980));

var afmt = SyncAfmtPacket.Parse(P("afmt-request"));
Eq("afmt.clockref", 0x7fa66ce20cb0UL, afmt.ClockRef);
Eq("afmt.correlation", 0x113229d80UL, afmt.CorrelationId);
Eq("afmt.rate", 48000.0, afmt.Asbd.SampleRate);
Eq("afmt.flags", 76U, afmt.Asbd.FormatFlags);
Eq("afmt.bpp", 4U, afmt.Asbd.BytesPerPacket);
Eq("afmt.fpp", 1U, afmt.Asbd.FramesPerPacket);
Eq("afmt.bpf", 4U, afmt.Asbd.BytesPerFrame);
Eq("afmt.cpf", 2U, afmt.Asbd.ChannelsPerFrame);
Eq("afmt.bpc", 16U, afmt.Asbd.BitsPerChannel);
Bytes("afmt.reply", Load("afmt-reply"), afmt.NewReply());

var og = SyncOgPacket.Parse(P("og-request"));
Eq("og.clockref", 0x7fba35425ff0UL, og.ClockRef);
Eq("og.correlation", 0x102d32f30UL, og.CorrelationId);
Eq("og.unknown", 1U, og.Unknown);
Bytes("og.reply", Load("og-reply"), og.NewReply());

var skew = SyncSkewPacket.Parse(P("skew-request"));
Eq("skew.clockref", 0x7fba35425ff0UL, skew.ClockRef);
Eq("skew.correlation", 0x102fdb960UL, skew.CorrelationId);
Bytes("skew.reply", Load("skew-reply"), skew.NewReply(48000.0));

var stop = SyncStopPacket.Parse(P("stop-request"));
Eq("stop.clockref", 0x7fba35425ff0UL, stop.ClockRef);
Eq("stop.correlation", 0x102fd4910UL, stop.CorrelationId);
Bytes("stop.reply", Load("stop-reply"), stop.NewReply());

var time = SyncTimePacket.Parse(P("time-request1"));
Eq("time.clockref", 0x7fa67cc17980UL, time.ClockRef);
Eq("time.correlation", 0x113223d50UL, time.CorrelationId);
var replyTime = new CMTime { Value = 0x0000BA62C442E1E1, Scale = 0x3B9ACA00, Flags = CMTime.FlagsHasBeenRounded, Epoch = 0 };
Bytes("time.reply", Load("time-reply1"), time.NewReply(replyTime));

Bytes("ping.bytes", Convert.FromHexString("10000000676e69700000000001000000"), QtPackets.PingBytes());
Bytes("asyn.need", Load("asyn-need"), QtPackets.AsynNeedBytes(0x0000000102c16ca0));
Bytes("asyn.hpd1", Load("asyn-hpd1"), QtPackets.AsynHpd1Bytes());
Bytes("asyn.hpa1", Load("asyn-hpa1"), QtPackets.AsynHpa1Bytes(0x00000001145392F0));
Bytes("asyn.hpa0", Load("asyn-hpa0"), QtPackets.AsynHpa0Bytes(0x0000000102C5FC10));
Bytes("asyn.hpd0", Load("asyn-hpd0"), QtPackets.AsynHpd0Bytes());

var fdsc = FormatDescriptor.Parse(W("formatdescriptor.bin"));
Eq("fdsc.mediatype", FormatDescriptor.MediaTypeVideo, fdsc.MediaType);
Eq("fdsc.dims", "1126x2436", $"{fdsc.VideoDimensionWidth}x{fdsc.VideoDimensionHeight}");
Eq("fdsc.pps", "27640033ac5680470133e69e6e04040404", Convert.ToHexString(fdsc.Pps).ToLowerInvariant());
Eq("fdsc.sps", "28ee3cb0", Convert.ToHexString(fdsc.Sps).ToLowerInvariant());

var fdscAudio = FormatDescriptor.Parse(W("formatdescriptor-audio.bin"));
Eq("fdscaudio.mediatype", FormatDescriptor.MediaTypeSound, fdscAudio.MediaType);
Eq("fdscaudio.rate", 48000.0, fdscAudio.Asbd.SampleRate);

var intdict = IndexKeyDict.Parse(W("intdict.bin"));
Eq("intdict.entries", 2, intdict.Entries.Count);
Eq("intdict.k0", (ushort)49, intdict.Entries[0].Key);
Check("intdict.nested", intdict.Entries[0].Value is IndexKeyDict);
var nested = (IndexKeyDict)intdict.Entries[0].Value!;
Eq("intdict.nested.len", 1, nested.Entries.Count);
Eq("intdict.nested.k", (ushort)105, nested.Entries[0].Key);
Eq("intdict.nested.data", 36, ((byte[])nested.Entries[0].Value!).Length);
Eq("intdict.k1", (ushort)52, intdict.Entries[1].Key);
Eq("intdict.v1", "H.264", intdict.Entries[1].Value);

var bul = StringKeyDict.Parse(W("bulvalue.bin"));
Eq("bul.entries", 1, bul.Entries.Count);
Eq("bul.key", "Valeria", bul.Entries[0].Key);
Eq("bul.value", true, bul.Entries[0].Value);
var bulDict = new StringKeyDict();
bulDict.Entries.Add(new("Valeria", true));
Bytes("bul.serialized", Load("bulvalue.bin"), bulDict.Serialize());

var dict = StringKeyDict.Parse(W("dict.bin"));
Eq("dict.entries", 3, dict.Entries.Count);
Eq("dict.k0", "Valeria", dict.Entries[0].Key);
Eq("dict.v0", true, dict.Entries[0].Value);
Eq("dict.k1", "HEVCDecoderSupports444", dict.Entries[1].Key);
Eq("dict.v1", true, dict.Entries[1].Value);
Eq("dict.k2", "DisplaySize", dict.Entries[2].Key);
var display = (StringKeyDict)dict.Entries[2].Value!;
Eq("dict.w", 1920.0, ((NSNumber)display.Entries[0].Value!).FloatValue);
Eq("dict.h", 1200.0, ((NSNumber)display.Entries[1].Value!).FloatValue);
Bytes("dict.serialized", Load("dict.bin"), QtPackets.AsynHpd1Bytes()[20..]);
Bytes("hpa1dict.serialized", Load("serialize_dict.bin"), QtPackets.AsynHpa1Bytes(0x00000001145392F0)[20..]);

var complex = StringKeyDict.Parse(W("complex_dict.bin"));
Eq("complex.entries", 3, complex.Entries.Count);
Check("complex.fdsc", complex.Entries[2].Value is FormatDescriptor);

var asbdBytes = Load("adsb-from-hpa-dict.bin");
var asbd = AudioStreamBasicDescription.Parse(asbdBytes);
Eq("asbd.rate", 48000.0, asbd.SampleRate);
Eq("asbd.flags", 12U, asbd.FormatFlags);
var asbdOut = new byte[AudioStreamBasicDescription.LengthInBytes];
asbd.Serialize(asbdOut);
Bytes("asbd.serialized", asbdBytes, asbdOut);

var skewT1 = new CMTime { Value = 0, Scale = 48000 };
var skewT2 = new CMTime { Value = 1, Scale = 48000 };
Eq("skew.none", 48000.0, CMClock.CalculateSkew(skewT1, skewT2, skewT1, skewT2));
Eq("skew.pos", 96000.0, CMClock.CalculateSkew(skewT1, new CMTime { Value = 2, Scale = 48000 }, skewT1, skewT2));
Eq("skew.neg", 47976.011994003, Math.Round(CMClock.CalculateSkew(skewT1, new CMTime { Value = 2000, Scale = 48000 }, skewT1, new CMTime { Value = 2001, Scale = 48000 }), 9));
var ns1 = new CMTime { Value = 0, Scale = CMClock.NanoSecondScale };
Eq("skew.scales.neg", 47999.232, Math.Round(CMClock.CalculateSkew(ns1, new CMTime { Value = 20833 * 5, Scale = CMClock.NanoSecondScale }, skewT1, new CMTime { Value = 5, Scale = 48000 }), 6));
Eq("skew.scales.pos", 48008.8318464, Math.Round(CMClock.CalculateSkew(ns1, new CMTime { Value = 20833 * 5001, Scale = CMClock.NanoSecondScale }, skewT1, new CMTime { Value = 5000, Scale = 48000 }), 7));

var clock = CMClock.WithHostTime(0xdead);
Eq("clock.scale", CMClock.NanoSecondScale, clock.TimeScale);
Check("clock.monotonic", clock.GetTime().Value <= clock.GetTime().Value);

var written = new List<byte[]>();
var consumed = new List<CMSampleBuffer>();
var session = new QtSession(written.Add, new SampleSink(consumed));
session.ReceiveFrame(Convert.FromHexString("676e69700000000001000000"));
Eq("session.ping.reply", 1, written.Count);
Bytes("session.ping.bytes", Convert.FromHexString("10000000676e69700000000001000000"), written[0]);

session.ReceiveFrame(Load("cwpa-request1")[4..]);
Check("session.cwpa.replies", written.Count >= 3);
session.ReceiveFrame(Load("cvrp-request")[4..]);
session.ReceiveFrame(Load("asyn-feed")[4..]);
Eq("session.feed.consumed", 1, consumed.Count);
session.ReceiveFrame(W("asyn-eat"));
Eq("session.eat.consumed", 2, consumed.Count);

Console.WriteLine($"\n{pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;

internal sealed class SampleSink : ISampleConsumer
{
    private readonly List<CMSampleBuffer> _sink;
    public SampleSink(List<CMSampleBuffer> sink) => _sink = sink;
    public void Consume(CMSampleBuffer buf) => _sink.Add(buf);
}
