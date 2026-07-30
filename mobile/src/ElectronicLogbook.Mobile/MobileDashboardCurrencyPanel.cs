using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public sealed record MobileDashboardCurrencyPanel(
    string Title,
    string StatusLabel,
    string StatusTone,
    string ActionSentence,
    IReadOnlyList<MobileDashboardCurrencyRule> Rules)
{
    public static MobileDashboardCurrencyPanel CreateVfr(IEnumerable<PortableLogbookCurrencyRow> singleEngineRows)
    {
        ArgumentNullException.ThrowIfNull(singleEngineRows);

        var rules = new[]
        {
            Rule(FindRow(singleEngineRows, "License", "Flight Review"), 90),
            Rule(FindRow(singleEngineRows, "Passenger Carrying", "Day"), 30)
        };
        var flightReview = rules[0].Row;
        var dayPassengerCarrying = rules[1].Row;
        var statusLabel = PortableLogbookDashboardCurrency.IsVfrRed(flightReview)
            ? "Not current"
            : PortableLogbookDashboardCurrency.IsVfrGreen(flightReview, dayPassengerCarrying)
                ? "Current"
                : "Action soon";

        return Create("VFR", statusLabel, rules);
    }

    public static MobileDashboardCurrencyPanel CreateIfr(IEnumerable<PortableLogbookCurrencyRow> singleEngineRows)
    {
        ArgumentNullException.ThrowIfNull(singleEngineRows);

        var rules = new[]
        {
            Rule(FindRow(singleEngineRows, "License", "Flight Review"), 90),
            Rule(FindRow(singleEngineRows, "License", "IPC"), 90),
            Rule(FindRow(singleEngineRows, "Passenger Carrying", "Day"), 30),
            Rule(FindRow(singleEngineRows, "Operation", "IFR (Apps)"), 30),
            Rule(FindRow(singleEngineRows, "Operation", "Single Pilot IFR"), 90)
        };
        var statusLabel = PortableLogbookDashboardCurrency.IsIfrRed(rules[0].Row, rules[1].Row, rules[3].Row, rules[4].Row)
            ? "Not current"
            : PortableLogbookDashboardCurrency.IsIfrGreen(rules[0].Row, rules[1].Row, rules[2].Row, rules[3].Row, rules[4].Row)
                ? "Current"
                : "Action soon";

        return Create("IFR", statusLabel, rules);
    }

    private static MobileDashboardCurrencyPanel Create(
        string title,
        string statusLabel,
        IEnumerable<MobileDashboardCurrencyRule> rules)
    {
        var orderedRules = rules
            .OrderBy(rule => rule.SortRank)
            .ThenBy(rule => rule.Row.DaysRemaining)
            .ThenBy(rule => rule.Label, StringComparer.Ordinal)
            .ToArray();
        var actionRule = orderedRules.First();
        var actionSentence = BuildActionSentence(title, statusLabel, actionRule);

        return new MobileDashboardCurrencyPanel(
            title,
            statusLabel,
            ToneForStatus(statusLabel),
            actionSentence,
            orderedRules);
    }

    private static MobileDashboardCurrencyRule Rule(PortableLogbookCurrencyRow row, int comfortableDays) =>
        new(
            LabelFor(row),
            comfortableDays,
            row,
            RuleDetail(row, comfortableDays),
            SortRank(row, comfortableDays));

    private static PortableLogbookCurrencyRow FindRow(
        IEnumerable<PortableLogbookCurrencyRow> rows,
        string category,
        string requirement) =>
        rows.First(row =>
            string.Equals(row.Category, category, StringComparison.Ordinal) &&
            string.Equals(row.Requirement, requirement, StringComparison.Ordinal));

    private static string LabelFor(PortableLogbookCurrencyRow row) =>
        string.Equals(row.Category, "Passenger Carrying", StringComparison.Ordinal)
            ? "Day passenger carrying"
            : row.Requirement;

    private static string RuleDetail(PortableLogbookCurrencyRow row, int comfortableDays)
    {
        var threshold = $"target > {comfortableDays} days";
        if (!string.Equals(row.Status, "Current", StringComparison.Ordinal))
        {
            return $"Not current · {threshold}";
        }

        return $"{row.DaysRemaining} days remaining · {threshold}";
    }

    private static int SortRank(PortableLogbookCurrencyRow row, int comfortableDays)
    {
        if (!string.Equals(row.Status, "Current", StringComparison.Ordinal))
        {
            return 0;
        }

        return row.DaysRemaining <= comfortableDays ? 1 : 2;
    }

    private static string DueText(PortableLogbookCurrencyRow row) =>
        row.CurrentOrRecentUntil is { }
            ? $"by {row.CurrentOrRecentUntilDisplay}"
            : "before the next relevant operation";

    private static string BuildActionSentence(
        string title,
        string statusLabel,
        MobileDashboardCurrencyRule actionRule)
    {
        if (string.Equals(statusLabel, "Current", StringComparison.Ordinal))
        {
            return $"{title} is comfortably current across the dashboard checks.";
        }

        if (!string.Equals(actionRule.Row.Status, "Current", StringComparison.Ordinal))
        {
            return $"{actionRule.Label} is not current. Open Currency to review the detail.";
        }

        return $"{actionRule.Label} needs attention {DueText(actionRule.Row)}. Open Currency to review the detail.";
    }

    private static string ToneForStatus(string statusLabel) =>
        statusLabel switch
        {
            "Current" => "current",
            "Not current" => "expired",
            _ => "warning"
        };
}

public sealed record MobileDashboardCurrencyRule(
    string Label,
    int ComfortableDays,
    PortableLogbookCurrencyRow Row,
    string Detail,
    int SortRank);
