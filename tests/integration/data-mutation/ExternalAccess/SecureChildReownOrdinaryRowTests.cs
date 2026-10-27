using System.Collections.Concurrent;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk.Query;
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
/// dropped a predicate re-owns a row here. Every refusal asserts NO owner write and NO eviction. The owner write is the Web
/// API's conditional PATCH (<see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>), answered by <see cref="ConditionalOwnerWebApi"/>
/// over the same rows and their <c>versionnumber</c>.
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
    private readonly ConditionalOwnerWebApi _webApi;

    public SecureChildReownOrdinaryRowTests() => _webApi = new ConditionalOwnerWebApi(() => _world);

    private SecureChildReconciler Reconciler(Mock<IGenericEntityService>? entities = null)
    {
        if (entities is null)
            return SecureChildShareWorld.ReconcilerOver(() => _world, new FakeRecordShareTable(), _webApi,
                accessCacheInvalidator: _evictions);

        var configuration = SecureChildShareWorld.Configuration();
        var resolver = new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver(
            entities.Object, configuration, NullLogger<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver>.Instance);
        var synchronizer = new SecureChildShareSynchronizer(entities.Object, new FakeRecordShareTable(),
            SecureChildShareWorld.NobodyWalled(), configuration, NullLogger<SecureChildShareSynchronizer>.Instance);
        return new SecureChildReconciler(entities.Object, resolver, synchronizer, _webApi, configuration,
            NullLogger<SecureChildReconciler>.Instance, new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing, _evictions);
    }

    private Guid TeamOwnedRow(string table, Guid team, Guid? storedBusinessUnit = null)
    {
        var id = Guid.NewGuid();
        _world.Add(table, id, ("owningteam", new EntityReference("team", team)), ("versionnumber", 1L));
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
        _webApi.ConditionalWrites.Should().Equal((SecureChildLineage.Children[table].EntitySet, id, "W/\"1\""));
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

    [Fact(DisplayName = "183 fix F3.1a: already owned by the target team, and that team is in the Secure Record BU — RefusedSecure, not AlreadyOwned")]
    public async Task AlreadyOwnedByASecureTeam_IsRefusedSecure_NotAlreadyOwned()
    {
        var id = TeamOwnedRow("sprk_decisionrecord", SecureFamilyTeam);

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_decisionrecord", id, SecureFamilyTeam, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.RefusedSecure, "the Secure guard runs before the AlreadyOwned short-circuit");
        NothingWritten();
    }

    [Fact(DisplayName = "183 fix F3.1b: already owned by the target team, with the row's own business unit the Secure Record BU — RefusedSecure")]
    public async Task AlreadyOwnedWithTheRowInTheSecureBusinessUnit_IsRefusedSecure_NotAlreadyOwned()
    {
        var id = TeamOwnedRow("sprk_signal", GeneralTeam, storedBusinessUnit: SecureBu);

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_signal", id, GeneralTeam, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.RefusedSecure, "the Secure guard runs before the AlreadyOwned short-circuit");
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
    [InlineData("team-missing")]   // a missing TARGET team stays Failed (refused), not NotFound
    [InlineData("no-version")]     // fix round: no version, no conditional write
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
            case "no-version": _world.Set("sprk_signal", id, "versionnumber", null); break;
        }

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_signal", id, target, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.Failed);
        result.Detail.Should().StartWith("refused:");
        NothingWritten();
        _webApi.ConditionalWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "183 fix F2: a row missing on the guard read is NotFound — no write")]
    public async Task RowMissingOnTheGuardRead_IsNotFound()
    {
        var id = TeamOwnedRow("sprk_signal", GeneralTeam);
        _world.Remove("sprk_signal", id);

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_signal", id, OtherBuTeam, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.NotFound);
        NothingWritten();
        _webApi.ConditionalWrites.Should().BeEmpty();
    }

    // Beyond the POML's list (one-line justification): the other half of NotFound — the row deleted between the guard read
    // and the conditional write (404) — is decided by the write, not by a read-back of a row that is gone.
    [Fact(DisplayName = "183 fix F2: a row deleted between the guard and the write is NotFound — nothing written, nothing evicted")]
    public async Task RowDeletedBeforeTheWrite_IsNotFound()
    {
        var id = TeamOwnedRow("sprk_decisionrecord", GeneralTeam);
        _world.AfterQueriesOf("team", 2, w => w.Remove("sprk_decisionrecord", id));

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_decisionrecord", id, OtherBuTeam, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.NotFound);
        _webApi.ConditionalWrites.Should().ContainSingle("the write was attempted, and refused");
        NothingWritten();
    }

    // ── F1: the guard and the write cannot race ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "183 fix F1: the secure pass moves the row into isolation after the guard — Conflict, nothing written, it stays isolated")]
    public async Task ARowMovedIntoIsolationAfterTheGuard_IsAConflict_AndStaysOnTheSecureTeam()
    {
        var id = TeamOwnedRow("sprk_signal", GeneralTeam);
        // After the guard's last read (the owner team's business unit), before the write: the secure pass re-owns the row
        // to the Secure team, which changes its version.
        _world.AfterQueriesOf("team", 2, w =>
        {
            w.MoveOwner("sprk_signal", id, DataversePrincipalRef.Team(SecureTeam));
            w.Set("sprk_signal", id, "versionnumber", 2L);
        });

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_signal", id, OtherBuTeam, CancellationToken.None);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.Conflict);
        _webApi.ConditionalWrites.Should().Equal(("sprk_signals", id, "W/\"1\""));
        _world.OwnerOf("sprk_signal", id).Should().Be(DataversePrincipalRef.Team(SecureTeam), "never moved back out of isolation");
        NothingWritten();
    }

    // ── F3.2: cancellation ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "183 fix F3.2: a cancelled token throws OperationCanceledException and writes nothing")]
    public async Task ACancelledToken_Throws_AndWritesNothing()
    {
        var id = TeamOwnedRow("sprk_signal", GeneralTeam);
        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken token) =>
                token.IsCancellationRequested
                    ? Task.FromCanceled<EntityCollection>(token)
                    : Task.FromResult(_world.Answer(query)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var act = () => Reconciler(entities).ReownOrdinaryRowAsync("sprk_signal", id, OtherBuTeam, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        NothingWritten();
        _webApi.ConditionalWrites.Should().BeEmpty();
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
        world.Add("sprk_signal", id, ("owningteam", new EntityReference("team", GeneralTeam)), ("versionnumber", 1L));
        var reconciler = SecureChildShareWorld.ReconcilerOver(
            () => world, new FakeRecordShareTable(), new ConditionalOwnerWebApi(() => world), accessCacheInvalidator: _evictions);

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
        _webApi.FaultAfterApply = true;

        var result = await Reconciler().ReownOrdinaryRowAsync("sprk_signal", id, OtherBuTeam, CancellationToken.None);

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

    // ── K4 (fix round): the existing pass's put-back onto a USER start owner ─────────────────────────────────────────────

    /// <summary>
    /// The secure pass's <c>RestoreStartOwnerAsync</c> through the shared write helper: a user-owned memo the unsecure pass
    /// pulled INTO isolation behind an isolated event, whose support then left, goes back on its USER and READS BACK as that
    /// user — so the pass does not report it failed. (The shape of
    /// <c>SecureChildTransitionTests.Unsecure_PutsARowItPulledIntoIsolationBackOnItsOwner_WhenItsSupportLeaves</c>, driven on
    /// the reconciler directly so its report can be read.) A helper that expected a TEAM for every put-back fails here.
    /// </summary>
    [Fact(DisplayName = "183 fix K4: the secure pass puts a pulled-in row back on its USER start owner, and it reads back")]
    public async Task TheSecurePass_PutsARowBackOnItsUserStartOwner_AndItReadsBack()
    {
        var root = Guid.NewGuid();
        var (otherSecure, halfProvisioned) = (Guid.NewGuid(), Guid.NewGuid());
        var (document, analysis, @event, memo) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world
            .FlaggedNotIsolatedRoot("sprk_workassignment", root) // mid-unsecure: moved off the Secure team, flag cleared last
            .SecureRoot("sprk_matter", otherSecure)
            .FlaggedNotIsolatedRoot("sprk_project", halfProvisioned)
            .UserOwnedChild("sprk_document", document,
                ("sprk_workassignment", "sprk_workassignment", root), ("sprk_matter", "sprk_matter", otherSecure))
            .OrdinaryChild("sprk_analysis", analysis,
                ("sprk_documentid", "sprk_document", document), ("sprk_regardingproject", "sprk_project", halfProvisioned))
            .SecureChild("sprk_event", @event, ("sprk_regardinganalysis", "sprk_analysis", analysis))
            .UserOwnedChild("sprk_memo", memo, ("sprk_regardingevent", "sprk_event", @event));

        var report = await Reconciler().ReconcileAsync(
            "sprk_workassignment", root, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        _world.OwnerWrites.Where(w => w.Id == memo).Select(w => w.Owner).Should().Equal(
            DataversePrincipalRef.Team(SecureTeam), DataversePrincipalRef.User(SecureChildShareWorld.SomeUser));
        _world.OwnerOf("sprk_memo", memo).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser));
        report.Changes.Should().NotContain(c => c.Id == memo,
            "put back on its own user and read back as that user: no net change, not a failure");
    }

    /// <summary>
    /// The Web API's conditional PATCH over the world's rows (<see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>'s
    /// production contract): a missing row is 404 (<see cref="KeyNotFoundException"/>), an ETag other than the row's
    /// <c>W/"versionnumber"</c> is 412 (<see cref="System.Data.DBConcurrencyException"/>) — nothing written on either.
    /// Otherwise the owner bind is applied through the world (recorded in <see cref="SecureChildShareWorld.OwnerWrites"/>,
    /// its write faults honoured) and the version moves on. The unconditional <see cref="UpdateAsync"/> applies without
    /// the check (the mutation proof's target).
    /// </summary>
    private sealed class ConditionalOwnerWebApi : DataverseWebApiClient
    {
        private readonly Func<SecureChildShareWorld> _world;

        public ConditionalOwnerWebApi(Func<SecureChildShareWorld> world)
            : base(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                NoCredential.Instance)
            => _world = world;

        public List<(string EntitySet, Guid Id, string ETag)> ConditionalWrites { get; } = new();

        /// <summary>The write lands, then the call faults (a timeout after the commit).</summary>
        public bool FaultAfterApply { get; set; }

        public override Task UpdateIfMatchAsync(
            string entitySetName, Guid id, object entity, string etag, CancellationToken cancellationToken = default)
        {
            ConditionalWrites.Add((entitySetName, id, etag));
            var table = TableOf(entitySetName);
            if (!_world().Has(table, id))
                throw new KeyNotFoundException($"{entitySetName}({id}) was not found; nothing was created.");
            if (etag != $"W/\"{_world().ValueOf<long>(table, id, "versionnumber")}\"")
                throw new System.Data.DBConcurrencyException($"{entitySetName}({id}) changed since it was read; the update was not applied.");
            return Apply(table, id, entity);
        }

        public override Task UpdateAsync(string entitySetName, Guid id, object entity, CancellationToken cancellationToken = default)
            => Apply(TableOf(entitySetName), id, entity);

        private Task Apply(string table, Guid id, object entity)
        {
            var fields = (IDictionary<string, object>)entity;
            fields.Keys.Should().Equal(new[] { "ownerid@odata.bind" }, "the re-own writes the owner and nothing else");
            var bind = (string)fields["ownerid@odata.bind"];
            bind.Should().StartWith("/teams(");
            var team = Guid.Parse(bind["/teams(".Length..^1]);
            _world().Update(table, id, new Dictionary<string, object> { ["ownerid"] = new EntityReference("team", team) });
            _world().Set(table, id, "versionnumber", _world().ValueOf<long>(table, id, "versionnumber") + 1);
            return FaultAfterApply
                ? Task.FromException(new HttpRequestException("Test: the call timed out after the write committed."))
                : Task.CompletedTask;
        }

        private static string TableOf(string entitySetName) =>
            SecureChildLineage.Children.Values.Single(t => t.EntitySet == entitySetName).LogicalName;
    }

    private sealed class NoCredential : TokenCredential
    {
        public static readonly NoCredential Instance = new();

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");
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
