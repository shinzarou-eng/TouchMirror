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
        if (args is ["reverse", var name, var port])
        {
            File.WriteAllText(Path.Combine(Root, name.Replace(':', '_')), port[4..]);
            return 0;
        }
        if (args is ["shell", var classpath, "app_process", "/", "com.touchmirror.engine.Server", "3.0", var scid])
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
            if (mode != "stall")
            {
                var hello = new byte[16];
                hello[0] = 0;
                BinaryPrimitives.WriteUInt32BigEndian(hello.AsSpan(1), 11);
                BinaryPrimitives.WriteUInt32BigEndian(hello.AsSpan(5), 0x544d4952);
                BinaryPrimitives.WriteUInt16BigEndian(hello.AsSpan(9), 3);
                hello[15] = mode == "bad-name" ? (byte)255 : (byte)0;
                await stream.WriteAsync(hello);
            }
            var buffer = new byte[1024];
            while (await stream.ReadAsync(buffer) > 0) { }
            return 0;
        }
        return 2;
    }
}
