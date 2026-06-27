using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BannerGuardSystem.Models;
// SettingsTab used by SettingsTab* converters

namespace BannerGuardSystem.Converters;

public class PeriodTabBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ReportPeriodType selected && parameter is string s
            && Enum.TryParse<ReportPeriodType>(s, out var tab))
            return selected == tab ? "#2563EB" : "#E5E7EB";
        return "#E5E7EB";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class PeriodTabForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ReportPeriodType selected && parameter is string s
            && Enum.TryParse<ReportPeriodType>(s, out var tab))
            return selected == tab ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 65, 81));
        return Brushes.Black;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class RankBarWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double ratio)
            return Math.Max(8, ratio * 200);
        return 8.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class SettingsTabBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SettingsTab selected && parameter is string s
            && Enum.TryParse<SettingsTab>(s, out var tab))
            return selected == tab ? "#2563EB" : "#E5E7EB";
        return "#E5E7EB";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class SettingsTabForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SettingsTab selected && parameter is string s
            && Enum.TryParse<SettingsTab>(s, out var tab))
            return selected == tab ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 65, 81));
        return Brushes.Black;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrEmpty(hex))
            return Brushes.Gray;
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        }
        catch
        {
            return Brushes.Gray;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}