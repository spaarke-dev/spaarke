using System.Security.Claims;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Security;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// Seam tests for <see cref="SemanticScopeProvider"/> (spaarke-ai-architecture-redesign-r2 task
/// AIR2-061, FR-B-12) — a real <see cref="SemanticScopeProvider"/> wrapping a real
/// <see cref="RagService"/>, with only the Azure AI Search transport boundary
/// (<see cref="SearchClient"/> / <see cref="SearchIndexClient"/>) doubled. Production types all the
/// way down to the ACL — exactly the assurance the sibling <c>PrivilegeAwareRagServiceTests</c>
/// (unit-level, RagService only) does not give: that the NEW provider seam cannot introduce a path
/// which bypasses <c>PrivilegeFilterBuilder</c>.
/// </summary>
/// <remarks>
/// Acceptance criteria verified (task AIR2-061):
/// <list type="bullet">
/// <item><description>The interface wraps the existing RagService (no forked/reimplemented retrieval) and returns its results, mapped into <c>RetrievalReference</c>.</description></item>
/// <item><description>The PrivilegeFilterBuilder ACL is preserved — every call routes through RagService's privilege-filtered search path.</description></item>
/// <item><description>NEGATIVE: no scenario (no principal / principal with groups / principal with zero groups) ever produces a captured search filter missing the <c>privilege_group_ids</c> clause.</description></item>
/// </list>
/// </remarks>
public sealed class SemanticScopeProviderSeamTests
{
    private readonly Mock<IKnowledgeDeploymentService> _deploymentServiceMock = new();
    private readonly Mock<IOpenAiClient> _openAiClientMock = new();
    private readonly Mock<IEmbeddingCache> _embeddingCacheMock = new();
    private readonly Mock<IPrivilegeGroupResolver> _privilegeGroupResolverMock = new();

    private SearchOptions? _capturedSearchOptions;

    public SemanticScopeProviderSeamTests()
    {
        _embeddingCacheMock
            .Setup(c => c.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);

        _openAiClientMock
            .Setup(c => c.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadOnlyMemory<float>(new float[3072]));
    }

    /// <summary>
    /// Builds the seam: a real <see cref="SemanticScopeProvider"/> over a real <see cref="RagService"/>,
    /// with a mocked <see cref="SearchClient"/> (transport boundary) that captures the
    /// <see cref="SearchOptions"/> passed to it and returns <paramref name="documents"/>.
    /// </summary>
    private ISemanticScopeProvider CreateProvider(params (KnowledgeDocument Document, double Score)[] documents)
    {
        var searchClientMock = new Mock<SearchClient>(
            new Uri("https://search.example.com"), "test-index", new AzureKeyCredential("fake-key"));

        var results = documents
            .Select(d => SearchModelFactory.SearchResult(d.Document, score: d.Score, highlights: null))
            .ToList();

        searchClientMock
            .Setup(c => c.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, SearchOptions, CancellationToken>((_, opts, _) => _capturedSearchOptions = opts)
            .ReturnsAsync(Response.FromValue(
                SearchModelFactory.SearchResults(results, totalCount: results.Count, facets: null, coverage: null, rawResponse: Mock.Of<Response>()),
                Mock.Of<Response>()));

        _deploymentServiceMock
            .Setup(d => d.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);

        var analysisOptions = Options.Create(new AnalysisOptions
        {
            DefaultRagModel = RagDeploymentModel.Shared,
            MaxKnowledgeResults = 5,
            MinRelevanceScore = 0.0f,
        });

        var aiSearchOptions = Options.Create(new AiSearchOptions
        {
            Endpoint = "https://test-search.search.windows.net",
            ApiKeySecretName = "test-api-key",
            KnowledgeIndexName = "spaarke-knowledge-index-v2",
            DiscoveryIndexName = "discovery-index",
        });

        var ragService = new RagService(
            _deploymentServiceMock.Object,
            _openAiClientMock.Object,
            _embeddingCacheMock.Object,
            _privilegeGroupResolverMock.Object,
            analysisOptions,
            new Mock<SearchIndexClient>(MockBehavior.Loose).Object,
            aiSearchOptions,
            Mock.Of<ILogger<RagService>>());

        return new SemanticScopeProvider(ragService, Mock.Of<ILogger<SemanticScopeProvider>>());
    }

    private static ClaimsPrincipal BuildPrincipal(string oid = "test-oid") =>
        new(new ClaimsIdentity(new[] { new Claim("oid", oid) }, "Test"));

    private void SetupGroupResolver(params string[] groupIds) =>
        _privilegeGroupResolverMock
            .Setup(r => r.ResolveGroupIdsAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(groupIds.ToList().AsReadOnly());

    // ─── Wraps RagService — returns mapped retrieval references ───────────────────────────

    [Fact]
    public async Task GetSemanticContextAsync_RagServiceReturnsResults_MapsToRetrievalReferences()
    {
        var doc = new KnowledgeDocument
        {
            Id = "chunk-1",
            DocumentId = "doc-1",
            TenantId = "tenant-a",
            FileName = "policy.pdf",
            KnowledgeSourceName = "Legal KB",
        };
        var sut = CreateProvider((doc, 0.95));

        var result = await sut.GetSemanticContextAsync(new SemanticScopeRequest
        {
            TenantId = "tenant-a",
            Query = "termination clause",
        });

        result.Retrieval.Should().ContainSingle();
        var reference = result.Retrieval[0];
        reference.DocumentRef.Should().Be("doc-1", "DocumentId should be preferred over the chunk Id");
        reference.IndexId.Should().Be("Legal KB");
        reference.ProvenanceClass.Should().Be("semantic_retrieval");
    }

    [Fact]
    public async Task GetSemanticContextAsync_OrphanChunkWithNoDocumentId_FallsBackToChunkId()
    {
        var doc = new KnowledgeDocument
        {
            Id = "orphan-chunk-1",
            DocumentId = null,
            TenantId = "tenant-a",
            FileName = "orphan.pdf",
        };
        var sut = CreateProvider((doc, 0.9));

        var result = await sut.GetSemanticContextAsync(new SemanticScopeRequest
        {
            TenantId = "tenant-a",
            Query = "orphan file",
        });

        result.Retrieval.Should().ContainSingle();
        result.Retrieval[0].DocumentRef.Should().Be("orphan-chunk-1");
    }

    // ─── ACL preservation (AIPU2-027) — the core security contract ────────────────────────

    [Fact]
    public async Task GetSemanticContextAsync_NoCallerPrincipal_StillAppliesPublicOnlyPrivilegeFilter()
    {
        var sut = CreateProvider();

        await sut.GetSemanticContextAsync(new SemanticScopeRequest
        {
            TenantId = "tenant-a",
            Query = "no caller present",
            CallerPrincipal = null,
        });

        _capturedSearchOptions.Should().NotBeNull();
        _capturedSearchOptions!.Filter.Should().Contain("privilege_group_ids",
            "the ACL must be present even when the provider is invoked with no caller principal — " +
            "this is the seam's core guarantee: it can never request an unfiltered search");
        _capturedSearchOptions.Filter.Should().Contain("not privilege_group_ids/any()",
            "public-only fallback clause must still be present (fail-closed)");
    }

    [Fact]
    public async Task GetSemanticContextAsync_CallerPrincipalWithGroups_ForwardsGroupsIntoPrivilegeFilter()
    {
        SetupGroupResolver("group-aaa", "group-bbb");
        var sut = CreateProvider();

        await sut.GetSemanticContextAsync(new SemanticScopeRequest
        {
            TenantId = "tenant-a",
            Query = "caller with groups",
            CallerPrincipal = BuildPrincipal(),
        });

        _capturedSearchOptions.Should().NotBeNull();
        _capturedSearchOptions!.Filter.Should().Contain("g eq 'group-aaa'",
            "the CallerPrincipal supplied to the provider must reach RagService's group resolution unchanged");
        _capturedSearchOptions.Filter.Should().Contain("g eq 'group-bbb'");
    }

    [Fact]
    public async Task GetSemanticContextAsync_CallerPrincipalWithZeroGroups_AppliesPublicOnlyFilter_NeverUnfiltered()
    {
        // Resolver returns an empty list (e.g. malformed token / no group memberships) — the seam
        // must NOT interpret "zero groups" as "skip the filter"; fail-closed to public-only.
        SetupGroupResolver();
        var sut = CreateProvider();

        await sut.GetSemanticContextAsync(new SemanticScopeRequest
        {
            TenantId = "tenant-a",
            Query = "caller with zero resolved groups",
            CallerPrincipal = BuildPrincipal(),
        });

        _capturedSearchOptions.Should().NotBeNull();
        _capturedSearchOptions!.Filter.Should().Contain("not privilege_group_ids/any()",
            "zero resolved groups must degrade to the public-only clause, never to an unfiltered search");
    }

    // ─── Semantic source is Azure AI Search + SPE, not Dataverse ──────────────────────────

    [Fact]
    public async Task GetSemanticContextAsync_TenantIdForwarded_RoutesThroughAzureAiSearchTenantFilter()
    {
        // The seam's only routing input besides the query is TenantId — forwarded unchanged into
        // RagSearchOptions.TenantId, which RagService unconditionally ANDs into the Azure AI Search
        // filter (ADR-014). There is no Dataverse client anywhere in this provider's dependency
        // graph (constructor takes only IRagService + ILogger) — the semantic source is
        // structurally Azure AI Search, never Dataverse search.
        var sut = CreateProvider();

        await sut.GetSemanticContextAsync(new SemanticScopeRequest
        {
            TenantId = "tenant-xyz",
            Query = "tenant routing check",
        });

        _capturedSearchOptions.Should().NotBeNull();
        _capturedSearchOptions!.Filter.Should().Contain("tenantId eq 'tenant-xyz'");
    }
}
