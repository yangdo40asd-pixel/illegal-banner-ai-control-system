using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.Converters;

public class EnforcementStatusToBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EnforcementProcessingStatus status)
            return new SolidColorBrush(Color.FromRgb(243, 244, 246));
        return status switch
        {
            EnforcementProcessingStatus.FineImposed => new SolidColorBrush(Color.FromRgb(220, 252, 231)),
            EnforcementProcessingStatus.Appeal => new SolidColorBrush(Color.FromRgb(219, 234, 254)),
            EnforcementProcessingStatus.Received => new SolidColorBrush(Color.FromRgb(219, 234, 254)),
            EnforcementProcessingStatus.Delinquent => new SolidColorBrush(Color.FromRgb(254, 243, 199)),
            EnforcementProcessingStatus.Urgent => new SolidColorBrush(Color.FromRgb(254, 226, 226)),
            _ => new SolidColorBrush(Color.FromRgb(243, 244, 246))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class EnforcementStatusToForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EnforcementProcessingStatus status)
            return new SolidColorBrush(Color.FromRgb(107, 114, 128));
        return status switch
        {
            EnforcementProcessingStatus.FineImposed => new SolidColorBrush(Color.FromRgb(22, 163, 74)),
            EnforcementProcessingStatus.Appeal => new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            EnforcementProcessingStatus.Received => new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            EnforcementProcessingStatus.Delinquent => new SolidColorBrush(Color.FromRgb(217, 119, 6)),
            EnforcementProcessingStatus.Urgent => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
            _ => new SolidColorBrush(Color.FromRgb(107, 114, 128))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class EnforcementStatusToRowBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EnforcementProcessingStatus status)
            return Brushes.Transparent;
        return status switch
        {
            EnforcementProcessingStatus.Appeal => new SolidColorBrush(Color.FromRgb(245, 243, 255)),
            EnforcementProcessingStatus.Delinquent => new SolidColorBrush(Color.FromRgb(255, 251, 235)),
            EnforcementProcessingStatus.Urgent => new SolidColorBrush(Color.FromRgb(254, 242, 242)),
            _ => Brushes.White
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}