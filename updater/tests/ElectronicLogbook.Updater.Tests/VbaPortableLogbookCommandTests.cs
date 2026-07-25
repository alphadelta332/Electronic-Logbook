namespace ElectronicLogbook.Updater.Tests;

public sealed class VbaPortableLogbookCommandTests
{
    [Fact]
    public void ModLogbookExposesPortableStatusCommand()
    {
        var source = ReadModLogbookSource();

        Assert.Contains("Public Sub ShowPortableLogbookStatus()", source, StringComparison.Ordinal);
        Assert.Contains("PORTABLE_LOGBOOK_UPDATER_EXE_NAME", source, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookUpdaterPath", source, StringComparison.Ordinal);
        Assert.Contains("RunPortableLogbookCommand(", source, StringComparison.Ordinal);
        Assert.Contains("\"status\"", source, StringComparison.Ordinal);
        Assert.Contains("QuoteCommandArgument(ThisWorkbook.FullName)", source, StringComparison.Ordinal);
        Assert.Contains("CreateObject(\"WScript.Shell\")", source, StringComparison.Ordinal);
        Assert.Contains("Run(commandLine, 0, True)", source, StringComparison.Ordinal);
        Assert.Contains("BuildUserFacingErrorMessage(", source, StringComparison.Ordinal);
        Assert.Contains("PORTABLE-STATUS-E001", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModLogbookExposesPortablePackageActionCommands()
    {
        var source = ReadModLogbookSource();

        Assert.Contains("Public Sub EnablePortableLogbook()", source, StringComparison.Ordinal);
        Assert.Contains("Public Sub ExportPortableLogbookPackage()", source, StringComparison.Ordinal);
        Assert.Contains("Public Sub PreviewPortableLogbookPackageImport()", source, StringComparison.Ordinal);
        Assert.Contains("Public Sub ImportPortableLogbookPackage()", source, StringComparison.Ordinal);
        Assert.Contains("Public Sub ProducePortableLogbookPrintedCopy()", source, StringComparison.Ordinal);
        Assert.Contains("Public Sub ViewPortableLogbookRevisionHistory()", source, StringComparison.Ordinal);
        Assert.Contains("Public Sub ResolvePortableLogbookConflict()", source, StringComparison.Ordinal);
        Assert.Contains("\"enable\"", source, StringComparison.Ordinal);
        Assert.Contains("\"export\"", source, StringComparison.Ordinal);
        Assert.Contains("\"import-preview\"", source, StringComparison.Ordinal);
        Assert.Contains("\"import-apply\"", source, StringComparison.Ordinal);
        Assert.Contains("\"printed-copy\"", source, StringComparison.Ordinal);
        Assert.Contains("\"revision-history\"", source, StringComparison.Ordinal);
        Assert.Contains("\"resolve-conflict\"", source, StringComparison.Ordinal);
        Assert.Contains("--recovery-output", source, StringComparison.Ordinal);
        Assert.Contains("--recovery-code-file", source, StringComparison.Ordinal);
        Assert.Contains("--package", source, StringComparison.Ordinal);
        Assert.Contains("--output", source, StringComparison.Ordinal);
        Assert.Contains("--holder-name", source, StringComparison.Ordinal);
        Assert.Contains("--holder-date-of-birth", source, StringComparison.Ordinal);
        Assert.Contains("--entry-id", source, StringComparison.Ordinal);
        Assert.Contains("--revision-id", source, StringComparison.Ordinal);
        Assert.Contains("--note", source, StringComparison.Ordinal);
        Assert.Contains("ChoosePortablePrintedCopyOutputPath", source, StringComparison.Ordinal);
        Assert.Contains("IsIsoDateOnly", source, StringComparison.Ordinal);
        Assert.Contains("Apply this package to the workbook portable storage?", source, StringComparison.Ordinal);
        Assert.Contains("PORTABLE-ENABLE-E001", source, StringComparison.Ordinal);
        Assert.Contains("PORTABLE-EXPORT-E001", source, StringComparison.Ordinal);
        Assert.Contains("PORTABLE-IMPORT-PREVIEW-E001", source, StringComparison.Ordinal);
        Assert.Contains("PORTABLE-IMPORT-APPLY-E001", source, StringComparison.Ordinal);
        Assert.Contains("PORTABLE-PRINTED-COPY-E001", source, StringComparison.Ordinal);
        Assert.Contains("PORTABLE-REVISION-HISTORY-E001", source, StringComparison.Ordinal);
        Assert.Contains("PORTABLE-RESOLVE-CONFLICT-E001", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewEntryCommandRepairAssignsPortableStatusButton()
    {
        var source = ReadModLogbookSource();

        Assert.Contains("portablelogbookstatus", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InStr(labelText, \"portable\")", source, StringComparison.Ordinal);
        Assert.Contains("InStr(labelText, \"status\")", source, StringComparison.Ordinal);
        Assert.Contains("actionName = \"ShowPortableLogbookStatus\"", source, StringComparison.Ordinal);
        Assert.Contains("enableportablelogbook", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actionName = \"EnablePortableLogbook\"", source, StringComparison.Ordinal);
        Assert.Contains("exportportablelogbookpackage", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actionName = \"ExportPortableLogbookPackage\"", source, StringComparison.Ordinal);
        Assert.Contains("previewportablelogbookpackageimport", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actionName = \"PreviewPortableLogbookPackageImport\"", source, StringComparison.Ordinal);
        Assert.Contains("importportablelogbookpackage", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actionName = \"ImportPortableLogbookPackage\"", source, StringComparison.Ordinal);
        Assert.Contains("produceportablelogbookprintedcopy", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actionName = \"ProducePortableLogbookPrintedCopy\"", source, StringComparison.Ordinal);
        Assert.Contains("viewportablelogbookrevisionhistory", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actionName = \"ViewPortableLogbookRevisionHistory\"", source, StringComparison.Ordinal);
        Assert.Contains("resolveportablelogbookconflict", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actionName = \"ResolvePortableLogbookConflict\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardLogbookExportDoesNotExposePortableMetadataColumns()
    {
        var source = ReadModLogbookSource();
        var buildColumnsBody = ExtractVbaProcedureBody(source, "BuildLogbookExportColumns", "End Sub");

        Assert.Contains("For columnIndex = customStart To customEnd", buildColumnsBody, StringComparison.Ordinal);
        Assert.Contains("IsPortableLogbookMetadataColumnName(sourceTable.ListColumns(columnIndex).Name)", buildColumnsBody, StringComparison.Ordinal);
        Assert.Contains("sourceIndexes.Add columnIndex", buildColumnsBody, StringComparison.Ordinal);
        Assert.Contains("Private Function IsPortableLogbookMetadataColumnName(ByVal columnName As String) As Boolean", source, StringComparison.Ordinal);
        Assert.Contains("normalized = \"portable entry id\" Or _", source, StringComparison.Ordinal);
        Assert.Contains("normalized = \"portable current revision id\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddLogbookExportColumn sourceTable, \"Portable Entry ID\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddLogbookExportColumn sourceTable, \"Portable Current Revision ID\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("outputHeaders.Add \"Portable Entry ID\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("outputHeaders.Add \"Portable Current Revision ID\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardCsvXlsxAndPdfExportsUseFilteredOutputValues()
    {
        var source = ReadModLogbookSource();
        var exportBody = ExtractVbaProcedureBody(source, "ExportLogbookToFile", "End Function");
        var csvBody = ExtractVbaProcedureBody(source, "WriteLogbookCsv", "End Sub");
        var formattedBody = ExtractVbaProcedureBody(source, "CreateFormattedLogbookExport", "End Function");

        Assert.Contains("BuildLogbookExportColumns sourceTable, selectedRows, combineDetails, _", exportBody, StringComparison.Ordinal);
        Assert.Contains("BuildLogbookExportValues(sourceTable, selectedRows, _", exportBody, StringComparison.Ordinal);
        Assert.Contains("WriteLogbookCsv outputPath, outputValues", exportBody, StringComparison.Ordinal);
        Assert.Contains("Set exportBook = CreateFormattedLogbookExport( _", exportBody, StringComparison.Ordinal);
        Assert.Contains("sourceSheet, sourceTable, outputValues, sourceIndexes, _", exportBody, StringComparison.Ordinal);
        Assert.Contains("exportFormat = \"xlsx\"", exportBody, StringComparison.Ordinal);
        Assert.Contains("ExportAsFixedFormat Type:=xlTypePDF", exportBody, StringComparison.Ordinal);

        Assert.Contains("Private Sub WriteLogbookCsv(ByVal outputPath As String, ByVal outputValues As Variant)", csvBody, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceTable", csvBody, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceSheet", csvBody, StringComparison.Ordinal);

        Assert.Contains("For sourceColumnIndex = sourceTable.ListColumns.Count To 1 Step -1", formattedBody, StringComparison.Ordinal);
        Assert.Contains("For Each outputIndex In sourceIndexes", formattedBody, StringComparison.Ordinal);
        Assert.Contains("If CLng(outputIndex) = sourceColumnIndex Then", formattedBody, StringComparison.Ordinal);
        Assert.Contains("If Not keepColumn Then", formattedBody, StringComparison.Ordinal);
        Assert.Contains("exportSheet.Columns(absoluteColumn).Delete", formattedBody, StringComparison.Ordinal);
        Assert.Contains("targetRange.Value2 = outputValues", formattedBody, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkbookNewEntryAssignsEntryIdBeforeFirstWorkbookSave()
    {
        var source = ReadModLogbookSource();
        var addToLogbookBody = ExtractVbaProcedureBody(source, "AddToLogbook", "End Sub");

        var addRowIndex = addToLogbookBody.IndexOf(
            "Set newRow = tbl.ListRows.Add(AlwaysInsert:=True)",
            StringComparison.Ordinal);
        var assignEntryIdIndex = addToLogbookBody.IndexOf(
            "AssignEntryIdToLogbookRowIfPresent newRow.Range, tbl",
            StringComparison.Ordinal);
        var saveIndex = addToLogbookBody.IndexOf("ThisWorkbook.Save", StringComparison.Ordinal);

        Assert.True(addRowIndex >= 0, "The New Entry append path should add a Logbook table row.");
        Assert.True(assignEntryIdIndex > addRowIndex, "The New Entry append path should assign EntryID after row allocation.");
        Assert.True(saveIndex > assignEntryIdIndex, "The New Entry append path should assign EntryID before workbook save.");
    }

    [Fact]
    public void LogTenAppendAssignsEntryIdBeforeWorkbookPersistence()
    {
        var source = ReadModLogbookSource();
        var importBody = ExtractVbaProcedureBody(source, "ImportFromLogTenFile", "End Function");
        var appendRowsBody = ExtractVbaProcedureBody(source, "AppendMappedLogTenRows", "End Sub");
        var appendRowBody = ExtractVbaProcedureBody(source, "AppendMappedLogTenRow", "End Sub");

        var appendCallIndex = importBody.IndexOf("AppendMappedLogTenRows tbl, rowsToImport", StringComparison.Ordinal);
        var saveIndex = importBody.IndexOf("ThisWorkbook.Save", StringComparison.Ordinal);
        Assert.True(appendCallIndex >= 0, "The LogTen import path should append mapped rows.");
        Assert.True(saveIndex > appendCallIndex, "The LogTen import path should append rows before workbook save.");

        var batchTargetIndex = appendRowsBody.IndexOf(
            "Set targetRow = tbl.DataBodyRange.Rows(originalRowCount + importIndex)",
            StringComparison.Ordinal);
        var batchAssignIndex = appendRowsBody.IndexOf(
            "AssignEntryIdToLogbookRowIfPresent targetRow, tbl",
            StringComparison.Ordinal);
        var batchWriteIndex = appendRowsBody.IndexOf("WriteMappedLogTenRow targetRow, tbl, mapped", StringComparison.Ordinal);

        Assert.True(batchAssignIndex > batchTargetIndex, "The LogTen batch append path should assign EntryID after selecting the appended row.");
        Assert.True(batchWriteIndex > batchAssignIndex, "The LogTen batch append path should assign EntryID before mapped row data is committed.");

        var singleAddIndex = appendRowBody.IndexOf("Set newRow = tbl.ListRows.Add(AlwaysInsert:=True)", StringComparison.Ordinal);
        var singleAssignIndex = appendRowBody.IndexOf(
            "AssignEntryIdToLogbookRowIfPresent newRow.Range, tbl",
            StringComparison.Ordinal);
        Assert.True(singleAssignIndex > singleAddIndex, "The single-row LogTen append helper should assign EntryID after row allocation.");

        var helperBody = ExtractVbaProcedureBody(source, "AssignEntryIdToLogbookRowIfPresent", "End Sub");
        Assert.Contains("If Not ListColumnExists(tbl, \"EntryID\") Then Exit Sub", helperBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Portable Entry ID", helperBody, StringComparison.Ordinal);
        Assert.Contains("entryIdCell.Value = NewPortableEntryId()", helperBody, StringComparison.Ordinal);
        Assert.Contains("CoCreateGuid(guid)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Scriptlet.TypeLib", source, StringComparison.Ordinal);
        Assert.Contains("NewPortableEntryId = \"ent_\" & LCase$(guidText)", source, StringComparison.Ordinal);
    }

    private static string ReadModLogbookSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "modLogbook.bas");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find modLogbook.bas from the test output directory.");
    }

    private static string ExtractVbaProcedureBody(string source, string procedureName, string terminator)
    {
        var procedureIndex = source.IndexOf(
            $"Private Sub {procedureName}(",
            StringComparison.Ordinal);
        if (procedureIndex < 0)
        {
            procedureIndex = source.IndexOf(
                $"Private Function {procedureName}(",
                StringComparison.Ordinal);
        }
        if (procedureIndex < 0)
        {
            procedureIndex = source.IndexOf(
                $"Public Sub {procedureName}(",
                StringComparison.Ordinal);
        }
        if (procedureIndex < 0)
        {
            procedureIndex = source.IndexOf(
                $"Public Function {procedureName}(",
                StringComparison.Ordinal);
        }
        if (procedureIndex < 0)
        {
            procedureIndex = source.IndexOf(
                $"Sub {procedureName}(",
                StringComparison.Ordinal);
        }
        if (procedureIndex < 0)
        {
            procedureIndex = source.IndexOf(
                $"Function {procedureName}(",
                StringComparison.Ordinal);
        }
        if (procedureIndex < 0)
        {
            throw new InvalidOperationException($"Could not find VBA procedure '{procedureName}'.");
        }

        var terminatorIndex = source.IndexOf(terminator, procedureIndex, StringComparison.Ordinal);
        if (terminatorIndex < 0)
        {
            throw new InvalidOperationException($"Could not find end of VBA procedure '{procedureName}'.");
        }

        return source.Substring(procedureIndex, terminatorIndex - procedureIndex + terminator.Length);
    }
}
