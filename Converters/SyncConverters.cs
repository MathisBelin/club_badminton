using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BadmintonClub.Converters;

/// <summary>bool (en marche) → vert / rouge.</summary>
public sealed class RunningBrushConverter : IValueConverter
{
    private static readonly Brush On = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly Brush Off = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? On : Off;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool (en marche) → « En marche » / « Arrêté ».</summary>
public sealed class RunningTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "En marche" : "Arrêté";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility (true = Visible), utilisé pour le spinner « en cours ».</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → « ▶ Démarrer » / « ⏸ Arrêter » selon l'état en marche.</summary>
public sealed class RunningToggleTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "⏸ Arrêter" : "▶ Démarrer";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
