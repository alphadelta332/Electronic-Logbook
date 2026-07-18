using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookMergeLawTests
{
    [Fact]
    public void MergeProducesSameHeadForEveryPermutationOfLinearHistory()
    {
        var create = CreateOperation("rev_create", "VH-AAA", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var correctionA = CorrectOperation(create, "rev_a", "VH-AAB", create.CreatedAt.AddMinutes(1));
        var correctionB = new CorrectEntryOperation(
            create.LogbookId,
            create.EntryId,
            new RevisionId("rev_b"),
            new HashSet<RevisionId> { correctionA.RevisionId },
            create.DeviceId,
            create.CreatedAt.AddMinutes(2),
            Entry("VH-AAC"));

        foreach (var permutation in Permute([create, correctionA, correctionB]))
        {
            var result = PortableLogbookMerger.Merge(permutation);
            var entry = Assert.Single(result.Entries.Values);
            Assert.Equal(correctionB.RevisionId, entry.CurrentRevisionId);
            Assert.Equal("VH-AAC", entry.Entry?.Registration);
            Assert.Empty(result.Conflicts);
        }
    }

    [Fact]
    public void MergeIsIdempotentUnderRepeatedDuplicateDelivery()
    {
        var create = CreateOperation("rev_create", "VH-AAA", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var correction = CorrectOperation(create, "rev_correct", "VH-AAB", create.CreatedAt.AddMinutes(1));

        var result = PortableLogbookMerger.Merge([create, correction, create, correction, correction, create]);

        Assert.Equal(2, result.OperationCount);
        var entry = Assert.Single(result.Entries.Values);
        Assert.Equal(correction.RevisionId, entry.CurrentRevisionId);
    }

    [Fact]
    public void MergeConflictHeadsAreStableAcrossPermutation()
    {
        var create = CreateOperation("rev_create", "VH-AAA", DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        var correctionA = CorrectOperation(create, "rev_a", "VH-AAB", create.CreatedAt.AddMinutes(1));
        var correctionB = CorrectOperation(create, "rev_b", "VH-AAC", create.CreatedAt.AddMinutes(2));

        foreach (var permutation in Permute([create, correctionA, correctionB]))
        {
            var result = PortableLogbookMerger.Merge(permutation);
            var conflict = Assert.Single(result.Conflicts);
            Assert.Equal([correctionA.RevisionId, correctionB.RevisionId], conflict.HeadRevisionIds);
            Assert.Empty(result.Entries);
        }
    }

    private static IEnumerable<IReadOnlyList<PortableLogbookOperation>> Permute(IReadOnlyList<PortableLogbookOperation> operations)
    {
        if (operations.Count == 1)
        {
            yield return operations;
            yield break;
        }

        for (var index = 0; index < operations.Count; index++)
        {
            var head = operations[index];
            var remainder = operations.Where((_, remainderIndex) => remainderIndex != index).ToArray();
            foreach (var tail in Permute(remainder))
            {
                yield return new[] { head }.Concat(tail).ToArray();
            }
        }
    }

    private static CreateEntryOperation CreateOperation(string revisionId, string registration, DateTimeOffset createdAt) =>
        new(
            new LogbookId("log_laws"),
            new EntryId("ent_1"),
            new RevisionId(revisionId),
            new DeviceId("dev_excel"),
            createdAt,
            Entry(registration));

    private static CorrectEntryOperation CorrectOperation(
        CreateEntryOperation create,
        string revisionId,
        string registration,
        DateTimeOffset createdAt) =>
        new(
            create.LogbookId,
            create.EntryId,
            new RevisionId(revisionId),
            new HashSet<RevisionId> { create.RevisionId },
            create.DeviceId,
            createdAt,
            Entry(registration));

    private static PortableLogbookEntry Entry(string registration) =>
        PortableLogbookEntry.Empty with
        {
            Date = new DateOnly(2026, 7, 18),
            AircraftType = "C172",
            Registration = registration,
            From = "YSBK",
            To = "YSBK",
            PilotInCommand = 1.2m
        };
}
