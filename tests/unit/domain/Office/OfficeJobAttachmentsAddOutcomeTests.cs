using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Office;

/// <summary>
/// spaarkeai-word-add-in-r1 task 141 review (F2): the effective state of an attachments add's job. The save records it
/// Completed at phase <c>AttachmentsQueued</c> when it queues the worker; the worker then records its outcome in the pipeline
/// columns. The status read keeps the save's status (a later save under the key still sees a performed job) and reports the
/// worker's outcome as the phase, so the pane can tell when the add finished and re-read what is saved. Pure domain logic: a
/// row in, a view out (ADR-038 KEEP path <c>tests/unit/domain/**</c>).
/// </summary>
[Trait("status", "task-141-word-add-in-r1")]
public class OfficeJobAttachmentsAddOutcomeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid RowId = Guid.Parse("14114114-0141-4141-8141-0000000000a1");
    private static readonly Guid EmlId = Guid.Parse("14114114-0141-4141-8141-0000000000e1");

    private static JobStatusResponse QueuedAddView(string phase = "AttachmentsQueued") => new()
    {
        JobId = Guid.Empty,
        Status = JobStatus.Completed,
        JobType = JobType.EmailSave,
        Progress = 100,
        CurrentPhase = phase,
        CompletedPhases = new List<CompletedPhase>(),
        CreatedAt = Now.AddSeconds(-20),
        CreatedBy = "creator-oid",
        CompletedAt = Now.AddSeconds(-19),
        Result = new JobResult { Artifact = new CreatedArtifact { Type = ArtifactType.Document, Id = EmlId } },
    };

    private static ProcessingJobRecord Row(
        int status, string stage, JobStatusResponse view, string? errorCode = null, string? errorMessage = null) => new()
        {
            Id = RowId,
            Status = status,
            Progress = 100,
            CurrentStage = stage,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Result = OfficeJobStatusService.SerializeView(view),
            CreatedOn = Now.UtcDateTime.AddSeconds(-20),
        };

    [Fact]
    public void AddWhoseWorkerLeftNamesUncreated_ReadsAsAttachmentsIncomplete_WithTheNames_AndStaysCompleted()
    {
        var row = Row(3, "AttachmentsIncomplete", QueuedAddView(), "OFFICE_ATTACHMENTS_NOT_ADDED", "Not added: Exhibit B.pdf");

        var view = OfficeJobStatusService.ToEffectiveView(row, Now);

        view.CurrentPhase.Should().Be("AttachmentsIncomplete", "the pane must know the add finished without every name");
        view.Error!.Message.Should().Contain("Exhibit B.pdf");
        view.Error.Code.Should().Be("OFFICE_ATTACHMENTS_NOT_ADDED");
        view.Status.Should().Be(JobStatus.Completed, "a later save under the key still sees a performed job, never a new attempt");
        view.Result!.Artifact!.Id.Should().Be(EmlId);
    }

    [Fact]
    public void AddWhoseFinalizationFailed_ReadsAsAttachmentsIncomplete()
    {
        var row = Row(3, "Failed", QueuedAddView(), "OFFICE_012", "Upload finalization failed: boom");

        OfficeJobStatusService.ToEffectiveView(row, Now).CurrentPhase.Should().Be("AttachmentsIncomplete");
    }

    [Fact]
    public void AddWhoseWorkerCreatedEveryName_ReadsAsAttachmentsAdded()
    {
        var row = Row(2, "AttachmentsAdded", QueuedAddView());

        var view = OfficeJobStatusService.ToEffectiveView(row, Now);

        view.CurrentPhase.Should().Be("AttachmentsAdded");
        view.Error.Should().BeNull();
    }

    [Fact]
    public void AddWhoseWorkerHasNotFinished_StaysAttachmentsQueued()
    {
        var row = Row(1, "AddingAttachments", QueuedAddView());

        OfficeJobStatusService.ToEffectiveView(row, Now).CurrentPhase.Should().Be("AttachmentsQueued");
    }

    [Fact]
    public void AnyOtherSave_IsUnchanged_WhateverItsPipelineColumnsSay()
    {
        var row = Row(3, "AttachmentsIncomplete", QueuedAddView(phase: "Complete"), "X", "Y");

        var view = OfficeJobStatusService.ToEffectiveView(row, Now);

        view.CurrentPhase.Should().Be("Complete");
        view.Error.Should().BeNull("a finalization failure is not a failed save");
    }
}
