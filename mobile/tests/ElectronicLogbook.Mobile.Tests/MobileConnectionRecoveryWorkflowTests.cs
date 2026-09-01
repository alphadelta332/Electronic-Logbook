using System.Security.Cryptography;
using ElectronicLogbook.Mobile;
using ElectronicLogbook.Portable;
using Microsoft.JSInterop;

namespace ElectronicLogbook.Mobile.Tests;

public sealed class MobileConnectionRecoveryWorkflowTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-07T01:00:00Z");

    public static TheoryData<MobileConnectionStage> EveryStage
    {
        get
        {
            var data = new TheoryData<MobileConnectionStage>();
            foreach (var stage in Enum.GetValues<MobileConnectionStage>())
            {
                data.Add(stage);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EveryStage))]
    public async Task FailureAtEveryStageReportsExactStageCodeAndExceptionAndCleansIncompleteArtifacts(
        MobileConnectionStage failingStage)
    {
        var js = new ProbeJsRuntime();
        var remote = new RecoveryClientStub();
        var clean = CreateWorkflow(js, remote);
        MobileConnectionDiagnosticReport report;

        if (failingStage <= MobileConnectionStage.LOCAL_PLAN_CREATE)
        {
            report = await CreateWorkflow(js, remote, failingStage).RunPreflightAsync();
        }
        else
        {
            var preflight = await clean.RunPreflightAsync();
            Assert.True(preflight.Passed);
            report = (await CreateWorkflow(js, remote, failingStage).RecoverAsync(preflight)).Diagnostics;
        }

        Assert.False(report.Passed);
        Assert.Equal(failingStage, report.CurrentStage);
        Assert.Equal($"TEST_{failingStage}", report.ErrorCode);
        var failure = Assert.Single(report.Stages, stage => !stage.Passed);
        Assert.Equal(typeof(MobileHostedDiagnosticException).FullName, failure.ExceptionType);
        Assert.Contains("Injected diagnostic failure", failure.ExceptionMessage, StringComparison.Ordinal);
        Assert.NotNull(failure.StackTrace);
        Assert.Empty(js.PackageKeys);
        Assert.DoesNotContain("portable-document", js.Values.Keys);
        Assert.DoesNotContain(js.Values.Keys, key => key.StartsWith("diagnostic-probe:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GreenPreflightThenRecoveryImportsOneRealKeyAndVerifiesSavedIdentifiers()
    {
        var js = new ProbeJsRuntime();
        var workflow = CreateWorkflow(js, new RecoveryClientStub());

        var preflight = await workflow.RunPreflightAsync();
        var result = await workflow.RecoverAsync(preflight);

        Assert.True(preflight.Passed);
        Assert.Equal(MobileConnectionStage.LOCAL_PLAN_CREATE, preflight.CurrentStage);
        Assert.True(result.Diagnostics.Passed);
        Assert.Equal(MobileConnectionStage.COMPLETE, result.Diagnostics.CurrentStage);
        Assert.Equal("COMPLETE", result.Diagnostics.ErrorCode);
        Assert.NotNull(result.Document);
        Assert.NotNull(result.HostedSync);
        Assert.Equal(result.Document.LogbookId, result.HostedSync.LogbookId);
        Assert.Single(js.PackageKeys);
        Assert.Contains("portable-document", js.Values.Keys);
        Assert.DoesNotContain(js.Values.Keys, key => key.StartsWith("diagnostic-probe:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InterruptedKeystoreProbeDeletesDisposableKey()
    {
        var js = new ProbeJsRuntime { InterruptEncryption = true };

        var report = await CreateWorkflow(js, new RecoveryClientStub()).RunPreflightAsync();

        Assert.False(report.Passed);
        Assert.Equal(MobileConnectionStage.ANDROID_KEYSTORE_PROBE, report.CurrentStage);
        Assert.StartsWith("UNEXPECTED_", report.ErrorCode, StringComparison.Ordinal);
        Assert.Empty(js.PackageKeys);
    }

    [Fact]
    public async Task FailedRecoveryRestoresPriorLocalStateAndRetainsNoIncompleteKey()
    {
        var js = new ProbeJsRuntime();
        var store = new BrowserLogbookStore(js);
        var previousDocument = PortableLogbookDocumentV2.CreateAustraliaFirst(
            new LogbookId("log_90000000000000000000000000000001"),
            MobileLogbookSession.CustomFields,
            PortableLogbookCurrencyOverrideDates.Empty,
            []);
        await store.SaveStateAsync(new BrowserLogbookStateV2(previousDocument, [], null));
        var previousJson = js.Values["portable-document"];
        var remote = new RecoveryClientStub();
        var clean = CreateWorkflow(js, remote);
        var preflight = await clean.RunPreflightAsync();

        var result = await CreateWorkflow(js, remote, MobileConnectionStage.LOCAL_STATE_READBACK).RecoverAsync(preflight);

        Assert.False(result.Diagnostics.Passed);
        Assert.Equal(previousJson, js.Values["portable-document"]);
        Assert.Empty(js.PackageKeys);
    }

    [Fact]
    public void RedactorRemovesSecretsEmailsUrlsPayloadsAndFullIdentifiers()
    {
        const string raw = "owner@example.com https://preview.supabase.co access_token=eyJabcdefghijk.abcdefghijklmnop.abcdefghijklmnop " +
            "payload_ciphertext=QWxhZGRpbjpvcGVuIHNlc2FtZVF1aXRlTG9uZ1NlY3JldFZhbHVlMTIzNDU2 " +
            "acct_10000000000000000000000000000001 10000000-0000-0000-0000-000000000001";

        var redacted = MobileDiagnosticRedactor.Redact(raw);

        Assert.DoesNotContain("owner@example.com", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("preview.supabase.co", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJabcdefghijk", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("QWxhZGRpb", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("10000000000000000000000000000001", redacted, StringComparison.Ordinal);
        Assert.Contains("[secret-redacted]", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownExceptionUsesRealTypeAndRedactedMessage()
    {
        var js = new ProbeJsRuntime();
        var workflow = new MobileConnectionRecoveryWorkflow(
            new RecoveryClientStub(),
            new BrowserLogbookStore(js),
            new BrowserPackageKeyStore(js),
            new ManualSyncClock(Now),
            faultInjector: stage => stage == MobileConnectionStage.CONFIG_LOAD
                ? new InvalidDataException("owner@example.com https://preview.supabase.co")
                : null);

        var report = await workflow.RunPreflightAsync();
        var json = report.ToRedactedJson();

        Assert.Equal("UNEXPECTED_InvalidDataException", report.ErrorCode);
        Assert.Contains(nameof(InvalidDataException), json, StringComparison.Ordinal);
        Assert.DoesNotContain("owner@example.com", json, StringComparison.Ordinal);
        Assert.DoesNotContain("preview.supabase.co", json, StringComparison.Ordinal);
    }

    private static MobileConnectionRecoveryWorkflow CreateWorkflow(
        ProbeJsRuntime js,
        RecoveryClientStub remote,
        MobileConnectionStage? failingStage = null) =>
        new(
            remote,
            new BrowserLogbookStore(js),
            new BrowserPackageKeyStore(js),
            new ManualSyncClock(Now),
            faultInjector: stage => stage == failingStage
                ? new MobileHostedDiagnosticException($"TEST_{stage}", "Injected diagnostic failure.")
                : null);

    private sealed class RecoveryClientStub : IMobileHostedRecoveryClient
    {
        private static readonly HostedAccountId AccountId = new("acct_10000000000000000000000000000001");
        private static readonly DeviceId DeviceId = new("dev_40000000000000000000000000000001");

        public ValueTask ValidateConfigAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<MobileHostedCredentialSnapshot> LoadCredentialSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MobileHostedCredentialSnapshot(MobileCredentialState.Registered, AccountId, DeviceId, Now.AddHours(1)));

        public ValueTask ValidateAccessTokenAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<MobileHostedPrincipal> ReadAuthUserAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MobileHostedPrincipal(AccountId));

        public ValueTask<MobileHostedAccountCheck> ReadAccountAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MobileHostedAccountCheck(true, true, true));

        public ValueTask<MobileHostedDeviceCheck> ReadDeviceAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MobileHostedDeviceCheck(true, true, true));

        public ValueTask<HostedSyncSession> GetRegisteredSessionAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HostedSyncSession(AccountId, DeviceId, Now.AddHours(1)));
    }

    private sealed class ProbeJsRuntime : IJSRuntime
    {
        public Dictionary<string, string> Values { get; } = [];
        public Dictionary<string, byte[]> PackageKeys { get; } = [];
        public bool InterruptEncryption { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return identifier switch
            {
                "electronicLogbookStore.load" => Result<TValue>(Values.GetValueOrDefault((string)args![0]!)),
                "electronicLogbookStore.save" => Save<TValue>(args),
                "electronicLogbookStore.delete" => DeleteValue<TValue>(args),
                "electronicLogbookKeys.importPackageKey" => ImportKey<TValue>(args),
                "electronicLogbookKeys.hasPackageKey" => Result<TValue>(PackageKeys.ContainsKey((string)args![0]!)),
                "electronicLogbookKeys.encrypt" => Encrypt<TValue>(args),
                "electronicLogbookKeys.decrypt" => Decrypt<TValue>(args),
                "electronicLogbookKeys.deletePackageKey" => DeleteKey<TValue>(args),
                _ => throw new JSException($"Unexpected JS call: {identifier}")
            };
        }

        private ValueTask<TValue> Save<TValue>(object?[]? args)
        {
            Values[(string)args![0]!] = (string)args[1]!;
            return Result<TValue>(null);
        }

        private ValueTask<TValue> DeleteValue<TValue>(object?[]? args)
        {
            Values.Remove((string)args![0]!);
            return Result<TValue>(null);
        }

        private ValueTask<TValue> ImportKey<TValue>(object?[]? args)
        {
            PackageKeys[(string)args![0]!] = ((byte[])args[1]!).ToArray();
            return Result<TValue>(true);
        }

        private ValueTask<TValue> DeleteKey<TValue>(object?[]? args)
        {
            PackageKeys.Remove((string)args![0]!);
            return Result<TValue>(null);
        }

        private ValueTask<TValue> Encrypt<TValue>(object?[]? args)
        {
            if (InterruptEncryption)
            {
                throw new OperationCanceledException("Simulated interruption.");
            }

            var key = PackageKeys[(string)args![0]!];
            var nonce = (byte[])args[1]!;
            var plaintext = (byte[])args[2]!;
            var aad = (byte[])args[3]!;
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
            return Result<TValue>(new BrowserPackageCiphertext(ciphertext, tag));
        }

        private ValueTask<TValue> Decrypt<TValue>(object?[]? args)
        {
            var key = PackageKeys[(string)args![0]!];
            var nonce = (byte[])args[1]!;
            var ciphertext = (byte[])args[2]!;
            var tag = (byte[])args[3]!;
            var aad = (byte[])args[4]!;
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
            return Result<TValue>(plaintext);
        }

        private static ValueTask<TValue> Result<TValue>(object? value) =>
            new(value is null ? default! : (TValue)value);
    }
}
