using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Task 193 G2b (#2338, owner F1-b: the paired grant follows the share): a SYSTEM path that removes a user's share because
/// they must no longer see the record ends the grant PAIRED with it ("Add person") in the same operation — the No Access
/// enforcer, the Restricted remover and the Assigned-To materializer, each through the one grant revoke /unshare-user
/// uses. The share first, then the grant; a grant that is not ended leaves the share removed and the run NOT complete. The
/// secure-root inheritance's twin is in <see cref="SecureRootInheritanceRound31Tests"/> (it needs that host).
/// </summary>
/// <remarks>
/// The production services run over the module-boundary doubles their own suites use; the paired grant lives in an
/// Assigned-To harness's grant table, and the grant revoke is the production one over it. Placement:
/// <c>tests/integration/data-mutation/**</c> — each path REMOVES access (ADR-038 data-mutation KEEP path).
/// </remarks>
public class PairedGrantFollowsShareRemovalTests
{
    private const string Project = "sprk_project";
    private const string MatterTable = "sprk_matter";
    private const int CollaborateMask = 262167;
    private const string Tenant = "00000000-0000-0000-0000-0000000000cc";

    private static DataversePrincipalRef User(Guid id) => DataversePrincipalRef.User(id);

    /// <summary>The grant world: an Assigned-To harness whose grant table holds the paired grants.</summary>
    private readonly AssignedAccessTestDoubles.Harness _grants = new();

    private Guid PairedGrant(ExternalGrantRootType type, Guid rootId, Guid user, Guid? contact = null)
    {
        var row = _grants.Grants.Seed(type, rootId, contact ?? Guid.NewGuid(), null, (int)ExternalAccessLevel.Collaborate, null);
        _grants.Grants.PairedUsers[row.Id] = user;
        return row.Id;
    }

    private bool Active(Guid rootId, Guid grant) => _grants.Grants.ActiveRowsOf(rootId).Any(r => r.Id == grant);

    // ── The No Access enforcer ──────────────────────────────────────────────────────────────────────────────────────

    private static readonly Guid SecureProject = Guid.Parse("19300000-0000-4000-8000-0000000000a1");
    private static readonly Guid Walled = Guid.Parse("19300000-0000-4000-8000-0000000000b1");
    private static readonly Guid Colleague = Guid.Parse("19300000-0000-4000-8000-0000000000b2");
    private static readonly Guid Author = Guid.Parse("19300000-0000-4000-8000-0000000000b3");
    private static readonly Guid WalledContact = Guid.Parse("19300000-0000-4000-8000-0000000000c1");

    private NoAccessEnforcementTestDoubles.Harness NoAccess(bool someoneElseKeepsIt = true)
    {
        var h = new NoAccessEnforcementTestDoubles.Harness();
        h.Participations.Flags[SecureProject] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        h.Store.Person(Author);
        h.Store.Person(Walled);
        h.Store.Person(Colleague);
        h.Store.Rights[(Author, SecureProject)] = AccessRights.Read | AccessRights.Write;
        // Someone else keeps the secure project, so S5 does not keep the walled user's share.
        if (someoneElseKeepsIt)
            h.Shares.Seed(Project, SecureProject, User(Colleague), CollaborateMask);
        h.PairedGrants = _grants.PairedGrantRevokeFor(Project, SecureProject);
        return h;
    }

    [Fact]
    public async Task NoAccess_RemovesTheWalledUsersShare_ThenEndsTheGrantPairedWithIt()
    {
        var h = NoAccess();
        h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var paired = PairedGrant(ExternalGrantRootType.Project, SecureProject, Walled);
        var other = PairedGrant(ExternalGrantRootType.Project, SecureProject, Colleague);
        var entry = h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await h.Enforcer.EnforceEntryAsync(entry, new[] { Tenant }, CancellationToken.None);

        report.Complete.Should().BeTrue();
        h.Shares.MaskOf(Project, SecureProject, User(Walled)).Should().BeNull("the share goes first");
        Active(SecureProject, paired).Should().BeFalse("the paired grant follows the share");
        Active(SecureProject, other).Should().BeTrue("a grant paired with someone else is untouched");
    }

    /// <summary>The entry names the CONTACT: the user it represents is covered, so their pairing ends too ("either way").</summary>
    [Fact]
    public async Task NoAccess_AnEntryNamingTheContact_EndsThePairingOfTheUserItRepresents()
    {
        var h = NoAccess();
        h.Identities.AddContact(WalledContact);
        h.Identities.AddSystemUser(Walled, oid: null, email: null, primaryContactId: WalledContact);
        h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var paired = PairedGrant(ExternalGrantRootType.Project, SecureProject, Walled, WalledContact);
        var entry = h.Store.AddEntry(subjectContact: WalledContact, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await h.Enforcer.EnforceEntryAsync(entry, new[] { Tenant }, CancellationToken.None);

        report.Complete.Should().BeTrue();
        Active(SecureProject, paired).Should().BeFalse();
    }

    /// <summary>The walled user holds no share any more (removed earlier), but their paired grant is still active: it ends.</summary>
    [Fact]
    public async Task NoAccess_WhenTheWalledUserHoldsNoShare_StillEndsTheirPairedGrant()
    {
        var h = NoAccess();
        var paired = PairedGrant(ExternalGrantRootType.Project, SecureProject, Walled);
        var entry = h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await h.Enforcer.EnforceEntryAsync(entry, new[] { Tenant }, CancellationToken.None);

        report.Complete.Should().BeTrue();
        Active(SecureProject, paired).Should().BeFalse();
    }

    [Fact]
    public async Task NoAccess_WhenThePairedGrantCannotBeEnded_TheShareStaysRemoved_AndTheEntryIsNotComplete()
    {
        var h = NoAccess();
        h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var paired = PairedGrant(ExternalGrantRootType.Project, SecureProject, Walled);
        _grants.Grants.FailDeactivations = true;
        var entry = h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await h.Enforcer.EnforceEntryAsync(entry, new[] { Tenant }, CancellationToken.None);

        report.Complete.Should().BeFalse("never reported fully enforced while the paired grant may still be active");
        report.Failures.Should().ContainSingle(f => f.Kind == NoAccessShareEnforcer.PairedGrantNotEndedKind)
            .Which.SystemUserId.Should().Be(Walled);
        report.Removed.Should().ContainSingle(r => r.SystemUserId == Walled, "the share removal stands");
        h.Shares.MaskOf(Project, SecureProject, User(Walled)).Should().BeNull();
        Active(SecureProject, paired).Should().BeTrue("left for the reconciliation job's R5");
    }

    [Fact]
    public async Task NoAccess_WhenThePairingColumnIsNotProvisioned_RemovesTheShareOnly_AsBefore()
    {
        var h = NoAccess();
        h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _grants.Grants.PairingColumnMissing = true;
        var entry = h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await h.Enforcer.EnforceEntryAsync(entry, new[] { Tenant }, CancellationToken.None);

        report.Complete.Should().BeTrue();
        report.Removed.Should().ContainSingle(r => r.SystemUserId == Walled);
    }

    /// <summary>S5 keeps the walled user's share (the last reader of a secure record): their paired grant is kept too.</summary>
    [Fact]
    public async Task NoAccess_WhenS5KeepsTheShare_ThePairedGrantIsKept()
    {
        var h = NoAccess(someoneElseKeepsIt: false);
        h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var paired = PairedGrant(ExternalGrantRootType.Project, SecureProject, Walled);
        var entry = h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        await h.Enforcer.EnforceEntryAsync(entry, new[] { Tenant }, CancellationToken.None);

        h.Shares.MaskOf(Project, SecureProject, User(Walled)).Should().Be(CollaborateMask, "S5: the record's last reader");
        Active(SecureProject, paired).Should().BeTrue("the share was not removed, so neither is its grant");
    }

    /// <summary>The paired contact's Assigned-To suggestion on the record (a pending row), as task 142 records it.</summary>
    private AssignedAccessLedgerRow Suggestion(ExternalGrantRootType type, Guid rootId, Guid contact) =>
        _grants.Store.SeedRow(type, rootId, Attorney1, new AssignedSubject(AssignedSubjectKind.Contact, contact),
            AssignedAccessState.PendingConfirmation);

    private AssignedAccessState StateOf(AssignedAccessLedgerRow row) =>
        _grants.Store.Ledger.Single(r => r.Id == row.Id).State;

    /// <summary>
    /// Task 193 G4a (A-F2, owner ruling 2026-10-28): the No Access enforcer's end of the paired grant is a SYSTEM removal.
    /// The contact's Assigned-To suggestion on the record stays as it was; only an operator's manual removal is a decline.
    /// </summary>
    [Fact]
    public async Task G4a_NoAccess_EndingThePairedGrant_LeavesTheContactsAssignedToSuggestionAlone()
    {
        var h = NoAccess();
        h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var paired = PairedGrant(ExternalGrantRootType.Project, SecureProject, Walled, WalledContact);
        var suggestion = Suggestion(ExternalGrantRootType.Project, SecureProject, WalledContact);
        var entry = h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await h.Enforcer.EnforceEntryAsync(entry, new[] { Tenant }, CancellationToken.None);

        report.Complete.Should().BeTrue();
        Active(SecureProject, paired).Should().BeFalse("the paired grant follows the share");
        StateOf(suggestion).Should().Be(AssignedAccessState.PendingConfirmation, "a system removal records no operator decline");
    }

    /// <summary>
    /// Task 193 G3a: an organization entry walls three users off three secure records (nine pairs) on a host without the
    /// pairing column. The column is checked once and ONE Warning is logged for the pass; the shares are all removed.
    /// </summary>
    [Fact]
    public async Task NoAccess_ManyUsersOnManyRecords_WithoutTheColumn_CheckTheColumnOnce_AndLogOneWarning()
    {
        var (h, users, records) = WalledFirm();
        var log = new CapturingLogger();
        h.PairedGrants = _grants.PairedGrantRevokeFor(Project, records[0], log);
        _grants.Grants.PairingColumnMissing = true;

        var report = await h.Enforcer.EnforceEntryAsync(
            h.Store.AddEntry(subjectOrganization: Firm, objectOrganization: Firm, modifiedBy: Author), new[] { Tenant },
            CancellationToken.None);

        report.Complete.Should().BeTrue();
        report.Removed.Should().HaveCount(users.Count * records.Count);
        log.Warnings.Should().Be(1, "one Warning per pass, not one per user per record");
        _grants.Grants.GrantQueryTops.Count(t => t == PairingReadTop).Should().Be(1, "the column is checked once");
    }

    /// <summary>The positive twin: with the column, every (record, user) pair is read for its pairing, and nothing is warned.</summary>
    [Fact]
    public async Task NoAccess_ManyUsersOnManyRecords_WithTheColumn_ReadEachPairsPairing_AndWarnNothing()
    {
        var (h, users, records) = WalledFirm();
        var log = new CapturingLogger();
        h.PairedGrants = _grants.PairedGrantRevokeFor(Project, records[0], log);

        var report = await h.Enforcer.EnforceEntryAsync(
            h.Store.AddEntry(subjectOrganization: Firm, objectOrganization: Firm, modifiedBy: Author), new[] { Tenant },
            CancellationToken.None);

        report.Complete.Should().BeTrue();
        log.Warnings.Should().Be(0);
        _grants.Grants.GrantQueryTops.Count(t => t == PairingReadTop).Should().Be(users.Count * records.Count);
    }

    private static readonly Guid Firm = Guid.Parse("19300000-0000-4000-8000-0000000000f1");

    /// <summary>The pairing read's page size (the bound plus one).</summary>
    private const int PairingReadTop = Sprk.Bff.Api.Api.ExternalAccess.ExternalGrantRevocationService.MaxPairedGrantsPerShare + 1;

    /// <summary>A firm of three members, each linked to a user holding a share on each of three secure records it is on.</summary>
    private static (NoAccessEnforcementTestDoubles.Harness H, IReadOnlyList<Guid> Users, IReadOnlyList<Guid> Records) WalledFirm()
    {
        var h = new NoAccessEnforcementTestDoubles.Harness();
        h.Store.Person(Author);
        h.Store.Person(Colleague);
        var records = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var contacts = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var users = new List<Guid>();
        foreach (var contact in contacts)
        {
            var user = Guid.NewGuid();
            h.Store.Person(user);
            h.Identities.AddContact(contact);
            h.Identities.AddSystemUser(user, oid: null, email: null, primaryContactId: contact);
            users.Add(user);
        }

        h.Participations.OrganizationMembers[Firm] = contacts.ToArray();
        foreach (var record in records)
        {
            h.Participations.Flags[record] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
            h.Participations.RecordOrganizations[record] = new[] { Firm };
            h.Store.Rights[(Author, record)] = AccessRights.Read | AccessRights.Write;
            h.Shares.Seed(Project, record, User(Colleague), CollaborateMask);
            foreach (var user in users)
                h.Shares.Seed(Project, record, User(user), CollaborateMask);
        }

        return (h, users, records);
    }

    /// <summary>Counts the Warnings a logger was given.</summary>
    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public int Warnings { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                Warnings++;
        }
    }

    // ── The Restricted remover ──────────────────────────────────────────────────────────────────────────────────────

    private readonly Guid _matter = Guid.NewGuid();

    private Task<RestrictedExternalShareReport> RemoveRestricted()
    {
        _grants.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        _grants.PairedGrants = _grants.PairedGrantRevokeFor(MatterTable, _matter);
        return _grants.RestrictedRemover.RemoveForRecordAsync(
            ExternalGrantRootType.Matter, _matter, new[] { Tenant }, CancellationToken.None);
    }

    [Fact]
    public async Task Restricted_RemovesTheExternalUsersShare_ThenEndsTheGrantPairedWithIt()
    {
        var external = _grants.SystemUser(isExternal: true);
        _grants.Shares.Seed(MatterTable, _matter, User(external), CollaborateMask);
        var paired = PairedGrant(ExternalGrantRootType.Matter, _matter, external);

        var report = await RemoveRestricted();

        report.Complete.Should().BeTrue();
        report.Removed.Should().Equal(external);
        Active(_matter, paired).Should().BeFalse("the paired grant follows the share");
    }

    [Fact]
    public async Task Restricted_WhenThePairedGrantCannotBeEnded_TheShareStaysRemoved_AndThePassIsNotComplete()
    {
        var external = _grants.SystemUser(isExternal: true);
        _grants.Shares.Seed(MatterTable, _matter, User(external), CollaborateMask);
        var paired = PairedGrant(ExternalGrantRootType.Matter, _matter, external);
        _grants.Grants.FailDeactivations = true;

        var report = await RemoveRestricted();

        report.Complete.Should().BeFalse();
        report.Failures.Should().ContainSingle(f => f.Kind == RestrictedExternalShareRemover.PairedGrantNotEndedKind)
            .Which.SystemUserId.Should().Be(external);
        report.Removed.Should().Equal(external);
        _grants.Shares.MaskOf(MatterTable, _matter, User(external)).Should().BeNull("the share removal stands");
        Active(_matter, paired).Should().BeTrue("left for the reconciliation job's R5");
    }

    /// <summary>
    /// Task 193 G4a (A-F1): a pass whose paired-grant end failed left the share removed; the NEXT pass finds the barred user
    /// holding no share and ends the grant still paired with them (as the No Access enforcer does), instead of never again.
    /// </summary>
    [Fact]
    public async Task G4a_Restricted_WhenThePairedGrantCouldNotBeEnded_TheNextPassEndsIt()
    {
        var external = _grants.SystemUser(isExternal: true);
        _grants.Shares.Seed(MatterTable, _matter, User(external), CollaborateMask);
        var paired = PairedGrant(ExternalGrantRootType.Matter, _matter, external);
        _grants.Grants.FailDeactivations = true;
        (await RemoveRestricted()).Complete.Should().BeFalse();
        _grants.Grants.FailDeactivations = false;

        var next = await RemoveRestricted();

        next.Complete.Should().BeTrue();
        next.Removed.Should().BeEmpty("the share was already removed");
        Active(_matter, paired).Should().BeFalse("the barred user holds no share, so the grant paired with them ends");
    }

    /// <summary>The must-not-fire twin: an INTERNAL user's paired grant, with no share, is not the Restricted rule's (R5's).</summary>
    [Fact]
    public async Task G4a_Restricted_LeavesThePairedGrantOfAnInternalUserWithNoShare()
    {
        var internalUser = _grants.SystemUser(isExternal: false);
        var paired = PairedGrant(ExternalGrantRootType.Matter, _matter, internalUser);

        var report = await RemoveRestricted();

        report.Complete.Should().BeTrue();
        Active(_matter, paired).Should().BeTrue("only a user barred from the Restricted record has their pairing ended here");
    }

    /// <summary>Task 193 G4a (A-F1): the pairing read failing is reported, never read as "nobody paired".</summary>
    [Fact]
    public async Task G4a_Restricted_WhenThePairingCannotBeRead_ThePassIsNotComplete()
    {
        _grants.SystemUser(isExternal: true);
        _grants.Grants.FailPairingReads = true;

        var report = await RemoveRestricted();

        report.Complete.Should().BeFalse();
        report.Failures.Should().ContainSingle(f => f.Kind == RestrictedExternalShareRemover.PairedGrantNotEndedKind);
    }

    /// <summary>Task 193 G4a (A-F2): the Restricted remover's end of the paired grant records no operator decline.</summary>
    [Fact]
    public async Task G4a_Restricted_EndingThePairedGrant_LeavesTheContactsAssignedToSuggestionAlone()
    {
        var external = _grants.SystemUser(isExternal: true);
        var contact = Guid.NewGuid();
        _grants.Shares.Seed(MatterTable, _matter, User(external), CollaborateMask);
        var paired = PairedGrant(ExternalGrantRootType.Matter, _matter, external, contact);
        var suggestion = Suggestion(ExternalGrantRootType.Matter, _matter, contact);

        var report = await RemoveRestricted();

        report.Complete.Should().BeTrue();
        Active(_matter, paired).Should().BeFalse();
        StateOf(suggestion).Should().Be(AssignedAccessState.PendingConfirmation, "a system removal records no operator decline");
    }

    // ── The Assigned-To materializer ────────────────────────────────────────────────────────────────────────────────

    private const string Attorney1 = "sprk_assignedattorney1";
    private const string Paralegal1 = "sprk_assignedparalegal1";

    /// <summary>A linked contact assigned, so the materializer shares the root with the user; then "Add person" pairs a grant.</summary>
    private async Task<(Guid Contact, Guid User, Guid Grant)> AutoSharedThenPaired()
    {
        _grants.HostResolvingPairedGrantRevoke(MatterTable, _matter);
        var (contact, user) = _grants.LinkedContact();
        _grants.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, contact);
        (await _grants.SyncAsync(ExternalGrantRootType.Matter, _matter)).Complete.Should().BeTrue();
        _grants.Shares.MaskOf(MatterTable, _matter, User(user)).Should().NotBeNull();
        return (contact, user, PairedGrant(ExternalGrantRootType.Matter, _matter, user, contact));
    }

    [Fact]
    public async Task Materializer_ClearingTheField_RemovesTheShare_ThenEndsTheGrantPairedWithIt()
    {
        var (_, user, paired) = await AutoSharedThenPaired();

        _grants.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, null);
        var outcome = await _grants.SyncAsync(ExternalGrantRootType.Matter, _matter);

        outcome.Complete.Should().BeTrue();
        _grants.Shares.MaskOf(MatterTable, _matter, User(user)).Should().BeNull();
        Active(_matter, paired).Should().BeFalse("the paired grant follows the share");
    }

    [Fact]
    public async Task Materializer_WhenThePairedGrantCannotBeEnded_TheShareStaysRemoved_AndTheRunIsNotComplete()
    {
        var (_, user, paired) = await AutoSharedThenPaired();
        _grants.Grants.FailDeactivations = true;

        _grants.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, null);
        var outcome = await _grants.SyncAsync(ExternalGrantRootType.Matter, _matter);

        outcome.Complete.Should().BeFalse();
        outcome.Failures.Should().ContainSingle(f => f.Kind == AssignedAccessMaterializer.PairedGrantNotEndedKind);
        _grants.Shares.MaskOf(MatterTable, _matter, User(user)).Should().BeNull("the share removal stands");
        Active(_matter, paired).Should().BeTrue("left for the reconciliation job's R5");
    }

    /// <summary>
    /// Task 193 G3a: two auto shares removed in one pass on a host without the pairing column. The materializer resolves the
    /// revoke from its OWN scope, so the column is checked once and ONE Warning is logged, not one per removed share.
    /// </summary>
    [Fact]
    public async Task Materializer_TwoSharesRemovedInOnePass_WithoutTheColumn_CheckTheColumnOnce_AndLogOneWarning()
    {
        var log = new CapturingLogger();
        _grants.HostResolvingPairedGrantRevoke(MatterTable, _matter, log);
        var (first, firstUser) = _grants.LinkedContact();
        var (second, secondUser) = _grants.LinkedContact();
        _grants.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, first);
        _grants.Store.Assign(ExternalGrantRootType.Matter, _matter, Paralegal1, second);
        (await _grants.SyncAsync(ExternalGrantRootType.Matter, _matter)).Complete.Should().BeTrue();
        _grants.Grants.PairingColumnMissing = true;

        _grants.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, null);
        _grants.Store.Assign(ExternalGrantRootType.Matter, _matter, Paralegal1, null);
        var outcome = await _grants.SyncAsync(ExternalGrantRootType.Matter, _matter);

        outcome.Complete.Should().BeTrue("a missing column ends nothing and is not a failure");
        _grants.Shares.MaskOf(MatterTable, _matter, User(firstUser)).Should().BeNull();
        _grants.Shares.MaskOf(MatterTable, _matter, User(secondUser)).Should().BeNull();
        log.Warnings.Should().Be(1);
        _grants.Grants.GrantQueryTops.Count(t => t == PairingReadTop).Should().Be(1, "the column is checked once");
    }

    /// <summary>Re-assigning keeps the share, so nothing is removed and the paired grant stays.</summary>
    [Fact]
    public async Task Materializer_WhenTheShareIsKept_ThePairedGrantIsKept()
    {
        var (_, user, paired) = await AutoSharedThenPaired();

        var outcome = await _grants.SyncAsync(ExternalGrantRootType.Matter, _matter);

        outcome.Complete.Should().BeTrue();
        _grants.Shares.MaskOf(MatterTable, _matter, User(user)).Should().Be(CollaborateMask);
        Active(_matter, paired).Should().BeTrue();
    }

    /// <summary>
    /// Task 193 G4a (A-F1): the pass that removed the share could not end its paired grant, so the ledger row stays live; the
    /// NEXT pass finds the share gone, ends the grant, then the row.
    /// </summary>
    [Fact]
    public async Task G4a_Materializer_WhenThePairedGrantCouldNotBeEnded_TheNextPassEndsIt()
    {
        var (contact, user, paired) = await AutoSharedThenPaired();
        _grants.Grants.FailDeactivations = true;
        _grants.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, null);
        (await _grants.SyncAsync(ExternalGrantRootType.Matter, _matter)).Complete.Should().BeFalse();
        AssignedRow(contact).State.Should().Be(AssignedAccessState.Shared, "the row stays live while the paired grant remains");
        _grants.Grants.FailDeactivations = false;

        var next = await _grants.SyncAsync(ExternalGrantRootType.Matter, _matter);

        next.Complete.Should().BeTrue();
        _grants.Shares.MaskOf(MatterTable, _matter, User(user)).Should().BeNull();
        Active(_matter, paired).Should().BeFalse("the next pass ends the grant paired with the removed share");
        AssignedRow(contact).State.Should().Be(AssignedAccessState.Revoked);
    }

    /// <summary>Task 193 G4a: a report-only job pass ends nothing; while a paired grant remains, the row stays live for a revoking pass.</summary>
    [Fact]
    public async Task G4a_Materializer_AReportOnlyPass_EndsNothing_AndKeepsTheRowLive_WhileAPairedGrantRemains()
    {
        var (contact, _, paired) = await AutoSharedThenPaired();
        _grants.Grants.FailDeactivations = true;
        _grants.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, null);
        await _grants.SyncAsync(ExternalGrantRootType.Matter, _matter);
        _grants.Grants.FailDeactivations = false;

        await _grants.SyncAsync(ExternalGrantRootType.Matter, _matter, revokeOnChange: false);

        Active(_matter, paired).Should().BeTrue("a report-only pass ends nothing");
        AssignedRow(contact).State.Should().Be(AssignedAccessState.Shared, "kept live for the next revoking pass");

        (await _grants.SyncAsync(ExternalGrantRootType.Matter, _matter)).Complete.Should().BeTrue();
        Active(_matter, paired).Should().BeFalse();
    }

    /// <summary>
    /// Task 193 G4a (A-F2): the materializer's end of a paired grant is a SYSTEM removal — the grant's contact keeps its own
    /// Assigned-To row on the record as it was (never Declined).
    /// </summary>
    [Fact]
    public async Task G4a_Materializer_EndingThePairedGrant_LeavesTheGrantContactsAssignedToRowUndeclined()
    {
        _grants.HostResolvingPairedGrantRevoke(MatterTable, _matter);
        var (linked, user) = _grants.LinkedContact();
        var other = _grants.Contact();
        _grants.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, linked);
        _grants.Store.Assign(ExternalGrantRootType.Matter, _matter, Paralegal1, other);
        (await _grants.SyncAsync(ExternalGrantRootType.Matter, _matter)).Complete.Should().BeTrue();
        var otherGrant = _grants.Grants.ActiveRowsOf(_matter, other).Single();
        _grants.Grants.PairedUsers[otherGrant.Id] = user; // the grant on that contact is paired with the user's share

        _grants.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, null);
        await _grants.SyncAsync(ExternalGrantRootType.Matter, _matter);

        Active(_matter, otherGrant.Id).Should().BeFalse("the paired grant follows the share");
        _grants.Store.RowsOf(_matter, other).Single(r => r.SourceField == Paralegal1).State
            .Should().NotBe(AssignedAccessState.Declined, "a system removal records no operator decline");
    }

    private AssignedAccessLedgerRow AssignedRow(Guid contact) =>
        _grants.Store.RowsOf(_matter, contact).Single(r => r.SourceField == Attorney1);
}
