using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ElectronicLogbook.Portable;

public static class PortableWorkbookMigrationVerification
{
    public static PortableWorkbookMigrationReceipt CreateReceipt(
        string sourceFingerprint,
        PortableLogbookDocumentV2 document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        ArgumentNullException.ThrowIfNull(document);

        var entries = ReadMigrationEntries(document);
        var entryValuesSha256 = HashJson(entries.Select(NormalizeEntry).ToArray());
        var customFieldDefinitionsSha256 = HashJson(document.CustomFieldDefinitions
            .OrderBy(field => field.Order)
            .ThenBy(field => field.Id.Value, StringComparer.Ordinal)
            .ToArray());
        var currencyOverrideDatesSha256 = HashJson(document.CurrencyOverrideDates);
        var documentSha256 = HashText(PortableLogbookJson.SerializeV2(document));
        var totals = PortableWorkbookMigrationTotals.Calculate(entries);
        var payload = new ReceiptPayload(
            sourceFingerprint,
            document.LogbookId,
            document.Operations[0].DeviceId,
            entries.Length,
            entryValuesSha256,
            customFieldDefinitionsSha256,
            currencyOverrideDatesSha256,
            documentSha256,
            totals);

        return new PortableWorkbookMigrationReceipt(
            payload.SourceFingerprint,
            payload.LogbookId,
            payload.DeviceId,
            payload.EntryCount,
            payload.EntryValuesSha256,
            payload.CustomFieldDefinitionsSha256,
            payload.CurrencyOverrideDatesSha256,
            payload.DocumentSha256,
            payload.CalculatedTotals,
            HashJson(payload));
    }

    public static void VerifyExact(
        PortableWorkbookMigrationReceipt expected,
        PortableLogbookDocumentV2 readback)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(readback);

        PortableWorkbookMigrationReceipt actual;
        try
        {
            actual = CreateReceipt(expected.SourceFingerprint, readback);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            throw new InvalidDataException(
                "Hosted migration readback is not a complete flight-operation snapshot.",
                ex);
        }

        if (actual.LogbookId != expected.LogbookId || actual.DeviceId != expected.DeviceId)
        {
            throw new InvalidDataException("Hosted migration readback belongs to a different logbook or device.");
        }
        if (actual.EntryCount != expected.EntryCount)
        {
            throw new InvalidDataException("Hosted migration readback flight count does not match the workbook.");
        }
        if (!SameHash(actual.EntryValuesSha256, expected.EntryValuesSha256))
        {
            throw new InvalidDataException("Hosted migration readback flight values do not match the workbook.");
        }
        if (!SameHash(actual.CustomFieldDefinitionsSha256, expected.CustomFieldDefinitionsSha256))
        {
            throw new InvalidDataException("Hosted migration readback custom fields do not match the workbook.");
        }
        if (!SameHash(actual.CurrencyOverrideDatesSha256, expected.CurrencyOverrideDatesSha256))
        {
            throw new InvalidDataException("Hosted migration readback currency override dates do not match the workbook.");
        }
        if (actual.CalculatedTotals != expected.CalculatedTotals)
        {
            throw new InvalidDataException("Hosted migration readback calculated totals do not match the workbook.");
        }
        if (!SameHash(actual.DocumentSha256, expected.DocumentSha256) ||
            !SameHash(actual.VerificationReceiptSha256, expected.VerificationReceiptSha256))
        {
            throw new InvalidDataException("Hosted migration readback changed after encryption or storage.");
        }
    }

    private static PortableLogbookWorkbookEntry[] ReadMigrationEntries(PortableLogbookDocumentV2 document)
    {
        if (document.Operations.Count == 0)
        {
            throw new InvalidOperationException("A workbook migration must contain at least one flight operation.");
        }
        if (document.Operations.Any(operation =>
                operation.Kind != PortableOperationKind.Create ||
                operation.Entry is null ||
                operation.LogbookId != document.LogbookId))
        {
            throw new InvalidDataException("A workbook migration must contain only complete create operations for its hosted logbook.");
        }

        var deviceIds = document.Operations.Select(operation => operation.DeviceId).Distinct().ToArray();
        if (deviceIds.Length != 1)
        {
            throw new InvalidDataException("A workbook migration must use one temporary workbook device.");
        }
        if (document.Operations.Select(operation => operation.EntryId).Distinct().Count() != document.Operations.Count ||
            document.Operations.Select(operation => operation.RevisionId).Distinct().Count() != document.Operations.Count)
        {
            throw new InvalidDataException("A workbook migration contains duplicate flight or revision identifiers.");
        }

        return document.Operations
            .OrderBy(operation => operation.CreatedAt)
            .ThenBy(operation => operation.RevisionId.Value, StringComparer.Ordinal)
            .Select(operation => operation.Entry!)
            .ToArray();
    }

    private static PortableLogbookWorkbookEntry NormalizeEntry(PortableLogbookWorkbookEntry entry) =>
        entry with
        {
            CustomFields = entry.CustomFields
                .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value)
        };

    private static string HashJson<T>(T value) =>
        HashText(JsonSerializer.Serialize(value, PortableLogbookJson.SerializerOptions));

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool SameHash(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed record ReceiptPayload(
        string SourceFingerprint,
        LogbookId LogbookId,
        DeviceId DeviceId,
        int EntryCount,
        string EntryValuesSha256,
        string CustomFieldDefinitionsSha256,
        string CurrencyOverrideDatesSha256,
        string DocumentSha256,
        PortableWorkbookMigrationTotals CalculatedTotals);
}

public sealed record PortableWorkbookMigrationReceipt(
    string SourceFingerprint,
    LogbookId LogbookId,
    DeviceId DeviceId,
    int EntryCount,
    string EntryValuesSha256,
    string CustomFieldDefinitionsSha256,
    string CurrencyOverrideDatesSha256,
    string DocumentSha256,
    PortableWorkbookMigrationTotals CalculatedTotals,
    string VerificationReceiptSha256);

public sealed record PortableWorkbookMigrationTotals(
    int EntryCount,
    decimal FlightHours,
    decimal SimulatorHours,
    decimal LoggedHours,
    int DayLandings,
    int NightLandings,
    int InstrumentApproaches,
    int Circling)
{
    public static PortableWorkbookMigrationTotals Calculate(
        IEnumerable<PortableLogbookWorkbookEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var materialized = entries.ToArray();
        var flightHours = materialized.Sum(FlightHoursFor);
        var simulatorHours = materialized.Sum(entry => entry.IfrSim.GetValueOrDefault());
        return new PortableWorkbookMigrationTotals(
            materialized.Length,
            flightHours,
            simulatorHours,
            flightHours + simulatorHours,
            materialized.Sum(entry => entry.LandingsDay.GetValueOrDefault()),
            materialized.Sum(entry => entry.LandingsNight.GetValueOrDefault()),
            materialized.Sum(entry =>
                entry.Ils.GetValueOrDefault() +
                entry.Vor.GetValueOrDefault() +
                entry.Rnp.GetValueOrDefault() +
                entry.Ndb.GetValueOrDefault() +
                entry.DgaCdi.GetValueOrDefault() +
                entry.DgaAzi.GetValueOrDefault()),
            materialized.Sum(entry => entry.Circling.GetValueOrDefault()));
    }

    private static decimal FlightHoursFor(PortableLogbookWorkbookEntry entry) =>
        entry.SeIcusDay.GetValueOrDefault() +
        entry.SeIcusNight.GetValueOrDefault() +
        entry.SeDualDay.GetValueOrDefault() +
        entry.SeDualNight.GetValueOrDefault() +
        entry.SeCommandDay.GetValueOrDefault() +
        entry.SeCommandNight.GetValueOrDefault() +
        entry.MeIcusDay.GetValueOrDefault() +
        entry.MeIcusNight.GetValueOrDefault() +
        entry.MeDualDay.GetValueOrDefault() +
        entry.MeDualNight.GetValueOrDefault() +
        entry.MeCommandDay.GetValueOrDefault() +
        entry.MeCommandNight.GetValueOrDefault() +
        entry.CopilotDay.GetValueOrDefault() +
        entry.CopilotNight.GetValueOrDefault();
}
