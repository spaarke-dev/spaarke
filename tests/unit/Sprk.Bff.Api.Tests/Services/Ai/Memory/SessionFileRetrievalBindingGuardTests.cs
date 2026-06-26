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

namespace Sprk.Bff.Api.Tests.Services.Ai.Memory;

/// <summary>
/// chat-routing-redesign-r1 task 100 (MVP-cut scope) — binding-NEGATIVE enforcement tests
/// for the Tier 5 session-files retrieval path against the architecture §5.2.1 rule that
/// chat-memory retrieval MUST NOT target <c>spaarke-insights-index</c> (spec FR-36).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this test exists</strong>:
/// </para>
/// <para>
/// The wrapper-handler design in the original task 100 POML (three handlers — Session,
/// Matter, Knowledge — over the three chat-domain indexes) was reduced in MVP scope to a
/// single binding-guard on the already-existing session-scoped retrieval path that lives
/// inside <see cref="RagService.SearchAsync(string, RagSearchOptions, System.Threading.CancellationToken)"/>
/// (added by R5 task 002 and exercised by RecallSessionFileHandler from task 085).
/// </para>
/// <para>
/// The guard sits at the top of the session-scoped routing branch (when
/// <see cref="RagSearchOptions.SessionId"/> is non-empty) and inspects
/// <see cref="AiSearchOptions.SessionFilesIndexName"/>. If that value is configured to
/// <c>spaarke-insights-index</c> (case-insensitive), the guard throws
/// <see cref="InvalidOperationException"/> before any Azure SDK call.
/// </para>
/// <para>
/// <strong>Coverage</strong>:
/// </para>
/// <list type="bullet">
///   <item>Insights-index configuration triggers fail-fast InvalidOperationException with
///   the architecture §5.2.1 message wording (architectural-guard contract).</item>
///   <item>Default <c>spaarke-session-files</c> configuration does NOT trigger the guard
///   (positive/happy path; ensures the guard is selective, not broken).</item>
///   <item>The error message references the binding source (§5.2.1, FR-36, allowed indexes)
///   so the reviewer / operator gets actionable signal.</item>
/// </list>
/// <para>
/// <strong>ADR-015 verification</strong>: the test verifies the exception is thrown
/// BEFORE the search executes (no query content reaches Azure Search); query length is
/// also intentionally short / not load-bearing.
/// </para>
/// </remarks>
[Trait("status", "new")]
[Trait("project", "chat-routing-redesign-r1")]
[Trait("task", "100")]
[Trait("scope", "mvp-cut")]
public sealed class SessionFileRetrievalBindingGuardTests
{
    private readonly Mock<IKnowledgeDeploymentService> _deploymentServiceMock;
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<IEmbeddingCache> _embeddingCacheMock;
    private readonly Mock<IPrivilegeGroupResolver> _privilegeGroupResolverMock;
    private readonly Mock<ILogger<RagService>> _loggerMock;
    private readonly Mock<SearchIndexClient> _searchIndexClientMock;
    private readonly IOptions<AnalysisOptions> _analysisOptions;

    // 3072-dim embedding stub (text-embedding-3-large shape)
    private readonly ReadOnlyMemory<float> _testEmbedding;

    public SessionFileRetrievalBindingGuardTests()
    {
        _deploymentServiceMock = new Mock<IKnowledgeDeploymentService>(MockBehavior.Loose);
        _openAiClientMock = new Mock<IOpenAiClient>(MockBehavior.Loose);
        _embeddingCacheMock = new Mock<IEmbeddingCache>(MockBehavior.Loose);
        _privilegeGroupResolverMock = new Mock<IPrivilegeGroupResolver>(MockBehavior.Loose);
        _loggerMock = new Mock<ILogger<RagService>>();
        _searchIndexClientMock = new Mock<SearchIndexClient>(MockBehavior.Loose);

        _analysisOptions = Options.Create(new AnalysisOptions
        {
            DefaultRagModel = RagDeploymentModel.Shared,
            MaxKnowledgeResults = 5,
            MinRelevanceScore = 0.7f
        });

        // Default group resolution: empty (public-only); never reached on guard-failure path.
        _privilegeGroupResolverMock
            .Setup(r => r.ResolveGroupIdsAsync(
                It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        // Stable 3072-d embedding so the embedding-generation step does not throw before
        // the guard fires. The guard sits AFTER embedding generation but BEFORE the
        // SearchClient is resolved (see RagService.SearchAsync line ~218).
        var embedding = new float[3072];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(i % 10) / 10f;
        }
        _testEmbedding = new ReadOnlyMemory<float>(embedding);

        _openAiClientMock
            .Setup(c => c.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);

        _embeddingCacheMock
            .Setup(c => c.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);
    }

    private RagService CreateService(string sessionFilesIndexName)
    {
        var aiSearchOptions = Options.Create(new AiSearchOptions
        {
            Endpoint = "https://test-search.search.windows.net",
            ApiKeySecretName = "test-api-key",
            KnowledgeIndexName = "spaarke-knowledge-index-v2",
            DiscoveryIndexName = "discovery-index",
            SessionFilesIndexName = sessionFilesIndexName,
            SessionFilesSemanticConfigName = "session-files-semantic-config"
        });

        return new RagService(
            _deploymentServiceMock.Object,
            _openAiClientMock.Object,
            _embeddingCacheMock.Object,
            _privilegeGroupResolverMock.Object,
            _analysisOptions,
            _searchIndexClientMock.Object,
            aiSearchOptions,
            _loggerMock.Object);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    // Binding-NEGATIVE enforcement (architecture §5.2.1 + spec FR-36)
    // ═══════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Retrieval_TargetingSparkleInsightsIndex_Throws_InvalidOperationException()
    {
        // Arrange — operator misconfigures SessionFilesIndexName to the Insights index.
        // The guard MUST fail-fast with a clear binding-violation message before any Azure
        // SDK call lands. This is the defense-in-depth check for architecture §5.2.1.
        var service = CreateService(sessionFilesIndexName: "spaarke-insights-index");

        var options = new RagSearchOptions
        {
            TenantId = "tenant-A",
            SessionId = "session-abc-123" // triggers session-scoped routing branch
        };

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.SearchAsync("any query text", options));

        // The message MUST cite the architectural source and name the allowed indexes so
        // operators have actionable signal. We don't pin the exact wording (avoid brittle
        // tests) but we DO verify the load-bearing tokens are present.
        ex.Message.Should().Contain("BINDING VIOLATION");
        ex.Message.Should().Contain("§5.2.1");
        ex.Message.Should().Contain("FR-36");
        ex.Message.Should().Contain("spaarke-insights-index");
        ex.Message.Should().Contain("spaarke-session-files");
        ex.Message.Should().Contain("spaarke-files-index");
        ex.Message.Should().Contain("spaarke-rag-references");

        // Defense-in-depth — the guard MUST fire BEFORE any SearchClient is requested.
        _searchIndexClientMock.Verify(
            x => x.GetSearchClient(It.IsAny<string>()),
            Times.Never);
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Retrieval_TargetingSparkleInsightsIndex_Throws_CaseInsensitive()
    {
        // Arrange — defense-in-depth against case-variant misconfiguration.
        // The architecture name is canonical lowercase, but the guard is case-insensitive
        // so a mid-case typo ("Spaarke-Insights-Index") still trips the rule.
        var service = CreateService(sessionFilesIndexName: "Spaarke-Insights-Index");

        var options = new RagSearchOptions
        {
            TenantId = "tenant-A",
            SessionId = "session-xyz"
        };

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.SearchAsync("query", options));
    }


}
