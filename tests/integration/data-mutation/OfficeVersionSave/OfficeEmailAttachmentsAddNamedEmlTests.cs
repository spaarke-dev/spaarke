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
/// spaarkeai-word-add-in-r1 task 141 review (F1, F3, F4, F5), end to end through <c>POST /api/office/save</c>: an attachments add
/// that NAMES its saved <c>.eml</c> (<c>email.savedDocumentId</c>) is never answered from the response cache, adds only under the
/// email archive itself, gives its children the <c>.eml</c> row's own message keys, and is refused under an invoice
/// submission's <c>.eml</c>.
/// </summary>
/// <remarks>
/// Module-boundary doubles only (ADR-038 §4): the real route (its <c>IdempotencyFilter</c> over the in-memory cache) and
/// <c>OfficeService</c> over <see cref="OfficeVersionSaveWorld"/>. The request is JSON with the field added by name, as in
/// <c>OfficeEmailAttachmentsAddByDocumentTests</c>.
/// </remarks>
[Trait("status", "task-141-word-add-in-r1")]
public class OfficeEmailAttachmentsAddNamedEmlTests
{
    private const string RfcId = "<CAF0-141-named@mail.example.com>";
    private const string ConversationOfTheSavedEmail = "AAQkADconversation-of-the-saved-email";
    private const string A = "Settlement terms.pdf";
    private const string B = "Exhibit A.pdf";
    private const string C = "Exhibit B.pdf";

    private static readonly SaveEntityReference Matter = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("14114114-0141-4141-8141-0000000000ff"),
    };

    // ── F1: a named add is never replayed ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SameAddRepeated_UnderTheSameHeader_AfterItsChildWasRemoved_ReachesTheServiceAndQueuesTheAddAgain()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);

        var first = await client.SendAsync(Add(emlId, new[] { B }, "add-b-same-header"));
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.FinalizationPayloads.Should().HaveCount(2);

        // The worker created B, and the user then deleted it (or the worker failed to create it): B is missing again.
        var child = world.SeedAttachmentChild(emlId, B);
        world.Documents.Remove(child);

        var again = await client.SendAsync(Add(emlId, new[] { B }, "add-b-same-header"));

        again.StatusCode.Should().Be(HttpStatusCode.Accepted);
        again.Headers.TryGetValues("X-Idempotency-Status", out var status).Should().BeTrue();
        status.Should().NotContain("cached", "a named add is never answered from the response cache");
        world.FinalizationPayloads.Should().HaveCount(3, "the repeat reached the service, which planned the add again");
        Selection(world.FinalizationPayloads.Last()).Should().Equal(B);
    }

    // ── F3: only the email archive itself ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_NamingAForwardedEmlThatIsAnAttachmentChild_IsRefused_AndWritesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        var eml = world.Documents[emlId];

        // A forwarded email saved as an attachment CHILD of the saved email: a .eml file, filed to the same record, linked to
        // the same communication, holding the same bytes. It is not the email archive.
        var (childId, _) = world.SeedDocument(
            eml.DriveId!, "Forwarded.eml", world.SpeItems[eml.ItemId!].Versions.Last(), matterId: Matter.EntityId);
        world.Documents[childId].ParentDocumentId = emlId;
        world.Documents[childId].RelatedCommunicationId = eml.RelatedCommunicationId;
        var jobsBefore = world.Jobs.Count;

        var response = await client.SendAsync(Add(childId, new[] { B }, "add-under-child"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "refused as a document that is not a saved email");
        (await Body(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        world.FinalizationPayloads.Should().ContainSingle("the first save's only: nothing is queued under the child");
        world.Jobs.Should().HaveCount(jobsBefore);
        world.DocumentCreates.Should().Be(1);
    }

    // ── F4: the children take the .eml row's message keys ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_WhoseRequestCarriesAnotherConversationId_QueuesTheChildrenWithTheRowsConversationIndex()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        world.Documents[emlId].EmailConversationIndex.Should().Be(ConversationOfTheSavedEmail, "the first save stored it");

        var response = await client.SendAsync(Add(emlId, new[] { B }, "add-other-conversation", conversationId: "AAQkADanother-thread"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var metadata = Property(world.FinalizationPayloads.Last(), "EmailMetadata");
        Property(metadata, "ConversationId").GetString().Should().Be(ConversationOfTheSavedEmail, "from the .eml row");
        Property(metadata, "InternetMessageId").GetString().Should().Be(RfcId, "the row's own message id");
    }

    [Fact]
    public async Task Add_ToAnEmlWhoseRowKeysItselfDifferently_QueuesTheChildrenWithTheRowsMessageId_NotTheRequests()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        // The row's own key differs from the request's (it was saved by another path); the request is still THIS email, found
        // through the row's communication (IsTheNamedEmailAsync).
        world.Documents[emlId].EmailMessageId = "<row-own-key-141@mail.example.com>";
        world.Documents[emlId].RelatedCommunicationId.Should().NotBeNull("the first save linked its communication");

        var response = await client.SendAsync(Add(emlId, new[] { B }, "add-row-key"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var metadata = Property(world.FinalizationPayloads.Last(), "EmailMetadata");
        Property(metadata, "InternetMessageId").GetString().Should().Be("<row-own-key-141@mail.example.com>");
    }

    // ── F5: never under an invoice submission's .eml ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_ToAnInvoiceSubmissionsEml_IsRefused_AndWritesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        world.Documents[emlId].RestrictedBySubmission = true;
        var jobsBefore = world.Jobs.Count;

        var response = await client.SendAsync(Add(emlId, new[] { B }, "add-submission"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Body(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        world.FinalizationPayloads.Should().ContainSingle("nothing is queued under a submission's .eml");
        world.Jobs.Should().HaveCount(jobsBefore);
        world.DocumentCreates.Should().Be(1);
        world.DownloadCalls.Should().Be(0, "refused before the stored .eml is read");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The first save, with only <paramref name="saved"/> ticked, under its own body key, then its children.</summary>
    private static async Task<Guid> FirstSaveWithChildren(HttpClient client, OfficeVersionSaveWorld world, params string[] saved)
    {
        world.CommunicationsByMessageId[RfcId] = Guid.Parse("14114114-0141-4141-8141-00000000c0c0");
        var first = EmailSave(saved.ToList(), ConversationOfTheSavedEmail) with { IdempotencyKey = "first-save-named-eml" };
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save") { Content = JsonContent.Create(first) };
        message.Headers.Add("X-Idempotency-Key", "first-save-named-eml");
        (await client.SendAsync(message)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var emlId = world.Documents.Values.Should()
            .ContainSingle(d => d.FileName != null && d.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Subject.Id;
        world.Documents[emlId].IsEmailArchive.Should().BeTrue("the email save flags its .eml the email archive");
        foreach (var name in saved)
            world.SeedAttachmentChild(emlId, name);
        return emlId;
    }

    private static SaveRequest EmailSave(List<string>? selection, string conversationId) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = Matter,
        Email = new EmailMetadata
        {
            Subject = "RE: Settlement",
            SenderEmail = "counsel@test.com",
            SentDate = new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero),
            InternetMessageId = RfcId,
            ConversationId = conversationId,
            Body = "<p>Attached.</p>",
            IsBodyHtml = true,
            IsNameSystemDerived = true,
            Attachments = new List<AttachmentReference> { Pdf(A), Pdf(B), Pdf(C) },
            SelectedAttachmentFileNames = selection,
        },
    };

    /// <summary>The pane's add: the same email request, naming the saved <c>.eml</c> and only the ticked names.</summary>
    private static HttpRequestMessage Add(
        Guid savedDocumentId, string[] names, string headerKey, string conversationId = ConversationOfTheSavedEmail)
    {
        var json = JsonSerializer.SerializeToNode(EmailSave(names.ToList(), conversationId))!.AsObject();
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
