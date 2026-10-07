// unified-access-control-r2 task 114 (owner round 67, 2026-10-06, amendments 3 and 4(b)) — a RESTRICTED record keeps no
// direct share held by a system user flagged sprk_isexternal = true. The PRODUCTION RestrictedExternalShareRemover runs over
// AssignedAccessTestDoubles' module boundaries (flags, the POA share table, the systemuser rows, the tenant cache, the real
// secure-child synchronizer). KEEP path: tests/integration/auth/** (ADR-038 security-auth). Every negative has a positive
// twin that differs in one input.

using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

public class RestrictedExternalShareRemoverTests
{
    private const string MatterTable = "sprk_matter";
    private const int ViewOnlyMask = 1;            // Read
    private const int CollaborateMask = 262167;    // Read 1 + Write 2 + Append 4 + AppendTo 16 + Share 262144

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();

    private void Restricted(bool secure = false)
        => _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: secure, IsRestricted: true);

    private void Share(Guid user, int mask = CollaborateMask)
        => _h.Shares.Seed(MatterTable, _matter, DataversePrincipalRef.User(user), mask);

    private int? MaskOf(Guid user) => _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.User(user));

    private Task<RestrictedExternalShareReport> RunAsync()
        => _h.RestrictedRemover.RemoveForRecordAsync(ExternalGrantRootType.Matter, _matter, new[] { TestTenant }, CancellationToken.None);

    [Fact]
    public async Task OnARestrictedRecord_TheShareOfAUserFlaggedExternal_IsRemoved_ConfirmedByReadBack_AndTheirCacheCleared()
    {
        Restricted();
        var external = _h.SystemUser(isExternal: true);
        Share(external);

        var report = await RunAsync();

        report.Outcome.Should().Be(RestrictedExternalShareOutcome.Evaluated);
        report.Complete.Should().BeTrue();
        report.Removed.Should().Equal(external);
        MaskOf(external).Should().BeNull("the share is gone");
        _h.Shares.Writes.Should().Equal("RevokeAccess");
        _h.Cache.Removed.Should().Contain(r => r.Tenant == TestTenant && r.Id.Contains(external.ToString("D")),
            "the removed user's impersonated root-set cache is cleared under the deployment tenant, never 'anonymous'");
    }

    /// <summary>Owner round 67 item 4: no other share is removed — an internal user, a BLANK flag, a team.</summary>
    [Fact]
    public async Task OnARestrictedRecord_OnlyAStoredTrueIsRemoved_ABlankFlagAnInternalUserAndATeamKeepTheirShares()
    {
        Restricted();
        var external = _h.SystemUser(isExternal: true);
        var blank = _h.SystemUser(isExternal: null);
        var internalUser = _h.SystemUser(isExternal: false);
        var team = Guid.NewGuid();
        Share(external);
        Share(blank);
        Share(internalUser);
        _h.Shares.Seed(MatterTable, _matter, DataversePrincipalRef.Team(team), CollaborateMask);

        var report = await RunAsync();

        report.Removed.Should().Equal(external);
        MaskOf(blank).Should().Be(CollaborateMask, "a blank sprk_isexternal is NOT external (round 67 item 3)");
        MaskOf(internalUser).Should().Be(CollaborateMask);
        _h.Shares.MaskOf(MatterTable, _matter, DataversePrincipalRef.Team(team)).Should().Be(CollaborateMask,
            "a team's share is never removed here: its other members would lose access");
    }

    /// <summary>The positive twin: on a record that is NOT Restricted the rule does nothing, and reads no share.</summary>
    [Fact]
    public async Task OnARecordThatIsNotRestricted_AnExternalUsersShareIsKept_AndNoShareIsRead()
    {
        var external = _h.SystemUser(isExternal: true);
        Share(external);

        var report = await RunAsync();

        report.Outcome.Should().Be(RestrictedExternalShareOutcome.NotRestricted);
        report.Complete.Should().BeTrue();
        MaskOf(external).Should().Be(CollaborateMask);
        _h.Shares.StrictReads.Should().Be(0);
        _h.Shares.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]   // the flag read throws
    [InlineData(false)]  // the record does not come back (Unreadable)
    public async Task WhenWhetherTheRecordIsRestrictedCannotBeRead_NothingIsRemoved_AndItIsAFailure(bool throws)
    {
        var external = _h.SystemUser(isExternal: true);
        Share(external);
        if (throws)
            _h.Participations.ThrowOnRead = true;
        else
            _h.Participations.Absent[_matter] = true;

        var report = await RunAsync();

        report.Outcome.Should().Be(RestrictedExternalShareOutcome.FlagsUnreadable);
        report.Complete.Should().BeFalse();
        report.Failures.Should().ContainSingle().Which.Kind.Should().Be("flags-unreadable");
        MaskOf(external).Should().Be(CollaborateMask);
        _h.Shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheSharesCannotBeRead_NothingIsRemoved_AndItIsAFailure()
    {
        Restricted();
        Share(_h.SystemUser(isExternal: true));
        _h.Shares.FailReads = true;

        var report = await RunAsync();

        report.Outcome.Should().Be(RestrictedExternalShareOutcome.Failed);
        report.Failures.Should().ContainSingle().Which.Kind.Should().Be("shares-unreadable");
        _h.Shares.Writes.Should().BeEmpty();
    }

    /// <summary>A flag that cannot be read is never "not external": the users' read failing removes nothing.</summary>
    [Fact]
    public async Task WhenTheUsersFlagsCannotBeRead_NothingIsRemoved_AndItIsAFailure()
    {
        Restricted();
        var external = _h.SystemUser(isExternal: true);
        Share(external);
        _h.Grants.FailQueries = true;

        var report = await RunAsync();

        report.Outcome.Should().Be(RestrictedExternalShareOutcome.Failed);
        report.Failures.Should().ContainSingle().Which.Kind.Should().Be("users-unreadable");
        MaskOf(external).Should().Be(CollaborateMask);
        _h.Shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ARevokeDataverseDoesNotApply_IsNotConfirmed_AndReportedNeverRemoved()
    {
        Restricted();
        var external = _h.SystemUser(isExternal: true);
        Share(external);
        _h.Shares.IgnoreWrites = true;

        var report = await RunAsync();

        report.Removed.Should().BeEmpty();
        report.Complete.Should().BeFalse();
        report.Failures.Should().ContainSingle(f => f.Kind == "revoke-not-confirmed" && f.SystemUserId == external);
        _h.Cache.Removed.Should().Contain(r => r.Id.Contains(external.ToString("D")),
            "the cache is cleared whether or not the revoke is confirmed: it may have applied");
    }

    // ── Restricted wins over the last-reader rule (owner round 67 item 3, decided 2026-10-06) ─────────────

    /// <summary>
    /// The external user is the last person who can open the secure Restricted record (a disabled internal user does not
    /// count): their share is REMOVED anyway, and the pass reports no-internal-reader — an administrator must share it with an
    /// internal user. A decision for a person, not a failure.
    /// </summary>
    [Fact]
    public async Task OnARestrictedSecureRecord_WhenOnlyExternalUsersCanOpenIt_TheirSharesAreRemoved_AndNoInternalReaderIsReported()
    {
        Restricted(secure: true);
        var external = _h.SystemUser(isExternal: true);
        var disabledInternal = _h.SystemUser(isExternal: false, disabled: true);
        Share(external);
        Share(disabledInternal);

        var report = await RunAsync();

        report.Removed.Should().Equal(external);
        MaskOf(external).Should().BeNull("Restricted wins over the last-reader rule");
        report.NoInternalReader.Should().BeTrue("a disabled internal user does not count as someone who can open it");
        report.Complete.Should().BeTrue("no internal reader is an administrator's action, not a failure");
        report.Failures.Should().BeEmpty();
    }

    /// <summary>On a Restricted record that is NOT secure the business unit can still open it: no-internal-reader is never reported.</summary>
    [Fact]
    public async Task OnARestrictedRecordThatIsNotSecure_RemovingTheOnlySharerReportsNoInternalReader_Never()
    {
        Restricted(secure: false);
        Share(_h.SystemUser(isExternal: true));

        var report = await RunAsync();

        report.Removed.Should().ContainSingle();
        report.NoInternalReader.Should().BeFalse();
    }

    /// <summary>The positive twin: an enabled internal reader remains, so the external user's share goes.</summary>
    [Fact]
    public async Task OnARestrictedSecureRecord_WithAnEnabledInternalReader_TheExternalUsersShareIsRemoved()
    {
        Restricted(secure: true);
        var external = _h.SystemUser(isExternal: true);
        var internalReader = _h.SystemUser(isExternal: null);
        Share(external);
        Share(internalReader, ViewOnlyMask);

        var report = await RunAsync();

        report.Removed.Should().Equal(external);
        report.NoInternalReader.Should().BeFalse("a blank-flagged enabled user with Read remains");
        MaskOf(internalReader).Should().Be(ViewOnlyMask);
    }

    // ── An external OWNER (follow-up item 2) ───────────────────────────────────

    /// <summary>
    /// The record's OWNER is flagged external: ownership confers access no revoke removes (Dataverse refuses an app-only
    /// revoke of the owner's own share, 0x80040223), so their share is not touched and the pass reports it once as
    /// owner-is-external — a decision, not a failure; other external sharers are still removed. Ownership is not changed.
    /// </summary>
    [Fact]
    public async Task AnExternalOwner_IsReportedOwnerIsExternal_NotRevoked_AndOtherExternalSharersAreStillRemoved()
    {
        Restricted();
        var owner = _h.SystemUser(isExternal: true);
        var other = _h.SystemUser(isExternal: true);
        Share(owner);
        Share(other);
        _h.Grants.RootOwners[_matter] = owner;

        var report = await RunAsync();

        report.OwnerIsExternal.Should().Be(owner);
        report.Complete.Should().BeTrue("an external owner is an administrator's action, not an unconfirmed removal");
        report.Failures.Should().BeEmpty();
        report.Removed.Should().Equal(other);
        MaskOf(owner).Should().Be(CollaborateMask, "the owner's share is never revoked here");
        _h.Shares.WriteLog.Should().NotContain(w => w.Principal == DataversePrincipalRef.User(owner));
    }

    /// <summary>The positive twin: an INTERNAL owner changes nothing — the external sharer is removed, no owner report.</summary>
    [Fact]
    public async Task AnInternalOwner_IsNotReported()
    {
        Restricted();
        var external = _h.SystemUser(isExternal: true);
        Share(external);
        _h.Grants.RootOwners[_matter] = _h.SystemUser(isExternal: false);

        var report = await RunAsync();

        report.OwnerIsExternal.Should().BeNull();
        report.Removed.Should().Equal(external);
    }

    // ── Serialized with task 143's enforcer (follow-up item 4) ──────────────────

    /// <summary>
    /// Another removal (task 143's No Access enforcer, /unshare-user) holds the record's per-record lease — the SAME key — so
    /// nothing is read or removed, and the pass fails (record-busy) for the job to retry.
    /// </summary>
    [Fact]
    public async Task WhileTheRecordsRemovalLeaseIsHeldElsewhere_NothingIsRemoved_AndThePassFailsRecordBusy()
    {
        Restricted();
        var external = _h.SystemUser(isExternal: true);
        Share(external);
        var held = await _h.Lease.TryAcquireAsync(
            NoAccessShareEnforcer.RecordLockId(MatterTable, _matter), occurrenceUtc: null, TimeSpan.FromMinutes(1),
            CancellationToken.None);
        held.Status.Should().Be(Spaarke.Scheduling.ScheduledJobLeaseStatus.Granted);

        var report = await RunAsync();

        report.Failures.Should().ContainSingle().Which.Kind.Should().Be("record-busy");
        MaskOf(external).Should().Be(CollaborateMask);
        _h.Shares.Writes.Should().BeEmpty();
        _h.Shares.StrictReads.Should().Be(0, "nothing is decided outside the lease");
    }

    /// <summary>The lease is renewed before every revoke; one that cannot be renewed removes nothing more (record-lock-lost).</summary>
    [Fact]
    public async Task WhenTheLeaseCannotBeRenewedBeforeARevoke_NothingIsRemoved_AndItIsAFailure()
    {
        Restricted();
        var external = _h.SystemUser(isExternal: true);
        Share(external);
        _h.Lease = new NonRenewingLease();

        var report = await RunAsync();

        report.Removed.Should().BeEmpty();
        report.Failures.Should().ContainSingle().Which.Kind.Should().Be("record-lock-lost");
        MaskOf(external).Should().Be(CollaborateMask);
    }

    /// <summary>The lease is released after the pass: a second pass takes it again (never left held).</summary>
    [Fact]
    public async Task TheLeaseIsReleasedAfterThePass()
    {
        Restricted();
        Share(_h.SystemUser(isExternal: true));
        await RunAsync();

        var again = await _h.Lease.TryAcquireAsync(
            NoAccessShareEnforcer.RecordLockId(MatterTable, _matter), occurrenceUtc: null, TimeSpan.FromMinutes(1),
            CancellationToken.None);

        again.Status.Should().Be(Spaarke.Scheduling.ScheduledJobLeaseStatus.Granted);
    }

    /// <summary>A lease that is granted and released normally but is never renewable (it expired).</summary>
    private sealed class NonRenewingLease : Spaarke.Scheduling.IScheduledJobLease
    {
        private readonly Spaarke.Scheduling.ProcessLocalScheduledJobLease _inner = new();

        public bool IsDistributed => true;

        public Task<Spaarke.Scheduling.ScheduledJobLeaseGrant> TryAcquireAsync(
            string jobId, DateTimeOffset? occurrenceUtc, TimeSpan duration, CancellationToken cancellationToken)
            => _inner.TryAcquireAsync(jobId, occurrenceUtc, duration, cancellationToken);

        public Task<bool> RenewAsync(string jobId, string token, TimeSpan duration, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task ReleaseAsync(string jobId, string token, CancellationToken cancellationToken)
            => _inner.ReleaseAsync(jobId, token, cancellationToken);
    }

    [Fact]
    public async Task ARepeatedPass_OverARecordAlreadyInLine_WritesNothing()
    {
        Restricted();
        Share(_h.SystemUser(isExternal: true));
        await RunAsync();
        var writes = _h.Shares.Writes.Count;

        var second = await RunAsync();

        second.Removed.Should().BeEmpty();
        _h.Shares.Writes.Should().HaveCount(writes);
    }
}
