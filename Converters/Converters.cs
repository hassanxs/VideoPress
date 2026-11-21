using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VideoPress.Models;
using VideoPress.Services;

namespace VideoPress.Converters;

// FileStatus → SolidColorBrush (text colour for status pill)
public class FileStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => (FileStatus)value switch
    {
        FileStatus.Done      => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E,0xCC,0x71)),
        FileStatus.Running   => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA0,0x8C,0xFF)),
        FileStatus.Error     => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE7,0x4C,0x3C)),
        FileStatus.Cancelled => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3,0x9C,0x12)),
        _                    => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55,0x55,0x60)),
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

// FileStatus → SolidColorBrush (background/border for status pill and card border)
public class FileStatusToBorderConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => (FileStatus)value switch
    {
        FileStatus.Done      => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A,0x3D,0x2A)),
        FileStatus.Running   => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E,0x1A,0x3F)),
        FileStatus.Error     => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3D,0x1A,0x1A)),
        FileStatus.Cancelled => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3D,0x2E,0x0A)),
        _                    => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A,0x1A,0x22)),
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

// FileStatus → Visibility: Visible only while Running (shows progress bar)
public class FileStatusToProgressVis : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => (FileStatus)value == FileStatus.Running ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

// bool → Visibility, with optional Invert
public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        bool b = value is bool bv && bv;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => (Visibility)value == Visibility.Visible ? !Invert : Invert;
}

// Enum → bool (for RadioButton IsChecked two-way binding)
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value?.Equals(p) ?? false;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => (value is bool b && b) ? p : System.Windows.Data.Binding.DoNothing;
}

// long → Visibility: Visible when value > 0. ConverterParameter=invert flips it.
public class LongToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        bool hasValue = value is long l && l > 0;
        bool invert   = p as string == "invert";
        return (hasValue ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

// string → Visibility: Visible when non-null and non-empty
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => !string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

// QualityMode → Visibility
public class QualityModeVisConverter : IValueConverter
{
    public QualityMode TargetMode { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is QualityMode q && q == TargetMode ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

// ResolutionMode → Visibility
public class ResModeVisConverter : IValueConverter
{
    public ResolutionMode TargetMode { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is ResolutionMode r && r == TargetMode ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

// string savings text → colour brush: green=saving, amber=larger, grey=same
public class SavingsTextColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var s = value as string ?? "";
        if (s.StartsWith("−"))
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E,0xCC,0x71)); // green
        if (s.StartsWith("+"))
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3,0x9C,0x12)); // amber
        return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55,0x55,0x60));      // grey
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

// GpuInfo → background brush or dot brush depending on ConverterParameter ("bg" or "dot")
// Binding source changed from Gpu.Vendor to Gpu (full object) for TestPassed access.
public class GpuVendorToColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var gpu    = value as GpuInfo ?? GpuInfo.CpuFallback;
        var vendor = gpu.Vendor;
        bool isBg  = p as string == "bg";

        // If test failed (VM mode) — use amber dot regardless of vendor
        if (!gpu.TestPassed && vendor != GpuVendor.Cpu && vendor != GpuVendor.Detecting)
        {
            var amber = System.Windows.Media.Color.FromRgb(0xF3,0x9C,0x12);
            return isBg
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(30,0xF3,0x9C,0x12))
                : new SolidColorBrush(amber);
        }

        var dot = vendor switch
        {
            GpuVendor.Nvidia    => System.Windows.Media.Color.FromRgb(0x76,0xB9,0x00),
            GpuVendor.Amd       => System.Windows.Media.Color.FromRgb(0xED,0x1C,0x24),
            GpuVendor.Intel     => System.Windows.Media.Color.FromRgb(0x00,0x71,0xC5),
            GpuVendor.Detecting => System.Windows.Media.Color.FromRgb(0xF3,0x9C,0x12),
            _                   => System.Windows.Media.Color.FromRgb(0x55,0x55,0x60),
        };

        if (!isBg) return new SolidColorBrush(dot);

        return vendor switch
        {
            GpuVendor.Nvidia    => new SolidColorBrush(System.Windows.Media.Color.FromArgb(40,0x76,0xB9,0x00)),
            GpuVendor.Amd       => new SolidColorBrush(System.Windows.Media.Color.FromArgb(40,0xED,0x1C,0x24)),
            GpuVendor.Intel     => new SolidColorBrush(System.Windows.Media.Color.FromArgb(40,0x00,0x71,0xC5)),
            GpuVendor.Detecting => new SolidColorBrush(System.Windows.Media.Color.FromArgb(30,0xF3,0x9C,0x12)),
            _                   => new SolidColorBrush(System.Windows.Media.Color.FromArgb(30,0x55,0x55,0x60)),
        };
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

// int → Visibility: Visible when value > 0 (for collection counts)
public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

// object → Visibility: Visible when non-null (or inverted: Visible when null)
public class NullToVisConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        bool isNull = value == null;
        return (isNull ^ Invert) ? Visibility.Collapsed : Visibility.Visible;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}
