using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.Shared.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// Idempotency on the Office save spine (spaarkeai-word-add-in-r1 task 039) — the data-mutation half: WRITES are
/// counted, before and after, in <see cref="OfficeVersionSaveWorld"/>.
/// <list type="bullet">
/// <item><b>Finding 4 (silent lost edits).</b> A second CREATE save of EDITED content to the same record under the
/// same file name, within the 24-hour window, was answered from cache and never written — by the response cache
/// when the pane reused its content-free <c>X-Idempotency-Key</c>, and by the persistent job lookup when the
/// server's own create key (content-free) matched. Both now let the edited save reach the upload attempt (it is
/// not a cached/duplicate replay), while a byte-identical retry still writes nothing.</item>
/// <item><b>Finding 2 (failed jobs replay as duplicates).</b> A retry with the same key after a FAILED save was
/// answered <c>Duplicate</c> with the failed job and wrote nothing. A failed (or cancelled) job no longer counts,
/// and a save that throws after its job exists now marks that job Failed.</item>
/// <item><b>Task 025 (D1).</b> Reaching the upload attempt is no longer the same as reaching SPE unopposed: a
/// second create under the SAME name as the first now REFUSES with OFFICE_020 (name collision) rather than
/// silently overwriting the first document's file — see
/// <see cref="SecondCreateSave_OfEditedContent_UnderThePanesReusedHeaderKey_IsRefusedAsACollision"/> and its
/// no-client-key sibling, updated by task 025 from their pre-025 "is written" assertions per this class's own
/// former remarks ("the row-level outcome (the D1 collision) belongs to task 025").</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>What "written" means here.</b> The SPE upload reached the drive (<see cref="OfficeVersionSaveWorld.UploadSmallCalls"/>,
/// the item's version history) and a new ProcessingJob ran.</para>
/// <para>Module-boundary doubles only (ADR-038 §4); the real route, filters and <c>IdempotencyFilter</c> over the
/// in-memory distributed cache.</para>
/// </remarks>
[Trait("status", "repaired")]
public class OfficeSaveSpineIdempotencyTests
{
    private const string DefaultContainer = "b!test-office-save-drive";

    // FR-02 (task 014): REAL minimal .docx bytes — a bare PK signature now classifies CORRUPT (OFFICE_021),
    // and dropping the PK prefix would make the stamper pass the bytes through, which is the false green this
    // migration exists to avoid.
    private static readonly byte[] Original = MinimalDocx.Create("original");
    private static readonly byte[] Edited = MinimalDocx.Create("edited");

    private static HttpRequestMessage Post(SaveRequest body, string? headerKey)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save") { Content = JsonContent.Create(body) };
        if (headerKey is not null)
            message.Headers.Add("X-Idempotency-Key", headerKey);
        return message;
    }

    /// <summary>Task 025: the string-valued top-level properties of a ProblemDetails error body — mirrors the
    /// established <c>ReadProblemAsync</c> idiom in <c>OfficeQuickCreateContractTests</c> (a local copy rather
    /// than a shared extraction, per that file's own precedent of one per test class).</summary>
    private static async Task<Dictionary<string, string?>> ReadProblemAsync(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await System.Text.Json.JsonDocument.ParseAsync(stream);
        return document.RootElement.EnumerateObject()
            .Where(property => property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
            .ToDictionary(property => property.Name, property => property.Value.GetString());
    }

    /// <summary>Two create saves of the SAME record and file name — as the pane sends them — differing only in content.</summary>
    private static (SaveRequest First, SaveRequest Second) CreateSavesOf(byte[] first, byte[] second)
    {
        var target = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() };
        return (
            OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", first) with { TargetEntity = target },
            OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", second) with { TargetEntity = target });
    }

    private static OfficeVersionSaveWorld.SpeItem SavedItem(OfficeVersionSaveWorld world) =>
        world.SpeItems.Values.Should().ContainSingle(i => i.DriveId == DefaultContainer && i.Name == "Brief.docx").Subject;

    // ── Finding 4 ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SecondCreateSave_OfEditedContent_UnderThePanesReusedHeaderKey_IsRefusedAsACollision()
    {
        // The pane's create key is content-free (sourceType, record, document URL), so the SAME header rides
        // both — but the server's own PERSISTENT key is content-aware (task 039 finding 4), so the second
        // (edited) save is not a cached/duplicate replay: it reaches the upload attempt. Task 025: what happens
        // there is now a refusal (OFFICE_020), not the pre-025 silent overwrite of the first document's file
        // (D1) — this test's own name and assertions were "...IsWritten" before task 025 fixed exactly this.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var (first, second) = CreateSavesOf(Original, Edited);

        (await client.SendAsync(Post(first, "pane-create-key"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var response = await client.SendAsync(Post(second, "pane-create-key"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a same-name second create is refused before any bytes move, not silently written over the first");
        (await ReadProblemAsync(response)).Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_020");
        world.UploadSmallCalls.Should().Be(1, "the collision is refused before a second upload — the edited bytes never reach SPE");
        // Compared by body text, not raw bytes: what is stored is the first save's content plus its identity
        // stamp (FR-02, task 014). The claim being made is "the first document's CONTENT is untouched".
        MinimalDocx.ReadBodyText(SavedItem(world).Versions.Should().ContainSingle().Subject)
            .Should().Be("original", "the first document is provably unchanged by the refused second save");
        world.Jobs.Should().HaveCount(2, "the refused attempt still gets its own ProcessingJob row, marked Failed");
        world.Jobs.Select(j => j.IdempotencyKey).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task SecondCreateSave_OfEditedContent_WithNoClientKey_IsRefusedAsACollision()
    {
        // No header and no body key: the server's own create key decides whether the second save reaches the
        // upload attempt at all — it does, because that key is content-aware (a different document IS a
        // different operation). Task 025: the upload itself now refuses the name collision (OFFICE_020) rather
        // than silently overwriting the first document's file (D1).
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var (first, second) = CreateSavesOf(Original, Edited);

        (await client.SendAsync(Post(first, headerKey: null))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var response = await client.SendAsync(Post(second, headerKey: null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a different document is a different operation, so it reaches the upload — which refuses the name collision");
        (await ReadProblemAsync(response)).Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_020");
        world.UploadSmallCalls.Should().Be(1);
        MinimalDocx.ReadBodyText(SavedItem(world).Versions.Should().ContainSingle().Subject)
            .Should().Be("original");
        world.Jobs.Should().HaveCount(2);
    }

    [Fact]
    public async Task ByteIdenticalCreateRetry_UnderTheSameHeaderKey_IsReplayed_AndWritesNothing()
    {
        // The escalation guard: content in the key must not break a true retry.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var (first, _) = CreateSavesOf(Original, Original);

        (await client.SendAsync(Post(first, "pane-create-key"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var retry = await client.SendAsync(Post(first, "pane-create-key"));

        retry.IsSuccessStatusCode.Should().BeTrue();
        retry.Headers.GetValues("X-Idempotency-Status").Should().Equal(new[] { "cached" });
        world.UploadSmallCalls.Should().Be(1, "an identical retry writes nothing new");
        world.DocumentCreates.Should().Be(1);
        world.Jobs.Should().ContainSingle();
    }

    [Fact]
    public async Task ByteIdenticalCreateRetry_UnderADifferentHeaderKey_IsDeduplicatedByTheServerKey()
    {
        // The response cache misses (different header), so the PERSISTENT key must recognise the same bytes.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var (first, _) = CreateSavesOf(Original, Original);

        (await client.SendAsync(Post(first, "attempt-a"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var retry = await client.SendAsync(Post(first, "attempt-b"));

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        (await retry.Content.ReadFromJsonAsync<SaveResponse>())!.Duplicate.Should().BeTrue();
        world.UploadSmallCalls.Should().Be(1);
        world.DocumentCreates.Should().Be(1);
        world.Jobs.Should().ContainSingle();
    }

    // ── Finding 2 ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RetryAfterALockedVersionSave_WithTheSameServerKey_StartsANewAttempt_AndWritesTheVersion()
    {
        // No client key at all: the SERVER's key is the same for the refused attempt and the retry. The refused
        // attempt leaves a Failed ProcessingJob under that key.
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument("b!doc-drive", "Brief.docx", Original);
        world.LockedItemIds.Add(itemId);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var request = OfficeVersionSaveWorld.VersionSave(documentId, Edited);

        (await client.SendAsync(Post(request, headerKey: null))).StatusCode.Should().Be((HttpStatusCode)423);
        world.JobStatuses.Values.Should().ContainSingle().Which.Should().Be(3, "precondition: the refused attempt's job is Failed");

        world.LockedItemIds.Remove(itemId); // the other editor let go
        var retry = await client.SendAsync(Post(request, headerKey: null));

        retry.StatusCode.Should().Be(HttpStatusCode.Accepted, "a failed attempt is not a duplicate of the retry");
        (await retry.Content.ReadFromJsonAsync<SaveResponse>())!.Duplicate.Should().BeFalse();
        world.SpeItems[itemId].Versions.Should().HaveCount(2);
        MinimalDocx.ReadBodyText(world.SpeItems[itemId].Versions[^1]).Should().Be("edited");
        world.Jobs.Should().HaveCount(2);
        world.Documents.Should().HaveCount(1);
    }

    [Fact]
    public async Task RetryAfterACreateSaveThatThrewMidway_WithTheSameKey_StartsANewAttempt()
    {
        // The save fails AFTER its job row and its SPE upload exist. Before task 039 the job was left Running, so
        // the retry was answered Duplicate from it — and nothing was ever recorded.
        var world = new OfficeVersionSaveWorld { FailNextDocumentCreate = true };
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var (request, _) = CreateSavesOf(Original, Original);

        (await client.SendAsync(Post(request, headerKey: null))).IsSuccessStatusCode.Should().BeFalse();
        world.JobStatuses.Values.Should().ContainSingle().Which.Should().Be(3, "a save that threw marks its job Failed");
        world.DocumentCreates.Should().Be(0);

        var retry = await client.SendAsync(Post(request, headerKey: null));

        retry.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await retry.Content.ReadFromJsonAsync<SaveResponse>())!.Duplicate.Should().BeFalse();
        world.DocumentCreates.Should().Be(1, "the retry records the document the failed attempt never did");
        world.Jobs.Should().HaveCount(2);
    }
}
