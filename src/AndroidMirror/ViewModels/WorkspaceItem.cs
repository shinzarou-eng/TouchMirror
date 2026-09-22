using CommunityToolkit.Mvvm.ComponentModel;
using TouchMirror.Services;

namespace TouchMirror.ViewModels;

public partial class WorkspaceItem : ObservableObject
{
    public Workspace Model { get; }
    public string Id => Model.Id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Initial))]
    private string _name;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private int _deviceCount;

    public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";

    public WorkspaceItem(Workspace model)
    {
        Model = model;
        _name = model.Name;
        _deviceCount = model.Devices.Count;
    }

    public void Refresh() => DeviceCount = Model.Devices.Count;
}

public sealed record MissingDeviceItem(WorkspaceDevice Prefs, string Name, string? ColorHex);

public sealed record MirrorAccount(int UserId, string Name);
