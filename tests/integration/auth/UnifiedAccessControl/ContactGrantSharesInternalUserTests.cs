using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 193 (#2338) steps 3-4 — Manage Access's ONE "add person" entry: <c>/grant</c> (and
/// <c>/invite-and-grant</c>) on a CONTACT asks who that contact represents (<see cref="ContactUserRepresentation"/>) and the
/// server decides. One row per line of the decision matrix, through the PRODUCTION handlers, decision, share core, grant core,
/// invite service and notifier over the task-142 harness's doubles. KEEP path: <c>tests/integration/auth/**</c>.
/// </summary>
public class ContactGrantSharesInternalUserTests
{
    private const string Email = "colleague@firm.test";
    private const string MatterTable = "sprk_matter";
    private const int CollaborateMask = 262167; // Read 1 + Write 2 + Append 4 + AppendTo 16 + Share 262144

    private const AccessRights EveryRight = AccessRights.Read | AccessRights.Write | AccessRights.Append
                                            | AccessRights.AppendTo | AccessRights.Share | AccessRights.Delete;

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();

    public ContactGrantSharesInternalUserTests()
    {
        _h.Store.Root(ExternalGrantRootType.Matter, _matter);
        _h.Grants.RootNames[_matter] = "Smith v. Smith";
    }

    private ContactInternalUserShare Decision() => new(
        new ContactUserRepresentation(_h.Identities, _h.Store, _h.Participations),
        _h.Shares, _h.Cache.Mock.Object, _h.Children, _h.Guard,
        TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(), _h.WorkforceIdentity);

    private Task<IResult> Grant(
        Guid contactId, ExternalAccessLevel level = ExternalAccessLevel.Collaborate, DateOnly? expiry = null) =>
        GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(contactId, Guid.Empty, level, expiry, null, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, _h.Ownership, _h.OwnerPrivileges,
            new InternalUserShareTests.StubCallerRightsProbe(EveryRight),
            _h.Materializer, _h.Notifier, _h.Invites, InternalUserShareCoreTests.Rig.Caller(), NullLogger<Program>.Instance,
            _h.Time, CancellationToken.None, Decision());

    private Task<IResult> InviteAndGrant(string email = Email, DateOnly? expiry = null) =>
        InviteAndGrantExternalUserEndpoint.InviteAndGrantAsync(
            new InviteExternalUserRequest(email, Guid.Empty, (int)ExternalAccessLevel.Collaborate, "Ada", "Lovelace", expiry, null,
                "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, _h.Ownership, _h.OwnerPrivileges,
            new InternalUserShareTests.StubCallerRightsProbe(EveryRight),
            _h.Ciam, _h.Emails, IdentityBindingTestKit.Binder(_h.Identities), InviteOrganizationTestKit.PortalOnly(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalAccess:PortalUrl"] = "https://external.test" })
                .Build(),
            TestInfrastructure.TestSpeOwnership.Customer("acme"),
            _h.Materializer, _h.Notifier, InternalUserShareCoreTests.Rig.Caller(), NullLogger<Program>.Instance, _h.Time,
            CancellationToken.None, Decision());

    /// <summary>A contact linked to ONE systemuser, that user as the share routes read it, and the contact's email.</summary>
    private (Guid Contact, Guid User) Colleague(
        bool disabled = false, int accessMode = 0, bool? isExternal = false, string fullName = "Ada Lovelace")
    {
        var (contact, user) = _h.LinkedContact(disabled, accessMode, null, isExternal);
        SeedUser(user, fullName, disabled, accessMode, isExternal);
        _h.Identities.Contacts[contact].Email = Email;
        return (contact, user);
    }

    private void SeedUser(Guid user, string fullName, bool disabled = false, int accessMode = 0, bool? isExternal = false)
        => _h.Grants.SystemUsers[user] = new InternalShareEndpoints.SystemUserRow
        {
            Id = user,
            FullName = fullName,
            IsDisabled = disabled,
            AccessMode = accessMode,
            ApplicationId = null,
            IsExternal = isExternal,
        };

    private static string? ReasonCode(IResult result)
        => result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails.Extensions.TryGetValue("reasonCode", out var code)
            ? code?.ToString()
            : null;

    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private void NothingWritten()
    {
        _h.Grants.WriteCount.Should().Be(0, "no grant row (nor any other Dataverse row through the client)");
        _h.Shares.Writes.Should().BeEmpty("no share");
        _h.Ciam.Attempts.Should().BeEmpty("no CIAM account");
        _h.Emails.InviteUrls.Should().BeEmpty("no invite email");
        _h.Emails.Sent.Should().BeEmpty("no grant email");
    }

    // ── /grant: the decision matrix ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_ContactRepresentingOneUser_SharesWithThatUser_AtTheLevel_AndWritesNoGrantRow()
    {
        var (contact, user) = Colleague();

        var result = await Grant(contact);

        var body = result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!;
        body.SharedWithUser.Should().Be(new SharedWithUser(user, "Ada Lovelace"));
        body.AccessRecordId.Should().Be(Guid.Empty);
        body.GrantedAccessLevel.Should().Be(ExternalAccessLevel.Collaborate);
        body.Invite.Should().BeNull("an internal user is never invited");
        body.ExpiryNotApplied.Should().BeNull("no expiry was sent, so there is nothing to report");
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        _h.Grants.Rows.Should().BeEmpty("a share, not a contact grant");
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_ContactRepresentingSeveralUsers_Is409_AndWritesNothing()
    {
        var (contact, _) = Colleague();
        SeedUser(_h.LinkLater(contact), "Second Person");

        var result = await Grant(contact);

        Status(result).Should().Be(StatusCodes.Status409Conflict);
        ReasonCode(result).Should().Be("sdap.access.grant.contact_represents_several_users");
        NothingWritten();
    }

    [Fact]
    public async Task Grant_ContactRepresentingOneDisabledUser_IsTheShareCoresUserDisabled_AndWritesNothing()
    {
        var (contact, _) = Colleague(disabled: true);

        var result = await Grant(contact);

        Status(result).Should().Be(StatusCodes.Status422UnprocessableEntity);
        ReasonCode(result).Should().Be(InternalShareEndpoints.UserDisabledReasonCode);
        NothingWritten();
    }

    [Fact]
    public async Task Grant_ContactRepresentingOneNonPerson_IsTheShareCoresUserNotAPerson_AndWritesNothing()
    {
        var (contact, _) = Colleague(accessMode: 4); // non-interactive

        var result = await Grant(contact);

        Status(result).Should().Be(StatusCodes.Status422UnprocessableEntity);
        ReasonCode(result).Should().Be(InternalShareEndpoints.UserNotAPersonReasonCode);
        NothingWritten();
    }

    [Fact]
    public async Task Grant_OnARestrictedRecord_ContactRepresentingAUserFlaggedExternal_IsTheRestrictedReason_AndWritesNothing()
    {
        _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        var (contact, _) = Colleague(isExternal: true);

        var result = await Grant(contact);

        Status(result).Should().Be(StatusCodes.Status422UnprocessableEntity);
        ReasonCode(result).Should().Be(InternalShareEndpoints.UserNotInternalReasonCode);
        NothingWritten();
    }

    [Fact]
    public async Task Grant_OnARestrictedRecord_ContactRepresentingAnInternalColleague_IsShared()
    {
        // A contact grant is refused on Restricted (record_restricted); the colleague the contact represents is not.
        _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        var (contact, user) = Colleague(isExternal: false);

        var result = await Grant(contact);

        result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!.SharedWithUser!.SystemUserId.Should().Be(user);
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        _h.Grants.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_WhenWhoTheContactRepresentsCannotBeRead_Is503_AndNeverFallsBackToAContactGrant()
    {
        var (contact, _) = Colleague();
        _h.Store.FailLinkRead = true;

        var result = await Grant(contact);

        Status(result).Should().Be(StatusCodes.Status503ServiceUnavailable);
        ReasonCode(result).Should().Be(ContactInternalUserShare.RepresentationUnreadableReasonCode);
        NothingWritten();
    }

    [Fact]
    public async Task Grant_WhenTheBindingColumnIsMissing_DecidesOnTheLinkAlone_AndShares()
    {
        var (contact, user) = Colleague();
        _h.Identities.GetContactStatus = LookupStatus.ColumnMissing;

        var result = await Grant(contact);

        result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!.SharedWithUser!.SystemUserId.Should().Be(user);
        _h.Grants.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_ContactRepresentingNobody_TakesTodaysGrantPath_WithItsInvite()
    {
        var contact = _h.Contact();
        _h.Identities.Contacts[contact].Email = "outside@counsel.test";

        var result = await Grant(contact);

        var body = result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!;
        body.SharedWithUser.Should().BeNull();
        body.AccessRecordId.Should().NotBe(Guid.Empty);
        body.Invite!.Status.Should().Be(GrantInviteStatus.Sent);
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        _h.Shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_InactiveContactLinkedToAUser_IsNotShared_TheGrantPathAnswersIt()
    {
        var (contact, user) = Colleague();
        _h.Identities.Contacts[contact].StateCode = 1;

        var result = await Grant(contact);

        _h.Shares.Writes.Should().BeEmpty("an inactive contact represents nobody: the grant path's own checks answer it");
        (result as Ok<GrantAccessResponse>)?.Value!.SharedWithUser.Should().BeNull();
        Status(result).Should().Be(StatusCodes.Status200OK);
    }

    /// <summary>
    /// C-fix2 (expiry ruling (c)): picking a colleague through their contact behaves exactly like picking them as a user —
    /// shared with no end date. The expiry the request carried is not applied, and the answer says so.
    /// </summary>
    [Fact]
    public async Task Grant_ContactRepresentingOneUser_WithAnExpiry_SharesWithNoEndDate_AndSaysExpiryNotApplied()
    {
        var (contact, user) = Colleague();

        var result = await Grant(contact, expiry: Today.AddDays(30));

        var body = result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!;
        body.SharedWithUser!.SystemUserId.Should().Be(user);
        body.ExpiryNotApplied.Should().BeTrue();
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        _h.Grants.Rows.Should().BeEmpty("no grant row, so no expiry anywhere");
        _h.Grants.WriteCount.Should().Be(0);
    }

    [Fact]
    public async Task Grant_ExpiryNotApplied_IsAbsentFromTheWireWhenNoExpiryWasSent()
    {
        var (contact, _) = Colleague();

        var withNone = await Grant(contact);

        var json = System.Text.Json.JsonSerializer.Serialize(
            withNone.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        json.Should().NotContain("expiryNotApplied");
        json.Should().Contain("sharedWithUser");
    }

    [Fact]
    public async Task Grant_ContactRepresentingNobody_WithAnExpiry_IsTheGrantPath_WithThatExpiry()
    {
        var contact = _h.Contact();

        var result = await Grant(contact, expiry: Today.AddDays(30));

        result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!.SharedWithUser.Should().BeNull();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Which.ExpiresDate.Should().Be(Today.AddDays(30));
    }

    [Fact]
    public async Task Grant_WhenTheNameReadFaultsAfterTheShare_Is200_WithTheShareConfirmed_FullNameNull_AndNoGrantRow()
    {
        var (contact, user) = Colleague();
        _h.Grants.FailQueriesSelecting = InternalShareEndpoints.SystemUserNameSelect;

        var result = await Grant(contact);

        var body = result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!;
        body.SharedWithUser.Should().Be(new SharedWithUser(user, null));
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask,
            "the share stands: the name is display only");
        _h.Grants.Rows.Should().BeEmpty();
    }

    // ── /invite-and-grant: never CIAM-invite an internal user ─────────────────────────────────────────────────────────

    [Fact]
    public async Task InviteAndGrant_ExistingContactLinkedToOneInternalUser_IsShared_AndNeverInvited()
    {
        var (contact, user) = Colleague();

        var result = await InviteAndGrant();

        var body = result.Should().BeOfType<Ok<InviteAndGrantResponse>>().Subject.Value!;
        body.ContactId.Should().Be(contact);
        body.OnboardStatus.Should().Be(InviteAndGrantExternalUserEndpoint.SharedWithInternalUserStatus);
        body.SharedWithUser.Should().Be(new SharedWithUser(user, "Ada Lovelace"));
        body.AccessRecordId.Should().Be(Guid.Empty);
        body.ExpiryNotApplied.Should().BeNull("no expiry was sent");
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        _h.Ciam.Attempts.Should().BeEmpty("never CIAM-invite an internal user");
        _h.Emails.InviteUrls.Should().BeEmpty();
        _h.Emails.Onboarding.Should().BeEmpty();
        _h.Grants.Rows.Should().BeEmpty();
        _h.Identities.Contacts[contact].Flag.Should().BeNull("decided before the binder's link refusal, which flags");
    }

    /// <summary>C-fix2: the same on /invite-and-grant — shared with no end date, never invited, and the answer says so.</summary>
    [Fact]
    public async Task InviteAndGrant_ExistingContactLinkedToOneInternalUser_WithAnExpiry_SharesWithNoEndDate_AndSaysExpiryNotApplied()
    {
        var (_, user) = Colleague();

        var result = await InviteAndGrant(expiry: Today.AddDays(30));

        var body = result.Should().BeOfType<Ok<InviteAndGrantResponse>>().Subject.Value!;
        body.SharedWithUser!.SystemUserId.Should().Be(user);
        body.ExpiryNotApplied.Should().BeTrue();
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        _h.Grants.Rows.Should().BeEmpty("no grant row, so no expiry anywhere");
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task InviteAndGrant_ExistingContactLinkedToSeveralUsers_Is409_AndWritesNothing()
    {
        var (contact, _) = Colleague();
        SeedUser(_h.LinkLater(contact), "Second Person");

        var result = await InviteAndGrant();

        Status(result).Should().Be(StatusCodes.Status409Conflict);
        ReasonCode(result).Should().Be(ContactInternalUserShare.RepresentsSeveralUsersReasonCode);
        NothingWritten();
    }

    // ── D1-fix2 (i): the contact's Assigned-To suggestions are answered by the share ──────────────────────────────────

    private const string AttorneyField = "sprk_assignedattorney1";

    private AssignedAccessLedgerRow Suggestion(Guid rootId, Guid contact, string field = AttorneyField,
        AssignedAccessState state = AssignedAccessState.PendingConfirmation, Guid? systemUserId = null)
        => _h.Store.SeedRow(ExternalGrantRootType.Matter, rootId, field,
            new AssignedSubject(AssignedSubjectKind.Contact, contact), state, systemUserId: systemUserId);

    [Fact]
    public async Task Grant_SharedContact_AdoptsItsStaleSuggestion_StampedWithTheUser_AndItIsNoLongerOffered()
    {
        var (contact, user) = Colleague();
        // Written before the contact was linked to the user: the row carries no user, so adopting by user cannot find it.
        var row = Suggestion(_matter, contact);

        var result = await Grant(contact);

        result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!.SharedWithUser!.SystemUserId.Should().Be(user);
        row.State.Should().Be(AssignedAccessState.Adopted);
        row.Reason.Should().Be(AssignedAccessReason.ManualGrant);
        row.SystemUserId.Should().Be(user);
        var listed = await _h.Materializer.ListAsync(ExternalGrantRootType.Matter, _matter, CancellationToken.None);
        listed.Should().NotContain(e => e.SubjectId == contact && e.State == nameof(AssignedAccessState.PendingConfirmation),
            "GET /assigned-access no longer offers the suggestion the share answered");
    }

    [Fact]
    public async Task Grant_SharedContact_LeavesOtherRecords_OtherContacts_AndOtherStatesUntouched()
    {
        var (contact, _) = Colleague();
        var otherMatter = Guid.NewGuid();
        var otherContact = Guid.NewGuid();
        var onOtherRecord = Suggestion(otherMatter, contact);
        var ofOtherContact = Suggestion(_matter, otherContact, "sprk_assignedattorney2");
        var declined = Suggestion(_matter, contact, "sprk_assignedparalegal1", AssignedAccessState.Declined);

        var result = await Grant(contact);

        result.Should().BeOfType<Ok<GrantAccessResponse>>();
        onOtherRecord.State.Should().Be(AssignedAccessState.PendingConfirmation);
        onOtherRecord.SystemUserId.Should().BeNull();
        ofOtherContact.State.Should().Be(AssignedAccessState.PendingConfirmation);
        ofOtherContact.SystemUserId.Should().BeNull();
        declined.State.Should().Be(AssignedAccessState.Declined);
        declined.SystemUserId.Should().BeNull();
    }

    [Fact]
    public async Task Grant_SharedContact_WhenTheLedgerWriteFails_KeepsTheShare_AndAnswers200()
    {
        var (contact, user) = Colleague();
        var row = Suggestion(_matter, contact);
        _h.Store.FailLedgerUpdates = true;

        var result = await Grant(contact);

        var body = result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!;
        body.SharedWithUser!.SystemUserId.Should().Be(user);
        _h.Shares.Writes.Should().NotBeEmpty("the share stands; the ledger is never a reason to undo it");
        _h.Grants.WriteCount.Should().Be(0, "still no contact grant row");
        row.State.Should().Be(AssignedAccessState.PendingConfirmation, "the write failed, and the next pass repairs it");
    }

    [Fact]
    public async Task InviteAndGrant_WhenTheEmailLookupFails_Is503ContactLookupFailed_AndWritesNothing()
    {
        Colleague();
        _h.Identities.EmailLookupStatus = LookupStatus.Failed;

        var result = await InviteAndGrant();

        Status(result).Should().Be(StatusCodes.Status503ServiceUnavailable);
        ReasonCode(result).Should().Be(ContactBindingDecision.InviteContactLookupFailed);
        NothingWritten();
    }

    [Fact]
    public async Task InviteAndGrant_WhenWhoTheContactRepresentsCannotBeRead_Is503_AndWritesNothing()
    {
        Colleague();
        _h.Store.FailLinkRead = true;

        var result = await InviteAndGrant();

        Status(result).Should().Be(StatusCodes.Status503ServiceUnavailable);
        ReasonCode(result).Should().Be(ContactInternalUserShare.RepresentationUnreadableReasonCode);
        NothingWritten();
    }
}
