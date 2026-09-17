using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TouchMirror.Services;

public sealed record AdbDevice(string Serial, string Model, string State, int? Battery = null,
    string? HardwareSerial = null, string? AltSerial = null, string? CustomName = null,
    string? Color = null, string? Diag = null)
{
    public string DeviceKey => HardwareSerial is { Length: > 0 } h ? h : Serial;
    public string DisplayName => CustomName ?? (string.IsNullOrWhiteSpace(Model) ? Serial : $"{Model} ({Serial})");
    public string ShortName => CustomName ?? (string.IsNullOrWhiteSpace(Model) ? Serial : Model);
    public bool IsReady => State == "device";
    public bool NeedsAuthorization => State == "unauthorized";
    public bool IsOffline => State == "offline";
    public bool IsRememberedOnly => State == "remembered";
    public bool HasDiag => Diag != null;
    private static string L(string key) => LocalizationService.Get(key);
    public string StateText => State switch
    {
        "device" => L("dev.ready"),
        "unauthorized" => L("dev.unauthorized"),
        "offline" => L("dev.offline"),
        "remembered" => L("dev.remembered"),
        _ => L("dev.notready")
    };
    public bool HasBattery => Battery.HasValue;
    public string BatteryText => Battery.HasValue ? $"{Battery} %" : "";
    public bool IsWifi => Serial.Contains(':');
    public bool HasDualTransport => AltSerial != null;
    public string TransportText => IsRememberedOnly ? L("dev.memorized")
        : HasDualTransport
            ? (IsWifi ? $"USB + WiFi — {Serial}" : "USB + WiFi")
            : IsWifi ? $"WiFi — {Serial}" : "USB";
    public bool ShowSerial => !IsWifi && !IsRememberedOnly;
    public string? SelectorHint => State switch
    {
        "unauthorized" => L("dev.hint_unauth"),
        "offline" => L("dev.hint_offline"),
        "remembered" => L("dev.hint_remembered"),
        _ => null
    };

    public bool MatchesSerial(string s) => Serial == s || AltSerial == s;

    public bool SharesIdentity(AdbDevice o)
        => (HardwareSerial is { Length: > 0 } && HardwareSerial == o.HardwareSerial)
           || MatchesSerial(o.Serial) || (o.AltSerial != null && MatchesSerial(o.AltSerial));

    public AdbDevice Preferring(string serial)
        => Serial == serial ? this : AltSerial == serial ? this with { Serial = serial } : this;
}

public sealed record AndroidProfile(int Id, string Name, bool Running);

public static class AdbService
{
    private static readonly Regex SerialOk = new(@"^[A-Za-z0-9._:\-]+$", RegexOptions.Compiled);

    private static string S(string serial)
        => SerialOk.IsMatch(serial) ? serial
           : throw new ArgumentException($"serial adb invalide : « {serial} »");

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
            throw new InvalidOperationException(string.Format(LocalizationService.Get("ex.adb_timeout"), args));
        }
    }

    public static async Task TrackDevicesAsync(Action onChanged, CancellationToken ct)
    {
        var adb = FindAdb();
        if (adb == null)
            return;
        var buf = new char[256];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var psi = new ProcessStartInfo(adb, "track-devices")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using var p = Process.Start(psi)!;
                while (!p.HasExited && !ct.IsCancellationRequested)
                {
                    var n = await p.StandardOutput.ReadAsync(buf, ct);
                    if (n == 0)
                        break;
                    onChanged();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch { }
            try { await Task.Delay(2000, ct); } catch { }
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
            AdbDiagnostics.Record(parts[0], parts[1]);
            devices.Add(new AdbDevice(parts[0], model, parts[1],
                Diag: AdbDiagnostics.Verdict(parts[0], parts[1])));
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
            var output = await RunAsync($"-s {S(serial)} shell getprop ro.serialno", ct);
            var s = output.Trim();
            return s.Length > 0 ? s : null;
        }
        catch { return null; }
    }

    public static async Task<int?> GetBatteryLevelAsync(string serial, CancellationToken ct = default)
    {
        try
        {
            var output = await RunAsync($"-s {S(serial)} shell dumpsys battery", ct);
            var m = Regex.Match(output, @"level:\s*(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : null;
        }
        catch { return null; }
    }

    public static async Task PushAsync(string serial, string localPath, string remotePath, CancellationToken ct = default)
        => await RunAsync($"-s {S(serial)} push \"{localPath}\" {remotePath}", ct);

    public static async Task ReverseAsync(string serial, string deviceSocket, int localPort, CancellationToken ct = default)
        => await RunAsync($"-s {S(serial)} reverse localabstract:{deviceSocket} tcp:{localPort}", ct);

    public static async Task<string> EnableWifiAsync(string serial, CancellationToken ct = default)
    {
        await RunAsync($"-s {S(serial)} tcpip 5555", ct);

        string ip = "";
        for (var i = 0; i < 8 && ip.Length == 0; i++)
        {
            await Task.Delay(1000, ct);
            try
            {
                var ipOutput = await RunAsync($"-s {S(serial)} shell ip -f inet addr show wlan0", ct);
                var match = Regex.Match(ipOutput, @"inet (\d+\.\d+\.\d+\.\d+)");
                if (match.Success)
                    ip = match.Groups[1].Value;
            }
            catch { }
        }
        if (ip.Length == 0)
            throw new InvalidOperationException(LocalizationService.Get("ex.no_wifi_ip"));

        for (var i = 0; i < 3; i++)
        {
            var result = await RunAsync($"connect {ip}:5555", ct);
            if (result.Contains("connected"))
                return ip;
            await Task.Delay(800, ct);
        }
        throw new InvalidOperationException(string.Format(LocalizationService.Get("ex.wifi_connect_fail"), ip));
    }

    public static async Task<(int W, int H)> GetScreenSizeAsync(string serial, CancellationToken ct = default)
    {
        var output = await RunAsync($"-s {S(serial)} shell wm size", ct);
        var m = Regex.Match(output, @"(\d+)x(\d+)");
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : (0, 0);
    }

    public static async Task<int> GetBrightnessAsync(string serial, CancellationToken ct = default)
    {
        var output = await RunAsync($"-s {S(serial)} shell settings get system screen_brightness", ct);
        return int.TryParse(output.Trim(), out var v) ? v : -1;
    }

    public static async Task SetBrightnessAsync(string serial, int value, CancellationToken ct = default)
    {
        await RunAsync($"-s {S(serial)} shell settings put system screen_brightness_mode 0", ct);
        await RunAsync($"-s {S(serial)} shell settings put system screen_brightness {value}", ct);
    }

    public static async Task<int> GetStayOnWhilePluggedInAsync(string serial, CancellationToken ct = default)
    {
        var output = await RunAsync($"-s {S(serial)} shell settings get global stay_on_while_plugged_in", ct);
        return int.TryParse(output.Trim(), out var v) ? v : -1;
    }

    public static async Task SetStayOnWhilePluggedInAsync(string serial, int value, CancellationToken ct = default)
    {
        await RunAsync($"-s {S(serial)} shell settings put global stay_on_while_plugged_in {value}", ct);
    }

    public static async Task WakeScreenAsync(string serial, CancellationToken ct = default)
    {
        await RunAsync($"-s {S(serial)} shell input keyevent KEYCODE_WAKEUP", ct);
    }

    public static async Task ReverseRemoveAsync(string serial, string deviceSocket)
    {
        try { await RunAsync($"-s {S(serial)} reverse --remove localabstract:{deviceSocket}"); }
        catch { }
    }

    public static async Task<List<AndroidProfile>> ListProfilesAsync(string serial, CancellationToken ct = default)
    {
        var output = await RunAsync($"-s {S(serial)} shell pm list users", ct);
        var list = new List<AndroidProfile>();
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            var m = Regex.Match(line, @"UserInfo\{(\d+):([^:}]*):[0-9a-fA-F]+\}\s*(.*)");
            if (!m.Success || int.Parse(m.Groups[1].Value) == 0)
                continue;
            var name = m.Groups[2].Value;
            list.Add(new AndroidProfile(int.Parse(m.Groups[1].Value),
                string.IsNullOrEmpty(name) ? $"Profil {m.Groups[1].Value}" : name,
                m.Groups[3].Value.Contains("running")));
        }
        return list;
    }

    private static readonly Regex ProfileNameOk = new(@"^[\p{L}\p{N} _\-]{1,32}$", RegexOptions.Compiled);

    public static async Task<int> CreateCloneProfileAsync(string serial, string name, CancellationToken ct = default)
    {
        if (!ProfileNameOk.IsMatch(name))
            throw new ArgumentException($"nom de profil invalide : « {name} »");
        var output = await RunAsync(
            $"-s {S(serial)} shell pm create-user --profileOf 0 --user-type android.os.usertype.profile.CLONE \"{name}\"", ct);
        var m = Regex.Match(output, @"user id (\d+)");
        if (!m.Success)
            throw new InvalidOperationException(string.Format(LocalizationService.Get("ex.profile_denied"), output.Trim()));
        return int.Parse(m.Groups[1].Value);
    }

    public static async Task InstallAppForUserAsync(string serial, int userId, string packageName, CancellationToken ct = default)
    {
        var output = await RunAsync($"-s {S(serial)} shell pm install-existing --user {userId} {packageName}", ct);
        if (!output.Contains("installed for user"))
            throw new InvalidOperationException(string.Format(LocalizationService.Get("ex.install_denied"), output.Trim()));
    }

    public static async Task StartUserAsync(string serial, int userId, CancellationToken ct = default)
        => await RunAsync($"-s {S(serial)} shell am start-user {userId}", ct);

    public static async Task RemoveUserProfileAsync(string serial, int userId, CancellationToken ct = default)
    {
        var output = await RunAsync($"-s {S(serial)} shell pm remove-user {userId}", ct);
        if (!output.Contains("Success"))
            throw new InvalidOperationException(string.Format(LocalizationService.Get("ex.remove_denied"), output.Trim()));
    }

    public static Process StartServerProcess(string serial, string remoteJar, string arguments)
    {
        var adb = FindAdb() ?? throw new InvalidOperationException(LocalizationService.Get("ex.adb_missing"));
        var psi = new ProcessStartInfo(adb,
            $"-s {S(serial)} shell CLASSPATH={remoteJar} app_process / com.touchmirror.engine.Server {arguments}")
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