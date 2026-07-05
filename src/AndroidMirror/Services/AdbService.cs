using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TouchMirror.Services;

public sealed record AdbDevice(string Serial, string Model, string State, int? Battery = null,
    string? HardwareSerial = null, string? AltSerial = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? Serial : $"{Model} ({Serial})";
    public string ShortName => string.IsNullOrWhiteSpace(Model) ? Serial : Model;
    public bool IsReady => State == "device";
    public bool NeedsAuthorization => State == "unauthorized";
    public bool IsOffline => State == "offline";
    public string StateText => State switch
    {
        "device" => "Prêt",
        "unauthorized" => "À autoriser sur le téléphone",
        "offline" => "Hors ligne — rebranche",
        _ => "Non prêt"
    };
    public bool HasBattery => Battery.HasValue;
    public string BatteryText => Battery.HasValue ? $"{Battery} %" : "";
    public bool IsWifi => Serial.Contains(':');
    public bool HasDualTransport => AltSerial != null;
    public string TransportText => HasDualTransport
        ? (IsWifi ? $"USB + WiFi — {Serial}" : "USB + WiFi")
        : IsWifi ? $"WiFi — {Serial}" : "USB";
    public bool ShowSerial => !IsWifi;

    public bool MatchesSerial(string s) => Serial == s || AltSerial == s;

    public bool SharesIdentity(AdbDevice o)
        => (HardwareSerial is { Length: > 0 } && HardwareSerial == o.HardwareSerial)
           || MatchesSerial(o.Serial) || (o.AltSerial != null && MatchesSerial(o.AltSerial));

    public AdbDevice Preferring(string serial)
        => Serial == serial ? this : AltSerial == serial ? this with { Serial = serial } : this;
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var stdout = await p.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = await p.StandardError.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token);
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"adb {args} -> {p.ExitCode}: {stderr.Trim()}");
            return stdout;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { p.Kill(); } catch { }
            throw new InvalidOperationException($"adb {args} — délai dépassé (30 s)");
        }
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

        var enriched = await Task.WhenAll(devices.Select(async d =>
        {
            if (!d.IsReady)
                return d;
            var battery = GetBatteryLevelAsync(d.Serial, ct);
            var hw = GetHardwareSerialAsync(d.Serial, ct);
            await Task.WhenAll(battery, hw);
            return d with { Battery = battery.Result, HardwareSerial = hw.Result };
        }));

        var result = new List<AdbDevice>();
        foreach (var g in enriched.GroupBy(d =>
                     d.HardwareSerial is { Length: > 0 } h ? h : $"{d.State}:{d.Serial}"))
        {
            var list = g.ToList();
            if (list.Count == 1)
            {
                result.Add(list[0]);
                continue;
            }
            var preferred = list.FirstOrDefault(d => !d.IsWifi) ?? list[0];
            var alt = list.FirstOrDefault(d => d.Serial != preferred.Serial);
            result.Add(preferred with { AltSerial = alt?.Serial });
        }
        return result;
    }

    public static async Task<string?> GetHardwareSerialAsync(string serial, CancellationToken ct = default)
    {
        try
        {
            var output = await RunAsync($"-s {serial} shell getprop ro.serialno", ct);
            var s = output.Trim();
            return s.Length > 0 ? s : null;
        }
        catch { return null; }
    }

    public static async Task<int?> GetBatteryLevelAsync(string serial, CancellationToken ct = default)
    {
        try
        {
            var output = await RunAsync($"-s {serial} shell dumpsys battery", ct);
            var m = Regex.Match(output, @"level:\s*(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : null;
        }
        catch { return null; }
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

    public static async Task<int> GetBrightnessAsync(string serial, CancellationToken ct = default)
    {
        var output = await RunAsync($"-s {serial} shell settings get system screen_brightness", ct);
        return int.TryParse(output.Trim(), out var v) ? v : -1;
    }

    public static async Task SetBrightnessAsync(string serial, int value, CancellationToken ct = default)
    {
        await RunAsync($"-s {serial} shell settings put system screen_brightness_mode 0", ct);
        await RunAsync($"-s {serial} shell settings put system screen_brightness {value}", ct);
    }

    public static async Task<int> GetStayOnWhilePluggedInAsync(string serial, CancellationToken ct = default)
    {
        var output = await RunAsync($"-s {serial} shell settings get global stay_on_while_plugged_in", ct);
        return int.TryParse(output.Trim(), out var v) ? v : -1;
    }

    public static async Task SetStayOnWhilePluggedInAsync(string serial, int value, CancellationToken ct = default)
    {
        await RunAsync($"-s {serial} shell settings put global stay_on_while_plugged_in {value}", ct);
    }

    public static async Task WakeScreenAsync(string serial, CancellationToken ct = default)
    {
        await RunAsync($"-s {serial} shell input keyevent KEYCODE_WAKEUP", ct);
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