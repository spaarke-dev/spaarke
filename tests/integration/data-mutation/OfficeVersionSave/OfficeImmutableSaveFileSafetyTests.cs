using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// An IMMUTABLE (Email / Attachment) save on <c>POST /api/office/save</c> never deletes an SPE file that a
/// <c>sprk_document</c> points at (spaarkeai-word-add-in-r1 task 046; the 025 analysis's M9 / R3, F-API-C3).
/// </summary>
/// <remarks>
/// <para><b>The failure mode.</b> Immutable content runs content-dedup in SUPPRESS mode: on a byte-identical hit the
/// save creates no row and deletes "the transient blob it just uploaded". The upload is PATH-keyed and states
/// <c>Replace</c>, so an upload whose name already exists in the container lands ON THAT EXISTING ITEM — and the
/// "transient blob" is then another document's own file. Deterministic for a direct Attachment save to a
/// different record under the canonical's name; the save reported success.</para>
/// <para><b>What "file intact" means here.</b> The item still exists in the world's drive and its current bytes are
/// the canonical's bytes. The Replace itself still adds an identical SPE version: that is the create path's
/// collision policy (task 025's <c>Fail</c> + <c>OFFICE_020</c>), not this task's.</para>
/// <para>Module-boundary doubles only (ADR-038 §4): the real route, filters, <c>OfficeService</c>,
/// <c>OfficeDocumentPersistence</c> and <c>ContentDedupDetector</c> over <see cref="OfficeVersionSaveWorld"/>.</para>
/// </remarks>
[Trait("status", "repaired")]
public class OfficeImmutableSaveFileSafetyTests
{
    /// <summary>Where a matter-targeted save lands in this fixture (<c>EmailProcessing:DefaultContainerId</c>).</summary>
    private const string SaveContainer = "b!test-office-save-drive";

    private static readonly byte[] ExhibitBytes = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };

    /// <summary>A direct Attachment save to a FRESH record — never the record the existing document belongs to.</summary>
    private static SaveRequest AttachmentSave(string fileName, byte[] bytes) => new()
    {
        ContentType = SaveContentType.Attachment,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
        Attachment = new AttachmentMetadata
        {
            AttachmentId = $"att-{Guid.NewGuid():N}",
            FileName = fileName,
            ContentBase64 = Convert.ToBase64String(bytes),
        },
    };

    private static async Task<JobStatusResponse> JobOf(HttpClient client, SaveResponse saved)
    {
        var response = await client.GetAsync($"/api/office/jobs/{saved.JobId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JobStatusResponse>())!;
    }

    [Fact]
    public async Task ByteIdenticalAttachmentSave_UnderTheCanonicalsNameInItsContainer_LeavesTheCanonicalsFileIntact()
    {
        var world = new OfficeVersionSaveWorld();
        var (canonicalId, canonicalItemId) = world.SeedDocument(SaveContainer, "Exhibit A.pdf", ExhibitBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", AttachmentSave("Exhibit A.pdf", ExhibitBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.UploadSmallCalls.Should().Be(1, "precondition: the save reached SPE");
        world.DeletedItemIds.Should().NotContain(canonicalItemId,
            "the upload landed on the canonical's own item, so that item is the canonical's file, not a transient blob");
        world.SpeItems.Should().ContainKey(canonicalItemId, "the canonical document's file still exists");
        world.SpeItems[canonicalItemId].Versions[^1].Should().Equal(ExhibitBytes, "its bytes are unchanged");
        world.Documents[canonicalId].ItemId.Should().Be(canonicalItemId, "the canonical row still points at that file");
    }

    [Fact]
    public async Task ByteIdenticalAttachmentSave_UnderTheCanonicalsNameInItsContainer_IsStillSuppressedAsADuplicate()
    {
        // AC2: the guard changes only the cleanup. The save is still a content duplicate of the canonical: no second
        // document, no finalization, and the completed job names the canonical so the pane opens it.
        var world = new OfficeVersionSaveWorld();
        var (canonicalId, _) = world.SeedDocument(SaveContainer, "Exhibit A.pdf", ExhibitBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/office/save", AttachmentSave("Exhibit A.pdf", ExhibitBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var saved = (await response.Content.ReadFromJsonAsync<SaveResponse>())!;
        saved.Duplicate.Should().BeFalse("a content duplicate is not an idempotent replay");
        world.DocumentCreates.Should().Be(0, "no second canonical document is created");
        world.Documents.Keys.Should().ContainSingle().Which.Should().Be(canonicalId);
        world.FinalizationPayloads.Should().BeEmpty("a suppressed duplicate is not finalized again");

        var job = await JobOf(client, saved);
        job.Status.Should().Be(JobStatus.Completed);
        job.CurrentPhase.Should().Be("DeduplicatedToExisting");
        job.Result!.Artifact!.Id.Should().Be(canonicalId, "the duplicate is reported against the canonical");
    }

    [Fact]
    public async Task ByteIdenticalAttachmentSave_UnderAHashLinkedCopysName_LeavesThatCopysFileIntact()
    {
        // The same defect, one row removed: the item the upload lands on belongs to a hash-linked COPY. The detector
        // (which excludes copies) resolves the TRUE canonical elsewhere, so comparing against the canonical's item
        // alone would still have deleted the copy's file.
        var world = new OfficeVersionSaveWorld();
        var (canonicalId, canonicalItemId) = world.SeedDocument("b!doc-drive", "Original.pdf", ExhibitBytes);
        var (copyId, copyItemId) = world.SeedDocument(
            SaveContainer, "Exhibit A.pdf", ExhibitBytes,
            linkedToCanonical: canonicalId, linkedHash: OfficeVersionSaveWorld.Hash(ExhibitBytes));
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", AttachmentSave("Exhibit A.pdf", ExhibitBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.DocumentCreates.Should().Be(0, "precondition: the immutable duplicate branch ran");
        world.DeletedItemIds.Should().BeEmpty("the upload landed on the linked copy's own item");
        world.SpeItems.Should().ContainKey(copyItemId, "the linked copy's file still exists");
        world.SpeItems[copyItemId].Versions[^1].Should().Equal(ExhibitBytes);
        world.Documents[copyId].ItemId.Should().Be(copyItemId);
        world.SpeItems.Should().ContainKey(canonicalItemId);
    }

    [Fact]
    public async Task DuplicateAttachmentCleanup_WhenTheFileReferenceCannotBeChecked_DeletesNothing()
    {
        // Fail-safe direction: the cleanup deletes only an item it has PROVEN no document points at. A different
        // name means the upload is a genuinely transient blob, but the reference lookup fails, so nothing is proven.
        var world = new OfficeVersionSaveWorld { FailDocumentReferenceLookup = true };
        world.SeedDocument("b!doc-drive", "Exhibit A.pdf", ExhibitBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/office/save", AttachmentSave("Exhibit A - received.pdf", ExhibitBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.DocumentCreates.Should().Be(0, "precondition: the immutable duplicate branch ran");
        world.DeletedItemIds.Should().BeEmpty(
            "a leaked transient blob is recoverable; deleting a file a document points at is not");
        var saved = (await response.Content.ReadFromJsonAsync<SaveResponse>())!;
        (await JobOf(client, saved)).CurrentPhase.Should().Be("DeduplicatedToExisting",
            "the save still completes as a duplicate; only the cleanup is skipped");
    }
}
