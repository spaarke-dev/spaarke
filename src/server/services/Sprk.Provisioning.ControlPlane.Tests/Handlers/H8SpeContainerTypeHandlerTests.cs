// -----------------------------------------------------------------------------
// H8SpeContainerTypeHandlerTests.cs
//
// Unit tests over H8SpeContainerTypeHandler (task 051 — wave C4 Batch 3E).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live pwsh / az CLI / Graph / KV. Fakes
//   replace the repository + all THREE collaborator seams (provisioner,
//   verifier, kvWriter) so the handler orchestration + §4C rollback
//   classification logic is exercised in isolation. Live-Azure coverage
//   belongs in env-guarded smoke tests (parity with H3/H4 — a real SPE
//   container-type creation requires an actual customer stamp + up-to-24h
//   replication lead-time, per design.md §4.1 H8 row).
//
// NOTE ON FILE LOCATION (POML deviation, Path C pivot-to-comply):
//   The task POML's <relevant-files>/<outputs> named
//   Handlers/H8SpeContainerTypeHandler.Tests.cs (colocated with the handler).
//   The actual, already-established convention for this codebase is a
//   SEPARATE test project (Sprk.Provisioning.ControlPlane.Tests) with tests
//   under Handlers/{Name}HandlerTests.cs — see H3EntraAppRegHandlerTests.cs /
//   H4KvSecretsPopulationHandlerTests.cs. This file follows the real
//   convention rather than the POML's literal (and sibling-inconsistent) path.
//
// COVERAGE (dispatcher-context required areas + POML acceptance criteria):
//   AC-1  Happy path — provisioner Success, verifier Verified, KV Wrote ->
//         Success + CompletedPhase(H8) + InterStepState.ContainerTypeId set +
//         T6Verified gate Verified.
//   AC-2  T6 trap at provisioning (IsDelegatedTokenTrap=true) ->
//         QuarantineRequired + TrapT6DelegatedTokenDetected; verifier/kvWriter
//         NEVER called.
//   AC-3  T6 trap at verification (IsDelegatedTokenTrap=true) ->
//         QuarantineRequired + TrapT6DelegatedTokenDetected; kvWriter NEVER
//         called.
//   AC-4  Non-T6 provisioning failure -> Resumable + ProvisioningFailed.
//   AC-5  Provisioner infra fault (throws) -> Resumable + ProvisioningInfraFault.
//   AC-6  Provisioning outputs incomplete (blank containerTypeId) -> Resumable.
//   AC-7  Verification NotVerified (non-T6) -> QuarantineRequired +
//         ContainerGetVerificationFailed.
//   AC-8  Verifier infra fault (throws, AFTER creation) -> QuarantineRequired
//         + VerificationInfraFault.
//   AC-9  KV write Failure (AFTER creation+verification) -> QuarantineRequired
//         + KvWriteFailed.
//   AC-10 KV writer infra fault (throws) -> QuarantineRequired + KvWriteInfraFault.
//   AC-11 Idempotency — two invocations for same customerId; second call is a
//         no-op (spe-{customerId} match) — NO provisioner/verifier/kvWriter calls.
//   AC-12 Missing tenantId (§4D I1/I5) -> Resumable + MissingTenantId.
//   AC-13 Missing keyVaultName -> Resumable + MissingKeyVaultName.
//   AC-14 Missing subscriptionId -> Resumable + MissingSubscriptionId.
//   AC-15 Missing sharePointDomain -> Resumable + MissingSharePointDomain.
//   AC-16 Missing owningAppId (H3 not complete — InterStepState.BffAppRegId
//         empty) -> Resumable + MissingOwningAppId.
//   AC-17 Run not found -> Resumable + RunNotFound.
//   AC-18 HandlerId mismatch -> throws InvalidOperationException.
//   AC-19 Idempotency-key format determinism — spe-{customerId}, customerId-only
//         (version-independent, unlike H4's secretsVer-suffixed key).
//   AC-20 KV writer request carries tenant-scoped inputs (subscriptionId +
//         keyVaultName from run parameters, never hardcoded) — I4 derivation.
//   AC-21 Upgrade mode propagated to KV writer (provisionedOn param present).
//   AC-22 (task 131) Verification ReplicationPending (24h SPE replication
//         lag, GraphAppOnlyContainerVerifier's 404 signature) -> Success +
//         RunStatus.WaitingOnGate (NOT Resumable, NOT QuarantineRequired) +
//         InterStepState IDs persisted + T6Verified gate Pending + NO
//         CompletedPhase appended + kvWriter NEVER called.
//   AC-30..AC-38 (unified-access-control-r2 task 165, owner round 41 item 1)
//         H8 RECORDS what it created before verifying it, and a re-entry
//         (after the 24h replication wait, a quarantine, a lost write or a
//         crash) RESUMES with the recorded root container — it never creates
//         a second container type or root container, never hands an unbound
//         container to H7, and when the recorded root container was removed
//         (failed bind) it creates only a new root container in the same type.
//   AC-23..AC-29 (unified-access-control-r2 task 165, owner round 35 item 1)
//         Every container is stamped with its owning business unit: H8 binds
//         the verified root container to the customer environment's ROOT
//         business unit (H5 output) BEFORE the KV write and the H7 handoff;
//         the owner is resolved before anything is created; a bind failure
//         is QuarantineRequired and never hands the container to H7.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Sprk.Provisioning.ControlPlane.Tests.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H8SpeContainerTypeHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h8-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "sub-cus-acme-prod";
    private const string KeyVaultName = "sprk-acme-prod-kv";
    private const string SharePointDomain = "acme.sharepoint.com";
    private const string OwningAppId = "77777777-8888-9999-aaaa-bbbbbbbbbbbb";
    private const string ContainerTypeId = "cccccccc-dddd-eeee-ffff-000000000001";
    private const string RootContainerId = "b!aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DataverseEnvUrl = "https://acme-prod.crm.dynamics.com";
    private static readonly Guid RootBusinessUnitId = Guid.Parse("0b0b0b0b-1111-2222-3333-444444444444");

    // ---------- AC-1 happy path ----------

    [Fact]
    public async Task AC1_HappyPath_AllSeamsGreen_SucceedsAndAdvancesState()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var verifier = FakeVerifier.Verified("active");
        var kvWriter = FakeKvWriter.Wrote();
        var handler = BuildHandler(repo, provisioner, verifier, kvWriter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H8SpeContainerTypeHandler.BuildIdempotencyKey(CustomerId));

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H8");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle().Which.Phase.Should().Be("H8");
        repo.LastWrittenRun.InterStepState.ContainerTypeId.Should().Be(ContainerTypeId);
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().Be(RootContainerId,
            "H7 (task 050) reads this field as the source for sprk_SharePointEmbeddedContainerId");
        repo.LastWrittenRun.GateStates.Should().ContainKey(SpeContainerTypeGates.T6Verified);
        repo.LastWrittenRun.GateStates[SpeContainerTypeGates.T6Verified].Status.Should().Be(GateState.Verified);
        repo.LastWrittenRun.GateStates[SpeContainerTypeGates.T6Verified].Evidence!.Value
            .GetProperty("verifiedViaAppOnlyToken").GetBoolean().Should().BeTrue(
                "genuine verification happened on the happy path");

        provisioner.CallCount.Should().Be(1);
        verifier.CallCount.Should().Be(1);
        kvWriter.CallCount.Should().Be(1);

        provisioner.LastRequest.Should().NotBeNull();
        provisioner.LastRequest!.OwningAppId.Should().Be(OwningAppId);
        provisioner.LastRequest.TenantId.Should().Be(TenantId);

        verifier.LastRequest!.ContainerId.Should().Be(RootContainerId);
        kvWriter.LastRequest!.ContainerTypeId.Should().Be(ContainerTypeId);

        // Task 165, round 35 item 1 — the root container is bound to the environment's root business unit.
        provisioner.BindCallCount.Should().Be(1);
        provisioner.LastBindRequest!.ContainerId.Should().Be(RootContainerId);
        provisioner.LastBindRequest.BusinessUnitId.Should().Be(RootBusinessUnitId);
        repo.LastWrittenRun.GateStates[SpeContainerTypeGates.T6Verified].Evidence!.Value
            .GetProperty("owningBusinessUnitId").GetString().Should().Be(RootBusinessUnitId.ToString("D"));
    }

    // ---------- AC-2 T6 trap at provisioning ----------

    [Fact]
    public async Task AC2_T6TrapAtProvisioning_FailsQuarantineRequired_NoVerifierOrKvWriterCall()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-2");
        var provisioner = FakeProvisioner.Failure(
            "T6 silent-fail trap detected: output contains 'public client not allowed'",
            isDelegatedTokenTrap: true);
        var verifier = FakeVerifier.Verified("active");
        var kvWriter = FakeKvWriter.Wrote();
        var handler = BuildHandler(repo, provisioner, verifier, kvWriter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.TrapT6DelegatedTokenDetected);
        failure.Diagnostic.Should().Contain("public client not allowed");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        verifier.CallCount.Should().Be(0);
        kvWriter.CallCount.Should().Be(0);
    }

    // ---------- AC-3 T6 trap at verification ----------

    [Fact]
    public async Task AC3_T6TrapAtVerification_FailsQuarantineRequired_NoKvWriterCall()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-3");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var verifier = FakeVerifier.NotVerified(
            "T6 silent-fail trap detected during verification: 'public client not allowed'",
            isDelegatedTokenTrap: true);
        var kvWriter = FakeKvWriter.Wrote();
        var handler = BuildHandler(repo, provisioner, verifier, kvWriter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.TrapT6DelegatedTokenDetected);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        kvWriter.CallCount.Should().Be(0);
    }

    // ---------- AC-4 non-T6 provisioning failure ----------

    [Fact]
    public async Task AC4_NonT6ProvisioningFailure_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-4");
        var provisioner = FakeProvisioner.Failure(
            "Create-NewContainerType.ps1 exited 1: Graph API 503 ServiceUnavailable",
            isDelegatedTokenTrap: false);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.ProvisioningFailed);
        failure.Diagnostic.Should().Contain("ServiceUnavailable");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- AC-5 provisioner infra fault ----------

    [Fact]
    public async Task AC5_ProvisionerThrows_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-5");
        var provisioner = FakeProvisioner.Throws(new FileNotFoundException("script not found"));
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.ProvisioningInfraFault);
        failure.Diagnostic.Should().Contain("FileNotFoundException");
    }

    // ---------- AC-6 provisioning outputs incomplete ----------

    [Fact]
    public async Task AC6_ProvisioningOutputsIncomplete_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-6");
        var provisioner = FakeProvisioner.Success(containerTypeId: "", rootContainerId: RootContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.ProvisioningOutputsIncomplete);
    }

    // ---------- AC-7 verification NotVerified (non-T6) ----------

    [Fact]
    public async Task AC7_VerificationNotVerified_NonT6_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-7");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        // NOTE (task 131): a 404 on this GET is now classified ReplicationPending
        // (see AC-22) — this test uses a genuinely non-transient error (403) to
        // exercise the NotVerified/QuarantineRequired path.
        var verifier = FakeVerifier.NotVerified("GET returned 403 Forbidden — unexpected permission error", isDelegatedTokenTrap: false);
        var handler = BuildHandler(repo, provisioner, verifier, FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.ContainerGetVerificationFailed);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- AC-8 verifier infra fault ----------

    [Fact]
    public async Task AC8_VerifierThrows_AfterCreation_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-8");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var verifier = FakeVerifier.Throws(new TimeoutException("verify script timed out"));
        var handler = BuildHandler(repo, provisioner, verifier, FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired,
            "container WAS created — an unverifiable post-condition is worse than a clean failure");
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.VerificationInfraFault);
        failure.Diagnostic.Should().Contain(RootContainerId);
    }

    // ---------- AC-9 KV write Failure ----------

    [Fact]
    public async Task AC9_KvWriteFailure_AfterCreationAndVerification_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-9");
        var kvWriter = FakeKvWriter.Failure("az keyvault secret set exit 1: Forbidden");
        var handler = BuildHandler(repo, FakeProvisioner.Success(ContainerTypeId, RootContainerId),
            FakeVerifier.Verified("active"), kvWriter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.KvWriteFailed);
        failure.Diagnostic.Should().Contain("Forbidden");
    }

    // ---------- AC-10 KV writer infra fault ----------

    [Fact]
    public async Task AC10_KvWriterThrows_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-10");
        var kvWriter = FakeKvWriter.Throws(new InvalidOperationException("az CLI not on PATH"));
        var handler = BuildHandler(repo, FakeProvisioner.Success(ContainerTypeId, RootContainerId),
            FakeVerifier.Verified("active"), kvWriter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.KvWriteInfraFault);
    }

    // ---------- AC-11 idempotency ----------

    [Fact]
    public async Task AC11_Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRun();
        var expectedKey = H8SpeContainerTypeHandler.BuildIdempotencyKey(CustomerId);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H8",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-11");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var verifier = FakeVerifier.Verified("active");
        var kvWriter = FakeKvWriter.Wrote();
        var handler = BuildHandler(repo, provisioner, verifier, kvWriter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        provisioner.CallCount.Should().Be(0);
        verifier.CallCount.Should().Be(0);
        kvWriter.CallCount.Should().Be(0);
    }

    // ---------- AC-12..AC-15 parameter guards ----------

    [Fact]
    public async Task AC12_MissingTenantId_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H8SpeContainerTypeHandler.TenantIdParameterKey);
        var repo = new FakeRepository(run, etag: "etag-12");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.MissingTenantId);
        provisioner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC13_MissingKeyVaultName_FailsResumable()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H8SpeContainerTypeHandler.KeyVaultNameParameterKey);
        var repo = new FakeRepository(run, etag: "etag-13");
        var handler = BuildHandler(repo, FakeProvisioner.Success(ContainerTypeId, RootContainerId),
            FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.MissingKeyVaultName);
    }

    [Fact]
    public async Task AC14_MissingSubscriptionId_FailsResumable()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H8SpeContainerTypeHandler.SubscriptionIdParameterKey);
        var repo = new FakeRepository(run, etag: "etag-14");
        var handler = BuildHandler(repo, FakeProvisioner.Success(ContainerTypeId, RootContainerId),
            FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.MissingSubscriptionId);
    }

    [Fact]
    public async Task AC15_MissingSharePointDomain_FailsResumable()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H8SpeContainerTypeHandler.SharePointDomainParameterKey);
        var repo = new FakeRepository(run, etag: "etag-15");
        var handler = BuildHandler(repo, FakeProvisioner.Success(ContainerTypeId, RootContainerId),
            FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.MissingSharePointDomain);
    }

    // ---------- AC-16 missing owningAppId (H3 not complete) ----------

    [Fact]
    public async Task AC16_MissingOwningAppId_H3NotComplete_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun();
        run.InterStepState.BffAppRegId = null;
        var repo = new FakeRepository(run, etag: "etag-16");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.MissingOwningAppId);
        failure.Diagnostic.Should().Contain("H3");
        provisioner.CallCount.Should().Be(0);
    }

    // ---------- AC-17 run not found ----------

    [Fact]
    public async Task AC17_RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var handler = BuildHandler(repo, FakeProvisioner.Success(ContainerTypeId, RootContainerId),
            FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.RunNotFound);
    }

    // ---------- AC-18 handler-id mismatch ----------

    [Fact]
    public async Task AC18_HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-18");
        var handler = BuildHandler(repo, FakeProvisioner.Success(ContainerTypeId, RootContainerId),
            FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

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

    // ---------- AC-19 idempotency-key format determinism ----------

    [Fact]
    public void AC19_IdempotencyKey_IsCustomerIdOnly_VersionIndependent()
    {
        var k1 = H8SpeContainerTypeHandler.BuildIdempotencyKey("acme");
        var k2 = H8SpeContainerTypeHandler.BuildIdempotencyKey("acme");
        k1.Should().Be(k2);
        k1.Should().Be("spe-acme");

        H8SpeContainerTypeHandler.BuildIdempotencyKey("other").Should().NotBe(k1);
    }

    // ---------- AC-20 KV writer request carries tenant-scoped inputs (I4) ----------

    [Fact]
    public async Task AC20_KvWriterRequest_UsesTenantScopedSubscriptionAndVault_NeverHardcoded()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-20");
        var kvWriter = FakeKvWriter.Wrote();
        var handler = BuildHandler(repo, FakeProvisioner.Success(ContainerTypeId, RootContainerId),
            FakeVerifier.Verified("active"), kvWriter);

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        kvWriter.LastRequest.Should().NotBeNull();
        kvWriter.LastRequest!.TargetKeyVaultName.Should().Be(KeyVaultName);
        kvWriter.LastRequest.SubscriptionId.Should().Be(SubscriptionId);
        kvWriter.LastRequest.CustomerId.Should().Be(CustomerId);
    }

    // ---------- AC-21 upgrade mode propagated ----------

    [Fact]
    public async Task AC21_UpgradeMode_ProvisionedOnPresent_PropagatedToKvWriter()
    {
        var run = BuildRun(provisionedOn: "2026-01-01T00:00:00Z");
        var repo = new FakeRepository(run, etag: "etag-21");
        var kvWriter = FakeKvWriter.Wrote();
        var handler = BuildHandler(repo, FakeProvisioner.Success(ContainerTypeId, RootContainerId),
            FakeVerifier.Verified("active"), kvWriter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        kvWriter.LastRequest!.UpgradeMode.Should().BeTrue();
    }

    // ---------- AC-22 (task 131) 24h SPE replication lag -> WaitingOnGate ----------

    [Fact]
    public async Task AC22_VerificationReplicationPending_SucceedsWithRunStatusWaitingOnGate_NoKvWriterCall()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-22");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var verifier = FakeVerifier.ReplicationPending(
            "App-only GET returned 404 Not Found — consistent with SPE's up-to-24h container-type " +
            "replication window.");
        var kvWriter = FakeKvWriter.Wrote();
        var handler = BuildHandler(repo, provisioner, verifier, kvWriter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        // Success, not Failure — H8 correctly identified + recorded the
        // external wait; this is not an operator-actionable error.
        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H8SpeContainerTypeHandler.BuildIdempotencyKey(CustomerId));

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.WaitingOnGate,
            "DS-4 §2 / this project's CLAUDE.md MUST rules: the 24h SPE replication gate is a run-level " +
            "external blocker, never Resumable/QuarantineRequired");
        repo.LastWrittenRun.CurrentPhase.Should().Be("H8");
        repo.LastWrittenRun.ErrorDetail.Should().BeNull("a replication-pending wait is not an error");

        // Container-type/root-container ARE real, durable side effects — RECORDED (InterStepState.ContainerTypeId +
        // the T6 gate's evidence) so a later re-entry RESUMES with them, but NOT handed to H7: the root container is
        // still UNBOUND (unified-access-control-r2 task 165, owner round 41 item 1).
        repo.LastWrittenRun.InterStepState.ContainerTypeId.Should().Be(ContainerTypeId);
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().BeNull(
            "SpeContainerId is H7's hand-off — an unbound container is never handed off");
        // Owner round 49 item 2: the resume record is TYPED, and it is read back from what Cosmos stores.
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.RootContainerId.Should().Be(RootContainerId);
        repo.LastWrittenRun.InterStepState.SpeContainerCreation.Status.Should().Be(SpeContainerCreationRecord.StatusReplicationPending);
        repo.LastWrittenRun.GateStates[SpeContainerTypeGates.T6Verified].Evidence!.Value
            .GetProperty("rootContainerId").GetString().Should().Be(RootContainerId,
                "the evidence is the operator's record — it now survives the production serializer too");

        // Gate is Pending, NOT Verified — verification genuinely has not happened yet.
        repo.LastWrittenRun.GateStates.Should().ContainKey(SpeContainerTypeGates.T6Verified);
        repo.LastWrittenRun.GateStates[SpeContainerTypeGates.T6Verified].Status.Should().Be(GateState.Pending);
        repo.LastWrittenRun.GateStates[SpeContainerTypeGates.T6Verified].Evidence!.Value
            .GetProperty("verifiedViaAppOnlyToken").GetBoolean().Should().BeFalse(
                "regression guard: evidence must NOT claim verification happened when it has not " +
                "(BuildEvidence previously hardcoded true unconditionally — task 131 fix)");

        // NOT recorded as a CompletedPhase — H8 has not finished; a resume
        // must re-execute HandleAsync in full (Level-3 idempotency does NOT
        // short-circuit this run).
        repo.LastWrittenRun.CompletedPhases.Should().BeEmpty();

        kvWriter.CallCount.Should().Be(0, "KV write only happens after Verified, unchanged ordering");
        provisioner.BindCallCount.Should().Be(0,
            "the container is bound only once it is verified addressable — binding it during the 24h replication " +
            "window would delete a healthy container");
    }

    // ---------- AC-23..AC-30 business-unit stamp (task 165, owner round 35 item 1) ----------

    [Fact]
    public async Task AC23_TheRootContainer_IsBoundToTheEnvironmentsRootBusinessUnit_AfterVerification_BeforeTheKvWrite()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-23");
        var order = new List<string>();
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId, order);
        var verifier = FakeVerifier.Verified("active", order);
        var kvWriter = FakeKvWriter.Wrote(order);
        var reader = FakeRootBusinessUnitReader.Returns(RootBusinessUnitId);
        var handler = BuildHandler(repo, provisioner, verifier, kvWriter, reader);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        order.Should().Equal("provision", "verify", "bind", "kv");
        reader.LastEnvironmentUrl.Should().Be(DataverseEnvUrl);
        reader.LastTenantId.Should().Be(TenantId, "§4D I5 — the customer's tenant, never a default");
        var bind = provisioner.LastBindRequest!;
        bind.ContainerId.Should().Be(RootContainerId);
        bind.BusinessUnitId.Should().Be(RootBusinessUnitId);
        bind.TenantId.Should().Be(TenantId);
        bind.OwningAppId.Should().Be(OwningAppId, "the same T6 identity that created the container binds it");
        bind.VaultName.Should().Be(KeyVaultName);
    }

    [Fact]
    public async Task AC24_MissingDataverseEnvUrl_FailsResumable_BeforeAnythingIsCreated()
    {
        var run = BuildRun();
        run.InterStepState.DataverseEnvUrl = null;
        var repo = new FakeRepository(run, etag: "etag-24");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var reader = FakeRootBusinessUnitReader.Returns(RootBusinessUnitId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote(), reader);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.MissingDataverseEnvUrl);
        provisioner.CallCount.Should().Be(0, "no container may be created whose owner is not established");
        reader.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC25_AnEnvironmentWithNoRootBusinessUnit_FailsResumable_AndCreatesNothing()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-25");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote(),
            FakeRootBusinessUnitReader.Returns(null));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.RootBusinessUnitUnresolved);
        provisioner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC26_ARootBusinessUnitReadFault_FailsResumable_AndCreatesNothing()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-26");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote(),
            FakeRootBusinessUnitReader.Throws(new HttpRequestException("Dataverse 503")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.RootBusinessUnitUnresolved);
        failure.Diagnostic.Should().Contain("Dataverse 503");
        provisioner.CallCount.Should().Be(0, "fail closed: never create a container with an unread owner");
    }

    [Theory]
    [InlineData(true, SpeContainerTypeRejectionCodes.ContainerBindingFailed)]
    [InlineData(false, SpeContainerTypeRejectionCodes.ContainerBindingFailedNotRemoved)]
    public async Task AC27_ABindFailure_IsQuarantined_AndTheContainerIsNeverHandedToH7(bool removed, string expectedCode)
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-27");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId,
            bindOutcome: new SpeContainerBindOutcome.NotBound("the stamp did not read back", removed));
        var kvWriter = FakeKvWriter.Wrote();
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), kvWriter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(expectedCode);
        failure.Diagnostic.Should().Contain("did not read back");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().BeNull(
            "an unbound (or removed) container must never become H7's sprk_SharePointEmbeddedContainerId");
        repo.LastWrittenRun.CompletedPhases.Should().BeEmpty();
        kvWriter.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC28_ABindInfraFault_IsQuarantined_AndTheContainerIsNeverHandedToH7()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-28");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId,
            bindThrows: new InvalidOperationException("KV cert unreadable"));
        var kvWriter = FakeKvWriter.Wrote();
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), kvWriter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.ContainerBindingInfraFault);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().BeNull();
        kvWriter.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC29_AnIdempotentReRun_ReadsNoBusinessUnit_AndBindsNothing()
    {
        var run = BuildRun();
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H8",
            IdempotencyKey = H8SpeContainerTypeHandler.BuildIdempotencyKey(CustomerId),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            JobId = RunId,
        });
        run.InterStepState.DataverseEnvUrl = null; // a run that completed H8 before H8 needed it — still a no-op
        var repo = new FakeRepository(run, etag: "etag-29");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var reader = FakeRootBusinessUnitReader.Returns(RootBusinessUnitId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote(), reader);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        reader.CallCount.Should().Be(0);
        provisioner.BindCallCount.Should().Be(0);
    }


    // ---------- AC-30..AC-38 resume with what was created (task 165, owner round 41 item 1) ----------

    [Fact]
    public async Task AC30_AReplicationPendingWait_ThenResume_CreatesNothing_AndBindsAndHandsOffTheSameContainer()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-30");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var verifier = FakeVerifier.Sequence(
            new SpeContainerVerificationResult.ReplicationPending("404 — the 24h replication window"),
            new SpeContainerVerificationResult.Verified("active"));
        var kvWriter = FakeKvWriter.Wrote();
        var handler = BuildHandler(repo, provisioner, verifier, kvWriter);

        (await handler.HandleAsync(BuildEnvelope(), CancellationToken.None)).Should().BeOfType<HandlerResult.Success>();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.WaitingOnGate);
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().BeNull("the container is not bound yet");

        // The reconciler re-dispatches H8 once the processed marker expires (~24h).
        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(1,
            "the resume must NOT create a second container type and root container (the first would be orphaned UNBOUND)");
        verifier.CallCount.Should().Be(2);
        verifier.LastRequest!.ContainerId.Should().Be(RootContainerId, "the resume verifies the container it recorded");
        provisioner.BindCallCount.Should().Be(1);
        provisioner.LastBindRequest!.ContainerId.Should().Be(RootContainerId);
        kvWriter.LastRequest!.ContainerTypeId.Should().Be(ContainerTypeId);
        repo.LastWrittenRun!.CompletedPhases.Should().ContainSingle(cp => cp.Phase == "H8");
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().Be(RootContainerId,
            "only now — bound — is the container handed to H7");
        repo.LastWrittenRun.GateStates[SpeContainerTypeGates.T6Verified].Status.Should().Be(GateState.Verified);
    }

    [Fact]
    public async Task AC31_AResumeDuringTheWait_CreatesNothing_AndKeepsWaiting()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-31");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var verifier = FakeVerifier.ReplicationPending("404 — still replicating");
        var handler = BuildHandler(repo, provisioner, verifier, FakeKvWriter.Wrote());

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);
        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(1);
        provisioner.BindCallCount.Should().Be(0, "an unaddressable container is not bound (D19)");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.WaitingOnGate);
        repo.LastWrittenRun.InterStepState.ContainerTypeId.Should().Be(ContainerTypeId);
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().BeNull();
    }

    [Fact]
    public async Task AC32_TheCreationIsRecordedBeforeVerification_SoAQuarantinedVerifyResumesWithTheSameContainer()
    {
        var run = BuildRun();
        var order = new List<string>();
        var repo = new FakeRepository(run, etag: "etag-32", order: order);
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId, order);
        var verifier = FakeVerifier.SequenceInOrder(order,
            new TimeoutException("verify timed out"),
            new SpeContainerVerificationResult.Verified("active"));
        var handler = BuildHandler(repo, provisioner, verifier, FakeKvWriter.Wrote(order));

        var first = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);
        first.Should().BeOfType<HandlerResult.Failure>().Which.Class.Should().Be(FailureClass.QuarantineRequired);
        order.Take(3).Should().Equal(new[] { "provision", "write", "verify" },
            "the creation is persisted to the run BEFORE anything that can fail, wait or crash");
        repo.LastWrittenRun!.InterStepState.ContainerTypeId.Should().Be(ContainerTypeId);

        // The operator clears the quarantine and resumes.
        var resumed = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        resumed.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(1);
        provisioner.LastBindRequest!.ContainerId.Should().Be(RootContainerId);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(RootContainerId);
    }

    [Fact]
    public async Task AC33_AFailedBindThatRemovedTheContainer_ResumesWithANewRootContainerInTheSameType_NeverASecondType()
    {
        const string secondRoot = "b!bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-33");
        var provisioner = FakeProvisioner.Sequence(
            new SpeContainerTypeProvisionOutcome[]
            {
                new SpeContainerTypeProvisionOutcome.Success(new SpeContainerTypeProvisionOutputs(ContainerTypeId, RootContainerId)),
                new SpeContainerTypeProvisionOutcome.Success(new SpeContainerTypeProvisionOutputs(ContainerTypeId, secondRoot)),
            },
            new SpeContainerBindOutcome[]
            {
                new SpeContainerBindOutcome.NotBound("the stamp did not read back — the container was removed", Removed: true),
                new SpeContainerBindOutcome.Bound(),
            });
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var first = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);
        first.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode
            .Should().Be(SpeContainerTypeRejectionCodes.ContainerBindingFailed);
        repo.LastWrittenRun!.InterStepState.ContainerTypeId.Should().Be(ContainerTypeId, "the type stays on record");
        H8SpeContainerTypeHandler.ReadRecordedCreation(repo.LastWrittenRun).RootContainerId
            .Should().BeNull("the removed container must not be verified again — a 404 reads as the replication wait");

        var resumed = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        resumed.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(2);
        provisioner.LastRequest!.ExistingContainerTypeId.Should().Be(ContainerTypeId,
            "a container type cannot be deleted — the resume creates only a root container in the recorded one");
        provisioner.LastBindRequest!.ContainerId.Should().Be(secondRoot);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(secondRoot);
    }

    [Fact]
    public async Task AC34_AProvisionerFailureAfterTheTypeWasCreated_RecordsTheType_AndTheResumeCreatesOnlyARootContainer()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-34");
        var provisioner = FakeProvisioner.Sequence(new SpeContainerTypeProvisionOutcome[]
        {
            new SpeContainerTypeProvisionOutcome.Failure("Graph 503 creating the root container", IsDelegatedTokenTrap: false,
                CreatedContainerTypeId: ContainerTypeId),
            new SpeContainerTypeProvisionOutcome.Success(new SpeContainerTypeProvisionOutputs(ContainerTypeId, RootContainerId)),
        });
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var first = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);
        first.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode
            .Should().Be(SpeContainerTypeRejectionCodes.ProvisioningFailed);
        repo.LastWrittenRun!.InterStepState.ContainerTypeId.Should().Be(ContainerTypeId);

        var resumed = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        resumed.Should().BeOfType<HandlerResult.Success>();
        provisioner.LastRequest!.ExistingContainerTypeId.Should().Be(ContainerTypeId);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(RootContainerId);
    }

    [Fact]
    public async Task AC35_ARunLeftByThePreRound41PendingPath_ResumesWithItsContainer_AndWithdrawsTheUnboundHandOff()
    {
        // The old MarkWaitingOnGateAsync wrote ContainerTypeId, SpeContainerId (the UNBOUND container — the defect) and
        // the T6 gate Pending with the root container in its evidence — which Cosmos' Newtonsoft serializer STORED as
        // {"valueKind":1} (owner round 49 item 2). This is the document such a run really has: SpeContainerId is its only
        // record of the root container.
        var run = BuildRun();
        run.Status = RunStatus.WaitingOnGate;
        run.InterStepState.ContainerTypeId = ContainerTypeId;
        run.InterStepState.SpeContainerId = RootContainerId;
        run.GateStates[SpeContainerTypeGates.T6Verified] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = "H8",
            Evidence = System.Text.Json.JsonSerializer.SerializeToElement(new { valueKind = 1 }),
        };
        var repo = new FakeRepository(run, etag: "etag-35");
        var provisioner = FakeProvisioner.Success("dddddddd-0000-0000-0000-000000000002", "b!second");
        var verifier = FakeVerifier.NotVerified("GET returned 403 Forbidden", isDelegatedTokenTrap: false);
        var handler = BuildHandler(repo, provisioner, verifier, FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.Class.Should().Be(FailureClass.QuarantineRequired);
        provisioner.CallCount.Should().Be(0, "the run already holds a container type and root container");
        verifier.LastRequest!.ContainerId.Should().Be(RootContainerId);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().BeNull(
            "the unbound hand-off is withdrawn — even when this entry stops short of completing");
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.RootContainerId.Should().Be(RootContainerId,
            "the root container is MOVED into the typed record, so the next resume still finds it");
    }

    [Fact]
    public async Task AC40_AnUnboundHandOff_IsWithdrawn_EvenWhenTheEntryStopsBeforeReadingItsRecord()
    {
        // The pre-round-41 pending path left SpeContainerId set; this entry fails reading the root business unit (5b) —
        // its failure write must not carry the unbound hand-off forward.
        var run = BuildRun();
        run.InterStepState.ContainerTypeId = ContainerTypeId;
        run.InterStepState.SpeContainerId = RootContainerId;
        var repo = new FakeRepository(run, etag: "etag-40");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote(),
            FakeRootBusinessUnitReader.Throws(new HttpRequestException("Dataverse 503")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode
            .Should().Be(SpeContainerTypeRejectionCodes.RootBusinessUnitUnresolved);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().BeNull();
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.RootContainerId.Should().Be(RootContainerId,
            "the withdrawn hand-off is the run's only record of its root container — it must survive this write");
        provisioner.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]   // the typed record names a root container but no type
    [InlineData(false)]  // a pre-round-41 hand-off without a type
    public async Task AC36_ARecordedRootContainerWithoutAType_IsQuarantined_AndNothingIsCreated(bool typedRecord)
    {
        var run = BuildRun();
        if (typedRecord)
        {
            run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord { RootContainerId = RootContainerId };
        }
        else
        {
            run.InterStepState.SpeContainerId = RootContainerId;
        }

        var repo = new FakeRepository(run, etag: "etag-36");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var verifier = FakeVerifier.Verified("active");
        var handler = BuildHandler(repo, provisioner, verifier, FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.CreationRecordInconsistent);
        provisioner.CallCount.Should().Be(0);
        verifier.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC37_TheCreationRecord_IsMergedOverAConcurrentWrite_AndSurvives()
    {
        var run = BuildRun();
        var concurrent = BuildRun();
        concurrent.GateStates["operator-note"] = new GateEntry { Status = GateState.Verified, VerifierHandler = "operator" };
        var repo = new FakeRepository(run, etag: "etag-37",
            scripted: (call, _) => call == 1
                ? new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(concurrent, "etag-37-operator"))
                : null);
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        repo.ETagsUsed[1].Should().Be("etag-37-operator", "the record is retried over the CURRENT document");
        repo.LastWrittenRun!.GateStates.Should().ContainKey("operator-note", "the concurrent write is kept");
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().Be(RootContainerId);
        provisioner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task AC39_ARecordNeverOverwritesAnotherCreationsRecord_TheNewOneIsQuarantinedInstead()
    {
        // A concurrent write that recorded a DIFFERENT container type: merging over it would orphan THAT creation.
        var run = BuildRun();
        var concurrent = BuildRun();
        concurrent.InterStepState.ContainerTypeId = "eeeeeeee-0000-0000-0000-00000000000e";
        var repo = new FakeRepository(run, etag: "etag-39",
            scripted: (call, _) => call == 1
                ? new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(concurrent, "etag-39-other"))
                : null);
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var verifier = FakeVerifier.Verified("active");
        var handler = BuildHandler(repo, provisioner, verifier, FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.CreationRecordNotPersisted);
        failure.Diagnostic.Should().Contain("eeeeeeee-0000-0000-0000-00000000000e");
        repo.ETagsUsed.Should().ContainSingle("the other creation's record is never written over");
        verifier.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC38_ACreationThatCannotBeRecorded_IsQuarantined_NamingBothIds_AndGoesNoFurther()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-38",
            scripted: (call, _) => call == 1 ? new ReplaceRunResult.NotFound() : null);
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var verifier = FakeVerifier.Verified("active");
        var kvWriter = FakeKvWriter.Wrote();
        var handler = BuildHandler(repo, provisioner, verifier, kvWriter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.CreationRecordNotPersisted);
        failure.Diagnostic.Should().Contain(ContainerTypeId).And.Contain(RootContainerId).And.Contain("-Bind");
        verifier.CallCount.Should().Be(0);
        provisioner.BindCallCount.Should().Be(0);
        kvWriter.CallCount.Should().Be(0);
    }

    // ---------- AC-41..AC-50 the record survives Cosmos; no fault leaves an orphan (task 165, owner round 49 item 2) ----------

    [Fact]
    public async Task AC41_TheCreationRecord_IsTypedInWhatCosmosStores_NotInGateEvidence()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-41");
        var handler = BuildHandler(repo, FakeProvisioner.Success(ContainerTypeId, RootContainerId),
            FakeVerifier.ReplicationPending("404 — the 24h replication window"), FakeKvWriter.Wrote());

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        using var stored = System.Text.Json.JsonDocument.Parse(repo.StoredJson!);
        var interStep = stored.RootElement.GetProperty("interStepState");
        interStep.GetProperty("containerTypeId").GetString().Should().Be(ContainerTypeId);
        interStep.GetProperty("speContainerCreation").GetProperty("rootContainerId").GetString().Should().Be(RootContainerId,
            "the record is a typed field — the production serializer writes it as itself");
        stored.RootElement.GetProperty("gateStates").GetProperty(SpeContainerTypeGates.T6Verified).GetProperty("evidence")
            .TryGetProperty("valueKind", out _).Should().BeFalse("gate evidence is no longer lost as {\"valueKind\":1}");
    }

    [Fact]
    public async Task AC42_ARootContainerPostWithNoAnswer_IsRecorded_AndTheResumeWaitsRatherThanCreatingASecond()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-42");
        var provisioner = FakeProvisioner.Sequence(new SpeContainerTypeProvisionOutcome[]
        {
            new SpeContainerTypeProvisionOutcome.Failure("Graph RootContainerPost got no answer: HttpRequestException",
                IsDelegatedTokenTrap: false, CreatedContainerTypeId: ContainerTypeId, RootContainerInDoubt: true),
            new SpeContainerTypeProvisionOutcome.RootContainerNotYetVisible(ContainerTypeId, "lists no container yet"),
        });
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var first = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        first.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.ProvisioningFailed);
        var record = repo.LastWrittenRun!.InterStepState.SpeContainerCreation!;
        repo.LastWrittenRun.InterStepState.ContainerTypeId.Should().Be(ContainerTypeId, "the type the failed call created is recorded");
        record.RootContainerInDoubtSince.Should().NotBeNull("the container may exist — the resume must not create blindly");
        record.Status.Should().Be(SpeContainerCreationRecord.StatusRootContainerInDoubt);

        var resumed = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        resumed.Should().BeOfType<HandlerResult.Success>();
        provisioner.Requests[1].ExistingContainerTypeId.Should().Be(ContainerTypeId, "never a second container type");
        provisioner.Requests[1].RootContainerCreationInDoubt.Should().BeTrue(
            "within the replication window the provisioner must not create while the type lists nothing");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.WaitingOnGate);
        repo.LastWrittenRun.InterStepState.SpeContainerId.Should().BeNull();
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.RootContainerInDoubtSince.Should().Be(record.RootContainerInDoubtSince,
            "the wait keeps the original window");
        provisioner.BindCallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC43_ARootContainerInDoubt_PastTheReplicationWindow_MayBeCreatedAgain()
    {
        var run = BuildRun();
        run.InterStepState.ContainerTypeId = ContainerTypeId;
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord
        {
            RootContainerInDoubtSince = DateTimeOffset.UtcNow - H8SpeContainerTypeHandler.RootContainerInDoubtWindow - TimeSpan.FromMinutes(5),
            Status = SpeContainerCreationRecord.StatusRootContainerInDoubt,
        };
        var repo = new FakeRepository(run, etag: "etag-43");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.LastRequest!.ExistingContainerTypeId.Should().Be(ContainerTypeId);
        provisioner.LastRequest.RootContainerCreationInDoubt.Should().BeFalse(
            "a container that has not appeared within the window was not created — the provisioner may create one");
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(RootContainerId);
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.RootContainerInDoubtSince.Should().BeNull();
    }

    [Fact]
    public async Task AC44_AnAdoptedRootContainer_AndEveryFurtherContainerInTheType_AreBound_BeforeTheHandOff()
    {
        const string extra = "b!eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        var run = BuildRun();
        run.InterStepState.ContainerTypeId = ContainerTypeId;
        var repo = new FakeRepository(run, etag: "etag-44");
        var order = new List<string>();
        var provisioner = FakeProvisioner.Sequence(new SpeContainerTypeProvisionOutcome[]
        {
            new SpeContainerTypeProvisionOutcome.Success(new SpeContainerTypeProvisionOutputs(
                ContainerTypeId, RootContainerId, AdditionalContainerIds: new[] { extra }, Adopted: true)),
        });
        var kvWriter = FakeKvWriter.Wrote(order);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), kvWriter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.BindRequests.Select(b => b.ContainerId).Should().Equal(RootContainerId, extra);
        provisioner.BindRequests.Should().OnlyContain(b => b.BusinessUnitId == RootBusinessUnitId,
            "every container in the customer's own type belongs to the environment's root unit");
        kvWriter.CallCount.Should().Be(1);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(RootContainerId);
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.AdditionalContainerIds.Should().BeNull();
        repo.LastWrittenRun.InterStepState.SpeContainerCreation.Status.Should().Be(SpeContainerCreationRecord.StatusBound);
        repo.LastWrittenRun.InterStepState.SpeContainerCreation.OwningBusinessUnitId.Should().Be(RootBusinessUnitId.ToString("D"));
    }

    [Fact]
    public async Task AC45_AFurtherContainerThatCanBeNeitherBoundNorRemoved_StaysOnRecord_QuarantinesTheRun_AndIsBoundOnResume()
    {
        const string extra = "b!eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        var run = BuildRun();
        run.InterStepState.ContainerTypeId = ContainerTypeId;
        var repo = new FakeRepository(run, etag: "etag-45");
        var provisioner = FakeProvisioner.Sequence(
            new SpeContainerTypeProvisionOutcome[]
            {
                new SpeContainerTypeProvisionOutcome.Success(new SpeContainerTypeProvisionOutputs(
                    ContainerTypeId, RootContainerId, AdditionalContainerIds: new[] { extra }, Adopted: true)),
            },
            new SpeContainerBindOutcome[]
            {
                new SpeContainerBindOutcome.Bound(),
                new SpeContainerBindOutcome.NotBound("the stamp did not read back; removing it also failed", Removed: false),
                new SpeContainerBindOutcome.Bound(),
                new SpeContainerBindOutcome.Bound(),
            });
        var kvWriter = FakeKvWriter.Wrote();
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), kvWriter);

        var first = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = first.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.ContainerBindingFailedNotRemoved);
        failure.Diagnostic.Should().Contain(extra);
        kvWriter.CallCount.Should().Be(0, "nothing durable consumes the root while a container of the run is unbound");
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().BeNull();
        repo.LastWrittenRun.InterStepState.SpeContainerCreation!.AdditionalContainerIds.Should().Equal(extra);

        var resumed = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        resumed.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(1, "the root is recorded — nothing is created or listed again");
        provisioner.BindRequests.Select(b => b.ContainerId).Should().Equal(RootContainerId, extra, RootContainerId, extra);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(RootContainerId);
    }

    [Fact]
    public async Task AC46_AContainerTypePostWithNoAnswer_IsQuarantined_AndNoTypeIsCreatedUntilAnOperatorClearsIt()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-46");
        var provisioner = FakeProvisioner.Sequence(new SpeContainerTypeProvisionOutcome[]
        {
            new SpeContainerTypeProvisionOutcome.Failure("Graph ContainerTypePost got no answer: TaskCanceledException",
                IsDelegatedTokenTrap: false, ContainerTypeInDoubt: true),
            new SpeContainerTypeProvisionOutcome.Success(new SpeContainerTypeProvisionOutputs(ContainerTypeId, RootContainerId)),
        });
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var first = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = first.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.ContainerTypeCreationInDoubt);
        failure.Diagnostic.Should().Contain("containerTypes").And.Contain(OwningAppId).And.Contain("clear-quarantine");
        repo.LastWrittenRun!.InterStepState.SpeContainerCreation!.ContainerTypeInDoubtSince.Should().NotBeNull();

        // A re-entry before any operator acted (a re-delivered dispatch) creates nothing.
        var again = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);
        again.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode
            .Should().Be(SpeContainerTypeRejectionCodes.ContainerTypeCreationInDoubt);
        provisioner.CallCount.Should().Be(1, "a container type may exist unnamed — H8 never creates a second one blindly");

        // The operator checks (no type exists), clears the quarantine and resumes.
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
        provisioner.LastRequest!.ExistingContainerTypeId.Should().BeNull();
        repo.LastWrittenRun!.InterStepState.SpeContainerCreation!.ContainerTypeInDoubtSince.Should().BeNull();
    }

    [Fact]
    public async Task AC47_AContainerTypeInDoubt_ThatTheOperatorFoundAndRecorded_IsResumedInThatType()
    {
        var run = BuildRun();
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord
        {
            ContainerTypeInDoubtSince = DateTimeOffset.UtcNow.AddHours(-1),
            Status = SpeContainerCreationRecord.StatusContainerTypeInDoubt,
        };
        run.InterStepState.ContainerTypeId = ContainerTypeId; // the operator found the orphan type and recorded it
        var repo = new FakeRepository(run, etag: "etag-47");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.LastRequest!.ExistingContainerTypeId.Should().Be(ContainerTypeId, "the found type is used, never a second one");
        repo.LastWrittenRun!.InterStepState.SpeContainerCreation!.ContainerTypeInDoubtSince.Should().BeNull();
    }

    [Fact]
    public async Task AC51_AnH8QuarantineClearedBeforeTheTypeWentInDoubt_IsNotTheOperatorsCheck_NoTypeIsCreated()
    {
        // Owner round 57 item 3 (VL7): only a clearance AT OR AFTER the moment the type went in doubt is the operator's
        // acknowledgement. An earlier H8 quarantine that an operator cleared (here: an hour before the container-type POST
        // got no answer) says nothing about the type in doubt — H8 must still refuse to create a type.
        var run = BuildRun();
        var typeInDoubtSince = DateTimeOffset.UtcNow.AddHours(-1);
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord
        {
            ContainerTypeInDoubtSince = typeInDoubtSince,
            Status = SpeContainerCreationRecord.StatusContainerTypeInDoubt,
        };
        run.Quarantine = new QuarantineInfo
        {
            State = QuarantineState.Cleared,
            Reason = "an earlier H8 quarantine",
            QuarantinedByHandler = H8SpeContainerTypeHandler.HandlerIdentifier,
            QuarantinedAt = typeInDoubtSince.AddHours(-2),
            ClearedAt = typeInDoubtSince.AddHours(-1),
            ClearedBy = "operator-oid",
        };
        var repo = new FakeRepository(run, etag: "etag-51");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.ContainerTypeCreationInDoubt);
        provisioner.CallCount.Should().Be(0, "a clearance older than the doubt is not the operator's check — no type is created");
        repo.LastWrittenRun!.InterStepState.SpeContainerCreation!.ContainerTypeInDoubtSince.Should().NotBeNull();
    }

    [Fact]
    public async Task AC48_APreRound41HandOff_NextToADifferentTypedRoot_IsBoundToo_NeverOrphaned()
    {
        const string legacy = "b!llllllllllllllllllllllllllllllllllllllllllllllllllllllllllllll";
        var run = BuildRun();
        run.InterStepState.ContainerTypeId = ContainerTypeId;
        run.InterStepState.SpeContainerId = legacy;
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord { RootContainerId = RootContainerId };
        var repo = new FakeRepository(run, etag: "etag-48");
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId);
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(0);
        provisioner.BindRequests.Select(b => b.ContainerId).Should().Equal(RootContainerId, legacy);
        repo.LastWrittenRun!.InterStepState.SpeContainerId.Should().Be(RootContainerId);
    }

    [Fact]
    public async Task AC49_ARemovedRootContainer_IsDroppedFromTheRecord_EvenWhenTheRemovalWriteMeetsAConcurrentWrite()
    {
        var run = BuildRun();
        var concurrent = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-49",
            scripted: (call, written) =>
            {
                if (call != 2)
                {
                    return null;
                }

                // The operator's concurrent write lands on the document that ALREADY records the root container.
                concurrent.InterStepState.ContainerTypeId = ContainerTypeId;
                concurrent.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord { RootContainerId = RootContainerId };
                concurrent.GateStates["operator-note"] = new GateEntry { Status = GateState.Verified, VerifierHandler = "operator" };
                return new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(concurrent, "etag-49-operator"));
            });
        var provisioner = FakeProvisioner.Success(ContainerTypeId, RootContainerId,
            bindOutcome: new SpeContainerBindOutcome.NotBound("the stamp did not read back — removed", Removed: true));
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode
            .Should().Be(SpeContainerTypeRejectionCodes.ContainerBindingFailed);
        H8SpeContainerTypeHandler.ReadRecordedCreation(repo.LastWrittenRun!).RootContainerId.Should().BeNull(
            "the removed container is dropped from the record, merged over the concurrent write");
        repo.LastWrittenRun!.GateStates.Should().ContainKey("operator-note");
        repo.LastWrittenRun.InterStepState.ContainerTypeId.Should().Be(ContainerTypeId);
    }

    [Fact]
    public async Task AC50_AFailureRecordThatCannotBePersisted_IsQuarantined_NamingTheTypeInDoubt()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-50",
            scripted: (call, _) => call == 1 ? new ReplaceRunResult.NotFound() : null);
        var provisioner = FakeProvisioner.Sequence(new SpeContainerTypeProvisionOutcome[]
        {
            new SpeContainerTypeProvisionOutcome.Failure("Graph ContainerTypePost got no answer", IsDelegatedTokenTrap: false,
                ContainerTypeInDoubt: true),
        });
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified("active"), FakeKvWriter.Wrote());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SpeContainerTypeRejectionCodes.CreationRecordNotPersisted);
        failure.Diagnostic.Should().Contain("may have created a container type");
    }

    // ---------- helpers ----------

    private static H8SpeContainerTypeHandler BuildHandler(
        IProvisioningRunRepository repo,
        ISpeContainerTypeProvisioner provisioner,
        ISpeContainerVerifier verifier,
        ISpeContainerIdKvWriter kvWriter,
        IDataverseRootBusinessUnitReader? rootReader = null)
    {
        return new H8SpeContainerTypeHandler(
            repo, provisioner, verifier, kvWriter,
            rootReader ?? FakeRootBusinessUnitReader.Returns(RootBusinessUnitId),
            Options.Create(new SpeContainerTypeOptions()),
            NullLogger<H8SpeContainerTypeHandler>.Instance);
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H8SpeContainerTypeHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(string? provisionedOn = null)
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
        run.Parameters.NonSecret[H8SpeContainerTypeHandler.TenantIdParameterKey] = TenantId;
        run.Parameters.NonSecret[H8SpeContainerTypeHandler.KeyVaultNameParameterKey] = KeyVaultName;
        run.Parameters.NonSecret[H8SpeContainerTypeHandler.SubscriptionIdParameterKey] = SubscriptionId;
        run.Parameters.NonSecret[H8SpeContainerTypeHandler.SharePointDomainParameterKey] = SharePointDomain;
        run.InterStepState.BffAppRegId = OwningAppId;
        run.InterStepState.DataverseEnvUrl = DataverseEnvUrl;
        if (provisionedOn is not null)
        {
            run.Parameters.NonSecret[H8SpeContainerTypeHandler.ProvisionedOnParameterKey] = provisionedOn;
        }
        return run;
    }

    // ---------- fakes ----------

    /// <summary>
    /// A repository that persists like Cosmos (owner round 49 item 2): it stores the JSON the PRODUCTION serializer writes
    /// (<see cref="ProductionCosmosSerializer"/> — the SDK serializer CosmosModule.BuildCosmosClient configures) and every
    /// read hands back a FRESH object deserialized from it. A field that does not survive Cosmos therefore does not survive
    /// here either — the round-41 fake handed back the same in-memory object and could not show that the resume record,
    /// kept in JsonElement evidence, came back as <c>{"valueKind":1}</c>.
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

    private sealed class FakeProvisioner : ISpeContainerTypeProvisioner
    {
        private readonly SpeContainerTypeProvisionOutcome? _outcome;
        private readonly Exception? _throwOnCall;
        private readonly SpeContainerBindOutcome _bindOutcome;
        private readonly Exception? _bindThrows;
        private readonly List<string>? _order;
        private readonly Queue<SpeContainerTypeProvisionOutcome>? _outcomes;
        private readonly Queue<SpeContainerBindOutcome>? _bindOutcomes;
        public int CallCount { get; private set; }
        public SpeContainerTypeProvisionRequest? LastRequest { get; private set; }
        public int BindCallCount { get; private set; }
        public SpeContainerBindRequest? LastBindRequest { get; private set; }

        private FakeProvisioner(
            SpeContainerTypeProvisionOutcome? outcome, Exception? throwOnCall,
            SpeContainerBindOutcome? bindOutcome = null, Exception? bindThrows = null, List<string>? order = null)
        {
            _outcome = outcome;
            _throwOnCall = throwOnCall;
            _bindOutcome = bindOutcome ?? new SpeContainerBindOutcome.Bound();
            _bindThrows = bindThrows;
            _order = order;
        }

        public static FakeProvisioner Success(
            string containerTypeId, string rootContainerId, List<string>? order = null,
            SpeContainerBindOutcome? bindOutcome = null, Exception? bindThrows = null)
            => new(new SpeContainerTypeProvisionOutcome.Success(
                new SpeContainerTypeProvisionOutputs(containerTypeId, rootContainerId)), null, bindOutcome, bindThrows, order);

        public static FakeProvisioner Failure(string diagnostic, bool isDelegatedTokenTrap)
            => new(new SpeContainerTypeProvisionOutcome.Failure(diagnostic, isDelegatedTokenTrap), null);

        public static FakeProvisioner Throws(Exception ex) => new(null, ex);

        /// <summary>One provision outcome (and optionally one bind outcome) per call, in order — for resume tests.</summary>
        public static FakeProvisioner Sequence(
            SpeContainerTypeProvisionOutcome[] outcomes, SpeContainerBindOutcome[]? binds = null)
            => new(outcomes, binds);

        private FakeProvisioner(SpeContainerTypeProvisionOutcome[] outcomes, SpeContainerBindOutcome[]? binds)
            : this((SpeContainerTypeProvisionOutcome?)null, null)
        {
            _outcomes = new Queue<SpeContainerTypeProvisionOutcome>(outcomes);
            _bindOutcomes = binds is null ? null : new Queue<SpeContainerBindOutcome>(binds);
        }

        /// <summary>Every provision request, in order.</summary>
        public List<SpeContainerTypeProvisionRequest> Requests { get; } = new();

        /// <summary>Every bind request, in order.</summary>
        public List<SpeContainerBindRequest> BindRequests { get; } = new();

        public Task<SpeContainerTypeProvisionOutcome> ProvisionAsync(
            SpeContainerTypeProvisionRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            Requests.Add(request);
            _order?.Add("provision");
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_outcomes is not null ? _outcomes.Dequeue() : _outcome!);
        }

        public Task<SpeContainerBindOutcome> BindRootContainerAsync(SpeContainerBindRequest request, CancellationToken ct)
        {
            BindCallCount++;
            LastBindRequest = request;
            BindRequests.Add(request);
            _order?.Add("bind");
            if (_bindThrows is not null) throw _bindThrows;
            return Task.FromResult(_bindOutcomes is { Count: > 0 } ? _bindOutcomes.Dequeue() : _bindOutcome);
        }
    }

    private sealed class FakeRootBusinessUnitReader : IDataverseRootBusinessUnitReader
    {
        private readonly Guid? _root;
        private readonly Exception? _throws;
        public int CallCount { get; private set; }
        public string? LastEnvironmentUrl { get; private set; }
        public string? LastTenantId { get; private set; }

        private FakeRootBusinessUnitReader(Guid? root, Exception? throws)
        {
            _root = root;
            _throws = throws;
        }

        public static FakeRootBusinessUnitReader Returns(Guid? root) => new(root, null);
        public static FakeRootBusinessUnitReader Throws(Exception ex) => new(null, ex);

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

        public static FakeVerifier Verified(string status, List<string>? order = null)
            => new(new SpeContainerVerificationResult.Verified(status), null, order);

        public static FakeVerifier NotVerified(string diagnostic, bool isDelegatedTokenTrap)
            => new(new SpeContainerVerificationResult.NotVerified(diagnostic, isDelegatedTokenTrap), null);

        public static FakeVerifier ReplicationPending(string diagnostic)
            => new(new SpeContainerVerificationResult.ReplicationPending(diagnostic), null);

        public static FakeVerifier Throws(Exception ex) => new(null, ex);

        /// <summary>One result (or exception) per call, in order — for resume tests.</summary>
        public static FakeVerifier Sequence(params object[] steps) => new(null, steps);

        /// <summary>As <see cref="Sequence(object[])"/>, recording "verify" into <paramref name="order"/>.</summary>
        public static FakeVerifier SequenceInOrder(List<string> order, params object[] steps) => new(order, steps);

        private FakeVerifier(List<string>? order, object[] steps)
            : this(null, null, order)
        {
            _sequence = new Queue<object>(steps);
        }

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

    private sealed class FakeKvWriter : ISpeContainerIdKvWriter
    {
        private readonly SpeContainerIdKvWriteResult? _result;
        private readonly Exception? _throwOnCall;
        private readonly List<string>? _order;
        public int CallCount { get; private set; }
        public SpeContainerIdKvWriteRequest? LastRequest { get; private set; }

        private FakeKvWriter(SpeContainerIdKvWriteResult? result, Exception? throwOnCall, List<string>? order = null)
        {
            _result = result;
            _throwOnCall = throwOnCall;
            _order = order;
        }

        public static FakeKvWriter Wrote(List<string>? order = null) => new(new SpeContainerIdKvWriteResult.Wrote(), null, order);
        public static FakeKvWriter Failure(string diagnostic)
            => new(new SpeContainerIdKvWriteResult.Failure(diagnostic), null);
        public static FakeKvWriter Throws(Exception ex) => new(null, ex);

        public Task<SpeContainerIdKvWriteResult> WriteAsync(
            SpeContainerIdKvWriteRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            _order?.Add("kv");
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_result!);
        }
    }
}
