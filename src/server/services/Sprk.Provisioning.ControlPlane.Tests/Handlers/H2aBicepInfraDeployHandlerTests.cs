// -----------------------------------------------------------------------------
// H2aBicepInfraDeployHandlerTests.cs
//
// Unit tests over H2aBicepInfraDeployHandler (task 044 — wave C4).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live Bicep / az CLI / ARM / pwsh /
//   Azure API. Fakes replace the repository + all four collaborator seams
//   (runner, ARM probe, drift detector) so the handler
//   orchestration logic is exercised in isolation. Live-Azure coverage
//   belongs in env-guarded smoke tests (H2a is not exercised end-to-end at
//   CI time by design — a real Bicep deploy is 10–20 min).
//
// COVERAGE:
//   T1  Happy path — Model 2 dedicated: 6-collaborator green + T1 probe
//       Match → Success + Cosmos state advances + interStepState populated.
//   T2  Happy path — Model 1 shared: TenancyModel="Model1" flows to
//       runner request; otherwise identical to T1.
//   T3  Idempotent no-op: run already has H2a CompletedPhase with matching
//       key → Success (no runner call, no state mutation).
//   T4  Missing tenantId (§4D I1): Failure(Resumable, missing-tenant-id) +
//       no runner call + Cosmos marked Failed.
//   T5  Missing subscriptionId: Failure(Resumable, missing-subscription-id).
//   T6  ARM template unresolvable (task 245b): Failure(Resumable,
//       arm-template-unavailable), no deploy. The idempotency version is the
//       resolved template's content version; a changed template is not a no-op.
//   (T7 "Redis presence" retired with task 245b — D-12 made Redis per-customer.)
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
//   T17 Idempotency key format determinism: same customerId + template version
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
    // Task 245b: H2a's idempotency version is the resolved template's content version.
    private const string BicepVer = "abc123def456";
    // Declares every output H2a requires (inspector rule R4, task 246) — like the real customer.json.
    private static readonly ResolvedArmTemplate TestTemplate =
        new("customer", "customer-arm-2026.10.01-1.json", TemplateDeclaring(ArmDeploymentRunner.RequiredOutputNames), BicepVer);

    private static string TemplateDeclaring(IEnumerable<string> outputNames) =>
        "{\"resources\":[],\"outputs\":{" +
        string.Join(",", outputNames.Select(n => $"\"{n}\":{{\"type\":\"string\",\"value\":\"x\"}}")) + "}}";
    private const string ExpectedUamiRid = "/subscriptions/x/resourceGroups/rg-spaarke-acme-prod/providers/Microsoft.ManagedIdentity/userAssignedIdentities/sprk-acme-prod-uami";

    // ---------- T1 happy path — Model 2 ----------

    [Fact]
    public async Task HappyPath_Model2_AllCollaboratorsGreen_SucceedsAndAdvancesState()
    {
        var run = BuildRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run, etag: "etag-1");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs(signalRDeployed: false));
        var probe = FakeArmKeyVaultRefProbe.Match();
        var driftDetector = new FakeUpgradeDriftDetector();
        var handler = BuildHandler(repo, runner, probe, driftDetector, RealInspector());

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
        // Task 245a — the customer-stamp values downstream handlers read from typed state.
        repo.LastWrittenRun.InterStepState.ResourceGroupName.Should().Be("rg-spaarke-acme-prod");
        repo.LastWrittenRun.InterStepState.AppServiceName.Should().Be("sprk-acme-prod-api");
        repo.LastWrittenRun.InterStepState.AppServiceStagingSlotName.Should().Be("staging");
        repo.LastWrittenRun.InterStepState.KeyVaultName.Should().Be("sprk-acme-prod-kv");
        repo.LastWrittenRun.InterStepState.KeyVaultUri.Should().Be("https://sprk-acme-prod-kv.vault.azure.net/");
        repo.LastWrittenRun.InterStepState.MiResourceId.Should().Be(ExpectedUamiRid);
        repo.LastWrittenRun.InterStepState.ServiceBusFullyQualifiedNamespace.Should().Be("spaarke-acme-prod-sbus.servicebus.windows.net");
        // Task 242 — the Managed Redis endpoint H4b sets as Redis__Endpoint.
        repo.LastWrittenRun.InterStepState.RedisEndpoint.Should().Be("sprk-acme-prod-redis.westus2.redis.azure.net:10000");
        // Task 246 — the Content Safety endpoint H4b sets as AiSafety__ContentSafety__Endpoint.
        repo.LastWrittenRun.InterStepState.ContentSafetyEndpoint.Should().Be("https://sprk-acme-prod-contentsafety.cognitiveservices.azure.com/");

        // Each collaborator called exactly once.
        runner.CallCount.Should().Be(1);
        probe.CallCount.Should().Be(1);
        driftDetector.CallCount.Should().Be(0, "upgrade mode did not fire — no provisionedOn param");
        runner.LastRequest.Should().NotBeNull();
        runner.LastRequest!.TenancyModel.Should().Be("Model2");
        // Task 245b: the template resolved for the key is the template deployed — one resolution.
        runner.ResolvedFor.Should().Be(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2);
        runner.LastRequest.Template.Should().BeSameAs(runner.Template);
    }

    // ---------- T2 happy path — Model 1 (dedicated stamp, D-12) ----------

    [Fact]
    public async Task HappyPath_Model1_TenancyModelFlowsToRunner()
    {
        var run = BuildRun(tenancyModel: "Model1");
        var repo = new FakeRepository(run, etag: "etag-2");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs(signalRDeployed: false));
        var probe = FakeArmKeyVaultRefProbe.Match();
        var driftDetector = new FakeUpgradeDriftDetector();
        var handler = BuildHandler(repo, runner, probe, driftDetector, RealInspector());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        runner.LastRequest!.TenancyModel.Should().Be("Model1");
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
        var handler = BuildHandler(repo, runner, probe, driftDetector, RealInspector());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        runner.CallCount.Should().Be(0);
        probe.CallCount.Should().Be(0);
    }

    // ---------- T4 missing tenantId (§4D I1) ----------

    [Fact]
    public async Task MissingTenantId_FailsResumable_NoRunnerCall()
    {
        var run = BuildRun(includeTenantId: false);
        var repo = new FakeRepository(run, etag: "etag-4");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector());

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
            new FakeUpgradeDriftDetector(), RealInspector());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.MissingSubscriptionId);
        runner.CallCount.Should().Be(0);
    }

    // ---------- T6 template resolution (task 245b) ----------

    [Fact]
    public async Task TemplateUnresolvable_FailsResumable_NothingDeployed()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-6");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        runner.ResolveFailure = new InvalidDataException("blob sha256 does not match the manifest");
        var inspector = RealInspector();
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), inspector);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.ArmTemplateUnavailable);
        failure.Diagnostic.Should().Contain("sha256 does not match");
        runner.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]   // the artifact store is reachable and "latest" names a NEWER template
    [InlineData(true)]    // the artifact store is down
    public async Task AlreadyCompletedInThisRun_IsADuplicateNoOp_WithoutResolvingTheTemplate(bool artifactStoreDown)
    {
        // ADR-004 at-least-once: a redelivered H2a message must neither fail a run that already moved on nor
        // redeploy a newer template mid-run (outputs already consumed downstream; no upgrade what-if).
        var run = BuildRun();
        var priorKey = H2aBicepInfraDeployHandler.BuildIdempotencyKey(CustomerId, "earlier-template-version");
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = H2aBicepInfraDeployHandler.HandlerIdentifier,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            IdempotencyKey = priorKey,
            JobId = RunId,
        });
        var repo = new FakeRepository(run, etag: "etag-6c");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        if (artifactStoreDown)
        {
            runner.ResolveFailure = new Azure.RequestFailedException(503, "blob service unavailable");
        }
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>().Which.IdempotencyKey.Should().Be(priorKey);
        runner.ResolvedFor.Should().BeNull("a completed H2a is not re-resolved");
        runner.CallCount.Should().Be(0);
        repo.LastWrittenRun.Should().BeNull("a duplicate delivery does not touch the run");
    }

    [Fact]
    public async Task IdempotencyKey_NamesTheContentVersionOfTheTemplateDeployed()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-6d");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        runner.Template = TestTemplate with { Version = "0123abcd" };
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>().Which.IdempotencyKey
            .Should().Be(H2aBicepInfraDeployHandler.BuildIdempotencyKey(CustomerId, "0123abcd"));
    }

    // ---------- EXEC-04 blank TenancyModel (Wave 2 pre-dispatch remediation 2026-08-27) ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankTenancyModel_FailsResumable_NoRunnerCall(string? tenancyModel)
    {
        var run = BuildRun(tenancyModel: tenancyModel!);
        var repo = new FakeRepository(run, etag: "etag-exec04");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var inspector = RealInspector();
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), inspector);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.MissingTenancyModel);
        failure.Diagnostic.Should().Contain("silently default");
        failure.Diagnostic.Should().Contain("TenancyModel");
        runner.CallCount.Should().Be(0, "H2a MUST NOT invoke deploy runner on blank TenancyModel");
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- HANDLER-05 resource-name availability (Wave 2 pre-dispatch remediation 2026-08-27) ----------

    [Fact]
    public async Task Handler05_NameAvailability_Conflict_FailsResumable_NoRunnerCall()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-h05-conflict");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var nameProbe = FakeResourceNameAvailabilityProbe.Conflict(
            ResourceNameKind.ServiceBusNamespace,
            "spaarke-acme-prod-sbus",
            "AlreadyExists: The specified service namespace is already taken.");
        var handler = BuildHandler(
            repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector(),
            nameProbe: nameProbe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.ResourceNameTaken);
        failure.Diagnostic.Should().Contain("spaarke-acme-prod-sbus");
        failure.Diagnostic.Should().Contain("AlreadyExists");
        failure.Diagnostic.Should().Contain("ServiceBusNamespace");
        runner.CallCount.Should().Be(0, "runner MUST NOT fire on a name-availability conflict");
        nameProbe.CallCount.Should().Be(1);
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    [Fact]
    public async Task Handler05_NameAvailability_AllAvailable_ProceedsToRunner()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-h05-ok");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var nameProbe = FakeResourceNameAvailabilityProbe.AllAvailable();
        var handler = BuildHandler(
            repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector(),
            nameProbe: nameProbe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        nameProbe.CallCount.Should().Be(1);
        runner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handler05_NameAvailability_UpgradeMode_SkipsCheck()
    {
        // Existing customer upgrade would collide with ITSELF (correct: an upgrade
        // does not re-provision, the resources already exist under the customer).
        // Handler MUST NOT invoke the name-availability probe on upgrade runs.
        var run = BuildRun(includeProvisionedOn: true);
        var repo = new FakeRepository(run, etag: "etag-h05-upgrade");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var nameProbe = FakeResourceNameAvailabilityProbe.Conflict(
            ResourceNameKind.StorageAccount, "sprkacmeprodsa", "would-collide-if-checked");
        var handler = BuildHandler(
            repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector(),
            nameProbe: nameProbe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        nameProbe.CallCount.Should().Be(0, "upgrade runs MUST skip name-availability check to avoid self-collision false-positive");
    }

    [Fact]
    public void Handler05_BuildGloballyNamespacedNameChecks_MirrorsCustomerBicepNamingConvention()
    {
        // customer.bicep: storageAccountName = take(toLower(replace('${baseName}sa', '-', '')), 24)
        //                  serviceBusName     = 'spaarke-${customerId}-${environmentName}-sbus'
        // (the check used to probe 'sprk-{id}-{env}-sb', a name the template never creates — task 245a)
        var checks = H2aBicepInfraDeployHandler.BuildGloballyNamespacedNameChecks("acme", "prod");

        checks.Should().HaveCount(2);
        checks[0].Kind.Should().Be(ResourceNameKind.StorageAccount);
        checks[0].RequestedName.Should().Be("sprkacmeprodsa");
        checks[1].Kind.Should().Be(ResourceNameKind.ServiceBusNamespace);
        checks[1].RequestedName.Should().Be("spaarke-acme-prod-sbus");
    }

    [Fact]
    public void Handler05_BuildGloballyNamespacedNameChecks_StorageNameTruncatedTo24Chars()
    {
        // Long customerId should trigger the 24-char cap on the storage name
        // (customer.bicep line 141 verbatim: take(..., 24)).
        var checks = H2aBicepInfraDeployHandler.BuildGloballyNamespacedNameChecks(
            "verylongcustomerid", "staging");

        var storage = checks.Single(c => c.Kind == ResourceNameKind.StorageAccount);
        storage.RequestedName.Length.Should().BeLessThanOrEqualTo(24);
        storage.RequestedName.Should().StartWith("sprk");
    }

    // ---------- T8 unpinned model deployment ----------

    [Fact]
    public async Task UnpinnedModelDeployment_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-8");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        // The real inspector over a template whose model descriptor is unpinned.
        runner.Template = TestTemplate with
        {
            Json = """{"resources":[{"properties":{"template":{"parameters":{"deployments":{"defaultValue":[{"name":"gpt-4o","model":"gpt-4o","version":"latest","capacity":10}]}}}}}]}""",
        };
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.ModelVersionNotPinned);
        failure.Diagnostic.Should().Contain("ADR-020");
        runner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task TemplateMissingARequiredOutput_FailsResumable_BeforeDeploying()
    {
        // Task 246 (R4): a template published before contentSafetyEndpoint existed, resolved by this worker. It must
        // stop here — Resumable, nothing deployed — not deploy for ~20 minutes and then quarantine on the outputs.
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-246");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        runner.Template = TestTemplate with
        {
            Json = TemplateDeclaring(ArmDeploymentRunner.RequiredOutputNames.Where(n => n != "contentSafetyEndpoint")),
        };
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.TemplateOutputsMissing);
        failure.Diagnostic.Should().Contain("contentSafetyEndpoint").And.Contain("publish-provisioning-arm-artifacts.yml");
        runner.CallCount.Should().Be(0, "nothing is deployed when the template cannot satisfy H2a's outputs");
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
                RealInspector(), runNotesDir: tempDir);

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
            driftDetector, RealInspector());

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
            RealInspector());

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
            new FakeUpgradeDriftDetector(), RealInspector());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.BicepDeployFailed);
        failure.Diagnostic.Should().Contain("az deployment sub create exit 1");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- HANDLER-10 kvRefIdentity invalid detector (Wave 2 pre-dispatch remediation 2026-08-27) ----------

    [Fact]
    public async Task Handler10_InvalidKvRefIdentity_FailsQuarantineRequired_NoRunnerCall()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-h10");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        runner.Template = TestTemplate with
        {
            Json = """{"resources":[{"type":"Microsoft.Web/sites","properties":{"keyVaultReferenceIdentity":"SystemAssigned"}}]}""",
        };
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.KvRefIdentityInvalid);
        failure.Diagnostic.Should().Contain("SystemAssigned");
        failure.Diagnostic.Should().Contain("ADR-028");
        runner.CallCount.Should().Be(0, "runner MUST NOT fire on kvRefIdentity-invalid detection");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- HANDLER-06 CogSvc-soft-lock routing (Wave 2 pre-dispatch remediation 2026-08-27) ----------

    [Fact]
    public async Task Handler06_RunnerReturnsSoftLockFailure_MappedToResumable_CogSvcSoftLockPersistent()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-h06");
        // The runner surfaces the CogSvc-soft-lock-persistent prefix; the handler MUST
        // route to Resumable + CogSvcSoftLockPersistent (NOT the default
        // QuarantineRequired + BicepDeployFailed).
        var runner = FakeBicepDeployRunner.Failure(
            ArmDeploymentRunner.CogSvcSoftLockDiagnosticPrefix +
            " ARM deployment 'customer-acme-20260827121200' for customerId 'acme' returned HTTP 409 RequestConflict on the Cognitive Services scope after 4 attempts across the [30s, 90s, 180s] backoff schedule.");
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable,
            "soft-lock exhaustion is Resumable — the ARM deploy rolls back on 409 so no partial state exists");
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.CogSvcSoftLockPersistent);
        failure.Diagnostic.Should().Contain("CogSvc-soft-lock-persistent");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed,
            "Resumable failure marks Failed (not Quarantined)");
    }

    [Fact]
    public async Task Handler06_RunnerReturnsGenericFailure_StillMappedToQuarantineRequired()
    {
        // Regression guard: non-soft-lock runner failures MUST continue to
        // route to the default QuarantineRequired + BicepDeployFailed.
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-h06-generic");
        var runner = FakeBicepDeployRunner.Failure("Generic ARM failure — quota exhausted for gpt-4o in eastus");
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.BicepDeployFailed);
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
            KeyVaultName = "kv",
            KeyVaultUri = "https://kv.vault.azure.net/",
            ServiceBusFullyQualifiedNamespace = "ns.servicebus.windows.net",
            RedisEndpoint = "r.westus2.redis.azure.net:10000",
            ContentSafetyEndpoint = "https://cs.cognitiveservices.azure.com/",
            SignalRDeployed = false,
        };
        var runner = FakeBicepDeployRunner.Success(incomplete);
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.BicepDeployOutputsIncomplete);
        failure.Diagnostic.Should().Contain(nameof(BicepDeployOutputs.OpenAiEndpoint));
    }

    [Theory]
    [InlineData(nameof(BicepDeployOutputs.KeyVaultName))]
    [InlineData(nameof(BicepDeployOutputs.KeyVaultUri))]
    [InlineData(nameof(BicepDeployOutputs.ServiceBusFullyQualifiedNamespace))]
    [InlineData(nameof(BicepDeployOutputs.RedisEndpoint))]
    [InlineData(nameof(BicepDeployOutputs.ContentSafetyEndpoint))]
    public async Task RunnerReturnsBlankStampOutput_FailsQuarantineRequired_NamingTheField(string blankField)
    {
        // Task 245a: the three outputs H2a now persists for downstream handlers are
        // required like the rest — a blank one must stop the run here, not at the reader.
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-245a-" + blankField);
        var complete = BuildOutputs();
        var outputs = new BicepDeployOutputs
        {
            ResourceGroupName = complete.ResourceGroupName,
            UserAssignedIdentityResourceId = complete.UserAssignedIdentityResourceId,
            UserAssignedIdentityObjectId = complete.UserAssignedIdentityObjectId,
            UserAssignedIdentityClientId = complete.UserAssignedIdentityClientId,
            AppServiceName = complete.AppServiceName,
            AppServiceStagingSlotName = complete.AppServiceStagingSlotName,
            OpenAiEndpoint = complete.OpenAiEndpoint,
            AiSearchEndpoint = complete.AiSearchEndpoint,
            CosmosEndpoint = complete.CosmosEndpoint,
            KeyVaultName = blankField == nameof(BicepDeployOutputs.KeyVaultName) ? "" : complete.KeyVaultName,
            KeyVaultUri = blankField == nameof(BicepDeployOutputs.KeyVaultUri) ? "" : complete.KeyVaultUri,
            ServiceBusFullyQualifiedNamespace = blankField == nameof(BicepDeployOutputs.ServiceBusFullyQualifiedNamespace)
                ? "" : complete.ServiceBusFullyQualifiedNamespace,
            RedisEndpoint = blankField == nameof(BicepDeployOutputs.RedisEndpoint) ? "" : complete.RedisEndpoint,
            ContentSafetyEndpoint = blankField == nameof(BicepDeployOutputs.ContentSafetyEndpoint) ? "" : complete.ContentSafetyEndpoint,
            SignalRDeployed = false,
        };
        var handler = BuildHandler(repo, FakeBicepDeployRunner.Success(outputs), FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BicepDeployRejectionCodes.BicepDeployOutputsIncomplete);
        failure.Diagnostic.Should().Contain(blankField);
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
            new FakeUpgradeDriftDetector(), RealInspector());

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        runner.LastRequest!.SignalREnabled.Should().Be(expected);
    }

    // ---------- A38b requireSecretFreeIdentity gate — retired by task 225b ----------
    // customer.bicep's `requireSecretFreeIdentity` parameter had no effect after T226 and was removed together
    // with the intake key, the H2a read and the runner payload entry. Secret-free is now the H4 default
    // (KvSecretsPopulationOptions.RequireSecretFreeIdentity) — see FileKvSecretManifestTests / H4 tests. Payload ↔
    // template parity is pinned by ArmTemplateInspectorTests.RealCustomerTemplate_DeclaresEveryParameterTheRunnerSends.

    // ---------- ISH-08 OpenAI location override (Wave 5 punchlist, 2026-08-27) ----------

    [Fact]
    public async Task Ish08_OpenAiLocation_Absent_RunnerRequestCarriesNull_DefaultBicepParamWins()
    {
        var run = BuildRun();
        // Explicitly ensure no openAiLocation key present.
        run.Parameters.NonSecret.Remove(H2aBicepInfraDeployHandler.OpenAiLocationParameterKey);
        var repo = new FakeRepository(run, etag: "etag-ish08-absent");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector());

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        runner.LastRequest.Should().NotBeNull();
        runner.LastRequest!.OpenAiLocation.Should().BeNull(
            "ISH-08 — absent NonSecret key must yield null so customer.bicep's openAiLocation default wins.");
    }

    [Fact]
    public async Task Ish08_OpenAiLocation_Populated_FlowsIntoRunnerRequest()
    {
        var run = BuildRun();
        run.Parameters.NonSecret[H2aBicepInfraDeployHandler.OpenAiLocationParameterKey] = "eastus2";
        var repo = new FakeRepository(run, etag: "etag-ish08-populated");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector());

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        runner.LastRequest!.OpenAiLocation.Should().Be("eastus2",
            "ISH-08 — populated NonSecret['openAiLocation'] must flow onto BicepDeployRequest so the runner overrides the Bicep default.");
    }

    [Fact]
    public async Task Ish08_OpenAiLocation_Whitespace_TreatedAsAbsent()
    {
        var run = BuildRun();
        run.Parameters.NonSecret[H2aBicepInfraDeployHandler.OpenAiLocationParameterKey] = "   ";
        var repo = new FakeRepository(run, etag: "etag-ish08-whitespace");
        var runner = FakeBicepDeployRunner.Success(BuildOutputs());
        var handler = BuildHandler(repo, runner, FakeArmKeyVaultRefProbe.Match(),
            new FakeUpgradeDriftDetector(), RealInspector());

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        runner.LastRequest!.OpenAiLocation.Should().BeNull(
            "ISH-08 — whitespace-only override is treated as absent (Bicep default wins).");
    }

    [Fact]
    public void Ish08_BuildParametersPayload_AbsentOpenAiLocation_OmitsKey()
    {
        // Overrides via null (pre-ISH-08 signature parity) — the ARM parameters
        // payload MUST omit openAiLocation entirely so the Bicep default wins.
        var request = new BicepDeployRequest(
            CustomerId: "acme",
            TenantId: "00000000-1111-2222-3333-444444444444",
            SubscriptionId: "22222222-3333-4444-5555-666666666666",
            TenancyModel: "Model2",
            Template: TestTemplate,
            EnvironmentName: "prod",
            Location: "westus2",
            SignalREnabled: false,
            OpenAiLocation: null);
        var payload = ArmDeploymentRunner.BuildParametersPayload(request, "7d1f0c3e-2b6a-4c55-9e1d-3a8b5c6d7e8f");
        var json = payload.ToString();
        json.Should().NotContain("openAiLocation",
            "ISH-08 — absent override must NOT surface in the ARM parameters payload (Bicep default wins).");
    }

    [Fact]
    public void Ish08_BuildParametersPayload_PopulatedOpenAiLocation_IncludesKey()
    {
        var request = new BicepDeployRequest(
            CustomerId: "acme",
            TenantId: "00000000-1111-2222-3333-444444444444",
            SubscriptionId: "22222222-3333-4444-5555-666666666666",
            TenancyModel: "Model2",
            Template: TestTemplate,
            EnvironmentName: "prod",
            Location: "westus2",
            SignalREnabled: false,
            OpenAiLocation: "eastus2");
        var payload = ArmDeploymentRunner.BuildParametersPayload(request, "7d1f0c3e-2b6a-4c55-9e1d-3a8b5c6d7e8f");
        var json = payload.ToString();
        json.Should().Contain("\"openAiLocation\"",
            "ISH-08 — populated override MUST appear in the ARM parameters payload so it overrides the Bicep default.");
        json.Should().Contain("eastus2");
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
            RealInspector());

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
            RealInspector());

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
        ArmTemplateInspector inspector,
        string? runNotesDir = null,
        FakeResourceNameAvailabilityProbe? nameProbe = null,
        BicepInfraDeployOptions? optionsOverride = null)
    {
        var options = Options.Create(optionsOverride ?? new BicepInfraDeployOptions
        {
            RunNotesDirectory = runNotesDir
                ?? Path.Combine(Path.GetTempPath(), "h2a-tests-" + Guid.NewGuid().ToString("N")),
        });
        // HANDLER-05 (Wave 2 pre-dispatch remediation 2026-08-27): the
        // resource-name-availability probe defaults to "all available" so
        // existing tests remain unaffected. HANDLER-05-specific tests supply
        // a Conflict-returning fake explicitly.
        var effectiveNameProbe = nameProbe ?? FakeResourceNameAvailabilityProbe.AllAvailable();
        return new H2aBicepInfraDeployHandler(
            repo, runner, probe, driftDetector, inspector, effectiveNameProbe, options,
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
        bool includeProvisionedOn = false,
        string tenancyModel = "Model2")
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-guid",
            TenancyModel = tenancyModel,
            Status = RunStatus.Running,
            Profile = tenancyModel == "Model1" ? "spaarke-hosted-model2" : "customer-owned-model2",
        };
        if (includeTenantId)
        {
            run.Parameters.NonSecret[H2aBicepInfraDeployHandler.TenantIdParameterKey] = TenantId;
        }
        if (includeSubscriptionId)
        {
            run.Parameters.NonSecret[H2aBicepInfraDeployHandler.SubscriptionIdParameterKey] = SubscriptionId;
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
        AppServiceName = "sprk-acme-prod-api",
        AppServiceStagingSlotName = "staging",
        OpenAiEndpoint = "https://sprk-acme-openai.openai.azure.com/",
        AiSearchEndpoint = "https://sprk-acme-search.search.windows.net/",
        CosmosEndpoint = "https://sprk-acme-cosmos.documents.azure.com:443/",
        KeyVaultName = "sprk-acme-prod-kv",
        KeyVaultUri = "https://sprk-acme-prod-kv.vault.azure.net/",
        ServiceBusFullyQualifiedNamespace = "spaarke-acme-prod-sbus.servicebus.windows.net",
        RedisEndpoint = "sprk-acme-prod-redis.westus2.redis.azure.net:10000",
        ContentSafetyEndpoint = "https://sprk-acme-prod-contentsafety.cognitiveservices.azure.com/",
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

        /// <summary>What ResolveTemplateAsync returns (task 245b).</summary>
        public ResolvedArmTemplate Template { get; set; } = TestTemplate;

        /// <summary>When set, ResolveTemplateAsync throws it.</summary>
        public Exception? ResolveFailure { get; set; }

        public Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel? ResolvedFor { get; private set; }

        public Task<ResolvedArmTemplate> ResolveTemplateAsync(
            Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel tenancyModel, CancellationToken ct)
        {
            ResolvedFor = tenancyModel;
            return ResolveFailure is null ? Task.FromResult(Template) : Task.FromException<ResolvedArmTemplate>(ResolveFailure);
        }

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

    /// <summary>
    /// HANDLER-05 (Wave 2 pre-dispatch remediation 2026-08-27) — probe fake.
    /// Default constructor returns AllAvailable; static helpers construct
    /// Conflict-returning fakes for the F10 negative-branch tests.
    /// </summary>
    private sealed class FakeResourceNameAvailabilityProbe : IResourceNameAvailabilityProbe
    {
        private readonly ResourceNameAvailabilityResult _result;
        public int CallCount { get; private set; }
        public ResourceNameAvailabilityRequest? LastRequest { get; private set; }

        private FakeResourceNameAvailabilityProbe(ResourceNameAvailabilityResult result) => _result = result;

        public static FakeResourceNameAvailabilityProbe AllAvailable()
            => new(new ResourceNameAvailabilityResult.AllAvailable());

        public static FakeResourceNameAvailabilityProbe Conflict(
            ResourceNameKind kind, string name, string reason)
            => new(new ResourceNameAvailabilityResult.Conflict(kind, name, reason));

        public Task<ResourceNameAvailabilityResult> CheckAvailabilityAsync(
            ResourceNameAvailabilityRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    /// <summary>The real (pure) template inspector — task 245b removed its stub seam (ADR-038 B5).</summary>
    private static ArmTemplateInspector RealInspector() => new(NullLogger<ArmTemplateInspector>.Instance);
}
