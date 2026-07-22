using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BadmintonClub.Models;

namespace BadmintonClub.Converters;

/// <summary>Type d'action de l'historique → couleur (ajout vert, suppression rouge, etc.).</summary>
public sealed class ActionBrushConverter : IValueConverter
{
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0));
    private static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly Brush Grey = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            ActivityAction.Ajout => Green,
            ActivityAction.Suppression => Red,
            ActivityAction.Modification => Amber,
            ActivityAction.Association => Blue,
            ActivityAction.Dissociation => Grey,
            _ => Grey
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility (true = Visible).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility inversé (true = Collapsed, false = Visible).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
