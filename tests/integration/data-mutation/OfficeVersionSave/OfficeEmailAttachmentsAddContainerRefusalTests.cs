using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 143 review (F5): an attachments add under a named saved <c>.eml</c> that the container guard
/// refuses (the <c>.eml</c> is kept in another container than the one this save goes to) says why in words that fit the
/// request. With no record, the save goes where the CALLER's documents are kept, so the refusal does not speak of a record;
/// with a record, it keeps task 141's wording about that record's documents.
/// </summary>
/// <remarks>
/// Module-boundary doubles only (ADR-038 §4): the real route and <c>OfficeService</c> over <see cref="OfficeVersionSaveWorld"/>,
/// as <c>OfficeEmailAttachmentsAddUnfiledEmlTests</c>.
/// </remarks>
[Trait("status", "task-143-word-add-in-r1")]
public class OfficeEmailAttachmentsAddContainerRefusalTests
{
    private const string RfcId = "<CAF0-143-container@mail.example.com>";
    private const string A = "Settlement terms.pdf";
    private const string B = "Exhibit A.pdf";

    private static readonly SaveEntityReference MatterA = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("14314314-0143-4143-8143-0000000000cc"),
    };

    [Fact]
    public async Task AddWithNoRecord_RefusedByTheContainerGuard_SaysTheEmailsDocumentsAreKeptElsewhere_WithoutMentioningARecord()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSave(client, world, target: null);
        world.Documents[emlId].DriveId = "b!another-container-143";

        var response = await client.SendAsync(Add(emlId, target: null, "add-no-record-elsewhere"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await Body(response);
        body.GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        var detail = body.GetProperty("detail").GetString();
        detail.Should().Be(
            "This saved email's documents are kept in a different place than yours, so attachments can't be added to it. "
            + "Nothing was saved.");
        detail.Should().NotContainEquivalentOf("record", "the request names no record");
        world.FinalizationPayloads.Should().ContainSingle("no child is queued in another container");
    }

    [Fact]
    public async Task AddNamingARecord_RefusedByTheContainerGuard_KeepsTheRecordWording()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSave(client, world, MatterA);
        world.Documents[emlId].DriveId = "b!another-container-143";

        var response = await client.SendAsync(Add(emlId, MatterA, "add-record-elsewhere"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await Body(response);
        body.GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        body.GetProperty("detail").GetString().Should().Be(
            "This record's documents are now kept somewhere else, so attachments can't be added to this saved email. "
            + "Nothing was saved.");
        world.FinalizationPayloads.Should().ContainSingle();
    }

    // ── Helpers (as OfficeEmailAttachmentsAddUnfiledEmlTests) ──────────────────────────────────────────────────────

    /// <summary>The first save to <paramref name="target"/> (or none), with only <see cref="A"/> ticked, then its child.</summary>
    private static async Task<Guid> FirstSave(HttpClient client, OfficeVersionSaveWorld world, SaveEntityReference? target)
    {
        world.CommunicationsByMessageId[RfcId] = Guid.Parse("14314314-0143-4143-8143-00000000c0c1");
        var first = EmailSave(new List<string> { A }, target) with { IdempotencyKey = "first-save-143-container" };
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save") { Content = JsonContent.Create(first) };
        message.Headers.Add("X-Idempotency-Key", "first-save-143-container");
        (await client.SendAsync(message)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var emlId = world.Documents.Values.Should()
            .ContainSingle(d => d.FileName != null && d.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Subject.Id;
        world.Documents[emlId].MatterId.Should().Be(target?.EntityId, "precondition: filed as the first save asked");
        world.SeedAttachmentChild(emlId, A);
        return emlId;
    }

    private static SaveRequest EmailSave(List<string>? selection, SaveEntityReference? target) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = target,
        Email = new EmailMetadata
        {
            Subject = "RE: Settlement container",
            SenderEmail = "counsel@test.com",
            SentDate = new DateTimeOffset(2026, 11, 27, 9, 30, 0, TimeSpan.Zero),
            InternetMessageId = RfcId,
            Body = "<p>Attached.</p>",
            IsBodyHtml = true,
            IsNameSystemDerived = true,
            Attachments = new List<AttachmentReference> { Pdf(A), Pdf(B) },
            SelectedAttachmentFileNames = selection,
        },
    };

    /// <summary>The pane's add: the same email request, naming the saved <c>.eml</c>, <see cref="B"/> and the record (or none).</summary>
    private static HttpRequestMessage Add(Guid savedDocumentId, SaveEntityReference? target, string headerKey)
    {
        var json = JsonSerializer.SerializeToNode(EmailSave(new List<string> { B }, target))!.AsObject();
        json["Email"]!.AsObject()["savedDocumentId"] = savedDocumentId.ToString("D");
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save")
        {
            Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("X-Idempotency-Key", headerKey);
        return message;
    }

    private static AttachmentReference Pdf(string name) => new()
    {
        AttachmentId = $"att-{name}",
        FileName = name,
        ContentType = "application/pdf",
        Size = 20_000,
        ContentBase64 = Convert.ToBase64String(new byte[20_000]),
    };

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
