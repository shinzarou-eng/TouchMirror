using System.Diagnostics;
using TouchMirror.Video;

var path = args[0];
var iters = args.Length > 1 ? int.Parse(args[1]) : 3;
var codec = Path.GetExtension(path).TrimStart('.') is "h265" or "hevc" ? "h265" : "h264";
var bytes = File.ReadAllBytes(path);
var nals = SplitAnnexB(bytes);

var auFile = Path.Combine(Path.GetDirectoryName(path) ?? ".", "au_sizes.json");
var packets = new List<byte[]>();
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
