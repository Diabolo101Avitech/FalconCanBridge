using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FalconCanBridge.App.Converters;

public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x4A));
    public Brush FalseBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0xB0, 0x30, 0x30));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueBrush : FalseBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
