using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using System.Text.Json;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// Task 048 (spaarkeai-word-add-in-r1) started this file for task 029's chunk-trim switch on every re-index path that shares
/// <see cref="PostUploadIndexingEnqueuer"/>; GitHub #3304 removed the switch (every index now trims inside
/// <see cref="FileIndexingService"/>), and what stays here is the seam's job shape: the idempotency key and the skip gates.
/// This is the highest-leverage single seam: both its methods are the ONLY place seven
/// producers build their indexing request — <c>EnqueueIfApplicableAsync</c> (OBO) is called by Compose
/// save-back (<c>ComposeService</c>) and the "LinearDocumentProfile" direct-Action re-index
/// (<c>AnalysisEndpoints</c>); <c>EnqueueAppOnlyIfApplicableAsync</c> (app-only via Service Bus) is called
/// by Office create + version save (<c>UploadFinalizationWorker</c>), Email-to-Document
/// (<c>IncomingCommunicationProcessor</c>), outbound-email enrichment (<c>CommunicationEnrichmentService</c>)
/// and post-AI-analysis re-index (<c>AnalysisResultPersistence</c>). None of those seven callers add any
/// logic of their own around the flag — proving it here proves it for all seven.
/// </summary>
/// <remarks>
/// <para><b>Boundary doubles only</b> (ADR-038): <see cref="IFileIndexingService"/> (the OBO pipeline this
/// class does not own), <see cref="JobSubmissionService"/> (the Service Bus wire — mocked at its own
/// method boundary the same way the pre-existing <c>PostUploadIndexingEnqueuerTests.cs</c> already does;
/// this is the class under test's own collaborator, not a transport-level
/// <c>Mock&lt;HttpMessageHandler&gt;</c>), and <see cref="IDocumentDataverseService"/> (Dataverse tracking
/// writes, irrelevant to this file's assertions). <see cref="PostUploadIndexingEnqueuer"/> itself is the
/// REAL production class.</para>
/// <para><b>Why a separate file instead of extending the existing
/// <c>tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PostUploadIndexingEnqueuerTests.cs</c>.</b> That file
/// predates the ADR-038 KEEP-path reorganization and lives outside all eight deletion-protected paths
/// (<c>tests/CLAUDE.md</c>). Per the binding "new tests in NEW test files where possible" constraint plus
/// ADR-038, new coverage goes here, at the <c>tests/unit/domain/**</c> KEEP path (the same placement task
/// 029 used for <c>RagServiceChunkTrimTests.cs</c>), never modifying or weakening the existing file.</para>
/// </remarks>
public sealed class PostUploadIndexingEnqueuerIndexKeyTests
{
    private readonly Mock<IFileIndexingService> _fileIndexingMock = new();
    private readonly Mock<JobSubmissionService> _jobSubmissionMock;
    private readonly Mock<IDocumentDataverseService> _documentServiceMock = new();
    private readonly Mock<ILogger<PostUploadIndexingEnqueuer>> _loggerMock = new();
    private readonly PostUploadIndexingOptions _options = new();
    private readonly ISearchIndexNameResolver _indexNames =
        Mock.Of<ISearchIndexNameResolver>(r => r.GetDefaultIndexName() == "spaarke-files-index");

    public PostUploadIndexingEnqueuerIndexKeyTests()
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
    }

    private PostUploadIndexingEnqueuer CreateSut() =>
        new(
            _fileIndexingMock.Object,
            _jobSubmissionMock.Object,
            _documentServiceMock.Object,
            TestDocumentIndexParentResolver.Over(),
            _indexNames,
            Options.Create(_options),
            _loggerMock.Object);

    private static HttpContext CreateHttpContext() => new DefaultHttpContext();

    private static PostUploadIndexingRequest ValidRequest(string? versionDiscriminator = null) =>
        new(
            TenantId: "tenant-048",
            DriveId: "drive-abc",
            ItemId: "item-xyz",
            FileName: "contract.docx",
            FileSizeBytes: 2048,
            ContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            DocumentId: "doc-guid-048",
            ParentEntity: null,
            Source: "TestSource",
            CorrelationId: "corr-id-048",
            VersionDiscriminator: versionDiscriminator);

    private static RagIndexingJobPayload DeserializePayload(JobContract job)
    {
        var json = job.Payload!.RootElement.GetRawText();
        return JsonSerializer.Deserialize<RagIndexingJobPayload>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    // GitHub #3304: the OBO fact that pinned FileIndexRequest.ReplaceStaleChunks went with the switch. Every index now removes
    // the file's leftover chunks inside FileIndexingService (tests/integration/regression/Issue3304_ReindexLeavesNoOldChunksTests.cs).

    // ===== App-only path — Office, Email-to-Document, outbound enrichment, post-analysis re-index ====

    [Fact]
    public async Task EnqueueAppOnlyIfApplicableAsync_NoVersionDiscriminator_KeepsThePlainPerItemKey()
    {
        // Every app-only producer through this seam that is not a version save (Office CREATE save, Email-to-Document,
        // CommunicationEnrichmentService, AnalysisResultPersistence) keeps the plain per-item key.
        JobContract? capturedJob = null;
        _jobSubmissionMock
            .Setup(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => capturedJob = job)
            .Returns(Task.CompletedTask);

        var result = await CreateSut().EnqueueAppOnlyIfApplicableAsync(
            ValidRequest(versionDiscriminator: null), CancellationToken.None);

        result.JobSubmitted.Should().BeTrue();
        capturedJob.Should().NotBeNull();
        DeserializePayload(capturedJob!).ItemId.Should().Be("item-xyz");
        capturedJob!.IdempotencyKey.Should().Be("rag-index-drive-abc-item-xyz",
            "the plain per-item key (no version suffix) must be preserved for every non-version caller");
    }

    [Fact]
    public async Task EnqueueAppOnlyIfApplicableAsync_WithVersionDiscriminator_StillAppendsVersionSuffixToIdempotencyKey()
    {
        // Regression guard for task 029's mechanism: VersionDiscriminator's one remaining role is this key suffix, so a
        // version is not skipped as a duplicate of the item's earlier index.
        JobContract? capturedJob = null;
        _jobSubmissionMock
            .Setup(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => capturedJob = job)
            .Returns(Task.CompletedTask);

        var jobId = Guid.NewGuid().ToString("N");
        var result = await CreateSut().EnqueueAppOnlyIfApplicableAsync(
            ValidRequest(versionDiscriminator: jobId), CancellationToken.None);

        result.JobSubmitted.Should().BeTrue();
        capturedJob!.IdempotencyKey.Should().Be($"rag-index-drive-abc-item-xyz-version-{jobId}",
            "task 029's version-key suffix keeps a version from being skipped as the item's earlier index");
    }

    [Fact]
    public async Task EnqueueAppOnlyIfApplicableAsync_FeatureFlagOff_StillSkipsBeforeBuildingAnyPayload()
    {
        // Negative control: the skip-gate ordering (feature flag → tenant → SPE ids → size → content-type)
        // that runs BEFORE the payload is built is untouched by this task.
        _options.PostUploadEnqueueEnabled = false;

        var result = await CreateSut().EnqueueAppOnlyIfApplicableAsync(ValidRequest(), CancellationToken.None);

        result.SkipReason.Should().Be("FeatureFlagDisabled");
        _jobSubmissionMock.Verify(
            s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
