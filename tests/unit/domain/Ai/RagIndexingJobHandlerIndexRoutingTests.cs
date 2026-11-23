using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// field-mapping-server-write-path-r1 task 041 (FR-40, #2502): the RAG indexing job writes the files index and stamps it,
/// whatever its payload, its document, its parent or the parent's business unit names. Design §14.7 must-fire 4.
/// </summary>
/// <remarks>
/// <para><b>Boundary doubles</b> (ADR-038): <see cref="IFileIndexingService"/> (captures the request handed to the pipeline),
/// <see cref="IIdempotencyService"/>, <see cref="IDocumentDataverseService"/> (the stamp) and a STRICT
/// <see cref="ISearchIndexNameResolver"/> that answers only the default: any record or business-unit read through it fails
/// the job, so "no catalog-link fetch is issued" is observable. The handler and its parent resolver are production code.</para>
/// </remarks>
public sealed class RagIndexingJobHandlerIndexRoutingTests
{
    private const string FilesIndex = "spaarke-files-index";
    private const string DocumentId = "41000000-0000-4000-8000-0000000000d1";
    private const string MatterId = "41000000-0000-4000-8000-0000000000a1";

    private readonly Mock<IFileIndexingService> _files = new();
    private readonly Mock<IIdempotencyService> _idempotency = new();
    private readonly Mock<IDocumentDataverseService> _documents = new();
    private readonly Mock<ISearchIndexNameResolver> _names = new(MockBehavior.Strict);

    public RagIndexingJobHandlerIndexRoutingTests()
    {
        _idempotency.Setup(i => i.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _idempotency
            .Setup(i => i.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _names.Setup(n => n.GetDefaultIndexName()).Returns(FilesIndex);
        _documents
            .Setup(d => d.GetDocumentAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity { Id = DocumentId, Name = "lease.docx", SearchIndexName = "spaarke-knowledge-index-v2" });
    }

    /// <summary>
    /// A queued payload as the producers wrote it before FR-40: the index field is in the JSON (an old job, or a producer that
    /// still sends one) or absent (every current producer), and a parent is named.
    /// </summary>
    private static JobContract Job(string? payloadIndex) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = RagIndexingJobHandler.JobTypeName,
        SubjectId = DocumentId,
        CorrelationId = "corr-041",
        IdempotencyKey = string.Empty,
        Attempt = 1,
        MaxAttempts = 3,
        Payload = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["tenantId"] = "tenant-041",
            ["driveId"] = "drive-041",
            ["itemId"] = "item-041",
            ["fileName"] = "lease.docx",
            ["documentId"] = DocumentId,
            ["parentEntity"] = new { entityType = "matter", entityId = MatterId, entityName = "Acme v. Widget" },
            ["searchIndexName"] = payloadIndex,
        })),
    };

    private RagIndexingJobHandler Handler() => new(
        _files.Object, _idempotency.Object, _documents.Object, _names.Object,
        TestDocumentIndexParentResolver.Over(), new RagTelemetry(), NullLogger<RagIndexingJobHandler>.Instance);

    [Theory]
    [InlineData("spaarke-invoices-index")]
    [InlineData(null)]
    public async Task ProcessAsync_WhenThePayloadDocumentParentOrUnitNamesAnotherIndex_WritesAndStampsTheFilesIndex(string? payloadIndex)
    {
        FileIndexRequest? written = null;
        _files
            .Setup(f => f.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .Callback<FileIndexRequest, CancellationToken>((r, _) => written = r)
            .ReturnsAsync(FileIndexingResult.Succeeded(chunksIndexed: 3, duration: TimeSpan.FromSeconds(1)));
        UpdateDocumentRequest? stamp = null;
        _documents
            .Setup(d => d.UpdateDocumentAsync(DocumentId, It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, UpdateDocumentRequest, CancellationToken>((_, u, _) => stamp = u)
            .Returns(Task.CompletedTask);

        var outcome = await Handler().ProcessAsync(Job(payloadIndex), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed, "the strict resolver answers only the default: no record or unit is read");
        _files.Verify(f => f.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()), Times.Once,
            "one write: there is no INDEX_NOT_ALLOWED retry any more");
        var named = JsonSerializer.SerializeToElement(written!).TryGetProperty("SearchIndexName", out var value)
                    && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        named.Should().BeNull("the request names no index, so the pipeline writes the files index");
        stamp!.SearchIndexName.Should().Be(FilesIndex, "the stamp equals the index written");
    }
}
