using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile;

public sealed class MobileLogbookSession(
    BrowserLogbookStore logbookStore,
    BrowserPackageKeyStore packageKeyStore)
{
    private readonly DeviceId deviceId = new("dev_mobile_preview");

    public static readonly CustomFieldDefinition[] CustomFields =
    [
        new(new CustomFieldId("cf_workbook_1"), "Custom 1", 1),
        new(new CustomFieldId("cf_workbook_2"), "Custom 2", 2),
        new(new CustomFieldId("cf_workbook_3"), "Custom 3", 3),
        new(new CustomFieldId("cf_workbook_4"), "Custom 4", 4)
    ];

    public PortableLogbookDocument Document { get; private set; } =
        PortableLogbookDocument.CreateAustraliaFirst(new LogbookId("log_mobile_preview"), CustomFields, []);

    public IReadOnlyList<PortableLogbookPackageReceipt> ImportReceipts { get; private set; } = [];

    public DateTimeOffset? LastSuccessfulExportAt { get; private set; }

    public BrowserLogbookExportCheckpoint? LastSuccessfulExport { get; private set; }

    public EntryDraft Draft { get; private set; } = EntryDraft.Create();

    public bool HasAttemptedSubmit { get; private set; }

    public bool HasEditedDraft { get; private set; }

    public EntryId? EditingEntryId { get; private set; }

    public RevisionId? EditingRevisionId { get; private set; }

    public bool IsLoaded { get; private set; }

    public bool IsStorageBlocked { get; private set; }

    public string? StorageError { get; private set; }

    public string PackageKeyStatus { get; private set; } = "Checking";

    public string? LastActionMessage { get; private set; }

    private MobileActionMessageScope LastActionMessageScope { get; set; } = MobileActionMessageScope.Global;

    public bool ShouldShowLastActionMessage(MobileActionMessageSurface surface) =>
        !string.IsNullOrWhiteSpace(LastActionMessage) &&
        (LastActionMessageScope == MobileActionMessageScope.Global ||
            surface == MobileActionMessageSurface.Draft);

    public PortableLogbookMergeResult MergeResult =>
        PortableLogbookMerger.Merge(Document.Operations);

    public IReadOnlyList<PortableLogbookMaterializedEntry> CurrentEntries =>
        MergeResult
            .Entries
            .Values
            .Where(entry => !entry.IsDeleted && entry.Entry is not null)
            .OrderByDescending(entry => entry.Entry!.Date)
            .ToArray();

    public PortableLogbookExchangeStatusSnapshot ExchangeStatus =>
        PortableLogbookExchangeStatus.Create(
            new PortableLogbookWorkingCopyState(
                Document.Operations.Count > 0,
                Document.Operations.Count,
                Document.Operations.Count(operation => operation.Kind == PortableOperationKind.Create),
                Document.Operations.Count(operation => operation.Kind == PortableOperationKind.Correction),
                Document.Operations.Count(operation => operation.Kind == PortableOperationKind.Deletion),
                Document.Operations.Select(operation => operation.RevisionId).ToArray(),
                DateTimeOffset.UtcNow),
            ImportReceipts,
            LastSuccessfulExportAt);

    public IReadOnlyList<string> DraftErrors => ValidateDraft();

    public IReadOnlyList<string> DraftWarnings => ValidateDraftWarnings();

    public bool ShouldShowDraftErrors => DraftErrors.Count > 0 && (HasAttemptedSubmit || HasEditedDraft);

    public string DraftModeLabel => EditingEntryId is null ? "New flight" : "Correction";

    public string SaveLabel => EditingEntryId is null ? "Add flight" : "Save correction";

    public string PackageKeyNotice => MobilePackageKeyNotice.Create(PackageKeyStatus);

    public IReadOnlyList<PortableLogbookMaterializedEntry> DeletedEntries =>
        MergeResult
            .Entries
            .Values
            .Where(entry => entry.IsDeleted)
            .OrderByDescending(entry => FindOperation(entry.CurrentRevisionId).CreatedAt)
            .ToArray();

    public async Task EnsureLoadedAsync()
    {
        if (IsLoaded)
        {
            return;
        }

        try
        {
            var state = await logbookStore.LoadStateAsync();
            if (state is not null)
            {
                Document = state.Document;
                ImportReceipts = state.ImportReceipts;
                LastSuccessfulExportAt = state.LastSuccessfulExportAt;
                LastSuccessfulExport = state.LastSuccessfulExport;
            }

            await RefreshPackageKeyStatusAsync();
            ResetDraft();
        }
        catch (BrowserLogbookStoreException ex)
        {
            StorageError = ex.Message + " Export a valid backup package from a compatible device before using this app version.";
            IsStorageBlocked = true;
        }
        finally
        {
            IsLoaded = true;
        }
    }

    public async Task SaveEntryAsync()
    {
        if (IsStorageBlocked)
        {
            return;
        }

        HasAttemptedSubmit = true;
        ClearLastActionMessage();
        if (DraftErrors.Count > 0)
        {
            return;
        }

        PortableLogbookOperation operation = EditingEntryId is null || EditingRevisionId is null
            ? new CreateEntryOperation(
                Document.LogbookId,
                EntryId.New(),
                RevisionId.New(),
                deviceId,
                DateTimeOffset.UtcNow,
                Draft.ToEntry(CustomFields))
            : new CorrectEntryOperation(
                Document.LogbookId,
                EditingEntryId.Value,
                RevisionId.New(),
                new HashSet<RevisionId> { EditingRevisionId.Value },
                deviceId,
                DateTimeOffset.UtcNow,
                Draft.ToEntry(CustomFields));

        Document = MobileLogbookDocument.AppendOperation(Document, CustomFields, operation);
        await SaveStateAsync();
        SetLastActionMessage(EditingEntryId is null ? "Flight added." : "Correction saved.");
        ResetDraft();
    }

    public void ResetDraft()
    {
        if (IsStorageBlocked)
        {
            return;
        }

        Draft = EntryDraft.Create(FindDraftDefaults());
        HasAttemptedSubmit = false;
        HasEditedDraft = false;
        EditingEntryId = null;
        EditingRevisionId = null;
    }

    public void CloneEntry(PortableLogbookEntry entry)
    {
        if (IsStorageBlocked)
        {
            return;
        }

        Draft = EntryDraft.FromEntry(entry, preserveDate: false);
        Draft.ClearUnsupportedWorkbookFields();
        HasAttemptedSubmit = false;
        HasEditedDraft = false;
        SetLastActionMessage("Draft started from recent flight.", MobileActionMessageScope.Draft);
    }

    public void EditEntry(PortableLogbookMaterializedEntry entry)
    {
        if (IsStorageBlocked || entry.Entry is null)
        {
            return;
        }

        Draft = EntryDraft.FromEntry(entry.Entry, preserveDate: true);
        EditingEntryId = entry.EntryId;
        EditingRevisionId = entry.CurrentRevisionId;
        HasAttemptedSubmit = false;
        HasEditedDraft = false;
        SetLastActionMessage("Correction draft opened.", MobileActionMessageScope.Draft);
    }

    public async Task DeleteEntryAsync(PortableLogbookMaterializedEntry entry)
    {
        if (IsStorageBlocked)
        {
            return;
        }

        var operation = new DeleteEntryOperation(
            Document.LogbookId,
            entry.EntryId,
            RevisionId.New(),
            new HashSet<RevisionId> { entry.CurrentRevisionId },
            deviceId,
            DateTimeOffset.UtcNow);

        Document = MobileLogbookDocument.AppendOperation(Document, CustomFields, operation);
        await SaveStateAsync();
        if (EditingEntryId == entry.EntryId)
        {
            ResetDraft();
        }

        SetLastActionMessage("Entry deleted.");
    }

    public async Task ResolveConflictAsync(PortableLogbookConflict conflict, RevisionId selectedRevisionId)
    {
        if (IsStorageBlocked)
        {
            return;
        }

        var selectedOperation = FindOperation(selectedRevisionId);
        var selectedEntry = EntryPayload(selectedOperation);
        PortableLogbookOperation resolution = selectedEntry is null
            ? new DeleteEntryOperation(
                Document.LogbookId,
                conflict.EntryId,
                RevisionId.New(),
                conflict.HeadRevisionIds.ToHashSet(),
                deviceId,
                DateTimeOffset.UtcNow)
            : PortableLogbookConflictResolution.CreateResolution(
                conflict,
                Document.LogbookId,
                deviceId,
                RevisionId.New(),
                DateTimeOffset.UtcNow,
                selectedEntry,
                $"Kept revision {selectedRevisionId.Value} in the mobile app.");

        Document = MobileLogbookDocument.AppendOperation(Document, CustomFields, resolution);
        await SaveStateAsync();
        SetLastActionMessage("Conflict resolved.");
    }

    private void SetLastActionMessage(
        string message,
        MobileActionMessageScope scope = MobileActionMessageScope.Global)
    {
        LastActionMessage = message;
        LastActionMessageScope = scope;
    }

    private void ClearLastActionMessage()
    {
        LastActionMessage = null;
        LastActionMessageScope = MobileActionMessageScope.Global;
    }

    public PortableLogbookMaterializedEntry? FindCurrentEntry(string? entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return null;
        }

        return MergeResult.Entries.TryGetValue(new EntryId(entryId), out var entry)
            ? entry
            : null;
    }

    public PortableLogbookRevisionHistoryView? FindHistory(string? entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return null;
        }

        try
        {
            return PortableLogbookRevisionHistory.ForEntry(Document, new EntryId(entryId));
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public void MarkDraftEdited()
    {
        HasEditedDraft = true;
    }

    public void UseFlightTimeAsDay()
    {
        if (IsStorageBlocked)
        {
            return;
        }

        Draft.Day = Draft.FlightTime;
        Draft.Night = 0;
        MarkDraftEdited();
    }

    public void UseDirectRoute()
    {
        if (IsStorageBlocked)
        {
            return;
        }

        var from = Draft.From.Trim();
        var to = Draft.To.Trim();
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return;
        }

        Draft.Route = string.Equals(from, to, StringComparison.OrdinalIgnoreCase)
            ? from
            : $"{from} {to}";
        MarkDraftEdited();
    }

    public void UseRecentRemark(string? value)
    {
        if (IsStorageBlocked || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        Draft.Remarks = value;
        MarkDraftEdited();
    }

    public IEnumerable<string> RecentValues(Func<PortableLogbookEntry, string?> selector) =>
        MobileRecentValues.Create(CurrentEntries, selector);

    public IEnumerable<string> RecentAirportValues() =>
        MobileAirportSuggestions.Create(CurrentEntries);

    public static string FormatHours(decimal hours) => hours.ToString("0.0");

    public static string FormatDate(DateOnly? date) => date?.ToString("dd MMM yyyy") ?? "No date";

    public static string FormatTimestamp(DateTimeOffset? timestamp) =>
        timestamp?.ToLocalTime().ToString("dd MMM yyyy HH:mm") ?? "Never";

    public string FormatRoute(PortableLogbookEntry entry)
    {
        var route = $"{entry.From}-{entry.To}".Trim(' ', '-');
        return string.IsNullOrWhiteSpace(route) ? "Route pending" : route;
    }

    public string FormatAircraft(PortableLogbookEntry entry)
    {
        var aircraft = $"{entry.AircraftType} {entry.Registration}".Trim();
        return string.IsNullOrWhiteSpace(aircraft) ? "Aircraft pending" : aircraft;
    }

    public string FormatRegistration(PortableLogbookEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Registration) ? "REG" : entry.Registration.Trim().ToUpperInvariant();

    public PortableLogbookOperation FindOperation(RevisionId revisionId) =>
        Document.Operations.First(operation => operation.RevisionId == revisionId);

    public IEnumerable<EntryDetail> EntryDetails(PortableLogbookEntry entry)
    {
        yield return new("Date", FormatDate(entry.Date));
        yield return new("Aircraft type", FormatText(entry.AircraftType));
        yield return new("Registration", FormatText(entry.Registration));
        yield return new("Flight number", FormatText(entry.FlightNumber));
        yield return new("From", FormatText(entry.From));
        yield return new("To", FormatText(entry.To));
        yield return new("Route", FormatText(entry.Route));
        yield return new("Remarks", FormatText(entry.Details));
        yield return new("Multi-pilot", FormatNullableHours(entry.MultiPilot));
        yield return new("PIC", FormatNullableHours(entry.PilotInCommand));
        yield return new("Co-pilot", FormatNullableHours(entry.CoPilot));
        yield return new("Dual", FormatNullableHours(entry.Dual));
        yield return new("Instructor", FormatNullableHours(entry.Instructor));
        yield return new("Day", FormatNullableHours(entry.Day));
        yield return new("Night", FormatNullableHours(entry.Night));
        yield return new("Instrument actual", FormatNullableHours(entry.InstrumentActual));
        yield return new("Instrument sim", FormatNullableHours(entry.InstrumentSimulated));
        yield return new("Landings day", FormatNullableCount(entry.LandingsDay));
        yield return new("Landings night", FormatNullableCount(entry.LandingsNight));
        yield return new("IFR approaches", FormatNullableCount(entry.IfrApproaches));
        yield return new("Holding", FormatNullableCount(entry.Holding));
        yield return new("RNP", FormatNullableCount(entry.Rnav));
        yield return new("Circling", FormatNullableCount(entry.Circling));

        foreach (var field in EntryCustomFields())
        {
            entry.CustomFields.TryGetValue(field.Id, out var customValue);
            yield return new(field.Label, FormatText(customValue));
        }
    }

    public string FormatConflictRoute(PortableLogbookOperation operation)
    {
        var entry = EntryPayload(operation);
        return entry is null ? "Deleted entry" : FormatRoute(entry);
    }

    public string FormatConflictAircraft(PortableLogbookOperation operation)
    {
        var entry = EntryPayload(operation);
        return entry is null ? "No active payload" : FormatAircraft(entry);
    }

    private async Task RefreshPackageKeyStatusAsync()
    {
        try
        {
            if (!await packageKeyStore.IsSupportedAsync())
            {
                PackageKeyStatus = "Unavailable";
                return;
            }

            PackageKeyStatus = await packageKeyStore.HasPackageKeyAsync(Document.LogbookId)
                ? "Ready"
                : "Not set";
        }
        catch (JSException)
        {
            PackageKeyStatus = "Unavailable";
        }
    }

    private ValueTask SaveStateAsync() =>
        logbookStore.SaveStateAsync(new BrowserLogbookState(Document, ImportReceipts, LastSuccessfulExportAt, LastSuccessfulExport));

    private string[] ValidateDraft()
    {
        return PortableLogbookEntryRules
            .Validate(Draft.ToEntry(CustomFields), DateOnly.FromDateTime(DateTime.Today))
            .Errors
            .Select(error => error.Message)
            .ToArray();
    }

    private string[] ValidateDraftWarnings()
    {
        return MobileLogbookEntryWarnings
            .Create(Draft.ToEntry(CustomFields), CurrentEntries, EditingEntryId)
            .ToArray();
    }

    private MobileEntryDraftDefaults FindDraftDefaults() =>
        MobileEntryDraftDefaultPlanner.Create(CurrentEntries);

    private IEnumerable<CustomFieldDefinition> EntryCustomFields() =>
        Document.CustomFieldDefinitions.Count > 0
            ? Document.CustomFieldDefinitions.OrderBy(field => field.Order)
            : CustomFields;

    private static PortableLogbookEntry? EntryPayload(PortableLogbookOperation operation) =>
        operation switch
        {
            CreateEntryOperation create => create.Entry,
            CorrectEntryOperation correction => correction.Entry,
            ResolveConflictOperation resolution => resolution.Entry,
            DeleteEntryOperation => null,
            _ => throw new InvalidOperationException($"Unsupported operation type {operation.GetType().Name}.")
        };

    private static string FormatText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Blank" : value.Trim();

    private static string FormatNullableHours(decimal? hours) =>
        hours is null ? "0.0" : FormatHours(hours.Value);

    private static string FormatNullableCount(int? count) =>
        count?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0";
}

public sealed record EntryDetail(string Label, string Value);

public sealed class EntryDraft
{
    public DateOnly Date { get; set; }
    public string AircraftType { get; set; } = string.Empty;
    public string Registration { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public decimal MultiPilot { get; set; }
    public decimal PilotInCommand { get; set; }
    public decimal CoPilot { get; set; }
    public decimal Dual { get; set; }
    public decimal Instructor { get; set; }
    public decimal Day { get; set; }
    public decimal Night { get; set; }
    public decimal InstrumentActual { get; set; }
    public decimal InstrumentSimulated { get; set; }
    public int? TakeoffsDay { get; set; }
    public int? TakeoffsNight { get; set; }
    public int? LandingsDay { get; set; }
    public int? LandingsNight { get; set; }
    public int? IfrApproaches { get; set; }
    public int? Holding { get; set; }
    public int? Rnav { get; set; }
    public int? Circling { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public Dictionary<CustomFieldId, string> CustomValues { get; } = [];

    public decimal FlightTime =>
        MultiPilot + PilotInCommand + CoPilot + Dual + Instructor;

    public decimal LoggedTime => FlightTime + InstrumentSimulated;

    public int TotalLandings => LandingsDay.GetValueOrDefault() + LandingsNight.GetValueOrDefault();

    public int TotalApproaches =>
        IfrApproaches.GetValueOrDefault() +
        Holding.GetValueOrDefault() +
        Rnav.GetValueOrDefault() +
        Circling.GetValueOrDefault();

    public static EntryDraft Create(MobileEntryDraftDefaults? defaults = null)
    {
        defaults ??= MobileEntryDraftDefaults.Empty;
        var draft = new EntryDraft
        {
            Date = DateOnly.FromDateTime(DateTime.Today),
            AircraftType = defaults.AircraftType,
            Registration = defaults.Registration,
            From = defaults.From,
            To = defaults.To,
            Route = defaults.Route
        };
        foreach (var field in MobileLogbookSession.CustomFields)
        {
            draft.CustomValues[field.Id] = string.Empty;
        }

        return draft;
    }

    public static EntryDraft FromEntry(PortableLogbookEntry entry, bool preserveDate)
    {
        var draft = Create();
        draft.Date = preserveDate && entry.Date is not null
            ? entry.Date.Value
            : DateOnly.FromDateTime(DateTime.Today);
        draft.AircraftType = entry.AircraftType ?? string.Empty;
        draft.Registration = entry.Registration ?? string.Empty;
        draft.FlightNumber = entry.FlightNumber ?? string.Empty;
        draft.From = entry.From ?? string.Empty;
        draft.To = entry.To ?? string.Empty;
        draft.Route = entry.Route ?? string.Empty;
        draft.MultiPilot = entry.MultiPilot ?? 0;
        draft.PilotInCommand = entry.PilotInCommand ?? 0;
        draft.CoPilot = entry.CoPilot ?? 0;
        draft.Dual = entry.Dual ?? 0;
        draft.Instructor = entry.Instructor ?? 0;
        draft.Day = entry.Day ?? 0;
        draft.Night = entry.Night ?? 0;
        draft.InstrumentActual = entry.InstrumentActual ?? 0;
        draft.InstrumentSimulated = entry.InstrumentSimulated ?? 0;
        draft.TakeoffsDay = entry.TakeoffsDay;
        draft.TakeoffsNight = entry.TakeoffsNight;
        draft.LandingsDay = entry.LandingsDay;
        draft.LandingsNight = entry.LandingsNight;
        draft.IfrApproaches = entry.IfrApproaches;
        draft.Holding = entry.Holding;
        draft.Rnav = entry.Rnav;
        draft.Circling = entry.Circling;
        draft.Remarks = entry.Details ?? string.Empty;
        foreach (var customField in entry.CustomFields)
        {
            draft.CustomValues[customField.Key] = customField.Value ?? string.Empty;
        }

        return draft;
    }

    public void ClearUnsupportedWorkbookFields()
    {
        TakeoffsDay = null;
        TakeoffsNight = null;
    }

    public PortableLogbookEntry ToEntry(IEnumerable<CustomFieldDefinition> fields)
    {
        IReadOnlyDictionary<CustomFieldId, string?> customValues = fields
            .Select(field => new KeyValuePair<CustomFieldId, string>(field.Id, CustomValues.GetValueOrDefault(field.Id, string.Empty)))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => (string?)pair.Value);

        return PortableLogbookEntry.Empty with
        {
            Date = Date,
            AircraftType = AircraftType.Trim(),
            Registration = Registration.Trim(),
            FlightNumber = FlightNumber.Trim(),
            From = From.Trim(),
            To = To.Trim(),
            Route = Route.Trim(),
            Details = Remarks.Trim(),
            MultiPilot = MultiPilot,
            PilotInCommand = PilotInCommand,
            CoPilot = CoPilot,
            Dual = Dual,
            Instructor = Instructor,
            Day = Day,
            Night = Night,
            InstrumentActual = InstrumentActual,
            InstrumentSimulated = InstrumentSimulated,
            TakeoffsDay = TakeoffsDay,
            TakeoffsNight = TakeoffsNight,
            LandingsDay = LandingsDay,
            LandingsNight = LandingsNight,
            IfrApproaches = IfrApproaches,
            Holding = Holding,
            Rnav = Rnav,
            Circling = Circling,
            CustomFields = customValues
        };
    }
}

public enum MobileActionMessageSurface
{
    Draft,
    Logbook
}

internal enum MobileActionMessageScope
{
    Global,
    Draft
}
