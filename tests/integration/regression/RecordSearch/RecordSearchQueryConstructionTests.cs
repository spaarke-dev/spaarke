using System.Security.Claims;
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
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Sprk.Bff.Api.Services.RecordMatching;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression.RecordSearch;

/// <summary>
/// spaarkeai-mcp-bff-api-r2 task 009 (spec FR-02, FR-15): <see cref="RecordSearchService"/> builds no
/// query language from the caller's free text. <c>request.Query</c> reaches Azure AI Search only as
/// <c>searchText</c> and the embedding input — never <see cref="SearchOptions.Filter"/> or any other
/// query-shaped option — and every filter literal it does build is escaped.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this matters now.</b> Task 022 exposes this service to Copilot as <c>spaarke_find_records</c>,
/// where the query text is written by a model from user and document content. Before this file, no test
/// asserted what reaches <c>SearchOptions.Filter</c>; the unit suite covers behaviour, and
/// <c>RecordSearchRowAuthorizationContractTests</c> covers row authorization.
/// </para>
/// <para>
/// <b>Observation point.</b> The real service runs; only its boundaries are doubled: the search client
/// (captures the outgoing <c>searchText</c> and <see cref="SearchOptions"/>), the embedding client and the
/// two Redis-backed caches. The filter is asserted by exact equality, so any fragment of the query text that
/// leaked into it — however it was spliced in — fails the test.
/// </para>
/// <para>
/// Seeding proof (task 009 notes): appending <c>request.Query</c> to the filter fails
/// <c>SearchAsync_HostileQuery_ReachesOnlySearchTextAndEmbedding</c>; making <c>EscapeODataValue</c>
/// return its input unchanged fails both escaping tests.
/// </para>
/// </remarks>
[Trait("category", "security")]
public sealed class RecordSearchQueryConstructionTests
{
    private const string TenantId = "aaaaaaaa-0009-0009-0009-000000000009";
    private const string IndexName = "spaarke-records-index";

    // Distinctive token carried by every hostile query, so a leak is unambiguous even where the query's
    // operators (eq, or, any) also appear legitimately in the filter.
    private const string Marker = "QX009INJ";

    private readonly Mock<SearchClient> _searchClient = new();
    private readonly Mock<IOpenAiClient> _openAi = new();
    private readonly Mock<IEmbeddingCache> _embeddingCache = new();
    private readonly Mock<ITenantCache> _tenantCache = new();
    private readonly Mock<IKnowledgeDeploymentService> _deployment = new(MockBehavior.Strict);

    private readonly List<string> _embeddingInputs = [];
    private string? _capturedSearchText;
    private SearchOptions? _capturedOptions;

    public RecordSearchQueryConstructionTests()
    {
        _embeddingCache.Setup(c => c.ComputeContentHash(It.IsAny<string>())).Returns("hash");
        _embeddingCache
            .Setup(c => c.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((text, _) => _embeddingInputs.Add(text))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);

        _openAi
            .Setup(c => c.GenerateEmbeddingAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, int?, CancellationToken>((text, _, _, _) => _embeddingInputs.Add(text))
            .ReturnsAsync(new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f]));

        _tenantCache
            .Setup(c => c.GetAsync<RecordSearchResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecordSearchResponse?)null);

        _searchClient
            .Setup(c => c.SearchAsync<SearchIndexDocument>(
                It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, SearchOptions, CancellationToken>((text, options, _) =>
            {
                _capturedSearchText = text;
                _capturedOptions = options;
            })
            .ReturnsAsync(Response.FromValue(
                SearchModelFactory.SearchResults<SearchIndexDocument>(
                    values: [], totalCount: 0, facets: null, coverage: null, rawResponse: null!),
                null!));
    }

    /// <summary>
    /// Hostile query texts: OData comparison and boolean operators, quote and parenthesis break-outs,
    /// FetchXML, a literal <c>$filter=</c>, a lambda <c>any(</c> and a <c>search.in</c> call — each crossed
    /// with every hybrid mode, because each mode takes a different branch for searchText and the embedding.
    /// </summary>
    public static TheoryData<string, string> HostileQueriesByMode()
    {
        string[] queries =
        [
            $"{Marker}' eq '{Marker}",
            $"{Marker}' or tenantId ne 'x",
            $"') or (tenantId ne '{Marker}') or ('",
            $"\"{Marker}\" and recordType eq 'sprk_invoice'",
            $"<fetch><entity name='sprk_matter'><filter><condition attribute='sprk_name' operator='like' value='%{Marker}%'/></filter></entity></fetch>",
            $"$filter=tenantId ne '{Marker}'",
            $"organizations/any(o: o ne '{Marker}')",
            $"search.in(recordType, '{Marker}', ',')",
        ];

        var data = new TheoryData<string, string>();
        foreach (var mode in RecordHybridSearchMode.ValidModes)
        {
            foreach (var query in queries)
            {
                data.Add(query, mode);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(HostileQueriesByMode))]
    public async Task SearchAsync_HostileQuery_ReachesOnlySearchTextAndEmbedding(string query, string mode)
    {
        var request = new RecordSearchRequest
        {
            Query = query,
            RecordTypes = [RecordEntityType.Matter],
            Filters = new RecordSearchFilters
            {
                Organizations = ["Acme"],
                People = ["Jane Roe"],
                ReferenceNumbers = ["MAT-1"],
            },
            Options = new RecordSearchOptions { HybridMode = mode },
        };

        await CreateService().SearchAsync(request);

        _capturedOptions.Should().NotBeNull("the search client must have been called");
        _capturedOptions!.Filter.Should().Be(
            $"tenantId eq '{TenantId}' and recordType eq 'sprk_matter'"
            + " and (organizations/any(o: o eq 'Acme'))"
            + " and (people/any(p: p eq 'Jane Roe'))"
            + " and (referenceNumbers/any(r: r eq 'MAT-1'))",
            "the filter is built only from the tenant claim and the structured filter inputs — "
            + "no fragment of the free-text query may appear in it");

        QueryShapedOptionStrings(_capturedOptions).Should().NotContain(s => s.Contains(Marker),
            "no other query-shaped search option (order-by, select, search fields, highlight fields, "
            + "scoring parameters, semantic query) may carry the query text either");

        _capturedSearchText.Should().Be(
            mode == RecordHybridSearchMode.VectorOnly ? "*" : query,
            "the query text is sent verbatim as searchText, the one parameter Azure AI Search treats as "
            + "search terms; vector-only mode sends the match-all '*' instead");

        var vectorQueries = _capturedOptions.VectorSearch?.Queries ?? new List<VectorQuery>();
        vectorQueries.Should().AllBeOfType<VectorizedQuery>(
            "vector queries carry the embedding computed here, never text for the service to vectorize");

        if (mode == RecordHybridSearchMode.KeywordOnly)
        {
            _embeddingInputs.Should().BeEmpty("keyword-only mode computes no embedding");
            vectorQueries.Should().BeEmpty();
        }
        else
        {
            _embeddingInputs.Should().NotBeEmpty().And.OnlyContain(text => text == query,
                "the embedding input is the query text itself and nothing derived from it");
            vectorQueries.Should().ContainSingle();
        }

        _deployment.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SearchAsync_FilterValuesWithSingleQuotes_AreEscapedByDoubling()
    {
        var request = new RecordSearchRequest
        {
            Query = "indemnity",
            RecordTypes = [RecordEntityType.Matter, "sprk_x') or (tenantId ne '"],
            Filters = new RecordSearchFilters
            {
                Organizations = ["O'Brien & Co", "Acme') or (tenantId ne 'x"],
                People = ["D'Arcy"],
                ReferenceNumbers = ["REF-1' or '1' eq '1"],
            },
            Options = new RecordSearchOptions { HybridMode = RecordHybridSearchMode.KeywordOnly },
        };

        await CreateService().SearchAsync(request);

        _capturedOptions!.Filter.Should().Be(
            $"tenantId eq '{TenantId}'"
            + " and search.in(recordType, 'sprk_matter,sprk_x'') or (tenantId ne ''', ',')"
            + " and (organizations/any(o: o eq 'O''Brien & Co') or organizations/any(o: o eq 'Acme'') or (tenantId ne ''x'))"
            + " and (people/any(p: p eq 'D''Arcy'))"
            + " and (referenceNumbers/any(r: r eq 'REF-1'' or ''1'' eq ''1'))",
            "every single quote inside a filter literal is doubled, so no value can close its literal and "
            + "append its own predicate");
    }

    [Fact]
    public async Task SearchAsync_SingleRecordTypeWithSingleQuote_IsEscapedByDoubling()
    {
        var request = new RecordSearchRequest
        {
            Query = "indemnity",
            RecordTypes = ["sprk_x' or tenantId ne 'y"],
            Options = new RecordSearchOptions { HybridMode = RecordHybridSearchMode.KeywordOnly },
        };

        await CreateService().SearchAsync(request);

        _capturedOptions!.Filter.Should().Be(
            $"tenantId eq '{TenantId}' and recordType eq 'sprk_x'' or tenantId ne ''y'",
            "the single-type branch (eq) escapes like the multi-type branch (search.in)");
    }

    private const string ExpectedFilter =
        $"tenantId eq '{TenantId}' and recordType eq 'sprk_matter'"
        + " and (organizations/any(o: o eq 'Acme'))";

    private static RecordSearchRequest HostileRequest(
        string? searchIndexName = null, bool preferKeywordRanking = false) => new()
        {
            Query = $"{Marker}' or tenantId ne 'x",
            RecordTypes = [RecordEntityType.Matter],
            Filters = new RecordSearchFilters { Organizations = ["Acme"] },
            Options = new RecordSearchOptions
            {
                HybridMode = RecordHybridSearchMode.KeywordOnly,
                PreferKeywordRanking = preferKeywordRanking,
            },
            SearchIndexName = searchIndexName,
        };

    [Fact]
    public async Task SearchAsync_ExplicitIndexPath_BuildsTheSameExactFilter()
    {
        const string explicitIndex = "tenant-records-index";
        var explicitClient = new Mock<SearchClient>();
        explicitClient
            .Setup(c => c.SearchAsync<SearchIndexDocument>(
                It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, SearchOptions, CancellationToken>((text, options, _) =>
            {
                _capturedSearchText = text;
                _capturedOptions = options;
            })
            .ReturnsAsync(Response.FromValue(
                SearchModelFactory.SearchResults<SearchIndexDocument>(
                    values: [], totalCount: 0, facets: null, coverage: null, rawResponse: null!),
                null!));
        _deployment
            .Setup(d => d.GetSearchClientAsync(TenantId, explicitIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(explicitClient.Object);

        await CreateService().SearchAsync(HostileRequest(searchIndexName: explicitIndex));

        _capturedOptions.Should().NotBeNull("the explicit-index client must have been called");
        _capturedOptions!.Filter.Should().Be(ExpectedFilter,
            "the tenant predicate and the escaped literals are built the same way whichever index is searched; "
            + "the explicit index changes the client, never the filter");
        _capturedSearchText.Should().Be(HostileRequest().Query);
        QueryShapedOptionStrings(_capturedOptions).Should().NotContain(s => s.Contains(Marker));
    }

    [Fact]
    public async Task SearchAsync_NoUser_FiltersOnTheConfiguredTenantNotAnAttackerChosenOne()
    {
        const string configuredTenant = "bbbbbbbb-0009-0009-0009-00000000000b";

        await CreateService(withUser: false, configuredTenant: configuredTenant).SearchAsync(HostileRequest());

        _capturedOptions!.Filter.Should().Be(
            $"tenantId eq '{configuredTenant}' and recordType eq 'sprk_matter'"
            + " and (organizations/any(o: o eq 'Acme'))",
            "with no user context the tenant comes from AzureAd:TenantId, and the query text still reaches "
            + "no part of the filter");
        _capturedOptions.Filter.Should().NotContain(Marker);
    }

    [Fact]
    public async Task SearchAsync_NoUserAndNoConfiguredTenant_FiltersOnTheSystemSentinel()
    {
        await CreateService(withUser: false).SearchAsync(HostileRequest());

        _capturedOptions!.Filter.Should().Be(
            "tenantId eq 'system' and recordType eq 'sprk_matter'"
            + " and (organizations/any(o: o eq 'Acme'))",
            "the predicate is never omitted, even with nothing to derive a tenant from");
    }

    [Fact]
    public async Task SearchAsync_PreferKeywordRanking_BuildsTheSameExactFilter()
    {
        await CreateService().SearchAsync(HostileRequest(preferKeywordRanking: true));

        _capturedOptions!.Filter.Should().Be(ExpectedFilter,
            "opting out of the semantic reranker changes ranking, not the filter");
        _capturedOptions.QueryType.Should().NotBe(SearchQueryType.Semantic,
            "this is the keyword-ranked branch the test is about");
        QueryShapedOptionStrings(_capturedOptions).Should().NotContain(s => s.Contains(Marker));
        _capturedSearchText.Should().Be(HostileRequest().Query);
    }

    private RecordSearchService CreateService(
        bool withUser = true, string? configuredTenant = null)
    {
        var indexClient = new Mock<SearchIndexClient>();
        indexClient.Setup(c => c.GetSearchClient(IndexName)).Returns(_searchClient.Object);

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = withUser
                    ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", TenantId)], "Test"))
                    : new ClaimsPrincipal(new ClaimsIdentity()),
            },
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configuredTenant is null
                ? []
                : new Dictionary<string, string?> { ["AzureAd:TenantId"] = configuredTenant })
            .Build();

        return new RecordSearchService(
            indexClient.Object,
            _openAi.Object,
            _embeddingCache.Object,
            _tenantCache.Object,
            Options.Create(new DocumentIntelligenceOptions { AiSearchIndexName = IndexName }),
            NullLogger<RecordSearchService>.Instance,
            _deployment.Object,
            configuration,
            accessor);
    }

    private static IEnumerable<string> QueryShapedOptionStrings(SearchOptions options)
    {
        IEnumerable<string?> values =
        [
            .. options.OrderBy,
            .. options.Select,
            .. options.SearchFields,
            .. options.HighlightFields,
            .. options.ScoringParameters,
            options.ScoringProfile,
            options.SemanticSearch?.SemanticQuery,
            options.SemanticSearch?.SemanticConfigurationName,
        ];

        return values.OfType<string>();
    }
}
