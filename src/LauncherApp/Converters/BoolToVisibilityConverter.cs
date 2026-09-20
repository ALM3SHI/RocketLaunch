using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LauncherApp.Converters;

/// <summary>
/// Converts bool to Visibility: true → Visible, false → Collapsed.
/// Used for hiding/showing UI sections based on state flags.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        // Pass "Invert" as ConverterParameter to flip the logic.
        if (parameter is string s && s == "Invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
