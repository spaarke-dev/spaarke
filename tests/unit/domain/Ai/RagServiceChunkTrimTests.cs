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

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// Task 029 (spaarkeai-word-add-in-r1, owner decision 2026-09-15, option B) — the index half of "a version save
/// replaces the document's chunks": <see cref="RagService.DeleteChunksBeyondCountAsync"/> removes the leftover tail
/// a previous, longer version left behind, from the index the new chunks were written to.
/// </summary>
/// <remarks>
/// <para><b>Why these assertions.</b> The trim is the only DELETE on the version re-index path, so what it may reach
/// is the whole risk: the WRONG index (the defect that ruled out the existing
/// <see cref="RagService.DeleteBySourceDocumentAsync"/>, which only ever targets the tenant default), another writer's
/// chunks for the same file, or the file's CURRENT chunks. Each test pins one of those.</para>
/// <para>Boundary doubles only: <see cref="IKnowledgeDeploymentService"/> (index routing) and the Azure SDK
/// <see cref="SearchClient"/> (the wire). The <see cref="SearchClient"/> double answers with the rows AI Search would
/// return for the filter and records every query and delete.</para>
/// </remarks>
public sealed class RagServiceChunkTrimTests
{
    private const string Tenant = "tenant-029";
    private const string SpeFileId = "01VERSIONEDITEM";
    private const string RoutedIndex = "spaarke-files-matter-index";

    private readonly Mock<IKnowledgeDeploymentService> _deployment = new();
    private readonly Mock<SearchClient> _searchClient = new();
    private readonly List<SearchOptions> _queries = new();
    private readonly List<string[]> _deleteBatches = new();
    private List<KnowledgeDocument> _matchingRows = new();
    private Func<string, bool> _deleteSucceeds = _ => true;

    public RagServiceChunkTrimTests()
    {
        _deployment
            .Setup(d => d.GetSearchClientAsync(Tenant, RoutedIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_searchClient.Object);
        _deployment
            .Setup(d => d.GetSearchClientAsync(Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_searchClient.Object);

        _searchClient
            .Setup(c => c.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, SearchOptions options, CancellationToken _) =>
            {
                _queries.Add(options);
                var page = _matchingRows
                    .Skip(options.Skip ?? 0)
                    .Take(options.Size ?? 50)
                    .Select(row => SearchModelFactory.SearchResult(row, 1.0, null))
                    .ToList();
                return Response.FromValue(
                    SearchModelFactory.SearchResults(page, _matchingRows.Count, null, null, null!), null!);
            });

        _searchClient
            .Setup(c => c.DeleteDocumentsAsync(
                "id", It.IsAny<IEnumerable<string>>(), It.IsAny<IndexDocumentsOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IEnumerable<string> keys, IndexDocumentsOptions _, CancellationToken _) =>
            {
                var batch = keys.ToArray();
                _deleteBatches.Add(batch);
                var results = batch
                    .Select(key => _deleteSucceeds(key)
                        ? SearchModelFactory.IndexingResult(key, null, true, 200)
                        : SearchModelFactory.IndexingResult(key, "rejected", false, 500))
                    .ToList();
                return Response.FromValue(SearchModelFactory.IndexDocumentsResult(results), null!);
            });
    }

    private RagService CreateService() => new(
        _deployment.Object,
        Mock.Of<IOpenAiClient>(),
        Mock.Of<IEmbeddingCache>(),
        Mock.Of<IPrivilegeGroupResolver>(),
        Options.Create(new AnalysisOptions()),
        new Mock<SearchIndexClient>(MockBehavior.Loose).Object,
        Options.Create(new AiSearchOptions
        {
            Endpoint = "https://test-search.search.windows.net",
            KnowledgeIndexName = "spaarke-files-index",
            DiscoveryIndexName = "discovery-index",
            SessionFilesIndexName = "spaarke-session-files",
            SessionFilesSemanticConfigName = "session-files-semantic-config"
        }),
        NullLogger<RagService>.Instance);

    private static KnowledgeDocument Chunk(string id, int chunkIndex) => new()
    {
        Id = id,
        TenantId = Tenant,
        SpeFileId = SpeFileId,
        ChunkIndex = chunkIndex
    };

    [Fact]
    public async Task DeleteChunksBeyondCount_WithRoutedIndex_DeletesOnlyThisFilesTailInThatIndex()
    {
        // The previous version had 5 chunks; the new one has 3. AI Search returns the tail {S}_3 and {S}_4 — and a
        // chunk RagIndexingPipeline stored for the same file under ITS id scheme, which is not a leftover of ours.
        _matchingRows = new List<KnowledgeDocument>
        {
            Chunk($"{SpeFileId}_3", 3),
            Chunk($"{SpeFileId}_4", 4),
            Chunk("doc-029_knowledge_5", 5),
        };

        var deleted = await CreateService().DeleteChunksBeyondCountAsync(Tenant, SpeFileId, 3, RoutedIndex);

        deleted.Should().Be(2);
        _deleteBatches.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new[] { $"{SpeFileId}_3", $"{SpeFileId}_4" },
                "only this pipeline's ids at or beyond the new chunk count are leftovers");
        _queries.Should().ContainSingle().Which.Filter.Should().Be(
            $"tenantId eq '{Tenant}' and speFileId eq '{SpeFileId}' and chunkIndex ge 3",
            "the query is tenant-scoped, file-scoped and can never match the new chunks 0..2");
        _deployment.Verify(d => d.GetSearchClientAsync(Tenant, RoutedIndex, It.IsAny<CancellationToken>()), Times.Once,
            "the trim must reach the per-record index the new chunks were written to");
        _deployment.Verify(d => d.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "the tenant-default index is not where this document's chunks are");
    }

    [Fact]
    public async Task DeleteChunksBeyondCount_WithoutIndexName_UsesTheTenantDefaultIndex()
    {
        _matchingRows = new List<KnowledgeDocument> { Chunk($"{SpeFileId}_1", 1) };

        var deleted = await CreateService().DeleteChunksBeyondCountAsync(Tenant, SpeFileId, 1, searchIndexName: null);

        deleted.Should().Be(1);
        _deployment.Verify(d => d.GetSearchClientAsync(Tenant, It.IsAny<CancellationToken>()), Times.Once);
        _deployment.Verify(
            d => d.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteChunksBeyondCount_WhenTheNewVersionIsNotShorter_DeletesNothing()
    {
        _matchingRows = new List<KnowledgeDocument>();

        var deleted = await CreateService().DeleteChunksBeyondCountAsync(Tenant, SpeFileId, 4, RoutedIndex);

        deleted.Should().Be(0);
        _deleteBatches.Should().BeEmpty("there is no tail, so no delete is issued at all");
    }

    [Fact]
    public async Task DeleteChunksBeyondCount_KeepCountBelowOne_RefusesWithoutTouchingTheIndex()
    {
        // Owner rule: the document must never be left without chunks. The facade cannot be asked to remove them all.
        var act = () => CreateService().DeleteChunksBeyondCountAsync(Tenant, SpeFileId, 0, RoutedIndex);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _queries.Should().BeEmpty();
        _deleteBatches.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteChunksBeyondCount_WhenALeftoverIsRejected_ThrowsSoTheJobRetries()
    {
        _matchingRows = new List<KnowledgeDocument> { Chunk($"{SpeFileId}_2", 2), Chunk($"{SpeFileId}_3", 3) };
        _deleteSucceeds = key => key != $"{SpeFileId}_3";

        var act = () => CreateService().DeleteChunksBeyondCountAsync(Tenant, SpeFileId, 2, RoutedIndex);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("1 of 2 leftover chunk(s) could not be deleted*");
    }
}
