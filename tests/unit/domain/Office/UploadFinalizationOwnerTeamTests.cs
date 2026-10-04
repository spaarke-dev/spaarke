using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Sprk.Bff.Api.Workers.Office;
using Sprk.Bff.Api.Workers.Office.Messages;
using Xunit;
using JobStatus = Sprk.Bff.Api.Models.Office.JobStatus;
using SaveContentType = Sprk.Bff.Api.Models.Office.SaveContentType;

namespace Sprk.Bff.Api.Tests.Domain.Office;

/// <summary>
/// The owner-team rule of <see cref="UploadFinalizationWorker"/> (spaarkeai-word-add-in-r1 task 080): every document
/// the worker creates for a save — an email's attachment children, and the fallback create — takes the team the save
/// CARRIED on its payload, so children always land in their parent's business unit. Only a payload without one (a
/// message enqueued before task 080) asks the resolver, and then from the save's own association and user.
/// </summary>
/// <remarks>
/// Asserted on the extracted static rule (ADR-038 A2: an internal member extracted because it carries a contract the
/// Service Bus worker's public surface cannot express without a full attachment pipeline). The writer side — that the
/// save puts the team ON the payload — is pinned end to end by <c>OfficeRecordOwnershipTests</c>.
/// </remarks>
[Trait("status", "new")]
public class UploadFinalizationOwnerTeamTests
{
    private static readonly Guid CarriedTeam = Guid.Parse("0a0a0a0a-0080-4080-8080-00000000000a");
    private static readonly Guid MatterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CallerOid = Guid.Parse("5a5a5a5a-0000-4000-8000-00000000cafe");

    [Fact]
    public async Task ResolveDocumentOwnerTeam_WhenThePayloadCarriesATeam_UsesItWithoutAskingTheResolver()
    {
        var resolver = new RecordOwnershipResolverDouble { TeamId = Guid.NewGuid() }; // a DIFFERENT answer, if asked

        var team = await UploadFinalizationWorker.ResolveDocumentOwnerTeamAsync(
            resolver, Payload(owningTeamId: CarriedTeam), CallerOid.ToString(), CancellationToken.None);

        team.Should().Be(CarriedTeam, "an attachment child must match the parent the save already owned");
        resolver.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveDocumentOwnerTeam_WhenThePayloadPredatesTheField_ResolvesFromTheAssociationAndUser()
    {
        var resolver = new RecordOwnershipResolverDouble();

        var team = await UploadFinalizationWorker.ResolveDocumentOwnerTeamAsync(
            resolver, Payload(owningTeamId: null), CallerOid.ToString(), CancellationToken.None);

        team.Should().Be(RecordOwnershipResolverDouble.DefaultTeamId);
        var asked = resolver.Requests.Should().ContainSingle().Subject;
        asked.TargetEntityLogicalName.Should().Be("matter", "the queued spelling; the resolver maps it");
        asked.TargetRecordId.Should().Be(MatterId);
        asked.CallerObjectId.Should().Be(CallerOid);
    }

    [Fact]
    public async Task ResolveDocumentOwnerTeam_WhenNothingResolves_ReturnsNull_SoTheWorkerCreatesNothing()
    {
        var resolver = new RecordOwnershipResolverDouble { TeamId = null };

        var team = await UploadFinalizationWorker.ResolveDocumentOwnerTeamAsync(
            resolver, Payload(owningTeamId: null), CallerOid.ToString(), CancellationToken.None);

        team.Should().BeNull("the worker refuses (fallback create) or skips (attachment children) — never app-owns");
    }

    private static UploadFinalizationPayload Payload(Guid? owningTeamId) => new()
    {
        ContentType = Sprk.Bff.Api.Models.Office.SaveContentType.Email,
        AssociationType = "matter",
        AssociationId = MatterId,
        ContainerId = "b!drive",
        TempFileLocation = "spe://b!drive/item",
        FileName = "Status.eml",
        OwningTeamId = owningTeamId,
    };

    // =================================================================================================
    // Task 093 (#1084 follow-on): the pipeline's terminal state. ProcessAsync is exercised directly —
    // the real UploadFinalizationWorker, doubled only at its module boundaries (IProcessingJobService,
    // JobSubmissionService, ITenantCache), per tests/CLAUDE.md's "mock at module boundaries" rule. The
    // Document content type with DocumentId already set, and AiOptions.RagIndex/InsightsIngest both
    // false, keeps the exercised surface to exactly the branch under test: the AppOnlyDocumentAnalysis
    // submission QueueNextStageAsync always makes once TriggerAiProcessing is true, with the email/
    // attachment/RAG/Insights side paths (each requiring their own boundary doubles) never reached.
    // =================================================================================================

    private static readonly Guid DocumentId = Guid.Parse("d0c0d0c0-0093-4093-8093-00000000d0c0");

    private sealed record WorkerFixture(
        UploadFinalizationWorker Worker,
        Mock<IProcessingJobService> ProcessingJobService,
        Mock<JobSubmissionService> JobSubmission,
        List<(Guid JobId, object Update)> RecordedUpdates);

    private static WorkerFixture CreateWorker()
    {
        var cache = new InMemoryTenantCache();

        var jobSubmission = new Mock<JobSubmissionService>(
            MockBehavior.Loose,
            Options.Create(new ServiceBusOptions()),
            NullLogger<JobSubmissionService>.Instance,
            new Mock<ServiceBusClient>().Object);
        jobSubmission
            .Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var recordedUpdates = new List<(Guid JobId, object Update)>();
        var processingJobService = new Mock<IProcessingJobService>();
        processingJobService
            .Setup(p => p.UpdateProcessingJobAsync(It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, object, CancellationToken>((id, update, _) => recordedUpdates.Add((id, update)))
            .Returns(Task.CompletedTask);

        var worker = new UploadFinalizationWorker(
            NullLogger<UploadFinalizationWorker>.Instance,
            cache,
            new Mock<ServiceBusClient>().Object,
            Mock.Of<IServiceScopeFactory>(), // not reached: RagIndex/InsightsIngest are off, ContentType is Document
            Options.Create(new ServiceBusOptions()),
            Options.Create(new GraphOptions()),
            Mock.Of<IDocumentDataverseService>(), // not reached: payload.DocumentId is already set
            processingJobService.Object,
            jobSubmission.Object,
            new ConfigurationBuilder().Build(),
            Mock.Of<IRecordOwnershipResolver>()); // not reached: no fallback create, no attachment children

        return new WorkerFixture(worker, processingJobService, jobSubmission, recordedUpdates);
    }

    private static OfficeJobMessage Message(bool triggerAiProcessing, int attempt = 1, int maxAttempts = 3) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = OfficeJobType.UploadFinalization,
        CorrelationId = "corr-093",
        IdempotencyKey = $"idem-093-{Guid.NewGuid():N}",
        UserId = CallerOid.ToString(),
        Attempt = attempt,
        MaxAttempts = maxAttempts,
        Payload = JsonSerializer.SerializeToElement(new UploadFinalizationPayload
        {
            ContentType = SaveContentType.Document,
            ContainerId = "b!drive",
            TempFileLocation = "spe://b!drive/item-093",
            FileName = "Brief.docx",
            FileSize = 1024,
            DocumentId = DocumentId,
            TriggerAiProcessing = triggerAiProcessing,
            AiOptions = new AiProcessingOptions { ProfileSummary = true, RagIndex = false, InsightsIngest = false },
        }),
    };

    /// <summary>The Dataverse <c>sprk_status</c> value an anonymous update object carries, read the same way
    /// production's own fake world does (<c>OfficeEndpointsContractTests.OfficeVersionSaveWorld.UpdateJob</c>):
    /// anonymous types' properties are public, so this is reflection over a KNOWN, public shape — not the
    /// non-public-member reflection ADR-038 B8 bans.</summary>
    private static int? StatusOf(object update) => (int?)update.GetType().GetProperty("Status")?.GetValue(update);

    private static string? StageOf(object update) => (string?)update.GetType().GetProperty("CurrentStage")?.GetValue(update);

    private static int? ProgressOf(object update) => (int?)update.GetType().GetProperty("Progress")?.GetValue(update);

    [Fact]
    public async Task ProcessAsync_SuccessfulSaveWithAiProcessing_EndsTerminalCompletedWithAStageNamingWhatWasHandedOn()
    {
        var fixture = CreateWorker();
        var message = Message(triggerAiProcessing: true);

        var outcome = await fixture.Worker.ProcessAsync(message, CancellationToken.None);

        outcome.IsSuccess.Should().BeTrue("queuing the follow-on AI work is this job's success condition, not its failure");

        // The AI analysis hand-off actually happened — "a stage that names what was handed on" must be true,
        // not just worded that way.
        fixture.JobSubmission.Verify(
            j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "the terminal write below claims AI analysis was queued; it must actually have been queued");

        var final = fixture.RecordedUpdates.Should().Contain(u => u.JobId == message.JobId && StatusOf(u.Update) == 2)
            .Subject;
        StatusOf(final.Update).Should().Be(2, "2 = Completed — the row must end in a TERMINAL status (ADR-017), not stay Running");
        StageOf(final.Update).Should().Be(
            "AiAnalysisQueued",
            "the stage must name what was handed on, not just report 70% / FileUploaded forever "
            + "(#1084 follow-on: before this fix nothing ever wrote a terminal status once this branch ran)");
        ProgressOf(final.Update).Should().Be(100);

        // Constraint (task 060 / this task): must not touch sprk_result. The worker's update object carries
        // exactly the pipeline columns — no "Result" property exists on it at all.
        final.Update.GetType().GetProperty("Result").Should().BeNull(
            "the pipeline's terminal write must never carry sprk_result — that is the SAVE's own view (task 060), "
            + "and the pane's behaviour must stay byte-for-byte unchanged");
    }

    [Fact]
    public async Task ProcessAsync_FinalizationFailsOnTheLastAttempt_EndsTerminalFailed()
    {
        var fixture = CreateWorker();
        fixture.JobSubmission
            .Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Service Bus submission failure"));
        var message = Message(triggerAiProcessing: true, attempt: 3, maxAttempts: 3);

        var outcome = await fixture.Worker.ProcessAsync(message, CancellationToken.None);

        outcome.IsSuccess.Should().BeFalse();
        outcome.ErrorCode.Should().Be("OFFICE_012");
        outcome.Retryable.Should().BeFalse("this was already the last attempt");

        var final = fixture.RecordedUpdates.Should().Contain(u => u.JobId == message.JobId && StatusOf(u.Update) == 3)
            .Subject;
        StatusOf(final.Update).Should().Be(3, "3 = Failed — a finalization failure on the last attempt is a TERMINAL outcome");
        StageOf(final.Update).Should().Be("Failed");
        ProgressOf(final.Update).Should().Be(0);
        final.Update.GetType().GetProperty("ErrorCode")?.GetValue(final.Update).Should().Be("OFFICE_012");
    }
}
