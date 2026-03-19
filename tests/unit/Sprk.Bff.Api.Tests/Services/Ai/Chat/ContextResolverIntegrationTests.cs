using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// End-to-end integration tests for <see cref="AnalysisChatContextResolver"/>
/// and the command catalog caching behaviour provided by <see cref="DynamicCommandResolver"/>.
///
/// Covers:
///   - NFR-03: Initialization time under 3 seconds with warm Redis cache.
///   - FR-16: Capability accuracy — different playbooks return different capability sets.
///   - ADR-009: IDistributedCache (Redis-first) caching with correct TTL.
///   - ADR-014: Tenant-scoped cache keys — two tenants never share cache entries.
///   - Cache hit/miss behaviour — Dataverse queried only once on first call.
///   - Cache invalidation — changed playbook yields fresh Dataverse query.
///   - Command catalog TTL — 5-minute absolute TTL on DynamicCommandResolver cache.
///
/// These tests mock <see cref="IGenericEntityService"/> for determinism and use
/// <see cref="MemoryDistributedCache"/> as the in-process <see cref="IDistributedCache"/> test double
/// (per knowledge pattern: no Redis infrastructure required).
/// </summary>
public class ContextResolverIntegrationTests
{
    // =========================================================================
    // Constants — tenants, analysis IDs, playbook GUIDs
    // =========================================================================

    private const string TenantA = "tenant-alpha";
    private const string TenantB = "tenant-bravo";

    private static readonly Guid AnalysisIdA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PlaybookIdContractReview = Guid.Parse("AAAA1111-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
    private static readonly Guid PlaybookIdPatentClaims = Guid.Parse("BBBB2222-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
    private static readonly Guid ScopeIdDefault = Guid.Parse("CCCC3333-CCCC-CCCC-CCCC-CCCCCCCCCCCC");
    private static readonly Guid SourceFileId = Guid.Parse("DDDD4444-DDDD-DDDD-DDDD-DDDDDDDDDDDD");

    /// <summary>
    /// Contract Review playbook capabilities: search + analyze + write_back + summarize.
    /// Option set values: 100000000, 100000001, 100000002, 100000006.
    /// </summary>
    private static readonly int[] ContractReviewCapabilities = [100000000, 100000001, 100000002, 100000006];

    /// <summary>
    /// Patent Claims playbook capabilities: search + analyze + selection_revise.
    /// Option set values: 100000000, 100000001, 100000004.
    /// </summary>
    private static readonly int[] PatentClaimsCapabilities = [100000000, 100000001, 100000004];

    // =========================================================================
    // Test infrastructure
    // =========================================================================

    private readonly Mock<IGenericEntityService> _entityServiceMock;
    private readonly MemoryDistributedCache _cache;
    private readonly ILogger<AnalysisChatContextResolver> _resolverLogger;
    private readonly AnalysisChatContextResolver _sut;

    public ContextResolverIntegrationTests()
    {
        _entityServiceMock = new Mock<IGenericEntityService>();
        _cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        _resolverLogger = new LoggerFactory().CreateLogger<AnalysisChatContextResolver>();

        _sut = new AnalysisChatContextResolver(
            _entityServiceMock.Object,
            _cache,
            _resolverLogger);
    }

    // =========================================================================
    // NFR-03: Initialization time — context resolution under 3 seconds (warm cache)
    // =========================================================================

    [Fact]
    public async Task ContextResolver_InitializesWithinThreeSeconds()
    {
        // Arrange — populate the cache with a pre-built response (warm cache scenario)
        var warmResponse = BuildContractReviewResponse();
        var cacheKey = AnalysisChatContextResolver.BuildCacheKey(TenantA, AnalysisIdA.ToString());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(warmResponse);
        await _cache.SetAsync(cacheKey, bytes, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = AnalysisChatContextResolver.ContextCacheTtl
        });

        // Act — time the resolution
        var stopwatch = Stopwatch.StartNew();
        var result = await _sut.ResolveAsync(
            AnalysisIdA.ToString(), TenantA, hostContext: null);
        stopwatch.Stop();

        // Assert — NFR-03: under 3 seconds with warm cache
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(3000,
            "NFR-03 requires context resolution to complete within 3 seconds with a warm Redis cache");
        result.Should().NotBeNull();
        result!.DefaultPlaybookName.Should().Be("Contract Review");
    }

    // =========================================================================
    // FR-16: Capability accuracy — different playbooks yield different capability sets
    // =========================================================================

    [Fact]
    public async Task CapabilityAccuracy_DifferentPlaybooksDifferentCapabilities()
    {
        // Arrange — set up two different playbooks with different capability sets
        var analysisIdContract = Guid.NewGuid();
        var analysisIdPatent = Guid.NewGuid();

        SetupDataverseForPlaybook(
            analysisIdContract, PlaybookIdContractReview,
            "Contract Review", ContractReviewCapabilities);

        SetupDataverseForPlaybook(
            analysisIdPatent, PlaybookIdPatentClaims,
            "Patent Claims", PatentClaimsCapabilities);

        SetupScopeQuery(); // Common scope query for both

        // Act — resolve context for each analysis (different playbooks)
        var contractResult = await _sut.ResolveAsync(
            analysisIdContract.ToString(), TenantA, hostContext: null);
        var patentResult = await _sut.ResolveAsync(
            analysisIdPatent.ToString(), TenantA, hostContext: null);

        // Assert — different playbooks return different capability sets (FR-16)
        contractResult.Should().NotBeNull();
        patentResult.Should().NotBeNull();

        var contractActionIds = contractResult!.InlineActions.Select(a => a.Id).ToHashSet();
        var patentActionIds = patentResult!.InlineActions.Select(a => a.Id).ToHashSet();

        contractActionIds.Should().NotBeEquivalentTo(patentActionIds,
            "FR-16: different playbooks must return different capability sets, not the same generic set for all");

        // Contract Review has write_back + summarize; Patent Claims does not
        contractActionIds.Should().Contain(PlaybookCapabilities.WriteBack);
        contractActionIds.Should().Contain(PlaybookCapabilities.Summarize);
        patentActionIds.Should().NotContain(PlaybookCapabilities.WriteBack);
        patentActionIds.Should().NotContain(PlaybookCapabilities.Summarize);

        // Patent Claims has selection_revise; Contract Review does not
        patentActionIds.Should().Contain(PlaybookCapabilities.SelectionRevise);
        contractActionIds.Should().NotContain(PlaybookCapabilities.SelectionRevise);
    }

    // =========================================================================
    // Cache HIT — second call does NOT query Dataverse
    // =========================================================================

    [Fact]
    public async Task CacheHit_ReturnsCachedResult()
    {
        // Arrange — set up Dataverse mock for a single analysis
        SetupDataverseForPlaybook(
            AnalysisIdA, PlaybookIdContractReview,
            "Contract Review", ContractReviewCapabilities);
        SetupScopeQuery();

        // Act — first call populates cache; second call should hit cache
        var firstResult = await _sut.ResolveAsync(
            AnalysisIdA.ToString(), TenantA, hostContext: null);
        var secondResult = await _sut.ResolveAsync(
            AnalysisIdA.ToString(), TenantA, hostContext: null);

        // Assert — both results are equivalent
        firstResult.Should().NotBeNull();
        secondResult.Should().NotBeNull();
        secondResult!.DefaultPlaybookId.Should().Be(firstResult!.DefaultPlaybookId);
        secondResult.DefaultPlaybookName.Should().Be(firstResult.DefaultPlaybookName);
        secondResult.InlineActions.Should().HaveCount(firstResult.InlineActions.Count);

        // Dataverse RetrieveAsync for the analysis output was called only ONCE
        // (the second call was served from cache)
        _entityServiceMock.Verify(
            e => e.RetrieveAsync(
                "sprk_analysisoutput",
                AnalysisIdA,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Cache hit should prevent a second Dataverse query for the same analysis");
    }

    // =========================================================================
    // Cache invalidation — changed playbook yields fresh Dataverse query
    // =========================================================================

    [Fact]
    public async Task CacheInvalidation_AfterContextChange()
    {
        // Arrange — set up Dataverse for Contract Review playbook
        SetupDataverseForPlaybook(
            AnalysisIdA, PlaybookIdContractReview,
            "Contract Review", ContractReviewCapabilities);
        SetupScopeQuery();

        // Act — first call populates cache
        var firstResult = await _sut.ResolveAsync(
            AnalysisIdA.ToString(), TenantA, hostContext: null);
        firstResult.Should().NotBeNull();
        firstResult!.DefaultPlaybookName.Should().Be("Contract Review");

        // Simulate cache invalidation: remove the cached entry
        var cacheKey = AnalysisChatContextResolver.BuildCacheKey(TenantA, AnalysisIdA.ToString());
        await _cache.RemoveAsync(cacheKey);

        // Reconfigure Dataverse to return Patent Claims playbook instead
        _entityServiceMock.Reset();
        SetupDataverseForPlaybook(
            AnalysisIdA, PlaybookIdPatentClaims,
            "Patent Claims", PatentClaimsCapabilities);
        SetupScopeQuery();

        // Act — second call after invalidation should query Dataverse again
        var secondResult = await _sut.ResolveAsync(
            AnalysisIdA.ToString(), TenantA, hostContext: null);

        // Assert — fresh result reflects the new playbook
        secondResult.Should().NotBeNull();
        secondResult!.DefaultPlaybookName.Should().Be("Patent Claims",
            "After cache invalidation, the resolver must query Dataverse for fresh data");

        // Dataverse was queried again for the analysis output
        _entityServiceMock.Verify(
            e => e.RetrieveAsync(
                "sprk_analysisoutput",
                AnalysisIdA,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "After cache invalidation, a fresh Dataverse query must occur");
    }

    // =========================================================================
    // ADR-014: Tenant scoping — separate cache entries per tenant
    // =========================================================================

    [Fact]
    public async Task TenantScoping_SeparateCacheEntries()
    {
        // Arrange — set up Dataverse for the same analysis ID under two tenants
        SetupDataverseForPlaybook(
            AnalysisIdA, PlaybookIdContractReview,
            "Contract Review", ContractReviewCapabilities);
        SetupScopeQuery();

        // Act — resolve for tenant A and tenant B using the same analysis ID
        var resultA = await _sut.ResolveAsync(
            AnalysisIdA.ToString(), TenantA, hostContext: null);
        var resultB = await _sut.ResolveAsync(
            AnalysisIdA.ToString(), TenantB, hostContext: null);

        // Assert — cache keys are different (tenant-scoped per ADR-014)
        var cacheKeyA = AnalysisChatContextResolver.BuildCacheKey(TenantA, AnalysisIdA.ToString());
        var cacheKeyB = AnalysisChatContextResolver.BuildCacheKey(TenantB, AnalysisIdA.ToString());

        cacheKeyA.Should().NotBe(cacheKeyB,
            "ADR-014: cache keys must be tenant-scoped — two different tenants must not share cache entries");

        cacheKeyA.Should().Contain(TenantA);
        cacheKeyB.Should().Contain(TenantB);

        // Both results should resolve successfully
        resultA.Should().NotBeNull();
        resultB.Should().NotBeNull();

        // Verify Dataverse was called twice (once per tenant, separate cache entries)
        _entityServiceMock.Verify(
            e => e.RetrieveAsync(
                "sprk_analysisoutput",
                AnalysisIdA,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "Each tenant should trigger a separate Dataverse query due to separate cache entries");
    }

    // =========================================================================
    // Cache TTL — 30-minute absolute TTL for AnalysisChatContextResolver
    // =========================================================================

    [Fact]
    public void CacheTtl_ThirtyMinutes_ForContextResolver()
    {
        // Assert — AnalysisChatContextResolver uses 30-minute absolute TTL (ADR-009)
        AnalysisChatContextResolver.ContextCacheTtl.Should().Be(TimeSpan.FromMinutes(30),
            "ADR-009: analysis context cache entries must have a 30-minute absolute TTL");
    }

    // =========================================================================
    // Cache TTL — 5-minute absolute TTL for DynamicCommandResolver
    // =========================================================================

    [Fact]
    public void CacheTtl_FiveMinutes_ForCommandCatalog()
    {
        // Assert — DynamicCommandResolver uses 5-minute absolute TTL (ADR-009)
        DynamicCommandResolver.CatalogCacheTtl.Should().Be(TimeSpan.FromMinutes(5),
            "ADR-009: command catalog cache entries must have a 5-minute absolute TTL");

        // Verify exact seconds value for precision
        DynamicCommandResolver.CatalogCacheTtl.TotalSeconds.Should().Be(300,
            "5-minute TTL = 300 seconds");
    }

    // =========================================================================
    // ADR-009: Verify resolver uses IDistributedCache, not in-memory Dictionary
    // =========================================================================

    [Fact]
    public async Task UsesIDistributedCache_NotInMemoryDictionary()
    {
        // Arrange — use a mock to verify IDistributedCache interactions
        var cacheMock = new Mock<IDistributedCache>();
        var analysisId = AnalysisIdA.ToString();
        var cacheKey = AnalysisChatContextResolver.BuildCacheKey(TenantA, analysisId);

        // Cache miss
        cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Allow cache write
        cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        SetupDataverseForPlaybook(
            AnalysisIdA, PlaybookIdContractReview,
            "Contract Review", ContractReviewCapabilities);
        SetupScopeQuery();

        var resolver = new AnalysisChatContextResolver(
            _entityServiceMock.Object,
            cacheMock.Object,
            _resolverLogger);

        // Act
        await resolver.ResolveAsync(analysisId, TenantA, hostContext: null);

        // Assert — IDistributedCache.GetAsync was called (not some internal Dictionary)
        cacheMock.Verify(
            c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()),
            Times.Once,
            "ADR-009: resolver must use IDistributedCache.GetAsync (Redis-first), not an in-memory Dictionary");

        // Assert — IDistributedCache.SetAsync was called with correct TTL options
        cacheMock.Verify(
            c => c.SetAsync(
                cacheKey,
                It.IsAny<byte[]>(),
                It.Is<DistributedCacheEntryOptions>(opts =>
                    opts.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(30)),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "ADR-009: resolver must cache via IDistributedCache.SetAsync with 30-minute absolute TTL");
    }

    // =========================================================================
    // DynamicCommandResolver: cache key tenant scoping
    // =========================================================================

    [Fact]
    public void CommandResolver_CacheKey_IsTenantScoped()
    {
        // Act
        var keyA = DynamicCommandResolver.BuildCacheKey(TenantA, "sprk_matter");
        var keyB = DynamicCommandResolver.BuildCacheKey(TenantB, "sprk_matter");

        // Assert — different tenants produce different cache keys (ADR-014)
        keyA.Should().NotBe(keyB,
            "ADR-014: command catalog cache keys must be tenant-scoped");
        keyA.Should().Contain(TenantA);
        keyB.Should().Contain(TenantB);
    }

    // =========================================================================
    // DynamicCommandResolver: command catalog cache hit avoids Dataverse query
    // =========================================================================

    [Fact]
    public async Task CommandResolver_CacheHit_SkipsDataverse()
    {
        // Arrange — pre-populate the command catalog cache
        var commandResolver = new DynamicCommandResolver(
            _entityServiceMock.Object,
            _cache,
            new LoggerFactory().CreateLogger<DynamicCommandResolver>());

        var hostContext = new ChatHostContext("sprk_matter", Guid.NewGuid().ToString());

        // Set up Dataverse mocks for first call
        SetupPlaybookQueryForEntityType("sprk_matter");
        SetupScopeCapabilityQuery();

        // Act — first call builds the catalog
        var firstResult = await commandResolver.ResolveCommandsAsync(TenantA, hostContext);

        // Reset mocks to detect second call
        _entityServiceMock.Invocations.Clear();

        // Act — second call should hit cache
        var secondResult = await commandResolver.ResolveCommandsAsync(TenantA, hostContext);

        // Assert — second call did NOT query Dataverse (cache hit)
        _entityServiceMock.Verify(
            e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "On cache hit, DynamicCommandResolver should not query Dataverse");

        firstResult.Should().NotBeNull();
        secondResult.Should().NotBeNull();
        secondResult.Count.Should().Be(firstResult.Count);
    }

    // =========================================================================
    // Full pipeline: context response includes commands, search guidance, scope metadata
    // =========================================================================

    [Fact]
    public async Task FullPipeline_AssemblesCompleteResponse()
    {
        // Arrange — full Dataverse setup with playbook, scopes, and entity context
        SetupDataverseForPlaybook(
            AnalysisIdA, PlaybookIdContractReview,
            "Contract Review", ContractReviewCapabilities);
        SetupScopeQuery(searchGuidance: "Focus on contractual obligations and liability clauses");

        var hostContext = new ChatHostContext("sprk_matter", Guid.NewGuid().ToString());

        // Act
        var result = await _sut.ResolveAsync(
            AnalysisIdA.ToString(), TenantA, hostContext);

        // Assert — full response assembled with all components
        result.Should().NotBeNull();
        result!.DefaultPlaybookId.Should().Be(PlaybookIdContractReview.ToString());
        result.DefaultPlaybookName.Should().Be("Contract Review");
        result.AvailablePlaybooks.Should().HaveCount(1);
        result.InlineActions.Should().HaveCount(ContractReviewCapabilities.Length);
        result.AnalysisContext.Should().NotBeNull();
        result.AnalysisContext.AnalysisId.Should().Be(AnalysisIdA.ToString());
        result.SearchGuidance.Should().Be("Focus on contractual obligations and liability clauses");
        result.ScopeMetadata.Should().NotBeNull();
        result.ScopeMetadata!.ScopeName.Should().Be("Default Scope");
    }

    // =========================================================================
    // Helper: Setup Dataverse mocks for a specific playbook
    // =========================================================================

    /// <summary>
    /// Configures <see cref="IGenericEntityService"/> mocks for the full 6-step
    /// Dataverse resolution pipeline in <see cref="AnalysisChatContextResolver"/>.
    /// </summary>
    private void SetupDataverseForPlaybook(
        Guid analysisId,
        Guid playbookId,
        string playbookName,
        int[] capabilityValues)
    {
        // Step 1: sprk_analysisoutput retrieval
        var analysisOutput = new Entity("sprk_analysisoutput", analysisId);
        analysisOutput["sprk_analysisplaybookid"] = new EntityReference("sprk_analysisplaybook", playbookId);
        analysisOutput["sprk_analysistype"] = "contract_review";
        analysisOutput["sprk_spefileid"] = new EntityReference("sprk_spefile", SourceFileId);
        analysisOutput["sprk_containerid"] = "container-001";

        _entityServiceMock
            .Setup(e => e.RetrieveAsync(
                "sprk_analysisoutput",
                analysisId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysisOutput);

        // Step 2: sprk_analysisplaybook retrieval
        var playbookEntity = new Entity("sprk_analysisplaybook", playbookId);
        playbookEntity["sprk_name"] = playbookName;
        playbookEntity["sprk_description"] = $"{playbookName} playbook description";
        playbookEntity["sprk_playbookcapabilities"] = new OptionSetValueCollection(
            capabilityValues.Select(v => new OptionSetValue(v)).ToList());
        playbookEntity["sprk_recordtype"] = "sprk_matter";
        playbookEntity["sprk_entitytype"] = "sprk_matter";
        playbookEntity["sprk_tags"] = "legal,contract";

        _entityServiceMock
            .Setup(e => e.RetrieveAsync(
                "sprk_analysisplaybook",
                playbookId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbookEntity);
    }

    /// <summary>
    /// Sets up the sprk_scope query for <see cref="AnalysisChatContextResolver.QueryActiveScopesAsync"/>.
    /// </summary>
    private void SetupScopeQuery(string? searchGuidance = null)
    {
        var scopeEntity = new Entity("sprk_scope", ScopeIdDefault);
        scopeEntity["sprk_name"] = "Default Scope";
        scopeEntity["sprk_description"] = "Default analysis scope";
        scopeEntity["sprk_searchguidance"] = searchGuidance ?? "Search relevant documents";
        scopeEntity["sprk_focusarea"] = "legal";
        scopeEntity["sprk_capabilities"] = "100000000,100000001";

        var scopeCollection = new EntityCollection(new List<Entity> { scopeEntity });

        _entityServiceMock
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_scope"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopeCollection);
    }

    /// <summary>
    /// Sets up playbook query for <see cref="DynamicCommandResolver.GetPlaybookCommandsAsync"/>.
    /// </summary>
    private void SetupPlaybookQueryForEntityType(string entityType)
    {
        var playbookEntity = new Entity("sprk_analysisplaybook", PlaybookIdContractReview);
        playbookEntity["sprk_name"] = "Contract Review";
        playbookEntity["sprk_description"] = "Review contracts";
        playbookEntity["sprk_triggerphrases"] = "review contract\nanalyze agreement";
        playbookEntity["sprk_analysisplaybookid"] = PlaybookIdContractReview;

        var collection = new EntityCollection(new List<Entity> { playbookEntity });

        _entityServiceMock
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q =>
                    q.EntityName == "sprk_analysisplaybook"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
    }

    /// <summary>
    /// Sets up scope capability query for <see cref="DynamicCommandResolver.GetScopeCapabilityCommandsAsync"/>.
    /// </summary>
    private void SetupScopeCapabilityQuery()
    {
        var scopeEntity = new Entity("sprk_scope", ScopeIdDefault);
        scopeEntity["sprk_name"] = "Default Scope";
        scopeEntity["sprk_capabilities"] = new OptionSetValueCollection(
            new List<OptionSetValue>
            {
                new(100000000), // search
                new(100000004), // summarize
            });

        var collection = new EntityCollection(new List<Entity> { scopeEntity });

        _entityServiceMock
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_scope"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
    }

    /// <summary>
    /// Builds a pre-built <see cref="AnalysisChatContextResponse"/> for cache warm-up scenarios.
    /// </summary>
    private static AnalysisChatContextResponse BuildContractReviewResponse()
    {
        var inlineActions = ContractReviewCapabilities
            .Where(v => AnalysisChatContextResolver.CapabilityToActionMap.ContainsKey(v))
            .Select(v => AnalysisChatContextResolver.CapabilityToActionMap[v])
            .ToList();

        return new AnalysisChatContextResponse(
            DefaultPlaybookId: PlaybookIdContractReview.ToString(),
            DefaultPlaybookName: "Contract Review",
            AvailablePlaybooks: [new AnalysisPlaybookInfo(
                PlaybookIdContractReview.ToString(),
                "Contract Review",
                "Contract Review playbook description")],
            InlineActions: inlineActions,
            KnowledgeSources: [],
            AnalysisContext: new AnalysisContextInfo(
                AnalysisId: AnalysisIdA.ToString(),
                AnalysisType: "contract_review",
                MatterType: null,
                PracticeArea: null,
                SourceFileId: SourceFileId.ToString(),
                SourceContainerId: "container-001"),
            Commands: DynamicCommandResolver.SystemCommands.ToList(),
            SearchGuidance: "Search relevant documents",
            ScopeMetadata: new AnalysisScopeMetadata(
                ScopeId: ScopeIdDefault.ToString(),
                ScopeName: "Default Scope",
                Description: "Default analysis scope",
                FocusArea: "legal"));
    }
}
