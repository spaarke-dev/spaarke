// Regression anchor for GitHub issue #3306 (document search). POST /api/ai/search with query "*" (or an empty query) and scope=all
// returned few or no documents although the caller can open many. A document is stored as many chunks; with no query text to rank by,
// the index returns chunks in index order, so one long document's chunks filled the page, and the page collapsed to one or two documents
// (none, when the caller cannot open them). With no query text, the search now asks for one row per document (its first chunk), so the
// page size, offset and count mean documents.
//
// The index is reproduced at the SearchClient double: rows in index order, the filter's first-chunk clause honoured, and a ranked query
// spreading across documents as a relevance ranking would.

using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Xunit;
using AppSearchOptions = Sprk.Bff.Api.Models.Ai.SemanticSearch.SearchOptions;
using AzureSearchOptions = Azure.Search.Documents.SearchOptions;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public sealed class Issue3306_MatchAllDocumentSearchOneRowPerDocumentTests
{
    private const string Tenant = "a221a95e-6abc-4434-aecc-e48338a1b2f2";

    private readonly List<KnowledgeDocument> _index = [];
    private readonly List<AzureSearchOptions> _calls = [];

    public Issue3306_MatchAllDocumentSearchOneRowPerDocumentTests()
    {
        // Index order: one long document first (twelve chunks), then three short ones, then one written before chunk numbers.
        _index.AddRange(Chunks("doc-long", 12));
        _index.AddRange(Chunks("doc-b", 2));
        _index.AddRange(Chunks("doc-c", 2));
        _index.AddRange(Chunks("doc-d", 2));
        _index.Add(new KnowledgeDocument { Id = "legacy_0", DocumentId = "doc-legacy", TenantId = Tenant, SpeFileId = "f-legacy", FileName = "legacy.pdf", Content = "x" });
    }

    private static IEnumerable<KnowledgeDocument> Chunks(string documentId, int count) =>
        Enumerable.Range(0, count).Select(i => new KnowledgeDocument
        {
            Id = $"{documentId}_{i}",
            DocumentId = documentId,
            TenantId = Tenant,
            SpeFileId = $"f-{documentId}",
            FileName = $"{documentId}.pdf",
            Content = "x",
            ChunkIndex = i,
        });

    private IEnumerable<KnowledgeDocument> Matching(AzureSearchOptions options) =>
        options.Filter?.Contains(SearchFilterBuilder.FirstChunkClause, StringComparison.Ordinal) == true
            ? _index.Where(row => row.ChunkIndex is null or 0)
            : _index;

    private SemanticSearchService CreateService()
    {
        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.SearchAsync<KnowledgeDocument>(It.IsAny<string>(), It.IsAny<AzureSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, AzureSearchOptions options, CancellationToken _) =>
            {
                _calls.Add(options);
                var matching = Matching(options).ToList();
                // A ranked query spreads across documents (here: one chunk per document first); "*" is plain index order.
                var ordered = text == "*" ? matching : matching.OrderBy(row => row.ChunkIndex ?? 0).ToList();
                var page = ordered.Skip(options.Skip ?? 0).Take(options.Size ?? 50)
                    .Select(row => SearchModelFactory.SearchResult(row, 1.0, null)).ToList();
                return Response.FromValue(SearchModelFactory.SearchResults(page, matching.Count, null, null, null!), null!);
            });

        var deployment = new Mock<IKnowledgeDeploymentService>();
        deployment.Setup(d => d.GetSearchClientAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(searchClient.Object);

        var preprocessor = new Mock<IQueryPreprocessor>();
        preprocessor
            .Setup(p => p.ProcessAsync(It.IsAny<SemanticSearchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SemanticSearchRequest request, string _, CancellationToken _) => new QueryPreprocessorResult(request, request.Query, WasModified: false));
        var postprocessor = new Mock<IResultPostprocessor>();
        postprocessor
            .Setup(p => p.ProcessAsync(It.IsAny<SemanticSearchResponse>(), It.IsAny<SemanticSearchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SemanticSearchResponse response, SemanticSearchRequest _, string _, CancellationToken _) => new ResultPostprocessorResult(response, WasModified: false));

        var openAi = new Mock<IOpenAiClient>();
        openAi
            .Setup(o => o.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        return new SemanticSearchService(
            deployment.Object,
            openAi.Object,
            Mock.Of<IEmbeddingCache>(),
            preprocessor.Object,
            postprocessor.Object,
            Mock.Of<IDataverseService>(),
            NullLogger<SemanticSearchService>.Instance);
    }

    private static SemanticSearchRequest Request(string? query, int limit, int offset = 0) => new()
    {
        Query = query,
        Scope = SearchScope.All,
        Options = new AppSearchOptions { Limit = limit, Offset = offset, HybridMode = HybridSearchMode.Rrf },
    };

    [Theory]
    [InlineData("*")]
    [InlineData("")]
    [InlineData(null)]
    public async Task SearchAsync_WithNoQueryText_ReturnsAFullPageOfDistinctDocuments(string? query)
    {
        var response = await CreateService().SearchAsync(Request(query, limit: 3), Tenant);

        response.Results.Select(r => r.DocumentId).Should().Equal(["doc-long", "doc-b", "doc-c"],
            "a page of three means three documents, not three chunks of the first one");
    }

    [Fact]
    public async Task SearchAsync_WithNoQueryText_PagesByDocumentAndKeepsADocumentWrittenWithoutChunkNumbers()
    {
        var response = await CreateService().SearchAsync(Request("*", limit: 3, offset: 3), Tenant);

        response.Results.Select(r => r.DocumentId).Should().Equal(["doc-d", "doc-legacy"]);
    }

    [Fact]
    public async Task SearchAsync_WithQueryText_DoesNotNarrowToTheFirstChunk()
    {
        // A ranked query matches on any chunk; narrowing it to the first chunk would hide documents that match further in.
        await CreateService().SearchAsync(Request("indemnity", limit: 3), Tenant);

        _calls.Should().ContainSingle().Which.Filter.Should().NotContain(SearchFilterBuilder.FirstChunkClause);
    }

    [Fact]
    public async Task CountAsync_CountsDocumentsNotChunks()
    {
        var response = await CreateService().CountAsync(Request("*", limit: 3), Tenant);

        response.Count.Should().Be(5, "five documents, although the index holds nineteen chunks");
        _calls.Should().ContainSingle().Which.Filter.Should().StartWith($"(tenantId eq '{Tenant}'");
    }
}
