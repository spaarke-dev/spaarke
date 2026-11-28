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
/// spaarkeai-word-add-in-r1 task 143 (fix B; owner 2026-11-27: an email's <c>.eml</c> and its communication are always filed to
/// the same record), end to end through <c>POST /api/office/save</c>: an attachments add that names its saved <c>.eml</c> applies
/// ONE "filed" rule (<c>EmailAttachmentAddPlanner.IsPlacedHereAsync</c>) — the <c>.eml</c>'s own record link, else the record
/// its <c>sprk_communication</c> is filed to — the rule the email identity route reports to the pane (task 132's fallback).
/// </summary>
/// <remarks>
/// Module-boundary doubles only (ADR-038 §4): the real route and <c>OfficeService</c> over <see cref="OfficeVersionSaveWorld"/>,
/// as <c>OfficeEmailAttachmentsAddUnfiledEmlTests</c>. The communication's filing is the world's
/// <see cref="OfficeVersionSaveWorld.CommunicationFiling"/>.
/// </remarks>
[Trait("status", "task-143-word-add-in-r1")]
public class OfficeEmailAttachmentsAddCommunicationFilingTests
{
    private const string RfcId = "<CAF0-143-comm-filing@mail.example.com>";
    private const string A = "Settlement terms.pdf";
    private const string B = "Exhibit A.pdf";
    private const string C = "Exhibit B.pdf";
    private static readonly Guid CommunicationId = Guid.Parse("14314314-0143-4143-8143-00000000c0c1");

    private static readonly SaveEntityReference MatterX = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("14314314-0143-4143-8143-0000000000a1"),
    };

    private static readonly SaveEntityReference MatterY = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("14314314-0143-4143-8143-0000000000b1"),
    };

    // ── Must fire ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddNamingX_UnderAnEmlFiledOnlyThroughItsCommunicationToX_QueuesTheAttachments_FiledToX()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await UnfiledEmlWithCommunication(client, world);
        FileCommunicationTo(world, MatterX);

        var response = await client.SendAsync(Add(emlId, new[] { B }, MatterX, "add-x-via-communication"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "the .eml is filed to X through its communication");
        world.FinalizationPayloads.Should().HaveCount(2, "the first save's, then the add's");
        var payload = world.FinalizationPayloads.Last();
        Selection(payload).Should().Equal(B);
        Property(payload, "AssociationType").GetString().Should().Be("matter", "the new attachment documents are filed to X");
        Property(payload, "AssociationId").GetGuid().Should().Be(MatterX.EntityId);
        world.Documents[emlId].MatterId.Should().BeNull("the add never writes the .eml's own link (that is task 144)");
    }

    [Fact]
    public async Task AddWithNoRecord_UnderAnEmlWhoseCommunicationIsFiledToNothing_StillQueues()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await UnfiledEmlWithCommunication(client, world);

        var response = await client.SendAsync(Add(emlId, new[] { B }, target: null, "add-truly-unfiled"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "neither the .eml nor its communication is filed");
        world.FinalizationPayloads.Should().HaveCount(2);
        Selection(world.FinalizationPayloads.Last()).Should().Equal(B);
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddWithNoRecord_UnderAnEmlFiledOnlyThroughItsCommunication_IsRefused_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await UnfiledEmlWithCommunication(client, world);
        FileCommunicationTo(world, MatterX);
        var jobsBefore = world.Jobs.Count;

        var response = await client.SendAsync(Add(emlId, new[] { B }, target: null, "add-none-via-communication"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the .eml is filed to X through its communication");
        (await Body(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        world.FinalizationPayloads.Should().ContainSingle("nothing is queued");
        world.Jobs.Should().HaveCount(jobsBefore);
    }

    [Fact]
    public async Task AddNamingX_UnderAnEmlWhoseCommunicationIsFiledToY_IsRefused_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await UnfiledEmlWithCommunication(client, world);
        FileCommunicationTo(world, MatterY);

        var response = await client.SendAsync(Add(emlId, new[] { B }, MatterX, "add-x-comm-y"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Body(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        world.FinalizationPayloads.Should().ContainSingle();
    }

    [Fact]
    public async Task AddNamingX_WhenTheCommunicationCannotBeRead_IsRefusedRetryably_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await UnfiledEmlWithCommunication(client, world);
        FileCommunicationTo(world, MatterX);
        world.FaultingCommunicationReads.Add(CommunicationId);

        var response = await client.SendAsync(Add(emlId, new[] { B }, MatterX, "add-x-comm-fault"));

        response.IsSuccessStatusCode.Should().BeFalse("where the .eml is filed is not known: fail closed");
        (await Body(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_014", "the retryable read failure");
        world.FinalizationPayloads.Should().ContainSingle();
    }

    [Fact]
    public async Task AddWithNoRecord_WhenTheCommunicationCannotBeRead_IsRefused_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await UnfiledEmlWithCommunication(client, world);
        world.FaultingCommunicationReads.Add(CommunicationId);

        var response = await client.SendAsync(Add(emlId, new[] { B }, target: null, "add-none-comm-fault"));

        response.IsSuccessStatusCode.Should().BeFalse("an unreadable communication may be filed: never read as unfiled");
        (await Body(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_014");
        world.FinalizationPayloads.Should().ContainSingle();
    }

    [Fact]
    public async Task AnEmlsOwnLink_Wins_OverItsCommunication()
    {
        // Filed to X by its own link, its communication to Y: the own link decides (the communication is not read).
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await UnfiledEmlWithCommunication(client, world);
        world.Documents[emlId].MatterId = MatterX.EntityId;
        FileCommunicationTo(world, MatterY);
        world.FaultingCommunicationReads.Add(CommunicationId);

        var response = await client.SendAsync(Add(emlId, new[] { B }, MatterX, "add-own-link"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        Property(world.FinalizationPayloads.Last(), "AssociationId").GetGuid().Should().Be(MatterX.EntityId);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A first save with NO record and only <see cref="A"/> ticked: an .eml with no record link, linked to its communication.</summary>
    private static async Task<Guid> UnfiledEmlWithCommunication(HttpClient client, OfficeVersionSaveWorld world)
    {
        world.CommunicationsByMessageId[RfcId] = CommunicationId;
        var first = EmailSave(new List<string> { A }, target: null) with { IdempotencyKey = "first-save-143-b" };
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save") { Content = JsonContent.Create(first) };
        message.Headers.Add("X-Idempotency-Key", "first-save-143-b");
        (await client.SendAsync(message)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var emlId = world.Documents.Values.Should()
            .ContainSingle(d => d.FileName != null && d.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Subject.Id;
        var eml = world.Documents[emlId];
        eml.MatterId.Should().BeNull("precondition: no record link of its own");
        eml.RelatedMatterId.Should().BeNull();
        eml.RelatedCommunicationId.Should().Be(CommunicationId, "precondition: linked to its communication");
        // The row's own message id matches the request, so the named-email check never reads the communication: the filing
        // rule's read is the one each test exercises.
        eml.EmailMessageId = RfcId;
        world.SeedAttachmentChild(emlId, A);
        return emlId;
    }

    private static void FileCommunicationTo(OfficeVersionSaveWorld world, SaveEntityReference matter) =>
        world.CommunicationFiling[CommunicationId] =
            ("sprk_regardingmatter", new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matter.EntityId));

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
