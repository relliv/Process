﻿using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Process.UI.Converters
{
    [ValueConversion(typeof(bool), typeof(bool))]
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Visibility.Collapsed;

            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
