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
        Assert.NotNull(archive.GetEntry("xl/_rels/workbook.xml.rels"));
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
        var workbookRelationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
        Assert.Contains(
            contentTypes.Root!.Elements().Where(element => element.Name.LocalName == "Override"),
            element => (string?)element.Attribute("PartName") == "/" + PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath);
        Assert.Contains(
            workbookRelationships.Root!.Elements().Where(element => element.Name.LocalName == "Relationship"),
            element =>
                (string?)element.Attribute("Id") == "rIdPortableLogbookStorage" &&
                (string?)element.Attribute("Target") == "../" + PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath);
        Assert.Null(archive.GetEntry("_rels/.rels"));
    }

    [Fact]
    public void WriteEnvelopeRepairsLegacyPackageRootCustomXmlRelationship()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var envelope = CreateEnvelope("log_storage_repair", PortableLogbookKey.Generate());
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            var relationshipNamespace = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            var packageRelationships = new XDocument(
                new XElement(
                    relationshipNamespace + "Relationships",
                    new XElement(
                        relationshipNamespace + "Relationship",
                        new XAttribute("Id", "rIdPortableLogbookStorage"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml"),
                        new XAttribute("Target", PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath))));
            ReplaceXml(archive, "_rels/.rels", packageRelationships);
        }

        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, envelope);

        using var repairedArchive = ZipFile.OpenRead(workbook);
        var repairedPackageRelationships = ReadXml(repairedArchive, "_rels/.rels");
        var repairedWorkbookRelationships = ReadXml(repairedArchive, "xl/_rels/workbook.xml.rels");
        Assert.DoesNotContain(
            repairedPackageRelationships.Root!.Elements().Where(element => element.Name.LocalName == "Relationship"),
            element => (string?)element.Attribute("Id") == "rIdPortableLogbookStorage");
        Assert.Contains(
            repairedWorkbookRelationships.Root!.Elements().Where(element => element.Name.LocalName == "Relationship"),
            element =>
                (string?)element.Attribute("Id") == "rIdPortableLogbookStorage" &&
                (string?)element.Attribute("Target") == "../" + PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath);
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
    public void OpenStateV2DecryptsStoredWorkbookFaithfulHistoryPackage()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var key = PortableLogbookKey.Generate();
        var envelope = CreateEnvelopeV2("log_open_v2", key);
        PortableLogbookWorkbookPackageStorage.WriteEnvelope(workbook, envelope);

        var state = PortableLogbookWorkbookPackageStorage.OpenStateV2(workbook, key);

        Assert.NotNull(state);
        Assert.Equal(envelope.LogbookId, state.Document.LogbookId);
        Assert.Equal(PortableLogbookDocumentV2.CurrentSchemaVersion, state.Document.SchemaVersion);
        var operation = Assert.Single(state.Document.Operations);
        Assert.Equal("DA40", operation.Entry?.Type);
        Assert.Equal(1.2m, operation.Entry?.SeCommandDay);
        Assert.Equal(0.4m, operation.Entry?.IfrIf);
        Assert.Equal(2, operation.Entry?.Ils);
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

        Assert.Equal(["EntryID", "Portable Current Revision ID"], result.ColumnsAdded);
        Assert.Equal([2, 3], result.HiddenColumnIndexes);
        using var archive = ZipFile.OpenRead(workbook);
        var table = ReadXml(archive, "xl/tables/table1.xml");
        var tableColumns = table.Root!
            .Elements()
            .Single(element => element.Name.LocalName == "tableColumns");
        Assert.Equal("3", (string?)tableColumns.Attribute("count"));
        Assert.Equal(
            ["Column1", "EntryID", "Portable Current Revision ID"],
            tableColumns.Elements().Select(column => (string?)column.Attribute("name")));
        Assert.Equal("A1:C2", (string?)table.Root.Attribute("ref"));
        Assert.Equal("A1:C2", (string?)table.Root.Elements().Single(element => element.Name.LocalName == "autoFilter").Attribute("ref")?.Value);

        var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
        Assert.Equal("EntryID", ReadInlineStringCell(worksheet, "B1"));
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
        Assert.Equal("EntryID", ReadInlineStringCell(worksheet, "B1"));
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
    public void WriteHiddenMetadataColumnValuesV2KeepsCellsOrderedAndExcludesTotalsRowFromFilter()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(archive, "A1:B3", ["EntryID", "Year"]);
            var table = ReadXml(archive, "xl/tables/table1.xml");
            table.Root!.SetAttributeValue("totalsRowCount", "1");
            table.Root.Elements().Single(element => element.Name.LocalName == "autoFilter")
                .SetAttributeValue("ref", "A1:B3");
            ReplaceXml(archive, "xl/tables/table1.xml", table);

            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "A3", "Totals");
            UpsertInlineStringCell(worksheet, "B3", "0");
            var totalsFormulaCell = worksheet.Descendants().Single(element =>
                element.Name.LocalName == "c" && (string?)element.Attribute("r") == "B3");
            totalsFormulaCell.SetAttributeValue("t", null);
            totalsFormulaCell.Elements().Remove();
            totalsFormulaCell.Add(
                new XElement(worksheet.Root!.Name.Namespace + "f", "SUBTOTAL(109,B2:B2)"),
                new XElement(worksheet.Root.Name.Namespace + "v", "0"));
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var row = new PortableLogbookWorkbookRowV2(
            new EntryId("ent_ordered"),
            new RevisionId("rev_ordered"),
            PortableLogbookWorkbookEntry.Empty with { Year = 2026 });
        PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValuesV2(workbook, [row]);

        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            var table = ReadXml(archive, "xl/tables/table1.xml");
            Assert.Equal("A1:C3", (string?)table.Root!.Attribute("ref"));
            Assert.Equal(
                "A1:C2",
                (string?)table.Root.Elements().Single(element => element.Name.LocalName == "autoFilter").Attribute("ref"));

            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            AssertCellReferencesAscending(worksheet, 2, ["A2", "B2", "C2"]);
            Assert.Equal("Totals", ReadInlineStringCell(worksheet, "A3"));
            Assert.Equal(
                "SUBTOTAL(109,B2:B2)",
                worksheet.Descendants().Single(element =>
                    element.Name.LocalName == "c" && (string?)element.Attribute("r") == "B3")
                    .Elements().Single(element => element.Name.LocalName == "f").Value);

            var dataRow = worksheet.Descendants().Single(element =>
                element.Name.LocalName == "row" && (string?)element.Attribute("r") == "2");
            dataRow.Elements().Single(element =>
                element.Name.LocalName == "c" && (string?)element.Attribute("r") == "A2").Remove();
            dataRow.Add(new XElement(
                worksheet.Root!.Name.Namespace + "c",
                new XAttribute("r", "A2"),
                new XAttribute("t", "inlineStr"),
                new XElement(
                    worksheet.Root.Name.Namespace + "is",
                    new XElement(worksheet.Root.Name.Namespace + "t", "ent_ordered"))));
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);

            table.Root.Elements().Single(element => element.Name.LocalName == "autoFilter")
                .SetAttributeValue("ref", "A1:C3");
            ReplaceXml(archive, "xl/tables/table1.xml", table);
        }

        PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValuesV2(workbook, [row]);

        using var repairedArchive = ZipFile.OpenRead(workbook);
        var repairedTable = ReadXml(repairedArchive, "xl/tables/table1.xml");
        Assert.Equal(
            "A1:C2",
            (string?)repairedTable.Root!.Elements().Single(element => element.Name.LocalName == "autoFilter").Attribute("ref"));
        var repairedWorksheet = ReadXml(repairedArchive, "xl/worksheets/sheet2.xml");
        AssertCellReferencesAscending(repairedWorksheet, 2, ["A2", "B2", "C2"]);
        Assert.Equal("Totals", ReadInlineStringCell(repairedWorksheet, "A3"));
        Assert.Equal(
            "SUBTOTAL(109,B2:B2)",
            repairedWorksheet.Descendants().Single(element =>
                element.Name.LocalName == "c" && (string?)element.Attribute("r") == "B3")
                .Elements().Single(element => element.Name.LocalName == "f").Value);
    }

    [Fact]
    public void WriteHiddenMetadataColumnValuesV2MovesTotalsRowWhenTableGrowsAndInvalidatesCalculationChain()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(archive, "A1:B3", ["EntryID", "Year"]);
            var table = ReadXml(archive, "xl/tables/table1.xml");
            table.Root!.SetAttributeValue("totalsRowCount", "1");
            table.Root.Elements().Single(element => element.Name.LocalName == "autoFilter")
                .SetAttributeValue("ref", "A1:B2");
            ReplaceXml(archive, "xl/tables/table1.xml", table);

            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "A3", "Totals");
            UpsertInlineStringCell(worksheet, "B3", "0");
            var totalsFormulaCell = worksheet.Descendants().Single(element =>
                element.Name.LocalName == "c" && (string?)element.Attribute("r") == "B3");
            totalsFormulaCell.SetAttributeValue("t", null);
            totalsFormulaCell.Elements().Remove();
            totalsFormulaCell.Add(
                new XElement(worksheet.Root!.Name.Namespace + "f", "SUBTOTAL(109,B2:B2)"),
                new XElement(worksheet.Root.Name.Namespace + "v", "0"));
            UpsertInlineStringCell(worksheet, "A4", "Grand Total");
            UpsertInlineStringCell(worksheet, "B4", "0");
            var grandTotalFormulaCell = worksheet.Descendants().Single(element =>
                element.Name.LocalName == "c" && (string?)element.Attribute("r") == "B4");
            grandTotalFormulaCell.SetAttributeValue("t", null);
            grandTotalFormulaCell.Elements().Remove();
            grandTotalFormulaCell.Add(
                new XElement(worksheet.Root.Name.Namespace + "f", "SUM(Table1[#Totals])"),
                new XElement(worksheet.Root.Name.Namespace + "v", "0"));
            UpsertInlineStringCell(worksheet, "D4", "Adjacent content");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);

            var relationshipNamespace = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            var workbookRelationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
            workbookRelationships.Root!.Add(new XElement(
                relationshipNamespace + "Relationship",
                new XAttribute("Id", "rIdCalcChain"),
                new XAttribute(
                    "Type",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain"),
                new XAttribute("Target", "calcChain.xml")));
            ReplaceXml(archive, "xl/_rels/workbook.xml.rels", workbookRelationships);

            var spreadsheetNamespace = worksheet.Root.Name.Namespace;
            ReplaceXml(
                archive,
                "xl/calcChain.xml",
                new XDocument(
                    new XElement(
                        spreadsheetNamespace + "calcChain",
                        new XElement(
                            spreadsheetNamespace + "c",
                            new XAttribute("r", "B3"),
                            new XAttribute("i", "2")))));

            var contentTypeNamespace = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
            ReplaceXml(
                archive,
                "[Content_Types].xml",
                new XDocument(
                    new XElement(
                        contentTypeNamespace + "Types",
                        new XElement(
                            contentTypeNamespace + "Override",
                            new XAttribute("PartName", "/xl/calcChain.xml"),
                            new XAttribute(
                                "ContentType",
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml")))));
        }

        PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValuesV2(
            workbook,
            [
                new PortableLogbookWorkbookRowV2(
                    new EntryId("ent_1"),
                    new RevisionId("rev_1"),
                    PortableLogbookWorkbookEntry.Empty with { Year = 2024 }),
                new PortableLogbookWorkbookRowV2(
                    new EntryId("ent_2"),
                    new RevisionId("rev_2"),
                    PortableLogbookWorkbookEntry.Empty with { Year = 2025 }),
                new PortableLogbookWorkbookRowV2(
                    new EntryId("ent_3"),
                    new RevisionId("rev_3"),
                    PortableLogbookWorkbookEntry.Empty with { Year = 2026 })
            ]);

        using var readArchive = ZipFile.OpenRead(workbook);
        var tableAfter = ReadXml(readArchive, "xl/tables/table1.xml");
        Assert.Equal("A1:C5", (string?)tableAfter.Root!.Attribute("ref"));
        Assert.Equal(
            "A1:C4",
            (string?)tableAfter.Root.Elements().Single(element => element.Name.LocalName == "autoFilter").Attribute("ref"));

        var worksheetAfter = ReadXml(readArchive, "xl/worksheets/sheet2.xml");
        Assert.Equal("ent_2", ReadInlineStringCell(worksheetAfter, "A3"));
        Assert.Equal("2025", ReadInlineStringCell(worksheetAfter, "B3"));
        Assert.DoesNotContain(
            worksheetAfter.Descendants().Where(element => element.Name.LocalName == "c"),
            element =>
                (string?)element.Attribute("r") == "B3" &&
                element.Elements().Any(child => child.Name.LocalName == "f"));
        Assert.Equal("Totals", ReadInlineStringCell(worksheetAfter, "A5"));
        Assert.Equal(
            "SUBTOTAL(109,B2:B2)",
            worksheetAfter.Descendants().Single(element =>
                element.Name.LocalName == "c" && (string?)element.Attribute("r") == "B5")
                .Elements().Single(element => element.Name.LocalName == "f").Value);
        Assert.Equal("Grand Total", ReadInlineStringCell(worksheetAfter, "A6"));
        Assert.Equal(
            "SUM(Table1[#Totals])",
            worksheetAfter.Descendants().Single(element =>
                element.Name.LocalName == "c" && (string?)element.Attribute("r") == "B6")
                .Elements().Single(element => element.Name.LocalName == "f").Value);
        Assert.Equal("Adjacent content", ReadInlineStringCell(worksheetAfter, "D4"));

        Assert.Null(readArchive.GetEntry("xl/calcChain.xml"));
        var relationshipsAfter = ReadXml(readArchive, "xl/_rels/workbook.xml.rels");
        Assert.DoesNotContain(
            relationshipsAfter.Root!.Elements().Where(element => element.Name.LocalName == "Relationship"),
            element => ((string?)element.Attribute("Type"))?.EndsWith("/calcChain", StringComparison.OrdinalIgnoreCase) == true);
        var contentTypesAfter = ReadXml(readArchive, "[Content_Types].xml");
        Assert.DoesNotContain(
            contentTypesAfter.Root!.Elements().Where(element => element.Name.LocalName == "Override"),
            element => string.Equals(
                (string?)element.Attribute("PartName"),
                "/xl/calcChain.xml",
                StringComparison.OrdinalIgnoreCase));
        var workbookAfter = ReadXml(readArchive, "xl/workbook.xml");
        var calculationProperties = workbookAfter.Root!.Elements()
            .Single(element => element.Name.LocalName == "calcPr");
        Assert.Equal("auto", (string?)calculationProperties.Attribute("calcMode"));
        Assert.Equal("1", (string?)calculationProperties.Attribute("fullCalcOnLoad"));
        Assert.Equal("1", (string?)calculationProperties.Attribute("forceFullCalc"));
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
                    "EntryID",
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
    public void WriteReadCurrentRowsV2RoundTripsEveryWorkbookFaithfulField()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var customFields = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(
            ["Custom 1", "Custom 2", "Custom 3", "Custom 4"]);
        var columns = PortableLogbookWorkbookFieldCatalog.PilotEnteredColumnNames
            .Concat([
                PortableLogbookWorkbookMetadata.HiddenLogbookColumns[0].WorkbookColumnName,
                PortableLogbookWorkbookMetadata.HiddenLogbookColumns[1].WorkbookColumnName
            ])
            .ToArray();
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(archive, "A1:AT2", columns);
        }

        var entry = CompleteWorkbookEntry();
        var writeResult = PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValuesV2(
            workbook,
            [new PortableLogbookWorkbookRowV2(new EntryId("ent_v2_all"), new RevisionId("rev_v2_all"), entry)],
            customFields);
        var rows = PortableLogbookWorkbookPackageStorage.ReadCurrentRowsV2(workbook, customFields);

        Assert.Equal([45, 46], writeResult.AbsoluteMetadataColumnIndexes);
        using (var readArchive = ZipFile.OpenRead(workbook))
        {
            var worksheet = ReadXml(readArchive, "xl/worksheets/sheet2.xml");
            Assert.Equal("ent_v2_all", ReadInlineStringCell(worksheet, "AS2"));
            Assert.Equal("rev_v2_all", ReadInlineStringCell(worksheet, "AT2"));
        }

        var row = Assert.Single(rows);
        Assert.Equal(new EntryId("ent_v2_all"), row.EntryId);
        Assert.Equal(new RevisionId("rev_v2_all"), row.CurrentRevisionId);
        Assert.Equal(
            PortableLogbookWorkbookEntryFields.ToFieldValues(entry),
            PortableLogbookWorkbookEntryFields.ToFieldValues(row.Entry));
        var fieldIds = PortableLogbookWorkbookEntryFields.ToFieldValues(row.Entry).Keys.ToArray();
        Assert.DoesNotContain(fieldIds, fieldId => fieldId is
            "aircraftType" or
            "registration" or
            "flightNumber" or
            "multiPilot" or
            "pilotInCommand" or
            "coPilot" or
            "dual" or
            "instructor" or
            "day" or
            "night" or
            "takeoffsDay" or
            "takeoffsNight" or
            "ifrApproaches" or
            "holding");
    }

    [Fact]
    public void ReadCurrentRowsForInspectionV2CountsUserRowsThatCannotBecomeFlights()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var customFields = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(
            ["Custom 1", "Custom 2", "Custom 3", "Custom 4"]);
        var columns = PortableLogbookWorkbookFieldCatalog.PilotEnteredColumnNames
            .Concat([
                PortableLogbookWorkbookMetadata.HiddenLogbookColumns[0].WorkbookColumnName,
                PortableLogbookWorkbookMetadata.HiddenLogbookColumns[1].WorkbookColumnName
            ])
            .ToArray();
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(archive, "A1:AT2", columns);
        }

        PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValuesV2(
            workbook,
            [new PortableLogbookWorkbookRowV2(new EntryId("ent_valid"), new RevisionId("rev_valid"), CompleteWorkbookEntry())],
            customFields);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(archive, "A1:AT3", columns);
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "A3", "2026");
            UpsertInlineStringCell(worksheet, "B3", "8");
            UpsertInlineStringCell(worksheet, "C3", "29");
            UpsertInlineStringCell(worksheet, "D3", "C172");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var inspection = PortableLogbookWorkbookPackageStorage.ReadCurrentRowsForInspectionV2(
            workbook,
            customFields);

        Assert.Single(inspection.Rows);
        Assert.Equal(2, inspection.UserDataRowCount);
        Assert.Equal(1, inspection.UnrecognizedUserDataRowCount);
    }

    [Fact]
    public void ReadWorkbookCustomFieldDefinitionsPreservesNamedWorkbookHeaders()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var columns = new[]
            {
                PortableLogbookWorkbookFieldCatalog.EntryIdColumnName,
                PortableLogbookWorkbookFieldCatalog.CalculatedProjectionColumnNames[0]
            }
            .Concat(PortableLogbookWorkbookFieldCatalog.PilotEnteredColumnNames)
            .ToArray();
        var labels = new[] { "Operation", "Training course", "Client", "Notes" };
        for (var index = 0; index < labels.Length; index++)
        {
            columns[17 + index] = labels[index];
        }

        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(archive, "A1:AT2", columns);
        }

        var customFields = PortableLogbookWorkbookPackageStorage.ReadWorkbookCustomFieldDefinitions(workbook);

        Assert.Equal(labels, customFields.Select(field => field.Label));
        Assert.Equal(["cf_workbook_1", "cf_workbook_2", "cf_workbook_3", "cf_workbook_4"], customFields.Select(field => field.Id.Value));
    }

    [Fact]
    public void ReadCurrentRowsV2AcceptsNamedMonthFromWorkbook()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var customFields = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(
            ["Custom 1", "Custom 2", "Custom 3", "Custom 4"]);
        var columns = PortableLogbookWorkbookFieldCatalog.PilotEnteredColumnNames
            .Concat([
                PortableLogbookWorkbookMetadata.HiddenLogbookColumns[0].WorkbookColumnName,
                PortableLogbookWorkbookMetadata.HiddenLogbookColumns[1].WorkbookColumnName
            ])
            .ToArray();
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(archive, "A1:AT2", columns);
        }

        PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValuesV2(
            workbook,
            [new PortableLogbookWorkbookRowV2(new EntryId("ent_named_month"), new RevisionId("rev_named_month"), CompleteWorkbookEntry())],
            customFields);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "B2", "Jan");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var row = Assert.Single(PortableLogbookWorkbookPackageStorage.ReadCurrentRowsV2(workbook, customFields));

        Assert.Equal(1, row.Entry.Month);
    }

    [Fact]
    public void ReadCurrentRowsV2AcceptsExcelDateSerialMonthFromWorkbook()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var customFields = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(
            ["Custom 1", "Custom 2", "Custom 3", "Custom 4"]);
        var columns = PortableLogbookWorkbookFieldCatalog.PilotEnteredColumnNames
            .Concat([
                PortableLogbookWorkbookMetadata.HiddenLogbookColumns[0].WorkbookColumnName,
                PortableLogbookWorkbookMetadata.HiddenLogbookColumns[1].WorkbookColumnName
            ])
            .ToArray();
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(archive, "A1:AT2", columns);
        }

        PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValuesV2(
            workbook,
            [new PortableLogbookWorkbookRowV2(new EntryId("ent_serial_month"), new RevisionId("rev_serial_month"), CompleteWorkbookEntry())],
            customFields);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "B2", "44772");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var row = Assert.Single(PortableLogbookWorkbookPackageStorage.ReadCurrentRowsV2(workbook, customFields));

        Assert.Equal(7, row.Entry.Month);
    }

    [Fact]
    public void ReadCurrentRowsV2AcceptsExcelDateSerialDayFromWorkbook()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        var customFields = PortableLogbookCustomFieldSet.CreateWorkbookCustomFields(
            ["Custom 1", "Custom 2", "Custom 3", "Custom 4"]);
        var columns = PortableLogbookWorkbookFieldCatalog.PilotEnteredColumnNames
            .Concat([
                PortableLogbookWorkbookMetadata.HiddenLogbookColumns[0].WorkbookColumnName,
                PortableLogbookWorkbookMetadata.HiddenLogbookColumns[1].WorkbookColumnName
            ])
            .ToArray();
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(archive, "A1:AT2", columns);
        }

        PortableLogbookWorkbookPackageStorage.WriteHiddenMetadataColumnValuesV2(
            workbook,
            [new PortableLogbookWorkbookRowV2(new EntryId("ent_serial_day"), new RevisionId("rev_serial_day"), CompleteWorkbookEntry())],
            customFields);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "C2", "44772");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var row = Assert.Single(PortableLogbookWorkbookPackageStorage.ReadCurrentRowsV2(workbook, customFields));

        Assert.Equal(30, row.Entry.Day);
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
        Assert.Equal("EntryID", ReadInlineStringCell(worksheet, "D5"));
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
                    "EntryID",
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
    public void ReadCurrentRowsMapsMasterWorkbookColumnAliases()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:I2",
                ["Date", "Type", "Reg", "Flight ID", "From", "To", "Via", "Remarks", "IfrSim"]);
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "A2", "2026-07-20");
            UpsertInlineStringCell(worksheet, "B2", "A320");
            UpsertInlineStringCell(worksheet, "C2", "VH-ALS");
            UpsertInlineStringCell(worksheet, "D2", "QF123");
            UpsertInlineStringCell(worksheet, "E2", "YSBK");
            UpsertInlineStringCell(worksheet, "F2", "YMML");
            UpsertInlineStringCell(worksheet, "G2", "DCT");
            UpsertInlineStringCell(worksheet, "H2", "Alias row");
            UpsertInlineStringCell(worksheet, "I2", "1.1");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var rows = PortableLogbookWorkbookPackageStorage.ReadCurrentRows(workbook);

        var row = Assert.Single(rows);
        Assert.Equal("A320", row.Entry.AircraftType);
        Assert.Equal("VH-ALS", row.Entry.Registration);
        Assert.Equal("QF123", row.Entry.FlightNumber);
        Assert.Equal("DCT", row.Entry.Route);
        Assert.Equal("Alias row", row.Entry.Details);
        Assert.Equal(1.1m, row.Entry.InstrumentSimulated);
    }

    [Fact]
    public void ReadCurrentRowsKeepsWorkbookRemarksSeparateFromCrewColumns()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:K2",
                [
                    "Date",
                    "Type",
                    "Reg",
                    "From",
                    "To",
                    "Remarks",
                    "PIC",
                    "Other Pilot or Crew",
                    "SeCommandDay",
                    "EntryID",
                    "Portable Current Revision ID"
                ]);
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "A2", "2026-07-20");
            UpsertInlineStringCell(worksheet, "B2", "A320");
            UpsertInlineStringCell(worksheet, "C2", "VH-ALS");
            UpsertInlineStringCell(worksheet, "D2", "YSBK");
            UpsertInlineStringCell(worksheet, "E2", "YMML");
            UpsertInlineStringCell(worksheet, "F2", "x3 CCTS PIPS");
            UpsertInlineStringCell(worksheet, "G2", "Self");
            UpsertInlineStringCell(worksheet, "H2", "Training captain");
            UpsertInlineStringCell(worksheet, "I2", "1.2");
            UpsertInlineStringCell(worksheet, "J2", "ent_crew");
            UpsertInlineStringCell(worksheet, "K2", "rev_crew");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var rows = PortableLogbookWorkbookPackageStorage.ReadCurrentRows(workbook);

        var row = Assert.Single(rows);
        Assert.Equal("x3 CCTS PIPS", row.Entry.Details);
        Assert.Equal(1.2m, row.Entry.PilotInCommand);
        Assert.DoesNotContain("PIC:", row.Entry.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("Crew:", row.Entry.Details, StringComparison.Ordinal);
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
                ["Date", "EntryID", "Portable Current Revision ID"]);
        }

        var rows = PortableLogbookWorkbookPackageStorage.ReadCurrentRows(workbook);

        Assert.Empty(rows);
    }

    [Fact]
    public void ReadCurrentRowsSkipsSummaryRowsWithoutFlightIdentity()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:B2",
                ["Date", "PIC"]);
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "B2", "Grand Total Flying Hours");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var rows = PortableLogbookWorkbookPackageStorage.ReadCurrentRows(workbook);

        Assert.Empty(rows);
    }

    [Fact]
    public void ReadCurrentRowsSkipsPlaceholderRowsWithoutFlightIdentityOrTime()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:C2",
                ["Date", "Via", "PIC"]);
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "A2", "2000-01-01");
            UpsertInlineStringCell(worksheet, "B2", "Do not delete these placeholder entries until");
            UpsertInlineStringCell(worksheet, "C2", "0");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
        }

        var rows = PortableLogbookWorkbookPackageStorage.ReadCurrentRows(workbook);

        Assert.Empty(rows);
    }

    [Fact]
    public void ReadCurrentRowsSkipsTemplatePlaceholderRowsWithoutAircraftOrLoggedTime()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            ReplaceLogbookTable(
                archive,
                "A1:F2",
                ["Date", "Aircraft Type", "Reg", "From", "To", "PIC"]);
            var worksheet = ReadXml(archive, "xl/worksheets/sheet2.xml");
            UpsertInlineStringCell(worksheet, "A2", "2000-01-01");
            ReplaceXml(archive, "xl/worksheets/sheet2.xml", worksheet);
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
    public void EnsureWorkbookIdentityMetadataUsesExistingWorkbookMetadataSheetAndColumn()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            var workbookXml = ReadXml(archive, "xl/workbook.xml");
            var backendSheet = workbookXml
                .Descendants()
                .Single(element => element.Name.LocalName == "sheet" && (string?)element.Attribute("name") == "Backend");
            backendSheet.SetAttributeValue("name", "Admin");
            SetDefinedName(workbookXml, "LogbookVersion", "Admin!$C$4");
            SetDefinedName(workbookXml, "suppressWarningsUntil", "Admin!$C$5");
            SetDefinedName(workbookXml, "DateAfterExport", "Admin!$C$7");
            SetDefinedName(workbookXml, "RoutesBuilt", "Admin!$C$8");
            SetDefinedName(workbookXml, "GitHubBranch", "Admin!$C$13");
            ReplaceXml(archive, "xl/workbook.xml", workbookXml);

            var admin = ReadXml(archive, "xl/worksheets/sheet1.xml");
            UpsertInlineStringCell(admin, "C4", TestRepo.Version);
            ReplaceXml(archive, "xl/worksheets/sheet1.xml", admin);
        }

        var result = PortableLogbookWorkbookPackageStorage.EnsureWorkbookIdentityMetadata(
            workbook,
            new LogbookId("log_admin_identity"),
            new DeviceId("dev_admin_identity"),
            PortableLogbookDocument.CurrentSchemaVersion);

        Assert.Equal(["C14", "C15", "C16"], result.CellsWritten);
        using var readArchive = ZipFile.OpenRead(workbook);
        var workbookAfter = ReadXml(readArchive, "xl/workbook.xml");
        AssertDefinedName(workbookAfter, PortableLogbookWorkbookMetadata.LogbookIdName, "'Admin'!$C$14");
        AssertDefinedName(workbookAfter, PortableLogbookWorkbookMetadata.DeviceIdName, "'Admin'!$C$15");
        AssertDefinedName(workbookAfter, PortableLogbookWorkbookMetadata.SchemaVersionName, "'Admin'!$C$16");

        var adminAfter = ReadXml(readArchive, "xl/worksheets/sheet1.xml");
        Assert.Equal("log_admin_identity", ReadInlineStringCell(adminAfter, "C14"));
        Assert.Equal("dev_admin_identity", ReadInlineStringCell(adminAfter, "C15"));
        Assert.Equal(PortableLogbookDocument.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), ReadInlineStringCell(adminAfter, "C16"));
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
    public void ReadCurrencyOverrideDatesReadsWorkbookDefinedNameDates()
    {
        var workbook = TestRepo.CreateMinimalWorkbookPackage(directory, TestRepo.Version);
        using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Update))
        {
            var workbookXml = ReadXml(archive, "xl/workbook.xml");
            var root = workbookXml.Root ?? throw new InvalidOperationException("Workbook XML is invalid.");
            var ns = root.Name.Namespace;
            var definedNames = root.Element(ns + "definedNames") ?? new XElement(ns + "definedNames");
            if (definedNames.Parent is null)
            {
                root.Add(definedNames);
            }

            definedNames.Add(
                new XElement(ns + "definedName", new XAttribute("name", "FROverride"), "'Backend'!$A$20"),
                new XElement(ns + "definedName", new XAttribute("name", "IPCOverride"), "'Backend'!$A$21"),
                new XElement(ns + "definedName", new XAttribute("name", "OPCOverride"), "'Backend'!$A$22"));
            ReplaceXml(archive, "xl/workbook.xml", workbookXml);

            var backend = ReadXml(archive, "xl/worksheets/sheet1.xml");
            UpsertInlineStringCell(backend, "A20", "2026-07-01");
            UpsertInlineStringCell(backend, "A21", "46205");
            UpsertInlineStringCell(backend, "A22", "2026-07-03");
            ReplaceXml(archive, "xl/worksheets/sheet1.xml", backend);
        }

        var overrides = PortableLogbookWorkbookPackageStorage.ReadCurrencyOverrideDates(workbook);

        Assert.Equal(new DateOnly(2026, 7, 1), overrides.FlightReview);
        Assert.Equal(new DateOnly(2026, 7, 2), overrides.InstrumentProficiencyCheck);
        Assert.Equal(new DateOnly(2026, 7, 3), overrides.OperatorProficiencyCheck);
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

    private static PortableLogbookWorkbookStorageEnvelope CreateEnvelopeV2(string logbookId, PortableLogbookKey key)
    {
        var customFieldId = new CustomFieldId("cf_workbook_1");
        var create = PortableLogbookOperationV2.Create(
            new LogbookId(logbookId),
            new EntryId("ent_1"),
            new RevisionId("rev_1"),
            new DeviceId("dev_excel"),
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            PortableLogbookWorkbookEntry.Empty with
            {
                Year = 2026,
                Month = 7,
                Day = 18,
                Type = "DA40",
                Reg = "VH-ABC",
                From = "YSBK",
                To = "YSBK",
                FlightReview = true,
                CustomFields = new Dictionary<CustomFieldId, string?> { [customFieldId] = "Alpha" },
                SeCommandDay = 1.2m,
                IfrIf = 0.4m,
                Ils = 2
            });
        var document = PortableLogbookDocumentV2.CreateAustraliaFirst(
            create.LogbookId,
            [new CustomFieldDefinition(customFieldId, "Custom 1", 1)],
            PortableLogbookCurrencyOverrideDates.Empty,
            [create]);
        return PortableLogbookWorkbookStorage.CreateEnvelope(
            document,
            PortableLogbookPackage.Write(document, key),
            []);
    }

    private static PortableLogbookWorkbookEntry CompleteWorkbookEntry() =>
        PortableLogbookWorkbookEntry.Empty with
        {
            Year = 2026,
            Month = 7,
            Day = 24,
            Type = "DA40",
            Reg = "VH-V2A",
            FlightId = "ELB024",
            Pic = "A Delta",
            OtherPilotOrCrew = "B Example",
            From = "YSBK",
            To = "YSCN",
            Via = "BK CN",
            Remarks = "Workbook faithful row",
            FlightReview = true,
            InstrumentProficiencyCheck = false,
            OperatorProficiencyCheck = true,
            CustomFields = new Dictionary<CustomFieldId, string?>
            {
                [new("cf_workbook_1")] = "Alpha",
                [new("cf_workbook_2")] = "Bravo",
                [new("cf_workbook_3")] = "Charlie",
                [new("cf_workbook_4")] = "Delta"
            },
            SeIcusDay = 0.1m,
            SeIcusNight = 0.2m,
            SeDualDay = 0.3m,
            SeDualNight = 0.4m,
            SeCommandDay = 0.5m,
            SeCommandNight = 0.6m,
            MeIcusDay = 0.7m,
            MeIcusNight = 0.8m,
            MeDualDay = 0.9m,
            MeDualNight = 1.0m,
            MeCommandDay = 1.1m,
            MeCommandNight = 1.2m,
            CopilotDay = 1.3m,
            CopilotNight = 1.4m,
            IfrIf = 1.5m,
            IfrSim = 1.6m,
            LandingsDay = 2,
            LandingsNight = 3,
            Ils = 4,
            Vor = 5,
            Rnp = 6,
            Ndb = 7,
            DgaCdi = 8,
            DgaAzi = 9,
            Circling = 10
        };

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

    private static void AssertCellReferencesAscending(
        XDocument worksheet,
        int rowNumber,
        IReadOnlyList<string> expectedReferences)
    {
        var references = worksheet
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "row" &&
                (string?)element.Attribute("r") == rowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Elements()
            .Where(element => element.Name.LocalName == "c")
            .Select(element => (string?)element.Attribute("r"))
            .Where(reference => reference is not null)
            .Cast<string>()
            .ToArray();

        Assert.Equal(expectedReferences, references);
    }

    private static void SetDefinedName(XDocument workbook, string name, string reference)
    {
        var definedName = workbook
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "definedName" &&
                (string?)element.Attribute("name") == name);
        definedName.Value = reference;
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
