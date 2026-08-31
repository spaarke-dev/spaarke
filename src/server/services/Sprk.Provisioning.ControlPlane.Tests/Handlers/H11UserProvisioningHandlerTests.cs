// -----------------------------------------------------------------------------
// H11UserProvisioningHandlerTests.cs
//
// Unit tests over H11UserProvisioningHandler (task 054 — wave C4 Batch 3F).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live Graph calls. Fakes replace the
//   repository + all three collaborator seams (user provisioner, B2B
//   invitation client, B2B consent verifier) so the handler orchestration
//   logic is exercised in isolation. Live-Graph coverage belongs in
//   env-guarded smoke tests — H11's real collaborators are "NOT under test
//   in the CI unit suite" per their own file headers (parity with H10's
//   GraphRestAppRoleGranter posture).
//
// COVERAGE (POML acceptance criteria + dispatcher-required cases):
//   AC-1  NativeAccount happy path — all users created + licensed.
//   AC-2  B2BGuest happy path — invitations sent, consent Verified.
//   AC-3  B2BGuest consent Pending — WaitingOnGate (not Failed).
//   AC-4  License-assignment failure — distinct code "LicenseAssignmentFailed"
//         naming the user; classified RetryableWithCleanup.
//   AC-5  Idempotency — second invocation with a matching CompletedPhase
//         entry short-circuits Success no-op; no collaborator calls.
//   AC-6  Missing tenantId (§4D I1) — Resumable, no collaborator calls.
//   AC-7  Missing identityPreset — Resumable.
//   AC-8  Invalid identityPreset value — Resumable.
//   AC-9  Missing usersJson — Resumable.
//   AC-10 Malformed usersJson — Resumable.
//   AC-11 Empty usersJson array — Resumable.
//   AC-12 Run not found — Resumable.
//   AC-13 Handler-id mismatch — throws InvalidOperationException.
//   AC-14 NativeAccount user-creation failure — Resumable, fail-fast (license
//         collaborator not called for that user).
//   AC-15 B2BGuest invitation failure — Resumable, fail-fast.
//   AC-16 Idempotency key format determinism (users-{customerId}).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H11UserProvisioningHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h11-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";

    private const string NativeUsersJson =
        "[{\"firstName\":\"Ada\",\"lastName\":\"Lovelace\",\"email\":\"ada@acme.com\",\"companyName\":\"Acme\"}," +
        "{\"firstName\":\"Grace\",\"lastName\":\"Hopper\",\"email\":\"grace@acme.com\",\"companyName\":\"Acme\"}]";

    private const string B2BUsersJson =
        "[{\"firstName\":\"Ada\",\"lastName\":\"Lovelace\",\"email\":\"ada@customer.com\"}," +
        "{\"firstName\":\"Grace\",\"lastName\":\"Hopper\",\"email\":\"grace@customer.com\"}]";

    // ---------- AC-1 NativeAccount happy path ----------

    [Fact]
    public async Task AC1_NativeAccountHappyPath_AllUsersCreatedAndLicensed()
    {
        var run = BuildRun(identityPreset: "NativeAccount", usersJson: NativeUsersJson);
        var repo = new FakeRepository(run, etag: "etag-1");
        var userProvisioner = FakeUserProvisioner.AllSucceed();
        var handler = BuildHandler(repo, userProvisioner, FakeInvitationClient.Success(), FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H11UserProvisioningHandler.BuildIdempotencyKey(CustomerId));

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H11");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle().Which.Phase.Should().Be("H11");
        repo.LastWrittenRun.InterStepState.ProvisionedUsers.Should().HaveCount(2);
        repo.LastWrittenRun.InterStepState.ProvisionedUsers!.Should()
            .OnlyContain(u => u.IdentityPreset == "NativeAccount");
        repo.LastWrittenRun.GateStates.Should().NotContainKey(H11Gates.B2BConsent,
            "NativeAccount branch never touches the B2B consent gate");

        userProvisioner.CreateCallCount.Should().Be(2);
        userProvisioner.AssignLicenseCallCount.Should().Be(2);
    }

    // ---------- AC-2 B2BGuest happy path — consent verified ----------

    [Fact]
    public async Task AC2_B2BGuestHappyPath_InvitationsSentAndConsentVerified()
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var repo = new FakeRepository(run, etag: "etag-2");
        var invitationClient = FakeInvitationClient.Success();
        var consentVerifier = FakeConsentVerifier.Verified();
        var userProvisioner = FakeUserProvisioner.AllSucceed();
        var handler = BuildHandler(repo, userProvisioner, invitationClient, consentVerifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H11UserProvisioningHandler.BuildIdempotencyKey(CustomerId));

        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle().Which.Phase.Should().Be("H11");
        repo.LastWrittenRun.InterStepState.ProvisionedUsers.Should().HaveCount(2);
        repo.LastWrittenRun.InterStepState.ProvisionedUsers!.Should().OnlyContain(u => u.IdentityPreset == "B2BGuest");
        repo.LastWrittenRun.GateStates.Should().ContainKey(H11Gates.B2BConsent)
            .WhoseValue.Status.Should().Be(GateState.Verified);

        invitationClient.CallCount.Should().Be(2);
        consentVerifier.CallCount.Should().Be(1);
        consentVerifier.LastInvitedUserIds.Should().HaveCount(2);
        userProvisioner.CreateCallCount.Should().Be(0, "B2BGuest branch never calls CreateUser/AssignLicense");
        userProvisioner.AssignLicenseCallCount.Should().Be(0);
    }

    // ---------- AC-3 B2BGuest consent pending -> WaitingOnGate ----------

    [Fact]
    public async Task AC3_B2BGuestConsentPending_TransitionsToWaitingOnGate_NotFailed()
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var repo = new FakeRepository(run, etag: "etag-3");
        var handler = BuildHandler(
            repo, FakeUserProvisioner.AllSucceed(), FakeInvitationClient.Success(),
            FakeConsentVerifier.Pending(accepted: 1, expected: 2));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H11UserProvisioningHandler.BuildIdempotencyKey(CustomerId));

        repo.LastWrittenRun!.Status.Should().Be(RunStatus.WaitingOnGate);
        repo.LastWrittenRun.CompletedPhases.Should().BeEmpty("H11 has not finished its job yet");
        repo.LastWrittenRun.GateStates.Should().ContainKey(H11Gates.B2BConsent)
            .WhoseValue.Status.Should().Be(GateState.Pending);
        repo.LastWrittenRun.InterStepState.ProvisionedUsers.Should().HaveCount(2,
            "invited-but-pending users are still recorded so an operator can see who was invited");
    }

    // ---------- AC-4 license-assignment failure ----------

    [Fact]
    public async Task AC4_LicenseAssignmentFails_FailsRetryableWithCleanup_NamesTheUser()
    {
        var run = BuildRun(identityPreset: "NativeAccount", usersJson: NativeUsersJson);
        var repo = new FakeRepository(run, etag: "etag-4");
        var userProvisioner = FakeUserProvisioner.LicenseFailsForSecondUser();
        var handler = BuildHandler(repo, userProvisioner, FakeInvitationClient.Success(), FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.RetryableWithCleanup);
        failure.RejectionCode.Should().Be(H11Rejections.LicenseAssignmentFailed);
        failure.Diagnostic.Should().Contain("Grace").And.Contain("Hopper");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed, "RetryableWithCleanup maps to Failed, not Quarantined");
        userProvisioner.CreateCallCount.Should().Be(2, "first user fully succeeded before the second user's license call failed");
        userProvisioner.AssignLicenseCallCount.Should().Be(2);
    }

    // ---------- AC-5 idempotency ----------

    [Fact]
    public async Task AC5_Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRun(identityPreset: "NativeAccount", usersJson: NativeUsersJson);
        var expectedKey = H11UserProvisioningHandler.BuildIdempotencyKey(CustomerId);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H11",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-5");
        var userProvisioner = FakeUserProvisioner.AllSucceed();
        var handler = BuildHandler(repo, userProvisioner, FakeInvitationClient.Success(), FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        userProvisioner.CreateCallCount.Should().Be(0);
    }

    // ---------- AC-6 missing tenantId ----------

    [Fact]
    public async Task AC6_MissingTenantId_FailsResumable_NoCollaboratorCalls()
    {
        var run = BuildRun(identityPreset: "NativeAccount", usersJson: NativeUsersJson, includeTenantId: false);
        var repo = new FakeRepository(run, etag: "etag-6");
        var userProvisioner = FakeUserProvisioner.AllSucceed();
        var handler = BuildHandler(repo, userProvisioner, FakeInvitationClient.Success(), FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H11Rejections.MissingTenantId);
        userProvisioner.CreateCallCount.Should().Be(0);
    }

    // ---------- AC-7 missing identityPreset ----------

    [Fact]
    public async Task AC7_MissingIdentityPreset_FailsResumable()
    {
        var run = BuildRun(identityPreset: null, usersJson: NativeUsersJson);
        var repo = new FakeRepository(run, etag: "etag-7");
        var handler = BuildHandler(repo, FakeUserProvisioner.AllSucceed(), FakeInvitationClient.Success(), FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H11Rejections.MissingIdentityPreset);
    }

    // ---------- AC-8 invalid identityPreset ----------

    [Fact]
    public async Task AC8_InvalidIdentityPreset_FailsResumable()
    {
        var run = BuildRun(identityPreset: "SomethingElse", usersJson: NativeUsersJson);
        var repo = new FakeRepository(run, etag: "etag-8");
        var handler = BuildHandler(repo, FakeUserProvisioner.AllSucceed(), FakeInvitationClient.Success(), FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H11Rejections.InvalidIdentityPreset);
    }

    // ---------- AC-9 missing usersJson ----------

    [Fact]
    public async Task AC9_MissingUsersJson_FailsResumable()
    {
        var run = BuildRun(identityPreset: "NativeAccount", usersJson: null);
        var repo = new FakeRepository(run, etag: "etag-9");
        var handler = BuildHandler(repo, FakeUserProvisioner.AllSucceed(), FakeInvitationClient.Success(), FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H11Rejections.MissingUsers);
    }

    // ---------- AC-10 malformed usersJson ----------

    [Fact]
    public async Task AC10_MalformedUsersJson_FailsResumable()
    {
        var run = BuildRun(identityPreset: "NativeAccount", usersJson: "{not-an-array");
        var repo = new FakeRepository(run, etag: "etag-10");
        var handler = BuildHandler(repo, FakeUserProvisioner.AllSucceed(), FakeInvitationClient.Success(), FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H11Rejections.MalformedUsersPayload);
    }

    // ---------- AC-11 empty usersJson array ----------

    [Fact]
    public async Task AC11_EmptyUsersArray_FailsResumable()
    {
        var run = BuildRun(identityPreset: "NativeAccount", usersJson: "[]");
        var repo = new FakeRepository(run, etag: "etag-11");
        var handler = BuildHandler(repo, FakeUserProvisioner.AllSucceed(), FakeInvitationClient.Success(), FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H11Rejections.MissingUsers);
    }

    // ---------- AC-12 run not found ----------

    [Fact]
    public async Task AC12_RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var handler = BuildHandler(repo, FakeUserProvisioner.AllSucceed(), FakeInvitationClient.Success(), FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H11Rejections.RunNotFound);
    }

    // ---------- AC-13 handler-id mismatch ----------

    [Fact]
    public async Task AC13_HandlerIdMismatch_Throws()
    {
        var run = BuildRun(identityPreset: "NativeAccount", usersJson: NativeUsersJson);
        var repo = new FakeRepository(run, etag: "etag-13");
        var handler = BuildHandler(repo, FakeUserProvisioner.AllSucceed(), FakeInvitationClient.Success(), FakeConsentVerifier.Verified());

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

    // ---------- AC-14 NativeAccount user-creation failure (fail-fast) ----------

    [Fact]
    public async Task AC14_UserCreationFails_FailsResumable_FailFast()
    {
        var run = BuildRun(identityPreset: "NativeAccount", usersJson: NativeUsersJson);
        var repo = new FakeRepository(run, etag: "etag-14");
        var userProvisioner = FakeUserProvisioner.CreationFailsForFirstUser();
        var handler = BuildHandler(repo, userProvisioner, FakeInvitationClient.Success(), FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H11Rejections.UserCreationFailed);
        failure.Diagnostic.Should().Contain("Ada").And.Contain("Lovelace");
        userProvisioner.CreateCallCount.Should().Be(1, "fails fast on the first user — second user never attempted");
        userProvisioner.AssignLicenseCallCount.Should().Be(0);
    }

    // ---------- AC-15 B2BGuest invitation failure (fail-fast) ----------

    [Fact]
    public async Task AC15_B2BInvitationFails_FailsResumable_FailFast()
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var repo = new FakeRepository(run, etag: "etag-15");
        var invitationClient = FakeInvitationClient.FailsForFirstUser();
        var consentVerifier = FakeConsentVerifier.Verified();
        var handler = BuildHandler(repo, FakeUserProvisioner.AllSucceed(), invitationClient, consentVerifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H11Rejections.B2BInvitationFailed);
        invitationClient.CallCount.Should().Be(1, "fails fast on the first invitation — second user never attempted");
        consentVerifier.CallCount.Should().Be(0, "consent verification never runs once an invitation fails");
    }

    // ---------- AC-16 idempotency key format determinism ----------

    [Fact]
    public void AC16_IdempotencyKey_IsDeterministicByCustomerOnly()
    {
        var k1 = H11UserProvisioningHandler.BuildIdempotencyKey("acme");
        var k2 = H11UserProvisioningHandler.BuildIdempotencyKey("acme");
        k1.Should().Be(k2);
        k1.Should().Be("users-acme");

        var k3 = H11UserProvisioningHandler.BuildIdempotencyKey("other-customer");
        k3.Should().NotBe(k1);
    }

    // ---------- helpers ----------

    private static H11UserProvisioningHandler BuildHandler(
        FakeRepository repo,
        FakeUserProvisioner userProvisioner,
        FakeInvitationClient invitationClient,
        FakeConsentVerifier consentVerifier)
        => new(repo, userProvisioner, invitationClient, consentVerifier,
            NullLogger<H11UserProvisioningHandler>.Instance);

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H11UserProvisioningHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(
        string? identityPreset, string? usersJson, bool includeTenantId = true)
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
            run.Parameters.NonSecret[H11UserProvisioningHandler.TenantIdParameterKey] = TenantId;
        }
        if (identityPreset is not null)
        {
            run.Parameters.NonSecret[H11UserProvisioningHandler.IdentityPresetParameterKey] = identityPreset;
        }
        if (usersJson is not null)
        {
            run.Parameters.NonSecret[H11UserProvisioningHandler.UsersJsonParameterKey] = usersJson;
        }
        return run;
    }

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
            => Task.FromResult(_run is null || _etag is null ? null : new ProvisioningRunReadResult(_run, _etag));

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

    private sealed class FakeUserProvisioner : IGraphUserProvisioner
    {
        private readonly Func<UserProvisioningEntry, UserCreationOutcome> _createBehavior;
        private readonly Func<string, LicenseAssignmentOutcome> _licenseBehavior;
        public int CreateCallCount { get; private set; }
        public int AssignLicenseCallCount { get; private set; }

        private FakeUserProvisioner(
            Func<UserProvisioningEntry, UserCreationOutcome> createBehavior,
            Func<string, LicenseAssignmentOutcome> licenseBehavior)
        {
            _createBehavior = createBehavior;
            _licenseBehavior = licenseBehavior;
        }

        public static FakeUserProvisioner AllSucceed() => new(
            entry => new UserCreationOutcome.Success($"userid-{entry.FirstName}", $"{entry.FirstName}.{entry.LastName}@spaarke.onmicrosoft.com"),
            _ => new LicenseAssignmentOutcome.Success());

        public static FakeUserProvisioner CreationFailsForFirstUser() => new(
            entry => entry.FirstName == "Ada"
                ? new UserCreationOutcome.Failure("Graph 400 Bad Request")
                : new UserCreationOutcome.Success($"userid-{entry.FirstName}", $"{entry.FirstName}.{entry.LastName}@spaarke.onmicrosoft.com"),
            _ => new LicenseAssignmentOutcome.Success());

        public static FakeUserProvisioner LicenseFailsForSecondUser() => new(
            entry => new UserCreationOutcome.Success($"userid-{entry.FirstName}", $"{entry.FirstName}.{entry.LastName}@spaarke.onmicrosoft.com"),
            userId => userId == "userid-Grace"
                ? new LicenseAssignmentOutcome.Failure("Insufficient licenses in tenant")
                : new LicenseAssignmentOutcome.Success());

        public Task<UserCreationOutcome> CreateUserAsync(UserProvisioningEntry entry, string tenantId, CancellationToken ct)
        {
            CreateCallCount++;
            return Task.FromResult(_createBehavior(entry));
        }

        public Task<LicenseAssignmentOutcome> AssignLicenseAsync(string userId, string tenantId, CancellationToken ct)
        {
            AssignLicenseCallCount++;
            return Task.FromResult(_licenseBehavior(userId));
        }
    }

    private sealed class FakeInvitationClient : IB2BInvitationClient
    {
        private readonly Func<UserProvisioningEntry, B2BInvitationOutcome> _behavior;
        public int CallCount { get; private set; }

        private FakeInvitationClient(Func<UserProvisioningEntry, B2BInvitationOutcome> behavior) => _behavior = behavior;

        public static FakeInvitationClient Success() => new(
            entry => new B2BInvitationOutcome.Success($"guestid-{entry.FirstName}", $"invitation-{entry.FirstName}"));

        public static FakeInvitationClient FailsForFirstUser() => new(
            entry => entry.FirstName == "Ada"
                ? new B2BInvitationOutcome.Failure("Graph 403 Forbidden")
                : new B2BInvitationOutcome.Success($"guestid-{entry.FirstName}", $"invitation-{entry.FirstName}"));

        public Task<B2BInvitationOutcome> InviteAsync(UserProvisioningEntry entry, string tenantId, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_behavior(entry));
        }
    }

    private sealed class FakeConsentVerifier : IB2BConsentVerifier
    {
        private readonly B2BConsentVerificationResult _result;
        public int CallCount { get; private set; }
        public IReadOnlyList<string>? LastInvitedUserIds { get; private set; }

        private FakeConsentVerifier(B2BConsentVerificationResult result) => _result = result;

        public static FakeConsentVerifier Verified() => new(new B2BConsentVerificationResult.Verified(2, 2, Evidence: null));

        public static FakeConsentVerifier Pending(int accepted, int expected)
            => new(new B2BConsentVerificationResult.Pending(accepted, expected, "not all accepted yet", Evidence: null));

        public Task<B2BConsentVerificationResult> VerifyAsync(
            string tenantId, IReadOnlyList<string> invitedUserIds, CancellationToken ct)
        {
            CallCount++;
            LastInvitedUserIds = invitedUserIds;
            return Task.FromResult(_result);
        }
    }
}
