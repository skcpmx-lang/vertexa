using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.App;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool flag ? !flag : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool flag ? !flag : value;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (parameter as string == "invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility visible && visible == Visibility.Visible;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var has = value is not null && value is not string s || (value is string str && !string.IsNullOrWhiteSpace(str));
        if (parameter as string == "invert") has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Maps an availability state onto the themed state brush (semantic colour only).</summary>
public sealed class AvailabilityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is AvailabilityState state ? state switch
        {
            AvailabilityState.Available => "SuccessBrush",
            AvailabilityState.PartiallyAvailable => "WarningBrush",
            AvailabilityState.Unavailable => "UnknownBrush",
            AvailabilityState.Unsupported => "UnknownBrush",
            AvailabilityState.AccessDenied => "CriticalBrush",
            AvailabilityState.RequiresAdministrator => "WarningBrush",
            AvailabilityState.RequiresPhysicalHardware => "InfoBrush",
            AvailabilityState.RequiresUserInteraction => "InfoBrush",
            AvailabilityState.Invalid => "CriticalBrush",
            AvailabilityState.NotTested => "UnknownBrush",
            _ => "UnknownBrush",
        } : "UnknownBrush";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Metric rows dim their value when no data exists, so a state label is never mistaken for a value.</summary>
public sealed class HasDataToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool has && has ? 1.0 : 0.55;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Human label for an availability state (used on chips and rows).</summary>
public sealed class AvailabilityLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is AvailabilityState state ? state.Label() : "Unknown";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
