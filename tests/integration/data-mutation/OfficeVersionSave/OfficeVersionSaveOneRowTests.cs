using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// FR-11 version save (spaarkeai-word-add-in-r1 task 023) — the data-mutation invariants of
/// <c>POST /api/office/save</c> with <c>document.existingDocumentId</c>: <b>exactly one <c>sprk_document</c> row
/// before and after, the SAME drive item, one more SPE version.</b>
/// </summary>
/// <remarks>
/// <para><b>The failure mode these tests exist to prevent.</b> Before task 023 every Word re-save of a
/// Spaarke document created a SECOND <c>sprk_document</c> — the record card and profile followed whichever row
/// was newest, and the earlier row's associations, profile and index entry were stranded (spec Success
/// Criterion 4). The opposite failure is as bad: a version save that wrote into a different item, or that let
/// the content-dedup gate delete the version it had just written.</para>
/// <para><b>Why an in-memory world rather than call counts.</b> Rows and SPE items are COUNTED, before and
/// after, in <see cref="OfficeVersionSaveWorld"/>, whose SPE drive keeps a real version history per item
/// (path-keyed upload vs item-keyed replace, exactly as the facade behaves). A mock that merely recorded a
/// Replace call would pass even if that call had targeted a different item. Module-boundary doubles only —
/// <c>IDataverseService</c>, the <c>SpeFileStore</c> facade (ADR-007), <c>IAccessDataSource</c>, the Service
/// Bus client (ADR-038 §4; no <c>Mock&lt;HttpMessageHandler&gt;</c>).</para>
/// </remarks>
[Trait("status", "repaired")]
public class OfficeVersionSaveOneRowTests
{
    private const string DocumentDrive = "b!doc-drive";

    private static readonly byte[] Original = { 0x50, 0x4B, 0x03, 0x04, 0x10 };
    private static readonly byte[] SecondDraft = { 0x50, 0x4B, 0x03, 0x04, 0x20, 0x20 };
    private static readonly byte[] ThirdDraft = { 0x50, 0x4B, 0x03, 0x04, 0x30, 0x30, 0x30 };

    [Fact]
    public async Task VersionSave_LeavesExactlyOneRow_AndAddsOneVersionToTheSameDriveItem()
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument(DocumentDrive, "Brief.docx", Original);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var rowsBefore = world.Documents.Count;
        var itemsBefore = world.SpeItems.Count;

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, SecondDraft));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // The one-row invariant.
        rowsBefore.Should().Be(1);
        world.Documents.Should().HaveCount(1, "a version save must never create a second sprk_document");
        world.Documents.Keys.Should().ContainSingle().Which.Should().Be(documentId);
        world.DocumentCreates.Should().Be(0);

        // Same drive item, one more version, carrying the new bytes.
        world.SpeItems.Should().HaveCount(itemsBefore, "no second SPE item is minted");
        var item = world.SpeItems[itemId];
        item.Versions.Should().HaveCount(2, "the SPE version count is incremented");
        item.Versions[^1].Should().Equal(SecondDraft);
        world.Documents[documentId].ItemId.Should().Be(itemId, "the row keeps the sprk_graphitemid it already carried");

        // Only the file metadata the version changed — never an identity column.
        var update = world.DocumentUpdates.Should().ContainSingle().Subject;
        update.DocumentId.Should().Be(documentId.ToString("D"));
        update.Update.FileSize.Should().Be(SecondDraft.Length);
        update.Update.FilePath.Should().Be(item.WebUrl);
        update.Update.FileName.Should().BeNull("SPE's item name did not change, so the row's name is not rewritten");
        update.Update.GraphItemId.Should().BeNull();
        update.Update.GraphDriveId.Should().BeNull();
        update.Update.Name.Should().BeNull();
        update.Update.Description.Should().BeNull();
        update.Update.MatterLookup.Should().BeNull("the version save never re-associates the document");
        update.Update.CanonicalHash.Should().BeNull();

        // Finalization is queued against the EXISTING document and the SAME item.
        var payload = world.FinalizationPayloads.Should().ContainSingle().Subject;
        OfficeVersionSaveWorld.PayloadValue(payload, "DocumentId").Should().Be(documentId.ToString("D"));
        OfficeVersionSaveWorld.PayloadValue(payload, "TempFileLocation").Should().Be($"spe://{DocumentDrive}/{itemId}");

        world.DeletedItemIds.Should().BeEmpty("nothing is deleted on the version path");
    }

    [Fact]
    public async Task SuccessiveRevisions_EachAddAVersion_AndAnIdenticalResendAddsNone()
    {
        // Before task 023's idempotency change, the second revision of the same document produced the SAME
        // server idempotency key as the first, and the persistent job lookup answered it "Duplicate" — the new
        // bytes were silently never written.
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument(DocumentDrive, "Brief.docx", Original);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        // The SAME target record on every revision, as the add-in sends it — only the bytes change.
        var target = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() };
        var secondRevision = OfficeVersionSaveWorld.VersionSave(documentId, SecondDraft) with { TargetEntity = target };
        var thirdRevision = OfficeVersionSaveWorld.VersionSave(documentId, ThirdDraft) with { TargetEntity = target };

        (await client.PostAsJsonAsync("/api/office/save", secondRevision))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync("/api/office/save", thirdRevision))
            .StatusCode.Should().Be(HttpStatusCode.Accepted, "a different revision is a different operation");

        world.SpeItems[itemId].Versions.Should().HaveCount(3);
        world.SpeItems[itemId].Versions[^1].Should().Equal(ThirdDraft);
        world.Jobs.Select(j => j.IdempotencyKey).Should().OnlyHaveUniqueItems();

        // A retry of the SAME request is de-duplicated — no fourth version.
        var resend = await client.PostAsJsonAsync("/api/office/save", thirdRevision);
        resend.IsSuccessStatusCode.Should().BeTrue();
        world.SpeItems[itemId].Versions.Should().HaveCount(3, "an identical re-send writes nothing new");

        world.Documents.Should().HaveCount(1);
        world.DocumentCreates.Should().Be(0);
        world.SpeItems.Should().HaveCount(1);
    }

    [Fact]
    public async Task VersionSave_OfAHashLinkedCopyWhoseContentDiverged_GraduatesIt_AndDeletesNothing()
    {
        // Content dedup on the version path = task 028's graduate-on-divergence ONLY. The row is a hash-linked
        // copy of another canonical; the new version's bytes differ, so the link is severed and the row
        // becomes its own canonical. The suppress mode never runs: nothing is deleted, nothing is redirected.
        var world = new OfficeVersionSaveWorld();
        var canonical = Guid.NewGuid();
        var (documentId, itemId) = world.SeedDocument(
            DocumentDrive, "Brief.docx", Original,
            linkedToCanonical: canonical, linkedHash: OfficeVersionSaveWorld.Hash(Original));
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, SecondDraft));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var graduation = world.GenericUpdates.Should().ContainSingle().Subject;
        graduation.Id.Should().Be(documentId);
        graduation.Fields["sprk_canonicaldocument"].Should().Be(DBNull.Value, "the link is severed");
        graduation.Fields["sprk_canonicalhash"].Should().Be(OfficeVersionSaveWorld.Hash(SecondDraft));
        world.Documents[documentId].CanonicalDocumentId.Should().BeNull();

        world.SpeItems[itemId].Versions.Should().HaveCount(2);
        world.DeletedItemIds.Should().BeEmpty();
        world.Documents.Should().HaveCount(1);
    }

    [Fact]
    public async Task VersionSave_ByteIdenticalToWhatTheLinkedCopyAlreadyHolds_KeepsTheLink_AndStillWritesTheVersion()
    {
        // A byte-identical re-save must not be treated as a duplicate that deletes the version just written,
        // or that redirects the user to another canonical.
        var world = new OfficeVersionSaveWorld();
        var canonical = Guid.NewGuid();
        var (documentId, itemId) = world.SeedDocument(
            DocumentDrive, "Brief.docx", SecondDraft,
            linkedToCanonical: canonical, linkedHash: OfficeVersionSaveWorld.Hash(SecondDraft));
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, SecondDraft));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.SpeItems[itemId].Versions.Should().HaveCount(2, "the version is written even though the bytes match");
        world.DeletedItemIds.Should().BeEmpty("the byte-identical short-circuit never runs on the version path");
        world.GenericUpdates.Should().BeEmpty("not diverged → the link stands");
        world.Documents[documentId].CanonicalDocumentId.Should().Be(canonical);

        var payload = world.FinalizationPayloads.Should().ContainSingle().Subject;
        OfficeVersionSaveWorld.PayloadValue(payload, "DocumentId")
            .Should().Be(documentId.ToString("D"), "the user stays on THEIR document, not a canonical");
    }
}
