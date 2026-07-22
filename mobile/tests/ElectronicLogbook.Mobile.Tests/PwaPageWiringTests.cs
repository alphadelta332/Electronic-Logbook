namespace ElectronicLogbook.Mobile.Tests;

public sealed class PwaPageWiringTests
{
    [Fact]
    public void HomePageWiresUserFacingPackageExchangeControls()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("Set up key", page, StringComparison.Ordinal);
        Assert.Contains("Restore key", page, StringComparison.Ordinal);
        Assert.Contains("Recovery code", page, StringComparison.Ordinal);
        Assert.Contains("Preview package", page, StringComparison.Ordinal);
        Assert.Contains("Import package", page, StringComparison.Ordinal);
        Assert.Contains("Export package", page, StringComparison.Ordinal);
        Assert.Contains("Support summary", page, StringComparison.Ordinal);
        Assert.Contains("SetupPackageKeyAsync", page, StringComparison.Ordinal);
        Assert.Contains("RestorePackageKeyAsync", page, StringComparison.Ordinal);
        Assert.Contains("PackageKeyStore.ImportRecoveryCodeAsync", page, StringComparison.Ordinal);
        Assert.Contains("FindPackageKeyRestoreLogbookId", page, StringComparison.Ordinal);
        Assert.Contains("Document = PortableLogbookDocument.CreateAustraliaFirst(restoreLogbookId, CustomFields, []);", page, StringComparison.Ordinal);
        Assert.Contains("ImportCompatibility = ImportPlan is null", page, StringComparison.Ordinal);
        Assert.Contains("ApplyPackageAsync", page, StringComparison.Ordinal);
        Assert.Contains("ExportPackageAsync", page, StringComparison.Ordinal);
        Assert.Contains("ExportSupportSummaryAsync", page, StringComparison.Ordinal);
        Assert.Contains("MobileSupportSummaryExportWorkflow.ExportAsync", page, StringComparison.Ordinal);
        Assert.Contains("class=\"key-notice\"", page, StringComparison.Ordinal);
        Assert.Contains("MobilePackageKeyNotice.Create", page, StringComparison.Ordinal);
        Assert.Contains("ImportCompatibility is MobilePackageImportCompatibility.WrongLogbook or MobilePackageImportCompatibility.UnsupportedSchema", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Package import and export are pending secure exchange wiring.", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageWiresCustomFieldDefinitionResolutionControls()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("custom-field-conflicts", page, StringComparison.Ordinal);
        Assert.Contains("Keep local labels", page, StringComparison.Ordinal);
        Assert.Contains("Use imported labels", page, StringComparison.Ordinal);
        Assert.Contains("ResolveCustomFieldsAsync", page, StringComparison.Ordinal);
        Assert.Contains("ClearPendingImportPreview();", page, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCustomFieldDefinitionChoice.KeepLocal", page, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCustomFieldDefinitionChoice.UseIncoming", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageOffersRecoveryRestoreForEmptyWrongLogbookPreview()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("@if (CanRestorePackageKey)", page, StringComparison.Ordinal);
        Assert.Contains("private bool CanRestorePackageKey => PackageKeyStatus == \"Not set\" || CanRestorePreviewedLogbook;", page, StringComparison.Ordinal);
        Assert.Contains("private bool CanRestorePreviewedLogbook =>", page, StringComparison.Ordinal);
        Assert.Contains("ImportCompatibility == MobilePackageImportCompatibility.WrongLogbook", page, StringComparison.Ordinal);
        Assert.Contains("Document.Operations.Count == 0", page, StringComparison.Ordinal);
        Assert.Contains("ImportReceipts.Count == 0", page, StringComparison.Ordinal);
        Assert.Contains("Restore the workbook recovery code before importing this package.", page, StringComparison.Ordinal);
        Assert.Contains("Package import is unavailable until the workbook recovery code is restored for the previewed package.", page, StringComparison.Ordinal);
        Assert.Contains("exchange-blocked", page, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageShowsBusyFeedbackDuringPackageExchange()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("private bool IsExchangeBusy", page, StringComparison.Ordinal);
        Assert.Contains("private string ExchangeBusyMessage", page, StringComparison.Ordinal);
        Assert.Contains("exchange-busy", page, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", page, StringComparison.Ordinal);
        Assert.Contains("BeginExchangeActionAsync", page, StringComparison.Ordinal);
        Assert.Contains("EndExchangeAction();", page, StringComparison.Ordinal);
        Assert.Contains("Opening package picker...", page, StringComparison.Ordinal);
        Assert.Contains("Creating package key...", page, StringComparison.Ordinal);
        Assert.Contains("Restoring package key...", page, StringComparison.Ordinal);
        Assert.Contains("Importing package...", page, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(IsStorageBlocked || IsExchangeBusy)\"", page, StringComparison.Ordinal);
        Assert.Contains("Package exchange is already in progress.", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageReportsWhyDisabledImportCannotRun()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("private string? ImportUnavailableMessage =>", page, StringComparison.Ordinal);
        Assert.Contains("PackageExchangeError = ImportUnavailableMessage;", page, StringComparison.Ordinal);
        Assert.Contains("Package import is unavailable while local storage is blocked.", page, StringComparison.Ordinal);
        Assert.Contains("Package import is unavailable because this browser cannot hold the required package key.", page, StringComparison.Ordinal);
        Assert.Contains("Package import is unavailable until this device has a package key.", page, StringComparison.Ordinal);
        Assert.Contains("Package import is unavailable because this package belongs to a different logbook.", page, StringComparison.Ordinal);
        Assert.Contains("Package import is unavailable because this package uses an unsupported schema.", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageShowsSharedDraftValidationAfterDraftEditingStarts()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("@oninput=\"MarkDraftEdited\"", page, StringComparison.Ordinal);
        Assert.Contains("@onchange=\"MarkDraftEdited\"", page, StringComparison.Ordinal);
        Assert.Contains("ShouldShowDraftErrors", page, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageOffersRecentFlightNumberAndRouteSuggestions()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("list=\"flight-numbers\"", page, StringComparison.Ordinal);
        Assert.Contains("datalist id=\"flight-numbers\"", page, StringComparison.Ordinal);
        Assert.Contains("RecentValues(entry => entry.FlightNumber)", page, StringComparison.Ordinal);
        Assert.Contains("list=\"routes\"", page, StringComparison.Ordinal);
        Assert.Contains("datalist id=\"routes\"", page, StringComparison.Ordinal);
        Assert.Contains("RecentValues(entry => entry.Route)", page, StringComparison.Ordinal);
        Assert.Contains("Recent remarks", page, StringComparison.Ordinal);
        Assert.Contains("UseRecentRemark", page, StringComparison.Ordinal);
        Assert.Contains("RecentValues(entry => entry.Details)", page, StringComparison.Ordinal);
        Assert.Contains("RecentAirportValues()", page, StringComparison.Ordinal);
        Assert.Contains("MobileAirportSuggestions.Create", page, StringComparison.Ordinal);
        Assert.Contains("MobileRecentValues.Create", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageOffersDirectRouteShortcut()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("Direct", page, StringComparison.Ordinal);
        Assert.Contains("UseDirectRoute", page, StringComparison.Ordinal);
        Assert.True(
            page.IndexOf("input @bind=\"Draft.Route\"", StringComparison.Ordinal) <
            page.IndexOf("@onclick=\"UseDirectRoute\"", StringComparison.Ordinal));
        Assert.Contains("Draft.Route = string.Equals(from, to, StringComparison.OrdinalIgnoreCase)", page, StringComparison.Ordinal);
        Assert.Contains("? from", page, StringComparison.Ordinal);
        Assert.Contains(": $\"{from} {to}\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageUsesMobileFriendlyCodeInputHints()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Equal(6, CountOccurrences(page, "autocapitalize=\"characters\""));
        Assert.Equal(7, CountOccurrences(page, "spellcheck=\"false\""));
        Assert.Contains("class=\"recovery-code-input\"", page, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"off\" spellcheck=\"false\" placeholder=\"Recovery code\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageClearsRecoveryCodeDraftAfterRestoreAttempt()
    {
        var page = ReadMobilePage("Home.razor");
        var handler = ExtractMethodBody(page, "private async Task RestorePackageKeyAsync");

        Assert.Contains("finally", handler, StringComparison.Ordinal);
        Assert.Contains("RecoveryCodeDraft = string.Empty;", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageOnlySwitchesLogbookForRecoveryRestoreOnEmptyDeviceCopy()
    {
        var page = ReadMobilePage("Home.razor");
        var handler = ExtractMethodBody(page, "private LogbookId FindPackageKeyRestoreLogbookId");

        Assert.Contains("ImportCompatibility != MobilePackageImportCompatibility.WrongLogbook", handler, StringComparison.Ordinal);
        Assert.Contains("Document.Operations.Count > 0 || ImportReceipts.Count > 0", handler, StringComparison.Ordinal);
        Assert.Contains("Cannot switch logbooks after this device copy has local entries or package receipts.", handler, StringComparison.Ordinal);
        Assert.Contains("return ImportPlan.LogbookId;", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageOffersFlightTimeToDayHoursShortcut()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("Use flight time as day", page, StringComparison.Ordinal);
        Assert.Contains("UseFlightTimeAsDay", page, StringComparison.Ordinal);
        Assert.Contains("Draft.Day = Draft.FlightTime", page, StringComparison.Ordinal);
        Assert.Contains("Draft.Night = 0", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageShowsAutomaticCountTotals()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("Takeoffs", page, StringComparison.Ordinal);
        Assert.Contains("@Draft.TotalTakeoffs", page, StringComparison.Ordinal);
        Assert.Contains("Landings", page, StringComparison.Ordinal);
        Assert.Contains("@Draft.TotalLandings", page, StringComparison.Ordinal);
        Assert.Contains("Approaches", page, StringComparison.Ordinal);
        Assert.Contains("@Draft.TotalApproaches", page, StringComparison.Ordinal);
        Assert.Contains("public int TotalTakeoffs => TakeoffsDay.GetValueOrDefault() + TakeoffsNight.GetValueOrDefault();", page, StringComparison.Ordinal);
        Assert.Contains("public int TotalLandings => LandingsDay.GetValueOrDefault() + LandingsNight.GetValueOrDefault();", page, StringComparison.Ordinal);
        Assert.Contains("public int TotalApproaches =>", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageAppliesDefaultDestinationFromDraftDefaults()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("To = defaults.To", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageOffersTakeoffLandingMatchShortcut()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("Match takeoffs", page, StringComparison.Ordinal);
        Assert.Contains("MatchTakeoffsToLandings", page, StringComparison.Ordinal);
        Assert.Contains("Draft.TakeoffsDay = Draft.LandingsDay", page, StringComparison.Ordinal);
        Assert.Contains("Draft.TakeoffsNight = Draft.LandingsNight", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageClearsDraftWhenDeletingEntryBeingEdited()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("if (EditingEntryId == entry.EntryId)", page, StringComparison.Ordinal);
        Assert.Contains("ResetDraft();", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePagePreservesDateForEditingButUsesTodayForCloning()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("Draft = EntryDraft.FromEntry(entry, preserveDate: false);", page, StringComparison.Ordinal);
        Assert.Contains("Draft = EntryDraft.FromEntry(entry.Entry, preserveDate: true);", page, StringComparison.Ordinal);
        Assert.Contains("preserveDate && entry.Date is not null", page, StringComparison.Ordinal);
        Assert.Contains("? entry.Date.Value", page, StringComparison.Ordinal);
        Assert.Contains(": DateOnly.FromDateTime(DateTime.Today)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageShowsRowLevelImportSummariesAfterPackageRead()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("class=\"import-summary\"", page, StringComparison.Ordinal);
        Assert.Contains("ImportExchangePlan.Preview.NewOperationSummaries", page, StringComparison.Ordinal);
        Assert.Contains("ImportExchangePlan.Preview.DuplicateOperationSummaries", page, StringComparison.Ordinal);
        Assert.Contains("FormatImportSummary", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageShowsCurrentRecordRawAndCustomFieldDetails()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("class=\"entry-details\"", page, StringComparison.Ordinal);
        Assert.Contains("EntryDetails(entry.Entry)", page, StringComparison.Ordinal);
        Assert.Contains("yield return new(\"Flight number\"", page, StringComparison.Ordinal);
        Assert.Contains("yield return new(\"Instrument actual\"", page, StringComparison.Ordinal);
        Assert.Contains("yield return new(\"RNP\"", page, StringComparison.Ordinal);
        Assert.Contains("foreach (var field in EntryCustomFields())", page, StringComparison.Ordinal);
        Assert.Contains("entry.CustomFields.TryGetValue(field.Id", page, StringComparison.Ordinal);
    }

    private static string ReadMobilePage(string relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ElectronicLogbook.Mobile",
            "Pages",
            relativePath)));

    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        var methodIndex = source.IndexOf(methodName, StringComparison.Ordinal);
        if (methodIndex < 0)
        {
            throw new InvalidOperationException($"Could not find method '{methodName}'.");
        }

        var openBraceIndex = source.IndexOf('{', methodIndex);
        if (openBraceIndex < 0)
        {
            throw new InvalidOperationException($"Could not find method body for '{methodName}'.");
        }

        var depth = 0;
        for (var index = openBraceIndex; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(openBraceIndex, index - openBraceIndex + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not find end of method body for '{methodName}'.");
    }
}
