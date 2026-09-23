using System.ComponentModel;
using TouchMirror.Services;

namespace TouchMirror.ViewModels;

public static class AvatarPalette
{
    private static readonly string[] Colors =
        { "#4EC98E", "#0078D4", "#9B7BD4", "#E8A33D", "#E06B9B", "#E05B4C", "#4EC9C0" };

    public static string For(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return Colors[6];
        var h = 0;
        foreach (var c in name)
            h = h * 31 + c;
        return Colors[(h & 0x7FFFFFFF) % Colors.Length];
    }

    public static string Initial(string? name)
        => string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[0].ToString().ToUpperInvariant();
}

public sealed class AccountItem : INotifyPropertyChanged
{
    public AccountItem(AdbDevice device, AndroidProfile? profile)
    {
        Device = device;
        Profile = profile;
        IsPrimary = profile == null;
        _name = profile?.Name ?? L("acct.primary");
        _editName = _name;
        Initial = AvatarPalette.Initial(_name);
        AvatarHex = IsPrimary ? "#C9D1D9" : AvatarPalette.For(_name);
        var t = profile?.Type ?? "";
        if (IsPrimary)
        {
            TypeTag = L("acct.user0");
            TagFgHex = "#8B95A1";
            TagBgHex = "#1AC9D1D9";
        }
        else if (t.EndsWith("profile.CLONE", StringComparison.Ordinal))
        {
            TypeTag = L("acct.clone");
            TagFgHex = "#4EC9C0";
            TagBgHex = "#214EC9C0";
        }
        else if (t.EndsWith("profile.MANAGED", StringComparison.Ordinal))
        {
            TypeTag = L("acct.managed");
            TagFgHex = "#9B7BD4";
            TagBgHex = "#249B7BD4";
        }
        else
        {
            TypeTag = "";
            TagFgHex = "#8B95A1";
            TagBgHex = "#1AC9D1D9";
        }
        HasTypeTag = TypeTag.Length > 0;
    }

    public AdbDevice Device { get; }
    public AndroidProfile? Profile { get; }
    public bool IsPrimary { get; }

    private string _name;
    public string Name
    {
        get => _name;
        private set
        {
            if (_name == value)
                return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }
    public void SetDisplayName(string name) => Name = name;

    private bool _isRenaming;
    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value)
                return;
            _isRenaming = value;
            if (value)
                EditName = Name;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRenaming)));
        }
    }

    private string _editName;
    public string EditName
    {
        get => _editName;
        set
        {
            if (_editName == value)
                return;
            _editName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditName)));
        }
    }

    private string? _playText;
    public string? PlayText
    {
        get => _playText;
        set
        {
            if (_playText == value)
                return;
            _playText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPlayText)));
        }
    }
    public bool HasPlayText => PlayText != null;

    public string Initial { get; }
    public string AvatarHex { get; }

    private string? _avatarKey;
    public string? AvatarKey
    {
        get => _avatarKey;
        set
        {
            if (_avatarKey == value)
                return;
            _avatarKey = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarKey)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAvatar)));
        }
    }
    public string? AvatarPath => AvatarKey is { } k
        ? System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "breeds", k + ".png")
        : null;
    public bool HasAvatar => AvatarKey != null;

    public string TypeTag { get; }
    public bool HasTypeTag { get; }
    public string TagFgHex { get; }
    public string TagBgHex { get; }
    public bool Running => Profile?.Running ?? false;
    public string StatusText => Running || IsPrimary ? L("acct.running") : L("acct.stopped");
    public string DotHex => Running || IsPrimary ? "#5BD98B" : "#4A5560";
    public string RingHex => Running || IsPrimary ? "#8C5BD98B" : "#55414B57";
    public bool HasStatusDot => Running || IsPrimary;
    public bool CanDelete => Profile?.Owned == true;
    public string ConfirmTitle => string.Format(L("acct.confirm"), Name);

    private bool _isConfirming;
    public bool IsConfirming
    {
        get => _isConfirming;
        set
        {
            if (_isConfirming == value)
                return;
            _isConfirming = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConfirming)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static string L(string key) => LocalizationService.Get(key);
}

public sealed class BreedAvatar : INotifyPropertyChanged
{
    public required string Key { get; init; }
    public required string Path { get; init; }
    public required string Name { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
