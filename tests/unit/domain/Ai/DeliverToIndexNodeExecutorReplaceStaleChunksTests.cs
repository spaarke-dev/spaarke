using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using System.Text.Json;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// Task 048 (spaarkeai-word-add-in-r1) — the playbook "Index" node
/// (<see cref="DeliverToIndexNodeExecutor"/>, <c>ExecutorType.DeliverToIndex</c>) is one of the callers
/// named in the task brief ("possibly the playbook Index node") and confirmed by 029's own residual note
/// (§9 item 4): a playbook can place this node after the document was already indexed earlier in the
/// same run or a prior run, and before this task it enqueued the per-item key with no trim.
/// </summary>
/// <remarks>
/// <para><b>Boundary double</b>: <see cref="JobSubmissionService"/> (the Service Bus wire, mocked at its
/// own method boundary — the same class the pre-existing enqueuer test and 048's other new domain tests
/// mock). <see cref="ITemplateEngine"/> is a loose mock never actually invoked by these cases: none of
/// the config values used here contain a <c>{{}}</c> placeholder, so production's own
/// <c>ResolveTemplate</c> short-circuits before calling it. <see cref="DeliverToIndexNodeExecutor"/>
/// itself is the REAL production class.</para>
/// </remarks>
public sealed class DeliverToIndexNodeExecutorReplaceStaleChunksTests
{
    private readonly Mock<JobSubmissionService> _jobSubmissionMock;
    private readonly DeliverToIndexNodeExecutor _executor;

    public DeliverToIndexNodeExecutorReplaceStaleChunksTests()
    {
        var sbOptions = new Mock<IOptions<ServiceBusOptions>>();
        sbOptions.Setup(o => o.Value).Returns(new ServiceBusOptions
        {
            QueueName = "test-jobs",
            CommunicationQueueName = "test-comms",
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        });
        _jobSubmissionMock = new Mock<JobSubmissionService>(
            MockBehavior.Strict,
            sbOptions.Object,
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<ServiceBusClient>().Object);

        _executor = new DeliverToIndexNodeExecutor(
            Mock.Of<ITemplateEngine>(),
            _jobSubmissionMock.Object,
            Mock.Of<ILogger<DeliverToIndexNodeExecutor>>());
    }

    private static NodeExecutionContext CreateContext(string configJson = @"{""indexName"":""knowledge""}")
    {
        var nodeId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = nodeId,
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Deliver to Index",
                ExecutionOrder = 3,
                OutputVariable = "indexResult",
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction { Id = actionId, Name = "Deliver to Index" },
            ExecutorType = ExecutorType.DeliverToIndex,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "tenant-048",
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "Playbook document",
                FileName = "playbook-doc.pdf",
                ExtractedText = "some previously extracted text",
                Metadata = new Dictionary<string, object?>
                {
                    ["GraphDriveId"] = "drive-playbook",
                    ["GraphItemId"] = "item-playbook",
                }
            }
        };
    }

    private static RagIndexingJobPayload DeserializePayload(JobContract job)
    {
        var json = job.Payload!.RootElement.GetRawText();
        return JsonSerializer.Deserialize<RagIndexingJobPayload>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    [Fact]
    public async Task ExecuteAsync_ValidNode_SetsReplaceStaleChunksTrueOnTheSubmittedPayload()
    {
        JobContract? capturedJob = null;
        _jobSubmissionMock
            .Setup(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => capturedJob = job)
            .Returns(Task.CompletedTask);

        var result = await _executor.ExecuteAsync(CreateContext(), CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedJob.Should().NotBeNull();
        capturedJob!.JobType.Should().Be(RagIndexingJobHandler.JobTypeName);
        capturedJob.IdempotencyKey.Should().Be("rag-index-drive-playbook-item-playbook",
            "the key is UNCHANGED by this task — only whether the trim runs");

        var payload = DeserializePayload(capturedJob);
        payload.ReplaceStaleChunks.Should().BeTrue(
            "a playbook can run the Index node again over a document it (or another path) already " +
            "indexed; a first index simply finds nothing to trim");
    }

    [Fact]
    public async Task ExecuteAsync_MissingGraphIds_StillDoesNotSubmitAnyJob()
    {
        // Negative control: the pre-existing validation short-circuit (no DriveId/ItemId in
        // DocumentContext.Metadata) is untouched by this task — no job, so no payload to trim.
        var context = CreateContext() with
        {
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "No SPE metadata",
                ExtractedText = "text",
                Metadata = new Dictionary<string, object?>()
            }
        };

        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        _jobSubmissionMock.Verify(
            s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
