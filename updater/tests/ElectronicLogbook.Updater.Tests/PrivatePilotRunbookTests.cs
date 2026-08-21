namespace ElectronicLogbook.Updater.Tests;

public sealed class PrivatePilotRunbookTests
{
    [Fact]
    public void PrivatePilotRunbookWiresHealthCheckAndLocalEvidence()
    {
        var runbook = File.ReadAllText(TestRepo.FindFile("docs", "private-pilot-runbook.md"));
        var healthScript = File.ReadAllText(TestRepo.FindFile("tools", "Invoke-PrivatePilotHealthCheck.ps1"));
        var preflightScript = File.ReadAllText(TestRepo.FindFile("tools", "Invoke-PrivatePilotPreflight.ps1"));
        var recoveryRehearsalScript = File.ReadAllText(TestRepo.FindFile("tools", "Invoke-HostedRecoveryRehearsal.ps1"));

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
        Assert.Contains("secretHandling", preflightScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host $ConnectionString", preflightScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Output $ConnectionString", preflightScript, StringComparison.Ordinal);

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
}
