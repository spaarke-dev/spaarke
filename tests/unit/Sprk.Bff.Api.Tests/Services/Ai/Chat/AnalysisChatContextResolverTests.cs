using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="AnalysisChatContextResolver"/>.
///
/// Verifies:
/// - <see cref="AnalysisChatContextResolver.ResolveAsync"/> returns cached value on cache hit
///   (Dataverse resolver is NOT called).
/// - <see cref="AnalysisChatContextResolver.ResolveAsync"/> calls Dataverse resolution on cache miss
///   and caches the result with a 30-minute absolute TTL (ADR-009).
/// - Cache key format: <c>analysis-context:{analysisId}</c>.
/// - Capability 100000004 (selection_revise) → <see cref="InlineActionInfo"/> with actionType='diff'.
/// - All 7 capabilities in <see cref="AnalysisChatContextResolver.CapabilityToActionMap"/> map to
///   the correct <see cref="PlaybookCapabilities"/> string and actionType.
/// - <see cref="AnalysisChatContextResolver.BuildCacheKey"/> produces the expected format.
/// </summary>
public class AnalysisChatContextResolverTests
{
    private const string AnalysisId = "analysis-abc-123";
    private const string CacheKeyPrefix = "analysis-context:";

    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<ILogger<AnalysisChatContextResolver>> _loggerMock;
    private readonly AnalysisChatContextResolver _sut;

    public AnalysisChatContextResolverTests()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<AnalysisChatContextResolver>>();
        _sut = new AnalysisChatContextResolver(_cacheMock.Object, _loggerMock.Object);
    }

    // =========================================================================
    // Cache Key Tests
    // =========================================================================

    [Fact]
    public void BuildCacheKey_ReturnsExpectedFormat()
    {
        // Act
        var key = AnalysisChatContextResolver.BuildCacheKey(AnalysisId);

        // Assert
        key.Should().Be($"{CacheKeyPrefix}{AnalysisId}");
    }

    [Theory]
    [InlineData("analysis-001")]
    [InlineData("11111111-2222-3333-4444-555555555555")]
    [InlineData("my-custom-key")]
    public void BuildCacheKey_AlwaysStartsWithPrefix(string id)
    {
        // Act
        var key = AnalysisChatContextResolver.BuildCacheKey(id);

        // Assert
        key.Should().StartWith(CacheKeyPrefix);
        key.Should().EndWith(id);
    }

    [Fact]
    public void CacheKeyPrefix_IsExpectedConstant()
    {
        // Assert — constant value is part of the cache eviction contract
        AnalysisChatContextResolver.CacheKeyPrefix.Should().Be("analysis-context:");
    }

    // =========================================================================
    // Cache Hit Tests (ADR-009 — Redis-first)
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ReturnsCachedValue_OnCacheHit()
    {
        // Arrange — serialize a known response and place it in the mock cache
        var cachedResponse = BuildStubResponse(AnalysisId);
        var cachedBytes = JsonSerializer.SerializeToUtf8Bytes(cachedResponse);
        var cacheKey = AnalysisChatContextResolver.BuildCacheKey(AnalysisId);

        _cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedBytes);

        // Act
        var result = await _sut.ResolveAsync(AnalysisId);

        // Assert
        result.Should().NotBeNull();
        result!.AnalysisContext.AnalysisId.Should().Be(AnalysisId);
        result.DefaultPlaybookName.Should().Be(cachedResponse.DefaultPlaybookName);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotCallCacheSet_OnCacheHit()
    {
        // Arrange
        var cachedResponse = BuildStubResponse(AnalysisId);
        var cachedBytes = JsonSerializer.SerializeToUtf8Bytes(cachedResponse);
        var cacheKey = AnalysisChatContextResolver.BuildCacheKey(AnalysisId);

        _cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedBytes);

        // Act
        await _sut.ResolveAsync(AnalysisId);

        // Assert — cache was NOT written again on a hit
        _cacheMock.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================
    // Cache Miss Tests (ADR-009 — cold path populates cache)
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_CallsResolveFromDataverse_OnCacheMiss()
    {
        // Arrange — cache returns null (miss)
        SetupCacheMiss();

        // Act — stub resolver returns a non-null response for any analysisId
        var result = await _sut.ResolveAsync(AnalysisId);

        // Assert — result is non-null (stub Dataverse path returns a response)
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveAsync_CachesResultAfterDataverseFetch_OnCacheMiss()
    {
        // Arrange — cache miss
        SetupCacheMiss();

        byte[]? capturedBytes = null;
        DistributedCacheEntryOptions? capturedOptions = null;
        string? capturedKey = null;

        _cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (key, bytes, opts, _) =>
                {
                    capturedKey = key;
                    capturedBytes = bytes;
                    capturedOptions = opts;
                })
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ResolveAsync(AnalysisId);

        // Assert — cache Set was called once with the expected key and options
        _cacheMock.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        capturedKey.Should().Be(AnalysisChatContextResolver.BuildCacheKey(AnalysisId));
        capturedBytes.Should().NotBeNull().And.NotBeEmpty();
        capturedOptions.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveAsync_CachesResult_With30MinuteAbsoluteTtl()
    {
        // Arrange
        SetupCacheMiss();

        DistributedCacheEntryOptions? capturedOptions = null;
        _cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (_, _, opts, _) => capturedOptions = opts)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ResolveAsync(AnalysisId);

        // Assert — 30-minute absolute TTL (ADR-009 — no sliding expiration to prevent stale data)
        capturedOptions.Should().NotBeNull();
        capturedOptions!.AbsoluteExpirationRelativeToNow.Should().Be(AnalysisChatContextResolver.ContextCacheTtl);
        capturedOptions.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromMinutes(30));
        capturedOptions.SlidingExpiration.Should().BeNull("analysis context uses absolute TTL, not sliding");
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNonNull_WhenDataverseStubSucceeds()
    {
        // Arrange — cache miss; stub Dataverse path always returns a value
        SetupCacheMiss();
        SetupCacheSetSuccess();

        // Act
        var result = await _sut.ResolveAsync(AnalysisId);

        // Assert — stub resolver returns a well-formed response
        result.Should().NotBeNull();
        result!.AnalysisContext.AnalysisId.Should().Be(AnalysisId);
    }

    [Fact]
    public async Task ResolveAsync_CachedResult_ContainsCorrectAnalysisId()
    {
        // Arrange
        SetupCacheMiss();

        byte[]? capturedBytes = null;
        _cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (_, bytes, _, _) => capturedBytes = bytes)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ResolveAsync(AnalysisId);

        // Assert — deserialize cached bytes and check the analysisId round-trips correctly
        capturedBytes.Should().NotBeNull();
        var deserialised = JsonSerializer.Deserialize<AnalysisChatContextResponse>(capturedBytes!);
        deserialised.Should().NotBeNull();
        deserialised!.AnalysisContext.AnalysisId.Should().Be(AnalysisId);
    }

    // =========================================================================
    // CapabilityToActionMap Tests — mapping correctness
    // =========================================================================

    [Fact]
    public void CapabilityToActionMap_ContainsAllSevenCapabilities()
    {
        // Assert — 7 defined capabilities in the spec
        AnalysisChatContextResolver.CapabilityToActionMap.Should().HaveCount(7);
    }

    [Fact]
    public void CapabilityToActionMap_AllOptionSetValues_ArePresent()
    {
        // Assert — all known Dataverse option set integer values are mapped
        var keys = AnalysisChatContextResolver.CapabilityToActionMap.Keys;
        keys.Should().Contain(100000000, "search capability");
        keys.Should().Contain(100000001, "analyze capability");
        keys.Should().Contain(100000002, "write_back capability");
        keys.Should().Contain(100000003, "reanalyze capability");
        keys.Should().Contain(100000004, "selection_revise capability");
        keys.Should().Contain(100000005, "web_search capability");
        keys.Should().Contain(100000006, "summarize capability");
    }

    [Theory]
    [InlineData(100000000, PlaybookCapabilities.Search, "chat")]
    [InlineData(100000001, PlaybookCapabilities.Analyze, "chat")]
    [InlineData(100000002, PlaybookCapabilities.WriteBack, "chat")]
    [InlineData(100000003, PlaybookCapabilities.Reanalyze, "chat")]
    [InlineData(100000004, PlaybookCapabilities.SelectionRevise, "diff")]
    [InlineData(100000005, PlaybookCapabilities.WebSearch, "chat")]
    [InlineData(100000006, PlaybookCapabilities.Summarize, "chat")]
    public void CapabilityToActionMap_EachEntry_HasCorrectIdAndActionType(
        int optionSetValue,
        string expectedId,
        string expectedActionType)
    {
        // Act
        var action = AnalysisChatContextResolver.CapabilityToActionMap[optionSetValue];

        // Assert
        action.Id.Should().Be(expectedId,
            $"option set value {optionSetValue} should map to capability id '{expectedId}'");
        action.ActionType.Should().Be(expectedActionType,
            $"option set value {optionSetValue} should have actionType '{expectedActionType}'");
    }

    [Fact]
    public void CapabilityToActionMap_Capability100000004_IsSelectionRevise_WithDiffActionType()
    {
        // Arrange — selection_revise is the only diff-type capability (opens DiffReviewPanel)
        var action = AnalysisChatContextResolver.CapabilityToActionMap[100000004];

        // Assert
        action.Id.Should().Be(PlaybookCapabilities.SelectionRevise);
        action.ActionType.Should().Be("diff",
            "selection_revise triggers DiffReviewPanel, not a chat response");
        action.Label.Should().NotBeNullOrWhiteSpace();
        action.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CapabilityToActionMap_AllChatTypeCapabilities_HaveActionTypeChat()
    {
        // Arrange — all entries except 100000004 (selection_revise) should be 'chat'
        var chatTypeEntries = AnalysisChatContextResolver.CapabilityToActionMap
            .Where(kvp => kvp.Key != 100000004)
            .ToList();

        // Assert
        chatTypeEntries.Should().AllSatisfy(kvp =>
            kvp.Value.ActionType.Should().Be("chat",
                $"capability {kvp.Key} ({kvp.Value.Id}) should route through the chat pipeline"));
    }

    [Fact]
    public void CapabilityToActionMap_AllEntries_HaveNonEmptyLabelsAndDescriptions()
    {
        // Assert — every entry is renderable in the UI (non-empty label and description)
        AnalysisChatContextResolver.CapabilityToActionMap.Should().AllSatisfy(kvp =>
        {
            kvp.Value.Label.Should().NotBeNullOrWhiteSpace(
                $"capability {kvp.Key} ({kvp.Value.Id}) must have a label for UI rendering");
            kvp.Value.Description.Should().NotBeNullOrWhiteSpace(
                $"capability {kvp.Key} ({kvp.Value.Id}) must have a description for tooltip/help text");
        });
    }

    [Fact]
    public void CapabilityToActionMap_AllIds_MapToKnownPlaybookCapabilityConstants()
    {
        // Arrange — collect all known constants
        var knownIds = PlaybookCapabilities.All.ToHashSet();

        // Assert — every mapped ID is a known PlaybookCapabilities constant
        AnalysisChatContextResolver.CapabilityToActionMap.Should().AllSatisfy(kvp =>
            knownIds.Should().Contain(kvp.Value.Id,
                $"capability {kvp.Key} maps to id '{kvp.Value.Id}' which should be in PlaybookCapabilities.All"));
    }

    // =========================================================================
    // PatentClaims Playbook Scenario Tests
    // (spec FR-06: search + analyze + selection_revise capabilities)
    // =========================================================================

    [Fact]
    public void CapabilityToActionMap_PatentClaimsCapabilities_ProduceExpectedInlineActions()
    {
        // Arrange — patent-claims playbook capabilities: search(100000000), analyze(100000001), selection_revise(100000004)
        var patentClaimsCapabilityValues = new[] { 100000000, 100000001, 100000004 };

        // Act — simulate what the resolver does when building inline actions from option set values
        var inlineActions = patentClaimsCapabilityValues
            .Select(v => AnalysisChatContextResolver.CapabilityToActionMap[v])
            .ToList();

        // Assert
        inlineActions.Should().HaveCount(3);
        inlineActions.Should().Contain(a => a.Id == PlaybookCapabilities.Search,
            "search capability (100000000) → prior-art-search inline action");
        inlineActions.Should().Contain(a => a.Id == PlaybookCapabilities.Analyze,
            "analyze capability (100000001) → extract-claims inline action");
        inlineActions.Should().Contain(a => a.Id == PlaybookCapabilities.SelectionRevise,
            "selection_revise capability (100000004) → claims diff inline action");
    }

    [Fact]
    public void CapabilityToActionMap_PatentClaimsSelectionRevise_IsDiffType()
    {
        // Assert — selection_revise in patent-claims context opens DiffReviewPanel (spec FR-06)
        var action = AnalysisChatContextResolver.CapabilityToActionMap[100000004];
        action.ActionType.Should().Be("diff",
            "patent-claims claims editing requires DiffReviewPanel (actionType='diff')");
    }

    [Fact]
    public void CapabilityToActionMap_PatentClaimsSearch_IsChatType()
    {
        // Assert — prior-art-search streams a chat response (not a diff)
        var action = AnalysisChatContextResolver.CapabilityToActionMap[100000000];
        action.Id.Should().Be(PlaybookCapabilities.Search);
        action.ActionType.Should().Be("chat");
    }

    [Fact]
    public void CapabilityToActionMap_PatentClaimsAnalyze_IsChatType()
    {
        // Assert — extract-claims streams a chat response
        var action = AnalysisChatContextResolver.CapabilityToActionMap[100000001];
        action.Id.Should().Be(PlaybookCapabilities.Analyze);
        action.ActionType.Should().Be("chat");
    }

    // =========================================================================
    // ContextCacheTtl Tests
    // =========================================================================

    [Fact]
    public void ContextCacheTtl_Is30Minutes()
    {
        // Assert — documented in class XML doc and ADR-009
        AnalysisChatContextResolver.ContextCacheTtl.Should().Be(TimeSpan.FromMinutes(30));
    }

    // =========================================================================
    // Helper Setup Methods
    // =========================================================================

    /// <summary>Sets up the distributed cache mock to return null (cache miss).</summary>
    private void SetupCacheMiss()
    {
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
    }

    /// <summary>Sets up the distributed cache mock to accept SetAsync calls without error.</summary>
    private void SetupCacheSetSuccess()
    {
        _cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>
    /// Builds a stub <see cref="AnalysisChatContextResponse"/> for use in cache-hit scenarios.
    /// Mirrors what the resolver returns from its current stub Dataverse path.
    /// </summary>
    private static AnalysisChatContextResponse BuildStubResponse(string analysisId)
        => new(
            DefaultPlaybookId: string.Empty,
            DefaultPlaybookName: "Default Analysis Playbook",
            AvailablePlaybooks: [],
            InlineActions: AnalysisChatContextResolver.CapabilityToActionMap.Values.ToList(),
            KnowledgeSources: [],
            AnalysisContext: new AnalysisContextInfo(
                AnalysisId: analysisId,
                AnalysisType: null,
                MatterType: null,
                PracticeArea: null,
                SourceFileId: null,
                SourceContainerId: null));
}
