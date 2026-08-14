using System.Globalization;

namespace ElectronicLogbook.Mobile;

public static class MobileHourField
{
    public static string Format(decimal? value) =>
        value is null or 0m
            ? string.Empty
            : value.Value.ToString("0.0", CultureInfo.InvariantCulture);

    public static decimal? Parse(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
