using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace EverythingLabeled.ExplorerHost;

/// <summary>Maps resource kind → DrawingImage from Themes/KindIcons.xaml.</summary>
public sealed class KindIconConverter : IValueConverter
{
    public static readonly KindIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = FindResultPresentation.IconKey(value as string ?? string.Empty);
        return Application.Current?.TryFindResource(key) as ImageSource;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
