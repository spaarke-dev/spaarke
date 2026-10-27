using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Slice C3: an administrator removes a person's external access (<c>/remove-person</c>) and reinstates it
/// (<c>/reinstate-person</c>). Through the PRODUCTION handlers, the shared revoke, the grant core, the materializer and
/// the binder, over the task-142 harness. The grant core's own refusal on every caller is pinned by
/// <see cref="GrantCoreRemovedPersonTests"/>. The rule: the removed marker alone refuses the person, whatever the
/// contact's statecode; removal writes it first and changes neither the contact's state nor its binding; reinstatement clears
/// the marker only. KEEP path: <c>tests/integration/auth/**</c>.
/// </summary>
public class PersonAccessAdministrationTests
{
    private const ExternalGrantRootType Matter = ExternalGrantRootType.Matter;
    private const string Attorney1 = "sprk_assignedattorney1";
    private const string Email = "person@customer.example";
    private const string CallerOid = "0a0a0a0a-1111-2222-3333-4444444444c3";
    private static readonly Guid AdminUser = Guid.Parse("a0a0a0a0-0000-4000-8000-0000000000c3");

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();
    private readonly Mock<IMembershipCacheInvalidator> _invalidator = new();
    private readonly BindingLogCapture<Program> _log = new();

    public PersonAccessAdministrationTests()
    {
        _h.Store.Root(Matter, _matter);
        _h.Grants.RootOwners[_matter] = Guid.NewGuid();
    }

    // ── Wiring ──────────────────────────────────────────────────────────────────────────────────────────────────

    private static HttpContext AdminContext(AdministratorAuthorityLeg leg = AdministratorAuthorityLeg.Direct)
    {
        var http = new DefaultHttpContext { TraceIdentifier = "trace-c3" };
        ExternalAccessAdministratorAuthorizationFilter.SetDecision(http,
            new AdministratorAuthorityDecision(AdminUser, leg, AdministratorAuthorityReason.Granted));
        return http;
    }

    private static HttpContext GranterContext() => new DefaultHttpContext
    {
        TraceIdentifier = "trace-c3-grant",
        User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("oid", CallerOid) }, "Test")),
    };

    private Task<IResult> Remove(Guid contactId, HttpContext? http = null, RecordContainerResolver? resolver = null) =>
        PersonAccessAdministrationEndpoints.RemovePersonAsync(
            new ExternalPersonRequest(contactId),
            _h.Grants,
            new SpeContainerMembershipService(TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()), NullLogger<SpeContainerMembershipService>.Instance),
            _h.Participations,
            _h.Materializer,
            resolver ?? TestRecordContainerResolver.ForNonSecureRecord("sprk_matter", _matter),
            _h.Identities,
            _invalidator.Object,
            _h.Time,
            http ?? AdminContext(),
            _log,
            CancellationToken.None);

    private Task<IResult> Reinstate(Guid contactId, HttpContext? http = null) =>
        PersonAccessAdministrationEndpoints.ReinstatePersonAsync(
            new ExternalPersonRequest(contactId), _h.Grants, _h.Participations, _h.Identities, _invalidator.Object, _h.Time,
            http ?? AdminContext(), _log, CancellationToken.None);

    private Task<IResult> Grant(Guid contactId) =>
        GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(contactId, Guid.Empty, ExternalAccessLevel.Collaborate, null, null, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new RecordOwnershipResolverDouble(), new TeamPrivilegeCheckerDouble(),
            new EverythingProbe(), _h.Materializer, _h.Notifier, _h.Invites, GranterContext(), NullLogger<Program>.Instance, _h.Time,
            CancellationToken.None);


    private Task<AssignedAccessOutcome> Sync() => _h.SyncAsync(Matter, _matter);

    /// <summary>A person signed in through CIAM (bound oid, External plane), with an outstanding invite stamp and baselines.</summary>
    private (Guid ContactId, string Oid) BoundPerson(int stateCode = 0)
    {
        var id = Guid.NewGuid();
        var oid = Guid.NewGuid().ToString("D");
        var c = _h.Identities.AddContact(id, email: Email, oid: oid, plane: IdentityPlaneMarker.External, stateCode: stateCode,
            inviteCodeHash: "code-hash");
        _h.Identities.StampInvite(id, Email, IdentityBindingTestKit.CustomerTenant);
        c.AccessPermissionGrant = (int)ExternalAccessLevel.ViewOnly;
        c.StandingGrant = (int)ExternalAccessLevel.ViewOnly;
        return (id, oid);
    }

    private ExternalGrantRow GrantOnMatter(Guid contactId)
        => _h.Grants.Seed(Matter, _matter, contactId, null, (int)ExternalAccessLevel.Collaborate, null);

    private static ProblemHttpResult Problem(IResult result) => result.Should().BeOfType<ProblemHttpResult>().Subject;

    private static string? ReasonCode(IResult result)
        => Problem(result).ProblemDetails.Extensions.TryGetValue("reasonCode", out var code) ? code as string : null;

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // /remove-person
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Remove_MarksThePerson_RevokesEveryGrant_EvictsCaches_ClearsTheInviteState_AndTheStandingGrant()
    {
        var (contact, oid) = BoundPerson();
        var linkedUser = Guid.NewGuid();
        _h.Identities.AddSystemUser(linkedUser, Guid.NewGuid(), "colleague@firm.example", primaryContactId: contact);
        var grant = GrantOnMatter(contact);

        var result = await Remove(contact);

        var ok = result.Should().BeOfType<Ok<RemovePersonResponse>>().Subject.Value!;
        ok.Steps.Should().OnlyContain(s => s.Completed);
        ok.Steps.Select(s => s.Step).Should().Equal(
            ExternalPersonRemovalService.MarkerStep, ExternalPersonRemovalService.GrantsStep, ExternalPersonRemovalService.CachesStep,
            ExternalPersonRemovalService.InviteStateStep, ExternalPersonRemovalService.StandingGrantStep);
        ok.GrantsRevoked.Should().Be(1);
        ok.GrantsNotCompleted.Should().BeEmpty();

        var row = _h.Identities.Contacts[contact];
        row.RemovedOn.Should().Be(_h.Time.Now, "the marker is stamped with the removal time");
        _h.Grants.Rows.Single(r => r.Id == grant.Id).IsActive.Should().BeFalse();
        _h.Participations.Invalidations.Should().Contain(i => i.Contacts.Contains(contact));
        _invalidator.Verify(i => i.InvalidateUserAccessAsync(linkedUser, "trace-c3", It.IsAny<CancellationToken>()), Times.Once);
        row.InvitedOn.Should().BeNull();
        row.InviteCodeHash.Should().BeNull();
        row.InvitedEmailHash.Should().BeNull();
        row.InvitedTenantId.Should().BeNull();
        row.AccessPermissionGrant.Should().BeNull();
        row.StandingGrant.Should().BeNull();

        // Neither the state nor the binding changes.
        row.StateCode.Should().Be(0);
        row.Oid.Should().Be(oid);
        row.Plane.Should().Be((int)IdentityPlaneMarker.External);
        row.KeyMirror.Should().Be(oid);
    }

    [Fact]
    public async Task Remove_WritesTheMarker_BeforeAnyGrantIsRevoked_AndClearsTheInviteState_After()
    {
        var (contact, _) = BoundPerson();
        GrantOnMatter(contact);
        bool? markedAtFirstGrantRead = null;
        bool? inviteStillSetAtFirstGrantRead = null;
        _h.Grants.BeforeGrantQuery = _ =>
        {
            markedAtFirstGrantRead ??= _h.Identities.Contacts[contact].RemovedOn is not null;
            inviteStillSetAtFirstGrantRead ??= _h.Identities.Contacts[contact].InviteCodeHash is not null;
        };

        await Remove(contact);

        markedAtFirstGrantRead.Should().BeTrue("the marker is written FIRST, so the person is refused before any revoke");
        inviteStillSetAtFirstGrantRead.Should().BeTrue("the invite state is cleared after the grants are revoked");
        _h.Identities.Writes.Select(w => w.Op).Should().ContainInOrder(
            "removed-marker", "removed-person-access-state-clear", "standing-grant-clear");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RemoveThenReinstate_LeavesTheStatecodeUnchanged(int stateCode)
    {
        var (contact, _) = BoundPerson(stateCode);

        await Remove(contact);
        _h.Identities.Contacts[contact].StateCode.Should().Be(stateCode, "removal does not change the contact's state");
        await Reinstate(contact);

        _h.Identities.Contacts[contact].StateCode.Should().Be(stateCode, "nor does reinstatement");
    }

    [Fact]
    public async Task Remove_IsIdempotent_ASecondCallKeepsTheMarker_AndRevokesNothingNew()
    {
        var (contact, _) = BoundPerson();
        GrantOnMatter(contact);
        await Remove(contact);
        var firstMarker = _h.Identities.Contacts[contact].RemovedOn;
        var grantWrites = _h.Grants.Updates.Count;
        _h.Time.Now = _h.Time.Now.AddHours(1);

        var second = await Remove(contact);

        var ok = second.Should().BeOfType<Ok<RemovePersonResponse>>().Subject.Value!;
        ok.Steps.Should().OnlyContain(s => s.Completed);
        ok.GrantsRevoked.Should().Be(0);
        _h.Identities.Contacts[contact].RemovedOn.Should().Be(firstMarker, "an existing marker is kept, not re-stamped");
        _h.Grants.Updates.Count.Should().Be(grantWrites, "no grant is left to revoke");
    }

    [Fact]
    public async Task Remove_AMarkerWriteThatFails_ChangesNothingElse()
    {
        var (contact, _) = BoundPerson();
        var grant = GrantOnMatter(contact);
        _h.Identities.RemovedMarkerWriteFailure = StoreWriteStatus.Failed;

        var result = await Remove(contact);

        Problem(result).StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        ReasonCode(result).Should().Be(PersonAccessAdministrationEndpoints.RemoveIncompleteReasonCode);
        _h.Grants.Rows.Single(r => r.Id == grant.Id).IsActive.Should().BeTrue();
        _h.Identities.Contacts[contact].InviteCodeHash.Should().NotBeNull();
        _h.Identities.Contacts[contact].StandingGrant.Should().NotBeNull();
    }

    [Fact]
    public async Task Remove_ARefusedStandingGrantWrite_IsReportedNotCompleted_NeverAsDone_AndThePersonStaysRemoved()
    {
        var (contact, _) = BoundPerson();
        _h.Identities.StandingGrantClearFailure = StoreWriteStatus.Forbidden;

        var result = await Remove(contact);

        Problem(result).StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var steps = (IReadOnlyList<ExternalPersonStepResult>)Problem(result).ProblemDetails.Extensions["steps"]!;
        steps.Single(s => s.Step == ExternalPersonRemovalService.StandingGrantStep).Completed.Should().BeFalse();
        steps.Single(s => s.Step == ExternalPersonRemovalService.StandingGrantStep).Detail.Should().Contain("Standing Grant Administrators");
        _h.Identities.Contacts[contact].RemovedOn.Should().NotBeNull();
        _h.Identities.Contacts[contact].StandingGrant.Should().NotBeNull();
    }

    [Fact]
    public async Task Remove_AGrantWhoseRevokeDidNotComplete_IsListed_AndTheRestStillRuns()
    {
        var (contact, _) = BoundPerson();
        var grant = GrantOnMatter(contact);

        var result = await Remove(contact, resolver: TestRecordContainerResolver.Throwing(new HttpRequestException("container read down")));

        Problem(result).StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        ((IReadOnlyList<Guid>)Problem(result).ProblemDetails.Extensions["grantsNotCompleted"]!).Should().Equal(grant.Id);
        _h.Identities.Contacts[contact].RemovedOn.Should().NotBeNull();
        _h.Identities.Contacts[contact].InviteCodeHash.Should().BeNull("the later steps still run");
    }

    // ── Slice C2b: the grant read is bounded; a read above the bound is never reported complete ──────────────────

    private void GrantRowsOnMatter(Guid contactId, int count)
    {
        for (var i = 0; i < count; i++)
            GrantOnMatter(contactId);
    }

    [Fact]
    public async Task Remove_MoreGrantRowsThanOneReadHolds_RevokesTheRowsRead_ReportsTheGrantsStepNotCompleted_AndARetryCompletes()
    {
        var (contact, _) = BoundPerson();
        GrantRowsOnMatter(contact, ExternalPersonRemovalService.MaxGrantsPerRemoval + 1);

        var first = await Remove(contact);

        Problem(first).StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        ReasonCode(first).Should().Be(PersonAccessAdministrationEndpoints.RemoveIncompleteReasonCode);
        var steps = (IReadOnlyList<ExternalPersonStepResult>)Problem(first).ProblemDetails.Extensions["steps"]!;
        var grants = steps.Single(s => s.Step == ExternalPersonRemovalService.GrantsStep);
        grants.Completed.Should().BeFalse("more grants than one read holds may remain");
        grants.Detail.Should().Contain("more may remain");
        _h.Grants.Rows.Where(r => r.ContactId == contact).Should().OnlyContain(r => !r.IsActive, "the rows read are revoked");

        var retry = await Remove(contact);

        retry.Should().BeOfType<Ok<RemovePersonResponse>>().Subject.Value!.Steps.Should().OnlyContain(s => s.Completed);
    }

    [Fact]
    public async Task Remove_AsManyGrantRowsAsTheBound_IsComplete()
    {
        var (contact, _) = BoundPerson();
        GrantRowsOnMatter(contact, ExternalPersonRemovalService.MaxGrantsPerRemoval);

        var result = await Remove(contact);

        var ok = result.Should().BeOfType<Ok<RemovePersonResponse>>().Subject.Value!;
        ok.Steps.Single(s => s.Step == ExternalPersonRemovalService.GrantsStep).Completed.Should().BeTrue();
        _h.Grants.Rows.Where(r => r.ContactId == contact).Should().OnlyContain(r => !r.IsActive);
    }

    [Fact]
    public async Task Remove_ReadsThePersonsGrants_WithOneMoreThanTheBound()
    {
        var (contact, _) = BoundPerson();
        GrantOnMatter(contact);

        await Remove(contact);

        _h.Grants.GrantQueryTops.Should().Contain(ExternalPersonRemovalService.MaxGrantsPerRemoval + 1,
            "an unbounded read takes one page and could leave grants unread without saying so");
    }

    [Fact]
    public async Task Remove_AnUnknownContact_Is404_AnUnreadableOne_Is503_AndNothingIsWritten()
    {
        Problem(await Remove(Guid.NewGuid())).StatusCode.Should().Be(StatusCodes.Status404NotFound);

        var (contact, _) = BoundPerson();
        _h.Identities.FailGetContact = true;
        Problem(await Remove(contact)).StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _h.Identities.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("no-decision")]
    [InlineData("refused-decision")]
    public async Task Remove_AndReinstate_WithoutTheAdministratorDecision_Are403_AndWriteNothing(string why)
    {
        var (contact, _) = BoundPerson();
        _h.Identities.Contacts[contact].RemovedOn = _h.Time.Now;
        HttpContext Http()
        {
            var http = new DefaultHttpContext();
            if (why == "refused-decision")
                ExternalAccessAdministratorAuthorizationFilter.SetDecision(http,
                    new AdministratorAuthorityDecision(AdminUser, AdministratorAuthorityLeg.None, AdministratorAuthorityReason.NotAMember));
            return http;
        }

        var remove = await Remove(contact, Http());
        var reinstate = await Reinstate(contact, Http());

        Problem(remove).StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        Problem(reinstate).StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        ReasonCode(remove).Should().Be(ExternalAccessAdministratorAuthorizationFilter.AdministratorRequiredReasonCode);
        _h.Identities.Writes.Should().BeEmpty();
        _h.Identities.Contacts[contact].RemovedOn.Should().NotBeNull("a refused reinstatement clears nothing");
    }

    [Theory]
    [InlineData(AdministratorAuthorityLeg.Direct)]
    [InlineData(AdministratorAuthorityLeg.Team)]
    public async Task Remove_AndReinstate_AreAudited_WithTheAdministrator_TheLeg_TheContact_AndNoEmail(AdministratorAuthorityLeg leg)
    {
        var (contact, _) = BoundPerson();

        await Remove(contact, AdminContext(leg));
        await Reinstate(contact, AdminContext(leg));

        var audit = _log.Messages.Where(m => m.StartsWith("[EXT-PERSON-AUDIT]", StringComparison.Ordinal)).ToList();
        audit.Should().HaveCount(2);
        audit[0].Should().StartWith("[EXT-PERSON-AUDIT] remove:");
        audit[1].Should().StartWith("[EXT-PERSON-AUDIT] reinstate:");
        audit.Should().OnlyContain(m => m.Contains(AdminUser.ToString(), StringComparison.Ordinal)
                                        && m.Contains($"({leg})", StringComparison.Ordinal)
                                        && m.Contains(contact.ToString(), StringComparison.Ordinal)
                                        && m.Contains("trace-c3", StringComparison.Ordinal)
                                        && m.Contains("statecode at start 0", StringComparison.Ordinal)
                                        && !m.Contains(Email, StringComparison.OrdinalIgnoreCase));
    }

    // ── After removal: refused everywhere, by the marker ────────────────────────────────────────────────────

    /// <summary>
    /// The person signs in again with the same object id and NO email claim: the bound contact is found by its oid and
    /// refused, and no contact is created. Removal keeps the binding for exactly this; clearing the oid would lose it.
    /// </summary>
    [Fact]
    public async Task AfterRemoval_ASameOidSignInWithNoEmail_IsDeniedContactRemoved_AndCreatesNoContact()
    {
        var (contact, oid) = BoundPerson();
        await Remove(contact);
        var contacts = _h.Identities.Contacts.Count;

        var result = await IdentityBindingTestKit.Binder(_h.Identities)
            .ResolveCiamCallerAsync(oid, email: null, inviteCode: null, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactRemoved);
        result.ContactId.Should().BeNull();
        _h.Identities.Contacts.Count.Should().Be(contacts, "no contact is created around a removal");
    }

    [Fact]
    public async Task AfterRemoval_ThePersonHoldsNoGrant_IsRefusedOnSignIn_Invite_AndGrant_AndNothingIsCreated()
    {
        var (contact, oid) = BoundPerson();
        GrantOnMatter(contact);
        await Remove(contact);
        var grantCreates = _h.Grants.Creates.Count;
        var contacts = _h.Identities.Contacts.Count;

        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
        (await _h.Identities.ReadRemovedMarkerAsync(contact, CancellationToken.None)).Should().Be(RemovedMarkerRead.Removed);
        (await IdentityBindingTestKit.Binder(_h.Identities).ResolveCiamCallerAsync(oid, Email, null, CancellationToken.None))
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactRemoved);
        (await IdentityBindingTestKit.Binder(_h.Identities).ResolveInviteContactAsync(Email, CancellationToken.None))
            .ReasonCode.Should().Be(ContactBindingDecision.InviteContactRemovedAdminReinstate);
        ReasonCode(await Grant(contact)).Should().Be(ContactBindingDecision.InviteContactRemovedAdminReinstate);

        _h.Grants.Creates.Count.Should().Be(grantCreates);
        _h.Identities.Contacts.Count.Should().Be(contacts);
        _h.Ciam.Attempts.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // /reinstate-person
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Reinstate_ClearsTheMarkerOnly_RestoresNoGrant_AndSendsNoInvite()
    {
        var (contact, oid) = BoundPerson();
        GrantOnMatter(contact);
        await Remove(contact);
        var writesBefore = _h.Identities.Writes.Count;
        var grantWrites = _h.Grants.WriteCount;

        var result = await Reinstate(contact);

        result.Should().BeOfType<Ok<ReinstatePersonResponse>>().Subject.Value!.MarkerCleared.Should().BeTrue();
        _h.Identities.Contacts[contact].RemovedOn.Should().BeNull();
        _h.Identities.Writes.Skip(writesBefore).Select(w => w.Op).Should().Equal("removed-marker-clear");
        _h.Grants.WriteCount.Should().Be(grantWrites, "no grant is restored");
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
        _h.Emails.Onboarding.Should().BeEmpty();
        _h.Emails.ExistingAccount.Should().BeEmpty();
        _h.Identities.Contacts[contact].Oid.Should().Be(oid);
    }

    [Fact]
    public async Task Reinstate_AnUnmarkedContact_IsANoOp()
    {
        var (contact, _) = BoundPerson();

        var result = await Reinstate(contact);

        result.Should().BeOfType<Ok<ReinstatePersonResponse>>().Subject.Value!.MarkerCleared.Should().BeFalse();
        _h.Identities.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Reinstate_AMarkerClearThatFails_IsIncomplete_AndThePersonStaysRemoved()
    {
        var (contact, _) = BoundPerson();
        await Remove(contact);
        _h.Identities.RemovedMarkerWriteFailure = StoreWriteStatus.Failed;

        var result = await Reinstate(contact);

        ReasonCode(result).Should().Be(PersonAccessAdministrationEndpoints.ReinstateIncompleteReasonCode);
        _h.Identities.Contacts[contact].RemovedOn.Should().NotBeNull();
    }

    [Fact]
    public async Task AfterReinstatement_ASameOidSignIn_BindsTheSameContact_ToAnEmptySet()
    {
        var (contact, oid) = BoundPerson();
        GrantOnMatter(contact);
        await Remove(contact);
        await Reinstate(contact);

        var result = await IdentityBindingTestKit.Binder(_h.Identities)
            .ResolveCiamCallerAsync(oid, Email, inviteCode: null, CancellationToken.None);

        result.ContactId.Should().Be(contact);
        result.DenyCode.Should().BeNull();
        _h.Grants.Rows.Where(r => r.ContactId == contact && r.IsActive).Should().BeEmpty("reinstatement restores no grant");
    }

    [Fact]
    public async Task AfterReinstatement_AFreshInviteAndGrant_FindTheExistingContact_NoSecondContact()
    {
        var (contact, _) = BoundPerson();
        GrantOnMatter(contact);
        await Remove(contact);
        await Reinstate(contact);
        var contacts = _h.Identities.Contacts.Count;

        var invitee = await IdentityBindingTestKit.Binder(_h.Identities).ResolveInviteContactAsync(Email, CancellationToken.None);
        invitee.ContactId.Should().Be(contact);
        invitee.Action.Should().NotBe(InviteContactAction.CreateContact).And.NotBe(InviteContactAction.Refuse);

        var granted = await Grant(contact);

        granted.Should().BeOfType<Ok<GrantAccessResponse>>();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        _h.Identities.Contacts.Count.Should().Be(contacts, "the existing contact is found; none is created");
        _h.Ciam.Attempts.Should().BeEmpty("the person already has an account; nothing is keyed onto a second contact");
    }

    [Fact]
    public async Task AfterReinstatement_TheAssignedToRule_DoesNotGrantAgain()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("control: the Assigned-To rule granted the person");

        await Remove(contact);
        await Reinstate(contact);
        await Sync();

        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty("the removal's revoke recorded Declined; no later pass re-grants");
        _h.Store.RowsOf(_matter, contact).Single().State.Should().Be(AssignedAccessState.Declined);
    }

    // ── Pure: what the removal writes ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheRemovalWrites_NeverNameABindingColumn_OrTheStatecode()
    {
        var columns = DataverseContactIdentityStore.RemovedPersonAccessStatePayload().Keys
            .Concat(DataverseContactIdentityStore.StandingGrantClearPayload().Keys)
            .Concat(DataverseContactIdentityStore.RemovedMarkerPayload(DateTimeOffset.UnixEpoch).Keys)
            .ToList();

        columns.Should().NotContain(new[]
        {
            ContactBindingDecision.ExternalObjectIdColumn, ContactBindingDecision.IdentityPlaneColumn, "sprk_externalobjectidkey",
            "statecode", "statuscode",
        });
        DataverseContactIdentityStore.RemovedPersonAccessStatePayload().Values.Should().OnlyContain(v => v == null);
        DataverseContactIdentityStore.RemovedMarkerPayload(null).Values.Should().OnlyContain(v => v == null);
        DataverseContactIdentityStore.RemovedMarkerPayload(new DateTimeOffset(2026, 10, 27, 9, 30, 0, TimeSpan.FromHours(-4)))
            [ContactBindingDecision.ExternalAccessRemovedOnColumn].Should().Be("2026-10-27T13:30:00Z");
    }

    [Fact]
    public void ThePersonsGrants_AreReadAcrossEveryRoot_ActiveOnly()
        => ExternalGrantLifecycle.ActiveRowsForContactFilter(Guid.Parse("eeeeeeee-0000-4000-8000-0000000000c3"))
            .Should().Be("_sprk_contact_value eq eeeeeeee-0000-4000-8000-0000000000c3 and statecode eq 0");

    // ── Doubles ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A granter who holds every right on the record, so the grant ceiling passes.</summary>
    private sealed class EverythingProbe : CallerRecordAccessProbe
    {
        public EverythingProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => Task.FromResult(AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo
                               | AccessRights.Share | AccessRights.Delete);
    }
}
