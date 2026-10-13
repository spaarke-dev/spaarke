using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// GitHub #1410 verifier pass 2 F2 — owner round 82 ("the parent permissions control") in the Assigned-To materializer: a work
/// assignment NOT flagged secure (inheritance pending, Refused or Failed) filed under a secure matter is governed by the
/// matter's No Access list. The materializer asks the guard's one entry point WHATEVER the record's flag, so a walled assignee
/// is never shared, a share the enforcer removed is restored once the wall lifts (never recorded as an operator's removal),
/// and a contact grant is not converted into a share for a walled user.
/// </summary>
/// <remarks>The PRODUCTION materializer, guard, deny-list matching and parent walk, over task 148's in-memory world.</remarks>
public class AssignedAccessParentWallTests
{
    private const ExternalGrantRootType WorkAssignment = ExternalGrantRootType.WorkAssignment;
    private const string WorkAssignmentTable = "sprk_workassignment";
    private const string Attorney1 = "sprk_assignedattorney1";
    private static readonly int CollaborateMask = RecordShareLevels.MaskForRightsCsv(RecordShareLevels.CollaborateRights);

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();
    private readonly Guid _workAssignment = Guid.NewGuid();
    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();

    public AssignedAccessParentWallTests()
    {
        _h.Store.Root(WorkAssignment, _workAssignment);
        _world.SecureRoot("sprk_matter", _matter)
            .Add(WorkAssignmentTable, _workAssignment,
                ("owningteam", new EntityReference("team", SecureChildShareWorld.GeneralTeam)),
                ("sprk_issecure", false),
                ("sprk_regardingmatter", new EntityReference("sprk_matter", _matter)));
        _h.Participations.Flags[_workAssignment] = RootRecordFlags.None; // not secure (yet)
        _h.Entities = SecureChildShareWorld.EntitiesOver(() => _world).Object;
    }

    private Task<AssignedAccessOutcome> Sync() => _h.SyncAsync(WorkAssignment, _workAssignment);

    private AssignedAccessLedgerRow LedgerRow(Guid subject) =>
        _h.Store.RowsOf(_workAssignment, subject).Single(r => r.SourceField == Attorney1);

    private int? ShareMask(Guid user) => _h.Shares.MaskOf(WorkAssignmentTable, _workAssignment, DataversePrincipalRef.User(user));

    [Fact(DisplayName = "#1410 F2: a walled assignee of a not-yet-secure record under a walled secure matter is never shared; shared once the wall lifts")]
    public async Task AWalledAssignee_IsNeverShared_AndIsSharedOnceTheWallLifts()
    {
        var (contact, user) = _h.LinkedContact();
        _h.DenyList.DenySystemUserOnRecord(user, _matter);
        _h.Store.Assign(WorkAssignment, _workAssignment, Attorney1, contact);

        await Sync();

        _h.Shares.Writes.Should().BeEmpty("the secure parent's list governs the record whatever its own flag");
        LedgerRow(contact).Reason.Should().Be(AssignedAccessReason.NoAccess);

        _h.DenyList = new GrantPolicyTestDoubles.SeamNoAccessListReader(); // the wall is lifted
        await Sync();

        ShareMask(user).Should().Be(CollaborateMask);
        LedgerRow(contact).State.Should().Be(AssignedAccessState.Shared);
    }

    [Fact(DisplayName = "#1410 F2: a share the enforcer removed for the parent's wall is RemovedByNoAccess (not Declined) and restored once lifted")]
    public async Task AShareTheEnforcerRemoved_IsRemovedByNoAccess_AndRestored()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(WorkAssignment, _workAssignment, Attorney1, contact);
        await Sync();
        ShareMask(user).Should().Be(CollaborateMask, "shared while nobody was walled");
        _h.DenyList.DenySystemUserOnRecord(user, _matter);
        _h.Shares.Reset(); // the enforcer (P1) removed the walled user's share on the filed record

        await Sync();
        LedgerRow(contact).State.Should().Be(AssignedAccessState.Skipped);
        LedgerRow(contact).Reason.Should().Be(AssignedAccessReason.RemovedByNoAccess, "a known cause, never an operator's removal");
        ShareMask(user).Should().BeNull();

        _h.DenyList = new GrantPolicyTestDoubles.SeamNoAccessListReader();
        var restored = await Sync();

        restored.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Restored);
        ShareMask(user).Should().Be(CollaborateMask);
    }

    [Fact(DisplayName = "#1410 F2: with no secure parent, a removed share on a not-secure record is still the operator's (Declined)")]
    public async Task WithNoSecureParent_ARemovedShare_IsStillDeclined()
    {
        _world.Set(WorkAssignmentTable, _workAssignment, "sprk_regardingmatter", null);
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(WorkAssignment, _workAssignment, Attorney1, contact);
        await Sync();
        _h.Shares.Reset();

        await Sync();

        ShareMask(user).Should().BeNull();
        LedgerRow(contact).State.Should().Be(AssignedAccessState.Declined);
        LedgerRow(contact).Reason.Should().Be(AssignedAccessReason.RemovedOutOfBand);
    }

    [Fact(DisplayName = "#1410 F2: the parent cannot be read -> nothing shared, the run reports the fault")]
    public async Task AnUnreadableParent_SharesNothing_AndFailsTheRun()
    {
        _world.FailingQueriesOf("sprk_matter");
        var (contact, _) = _h.LinkedContact();
        _h.Store.Assign(WorkAssignment, _workAssignment, Attorney1, contact);

        var outcome = await Sync();

        _h.Shares.Writes.Should().BeEmpty("fail closed");
        LedgerRow(contact).Reason.Should().Be(AssignedAccessReason.NoAccessUnverifiable);
        outcome.Complete.Should().BeFalse("the fault fails the run, never a quiet nothing");
    }

    [Fact(DisplayName = "#1410 F2: a grant is NOT converted to a share for a walled user (left as it is), and converts once the wall lifts")]
    public async Task AGrant_IsNotConverted_ForAWalledUser_AndConvertsOnceLifted()
    {
        var contact = _h.Contact();
        _h.Store.Assign(WorkAssignment, _workAssignment, Attorney1, contact);
        await Sync();
        var grant = _h.Grants.ActiveRowsOf(_workAssignment, contact).Should().ContainSingle().Subject;
        var user = _h.LinkLater(contact);
        _h.DenyList.DenySystemUserOnRecord(user, _matter);

        await Sync();

        ShareMask(user).Should().BeNull("a walled user is not converted to a share");
        _h.Grants.ActiveRowsOf(_workAssignment, contact).Should().ContainSingle().Which.Id.Should().Be(grant.Id,
            "the grant is left exactly as it is");
        LedgerRow(contact).State.Should().Be(AssignedAccessState.Granted);

        _h.DenyList = new GrantPolicyTestDoubles.SeamNoAccessListReader();
        await Sync();

        ShareMask(user).Should().Be(CollaborateMask);
        _h.Grants.ActiveRowsOf(_workAssignment, contact).Should().BeEmpty();
        LedgerRow(contact).Reason.Should().Be(AssignedAccessReason.ConvertedFromGrant);
    }

    // ── Task 174 (owner round 84): the materializer reads the EFFECTIVE flags; S5 keeps the record's OWN flag ──────────

    /// <summary>The flag reads walk this test's world (production walks Dataverse); nothing else changes.</summary>
    private void FlagsWalkTheWorld()
    {
        var participations = new GrantPolicyTestDoubles.FlagStubParticipationService(
            RootRecordFlags.None, SecureChildShareWorld.EntitiesOver(() => _world).Object);
        participations.Flags[_workAssignment] = RootRecordFlags.None;
        _h.Participations = participations;
    }

    [Fact(DisplayName = "174 F2: an assignee of a not-yet-flagged child of a secure matter is SUGGESTED, not shared (effective Secure, A3)")]
    public async Task AnAssigneeOfANotYetFlaggedChildOfASecureMatter_IsSuggested_NotShared()
    {
        FlagsWalkTheWorld();
        var (contact, _) = _h.LinkedContact();
        _h.Store.Assign(WorkAssignment, _workAssignment, Attorney1, contact);

        await Sync();

        _h.Shares.Writes.Should().BeEmpty("the record is secure through its parent: suggest, do not share (owner A3)");
        LedgerRow(contact).State.Should().Be(AssignedAccessState.PendingConfirmation);
    }

    [Fact(DisplayName = "174 F1-c: an ended assignment on a not-yet-flagged child of a secure matter is revoked (S5 follows the stored flag)")]
    public async Task AnEndedAssignment_OnANotYetFlaggedChild_IsRevoked()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(WorkAssignment, _workAssignment, Attorney1, contact);
        await Sync();
        ShareMask(user).Should().Be(CollaborateMask, "shared before the effective flags were read");
        FlagsWalkTheWorld();
        _h.Store.Assign(WorkAssignment, _workAssignment, Attorney1, null);

        await Sync();

        ShareMask(user).Should().BeNull("the record is still owned by its business unit: S5 does not keep the share");
        LedgerRow(contact).Reason.Should().NotBe(AssignedAccessReason.KeptSecureRecord);
    }
}
