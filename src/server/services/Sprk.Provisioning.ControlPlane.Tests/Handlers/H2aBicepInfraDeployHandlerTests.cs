// -----------------------------------------------------------------------------
// H2aBicepInfraDeployHandlerTests.cs
//
// Unit tests over H2aBicepInfraDeployHandler (task 044 — wave C4).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live Bicep / az CLI / ARM / pwsh /
//   Azure API. Fakes replace the repository + all four collaborator seams
//   (runner, ARM probe, drift detector, template inspector) so the handler
//   orchestration logic is exercised in isolation. Live-Azure coverage
//   belongs in env-guarded smoke tests (H2a is not exercised end-to-end at
//   CI time by design — a real Bicep deploy is 10–20 min).
//
// COVERAGE:
//   T1  Happy path — Model 2 dedicated: 6-collaborator green + T1 probe
//       Match → Success + Cosmos state advances + interStepState populated.
//   T2  Happy path — Model 1 shared: TenancyModel="Model1Shared" flows to
//       runner request; otherwise identical to T1.
//   T3  Idempotent no-op: run already has H2a CompletedPhase with matching
//       key → Success (no runner call, no state mutation).
//   T4  Missing tenantId (§4D I1): Failure(Resumable, missing-tenant-id) +
//       no runner call + Cosmos marked Failed.
//   T5  Missing subscriptionId: Failure(Resumable, missing-subscription-id).
//   T6  Missing bicepVer: Failure(Resumable, missing-bicep-version).
//   T7  Redis presence in template: Failure(QuarantineRequired,
//       redis-provisioning-forbidden) + Cosmos marked Quarantined.
//   T8  Unpinned model deployment: Failure(QuarantineRequired,
//       model-version-not-pinned).
//   T9  Upgrade-mode drift detected: Failure(QuarantineRequired,
//       upgrade-mode-drift) + drift report written to disk.
//   T10 Upgrade-mode NO-drift: runner + T1 probe execute; Success.
//   T11 T1 trap post-condition mismatch: runner succeeds but probe returns
//       Mismatch → Failure(QuarantineRequired,
//       trap-T1-keyvault-reference-identity-mismatch) + Cosmos marked
//       Quarantined + observed slot identities cited in diagnostic.
//   T12 Runner returns Failure: Failure(QuarantineRequired,
//       bicep-deploy-failed) + Cosmos marked Quarantined.
//   T13 Runner returns Success with incomplete outputs: Failure(QuarantineRequired,
//       bicep-deploy-outputs-incomplete).
//   T14 SignalR flag OFF: runner request carries SignalREnabled=false.
//   T15 SignalR flag ON: runner request carries SignalREnabled=true.
//   T16 HandlerId mismatch: throws InvalidOperationException.
//   T17 Idempotency key format determinism: same customerId + bicepVer
//       produce same key.
//   T18 Run not found: Failure(Resumable, run-not-found).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H2aBicepInfraDeployHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h2a-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "sub-cus-acme-prod";
    private const string BicepVer = "abc123def456";
    private const string ExpectedUamiRid = "/subscriptions/x/resourceGroups/rg-spaarke-acme-prod/providers/Microsoft.ManagedIdentity/userAssignedIdentities/sprk-acme-prod-uami";

    // ---------- T1 happy path — Model 2 dedicated ----------

    [Fact]
    public async Task HappyPath_Model2Dedicated_AllCollaboratorsGreen_SucceedsAndAdvancesState()
    {
        var run = BuildRun(tenancyModel: "Model2Dedicated");
        var repo = new FakeRepository(run, etag: "etag-1");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs(signalRDeployed: false));
        var probe = FakeArmKeyVaultRefProbe.Match();
        var driftDetector = new FakeUpgradeDriftDetector();
        var inspector = FakeBicepTemplateInspector.Clean();
        var handler = BuildHandler(repo, runner, probe, driftDetector, inspector);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H2aBicepInfraDeployHandler.BuildIdempotencyKey(CustomerId, BicepVer));

        // Cosmos state advanced with interStepState populated.
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H2a");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle()
            .Which.Phase.Should().Be("H2a");
        repo.LastWrittenRun.InterStepState.OpenAiEndpoint.Should().Be("https://sprk-acme-openai.openai.azure.com/");
        repo.LastWrittenRun.InterStepState.AiSearchEndpoint.Should().Be("https://sprk-acme-search.search.windows.net/");
        repo.LastWrittenRun.InterStepState.CosmosEndpoint.Should().Be("https://sprk-acme-cosmos.documents.azure.com:443/");
        repo.LastWrittenRun.InterStepState.MiObjectId.Should().Be("uami-oid");
        repo.LastWrittenRun.InterStepState.MiClientId.Should().Be("uami-cid");

        // Each collaborator called exactly once.
        runner.CallCount.Should().Be(1);
        probe.CallCount.Should().Be(1);
        driftDetector.CallCount.Should().Be(0, "upgrade mode did not fire — no provisionedOn param");
        inspector.CallCount.Should().Be(1);
        runner.LastRequest.Should().NotBeNull();
        runner.LastRequest!.TenancyModel.Should().Be("Model2Dedicated");
    }

    // ---------- T2 happy path — Model 1 shared ----------

    [Fact]
    public async Task HappyPath_Model1Shared_TenancyModelFlowsToRunner()
    {
        var run = BuildRun(tenancyModel: "Model1Shared");
        var repo = new FakeRepository(run, etag: "etag-2");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs(signalRDeployed: false));
        var probe = FakeArmKeyVaultRefProbe.Match();
        var driftDetector = new FakeUpgradeDriftDetector();
        var inspector = FakeBicepTemplateInspector.Clean();
        var handler = BuildHandler(repo, runner, probe, driftDetector, inspector);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        runner.LastRequest!.TenancyModel.Should().Be("Model1Shared");
    }

    // ---------- T3 idempotency ----------

    [Fact]
    public async Task Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRun();
        var expectedKey = H2aBicepInfraDeployHandler.BuildIdempotencyKey(CustomerId, BicepVer);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H2a",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-3");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var probe = FakeArmKeyVaultRefProbe.Match();
        var driftDetector = new FakeUpgradeDriftDetector();
        var inspector = FakeBicepTemplateInspector.Clean();
        var handler = BuildHandler(repo, runner, probe, driftDetector, inspector);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        runner.CallCount.Should().Be(0);
        probe.CallCount.Should().Be(0);
        inspector.CallCount.Should().Be(0);
    }

    // ---------- T4 missing tenantId (§4D I1) ----------

    [Fact]
    public async Task MissingTenantId_FailsResumable_NoRunnerCall()
    {
        var run = BuildRun(includeTenantId: false);
        var repo = new FakeRepository(run, etag: "etag-4");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), FakeBicepTemplateInspector.Clean());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.MissingTenantId);
        failure.Diagnostic.Should().Contain("§4D I1");
        runner.CallCount.Should().Be(0);
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- T5 missing subscriptionId ----------

    [Fact]
    public async Task MissingSubscriptionId_FailsResumable()
    {
        var run = BuildRun(includeSubscriptionId: false);
        var repo = new FakeRepository(run, etag: "etag-5");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), FakeBicepTemplateInspector.Clean());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.MissingSubscriptionId);
        runner.CallCount.Should().Be(0);
    }

    // ---------- T6 missing bicepVer ----------

    [Fact]
    public async Task MissingBicepVersion_FailsResumable()
    {
        var run = BuildRun(includeBicepVer: false);
        var repo = new FakeRepository(run, etag: "etag-6");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), FakeBicepTemplateInspector.Clean());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.MissingBicepVersion);
        runner.CallCount.Should().Be(0);
    }

    // ---------- T7 Redis presence ----------

    [Fact]
    public async Task RedisInTemplate_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-7");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var inspector = FakeBicepTemplateInspector.WithRedis("customer.bicep:100");
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), inspector);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.RedisProvisioningForbidden);
        failure.Diagnostic.Should().Contain("Q-E FR-12");
        runner.CallCount.Should().Be(0, "template inspector fires BEFORE runner");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.Quarantine.Should().NotBeNull();
    }

    // ---------- T8 unpinned model deployment ----------

    [Fact]
    public async Task UnpinnedModelDeployment_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-8");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var inspector = FakeBicepTemplateInspector.WithUnpinnedModel("gpt-4o", "latest");
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), inspector);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.ModelVersionNotPinned);
        failure.Diagnostic.Should().Contain("ADR-020");
        runner.CallCount.Should().Be(0);
    }

    // ---------- T9 upgrade-mode drift REJECT ----------

    [Fact]
    public async Task UpgradeMode_DriftDetected_FailsQuarantineRequired_AndWritesReport()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "h2a-drift-" + Guid.NewGuid().ToString("N"));
        try
        {
            var run = BuildRun(includeProvisionedOn: true);
            var repo = new FakeRepository(run, etag: "etag-9");
            var runner = FakeBicepDeployRunner.Success(BuildOutputs());
            var driftDetector = FakeUpgradeDriftDetector.WithDrift("{'changes':[{'changeType':'Modify'}]}");
            var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(), driftDetector,
                FakeBicepTemplateInspector.Clean(), runNotesDir: tempDir);

            var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

            var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
            failure.Class.Should().Be(FailureClass.QuarantineRequired);
            failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.UpgradeModeDrift);
            failure.Diagnostic.Should().Contain("REJECT per §14A.5");
            failure.Diagnostic.Should().Contain(tempDir);
            runner.CallCount.Should().Be(0, "runner skipped on drift REJECT");

            // A drift report file exists.
            var files = Directory.GetFiles(tempDir, "drift-acme-*.md");
            files.Should().ContainSingle();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    // ---------- T10 upgrade-mode NO drift ----------

    [Fact]
    public async Task UpgradeMode_NoDrift_ProceedsToRunner()
    {
        var run = BuildRun(includeProvisionedOn: true);
        var repo = new FakeRepository(run, etag: "etag-10");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var driftDetector = new FakeUpgradeDriftDetector();
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            driftDetector, FakeBicepTemplateInspector.Clean());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        driftDetector.CallCount.Should().Be(1, "upgrade mode fires drift detector");
        runner.CallCount.Should().Be(1);
    }

    // ---------- T11 T1 trap post-condition mismatch ----------

    [Fact]
    public async Task T1TrapMismatch_FailsQuarantineRequired_CitesObservedValues()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-11");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var probe = FakeArmKeyVaultRefProbe.Mismatch(
            observedProd: "SystemAssigned",
            observedStaging: null);
        var handler = BuildHandler(repo, runner, probe, new FakeUpgradeDriftDetector(),
            FakeBicepTemplateInspector.Clean());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.TrapT1KeyVaultReferenceIdentityMismatch);
        failure.Diagnostic.Should().Contain(ExpectedUamiRid);
        failure.Diagnostic.Should().Contain("SystemAssigned");
        failure.Diagnostic.Should().Contain("(null)");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- T12 runner returns failure ----------

    [Fact]
    public async Task RunnerReturnsFailure_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-12");
        var runner = FakeBicepDeployRunner.Failure("az deployment sub create exit 1: quota");
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), FakeBicepTemplateInspector.Clean());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.BicepDeployFailed);
        failure.Diagnostic.Should().Contain("az deployment sub create exit 1");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- T13 runner returns success with incomplete outputs ----------

    [Fact]
    public async Task RunnerReturnsIncompleteOutputs_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-13");
        // Missing openAiEndpoint — one required field blank.
        var incomplete = new BicepDeployOutputs
        {
            ResourceGroupName = "rg-x",
            UserAssignedIdentityResourceId = ExpectedUamiRid,
            UserAssignedIdentityObjectId = "oid",
            UserAssignedIdentityClientId = "cid",
            AppServiceName = "app",
            AppServiceStagingSlotName = "staging",
            OpenAiEndpoint = "   ",
            AiSearchEndpoint = "https://x/",
            CosmosEndpoint = "https://x/",
            SignalRDeployed = false,
        };
        var runner = FakeBicepDeployRunner.Success(incomplete);
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), FakeBicepTemplateInspector.Clean());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.BicepDeployOutputsIncomplete);
    }

    // ---------- T14/T15 SignalR feature flag ----------

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public async Task SignalREnabledFlag_FlowsToRunnerRequest(string flagValue, bool expected)
    {
        var run = BuildRun();
        run.Parameters.NonSecret[H2aBicepInfraDeployHandler.SignalREnabledParameterKey] = flagValue;
        var repo = new FakeRepository(run, etag: "etag-14");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs(signalRDeployed: expected));
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), FakeBicepTemplateInspector.Clean());

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        runner.LastRequest!.SignalREnabled.Should().Be(expected);
    }

    // ---------- T16 handler-id mismatch ----------

    [Fact]
    public async Task HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-15");
        var handler = BuildHandler(repo,
            FakeBicepDeployRunner.Success(BuildOutputs()),
            FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(),
            FakeBicepTemplateInspector.Clean());

        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H0",
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched HandlerId*");
    }

    // ---------- T17 idempotency key format ----------

    [Fact]
    public void IdempotencyKey_IsDeterministicByCustomerAndBicepVer()
    {
        var k1 = H2aBicepInfraDeployHandler.BuildIdempotencyKey("acme", "abc123");
        var k2 = H2aBicepInfraDeployHandler.BuildIdempotencyKey("acme", "abc123");
        k1.Should().Be(k2);
        k1.Should().Be("infra-acme-abc123");
    }

    // ---------- T18 run not found ----------

    [Fact]
    public async Task RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var handler = BuildHandler(repo,
            FakeBicepDeployRunner.Success(BuildOutputs()),
            FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(),
            FakeBicepTemplateInspector.Clean());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.RunNotFound);
    }

    // ---------- helpers ----------

    private static H2aBicepInfraDeployHandler BuildHandler(
        FakeRepository repo,
        FakeBicepDeployRunner runner,
        FakeArmKeyVaultRefProbe probe,
        FakeUpgradeDriftDetector driftDetector,
        FakeBicepTemplateInspector inspector,
        string? runNotesDir = null)
    {
        var options = Options.Create(new BicepInfraDeployOptions
        {
            RunNotesDirectory = runNotesDir
                ?? Path.Combine(Path.GetTempPath(), "h2a-tests-" + Guid.NewGuid().ToString("N")),
        });
        return new H2aBicepInfraDeployHandler(
            repo, runner, probe, driftDetector, inspector, options,
            NullLogger<H2aBicepInfraDeployHandler>.Instance);
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H2aBicepInfraDeployHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(
        bool includeTenantId = true,
        bool includeSubscriptionId = true,
        bool includeBicepVer = true,
        bool includeProvisionedOn = false,
        string tenancyModel = "Model2Dedicated")
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-guid",
            TenancyModel = tenancyModel,
            Status = RunStatus.Running,
            Profile = tenancyModel == "Model1Shared" ? "spaarke-hosted-model1-trial" : "spaarke-hosted-model2",
        };
        if (includeTenantId)
        {
            run.Parameters.NonSecret[H2aBicepInfraDeployHandler.TenantIdParameterKey] = TenantId;
        }
        if (includeSubscriptionId)
        {
            run.Parameters.NonSecret[H2aBicepInfraDeployHandler.SubscriptionIdParameterKey] = SubscriptionId;
        }
        if (includeBicepVer)
        {
            run.Parameters.NonSecret[H2aBicepInfraDeployHandler.BicepVersionParameterKey] = BicepVer;
        }
        if (includeProvisionedOn)
        {
            run.Parameters.NonSecret[H2aBicepInfraDeployHandler.ProvisionedOnParameterKey] = "2026-08-01T00:00:00Z";
        }
        return run;
    }

    private static BicepDeployOutputs BuildOutputs(bool signalRDeployed = false) => new()
    {
        ResourceGroupName = "rg-spaarke-acme-prod",
        UserAssignedIdentityResourceId = ExpectedUamiRid,
        UserAssignedIdentityObjectId = "uami-oid",
        UserAssignedIdentityClientId = "uami-cid",
        AppServiceName = "sprk-acme-prod-app",
        AppServiceStagingSlotName = "staging",
        OpenAiEndpoint = "https://sprk-acme-openai.openai.azure.com/",
        AiSearchEndpoint = "https://sprk-acme-search.search.windows.net/",
        CosmosEndpoint = "https://sprk-acme-cosmos.documents.azure.com:443/",
        SignalRDeployed = signalRDeployed,
    };

    /// <summary>Repository fake — records last written run.</summary>
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

    /// <summary>Bicep-deploy-runner fake — records the last request + returns canned outcomes.</summary>
    private sealed class FakeBicepDeployRunner : IBicepDeployRunner
    {
        private readonly BicepDeployOutcome _outcome;
        public int CallCount { get; private set; }
        public BicepDeployRequest? LastRequest { get; private set; }

        private FakeBicepDeployRunner(BicepDeployOutcome outcome) => _outcome = outcome;

        public static FakeBicepDeployRunner Success(BicepDeployOutputs outputs)
            => new(new BicepDeployOutcome.Success(outputs));

        public static FakeBicepDeployRunner Failure(string diagnostic)
            => new(new BicepDeployOutcome.Failure(diagnostic));

        public Task<BicepDeployOutcome> DeployAsync(BicepDeployRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }

    /// <summary>T1 probe fake.</summary>
    private sealed class FakeArmKeyVaultRefProbe : IArmKeyVaultRefProbe
    {
        private readonly ArmKeyVaultRefProbeResult _result;
        public int CallCount { get; private set; }

        private FakeArmKeyVaultRefProbe(ArmKeyVaultRefProbeResult result) => _result = result;

        public static FakeArmKeyVaultRefProbe Match()
            => new(new ArmKeyVaultRefProbeResult.Match());

        public static FakeArmKeyVaultRefProbe Mismatch(string? observedProd, string? observedStaging)
            => new(new ArmKeyVaultRefProbeResult.Mismatch(observedProd, observedStaging));

        public Task<ArmKeyVaultRefProbeResult> VerifyKeyVaultReferenceIdentityAsync(
            ArmKeyVaultRefProbeInput input, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    /// <summary>Upgrade drift detector fake.</summary>
    private sealed class FakeUpgradeDriftDetector : IUpgradeDriftDetector
    {
        private readonly UpgradeDriftDetectionResult _result;
        public int CallCount { get; private set; }

        public FakeUpgradeDriftDetector()
            : this(new UpgradeDriftDetectionResult.NoDrift()) { }

        private FakeUpgradeDriftDetector(UpgradeDriftDetectionResult result) => _result = result;

        public static FakeUpgradeDriftDetector WithDrift(string report)
            => new(new UpgradeDriftDetectionResult.DriftDetected(report));

        public Task<UpgradeDriftDetectionResult> DetectDriftAsync(
            BicepDeployRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    /// <summary>Template-inspector fake.</summary>
    private sealed class FakeBicepTemplateInspector : IBicepTemplateInspector
    {
        private readonly BicepTemplateInspectionResult _result;
        public int CallCount { get; private set; }

        private FakeBicepTemplateInspector(BicepTemplateInspectionResult result) => _result = result;

        public static FakeBicepTemplateInspector Clean()
            => new(new BicepTemplateInspectionResult(false, string.Empty, false, string.Empty));

        public static FakeBicepTemplateInspector WithRedis(string reference)
            => new(new BicepTemplateInspectionResult(true, reference, false, string.Empty));

        public static FakeBicepTemplateInspector WithUnpinnedModel(string deployment, string version)
            => new(new BicepTemplateInspectionResult(false, string.Empty, true,
                $"modules/openai.bicep: deployment '{deployment}' version '{version}'"));

        public Task<BicepTemplateInspectionResult> InspectAsync(
            BicepDeployRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }
}
