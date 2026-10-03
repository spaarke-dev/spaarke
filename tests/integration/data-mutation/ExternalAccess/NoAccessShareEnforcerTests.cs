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
            _h.Lease, Microsoft.Extensions.Logging.Abstractions.NullLogger<NoAccessShareEnforcer>.Instance);
        interleaved.BeforeFirstRevoke = async () =>
            secondReport = await other.EnforceEntryAsync(second, new[] { Tenant }, CancellationToken.None);
        var enforcer = new NoAccessShareEnforcer(_h.Store, _h.Participations, _h.Identities, interleaved, _h.Cache.Mock.Object,
            _h.Lease, Microsoft.Extensions.Logging.Abstractions.NullLogger<NoAccessShareEnforcer>.Instance);

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

    /// <summary>The strict share table, with a hook that runs ONCE just before the first revoke reaches it.</summary>
    private sealed class InterleavingShares(FakeRecordShareTable inner) : Sprk.Bff.Api.Services.Access.IDataverseRecordShareService
    {
        public Func<Task>? BeforeFirstRevoke { get; set; }

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

        public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessOrThrowAsync(string entityLogicalName,
            Guid recordId, CancellationToken ct = default) => inner.GetPrincipalAccessOrThrowAsync(entityLogicalName, recordId, ct);
    }
}
