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
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
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

        provisioner.CallCount.Should().Be(1);
        verifier.CallCount.Should().Be(1);
        kvWriter.CallCount.Should().Be(1);

        provisioner.LastRequest.Should().NotBeNull();
        provisioner.LastRequest!.OwningAppId.Should().Be(OwningAppId);
        provisioner.LastRequest.TenantId.Should().Be(TenantId);

        verifier.LastRequest!.ContainerId.Should().Be(RootContainerId);
        kvWriter.LastRequest!.ContainerTypeId.Should().Be(ContainerTypeId);
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
        var verifier = FakeVerifier.NotVerified("GET returned 404 — propagation lag", isDelegatedTokenTrap: false);
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

    // ---------- helpers ----------

    private static H8SpeContainerTypeHandler BuildHandler(
        IProvisioningRunRepository repo,
        ISpeContainerTypeProvisioner provisioner,
        ISpeContainerVerifier verifier,
        ISpeContainerIdKvWriter kvWriter)
    {
        return new H8SpeContainerTypeHandler(
            repo, provisioner, verifier, kvWriter,
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
        if (provisionedOn is not null)
        {
            run.Parameters.NonSecret[H8SpeContainerTypeHandler.ProvisionedOnParameterKey] = provisionedOn;
        }
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

    private sealed class FakeProvisioner : ISpeContainerTypeProvisioner
    {
        private readonly SpeContainerTypeProvisionOutcome? _outcome;
        private readonly Exception? _throwOnCall;
        public int CallCount { get; private set; }
        public SpeContainerTypeProvisionRequest? LastRequest { get; private set; }

        private FakeProvisioner(SpeContainerTypeProvisionOutcome? outcome, Exception? throwOnCall)
        {
            _outcome = outcome;
            _throwOnCall = throwOnCall;
        }

        public static FakeProvisioner Success(string containerTypeId, string rootContainerId)
            => new(new SpeContainerTypeProvisionOutcome.Success(
                new SpeContainerTypeProvisionOutputs(containerTypeId, rootContainerId)), null);

        public static FakeProvisioner Failure(string diagnostic, bool isDelegatedTokenTrap)
            => new(new SpeContainerTypeProvisionOutcome.Failure(diagnostic, isDelegatedTokenTrap), null);

        public static FakeProvisioner Throws(Exception ex) => new(null, ex);

        public Task<SpeContainerTypeProvisionOutcome> ProvisionAsync(
            SpeContainerTypeProvisionRequest request, CancellationToken ct)
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

        public static FakeVerifier NotVerified(string diagnostic, bool isDelegatedTokenTrap)
            => new(new SpeContainerVerificationResult.NotVerified(diagnostic, isDelegatedTokenTrap), null);

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

    private sealed class FakeKvWriter : ISpeContainerIdKvWriter
    {
        private readonly SpeContainerIdKvWriteResult? _result;
        private readonly Exception? _throwOnCall;
        public int CallCount { get; private set; }
        public SpeContainerIdKvWriteRequest? LastRequest { get; private set; }

        private FakeKvWriter(SpeContainerIdKvWriteResult? result, Exception? throwOnCall)
        {
            _result = result;
            _throwOnCall = throwOnCall;
        }

        public static FakeKvWriter Wrote() => new(new SpeContainerIdKvWriteResult.Wrote(), null);
        public static FakeKvWriter Failure(string diagnostic)
            => new(new SpeContainerIdKvWriteResult.Failure(diagnostic), null);
        public static FakeKvWriter Throws(Exception ex) => new(null, ex);

        public Task<SpeContainerIdKvWriteResult> WriteAsync(
            SpeContainerIdKvWriteRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_result!);
        }
    }
}
