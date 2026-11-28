using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 143, end to end through <c>POST /api/office/save</c>: an attachments add that names its saved
/// <c>.eml</c> (<c>email.savedDocumentId</c>) and NO record adds under an <c>.eml</c> filed to no record
/// (<c>EmailAttachmentAddPlanner.IsFiledToNoRecord</c>), and is refused under an <c>.eml</c> filed to a record. An add naming a
/// record keeps task 141's rule: the <c>.eml</c> must be filed to that record.
/// </summary>
/// <remarks>
/// Module-boundary doubles only (ADR-038 §4): the real route (its filters and the request validator) and <c>OfficeService</c>
/// over <see cref="OfficeVersionSaveWorld"/>, as <c>OfficeEmailAttachmentsAddNamedEmlTests</c>.
/// </remarks>
[Trait("status", "task-143-word-add-in-r1")]
public class OfficeEmailAttachmentsAddUnfiledEmlTests
{
    private const string RfcId = "<CAF0-143-unfiled@mail.example.com>";
    private const string A = "Settlement terms.pdf";
    private const string B = "Exhibit A.pdf";
    private const string C = "Exhibit B.pdf";

    private static readonly SaveEntityReference MatterA = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("14314314-0143-4143-8143-0000000000aa"),
    };

    private static readonly SaveEntityReference MatterB = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("14314314-0143-4143-8143-0000000000bb"),
    };

    // ── Must fire ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddWithNoRecord_UnderAnEmlFiledToNoRecord_QueuesTheNamedAttachments()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSave(client, world, target: null, A);

        var response = await client.SendAsync(Add(emlId, new[] { B }, target: null, "add-unfiled"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "an add with no record under an unfiled .eml is allowed");
        world.FinalizationPayloads.Should().HaveCount(2, "the first save's, then the add's");
        Selection(world.FinalizationPayloads.Last()).Should().Equal(B);
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddWithNoRecord_UnderAnEmlFiledToAMatter_IsRefused_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSave(client, world, MatterA, A);
        var jobsBefore = world.Jobs.Count;

        var response = await client.SendAsync(Add(emlId, new[] { B }, target: null, "add-no-record-filed"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Body(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        world.FinalizationPayloads.Should().ContainSingle("nothing is added under an .eml filed to a record");
        world.Jobs.Should().HaveCount(jobsBefore);
        world.DocumentCreates.Should().Be(1);
    }

    [Fact]
    public async Task AddWithNoRecord_UnderAnEmlFiledOnlyThroughItsRelatedMatter_IsRefused()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSave(client, world, target: null, A);
        world.Documents[emlId].RelatedMatterId = MatterB.EntityId; // filed since, through the second family

        var response = await client.SendAsync(Add(emlId, new[] { B }, target: null, "add-related-matter"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        world.FinalizationPayloads.Should().ContainSingle();
    }

    [Fact]
    public async Task AddNamingMatterA_UnderAnEmlFiledToMatterB_IsStillRefused()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSave(client, world, MatterB, A);

        var response = await client.SendAsync(Add(emlId, new[] { B }, MatterA, "add-a-under-b"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Body(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        world.FinalizationPayloads.Should().ContainSingle();
    }

    [Fact]
    public async Task AddNamingARecord_UnderAnEmlFiledToNoRecord_IsRefused()
    {
        // The add never files the .eml: naming a record it is not filed to is refused, as in task 141.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSave(client, world, target: null, A);

        var response = await client.SendAsync(Add(emlId, new[] { B }, MatterA, "add-record-under-unfiled"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        world.FinalizationPayloads.Should().ContainSingle();
    }

    [Fact]
    public async Task AddWithNoRecord_WithoutWriteOnTheEml_IsRefused_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSave(client, world, target: null, A);
        world.AccessByDocumentId[emlId] = AccessRights.Read;

        var response = await client.SendAsync(Add(emlId, new[] { B }, target: null, "add-no-write"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the route's filter needs Write on the named .eml");
        world.FinalizationPayloads.Should().ContainSingle();
        world.DownloadCalls.Should().Be(0, "refused before the stored .eml is read");
    }

    [Fact]
    public async Task AddWithNoRecord_WhenTheCallersDocumentsAreKeptElsewhere_IsRefused_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSave(client, world, target: null, A);
        // The .eml is kept in another container than the one a no-record save of this caller goes to.
        world.Documents[emlId].DriveId = "b!another-container-143";

        var response = await client.SendAsync(Add(emlId, new[] { B }, target: null, "add-other-container"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Body(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        world.FinalizationPayloads.Should().ContainSingle("no child is queued in another container");
    }

    [Fact]
    public async Task AddWithNoRecord_WithoutAttachmentNames_IsStillRefusedByTheValidator()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSave(client, world, target: null, A);

        var response = await client.SendAsync(Add(emlId, Array.Empty<string>(), target: null, "add-no-names"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        world.FinalizationPayloads.Should().ContainSingle();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The first save to <paramref name="target"/> (or none), with only <paramref name="saved"/> ticked, then its children.</summary>
    private static async Task<Guid> FirstSave(
        HttpClient client, OfficeVersionSaveWorld world, SaveEntityReference? target, params string[] saved)
    {
        world.CommunicationsByMessageId[RfcId] = Guid.Parse("14314314-0143-4143-8143-00000000c0c0");
        var first = EmailSave(saved.ToList(), target) with { IdempotencyKey = "first-save-143" };
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save") { Content = JsonContent.Create(first) };
        message.Headers.Add("X-Idempotency-Key", "first-save-143");
        (await client.SendAsync(message)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var emlId = world.Documents.Values.Should()
            .ContainSingle(d => d.FileName != null && d.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Subject.Id;
        world.Documents[emlId].IsEmailArchive.Should().BeTrue();
        world.Documents[emlId].MatterId.Should().Be(target?.EntityId, "precondition: filed as the first save asked");
        foreach (var name in saved)
            world.SeedAttachmentChild(emlId, name);
        return emlId;
    }

    private static SaveRequest EmailSave(List<string>? selection, SaveEntityReference? target) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = target,
        Email = new EmailMetadata
        {
            Subject = "RE: Settlement",
            SenderEmail = "counsel@test.com",
            SentDate = new DateTimeOffset(2026, 11, 27, 9, 30, 0, TimeSpan.Zero),
            InternetMessageId = RfcId,
            Body = "<p>Attached.</p>",
            IsBodyHtml = true,
            IsNameSystemDerived = true,
            Attachments = new List<AttachmentReference> { Pdf(A), Pdf(B), Pdf(C) },
            SelectedAttachmentFileNames = selection,
        },
    };

    /// <summary>The pane's add: the same email request, naming the saved <c>.eml</c>, the ticked names and the record (or none).</summary>
    private static HttpRequestMessage Add(Guid savedDocumentId, string[] names, SaveEntityReference? target, string headerKey)
    {
        var json = JsonSerializer.SerializeToNode(EmailSave(names.ToList(), target))!.AsObject();
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

    private static JsonElement Property(JsonElement element, string name) =>
        element.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static List<string> Selection(JsonElement payload) =>
        Property(Property(payload, "EmailMetadata"), "SelectedAttachmentFileNames").EnumerateArray()
            .Select(e => e.GetString()!).ToList();
}
