using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// UAC task 183 (owner round 96, D-135) — <see cref="SecureChildReconciler.ReownOrdinaryRowAsync"/>, the one entry point
/// ontology task 148 re-owns an ordinary Decision Record or Signal through. Driven over the REAL reconciler and one in-memory
/// Dataverse that evaluates every query (<see cref="SecureChildShareWorld"/>), so a guard that read the wrong column or
/// dropped a predicate re-owns a row here. Every refusal asserts NO owner write and NO eviction.
/// </summary>
public class SecureChildReownOrdinaryRowTests
{
    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;
    private static readonly Guid GeneralBu = SecureChildShareWorld.GeneralBu;
    private static readonly Guid SecureBu = SecureChildShareWorld.SecureBu;
    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;
    private static readonly Guid OtherBu = Guid.Parse("0b000000-0000-4000-8000-0000000000b1");
    private static readonly Guid OtherBuTeam = Guid.Parse("0b000000-0000-4000-8000-0000000000b2");

    /// <summary>Task 178's shape: a second owner team in the Secure Record BU (one per secure family), not the named one.</summary>
    private static readonly Guid SecureFamilyTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000004");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", OtherBu, ("name", "Litigation"))
        .Team(OtherBuTeam, OtherBu, "Litigation", isDefault: true, teamType: 0)
        .Team(SecureFamilyTeam, SecureBu, "Secure Matter Owners", isDefault: false, teamType: 0);

    private readonly EvictionRecorder _evictions = new();

    private SecureChildReconciler Reconciler(Mock<IGenericEntityService>? entities = null)
    {
        if (entities is null)
            return SecureChildShareWorld.ReconcilerOver(() => _world, new FakeRecordShareTable(), webApi: null!,
                accessCacheInvalidator: _evictions);

        var configuration = SecureChildShareWorld.Configuration();
        var resolver = new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver(
            entities.Object, configuration, NullLogger<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver>.Instance);
        var synchronizer = new SecureChildShareSynchronizer(entities.Object, new FakeRecordShareTable(),
            SecureChildShareWorld.NobodyWalled(), configuration, NullLogger<SecureChildShareSynchronizer>.Instance);
        return new SecureChildReconciler(entities.Object, resolver, synchronizer, webApi: null!, configuration,
            NullLogger<SecureChildReconciler>.Instance, _evictions);
    }

    private Guid TeamOwnedRow(string table, Guid team, Guid? storedBusinessUnit = null)
    {
        var id = Guid.NewGuid();
        _world.Add(table, id, ("owningteam", new EntityReference("team", team)));
        if (storedBusinessUnit is { } bu)
            _world.Set(table, id, "owningbusinessunit", new EntityReference("businessunit", bu));
        return id;
    }

    private void NothingWritten()
    {
        _world.OwnerWrites.Should().BeEmpty("a refusal or a no-op never writes an owner");
        _evictions.OwnerChanges.Should().BeEmpty("nothing written, nothing evicted");
    }

    // ── Re-owned ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "183: an ordinary row on ordinary team A is re-owned to ordinary team B, read back, evicted once")]
    [InlineData("sprk_decisionrecord")]
    [InlineData("sprk_signal")]
    public async Task OrdinaryRow_IsReownedToTheTeam_ReadBack_AndEvictedOnce(string table)
    {
        var id = TeamOwnedRow(table, GeneralTeam);

        var result = await Reconciler().ReownOrdinaryRowAsync(table, id, OtherBuTeam, CancellationToken.None);

        result.Should().Be(new OrdinaryRowReownResult(OrdinaryRowReownOutcome.Reowned, null));
        _world.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(OtherBuTeam));
        _world.OwnerWrites.Should().ContainSingle().Which.Owner.Should().Be(DataversePrincipalRef.Team(OtherBuTeam));
        _evictions.OwnerChanges.Should().Equal(
            (table, SecureChildLineage.Children[table].EntitySet, id, $"reown-ordinary-row:{table}:{id:D}"));
    }

    [Fact(DisplayName = "183: already owned by the team — AlreadyOwned, no write")]
    public async Task AlreadyOwnedByTheTeam_IsAlreadyOwned_AndNothingIsWritten()
    {
        var id = TeamOwnedRow("sprk_decisionrecord", OtherBuTeam);

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_decisionrecord", id, OtherBuTeam, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.AlreadyOwned);
        NothingWritten();
    }

    // ── The Secure guard ─────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "183: a row owned by ANY team in the Secure Record BU is RefusedSecure — no write, no evict")]
    [InlineData("named")]
    [InlineData("family")]   // task 178: a per-family owner team in the Secure BU
    [InlineData("default")]  // the Secure BU's default team (the retired owner)
    public async Task RowOwnedInTheSecureBusinessUnit_IsRefusedSecure(string which)
    {
        var owner = which switch { "named" => SecureTeam, "family" => SecureFamilyTeam, _ => SecureChildShareWorld.SecureDefaultTeam };
        var id = TeamOwnedRow("sprk_signal", owner);

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_signal", id, GeneralTeam, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.RefusedSecure);
        _world.OwnerOf("sprk_signal", id).Should().Be(DataversePrincipalRef.Team(owner));
        NothingWritten();
    }

    [Fact(DisplayName = "183: the owner team's own business unit decides, even when the row's stored business unit says ordinary")]
    public async Task OwnerTeamInTheSecureBusinessUnit_IsRefusedSecure_WhateverTheRowsStoredBusinessUnit()
    {
        // Mutation target: the owner-team half of the out-of-Secure guard (the row's derived unit would catch case above).
        var id = TeamOwnedRow("sprk_decisionrecord", SecureFamilyTeam, storedBusinessUnit: GeneralBu);

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_decisionrecord", id, GeneralTeam, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.RefusedSecure);
        NothingWritten();
    }

    // Beyond the POML's list (one-line justification): a row can be owned across business units, so its OWN business unit
    // being the Secure Record BU is refused too, not only its owner team's.
    [Fact(DisplayName = "183: a row whose own business unit is the Secure Record BU is RefusedSecure, though its owner team is ordinary")]
    public async Task RowsOwnBusinessUnitIsTheSecureBusinessUnit_IsRefusedSecure()
    {
        var id = TeamOwnedRow("sprk_signal", GeneralTeam, storedBusinessUnit: SecureBu);

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_signal", id, OtherBuTeam, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.RefusedSecure);
        NothingWritten();
    }

    [Theory(DisplayName = "183: a target team in the Secure Record BU is RefusedSecure — no write")]
    [InlineData("named")]
    [InlineData("family")]
    public async Task TargetTeamInTheSecureBusinessUnit_IsRefusedSecure(string which)
    {
        var target = which == "named" ? SecureTeam : SecureFamilyTeam;
        var id = TeamOwnedRow("sprk_decisionrecord", GeneralTeam);

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_decisionrecord", id, target, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.RefusedSecure);
        _world.OwnerOf("sprk_decisionrecord", id).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        NothingWritten();
    }

    [Theory(DisplayName = "183: the Secure Record BU ambiguous or unreadable, or the row or a team unreadable — Failed, no write")]
    [InlineData("ambiguous")]
    [InlineData("bu-fault")]
    [InlineData("team-fault")]
    [InlineData("row-fault")]
    [InlineData("team-missing")]
    [InlineData("row-missing")]
    public async Task UnreadableOrAmbiguous_IsFailed_AndNothingIsWritten(string fault)
    {
        var id = TeamOwnedRow("sprk_signal", GeneralTeam);
        var target = OtherBuTeam;
        switch (fault)
        {
            case "ambiguous": _world.Add("businessunit", Guid.NewGuid(), ("name", SecureChildShareWorld.SecureBuName)); break;
            case "bu-fault": _world.FailingQueriesOf("businessunit"); break;
            case "team-fault": _world.FailingQueriesOf("team"); break;
            case "row-fault": _world.FailingRowReadsOf("sprk_signal", id); break;
            case "team-missing": target = Guid.NewGuid(); break;
            case "row-missing": _world.Remove("sprk_signal", id); break;
        }

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_signal", id, target, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.Failed);
        result.Detail.Should().StartWith("refused:");
        NothingWritten();
    }

    [Fact(DisplayName = "183: a user-owned row is RefusedNotTeamOwned — no write")]
    public async Task UserOwnedRow_IsRefusedNotTeamOwned()
    {
        var id = Guid.NewGuid();
        _world.UserOwnedChild("sprk_decisionrecord", id);

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_decisionrecord", id, GeneralTeam, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.RefusedNotTeamOwned);
        NothingWritten();
    }

    // Beyond the POML's list (one-line justification): "no Secure Record BU" is RecordOwnershipResolver's "no business unit
    // is it", not an ambiguity — an environment without secure records still re-owns.
    [Fact(DisplayName = "183: an environment with no Secure Record BU re-owns (no business unit is it)")]
    public async Task NoSecureBusinessUnit_ReownsTheRow()
    {
        var world = SecureChildShareWorld.WithoutSecureBusinessUnit()
            .Add("businessunit", OtherBu, ("name", "Litigation"))
            .Team(OtherBuTeam, OtherBu, "Litigation", isDefault: true, teamType: 0);
        var id = Guid.NewGuid();
        world.Add("sprk_signal", id, ("owningteam", new EntityReference("team", GeneralTeam)));
        var reconciler = SecureChildShareWorld.ReconcilerOver(() => world, new FakeRecordShareTable(), webApi: null!,
            accessCacheInvalidator: _evictions);

        var result = await reconciler.ReownOrdinaryRowAsync("sprk_signal", id, OtherBuTeam, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.Reowned);
        world.OwnerOf("sprk_signal", id).Should().Be(DataversePrincipalRef.Team(OtherBuTeam));
    }

    // ── The contract's arguments ─────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "183: another table, an empty id or an empty team throws ArgumentException")]
    [InlineData("sprk_todo", false, false)]
    [InlineData("sprk_document", false, false)]
    [InlineData("sprk_matter", false, false)]
    [InlineData("", false, false)]
    [InlineData("sprk_signal", true, false)]
    [InlineData("sprk_decisionrecord", false, true)]
    public async Task OtherTableOrEmptyIds_Throw(string table, bool emptyId, bool emptyTeam)
    {
        var id = emptyId ? Guid.Empty : TeamOwnedRow("sprk_signal", GeneralTeam);

        var act = () => Reconciler().ReownOrdinaryRowAsync(table, id, emptyTeam ? Guid.Empty : OtherBuTeam, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        NothingWritten();
    }

    // ── The write is the pass's: the read-back decides ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "183: a write that faults but landed is decided by the read-back — Reowned, evicted once")]
    public async Task WriteThatFaultsButLanded_IsReowned()
    {
        var id = TeamOwnedRow("sprk_signal", GeneralTeam);
        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        entities
            .Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string t, Guid i, Dictionary<string, object> f, CancellationToken _) =>
            {
                _world.Update(t, i, f);
                return Task.FromException(new TimeoutException("Test: the call timed out after the write committed."));
            });

        var result = await Reconciler(entities).ReownOrdinaryRowAsync("sprk_signal", id, OtherBuTeam, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.Reowned);
        _evictions.OwnerChanges.Should().ContainSingle();
    }

    [Fact(DisplayName = "183: a write Dataverse refused does not read back — Failed, evicted once (the attempt can have committed)")]
    public async Task WriteThatWasRefused_IsFailed_AndStillEvictedOnce()
    {
        var id = TeamOwnedRow("sprk_decisionrecord", GeneralTeam);
        _world.RefusingOwnerWritesOf(id);

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_decisionrecord", id, OtherBuTeam, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.Failed);
        result.Detail.Should().StartWith("Dataverse refused the re-own");
        _world.OwnerOf("sprk_decisionrecord", id).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _evictions.OwnerChanges.Should().ContainSingle();
    }

    /// <summary>Records every owner-change eviction, with its reason.</summary>
    private sealed class EvictionRecorder : IMembershipCacheInvalidator
    {
        public ConcurrentQueue<(string Entity, string EntitySet, Guid RecordId, string? Reason)> OwnerChanges { get; } = new();

        public Task PublishInvalidationAsync(Guid personId, string entityLogicalName, string? correlationId, CancellationToken ct) =>
            Task.CompletedTask;

        public Task InvalidateUserAccessAsync(Guid systemUserId, string? correlationId, CancellationToken ct) => Task.CompletedTask;

        public Task InvalidateRecordOwnerChangeAsync(
            string entityLogicalName, string entitySetName, Guid recordId, string? correlationId, CancellationToken ct)
        {
            OwnerChanges.Enqueue((entityLogicalName, entitySetName, recordId, correlationId));
            return Task.CompletedTask;
        }

        public Task InvalidateRecordShareChangeAsync(string entitySetName, Guid recordId, string? correlationId, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
