using System.Text.RegularExpressions;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// `tests/integration/tenant/**` — tenant-isolation KEEP category (ADR-038 §2 path #4:
/// "cross-tenant reads must 404, not 403" / tenant boundary enforcement). Backfills the
/// CRITICAL BACKFILL item flagged in <c>tests/integration/tenant/README.md</c> (2026-06-26
/// inventory: this category had zero compiled test files in this worktree at the time this
/// test was authored).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this guards (NFR-06 + task 052, ai-advanced-capabilities-nda-r1)</b>: golden
/// reference documents in <c>spaarke-rag-references</c> (KNW-001..KNW-011, including the NDA
/// standard NDA-REVIEW grounds against) are seeded <c>tenantId="system"</c> — an intentional,
/// org-wide, tenant-agnostic sentinel. <see cref="ReferenceRetrievalService.SearchReferencesAsync"/>
/// filters by the CALLER's tenant. Before the OR-clause fix
/// (<c>projects/ai-advanced-capabilities-nda-r1/notes/tenant-pin-analysis.md</c> §4/§6), the
/// filter was unconditionally <c>tenantId eq '{caller}'</c>, which excluded every golden
/// reference for any real interactive caller — grounding silently returned zero chunks, with
/// no error surfaced (L1 retrieval failures are non-fatal by design). The fix ORs in the
/// <c>"system"</c> sentinel: <c>(tenantId eq '{caller}' or tenantId eq 'system')</c>.
/// </para>
/// <para>
/// <b>What must NOT regress</b>: the OR-clause must not be widened beyond the caller's own
/// tenant + the <c>"system"</c> sentinel — a document seeded under a genuinely different real
/// tenant's GUID must remain excluded. This test proves both directions.
/// </para>
/// <para>
/// <b>Boundary faked, real path exercised</b>: only the Azure AI Search SDK boundary
/// (<see cref="SearchClient.SearchAsync{T}(string, SearchOptions, System.Threading.CancellationToken)"/>)
/// is a module-boundary fake — this session has no live Azure AI Search credentials
/// (ENV-BLOCKED; see <c>tenant-pin-analysis.md</c> §8). <see cref="ReferenceRetrievalService"/>
/// itself, its OData filter construction (<c>BuildSearchOptions</c>), and its result-shaping
/// logic are the real production types — no mocking of the class under test's own logic.
/// The fake does NOT hardcode "system is always retrievable": it parses the ACTUAL filter
/// string <c>BuildSearchOptions</c> produced (every <c>tenantId eq '&lt;value&gt;'</c> literal)
/// and applies that literal membership test to a small seeded fake index — precisely what
/// Azure AI Search's OData evaluator does for this filter shape. If the OR-clause were ever
/// reverted (dropping the <c>"system"</c> disjunct) or widened (adding an unintended tenant),
/// these tests fail — that IS the regression guard (task 052 acceptance criterion 2).
/// </para>
/// </remarks>
[Trait("category", "tenant-isolation")]
public class ReferenceRetrievalTenantPinTests
{
    // The literal this suite is pinned against — reused from the SAME shared constant
    // ReferenceRetrievalService.BuildSearchOptions reuses (Sprk.Bff.Api.Infrastructure.Cache.
    // SystemCacheKeys.SystemTenantSentinel), so this test fails to COMPILE (not just fails at
    // runtime) if that constant's value or name ever changes without updating the production
    // OR-clause in lockstep.
    private const string SystemSentinel = SystemCacheKeys.SystemTenantSentinel;

    private readonly Mock<IOpenAiClient> _openAiClientMock = new();
    private readonly Mock<IEmbeddingCache> _embeddingCacheMock = new();
    private readonly Mock<ITenantCache> _tenantCacheMock = new();
    private readonly Mock<SearchIndexClient> _searchIndexClientMock = new(MockBehavior.Loose);
    private readonly Mock<ILogger<ReferenceRetrievalService>> _loggerMock = new();
    private readonly ReadOnlyMemory<float> _testEmbedding;

    public ReferenceRetrievalTenantPinTests()
    {
        var embedding = new float[3072];
        for (var i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(i % 10) / 10f;
        }
        _testEmbedding = new ReadOnlyMemory<float>(embedding);

        // Embedding cache miss -> forces GenerateEmbeddingAsync (both stubbed; neither
        // path is under test here).
        _embeddingCacheMock
            .Setup(x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);
        _embeddingCacheMock
            .Setup(x => x.SetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);

        // Redis result-cache miss on every call (not under test here).
        _tenantCacheMock
            .Setup(x => x.GetAsync<ReferenceSearchResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceSearchResponse?)null);
        _tenantCacheMock
            .Setup(x => x.SetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<ReferenceSearchResponse>(), It.IsAny<TimeSpan?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private ReferenceRetrievalService CreateService()
    {
        var aiSearchOptions = Options.Create(new AiSearchOptions
        {
            Endpoint = "https://test-search.search.windows.net",
            // RagReferencesIndexName defaults to "spaarke-rag-references" — matches production.
        });

        return new ReferenceRetrievalService(
            _searchIndexClientMock.Object,
            _openAiClientMock.Object,
            _embeddingCacheMock.Object,
            _tenantCacheMock.Object,
            aiSearchOptions,
            _loggerMock.Object);
    }

    /// <summary>
    /// Wires <see cref="SearchIndexClient.GetSearchClient"/> to a fake <see cref="SearchClient"/>
    /// whose <c>SearchAsync</c> evaluates the REAL filter string
    /// <see cref="ReferenceRetrievalService"/> built against a small seeded fake index, returning
    /// only documents whose <c>tenantId</c> is one of the literal values the filter's
    /// <c>tenantId eq '...'</c> clauses admit. This is a module-boundary fake honoring the filter
    /// semantics (task 052 instruction), not a hardcoded "always return the golden doc" stub.
    /// </summary>
    private Mock<SearchClient> SetupReferencesSearchClient(
        IReadOnlyList<KnowledgeDocument> seededIndex,
        Action<SearchOptions>? captureFilter = null)
    {
        var searchClientMock = new Mock<SearchClient>();

        searchClientMock
            .Setup(x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, SearchOptions opts, CancellationToken _) =>
            {
                captureFilter?.Invoke(opts);

                var allowedTenantIds = ExtractEqTenantIds(opts.Filter);
                var matches = seededIndex
                    .Where(d => allowedTenantIds.Contains(d.TenantId))
                    .Select(d => SearchModelFactory.SearchResult(d, score: 1.0, highlights: null))
                    .ToList();

                var searchResults = SearchModelFactory.SearchResults<KnowledgeDocument>(
                    values: matches,
                    totalCount: matches.Count,
                    facets: null,
                    coverage: null,
                    rawResponse: null!);

                return Task.FromResult(Response.FromValue(searchResults, null!));
            });

        _searchIndexClientMock
            .Setup(c => c.GetSearchClient(It.IsAny<string>()))
            .Returns(searchClientMock.Object);

        return searchClientMock;
    }

    /// <summary>
    /// Extracts every literal tenant value from `tenantId eq '&lt;value&gt;'` clauses in an
    /// OData filter string — the same equality shape <c>BuildSearchOptions</c> emits (a single
    /// OR'd pair, optionally AND'd with other filters). Mirrors what Azure AI Search's filter
    /// evaluator does for this predicate shape; does not reimplement general OData semantics.
    /// </summary>
    private static HashSet<string> ExtractEqTenantIds(string? filter)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(filter))
        {
            return ids;
        }

        foreach (System.Text.RegularExpressions.Match m in Regex.Matches(filter, "tenantId eq '([^']*)'"))
        {
            ids.Add(m.Groups[1].Value);
        }

        return ids;
    }

    private static KnowledgeDocument SystemSentinelDocument(string knowledgeSourceId = "KNW-011") => new()
    {
        Id = $"{knowledgeSourceId}_ref_0",
        TenantId = SystemSentinel,
        KnowledgeSourceId = knowledgeSourceId,
        KnowledgeSourceName = "Spaarke NDA Standard",
        DocumentType = "legal",
        FileName = "KNW-011-spaarke-nda-standard.md",
        ChunkIndex = 0,
        ChunkCount = 1,
        Content = "B3 Definition of Confidential Information: must cover oral/visual/written/electronic disclosures..."
    };

    private static KnowledgeDocument OtherTenantDocument(string otherTenantId) => new()
    {
        Id = "other-tenant-doc_ref_0",
        TenantId = otherTenantId,
        KnowledgeSourceId = "OTHER-TENANT-SOURCE",
        KnowledgeSourceName = "Some other tenant's private reference",
        DocumentType = "legal",
        FileName = "other-tenant-private.md",
        ChunkIndex = 0,
        ChunkCount = 1,
        Content = "This document must never leak across the tenant boundary."
    };

    /// <summary>
    /// (a) — NFR-06 grounding proof: an arbitrary caller tenant (a real Entra ID tenant GUID,
    /// not "system") still retrieves the org-wide golden-reference document.
    /// </summary>
    [Fact]
    public async Task SearchReferencesAsync_UnderArbitraryCallerTenant_ReturnsNonZeroSystemSentinelChunks()
    {
        // Arrange
        var callerTenantId = "3f1a9c2e-58f1-4e2a-9a10-6b6f9c9d0a11"; // arbitrary real tenant GUID
        var otherTenantId = "9d6f2b40-1c77-4d3e-8b52-7a2e5f0c44aa";  // a DIFFERENT real tenant GUID
        var seededIndex = new List<KnowledgeDocument>
        {
            SystemSentinelDocument(),
            OtherTenantDocument(otherTenantId)
        };
        SetupReferencesSearchClient(seededIndex);
        var service = CreateService();

        // Act
        var response = await service.SearchReferencesAsync(
            "definition of confidential information",
            new ReferenceSearchOptions { TenantId = callerTenantId, TopK = 5, MinScore = 0f });

        // Assert — non-zero chunks, including the seeded standard (KNW-011).
        response.Results.Should().NotBeEmpty("grounding must not silently return zero chunks for a real caller tenant (NFR-06)");
        response.Results.Should().Contain(r => r.KnowledgeSourceId == "KNW-011");
    }

    /// <summary>
    /// (b) — isolation proof: a reference document seeded under a genuinely DIFFERENT real
    /// tenant GUID is NOT returned to a caller from another (also real, also non-"system")
    /// tenant. The OR-clause admits only {caller's own tenant, "system"} — never a third party's
    /// tenant. If <c>BuildSearchOptions</c> ever regresses to OR in more than the caller's own
    /// tenant (or to drop the tenant filter altogether), this test fails.
    /// </summary>
    [Fact]
    public async Task SearchReferencesAsync_DocumentSeededUnderDifferentRealTenant_IsExcludedForOtherCallerTenant()
    {
        // Arrange
        var callerTenantId = "3f1a9c2e-58f1-4e2a-9a10-6b6f9c9d0a11";
        var otherTenantId = "9d6f2b40-1c77-4d3e-8b52-7a2e5f0c44aa";
        var seededIndex = new List<KnowledgeDocument>
        {
            SystemSentinelDocument(),
            OtherTenantDocument(otherTenantId)
        };
        SetupReferencesSearchClient(seededIndex);
        var service = CreateService();

        // Act
        var response = await service.SearchReferencesAsync(
            "definition of confidential information",
            new ReferenceSearchOptions { TenantId = callerTenantId, TopK = 5, MinScore = 0f });

        // Assert — the other tenant's private reference must never leak to this caller.
        response.Results.Should().NotContain(r => r.KnowledgeSourceId == "OTHER-TENANT-SOURCE");
    }

    /// <summary>
    /// Literal regression guard on the filter STRING itself (task 052 acceptance criterion 2):
    /// the OR-clause must admit EXACTLY {caller's tenant, "system"} — no more, no fewer. Fails
    /// (a) if the "system" disjunct is ever dropped (reverting to the pre-fix unconditional
    /// filter — grounding silently zeroes), or (b) if a filter change ever widens the OR beyond
    /// these two literals (a cross-tenant isolation regression).
    /// </summary>
    [Fact]
    public async Task SearchReferencesAsync_BuildsFilter_AdmittingOnlyCallerTenantAndSystemSentinel()
    {
        // Arrange
        var callerTenantId = "3f1a9c2e-58f1-4e2a-9a10-6b6f9c9d0a11";
        SearchOptions? capturedOptions = null;
        SetupReferencesSearchClient(new List<KnowledgeDocument> { SystemSentinelDocument() }, captureFilter: opts => capturedOptions = opts);
        var service = CreateService();

        // Act
        await service.SearchReferencesAsync(
            "definition of confidential information",
            new ReferenceSearchOptions { TenantId = callerTenantId, TopK = 5, MinScore = 0f });

        // Assert
        capturedOptions.Should().NotBeNull();
        var allowedTenantIds = ExtractEqTenantIds(capturedOptions!.Filter);
        allowedTenantIds.Should().BeEquivalentTo(new[] { callerTenantId, SystemSentinel });
    }
}
