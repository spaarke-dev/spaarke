using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// A typed-name collision on an IMMUTABLE save (Email / Attachment) never overwrites another document's file
/// (spaarkeai-word-add-in-r1 task 054 — the unimplemented half of the 2026-09-15 owner decision).
/// </summary>
/// <remarks>
/// <para><b>The failure mode.</b> Task 046 (b) gave a SYSTEM-DERIVED <c>.eml</c> name a unique suffix, and the
/// owner decided a name the user TYPED is never changed — its collisions go to task 025's refuse-and-ask prompt
/// instead. Task 025 shipped that prompt for <c>SaveContentType.Document</c> ONLY, because a blanket
/// <c>ConflictBehavior.Fail</c> regressed <see cref="OfficeImmutableSaveFileSafetyTests"/>. So two DIFFERENT
/// emails saved under the same typed name still collapsed to ONE stored file: the second save's path-keyed
/// <c>Replace</c> landed on the first email's item, the first document's row went on pointing at a file that now
/// held the second email, and the pane reported success.</para>
/// <para><b>Why a name alone could not decide it.</b> An immutable capture's safety net is content-hash dedup,
/// which runs AFTER the upload because the hash is SPE's; a name refusal must decide BEFORE it. Two saves of the
/// same capture are a duplicate suppress is right to collapse; two different emails sharing a typed name are two
/// documents. Only the CONTENT separates them, so the collision resolver compares the request's bytes against the
/// owning document's stored file (<c>OfficeStorageUploader.ItemHoldsContentAsync</c>, task 047) before anything is
/// written: identical continues as today's duplicate, different (or unreadable) is refused.</para>
/// <para>Module-boundary doubles only (ADR-038 §4): the real route, filters, <c>OfficeService</c>,
/// <c>OfficeStorageUploader</c>, <c>OfficeDocumentPersistence</c> and <c>ContentDedupDetector</c> over
/// <see cref="OfficeVersionSaveWorld"/>, whose drive is path-keyed exactly as the facade is.</para>
/// </remarks>
[Trait("status", "repaired")]
public class OfficeTypedNameCollisionTests
{
    /// <summary>Where a matter-targeted save lands in this fixture (<c>EmailProcessing:DefaultContainerId</c>).</summary>
    private const string SaveContainer = "b!test-office-save-drive";

    /// <summary>The readable name for subject "Re: Filing" sent 2026-09-14 (':' is stripped by the sanitizer).</summary>
    private const string TypedName = "2026-09-14_Re Filing.eml";

    private static readonly DateTimeOffset Sent = new(2026, 9, 14, 9, 30, 0, TimeSpan.Zero);

    private static readonly byte[] ExhibitBytes = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
    private static readonly byte[] DifferentBytes = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };

    private static SaveEntityReference NewRecord() => new() { EntityType = "matter", EntityId = Guid.NewGuid() };

    /// <summary>
    /// An Outlook pane save whose Document Name the USER typed: <c>isNameSystemDerived: false</c>, so task 046 (b)
    /// adds no unique suffix and two different emails ask for the very same stored name.
    /// </summary>
    private static SaveRequest TypedNameEmailSave(SaveEntityReference record, string messageId, string body) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = record,
        Email = new EmailMetadata
        {
            Subject = "Re: Filing",
            SenderEmail = "counsel@test.com",
            SentDate = Sent,
            InternetMessageId = messageId,
            Body = body,
            IsNameSystemDerived = false,
        },
    };

    private static SaveRequest AttachmentSave(string fileName, byte[] bytes) => new()
    {
        ContentType = SaveContentType.Attachment,
        TargetEntity = NewRecord(),
        Attachment = new AttachmentMetadata
        {
            AttachmentId = $"att-{Guid.NewGuid():N}",
            FileName = fileName,
            ContentBase64 = Convert.ToBase64String(bytes),
        },
    };

    private static List<OfficeVersionSaveWorld.SpeItem> EmlItems(OfficeVersionSaveWorld world) =>
        world.SpeItems.Values.Where(i => i.Name.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>The string-valued top-level properties of a ProblemDetails body. A null-valued extension (an
    /// absent <c>existingDocumentId</c>) is therefore absent from the result, which is what the tests assert on.
    /// Mirrors <see cref="OfficeCreateCollisionTests"/>' own helper.</summary>
    private static async Task<Dictionary<string, string?>> ReadProblemAsync(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(property => property.Name, property => property.Value.GetString());
    }

    private static async Task<JobStatusResponse> JobOf(HttpClient client, SaveResponse saved)
    {
        var response = await client.GetAsync($"/api/office/jobs/{saved.JobId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JobStatusResponse>())!;
    }

    // ── The defect: two different emails, one typed name ──────────────────────────────────────────────

    [Fact]
    public async Task TwoDifferentEmails_UnderTheSameTypedName_LeaveTheFirstEmailsFileIntact_AndRefuseTheSecond()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var record = NewRecord();

        (await client.PostAsJsonAsync("/api/office/save", TypedNameEmailSave(record, "<first@test.com>", "First reply.")))
            .StatusCode.Should().Be(HttpStatusCode.Accepted, "precondition: the first email is filed under the typed name");

        var second = await client.PostAsJsonAsync(
            "/api/office/save", TypedNameEmailSave(record, "<second@test.com>", "Second reply."));

        // Before task 054 this is where the data was lost: the second save's Replace landed on the FIRST
        // email's item, so the stored file held "Second reply." while the first document's row still pointed
        // at it — and the response was a 202.
        var stored = EmlItems(world).Should().ContainSingle(
            "the refused second save wrote no file of its own").Subject;
        Encoding.UTF8.GetString(stored.Versions[^1]).Should().Contain("First reply.",
            "the first email's file still holds the FIRST email — it was never overwritten");
        stored.Versions.Should().ContainSingle("the refused save added no version to it either");

        second.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a different email under a name that is already taken is refused, never written over it");
        var problem = await ReadProblemAsync(second);
        problem.Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_020",
            "the refusal reuses task 025's collision contract rather than minting a second one");
        problem.Should().ContainKey("fileName").WhoseValue.Should().Be(TypedName);

        world.DocumentCreates.Should().Be(1, "only the first email became an sprk_document");
        world.DeletedItemIds.Should().BeEmpty("a refusal deletes nothing");
    }

    [Fact]
    public async Task ImmutableNameCollisionRefusal_OffersNoVersionSaveRetry_BecauseOnlyDocumentSavesHonourExistingDocumentId()
    {
        // An Email/Attachment save carrying document.existingDocumentId IGNORES it and creates its own
        // document (OfficeVersionSaveContractTests pins that). Advertising the version-save retry here would
        // therefore offer the pane a choice the server does not honour, so the refusal deliberately omits it.
        var world = new OfficeVersionSaveWorld();
        var (_, existingItemId) = world.SeedDocument(SaveContainer, "Exhibit A.pdf", ExhibitBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", AttachmentSave("Exhibit A.pdf", DifferentBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await ReadProblemAsync(response);
        problem.Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_020");
        problem.Should().ContainKey("fileName").WhoseValue.Should().Be("Exhibit A.pdf");
        problem.Should().NotContainKey("existingDocumentId",
            "an immutable capture has no version-save retry to offer");

        world.SpeItems[existingItemId].Versions.Should().ContainSingle("the existing file is untouched");
        world.SpeItems[existingItemId].Versions[0].Should().Equal(ExhibitBytes);
        world.DocumentCreates.Should().Be(0, "the refused save created no row");
    }

    // ── The other arm: byte-identical content is still the duplicate suppress is for ───────────────────

    [Fact]
    public async Task ByteIdenticalAttachmentSave_UnderAnExistingDocumentsName_IsResolvedToAReplace_NotARefusal()
    {
        // The collision IS detected (CollisionRefusals == 1 — SPE refused the first PUT), and then RESOLVED by
        // comparing content: the stored file already holds exactly these bytes, so the save continues under
        // Replace and the dedup branch suppresses it, byte-for-byte as before task 054. This is the arm that
        // makes OfficeImmutableSaveFileSafetyTests still pass unmodified.
        var world = new OfficeVersionSaveWorld();
        var (canonicalId, canonicalItemId) = world.SeedDocument(SaveContainer, "Exhibit A.pdf", ExhibitBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/office/save", AttachmentSave("Exhibit A.pdf", ExhibitBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "a byte-identical capture is a duplicate, not a clash");
        world.CollisionRefusals.Should().Be(1, "SPE still refuses the first PUT — the resolution happens after it");
        world.UploadSmallCalls.Should().Be(1, "and exactly one upload then wrote, under Replace");
        world.DocumentCreates.Should().Be(0, "the duplicate is suppressed, as it always was");
        world.DeletedItemIds.Should().NotContain(canonicalItemId, "the item is the existing document's own file");
        world.SpeItems[canonicalItemId].Versions[^1].Should().Equal(ExhibitBytes);

        var job = await JobOf(client, (await response.Content.ReadFromJsonAsync<SaveResponse>())!);
        job.CurrentPhase.Should().Be("DeduplicatedToExisting");
        job.Result!.Artifact!.Id.Should().Be(canonicalId, "the duplicate is reported against the canonical");
    }

    // ── Fail-safe: an unknown is never "identical" ────────────────────────────────────────────────────

    [Fact]
    public async Task ImmutableNameCollision_WhenTheExistingFileCannotBeRead_IsRefused_AndOverwritesNothing()
    {
        // The content comparison is the only thing that can authorise the Replace. When it cannot be made, the
        // save is refused: a refusal is recoverable (rename and retry), overwriting another document's file is
        // not. Same direction as task 046's cleanup guard.
        var world = new OfficeVersionSaveWorld { FailDownloads = true };
        var (_, existingItemId) = world.SeedDocument(SaveContainer, "Exhibit A.pdf", ExhibitBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", AttachmentSave("Exhibit A.pdf", ExhibitBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "unreadable content cannot prove the save is a duplicate, so it is not treated as one");
        (await ReadProblemAsync(response)).Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_020");
        world.SpeItems[existingItemId].Versions.Should().ContainSingle("nothing was written to the existing file");
        world.DocumentCreates.Should().Be(0);
    }

    // ── Task 025's reclaim rule extends unchanged: an unowned name is not a lockout ────────────────────

    [Fact]
    public async Task ImmutableNameCollision_WithNoOwningDocument_IsStillReclaimed_NotRefused()
    {
        // An orphan left by an earlier attempt that uploaded but failed before its row was written. Nothing
        // owns the name, so no document's bytes are at risk and Replace is still the right answer — otherwise
        // a transient failure would permanently lock out every retry under that name (task 025).
        var world = new OfficeVersionSaveWorld();
        world.SeedDocument(SaveContainer, "Exhibit A.pdf", ExhibitBytes, withPointers: false);
        var orphanItemId = world.SpeItems.Values.Single(i => i.Name == "Exhibit A.pdf").Id;
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", AttachmentSave("Exhibit A.pdf", DifferentBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "an unowned name is reclaimed, never refused");
        world.CollisionRefusals.Should().Be(1, "SPE refused the first PUT; the reclaim followed it");
        world.SpeItems[orphanItemId].Versions[^1].Should().Equal(DifferentBytes, "the reclaimed item now holds this save");
        world.DocumentCreates.Should().Be(1, "the save went on to create its own document");
    }
}
