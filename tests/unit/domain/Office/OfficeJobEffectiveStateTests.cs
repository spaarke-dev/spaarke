using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Office;

/// <summary>
/// Task 060 (GitHub #1084): the ONE rule that turns a <c>sprk_processingjob</c> row into the Office job's effective state.
/// The status read and the task 039 idempotency check both use it. Also covers what the row's <c>sprk_payload</c> holds.
/// Pure domain logic: a row in, a view out, with no mocks, no DI and no I/O (ADR-038 KEEP path
/// <c>tests/unit/domain/**</c>).
/// </summary>
/// <remarks>
/// <para><b>Why a rule at all.</b> The save is synchronous: it ends Completed with its document, or Failed. The
/// finalization workers then rewrite the row's pipeline columns (<c>sprk_status</c>, <c>sprk_currentstage</c>,
/// <c>sprk_progress</c>). The save's own view therefore lives in <c>sprk_result</c>, which the workers never write, and it
/// is authoritative. A save whose request died leaves a non-terminal row forever, and past the save's maximum lifetime
/// that reads as Failed. Otherwise the pane would wait forever, and a retry would be answered with a job that never
/// finishes.</para>
/// </remarks>
public class OfficeJobEffectiveStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid RowId = Guid.Parse("10840000-0000-0000-0000-000000000060");
    private static readonly Guid DocumentId = Guid.Parse("10840000-0000-0000-0000-00000000d0c0");

    private static JobStatusResponse View(JobStatus status, string phase, DateTimeOffset createdAt, string? createdBy = "creator-oid") => new()
    {
        JobId = Guid.Empty,
        Status = status,
        JobType = JobType.DocumentSave,
        Progress = status == JobStatus.Completed ? 100 : 30,
        CurrentPhase = phase,
        CompletedPhases = new List<CompletedPhase>(),
        CreatedAt = createdAt,
        CreatedBy = createdBy,
        CompletedAt = status == JobStatus.Completed ? createdAt.AddSeconds(2) : null,
        Result = status == JobStatus.Completed
            ? new JobResult { Artifact = new CreatedArtifact { Type = ArtifactType.Document, Id = DocumentId } }
            : null,
    };

    private static ProcessingJobRecord Row(int status, JobStatusResponse? view, DateTime? createdOn = null, string? initiatedByOid = null) => new()
    {
        Id = RowId,
        Status = status,
        Progress = 70,
        CurrentStage = "FileAlreadyUploaded",
        Result = view is null ? null : OfficeJobStatusService.SerializeView(view),
        CreatedOn = createdOn ?? Now.UtcDateTime.AddSeconds(-30),
        InitiatedByOid = initiatedByOid,
    };

    [Fact]
    public void ToEffectiveView_WhenFinalizationRewroteThePipelineColumns_ReturnsTheSavesCompletedViewAndItsDocument()
    {
        // The save completed; UploadFinalizationWorker then wrote In Progress, and on its last attempt Failed.
        var row = Row(status: 3, View(JobStatus.Completed, "Complete", Now.AddSeconds(-30)));

        var view = OfficeJobStatusService.ToEffectiveView(row, Now);

        view.JobId.Should().Be(RowId, "the job id is the row's id, never whatever the stored view carried");
        view.Status.Should().Be(JobStatus.Completed, "a finalization failure is not a failed save");
        view.CurrentPhase.Should().Be("Complete");
        view.Progress.Should().Be(100);
        view.Result!.Artifact!.Id.Should().Be(DocumentId);
    }

    [Fact]
    public void ToEffectiveView_WhenTheViewRecordsNoCreator_FallsBackToTheInitiatedByJoin()
    {
        var row = Row(status: 2, View(JobStatus.Completed, "Complete", Now.AddSeconds(-30), createdBy: null),
            initiatedByOid: "joined-oid");

        OfficeJobStatusService.ToEffectiveView(row, Now).CreatedBy.Should().Be("joined-oid");
    }

    [Fact]
    public void ToEffectiveView_WhenTheViewRecordsACreator_PrefersItToTheInitiatedByJoin()
    {
        var row = Row(status: 2, View(JobStatus.Completed, "Complete", Now.AddSeconds(-30)), initiatedByOid: "joined-oid");

        OfficeJobStatusService.ToEffectiveView(row, Now).CreatedBy.Should().Be("creator-oid");
    }

    [Fact]
    public void ToEffectiveView_ForARowWrittenBeforeTheView_ReadsThePipelineColumnsWithNoArtifact()
    {
        var row = Row(status: 2, view: null, initiatedByOid: "joined-oid");

        var view = OfficeJobStatusService.ToEffectiveView(row, Now);

        view.Status.Should().Be(JobStatus.Completed);
        view.CurrentPhase.Should().Be("Complete");
        view.Progress.Should().Be(100);
        view.Result.Should().BeNull("no outcome was ever stored for a row written before task 060");
        view.CreatedBy.Should().Be("joined-oid");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToEffectiveView_ForANonTerminalJobOlderThanASaveCanRun_ReadsAsAbandonedAndRetryable(bool withView)
    {
        var createdAt = Now - OfficeJobStatusService.MaxSaveDuration - TimeSpan.FromSeconds(1);
        var row = Row(status: 1, withView ? View(JobStatus.Running, "FileUploaded", createdAt) : null,
            createdOn: createdAt.UtcDateTime);

        var view = OfficeJobStatusService.ToEffectiveView(row, Now);

        view.Status.Should().Be(JobStatus.Failed, "the request that owned this save no longer exists");
        view.CurrentPhase.Should().Be(OfficeJobStatusService.AbandonedPhase);
        view.Error!.Retryable.Should().BeTrue();
    }

    [Fact]
    public void ToEffectiveView_ForANonTerminalJobWithinASavesLifetime_StaysRunning()
    {
        var createdAt = Now - OfficeJobStatusService.MaxSaveDuration + TimeSpan.FromSeconds(1);
        var row = Row(status: 1, View(JobStatus.Running, "FileUploaded", createdAt));

        var view = OfficeJobStatusService.ToEffectiveView(row, Now);

        view.Status.Should().Be(JobStatus.Running, "another request may still be mid-save");
        view.CurrentPhase.Should().Be("FileUploaded");
    }

    [Fact]
    public void ToEffectiveView_ForATerminalJob_IsNeverAbandoned_HoweverOld()
    {
        var createdAt = Now.AddDays(-30);
        var row = Row(status: 2, View(JobStatus.Completed, "Complete", createdAt));

        OfficeJobStatusService.ToEffectiveView(row, Now).Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public void ToEffectiveView_WhenTheResultIsNotTheSavesSchema_ReadsTheRowAsOneWithoutAView()
    {
        var row = Row(status: 2, view: null) with { Result = "{ not the save's view" };

        var view = OfficeJobStatusService.ToEffectiveView(row, Now);

        view.Status.Should().Be(JobStatus.Completed);
        view.Result.Should().BeNull();
    }

    [Fact]
    public void BuildPayload_KeepsTheRequestsMetadata_AndDropsEveryContentField()
    {
        const string content = "Q09OVEVOVA==CONTENT-THAT-MUST-NOT-REACH-THE-JOB-ROW";
        var request = new SaveRequest
        {
            ContentType = SaveContentType.Email,
            TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = DocumentId },
            Email = new EmailMetadata
            {
                Subject = "Filing",
                SenderEmail = "sender@test.com",
                Body = content,
                Attachments = new List<AttachmentReference>
                {
                    new() { AttachmentId = "att-1", FileName = "a.pdf", ContentBase64 = content },
                },
            },
            Attachment = new AttachmentMetadata { AttachmentId = "att-2", FileName = "b.pdf", ContentBase64 = content },
            Document = new DocumentMetadata { FileName = "c.docx", Title = "Brief", ContentBase64 = content },
        };

        var payload = OfficeJobStatusService.BuildPayload(request, "b!container");

        payload.Should().NotContain(content);
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        root.GetProperty("ContainerId").GetString().Should().Be("b!container");
        root.GetProperty("Email").GetProperty("Subject").GetString().Should().Be("Filing");
        root.GetProperty("Email").GetProperty("Attachments")[0].GetProperty("FileName").GetString().Should().Be("a.pdf");
        root.GetProperty("Attachment").GetProperty("FileName").GetString().Should().Be("b.pdf");
        root.GetProperty("Document").GetProperty("Title").GetString().Should().Be("Brief");
    }

    [Fact]
    public void BuildPayload_WhenTheMetadataAloneExceedsTheColumn_RecordsOnlyTheIdentifyingFields()
    {
        var recipients = Enumerable.Range(0, 2_000)
            .Select(i => new Recipient { Email = $"recipient-{i:D4}@a-long-enough-domain.example.com", Name = $"Recipient {i}" })
            .ToList();
        var request = new SaveRequest
        {
            ContentType = SaveContentType.Email,
            TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = DocumentId },
            Email = new EmailMetadata { Subject = "All hands", SenderEmail = "sender@test.com", Recipients = recipients },
        };

        var payload = OfficeJobStatusService.BuildPayload(request, "b!container");

        payload.Length.Should().BeLessThanOrEqualTo(OfficeJobStatusService.PayloadMaxLength,
            "a longer sprk_payload makes Dataverse refuse the create, and the save then has no job row");
        using var json = JsonDocument.Parse(payload);
        json.RootElement.GetProperty("ContentType").GetString().Should().Be("Email");
        json.RootElement.GetProperty("ContainerId").GetString().Should().Be("b!container");
    }
}
