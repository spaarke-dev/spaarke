// -----------------------------------------------------------------------------
// H3EntraAppRegHandlerTests.cs
//
// Unit tests over H3EntraAppRegHandler (task 046 — wave C4).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live Graph / az CLI / pwsh / Azure API.
//   Fakes replace the repository + both collaborator seams (provisioner,
//   consent verifier) so the handler orchestration logic is exercised in
//   isolation. Live-Graph coverage belongs in env-guarded smoke tests (H3
//   is not exercised end-to-end at CI time by design — a real Entra
//   app-registration creation requires an admin-scoped operator token).
//
// COVERAGE (POML acceptance criteria mapping):
//   AC-1  Happy path (Verified) — provisioner + verifier green → Success +
//         CompletedPhase(H3) + bffAppRegId in interStepState + admin-consent
//         gate Verified.
//   AC-2  Missing tenantId (§4D I1) — Failure(Resumable, MissingTenantId);
//         no provisioner call.
//   AC-3  S2S accidental provisioning (interStepState pre-populated) —
//         Failure(QuarantineRequired, S2SAppRegForbidden).
//   AC-4  Cleartext-secret-leak — Failure(QuarantineRequired, CleartextSecretLeak)
//         + Cosmos marked Quarantined.
//   AC-5  Admin-consent Pending → WaitingOnGate transition, admin-consent gate
//         Pending, bffAppRegId still written, NO CompletedPhase (H3 pending
//         completion), Success outcome.
//   AC-6  Graph SDK-shape (Wave-C5-forward) — verifier throws (simulating
//         ODataError bubble) → Failure(Resumable, ProvisioningFailed) not
//         Quarantined; parity with Graph error-type NFR-09.
//   AC-7  Idempotency: two invocations for same (customerId, tenantId) —
//         second call short-circuits Success no-op (no provisioner/verifier
//         calls).
//   AC-8  Handler registers in L2 DI + build green — validated by build
//         gate, not tested here.
//
// Plus defensive negative branches:
//   AC-9  Missing keyVaultName — Failure(Resumable, MissingKeyVaultName).
//   AC-10 ExpectedAppRoleCount <= 0 config drift — Failure(Resumable,
//         NullAppRoleIdInCatalog).
//   AC-11 Provisioner returns Failure (script exit non-zero) — Failure(Resumable,
//         ProvisioningFailed).
//   AC-12 Provisioner returns Success with blank BffAppRegId — Failure(Resumable,
//         ProvisioningOutputsIncomplete).
//   AC-13 HandlerId mismatch — throws InvalidOperationException.
//   AC-14 Idempotency key format determinism — same (customerId, tenantId)
//         produce same key.
//   AC-15 Run not found — Failure(Resumable, RunNotFound).
//   AC-16 KV URI-ref format is safe (cleartext scanner short-circuits on
//         @Microsoft.KeyVault prefix).
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
    private const int ExpectedRoleCount = 14;

    private static readonly string ExpectedKvUriRef =
        RegisterEntraAppRegScriptProvisioner.BuildKvUriReference(KeyVaultName);

    // ---------- AC-1 happy path (Verified) ----------

    [Fact]
    public async Task AC1_HappyPath_ConsentVerified_SucceedsAndAdvancesState()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var provisioner = FakeProvisioner.Success(BuildOutputs());
        var verifier = FakeVerifier.Verified(ExpectedRoleCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(
            H3EntraAppRegHandler.BuildIdempotencyKey(CustomerId, TenantId));

        // Cosmos state advanced.
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H3");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle()
            .Which.Phase.Should().Be("H3");
        repo.LastWrittenRun.InterStepState.BffAppRegId.Should().Be(BffAppRegId);
        repo.LastWrittenRun.InterStepState.S2SAppRegId.Should().BeNull(
            "S2S is dropped per r3 task 060 — MUST remain unpopulated");
        repo.LastWrittenRun.GateStates.Should().ContainKey(EntraAppRegGates.AdminConsent)
            .WhoseValue.Status.Should().Be(GateState.Verified);

        // Each collaborator called exactly once.
        provisioner.CallCount.Should().Be(1);
        verifier.CallCount.Should().Be(1);
        verifier.LastBffAppRegId.Should().Be(BffAppRegId);
        verifier.LastTenantId.Should().Be(TenantId);
        verifier.LastExpectedRoleCount.Should().Be(ExpectedRoleCount);
    }

    // ---------- AC-2 missing tenantId (§4D I1) ----------

    [Fact]
    public async Task AC2_MissingTenantId_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun(includeTenantId: false);
        var repo = new FakeRepository(run, etag: "etag-2");
        var provisioner = FakeProvisioner.Success(BuildOutputs());
        var verifier = FakeVerifier.Verified(ExpectedRoleCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.MissingTenantId);
        failure.Diagnostic.Should().Contain("§4D I1");
        provisioner.CallCount.Should().Be(0);
        verifier.CallCount.Should().Be(0);
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- AC-3 S2S accidental provisioning (interStepState pre-populated) ----------

    [Fact]
    public async Task AC3_S2SAppRegAlreadyPresent_FailsQuarantineRequired()
    {
        var run = BuildRun();
        run.InterStepState.S2SAppRegId = "phantom-s2s-app-reg-id";
        var repo = new FakeRepository(run, etag: "etag-3");
        var provisioner = FakeProvisioner.Success(BuildOutputs());
        var verifier = FakeVerifier.Verified(ExpectedRoleCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.S2SAppRegForbidden);
        failure.Diagnostic.Should().Contain("r3 task 060");
        provisioner.CallCount.Should().Be(1, "provisioner ran but the S2S guard trips post-provision");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.Quarantine.Should().NotBeNull();
    }

    // ---------- AC-4 cleartext-secret leak ----------

    [Fact]
    public async Task AC4_CleartextSecretLeak_InProvisionerOutput_FailsQuarantineRequired()
    {
        // Provisioner returns a client-secret-shaped literal instead of the KV URI ref.
        var leakyOutputs = new EntraAppRegOutputs
        {
            BffAppRegId = BffAppRegId,
            BffClientSecretKvUri = "Nx8Q~aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789.-_",
        };
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-4");
        var provisioner = FakeProvisioner.Success(leakyOutputs);
        var verifier = FakeVerifier.Verified(ExpectedRoleCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.CleartextSecretLeak);
        failure.Diagnostic.Should().Contain("ADR-028");
        verifier.CallCount.Should().Be(0, "leak guard trips BEFORE consent verification");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);

        // Cosmos interStepState MUST NOT contain the leaked secret string.
        var interStep = repo.LastWrittenRun.InterStepState;
        interStep.BffAppRegId.Should().BeNull("no bffAppRegId written on leak-guard failure path");
    }

    // ---------- AC-5 admin-consent Pending → WaitingOnGate ----------

    [Fact]
    public async Task AC5_ConsentPending_TransitionsToWaitingOnGate_NotFailure()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-5");
        var provisioner = FakeProvisioner.Success(BuildOutputs());
        var verifier = FakeVerifier.Pending(
            grantedCount: 0,
            expectedCount: ExpectedRoleCount,
            diagnostic: "Tenant admin has not yet granted consent for the 14 Graph app roles.");
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        // Success outcome (envelope processed correctly); state = WaitingOnGate.
        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(
            H3EntraAppRegHandler.BuildIdempotencyKey(CustomerId, TenantId));

        repo.LastWrittenRun!.Status.Should().Be(RunStatus.WaitingOnGate);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H3");
        repo.LastWrittenRun.CompletedPhases.Should().BeEmpty(
            "H3 has not completed its job yet — admin-consent is pending");
        repo.LastWrittenRun.InterStepState.BffAppRegId.Should().Be(BffAppRegId,
            "bffAppRegId still written so H4 can consume it after operator resumes");
        var gate = repo.LastWrittenRun.GateStates[EntraAppRegGates.AdminConsent];
        gate.Status.Should().Be(GateState.Pending);
        gate.VerifierHandler.Should().Be("H3");
    }

    // ---------- AC-6 verifier throws (simulating ODataError bubble) ----------

    [Fact]
    public async Task AC6_VerifierThrowsUnexpected_FailsResumable_NotQuarantined()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-6");
        var provisioner = FakeProvisioner.Success(BuildOutputs());
        var verifier = FakeVerifier.Throws(
            new InvalidOperationException("Graph ODataError bubbled up: 503 Service Unavailable"));
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.ProvisioningFailed);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed,
            "verifier fault is Resumable — Failed not Quarantined");
    }

    // ---------- AC-7 idempotency ----------

    [Fact]
    public async Task AC7_Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRun();
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
        var provisioner = FakeProvisioner.Success(BuildOutputs());
        var verifier = FakeVerifier.Verified(ExpectedRoleCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        provisioner.CallCount.Should().Be(0);
        verifier.CallCount.Should().Be(0);
    }

    // ---------- AC-9 missing keyVaultName ----------

    [Fact]
    public async Task AC9_MissingKeyVaultName_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun(includeKvName: false);
        var repo = new FakeRepository(run, etag: "etag-9");
        var provisioner = FakeProvisioner.Success(BuildOutputs());
        var verifier = FakeVerifier.Verified(ExpectedRoleCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.MissingKeyVaultName);
        provisioner.CallCount.Should().Be(0);
    }

    // ---------- AC-10 ExpectedAppRoleCount <= 0 config drift ----------

    [Fact]
    public async Task AC10_ExpectedRoleCountZero_FailsResumable_NoProvisionerCall()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-10");
        var provisioner = FakeProvisioner.Success(BuildOutputs());
        var verifier = FakeVerifier.Verified(ExpectedRoleCount);
        var handler = BuildHandler(repo, provisioner, verifier, expectedRoleCount: 0);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.NullAppRoleIdInCatalog);
        failure.Diagnostic.Should().Contain("GraphAppRoles");
        provisioner.CallCount.Should().Be(0);
    }

    // ---------- AC-11 provisioner returns Failure ----------

    [Fact]
    public async Task AC11_ProvisionerReturnsFailure_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-11");
        var provisioner = FakeProvisioner.Failure(
            "Register-EntraAppRegistrations.ps1 exit 1: Graph API 403 InsufficientPrivileges");
        var verifier = FakeVerifier.Verified(ExpectedRoleCount);
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
    public async Task AC12_ProvisionerReturnsBlankAppId_FailsResumable()
    {
        var incompleteOutputs = new EntraAppRegOutputs
        {
            BffAppRegId = "   ",
            BffClientSecretKvUri = ExpectedKvUriRef,
        };
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-12");
        var provisioner = FakeProvisioner.Success(incompleteOutputs);
        var verifier = FakeVerifier.Verified(ExpectedRoleCount);
        var handler = BuildHandler(repo, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.ProvisioningOutputsIncomplete);
    }

    // ---------- AC-13 handler-id mismatch ----------

    [Fact]
    public async Task AC13_HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-13");
        var handler = BuildHandler(repo,
            FakeProvisioner.Success(BuildOutputs()),
            FakeVerifier.Verified(ExpectedRoleCount));

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

    // ---------- AC-14 idempotency key format determinism ----------

    [Fact]
    public void AC14_IdempotencyKey_IsDeterministicByCustomerAndTenant()
    {
        var k1 = H3EntraAppRegHandler.BuildIdempotencyKey("acme", TenantId);
        var k2 = H3EntraAppRegHandler.BuildIdempotencyKey("acme", TenantId);
        k1.Should().Be(k2);
        k1.Should().Be($"appreg-acme-{TenantId}");

        // Different tenantId → different key.
        var k3 = H3EntraAppRegHandler.BuildIdempotencyKey("acme", "different-tenant");
        k3.Should().NotBe(k1);
    }

    // ---------- AC-15 run not found ----------

    [Fact]
    public async Task AC15_RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var handler = BuildHandler(repo,
            FakeProvisioner.Success(BuildOutputs()),
            FakeVerifier.Verified(ExpectedRoleCount));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.RunNotFound);
    }

    // ---------- AC-16 KV URI-ref format short-circuits cleartext scanner ----------

    [Fact]
    public void AC16_IsCleartextSecretPattern_ShortCircuitsOnKvUriRef()
    {
        // Actual KV URI ref literal — MUST NOT trip the guard.
        H3EntraAppRegHandler.IsCleartextSecretPattern(ExpectedKvUriRef).Should().BeFalse();

        // Also a legit KV URI ref — safe.
        H3EntraAppRegHandler.IsCleartextSecretPattern(
            "@Microsoft.KeyVault(SecretUri=https://sprk-x.vault.azure.net/secrets/BFF-API-ClientSecret/)"
        ).Should().BeFalse();

        // Cleartext-shape — MUST trip the guard.
        H3EntraAppRegHandler.IsCleartextSecretPattern(
            "Nx8Q~aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789.-_"
        ).Should().BeTrue();

        // Blank / null → safe (nothing to leak).
        H3EntraAppRegHandler.IsCleartextSecretPattern(string.Empty).Should().BeFalse();
        H3EntraAppRegHandler.IsCleartextSecretPattern("   ").Should().BeFalse();

        // Short values — safe (below 40-char pattern threshold).
        H3EntraAppRegHandler.IsCleartextSecretPattern("guid-like-value").Should().BeFalse();
    }

    // ---------- helpers ----------

    private static H3EntraAppRegHandler BuildHandler(
        FakeRepository repo,
        FakeProvisioner provisioner,
        FakeVerifier verifier,
        int expectedRoleCount = ExpectedRoleCount)
    {
        var options = Options.Create(new EntraAppRegOptions
        {
            ExpectedAppRoleCount = expectedRoleCount,
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
        bool includeTenantId = true,
        bool includeKvName = true)
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
        if (includeTenantId)
        {
            run.Parameters.NonSecret[H3EntraAppRegHandler.TenantIdParameterKey] = TenantId;
        }
        if (includeKvName)
        {
            run.Parameters.NonSecret[H3EntraAppRegHandler.KeyVaultNameParameterKey] = KeyVaultName;
        }
        return run;
    }

    private static EntraAppRegOutputs BuildOutputs() => new()
    {
        BffAppRegId = BffAppRegId,
        BffClientSecretKvUri = ExpectedKvUriRef,
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

    /// <summary>Provisioner fake — records the last request + returns canned outcomes.</summary>
    private sealed class FakeProvisioner : IEntraAppRegProvisioner
    {
        private readonly EntraAppRegOutcome _outcome;
        public int CallCount { get; private set; }
        public EntraAppRegRequest? LastRequest { get; private set; }

        private FakeProvisioner(EntraAppRegOutcome outcome) => _outcome = outcome;

        public static FakeProvisioner Success(EntraAppRegOutputs outputs)
            => new(new EntraAppRegOutcome.Success(outputs));

        public static FakeProvisioner Failure(string diagnostic)
            => new(new EntraAppRegOutcome.Failure(diagnostic));

        public Task<EntraAppRegOutcome> ProvisionAsync(EntraAppRegRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }

    /// <summary>Admin-consent verifier fake.</summary>
    private sealed class FakeVerifier : IAdminConsentVerifier
    {
        private readonly Func<Task<AdminConsentVerificationResult>> _behavior;
        public int CallCount { get; private set; }
        public string? LastBffAppRegId { get; private set; }
        public string? LastTenantId { get; private set; }
        public int LastExpectedRoleCount { get; private set; }

        private FakeVerifier(Func<Task<AdminConsentVerificationResult>> behavior)
            => _behavior = behavior;

        public static FakeVerifier Verified(int grantedCount)
            => new(() => Task.FromResult<AdminConsentVerificationResult>(
                new AdminConsentVerificationResult.Verified(grantedCount, grantedCount, null)));

        public static FakeVerifier Pending(int grantedCount, int expectedCount, string diagnostic)
            => new(() => Task.FromResult<AdminConsentVerificationResult>(
                new AdminConsentVerificationResult.Pending(grantedCount, expectedCount, diagnostic, null)));

        public static FakeVerifier Throws(Exception ex)
            => new(() => Task.FromException<AdminConsentVerificationResult>(ex));

        public Task<AdminConsentVerificationResult> VerifyAsync(
            string bffAppRegId, string tenantId, int expectedRoleCount, CancellationToken ct)
        {
            CallCount++;
            LastBffAppRegId = bffAppRegId;
            LastTenantId = tenantId;
            LastExpectedRoleCount = expectedRoleCount;
            return _behavior();
        }
    }
}
