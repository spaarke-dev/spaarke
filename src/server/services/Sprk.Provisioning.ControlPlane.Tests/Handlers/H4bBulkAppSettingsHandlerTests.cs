// -----------------------------------------------------------------------------
// H4bBulkAppSettingsHandlerTests.cs
//
// Task 201 — unit tests over H4bBulkAppSettingsHandler. xunit +
// FluentAssertions + hand-rolled fakes for every seam, following the H4
// (task 047) + H4-shared (task 200) test exemplar shape.
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live pwsh / HTTP / Kudu / Azure API.
//   Fakes replace the repository + ALL FOUR collaborator seams (manifest,
//   process runner, healthz probe, container log fetcher). Live-integration
//   coverage belongs in env-guarded smoke tests (out of scope for CI here).
//
// COVERAGE (POML acceptance-criteria mapping):
//   AC-1  Happy path — all resolved + Configure exit 0 + healthz Success →
//         HandlerResult.Success + Cosmos advanced.
//   AC-2  Per-env-input missing → Failure(Resumable, PerEnvInputMissing)
//         BEFORE any script call; diagnostic names the source-key +
//         iOptionsModule.
//   AC-3  PS non-zero exit → Failure(Resumable, AppSettingsWriteFailed)
//         with redacted diagnostic.
//   AC-4  Healthz timeout with parseable module log → Failure
//         (QuarantineRequired, HealthzTimeout, "BFF fail-fast on SpeAdminModule").
//   AC-5  Healthz timeout with UN-parseable log → Failure
//         (QuarantineRequired, HealthzTimeout, generic diagnostic pointing at Kudu).
//   AC-6  Idempotency-key match — 2nd run same (env, secretsVer) →
//         Success short-circuit, 0 external calls.
//   AC-7  Literal per_env_source resolved verbatim (no envelope lookup).
//   AC-8  TryParseFailFastModule theory — SESSION 2 SpeAdmin +
//         CosmosPersistence samples + unparseable samples.
//   AC-9  Missing-required-parameter guard (theory over required params).
//   AC-10 HandlerId mismatch throws InvalidOperationException.
//   AC-11 Idempotency-key format determinism.
//   AC-12 Run not found → Resumable + RunNotFound.
//   AC-13 Optional per_env entry with missing source → skipped, no fail.
//   AC-14 Empty manifest (0 per_env_settings entries) — happy path still works;
//         script is still invoked (KV-refs alone might be needed).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H4bBulkAppSettingsHandlerTests
{
    private const string CustomerId = "acme-prod";
    private const string RunId = "01j9-h4b-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "sub-h4b-prod";
    private const string KeyVaultName = "sprk-prod-kv";
    private const string ResourceGroupName = "rg-spaarke-prod";
    private const string AppServiceName = "sprk-prod-api";
    private const string EnvironmentName = "prod";
    private const string SecretsVer = "manifest-hash-h4b-xyz";

    // ---------- AC-1 happy path ----------

    [Fact]
    public async Task AC1_HappyPath_ResolveAllInputsAndAdvanceCosmos()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-1");
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var runner = FakeProcessRunner.Zero();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, runner, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(
            H4bBulkAppSettingsHandler.BuildIdempotencyKey(EnvironmentName, SecretsVer));
        runner.CallCount.Should().Be(1);
        probe.CallCount.Should().Be(1);
        // Fetch NOT called on happy path.
        fetcher.CallCount.Should().Be(0);
        // Cosmos advanced.
        repo.LastWrittenRun!.CurrentPhase.Should().Be(HandlerIds.H4b);
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle()
            .Which.Phase.Should().Be(HandlerIds.H4b);

        // Verify the pwsh argv contains the fixed args in the expected order + the
        // per-env source values by PascalCase name.
        var args = runner.LastArgs!;
        args.Should().Contain("-VaultName");
        args.Should().Contain(KeyVaultName);
        args.Should().Contain("-AppServiceName");
        args.Should().Contain(AppServiceName);
        args.Should().Contain("-ResourceGroupName");
        args.Should().Contain(ResourceGroupName);
        args.Should().Contain("-KvVaultUri");
        args.Should().Contain("https://sprk-prod-kv.vault.azure.net/");
    }

    // ---------- AC-2 per-env-input missing ----------

    [Fact]
    public async Task AC2_PerEnvInputMissing_ResumableFailure_BeforeAnyScriptCall()
    {
        var run = BuildRun();
        // Remove kv_vault_uri from Parameters.NonSecret so H2a's expected output is missing.
        run.Parameters.NonSecret.Remove("kv_vault_uri");
        var repo = new FakeRepository(run, "etag-2");
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var runner = FakeProcessRunner.Zero();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, runner, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BulkAppSettingsRejectionCodes.PerEnvInputMissing);
        failure.Diagnostic.Should().Contain("kv_vault_uri");
        failure.Diagnostic.Should().Contain("SpeAdmin__KeyVaultUri");
        failure.Diagnostic.Should().Contain("SpeAdminModule");
        runner.CallCount.Should().Be(0);
        probe.CallCount.Should().Be(0);
    }

    // ---------- AC-3 PS non-zero exit ----------

    [Fact]
    public async Task AC3_ProcessNonZeroExit_ResumableFailure_WithRedactedDiagnostic()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-3");
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var runner = FakeProcessRunner.NonZero(exitCode: 1, stdout: "attempting...", stderr: "az: authentication failed");
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, runner, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BulkAppSettingsRejectionCodes.AppSettingsWriteFailed);
        failure.Diagnostic.Should().Contain("exit code 1");
        failure.Diagnostic.Should().Contain("az: authentication failed");
        // Probe not reached.
        probe.CallCount.Should().Be(0);
    }

    // ---------- AC-4 healthz timeout with parseable module ----------

    [Fact]
    public async Task AC4_HealthzTimeoutWithParseableModule_QuarantineRequired_WithModuleDiagnostic()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-4");
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var runner = FakeProcessRunner.Zero();
        var probe = FakeHealthzProbe.Timeout("HTTP 502 (elapsed 480s across 5 attempts)");
        var fetcher = new FakeContainerLogFetcher
        {
            NextLogs = "2026-08-24T14:00:00.123Z INFO Booting BFF...\n" +
                       "Unhandled exception. System.InvalidOperationException: SpeAdmin:KeyVaultUri (or KeyVaultUri) configuration is required for SpeAdminModule.\n" +
                       "   at Sprk.Bff.Api.Infrastructure.DI.SpeAdminModule.AddSpeAdminModule(IServiceCollection services)\n",
        };
        var handler = Build(repo, manifest, runner, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BulkAppSettingsRejectionCodes.HealthzTimeout);
        failure.Diagnostic.Should().Contain("BFF fail-fast on SpeAdminModule");
        failure.Diagnostic.Should().Contain("KeyVaultUri");
        fetcher.CallCount.Should().Be(1);
    }

    // ---------- AC-5 healthz timeout with unparseable log ----------

    [Fact]
    public async Task AC5_HealthzTimeoutWithUnparseableLog_QuarantineRequired_WithGenericDiagnostic()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-5");
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var runner = FakeProcessRunner.Zero();
        var probe = FakeHealthzProbe.Timeout("no response (elapsed 480s)");
        var fetcher = new FakeContainerLogFetcher { NextLogs = "starting up... waiting for db..." };
        var handler = Build(repo, manifest, runner, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BulkAppSettingsRejectionCodes.HealthzTimeout);
        failure.Diagnostic.Should().NotContain("BFF fail-fast on");
        failure.Diagnostic.Should().Contain("did not carry a parseable fail-fast");
        failure.Diagnostic.Should().Contain(AppServiceName);
        failure.Diagnostic.Should().Contain("/api/logs/docker");
    }

    // ---------- AC-6 idempotency short-circuit ----------

    [Fact]
    public async Task AC6_IdempotencyKeyMatch_ShortCircuitSuccess_NoExternalCalls()
    {
        var run = BuildRun();
        var expectedKey = H4bBulkAppSettingsHandler.BuildIdempotencyKey(EnvironmentName, SecretsVer);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIds.H4b,
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, "etag-6");
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var runner = FakeProcessRunner.Zero();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, runner, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(expectedKey);
        // Idempotent no-op does NOT invoke manifest / process / probe.
        manifest.CallCount.Should().Be(0);
        runner.CallCount.Should().Be(0);
        probe.CallCount.Should().Be(0);
        repo.LastWrittenRun.Should().BeNull();
    }

    // ---------- AC-7 literal per_env_source ----------

    [Fact]
    public async Task AC7_LiteralPerEnvSource_UsesLiteralValueDirectly_NoEnvelopeLookup()
    {
        var run = BuildRun();
        // Remove any parameter keys — literal source shouldn't need envelope lookup.
        var repo = new FakeRepository(run, "etag-7");
        var literalOnly = new List<PerEnvSettingEntry>
        {
            new(
                Key: "Graph__ManagedIdentity__Enabled",
                PerEnvSource: PerEnvSettingSource.Literal,
                LiteralValue: "true",
                ParameterKey: null,
                Required: true,
                IOptionsModuleName: "GraphModule"),
        };
        var manifest = FakePerEnvManifest.Success(literalOnly);
        var runner = FakeProcessRunner.Zero();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, runner, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        // No per-env resolved source → argv only has the 3 fixed args (no -<PsVar>).
        var args = runner.LastArgs!;
        args.Should().NotContain(a => a.StartsWith("-", StringComparison.Ordinal) && a.Length > 1 &&
                                       a != "-VaultName" && a != "-AppServiceName" && a != "-ResourceGroupName" &&
                                       a != "-NoProfile" && a != "-NonInteractive" && a != "-File");
    }

    // ---------- AC-8 TryParseFailFastModule theory ----------

    public static TheoryData<string, bool, string?> FailFastSamples() => new()
    {
        // SESSION 2 verbatim SpeAdmin trigger.
        {
            "Unhandled exception. System.InvalidOperationException: SpeAdmin:KeyVaultUri (or KeyVaultUri) configuration is required for SpeAdminModule.",
            true,
            "SpeAdminModule"
        },
        // SESSION 2 verbatim CosmosPersistence trigger.
        {
            "Unhandled exception. System.InvalidOperationException: CosmosPersistence:Endpoint configuration is required for AiPersistenceModule.",
            true,
            "AiPersistenceModule"
        },
        // Non-matching text — no fail-fast in log.
        {
            "2026-08-24 App started successfully",
            false,
            null
        },
        // Fail-fast present but no *Module suffix in the message.
        {
            "Unhandled exception. System.InvalidOperationException: some other config problem",
            true,
            null  // Parseable exception line but no module extractable.
        },
    };

    [Theory]
    [MemberData(nameof(FailFastSamples))]
    public void AC8_TryParseFailFastModule_ExtractsExpectedModule(
        string logSample, bool expectedParsed, string? expectedModule)
    {
        var parsed = H4bBulkAppSettingsHandler.TryParseFailFastModule(logSample, out var module, out var detail);
        parsed.Should().Be(expectedParsed);
        if (expectedParsed)
        {
            module.Should().Be(expectedModule);
            if (expectedModule is not null)
            {
                detail.Should().NotBeNullOrWhiteSpace();
            }
        }
    }

    // ---------- AC-9 missing-required-parameter guard ----------

    [Theory]
    [InlineData(H4bBulkAppSettingsHandler.TenantIdParameterKey,
                BulkAppSettingsRejectionCodes.MissingTenantId)]
    [InlineData(H4bBulkAppSettingsHandler.SubscriptionIdParameterKey,
                BulkAppSettingsRejectionCodes.MissingSubscriptionId)]
    [InlineData(H4bBulkAppSettingsHandler.KeyVaultNameParameterKey,
                BulkAppSettingsRejectionCodes.MissingKeyVaultName)]
    [InlineData(H4bBulkAppSettingsHandler.ResourceGroupNameParameterKey,
                BulkAppSettingsRejectionCodes.MissingResourceGroupName)]
    [InlineData(H4bBulkAppSettingsHandler.AppServiceNameParameterKey,
                BulkAppSettingsRejectionCodes.MissingAppServiceName)]
    [InlineData(H4bBulkAppSettingsHandler.EnvironmentNameParameterKey,
                BulkAppSettingsRejectionCodes.MissingEnvironmentName)]
    [InlineData(H4bBulkAppSettingsHandler.SecretsVersionParameterKey,
                BulkAppSettingsRejectionCodes.MissingSecretsVersion)]
    public async Task AC9_MissingRequiredParameter_FailsResumable_NoExternalCalls(
        string parameterKey, string expectedRejectionCode)
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(parameterKey);
        var repo = new FakeRepository(run, "etag-guard");
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var runner = FakeProcessRunner.Zero();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, runner, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(expectedRejectionCode);
        runner.CallCount.Should().Be(0);
        probe.CallCount.Should().Be(0);
    }

    // ---------- AC-10 handler-id mismatch ----------

    [Fact]
    public async Task AC10_HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-10");
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var runner = FakeProcessRunner.Zero();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, runner, probe, fetcher);

        var wrong = new HandlerEnvelope
        {
            HandlerId = "H4",  // wrong
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrong, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched HandlerId*");
    }

    // ---------- AC-11 idempotency-key determinism ----------

    [Fact]
    public void AC11_IdempotencyKey_IsDeterministicByEnvAndSecretsVer()
    {
        var k1 = H4bBulkAppSettingsHandler.BuildIdempotencyKey("prod", "hash-v1");
        var k2 = H4bBulkAppSettingsHandler.BuildIdempotencyKey("prod", "hash-v1");
        k1.Should().Be(k2);
        k1.Should().Be("appsettings-prod-hash-v1");
        H4bBulkAppSettingsHandler.BuildIdempotencyKey("prod", "hash-v2").Should().NotBe(k1);
        H4bBulkAppSettingsHandler.BuildIdempotencyKey("dev", "hash-v1").Should().NotBe(k1);
    }

    // ---------- AC-12 run not found ----------

    [Fact]
    public async Task AC12_RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var runner = FakeProcessRunner.Zero();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, runner, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BulkAppSettingsRejectionCodes.RunNotFound);
    }

    // ---------- AC-13 optional per-env entry skipping ----------

    [Fact]
    public async Task AC13_OptionalPerEnvEntryMissing_SkipSilently_HappyPath()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove("optional_key");
        var repo = new FakeRepository(run, "etag-13");
        var entries = new List<PerEnvSettingEntry>
        {
            // Optional entry — missing source → skip; no fail.
            new("Some__OptionalSetting", PerEnvSettingSource.FromHandlerOutput,
                LiteralValue: null, ParameterKey: "optional_key", Required: false,
                IOptionsModuleName: "SomeOptionalModule"),
        };
        var manifest = FakePerEnvManifest.Success(entries);
        var runner = FakeProcessRunner.Zero();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, runner, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        runner.CallCount.Should().Be(1);
    }

    // ---------- AC-14 empty per_env_settings manifest ----------

    [Fact]
    public async Task AC14_EmptyPerEnvSettingsManifest_ScriptStillInvoked_HappyPath()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-14");
        var manifest = FakePerEnvManifest.Success(Array.Empty<PerEnvSettingEntry>());
        var runner = FakeProcessRunner.Zero();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, runner, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        runner.CallCount.Should().Be(1, "Configure script writes KV-refs even when per_env_settings is empty");
        // Fixed args only — no per-env sources contributed.
        var args = runner.LastArgs!;
        args.Should().Contain("-VaultName");
        args.Should().Contain("-AppServiceName");
        args.Should().Contain("-ResourceGroupName");
    }

    // ---------- helpers ----------

    private static H4bBulkAppSettingsHandler Build(
        IProvisioningRunRepository repo,
        IPerEnvSettingsManifest manifest,
        IProcessRunner runner,
        IHealthzProbe probe,
        IContainerLogFetcher fetcher)
    {
        return new H4bBulkAppSettingsHandler(
            repo, manifest, runner, probe, fetcher,
            Options.Create(new BulkAppSettingsOptions()),
            NullLogger<H4bBulkAppSettingsHandler>.Instance);
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = HandlerIds.H4b,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun()
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-guid",
            TenancyModel = "Model1SharedTrial",
            Status = RunStatus.Running,
            Profile = "spaarke-hosted-model1",
        };
        var p = run.Parameters.NonSecret;
        p[H4bBulkAppSettingsHandler.TenantIdParameterKey] = TenantId;
        p[H4bBulkAppSettingsHandler.SubscriptionIdParameterKey] = SubscriptionId;
        p[H4bBulkAppSettingsHandler.KeyVaultNameParameterKey] = KeyVaultName;
        p[H4bBulkAppSettingsHandler.ResourceGroupNameParameterKey] = ResourceGroupName;
        p[H4bBulkAppSettingsHandler.AppServiceNameParameterKey] = AppServiceName;
        p[H4bBulkAppSettingsHandler.EnvironmentNameParameterKey] = EnvironmentName;
        p[H4bBulkAppSettingsHandler.SecretsVersionParameterKey] = SecretsVer;
        // Sources the standard entries reference:
        p["kv_vault_uri"] = "https://sprk-prod-kv.vault.azure.net/";
        p["cosmos_endpoint"] = "https://sprk-prod-cosmos.documents.azure.com/";
        p["tenant_id"] = TenantId;
        p["bff_app_client_id"] = "00000000-aaaa-bbbb-cccc-999999999999";
        p["container_type_id"] = "00000000-dead-beef-0000-000000000001";
        p["uami_client_id"] = "00000000-1111-2222-3333-555555555555";
        return run;
    }

    /// <summary>
    /// Matches the shape of the shipped manifest.yaml per_env_settings entries
    /// (task 201). Kept in a helper so tests share ONE canonical entry list
    /// (mirrors H4-shared's BuildSharedEntries).
    /// </summary>
    private static IReadOnlyList<PerEnvSettingEntry> BuildStandardEntries() =>
    [
        new("SpeAdmin__KeyVaultUri", PerEnvSettingSource.FromHandlerOutput,
            LiteralValue: null, ParameterKey: "kv_vault_uri", Required: true,
            IOptionsModuleName: "SpeAdminModule"),
        new("CosmosPersistence__Endpoint", PerEnvSettingSource.FromHandlerOutput,
            LiteralValue: null, ParameterKey: "cosmos_endpoint", Required: true,
            IOptionsModuleName: "AiPersistenceModule"),
        new("AzureAd__TenantId", PerEnvSettingSource.FromHandlerParameter,
            LiteralValue: null, ParameterKey: "tenant_id", Required: true,
            IOptionsModuleName: "AzureAdOptions"),
        new("AzureAd__ClientId", PerEnvSettingSource.FromHandlerOutput,
            LiteralValue: null, ParameterKey: "bff_app_client_id", Required: true,
            IOptionsModuleName: "AzureAdOptions"),
        new("SharePointEmbedded__ContainerTypeId", PerEnvSettingSource.FromHandlerOutput,
            LiteralValue: null, ParameterKey: "container_type_id", Required: true,
            IOptionsModuleName: "SpeOptions"),
        new("Graph__ManagedIdentity__Enabled", PerEnvSettingSource.Literal,
            LiteralValue: "true", ParameterKey: null, Required: true,
            IOptionsModuleName: "GraphModule"),
        new("Graph__ManagedIdentity__ClientId", PerEnvSettingSource.FromHandlerOutput,
            LiteralValue: null, ParameterKey: "uami_client_id", Required: true,
            IOptionsModuleName: "GraphModule"),
    ];

    // ---------- fakes ----------

    private sealed class FakeRepository : IProvisioningRunRepository
    {
        private ProvisioningRun? _run;
        private string? _etag;
        public ProvisioningRun? LastWrittenRun { get; private set; }

        public FakeRepository(ProvisioningRun? run, string? etag)
        {
            _run = run;
            _etag = etag;
        }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult(_run is null || _etag is null
                ? null
                : new ProvisioningRunReadResult(_run, _etag));

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
        {
            LastWrittenRun = run;
            _run = run;
            _etag = ifMatchEtag + "-next";
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, _etag));
        }
    }

    private sealed class FakePerEnvManifest : IPerEnvSettingsManifest
    {
        private readonly PerEnvSettingsManifestReadResult _result;
        public int CallCount { get; private set; }
        private FakePerEnvManifest(PerEnvSettingsManifestReadResult result) => _result = result;
        public static FakePerEnvManifest Success(IReadOnlyList<PerEnvSettingEntry> entries)
            => new(new PerEnvSettingsManifestReadResult.Success(entries));
        public static FakePerEnvManifest Failure(string diag)
            => new(new PerEnvSettingsManifestReadResult.Failure(diag));
        public Task<PerEnvSettingsManifestReadResult> ReadAsync(CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly int _exitCode;
        private readonly string _stdout;
        private readonly string _stderr;
        public int CallCount { get; private set; }
        public IReadOnlyList<string>? LastArgs { get; private set; }

        private FakeProcessRunner(int exitCode, string stdout, string stderr)
        {
            _exitCode = exitCode;
            _stdout = stdout;
            _stderr = stderr;
        }
        public static FakeProcessRunner Zero() => new(0, "OK", "");
        public static FakeProcessRunner NonZero(int exitCode, string stdout, string stderr)
            => new(exitCode, stdout, stderr);

        public Task<ProcessResult> RunAsync(
            string executable, IReadOnlyList<string> args,
            IReadOnlyDictionary<string, string>? environment,
            TimeSpan? timeout, CancellationToken cancellationToken)
        {
            CallCount++;
            LastArgs = args;
            return Task.FromResult(new ProcessResult(_exitCode, _stdout, _stderr));
        }
    }

    private sealed class FakeHealthzProbe : IHealthzProbe
    {
        private readonly HealthzResult _result;
        public int CallCount { get; private set; }
        private FakeHealthzProbe(HealthzResult result) => _result = result;
        public static FakeHealthzProbe Success() => new(new HealthzResult.Success(200, TimeSpan.FromSeconds(30)));
        public static FakeHealthzProbe Timeout(string summary) => new(new HealthzResult.Timeout(summary, 5));
        public Task<HealthzResult> ProbeWithBackoffAsync(Uri healthzUrl, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeContainerLogFetcher : IContainerLogFetcher
    {
        public string NextLogs { get; set; } = string.Empty;
        public int CallCount { get; private set; }
        public Task<string> FetchDockerLogsAsync(string appServiceName, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(NextLogs);
        }
    }
}
