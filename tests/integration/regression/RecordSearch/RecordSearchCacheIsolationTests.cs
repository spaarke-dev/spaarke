using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Sprk.Bff.Api.Services.RecordMatching;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression.RecordSearch;

/// <summary>
/// spaarkeai-mcp-bff-api-r2 task 009 (adversarial-review finding F1): the record-search result cache must
/// not let one request answer for another that would have been refused or answered differently.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect.</b> The cache lookup ran BEFORE the explicit-index allow-list check, and the cache id
/// omitted <c>SearchIndexName</c> and <c>PreferKeywordRanking</c> and joined list values with <c>,</c>/<c>|</c>.
/// So user A caching "acme" on the default index let user B in the same tenant post the same body with
/// <c>searchIndexName:"not-allowed"</c> and receive 200 cached results instead of 400 <c>INDEX_NOT_ALLOWED</c>;
/// and <c>["a","b"]</c> collided with <c>["a,b"]</c>.
/// </para>
/// <para>
/// <b>Observation point.</b> The real service runs against a dictionary-backed <see cref="ITenantCache"/>
/// (so a real hit can occur), a deployment resolver double that enforces an allow-list the way
/// <c>KnowledgeDeploymentService</c> does (throws <c>INDEX_NOT_ALLOWED</c>), and a recording search client.
/// </para>
/// </remarks>
[Trait("category", "security")]
public sealed class RecordSearchCacheIsolationTests
{
    private const string TenantId = "aaaaaaaa-0009-0009-0009-000000000009";
    private const string DefaultIndex = "spaarke-records-index";
    private const string AllowedIndex = "allowed-index";
    private const string NotAllowedIndex = "not-allowed";

    private readonly Mock<SearchClient> _defaultClient = new();
    private readonly Mock<SearchClient> _allowedClient = new();
    private readonly Mock<IOpenAiClient> _openAi = new();
    private readonly Mock<IEmbeddingCache> _embeddingCache = new();
    private readonly Mock<ITenantCache> _tenantCache = new();
    private readonly Mock<IKnowledgeDeploymentService> _deployment = new();

    private readonly ConcurrentDictionary<string, RecordSearchResponse> _cacheStore = new();
    private readonly List<string> _writtenCacheIds = [];

    public RecordSearchCacheIsolationTests()
    {
        // A real content hash: distinct inputs must give distinct ids, which a constant stub would hide.
        _embeddingCache
            .Setup(c => c.ComputeContentHash(It.IsAny<string>()))
            .Returns<string>(s => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(s))));
        _embeddingCache
            .Setup(c => c.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);
        _openAi
            .Setup(c => c.GenerateEmbeddingAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f]));

        _tenantCache
            .Setup(c => c.GetAsync<RecordSearchResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, string, int, string, CancellationToken>((tenant, resource, id, version, _, _) =>
                Task.FromResult(_cacheStore.TryGetValue($"{tenant}|{resource}|{id}|{version}", out var hit) ? hit : null));
        _tenantCache
            .Setup(c => c.SetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<RecordSearchResponse>(), It.IsAny<TimeSpan?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, string, int, RecordSearchResponse, TimeSpan?, string, CancellationToken>(
                (tenant, resource, id, version, value, _, _, _) =>
                {
                    _cacheStore[$"{tenant}|{resource}|{id}|{version}"] = value;
                    _writtenCacheIds.Add(id);
                    return Task.CompletedTask;
                });

        ConfigureSearchClient(_defaultClient);
        ConfigureSearchClient(_allowedClient);

        // The allow-list, as KnowledgeDeploymentService enforces it: allowed → a client; anything else → 400.
        _deployment
            .Setup(d => d.GetSearchClientAsync(It.IsAny<string>(), AllowedIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_allowedClient.Object);
        _deployment
            .Setup(d => d.GetSearchClientAsync(It.IsAny<string>(), NotAllowedIndex, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SdapProblemException(
                code: "INDEX_NOT_ALLOWED",
                title: "AI Search index not allowed",
                detail: "not in the allow-list",
                statusCode: 400));
    }

    [Fact]
    public async Task SearchAsync_NotAllowedIndexAfterCachedDefault_IsRefusedNotServedFromCache()
    {
        var service = CreateService();

        await service.SearchAsync(Request());
        _writtenCacheIds.Should().ContainSingle("the first (default-index) search populates the cache");

        // User B, same tenant, same body, but an index that is not on the allow-list.
        var act = () => service.SearchAsync(Request(searchIndexName: NotAllowedIndex));

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("INDEX_NOT_ALLOWED",
                "the allow-list is checked before the cache is consulted, so a cached answer for another "
                + "index can never stand in for the refusal");
    }

    [Fact]
    public async Task SearchAsync_ExplicitIndexMatchingNothingCached_RunsItsOwnSearchOnThatIndex()
    {
        var service = CreateService();

        await service.SearchAsync(Request());
        await service.SearchAsync(Request(searchIndexName: AllowedIndex));

        _defaultClient.Verify(c => c.SearchAsync<SearchIndexDocument>(
            It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        _allowedClient.Verify(c => c.SearchAsync<SearchIndexDocument>(
            It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()), Times.Once,
            "the default-index entry must not answer an explicit-index request");
    }

    [Fact]
    public async Task SearchAsync_RepeatOfTheSameRequest_IsStillServedFromCache()
    {
        var service = CreateService();

        await service.SearchAsync(Request(searchIndexName: AllowedIndex));
        await service.SearchAsync(Request(searchIndexName: AllowedIndex));

        _allowedClient.Verify(c => c.SearchAsync<SearchIndexDocument>(
            It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()), Times.Once,
            "tightening the id must not turn the cache off for identical requests");
    }

    public static TheoryData<string, RecordSearchRequest, RecordSearchRequest> DistinctRequests()
    {
        var data = new TheoryData<string, RecordSearchRequest, RecordSearchRequest>
        {
            { "different index", Request(), Request(searchIndexName: AllowedIndex) },
            { "ranking preference", Request(), Request(preferKeywordRanking: true) },
            {
                "organizations list vs one comma-joined value",
                Request(filters: new RecordSearchFilters { Organizations = ["a", "b"] }),
                Request(filters: new RecordSearchFilters { Organizations = ["a,b"] })
            },
            {
                "people list vs one pipe-joined value",
                Request(filters: new RecordSearchFilters { Organizations = ["a"], People = ["b"] }),
                Request(filters: new RecordSearchFilters { Organizations = ["a|ppl:b"] })
            },
            {
                "record types list vs one comma-joined value",
                Request(recordTypes: [RecordEntityType.Matter, RecordEntityType.Project]),
                Request(recordTypes: ["sprk_matter,sprk_project"])
            },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(DistinctRequests))]
    public async Task SearchAsync_RequestsThatDifferInScope_GetDifferentCacheIds(
        string _, RecordSearchRequest first, RecordSearchRequest second)
    {
        var service = CreateService();

        await service.SearchAsync(first);
        await service.SearchAsync(second);

        _writtenCacheIds.Should().HaveCount(2, "the second request must miss the first one's cache entry");
        _writtenCacheIds[0].Should().NotBe(_writtenCacheIds[1]);
    }

    [Fact]
    public async Task SearchAsync_EquivalentRequests_ShareACacheId()
    {
        var service = CreateService();

        await service.SearchAsync(Request(
            searchIndexName: AllowedIndex,
            filters: new RecordSearchFilters { Organizations = ["a", "b"] }));
        await service.SearchAsync(Request(
            searchIndexName: AllowedIndex,
            filters: new RecordSearchFilters { Organizations = ["b", "a"] }));

        _writtenCacheIds.Should().ContainSingle(
            "list order and an unchanged index name are not part of a request's identity");
    }

    private static RecordSearchRequest Request(
        string? searchIndexName = null,
        bool preferKeywordRanking = false,
        RecordSearchFilters? filters = null,
        IReadOnlyList<string>? recordTypes = null) => new()
        {
            Query = "acme",
            RecordTypes = recordTypes ?? [RecordEntityType.Matter],
            Filters = filters,
            Options = new RecordSearchOptions
            {
                HybridMode = RecordHybridSearchMode.KeywordOnly,
                PreferKeywordRanking = preferKeywordRanking,
            },
            SearchIndexName = searchIndexName,
        };

    private static void ConfigureSearchClient(Mock<SearchClient> client) =>
        client
            .Setup(c => c.SearchAsync<SearchIndexDocument>(
                It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(
                SearchModelFactory.SearchResults<SearchIndexDocument>(
                    values: [], totalCount: 0, facets: null, coverage: null, rawResponse: null!),
                null!));

    private RecordSearchService CreateService()
    {
        var indexClient = new Mock<SearchIndexClient>();
        indexClient.Setup(c => c.GetSearchClient(DefaultIndex)).Returns(_defaultClient.Object);

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", TenantId)], "Test")),
            },
        };

        return new RecordSearchService(
            indexClient.Object,
            _openAi.Object,
            _embeddingCache.Object,
            _tenantCache.Object,
            Options.Create(new DocumentIntelligenceOptions { AiSearchIndexName = DefaultIndex }),
            NullLogger<RecordSearchService>.Instance,
            _deployment.Object,
            new ConfigurationBuilder().Build(),
            accessor);
    }
}
