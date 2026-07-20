using System.IO.Compression;
using System.Xml.Linq;
using ElectronicLogbook.Portable;
using ElectronicLogbook.Updater;

namespace ElectronicLogbook.Updater.Tests;

public sealed class PortableLogbookWorkbookPackageStorageTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"PortableLogbookWorkbookPackageStorageTests-{Guid.NewGuid():N}");

    public PortableLogbookWorkbookPackageStorageTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public void ReadEnvelopeReturnsNullWhenWorkbookHasNoPortableStoragePart()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);

        var envelope = PortableLogbookWorkbookPackageStorage.ReadEnvelope(workbook);

        Assert.Null(envelope);
    }

    [Fact]
    public void WriteEnvelopeStoresAndReplacesPortableStoragePart()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var key = PortableLogbookKey.Generate();
        var firstEnvelope = CreateEnvelope("log_storage_1", key);
        var secondEnvelope = CreateEnvelope("log_storage_2", key);

        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, firstEnvelope);
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, secondEnvelope);
        var read = PortableLogbookWorkbookPackageStorage.ReadEnvelope(workbook);

        Assert.NotNull(read);
        Assert.Equal(secondEnvelope.LogbookId, read.LogbookId);
        Assert.Equal(secondEnvelope.Summary, read.Summary);
        using var archive = ZipFile.OpenRead(workbook);
        Assert.NotNull(archive.GetEntry(PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath));
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("_rels/.rels"));
        Assert.Single(archive.Entries, entry => entry.FullName == PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath);
    }

    [Fact]
    public void ReadEnvelopeAllowsWorkbookFileSharedForReadWrite()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var envelope = CreateEnvelope("log_shared_read", PortableLogbookKey.Generate());
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, envelope);
        using var sharedOpen = new FileStream(
            workbook,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        var read = PortableLogbookWorkbookPackageStorage.ReadEnvelope(workbook);

        Assert.NotNull(read);
        Assert.Equal(envelope.LogbookId, read.LogbookId);
    }

    [Fact]
    public void WriteEnvelopeRegistersCustomXmlContentTypeAndRelationship()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var envelope = CreateEnvelope("log_storage", PortableLogbookKey.Generate());

        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, envelope);

        using var archive = ZipFile.OpenRead(workbook);
        var contentTypes = ReadXml(archive, "[Content_Types].xml");
        var relationships = ReadXml(archive, "_rels/.rels");
        Assert.Contains(
            contentTypes.Root!.Elements().Where(element => element.Name.LocalName == "Override"),
            element => (string?)element.Attribute("PartName") == "/" + PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath);
        Assert.Contains(
            relationships.Root!.Elements().Where(element => element.Name.LocalName == "Relationship"),
            element =>
                (string?)element.Attribute("Id") == "rIdPortableLogbookStorage" &&
                (string?)element.Attribute("Target") == PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath);
    }

    [Fact]
    public void CopyEnvelopeCopiesPortableStorageBetweenWorkbookPackages()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version, "source.xlsm");
        var destination = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version, "destination.xlsm");
        var envelope = CreateEnvelope("log_copy", PortableLogbookKey.Generate());
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(source, envelope);

        var copied = PortableLogbookWorkbookPackageStorage.CopyEnvelope(source, destination);
        var read = PortableLogbookWorkbookPackageStorage.ReadEnvelope(destination);

        Assert.True(copied);
        Assert.NotNull(read);
        Assert.Equal(envelope.LogbookId, read.LogbookId);
        Assert.Equal(envelope.Summary, read.Summary);
    }

    [Fact]
    public void CopyEnvelopeLeavesDestinationUntouchedWhenSourceHasNoPortableStorage()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version, "source.xlsm");
        var destination = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version, "destination.xlsm");

        var copied = PortableLogbookWorkbookPackageStorage.CopyEnvelope(source, destination);

        Assert.False(copied);
        Assert.Null(PortableLogbookWorkbookPackageStorage.ReadEnvelope(destination));
    }

    [Fact]
    public void OpenStateReturnsNullWhenWorkbookHasNoPortableStoragePart()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);

        var state = PortableLogbookWorkbookPackageStorage.OpenState(workbook, PortableLogbookKey.Generate());

        Assert.Null(state);
    }

    [Fact]
    public void OpenStateDecryptsStoredPortableHistoryPackage()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var key = PortableLogbookKey.Generate();
        var envelope = CreateEnvelope("log_open", key);
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, envelope);

        var state = PortableLogbookWorkbookPackageStorage.OpenState(workbook, key);

        Assert.NotNull(state);
        Assert.Equal(envelope.LogbookId, state.Document.LogbookId);
        Assert.Equal(envelope.SchemaVersion, state.Document.SchemaVersion);
        Assert.Single(state.Document.Operations);
        Assert.Empty(state.ImportReceipts);
    }

    [Fact]
    public void OpenStateRejectsWrongKeyWithoutReturningStoredState()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var envelope = CreateEnvelope("log_wrong_key", PortableLogbookKey.Generate());
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, envelope);

        var exception = Assert.Throws<PortableLogbookPackageException>(
            () => PortableLogbookWorkbookPackageStorage.OpenState(workbook, PortableLogbookKey.Generate()));

        Assert.Equal(PortableLogbookPackageError.AuthenticationFailed, exception.Error);
    }

    [Fact]
    public void EnsureHiddenMetadataColumnsAddsColumnsToLogbookTableAndHidesWorksheetColumns()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);

        var result = PortableLogbookWorkbookPackageStorage.EnsureHiddenMetadataColumns(workbook);

        Assert.Equal(["Portable Entry ID", "Portable Current Revision ID"], result.ColumnsAdded);
        Assert.Equal([2, 3], result.HiddenColumnIndexes);
        using var archive = ZipFile.OpenRead(workbook);
        var table = ReadXml(archive, "xl/tables/table1.xml");
        var tableColumns = table.Root!
            .Elements()
            .Single(element => element.Name.LocalName == "tableColumns");
        Assert.Equal("3", (string?)tableColumns.Attribute("count"));
        Assert.Equal(
            ["Column1", "Portable Entry ID", "Portable Current Revision ID"],
            tableColumns.Elements().Select(column => (string?)column.Attribute("name")));
        Assert.Equal("A1:C2", (string?)table.Root.Attribute("ref"));
        Assert.Equal("A1:C2", (string?)table.Root.Elements().Single(element => element.Name.LocalName == "autoFilter").Attribute("ref")?.Value);

        var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
        Assert.Equal("Portable Entry ID", ReadInlineStringCell(worksheet, "B1"));
        Assert.Equal("Portable Current Revision ID", ReadInlineStringCell(worksheet, "C1"));
        var hiddenColumns = worksheet
            .Descendants()
            .Where(element => element.Name.LocalName == "col" && (string?)element.Attribute("hidden") == "1")
            .Select(element => ((string?)element.Attribute("min"), (string?)element.Attribute("max")))
            .ToArray();
        Assert.Equal([("2", "2"), ("3", "3")], hiddenColumns);
    }

    [Fact]
    public void EnsureHiddenMetadataColumnsIsIdempotent()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        PortableLogbookWorkbookPackageStorage.EnsureHiddenMetadataColumns(workbook);

        var result = PortableLogbookWorkbookPackageStorage.EnsureHiddenMetadataColumns(workbook);

        Assert.Empty(result.ColumnsAdded);
        Assert.Equal([2, 3], result.HiddenColumnIndexes);
        using var archive = ZipFile.OpenRead(workbook);
        var table = ReadXml(archive, "xl/tables/table1.xml");
        var tableColumns = table.Root!
            .Elements()
            .Single(element => element.Name.LocalName == "tableColumns");
        Assert.Equal(3, tableColumns.Elements().Count());
    }

    [Fact]
    public void WriteHiddenMetadataColumnValuesAddsColumnsAndWritesCurrentRowIds()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);

        var result = PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValues(
            workbook,
            [WorkbookRow("ent_1", "rev_1")]);

        Assert.Equal(Path.GetFullPath(workbook), result.WorkbookPath);
        Assert.Equal(1, result.RowCount);
        Assert.Equal([2, 3], result.AbsoluteMetadataColumnIndexes);
        using var archive = ZipFile.OpenRead(workbook);
        var table = ReadXml(archive, "xl/tables/table1.xml");
        Assert.Equal("A1:C2", (string?)table.Root!.Attribute("ref"));
        Assert.Equal("A1:C2", (string?)table.Root.Elements().Single(element => element.Name.LocalName == "autoFilter").Attribute("ref")?.Value);

        var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
        Assert.Equal("Portable Entry ID", ReadInlineStringCell(worksheet, "B1"));
        Assert.Equal("Portable Current Revision ID", ReadInlineStringCell(worksheet, "C1"));
        Assert.Equal("ent_1", ReadInlineStringCell(worksheet, "B2"));
        Assert.Equal("rev_1", ReadInlineStringCell(worksheet, "C2"));
        var hiddenColumns = worksheet
            .Descendants()
            .Where(element => element.Name.LocalName == "col" && (string?)element.Attribute("hidden") == "1")
            .Select(element => ((string?)element.Attribute("min"), (string?)element.Attribute("max")))
            .ToArray();
        Assert.Equal([("2", "2"), ("3", "3")], hiddenColumns);
    }

    [Fact]
    public void WriteHiddenMetadataColumnValuesClearsStaleRows()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValues(
            workbook,
            [
                WorkbookRow("ent_1", "rev_1"),
                WorkbookRow("ent_2", "rev_2")
            ]);

        PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValues(
            workbook,
            [WorkbookRow("ent_1", "rev_3")]);

        using var archive = ZipFile.OpenRead(workbook);
        var table = ReadXml(archive, "xl/tables/table1.xml");
        Assert.Equal("A1:C3", (string?)table.Root!.Attribute("ref"));
        var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
        Assert.Equal("ent_1", ReadInlineStringCell(worksheet, "B2"));
        Assert.Equal("rev_3", ReadInlineStringCell(worksheet, "C2"));
        Assert.Null(ReadInlineStringCell(worksheet, "B3"));
        Assert.Null(ReadInlineStringCell(worksheet, "C3"));
    }

    [Fact]
    public void WriteHiddenMetadataColumnValuesWritesVisiblePayloadCellsWhenColumnsExist()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var customField = new CustomFieldDefinition(new CustomFieldId("cf_role"), "Role", 1);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:G2",
                [
                    "Date",
                    "Reg",
                    "PIC",
                    "Landings Day",
                    "Role",
                    "Portable Entry ID",
                    "Portable Current Revision ID"
                ]);
        }

        PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValues(
            workbook,
            [
                new PortableLogbookWorkbookRow(
                    new EntryId("ent_payload"),
                    new RevisionId("rev_payload"),
                    PortableLogbookEntry.Empty with
                    {
                        Date = new DateOnly(2026, 7, 18),
                        Registration = "VH-ABC",
                        PilotInCommand = 1.2m,
                        LandingsDay = 3,
                        CustomFields = new Dictionary<CustomFieldId, string?> { [customField.Id] = "PICUS" }
                    })
            ],
            [customField]);

        using var readArchive = ZipFile.OpenRead(workbook);
        var worksheet = ReadXml(readArchive, "xl/worksheets/sheet2.xml");
        Assert.Equal("2026-07-18", ReadInlineStringCell(worksheet, "A2"));
        Assert.Equal("VH-ABC", ReadInlineStringCell(worksheet, "B2"));
        Assert.Equal("1.2", ReadInlineStringCell(worksheet, "C2"));
        Assert.Equal("3", ReadInlineStringCell(worksheet, "D2"));
        Assert.Equal("PICUS", ReadInlineStringCell(worksheet, "E2"));
        Assert.Equal("ent_payload", ReadInlineStringCell(worksheet, "F2"));
        Assert.Equal("rev_payload", ReadInlineStringCell(worksheet, "G2"));
    }

    [Fact]
    public void WriteHiddenMetadataColumnValuesUsesAbsoluteWorksheetColumnsWhenTableDoesNotStartInColumnA()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            var table = ReadXml(archive, "xl/tables/table1.xml");
            table.Root!.SetAttributeValue("ref", "C5:C6");
            table.Root.Elements().Single(element => element.Name.LocalName == "autoFilter").SetAttributeValue("ref", "C5:C6");
            ReplaceXml(archive, "xl/tables/table1.xml", table);
        }

        var result = PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValues(
            workbook,
            [WorkbookRow("ent_offset", "rev_offset")]);

        Assert.Equal([4, 5], result.AbsoluteMetadataColumnIndexes);
        using var readArchive = ZipFile.OpenRead(workbook);
        var tableAfter = ReadXml(readArchive, "xl/tables/table1.xml");
        Assert.Equal("C5:E6", (string?)tableAfter.Root!.Attribute("ref"));
        var worksheet = ReadXml(readArchive, "xl/worksheets/sheet2.xml");
        Assert.Equal("Portable Entry ID", ReadInlineStringCell(worksheet, "D5"));
        Assert.Equal("Portable Current Revision ID", ReadInlineStringCell(worksheet, "E5"));
        Assert.Equal("ent_offset", ReadInlineStringCell(worksheet, "D6"));
        Assert.Equal("rev_offset", ReadInlineStringCell(worksheet, "E6"));
        var hiddenColumns = worksheet
            .Descendants()
            .Where(element => element.Name.LocalName == "col" && (string?)element.Attribute("hidden") == "1")
            .Select(element => ((string?)element.Attribute("min"), (string?)element.Attribute("max")))
            .ToArray();
        Assert.Equal([("4", "4"), ("5", "5")], hiddenColumns);
    }

    [Fact]
    public void ReadCurrentRowsBuildsPortableRowsFromWorkbookTableCells()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var customField = new CustomFieldDefinition(new CustomFieldId("cf_role"), "Role", 1);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:G3",
                [
                    "Date",
                    "Reg",
                    "PIC",
                    "Landings Day",
                    "Role",
                    "Portable Entry ID",
                    "Portable Current Revision ID"
                ]);
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "A2", "2026-07-18");
            UpsertInlineStringCell(worksheet, "B2", "VH-ABC");
            UpsertInlineStringCell(worksheet, "C2", "1.2");
            UpsertInlineStringCell(worksheet, "D2", "3");
            UpsertInlineStringCell(worksheet, "E2", "PICUS");
            UpsertInlineStringCell(worksheet, "F2", "ent_1");
            UpsertInlineStringCell(worksheet, "G2", "rev_1");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var rows = PortableLogbookWorkbookPackageStorage.ReadCurrentRows(workbook, [customField]);

        var row = Assert.Single(rows);
        Assert.Equal(new EntryId("ent_1"), row.EntryId);
        Assert.Equal(new RevisionId("rev_1"), row.CurrentRevisionId);
        Assert.Equal(new DateOnly(2026, 7, 18), row.Entry.Date);
        Assert.Equal("VH-ABC", row.Entry.Registration);
        Assert.Equal(1.2m, row.Entry.PilotInCommand);
        Assert.Equal(3, row.Entry.LandingsDay);
        Assert.Equal("PICUS", row.Entry.CustomFields[customField.Id]);
    }

    [Fact]
    public void ReadCurrentRowsSkipsCompletelyBlankRows()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:C3",
                ["Date", "Portable Entry ID", "Portable Current Revision ID"]);
        }

        var rows = PortableLogbookWorkbookPackageStorage.ReadCurrentRows(workbook);

        Assert.Empty(rows);
    }

    [Fact]
    public void EnsureWorkbookIdentityMetadataAddsHiddenDefinedNamesAndBackendValues()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);

        var result = PortableLogbookWorkbookPackageStorage.EnsureWorkbookIdentityMetadata(
            workbook,
            new LogbookId("log_identity"),
            new DeviceId("dev_identity"),
            PortableLogbookDocument.CurrentSchemaVersion);

        Assert.Equal(new LogbookId("log_identity"), result.LogbookId);
        Assert.Equal(new DeviceId("dev_identity"), result.DeviceId);
        Assert.Equal(
            [
                PortableLogbookWorkbookMetadata.LogbookIdName,
                PortableLogbookWorkbookMetadata.DeviceIdName,
                PortableLogbookWorkbookMetadata.SchemaVersionName
            ],
            result.NamesAdded);
        Assert.Equal(["A8", "A9", "A10"], result.CellsWritten);
        using var archive = ZipFile.OpenRead(workbook);
        var workbookXml = ReadXml(archive, "xl/workbook.xml");
        AssertDefinedName(workbookXml, PortableLogbookWorkbookMetadata.LogbookIdName, "'Backend'!$A$8");
        AssertDefinedName(workbookXml, PortableLogbookWorkbookMetadata.DeviceIdName, "'Backend'!$A$9");
        AssertDefinedName(workbookXml, PortableLogbookWorkbookMetadata.SchemaVersionName, "'Backend'!$A$10");

        var backend = ReadXml(archive, "xl/worksheets/sheet1.xml");
        Assert.Equal("log_identity", ReadInlineStringCell(backend, "A8"));
        Assert.Equal("dev_identity", ReadInlineStringCell(backend, "A9"));
        Assert.Equal(PortableLogbookDocument.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), ReadInlineStringCell(backend, "A10"));
    }

    [Fact]
    public void EnsureWorkbookIdentityMetadataReusesExistingDefinedNames()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        PortableLogbookWorkbookPackageStorage.EnsureWorkbookIdentityMetadata(
            workbook,
            new LogbookId("log_original"),
            new DeviceId("dev_original"),
            PortableLogbookDocument.CurrentSchemaVersion);

        var result = PortableLogbookWorkbookPackageStorage.EnsureWorkbookIdentityMetadata(
            workbook,
            new LogbookId("log_reused"),
            new DeviceId("dev_reused"),
            PortableLogbookDocument.CurrentSchemaVersion);

        Assert.Empty(result.NamesAdded);
        Assert.Equal(["A8", "A9", "A10"], result.CellsWritten);
        using var archive = ZipFile.OpenRead(workbook);
        var workbookXml = ReadXml(archive, "xl/workbook.xml");
        Assert.Equal(
            1,
            workbookXml
                .Descendants()
                .Count(element =>
                    element.Name.LocalName == "definedName" &&
                    (string?)element.Attribute("name") == PortableLogbookWorkbookMetadata.LogbookIdName));
        var backend = ReadXml(archive, "xl/worksheets/sheet1.xml");
        Assert.Equal("log_reused", ReadInlineStringCell(backend, "A8"));
        Assert.Equal("dev_reused", ReadInlineStringCell(backend, "A9"));
    }

    [Fact]
    public void ReadWorkbookIdentityMetadataReturnsStoredIdentity()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        PortableLogbookWorkbookPackageStorage.EnsureWorkbookIdentityMetadata(
            workbook,
            new LogbookId("log_read_identity"),
            new DeviceId("dev_read_identity"),
            PortableLogbookDocument.CurrentSchemaVersion);

        var identity = PortableLogbookWorkbookPackageStorage.ReadWorkbookIdentityMetadata(workbook);

        Assert.NotNull(identity);
        Assert.Equal(new LogbookId("log_read_identity"), identity.LogbookId);
        Assert.Equal(new DeviceId("dev_read_identity"), identity.DeviceId);
        Assert.Equal(PortableLogbookDocument.CurrentSchemaVersion, identity.SchemaVersion);
    }

    [Fact]
    public void CopyWorkbookIdentityMetadataCopiesIdentityBetweenWorkbookPackages()
    {
        var source = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version, "source.xlsm");
        var destination = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version, "destination.xlsm");
        PortableLogbookWorkbookPackageStorage.EnsureWorkbookIdentityMetadata(
            source,
            new LogbookId("log_copy_identity"),
            new DeviceId("dev_copy_identity"),
            PortableLogbookDocument.CurrentSchemaVersion);

        var copied = PortableLogbookWorkbookPackageStorage.CopyWorkbookIdentityMetadata(source, destination);
        var identity = PortableLogbookWorkbookPackageStorage.ReadWorkbookIdentityMetadata(destination);

        Assert.True(copied);
        Assert.NotNull(identity);
        Assert.Equal(new LogbookId("log_copy_identity"), identity.LogbookId);
        Assert.Equal(new DeviceId("dev_copy_identity"), identity.DeviceId);
        Assert.Equal(PortableLogbookDocument.CurrentSchemaVersion, identity.SchemaVersion);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PortableLogbookWorkbookStorageEnvelope CreateEnvelope(string logbookId, PortableLogbookKey key)
    {
        var create = new CreateEntryOperation(
            new LogbookId(logbookId),
            new EntryId("ent_1"),
            new RevisionId("rev_1"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookEntry.Empty with
            {
                Date = new DateOnly(2026, 7, 18),
                AircraftType = "C172",
                Registration = "VH-ABC",
                From = "YSBK",
                To = "YSBK",
                PilotInCommand = 1.2m
            });
        var document = PortableLogbookDocument.CreateAustraliaFirst(create.LogbookId, [], [create]);
        return PortableLogbookWorkbookStorage.CreateEnvelope(
            document,
            PortableLogbookPackage.Write(document, key),
            []);
    }

    private static PortableLogbookWorkbookRow WorkbookRow(string entryId, string revisionId) =>
        new(
            new EntryId(entryId),
            new RevisionId(revisionId),
            PortableLogbookEntry.Empty);

    private static XDocument ReadXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"{entryName} was not found.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplaceXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static void ReplaceLogbookTable(
        ZipArchive archive,
        string reference,
        IReadOnlyList<string> columnNames)
    {
        var table = ReadXml(archive, "xl/tables/table1.xml");
        var ns = table.Root!.Name.Namespace;
        table.Root.SetAttributeValue("ref", reference);
        table.Root.Elements().Single(element => element.Name.LocalName == "autoFilter").SetAttributeValue("ref", reference);
        var tableColumns = table.Root.Elements().Single(element => element.Name.LocalName == "tableColumns");
        tableColumns.SetAttributeValue("count", columnNames.Count);
        tableColumns.Elements().Remove();
        for (var index = 0; index < columnNames.Count; index++)
        {
            tableColumns.Add(new XElement(
                ns + "tableColumn",
                new XAttribute("id", index + 1),
                new XAttribute("name", columnNames[index])));
        }

        ReplaceXml(archive, "xl/tables/table1.xml", table);
    }

    private static void UpsertInlineStringCell(XDocument worksheet, string cellReference, string value)
    {
        var root = worksheet.Root ?? throw new InvalidOperationException("Worksheet XML is invalid.");
        var ns = root.Name.Namespace;
        var rowNumber = int.Parse(
            new string(cellReference.SkipWhile(char.IsLetter).ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
        var sheetData = root.Element(ns + "sheetData");
        if (sheetData is null)
        {
            sheetData = new XElement(ns + "sheetData");
            root.Add(sheetData);
        }

        var row = sheetData.Elements(ns + "row")
            .FirstOrDefault(element => ((int?)element.Attribute("r") ?? 0) == rowNumber);
        if (row is null)
        {
            row = new XElement(ns + "row", new XAttribute("r", rowNumber));
            sheetData.Add(row);
        }

        row.Elements(ns + "c")
            .Where(cell => string.Equals((string?)cell.Attribute("r"), cellReference, StringComparison.OrdinalIgnoreCase))
            .Remove();
        row.Add(new XElement(
            ns + "c",
            new XAttribute("r", cellReference),
            new XAttribute("t", "inlineStr"),
            new XElement(ns + "is", new XElement(ns + "t", value))));
    }

    private static void AssertDefinedName(XDocument workbook, string name, string expectedReference)
    {
        var definedName = workbook
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "definedName" &&
                (string?)element.Attribute("name") == name);
        Assert.Equal(expectedReference, definedName.Value);
        Assert.Equal("1", (string?)definedName.Attribute("hidden"));
    }

    private static string? ReadInlineStringCell(XDocument worksheet, string cellReference) =>
        worksheet
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "c" &&
                string.Equals((string?)element.Attribute("r"), cellReference, StringComparison.OrdinalIgnoreCase))
            ?.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "t")
            ?.Value;
}
