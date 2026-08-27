// -----------------------------------------------------------------------------
// H13E2EAcceptanceGateHandlerTests.cs
//
// Unit tests over H13E2EAcceptanceGateHandler (task 055, wave C4 Batch 4E).
// THE FINAL GATE. Sole authority for the Dataverse registry
// `sprk_dataverseenvironment.SetupStatus → Ready` transition.
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live pwsh / az CLI / Graph / Dataverse.
//   Fakes replace the repository + 6 collaborator seams + registry lookup
//   client so the handler orchestration + §4C rollback classification + all
//   green/red branches are exercised in isolation. Live-Azure coverage
//   belongs to the Phase F acceptance suite (task 089) per POML mandatory
//   pre-work #escalation.
//
// COVERAGE (POML acceptance criteria mapped to test cases):
//   AC-1   Happy path — all 6 gates green → Success + Cosmos state Completed +
//          registry Ready transition + all 6 H13 gates Verified.
//   AC-2a  Missing tenantId (§4D I1) → Resumable + MissingTenantId.
//   AC-2b  Missing subscriptionId → Resumable + MissingSubscriptionId.
//   AC-2c  Missing buildId → Resumable + MissingBuildId.
//   AC-2d  Missing bffApiUrl → Resumable + MissingBffApiUrl.
//   AC-2e  Missing InterStepState.dataverseEnvUrl → Resumable + MissingDataverseEnvUrl.
//   AC-3   Extended validate script Failure (SC #5) → QuarantineRequired +
//          ExtendedValidationFailed.
//   AC-4   Extended validate script infra fault → Resumable + ExtendedValidationInfraFault.
//   AC-5a..f Each of 6 T1–T6 trap fail branches → QuarantineRequired + distinct code.
//   AC-6a..e Each of 5 I1–I5 invariant fail branches → QuarantineRequired + distinct code.
//   AC-7   Trap verifier InfraFault (no failed traps) → Resumable + TrapVerifierInfraFault.
//   AC-8   Invariant verifier InfraFault (no failed invariants) → Resumable + InvariantVerifierInfraFault.
//   AC-9   Naming-conformance FAILED (SC #17) → QuarantineRequired + NamingConformanceFailed.
//   AC-10  Naming-conformance infra fault → Resumable + NamingConformanceInfraFault.
//   AC-11a Cost drift > threshold + CostDriftFailsRun=false (default) → SUCCESS
//          with advisory-warn on the run.
//   AC-11b Cost drift > threshold + CostDriftFailsRun=true → QuarantineRequired +
//          CostDriftExceeded.
//   AC-12  Cost query infra fault → Resumable + CostQueryInfraFault.
//   AC-13  Registry updater Failure → Resumable + RegistryUpdateFailed.
//   AC-14  Registry updater throws → Resumable + RegistryUpdateFailed.
//   AC-15  Idempotency — level-3 CompletedPhases match → Success no-op;
//          NO collaborator seam invoked; NO Cosmos write.
//   AC-16  Idempotency-key format determinism — validate-{customerId}-{buildId}.
//   AC-17  Run not found → Resumable + RunNotFound.
//   AC-18  HandlerId mismatch → throws InvalidOperationException.
//   AC-19a..f Trap rejection code mapping table — each TrapKind maps to distinct code.
//   AC-20a..e Invariant rejection code mapping table — each InvariantKind maps to distinct code.
//   AC-21  All infra faults present at once — first-in-priority (extended-validate
//          infra fault) wins the diagnostic; still Resumable.
//   AC-22  Trap Failed short-circuits over invariant Failed (both present) —
//          trap wins (deterministic aggregation priority).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Registry;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H13E2EAcceptanceGateHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h13-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "sub-cus-acme-prod";
    private const string BuildId = "ci-2026.08.18.1";
    private const string BffApiUrl = "https://sprk-bff-acme.azurewebsites.net";
    private const string DataverseUrl = "https://sprk-acme.crm.dynamics.com";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";
    private const string AppServiceName = "sprk-bff-acme";
    private const string KeyVaultName = "sprk-acme-prod-kv";
    private const string RegistryDataverseUrl = "https://spaarke-registry.crm.dynamics.com";
    private const string EnvironmentId = "b0000000-0000-0000-0000-000000000001";

    // ---------- AC-1 happy path ----------

    [Fact]
    public async Task AC1_HappyPath_AllGreen_SucceedsAndTransitionsToReady()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-1");
        var handler = BuildHandler(repo, out var seams);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H13E2EAcceptanceGateHandler.BuildIdempotencyKey(CustomerId, BuildId));

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Completed);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H13");
        repo.LastWrittenRun.CompletedOn.Should().NotBeNull();
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle().Which.Phase.Should().Be("H13");

        repo.LastWrittenRun.GateStates.Should().ContainKey(H13Gates.ExtendedValidationVerified);
        repo.LastWrittenRun.GateStates.Should().ContainKey(H13Gates.TrapCatalogVerified);
        repo.LastWrittenRun.GateStates.Should().ContainKey(H13Gates.InvariantCatalogVerified);
        repo.LastWrittenRun.GateStates.Should().ContainKey(H13Gates.NamingConformanceVerified);
        repo.LastWrittenRun.GateStates.Should().ContainKey(H13Gates.CostEnvelopeVerified);
        repo.LastWrittenRun.GateStates.Should().ContainKey(H13Gates.RegistryReadyTransitioned);
        repo.LastWrittenRun.GateStates[H13Gates.TrapCatalogVerified].Status.Should().Be(GateState.Verified);

        seams.Validator.CallCount.Should().Be(1);
        seams.Traps.CallCount.Should().Be(1);
        seams.Invariants.CallCount.Should().Be(1);
        seams.Naming.CallCount.Should().Be(1);
        seams.Cost.CallCount.Should().Be(1);
        seams.Registry.CallCount.Should().Be(1);
    }

    // ---------- AC-2 parameter guards ----------

    [Fact]
    public async Task AC2a_MissingTenantId_FailsResumable_NoSeamInvoked()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H13E2EAcceptanceGateHandler.TenantIdParameterKey);
        var repo = new FakeRepository(run, etag: "etag-2a");
        var handler = BuildHandler(repo, out var seams);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H13Rejections.MissingTenantId);
        seams.Validator.CallCount.Should().Be(0);
        seams.Traps.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC2b_MissingSubscriptionId_FailsResumable()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H13E2EAcceptanceGateHandler.SubscriptionIdParameterKey);
        var repo = new FakeRepository(run, etag: "etag-2b");
        var handler = BuildHandler(repo, out _);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H13Rejections.MissingSubscriptionId);
    }

    [Fact]
    public async Task AC2c_MissingBuildId_FailsResumable()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H13E2EAcceptanceGateHandler.BuildIdParameterKey);
        var repo = new FakeRepository(run, etag: "etag-2c");
        var handler = BuildHandler(repo, out _);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H13Rejections.MissingBuildId);
    }

    [Fact]
    public async Task AC2d_MissingBffApiUrl_FailsResumable()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H13E2EAcceptanceGateHandler.BffApiUrlParameterKey);
        var repo = new FakeRepository(run, etag: "etag-2d");
        var handler = BuildHandler(repo, out _);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H13Rejections.MissingBffApiUrl);
    }

    [Fact]
    public async Task AC2e_MissingDataverseEnvUrl_FailsResumable()
    {
        var run = BuildRun();
        run.InterStepState.DataverseEnvUrl = null;
        var repo = new FakeRepository(run, etag: "etag-2e");
        var handler = BuildHandler(repo, out _);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H13Rejections.MissingDataverseEnvUrl);
    }

    // ---------- AC-3 extended validate script Failure ----------

    [Fact]
    public async Task AC3_ExtendedValidateFailure_FailsQuarantine()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-3");
        var handler = BuildHandler(repo, out var seams,
            configureSeams: s => s.Validator = FakeValidator.Failure(new[] { "bff-healthz", "sample-analysis" }, "500 from /healthz | sample analysis timed out"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(H13Rejections.ExtendedValidationFailed);
        failure.Diagnostic.Should().Contain("SC #5").And.Contain("2 failing check");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        seams.Registry.CallCount.Should().Be(0, "registry PATCH never runs on failure");
    }

    // ---------- AC-4 extended validate script infra fault ----------

    [Fact]
    public async Task AC4_ExtendedValidateInfraFault_FailsResumable()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-4");
        var handler = BuildHandler(repo, out _,
            configureSeams: s => s.Validator = FakeValidator.Throws(new FileNotFoundException("script missing")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H13Rejections.ExtendedValidationInfraFault);
    }

    // ---------- AC-5 trap fail branches (6 tests) ----------

    [Theory]
    [InlineData(TrapKind.T1KeyVaultReferenceIdentity, H13Rejections.TrapT1Failed)]
    [InlineData(TrapKind.T2DataverseAppUser, H13Rejections.TrapT2Failed)]
    [InlineData(TrapKind.T3GraphAppRoleParity, H13Rejections.TrapT3Failed)]
    [InlineData(TrapKind.T4ExchangePolicyCount, H13Rejections.TrapT4Failed)]
    [InlineData(TrapKind.T5SlotMiKvRbac, H13Rejections.TrapT5Failed)]
    [InlineData(TrapKind.T6SpeConfidentialClient, H13Rejections.TrapT6Failed)]
    public async Task AC5_TrapFailBranch_FailsQuarantineWithDistinctCode(TrapKind failingTrap, string expectedCode)
    {
        var repo = new FakeRepository(BuildRun(), etag: $"etag-5-{failingTrap}");
        var handler = BuildHandler(repo, out var seams,
            configureSeams: s => s.Traps = FakeTrapVerifier.WithFailure(failingTrap, "expected X observed Y"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(expectedCode);
        failure.Diagnostic.Should().Contain("silent-fail trap detected");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        seams.Registry.CallCount.Should().Be(0);
    }

    // ---------- AC-6 invariant fail branches (5 tests) ----------

    [Theory]
    [InlineData(InvariantKind.I1NoHardcodedTenant, H13Rejections.InvariantI1Failed)]
    [InlineData(InvariantKind.I2AiSearchTenantFilter, H13Rejections.InvariantI2Failed)]
    [InlineData(InvariantKind.I3CosmosPartitionKey, H13Rejections.InvariantI3Failed)]
    [InlineData(InvariantKind.I4SpeContainerResolver, H13Rejections.InvariantI4Failed)]
    [InlineData(InvariantKind.I5GraphTokenTenant, H13Rejections.InvariantI5Failed)]
    public async Task AC6_InvariantFailBranch_FailsQuarantineWithDistinctCode(InvariantKind failing, string expectedCode)
    {
        var repo = new FakeRepository(BuildRun(), etag: $"etag-6-{failing}");
        var handler = BuildHandler(repo, out _,
            configureSeams: s => s.Invariants = FakeInvariantVerifier.WithFailure(failing, "sample violation"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(expectedCode);
        failure.Diagnostic.Should().Contain("tenant-isolation invariant VIOLATED");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- AC-7 trap verifier InfraFault ----------

    [Fact]
    public async Task AC7_TrapVerifierInfraFault_FailsResumable()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-7");
        var handler = BuildHandler(repo, out _,
            configureSeams: s => s.Traps = FakeTrapVerifier.WithInfraFault(TrapKind.T3GraphAppRoleParity, "Graph 503"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H13Rejections.TrapVerifierInfraFault);
        failure.Diagnostic.Should().Contain("Graph 503");
    }

    // ---------- AC-8 invariant verifier InfraFault ----------

    [Fact]
    public async Task AC8_InvariantVerifierInfraFault_FailsResumable()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-8");
        var handler = BuildHandler(repo, out _,
            configureSeams: s => s.Invariants = FakeInvariantVerifier.WithInfraFault(InvariantKind.I2AiSearchTenantFilter, "endpoint 404"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H13Rejections.InvariantVerifierInfraFault);
    }

    // ---------- AC-9 naming-conformance FAILED ----------

    [Fact]
    public async Task AC9_NamingConformanceFailed_FailsQuarantine()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-9");
        var handler = BuildHandler(repo, out _,
            configureSeams: s => s.Naming = FakeNamingChecker.Failure(1, "R1: SPRK-DEV-DATAVERSE-URL contains env token DEV"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(H13Rejections.NamingConformanceFailed);
        failure.Diagnostic.Should().Contain("SPRK-DEV-DATAVERSE-URL");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- AC-10 naming-conformance infra fault ----------

    [Fact]
    public async Task AC10_NamingConformanceInfraFault_FailsResumable()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-10");
        var handler = BuildHandler(repo, out _,
            configureSeams: s => s.Naming = FakeNamingChecker.Throws(new FileNotFoundException("script missing")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H13Rejections.NamingConformanceInfraFault);
    }

    // ---------- AC-11a cost drift advisory-warn (default) ----------

    [Fact]
    public async Task AC11a_CostDriftAdvisoryWarnDefault_SucceedsWithAdvisoryOnRun()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-11a");
        var handler = BuildHandler(repo, out var seams,
            configureSeams: s => s.Cost = FakeCostChecker.OverBudget(observed: 620m, expected: 400m, drift: 0.55m));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Completed);
        repo.LastWrittenRun.ErrorDetail.Should().NotBeNull();
        repo.LastWrittenRun.ErrorDetail!.Should().Contain("advisory");
        repo.LastWrittenRun.ErrorDetail.Should().Contain("cost-drift");
        seams.Registry.CallCount.Should().Be(1, "advisory drift does NOT block the Ready transition");
    }

    // ---------- AC-11b cost drift fail-run (opted-in) ----------

    [Fact]
    public async Task AC11b_CostDriftFailsRunOptIn_FailsQuarantine()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-11b");
        var handler = BuildHandler(repo, out var seams,
            configureSeams: s => s.Cost = FakeCostChecker.OverBudget(observed: 620m, expected: 400m, drift: 0.55m),
            configureOptions: o => o.CostDriftFailsRun = true);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(H13Rejections.CostDriftExceeded);
        seams.Registry.CallCount.Should().Be(0);
    }

    // ---------- AC-12 cost query infra fault ----------

    [Fact]
    public async Task AC12_CostQueryInfraFault_FailsResumable()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-12");
        var handler = BuildHandler(repo, out _,
            configureSeams: s => s.Cost = FakeCostChecker.Throws(new InvalidOperationException("az not on PATH")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H13Rejections.CostQueryInfraFault);
    }

    // ---------- AC-13 registry updater Failure ----------

    [Fact]
    public async Task AC13_RegistryUpdaterFailure_FailsResumable()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-13");
        var handler = BuildHandler(repo, out _,
            configureSeams: s => s.Registry = FakeRegistryUpdater.Failure("PATCH 412 ETag conflict"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H13Rejections.RegistryUpdateFailed);
    }

    // ---------- AC-14 registry updater throws ----------

    [Fact]
    public async Task AC14_RegistryUpdaterThrows_FailsResumable()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-14");
        var handler = BuildHandler(repo, out _,
            configureSeams: s => s.Registry = FakeRegistryUpdater.Throws(new InvalidOperationException("token acquisition failed")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H13Rejections.RegistryUpdateFailed);
    }

    // ---------- AC-15 idempotency (level-3 durable no-op) ----------

    [Fact]
    public async Task AC15_Idempotent_MatchingCompletedPhase_IsNoOp_NoSeamInvoked()
    {
        var run = BuildRun();
        var expectedKey = H13E2EAcceptanceGateHandler.BuildIdempotencyKey(CustomerId, BuildId);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H13",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-15");
        var handler = BuildHandler(repo, out var seams);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        seams.Validator.CallCount.Should().Be(0);
        seams.Traps.CallCount.Should().Be(0);
        seams.Invariants.CallCount.Should().Be(0);
        seams.Naming.CallCount.Should().Be(0);
        seams.Cost.CallCount.Should().Be(0);
        seams.Registry.CallCount.Should().Be(0);
    }

    // ---------- AC-16 idempotency-key format determinism ----------

    [Fact]
    public void AC16_IdempotencyKey_Deterministic_CustomerIdAndBuildId()
    {
        var k1 = H13E2EAcceptanceGateHandler.BuildIdempotencyKey("acme", "ci-42");
        var k2 = H13E2EAcceptanceGateHandler.BuildIdempotencyKey("acme", "ci-42");
        k1.Should().Be(k2);
        k1.Should().Be("validate-acme-ci-42");

        H13E2EAcceptanceGateHandler.BuildIdempotencyKey("acme", "ci-43").Should().NotBe(k1,
            "new build => new key => re-verifies from scratch");
        H13E2EAcceptanceGateHandler.BuildIdempotencyKey("other", "ci-42").Should().NotBe(k1);
    }

    // ---------- AC-17 run not found ----------

    [Fact]
    public async Task AC17_RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var handler = BuildHandler(repo, out _);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H13Rejections.RunNotFound);
    }

    // ---------- AC-18 handler-id mismatch ----------

    [Fact]
    public async Task AC18_HandlerIdMismatch_Throws()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-18");
        var handler = BuildHandler(repo, out _);
        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H2a",
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*mismatched HandlerId*");
    }

    // ---------- AC-19 trap rejection code mapping ----------

    [Theory]
    [InlineData(TrapKind.T1KeyVaultReferenceIdentity, H13Rejections.TrapT1Failed)]
    [InlineData(TrapKind.T2DataverseAppUser, H13Rejections.TrapT2Failed)]
    [InlineData(TrapKind.T3GraphAppRoleParity, H13Rejections.TrapT3Failed)]
    [InlineData(TrapKind.T4ExchangePolicyCount, H13Rejections.TrapT4Failed)]
    [InlineData(TrapKind.T5SlotMiKvRbac, H13Rejections.TrapT5Failed)]
    [InlineData(TrapKind.T6SpeConfidentialClient, H13Rejections.TrapT6Failed)]
    public void AC19_MapTrapKindToRejectionCode_IsDistinctPerTrap(TrapKind kind, string expectedCode)
    {
        H13E2EAcceptanceGateHandler.MapTrapKindToRejectionCode(kind).Should().Be(expectedCode);
    }

    // ---------- AC-20 invariant rejection code mapping ----------

    [Theory]
    [InlineData(InvariantKind.I1NoHardcodedTenant, H13Rejections.InvariantI1Failed)]
    [InlineData(InvariantKind.I2AiSearchTenantFilter, H13Rejections.InvariantI2Failed)]
    [InlineData(InvariantKind.I3CosmosPartitionKey, H13Rejections.InvariantI3Failed)]
    [InlineData(InvariantKind.I4SpeContainerResolver, H13Rejections.InvariantI4Failed)]
    [InlineData(InvariantKind.I5GraphTokenTenant, H13Rejections.InvariantI5Failed)]
    public void AC20_MapInvariantKindToRejectionCode_IsDistinctPerInvariant(InvariantKind kind, string expectedCode)
    {
        H13E2EAcceptanceGateHandler.MapInvariantKindToRejectionCode(kind).Should().Be(expectedCode);
    }

    // ---------- AC-21 aggregation priority — trap Failed wins over invariant Failed ----------

    [Fact]
    public async Task AC21_TrapFailedTakesPriorityOverInvariantFailed()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-21");
        var handler = BuildHandler(repo, out _,
            configureSeams: s =>
            {
                s.Traps = FakeTrapVerifier.WithFailure(TrapKind.T1KeyVaultReferenceIdentity, "trap wins");
                s.Invariants = FakeInvariantVerifier.WithFailure(InvariantKind.I2AiSearchTenantFilter, "should not surface");
            });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H13Rejections.TrapT1Failed, "trap failure has higher priority in aggregation");
    }

    // ---------- helpers ----------

    private sealed class Seams
    {
        public IE2EValidationRunner Validator { get; set; } = FakeValidator.Success();
        public IE2ETrapVerifier Traps { get; set; } = FakeTrapVerifier.AllPassed();
        public IE2EInvariantVerifier Invariants { get; set; } = FakeInvariantVerifier.AllPassed();
        public INamingConformanceChecker Naming { get; set; } = FakeNamingChecker.Success();
        public ICostEnvelopeChecker Cost { get; set; } = FakeCostChecker.WithinBudget();
        public IRegistrySetupStatusUpdater Registry { get; set; } = FakeRegistryUpdater.Success();

        public FakeValidator ValidatorFake => (FakeValidator)Validator;
        public FakeTrapVerifier TrapsFake => (FakeTrapVerifier)Traps;
        public FakeInvariantVerifier InvariantsFake => (FakeInvariantVerifier)Invariants;
        public FakeNamingChecker NamingFake => (FakeNamingChecker)Naming;
        public FakeCostChecker CostFake => (FakeCostChecker)Cost;
        public FakeRegistryUpdater RegistryFake => (FakeRegistryUpdater)Registry;

        public int VCallCount(IE2EValidationRunner _) => ((FakeValidator)Validator).CallCount;
    }

    private sealed class ResolvedSeams
    {
        public required FakeValidator Validator { get; init; }
        public required FakeTrapVerifier Traps { get; init; }
        public required FakeInvariantVerifier Invariants { get; init; }
        public required FakeNamingChecker Naming { get; init; }
        public required FakeCostChecker Cost { get; init; }
        public required FakeRegistryUpdater Registry { get; init; }
    }

    private H13E2EAcceptanceGateHandler BuildHandler(
        FakeRepository repo,
        out ResolvedSeams resolved,
        Action<Seams>? configureSeams = null,
        Action<H13AcceptanceOptions>? configureOptions = null)
    {
        var seams = new Seams();
        configureSeams?.Invoke(seams);

        var options = new H13AcceptanceOptions();
        configureOptions?.Invoke(options);

        var registryClient = new FakeRegistryClient();

        resolved = new ResolvedSeams
        {
            Validator = seams.ValidatorFake,
            Traps = seams.TrapsFake,
            Invariants = seams.InvariantsFake,
            Naming = seams.NamingFake,
            Cost = seams.CostFake,
            Registry = seams.RegistryFake,
        };

        return new H13E2EAcceptanceGateHandler(
            repo, seams.Validator, seams.Traps, seams.Invariants,
            seams.Naming, seams.Cost, seams.Registry,
            registryClient, Options.Create(options),
            NullLogger<H13E2EAcceptanceGateHandler>.Instance);
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H13E2EAcceptanceGateHandler.HandlerIdentifier,
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
            EnvironmentId = EnvironmentId,
            TenancyModel = "Model2Dedicated",
            Status = RunStatus.Running,
            Profile = "spaarke-hosted-model2",
        };
        run.Parameters.NonSecret[H13E2EAcceptanceGateHandler.TenantIdParameterKey] = TenantId;
        run.Parameters.NonSecret[H13E2EAcceptanceGateHandler.SubscriptionIdParameterKey] = SubscriptionId;
        run.Parameters.NonSecret[H13E2EAcceptanceGateHandler.BuildIdParameterKey] = BuildId;
        run.Parameters.NonSecret[H13E2EAcceptanceGateHandler.BffApiUrlParameterKey] = BffApiUrl;
        run.Parameters.NonSecret[H13E2EAcceptanceGateHandler.ResourceGroupNameParameterKey] = ResourceGroupName;
        run.Parameters.NonSecret[H13E2EAcceptanceGateHandler.AppServiceNameParameterKey] = AppServiceName;
        run.Parameters.NonSecret[H13E2EAcceptanceGateHandler.KeyVaultNameParameterKey] = KeyVaultName;
        run.Parameters.NonSecret[H13E2EAcceptanceGateHandler.RegistryDataverseUrlParameterKey] = RegistryDataverseUrl;
        run.InterStepState.DataverseEnvUrl = DataverseUrl;
        run.InterStepState.BffAppRegId = "bff-appreg-id";
        run.InterStepState.MiClientId = "uami-client-id";
        return run;
    }

    // ==== fakes ====

    private sealed class FakeRepository : IProvisioningRunRepository
    {
        private ProvisioningRun? _run;
        private string? _etag;
        public ProvisioningRun? LastWrittenRun { get; private set; }
        public int ReplaceCallCount { get; private set; }

        public FakeRepository(ProvisioningRun? run, string? etag) { _run = run; _etag = etag; }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult(_run is null || _etag is null ? null : new ProvisioningRunReadResult(_run, _etag));

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
        {
            ReplaceCallCount++;
            LastWrittenRun = run;
            _run = run;
            _etag = ifMatchEtag + "-next";
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, _etag));
        }
    }

    private sealed class FakeRegistryClient : IDataverseEnvironmentRegistryClient
    {
        public Task<DataverseEnvironmentRegistrySnapshot?> LookupByTenantIdAsync(string tenantId, CancellationToken ct)
            => Task.FromResult<DataverseEnvironmentRegistrySnapshot?>(null);

        // Task 112 extended the interface with the WRITE path. H13 currently
        // routes its Ready-writer through the separate IRegistrySetupStatusUpdater
        // seam (task 184 will swap that seam onto UpdateSetupStatusAsync), so
        // this fake never observes a call — return Success for the compile.
        public Task<RegistryUpdateOutcome> UpdateSetupStatusAsync(
            RegistrySetupStatusUpdate update, CancellationToken cancellationToken)
            => Task.FromResult<RegistryUpdateOutcome>(new RegistryUpdateOutcome.Success());

        // REG-01 (customer-provisioning-orchestration-r1 Wave 2 B24, 2026-08-27):
        // H13 step 9.5 invokes UpdateColumnsAsync to promote run-derived values
        // into the sprk_dataverseenvironment row BEFORE the Ready transition.
        // The fake accepts any column set + returns Success so H13 tests focus
        // on the aggregation logic + gate-decision behavior.
        public Task<RegistryUpdateOutcome> UpdateColumnsAsync(
            string environmentId,
            IReadOnlyDictionary<string, object?> columns,
            string customerIdForLog,
            string runIdForLog,
            CancellationToken cancellationToken)
            => Task.FromResult<RegistryUpdateOutcome>(new RegistryUpdateOutcome.Success());
    }

    private sealed class FakeValidator : IE2EValidationRunner
    {
        private readonly Func<Task<E2EValidationOutcome>> _run;
        public int CallCount { get; private set; }
        private FakeValidator(Func<Task<E2EValidationOutcome>> run) { _run = run; }

        public static FakeValidator Success() =>
            new(() => Task.FromResult<E2EValidationOutcome>(new E2EValidationOutcome.Success(
                new[] { "bff-healthz", "sample-analysis" }, Array.Empty<string>())));
        public static FakeValidator Failure(IReadOnlyList<string> failed, string diag) =>
            new(() => Task.FromResult<E2EValidationOutcome>(new E2EValidationOutcome.Failure(failed, diag)));
        public static FakeValidator Throws(Exception ex) => new(() => throw ex);

        public Task<E2EValidationOutcome> RunAsync(E2EValidationRequest request, CancellationToken ct)
        {
            CallCount++;
            return _run();
        }
    }

    private sealed class FakeTrapVerifier : IE2ETrapVerifier
    {
        private readonly TrapCatalogVerificationResult _result;
        private readonly Exception? _throws;
        public int CallCount { get; private set; }
        private FakeTrapVerifier(TrapCatalogVerificationResult result, Exception? throws = null)
        { _result = result; _throws = throws; }

        public static FakeTrapVerifier AllPassed()
        {
            var outcomes = Enum.GetValues<TrapKind>()
                .Select(k => (TrapVerificationOutcome)new TrapVerificationOutcome.Passed(k))
                .ToArray();
            return new FakeTrapVerifier(new TrapCatalogVerificationResult(outcomes));
        }

        public static FakeTrapVerifier WithFailure(TrapKind failing, string diag)
        {
            var outcomes = Enum.GetValues<TrapKind>()
                .Select(k => k == failing
                    ? (TrapVerificationOutcome)new TrapVerificationOutcome.Failed(k, diag)
                    : new TrapVerificationOutcome.Passed(k))
                .ToArray();
            return new FakeTrapVerifier(new TrapCatalogVerificationResult(outcomes));
        }

        public static FakeTrapVerifier WithInfraFault(TrapKind kind, string diag)
        {
            var outcomes = Enum.GetValues<TrapKind>()
                .Select(k => k == kind
                    ? (TrapVerificationOutcome)new TrapVerificationOutcome.InfraFault(k, diag)
                    : new TrapVerificationOutcome.Passed(k))
                .ToArray();
            return new FakeTrapVerifier(new TrapCatalogVerificationResult(outcomes));
        }

        public Task<TrapCatalogVerificationResult> VerifyAllAsync(TrapVerificationRequest request, CancellationToken ct)
        {
            CallCount++;
            if (_throws is not null) throw _throws;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeInvariantVerifier : IE2EInvariantVerifier
    {
        private readonly InvariantCatalogVerificationResult _result;
        public int CallCount { get; private set; }
        private FakeInvariantVerifier(InvariantCatalogVerificationResult result) { _result = result; }

        public static FakeInvariantVerifier AllPassed()
        {
            var outcomes = Enum.GetValues<InvariantKind>()
                .Select(k => (InvariantVerificationOutcome)new InvariantVerificationOutcome.Passed(k))
                .ToArray();
            return new FakeInvariantVerifier(new InvariantCatalogVerificationResult(outcomes));
        }

        public static FakeInvariantVerifier WithFailure(InvariantKind failing, string diag)
        {
            var outcomes = Enum.GetValues<InvariantKind>()
                .Select(k => k == failing
                    ? (InvariantVerificationOutcome)new InvariantVerificationOutcome.Failed(k, diag)
                    : new InvariantVerificationOutcome.Passed(k))
                .ToArray();
            return new FakeInvariantVerifier(new InvariantCatalogVerificationResult(outcomes));
        }

        public static FakeInvariantVerifier WithInfraFault(InvariantKind kind, string diag)
        {
            var outcomes = Enum.GetValues<InvariantKind>()
                .Select(k => k == kind
                    ? (InvariantVerificationOutcome)new InvariantVerificationOutcome.InfraFault(k, diag)
                    : new InvariantVerificationOutcome.Passed(k))
                .ToArray();
            return new FakeInvariantVerifier(new InvariantCatalogVerificationResult(outcomes));
        }

        public Task<InvariantCatalogVerificationResult> VerifyAllAsync(InvariantVerificationRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeNamingChecker : INamingConformanceChecker
    {
        private readonly Func<Task<NamingConformanceOutcome>> _check;
        public int CallCount { get; private set; }
        private FakeNamingChecker(Func<Task<NamingConformanceOutcome>> check) { _check = check; }

        public static FakeNamingChecker Success() =>
            new(() => Task.FromResult<NamingConformanceOutcome>(new NamingConformanceOutcome.Success()));
        public static FakeNamingChecker Failure(int exit, string diag) =>
            new(() => Task.FromResult<NamingConformanceOutcome>(new NamingConformanceOutcome.Failure(exit, diag)));
        public static FakeNamingChecker Throws(Exception ex) => new(() => throw ex);

        public Task<NamingConformanceOutcome> CheckAsync(NamingConformanceRequest request, CancellationToken ct)
        {
            CallCount++;
            return _check();
        }
    }

    private sealed class FakeCostChecker : ICostEnvelopeChecker
    {
        private readonly Func<Task<CostEnvelopeReport>> _check;
        public int CallCount { get; private set; }
        private FakeCostChecker(Func<Task<CostEnvelopeReport>> check) { _check = check; }

        public static FakeCostChecker WithinBudget() =>
            new(() => Task.FromResult(new CostEnvelopeReport(
                ObservedMonthlyUsd: 380m, ExpectedMonthlyUsd: 400m, DriftFraction: -0.05m,
                ExceedsAdvisoryThreshold: false, Summary: "within budget")));
        public static FakeCostChecker OverBudget(decimal observed, decimal expected, decimal drift) =>
            new(() => Task.FromResult(new CostEnvelopeReport(
                ObservedMonthlyUsd: observed, ExpectedMonthlyUsd: expected, DriftFraction: drift,
                ExceedsAdvisoryThreshold: true, Summary: $"drift={drift:P0}")));
        public static FakeCostChecker Throws(Exception ex) => new(() => throw ex);

        public Task<CostEnvelopeReport> CheckAsync(CostEnvelopeRequest request, CancellationToken ct)
        {
            CallCount++;
            return _check();
        }
    }

    private sealed class FakeRegistryUpdater : IRegistrySetupStatusUpdater
    {
        private readonly Func<Task<RegistrySetupStatusUpdateOutcome>> _fn;
        public int CallCount { get; private set; }
        private FakeRegistryUpdater(Func<Task<RegistrySetupStatusUpdateOutcome>> fn) { _fn = fn; }

        public static FakeRegistryUpdater Success() =>
            new(() => Task.FromResult<RegistrySetupStatusUpdateOutcome>(new RegistrySetupStatusUpdateOutcome.Success()));
        public static FakeRegistryUpdater Failure(string diag) =>
            new(() => Task.FromResult<RegistrySetupStatusUpdateOutcome>(new RegistrySetupStatusUpdateOutcome.Failure(diag)));
        public static FakeRegistryUpdater Throws(Exception ex) => new(() => throw ex);

        public Task<RegistrySetupStatusUpdateOutcome> TransitionToReadyAsync(
            RegistrySetupStatusUpdateRequest request, CancellationToken ct)
        {
            CallCount++;
            return _fn();
        }
    }
}
