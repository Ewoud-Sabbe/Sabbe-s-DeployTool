using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DeployTool.Core.Models;

namespace DeployTool.Converters;

public sealed class ItemStatusToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ItemStatus.Pending => "",
        ItemStatus.Running => "⏳",
        ItemStatus.Succeeded => "✅",
        ItemStatus.Failed => "❌",
        ItemStatus.AlreadyInstalled => "ℹ️",
        _ => ""
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ItemStatusToRetryVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ItemStatus.Failed ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is false ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ItemStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ItemStatus.Succeeded => Brushes.Green,
        ItemStatus.Failed => Brushes.Red,
        ItemStatus.Running => Brushes.DarkOrange,
        ItemStatus.AlreadyInstalled => Brushes.SteelBlue,
        _ => Brushes.Gray
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
