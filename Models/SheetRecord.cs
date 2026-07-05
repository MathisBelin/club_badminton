using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace BadmintonClub.Models;

/// <summary>
/// Un Google Sheet créé par l'application, mémorisé localement pour pouvoir
/// le retrouver, l'ouvrir ou le supprimer plus tard.
/// </summary>
public class SheetRecord : INotifyPropertyChanged
{
    public string SpreadsheetId { get; set; } = string.Empty;

    private string _nom = string.Empty;
    public string Nom
    {
        get => _nom;
        set { _nom = value; OnPropertyChanged(); }
    }

    public string Url { get; set; } = string.Empty;
    public DateTime DateCreation { get; set; } = DateTime.Now;

    private bool _isSelected;

    /// <summary>Sélection (case à cocher) pour les actions groupées. Non sérialisé.</summary>
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
