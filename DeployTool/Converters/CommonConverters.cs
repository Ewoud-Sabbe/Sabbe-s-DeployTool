using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DeployTool.Converters;

public sealed class BoolToStarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "★" : "☆";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BoolToAccentForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ItemStatusToPillForegroundConverter.FindBrush(value is true ? "AccentBrush" : "TextTertiaryBrush");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
