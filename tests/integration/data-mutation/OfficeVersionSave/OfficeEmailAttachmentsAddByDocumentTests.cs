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
/// spaarkeai-word-add-in-r1 task 141, end to end through <c>POST /api/office/save</c>: the pane's "add these attachments to the
/// saved email" save, which NAMES the saved <c>.eml</c> (<c>email.savedDocumentId</c>) instead of relying on a job under the same
/// idempotency key.
/// </summary>
/// <remarks>
/// <para>The owner's test (2026-11-26): an email saved with 1 of its attachments, then the other ones ticked and saved — nothing
/// was added, because task 138's add ran only when a Completed job under the SAME key was found, and the key includes the
/// target. Every test here starts from a first save whose job is under ANOTHER key (a body key), so no job matches: the add must
/// work from the named document alone.</para>
/// <para>The request is sent as JSON with the field added by name, so the "fails before" run (the field unknown to the server)
/// compiles and runs the same requests. Module-boundary doubles only (ADR-038 §4): the real route and <c>OfficeService</c> over
/// <see cref="OfficeVersionSaveWorld"/>; the stored <c>.eml</c> is the bytes the first save uploaded.</para>
/// </remarks>
[Trait("status", "task-141-word-add-in-r1")]
public class OfficeEmailAttachmentsAddByDocumentTests
{
    private const string RfcId = "<CAF0-141-add@mail.example.com>";
    private const string A = "Settlement terms.pdf";
    private const string B = "Exhibit A.pdf";
    private const string C = "Exhibit B.pdf";

    private static readonly SaveEntityReference Matter = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("14114114-0141-4141-8141-0000000000ee"),
    };

    [Fact]
    public async Task Add_NamingTheSavedEmail_WithNoJobUnderTheKey_QueuesExactlyTheTickedOnes_UnderTheExistingEml()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);

        var response = await client.SendAsync(Add(emlId, new[] { B, C }, "add-bc"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "the add needs no earlier job under the same key");
        (await Names(response, "attachmentsAdded")).Should().BeEquivalentTo(new[] { B, C });
        world.DocumentCreates.Should().Be(1, "no second .eml; the save itself creates nothing");
        world.UploadSmallCalls.Should().Be(1, "the email is never uploaded again");
        world.SpeItems.Values.Count(i => i.Name.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Should().Be(1);

        var add = world.FinalizationPayloads.Should().HaveCount(2).And.Subject.Last();
        Bool(add, "AttachmentsOnly").Should().BeTrue();
        OfficeVersionSaveWorld.PayloadValue(add, "DocumentId").Should().Be(emlId.ToString(), "under the existing .eml");
        Selection(add).Should().BeEquivalentTo(new[] { B, C });
        OfficeVersionSaveWorld.PayloadValue(add, "OwningTeamId").Should().NotBeNullOrEmpty("the children are created team-owned");
    }

    [Fact]
    public async Task Add_NamingASavedNameAndAnUnsavedOne_QueuesOnlyTheUnsavedOne_NeverASecondChild()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);

        var response = await client.SendAsync(Add(emlId, new[] { A, B }, "add-ab"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Names(response, "attachmentsAdded")).Should().Equal(B);
        Selection(world.FinalizationPayloads.Last()).Should().Equal(B);
    }

    [Fact]
    public async Task Add_NamingOnlySavedNames_AddsNothing_AndAnswersWithTheSavedEmail_WritingNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        var jobsBefore = world.Jobs.Count;

        var response = await client.SendAsync(Add(emlId, new[] { A }, "add-a"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Body(response);
        body.GetProperty("duplicate").GetBoolean().Should().BeTrue();
        body.GetProperty("artifact").GetProperty("id").GetString().Should().Be(emlId.ToString("D"), "the pane completes on it");
        body.TryGetProperty("attachmentsAdded", out _).Should().BeFalse();
        world.FinalizationPayloads.Should().ContainSingle("the first save's only");
        world.Jobs.Should().HaveCount(jobsBefore, "nothing to add writes no job");
        world.DocumentCreates.Should().Be(1);
    }

    [Fact]
    public async Task Add_NamingAFileTheStoredEmailDoesNotHold_ReportsItNotStored_AndNeverCreatesItFromTheRequest()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);

        var response = await client.SendAsync(Add(emlId, new[] { B, "Not in the email.pdf" }, "add-not-stored"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Names(response, "attachmentsAdded")).Should().Equal(B);
        (await Names(response, "attachmentsNotStored")).Should().Equal("Not in the email.pdf");
        Selection(world.FinalizationPayloads.Last()).Should().Equal(new[] { B }, "only what the stored .eml holds is queued");

        var onlyNotStored = await client.SendAsync(Add(emlId, new[] { "Not in the email.pdf" }, "add-only-not-stored"));

        onlyNotStored.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Names(onlyNotStored, "attachmentsNotStored")).Should().Equal("Not in the email.pdf");
        world.FinalizationPayloads.Should().HaveCount(2, "nothing is queued for a name the .eml does not hold");
    }

    [Fact]
    public async Task Add_ByACallerWithoutWriteOnTheSavedEmail_Is403_AndWritesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        world.AccessByDocumentId[emlId] = AccessRights.Read;
        var jobsBefore = world.Jobs.Count;

        var response = await client.SendAsync(Add(emlId, new[] { B }, "add-no-write"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        world.FinalizationPayloads.Should().ContainSingle("no child is queued");
        world.Jobs.Should().HaveCount(jobsBefore, "refused before the job row");
        world.DocumentCreates.Should().Be(1, "the .eml is unchanged and nothing else is created");
        world.DownloadCalls.Should().Be(0, "a refused caller learns nothing from the stored .eml");
    }

    [Fact]
    public async Task Add_ToAnEmailNotFiledToTheNamedRecord_IsRefused_AndWritesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        var jobsBefore = world.Jobs.Count;
        var otherMatter = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() };

        var response = await client.SendAsync(Add(emlId, new[] { B }, "add-other-record", otherMatter));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        world.FinalizationPayloads.Should().ContainSingle();
        world.Jobs.Should().HaveCount(jobsBefore);
        world.DocumentCreates.Should().Be(1);
    }

    [Fact]
    public async Task Add_NamingADocumentThatIsNotASavedEmail_IsRefused_AndWritesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var (docId, _) = world.SeedDocument("drive-141", "Brief.docx", Encoding.UTF8.GetBytes("word"), matterId: Matter.EntityId);

        var response = await client.SendAsync(Add(docId, new[] { B }, "add-not-email"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        world.ShouldHaveWrittenNothing();
    }

    [Fact]
    public async Task Add_WhenTheStoredEmailCannotBeRead_IsARetryableRefusal_AndWritesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        var jobsBefore = world.Jobs.Count;
        world.FailDownloads = true;

        var response = await client.SendAsync(Add(emlId, new[] { B }, "add-unreadable"));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        world.FinalizationPayloads.Should().ContainSingle();
        world.Jobs.Should().HaveCount(jobsBefore);
    }

    [Fact]
    public async Task Add_WithoutNames_Is400_AndWithoutARecordUnderAFiledEml_IsRefused_AndWritesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        var jobsBefore = world.Jobs.Count;

        var noRecord = await client.SendAsync(Add(emlId, new[] { B }, "add-no-record", target: null, omitTarget: true));
        var noNames = await client.SendAsync(Add(emlId, Array.Empty<string>(), "add-no-names"));

        // Task 143: the record is optional; with none the add is allowed only under an .eml filed to no record, and this one is
        // filed to the matter (OFFICE_009).
        noRecord.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        noNames.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        world.FinalizationPayloads.Should().ContainSingle();
        world.Jobs.Should().HaveCount(jobsBefore);
        world.DocumentCreates.Should().Be(1, "an add never falls back to an email save");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The first save, with only <paramref name="saved"/> ticked, under its OWN body key (so no job is under the add's key),
    /// then the children its worker would create.
    /// </summary>
    private static async Task<Guid> FirstSaveWithChildren(HttpClient client, OfficeVersionSaveWorld world, params string[] saved)
    {
        var first = EmailSave(saved.ToList()) with { IdempotencyKey = "first-save-by-another-path" };
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save") { Content = JsonContent.Create(first) };
        message.Headers.Add("X-Idempotency-Key", "first-save");
        (await client.SendAsync(message)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var emlId = world.Documents.Values.Should()
            .ContainSingle(d => d.FileName != null && d.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Subject.Id;
        foreach (var name in saved)
            world.SeedAttachmentChild(emlId, name);
        return emlId;
    }

    private static SaveRequest EmailSave(List<string>? selection, SaveEntityReference? target = null) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = target ?? Matter,
        Email = new EmailMetadata
        {
            Subject = "RE: Settlement",
            SenderEmail = "counsel@test.com",
            SentDate = new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero),
            InternetMessageId = RfcId,
            Body = "<p>Attached.</p>",
            IsBodyHtml = true,
            IsNameSystemDerived = true,
            Attachments = new List<AttachmentReference> { Pdf(A), Pdf(B), Pdf(C) },
            SelectedAttachmentFileNames = selection,
        },
    };

    /// <summary>The pane's add: the same email request, naming the saved <c>.eml</c> and only the ticked names.</summary>
    private static HttpRequestMessage Add(
        Guid savedDocumentId, string[] names, string headerKey, SaveEntityReference? target = null, bool omitTarget = false)
    {
        var json = JsonSerializer.SerializeToNode(EmailSave(names.ToList(), target))!.AsObject();
        json["Email"]!.AsObject()["savedDocumentId"] = savedDocumentId.ToString("D");
        if (omitTarget)
            json.Remove("TargetEntity");
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

    private static async Task<List<string>> Names(HttpResponseMessage response, string property)
    {
        var body = await Body(response);
        body.TryGetProperty(property, out var names).Should().BeTrue($"the answer names {property}");
        return names.EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool Bool(JsonElement payload, string name) =>
        Property(payload, name) is { ValueKind: JsonValueKind.True };

    private static List<string> Selection(JsonElement payload) =>
        Property(Property(payload, "EmailMetadata"), "SelectedAttachmentFileNames").EnumerateArray()
            .Select(e => e.GetString()!).ToList();
}
