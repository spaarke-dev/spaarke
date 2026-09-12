using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// NFR-08 on the FR-11 "Save as new document" override (spaarkeai-word-add-in-r1 task 024): when the user
/// chooses to save an IDENTIFIED document as a new one and its bytes are identical to an existing canonical,
/// the result is ONE NEW <c>sprk_document</c> carrying <c>sprk_canonicaldocument</c> → the canonical (the
/// editable LINK/GRADUATE mode) — never the immutable suppress mode that deletes the upload and answers with the
/// canonical's id. Once the copy is edited and saved again, the link is severed (graduation).
/// </summary>
/// <remarks>
/// <para><b>What the override sends.</b> A <c>ContentType=Document</c> save with NO
/// <c>document.existingDocumentId</c> — the add-in omits it when the user picks "A new document" (task 024,
/// <c>resolveSaveMode</c>). It therefore takes <c>POST /api/office/save</c>'s create path, where task 028 keys the
/// dedup mode on the content type (<c>OfficeDocumentPersistence.IsEditableContent</c>). These tests prove that
/// routing through the real HTTP route — the filters, <c>OfficeService</c>, <c>OfficeDocumentPersistence</c> and
/// the real <c>ContentDedupDetector</c> — rather than by calling the persistence method directly.</para>
/// <para><b>Why the in-memory world.</b> The link is asserted by READING THE COLUMN back from the row
/// (<see cref="OfficeVersionSaveWorld.DocumentRow.CanonicalDocumentId"/>), and rows, SPE items and deletions are
/// counted before and after — a recorded call alone would pass even if it targeted the wrong row. Module-boundary
/// doubles only (ADR-038 §4: <c>IDataverseService</c>, the <c>SpeFileStore</c> facade, <c>IAccessDataSource</c>, the
/// Service Bus client; no <c>Mock&lt;HttpMessageHandler&gt;</c>).</para>
/// </remarks>
[Trait("status", "repaired")]
public class OfficeSaveAsNewDocumentLinkGraduateTests
{
    private const string DocumentDrive = "b!doc-drive";

    private static readonly byte[] Original = { 0x50, 0x4B, 0x03, 0x04, 0x11 };
    private static readonly byte[] Edited = { 0x50, 0x4B, 0x03, 0x04, 0x22, 0x22 };

    [Fact]
    public async Task SaveAsNewDocument_ByteIdenticalToTheIdentifiedDocument_CreatesOneNewRowLinkedToIt_AndLeavesTheOriginalUntouched()
    {
        var world = new OfficeVersionSaveWorld();
        var (originalId, originalItemId) = world.SeedDocument(DocumentDrive, "Brief.docx", Original);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Original));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var result = await response.Content.ReadFromJsonAsync<SaveResponse>();
        result!.Success.Should().BeTrue();
        result.Duplicate.Should().BeFalse("an editable byte-identical save is recorded, not reported as a duplicate");

        // Exactly ONE new sprk_document row.
        world.DocumentCreates.Should().Be(1);
        world.Documents.Should().HaveCount(2);
        var copy = world.Documents.Values.Single(r => r.Id != originalId);

        // It carries sprk_canonicaldocument → the canonical, read back from the row — and the same content identity.
        copy.CanonicalDocumentId.Should().Be(originalId, "a byte-identical editable save is a hash-linked COPY (link/graduate)");
        copy.CanonicalHash.Should().Be(OfficeVersionSaveWorld.Hash(Original));
        world.GenericUpdates
            .Where(u => u.Id == copy.Id && u.Fields.ContainsKey("sprk_canonicaldocument"))
            .Should().ContainSingle()
            .Which.Fields["sprk_canonicaldocument"].Should().BeOfType<Microsoft.Xrm.Sdk.EntityReference>()
            .Which.Id.Should().Be(originalId);

        // A genuinely new SPE item — so sprk_graphitemid_uk is satisfied naturally (NFR-07), nothing relaxed.
        copy.ItemId.Should().NotBeNull().And.NotBe(originalItemId);
        world.SpeItems.Should().HaveCount(2);

        // NOT the immutable suppress branch: the upload is not deleted, and the user stays on THEIR new document.
        world.DeletedItemIds.Should().BeEmpty("suppress deletes the just-uploaded blob; link/graduate never does");
        var payload = world.FinalizationPayloads.Should().ContainSingle().Subject;
        OfficeVersionSaveWorld.PayloadValue(payload, "DocumentId")
            .Should().Be(copy.Id.ToString("D"), "the response is never redirected to the canonical record");

        // The original record and its file are untouched.
        world.SpeItems[originalItemId].Versions.Should().HaveCount(1);
        world.DocumentUpdates.Should().NotContain(u => u.DocumentId == originalId.ToString("D"));
        world.GenericUpdates.Should().NotContain(u => u.Id == originalId);
        world.Documents[originalId].CanonicalDocumentId.Should().BeNull();
        world.ReplaceCalls.Should().Be(0, "the override writes no version of any existing item");
    }

    [Fact]
    public async Task LinkedCopyMadeByTheOverride_WhenEditedAndSavedAgain_GraduatesToItsOwnCanonical()
    {
        var world = new OfficeVersionSaveWorld();
        var (originalId, originalItemId) = world.SeedDocument(DocumentDrive, "Brief.docx", Original);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        (await client.PostAsJsonAsync("/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Original)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var copy = world.Documents.Values.Single(r => r.Id != originalId);
        copy.CanonicalDocumentId.Should().Be(originalId, "precondition: the override recorded a hash-linked copy");

        // The copy is now the open document: task 013 resolves it, so the pane's DEFAULT is a version save of it.
        // The caller holds the same rights on their new document as on the one they copied.
        world.AccessByDocumentId[copy.Id] = world.AccessByDocumentId[originalId];

        var resave = await client.PostAsJsonAsync("/api/office/save", OfficeVersionSaveWorld.VersionSave(copy.Id, Edited));

        resave.StatusCode.Should().Be(HttpStatusCode.Accepted);
        copy.CanonicalDocumentId.Should().BeNull("the link is severed the moment the copy diverges (graduation)");
        copy.CanonicalHash.Should().Be(OfficeVersionSaveWorld.Hash(Edited), "the graduated copy stamps its own identity");
        world.GenericUpdates
            .Where(u => u.Id == copy.Id
                && u.Fields.TryGetValue("sprk_canonicaldocument", out var link) && link is DBNull)
            .Should().ContainSingle();

        world.SpeItems[copy.ItemId!].Versions.Should().HaveCount(2, "the edit is a new version of the copy's own item");
        world.Documents.Should().HaveCount(2, "graduation never creates a row");
        world.DocumentCreates.Should().Be(1);
        world.DeletedItemIds.Should().BeEmpty();

        // The canonical it was linked to is untouched throughout.
        world.SpeItems[originalItemId].Versions.Should().HaveCount(1);
        world.Documents[originalId].CanonicalHash.Should().Be(OfficeVersionSaveWorld.Hash(Original));
        world.GenericUpdates.Should().NotContain(u => u.Id == originalId);
    }

    [Fact]
    public async Task SaveAsNewDocument_WithDifferentBytes_CreatesAnUnlinkedDocumentThatIsItsOwnCanonical()
    {
        // The link is a statement about byte-identity, not about where the document came from.
        var world = new OfficeVersionSaveWorld();
        var (originalId, _) = world.SeedDocument(DocumentDrive, "Brief.docx", Original);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Edited));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.DocumentCreates.Should().Be(1);
        var created = world.Documents.Values.Single(r => r.Id != originalId);
        created.CanonicalDocumentId.Should().BeNull();
        created.CanonicalHash.Should().Be(OfficeVersionSaveWorld.Hash(Edited));
        world.GenericUpdates.Should().NotContain(u => u.Fields.ContainsKey("sprk_canonicaldocument"));
        world.DeletedItemIds.Should().BeEmpty();
    }
}
