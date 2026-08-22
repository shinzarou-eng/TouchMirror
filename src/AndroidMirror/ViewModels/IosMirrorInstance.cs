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
                View.SetWaitingOverlay(true);
            });
        service.Exited += () =>
            View.Dispatcher.Invoke(() =>
            {
                RaiseLog("airplay: récepteur arrêté");
                IsConnected = false;
                View.SetWaitingOverlay(true);
            });
        service.Log += m => RaiseLog(m);

        return View.Dispatcher.InvokeAsync(() =>
        {
            View.AttachDecoder(service.Frames);
            View.SetIosReadOnly(true);
            View.SetWaitingOverlay(true);
        }).Task;
    }

    public override string ToggleRecording(string videoCodec)
        => "Enregistrement vidéo indisponible sur iOS — capture PNG seulement.";

    public new async Task DisconnectAsync()
    {
        _service = null;
        await base.DisconnectAsync();
    }
}
