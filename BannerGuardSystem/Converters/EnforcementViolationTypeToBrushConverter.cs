using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.Converters;

public class EnforcementViolationTypeToBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EnforcementViolationType type)
            return new SolidColorBrush(Color.FromRgb(254, 226, 226));
        return type switch
        {
            EnforcementViolationType.IllegalBanner => new SolidColorBrush(Color.FromRgb(254, 226, 226)),
            EnforcementViolationType.IllegalFlyer => new SolidColorBrush(Color.FromRgb(254, 243, 199)),
            _ => new SolidColorBrush(Color.FromRgb(243, 244, 246))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class EnforcementViolationTypeToForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EnforcementViolationType type)
            return new SolidColorBrush(Color.FromRgb(185, 28, 28));
        return type switch
        {
            EnforcementViolationType.IllegalBanner => new SolidColorBrush(Color.FromRgb(185, 28, 28)),
            EnforcementViolationType.IllegalFlyer => new SolidColorBrush(Color.FromRgb(180, 83, 9)),
            _ => new SolidColorBrush(Color.FromRgb(107, 114, 128))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}