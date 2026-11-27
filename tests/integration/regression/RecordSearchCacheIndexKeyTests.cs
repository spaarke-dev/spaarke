// Regression anchor: record search cached its results under a key that left the target index out, and read the cache BEFORE the index was
// resolved and validated. The same query against another index (or the standard one) was therefore answered with the first index's
// cached results for the whole TTL, and a request naming an index the caller may not use could be served another index's results
// from the cache. The effective index is now part of the key; a named index and the standard index never share one.
//
// The cache is a Moq double of ITenantCache backed by a dictionary keyed on exactly what the service passes (tenant, resource, id,
// version), so a key collision shows up the way it does against Redis: as a served hit.

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
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Sprk.Bff.Api.Services.RecordMatching;
using Xunit;
using AzureSearchOptions = Azure.Search.Documents.SearchOptions;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public sealed class RecordSearchCacheIndexKeyTests
{
    private const string Tenant = "a221a95e-6abc-4434-aecc-e48338a1b2f2";
    private const string StandardIndex = "spaarke-records-index";

    private readonly Dictionary<string, RecordSearchResponse> _cache = new();
    private readonly Dictionary<string, int> _searchesPerIndex = new();

    private SearchClient ClientFor(string indexName)
    {
        var client = new Mock<SearchClient>();
        client
            .Setup(c => c.SearchAsync<SearchIndexDocument>(It.IsAny<string>(), It.IsAny<AzureSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                _searchesPerIndex[indexName] = _searchesPerIndex.GetValueOrDefault(indexName) + 1;
                return Response.FromValue(
                    SearchModelFactory.SearchResults(new List<SearchResult<SearchIndexDocument>>(), 0, null, null, null!), null!);
            });
        return client.Object;
    }

    private RecordSearchService CreateService()
    {
        var indexClient = new Mock<SearchIndexClient>();
        indexClient.Setup(i => i.GetSearchClient(StandardIndex)).Returns(ClientFor(StandardIndex));

        var deployment = new Mock<IKnowledgeDeploymentService>();
        deployment
            .Setup(d => d.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string? name, CancellationToken _) => ClientFor(name!));

        var embeddingCache = new Mock<IEmbeddingCache>();
        embeddingCache.Setup(e => e.ComputeContentHash(It.IsAny<string>())).Returns((string text) => "h(" + text + ")");

        var cache = new Mock<ITenantCache>();
        cache
            .Setup(c => c.GetAsync<RecordSearchResponse>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenant, string resource, string id, int version, string _, CancellationToken _) =>
                _cache.GetValueOrDefault($"{tenant}|{resource}|{id}|{version}"));
        cache
            .Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<RecordSearchResponse>(), It.IsAny<TimeSpan?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string tenant, string resource, string id, int version, RecordSearchResponse value, TimeSpan? _, string _, CancellationToken _) =>
                _cache[$"{tenant}|{resource}|{id}|{version}"] = value)
            .Returns(Task.CompletedTask);

        return new RecordSearchService(
            indexClient.Object,
            new Mock<IOpenAiClient>().Object,
            embeddingCache.Object,
            cache.Object,
            Options.Create(new DocumentIntelligenceOptions { AiSearchIndexName = StandardIndex }),
            NullLogger<RecordSearchService>.Instance,
            deployment.Object,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:TenantId"] = Tenant }).Build(),
            new Mock<IHttpContextAccessor>().Object);
    }

    private static RecordSearchRequest Request(string? searchIndexName) => new()
    {
        Query = "*",
        RecordTypes = [RecordEntityType.Matter],
        SearchIndexName = searchIndexName,
    };

    [Fact]
    public async Task TheSameQueryAgainstAnotherIndex_IsNotServedTheFirstIndexsCachedResults()
    {
        var service = CreateService();

        await service.SearchAsync(Request("records-index-a"));
        await service.SearchAsync(Request("records-index-b"));

        _searchesPerIndex.GetValueOrDefault("records-index-a").Should().Be(1);
        _searchesPerIndex.GetValueOrDefault("records-index-b").Should().Be(1, "index B must be searched, not answered from index A's cache entry");
    }

    [Fact]
    public async Task AQueryAgainstANamedIndexAndTheStandardIndex_DoNotShareACacheEntry()
    {
        var service = CreateService();

        await service.SearchAsync(Request(searchIndexName: null));
        await service.SearchAsync(Request("records-index-a"));
        // And in the other order: the named index first, the standard index second (here the standard one is named explicitly).
        await service.SearchAsync(Request(StandardIndex));

        _searchesPerIndex.GetValueOrDefault(StandardIndex).Should().Be(2, "the unnamed request and the request that names the standard index are keyed apart");
        _searchesPerIndex.GetValueOrDefault("records-index-a").Should().Be(1);
    }

    [Fact]
    public async Task TheSameQueryAgainstTheSameIndex_IsStillServedFromTheCache()
    {
        var service = CreateService();

        await service.SearchAsync(Request("records-index-a"));
        await service.SearchAsync(Request("records-index-a"));
        await service.SearchAsync(Request(searchIndexName: null));
        await service.SearchAsync(Request(searchIndexName: "  "));

        _searchesPerIndex.GetValueOrDefault("records-index-a").Should().Be(1, "the second identical request is a cache hit");
        _searchesPerIndex.GetValueOrDefault(StandardIndex).Should().Be(1, "a blank name is the standard index, keyed like no name");
    }
}
