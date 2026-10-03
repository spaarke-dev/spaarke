using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 142 (#1065) — the Assigned-To invariant owner, <see cref="AssignedAccessMaterializer"/>,
/// against the owner's rules (round 2 item 5 + Q5; round 3 A1–A8; round 3 R3): Collaborate for every "Assigned *"
/// contact or organization, a POA share for a contact linked to an eligible internal user, the Restricted / Secure /
/// No Access vetoes, never-lower, operator removal sticks, revoke-on-change, renewal, 141 conversion, idempotency.
/// </summary>
/// <remarks>
/// The PRODUCTION materializer runs with the production grant core (<c>CreateGrantAsync</c>), the production No Access
/// guard and the production deny-list evaluator; only module boundaries are doubled (<see cref="AssignedAccessTestDoubles"/>).
/// Every negative has a positive twin that differs in one input. KEEP path: <c>tests/integration/auth/**</c> (ADR-038 §2).
/// </remarks>
public class AssignedAccessMaterializerTests
{
    private const ExternalGrantRootType Matter = ExternalGrantRootType.Matter;
    private const string MatterTable = "sprk_matter";
    private const string Attorney1 = "sprk_assignedattorney1";
    private const string Paralegal1 = "sprk_assignedparalegal1";
    private const string LawFirm1 = "sprk_assignedlawfirm1";
    private const int Collaborate = (int)ExternalAccessLevel.Collaborate;
    private const int ViewOnly = (int)ExternalAccessLevel.ViewOnly;
    private const int FullAccess = (int)ExternalAccessLevel.FullAccess;

    private static readonly int CollaborateMask = RecordShareLevels.MaskForRightsCsv(RecordShareLevels.CollaborateRights);

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();

    public AssignedAccessMaterializerTests() => _h.Store.Root(Matter, _matter);

    private void Secure() => _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

    private void Restricted() => _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: false, IsRestricted: true);

    private Task<AssignedAccessOutcome> Sync(bool revokeOnChange = true, AssignedAccessTrigger trigger = AssignedAccessTrigger.Sync)
        => _h.SyncAsync(Matter, _matter, revokeOnChange, trigger);

    private AssignedAccessLedgerRow LedgerRow(Guid subject, string field)
        => _h.Store.RowsOf(_matter, subject).Single(r => r.SourceField == field);

    private int? ShareMask(Guid user) => _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user));

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 2 — an unlinked contact gets ONE Collaborate grant, expiry today + 90
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnlinkedContact_OnAStandardMatter_GetsExactlyOneCollaborateGrant_ExpiringTodayPlus90_AndTheLedgerSaysGranted()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await Sync();

        outcome.Complete.Should().BeTrue();
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Subject;
        grant.AccessLevel.Should().Be(Collaborate);
        grant.ExpiresDate.Should().Be(Today.AddDays(90));
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Granted);
        row.GrantId.Should().Be(grant.Id);
        row.GrantedLevel.Should().Be(Collaborate);
        row.GrantedExpiry.Should().Be(Today.AddDays(90));
        _h.Shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task NoAssignedColumn_WritesNothing()
    {
        var outcome = await Sync();

        outcome.Complete.Should().BeTrue();
        outcome.Entries.Should().BeEmpty();
        _h.TotalWrites.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 3 — a contact linked to an eligible internal user gets a POA share; other link shapes
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LinkedEligibleContact_GetsAShareAtTheCollaborateMask_NoGrantRow_AndTheLedgerSaysShared()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        ShareMask(user).Should().Be(CollaborateMask);
        CollaborateMask.Should().Be(262167, "Collaborate's mask comes from RecordShareLevels (task 139: R|W|Append|AppendTo|Share)");
        _h.Grants.Rows.Should().BeEmpty();
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Shared);
        row.SystemUserId.Should().Be(user);
        row.GrantedLevel.Should().Be(CollaborateMask);
    }

    [Fact]
    public async Task LinkedToAUserRefusedOnlyBecauseExternal_GetsAGrantRowInstead()
    {
        var (contact, _) = _h.LinkedContact(isExternal: true);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Which.AccessLevel.Should().Be(Collaborate);
        _h.Shares.Writes.Should().BeEmpty();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Granted);
    }

    [Theory]
    [InlineData(true, 0, false)]   // disabled
    [InlineData(false, 4, false)]  // non-interactive account — not a person
    [InlineData(false, 0, true)]   // application user — not a person
    public async Task LinkedToADisabledOrNonPersonUser_IsSkippedIneligible_WithNoWrite(bool disabled, int accessMode, bool application)
    {
        var (contact, user) = _h.LinkedContact(disabled: disabled, accessMode: accessMode,
            applicationId: application ? Guid.NewGuid() : null);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        _h.Grants.Rows.Should().BeEmpty();
        _h.Shares.Writes.Should().BeEmpty();
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Skipped);
        row.Reason.Should().Be(AssignedAccessReason.Ineligible);
        row.SystemUserId.Should().Be(user);
    }

    [Fact]
    public async Task AContactRepresentingTwoUsers_IsSkippedAmbiguous_NeverOneOfTwo()
    {
        var (contact, _) = _h.LinkedContact();
        _h.LinkLater(contact);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        _h.Grants.Rows.Should().BeEmpty();
        _h.Shares.Writes.Should().BeEmpty();
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.LinkAmbiguous);
    }

    [Fact]
    public async Task AnUnreadableLink_IsSkipped_NeverAGrantToWhatMightBeAnInternalUser()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.Store.FailLinkRead = true;

        await Sync();

        _h.Grants.Rows.Should().BeEmpty();
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.LinkUnreadable);
    }

    [Fact]
    public async Task AUserBoundToTheContactsOidWithNoLinkYet_IsRepresented_AndShared()
    {
        var oid = Guid.NewGuid();
        var contact = _h.Contact(oid: oid.ToString());
        var user = Guid.NewGuid();
        _h.Store.UsersByOid[oid] = new() { new AssignedLinkCandidate(user, null, oid, false, 0, null, false) };
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        ShareMask(user).Should().Be(CollaborateMask);
    }

    [Fact]
    public async Task AUserBoundToTheOidButLinkedToAnotherContact_IsNotThisContacts_SoTheContactGetsAGrant()
    {
        var oid = Guid.NewGuid();
        var contact = _h.Contact(oid: oid.ToString());
        var user = Guid.NewGuid();
        _h.Store.UsersByOid[oid] = new() { new AssignedLinkCandidate(user, Guid.NewGuid(), oid, false, 0, null, false) };
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        ShareMask(user).Should().BeNull();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 4 — organizations
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnAssignedLawFirm_OnAStandardRoot_GetsAnOrganizationGrant_WithNoContact_AtCollaborate()
    {
        var firm = Guid.NewGuid();
        _h.Store.Assign(Matter, _matter, LawFirm1, firm);

        await Sync();

        var grant = _h.Grants.ActiveRowsOf(_matter, organizationId: firm).Should().ContainSingle().Subject;
        grant.ContactId.Should().BeNull();
        grant.AccessLevel.Should().Be(Collaborate);
        LedgerRow(firm, LawFirm1).State.Should().Be(AssignedAccessState.Granted);
    }

    [Fact]
    public async Task AnAssignedLawFirm_OnASecureRoot_GetsNoGrant_AndIsSkippedWithTheReason()
    {
        Secure();
        var firm = Guid.NewGuid();
        _h.Store.Assign(Matter, _matter, LawFirm1, firm);

        await Sync();

        _h.Grants.Rows.Should().BeEmpty();
        var row = LedgerRow(firm, LawFirm1);
        row.State.Should().Be(AssignedAccessState.Skipped);
        row.Reason.Should().Be(AssignedAccessReason.OrganizationOnSecure);
    }

    [Fact]
    public async Task AnAssignedLawFirm_OnARestrictedRoot_GetsNoGrant_AndIsSkippedRestricted()
    {
        Restricted();
        var firm = Guid.NewGuid();
        _h.Store.Assign(Matter, _matter, LawFirm1, firm);

        await Sync();

        _h.Grants.Rows.Should().BeEmpty();
        LedgerRow(firm, LawFirm1).Reason.Should().Be(AssignedAccessReason.Restricted);
    }

    [Fact]
    public async Task AnInactiveLawFirm_GetsNoGrant()
    {
        var firm = Guid.NewGuid();
        _h.Store.OrganizationStates[firm] = 1;
        _h.Store.Assign(Matter, _matter, LawFirm1, firm);

        await Sync();

        _h.Grants.Rows.Should().BeEmpty();
        LedgerRow(firm, LawFirm1).Reason.Should().Be(AssignedAccessReason.SubjectInactive);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 5 — Restricted: no contact grant, but a linked internal user's share
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnARestrictedRoot_AContactIsSkippedRestricted_ButALinkedInternalAssigneeIsStillShared()
    {
        Restricted();
        var external = _h.Contact();
        var (internalContact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, external);
        _h.Store.Assign(Matter, _matter, Paralegal1, internalContact);

        await Sync();

        _h.Grants.Rows.Should().BeEmpty();
        LedgerRow(external, Attorney1).Reason.Should().Be(AssignedAccessReason.Restricted);
        ShareMask(user).Should().Be(CollaborateMask);
        LedgerRow(internalContact, Paralegal1).State.Should().Be(AssignedAccessState.Shared);
    }

    [Fact]
    public async Task OnAnInactiveRoot_NothingIsWritten()
    {
        _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: false, IsRestricted: false, IsInactive: true);
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        _h.Grants.Rows.Should().BeEmpty();
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.RootInactive);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 6 — Secure (owner A3 = prompt): suggested, nothing written; an existing auto grant is kept
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnASecureRoot_AContactAndALinkedUserAreSuggested_PendingConfirmation_AndNothingIsGrantedOrShared()
    {
        Secure();
        var contact = _h.Contact();
        var (linked, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.Store.Assign(Matter, _matter, Paralegal1, linked);

        await Sync();

        _h.Grants.Rows.Should().BeEmpty();
        _h.Shares.Writes.Should().BeEmpty();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.PendingConfirmation);
        var linkedRow = LedgerRow(linked, Paralegal1);
        linkedRow.State.Should().Be(AssignedAccessState.PendingConfirmation);
        linkedRow.SystemUserId.Should().Be(user, "the suggestion names the user so a manual share can adopt it");
    }

    [Fact]
    public async Task WhenARecordBecomesSecure_AnExistingAutoGrantIsKept()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        Secure();
        var before = _h.TotalWrites;

        await Sync();

        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Granted);
        _h.TotalWrites.Should().Be(before);
    }

    [Fact]
    public async Task Dismiss_MarksTheSuggestionDeclined_AndNoLaterPassSuggestsOrGrantsIt()
    {
        Secure();
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        var entry = LedgerRow(contact, Attorney1);

        var declined = await _h.Materializer.DismissAsync(Matter, _matter, entry.Id, CancellationToken.None);
        _h.Participations.Flags[_matter] = RootRecordFlags.None; // even if the record is later unsecured
        await Sync();

        declined.Should().Be(1);
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Declined);
        _h.Grants.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Dismiss_OfAnEntryThatIsNotPending_ReturnsNull_AndChangesNothing()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        var entry = LedgerRow(contact, Attorney1);

        var declined = await _h.Materializer.DismissAsync(Matter, _matter, entry.Id, CancellationToken.None);

        declined.Should().BeNull();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Granted);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 7 — No Access, and unreadable inputs never grant
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AContactOnTheRecordsNoAccessList_GetsNoGrant_SkippedNoAccess()
    {
        var contact = _h.Contact();
        _h.DenyList.DenyContactOnRecord(contact, _matter);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        _h.Grants.Rows.Should().BeEmpty();
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.NoAccess);
    }

    [Fact]
    public async Task AContactWhoseOrganizationIsOnTheRecordsNoAccessList_GetsNoGrant()
    {
        var contact = _h.Contact();
        var contactsFirm = Guid.NewGuid();
        _h.Participations.ContactOrganizations[contact] = new[] { contactsFirm };
        _h.DenyList.DenyOrganizationOnRecord(contactsFirm, _matter);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        _h.Grants.Rows.Should().BeEmpty();
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.NoAccess);
    }

    [Fact]
    public async Task AContactWalledOffAnOrganizationTheRecordReferences_GetsNoGrant()
    {
        var contact = _h.Contact();
        var walledOrg = Guid.NewGuid();
        _h.Participations.RecordOrganizations[_matter] = new[] { walledOrg };
        _h.DenyList.DenyContactOnOrganization(contact, walledOrg);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        _h.Grants.Rows.Should().BeEmpty();
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.NoAccess);
    }

    [Fact]
    public async Task AContactNotOnTheList_IsGranted_TheTwinOfTheDenyCase()
    {
        var contact = _h.Contact();
        _h.DenyList.DenyContactOnRecord(Guid.NewGuid(), _matter); // someone else
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
    }

    [Fact]
    public async Task AnUnreadableDenyList_IsASkip_NeverAGrant()
    {
        var contact = _h.Contact();
        _h.DenyList.Faults = true;
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        _h.Grants.Rows.Should().BeEmpty();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Skipped);
    }

    [Fact]
    public async Task UnreadableFlags_WriteNothingAtAll_AndReportFlagsUnreadable()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.Participations.Flags[_matter] = RootRecordFlags.Unreadable;

        var outcome = await Sync();

        outcome.Status.Should().Be(AssignedAccessStatus.FlagsUnreadable);
        outcome.Entries.Should().ContainSingle().Which.Reason.Should().Be(AssignedAccessReason.FlagsUnreadable);
        _h.TotalWrites.Should().Be(0, "an unreadable flag set is no write this pass (ADR-003)");
    }

    [Fact]
    public async Task ALinkedInternalAssigneeWalledOffASecureRecord_GetsNoShare_AndIsNotEvenSuggested()
    {
        Secure();
        var (contact, user) = _h.LinkedContact();
        _h.DenyList.DenySystemUserOnRecord(user, _matter);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        _h.Shares.Writes.Should().BeEmpty();
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.NoAccess);
    }

    [Fact]
    public async Task UnreadableRootOrLedger_FailsWithNoWrite()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        _h.Store.FailLedgerRead = true;
        var ledgerFault = await Sync();
        _h.Store.FailLedgerRead = false;
        _h.Store.FailRootRead = true;
        var rootFault = await Sync();

        ledgerFault.Status.Should().Be(AssignedAccessStatus.Failed);
        ledgerFault.Failures.Should().ContainSingle().Which.Kind.Should().Be("ledger-unreadable");
        rootFault.Status.Should().Be(AssignedAccessStatus.Failed);
        _h.TotalWrites.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 8 — never lower; equal or higher existing access is untouched
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(FullAccess)]
    [InlineData(Collaborate)]
    public async Task AnExistingGrantAtOrAboveCollaborate_IsUntouched_ZeroGrantWrites_CoveredByExisting(int level)
    {
        var contact = _h.Contact();
        var existing = _h.Grants.Seed(Matter, _matter, contact, null, level, Today.AddDays(200));
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        _h.Grants.WriteCount.Should().Be(0);
        var row = _h.Grants.Rows.Single();
        row.AccessLevel.Should().Be(level, "never lowered");
        row.ExpiresDate.Should().Be(Today.AddDays(200), "never shortened");
        var ledger = LedgerRow(contact, Attorney1);
        ledger.State.Should().Be(AssignedAccessState.CoveredByExisting);
        ledger.GrantId.Should().Be(existing.Id);
    }

    [Fact]
    public async Task AnExistingShareCarryingCollaborate_IsUntouched_CoveredByExisting()
    {
        var (contact, user) = _h.LinkedContact();
        var fullMask = RecordShareLevels.MaskForRightsCsv(RecordShareLevels.FullAccessRights);
        _h.Shares.Seed(MatterTable, _matter, DataversePrincipalRef.User(user), fullMask);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        _h.Shares.Writes.Should().BeEmpty();
        ShareMask(user).Should().Be(fullMask);
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.CoveredByExisting);
    }

    [Fact]
    public async Task AnExistingLowerGrant_IsRaised_KeepingItsExpiry_AndPutBackWhenTheAssignmentEnds()
    {
        var contact = _h.Contact();
        _h.Grants.Seed(Matter, _matter, contact, null, ViewOnly, Today.AddDays(200));
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();
        var raised = _h.Grants.ActiveRowsOf(_matter, contact).Single();
        var (raisedLevel, raisedExpiry) = (raised.AccessLevel, raised.ExpiresDate);
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.RaisedFromLevelPrefix + ViewOnly);
        _h.Store.Assign(Matter, _matter, Attorney1, null);
        await Sync();

        raisedLevel.Should().Be(Collaborate);
        raisedExpiry.Should().Be(Today.AddDays(200), "the core keeps an existing expiry");
        var after = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle(
            "a raised MANUAL grant is restored, never deactivated").Subject;
        after.AccessLevel.Should().Be(ViewOnly);
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.PriorLevelRestored);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 9 — operator removal sticks; known causes are not declines
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnAutoGrantDeactivatedOutsideTheBff_IsRecordedDeclined_AndNeverReCreated()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Single();
        _h.Grants.DeactivateOutOfBand(grant.Id);

        await Sync();
        var afterDecline = _h.Grants.WriteCount;
        await Sync(trigger: AssignedAccessTrigger.Job);

        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Declined);
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.RemovedOutOfBand);
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
        _h.Grants.WriteCount.Should().Be(afterDecline, "a following form-save sync and a job run make zero writes");
    }

    [Fact]
    public async Task AnAutoShareRemovedThroughTheOobShareDialog_IsRecordedDeclined_AndNeverReShared()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        _h.Shares.Reset();

        await Sync();
        await Sync(trigger: AssignedAccessTrigger.Job);

        ShareMask(user).Should().BeNull();
        _h.Shares.Writes.Should().BeEmpty();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Declined);
    }

    [Fact]
    public async Task AnAutoShareRemovedByTheNoAccessEnforcer_IsSkippedRemovedByNoAccess_AndRestoredOnceTheWallIsLifted()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();                                     // shared while the record was standard
        Secure();                                         // became secure (A3: kept)
        _h.DenyList.DenySystemUserOnRecord(user, _matter);
        _h.Shares.Reset();                                // task 143's enforcer removed the walled share

        await Sync();
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.RemovedByNoAccess);
        ShareMask(user).Should().BeNull();

        _h.DenyList = new GrantPolicyTestDoubles.SeamNoAccessListReader(); // the entry is deactivated: the wall is lifted
        var outcome = await Sync();

        outcome.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Restored);
        ShareMask(user).Should().Be(CollaborateMask);
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Shared);
    }

    [Fact]
    public async Task AnAutoShareRemovedOnASecureRecordWithNoWall_IsAnOperatorRemoval_Declined()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        Secure();
        _h.Shares.Reset();

        await Sync();

        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Declined);
        ShareMask(user).Should().BeNull();
    }

    [Fact]
    public async Task AGrantRemovedByProjectClosure_IsAKnownCause_NotADecline()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        _h.Grants.DeactivateOutOfBand(_h.Grants.ActiveRowsOf(_matter, contact).Single().Id);
        _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: false, IsRestricted: false, IsInactive: true);

        await Sync();

        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Skipped);
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.RootInactive);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 10 — revoke-on-change (owner A4)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearingTheField_RevokesTheUnmodifiedAutoGrant()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();

        _h.Store.Assign(Matter, _matter, Attorney1, null);
        var outcome = await Sync();

        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Revoked);
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.AccessRemoved);
        outcome.Entries.Should().ContainSingle(e => e.Action == AssignedAccessAction.Revoked);
        _h.Participations.Invalidations.Should().Contain(i => i.Contacts.Contains(contact));
    }

    [Fact]
    public async Task ChangingTheField_RevokesThePreviousSubject_AndGrantsTheNewOne()
    {
        var first = _h.Contact();
        var second = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, first);
        await Sync();

        _h.Store.Assign(Matter, _matter, Attorney1, second);
        await Sync();

        _h.Grants.ActiveRowsOf(_matter, first).Should().BeEmpty();
        _h.Grants.ActiveRowsOf(_matter, second).Should().ContainSingle();
    }

    [Fact]
    public async Task ClearingOneField_DoesNotRevoke_WhenAnotherRegistryColumnStillNamesTheSubject()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.Store.Assign(Matter, _matter, Paralegal1, contact);
        await Sync();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("one subject named twice gets ONE grant");

        _h.Store.Assign(Matter, _matter, Attorney1, null);
        await Sync();

        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.KeptOtherField);
        LedgerRow(contact, Paralegal1).State.Should().Be(AssignedAccessState.Granted);
    }

    [Fact]
    public async Task ClearingTheField_DoesNotRevoke_AnAdoptedGrant()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Single();
        await _h.Materializer.MarkGrantAdoptedAsync(Matter, _matter, contact, null, grant.Id, CancellationToken.None);

        _h.Store.Assign(Matter, _matter, Attorney1, null);
        await Sync();

        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.KeptAdopted);
    }

    [Fact]
    public async Task ClearingTheField_DoesNotRevoke_AGrantModifiedSince()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Single();
        grant.AccessLevel = FullAccess; // raised by hand in MDA

        _h.Store.Assign(Matter, _matter, Attorney1, null);
        await Sync();

        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.KeptModified);
    }

    [Fact]
    public async Task ClearingTheField_OfADeclinedEntry_RemovesNothing_AndALaterReAssignmentGrantsAgain()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        _h.Grants.DeactivateOutOfBand(_h.Grants.ActiveRowsOf(_matter, contact).Single().Id);
        await Sync(); // Declined

        _h.Store.Assign(Matter, _matter, Attorney1, null);
        await Sync();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Revoked);

        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();

        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("a new assignment is not the declined one");
    }

    [Fact]
    public async Task ClearingTheField_RevokesTheUnmodifiedAutoShare()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();

        _h.Store.Assign(Matter, _matter, Attorney1, null);
        await Sync();

        ShareMask(user).Should().BeNull();
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.AccessRemoved);
    }

    [Fact]
    public async Task TheJob_WithItsRevokeSwitchOff_ReportsWouldRevoke_AndRemovesNothing()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();

        _h.Store.Assign(Matter, _matter, Attorney1, null);
        var outcome = await Sync(revokeOnChange: false, trigger: AssignedAccessTrigger.Job);

        outcome.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.WouldRevoke);
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Granted, "left for the switch or the next sync");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 11 — idempotency
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASecondSyncOfAnUnchangedRoot_MakesZeroDataverseWrites()
    {
        var contact = _h.Contact();
        var (linked, _) = _h.LinkedContact();
        var firm = Guid.NewGuid();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.Store.Assign(Matter, _matter, Paralegal1, linked);
        _h.Store.Assign(Matter, _matter, LawFirm1, firm);
        await Sync();
        var after = _h.TotalWrites;

        var second = await Sync();

        after.Should().BeGreaterThan(0);
        _h.TotalWrites.Should().Be(after);
        second.Writes.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 16 (the materializer half) — 141 conversion, renewal, the cache key
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhenTask141LinksTheContactLater_TheGrantIsRevoked_AndAShareCreated()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        var user = _h.LinkLater(contact);

        await Sync();

        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
        ShareMask(user).Should().Be(CollaborateMask);
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Shared);
        row.Reason.Should().Be(AssignedAccessReason.ConvertedFromGrant);
    }

    [Fact]
    public async Task WhenTask141LinksADeclinedContact_TheDeclineCarriesOver_AndNothingIsShared()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        _h.Grants.DeactivateOutOfBand(_h.Grants.ActiveRowsOf(_matter, contact).Single().Id);
        await Sync();
        var user = _h.LinkLater(contact);

        await Sync();

        ShareMask(user).Should().BeNull();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Declined);
    }

    [Fact]
    public async Task AnUnmodifiedAutoGrantInsideTheReminderWindow_IsRenewedToTodayPlus90()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        _h.Time.Now = _h.Time.Now.AddDays(70); // 20 days before expiry, inside the 30-day window

        var outcome = await Sync(trigger: AssignedAccessTrigger.Job);

        var renewed = Today.AddDays(70).AddDays(90);
        _h.Grants.ActiveRowsOf(_matter, contact).Single().ExpiresDate.Should().Be(renewed);
        LedgerRow(contact, Attorney1).GrantedExpiry.Should().Be(renewed);
        outcome.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Renewed);
    }

    [Fact]
    public async Task AnAutoGrantOutsideTheWindow_IsNotRenewed_TheTwin()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        _h.Time.Now = _h.Time.Now.AddDays(30); // 60 days left

        await Sync(trigger: AssignedAccessTrigger.Job);

        _h.Grants.ActiveRowsOf(_matter, contact).Single().ExpiresDate.Should().Be(Today.AddDays(90));
    }

    [Fact]
    public async Task AShareWrittenInTheJob_ClearsTheUsersRootSetUnderTheDeploymentTenant_NeverAnonymous()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await _h.Materializer.MaterializeAsync(
            new AssignedAccessRequest(Matter, _matter, AssignedAccessTrigger.Job, null, false,
                _h.Materializer.DeploymentTenants()),
            CancellationToken.None);

        var expectedId = ImpersonatedRootSetSource.CacheId(user, MatterTable);
        _h.Cache.Removed.Should().Contain(r => r.Tenant == TestTenant && r.Id == expectedId,
            "the key ImpersonatedRootSetSource.GetAsync reads for that user");
        _h.Cache.Removed.Should().NotContain(r => r.Tenant == "anonymous");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Scope (owner A6) and L1 filtering
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AChildEntityWrite_IsNeverMaterialized_ChildAssigneesGetNoRootGrant()
    {
        var outcome = await _h.Materializer.AfterWriteAsync("sprk_event", Guid.NewGuid(), null, null, CancellationToken.None);

        outcome.Should().BeNull();
        _h.TotalWrites.Should().Be(0);
    }

    [Fact]
    public async Task AnInlineWriteThatTouchedNoRegistryColumn_IsSkipped_AndOneThatDid_IsMaterialized()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var untouched = await _h.Materializer.AfterWriteAsync(MatterTable, _matter, new[] { "sprk_mattername" }, null, CancellationToken.None);
        var touched = await _h.Materializer.AfterWriteAsync(
            MatterTable, _matter, new[] { "sprk_AssignedAttorney1@odata.bind" }, null, CancellationToken.None);

        untouched.Should().BeNull();
        touched!.Complete.Should().BeTrue();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
    }

    [Fact]
    public void SourceFieldLabels_NameTheField_AsManageAccessShowsIt()
    {
        AssignedAccessMaterializer.SourceFieldLabel("sprk_assignedparalegal1").Should().Be("Assigned Paralegal 1");
        AssignedAccessMaterializer.SourceFieldLabel("sprk_assignedtoexternal").Should().Be("Assigned To (External)");
        AssignedAccessMaterializer.SourceFieldLabel("sprk_assignedlawfirmattorney1").Should().Be("Assigned Law Firm Attorney 1");
        AssignedAccessMaterializer.SourceFieldLabel("sprk_somethingelse").Should().Be("sprk_somethingelse");
    }
}
