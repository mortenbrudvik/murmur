using System.Globalization;
using System.Windows.Data;

namespace Murmur.UI.Pill;

/// <summary>Turns a waveform bar level (0.2 to 1.0) into its height in DIPs: <c>level * MaxHeight</c>.</summary>
[ValueConversion(typeof(double), typeof(double))]
public sealed class BarHeightConverter : IValueConverter
{
    /// <summary>Height of a full bar, and of the waveform area that holds the bars.</summary>
    public const double MaxHeight = 16;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double level ? level * MaxHeight : 0d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
