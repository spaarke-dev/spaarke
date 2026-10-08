using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.NoAccessEnforcementTestDoubles;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 143 — the No Access ENFORCER: what it removes, what it never removes, and when it
/// refuses to act (owner Q4, N2, N4, N5, S5). The production <see cref="NoAccessShareEnforcer"/> runs over module-boundary
/// doubles (<see cref="NoAccessEnforcementTestDoubles"/>); the POA share table is the strict one the share routes use
/// (a revoke of nothing throws, a read-back sees what the writes did).
/// </summary>
/// <remarks>
/// Placement: <c>tests/integration/data-mutation/**</c> — the enforcer REMOVES access, the ADR-038 data-mutation KEEP
/// path. Masks are Dataverse literals (Read 1, Write 2, Append 4, AppendTo 16, Share 262144).
/// </remarks>
public class NoAccessShareEnforcerTests
{
    private const string Project = "sprk_project";
    private const int CollaborateMask = 262167;
    private const string Tenant = "00000000-0000-0000-0000-0000000000cc";

    private static readonly Guid SecureProject = Guid.Parse("14314314-3143-1431-4314-314314314301");
    private static readonly Guid OpenProject = Guid.Parse("14314314-3143-1431-4314-314314314302");
    private static readonly Guid Walled = Guid.Parse("14314314-3143-1431-4314-3143143143a1");
    private static readonly Guid Colleague = Guid.Parse("14314314-3143-1431-4314-3143143143a2");
    private static readonly Guid Author = Guid.Parse("14314314-3143-1431-4314-3143143143a3");
    private static readonly Guid Team = Guid.Parse("14314314-3143-1431-4314-3143143143b1");
    private static readonly Guid Firm = Guid.Parse("14314314-3143-1431-4314-3143143143c1");
    private static readonly Guid LinkedContact = Guid.Parse("14314314-3143-1431-4314-3143143143d1");

    private readonly Harness _h = new();

    public NoAccessShareEnforcerTests()
    {
        _h.Participations.Flags[SecureProject] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        _h.Participations.Flags[OpenProject] = RootRecordFlags.None;
        _h.Store.Person(Author);
        _h.Store.Person(Walled);
        _h.Store.Person(Colleague);
        _h.Store.Rights[(Author, SecureProject)] = AccessRights.Read | AccessRights.Write;
        _h.Store.Rights[(Author, OpenProject)] = AccessRights.Read | AccessRights.Write;

        // Someone else keeps the secure project, so S5 does not stop the removal unless a test removes them.
        _h.Shares.Seed(Project, SecureProject, DataversePrincipalRef.User(Colleague), CollaborateMask);
    }

    private static DataversePrincipalRef User(Guid id) => DataversePrincipalRef.User(id);

    private Task<NoAccessEnforcementReport> Enforce(Guid entryId) =>
        _h.Enforcer.EnforceEntryAsync(entryId, new[] { Tenant }, CancellationToken.None);

    // ── Criterion 6: the share is removed, confirmed gone, and the cache cleared ─────────────────────────────

    [Fact]
    public async Task Enforce_ASystemUserEntry_RevokesTheDirectShare_ConfirmsItGone_AndClearsTheUsersCacheKey()
    {
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Complete.Should().BeTrue();
        report.Removed.Should().ContainSingle().Which.Should().Be(
            new NoAccessRemovedShare(Walled, Project, SecureProject, CollaborateMask));
        _h.Shares.MaskOf(Project, SecureProject, User(Walled)).Should().BeNull("the direct share is gone, by read-back");
        _h.Shares.MaskOf(Project, SecureProject, User(Colleague)).Should().Be(CollaborateMask, "nobody else's share is touched");
        _h.Cache.Removed.Should().Contain((Tenant, ImpersonatedRootSetSource.CacheResource, $"{Walled:D}:{Project}"),
            "the walled user's impersonated root set is cleared under the key that user's own reads write");
    }

    [Fact]
    public async Task Enforce_WhenTheReadBackStillFindsTheShare_IsAFailureNamingTheUserAndRecord_NeverRemoved()
    {
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Shares.IgnoreWrites = true; // Dataverse accepts the revoke and keeps the share
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Removed.Should().BeEmpty("a share still there is never reported removed");
        report.Complete.Should().BeFalse();
        report.Failures.Should().ContainSingle(f => f.Kind == "revoke-not-confirmed").Which.Message
            .Should().Contain(Walled.ToString()).And.Contain(SecureProject.ToString());
        _h.Cache.Removed.Should().NotBeEmpty("the cache is cleared even when the revoke is not confirmed");
    }

    // ── Criterion 7 / owner N2: team, team ownership and role access are reported, never revoked ─────────────

    [Fact]
    public async Task Enforce_AccessThroughATeamShare_IsReportedNotEnforceable_AndTheTeamShareIsLeftAlone()
    {
        _h.Shares.Seed(Project, SecureProject, DataversePrincipalRef.Team(Team), CollaborateMask);
        _h.Store.TeamMembers[Walled] = new HashSet<Guid> { Team };
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.NotEnforceable.Should().ContainSingle().Which.Should().Be(
            new NoAccessNotEnforceable(Walled, Project, SecureProject, NoAccessEnforcementReason.TeamShare, Team));
        _h.Shares.MaskOf(Project, SecureProject, DataversePrincipalRef.Team(Team)).Should().Be(CollaborateMask,
            "a team share is never revoked — it would strip every other member");
        _h.Shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enforce_AccessThroughTeamOwnership_IsReportedAsTeamOwnership()
    {
        _h.Store.OwningTeams[SecureProject] = Team;
        _h.Store.TeamMembers[Walled] = new HashSet<Guid> { Team };
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.NotEnforceable.Should().ContainSingle(n => n.Mechanism == NoAccessEnforcementReason.TeamOwnership && n.TeamId == Team);
        _h.Shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enforce_AccessDataverseStillConfersWithNoShareOrTeam_IsReportedAsRoleOrBusinessUnit()
    {
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Store.Rights[(Walled, SecureProject)] = AccessRights.Read; // a role, its depth, or the business unit
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Removed.Should().ContainSingle("the direct share is still removed");
        report.NotEnforceable.Should().ContainSingle().Which.Mechanism.Should().Be(NoAccessEnforcementReason.RoleOrBusinessUnit);
    }

    // ── Owner N5 (criterion 8a): the entry's author must hold Write on the record ─────────────────────────

    [Fact]
    public async Task Enforce_WhenTheAuthorLacksWriteOnTheRecord_RemovesNothingThere_AndSaysSo()
    {
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Store.Rights[(Author, SecureProject)] = AccessRights.Read;
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Removed.Should().BeEmpty();
        report.NotEnforced.Should().ContainSingle().Which.Should().Be(
            new NoAccessNotEnforced(Project, SecureProject, null, NoAccessEnforcementReason.AuthorLacksWrite));
        _h.Shares.Writes.Should().BeEmpty("an author without Write cannot use an entry to remove access");
        _h.Store.RightsReads.Should().Contain((Author, SecureProject), "the AUTHOR's rights were read, for that record");
    }

    [Fact]
    public async Task Enforce_WhenTheAuthorIsAnApplicationUser_RemovesNothing()
    {
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Store.Person(Author, applicationId: Guid.NewGuid());
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.NotEnforced.Should().ContainSingle(n => n.Reason == NoAccessEnforcementReason.AuthorNotAPerson);
        _h.Shares.Writes.Should().BeEmpty();
    }

    // ── Owner S5: never the last person who can see a secure record ───────────────────────────────────────

    [Fact]
    public async Task Enforce_WhenTheWalledUserIsTheLastPersonWhoCanOpenTheSecureRecord_KeepsTheShare()
    {
        _h.Shares.Reset(); // the colleague's share is gone: the walled user is the only reader
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Removed.Should().BeEmpty();
        report.NotEnforced.Should().ContainSingle().Which.Should().Be(
            new NoAccessNotEnforced(Project, SecureProject, Walled, NoAccessEnforcementReason.LastPersonOnSecureRecord));
        _h.Shares.MaskOf(Project, SecureProject, User(Walled)).Should().Be(CollaborateMask);
    }

    [Fact]
    public async Task Enforce_AnotherReaderWhoIsDisabled_DoesNotCountAsSomeoneWhoKeepsAccess()
    {
        _h.Store.Person(Colleague, disabled: true);
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.NotEnforced.Should().ContainSingle(n => n.Reason == NoAccessEnforcementReason.LastPersonOnSecureRecord);
        _h.Shares.Writes.Should().BeEmpty();
    }

    // ── Q4 scope: non-secure records and non-root objects ─────────────────────────────────────────────────

    [Fact]
    public async Task Enforce_OnANonSecureRecord_RemovesNothing()
    {
        _h.Shares.Seed(Project, OpenProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, OpenProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.NotEnforced.Should().ContainSingle().Which.Reason.Should().Be(NoAccessEnforcementReason.NotSecure);
        _h.Shares.MaskOf(Project, OpenProject, User(Walled)).Should().Be(CollaborateMask);
    }

    // ── Subject and object forms (criteria 3, 12) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Enforce_AContactSubject_ReachesTheSystemUserItRepresents()
    {
        _h.Identities.AddContact(LinkedContact);
        _h.Identities.AddSystemUser(Walled, oid: null, email: null, primaryContactId: LinkedContact);
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectContact: LinkedContact, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Removed.Should().ContainSingle(r => r.SystemUserId == Walled);
    }

    [Fact]
    public async Task Enforce_AnOrganizationSubject_ReachesEveryLinkedMember_AndAnOrganizationObjectCoversEverySecureRecordReferencingIt()
    {
        var otherSecure = Guid.NewGuid();
        _h.Participations.Flags[otherSecure] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        _h.Participations.RecordOrganizations[SecureProject] = new[] { Firm };
        _h.Participations.RecordOrganizations[otherSecure] = new[] { Firm };
        _h.Participations.RecordOrganizations[OpenProject] = new[] { Firm }; // referenced, but not secure
        _h.Participations.OrganizationMembers[Firm] = new[] { LinkedContact };
        _h.Identities.AddContact(LinkedContact);
        _h.Identities.AddSystemUser(Walled, oid: null, email: null, primaryContactId: LinkedContact);
        _h.Store.Rights[(Author, otherSecure)] = AccessRights.Write;
        _h.Shares.Seed(Project, otherSecure, User(Colleague), CollaborateMask);
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Shares.Seed(Project, otherSecure, User(Walled), CollaborateMask);
        _h.Shares.Seed(Project, OpenProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectOrganization: Firm, objectOrganization: Firm, modifiedBy: Author);

        var report = await Enforce(entry);

        report.Removed.Select(r => r.RecordId).Should().BeEquivalentTo(new[] { SecureProject, otherSecure });
        _h.Shares.MaskOf(Project, OpenProject, User(Walled)).Should().Be(CollaborateMask,
            "a non-secure record that references the organization is not the internal wall's (Q4)");
        report.SubjectKind.Should().Be("organization");
        report.CoveredUsers.Should().Be(1, "the blast radius of an organization subject is reported");
    }

    [Fact]
    public async Task Enforce_IsRecomputedEachCall_SoAShareMadeAfterTheEntryIsRemovedNextTime()
    {
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);
        (await Enforce(entry)).Removed.Should().BeEmpty("nothing to remove yet");

        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask); // out-of-band MDA share, after the entry

        (await Enforce(entry)).Removed.Should().ContainSingle(r => r.SystemUserId == Walled);
    }

    [Fact]
    public async Task EnforceForRecord_ReappliesEveryEntryCoveringTheRecord_ByIdOrByAReferencedOrganization()
    {
        // Owner R3: the task-142 "Update Access" command re-applies No Access for one record.
        var firm = Guid.NewGuid();
        _h.Participations.RecordOrganizations[SecureProject] = new[] { firm };
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Shares.Seed(Project, SecureProject, User(Author), CollaborateMask);
        _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);
        _h.Store.AddEntry(subjectUser: Author, objectOrganization: firm, modifiedBy: Author);
        _h.Store.AddEntry(subjectUser: Colleague, objectRecord: (Project, OpenProject), modifiedBy: Author); // another record

        var reports = await _h.Enforcer.EnforceForRecordAsync(Project, SecureProject, new[] { Tenant }, CancellationToken.None);

        reports.Should().HaveCount(2, "only the two entries covering this record are re-applied");
        reports.SelectMany(r => r.Removed).Select(r => r.SystemUserId).Should().BeEquivalentTo(new[] { Walled, Author });
        _h.Shares.MaskOf(Project, SecureProject, User(Colleague)).Should().Be(CollaborateMask);
    }

    [Fact]
    public async Task EnforceForRecord_WhenTheRecordsOrganizationsCannotBeRead_FailsAndRemovesNothing()
    {
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);
        _h.Participations.ReferencedOrganizationsThrow = new HttpRequestException("Dataverse 503");

        var reports = await _h.Enforcer.EnforceForRecordAsync(Project, SecureProject, new[] { Tenant }, CancellationToken.None);

        reports.Should().ContainSingle().Which.Outcome.Should().Be(NoAccessEnforcementOutcome.Failed);
        _h.Shares.Writes.Should().BeEmpty();
    }

    // ── Terminal outcomes and fail-closed ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Enforce_AnInactiveEntry_RemovesNothing()
    {
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author, stateCode: 1);

        var report = await Enforce(entry);

        report.Outcome.Should().Be(NoAccessEnforcementOutcome.Inactive);
        _h.Shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enforce_AnEntryWithTwoSubjects_IsMalformed_AndRemovesNothing()
    {
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(
            subjectUser: Walled, subjectContact: LinkedContact, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Outcome.Should().Be(NoAccessEnforcementOutcome.Malformed);
        _h.Shares.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{14314314-3143-1431-4314-314314314301}")] // braces: the record filter never matches it
    [InlineData("14314314314314314314314314314301")]       // 32 digits, no hyphens
    [InlineData(" 14314314-3143-1431-4314-314314314301")]  // leading space: the record filter never matches it
    [InlineData("14314314-3143-1431-4314-314314314301\t")] // trailing tab: significant to Dataverse's comparison
    public async Task Enforce_ARecordIdNotInTheCanonicalForm_IsMalformed_AndRemovesNothing(string storedId)
    {
        // Task 154: before, Guid.TryParse accepted these, so the share was removed while the read-time veto (string
        // equality on the canonical id) never matched the entry: a wall that looked enforced and walled nothing.
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(
            subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author, objectRecordIdText: storedId);

        var report = await Enforce(entry);

        report.Outcome.Should().Be(NoAccessEnforcementOutcome.Malformed);
        _h.Shares.Writes.Should().BeEmpty();
        _h.Shares.MaskOf(Project, SecureProject, User(Walled)).Should().Be(CollaborateMask);
    }

    [Theory]
    [InlineData("14314314-3143-1431-4314-314314314301", "upper")]        // upper case: case-insensitive
    [InlineData("14314314-3143-1431-4314-314314314301  ", "")]           // trailing spaces: padding
    [InlineData("14314314-3143-1431-4314-314314314301\u3000", "")]      // trailing U+3000: width-insensitive padding
    public async Task Enforce_ARecordIdTheVetoMatches_IsEnforced(string storedId, string casing)
    {
        // Dataverse's string comparison (measured live, task 154) matches these, so the read-time veto honours the entry
        // and the enforcer must too: refusing them would make a working wall stop removing shares.
        var stored = casing == "upper" ? storedId.ToUpperInvariant() : storedId;
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(
            subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author, objectRecordIdText: stored);

        var report = await Enforce(entry);

        report.Outcome.Should().Be(NoAccessEnforcementOutcome.Evaluated);
        report.Removed.Should().ContainSingle(r => r.SystemUserId == Walled && r.RecordId == SecureProject);
    }

    [Fact]
    public async Task Enforce_WhenTheSharesCannotBeRead_IsAFailure_AndNothingIsReportedClean()
    {
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Shares.FailReads = true;
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Complete.Should().BeFalse();
        report.Failures.Should().ContainSingle(f => f.Kind == "shares-unreadable");
        _h.Shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enforce_WhenTheAuthorsRightsCannotBeRead_RemovesNothing_AndFails()
    {
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Store.FailRightsRead = true;
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Failures.Should().ContainSingle(f => f.Kind == "author-rights-unreadable");
        _h.Shares.Writes.Should().BeEmpty();
    }

    // ── Task 143 r1: the production fault shapes, and S5 under concurrency ─────────────────────────────────

    [Fact]
    public async Task EnforceForRecord_WhenTheRecordsOrganizationsComeBackUnresolved_FailsAndRemovesNothing()
    {
        // The PRODUCTION fault shape: the referenced-organization read reports Unresolved for the record and never
        // throws for it. Treating that as "references nothing" would skip every entry on a referenced organization.
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);
        _h.Participations.UnreadableReferencedOrganizations[SecureProject] = true;

        var reports = await _h.Enforcer.EnforceForRecordAsync(Project, SecureProject, new[] { Tenant }, CancellationToken.None);

        var report = reports.Should().ContainSingle().Subject;
        report.Outcome.Should().Be(NoAccessEnforcementOutcome.Failed);
        report.Failures.Should().ContainSingle(f => f.Kind == "covering-entries-unreadable");
        _h.Shares.Writes.Should().BeEmpty("nothing is enforced from a covering set that could not be read");
    }

    [Fact]
    public async Task Enforce_WhenTheRecordsOwnerCannotBeRead_ResidualAccessIsAFailure_NeverReportedClean()
    {
        // Criterion 7: access through team OWNERSHIP cannot be judged without the owner. The direct share still goes
        // (that removal does not depend on the owner); the residual check is a failure, never "nothing left".
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Store.FailOwnerRead = true;
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Complete.Should().BeFalse();
        report.Failures.Should().ContainSingle(f => f.Kind == "residual-access-unverifiable" && f.SystemUserId == Walled);
        report.NotEnforceable.Should().BeEmpty();
    }

    [Fact]
    public async Task Enforce_WhileAnotherEnforcementHoldsTheRecordsRemovalLock_RemovesNothing_AndSaysTryAgain()
    {
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);
        var held = await _h.Lease.TryAcquireAsync(
            NoAccessShareEnforcer.RecordLockId(Project, SecureProject), null, TimeSpan.FromMinutes(1), CancellationToken.None);
        held.Status.Should().Be(Spaarke.Scheduling.ScheduledJobLeaseStatus.Granted);

        var busy = await Enforce(entry);

        busy.Removed.Should().BeEmpty();
        busy.Failures.Should().ContainSingle(f => f.Kind == "record-busy" && f.RecordId == SecureProject);
        _h.Shares.MaskOf(Project, SecureProject, User(Walled)).Should().Be(CollaborateMask);

        await _h.Lease.ReleaseAsync(NoAccessShareEnforcer.RecordLockId(Project, SecureProject), held.Token!, CancellationToken.None);
        (await Enforce(entry)).Removed.Should().ContainSingle(r => r.SystemUserId == Walled, "once the lock is free it removes");
    }

    [Fact]
    public async Task Enforce_WhenTheRemovalLockCannotBeTaken_RemovesNothing()
    {
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Lease = new UnavailableLease();
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Failures.Should().ContainSingle(f => f.Kind == "record-lock-unavailable");
        _h.Shares.Writes.Should().BeEmpty("no lock, no removal: S5 cannot be protected without it (fail closed)");
    }

    [Fact]
    public async Task TwoConcurrentEnforcements_OnASecureRecordWhoseOnlyReadersAreBothWalled_NeverRemoveBoth()
    {
        // Owner S5 under concurrency (verifier finding 6): the save-time endpoint for one entry and the job (or a second
        // endpoint call) for another, interleaved so the SECOND starts while the first is between its "someone else
        // keeps access" check and its revoke. Without a per-record lock each sees the other user as the reader who
        // remains, and both are removed — a secure record nobody can open.
        var walledToo = Guid.NewGuid();
        _h.Store.Person(walledToo);
        _h.Shares.Reset(); // only the two walled users can read the record
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Shares.Seed(Project, SecureProject, User(walledToo), CollaborateMask);
        var first = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);
        var second = _h.Store.AddEntry(subjectUser: walledToo, objectRecord: (Project, SecureProject), modifiedBy: Author);

        NoAccessEnforcementReport? secondReport = null;
        var interleaved = new InterleavingShares(_h.Shares);
        var other = new NoAccessShareEnforcer(_h.Store, _h.Participations, _h.Identities, _h.Shares, _h.Cache.Mock.Object,
            _h.Lease, _h.ChildShares(), _h.Entities(), Microsoft.Extensions.Logging.Abstractions.NullLogger<NoAccessShareEnforcer>.Instance);
        interleaved.BeforeFirstRevoke = async () =>
            secondReport = await other.EnforceEntryAsync(second, new[] { Tenant }, CancellationToken.None);
        var enforcer = new NoAccessShareEnforcer(_h.Store, _h.Participations, _h.Identities, interleaved, _h.Cache.Mock.Object,
            _h.Lease, _h.ChildShares(interleaved), _h.Entities(), Microsoft.Extensions.Logging.Abstractions.NullLogger<NoAccessShareEnforcer>.Instance);

        var firstReport = await enforcer.EnforceEntryAsync(first, new[] { Tenant }, CancellationToken.None);

        secondReport.Should().NotBeNull("the second enforcement ran inside the first one's removal window");
        var stillReading = new[] { Walled, walledToo }
            .Where(u => _h.Shares.MaskOf(Project, SecureProject, User(u)) is { } m && (m & 1) != 0)
            .ToList();
        stillReading.Should().NotBeEmpty("a secure record always keeps at least one person who can open it (owner S5)");
        firstReport.Removed.Should().ContainSingle(r => r.SystemUserId == Walled);
        secondReport!.Removed.Should().BeEmpty();
        secondReport.Failures.Should().ContainSingle(f => f.Kind == "record-busy",
            "the second enforcement waits its turn; the job retries it within 5 minutes");
    }

    [Fact]
    public async Task AnEnforcementWhoseFirstShareReadPredatesAnotherEnforcementsRemoval_ReReadsUnderTheLock_AndKeepsTheLastReader()
    {
        // Task 143 r2 (verifier finding 1): enforcement B reads the shares {Walled, walledToo} BEFORE enforcement A takes
        // the record's lock; A then removes Walled and RELEASES the lock; only then does B take it. The lock is free, so
        // only the re-read under it stands between B and the stale "Walled still keeps access" — on the stale read B would
        // remove walledToo too, and the secure record would have no reader left (owner S5).
        var walledToo = Guid.NewGuid();
        _h.Store.Person(walledToo);
        _h.Shares.Reset(); // only the two walled users can read the record
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Shares.Seed(Project, SecureProject, User(walledToo), CollaborateMask);
        var entryA = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);
        var entryB = _h.Store.AddEntry(subjectUser: walledToo, objectRecord: (Project, SecureProject), modifiedBy: Author);

        NoAccessEnforcementReport? reportA = null;
        var staleFirstRead = new InterleavingShares(_h.Shares)
        {
            AfterFirstRead = async () => reportA = await _h.Enforcer.EnforceEntryAsync(entryA, new[] { Tenant }, CancellationToken.None),
        };
        var enforcerB = new NoAccessShareEnforcer(_h.Store, _h.Participations, _h.Identities, staleFirstRead, _h.Cache.Mock.Object,
            _h.Lease, _h.ChildShares(staleFirstRead), _h.Entities(), Microsoft.Extensions.Logging.Abstractions.NullLogger<NoAccessShareEnforcer>.Instance);

        var reportB = await enforcerB.EnforceEntryAsync(entryB, new[] { Tenant }, CancellationToken.None);

        reportA.Should().NotBeNull("A ran, start to finish, between B's first share read and B's lock");
        reportA!.Removed.Should().ContainSingle(r => r.SystemUserId == Walled);
        _h.Shares.MaskOf(Project, SecureProject, User(Walled)).Should().BeNull();
        reportB.Failures.Should().BeEmpty("the lock was free when B asked for it — the re-read, not the lock, decides here");
        reportB.Removed.Should().BeEmpty("on the fresh read under the lock walledToo is the last reader");
        reportB.NotEnforced.Should().ContainSingle().Which.Should().Be(
            new NoAccessNotEnforced(Project, SecureProject, walledToo, NoAccessEnforcementReason.LastPersonOnSecureRecord));
        _h.Shares.MaskOf(Project, SecureProject, User(walledToo)).Should().Be(CollaborateMask,
            "a secure record always keeps at least one person who can open it (owner S5)");
    }

    [Fact]
    public async Task Enforce_WhenTheRemovalLockExpiredBeforeTheRevoke_RemovesNothing_AndSaysTryAgain()
    {
        // Task 143 r2 (verifier finding 3): reads slowed past the lease (throttling) would let a second enforcement take
        // the lock and judge S5 from shares this one is about to change. The lease is renewed just before the revoke; a
        // lease that is no longer ours removes nothing.
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Lease = new ExpiringLease(renewThrows: false);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Removed.Should().BeEmpty();
        report.Failures.Should().ContainSingle(f => f.Kind == "record-lock-lost" && f.SystemUserId == Walled);
        _h.Shares.Writes.Should().BeEmpty("a lease that expired before the revoke no longer protects owner S5");
        _h.Shares.MaskOf(Project, SecureProject, User(Walled)).Should().Be(CollaborateMask);
    }

    [Fact]
    public async Task Enforce_WhenTheRemovalLockCannotBeRenewedBeforeTheRevoke_RemovesNothing()
    {
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        _h.Lease = new ExpiringLease(renewThrows: true);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Failures.Should().ContainSingle(f => f.Kind == "record-lock-unavailable");
        _h.Shares.Writes.Should().BeEmpty();
    }

    // ── Task 149 merged after this task (AC6): the secure record's children follow a removal at once ──────────────

    private const int CollaborateOnChild = 23; // Collaborate without Share — task 149's child mirror
    private static readonly Guid ChildDocument = Guid.Parse("14314314-3143-1431-4314-3143143143e1");
    private static readonly Guid ChildEvent = Guid.Parse("14314314-3143-1431-4314-3143143143e2");

    /// <summary>The secure project as task 149's synchronizer reads it: owned by the Secure team, with two children.</summary>
    private void SecureProjectWithChildren()
    {
        _h.ChildWorld = SecureChildShareWorld.Standard()
            .SecureRoot(Project, SecureProject)
            .SecureChild("sprk_document", ChildDocument, ("sprk_project", Project, SecureProject))
            .SecureChild("sprk_event", ChildEvent, ("sprk_regardingproject", Project, SecureProject));
        foreach (var (table, id) in new[] { ("sprk_document", ChildDocument), ("sprk_event", ChildEvent) })
        {
            _h.Shares.Seed(table, id, User(Walled), CollaborateOnChild);
            _h.Shares.Seed(table, id, User(Colleague), CollaborateOnChild);
        }
    }

    [Fact]
    public async Task Enforce_ARemovedRootShare_RemovesTheWalledUserFromEveryChildAtOnce_NotAtTheNextTick()
    {
        SecureProjectWithChildren();
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Complete.Should().BeTrue();
        report.Removed.Should().ContainSingle(r => r.SystemUserId == Walled && r.RecordId == SecureProject);
        _h.Shares.MaskOf("sprk_document", ChildDocument, User(Walled)).Should().BeNull("the children follow the root in the same call");
        _h.Shares.MaskOf("sprk_event", ChildEvent, User(Walled)).Should().BeNull();
        _h.Shares.MaskOf("sprk_document", ChildDocument, User(Colleague)).Should().Be(CollaborateOnChild, "the colleague keeps R");
        _h.Shares.MaskOf("sprk_event", ChildEvent, User(Colleague)).Should().Be(CollaborateOnChild);
    }

    [Fact]
    public async Task Enforce_WhenTheChildrenCannotBeUpdated_IsAFailureNamingTheRecord_AndTheRootRemovalStands()
    {
        SecureProjectWithChildren();
        _h.ChildWorld.FailingQueriesOf("sprk_event");
        _h.Shares.Seed(Project, SecureProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Complete.Should().BeFalse("a fan-out that could not finish is never \"complete\"");
        report.Failures.Should().ContainSingle(f => f.Kind == "children-incomplete" && f.RecordId == SecureProject);
        report.Removed.Should().ContainSingle(r => r.SystemUserId == Walled, "the root removal stands");
        _h.Shares.MaskOf(Project, SecureProject, User(Walled)).Should().BeNull();
    }

    [Fact]
    public async Task Enforce_WhenNothingWasRemovedOnTheRecord_DoesNotReadItsChildren()
    {
        SecureProjectWithChildren(); // the walled user holds child shares but no ROOT share: nothing to remove at the root
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);

        await Enforce(entry);

        // Task 158 final round (round 58 item 1): what is FILED UNDER the project (secure work assignments and projects) is
        // read whether or not anything was removed on it — that is the wall's reach, not task 149's fan-out to its children.
        _h.ChildWorld.QueriedTables.Should().OnlyContain(t => t == Project || t == "sprk_workassignment",
            "the fan-out to the record's CHILDREN runs only after a removal; the reconcile owns the rest");
        _h.ChildWorld.QueriedTables.Should().NotContain(new[] { "sprk_document", "sprk_event" });
    }

    // ── Task 158 final round (main-session round 58 item 1): the secure records FILED UNDER a walled record follow ─────

    private const string Matter = "sprk_matter";
    private const string WorkAssignment = "sprk_workassignment";
    private static readonly Guid SecureMatter = Guid.Parse("15858158-5815-8158-1581-5815815815a1");
    private static readonly Guid FiledWorkAssignment = Guid.Parse("15858158-5815-8158-1581-5815815815b1");
    private static readonly Guid FiledProject = Guid.Parse("15858158-5815-8158-1581-5815815815b2");
    private static readonly Guid MatterType = Guid.Parse("15858158-5815-8158-1581-5815815815c1");

    /// <summary>
    /// A secure matter with a secure work assignment filed under it (typed lookup) — both owned by the named team, the author
    /// holding Write on both, the colleague reading both (so S5 does not stop a removal unless a test says so).
    /// </summary>
    private void SecureMatterWithAFiledWorkAssignment(bool filedFlaggedSecure = true)
    {
        _h.Participations.Flags[SecureMatter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        _h.Participations.RecordTables[SecureMatter] = Matter;
        _h.Store.Rights[(Author, SecureMatter)] = AccessRights.Read | AccessRights.Write;
        _h.Store.Rights[(Author, FiledWorkAssignment)] = AccessRights.Read | AccessRights.Write;
        _h.ChildWorld = SecureChildShareWorld.Standard()
            .SecureRoot(Matter, SecureMatter)
            .Add(WorkAssignment, FiledWorkAssignment,
                ("owningteam", new Microsoft.Xrm.Sdk.EntityReference("team", SecureChildShareWorld.SecureTeam)),
                ("sprk_issecure", filedFlaggedSecure),
                ("sprk_regardingmatter", new Microsoft.Xrm.Sdk.EntityReference(Matter, SecureMatter)));
        _h.Shares.Seed(Matter, SecureMatter, User(Colleague), CollaborateMask);
        _h.Shares.Seed(WorkAssignment, FiledWorkAssignment, User(Colleague), CollaborateMask);
    }

    /// <summary>A secure project filed under the secure matter by the polymorphic pair (its text id + its type row).</summary>
    private void SecureProjectFiledUnderTheMatterByThePair(Guid? pairNames = null, string pairTypeName = Matter)
    {
        _h.Store.Rights[(Author, FiledProject)] = AccessRights.Read | AccessRights.Write;
        _h.ChildWorld
            .Add("sprk_recordtype_ref", MatterType, ("sprk_recordlogicalname", pairTypeName))
            .Add(Project, FiledProject,
                ("owningteam", new Microsoft.Xrm.Sdk.EntityReference("team", SecureChildShareWorld.SecureTeam)),
                ("sprk_issecure", true),
                ("sprk_regardingrecordid", (pairNames ?? SecureMatter).ToString("D")),
                ("sprk_regardingrecordtype", new Microsoft.Xrm.Sdk.EntityReference("sprk_recordtype_ref", MatterType)));
        _h.Shares.Seed(Project, FiledProject, User(Colleague), CollaborateMask);
    }

    /// <summary>
    /// Round 58 item 1: a person ADDED to a secure matter's No Access list loses their DIRECT share on the secure work
    /// assignment filed under it, in the same enforcement — removed, confirmed gone, the colleague untouched.
    /// </summary>
    [Fact]
    public async Task Enforce_AnEntryOnASecureMatter_AlsoRemovesTheWalledPersonsShareOnASecureWorkAssignmentFiledUnderIt()
    {
        SecureMatterWithAFiledWorkAssignment();
        _h.Shares.Seed(Matter, SecureMatter, User(Walled), CollaborateMask);
        _h.Shares.Seed(WorkAssignment, FiledWorkAssignment, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Matter, SecureMatter), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Complete.Should().BeTrue(string.Join("; ", report.Failures.Select(f => f.Message)));
        report.Removed.Select(r => (r.RecordType, r.RecordId)).Should().BeEquivalentTo(new[]
        {
            (Matter, SecureMatter), (WorkAssignment, FiledWorkAssignment),
        });
        _h.Shares.MaskOf(WorkAssignment, FiledWorkAssignment, User(Walled)).Should().BeNull("the wall reaches what is filed under it");
        _h.Shares.MaskOf(WorkAssignment, FiledWorkAssignment, User(Colleague)).Should().Be(CollaborateMask);
        report.CoveredRecords.Should().Be(2);
    }

    /// <summary>
    /// The pair: a secure project filed under the matter by its polymorphic regarding — reached too, even though the walled
    /// person holds nothing on the matter itself (the filed records are reached whether or not the parent had a share).
    /// </summary>
    [Fact]
    public async Task Enforce_AnEntryOnASecureMatter_ReachesAProjectFiledUnderItByThePair_EvenWithNoShareOnTheMatter()
    {
        SecureMatterWithAFiledWorkAssignment();
        SecureProjectFiledUnderTheMatterByThePair();
        _h.Shares.Seed(Project, FiledProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Matter, SecureMatter), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Complete.Should().BeTrue(string.Join("; ", report.Failures.Select(f => f.Message)));
        report.Removed.Should().ContainSingle().Which.Should().Be(new NoAccessRemovedShare(Walled, Project, FiledProject, CollaborateMask));
        _h.Shares.MaskOf(Project, FiledProject, User(Walled)).Should().BeNull();
    }

    /// <summary>An ORGANIZATION entry: a matter that references the organization is covered, and so is what is filed under it — once each.</summary>
    [Fact]
    public async Task Enforce_AnOrganizationEntry_ReachesWhatIsFiledUnderACoveredMatter_AndARecordCoveredTwiceOnce()
    {
        SecureMatterWithAFiledWorkAssignment();
        _h.Participations.RecordOrganizations[SecureMatter] = new[] { Firm };
        _h.Participations.Flags[FiledWorkAssignment] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        _h.Participations.RecordTables[FiledWorkAssignment] = WorkAssignment;
        _h.Participations.RecordOrganizations[FiledWorkAssignment] = new[] { Firm }; // covered in its own right too
        _h.Shares.Seed(Matter, SecureMatter, User(Walled), CollaborateMask);
        _h.Shares.Seed(WorkAssignment, FiledWorkAssignment, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectOrganization: Firm, modifiedBy: Author);

        var report = await Enforce(entry);

        report.Complete.Should().BeTrue(string.Join("; ", report.Failures.Select(f => f.Message)));
        report.Removed.Select(r => r.RecordId).Should().BeEquivalentTo(new[] { SecureMatter, FiledWorkAssignment });
        report.CoveredRecords.Should().Be(2, "the work assignment, covered and filed under a covered matter, is enforced once");
    }

    /// <summary>Owner S5 on a filed record: the walled person is the last one who can open it — the share is kept, and said so.</summary>
    [Fact]
    public async Task Enforce_OnARecordFiledUnderTheMatter_NeverRemovesItsLastReader()
    {
        SecureMatterWithAFiledWorkAssignment();
        _h.Shares.Seed(WorkAssignment, FiledWorkAssignment, User(Walled), CollaborateMask);
        _h.Store.Person(Colleague, disabled: true); // the colleague cannot open it any more
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Matter, SecureMatter), modifiedBy: Author);

        var report = await Enforce(entry);

        report.NotEnforced.Should().Contain(new NoAccessNotEnforced(
            WorkAssignment, FiledWorkAssignment, Walled, NoAccessEnforcementReason.LastPersonOnSecureRecord));
        _h.Shares.MaskOf(WorkAssignment, FiledWorkAssignment, User(Walled)).Should().Be(CollaborateMask);
    }

    /// <summary>Fault: what is filed under the matter cannot be read — children-incomplete on the matter; the matter's removal stands.</summary>
    [Fact]
    public async Task Enforce_WhenWhatIsFiledUnderTheMatterCannotBeRead_IsChildrenIncomplete_AndTheMattersRemovalStands()
    {
        SecureMatterWithAFiledWorkAssignment();
        _h.ChildWorld.FailingQueriesOf(WorkAssignment);
        _h.Shares.Seed(Matter, SecureMatter, User(Walled), CollaborateMask);
        _h.Shares.Seed(WorkAssignment, FiledWorkAssignment, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Matter, SecureMatter), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Complete.Should().BeFalse("a wall not applied to everything filed under it is never complete");
        report.Failures.Should().ContainSingle(f => f.Kind == "children-incomplete" && f.RecordId == SecureMatter);
        report.Removed.Should().ContainSingle(r => r.RecordId == SecureMatter, "the matter's removal stands");
        _h.Shares.MaskOf(WorkAssignment, FiledWorkAssignment, User(Walled)).Should().Be(CollaborateMask);
    }

    /// <summary>Fault on ONE filed record (its shares cannot be read): named on that record, and children-incomplete on the matter.</summary>
    [Fact]
    public async Task Enforce_WhenAFiledRecordsSharesCannotBeRead_IsAFailureThere_AndChildrenIncompleteOnTheMatter()
    {
        SecureMatterWithAFiledWorkAssignment();
        _h.Shares.Seed(WorkAssignment, FiledWorkAssignment, User(Walled), CollaborateMask);
        _h.Shares.FailReadsOfRecord = (WorkAssignment, FiledWorkAssignment);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Matter, SecureMatter), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Failures.Should().Contain(f => f.Kind == "shares-unreadable" && f.RecordId == FiledWorkAssignment);
        report.Failures.Should().Contain(f => f.Kind == "children-incomplete" && f.RecordId == SecureMatter);
        report.Complete.Should().BeFalse();
    }

    /// <summary>A record filed under the matter whose filing TYPE cannot be read: nothing removed on a guess — reported.</summary>
    [Fact]
    public async Task Enforce_ARecordWhoseFilingTypeCannotBeRead_IsLeftAlone_AndReportedIncomplete()
    {
        SecureMatterWithAFiledWorkAssignment();
        SecureProjectFiledUnderTheMatterByThePair();
        _h.ChildWorld.FailingQueriesOf("sprk_recordtype_ref");
        _h.Shares.Seed(Project, FiledProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Matter, SecureMatter), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Failures.Should().ContainSingle(f => f.Kind == "children-incomplete" && f.RecordId == SecureMatter);
        _h.Shares.MaskOf(Project, FiledProject, User(Walled)).Should().Be(CollaborateMask);
    }

    /// <summary>Q4: a record filed under the matter that is not secure yet is not the wall's (the inheritance job secures it first).</summary>
    [Fact]
    public async Task Enforce_ARecordFiledUnderTheMatterThatIsNotSecureYet_IsNotTheWalls()
    {
        SecureMatterWithAFiledWorkAssignment(filedFlaggedSecure: false);
        _h.Shares.Seed(WorkAssignment, FiledWorkAssignment, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Matter, SecureMatter), modifiedBy: Author);

        var report = await Enforce(entry);

        report.NotEnforced.Should().Contain(new NoAccessNotEnforced(
            WorkAssignment, FiledWorkAssignment, null, NoAccessEnforcementReason.NotSecure));
        _h.Shares.MaskOf(WorkAssignment, FiledWorkAssignment, User(Walled)).Should().Be(CollaborateMask);
    }

    /// <summary>Owner N5: an author without Write on the MATTER reaches nothing filed under it either.</summary>
    [Fact]
    public async Task Enforce_WhenTheAuthorLacksWriteOnTheMatter_NothingFiledUnderItIsTouched()
    {
        SecureMatterWithAFiledWorkAssignment();
        _h.Store.Rights[(Author, SecureMatter)] = AccessRights.Read;
        _h.Shares.Seed(WorkAssignment, FiledWorkAssignment, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Matter, SecureMatter), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Removed.Should().BeEmpty();
        _h.Shares.MaskOf(WorkAssignment, FiledWorkAssignment, User(Walled)).Should().Be(CollaborateMask);
        _h.ChildWorld.QueriedTables.Should().NotContain(WorkAssignment, "nothing filed under it is even read");
    }

    /// <summary>A covered WORK ASSIGNMENT has nothing filed under it: a project whose pair names it is not reached.</summary>
    [Fact]
    public async Task Enforce_AnEntryOnAWorkAssignment_ReachesNothingWhosePairNamesIt()
    {
        SecureMatterWithAFiledWorkAssignment();
        SecureProjectFiledUnderTheMatterByThePair(pairNames: FiledWorkAssignment, pairTypeName: WorkAssignment);
        _h.Participations.Flags[FiledWorkAssignment] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        _h.Shares.Seed(Project, FiledProject, User(Walled), CollaborateMask);
        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (WorkAssignment, FiledWorkAssignment), modifiedBy: Author);

        var report = await Enforce(entry);

        _h.Shares.MaskOf(Project, FiledProject, User(Walled)).Should().Be(CollaborateMask,
            "a work assignment passes nothing on (a pair naming it is not filed under a secure record)");
        report.Complete.Should().BeTrue(string.Join("; ", report.Failures.Select(f => f.Message)));
    }

    /// <summary>The bound: past <see cref="NoAccessShareEnforcer.MaxCoveredRecords"/> records in all, the report is truncated.</summary>
    [Fact]
    public async Task Enforce_MoreRecordsFiledUnderTheMatterThanOneCallCovers_IsTruncated()
    {
        SecureMatterWithAFiledWorkAssignment();
        for (var i = 0; i < NoAccessShareEnforcer.MaxCoveredRecords; i++)
        {
            _h.ChildWorld.Add(WorkAssignment, Guid.NewGuid(), ("sprk_issecure", true),
                ("sprk_regardingmatter", new Microsoft.Xrm.Sdk.EntityReference(Matter, SecureMatter)));
        }

        var entry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Matter, SecureMatter), modifiedBy: Author);

        var report = await Enforce(entry);

        report.Truncated.Should().BeTrue();
        report.Complete.Should().BeFalse();
        report.CoveredRecords.Should().Be(NoAccessShareEnforcer.MaxCoveredRecords);
    }

    /// <summary>
    /// "Update Access" on a work assignment filed under a secure matter re-applies the MATTER's entries too (its list governs
    /// every share on the work assignment — round 39 item 2), found through the one parent walk.
    /// </summary>
    [Fact]
    public async Task EnforceForRecord_OnAWorkAssignmentFiledUnderASecureMatter_ReappliesTheMattersEntries()
    {
        SecureMatterWithAFiledWorkAssignment();
        _h.Shares.Seed(WorkAssignment, FiledWorkAssignment, User(Walled), CollaborateMask);
        var matterEntry = _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Matter, SecureMatter), modifiedBy: Author);

        var reports = await _h.Enforcer.EnforceForRecordAsync(WorkAssignment, FiledWorkAssignment, new[] { Tenant }, CancellationToken.None);

        reports.Should().ContainSingle(r => r.EntryId == matterEntry);
        _h.Shares.MaskOf(WorkAssignment, FiledWorkAssignment, User(Walled)).Should().BeNull("the matter's wall reaches it now");
        _h.Shares.MaskOf(WorkAssignment, FiledWorkAssignment, User(Colleague)).Should().Be(CollaborateMask);
    }

    /// <summary>What the work assignment is filed under cannot be read: nothing is re-applied — reported, never "done".</summary>
    [Fact]
    public async Task EnforceForRecord_WhenWhatTheRecordIsFiledUnderCannotBeRead_ReappliesNothing_AndSaysSo()
    {
        SecureMatterWithAFiledWorkAssignment();
        _h.ChildWorld.FailingRowReadsOf(WorkAssignment, FiledWorkAssignment);
        _h.Participations.Flags[FiledWorkAssignment] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        _h.Shares.Seed(WorkAssignment, FiledWorkAssignment, User(Walled), CollaborateMask);
        _h.Store.AddEntry(subjectUser: Walled, objectRecord: (WorkAssignment, FiledWorkAssignment), modifiedBy: Author);

        var reports = await _h.Enforcer.EnforceForRecordAsync(WorkAssignment, FiledWorkAssignment, new[] { Tenant }, CancellationToken.None);

        reports.Should().ContainSingle().Which.Outcome.Should().Be(NoAccessEnforcementOutcome.Failed);
        _h.Shares.MaskOf(WorkAssignment, FiledWorkAssignment, User(Walled)).Should().Be(CollaborateMask);
    }

    /// <summary>More entries cover a secure parent than one call re-applies: reported (the 5-minute job enforces the rest).</summary>
    [Fact]
    public async Task EnforceForRecord_WhenMoreEntriesCoverTheMatterThanOneCallReapplies_IsReportedTruncated()
    {
        SecureMatterWithAFiledWorkAssignment();
        for (var i = 0; i <= NoAccessShareEnforcer.MaxEntriesPerRecord; i++)
            _h.Store.AddEntry(subjectUser: Guid.NewGuid(), objectRecord: (Matter, SecureMatter), modifiedBy: Author);

        var reports = await _h.Enforcer.EnforceForRecordAsync(WorkAssignment, FiledWorkAssignment, new[] { Tenant }, CancellationToken.None);

        reports.Should().Contain(r => r.Failures.Any(f => f.Kind == "covering-entries-truncated"));
    }

    /// <summary>A lease that is granted, then found expired (or unreachable) when renewed.</summary>
    private sealed class ExpiringLease(bool renewThrows) : Spaarke.Scheduling.IScheduledJobLease
    {
        private readonly Spaarke.Scheduling.ProcessLocalScheduledJobLease _inner = new();

        public bool IsDistributed => true;

        public Task<Spaarke.Scheduling.ScheduledJobLeaseGrant> TryAcquireAsync(
            string jobId, DateTimeOffset? occurrenceUtc, TimeSpan duration, CancellationToken cancellationToken)
            => _inner.TryAcquireAsync(jobId, occurrenceUtc, duration, cancellationToken);

        public Task<bool> RenewAsync(string jobId, string token, TimeSpan duration, CancellationToken cancellationToken)
            => renewThrows
                ? throw new Spaarke.Scheduling.ScheduledJobLeaseUnavailableException("Simulated: Redis is not connected.")
                : Task.FromResult(false);

        public Task ReleaseAsync(string jobId, string token, CancellationToken cancellationToken)
            => _inner.ReleaseAsync(jobId, token, cancellationToken);
    }

    /// <summary>A lease store that cannot be reached (Redis down).</summary>
    private sealed class UnavailableLease : Spaarke.Scheduling.IScheduledJobLease
    {
        public bool IsDistributed => true;

        public Task<Spaarke.Scheduling.ScheduledJobLeaseGrant> TryAcquireAsync(
            string jobId, DateTimeOffset? occurrenceUtc, TimeSpan duration, CancellationToken cancellationToken)
            => throw new Spaarke.Scheduling.ScheduledJobLeaseUnavailableException("Simulated: Redis is not connected.");

        public Task<bool> RenewAsync(string jobId, string token, TimeSpan duration, CancellationToken cancellationToken)
            => throw new Spaarke.Scheduling.ScheduledJobLeaseUnavailableException("Simulated.");

        public Task ReleaseAsync(string jobId, string token, CancellationToken cancellationToken)
            => throw new Spaarke.Scheduling.ScheduledJobLeaseUnavailableException("Simulated.");
    }

    /// <summary>
    /// The strict share table, with two one-shot hooks: one runs just before the first revoke reaches it; the other runs
    /// just after the first share read was taken, and that read's (now stale) answer is what the caller gets.
    /// </summary>
    private sealed class InterleavingShares(FakeRecordShareTable inner) : Sprk.Bff.Api.Services.Access.IDataverseRecordShareService
    {
        public Func<Task>? BeforeFirstRevoke { get; set; }

        public Func<Task>? AfterFirstRead { get; set; }

        public Task GrantAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, string accessRightsCsv,
            CancellationToken ct = default) => inner.GrantAccessAsync(entitySetName, recordId, principal, accessRightsCsv, ct);

        public Task ModifyAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, string accessRightsCsv,
            CancellationToken ct = default) => inner.ModifyAccessAsync(entitySetName, recordId, principal, accessRightsCsv, ct);

        public async Task RevokeAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal,
            CancellationToken ct = default)
        {
            if (BeforeFirstRevoke is { } hook)
            {
                BeforeFirstRevoke = null;
                await hook();
            }

            await inner.RevokeAccessAsync(entitySetName, recordId, principal, ct);
        }

        public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessAsync(string entityLogicalName, Guid recordId,
            CancellationToken ct = default) => inner.GetPrincipalAccessAsync(entityLogicalName, recordId, ct);

        public async Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessOrThrowAsync(string entityLogicalName,
            Guid recordId, CancellationToken ct = default)
        {
            var shares = await inner.GetPrincipalAccessOrThrowAsync(entityLogicalName, recordId, ct);
            if (AfterFirstRead is { } hook)
            {
                AfterFirstRead = null;
                await hook();
            }

            return shares;
        }

        // Task 149: the batched strict read the secure-child synchronizer uses (merged with task 143).
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>> GetPrincipalAccessForRecordsOrThrowAsync(
            string entityLogicalName, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
            => inner.GetPrincipalAccessForRecordsOrThrowAsync(entityLogicalName, recordIds, ct);
    }
}
