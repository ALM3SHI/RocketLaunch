using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LauncherApp.Converters;

public sealed class OnlineToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Online = new(Color.FromRgb(0x3D, 0xD6, 0x8C));
    private static readonly SolidColorBrush Offline = new(Color.FromRgb(0x5A, 0x5D, 0x6B));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Online : Offline;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
