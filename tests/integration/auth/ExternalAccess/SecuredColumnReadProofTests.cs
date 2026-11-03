// SecuredColumnReadProof: an EMPTY read of a field-secured column is trusted only once the BFF's read of the column is
// proven — by the System Administrator role, or by a field security profile granting Read, held directly or through a team.
// One proof covers a set of columns (every one must be proven), holds for ProofLifetime, and is then made again.
//
// Module-boundary substitute only (IColumnReadPermissionSource) per tests/CLAUDE.md.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public class SecuredColumnReadProofTests
{
    // The contact identity columns: the binding, its plane and the five collision-flag columns.
    private static readonly string[] Columns =
    [
        ContactBindingDecision.ExternalObjectIdColumn, ContactBindingDecision.IdentityPlaneColumn,
        DataverseContactIdentityStore.FlagOnColumn, DataverseContactIdentityStore.FlagOidColumn,
        DataverseContactIdentityStore.FlagPlaneColumn, DataverseContactIdentityStore.FlagReasonColumn,
        DataverseContactIdentityStore.FlagPartiesColumn,
    ];

    private static SecuredColumnReadProof ProofOver(Grants grants, TimeProvider? time = null)
        => new(grants, "contact", Columns, NullLogger.Instance, time);

    // ── must fire ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoLeg_IsUnproven()
    {
        var grants = new Grants();

        (await ProofOver(grants).ProveAsync(CancellationToken.None)).Should().Be(ColumnReadProofOutcome.Unproven);
        grants.Calls.Should().Equal("me", "admin", "direct", "team");
    }

    [Theory]
    [InlineData(ContactBindingDecision.IdentityPlaneColumn)]
    [InlineData(DataverseContactIdentityStore.FlagPartiesColumn)]
    [InlineData(DataverseContactIdentityStore.FlagOnColumn)]
    public async Task OneColumnNotGranted_IsUnproven_ForTheWholeSet(string missing)
    {
        var grants = new Grants { Direct = Columns.Where(c => c != missing).ToHashSet() };

        (await ProofOver(grants).IsProvenAsync(CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task AFault_IsFaulted_AndNotRemembered()
    {
        var grants = new Grants { Faults = true, Direct = Columns.ToHashSet() };
        var proof = ProofOver(grants);

        (await proof.ProveAsync(CancellationToken.None)).Should().Be(ColumnReadProofOutcome.Faulted);
        grants.Faults = false;
        (await proof.ProveAsync(CancellationToken.None)).Should().Be(ColumnReadProofOutcome.Proven);
    }

    [Fact]
    public async Task NoOwnUser_IsFaulted()
        => (await ProofOver(new Grants { OwnUser = null, Admin = true }).ProveAsync(CancellationToken.None))
            .Should().Be(ColumnReadProofOutcome.Faulted);

    [Fact]
    public async Task AfterTheProofLifetime_ANowFailingReproof_Denies()
    {
        // F2: the proof is made again after ProofLifetime; a grant removed meanwhile now fails it.
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-23T10:00:00Z"));
        var grants = new Grants { Direct = Columns.ToHashSet() };
        var proof = ProofOver(grants, clock);
        (await proof.IsProvenAsync(CancellationToken.None)).Should().BeTrue();

        grants.Direct.Clear();
        clock.Advance(SecuredColumnReadProof.ProofLifetime);

        (await proof.IsProvenAsync(CancellationToken.None)).Should().BeFalse();
        grants.Calls.Count(c => c == "me").Should().Be(2, "the expired proof was made again");
    }

    [Fact]
    public async Task ThePlatformsCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var grants = new Grants { CancelWith = cts.Token };

        await FluentActions.Awaiting(() => ProofOver(grants).ProveAsync(cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    // ── must not fire ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheSystemAdministratorRole_Proves_WithoutAProfileRead()
    {
        var grants = new Grants { Admin = true };

        (await ProofOver(grants).ProveAsync(CancellationToken.None)).Should().Be(ColumnReadProofOutcome.Proven);
        grants.Calls.Should().Equal("me", "admin");
    }

    [Fact]
    public async Task DirectAndTeamProfiles_TogetherCoverEveryColumn_Proves()
    {
        var grants = new Grants { Direct = Columns.Take(3).ToHashSet(), Team = Columns.Skip(3).ToHashSet() };

        (await ProofOver(grants).ProveAsync(CancellationToken.None)).Should().Be(ColumnReadProofOutcome.Proven);
        grants.TeamAsked.Should().BeEquivalentTo(Columns.Skip(3), "only the columns the direct profiles left are asked of the teams");
    }

    [Fact]
    public async Task AProof_IsHeldWithinItsLifetime_OneLifetimeForEveryColumn()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-23T10:00:00Z"));
        var grants = new Grants { Team = Columns.ToHashSet() };
        var proof = ProofOver(grants, clock);

        (await proof.IsProvenAsync(CancellationToken.None)).Should().BeTrue();
        var calls = grants.Calls.Count;
        clock.Advance(SecuredColumnReadProof.ProofLifetime - TimeSpan.FromSeconds(1));

        (await proof.IsProvenAsync(CancellationToken.None)).Should().BeTrue();
        grants.Calls.Should().HaveCount(calls, "within its lifetime the proof is held");
        SecuredColumnReadProof.ProofLifetime.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task AFailedProof_IsMadeAgain_AtTheNextUse()
    {
        var grants = new Grants();
        var proof = ProofOver(grants);

        (await proof.IsProvenAsync(CancellationToken.None)).Should().BeFalse();
        grants.Admin = true;

        (await proof.IsProvenAsync(CancellationToken.None)).Should().BeTrue("a later grant is picked up");
    }

    /// <summary>What the BFF's user holds. Records each read in <see cref="Calls"/>.</summary>
    private sealed class Grants : IColumnReadPermissionSource
    {
        public Guid? OwnUser { get; set; } = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        public bool Admin { get; set; }
        public HashSet<string> Direct { get; set; } = new();
        public HashSet<string> Team { get; set; } = new();
        public bool Faults { get; set; }
        public CancellationToken? CancelWith { get; set; }
        public List<string> Calls { get; } = new();
        public List<string> TeamAsked { get; } = new();

        public Task<Guid?> ReadOwnUserIdAsync(CancellationToken ct)
        {
            Calls.Add("me");
            CancelWith?.ThrowIfCancellationRequested();
            return Faults ? throw new HttpRequestException("throttled") : Task.FromResult(OwnUser);
        }

        public Task<bool> HoldsSystemAdministratorAsync(Guid user, CancellationToken ct)
        {
            Calls.Add("admin");
            return Task.FromResult(Admin);
        }

        public Task<IReadOnlyCollection<string>> ColumnsGrantingReadAsync(
            Guid user, string table, IReadOnlyCollection<string> columns, bool throughTeam, CancellationToken ct)
        {
            Calls.Add(throughTeam ? "team" : "direct");
            if (throughTeam)
            {
                TeamAsked.AddRange(columns);
            }

            var granted = throughTeam ? Team : Direct;
            return Task.FromResult<IReadOnlyCollection<string>>(columns.Where(granted.Contains).ToArray());
        }
    }
}
