using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using SQLStudio.ViewModels;

namespace SQLStudio.Converters;

public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string paramString)
        {
            var parts = paramString.Split(';');
            if (parts.Length == 2)
            {
                var colorStr = boolValue ? parts[0] : parts[1];
                return Brush.Parse(colorStr);
            }
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string paramString)
        {
            var parts = paramString.Split(';');
            if (parts.Length == 2)
            {
                return boolValue ? parts[0] : parts[1];
            }
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToAlignmentConverter : IValueConverter
{
    public static readonly BoolToAlignmentConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isUser)
        {
            return isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        return HorizontalAlignment.Left;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ChatBubbleColorConverter : IMultiValueConverter
{
    public static readonly ChatBubbleColorConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is bool isUser && values[1] is bool isError)
        {
            if (isError)
                return Brush.Parse("#3d1f20"); // Dark red for errors
            return isUser ? Brush.Parse("#1f6feb") : Brush.Parse("#21262d"); // Blue for user, dark gray for AI
        }
        return Brush.Parse("#21262d");
    }
}

public class BoolToForegroundConverter : IValueConverter
{
    public static readonly BoolToForegroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Both user and AI messages use light text on dark theme
        return Brush.Parse("#e6edf3");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class HistoryModeConverter : IValueConverter
{
    public static readonly HistoryModeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ChatHistoryMode mode)
        {
            return mode switch
            {
                ChatHistoryMode.Complete => "完整",
                ChatHistoryMode.Improved => "改进",
                _ => value.ToString()
            };
        }
        return value?.ToString() ?? "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class AiModelStatusColorConverter : IValueConverter
{
    public static readonly AiModelStatusColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasModel = !string.IsNullOrEmpty(value as string);
        return hasModel ? Brush.Parse("#3fb950") : Brush.Parse("#f85149");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class AiModelStepColorConverter : IValueConverter
{
    public static readonly AiModelStepColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasModel = !string.IsNullOrEmpty(value as string);
        return hasModel ? Brush.Parse("#238636") : Brush.Parse("#30363d");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
