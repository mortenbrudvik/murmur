using System.Globalization;
using System.Windows.Data;

namespace Murmur.UI.Splash;

/// <summary>The splash's clock to the opacity of its status line and version (<see cref="SplashTimeline.StatusOpacity"/>).</summary>
[ValueConversion(typeof(double), typeof(double))]
internal sealed class StatusOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double time ? SplashTimeline.StatusOpacity(time) : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
