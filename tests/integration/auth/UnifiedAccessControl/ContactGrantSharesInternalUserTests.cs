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
        TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(),
        _h.Identities, _h.Ownership, _h.OwnerPrivileges, _h.Time, _h.WorkforceIdentity);

    private Task<IResult> Grant(
        Guid contactId, ExternalAccessLevel level = ExternalAccessLevel.Collaborate, DateOnly? expiry = null,
        Guid? firm = null, AccessRights callerRights = EveryRight) =>
        GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(contactId, Guid.Empty, level, expiry, firm, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, _h.Ownership, _h.OwnerPrivileges,
            new InternalUserShareTests.StubCallerRightsProbe(callerRights),
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

    /// <summary>
    /// Task 193 G1 (owner F1-b, decisions (a) and (iii)): the colleague is SHARED with (model-driven app, no end date) AND the
    /// contact is granted (workforce app), paired with the share and dated today + 90 as any grant. Never invited, never emailed.
    /// </summary>
    [Fact]
    public async Task Grant_ContactRepresentingOneUser_SharesWithThatUser_AndGrantsTheContactPairedWithThem_UntilTodayPlus90()
    {
        var (contact, user) = Colleague();

        var result = await Grant(contact);

        var body = result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!;
        body.SharedWithUser.Should().Be(new SharedWithUser(user, "Ada Lovelace"));
        body.AccessRecordId.Should().Be(Guid.Empty, "the answer's own id stays the share's; the paired row is in workforceApp");
        body.GrantedAccessLevel.Should().Be(ExternalAccessLevel.Collaborate);
        body.Invite.Should().BeNull("an internal user is never invited");
        body.ExpiryNotApplied.Should().BeNull("no expiry was sent, so there is nothing to report");
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);

        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("the paired workforce-app grant").Subject;
        grant.AccessLevel.Should().Be((int)ExternalAccessLevel.Collaborate);
        grant.ExpiresDate.Should().Be(ExternalGrantLifecycle.DefaultExpiry(Today), "dated as any grant (owner decision (a))");
        _h.Grants.PairedUsers[grant.Id].Should().Be(user, "sprk_pairedsystemuser names the colleague (owner decision (iii))");
        body.WorkforceApp.Should().Be(new WorkforceAppAccess(true, null, ExternalGrantLifecycle.DefaultExpiry(Today), grant.Id));
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.InviteUrls.Should().BeEmpty();
        _h.Emails.Sent.Should().BeEmpty("the paired grant sends no contact email: the share core told the colleague in-app");
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

        var body = result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!;
        body.SharedWithUser!.SystemUserId.Should().Be(user);
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        _h.Grants.Rows.Should().BeEmpty("task 193 G1: the grant core refuses a contact grant on Restricted; the share stands");
        body.WorkforceApp.Should().Be(WorkforceAppAccess.NotGiven(ExternalGrantLifecycle.RecordRestrictedReasonCode));
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
        _h.Grants.PairedUsers.Values.Should().ContainSingle().Which.Should().Be(user, "the paired grant beside the share");
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
    /// C-fix2 (expiry ruling (c)): picking a colleague through their contact shares exactly as picking them as a user: no end
    /// date, and the answer says the expiry was not applied to the share. G1 (owner decision (a)): the paired workforce-app
    /// grant takes the requested date, and the answer says until when.
    /// </summary>
    [Fact]
    public async Task Grant_ContactRepresentingOneUser_WithAnExpiry_SharesWithNoEndDate_AndThePairedGrantTakesTheDate()
    {
        var (contact, user) = Colleague();

        var result = await Grant(contact, expiry: Today.AddDays(30));

        var body = result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!;
        body.SharedWithUser!.SystemUserId.Should().Be(user);
        body.ExpiryNotApplied.Should().BeTrue("the SHARE has no end date");
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Which.ExpiresDate.Should().Be(Today.AddDays(30));
        body.WorkforceApp!.Until.Should().Be(Today.AddDays(30));
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
        body.WorkforceApp!.Visible.Should().BeTrue("the paired grant does not depend on the name either");
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
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("task 193 G1: the paired workforce-app grant").Subject;
        _h.Grants.PairedUsers[grant.Id].Should().Be(user);
        body.WorkforceApp.Should().Be(new WorkforceAppAccess(true, null, ExternalGrantLifecycle.DefaultExpiry(Today), grant.Id));
        _h.Identities.Contacts[contact].Flag.Should().BeNull("decided before the binder's link refusal, which flags");
    }

    /// <summary>C-fix2: the same on /invite-and-grant — shared with no end date, never invited, and the answer says so. G1: the
    /// paired workforce-app grant takes the requested date.</summary>
    [Fact]
    public async Task InviteAndGrant_ExistingContactLinkedToOneInternalUser_WithAnExpiry_SharesWithNoEndDate_AndThePairedGrantTakesTheDate()
    {
        var (contact, user) = Colleague();

        var result = await InviteAndGrant(expiry: Today.AddDays(30));

        var body = result.Should().BeOfType<Ok<InviteAndGrantResponse>>().Subject.Value!;
        body.SharedWithUser!.SystemUserId.Should().Be(user);
        body.ExpiryNotApplied.Should().BeTrue("the SHARE has no end date");
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Which.ExpiresDate.Should().Be(Today.AddDays(30));
        body.WorkforceApp!.Until.Should().Be(Today.AddDays(30));
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
        _h.Grants.Updates.Should().BeEmpty("the ledger write failing writes no grant row beside the paired one");
        _h.Grants.Creates.Should().ContainSingle("only the paired workforce-app grant");
        row.State.Should().Be(AssignedAccessState.PendingConfirmation, "the write failed, and the next pass repairs it");
    }

    // ── V-fix F1-a: a share whose related records are not all updated yet still says who it was shared with ─────────

    /// <summary>A secure matter whose related documents cannot be read: the record's own share lands, then
    /// <c>children_incomplete</c>.</summary>
    private void RelatedRecordsUnreadable()
        => _h.ChildWorld = Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.Standard()
            .SecureRoot(MatterTable, _matter)
            .FailingQueriesOf("sprk_document");

    private static IDictionary<string, object?> Extensions(IResult result)
        => result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails.Extensions;

    [Fact]
    public async Task Grant_SharedContact_WhenRelatedRecordsAreIncomplete_NamesTheUser_SaysNoEndDate_AndAdoptsTheSuggestion()
    {
        var (contact, user) = Colleague();
        var row = Suggestion(_matter, contact);
        RelatedRecordsUnreadable();

        var result = await Grant(contact, expiry: DateOnly.FromDateTime(_h.Time.GetUtcNow().UtcDateTime).AddDays(30));

        ReasonCode(result).Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        var extensions = Extensions(result);
        extensions["sharedWithUser"].Should().Be(new SharedWithUser(user, "Ada Lovelace"));
        extensions["expiryNotApplied"].Should().Be(true);
        _h.Shares.Writes.Should().NotBeEmpty("the record's own share stands");
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("the paired workforce-app grant, as with the 200").Subject;
        extensions["workforceApp"].Should().Be(new WorkforceAppAccess(
            true, null, DateOnly.FromDateTime(_h.Time.GetUtcNow().UtcDateTime).AddDays(30), grant.Id));
        row.State.Should().Be(AssignedAccessState.Adopted);
        row.SystemUserId.Should().Be(user);
    }

    [Fact]
    public async Task Grant_SharedContact_WhenRelatedRecordsAreIncomplete_WithNoExpiry_SaysNothingAboutAnEndDate()
    {
        var (contact, user) = Colleague();
        RelatedRecordsUnreadable();

        var result = await Grant(contact);

        ReasonCode(result).Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        var extensions = Extensions(result);
        extensions["sharedWithUser"].Should().Be(new SharedWithUser(user, "Ada Lovelace"));
        extensions.Should().NotContainKey("expiryNotApplied");
    }

    // ── V-fix item 5: the caller's own rights cap a share made through a contact, as they cap a grant ───────────────

    [Fact]
    public async Task Grant_ColleaguesContact_AboveTheCallersOwnLevel_IsSharedAtTheCallersLevel_AndSaysNarrowed()
    {
        var (contact, user) = Colleague();
        const AccessRights collaborateOnly = AccessRights.Read | AccessRights.Write | AccessRights.Append
                                             | AccessRights.AppendTo | AccessRights.Share; // no Delete

        var result = await Grant(contact, ExternalAccessLevel.FullAccess, callerRights: collaborateOnly);

        var body = result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!;
        body.SharedWithUser!.SystemUserId.Should().Be(user);
        body.Narrowed.Should().BeTrue();
        body.GrantedAccessLevel.Should().Be(ExternalAccessLevel.Collaborate);
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
    }

    [Fact]
    public async Task Grant_ColleaguesContact_WhenTheCallerHoldsNothingGrantable_Is403CallerCannotGrant_AndWritesNothing()
    {
        var (contact, _) = Colleague();

        var result = await Grant(contact, ExternalAccessLevel.ViewOnly, callerRights: AccessRights.None);

        Status(result).Should().Be(StatusCodes.Status403Forbidden);
        ReasonCode(result).Should().Be(InternalShareEndpoints.CallerCannotGrantReasonCode);
        NothingWritten();
    }

    // ── V-fix item 3 (owner ruling: fail closed): a No Access entry still blocks adding a contact, on every record ─────

    [Fact]
    public async Task Grant_ColleaguesContact_OnTheNoAccessListOfAnOrdinaryRecord_Is422GranteeDenied_AndWritesNothing()
    {
        var (contact, _) = Colleague();
        _h.DenyList.DenyContactOnRecord(contact, _matter);

        var result = await Grant(contact);

        Status(result).Should().Be(StatusCodes.Status422UnprocessableEntity);
        ReasonCode(result).Should().Be(GrantPolicyDecision.GranteeDenied.ReasonCode);
        NothingWritten();
    }

    [Fact]
    public async Task Grant_ColleaguesContact_WhoseFirmIsOnTheNoAccessList_Is422GranteeDenied_AndWritesNothing()
    {
        var (contact, _) = Colleague();
        var firm = Guid.NewGuid();
        _h.Participations.ContactOrganizations[contact] = new[] { firm };
        _h.DenyList.DenyOrganizationOnRecord(firm, _matter);

        var result = await Grant(contact);

        Status(result).Should().Be(StatusCodes.Status422UnprocessableEntity);
        ReasonCode(result).Should().Be(GrantPolicyDecision.GranteeDenied.ReasonCode);
        NothingWritten();
    }

    [Fact]
    public async Task Grant_ColleaguesContact_WhenTheRequestNamesAFirmOnTheNoAccessList_Is422GranteeDenied_AndWritesNothing()
    {
        var (contact, _) = Colleague();
        var firm = Guid.NewGuid();
        _h.DenyList.DenyOrganizationOnRecord(firm, _matter);

        var result = await Grant(contact, firm: firm);

        Status(result).Should().Be(StatusCodes.Status422UnprocessableEntity);
        ReasonCode(result).Should().Be(GrantPolicyDecision.GranteeDenied.ReasonCode);
        NothingWritten();
    }

    [Fact]
    public async Task Grant_ColleaguesContact_WithAnotherContactOnTheNoAccessList_IsShared()
    {
        var (contact, user) = Colleague();
        _h.DenyList.DenyContactOnRecord(Guid.NewGuid(), _matter);

        var result = await Grant(contact);

        result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!.SharedWithUser!.SystemUserId.Should().Be(user);
    }

    [Fact]
    public async Task InviteAndGrant_ColleaguesContact_OnTheNoAccessList_Is422GranteeDenied_AndWritesNothing()
    {
        var (contact, _) = Colleague();
        _h.DenyList.DenyContactOnRecord(contact, _matter);

        var result = await InviteAndGrant();

        Status(result).Should().Be(StatusCodes.Status422UnprocessableEntity);
        ReasonCode(result).Should().Be(GrantPolicyDecision.GranteeDenied.ReasonCode);
        NothingWritten();
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

    // ── Task 193 G1 (owner F1-b; decisions 1, 2, (a), (iii)): the paired workforce-app grant ───────────────────────────

    private Task<IResult> ShareUser(Guid user, ExternalAccessLevel level = ExternalAccessLevel.Collaborate, DateOnly? expiry = null) =>
        InternalShareEndpoints.ShareAsync(
            new ShareRecordWithUserRequest("matter", _matter, user, level, expiry),
            _h.Shares, _h.Grants, _h.Participations, _h.Cache.Mock.Object, new InternalUserShareTests.StubCallerRightsProbe(EveryRight),
            _h.Children, _h.Guard, TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(), _h.Materializer,
            _h.Notifier, InternalUserShareCoreTests.Rig.Caller(), NullLogger<Program>.Instance, CancellationToken.None,
            _h.WorkforceIdentity, Decision(), _h.AccessibleRecords, _h.Time);

    private void ShareOnly(IResult result, Guid user, string reason)
    {
        var body = result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!;
        body.SharedWithUser!.SystemUserId.Should().Be(user);
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask, "the share stands");
        _h.Grants.Creates.Should().BeEmpty("no workforce-app grant was written");
        _h.Grants.PairedUsers.Should().BeEmpty();
        body.WorkforceApp.Should().Be(WorkforceAppAccess.NotGiven(reason));
    }

    [Fact]
    public async Task G1_Grant_WhenThePairingColumnIsNotProvisioned_SharesOnly_WritesNoUnpairedGrant_AndSaysSo()
    {
        var (contact, user) = Colleague();
        _h.Grants.PairingColumnMissing = true;

        var result = await Grant(contact);

        ShareOnly(result, user, ContactInternalUserShare.WorkforcePairingColumnMissingReasonCode);
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty("fail closed: never a grant the share's removal could not find");
    }

    [Fact]
    public async Task G1_Grant_WhenThePairingCannotBeRead_SharesOnly_AndSaysSo()
    {
        var (contact, user) = Colleague();
        _h.Grants.FailPairingReads = true;

        var result = await Grant(contact);

        ShareOnly(result, user, ContactInternalUserShare.WorkforcePairingUnreadableReasonCode);
    }

    [Fact]
    public async Task G1_Grant_WhenTheGrantWriteFails_KeepsTheShare_AndSaysTheWorkforceGrantFailed()
    {
        var (contact, user) = Colleague();
        _h.Grants.GrantCreateFailure = new HttpRequestException("Simulated Dataverse write failure.");

        var result = await Grant(contact);

        ShareOnly(result, user, ContactInternalUserShare.WorkforceGrantFailedReasonCode);
    }

    /// <summary>
    /// Unreadable flags: the share is refused before anything is written (the No Access check cannot tell whether the record
    /// is secure, fail closed), so there is no share for a grant to pair with, and no grant either.
    /// </summary>
    [Fact]
    public async Task G1_Grant_WhenTheFlagsAreUnreadable_RefusesTheShare_AndWritesNoGrant()
    {
        var (contact, _) = Colleague();
        _h.Participations.Flags[_matter] = RootRecordFlags.Unreadable;

        var result = await Grant(contact);

        Status(result).Should().BeOneOf(StatusCodes.Status500InternalServerError, StatusCodes.Status503ServiceUnavailable);
        NothingWritten();
    }

    /// <summary>Decision 1: Secure is share + grant, as Standard and Limited (only Restricted and unreadable flags share only).</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task G1_Grant_OnAStandardOrSecureRecord_SharesAndGrantsThePairedContact(bool secure, bool restricted)
    {
        _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: secure, IsRestricted: restricted);
        var (contact, user) = Colleague();

        var result = await Grant(contact);

        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Subject;
        _h.Grants.PairedUsers[grant.Id].Should().Be(user);
        var visible = result switch
        {
            Ok<GrantAccessResponse> ok => ok.Value!.WorkforceApp,
            ProblemHttpResult problem => (WorkforceAppAccess?)problem.ProblemDetails.Extensions["workforceApp"],
            _ => null,
        };
        visible!.Visible.Should().BeTrue();
    }

    /// <summary>Idempotent re-add: the one paired grant is kept (no second row, no rewrite), the share re-shared at the same level.</summary>
    [Fact]
    public async Task G1_Grant_ReAdd_KeepsTheOnePairedGrant_WritesNoGrantChange_AndTheShareIsUnchanged()
    {
        var (contact, user) = Colleague();
        await Grant(contact);
        var first = _h.Grants.ActiveRowsOf(_matter, contact).Single();
        var createsBefore = _h.Grants.Creates.Count;
        var updatesBefore = _h.Grants.Updates.Count;

        var again = await Grant(contact);

        var body = again.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!;
        body.WorkforceApp.Should().Be(new WorkforceAppAccess(true, null, first.ExpiresDate, first.Id));
        _h.Grants.Creates.Should().HaveCount(createsBefore, "the existing paired grant is kept");
        _h.Grants.Updates.Should().HaveCount(updatesBefore, "already paired with this user, at this level and date: nothing to write");
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
    }

    /// <summary>Adoption on re-add (owner decision (iii)): a grant the contact already held, unpaired, is stamped with the user.</summary>
    [Fact]
    public async Task G1_Grant_ReAdd_OverAnUnpairedGrant_AdoptsIt_StampingThePairing_InOneWrite()
    {
        var (contact, user) = Colleague();
        var existing = _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, contact, null, (int)ExternalAccessLevel.Collaborate,
            Today.AddDays(40));

        var result = await Grant(contact);

        result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!.WorkforceApp
            .Should().Be(new WorkforceAppAccess(true, null, Today.AddDays(40), existing.Id), "a live grant keeps its date");
        _h.Grants.Creates.Should().BeEmpty();
        _h.Grants.Updates.Should().ContainSingle().Which.Payload.Should().Contain($"/systemusers({user:D})");
        _h.Grants.PairedUsers[existing.Id].Should().Be(user);
    }

    /// <summary>
    /// Task 193 G4a (B-F5): a LAPSED higher grant confers nothing, so it does not block a narrowed re-add (never-lower counts
    /// only rows that still give access). The colleague's workforce-app access is restored at the granted level with a valid
    /// date, on the same row, paired with them.
    /// </summary>
    [Fact]
    public async Task G4a_Grant_Narrowed_OverALapsedHigherGrant_RestoresIt_AndTheColleagueIsVisible()
    {
        var (contact, user) = Colleague();
        var lapsed = _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, contact, null, (int)ExternalAccessLevel.FullAccess,
            Today.AddDays(-5));
        const AccessRights collaborateOnly = AccessRights.Read | AccessRights.Write | AccessRights.Append
                                             | AccessRights.AppendTo | AccessRights.Share; // no Delete

        var result = await Grant(contact, ExternalAccessLevel.FullAccess, callerRights: collaborateOnly);

        var body = result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!;
        body.WorkforceApp.Should().Be(new WorkforceAppAccess(true, null, ExternalGrantLifecycle.DefaultExpiry(Today), lapsed.Id),
            "the lapsed grant gave nothing, so restoring it lowers no one's access");
        var row = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Subject;
        row.AccessLevel.Should().Be((int)ExternalAccessLevel.Collaborate);
        _h.Grants.PairedUsers[lapsed.Id].Should().Be(user);
    }

    /// <summary>The must-not-fire twin: a LIVE higher grant still refuses the narrowed re-add; only the share stands.</summary>
    [Fact]
    public async Task G4a_Grant_Narrowed_OverALiveHigherGrant_StillNeverLowersIt()
    {
        var (contact, _) = Colleague();
        var live = _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, contact, null, (int)ExternalAccessLevel.FullAccess,
            Today.AddDays(40));
        const AccessRights collaborateOnly = AccessRights.Read | AccessRights.Write | AccessRights.Append
                                             | AccessRights.AppendTo | AccessRights.Share; // no Delete

        var result = await Grant(contact, ExternalAccessLevel.FullAccess, callerRights: collaborateOnly);

        result.Should().BeOfType<Ok<GrantAccessResponse>>().Subject.Value!.WorkforceApp
            .Should().Be(WorkforceAppAccess.NotGiven(ExternalGrantLifecycle.WouldLowerExistingReasonCode));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle()
            .Which.AccessLevel.Should().Be((int)ExternalAccessLevel.FullAccess, "a live higher grant is never lowered");
        _h.Grants.PairedUsers.Should().NotContainKey(live.Id);
    }

    // ── /share-user: the reverse lookup (decision 2) ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task G1_ShareUser_WithTheOneContactThatRepresentsThem_SharesAndGrantsThatContactPairedWithThem()
    {
        var (contact, user) = Colleague();

        var result = await ShareUser(user);

        var body = result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>().Subject.Value!;
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Subject;
        grant.ExpiresDate.Should().Be(ExternalGrantLifecycle.DefaultExpiry(Today));
        _h.Grants.PairedUsers[grant.Id].Should().Be(user);
        body.WorkforceApp.Should().Be(new WorkforceAppAccess(true, null, ExternalGrantLifecycle.DefaultExpiry(Today), grant.Id));
        body.ExpiryNotApplied.Should().BeNull("no expiry was sent");
        _h.Emails.InviteUrls.Should().BeEmpty();
    }

    /// <summary>
    /// Task 193 G4a (B-F3): a date sent with /share-user dates the paired workforce-app grant (as /grant's date does); the share
    /// has no end date, and the answer says so.
    /// </summary>
    [Fact]
    public async Task G4a_ShareUser_WithAnExpiry_DatesThePairedGrant_AndSaysTheShareHasNone()
    {
        var (contact, user) = Colleague();

        var result = await ShareUser(user, expiry: Today.AddDays(30));

        var body = result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>().Subject.Value!;
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Subject;
        grant.ExpiresDate.Should().Be(Today.AddDays(30));
        body.WorkforceApp.Should().Be(new WorkforceAppAccess(true, null, Today.AddDays(30), grant.Id));
        body.ExpiryNotApplied.Should().BeTrue("the SHARE has no end date");
    }

    /// <summary>Task 193 G4a (B-F3): a past date is refused before anything is written, as /grant refuses it.</summary>
    [Fact]
    public async Task G4a_ShareUser_WithAPastExpiry_Is400_AndWritesNothing()
    {
        var (_, user) = Colleague();

        var result = await ShareUser(user, expiry: Today.AddDays(-1));

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
        ReasonCode(result).Should().Be(GrantExternalAccessEndpoint.ExpiryInPastReasonCode);
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().BeNull("nothing was shared");
        _h.Grants.Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task G1_ShareUser_WithNoContactThatRepresentsThem_SharesOnly_AndSaysSo()
    {
        var user = Guid.NewGuid();
        _h.Identities.AddSystemUser(user, Guid.NewGuid(), $"{user:N}@firm.example");
        SeedUser(user, "Bea Unlinked");

        var result = await ShareUser(user);

        var body = result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>().Subject.Value!;
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        _h.Grants.Creates.Should().BeEmpty();
        body.WorkforceApp.Should().Be(WorkforceAppAccess.NotGiven(ContactInternalUserShare.WorkforceNoLinkedContactReasonCode));
    }

    [Fact]
    public async Task G1_ShareUser_WhenTheirLinkCannotBeRead_SharesOnly_AndNeverGuessesAContact()
    {
        var (_, user) = Colleague();
        _h.Store.FailLinkRead = true;

        var result = await ShareUser(user);

        var body = result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>().Subject.Value!;
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        _h.Grants.Creates.Should().BeEmpty();
        body.WorkforceApp.Should().Be(WorkforceAppAccess.NotGiven(ContactInternalUserShare.WorkforceLinkUnreadableReasonCode));
    }

    [Fact]
    public async Task G1_ShareUser_WhenTwoContactsRepresentThem_SharesOnly_AndPicksNeither()
    {
        var (contact, user) = Colleague();
        var oid = _h.Identities.SystemUsers[user].Oid!.Value;
        var second = _h.Contact(oid: oid.ToString());
        _h.Store.UsersByLink.GetOrAdd(second, _ => new List<AssignedLinkCandidate>())
            .Add(new AssignedLinkCandidate(user, second, oid, false, 0, null, false));

        var result = await ShareUser(user);

        var body = result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>().Subject.Value!;
        _h.Grants.Creates.Should().BeEmpty();
        body.WorkforceApp.Should().Be(WorkforceAppAccess.NotGiven(ContactInternalUserShare.WorkforceSeveralContactsReasonCode));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
    }

    [Fact]
    public async Task G1_ShareUser_OnARestrictedRecord_SharesOnly_WithTheGrantCoresReason()
    {
        _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        var (_, user) = Colleague();

        var result = await ShareUser(user);

        var body = result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>().Subject.Value!;
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user)).Should().Be(CollaborateMask);
        _h.Grants.Creates.Should().BeEmpty();
        body.WorkforceApp.Should().Be(WorkforceAppAccess.NotGiven(ExternalGrantLifecycle.RecordRestrictedReasonCode));
    }

    // ── Task 193 G2: the paired grant follows the share (revoke), and Current Access shows the pairing ────────────────

    private static SpeContainerMembershipService Spe() => new(
        TestInfrastructure.TestSpeOwnership.AllowAll(Moq.Mock.Of<Sprk.Bff.Api.Infrastructure.Graph.IGraphClientFactory>()),
        NullLogger<SpeContainerMembershipService>.Instance);

    /// <summary><c>/unshare-user</c> through the production handler; <paramref name="pairing"/> false drives it without the SPE
    /// services, as the share-only auth tests do.</summary>
    private Task<IResult> Unshare(Guid user, bool pairing = true)
    {
        _h.Grants.RootOwners.TryAdd(_matter, Guid.NewGuid()); // #1620: the owner-aware revoke reads the record's owner
        return InternalShareEndpoints.UnshareAsync(
            new UnshareRecordWithUserRequest("matter", _matter, user),
            _h.Shares, _h.Grants, _h.Participations, _h.Cache.Mock.Object, _h.Materializer, _h.Children,
            TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(), new Spaarke.Scheduling.ProcessLocalScheduledJobLease(),
            InternalUserShareCoreTests.Rig.Caller(), NullLogger<Program>.Instance, CancellationToken.None,
            _h.WorkforceIdentity,
            pairing ? _h.PairedGrantRevokeFor(MatterTable, _matter) : null);
    }

    private Task<IResult> RevokeGrant(Guid accessRecordId) =>
        RevokeExternalAccessEndpoint.RevokeAccessAsync(
            new RevokeAccessRequest(accessRecordId, Guid.Empty, Guid.Empty),
            _h.Grants, Spe(), _h.Participations, _h.Materializer,
            TestRecordContainerResolver.ForNonSecureRecord(MatterTable, _matter),
            InternalUserShareCoreTests.Rig.Caller(), NullLogger<Program>.Instance, CancellationToken.None);

    /// <summary>A colleague added through their contact: the share AND the paired grant are in place.</summary>
    private async Task<(Guid Contact, Guid User, Guid Grant)> AddedColleague()
    {
        var (contact, user) = Colleague();
        (await Grant(contact)).Should().BeOfType<Ok<GrantAccessResponse>>();
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Single();
        _h.Grants.PairedUsers[grant.Id].Should().Be(user);
        return (contact, user, grant.Id);
    }

    private int? ShareMask(Guid user) => _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user));

    // ── Task 193 G3a: "Add person" and the Assigned-To rule, in either order ──────────────────────────────────────────

    private const string Attorney1 = "sprk_assignedattorney1";

    private async Task Assign(Guid? contact)
    {
        _h.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, contact);
        (await _h.SyncAsync(ExternalGrantRootType.Matter, _matter)).Complete.Should().BeTrue();
    }

    private AssignedAccessLedgerRow AssignedRow(Guid contact) =>
        _h.Store.RowsOf(_matter, contact).Single(r => r.SourceField == Attorney1);

    /// <summary>Added first: the rule finds the person's share covering its target, so the clear has nothing of its own to remove.</summary>
    [Fact]
    public async Task G3a_AddedFirst_ThenAssigned_ThenUnassigned_TheShareAndItsPairedGrantStay()
    {
        _h.HostResolvingPairedGrantRevoke(MatterTable, _matter);
        var (contact, user, grant) = await AddedColleague();

        await Assign(contact);
        AssignedRow(contact).State.Should().Be(AssignedAccessState.CoveredByExisting);
        await Assign(null);

        ShareMask(user).Should().Be(CollaborateMask, "the explicitly added access is not the rule's to remove");
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle(r => r.Id == grant);
    }

    /// <summary>Assigned first: "Add person" at the same level writes no share, but the share becomes the operator's (Adopted).</summary>
    [Fact]
    public async Task G3a_AssignedFirst_ThenAddedAtTheSameLevel_ThenUnassigned_TheShareAndItsPairedGrantStay()
    {
        _h.HostResolvingPairedGrantRevoke(MatterTable, _matter);
        var (contact, user) = Colleague();
        await Assign(contact);
        AssignedRow(contact).State.Should().Be(AssignedAccessState.Shared);
        ShareMask(user).Should().Be(CollaborateMask);

        (await Grant(contact)).Should().BeOfType<Ok<GrantAccessResponse>>();
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Single();
        _h.Grants.PairedUsers[grant.Id].Should().Be(user);
        AssignedRow(contact).State.Should().Be(AssignedAccessState.Adopted, "the deliberate share is now the operator's");

        await Assign(null);

        ShareMask(user).Should().Be(CollaborateMask, "an Adopted share is kept when the assignment ends");
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle(r => r.Id == grant.Id);
    }

    /// <summary>
    /// Removed through /unshare-user while still assigned: the share and its paired grant go, the row is Declined, and the
    /// rule does not put the share back while the assignment persists (the materializer's contract for an operator removal).
    /// </summary>
    [Fact]
    public async Task G3a_AssignedAndAdded_ThenUnsharedWhileAssigned_TheRemovalStands_AndTheRuleDoesNotReshare()
    {
        _h.HostResolvingPairedGrantRevoke(MatterTable, _matter);
        var (contact, user) = Colleague();
        await Assign(contact);
        (await Grant(contact)).Should().BeOfType<Ok<GrantAccessResponse>>();
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Single();

        (await Unshare(user)).Should().BeOfType<Ok<UnshareRecordWithUserResponse>>();
        AssignedRow(contact).State.Should().Be(AssignedAccessState.Declined);

        await Assign(contact); // the assignment persists; the rule runs again

        ShareMask(user).Should().BeNull("the operator's removal stands while the assignment persists");
        _h.Grants.ActiveRowsOf(_matter, contact).Should().NotContain(r => r.Id == grant.Id, "the paired grant followed the share");
        AssignedRow(contact).State.Should().Be(AssignedAccessState.Declined);
    }


    [Fact]
    public async Task G2_Unshare_RemovesTheShare_ThenEndsTheGrantPairedWithIt()
    {
        var (contact, user, _) = await AddedColleague();

        var result = await Unshare(user);

        result.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>().Subject.Value
            .Should().Be(new UnshareRecordWithUserResponse(user, Removed: true, PairedGrantEnded: true, PairedGrant: "ended"));
        ShareMask(user).GetValueOrDefault().Should().Be(0);
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty("the paired grant follows the share");
    }

    [Fact]
    public async Task G2_Unshare_WhenThePairedGrantCannotBeEnded_KeepsTheShareRemoved_AndSaysSo()
    {
        var (contact, user, grant) = await AddedColleague();
        _h.Grants.FailDeactivations = true;

        var result = await Unshare(user);

        result.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>().Subject.Value
            .Should().Be(new UnshareRecordWithUserResponse(user, Removed: true, PairedGrantEnded: false, PairedGrant: "endFailed"),
                "the grant is still active and listed");
        ShareMask(user).GetValueOrDefault().Should().Be(0, "the share stays removed");
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Which.Id.Should().Be(grant,
            "left active; the next pass, a repeated removal or R5 (writes on) ends it");
    }

    [Fact]
    public async Task G2_Unshare_WhenTheShareRemovalFails_ChangesNothing_TheGrantStays()
    {
        var (contact, user, grant) = await AddedColleague();
        var updatesBefore = _h.Grants.Updates.Count;
        _h.Shares.FailWritesOnRecord = ("sprk_matters", _matter);

        var result = await Unshare(user);

        result.Should().NotBeOfType<Ok<UnshareRecordWithUserResponse>>();
        ShareMask(user).Should().Be(CollaborateMask);
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Which.Id.Should().Be(grant);
        _h.Grants.Updates.Should().HaveCount(updatesBefore, "no grant row was touched");
    }

    [Fact]
    public async Task G2_Unshare_WhenThePairingColumnIsNotProvisioned_RemovesTheShareOnly()
    {
        var (contact, user, _) = await AddedColleague();
        _h.Grants.PairingColumnMissing = true;

        var result = await Unshare(user);

        result.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>().Subject.Value
            .Should().Be(new UnshareRecordWithUserResponse(user, Removed: true, PairedGrant: "none"),
                "nothing is paired where the column is missing");
        ShareMask(user).GetValueOrDefault().Should().Be(0);
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
    }

    [Fact]
    public async Task G2_Unshare_WhenThePairingCannotBeRead_KeepsTheShareRemoved_AndSaysTheGrantWasNotEnded()
    {
        var (contact, user, _) = await AddedColleague();
        _h.Grants.FailPairingReads = true;

        var result = await Unshare(user);

        var body = result.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>().Subject.Value!;
        body.PairedGrantEnded.Should().BeFalse();
        body.PairedGrant.Should().Be("unconfirmed", "a read that failed is not a grant that failed to end (task 193 G4a, B-F1)");
        ShareMask(user).GetValueOrDefault().Should().Be(0);
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
    }

    /// <summary>Found by the pairing column and the record only: a deliberate grant, and a grant paired with someone else, stay.</summary>
    [Fact]
    public async Task G2_Unshare_EndsOnlyTheGrantPairedWithThatUser()
    {
        var (_, user, _) = await AddedColleague();
        var deliberate = _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, _h.Contact(), null,
            (int)ExternalAccessLevel.Collaborate, Today.AddDays(30));
        var someoneElses = _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, _h.Contact(), null,
            (int)ExternalAccessLevel.Collaborate, Today.AddDays(30));
        _h.Grants.PairedUsers[someoneElses.Id] = Guid.NewGuid();

        await Unshare(user);

        _h.Grants.ActiveRowsOf(_matter, deliberate.ContactId!.Value).Should().ContainSingle();
        _h.Grants.ActiveRowsOf(_matter, someoneElses.ContactId!.Value).Should().ContainSingle();
    }

    /// <summary>A repeat after a grant that was not ended (or a share removed elsewhere) completes it: no share, still ended.</summary>
    [Fact]
    public async Task G2_Unshare_WhenTheUserHoldsNoShare_StillEndsTheirPairedGrant()
    {
        var (contact, user, _) = await AddedColleague();
        _h.Grants.FailDeactivations = true;
        await Unshare(user);
        _h.Grants.FailDeactivations = false;

        var again = await Unshare(user);

        again.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>().Subject.Value
            .Should().Be(new UnshareRecordWithUserResponse(user, Removed: false, PairedGrantEnded: true, PairedGrant: "ended"));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
    }

    [Fact]
    public async Task G2_Unshare_OfAShareWithNoPairedGrant_AnswersAsBefore()
    {
        var (_, user) = Colleague();
        _h.Shares.Seed(MatterTable, _matter, DataversePrincipalRef.User(user), CollaborateMask); // a share with no paired grant

        var result = await Unshare(user);

        result.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>().Subject.Value
            .Should().Be(new UnshareRecordWithUserResponse(user, Removed: true, PairedGrant: "none"));
    }

    /// <summary>children_incomplete is sent only after the record's own share is removed, so the paired grant is ended there too.</summary>
    [Fact]
    public async Task G2_Unshare_WhenRelatedRecordsAreIncomplete_StillEndsThePairedGrant_AndSaysSo()
    {
        var (contact, user, _) = await AddedColleague();
        RelatedRecordsUnreadable();

        var result = await Unshare(user);

        ReasonCode(result).Should().Be(InternalShareEndpoints.ChildrenIncompleteReasonCode);
        Extensions(result)["pairedGrantEnded"].Should().Be(true);
        Extensions(result)["pairedGrant"].Should().Be("ended");
        ShareMask(user).GetValueOrDefault().Should().Be(0);
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
    }

    /// <summary>The owner's cleanup ruling: grant gone, share stays. Revoking only the paired grant never removes the share.</summary>
    [Fact]
    public async Task G2_RevokingOnlyThePairedGrant_LeavesTheShare()
    {
        var (contact, user, grant) = await AddedColleague();

        var result = await RevokeGrant(grant);

        result.Should().BeOfType<Ok<RevokeAccessResponse>>();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
        ShareMask(user).Should().Be(CollaborateMask, "the share stands on its own");
        _h.Shares.WriteLog.Should().NotContain(w => w.Action.StartsWith("Revoke", StringComparison.Ordinal));
    }

    // ── Task 193 G4a: the paired grant's end is told apart, retried, and never lost to an aborted request ─────────────

    /// <summary><c>/unshare-user</c> with a given grant revoke and token.</summary>
    private Task<IResult> UnshareWith(
        Guid user, Sprk.Bff.Api.Api.ExternalAccess.ExternalGrantRevocationService pairedGrants, CancellationToken ct = default)
    {
        _h.Grants.RootOwners.TryAdd(_matter, Guid.NewGuid());
        return InternalShareEndpoints.UnshareAsync(
            new UnshareRecordWithUserRequest("matter", _matter, user),
            _h.Shares, _h.Grants, _h.Participations, _h.Cache.Mock.Object, _h.Materializer, _h.Children,
            TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(), new Spaarke.Scheduling.ProcessLocalScheduledJobLease(),
            InternalUserShareCoreTests.Rig.Caller(), NullLogger<Program>.Instance, ct, _h.WorkforceIdentity, pairedGrants);
    }

    /// <summary>
    /// A-F3 + B-F1: the paired grant's rows end (it is no longer listed) but its file access cannot be confirmed removed. The
    /// answer says exactly that, not "still listed"; the pairing is kept, so repeating the removal retries the file step and
    /// then reports it ended; a third removal finds nothing paired.
    /// </summary>
    [Fact]
    public async Task G4a_Unshare_WhenThePairedGrantsFileAccessCannotBeConfirmed_SaysSo_AndARepeatRetriesTheFileStep()
    {
        var (contact, user, grant) = await AddedColleague();
        var fileStepUndecidable = _h.PairedGrantRevokeFor(MatterTable, _matter,
            containers: TestRecordContainerResolver.Throwing(new HttpRequestException("Simulated container read failure.")));

        var first = await UnshareWith(user, fileStepUndecidable);

        first.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>().Subject.Value.Should().Be(
            new UnshareRecordWithUserResponse(user, Removed: true, PairedGrantEnded: false, PairedGrant: "fileAccessUnconfirmed"));
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty("its rows are ended, so it is no longer listed");
        _h.Grants.PairedUsers.Should().ContainKey(grant, "the pairing is kept so a repeat retries the file step");

        var again = await Unshare(user);

        again.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>().Subject.Value.Should().Be(
            new UnshareRecordWithUserResponse(user, Removed: false, PairedGrantEnded: true, PairedGrant: "ended"),
            "the repeat ran the file step again and it completed");
        _h.Grants.PairedUsers.Should().NotContainKey(grant, "a confirmed end clears the pairing");

        (await Unshare(user)).Should().BeOfType<Ok<UnshareRecordWithUserResponse>>().Subject.Value!.PairedGrant
            .Should().Be("none");
    }

    /// <summary>
    /// A-F4: the request is aborted after its share is confirmed gone; the paired grant is still ended (the step runs to its
    /// end whatever happens to the request, as the adoption marker does).
    /// </summary>
    [Fact]
    public async Task G4a_Unshare_AbortedOnceTheShareIsGone_StillEndsThePairedGrant()
    {
        var (contact, user, _) = await AddedColleague();
        using var abort = new CancellationTokenSource();
        _h.Grants.BeforeGrantQuery = _ =>
        {
            if (ShareMask(user).GetValueOrDefault() == 0)
                abort.Cancel(); // the client goes away once the share is removed
        };

        var result = await UnshareWith(user, _h.PairedGrantRevokeFor(MatterTable, _matter), abort.Token);

        abort.IsCancellationRequested.Should().BeTrue("the request was aborted after its share was removed");
        result.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>().Subject.Value!.PairedGrant.Should().Be("ended");
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty("the grant does not outlive its share");
    }

    // ── Current Access: pairedWith ──────────────────────────────────────────────────────────────────────────────────

    private async Task<RecordUserSharesResponse> UserShares() =>
        (await InternalShareEndpoints.ListAsync(new RecordUserSharesQuery("matter", _matter), _h.Shares, _h.Grants,
            _h.Participations, _h.Store, InternalUserShareCoreTests.Rig.Caller(), NullLogger<Program>.Instance,
            CancellationToken.None))
        .Should().BeOfType<Ok<RecordUserSharesResponse>>().Subject.Value!;

    private async Task<RecordGrantsResponse> RecordGrants() =>
        (await RecordGrantsEndpoint.Handle(new RecordGrantsQuery("matter", _matter), _h.Grants, _h.Time,
            NullLogger<Program>.Instance, InternalUserShareCoreTests.Rig.Caller(), CancellationToken.None))
        .Should().BeOfType<Ok<RecordGrantsResponse>>().Subject.Value!;

    [Fact]
    public async Task G2_CurrentAccess_TheShareAndItsPairedGrant_EachNameTheOther_ByIdAndKindOnly()
    {
        var (_, user, grant) = await AddedColleague();

        var share = (await UserShares()).Shares.Single(r => r.SystemUserId == user);
        share.PairedWith.Should().Be(new RecordAccessPairing(grant, RecordAccessPairing.GrantKind));

        var grantRow = (await RecordGrants()).Grants.Single(r => r.AccessRecordId == grant);
        grantRow.PairedWith.Should().Be(new RecordAccessPairing(user, RecordAccessPairing.ShareKind));

        var json = System.Text.Json.JsonSerializer.Serialize(grantRow);
        json.Should().NotContain(Email, "the grant row never carries the user's email");
        json.Should().NotContain("Ada Lovelace", "nor the user's name");
    }

    [Fact]
    public async Task G2_CurrentAccess_ADeliberateGrantAndAPlainShare_CarryNoPairing()
    {
        var plain = _h.Grants.Seed(ExternalGrantRootType.Matter, _matter, _h.Contact(), null,
            (int)ExternalAccessLevel.Collaborate, Today.AddDays(30));
        var (_, user) = Colleague();
        _h.Shares.Seed(MatterTable, _matter, DataversePrincipalRef.User(user), CollaborateMask); // a share with no paired grant

        (await UserShares()).Shares.Single(r => r.SystemUserId == user).PairedWith.Should().BeNull();
        (await RecordGrants()).Grants.Single(r => r.AccessRecordId == plain.Id).PairedWith.Should().BeNull();
    }

    /// <summary>Display only: a pairing that cannot be read pairs nothing and never fails either list (fails quiet).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task G2_CurrentAccess_WhenThePairingCannotBeRead_ListsEverythingUnpaired(bool columnMissing)
    {
        var (_, user, grant) = await AddedColleague();
        if (columnMissing)
            _h.Grants.PairingColumnMissing = true;
        else
            _h.Grants.FailPairingReads = true;

        (await UserShares()).Shares.Single(r => r.SystemUserId == user).PairedWith.Should().BeNull();
        (await RecordGrants()).Grants.Single(r => r.AccessRecordId == grant).PairedWith.Should().BeNull();
    }
}
