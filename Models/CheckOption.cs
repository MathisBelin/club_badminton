using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BadmintonClub.Models;

/// <summary>Option cochable générique pour le multiselect (texte affiché + donnée associée).</summary>
public class CheckOption : INotifyPropertyChanged
{
    public string Text { get; set; } = string.Empty;
    public object? Tag { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
