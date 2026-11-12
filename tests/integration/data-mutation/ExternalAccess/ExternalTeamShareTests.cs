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

        public bool Faults { get; set; }

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
            if (Faults)
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

            return Task.FromResult(new ExternalVisibilityResult(decided, faulted: false) { RootsReached = reached });
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
            new Dictionary<string, DateTime>(), since, perTableCap: 1, CancellationToken.None);

        rows.Should().Equal(("sprk_event", EventA));
        capped.Should().ContainKey("sprk_event").WhoseValue.Should().Be(since.AddMinutes(1));
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
        first.Success.Should().BeFalse("an undecided row is left out of line until it is decided");
        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().BeNull();

        _predicate.Says("sprk_event", EventA, ExternalVisibilityVerdict.Visible, ExternalVisibilityReason.Visible);
        _predicate.Asked.Clear();
        await job.ExecuteAsync(RunContext(), CancellationToken.None);

        _predicate.Asked.Should().Contain(("sprk_event", EventA), "the carried row is decided again");
        _shares.MaskOf("sprk_event", EventA, Team(ExternalTeam)).Should().Be(ReadAppendTo);
    }
}
