using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SQLStudio.ViewModels;

namespace SQLStudio.Converters;

public class StepStatusColorConverter : IValueConverter
{
    public static readonly StepStatusColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is StepStatus status)
        {
            return status switch
            {
                StepStatus.Pending => new SolidColorBrush(Color.Parse("#30363d")),
                StepStatus.InProgress => new SolidColorBrush(Color.Parse("#1f6feb")),
                StepStatus.Completed => new SolidColorBrush(Color.Parse("#238636")),
                StepStatus.Failed => new SolidColorBrush(Color.Parse("#da3633")),
                _ => new SolidColorBrush(Color.Parse("#30363d"))
            };
        }
        return new SolidColorBrush(Color.Parse("#30363d"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StepStatusIconConverter : IValueConverter
{
    public static readonly StepStatusIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is StepStatus status)
        {
            return status switch
            {
                StepStatus.Pending => "○",
                StepStatus.InProgress => "◐",
                StepStatus.Completed => "✓",
                StepStatus.Failed => "✗",
                _ => "○"
            };
        }
        return "○";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StepStatusTextColorConverter : IValueConverter
{
    public static readonly StepStatusTextColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is StepStatus status)
        {
            return status switch
            {
                StepStatus.Pending => new SolidColorBrush(Color.Parse("#8b949e")),
                StepStatus.InProgress => new SolidColorBrush(Color.Parse("#58a6ff")),
                StepStatus.Completed => new SolidColorBrush(Color.Parse("#3fb950")),
                StepStatus.Failed => new SolidColorBrush(Color.Parse("#f85149")),
                _ => new SolidColorBrush(Color.Parse("#8b949e"))
            };
        }
        return new SolidColorBrush(Color.Parse("#8b949e"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ListToStringConverter : IValueConverter
{
    public static readonly ListToStringConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IEnumerable<string> list)
        {
            var items = list.ToList();
            if (items.Count == 0) return "";
            return string.Join(", ", items);
        }
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 此转换器仅用于显示，不支持反向转换
        return null;
    }
}
