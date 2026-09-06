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
    public bool Featured => Entry.Featured;
    public List<string> Tags => Entry.Tags;
    public string ShortHash => Entry.Hash.Length > 16 ? Entry.Hash[..16] + "…" : Entry.Hash;

    [ObservableProperty] private string _actionLabel = "Installer";
    [ObservableProperty] private bool _canInstall = true;
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUninstall))]
    private bool _isPresent;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    private bool _isActive;
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private bool _codeBusy;
    [ObservableProperty] private bool _codeError;
    [ObservableProperty] private List<string> _capabilities = new();

    public bool CanUninstall => IsPresent;
    public string ToggleLabel => IsActive
        ? LocalizationService.Get("misc.disable")
        : LocalizationService.Get("misc.enable");

    public void Refresh(IReadOnlyList<PluginInstance> installed)
    {
        var p = installed.FirstOrDefault(x => x.Id == Id);
        IsPresent = p != null;
        IsActive = p?.Running == true;
        IsInstalled = p?.ContentHash != null
                      && string.Equals(p.ContentHash, Entry.Hash, StringComparison.OrdinalIgnoreCase);
        if (IsInstalled)
        {
            ActionLabel = LocalizationService.Get("installe");
            CanInstall = false;
            return;
        }
        if (!string.IsNullOrEmpty(Entry.MinAppVersion)
            && Version.TryParse(Entry.MinAppVersion, out var min)
            && min > UpdateService.CurrentVersion)
        {
            ActionLabel = string.Format(LocalizationService.Get("mkt.requires"), Entry.MinAppVersion);
            CanInstall = false;
            return;
        }
        CanInstall = true;
        ActionLabel = p == null ? LocalizationService.Get("mkt.install")
            : p.Version != Entry.Version ? LocalizationService.Get("mkt.update")
            : LocalizationService.Get("mkt.reconfirm");
    }
}
