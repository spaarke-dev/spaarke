using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.Shared.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// Task 029 (spaarkeai-word-add-in-r1) — the SAVE half of "re-profile and re-index after a version save": what
/// <c>POST /api/office/save</c> puts on the finalization message, which is everything the background pipeline gets
/// to tell a new version apart from a redelivery.
/// </summary>
/// <remarks>
/// <para><b>The failure mode.</b> A version save keeps the first save's <c>sprk_document</c> id and SPE item id,
/// and those were the only inputs to the profile key (<c>analysis-{documentId}-documentprofile</c>) and the index key
/// (<c>rag-index-{driveId}-{itemId}</c>). So every re-save was answered "already processed" and the profile and
/// Find results kept describing the first version.</para>
/// <para><b>The contract pinned here.</b> A version save stamps its OWN ProcessingJob id as
/// <c>VersionSaveJobId</c>; a first save and an Email save carry no such property at all, so their payloads — and
/// every key derived from them — are byte-for-byte what they were. The worker/handler half is
/// <c>VersionSaveAiRefreshSeamTests</c>.</para>
/// <para>Module-boundary doubles only (ADR-038 §4): the real route and <c>OfficeService</c> over
/// <see cref="OfficeVersionSaveWorld"/>, whose Service Bus double records each finalization payload.</para>
/// </remarks>
[Trait("status", "repaired")]
public class OfficeVersionSaveAiRefreshPayloadTests
{
    private const string DocumentDrive = "b!doc-drive";

    // FR-02 (task 014): REAL minimal .docx bytes, not a bare PK signature. Once the save path stamps the
    // document identity into the uploaded bytes, a zip signature with nothing behind it classifies CORRUPT and
    // the save is refused with OFFICE_021 — so a stub fixture would silently test a refusal instead of this
    // class's actual subject. Note the trap NOT taken: dropping the PK prefix also makes these tests pass,
    // because the stamper then treats the bytes as a non-OOXML payload and passes them through — green tests
    // over a production path that never stamps anything.
    private static readonly byte[] Original = MinimalDocx.Create("original");
    private static readonly byte[] DraftB = MinimalDocx.Create("draft B");
    private static readonly byte[] DraftA = MinimalDocx.Create("draft A");

    private static async Task<Guid> ReadJobIdAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("jobId").GetString()!);
    }

    private static bool HasProperty(JsonElement payload, string name) =>
        payload.EnumerateObject().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task VersionSave_StampsItsOwnJobId_OnTheFinalizationPayload()
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, _) = world.SeedDocument(DocumentDrive, "Brief.docx", Original);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, DraftB));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var jobId = await ReadJobIdAsync(response);

        var payload = world.FinalizationPayloads.Should().ContainSingle().Subject;
        Guid.Parse(OfficeVersionSaveWorld.PayloadValue(payload, "VersionSaveJobId")!).Should().Be(jobId,
            "the discriminator is the save's own ProcessingJob — the one thing a retry of this save repeats and a new save never does");
        OfficeVersionSaveWorld.PayloadValue(payload, "DocumentId").Should().Be(documentId.ToString("D"),
            "the version still finalizes against the EXISTING document");
    }

    [Fact]
    public async Task SuccessiveVersionSaves_EachStampTheirOwnJobId()
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, _) = world.SeedDocument(DocumentDrive, "Brief.docx", Original);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var target = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() };

        var first = await client.PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, DraftB) with { TargetEntity = target });
        var second = await client.PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, DraftA) with { TargetEntity = target });

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var jobIds = new[] { await ReadJobIdAsync(first), await ReadJobIdAsync(second) };

        world.FinalizationPayloads.Should().HaveCount(2);
        world.FinalizationPayloads
            .Select(p => Guid.Parse(OfficeVersionSaveWorld.PayloadValue(p, "VersionSaveJobId")!))
            .Should().Equal(jobIds, "each version is its own refresh, keyed to its own save");
        jobIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task FirstDocumentSave_CarriesNoVersionSaveJobId()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Fresh.docx", Original));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var payload = world.FinalizationPayloads.Should().ContainSingle().Subject;
        HasProperty(payload, "VersionSaveJobId").Should().BeFalse(
            "a first save's payload is unchanged, so its profile and index keys stay per-document and per-item");
    }

    [Fact]
    public async Task EmailSave_CarriesNoVersionSaveJobId()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", new SaveRequest
        {
            ContentType = SaveContentType.Email,
            TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
            Email = new EmailMetadata
            {
                Subject = "Status update",
                SenderEmail = "counsel@test.com",
                SentDate = new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero),
                InternetMessageId = "<status-029@test.com>",
                Body = "Filed today.",
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var payload = world.FinalizationPayloads.Should().ContainSingle().Subject;
        HasProperty(payload, "VersionSaveJobId").Should().BeFalse("an Email save is never a version");
    }
}
