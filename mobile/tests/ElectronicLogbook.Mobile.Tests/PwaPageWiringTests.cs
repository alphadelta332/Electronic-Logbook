namespace ElectronicLogbook.Mobile.Tests;

public sealed class PwaPageWiringTests
{
    [Fact]
    public void UserFacingFlightPagesDoNotRenderSystemEntryIds()
    {
        var flightDetail = ReadMobilePage("FlightDetail.razor");
        var logbook = ReadMobilePage("Logbook.razor");

        Assert.DoesNotContain("<h1>@History.EntryId.Value</h1>", flightDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>@entry.EntryId.Value</strong>", logbook, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>@conflict.EntryId.Value</strong>", logbook, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsRoutesPackageExchangeThroughAdvancedRecoveryOnly()
    {
        var settings = ReadMobilePage("Settings.razor");
        var exchange = ReadMobilePage("PackageExchange.razor");

        Assert.Contains("Href=\"/exchange\"", settings, StringComparison.Ordinal);
        Assert.Contains("Advanced recovery", settings, StringComparison.Ordinal);
        Assert.Contains("Advanced recovery workspace", settings, StringComparison.Ordinal);
        Assert.Contains("Account export", settings, StringComparison.Ordinal);
        Assert.Contains("Account deletion", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("/legacy#exchange", settings, StringComparison.Ordinal);
        Assert.Contains("@page \"/exchange\"", exchange, StringComparison.Ordinal);
        Assert.Contains("<PageTitle>Advanced recovery</PageTitle>", exchange, StringComparison.Ordinal);
        Assert.Contains("<h1>Manual Package Exchange</h1>", exchange, StringComparison.Ordinal);
        Assert.Contains("Use this only for recovery, account export, key loss, conflict support", exchange, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookDocumentV2", exchange, StringComparison.Ordinal);
        Assert.Contains("MobilePackageImportWorkflow.ReadV2Async", exchange, StringComparison.Ordinal);
        Assert.Contains("Session.ApplyWorkbookPackageAsync", exchange, StringComparison.Ordinal);
        Assert.Contains("Session.ExportWorkbookPackageAsync", exchange, StringComparison.Ordinal);
        Assert.Contains("package-exchange-feedback", exchange, StringComparison.Ordinal);
        Assert.Contains("Label=\"Recovery code\"", exchange, StringComparison.Ordinal);
        Assert.Contains("Recovery needs attention", exchange, StringComparison.Ordinal);
        Assert.DoesNotContain("PortableLogbookDocument.CreateAustraliaFirst", exchange, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate6RemovesTheLegacyPageAndRoute()
    {
        var pagesDirectory = GetMobilePagesDirectory();

        Assert.False(File.Exists(Path.Combine(pagesDirectory, "Home.razor")));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(pagesDirectory, "*.razor", SearchOption.TopDirectoryOnly),
            path => File.ReadAllText(path).Contains("@page \"/legacy\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Gate3ShellKeepsNewFlightCentredAcrossSevenDestinations()
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
        Assert.Contains("<PathOnlyNavLink Href=\"/flights\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/routes\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/flights/new\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/charts\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/currency\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/settings\"", layout, StringComparison.Ordinal);
        Assert.Contains("<span>Home</span>", layout, StringComparison.Ordinal);
        Assert.Contains("<span>Logbook</span>", layout, StringComparison.Ordinal);
        Assert.Contains("<span>Route Map</span>", layout, StringComparison.Ordinal);
        Assert.Contains("<span>New flight</span>", layout, StringComparison.Ordinal);
        Assert.Contains("<span>Charts</span>", layout, StringComparison.Ordinal);
        Assert.Contains("<span>Currency</span>", layout, StringComparison.Ordinal);
        Assert.Contains("<span>Settings</span>", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>Exchange</span>", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("MudMenu", layout, StringComparison.Ordinal);
        Assert.True(
            layout.IndexOf("href=\"/currency\"", StringComparison.Ordinal)
            < layout.IndexOf("href=\"/flights/new\"", StringComparison.Ordinal));
        Assert.True(
            layout.IndexOf("href=\"/flights/new\"", StringComparison.Ordinal)
            < layout.IndexOf("href=\"/routes\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Gate3FutureRouteAndChartDestinationsAreClearlyMarkedComingSoon()
    {
        var routes = ReadMobilePage("Routes.razor");
        var charts = ReadMobilePage("Charts.razor");

        Assert.Contains("@page \"/routes\"", routes, StringComparison.Ordinal);
        Assert.Contains("<h1 id=\"routes-heading\">Route map</h1>", routes, StringComparison.Ordinal);
        Assert.Contains("<p>Coming soon!</p>", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("future-feature", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("Planned after the first release", routes, StringComparison.Ordinal);
        Assert.Contains("@page \"/charts\"", charts, StringComparison.Ordinal);
        Assert.Contains("<h1 id=\"charts-heading\">Charts</h1>", charts, StringComparison.Ordinal);
        Assert.Contains("<p>Coming soon!</p>", charts, StringComparison.Ordinal);
        Assert.DoesNotContain("future-feature", charts, StringComparison.Ordinal);
        Assert.DoesNotContain("Planned after the first release", charts, StringComparison.Ordinal);
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
        Assert.Contains("Accent colour", settings, StringComparison.Ordinal);
        Assert.Contains("MobileUiPreferences.AccentOptions", settings, StringComparison.Ordinal);
        Assert.Contains("SetAccentAsync", settings, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddScoped<MobileUiPreferenceState>()", program, StringComparison.Ordinal);
        Assert.Contains("electronicLogbookUiPreferences.applyTheme", preferenceStore, StringComparison.Ordinal);
        Assert.Contains("DefaultAccent", preferenceStore, StringComparison.Ordinal);
        Assert.Contains("await store.ApplyThemeAsync(preferences);", preferenceState, StringComparison.Ordinal);
        Assert.Contains("SetAccentAsync", preferenceState, StringComparison.Ordinal);
        Assert.Contains("data-elb-theme", script, StringComparison.Ordinal);
        Assert.Contains("data-elb-accent", script, StringComparison.Ordinal);
        Assert.Contains("html[data-elb-theme=\"dark\"]", css, StringComparison.Ordinal);
        Assert.Contains("html[data-elb-theme=\"system\"]", css, StringComparison.Ordinal);
        Assert.Contains("html[data-elb-accent=\"ocean\"]", css, StringComparison.Ordinal);
        Assert.Contains(".accent-picker", css, StringComparison.Ordinal);
        Assert.Contains("color: var(--app-text);", css, StringComparison.Ordinal);
        Assert.DoesNotContain("color: #ffffff;\r\n    font-size: 18px;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3FilledPrimaryActionsFollowSelectedAccent()
    {
        var newFlight = ReadMobilePage("NewFlight.razor");
        var flightDetail = ReadMobilePage("FlightDetail.razor");
        var settings = ReadMobilePage("Settings.razor");
        var css = ReadMobileAsset("css", "app.css");

        Assert.Contains("Variant=\"Variant.Outlined\" Color=\"Color.Success\" OnClick=\"ReviewSave\"", newFlight, StringComparison.Ordinal);
        Assert.Contains("@Session.SaveLabel", newFlight, StringComparison.Ordinal);
        Assert.Contains("Color=\"Color.Primary\" StartIcon=\"@Icons.Material.Filled.Edit\" OnClick=\"EditEntry\"", flightDetail, StringComparison.Ordinal);
        Assert.Contains("Href=\"/exchange\" Variant=\"Variant.Outlined\"", settings, StringComparison.Ordinal);
        Assert.Matches(
            @"(?s)\.mud-button-root\.mud-button-filled-primary:not\(:disabled\)\s*\{(?=[^}]*background-color:\s*var\(--app-primary\))(?=[^}]*color:\s*var\(--app-primary-text\))",
            css);
        Assert.Matches(
            @"(?s)\.mud-button-root\.mud-button-filled-primary:not\(:disabled\):hover\s*\{[^}]*background-color:\s*color-mix\(in srgb, var\(--app-primary\)",
            css);
    }

    [Fact]
    public void Gate3SettingsExposesSupportSummaryExport()
    {
        var settings = ReadMobilePage("Settings.razor");

        Assert.Contains("@inject BrowserFileStore FileStore", settings, StringComparison.Ordinal);
        Assert.Contains("Support summary", settings, StringComparison.Ordinal);
        Assert.Contains("ExportSupportSummaryAsync", settings, StringComparison.Ordinal);
        Assert.Contains("MobileSupportSummaryExportWorkflow.ExportAsync", settings, StringComparison.Ordinal);
        Assert.Contains("Session.DocumentV2", settings, StringComparison.Ordinal);
        Assert.Contains("SupportSummaryMessage", settings, StringComparison.Ordinal);
        Assert.Contains("SupportSummaryError", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3SettingsExposesDeviceStateExportForAdvancedRecovery()
    {
        var settings = ReadMobilePage("Settings.razor");

        Assert.Contains("Export account backup", settings, StringComparison.Ordinal);
        Assert.Contains("ExportDeviceStateAsync", settings, StringComparison.Ordinal);
        Assert.Contains("MobileDeviceStateExportWorkflow.ExportAsync", settings, StringComparison.Ordinal);
        Assert.Contains("new BrowserLogbookStateV2", settings, StringComparison.Ordinal);
        Assert.Contains("Session.HostedSync", settings, StringComparison.Ordinal);
        Assert.Contains("DeviceStateExportMessage", settings, StringComparison.Ordinal);
        Assert.Contains("DeviceStateExportError", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsExposesHostedSyncStatusAndManualRefresh()
    {
        var settings = ReadMobilePage("Settings.razor");
        var session = ReadMobileSource("MobileLogbookSession.cs");

        Assert.Contains("Connection status", settings, StringComparison.Ordinal);
        Assert.Contains("Hosted sync", settings, StringComparison.Ordinal);
        Assert.Contains("Invited email", settings, StringComparison.Ordinal);
        Assert.Contains("Code or unused sign-in link", settings, StringComparison.Ordinal);
        Assert.Contains("Outlook Safe Links and unused direct links are supported", settings, StringComparison.Ordinal);
        Assert.Contains("Send sign-in email", settings, StringComparison.Ordinal);
        Assert.Contains("Connect account", settings, StringComparison.Ordinal);
        Assert.Contains("Resume verified sign-in", settings, StringComparison.Ordinal);
        Assert.Contains("ResumeHostedInviteAcceptanceAsync", settings, StringComparison.Ordinal);
        Assert.Contains("StartHostedInviteAcceptanceAsync", settings, StringComparison.Ordinal);
        Assert.Contains("CompleteHostedInviteAcceptanceAsync", settings, StringComparison.Ordinal);
        Assert.Contains("Sign in with Google", settings, StringComparison.Ordinal);
        Assert.Contains("Add Google sign-in", settings, StringComparison.Ordinal);
        Assert.Contains("SignInWithGoogleAsync", settings, StringComparison.Ordinal);
        Assert.Contains("LinkGoogleIdentityAsync", settings, StringComparison.Ordinal);
        Assert.Contains("AccountFailureMessage", settings, StringComparison.Ordinal);
        Assert.Contains("Do not request another email", settings, StringComparison.Ordinal);
        Assert.Contains("Run connection preflight", settings, StringComparison.Ordinal);
        Assert.Contains("Recover retained connection", settings, StringComparison.Ordinal);
        Assert.Contains("Copy redacted diagnostics", settings, StringComparison.Ordinal);
        Assert.Contains("Technical details", settings, StringComparison.Ordinal);
        Assert.Contains("UNEXPECTED_", settings, StringComparison.Ordinal);
        Assert.Contains("MobileConnectionStage.ACCESS_TOKEN_VALIDATE", settings, StringComparison.Ordinal);
        Assert.Contains("electronicLogbookDiagnostics.copy", settings, StringComparison.Ordinal);
        Assert.Contains("@Session.HostedSyncStatusLabel", settings, StringComparison.Ordinal);
        Assert.Contains("@Session.HostedSyncStatusDetail", settings, StringComparison.Ordinal);
        Assert.Contains("Sync now", settings, StringComparison.Ordinal);
        Assert.Contains("SyncHostedNowAsync", settings, StringComparison.Ordinal);
        Assert.Contains("Sync diagnostics", settings, StringComparison.Ordinal);
        Assert.Contains("Pending upload details", settings, StringComparison.Ordinal);
        Assert.Contains("Other-device history excluded from uploads", settings, StringComparison.Ordinal);
        Assert.Contains("HostedSyncDiagnostics", settings, StringComparison.Ordinal);
        Assert.Contains("Session.HostedSyncChanged += OnHostedSyncChanged", settings, StringComparison.Ordinal);
        Assert.Contains("Session.HostedSyncChanged -= OnHostedSyncChanged", settings, StringComparison.Ordinal);
        Assert.Contains("reauthenticate", settings, StringComparison.Ordinal);
        Assert.Contains("restore a lost key", settings, StringComparison.Ordinal);
        Assert.Contains("revoked device", settings, StringComparison.Ordinal);
        Assert.Contains("Needs attention", session, StringComparison.Ordinal);
        Assert.Contains("PendingLocalOperationCount", session, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalErrorUiOffersExplicitDismissAndRestartActions()
    {
        var index = ReadMobileWebAsset("index.html");

        Assert.Contains("Restart app", index, StringComparison.Ordinal);
        Assert.Contains("class=\"dismiss\">Dismiss", index, StringComparison.Ordinal);
        Assert.DoesNotContain("An unhandled error has occurred", index, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramWiresRealMobileHostedSyncTransportForPilotBuilds()
    {
        var program = ReadMobileSource("Program.cs");
        var client = ReadMobileSource("MobileSupabaseHostedSyncClient.cs");
        var gitignore = ReadProjectFile(".gitignore");

        Assert.Contains("BrowserHostedCredentialStore", program, StringComparison.Ordinal);
        Assert.Contains("MobileSupabaseHostedSyncClient", program, StringComparison.Ordinal);
        Assert.Contains("IHostedLogbookAuthenticator", program, StringComparison.Ordinal);
        Assert.Contains("IHostedLogbookLedger", program, StringComparison.Ordinal);
        Assert.Contains("IMobileHostedRecoveryClient", program, StringComparison.Ordinal);
        Assert.Contains("MobileConnectionRecoveryWorkflow", program, StringComparison.Ordinal);
        Assert.Contains("hosted-sync.local.json", client, StringComparison.Ordinal);
        Assert.Contains("create_user", client, StringComparison.Ordinal);
        Assert.DoesNotContain("should_create_user", client, StringComparison.Ordinal);
        Assert.Contains("token_hash", client, StringComparison.Ordinal);
        Assert.Contains("accept_hosted_invitation", client, StringComparison.Ordinal);
        Assert.Contains("append_hosted_operation", client, StringComparison.Ordinal);
        Assert.Contains("hosted-sync.local.json", gitignore, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate4SettingsAndExchangeExposeDeviceHealthAndResolvableV2PackageImport()
    {
        var settings = ReadMobilePage("Settings.razor");
        var exchange = ReadMobilePage("PackageExchange.razor");
        var session = ReadMobileSource("MobileLogbookSession.cs");
        var workflow = ReadMobileSource("MobilePackageImportApplyWorkflow.cs");
        var css = ReadMobileAsset("css", "app.css");

        Assert.Contains("Device health", settings, StringComparison.Ordinal);
        Assert.Contains("Local storage", settings, StringComparison.Ordinal);
        Assert.Contains("Offline changes", settings, StringComparison.Ordinal);
        Assert.Contains("Manual Package Exchange", settings, StringComparison.Ordinal);
        Assert.Contains("Session.ExchangeStatus.PendingOperationCount", settings, StringComparison.Ordinal);
        Assert.Contains("DocumentV2.Operations.Count", session, StringComparison.Ordinal);
        Assert.Contains("ApplyWorkbookPackageWithCustomFieldResolutionsAsync", session, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookDocumentV2 localDocument", workflow, StringComparison.Ordinal);
        Assert.Contains("Custom field differences", exchange, StringComparison.Ordinal);
        Assert.Contains("Keep device labels", exchange, StringComparison.Ordinal);
        Assert.Contains("Use package labels", exchange, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCustomFieldDefinitionResolution", exchange, StringComparison.Ordinal);
        Assert.Contains(".device-health-grid", css, StringComparison.Ordinal);
        Assert.Contains(".custom-field-resolution-row", css, StringComparison.Ordinal);
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

        Assert.Contains("OnBackPressedCallback", activity, StringComparison.Ordinal);
        Assert.Contains("getOnBackPressedDispatcher().addCallback(this, callback)", activity, StringComparison.Ordinal);
        Assert.Contains("public void handleOnBackPressed()", activity, StringComparison.Ordinal);
        Assert.Contains("dispatchBackToWebView(this)", activity, StringComparison.Ordinal);
        Assert.Contains("callback.setEnabled(false)", activity, StringComparison.Ordinal);
        Assert.Contains("getOnBackPressedDispatcher().onBackPressed()", activity, StringComparison.Ordinal);
        Assert.Contains("callback.setEnabled(true)", activity, StringComparison.Ordinal);
        Assert.DoesNotContain("public void onBackPressed()", activity, StringComparison.Ordinal);
        Assert.Contains("handleAndroidBack", activity, StringComparison.Ordinal);
        Assert.Contains("window.electronicLogbookNavigation", bridge, StringComparison.Ordinal);
        Assert.Contains("history.back()", bridge, StringComparison.Ordinal);
        Assert.Contains("path === \"/\"", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate2NewFlightBackAlwaysReturnsToTheDashboardWithAVisibleFallback()
    {
        var page = ReadMobilePage("NewFlight.razor");
        var activity = ReadProjectFile("mobile", "android", "app", "src", "main", "java", "com", "alphadelta", "electroniclogbook", "MainActivity.java");
        var bridge = ReadMobileAsset("js", "logbookStore.js");

        Assert.Contains("class=\"page-back-link\"", page, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Back to dashboard\"", page, StringComparison.Ordinal);
        Assert.Contains("Icons.Material.Filled.ArrowBack", page, StringComparison.Ordinal);
        Assert.Contains("<span>Back to dashboard</span>", page, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"RequestCancel\"", page, StringComparison.Ordinal);
        Assert.Contains("@onclick:preventDefault=\"true\"", page, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo(\"/\", replace: true);", page, StringComparison.Ordinal);
        Assert.Contains("handleAndroidBack", activity, StringComparison.Ordinal);
        Assert.Contains("path === \"/flights/new\"", bridge, StringComparison.Ordinal);
        Assert.Contains("/^\\/flights\\/[^/]+\\/edit$/.test(path)", bridge, StringComparison.Ordinal);
        Assert.Contains("history.replaceState(history.state, \"\", \"/\")", bridge, StringComparison.Ordinal);
        Assert.Contains("new PopStateEvent(\"popstate\"", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3DashboardShowsTotalHoursAndSeparate28And365DayHours()
    {
        var page = ReadMobilePage("Dashboard.razor");
        var activity = ReadMobileSource("MobileDashboardFlightActivity.cs");
        var css = ReadMobileAsset("css", "app.css");

        Assert.Contains("Total flying hours", page, StringComparison.Ordinal);
        Assert.Contains("Total aeronautical experience", page, StringComparison.Ordinal);
        Assert.Contains("Last 28 days", page, StringComparison.Ordinal);
        Assert.Contains("Last 365 days", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Last 90 days", page, StringComparison.Ordinal);
        Assert.DoesNotContain("RecentFlightCount", page, StringComparison.Ordinal);
        Assert.Contains("<DashboardLastFlight Entry=\"@LastFlight\" />", page, StringComparison.Ordinal);
        Assert.Contains("TotalFlyingHours", page, StringComparison.Ordinal);
        Assert.Contains("WorkbookFlightTime(entry.Entry!)", page, StringComparison.Ordinal);
        Assert.Contains("TotalAeronauticalExperience", page, StringComparison.Ordinal);
        Assert.Contains("WorkbookLoggedTime(entry.Entry!)", page, StringComparison.Ordinal);
        Assert.Contains("HoursLast28Days", page, StringComparison.Ordinal);
        Assert.Contains("HoursLast365Days", page, StringComparison.Ordinal);
        Assert.Contains("HoursWithinDays", page, StringComparison.Ordinal);
        Assert.Contains("date >= cutoff && date <= today", activity, StringComparison.Ordinal);
        Assert.Contains("href=\"/flights?view=totals\"", page, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, CountOccurrences(page, "href=\"/flights?view=entries\""));
        Assert.Contains("dashboard-hours-total", page, StringComparison.Ordinal);
        Assert.Contains("dashboard-hours-periods", page, StringComparison.Ordinal);
        Assert.Contains("font-size: clamp(64px, 20vw, 96px);", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains("dashboard-empty-state", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3DashboardUsesHeroHierarchyAndReadableHourLabels()
    {
        var page = ReadMobilePage("Dashboard.razor");
        var css = ReadMobileAsset("css", "app.css");

        Assert.Contains("<h1 id=\"dashboard-total-hours-heading\">Total flying hours</h1>", page, StringComparison.Ordinal);
        Assert.Contains("<small>Total aeronautical experience <strong>@MobileLogbookSession.FormatHours(TotalAeronauticalExperience)</strong></small>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("FormatHours(TotalAeronauticalExperience) h", page, StringComparison.Ordinal);
        Assert.Matches(
            @"(?s)\.dashboard-hours-hero\s*\{[^}]*border:\s*1px solid var\(--app-border\)[^}]*border-radius:\s*14px[^}]*background:\s*var\(--app-surface\)",
            css);
        Assert.Contains("<strong>Last 28 days</strong>", page, StringComparison.Ordinal);
        Assert.Contains("<strong>Last 365 days</strong>", page, StringComparison.Ordinal);
        Assert.Matches(
            @"(?s)\.dashboard-hours-total > h1\s*\{[^}]*font-size:\s*18px",
            css);
        Assert.Matches(
            @"(?s)\.dashboard-hours-period > strong\s*\{[^}]*font-size:\s*14px",
            css);
    }

    [Fact]
    public void Gate3DashboardExperienceSnapshotKeepsAuthorityAndEngineDenominatorsSeparate()
    {
        var dashboard = ReadMobilePage("Dashboard.razor");
        var snapshot = ReadMobilePage("DashboardExperienceSnapshot.razor");
        var summary = ReadMobileSource("MobileDashboardExperienceSummary.cs");
        var css = ReadMobileAsset("css", "app.css");

        Assert.Contains("<DashboardExperienceSnapshot Summary=\"@ExperienceSummary\" />", dashboard, StringComparison.Ordinal);
        Assert.Matches(
            @"(?s)<section class=""dashboard-hours-hero"".*?<DashboardExperienceSnapshot Summary=""@ExperienceSummary"" />.*?</section>\s*<div class=""dashboard-side-column"">\s*<DashboardLastFlight",
            dashboard);
        Assert.Contains("<h3 id=\"dashboard-authority-heading\">Authority</h3>", snapshot, StringComparison.Ordinal);
        Assert.Contains("Command", snapshot, StringComparison.Ordinal);
        Assert.Contains("ICUS", snapshot, StringComparison.Ordinal);
        Assert.Contains("Dual", snapshot, StringComparison.Ordinal);
        Assert.Contains("Copilot", snapshot, StringComparison.Ordinal);
        Assert.Contains("<h3 id=\"dashboard-engine-heading\">Engine class</h3>", snapshot, StringComparison.Ordinal);
        Assert.Contains("<span>Single</span>", snapshot, StringComparison.Ordinal);
        Assert.Contains("<span>Multi</span>", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>SE</span>", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>ME</span>", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("@Hours(Summary.AuthorityHours) h", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("@Hours(Summary.ClassifiedEngineHours) h", snapshot, StringComparison.Ordinal);
        Assert.Contains("href=\"/flights?view=totals\"", snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CommandHours + IcusHours + DualHours + CopilotHours", summary, StringComparison.Ordinal);
        Assert.Contains("SingleEngineHours + MultiEngineHours", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("IfrIf", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("IfrSim", summary, StringComparison.Ordinal);
        Assert.Contains(".dashboard-experience-bar", css, StringComparison.Ordinal);
        Assert.Matches(
            @"(?s)\.dashboard-experience-snapshot\s*\{[^}]*border-top:\s*1px solid var\(--app-border\)[^}]*padding-top:\s*18px",
            css);
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
        Assert.Contains("<NavLink class=\"dashboard-section dashboard-currency\" href=\"/currency\" aria-label=\"Open currency and recency status\">", page, StringComparison.Ordinal);
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
    public void Gate3CurrencyUsesCdiLikeInstrumentApproachIcon()
    {
        var page = ReadMobilePage("Currency.razor");

        Assert.Contains(
            "\"Approaches\" => Icons.Material.Filled.GpsFixed",
            page,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"Approaches\" => Icons.Material.Filled.TrackChanges",
            page,
            StringComparison.Ordinal);
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
        Assert.Contains("OnClick=\"RequestDeleteEntry\"", page, StringComparison.Ordinal);
        Assert.Contains("ShowDeleteConfirmation", page, StringComparison.Ordinal);
        Assert.Contains("Confirm deletion", page, StringComparison.Ordinal);
        Assert.Contains("Delete this flight?", page, StringComparison.Ordinal);
        Assert.Contains("Delete flight", page, StringComparison.Ordinal);
        Assert.Contains("you can undo this deletion for about five seconds", page, StringComparison.Ordinal);
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
    public void Gate2LayoutShowsSharedAnimatedFeedbackForAddedModifiedAndDeletedEntries()
    {
        var feedback = ReadMobileSource(Path.Combine("Layout", "MobileActionFeedback.razor"));
        var layout = ReadMobileSource(Path.Combine("Layout", "MainLayout.razor"));
        var session = ReadMobileSource("MobileLogbookSession.cs");
        var css = ReadMobileAsset("css", "app.css");

        Assert.Contains("<MobileActionFeedback />", layout, StringComparison.Ordinal);
        Assert.Contains("class=\"action-feedback-message @AnimationClass\"", feedback, StringComparison.Ordinal);
        Assert.Contains("Session.ActionFeedbackMessage", feedback, StringComparison.Ordinal);
        Assert.Contains("Session.CanUndoLastWorkbookAction", feedback, StringComparison.Ordinal);
        Assert.Contains("Session.UndoLastWorkbookActionAsync()", feedback, StringComparison.Ordinal);
        Assert.Contains("Session.ActionFeedbackRemaining", feedback, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(remaining.Value, cancellationToken)", feedback, StringComparison.Ordinal);
        Assert.Contains("PlayExitAsync", feedback, StringComparison.Ordinal);
        Assert.Contains("action-feedback-message-enter", feedback, StringComparison.Ordinal);
        Assert.Contains("action-feedback-message-exit", feedback, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(240, cancellationToken)", feedback, StringComparison.Ordinal);
        Assert.Contains("\"Entry added.\"", session, StringComparison.Ordinal);
        Assert.Contains("\"Entry modified.\"", session, StringComparison.Ordinal);
        Assert.Contains("\"Entry deleted.\"", session, StringComparison.Ordinal);
        Assert.Contains("WorkbookActionUndoKind.None", session, StringComparison.Ordinal);
        Assert.Contains("WorkbookActionUndoKind.RestoreModifiedEntry", session, StringComparison.Ordinal);
        Assert.Contains(".action-feedback-message", css, StringComparison.Ordinal);
        Assert.Contains("bottom: calc(78px + var(--native-safe-bottom));", css, StringComparison.Ordinal);
        Assert.Contains("z-index: 25;", css, StringComparison.Ordinal);
        Assert.Contains("--action-feedback-hidden-offset: calc(100% + 88px + var(--native-safe-bottom));", css, StringComparison.Ordinal);
        Assert.Contains("animation: action-feedback-message-enter 300ms", css, StringComparison.Ordinal);
        Assert.Contains("animation: action-feedback-message-exit 240ms", css, StringComparison.Ordinal);
        Assert.Contains("@keyframes action-feedback-message-exit", css, StringComparison.Ordinal);
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
        Assert.Contains("<PathOnlyNavLink Href=\"/flights\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/routes\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/flights/new\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/charts\"", layout, StringComparison.Ordinal);
        Assert.Contains("AddNavigationClass = \"bottom-nav-add\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/currency\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/settings\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Primary\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Dashboard\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Logbook\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Route Map\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Charts\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Currency\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Settings\"", layout, StringComparison.Ordinal);
        Assert.Contains("Icons.Material.Filled.Add", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("nav-progress", layout, StringComparison.Ordinal);
        Assert.Contains("aria-busy=\"@IsNavigationPending\"", layout, StringComparison.Ordinal);
        Assert.Contains("class=\"visually-hidden\" role=\"status\"", layout, StringComparison.Ordinal);
        Assert.Contains("ShowNavigationPending", layout, StringComparison.Ordinal);
        Assert.Contains("CompleteNavigationFeedbackAsync", layout, StringComparison.Ordinal);
        Assert.Contains("@inject IJSRuntime JS", layout, StringComparison.Ordinal);
        Assert.Contains("_ = ScrollMainToTopAsync();", layout, StringComparison.Ordinal);
        Assert.Contains("electronicLogbookNavigation.scrollMainToTop", layout, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(250, cancellationToken)", layout, StringComparison.Ordinal);
        Assert.Contains("electronicLogbookNetwork.subscribe", layout, StringComparison.Ordinal);
        Assert.Contains("HandleNetworkRestoredAsync", layout, StringComparison.Ordinal);
        Assert.Contains("SyncHostedAfterNetworkRestoredAsync", layout, StringComparison.Ordinal);
        Assert.Contains("electronicLogbookNetwork.unsubscribe", layout, StringComparison.Ordinal);
        Assert.Contains("nav-pending-link", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".bottom-nav::before", css, StringComparison.Ordinal);
        Assert.Matches(
            @"(?s)\.bottom-nav a\.active\s*\{[^}]*color:\s*var\(--app-primary\)",
            css);
        Assert.Matches(
            @"(?s)\.bottom-nav a\.nav-pending-link\s*\{[^}]*color:\s*var\(--app-primary\)",
            css);
        Assert.Contains("touch-action: manipulation", css, StringComparison.Ordinal);
        Assert.Matches(
            @"(?s)\.bottom-nav a\s*\{[^}]*-webkit-tap-highlight-color:\s*transparent[^}]*transition:\s*color 130ms ease, opacity 100ms ease, transform 100ms ease",
            css);
        Assert.Matches(
            @"(?s)\.bottom-nav a:active\s*\{[^}]*color:\s*var\(--app-primary\)[^}]*filter:\s*none[^}]*opacity:\s*0\.72[^}]*transform:\s*scale\(0\.96\)",
            css);
        Assert.Contains("grid-template-columns: repeat(7, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains("var(--native-safe-bottom)", css, StringComparison.Ordinal);
        Assert.Contains(".bottom-nav-add-icon", css, StringComparison.Ordinal);
        Assert.Matches(
            @"(?s)\.bottom-nav \.bottom-nav-add\.active,\s*\.bottom-nav \.bottom-nav-add\.nav-pending-link\s*\{[^}]*color:\s*var\(--app-primary\)",
            css);
    }

    [Fact]
    public void Gate3LogbookViewSwitcherChangesViewsWithoutTransientLoadingFeedback()
    {
        var page = ReadMobilePage("Logbook.razor");

        Assert.Contains("role=\"group\" aria-label=\"Logbook view\"", page, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"@(ActiveView == LogbookView.Entries)\" @onclick=\"() => SelectView(LogbookView.Entries)\">Entries</button>", page, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"@(ActiveView == LogbookView.Totals)\" @onclick=\"() => SelectView(LogbookView.Totals)\">Totals</button>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("view-progress", page, StringComparison.Ordinal);
        Assert.DoesNotContain("MudProgressLinear", page, StringComparison.Ordinal);
        Assert.DoesNotContain("IsViewSwitchPending", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Yield", page, StringComparison.Ordinal);
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
    public void Gate3NewFlightAvoidsNativeSuggestionPopups()
    {
        var page = ReadMobilePage("NewFlight.razor");

        Assert.DoesNotContain("<datalist", page, StringComparison.Ordinal);
        Assert.DoesNotContain(" list=", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3NewFlightOmitsRecentRemarksPicker()
    {
        var page = ReadMobilePage("NewFlight.razor");

        Assert.DoesNotContain("Recent remarks", page, StringComparison.Ordinal);
        Assert.DoesNotContain("UseRecentRemark", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3NewFlightKeepsDraftTotalsOutOfTheScrollingView()
    {
        var page = ReadMobilePage("NewFlight.razor");

        Assert.DoesNotContain("entry-totals", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Draft totals", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3NewFlightPresentsChecksAsCompactTouchTargets()
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

        Assert.Contains("field-grid check-grid", page, StringComparison.Ordinal);
        Assert.Equal(3, page.Split("class=\"check-option\"", StringSplitOptions.None).Length - 1);
        Assert.Contains(".check-option {", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px;", css, StringComparison.Ordinal);
        Assert.Contains(".check-option input[type=\"checkbox\"]", css, StringComparison.Ordinal);
        Assert.Contains("width: 20px;", css, StringComparison.Ordinal);
        Assert.Contains("height: 20px;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate2NewFlightKeepsZeroHourFieldsBlankUntilTypedTextIsCommitted()
    {
        var page = ReadMobilePage("NewFlight.razor");

        Assert.Equal(16, CountOccurrences(page, "value=\"@MobileHourField.Format("));
        Assert.Equal(16, CountOccurrences(page, "@onchange=\"args => UpdateHour(args,"));
        Assert.DoesNotContain("ShowHourDefault", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@onfocus", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@onblur", page, StringComparison.Ordinal);
        Assert.Contains("MobileHourField.Parse(args.Value?.ToString())", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate2NewFlightUsesExplicitActionsAndReviewsValuesBeforeSaving()
    {
        var page = ReadMobilePage("NewFlight.razor");
        var session = ReadMobileSource("MobileLogbookSession.cs");
        var css = ReadMobileWebAsset("css/app.css");

        Assert.Contains("role=\"group\" aria-label=\"Flight form actions\"", page, StringComparison.Ordinal);
        Assert.Contains("OnClick=\"RequestClear\"", page, StringComparison.Ordinal);
        Assert.Contains("OnClick=\"ReviewSave\"", page, StringComparison.Ordinal);
        Assert.Contains("Clear form", page, StringComparison.Ordinal);
        Assert.Contains("@Session.SaveLabel", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Icons.Material.Filled.Refresh", page, StringComparison.Ordinal);
        Assert.DoesNotContain("MudIconButton", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Color=\"Color.Default\" OnClick=\"RequestCancel\"", page, StringComparison.Ordinal);
        Assert.Contains("Variant=\"Variant.Outlined\" Color=\"Color.Error\" OnClick=\"RequestClear\"", page, StringComparison.Ordinal);
        Assert.Contains("Variant=\"Variant.Outlined\" Color=\"Color.Success\" OnClick=\"ReviewSave\"", page, StringComparison.Ordinal);
        Assert.Contains("Icons.Material.Filled.DeleteSweep", page, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@Session.SaveLabel\"", page, StringComparison.Ordinal);
        Assert.Contains("white-space: nowrap;", css, StringComparison.Ordinal);

        Assert.Contains("Session.PrepareWorkbookDraftForReview()", page, StringComparison.Ordinal);
        Assert.Contains("Session.WorkbookDraft.ToEntry(Session.WorkbookCustomFields)", page, StringComparison.Ordinal);
        Assert.Contains(".EntryDetails(", page, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", page, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", page, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Flight values to save\"", page, StringComparison.Ordinal);
        Assert.Contains("Review before saving", page, StringComparison.Ordinal);
        Assert.Contains("OnClick=\"ConfirmSaveAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("public bool PrepareWorkbookDraftForReview()", session, StringComparison.Ordinal);
        Assert.Contains("return WorkbookDraftErrors.Count == 0;", session, StringComparison.Ordinal);

        Assert.Contains("PendingAction = DraftAction.Clear;", page, StringComparison.Ordinal);
        Assert.Contains("PendingAction = DraftAction.Cancel;", page, StringComparison.Ordinal);
        Assert.Contains("Session.ResetWorkbookDraft();", page, StringComparison.Ordinal);
        Assert.Contains("This cannot be undone.", page, StringComparison.Ordinal);
        Assert.Contains("Your changes will not be saved", page, StringComparison.Ordinal);
        Assert.Contains(".entry-action-dialog-backdrop", css, StringComparison.Ordinal);
        Assert.Contains(".entry-review-values", css, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3NewFlightReturnsToDashboardAfterTheReviewedSave()
    {
        var page = ReadMobilePage("NewFlight.razor");

        Assert.Contains("await Session.SaveWorkbookEntryAsync();", page, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo(\"/\");", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowSaveConfirmation", page, StringComparison.Ordinal);
        Assert.DoesNotContain("await Task.Delay(650);", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate3NewFlightKeepsTheCompletionActionVisibleAboveBottomNavigation()
    {
        var page = ReadMobilePage("NewFlight.razor");
        var css = ReadMobileWebAsset("css/app.css");

        Assert.Contains("class=\"persistent-action-bar\"", page, StringComparison.Ordinal);
        Assert.Contains("OnClick=\"ReviewSave\"", page, StringComparison.Ordinal);
        Assert.Matches(@"(?s)\.flight-entry-page\s*\{[^}]*padding-bottom:\s*76px", css);
        Assert.Matches(@"(?s)\.persistent-action-bar\s*\{[^}]*position:\s*fixed", css);
        Assert.Matches(@"(?s)\.persistent-action-bar\s*\{[^}]*bottom:\s*calc\(74px \+ var\(--native-safe-bottom\)\)", css);
        Assert.Matches(@"(?s)\.persistent-action-bar\s*\{[^}]*z-index:\s*25", css);
        Assert.Contains("background: color-mix(in srgb, var(--app-surface) 94%, transparent);", css, StringComparison.Ordinal);
        Assert.Contains(".action-buttons .mud-button-root", css, StringComparison.Ordinal);
        Assert.Contains("width: 100%;", css, StringComparison.Ordinal);
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
        Assert.Contains("public bool ShouldShowWorkbookDraftErrors => WorkbookDraftErrors.Count > 0 && HasAttemptedSubmit;", session, StringComparison.Ordinal);
        Assert.Contains("cache.addAll(assetsRequests)", offlineWorker, StringComparison.Ordinal);
        Assert.Contains("return cachedResponse || fetch(event.request);", offlineWorker, StringComparison.Ordinal);
        Assert.Contains(".app-main", css, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden;", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(7, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains(".bottom-nav a", css, StringComparison.Ordinal);
        Assert.Contains("min-width: 0;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate5AuditEnforcesWcagTargetSizeAndValidLogbookViewSemantics()
    {
        var logbook = ReadMobilePage("Logbook.razor");
        var css = ReadMobileWebAsset("css/app.css");
        var audit = ReadProjectFile("mobile", "scripts", "capture-pwa-visual-audit.mjs");
        var package = ReadProjectFile("mobile", "package.json");

        Assert.Contains("\"@axe-core/playwright\"", package, StringComparison.Ordinal);
        Assert.Contains("import AxeBuilder from \"@axe-core/playwright\";", audit, StringComparison.Ordinal);
        Assert.Contains("wcag22aa", audit, StringComparison.Ordinal);
        Assert.Contains("smallControlTargets", audit, StringComparison.Ordinal);
        Assert.Contains("bounds.width < 44 || bounds.height < 44", audit, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(audit, "await assertAccessible(page"));
        Assert.Contains("role=\"group\" aria-label=\"Logbook view\"", logbook, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(logbook, "aria-pressed="));
        Assert.DoesNotContain("role=\"tablist\"", logbook, StringComparison.Ordinal);
        Assert.Matches(@"(?s)\.mud-button-root\s*\{[^}]*min-height:\s*44px", css);
        Assert.Matches(@"(?s)\.accent-option\s*\{[^}]*min-height:\s*44px", css);
        Assert.Matches(@"(?s)\.currency-licence-engine-switch \.currency-engine-tab\s*\{[^}]*min-height:\s*44px", css);
    }

    [Fact]
    public void VisualAuditCoversTabletPortraitAndLandscapeProfiles()
    {
        var audit = ReadProjectFile("mobile", "scripts", "capture-pwa-visual-audit.mjs");

        Assert.Contains("{ name: \"wide-768\", width: 768, height: 1024, fontScale: 1 }", audit, StringComparison.Ordinal);
        Assert.Contains("{ name: \"ipad-landscape-1024x768\", width: 1024, height: 768, fontScale: 1 }", audit, StringComparison.Ordinal);
    }

    private static string ReadMobilePage(string relativePath) =>
        File.ReadAllText(Path.Combine(GetMobilePagesDirectory(), relativePath));

    private static string GetMobilePagesDirectory() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ElectronicLogbook.Mobile",
            "Pages"));

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
