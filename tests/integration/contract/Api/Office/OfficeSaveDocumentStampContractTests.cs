using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Shared.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// FR-02 (spaarkeai-word-add-in-r1 task 014) at the <c>POST /api/office/save</c> boundary: the identity stamp
/// is written into the bytes that reach storage, on both the version path and the create path.
/// </summary>
/// <remarks>
/// <para><b>What these tests add over the unit-level stamper tests.</b> <c>OfficeDocumentStampTests</c> proves
/// the stamper's byte behaviour in isolation. What can only be proven HERE, through the real route, filters and
/// <c>OfficeService</c>, is that the bytes which actually reach SPE are the stamped ones, that the id stamped
/// on a CREATE is the same id the <c>sprk_document</c> row ends up carrying (the pre-assigned-key contract),
/// and that a refusal happens before any storage write rather than after.</para>
/// <para>Module-boundary doubles only (ADR-038 §4) over <see cref="OfficeVersionSaveWorld"/>, whose SPE drive
/// retains the exact bytes each write delivered — which is what lets these tests read the stamp back out of
/// "storage" rather than trusting a recorded call.</para>
/// </remarks>
[Trait("status", "repaired")]
public class OfficeSaveDocumentStampContractTests
{
    private const string DocumentDrive = "b!doc-drive";

    private static async Task<string?> ErrorCodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    // ── AC1: a document saved through the pane carries the stamp ──────────────────────────────────

    [Fact]
    public async Task Post_OfficeSave_DocumentCreate_StampsTheCreatedRowsOwnIdIntoTheStoredBytes()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", MinimalDocx.Create("first draft")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var created = world.Documents.Values.Should().ContainSingle().Subject;
        var stored = world.SpeItems[created.ItemId!].Versions.Should().ContainSingle().Subject;

        // The whole point of pre-assigning the key: the stored file and the row that owns it agree on identity.
        OfficeDocumentStamp.TryReadStamp(stored).Should().Be(created.Id,
            "the id stamped into the uploaded bytes IS the primary key the row was created with");
        MinimalDocx.ReadBodyText(stored).Should().Be("first draft", "stamping does not disturb the body");
    }

    [Fact]
    public async Task Post_OfficeSave_VersionSave_StampsTheTargetDocumentsIdIntoTheNewVersion()
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument(DocumentDrive, "Brief.docx", MinimalDocx.Create("v1"));
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, MinimalDocx.Create("v2")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var stored = world.SpeItems[itemId].Versions[^1];
        OfficeDocumentStamp.TryReadStamp(stored).Should().Be(documentId);
        MinimalDocx.ReadBodyText(stored).Should().Be("v2");
    }

    [Fact]
    public async Task Post_OfficeSave_OfAnAlreadyStampedDocument_LeavesExactlyOneStampPart()
    {
        // The round-trip case FR-02 exists for: a document downloaded from Spaarke, edited locally and
        // re-uploaded. Its bytes already carry the stamp, so parts must not accumulate on each save.
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument(DocumentDrive, "Brief.docx", MinimalDocx.Create("v1"));
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        (await client.PostAsJsonAsync("/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, MinimalDocx.Create("v2"))))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var alreadyStamped = world.SpeItems[itemId].Versions[^1];
        var response = await client.PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, alreadyStamped, comment: "re-uploaded"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var stored = world.SpeItems[itemId].Versions[^1];
        OfficeDocumentStamp.TryReadStamp(stored).Should().Be(documentId);
        StampPartCount(stored).Should().Be(1, "re-saving a stamped document must not accumulate stamp parts");
    }

    // ── AC3: a document saved BEFORE this release is unaffected ───────────────────────────────────

    [Fact]
    public async Task Post_OfficeSave_VersionSaveOfAnUnstampedPreReleaseDocument_Succeeds_AndStampsOnlyTheNewVersion()
    {
        // Forward-only (owner decision 2026-09-04): the seeded version stands for bytes stored before FR-02.
        // It is never rewritten — only the version this save writes carries a stamp.
        var world = new OfficeVersionSaveWorld();
        var preRelease = MinimalDocx.Create("pre-release");
        var (documentId, itemId) = world.SeedDocument(DocumentDrive, "Brief.docx", preRelease);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, MinimalDocx.Create("after release")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.SpeItems[itemId].Versions.Should().HaveCount(2);
        world.SpeItems[itemId].Versions[0].Should().Equal(preRelease,
            "the pre-release version's stored bytes are byte-for-byte untouched — there is no backfill");
        OfficeDocumentStamp.TryReadStamp(world.SpeItems[itemId].Versions[0]).Should().BeNull();
        OfficeDocumentStamp.TryReadStamp(world.SpeItems[itemId].Versions[^1]).Should().Be(documentId);
    }

    // ── Negative: a non-OOXML payload on the same path is stored untouched ────────────────────────

    [Theory]
    [InlineData("Exhibit A.pdf", "%PDF-1.7\n1 0 obj\n<<>>\nendobj\ntrailer\n%%EOF")]
    [InlineData("Message.eml", "From: a@b.test\r\nSubject: filed\r\n\r\nbody text")]
    public async Task Post_OfficeSave_DocumentWithANonOoxmlPayload_StoresTheBytesUnmodified_AndDoesNotFail(
        string fileName, string payload)
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var bytes = Encoding.UTF8.GetBytes(payload);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave(fileName, bytes));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "a non-Word payload is stored, never refused");
        var created = world.Documents.Values.Should().ContainSingle().Subject;
        world.SpeItems[created.ItemId!].Versions.Should().ContainSingle()
            .Which.Should().Equal(bytes, "the stamp applies only where an OOXML package is present");
        created.FileSize.Should().Be(bytes.Length);
    }

    // ── Negative: corrupt bytes are refused BEFORE any storage write ──────────────────────────────

    [Fact]
    public async Task Post_OfficeSave_DocumentWithCorruptPackageBytes_Returns400Office021_AndWritesNothingToStorage()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        // A zip signature with nothing readable behind it: claims to be a package, is not one.
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Broken.docx", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x11 }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await ErrorCodeOf(response)).Should().Be("OFFICE_021",
            "distinct from OFFICE_020, which is task 025's name collision");

        world.UploadSmallCalls.Should().Be(0, "the refusal precedes any SPE write — no partial bytes are stored");
        world.ReplaceCalls.Should().Be(0);
        world.SpeItems.Should().BeEmpty();
        world.DocumentCreates.Should().Be(0, "no sprk_document is created for a file that cannot be read");
        world.JobStatuses.Values.Should().ContainSingle().Which.Should().Be(3, "the save's job is marked Failed");
    }

    [Fact]
    public async Task Post_OfficeSave_VersionSaveWithCorruptPackageBytes_IsRefused_AndLeavesTheExistingVersionIntact()
    {
        var world = new OfficeVersionSaveWorld();
        var intact = MinimalDocx.Create("intact");
        var (documentId, itemId) = world.SeedDocument(DocumentDrive, "Brief.docx", intact);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x11 }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeOf(response)).Should().Be("OFFICE_021");
        world.ReplaceCalls.Should().Be(0);
        world.SpeItems[itemId].Versions.Should().ContainSingle()
            .Which.Should().Equal(intact, "a refused version save leaves the document exactly as it was");
    }

    // ── ADR-049 independence + Email/Attachment are never stamped ─────────────────────────────────

    [Fact]
    public async Task Post_OfficeSave_EmailSave_IsNeverStamped()
    {
        // Email and Attachment are immutable captures, not Word documents: they are not even inspected.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", new SaveRequest
        {
            ContentType = SaveContentType.Email,
            TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
            Email = new EmailMetadata { Subject = "Filing", SenderEmail = "sender@test.com", InternetMessageId = "<m1@test>" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = world.Documents.Values.Should().ContainSingle().Subject;
        OfficeDocumentStamp.TryReadStamp(world.SpeItems[created.ItemId!].Versions[^1]).Should().BeNull();
    }

    /// <summary>
    /// How many parts in the package ARE a Spaarke stamp — identified the same way the reader identifies one:
    /// an exact namespace match on the part's ROOT element.
    /// </summary>
    /// <remarks>
    /// Deliberately not a substring scan for the namespace. A correctly stamped package mentions the namespace
    /// in TWO parts — the stamp itself and its <c>itemProps</c> sibling, whose <c>ds:schemaRef ds:uri</c> names
    /// the schema — so a text scan answers 2 for a perfectly correct package and would report accumulation that
    /// is not happening.
    /// </remarks>
    private static int StampPartCount(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        var count = 0;
        foreach (var entry in zip.Entries)
        {
            using var part = entry.Open();
            try
            {
                var root = System.Xml.Linq.XDocument.Load(part).Root;
                if (root?.Name.NamespaceName == OfficeDocumentStamp.StampNamespace)
                {
                    count++;
                }
            }
            catch (System.Xml.XmlException)
            {
                // Not an XML part; cannot be a stamp.
            }
        }

        return count;
    }
}
