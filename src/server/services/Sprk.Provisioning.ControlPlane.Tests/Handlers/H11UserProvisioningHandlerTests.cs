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
//         identifying the user by position (never by name — D15); RetryableWithCleanup.
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
//   T232  (D2, G10 — Model 1 guests usable): Model1 refuses NativeAccount; a
//         B2BGuest run needs the security group id; the group must be
//         sprk-{customerId}-users and security-enabled and guest access must be
//         allowed — each refused BEFORE any invitation; after redemption each
//         guest joins the group and becomes a Dataverse user with the
//         configured roles (systemuserid recorded); a missing role / membership /
//         user failure is Resumable and names the entry; Pending writes nothing
//         to the group or Dataverse; NativeAccount without a licence SKU creates
//         nobody.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    private const string GroupId = "6f1c2b3a-4d5e-4f60-8a7b-9c0d1e2f3a4b";
    private const string EnvUrl = "https://spaarke-acme.crm.dynamics.com/";

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

    // ---------- T232 Model 1 guests usable ----------

    [Fact]
    public async Task T232_RedeemedGuests_JoinTheGroupAndBecomeDataverseUsersWithTheRoles()
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson, tenancyModel: "Model1");
        var repo = new FakeRepository(run, etag: "etag-t232");
        var group = FakeSecurityGroupClient.ThisCustomers();
        var writer = FakeGuestUserWriter.AllSucceed();
        var handler = BuildHandler(repo, FakeUserProvisioner.AllSucceed(), FakeInvitationClient.Success(),
            FakeConsentVerifier.Verified(), group, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        group.ReadGroupIds.Should().Equal(GroupId);
        group.AddedMembers.Should().Equal((GroupId, "guestid-Ada"), (GroupId, "guestid-Grace"));
        writer.Requests.Select(r => (r.EnvironmentUrl, r.TenantId, r.EntraObjectId)).Should().Equal(
            (EnvUrl, TenantId, "guestid-Ada"), (EnvUrl, TenantId, "guestid-Grace"));
        writer.ResolvedRoleNames.Should().ContainSingle("the roles are resolved once, not per guest")
            .Which.Should().Equal(H11UserProvisioningOptions.DefaultGuestSecurityRoleName);
        writer.Requests.Should().OnlyContain(r => r.RoleIds.SequenceEqual(new[] { FakeGuestUserWriter.RoleId }));
        repo.LastWrittenRun!.InterStepState.ProvisionedUsers!.Select(u => u.DataverseSystemUserId)
            .Should().Equal("sysuser-guestid-Ada", "sysuser-guestid-Grace");
    }

    [Fact]
    public async Task T232_Model1WithNativeAccount_IsRefusedBeforeAnyGraphCall()
    {
        var run = BuildRun(identityPreset: "NativeAccount", usersJson: NativeUsersJson, tenancyModel: "Model1");
        var userProvisioner = FakeUserProvisioner.AllSucceed();
        var handler = BuildHandler(new FakeRepository(run, "e"), userProvisioner, FakeInvitationClient.Success(),
            FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H11Rejections.Model1RequiresB2BGuest);
        userProvisioner.CreateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task T232_B2BGuestWithoutTheSecurityGroupId_IsRefusedBeforeAnyInvitation()
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson, securityGroupId: null);
        var invitations = FakeInvitationClient.Success();
        var handler = BuildHandler(new FakeRepository(run, "e"), FakeUserProvisioner.AllSucceed(), invitations,
            FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode.Should().Be(H11Rejections.MissingSecurityGroupId);
        invitations.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("sprk-other-users", true)]    // another customer's group
    [InlineData("acme users", true)]          // not the naming rule
    [InlineData("sprk-acme-users", false)]    // a Microsoft 365 group, not security-enabled
    public async Task T232_AGroupThatIsNotThisCustomersSecurityGroup_IsRefusedAndNothingIsWritten(
        string displayName, bool securityEnabled)
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var repo = new FakeRepository(run, "e");
        var invitations = FakeInvitationClient.Success();
        var group = FakeSecurityGroupClient.Returning(new SecurityGroupReadOutcome.Found(displayName, securityEnabled));
        var writer = FakeGuestUserWriter.AllSucceed();
        var handler = BuildHandler(repo, FakeUserProvisioner.AllSucceed(), invitations, FakeConsentVerifier.Verified(),
            group, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H11Rejections.SecurityGroupRejected);
        failure.Diagnostic.Should().Contain(GroupId);
        invitations.CallCount.Should().Be(0, "a group that is not this customer's is refused before anyone is invited");
        group.AddedMembers.Should().BeEmpty();
        writer.Requests.Should().BeEmpty();
        repo.LastWrittenRun!.InterStepState.ProvisionedUsers.Should().BeNull();
    }

    [Fact]
    public async Task T232_AnUnreadableGroup_IsRefusedBeforeAnyInvitation()
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var invitations = FakeInvitationClient.Success();
        var handler = BuildHandler(new FakeRepository(run, "e"), FakeUserProvisioner.AllSucceed(), invitations,
            FakeConsentVerifier.Verified(),
            FakeSecurityGroupClient.Returning(new SecurityGroupReadOutcome.Failure("404 Not Found")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode.Should().Be(H11Rejections.SecurityGroupRejected);
        invitations.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task T232_RestrictedGuestAccess_IsRefusedBeforeAnyInvitation()
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var invitations = FakeInvitationClient.Success();
        var writer = FakeGuestUserWriter.AllSucceed(guestAccess: new GuestAccessOutcome.Restricted());
        var handler = BuildHandler(new FakeRepository(run, "e"), FakeUserProvisioner.AllSucceed(), invitations,
            FakeConsentVerifier.Verified(), FakeSecurityGroupClient.ThisCustomers(), writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H11Rejections.GuestAccessRestricted);
        failure.Diagnostic.Should().Contain("PRQ-C-12");
        invitations.CallCount.Should().Be(0);
        writer.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task T232_WithoutH5sEnvironment_IsRefusedBeforeAnyInvitation()
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        run.InterStepState.DataverseEnvUrl = null;
        var invitations = FakeInvitationClient.Success();
        var handler = BuildHandler(new FakeRepository(run, "e"), FakeUserProvisioner.AllSucceed(), invitations,
            FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode.Should().Be(H11Rejections.MissingDataverseEnvUrl);
        invitations.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task T232_AConfiguredRoleTheEnvironmentLacks_IsRefusedBeforeAnyInvitation_NamingTheRole()
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var repo = new FakeRepository(run, "e");
        var invitations = FakeInvitationClient.Success();
        var group = FakeSecurityGroupClient.ThisCustomers();
        var writer = FakeGuestUserWriter.AllSucceed(roles: new GuestRoleResolution.RoleNotFound("Spaarke Basic User"));
        var handler = BuildHandler(repo, FakeUserProvisioner.AllSucceed(), invitations,
            FakeConsentVerifier.Verified(), group, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H11Rejections.SecurityRoleNotFound);
        failure.Diagnostic.Should().Contain("'Spaarke Basic User'").And.Contain("no invitation sent");
        invitations.CallCount.Should().Be(0, "a missing role must not send invitations first (the package is T218's)");
        group.AddedMembers.Should().BeEmpty();
        writer.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task T232_TheGroupNameMatch_IgnoresCase()
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var group = FakeSecurityGroupClient.Returning(new SecurityGroupReadOutcome.Found("SPRK-ACME-Users", SecurityEnabled: true));
        var handler = BuildHandler(new FakeRepository(run, "e"), FakeUserProvisioner.AllSucceed(),
            FakeInvitationClient.Success(), FakeConsentVerifier.Verified(), group);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>("Entra group names are case-insensitive for people; the id is the identity");
    }

    [Fact]
    public async Task T232_AResumedRun_AfterAPartialFailure_RepeatsTheStepsAndCompletes()
    {
        // The first attempt stopped after guest 1's membership (the writer failed). The resumed run repeats guest 1's
        // membership (each seam is idempotent — Graph treats an existing member as success; the seam tests pin that)
        // and finishes both guests.
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var repo = new FakeRepository(run, "e");
        var group = FakeSecurityGroupClient.ThisCustomers();
        var failing = FakeGuestUserWriter.Returning(new DataverseGuestUserOutcome.Failure("404 — not yet a member"));
        await BuildHandler(repo, FakeUserProvisioner.AllSucceed(), FakeInvitationClient.Success(),
            FakeConsentVerifier.Verified(), group, failing).HandleAsync(BuildEnvelope(), CancellationToken.None);

        var writer = FakeGuestUserWriter.AllSucceed();
        var result = await BuildHandler(repo, FakeUserProvisioner.AllSucceed(), FakeInvitationClient.Success(),
            FakeConsentVerifier.Verified(), group, writer).HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        group.AddedMembers.Should().Equal(
            [(GroupId, "guestid-Ada"), (GroupId, "guestid-Ada"), (GroupId, "guestid-Grace")],
            "the resumed run repeats guest 1's membership rather than skipping it");
        failing.Requests.Should().ContainSingle("the first attempt stopped at guest 1");
        writer.Requests.Select(r => r.EntraObjectId).Should().Equal("guestid-Ada", "guestid-Grace");
        repo.LastWrittenRun!.InterStepState.ProvisionedUsers!.Select(u => u.DataverseSystemUserId)
            .Should().Equal("sysuser-guestid-Ada", "sysuser-guestid-Grace");
    }

    [Fact]
    public async Task T232_RolesThatCannotBeResolved_AreRefusedBeforeAnyInvitation_WithTheDataverseCode()
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var invitations = FakeInvitationClient.Success();
        var writer = FakeGuestUserWriter.AllSucceed(
            roles: new GuestRoleResolution.Failure("More than one role named 'Spaarke Basic User' in the root business unit"));
        var handler = BuildHandler(new FakeRepository(run, "e"), FakeUserProvisioner.AllSucceed(), invitations,
            FakeConsentVerifier.Verified(), FakeSecurityGroupClient.ThisCustomers(), writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H11Rejections.DataverseUserFailed, "the role exists — it is ambiguous, not missing");
        invitations.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task T232_AConsentCheckTimeout_IsAResumableFailure_SayingItTimedOut()
    {
        // An HttpClient timeout is a TaskCanceledException while the CALLER's token is not cancelled.
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var handler = BuildHandler(new FakeRepository(run, "e"), FakeUserProvisioner.AllSucceed(),
            FakeInvitationClient.Success(), FakeConsentVerifier.Throwing(new TaskCanceledException("HttpClient.Timeout")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H11Rejections.B2BInvitationFailed);
        failure.Diagnostic.Should().Contain("timed out");
    }

    [Fact]
    public async Task T232_TheCallersCancellation_Propagates()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var handler = BuildHandler(new FakeRepository(run, "e"), FakeUserProvisioner.AllSucceed(),
            FakeInvitationClient.Success(), FakeConsentVerifier.Throwing(new OperationCanceledException(cancelled.Token)));

        var act = () => handler.HandleAsync(BuildEnvelope(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("a shutdown is not a provisioning failure");
    }

    [Fact]
    public void T232_ConfiguredGuestRoles_ReplaceTheDefault_NotAppendToIt()
    {
        // The configuration binder APPENDS to an initialised list — the default must not survive a configured value.
        var options = new H11UserProvisioningOptions();
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GuestSecurityRoleNames:0"] = "Spaarke Guest" })
            .Build()
            .Bind(options);

        options.EffectiveGuestSecurityRoleNames.Should().Equal("Spaarke Guest");
        new H11UserProvisioningOptions().EffectiveGuestSecurityRoleNames.Should().Equal(H11UserProvisioningOptions.DefaultGuestSecurityRoleName);
    }

    [Fact]
    public async Task T232_AGroupMembershipFailure_IsResumable_AndMakesNoDataverseUser()
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var group = FakeSecurityGroupClient.ThisCustomers(membership: new SecurityGroupMembershipOutcome.Failure("403"));
        var writer = FakeGuestUserWriter.AllSucceed();
        var handler = BuildHandler(new FakeRepository(run, "e"), FakeUserProvisioner.AllSucceed(),
            FakeInvitationClient.Success(), FakeConsentVerifier.Verified(), group, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H11Rejections.SecurityGroupMembershipFailed);
        writer.Requests.Should().BeEmpty("Dataverse adds a user only once it is a member of the environment's group");
    }

    [Fact]
    public async Task T232_ADataverseUserFailure_IsResumable_NamingTheEntryByPosition()
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var handler = BuildHandler(new FakeRepository(run, "e"), FakeUserProvisioner.AllSucceed(),
            FakeInvitationClient.Success(), FakeConsentVerifier.Verified(), FakeSecurityGroupClient.ThisCustomers(),
            FakeGuestUserWriter.Returning(new DataverseGuestUserOutcome.Failure("403 Forbidden")));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H11Rejections.DataverseUserFailed);
        failure.Diagnostic.Should().Contain("entry 1").And.NotContain("ada@customer.com");
    }

    [Fact]
    public async Task T232_NativeAccountWithoutALicenceSku_CreatesNobody()
    {
        var run = BuildRun(identityPreset: "NativeAccount", usersJson: NativeUsersJson);
        var userProvisioner = FakeUserProvisioner.AllSucceed();
        var handler = BuildHandler(new FakeRepository(run, "e"), userProvisioner, FakeInvitationClient.Success(),
            FakeConsentVerifier.Verified(), options: new H11UserProvisioningOptions());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H11Rejections.LicenseSkuNotConfigured);
        userProvisioner.CreateCallCount.Should().Be(0, "an unlicensed user cannot open the environment — none is created");
    }

    // ---------- AC-3 B2BGuest consent pending -> WaitingOnGate ----------

    [Fact]
    public async Task AC3_B2BGuestConsentPending_TransitionsToWaitingOnGate_NotFailed()
    {
        var run = BuildRun(identityPreset: "B2BGuest", usersJson: B2BUsersJson);
        var repo = new FakeRepository(run, etag: "etag-3");
        var pendingGroup = FakeSecurityGroupClient.ThisCustomers();
        var pendingWriter = FakeGuestUserWriter.AllSucceed();
        var handler = BuildHandler(
            repo, FakeUserProvisioner.AllSucceed(), FakeInvitationClient.Success(),
            FakeConsentVerifier.Pending(accepted: 1, expected: 2), pendingGroup, pendingWriter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H11UserProvisioningHandler.BuildIdempotencyKey(CustomerId));
        pendingGroup.AddedMembers.Should().BeEmpty("T232: nobody joins the group or Dataverse until every guest has redeemed");
        pendingWriter.Requests.Should().BeEmpty();

        repo.LastWrittenRun!.Status.Should().Be(RunStatus.WaitingOnGate);
        repo.LastWrittenRun.CompletedPhases.Should().BeEmpty("H11 has not finished its job yet");
        repo.LastWrittenRun.GateStates.Should().ContainKey(H11Gates.B2BConsent)
            .WhoseValue.Status.Should().Be(GateState.Pending);
        repo.LastWrittenRun.InterStepState.ProvisionedUsers.Should().HaveCount(2,
            "invited-but-pending users are still recorded so an operator can see who was invited");
    }

    // ---------- AC-4 license-assignment failure ----------

    [Fact]
    public async Task AC4_LicenseAssignmentFails_FailsRetryableWithCleanup_IdentifiesTheUserByPosition()
    {
        var run = BuildRun(identityPreset: "NativeAccount", usersJson: NativeUsersJson);
        var repo = new FakeRepository(run, etag: "etag-4");
        var userProvisioner = FakeUserProvisioner.LicenseFailsForSecondUser();
        var handler = BuildHandler(repo, userProvisioner, FakeInvitationClient.Success(), FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.RetryableWithCleanup);
        failure.RejectionCode.Should().Be(H11Rejections.LicenseAssignmentFailed);
        // D15 (task 245c): the diagnostic reaches run.ErrorDetail and logs — position + Entra id, never the name.
        // (The fake provisioner's Entra id is "userid-Grace"; a real one is a GUID — so check the surname and email.)
        failure.Diagnostic.Should().Contain("entry 2").And.NotContain("Hopper").And.NotContain("grace@acme.com");
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

    // ---------- T245c: the whole list is checked before the first Graph call ----------

    [Fact]
    public async Task B2BGuestListWithAnEntryMissingItsEmail_FailsBeforeAnyInvitationIsSent()
    {
        // Before T245c H11 found the missing email while inviting — after entry 1's invitation had gone out.
        var run = BuildRun(identityPreset: "B2BGuest", usersJson:
            "[{\"firstName\":\"Ada\",\"lastName\":\"Lovelace\",\"email\":\"ada@customer.com\"}," +
            "{\"firstName\":\"Grace\",\"lastName\":\"Hopper\"}]");
        var repo = new FakeRepository(run, etag: "etag-t245c");
        var invitationClient = FakeInvitationClient.Success();
        var handler = BuildHandler(repo, FakeUserProvisioner.AllSucceed(), invitationClient, FakeConsentVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H11Rejections.InvalidUserEntry);
        failure.Diagnostic.Should().Contain("entry 2");
        invitationClient.CallCount.Should().Be(0, "no invitation is sent from a list H11 cannot complete");
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
        failure.Diagnostic.Should().Contain("entry 1").And.NotContain("Ada").And.NotContain("Lovelace");
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

        var k3 = H11UserProvisioningHandler.BuildIdempotencyKey("other");
        k3.Should().NotBe(k1);
    }

    // ---------- helpers ----------

    private static H11UserProvisioningHandler BuildHandler(
        FakeRepository repo,
        FakeUserProvisioner userProvisioner,
        FakeInvitationClient invitationClient,
        FakeConsentVerifier consentVerifier,
        FakeSecurityGroupClient? securityGroupClient = null,
        FakeGuestUserWriter? guestUserWriter = null,
        H11UserProvisioningOptions? options = null)
        => new(repo, userProvisioner, invitationClient, consentVerifier,
            securityGroupClient ?? FakeSecurityGroupClient.ThisCustomers(),
            guestUserWriter ?? FakeGuestUserWriter.AllSucceed(),
            Options.Create(options ?? new H11UserProvisioningOptions { PowerAppsPlan2TrialSkuId = "sku-power-apps" }),
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
        string? identityPreset, string? usersJson, bool includeTenantId = true, string tenancyModel = "Model2",
        string? securityGroupId = GroupId)
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
        run.InterStepState.DataverseEnvUrl = EnvUrl;   // H5's output (H5 → H10 → H11)
        if (securityGroupId is not null)
        {
            run.Parameters.NonSecret[H11UserProvisioningHandler.EnvironmentSecurityGroupIdParameterKey] = securityGroupId;
        }
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

    private sealed class FakeSecurityGroupClient : IEnvironmentSecurityGroupClient
    {
        private readonly SecurityGroupReadOutcome _read;
        private readonly SecurityGroupMembershipOutcome _membership;
        public List<string> ReadGroupIds { get; } = [];
        public List<(string GroupId, string UserId)> AddedMembers { get; } = [];

        private FakeSecurityGroupClient(SecurityGroupReadOutcome read, SecurityGroupMembershipOutcome membership)
        {
            _read = read;
            _membership = membership;
        }

        public static FakeSecurityGroupClient ThisCustomers(SecurityGroupMembershipOutcome? membership = null)
            => new(new SecurityGroupReadOutcome.Found($"sprk-{CustomerId}-users", SecurityEnabled: true),
                membership ?? new SecurityGroupMembershipOutcome.Success());

        public static FakeSecurityGroupClient Returning(SecurityGroupReadOutcome read)
            => new(read, new SecurityGroupMembershipOutcome.Success());

        public Task<SecurityGroupReadOutcome> ReadAsync(string groupId, string tenantId, CancellationToken ct)
        {
            ReadGroupIds.Add(groupId);
            return Task.FromResult(_read);
        }

        public Task<SecurityGroupMembershipOutcome> AddMemberAsync(string groupId, string userId, string tenantId, CancellationToken ct)
        {
            if (_membership is SecurityGroupMembershipOutcome.Success)
            {
                AddedMembers.Add((groupId, userId));
            }
            return Task.FromResult(_membership);
        }
    }

    private sealed class FakeGuestUserWriter : IDataverseGuestUserWriter
    {
        public static readonly Guid RoleId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");

        private readonly Func<DataverseGuestUserRequest, DataverseGuestUserOutcome> _behavior;
        private readonly GuestAccessOutcome _guestAccess;
        private readonly GuestRoleResolution _roles;
        public List<DataverseGuestUserRequest> Requests { get; } = [];
        public List<IReadOnlyList<string>> ResolvedRoleNames { get; } = [];

        private FakeGuestUserWriter(
            Func<DataverseGuestUserRequest, DataverseGuestUserOutcome> behavior, GuestAccessOutcome guestAccess,
            GuestRoleResolution roles)
        {
            _behavior = behavior;
            _guestAccess = guestAccess;
            _roles = roles;
        }

        public static FakeGuestUserWriter AllSucceed(GuestAccessOutcome? guestAccess = null, GuestRoleResolution? roles = null)
            => new(r => new DataverseGuestUserOutcome.Success($"sysuser-{r.EntraObjectId}"),
                guestAccess ?? new GuestAccessOutcome.Allowed(), roles ?? new GuestRoleResolution.Resolved([RoleId]));

        public static FakeGuestUserWriter Returning(DataverseGuestUserOutcome outcome)
            => new(_ => outcome, new GuestAccessOutcome.Allowed(), new GuestRoleResolution.Resolved([RoleId]));

        public Task<GuestRoleResolution> ResolveRolesAsync(
            string environmentUrl, string tenantId, IReadOnlyList<string> roleNames, CancellationToken ct)
        {
            ResolvedRoleNames.Add(roleNames);
            return Task.FromResult(_roles);
        }

        public Task<DataverseGuestUserOutcome> EnsureGuestUserAsync(DataverseGuestUserRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_behavior(request));
        }

        public Task<GuestAccessOutcome> ReadGuestAccessAsync(string environmentUrl, string tenantId, CancellationToken ct)
            => Task.FromResult(_guestAccess);
    }

    private sealed class FakeConsentVerifier : IB2BConsentVerifier
    {
        private readonly B2BConsentVerificationResult _result;
        private readonly Exception? _throws;
        public int CallCount { get; private set; }
        public IReadOnlyList<string>? LastInvitedUserIds { get; private set; }

        private FakeConsentVerifier(B2BConsentVerificationResult result, Exception? throws = null)
        {
            _result = result;
            _throws = throws;
        }

        public static FakeConsentVerifier Throwing(Exception ex)
            => new(new B2BConsentVerificationResult.Verified(0, 0, Evidence: null), ex);

        public static FakeConsentVerifier Verified() => new(new B2BConsentVerificationResult.Verified(2, 2, Evidence: null));

        public static FakeConsentVerifier Pending(int accepted, int expected)
            => new(new B2BConsentVerificationResult.Pending(accepted, expected, "not all accepted yet", Evidence: null));

        public Task<B2BConsentVerificationResult> VerifyAsync(
            string tenantId, IReadOnlyList<string> invitedUserIds, CancellationToken ct)
        {
            CallCount++;
            LastInvitedUserIds = invitedUserIds;
            return _throws is null ? Task.FromResult(_result) : Task.FromException<B2BConsentVerificationResult>(_throws);
        }
    }
}
