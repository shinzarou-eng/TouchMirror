using CommunityToolkit.Mvvm.ComponentModel;
using TouchMirror.Services;

namespace TouchMirror.ViewModels;

public partial class MarketplaceItem : ObservableObject
{
    public required MarketplaceEntry Entry { get; init; }

    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public string Icon => string.IsNullOrEmpty(Entry.Icon) ? "🧩" : Entry.Icon;
    public string Description => Entry.Description;
    public string Meta => $"v{Entry.Version} · {Entry.Author}";
    public bool Official => Entry.Official;

    [ObservableProperty] private string _actionLabel = "Installer";
    [ObservableProperty] private bool _canInstall = true;
    [ObservableProperty] private bool _isInstalled;

    public void Refresh(IReadOnlyList<PluginInstance> installed)
    {
        var p = installed.FirstOrDefault(x => x.Id == Id);
        IsInstalled = p?.ContentHash != null
                      && string.Equals(p.ContentHash, Entry.Hash, StringComparison.OrdinalIgnoreCase);
        if (IsInstalled)
        {
            ActionLabel = "Installé";
            CanInstall = false;
            return;
        }
        if (!string.IsNullOrEmpty(Entry.MinAppVersion)
            && Version.TryParse(Entry.MinAppVersion, out var min)
            && min > UpdateService.CurrentVersion)
        {
            ActionLabel = $"Requiert v{Entry.MinAppVersion}";
            CanInstall = false;
            return;
        }
        CanInstall = true;
        ActionLabel = p == null ? "Installer"
            : p.Version != Entry.Version ? "Mettre à jour"
            : "Remplacer";
    }
}
