using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="ChatContextMappingService"/>.
///
/// Verifies:
/// - Four-tier resolution precedence: exact match, entity+any, wildcard+pageType, global fallback.
/// - Redis cache hit returns cached response without querying Dataverse.
/// - Redis cache miss queries Dataverse and caches the result with 30-minute sliding TTL.
/// - Default playbook selection: first with isDefault=true, or first record if none marked.
/// - Multiple available playbooks in response.
/// - Empty result when no mappings exist at all.
/// - Sort order within tier (sprk_sortorder ASC).
/// - Cache key format: "chat:ctx-mapping:{entityType}:{pageType}".
/// </summary>
public class ChatContextMappingServiceTests
{
    private const string TenantId = "tenant-test";
    private const string EntityType = "sprk_matter";
    private const string PageType = "main";

    private static readonly Guid PlaybookIdA = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
    private static readonly Guid PlaybookIdB = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
    private static readonly Guid PlaybookIdC = Guid.Parse("CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC");

    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<IGenericEntityService> _genericEntityServiceMock;
    private readonly Mock<ILogger<ChatContextMappingService>> _loggerMock;
    private readonly ChatContextMappingService _sut;

    public ChatContextMappingServiceTests()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _genericEntityServiceMock = new Mock<IGenericEntityService>();
        _loggerMock = new Mock<ILogger<ChatContextMappingService>>();
        _sut = new ChatContextMappingService(
            _cacheMock.Object,
            _genericEntityServiceMock.Object,
            _loggerMock.Object);
    }

    // =========================================================================
    // Cache key and TTL constants
    // =========================================================================

    [Fact]
    public void BuildCacheKey_ProducesExpectedPattern()
    {
        var key = ChatContextMappingService.BuildCacheKey("sprk_matter", "main");
        key.Should().Be("chat:ctx-mapping:sprk_matter:main");
    }

    [Fact]
    public void BuildCacheKey_UsesAny_WhenPageTypeIsNull()
    {
        var key = ChatContextMappingService.BuildCacheKey("sprk_matter", null);
        key.Should().Be("chat:ctx-mapping:sprk_matter:any");
    }

    [Fact]
    public void MappingCacheTtl_Is30Minutes()
    {
        ChatContextMappingService.MappingCacheTtl.Should().Be(TimeSpan.FromMinutes(30));
    }

    // =========================================================================
    // Cache HIT — returns cached response, no Dataverse query
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ReturnsCachedResponse_OnRedisHit_WithoutQueryingDataverse()
    {
        // Arrange
        var cachedResponse = new ChatContextMappingResponse(
            new ChatPlaybookInfo(PlaybookIdA, "Cached Playbook", "From cache"),
            [new ChatPlaybookInfo(PlaybookIdA, "Cached Playbook", "From cache")]);

        var cacheKey = ChatContextMappingService.BuildCacheKey(EntityType, PageType);
        var cachedBytes = JsonSerializer.SerializeToUtf8Bytes(cachedResponse);

        _cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedBytes);
        _cacheMock
            .Setup(c => c.RefreshAsync(cacheKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert
        result.Should().NotBeNull();
        result.DefaultPlaybook.Should().NotBeNull();
        result.DefaultPlaybook!.Id.Should().Be(PlaybookIdA);
        result.DefaultPlaybook.Name.Should().Be("Cached Playbook");
        result.AvailablePlaybooks.Should().HaveCount(1);

        // Dataverse MUST NOT be called on a cache hit
        _genericEntityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_RefreshesSlidingTtl_OnRedisHit()
    {
        // Arrange
        var cachedResponse = new ChatContextMappingResponse(
            new ChatPlaybookInfo(PlaybookIdA, "Playbook", null),
            [new ChatPlaybookInfo(PlaybookIdA, "Playbook", null)]);

        var cacheKey = ChatContextMappingService.BuildCacheKey(EntityType, PageType);
        var cachedBytes = JsonSerializer.SerializeToUtf8Bytes(cachedResponse);

        _cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedBytes);
        _cacheMock
            .Setup(c => c.RefreshAsync(cacheKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert — sliding TTL is refreshed via RefreshAsync
        _cacheMock.Verify(c => c.RefreshAsync(cacheKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // Cache MISS — queries Dataverse, caches result
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_QueriesDataverse_OnCacheMiss_AndCachesResult()
    {
        // Arrange
        SetupCacheMiss();
        SetupCacheSetSuccess();

        var entities = CreateEntityCollection(
            CreateMappingEntity(PlaybookIdA, "Playbook A", "Description A", isDefault: true, sortOrder: 1));

        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entities);

        // Act
        var result = await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert — Dataverse was queried
        _genericEntityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        // Assert — result was cached in Redis
        _cacheMock.Verify(c => c.SetAsync(
            It.Is<string>(k => k == ChatContextMappingService.BuildCacheKey(EntityType, PageType)),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);

        result.DefaultPlaybook.Should().NotBeNull();
        result.DefaultPlaybook!.Name.Should().Be("Playbook A");
    }

    [Fact]
    public async Task ResolveAsync_CachesWithSlidingTtl_Of30Minutes()
    {
        // Arrange
        SetupCacheMiss();

        DistributedCacheEntryOptions? capturedOptions = null;
        _cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (_, _, opts, _) => capturedOptions = opts)
            .Returns(Task.CompletedTask);

        var entities = CreateEntityCollection(
            CreateMappingEntity(PlaybookIdA, "Playbook A", null, isDefault: true, sortOrder: 1));

        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entities);

        // Act
        await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert — 30-minute sliding TTL (ADR-009)
        capturedOptions.Should().NotBeNull();
        capturedOptions!.SlidingExpiration.Should().Be(TimeSpan.FromMinutes(30));
    }

    // =========================================================================
    // Resolution precedence: Tier 1 — Exact match (entityType + pageType)
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ExactMatch_ReturnsResultFromFirstQuery()
    {
        // Arrange
        SetupCacheMiss();
        SetupCacheSetSuccess();

        var entities = CreateEntityCollection(
            CreateMappingEntity(PlaybookIdA, "Exact Match", "For sprk_matter + main", isDefault: true, sortOrder: 1));

        // Only the first query (exact match) returns results
        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => HasCondition(q, "sprk_entitytype", EntityType)
                                            && HasCondition(q, "sprk_pagetype", PageType)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entities);

        // Act
        var result = await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert
        result.DefaultPlaybook.Should().NotBeNull();
        result.DefaultPlaybook!.Name.Should().Be("Exact Match");
        result.AvailablePlaybooks.Should().HaveCount(1);

        // Dataverse should only be called once (exact match succeeded)
        _genericEntityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // Resolution precedence: Tier 2 — Entity + any (entityType + "any")
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_FallsBackToEntityPlusAny_WhenExactMatchEmpty()
    {
        // Arrange
        SetupCacheMiss();
        SetupCacheSetSuccess();

        var emptyCollection = CreateEntityCollection();
        var entityAnyCollection = CreateEntityCollection(
            CreateMappingEntity(PlaybookIdB, "Entity+Any", null, isDefault: true, sortOrder: 1));

        var callCount = 0;
        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? emptyCollection : entityAnyCollection;
            });

        // Act
        var result = await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert
        result.DefaultPlaybook.Should().NotBeNull();
        result.DefaultPlaybook!.Name.Should().Be("Entity+Any");

        // Tier 1 (exact) + Tier 2 (entity+any) = 2 calls
        _genericEntityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // =========================================================================
    // Resolution precedence: Tier 3 — Wildcard + pageType ("*" + pageType)
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_FallsBackToWildcardPlusPageType_WhenTier1And2Empty()
    {
        // Arrange
        SetupCacheMiss();
        SetupCacheSetSuccess();

        var emptyCollection = CreateEntityCollection();
        var wildcardPageTypeCollection = CreateEntityCollection(
            CreateMappingEntity(PlaybookIdC, "Wildcard+PageType", null, isDefault: true, sortOrder: 1));

        var callCount = 0;
        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // Call 1: exact match (empty), Call 2: entity+any (empty), Call 3: wildcard+pageType (hit)
                return callCount <= 2 ? emptyCollection : wildcardPageTypeCollection;
            });

        // Act
        var result = await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert
        result.DefaultPlaybook.Should().NotBeNull();
        result.DefaultPlaybook!.Name.Should().Be("Wildcard+PageType");

        // Tier 1 + Tier 2 + Tier 3 = 3 calls
        _genericEntityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    // =========================================================================
    // Resolution precedence: Tier 4 — Global fallback ("*" + "any")
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_FallsBackToGlobalFallback_WhenTier1Through3Empty()
    {
        // Arrange
        SetupCacheMiss();
        SetupCacheSetSuccess();

        var emptyCollection = CreateEntityCollection();
        var globalFallbackCollection = CreateEntityCollection(
            CreateMappingEntity(PlaybookIdA, "Global Fallback", "Catch-all", isDefault: true, sortOrder: 1));

        var callCount = 0;
        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // Calls 1-3 return empty, call 4 returns global fallback
                return callCount <= 3 ? emptyCollection : globalFallbackCollection;
            });

        // Act
        var result = await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert
        result.DefaultPlaybook.Should().NotBeNull();
        result.DefaultPlaybook!.Name.Should().Be("Global Fallback");
        result.DefaultPlaybook.Description.Should().Be("Catch-all");

        // Tier 1 + Tier 2 + Tier 3 + Tier 4 = 4 calls
        _genericEntityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }

    // =========================================================================
    // Resolution: pageType is null — skips Tier 2 and Tier 3
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_WhenPageTypeIsNull_SkipsTier2And3_JumpsStraightToTier4()
    {
        // When pageType is null, effectivePageType is "any".
        // Tier 1: entityType + "any" (exact match IS the entity+any tier)
        // Tier 2: skipped (effectivePageType == "any")
        // Tier 3: skipped (effectivePageType == "any")
        // Tier 4: "*" + "any" (global fallback)

        // Arrange
        SetupCacheMiss();
        SetupCacheSetSuccess();

        var emptyCollection = CreateEntityCollection();
        var globalCollection = CreateEntityCollection(
            CreateMappingEntity(PlaybookIdA, "Global", null, isDefault: true, sortOrder: 1));

        var callCount = 0;
        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // Call 1: entityType + "any" (empty), Call 2: "*" + "any" (hit)
                return callCount == 1 ? emptyCollection : globalCollection;
            });

        // Act
        var result = await _sut.ResolveAsync(EntityType, null, TenantId);

        // Assert
        result.DefaultPlaybook.Should().NotBeNull();
        result.DefaultPlaybook!.Name.Should().Be("Global");

        // Only 2 queries: Tier 1 (entity+any) and Tier 4 (global)
        _genericEntityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // =========================================================================
    // Empty result — no mappings exist at all
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ReturnsEmptyResponse_WhenNoMappingsExistAtAll()
    {
        // Arrange
        SetupCacheMiss();
        SetupCacheSetSuccess();

        var emptyCollection = CreateEntityCollection();
        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyCollection);

        // Act
        var result = await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert
        result.DefaultPlaybook.Should().BeNull();
        result.AvailablePlaybooks.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_CachesEmptyResponse_ToAvoidRepeatedDataverseMisses()
    {
        // Arrange
        SetupCacheMiss();

        byte[]? capturedBytes = null;
        _cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (_, bytes, _, _) => capturedBytes = bytes)
            .Returns(Task.CompletedTask);

        var emptyCollection = CreateEntityCollection();
        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyCollection);

        // Act
        await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert — empty response IS cached (prevents repeated misses)
        _cacheMock.Verify(c => c.SetAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        capturedBytes.Should().NotBeNull();
        var deserialized = JsonSerializer.Deserialize<ChatContextMappingResponse>(capturedBytes!);
        deserialized.Should().NotBeNull();
        deserialized!.DefaultPlaybook.Should().BeNull();
        deserialized.AvailablePlaybooks.Should().BeEmpty();
    }

    // =========================================================================
    // Default playbook selection
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_SelectsFirstIsDefaultTrue_AsDefaultPlaybook()
    {
        // Arrange
        SetupCacheMiss();
        SetupCacheSetSuccess();

        var entities = CreateEntityCollection(
            CreateMappingEntity(PlaybookIdA, "Non-Default First", null, isDefault: false, sortOrder: 1),
            CreateMappingEntity(PlaybookIdB, "Default Second", null, isDefault: true, sortOrder: 2),
            CreateMappingEntity(PlaybookIdC, "Non-Default Third", null, isDefault: false, sortOrder: 3));

        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entities);

        // Act
        var result = await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert — default playbook is the one with isDefault=true
        result.DefaultPlaybook.Should().NotBeNull();
        result.DefaultPlaybook!.Id.Should().Be(PlaybookIdB);
        result.DefaultPlaybook.Name.Should().Be("Default Second");
    }

    [Fact]
    public async Task ResolveAsync_SelectsFirstRecord_AsDefault_WhenNoneMarkedDefault()
    {
        // Arrange
        SetupCacheMiss();
        SetupCacheSetSuccess();

        var entities = CreateEntityCollection(
            CreateMappingEntity(PlaybookIdA, "First (sort=1)", null, isDefault: false, sortOrder: 1),
            CreateMappingEntity(PlaybookIdB, "Second (sort=2)", null, isDefault: false, sortOrder: 2));

        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entities);

        // Act
        var result = await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert — first record becomes default when none are marked
        result.DefaultPlaybook.Should().NotBeNull();
        result.DefaultPlaybook!.Id.Should().Be(PlaybookIdA);
        result.DefaultPlaybook.Name.Should().Be("First (sort=1)");
    }

    // =========================================================================
    // Multiple available playbooks
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ReturnsAllPlaybooks_InAvailablePlaybooks()
    {
        // Arrange
        SetupCacheMiss();
        SetupCacheSetSuccess();

        var entities = CreateEntityCollection(
            CreateMappingEntity(PlaybookIdA, "Alpha", "Desc A", isDefault: true, sortOrder: 1),
            CreateMappingEntity(PlaybookIdB, "Beta", "Desc B", isDefault: false, sortOrder: 2),
            CreateMappingEntity(PlaybookIdC, "Gamma", "Desc C", isDefault: false, sortOrder: 3));

        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entities);

        // Act
        var result = await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert
        result.AvailablePlaybooks.Should().HaveCount(3);
        result.AvailablePlaybooks[0].Name.Should().Be("Alpha");
        result.AvailablePlaybooks[1].Name.Should().Be("Beta");
        result.AvailablePlaybooks[2].Name.Should().Be("Gamma");

        result.DefaultPlaybook.Should().NotBeNull();
        result.DefaultPlaybook!.Id.Should().Be(PlaybookIdA);
    }

    // =========================================================================
    // Sort order within tier (sprk_sortorder ASC)
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_QueryExpression_OrdersBySortOrderAscending()
    {
        // Arrange
        SetupCacheMiss();
        SetupCacheSetSuccess();

        QueryExpression? capturedQuery = null;
        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => capturedQuery = q)
            .ReturnsAsync(CreateEntityCollection(
                CreateMappingEntity(PlaybookIdA, "Playbook", null, isDefault: true, sortOrder: 1)));

        // Act
        await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert — query must include ORDER BY sprk_sortorder ASC
        capturedQuery.Should().NotBeNull();
        capturedQuery!.Orders.Should().ContainSingle();
        capturedQuery.Orders[0].AttributeName.Should().Be("sprk_sortorder");
        capturedQuery.Orders[0].OrderType.Should().Be(OrderType.Ascending);
    }

    [Fact]
    public async Task ResolveAsync_QueryExpression_FiltersActiveRecordsOnly()
    {
        // Arrange
        SetupCacheMiss();
        SetupCacheSetSuccess();

        QueryExpression? capturedQuery = null;
        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => capturedQuery = q)
            .ReturnsAsync(CreateEntityCollection(
                CreateMappingEntity(PlaybookIdA, "Playbook", null, isDefault: true, sortOrder: 1)));

        // Act
        await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert — query must filter statecode = 0 (Active)
        capturedQuery.Should().NotBeNull();
        capturedQuery!.Criteria.Conditions
            .Should().Contain(c => c.AttributeName == "statecode" && (int)c.Values[0] == 0);
    }

    [Fact]
    public async Task ResolveAsync_QueryExpression_LinksToPlaybookEntity()
    {
        // Arrange
        SetupCacheMiss();
        SetupCacheSetSuccess();

        QueryExpression? capturedQuery = null;
        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => capturedQuery = q)
            .ReturnsAsync(CreateEntityCollection(
                CreateMappingEntity(PlaybookIdA, "Playbook", null, isDefault: true, sortOrder: 1)));

        // Act
        await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert — query links to sprk_analysisplaybook with alias "pb"
        capturedQuery.Should().NotBeNull();
        capturedQuery!.LinkEntities.Should().ContainSingle();
        var link = capturedQuery.LinkEntities[0];
        link.LinkToEntityName.Should().Be("sprk_analysisplaybook");
        link.LinkFromAttributeName.Should().Be("sprk_playbookid");
        link.LinkToAttributeName.Should().Be("sprk_analysisplaybookid");
        link.EntityAlias.Should().Be("pb");
    }

    // =========================================================================
    // Dataverse exception handling — graceful degradation
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ReturnsEmptyResponse_WhenDataverseThrows()
    {
        // Arrange — the entity may not be deployed yet
        SetupCacheMiss();
        SetupCacheSetSuccess();

        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Entity sprk_aichatcontextmapping not found"));

        // Act
        var result = await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert — graceful degradation: empty response, not exception
        result.DefaultPlaybook.Should().BeNull();
        result.AvailablePlaybooks.Should().BeEmpty();
    }

    // =========================================================================
    // Edge case: playbook with null description
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_HandlesPlaybookWithNullDescription()
    {
        // Arrange
        SetupCacheMiss();
        SetupCacheSetSuccess();

        var entities = CreateEntityCollection(
            CreateMappingEntity(PlaybookIdA, "No Description", null, isDefault: true, sortOrder: 1));

        _genericEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entities);

        // Act
        var result = await _sut.ResolveAsync(EntityType, PageType, TenantId);

        // Assert
        result.DefaultPlaybook.Should().NotBeNull();
        result.DefaultPlaybook!.Description.Should().BeNull();
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private void SetupCacheMiss()
    {
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
    }

    private void SetupCacheSetSuccess()
    {
        _cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>
    /// Creates a Dataverse entity record that mimics the shape of a sprk_aichatcontextmapping row
    /// with a linked sprk_analysisplaybook entity (aliased as "pb").
    /// </summary>
    private static Entity CreateMappingEntity(
        Guid playbookId,
        string playbookName,
        string? playbookDescription,
        bool isDefault,
        int sortOrder)
    {
        var entity = new Entity("sprk_aichatcontextmapping", Guid.NewGuid());
        entity["sprk_playbookid"] = new EntityReference("sprk_analysisplaybook", playbookId)
        {
            Name = playbookName
        };
        entity["sprk_isdefault"] = isDefault;
        entity["sprk_sortorder"] = sortOrder;

        // Linked entity fields come back as AliasedValue
        entity["pb.sprk_name"] = new AliasedValue("sprk_analysisplaybook", "sprk_name", playbookName);
        if (playbookDescription is not null)
        {
            entity["pb.sprk_description"] = new AliasedValue("sprk_analysisplaybook", "sprk_description", playbookDescription);
        }

        return entity;
    }

    private static EntityCollection CreateEntityCollection(params Entity[] entities)
    {
        var collection = new EntityCollection();
        collection.Entities.AddRange(entities);
        return collection;
    }

    /// <summary>
    /// Checks if a QueryExpression has a specific condition on a given attribute with a given value.
    /// </summary>
    private static bool HasCondition(QueryExpression query, string attributeName, string value)
    {
        return query.Criteria.Conditions
            .Any(c => c.AttributeName == attributeName && c.Values.Contains(value));
    }
}
