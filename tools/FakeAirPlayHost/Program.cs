using System.IO.Pipes;

string Arg(string key, string def) {
    for (int i = 0; i + 1 < args.Length; i++)
        if (args[i] == key) return args[i + 1];
    return def;
}
var vp = Arg("--video-pipe", "");
var ep = Arg("--event-pipe", "");
if (vp == "" || ep == "") { Console.Error.WriteLine("usage: --video-pipe P --event-pipe P"); return 2; }

var video = new NamedPipeClientStream(".", vp, PipeDirection.Out);
var events = new NamedPipeClientStream(".", ep, PipeDirection.Out);
video.Connect(15000);
events.Connect(15000);

var sw = new StreamWriter(events);
sw.WriteLine("{\"type\":\"ready\",\"name\":\"FakeHost\",\"deviceId\":\"\"}");
sw.Flush();
// délai réaliste : l'app souscrit aux events après StartAsync
Thread.Sleep(2500);
sw.WriteLine("{\"type\":\"connected\",\"name\":\"Fake iPhone\",\"deviceId\":\"AA:BB:CC:DD:EE:FF\"}");
sw.Flush();

const int W = 640, H = 360;
int yLen = W * H, uvLen = (W / 2) * (H / 2);
var y = new byte[yLen]; var u = new byte[uvLen]; var v = new byte[uvLen];
var frame = new byte[yLen + uvLen + uvLen];
var id = System.Text.Encoding.UTF8.GetBytes("AA:BB:CC:DD:EE:FF");

byte[] Head(long pts, int idLen) {
    var ms = new MemoryStream();
    var bw = new BinaryWriter(ms);
    bw.Write((uint)(4 + 8 + 4 + 4 + 12 + 12 + 1 + 4 + idLen + frame.Length));
    bw.Write(1u);
    bw.Write((ulong)pts);
    bw.Write((uint)W); bw.Write((uint)H);
    bw.Write((uint)W); bw.Write((uint)(W/2)); bw.Write((uint)(W/2));
    bw.Write((uint)yLen); bw.Write((uint)uvLen); bw.Write((uint)uvLen);
    bw.Write((byte)1);
    bw.Write((uint)idLen);
    return ms.ToArray();
}

long pts = 0;
for (int t = 0; t < 600; t++) {
    for (int j = 0; j < H; j++)
        for (int i = 0; i < W; i++)
            y[j * W + i] = (byte)((i + t * 4) & 0xFF);
    int bx = (t * 5) % (W - 80), by = (t * 3) % (H - 80);
    for (int j = by; j < by + 80; j++)
        for (int i = bx; i < bx + 80; i++)
            y[j * W + i] = 255;
    Array.Fill(u, (byte)128); Array.Fill(v, (byte)128);
    Buffer.BlockCopy(y, 0, frame, 0, yLen);
    Buffer.BlockCopy(u, 0, frame, yLen, uvLen);
    Buffer.BlockCopy(v, 0, frame, yLen + uvLen, uvLen);

    video.Write(Head(pts, id.Length));
    video.Write(id);
    video.Write(frame);
    video.Flush();
    pts += 33333;
    Thread.Sleep(33);
}
sw.WriteLine("{\"type\":\"disconnected\",\"name\":\"Fake iPhone\",\"deviceId\":\"AA:BB:CC:DD:EE:FF\"}");
sw.Flush();
return 0;
