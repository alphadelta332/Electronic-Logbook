namespace ElectronicLogbook.Portable;

public static class PortableLogbookRetention
{
    public const int MinimumRetentionYears = 7;

    public static PortableLogbookRetentionSnapshot Evaluate(
        PortableLogbookDocument document,
        DateOnly asOf,
        int minimumRetentionYears = MinimumRetentionYears)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (minimumRetentionYears < MinimumRetentionYears)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumRetentionYears),
                $"Portable logbook retention cannot be shorter than {MinimumRetentionYears} years.");
        }

        var cutoff = asOf.AddYears(-minimumRetentionYears);
        var retainedOperationCount = document.Operations.Count(operation => DateOnly.FromDateTime(operation.CreatedAt.UtcDateTime) >= cutoff);
        var eligibleForArchiveCount = document.Operations.Count - retainedOperationCount;
        return new PortableLogbookRetentionSnapshot(cutoff, document.Operations.Count, retainedOperationCount, eligibleForArchiveCount);
    }
}

public sealed record PortableLogbookRetentionSnapshot(
    DateOnly RetainAfter,
    int TotalOperationCount,
    int MinimumRetainedOperationCount,
    int OlderThanMinimumRetentionCount);
