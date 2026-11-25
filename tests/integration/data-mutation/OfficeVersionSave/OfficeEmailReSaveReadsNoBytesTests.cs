using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 138 (#2209), fix round 1b, end to end through <c>POST /api/office/save</c>: an email re-save
/// whose ticked attachments ALL already have a child under the saved <c>.eml</c> reads no SPE bytes (as before this task); one
/// that names a file with no child yet reads the <c>.eml</c> to find out what to add.
/// </summary>
[Trait("status", "task-138-word-add-in-r1")]
public class OfficeEmailReSaveReadsNoBytesTests
{
    private const string RfcId = "<CAF0-138-nobytes@mail.example.com>";
    private const string A = "Settlement terms.pdf";
    private const string B = "Exhibit A.pdf";

    private static readonly SaveEntityReference Matter = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("13813813-0138-4138-8138-0000000000f1"),
    };

    [Fact]
    public async Task ReSave_WhoseTickedAttachmentsAllHaveAChild_DownloadsNoSpeBytes()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        await FirstSaveWithChildren(client, world, A);
        var downloadsBefore = world.DownloadCalls;

        var response = await client.SendAsync(Post(EmailSave(new List<string> { A }), "resave-a"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("duplicate").GetBoolean().Should().BeTrue();
        world.DownloadCalls.Should().Be(downloadsBefore, "every ticked name already has a child: nothing can be missing");
    }

    [Fact]
    public async Task ReSave_NamingAFileWithNoChildYet_ReadsTheEml_AndAddsIt()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        await FirstSaveWithChildren(client, world, A);
        var downloadsBefore = world.DownloadCalls;

        var response = await client.SendAsync(Post(EmailSave(new List<string> { A, B }), "resave-ab"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.DownloadCalls.Should().Be(downloadsBefore + 1, "the stored .eml is read once, to find what is missing");
    }

    private static async Task FirstSaveWithChildren(HttpClient client, OfficeVersionSaveWorld world, params string[] saved)
    {
        (await client.SendAsync(Post(EmailSave(saved.ToList()), "first-save"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var emlId = world.Documents.Values.Should()
            .ContainSingle(d => d.FileName != null && d.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Subject.Id;
        foreach (var name in saved)
            world.SeedAttachmentChild(emlId, name);
    }

    private static SaveRequest EmailSave(List<string>? selection) => new()
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
            Attachments = new List<AttachmentReference> { Pdf(A), Pdf(B) },
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
}
