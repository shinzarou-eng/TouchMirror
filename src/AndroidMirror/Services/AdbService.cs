using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TouchMirror.Services;

public sealed record AdbDevice(string Serial, string Model, string State, int? Battery = null,
    string? HardwareSerial = null, string? AltSerial = null, string? CustomName = null,
    string? Color = null, string? Diag = null, bool Pinned = false, bool IsSelected = false,
    bool IsMirrored = false)
{
    public string DeviceKey => HardwareSerial is { Length: > 0 } h ? h : Serial;
    public string MaskedSerial => Serial.Length > 7 ? Serial[..4] + "•••" + Serial[^3..] : "•••";
    public string DisplayName => CustomName ?? (string.IsNullOrWhiteSpace(Model) ? MaskedSerial : $"{Model} ({MaskedSerial})");
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
    public bool IsWifi => Serial.Contains(':') || Serial.Contains("._tcp");
    public bool HasDualTransport => AltSerial != null;
    public string TransportText => IsRememberedOnly ? L("dev.memorized")
        : HasDualTransport
            ? (IsWifi ? $"USB + WiFi — {Serial}" : "USB + WiFi")
            : IsWifi ? $"WiFi — {Serial}" : "USB";
    public bool ShowSerial => !IsWifi && !IsRememberedOnly;
    public string LiveText => IsMirrored ? L("en_direct")
        : IsReady ? L("dev.live") : IsRememberedOnly ? L("dev.memorized") : "";
    public string SidebarMeta => IsRememberedOnly ? ""
        : $" · {(HasDualTransport ? "usb+wifi" : IsWifi ? "wifi" : "usb")}";
    public int Chassis
    {
        get
        {
            var m = (Model ?? "").Replace(" ", "").Replace("-", "").ToUpperInvariant();
            if (m.StartsWith("SMX") || m.StartsWith("SMT") || m.StartsWith("SMP") || m.Contains("TAB") || m.Contains("IPAD") || m.Contains("MATEPAD")) return 2;
            if (m.Contains("IPHONE")) return 6;
            if (m.Contains("PIXEL") || m.StartsWith("GP")) return 3;
            if (m.Contains("REDMI") || m.Contains("POCO") || m.Contains("XIAOMI") || m.StartsWith("MI")
                || System.Text.RegularExpressions.Regex.IsMatch(m, @"^M[12]\d{3}")) return 4;
            if (m.Contains("ONEPLUS") || m.Contains("OPPO") || m.Contains("REALME") || m.Contains("VIVO")
                || m.Contains("HONOR") || m.Contains("HUAWEI") || m.Contains("NOTHING") || m.StartsWith("CPH")
                || m.StartsWith("RMX") || m.StartsWith("LE") || m.StartsWith("NE") || m.StartsWith("KB")
                || m.StartsWith("XQ")) return 5;
            if (m.StartsWith("SM") || m.StartsWith("GT")) return 1;
            return 0;
        }
    }
    public string? SelectorHint => State switch
    {
        "unauthorized" => L("dev.hint_unauth"),
        "offline" => L("dev.hint_offline"),
        "remembered" => L("dev.hint_remembered"),
        _ => null
    };

    public string PinMenuText => L(Pinned ? "desepingler_cet_appareil" : "epingler_cet_appareil");

    public bool MatchesSerial(string s) => Serial == s || AltSerial == s;

    public bool SharesIdentity(AdbDevice o)
        => (HardwareSerial is { Length: > 0 } && HardwareSerial == o.HardwareSerial)
           || MatchesSerial(o.Serial) || (o.AltSerial != null && MatchesSerial(o.AltSerial));

    public AdbDevice Preferring(string serial)
        => Serial == serial ? this
            : AltSerial == serial ? this with { Serial = serial, AltSerial = Serial }
            : this;
}

public sealed record AndroidProfile(int Id, string Name, bool Running, bool Owned = false);

public static class AdbService
{
    private static readonly Regex SerialOk = new(@"^[A-Za-z0-9._:\-]+$", RegexOptions.Compiled);
    private static readonly Regex PackageOk = new(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$", RegexOptions.Compiled);

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

    public static Task<string> RunTextAsync(string args, CancellationToken ct = default)
        => RunAsync(args, ct);

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
            var stdout = p.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = p.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr, p.WaitForExitAsync(timeout.Token));
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"adb {args} -> {p.ExitCode}: {stderr.Result.Trim()}");
            return stdout.Result;
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(); } catch { }
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException(string.Format(LocalizationService.Get("ex.adb_timeout"), args));
        }
    }

    public static async Task<byte[]?> ScreencapAsync(string serial, CancellationToken ct = default)
    {
        var adb = FindAdb();
        if (adb == null)
            return null;
        var psi = new ProcessStartInfo(adb, $"-s {S(serial)} exec-out screencap -p")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var ms = new MemoryStream();
            var copy = p.StandardOutput.BaseStream.CopyToAsync(ms, timeout.Token);
            await p.WaitForExitAsync(timeout.Token);
            await copy;
            return p.ExitCode == 0 && ms.Length > 100 ? ms.ToArray() : null;
        }
        catch
        {
            try { p.Kill(); } catch { }
            return null;
        }
    }

    public static async Task<double> MeasureAdbMbpsAsync(string serial, int mb, CancellationToken ct = default)
    {
        var adb = FindAdb();
        if (adb == null)
            return -1;
        var psi = new ProcessStartInfo(adb, $"-s {S(serial)} exec-out dd if=/dev/zero bs=1M count={mb}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long total = 0;
            var buf = new byte[256 * 1024];
            var stream = p.StandardOutput.BaseStream;
            int n;
            while ((n = await stream.ReadAsync(buf, timeout.Token)) > 0)
                total += n;
            sw.Stop();
            await p.WaitForExitAsync(timeout.Token);
            return sw.Elapsed.TotalSeconds > 0.1 ? total * 8.0 / 1e6 / sw.Elapsed.TotalSeconds : -1;
        }
        catch
        {
            try { p.Kill(); } catch { }
            return -1;
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
                var stderr = p.StandardError.ReadToEndAsync(ct);
                try
                {
                    while (!p.HasExited && !ct.IsCancellationRequested)
                    {
                        var n = await p.StandardOutput.ReadAsync(buf, ct);
                        if (n == 0)
                            break;
                        onChanged();
                    }
                }
                finally
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                    try { await stderr; } catch (OperationCanceledException) { }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch { }
            try { await Task.Delay(2000, ct); } catch { }
        }
    }

    private static List<AdbDevice> ParseDevices(string output)
    {
        var devices = new List<AdbDevice>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !SerialOk.IsMatch(parts[0])
                || parts[1] is not ("device" or "unauthorized" or "offline" or "recovery" or "sideload" or "bootloader" or "no"))
                continue;
            var modelMatch = Regex.Match(line, @"model:([^\s]+)");
            var model = modelMatch.Success ? modelMatch.Groups[1].Value.Replace('_', ' ') : "";
            devices.Add(new AdbDevice(parts[0], model, parts[1]));
        }
        return devices;
    }

    public static async Task<string> GetDeviceStateAsync(string serial, CancellationToken ct = default)
        => ParseDevices(await RunAsync("devices -l", ct)).FirstOrDefault(d => d.Serial == serial)?.State ?? "missing";

    public static async Task<IReadOnlyList<AdbDevice>> GetDevicesAsync(CancellationToken ct = default)
    {
        var devices = ParseDevices(await RunAsync("devices -l", ct));
        foreach (var d in devices)
            AdbDiagnostics.Record(d.Serial, d.State);
        devices = devices.Select(d => d with { Diag = AdbDiagnostics.Verdict(d.Serial, d.State) }).ToList();

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
            return SerialOk.IsMatch(s) && !s.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                && !s.Equals("null", StringComparison.OrdinalIgnoreCase) && s.Any(c => c != '0') ? s : null;
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

    internal static string Q(string path)
    {
        if (path.Any(c => c is '"' or '\r' or '\n'))
            throw new ArgumentException($"chemin invalide : « {path} »");
        return $"\"{path}\"";
    }

    public static async Task PushAsync(string serial, string localPath, string remotePath, CancellationToken ct = default)
        => await RunAsync($"-s {S(serial)} push {Q(localPath)} {Q(remotePath)}", ct);

    public static async Task<string> InstallApkAsync(string serial, string localPath, CancellationToken ct = default)
        => await RunAsync($"-s {S(serial)} install -r {Q(localPath)}", ct);

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

    public static async Task<int> GetScreenDensityAsync(string serial, CancellationToken ct = default)
    {
        var output = await RunAsync($"-s {S(serial)} shell wm density", ct);
        var m = Regex.Match(output, @"(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
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

    public static async Task<int> GetBrightnessModeAsync(string serial, CancellationToken ct = default)
    {
        var output = await RunAsync($"-s {S(serial)} shell settings get system screen_brightness_mode", ct);
        return int.TryParse(output.Trim(), out var v) ? v : -1;
    }

    public static async Task SetBrightnessModeAsync(string serial, int value, CancellationToken ct = default)
    {
        await RunAsync($"-s {S(serial)} shell settings put system screen_brightness_mode {value}", ct);
    }

    public static async Task<AdbDevice?> ResolveAsync(AdbDevice device, CancellationToken ct = default)
    {
        try
        {
            var list = await GetDevicesAsync(ct);
            return list.FirstOrDefault(d => d.SharesIdentity(device));
        }
        catch { return null; }
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await RunAsync($"-s {S(serial)} reverse --remove localabstract:{deviceSocket}", timeout.Token); }
        catch { }
    }

    public static async Task<List<AndroidProfile>> ListProfilesAsync(string serial, CancellationToken ct = default)
    {
        var output = await RunAsync($"-s {S(serial)} shell pm list users", ct);
        var types = new Dictionary<int, string>();
        try
        {
            var dump = await RunAsync($"-s {S(serial)} shell dumpsys user", ct);
            var lastId = -1;
            foreach (var line in dump.Split('\n'))
            {
                var um = Regex.Match(line, @"UserInfo\{(\d+):");
                if (um.Success)
                {
                    lastId = int.Parse(um.Groups[1].Value);
                    continue;
                }
                var tm = Regex.Match(line, @"Type:\s*(\S+)");
                if (tm.Success && lastId >= 0)
                    types[lastId] = tm.Groups[1].Value;
            }
        }
        catch { }
        var list = new List<AndroidProfile>();
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            var m = Regex.Match(line, @"UserInfo\{(\d+):([^:}]*):[0-9a-fA-F]+\}\s*(.*)");
            if (!m.Success || int.Parse(m.Groups[1].Value) == 0)
                continue;
            var id = int.Parse(m.Groups[1].Value);
            if (types.Count > 0 && types.TryGetValue(id, out var t)
                && !t.EndsWith("profile.CLONE", StringComparison.Ordinal))
                continue;
            var name = m.Groups[2].Value;
            list.Add(new AndroidProfile(id,
                string.IsNullOrEmpty(name) ? $"Profil {id}" : name,
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
        if (!PackageOk.IsMatch(packageName))
            throw new ArgumentException($"package invalide : « {packageName} »");
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

    public static async Task<bool> PrepareServerAsync(string serial, string localPath, string sessionId, CancellationToken ct = default)
    {
        if (!Regex.IsMatch(sessionId, @"\A[0-9a-f]{8}\z"))
            throw new ArgumentException("Invalid session ID", nameof(sessionId));
        using var file = File.OpenRead(localPath);
        var hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(file, ct)).ToLowerInvariant();
        var cachePath = $"/data/local/tmp/touchmirror-cache-{hash}.jar";
        var sessionPath = $"/data/local/tmp/touchmirror-{sessionId}.jar";
        try
        {
            var output = await RunAsync($"-s {S(serial)} shell sha256sum {cachePath}", ct);
            var remoteHash = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.Equals(remoteHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                await RunAsync($"-s {S(serial)} shell cp {cachePath} {sessionPath}", ct);
                return true;
            }
        }
        catch (InvalidOperationException) { }
        ct.ThrowIfCancellationRequested();
        await PushAsync(serial, localPath, sessionPath, ct);
        try { await RunAsync($"-s {S(serial)} shell cp {sessionPath} {cachePath}", ct); }
        catch (InvalidOperationException) { }
        return false;
    }

    private static readonly Regex HostPortOk = new(@"^[A-Za-z0-9.\-_]{1,253}:\d{1,5}$", RegexOptions.Compiled);

    public static string? ParseMdnsServiceAddress(string output, string instanceName)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = Regex.Match(line, @"^(\S+)\s+_adb-tls-pairing\._tcp\s+(\d+\.\d+\.\d+\.\d+):(\d+)");
            if (m.Success && m.Groups[1].Value == instanceName)
                return $"{m.Groups[2].Value}:{m.Groups[3].Value}";
        }
        return null;
    }

    public static async Task<string?> FindMdnsServiceAddressAsync(string instanceName, CancellationToken ct = default)
    {
        try { return ParseMdnsServiceAddress(await RunAsync("mdns services", ct), instanceName); }
        catch { return null; }
    }

    public static async Task<string> PairAsync(string hostPort, string code, CancellationToken ct = default)
    {
        if (!HostPortOk.IsMatch(hostPort))
            throw new ArgumentException($"adresse invalide : « {hostPort} »");
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64 || code.Any(char.IsWhiteSpace))
            throw new ArgumentException("code invalide");
        string output;
        try { output = await RunAsync($"pair {hostPort} {code}", ct); }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(ex.Message.Replace(code, "•••"));
        }
        if (!output.Contains("Successfully paired", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(output.Trim());
        return output.Trim();
    }

    public static async Task<string> ConnectAsync(string hostPort, CancellationToken ct = default)
    {
        if (!HostPortOk.IsMatch(hostPort))
            throw new ArgumentException($"adresse invalide : « {hostPort} »");
        var output = await RunAsync($"connect {hostPort}", ct);
        if (!output.Contains("connected to", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(output.Trim());
        return output.Trim();
    }

    public static async Task<string> ProbeAsync(string args, CancellationToken ct = default)
    {
        try { return (await RunAsync(args, ct)).Trim(); }
        catch (Exception ex) { return ex.Message; }
    }

    public sealed record ForeignAdbProcess(string Name, string? Path, int Pid = 0);

    private static readonly string[] CompetingProcessNames =
    {
        "adb", "scrcpy", "sndcpy", "QtScrcpy", "QtScrcpyCore", "escrcpy", "guiscrcpy",
        "walky", "Walky Mirroring", "gnirehtet", "studio64", "dnplayer", "Nox", "MuMuPlayer", "HD-Player"
    };

    public static List<ForeignAdbProcess> FindCompetingProcesses()
    {
        var own = FindAdb();
        var ownDir = own != null ? Path.GetDirectoryName(own)!.TrimEnd('\\') : null;
        var found = new List<ForeignAdbProcess>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in CompetingProcessNames)
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch { continue; }
            foreach (var p in procs)
            {
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }
                var pid = p.Id;
                var isOwnAdb = name == "adb" && path != null && ownDir != null
                    && Path.GetDirectoryName(path)!.TrimEnd('\\').Equals(ownDir, StringComparison.OrdinalIgnoreCase);
                try { p.Dispose(); } catch { }
                if (isOwnAdb)
                    continue;
                if (seen.Add(path ?? name))
                    found.Add(new ForeignAdbProcess(name, path, pid));
            }
        }
        return found;
    }

    public sealed record PnpAndroidDevice(string Name, string Vid, string? Brand, string? UsbSerial = null);

    private static readonly Dictionary<string, string> AndroidVendorVids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["2717"] = "Xiaomi", ["2A70"] = "OnePlus", ["22D9"] = "Oppo", ["04E8"] = "Samsung",
        ["18D1"] = "Google", ["0BB4"] = "HTC", ["12D1"] = "Huawei", ["0FCE"] = "Sony",
        ["19D2"] = "ZTE", ["2D95"] = "Vivo", ["22B8"] = "Motorola", ["1004"] = "LG",
        ["0B05"] = "Asus", ["1BBB"] = "TCL", ["2A45"] = "Meizu", ["0E79"] = "Archos",
        ["271D"] = "Nothing", ["0E8D"] = "MediaTek"
    };

    private static readonly Regex UsbVid = new(@"VID_([0-9A-Fa-f]{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AndroidName = new(@"android|\badb\b|redmi|poco|xiaomi|oneplus|galaxy|pixel|\bSM-[A-Z0-9]+|oppo|realme",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static PnpAndroidDevice? ClassifyPnpDevice(string id, string name, IEnumerable<string> compatibleIds)
    {
        var m = UsbVid.Match(id);
        if (!m.Success)
            return null;
        var vid = m.Groups[1].Value.ToUpperInvariant();
        AndroidVendorVids.TryGetValue(vid, out var brand);
        var ids = string.Join(" ", compatibleIds);
        var adb = ids.Contains("Class_ff&SubClass_42&Prot_01", StringComparison.OrdinalIgnoreCase);
        var mtp = ids.Contains("MS_COMP_MTP", StringComparison.OrdinalIgnoreCase)
            || name.Contains("MTP", StringComparison.OrdinalIgnoreCase);
        var parts = id.Split('\\', '#');
        var vi = Array.FindIndex(parts, s => s.Contains("VID_", StringComparison.OrdinalIgnoreCase));
        var seg = vi >= 0 && vi + 1 < parts.Length ? parts[vi + 1] : "";
        var serial = seg.Length >= 6 && SerialOk.IsMatch(seg) ? seg : null;
        return AndroidName.IsMatch(name) || adb || (brand != null && mtp)
            ? new PnpAndroidDevice(name, vid, brand, serial) : null;
    }

    public static async Task<List<PnpAndroidDevice>> DetectPnpAndroidAsync(CancellationToken ct = default)
    {
        var infos = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
            "System.Devices.Present:=System.StructuredQueryType.Boolean#True",
            new[] { "System.Devices.CompatibleIds", "System.Devices.ContainerId" },
            Windows.Devices.Enumeration.DeviceInformationKind.Device).AsTask(ct);
        var found = new Dictionary<string, PnpAndroidDevice>();
        foreach (var info in infos)
        {
            var ids = info.Properties.TryGetValue("System.Devices.CompatibleIds", out var value)
                ? value as string[] ?? Array.Empty<string>() : Array.Empty<string>();
            var device = ClassifyPnpDevice(info.Id, info.Name, ids);
            if (device == null)
                continue;
            var key = info.Properties.TryGetValue("System.Devices.ContainerId", out var container)
                && container is Guid guid && guid != Guid.Empty ? guid.ToString() : info.Id;
            if (!found.TryGetValue(key, out var previous) || !AndroidName.IsMatch(previous.Name))
                found[key] = device;
        }
        return found.Values.ToList();
    }
}
