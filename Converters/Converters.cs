using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DeployApp.Converters
{
    /// <summary>
    /// Converts a status string to a theme-aware foreground brush.
    /// Requested/Scheduled = Warning, Deployed = Success, Failed/Rolled Back = Danger, Cancelled = Muted.
    /// The XAML grids use the StatusText style instead, which also updates live when the theme changes.
    /// </summary>
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = (value as string ?? string.Empty) switch
            {
                "Requested" or "Scheduled" => "WarningBrush",
                "Deployed" => "SuccessBrush",
                "Failed" or "Rolled Back" => "DangerBrush",
                "Cancelled" => "MutedBrush",
                _ => "TextPrimaryBrush"
            };
            return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Returns Visibility.Visible when the string is non-empty, Collapsed otherwise.
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
