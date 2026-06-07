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

// Disambiguate between our pipeline result type and the Azure SDK indexing result type.
using AzureSdkIndexingResult = Azure.Search.Documents.Models.IndexingResult;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for RagService - Hybrid RAG search implementation.
/// Tests hybrid search (keyword + vector), semantic ranking, and document indexing.
/// </summary>
[Trait("status", "repaired")]
public class RagServiceTests
{
    private readonly Mock<IKnowledgeDeploymentService> _deploymentServiceMock;
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<IEmbeddingCache> _embeddingCacheMock;
    private readonly Mock<IPrivilegeGroupResolver> _privilegeGroupResolverMock;
    private readonly Mock<ILogger<RagService>> _loggerMock;
    private readonly IOptions<AnalysisOptions> _options;

    // R5 task 002 — session-files routing requires per-test SearchIndexClient mock
    // configuration. The mock is constructed once per test class instance and exposed
    // so tests targeting SessionId-routing can pre-configure GetSearchClient(indexName)
    // to return a mock SearchClient and Verify that the session-files index was selected.
    private readonly Mock<SearchIndexClient> _searchIndexClientMock;
    private const string SessionFilesIndexName = "spaarke-session-files";

    // Test embedding (3072 dimensions like text-embedding-3-large)
    private readonly ReadOnlyMemory<float> _testEmbedding;

    public RagServiceTests()
    {
        _deploymentServiceMock = new Mock<IKnowledgeDeploymentService>();
        _openAiClientMock = new Mock<IOpenAiClient>();
        _embeddingCacheMock = new Mock<IEmbeddingCache>();
        _privilegeGroupResolverMock = new Mock<IPrivilegeGroupResolver>();
        _loggerMock = new Mock<ILogger<RagService>>();
        _searchIndexClientMock = new Mock<SearchIndexClient>(MockBehavior.Loose);
        _options = Options.Create(new AnalysisOptions
        {
            DefaultRagModel = RagDeploymentModel.Shared,
            MaxKnowledgeResults = 5,
            MinRelevanceScore = 0.7f
        });

        // Default: user has no groups — privilege filter will apply public-only clause
        _privilegeGroupResolverMock
            .Setup(r => r.ResolveGroupIdsAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        // Create a test embedding vector (3072 dimensions for text-embedding-3-large)
        var embedding = new float[3072];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(i % 10) / 10f;
        }
        _testEmbedding = new ReadOnlyMemory<float>(embedding);
    }

    private RagService CreateService()
    {
        // Constructor updated 2026-06-01 (RB-T028-03/04/05/06 repair, Phase 1c test infra):
        // Tier 3 B8 refactor (commit 5613b8ad) added required SearchIndexClient +
        // IOptions<AiSearchOptions> parameters to absorb the SDK calls previously made by
        // KnowledgeBaseEndpoints. Existing unit tests do not exercise the B8 admin methods,
        // so a Loose mock SearchIndexClient + a default-shape AiSearchOptions satisfies the
        // constructor without affecting test semantics.
        //
        // R5 task 002 (2026-06-04): the shared `_searchIndexClientMock` field is also used
        // by session-files-routing tests to Verify that GetSearchClient(SessionFilesIndexName)
        // is invoked when `RagSearchOptions.SessionId` is non-empty. The AiSearchOptions
        // here includes the SessionFilesIndexName default so existing tests remain unaffected.
        var aiSearchOptions = Options.Create(new AiSearchOptions
        {
            Endpoint = "https://test-search.search.windows.net",
            ApiKeySecretName = "test-api-key",
            KnowledgeIndexName = "spaarke-knowledge-index-v2",
            DiscoveryIndexName = "discovery-index",
            SessionFilesIndexName = SessionFilesIndexName,
            SessionFilesSemanticConfigName = "session-files-semantic-config"
        });

        return new RagService(
            _deploymentServiceMock.Object,
            _openAiClientMock.Object,
            _embeddingCacheMock.Object,
            _privilegeGroupResolverMock.Object,
            _options,
            _searchIndexClientMock.Object,
            aiSearchOptions,
            _loggerMock.Object);
    }

    #region SearchAsync Tests

    [Fact]
    public async Task SearchAsync_NullQuery_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.SearchAsync(null!, options));
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.SearchAsync(string.Empty, options));
    }

    [Fact]
    public async Task SearchAsync_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.SearchAsync("test query", null!));
    }

    [Fact]
    public async Task SearchAsync_EmptyTenantId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = string.Empty };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.SearchAsync("test query", options));
    }

    [Fact]
    public async Task SearchAsync_ValidQuery_GeneratesEmbedding()
    {
        // Arrange
        var service = CreateService();
        var query = "test search query";
        var options = new RagSearchOptions { TenantId = "tenant-1", UseVectorSearch = true };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(query, options);

        // Assert - Embedding should be generated
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(query, null, It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_VectorSearchDisabled_SkipsEmbedding()
    {
        // Arrange
        var service = CreateService();
        var query = "test search query";
        var options = new RagSearchOptions
        {
            TenantId = "tenant-1",
            UseVectorSearch = false
        };

        SetupMockSearchClient();

        // Act
        await service.SearchAsync(query, options);

        // Assert - Embedding should NOT be generated
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_UsesCorrectTenant_ForSearchClient()
    {
        // Arrange
        var service = CreateService();
        var tenantId = "tenant-123";
        var options = new RagSearchOptions { TenantId = tenantId };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync("test query", options);

        // Assert - Should get SearchClient for correct tenant
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync(tenantId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithDeploymentId_UsesDeploymentIdRouting()
    {
        // Arrange
        var service = CreateService();
        var deploymentId = Guid.NewGuid();
        var options = new RagSearchOptions
        {
            TenantId = "tenant-1",
            DeploymentId = deploymentId
        };

        SetupMockEmbedding();
        SetupMockSearchClientByDeployment();

        // Act
        await service.SearchAsync("test query", options);

        // Assert - Should get SearchClient by deployment ID
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientByDeploymentAsync(deploymentId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── multi-container-multi-index-r1 FR-BFF-07 (task 014) ──────────────────────────────
    // The three tests below cover the SearchIndexName thread-through:
    //   (a) regression — SearchIndexName absent leaves the call site on the 2-arg overload
    //       (preserves NFR-02 / FR-BFF-04 fall-through to the existing 2-tier chain);
    //   (b) thread-through — SearchIndexName non-empty routes via the 3-arg overload with
    //       the value passed through verbatim (FR-BFF-03);
    //   (c) whitespace — SearchIndexName whitespace-only is treated as absent (FR-BFF-04).

    [Fact]
    public async Task SearchAsync_WhenSearchIndexNameAbsent_UsesTwoArgResolverOverload()
    {
        // Regression: existing callers (no SearchIndexName) MUST continue to invoke the
        // original 2-arg GetSearchClientAsync overload. The 3-arg overload MUST NOT be hit.
        // Spec NFR-02 backward-compat (per task 010 two-overload design rationale).

        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = "tenant-123" }; // SearchIndexName not set

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync("test query", options);

        // Assert — 2-arg overload invoked exactly once; 3-arg overload NOT touched.
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync("tenant-123", It.IsAny<CancellationToken>()),
            Times.Once);
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WhenSearchIndexNameProvided_ThreadsThroughToThreeArgResolverOverload()
    {
        // FR-BFF-07 thread-through: when RagSearchRequest.SearchIndexName flows into
        // RagSearchOptions.SearchIndexName (endpoint wiring done in task 016), RagService
        // MUST pass the value verbatim into the 3-arg resolver overload, which applies
        // allow-list validation in KnowledgeDeploymentService (FR-BFF-02 / NFR-08).

        // Arrange
        var service = CreateService();
        var explicitIndex = "spaarke-file-index";
        var options = new RagSearchOptions
        {
            TenantId = "tenant-123",
            SearchIndexName = explicitIndex
        };

        SetupMockEmbedding();
        SetupMockSearchClientWithExplicitIndex();

        // Act
        await service.SearchAsync("test query", options);

        // Assert — 3-arg overload invoked with the explicit index name; 2-arg overload NOT touched.
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync("tenant-123", explicitIndex, It.IsAny<CancellationToken>()),
            Times.Once);
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WhenSearchIndexNameWhitespace_UsesTwoArgResolverOverload()
    {
        // FR-BFF-04 — whitespace-only SearchIndexName MUST be treated identically to null
        // (fall through to the existing 2-tier chain). Defense-in-depth against accidental
        // " " values flowing in from upstream URL params or DTO defaulting.

        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions
        {
            TenantId = "tenant-123",
            SearchIndexName = "   "
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync("test query", options);

        // Assert — 2-arg overload invoked; 3-arg overload NOT touched.
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync("tenant-123", It.IsAny<CancellationToken>()),
            Times.Once);
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_ReturnsSearchDurationMetrics()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync("test query", options);

        // Assert
        result.Should().NotBeNull();
        result.SearchDurationMs.Should().BeGreaterOrEqualTo(0);
        result.EmbeddingDurationMs.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task SearchAsync_ReturnsOriginalQuery()
    {
        // Arrange
        var service = CreateService();
        var query = "my test query";
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(query, options);

        // Assert
        result.Query.Should().Be(query);
    }

    #endregion

    #region SearchAsync — Session-Files Routing (R5 task 002 / FR-09)

    // The four tests in this region verify the additive `SessionId` routing branch added
    // by R5 task 002. They cover (a) regression — SessionId absent leaves behavior
    // identical to pre-R5; (b) routing — SessionId present invokes
    // SearchIndexClient.GetSearchClient(SessionFilesIndexName); (c) filter invariant —
    // BOTH `tenantId eq '...'` AND `sessionId eq '...'` clauses are emitted per
    // ADR-014 (tenant + session isolation); (d) skip-behavior — knowledge-source /
    // privilege-group / parent-entity filters are NOT applied under session routing
    // because the session-files schema does not carry those columns (task 001).

    [Fact]
    public async Task SearchAsync_WhenSessionIdAbsent_RoutesToTenantKnowledgeIndex()
    {
        // R5 task 002 regression: pre-R5 callers (R3/R4 wizard, document classifier,
        // analysis nodes) pass NO SessionId — behavior MUST stay byte-for-byte identical.
        // Spec NFR-10 back-compat.

        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = "tenant-1" }; // No SessionId

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync("test query", options);

        // Assert — Deployment-service path (existing) is invoked, session-files index
        // is NOT touched.
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync("tenant-1", It.IsAny<CancellationToken>()),
            Times.Once);
        _searchIndexClientMock.Verify(
            x => x.GetSearchClient(SessionFilesIndexName),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WhenSessionIdProvided_RoutesToSessionFilesIndex()
    {
        // R5 task 002 / FR-09: session-scoped retrieval routes via
        // SearchIndexClient.GetSearchClient(SessionFilesIndexName) — NOT via
        // IKnowledgeDeploymentService (which routes per-tenant for the knowledge index).

        // Arrange
        var sessionSearchClientMock = SetupSessionFilesSearchClient();
        var service = CreateService();
        var options = new RagSearchOptions
        {
            TenantId = "tenant-1",
            SessionId = "session-abc-123"
        };

        SetupMockEmbedding();

        // Act
        await service.SearchAsync("test query", options);

        // Assert — session-files index is selected via SearchIndexClient;
        // deployment-service path is NOT touched.
        _searchIndexClientMock.Verify(
            x => x.GetSearchClient(SessionFilesIndexName),
            Times.Once);
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientByDeploymentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WhenSessionIdProvided_AppliesTenantAndSessionFilter()
    {
        // R5 task 002 / ADR-014: tenant isolation invariant — when SessionId is set,
        // the OData filter MUST AND `tenantId eq '<tid>'` with `sessionId eq '<sid>'`.
        // A session query in tenant A can never leak across to tenant B.

        // Arrange
        SearchOptions? capturedOptions = null;
        var sessionSearchClientMock = SetupSessionFilesSearchClient(capture: opts => capturedOptions = opts);
        var service = CreateService();
        var options = new RagSearchOptions
        {
            TenantId = "tenant-A",
            SessionId = "session-xyz"
        };

        SetupMockEmbedding();

        // Act
        await service.SearchAsync("test query", options);

        // Assert — OData filter contains BOTH tenant AND session clauses ANDed.
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Filter.Should().NotBeNull();
        capturedOptions.Filter.Should().Contain("tenantId eq 'tenant-A'");
        capturedOptions.Filter.Should().Contain("sessionId eq 'session-xyz'");
        capturedOptions.Filter.Should().Contain(" and ");
        // Defense-in-depth — ensure clauses are NEVER OR'd (cross-tenant leak risk).
        capturedOptions.Filter.Should().NotContain("tenantId eq 'tenant-A' or sessionId");
    }

    [Fact]
    public async Task SearchAsync_WhenSessionIdProvided_DoesNotApplyKnowledgeSourceOrPrivilegeFilters()
    {
        // R5 task 002 — session-files schema (task 001) does NOT carry
        // `knowledgeSourceId` / `parentEntityType` / `parentEntityId` / `privilege_group_ids`
        // columns. The session-routing branch SKIPS those filters even when callers pass
        // them. Documented in task 002 POML constraints.

        // Arrange
        SearchOptions? capturedOptions = null;
        var sessionSearchClientMock = SetupSessionFilesSearchClient(capture: opts => capturedOptions = opts);
        var service = CreateService();
        var options = new RagSearchOptions
        {
            TenantId = "tenant-1",
            SessionId = "session-abc",
            KnowledgeSourceId = "ks-001", // SHOULD BE IGNORED
            DocumentType = "contract",    // SHOULD BE IGNORED
            ParentEntityType = "matter",  // SHOULD BE IGNORED
            ParentEntityId = "matter-456" // SHOULD BE IGNORED
        };

        SetupMockEmbedding();

        // Act
        await service.SearchAsync("test query", options);

        // Assert — knowledge-source / document-type / parent-entity clauses NOT emitted,
        // privilege-group clause NOT emitted; only tenant + session remain (plus any
        // tag-based clauses, which ARE valid against the session-files schema).
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Filter.Should().NotContain("knowledgeSourceId");
        capturedOptions.Filter.Should().NotContain("documentType eq");
        capturedOptions.Filter.Should().NotContain("parentEntityType eq");
        capturedOptions.Filter.Should().NotContain("parentEntityId eq");
        capturedOptions.Filter.Should().NotContain("privilege_group_ids");
        // Tenant + session ARE present (regression guard).
        capturedOptions.Filter.Should().Contain("tenantId eq 'tenant-1'");
        capturedOptions.Filter.Should().Contain("sessionId eq 'session-abc'");
    }

    #endregion

    #region RagSearchOptions Extended Properties Tests

    [Fact]
    public void RagSearchOptions_ExcludeKnowledgeSourceIds_DefaultsToNull()
    {
        // Arrange & Act
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        // Assert
        options.ExcludeKnowledgeSourceIds.Should().BeNull();
    }

    [Fact]
    public void RagSearchOptions_RequiredTags_DefaultsToNull()
    {
        // Arrange & Act
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        // Assert
        options.RequiredTags.Should().BeNull();
    }

    [Fact]
    public void RagSearchOptions_ExcludeTags_DefaultsToNull()
    {
        // Arrange & Act
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        // Assert
        options.ExcludeTags.Should().BeNull();
    }

    [Fact]
    public void RagSearchOptions_ParentEntityType_DefaultsToNull()
    {
        // Arrange & Act
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        // Assert
        options.ParentEntityType.Should().BeNull();
    }

    [Fact]
    public void RagSearchOptions_ParentEntityId_DefaultsToNull()
    {
        // Arrange & Act
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        // Assert
        options.ParentEntityId.Should().BeNull();
    }

    [Fact]
    public void RagSearchOptions_SessionId_DefaultsToNull()
    {
        // R5 task 002 — SessionId is the new additive property; default-null preserves
        // pre-R5 routing behavior for all existing callers (NFR-10 back-compat).

        // Arrange & Act
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        // Assert
        options.SessionId.Should().BeNull();
    }

    [Fact]
    public void RagSearchOptions_SessionId_CanBeSet()
    {
        // Arrange & Act
        var options = new RagSearchOptions
        {
            TenantId = "tenant-1",
            SessionId = "session-abc-123"
        };

        // Assert
        options.SessionId.Should().Be("session-abc-123");
    }

    [Fact]
    public void RagSearchOptions_EntityScope_BothFieldsCanBeSet()
    {
        // Arrange & Act
        var options = new RagSearchOptions
        {
            TenantId = "tenant-1",
            ParentEntityType = "matter",
            ParentEntityId = "matter-456"
        };

        // Assert
        options.ParentEntityType.Should().Be("matter");
        options.ParentEntityId.Should().Be("matter-456");
    }

    [Fact]
    public void RagSearchOptions_ExcludeKnowledgeSourceIds_CanBeSet()
    {
        // Arrange & Act
        var excludeIds = new List<string> { "ks-001", "ks-002", "ks-003" };
        var options = new RagSearchOptions
        {
            TenantId = "tenant-1",
            ExcludeKnowledgeSourceIds = excludeIds
        };

        // Assert
        options.ExcludeKnowledgeSourceIds.Should().NotBeNull();
        options.ExcludeKnowledgeSourceIds.Should().HaveCount(3);
        options.ExcludeKnowledgeSourceIds.Should().ContainInOrder("ks-001", "ks-002", "ks-003");
    }

    [Fact]
    public void RagSearchOptions_RequiredAndExcludeTags_CanCoexist()
    {
        // Arrange & Act
        var requiredTags = new List<string> { "finance", "compliance" };
        var excludeTags = new List<string> { "draft", "archived" };
        var options = new RagSearchOptions
        {
            TenantId = "tenant-1",
            RequiredTags = requiredTags,
            ExcludeTags = excludeTags
        };

        // Assert
        options.RequiredTags.Should().NotBeNull();
        options.RequiredTags.Should().HaveCount(2);
        options.RequiredTags.Should().Contain("finance");
        options.RequiredTags.Should().Contain("compliance");

        options.ExcludeTags.Should().NotBeNull();
        options.ExcludeTags.Should().HaveCount(2);
        options.ExcludeTags.Should().Contain("draft");
        options.ExcludeTags.Should().Contain("archived");
    }

    #endregion

    #region IndexDocumentAsync Tests

    [Fact]
    public async Task IndexDocumentAsync_NullDocument_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.IndexDocumentAsync(null!));
    }

    [Fact]
    public async Task IndexDocumentAsync_EmptyTenantId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument { TenantId = string.Empty, SpeFileId = "file-1" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.IndexDocumentAsync(document));
    }

    [Fact]
    public async Task IndexDocumentAsync_EmptySpeFileId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument { TenantId = "tenant-1", SpeFileId = string.Empty };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.IndexDocumentAsync(document));
    }

    [Fact]
    public async Task IndexDocumentAsync_NullSpeFileId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument { TenantId = "tenant-1", SpeFileId = null };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.IndexDocumentAsync(document));
    }

    [Fact]
    public async Task IndexDocumentAsync_DocumentWithoutVector_GeneratesEmbedding()
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument
        {
            Id = "doc-1",
            TenantId = "tenant-1",
            SpeFileId = "file-1",
            Content = "Test document content"
            // ContentVector is empty
        };

        SetupMockEmbedding();
        SetupMockSearchClientForIndexing();

        // Act
        await service.IndexDocumentAsync(document);

        // Assert - Embedding should be generated
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync("Test document content", null, It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IndexDocumentAsync_DocumentWithVector_SkipsEmbedding()
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument
        {
            Id = "doc-1",
            TenantId = "tenant-1",
            SpeFileId = "file-1",
            Content = "Test document content",
            ContentVector = _testEmbedding
        };

        SetupMockSearchClientForIndexing();

        // Act
        await service.IndexDocumentAsync(document);

        // Assert - Embedding should NOT be generated
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IndexDocumentAsync_SetsTimestamps()
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument
        {
            Id = "doc-1",
            TenantId = "tenant-1",
            SpeFileId = "file-1",
            Content = "Test content",
            ContentVector = _testEmbedding
        };

        SetupMockSearchClientForIndexing();

        // Act
        var beforeIndex = DateTimeOffset.UtcNow;
        var result = await service.IndexDocumentAsync(document);
        var afterIndex = DateTimeOffset.UtcNow;

        // Assert
        result.CreatedAt.Should().BeOnOrAfter(beforeIndex);
        result.CreatedAt.Should().BeOnOrBefore(afterIndex);
        result.UpdatedAt.Should().BeOnOrAfter(beforeIndex);
        result.UpdatedAt.Should().BeOnOrBefore(afterIndex);
    }

    [Fact]
    public async Task IndexDocumentAsync_UsesProvidedFileName()
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument
        {
            Id = "doc-1",
            TenantId = "tenant-1",
            SpeFileId = "file-1",
            FileName = "Contract.pdf",
            ContentVector = _testEmbedding
        };

        SetupMockSearchClientForIndexing();

        // Act
        var result = await service.IndexDocumentAsync(document);

        // Assert - FileName should be preserved
        result.FileName.Should().Be("Contract.pdf");
    }

    [Fact]
    public async Task IndexDocumentAsync_ExtractsFileTypeFromFileName()
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument
        {
            Id = "doc-1",
            TenantId = "tenant-1",
            SpeFileId = "file-1",
            FileName = "Document.docx",
            ContentVector = _testEmbedding
        };

        SetupMockSearchClientForIndexing();

        // Act
        var result = await service.IndexDocumentAsync(document);

        // Assert - FileType should be extracted
        result.FileType.Should().Be("docx");
    }

    [Fact]
    public async Task IndexDocumentAsync_PreservesExistingFileType()
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument
        {
            Id = "doc-1",
            TenantId = "tenant-1",
            SpeFileId = "file-1",
            FileName = "Document.docx",
            FileType = "pdf", // Explicitly set different type
            ContentVector = _testEmbedding
        };

        SetupMockSearchClientForIndexing();

        // Act
        var result = await service.IndexDocumentAsync(document);

        // Assert - FileType should not be overwritten
        result.FileType.Should().Be("pdf");
    }

    [Theory]
    [InlineData("Document.pdf", "pdf")]
    [InlineData("Spreadsheet.xlsx", "xlsx")]
    [InlineData("Presentation.pptx", "pptx")]
    [InlineData("Email.msg", "msg")]
    [InlineData("Email.eml", "eml")]
    [InlineData("Text.txt", "txt")]
    [InlineData("Web.html", "html")]
    [InlineData("Word.doc", "doc")]
    [InlineData("Excel.xls", "xls")]
    [InlineData("Data.csv", "csv")]
    public async Task IndexDocumentAsync_ExtractsCorrectFileType(string fileName, string expectedType)
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument
        {
            Id = "doc-1",
            TenantId = "tenant-1",
            SpeFileId = "file-1",
            FileName = fileName,
            ContentVector = _testEmbedding
        };

        SetupMockSearchClientForIndexing();

        // Act
        var result = await service.IndexDocumentAsync(document);

        // Assert
        result.FileType.Should().Be(expectedType);
    }

    [Theory]
    [InlineData("Document", "unknown")]
    [InlineData("Document.xyz", "unknown")]
    [InlineData("", "unknown")]
    [InlineData("Document.", "unknown")]
    public async Task IndexDocumentAsync_ReturnsUnknownForInvalidExtension(string fileName, string expectedType)
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument
        {
            Id = "doc-1",
            TenantId = "tenant-1",
            SpeFileId = "file-1",
            FileName = string.IsNullOrEmpty(fileName) ? "unknown" : fileName,
            ContentVector = _testEmbedding
        };

        SetupMockSearchClientForIndexing();

        // Act
        var result = await service.IndexDocumentAsync(document);

        // Assert
        result.FileType.Should().Be(expectedType);
    }

    [Fact]
    public async Task IndexDocumentAsync_OrphanFile_WorksWithNullDocumentId()
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument
        {
            Id = "file-1_0",
            TenantId = "tenant-1",
            SpeFileId = "file-1",
            DocumentId = null, // Orphan file
            FileName = "Orphan.pdf",
            ContentVector = _testEmbedding
        };

        SetupMockSearchClientForIndexing();

        // Act
        var result = await service.IndexDocumentAsync(document);

        // Assert
        result.DocumentId.Should().BeNull();
        result.SpeFileId.Should().Be("file-1");
        result.FileType.Should().Be("pdf");
    }

    #endregion

    #region IndexDocumentsBatchAsync Tests

    [Fact]
    public async Task IndexDocumentsBatchAsync_NullDocuments_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.IndexDocumentsBatchAsync(null!));
    }

    [Fact]
    public async Task IndexDocumentsBatchAsync_EmptyList_ReturnsEmptyResults()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.IndexDocumentsBatchAsync(Array.Empty<KnowledgeDocument>());

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task IndexDocumentsBatchAsync_MultipleDocuments_GeneratesBatchEmbeddings()
    {
        // Arrange
        var service = CreateService();
        var documents = new[]
        {
            new KnowledgeDocument { Id = "doc-1", TenantId = "tenant-1", SpeFileId = "file-1", Content = "Content 1" },
            new KnowledgeDocument { Id = "doc-2", TenantId = "tenant-1", SpeFileId = "file-2", Content = "Content 2" }
        };

        SetupMockBatchEmbeddings(2);
        SetupMockSearchClientForIndexing();

        // Act
        await service.IndexDocumentsBatchAsync(documents);

        // Assert - Batch embedding should be called
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingsAsync(It.Is<IEnumerable<string>>(e => e.Count() == 2), null, It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IndexDocumentsBatchAsync_MissingSpeFileId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var documents = new[]
        {
            new KnowledgeDocument { Id = "doc-1", TenantId = "tenant-1", SpeFileId = "file-1", Content = "Content 1" },
            new KnowledgeDocument { Id = "doc-2", TenantId = "tenant-1", SpeFileId = null, Content = "Content 2" }
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.IndexDocumentsBatchAsync(documents));
    }

    [Fact]
    public async Task IndexDocumentsBatchAsync_ExtractsFileTypeForAllDocuments()
    {
        // Arrange
        var service = CreateService();
        var documents = new[]
        {
            new KnowledgeDocument
            {
                Id = "doc-1",
                TenantId = "tenant-1",
                SpeFileId = "file-1",
                FileName = "Contract.pdf",
                ContentVector = _testEmbedding
            },
            new KnowledgeDocument
            {
                Id = "doc-2",
                TenantId = "tenant-1",
                SpeFileId = "file-2",
                FileName = "Report.docx",
                ContentVector = _testEmbedding
            }
        };

        SetupMockSearchClientForIndexing();

        // Act
        await service.IndexDocumentsBatchAsync(documents);

        // Assert - All documents should have file type extracted
        documents[0].FileType.Should().Be("pdf");
        documents[1].FileType.Should().Be("docx");
    }

    [Fact]
    public async Task IndexDocumentsBatchAsync_OrphanFiles_GroupsBySpeFileId()
    {
        // Arrange
        var service = CreateService();
        var speFileId = "orphan-file-1";
        var documents = new[]
        {
            new KnowledgeDocument
            {
                Id = $"{speFileId}_0",
                TenantId = "tenant-1",
                SpeFileId = speFileId,
                DocumentId = null, // Orphan - no DocumentId
                FileName = "Orphan.pdf",
                ContentVector = _testEmbedding,
                ChunkIndex = 0
            },
            new KnowledgeDocument
            {
                Id = $"{speFileId}_1",
                TenantId = "tenant-1",
                SpeFileId = speFileId,
                DocumentId = null, // Orphan - no DocumentId
                FileName = "Orphan.pdf",
                ContentVector = _testEmbedding,
                ChunkIndex = 1
            }
        };

        SetupMockSearchClientForIndexing();

        // Act
        await service.IndexDocumentsBatchAsync(documents);

        // Assert - Both chunks should have documentVector set (grouped by SpeFileId)
        documents[0].DocumentVector.Length.Should().BeGreaterThan(0);
        documents[1].DocumentVector.Length.Should().BeGreaterThan(0);
        // Both should have the same documentVector (averaged from same file's chunks)
        documents[0].DocumentVector.ToArray().Should().BeEquivalentTo(documents[1].DocumentVector.ToArray());
    }

    #endregion

    #region DeleteDocumentAsync Tests

    [Fact]
    public async Task DeleteDocumentAsync_NullDocumentId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.DeleteDocumentAsync(null!, "tenant-1"));
    }

    [Fact]
    public async Task DeleteDocumentAsync_NullTenantId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.DeleteDocumentAsync("doc-1", null!));
    }

    #endregion

    #region GetEmbeddingAsync Tests

    [Fact]
    public async Task GetEmbeddingAsync_NullText_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.GetEmbeddingAsync(null!));
    }

    [Fact]
    public async Task GetEmbeddingAsync_EmptyText_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.GetEmbeddingAsync(string.Empty));
    }

    [Fact]
    public async Task GetEmbeddingAsync_ValidText_ReturnsEmbedding()
    {
        // Arrange
        var service = CreateService();
        SetupMockEmbedding();

        // Act
        var result = await service.GetEmbeddingAsync("test text");

        // Assert - 3072 dimensions for text-embedding-3-large
        result.Length.Should().Be(3072);
    }

    [Fact]
    public async Task GetEmbeddingAsync_CacheHit_DoesNotCallOpenAi()
    {
        // Arrange
        var service = CreateService();
        SetupMockEmbeddingCacheHit();

        // Act
        var result = await service.GetEmbeddingAsync("test text");

        // Assert - Should NOT call OpenAI (cache hit)
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        result.Length.Should().Be(3072);
    }

    [Fact]
    public async Task GetEmbeddingAsync_CacheMiss_CallsOpenAiAndCaches()
    {
        // Arrange
        var service = CreateService();
        SetupMockEmbedding();

        // Act
        var result = await service.GetEmbeddingAsync("test text");

        // Assert - Should call OpenAI and cache the result
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync("test text", null, It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _embeddingCacheMock.Verify(
            x => x.SetEmbeddingForContentAsync("test text", It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Embedding Cache Tests

    [Fact]
    public async Task SearchAsync_CacheHit_ReturnsEmbeddingCacheHitTrue()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = "tenant-1", UseVectorSearch = true };

        SetupMockEmbeddingCacheHit();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync("test query", options);

        // Assert
        result.EmbeddingCacheHit.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_CacheMiss_ReturnsEmbeddingCacheHitFalse()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = "tenant-1", UseVectorSearch = true };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync("test query", options);

        // Assert
        result.EmbeddingCacheHit.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_CacheHit_DoesNotCallOpenAi()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = "tenant-1", UseVectorSearch = true };

        SetupMockEmbeddingCacheHit();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync("test query", options);

        // Assert - Should NOT call OpenAI when cache hits
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_CacheMiss_CachesGeneratedEmbedding()
    {
        // Arrange
        var service = CreateService();
        var query = "test query";
        var options = new RagSearchOptions { TenantId = "tenant-1", UseVectorSearch = true };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(query, options);

        // Assert - Should cache the generated embedding
        _embeddingCacheMock.Verify(
            x => x.SetEmbeddingForContentAsync(query, It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_VectorSearchDisabled_DoesNotUseCache()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions
        {
            TenantId = "tenant-1",
            UseVectorSearch = false,
            UseKeywordSearch = true
        };

        SetupMockSearchClient();

        // Act
        await service.SearchAsync("test query", options);

        // Assert - Should NOT check cache when vector search is disabled
        _embeddingCacheMock.Verify(
            x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region RagSearchOptions Tests

    [Fact]
    public void RagSearchOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        // Assert
        options.TopK.Should().Be(5);
        options.MinScore.Should().Be(0.7f);
        options.UseSemanticRanking.Should().BeTrue();
        options.UseVectorSearch.Should().BeTrue();
        options.UseKeywordSearch.Should().BeTrue();
        options.DeploymentId.Should().BeNull();
        options.KnowledgeSourceId.Should().BeNull();
        options.DocumentType.Should().BeNull();
        options.Tags.Should().BeNull();
    }

    #endregion

    #region RagSearchResponse Tests

    [Fact]
    public void RagSearchResponse_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var response = new RagSearchResponse();

        // Assert
        response.Query.Should().BeEmpty();
        response.Results.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
        response.SearchDurationMs.Should().Be(0);
        response.EmbeddingDurationMs.Should().Be(0);
        response.EmbeddingCacheHit.Should().BeFalse();
    }

    #endregion

    #region IndexResult Tests

    [Fact]
    public void IndexResult_Success_HasCorrectProperties()
    {
        // Arrange & Act
        var result = IndexResult.Success("doc-123");

        // Assert
        result.Id.Should().Be("doc-123");
        result.Succeeded.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void IndexResult_Failure_HasCorrectProperties()
    {
        // Arrange & Act
        var result = IndexResult.Failure("doc-123", "Index failed");

        // Assert
        result.Id.Should().Be("doc-123");
        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("Index failed");
    }

    #endregion

    #region Helper Methods

    private void SetupMockEmbedding()
    {
        // Setup cache as miss (returns null)
        _embeddingCacheMock
            .Setup(x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);

        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);
    }

    private void SetupMockEmbeddingCacheHit()
    {
        // Setup cache as hit (returns cached embedding)
        _embeddingCacheMock
            .Setup(x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);
    }

    private void SetupMockBatchEmbeddings(int count)
    {
        var embeddings = new List<ReadOnlyMemory<float>>();
        for (int i = 0; i < count; i++)
        {
            embeddings.Add(_testEmbedding);
        }

        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embeddings);
    }

    private void SetupMockSearchClient()
    {
        var searchClientMock = new Mock<SearchClient>();

        // Create empty search results
        var searchResults = SearchModelFactory.SearchResults<KnowledgeDocument>(
            values: new List<SearchResult<KnowledgeDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        var responseMock = Response.FromValue(searchResults, null!);

        searchClientMock
            .Setup(x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock);

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);
    }

    /// <summary>
    /// multi-container-multi-index-r1 FR-BFF-07 (task 014) — mock the 3-arg
    /// <c>GetSearchClientAsync(tenantId, indexName, ct)</c> overload (separate from the 2-arg
    /// overload mocked by <see cref="SetupMockSearchClient"/>) so tests can verify the
    /// thread-through is correctly routed to the explicit-index overload.
    /// </summary>
    private void SetupMockSearchClientWithExplicitIndex()
    {
        var searchClientMock = new Mock<SearchClient>();

        var searchResults = SearchModelFactory.SearchResults<KnowledgeDocument>(
            values: new List<SearchResult<KnowledgeDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        var responseMock = Response.FromValue(searchResults, null!);

        searchClientMock
            .Setup(x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock);

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);
    }

    private void SetupMockSearchClientByDeployment()
    {
        var searchClientMock = new Mock<SearchClient>();

        // Create empty search results
        var searchResults = SearchModelFactory.SearchResults<KnowledgeDocument>(
            values: new List<SearchResult<KnowledgeDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        var responseMock = Response.FromValue(searchResults, null!);

        searchClientMock
            .Setup(x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock);

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientByDeploymentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);
    }

    /// <summary>
    /// R5 task 002 — wire <c>SearchIndexClient.GetSearchClient(SessionFilesIndexName)</c>
    /// to return a mock <see cref="SearchClient"/> that returns empty results, capturing
    /// the <see cref="SearchOptions"/> passed to <c>SearchAsync</c> so tests can assert
    /// the OData filter contents (tenant + session clauses) and the absence of
    /// knowledge-source / privilege-group clauses.
    /// </summary>
    private Mock<SearchClient> SetupSessionFilesSearchClient(Action<SearchOptions>? capture = null)
    {
        var searchClientMock = new Mock<SearchClient>();

        var searchResults = SearchModelFactory.SearchResults<KnowledgeDocument>(
            values: new List<SearchResult<KnowledgeDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        var responseMock = Response.FromValue(searchResults, null!);

        searchClientMock
            .Setup(x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SearchOptions, CancellationToken>((_, opts, _) => capture?.Invoke(opts))
            .ReturnsAsync(responseMock);

        // IndexName is read by RagService.SearchAsync's LogRetrievalQuery call — supply
        // the session-files index name so the structured log emits the correct value.
        searchClientMock
            .SetupGet(x => x.IndexName)
            .Returns(SessionFilesIndexName);

        _searchIndexClientMock
            .Setup(c => c.GetSearchClient(SessionFilesIndexName))
            .Returns(searchClientMock.Object);

        return searchClientMock;
    }

    private void SetupMockSearchClientForIndexing()
    {
        var searchClientMock = new Mock<SearchClient>();

        // Create successful index result (key, errorMessage, succeeded, status)
        var indexResult = SearchModelFactory.IndexDocumentsResult(
            new List<AzureSdkIndexingResult>
            {
                SearchModelFactory.IndexingResult("doc-1", null, true, 201)
            });

        var responseMock = Response.FromValue(indexResult, null!);

        searchClientMock
            .Setup(x => x.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock);

        // SearchClient.Endpoint is a virtual property — Moq returns null by default,
        // which would NRE the observability LogInformation in RagService.IndexDocumentsBatchAsync.
        // Configure a benign test endpoint so the log call succeeds.
        searchClientMock
            .SetupGet(x => x.Endpoint)
            .Returns(new Uri("https://test-search.search.windows.net"));

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);

        // RagService.IndexDocumentsBatchAsync also resolves a KnowledgeDeploymentConfig
        // for observability logging (Model, IndexName, Endpoint, BatchSize). Provide a
        // minimal Shared-model config so the observability log call does not NRE.
        _deploymentServiceMock
            .Setup(x => x.GetDeploymentConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeDeploymentConfig
            {
                TenantId = "tenant-1",
                Name = "test-deployment",
                Model = RagDeploymentModel.Shared,
                IndexName = "spaarke-knowledge-index-v2",
                IsActive = true
            });
    }

    #endregion
}
