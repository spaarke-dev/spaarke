// -----------------------------------------------------------------------------
// H8SpeContainerHandlerTests.cs
//
// Unit tests over H8SpeContainerHandler (H8-B semantics per task 214, 2026-08-30).
// SUPERSEDES H8SpeContainerTypeHandlerTests.cs (deleted with the old handler).
//
// H8-B RESPONSIBILITY (per topology doc §6):
//   Create ONE SPE container per customer inside a PRE-EXISTING container-type
//   (from spaarke-constants.yaml). Two Graph calls under app-only cert-based
//   credential: POST /containers + POST /containers/{id}/activate. Then verify
//   readability via app-only GET.
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live Graph / KV. Fakes replace the
//   repository + both collaborator seams (provisioner + verifier) so the
//   handler orchestration + §4C rollback classification logic is exercised in
//   isolation. Live-Azure coverage belongs in env-guarded smoke tests + Phase
//   F acceptance runs.
//
// COVERAGE:
//   AC-1  Happy path — provisioner Success, verifier Verified -> Success +
//         CompletedPhase(H8) + InterStepState.SpeContainerId set +
//         T6Verified gate Verified.
//   AC-2  Provisioner CreateFailure -> Resumable + ProvisioningFailed;
//         verifier NEVER called; NO container-id persisted.
//   AC-3  Provisioner ActivateFailure -> QuarantineRequired +
//         ContainerActivationFailed; container-id IS persisted (audit); verifier
//         NEVER called (activation failed, no point verifying).
//   AC-4  Provisioner infra fault (throws) -> Resumable + ProvisioningInfraFault.
//   AC-5  Provisioner outputs incomplete (blank containerId) -> Resumable +
//         ProvisioningOutputsIncomplete.
//   AC-6  Verifier NotVerified (403 or similar) -> QuarantineRequired +
//         ContainerGetVerificationFailed; container-id persisted.
//   AC-7  Verifier infra fault (throws, AFTER creation+activation) ->
//         QuarantineRequired + VerificationInfraFault; container-id persisted.
//   AC-8  Verifier ReplicationPending (24h SPE lag, 404) -> Success +
//         RunStatus.WaitingOnGate + gate Pending + container-id persisted +
//         NO CompletedPhase appended (resume re-runs HandleAsync).
//   AC-9  Idempotency — matching CompletedPhase makes second invocation a
//         durable no-op — NO provisioner/verifier calls.
//   AC-10 Idempotency-key format determinism — spe-{customerId}, customerId-only.
//   AC-11 Missing tenantId (§4D I1/I5) -> Resumable + MissingTenantId.
//   AC-12 Missing containerTypeId (from constants) -> Resumable +
//         MissingContainerTypeId. Fires when operator hasn't completed the
//         topology-setup runbook or SKILL Step 4.0 was bypassed.
//   AC-13 Missing keyVaultName -> Resumable + MissingKeyVaultName.
//   AC-14 Missing owningAppId (H3 not complete — InterStepState.BffAppRegId
//         empty) -> Resumable + MissingOwningAppId.
//   AC-15 Run not found -> Resumable + RunNotFound.
//   AC-16 HandlerId mismatch -> throws InvalidOperationException.
//   AC-17 Provisioner request carries all required inputs (tenant-scoped,
//         never hardcoded; containerTypeId from run parameters, owningAppId
//         from InterStepState).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H8SpeContainerHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h8-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string KeyVaultName = "sprk-acme-prod-kv";
    private const string OwningAppId = "77777777-8888-9999-aaaa-bbbbbbbbbbbb";
    private const string ContainerTypeId = "cccccccc-dddd-eeee-ffff-000000000001";
    private const string ContainerId = "b!aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    // ---------- AC-1 happy path ----------

    [Fact]
    public async Task AC1_HappyPath_CreateActivateVerify_SucceedsAndAdvancesState()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var verifier = FakeVerifier.Verified("active");
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H8SpeContainerHandler.BuildIdempotencyKey(CustomerId));

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H8");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle().Which.Phase.Should().Be("H8");
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().Be(ContainerId,
            "H7 (task 050) reads this field as the source for sprk_SharePointEmbeddedContainerId");
        repo.LastWrittenRun.GateStates.Should().ContainKey(SpeContainerGates.T6Verified);
        repo.LastWrittenRun.GateStates[SpeContainerGates.T6Verified].Status.Should().Be(GateState.Verified);
        repo.LastWrittenRun.GateStates[SpeContainerGates.T6Verified].Evidence!.Value
            .GetProperty("verifiedViaAppOnlyToken").GetBoolean().Should().BeTrue(
                "genuine verification happened on the happy path");
        repo.LastWrittenRun.GateStates[SpeContainerGates.T6Verified].Evidence!.Value
            .GetProperty("containerId").GetString().Should().Be(ContainerId);

        provisioner.CallCount.Should().Be(1);
        verifier.CallCount.Should().Be(1);
    }

    // ---------- AC-2 provisioner CreateFailure ----------

    [Fact]
    public async Task AC2_ProvisionerCreateFailure_FailsResumable_NoVerifierCall_NoContainerIdPersisted()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-2");
        var provisioner = FakeProvisioner.CreateFailure("Graph POST /containers 503 ServiceUnavailable");
        var verifier = FakeVerifier.Verified("active");
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ProvisioningFailed);
        failure.Diagnostic.Should().Contain("ServiceUnavailable");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().BeNullOrEmpty("nothing was created");
        verifier.CallCount.Should().Be(0);
    }

    // ---------- AC-3 provisioner ActivateFailure ----------

    [Fact]
    public async Task AC3_ProvisionerActivateFailure_FailsQuarantineRequired_ContainerIdPersistedForAudit()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-3");
        var provisioner = FakeProvisioner.ActivateFailure(ContainerId,
            "Graph POST /activate 500 InternalServerError — container created but unusable");
        var verifier = FakeVerifier.Verified("active");
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired,
            "container was created but is unusable until activated (topology doc §6)");
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerActivationFailed);
        failure.Diagnostic.Should().Contain("InternalServerError");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().Be(ContainerId,
            "created-but-not-activated container-id is persisted for audit/cleanup");
        verifier.CallCount.Should().Be(0);
    }

    // ---------- AC-4 provisioner infra fault ----------

    [Fact]
    public async Task AC4_ProvisionerThrows_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-4");
        var provisioner = FakeProvisioner.Throws(new TimeoutException("KV cert load timed out"));
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ProvisioningInfraFault);
        failure.Diagnostic.Should().Contain("TimeoutException");
    }

    // ---------- AC-5 provisioner outputs incomplete ----------

    [Fact]
    public async Task AC5_ProvisionerOutputsIncomplete_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-5");
        var provisioner = FakeProvisioner.Success(containerId: "");
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ProvisioningOutputsIncomplete);
    }

    // ---------- AC-6 verifier NotVerified ----------

    [Fact]
    public async Task AC6_VerifierNotVerified_FailsQuarantineRequired_ContainerIdPersisted()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-6");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var verifier = FakeVerifier.NotVerified("GET returned 403 Forbidden — unexpected permission error");
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerGetVerificationFailed);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().Be(ContainerId,
            "container was created + activated; unverifiable ≠ non-existent");
    }

    // ---------- AC-7 verifier infra fault ----------

    [Fact]
    public async Task AC7_VerifierThrows_AfterCreation_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-7");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var verifier = FakeVerifier.Throws(new TimeoutException("verifier Graph GET timed out"));
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired,
            "container WAS created + activated — an unverifiable post-condition is worse than a clean failure");
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.VerificationInfraFault);
        failure.Diagnostic.Should().Contain(ContainerId);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(ContainerId);
    }

    // ---------- AC-8 verifier ReplicationPending -> WaitingOnGate ----------

    [Fact]
    public async Task AC8_VerifierReplicationPending_SucceedsWithRunStatusWaitingOnGate()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-8");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var verifier = FakeVerifier.ReplicationPending(
            "App-only GET returned 404 Not Found — consistent with SPE's up-to-24h container-type " +
            "replication window.");
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        // Success, not Failure — H8 correctly identified + recorded the
        // external wait; this is not an operator-actionable error.
        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H8SpeContainerHandler.BuildIdempotencyKey(CustomerId));

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.WaitingOnGate,
            "DS-4 §2 / this project's CLAUDE.md MUST rules: the 24h SPE replication gate is a " +
            "run-level external blocker, never Resumable/QuarantineRequired");
        repo.LastWrittenRun.CurrentPhase.Should().Be("H8");
        repo.LastWrittenRun.ErrorDetail.Should().BeNull("a replication-pending wait is not an error");

        // Container IS a real, durable side effect — persisted so a later resume
        // does not need to re-derive it.
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().Be(ContainerId);

        // Gate is Pending, NOT Verified — verification genuinely has not happened yet.
        repo.LastWrittenRun.GateStates.Should().ContainKey(SpeContainerGates.T6Verified);
        repo.LastWrittenRun.GateStates[SpeContainerGates.T6Verified].Status.Should().Be(GateState.Pending);
        repo.LastWrittenRun.GateStates[SpeContainerGates.T6Verified].Evidence!.Value
            .GetProperty("verifiedViaAppOnlyToken").GetBoolean().Should().BeFalse(
                "regression guard: evidence must NOT claim verification happened when it has not");

        // NOT recorded as a CompletedPhase — H8 has not finished; a resume
        // must re-execute HandleAsync in full.
        repo.LastWrittenRun.CompletedPhases.Should().BeEmpty();
    }

    // ---------- AC-9 idempotency ----------

    [Fact]
    public async Task AC9_Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRun();
        var expectedKey = H8SpeContainerHandler.BuildIdempotencyKey(CustomerId);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H8",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-9");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var verifier = FakeVerifier.Verified("active");
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        provisioner.CallCount.Should().Be(0);
        verifier.CallCount.Should().Be(0);
    }

    // ---------- AC-10 idempotency-key format determinism ----------

    [Fact]
    public void AC10_IdempotencyKey_IsCustomerIdOnly_VersionIndependent()
    {
        var k1 = H8SpeContainerHandler.BuildIdempotencyKey("acme");
        var k2 = H8SpeContainerHandler.BuildIdempotencyKey("acme");
        k1.Should().Be(k2);
        k1.Should().Be("spe-acme");

        H8SpeContainerHandler.BuildIdempotencyKey("other").Should().NotBe(k1);
    }

    // ---------- AC-11..AC-14 parameter guards ----------

    [Fact]
    public async Task AC11_MissingTenantId_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H8SpeContainerHandler.TenantIdParameterKey);
        var repo = new FakeRepository(run, etag: "etag-11");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.MissingTenantId);
        provisioner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC12_MissingContainerTypeId_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H8SpeContainerHandler.ContainerTypeIdParameterKey);
        var repo = new FakeRepository(run, etag: "etag-12");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.MissingContainerTypeId);
        failure.Diagnostic.Should().Contain("spaarke-constants.yaml",
            "operator diagnostic points at the source that populates this parameter");
        provisioner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC13_MissingKeyVaultName_FailsResumable()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H8SpeContainerHandler.KeyVaultNameParameterKey);
        var repo = new FakeRepository(run, etag: "etag-13");
        var handler = BuildHandler(repo, FakeProvisioner.Success(ContainerId), FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.MissingKeyVaultName);
    }

    [Fact]
    public async Task AC14_MissingOwningAppId_H3NotComplete_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun();
        run.InterStepState.BffAppRegId = null;
        var repo = new FakeRepository(run, etag: "etag-14");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.MissingOwningAppId);
        failure.Diagnostic.Should().Contain("H3");
        provisioner.CallCount.Should().Be(0);
    }

    // ---------- AC-15 run not found ----------

    [Fact]
    public async Task AC15_RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var handler = BuildHandler(repo, FakeProvisioner.Success(ContainerId), FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.RunNotFound);
    }

    // ---------- AC-16 handler-id mismatch ----------

    [Fact]
    public async Task AC16_HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-16");
        var handler = BuildHandler(repo, FakeProvisioner.Success(ContainerId), FakeVerifier.Verified("active"));

        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H3",
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched HandlerId*");
    }

    // ---------- AC-17 provisioner request carries required inputs ----------

    [Fact]
    public async Task AC17_ProvisionerRequest_CarriesTenantScopedInputs_NeverHardcoded()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-17");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        provisioner.LastRequest.Should().NotBeNull();
        provisioner.LastRequest!.CustomerId.Should().Be(CustomerId);
        provisioner.LastRequest.TenantId.Should().Be(TenantId, "§4D I5 tenant-scoped, never default");
        provisioner.LastRequest.ContainerTypeId.Should().Be(ContainerTypeId,
            "sourced from run parameters, not hardcoded");
        provisioner.LastRequest.OwningAppId.Should().Be(OwningAppId,
            "sourced from InterStepState.BffAppRegId (H3 output)");
        provisioner.LastRequest.VaultName.Should().Be(KeyVaultName);
        provisioner.LastRequest.DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    // ---------- helpers ----------

    private static H8SpeContainerHandler BuildHandler(
        IProvisioningRunRepository repo,
        ISpeContainerProvisioner provisioner,
        ISpeContainerVerifier verifier)
    {
        return new H8SpeContainerHandler(
            repo, provisioner, verifier,
            Options.Create(new SpeContainerOptions()),
            NullLogger<H8SpeContainerHandler>.Instance);
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H8SpeContainerHandler.HandlerIdentifier,
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
            TenancyModel = "Model2Dedicated",
            Status = RunStatus.Running,
            Profile = "spaarke-hosted-model2",
        };
        run.Parameters.NonSecret[H8SpeContainerHandler.TenantIdParameterKey] = TenantId;
        run.Parameters.NonSecret[H8SpeContainerHandler.ContainerTypeIdParameterKey] = ContainerTypeId;
        run.Parameters.NonSecret[H8SpeContainerHandler.KeyVaultNameParameterKey] = KeyVaultName;
        run.InterStepState.BffAppRegId = OwningAppId;
        return run;
    }

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

    private sealed class FakeProvisioner : ISpeContainerProvisioner
    {
        private readonly SpeContainerProvisionOutcome? _outcome;
        private readonly Exception? _throwOnCall;
        public int CallCount { get; private set; }
        public SpeContainerProvisionRequest? LastRequest { get; private set; }

        private FakeProvisioner(SpeContainerProvisionOutcome? outcome, Exception? throwOnCall)
        {
            _outcome = outcome;
            _throwOnCall = throwOnCall;
        }

        public static FakeProvisioner Success(string containerId)
            => new(new SpeContainerProvisionOutcome.Success(
                new SpeContainerProvisionOutputs(containerId)), null);

        public static FakeProvisioner CreateFailure(string diagnostic)
            => new(new SpeContainerProvisionOutcome.CreateFailure(diagnostic), null);

        public static FakeProvisioner ActivateFailure(string containerId, string diagnostic)
            => new(new SpeContainerProvisionOutcome.ActivateFailure(containerId, diagnostic), null);

        public static FakeProvisioner Throws(Exception ex) => new(null, ex);

        public Task<SpeContainerProvisionOutcome> ProvisionAsync(
            SpeContainerProvisionRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_outcome!);
        }
    }

    private sealed class FakeVerifier : ISpeContainerVerifier
    {
        private readonly SpeContainerVerificationResult? _result;
        private readonly Exception? _throwOnCall;
        public int CallCount { get; private set; }
        public SpeContainerVerificationRequest? LastRequest { get; private set; }

        private FakeVerifier(SpeContainerVerificationResult? result, Exception? throwOnCall)
        {
            _result = result;
            _throwOnCall = throwOnCall;
        }

        public static FakeVerifier Verified(string status)
            => new(new SpeContainerVerificationResult.Verified(status), null);

        public static FakeVerifier NotVerified(string diagnostic)
            => new(new SpeContainerVerificationResult.NotVerified(diagnostic), null);

        public static FakeVerifier ReplicationPending(string diagnostic)
            => new(new SpeContainerVerificationResult.ReplicationPending(diagnostic), null);

        public static FakeVerifier Throws(Exception ex) => new(null, ex);

        public Task<SpeContainerVerificationResult> VerifyAsync(
            SpeContainerVerificationRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_result!);
        }
    }
}
