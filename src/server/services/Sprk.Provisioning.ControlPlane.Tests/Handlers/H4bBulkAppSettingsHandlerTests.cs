// -----------------------------------------------------------------------------
// H4bBulkAppSettingsHandlerTests.cs
//
// Task 201 — unit tests over H4bBulkAppSettingsHandler. xunit +
// FluentAssertions + hand-rolled fakes for every seam, following the H4
// (task 047) test exemplar shape.
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live HTTP / Kudu / Azure API.
//   Fakes replace the repository + ALL FOUR collaborator seams (manifest,
//   app-settings writer, healthz probe, container log fetcher). The ARM write
//   itself is ArmAppServiceSettingsWriterTests; parity with the generated
//   Configure script is H4bConfigureScriptParityTests (task 253).
//
// COVERAGE (POML acceptance-criteria mapping):
//   AC-1  Happy path — all resolved + write Success + healthz Success →
//         HandlerResult.Success + Cosmos advanced; the write carries the KV
//         references (stamp vault) and the per-env values.
//   AC-2  Per-env-input missing → Failure(Resumable, PerEnvInputMissing)
//         BEFORE any write; diagnostic names the source-key +
//         iOptionsModule.
//   AC-3  Writer Failure / throw → Failure(Resumable, AppSettingsWriteFailed);
//         caller cancellation propagates.
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
//         the KV references are still written.
//
//   Task 205c / punch row A39 (auth-v4 §10.2 live-contract 8-entry set):
//   AC-15 All 8 §10.2 entries resolve + apply correctly in ONE HandleAsync
//         pass — literal entries need no envelope lookup, FromHandlerOutput
//         entries resolve via the shared service_bus_fqns / uami_client_id
//         sources, and the 3 ServiceBus FQNS settings all carry the one
//         resolved value.
//   AC-16 Missing service_bus_fqns (entries 4/5/6's shared source) →
//         Failure(Resumable, PerEnvInputMissing) BEFORE any write.
//   AC-17 Missing uami_client_id (entry 3's source — the POML's explicit
//         load-bearing-key-omission case) → Failure(Resumable,
//         PerEnvInputMissing) BEFORE any write; does NOT silently skip.
//   AC-18 SF-18 required=true sweep — the shipped A39 entry set (as
//         constructed by BuildA39Entries) carries Required=true on all 7 new
//         entries (metadata assertion; the real-manifest.yaml equivalent
//         lives in FilePerEnvSettingsManifestTests).
//   AC-19 FIC-flap tolerance budget guard — HttpHealthzProbe's shipped
//         DefaultBackoffSchedule total budget must stay comfortably above
//         the measured ~130s AADSTS70025 propagation-flap window (regression
//         guard for the H4b boot-retry-allowance choice documented in
//         manifest.yaml + H4bBulkAppSettingsHandler.cs).
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
    private const string CustomerId = "acmeprod";
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
    public async Task AC1_HappyPath_WritesKeyVaultReferencesAndPerEnvValues_AndAdvancesCosmos()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-1");
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, writer, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(
            H4bBulkAppSettingsHandler.BuildIdempotencyKey(EnvironmentName, SecretsVer));
        writer.CallCount.Should().Be(1);
        probe.CallCount.Should().Be(1);
        // Fetch NOT called on happy path.
        fetcher.CallCount.Should().Be(0);
        // Cosmos advanced.
        repo.LastWrittenRun!.CurrentPhase.Should().Be(HandlerIds.H4b);
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle()
            .Which.Phase.Should().Be(HandlerIds.H4b);

        // The write targets the stamp's App Service in the customer's subscription.
        var request = writer.LastRequest!;
        request.SubscriptionId.Should().Be(SubscriptionId);
        request.ResourceGroupName.Should().Be(ResourceGroupName);
        request.AppServiceName.Should().Be(AppServiceName);
        // A KV reference names the STAMP vault (H2a's KeyVaultName) and the secret's canonical name.
        request.Settings["AzureOpenAI__Endpoint"].Should()
            .Be("@Microsoft.KeyVault(VaultName=sprk-prod-kv;SecretName=AzureOpenAI-Endpoint)");
        // A per-env value is the run's resolved value.
        request.Settings["SpeAdmin__KeyVaultUri"].Should().Be("https://sprk-prod-kv.vault.azure.net/");
        request.Settings["Graph__ManagedIdentity__Enabled"].Should().Be("true");
    }

    [Fact]
    public async Task AC1a_PerEnvEntryWithTheSameKeyAsAKeyVaultReference_WinsOverTheReference()
    {
        // The generated script emitted per-env lines after the KV references and az kept the last value:
        // AzureAd__TenantId is both a TenantId secret app_setting and a per_env_settings entry.
        var writer = FakeSettingsWriter.Succeeds();
        var handler = Build(new FakeRepository(BuildRun(), "etag-1a"), FakePerEnvManifest.Success(BuildStandardEntries()),
            writer, FakeHealthzProbe.Success(), new FakeContainerLogFetcher());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        writer.LastRequest!.Settings["AzureAd__TenantId"].Should().Be(TenantId);
        writer.LastRequest.Settings["TENANT_ID"].Should().Be("@Microsoft.KeyVault(VaultName=sprk-prod-kv;SecretName=TenantId)",
            "a KV reference nobody overrides stays a reference");
    }

    // ---------- AC-1b every PerEnvSourceCatalog source reaches the write (task 245a) ----------

    [Fact]
    public async Task AC1b_EverySourceInTheCatalog_WritesItsOwnRunValue()
    {
        // Expected values are written out from BuildRun(), NOT computed with the catalog's Resolve —
        // a source wired to the wrong InterStepState property must fail here.
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kv_vault_uri"] = "https://sprk-prod-kv.vault.azure.net/",
            ["cosmos_endpoint"] = "https://sprk-prod-cosmos.documents.azure.com/",
            ["uami_client_id"] = "00000000-1111-2222-3333-555555555555",
            ["service_bus_fqns"] = "spaarke-acme-prod-sbus.servicebus.windows.net",
            ["redis_endpoint"] = "sprk-acme-prod-redis.westus2.redis.azure.net:10000",   // T242: H2a's RedisEndpoint
            ["content_safety_endpoint"] = "https://sprk-acme-prod-contentsafety.cognitiveservices.azure.com/",   // T246: H2a's ContentSafetyEndpoint
            ["bff_app_client_id"] = "00000000-aaaa-bbbb-cccc-999999999999",
            ["tenant_id"] = TenantId,
            ["container_type_id"] = "00000000-dead-beef-0000-000000000001",
            ["customer_id"] = CustomerId,   // T238: the run's own customerId, verbatim
            ["dataverse_env_url"] = "https://acme.crm.dynamics.com/",   // T245b: H5's DataverseEnvUrl
            ["spe_container_id"] = "b!h8-created-customer-container",   // T227c: H8's SpeContainerId
            ["openai_monthly_limit_usd"] = "500",   // T254: the optional OpenAI spend limit (intake)
        };
        expected.Keys.Should().BeEquivalentTo(PerEnvSourceCatalog.BySourceKey.Keys,
            "a source added to PerEnvSourceCatalog needs a row here");

        IReadOnlyList<PerEnvSettingEntry> oneEntryPerSource = PerEnvSourceCatalog.All
            .Select(s => new PerEnvSettingEntry($"Setting__For__{s.SourceKey}",
                s.ProducerHandlerId is null ? PerEnvSettingSource.FromHandlerParameter : PerEnvSettingSource.FromHandlerOutput,
                LiteralValue: null, ParameterKey: s.SourceKey, Required: true, IOptionsModuleName: "AnyModule"))
            .ToList();
        var writer = FakeSettingsWriter.Succeeds();
        var handler = Build(new FakeRepository(BuildRun(), "etag-1b"), FakePerEnvManifest.Success(oneEntryPerSource),
            writer, FakeHealthzProbe.Success(), new FakeContainerLogFetcher());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        var settings = writer.LastRequest!.Settings;
        foreach (var (sourceKey, value) in expected)
        {
            settings.Should().ContainKey($"Setting__For__{sourceKey}")
                .WhoseValue.Should().Be(value, $"source '{sourceKey}' must carry its own run value");
        }
    }

    // ---------- T254: an optional (required: false) setting the run does not carry ----------

    [Fact]
    public async Task T254_AnOptionalSettingTheRunDoesNotCarry_IsNotWritten()
    {
        // No openAiMonthlyLimitUsd at intake = no spend limit (owner G37): H4b writes nothing for it, so a value an
        // operator set on the site (scripts/Set-AiSpendLimit.ps1) survives the merge.
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(IntakeParameterCatalog.OpenAiMonthlyLimitUsd);
        var entries = new List<PerEnvSettingEntry>
        {
            new("AiSpendLimit__MonthlyLimitUsd", PerEnvSettingSource.FromHandlerParameter, LiteralValue: null,
                ParameterKey: "openai_monthly_limit_usd", Required: false, IOptionsModuleName: "AnalysisServicesModule"),
        };
        var writer = FakeSettingsWriter.Succeeds();
        var handler = Build(new FakeRepository(run, "etag-t254"), FakePerEnvManifest.Success(entries),
            writer, FakeHealthzProbe.Success(), new FakeContainerLogFetcher());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        writer.LastRequest!.Settings.Should().NotContainKey("AiSpendLimit__MonthlyLimitUsd");
    }

    [Fact]
    public async Task T254_AnOptionalSettingTheRunCarries_IsWritten()
    {
        var entries = new List<PerEnvSettingEntry>
        {
            new("AiSpendLimit__MonthlyLimitUsd", PerEnvSettingSource.FromHandlerParameter, LiteralValue: null,
                ParameterKey: "openai_monthly_limit_usd", Required: false, IOptionsModuleName: "AnalysisServicesModule"),
        };
        var writer = FakeSettingsWriter.Succeeds();
        var handler = Build(new FakeRepository(BuildRun(), "etag-t254b"), FakePerEnvManifest.Success(entries),
            writer, FakeHealthzProbe.Success(), new FakeContainerLogFetcher());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        writer.LastRequest!.Settings["AiSpendLimit__MonthlyLimitUsd"].Should().Be("500");
    }

    // ---------- AC-2 per-env-input missing ----------

    [Fact]
    public async Task AC2_PerEnvInputMissing_ResumableFailure_BeforeAnyWrite()
    {
        var run = BuildRun();
        // H2a's KeyVaultUri output is missing (kv_vault_uri source).
        run.InterStepState.KeyVaultUri = null;
        var repo = new FakeRepository(run, "etag-2");
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, writer, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BulkAppSettingsRejectionCodes.PerEnvInputMissing);
        failure.Diagnostic.Should().Contain("kv_vault_uri");
        failure.Diagnostic.Should().Contain("SpeAdmin__KeyVaultUri");
        failure.Diagnostic.Should().Contain("SpeAdminModule");
        failure.Diagnostic.Should().Contain("InterStepState.KeyVaultUri", "the diagnostic names where the value lives");
        failure.Diagnostic.Should().Contain("H2a must complete", "and which handler produces it");
        writer.CallCount.Should().Be(0);
        probe.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC2b_IntakeSourcedPerEnvInputMissing_DiagnosticPointsAtIntake()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(IntakeParameterCatalog.ContainerTypeId);
        var repo = new FakeRepository(run, "etag-2b");
        var writer = FakeSettingsWriter.Succeeds();
        var handler = Build(repo, FakePerEnvManifest.Success(BuildStandardEntries()), writer,
            FakeHealthzProbe.Success(), new FakeContainerLogFetcher());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(BulkAppSettingsRejectionCodes.PerEnvInputMissing);
        failure.Diagnostic.Should().Contain("container_type_id");
        failure.Diagnostic.Should().Contain("supply it at intake");
        writer.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC2c_PerEnvSourceNotInCatalog_FailsManifestReadFailed_BeforeAnyWrite()
    {
        // A hand-built manifest bypasses FilePerEnvSettingsManifest's load-time check; H4b must
        // still refuse a source it cannot resolve rather than treat it as "missing".
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-2c");
        var entries = new List<PerEnvSettingEntry>
        {
            new("Some__Setting", PerEnvSettingSource.FromHandlerOutput,
                LiteralValue: null, ParameterKey: "not_a_catalog_source", Required: true,
                IOptionsModuleName: "SomeModule"),
        };
        var writer = FakeSettingsWriter.Succeeds();
        var handler = Build(repo, FakePerEnvManifest.Success(entries), writer,
            FakeHealthzProbe.Success(), new FakeContainerLogFetcher());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(BulkAppSettingsRejectionCodes.ManifestReadFailed);
        failure.Diagnostic.Should().Contain("not_a_catalog_source");
        writer.CallCount.Should().Be(0);
    }

    // ---------- AC-3 the write fails ----------

    [Fact]
    public async Task AC3_WriterReportsFailure_ResumableFailure_WithWriterDiagnostic()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-3");
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var writer = FakeSettingsWriter.Fails("ARM refused the app-settings merge on the staging slot (HTTP 403, AuthorizationFailed).");
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, writer, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BulkAppSettingsRejectionCodes.AppSettingsWriteFailed);
        failure.Diagnostic.Should().Contain("staging slot").And.Contain("AuthorizationFailed");
        repo.LastWrittenRun!.CompletedPhases.Should().BeEmpty("a failed write never marks H4b complete");
        // Probe not reached.
        probe.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC3b_WriterThrows_ResumableFailure_NotAnUnhandledException()
    {
        var writer = FakeSettingsWriter.Throws(new InvalidOperationException("credential unavailable"));
        var probe = FakeHealthzProbe.Success();
        var handler = Build(new FakeRepository(BuildRun(), "etag-3b"), FakePerEnvManifest.Success(BuildStandardEntries()),
            writer, probe, new FakeContainerLogFetcher());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BulkAppSettingsRejectionCodes.AppSettingsWriteFailed);
        failure.Diagnostic.Should().Contain("credential unavailable").And.Contain(AppServiceName);
        probe.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC3c_CallerCancelsDuringWrite_CancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        var writer = FakeSettingsWriter.CancelsCaller(cts);
        var handler = Build(new FakeRepository(BuildRun(), "etag-3c"), FakePerEnvManifest.Success(BuildStandardEntries()),
            writer, FakeHealthzProbe.Success(), new FakeContainerLogFetcher());

        var act = async () => await handler.HandleAsync(BuildEnvelope(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("a Worker shutdown is not a run failure");
    }

    // ---------- AC-4 healthz timeout with parseable module ----------

    [Fact]
    public async Task AC4_HealthzTimeoutWithParseableModule_QuarantineRequired_WithModuleDiagnostic()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-4");
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Timeout("HTTP 502 (elapsed 480s across 5 attempts)");
        var fetcher = new FakeContainerLogFetcher
        {
            NextLogs = "2026-08-24T14:00:00.123Z INFO Booting BFF...\n" +
                       "Unhandled exception. System.InvalidOperationException: SpeAdmin:KeyVaultUri (or KeyVaultUri) configuration is required for SpeAdminModule.\n" +
                       "   at Sprk.Bff.Api.Infrastructure.DI.SpeAdminModule.AddSpeAdminModule(IServiceCollection services)\n",
        };
        var handler = Build(repo, manifest, writer, probe, fetcher);

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
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Timeout("no response (elapsed 480s)");
        var fetcher = new FakeContainerLogFetcher { NextLogs = "starting up... waiting for db..." };
        var handler = Build(repo, manifest, writer, probe, fetcher);

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
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, writer, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(expectedKey);
        // Idempotent no-op does NOT invoke process / probe. The manifest is read once — its content
        // version is the key's secretsVer (task 245b).
        manifest.CallCount.Should().Be(1);
        writer.CallCount.Should().Be(0);
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
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, writer, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        // The literal is written verbatim; the fake manifest's KV references come along.
        var settings = writer.LastRequest!.Settings;
        settings["Graph__ManagedIdentity__Enabled"].Should().Be("true");
        settings.Keys.Except(StandardKeyVaultReferences.Select(r => r.AppSettingKey))
            .Should().Equal("Graph__ManagedIdentity__Enabled");
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
    public async Task AC9_MissingRequiredParameter_FailsResumable_NoExternalCalls(
        string parameterKey, string expectedRejectionCode)
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(parameterKey);
        var repo = new FakeRepository(run, "etag-guard");
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, writer, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(expectedRejectionCode);
        writer.CallCount.Should().Be(0);
        probe.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(nameof(InterStepState.KeyVaultName), BulkAppSettingsRejectionCodes.MissingKeyVaultName)]
    [InlineData(nameof(InterStepState.ResourceGroupName), BulkAppSettingsRejectionCodes.MissingResourceGroupName)]
    [InlineData(nameof(InterStepState.AppServiceName), BulkAppSettingsRejectionCodes.MissingAppServiceName)]
    public async Task AC9b_MissingH2aOutput_FailsResumable_NoExternalCalls(
        string interStepStateProperty, string expectedRejectionCode)
    {
        // Task 245a: these are H2a outputs in InterStepState — never run parameters.
        var run = BuildRun();
        switch (interStepStateProperty)
        {
            case nameof(InterStepState.KeyVaultName): run.InterStepState.KeyVaultName = null; break;
            case nameof(InterStepState.ResourceGroupName): run.InterStepState.ResourceGroupName = null; break;
            case nameof(InterStepState.AppServiceName): run.InterStepState.AppServiceName = null; break;
        }
        var repo = new FakeRepository(run, "etag-guard-iss");
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Success();
        var handler = Build(repo, FakePerEnvManifest.Success(BuildStandardEntries()), writer, probe,
            new FakeContainerLogFetcher());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(expectedRejectionCode);
        failure.Diagnostic.Should().Contain($"InterStepState.{interStepStateProperty}");
        writer.CallCount.Should().Be(0);
        probe.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC9c_EnvironmentNameAbsent_ResolvesToProd_SameAsH2aAndH2b()
    {
        // A run created before CreateRun stored environmentName resolves to the shared default
        // instead of failing (H4b used to fail where H2a/H2b silently defaulted).
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(IntakeParameterCatalog.EnvironmentName);
        var repo = new FakeRepository(run, "etag-env");
        var handler = Build(repo, FakePerEnvManifest.Success(BuildStandardEntries()), FakeSettingsWriter.Succeeds(),
            FakeHealthzProbe.Success(), new FakeContainerLogFetcher());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>().Which.IdempotencyKey
            .Should().Be(H4bBulkAppSettingsHandler.BuildIdempotencyKey(IntakeParameterCatalog.DefaultEnvironmentName, SecretsVer));
    }

    // ---------- AC-10 handler-id mismatch ----------

    [Fact]
    public async Task AC10_HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-10");
        var manifest = FakePerEnvManifest.Success(BuildStandardEntries());
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, writer, probe, fetcher);

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
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, writer, probe, fetcher);

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
        run.InterStepState.CosmosEndpoint = null;
        var repo = new FakeRepository(run, "etag-13");
        var entries = new List<PerEnvSettingEntry>
        {
            // Optional entry — missing source → skip; no fail. (Required-ness is per manifest
            // entry; the source itself must still be a catalog source.)
            new("Some__OptionalSetting", PerEnvSettingSource.FromHandlerOutput,
                LiteralValue: null, ParameterKey: "cosmos_endpoint", Required: false,
                IOptionsModuleName: "SomeOptionalModule"),
        };
        var manifest = FakePerEnvManifest.Success(entries);
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, writer, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        writer.CallCount.Should().Be(1);
        writer.LastRequest!.Settings.Should().NotContainKey("Some__OptionalSetting");
    }

    // ---------- AC-14 empty per_env_settings manifest ----------

    [Fact]
    public async Task AC14_EmptyPerEnvSettingsManifest_KeyVaultReferencesStillWritten_HappyPath()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-14");
        var manifest = FakePerEnvManifest.Success(Array.Empty<PerEnvSettingEntry>());
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, writer, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        writer.CallCount.Should().Be(1, "the KV references are written even when per_env_settings is empty");
        writer.LastRequest!.Settings.Keys.Should().BeEquivalentTo(StandardKeyVaultReferences.Select(r => r.AppSettingKey));
    }

    // ---------- AC-15 A39 8-entry happy path ----------

    [Fact]
    public async Task AC15_A39EightEntries_ResolveSharedServiceBusFqns_HappyPath()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-15");
        var entries = BuildStandardEntries().Concat(BuildA39Entries()).ToList();
        var manifest = FakePerEnvManifest.Success(entries);
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, writer, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        var settings = writer.LastRequest!.Settings;
        // Literal entries (Order__0, RequireSecretFreeIdentity, AiSearch/AiSafety MI flags) are written verbatim.
        settings["Graph__Credentials__Order__0"].Should().Be("ManagedIdentityFederated");
        settings["Graph__Credentials__RequireSecretFreeIdentity"].Should().Be("true");
        settings["AiSearch__ManagedIdentity__Enabled"].Should().Be("true");
        settings["AiSafety__ContentSafety__ManagedIdentity__Enabled"].Should().Be("true");
        // The 3 ServiceBus FQNS settings share ONE source key → all three carry its one value.
        foreach (var key in new[]
                 {
                     "ServiceBus__FullyQualifiedNamespace",
                     "Membership__EventPublisher__ServiceBusNamespace",
                     "Membership__JunctionUpdater__ServiceBusNamespace",
                 })
        {
            settings[key].Should().Be("spaarke-acme-prod-sbus.servicebus.windows.net");
        }
    }

    // ---------- AC-16 missing service_bus_fqns (entries 4/5/6 shared source) ----------

    [Fact]
    public async Task AC16_MissingServiceBusFqns_ResumableFailure_BeforeAnyWrite()
    {
        var run = BuildRun();
        run.InterStepState.ServiceBusFullyQualifiedNamespace = null;
        var repo = new FakeRepository(run, "etag-16");
        var manifest = FakePerEnvManifest.Success(BuildA39Entries());
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, writer, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BulkAppSettingsRejectionCodes.PerEnvInputMissing);
        failure.Diagnostic.Should().Contain("service_bus_fqns");
        writer.CallCount.Should().Be(0);
        probe.CallCount.Should().Be(0);
    }

    // ---------- AC-17 missing uami_client_id (entry 3 — POML's explicit load-bearing case) ----------

    [Fact]
    public async Task AC17_MissingUamiClientId_ResumableFailure_DoesNotSilentlySkip()
    {
        var run = BuildRun();
        run.InterStepState.MiClientId = null;
        var repo = new FakeRepository(run, "etag-17");
        var entry3Only = new List<PerEnvSettingEntry>
        {
            new("ManagedIdentity__ClientId", PerEnvSettingSource.FromHandlerOutput,
                LiteralValue: null, ParameterKey: "uami_client_id", Required: true,
                IOptionsModuleName: "GraphModule"),
        };
        var manifest = FakePerEnvManifest.Success(entry3Only);
        var writer = FakeSettingsWriter.Succeeds();
        var probe = FakeHealthzProbe.Success();
        var fetcher = new FakeContainerLogFetcher();
        var handler = Build(repo, manifest, writer, probe, fetcher);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BulkAppSettingsRejectionCodes.PerEnvInputMissing);
        failure.Diagnostic.Should().Contain("uami_client_id");
        failure.Diagnostic.Should().Contain("ManagedIdentity__ClientId");
        // MUST fail hard, not silently skip — no write reached.
        writer.CallCount.Should().Be(0);
    }

    // ---------- AC-18 SF-18 required=true sweep over the A39 entry set ----------

    [Fact]
    public void AC18_A39Entries_AllCarryRequiredTrue_Sf18SilentSkipTrapAvoided()
    {
        var entries = BuildA39Entries();
        entries.Should().HaveCount(7);
        entries.Should().OnlyContain(e => e.Required,
            "H4b:286 silently skips missing optional entries -- every auth-v4 " +
            "§10.2 entry is load-bearing and MUST be required=true");
    }

    // ---------- AC-19 FIC-flap tolerance budget guard ----------

    [Fact]
    public void AC19_HealthzBackoffBudget_ExceedsMeasuredFicFlapWindowWithMargin()
    {
        // Auth-v4 §11 invariant 2 measured the FIC propagation flap at ~130s
        // (~8 failures, AADSTS70025). H4b's own /healthz backoff-poll is the
        // CHOSEN tolerance mechanism (see manifest.yaml + H4bBulkAppSettingsHandler.cs
        // comments) -- this guards against a future shrink silently reopening
        // the boot-loop risk on a fresh stamp.
        var totalBudgetSeconds = HttpHealthzProbe.DefaultBackoffSchedule
            .Aggregate(TimeSpan.Zero, (sum, delay) => sum + delay)
            .TotalSeconds;

        totalBudgetSeconds.Should().BeGreaterThanOrEqualTo(300,
            "the healthz backoff budget must comfortably exceed the measured " +
            "~130s FIC-propagation-flap window (>2x margin) for the boot-retry " +
            "allowance choice to hold");
    }

    // ---------- task 253: parity with the generated Configure-AppServiceSettings script ----------
    //
    // H4b used to run scripts/canonical-secret-catalog/generated/Configure-AppServiceSettings.generated.ps1. It now
    // builds the settings itself. These tests run H4b over the REAL embedded manifest and compare what it writes with
    // what that generated script would have written for the same run: the script's $settings array (and its
    // `required: false` lines) evaluated with the run's values, last value winning — `az webapp config appsettings
    // set` semantics. A manifest change the generator was not re-run for, or a C# rule that drifts from the
    // generator's, fails here.

    /// <summary>The generated script's per-source parameters (PascalCase of the source key) → BuildRun()'s values.</summary>
    private static readonly IReadOnlyDictionary<string, string> ScriptArguments = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["KvVaultUri"] = "https://sprk-prod-kv.vault.azure.net/",
        ["CosmosEndpoint"] = "https://sprk-prod-cosmos.documents.azure.com/",
        ["UamiClientId"] = "00000000-1111-2222-3333-555555555555",
        ["ServiceBusFqns"] = "spaarke-acme-prod-sbus.servicebus.windows.net",
        ["RedisEndpoint"] = "sprk-acme-prod-redis.westus2.redis.azure.net:10000",
        ["ContentSafetyEndpoint"] = "https://sprk-acme-prod-contentsafety.cognitiveservices.azure.com/",
        ["BffAppClientId"] = "00000000-aaaa-bbbb-cccc-999999999999",
        ["TenantId"] = TenantId,
        ["ContainerTypeId"] = "00000000-dead-beef-0000-000000000001",
        ["CustomerId"] = CustomerId,
        ["DataverseEnvUrl"] = "https://acme.crm.dynamics.com/",
        ["SpeContainerId"] = "b!h8-created-customer-container",
        ["OpenaiMonthlyLimitUsd"] = "500",
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Parity_WritesExactlyTheSettingsTheGeneratedScriptWrote(bool runCarriesSpendLimit)
    {
        var run = BuildRun();
        var arguments = new Dictionary<string, string>(ScriptArguments, StringComparer.Ordinal);
        if (!runCarriesSpendLimit)
        {
            run.Parameters.NonSecret.Remove(IntakeParameterCatalog.OpenAiMonthlyLimitUsd);
            arguments["OpenaiMonthlyLimitUsd"] = string.Empty;   // the script's optional parameter, defaulted ''
        }
        var writer = FakeSettingsWriter.Succeeds();
        var handler = Build(new FakeRepository(run, "etag-parity"),
            new FilePerEnvSettingsManifest(NullLogger<FilePerEnvSettingsManifest>.Instance),
            writer, FakeHealthzProbe.Success(), new FakeContainerLogFetcher());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        var scriptSettings = EvaluateGeneratedConfigureScript(KeyVaultName, arguments);
        writer.LastRequest!.Settings.Should().BeEquivalentTo(scriptSettings,
            "H4b must write exactly what Configure-AppServiceSettings.generated.ps1 wrote — same names, same values, " +
            "same Key Vault references (re-run Invoke-CatalogGenerator.ps1 if the manifest changed)");

        // The cases the parity rests on, stated explicitly.
        scriptSettings.Count.Should().BeGreaterThan(40, "the evaluator must have read the whole $settings array");
        scriptSettings["AzureAd__TenantId"].Should().Be(TenantId, "the per-env value wins over TenantId's KV reference");
        scriptSettings["AzureOpenAI__Endpoint"].Should().Be("@Microsoft.KeyVault(VaultName=sprk-prod-kv;SecretName=AzureOpenAI-Endpoint)");
        scriptSettings.ContainsKey("AiSpendLimit__MonthlyLimitUsd").Should().Be(runCarriesSpendLimit,
            "T254: the optional spend limit is written only when the run carries it");
    }

    /// <summary>
    /// Evaluates the generated script's settings the way PowerShell + az did: every line of the <c>$settings = @( … )</c>
    /// array, then each conditional <c>$settings += …</c> line whose parameter is not blank; <c>$(Format-KvRef 'S')</c>
    /// becomes the Key Vault reference, <c>$Var</c> the argument; a later duplicate key wins.
    /// </summary>
    private static IReadOnlyDictionary<string, string> EvaluateGeneratedConfigureScript(
        string vaultName, IReadOnlyDictionary<string, string> arguments)
    {
        var path = LocateRepoFile(Path.Combine(
            "scripts", "canonical-secret-catalog", "generated", "Configure-AppServiceSettings.generated.ps1"));
        var lines = File.ReadAllLines(path).Select(l => l.TrimEnd('\r')).ToList();

        var arrayLine = new System.Text.RegularExpressions.Regex("^\\s*\"(?<key>[^=\"]+)=(?<value>[^\"]*)\",?$");
        var optionalLine = new System.Text.RegularExpressions.Regex(
            "^if \\(-not \\[string\\]::IsNullOrWhiteSpace\\(\\$(?<var>\\w+)\\)\\) \\{ \\$settings \\+= \"(?<key>[^=\"]+)=(?<value>[^\"]*)\" \\}$");
        var kvRef = new System.Text.RegularExpressions.Regex("\\$\\(Format-KvRef '(?<secret>[^']+)'\\)");
        var variable = new System.Text.RegularExpressions.Regex("\\$(?<var>[A-Za-z]\\w*)");

        string Expand(string value)
        {
            var withRefs = kvRef.Replace(value, m => $"@Microsoft.KeyVault(VaultName={vaultName};SecretName={m.Groups["secret"].Value})");
            return variable.Replace(withRefs, m => arguments.TryGetValue(m.Groups["var"].Value, out var argument)
                ? argument
                : throw new InvalidOperationException(
                    $"The generated script uses ${m.Groups["var"].Value}, which this test has no value for — add it to ScriptArguments."));
        }

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        var start = lines.FindIndex(l => l.Trim() == "$settings = @(");
        start.Should().BeGreaterThanOrEqualTo(0, "the generated script declares its $settings array");
        var i = start + 1;
        for (; i < lines.Count && lines[i].Trim() != ")"; i++)
        {
            var match = arrayLine.Match(lines[i]);
            match.Success.Should().BeTrue($"every $settings line is \"key=value\" (line {i + 1}: {lines[i]})");
            settings[match.Groups["key"].Value] = Expand(match.Groups["value"].Value);
        }
        for (; i < lines.Count; i++)
        {
            var match = optionalLine.Match(lines[i]);
            if (match.Success && !string.IsNullOrWhiteSpace(arguments[match.Groups["var"].Value]))
            {
                settings[match.Groups["key"].Value] = Expand(match.Groups["value"].Value);
            }
        }
        return settings;
    }

    private static string LocateRepoFile(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException($"Could not locate {relativePath} by walking up from {AppContext.BaseDirectory}.");
    }

    // ---------- helpers ----------

    private static H4bBulkAppSettingsHandler Build(
        IProvisioningRunRepository repo,
        IPerEnvSettingsManifest manifest,
        IAppServiceSettingsWriter writer,
        IHealthzProbe probe,
        IContainerLogFetcher fetcher)
    {
        return new H4bBulkAppSettingsHandler(
            repo, manifest, writer, probe, fetcher,
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
            TenancyModel = "Model1",
            Status = RunStatus.Running,
            Profile = "spaarke-hosted-model2",
        };
        // Intake values (IntakeParameterCatalog) — the only things in Parameters.NonSecret.
        var p = run.Parameters.NonSecret;
        p[H4bBulkAppSettingsHandler.TenantIdParameterKey] = TenantId;
        p[H4bBulkAppSettingsHandler.SubscriptionIdParameterKey] = SubscriptionId;
        p[IntakeParameterCatalog.EnvironmentName] = EnvironmentName;
        p[IntakeParameterCatalog.ContainerTypeId] = "00000000-dead-beef-0000-000000000001";
        p[IntakeParameterCatalog.OpenAiMonthlyLimitUsd] = "500";   // T254 (optional)
        // Upstream handler outputs (task 245a) — H2a's and H3's typed InterStepState.
        var s = run.InterStepState;
        s.KeyVaultName = KeyVaultName;
        s.ResourceGroupName = ResourceGroupName;
        s.AppServiceName = AppServiceName;
        s.KeyVaultUri = "https://sprk-prod-kv.vault.azure.net/";
        s.CosmosEndpoint = "https://sprk-prod-cosmos.documents.azure.com/";
        s.MiClientId = "00000000-1111-2222-3333-555555555555";
        s.ServiceBusFullyQualifiedNamespace = "spaarke-acme-prod-sbus.servicebus.windows.net";
        s.ContentSafetyEndpoint = "https://sprk-acme-prod-contentsafety.cognitiveservices.azure.com/";   // H2a output (task 246 — AiSafety__ContentSafety__Endpoint)
        s.RedisEndpoint = "sprk-acme-prod-redis.westus2.redis.azure.net:10000";   // H2a output (task 242 — Redis__Endpoint)
        s.BffAppRegId = "00000000-aaaa-bbbb-cccc-999999999999";
        s.DataverseEnvUrl = "https://acme.crm.dynamics.com/";   // H5 output (task 245b — Dataverse__ServiceUrl)
        s.SpeContainerId = "b!h8-created-customer-container";   // H8 output (task 227c — EmailProcessing__DefaultContainerId)
        return run;
    }

    /// <summary>
    /// The 7 NEW auth-v4 §10.2 live-contract entries added by task 205c / punch
    /// row A39 (entry 3, ManagedIdentity__ClientId, already existed pre-A39 and
    /// is covered by BuildStandardEntries' Graph__ManagedIdentity__ClientId
    /// sibling — see manifest.yaml for the shipped shape both share). Mirrors
    /// the shipped manifest.yaml per_env_settings A39 section verbatim.
    /// </summary>
    private static IReadOnlyList<PerEnvSettingEntry> BuildA39Entries() =>
    [
        new("Graph__Credentials__Order__0", PerEnvSettingSource.Literal,
            LiteralValue: "ManagedIdentityFederated", ParameterKey: null, Required: true,
            IOptionsModuleName: "CredentialSelectionOptions"),
        new("Graph__Credentials__RequireSecretFreeIdentity", PerEnvSettingSource.Literal,
            LiteralValue: "true", ParameterKey: null, Required: true,
            IOptionsModuleName: "CredentialSelectionOptions"),
        new("ServiceBus__FullyQualifiedNamespace", PerEnvSettingSource.FromHandlerOutput,
            LiteralValue: null, ParameterKey: "service_bus_fqns", Required: true,
            IOptionsModuleName: "ServiceBusOptions"),
        new("Membership__EventPublisher__ServiceBusNamespace", PerEnvSettingSource.FromHandlerOutput,
            LiteralValue: null, ParameterKey: "service_bus_fqns", Required: true,
            IOptionsModuleName: "ServiceBusOptions"),
        new("Membership__JunctionUpdater__ServiceBusNamespace", PerEnvSettingSource.FromHandlerOutput,
            LiteralValue: null, ParameterKey: "service_bus_fqns", Required: true,
            IOptionsModuleName: "ServiceBusOptions"),
        new("AiSearch__ManagedIdentity__Enabled", PerEnvSettingSource.Literal,
            LiteralValue: "true", ParameterKey: null, Required: true,
            IOptionsModuleName: "AiSearchOptions"),
        new("AiSafety__ContentSafety__ManagedIdentity__Enabled", PerEnvSettingSource.Literal,
            LiteralValue: "true", ParameterKey: null, Required: true,
            IOptionsModuleName: "AiSafetyOptions"),
    ];

    /// <summary>
    /// Matches the shape of the shipped manifest.yaml per_env_settings entries
    /// (task 201). Kept in a helper so tests share ONE canonical entry list.
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
        new("SharePointEmbedded__ContainerTypeId", PerEnvSettingSource.FromHandlerParameter,
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
        public static FakePerEnvManifest Success(IReadOnlyList<PerEnvSettingEntry> entries, string contentVersion = SecretsVer)
            => new(new PerEnvSettingsManifestReadResult.Success(entries, contentVersion, StandardKeyVaultReferences));
        public static FakePerEnvManifest Failure(string diag)
            => new(new PerEnvSettingsManifestReadResult.Failure(diag));
        public Task<PerEnvSettingsManifestReadResult> ReadAsync(CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    /// <summary>
    /// A few of the shipped manifest's KV references (secrets[].app_settings) — including TenantId's
    /// AzureAd__TenantId, which a per_env_settings entry overrides.
    /// </summary>
    private static readonly IReadOnlyList<KeyVaultReferenceSetting> StandardKeyVaultReferences =
    [
        new("AzureOpenAI__Endpoint", "AzureOpenAI-Endpoint"),
        new("AzureAd__TenantId", "TenantId"),
        new("TENANT_ID", "TenantId"),
    ];

    private sealed class FakeSettingsWriter : IAppServiceSettingsWriter
    {
        private readonly Func<AppServiceSettingsWriteRequest, CancellationToken, Task<AppServiceSettingsWriteResult>> _respond;
        public int CallCount { get; private set; }
        public AppServiceSettingsWriteRequest? LastRequest { get; private set; }

        private FakeSettingsWriter(Func<AppServiceSettingsWriteRequest, CancellationToken, Task<AppServiceSettingsWriteResult>> respond)
            => _respond = respond;

        public static FakeSettingsWriter Succeeds() => new((_, _) => Task.FromResult<AppServiceSettingsWriteResult>(
            new AppServiceSettingsWriteResult.Success(new[] { "production", "staging" })));

        public static FakeSettingsWriter Fails(string diagnostic) => new((_, _) =>
            Task.FromResult<AppServiceSettingsWriteResult>(new AppServiceSettingsWriteResult.Failure(diagnostic)));

        public static FakeSettingsWriter Throws(Exception exception) => new((_, _) => throw exception);

        public static FakeSettingsWriter CancelsCaller(CancellationTokenSource caller) => new((_, ct) =>
        {
            caller.Cancel();
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("the caller's token was not passed to the writer");
        });

        public Task<AppServiceSettingsWriteResult> MergeAsync(AppServiceSettingsWriteRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return _respond(request, cancellationToken);
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
