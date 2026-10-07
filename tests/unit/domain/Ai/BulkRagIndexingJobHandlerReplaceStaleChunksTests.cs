using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
/// <see cref="FileIndexRequest"/>), <see cref="IIdempotencyService"/>, <see cref="ISearchIndexNameResolver"/>,
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
        CreateSut()
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

        var resolverMock = new Mock<ISearchIndexNameResolver>();
        resolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var statusStore = new BatchJobStatusStore(Mock.Of<IDistributedCache>(), NullLogger<BatchJobStatusStore>.Instance);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = DataverseUrl })
            .Build();

        var httpHandler = new StaticJsonHttpMessageHandler(OneDocumentODataJson);
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
            resolverMock.Object,
            new RagTelemetry(),
            configuration,
            httpClientFactoryMock.Object,
            credentialMock.Object,
            NullLogger<BulkRagIndexingJobHandler>.Instance);

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
}
