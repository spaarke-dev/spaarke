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
/// spaarkeai-word-add-in-r1 task 141: an attachments add records its queued state BEFORE it queues the worker's message, never after.
/// </summary>
/// <remarks>
/// <para>The race: the add queued the finalization and only then recorded <c>Completed / AttachmentsQueued</c> on its job. A worker
/// that finished first had already recorded <c>AttachmentsAdded</c> or <c>AttachmentsIncomplete</c> on that row, and the save's later
/// write put it back to <c>AttachmentsQueued</c>, so the pane polled a job that never reached its outcome.</para>
/// <para>The queue double runs <see cref="OfficeVersionSaveWorld.OnFinalizationSend"/> inside the send: a test reads the job row there
/// (what the worker would find) or records a worker's outcome there (a worker that finishes before the save returns), or throws (a
/// queue that failed). Module-boundary doubles only (ADR-038 §4), the same world as <see cref="OfficeEmailAttachmentsAddNamedEmlTests"/>.</para>
/// </remarks>
[Trait("status", "task-141-word-add-in-r1")]
public class OfficeEmailAttachmentsAddQueueOrderTests
{
    private const string RfcId = "<CAF0-141-queue-order@mail.example.com>";
    private const string ConversationOfTheSavedEmail = "AAQkADconversation-queue-order";
    private const string A = "Settlement terms.pdf";
    private const string B = "Exhibit A.pdf";

    // The Dataverse sprk_status the world stores: 2 Completed, 3 Failed.
    private const int Completed = 2;
    private const int Failed = 3;

    private static readonly SaveEntityReference Matter = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("14114114-0141-4141-8141-0000000000a1"),
    };

    [Fact]
    public async Task Add_RecordsTheQueuedState_BeforeItQueuesTheWorkersMessage()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);

        (int? Status, string? Stage)? atQueueTime = null;
        world.OnFinalizationSend = message =>
        {
            var row = world.ReadJob(JobIdOf(message))!;
            atQueueTime = (row.Status, row.CurrentStage);
        };

        var response = await client.SendAsync(Add(emlId, new[] { B }, "add-b-order"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        atQueueTime.Should().NotBeNull("the add queued a message");
        atQueueTime!.Value.Status.Should().Be(Completed, "the job reads as queued by the time the worker can see it");
        atQueueTime.Value.Stage.Should().Be("AttachmentsQueued");
    }

    [Theory]
    [InlineData(Failed, "AttachmentsIncomplete")]
    [InlineData(Completed, "AttachmentsAdded")]
    public async Task Add_WhoseWorkerFinishesBeforeTheSaveReturns_KeepsTheWorkersOutcome(int workerStatus, string workerStage)
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);

        // The worker picks the message up and records its outcome on the job's row (UploadFinalizationWorker) before the
        // save's request has returned.
        world.OnFinalizationSend = message =>
            world.UpdateJob(JobIdOf(message), new { Status = workerStatus, CurrentStage = workerStage });

        var response = await client.SendAsync(Add(emlId, new[] { B }, "add-b-worker-first"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var job = world.ReadJob(world.JobRows.Keys.Last())!;
        job.Status.Should().Be(workerStatus, "the save's own write must not undo the worker's");
        job.CurrentStage.Should().Be(workerStage);
    }

    [Fact]
    public async Task Add_WhoseQueueThrows_DoesNotLeaveTheJobCompletedOrQueued_AndAnswersTheGenericSaveError()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        world.OnFinalizationSend = _ => throw new InvalidOperationException("Test: Service Bus refused the message.");

        var response = await client.SendAsync(Add(emlId, new[] { B }, "add-b-queue-throws"));

        // The shape any other save that throws after its job exists answers: a generic, retryable 500.
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await Body(response);
        problem.GetProperty("errorCode").GetString().Should().Be("OFFICE_INTERNAL");
        problem.GetProperty("retryable").GetBoolean().Should().BeTrue();
        var job = world.ReadJob(world.JobRows.Keys.Last())!;
        job.Status.Should().Be(Failed, "a job whose message was never queued must not read as queued or completed");
        job.CurrentStage.Should().Be("Failed");
        world.FinalizationPayloads.Should().ContainSingle("only the first save's message was queued");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Guid JobIdOf(string messageJson)
    {
        using var document = JsonDocument.Parse(messageJson);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "JobId", StringComparison.OrdinalIgnoreCase))
                return property.Value.GetGuid();
        }

        throw new InvalidOperationException("The queued message names no job.");
    }

    /// <summary>The first save, with only <paramref name="saved"/> ticked, under its own body key, then its children.</summary>
    private static async Task<Guid> FirstSaveWithChildren(HttpClient client, OfficeVersionSaveWorld world, params string[] saved)
    {
        world.CommunicationsByMessageId[RfcId] = Guid.Parse("14114114-0141-4141-8141-00000000a1c0");
        var first = EmailSave(saved.ToList()) with { IdempotencyKey = "first-save-queue-order" };
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save") { Content = JsonContent.Create(first) };
        message.Headers.Add("X-Idempotency-Key", "first-save-queue-order");
        (await client.SendAsync(message)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var emlId = world.Documents.Values.Should()
            .ContainSingle(d => d.FileName != null && d.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Subject.Id;
        foreach (var name in saved)
            world.SeedAttachmentChild(emlId, name);
        return emlId;
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
            ConversationId = ConversationOfTheSavedEmail,
            Body = "<p>Attached.</p>",
            IsBodyHtml = true,
            IsNameSystemDerived = true,
            Attachments = new List<AttachmentReference> { Pdf(A), Pdf(B) },
            SelectedAttachmentFileNames = selection,
        },
    };

    /// <summary>The pane's add: the same email request, naming the saved <c>.eml</c> and only the ticked names.</summary>
    private static HttpRequestMessage Add(Guid savedDocumentId, string[] names, string headerKey)
    {
        var json = JsonSerializer.SerializeToNode(EmailSave(names.ToList()))!.AsObject();
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
