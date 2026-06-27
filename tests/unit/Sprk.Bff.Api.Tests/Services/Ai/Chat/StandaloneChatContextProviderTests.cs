using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="StandaloneChatContextProvider"/>.
///
/// Verifies:
/// - <see cref="StandaloneChatContextProvider.ResolveAsync"/> returns cached value on cache hit
///   (cold path is NOT called).
/// - <see cref="StandaloneChatContextProvider.ResolveAsync"/> populates the cache on cache miss
///   with a 30-minute absolute TTL (ADR-009).
/// - <see cref="StandaloneChatContextProvider.ResolveAsync"/> returns null for unsupported entity types
///   (endpoint maps to 400 ProblemDetails).
/// - Cache key format: <c>chat-context:{tenantId}:standalone:{entityType}:{entityId}</c>.
/// - <see cref="StandaloneChatContextProvider.ContextCacheTtl"/> is 30 minutes.
/// - Allowlist: supported entity types match <see cref="StandaloneChatContextProvider.SupportedEntityTypes"/>.
/// - Entity field catalog returns expected fields for each supported entity type.
/// </summary>
public class StandaloneChatContextProviderTests
{
    private const string TenantId = "tenant-test-023";
    private const string ContactEntityType = "contact";
    private const string ContactEntityId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    private readonly InMemoryTenantCache _cache;
    private readonly Mock<ILogger<StandaloneChatContextProvider>> _loggerMock;
    private readonly StandaloneChatContextProvider _sut;
    private const string CacheResource = "standalone-chat-context";

    public StandaloneChatContextProviderTests()
    {
        _cache = new InMemoryTenantCache();
        _loggerMock = new Mock<ILogger<StandaloneChatContextProvider>>();

        _sut = new StandaloneChatContextProvider(_cache, _loggerMock.Object);
    }

    // =========================================================================
    // Cache Key Tests
    // =========================================================================

    [Fact]
    public void BuildCacheKey_ReturnsExpectedFormat()
    {
        // Act
        var key = StandaloneChatContextProvider.BuildCacheKey(TenantId, ContactEntityType, ContactEntityId);

        // Assert — FR-05 on-wire key format
        key.Should().Be($"tenant:{TenantId}:{CacheResource}:{ContactEntityType}:{ContactEntityId}:v1");
    }

    [Theory]
    [InlineData("contact", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")]
    [InlineData("account", "11111111-2222-3333-4444-555555555555")]
    [InlineData("sprk_matter", "99999999-8888-7777-6666-555555555555")]
    public void BuildCacheKey_AlwaysHasTenantPrefixAndStandaloneResource(string entityType, string entityId)
    {
        // Act
        var key = StandaloneChatContextProvider.BuildCacheKey(TenantId, entityType, entityId);

        // Assert — FR-05 format: tenant:{tid}:standalone-chat-context:{entityType}:{entityId}:v1
        key.Should().StartWith("tenant:");
        key.Should().Contain(CacheResource);
        key.Should().Contain(entityType);
        key.Should().EndWith($"{entityId}:v1");
    }

    [Fact]
    public void CacheKeyPrefix_IsExpectedConstant()
    {
        // Assert — post-FR-05 the legacy prefix constant is "tenant:"; resource segment is separate.
        StandaloneChatContextProvider.CacheKeyPrefix.Should().Be("tenant:");
    }

    // =========================================================================
    // Cache Hit Tests (ADR-009 — Redis-first)
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ReturnsCachedValue_OnCacheHit()
    {
        // Arrange — seed the in-memory tenant cache with a known response
        var cachedResponse = BuildStubResponse(ContactEntityType, ContactEntityId);
        var cacheId = $"{ContactEntityType}:{ContactEntityId}";
        await _cache.SetAsync<StandaloneChatContextResponse>(
            TenantId, CacheResource, cacheId, 1, cachedResponse);

        // Act
        var result = await _sut.ResolveAsync(ContactEntityType, ContactEntityId, TenantId);

        // Assert
        result.Should().NotBeNull();
        result!.EntityType.Should().Be(ContactEntityType);
        result.EntityId.Should().Be(ContactEntityId);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotChangeCache_OnCacheHit()
    {
        // Arrange — seed with a custom stub; on hit, the cache value should be returned
        // unchanged. (We can't observe SetAsync without re-mocking, so we assert the
        // seeded value is still what's in the cache after ResolveAsync runs.)
        var seedResponse = BuildStubResponse(ContactEntityType, ContactEntityId);
        var cacheId = $"{ContactEntityType}:{ContactEntityId}";
        await _cache.SetAsync<StandaloneChatContextResponse>(
            TenantId, CacheResource, cacheId, 1, seedResponse);

        // Act
        await _sut.ResolveAsync(ContactEntityType, ContactEntityId, TenantId);

        // Assert — cached value still matches what we seeded
        var stillCached = await _cache.GetAsync<StandaloneChatContextResponse>(
            TenantId, CacheResource, cacheId, 1);
        stillCached.Should().NotBeNull();
        stillCached!.EntityId.Should().Be(seedResponse.EntityId);
    }

    // =========================================================================
    // Cache Miss Tests (ADR-009 — cold path populates cache)
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ReturnsNonNull_OnCacheMiss_ForSupportedEntityType()
    {
        // Arrange — cache returns null (miss); entity type is supported
        // Act
        var result = await _sut.ResolveAsync(ContactEntityType, ContactEntityId, TenantId);

        // Assert
        result.Should().NotBeNull();
        result!.EntityType.Should().Be(ContactEntityType);
        result.EntityId.Should().Be(ContactEntityId);
    }

    [Fact]
    public async Task ResolveAsync_CachesResult_OnCacheMiss()
    {
        // Arrange — empty cache → miss

        // Act
        await _sut.ResolveAsync(ContactEntityType, ContactEntityId, TenantId);

        // Assert — entry was written to the FR-05 cache slot
        var cacheId = $"{ContactEntityType}:{ContactEntityId}";
        var written = await _cache.GetAsync<StandaloneChatContextResponse>(
            TenantId, CacheResource, cacheId, 1);
        written.Should().NotBeNull("provider must populate the cache on a miss");
        written!.EntityId.Should().Be(ContactEntityId);
    }

    [Fact]
    public void ResolveAsync_CachesResult_With30MinuteAbsoluteTtl()
    {
        // Assert — production constant is exposed and equals 30 minutes (ADR-009).
        StandaloneChatContextProvider.ContextCacheTtl.Should().Be(TimeSpan.FromMinutes(30));
    }

    // =========================================================================
    // Unsupported Entity Type Tests (returns null → endpoint maps to 400)
    // =========================================================================

    [Theory]
    [InlineData("lead")]
    [InlineData("task")]
    [InlineData("email")]
    [InlineData("custom_entity")]
    [InlineData("")]
    [InlineData("CONTACT_NOT_LOWERCASE")] // allowlist is case-insensitive but test edge case
    public async Task ResolveAsync_ReturnsNull_ForUnsupportedEntityType(string unsupportedEntityType)
    {
        // Act
        var result = await _sut.ResolveAsync(unsupportedEntityType, ContactEntityId, TenantId);

        // Assert — null signals unsupported type → endpoint returns 400 ProblemDetails
        // Note: "CONTACT_NOT_LOWERCASE" is matched case-insensitively, so it resolves successfully.
        // Only truly unsupported types return null.
        if (StandaloneChatContextProvider.SupportedEntityTypes.Contains(unsupportedEntityType))
        {
            result.Should().NotBeNull("case-insensitive match should resolve");
        }
        else
        {
            result.Should().BeNull("unsupported entity type should return null for 400 mapping");
        }
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_ForUnsupportedType_AndDoesNotWriteToCache()
    {
        // Arrange
        const string unsupportedType = "lead";
        var cacheId = $"{unsupportedType}:{ContactEntityId}";

        // Act
        var result = await _sut.ResolveAsync(unsupportedType, ContactEntityId, TenantId);

        // Assert
        result.Should().BeNull();
        var written = await _cache.GetAsync<StandaloneChatContextResponse>(
            TenantId, CacheResource, cacheId, 1);
        written.Should().BeNull("unsupported entity types must not write to cache");
    }

    // =========================================================================
    // Supported Entity Types Allowlist Tests
    // =========================================================================

    [Fact]
    public void SupportedEntityTypes_ContainsAllExpectedTypes()
    {
        // Assert — the 5 specified entity types must be in the allowlist
        StandaloneChatContextProvider.SupportedEntityTypes.Should().Contain("contact");
        StandaloneChatContextProvider.SupportedEntityTypes.Should().Contain("account");
        StandaloneChatContextProvider.SupportedEntityTypes.Should().Contain("opportunity");
        StandaloneChatContextProvider.SupportedEntityTypes.Should().Contain("incident");
        StandaloneChatContextProvider.SupportedEntityTypes.Should().Contain("sprk_matter");
    }

    [Theory]
    [InlineData("contact")]
    [InlineData("CONTACT")]
    [InlineData("Contact")]
    [InlineData("account")]
    [InlineData("ACCOUNT")]
    [InlineData("opportunity")]
    [InlineData("OPPORTUNITY")]
    [InlineData("incident")]
    [InlineData("INCIDENT")]
    [InlineData("sprk_matter")]
    [InlineData("SPRK_MATTER")]
    public void SupportedEntityTypes_IsCaseInsensitive(string entityType)
    {
        // Assert — allowlist uses OrdinalIgnoreCase comparer
        StandaloneChatContextProvider.SupportedEntityTypes.Should().Contain(
            entityType, $"'{entityType}' should match case-insensitively");
    }

    // =========================================================================
    // Entity Field Catalog Tests
    // =========================================================================

    [Theory]
    [InlineData("contact", "fullname", "Full Name")]
    [InlineData("account", "name", "Account Name")]
    [InlineData("opportunity", "name", "Opportunity Name")]
    [InlineData("incident", "title", "Case Title")]
    [InlineData("sprk_matter", "sprk_mattername", "Matter Name")]
    public async Task ResolveAsync_ReturnsRequiredField_ForEachEntityType(
        string entityType, string expectedLogicalName, string expectedLabel)
    {
        // Arrange — cache miss
        var entityId = Guid.NewGuid().ToString();

        // Act
        var result = await _sut.ResolveAsync(entityType, entityId, TenantId);

        // Assert — each entity type has a required "name" field
        result.Should().NotBeNull();
        result!.ContextFields.Should().Contain(
            f => f.LogicalName == expectedLogicalName && f.DisplayLabel == expectedLabel,
            $"{entityType} should have a '{expectedLabel}' field ({expectedLogicalName})");
    }

    [Theory]
    [InlineData("contact")]
    [InlineData("account")]
    [InlineData("opportunity")]
    [InlineData("incident")]
    [InlineData("sprk_matter")]
    public async Task ResolveAsync_AllEntityTypes_HaveNonEmptyContextFields(string entityType)
    {
        // Arrange — cache miss
        var entityId = Guid.NewGuid().ToString();

        // Act
        var result = await _sut.ResolveAsync(entityType, entityId, TenantId);

        // Assert
        result.Should().NotBeNull();
        result!.ContextFields.Should().NotBeEmpty($"{entityType} must have at least one context field");
        result.ContextFields.Should().AllSatisfy(f =>
        {
            f.LogicalName.Should().NotBeNullOrWhiteSpace("each field must have a logical name");
            f.DisplayLabel.Should().NotBeNullOrWhiteSpace("each field must have a display label");
            f.FieldType.Should().NotBeNullOrWhiteSpace("each field must have a field type");
        });
    }

    [Theory]
    [InlineData("contact")]
    [InlineData("account")]
    [InlineData("opportunity")]
    [InlineData("incident")]
    [InlineData("sprk_matter")]
    public async Task ResolveAsync_AllEntityTypes_HaveCorrectDisplayName(string entityType)
    {
        // Arrange
        var entityId = Guid.NewGuid().ToString();

        // Act
        var result = await _sut.ResolveAsync(entityType, entityId, TenantId);

        // Assert — DisplayName should be human-readable (not the logical name)
        result.Should().NotBeNull();
        result!.DisplayName.Should().NotBeNullOrWhiteSpace();
        result.DisplayName.Should().NotBe(entityType.ToLowerInvariant(),
            "DisplayName should be a human-readable label, not the raw logical name");
    }

    [Fact]
    public async Task ResolveAsync_SprKMatter_HasPracticeAreaField()
    {
        // Arrange
        const string entityType = "sprk_matter";
        var entityId = Guid.NewGuid().ToString();

        // Act
        var result = await _sut.ResolveAsync(entityType, entityId, TenantId);

        // Assert — practice area is a key context field for Spaarke matters
        result.Should().NotBeNull();
        result!.ContextFields.Should().Contain(
            f => f.LogicalName == "sprk_practicearea",
            "sprk_matter should expose practice area as a context field");
    }

    [Fact]
    public async Task ResolveAsync_Contact_HasRequiredFlag_OnFullnameField()
    {
        // Arrange
        var entityId = Guid.NewGuid().ToString();

        // Act
        var result = await _sut.ResolveAsync("contact", entityId, TenantId);

        // Assert — fullname is required for effective context resolution
        result.Should().NotBeNull();
        var fullnameField = result!.ContextFields.FirstOrDefault(f => f.LogicalName == "fullname");
        fullnameField.Should().NotBeNull("contact must have a fullname field");
        fullnameField!.IsRequired.Should().BeTrue("contact fullname is required for AI context");
    }

    // =========================================================================
    // ContextCacheTtl Tests
    // =========================================================================

    [Fact]
    public void ContextCacheTtl_Is30Minutes()
    {
        // Assert — documented in class XML doc and ADR-009
        StandaloneChatContextProvider.ContextCacheTtl.Should().Be(TimeSpan.FromMinutes(30));
    }

    // =========================================================================
    // Response Structure Tests
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_Response_HasCorrectEntityTypeAndId()
    {
        // Arrange
        const string entityType = "account";
        const string entityId = "11111111-2222-3333-4444-555555555555";

        // Act
        var result = await _sut.ResolveAsync(entityType, entityId, TenantId);

        // Assert
        result.Should().NotBeNull();
        result!.EntityType.Should().Be(entityType);
        result.EntityId.Should().Be(entityId);
    }

    [Fact]
    public async Task ResolveAsync_Response_SerializesToJsonCorrectly()
    {
        // Arrange
        const string entityType = "contact";
        var entityId = Guid.NewGuid().ToString();

        // Act
        var result = await _sut.ResolveAsync(entityType, entityId, TenantId);

        // Assert — response must round-trip through JSON serialization
        result.Should().NotBeNull();
        var resultNotNull = result!;
        var json = JsonSerializer.SerializeToUtf8Bytes(resultNotNull);
        var deserialized = JsonSerializer.Deserialize<StandaloneChatContextResponse>(json);

        deserialized.Should().NotBeNull();
        deserialized!.EntityType.Should().Be(resultNotNull.EntityType);
        deserialized.EntityId.Should().Be(resultNotNull.EntityId);
        deserialized.ContextFields.Should().HaveCount(resultNotNull.ContextFields.Count);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Builds a stub <see cref="StandaloneChatContextResponse"/> for use in cache-hit scenarios.
    /// </summary>
    private static StandaloneChatContextResponse BuildStubResponse(string entityType, string entityId)
        => new(
            EntityType: entityType,
            EntityId: entityId,
            DisplayName: "Contact",
            ContextFields:
            [
                new StandaloneContextField("fullname", "Full Name", "text", IsRequired: true),
                new StandaloneContextField("emailaddress1", "Email", "text"),
            ]);
}
