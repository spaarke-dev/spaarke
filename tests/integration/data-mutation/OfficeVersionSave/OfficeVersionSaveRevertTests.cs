using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// A version save that REPEATS an earlier version's content (spaarkeai-word-add-in-r1 task 047). A user saves
/// version B of a document, then A, then B again. Both de-duplication layers keyed that third save on content alone,
/// so it was answered with the FIRST B save's job and never written: SPE stayed at A while the pane reported success.
/// <list type="bullet">
/// <item><b>The response cache</b> (<c>IdempotencyFilter</c>, 24 hours): the pane's <c>X-Idempotency-Key</c> for a
/// version save is <c>sha256(existingDocumentId, fileName, contentSha256, failedAttempts)</c>, so the third save
/// re-sends the first save's header and was replayed (<c>X-Idempotency-Status: cached</c>).</item>
/// <item><b>The persistent job lookup</b> (no time window): the body key, or the server's own key
/// (<c>…|version-content:{hash}</c>), names the first save's Completed ProcessingJob, which made the request a
/// Duplicate.</item>
/// </list>
/// A true retry of ONE save must still write once and be answered with that save's job.
/// </summary>
/// <remarks>
/// WRITES are counted in <see cref="OfficeVersionSaveWorld"/>: the item's SPE version history and its ProcessingJob
/// rows. Module-boundary doubles only (ADR-038 §4); the real route, filters and <c>IdempotencyFilter</c> over the
/// in-memory distributed cache.
/// </remarks>
[Trait("status", "repaired")]
public class OfficeVersionSaveRevertTests
{
    private const string DocumentDrive = "b!doc-drive";

    private static readonly byte[] Initial = { 0x50, 0x4B, 0x03, 0x04, 0x30 };
    private static readonly byte[] B = { 0x50, 0x4B, 0x03, 0x04, 0x42, 0x42 };
    private static readonly byte[] A = { 0x50, 0x4B, 0x03, 0x04, 0x41, 0x41, 0x41 };

    private static readonly SaveEntityReference Target = new() { EntityType = "matter", EntityId = Guid.NewGuid() };

    private static HttpRequestMessage Post(SaveRequest body, string? headerKey)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save") { Content = JsonContent.Create(body) };
        if (headerKey is not null)
            message.Headers.Add("X-Idempotency-Key", headerKey);
        return message;
    }

    /// <summary>
    /// Stand-in for the pane's version key (<c>useSaveFlow.ts</c> <c>computeIdempotencyKey</c> with
    /// <c>VersionIdempotencyParts</c>): the same document and the same content give the same key, whatever happened
    /// in between. No failed attempts here, so the counter adds nothing.
    /// </summary>
    private static string PaneKey(Guid documentId, byte[] content) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{documentId:D}|Brief.docx|{Convert.ToBase64String(content)}"))).ToLowerInvariant();

    /// <summary>A version save to one fixed record, so the server's own key depends on the content alone.</summary>
    private static SaveRequest Save(Guid documentId, byte[] content, string? bodyKey = null) =>
        OfficeVersionSaveWorld.VersionSave(documentId, content) with { TargetEntity = Target, IdempotencyKey = bodyKey };

    /// <summary>The pane's request: its content-only key in the body AND in the header.</summary>
    private static HttpRequestMessage PanePost(Guid documentId, byte[] content)
    {
        var key = PaneKey(documentId, content);
        return Post(Save(documentId, content, key), key);
    }

    private static async Task<SaveResponse> BodyOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<SaveResponse>())!;

    // ── B, then A, then B: every save is written ──────────────────────────────────────────────────────

    [Fact]
    public async Task VersionSaves_B_A_B_UnderThePanesKeys_WriteThreeVersions()
    {
        // The shipped pane: the third save re-sends the first save's key in the header AND the body.
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument(DocumentDrive, "Brief.docx", Initial);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        (await client.SendAsync(PanePost(documentId, B))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.SendAsync(PanePost(documentId, A))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var third = await client.SendAsync(PanePost(documentId, B));

        world.SpeItems[itemId].Versions.Should().HaveCount(4,
            "the seed plus three saves: the third save of B follows a different written version, so it is written");
        world.SpeItems[itemId].Versions[^1].Should().Equal(B, "SPE holds what the user saved last");
        third.StatusCode.Should().Be(HttpStatusCode.Accepted);
        third.Headers.GetValues("X-Idempotency-Status").Should().Equal(new[] { "new" },
            "the response cache must not replay the first B save's 202");
        (await BodyOf(third)).Duplicate.Should().BeFalse();
        world.Jobs.Should().HaveCount(3);
        world.Documents.Should().HaveCount(1, "a version never creates a row");
    }

    [Fact]
    public async Task VersionSaves_B_A_B_WithTheContentKeyInTheBody_AndAFreshHeaderEachTime_WriteThreeVersions()
    {
        // The response cache misses (a different header each time), so this isolates the PERSISTENT job lookup.
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument(DocumentDrive, "Brief.docx", Initial);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        (await client.SendAsync(Post(Save(documentId, B, PaneKey(documentId, B)), "attempt-1")))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.SendAsync(Post(Save(documentId, A, PaneKey(documentId, A)), "attempt-2")))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var third = await client.SendAsync(Post(Save(documentId, B, PaneKey(documentId, B)), "attempt-3"));

        third.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "the first B save's Completed job no longer describes the document, which now holds A");
        (await BodyOf(third)).Duplicate.Should().BeFalse();
        world.SpeItems[itemId].Versions.Should().HaveCount(4);
        world.SpeItems[itemId].Versions[^1].Should().Equal(B);
        world.Jobs.Should().HaveCount(3);
    }

    [Fact]
    public async Task VersionSaves_B_A_B_WithNoClientKey_WriteThreeVersions()
    {
        // No header and no body key: the server's own content-aware key decides — every client that sends none.
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument(DocumentDrive, "Brief.docx", Initial);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        (await client.SendAsync(Post(Save(documentId, B), headerKey: null))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.SendAsync(Post(Save(documentId, A), headerKey: null))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var third = await client.SendAsync(Post(Save(documentId, B), headerKey: null));

        third.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await BodyOf(third)).Duplicate.Should().BeFalse();
        world.SpeItems[itemId].Versions.Should().HaveCount(4);
        world.SpeItems[itemId].Versions[^1].Should().Equal(B);
        world.Jobs.Should().HaveCount(3);
    }

    // ── The same save sent twice: written once, answered with the first save's job ────────────────────

    [Fact]
    public async Task TheSameVersionSave_SentTwice_UnderThePanesKeys_WritesOnce_AndTheSecondReturnsTheFirstJob()
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument(DocumentDrive, "Brief.docx", Initial);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        var first = await client.SendAsync(PanePost(documentId, B));
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var retry = await client.SendAsync(PanePost(documentId, B));

        retry.IsSuccessStatusCode.Should().BeTrue();
        (await BodyOf(retry)).JobId.Should().Be((await BodyOf(first)).JobId, "a retry is answered with the save it repeats");
        world.SpeItems[itemId].Versions.Should().HaveCount(2, "the retry writes nothing new");
        world.Jobs.Should().ContainSingle();
    }

    [Fact]
    public async Task TheSameVersionSave_SentTwice_WithNoClientKey_WritesOnce_AndTheSecondIsTheFirstJobsDuplicate()
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument(DocumentDrive, "Brief.docx", Initial);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        var first = await client.SendAsync(Post(Save(documentId, B), headerKey: null));
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var retry = await client.SendAsync(Post(Save(documentId, B), headerKey: null));

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyOf(retry);
        body.Duplicate.Should().BeTrue();
        body.JobId.Should().Be((await BodyOf(first)).JobId);
        world.SpeItems[itemId].Versions.Should().HaveCount(2);
        world.Jobs.Should().ContainSingle();
    }

    [Fact]
    public async Task ASecondIdenticalVersionSave_WhenTheDocumentsCurrentContentCannotBeRead_IsWritten_NotAnsweredFromTheOldJob()
    {
        // The decision's failure direction, pinned: "Duplicate" is answered only when the document is PROVEN to hold
        // these bytes. When that cannot be read, the save is written — at worst one more identical version, never a
        // lost one.
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument(DocumentDrive, "Brief.docx", Initial);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        (await client.SendAsync(PanePost(documentId, B))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.FailDownloads = true;
        var again = await client.SendAsync(PanePost(documentId, B));

        again.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await BodyOf(again)).Duplicate.Should().BeFalse();
        world.SpeItems[itemId].Versions.Should().HaveCount(3);
        world.Jobs.Should().HaveCount(2);
    }

    // ── Task 039's rule holds: a Failed or Cancelled attempt is not a duplicate ──────────────────────

    [Fact]
    public async Task RetryAfterACancelledVersionSave_WithTheSameKey_RunsAgain()
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument(DocumentDrive, "Brief.docx", Initial);
        world.LockedItemIds.Add(itemId);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        (await client.SendAsync(Post(Save(documentId, B), headerKey: null))).StatusCode.Should().Be((HttpStatusCode)423);
        var jobId = world.JobStatuses.Keys.Should().ContainSingle().Subject;
        world.JobStatuses[jobId] = 4; // the refused attempt's job, now Cancelled
        world.LockedItemIds.Remove(itemId);

        var retry = await client.SendAsync(Post(Save(documentId, B), headerKey: null));

        retry.StatusCode.Should().Be(HttpStatusCode.Accepted, "a cancelled attempt is not a performed operation");
        world.SpeItems[itemId].Versions.Should().HaveCount(2);
        world.SpeItems[itemId].Versions[^1].Should().Equal(B);
        world.Jobs.Should().HaveCount(2);
    }

    // ── Email and Attachment: unchanged ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmailSave_ResentWithNoClientKey_IsStillAnsweredDuplicate_AndReadsNoFile()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var request = new SaveRequest
        {
            ContentType = SaveContentType.Email,
            TargetEntity = Target,
            Email = new EmailMetadata { Subject = "Filing", SenderEmail = "sender@test.com", InternetMessageId = "<m1@test>" },
        };

        var first = await client.SendAsync(Post(request, headerKey: null));
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var resent = await client.SendAsync(Post(request, headerKey: null));

        resent.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyOf(resent);
        body.Duplicate.Should().BeTrue();
        body.JobId.Should().Be((await BodyOf(first)).JobId);
        world.DocumentCreates.Should().Be(1);
        world.Jobs.Should().ContainSingle();
        world.DownloadCalls.Should().Be(0, "an immutable message's duplicate is decided by its key alone, as before");
    }

    [Fact]
    public async Task AttachmentSave_ResentUnderTheSameHeader_IsStillReplayedFromTheResponseCache()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var request = new SaveRequest
        {
            ContentType = SaveContentType.Attachment,
            TargetEntity = Target,
            Attachment = new AttachmentMetadata
            {
                AttachmentId = "att-1",
                FileName = "Exhibit A.pdf",
                ContentBase64 = Convert.ToBase64String(B),
            },
        };

        (await client.SendAsync(Post(request, "outlook-attachment-key"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var replay = await client.SendAsync(Post(request, "outlook-attachment-key"));

        replay.StatusCode.Should().Be(HttpStatusCode.Accepted);
        replay.Headers.GetValues("X-Idempotency-Status").Should().Equal(new[] { "cached" });
        world.UploadSmallCalls.Should().Be(1);
        world.Jobs.Should().ContainSingle();
        world.DownloadCalls.Should().Be(0);
    }
}
