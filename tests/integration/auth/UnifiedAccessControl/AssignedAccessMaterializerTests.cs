using FluentAssertions;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.Services.Communication;
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

    /// <summary>
    /// Task 114 (owner round 67 amendment 1): the materializer asks /share-user's ONE rule. A linked user flagged external,
    /// or with a BLANK flag, is an enabled person and is SHARED with on a record that is not Restricted — this used to
    /// fall back to a contact grant row ("refused only because external").
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task LinkedToAUserFlaggedExternalOrBlank_OnAStandardRoot_IsShared_NoGrantRow(bool? isExternal)
    {
        var (contact, user) = _h.LinkedContact(isExternal: isExternal);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        ShareMask(user).Should().Be(CollaborateMask);
        _h.Grants.Rows.Should().BeEmpty();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Shared);
    }

    /// <summary>
    /// Task 114: on a RESTRICTED root a linked user flagged external gets nothing — no share (the rule) and no contact grant
    /// (Restricted admits no contact access) — recorded Skipped(restricted). A blank-flag linked user is still shared.
    /// </summary>
    [Fact]
    public async Task OnARestrictedRoot_ALinkedUserFlaggedExternal_GetsNoShareAndNoGrant_ButABlankFlaggedOneIsShared()
    {
        Restricted();
        var (externalContact, externalUser) = _h.LinkedContact(isExternal: true);
        var (blankContact, blankUser) = _h.LinkedContact(isExternal: null);
        _h.Store.Assign(Matter, _matter, Attorney1, externalContact);
        _h.Store.Assign(Matter, _matter, Paralegal1, blankContact);

        await Sync();

        ShareMask(externalUser).Should().BeNull();
        _h.Grants.Rows.Should().BeEmpty();
        var row = LedgerRow(externalContact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Skipped);
        row.Reason.Should().Be(AssignedAccessReason.Restricted);
        ShareMask(blankUser).Should().Be(CollaborateMask, "a blank sprk_isexternal is not external (round 67 item 3)");
    }

    /// <summary>
    /// Task 114 (amendment 3): a record that BECOMES Restricted loses the auto share of a user flagged external (the
    /// Restricted remover takes it, before the materializer on the sync route) — recorded as the known cause
    /// Skipped(restricted), never Declined; and once the record is no longer Restricted the share is given back.
    /// </summary>
    [Fact]
    public async Task WhenARecordBecomesRestricted_AnExternalUsersAutoShareIsRemoved_RecordedRestricted_AndRestoredAfter()
    {
        var (contact, user) = _h.LinkedContact(isExternal: true);
        _h.SystemUser(isExternal: true, id: user);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        ShareMask(user).Should().Be(CollaborateMask);

        Restricted();
        var removal = await _h.RestrictedRemover.RemoveForRecordAsync(Matter, _matter, new[] { TestTenant }, CancellationToken.None);
        await Sync();

        removal.Removed.Should().Equal(user);
        ShareMask(user).Should().BeNull();
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Skipped, "a known cause, not an operator's removal");
        row.Reason.Should().Be(AssignedAccessReason.Restricted);

        _h.Participations.Flags[_matter] = RootRecordFlags.None; // Standard again
        await Sync();

        ShareMask(user).Should().Be(CollaborateMask, "the share comes back once the record is not Restricted");
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Shared);
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

    /// <summary>
    /// Task 142 round 18 (R-14): a contact with more organizations than ONE deny-list query holds (here 30 active
    /// memberships; the bound is 25) is CHECKED. Before round 18 the reader refused the set deterministically, so every
    /// pass reported deny-list-unreadable — the sync answered 500 and the job stayed red for as long as the contact stayed
    /// assigned. Now: no entry → granted, the run complete; an entry on the 30th organization → Skipped(no-access), the
    /// record's policy, the run still complete.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AContactWithMoreOrganizationsThanOneQueryHolds_IsChecked_AndTheRunCompletes(bool lastOrganizationWalled)
    {
        var contact = _h.Contact();
        var memberships = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToArray();
        _h.Participations.ContactOrganizations[contact] = memberships;
        if (lastOrganizationWalled)
            _h.DenyList.DenyOrganizationOnRecord(memberships[^1], _matter);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await Sync();

        outcome.Complete.Should().BeTrue("a large subject set is not a fault (round 18)");
        outcome.Failures.Should().BeEmpty();
        if (lastOrganizationWalled)
        {
            _h.Grants.Rows.Should().BeEmpty();
            LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.NoAccess);
        }
        else
        {
            _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
            LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Granted);
        }
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
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.RaisedFromLevel(ViewOnly, Today.AddDays(200)));
        _h.Store.Assign(Matter, _matter, Attorney1, null);
        await Sync();

        raisedLevel.Should().Be(Collaborate);
        raisedExpiry.Should().Be(Today.AddDays(200), "the core keeps an existing expiry");
        var after = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle(
            "a raised MANUAL grant is restored, never deactivated").Subject;
        after.AccessLevel.Should().Be(ViewOnly);
        after.ExpiresDate.Should().Be(Today.AddDays(200), "the operator's date is put back with the level");
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

    // ─────────────────────────────────────────────────────────────────────────────
    // Round r1, finding 1 (criteria 8 + 9) — only a grant that CONFERS access covers; a lapse is a known cause
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task P1_AnExpiredCollaborateGrant_DoesNotCover_TheAssignedContactIsGivenAccessThatConfers()
    {
        var contact = _h.Contact();
        var expired = _h.Grants.Seed(Matter, _matter, contact, null, Collaborate, Today.AddDays(-1));
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await Sync();

        outcome.Complete.Should().BeTrue();
        outcome.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Granted);
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Subject;
        grant.Id.Should().Be(expired.Id, "one row per (subject, root): the core's upsert gives the lapsed row a live date");
        grant.AccessLevel.Should().Be(Collaborate);
        grant.ExpiresDate.Should().Be(Today.AddDays(90));
        ExternalParticipationService.ConfersAccessOn(grant.ExpiresDate, Today).Should().BeTrue();
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Granted, "never CoveredByExisting over a grant that confers nothing");
        row.GrantedExpiry.Should().Be(Today.AddDays(90));
        row.Reason.Should().BeNull("a lapsed grant conferred nothing, so there is no earlier level to put back");
    }

    [Fact]
    public async Task ACollaborateGrantExpiringToday_StillConfers_SoItCovers_TheTwinOfP1()
    {
        var contact = _h.Contact();
        var existing = _h.Grants.Seed(Matter, _matter, contact, null, Collaborate, Today);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        _h.Grants.WriteCount.Should().Be(0, "the expiry date itself still confers (the read filter's ge)");
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.CoveredByExisting);
        row.GrantId.Should().Be(existing.Id);
    }

    [Fact]
    public async Task P2_ACoveringManualGrantThatLapsesLater_IsAKnownCause_AndTheStillAssignedContactIsGivenAccessAgain()
    {
        var contact = _h.Contact();
        _h.Grants.Seed(Matter, _matter, contact, null, Collaborate, Today.AddDays(1));
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.CoveredByExisting);
        var later = Today.AddDays(3);
        _h.Time.Now = _h.Time.Now.AddDays(3); // the covering grant expired yesterday

        var outcome = await Sync(trigger: AssignedAccessTrigger.Job);

        outcome.Complete.Should().BeTrue();
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Granted, "a lapse is a known cause (owner (e)) — never Declined, never left covered");
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Subject;
        grant.ExpiresDate.Should().Be(later.AddDays(90));
        ExternalParticipationService.ConfersAccessOn(grant.ExpiresDate, later).Should().BeTrue();
    }

    [Fact]
    public async Task ACoveringManualGrantDeactivatedOutsideTheBff_IsAnOperatorRemoval_Declined_TheTwinOfP2()
    {
        var contact = _h.Contact();
        var manual = _h.Grants.Seed(Matter, _matter, contact, null, Collaborate, Today.AddDays(200));
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        _h.Grants.DeactivateOutOfBand(manual.Id);

        await Sync();
        await Sync(trigger: AssignedAccessTrigger.Job);

        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Declined);
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.RemovedOutOfBand);
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty("a deliberate removal is not a lapse");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Round r1, finding 2 (ADR-003) — never "granted" or "restored" over a grant that confers nothing
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task P3_AnExpiredLowerGrant_IsGivenCollaborateWithADateThatConfers_AndTheEndOfTheAssignmentLeavesNoAccess()
    {
        var contact = _h.Contact();
        var lapsed = _h.Grants.Seed(Matter, _matter, contact, null, ViewOnly, Today.AddDays(-5));
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await Sync();

        outcome.Complete.Should().BeTrue("the core's expired_not_restored warning cannot occur: the rule asks for a date");
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Subject;
        grant.Id.Should().Be(lapsed.Id);
        grant.AccessLevel.Should().Be(Collaborate);
        grant.ExpiresDate.Should().Be(Today.AddDays(90));
        ExternalParticipationService.ConfersAccessOn(grant.ExpiresDate, Today).Should().BeTrue();
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Granted);
        row.GrantedExpiry.Should().Be(Today.AddDays(90), "the ledger records the date the grant really carries");
        row.Reason.Should().BeNull("the lapsed View Only conferred nothing — not a level to put back");

        _h.Store.Assign(Matter, _matter, Attorney1, null);
        await Sync();

        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty("the subject had no access here before the assignment");
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.AccessRemoved);
    }

    [Fact]
    public async Task AGrantThatLapsesBetweenTheRulesReadAndTheCoresRead_IsAFailure_NeverRecordedGranted_AndTheNextPassHealsIt()
    {
        var contact = _h.Contact();
        var manual = _h.Grants.Seed(Matter, _matter, contact, null, ViewOnly, Today); // confers today
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        // Read 1 is the rule's (it sees a conferring View Only and asks to raise it, keeping its date); read 2 is the
        // grant core's — by then the row has lapsed, so the core writes and answers expired_not_restored.
        _h.Grants.BeforeGrantQuery = n =>
        {
            if (n == 2)
                manual.ExpiresDate = Today.AddDays(-1);
        };

        var outcome = await Sync();

        outcome.Complete.Should().BeFalse();
        outcome.Failures.Should().ContainSingle().Which.Kind.Should().Be("grant-not-conferring");
        outcome.Entries.Should().NotContain(e => e.Action == AssignedAccessAction.Raised || e.Action == AssignedAccessAction.Granted);
        _h.Store.RowsOf(_matter, contact).Should().NotContain(r => r.State == AssignedAccessState.Granted);

        _h.Grants.BeforeGrantQuery = null;
        var next = await Sync(trigger: AssignedAccessTrigger.Job);

        next.Complete.Should().BeTrue();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Granted);
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Single();
        ExternalParticipationService.ConfersAccessOn(grant.ExpiresDate, Today).Should().BeTrue();
    }

    [Fact]
    public async Task ARaisedGrantThatLapsedBeforeTheAssignmentEnded_IsPutBackToItsLevel_ButNeverReportedRestored()
    {
        var contact = _h.Contact();
        _h.Grants.Seed(Matter, _matter, contact, null, ViewOnly, Today.AddDays(10));
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync(); // raised to Collaborate, keeping its 10-day date
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.RaisedFromLevel(ViewOnly, Today.AddDays(10)));
        _h.Store.Assign(Matter, _matter, Attorney1, null);
        await Sync(revokeOnChange: false, trigger: AssignedAccessTrigger.Job); // report-only: kept, no longer renewed
        _h.Time.Now = _h.Time.Now.AddDays(11); // it lapses

        var outcome = await Sync();

        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Subject;
        grant.AccessLevel.Should().Be(ViewOnly, "the operator's level is put back");
        grant.ExpiresDate.Should().Be(Today.AddDays(10), "never extended by the rule");
        var entry = outcome.Entries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AssignedAccessAction.Ledger, "no access was put back, so none is reported");
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Revoked);
        row.Reason.Should().Be(AssignedAccessReason.PriorLevelRestoredLapsed);
        outcome.Complete.Should().BeTrue("the rule's own access had ended by expiry — nothing is left to retry");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Round r1, findings 3–5 (criterion 10 for SHARES; owner S5)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearingOneField_DoesNotRemoveTheShare_WhenAnotherRegistryColumnStillNamesTheLinkedAssignee()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.Store.Assign(Matter, _matter, Paralegal1, contact);
        await Sync();
        ShareMask(user).Should().Be(CollaborateMask);

        _h.Store.Assign(Matter, _matter, Attorney1, null);
        await Sync();
        await Sync(trigger: AssignedAccessTrigger.Job); // and the next pass sees the share there — never a decline

        ShareMask(user).Should().Be(CollaborateMask, "the user is still assigned through Paralegal 1");
        _h.Shares.Writes.Should().NotContain(w => w.StartsWith("RevokeAccess", StringComparison.Ordinal));
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Revoked);
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.KeptOtherField);
        LedgerRow(contact, Paralegal1).State.Should().Be(AssignedAccessState.Shared);
    }

    [Theory]
    [InlineData(true)]  // the operator widened it to Full Access in the OOB MDA Share dialog
    [InlineData(false)] // the operator narrowed it to Read in the same dialog
    public async Task ClearingTheField_DoesNotRemoveAShareWhoseRightsAnOperatorChangedOutsideTheBff(bool widened)
    {
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        var changedMask = widened ? RecordShareLevels.MaskForRightsCsv(RecordShareLevels.FullAccessRights) : 1;
        _h.Shares.Seed(MatterTable, _matter, DataversePrincipalRef.User(user), changedMask);
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Shared, "the OOB dialog marks nothing Adopted");

        _h.Store.Assign(Matter, _matter, Attorney1, null);
        await Sync();

        ShareMask(user).Should().Be(changedMask, "deliberately granted access is never removed by the rule");
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.KeptModified);
    }

    [Fact]
    public async Task ClearingTheField_OnASecureRecord_NeverRemovesTheAutoShare_OwnerS5()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync(); // shared while the record was standard
        Secure();     // became secure (A3: kept)
        _h.Store.Assign(Matter, _matter, Attorney1, null);

        await Sync();

        ShareMask(user).Should().Be(CollaborateMask, "a secure record always keeps someone who can see it (S5)");
        _h.Shares.Writes.Should().NotContain(w => w.StartsWith("RevokeAccess", StringComparison.Ordinal));
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Revoked);
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.KeptSecureRecord);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Round r1, finding 6 (criterion 9) — an inactive organization (R2) is a known cause, not a decline
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnOrganizationGrantDeactivatedByR2_IsAKnownCause_NotADecline_AndIsRestoredWhenTheOrganizationIsActiveAgain()
    {
        var firm = Guid.NewGuid();
        _h.Store.Assign(Matter, _matter, LawFirm1, firm);
        await Sync();
        var grant = _h.Grants.ActiveRowsOf(_matter, organizationId: firm).Single();
        _h.Store.OrganizationStates[firm] = 1;   // the organization is deactivated…
        _h.Grants.DeactivateOutOfBand(grant.Id); // …and ExternalAccessReconciliationJob R2 deactivates its grant

        await Sync(trigger: AssignedAccessTrigger.Job);

        LedgerRow(firm, LawFirm1).State.Should().Be(AssignedAccessState.Skipped, "R2 is a known cause, never an operator's removal");
        LedgerRow(firm, LawFirm1).Reason.Should().Be(AssignedAccessReason.SubjectInactive);

        _h.Store.OrganizationStates[firm] = 0;   // reactivated
        await Sync(trigger: AssignedAccessTrigger.Job);

        _h.Grants.ActiveRowsOf(_matter, organizationId: firm).Should().ContainSingle("restored once the organization is active");
        LedgerRow(firm, LawFirm1).State.Should().Be(AssignedAccessState.Granted);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Round r1, finding 10 (criterion 16) — Restricted AFTER the grant
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhenARecordBecomesRestricted_TheAutoGrantIsKeptButNotRenewed_AndRenewedOnceItIsStandardAgain()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        Restricted();
        _h.Time.Now = _h.Time.Now.AddDays(70); // inside the renewal window
        var before = _h.Grants.WriteCount;

        var restricted = await Sync(trigger: AssignedAccessTrigger.Job);

        restricted.Complete.Should().BeTrue();
        _h.Grants.WriteCount.Should().Be(before, "no renewal (and no other grant write) on a Restricted record");
        var kept = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Subject;
        kept.ExpiresDate.Should().Be(Today.AddDays(90));
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Granted);
        _h.Participations.Flags[_matter].RemovesContactSourcedAccess.Should().BeTrue(
            "the read path suppresses contact access on a Restricted record (round 2 item 3)");

        _h.Participations.Flags[_matter] = RootRecordFlags.None;
        await Sync(trigger: AssignedAccessTrigger.Job);

        _h.Grants.ActiveRowsOf(_matter, contact).Single().ExpiresDate.Should().Be(Today.AddDays(70).AddDays(90));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Round r1, finding 11 — eligibility changes after a share (the link itself never moves: 141 contract §2–§3)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ALinkedAssigneeWhoseUserIsLaterDisabled_KeepsTheShare_WithZeroWrites()
    {
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        var candidate = _h.Store.UsersByLink[contact].Single();
        _h.Store.UsersByLink[contact] = new() { candidate with { IsDisabled = true } };
        var before = _h.TotalWrites;

        await Sync(trigger: AssignedAccessTrigger.Job);

        ShareMask(user).Should().Be(CollaborateMask, "a disabled user cannot sign in; re-enabled, the still-assigned user keeps it");
        _h.TotalWrites.Should().Be(before);
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Shared);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Round r2, finding 1 (criterion 10) — a raised grant goes back to the operator's level AND date
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARaisedGrantRenewedWhileAssigned_IsPutBackToTheOperatorsLevelAndDate_WhenTheAssignmentEnds()
    {
        var contact = _h.Contact();
        var operatorDate = Today.AddDays(10);
        _h.Grants.Seed(Matter, _matter, contact, null, ViewOnly, operatorDate);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync(); // raised to Collaborate, keeping the operator's date
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.RaisedFromLevel(ViewOnly, operatorDate));

        var renewal = await Sync(trigger: AssignedAccessTrigger.Job); // 10 days left: inside the 30-day window

        renewal.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Renewed);
        _h.Grants.ActiveRowsOf(_matter, contact).Single().ExpiresDate.Should().Be(Today.AddDays(90), "renewed while assigned (A5)");

        _h.Store.Assign(Matter, _matter, Attorney1, null);
        var end = await Sync();

        end.Complete.Should().BeTrue();
        end.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Restored);
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Subject;
        grant.AccessLevel.Should().Be(ViewOnly);
        grant.ExpiresDate.Should().Be(operatorDate, "the operator's date — the rule's renewal never outlives the assignment");
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.PriorLevelRestored);
    }

    [Fact]
    public async Task ARaisedGrantRenewedPastTheOperatorsDate_EndsWithTheAssignment_ReportedRevoked_NeverRestored()
    {
        var contact = _h.Contact();
        var operatorDate = Today.AddDays(10);
        _h.Grants.Seed(Matter, _matter, contact, null, ViewOnly, operatorDate);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        await Sync(trigger: AssignedAccessTrigger.Job); // renewed to today + 90
        _h.Time.Now = _h.Time.Now.AddDays(20);           // the operator's date has passed; the renewed grant still confers
        var later = Today.AddDays(20);
        _h.Store.Assign(Matter, _matter, Attorney1, null);
        var invalidationsBefore = _h.Participations.Invalidations.Count;

        var end = await Sync();

        end.Complete.Should().BeTrue("the assignment's access ended; nothing is left to retry");
        var entry = end.Entries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AssignedAccessAction.Revoked, "putting the operator's date back ENDED the access");
        entry.Reason.Should().Be(AssignedAccessReason.PriorLevelRestoredLapsed);
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Subject;
        grant.AccessLevel.Should().Be(ViewOnly);
        grant.ExpiresDate.Should().Be(operatorDate);
        ExternalParticipationService.ConfersAccessOn(grant.ExpiresDate, later).Should().BeFalse(
            "no access outlives the date the operator chose");
        _h.Participations.Invalidations.Skip(invalidationsBefore).Should().Contain(i => i.Contacts.Contains(contact),
            "the core skips its own invalidation when the written grant confers nothing — the cached grant set must not keep serving it");
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Revoked);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Round r2, finding 2 (criterion 16 / ADR-036 A1) — a restore the record's policy forbids waits; it is not a failure
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, "secure", ExternalGrantLifecycle.OrgGrantDirectOnlyReasonCode)]
    [InlineData(true, "limited", ExternalGrantLifecycle.OrgGrantDirectOnlyReasonCode)]
    [InlineData(true, "restricted", ExternalGrantLifecycle.RecordRestrictedReasonCode)]
    [InlineData(false, "restricted", ExternalGrantLifecycle.RecordRestrictedReasonCode)]
    [InlineData(false, "no-access", ExternalGrantLifecycle.GranteeDeniedReasonCode)]
    public async Task ARaisedGrantWhoseRestoreThePolicyForbids_WaitsWithoutFailing_AndIsPutBackOnceThePolicyAllows(
        bool organization, string hold, string reasonCode)
    {
        var subject = organization ? Guid.NewGuid() : _h.Contact();
        var field = organization ? LawFirm1 : Attorney1;
        _h.Grants.Seed(Matter, _matter, organization ? null : subject, organization ? subject : null, ViewOnly, Today.AddDays(200));
        _h.Store.Assign(Matter, _matter, field, subject);
        await Sync();
        LedgerRow(subject, field).Reason.Should().Be(AssignedAccessReason.RaisedFromLevel(ViewOnly, Today.AddDays(200)));
        switch (hold)
        {
            case "secure": Secure(); break;
            case "limited": _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: false, IsRestricted: false, IsLimited: true); break;
            case "restricted": Restricted(); break;
            default: _h.DenyList.DenyContactOnRecord(subject, _matter); break;
        }

        _h.Store.Assign(Matter, _matter, field, null);
        var held = await Sync();
        var before = _h.TotalWrites;
        var job = await Sync(trigger: AssignedAccessTrigger.Job);

        foreach (var pass in new[] { held, job })
        {
            pass.Complete.Should().BeTrue("the record's policy is not a fault — the job is not red and the form is not warned");
            pass.Failures.Should().BeEmpty();
            var entry = pass.Entries.Should().ContainSingle().Subject;
            entry.Reason.Should().Be(AssignedAccessReason.RestorePendingPrefix + reasonCode);
            entry.Action.Should().Be(AssignedAccessAction.None);
        }

        _h.TotalWrites.Should().Be(before, "a held restore writes nothing, pass after pass");
        var kept = (organization ? _h.Grants.ActiveRowsOf(_matter, organizationId: subject) : _h.Grants.ActiveRowsOf(_matter, subject))
            .Should().ContainSingle().Subject;
        kept.AccessLevel.Should().Be(Collaborate);
        LedgerRow(subject, field).State.Should().Be(AssignedAccessState.Granted, "kept live, so every pass tries again");

        _h.Participations.Flags[_matter] = RootRecordFlags.None;          // the policy allows it again…
        _h.DenyList = new GrantPolicyTestDoubles.SeamNoAccessListReader(); // …and the No Access entry is gone
        var allowed = await Sync();

        allowed.Complete.Should().BeTrue();
        allowed.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Restored);
        var restored = (organization ? _h.Grants.ActiveRowsOf(_matter, organizationId: subject) : _h.Grants.ActiveRowsOf(_matter, subject))
            .Should().ContainSingle().Subject;
        restored.AccessLevel.Should().Be(ViewOnly);
        restored.ExpiresDate.Should().Be(Today.AddDays(200));
        LedgerRow(subject, field).Reason.Should().Be(AssignedAccessReason.PriorLevelRestored);
    }

    [Fact]
    public async Task ARestoreRefusedBecauseThePolicyCouldNotBeRead_IsStillAFailure_TheTwinOfThePolicyHold()
    {
        var contact = _h.Contact();
        _h.Grants.Seed(Matter, _matter, contact, null, ViewOnly, Today.AddDays(200));
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        _h.Store.Assign(Matter, _matter, Attorney1, null);
        // The rule's own flag read (first) succeeds; the grant core's — after the rule's grant query — faults.
        _h.Grants.BeforeGrantQuery = _ => _h.Participations.ThrowOnRead = true;

        var outcome = await Sync();

        outcome.Complete.Should().BeFalse("an unreadable policy is a fault, not the record's policy (ADR-003)");
        outcome.Failures.Should().ContainSingle().Which.Kind.Should().Be("restore-refused");
        _h.Grants.ActiveRowsOf(_matter, contact).Single().AccessLevel.Should().Be(Collaborate);

        _h.Grants.BeforeGrantQuery = null;
        _h.Participations.ThrowOnRead = false;
        var next = await Sync();

        next.Complete.Should().BeTrue();
        _h.Grants.ActiveRowsOf(_matter, contact).Single().AccessLevel.Should().Be(ViewOnly);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Round r2, finding 3 — the RECORDED deviation from "never downgrades a level" (notes §6)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnExpiredFullAccessGrant_IsGivenCollaborate_TheRecordedDeviation_ItConferredNothing()
    {
        var contact = _h.Contact();
        var lapsed = _h.Grants.Seed(Matter, _matter, contact, null, FullAccess, Today.AddDays(-5));
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await Sync();

        outcome.Complete.Should().BeTrue();
        var grant = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Subject;
        grant.Id.Should().Be(lapsed.Id);
        grant.AccessLevel.Should().Be(Collaborate,
            "the rule's ceiling (A1) cannot write Full Access, and the expired Full Access conferred nothing (none -> Collaborate)");
        grant.ExpiresDate.Should().Be(Today.AddDays(90));
        LedgerRow(contact, Attorney1).Reason.Should().BeNull("a lapsed level is not a prior to restore");

        _h.Store.Assign(Matter, _matter, Attorney1, null);
        await Sync();

        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty("the subject had no access here before the assignment");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Round r3, item 2 (verifier r2 finding 3 / criterion 19) — a raise records the LATEST conferring date
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two conferring View Only grants on ONE key before the raise (pre-existing duplicates, or a lost create race). The
    /// grant core collapses the key onto the longest-conferring survivor, so the operator's access ran until the LATER
    /// date: the raise records that one, and the end of the assignment puts it back — never the earlier date, which would
    /// shorten the surviving grant (constraint "never shortens an expiry").
    /// </summary>
    [Fact]
    public async Task TwoConferringLowerGrantsOnOneKey_TheRaiseRecordsTheLaterDate_AndTheRestoreNeverShortensTheSurvivor()
    {
        var contact = _h.Contact();
        var earlier = Today.AddDays(30);
        var later = Today.AddDays(200);
        _h.Grants.Seed(Matter, _matter, contact, null, ViewOnly, earlier);
        _h.Grants.Seed(Matter, _matter, contact, null, ViewOnly, later);
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var raise = await Sync();

        raise.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Raised);
        var survivor = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle(
            "the core collapses the key onto the longest-conferring row").Subject;
        survivor.ExpiresDate.Should().Be(later);
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.RaisedFromLevel(ViewOnly, later),
            "the subject's access ran until the LATER of the two conferring dates");

        _h.Store.Assign(Matter, _matter, Attorney1, null);
        var end = await Sync();

        end.Complete.Should().BeTrue();
        end.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Restored);
        var restored = _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle().Subject;
        restored.Id.Should().Be(survivor.Id);
        restored.AccessLevel.Should().Be(ViewOnly);
        restored.ExpiresDate.Should().Be(later, "a restore never shortens the operator's surviving grant");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Round r3, item 3 (verifier r2 finding 4) — a No Access check that THROWS is a fault, never an entry or a hold
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>An HttpClient timeout: the deny-veto code rethrows it, so the No Access check THROWS (task 139 r1).</summary>
    private static TaskCanceledException DenyListTimeout() => new("Simulated HttpClient timeout (the caller did not cancel).");

    private CapturingLogger<AssignedAccessMaterializer> CaptureLog()
    {
        var log = new CapturingLogger<AssignedAccessMaterializer>();
        _h.Logger = log;
        return log;
    }

    /// <summary>The fault's fingerprint: not complete, one failure of its own kind for the subject, its own error line,
    /// and never a policy hold or a No Access entry.</summary>
    private static void AssertDenyListFault(AssignedAccessOutcome outcome, CapturingLogger<AssignedAccessMaterializer> log, Guid subject)
    {
        outcome.Complete.Should().BeFalse("a deny-list read fault is reported: the job goes red and the form is told");
        var failure = outcome.Failures.Should().ContainSingle().Subject;
        failure.Kind.Should().Be(AssignedAccessMaterializer.DenyListUnreadableFailure);
        failure.SubjectId.Should().Be(subject);
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("DENY-LIST-UNREADABLE"),
            "logged as its own line, so monitoring can alert on a deny-list read fault");
        outcome.Entries.Should().NotContain(
            e => e.Reason != null && e.Reason.StartsWith(AssignedAccessReason.RestorePendingPrefix, StringComparison.Ordinal),
            "a fault is never the record's policy (a hold)");
        outcome.Entries.Should().NotContain(e => e.Reason == AssignedAccessReason.NoAccess, "a fault is never an entry on the list");
    }

    /// <summary>
    /// The verifier's case: a raised grant's restore whose No Access check THROWS. The core answers with the deny check's
    /// wire code, but it is a FAULT: reported (never a green "restore-pending"), nothing written, the row kept live and the
    /// restore made once the list reads again. The twin — an ENTRY on the list, a real hold — is the "no-access" row of
    /// <see cref="ARaisedGrantWhoseRestoreThePolicyForbids_WaitsWithoutFailing_AndIsPutBackOnceThePolicyAllows"/>.
    /// </summary>
    [Fact]
    public async Task ARestoreWhoseNoAccessCheckThrows_IsADenyListFault_NeverAPolicyHold_AndIsPutBackOnceTheListReads()
    {
        var log = CaptureLog();
        var contact = _h.Contact();
        _h.Grants.Seed(Matter, _matter, contact, null, ViewOnly, Today.AddDays(200));
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync(); // raised to Collaborate
        _h.Store.Assign(Matter, _matter, Attorney1, null);
        _h.DenyList.Throws = DenyListTimeout();
        var before = _h.TotalWrites;

        var faulted = await Sync();

        AssertDenyListFault(faulted, log, contact);
        _h.TotalWrites.Should().Be(before, "nothing is written while the list cannot be read");
        _h.Grants.ActiveRowsOf(_matter, contact).Single().AccessLevel.Should().Be(Collaborate);
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Granted, "kept live, so the next pass retries");

        _h.DenyList.Throws = null;
        var next = await Sync();

        next.Complete.Should().BeTrue();
        next.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Restored);
        var restored = _h.Grants.ActiveRowsOf(_matter, contact).Single();
        restored.AccessLevel.Should().Be(ViewOnly);
        restored.ExpiresDate.Should().Be(Today.AddDays(200));
    }

    /// <summary>A fresh grant whose No Access check (the grant core's) THROWS: nothing granted, reported, not "no-access".</summary>
    [Fact]
    public async Task AFreshGrantWhoseNoAccessCheckThrows_IsADenyListFault_NotNoAccess_AndNothingIsGranted()
    {
        var log = CaptureLog();
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.DenyList.Throws = DenyListTimeout();

        var faulted = await Sync();

        AssertDenyListFault(faulted, log, contact);
        _h.Grants.Rows.Should().BeEmpty("fail closed");
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Skipped);
        row.Reason.Should().Be(AssignedAccessReason.NoAccessUnverifiable, "nobody is known to be on the list");

        _h.DenyList.Throws = null;
        var next = await Sync();

        next.Complete.Should().BeTrue();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("decided again once the list reads (Skipped is never sticky)");
    }

    /// <summary>On a SECURE record the materializer's own No Access check runs before suggesting: a throw is a fault too.</summary>
    [Fact]
    public async Task ASecureSuggestionWhoseNoAccessCheckThrows_IsADenyListFault_AndIsNotSuggested()
    {
        Secure();
        var log = CaptureLog();
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.DenyList.Throws = DenyListTimeout();

        var faulted = await Sync();

        AssertDenyListFault(faulted, log, contact);
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Skipped, "never suggested while the list cannot be read");
        row.Reason.Should().Be(AssignedAccessReason.NoAccessUnverifiable);
        _h.Grants.Rows.Should().BeEmpty();

        _h.DenyList.Throws = null;
        var next = await Sync();

        next.Complete.Should().BeTrue();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.PendingConfirmation);
    }

    /// <summary>A renewal (owner A5) whose No Access check THROWS: reported, not renewed this pass, the grant kept as it is.</summary>
    [Fact]
    public async Task ARenewalWhoseNoAccessCheckThrows_IsADenyListFault_AndTheGrantIsKeptAsItIs()
    {
        var log = CaptureLog();
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync(); // granted, today + 90
        _h.Time.Now = _h.Time.Now.AddDays(70); // 20 days left: inside the 30-day renewal window
        _h.DenyList.Throws = DenyListTimeout();

        var faulted = await Sync(trigger: AssignedAccessTrigger.Job);

        AssertDenyListFault(faulted, log, contact);
        faulted.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.None);
        _h.Grants.ActiveRowsOf(_matter, contact).Single().ExpiresDate.Should().Be(Today.AddDays(90), "not renewed this pass");
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Granted);

        _h.DenyList.Throws = null;
        var next = await Sync(trigger: AssignedAccessTrigger.Job);

        next.Complete.Should().BeTrue();
        next.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Renewed);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Batch 4 integration — the BINDING 142 x 149 merge-order obligation (task 149 note §14): a CONFIRMED root share
    // write reaches the secure record's CHILDREN in the same run, through task 149's REAL synchronizer.
    // ─────────────────────────────────────────────────────────────────────────────

    private static readonly Guid ChildDocument = Guid.Parse("14914200-0000-4000-8000-0000000000d1");

    private int? ChildMask(Guid user) => _h.Shares.MaskOf("sprk_document", ChildDocument, DataversePrincipalRef.User(user));

    /// <summary>A Secure-team-owned matter (its <c>sprk_issecure</c> as given) with one Secure-team-owned document under it.</summary>
    private Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld SecureTeamOwnedMatter(bool flagged) =>
        Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.Standard()
            .Add(MatterTable, _matter,
                ("owningteam", new Microsoft.Xrm.Sdk.EntityReference(
                    "team", Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.SecureTeam)),
                ("sprk_issecure", flagged))
            .SecureChild("sprk_document", ChildDocument, ("sprk_matter", MatterTable, _matter));

    /// <summary>
    /// (a) A 142 share REMOVAL on a root the synchronizer treats as secure — Secure-team-owned with its flag reading No, the
    /// only state in which 142 removes one there — removes the user from every child in the SAME call, not at the next tick.
    /// </summary>
    [Fact]
    public async Task AnAutoShareRemoval_OnASecureTeamOwnedRoot_RemovesTheUserFromItsChildren_InTheSameRun()
    {
        _h.ChildWorld = SecureTeamOwnedMatter(flagged: false);
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        var shared = await Sync();
        shared.Complete.Should().BeTrue();
        ShareMask(user).Should().Be(CollaborateMask);
        ChildMask(user).Should().NotBeNull("the root share write reached the child in the same run");

        _h.Store.Assign(Matter, _matter, Attorney1, null);
        var removed = await Sync();

        removed.Complete.Should().BeTrue();
        ShareMask(user).Should().BeNull("142 ended the auto share on the root");
        ChildMask(user).Should().BeNull("and the child followed in the same run");
    }

    /// <summary>
    /// (b) The RESTORE after a lifted No Access wall on a flagged secure root gives the user every child in the same call
    /// (task 143's guard is consulted for the child grant, as for every one).
    /// </summary>
    [Fact]
    public async Task ARestoreAfterTheWallIsLifted_OnAFlaggedSecureRoot_GivesTheUserItsChildren_InTheSameRun()
    {
        _h.ChildWorld = SecureTeamOwnedMatter(flagged: true);
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();                                     // shared while the record's flags read standard
        Secure();                                         // became secure (A3: kept)
        _h.DenyList.DenySystemUserOnRecord(user, _matter);
        _h.Shares.Reset();                                // task 143's enforcer removed the walled share (root and child)

        await Sync();
        ChildMask(user).Should().BeNull("walled: nothing restored, nothing fanned out");

        _h.DenyList = new GrantPolicyTestDoubles.SeamNoAccessListReader(); // the wall is lifted
        var restored = await Sync();

        restored.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Restored);
        restored.Complete.Should().BeTrue();
        ShareMask(user).Should().Be(CollaborateMask);
        ChildMask(user).Should().NotBeNull("the restore reached the child in the same run, not at the next tick");
    }

    /// <summary>(c) A fan-out that is not complete is a failure of the run; the root write STANDS.</summary>
    [Fact]
    public async Task AnIncompleteFanOut_IsARunFailure_AndTheRootShareStands()
    {
        _h.ChildWorld = SecureTeamOwnedMatter(flagged: false).FailingQueriesOf("sprk_document");
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await Sync();

        outcome.Complete.Should().BeFalse("the job reports Success = false");
        outcome.Failures.Should().ContainSingle(f => f.Kind == "children-incomplete");
        ShareMask(user).Should().Be(CollaborateMask, "the root write is never rolled back");
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Shared);
    }

    /// <summary>
    /// (d) A run with no confirmed share write does not read the children at all: over the same unreadable-children world
    /// as (c), a grant-only run (an unlinked contact) and a second run of an unchanged root are complete.
    /// </summary>
    [Fact]
    public async Task ARunWithNoConfirmedShareWrite_DoesNotReadTheChildren()
    {
        _h.ChildWorld = SecureTeamOwnedMatter(flagged: false).FailingQueriesOf("sprk_document");
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var granted = await Sync();

        granted.Complete.Should().BeTrue("a grant is not a share: the children are never read");
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
        _h.Shares.Writes.Should().BeEmpty();

        var (linked, user) = _h.LinkedContact();
        _h.ChildWorld = SecureTeamOwnedMatter(flagged: false);
        _h.Store.Assign(Matter, _matter, Paralegal1, linked);
        (await Sync()).Complete.Should().BeTrue();
        ShareMask(user).Should().Be(CollaborateMask);

        _h.ChildWorld = SecureTeamOwnedMatter(flagged: false).FailingQueriesOf("sprk_document");
        var unchanged = await Sync();

        unchanged.Complete.Should().BeTrue("an unchanged root writes no share, so the unreadable children are never asked");
        unchanged.Writes.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Round r4, item 1 (owner round 13 item 4 — R-13) — the faults the deny-veto check used to ABSORB into "denied" are
    // an Unverifiable answer now: on every consumer path they are a deny-list fault, never an entry and never a hold
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The three read faults the deny-veto code absorbed before r4 (memberships unreadable, the record's referenced
    /// organizations unreadable, a fail-closed deny-list read) — each answered <see cref="NoAccessCheckAnswer.Unverifiable"/>
    /// by the write-time check now. The r3 tests above inject a THROW; these inject the faults that never threw.
    /// </summary>
    private void InjectAbsorbedNoAccessFault(string fault)
    {
        switch (fault)
        {
            case "memberships-unreadable": _h.Participations.MembershipsUnreadable = true; break;
            case "referenced-organizations-unreadable": _h.Participations.UnreadableReferencedOrganizations[_matter] = true; break;
            case "deny-list-fails-closed": _h.DenyList.Faults = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault), fault, "unknown fault");
        }
    }

    private void ClearAbsorbedNoAccessFaults()
    {
        _h.Participations.MembershipsUnreadable = false;
        _h.Participations.UnreadableReferencedOrganizations.TryRemove(_matter, out _);
        _h.DenyList.Faults = false;
    }

    /// <summary>
    /// The R-13 headline: before r4 a restore whose No Access check met one of these faults HELD green
    /// (<c>restore-pending:…grantee_denied</c>). It is a deny-list fault: reported, the run red, nothing written, the row
    /// kept live — and the restore made once the inputs read. Twin: the "no-access" row of the hold theory (an ENTRY holds).
    /// </summary>
    [Theory]
    [InlineData("memberships-unreadable")]
    [InlineData("referenced-organizations-unreadable")]
    [InlineData("deny-list-fails-closed")]
    public async Task ARestoreWhoseNoAccessCheckIsUnverifiable_IsADenyListFault_NeverAPolicyHold(string fault)
    {
        var log = CaptureLog();
        var contact = _h.Contact();
        _h.Grants.Seed(Matter, _matter, contact, null, ViewOnly, Today.AddDays(200));
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync(); // raised to Collaborate
        _h.Store.Assign(Matter, _matter, Attorney1, null);
        InjectAbsorbedNoAccessFault(fault);
        var before = _h.TotalWrites;

        var faulted = await Sync();

        AssertDenyListFault(faulted, log, contact);
        _h.TotalWrites.Should().Be(before, "nothing is written while the check cannot be completed");
        _h.Grants.ActiveRowsOf(_matter, contact).Single().AccessLevel.Should().Be(Collaborate);
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Granted, "kept live, so the next pass retries");

        ClearAbsorbedNoAccessFaults();
        var next = await Sync();

        next.Complete.Should().BeTrue();
        next.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Restored);
        _h.Grants.ActiveRowsOf(_matter, contact).Single().AccessLevel.Should().Be(ViewOnly);
    }

    /// <summary>Before r4: <c>Skipped(no-access)</c> with a clean run. Now a fault, <c>no-access-unverifiable</c>.</summary>
    [Theory]
    [InlineData("memberships-unreadable")]
    [InlineData("referenced-organizations-unreadable")]
    [InlineData("deny-list-fails-closed")]
    public async Task AFreshGrantWhoseNoAccessCheckIsUnverifiable_IsADenyListFault_NotNoAccess_AndNothingIsGranted(string fault)
    {
        var log = CaptureLog();
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        InjectAbsorbedNoAccessFault(fault);

        var faulted = await Sync();

        AssertDenyListFault(faulted, log, contact);
        _h.Grants.Rows.Should().BeEmpty("fail closed");
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Skipped);
        row.Reason.Should().Be(AssignedAccessReason.NoAccessUnverifiable, "nobody is known to be on the list");

        ClearAbsorbedNoAccessFaults();
        var next = await Sync();

        next.Complete.Should().BeTrue();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("decided again once the inputs read");
    }

    /// <summary>The materializer's OWN call of the tri-state check, before suggesting on a secure record.</summary>
    [Theory]
    [InlineData("memberships-unreadable")]
    [InlineData("referenced-organizations-unreadable")]
    [InlineData("deny-list-fails-closed")]
    public async Task ASecureSuggestionWhoseNoAccessCheckIsUnverifiable_IsADenyListFault_AndIsNotSuggested(string fault)
    {
        Secure();
        var log = CaptureLog();
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        InjectAbsorbedNoAccessFault(fault);

        var faulted = await Sync();

        AssertDenyListFault(faulted, log, contact);
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Skipped, "never suggested while the check cannot be completed");
        row.Reason.Should().Be(AssignedAccessReason.NoAccessUnverifiable);
        _h.Grants.Rows.Should().BeEmpty();

        ClearAbsorbedNoAccessFaults();
        var next = await Sync();

        next.Complete.Should().BeTrue();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.PendingConfirmation);
    }

    /// <summary>
    /// The secure suggestion's twin: an ENTRY on the list is the record's policy — <c>Skipped(no-access)</c>, a clean run,
    /// no failure (so the tri-state's Denied branch is not read as a fault either).
    /// </summary>
    [Fact]
    public async Task ASecureSuggestionForAContactOnTheList_IsSkippedNoAccess_ACleanRun()
    {
        Secure();
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.DenyList.DenyContactOnRecord(contact, _matter);

        var outcome = await Sync();

        outcome.Complete.Should().BeTrue();
        outcome.Failures.Should().BeEmpty();
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.NoAccess);
    }

    /// <summary>
    /// Task 142 r5 (r4 verifier finding 6): an answer the code does not know — a value outside the enum, which no
    /// production check returns — is never "allowed". On a SECURE record it reaches the materializer's own check (before
    /// suggesting); on a standard record it reaches the grant core's switch (through the fresh-grant path). Either way it
    /// is a deny-list fault: reported, nothing suggested or granted. Twin: the same double answering Allowed suggests /
    /// grants on the next pass.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnUnknownNoAccessAnswer_IsADenyListFault_NeverSuggestedNorGranted(bool secure)
    {
        if (secure)
            Secure();
        var log = CaptureLog();
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        _h.NoAccessCheckOverride = GrantPolicyTestDoubles.DenyListAnswering((NoAccessCheckAnswer)99);

        var faulted = await Sync();

        AssertDenyListFault(faulted, log, contact);
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Skipped, "an unknown answer is never read as allowed");
        row.Reason.Should().Be(AssignedAccessReason.NoAccessUnverifiable);
        _h.Grants.Rows.Should().BeEmpty("fail closed");

        _h.NoAccessCheckOverride = GrantPolicyTestDoubles.DenyListAnswering(NoAccessCheckAnswer.Allowed);
        var next = await Sync();

        next.Complete.Should().BeTrue();
        if (secure)
            LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.PendingConfirmation);
        else
            _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle("granted once the answer is Allowed");
    }

    [Theory]
    [InlineData("memberships-unreadable")]
    [InlineData("referenced-organizations-unreadable")]
    [InlineData("deny-list-fails-closed")]
    public async Task ARenewalWhoseNoAccessCheckIsUnverifiable_IsADenyListFault_AndTheGrantIsKeptAsItIs(string fault)
    {
        var log = CaptureLog();
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync(); // granted, today + 90
        _h.Time.Now = _h.Time.Now.AddDays(70); // 20 days left: inside the 30-day renewal window
        InjectAbsorbedNoAccessFault(fault);

        var faulted = await Sync(trigger: AssignedAccessTrigger.Job);

        AssertDenyListFault(faulted, log, contact);
        faulted.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.None);
        _h.Grants.ActiveRowsOf(_matter, contact).Single().ExpiresDate.Should().Be(Today.AddDays(90), "not renewed this pass");

        ClearAbsorbedNoAccessFaults();
        var next = await Sync(trigger: AssignedAccessTrigger.Job);

        next.Complete.Should().BeTrue();
        next.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Renewed);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Round r4, item 2 (owner round 13 item 5) — task 143's wall guard answering Unverifiable on the internal-user share
    // path FAILS THE RUN like the deny-list fault: counted, logged, the job red
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Three ways the wall guard cannot answer: the deny list, the linked contact's memberships, the record's
    /// referenced organizations — each <see cref="SecureShareWallOutcome.Unverifiable"/>.</summary>
    private void MakeTheWallUnverifiable(string fault)
    {
        switch (fault)
        {
            case "deny-list": _h.DenyList.Faults = true; break;
            case "memberships": _h.Participations.MembershipsUnreadable = true; break;
            case "referenced-organizations": _h.Participations.UnreadableReferencedOrganizations[_matter] = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault), fault, "unknown fault");
        }
    }

    /// <summary>
    /// An auto share gone from a SECURE record (r0 <c>ContinueOursAsync</c>): whether task 143's enforcer removed it (a
    /// known cause) or an operator did (Declined) cannot be told while the wall cannot be read. Nothing is decided — and
    /// the run fails. Once the wall reads (no entry), it is the operator's removal: Declined.
    /// </summary>
    [Theory]
    [InlineData("deny-list")]
    [InlineData("memberships")]
    [InlineData("referenced-organizations")]
    public async Task AnAutoShareGoneOnASecureRecord_WhoseWallIsUnverifiable_FailsTheRun_AndDecidesNothingThisPass(string fault)
    {
        var log = CaptureLog();
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync(); // shared while the record was standard
        Secure();
        _h.Shares.Reset();
        MakeTheWallUnverifiable(fault);
        var before = _h.TotalWrites;

        var faulted = await Sync();

        AssertDenyListFault(faulted, log, contact);
        faulted.Entries.Should().ContainSingle().Which.Reason.Should().Be(AssignedAccessReason.NoAccessUnverifiable);
        _h.TotalWrites.Should().Be(before, "nothing is decided while the wall cannot be read");
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Shared, "never Declined on a guess");

        ClearAbsorbedNoAccessFaults();
        var next = await Sync();

        next.Complete.Should().BeTrue();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.Declined);
        ShareMask(user).Should().BeNull();
    }

    /// <summary>
    /// A fresh share on a SECURE record (A3: suggested) whose wall cannot be read: neither shared nor suggested, and the
    /// run fails. Once it reads, the suggestion is made.
    /// </summary>
    [Theory]
    [InlineData("deny-list")]
    [InlineData("memberships")]
    [InlineData("referenced-organizations")]
    public async Task AFreshShareOnASecureRecord_WhoseWallIsUnverifiable_FailsTheRun_NeitherSharedNorSuggested(string fault)
    {
        Secure();
        var log = CaptureLog();
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        MakeTheWallUnverifiable(fault);

        var faulted = await Sync();

        AssertDenyListFault(faulted, log, contact);
        ShareMask(user).Should().BeNull();
        var row = LedgerRow(contact, Attorney1);
        row.State.Should().Be(AssignedAccessState.Skipped);
        row.Reason.Should().Be(AssignedAccessReason.NoAccessUnverifiable);

        ClearAbsorbedNoAccessFaults();
        var next = await Sync();

        next.Complete.Should().BeTrue();
        LedgerRow(contact, Attorney1).State.Should().Be(AssignedAccessState.PendingConfirmation);
    }

    /// <summary>
    /// Restoring a share task 143's enforcer removed, once the wall is lifted — but the lifted wall cannot be READ: not
    /// restored, the run fails, and the ledger keeps <c>removed-by-no-access</c> so the restore still happens once it
    /// reads. The twin is <see cref="AnAutoShareRemovedByTheNoAccessEnforcer_IsSkippedRemovedByNoAccess_AndRestoredOnceTheWallIsLifted"/>.
    /// </summary>
    [Fact]
    public async Task RestoringAShareTheEnforcerRemoved_WhoseWallIsUnverifiable_FailsTheRun_AndRestoresOnceItReads()
    {
        var log = CaptureLog();
        var (contact, user) = _h.LinkedContact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        await Sync();
        Secure();
        _h.DenyList.DenySystemUserOnRecord(user, _matter);
        _h.Shares.Reset(); // task 143's enforcer removed the walled share
        await Sync();
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.RemovedByNoAccess);

        _h.DenyList = new GrantPolicyTestDoubles.SeamNoAccessListReader { Faults = true }; // lifted — but unreadable
        var faulted = await Sync();

        AssertDenyListFault(faulted, log, contact);
        ShareMask(user).Should().BeNull("never restored on a wall nobody could read");
        LedgerRow(contact, Attorney1).Reason.Should().Be(AssignedAccessReason.RemovedByNoAccess, "the restore is still owed");

        _h.DenyList = new GrantPolicyTestDoubles.SeamNoAccessListReader();
        var next = await Sync();

        next.Complete.Should().BeTrue();
        next.Entries.Should().ContainSingle().Which.Action.Should().Be(AssignedAccessAction.Restored);
        ShareMask(user).Should().Be(CollaborateMask);
    }
}
