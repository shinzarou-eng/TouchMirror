using CommunityToolkit.Mvvm.ComponentModel;
using TouchMirror.Services;

namespace TouchMirror.ViewModels;

/// <summary>Enveloppe observable d'un espace de travail pour l'UI (nom, édition, état).</summary>
public partial class WorkspaceItem : ObservableObject
{
    public Workspace Model { get; }
    public string Id => Model.Id;

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private int _deviceCount;

    public WorkspaceItem(Workspace model)
    {
        Model = model;
        _name = model.Name;
        _deviceCount = model.Devices.Count;
    }

    public void Refresh() => DeviceCount = Model.Devices.Count;
}

/// <summary>Membre d'un espace non connecté — affiché en tuile fantôme.</summary>
public sealed record MissingDeviceItem(WorkspaceDevice Prefs, string Name, string? ColorHex);
