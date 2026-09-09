using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Markup;

namespace TouchMirror.Services;

/// <summary>
/// Localisation : charge lang/&lt;code&gt;.json (clés plates → texte) avec
/// repli sur le français. Chaque clé se lit via l'indexeur — les bindings
/// XAML ({loc:Loc cle}) se rafraîchissent au changement de langue.
/// Les fichiers sont de simples JSON : la communauté peut ajouter une langue
/// en déposant lang/es.json etc. sans recompiler.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Langues proposées (code → étiquette affichée dans le sélecteur).</summary>
    public static readonly (string Code, string Label)[] Languages =
        [("fr", "Français"), ("en", "English")];

    private Dictionary<string, string> _strings = new();

    /// <summary>Code langue actif ("fr" par défaut).</summary>
    public string Current { get; private set; } = "fr";

    /// <summary>Texte d'une clé ; repli sur le français, puis la clé brute.</summary>
    public string this[string key] =>
        _strings.TryGetValue(key, out var v) ? v
        : Fr.TryGetValue(key, out var f) ? f
        : key;

    /// <summary>Accès C# (statuts, logs, dialogues).</summary>
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

    /// <summary>Charge la langue et notifie tous les bindings indexés.</summary>
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

/// <summary>Extension XAML : <c>Text="{loc:Loc ma.cle}"</c> → texte localisé, suivi du changement de langue.</summary>
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
