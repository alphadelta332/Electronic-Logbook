using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public static class WorkbookMigrationPayloadConverter
{
    public static WorkbookMigrationPayload ConvertWorkbook(
        string workbookPath,
        HostedWorkbookMigration migration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentNullException.ThrowIfNull(migration);

        WorkbookPackageValidator.ValidateWorkbookPackage(workbookPath);
        var customFields = PortableLogbookWorkbookPackageStorage
            .ReadWorkbookCustomFieldDefinitions(workbookPath);
        var rows = PortableLogbookWorkbookPackageStorage
            .ReadCurrentRowsForInspectionV2(workbookPath, customFields);
        if (rows.UnrecognizedUserDataRowCount > 0)
        {
            throw new InvalidDataException(
                "The spreadsheet still contains rows that cannot be converted into flights. Fix the warnings in Excel and try again.");
        }

        return ConvertRows(
            rows.Rows,
            customFields,
            PortableLogbookWorkbookPackageStorage.ReadCurrencyOverrideDates(workbookPath),
            migration);
    }

    public static WorkbookMigrationPayload ConvertRows(
        IEnumerable<PortableLogbookWorkbookRowV2> rows,
        IEnumerable<CustomFieldDefinition> customFieldDefinitions,
        PortableLogbookCurrencyOverrideDates currencyOverrideDates,
        HostedWorkbookMigration migration)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(customFieldDefinitions);
        ArgumentNullException.ThrowIfNull(currencyOverrideDates);
        ArgumentNullException.ThrowIfNull(migration);

        if (migration.Status is not (
                HostedWorkbookMigrationStatus.Pending or
                HostedWorkbookMigrationStatus.Completed))
        {
            throw new InvalidOperationException(
                "Flight data can be prepared only for a pending or completed spreadsheet migration.");
        }

        var materializedRows = rows.ToArray();
        if (materializedRows.Length == 0)
        {
            throw new InvalidOperationException(
                "The spreadsheet does not contain any flights to move to FlightLogX.");
        }

        var operations = materializedRows
            .Select((row, index) => CreateOperation(row.Entry, index, migration))
            .ToArray();
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            migration.LogbookId,
            customFieldDefinitions,
            currencyOverrideDates,
            operations);
        var receipt = PortableWorkbookMigrationVerification.CreateReceipt(
            migration.SourceFingerprint,
            document);
        return new WorkbookMigrationPayload(document, receipt);
    }

    private static PortableLogbookOperationV2 CreateOperation(
        PortableLogbookWorkbookEntry entry,
        int rowIndex,
        HostedWorkbookMigration migration)
    {
        var normalizedEntry = entry with
        {
            CustomFields = entry.CustomFields
                .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value)
        };
        var entryJson = JsonSerializer.Serialize(
            normalizedEntry,
            PortableLogbookJson.SerializerOptions);
        var identitySeed = string.Join(
            "\0",
            "electronic-logbook.workbook-migration-operation.v1",
            migration.MigrationId.Value,
            migration.SourceFingerprint,
            rowIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            entryJson);
        var entryId = new EntryId("ent_" + DeterministicHex(identitySeed, "entry"));
        var revisionId = new RevisionId("rev_" + DeterministicHex(identitySeed, "revision"));

        return PortableLogbookOperationV2.Create(
            migration.LogbookId,
            entryId,
            revisionId,
            migration.DeviceId,
            migration.StartedAt.ToUniversalTime().AddMilliseconds(rowIndex),
            normalizedEntry);
    }

    private static string DeterministicHex(string seed, string purpose)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed + "\0" + purpose));
        return Convert.ToHexString(bytes.AsSpan(0, 16)).ToLowerInvariant();
    }
}

public sealed record WorkbookMigrationPayload(
    PortableLogbookDocumentV2 Document,
    PortableWorkbookMigrationReceipt Receipt);
