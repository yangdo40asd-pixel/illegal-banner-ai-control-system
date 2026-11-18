using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BannerGuardSystem.Converters;

public class AiConfidenceToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double confidence && value is not int) 
            return new SolidColorBrush(Color.FromRgb(34, 197, 94));

        double score = value is double d ? d : System.Convert.ToDouble(value);
        if (score >= 80) return new SolidColorBrush(Color.FromRgb(34, 197, 94));
        if (score >= 50) return new SolidColorBrush(Color.FromRgb(249, 115, 22));
        return new SolidColorBrush(Color.FromRgb(239, 68, 68));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}