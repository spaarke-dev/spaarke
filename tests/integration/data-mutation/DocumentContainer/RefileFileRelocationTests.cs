// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/DocumentContainer/**`
//   - Justification: #2159 / #2151 — a re-filed document's file follows its derived container through the ONE
//     DocumentContainerRelocator (purpose Refile), driven by SecureChildReconciliationJob's recent-changes cadence. Each
//     test pins a write contract: what is moved, and what is NEVER moved (out of isolation, an unbound source, a file
//     already in place, an unfiled document's declared or unknown home), with the job's cap, cursor and carry.
//
// Doubles are module boundaries only (ADR-038 §4), the same as DocumentContainerRelocatorTests and
// MakeSecureFileRelocationTests: the REAL RecordContainerResolver and relocator over task 166's document-pointer world,
// and the REAL job over task 148's in-memory world, joined by the same document ids.

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using T = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.DocumentContainerRelocatorTests;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;

public class RefileFileRelocationTests
{
    internal const string Item = T.Item;
    internal const string ProcedureLibrary = "b!ebilling-procedure-library";
    private const string ProcedureLibraryKey = "Finance:ProcedureLibraryContainerId";
    private const string EbillingOutputKey = "Finance:EbillingOutputContainerId";
    internal const string SecondSecureContainer = "b!secure-matter-two-own-container";
    internal const string Bound = DocumentPointerBinding.BoundItemIdColumn;

    internal static readonly Guid SecondSecureMatter = Guid.Parse("1c000000-0000-4000-8000-00000000f1a0");

    // ── The pointer world ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary><see cref="T.Environment"/> — two business units' containers, a secure matter S and a plain matter — plus a
    /// second secure matter S2, with the strict rule and any declared home as asked. An item it does not list was uploaded
    /// by the BFF identity (a relocation copy, or a record-keyed upload) — #2159 B9's proof of a BFF-placed file.</summary>
    internal static World Env(bool strict = false, string? homeKey = null, string? home = ProcedureLibrary)
    {
        var world = new World { ArchiveContainerId = T.ArchiveContainer, Strict = strict };
        world.DefaultItem = new SpeItemCreator("file.pdf", null, T.BffApplication, Size: 1234, QuickXorHash: "hash-1234");
        var template = T.Environment();
        foreach (var (unit, value) in template.BusinessUnits)
            world.BusinessUnits[unit] = value;
        foreach (var (container, owner) in template.SecureClaims)
            world.SecureClaims[container] = owner;
        foreach (var (key, row) in template.Rows)
            world.Rows[key] = row;
        world.SecureClaims[SecondSecureContainer] = ("sprk_matter", SecondSecureMatter);
        world.Rows[("sprk_matter", SecondSecureMatter)] = new Entity("sprk_matter", SecondSecureMatter)
        {
            ["sprk_issecure"] = true,
            ["sprk_containerid"] = SecondSecureContainer,
            ["owningbusinessunit"] = new EntityReference("businessunit", T.CustomerA),
        };
        if (homeKey is not null)
            world.Settings[homeKey] = home;
        return world;
    }

    /// <summary>A document the BFF created, owned in customer A, filed under <paramref name="matter"/> (none = unfiled), its
    /// file at <paramref name="drive"/>/<see cref="Item"/>, bound by the BFF unless told otherwise.</summary>
    internal static Entity Doc(Guid id, Guid? matter, string drive, string? bound = Item)
    {
        var row = World.Document(id, TestRecordContainerResolver.PointerWorldBffUser, T.CustomerA);
        if (matter is { } m)
            row["sprk_matter"] = new EntityReference("sprk_matter", m);
        row["sprk_graphdriveid"] = drive;
        row["sprk_graphitemid"] = Item;
        if (bound is not null)
            row[Bound] = bound;
        return row;
    }

    /// <summary>The Secure Record owner team of the pointer world (the job world's own, so the two worlds agree).</summary>
    internal static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;

    /// <summary>#2159 A2: ANOTHER team in the Secure Record business unit (a secure family's own owner team, task 178).</summary>
    internal static readonly Guid FamilyTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000002162");

    /// <summary>
    /// The environment has the Secure Record business unit (with the owner team and a family team in it, and the ordinary
    /// team in the root unit); the row is owned by the owner team (ISOLATED) or by the ordinary team.
    /// </summary>
    internal static void Own(World world, Guid id, bool isolated) => OwnBy(world, id, isolated ? SecureTeam : SecureChildShareWorld.GeneralTeam);

    /// <summary>As <see cref="Own"/>, owned by <paramref name="team"/>.</summary>
    internal static void OwnBy(World world, Guid id, Guid team)
    {
        world.HasSecureRecordBusinessUnit = true;
        world.Teams[SecureTeam] = World.SecureRecordBusinessUnit;
        world.Teams[FamilyTeam] = World.SecureRecordBusinessUnit;
        world.Teams[SecureChildShareWorld.GeneralTeam] = TestRecordContainerResolver.PointerWorldRootBusinessUnit;
        world.Rows[("sprk_document", id)]["owningteam"] = new EntityReference("team", team);
    }

    private static Task<DocumentRelocationOutcome> RefileAsync(
        T.Rig rig, Guid id, bool isolated, bool apply = true)
    {
        Own(rig.World, id, isolated);
        return rig.Relocator.RelocateIfMisplacedAsync(id, apply, RelocationPurpose.Refile);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The relocator, purpose Refile — must fire
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "#2159 (b): an ordinary document re-filed to another business unit's record moves into that container")]
    public async Task Refile_OrdinaryToAnotherBusinessUnit_MovesTheFile()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        var rig = new T.Rig(world);

        var outcome = await RefileAsync(rig, T.DocumentId, isolated: false);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container, "the plain matter's container is customer A1's");
        rig.Steps.Should().Contain($"delete {T.CustomerBContainer}/{Item} (after re-point)");
    }

    [Fact(DisplayName = "#2159 B9: a BOUND item another person uploaded is SourceUnverified under Refile — the binding alone is not proof")]
    public async Task Refile_ABoundItemAnotherPersonUploaded_IsSourceUnverified()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        world.Items[(T.CustomerBContainer, Item)] = new SpeItemCreator("memo.docx", T.OtherPersonObjectId.ToString("D"), null, 10, "h");
        var rig = new T.Rig(world);

        var outcome = await RefileAsync(rig, T.DocumentId, isolated: false);

        outcome.State.Should().Be(RelocationState.SourceUnverified, outcome.Detail);
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2159 (d): a document that LEFT isolation (F3) moves out of the secure container into its new one")]
    public async Task Refile_ARowNoLongerIsolated_LeavesTheSecureContainer()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.SecureContainer);
        var rig = new T.Rig(world);

        var outcome = await RefileAsync(rig, T.DocumentId, isolated: false);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
        rig.Steps.Should().Contain($"delete {T.SecureContainer}/{Item} (after re-point)", "the secure copy goes once moved");
    }

    [Fact(DisplayName = "#2159 (e) / A1: an isolated document re-filed from secure S1 to secure S2 outside the BFF is HELD (leaving S1 needs F3)")]
    public async Task Refile_IsolatedFromOneSecureRecordToAnother_IsHeld()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, SecondSecureMatter, T.SecureContainer);
        var rig = new T.Rig(world);

        var outcome = await RefileAsync(rig, T.DocumentId, isolated: true);

        outcome.State.Should().Be(RelocationState.HeldIsolated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(T.SecureContainer, "S1's bytes never reach S2's audience without F3");
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2159/#2151: an unfiled document whose file is in ANOTHER business unit's container moves into its own unit's")]
    public async Task Refile_UnfiledInAnotherBusinessUnitsContainer_MovesToItsOwn()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, null, T.CustomerBContainer);
        var rig = new T.Rig(world);

        var outcome = await RefileAsync(rig, T.DocumentId, isolated: false);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerAContainer, "its owning business unit's container");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The relocator, purpose Refile — must NOT fire
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "#2159 widening guard: an ISOLATED document whose filing was edited outside the BFF stays in the secure container")]
    public async Task Refile_AnIsolatedRowDerivedOutsideEverySecureRecord_IsHeld()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.SecureContainer);
        var rig = new T.Rig(world);

        var outcome = await RefileAsync(rig, T.DocumentId, isolated: true);

        outcome.State.Should().Be(RelocationState.HeldIsolated);
        world.Updates.Should().BeEmpty("nothing is re-pointed");
        rig.Steps.Should().BeEmpty("nothing is downloaded, uploaded or deleted");
        rig.Copies.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2159 widening guard: an isolated document never leaves for another business unit's container either")]
    public async Task Refile_AnIsolatedRowInABusinessUnitContainer_IsHeld()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        var rig = new T.Rig(world);

        var outcome = await RefileAsync(rig, T.DocumentId, isolated: true);

        outcome.State.Should().Be(RelocationState.HeldIsolated);
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2159: an UNBOUND pointer the legacy check cannot vouch for is SourceUnverified, and nothing moves")]
    public async Task Refile_AnUnboundPointerToAnotherPersonsItem_IsSourceUnverified()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer, bound: null);
        world.Items[(T.CustomerBContainer, Item)] = new SpeItemCreator("payroll.xlsx", T.OtherPersonObjectId.ToString("D"), null, 10, "h");
        var rig = new T.Rig(world);

        var outcome = await RefileAsync(rig, T.DocumentId, isolated: false);

        outcome.State.Should().Be(RelocationState.SourceUnverified);
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2159: a pointer whose bound copy names ANOTHER item is SourceUnverified, and nothing moves")]
    public async Task Refile_AMismatchedPointer_IsSourceUnverified()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer, bound: "01ANOTHERITEM");
        var rig = new T.Rig(world);

        var outcome = await RefileAsync(rig, T.DocumentId, isolated: false);

        outcome.State.Should().Be(RelocationState.SourceUnverified);
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2159: a document already in its derived container is InPlace")]
    public async Task Refile_AFileInItsDerivedContainer_IsInPlace()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerA1Container);
        var rig = new T.Rig(world);

        var outcome = await RefileAsync(rig, T.DocumentId, isolated: true);

        outcome.State.Should().Be(RelocationState.InPlace);
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
    }

    [Theory(DisplayName = "#2159: an isolation that cannot be read (the owning team's business unit unreadable) moves nothing, whatever the purpose")]
    [InlineData(RelocationPurpose.Refile)]
    [InlineData(RelocationPurpose.MakeSecure)]
    [InlineData(RelocationPurpose.LegacyMigration)]
    public async Task AnUnreadableIsolation_MovesNothing(RelocationPurpose purpose)
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        Own(world, T.DocumentId, isolated: false);
        world.FaultQueriesOf = "team";
        var rig = new T.Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, purpose);

        outcome.State.Should().Be(RelocationState.Failed, outcome.Detail);
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2159 A2: two Secure Record business units (ambiguous) is an unreadable isolation: nothing moves")]
    public async Task AnAmbiguousSecureBusinessUnit_MovesNothing()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        Own(world, T.DocumentId, isolated: false);
        var rig = new T.Rig(world);
        world.SecureRecordBusinessUnitAmbiguous = true;

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration);

        outcome.State.Should().Be(RelocationState.Failed, outcome.Detail);
        world.Updates.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2159: report-only plans the re-file and writes nothing")]
    public async Task Refile_ReportOnly_PlansTheMove_AndWritesNothing()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        var rig = new T.Rig(world);

        var outcome = await RefileAsync(rig, T.DocumentId, isolated: false, apply: false);

        outcome.State.Should().Be(RelocationState.WouldRelocate);
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2151 seam: an UNFILED document in a non-business-unit container that is no declared home is reported and left")]
    public async Task Refile_UnfiledInAContainerOfNoBusinessUnit_IsUnfiledHomeUnknown()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, null, T.SecureContainer);
        var rig = new T.Rig(world);

        var outcome = await RefileAsync(rig, T.DocumentId, isolated: false);

        outcome.State.Should().Be(RelocationState.UnfiledHomeUnknown);
        world.Updates.Should().BeEmpty("it is never pulled into a business unit's container");
        rig.Steps.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // #2151 — declared home containers of unfiled documents
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "#2151: an unfiled document the BFF placed in a declared home is served by the strict rule and stays put")]
    [InlineData(ProcedureLibraryKey)]
    [InlineData(EbillingOutputKey)]
    public async Task DeclaredHome_UnfiledAndBound_IsServedUnderStrict_AndNotMoved(string key)
    {
        var world = Env(strict: true, homeKey: key);
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, null, ProcedureLibrary);
        var rig = new T.Rig(world);

        (await rig.Resolver.IsDocumentPointerContainerAllowedAsync(T.DocumentId, ProcedureLibrary, Item))
            .Should().BeTrue("the declared home is this unfiled document's derived container");
        var outcome = await RefileAsync(rig, T.DocumentId, isolated: false);

        outcome.State.Should().Be(RelocationState.InPlace);
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2151: the interim rule serves a BFF-uploaded file in a declared home (through the strict test it already applies)")]
    public async Task DeclaredHome_UnderTheInterimRule_ABffUploadedFileIsServed()
    {
        var world = Env(strict: false, homeKey: ProcedureLibraryKey);
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, null, ProcedureLibrary);
        world.Items[(ProcedureLibrary, Item)] = new SpeItemCreator("guidelines.pdf", null, T.BffApplication, 10, "h");
        var rig = new T.Rig(world);

        (await rig.Resolver.IsDocumentPointerContainerAllowedAsync(T.DocumentId, ProcedureLibrary, Item)).Should().BeTrue();
    }

    [Fact(DisplayName = "#2151 / D-145: the same document FILED under a record moves into the record's container")]
    public async Task DeclaredHome_OnceFiledUnderARecord_MovesToTheRecordsContainer()
    {
        var world = Env(strict: true, homeKey: ProcedureLibraryKey);
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, ProcedureLibrary);
        var rig = new T.Rig(world);

        var outcome = await RefileAsync(rig, T.DocumentId, isolated: false);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
    }

    [Theory(DisplayName = "#2151: a file in a declared home whose pointer is NOT the bound item is no home — refused under strict, never moved")]
    [InlineData(null)]
    [InlineData("01ANOTHERITEM")]
    public async Task DeclaredHome_WithoutTheBoundItem_IsNotAHome(string? bound)
    {
        var world = Env(strict: true, homeKey: ProcedureLibraryKey);
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, null, ProcedureLibrary, bound: bound);
        var rig = new T.Rig(world);

        (await rig.Resolver.IsDocumentPointerContainerAllowedAsync(T.DocumentId, ProcedureLibrary, Item))
            .Should().BeFalse("only a file the BFF placed there is at home");
        (await rig.Resolver.DeriveDocumentContainersAsync(T.DocumentId)).DeclaredHome.Should().BeFalse();
        var outcome = await RefileAsync(rig, T.DocumentId, isolated: false);

        outcome.State.Should().BeOneOf(RelocationState.UnfiledHomeUnknown, RelocationState.SourceUnverified);
        world.Updates.Should().BeEmpty("it is not pulled into the business unit's container");
    }

    [Fact(DisplayName = "#2151: a blank home key declares no home")]
    public void DeclaredHomes_ABlankKeyAddsNoHome()
    {
        var blank = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ProcedureLibraryKey] = "   ",
            [EbillingOutputKey] = "",
        }).Build();
        var one = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ProcedureLibraryKey] = $" {ProcedureLibrary} ",
            [EbillingOutputKey] = " ",
        }).Build();

        RecordContainerResolver.DeclaredUnfiledHomesFrom(blank).Should().BeEmpty();
        RecordContainerResolver.DeclaredUnfiledHomesFrom(null).Should().BeEmpty();
        RecordContainerResolver.DeclaredUnfiledHomesFrom(one).Should().Equal(ProcedureLibrary);
    }

    [Fact(DisplayName = "#2151: with no home configured (master today) an unfiled document's derivation is unchanged")]
    public async Task DeclaredHomes_Unset_DerivationIsTheBusinessUnits()
    {
        var world = Env(strict: true);
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, null, ProcedureLibrary);
        var rig = new T.Rig(world);

        var derivation = await rig.Resolver.DeriveDocumentContainersAsync(T.DocumentId);

        derivation.AllowedContainers.Should().Equal(T.CustomerAContainer);
        derivation.Unfiled.Should().BeTrue();
        (await rig.Resolver.IsDocumentPointerContainerAllowedAsync(T.DocumentId, ProcedureLibrary, Item)).Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // #2159 — the widening guard holds for EVERY purpose (Make Secure's backstop, the legacy migration, a re-file)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static readonly Guid ParentDocument = Guid.Parse("00000000-0000-4000-8000-0000000f0001");

    /// <summary>
    /// An ISOLATED document whose own filing now points at the plain matter (an edit outside the BFF), its file still in
    /// the secure container, while its PARENT document is filed under the secure matter — so the legacy source check
    /// vouches for the file (it "hangs off" the secure record through its parent) although its derivation is not secure.
    /// </summary>
    private static World IsolatedInTheSecureContainerViaItsParent()
    {
        var world = Env();
        var parent = Doc(ParentDocument, T.SecureMatter, T.SecureContainer, bound: null);
        parent["sprk_graphitemid"] = "01PARENTSOWNFILE";
        world.Rows[("sprk_document", ParentDocument)] = parent;
        var row = Doc(T.DocumentId, T.PlainMatter, T.SecureContainer);
        row["sprk_parentdocument"] = new EntityReference("sprk_document", ParentDocument);
        world.Rows[("sprk_document", T.DocumentId)] = row;
        Own(world, T.DocumentId, isolated: true);
        return world;
    }

    /// <summary>
    /// The row (created by the BFF) was made secure earlier: its file is the BFF's copy in S — which the legacy source check
    /// vouches for — and its ledger still owes the shared source's delete.
    /// </summary>
    private static void MadeSecureEarlier(World world)
    {
        var row = world.Rows[("sprk_document", T.DocumentId)];
        row["createdby"] = new EntityReference("systemuser", TestRecordContainerResolver.PointerWorldBffUser);
        row["sprk_graphitemid"] = row[Bound] = T.Rig.CopyItem;
        row[T.Ledger] = T.LedgerJson(T.CustomerAContainer, Item, witnessSize: 1234, witnessHash: "hash-1234", witnessVersion: "1.0");
        world.Items[(T.SecureContainer, T.Rig.CopyItem)] = new SpeItemCreator("memo.docx", null, T.BffApplication, 1234, "hash-1234");
    }

    [Theory(DisplayName = "#2159 widening guard, every purpose: an isolated document never leaves the secure container for a business unit's")]
    [InlineData(RelocationPurpose.MakeSecure)]
    [InlineData(RelocationPurpose.LegacyMigration)]
    [InlineData(RelocationPurpose.Refile)]
    public async Task EveryPurpose_AnIsolatedDocumentInTheSecureContainer_DerivedOutside_IsHeld(RelocationPurpose purpose)
    {
        var world = IsolatedInTheSecureContainerViaItsParent();
        var rig = new T.Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, purpose);

        outcome.State.Should().Be(RelocationState.HeldIsolated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(T.SecureContainer, "its bytes stay in isolation");
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty("nothing is downloaded, uploaded or deleted");
    }

    [Theory(DisplayName = "#2159 widening guard, every purpose: an isolated document in one business unit's container is not moved into another's")]
    [InlineData(RelocationPurpose.MakeSecure)]
    [InlineData(RelocationPurpose.LegacyMigration)]
    [InlineData(RelocationPurpose.Refile)]
    public async Task EveryPurpose_AnIsolatedDocumentInABusinessUnitContainer_DerivedToAnother_IsHeld(RelocationPurpose purpose)
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerAContainer);
        Own(world, T.DocumentId, isolated: true);
        var rig = new T.Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, purpose);

        outcome.State.Should().Be(RelocationState.HeldIsolated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerAContainer);
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
    }

    [Theory(DisplayName = "#2159 widening guard, every purpose: report-only plans no move of an isolated document either")]
    [InlineData(RelocationPurpose.MakeSecure)]
    [InlineData(RelocationPurpose.LegacyMigration)]
    public async Task EveryPurpose_ReportOnly_AnIsolatedDocumentIsHeld_NotPlanned(RelocationPurpose purpose)
    {
        var world = IsolatedInTheSecureContainerViaItsParent();
        var rig = new T.Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: false, purpose);

        outcome.State.Should().Be(RelocationState.HeldIsolated, outcome.Detail);
    }

    [Theory(DisplayName = "#2159 must not fire: an isolated document whose derivation IS secure still moves into its secure container")]
    [InlineData(RelocationPurpose.MakeSecure)]
    [InlineData(RelocationPurpose.LegacyMigration)]
    [InlineData(RelocationPurpose.Refile)]
    public async Task EveryPurpose_AnIsolatedDocumentDerivedSecure_StillMoves(RelocationPurpose purpose)
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.SecureMatter, T.CustomerAContainer);
        Own(world, T.DocumentId, isolated: true);
        var rig = new T.Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, purpose);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(T.SecureContainer, "into the secure record's own container narrows");
    }

    [Theory(DisplayName = "#2159 must not fire: a document that is NOT isolated still moves between business units' containers")]
    [InlineData(RelocationPurpose.MakeSecure)]
    [InlineData(RelocationPurpose.LegacyMigration)]
    public async Task EveryPurpose_AnOrdinaryDocument_StillMoves(RelocationPurpose purpose)
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerAContainer);
        Own(world, T.DocumentId, isolated: false);
        var rig = new T.Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, purpose);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
    }

    [Theory(DisplayName = "#2159 a settle is not a move: a held document's ledger still deletes a stale source outside the secure container")]
    [InlineData(RelocationPurpose.MakeSecure)]
    [InlineData(RelocationPurpose.LegacyMigration)]
    [InlineData(RelocationPurpose.Refile)]
    public async Task EveryPurpose_AHeldDocument_StillSettlesItsLedger_AndOnlyNarrows(RelocationPurpose purpose)
    {
        var world = IsolatedInTheSecureContainerViaItsParent();
        MadeSecureEarlier(world);
        var rig = new T.Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, purpose);

        outcome.State.Should().Be(RelocationState.HeldIsolated, outcome.Detail);
        outcome.Pending.Should().BeEmpty("the ledger settled");
        rig.Steps.Should().Equal($"delete {T.CustomerAContainer}/{Item}");
        world.ItemFacts(T.CustomerAContainer, Item).Should().BeNull("the stale shared copy is gone: that narrows");
        rig.DriveOf(T.DocumentId).Should().Be(T.SecureContainer);
        rig.ItemOf(T.DocumentId).Should().Be(T.Rig.CopyItem, "the file itself never moves out of isolation");
        rig.Copies.Should().BeEmpty();
    }

    [Theory(DisplayName = "#2159 a settle never widens: another row that is isolated is not moved along out of the source's container")]
    [InlineData(RelocationPurpose.MakeSecure)]
    [InlineData(RelocationPurpose.LegacyMigration)]
    public async Task EveryPurpose_AnIsolatedOtherRow_IsNotMovedAlong(RelocationPurpose purpose)
    {
        // An ordinary document moves from customer A's container to A1's; an ISOLATED row naming the same file also derives
        // to A1. It is not moved along: it keeps the source as its file (settled), and the source is kept for it.
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerAContainer);
        world.Rows[("sprk_document", T.OtherDocumentId)] = Doc(T.OtherDocumentId, T.PlainMatter, T.CustomerAContainer);
        Own(world, T.DocumentId, isolated: false);
        Own(world, T.OtherDocumentId, isolated: true);
        var rig = new T.Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, purpose);

        outcome.State.Should().Be(RelocationState.RelocatedSourceKeptForOtherRecords, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
        rig.DriveOf(T.OtherDocumentId).Should().Be(T.CustomerAContainer, "an isolated row's file is never moved along out");
        rig.ItemOf(T.OtherDocumentId).Should().Be(Item);
        outcome.MovedAlong.Should().BeEmpty();
        world.ItemFacts(T.CustomerAContainer, Item).Should().NotBeNull("kept: the isolated row still uses it");
    }

    [Fact(DisplayName = "#2159 must not fire: an ordinary other row is still moved along")]
    public async Task AnOrdinaryOtherRow_IsStillMovedAlong()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerAContainer);
        world.Rows[("sprk_document", T.OtherDocumentId)] = Doc(T.OtherDocumentId, T.PlainMatter, T.CustomerAContainer);
        Own(world, T.DocumentId, isolated: false);
        Own(world, T.OtherDocumentId, isolated: false);
        var rig = new T.Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        rig.DriveOf(T.OtherDocumentId).Should().Be(T.CustomerA1Container);
        outcome.MovedAlong.Should().ContainSingle().Which.State.Should().Be(RelocationState.Relocated);
    }

    [Fact(DisplayName = "#2159: the Make Secure backstop holds an isolated document its LEDGER-OWING listing feeds it (re-filed outside the BFF)")]
    public async Task TheBackstop_LedgerOwingListing_HoldsAnIsolatedDocument()
    {
        var pointer = IsolatedInTheSecureContainerViaItsParent();
        MadeSecureEarlier(pointer);
        var rig = new T.Rig(pointer);
        var world = SecureChildShareWorld.Standard();
        world.Add("businessunit", T.CustomerA, ("sprk_containerid", T.CustomerAContainer));
        world.Add("sprk_document", T.DocumentId,
            ("owningteam", new EntityReference("team", world.OwnerTeam)),
            ("sprk_graphdriveid", T.SecureContainer),
            (DocumentContainerRelocator.RelocationLedgerColumn, "{\"v\":1}"));
        var harness = new JobHarness(world, rig);

        var (_, _, makeSecure) = await harness.RunAsync();

        makeSecure.GetProperty("ledgerOwing").GetInt32().Should().Be(1);
        makeSecure.GetProperty("stranded").GetInt32().Should().Be(0, "its file is in the secure container, no business unit's");
        makeSecure.GetProperty("moved").GetInt32().Should().Be(0);
        makeSecure.GetProperty("counts").GetProperty(nameof(RelocationState.HeldIsolated)).GetInt32().Should().Be(1);
        rig.DriveOf(T.DocumentId).Should().Be(T.SecureContainer, "the backstop never moves it out of isolation");
        rig.Copies.Should().BeEmpty();
        pointer.ItemFacts(T.CustomerAContainer, Item).Should().BeNull("its ledger still settled the stale shared source");
    }

    [Fact(DisplayName = "#2159: the Make Secure backstop holds an isolated document its STRANDED listing feeds it when it derives outside every secure record")]
    public async Task TheBackstop_StrandedListing_HoldsAnIsolatedDocumentDerivedOutside()
    {
        var pointer = Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerAContainer);
        Own(pointer, T.DocumentId, isolated: true);
        var rig = new T.Rig(pointer);
        var harness = new JobHarness(JobWorld((T.DocumentId, T.CustomerAContainer, true)), rig);

        var (_, _, makeSecure) = await harness.RunAsync();

        makeSecure.GetProperty("stranded").GetInt32().Should().Be(1);
        makeSecure.GetProperty("moved").GetInt32().Should().Be(0);
        makeSecure.GetProperty("counts").GetProperty(nameof(RelocationState.HeldIsolated)).GetInt32().Should().Be(1);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerAContainer, "not moved into another business unit's container");
        pointer.Updates.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2151 must not fire: the unfiled-home hold is the re-file's only — the legacy migration still moves an unfiled file out of the archive container")]
    public async Task TheUnfiledHomeHold_IsTheRefilesOnly_TheLegacyMigrationStillMoves()
    {
        var world = Env();
        world.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, null, T.ArchiveContainer);
        Own(world, T.DocumentId, isolated: false);
        var rig = new T.Rig(world);

        var legacy = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: false, RelocationPurpose.LegacyMigration);
        var refile = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: false, RelocationPurpose.Refile);

        legacy.State.Should().Be(RelocationState.WouldRelocate, "task 166 pulls an unfiled archive file into its business unit's container");
        legacy.TargetDrive.Should().Be(T.CustomerAContainer);
        refile.State.Should().Be(RelocationState.UnfiledHomeUnknown, "a re-file never pulls it from a container of no business unit");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The trigger — SecureChildReconciliationJob's re-file pass
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The job over task 148's world and the REAL relocator and resolver over task 166's world.</summary>
    internal sealed class JobHarness
    {
        private readonly IConfigurationRoot _configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        private readonly SecureChildReconciliationJob _job;

        public JobHarness(SecureChildShareWorld world, T.Rig rig, TimeProvider? time = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(SecureChildShareWorld.EntitiesOver(() => world).Object);
            services.AddScoped(_ => SecureChildShareWorld.ReconcilerOver(() => world, new FakeRecordShareTable(), null!));
            services.AddScoped(_ => SecureChildShareWorld.SynchronizerOver(() => world, new FakeRecordShareTable()));
            services.AddScoped(_ => rig.Relocator);
            services.AddScoped(_ => rig.Resolver);
            services.AddSingleton(_ => SecureChildShareWorld.CoreAncestorsOver(() => world));
            var provider = services.BuildServiceProvider();
            foreach (var (key, value) in SecureChildShareWorld.Configuration().AsEnumerable())
                _configuration[key] = value;
            _job = new SecureChildReconciliationJob(
                provider.GetRequiredService<IServiceScopeFactory>(), time ?? TimeProvider.System, _configuration,
                NullLogger<SecureChildReconciliationJob>.Instance);
        }

        /// <summary>The last run's whole report.</summary>
        public JsonElement Last { get; private set; }

        public async Task<(JobRunResult Result, JsonElement Refile, JsonElement MakeSecure)> RunAsync(
            string? maxRelocations = null, bool writes = true, string? examinationBudget = null, CancellationToken ct = default)
        {
            _configuration[SecureChildReconciliationJob.MaxRelocationsPerRunConfigKey] = maxRelocations;
            _configuration[SecureChildReconciliationJob.RefileExaminationBudgetConfigKey] = examinationBudget;
            _configuration[SecureChildReconciliationJob.RecentChangesWritesEnabledConfigKey] = writes ? null : "false";
            var result = await _job.ExecuteAsync(
                new JobRunContext(Guid.NewGuid(), "test", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
                ct);
            using var doc = JsonDocument.Parse(result.ResultJson!);
            Last = doc.RootElement.Clone();
            return (result, doc.RootElement.GetProperty("refileRelocations").Clone(),
                doc.RootElement.GetProperty("makeSecureRelocations").Clone());
        }
    }

    /// <summary>The job's view of the documents: each just modified, its pointer's drive and owner as given.</summary>
    internal static SecureChildShareWorld JobWorld(params (Guid Id, string Drive, bool Isolated)[] documents)
    {
        var world = SecureChildShareWorld.Standard();
        world.Add("businessunit", T.CustomerA, ("sprk_containerid", T.CustomerAContainer));
        foreach (var (id, drive, isolated) in documents)
        {
            world.Add("sprk_document", id,
                ("owningteam", new EntityReference("team", isolated ? world.OwnerTeam : SecureChildShareWorld.GeneralTeam)),
                ("sprk_graphdriveid", drive));
            world.Modified("sprk_document", id, DateTime.UtcNow);
        }

        return world;
    }

    internal static Guid[] Ids(JsonElement refile, string list)
        => refile.GetProperty(list).EnumerateArray().Select(e => e.GetGuid()).ToArray();

    [Fact(DisplayName = "#2159: the job moves a re-filed document's file within its run, with purpose Refile, and reports it")]
    public async Task TheJob_MovesARefiledDocument_AndReportsIt()
    {
        var pointer = Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        var rig = new T.Rig(pointer);
        var harness = new JobHarness(JobWorld((T.DocumentId, T.CustomerBContainer, false)), rig);

        var (result, refile, _) = await harness.RunAsync();

        refile.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeWrite);
        refile.GetProperty("moved").GetInt32().Should().Be(1);
        Ids(refile, "movedDocuments").Should().Equal(T.DocumentId);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact(DisplayName = "#2159: with the recent-changes writes off the re-file pass is report-only")]
    public async Task TheJob_WithWritesOff_OnlyReports()
    {
        var pointer = Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        var rig = new T.Rig(pointer);
        var harness = new JobHarness(JobWorld((T.DocumentId, T.CustomerBContainer, false)), rig);

        var (_, refile, _) = await harness.RunAsync(writes: false);

        refile.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeReportOnly);
        refile.GetProperty("wouldMove").GetInt32().Should().Be(1);
        refile.GetProperty("moved").GetInt32().Should().Be(0);
        pointer.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2159 widening guard: the job holds an isolated document whose filing was edited outside the BFF")]
    public async Task TheJob_HoldsAnIsolatedDocument_AndMovesNothing()
    {
        var pointer = Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.SecureContainer);
        Own(pointer, T.DocumentId, isolated: true);
        var rig = new T.Rig(pointer);
        var harness = new JobHarness(JobWorld((T.DocumentId, T.SecureContainer, true)), rig);

        var (result, refile, _) = await harness.RunAsync();

        refile.GetProperty("heldIsolated").GetInt32().Should().Be(1);
        Ids(refile, "heldIsolatedDocuments").Should().Equal(T.DocumentId);
        pointer.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
        result.Success.Should().BeTrue("a held row is the guard working, not an unfinished move");
    }

    [Fact(DisplayName = "#2151: the job leaves an unfiled, modified document in the Procedure Library where it is")]
    public async Task TheJob_LeavesADeclaredHomeDocumentInPlace()
    {
        var pointer = Env(strict: true, homeKey: ProcedureLibraryKey);
        pointer.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, null, ProcedureLibrary);
        var rig = new T.Rig(pointer);
        var harness = new JobHarness(JobWorld((T.DocumentId, ProcedureLibrary, false)), rig);

        var (_, refile, _) = await harness.RunAsync();

        refile.GetProperty("inPlace").GetInt32().Should().Be(1);
        refile.GetProperty("moved").GetInt32().Should().Be(0);
        pointer.Updates.Should().BeEmpty();
        rig.Locks.Released.Should().BeEmpty("a document in place never reaches the relocator");
    }

    [Fact(DisplayName = "#2159: a document already in place never spends the relocation budget")]
    public async Task TheJob_ADocumentInPlace_NeverSpendsTheCap()
    {
        var inPlace = Guid.Parse("00000000-0000-4000-8000-0000000d0001");
        var misplaced = Guid.Parse("00000000-0000-4000-8000-0000000d0002");
        var pointer = Env();
        pointer.Rows[("sprk_document", inPlace)] = Doc(inPlace, T.PlainMatter, T.CustomerA1Container);
        pointer.Rows[("sprk_document", misplaced)] = Doc(misplaced, T.PlainMatter, T.CustomerBContainer);
        var rig = new T.Rig(pointer);
        var harness = new JobHarness(
            JobWorld((inPlace, T.CustomerA1Container, false), (misplaced, T.CustomerBContainer, false)), rig);

        var (_, refile, _) = await harness.RunAsync(maxRelocations: "1");

        refile.GetProperty("inPlace").GetInt32().Should().Be(1);
        Ids(refile, "movedDocuments").Should().Equal(misplaced);
        refile.GetProperty("deferred").GetInt32().Should().Be(0);
    }

    [Fact(DisplayName = "#2159: an isolation read fault moves nothing, fails the run, and the row is carried to the next run")]
    public async Task TheJob_AnIsolationReadFault_CarriesTheRow()
    {
        var pointer = Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.PlainMatter, T.SecureContainer);
        Own(pointer, T.DocumentId, isolated: false);
        pointer.FaultQueriesOf = "team";
        var rig = new T.Rig(pointer);
        var world = JobWorld((T.DocumentId, T.SecureContainer, false));
        var harness = new JobHarness(world, rig);

        var (first, run1, _) = await harness.RunAsync();

        run1.GetProperty("failed").GetInt32().Should().Be(1);
        run1.GetProperty("carried").GetInt32().Should().Be(1);
        pointer.Updates.Should().BeEmpty();
        first.Success.Should().BeFalse();

        // The fault clears; the row is no longer in any window (an old modifiedon) — only the carry brings it back.
        pointer.FaultQueriesOf = null;
        world.Modified("sprk_document", T.DocumentId, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var (second, run2, _) = await harness.RunAsync();

        run2.GetProperty("retriedRows").GetInt32().Should().Be(1);
        Ids(run2, "movedDocuments").Should().Equal(T.DocumentId);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
        second.Success.Should().BeTrue(second.ErrorMessage);
    }

    [Fact(DisplayName = "#2159: past the cap the rest is carried, and the cursor lets a later document past one whose lock is held")]
    public async Task TheJob_CapAndCursor_NoDocumentStarves()
    {
        var first = Guid.Parse("00000000-0000-4000-8000-0000000e0001");
        var second = Guid.Parse("00000000-0000-4000-8000-0000000e0002");
        var pointer = Env();
        pointer.Rows[("sprk_document", first)] = Doc(first, T.PlainMatter, T.CustomerBContainer);
        var other = Doc(second, T.PlainMatter, T.CustomerBContainer);
        other["sprk_graphitemid"] = other[Bound] = "01SECONDITEM";
        pointer.Rows[("sprk_document", second)] = other;
        var rig = new T.Rig(pointer);
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(first)); // another relocation holds the first
        var world = JobWorld((first, T.CustomerBContainer, false), (second, T.CustomerBContainer, false));
        var harness = new JobHarness(world, rig);

        var (_, run1, _) = await harness.RunAsync(maxRelocations: "1");
        var (_, run2, _) = await harness.RunAsync(maxRelocations: "1");

        Ids(run1, "failedDocuments").Should().Equal(new[] { first }, "a held lock is reported, never waited on");
        Ids(run1, "deferredDocuments").Should().Equal(second);
        run1.GetProperty("capReached").GetBoolean().Should().BeTrue();
        Ids(run2, "movedDocuments").Should().Equal(new[] { second }, "the next run continues after the document the cap stopped at");
        rig.DriveOf(second).Should().Be(T.CustomerA1Container);
    }

    [Fact(DisplayName = "#2159: a document the Make Secure backstop took this run is not relocated twice")]
    public async Task TheJob_ADocumentTheBackstopTook_IsNotRefiledTwice()
    {
        var pointer = Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = Doc(T.DocumentId, T.SecureMatter, T.CustomerAContainer);
        var rig = new T.Rig(pointer);
        var harness = new JobHarness(JobWorld((T.DocumentId, T.CustomerAContainer, true)), rig);

        var (_, refile, makeSecure) = await harness.RunAsync();

        makeSecure.GetProperty("moved").GetInt32().Should().Be(1, "the backstop moved the stranded isolated file");
        refile.GetProperty("skippedForMakeSecure").GetInt32().Should().Be(1);
        refile.GetProperty("moved").GetInt32().Should().Be(0);
        rig.Locks.Released.Count(k => k == DocumentContainerRelocator.RelocationLockKey(T.DocumentId))
            .Should().Be(1, "one relocation of the document in the run");
        rig.DriveOf(T.DocumentId).Should().Be(T.SecureContainer);
    }
}
