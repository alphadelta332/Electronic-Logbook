namespace ElectronicLogbook.Mobile;

public static class MobileRelativeDate
{
    public static string? Format(DateOnly? date) =>
        Format(date, DateOnly.FromDateTime(DateTime.Today));

    public static string? Format(DateOnly? date, DateOnly today)
    {
        if (date is null)
        {
            return null;
        }

        if (date > today)
        {
            return null;
        }

        if (date == today)
        {
            return "Today";
        }

        var earlier = date.Value;
        var days = today.DayNumber - earlier.DayNumber;

        if (days == 1)
        {
            return "Yesterday";
        }

        var years = CompleteYearsBetween(earlier, today);
        if (years > 0)
        {
            return Ago(years, "year");
        }

        var months = CompleteMonthsBetween(earlier, today);
        return months > 0
            ? Ago(months, "month")
            : Ago(days, "day");
    }

    private static int CompleteYearsBetween(DateOnly earlier, DateOnly later)
    {
        var years = later.Year - earlier.Year;
        return earlier.AddYears(years) > later ? years - 1 : years;
    }

    private static int CompleteMonthsBetween(DateOnly earlier, DateOnly later)
    {
        var months = ((later.Year - earlier.Year) * 12) + later.Month - earlier.Month;
        return earlier.AddMonths(months) > later ? months - 1 : months;
    }

    private static string Ago(int value, string unit)
    {
        var pluralizedUnit = value == 1 ? unit : $"{unit}s";
        return $"{value} {pluralizedUnit} ago";
    }
}
