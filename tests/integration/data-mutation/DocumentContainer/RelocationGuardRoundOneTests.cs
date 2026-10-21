// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/DocumentContainer/**`
//   - Justification: #2159 review round 1, slice 1 — the relocator's own rules for EVERY purpose: an isolated row never
//     leaves its secure record for another (A1, S1 to S2 needs F3, including the Make Secure provisioning lane); isolation
//     is "owned in the Secure Record business unit", any team there (A2); the guard runs again just before the re-point
//     (B8); a re-file moves only a BFF-bound item the BFF uploaded, never by the legacy check (B9), while the migration
//     also accepts that proof (B3); a checked-out document is never moved (B4). Each pins a write contract: what moves,
//     and what never does.
//
// Doubles are module boundaries only (ADR-038 §4): the REAL RecordContainerResolver and relocator over task 166's
// document-pointer world (RefileFileRelocationTests' environment).

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Documents;
using Xunit;
using R = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.RefileFileRelocationTests;
using T = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.DocumentContainerRelocatorTests;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;

public class RelocationGuardRoundOneTests
{
    private const string Item = R.Item;

    private static Task<DocumentRelocationOutcome> RelocateAsync(T.Rig rig, RelocationPurpose purpose, bool apply = true)
        => rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply, purpose);

    /// <summary>The document filed under <paramref name="matter"/>, its file at <paramref name="drive"/>, owned as told.</summary>
    private static (World World, T.Rig Rig) Placed(Guid matter, string drive, bool isolated)
    {
        var world = R.Env();
        world.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, matter, drive);
        R.Own(world, T.DocumentId, isolated);
        return (world, new T.Rig(world));
    }

    private static void NothingMoved(World world, T.Rig rig)
    {
        world.Updates.Should().BeEmpty("nothing is re-pointed");
        rig.Steps.Should().BeEmpty("nothing is downloaded, uploaded or deleted");
        rig.Copies.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // A1 — leaving secure record S1 for S2 is an un-secure: it needs F3 (owner round 10 item 7)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "A1 must not fire: an isolated document whose file is in S1 and that derives to S2 is HELD, whatever the purpose")]
    [InlineData(RelocationPurpose.Refile)]
    [InlineData(RelocationPurpose.MakeSecure)]
    [InlineData(RelocationPurpose.LegacyMigration)]
    public async Task A1_AnIsolatedDocumentFromS1ToS2_IsHeld(RelocationPurpose purpose)
    {
        var (world, rig) = Placed(R.SecondSecureMatter, T.SecureContainer, isolated: true);

        var outcome = await RelocateAsync(rig, purpose);

        outcome.State.Should().Be(RelocationState.HeldIsolated, outcome.Detail);
        outcome.Detail.Should().Contain("F3");
        rig.DriveOf(T.DocumentId).Should().Be(T.SecureContainer);
        NothingMoved(world, rig);
    }

    [Fact(DisplayName = "A1: report-only plans no S1-to-S2 move either")]
    public async Task A1_ReportOnly_IsHeld()
    {
        var (_, rig) = Placed(R.SecondSecureMatter, T.SecureContainer, isolated: true);

        (await RelocateAsync(rig, RelocationPurpose.MakeSecure, apply: false)).State.Should().Be(RelocationState.HeldIsolated);
    }

    [Fact(DisplayName = "A1: the Make Secure provisioning lane (the batch with S2 as the expected target) cannot pull S1's bytes into S2")]
    public async Task A1_TheProvisioningLane_CannotPullS1IntoS2()
    {
        // Provisioning relocates the documents its child pass just ISOLATED (ProvisionProjectEndpoint.FilesFollowAsync).
        var (world, rig) = Placed(R.SecondSecureMatter, T.SecureContainer, isolated: true);

        var batch = await rig.Relocator.RelocateDocumentsAsync(
            [T.DocumentId], R.SecondSecureContainer, RelocationPurpose.MakeSecure, apply: true);

        batch.Outcomes.Should().ContainSingle().Which.State.Should().Be(RelocationState.HeldIsolated);
        rig.DriveOf(T.DocumentId).Should().Be(T.SecureContainer);
        NothingMoved(world, rig);
    }

    [Theory(DisplayName = "A1 must fire: an isolated document whose file is in a business unit's container still moves into its secure container")]
    [InlineData(RelocationPurpose.MakeSecure)]
    [InlineData(RelocationPurpose.LegacyMigration)]
    [InlineData(RelocationPurpose.Refile)]
    public async Task A1_AnIsolatedDocumentFromABusinessUnitIntoS_Moves(RelocationPurpose purpose)
    {
        var (_, rig) = Placed(T.SecureMatter, T.CustomerAContainer, isolated: true);

        var outcome = await RelocateAsync(rig, purpose);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(T.SecureContainer, "that narrows");
    }

    [Theory(DisplayName = "A1 must fire: a document that is NOT isolated still moves from S1 to S2")]
    [InlineData(RelocationPurpose.MakeSecure)]
    [InlineData(RelocationPurpose.LegacyMigration)]
    [InlineData(RelocationPurpose.Refile)]
    public async Task A1_AnOrdinaryDocumentFromS1ToS2_Moves(RelocationPurpose purpose)
    {
        var (_, rig) = Placed(R.SecondSecureMatter, T.SecureContainer, isolated: false);

        var outcome = await RelocateAsync(rig, purpose);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(R.SecondSecureContainer);
    }

    [Fact(DisplayName = "A1: whose secure container the file is in cannot be read: nothing moves")]
    public async Task A1_AnUnreadableSourceOwner_MovesNothing()
    {
        var (world, rig) = Placed(R.SecondSecureMatter, T.SecureContainer, isolated: true);
        world.FaultQueriesOf = "sprk_project"; // the reverse lookup's secure-claimant probe (one of the securable tables)

        var outcome = await RelocateAsync(rig, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.Failed, outcome.Detail);
        outcome.Detail.Should().Contain("whose secure container its file is in");
        NothingMoved(world, rig);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // A2 — isolated = owned in the Secure Record business unit (any team there; #2162)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "A2 must fire: a row owned by ANOTHER team in the Secure Record business unit (a family's own team) is isolated")]
    [InlineData(RelocationPurpose.Refile)]
    [InlineData(RelocationPurpose.MakeSecure)]
    [InlineData(RelocationPurpose.LegacyMigration)]
    public async Task A2_AFamilyTeamOwnedRow_IsHeld(RelocationPurpose purpose)
    {
        var (world, rig) = Placed(T.PlainMatter, T.CustomerAContainer, isolated: false);
        R.OwnBy(world, T.DocumentId, R.FamilyTeam);

        var outcome = await RelocateAsync(rig, purpose);

        outcome.State.Should().Be(RelocationState.HeldIsolated, outcome.Detail);
        NothingMoved(world, rig);
    }

    [Fact(DisplayName = "A2 must fire: a row whose own business unit is the Secure Record one is isolated, whoever owns it")]
    public async Task A2_ARowInTheSecureBusinessUnit_IsHeld()
    {
        var (world, rig) = Placed(T.PlainMatter, T.CustomerAContainer, isolated: false);
        var row = world.Rows[("sprk_document", T.DocumentId)];
        row.Attributes.Remove("owningteam");
        row["owningbusinessunit"] = new EntityReference("businessunit", World.SecureRecordBusinessUnit);

        var outcome = await RelocateAsync(rig, RelocationPurpose.LegacyMigration);

        outcome.State.Should().Be(RelocationState.HeldIsolated, outcome.Detail);
        NothingMoved(world, rig);
    }

    [Fact(DisplayName = "A2 must not fire: a row owned by a team outside the Secure Record business unit still moves")]
    public async Task A2_AnOrdinaryTeamOwnedRow_Moves()
    {
        var (_, rig) = Placed(T.PlainMatter, T.CustomerAContainer, isolated: false);

        (await RelocateAsync(rig, RelocationPurpose.LegacyMigration)).State.Should().Be(RelocationState.Relocated);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
    }

    [Fact(DisplayName = "A2 must not fire: no Secure Record business unit in the environment means no row is isolated")]
    public async Task A2_NoSecureBusinessUnit_NothingIsIsolated()
    {
        var (world, rig) = Placed(T.PlainMatter, T.CustomerAContainer, isolated: true);
        world.HasSecureRecordBusinessUnit = false;

        (await RelocateAsync(rig, RelocationPurpose.LegacyMigration)).State.Should().Be(RelocationState.Relocated);
    }

    [Fact(DisplayName = "A2: an owning team whose business unit cannot be read moves nothing")]
    public async Task A2_AnUnreadableTeam_MovesNothing()
    {
        var (world, rig) = Placed(T.PlainMatter, T.CustomerAContainer, isolated: false);
        R.OwnBy(world, T.DocumentId, Guid.Parse("0000dead-0000-4000-8000-000000002159")); // a team the world cannot read

        var outcome = await RelocateAsync(rig, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.Failed, outcome.Detail);
        outcome.Detail.Should().Contain("whether it is isolated");
        NothingMoved(world, rig);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // B8 — the guard and the derivation are asked again under the confirmed lock, just before the re-point
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "B8: a filing changed while the file was copied stops the move — the copy is removed and nothing is written")]
    public async Task B8_AFilingChangedDuringTheCopy_WritesNothing()
    {
        var (world, rig) = Placed(T.PlainMatter, T.CustomerAContainer, isolated: false);
        rig.BeforeDownload = (_, _) =>
        {
            world.Rows[("sprk_document", T.DocumentId)]["sprk_matter"] = new EntityReference("sprk_matter", T.SecureMatter);
            return Task.CompletedTask;
        };

        var outcome = await RelocateAsync(rig, RelocationPurpose.LegacyMigration);

        outcome.State.Should().Be(RelocationState.Failed, outcome.Detail);
        outcome.Detail.Should().Contain("derived container changed");
        world.Updates.Should().BeEmpty("the row is never re-pointed");
        rig.Copies.Should().ContainSingle();
        rig.Steps.Should().Contain($"delete {T.CustomerA1Container}/{T.Rig.CopyItem}", "the copy is removed");
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerAContainer);
    }

    [Fact(DisplayName = "B8: a row isolated while its file was copied is not re-pointed out of isolation")]
    public async Task B8_ARowIsolatedDuringTheCopy_WritesNothing()
    {
        var (world, rig) = Placed(T.PlainMatter, T.CustomerAContainer, isolated: false);
        rig.BeforeDownload = (_, _) =>
        {
            R.Own(world, T.DocumentId, isolated: true);
            return Task.CompletedTask;
        };

        var outcome = await RelocateAsync(rig, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.Failed, outcome.Detail);
        outcome.Detail.Should().Contain("widening guard");
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().Contain($"delete {T.CustomerA1Container}/{T.Rig.CopyItem}");
    }

    [Fact(DisplayName = "B8: an isolation that cannot be read at the re-point (Dataverse unavailable mid-copy) stops the move, removes the copy and writes nothing")]
    public async Task B8_AnUnreadableRecheck_WritesNothing()
    {
        var (world, rig) = Placed(T.PlainMatter, T.CustomerAContainer, isolated: false);
        rig.BeforeDownload = (_, _) =>
        {
            world.FaultQueriesOf = "businessunit";
            return Task.CompletedTask;
        };

        var outcome = await RelocateAsync(rig, RelocationPurpose.LegacyMigration);

        outcome.State.Should().Be(RelocationState.Failed, outcome.Detail);
        outcome.Detail.Should().Contain("the widening guard stopped it while the file was copied").And.Contain("could not be read");
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().Contain($"delete {T.CustomerA1Container}/{T.Rig.CopyItem}");
    }

    [Fact(DisplayName = "B8: a row moved ALONG is re-checked too — isolated while its copy was made, it is not re-pointed")]
    public async Task B8_AMovedAlongRowIsolatedDuringItsCopy_IsNotRepointed()
    {
        var world = R.Env();
        world.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, T.PlainMatter, T.CustomerAContainer);
        world.Rows[("sprk_document", T.OtherDocumentId)] = R.Doc(T.OtherDocumentId, T.PlainMatter, T.CustomerAContainer);
        R.Own(world, T.DocumentId, isolated: false);
        R.Own(world, T.OtherDocumentId, isolated: false);
        var rig = new T.Rig(world);
        var downloads = 0;
        rig.BeforeDownload = (_, _) =>
        {
            if (++downloads == 2) // the moved-along row's own copy
                R.Own(world, T.OtherDocumentId, isolated: true);
            return Task.CompletedTask;
        };

        var outcome = await RelocateAsync(rig, RelocationPurpose.LegacyMigration);

        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container, "the document itself moved");
        rig.DriveOf(T.OtherDocumentId).Should().Be(T.CustomerAContainer, "the other row is never re-pointed out of isolation");
        outcome.MovedAlong.Should().ContainSingle().Which.State.Should().Be(RelocationState.Failed);
        world.ItemFacts(T.CustomerAContainer, Item).Should().NotBeNull("the source is kept: a row still names it");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // B9 / B3 — the proof of a BFF-placed file
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "B9: an UNBOUND row is never re-filed by the legacy check — the migration still moves it")]
    public async Task B9_AnUnboundRow_IsNotRefiled_ButTheMigrationMovesIt()
    {
        var world = R.Env();
        world.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, T.PlainMatter, T.CustomerAContainer, bound: null);
        R.Own(world, T.DocumentId, isolated: false);
        var rig = new T.Rig(world);

        var refile = await RelocateAsync(rig, RelocationPurpose.Refile, apply: false);
        var legacy = await RelocateAsync(rig, RelocationPurpose.LegacyMigration, apply: false);

        refile.State.Should().Be(RelocationState.SourceUnverified, "a legacy misplaced row is the operator-gated migration's");
        legacy.State.Should().Be(RelocationState.WouldRelocate, "the legacy check vouches for the BFF's own row and upload");
    }

    [Fact(DisplayName = "B9: a bound item the BFF did not upload is SourceUnverified under Refile, even when the row's creator uploaded it")]
    public async Task B9_ABoundItemTheCreatorUploaded_IsSourceUnverifiedUnderRefile()
    {
        var world = R.Env();
        var row = R.Doc(T.DocumentId, T.PlainMatter, T.CustomerAContainer);
        row["createdby"] = new EntityReference("systemuser", TestRecordContainerResolver.PointerWorldCreator);
        world.Rows[("sprk_document", T.DocumentId)] = row;
        world.Items[(T.CustomerAContainer, Item)] =
            new SpeItemCreator("memo.docx", TestRecordContainerResolver.PointerWorldCreatorObjectId.ToString("D"), null, 1234, "hash-1234");
        R.Own(world, T.DocumentId, isolated: false);
        var rig = new T.Rig(world);

        (await RelocateAsync(rig, RelocationPurpose.Refile, apply: false)).State.Should().Be(RelocationState.SourceUnverified);
        (await RelocateAsync(rig, RelocationPurpose.LegacyMigration, apply: false)).State.Should().Be(
            RelocationState.WouldRelocate, "the migration's legacy check still vouches for the creator's own upload");
    }

    [Fact(DisplayName = "B3 must fire: the migration repairs a (d) row — a person's row whose file is the BFF's copy in S, re-filed outside")]
    public async Task B3_TheMigrationRepairsARowWhoseFileIsTheBffsCopy()
    {
        // The legacy check alone refuses it: the item is app-only and the row was created by a person, and the row no
        // longer hangs off S. The bound, BFF-uploaded item is the proof.
        var world = R.Env();
        var row = R.Doc(T.DocumentId, T.PlainMatter, T.SecureContainer);
        row["createdby"] = new EntityReference("systemuser", TestRecordContainerResolver.PointerWorldCreator);
        world.Rows[("sprk_document", T.DocumentId)] = row;
        R.Own(world, T.DocumentId, isolated: false);
        var rig = new T.Rig(world);

        var outcome = await RelocateAsync(rig, RelocationPurpose.LegacyMigration);

        outcome.State.Should().Be(RelocationState.Relocated, outcome.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
    }

    [Fact(DisplayName = "B3 must not fire: the same row whose bound item another person uploaded is SourceUnverified under the migration")]
    public async Task B3_ABoundItemAnotherPersonUploaded_IsSourceUnverifiedUnderTheMigration()
    {
        var world = R.Env();
        var row = R.Doc(T.DocumentId, T.PlainMatter, T.SecureContainer);
        row["createdby"] = new EntityReference("systemuser", TestRecordContainerResolver.PointerWorldCreator);
        world.Rows[("sprk_document", T.DocumentId)] = row;
        world.Items[(T.SecureContainer, Item)] = new SpeItemCreator("memo.docx", T.OtherPersonObjectId.ToString("D"), null, 1234, "h");
        R.Own(world, T.DocumentId, isolated: false);
        var rig = new T.Rig(world);

        (await RelocateAsync(rig, RelocationPurpose.LegacyMigration)).State.Should().Be(RelocationState.SourceUnverified);
        world.Updates.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // B4 — a checked-out document is never moved, whatever the purpose
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "B4 must not fire: a checked-out document is reported CheckedOut and nothing moves")]
    [InlineData(RelocationPurpose.Refile, true)]
    [InlineData(RelocationPurpose.MakeSecure, true)]
    [InlineData(RelocationPurpose.LegacyMigration, true)]
    [InlineData(RelocationPurpose.LegacyMigration, false)]
    public async Task B4_ACheckedOutDocument_IsLeftAlone(RelocationPurpose purpose, bool byHolder)
    {
        var (world, rig) = Placed(T.SecureMatter, T.CustomerAContainer, isolated: false);
        var row = world.Rows[("sprk_document", T.DocumentId)];
        if (byHolder)
            row[DocumentContainerRelocator.CheckedOutByColumn] = new EntityReference("systemuser", TestRecordContainerResolver.PointerWorldCreator);
        else
            row[DocumentContainerRelocator.CheckedOutDateColumn] = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

        var outcome = await RelocateAsync(rig, purpose);

        outcome.State.Should().Be(RelocationState.CheckedOut, outcome.Detail);
        NothingMoved(world, rig);
    }

    [Fact(DisplayName = "B4: a checked-out document's ledger is not settled either (a re-copy would re-point it)")]
    public async Task B4_ACheckedOutDocumentsLedger_IsNotSettled()
    {
        var (world, rig) = Placed(T.SecureMatter, T.SecureContainer, isolated: true);
        var row = world.Rows[("sprk_document", T.DocumentId)];
        row["sprk_graphitemid"] = row[R.Bound] = T.Rig.CopyItem;
        var ledger = T.LedgerJson(T.CustomerAContainer, Item, witnessSize: 1234, witnessHash: "hash-1234", witnessVersion: "1.0");
        row[T.Ledger] = ledger;
        row[DocumentContainerRelocator.CheckedOutByColumn] = new EntityReference("systemuser", TestRecordContainerResolver.PointerWorldCreator);

        var outcome = await RelocateAsync(rig, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.CheckedOut);
        NothingMoved(world, rig);
        rig.LedgerOf(T.DocumentId).Should().Be(ledger);
    }

    [Fact(DisplayName = "B4: a checked-out OTHER row is not moved along; the source is kept for it")]
    public async Task B4_ACheckedOutOtherRow_IsNotMovedAlong()
    {
        var world = R.Env();
        world.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, T.PlainMatter, T.CustomerAContainer);
        var other = R.Doc(T.OtherDocumentId, T.PlainMatter, T.CustomerAContainer);
        other[DocumentContainerRelocator.CheckedOutDateColumn] = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
        world.Rows[("sprk_document", T.OtherDocumentId)] = other;
        R.Own(world, T.DocumentId, isolated: false);
        R.Own(world, T.OtherDocumentId, isolated: false);
        var rig = new T.Rig(world);

        var outcome = await RelocateAsync(rig, RelocationPurpose.LegacyMigration);

        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
        rig.DriveOf(T.OtherDocumentId).Should().Be(T.CustomerAContainer, "its holder is editing the source");
        outcome.MovedAlong.Should().ContainSingle().Which.State.Should().Be(RelocationState.CheckedOut);
        world.ItemFacts(T.CustomerAContainer, Item).Should().NotBeNull("never deleted while a checked-out row names it");
    }
}
