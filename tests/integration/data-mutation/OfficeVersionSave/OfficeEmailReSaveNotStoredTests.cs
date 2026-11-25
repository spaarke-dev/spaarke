using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 138 (#2209), fix round 1c (item G): an email re-save that ticks an attachment the STORED
/// <c>.eml</c> does not hold (the first save left it out: unreadable or too big then) says so, instead of answering as if
/// nothing was asked.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The answer lists those names in <c>attachmentsNotStored</c>, on the add answer and on the "already saved" one, and
/// omits the property when there are none (as <c>attachmentsAdded</c> is omitted).</item>
/// <item>Nothing is created for them from the request's own bytes: the queued add names only what the stored <c>.eml</c> holds.</item>
/// </list>
/// Module-boundary doubles only (ADR-038 §4), the same world as <c>OfficeEmailReSaveAddsAttachmentsTests</c>.
/// </remarks>
[Trait("status", "task-138-word-add-in-r1")]
public class OfficeEmailReSaveNotStoredTests
{
    private const string RfcId = "<CAF0-138-notstored@mail.example.com>";
    private const string A = "Settlement terms.pdf";
    private const string B = "Exhibit A.pdf";
    private const string C = "Exhibit B.pdf";

    private static readonly SaveEntityReference Matter = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("13813813-0138-4138-8138-0000000000d4"),
    };

    [Fact]
    public async Task ReSave_TickingAnAttachmentTheStoredEmlLacks_NamesIt_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        // The first save's .eml holds only A (B could not be read then); A has its child.
        await FirstSave(client, world, inEml: new[] { A }, ticked: new[] { A });

        var response = await client.SendAsync(Post(EmailSave(new[] { A, B }, new List<string> { A, B }), "ab"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Body(response);
        body.GetProperty("duplicate").GetBoolean().Should().BeTrue();
        Names(body, "attachmentsNotStored").Should().Equal(B);
        body.TryGetProperty("attachmentsAdded", out _).Should().BeFalse();
        world.FinalizationPayloads.Should().ContainSingle("nothing can be created for B: no add job is queued");
        world.Jobs.Should().ContainSingle();
    }

    [Fact]
    public async Task ReSave_WithOneMissingAndOneNotStored_AddsTheMissingOne_AndNamesTheOtherOnTheSameAnswer()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        // The .eml holds A and B; only A has its child. C was never in the .eml.
        await FirstSave(client, world, inEml: new[] { A, B }, ticked: new[] { A });

        var response = await client.SendAsync(Post(EmailSave(new[] { A, B, C }, new List<string> { A, B, C }), "abc"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await Body(response);
        Names(body, "attachmentsAdded").Should().Equal(B);
        Names(body, "attachmentsNotStored").Should().Equal(C);
        var add = world.FinalizationPayloads.Should().HaveCount(2).And.Subject.Last();
        Property(Property(add, "EmailMetadata"), "SelectedAttachmentFileNames").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(new[] { B }, "C is not created from the request's bytes: the add names only what the .eml holds");
    }

    [Fact]
    public async Task ReSave_WhereEverythingTickedIsStored_OmitsTheProperty()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        await FirstSave(client, world, inEml: new[] { A, B }, ticked: new[] { A });

        var added = await client.SendAsync(Post(EmailSave(new[] { A, B }, new List<string> { A, B }), "stored-add"));
        (await Body(added)).TryGetProperty("attachmentsNotStored", out _).Should().BeFalse("B is in the .eml: it is added, not unstored");

        // Same selection, named in another case: A is stored and has its child, so this is the plain duplicate answer.
        var duplicate = await client.SendAsync(Post(EmailSave(new[] { A, B }, new List<string> { A.ToUpperInvariant() }), "stored-dup"));
        duplicate.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Body(duplicate)).TryGetProperty("attachmentsNotStored", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ReSave_OfEveryAttachment_NeverReportsNotStored()
    {
        // No selection (null) names nothing the .eml could lack: every attachment IS what the .eml holds.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        await FirstSave(client, world, inEml: new[] { A }, ticked: new[] { A });

        var response = await client.SendAsync(Post(EmailSave(new[] { A, B }, selection: null), "all"));

        (await Body(response)).TryGetProperty("attachmentsNotStored", out _).Should().BeFalse();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The first save: its request carries <paramref name="inEml"/> as attachments (those are what the stored <c>.eml</c> holds),
    /// ticks <paramref name="ticked"/>, and then the children its worker would create (one per ticked name).
    /// </summary>
    private static async Task FirstSave(
        HttpClient client, OfficeVersionSaveWorld world, string[] inEml, string[] ticked)
    {
        (await client.SendAsync(Post(EmailSave(inEml, ticked.ToList()), "first-save"))).StatusCode
            .Should().Be(HttpStatusCode.Accepted);
        var emlId = world.Documents.Values.Should()
            .ContainSingle(d => d.FileName != null && d.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Subject.Id;
        foreach (var name in ticked)
            world.SeedAttachmentChild(emlId, name);
    }

    private static SaveRequest EmailSave(string[] attachments, List<string>? selection) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = Matter,
        Email = new EmailMetadata
        {
            Subject = "RE: Settlement",
            SenderEmail = "counsel@test.com",
            SentDate = new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero),
            InternetMessageId = RfcId,
            Body = "<p>Attached.</p>",
            IsBodyHtml = true,
            IsNameSystemDerived = true,
            Attachments = attachments.Select(Pdf).ToList(),
            SelectedAttachmentFileNames = selection,
        },
    };

    private static AttachmentReference Pdf(string name) => new()
    {
        AttachmentId = $"att-{name}",
        FileName = name,
        ContentType = "application/pdf",
        Size = 20_000,
        ContentBase64 = Convert.ToBase64String(new byte[20_000]),
    };

    private static HttpRequestMessage Post(SaveRequest body, string headerKey)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save") { Content = JsonContent.Create(body) };
        message.Headers.Add("X-Idempotency-Key", headerKey);
        return message;
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static List<string?> Names(JsonElement body, string property)
    {
        body.TryGetProperty(property, out var names).Should().BeTrue($"the answer carries {property}");
        return names.EnumerateArray().Select(e => e.GetString()).ToList();
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
}
