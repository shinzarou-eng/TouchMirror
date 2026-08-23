using TouchMirror.Services;
using TouchMirror.Views;

namespace TouchMirror.ViewModels;

/// <summary>
/// Tuile iOS : miroir AirPlay en affichage seul. Réutilise la plomberie
/// MirrorInstance (collection, ordre, workspaces, nom, accent) sans
/// ScrcpySession ni ControlChannel — l'iPhone n'est pas contrôlable.
/// </summary>
public sealed class IosMirrorInstance : MirrorInstance
{
    private AirPlayService? _service;
    private Video.AirPlayAudioPlayer? _audio;

    public bool IsIos => true;

    private static AdbDevice IosDevice => new(
        Serial: "ios:airplay",
        Model: "iPhone",
        State: "device",
        HardwareSerial: "ios-airplay");

    public IosMirrorInstance() : base(IosDevice)
    {
        DeviceName = "iPhone (AirPlay)";
    }

    public Task StartAsync(AirPlayService service)
    {
        _service = service;

        service.DeviceConnected += (name, id) =>
            View.Dispatcher.Invoke(() =>
            {
                View.SetWaitingOverlay(false);
                IsConnected = true;
                DeviceName = string.IsNullOrWhiteSpace(name) ? "iPhone (AirPlay)" : name;
                RaiseConnected();
            });
        service.DeviceDisconnected += (name, id) =>
            View.Dispatcher.Invoke(() =>
            {
                IsConnected = false;
                DeviceName = "iPhone (AirPlay)";
                _audio?.Dispose();
                _audio = null;
                View.SetWaitingOverlay(true);
            });
        service.AudioFrame += (rate, ch, bits, data, len) =>
        {
            _audio ??= CreateAudio();
            _audio?.Feed(rate, ch, bits, data, len);
        };
        service.Exited += () =>
            View.Dispatcher.Invoke(() =>
            {
                RaiseLog("airplay: récepteur arrêté");
                IsConnected = false;
                View.SetWaitingOverlay(true);
            });
        service.Log += m => RaiseLog(m);

        // L'iPhone peut s'être connecté avant la souscription — on resynchronise.
        var already = service.ConnectedDeviceName;
        return View.Dispatcher.InvokeAsync(() =>
        {
            View.AttachDecoder(service.Frames);
            View.SetIosReadOnly(true);
            if (already != null)
            {
                IsConnected = true;
                DeviceName = string.IsNullOrWhiteSpace(already) ? "iPhone (AirPlay)" : already;
            }
            View.SetWaitingOverlay(already == null);
        }).Task;
    }

    private Video.AirPlayAudioPlayer CreateAudio()
    {
        var a = new Video.AirPlayAudioPlayer();
        a.Error += m => RaiseLog($"audio: {m}");
        return a;
    }

    public override void SetAudioMuted(bool muted)
    {
        try { if (_audio != null) _audio.Volume = muted ? 0f : 1f; } catch { }
    }

    public override string ToggleRecording(string videoCodec)
        => "Enregistrement vidéo indisponible sur iOS — capture PNG seulement.";

    public new async Task DisconnectAsync()
    {
        _service = null;
        _audio?.Dispose();
        _audio = null;
        await base.DisconnectAsync();
    }
}
