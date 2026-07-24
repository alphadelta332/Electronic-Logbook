using System.Globalization;

namespace ElectronicLogbook.Updater;

internal static class WorkbookValue
{
    public static string StableValue(object? value)
    {
        return value switch
        {
            null => "",
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            DateTime date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            bool flag => flag ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };
    }

    public static bool IsBlankValue(object? value)
    {
        return value is null ||
            (value is string text && string.IsNullOrWhiteSpace(text));
    }

    public static double ToDouble(object? value)
    {
        return value switch
        {
            null => 0,
            double number => number,
            float number => number,
            int number => number,
            decimal number => (double)number,
            string text when double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) => number,
            _ => 0
        };
    }

    public static bool ToBoolean(object? value)
    {
        return value switch
        {
            null => false,
            bool flag => flag,
            double number => Math.Abs(number) > double.Epsilon,
            float number => Math.Abs(number) > float.Epsilon,
            int number => number != 0,
            decimal number => number != 0,
            string text when bool.TryParse(text.Trim(), out var flag) => flag,
            string text when double.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var number) => Math.Abs(number) > double.Epsilon,
            string text when string.Equals(text.Trim(), "yes", StringComparison.OrdinalIgnoreCase) => true,
            string text when string.Equals(text.Trim(), "y", StringComparison.OrdinalIgnoreCase) => true,
            string text when string.Equals(text.Trim(), "x", StringComparison.OrdinalIgnoreCase) => true,
            _ => false
        };
    }

    public static double? ToLogbookDate(object? yearValue, object? monthValue, object? dayValue)
    {
        var year = ResolveLogbookYear(yearValue);
        var day = ResolveLogbookDay(dayValue);
        var monthText = StableValue(monthValue).Trim();
        var month = ResolveLogbookMonth(monthValue, monthText);
        if (year <= 0 || day <= 0 || string.IsNullOrWhiteSpace(monthText))
        {
            return null;
        }

        if (month <= 0)
        {
            return null;
        }

        try
        {
            return new DateTime(year, month, day).ToOADate();
        }
        catch
        {
            return null;
        }
    }

    private static int ResolveLogbookYear(object? yearValue)
    {
        if (yearValue is DateTime date)
        {
            return date.Year;
        }

        return (int)ToDouble(yearValue);
    }

    private static int ResolveLogbookMonth(object? monthValue, string monthText)
    {
        if (monthValue is DateTime date)
        {
            return date.Month;
        }

        var monthNumber = ToDouble(monthValue);
        if (monthNumber >= 1 && monthNumber <= 12)
        {
            return (int)monthNumber;
        }
        if (monthNumber > 31)
        {
            try
            {
                return DateTime.FromOADate(monthNumber).Month;
            }
            catch
            {
                return 0;
            }
        }

        if (int.TryParse(monthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericMonth))
        {
            if (numericMonth >= 1 && numericMonth <= 12)
            {
                return numericMonth;
            }
            if (numericMonth > 31)
            {
                try
                {
                    return DateTime.FromOADate(numericMonth).Month;
                }
                catch
                {
                    return 0;
                }
            }
        }

        var format = CultureInfo.InvariantCulture.DateTimeFormat;
        for (var month = 1; month <= 12; month++)
        {
            if (string.Equals(monthText, format.AbbreviatedMonthNames[month - 1], StringComparison.OrdinalIgnoreCase) ||
                string.Equals(monthText, format.MonthNames[month - 1], StringComparison.OrdinalIgnoreCase))
            {
                return month;
            }
        }

        return 0;
    }

    private static int ResolveLogbookDay(object? dayValue)
    {
        if (dayValue is DateTime date)
        {
            return date.Day;
        }

        var dayNumber = ToDouble(dayValue);
        if (dayNumber >= 1 && dayNumber <= 31)
        {
            return (int)dayNumber;
        }
        if (dayNumber > 31)
        {
            try
            {
                return DateTime.FromOADate(dayNumber).Day;
            }
            catch
            {
                return 0;
            }
        }
        return 0;
    }
}
