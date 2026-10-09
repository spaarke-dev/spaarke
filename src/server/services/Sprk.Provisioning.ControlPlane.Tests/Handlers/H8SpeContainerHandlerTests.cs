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
//   readability via app-only GET, then BIND the container to its business unit.
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live Graph / KV / Dataverse. Fakes replace the
//   repository + the three collaborator seams (provisioner, verifier, root
//   business-unit reader) so the handler orchestration + §4C rollback
//   classification logic is exercised in isolation. The repository persists
//   through the PRODUCTION Cosmos serializer (ProductionCosmosSerializer), so a
//   field that does not survive Cosmos does not survive here either.
//
// COVERAGE:
//   AC-1..AC-17 (task 214 / 245b — H8-B) — happy path, create/activate/verify
//         failure classes, idempotency, parameter guards, owning-app identity.
//         Adapted for task 165: a container that is not yet bound is RECORDED in
//         the typed InterStepState.SpeContainerCreation, never handed to H7
//         through InterStepState.SpeContainerId.
//   AC-18..AC-23 (unified-access-control-r2 task 165, owner round 35 item 1)
//         Every container is stamped with its owning business unit: H8 binds the
//         verified container to the customer environment's ROOT business unit
//         (H5 output) BEFORE the H7 hand-off; the owner is resolved before
//         anything is created; a bind failure is QuarantineRequired and never
//         hands the container to H7.
//   AC-24..AC-42 (task 165, owner rounds 41 + 49) — H8 RECORDS what it created
//         before verifying it, and a re-entry (after the 24h replication wait, a
//         quarantine, a lost write or a crash) RESUMES with the recorded
//         container: it never creates a second one and never orphans the first
//         unbound. A container POST with no answer is quarantined until an
//         operator has checked.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Sprk.Provisioning.ControlPlane.Tests.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

// Task 227b: G1..G3 — H8 grants the customer's BFF identities on the container-type registration first.
// Task 227e: R1..R12 — a later run reuses the container the environment records (never removed on a failed bind),
// record and environment disagreeing stops the run naming both, and the spaarkeCustomerId marker follows the bind.
public sealed class H8SpeContainerHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h8-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string OwningAppId = "77777777-8888-9999-aaaa-bbbbbbbbbbbb";
    private const string ContainerTypeId = "cccccccc-dddd-eeee-ffff-000000000001";
    private const string ContainerId = "b!aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SecondContainerId = "b!bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DataverseEnvUrl = "https://acme-prod.crm.dynamics.com";
    private const string UamiClientId = "55555555-0000-0000-0000-0000000000a1";
    private const string BffAppId = "99999999-0000-0000-0000-00000000bf00";
    private static readonly Guid RootBusinessUnitId = Guid.Parse("0b0b0b0b-1111-2222-3333-444444444444");

    // ---------- AC-1 happy path ----------

    [Fact]
    public async Task AC1_HappyPath_CreateActivateVerifyBind_SucceedsAndAdvancesState()
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

        // Task 165, round 35 item 1 — the container is bound to the environment's root business unit.
        provisioner.BindRequests.Should().ContainSingle();
        provisioner.LastBindRequest!.ContainerId.Should().Be(ContainerId);
        provisioner.LastBindRequest.BusinessUnitId.Should().Be(RootBusinessUnitId);
        repo.LastWrittenRun.GateStates[SpeContainerGates.T6Verified].Evidence!.Value
            .GetProperty("owningBusinessUnitId").GetString().Should().Be(RootBusinessUnitId.ToString("D"));
        var record = repo.LastWrittenRun.InterStepState.SpeContainerCreation!;
        record.Status.Should().Be(SpeContainerCreationRecord.StatusBound);
        record.OwningBusinessUnitId.Should().Be(RootBusinessUnitId.ToString("D"));
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
        repo.LastWrittenRun.InterStepState.SpeContainerCreation.Should().BeNull("Graph answered — nothing was created");
        verifier.CallCount.Should().Be(0);
    }

    // ---------- AC-3 provisioner ActivateFailure ----------

    [Fact]
    public async Task AC3_ProvisionerActivateFailure_FailsQuarantineRequired_ContainerRecordedNotHandedOff()
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
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().BeNull(
            "SpeContainerId is H7's hand-off — an unbound, unactivated container is never handed off (task 165)");
        var record = repo.LastWrittenRun.InterStepState.SpeContainerCreation!;
        record.RootContainerId.Should().Be(ContainerId, "the created-but-not-activated container is RECORDED for the resume");
        record.Status.Should().Be(SpeContainerCreationRecord.StatusActivationFailed);
        verifier.CallCount.Should().Be(0);
    }

    // ---------- AC-4 provisioner infra fault ----------

    [Fact]
    public async Task AC4_ProvisionerThrows_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-4");
        var provisioner = FakeProvisioner.Throws(new TimeoutException("owner-app token exchange timed out"));
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
    public async Task AC6_VerifierNotVerified_FailsQuarantineRequired_ContainerRecorded()
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
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().BeNull("an unbound container is never handed to H7");
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.RootContainerId.Should().Be(ContainerId,
            "container was created + activated; unverifiable ≠ non-existent — it stays on record");
        provisioner.BindRequests.Should().BeEmpty();
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
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().BeNull();
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.RootContainerId.Should().Be(ContainerId);
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

        // The container IS a real, durable side effect — RECORDED (typed) so a later resume continues with it, but NOT
        // handed to H7: it is still UNBOUND (task 165, owner round 41 item 1).
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().BeNull(
            "SpeContainerId is H7's hand-off — an unbound container is never handed off");
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.RootContainerId.Should().Be(ContainerId);
        repo.LastWrittenRun.InterStepState.SpeContainerCreation.Status.Should().Be(SpeContainerCreationRecord.StatusReplicationPending);

        // Gate is Pending, NOT Verified — verification genuinely has not happened yet.
        repo.LastWrittenRun.GateStates.Should().ContainKey(SpeContainerGates.T6Verified);
        repo.LastWrittenRun.GateStates[SpeContainerGates.T6Verified].Status.Should().Be(GateState.Pending);
        repo.LastWrittenRun.GateStates[SpeContainerGates.T6Verified].Evidence!.Value
            .GetProperty("verifiedViaAppOnlyToken").GetBoolean().Should().BeFalse(
                "regression guard: evidence must NOT claim verification happened when it has not");

        // NOT recorded as a CompletedPhase — H8 has not finished.
        repo.LastWrittenRun.CompletedPhases.Should().BeEmpty();
        provisioner.BindRequests.Should().BeEmpty(
            "the container is bound only once it is verified addressable — binding it during the 24h replication " +
            "window would delete a healthy container");
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
        run.InterStepState.DataverseEnvUrl = null; // a run that completed H8 before H8 needed it — still a no-op
        var repo = new FakeRepository(run, etag: "etag-9");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var verifier = FakeVerifier.Verified("active");
        var reader = FakeRootBusinessUnitReader.Returns(RootBusinessUnitId);
        var handler = BuildHandler(repo, provisioner, verifier, reader);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        provisioner.CallCount.Should().Be(0);
        verifier.CallCount.Should().Be(0);
        reader.CallCount.Should().Be(0);
        provisioner.BindRequests.Should().BeEmpty();
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
        provisioner.LastBindRequest!.OwningAppId.Should().Be(OwningAppId, "the identity that created the container binds it");
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
        provisioner.LastRequest.Description.Should().Contain(CustomerId).And.Contain(RunId,
            "an operator checking a creation in doubt finds the container by its run (task 165) — the type is shared");
    }

    [Fact]
    public async Task AC17_ProvisionerRequest_ContainerTypeIdIsTrimmed_LikeH4sVaultWrite()
    {
        // T226: H4 writes the trimmed id to SPE-ContainerTypeId; Graph must receive the same id.
        var run = BuildRun();
        run.Parameters.NonSecret[H8SpeContainerHandler.ContainerTypeIdParameterKey] = $"  {ContainerTypeId}\n";
        var repo = new FakeRepository(run, etag: "etag-17b");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        provisioner.LastRequest!.ContainerTypeId.Should().Be(ContainerTypeId);
    }

    // ---------- AC-18..AC-23 business-unit stamp (task 165, owner round 35 item 1) ----------

    [Fact]
    public async Task AC18_TheContainer_IsBoundToTheEnvironmentsRootBusinessUnit_AfterVerification_BeforeTheHandOff()
    {
        var run = BuildRun();
        var order = new List<string>();
        var repo = new FakeRepository(run, etag: "etag-18", order: order);
        var provisioner = FakeProvisioner.Success(ContainerId, order);
        var verifier = FakeVerifier.Verified("active", order);
        var reader = FakeRootBusinessUnitReader.Returns(RootBusinessUnitId);
        var handler = BuildHandler(repo, provisioner, verifier, reader);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        order.Should().Equal(new[] { "provision", "write", "verify", "bind", "write" },
            "create, RECORD, verify, bind, then the completion write that hands the container to H7");
        reader.LastEnvironmentUrl.Should().Be(DataverseEnvUrl);
        reader.LastTenantId.Should().Be(TenantId, "§4D I5 — the customer's tenant, never a default");
        var bind = provisioner.LastBindRequest!;
        bind.ContainerId.Should().Be(ContainerId);
        bind.BusinessUnitId.Should().Be(RootBusinessUnitId);
        bind.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public async Task AC19_MissingDataverseEnvUrl_FailsResumable_BeforeAnythingIsCreated()
    {
        var run = BuildRun();
        run.InterStepState.DataverseEnvUrl = null;
        var repo = new FakeRepository(run, etag: "etag-19");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var reader = FakeRootBusinessUnitReader.Returns(RootBusinessUnitId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), reader);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.MissingDataverseEnvUrl);
        provisioner.CallCount.Should().Be(0, "no container may be created whose owner is not established");
        reader.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC20_AnEnvironmentWithNoRootBusinessUnit_FailsResumable_AndCreatesNothing()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-20");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeRootBusinessUnitReader.Returns(null));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.RootBusinessUnitUnresolved);
        provisioner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC21_ARootBusinessUnitReadFault_FailsResumable_AndCreatesNothing()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-21");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"),
            FakeRootBusinessUnitReader.Throws(new HttpRequestException("Dataverse 503")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.RootBusinessUnitUnresolved);
        failure.Diagnostic.Should().Contain("Dataverse 503");
        provisioner.CallCount.Should().Be(0, "fail closed: never create a container with an unread owner");
    }

    [Theory]
    [InlineData(true, SpeContainerRejectionCodes.ContainerBindingFailed)]
    [InlineData(false, SpeContainerRejectionCodes.ContainerBindingFailedNotRemoved)]
    public async Task AC22_ABindFailure_IsQuarantined_AndTheContainerIsNeverHandedToH7(bool removed, string expectedCode)
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-22");
        var provisioner = FakeProvisioner.Success(ContainerId,
            bindOutcome: new SpeContainerBindOutcome.NotBound("the stamp did not read back", removed));
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(expectedCode);
        failure.Diagnostic.Should().Contain("did not read back");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().BeNull(
            "an unbound (or removed) container must never become H7's sprk_SharePointEmbeddedContainerId");
        repo.LastWrittenRun.CompletedPhases.Should().BeEmpty();
        H8SpeContainerHandler.ReadRecordedCreation(repo.LastWrittenRun).RootContainerId.Should().Be(removed ? null : ContainerId,
            removed
                ? "a removed container is dropped from the record — a resume must not verify a deleted container"
                : "an unbound container that could not be removed stays on record — a resume binds it");
    }

    [Fact]
    public async Task AC23_ABindInfraFault_IsQuarantined_AndTheContainerIsNeverHandedToH7()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-23");
        var provisioner = FakeProvisioner.Success(ContainerId, bindThrows: new InvalidOperationException("token exchange failed"));
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerBindingInfraFault);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().BeNull();
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.RootContainerId.Should().Be(ContainerId);
    }

    // ---------- AC-24..AC-42 resume with what was created (task 165, owner rounds 41 + 49) ----------

    [Fact]
    public async Task AC24_AReplicationPendingWait_ThenResume_CreatesNothing_AndBindsAndHandsOffTheSameContainer()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-24");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var verifier = FakeVerifier.Sequence(
            new SpeContainerVerificationResult.ReplicationPending("404 — the 24h replication window"),
            new SpeContainerVerificationResult.Verified("active"));
        var handler = BuildHandler(repo, provisioner, verifier);

        (await handler.HandleAsync(BuildEnvelope(), CancellationToken.None)).Should().BeOfType<HandlerResult.Success>();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.WaitingOnGate);
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().BeNull("the container is not bound yet");

        // The reconciler re-dispatches H8 once the processed marker expires (~24h).
        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(1,
            "the resume must NOT create a second container (the first would be orphaned UNBOUND)");
        verifier.CallCount.Should().Be(2);
        verifier.LastRequest!.ContainerId.Should().Be(ContainerId, "the resume verifies the container it recorded");
        provisioner.BindRequests.Should().ContainSingle().Which.ContainerId.Should().Be(ContainerId);
        repo.LastWrittenRun!.CompletedPhases.Should().ContainSingle(cp => cp.Phase == "H8");
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().Be(ContainerId,
            "only now — bound — is the container handed to H7");
        repo.LastWrittenRun.GateStates[SpeContainerGates.T6Verified].Status.Should().Be(GateState.Verified);
    }

    [Fact]
    public async Task AC25_AResumeDuringTheWait_CreatesNothing_AndKeepsWaiting()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-25");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.ReplicationPending("404 — still replicating"));

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);
        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(1);
        provisioner.BindRequests.Should().BeEmpty("an unaddressable container is not bound (D19)");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.WaitingOnGate);
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.RootContainerId.Should().Be(ContainerId);
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().BeNull();
    }

    [Fact]
    public async Task AC26_TheCreationIsRecordedBeforeVerification_SoAQuarantinedVerifyResumesWithTheSameContainer()
    {
        var run = BuildRun();
        var order = new List<string>();
        var repo = new FakeRepository(run, etag: "etag-26", order: order);
        var provisioner = FakeProvisioner.Success(ContainerId, order);
        var verifier = FakeVerifier.SequenceInOrder(order,
            new TimeoutException("verify timed out"),
            new SpeContainerVerificationResult.Verified("active"));
        var handler = BuildHandler(repo, provisioner, verifier);

        var first = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);
        first.Should().BeOfType<HandlerResult.Failure>().Which.Class.Should().Be(FailureClass.QuarantineRequired);
        order.Take(3).Should().Equal(new[] { "provision", "write", "verify" },
            "the creation is persisted to the run BEFORE anything that can fail, wait or crash");

        // The operator clears the quarantine and resumes.
        var resumed = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        resumed.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(1);
        provisioner.LastBindRequest!.ContainerId.Should().Be(ContainerId);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(ContainerId);
    }

    [Fact]
    public async Task AC27_AFailedBindThatRemovedTheContainer_ResumesWithExactlyOneNewContainer()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-27");
        var provisioner = FakeProvisioner.Sequence(
            new SpeContainerProvisionOutcome[]
            {
                new SpeContainerProvisionOutcome.Success(new SpeContainerProvisionOutputs(ContainerId)),
                new SpeContainerProvisionOutcome.Success(new SpeContainerProvisionOutputs(SecondContainerId)),
            },
            new SpeContainerBindOutcome[]
            {
                new SpeContainerBindOutcome.NotBound("the stamp did not read back — the container was removed", Removed: true),
                new SpeContainerBindOutcome.Bound(),
            });
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var first = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);
        first.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode
            .Should().Be(SpeContainerRejectionCodes.ContainerBindingFailed);
        H8SpeContainerHandler.ReadRecordedCreation(repo.LastWrittenRun!).RootContainerId
            .Should().BeNull("the removed container must not be verified again — a 404 reads as the replication wait");

        var resumed = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        resumed.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(2, "the removed container is replaced by exactly one new one");
        provisioner.LastBindRequest!.ContainerId.Should().Be(SecondContainerId);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(SecondContainerId);
    }

    [Fact]
    public async Task AC28_AnActivationFailure_ResumesByActivatingTheSameContainer_NeverASecondCreate()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-28");
        var provisioner = FakeProvisioner.ActivateFailure(ContainerId, "Graph POST /activate 503");
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var first = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);
        first.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode
            .Should().Be(SpeContainerRejectionCodes.ContainerActivationFailed);

        // The operator clears the quarantine and resumes.
        var resumed = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        resumed.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(1, "the recorded container is activated again — never a second create");
        provisioner.ActivationRequests.Should().ContainSingle().Which.ContainerId.Should().Be(ContainerId);
        provisioner.LastBindRequest!.ContainerId.Should().Be(ContainerId);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(ContainerId);
    }

    [Fact]
    public async Task AC29_ARunLeftByAnOlderH8sPendingPath_ResumesWithItsContainer_AndWithdrawsTheUnboundHandOff()
    {
        // An H8 before task 165 wrote the UNBOUND container into SpeContainerId (the defect) on its replication-pending
        // path, with the T6 gate Pending. That field is such a run's only record of the container.
        var run = BuildRun();
        run.Status = RunStatus.WaitingOnGate;
        run.InterStepState.SpeContainerId = ContainerId;
        run.GateStates[SpeContainerGates.T6Verified] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = "H8",
            Evidence = System.Text.Json.JsonSerializer.SerializeToElement(new { valueKind = 1 }),
        };
        var repo = new FakeRepository(run, etag: "etag-29");
        var provisioner = FakeProvisioner.Success(SecondContainerId);
        var verifier = FakeVerifier.NotVerified("GET returned 403 Forbidden");
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.Class.Should().Be(FailureClass.QuarantineRequired);
        provisioner.CallCount.Should().Be(0, "the run already holds a container");
        provisioner.ActivationRequests.Should().BeEmpty("the older H8 activated it before it waited");
        verifier.LastRequest!.ContainerId.Should().Be(ContainerId);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().BeNull(
            "the unbound hand-off is withdrawn — even when this entry stops short of completing");
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.RootContainerId.Should().Be(ContainerId,
            "the container is MOVED into the typed record, so the next resume still finds it");
    }

    [Fact]
    public async Task AC30_AnOlderH8sActivationQuarantine_IsResumedByActivatingItsContainer()
    {
        // The older H8 wrote the created-but-not-activated container into SpeContainerId and quarantined with its
        // activation gate — the resume must activate THAT container, not verify an inactive one or create another.
        var run = BuildRun();
        run.InterStepState.SpeContainerId = ContainerId;
        run.GateStates[$"h8-{SpeContainerRejectionCodes.ContainerActivationFailed}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = "H8",
        };
        var repo = new FakeRepository(run, etag: "etag-30");
        var provisioner = FakeProvisioner.Success(SecondContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(0);
        provisioner.ActivationRequests.Should().ContainSingle().Which.ContainerId.Should().Be(ContainerId);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(ContainerId);
    }

    [Fact]
    public async Task AC31_AnUnboundHandOff_IsWithdrawn_EvenWhenTheEntryStopsBeforeReadingItsRecord()
    {
        // The older pending path left SpeContainerId set; this entry fails reading the root business unit (5b) — its
        // failure write must not carry the unbound hand-off forward.
        var run = BuildRun();
        run.InterStepState.SpeContainerId = ContainerId;
        var repo = new FakeRepository(run, etag: "etag-31");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"),
            FakeRootBusinessUnitReader.Throws(new HttpRequestException("Dataverse 503")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode
            .Should().Be(SpeContainerRejectionCodes.RootBusinessUnitUnresolved);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().BeNull();
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.RootContainerId.Should().Be(ContainerId,
            "the withdrawn hand-off is the run's only record of its container — it must survive this write");
        provisioner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC32_TheCreationRecord_IsMergedOverAConcurrentWrite_AndSurvives()
    {
        var run = BuildRun();
        var concurrent = BuildRun();
        concurrent.GateStates["operator-note"] = new GateEntry { Status = GateState.Verified, VerifierHandler = "operator" };
        var repo = new FakeRepository(run, etag: "etag-32",
            scripted: (call, _) => call == 1
                ? new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(concurrent, "etag-32-operator"))
                : null);
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        repo.ETagsUsed[1].Should().Be("etag-32-operator", "the record is retried over the CURRENT document");
        repo.LastWrittenRun!.GateStates.Should().ContainKey("operator-note", "the concurrent write is kept");
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().Be(ContainerId);
        provisioner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task AC33_ARecordNeverOverwritesAnotherCreationsRecord_TheNewOneIsQuarantinedInstead()
    {
        // A concurrent write that recorded a DIFFERENT container: merging over it would orphan THAT creation.
        var run = BuildRun();
        var concurrent = BuildRun();
        concurrent.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord { RootContainerId = SecondContainerId };
        var repo = new FakeRepository(run, etag: "etag-33",
            scripted: (call, _) => call == 1
                ? new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(concurrent, "etag-33-other"))
                : null);
        var provisioner = FakeProvisioner.Success(ContainerId);
        var verifier = FakeVerifier.Verified("active");
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.CreationRecordNotPersisted);
        failure.Diagnostic.Should().Contain(SecondContainerId).And.Contain(ContainerId);
        repo.ETagsUsed.Should().ContainSingle("the other creation's record is never written over");
        verifier.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC34_ACreationThatCannotBeRecorded_IsQuarantined_NamingTheContainer_AndGoesNoFurther()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-34",
            scripted: (call, _) => call == 1 ? new ReplaceRunResult.NotFound() : null);
        var provisioner = FakeProvisioner.Success(ContainerId);
        var verifier = FakeVerifier.Verified("active");
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.CreationRecordNotPersisted);
        failure.Diagnostic.Should().Contain(ContainerId).And.Contain("-Bind");
        verifier.CallCount.Should().Be(0);
        provisioner.BindRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AC35_TheCreationRecord_IsTypedInWhatCosmosStores_NotInGateEvidence()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-35");
        var handler = BuildHandler(repo, FakeProvisioner.Success(ContainerId),
            FakeVerifier.ReplicationPending("404 — the 24h replication window"));

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        using var stored = System.Text.Json.JsonDocument.Parse(repo.StoredJson!);
        var interStep = stored.RootElement.GetProperty("interStepState");
        interStep.GetProperty("speContainerCreation").GetProperty("rootContainerId").GetString().Should().Be(ContainerId,
            "the record is a typed field — the production serializer writes it as itself");
        (interStep.TryGetProperty("speContainerId", out var handOff) && handOff.ValueKind != System.Text.Json.JsonValueKind.Null)
            .Should().BeFalse("nothing unbound is stored as H7's hand-off");
        stored.RootElement.GetProperty("gateStates").GetProperty(SpeContainerGates.T6Verified).GetProperty("evidence")
            .TryGetProperty("valueKind", out _).Should().BeFalse("gate evidence is no longer lost as {\"valueKind\":1}");
    }

    [Fact]
    public async Task AC36_AContainerPostWithNoAnswer_IsQuarantined_AndNoContainerIsCreatedUntilAnOperatorClearsIt()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-36");
        var provisioner = FakeProvisioner.Sequence(new SpeContainerProvisionOutcome[]
        {
            new SpeContainerProvisionOutcome.CreateFailure("Graph POST got no answer: TaskCanceledException", ContainerInDoubt: true),
            new SpeContainerProvisionOutcome.Success(new SpeContainerProvisionOutputs(ContainerId)),
        });
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var first = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = first.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerCreationInDoubt);
        failure.Diagnostic.Should().Contain(ContainerTypeId).And.Contain(OwningAppId).And.Contain(RunId).And.Contain("clear-quarantine");
        var record = repo.LastWrittenRun!.InterStepState.SpeContainerCreation!;
        record.RootContainerInDoubtSince.Should().NotBeNull("the container may exist — the resume must not create blindly");
        record.Status.Should().Be(SpeContainerCreationRecord.StatusRootContainerInDoubt);

        // A re-entry before any operator acted (a re-delivered dispatch) creates nothing.
        var again = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);
        again.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode
            .Should().Be(SpeContainerRejectionCodes.ContainerCreationInDoubt);
        provisioner.CallCount.Should().Be(1, "a container may exist unnamed — H8 never creates a second one blindly");

        // The operator checks (no container exists), clears the quarantine and resumes.
        repo.Mutate(r =>
        {
            r.Quarantine!.State = QuarantineState.Cleared;
            r.Quarantine.ClearedAt = DateTimeOffset.UtcNow;
            r.Quarantine.ClearedBy = "operator-oid";
            r.Status = RunStatus.Failed;
        });
        var resumed = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        resumed.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(2);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(ContainerId);
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.RootContainerInDoubtSince.Should().BeNull();
    }

    [Fact]
    public async Task AC37_AContainerInDoubt_ThatTheOperatorFoundAndRecorded_IsActivatedAndBound_NotCreated()
    {
        var run = BuildRun();
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord
        {
            RootContainerId = ContainerId, // the operator found the container and recorded it
            RootContainerInDoubtSince = DateTimeOffset.UtcNow.AddHours(-1),
            Status = SpeContainerCreationRecord.StatusRootContainerInDoubt,
        };
        var repo = new FakeRepository(run, etag: "etag-37");
        var provisioner = FakeProvisioner.Success(SecondContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(0, "the found container is used, never a second one");
        provisioner.ActivationRequests.Should().ContainSingle("its activation was never sent").Which.ContainerId
            .Should().Be(ContainerId);
        provisioner.LastBindRequest!.ContainerId.Should().Be(ContainerId);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(ContainerId);
    }

    [Fact]
    public async Task AC38_AnH8QuarantineClearedBeforeTheCreationWentInDoubt_IsNotTheOperatorsCheck_NothingIsCreated()
    {
        // Owner round 57 item 3 (VL7): only a clearance AT OR AFTER the moment the creation went in doubt is the
        // operator's acknowledgement.
        var run = BuildRun();
        var inDoubtSince = DateTimeOffset.UtcNow.AddHours(-1);
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord
        {
            RootContainerInDoubtSince = inDoubtSince,
            Status = SpeContainerCreationRecord.StatusRootContainerInDoubt,
        };
        run.Quarantine = new QuarantineInfo
        {
            State = QuarantineState.Cleared,
            Reason = "an earlier H8 quarantine",
            QuarantinedByHandler = H8SpeContainerHandler.HandlerIdentifier,
            QuarantinedAt = inDoubtSince.AddHours(-2),
            ClearedAt = inDoubtSince.AddHours(-1),
            ClearedBy = "operator-oid",
        };
        var repo = new FakeRepository(run, etag: "etag-38");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerCreationInDoubt);
        provisioner.CallCount.Should().Be(0, "a clearance older than the doubt is not the operator's check");
        repo.LastWrittenRun!.InterStepState.SpeContainerCreation!.RootContainerInDoubtSince.Should().NotBeNull();
    }

    [Fact]
    public async Task AC39_AnOlderHandOff_NextToADifferentTypedRoot_IsBoundToo_NeverOrphaned()
    {
        const string legacy = "b!llllllllllllllllllllllllllllllllllllllllllllllllllllllllllllll";
        var run = BuildRun();
        run.InterStepState.SpeContainerId = legacy;
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord
        {
            RootContainerId = ContainerId,
            Status = SpeContainerCreationRecord.StatusCreated,
        };
        var repo = new FakeRepository(run, etag: "etag-39");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(0);
        provisioner.BindRequests.Select(b => b.ContainerId).Should().Equal(ContainerId, legacy);
        provisioner.BindRequests.Should().OnlyContain(b => b.BusinessUnitId == RootBusinessUnitId);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(ContainerId);
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.AdditionalContainerIds.Should().BeNull();
    }

    [Fact]
    public async Task AC40_AFurtherContainerThatCanBeNeitherBoundNorRemoved_StaysOnRecord_QuarantinesTheRun_AndIsBoundOnResume()
    {
        const string extra = "b!eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        var run = BuildRun();
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord
        {
            RootContainerId = ContainerId,
            AdditionalContainerIds = new List<string> { extra },
            Status = SpeContainerCreationRecord.StatusCreated,
        };
        var repo = new FakeRepository(run, etag: "etag-40");
        var provisioner = FakeProvisioner.Sequence(
            new SpeContainerProvisionOutcome[] { },
            new SpeContainerBindOutcome[]
            {
                new SpeContainerBindOutcome.Bound(),
                new SpeContainerBindOutcome.NotBound("the stamp did not read back; removing it also failed", Removed: false),
                new SpeContainerBindOutcome.Bound(),
                new SpeContainerBindOutcome.Bound(),
            });
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var first = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = first.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerBindingFailedNotRemoved);
        failure.Diagnostic.Should().Contain(extra);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().BeNull();
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.AdditionalContainerIds.Should().Equal(extra);

        var resumed = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        resumed.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(0, "the root is recorded — nothing is created");
        provisioner.BindRequests.Select(b => b.ContainerId).Should().Equal(ContainerId, extra, ContainerId, extra);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(ContainerId);
    }

    [Fact]
    public async Task AC41_ARemovedContainer_IsDroppedFromTheRecord_EvenWhenTheRemovalWriteMeetsAConcurrentWrite()
    {
        var run = BuildRun();
        var concurrent = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-41",
            scripted: (call, _) =>
            {
                if (call != 2)
                {
                    return null;
                }

                // The operator's concurrent write lands on the document that ALREADY records the container.
                concurrent.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord { RootContainerId = ContainerId };
                concurrent.GateStates["operator-note"] = new GateEntry { Status = GateState.Verified, VerifierHandler = "operator" };
                return new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(concurrent, "etag-41-operator"));
            });
        var provisioner = FakeProvisioner.Success(ContainerId,
            bindOutcome: new SpeContainerBindOutcome.NotBound("the stamp did not read back — removed", Removed: true));
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode
            .Should().Be(SpeContainerRejectionCodes.ContainerBindingFailed);
        H8SpeContainerHandler.ReadRecordedCreation(repo.LastWrittenRun!).RootContainerId.Should().BeNull(
            "the removed container is dropped from the record, merged over the concurrent write");
        repo.LastWrittenRun!.GateStates.Should().ContainKey("operator-note");
    }

    [Fact]
    public async Task AC42_AnInDoubtRecordThatCannotBePersisted_IsQuarantined_SayingAContainerMayExist()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-42",
            scripted: (call, _) => call == 1 ? new ReplaceRunResult.NotFound() : null);
        var provisioner = FakeProvisioner.Sequence(new SpeContainerProvisionOutcome[]
        {
            new SpeContainerProvisionOutcome.CreateFailure("Graph POST got no answer", ContainerInDoubt: true),
        });
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.CreationRecordNotPersisted);
        failure.Diagnostic.Should().Contain("may have created a container it cannot name");
    }

    // ---------- G1..G3 container-type grants (task 227b) ----------

    [Fact]
    public async Task G1_EnsuresBothGrantsAsTheOwningApp_BeforeCreatingTheContainer()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-18");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        provisioner.Calls.Should().StartWith(new[] { "grants", "provision" });
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
    public async Task G2_MissingGrantIdentity_FailsResumable_BeforeAnyGraphCall(bool missingUami, bool missingBffApp)
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
    public async Task G3_RefusedGrant_FailsResumable_NamingTheApp_NoContainerCreated()
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
    public async Task G3_GrantInfraFault_FailsResumable_NoContainerCreated()
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

    // ---------- R1..R7 one root container per customer, ever + the ownership marker (task 227e) ----------

    [Fact]
    public async Task R1_LaterRun_ReusesTheContainerTheEnvironmentRecords_CreatesNothing_NeverRemovesIt()
    {
        var run = BuildRun();   // a new run: empty creation record
        var repo = new FakeRepository(run, etag: "etag-r1");
        var provisioner = FakeProvisioner.Success(SecondContainerId);
        var reader = FakeRootBusinessUnitReader.Returns(RootBusinessUnitId, recordedContainerId: ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), reader);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(0, "the customer already has a container — H4b must keep the BFF on it");
        provisioner.LastBindRequest!.ContainerId.Should().Be(ContainerId);
        provisioner.LastBindRequest.RemoveIfNotBound.Should().BeFalse("the customer's existing container is never removed");
        provisioner.LastMarkerRequest.Should().Be(new SpeContainerMarkerRequest(TenantId, OwningAppId, ContainerId, CustomerId));
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(ContainerId);
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle(cp => cp.Phase == "H8");
        reader.LastRecordedEnvironmentUrl.Should().Be(DataverseEnvUrl);
    }

    [Fact]
    public async Task R2_RecordAndEnvironmentNameDifferentContainers_FailsResumable_NamingBoth_CreatesNothing()
    {
        var run = BuildRun();
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord
        {
            RootContainerId = SecondContainerId,
            Status = SpeContainerCreationRecord.StatusReplicationPending,
        };
        var repo = new FakeRepository(run, etag: "etag-r2");
        var provisioner = FakeProvisioner.Success(SecondContainerId);
        var verifier = FakeVerifier.Verified("active");
        var handler = BuildHandler(repo, provisioner, verifier,
            FakeRootBusinessUnitReader.Returns(RootBusinessUnitId, recordedContainerId: ContainerId));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.DuplicateCustomerContainers);
        failure.Diagnostic.Should().Contain(ContainerId).And.Contain(SecondContainerId).And.Contain("never picks one");
        provisioner.CallCount.Should().Be(0);
        provisioner.BindRequests.Should().BeEmpty();
        verifier.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task R3_RecordedContainerUnreadable_FailsResumable_CreatesNothing()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-r3");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"),
            FakeRootBusinessUnitReader.Returns(RootBusinessUnitId,
                recordedThrows: new InvalidOperationException("GET environmentvariabledefinitions failed: 403 Forbidden.")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.RecordedContainerUnreadable);
        failure.Diagnostic.Should().Contain("403");
        provisioner.CallCount.Should().Be(0, "no verdict on whether the customer has a container is not 'none'");
    }

    [Fact]
    public async Task R4_ReEntryOfAnAdoptingRun_StillNeverRemovesTheContainer()
    {
        var run = BuildRun();
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord
        {
            RootContainerId = ContainerId,
            Status = SpeContainerCreationRecord.StatusReplicationPending,
            AdoptedRootContainerId = ContainerId,
        };
        var repo = new FakeRepository(run, etag: "etag-r4");
        var provisioner = FakeProvisioner.Success(SecondContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"),
            FakeRootBusinessUnitReader.Returns(RootBusinessUnitId, recordedContainerId: ContainerId));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(0);
        provisioner.LastBindRequest!.RemoveIfNotBound.Should().BeFalse();
    }

    [Fact]
    public async Task R5_FirstRun_CreatesAndBindsWithRemoval_ThenMarks()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-r5");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.Calls.Should().Equal("grants", "provision", "bind", "marker");
        provisioner.LastBindRequest!.RemoveIfNotBound.Should().BeTrue("a container H8 just created and could not bind is removed");
        provisioner.LastMarkerRequest.Should().Be(new SpeContainerMarkerRequest(TenantId, OwningAppId, ContainerId, CustomerId),
            "the customer's BFF recognises its containers by spaarkeCustomerId = Customer__Id (T227d)");
    }

    [Fact]
    public async Task R6_MarkerRefused_FailsResumable_ContainerStaysOnRecord_NotHandedToH7()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-r6");
        var provisioner = FakeProvisioner.Success(ContainerId);
        provisioner.MarkerOutcome = new SpeContainerMarkerOutcome.Failure("Graph PATCH .../customProperties failed with HTTP 403.");
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerMarkerFailed);
        repo.LastWrittenRun!.InterStepState.SpeContainerCreation!.RootContainerId.Should().Be(ContainerId, "a resume marks it");
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().BeNullOrEmpty("H7 consumes only a finished container");
        repo.LastWrittenRun.CompletedPhases.Should().BeEmpty();
    }

    [Fact]
    public async Task R7_MarkerInfraFault_FailsResumable_ContainerStaysOnRecord()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-r7");
        var provisioner = FakeProvisioner.Success(ContainerId);
        provisioner.MarkerThrows = new TimeoutException("The marker write exceeded 00:01:00.");
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerMarkerInfraFault);
        repo.LastWrittenRun!.InterStepState.SpeContainerCreation!.RootContainerId.Should().Be(ContainerId);
    }

    [Theory]
    [InlineData("marker")]
    [InlineData("unit")]
    [InlineData("type")]
    public async Task R8_ReusedContainerIsNotTheCustomers_FailsResumable_WritesNothingToIt(string mismatch)
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-r8");
        var provisioner = FakeProvisioner.Success(SecondContainerId);
        provisioner.InspectOutcome = mismatch switch
        {
            "marker" => new SpeContainerInspection.Found(ContainerTypeId, "globex", false, null, false),
            "unit" => new SpeContainerInspection.Found(ContainerTypeId, null, false, Guid.NewGuid(), false),
            _ => new SpeContainerInspection.Found("dddddddd-0000-0000-0000-000000000009", null, false, null, false),
        };
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"),
            FakeRootBusinessUnitReader.Returns(RootBusinessUnitId, recordedContainerId: ContainerId));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.RecordedContainerNotTheCustomers);
        provisioner.Calls.Should().Equal(new[] { "inspect" }, "no grant, no stamp, no marker — another customer's container is never written");
        repo.LastWrittenRun!.InterStepState.SpeContainerCreation?.RootContainerId.Should().BeNull("it was never adopted");
    }

    [Fact]
    public async Task R9_ReusedContainerGone_FailsResumableNotFound_NotTheReplicationWait()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-r9");
        var provisioner = FakeProvisioner.Success(SecondContainerId);
        provisioner.InspectOutcome = new SpeContainerInspection.NotFound();
        var verifier = FakeVerifier.Verified("active");
        var handler = BuildHandler(repo, provisioner, verifier,
            FakeRootBusinessUnitReader.Returns(RootBusinessUnitId, recordedContainerId: ContainerId));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.RecordedContainerNotFound);
        repo.LastWrittenRun!.Status.Should().NotBe(RunStatus.WaitingOnGate);
        provisioner.CallCount.Should().Be(0, "a missing recorded container is never silently replaced");
        verifier.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task R10_AdoptionIsKeptOnTheRecord_EvenWhenTheEnvironmentLaterReadsEmpty()
    {
        var run = BuildRun();
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord
        {
            RootContainerId = ContainerId,
            Status = SpeContainerCreationRecord.StatusReplicationPending,
            AdoptedRootContainerId = ContainerId,
        };
        var repo = new FakeRepository(run, etag: "etag-r10");
        var provisioner = FakeProvisioner.Success(SecondContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"),
            FakeRootBusinessUnitReader.Returns(RootBusinessUnitId, recordedContainerId: null));

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        provisioner.LastBindRequest!.RemoveIfNotBound.Should().BeFalse("a failed bind must never delete the customer's container");
        provisioner.LastInspectionRequest!.ContainerId.Should().Be(ContainerId, "an adopted container is re-checked before every bind");
    }

    [Fact]
    public async Task R11_AdoptedContainerBindFails_QuarantinesWithoutRemoving_KeepsItOnRecord()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-r11");
        var provisioner = FakeProvisioner.Success(SecondContainerId,
            bindOutcome: new SpeContainerBindOutcome.NotBound("stamp did not read back. It is the customer's existing container, so it was NOT removed.", Removed: false));
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"),
            FakeRootBusinessUnitReader.Returns(RootBusinessUnitId, recordedContainerId: ContainerId));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerRejectionCodes.ContainerBindingFailedNotRemoved);
        repo.LastWrittenRun!.InterStepState.SpeContainerCreation!.RootContainerId.Should().Be(ContainerId);
        repo.LastWrittenRun.InterStepState.SpeContainerCreation.AdoptedRootContainerId.Should().Be(ContainerId);
    }

    [Fact]
    public async Task R12_EverythingIsCheckedBeforeTheGrantsAreWritten()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-r12");
        var provisioner = FakeProvisioner.Success(ContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"),
            FakeRootBusinessUnitReader.Returns(RootBusinessUnitId, recordedThrows: new InvalidOperationException("403")));

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        provisioner.GrantCallCount.Should().Be(0, "a run that stops on a check has written nothing — not even the grants");
    }

    // ---------- helpers ----------

    private static H8SpeContainerHandler BuildHandler(
        IProvisioningRunRepository repo,
        ISpeContainerProvisioner provisioner,
        ISpeContainerVerifier verifier,
        IDataverseRootBusinessUnitReader? rootReader = null)
    {
        return new H8SpeContainerHandler(
            repo, provisioner, verifier,
            rootReader ?? FakeRootBusinessUnitReader.Returns(RootBusinessUnitId),
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
        run.InterStepState.BffAppRegId = "99999999-0000-0000-0000-00000000bf00";   // customer BFF app — NOT the SPE owner
        run.InterStepState.DataverseEnvUrl = DataverseEnvUrl;
        run.InterStepState.MiClientId = UamiClientId;   // stamp UAMI (H2a) — granted on the container type (T227b)
        return run;
    }

    // ---------- fakes ----------

    /// <summary>
    /// A repository that persists like Cosmos (owner round 49 item 2): it stores the JSON the PRODUCTION serializer writes
    /// (<see cref="ProductionCosmosSerializer"/> — the SDK serializer CosmosModule.BuildCosmosClient configures) and every
    /// read hands back a FRESH object deserialized from it. A field that does not survive Cosmos therefore does not survive
    /// here either.
    /// </summary>
    private sealed class FakeRepository : IProvisioningRunRepository
    {
        private string? _stored;
        private string? _etag;
        private readonly List<string>? _order;
        private readonly Func<int, ProvisioningRun, ReplaceRunResult?>? _scripted;
        private int _replaceCalls;

        /// <summary>What a read of the stored document returns now (a fresh copy, as Cosmos would give).</summary>
        public ProvisioningRun? LastWrittenRun { get; private set; }

        /// <summary>The stored document, as JSON — what Cosmos holds.</summary>
        public string? StoredJson => _stored;

        /// <summary>The If-Match ETag of every ReplaceRunAsync call, in order.</summary>
        public List<string> ETagsUsed { get; } = new();

        public FakeRepository(
            ProvisioningRun? run, string? etag, List<string>? order = null,
            Func<int, ProvisioningRun, ReplaceRunResult?>? scripted = null)
        {
            _stored = run is null ? null : ProductionCosmosSerializer.Serialize(run);
            _etag = etag;
            _order = order;
            _scripted = scripted;
        }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult(_stored is null || _etag is null
                ? null
                : new ProvisioningRunReadResult(ProductionCosmosSerializer.Deserialize<ProvisioningRun>(_stored), _etag));

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => throw new NotImplementedException();

        /// <summary>A write by someone else (an operator, the clear-quarantine service) to the stored document.</summary>
        public void Mutate(Action<ProvisioningRun> change)
        {
            var current = ProductionCosmosSerializer.Deserialize<ProvisioningRun>(_stored!);
            change(current);
            _stored = ProductionCosmosSerializer.Serialize(current);
            LastWrittenRun = ProductionCosmosSerializer.Deserialize<ProvisioningRun>(_stored);
            _etag += "-operator";
        }

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
        {
            _replaceCalls++;
            ETagsUsed.Add(ifMatchEtag);
            _order?.Add("write");
            if (_scripted?.Invoke(_replaceCalls, run) is { } scripted)
            {
                // A scripted conflict hands back a document as Cosmos would: read through the serializer.
                return Task.FromResult(scripted is ReplaceRunResult.Conflict conflict
                    ? new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(
                        ProductionCosmosSerializer.RoundTrip(conflict.Current.Run), conflict.Current.ETag))
                    : scripted);
            }

            _stored = ProductionCosmosSerializer.Serialize(run);
            LastWrittenRun = ProductionCosmosSerializer.Deserialize<ProvisioningRun>(_stored);
            _etag = ifMatchEtag + "-next";
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(
                ProductionCosmosSerializer.Deserialize<ProvisioningRun>(_stored), _etag));
        }
    }

    private sealed class FakeProvisioner : ISpeContainerProvisioner
    {
        private readonly SpeContainerProvisionOutcome? _outcome;
        private readonly Exception? _throwOnCall;
        private readonly SpeContainerBindOutcome _bindOutcome;
        private readonly Exception? _bindThrows;
        private readonly List<string>? _order;
        private readonly Queue<SpeContainerProvisionOutcome>? _outcomes;
        private readonly Queue<SpeContainerBindOutcome>? _bindOutcomes;
        public int CallCount { get; private set; }
        public SpeContainerProvisionRequest? LastRequest { get; private set; }
        public SpeContainerBindRequest? LastBindRequest { get; private set; }

        /// <summary>Every bind request, in order.</summary>
        public List<SpeContainerBindRequest> BindRequests { get; } = new();

        /// <summary>Every activation of a recorded container, in order.</summary>
        public List<SpeContainerActivationRequest> ActivationRequests { get; } = new();

        /// <summary>Every provisioner call, in order (grants / provision / activate / bind / marker).</summary>
        public List<string> Calls { get; } = new();

        public int GrantCallCount { get; private set; }
        public SpeContainerTypeGrantRequest? LastGrantRequest { get; private set; }
        public SpeContainerTypeGrantOutcome GrantOutcome { get; set; } = new SpeContainerTypeGrantOutcome.Success([]);
        public Exception? GrantThrows { get; set; }

        public SpeContainerMarkerRequest? LastMarkerRequest { get; private set; }
        public SpeContainerInspectionRequest? LastInspectionRequest { get; private set; }
        public SpeContainerInspection InspectOutcome { get; set; } =
            new SpeContainerInspection.Found(ContainerTypeId, Marker: null, MarkerUnreadable: false, BusinessUnitId: null, StampUnreadable: false);
        public SpeContainerMarkerOutcome MarkerOutcome { get; set; } = new SpeContainerMarkerOutcome.Success(Written: true);
        public Exception? MarkerThrows { get; set; }

        private FakeProvisioner(
            SpeContainerProvisionOutcome? outcome, Exception? throwOnCall,
            SpeContainerBindOutcome? bindOutcome = null, Exception? bindThrows = null, List<string>? order = null)
        {
            _outcome = outcome;
            _throwOnCall = throwOnCall;
            _bindOutcome = bindOutcome ?? new SpeContainerBindOutcome.Bound();
            _bindThrows = bindThrows;
            _order = order;
        }

        private FakeProvisioner(SpeContainerProvisionOutcome[] outcomes, SpeContainerBindOutcome[]? binds)
            : this((SpeContainerProvisionOutcome?)null, null)
        {
            _outcomes = new Queue<SpeContainerProvisionOutcome>(outcomes);
            _bindOutcomes = binds is null ? null : new Queue<SpeContainerBindOutcome>(binds);
        }

        public static FakeProvisioner Success(
            string containerId, List<string>? order = null,
            SpeContainerBindOutcome? bindOutcome = null, Exception? bindThrows = null)
            => new(new SpeContainerProvisionOutcome.Success(
                new SpeContainerProvisionOutputs(containerId)), null, bindOutcome, bindThrows, order);

        public static FakeProvisioner CreateFailure(string diagnostic)
            => new(new SpeContainerProvisionOutcome.CreateFailure(diagnostic), null);

        public static FakeProvisioner ActivateFailure(string containerId, string diagnostic)
            => new(new SpeContainerProvisionOutcome.ActivateFailure(containerId, diagnostic), null);

        public static FakeProvisioner Throws(Exception ex) => new(null, ex);

        /// <summary>One provision outcome (and optionally one bind outcome) per call, in order — for resume tests.</summary>
        public static FakeProvisioner Sequence(
            SpeContainerProvisionOutcome[] outcomes, SpeContainerBindOutcome[]? binds = null)
            => new(outcomes, binds);

        public Task<SpeContainerProvisionOutcome> ProvisionAsync(
            SpeContainerProvisionRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            Calls.Add("provision");
            _order?.Add("provision");
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_outcomes is not null ? _outcomes.Dequeue() : _outcome!);
        }

        public Task<SpeContainerProvisionOutcome> ActivateAsync(SpeContainerActivationRequest request, CancellationToken ct)
        {
            ActivationRequests.Add(request);
            Calls.Add("activate");
            _order?.Add("activate");
            return Task.FromResult<SpeContainerProvisionOutcome>(
                new SpeContainerProvisionOutcome.Success(new SpeContainerProvisionOutputs(request.ContainerId)));
        }

        public Task<SpeContainerBindOutcome> BindRootContainerAsync(SpeContainerBindRequest request, CancellationToken ct)
        {
            LastBindRequest = request;
            BindRequests.Add(request);
            Calls.Add("bind");
            _order?.Add("bind");
            if (_bindThrows is not null) throw _bindThrows;
            return Task.FromResult(_bindOutcomes is { Count: > 0 } ? _bindOutcomes.Dequeue() : _bindOutcome);
        }

        public Task<SpeContainerTypeGrantOutcome> EnsureGrantsAsync(SpeContainerTypeGrantRequest request, CancellationToken ct)
        {
            GrantCallCount++;
            LastGrantRequest = request;
            Calls.Add("grants");
            if (GrantThrows is not null) throw GrantThrows;
            return Task.FromResult(GrantOutcome);
        }

        public Task<SpeContainerInspection> InspectContainerAsync(SpeContainerInspectionRequest request, CancellationToken ct)
        {
            LastInspectionRequest = request;
            Calls.Add("inspect");
            return Task.FromResult(InspectOutcome);
        }

        public Task<SpeContainerMarkerOutcome> EnsureCustomerMarkerAsync(SpeContainerMarkerRequest request, CancellationToken ct)
        {
            LastMarkerRequest = request;
            Calls.Add("marker");
            if (MarkerThrows is not null) throw MarkerThrows;
            return Task.FromResult(MarkerOutcome);
        }
    }

    private sealed class FakeRootBusinessUnitReader : IDataverseRootBusinessUnitReader
    {
        private readonly Guid? _root;
        private readonly Exception? _throws;
        public int CallCount { get; private set; }
        public string? LastEnvironmentUrl { get; private set; }
        public string? LastTenantId { get; private set; }

        private string? _recordedContainerId;
        private Exception? _recordedThrows;
        public string? LastRecordedEnvironmentUrl { get; private set; }

        private FakeRootBusinessUnitReader(Guid? root, Exception? throws)
        {
            _root = root;
            _throws = throws;
        }

        /// <param name="recordedContainerId">The environment's sprk_SharePointEmbeddedContainerId (task 227e); null = first run.</param>
        public static FakeRootBusinessUnitReader Returns(
            Guid? root, string? recordedContainerId = null, Exception? recordedThrows = null)
            => new(root, null) { _recordedContainerId = recordedContainerId, _recordedThrows = recordedThrows };
        public static FakeRootBusinessUnitReader Throws(Exception ex) => new(null, ex);

        public Task<string?> ReadRecordedContainerIdAsync(string environmentUrl, string tenantId, CancellationToken ct)
        {
            LastRecordedEnvironmentUrl = environmentUrl;
            if (_recordedThrows is not null) throw _recordedThrows;
            return Task.FromResult(_recordedContainerId);
        }

        public Task<Guid?> ReadRootBusinessUnitIdAsync(string environmentUrl, string tenantId, CancellationToken ct)
        {
            CallCount++;
            LastEnvironmentUrl = environmentUrl;
            LastTenantId = tenantId;
            if (_throws is not null) throw _throws;
            return Task.FromResult(_root);
        }
    }

    private sealed class FakeVerifier : ISpeContainerVerifier
    {
        private readonly SpeContainerVerificationResult? _result;
        private readonly Exception? _throwOnCall;
        private readonly List<string>? _order;
        private readonly Queue<object>? _sequence;
        public int CallCount { get; private set; }
        public SpeContainerVerificationRequest? LastRequest { get; private set; }

        private FakeVerifier(SpeContainerVerificationResult? result, Exception? throwOnCall, List<string>? order = null)
        {
            _result = result;
            _throwOnCall = throwOnCall;
            _order = order;
        }

        private FakeVerifier(List<string>? order, object[] steps)
            : this(null, null, order)
        {
            _sequence = new Queue<object>(steps);
        }

        public static FakeVerifier Verified(string status, List<string>? order = null)
            => new(new SpeContainerVerificationResult.Verified(status), null, order);

        public static FakeVerifier NotVerified(string diagnostic)
            => new(new SpeContainerVerificationResult.NotVerified(diagnostic), null);

        public static FakeVerifier ReplicationPending(string diagnostic)
            => new(new SpeContainerVerificationResult.ReplicationPending(diagnostic), null);

        public static FakeVerifier Throws(Exception ex) => new(null, ex);

        /// <summary>One result (or exception) per call, in order — for resume tests.</summary>
        public static FakeVerifier Sequence(params object[] steps) => new(null, steps);

        /// <summary>As <see cref="Sequence(object[])"/>, recording "verify" into <paramref name="order"/>.</summary>
        public static FakeVerifier SequenceInOrder(List<string> order, params object[] steps) => new(order, steps);

        public Task<SpeContainerVerificationResult> VerifyAsync(
            SpeContainerVerificationRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            _order?.Add("verify");
            if (_sequence is not null)
            {
                return _sequence.Dequeue() switch
                {
                    Exception ex => throw ex,
                    SpeContainerVerificationResult r => Task.FromResult(r),
                    var other => throw new InvalidOperationException($"bad step {other}"),
                };
            }

            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_result!);
        }
    }
}
