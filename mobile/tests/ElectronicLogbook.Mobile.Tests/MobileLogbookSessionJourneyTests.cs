using System.Text.Json;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileLogbookSessionJourneyTests
{
    [Fact]
    public async Task WorkbookFaithfulJourneySavesCanonicalV2EntryAndReloadsFromBrowserStorage()
    {
        var jsRuntime = new JourneyJsRuntime();
        var session = CreateSession(jsRuntime);

        await session.EnsureLoadedWorkbookAsync();
        FillWorkbookDraft(session.WorkbookDraft);
        Assert.Empty(session.DocumentV2.Operations);

        await session.SaveWorkbookEntryAsync();

        var added = Assert.Single(session.CurrentEntriesV2);
        var operation = Assert.Single(session.DocumentV2.Operations);
        Assert.StartsWith("ent_", operation.EntryId.Value, StringComparison.Ordinal);
        Assert.Equal(36, operation.EntryId.Value.Length);
        Assert.NotNull(added.Entry);
        Assert.Equal("C172", added.Entry.Type);
        Assert.Equal("VH-WBV", added.Entry.Reg);
        Assert.Equal("Alex", added.Entry.Pic);
        Assert.Equal(1.1m, added.Entry.SeCommandDay);
        Assert.Equal(0.4m, added.Entry.IfrIf);
        Assert.Equal(2, added.Entry.Ils);
        Assert.Equal("Flight added.", session.LastActionMessage);
        Assert.Equal(1, jsRuntime.SaveCount);

        var reloaded = CreateSession(jsRuntime);
        await reloaded.EnsureLoadedWorkbookAsync();

        var reloadedEntry = Assert.Single(reloaded.CurrentEntriesV2);
        Assert.Equal(operation.EntryId, reloadedEntry.EntryId);
        Assert.Equal("VH-WBV", reloadedEntry.Entry?.Reg);
        Assert.Equal("Ready", reloaded.PackageKeyStatus);
    }

    [Fact]
    public async Task WorkbookFaithfulEntryIdIsAllocatedOnlyWhenSavePersistsCreateOperation()
    {
        var jsRuntime = new JourneyJsRuntime();
        var session = CreateSession(jsRuntime);

        await session.EnsureLoadedWorkbookAsync();
        Assert.Empty(session.DocumentV2.Operations);

        Assert.NotEmpty(session.WorkbookDraftErrors);
        Assert.Empty(session.DocumentV2.Operations);

        await session.SaveWorkbookEntryAsync();

        Assert.Empty(session.DocumentV2.Operations);
        Assert.Equal(0, jsRuntime.SaveCount);

        FillWorkbookDraft(session.WorkbookDraft);
        await session.SaveWorkbookEntryAsync();

        var create = Assert.Single(session.DocumentV2.Operations);
        Assert.Equal(PortableOperationKind.Create, create.Kind);
        Assert.StartsWith("ent_", create.EntryId.Value, StringComparison.Ordinal);
        Assert.Equal(1, jsRuntime.SaveCount);

        var savedEntryId = create.EntryId;
        var savedRevisionId = create.RevisionId;
        var current = Assert.Single(session.CurrentEntriesV2);
        session.EditWorkbookEntry(current);
        session.WorkbookDraft.Reg = "VH-WBX";
        await session.SaveWorkbookEntryAsync();

        var correction = session.DocumentV2.Operations.Last();
        Assert.Equal(PortableOperationKind.Correction, correction.Kind);
        Assert.Equal(savedEntryId, correction.EntryId);
        Assert.Equal(savedRevisionId, Assert.Single(correction.ParentRevisionIds));
        Assert.Equal(2, jsRuntime.SaveCount);

        await session.DeleteWorkbookEntryAsync(Assert.Single(session.CurrentEntriesV2));

        var deletion = session.DocumentV2.Operations.Last();
        Assert.Equal(PortableOperationKind.Deletion, deletion.Kind);
        Assert.Equal(savedEntryId, deletion.EntryId);
        Assert.Equal(correction.RevisionId, Assert.Single(deletion.ParentRevisionIds));
        Assert.Equal(3, jsRuntime.SaveCount);
    }

    [Fact]
    public async Task Gate3EntryJourneyAddsEditsClonesDeletesAndReloadsFromBrowserStorage()
    {
        var jsRuntime = new JourneyJsRuntime();
        var session = CreateSession(jsRuntime);

        await session.EnsureLoadedAsync();
        FillDraft(session.Draft, "VH-ADD", 1.0m);
        await session.SaveEntryAsync();

        var added = Assert.Single(session.CurrentEntries);
        Assert.Equal("Flight added.", session.LastActionMessage);
        Assert.Equal("VH-ADD", added.Entry?.Registration);
        Assert.Equal(1, jsRuntime.SaveCount);

        session.EditEntry(added);
        session.Draft.Registration = "VH-EDIT";
        session.Draft.PilotInCommand = 1.2m;
        session.Draft.Day = 1.2m;
        await session.SaveEntryAsync();

        var edited = Assert.Single(session.CurrentEntries);
        Assert.Equal("Correction saved.", session.LastActionMessage);
        Assert.Equal("VH-EDIT", edited.Entry?.Registration);
        Assert.Equal(2, edited.RevisionHistory.Count);

        session.CloneEntry(edited.Entry!);
        Assert.Null(session.EditingEntryId);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), session.Draft.Date);
        Assert.Equal("VH-EDIT", session.Draft.Registration);
        Assert.Null(session.Draft.TakeoffsDay);
        Assert.Null(session.Draft.TakeoffsNight);
        Assert.Equal("Draft started from recent flight.", session.LastActionMessage);
        Assert.True(session.ShouldShowLastActionMessage(MobileActionMessageSurface.Draft));
        Assert.False(session.ShouldShowLastActionMessage(MobileActionMessageSurface.Logbook));
        await session.SaveEntryAsync();

        Assert.Equal(2, session.CurrentEntries.Count);
        Assert.Equal(3, session.Document.Operations.Count);
        Assert.True(session.ShouldShowLastActionMessage(MobileActionMessageSurface.Logbook));

        await session.DeleteEntryAsync(edited);

        Assert.Equal("Entry deleted.", session.LastActionMessage);
        Assert.Single(session.CurrentEntries);
        var deleted = Assert.Single(session.DeletedEntries);
        Assert.Equal(edited.EntryId, deleted.EntryId);
        Assert.True(session.FindHistory(edited.EntryId.Value)?.IsDeleted);

        var reloaded = CreateSession(jsRuntime);
        await reloaded.EnsureLoadedAsync();

        Assert.Single(reloaded.CurrentEntries);
        Assert.Single(reloaded.DeletedEntries);
        Assert.Equal(session.Document.Operations.Count, reloaded.Document.Operations.Count);
        Assert.Equal("Ready", reloaded.PackageKeyStatus);
    }

    [Fact]
    public async Task Gate3ConflictJourneyLoadsConflictAndPersistsResolution()
    {
        var jsRuntime = new JourneyJsRuntime();
        var create = CreateOperation("rev_create", "VH-BASE", 1.0m);
        var local = CorrectOperation(create, "rev_local", "VH-LOCAL", 1.1m, new DeviceId("dev_mobile"));
        var incoming = CorrectOperation(create, "rev_incoming", "VH-IMPORT", 1.4m, new DeviceId("dev_excel"));
        var document = PortableLogbookDocument.CreateAustraliaFirst(
            create.LogbookId,
            MobileLogbookSession.CustomFields,
            [create, local, incoming]);
        await new BrowserLogbookStore(jsRuntime).SaveStateAsync(new BrowserLogbookState(document, [], null));

        var session = CreateSession(jsRuntime);
        await session.EnsureLoadedAsync();

        var conflict = Assert.Single(session.MergeResult.Conflicts);
        Assert.Equal(create.EntryId, conflict.EntryId);
        Assert.Equal([incoming.RevisionId, local.RevisionId], conflict.HeadRevisionIds);

        await session.ResolveConflictAsync(conflict, local.RevisionId);

        Assert.Empty(session.MergeResult.Conflicts);
        var resolved = Assert.Single(session.CurrentEntries);
        Assert.Equal("VH-LOCAL", resolved.Entry?.Registration);
        Assert.Equal("Conflict resolved.", session.LastActionMessage);
        Assert.IsType<ResolveConflictOperation>(session.Document.Operations.Last());

        var reloaded = CreateSession(jsRuntime);
        await reloaded.EnsureLoadedAsync();

        Assert.Empty(reloaded.MergeResult.Conflicts);
        Assert.Equal("VH-LOCAL", Assert.Single(reloaded.CurrentEntries).Entry?.Registration);
    }

    private static MobileLogbookSession CreateSession(JourneyJsRuntime jsRuntime) =>
        new(new BrowserLogbookStore(jsRuntime), new BrowserPackageKeyStore(jsRuntime));

    private static void FillDraft(EntryDraft draft, string registration, decimal hours)
    {
        draft.Date = DateOnly.FromDateTime(DateTime.Today);
        draft.AircraftType = "C172";
        draft.Registration = registration;
        draft.FlightNumber = "AD332";
        draft.From = "YSCN";
        draft.To = "YMML";
        draft.Route = "YSCN YMML";
        draft.PilotInCommand = hours;
        draft.Day = hours;
        draft.TakeoffsDay = 1;
        draft.LandingsDay = 1;
    }

    private static void FillWorkbookDraft(MobileWorkbookEntryDraft draft)
    {
        draft.Date = new DateOnly(2026, 7, 24);
        draft.Type = "C172";
        draft.Reg = "VH-WBV";
        draft.FlightId = "AD332";
        draft.Pic = "Alex";
        draft.OtherPilotOrCrew = "Jamie";
        draft.From = "YSBK";
        draft.To = "YSCN";
        draft.Via = "YWOL";
        draft.Remarks = "Workbook faithful";
        draft.FlightReview = true;
        draft.SeCommandDay = 1.1m;
        draft.IfrIf = 0.4m;
        draft.LandingsDay = 1;
        draft.Ils = 2;
    }

    private static CreateEntryOperation CreateOperation(string revisionId, string registration, decimal hours)
    {
        var logbookId = new LogbookId("log_mobile_preview");
        return new CreateEntryOperation(
            logbookId,
            new EntryId("entry_gate3"),
            new RevisionId(revisionId),
            new DeviceId("dev_seed"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            Entry(registration, hours));
    }

    private static CorrectEntryOperation CorrectOperation(
        CreateEntryOperation parent,
        string revisionId,
        string registration,
        decimal hours,
        DeviceId deviceId) =>
        new(
            parent.LogbookId,
            parent.EntryId,
            new RevisionId(revisionId),
            new HashSet<RevisionId> { parent.RevisionId },
            deviceId,
            parent.CreatedAt.AddMinutes(1),
            Entry(registration, hours));

    private static PortableLogbookEntry Entry(string registration, decimal hours) =>
        PortableLogbookEntry.Empty with
        {
            Date = DateOnly.Parse("2026-07-18"),
            AircraftType = "C172",
            Registration = registration,
            FlightNumber = "AD332",
            From = "YSCN",
            To = "YMML",
            Route = "YSCN YMML",
            PilotInCommand = hours,
            Day = hours,
            TakeoffsDay = 1,
            LandingsDay = 1
        };

    private sealed class JourneyJsRuntime : IJSRuntime
    {
        public string? StoredJson { get; private set; }

        public int SaveCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return identifier switch
            {
                "electronicLogbookStore.load" => new ValueTask<TValue>((TValue)(object?)StoredJson!),
                "electronicLogbookStore.save" => Save<TValue>(args),
                "electronicLogbookKeys.isSupported" => new ValueTask<TValue>((TValue)(object)true),
                "electronicLogbookKeys.hasPackageKey" => new ValueTask<TValue>((TValue)(object)true),
                _ => throw new JSException($"Unexpected JS call: {identifier}")
            };
        }

        private ValueTask<TValue> Save<TValue>(object?[]? args)
        {
            Assert.NotNull(args);
            Assert.Equal("portable-document", Assert.IsType<string>(args[0]));
            StoredJson = Assert.IsType<string>(args[1]);
            SaveCount++;
            JsonSerializer.Deserialize<BrowserLogbookStoredDocument>(StoredJson, PortableLogbookJson.SerializerOptions);
            return new ValueTask<TValue>(default(TValue)!);
        }
    }
}
