namespace ElectronicLogbook.Mobile.Tests;

public sealed class PwaPageWiringTests
{
    [Fact]
    public void UserFacingFlightPagesDoNotRenderSystemEntryIds()
    {
        var flightDetail = ReadMobilePage("FlightDetail.razor");
        var logbook = ReadMobilePage("Logbook.razor");
        var home = ReadMobilePage("Home.razor");

        Assert.DoesNotContain("<h1>@History.EntryId.Value</h1>", flightDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>@entry.EntryId.Value</strong>", logbook, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>@conflict.EntryId.Value</strong>", logbook, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>@conflict.EntryId.Value</strong>", home, StringComparison.Ordinal);
        Assert.DoesNotContain("summary.EntryId.Value", home, StringComparison.Ordinal);
    }

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
    public void HomePageAlwaysOffersRecoveryRestoreWhenBrowserKeysAreAvailable()
    {
        var page = ReadMobilePage("Home.razor");

        Assert.Contains("@if (CanRestorePackageKey)", page, StringComparison.Ordinal);
        Assert.Contains("private bool CanRestorePackageKey => PackageKeyStatus is \"Not set\" or \"Ready\";", page, StringComparison.Ordinal);
        Assert.Contains("Replace this device's package key with the workbook recovery code.", page, StringComparison.Ordinal);
        Assert.Contains("private bool CanSetupPackageKey => PackageKeyStatus == \"Not set\";", page, StringComparison.Ordinal);
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
    public void HomePageKeepsRecoveryCodeDraftUnlessRestoreSucceeds()
    {
        var page = ReadMobilePage("Home.razor");
        var handler = ExtractMethodBody(page, "private async Task RestorePackageKeyAsync");

        Assert.Contains("var restored = false;", handler, StringComparison.Ordinal);
        Assert.Contains("PendingImportFile is not null", handler, StringComparison.Ordinal);
        Assert.Contains("MobilePackageImportWorkflow.ReadAsync(validationDocument, PendingImportFile, PackageKeyStore)", handler, StringComparison.Ordinal);
        Assert.Contains("verifiedSelectedPackage = true;", handler, StringComparison.Ordinal);
        Assert.Contains("Package key restored and verified against the selected package.", handler, StringComparison.Ordinal);
        Assert.Contains("PackageKeyStore.DeletePackageKeyAsync(restoreLogbookId)", handler, StringComparison.Ordinal);
        Assert.Contains("Recovery code does not decrypt the selected package.", handler, StringComparison.Ordinal);
        Assert.Contains("restored = true;", handler, StringComparison.Ordinal);
        Assert.Contains("finally", handler, StringComparison.Ordinal);
        Assert.Contains("if (restored)", handler, StringComparison.Ordinal);
        Assert.Contains("RecoveryCodeDraft = string.Empty;", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsRoutesPackageExchangeThroughTheSchemaV2Workspace()
    {
        var settings = ReadMobilePage("Settings.razor");
        var exchange = ReadMobilePage("PackageExchange.razor");

        Assert.Contains("Href=\"/exchange\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("/legacy#exchange", settings, StringComparison.Ordinal);
        Assert.Contains("@page \"/exchange\"", exchange, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookDocumentV2", exchange, StringComparison.Ordinal);
        Assert.Contains("MobilePackageImportWorkflow.ReadV2Async", exchange, StringComparison.Ordinal);
        Assert.Contains("Session.ApplyWorkbookPackageAsync", exchange, StringComparison.Ordinal);
        Assert.Contains("Session.ExportWorkbookPackageAsync", exchange, StringComparison.Ordinal);
        Assert.Contains("package-exchange-feedback", exchange, StringComparison.Ordinal);
        Assert.Contains("Exchange needs attention", exchange, StringComparison.Ordinal);
        Assert.DoesNotContain("PortableLogbookDocument.CreateAustraliaFirst", exchange, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageOnlySwitchesLogbookForRecoveryRestoreOnEmptyDeviceCopy()
    {
        var page = ReadMobilePage("Home.razor");
        var handler = ExtractMethodBody(page, "private LogbookId FindPackageKeyRestoreLogbookId");

        Assert.Contains("if (ImportPlan is null)", handler, StringComparison.Ordinal);
        Assert.Contains("Preview the package first, then restore the workbook recovery code for that package.", handler, StringComparison.Ordinal);
        Assert.Contains("ImportCompatibility != MobilePackageImportCompatibility.WrongLogbook", handler, StringComparison.Ordinal);
        Assert.Contains("return ImportPlan.LogbookId;", handler, StringComparison.Ordinal);
        Assert.Contains("Document.Operations.Count > 0 || ImportReceipts.Count > 0", handler, StringComparison.Ordinal);
        Assert.Contains("Cannot switch logbooks after this device copy has local entries or package receipts.", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void HomePageExplainsImportStageDecryptFailure()
    {
        var page = ReadMobilePage("Home.razor");
        var handler = ExtractMethodBody(page, "private async Task ApplyPackageAsync");

        Assert.Contains("catch (MobilePackageImportWorkflowException ex)", handler, StringComparison.Ordinal);
        Assert.Contains("Import could not decrypt the selected package with the key stored on this device.", handler, StringComparison.Ordinal);
        Assert.Contains("confirm the restore message says it was verified against the selected package", handler, StringComparison.Ordinal);
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
    public void Gate3ShellUsesApprovedFiveDestinationNavigation()
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
        Assert.Contains("href=\"/flights/new\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/currency\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/settings\"", layout, StringComparison.Ordinal);
        Assert.Contains("<span>Dashboard</span>", layout, StringComparison.Ordinal);
        Assert.Contains("<span>Logbook</span>", layout, StringComparison.Ordinal);
        Assert.Contains("<span>New flight</span>", layout, StringComparison.Ordinal);
        Assert.Contains("<span>Currency</span>", layout, StringComparison.Ordinal);
        Assert.Contains("<span>Settings</span>", layout, StringComparison.Ordinal);
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
        var preferenceStore = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ElectronicLogbook.Mobile",
            "BrowserUiPreferencesStore.cs")));
        var preferenceState = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ElectronicLogbook.Mobile",
            "MobileUiPreferenceState.cs")));
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
        var script = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ElectronicLogbook.Mobile",
            "wwwroot",
            "js",
            "logbookStore.js")));

        Assert.Contains("MobileUiPreferenceState", layout, StringComparison.Ordinal);
        Assert.Contains("UiPreferences.IsDarkMode", layout, StringComparison.Ordinal);
        Assert.Contains("Appearance", settings, StringComparison.Ordinal);
        Assert.Contains("ThemeButtonClass(\"System\")", settings, StringComparison.Ordinal);
        Assert.Contains("ThemeButtonClass(\"Light\")", settings, StringComparison.Ordinal);
        Assert.Contains("ThemeButtonClass(\"Dark\")", settings, StringComparison.Ordinal);
        Assert.Contains("SetSystemThemeModeAsync", settings, StringComparison.Ordinal);
        Assert.Contains("UiPreferences.SetThemeModeAsync(\"System\")", settings, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddScoped<MobileUiPreferenceState>()", program, StringComparison.Ordinal);
        Assert.Contains("electronicLogbookUiPreferences.applyTheme", preferenceStore, StringComparison.Ordinal);
        Assert.Contains("await store.ApplyThemeAsync(preferences);", preferenceState, StringComparison.Ordinal);
        Assert.Contains("data-elb-theme", script, StringComparison.Ordinal);
        Assert.Contains("html[data-elb-theme=\"dark\"]", css, StringComparison.Ordinal);
        Assert.Contains("html[data-elb-theme=\"system\"]", css, StringComparison.Ordinal);
        Assert.Contains("color: var(--app-text);", css, StringComparison.Ordinal);
        Assert.DoesNotContain("color: #ffffff;\r\n    font-size: 18px;", css, StringComparison.Ordinal);
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
    public void Gate3SettingsExposesDeviceStateExportForDevelopmentRecovery()
    {
        var settings = ReadMobilePage("Settings.razor");

        Assert.Contains("Export device state", settings, StringComparison.Ordinal);
        Assert.Contains("ExportDeviceStateAsync", settings, StringComparison.Ordinal);
        Assert.Contains("MobileDeviceStateExportWorkflow.ExportAsync", settings, StringComparison.Ordinal);
        Assert.Contains("new BrowserLogbookStateV2", settings, StringComparison.Ordinal);
        Assert.Contains("DeviceStateExportMessage", settings, StringComparison.Ordinal);
        Assert.Contains("DeviceStateExportError", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3LogbookUsesBalancedEntryRowsAndSeparateTotalsView()
    {
        var page = ReadMobilePage("Logbook.razor");
        var row = ReadMobilePage("LogbookFlightRow.razor");
        var css = ReadMobileAsset("css", "app.css");

        Assert.Contains("@page \"/flights\"", page, StringComparison.Ordinal);
        Assert.Contains("Entries", page, StringComparison.Ordinal);
        Assert.Contains("Totals", page, StringComparison.Ordinal);
        Assert.Contains("<LogbookFlightRow Entry=\"@entry\" />", page, StringComparison.Ordinal);
        Assert.Contains("logbook-entry-row", row, StringComparison.Ordinal);
        Assert.Contains("logbook-row-date", row, StringComparison.Ordinal);
        Assert.Contains("FormatRowDate(Entry.Entry!.Date)", row, StringComparison.Ordinal);
        Assert.Contains("dd MMM yy", row, StringComparison.Ordinal);
        Assert.Contains("logbook-row-route", row, StringComparison.Ordinal);
        Assert.Contains("Session.FormatRoute(Entry.Entry!)", row, StringComparison.Ordinal);
        Assert.Contains("EntryRemarks(Entry.Entry!)", row, StringComparison.Ordinal);
        Assert.Contains("entry.Remarks?.Trim() ?? string.Empty", row, StringComparison.Ordinal);
        Assert.Contains("logbook-row-aircraft", row, StringComparison.Ordinal);
        Assert.Contains("EntryAircraftMeta(Entry.Entry!)", row, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(entry.FlightId)", row, StringComparison.Ordinal);
        Assert.Contains("entry.Reg?.Trim() ?? string.Empty", row, StringComparison.Ordinal);
        Assert.Contains("string.Join(\" · \",", row, StringComparison.Ordinal);
        Assert.Contains("CurrentEntriesV2", page, StringComparison.Ordinal);
        Assert.Contains("logbook-row-hours", row, StringComparison.Ordinal);
        Assert.Contains("MobileLogbookSession.WorkbookLoggedTime(Entry.Entry!)", row, StringComparison.Ordinal);
        Assert.Contains("SimLabel(Entry.Entry!)", row, StringComparison.Ordinal);
        Assert.Contains("IfrSim.GetValueOrDefault() > 0 ? \"Sim\" : string.Empty", row, StringComparison.Ordinal);
        Assert.DoesNotContain("Session.FormatRegistration(Entry.Entry!)", row, StringComparison.Ordinal);
        Assert.DoesNotContain("Session.FormatAircraft(Entry.Entry!)", row, StringComparison.Ordinal);
        Assert.DoesNotContain("EntryMeta", row, StringComparison.Ordinal);
        Assert.DoesNotContain("ldg |", row, StringComparison.Ordinal);
        Assert.Contains("Flying totals", page, StringComparison.Ordinal);
        Assert.Contains("filter-sheet", page, StringComparison.Ordinal);
        Assert.Contains("FilterRecentOnly", page, StringComparison.Ordinal);
        Assert.Contains("FilterFlightsWithApproachesOnly", page, StringComparison.Ordinal);
        Assert.Contains("Deletion history", page, StringComparison.Ordinal);
        Assert.Contains("Session.DeletedEntriesV2", page, StringComparison.Ordinal);
        Assert.Contains(".logbook-page .logbook-entry-row", css, StringComparison.Ordinal);
        Assert.Contains("\"date hours\"", css, StringComparison.Ordinal);
        Assert.Contains("\"route route\"", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3LogbookSupportsEntriesAndTotalsDeepLinks()
    {
        var page = ReadMobilePage("Logbook.razor");

        Assert.Contains("[SupplyParameterFromQuery(Name = \"view\")]", page, StringComparison.Ordinal);
        Assert.Contains("public string? View { get; set; }", page, StringComparison.Ordinal);
        Assert.Contains("string.Equals(View, \"totals\", StringComparison.OrdinalIgnoreCase)", page, StringComparison.Ordinal);
        Assert.Contains("/flights?view={", page, StringComparison.Ordinal);
        Assert.Contains("? \"totals\" : \"entries\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3LogbookTotalsKeepEveryCanonicalHourLandingAndApproachColumnSeparate()
    {
        var page = ReadMobilePage("Logbook.razor");

        Assert.Contains("Flying totals", page, StringComparison.Ordinal);
        foreach (var column in new[]
                 {
                     "SeIcusDay", "SeIcusNight", "SeDualDay", "SeDualNight", "SeCommandDay", "SeCommandNight",
                     "MeIcusDay", "MeIcusNight", "MeDualDay", "MeDualNight", "MeCommandDay", "MeCommandNight",
                     "CopilotDay", "CopilotNight", "IfrIf", "IfrSim",
                     "LandingsDay", "LandingsNight",
                     "Ils", "Vor", "Rnp", "Ndb", "DgaCdi", "DgaAzi", "Circling"
                 })
        {
            Assert.Contains($"entry.{column}", page, StringComparison.Ordinal);
        }

        Assert.Contains("FormatHoursTotal", page, StringComparison.Ordinal);
        Assert.Contains("FormatCountTotal", page, StringComparison.Ordinal);
        Assert.Contains("logbook-totals", page, StringComparison.Ordinal);
        Assert.Contains("totals-table", page, StringComparison.Ordinal);
        Assert.Contains("totals-pair-list", page, StringComparison.Ordinal);
        Assert.Contains("totals-count-grid", page, StringComparison.Ordinal);
        Assert.Contains("role=\"table\"", page, StringComparison.Ordinal);
        Assert.Contains("role=\"columnheader\"", page, StringComparison.Ordinal);
        Assert.Contains("Workbook columns summed across @EntryCountLabel", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>Hours</span>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>Count</span>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Detail=\"SE\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Detail=\"ME\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Total hours", page, StringComparison.Ordinal);
        Assert.DoesNotContain("TotalCommand", page, StringComparison.Ordinal);
        Assert.DoesNotContain("TotalLandings", page, StringComparison.Ordinal);
        Assert.DoesNotContain("TotalApproaches", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3LogbookTotalsAlignHourFiguresOnSharedColumnTracks()
    {
        var css = ReadMobileAsset("css", "app.css");

        Assert.Contains(
            "--totals-hour-columns: minmax(92px, 1.4fr) repeat(2, minmax(64px, 1fr));",
            css,
            StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(css, "grid-template-columns: var(--totals-hour-columns);"));
        Assert.Contains("font-variant-numeric: tabular-nums;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3LogbookTotalsPairDayAndNightHeadersWithAccessibleIcons()
    {
        var page = ReadMobilePage("Logbook.razor");
        var css = ReadMobileAsset("css", "app.css");

        Assert.Equal(4, CountOccurrences(page, "class=\"totals-time-header\" role=\"columnheader\""));
        Assert.Equal(2, CountOccurrences(page, "Icons.Material.Filled.LightMode"));
        Assert.Equal(2, CountOccurrences(page, "Icons.Material.Filled.DarkMode"));
        Assert.Equal(4, CountOccurrences(page, "aria-hidden=\"true\""));
        Assert.Contains(".totals-time-header .mud-icon-root", css, StringComparison.Ordinal);
        Assert.Contains("flex: 0 0 14px;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3NormalMobileUiHidesUnsupportedTakeoffFields()
    {
        var newFlight = ReadMobilePage("NewFlight.razor");
        var session = ReadMobileSource("MobileLogbookSession.cs");

        Assert.DoesNotContain("Takeoffs day", newFlight, StringComparison.Ordinal);
        Assert.DoesNotContain("Takeoffs night", newFlight, StringComparison.Ordinal);
        Assert.DoesNotContain("Match takeoffs", newFlight, StringComparison.Ordinal);
        Assert.DoesNotContain("MatchTakeoffsToLandings", newFlight, StringComparison.Ordinal);
        Assert.DoesNotContain("Takeoffs day", session, StringComparison.Ordinal);
        Assert.DoesNotContain("Takeoffs night", session, StringComparison.Ordinal);
        Assert.Contains("ClearUnsupportedWorkbookFields", session, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidBackButtonDelegatesToInAppNavigationBeforeExit()
    {
        var activity = ReadProjectFile("mobile", "android", "app", "src", "main", "java", "com", "alphadelta", "electroniclogbook", "MainActivity.java");
        var bridge = ReadMobileAsset("js", "logbookStore.js");

        Assert.Contains("public void onBackPressed()", activity, StringComparison.Ordinal);
        Assert.Contains("handleAndroidBack", activity, StringComparison.Ordinal);
        Assert.Contains("window.electronicLogbookNavigation", bridge, StringComparison.Ordinal);
        Assert.Contains("history.back()", bridge, StringComparison.Ordinal);
        Assert.Contains("path === \"/\"", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3DashboardShowsTotalHoursAndSeparate28And365DayHours()
    {
        var page = ReadMobilePage("Dashboard.razor");
        var activity = ReadMobileSource("MobileDashboardFlightActivity.cs");
        var css = ReadMobileAsset("css", "app.css");

        Assert.Contains("Total hours", page, StringComparison.Ordinal);
        Assert.Contains("Hours last 28 days", page, StringComparison.Ordinal);
        Assert.Contains("Hours last 365 days", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Last 90 days", page, StringComparison.Ordinal);
        Assert.DoesNotContain("RecentFlightCount", page, StringComparison.Ordinal);
        Assert.Contains("<DashboardLastFlight Entry=\"@LastFlight\" />", page, StringComparison.Ordinal);
        Assert.Contains("TotalFlyingHours", page, StringComparison.Ordinal);
        Assert.Contains("HoursLast28Days", page, StringComparison.Ordinal);
        Assert.Contains("HoursLast365Days", page, StringComparison.Ordinal);
        Assert.Contains("HoursWithinDays", page, StringComparison.Ordinal);
        Assert.Contains("date >= cutoff && date <= today", activity, StringComparison.Ordinal);
        Assert.Contains("dashboard-total-hours", page, StringComparison.Ordinal);
        Assert.Contains("href=\"/flights?view=totals\"", page, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, CountOccurrences(page, "href=\"/flights?view=entries\""));
        Assert.Contains("grid-template-columns: minmax(0, 1.2fr) repeat(2, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains(".status-strip.dashboard-metrics", css, StringComparison.Ordinal);
        Assert.Contains("grid-column: 1 / -1;", css, StringComparison.Ordinal);
        Assert.Contains("dashboard-empty-state", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3WorkbookProjectionAndCurrencySummariesAreCachedForResponsiveTabs()
    {
        var session = ReadMobileSource("MobileLogbookSession.cs");
        var dashboard = ReadMobilePage("Dashboard.razor");
        var currency = ReadMobilePage("Currency.razor");

        Assert.Contains("mergeResultV2Cache ??= PortableLogbookWorkbookProjection.MergeV2", session, StringComparison.Ordinal);
        Assert.Contains("currentEntriesV2Cache ??= MergeResultV2", session, StringComparison.Ordinal);
        Assert.Contains("deletedEntriesV2Cache ??= MergeResultV2", session, StringComparison.Ordinal);
        Assert.Contains("InvalidateWorkbookProjectionCache", session, StringComparison.Ordinal);
        Assert.Contains("currencyRecencySummaryCache", session, StringComparison.Ordinal);
        Assert.Contains("GetCurrencyRecencySummary", dashboard, StringComparison.Ordinal);
        Assert.Contains("GetCurrencyRecencySummary", currency, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3DashboardCurrencySnapshotShowsOnlyTheFullCurrencyHeaderTotals()
    {
        var page = ReadMobilePage("Dashboard.razor");
        var overview = ReadMobilePage("DashboardCurrencyOverview.razor");
        var currency = ReadMobilePage("Currency.razor");
        var summary = ReadMobileSource("MobileCurrencyRecencySummary.cs");

        Assert.Contains("<DashboardCurrencyOverview Summary=\"@CurrencySummary\" />", page, StringComparison.Ordinal);
        Assert.DoesNotContain("VFR and IFR overview", page, StringComparison.Ordinal);
        Assert.Contains("currency-overview dashboard-currency-overview", overview, StringComparison.Ordinal);
        Assert.Contains("@Summary.CurrentCount", overview, StringComparison.Ordinal);
        Assert.Contains("@Summary.DueSoonCount", overview, StringComparison.Ordinal);
        Assert.Contains("@Summary.ExpiredCount", overview, StringComparison.Ordinal);
        Assert.Contains("<span>Current</span>", overview, StringComparison.Ordinal);
        Assert.Contains("<span>Due soon</span>", overview, StringComparison.Ordinal);
        Assert.Contains("<span>Expired</span>", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("<details", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("MudButton", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("VfrPanel", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("IfrPanel", overview, StringComparison.Ordinal);
        Assert.Contains("CurrentCount => Summary.CurrentCount", currency, StringComparison.Ordinal);
        Assert.Contains("DueSoonCount => Summary.DueSoonCount", currency, StringComparison.Ordinal);
        Assert.Contains("ExpiredCount => Summary.ExpiredCount", currency, StringComparison.Ordinal);
        Assert.Contains("public int CurrentCount => SingleEngineRows.Count(IsCurrent);", summary, StringComparison.Ordinal);
        Assert.Contains("public int DueSoonCount => SingleEngineRows.Count(IsDueSoon);", summary, StringComparison.Ordinal);
        Assert.Contains("public int ExpiredCount => SingleEngineRows.Count(IsExpired);", summary, StringComparison.Ordinal);
        Assert.Contains("CheckCircleOutline", overview, StringComparison.Ordinal);
        Assert.Contains("Schedule", overview, StringComparison.Ordinal);
        Assert.Contains("ErrorOutline", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("<CurrencyRowList Rows=\"@VfrCurrencyRows\" />", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<CurrencyRowList Rows=\"@IfrCurrencyRows\" />", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Package key", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Package health", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Recent flights", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3DashboardKeepsLastFlightAndEmptyStateActionsUsableAfterSnapshotSimplification()
    {
        var page = ReadMobilePage("Dashboard.razor");
        var overview = ReadMobilePage("DashboardCurrencyOverview.razor");
        var lastFlight = ReadMobilePage("DashboardLastFlight.razor");

        Assert.Contains("<DashboardLastFlight Entry=\"@LastFlight\" />", page, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Last flight context\"", lastFlight, StringComparison.Ordinal);
        Assert.DoesNotContain("Href=", overview, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"No flights yet\"", page, StringComparison.Ordinal);
        Assert.Contains("Href=\"/flights/new\"", page, StringComparison.Ordinal);
        Assert.Contains("Add first flight", page, StringComparison.Ordinal);
        Assert.Contains("Href=\"/settings\"", page, StringComparison.Ordinal);
        Assert.Contains("Open Settings", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3LogbookRowsUseCanonicalWorkbookDateRouteRemarksAndCalculatedTotal()
    {
        var page = ReadMobilePage("LogbookFlightRow.razor");

        Assert.Contains("FormatRowDate(Entry.Entry!.Date)", page, StringComparison.Ordinal);
        Assert.Contains("Session.FormatRoute(Entry.Entry!)", page, StringComparison.Ordinal);
        Assert.Contains("StripGeneratedCrewSuffix(entry.Remarks?.Trim() ?? string.Empty)", page, StringComparison.Ordinal);
        Assert.Contains("MobileLogbookSession.WorkbookLoggedTime(Entry.Entry!)", page, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Calculated total time\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3DashboardUsesDedicatedLastFlightSummaryWithoutChangingLogbookRows()
    {
        var dashboard = ReadMobilePage("Dashboard.razor");
        var lastFlight = ReadMobilePage("DashboardLastFlight.razor");
        var logbook = ReadMobilePage("Logbook.razor");
        var css = ReadMobileAsset("css", "app.css");

        Assert.Contains("<DashboardLastFlight Entry=\"@LastFlight\" />", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("<LogbookFlightRow", dashboard, StringComparison.Ordinal);
        Assert.Contains("dashboard-last-flight-link", lastFlight, StringComparison.Ordinal);
        Assert.Contains("href=\"@($\"/flights/{Entry.EntryId.Value}\")\"", lastFlight, StringComparison.Ordinal);
        Assert.Contains("Session.FormatRoute(flight)", lastFlight, StringComparison.Ordinal);
        Assert.Contains("AircraftMeta(flight)", lastFlight, StringComparison.Ordinal);
        Assert.Contains("WorkbookLoggedTime(flight)", lastFlight, StringComparison.Ordinal);
        Assert.DoesNotContain("logbook-entry-row", lastFlight, StringComparison.Ordinal);
        Assert.Contains("<LogbookFlightRow Entry=\"@entry\" />", logbook, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 44px minmax(0, 1fr) auto;", css, StringComparison.Ordinal);
        Assert.Contains("padding: 16px;", css, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3CurrencyPageShowsWorkbookFaithfulEngineRowsAndExpirySummaries()
    {
        var page = ReadMobilePage("Currency.razor");
        var summary = ReadMobileSource("MobileCurrencyRecencySummary.cs");

        Assert.Contains("@page \"/currency\"", page, StringComparison.Ordinal);
        Assert.Contains("Single engine", page, StringComparison.Ordinal);
        Assert.Contains("Multi engine", page, StringComparison.Ordinal);
        Assert.Contains("currency-overview", page, StringComparison.Ordinal);
        Assert.Contains("Due soon", page, StringComparison.Ordinal);
        Assert.Contains("Expired", page, StringComparison.Ordinal);
        Assert.Contains("CategoryStatusSummary", page, StringComparison.Ordinal);
        Assert.Contains("CurrencyCategories", page, StringComparison.Ordinal);
        Assert.Contains("RowsForCategory", page, StringComparison.Ordinal);
        Assert.Contains("Instrument approaches", page, StringComparison.Ordinal);
        Assert.Contains("CreateSingleEngineFlightReview", summary, StringComparison.Ordinal);
        Assert.Contains("CreateMultiEngineFlightReview", summary, StringComparison.Ordinal);
        Assert.Contains("CreateCirclingApproach", summary, StringComparison.Ordinal);
        Assert.Contains("CurrentlyExpiredSingleEngineRows", summary, StringComparison.Ordinal);
        Assert.Contains("NextExpiringSingleEngineRow", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3CurrencyPageDoesNotExposeOverrideDateEditing()
    {
        var page = ReadMobilePage("Currency.razor");
        var session = ReadMobileSource("MobileLogbookSession.cs");

        Assert.DoesNotContain("Override dates", page, StringComparison.Ordinal);
        Assert.DoesNotContain("override date", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SaveOverrideDatesAsync", page, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveCurrencyOverrideDatesAsync", session, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3CurrencyPageKeepsEngineBoundariesAndExpiryStatesAccessible()
    {
        var page = ReadMobilePage("Currency.razor");
        var row = ReadMobilePage("CurrencyRowList.razor");
        var css = ReadMobileAsset("css", "app.css");

        Assert.Contains("currency-category-list", page, StringComparison.Ordinal);
        Assert.Contains("currency-category-panel", page, StringComparison.Ordinal);
        Assert.Contains("currency-engine-switch", page, StringComparison.Ordinal);
        Assert.Contains("currency-licence-engine-switch", page, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Licence engine class\"", page, StringComparison.Ordinal);
        Assert.True(
            page.IndexOf("currency-licence-engine-switch", StringComparison.Ordinal) >
            page.IndexOf("</summary>", StringComparison.Ordinal));
        Assert.DoesNotContain("@onclick:stopPropagation", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@onkeydown:stopPropagation", page, StringComparison.Ordinal);
        Assert.Contains("aria-pressed", page, StringComparison.Ordinal);
        Assert.Contains("SelectedLicenceRows", page, StringComparison.Ordinal);
        Assert.Contains("? SelectedLicenceRows", page, StringComparison.Ordinal);
        Assert.Contains(": Summary.SingleEngineRows", page, StringComparison.Ordinal);
        Assert.Contains("CurrentCount => Summary.CurrentCount", page, StringComparison.Ordinal);
        Assert.Contains("DueSoonCount => Summary.DueSoonCount", page, StringComparison.Ordinal);
        Assert.Contains("ExpiredCount => Summary.ExpiredCount", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ActiveRows", page, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedEngineHeading", page, StringComparison.Ordinal);
        Assert.Contains("currency-requirement-status-icon", row, StringComparison.Ordinal);
        Assert.Contains("StatusIcon(row)", row, StringComparison.Ordinal);
        Assert.Contains("StatusLabel(row)", row, StringComparison.Ordinal);
        Assert.Contains("MobileCurrencyPeriodDetail.Items(row, ApproachPeriodTotals)", row, StringComparison.Ordinal);
        Assert.Contains("ApproachPeriodTotals=\"@Summary.ApproachPeriodTotals\"", page, StringComparison.Ordinal);
        Assert.Contains("currency-requirement-detail", row, StringComparison.Ordinal);
        Assert.Contains("column-gap: 18px;", css, StringComparison.Ordinal);
        Assert.DoesNotContain("{row.RelevantPeriodTotal} in period", row, StringComparison.Ordinal);
        Assert.Contains("currency-row-current", row, StringComparison.Ordinal);
        Assert.Contains("currency-row-warning", row, StringComparison.Ordinal);
        Assert.Contains("currency-row-expired", row, StringComparison.Ordinal);
        Assert.Contains(".currency-requirement-status-icon .mud-icon-root", css, StringComparison.Ordinal);
        Assert.Contains(".currency-licence-engine-switch", css, StringComparison.Ordinal);
        Assert.Contains("margin: -2px 14px 10px 52px;", css, StringComparison.Ordinal);
        Assert.DoesNotContain("border: 2px solid var(--currency-row-accent);", css, StringComparison.Ordinal);
        Assert.DoesNotContain("CasrReference", row, StringComparison.Ordinal);
        Assert.DoesNotContain("@row.Category", row, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3DashboardRemovesRecordHealthFromTheFlightSummary()
    {
        var page = ReadMobilePage("Dashboard.razor");

        Assert.DoesNotContain("dashboard-record-health", page, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordHealthTitle", page, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordHealthReason", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Session.MergeResultV2.Conflicts.Count", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3FlightDetailIsReadFirstWithEditHistoryAndDeleteActions()
    {
        var page = ReadMobilePage("FlightDetail.razor");
        var css = ReadMobileAsset("css", "app.css");

        Assert.Contains("@page \"/flights/{EntryId}\"", page, StringComparison.Ordinal);
        Assert.Contains("Read first", page, StringComparison.Ordinal);
        Assert.Contains("Edit entry", page, StringComparison.Ordinal);
        Assert.Contains("Immutable history", page, StringComparison.Ordinal);
        Assert.Contains("Session.EntryDetails(CurrentEntry.Entry)", page, StringComparison.Ordinal);
        Assert.Contains("Session.DeleteWorkbookEntryAsync(CurrentEntry)", page, StringComparison.Ordinal);
        Assert.Contains("History?.IsDeleted == true", page, StringComparison.Ordinal);
        Assert.Contains("Deleted entry", page, StringComparison.Ordinal);
        Assert.Contains("Deletion history", page, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo($\"/flights/{CurrentEntry.EntryId.Value}/edit\")", page, StringComparison.Ordinal);
        Assert.Contains("record-field-groups", page, StringComparison.Ordinal);
        Assert.Contains("Crew and route", page, StringComparison.Ordinal);
        Assert.Contains("\"Checks\"", page, StringComparison.Ordinal);
        Assert.Contains("Logged time", page, StringComparison.Ordinal);
        Assert.Contains("Custom fields", page, StringComparison.Ordinal);
        Assert.Contains("Landings and approaches", page, StringComparison.Ordinal);
        Assert.Contains("<dl class=\"record-field-list\">", page, StringComparison.Ordinal);
        Assert.Contains("<dt>@detail.Label</dt>", page, StringComparison.Ordinal);
        Assert.Contains("<dd>@detail.Value</dd>", page, StringComparison.Ordinal);
        Assert.Contains("value == \"-\"", page, StringComparison.Ordinal);
        Assert.Contains("detail.Group == group", page, StringComparison.Ordinal);
        Assert.True(
            page.IndexOf("EntryDetailGroup.LoggedTime", StringComparison.Ordinal) <
            page.IndexOf("EntryDetailGroup.CustomFields", StringComparison.Ordinal));
        Assert.True(
            page.IndexOf("EntryDetailGroup.CustomFields", StringComparison.Ordinal) <
            page.IndexOf("EntryDetailGroup.LandingsAndApproaches", StringComparison.Ordinal));
        Assert.Contains("class=\"page-back-link\" href=\"/flights?view=entries\"", page, StringComparison.Ordinal);
        Assert.Contains("Back to Logbook", page, StringComparison.Ordinal);
        Assert.DoesNotContain("entry-details detail-grid", page, StringComparison.Ordinal);
        Assert.Contains("color: var(--app-text);", css, StringComparison.Ordinal);
        Assert.Contains("color: var(--app-text-muted);", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3NewFlightRouteMapSupportsDedicatedEditRoute()
    {
        var page = ReadMobilePage("NewFlight.razor");

        Assert.Contains("@page \"/flights/new\"", page, StringComparison.Ordinal);
        Assert.Contains("@page \"/flights/{EntryId}/edit\"", page, StringComparison.Ordinal);
        Assert.Contains("public string? EntryId { get; set; }", page, StringComparison.Ordinal);
        Assert.Contains("LoadEditRoute", page, StringComparison.Ordinal);
        Assert.Contains("Session.FindCurrentEntryV2(EntryId)", page, StringComparison.Ordinal);
        Assert.Contains("Session.EditWorkbookEntry(entry)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3PrimaryNavigationProvidesSymmetricalDestinationsAndAccessibleRaisedNewFlightAction()
    {
        var layout = ReadMobileSource("Layout/MainLayout.razor");
        var css = ReadMobileAsset("css", "app.css");

        Assert.Contains("href=\"/\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/flights\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/flights/new\"", layout, StringComparison.Ordinal);
        Assert.Contains("AddNavigationClass = \"bottom-nav-add\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/currency\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/settings\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Primary\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Dashboard\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Logbook\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Currency\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Settings\"", layout, StringComparison.Ordinal);
        Assert.Contains("Icons.Material.Filled.Add", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("nav-progress", layout, StringComparison.Ordinal);
        Assert.Contains("aria-busy=\"@IsNavigationPending\"", layout, StringComparison.Ordinal);
        Assert.Contains("class=\"visually-hidden\" role=\"status\"", layout, StringComparison.Ordinal);
        Assert.Contains("ShowNavigationPending", layout, StringComparison.Ordinal);
        Assert.Contains("CompleteNavigationFeedbackAsync", layout, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(250, cancellationToken)", layout, StringComparison.Ordinal);
        Assert.Contains("nav-pending-link", css, StringComparison.Ordinal);
        Assert.Contains(".bottom-nav::before", css, StringComparison.Ordinal);
        Assert.Contains("transform 260ms", css, StringComparison.Ordinal);
        Assert.Contains(".bottom-nav:has(> a.active)::before", css, StringComparison.Ordinal);
        Assert.Contains(":has(> a:nth-of-type(5).nav-pending-link)", css, StringComparison.Ordinal);
        Assert.Contains("touch-action: manipulation", css, StringComparison.Ordinal);
        Assert.Contains(":active", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(5, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains("var(--native-safe-bottom)", css, StringComparison.Ordinal);
        Assert.Contains(".bottom-nav-add-icon", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3LogbookViewSwitcherLabelsItsEntriesAndTotalsDestinations()
    {
        var page = ReadMobilePage("Logbook.razor");

        Assert.Contains("role=\"tablist\" aria-label=\"Logbook view\"", page, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"() => SelectViewAsync(LogbookView.Entries)\">Entries</button>", page, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"() => SelectViewAsync(LogbookView.Totals)\">Totals</button>", page, StringComparison.Ordinal);
        Assert.Contains("view-progress", page, StringComparison.Ordinal);
        Assert.Contains("IsViewSwitchPending", page, StringComparison.Ordinal);
        Assert.Contains("await Task.Yield();", page, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo($\"/flights?view={(view == LogbookView.Totals ? \"totals\" : \"entries\")}\");", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3NewFlightGroupsEveryWorkbookInputAroundTheCanonicalDraft()
    {
        var page = ReadMobilePage("NewFlight.razor");
        var session = ReadMobileSource("MobileLogbookSession.cs");

        Assert.Contains("type=\"date\"", page, StringComparison.Ordinal);
        Assert.Contains("@bind=\"Session.WorkbookDraft.Date\"", page, StringComparison.Ordinal);
        Assert.Contains("Identity", page, StringComparison.Ordinal);
        Assert.Contains("Pilots and remarks", page, StringComparison.Ordinal);
        Assert.Contains("FR, IPC, OPC, and custom fields", page, StringComparison.Ordinal);
        Assert.Contains("Operating capacity", page, StringComparison.Ordinal);
        Assert.Contains("Landings", page, StringComparison.Ordinal);
        Assert.Contains("Approach types", page, StringComparison.Ordinal);
        Assert.Contains("Session.WorkbookDraft.FlightReview", page, StringComparison.Ordinal);
        Assert.Contains("Session.WorkbookDraft.InstrumentProficiencyCheck", page, StringComparison.Ordinal);
        Assert.Contains("Session.WorkbookDraft.OperatorProficiencyCheck", page, StringComparison.Ordinal);
        Assert.Contains("Session.WorkbookCustomFields", page, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyList<CustomFieldDefinition> WorkbookCustomFields", session, StringComparison.Ordinal);
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

    [Fact]
    public void Gate3PreservesThemeDraftOfflineAndOverflowContracts()
    {
        var page = ReadMobilePage("NewFlight.razor");
        var session = ReadMobileSource("MobileLogbookSession.cs");
        var css = ReadMobileWebAsset("css/app.css");
        var offlineWorker = ReadMobileWebAsset("service-worker.published.js");

        Assert.Contains("--app-bg:", css, StringComparison.Ordinal);
        Assert.Contains("--app-primary:", css, StringComparison.Ordinal);
        Assert.Contains("html[data-elb-theme=\"dark\"]", css, StringComparison.Ordinal);
        Assert.Contains("html[data-elb-theme=\"system\"]", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
        Assert.Contains("animation: none;", css, StringComparison.Ordinal);
        Assert.Contains("@oninput=\"Session.MarkDraftEdited\"", page, StringComparison.Ordinal);
        Assert.Contains("@onchange=\"Session.MarkDraftEdited\"", page, StringComparison.Ordinal);
        Assert.Contains("public bool HasEditedDraft", session, StringComparison.Ordinal);
        Assert.Contains("HasAttemptedSubmit || HasEditedDraft", session, StringComparison.Ordinal);
        Assert.Contains("cache.addAll(assetsRequests)", offlineWorker, StringComparison.Ordinal);
        Assert.Contains("return cachedResponse || fetch(event.request);", offlineWorker, StringComparison.Ordinal);
        Assert.Contains(".app-main", css, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden;", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(5, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains(".bottom-nav a", css, StringComparison.Ordinal);
        Assert.Contains("min-width: 0;", css, StringComparison.Ordinal);
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

    private static string ReadMobileSource(string relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ElectronicLogbook.Mobile",
            relativePath)));

    private static string ReadMobileWebAsset(string relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ElectronicLogbook.Mobile",
            "wwwroot",
            relativePath)));

    private static string ReadMobileAsset(params string[] relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            [
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "ElectronicLogbook.Mobile",
                "wwwroot",
                ..relativePath
            ])));

    private static string ReadProjectFile(params string[] relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            [
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "..",
                ..relativePath
            ])));

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
