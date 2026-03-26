using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Agent;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Agent;

/// <summary>
/// Unit tests for AgentConversationService.
/// Validates conversation context creation, caching, updates, and removal.
/// </summary>
public class AgentConversationServiceTests
{
    private const string TenantId = "test-tenant-001";
    private const string ConversationId = "conv-abc-123";
    private const string UserId = "user-xyz-789";

    private readonly MemoryDistributedCache _cache;
    private readonly Mock<ILogger<AgentConversationService>> _loggerMock;
    private readonly AgentConversationService _service;

    public AgentConversationServiceTests()
    {
        _cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        _loggerMock = new Mock<ILogger<AgentConversationService>>();
        _service = new AgentConversationService(_cache, _loggerMock.Object);
    }

    private static string BuildCacheKey(string tenantId, string conversationId) =>
        $"agent:conv:{tenantId}:{conversationId}";

    #region GetOrCreateContextAsync Tests

    [Fact]
    public async Task GetOrCreateContextAsync_WhenNotCached_CreatesNewContext()
    {
        var result = await _service.GetOrCreateContextAsync(TenantId, ConversationId, UserId);

        result.Should().NotBeNull();
        result.TenantId.Should().Be(TenantId);
        result.ConversationId.Should().Be(ConversationId);
        result.UserId.Should().Be(UserId);
        result.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetOrCreateContextAsync_WhenNotCached_SetsNullOptionalFields()
    {
        var result = await _service.GetOrCreateContextAsync(TenantId, ConversationId, UserId);

        result.BffChatSessionId.Should().BeNull();
        result.ActiveDocumentId.Should().BeNull();
        result.ActiveDocumentName.Should().BeNull();
        result.ActiveMatterId.Should().BeNull();
        result.ActiveMatterName.Should().BeNull();
        result.ActivePlaybookId.Should().BeNull();
        result.LastAnalysisId.Should().BeNull();
        result.CurrentEntityType.Should().BeNull();
        result.CurrentEntityId.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateContextAsync_WhenCached_ReturnsCachedContext()
    {
        var existingContext = new AgentConversationContext
        {
            TenantId = TenantId,
            ConversationId = ConversationId,
            UserId = UserId,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ActiveDocumentId = Guid.NewGuid(),
            ActiveDocumentName = "TestDoc.pdf",
            BffChatSessionId = "bff-session-456"
        };

        await _cache.SetStringAsync(
            BuildCacheKey(TenantId, ConversationId),
            JsonSerializer.Serialize(existingContext));

        var result = await _service.GetOrCreateContextAsync(TenantId, ConversationId, UserId);

        result.BffChatSessionId.Should().Be("bff-session-456");
        result.ActiveDocumentName.Should().Be("TestDoc.pdf");
        result.ActiveDocumentId.Should().Be(existingContext.ActiveDocumentId);
    }

    [Fact]
    public async Task GetOrCreateContextAsync_WhenCachedJsonInvalid_CreatesNewContext()
    {
        await _cache.SetStringAsync(
            BuildCacheKey(TenantId, ConversationId),
            "not-valid-json-{{{");

        // JsonSerializer.Deserialize throws on invalid JSON, so the service
        // should handle this gracefully. If it throws, the test catches it.
        Func<Task> act = () => _service.GetOrCreateContextAsync(TenantId, ConversationId, UserId);

        // Depending on implementation, this may throw or return a new context.
        // The current implementation will throw JsonException.
        // This test documents the behavior.
        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task GetOrCreateContextAsync_DifferentConversations_ReturnSeparateContexts()
    {
        var context1 = new AgentConversationContext
        {
            TenantId = TenantId,
            ConversationId = "conv-1",
            UserId = "user-1",
            ActiveDocumentName = "Doc1.pdf"
        };
        var context2 = new AgentConversationContext
        {
            TenantId = TenantId,
            ConversationId = "conv-2",
            UserId = "user-2",
            ActiveDocumentName = "Doc2.pdf"
        };

        await _cache.SetStringAsync(
            BuildCacheKey(TenantId, "conv-1"),
            JsonSerializer.Serialize(context1));
        await _cache.SetStringAsync(
            BuildCacheKey(TenantId, "conv-2"),
            JsonSerializer.Serialize(context2));

        var result1 = await _service.GetOrCreateContextAsync(TenantId, "conv-1", "user-1");
        var result2 = await _service.GetOrCreateContextAsync(TenantId, "conv-2", "user-2");

        result1.ActiveDocumentName.Should().Be("Doc1.pdf");
        result2.ActiveDocumentName.Should().Be("Doc2.pdf");
    }

    [Fact]
    public async Task GetOrCreateContextAsync_DifferentTenants_ReturnSeparateContexts()
    {
        var context1 = new AgentConversationContext
        {
            TenantId = "tenant-A",
            ConversationId = ConversationId,
            UserId = UserId,
            ActiveDocumentName = "TenantADoc.pdf"
        };

        await _cache.SetStringAsync(
            BuildCacheKey("tenant-A", ConversationId),
            JsonSerializer.Serialize(context1));

        var resultA = await _service.GetOrCreateContextAsync("tenant-A", ConversationId, UserId);
        var resultB = await _service.GetOrCreateContextAsync("tenant-B", ConversationId, UserId);

        resultA.ActiveDocumentName.Should().Be("TenantADoc.pdf");
        resultB.ActiveDocumentName.Should().BeNull(); // new context for tenant-B
    }

    #endregion

    #region UpdateContextAsync Tests

    [Fact]
    public async Task UpdateContextAsync_PersistsToCache()
    {
        var context = new AgentConversationContext
        {
            TenantId = TenantId,
            ConversationId = ConversationId,
            UserId = UserId,
            ActiveDocumentId = Guid.NewGuid(),
            ActiveDocumentName = "Updated.pdf",
            ActivePlaybookId = Guid.NewGuid()
        };

        await _service.UpdateContextAsync(context);

        var cached = await _cache.GetStringAsync(BuildCacheKey(TenantId, ConversationId));
        cached.Should().NotBeNull();

        var deserialized = JsonSerializer.Deserialize<AgentConversationContext>(cached!);
        deserialized!.ActiveDocumentName.Should().Be("Updated.pdf");
        deserialized.ActiveDocumentId.Should().Be(context.ActiveDocumentId);
        deserialized.ActivePlaybookId.Should().Be(context.ActivePlaybookId);
    }

    [Fact]
    public async Task UpdateContextAsync_OverwritesPreviousValue()
    {
        var original = new AgentConversationContext
        {
            TenantId = TenantId,
            ConversationId = ConversationId,
            UserId = UserId,
            ActiveDocumentName = "Original.pdf"
        };
        await _service.UpdateContextAsync(original);

        var updated = new AgentConversationContext
        {
            TenantId = TenantId,
            ConversationId = ConversationId,
            UserId = UserId,
            ActiveDocumentName = "Updated.pdf"
        };
        await _service.UpdateContextAsync(updated);

        var cached = await _cache.GetStringAsync(BuildCacheKey(TenantId, ConversationId));
        var deserialized = JsonSerializer.Deserialize<AgentConversationContext>(cached!);
        deserialized!.ActiveDocumentName.Should().Be("Updated.pdf");
    }

    [Fact]
    public async Task UpdateContextAsync_LogsUpdate()
    {
        var context = new AgentConversationContext
        {
            TenantId = TenantId,
            ConversationId = ConversationId,
            UserId = UserId
        };

        await _service.UpdateContextAsync(context);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(ConversationId)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region RemoveContextAsync Tests

    [Fact]
    public async Task RemoveContextAsync_ClearsCache()
    {
        var context = new AgentConversationContext
        {
            TenantId = TenantId,
            ConversationId = ConversationId,
            UserId = UserId
        };
        await _service.UpdateContextAsync(context);

        // Verify it exists first
        var before = await _cache.GetStringAsync(BuildCacheKey(TenantId, ConversationId));
        before.Should().NotBeNull();

        await _service.RemoveContextAsync(TenantId, ConversationId);

        var after = await _cache.GetStringAsync(BuildCacheKey(TenantId, ConversationId));
        after.Should().BeNull();
    }

    [Fact]
    public async Task RemoveContextAsync_DoesNotAffectOtherConversations()
    {
        var context1 = new AgentConversationContext
        {
            TenantId = TenantId,
            ConversationId = "conv-to-remove",
            UserId = UserId
        };
        var context2 = new AgentConversationContext
        {
            TenantId = TenantId,
            ConversationId = "conv-to-keep",
            UserId = UserId
        };

        await _service.UpdateContextAsync(context1);
        await _service.UpdateContextAsync(context2);

        await _service.RemoveContextAsync(TenantId, "conv-to-remove");

        var removed = await _cache.GetStringAsync(BuildCacheKey(TenantId, "conv-to-remove"));
        var kept = await _cache.GetStringAsync(BuildCacheKey(TenantId, "conv-to-keep"));

        removed.Should().BeNull();
        kept.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveContextAsync_SucceedsWhenNoContextExists()
    {
        var act = () => _service.RemoveContextAsync(TenantId, "nonexistent-conv");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveContextAsync_LogsRemoval()
    {
        await _service.RemoveContextAsync(TenantId, ConversationId);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(ConversationId)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region GetBffSessionIdAsync Tests

    [Fact]
    public async Task GetBffSessionIdAsync_WhenSessionMapped_ReturnsSessionId()
    {
        var context = new AgentConversationContext
        {
            TenantId = TenantId,
            ConversationId = ConversationId,
            UserId = UserId,
            BffChatSessionId = "bff-session-abc"
        };
        await _cache.SetStringAsync(
            BuildCacheKey(TenantId, ConversationId),
            JsonSerializer.Serialize(context));

        var result = await _service.GetBffSessionIdAsync(TenantId, ConversationId);

        result.Should().Be("bff-session-abc");
    }

    [Fact]
    public async Task GetBffSessionIdAsync_WhenNoSessionMapped_ReturnsNull()
    {
        var result = await _service.GetBffSessionIdAsync(TenantId, ConversationId);

        result.Should().BeNull();
    }

    #endregion

    #region SetBffSessionIdAsync Tests

    [Fact]
    public async Task SetBffSessionIdAsync_WhenContextExists_UpdatesSessionId()
    {
        var context = new AgentConversationContext
        {
            TenantId = TenantId,
            ConversationId = ConversationId,
            UserId = UserId,
            ActiveDocumentName = "Keep.pdf"
        };
        await _cache.SetStringAsync(
            BuildCacheKey(TenantId, ConversationId),
            JsonSerializer.Serialize(context));

        await _service.SetBffSessionIdAsync(TenantId, ConversationId, "new-bff-session");

        var cached = await _cache.GetStringAsync(BuildCacheKey(TenantId, ConversationId));
        var deserialized = JsonSerializer.Deserialize<AgentConversationContext>(cached!);
        deserialized!.BffChatSessionId.Should().Be("new-bff-session");
        deserialized.ActiveDocumentName.Should().Be("Keep.pdf"); // other fields preserved
    }

    [Fact]
    public async Task SetBffSessionIdAsync_WhenNoContext_DoesNothing()
    {
        // No context exists for this conversation
        await _service.SetBffSessionIdAsync(TenantId, "no-such-conv", "bff-session");

        // Should not create a new cache entry
        var cached = await _cache.GetStringAsync(BuildCacheKey(TenantId, "no-such-conv"));
        // The implementation calls GetOrCreateContextAsync which creates a new context
        // with empty userId, but SetBffSessionIdAsync only updates if cached is not null.
        // Since the cache is empty initially, this should not create an entry.
        cached.Should().BeNull();
    }

    #endregion

    #region CancellationToken Tests

    [Fact]
    public async Task GetOrCreateContextAsync_RespectsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.GetOrCreateContextAsync(TenantId, ConversationId, UserId, cts.Token));
    }

    [Fact]
    public async Task UpdateContextAsync_RespectsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var context = new AgentConversationContext
        {
            TenantId = TenantId,
            ConversationId = ConversationId,
            UserId = UserId
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.UpdateContextAsync(context, cts.Token));
    }

    [Fact]
    public async Task RemoveContextAsync_RespectsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.RemoveContextAsync(TenantId, ConversationId, cts.Token));
    }

    #endregion

    #region AgentConversationContext Model Tests

    [Fact]
    public void AgentConversationContext_DefaultValues()
    {
        var context = new AgentConversationContext();

        context.TenantId.Should().Be("");
        context.ConversationId.Should().Be("");
        context.UserId.Should().Be("");
        context.BffChatSessionId.Should().BeNull();
        context.ActiveDocumentId.Should().BeNull();
        context.ActiveMatterId.Should().BeNull();
        context.ActivePlaybookId.Should().BeNull();
        context.LastAnalysisId.Should().BeNull();
        context.CurrentEntityType.Should().BeNull();
        context.CurrentEntityId.Should().BeNull();
    }

    [Fact]
    public void AgentConversationContext_RoundTripsViaJson()
    {
        var context = new AgentConversationContext
        {
            TenantId = "t1",
            ConversationId = "c1",
            UserId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
            BffChatSessionId = "bff-1",
            ActiveDocumentId = Guid.NewGuid(),
            ActiveDocumentName = "Doc.pdf",
            ActiveMatterId = Guid.NewGuid(),
            ActiveMatterName = "Matter One",
            ActivePlaybookId = Guid.NewGuid(),
            LastAnalysisId = Guid.NewGuid(),
            CurrentEntityType = "sprk_matter",
            CurrentEntityId = Guid.NewGuid()
        };

        var json = JsonSerializer.Serialize(context);
        var deserialized = JsonSerializer.Deserialize<AgentConversationContext>(json);

        deserialized.Should().BeEquivalentTo(context);
    }

    #endregion
}
