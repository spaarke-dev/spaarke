// -----------------------------------------------------------------------------
// H8SpeContainerHandlerTests.cs
//
// Unit tests over H8SpeContainerHandler (H8-B semantics per task 214, 2026-08-30).
// SUPERSEDES H8SpeContainerTypeHandlerTests.cs (deleted with the old handler).
//
// H8-B RESPONSIBILITY (per topology doc §6):
//   Create ONE SPE container per customer inside a PRE-EXISTING container-type
//   (from spaarke-constants.yaml). Two Graph calls, app-only as the owning app
//   (MI-FIC, task 248): POST /containers + POST /containers/{id}/activate. Then verify
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
//   AC-13 No SpeContainerOptions.ContainerTypeOwners entry for the run's
//         containerTypeId -> Resumable + ContainerTypeOwnerNotConfigured
//         (task 245b), nothing created.
//   AC-14 The owning-app credential is the container type's OWNER from L2
//         configuration, never the customer BFF app (InterStepState.BffAppRegId)
//         — task 245b.
//   AC-15 Run not found -> Resumable + RunNotFound.
//   AC-16 HandlerId mismatch -> throws InvalidOperationException.
//   AC-17 Provisioner request carries all required inputs (tenant-scoped,
//         never hardcoded; containerTypeId from run parameters, owning app
//         from SpeContainerOptions.ContainerTypeOwners).
//   AC-18 Task 227b (G9): before creating the container, H8 ensures two
//         container-type grants as the owning app — stamp UAMI application
//         full, BFF app registration delegated full.
//   AC-19 Missing MiClientId / BffAppRegId -> Resumable GrantIdentityMissing,
//         no Graph call at all.
//   AC-20 Refused grant -> Resumable ContainerTypeGrantFailed naming the app;
//         grant infra fault -> Resumable ContainerTypeGrantInfraFault; no
//         container created either way.
//   AC-21 Task 227e: a re-run after ReplicationPending reuses the container
//         the first attempt created (InterStepState.SpeContainerId) — no second.
//   AC-22 A later run (empty InterStepState) finds the customer's container
//         by lookup and reuses it — nothing created.
//   AC-23 Two containers are the customer's -> Resumable
//         DuplicateCustomerContainers naming both; nothing created or chosen.
//   AC-24 Lookup without a verdict / lookup fault -> Resumable; nothing created.
//   AC-25 A reused container still inactive is activated; refusal ->
//         QuarantineRequired.
//   AC-26 Marker refused / faulted -> Resumable; container id kept for resume.
//   AC-27 The container is marked spaarkeCustomerId = customerId after verification.
// The GET-then-PUT/PATCH semantics (a re-run writes nothing) are covered by
// GraphContainerProvisionerGrantTests against the real Graph SDK; the lookup, marker
// and activation calls by GraphContainerProvisionerReuseTests.
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
    private const string OwningAppId = "77777777-8888-9999-aaaa-bbbbbbbbbbbb";
    private const string ContainerTypeId = "cccccccc-dddd-eeee-ffff-000000000001";
    private const string ContainerId = "b!aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string UamiClientId = "55555555-0000-0000-0000-0000000000a1";
    private const string BffAppId = "99999999-0000-0000-0000-00000000bf00";

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
        var provisioner = FakeProvisioner.Throws(new TimeoutException("Graph container create timed out"));
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
        provisioner.Calls.Should().NotContain("marker", "the marker is written after verification (T227e)");
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
        provisioner.GrantCallCount.Should().Be(0, "a completed H8 does not touch the registration again");
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
    public async Task AC13_NoOwnerConfiguredForTheContainerType_FailsResumable_NothingCreated()
    {
        var run = BuildRun();
        run.Parameters.NonSecret[H8SpeContainerHandler.ContainerTypeIdParameterKey] = "dddddddd-0000-0000-0000-000000000009";
        var repo = new FakeRepository(run, etag: "etag-13");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerTypeOwnerNotConfigured);
        failure.Diagnostic.Should().Contain("dddddddd-0000-0000-0000-000000000009");
        provisioner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC14_AuthenticatesAsTheOwningApp_NotTheCustomerBffApp()
    {
        // H8 signs in as the container type's OWNING app (task 248: through the Worker UAMI's federated
        // credential on it); the customer BFF app (H3 output) is a separate identity (topology §3A) — H8
        // must never present it.
        var run = BuildRun();
        run.InterStepState.BffAppRegId = "99999999-0000-0000-0000-00000000bf00";
        var repo = new FakeRepository(run, etag: "etag-14");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var verifier = FakeVerifier.Verified("active");
        var handler = BuildHandler(repo, provisioner, verifier);

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        provisioner.LastRequest!.OwningAppId.Should().Be(OwningAppId);
        provisioner.LastRequest.OwningAppId.Should().NotBe(run.InterStepState.BffAppRegId);
        verifier.LastRequest!.OwningAppId.Should().Be(OwningAppId);
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
            "the container type's owning app, from SpeContainerOptions.ContainerTypeOwners (task 245b)");
        provisioner.LastRequest.DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AC17_ProvisionerRequest_ContainerTypeIdIsTrimmed()
    {
        // The intake id is operator-maintained text (spaarke-constants.yaml); Graph must receive it trimmed.
        var run = BuildRun();
        run.Parameters.NonSecret[H8SpeContainerHandler.ContainerTypeIdParameterKey] = $"  {ContainerTypeId}\n";
        var repo = new FakeRepository(run, etag: "etag-17b");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        provisioner.LastRequest!.ContainerTypeId.Should().Be(ContainerTypeId);
    }

    // ---------- AC-18..AC-20 container-type grants (task 227b) ----------

    [Fact]
    public async Task AC18_EnsuresBothGrantsAsTheOwningApp_BeforeCreatingTheContainer()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-18");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        provisioner.Calls.Should().Equal("grants", "lookup", "provision", "marker");
        var request = provisioner.LastGrantRequest!;
        request.TenantId.Should().Be(TenantId);
        request.ContainerTypeId.Should().Be(ContainerTypeId);
        request.OwningAppId.Should().Be(OwningAppId, "only the owning app may change its container type's registration");
        request.Grants.Should().HaveCount(2);
        request.Grants.Should().ContainSingle(g => g.AppId == UamiClientId).Which.Should().Match<SpeContainerTypeGrant>(g =>
            g.ApplicationPermissions.SequenceEqual(new[] { "full" }) && g.DelegatedPermissions.Count == 0,
            "the BFF's app-only Graph calls run as the stamp UAMI");
        request.Grants.Should().ContainSingle(g => g.AppId == BffAppId).Which.Should().Match<SpeContainerTypeGrant>(g =>
            g.DelegatedPermissions.SequenceEqual(new[] { "full" }) && g.ApplicationPermissions.Count == 0,
            "the BFF's OBO calls run as its own app registration");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AC19_MissingGrantIdentity_FailsResumable_BeforeAnyGraphCall(bool missingUami, bool missingBffApp)
    {
        var run = BuildRun();
        if (missingUami) run.InterStepState.MiClientId = null;
        if (missingBffApp) run.InterStepState.BffAppRegId = " ";
        var repo = new FakeRepository(run, etag: "etag-19");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.GrantIdentityMissing);
        failure.Diagnostic.Should().Contain(missingUami ? "MiClientId" : "BffAppRegId");
        provisioner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AC20_RefusedGrant_FailsResumable_NamingTheApp_NoContainerCreated()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-20");
        var provisioner = FakeProvisioner.Success(ContainerId);
        provisioner.GrantOutcome = new SpeContainerTypeGrantOutcome.Failure(BffAppId, "Graph PUT ... failed with HTTP 403: accessDenied.");
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerTypeGrantFailed);
        failure.Diagnostic.Should().Contain(BffAppId).And.Contain("FileStorageContainerTypeReg.Selected");
        provisioner.Calls.Should().Equal("grants");
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task AC20_GrantInfraFault_FailsResumable_NoContainerCreated()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-20b");
        var provisioner = FakeProvisioner.Success(ContainerId);
        provisioner.GrantThrows = new TimeoutException("Container-type grant GET exceeded 00:01:00.");
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerTypeGrantInfraFault);
        provisioner.Calls.Should().Equal("grants");
    }

    // ---------- AC-21..AC-27 find-before-create + marker (task 227e) ----------

    [Fact]
    public async Task AC21_ReRunAfterReplicationPending_ReusesTheRunsContainer_CreatesNoSecond()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-21");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var verifier = FakeVerifier.Sequence(
            new SpeContainerVerificationResult.ReplicationPending("404 — replicating"),
            new SpeContainerVerificationResult.Verified("active"));
        var handler = BuildHandler(repo, provisioner, verifier);

        var first = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.WaitingOnGate);
        var second = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        first.Should().BeOfType<HandlerResult.Success>();
        second.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(1, "the re-run reuses the container the first attempt created");
        provisioner.Calls.Should().Equal("grants", "lookup", "provision", "grants", "marker");
        verifier.LastRequest!.ContainerId.Should().Be(ContainerId);
        provisioner.LastMarkerRequest!.ContainerId.Should().Be(ContainerId);
        repo.LastWrittenRun.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().Be(ContainerId);
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle(cp => cp.Phase == "H8");
    }

    [Fact]
    public async Task AC22_LaterRun_FindsTheCustomersContainer_ReusesIt_CreatesNothing()
    {
        const string existing = "b!existing-customer-container";
        var run = BuildRun();   // a new run: nothing in InterStepState
        var repo = new FakeRepository(run, etag: "etag-22");
        var provisioner = FakeProvisioner.Success(ContainerId);
        provisioner.LookupOutcome = new SpeContainerLookupOutcome.Found(
            [new SpeContainerMatch(existing, "Spaarke Container - acme", Marker: CustomerId)]);
        var verifier = FakeVerifier.Verified("active");
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(0, "the customer already has a container");
        provisioner.Calls.Should().Equal("grants", "lookup", "marker");
        provisioner.LastLookupRequest.Should().Be(new SpeContainerLookupRequest(
            TenantId, ContainerTypeId, OwningAppId, CustomerId, "Spaarke Container - acme",
            H8SpeContainerHandler.BuildContainerDescription(CustomerId)));
        verifier.LastRequest!.ContainerId.Should().Be(existing);
        provisioner.LastMarkerRequest.Should().Be(new SpeContainerMarkerRequest(TenantId, OwningAppId, existing, CustomerId));
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(existing, "H4b keeps the BFF on the same container");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle(cp => cp.Phase == "H8");
    }

    [Fact]
    public async Task AC23_TwoContainersAreTheCustomers_FailsResumable_NamingBoth_CreatesNothing()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-23");
        var provisioner = FakeProvisioner.Success(ContainerId);
        provisioner.LookupOutcome = new SpeContainerLookupOutcome.Found(
        [
            new SpeContainerMatch("b!first", "Spaarke Container - acme", Marker: CustomerId),
            new SpeContainerMatch("b!second", "Spaarke Container - acme", Marker: null),
        ]);
        var verifier = FakeVerifier.Verified("active");
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.DuplicateCustomerContainers);
        failure.Diagnostic.Should().Contain("b!first").And.Contain("b!second").And.Contain("never picks one");
        provisioner.Calls.Should().Equal("grants", "lookup");
        verifier.CallCount.Should().Be(0);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().BeNullOrEmpty("neither container is chosen");
        repo.LastWrittenRun.CompletedPhases.Should().BeEmpty();
    }

    [Fact]
    public async Task AC24_LookupWithoutAVerdict_FailsResumable_CreatesNothing()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-24");
        var provisioner = FakeProvisioner.Success(ContainerId);
        provisioner.LookupOutcome = new SpeContainerLookupOutcome.Failure("Graph GET /storage/fileStorage/containers failed with HTTP 403.");
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerLookupFailed);
        failure.Diagnostic.Should().Contain("HTTP 403");
        provisioner.CallCount.Should().Be(0, "no verdict is not 'none found'");
    }

    [Fact]
    public async Task AC24_LookupInfraFault_FailsResumable_CreatesNothing()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-24b");
        var provisioner = FakeProvisioner.Success(ContainerId);
        provisioner.LookupThrows = new TimeoutException("The containers listing exceeded 00:01:00.");
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerLookupInfraFault);
        provisioner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC25_ReusedContainerStillInactive_IsActivated_ThenCompletes()
    {
        var run = BuildRun();
        run.InterStepState.SpeContainerId = ContainerId;   // an earlier attempt's activation failed (AC-3)
        var repo = new FakeRepository(run, etag: "etag-25");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("Inactive"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.Calls.Should().Equal("grants", "activate", "marker");
        provisioner.LastActivationRequest.Should().Be(new SpeContainerActivationRequest(TenantId, OwningAppId, ContainerId));
        repo.LastWrittenRun!.GateStates[SpeContainerGates.T6Verified].Evidence!.Value
            .GetProperty("verifiedStatus").GetString().Should().Be("active");
    }

    [Fact]
    public async Task AC25_ReusedContainerActivationRefused_FailsQuarantineRequired()
    {
        var run = BuildRun();
        run.InterStepState.SpeContainerId = ContainerId;
        var repo = new FakeRepository(run, etag: "etag-25b");
        var provisioner = FakeProvisioner.Success(ContainerId);
        provisioner.ActivateOutcome = new SpeContainerProvisionOutcome.ActivateFailure(ContainerId, "activate failed with ODataError 500");
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("inactive"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerActivationFailed);
        provisioner.Calls.Should().NotContain("marker");
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(ContainerId);
    }

    [Fact]
    public async Task AC25_ReusedContainerActivationFaults_FailsQuarantineRequired_KeepsTheContainer()
    {
        var run = BuildRun();
        run.InterStepState.SpeContainerId = ContainerId;
        var repo = new FakeRepository(run, etag: "etag-25c");
        var provisioner = FakeProvisioner.Success(ContainerId);
        provisioner.ActivateThrows = new TimeoutException("Activating container exceeded 00:01:00.");
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("inactive"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired, "its activation status is unknown");
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerActivationInfraFault);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(ContainerId);
    }

    [Fact]
    public async Task AC26_MarkerRefused_FailsResumable_KeepsTheContainerForResume()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-26");
        var provisioner = FakeProvisioner.Success(ContainerId);
        provisioner.MarkerOutcome = new SpeContainerMarkerOutcome.Failure("Graph PATCH .../customProperties failed with HTTP 403.");
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerMarkerFailed);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(ContainerId, "a resume reuses it (AC-21 path)");
        repo.LastWrittenRun.CompletedPhases.Should().BeEmpty();
    }

    [Fact]
    public async Task AC26_MarkerInfraFault_FailsResumable_KeepsTheContainerForResume()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-26b");
        var provisioner = FakeProvisioner.Success(ContainerId);
        provisioner.MarkerThrows = new TimeoutException("The marker write exceeded 00:01:00.");
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerMarkerInfraFault);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(ContainerId);
    }

    [Fact]
    public async Task AC27_NewContainer_IsMarkedForTheCustomer_AfterVerification()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-27");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        provisioner.LastMarkerRequest.Should().Be(new SpeContainerMarkerRequest(TenantId, OwningAppId, ContainerId, CustomerId),
            "the customer's BFF recognises its containers by spaarkeCustomerId = Customer__Id (T227d)");
    }

    // ---------- helpers ----------

    private static H8SpeContainerHandler BuildHandler(
        IProvisioningRunRepository repo,
        ISpeContainerProvisioner provisioner,
        ISpeContainerVerifier verifier)
    {
        return new H8SpeContainerHandler(
            repo, provisioner, verifier,
            Options.Create(OwnerOptions()),
            NullLogger<H8SpeContainerHandler>.Instance);
    }

    /// <summary>One configured owner for the test container type (task 245b).</summary>
    internal static SpeContainerOptions OwnerOptions() => new()
    {
        ContainerTypeOwners =
        [
            new SpeContainerTypeOwner
            {
                ContainerTypeId = ContainerTypeId,
                OwnerAppId = OwningAppId,
            },
        ],
    };

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
            TenancyModel = "Model2",
            Status = RunStatus.Running,
            Profile = "spaarke-hosted-model2",
        };
        run.Parameters.NonSecret[H8SpeContainerHandler.TenantIdParameterKey] = TenantId;
        run.Parameters.NonSecret[H8SpeContainerHandler.ContainerTypeIdParameterKey] = ContainerTypeId;
        run.InterStepState.BffAppRegId = BffAppId;   // customer BFF app — NOT the SPE owner
        run.InterStepState.MiClientId = UamiClientId;   // stamp UAMI (H2a) — granted on the container type (T227b)
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
        public int GrantCallCount { get; private set; }
        public SpeContainerTypeGrantRequest? LastGrantRequest { get; private set; }
        public List<string> Calls { get; } = new();
        public SpeContainerTypeGrantOutcome GrantOutcome { get; set; } = new SpeContainerTypeGrantOutcome.Success([]);
        public Exception? GrantThrows { get; set; }
        public SpeContainerLookupOutcome LookupOutcome { get; set; } = new SpeContainerLookupOutcome.Found([]);
        public Exception? LookupThrows { get; set; }
        public SpeContainerLookupRequest? LastLookupRequest { get; private set; }
        public SpeContainerProvisionOutcome? ActivateOutcome { get; set; }
        public Exception? ActivateThrows { get; set; }
        public SpeContainerActivationRequest? LastActivationRequest { get; private set; }
        public SpeContainerMarkerOutcome MarkerOutcome { get; set; } = new SpeContainerMarkerOutcome.Success(Written: true);
        public Exception? MarkerThrows { get; set; }
        public SpeContainerMarkerRequest? LastMarkerRequest { get; private set; }

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
            Calls.Add("provision");
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_outcome!);
        }

        public Task<SpeContainerTypeGrantOutcome> EnsureGrantsAsync(
            SpeContainerTypeGrantRequest request, CancellationToken ct)
        {
            GrantCallCount++;
            LastGrantRequest = request;
            Calls.Add("grants");
            if (GrantThrows is not null) throw GrantThrows;
            return Task.FromResult(GrantOutcome);
        }

        public Task<SpeContainerLookupOutcome> FindCustomerContainersAsync(
            SpeContainerLookupRequest request, CancellationToken ct)
        {
            LastLookupRequest = request;
            Calls.Add("lookup");
            if (LookupThrows is not null) throw LookupThrows;
            return Task.FromResult(LookupOutcome);
        }

        public Task<SpeContainerProvisionOutcome> ActivateAsync(
            SpeContainerActivationRequest request, CancellationToken ct)
        {
            LastActivationRequest = request;
            Calls.Add("activate");
            if (ActivateThrows is not null) throw ActivateThrows;
            return Task.FromResult(ActivateOutcome ?? new SpeContainerProvisionOutcome.Success(
                new SpeContainerProvisionOutputs(request.ContainerId)));
        }

        public Task<SpeContainerMarkerOutcome> EnsureCustomerMarkerAsync(
            SpeContainerMarkerRequest request, CancellationToken ct)
        {
            LastMarkerRequest = request;
            Calls.Add("marker");
            if (MarkerThrows is not null) throw MarkerThrows;
            return Task.FromResult(MarkerOutcome);
        }
    }

    private sealed class FakeVerifier : ISpeContainerVerifier
    {
        private readonly Queue<SpeContainerVerificationResult> _sequence = new();
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

        /// <summary>Answers each call with the next result — one handler invocation per result.</summary>
        public static FakeVerifier Sequence(params SpeContainerVerificationResult[] results)
        {
            var verifier = new FakeVerifier(null, null);
            foreach (var result in results) verifier._sequence.Enqueue(result);
            return verifier;
        }

        public Task<SpeContainerVerificationResult> VerifyAsync(
            SpeContainerVerificationRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_sequence.Count > 0 ? _sequence.Dequeue() : _result!);
        }
    }
}
