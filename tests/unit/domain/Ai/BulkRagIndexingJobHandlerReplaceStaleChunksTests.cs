using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// Task 048 (spaarkeai-word-add-in-r1) — <see cref="BulkRagIndexingJobHandler"/> is the admin / scheduled
/// bulk re-index path found during this task's audit. Unlike every other caller this task turns the trim
/// on for, this one already carries an explicit, first-class signal for "this item may already have
/// chunks": <see cref="BulkRagIndexingPayload.ForceReindex"/> — the SAME flag that already bypasses the
/// per-item idempotency skip (<c>ProcessSingleDocumentAsync</c>) and the "unindexed only" Dataverse filter
/// (<c>QueryDocumentsAsync</c>, <c>sprk_ragindexedon eq null</c>). Task 048 ties
/// <see cref="FileIndexRequest.ReplaceStaleChunks"/> to that SAME flag rather than turning it on
/// unconditionally, so the default "unindexed" sweep (every document here is a first index by
/// construction) never pays for a trim query that can only ever delete nothing.
/// </summary>
/// <remarks>
/// <para><b>Boundary doubles</b> (ADR-038): <see cref="IFileIndexingService"/> (captures the
/// <see cref="FileIndexRequest"/>), <see cref="IIdempotencyService"/>,
/// and the raw Dataverse OData <see cref="HttpClient"/> the handler builds internally from
/// <c>IHttpClientFactory.CreateClient("DataverseBatch")</c>. That last one is answered by a small,
/// HAND-WRITTEN <see cref="HttpMessageHandler"/> subclass returning one canned document row — this is
/// NOT <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038 B1 bans the Moq-based transport mock specifically,
/// because it encodes brittle per-request setups; a fixed-response fake handler is the established
/// alternative already used in this test tree, e.g. <c>DocumentIdentityContractTests.cs</c>).
/// <see cref="BatchJobStatusStore"/> and <see cref="RagTelemetry"/> are the REAL production classes —
/// both are inert without a real Redis / OpenTelemetry exporter, so no double is needed.</para>
/// </remarks>
public sealed class BulkRagIndexingJobHandlerReplaceStaleChunksTests
{
    private const string TenantId = "tenant-bulk-048";
    private const string DataverseUrl = "https://test.crm.dynamics.com";

    private static readonly string OneDocumentODataJson = JsonSerializer.Serialize(new
    {
        value = new[]
        {
            new
            {
                sprk_documentid = "77777777-0000-0000-0000-000000000048",
                sprk_filename = "already-indexed.docx",
                sprk_graphdriveid = "drive-bulk-048",
                sprk_graphitemid = "item-bulk-048",
            }
        }
    });

    private const string DocumentId = "77777777-0000-0000-0000-000000000048";
    private const string DefaultIndexName = "spaarke-files-index";

    /// <summary>The same document, stamped before task 041 with a non-default index.</summary>
    private static readonly string StaleStampODataJson = JsonSerializer.Serialize(new
    {
        value = new[]
        {
            new
            {
                sprk_documentid = DocumentId,
                sprk_filename = "already-indexed.docx",
                sprk_graphdriveid = "drive-bulk-048",
                sprk_graphitemid = "item-bulk-048",
                sprk_searchindexname = "spaarke-knowledge-index-v2",
            }
        }
    });

    private sealed class StaticJsonHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _json;
        public List<string> RequestedUrls { get; } = new();

        public StaticJsonHttpMessageHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private static (BulkRagIndexingJobHandler Handler, Mock<IFileIndexingService> FileIndexing, StaticJsonHttpMessageHandler HttpHandler)
        CreateSut(
            ILogger<BulkRagIndexingJobHandler>? logger = null,
            Mock<IDocumentDataverseService>? documentService = null,
            string? odataJson = null)
    {
        var fileIndexingMock = new Mock<IFileIndexingService>();
        fileIndexingMock
            .Setup(f => f.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileIndexingResult { Success = true, ChunksIndexed = 2 });

        var idempotencyMock = new Mock<IIdempotencyService>();
        idempotencyMock
            .Setup(s => s.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        idempotencyMock
            .Setup(s => s.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var statusStore = new BatchJobStatusStore(Mock.Of<IDistributedCache>(), NullLogger<BatchJobStatusStore>.Instance);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = DataverseUrl })
            .Build();

        var httpHandler = new StaticJsonHttpMessageHandler(odataJson ?? OneDocumentODataJson);
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock
            .Setup(f => f.CreateClient("DataverseBatch"))
            .Returns(() => new HttpClient(httpHandler));

        var credentialMock = new Mock<TokenCredential>();
        credentialMock
            .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessToken("fake-token", DateTimeOffset.UtcNow.AddHours(1)));

        var handler = new BulkRagIndexingJobHandler(
            fileIndexingMock.Object,
            statusStore,
            idempotencyMock.Object,
            (documentService ?? new Mock<IDocumentDataverseService>()).Object,
            Mock.Of<ISearchIndexNameResolver>(r => r.GetDefaultIndexName() == DefaultIndexName),
            new RagTelemetry(),
            configuration,
            httpClientFactoryMock.Object,
            credentialMock.Object,
            logger ?? NullLogger<BulkRagIndexingJobHandler>.Instance);

        return (handler, fileIndexingMock, httpHandler);
    }

    private static JobContract MakeJob(bool forceReindex) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = BulkRagIndexingJobHandler.JobTypeName,
        SubjectId = TenantId,
        CorrelationId = "corr-bulk-048",
        IdempotencyKey = $"bulk-rag-{TenantId}-{DateTimeOffset.UtcNow.Ticks}",
        Attempt = 1,
        MaxAttempts = 1,
        CreatedAt = DateTimeOffset.UtcNow,
        Payload = JsonDocument.Parse(JsonSerializer.Serialize(new BulkRagIndexingPayload
        {
            TenantId = TenantId,
            Filter = forceReindex ? "all" : "unindexed",
            MaxDocuments = 10,
            MaxConcurrency = 1,
            ForceReindex = forceReindex,
            Source = "Test",
        })),
    };

    [Fact]
    public async Task ProcessAsync_ForceReindexTrue_SetsReplaceStaleChunksTrueOnTheFileIndexRequest()
    {
        var (handler, fileIndexingMock, _) = CreateSut();
        FileIndexRequest? captured = null;
        fileIndexingMock
            .Setup(f => f.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .Callback<FileIndexRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new FileIndexingResult { Success = true, ChunksIndexed = 2 });

        var outcome = await handler.ProcessAsync(MakeJob(forceReindex: true), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed, "the single document must have indexed successfully");
        captured.Should().NotBeNull();
        captured!.ReplaceStaleChunks.Should().BeTrue(
            "ForceReindex is this handler's own explicit signal that a document may already carry " +
            "chunks — the same flag that already bypasses the per-item skip and the unindexed-only " +
            "Dataverse filter, so the trim runs wherever a re-index actually happens");
    }

    [Fact]
    public async Task ProcessAsync_ForceReindexFalse_LeavesReplaceStaleChunksFalseOnTheFileIndexRequest()
    {
        // The default "unindexed" sweep (ScheduledRagIndexingService and the admin default): every
        // document QueryDocumentsAsync returns here was selected BECAUSE it has never been indexed
        // (sprk_ragindexedon eq null) — there is never a tail to trim, so this negative case must NOT
        // pay for the extra search call task 048 proves is harmless-but-real everywhere else.
        var (handler, fileIndexingMock, _) = CreateSut();
        FileIndexRequest? captured = null;
        fileIndexingMock
            .Setup(f => f.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .Callback<FileIndexRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new FileIndexingResult { Success = true, ChunksIndexed = 2 });

        await handler.ProcessAsync(MakeJob(forceReindex: false), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ReplaceStaleChunks.Should().BeFalse(
            "the scheduled/default sweep only ever selects documents that were never indexed before " +
            "(sprk_ragindexedon eq null) — a trim call there can only ever delete nothing");
    }

    [Theory]
    [InlineData(true, 0, 1)]
    [InlineData(false, 1, 0)]
    public async Task ProcessAsync_FileSkippedByIndexing_IsCountedAsSkipped_NotAsProcessed(bool skipped, int processed, int skippedCount)
    {
        // ebilling-r1 task 028 (FR-16): a file in the E-Billing Output Files container is deliberately not
        // indexed. The bulk job must count it as skipped, not as an indexed document.
        var logger = new CompletionCapturingLogger();
        var documentService = new Mock<IDocumentDataverseService>();
        var (handler, fileIndexingMock, _) = CreateSut(logger, documentService);
        fileIndexingMock
            .Setup(f => f.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(skipped
                ? FileIndexingResult.Skip(FileIndexingService.NonIndexedContainerReason)
                : new FileIndexingResult { Success = true, ChunksIndexed = 2 });

        var outcome = await handler.ProcessAsync(MakeJob(forceReindex: false), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        logger.Completion.Should().NotBeNull("the handler logs its processed/skipped counts on completion");
        logger.Completion!["Processed"].Should().Be(processed);
        logger.Completion["Skipped"].Should().Be(skippedCount);
        // Task 041 review round 2 N3: a skipped file was not written to any index, so it is never stamped.
        documentService.Verify(
            d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()),
            skipped ? Times.Never() : Times.Once());
    }

    /// <summary>
    /// Task 041 (FR-40, #2502; design §14.7 must-fire 4): the document, its matter or the matter's business unit names another
    /// index. The bulk job reads none of them (it takes no index resolver): one write, with a request that names no index (the
    /// files index), and no INDEX_NOT_ALLOWED retry.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_WhenTheDocumentParentOrUnitNamesAnotherIndex_WritesTheFilesIndexOnce()
    {
        var (handler, fileIndexingMock, _) = CreateSut();
        FileIndexRequest? captured = null;
        fileIndexingMock
            .Setup(f => f.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .Callback<FileIndexRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new FileIndexingResult { Success = true, ChunksIndexed = 2 });

        var outcome = await handler.ProcessAsync(MakeJob(forceReindex: true), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        fileIndexingMock.Verify(f => f.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        var named = JsonSerializer.SerializeToElement(captured!).TryGetProperty("SearchIndexName", out var value)
                    && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        named.Should().BeNull("the index follows the content type: the request names none, so the files index is written");
    }

    /// <summary>
    /// Task 041 (FR-40, AC3; review round 1 F1): the bulk job writes the knowledge index, so it stamps that index on the
    /// document. A document stamped with another index before task 041 is re-stamped with the default on a force re-index.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_ForceReindexOfADocumentStampedWithAnotherIndex_RestampsTheDefaultIndex()
    {
        var documentService = new Mock<IDocumentDataverseService>();
        UpdateDocumentRequest? stamped = null;
        documentService
            .Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, UpdateDocumentRequest, CancellationToken>((_, req, _) => stamped = req)
            .Returns(Task.CompletedTask);
        var (handler, _, _) = CreateSut(documentService: documentService, odataJson: StaleStampODataJson);

        var outcome = await handler.ProcessAsync(MakeJob(forceReindex: true), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        documentService.Verify(d => d.UpdateDocumentAsync(DocumentId, It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        stamped!.SearchIndexName.Should().Be(DefaultIndexName, "the stamp always equals the index actually written, never the stale one");
        stamped.SearchIndexed.Should().BeTrue();
        stamped.SearchIndexCompletedOn.Should().NotBeNull();
    }

    /// <summary>Review round 1 F1: a failed stamp is logged and never fails the document or the job; indexing succeeded.</summary>
    [Fact]
    public async Task ProcessAsync_WhenTheStampFails_TheJobStillCompletesWithTheDocumentProcessed()
    {
        var documentService = new Mock<IDocumentDataverseService>();
        documentService
            .Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));
        var logger = new CompletionCapturingLogger();
        var (handler, fileIndexingMock, _) = CreateSut(logger, documentService, StaleStampODataJson);

        var outcome = await handler.ProcessAsync(MakeJob(forceReindex: true), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        logger.Completion!["Processed"].Should().Be(1);
        fileIndexingMock.Verify(f => f.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()), Times.Once,
            "a failed stamp never retries the index");
    }

    /// <summary>Captures the structured state of the handler's "completed" log line.</summary>
    private sealed class CompletionCapturingLogger : ILogger<BulkRagIndexingJobHandler>
    {
        public Dictionary<string, object?>? Completion { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> values
                && values.Any(v => v.Key == "{OriginalFormat}"
                    && (v.Value as string)?.StartsWith("Bulk RAG indexing job {JobId} completed", StringComparison.Ordinal) == true))
            {
                Completion = values.ToDictionary(v => v.Key, v => v.Value);
            }
        }
    }
}
