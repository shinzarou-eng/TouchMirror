using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TouchMirror.Services;

public sealed record AdbDevice(string Serial, string Model, string State)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? Serial : $"{Model} ({Serial})";
    public bool IsReady => State == "device";
}

public static class AdbService
{
    private static string? _adbPath;

    public static string? FindAdb()
    {
        if (_adbPath != null)
            return _adbPath;

        var candidates = new List<string>();

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "assets", "platform-tools", "adb.exe"));

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe"));

        foreach (var env in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            var root = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrEmpty(root))
                candidates.Add(Path.Combine(root, "platform-tools", "adb.exe"));
        }

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                _adbPath = c;
                return c;
            }
        }

        try
        {
            var p = Process.Start(new ProcessStartInfo("where.exe", "adb")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            var line = p?.StandardOutput.ReadLine();
            p?.WaitForExit(3000);
            if (!string.IsNullOrWhiteSpace(line) && File.Exists(line.Trim()))
            {
                _adbPath = line.Trim();
                return _adbPath;
            }
        }
        catch { }

        return null;
    }

    private static async Task<string> RunAsync(string args, CancellationToken ct = default)
    {
        var adb = FindAdb() ?? throw new InvalidOperationException("adb introuvable (platform-tools du SDK Android requis)");
        var psi = new ProcessStartInfo(adb, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var p = Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync(ct);
        var stderr = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"adb {args} -> {p.ExitCode}: {stderr.Trim()}");
        return stdout;
    }

    public static async Task<IReadOnlyList<AdbDevice>> GetDevicesAsync(CancellationToken ct = default)
    {
        var output = await RunAsync("devices -l", ct);
        var devices = new List<AdbDevice>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;
            var modelMatch = Regex.Match(line, @"model:([^\s]+)");
            var model = modelMatch.Success ? modelMatch.Groups[1].Value.Replace('_', ' ') : "";
            devices.Add(new AdbDevice(parts[0], model, parts[1]));
        }
        return devices;
    }

    public static async Task PushAsync(string serial, string localPath, string remotePath, CancellationToken ct = default)
        => await RunAsync($"-s {serial} push \"{localPath}\" {remotePath}", ct);

    public static async Task ReverseAsync(string serial, string deviceSocket, int localPort, CancellationToken ct = default)
        => await RunAsync($"-s {serial} reverse localabstract:{deviceSocket} tcp:{localPort}", ct);

    public static async Task<string> EnableWifiAsync(string serial, CancellationToken ct = default)
    {
        await RunAsync($"-s {serial} tcpip 5555", ct);

        string ip = "";
        for (var i = 0; i < 8 && ip.Length == 0; i++)
        {
            await Task.Delay(1000, ct);
            try
            {
                var ipOutput = await RunAsync($"-s {serial} shell ip -f inet addr show wlan0", ct);
                var match = Regex.Match(ipOutput, @"inet (\d+\.\d+\.\d+\.\d+)");
                if (match.Success)
                    ip = match.Groups[1].Value;
            }
            catch { }
        }
        if (ip.Length == 0)
            throw new InvalidOperationException("IP WiFi introuvable — téléphone connecté en WiFi ?");

        for (var i = 0; i < 3; i++)
        {
            var result = await RunAsync($"connect {ip}:5555", ct);
            if (result.Contains("connected"))
                return ip;
            await Task.Delay(800, ct);
        }
        throw new InvalidOperationException($"adb connect {ip}:5555 a échoué — PC et tel sur le même WiFi ?");
    }

    public static async Task<(int W, int H)> GetScreenSizeAsync(string serial, CancellationToken ct = default)
    {
        var output = await RunAsync($"-s {serial} shell wm size", ct);
        var m = Regex.Match(output, @"(\d+)x(\d+)");
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : (0, 0);
    }

    public static async Task ReverseRemoveAsync(string serial, string deviceSocket)
    {
        try { await RunAsync($"-s {serial} reverse --remove localabstract:{deviceSocket}"); }
        catch { }
    }

    public static Process StartServerProcess(string serial, string remoteJar, string arguments)
    {
        var adb = FindAdb() ?? throw new InvalidOperationException("adb introuvable");
        var psi = new ProcessStartInfo(adb,
            $"-s {serial} shell CLASSPATH={remoteJar} app_process / com.genymobile.scrcpy.Server {arguments}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        return Process.Start(psi)!;
    }
}