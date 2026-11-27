// Follow-up to GitHub issue #3239 (record search and document search answered 500 for query "*"). Three more search services build the
// same request: RAG search (POST /api/ai/rag/search, caller-chosen options), invoice search (GET /api/finance/invoices/search) and
// reference retrieval (action grounding). Each sent the "*" to the embedder and then to the index as star + vector query + highlights,
// which Azure AI Search refuses with a 400 ("Parameter 'highlight' is not supported for search requests with pure vector queries").
// RAG search also asked for highlights on its vector-only mode (UseKeywordSearch=false), which it runs as "*" + vector.
//
// The wire is the assertion: each service is real, the search client is the real Azure SDK client, and only its HTTP transport is a
// hand-rolled fake that records the search request body and answers the way the index does (400 for star + vector + highlight).

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core.Pipeline;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public sealed class Issue3239_MatchAllOtherSearchServicesTests
{
    private const string Tenant = "a221a95e-6abc-4434-aecc-e48338a1b2f2";
    private static readonly ReadOnlyMemory<float> Embedding = new float[] { 0.1f, 0.2f, 0.3f };

    private readonly SearchWire _wire = new();
    private readonly Mock<IOpenAiClient> _openAi = new();
    private readonly Mock<IInvoiceAi> _invoiceAi = new();

    public Issue3239_MatchAllOtherSearchServicesTests()
    {
        _openAi
            .Setup(o => o.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Embedding);
        _invoiceAi
            .Setup(o => o.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Embedding);
    }

    private SearchClient CreateSearchClient(string indexName) => new(
        new Uri("https://test-search.search.windows.net"),
        indexName,
        new Azure.AzureKeyCredential(Guid.NewGuid().ToString("N")),
        new SearchClientOptions { Transport = new HttpClientTransport(new HttpClient(_wire)), Retry = { MaxRetries = 0 } });

    private Mock<SearchIndexClient> CreateIndexClient(string indexName)
    {
        var indexClient = new Mock<SearchIndexClient>(MockBehavior.Loose);
        indexClient.Setup(i => i.GetSearchClient(indexName)).Returns(CreateSearchClient(indexName));
        return indexClient;
    }

    private void EmbeddingWasNeverRequested()
    {
        _openAi.Verify(
            o => o.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _invoiceAi.Verify(
            o => o.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── RAG search (POST /api/ai/rag/search) ────────────────────────────────────────────────────────────────────────────

    private RagService CreateRag()
    {
        var deployment = new Mock<IKnowledgeDeploymentService>();
        deployment.Setup(d => d.GetSearchClientAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(CreateSearchClient("spaarke-files-index"));

        return new RagService(
            deployment.Object,
            _openAi.Object,
            Mock.Of<IEmbeddingCache>(),
            Mock.Of<Sprk.Bff.Api.Services.Ai.Security.IPrivilegeGroupResolver>(),
            Options.Create(new AnalysisOptions()),
            new Mock<SearchIndexClient>(MockBehavior.Loose).Object,
            Options.Create(new AiSearchOptions { Endpoint = "https://test-search.search.windows.net", KnowledgeIndexName = "spaarke-files-index" }),
            NullLogger<RagService>.Instance);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("  *  ")]
    public async Task RagSearch_MatchAllQuery_RunsAsAPlainKeywordStarSearchInsteadOfFailing(string query)
    {
        var rag = CreateRag();

        var response = await rag.SearchAsync(query, new RagSearchOptions { TenantId = Tenant });

        response.Results.Should().BeEmpty();
        var body = _wire.Searches.Should().ContainSingle().Subject;
        body.GetProperty("search").GetString().Should().Be("*");
        body.TryGetProperty("vectorQueries", out _).Should().BeFalse("a match-all has no text to embed");
        body.TryGetProperty("highlight", out _).Should().BeFalse("the index refuses highlights without query text");
        body.GetProperty("filter").GetString().Should().Contain($"tenantId eq '{Tenant}'");
        EmbeddingWasNeverRequested();
    }

    [Fact]
    public async Task RagSearch_VectorOnlyMode_DoesNotAskForHighlightsOnAPureVectorQuery()
    {
        var rag = CreateRag();

        await rag.SearchAsync("indemnification clause", new RagSearchOptions { TenantId = Tenant, UseVectorSearch = true, UseKeywordSearch = false });

        var body = _wire.Searches.Should().ContainSingle().Subject;
        body.TryGetProperty("vectorQueries", out _).Should().BeTrue();
        body.TryGetProperty("highlight", out _).Should().BeFalse();
    }

    [Fact]
    public async Task RagSearch_OrdinaryQuery_StillRunsHybridWithHighlightsAndSemanticRanking()
    {
        var rag = CreateRag();

        await rag.SearchAsync("indemnification clause", new RagSearchOptions { TenantId = Tenant });

        var body = _wire.Searches.Should().ContainSingle().Subject;
        body.GetProperty("search").GetString().Should().Be("indemnification clause");
        body.TryGetProperty("vectorQueries", out _).Should().BeTrue();
        body.GetProperty("highlight").GetString().Should().Be("content");
        body.GetProperty("queryType").GetString().Should().Be("semantic");
    }

    // ── invoice search (GET /api/finance/invoices/search) ───────────────────────────────────────────────────────────────

    private InvoiceSearchService CreateInvoiceSearch() => new(
        CreateIndexClient("spaarke-invoices-index").Object,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:TenantId"] = Tenant }).Build(),
        NullLogger<InvoiceSearchService>.Instance,
        _invoiceAi.Object);

    [Fact]
    public async Task InvoiceSearch_MatchAllQuery_RunsAsAKeywordStarSearchInsteadOfFailing()
    {
        var matterId = Guid.Parse("3239b000-0000-4000-8000-000000000001");
        var service = CreateInvoiceSearch();

        var response = await service.SearchAsync("*", matterId);

        response.Results.Should().BeEmpty();
        var body = _wire.Searches.Should().ContainSingle().Subject;
        body.GetProperty("search").GetString().Should().Be("*");
        body.TryGetProperty("vectorQueries", out _).Should().BeFalse();
        body.TryGetProperty("highlight", out _).Should().BeFalse();
        body.TryGetProperty("queryType", out var queryType).Should().Be(false, $"no semantic ranking on a match-all, got {queryType}");
        // The tenant and matter scoping still applies to the match-all.
        body.GetProperty("filter").GetString().Should().Contain($"tenantId eq '{Tenant}'").And.Contain($"matterId eq '{matterId}'");
        EmbeddingWasNeverRequested();
    }

    [Fact]
    public async Task InvoiceSearch_OrdinaryQuery_StillRunsHybridWithHighlightsAndSemanticRanking()
    {
        var service = CreateInvoiceSearch();

        await service.SearchAsync("late fee", Guid.NewGuid());

        var body = _wire.Searches.Should().ContainSingle().Subject;
        body.GetProperty("search").GetString().Should().Be("late fee");
        body.TryGetProperty("vectorQueries", out _).Should().BeTrue();
        body.GetProperty("highlight").GetString().Should().Be("content");
        body.GetProperty("queryType").GetString().Should().Be("semantic");
    }

    // ── reference retrieval (action grounding) ──────────────────────────────────────────────────────────────────────────

    private ReferenceRetrievalService CreateReferences()
    {
        var aiSearch = new AiSearchOptions { Endpoint = "https://test-search.search.windows.net" };
        var tenantCache = new Mock<ITenantCache>();
        tenantCache
            .Setup(c => c.GetAsync<ReferenceSearchResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceSearchResponse?)null);

        return new ReferenceRetrievalService(
            CreateIndexClient(aiSearch.RagReferencesIndexName).Object,
            _openAi.Object,
            Mock.Of<IEmbeddingCache>(),
            tenantCache.Object,
            Options.Create(aiSearch),
            NullLogger<ReferenceRetrievalService>.Instance);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("  *  ")]
    public async Task ReferenceSearch_MatchAllQuery_ReturnsNoReferencesAndNeverReachesTheIndex(string query)
    {
        // Grounding with arbitrary tenant-wide chunks is worse than none: the caller runs ungrounded on an empty result.
        var service = CreateReferences();

        var response = await service.SearchReferencesAsync(query, new ReferenceSearchOptions { TenantId = Tenant });

        response.Results.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
        _wire.Searches.Should().BeEmpty("a match-all sends no search request at all");
        EmbeddingWasNeverRequested();
    }

    [Fact]
    public async Task ReferenceSearch_OrdinaryQuery_StillRunsHybridWithHighlightsAndSemanticRanking()
    {
        var service = CreateReferences();

        await service.SearchReferencesAsync("limitation of liability", new ReferenceSearchOptions { TenantId = Tenant });

        var body = _wire.Searches.Should().ContainSingle().Subject;
        body.GetProperty("search").GetString().Should().Be("limitation of liability");
        body.TryGetProperty("vectorQueries", out _).Should().BeTrue();
        body.GetProperty("highlight").GetString().Should().Be("content");
        body.GetProperty("queryType").GetString().Should().Be("semantic");
    }

    /// <summary>
    /// The index's wire: records the JSON body of every search request, answers 400 the way the index does for highlights on a
    /// pure vector query ("*" plus vector queries) and otherwise answers an empty result page.
    /// </summary>
    private sealed class SearchWire : HttpMessageHandler
    {
        public List<JsonElement> Searches { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var text = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var parsed = JsonDocument.Parse(text);
            var body = parsed.RootElement.Clone();
            Searches.Add(body);

            var pureVector = body.TryGetProperty("search", out var search) && search.GetString() == "*"
                && body.TryGetProperty("vectorQueries", out _);
            if (pureVector && body.TryGetProperty("highlight", out _))
            {
                const string error = "{\"error\":{\"code\":\"\",\"message\":\"Parameter 'highlight' is not supported for search requests with pure vector queries.\"}}";
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(error, Encoding.UTF8, "application/json") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"@odata.count\":0,\"value\":[]}", Encoding.UTF8, "application/json"),
            };
        }
    }
}
