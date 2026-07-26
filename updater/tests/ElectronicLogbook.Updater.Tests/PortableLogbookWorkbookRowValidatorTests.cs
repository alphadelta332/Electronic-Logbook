using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookWorkbookRowValidatorTests
{
    [Fact]
    public void ValidateAcceptsNewRowsAndRowsMatchingKnownCurrentRevision()
    {
        var known = Known("ent_1", "rev_1");
        var rows = new[]
        {
            new PortableLogbookWorkbookRow(null, null, Entry("VH-NEW")),
            new PortableLogbookWorkbookRow(known.EntryId, known.CurrentRevisionId, Entry("VH-ABC"))
        };

        var result = PortableLogbookWorkbookRowValidator.Validate(rows, [known]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateRejectsRevisionWithoutEntryId()
    {
        var result = PortableLogbookWorkbookRowValidator.Validate(
            [new PortableLogbookWorkbookRow(null, new RevisionId("rev_1"), Entry("VH-ABC"))],
            []);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal(PortableLogbookWorkbookRowValidationCode.RevisionWithoutEntryId, error.Code);
        Assert.Equal(1, error.RowNumber);
    }

    [Fact]
    public void ValidateRejectsMissingEntryPayload()
    {
        var result = PortableLogbookWorkbookRowValidator.Validate(
            [new PortableLogbookWorkbookRow(null, null, null!)],
            []);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal(PortableLogbookWorkbookRowValidationCode.MissingEntryPayload, error.Code);
        Assert.Equal(1, error.RowNumber);
    }

    [Fact]
    public void ValidateRejectsUnknownEntryId()
    {
        var result = PortableLogbookWorkbookRowValidator.Validate(
            [new PortableLogbookWorkbookRow(new EntryId("ent_unknown"), new RevisionId("rev_1"), Entry("VH-ABC"))],
            []);

        Assert.Contains(result.Errors, error => error.Code == PortableLogbookWorkbookRowValidationCode.UnknownEntryId);
    }

    [Fact]
    public void ValidateRejectsMalformedEntryIdBeforeWorkbookReconciliation()
    {
        var result = PortableLogbookWorkbookRowValidator.Validate(
            [new PortableLogbookWorkbookRow(new EntryId("not_an_entry_id"), new RevisionId("rev_1"), Entry("VH-ABC"))],
            []);

        Assert.Contains(result.Errors, error => error.Code == PortableLogbookWorkbookRowValidationCode.InvalidEntryId);
    }

    [Fact]
    public void ValidateRejectsMissingCurrentRevisionForKnownEntry()
    {
        var known = Known("ent_1", "rev_1");

        var result = PortableLogbookWorkbookRowValidator.Validate(
            [new PortableLogbookWorkbookRow(known.EntryId, null, Entry("VH-ABC"))],
            [known]);

        Assert.Contains(result.Errors, error => error.Code == PortableLogbookWorkbookRowValidationCode.MissingCurrentRevisionId);
    }

    [Fact]
    public void ValidateRejectsStaleCurrentRevisionForKnownEntry()
    {
        var known = Known("ent_1", "rev_current");

        var result = PortableLogbookWorkbookRowValidator.Validate(
            [new PortableLogbookWorkbookRow(known.EntryId, new RevisionId("rev_old"), Entry("VH-ABC"))],
            [known]);

        Assert.Contains(result.Errors, error => error.Code == PortableLogbookWorkbookRowValidationCode.StaleCurrentRevisionId);
    }

    [Fact]
    public void ValidateRejectsDuplicateKnownEntryIdRows()
    {
        var known = Known("ent_1", "rev_current");

        var result = PortableLogbookWorkbookRowValidator.Validate(
            [
                new PortableLogbookWorkbookRow(known.EntryId, known.CurrentRevisionId, Entry("VH-ABC")),
                new PortableLogbookWorkbookRow(known.EntryId, known.CurrentRevisionId, Entry("VH-EDITED"))
            ],
            [known]);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal(PortableLogbookWorkbookRowValidationCode.DuplicateEntryId, error.Code);
        Assert.Equal(2, error.RowNumber);
    }

    private static PortableLogbookMaterializedEntry Known(string entryId, string revisionId) =>
        new(
            new EntryId(entryId),
            new RevisionId(revisionId),
            IsDeleted: false,
            Entry("VH-ABC"),
            [new RevisionId(revisionId)]);

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
