using System;
using System.Globalization;
using System.Windows.Data;

namespace Odinsons.ValheimLauncher
{
    public class MultiplyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double number && double.TryParse(parameter?.ToString(), out double multiplier))
            {
                return number * multiplier;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}