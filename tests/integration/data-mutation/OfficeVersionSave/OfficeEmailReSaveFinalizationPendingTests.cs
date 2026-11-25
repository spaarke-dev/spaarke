using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 138 (#2209), fix round 1c (item H): an email re-save adds nothing WHILE the job it matched is
/// still creating its documents, and plans again once that job has finished or been given up on.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The save marks its job Completed as soon as the finalization is QUEUED, so a re-save made while the worker is still
/// running sees no children yet and would count every attachment missing ("Added 2" when only one is new, and a redundant
/// SPE version of the first). Unfinished = the worker's processed mark is absent AND the job is younger than the existing
/// abandoned age (<c>OfficeJobStatusService.MaxSaveDuration</c>). The mark is read under the key the matched job's message
/// was queued with: the save's key for a first save, the add job's own key for an attachments-only job.</item>
/// <item>A worker that fails leaves its names without children, and never sets its mark: once the job is past the abandoned
/// age, the NEXT re-save plans the same names again (the failure path the pane's wording relies on).</item>
/// </list>
/// Module-boundary doubles only (ADR-038 §4): the real route and <c>OfficeService</c> over <see cref="OfficeVersionSaveWorld"/>,
/// the workers not run: <see cref="OfficeVersionSaveWorld.WorkersFinishInstantly"/> false leaves their marks unset, and the
/// test sets them through <see cref="OfficeVersionSaveTestWebAppFactory.MarkFinalizationProcessedAsync"/>.
/// </remarks>
[Trait("status", "task-138-word-add-in-r1")]
public class OfficeEmailReSaveFinalizationPendingTests
{
    private const string RfcId = "<CAF0-138-pending@mail.example.com>";
    private const string A = "Settlement terms.pdf";
    private const string B = "Exhibit A.pdf";
    private const string C = "Exhibit B.pdf";

    private static readonly SaveEntityReference Matter = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("13813813-0138-4138-8138-0000000000c3"),
    };

    [Fact]
    public async Task ReSave_WhileTheFirstSavesWorkerHasNotFinished_AddsNothing_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld { WorkersFinishInstantly = false };
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        await FirstSave(client, world, A);

        // The worker is still running: no child exists yet, so every ticked attachment would read as missing.
        var response = await client.SendAsync(Post(EmailSave(new List<string> { A, B }), "while-running"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Body(response)).GetProperty("duplicate").GetBoolean().Should().BeTrue();
        world.FinalizationPayloads.Should().ContainSingle("no add job is queued over documents still being created");
        world.Jobs.Should().ContainSingle();
    }

    [Fact]
    public async Task ReSave_OnceTheFirstSavesWorkerHasFinished_AddsTheMissingAttachment()
    {
        var world = new OfficeVersionSaveWorld { WorkersFinishInstantly = false };
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSave(client, world, A);
        await factory.MarkFinalizationProcessedAsync(world.FinalizationMessageKeys.Single());
        world.SeedAttachmentChild(emlId, A);

        var response = await client.SendAsync(Post(EmailSave(new List<string> { A, B }), "after-finished"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Body(response)).GetProperty("attachmentsAdded").EnumerateArray().Select(e => e.GetString()).Should().Equal(B);
        world.FinalizationPayloads.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReSave_WhenTheFirstSavesWorkerNeverFinished_ButTheJobIsPastTheAbandonedAge_PlansTheAddAnyway()
    {
        var world = new OfficeVersionSaveWorld { WorkersFinishInstantly = false };
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSave(client, world, A);
        world.SeedAttachmentChild(emlId, A);
        world.AgeJob(world.JobRows.Keys.Single(), TimeSpan.FromMinutes(6));

        var response = await client.SendAsync(Post(EmailSave(new List<string> { A, B }), "past-age"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "a job older than the abandoned age is given up on, mark or not");
        (await Body(response)).GetProperty("attachmentsAdded").EnumerateArray().Select(e => e.GetString()).Should().Equal(B);
    }

    [Fact]
    public async Task ReSave_WhileAnAddJobsWorkerHasNotFinished_AddsNothing_EvenThoughTheFirstSavesMarkIsSet()
    {
        // The matched job is now the ADD job: its mark is under its own message key, not the first save's. Reading the first
        // save's key would see "processed" and add B and C over documents the add worker is still creating.
        var world = new OfficeVersionSaveWorld { WorkersFinishInstantly = false };
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSave(client, world, A);
        await factory.MarkFinalizationProcessedAsync(world.FinalizationMessageKeys.Single());
        world.SeedAttachmentChild(emlId, A);
        (await client.SendAsync(Post(EmailSave(new List<string> { A, B }), "add-b"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.FinalizationPayloads.Should().HaveCount(2);

        var whileRunning = await client.SendAsync(Post(EmailSave(new List<string> { A, B, C }), "add-b-and-c"));

        whileRunning.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Body(whileRunning)).GetProperty("duplicate").GetBoolean().Should().BeTrue();
        world.FinalizationPayloads.Should().HaveCount(2, "the add job's worker has not finished");

        // The add worker finishes (its child exists, its mark is set): the next re-save plans what is still missing.
        await factory.MarkFinalizationProcessedAsync(world.FinalizationMessageKeys.Last());
        world.SeedAttachmentChild(emlId, B);
        var afterFinished = await client.SendAsync(Post(EmailSave(new List<string> { A, B, C }), "add-c"));

        afterFinished.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Body(afterFinished)).GetProperty("attachmentsAdded").EnumerateArray().Select(e => e.GetString()).Should().Equal(C);
    }

    [Fact]
    public async Task ReSave_AfterAnAddThatFailed_PlansTheSameNamesAgain()
    {
        // H1: the pane never says "added" at queue time; a worker that fails leaves the names without a child and never sets its
        // mark. Past the abandoned age, the next re-save plans the same names again.
        var world = new OfficeVersionSaveWorld { WorkersFinishInstantly = false };
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSave(client, world, A);
        await factory.MarkFinalizationProcessedAsync(world.FinalizationMessageKeys.Single());
        world.SeedAttachmentChild(emlId, A);

        var first = await client.SendAsync(Post(EmailSave(new List<string> { A, B }), "add-b-first"));
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Body(first)).GetProperty("attachmentsAdded").EnumerateArray().Select(e => e.GetString()).Should().Equal(B);

        // The add worker failed: no child for B, no mark. Then time passes.
        world.AgeJob(world.JobRows.Keys.Last(), TimeSpan.FromMinutes(6));
        var again = await client.SendAsync(Post(EmailSave(new List<string> { A, B }), "add-b-again"));

        again.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Body(again)).GetProperty("attachmentsAdded").EnumerateArray().Select(e => e.GetString()).Should().Equal(B);
        world.FinalizationPayloads.Should().HaveCount(3, "the failed add is queued again");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The first save, with only <paramref name="saved"/> ticked. Returns the <c>.eml</c> document's id.</summary>
    private static async Task<Guid> FirstSave(HttpClient client, OfficeVersionSaveWorld world, params string[] saved)
    {
        (await client.SendAsync(Post(EmailSave(saved.ToList()), "first-save"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        return world.Documents.Values.Should()
            .ContainSingle(d => d.FileName != null && d.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Subject.Id;
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
            Attachments = new List<AttachmentReference> { Pdf(A), Pdf(B), Pdf(C) },
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
}
