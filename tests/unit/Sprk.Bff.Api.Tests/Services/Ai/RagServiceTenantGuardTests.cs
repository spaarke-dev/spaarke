using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Security;
using Xunit;

using AzureSdkIndexingResult = Azure.Search.Documents.Models.IndexingResult;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// The two tenant guards unified-access-control-r2 task 163 added inside <see cref="RagService"/> (sweep
/// findings #6 and #30). Under the Shared deployment model every tenant's chunks live in ONE physical index,
/// partitioned only by the <c>tenantId</c> field, so:
/// <list type="bullet">
///   <item><see cref="RagService.DeleteDocumentAsync"/> must delete a chunk only when the chunk with that key
///   carries the caller's tenant — a key from another tenant's partition and an absent key both delete
///   nothing and return false;</item>
///   <item><see cref="RagService.IndexDocumentsBatchAsync(IEnumerable{KnowledgeDocument}, string?, CancellationToken)"/>
///   (both overloads) must refuse a list whose documents carry more than one TenantId BEFORE any embedding or
///   write — it picks the search client from the first document but writes each with its own TenantId.</item>
/// </list>
/// The search client is substituted at the SDK boundary (virtual members, results built with
/// <see cref="SearchModelFactory"/>); the in-memory "index" below answers the ownership query by its filter.
/// </summary>
public sealed class RagServiceTenantGuardTests
{
    private const string CallerTenant = "tenant-a";
    private const string OtherTenant = "tenant-b";

    private readonly Mock<IKnowledgeDeploymentService> _deployment = new();
    private readonly Mock<IOpenAiClient> _openAi = new();
    private readonly Mock<SearchClient> _searchClient = new();
    private readonly List<(string Id, string TenantId)> _index = [];

    public RagServiceTenantGuardTests()
    {
        _deployment
            .Setup(d => d.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_searchClient.Object);

        // The ownership lookup: answer exactly like an index holding _index would for "id eq 'k' and tenantId eq 't'".
        _searchClient
            .Setup(c => c.SearchAsync<KnowledgeDocument>(It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, SearchOptions options, CancellationToken _) =>
            {
                var hits = _index
                    .Where(chunk => options.Filter.Contains($"id eq '{chunk.Id}'", StringComparison.Ordinal)
                                    && options.Filter.Contains($"tenantId eq '{chunk.TenantId}'", StringComparison.Ordinal))
                    .Select(chunk => SearchModelFactory.SearchResult(
                        new KnowledgeDocument { Id = chunk.Id, TenantId = chunk.TenantId }, 1.0, null))
                    .ToList();

                return Response.FromValue(
                    SearchModelFactory.SearchResults<KnowledgeDocument>(hits, hits.Count, null, null, null!), null!);
            });

        _searchClient
            .Setup(c => c.DeleteDocumentsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IndexDocumentsOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IEnumerable<string> keys, IndexDocumentsOptions _, CancellationToken _) =>
                Response.FromValue(
                    SearchModelFactory.IndexDocumentsResult(
                        keys.Select(k => SearchModelFactory.IndexingResult(k, null, true, 200)).ToList<AzureSdkIndexingResult>()),
                    null!));
    }

    // =====================================================================================================
    // DeleteDocumentAsync — tenant-bound
    // =====================================================================================================

    [Fact]
    public async Task DeleteDocumentAsync_ChunkInTheCallersPartition_IsDeleted()
    {
        _index.Add(("chunk-1", CallerTenant));

        var deleted = await CreateService().DeleteDocumentAsync("chunk-1", CallerTenant);

        deleted.Should().BeTrue();
        _searchClient.Verify(c => c.DeleteDocumentsAsync("id", It.Is<IEnumerable<string>>(k => k.Single() == "chunk-1"),
            It.IsAny<IndexDocumentsOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("another-tenants-chunk")]
    [InlineData("absent")]
    public async Task DeleteDocumentAsync_ChunkOfAnotherTenantOrAbsent_DeletesNothing_AndReturnsFalse(string mode)
    {
        if (mode == "another-tenants-chunk")
        {
            _index.Add(("chunk-1", OtherTenant));
        }

        var deleted = await CreateService().DeleteDocumentAsync("chunk-1", CallerTenant);

        deleted.Should().BeFalse("a key outside the caller's partition must look exactly like an absent key");
        _searchClient.Verify(c => c.DeleteDocumentsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
            It.IsAny<IndexDocumentsOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =====================================================================================================
    // IndexDocumentsBatchAsync — one tenant per batch (both overloads)
    // =====================================================================================================

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexDocumentsBatchAsync_MixedTenants_ThrowsBeforeAnyEmbeddingOrWrite(bool threeArgOverload)
    {
        var documents = new[]
        {
            new KnowledgeDocument { Id = "a", TenantId = CallerTenant, SpeFileId = "f", Content = "x" },
            new KnowledgeDocument { Id = "b", TenantId = OtherTenant, SpeFileId = "f", Content = "y" },
        };
        var service = CreateService();

        Func<Task> act = threeArgOverload
            ? () => service.IndexDocumentsBatchAsync(documents, "some-index", CancellationToken.None)
            : () => service.IndexDocumentsBatchAsync(documents, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        _openAi.Verify(o => o.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _deployment.Verify(d => d.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _deployment.Verify(d => d.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IndexDocumentsBatchAsync_ACaseVariantOfTheSameTenant_IsAlsoRefused()
    {
        // Partition keys compare as strings; "TENANT-A" would be a separate, silently-created partition.
        var documents = new[]
        {
            new KnowledgeDocument { Id = "a", TenantId = CallerTenant, SpeFileId = "f", Content = "x" },
            new KnowledgeDocument { Id = "b", TenantId = CallerTenant.ToUpperInvariant(), SpeFileId = "f", Content = "y" },
        };

        var act = () => CreateService().IndexDocumentsBatchAsync(documents);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private RagService CreateService() =>
        new(
            _deployment.Object,
            _openAi.Object,
            new Mock<IEmbeddingCache>().Object,
            new Mock<IPrivilegeGroupResolver>().Object,
            Options.Create(new AnalysisOptions()),
            new Mock<SearchIndexClient>(MockBehavior.Loose).Object,
            Options.Create(new AiSearchOptions { Endpoint = "https://test-search.search.windows.net" }),
            NullLogger<RagService>.Instance);
}
