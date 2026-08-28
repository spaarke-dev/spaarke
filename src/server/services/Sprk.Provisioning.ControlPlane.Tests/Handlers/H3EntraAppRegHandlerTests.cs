// -----------------------------------------------------------------------------
// H3EntraAppRegHandlerTests.cs
//
// Unit tests over H3EntraAppRegHandler. REWRITTEN task 130 (Wave G-3, xhigh)
// for the Graph SDK port + Model 1/Model 2 tenancy branch + real consent
// verifier + deferred-KV-write ordering + BFF-API-ClientId/Audience
// RunParameters.Secrets writes.
//
// ADR-038 CATEGORY: Path #1 — pure C# unit test. NO live Graph / Azure API.
// Fakes replace the repository + both collaborator seams (provisioner,
// consent verifier) so the handler orchestration logic is exercised in
// isolation. Live-Graph coverage belongs in env-guarded smoke tests (H3's
// Graph/KV collaborators are NOT unit-tested — see
// GraphAppRegistrationProvisioner.cs file header for the established
// project precedent this follows).
//
// COVERAGE MAP:
//   AC-M2-1  Model 2 happy path (Verified) — Success + CompletedPhase(H3) +
//            bffAppRegId + 3 RunParameters.Secrets entries + admin-consent
//            gate Verified + CommitPendingSecretsAsync called ONCE with the
//            3 staged writes (AFTER consent verified — DS-4 §3 ordering).
//   AC-M1-1  Model 1 happy path — VerifySharedAsync=Current + consent
//            Verified → Success; ProvisionAsync NEVER called (0 new app-reg /
//            0 new FIC); CommitPendingSecretsAsync NEVER called (nothing to
//            commit — Model 1 only references pre-existing shared entries).
//   AC-I6-1  Missing tenancyModel → Resumable MissingOrInvalidTenancyModel;
//            provisioner never touched (I6 fires before any branch logic).
//   AC-I6-2  Unrecognized tenancyModel value → same as AC-I6-1 (no silent
//            default to either branch).
//   AC-2     Missing tenantId (§4D I1).
//   AC-3     S2S accidental provisioning (interStepState pre-populated).
//   AC-4     Cleartext-secret-leak.
//   AC-5     Admin-consent Pending → WaitingOnGate; CommitPendingSecretsAsync
//            NOT called (KV writes must not happen before consent verified —
//            the specific defect DS-4 §3's ordering constraint prevents).
//   AC-6     Verifier throws (simulating ODataError bubble) → Resumable, not
//            Quarantined.
//   AC-7     Idempotency: second invocation short-circuits.
//   AC-9     Model 2 missing keyVaultName.
//   AC-10    ExpectedDelegatedScopeCount <= 0.
//   AC-11    Provisioner returns Failure.
//   AC-12    Provisioner returns Success with blank BffAppRegId.
//   AC-13    HandlerId mismatch — throws.
//   AC-14    Idempotency key format determinism.
//   AC-15    Run not found.
//   AC-16    KV URI-ref format is safe (cleartext scanner short-circuits).
//   AC-M2-2  Missing InterStepState.MiObjectId (Model 2 FIC subject) →
//            Resumable MissingUamiObjectId; provisioner never called.
//   AC-M1-2  Missing shared app-reg config (Model 1) → Resumable
//            MissingSharedAppRegConfig; VerifySharedAsync never called.
//   AC-M1-3  Shared app-reg Drifted → Resumable SharedAppRegConfigurationDrift.
//   AC-KV-1  Deferred KV commit fails AFTER consent verified → QuarantineRequired
//            (app-reg + consent both real; KV state now ambiguous).
//   AC-PARSE KV URI reference round-trip parse (vault, secretName).
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H3EntraAppRegHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h3-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string KeyVaultName = "sprk-acme-prod-kv";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string UamiObjectId = "ffffffff-1111-2222-3333-000000000000";
    private const string SharedAppId = "shared-app-id-0000-0000-000000000000";
    private const string SharedPlatformKv = "sprk-platform-prod-kv";
    private const int ExpectedScopeCount = 5;

    private static readonly string ExpectedKvUriRef =
        GraphAppRegistrationProvisioner.BuildKvUriReference(KeyVaultName, GraphAppRegistrationProvisioner.ClientSecretName);
    private static readonly string ExpectedSharedKvUriRef =
        GraphAppRegistrationProvisioner.BuildKvUriReference(SharedPlatformKv, GraphAppRegistrationProvisioner.ClientSecretName);

    // ---------- AC-M2-1 Model 2 happy path ----------

    [Fact]
    public async Task AcM2_1_Model2HappyPath_ConsentVerified_CommitsKvAfterConsent()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model2Dedicated);
        var repo = new FakeRepository(run, etag: "etag-1");
        var pendingWrites = BuildPendingWrites();
        var provisioner = FakeProvisioner.Success(BuildOutputs(pendingWrites));
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H3EntraAppRegHandler.BuildIdempotencyKey(CustomerId, TenantId));

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H3");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle().Which.Phase.Should().Be("H3");
        repo.LastWrittenRun.InterStepState.BffAppRegId.Should().Be(BffAppRegId);
        repo.LastWrittenRun.InterStepState.S2SAppRegId.Should().BeNull();
        repo.LastWrittenRun.GateStates.Should().ContainKey(EntraAppRegGates.AdminConsent)
            .WhoseValue.Status.Should().Be(GateState.Verified);

        // BFF-API-ClientId / Audience / ClientSecret all referenced (task 129 contract).
        repo.LastWrittenRun.Parameters.Secrets.Should().ContainKey(GraphAppRegistrationProvisioner.ClientIdSecretName)
            .WhoseValue.Should().Be(new KeyVaultSecretRef(KeyVaultName, GraphAppRegistrationProvisioner.ClientIdSecretName));
        repo.LastWrittenRun.Parameters.Secrets.Should().ContainKey(GraphAppRegistrationProvisioner.AudienceSecretName)
            .WhoseValue.Should().Be(new KeyVaultSecretRef(KeyVaultName, GraphAppRegistrationProvisioner.AudienceSecretName));
        repo.LastWrittenRun.Parameters.Secrets.Should().ContainKey(GraphAppRegistrationProvisioner.ClientSecretName)
            .WhoseValue.Should().Be(new KeyVaultSecretRef(KeyVaultName, GraphAppRegistrationProvisioner.ClientSecretName));

        provisioner.ProvisionCallCount.Should().Be(1);
        provisioner.CommitCallCount.Should().Be(1, "KV writes commit exactly once, AFTER consent is verified");
        provisioner.LastCommittedWrites.Should().BeEquivalentTo(pendingWrites);
        provisioner.VerifySharedCallCount.Should().Be(0, "Model 2 never calls the Model-1-only verification path");
        verifier.CallCount.Should().Be(1);
        verifier.LastBffAppRegId.Should().Be(BffAppRegId);
        verifier.LastTenantId.Should().Be(TenantId);
        verifier.LastExpectedScopeCount.Should().Be(ExpectedScopeCount);

        // Request threaded to the provisioner carried the UAMI principalId (FIC subject) + profile.
        provisioner.LastProvisionRequest!.UamiPrincipalId.Should().Be(UamiObjectId);
        provisioner.LastProvisionRequest.Profile.Should().Be("spaarke-hosted-model2");
    }

    // ---------- AC-M1-1 Model 1 happy path ----------

    [Fact]
    public async Task AcM1_1_Model1HappyPath_NoNewAppRegOrFic_NoKvWrites()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model1Shared, includeKvName: false, includeUamiObjectId: false);
        var repo = new FakeRepository(run, etag: "etag-m1");
        var provisioner = FakeProvisioner.SharedCurrent();
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier,
            sharedAppId: SharedAppId, sharedKv: SharedPlatformKv);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.ProvisionCallCount.Should().Be(0, "Model 1 MUST create ZERO new app-reg objects");
        provisioner.CommitCallCount.Should().Be(0, "Model 1 has nothing to commit — references only");
        provisioner.VerifySharedCallCount.Should().Be(1);
        provisioner.LastVerifySharedRequest!.SharedAppId.Should().Be(SharedAppId);

        repo.LastWrittenRun!.InterStepState.BffAppRegId.Should().Be(SharedAppId);
        repo.LastWrittenRun.Parameters.Secrets[GraphAppRegistrationProvisioner.ClientIdSecretName]
            .Should().Be(new KeyVaultSecretRef(SharedPlatformKv, GraphAppRegistrationProvisioner.ClientIdSecretName));
        repo.LastWrittenRun.Parameters.Secrets[GraphAppRegistrationProvisioner.AudienceSecretName]
            .Should().Be(new KeyVaultSecretRef(SharedPlatformKv, GraphAppRegistrationProvisioner.AudienceSecretName));
        repo.LastWrittenRun.Parameters.Secrets[GraphAppRegistrationProvisioner.ClientSecretName]
            .Should().Be(new KeyVaultSecretRef(SharedPlatformKv, GraphAppRegistrationProvisioner.ClientSecretName));

        verifier.LastBffAppRegId.Should().Be(SharedAppId, "Model 1 still verifies THIS customer tenant's consent for the SHARED app");
    }

    // ---------- AC-I6 tenancy model I6 enforcement ----------

    [Fact]
    public async Task AcI6_1_MissingTenancyModel_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun(tenancyModel: null!);
        var repo = new FakeRepository(run, etag: "etag-i6-1");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.MissingOrInvalidTenancyModel);
        provisioner.ProvisionCallCount.Should().Be(0);
        provisioner.VerifySharedCallCount.Should().Be(0);
        verifier.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AcI6_2_UnrecognizedTenancyModel_FailsResumable_NoSilentDefault()
    {
        var run = BuildRun(tenancyModel: "SomeFutureModel");
        var repo = new FakeRepository(run, etag: "etag-i6-2");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.MissingOrInvalidTenancyModel);
        provisioner.ProvisionCallCount.Should().Be(0);
        provisioner.VerifySharedCallCount.Should().Be(0);
    }

    // ---------- AC-2 missing tenantId ----------

    [Fact]
    public async Task Ac2_MissingTenantId_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model2Dedicated, includeTenantId: false);
        var repo = new FakeRepository(run, etag: "etag-2");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.MissingTenantId);
        failure.Diagnostic.Should().Contain("§4D I1");
        provisioner.ProvisionCallCount.Should().Be(0);
        verifier.CallCount.Should().Be(0);
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- AC-3 S2S accidental provisioning ----------

    [Fact]
    public async Task Ac3_S2SAppRegAlreadyPresent_FailsQuarantineRequired()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model2Dedicated);
        run.InterStepState.S2SAppRegId = "phantom-s2s-app-reg-id";
        var repo = new FakeRepository(run, etag: "etag-3");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.S2SAppRegForbidden);
        failure.Diagnostic.Should().Contain("r3 task 060");
        provisioner.ProvisionCallCount.Should().Be(1, "provisioner ran but the S2S guard trips post-provision");
        provisioner.CommitCallCount.Should().Be(0, "guard trips before consent is even checked");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- AC-4 cleartext-secret leak ----------

    [Fact]
    public async Task Ac4_CleartextSecretLeak_InProvisionerOutput_FailsQuarantineRequired()
    {
        var leakyOutputs = new EntraAppRegOutputs
        {
            BffAppRegId = BffAppRegId,
            BffClientSecretKvUri = "Nx8Q~aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789.-_",
            PendingKvWrites = BuildPendingWrites(),
        };
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model2Dedicated);
        var repo = new FakeRepository(run, etag: "etag-4");
        var provisioner = FakeProvisioner.Success(leakyOutputs);
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.CleartextSecretLeak);
        failure.Diagnostic.Should().Contain("ADR-028");
        verifier.CallCount.Should().Be(0, "leak guard trips BEFORE consent verification");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.InterStepState.BffAppRegId.Should().BeNull();
    }

    // ---------- AC-5 admin-consent Pending -> WaitingOnGate; NO KV commit ----------

    [Fact]
    public async Task Ac5_ConsentPending_TransitionsToWaitingOnGate_DoesNotCommitKv()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model2Dedicated);
        var repo = new FakeRepository(run, etag: "etag-5");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var verifier = FakeVerifier.Pending(0, ExpectedScopeCount,
            "Tenant admin has not yet granted consent for the 5 delegated scopes.");
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H3EntraAppRegHandler.BuildIdempotencyKey(CustomerId, TenantId));

        repo.LastWrittenRun!.Status.Should().Be(RunStatus.WaitingOnGate);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H3");
        repo.LastWrittenRun.CompletedPhases.Should().BeEmpty();
        repo.LastWrittenRun.InterStepState.BffAppRegId.Should().Be(BffAppRegId);
        repo.LastWrittenRun.Parameters.Secrets.Should().BeEmpty(
            "BFF-API-* refs are only written on the Verified path — never while consent is Pending");
        var gate = repo.LastWrittenRun.GateStates[EntraAppRegGates.AdminConsent];
        gate.Status.Should().Be(GateState.Pending);
        gate.VerifierHandler.Should().Be("H3");

        provisioner.CommitCallCount.Should().Be(0,
            "DS-4 §3 BINDING: KV writes MUST NOT happen before consent gate genuinely passes");
    }

    // ---------- AC-6 verifier throws ----------

    [Fact]
    public async Task Ac6_VerifierThrowsUnexpected_FailsResumable_NotQuarantined()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model2Dedicated);
        var repo = new FakeRepository(run, etag: "etag-6");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var verifier = FakeVerifier.Throws(new InvalidOperationException("Graph ODataError bubbled up: 503 Service Unavailable"));
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.ProvisioningFailed);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
        provisioner.CommitCallCount.Should().Be(0);
    }

    // ---------- AC-7 idempotency ----------

    [Fact]
    public async Task Ac7_Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model2Dedicated);
        var expectedKey = H3EntraAppRegHandler.BuildIdempotencyKey(CustomerId, TenantId);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H3",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-7");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        provisioner.ProvisionCallCount.Should().Be(0);
        verifier.CallCount.Should().Be(0);
    }

    // ---------- AC-9 Model 2 missing keyVaultName ----------

    [Fact]
    public async Task Ac9_Model2MissingKeyVaultName_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model2Dedicated, includeKvName: false);
        var repo = new FakeRepository(run, etag: "etag-9");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.MissingKeyVaultName);
        provisioner.ProvisionCallCount.Should().Be(0);
    }

    // ---------- AC-10 ExpectedDelegatedScopeCount <= 0 ----------

    [Fact]
    public async Task Ac10_ExpectedScopeCountZero_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model2Dedicated);
        var repo = new FakeRepository(run, etag: "etag-10");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier, expectedScopeCount: 0);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.NullAppRoleIdInCatalog);
        provisioner.ProvisionCallCount.Should().Be(0);
    }

    // ---------- AC-11 provisioner returns Failure ----------

    [Fact]
    public async Task Ac11_ProvisionerReturnsFailure_FailsResumable()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model2Dedicated);
        var repo = new FakeRepository(run, etag: "etag-11");
        var provisioner = FakeProvisioner.Failure("Graph ODataError 403 InsufficientPrivileges");
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.ProvisioningFailed);
        failure.Diagnostic.Should().Contain("InsufficientPrivileges");
        verifier.CallCount.Should().Be(0);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- AC-12 provisioner returns Success with blank BffAppRegId ----------

    [Fact]
    public async Task Ac12_ProvisionerReturnsBlankAppId_FailsResumable()
    {
        var incompleteOutputs = new EntraAppRegOutputs
        {
            BffAppRegId = "   ",
            BffClientSecretKvUri = ExpectedKvUriRef,
        };
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model2Dedicated);
        var repo = new FakeRepository(run, etag: "etag-12");
        var provisioner = FakeProvisioner.Success(incompleteOutputs);
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.ProvisioningOutputsIncomplete);
    }

    // ---------- AC-13 handler-id mismatch ----------

    [Fact]
    public async Task Ac13_HandlerIdMismatch_Throws()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model2Dedicated);
        var repo = new FakeRepository(run, etag: "etag-13");
        var handler = BuildHandler(repo,
            FakeProvisioner.Success(BuildOutputs(BuildPendingWrites())),
            FakeVerifier.Verified(ExpectedScopeCount));

        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H0",
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*mismatched HandlerId*");
    }

    // ---------- AC-14 idempotency key format determinism ----------

    [Fact]
    public void Ac14_IdempotencyKey_IsDeterministicByCustomerAndTenant()
    {
        var k1 = H3EntraAppRegHandler.BuildIdempotencyKey("acme", TenantId);
        var k2 = H3EntraAppRegHandler.BuildIdempotencyKey("acme", TenantId);
        k1.Should().Be(k2);
        k1.Should().Be($"appreg-acme-{TenantId}");

        var k3 = H3EntraAppRegHandler.BuildIdempotencyKey("acme", "different-tenant");
        k3.Should().NotBe(k1);
    }

    // ---------- AC-15 run not found ----------

    [Fact]
    public async Task Ac15_RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var handler = BuildHandler(repo,
            FakeProvisioner.Success(BuildOutputs(BuildPendingWrites())),
            FakeVerifier.Verified(ExpectedScopeCount));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.RunNotFound);
    }

    // ---------- AC-16 KV URI-ref format short-circuits cleartext scanner ----------

    [Fact]
    public void Ac16_IsCleartextSecretPattern_ShortCircuitsOnKvUriRef()
    {
        H3EntraAppRegHandler.IsCleartextSecretPattern(ExpectedKvUriRef).Should().BeFalse();
        H3EntraAppRegHandler.IsCleartextSecretPattern(
            "@Microsoft.KeyVault(SecretUri=https://sprk-x.vault.azure.net/secrets/BFF-API-ClientSecret/)"
        ).Should().BeFalse();
        H3EntraAppRegHandler.IsCleartextSecretPattern(
            "Nx8Q~aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789.-_"
        ).Should().BeTrue();
        H3EntraAppRegHandler.IsCleartextSecretPattern(string.Empty).Should().BeFalse();
        H3EntraAppRegHandler.IsCleartextSecretPattern("   ").Should().BeFalse();
        H3EntraAppRegHandler.IsCleartextSecretPattern("guid-like-value").Should().BeFalse();
    }

    // ---------- AC-M2-2 missing UAMI principalId (FIC subject) ----------

    [Fact]
    public async Task AcM2_2_MissingUamiObjectId_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model2Dedicated, includeUamiObjectId: false);
        var repo = new FakeRepository(run, etag: "etag-m2-2");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.MissingUamiObjectId);
        provisioner.ProvisionCallCount.Should().Be(0);
    }

    // ---------- AC-M1-2 missing shared app-reg config ----------

    [Fact]
    public async Task AcM1_2_MissingSharedAppRegConfig_FailsResumable_NoVerifyCall()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model1Shared, includeKvName: false, includeUamiObjectId: false);
        var repo = new FakeRepository(run, etag: "etag-m1-2");
        var provisioner = FakeProvisioner.SharedCurrent();
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        // No sharedAppId/sharedKv configured.
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.MissingSharedAppRegConfig);
        provisioner.VerifySharedCallCount.Should().Be(0);
    }

    // ---------- AC-M1-3 shared app-reg drift ----------

    [Fact]
    public async Task AcM1_3_SharedAppRegDrifted_FailsResumable()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model1Shared, includeKvName: false, includeUamiObjectId: false);
        var repo = new FakeRepository(run, etag: "etag-m1-3");
        var provisioner = FakeProvisioner.SharedDrifted("signInAudience mismatch");
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier, sharedAppId: SharedAppId, sharedKv: SharedPlatformKv);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.SharedAppRegConfigurationDrift);
        failure.Diagnostic.Should().Contain("signInAudience mismatch");
    }

    // ---------- AC-KV-1 deferred commit failure after consent verified ----------

    [Fact]
    public async Task AcKv_1_CommitFailsAfterConsentVerified_QuarantineRequired()
    {
        var run = BuildRun(tenancyModel: H3EntraAppRegHandler.Model2Dedicated);
        var repo = new FakeRepository(run, etag: "etag-kv-1");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        provisioner.CommitFailureDiagnostic = "SecretClient.SetSecretAsync failed: RequestFailedException 403";
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.Diagnostic.Should().Contain("RequestFailedException 403");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        provisioner.CommitCallCount.Should().Be(1);
    }

    // ---------- AC-PARSE KV URI reference round-trip ----------

    [Fact]
    public void AcParse_KvUriReference_RoundTrips()
    {
        var (vault, name) = H3EntraAppRegHandler.ParseKvUriReference(ExpectedKvUriRef);
        vault.Should().Be(KeyVaultName);
        name.Should().Be(GraphAppRegistrationProvisioner.ClientSecretName);

        var (sharedVault, sharedName) = H3EntraAppRegHandler.ParseKvUriReference(ExpectedSharedKvUriRef);
        sharedVault.Should().Be(SharedPlatformKv);
        sharedName.Should().Be(GraphAppRegistrationProvisioner.ClientSecretName);
    }

    // ---------- helpers ----------

    private static H3EntraAppRegHandler BuildHandler(
        FakeRepository repo,
        FakeProvisioner provisioner,
        FakeVerifier verifier,
        int expectedScopeCount = ExpectedScopeCount,
        string? sharedAppId = null,
        string? sharedKv = null)
    {
        var options = Options.Create(new EntraAppRegOptions
        {
            ExpectedDelegatedScopeCount = expectedScopeCount,
            SharedBffAppRegistrationId = sharedAppId,
            SharedPlatformKeyVaultName = sharedKv,
        });
        return new H3EntraAppRegHandler(
            repo, provisioner, verifier, options,
            NullLogger<H3EntraAppRegHandler>.Instance);
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H3EntraAppRegHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(
        string tenancyModel,
        bool includeTenantId = true,
        bool includeKvName = true,
        bool includeUamiObjectId = true)
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-guid",
            TenancyModel = tenancyModel,
            Status = RunStatus.Running,
            Profile = "spaarke-hosted-model2",
        };
        if (includeTenantId)
        {
            run.Parameters.NonSecret[H3EntraAppRegHandler.TenantIdParameterKey] = TenantId;
        }
        if (includeKvName)
        {
            run.Parameters.NonSecret[H3EntraAppRegHandler.KeyVaultNameParameterKey] = KeyVaultName;
        }
        if (includeUamiObjectId)
        {
            run.InterStepState.MiObjectId = UamiObjectId;
        }
        return run;
    }

    private static IReadOnlyList<PendingKvSecretWrite> BuildPendingWrites() => new[]
    {
        new PendingKvSecretWrite(KeyVaultName, GraphAppRegistrationProvisioner.ClientIdSecretName, BffAppRegId),
        new PendingKvSecretWrite(KeyVaultName, GraphAppRegistrationProvisioner.AudienceSecretName, $"api://{BffAppRegId}"),
        new PendingKvSecretWrite(KeyVaultName, GraphAppRegistrationProvisioner.ClientSecretName, "super-secret-cleartext-value"),
    };

    private static EntraAppRegOutputs BuildOutputs(IReadOnlyList<PendingKvSecretWrite> pendingWrites) => new()
    {
        BffAppRegId = BffAppRegId,
        BffClientSecretKvUri = ExpectedKvUriRef,
        PendingKvWrites = pendingWrites,
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

    /// <summary>Provisioner fake — records calls + returns canned outcomes for all 3 IEntraAppRegProvisioner members.</summary>
    private sealed class FakeProvisioner : IEntraAppRegProvisioner
    {
        private readonly EntraAppRegOutcome? _provisionOutcome;
        private readonly EntraAppRegSharedVerifyOutcome? _verifySharedOutcome;

        public int ProvisionCallCount { get; private set; }
        public int VerifySharedCallCount { get; private set; }
        public int CommitCallCount { get; private set; }
        public EntraAppRegRequest? LastProvisionRequest { get; private set; }
        public EntraAppRegSharedVerifyRequest? LastVerifySharedRequest { get; private set; }
        public IReadOnlyList<PendingKvSecretWrite>? LastCommittedWrites { get; private set; }
        public string? CommitFailureDiagnostic { get; set; }

        private FakeProvisioner(EntraAppRegOutcome? provisionOutcome, EntraAppRegSharedVerifyOutcome? verifySharedOutcome)
        {
            _provisionOutcome = provisionOutcome;
            _verifySharedOutcome = verifySharedOutcome;
        }

        public static FakeProvisioner Success(EntraAppRegOutputs outputs)
            => new(new EntraAppRegOutcome.Success(outputs), null);

        public static FakeProvisioner Failure(string diagnostic)
            => new(new EntraAppRegOutcome.Failure(diagnostic), null);

        public static FakeProvisioner SharedCurrent()
            => new(null, new EntraAppRegSharedVerifyOutcome.Current());

        public static FakeProvisioner SharedDrifted(string diagnostic)
            => new(null, new EntraAppRegSharedVerifyOutcome.Drifted(diagnostic));

        public Task<EntraAppRegOutcome> ProvisionAsync(EntraAppRegRequest request, CancellationToken ct)
        {
            ProvisionCallCount++;
            LastProvisionRequest = request;
            return Task.FromResult(_provisionOutcome!);
        }

        public Task<EntraAppRegSharedVerifyOutcome> VerifySharedAsync(EntraAppRegSharedVerifyRequest request, CancellationToken ct)
        {
            VerifySharedCallCount++;
            LastVerifySharedRequest = request;
            return Task.FromResult(_verifySharedOutcome!);
        }

        public Task<string?> CommitPendingSecretsAsync(IReadOnlyList<PendingKvSecretWrite> pendingWrites, CancellationToken ct)
        {
            CommitCallCount++;
            LastCommittedWrites = pendingWrites;
            return Task.FromResult(CommitFailureDiagnostic);
        }
    }

    /// <summary>Admin-consent verifier fake.</summary>
    private sealed class FakeVerifier : IAdminConsentVerifier
    {
        private readonly Func<Task<AdminConsentVerificationResult>> _behavior;
        public int CallCount { get; private set; }
        public string? LastBffAppRegId { get; private set; }
        public string? LastTenantId { get; private set; }
        public int LastExpectedScopeCount { get; private set; }

        private FakeVerifier(Func<Task<AdminConsentVerificationResult>> behavior) => _behavior = behavior;

        public static FakeVerifier Verified(int grantedCount)
            => new(() => Task.FromResult<AdminConsentVerificationResult>(
                new AdminConsentVerificationResult.Verified(grantedCount, grantedCount, null)));

        public static FakeVerifier Pending(int grantedCount, int expectedCount, string diagnostic)
            => new(() => Task.FromResult<AdminConsentVerificationResult>(
                new AdminConsentVerificationResult.Pending(grantedCount, expectedCount, diagnostic, null)));

        public static FakeVerifier Throws(Exception ex)
            => new(() => Task.FromException<AdminConsentVerificationResult>(ex));

        public Task<AdminConsentVerificationResult> VerifyAsync(
            string bffAppRegId, string tenantId, int expectedDelegatedScopeCount, CancellationToken ct)
        {
            CallCount++;
            LastBffAppRegId = bffAppRegId;
            LastTenantId = tenantId;
            LastExpectedScopeCount = expectedDelegatedScopeCount;
            return _behavior();
        }
    }
}
