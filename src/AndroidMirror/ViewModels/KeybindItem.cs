using CommunityToolkit.Mvvm.ComponentModel;

namespace TouchMirror.ViewModels;

/// <summary>
/// Raccourci clavier plaqué sur la vidéo d'un miroir : appuyer sur la touche
/// envoie un tap à la position (Rx,Ry) — contrôle manuel, 1 touche = 1 tap.
/// </summary>
public sealed partial class KeybindItem : ObservableObject
{
    /// <summary>Nom de la touche WPF (« A », « D1 », « Space », « F1 »…).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private string _key = "";

    /// <summary>Position relative 0..1 dans l'image vidéo (repère appareil).</summary>
    [ObservableProperty] private double _rx;
    [ObservableProperty] private double _ry;

    /// <summary>En attente d'assignation de touche (mode édition).</summary>
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
