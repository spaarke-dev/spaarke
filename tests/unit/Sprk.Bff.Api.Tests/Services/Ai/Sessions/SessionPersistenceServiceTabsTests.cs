using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Sessions;

/// <summary>
/// Unit tests for <see cref="SessionPersistenceService.SaveTabsAsync"/> (NFR-09, task 065).
///
/// Verifies:
/// - SaveTabsAsync returns false when the session does not exist in either Redis or Cosmos.
/// - Saving an empty tab list produces an empty Tabs field on the persisted document.
/// - Multiple tabs are persisted in the supplied order with all fields preserved.
/// - ActiveTabId is updated and persisted.
/// - Backwards-compatible round-trip: a session document that pre-dates the Tabs schema
///   loads cleanly, has Tabs/ActiveTabId set, and saves back without losing other state.
///
/// Patterns mirror <see cref="SessionPersistenceServiceTests"/> — same fixture wiring,
/// same Moq + FluentAssertions stack, same Cosmos mocking approach.
/// </summary>
public class SessionPersistenceServiceTabsTests
{
    private const string TenantId = "tenant-abc";
    private const string SessionId = "session-xyz";
    private const string DatabaseName = "spaarke-ai";
    private const string CosmosEndpoint = "https://spaarke-cosmos-dev.documents.azure.com:443/";

    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<CosmosClient> _cosmosClientMock;
    private readonly Mock<Container> _containerMock;
    private readonly Mock<ILogger<SessionPersistenceService>> _loggerMock;
    private readonly IConfiguration _configuration;
    private readonly SessionPersistenceService _sut;

    public SessionPersistenceServiceTabsTests()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _cosmosClientMock = new Mock<CosmosClient>();
        _containerMock = new Mock<Container>();
        _loggerMock = new Mock<ILogger<SessionPersistenceService>>();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CosmosPersistence:Endpoint"] = CosmosEndpoint,
                ["CosmosPersistence:DatabaseName"] = DatabaseName
            })
            .Build();

        _cosmosClientMock
            .Setup(c => c.GetContainer(DatabaseName, "sessions"))
            .Returns(_containerMock.Object);

        _sut = new SessionPersistenceService(
            _cacheMock.Object,
            _cosmosClientMock.Object,
            _configuration,
            _loggerMock.Object);
    }

    // =========================================================================
    // Test 1: No existing session → returns false
    // =========================================================================

    [Fact]
    public async Task SaveTabsAsync_NoExistingSession_ReturnsFalse()
    {
        // Arrange — Redis miss + Cosmos 404
        SetupCacheMiss();
        _containerMock
            .Setup(c => c.ReadItemAsync<StoredSession>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", HttpStatusCode.NotFound, 0, "", 0));

        var tabs = new[] { BuildTab("tab-1", "wizard.create-matter") };

        // Act
        var result = await _sut.SaveTabsAsync(SessionId, TenantId, tabs, activeTabId: "tab-1");

        // Assert
        result.Should().BeFalse("SaveTabsAsync must return false when no session exists in either store");

        // No writes should have happened
        _cacheMock.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Never,
            "Redis must not be written when the session does not exist");
    }

    // =========================================================================
    // Test 2: Empty tab list → empty Tabs field persisted
    // =========================================================================

    [Fact]
    public async Task SaveTabsAsync_WithEmptyList_SetsEmptyTabsField()
    {
        // Arrange — existing session loaded from Redis
        var existing = BuildStoredSession(withTabs: BuildTab("old-tab", "wizard.old"));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(existing);
        _cacheMock
            .Setup(c => c.GetAsync(SessionPersistenceService.BuildRedisKey(TenantId, SessionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);
        SetupCacheSetSuccess();

        StoredSession? capturedSession = null;
        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<StoredSession, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (s, _, _, _) => capturedSession = s)
            .ReturnsAsync(CreateFakeUpsertResponse());

        // Act
        var result = await _sut.SaveTabsAsync(SessionId, TenantId, Array.Empty<StoredWorkspaceTab>(), activeTabId: "home");
        await Task.Delay(50); // fire-and-forget Cosmos

        // Assert
        result.Should().BeTrue();
        capturedSession.Should().NotBeNull();
        capturedSession!.Tabs.Should().BeEmpty("an empty list must clear the persisted tabs");
        capturedSession.ActiveTabId.Should().Be("home");
    }

    // =========================================================================
    // Test 3: Multiple tabs → all persisted in supplied order
    // =========================================================================

    [Fact]
    public async Task SaveTabsAsync_WithMultipleTabs_PersistsAllAndOrder()
    {
        // Arrange — existing empty session in Redis
        var existing = BuildStoredSession();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(existing);
        _cacheMock
            .Setup(c => c.GetAsync(SessionPersistenceService.BuildRedisKey(TenantId, SessionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);
        SetupCacheSetSuccess();

        StoredSession? capturedSession = null;
        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<StoredSession, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (s, _, _, _) => capturedSession = s)
            .ReturnsAsync(CreateFakeUpsertResponse());

        var tabs = new[]
        {
            BuildTab("tab-a", "wizard.create-matter", "Create Matter"),
            BuildTab("tab-b", "wizard.create-project", "Create Project"),
            BuildTab("tab-c", "wizard.find-similar", "Find Similar")
        };

        // Act
        var result = await _sut.SaveTabsAsync(SessionId, TenantId, tabs, activeTabId: "tab-b");
        await Task.Delay(50);

        // Assert
        result.Should().BeTrue();
        capturedSession.Should().NotBeNull();
        capturedSession!.Tabs.Should().HaveCount(3);
        capturedSession.Tabs[0].Id.Should().Be("tab-a");
        capturedSession.Tabs[0].WidgetType.Should().Be("wizard.create-matter");
        capturedSession.Tabs[0].DisplayName.Should().Be("Create Matter");
        capturedSession.Tabs[1].Id.Should().Be("tab-b");
        capturedSession.Tabs[2].Id.Should().Be("tab-c");
        capturedSession.ActiveTabId.Should().Be("tab-b");

        // Cosmos upsert used the tenant partition key
        _containerMock.Verify(c => c.UpsertItemAsync(
            It.IsAny<StoredSession>(),
            It.Is<PartitionKey?>(pk => pk == new PartitionKey(TenantId)),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // Test 4: ActiveTabId is updated on save
    // =========================================================================

    [Fact]
    public async Task SaveTabsAsync_UpdatesActiveTabId()
    {
        // Arrange — existing session with a different active tab
        var existing = BuildStoredSession();
        existing.ActiveTabId = "old-active";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(existing);
        _cacheMock
            .Setup(c => c.GetAsync(SessionPersistenceService.BuildRedisKey(TenantId, SessionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);
        SetupCacheSetSuccess();

        StoredSession? capturedSession = null;
        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<StoredSession, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (s, _, _, _) => capturedSession = s)
            .ReturnsAsync(CreateFakeUpsertResponse());

        // Act — set to a new active tab id
        var result = await _sut.SaveTabsAsync(
            SessionId, TenantId,
            new[] { BuildTab("new-active", "wizard.foo") },
            activeTabId: "new-active");
        await Task.Delay(50);

        // Assert
        result.Should().BeTrue();
        capturedSession!.ActiveTabId.Should().Be("new-active");
    }

    // =========================================================================
    // Test 5: Backwards-compatibility — older session document without Tabs field
    // =========================================================================

    [Fact]
    public async Task SaveTabsAsync_OldSessionWithoutTabsField_BackwardsCompatible()
    {
        // Arrange — simulate an older Cosmos document that has no "tabs" or "activeTabId"
        // field at all (predates task 065). We construct the JSON manually to ensure the
        // deserializer copes with the missing fields rather than relying on the C# default-
        // initialised properties (which would mask the test).
        var legacyJson = """
        {
          "id": "session-xyz",
          "sessionId": "session-xyz",
          "tenantId": "tenant-abc",
          "playbookId": null,
          "messages": [
            { "messageId": "m1", "role": "user", "content": "hello", "timestamp": "2026-01-01T00:00:00Z" }
          ],
          "widgetStates": { "w1": "{}" },
          "createdAt": "2026-01-01T00:00:00Z",
          "lastActivity": "2026-01-01T00:00:00Z",
          "entityRefs": [],
          "conversationSummary": null,
          "summary": null
        }
        """;
        var legacyBytes = System.Text.Encoding.UTF8.GetBytes(legacyJson);

        _cacheMock
            .Setup(c => c.GetAsync(SessionPersistenceService.BuildRedisKey(TenantId, SessionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(legacyBytes);
        SetupCacheSetSuccess();

        StoredSession? capturedSession = null;
        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<StoredSession, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (s, _, _, _) => capturedSession = s)
            .ReturnsAsync(CreateFakeUpsertResponse());

        var newTab = BuildTab("tab-1", "wizard.create-matter");

        // Act — save tabs against the legacy document
        var result = await _sut.SaveTabsAsync(
            SessionId, TenantId,
            new[] { newTab },
            activeTabId: "tab-1");
        await Task.Delay(50);

        // Assert — round-trip preserved Messages + WidgetStates, ADDED Tabs + ActiveTabId
        result.Should().BeTrue("legacy session document must deserialize cleanly");
        capturedSession.Should().NotBeNull();
        capturedSession!.SessionId.Should().Be(SessionId);
        capturedSession.Messages.Should().HaveCount(1, "existing messages must be preserved");
        capturedSession.Messages[0].Content.Should().Be("hello");
        capturedSession.WidgetStates.Should().ContainKey("w1", because: "existing widget state must be preserved");
        capturedSession.Tabs.Should().HaveCount(1);
        capturedSession.Tabs[0].Id.Should().Be("tab-1");
        capturedSession.ActiveTabId.Should().Be("tab-1");
    }

    // =========================================================================
    // Helpers — mirror SessionPersistenceServiceTests
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
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static StoredWorkspaceTab BuildTab(string id, string widgetType, string displayName = "Tab")
        => new(
            Id: id,
            WidgetType: widgetType,
            WidgetData: null,
            DisplayName: displayName);

    private static StoredSession BuildStoredSession(params StoredWorkspaceTab[] withTabs) => new()
    {
        Id = SessionId,
        SessionId = SessionId,
        TenantId = TenantId,
        Messages = [],
        WidgetStates = [],
        Tabs = withTabs.ToList(),
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        LastActivity = DateTimeOffset.UtcNow
    };

    private static ItemResponse<StoredSession> CreateFakeUpsertResponse()
    {
        var mock = new Mock<ItemResponse<StoredSession>>();
        mock.SetupGet(r => r.Resource).Returns(BuildStoredSession());
        mock.SetupGet(r => r.StatusCode).Returns(HttpStatusCode.OK);
        return mock.Object;
    }
}
