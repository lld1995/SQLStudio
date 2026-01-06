using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace SQLStudio.Converters;

public class SqlVisibilityConverter : IMultiValueConverter
{
    public static readonly SqlVisibilityConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2)
        {
            var sql = values[0] as string;
            var isStreaming = values[1] as bool? ?? false;
            
            // Only show SQL if it exists and streaming is complete
            return !string.IsNullOrEmpty(sql) && !isStreaming;
        }
        
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
