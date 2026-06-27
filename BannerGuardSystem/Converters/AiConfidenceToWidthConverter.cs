using System.Globalization;
using System.Windows.Data;

namespace BannerGuardSystem.Converters;

public class AiConfidenceToWidthConverter : IValueConverter
{
    private const double MaxWidth = 80;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double score && value is not int)
            return 0.0;

        double confidence = value is double d ? d : System.Convert.ToDouble(value);
        return Math.Clamp(confidence / 100.0 * MaxWidth, 0, MaxWidth);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}