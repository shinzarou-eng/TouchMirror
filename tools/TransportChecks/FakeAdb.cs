using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

internal static class FakeAdb
{
    public static string Root => Environment.GetEnvironmentVariable("TOUCHMIRROR_CHECK_ROOT")
        ?? throw new InvalidOperationException("Test root not configured");
    public static string Remote(string path) => Path.Combine(Root, Path.GetFileName(path));

    public static async Task<int> RunAsync(string[] args)
    {
        if (args is ["devices", "-l"])
        {
            Console.WriteLine("List of devices attached");
            var state = File.ReadAllText(Path.Combine(Root, "state"));
            if (state != "missing") Console.WriteLine($"test-phone\t{state} model:Test_Phone");
            return 0;
        }
        if (args.Length < 3 || args[0] != "-s" || args[1] != "test-phone") return 2;
        args = args[2..];
        if (args is ["push", var local, var remote])
        {
            File.Copy(local, Remote(remote), true);
            File.AppendAllText(Path.Combine(Root, "pushes"), ".");
            return 0;
        }
        if (args is ["shell", "sha256sum", var path])
        {
            if (!File.Exists(Remote(path))) return 1;
            Console.WriteLine($"{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Remote(path))))}  {path}");
            return 0;
        }
        if (args is ["shell", "cp", var source, var target])
        {
            File.Copy(Remote(source), Remote(target), true);
            return 0;
        }
        if (args is ["reverse", "--remove", var socket])
        {
            File.Delete(Path.Combine(Root, socket.Replace(':', '_')));
            return 0;
        }
        if (args is ["reverse", "--list"])
        {
            foreach (var f in Directory.GetFiles(Root, "localabstract_*"))
            {
                var n = Path.GetFileName(f);
                var sep = n.IndexOf('_');
                if (sep > 0)
                    Console.WriteLine($"{n[..sep]}:{n[(sep + 1)..]} tcp:{File.ReadAllText(f)}");
            }
            return 0;
        }
        if (args is ["reverse", var name, var port])
        {
            File.WriteAllText(Path.Combine(Root, name.Replace(':', '_')), port[4..]);
            return 0;
        }
        if (args is ["shell", var classpath, "app_process", "/", "com.touchmirror.engine.Server", "4.0", var scid])
        {
            if (!File.Exists(Remote(classpath[10..]))) return 3;
            File.AppendAllText(Path.Combine(Root, "starts"), ".");
            var portFile = Path.Combine(Root, $"localabstract_touchmirror_{scid}");
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, int.Parse(File.ReadAllText(portFile)));
            File.WriteAllText(Path.Combine(Root, "accepted"), "yes");
            var stream = client.GetStream();
            var mode = File.ReadAllText(Path.Combine(Root, "mode"));
            if (mode == "eof") return 0;
            if (mode == "partial")
            {
                await stream.WriteAsync(new byte[] { 0, 0, 0 });
                return 0;
            }
            if (mode == "v3")
            {
                var old = new byte[16];
                old[0] = 0;
                BinaryPrimitives.WriteUInt32BigEndian(old.AsSpan(1), 11);
                BinaryPrimitives.WriteUInt32BigEndian(old.AsSpan(5), 0x544d4952);
                BinaryPrimitives.WriteUInt16BigEndian(old.AsSpan(9), 3);
                await stream.WriteAsync(old);
                await DrainAsync(stream);
                return 0;
            }
            if (mode != "stall")
            {
                var hello = new byte[20];
                "TMIR"u8.CopyTo(hello);
                BinaryPrimitives.WriteInt32BigEndian(hello.AsSpan(5), 11);
                BinaryPrimitives.WriteUInt32BigEndian(hello.AsSpan(9), 0x544d4952);
                BinaryPrimitives.WriteUInt16BigEndian(hello.AsSpan(13), 4);
                hello[19] = mode == "bad-name" ? (byte)255 : (byte)0;
                if (mode == "bad-magic")
                    hello[0] = (byte)'X';
                await stream.WriteAsync(hello);
            }
            if (mode == "cfg-echo")
            {
                try
                {
                    client.ReceiveTimeout = 8000;
                    var head = new byte[9];
                    var got = 0;
                    while (got < 9)
                        got += stream.Read(head, got, 9 - got);
                    var len = BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(5));
                    var ok = head.AsSpan(0, 4).SequenceEqual("TMIR"u8) && head[4] == 3 && len is > 0 and < 4096;
                    File.WriteAllText(Path.Combine(Root, ok ? "cfgok" : "cfgfail"), "x");
                }
                catch { File.WriteAllText(Path.Combine(Root, "cfgfail"), "x"); }
            }
            if (mode == "desync")
            {
                var junk = new byte[37];
                new Random(3).NextBytes(junk);
                await stream.WriteAsync(junk);
                await stream.WriteAsync(VideoFrame(0x11));
                var mid = new byte[19];
                new Random(4).NextBytes(mid);
                mid[5] = (byte)'T'; mid[6] = (byte)'M'; mid[7] = (byte)'I'; mid[8] = (byte)'R';
                mid[9] = 0xFF;
                await stream.WriteAsync(mid);
                await stream.WriteAsync(VideoFrame(0x22));
            }
            if (mode == "cut")
            {
                await stream.WriteAsync(VideoFrame(0x33));
                return 0;
            }
            await DrainAsync(stream);
            return 0;
        }
        return 2;
    }

    private static byte[] VideoFrame(int tag)
    {
        var frame = new byte[9 + 14];
        "TMIR"u8.CopyTo(frame);
        frame[4] = 1;
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(5), 14);
        frame[9] = 1;
        BinaryPrimitives.WriteInt64BigEndian(frame.AsSpan(10), 1000);
        frame[18] = 2;
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(19), tag);
        return frame;
    }

    private static async Task DrainAsync(NetworkStream stream)
    {
        var buffer = new byte[1024];
        while (await stream.ReadAsync(buffer) > 0) { }
    }
}
