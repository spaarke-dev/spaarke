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

    // ── S5: a secure record keeps someone who can open it ──────────────────────

    [Fact]
    public async Task OnARestrictedSecureRecord_WhenOnlyExternalUsersCanOpenIt_TheirSharesAreKept_AndReported()
    {
        Restricted(secure: true);
        var external = _h.SystemUser(isExternal: true);
        var disabledInternal = _h.SystemUser(isExternal: false, disabled: true);
        Share(external);
        Share(disabledInternal);

        var report = await RunAsync();

        report.Removed.Should().BeEmpty();
        report.KeptAsLastReader.Should().Equal(external);
        report.Complete.Should().BeTrue("S5 keeping a share is a decision, not a failure");
        MaskOf(external).Should().Be(CollaborateMask, "a disabled internal user does not count as someone who can open it");
        _h.Shares.Writes.Should().BeEmpty();
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
        report.KeptAsLastReader.Should().BeEmpty();
        MaskOf(internalReader).Should().Be(ViewOnlyMask);
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
