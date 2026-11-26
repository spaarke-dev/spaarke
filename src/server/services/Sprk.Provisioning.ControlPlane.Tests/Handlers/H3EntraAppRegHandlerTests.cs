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
//   AC-M2-1  Happy path (Verified) — Success + CompletedPhase(H3) +
//            bffAppRegId + 3 RunParameters.Secrets entries + admin-consent
//            gate Verified + CommitPendingSecretsAsync called ONCE with the
//            3 staged writes (AFTER consent verified — DS-4 §3 ordering).
//            (Historically labeled "Model 2 happy path"; post-task-222/D-13
//            H3 runs the same per-customer creation path for both tenancy
//            values, so this is now the sole happy path.)
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
//   AC-9     Missing InterStepState.KeyVaultName (H2a output; task 245a) →
//            Resumable MissingKeyVaultName.
//   AC-10    ExpectedDelegatedScopeCount <= 0.
//   AC-11    Provisioner returns Failure.
//   AC-12    Provisioner returns Success with blank BffAppRegId.
//   AC-13    HandlerId mismatch — throws.
//   AC-14    Idempotency key format determinism.
//   AC-15    Run not found.
//   AC-16    KV URI-ref format is safe (cleartext scanner short-circuits).
//   AC-M2-2  Missing InterStepState.MiObjectId (FIC subject) → Resumable
//            MissingUamiObjectId; provisioner never called. (Historically
//            labeled "Model 2 FIC subject"; post-task-222/D-13 the FIC
//            subject requirement applies unconditionally in both models.)
//   AC-KV-1  Deferred KV commit fails AFTER consent verified → QuarantineRequired
//            (app-reg + consent both real; KV state now ambiguous).
//   AC-PARSE KV URI reference round-trip parse (vault, secretName).
//   T240a-1  The request carries the customer's Dataverse origin as the only SPA
//            redirect and the configured shared clients (canonical, de-duplicated).
//   T240a-2  An intake URL that is missing or names another customer's environment
//            → Resumable DataverseEnvUrlInvalid; provisioner never called.
//   T240a-3  A non-GUID pre-authorized client id → Resumable
//            PreAuthorizedClientAppIdInvalid; provisioner never called.
//
// RETIRED 2026-09-29 (task 222 per D-13): the shared multitenant BFF app-reg
// branch was deleted from H3EntraAppRegHandler. The three tests that pinned
// that branch's behavior (AC-M1-1 happy path, AC-M1-2 missing shared config,
// AC-M1-3 shared drift) were deleted with it — they exercised a code path
// that no longer exists. RunsEndpointsTests continues to cover Model1Shared
// as a valid tenancy STRING (H3 routes it through the same per-customer
// creation path as Model2Dedicated post-Item-1).
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Registry;
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
    private static readonly int ExpectedScopeCount = EntraAppRegPermissionCatalog.All.Count;
    private const string DataverseEnvUrl = "https://spaarke-acme.crm.dynamics.com/";
    private const string AddInClientId = "c1258e2d-1688-49d2-ac99-a7485ebd9995";
    private const string OperatorToolsClientId = "7d0e6c1a-3b2f-4e5d-9a8b-1c2d3e4f5a6b";
    private const string EnvironmentRowId = "721bce03-13c8-f111-a05c-0022482913fc";

    private static readonly string ExpectedKvUriRef =
        GraphAppRegistrationProvisioner.BuildKvUriReference(KeyVaultName, GraphAppRegistrationProvisioner.ClientSecretName);

    // ---------- AC-M2-1 Model 2 happy path ----------

    [Fact]
    public async Task AcM2_1_Model2HappyPath_ConsentVerified_CommitsKvAfterConsent()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2));
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

        // Task 245a: H3 commits BFF-API-ClientId / BFF-API-Audience to the vault itself (asserted via
        // LastCommittedWrites below) and hands nothing on through run.Parameters.Secrets — H4 runs before
        // H3 and could never read such refs (the deadlock the old task-129 refs caused).
        repo.LastWrittenRun.Parameters.Secrets.Should().BeEmpty();

        provisioner.ProvisionCallCount.Should().Be(1);
        provisioner.CommitCallCount.Should().Be(1, "KV writes commit exactly once, AFTER consent is verified");
        provisioner.LastCommittedWrites.Should().BeEquivalentTo(pendingWrites);
        verifier.CallCount.Should().Be(1);
        verifier.LastBffAppRegId.Should().Be(BffAppRegId);
        verifier.LastTenantId.Should().Be(TenantId);
        verifier.LastExpectedScopeCount.Should().Be(ExpectedScopeCount);

        // Request threaded to the provisioner carried the UAMI principalId (FIC subject) + profile.
        provisioner.LastProvisionRequest!.UamiPrincipalId.Should().Be(UamiObjectId);
        provisioner.LastProvisionRequest.Profile.Should().Be("spaarke-hosted-model2");

        // Bucket B HIGH#3 SESSION 18 (customer-provisioning-orchestration-r1 adversarial
        // e2e verify workflow wepdcb8we) + .claude/constraints/provisioning.md § KV
        // credential lifecycle rule 1: H3 MUST request the secret-free identity path
        // on every Model 2 dispatch — BFF-API-ClientSecret was DELETED from KV by
        // auth-v4 task 033 (2026-08-24) and re-introduction is a HARD-fail contract
        // violation. This assertion locks the H3 handler's explicit-true call site
        // so a future refactor that drops the parameter or flips the DTO default
        // to false will fail the build here rather than silently re-open the
        // silent-mint path the verify workflow surfaced.
        provisioner.LastProvisionRequest.RequireSecretFreeIdentity.Should().BeTrue(
            "H3 handler MUST always request secret-free identity for Model 2 dispatches — " +
            "both spaarke-hosted-model2 and customer-owned-model2 profiles are secret-free " +
            "per ADR-028 A4. Any dispatch with RequireSecretFreeIdentity=false would let " +
            "GraphAppRegistrationProvisioner mint a BFF-API-ClientSecret contra auth-v4 §10.");
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
    }

    // ---------- AC-2 missing tenantId ----------

    [Fact]
    public async Task Ac2_MissingTenantId_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2), includeTenantId: false);
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
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2));
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
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2));
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
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2));
        var repo = new FakeRepository(run, etag: "etag-5");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var verifier = FakeVerifier.Pending(0, ExpectedScopeCount,
            "Tenant admin has not yet granted consent for the delegated scopes.");
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
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2));
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
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2));
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
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2), includeKvName: false);
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
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2));
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
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2));
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
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2));
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
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2));
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
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2), includeUamiObjectId: false);
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

    // ---------- T240a client access ----------

    [Fact]
    public async Task T240a_1_Request_CarriesTheDataverseOriginAndTheConfiguredClients()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var repo = new FakeRepository(run, etag: "etag-t240a-1");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var verifier = FakeVerifier.Verified(ExpectedScopeCount);
        var handler = BuildHandler(repo, provisioner, verifier,
            preAuthorizedClientAppIds: [AddInClientId.ToUpperInvariant(), AddInClientId]);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        var request = provisioner.LastProvisionRequest!;
        request.SpaRedirectUris.Should().Equal(["https://spaarke-acme.crm.dynamics.com"],
            "the code pages send window.location.origin — no trailing slash");
        request.PreAuthorizedClientAppIds.Should().Equal([AddInClientId],
            "client ids are canonical (lowercase) and de-duplicated");
    }

    [Fact]
    public async Task T240a_1b_EnvironmentNamedUrl_IsTheRedirect()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1),
            dataverseEnvUrl: "https://spaarke-acme-prod.crm4.dynamics.com/");
        run.Parameters.NonSecret[IntakeParameterCatalog.EnvironmentName] = "prod";
        var repo = new FakeRepository(run, etag: "etag-t240a-1b");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified(ExpectedScopeCount));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.LastProvisionRequest!.SpaRedirectUris.Should().Equal(["https://spaarke-acme-prod.crm4.dynamics.com"]);
        provisioner.LastProvisionRequest.PreAuthorizedClientAppIds.Should().BeEmpty("none configured");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://spaarke-other.crm.dynamics.com/")]
    [InlineData("http://spaarke-acme.crm.dynamics.com/")]
    public async Task T240a_2_InvalidDataverseEnvUrl_FailsResumable_NoProvisionerCall(string? dataverseEnvUrl)
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1),
            dataverseEnvUrl: dataverseEnvUrl);
        var repo = new FakeRepository(run, etag: "etag-t240a-2");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified(ExpectedScopeCount));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.DataverseEnvUrlInvalid);
        provisioner.ProvisionCallCount.Should().Be(0, "nothing is written to Entra");
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task T240a_3_InvalidPreAuthorizedClientId_FailsResumable_NoProvisionerCall(string badClientId)
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var repo = new FakeRepository(run, etag: "etag-t240a-3");
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified(ExpectedScopeCount),
            preAuthorizedClientAppIds: [AddInClientId, badClientId]);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.PreAuthorizedClientAppIdInvalid);
        provisioner.ProvisionCallCount.Should().Be(0, "nothing is written to Entra");
    }

    // ---------- Demo environment plan decision 1: Demo-only clients ----------

    [Fact]
    public async Task Demo_1_ActiveDemoRow_AddsTheDemoClient_AfterThePlatformClients()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var registry = FakeRegistry.Returning(DemoRow(environmentType: 1, isActive: true));
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-1"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), preAuthorizedClientAppIds: [AddInClientId],
            demoPreAuthorizedClientAppIds: [OperatorToolsClientId.ToUpperInvariant()], registry: registry);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.LastProvisionRequest!.PreAuthorizedClientAppIds.Should().Equal([AddInClientId, OperatorToolsClientId]);
        registry.LookedUpIds.Should().Equal([EnvironmentRowId], "H3 reads the run's own registry row by id");
    }

    public static TheoryData<int?, bool?, string> NotActiveDemoRows => new()
    {
        { 6, true, "Production" },
        { 0, true, "Development" },
        { null, true, "type unset" },
        { 1, false, "Demo but inactive" },
        { 1, null, "Demo, active unset" },
    };

    [Theory]
    [MemberData(nameof(NotActiveDemoRows))]
    public async Task Demo_2_RowThatIsNotAnActiveDemo_LeavesTheDemoClientOut(int? environmentType, bool? isActive, string because)
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-2"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), preAuthorizedClientAppIds: [AddInClientId],
            demoPreAuthorizedClientAppIds: [OperatorToolsClientId],
            registry: FakeRegistry.Returning(DemoRow(environmentType, isActive)));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.LastProvisionRequest!.PreAuthorizedClientAppIds.Should().Equal([AddInClientId], because);
    }

    [Fact]
    public async Task Demo_2b_NoRow_LeavesTheDemoClientOut()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-2b"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), demoPreAuthorizedClientAppIds: [OperatorToolsClientId],
            registry: FakeRegistry.Returning(null));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.LastProvisionRequest!.PreAuthorizedClientAppIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Demo_2c_ActiveDemoRowOfAnotherCustomer_LeavesTheDemoClientOut()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var otherCustomersRow = DemoRow(environmentType: 1, isActive: true) with { CustomerId = "contoso" };
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-2c"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), demoPreAuthorizedClientAppIds: [OperatorToolsClientId],
            registry: FakeRegistry.Returning(otherCustomersRow));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.LastProvisionRequest!.PreAuthorizedClientAppIds.Should().BeEmpty(
            "a run whose environment id points at another customer's row never borrows that row's Demo flag");
    }

    [Fact]
    public async Task Demo_3_RegistryReadFails_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-3"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), demoPreAuthorizedClientAppIds: [OperatorToolsClientId],
            registry: FakeRegistry.Throwing(new InvalidOperationException("Registry lookup infrastructure error")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.RegistryEnvironmentTypeUnavailable);
        provisioner.ProvisionCallCount.Should().Be(0, "nothing is written to Entra");
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Demo_4_InvalidDemoClientId_FailsResumable_BeforeAnyRegistryRead(string badClientId)
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var registry = FakeRegistry.Returning(DemoRow(environmentType: 1, isActive: true));
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-4"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), demoPreAuthorizedClientAppIds: [OperatorToolsClientId, badClientId],
            registry: registry);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.DemoPreAuthorizedClientAppIdInvalid);
        provisioner.ProvisionCallCount.Should().Be(0);
        registry.LookedUpIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Demo_5_NoDemoClientsConfigured_NeverReadsTheRegistry()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var registry = FakeRegistry.Throwing(new InvalidOperationException("must not be called"));
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-5"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), preAuthorizedClientAppIds: [AddInClientId], registry: registry);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.LastProvisionRequest!.PreAuthorizedClientAppIds.Should().Equal([AddInClientId]);
        registry.LookedUpIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Demo_6_DemoClientAlsoInThePlatformList_IsListedOnce()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-6"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), preAuthorizedClientAppIds: [AddInClientId, OperatorToolsClientId],
            demoPreAuthorizedClientAppIds: [OperatorToolsClientId],
            registry: FakeRegistry.Returning(DemoRow(environmentType: 1, isActive: true)));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.LastProvisionRequest!.PreAuthorizedClientAppIds.Should().Equal([AddInClientId, OperatorToolsClientId]);
    }

    [Fact]
    public async Task Demo_7_CustomerIdMatchIgnoresCase()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var upperCaseRow = DemoRow(environmentType: 1, isActive: true) with { CustomerId = CustomerId.ToUpperInvariant() };
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-7"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), demoPreAuthorizedClientAppIds: [OperatorToolsClientId],
            registry: FakeRegistry.Returning(upperCaseRow));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.LastProvisionRequest!.PreAuthorizedClientAppIds.Should().Equal([OperatorToolsClientId],
            "the registry stores the customer id as entered; the match is case-insensitive like every other customer-id match");
    }

    [Fact]
    public async Task Demo_8_RegistryTimeout_WithCallerNotCancelled_FailsResumable()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-8"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), demoPreAuthorizedClientAppIds: [OperatorToolsClientId],
            registry: FakeRegistry.Throwing(new TaskCanceledException("HttpClient timeout")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.RegistryEnvironmentTypeUnavailable);
        provisioner.ProvisionCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Demo_9_CallerCancelledDuringRegistryRead_Propagates_NoProvisionerCall()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        using var cts = new CancellationTokenSource();
        var registry = FakeRegistry.Running(() =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-9"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), demoPreAuthorizedClientAppIds: [OperatorToolsClientId],
            registry: registry);

        var act = () => handler.HandleAsync(BuildEnvelope(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        provisioner.ProvisionCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Demo_10_StampOutsideTheSpaarkeTenant_LeavesTheDemoClientOut()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-10"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), demoPreAuthorizedClientAppIds: [OperatorToolsClientId],
            registry: FakeRegistry.Returning(DemoRow(environmentType: 1, isActive: true)),
            spaarkeTenantId: "99999999-9999-9999-9999-999999999999");

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.LastProvisionRequest!.PreAuthorizedClientAppIds.Should().BeEmpty(
            "the operator client is a single-tenant Spaarke app: a customer-tenant registration never lists it");
    }

    [Fact]
    public async Task Demo_10b_SpaarkeTenantIdNotConfigured_LeavesTheDemoClientOut()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-10b"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), demoPreAuthorizedClientAppIds: [OperatorToolsClientId],
            registry: FakeRegistry.Returning(DemoRow(environmentType: 1, isActive: true)), spaarkeTenantId: "");

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.LastProvisionRequest!.PreAuthorizedClientAppIds.Should().BeEmpty(
            "without a configured Spaarke tenant H3 cannot prove the stamp is a Spaarke-tenant one");
    }

    [Fact]
    public async Task Demo_11_RegistryRowOfAnotherTenant_LeavesTheDemoClientOut()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var otherTenantRow = DemoRow(environmentType: 1, isActive: true) with { TenantId = "88888888-8888-8888-8888-888888888888" };
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-11"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), demoPreAuthorizedClientAppIds: [OperatorToolsClientId],
            registry: FakeRegistry.Returning(otherTenantRow));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.LastProvisionRequest!.PreAuthorizedClientAppIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Demo_12_RegistryReadFails_DiagnosticCarriesTheCause()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1));
        var provisioner = FakeProvisioner.Success(BuildOutputs(BuildPendingWrites()));
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-demo-12"), provisioner,
            FakeVerifier.Verified(ExpectedScopeCount), demoPreAuthorizedClientAppIds: [OperatorToolsClientId],
            registry: FakeRegistry.Throwing(new InvalidOperationException("Registry lookup returned HTTP 403 Forbidden")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("HTTP 403 Forbidden", "the operator must see why the read failed, not only its type");
    }

    private static DataverseEnvironmentRegistrySnapshot DemoRow(int? environmentType, bool? isActive) =>
        new(EnvironmentRowId, CustomerId, TenantId, "Ready", CurrentRunId: null, ProvisionedOn: null,
            EnvironmentType: environmentType, IsActive: isActive);

    private sealed class FakeRegistry : IDataverseEnvironmentRegistryClient
    {
        private readonly DataverseEnvironmentRegistrySnapshot? _row;
        private readonly Exception? _throw;
        private readonly Func<DataverseEnvironmentRegistrySnapshot?>? _onLookup;

        private FakeRegistry(DataverseEnvironmentRegistrySnapshot? row, Exception? toThrow,
            Func<DataverseEnvironmentRegistrySnapshot?>? onLookup = null)
        {
            _row = row;
            _throw = toThrow;
            _onLookup = onLookup;
        }

        public static FakeRegistry Returning(DataverseEnvironmentRegistrySnapshot? row) => new(row, null);

        public static FakeRegistry Throwing(Exception ex) => new(null, ex);

        public static FakeRegistry Running(Func<DataverseEnvironmentRegistrySnapshot?> onLookup) => new(null, null, onLookup);

        public List<string> LookedUpIds { get; } = [];

        public Task<DataverseEnvironmentRegistrySnapshot?> LookupByEnvironmentIdAsync(string environmentId, CancellationToken cancellationToken)
        {
            LookedUpIds.Add(environmentId);
            if (_onLookup is not null)
            {
                return Task.FromResult(_onLookup());
            }
            return _throw is not null
                ? Task.FromException<DataverseEnvironmentRegistrySnapshot?>(_throw)
                : Task.FromResult(_row);
        }

        public Task<DataverseEnvironmentRegistrySnapshot?> LookupByTenantIdAsync(string tenantId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("H3 never looks the registry up by tenant id");

        public Task<RegistryUpdateOutcome> UpdateSetupStatusAsync(RegistrySetupStatusUpdate update, CancellationToken cancellationToken)
            => throw new InvalidOperationException("H3 never writes the registry");
    }

    // ---------- AC-KV-1 deferred commit failure after consent verified ----------

    [Fact]
    public async Task AcKv_1_CommitFailsAfterConsentVerified_QuarantineRequired()
    {
        var run = BuildRun(tenancyModel: nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2));
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

    // ---------- helpers ----------

    private static H3EntraAppRegHandler BuildHandler(
        FakeRepository repo,
        FakeProvisioner provisioner,
        FakeVerifier verifier,
        int? expectedScopeCount = null,
        List<string>? preAuthorizedClientAppIds = null,
        List<string>? demoPreAuthorizedClientAppIds = null,
        IDataverseEnvironmentRegistryClient? registry = null,
        string? spaarkeTenantId = null)
    {
        var options = Options.Create(new EntraAppRegOptions
        {
            ExpectedDelegatedScopeCount = expectedScopeCount ?? ExpectedScopeCount,
            PreAuthorizedClientAppIds = preAuthorizedClientAppIds ?? [],
            DemoPreAuthorizedClientAppIds = demoPreAuthorizedClientAppIds ?? [],
            SpaarkeTenantId = spaarkeTenantId ?? TenantId,
        });
        return new H3EntraAppRegHandler(
            repo, provisioner, verifier, options,
            registry ?? FakeRegistry.Throwing(new InvalidOperationException("no Demo-only clients configured: H3 must not read the registry")),
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
        bool includeUamiObjectId = true,
        string? dataverseEnvUrl = DataverseEnvUrl)
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = EnvironmentRowId,
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
            // Task 245a (G25): the customer vault name is H2a's output, not a run parameter.
            run.InterStepState.KeyVaultName = KeyVaultName;
        }
        if (includeUamiObjectId)
        {
            run.InterStepState.MiObjectId = UamiObjectId;
        }
        if (dataverseEnvUrl is not null)
        {
            // T240a: H3 derives the code pages' SPA redirect from the intake environment URL.
            run.Parameters.NonSecret[IntakeParameterCatalog.DataverseEnvUrl] = dataverseEnvUrl;
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

    /// <summary>Provisioner fake — records calls + returns canned outcomes for the 2 remaining IEntraAppRegProvisioner members (ProvisionAsync + CommitPendingSecretsAsync). The VerifySharedAsync member was removed in task 222 per D-13 (the shared-app-reg branch was retired).</summary>
    private sealed class FakeProvisioner : IEntraAppRegProvisioner
    {
        private readonly EntraAppRegOutcome? _provisionOutcome;

        public int ProvisionCallCount { get; private set; }
        public int CommitCallCount { get; private set; }
        public EntraAppRegRequest? LastProvisionRequest { get; private set; }
        public IReadOnlyList<PendingKvSecretWrite>? LastCommittedWrites { get; private set; }
        public string? CommitFailureDiagnostic { get; set; }

        private FakeProvisioner(EntraAppRegOutcome? provisionOutcome)
        {
            _provisionOutcome = provisionOutcome;
        }

        public static FakeProvisioner Success(EntraAppRegOutputs outputs)
            => new(new EntraAppRegOutcome.Success(outputs));

        public static FakeProvisioner Failure(string diagnostic)
            => new(new EntraAppRegOutcome.Failure(diagnostic));

        public Task<EntraAppRegOutcome> ProvisionAsync(EntraAppRegRequest request, CancellationToken ct)
        {
            ProvisionCallCount++;
            LastProvisionRequest = request;
            return Task.FromResult(_provisionOutcome!);
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
