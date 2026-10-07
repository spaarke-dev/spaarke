using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.Shared.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression;

/// <summary>
/// GitHub #1084 (ISS-017, task 060): the Office save's job record (<c>sprk_processingjob</c>) was not durable.
/// </summary>
/// <remarks>
/// <para><b>Three production defects</b>, each confirmed in App Insights (<c>spe-insights-dev-67e2xz</c>, 60 days):</para>
/// <list type="number">
/// <item>The job's outcome, meaning which document the save produced, lived only in a process-wide static
/// dictionary. A restart, or a poll that reached another instance, could not answer it.</item>
/// <item>The two job-row reads returned ANONYMOUS types, which the BFF read through <c>dynamic</c>. The runtime binder
/// cannot see another assembly's internals, so every read threw and was swallowed
/// (<c>RuntimeBinderException</c>, 2026-08-25). The status poll then answered 404, and the 039 idempotency check
/// answered "not a duplicate".</item>
/// <item>The save put the content's base64 into <c>sprk_payload</c> (at most 50,000 characters), so most saves got no
/// row at all: 40 saves, 13 rows, 27 refusals.</item>
/// </list>
/// <para><b>Why a cloned row stands in for a restart.</b> The old store was <c>static</c>, so two test hosts in one
/// process share it, and polling the original id from a second host would pass before the fix. A row copied to an id
/// this process never created is the exact restart condition: the row is byte-identical to what the real save wrote,
/// and no memory holds it.</para>
/// </remarks>
[Trait("status", "new")]
public class Issue1084_OfficeJobRecordDurabilityTests
{
    private static readonly byte[] Docx = MinimalDocx.Create("issue 1084 brief");

    private static async Task<SaveResponse> SaveAsync(HttpClient client, SaveRequest request, HttpStatusCode expected)
    {
        var response = await client.PostAsJsonAsync("/api/office/save", request);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SaveResponse>())!;
    }

    [Fact]
    public async Task JobStatus_ReadByAProcessThatNeverHeldIt_ReturnsTheCompletedSaveAndItsDocument()
    {
        var world = new OfficeVersionSaveWorld();
        Guid restartedJobId;
        Guid createdDocumentId;
        using (var original = new OfficeVersionSaveTestWebAppFactory(world))
        {
            var saved = await SaveAsync(
                original.CreateClient(), OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Docx), HttpStatusCode.Accepted);
            createdDocumentId = world.Documents.Values.Should().ContainSingle().Subject.Id;
            restartedJobId = world.CloneJobRowAsAnotherProcessWouldSeeIt(saved.JobId!.Value);
        }

        using var restarted = new OfficeVersionSaveTestWebAppFactory(world);
        var poll = await restarted.CreateClient().GetAsync($"/api/office/jobs/{restartedJobId}");

        poll.StatusCode.Should().Be(HttpStatusCode.OK,
            "the job row is the durable record; a process that never held the job in memory must still answer it");
        var job = (await poll.Content.ReadFromJsonAsync<JobStatusResponse>())!;
        job.JobId.Should().Be(restartedJobId);
        job.Status.Should().Be(JobStatus.Completed);
        job.CurrentPhase.Should().Be("Complete");
        job.Progress.Should().Be(100);
        job.Result.Should().NotBeNull("the pane completes on result.artifact.id, so the outcome must be persisted");
        job.Result!.Artifact!.Id.Should().Be(createdDocumentId);
    }

    [Fact]
    public async Task JobStream_ReadByAProcessThatNeverHeldIt_DeliversConnectedProgressAndCompleteWithTheDocument()
    {
        var world = new OfficeVersionSaveWorld();
        Guid restartedJobId;
        Guid createdDocumentId;
        using (var original = new OfficeVersionSaveTestWebAppFactory(world))
        {
            var saved = await SaveAsync(
                original.CreateClient(), OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Docx), HttpStatusCode.Accepted);
            createdDocumentId = world.Documents.Values.Should().ContainSingle().Subject.Id;
            restartedJobId = world.CloneJobRowAsAnotherProcessWouldSeeIt(saved.JobId!.Value);
        }

        using var restarted = new OfficeVersionSaveTestWebAppFactory(world);
        var body = await restarted.CreateClient().GetStringAsync($"/api/office/jobs/{restartedJobId}/stream");
        var events = ParseSse(body);

        // The sequence a completed job has always produced: the stream reads the job once, finds it terminal, and ends.
        events.Select(e => e.Name).Should().Equal("connected", "progress", "job-complete");
        events[1].Data.GetProperty("progress").GetInt32().Should().Be(100);
        events[2].Data.GetProperty("documentId").GetGuid().Should().Be(createdDocumentId);
    }

    [Fact]
    public async Task RepeatedSave_WhoseFirstAttemptWasWrittenByAnotherProcess_IsAnsweredAsADuplicateOfThatJob()
    {
        var world = new OfficeVersionSaveWorld();
        var request = OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Docx);
        Guid firstJobId;
        using (var original = new OfficeVersionSaveTestWebAppFactory(world))
        {
            firstJobId = (await SaveAsync(original.CreateClient(), request, HttpStatusCode.Accepted)).JobId!.Value;
        }

        using var restarted = new OfficeVersionSaveTestWebAppFactory(world);
        var repeat = await SaveAsync(restarted.CreateClient(), request, HttpStatusCode.OK);

        repeat.Duplicate.Should().BeTrue("the 039 check finds the completed row with this key and must be able to read it");
        repeat.JobId.Should().Be(firstJobId);
        world.DocumentCreates.Should().Be(1, "a detected duplicate writes nothing");
    }

    [Fact]
    public async Task RetryOfASaveWhoseRequestDied_RunsAgain_AndTheAbandonedJobReadsAsAFailure()
    {
        // A save killed mid-flight (a restart) leaves its row non-terminal forever: no catch ran to mark it Failed. Once
        // the 039 check can read rows, a retry would otherwise be answered "duplicate" with a job that never finishes.
        var world = new OfficeVersionSaveWorld();
        const string key = "issue-1084-abandoned-save";
        var startedAt = DateTimeOffset.UtcNow - OfficeJobStatusService.MaxSaveDuration - TimeSpan.FromMinutes(1);
        var abandonedJobId = world.RecordJob(new
        {
            Name = "Document Save - request died",
            IdempotencyKey = key,
            Status = 1,
            Result = OfficeJobStatusService.SerializeView(new JobStatusResponse
            {
                JobId = Guid.Empty,
                Status = JobStatus.Running,
                JobType = JobType.DocumentSave,
                Progress = 30,
                CurrentPhase = "FileUploaded",
                CreatedAt = startedAt,
                CreatedBy = "test-user-oid",
            }),
        });
        world.JobCreatedOn[abandonedJobId] = startedAt.UtcDateTime;

        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        var poll = await client.GetAsync($"/api/office/jobs/{abandonedJobId}");
        poll.StatusCode.Should().Be(HttpStatusCode.OK);
        var job = (await poll.Content.ReadFromJsonAsync<JobStatusResponse>())!;
        job.Status.Should().Be(JobStatus.Failed, "the pane must reach a definite outcome, not poll forever");
        job.CurrentPhase.Should().Be(OfficeJobStatusService.AbandonedPhase);
        job.Error!.Retryable.Should().BeTrue();

        var retry = await SaveAsync(
            client, OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Docx) with { IdempotencyKey = key },
            HttpStatusCode.Accepted);

        retry.Duplicate.Should().BeFalse("an abandoned attempt is not a performed operation");
        retry.JobId.Should().NotBe(abandonedJobId);
        world.DocumentCreates.Should().Be(1);
    }

    [Fact]
    public async Task FinalizationRewritingTheRow_ChangesNeitherTheSavesAnswer_NorMakesARepeatRunAgain()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var request = OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Docx);
        var saved = await SaveAsync(client, request, HttpStatusCode.Accepted);
        var documentId = world.Documents.Values.Should().ContainSingle().Subject.Id;

        // UploadFinalizationWorker's last failed attempt, written to the SAME row exactly as the worker writes it.
        world.UpdateJob(saved.JobId!.Value, new
        {
            Status = 3,
            Progress = 0,
            CurrentStage = "Failed",
            ErrorCode = "OFFICE_012",
            ErrorMessage = "Upload finalization failed",
        });

        var job = (await (await client.GetAsync($"/api/office/jobs/{saved.JobId}")).Content
            .ReadFromJsonAsync<JobStatusResponse>())!;
        job.Status.Should().Be(JobStatus.Completed, "the save succeeded; finalization is a later stage, not the save");
        job.Result!.Artifact!.Id.Should().Be(documentId);

        var repeat = await SaveAsync(client, request, HttpStatusCode.OK);
        repeat.Duplicate.Should().BeTrue("the document exists; repeating the save must not write it again");
        repeat.JobId.Should().Be(saved.JobId);
        world.DocumentCreates.Should().Be(1);
    }

    [Fact]
    public async Task SaveWhoseJobRowCannotBeCreated_IsRefusedAsARetryable502_BeforeAnythingIsWritten()
    {
        // It used to continue with a made-up job id held only in memory: no durable status and no idempotency, the very
        // state #1084 found behind 27 of 40 saves. A job with no row is not a job (ADR-017: no orphaned jobs).
        var world = new OfficeVersionSaveWorld { FailNextJobCreate = true };
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Docx));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway, "Dataverse failed; the request was not at fault");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be("OFFICE_014");
        problem.GetProperty("retryable").GetBoolean().Should().BeTrue();
        world.UploadSmallCalls.Should().Be(0, "nothing reaches storage without a job row");
        world.DocumentCreates.Should().Be(0);
        world.JobRows.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SaveContentType.Document)]
    [InlineData(SaveContentType.Attachment)]
    [InlineData(SaveContentType.Email)]
    public async Task SaveOfLargeContent_GetsADurableJobRow_WhosePayloadCarriesNoContent(SaveContentType contentType)
    {
        // 40,000 random bytes, which is 53,336 characters of base64 and more than sprk_payload holds. Random, so
        // nothing compresses it below the limit; deterministic, so the test is repeatable.
        var bytes = new byte[40_000];
        new Random(1084).NextBytes(bytes);
        var base64 = Convert.ToBase64String(bytes);
        var target = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() };
        var request = contentType switch
        {
            SaveContentType.Document => new SaveRequest
            {
                ContentType = contentType,
                TargetEntity = target,
                Document = new DocumentMetadata { FileName = "Large.bin", ContentBase64 = base64 },
            },
            SaveContentType.Attachment => new SaveRequest
            {
                ContentType = contentType,
                TargetEntity = target,
                Attachment = new AttachmentMetadata { AttachmentId = "att-1084", FileName = "Large.pdf", ContentBase64 = base64 },
            },
            _ => new SaveRequest
            {
                ContentType = contentType,
                TargetEntity = target,
                Email = new EmailMetadata { Subject = "Large", SenderEmail = "sender@test.com", Body = base64 },
            },
        };

        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var saved = await SaveAsync(factory.CreateClient(), request, HttpStatusCode.Accepted);

        world.JobRows.Should().ContainKey(saved.JobId!.Value,
            "every save needs its job row; without one there is no durable status and no idempotency");
        var payload = world.JobRows[saved.JobId!.Value]["Payload"] as string;
        payload.Should().NotBeNull();
        payload!.Length.Should().BeLessThanOrEqualTo(OfficeVersionSaveWorld.PayloadMaxLength);
        payload.Should().NotContain(base64[..64], "content bytes and bodies do not belong in the job row (ADR-004, ADR-015)");
    }

    private static List<(string Name, JsonElement Data)> ParseSse(string body)
    {
        var events = new List<(string, JsonElement)>();
        foreach (var block in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string? name = null;
            string? data = null;
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("event: ", StringComparison.Ordinal)) name = line["event: ".Length..].Trim();
                else if (line.StartsWith("data: ", StringComparison.Ordinal)) data = line["data: ".Length..];
            }

            if (name is not null && data is not null)
                events.Add((name, JsonDocument.Parse(data).RootElement.Clone()));
        }

        return events;
    }
}
