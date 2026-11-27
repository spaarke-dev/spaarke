using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Email;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Auth.SpeAdmin;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 141 review (F2), task 138's key-matched add: once the status read reports an add job's worker
/// finished (phase <c>AttachmentsAdded</c> or <c>AttachmentsIncomplete</c>), a re-save under the same key is not held back as
/// "still being created" (it plans what is missing now), whatever the processed mark says.
/// </summary>
[Trait("status", "task-141-word-add-in-r1")]
public class EmailAttachmentAddPlannerFinishedAddTests
{
    [Theory]
    [InlineData("AttachmentsAdded")]
    [InlineData("AttachmentsIncomplete")]
    public async Task AFinishedAddJob_IsNotPending_EvenWhenYoung_AndWithNoProcessedMark(string phase)
    {
        var cache = new Mock<ITenantCache>(); // no mark under any key
        var job = new JobStatusResponse
        {
            JobId = Guid.NewGuid(),
            Status = JobStatus.Completed,
            JobType = JobType.EmailSave,
            Progress = 100,
            CurrentPhase = phase,
            CompletedPhases = new List<CompletedPhase>(),
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
        };

        var pending = await Planner(cache.Object).IsFinalizationPendingAsync(job, "idem-141", CancellationToken.None);

        pending.Should().BeFalse("the worker has recorded its outcome; the children are there to be counted");
    }

    [Fact]
    public async Task AQueuedAddJob_WithNoMark_IsStillPending_WhileYoung()
    {
        var job = new JobStatusResponse
        {
            JobId = Guid.NewGuid(),
            Status = JobStatus.Completed,
            JobType = JobType.EmailSave,
            Progress = 100,
            CurrentPhase = "AttachmentsQueued",
            CompletedPhases = new List<CompletedPhase>(),
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
        };

        (await Planner(Mock.Of<ITenantCache>()).IsFinalizationPendingAsync(job, "idem-141", CancellationToken.None))
            .Should().BeTrue("unchanged: the worker has not finished");
    }

    private static EmailAttachmentAddPlanner Planner(ITenantCache cache)
    {
        var graphClientFactory = Mock.Of<IGraphClientFactory>();
        var spe = new Mock<SpeFileStore>(
            MockBehavior.Loose,
            new ContainerOperations(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<ILogger<ContainerOperations>>()),
            new DriveItemOperations(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<ILogger<DriveItemOperations>>()),
            new UploadSessionManager(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
            new UserOperations(graphClientFactory, Mock.Of<ILogger<UserOperations>>()),
            null!);
        return new EmailAttachmentAddPlanner(
            Mock.Of<IDocumentDataverseService>(),
            Mock.Of<IGenericEntityService>(),
            spe.Object,
            Mock.Of<IEmailToEmlConverter>(),
            new AttachmentFilterService(Options.Create(new EmailProcessingOptions()), NullLogger<AttachmentFilterService>.Instance),
            cache,
            new ConfigurationBuilder().Build(),
            TimeProvider.System,
            NullLogger<EmailAttachmentAddPlanner>.Instance);
    }
}
