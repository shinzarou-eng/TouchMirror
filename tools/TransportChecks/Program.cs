using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using TouchMirror.Engine;
using TouchMirror.Services;
using TouchMirror.ViewModels;

internal static class Program
{
    private static int _failed;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] is not ("--preview" or "--hub")) return FakeAdb.RunAsync(args).GetAwaiter().GetResult();
        AppContext.SetData("TouchMirror.LogDirectory", Path.Combine(Path.GetTempPath(), "TouchMirrorChecks", Guid.NewGuid().ToString("N")));
        Check("control disposal is repeatable", () =>
        {
            using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            var channel = new ControlChannel(socket);
            channel.Dispose();
            channel.Dispose();
            channel.SendSimple(ControlMsgType.ResetVideo);
        });
        Check("control send/dispose race", () =>
        {
            for (var i = 0; i < 100; i++)
            {
                using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                var channel = new ControlChannel(socket);
                Parallel.Invoke(() => { for (var n = 0; n < 100; n++) channel.SendSimple(ControlMsgType.ResetVideo); },
                    channel.Dispose, channel.Dispose);
            }
        });
        Check("malformed clipboard length is ignored", () =>
        {
            using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            using var channel = new ControlChannel(socket);
            channel.ClipboardReceived += _ => throw new InvalidOperationException("invalid clipboard delivered");
            channel.Feed(new byte[] { 0x50, 0xff, 0xff, 0xff, 0xff });
        });
        Check("ADB parses tabs, unauthorized and offline without a header", () =>
        {
            var devices = (IReadOnlyList<AdbDevice>)Invoke("ParseDevices", "phone-a\tunauthorized\r\nphone-b\toffline\r\nphone-c device product:x model:Redmi_Note device:y\n")!;
            Require(devices.Count == 3 && devices[0].NeedsAuthorization && devices[1].IsOffline
                && devices[2].Model == "Redmi Note", "missing or incorrect devices");
        });
        Check("PnP rejects vendor-only peripherals", () =>
        {
            Require(Invoke("ClassifyPnpDevice", "USB\\VID_04E8&PID_0001\\test", "Samsung Printer", Array.Empty<string>()) == null,
                "printer classified as Android");
            Require(Invoke("ClassifyPnpDevice", "USB\\VID_0B05&PID_0001\\test", "USB Composite Device", Array.Empty<string>()) == null,
                "generic Asus peripheral classified as Android");
        });
        Check("PnP recognizes Redmi, ADB and MTP interfaces", () =>
        {
            Require(Invoke("ClassifyPnpDevice", "USB\\VID_2717&PID_0001\\test", "Redmi Note", Array.Empty<string>()) != null, "Redmi missing");
            Require(Invoke("ClassifyPnpDevice", "USB\\VID_04E8&PID_0001\\test", "Unknown device", new[] { "USB\\Class_ff&SubClass_42&Prot_01" }) != null, "ADB interface missing");
            Require(Invoke("ClassifyPnpDevice", "USB\\VID_04E8&PID_0001\\test", "MTP USB Device", new[] { "USB\\MS_COMP_MTP" }) != null, "MTP interface missing");
        });
        Check("QR payload follows the ADB WIFI format", () =>
        {
            using var session = new QrPairSession();
            var m = Regex.Match(session.Payload, @"^WIFI:T:ADB;S:(studio-[A-Za-z0-9]{10});P:([A-Za-z0-9]{12});;$");
            Require(m.Success, $"bad payload {session.Payload}");
            Require(m.Groups[1].Value == session.ServiceName && m.Groups[2].Value == session.Password, "fields not in payload");
        });
        Check("QR sessions produce unique credentials", () =>
        {
            using var a = new QrPairSession();
            using var b = new QrPairSession();
            Require(a.Payload != b.Payload, "identical session credentials");
        });
        Check("mdns services parsing resolves only the requested instance", () =>
        {
            var output = "List of discovered mdns services\n"
                + "adb-X1\t_adb-tls-connect._tcp\t192.168.1.5:39015\n"
                + "studio-aBc123XyZ0  _adb-tls-pairing._tcp   192.168.1.7:45821\n"
                + "studio-other0000  _adb-tls-pairing._tcp   10.0.0.2:1111\n";
            Require(AdbService.ParseMdnsServiceAddress(output, "studio-aBc123XyZ0") == "192.168.1.7:45821", "instance not resolved");
            Require(AdbService.ParseMdnsServiceAddress(output, "studio-missing000") == null, "phantom instance resolved");
        });
        Check("hub carousel peeks a constant overlap for portrait and landscape cards", () =>
        {
            var portraitX = TouchMirror.MainWindow.HubCardOffset(1, 103.0, 206.0, 0.55);
            Require(Math.Abs(portraitX - (103 + 56.65 - 56)) < 0.01, $"portrait offset {portraitX}");
            var landscapeX = TouchMirror.MainWindow.HubCardOffset(1, 103.0, 404.0, 0.55);
            var landscapeInner = landscapeX - 404.0 * 0.55 / 2;
            Require(Math.Abs(103 - landscapeInner - 56) < 0.01, $"landscape overlap not 56 (inner {landscapeInner})");
            Require(TouchMirror.MainWindow.HubCardOffset(-1, 103.0, 404.0, 0.55) == -landscapeX, "asymmetric offsets");
            Require(Math.Abs(TouchMirror.MainWindow.HubCardOffset(2, 103.0, 206.0, 0.42)) > Math.Abs(portraitX), "far card not pushed out");
            Require(TouchMirror.MainWindow.HubCardOffset(0, 103.0, 206.0, 1.0) == 0, "center not zero");
        });
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources["RadiusControl"] = new CornerRadius(7);
        app.Resources["FsMeta"] = 12d;
        app.Resources["FsTitle"] = 18d;
        app.Resources["FsBody"] = 13.5d;
        app.Resources["Ic"] = new Style(typeof(System.Windows.Shapes.Path));
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/TouchMirror;component/Themes/Icons.xaml", UriKind.Relative)
        });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        if (args is ["--preview", var repo, var output]) return DiagnosticPreview.Run(repo, output);
        if (args is ["--hub", var repoHub, var outputHub]) return DiagnosticPreview.RunHub(repoHub, outputHub);
        var frame = new DispatcherFrame();
        var checks = TransportAsync();
        checks.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false),
            TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.PushFrame(frame);
        checks.GetAwaiter().GetResult();
        Console.WriteLine($"Failures: {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static readonly AdbDevice TestPhone = new("test-phone", "Test Phone", "device");
    private static readonly EngineOptions TestOptions = new() { Audio = false, TurnScreenOff = false };

    private static void ResetFake(string mode = "hold", string state = "device")
    {
        var root = Path.Combine(Path.GetTempPath(), "TouchMirrorChecks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("TOUCHMIRROR_CHECK_ROOT", root);
        File.WriteAllText(Path.Combine(root, "mode"), mode);
        File.WriteAllText(Path.Combine(root, "state"), state);
        typeof(AdbService).GetField("_adbPath", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, Environment.ProcessPath);
    }

    private static int Starts => File.Exists(Path.Combine(FakeAdb.Root, "starts"))
        ? File.ReadAllText(Path.Combine(FakeAdb.Root, "starts")).Length : 0;

    private static Task LoseAsync(MirrorInstance mirror)
        => (Task)typeof(MirrorInstance).GetMethod("OnSessionLostAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(mirror, new object[] { mirror.Session! })!;

    private static async Task TransportAsync()
    {
        await CheckAsync("cache verifies content, not size, and isolates session JARs", async () =>
        {
            ResetFake();
            var local = Path.Combine(FakeAdb.Root, "input.jar");
            await File.WriteAllBytesAsync(local, new byte[] { 1, 2, 3, 4 });
            Require(!await AdbService.PrepareServerAsync("test-phone", local, "11111111"), "first transfer must push");
            Require(await AdbService.PrepareServerAsync("test-phone", local, "22222222"), "matching hash must reuse cache");
            var cache = Directory.GetFiles(FakeAdb.Root, "touchmirror-cache-*.jar").Single();
            await File.WriteAllBytesAsync(cache, new byte[] { 4, 3, 2, 1 });
            Require(!await AdbService.PrepareServerAsync("test-phone", local, "33333333"), "same-sized corrupt cache was reused");
            Require(File.Exists(FakeAdb.Remote("touchmirror-11111111.jar")) && File.Exists(FakeAdb.Remote("touchmirror-22222222.jar")), "session paths not isolated");
        });
        foreach (var mode in new[] { "eof", "partial", "bad-name", "stall" })
            await CheckAsync($"handshake rejects {mode} and disposes twice", async () =>
            {
                ResetFake(mode);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                var session = new EngineSession(TestPhone, TestOptions, timeout.Token);
                try
                {
                    var rejected = false;
                    try { await session.StartAsync(); }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException) { rejected = true; }
                    Require(rejected, "invalid handshake accepted");
                }
                finally
                {
                    await session.DisposeAsync();
                    await session.DisposeAsync();
                }
            });
        await CheckAsync("recovery retains identity, stops recording and clears pending input", async () =>
        {
            ResetFake();
            using var mirror = new MirrorInstance(TestPhone) { AccountUserId = 10, AccountName = "Test profile" };
            await mirror.StartAsync(TestOptions);
            mirror.IsRecording = true;
            var viewType = mirror.View.GetType();
            viewType.GetField("_pressedButtons", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(mirror.View, 1u);
            var recovered = false;
            mirror.Connected += m => recovered = m.IsReconnecting;
            await LoseAsync(mirror);
            Require(recovered && mirror.IsConnected && !mirror.IsReconnecting && !mirror.IsRecording && Starts == 2, "incorrect recovery state");
            Require(mirror.AccountUserId == 10 && mirror.Device.Serial == "test-phone", "recovery changed target");
            Require((uint)viewType.GetField("_pressedButtons", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(mirror.View)! == 0, "stale input retained");
            await mirror.DisconnectAsync();
        });
        await CheckAsync("recovery waits for the same device to return", async () =>
        {
            ResetFake();
            using var mirror = new MirrorInstance(TestPhone);
            await mirror.StartAsync(TestOptions);
            File.WriteAllText(Path.Combine(FakeAdb.Root, "state"), "missing");
            var recovery = LoseAsync(mirror);
            await Task.Delay(1800);
            Require(!mirror.IsConnected && mirror.IsReconnecting && Starts == 1, "restarted before device returned");
            File.WriteAllText(Path.Combine(FakeAdb.Root, "state"), "device");
            await recovery;
            Require(mirror.IsConnected && Starts == 2, "did not recover after device returned");
            await mirror.DisconnectAsync();
        });
        await CheckAsync("repeated short sessions share a bounded retry budget", async () =>
        {
            ResetFake();
            using var mirror = new MirrorInstance(TestPhone);
            await mirror.StartAsync(TestOptions);
            for (var i = 0; i < 4; i++) await LoseAsync(mirror);
            Require(Starts == 4 && mirror.UnexpectedDeath && !mirror.IsConnected, "short sessions reset retry budget");
        });
        await CheckAsync("manual disconnect cancels pending recovery and notifies once", async () =>
        {
            ResetFake();
            using var mirror = new MirrorInstance(TestPhone);
            var notifications = 0;
            mirror.Disconnected += _ => notifications++;
            await mirror.StartAsync(TestOptions);
            var recovery = LoseAsync(mirror);
            mirror.ManualDisconnect = true;
            await mirror.DisconnectAsync();
            await recovery;
            await mirror.DisconnectAsync();
            Require(Starts == 1 && notifications == 1 && mirror.Session == null && !mirror.IsConnected, "recovery survived manual disconnect");
        });
        await CheckAsync("disconnect cancels a stalled startup", async () =>
        {
            ResetFake("stall");
            using var mirror = new MirrorInstance(TestPhone);
            var start = mirror.StartAsync(TestOptions);
            for (var i = 0; i < 100 && !File.Exists(Path.Combine(FakeAdb.Root, "accepted")); i++) await Task.Delay(50);
            var stop = mirror.DisconnectAsync();
            try { await start; throw new InvalidOperationException("startup was not cancelled"); }
            catch (OperationCanceledException) { }
            await stop;
            Require(mirror.Session == null && !mirror.IsConnected, "mirror resurrected after stop");
        });
        await CheckAsync("unauthorized device never restarts server", async () =>
        {
            ResetFake();
            using var mirror = new MirrorInstance(TestPhone);
            await mirror.StartAsync(TestOptions);
            File.WriteAllText(Path.Combine(FakeAdb.Root, "state"), "unauthorized");
            await LoseAsync(mirror);
            Require(Starts == 1 && mirror.UnexpectedDeath && mirror.LastDeviceState == "unauthorized" && mirror.Session == null, "authorization was ignored");
        });
        await CheckAsync("three failed startup retries exhaust recovery", async () =>
        {
            ResetFake();
            using var mirror = new MirrorInstance(TestPhone);
            await mirror.StartAsync(TestOptions);
            File.WriteAllText(Path.Combine(FakeAdb.Root, "mode"), "eof");
            await LoseAsync(mirror);
            Require(Starts == 4 && mirror.UnexpectedDeath && mirror.Session == null, "retry budget incorrect");
        });
        await CheckAsync("disposed QR session cancels the wait loop", async () =>
        {
            var session = new QrPairSession();
            session.Dispose();
            var cancelled = false;
            try { await session.RunAsync((_, _) => Task.CompletedTask); }
            catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled, "session kept waiting after dispose");
        });
        await CheckAsync("QR session discovers an advertised pairing service", async () =>
        {
            ResetFake();
            using var session = new QrPairSession();
            using var advertiser = new Makaretu.Dns.ServiceDiscovery(MdnsHost.Instance);
            advertiser.Advertise(new Makaretu.Dns.ServiceProfile(session.ServiceName, "_adb-tls-pairing._tcp", 39999));
            var states = new System.Collections.Concurrent.ConcurrentQueue<QrPairState>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                await session.RunAsync((s, _) => { states.Enqueue(s); return Task.CompletedTask; }, cts.Token);
            }
            catch (OperationCanceledException) { }
            Require(states.Contains(QrPairState.Pairing) || states.Contains(QrPairState.Failed) || states.Contains(QrPairState.Done),
                $"discovery never fired (states: {string.Join(",", states)})");
        });
        await CheckAsync("Windows PnP enumeration runs with a deadline", async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var devices = await AdbService.DetectPnpAndroidAsync(timeout.Token);
            Console.WriteLine($"PnP candidates: {devices.Count}");
        });
    }

    private static async Task CheckAsync(string name, Func<Task> check)
    {
        try { await check(); Console.WriteLine($"PASS {name}"); }
        catch (Exception ex) { _failed++; Console.WriteLine($"FAIL {name}: {ex.GetBaseException().Message}"); }
    }

    private static object? Invoke(string name, params object[] args)
        => (typeof(AdbService).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing {name}")).Invoke(null, args);

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Check(string name, Action check)
    {
        try { check(); Console.WriteLine($"PASS {name}"); }
        catch (Exception ex) { _failed++; Console.WriteLine($"FAIL {name}: {ex.GetBaseException().Message}"); }
    }
}
