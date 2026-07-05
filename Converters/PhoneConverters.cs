using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BadmintonClub.Helpers;

namespace BadmintonClub.Converters;

/// <summary>Affiche un numéro de téléphone au format « 06 12 34 56 78 ».</summary>
public sealed class PhoneFormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => PhoneFormatter.Format(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value ?? string.Empty;
}

/// <summary>Rouge si le numéro reste invalide malgré la correction (ignoré si vide).</summary>
public sealed class PhoneColorConverter : IValueConverter
{
    private static readonly Brush Invalid = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly Brush Normal = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => PhoneFormatter.IsValid(value as string) ? Normal : Invalid;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
