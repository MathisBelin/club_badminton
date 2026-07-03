using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BadmintonClub.Models;

/// <summary>Un libellé Gmail (groupe de contacts) affiché dans la liste.</summary>
public class LabelItem : INotifyPropertyChanged
{
    public string ResourceName { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public int NombreMembres { get; set; }

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
