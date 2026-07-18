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

    [Fact]
    public void MergeLawsHoldAcrossSeededMixedHistoriesAndDuplicateBatches()
    {
        var operations = MixedHistoryOperations();
        var canonical = MergeSignature(PortableLogbookMerger.Merge(operations));
        var random = new Random(210);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var delivery = operations
                .Concat(operations.Where((_, index) => index % 2 == attempt % 2))
                .ToArray();
            Shuffle(delivery, random);

            var result = PortableLogbookMerger.Merge(delivery);

            Assert.Equal(canonical, MergeSignature(result));
            Assert.Equal(operations.Count, result.OperationCount);
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

    private static IReadOnlyList<PortableLogbookOperation> MixedHistoryOperations()
    {
        var logbookId = new LogbookId("log_laws");
        var device = new DeviceId("dev_excel");
        var createdAt = DateTimeOffset.Parse("2026-07-18T00:00:00Z");

        var entryA = new CreateEntryOperation(
            logbookId,
            new EntryId("ent_a"),
            new RevisionId("rev_a_create"),
            device,
            createdAt,
            Entry("VH-AA0"));
        var entryACorrect = new CorrectEntryOperation(
            logbookId,
            entryA.EntryId,
            new RevisionId("rev_a_correct"),
            new HashSet<RevisionId> { entryA.RevisionId },
            device,
            createdAt.AddMinutes(1),
            Entry("VH-AA1"));

        var entryB = new CreateEntryOperation(
            logbookId,
            new EntryId("ent_b"),
            new RevisionId("rev_b_create"),
            device,
            createdAt.AddMinutes(2),
            Entry("VH-BB0"));
        var entryBDelete = new DeleteEntryOperation(
            logbookId,
            entryB.EntryId,
            new RevisionId("rev_b_delete"),
            new HashSet<RevisionId> { entryB.RevisionId },
            device,
            createdAt.AddMinutes(3));

        var entryC = new CreateEntryOperation(
            logbookId,
            new EntryId("ent_c"),
            new RevisionId("rev_c_create"),
            device,
            createdAt.AddMinutes(4),
            Entry("VH-CC0"));
        var entryCLocal = new CorrectEntryOperation(
            logbookId,
            entryC.EntryId,
            new RevisionId("rev_c_local"),
            new HashSet<RevisionId> { entryC.RevisionId },
            new DeviceId("dev_excel"),
            createdAt.AddMinutes(5),
            Entry("VH-CC1"));
        var entryCMobile = new CorrectEntryOperation(
            logbookId,
            entryC.EntryId,
            new RevisionId("rev_c_mobile"),
            new HashSet<RevisionId> { entryC.RevisionId },
            new DeviceId("dev_mobile"),
            createdAt.AddMinutes(6),
            Entry("VH-CC2"));

        return [entryA, entryACorrect, entryB, entryBDelete, entryC, entryCLocal, entryCMobile];
    }

    private static string MergeSignature(PortableLogbookMergeResult result)
    {
        var entries = result.Entries
            .OrderBy(entry => entry.Key.Value, StringComparer.Ordinal)
            .Select(entry => string.Join(
                ":",
                entry.Key.Value,
                entry.Value.CurrentRevisionId.Value,
                entry.Value.IsDeleted,
                entry.Value.Entry?.Registration ?? "",
                string.Join(",", entry.Value.RevisionHistory.Select(revision => revision.Value))));
        var conflicts = result.Conflicts
            .OrderBy(conflict => conflict.EntryId.Value, StringComparer.Ordinal)
            .Select(conflict => string.Join(
                ":",
                conflict.EntryId.Value,
                string.Join(",", conflict.HeadRevisionIds.Select(revision => revision.Value))));

        return string.Join("|", entries) + "||" + string.Join("|", conflicts);
    }

    private static void Shuffle<T>(IList<T> values, Random random)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
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
