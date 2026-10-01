using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CodeCompare.Infrastructure;

/// <summary>Formats a byte count as B / KB / MB / GB.</summary>
public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes) return "";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes:N0} B" : $"{size:0.#} {units[unit]}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Formats a number with an explicit sign; "+" prefix is optional via parameter "prefix".</summary>
public sealed class SignedNumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        int n when n > 0 => $"+{n:N0}",
        int n when n < 0 => $"−{-n:N0}",
        int => "0",
        _ => "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Shows zero as an empty cell so changed rows stand out; parameter is an optional prefix such as "+".</summary>
public sealed class BlankZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        int n when n != 0 => $"{parameter}{n:N0}",
        _ => "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Green for positive, red for negative, muted for zero.</summary>
public sealed class DeltaBrushConverter : IValueConverter
{
    private static readonly Brush Positive = Frozen(0x1A, 0x7F, 0x37);
    private static readonly Brush Negative = Frozen(0xCF, 0x22, 0x2E);
    private static readonly Brush Neutral = Frozen(0x65, 0x6D, 0x76);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        int n when n > 0 => Positive,
        int n when n < 0 => Negative,
        _ => Neutral,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Visible when the bound string is empty (used for placeholder text).</summary>
public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
