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

    // ── The Assigned-To materializer ────────────────────────────────────────────────────────────────────────────────

    private const string Attorney1 = "sprk_assignedattorney1";

    /// <summary>A linked contact assigned, so the materializer shares the root with the user; then "Add person" pairs a grant.</summary>
    private async Task<(Guid Contact, Guid User, Guid Grant)> AutoSharedThenPaired()
    {
        _grants.ScopesResolvingPairedGrantRevoke(MatterTable, _matter);
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
}
