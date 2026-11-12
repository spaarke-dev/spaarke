using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 191c (#2179 design §3.4, §3.5, §3.7, §3.8) — the External Collaborators team's shares follow
/// the visibility predicate (task 191b): Read + AppendTo on the seven tables partners file under, Read on the other lineage
/// tables, nothing on the never-shared tables, on a Secure family or on any row the predicate does not show; flagged users'
/// per-user shares on hidden children end; rows a flagged user owns are re-owned (never to the External team); everything is
/// report-only until its setting is on.
/// </summary>
/// <remarks>
/// <para><b>What runs for real.</b> The REAL <see cref="ExternalTeamShareSynchronizer"/> (and, for the mirror's filter, the REAL
/// <see cref="SecureChildShareSynchronizer"/>; for the re-own, the REAL <see cref="RecordOwnershipResolver"/>) over
/// <see cref="SecureChildShareWorld"/> (an in-memory Dataverse that evaluates each query) and <see cref="FakeRecordShareTable"/>
/// (the strict POA double). The predicate is the one substituted seam (<see cref="ExternalVisibilityDecider"/>): its own rules
/// are task 191b's tests; here it answers what each test says.</para>
/// <para><b>Masks are Dataverse's numbers</b>: Read 1, AppendTo 16.</para>
/// </remarks>
public class ExternalTeamShareTests
{
    private const int Read = 1;
    private const int ReadAppendTo = 17;
    private const string ReadAppendToCsv = "ReadAccess,AppendToAccess";
    private const string ReadCsv = "ReadAccess";

    private static readonly Guid ExternalBu = Guid.Parse("e7000000-0191-4c00-8000-000000000001");
    private static readonly Guid ExternalTeam = Guid.Parse("e7000000-0191-4c00-8000-000000000002");
    private static readonly Guid DirectBu = Guid.Parse("e7000000-0191-4c00-8000-000000000003");
    private static readonly Guid DirectTeam = Guid.Parse("e7000000-0191-4c00-8000-000000000004");
    private static readonly Guid Partner = Guid.Parse("e7000000-0191-4c00-8000-00000000000a");
    private static readonly Guid Colleague = Guid.Parse("e7000000-0191-4c00-8000-00000000000b");

    private static readonly Guid Matter = Guid.Parse("e7100000-0191-4c00-8000-000000000001");
    private static readonly Guid SecureMatter = Guid.Parse("e7100000-0191-4c00-8000-000000000002");
    private static readonly Guid FlaggedMatter = Guid.Parse("e7100000-0191-4c00-8000-000000000003");
    private static readonly Guid MaskedMatter = Guid.Parse("e7100000-0191-4c00-8000-000000000004");
    private static readonly Guid EventA = Guid.Parse("e7200000-0191-4c00-8000-000000000001");
    private static readonly Guid EventB = Guid.Parse("e7200000-0191-4c00-8000-000000000002");
    private static readonly Guid EventOwnedBySecureTeam = Guid.Parse("e7200000-0191-4c00-8000-000000000003");
    private static readonly Guid EventUnderFlagged = Guid.Parse("e7200000-0191-4c00-8000-000000000004");
    private static readonly Guid EventUnderMasked = Guid.Parse("e7200000-0191-4c00-8000-000000000005");
    private static readonly Guid EventOwnedByPartner = Guid.Parse("e7200000-0191-4c00-8000-000000000006");
    private static readonly Guid UnfiledEventOwnedByPartner = Guid.Parse("e7200000-0191-4c00-8000-000000000007");
    private static readonly Guid EventOwnedByExternalTeam = Guid.Parse("e7200000-0191-4c00-8000-000000000008");
    private static readonly Guid Doc = Guid.Parse("e7300000-0191-4c00-8000-000000000001");
    private static readonly Guid FileVersion = Guid.Parse("e7400000-0191-4c00-8000-000000000001");
    private static readonly Guid Invoice = Guid.Parse("e7500000-0191-4c00-8000-000000000001");
    private static readonly Guid Policy = Guid.Parse("e7600000-0191-4c00-8000-000000000001");
    private static readonly Guid DirectMessage = Guid.Parse("e7700000-0191-4c00-8000-000000000001");

    private static DataversePrincipalRef Team(Guid id) => DataversePrincipalRef.Team(id);
    private static DataversePrincipalRef User(Guid id) => DataversePrincipalRef.User(id);

    private readonly FakeRecordShareTable _shares = new();
    private readonly FakePredicate _predicate = new();

    // ── The world ───────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The standard world plus the External Collaborators unit and its default team, a partner flagged external, an internal
    /// colleague (stored false), an ordinary matter with an event, a document, its file version, an invoice and a policy under
    /// it; the Secure-family cases; rows a partner owns.
    /// </summary>
    private static SecureChildShareWorld World(bool withExternalUnit = true)
    {
        var world = SecureChildShareWorld.Standard();
        if (withExternalUnit)
        {
            world.Add("businessunit", ExternalBu, ("name", "External Collaborators"),
                ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
            world.Team(ExternalTeam, ExternalBu, "External Collaborators", isDefault: true, teamType: 0);
        }

        world.Add("systemuser", Partner, ("businessunitid", new EntityReference("businessunit", withExternalUnit ? ExternalBu : SecureChildShareWorld.GeneralBu)),
            ("sprk_isexternal", true));
        world.Add("systemuser", Colleague, ("businessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)),
            ("sprk_isexternal", false));

        return world
            .OrdinaryRoot("sprk_matter", Matter)
            .SecureRoot("sprk_matter", SecureMatter)
            .FlaggedNotIsolatedRoot("sprk_matter", FlaggedMatter)
            .MaskedFlagNotIsolatedRoot("sprk_matter", MaskedMatter)
            .OrdinaryChild("sprk_event", EventA, ("sprk_regardingmatter", "sprk_matter", Matter))
            .OrdinaryChild("sprk_event", EventB, ("sprk_regardingmatter", "sprk_matter", Matter))
            .SecureChild("sprk_event", EventOwnedBySecureTeam, ("sprk_regardingmatter", "sprk_matter", Matter))
            .OrdinaryChild("sprk_event", EventUnderFlagged, ("sprk_regardingmatter", "sprk_matter", FlaggedMatter))
            .OrdinaryChild("sprk_event", EventUnderMasked, ("sprk_regardingmatter", "sprk_matter", MaskedMatter))
            .OrdinaryChild("sprk_document", Doc, ("sprk_matter", "sprk_matter", Matter))
            .OrdinaryChild("sprk_fileversion", FileVersion, ("sprk_document", "sprk_document", Doc))
            .OrdinaryChild("sprk_invoice", Invoice, ("sprk_matter", "sprk_matter", Matter))
            .OrdinaryChild("sprk_policy", Policy, ("sprk_matter", "sprk_matter", Matter))
            .Add("sprk_event", EventOwnedByPartner, ("owninguser", new EntityReference("systemuser", Partner)),
                ("sprk_regardingmatter", new EntityReference("sprk_matter", Matter)))
            .Add("sprk_event", UnfiledEventOwnedByPartner, ("owninguser", new EntityReference("systemuser", Partner)))
            .Add("sprk_event", EventOwnedByExternalTeam, ("owningteam", new EntityReference("team", ExternalTeam)),
                ("sprk_regardingmatter", new EntityReference("sprk_matter", Matter)))
            .Add("sprk_communication", DirectMessage, ("owningteam", new EntityReference("team", DirectTeam)));
    }

    private static IConfiguration Config(bool writes = true, bool reown = false, string? customerBu = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecureRecord:BusinessUnitName"] = SecureChildShareWorld.SecureBuName,
            ["SecureRecord:OwnerTeamName"] = SecureChildShareWorld.SecureOwnerTeamName,
            [ExternalTeamShareSynchronizer.WritesEnabledConfigKey] = writes ? "true" : "false",
            [ExternalTeamShareSynchronizer.ReownWritesEnabledConfigKey] = reown ? "true" : null,
            ["ExternalAccess:CustomerBusinessUnitId"] = customerBu,
        }).Build();

    private ExternalTeamShareSynchronizer Engine(
        SecureChildShareWorld world, IConfiguration configuration,
        Func<CancellationToken, Task<DirectMessageOwnerTeamIdentity>>? directTeam = null)
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => world).Object;
        return new ExternalTeamShareSynchronizer(
            entities, _shares, _predicate.Decide, configuration, NullLogger<ExternalTeamShareSynchronizer>.Instance,
            ownership: new RecordOwnershipResolver(entities, configuration, NullLogger<RecordOwnershipResolver>.Instance))
        {
            DirectTeamIdentity = directTeam ?? (_ => Task.FromResult(new DirectMessageOwnerTeamIdentity(DirectBu, DirectTeam, null, null))),
        };
    }

    /// <summary>The predicate seam: Visible unless a test says otherwise; a child's roots default to <see cref="Matter"/>, a
    /// root's to itself.</summary>
    private sealed class FakePredicate
    {
        private readonly Dictionary<(string, Guid), (ExternalVisibilityVerdict Verdict, ExternalVisibilityReason Reason)> _verdicts = new();
        private readonly Dictionary<(string, Guid), (string Table, Guid Id)[]> _roots = new();
        private readonly HashSet<(string, Guid)> _rootsUnknown = new();
        private readonly HashSet<(string, Guid)> _effectivelySecure = new();
        private readonly HashSet<(string, Guid)> _faultsWhenAsked = new();

        public bool Faults { get; set; }

        /// <summary>Runs on every decision (a test that cancels mid-pass).</summary>
        public Action? OnDecide { get; set; }

        /// <summary>The number of decisions asked for (one per call).</summary>
        public int Calls { get; private set; }

        /// <summary>The root's effective flags say Secure (Secure inherited from an ancestor root, the stored flag still false).</summary>
        public FakePredicate SecureByInheritance(string table, Guid id)
        {
            _effectivelySecure.Add((table, id));
            return this;
        }

        /// <summary>Any decision whose batch includes this row faults (one bad row).</summary>
        public FakePredicate FaultsWhenAsked(string table, Guid id)
        {
            _faultsWhenAsked.Add((table, id));
            return this;
        }

        public List<(string Table, Guid Id)> Asked { get; } = new();

        public FakePredicate Says(string table, Guid id, ExternalVisibilityVerdict verdict, ExternalVisibilityReason reason)
        {
            _verdicts[(table, id)] = (verdict, reason);
            return this;
        }

        public FakePredicate Hides(string table, Guid id) =>
            Says(table, id, ExternalVisibilityVerdict.Hidden, ExternalVisibilityReason.RootRestricted);

        public FakePredicate RootsOf(string table, Guid id, params (string Table, Guid Id)[] roots)
        {
            _roots[(table, id)] = roots;
            return this;
        }

        /// <summary>The decision does not carry this row's roots (fail closed: the family cannot be told).</summary>
        public FakePredicate RootsUnknownFor(string table, Guid id)
        {
            _rootsUnknown.Add((table, id));
            return this;
        }

        public Task<ExternalVisibilityResult> Decide(IReadOnlyCollection<(string Table, Guid Id)> rows, CancellationToken ct)
        {
            Asked.AddRange(rows);
            Calls++;
            OnDecide?.Invoke();
            ct.ThrowIfCancellationRequested();
            if (Faults || rows.Any(r => _faultsWhenAsked.Contains(r)))
                throw new InvalidOperationException("Test: the predicate's reads faulted.");

            var decided = new Dictionary<ExternalVisibility.Node, ExternalVisibilityDecision>();
            var reached = new Dictionary<ExternalVisibility.Node, IReadOnlyCollection<ExternalVisibility.Node>>();
            foreach (var (table, id) in rows)
            {
                var node = new ExternalVisibility.Node(table, id);
                var (verdict, reason) = _verdicts.TryGetValue((table, id), out var said)
                    ? said
                    : (ExternalVisibilityVerdict.Visible, ExternalVisibilityReason.Visible);
                decided[node] = new ExternalVisibilityDecision(verdict, reason, table, id);
                if (_rootsUnknown.Contains((table, id)))
                    continue;
                var roots = _roots.TryGetValue((table, id), out var given)
                    ? given
                    : SecureChildLineage.IsRoot(table) ? [(table, id)] : [("sprk_matter", Matter)];
                reached[node] = roots.Select(r => new ExternalVisibility.Node(r.Table, r.Id)).ToArray();
            }

            return Task.FromResult(new ExternalVisibilityResult(decided, faulted: false)
            {
                RootsReached = reached,
                EffectivelySecureRoots = reached.Values.SelectMany(r => r)
                    .Where(n => _effectivelySecure.Contains((n.Table, n.Id))).ToHashSet(),
            });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Grants: exact masks, read back
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Sync_VisibleRows_GrantTheTeamExactlyReadAppendToOnFilingTablesAndReadElsewhere()
    {
        var result = await Engine(World(), Config()).SyncRowsAsync(
            [("sprk_matter", Matter), ("sprk_event", EventA), ("sprk_document", Doc), ("sprk_fileversion", FileVersion)],
            "test", CancellationToken.None);

        result.Status.Should().Be(ExternalTeamShareStatus.Completed);
        _shares.MaskOf("sprk_matter", Matter, Team(ExternalTeam)).Should().Be(ReadAppendTo);
        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().Be(ReadAppendTo);
        _shares.MaskOf("sprk_document", Doc, Team(ExternalTeam)).Should().Be(ReadAppendTo);
        _shares.MaskOf("sprk_fileversion", FileVersion, Team(ExternalTeam)).Should().Be(Read, "a file version is not a table partners file under");
        _shares.WriteLog.Should().OnlyContain(w => w.Action == "GrantAccess" && w.Principal == Team(ExternalTeam));
        _shares.WriteLog.Single(w => w.RecordId == FileVersion).Rights.Should().Be(ReadCsv);
        _shares.WriteLog.Where(w => w.RecordId != FileVersion).Should().OnlyContain(w => w.Rights == ReadAppendToCsv);
        result.Written.Should().Be(4, "every grant read back as written");
        result.Shareable.Should().Be(4);
    }

    [Fact]
    public async Task Sync_AGrantThatDoesNotReadBack_IsCarried()
    {
        _shares.IgnoreWrites = true;

        var result = await Engine(World(), Config()).SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);

        result.Status.Should().Be(ExternalTeamShareStatus.Incomplete);
        result.Carried.Should().Contain(("sprk_event", EventA));
        result.Written.Should().Be(0);
    }

    [Fact]
    public async Task Sync_ATeamShareAtTheWrongMask_IsChangedToTheTablesMask()
    {
        _shares.Seed("sprk_event", EventA, Team(ExternalTeam), Read);
        _shares.Seed("sprk_fileversion", FileVersion, Team(ExternalTeam), ReadAppendTo);

        await Engine(World(), Config()).SyncRowsAsync(
            [("sprk_event", EventA), ("sprk_fileversion", FileVersion)], "test", CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().Be(ReadAppendTo);
        _shares.MaskOf("sprk_fileversion", FileVersion, Team(ExternalTeam)).Should().Be(Read, "a wider share is narrowed");
        _shares.WriteLog.Should().OnlyContain(w => w.Action == "ModifyAccess");
    }

    [Fact]
    public async Task Sync_ARowAlreadySharedAtItsMask_IsNotWritten()
    {
        _shares.Seed("sprk_event", EventA, Team(ExternalTeam), ReadAppendTo);

        var result = await Engine(World(), Config()).SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);

        result.Status.Should().Be(ExternalTeamShareStatus.Completed);
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_ARowThatBecomesVisibleAgain_IsGrantedAgain()
    {
        _shares.Seed("sprk_event", EventA, Team(ExternalTeam), ReadAppendTo);
        _predicate.Hides("sprk_event", EventA);
        var engine = Engine(World(), Config());
        await engine.SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);
        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().BeNull();

        _predicate.Says("sprk_event", EventA, ExternalVisibilityVerdict.Visible, ExternalVisibilityReason.Visible);
        await engine.SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().Be(ReadAppendTo);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Revokes: hidden, undecided, secure, never-shared
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData((int)ExternalVisibilityVerdict.Hidden, (int)ExternalVisibilityReason.RootRestricted, false)]
    [InlineData((int)ExternalVisibilityVerdict.Hidden, (int)ExternalVisibilityReason.HardDeny, false)]
    [InlineData((int)ExternalVisibilityVerdict.Hidden, (int)ExternalVisibilityReason.DocumentHidden, false)]
    [InlineData((int)ExternalVisibilityVerdict.Undecided, (int)ExternalVisibilityReason.ReadFailed, true)]
    [InlineData((int)ExternalVisibilityVerdict.Undecided, (int)ExternalVisibilityReason.RootUnreadable, true)]
    [InlineData((int)ExternalVisibilityVerdict.Undecided, (int)ExternalVisibilityReason.NoRoot, false)]
    [InlineData((int)ExternalVisibilityVerdict.Undecided, (int)ExternalVisibilityReason.TooDeep, false)]
    public async Task Sync_ARowThePredicateDoesNotShow_LosesTheTeamShare_AndATransientOneIsCarried(
        int verdict, int reason, bool carried)
    {
        _shares.Seed("sprk_event", EventA, Team(ExternalTeam), ReadAppendTo);
        _predicate.Says("sprk_event", EventA, (ExternalVisibilityVerdict)verdict, (ExternalVisibilityReason)reason);

        var result = await Engine(World(), Config()).SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().BeNull("only Visible shows a row");
        _shares.WriteLog.Should().ContainSingle().Which.Action.Should().Be("RevokeAccess");
        result.Carried.Contains(("sprk_event", EventA)).Should().Be(carried);
    }

    [Fact]
    public async Task Sync_AnUndecidedRowWithNoShare_IsNeverGranted()
    {
        _predicate.Says("sprk_event", EventA, ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.ReadFailed);

        await Engine(World(), Config()).SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);

        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_APredicateThatFaults_GrantsNothing_RevokesTheTeam_AndCarriesTheRows()
    {
        _shares.Seed("sprk_event", EventA, Team(ExternalTeam), ReadAppendTo);
        _predicate.Faults = true;

        var result = await Engine(World(), Config()).SyncRowsAsync(
            [("sprk_event", EventA), ("sprk_event", EventB)], "test", CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().BeNull("an undecided row fails closed");
        _shares.WriteLog.Should().NotContain(w => w.Action == "GrantAccess");
        result.Carried.Should().Contain([("sprk_event", EventA), ("sprk_event", EventB)]);
    }

    public static TheoryData<string, Guid, string> SecureFamilies => new()
    {
        { "sprk_event", EventOwnedBySecureTeam, "owned by the Secure Record owner team" },
        { "sprk_event", EventUnderFlagged, "filed under a root flagged secure (not yet isolated)" },
        { "sprk_event", EventUnderMasked, "filed under a root whose flag reads empty (fail closed)" },
        { "sprk_matter", SecureMatter, "a secure root" },
        { "sprk_matter", MaskedMatter, "a root whose flag reads empty" },
    };

    [Theory]
    [MemberData(nameof(SecureFamilies))]
    public async Task Sync_ASecureFamilyRow_IsNeverTeamShared_EvenWhenThePredicateShowsIt(string table, Guid id, string why)
    {
        _shares.Seed(table, id, Team(ExternalTeam), ReadAppendTo);
        if (id == EventUnderFlagged)
            _predicate.RootsOf(table, id, ("sprk_matter", FlaggedMatter));
        if (id == EventUnderMasked)
            _predicate.RootsOf(table, id, ("sprk_matter", MaskedMatter));

        var result = await Engine(World(), Config()).SyncRowsAsync([(table, id)], "test", CancellationToken.None);

        _shares.MaskOf(table, id, Team(ExternalTeam)).Should().BeNull(why);
        _shares.WriteLog.Should().NotContain(w => w.Action == "GrantAccess");
        result.SecureFamily.Should().Be(1);
    }

    [Fact]
    public async Task Sync_ARowWhoseRootsTheDecisionDoesNotCarry_IsTreatedAsSecure()
    {
        _predicate.RootsUnknownFor("sprk_event", EventA);

        var result = await Engine(World(), Config()).SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);

        _shares.WriteLog.Should().BeEmpty("a family that cannot be told is never shared");
        result.SecureFamily.Should().Be(1);
    }

    [Theory]
    [InlineData("sprk_invoice")]
    [InlineData("sprk_policy")]
    public async Task Sync_ANeverSharedTable_HasNoTeamShare_WhateverThePredicateSays(string table)
    {
        var id = table == "sprk_invoice" ? Invoice : Policy;
        _shares.Seed(table, id, Team(ExternalTeam), Read);

        await Engine(World(), Config()).SyncRowsAsync([(table, id)], "test", CancellationToken.None);

        _shares.MaskOf(table, id, Team(ExternalTeam)).Should().BeNull();
        _shares.WriteLog.Should().ContainSingle().Which.Action.Should().Be("RevokeAccess");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Per-user shares: flagged users only, children only
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Sync_AHiddenChild_LosesAFlaggedUsersShare_AndKeepsAnInternalUsersShare()
    {
        _shares.Seed("sprk_event", EventA, User(Partner), Read);
        _shares.Seed("sprk_event", EventA, User(Colleague), Read);
        _predicate.Hides("sprk_event", EventA);

        var result = await Engine(World(), Config()).SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, User(Partner)).Should().BeNull();
        _shares.MaskOf("sprk_event", EventA, User(Colleague)).Should().Be(Read, "a user whose stored flag is false is internal");
        result.FlaggedUserRevokes.Should().Be(1);
        result.Written.Should().Be(1);
    }

    [Fact]
    public async Task Sync_AVisibleChild_KeepsAFlaggedUsersShare()
    {
        _shares.Seed("sprk_event", EventA, User(Partner), Read);

        await Engine(World(), Config()).SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, User(Partner)).Should().Be(Read);
    }

    [Fact]
    public async Task Sync_AHiddenRoot_KeepsAFlaggedUsersShare_ForTheRestrictedRemover()
    {
        _shares.Seed("sprk_matter", Matter, User(Partner), Read);
        _predicate.Hides("sprk_matter", Matter);

        await Engine(World(), Config()).SyncRowsAsync([("sprk_matter", Matter)], "test", CancellationToken.None);

        _shares.MaskOf("sprk_matter", Matter, User(Partner)).Should().Be(Read, "a root's per-user shares are I-14's");
    }

    [Fact]
    public async Task Sync_AHiddenSecureTeamOwnedChild_KeepsAFlaggedUsersShare_ForTheMirror()
    {
        _shares.Seed("sprk_event", EventOwnedBySecureTeam, User(Partner), Read);
        _predicate.Hides("sprk_event", EventOwnedBySecureTeam);

        await Engine(World(), Config()).SyncRowsAsync([("sprk_event", EventOwnedBySecureTeam)], "test", CancellationToken.None);

        _shares.MaskOf("sprk_event", EventOwnedBySecureTeam, User(Partner)).Should().Be(Read);
    }

    [Fact]
    public async Task Sync_AFlaggedUserWhoOwnsTheHiddenChild_IsRevokedAsTheOwner()
    {
        _shares.Seed("sprk_event", EventOwnedByPartner, User(Partner), Read);
        _shares.OwningUsers[EventOwnedByPartner] = Partner; // an app-only revoke of the owner's own share is refused
        _predicate.Hides("sprk_event", EventOwnedByPartner);

        var result = await Engine(World(), Config()).SyncRowsAsync([("sprk_event", EventOwnedByPartner)], "test", CancellationToken.None);

        _shares.RevokesAsOwner.Should().ContainSingle().Which.Should().Be((EventOwnedByPartner, User(Partner)));
        _shares.MaskOf("sprk_event", EventOwnedByPartner, User(Partner)).Should().BeNull();
        result.Carried.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_AHiddenDirectMessage_KeepsItsFlaggedParticipantsShare_AndReportsIt()
    {
        _shares.Seed("sprk_communication", DirectMessage, User(Partner), Read);
        _predicate.Says("sprk_communication", DirectMessage, ExternalVisibilityVerdict.Hidden, ExternalVisibilityReason.CommunicationHidden);

        var result = await Engine(World(), Config()).SyncRowsAsync([("sprk_communication", DirectMessage)], "test", CancellationToken.None);

        _shares.MaskOf("sprk_communication", DirectMessage, User(Partner)).Should().Be(Read, "task 198's writer owns Direct shares");
        result.DirectOwnedFlaggedShares.Should().Be(1);
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_WhenTheDirectTeamCannotBeTold_FlaggedRevokesAreWithheldAndCarried()
    {
        _shares.Seed("sprk_event", EventA, User(Partner), Read);
        _predicate.Hides("sprk_event", EventA);
        var unreadable = new DirectMessageOwnerTeamIdentity(null, null, "direct_message_owner_team_unreadable", new InvalidOperationException("Test"));

        var result = await Engine(World(), Config(), _ => Task.FromResult(unreadable))
            .SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, User(Partner)).Should().Be(Read);
        result.FlaggedRevokesWithheld.Should().Be(1);
        result.Carried.Should().Contain(("sprk_event", EventA));
    }

    [Fact]
    public async Task Sync_WithNoExternalUnit_StillRevokesFlaggedUsers_AndWritesNoTeamShare()
    {
        _shares.Seed("sprk_event", EventA, User(Partner), Read);
        _predicate.Hides("sprk_event", EventA);

        var result = await Engine(World(withExternalUnit: false), Config()).SyncRowsAsync(
            [("sprk_event", EventA), ("sprk_event", EventB)], "test", CancellationToken.None);

        result.Status.Should().Be(ExternalTeamShareStatus.Completed);
        _shares.MaskOf("sprk_event", EventA, User(Partner)).Should().BeNull();
        _shares.WriteLog.Should().ContainSingle().Which.Principal.Should().Be(User(Partner));
        result.Written.Should().Be(1);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Report-only, prerequisites, the census alarm
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Sync_WithTheSettingOff_WritesNothing_AndReportsThePlanPerTable()
    {
        _shares.Seed("sprk_event", EventB, Team(ExternalTeam), ReadAppendTo);
        _shares.Seed("sprk_event", EventB, User(Partner), Read);
        _predicate.Hides("sprk_event", EventB);

        var result = await Engine(World(), Config(writes: false)).SyncRowsAsync(
            [("sprk_event", EventA), ("sprk_event", EventB), ("sprk_fileversion", FileVersion)], "test", CancellationToken.None);

        _shares.WriteLog.Should().BeEmpty();
        result.WritesEnabled.Should().BeFalse();
        result.Written.Should().Be(0);
        var events = result.Tables.Single(t => t.Table == "sprk_event");
        events.TeamGrants.Should().Be(1);
        events.TeamRevokes.Should().Be(1);
        events.FlaggedUserRevokes.Should().Be(1);
        events.GrantRows.Should().Equal(EventA);
        events.RevokeRows.Should().Equal(EventB);
        result.Tables.Single(t => t.Table == "sprk_fileversion").TeamGrants.Should().Be(1);
    }

    [Fact]
    public async Task Sync_WhenTwoUnitsCarryTheExternalName_DecidesAndWritesNothing()
    {
        var world = World();
        world.Add("businessunit", Guid.NewGuid(), ("name", "External Collaborators"));
        _shares.Seed("sprk_event", EventA, Team(ExternalTeam), ReadAppendTo);
        _predicate.Hides("sprk_event", EventA);

        var result = await Engine(world, Config()).SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);

        result.Status.Should().Be(ExternalTeamShareStatus.Failed);
        result.Carried.Should().Contain(("sprk_event", EventA));
        _shares.WriteLog.Should().BeEmpty();
        _predicate.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_WhenTheExternalUnitHasNoDefaultTeam_DecidesAndWritesNothing()
    {
        var world = SecureChildShareWorld.Standard()
            .Add("businessunit", ExternalBu, ("name", "External Collaborators"))
            .OrdinaryRoot("sprk_matter", Matter)
            .OrdinaryChild("sprk_event", EventA, ("sprk_regardingmatter", "sprk_matter", Matter));

        var result = await Engine(world, Config()).SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);

        result.Status.Should().Be(ExternalTeamShareStatus.Failed);
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_WhenTheTeamsCannotBeRead_DecidesAndWritesNothing()
    {
        var world = World().FailingQueriesOf("businessunit");

        var result = await Engine(world, Config()).SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);

        result.Status.Should().Be(ExternalTeamShareStatus.Failed);
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_ARowTheExternalTeamOwns_RaisesTheCensusAlarm()
    {
        var result = await Engine(World(), Config(writes: false)).SyncRowsAsync(
            [("sprk_event", EventOwnedByExternalTeam), ("sprk_event", EventA)], "test", CancellationToken.None);

        result.ExternalTeamOwned.Should().Be(1);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Re-own: rows a flagged user owns
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Sync_AFiledRowAFlaggedUserOwns_IsReownedToItsParentsTeam()
    {
        var world = World();

        var result = await Engine(world, Config(reown: true)).SyncRowsAsync(
            [("sprk_event", EventOwnedByPartner)], "test", CancellationToken.None);

        world.OwnerOf("sprk_event", EventOwnedByPartner).Should().Be(Team(SecureChildShareWorld.GeneralTeam));
        result.Reowned.Should().Be(1);
    }

    [Fact]
    public async Task Sync_AnUnfiledRowAFlaggedUserOwns_IsReownedInTheCustomerUnit_NeverTheirOwn()
    {
        var world = World();
        _predicate.Says("sprk_event", UnfiledEventOwnedByPartner, ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.NoRoot);

        var result = await Engine(world, Config(reown: true, customerBu: SecureChildShareWorld.CustomerBu.ToString()))
            .SyncRowsAsync([("sprk_event", UnfiledEventOwnedByPartner)], "test", CancellationToken.None);

        world.OwnerOf("sprk_event", UnfiledEventOwnedByPartner).Should().Be(Team(SecureChildShareWorld.CustomerTeam));
        result.Reowned.Should().Be(1);
    }

    [Fact]
    public async Task Sync_AnUnfiledRowAFlaggedUserOwns_WithNoCustomerUnit_IsCarriedNotReowned()
    {
        var world = World();
        _predicate.Says("sprk_event", UnfiledEventOwnedByPartner, ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.NoRoot);

        var result = await Engine(world, Config(reown: true)).SyncRowsAsync(
            [("sprk_event", UnfiledEventOwnedByPartner)], "test", CancellationToken.None);

        world.OwnerWrites.Should().BeEmpty("the external users' own unit never owns their record");
        result.Carried.Should().Contain(("sprk_event", UnfiledEventOwnedByPartner));
    }

    [Fact]
    public async Task Sync_AReownWhoseAnswerIsTheExternalTeam_IsRefused()
    {
        var world = World();
        _predicate.Says("sprk_event", UnfiledEventOwnedByPartner, ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.NoRoot);

        // A misconfigured customer unit that IS the External Collaborators unit.
        var result = await Engine(world, Config(reown: true, customerBu: ExternalBu.ToString()))
            .SyncRowsAsync([("sprk_event", UnfiledEventOwnedByPartner)], "test", CancellationToken.None);

        world.OwnerWrites.Should().BeEmpty();
        result.Carried.Should().Contain(("sprk_event", UnfiledEventOwnedByPartner));
    }

    [Fact]
    public async Task Sync_WithReownOff_ReportsTheReownAndWritesNoOwner()
    {
        var world = World();

        var result = await Engine(world, Config(reown: false)).SyncRowsAsync(
            [("sprk_event", EventOwnedByPartner)], "test", CancellationToken.None);

        world.OwnerWrites.Should().BeEmpty();
        result.Tables.Single(t => t.Table == "sprk_event").Reowns.Should().Be(1);
        result.ReownWritesEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Sync_ARowAnInternalUserOwns_IsNotReowned()
    {
        var world = World().Add("sprk_event", EventB, ("owninguser", new EntityReference("systemuser", Colleague)),
            ("sprk_regardingmatter", new EntityReference("sprk_matter", Matter)));

        var result = await Engine(world, Config(reown: true)).SyncRowsAsync([("sprk_event", EventB)], "test", CancellationToken.None);

        world.OwnerWrites.Should().BeEmpty();
        result.Reowns.Should().Be(0);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The family walk and the listings
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SyncFamily_FromARoot_ReachesEveryRowFiledUnderIt()
    {
        var result = await Engine(World(), Config()).SyncFamilyAsync("sprk_matter", Matter, "test", CancellationToken.None);

        _shares.MaskOf("sprk_matter", Matter, Team(ExternalTeam)).Should().Be(ReadAppendTo);
        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().Be(ReadAppendTo);
        _shares.MaskOf("sprk_document", Doc, Team(ExternalTeam)).Should().Be(ReadAppendTo);
        _shares.MaskOf("sprk_fileversion", FileVersion, Team(ExternalTeam)).Should().Be(Read, "a grandchild, through the document");
        _shares.MaskOf("sprk_invoice", Invoice, Team(ExternalTeam)).Should().BeNull();
        _shares.MaskOf("sprk_event", EventOwnedBySecureTeam, Team(ExternalTeam)).Should().BeNull();
        _shares.MaskOf("sprk_event", EventUnderFlagged, Team(ExternalTeam)).Should().BeNull("not filed under this matter");
        _predicate.Asked.Should().Contain(("sprk_fileversion", FileVersion));
    }

    [Fact]
    public async Task ListModifiedSince_ReturnsTheRowsChangedSinceTheWatermark_AndHoldsACappedTable()
    {
        var since = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var world = World()
            .Modified("sprk_event", EventA, since.AddMinutes(1))
            .Modified("sprk_event", EventB, since.AddMinutes(2))
            .Modified("sprk_document", Doc, since.AddMinutes(-1));

        var (rows, capped) = await Engine(world, Config()).ListModifiedSinceAsync(
            new Dictionary<string, (DateTime At, Guid AfterId)>(), since, perTableCap: 1, CancellationToken.None);

        rows.Should().Equal(("sprk_event", EventA));
        capped.Should().ContainKey("sprk_event").WhoseValue.Should().Be((since.AddMinutes(1), EventA));
    }

    [Fact]
    public async Task ListSweepPage_PagesThroughEveryLineageTable_ThenEnds()
    {
        var engine = Engine(World(), Config());
        var all = new List<(string Table, Guid Id)>();
        (string Table, Guid After)? cursor = null;
        for (var page = 0; page < 200; page++)
        {
            var (rows, next) = await engine.ListSweepPageAsync(cursor, pageSize: 3, CancellationToken.None);
            all.AddRange(rows);
            if (next is null)
                break;
            cursor = next;
        }

        all.Should().OnlyHaveUniqueItems();
        all.Should().Contain([("sprk_matter", Matter), ("sprk_event", EventA), ("sprk_fileversion", FileVersion), ("sprk_invoice", Invoice)]);
        all.Count(r => r.Table == "sprk_event").Should().Be(8);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The secure mirror's flagged-principal filter
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static readonly Guid SecureProject = Guid.Parse("e7800000-0191-4c00-8000-000000000001");
    private static readonly Guid SecureDoc = Guid.Parse("e7800000-0191-4c00-8000-000000000002");

    private SecureChildShareSynchronizer Mirror(SecureChildShareWorld world, IConfiguration configuration) =>
        new(SecureChildShareWorld.EntitiesOver(() => world).Object, _shares, SecureChildShareWorld.NobodyWalled(),
            configuration, NullLogger<SecureChildShareSynchronizer>.Instance, Engine(world, configuration));

    private static SecureChildShareWorld MirrorWorld() => World()
        .SecureRoot("sprk_project", SecureProject)
        .SecureChild("sprk_document", SecureDoc, ("sprk_project", "sprk_project", SecureProject));

    [Fact]
    public async Task Mirror_ASecureChildThePredicateHides_IsNotMirroredToAFlaggedUser_ButIsToAnInternalOne()
    {
        _shares.Seed("sprk_project", SecureProject, User(Partner), Read);
        _shares.Seed("sprk_project", SecureProject, User(Colleague), Read);
        _predicate.Hides("sprk_document", SecureDoc);

        await Mirror(MirrorWorld(), Config()).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", SecureDoc, User(Colleague)).Should().Be(Read);
        _shares.MaskOf("sprk_document", SecureDoc, User(Partner)).Should().BeNull();
    }

    [Fact]
    public async Task Mirror_ASecureChildThePredicateShows_IsMirroredToAFlaggedUser()
    {
        _shares.Seed("sprk_project", SecureProject, User(Partner), Read);

        await Mirror(MirrorWorld(), Config()).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", SecureDoc, User(Partner)).Should().Be(Read);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, Read)]
    public async Task Mirror_AFlaggedUsersShareOnAHiddenSecureChild_IsRevokedOnlyWithTheWritesOn(bool writes, int? expected)
    {
        _shares.Seed("sprk_project", SecureProject, User(Partner), Read);
        _shares.Seed("sprk_document", SecureDoc, User(Partner), Read);
        _predicate.Hides("sprk_document", SecureDoc);

        await Mirror(MirrorWorld(), Config(writes: writes)).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", SecureDoc, User(Partner)).Should().Be(expected);
    }

    [Fact]
    public async Task Mirror_WhenThePredicateFaults_NothingIsAddedForAFlaggedUser()
    {
        _shares.Seed("sprk_project", SecureProject, User(Partner), Read);
        _shares.Seed("sprk_project", SecureProject, User(Colleague), Read);
        _predicate.Faults = true;

        await Mirror(MirrorWorld(), Config()).ReconcileAllAsync(CancellationToken.None);

        _shares.MaskOf("sprk_document", SecureDoc, User(Partner)).Should().BeNull();
        _shares.MaskOf("sprk_document", SecureDoc, User(Colleague)).Should().Be(Read);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The two-minute job's External-team pass
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private (SecureChildShareReconciliationJob Job, SecureChildShareWorld World) Job(IConfiguration configuration, bool composeEngine = true)
    {
        var world = World();
        var engine = Engine(world, configuration);
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services,
            new SecureChildShareSynchronizer(SecureChildShareWorld.EntitiesOver(() => world).Object, _shares,
                SecureChildShareWorld.NobodyWalled(), configuration, NullLogger<SecureChildShareSynchronizer>.Instance,
                composeEngine ? engine : null));
        if (composeEngine)
            Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, engine);
        var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var job = new SecureChildShareReconciliationJob(
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<
                Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(provider),
            TimeProvider.System, NullLogger<SecureChildShareReconciliationJob>.Instance);
        return (job, world);
    }

    private static Spaarke.Scheduling.JobRunContext RunContext() =>
        new(Guid.NewGuid(), "corr-191c", Spaarke.Scheduling.JobRunTrigger.Scheduled, new Dictionary<string, object>());

    [Fact]
    public async Task Job_AtInstanceStart_SweepsEveryLineageRow_AndSharesTheVisibleOrdinaryOnes()
    {
        var (job, _) = Job(Config());

        var run = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().Be(ReadAppendTo);
        _shares.MaskOf("sprk_fileversion", FileVersion, Team(ExternalTeam)).Should().Be(Read);
        _shares.MaskOf("sprk_event", EventOwnedBySecureTeam, Team(ExternalTeam)).Should().BeNull();
        _shares.MaskOf("sprk_invoice", Invoice, Team(ExternalTeam)).Should().BeNull();
        run.ResultJson.Should().Contain("\"externalTeam\"");
    }

    [Fact]
    public async Task Job_WithTheSettingOff_WritesNoShare()
    {
        var (job, _) = Job(Config(writes: false));

        await job.ExecuteAsync(RunContext(), CancellationToken.None);

        _shares.WriteLog.Should().NotContain(w => w.Principal == Team(ExternalTeam));
    }

    [Fact]
    public async Task Job_WithoutTheEngine_ReportsThePassUnavailable_AndStillSucceeds()
    {
        var (job, _) = Job(Config(), composeEngine: false);

        var run = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        run.Success.Should().BeTrue();
        run.ResultJson.Should().Contain("\"externalTeam\":{\"status\":\"unavailable\"");
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact]
    public async Task Job_ARowThatCannotBeDecided_IsCarriedToTheNextRun()
    {
        var (job, _) = Job(Config());
        _predicate.Says("sprk_event", EventA, ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.ReadFailed);

        var first = await job.ExecuteAsync(RunContext(), CancellationToken.None);
        first.Success.Should().BeTrue("a carried row is not a failure: an undecidable row would hold the job incomplete for ever (F3)");
        first.ResultJson.Should().Contain("\"status\":\"incomplete\"");
        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().BeNull();

        _predicate.Says("sprk_event", EventA, ExternalVisibilityVerdict.Visible, ExternalVisibilityReason.Visible);
        _predicate.Asked.Clear();
        await job.ExecuteAsync(RunContext(), CancellationToken.None);

        _predicate.Asked.Should().Contain(("sprk_event", EventA), "the carried row is decided again");
        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().Be(ReadAppendTo);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Adversarial pass 1 (F1–F4): each test fails without its fix
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task F1_AVisibleChildUnderARootSecureByInheritance_IsNeverTeamShared_AndLosesAnExistingTeamShare()
    {
        // The matter's stored flag is false, but an ancestor made it Secure (the inheritance job has not stamped it yet).
        _shares.Seed("sprk_event", EventB, Team(ExternalTeam), ReadAppendTo);
        _predicate.SecureByInheritance("sprk_matter", Matter);

        var result = await Engine(World(), Config()).SyncRowsAsync(
            [("sprk_event", EventA), ("sprk_event", EventB)], "test", CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().BeNull();
        _shares.MaskOf("sprk_event", EventB, Team(ExternalTeam)).Should().BeNull();
        result.SecureFamily.Should().Be(2);
    }

    [Fact]
    public async Task F2_OneRowThatFaultsThePredicate_DoesNotHoldBackTheRest_AndIsCarried()
    {
        _predicate.FaultsWhenAsked("sprk_event", EventB);

        var result = await Engine(World(), Config()).SyncRowsAsync(
            [("sprk_event", EventA), ("sprk_event", EventB)], "test", CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().Be(ReadAppendTo, "the batch is bisected");
        _shares.MaskOf("sprk_event", EventB, Team(ExternalTeam)).Should().BeNull();
        result.Carried.Should().Contain(("sprk_event", EventB)).And.NotContain(("sprk_event", EventA));
    }

    [Fact]
    public async Task F2_ASystemicFault_StopsSplittingAfterTheFirstSplit()
    {
        _predicate.Faults = true;
        var world = World();
        var rows = Enumerable.Range(1, 64).Select(i => ("sprk_event", new Guid(0x7e000000 + i, 0x0191, 0x4c00, new byte[8]))).ToList();
        foreach (var (table, id) in rows)
            world.OrdinaryChild(table, id, ("sprk_regardingmatter", "sprk_matter", Matter));

        var result = await Engine(world, Config()).SyncRowsAsync(rows, "test", CancellationToken.None);

        _predicate.Calls.Should().Be(3, "the whole batch and both halves faulted: not one bad row");
        result.Carried.Should().Contain(rows);
    }

    [Fact]
    public async Task F2_BisectingStopsAtTheCap()
    {
        var world = World();
        var rows = Enumerable.Range(1, 64).Select(i => ("sprk_event", new Guid(0x7e000000 + i, 0x0191, 0x4c00, new byte[8]))).ToList();
        foreach (var (table, id) in rows)
            world.OrdinaryChild(table, id, ("sprk_regardingmatter", "sprk_matter", Matter));
        foreach (var (table, id) in rows.Where((_, i) => i < 32 && i % 4 == 0))
            _predicate.FaultsWhenAsked(table, id); // eight bad rows, all in the first half

        var result = await Engine(world, Config()).SyncRowsAsync(rows, "test", CancellationToken.None);

        _predicate.Calls.Should().Be(ExternalTeamShareSynchronizer.MaxDecideCalls);
        result.Carried.Should().Contain(rows.Where((_, i) => i < 32 && i % 4 == 0));
    }

    [Fact]
    public async Task F2_OneRowThatFaultsThePredicate_CostsOnlyItsOwnFlaggedUserShares()
    {
        _shares.Seed("sprk_event", EventA, User(Partner), Read);
        _shares.Seed("sprk_event", EventB, User(Partner), Read);
        _predicate.FaultsWhenAsked("sprk_event", EventB);

        var result = await Engine(World(), Config()).SyncRowsAsync(
            [("sprk_event", EventA), ("sprk_event", EventB)], "test", CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, User(Partner)).Should().Be(Read, "EventA is decided Visible once the batch is bisected");
        _shares.MaskOf("sprk_event", EventB, User(Partner)).Should().BeNull("an undecided row fails closed (task constraint)");
        result.Carried.Should().Contain(("sprk_event", EventB));
    }

    [Fact]
    public async Task F2_ListModifiedSince_AdvancesThroughMoreRowsThanTheCapAtOneInstant()
    {
        var at = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var world = World()
            .Modified("sprk_event", EventA, at)
            .Modified("sprk_event", EventB, at)
            .Modified("sprk_event", EventOwnedByPartner, at);
        var engine = Engine(world, Config());

        var seen = new List<(string Table, Guid Id)>();
        var held = new Dictionary<string, (DateTime At, Guid AfterId)>();
        for (var run = 0; run < 4; run++)
        {
            var (rows, capped) = await engine.ListModifiedSinceAsync(held, at, perTableCap: 1, CancellationToken.None);
            seen.AddRange(rows.Where(r => r.Table == "sprk_event"));
            held = capped.TryGetValue("sprk_event", out var position)
                ? new Dictionary<string, (DateTime At, Guid AfterId)> { ["sprk_event"] = position }
                : new Dictionary<string, (DateTime At, Guid AfterId)>();
        }

        seen.Should().Contain([("sprk_event", EventA), ("sprk_event", EventB), ("sprk_event", EventOwnedByPartner)]);
        seen.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task F3_ARowWhoseRootCannotBeRead_IsCarried()
    {
        var world = World().FailingQueriesOf("sprk_matter");

        var result = await Engine(world, Config()).SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().BeNull();
        result.Carried.Should().Contain(("sprk_event", EventA));
    }

    [Fact]
    public async Task F3_AnInlineHook_StopsAtItsCap_AndHandsTheWalkBack()
    {
        var result = await Engine(World(), Config()).SyncFamilyAsync(
            "sprk_matter", Matter, "test", CancellationToken.None, maxRows: 1);

        result.Status.Should().Be(ExternalTeamShareStatus.Incomplete);
        result.WalkAgain.Should().Equal(("sprk_matter", Matter));
        result.Carried.Should().NotContain(("sprk_matter", Matter));
    }

    [Fact]
    public async Task F3_ARowCarriedForTooManyRuns_IsLeftToTheSweep()
    {
        var (job, _) = Job(Config());
        _predicate.Says("sprk_event", EventA, ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.ReadFailed);

        for (var run = 0; run <= SecureChildShareReconciliationJob.MaxExternalCarriedRuns; run++)
            await job.ExecuteAsync(RunContext(), CancellationToken.None);
        _predicate.Asked.Clear();
        await job.ExecuteAsync(RunContext(), CancellationToken.None);

        _predicate.Asked.Should().NotContain(("sprk_event", EventA));
    }

    [Fact]
    public async Task F3_TheMirror_AsksThePredicateOnceForAllItsSecureChildren()
    {
        var world = MirrorWorld()
            .SecureChild("sprk_document", SecureDoc2, ("sprk_project", "sprk_project", SecureProject));
        _shares.Seed("sprk_project", SecureProject, User(Partner), Read);

        await Mirror(world, Config()).ReconcileAllAsync(CancellationToken.None);

        _predicate.Calls.Should().Be(1);
        _shares.MaskOf("sprk_document", SecureDoc2, User(Partner)).Should().Be(Read);
    }

    private static readonly Guid SecureDoc2 = Guid.Parse("e7800000-0191-4c00-8000-000000000003");

    [Fact]
    public async Task Reown_AnAnswerOfTheSecureTeam_IsRefused_AndCarried()
    {
        var world = World().Add("sprk_event", EventOwnedByPartnerUnderSecure,
            ("owninguser", new EntityReference("systemuser", Partner)),
            ("sprk_regardingmatter", new EntityReference("sprk_matter", SecureMatter)));
        _predicate.RootsOf("sprk_event", EventOwnedByPartnerUnderSecure, ("sprk_matter", SecureMatter));

        var result = await Engine(world, Config(reown: true)).SyncRowsAsync(
            [("sprk_event", EventOwnedByPartnerUnderSecure)], "test", CancellationToken.None);

        world.OwnerWrites.Should().BeEmpty("moving a row into isolation is the secure reconciler's job");
        result.Carried.Should().Contain(("sprk_event", EventOwnedByPartnerUnderSecure));
    }

    private static readonly Guid EventOwnedByPartnerUnderSecure = Guid.Parse("e7200000-0191-4c00-8000-000000000009");

    // ── The L1 hooks (F3: untested before) ──────────────────────────────────────────────────────────────────────

    private SecureChildReconciler Reconciler(SecureChildShareWorld world, IConfiguration configuration) =>
        SecureChildShareWorld.ReconcilerOver(() => world, _shares, null!, externalTeam: Engine(world, configuration));

    [Fact]
    public async Task Hook_AReFile_BringsTheReFiledRowsTeamShareIntoLine_WithTheWritesOn()
    {
        var world = World();

        await Reconciler(world, Config()).AfterRefileAsync("sprk_event", EventA, () => Task.FromResult(false), CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().Be(ReadAppendTo);
    }

    [Fact]
    public async Task Hook_AReFile_WritesNothing_WithTheWritesOff()
    {
        var world = World();

        await Reconciler(world, Config(writes: false)).AfterRefileAsync("sprk_event", EventA, () => Task.FromResult(false), CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().BeNull();
    }

    [Fact]
    public async Task Hook_ASecureTransition_RevokesTheTeamShareAcrossTheFamily()
    {
        var world = World();
        _shares.Seed("sprk_event", EventA, Team(ExternalTeam), ReadAppendTo);
        _predicate.SecureByInheritance("sprk_matter", Matter);

        await Reconciler(world, Config()).ReconcileAsync(
            "sprk_matter", Matter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().BeNull();
    }

    [Fact]
    public async Task Hook_ACancelledReFile_DoesNotThrowFromTheTeamShareStep()
    {
        var world = World();
        using var cancelled = new CancellationTokenSource();
        _predicate.OnDecide = () => cancelled.Cancel();

        var refile = () => Reconciler(world, Config()).AfterRefileAsync(
            "sprk_event", EventA, () => Task.FromResult(false), cancelled.Token);

        await refile.Should().NotThrowAsync<OperationCanceledException>();
    }

    // ── Adversarial pass 2 ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task P2F2_AWriteThatDoesNotReadBack_FailsTheJob_AnUndecidedRowDoesNot()
    {
        var (job, _) = Job(Config());
        _shares.IgnoreWrites = true;

        var run = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        run.Success.Should().BeFalse("a share that did not read back is a fault, not an answer");
        run.ResultJson.Should().MatchRegex("\"failures\":[1-9]");
    }

    [Fact]
    public async Task P2F2_ARowCarriedOnlyForAnUndecidedAnswer_CountsNoFailure()
    {
        _predicate.Says("sprk_event", EventA, ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.ReadFailed);

        var result = await Engine(World(), Config()).SyncRowsAsync([("sprk_event", EventA)], "test", CancellationToken.None);

        result.Carried.Should().Contain(("sprk_event", EventA));
        result.Failures.Should().Be(0);
    }

    [Fact]
    public async Task P2F1_AFamilyLargerThanOnePass_IsBroughtIntoLineInFull()
    {
        var world = World();
        var events = Enumerable.Range(1, ExternalTeamShareSynchronizer.MaxFamilyRows + 5)
            .Select(i => new Guid(0x7f000000 + i, 0x0191, 0x4c00, new byte[8])).ToList();
        foreach (var id in events)
            world.OrdinaryChild("sprk_event", id, ("sprk_regardingmatter", "sprk_matter", Matter));

        var result = await Engine(world, Config(writes: false)).SyncFamilyAsync("sprk_matter", Matter, "test", CancellationToken.None);

        result.Status.Should().NotBe(ExternalTeamShareStatus.Failed);
        result.WalkAgain.Should().BeEmpty("the walk collected every row");
        result.Examined.Should().BeGreaterThan(ExternalTeamShareSynchronizer.MaxFamilyRows);
        result.Tables.Single(t => t.Table == "sprk_event").TeamGrants.Should().BeGreaterThan(ExternalTeamShareSynchronizer.MaxFamilyRows);
    }

    // ── uac-r2 task 197b: one source for the External Collaborators unit's name ─────────────────────────────────────

    [Fact(DisplayName = "197b must-fire: the retired unit-name setting is not read; the role file's unit is found though it names another unit")]
    public async Task R197b_TheRetiredUnitNameSetting_IsNotRead()
    {
        var world = World();
        var decoyBu = Guid.NewGuid();
        var decoyTeam = Guid.NewGuid();
        world.Add("businessunit", decoyBu, ("name", "Some Other Unit"),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        world.Team(decoyTeam, decoyBu, "Some Other Unit", isDefault: true, teamType: 0);
        var configuration = new ConfigurationBuilder().AddConfiguration(Config()).AddInMemoryCollection(
            new Dictionary<string, string?> { [ExternalTeamShareSynchronizer.RetiredBusinessUnitNameConfigKey] = "Some Other Unit" }).Build();

        var (team, refusal) = await Engine(world, configuration).ResolveExternalTeamAsync(CancellationToken.None);

        refusal.Should().BeNull();
        team.Should().Be(ExternalTeam, "the unit is the one config/external-collaborator-role.json names");
    }

    [Fact(DisplayName = "197b: no External Collaborators unit yet is no team and no refusal")]
    public async Task R197b_NoUnit_IsNoTeam_AndNoRefusal()
    {
        var (team, refusal) = await Engine(World(withExternalUnit: false), Config()).ResolveExternalTeamAsync(CancellationToken.None);

        team.Should().BeNull();
        refusal.Should().BeNull();
    }

    [Fact(DisplayName = "197b must-fire: a unit carrying the name that is not a direct child of the root is refused (the one lookup every reader uses)")]
    public async Task R197b_AUnitNotUnderTheRoot_IsRefused()
    {
        var world = World(withExternalUnit: false);
        var middle = Guid.NewGuid();
        world.Add("businessunit", middle, ("name", "Middle"),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        world.Add("businessunit", ExternalBu, ("name", Sprk.Bff.Api.Services.ExternalAccess.ExternalCollaboratorPlacement.BusinessUnitName),
            ("parentbusinessunitid", new EntityReference("businessunit", middle)));
        world.Team(ExternalTeam, ExternalBu, "External Collaborators", isDefault: true, teamType: 0);

        var (team, refusal) = await Engine(world, Config()).ResolveExternalTeamAsync(CancellationToken.None);

        team.Should().BeNull();
        refusal.Should().NotBeNull();
    }

    [Fact(DisplayName = "197b must-fire: a leftover value in the retired setting is reported once at startup, by its key only")]
    public void R197b_ALeftoverRetiredSetting_IsReported_ByKeyOnly()
    {
        const string leftover = "a-leftover-unit-name-value";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [ExternalTeamShareSynchronizer.RetiredBusinessUnitNameConfigKey] = leftover }).Build();
        var logger = new MessageLogger();

        ExternalTeamShareSynchronizer.ReportRetiredSettings(configuration, logger).Should().BeTrue();

        logger.Messages.Should().ContainSingle().Which.Should().Match<(Microsoft.Extensions.Logging.LogLevel Level, string Text)>(m =>
            m.Level == Microsoft.Extensions.Logging.LogLevel.Warning
            && m.Text.Contains(ExternalTeamShareSynchronizer.RetiredBusinessUnitNameConfigKey)
            && !m.Text.Contains(leftover));
    }

    [Theory(DisplayName = "197b must-not-fire: the retired setting unset or blank, nothing is reported")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void R197b_NoRetiredSetting_ReportsNothing(string? value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [ExternalTeamShareSynchronizer.RetiredBusinessUnitNameConfigKey] = value }).Build();
        var logger = new MessageLogger();

        ExternalTeamShareSynchronizer.ReportRetiredSettings(configuration, logger).Should().BeFalse();

        logger.Messages.Should().BeEmpty();
    }

    private sealed class MessageLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Text)> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add((logLevel, formatter(state, exception)));
    }
}
