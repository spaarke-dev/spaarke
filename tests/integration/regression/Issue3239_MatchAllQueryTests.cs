// Regression anchor for GitHub issue #3239. POST /api/ai/search/records with query "*" answered 500 "Record Search Failed": the service
// embedded the "*", sent it with a query vector and asked for highlights, and Azure AI Search answers 400 "Parameter 'highlight' is not
// supported for search requests with pure vector queries" ("*" with a vector query IS a pure vector query). A "*" query is now a match-all:
// a plain keyword "*" search, no embedding, no highlights, no semantic ranking, and the filters still apply. Document search
// (/api/ai/search) had the same shape and gets the same treatment, plus the vector-only mode of both, which sends "*" the same way.
//
// The Azure rule is reproduced at the SearchClient double (EnforceAzureRules), so a request Azure would reject fails the test the way it
// failed production, instead of the test asserting on option details alone.

using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.RecordMatching;
using Xunit;
using AppSearchOptions = Sprk.Bff.Api.Models.Ai.SemanticSearch.SearchOptions;
using AzureSearchOptions = Azure.Search.Documents.SearchOptions;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public sealed class Issue3239_MatchAllQueryTests
{
    private const string Tenant = "a221a95e-6abc-4434-aecc-e48338a1b2f2";
    private const string IndexName = "spaarke-records-index";
    private static readonly ReadOnlyMemory<float> Embedding = new float[] { 0.1f, 0.2f, 0.3f };

    private readonly Mock<IOpenAiClient> _openAi = new();

    public Issue3239_MatchAllQueryTests()
    {
        _openAi
            .Setup(o => o.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Embedding);
    }

    /// <summary>What Azure AI Search does with a request: the highlight option is refused on a pure vector query ("*" plus a vector).</summary>
    private static void EnforceAzureRules(string searchText, AzureSearchOptions options)
    {
        var pureVector = searchText == "*" && options.VectorSearch?.Queries.Count > 0;
        if (pureVector && options.HighlightFields.Count > 0)
        {
            throw new RequestFailedException(400, "Parameter 'highlight' is not supported for search requests with pure vector queries.");
        }
    }

    // ── record search ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record RecordCall(string SearchText, AzureSearchOptions Options);

    private (RecordSearchService Service, List<RecordCall> Calls) CreateRecordSearch()
    {
        var calls = new List<RecordCall>();
        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.SearchAsync<SearchIndexDocument>(It.IsAny<string>(), It.IsAny<AzureSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, AzureSearchOptions options, CancellationToken _) =>
            {
                EnforceAzureRules(text, options);
                calls.Add(new RecordCall(text, options));
                return Response.FromValue(
                    SearchModelFactory.SearchResults(new List<SearchResult<SearchIndexDocument>>(), 0, null, null, null!), null!);
            });

        var indexClient = new Mock<SearchIndexClient>();
        indexClient.Setup(i => i.GetSearchClient(IndexName)).Returns(searchClient.Object);

        var embeddingCache = new Mock<IEmbeddingCache>();
        embeddingCache.Setup(e => e.ComputeContentHash(It.IsAny<string>())).Returns("hash");

        var service = new RecordSearchService(
            indexClient.Object,
            _openAi.Object,
            embeddingCache.Object,
            new Mock<ITenantCache>().Object,
            Options.Create(new DocumentIntelligenceOptions { AiSearchIndexName = IndexName }),
            NullLogger<RecordSearchService>.Instance,
            new Mock<IKnowledgeDeploymentService>().Object,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:TenantId"] = Tenant }).Build(),
            new Mock<IHttpContextAccessor>().Object);

        return (service, calls);
    }

    private static RecordSearchRequest RecordRequest(string query, string? hybridMode = null, IReadOnlyList<string>? matterTypes = null) => new()
    {
        Query = query,
        RecordTypes = [RecordEntityType.Matter, RecordEntityType.Project],
        Filters = matterTypes is null ? null : new RecordSearchFilters { MatterTypes = matterTypes.ToList() },
        Options = hybridMode is null ? null : new RecordSearchOptions { HybridMode = hybridMode },
    };

    [Theory]
    [InlineData("*")]
    [InlineData("  *  ")]
    public async Task RecordSearch_MatchAllQuery_RunsAsAPlainKeywordStarSearchInsteadOfFailing(string query)
    {
        var (service, calls) = CreateRecordSearch();

        var response = await service.SearchAsync(RecordRequest(query));

        var call = calls.Should().ContainSingle().Subject;
        call.SearchText.Should().Be("*");
        call.Options.VectorSearch.Should().BeNull("a match-all has no text to embed");
        call.Options.HighlightFields.Should().BeEmpty("Azure refuses highlights without query text");
        call.Options.SemanticSearch.Should().BeNull();
        call.Options.QueryType.Should().NotBe(SearchQueryType.Semantic);
        _openAi.Verify(
            o => o.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        response.Metadata.HybridMode.Should().Be(RecordHybridSearchMode.KeywordOnly);
    }

    [Fact]
    public async Task RecordSearch_MatchAllQuery_StillAppliesTheTenantRecordTypeAndMatterTypeFilters()
    {
        var (service, calls) = CreateRecordSearch();
        var matterType = "3239b000-0000-4000-8000-0000000000a1";

        await service.SearchAsync(RecordRequest("*", matterTypes: [matterType]));

        var filter = calls.Should().ContainSingle().Subject.Options.Filter;
        filter.Should().Contain($"tenantId eq '{Tenant}'")
            .And.Contain("search.in(recordType,")
            .And.Contain(matterType);
    }

    [Fact]
    public async Task RecordSearch_VectorOnlyMode_DoesNotAskForHighlightsOnAPureVectorQuery()
    {
        var (service, calls) = CreateRecordSearch();

        await service.SearchAsync(RecordRequest("patent", RecordHybridSearchMode.VectorOnly));

        var call = calls.Should().ContainSingle().Subject;
        call.SearchText.Should().Be("*");
        call.Options.VectorSearch.Should().NotBeNull();
        call.Options.HighlightFields.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordSearch_OrdinaryQuery_StillRunsHybridWithHighlightsAndSemanticRanking()
    {
        var (service, calls) = CreateRecordSearch();

        await service.SearchAsync(RecordRequest("patent"));

        var call = calls.Should().ContainSingle().Subject;
        call.SearchText.Should().Be("patent");
        call.Options.VectorSearch!.Queries.Should().ContainSingle();
        call.Options.HighlightFields.Should().BeEquivalentTo("recordName", "recordDescription");
        call.Options.QueryType.Should().Be(SearchQueryType.Semantic);
    }

    // ── document search ─────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record DocumentCall(string SearchText, AzureSearchOptions Options);

    private (SemanticSearchService Service, List<DocumentCall> Calls) CreateDocumentSearch(SearchResult<KnowledgeDocument>? hit = null)
    {
        var calls = new List<DocumentCall>();
        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.SearchAsync<KnowledgeDocument>(It.IsAny<string>(), It.IsAny<AzureSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, AzureSearchOptions options, CancellationToken _) =>
            {
                EnforceAzureRules(text, options);
                calls.Add(new DocumentCall(text, options));
                var rows = hit is null ? new List<SearchResult<KnowledgeDocument>>() : [hit];
                return Response.FromValue(SearchModelFactory.SearchResults(rows, rows.Count, null, null, null!), null!);
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

        var service = new SemanticSearchService(
            deployment.Object,
            _openAi.Object,
            Mock.Of<IEmbeddingCache>(),
            preprocessor.Object,
            postprocessor.Object,
            Mock.Of<IDataverseService>(),
            NullLogger<SemanticSearchService>.Instance);

        return (service, calls);
    }

    private static SemanticSearchRequest DocumentRequest(string query, string? hybridMode = null, double? minScore = null) => new()
    {
        Query = query,
        Scope = SearchScope.All,
        Options = hybridMode is null && minScore is null
            ? null
            : new AppSearchOptions { HybridMode = hybridMode ?? HybridSearchMode.Rrf, MinScore = minScore ?? 0.0 },
    };

    [Fact]
    public async Task DocumentSearch_MatchAllQuery_RunsAsAKeywordStarSearchWithoutEmbeddingTheStar()
    {
        var (service, calls) = CreateDocumentSearch();

        await service.SearchAsync(DocumentRequest(" * "), Tenant);

        var call = calls.Should().ContainSingle().Subject;
        call.SearchText.Should().Be("*");
        call.Options.VectorSearch.Should().BeNull("a match-all has no text to embed");
        _openAi.Verify(
            o => o.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DocumentSearch_VectorOnlyMode_DoesNotAskForHighlightsOnAPureVectorQuery()
    {
        var (service, calls) = CreateDocumentSearch();

        await service.SearchAsync(DocumentRequest("indemnity", HybridSearchMode.VectorOnly), Tenant);

        var call = calls.Should().ContainSingle().Subject;
        call.Options.VectorSearch.Should().NotBeNull();
        call.Options.HighlightFields.Should().BeEmpty();
    }

    [Fact]
    public async Task DocumentSearch_OrdinaryQuery_StillRunsHybridWithHighlights()
    {
        var (service, calls) = CreateDocumentSearch();

        await service.SearchAsync(DocumentRequest("indemnity"), Tenant);

        var call = calls.Should().ContainSingle().Subject;
        call.SearchText.Should().Be("indemnity");
        call.Options.VectorSearch!.Queries.Should().ContainSingle();
        call.Options.HighlightFields.Should().ContainSingle().Which.Should().Be("content");
    }

    [Fact]
    public async Task DocumentSearch_MatchAllQuery_IsNotCutByTheMinScoreThresholdThatAppliesToRealQueries()
    {
        // A match-all has no relevance to threshold (its scores are the low RRF/BM25 values of an unranked list), exactly like the
        // empty query; a real query is cut by the same threshold.
        var chunk = new KnowledgeDocument { Id = "c1", DocumentId = "doc-3239", TenantId = Tenant, SpeFileId = "f1", FileName = "a.pdf", Content = "x" };
        var hit = SearchModelFactory.SearchResult(chunk, score: 0.01, highlights: null);

        var (matchAllService, _) = CreateDocumentSearch(hit);
        var matchAll = await matchAllService.SearchAsync(DocumentRequest("*", minScore: 0.5), Tenant);

        var (queryService, _) = CreateDocumentSearch(hit);
        var real = await queryService.SearchAsync(DocumentRequest("indemnity", minScore: 0.5), Tenant);

        matchAll.Results.Should().ContainSingle();
        real.Results.Should().BeEmpty();
    }
}
