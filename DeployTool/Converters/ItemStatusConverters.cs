using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DeployTool.Core.Models;

namespace DeployTool.Converters;

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

/// <summary>Hides the status pill entirely for items that haven't run yet (nothing to report).</summary>
public sealed class ItemStatusToPillVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ItemStatus.Pending ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ItemStatusToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ItemStatus.Running => "⏳",
        ItemStatus.Succeeded => "✓",
        ItemStatus.Failed => "✕",
        ItemStatus.AlreadyInstalled => "ℹ",
        _ => ""
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ItemStatusToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ItemStatus.Running => "Bezig...",
        ItemStatus.Succeeded => "Geslaagd",
        ItemStatus.Failed => "Mislukt",
        ItemStatus.AlreadyInstalled => "Al geïnstalleerd",
        _ => ""
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ItemStatusToPillForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        FindBrush(value switch
        {
            ItemStatus.Succeeded => "SuccessBrush",
            ItemStatus.Failed => "FailBrush",
            ItemStatus.Running => "RunningBrush",
            ItemStatus.AlreadyInstalled => "InfoBrush",
            _ => "PendingBrush"
        });

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    internal static Brush FindBrush(string key) =>
        (Brush)(Application.Current.TryFindResource(key) ?? Brushes.Gray);
}

public sealed class ItemStatusToPillBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ItemStatusToPillForegroundConverter.FindBrush(value switch
        {
            ItemStatus.Succeeded => "SuccessSoftBrush",
            ItemStatus.Failed => "FailSoftBrush",
            ItemStatus.Running => "RunningSoftBrush",
            ItemStatus.AlreadyInstalled => "InfoSoftBrush",
            _ => "PendingSoftBrush"
        });

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
