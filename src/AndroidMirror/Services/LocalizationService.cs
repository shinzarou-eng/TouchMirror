using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Markup;

namespace TouchMirror.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public static readonly (string Code, string Label)[] Languages =
        [("fr", "Français"), ("en", "English")];

    private Dictionary<string, string> _strings = new();

    public string Current { get; private set; } = "fr";

    public string this[string key] =>
        _strings.TryGetValue(key, out var v) ? v
        : Fr.TryGetValue(key, out var f) ? f
        : key;

    public static string Get(string key) => Instance[key];

    private static Dictionary<string, string> _fr = new();
    private static Dictionary<string, string> Fr
    {
        get
        {
            if (_fr.Count == 0)
                _fr = ReadLangFile("fr") ?? new();
            return _fr;
        }
    }

    public void Load(string code)
    {
        Current = code;
        _strings = code == "fr" ? new() : (ReadLangFile(code) ?? new());
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
    }

    private static Dictionary<string, string>? ReadLangFile(string code)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "lang", $"{code}.json");
            if (!File.Exists(path))
                path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "lang", $"{code}.json");
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        }
        catch { return null; }
    }
}

[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string key) => Key = key;
    public string Key { get; }

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new System.Windows.Data.Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = System.Windows.Data.BindingMode.OneWay
        }.ProvideValue(serviceProvider);
}
