using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 142 — the operator's own actions on Assigned-To access (criteria 6, 9, 17), through the
/// PRODUCTION handlers of <c>/revoke</c>, <c>/unshare-user</c>, <c>/grant</c> and <c>/share-user</c> over the same harness
/// the materializer runs on: removal in Manage Access records Declined and no later pass re-creates it; a manual grant or
/// share onto a subject the ledger holds (incl. "Grant" on a secure-record suggestion) records Adopted; and removing an
/// auto grant whose contact still reaches the record through a standing or organization term SAYS so (owner A2 reversed:
/// those terms stay).
/// </summary>
/// <remarks>The handlers are called directly — the filter's own gate on these routes is pinned by
/// <c>DelegationRuleCharacterizationTests</c>; here the subject is what the handler writes to the ledger. KEEP path:
/// <c>tests/integration/auth/**</c>.</remarks>
public class AssignedAccessMarkerTests
{
    private const ExternalGrantRootType Matter = ExternalGrantRootType.Matter;
    private const string Attorney1 = "sprk_assignedattorney1";
    private const string LawFirm1 = "sprk_assignedlawfirm1";

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();

    public AssignedAccessMarkerTests() => _h.Store.Root(Matter, _matter);

    private Task<AssignedAccessOutcome> Sync() => _h.SyncAsync(Matter, _matter);

    private AssignedAccessLedgerRow Row(Guid subject) => _h.Store.RowsOf(_matter, subject).Single();

    private static HttpContext Context() => new DefaultHttpContext { TraceIdentifier = "trace-142" };

    private Task<IResult> Revoke(Guid accessRecordId) =>
        RevokeExternalAccessEndpoint.RevokeAccessAsync(
            new RevokeAccessRequest(accessRecordId, Guid.Empty, Guid.Empty),
            _h.Grants,
            new SpeContainerMembershipService(Mock.Of<IGraphClientFactory>(), NullLogger<SpeContainerMembershipService>.Instance),
            _h.Participations,
            _h.Materializer,
            // Task 166: the container is derived from the grant's root. Here the matter's derived container is the shared
            // business-unit one, so the SPE step is skipped (this class tests the Assigned-To marker, not the container).
            TestRecordContainerResolver.ForNonSecureRecord("sprk_matter", _matter),
            Context(), NullLogger<Program>.Instance, CancellationToken.None);

    private Task<IResult> Grant(Guid contactId) =>
        GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(contactId, Guid.Empty, ExternalAccessLevel.Collaborate, null, null, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new WriteProbe(),
            _h.Materializer, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

    private Task<IResult> ShareUser(Guid user) =>
        InternalShareEndpoints.ShareAsync(
            new ShareRecordWithUserRequest("matter", _matter, user, ExternalAccessLevel.Collaborate),
            _h.Shares, _h.Grants, _h.Participations, _h.Cache.Mock.Object, new WriteProbe(), Children(), _h.Guard,
            Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(), _h.Materializer, Context(), NullLogger<Program>.Instance, CancellationToken.None);

    /// <summary>Batch 4 integration (task 149): the share routes fan out to a secure root's children — the REAL
    /// synchronizer over a world with no secure record, so it answers "not applicable" for this ordinary matter.</summary>
    private Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer Children() =>
        Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.SynchronizerOver(() => Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.Standard(), _h.Shares);

    private Task<IResult> UnshareUser(Guid user) =>
        InternalShareEndpoints.UnshareAsync(
            new UnshareRecordWithUserRequest("matter", _matter, user),
            _h.Shares, _h.Grants, _h.Participations, _h.Cache.Mock.Object,
            _h.Materializer, Children(), Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(), Context(), NullLogger<Program>.Instance, CancellationToken.None);

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 9 — removal through Manage Access sticks; a manual grant afterwards still succeeds
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokingAnAutoGrant_RecordsDeclined_AndAFollowingSyncMakesZeroWrites()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Single();

        var result = await Revoke(grant.Id);
        var writesAfterRevoke = _h.TotalWrites;
        await Sync();

        result.Should().BeOfType<Ok<RevokeAccessResponse>>();
        Row(contact).State.Should().Be(AssignedAccessState.Declined);
        Row(contact).Reason.Should().Be(AssignedAccessReason.RemovedByOperator);
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
        _h.TotalWrites.Should().Be(writesAfterRevoke, "the sync makes zero writes for a declined subject");
    }

    [Fact]
    public async Task AManualGrantAfterARemoval_StillSucceeds_AndIsAdopted()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        await Revoke(_h.Grants.ActiveRowsOf(_matter, contact).Single().Id);

        var result = await Grant(contact);

        result.Should().BeOfType<Ok<GrantAccessResponse>>();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        Row(contact).State.Should().Be(AssignedAccessState.Adopted);
    }

    [Fact]
    public async Task UnsharingAnAutoShare_RecordsDeclined_AndNoLaterSyncSharesAgain()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Grants.Person(user);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();

        var result = await UnshareUser(user);
        var stateAfterUnshare = (Row(contact).State, Row(contact).Reason);
        await Sync();

        result.Should().BeOfType<Ok<UnshareRecordWithUserResponse>>();
        stateAfterUnshare.Should().Be((AssignedAccessState.Declined, AssignedAccessReason.RemovedByOperator),
            "the unshare itself records the operator's removal — not the next sync's out-of-band detection");
        Row(contact).State.Should().Be(AssignedAccessState.Declined);
        Row(contact).Reason.Should().Be(AssignedAccessReason.RemovedByOperator);
        _h.Shares.MaskOf("sprk_matter", _matter, DataversePrincipalRef.User(user)).Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 6 — "Grant" on a secure-record suggestion writes through the normal path and marks Adopted
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GrantingASuggestedContact_OnASecureRecord_WritesTheGrant_AndMarksAdopted()
    {
        _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        Row(contact).State.Should().Be(AssignedAccessState.PendingConfirmation);

        var result = await Grant(contact);

        result.Should().BeOfType<Ok<GrantAccessResponse>>();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        Row(contact).State.Should().Be(AssignedAccessState.Adopted);
    }

    [Fact]
    public async Task SharingASuggestedLinkedUser_OnASecureRecord_MarksAdopted()
    {
        _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        var (contact, user) = _h.LinkedContact();
        _h.Grants.Person(user);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();

        var result = await ShareUser(user);

        result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>();
        Row(contact).State.Should().Be(AssignedAccessState.Adopted);
    }

    [Fact]
    public async Task AManualGrantOfASubjectTheLedgerDoesNotHold_CreatesNoLedgerRow()
    {
        var contact = _h.Contact();

        await Grant(contact);

        _h.Store.Ledger.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 17 (A2 reversed = keep): never "removed" while access silently remains
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokingAnAutoGrant_OfAStandingContact_NamesTheStandingTerm_BeforeAndAfter()
    {
        var contact = _h.Contact();
        _h.Standing.Contacts[contact] = ExternalAccessLevel.ViewOnly;
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();

        var before = await _h.Materializer.ListAsync(Matter, _matter, CancellationToken.None);
        var result = await Revoke(_h.Grants.ActiveRowsOf(_matter, contact).Single().Id);

        before.Single().ResidualAccessTerms.Should().Equal(AssignedAccessMaterializer.ResidualTerm.StandingGrant);
        var body = result.Should().BeOfType<Ok<RevokeAccessResponse>>().Subject.Value!;
        body.ResidualAccessTerms.Should().Equal(AssignedAccessMaterializer.ResidualTerm.StandingGrant);
    }

    [Fact]
    public async Task RevokingAnAutoGrant_OfAContactWhoseAssignedFirmHasAStandingGrant_NamesTheOrganizationTerm()
    {
        var contact = _h.Contact();
        var firm = Guid.NewGuid();
        _h.Participations.ContactOrganizations[contact] = new[] { firm };
        _h.Standing.Organizations[firm] = ExternalAccessLevel.ViewOnly;
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.Store.Assign(Matter, _matter, LawFirm1, firm);
        await Sync();

        var result = await Revoke(_h.Grants.ActiveRowsOf(_matter, contact).Single().Id);

        result.Should().BeOfType<Ok<RevokeAccessResponse>>().Subject.Value!.ResidualAccessTerms
            .Should().Equal(AssignedAccessMaterializer.ResidualTerm.OrganizationStandingGrant);
    }

    [Fact]
    public async Task RevokingAnOrganizationsAutoGrant_WhileItsStandingGrantStillCoversItsPeople_SaysSo_BeforeAndAfter()
    {
        var firm = Guid.NewGuid();
        var otherFirm = Guid.NewGuid();
        _h.Standing.Organizations[firm] = ExternalAccessLevel.ViewOnly;
        _h.Store.Assign(Matter, _matter, LawFirm1, firm);
        _h.Store.Assign(Matter, _matter, "sprk_assignedlawfirm2", otherFirm); // no standing grant: its twin
        await Sync();

        var before = await _h.Materializer.ListAsync(Matter, _matter, CancellationToken.None);
        var result = await Revoke(_h.Grants.ActiveRowsOf(_matter, organizationId: firm).Single().Id);
        var twin = await Revoke(_h.Grants.ActiveRowsOf(_matter, organizationId: otherFirm).Single().Id);

        before.Single(e => e.SubjectId == firm).ResidualAccessTerms
            .Should().Equal(AssignedAccessMaterializer.ResidualTerm.OrganizationMembersStandingGrant);
        result.Should().BeOfType<Ok<RevokeAccessResponse>>().Subject.Value!.ResidualAccessTerms
            .Should().Equal(AssignedAccessMaterializer.ResidualTerm.OrganizationMembersStandingGrant);
        twin.Should().BeOfType<Ok<RevokeAccessResponse>>().Subject.Value!.ResidualAccessTerms.Should().BeNull();
        Row(firm).State.Should().Be(AssignedAccessState.Declined);
    }

    [Fact]
    public async Task RevokingAnAutoGrant_WithNoReadTimeTerm_SaysNothingRemains()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();

        var result = await Revoke(_h.Grants.ActiveRowsOf(_matter, contact).Single().Id);

        result.Should().BeOfType<Ok<RevokeAccessResponse>>().Subject.Value!.ResidualAccessTerms.Should().BeNull();
    }

    [Fact]
    public async Task OnASecureRecord_TheDerivedTermsContributeNothing_SoNothingIsNamed()
    {
        var contact = _h.Contact();
        _h.Standing.Contacts[contact] = ExternalAccessLevel.ViewOnly;
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var result = await Revoke(_h.Grants.ActiveRowsOf(_matter, contact).Single().Id);

        result.Should().BeOfType<Ok<RevokeAccessResponse>>().Subject.Value!.ResidualAccessTerms.Should().BeNull();
    }

    [Fact]
    public async Task WhenTheTermsCannotBeRead_TheOperatorIsToldAccessMayRemain()
    {
        var contact = _h.Contact();
        var firm = Guid.NewGuid();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.Store.Assign(Matter, _matter, LawFirm1, firm);
        await Sync();
        _h.Participations.MembershipsUnreadable = true;

        var result = await Revoke(_h.Grants.ActiveRowsOf(_matter, contact).Single().Id);

        result.Should().BeOfType<Ok<RevokeAccessResponse>>().Subject.Value!.ResidualAccessTerms
            .Should().Equal(AssignedAccessMaterializer.ResidualTerm.Unknown);
    }

    [Fact]
    public async Task AMarkerThatCannotWriteTheLedger_NeverFailsTheOperatorsRevoke()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        _h.Store.FailLedgerWrites = true;

        var result = await Revoke(_h.Grants.ActiveRowsOf(_matter, contact).Single().Id);

        result.Should().BeOfType<Ok<RevokeAccessResponse>>();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
    }

    private sealed class WriteProbe : CallerRecordAccessProbe
    {
        public WriteProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => Task.FromResult(AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo
                               | AccessRights.Share | AccessRights.Delete);
    }
}
