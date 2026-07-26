namespace ElectronicLogbook.Portable;

using System.Globalization;

/// <summary>
/// A workbook-faithful user-facing row from the Currency + Recency table.
/// </summary>
public sealed record PortableLogbookCurrencyRow(
    string Category,
    string CasrReference,
    string Requirement,
    int RelevantPeriodTotal,
    string Status,
    int DaysRemaining,
    DateOnly? CurrentOrRecentUntil,
    DateOnly? CalculatedExpiry)
{
    public string CurrentOrRecentUntilDisplay =>
        CurrentOrRecentUntil?.ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? "Never Current";

    public string CalculatedExpiryDisplay =>
        CalculatedExpiry?.ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? "-";
}
