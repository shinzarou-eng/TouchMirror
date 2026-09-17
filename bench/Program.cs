using System.Diagnostics;
using TouchMirror.Video;
using FFmpeg.AutoGen;

var path = args[0];
var ext = Path.GetExtension(path).TrimStart('.');
var codec = ext is "h265" or "hevc" ? "h265" : ext == "av1" ? "av1" : "h264";
var bytes = File.ReadAllBytes(path);

if (args.Length > 1 && args[1] == "mux")
{
    MuxTest(path, codec, bytes);
    return;
}
if (args.Length > 1 && args[1] == "probe")
{
    VideoDecoder.InitializeFFmpeg();
    unsafe
    {
        void* iter = null;
        AVCodec* c;
        while ((c = ffmpeg.av_codec_iterate(&iter)) != null)
            if (ffmpeg.av_codec_is_decoder(c) != 0 &&
                (System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)c->name)?.Contains("av1") == true
                 || System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)c->name)?.Contains("hevc") == true))
                Console.WriteLine($"decoder: {System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)c->name)}");
    }
    return;
}
var iters = args.Length > 1 ? int.Parse(args[1]) : 3;

var auFile = Path.Combine(Path.GetDirectoryName(path) ?? ".", "au_sizes.json");
var packets = new List<byte[]>();
if (codec == "av1")
{
    var i = 0;
    while (i < bytes.Length && !TryObu(bytes, i, out _, out _)) i++;
    while (i < bytes.Length)
    {
        var j = i;
        while (j < bytes.Length)
        {
            if (!TryObu(bytes, j, out var t, out var l)) { j = bytes.Length; break; }
            if (t == 2 && j > i)
                break;
            j += l;
        }
        packets.Add(bytes[i..Math.Min(j, bytes.Length)]);
        i = j <= i ? bytes.Length : j;
    }
}
else
{
    var nals = SplitAnnexB(bytes);
    if (File.Exists(auFile))
    {
        var sizes = System.Text.Json.JsonSerializer.Deserialize<int[]>(File.ReadAllText(auFile))!;
        var off = 0;
        foreach (var sz in sizes)
        {
            var au = new List<byte[]>();
            var acc = 0;
            while (acc < sz && nals.Count > 0) { au.Add(nals[0]); acc += nals[0].Length; nals.RemoveAt(0); }
            packets.Add(au.SelectMany(x => x).ToArray());
        }
    }
    else packets = nals;
}
Console.WriteLine($"{path} — {bytes.Length / 1024.0:F0} KB, {packets.Count} paquets, codec={codec}");

foreach (var mode in new[] { "gpu", "cpu", "gpu", "cpu" })
{
    var hw = mode == "gpu";
    int frames = 0;
    long feedTicks = 0;
    bool hwActive = false; int fallbacks = 0;
    var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
    var sw = Stopwatch.StartNew();
    using (var dec = new VideoDecoder(codec, preferHardware: hw))
    {
        var colorPrinted = false;
        dec.GpuFrame += (t, s, w, h, ci) =>
        {
            frames++;
            if (colorPrinted) return;
            colorPrinted = true;
            var sp = (ci >> 1) switch { 0 => "bt601", 2 => "bt2020", _ => "bt709" };
            Console.WriteLine($"colorspace={sp} range={((ci & 1) == 1 ? "full" : "limited")}");
        };
        dec.FrameAvailable += () =>
        {
            frames++;
            while (dec.TryTakeLatest(out var buf, out _, out _))
                if (buf != null) dec.Release(buf);
        };
        foreach (var nal in packets)
        {
            var t = Stopwatch.GetTimestamp();
            dec.Feed(nal);
            feedTicks += Stopwatch.GetTimestamp() - t;
            while (dec.TryTakeLatest(out var b, out _, out _))
                if (b != null) dec.Release(b);
        }
        Thread.Sleep(300);
        hwActive = dec.HardwareDecoding;
        fallbacks = dec.HardwareFallbacks;
    }
    sw.Stop();
    var cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuStart).TotalMilliseconds;
    var wall = sw.Elapsed.TotalMilliseconds;
    Console.WriteLine($"[{(hw ? "GPU" : "CPU")}{(hwActive ? "+hw" : hw ? "→sw!" : "")}{(fallbacks > 0 ? $" fallbacks={fallbacks}" : "")}] frames={frames} wall={wall:F0}ms " +
        $"feed_decode={feedTicks / (double)Stopwatch.Frequency * 1000:F0}ms " +
        $"({feedTicks / (double)Stopwatch.Frequency * 1000 / Math.Max(frames, 1):F2}ms/frame) " +
        $"cpu_proc={cpu:F0}ms ({100 * cpu / wall:F1}% d'un cœur)  " +
        $"débit décodé≈{frames / (wall / 1000):F0} fps");
}

static void MuxTest(string srcPath, string codec, byte[] bytes)
{
    VideoDecoder.InitializeFFmpeg();
    var outPath = Path.ChangeExtension(srcPath, ".test.mp4");
    using var rec = new Mp4Recorder(outPath, 2560, 1600, codec);

    List<byte[]> units;
    if (codec == "av1")
    {
        units = new List<byte[]>();
        var i = 0;
        while (i < bytes.Length && !TryObu(bytes, i, out _, out _)) i++;
        while (i < bytes.Length)
        {
            var j = i;
            while (j < bytes.Length)
            {
                if (!TryObu(bytes, j, out var t, out var l)) { j = bytes.Length; break; }
                if (t == 2 && j > i)
                    break;
                j += l;
            }
            units.Add(bytes[i..Math.Min(j, bytes.Length)]);
            i = j <= i ? bytes.Length : j;
        }
    }
    else
    {
        units = new List<byte[]>();
        var aus = new List<byte[]>();
        byte[] Join(IEnumerable<byte[]> ns) => ns.SelectMany(n => n).ToArray();
        bool IsVcl(byte[] nal)
        {
            var off = nal.Length > 3 && nal[2] == 1 ? 3 : 4;
            var h = nal[off];
            return codec == "h265" ? (((h >> 1) & 0x3F) <= 31) : ((h & 0x1F) <= 5);
        }
        foreach (var nal in SplitAnnexB(bytes))
        {
            bool vcl = IsVcl(nal);
            bool newAu = vcl && aus.Any(IsVcl);
            if (newAu && aus.Count > 0)
            {
                units.Add(Join(aus));
                aus.Clear();
            }
            aus.Add(nal);
        }
        if (aus.Count > 0)
            units.Add(Join(aus));
    }

    int cfgIdx;
    if (codec == "av1")
    {
        cfgIdx = 0;
    }
    else
    {
        cfgIdx = units.FindIndex(u => SplitAnnexB(u).Any(n =>
        {
            var off = n.Length > 3 && n[2] == 1 ? 3 : 4;
            var type = codec == "h265" ? ((n[off] >> 1) & 0x3F) : (n[off] & 0x1F);
            return type == (codec == "h265" ? 33 : 7);
        }));
    }
    if (cfgIdx < 0)
    {
        Console.WriteLine($"pas de config trouvée dans {units.Count} unités — fichier sans header attendu");
    }
    else
    {
        rec.WriteConfig(units[cfgIdx]);
    }
    Console.WriteLine($"config: headerWritten={rec.HeaderWritten}, {units.Count} unités");
    long pts = 0;
    var first = true;
    for (var k = 0; k < units.Count; k++)
    {
        if (k == cfgIdx) continue;
        rec.WritePacket(units[k], pts += 33_000, keyframe: first);
        first = false;
    }
    Thread.Sleep(50);
    rec.Dispose();

    var info = new FileInfo(outPath);
    Console.WriteLine($"mp4: {info.Length / 1024} KB");
    unsafe
    {
        AVFormatContext* fmt = null;
        var r = ffmpeg.avformat_open_input(&fmt, outPath, null, null);
        if (r < 0) { Console.WriteLine("avformat_open_input ECHEC"); return; }
        for (uint s = 0; s < fmt->nb_streams; s++)
            Console.WriteLine($"stream {s}: codec_id={fmt->streams[s]->codecpar->codec_id} " +
                $"{fmt->streams[s]->codecpar->width}x{fmt->streams[s]->codecpar->height} " +
                $"extradata={fmt->streams[s]->codecpar->extradata_size}B");
        var pkt = ffmpeg.av_packet_alloc();
        var n = 0;
        while (ffmpeg.av_read_frame(fmt, pkt) >= 0) { n++; ffmpeg.av_packet_unref(pkt); }
        Console.WriteLine($"packets lus={n}");
        ffmpeg.avformat_close_input(&fmt);
    }
}

static unsafe bool TryObu(byte[] d, int i, out int type, out int len)
{
    type = -1; len = 0;
    if (i >= d.Length || (d[i] & 0x80) != 0) return false;
    type = (d[i] >> 3) & 0xF;
    bool ext = (d[i] & 0x04) != 0;
    bool hasSize = (d[i] & 0x02) != 0;
    int p = i + 1 + (ext ? 1 : 0);
    if (!hasSize) return false;
    long size = 0; int sh = 0;
    while (p < d.Length)
    {
        size |= (long)(d[p] & 0x7f) << sh;
        if ((d[p++] & 0x80) == 0) break;
        sh += 7;
    }
    len = p - i + (int)size;
    return i + len <= d.Length;
}

static List<byte[]> SplitAnnexB(byte[] buf)
{
    var list = new List<byte[]>();
    var i = 0; var start = -1;
    while (i < buf.Length - 2)
    {
        var isStart = buf[i] == 0 && buf[i + 1] == 0 &&
                      (buf[i + 2] == 1 || (buf[i + 2] == 0 && i + 3 < buf.Length && buf[i + 3] == 1));
        if (isStart)
        {
            if (start >= 0) list.Add(buf[start..i]);
            start = i;
            i += buf[i + 2] == 1 ? 3 : 4;
        }
        else i++;
    }
    if (start >= 0 && start < buf.Length) list.Add(buf[start..]);
    return list;
}
