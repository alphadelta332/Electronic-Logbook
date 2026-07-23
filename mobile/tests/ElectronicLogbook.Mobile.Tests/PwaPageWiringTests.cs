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

    [Fact]
    public void Gate3ShellUsesDashboardLogbookAndSettingsNavigationOnly()
    {
        var layout = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ElectronicLogbook.Mobile",
            "Layout",
            "MainLayout.razor")));

        Assert.Contains("href=\"/\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/flights\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/settings\"", layout, StringComparison.Ordinal);
        Assert.Contains("<span>Dashboard</span>", layout, StringComparison.Ordinal);
        Assert.Contains("<span>Logbook</span>", layout, StringComparison.Ordinal);
        Assert.Contains("<span>Settings</span>", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/flights/new\"", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>New</span>", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>Exchange</span>", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("MudMenu", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3SettingsOwnsAppearanceChoices()
    {
        var layout = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ElectronicLogbook.Mobile",
            "Layout",
            "MainLayout.razor")));
        var settings = ReadMobilePage("Settings.razor");
        var program = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ElectronicLogbook.Mobile",
            "Program.cs")));

        Assert.Contains("MobileUiPreferenceState", layout, StringComparison.Ordinal);
        Assert.Contains("UiPreferences.IsDarkMode", layout, StringComparison.Ordinal);
        Assert.Contains("Appearance", settings, StringComparison.Ordinal);
        Assert.Contains("ThemeButtonClass(\"System\")", settings, StringComparison.Ordinal);
        Assert.Contains("ThemeButtonClass(\"Light\")", settings, StringComparison.Ordinal);
        Assert.Contains("ThemeButtonClass(\"Dark\")", settings, StringComparison.Ordinal);
        Assert.Contains("SetSystemThemeModeAsync", settings, StringComparison.Ordinal);
        Assert.Contains("UiPreferences.SetThemeModeAsync(\"System\")", settings, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddScoped<MobileUiPreferenceState>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3SettingsExposesSupportSummaryExport()
    {
        var settings = ReadMobilePage("Settings.razor");

        Assert.Contains("@inject BrowserFileStore FileStore", settings, StringComparison.Ordinal);
        Assert.Contains("Support summary", settings, StringComparison.Ordinal);
        Assert.Contains("ExportSupportSummaryAsync", settings, StringComparison.Ordinal);
        Assert.Contains("MobileSupportSummaryExportWorkflow.ExportAsync", settings, StringComparison.Ordinal);
        Assert.Contains("Session.Document", settings, StringComparison.Ordinal);
        Assert.Contains("SupportSummaryMessage", settings, StringComparison.Ordinal);
        Assert.Contains("SupportSummaryError", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3LogbookUsesBalancedEntryRowsAndSeparateTotalsView()
    {
        var page = ReadMobilePage("Logbook.razor");

        Assert.Contains("@page \"/flights\"", page, StringComparison.Ordinal);
        Assert.Contains("Entries", page, StringComparison.Ordinal);
        Assert.Contains("Totals", page, StringComparison.Ordinal);
        Assert.Contains("logbook-entry-row", page, StringComparison.Ordinal);
        Assert.Contains("Session.FormatRegistration(entry.Entry!)", page, StringComparison.Ordinal);
        Assert.Contains("Session.FormatRoute(entry.Entry!)", page, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookEntryRules.LoggedTime(entry.Entry!)", page, StringComparison.Ordinal);
        Assert.Contains("Total hours", page, StringComparison.Ordinal);
        Assert.Contains("filter-sheet", page, StringComparison.Ordinal);
        Assert.Contains("FilterRecentOnly", page, StringComparison.Ordinal);
        Assert.Contains("FilterFlightsWithApproachesOnly", page, StringComparison.Ordinal);
        Assert.Contains("Deletion history", page, StringComparison.Ordinal);
        Assert.Contains("Session.DeletedEntries", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3DashboardShowsLastFlightTotalHoursAndNinetyDaySnapshot()
    {
        var page = ReadMobilePage("Dashboard.razor");

        Assert.Contains("Total hours", page, StringComparison.Ordinal);
        Assert.Contains("Last 90 days", page, StringComparison.Ordinal);
        Assert.Contains("dashboard-last-flight", page, StringComparison.Ordinal);
        Assert.Contains("LastFlightTitle", page, StringComparison.Ordinal);
        Assert.Contains("LastFlightDetail", page, StringComparison.Ordinal);
        Assert.Contains("RecentSnapshotDetail", page, StringComparison.Ordinal);
        Assert.Contains("TotalFlyingHours", page, StringComparison.Ordinal);
        Assert.Contains("RecentCutoff", page, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookEntryRules.LoggedTime", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3DashboardRecordHealthGivesExplicitReasonWithoutPilotRules()
    {
        var page = ReadMobilePage("Dashboard.razor");

        Assert.Contains("dashboard-record-health", page, StringComparison.Ordinal);
        Assert.Contains("RecordHealthTitle", page, StringComparison.Ordinal);
        Assert.Contains("RecordHealthReason", page, StringComparison.Ordinal);
        Assert.Contains("Session.MergeResult.Conflicts.Count", page, StringComparison.Ordinal);
        Assert.Contains("Last dated entry is", page, StringComparison.Ordinal);
        Assert.Contains("within the last 90 days", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Part 61", page, StringComparison.Ordinal);
        Assert.DoesNotContain("CASR", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3FlightDetailIsReadFirstWithEditHistoryAndDeleteActions()
    {
        var page = ReadMobilePage("FlightDetail.razor");

        Assert.Contains("@page \"/flights/{EntryId}\"", page, StringComparison.Ordinal);
        Assert.Contains("Read first", page, StringComparison.Ordinal);
        Assert.Contains("Edit entry", page, StringComparison.Ordinal);
        Assert.Contains("Immutable history", page, StringComparison.Ordinal);
        Assert.Contains("Session.EntryDetails(CurrentEntry.Entry)", page, StringComparison.Ordinal);
        Assert.Contains("Session.DeleteEntryAsync(CurrentEntry)", page, StringComparison.Ordinal);
        Assert.Contains("History?.IsDeleted == true", page, StringComparison.Ordinal);
        Assert.Contains("Deleted entry", page, StringComparison.Ordinal);
        Assert.Contains("Deletion history", page, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo($\"/flights/{CurrentEntry.EntryId.Value}/edit\")", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3NewFlightRouteMapSupportsDedicatedEditRoute()
    {
        var page = ReadMobilePage("NewFlight.razor");

        Assert.Contains("@page \"/flights/new\"", page, StringComparison.Ordinal);
        Assert.Contains("@page \"/flights/{EntryId}/edit\"", page, StringComparison.Ordinal);
        Assert.Contains("public string? EntryId { get; set; }", page, StringComparison.Ordinal);
        Assert.Contains("LoadEditRoute", page, StringComparison.Ordinal);
        Assert.Contains("Session.FindCurrentEntry(EntryId)", page, StringComparison.Ordinal);
        Assert.Contains("Session.EditEntry(entry)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3NewFlightShowsReducedMotionAwareSaveConfirmationBeforeDashboardReturn()
    {
        var page = ReadMobilePage("NewFlight.razor");
        var css = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ElectronicLogbook.Mobile",
            "wwwroot",
            "css",
            "app.css")));

        Assert.Contains("ShowSaveConfirmation", page, StringComparison.Ordinal);
        Assert.Contains("class=\"save-confirmation\"", page, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"assertive\"", page, StringComparison.Ordinal);
        Assert.Contains("Icons.Material.Filled.Check", page, StringComparison.Ordinal);
        Assert.Contains("await Task.Delay(650);", page, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo(\"/\");", page, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
        Assert.Contains(".save-confirmation-icon", css, StringComparison.Ordinal);
        Assert.Contains("animation: none;", css, StringComparison.Ordinal);
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
