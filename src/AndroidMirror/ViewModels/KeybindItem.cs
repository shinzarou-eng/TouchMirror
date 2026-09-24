using CommunityToolkit.Mvvm.ComponentModel;

namespace TouchMirror.ViewModels;

public sealed partial class KeybindItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private string _key = "";

    [ObservableProperty] private double _rx;
    [ObservableProperty] private double _ry;

    [ObservableProperty] private bool _isEditing;

    public string Label
    {
        get
        {
            if (string.IsNullOrEmpty(Key))
                return "…";
            if (Key.Length == 2 && Key[0] == 'D' && char.IsDigit(Key[1]))
                return Key[1].ToString();
            if (Key.StartsWith("NumPad"))
                return "Num" + Key[6..];
            return Key;
        }
    }
}
