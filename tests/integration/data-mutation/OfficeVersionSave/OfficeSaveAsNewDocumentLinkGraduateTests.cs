using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.Shared.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// NFR-08 on the FR-11 "Save as new document" override (spaarkeai-word-add-in-r1 task 024, amended by task
/// 014): when the user chooses to save an IDENTIFIED document as a new one, the result is ONE NEW
/// <c>sprk_document</c> — never the immutable suppress mode that deletes the upload and answers with the
/// canonical's id.
/// </summary>
/// <remarks>
/// <para><b>⚠️ Amended 2026-09-17 by FR-02 stamping (task 014), owner-accepted.</b> These tests previously
/// asserted that a byte-identical override also wrote <c>sprk_canonicaldocument</c> → the canonical (the
/// editable LINK half of link/graduate). That is no longer reachable for a Word-pane save, and the reason is
/// structural rather than incidental: content identity is the hash of the bytes as STORED, and the save now
/// stamps each record's OWN id into those bytes. Two byte-identical local drafts saved to different records
/// therefore have different stored bytes by construction, so they can never hash-equal.</para>
///
/// <para><b>What changed, and what did not.</b> Spec SC-5 and NFR-08 were amended on 2026-09-17 (see the
/// "NFR-08 / content identity" row in spec.md's ADR Tensions table). The <b>binding half is unchanged and is
/// what these tests still defend</b>: a create MUST always create, and MUST NEVER use the immutable suppress
/// path — suppress-forever on an editable document collapses two distinct drafts into one record, which is
/// data loss. Only the <i>link</i> became best-effort: a lost link costs a notification, never a record and
/// never a byte.</para>
///
/// <para><b>Why the graduation scenario is not duplicated here.</b> With no link produced on this path, a link
/// only exists for rows that predate FR-02 (or came from a non-Word path). Graduation is therefore covered
/// where such a row can actually be constructed — <c>OfficeVersionSaveOneRowTests</c>, in this same
/// data-mutation KEEP path: one test for a diverged copy, one for the §6b case where the stamp alone causes
/// divergence. No scenario was dropped in this migration, only relocated to where its precondition is real.</para>
///
/// <para><b>Why the in-memory world.</b> Rows, SPE items and deletions are counted before and after, and the
/// columns are read back off the row — a recorded call alone would pass even if it targeted the wrong row.
/// Module-boundary doubles only (ADR-038 §4: <c>IDataverseService</c>, the <c>SpeFileStore</c> facade,
/// <c>IAccessDataSource</c>, the Service Bus client; no <c>Mock&lt;HttpMessageHandler&gt;</c>).</para>
/// </remarks>
[Trait("status", "repaired")]
public class OfficeSaveAsNewDocumentLinkGraduateTests
{
    private const string DocumentDrive = "b!doc-drive";

    // FR-02 (task 014): REAL minimal .docx bytes. A bare PK signature classifies CORRUPT and every save here
    // would be refused with OFFICE_021; dropping the PK prefix instead would make the stamper pass the bytes
    // through, leaving these tests green while production stamped nothing — the false green this class is the
    // primary example of, because it is the link assertion above that would have stayed misleadingly true.
    private static readonly byte[] Original = MinimalDocx.Create("original");
    private static readonly byte[] Edited = MinimalDocx.Create("edited");

    [Fact]
    public async Task SaveAsNewDocument_ByteIdenticalToTheIdentifiedDocument_CreatesOneNewRow_AndIsNeverSuppressed()
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

        // ── The binding half of NFR-08: exactly ONE new row, and the suppress path never runs. ──
        world.DocumentCreates.Should().Be(1);
        world.Documents.Should().HaveCount(2);
        var copy = world.Documents.Values.Single(r => r.Id != originalId);

        world.DeletedItemIds.Should().BeEmpty(
            "suppress deletes the just-uploaded blob and redirects the user to the canonical; an editable save must never");
        var payload = world.FinalizationPayloads.Should().ContainSingle().Subject;
        OfficeVersionSaveWorld.PayloadValue(payload, "DocumentId")
            .Should().Be(copy.Id.ToString("D"), "the response is never redirected to the canonical record");

        // A genuinely new SPE item — so sprk_graphitemid_uk is satisfied naturally (NFR-07), nothing relaxed.
        copy.ItemId.Should().NotBeNull().And.NotBe(originalItemId);
        world.SpeItems.Should().HaveCount(2);

        // ── The amended half (SC-5, 2026-09-17): no hash link, and THIS is why. ──
        var storedCopyBytes = world.SpeItems[copy.ItemId!].Versions.Should().ContainSingle().Subject;
        OfficeDocumentStamp.TryReadStamp(storedCopyBytes).Should().Be(copy.Id,
            "the stored bytes carry the NEW record's own identity — which is precisely what makes them differ "
            + "from the canonical's, so a content-hash link can never be found");

        copy.CanonicalDocumentId.Should().BeNull(
            "amended SC-5: the stamp makes two byte-identical drafts differ once stored, so no link is produced");
        copy.CanonicalHash.Should().Be(OfficeVersionSaveWorld.Hash(storedCopyBytes),
            "content identity is the hash of the bytes as STORED, stamp included");
        world.GenericUpdates.Should().NotContain(u => u.Fields.ContainsKey("sprk_canonicaldocument"),
            "no link is written for a Word-pane save; a lost link costs a notification, never a record");

        // ── The original record and its file are untouched throughout. ──
        world.SpeItems[originalItemId].Versions.Should().HaveCount(1);
        MinimalDocx.ReadBodyText(world.SpeItems[originalItemId].Versions[0]).Should().Be("original");
        world.DocumentUpdates.Should().NotContain(u => u.DocumentId == originalId.ToString("D"));
        world.GenericUpdates.Should().NotContain(u => u.Id == originalId);
        world.Documents[originalId].CanonicalDocumentId.Should().BeNull();
        world.ReplaceCalls.Should().Be(0, "the override writes no version of any existing item");
    }

    [Fact]
    public async Task TheCopyMadeByTheOverride_WhenEditedAndSavedAgain_VersionsItsOwnItem_AndCreatesNoRow()
    {
        // The override's output must be a normal, independently versionable document: the pane resolves it as
        // the open document, so its DEFAULT next save is a version of IT — not of the record it was copied from.
        var world = new OfficeVersionSaveWorld();
        var (originalId, originalItemId) = world.SeedDocument(DocumentDrive, "Brief.docx", Original);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        (await client.PostAsJsonAsync("/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Original)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var copy = world.Documents.Values.Single(r => r.Id != originalId);

        // The caller holds the same rights on their new document as on the one they copied (a test-fixture fact
        // about real Dataverse access, not something the create path grants).
        world.AccessByDocumentId[copy.Id] = world.AccessByDocumentId[originalId];

        var resave = await client.PostAsJsonAsync("/api/office/save", OfficeVersionSaveWorld.VersionSave(copy.Id, Edited));

        resave.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.SpeItems[copy.ItemId!].Versions.Should().HaveCount(2, "the edit is a new version of the COPY's own item");
        MinimalDocx.ReadBodyText(world.SpeItems[copy.ItemId!].Versions[^1]).Should().Be("edited");
        OfficeDocumentStamp.TryReadStamp(world.SpeItems[copy.ItemId!].Versions[^1]).Should().Be(copy.Id,
            "a version save re-stamps the row it writes to, so the file keeps naming its own record");

        world.Documents.Should().HaveCount(2, "a version save never creates a row");
        world.DocumentCreates.Should().Be(1, "only the override's own create");
        world.DeletedItemIds.Should().BeEmpty();

        // The record it was copied from is untouched throughout.
        world.SpeItems[originalItemId].Versions.Should().HaveCount(1);
        world.GenericUpdates.Should().NotContain(u => u.Id == originalId);
    }

    [Fact]
    public async Task SaveAsNewDocument_WithDifferentBytes_CreatesAnUnlinkedDocumentThatIsItsOwnCanonical()
    {
        // Unchanged by task 014: differing content was never linked, so this case has no amendment.
        var world = new OfficeVersionSaveWorld();
        var (originalId, _) = world.SeedDocument(DocumentDrive, "Brief.docx", Original);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Edited));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.DocumentCreates.Should().Be(1);
        var created = world.Documents.Values.Single(r => r.Id != originalId);
        created.CanonicalDocumentId.Should().BeNull();
        created.CanonicalHash.Should().Be(
            OfficeVersionSaveWorld.Hash(world.SpeItems[created.ItemId!].Versions[^1]),
            "the row stamps the content identity of the bytes as stored");
        world.GenericUpdates.Should().NotContain(u => u.Fields.ContainsKey("sprk_canonicaldocument"));
        world.DeletedItemIds.Should().BeEmpty();
    }
}
