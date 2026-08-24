namespace ElectronicLogbook.Updater.Tests;

public sealed class PrivatePilotRunbookTests
{
    [Fact]
    public void PrivatePilotRunbookWiresHealthCheckAndLocalEvidence()
    {
        var runbook = File.ReadAllText(TestRepo.FindFile("docs", "private-pilot-runbook.md"));
        var healthScript = File.ReadAllText(TestRepo.FindFile("tools", "Invoke-PrivatePilotHealthCheck.ps1"));
        var preflightScript = File.ReadAllText(TestRepo.FindFile("tools", "Invoke-PrivatePilotPreflight.ps1"));
        var rlsHarness = File.ReadAllText(TestRepo.FindFile("supabase", "tests", "hosted_pilot_rls.sql"));
        var recoveryRehearsalScript = File.ReadAllText(TestRepo.FindFile("tools", "Invoke-HostedRecoveryRehearsal.ps1"));
        var emailOtpConfigScript = File.ReadAllText(TestRepo.FindFile("tools", "Test-HostedEmailOtpConfiguration.ps1"));
        var hostedSetup = File.ReadAllText(TestRepo.FindFile("docs", "hosted-pilot-supabase.md"));

        Assert.Contains("artifacts/private-pilot-20260806/cohort.md", runbook, StringComparison.Ordinal);
        Assert.Contains("Invoke-PrivatePilotHealthCheck.ps1", runbook, StringComparison.Ordinal);
        Assert.Contains("Invoke-PrivatePilotPreflight.ps1", runbook, StringComparison.Ordinal);
        Assert.Contains("ELB_SUPABASE_PILOT_DB_URL", runbook, StringComparison.Ordinal);
        Assert.Contains("get_hosted_pilot_health", runbook, StringComparison.Ordinal);

        Assert.Contains("ELB_SUPABASE_PILOT_DB_URL", healthScript, StringComparison.Ordinal);
        Assert.Contains("public.get_hosted_pilot_health", healthScript, StringComparison.Ordinal);
        Assert.Contains("paidPlanUpgradeTriggers", healthScript, StringComparison.Ordinal);
        Assert.Contains("localReviewFindings", healthScript, StringComparison.Ordinal);
        Assert.Contains("The value is never printed", healthScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host $ConnectionString", healthScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Output $ConnectionString", healthScript, StringComparison.Ordinal);

        Assert.Contains("docs\\private-pilot-runbook.md", preflightScript, StringComparison.Ordinal);
        Assert.Contains("supabase\\tests\\hosted_pilot_rls.sql", preflightScript, StringComparison.Ordinal);
        Assert.Contains("Invoke-PrivatePilotHealthCheck.ps1", preflightScript, StringComparison.Ordinal);
        Assert.Contains("ElectronicLogbook\\Supabase", preflightScript, StringComparison.Ordinal);
        Assert.Contains("hosted-pilot-projects.local.json", preflightScript, StringComparison.Ordinal);
        Assert.Contains("access-token.txt", preflightScript, StringComparison.Ordinal);
        Assert.Contains("private-pilot database region is ap-southeast-2", preflightScript, StringComparison.Ordinal);
        Assert.Contains("Supabase management token sees active private-pilot project in ap-southeast-2", preflightScript, StringComparison.Ordinal);
        Assert.Contains("private-pilot project is not active and healthy", preflightScript, StringComparison.Ordinal);
        Assert.Contains("Auth signup disabled with invited-user email and Google recovery only", preflightScript, StringComparison.Ordinal);
        Assert.Contains("Google returning-user recovery is not enabled", preflightScript, StringComparison.Ordinal);
        Assert.Contains("one or more unapproved Auth providers are enabled", preflightScript, StringComparison.Ordinal);
        Assert.Contains("and one hosted logbook", preflightScript, StringComparison.Ordinal);
        Assert.DoesNotContain("and no hosted logbook", preflightScript, StringComparison.Ordinal);
        Assert.DoesNotContain("artifacts\\private-pilot-20260806\\cohort.md", preflightScript, StringComparison.Ordinal);
        Assert.Contains("secretHandling", preflightScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host $ConnectionString", preflightScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Output $ConnectionString", preflightScript, StringComparison.Ordinal);

        Assert.Contains("elb_rls_test.baseline_health", rlsHarness, StringComparison.Ordinal);
        Assert.Contains("grant select on elb_rls_test.baseline_health to authenticated", rlsHarness, StringComparison.Ordinal);
        Assert.Contains("baseline.active_account_count + 4", rlsHarness, StringComparison.Ordinal);
        Assert.Contains("baseline.active_device_count + 4", rlsHarness, StringComparison.Ordinal);
        Assert.Contains("baseline.stored_operation_count + 2", rlsHarness, StringComparison.Ordinal);
        Assert.DoesNotContain("stored_operation_count = 2", rlsHarness, StringComparison.Ordinal);

        Assert.Contains("displayed six-digit", runbook, StringComparison.Ordinal);
        Assert.DoesNotContain("OTP or magic-link", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("auth-dev.flightlogx.app", hostedSetup, StringComparison.Ordinal);
        Assert.Contains("auth.flightlogx.app", hostedSetup, StringComparison.Ordinal);
        Assert.Contains("Test-HostedEmailOtpConfiguration.ps1", hostedSetup, StringComparison.Ordinal);
        Assert.Contains("mailer_otp_exp", emailOtpConfigScript, StringComparison.Ordinal);
        Assert.Contains("rate_limit_email_sent", emailOtpConfigScript, StringComparison.Ordinal);
        Assert.Contains("rate_limit_otp", emailOtpConfigScript, StringComparison.Ordinal);
        Assert.Contains("ConfirmationURL|TokenHash", emailOtpConfigScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host $managementToken", emailOtpConfigScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Output $managementToken", emailOtpConfigScript, StringComparison.Ordinal);

        Assert.Contains("ACTIVE_HEALTHY", recoveryRehearsalScript, StringComparison.Ordinal);
        Assert.Contains("Sydney development project", recoveryRehearsalScript, StringComparison.Ordinal);
        Assert.Contains("public Auth signup to be disabled", recoveryRehearsalScript, StringComparison.Ordinal);
        Assert.Contains("email to be the only enabled external Auth provider", recoveryRehearsalScript, StringComparison.Ordinal);
        Assert.Contains("$psqlExecutablePath", recoveryRehearsalScript, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL 17 psql executable path could not be resolved", recoveryRehearsalScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host $managementToken", recoveryRehearsalScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Output $managementToken", recoveryRehearsalScript, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicReleaseHardeningRemainsExplicitlyGated()
    {
        var gate = File.ReadAllText(TestRepo.FindFile("docs", "public-release-hardening-gate.md"));

        Assert.Contains("Status: intentionally not started", gate, StringComparison.Ordinal);
        Assert.Contains("private pilot exit decision is `pass` or `pass with issues`", gate, StringComparison.Ordinal);
        Assert.Contains("project owner explicitly decides to pursue public release", gate, StringComparison.Ordinal);
        Assert.Contains("Do not start these until the entry criteria pass", gate, StringComparison.Ordinal);
        Assert.Contains("public signup or waitlist", gate, StringComparison.Ordinal);
        Assert.Contains("billing, subscriptions", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveWorkbookOtpRehearsalVerifiesTheExactWorkbookIdentity()
    {
        var rehearsal = File.ReadAllText(TestRepo.FindFile(
            "supabase", "tests", "HostedRecoveryRehearsal", "Program.cs"));

        Assert.Contains("ELB_REHEARSAL_LIVE_WORKBOOK_PATH", rehearsal, StringComparison.Ordinal);
        Assert.Contains("PortableLogbookCommandRunner.ReadHostedStatus", rehearsal, StringComparison.Ordinal);
        Assert.Contains("workbookAccountId == accountId", rehearsal, StringComparison.Ordinal);
        Assert.Contains("workbookLogbookId == logbookId", rehearsal, StringComparison.Ordinal);
        Assert.Contains("device_id=eq.{workbookDeviceId}", rehearsal, StringComparison.Ordinal);
        Assert.Contains(
            "ReadAcknowledgementAsync(http, serviceRoleKey, logbookId, workbookDeviceId)",
            rehearsal,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WorkbookClientInvestigationUsesTheProductionClientAndIndependentDeviceQueries()
    {
        var rehearsal = File.ReadAllText(TestRepo.FindFile(
            "supabase", "tests", "HostedRecoveryRehearsal", "Program.cs"));
        var launcher = File.ReadAllText(TestRepo.FindFile("tools", "Invoke-HostedRecoveryRehearsal.ps1"));

        Assert.Contains("WorkbookClientInvestigation", launcher, StringComparison.Ordinal);
        Assert.Contains("ELB_REHEARSAL_WORKBOOK_CLIENT", launcher, StringComparison.Ordinal);
        Assert.Contains("new SupabaseWorkbookConnectionClient", rehearsal, StringComparison.Ordinal);
        Assert.Contains("connectionClient.RestoreWorkbookKeyAsync", rehearsal, StringComparison.Ordinal);
        Assert.Contains("connectionClient.ActivateWorkbookDeviceAsync", rehearsal, StringComparison.Ordinal);
        Assert.Contains("ReadDeviceObservationPairAsync", rehearsal, StringComparison.Ordinal);
        Assert.Contains("from public.devices", rehearsal, StringComparison.Ordinal);
        Assert.Contains("device_id=eq.{deviceId}", rehearsal, StringComparison.Ordinal);
    }
}
