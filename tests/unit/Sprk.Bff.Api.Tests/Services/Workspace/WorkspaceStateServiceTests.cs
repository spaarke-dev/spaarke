using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Unit tests for <see cref="WorkspaceStateService"/> — R6 Pillar 6a / task 051.
///
/// Acceptance-criteria coverage:
///   (a) Per-tenant cache-key isolation — two tenants, same sessionId → different keys.
///   (b) Redis TTL = 24h on UpsertTab.
///   (c) PinTab writes through to Cosmos with matterId tag + IsPinned=true.
///   (d) CloseTab removes from Redis; does NOT touch Cosmos durable.
///   (e) GetTabs merges hot (Redis) + durable (Cosmos) rows; hot wins on tab-id collision.
///   (f) JSON polymorphism round-trips each of the 4 widget-data variants.
///
/// Cosmos interactions are verified via the Moq <see cref="Container"/> + injected
/// <see cref="CosmosClient"/>. Redis is mocked via <see cref="IDistributedCache"/>.
/// </summary>
public class WorkspaceStateServiceTests
{
    private const string DatabaseName = "spaarke-ai";
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string SessionId = "session-001";

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// In-memory <see cref="ITenantCache"/> for tests. Mirrors the real wrapper's
    /// key format <c>tenant:{tenantId}:{resource}:{id}:v{version}</c> so the Store
    /// dictionary can be asserted against canonical keys (e.g.,
    /// <c>tenant:tenant-a:workspace-state:session-001:v1</c>).
    /// </summary>
    private sealed class FakeTenantCache : ITenantCache
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        /// <summary>(Key, RawBytes, TTL) — TTL is null for "no explicit TTL".</summary>
        public Dictionary<string, (byte[] Value, TimeSpan? Ttl)> Store { get; } = new();

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version,
            string cacheInstance = "default", CancellationToken ct = default)
        {
            var key = BuildKey(tenantId, resource, id, version);
            if (!Store.TryGetValue(key, out var entry)) return Task.FromResult(default(T));
            var deserialized = JsonSerializer.Deserialize<T>(entry.Value, SerializerOptions);
            return Task.FromResult(deserialized);
        }

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value,
            TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            var key = BuildKey(tenantId, resource, id, version);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
            Store[key] = (bytes, ttl);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string tenantId, string resource, string id, int version,
            string cacheInstance = "default", CancellationToken ct = default)
        {
            var key = BuildKey(tenantId, resource, id, version);
            Store.Remove(key);
            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version,
            Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null,
            string cacheInstance = "default", CancellationToken ct = default)
        {
            var existing = await GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);
            if (existing is not null) return existing;
            var produced = await factory(ct);
            await SetAsync(tenantId, resource, id, version, produced, ttl, cacheInstance, ct);
            return produced;
        }

        private static string BuildKey(string tenantId, string resource, string id, int version)
            => $"tenant:{tenantId}:{resource}:{id}:v{version}";
    }

    private static (WorkspaceStateService Service, FakeTenantCache Cache, Mock<Container> ContainerMock)
        CreateSut(Action<Mock<Container>>? configureContainer = null)
    {
        var cache = new FakeTenantCache();
        var containerMock = new Mock<Container>();
        configureContainer?.Invoke(containerMock);

        var clientMock = new Mock<CosmosClient>();
        clientMock
            .Setup(c => c.GetContainer(DatabaseName, WorkspaceStateService.CosmosContainerName))
            .Returns(containerMock.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CosmosPersistence:DatabaseName"] = DatabaseName,
            })
            .Build();

        var sut = new WorkspaceStateService(
            cache: cache,
            cosmosClient: clientMock.Object,
            configuration: config,
            logger: NullLogger<WorkspaceStateService>.Instance);
        return (sut, cache, containerMock);
    }

    private static WorkspaceTab MakeTab(
        string id,
        string tenantId,
        string sessionId,
        WorkspaceTabWidgetData widgetData,
        bool pinned = false,
        string matterId = "matter-1",
        string matterName = "Matter 1")
        => new()
        {
            Id = id,
            WidgetType = widgetData.Kind,
            WidgetData = widgetData,
            SessionId = sessionId,
            TenantId = tenantId,
            VisibleToAssistant = true,
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "agent",
                CreatedBy = "agent-foo",
                CreatedAt = "2026-06-09T00:00:00Z",
            },
            MatterContext = new WorkspaceTabMatterContext
            {
                MatterId = matterId,
                MatterName = matterName,
            },
            IsPinned = pinned,
            CanEdit = true,
            CreatedAt = "2026-06-09T00:00:00Z",
            UpdatedAt = "2026-06-09T00:00:00Z",
        };

    private static SummaryTabWidgetData SummaryData(string body = "hello", string? tldr = null)
        => new() { Body = body, Tldr = tldr };

    // =========================================================================
    // (a) Per-tenant cache-key isolation (NFR-16 BINDING)
    // =========================================================================

    [Fact]
    public void BuildRedisKey_IsolatesTenants_ForSameSessionId()
    {
        // Arrange — post-migration shape: tenant:{tenantId}:workspace-state:{sessionId}:v1
        var keyA = WorkspaceStateService.BuildRedisKey(TenantA, SessionId);
        var keyB = WorkspaceStateService.BuildRedisKey(TenantB, SessionId);

        // Assert — keys are distinct and both contain the tenantId
        keyA.Should().NotBe(keyB);
        keyA.Should().Be($"tenant:{TenantA}:workspace-state:{SessionId}:v1");
        keyB.Should().Be($"tenant:{TenantB}:workspace-state:{SessionId}:v1");
    }

    [Fact]
    public async Task UpsertTabAsync_WritesToTenantSpecificRedisKey()
    {
        // Arrange
        var (sut, cache, _) = CreateSut();
        var tabA = MakeTab("tab-1", TenantA, SessionId, SummaryData());
        var tabB = MakeTab("tab-1", TenantB, SessionId, SummaryData());

        // Act
        await sut.UpsertTabAsync(TenantA, SessionId, tabA);
        await sut.UpsertTabAsync(TenantB, SessionId, tabB);

        // Assert — both keys exist in cache; they do NOT collide
        cache.Store.Should().ContainKey($"tenant:{TenantA}:workspace-state:{SessionId}:v1");
        cache.Store.Should().ContainKey($"tenant:{TenantB}:workspace-state:{SessionId}:v1");
        cache.Store.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTabsAsync_ReturnsOnlyOwnTenantData()
    {
        // Arrange
        var (sut, _, containerMock) = CreateSut();
        SetupEmptyCosmosQuery(containerMock);

        await sut.UpsertTabAsync(TenantA, SessionId, MakeTab("tab-1", TenantA, SessionId, SummaryData("for-A")));
        await sut.UpsertTabAsync(TenantB, SessionId, MakeTab("tab-1", TenantB, SessionId, SummaryData("for-B")));

        // Act
        var tabsA = await sut.GetTabsAsync(TenantA, SessionId);
        var tabsB = await sut.GetTabsAsync(TenantB, SessionId);

        // Assert
        tabsA.Should().HaveCount(1);
        ((SummaryTabWidgetData)tabsA[0].WidgetData).Body.Should().Be("for-A");
        tabsA[0].TenantId.Should().Be(TenantA);

        tabsB.Should().HaveCount(1);
        ((SummaryTabWidgetData)tabsB[0].WidgetData).Body.Should().Be("for-B");
        tabsB[0].TenantId.Should().Be(TenantB);
    }

    [Fact]
    public async Task UpsertTabAsync_ThrowsOnTenantMismatch()
    {
        // Arrange
        var (sut, _, _) = CreateSut();
        var tabA = MakeTab("tab-1", TenantA, SessionId, SummaryData());

        // Act / Assert — calling with the wrong tenantId arg surfaces the mismatch
        var act = () => sut.UpsertTabAsync(TenantB, SessionId, tabA);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Tenant mismatch*");
    }

    // =========================================================================
    // (b) Redis TTL = 24h on UpsertTab (FR-32)
    // =========================================================================

    [Fact]
    public async Task UpsertTabAsync_SetsRedisTtlTo24Hours()
    {
        // Arrange
        var (sut, cache, _) = CreateSut();
        var tab = MakeTab("tab-1", TenantA, SessionId, SummaryData());

        // Act
        await sut.UpsertTabAsync(TenantA, SessionId, tab);

        // Assert — post-migration: AbsoluteExpirationRelativeToNow (the wrapper does not expose
        // SlidingExpiration); 24h horizon preserved.
        var (_, ttl) = cache.Store[$"tenant:{TenantA}:workspace-state:{SessionId}:v1"];
        ttl.Should().Be(TimeSpan.FromHours(24));
    }

    // =========================================================================
    // (c) PinTab writes through to Cosmos with matterId + IsPinned=true
    // =========================================================================

    [Fact]
    public async Task PinTabAsync_WritesThroughToCosmos_WithMatterIdTagAndIsPinnedTrue()
    {
        // Arrange
        WorkspaceStateService.WorkspaceTabDurableDocument? capturedDoc = null;
        PartitionKey? capturedPk = null;

        var (sut, _, containerMock) = CreateSut(c =>
        {
            c.Setup(x => x.UpsertItemAsync(
                    It.IsAny<WorkspaceStateService.WorkspaceTabDurableDocument>(),
                    It.IsAny<PartitionKey?>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<WorkspaceStateService.WorkspaceTabDurableDocument, PartitionKey?, ItemRequestOptions, CancellationToken>(
                    (doc, pk, _, _) => { capturedDoc = doc; capturedPk = pk; })
                .ReturnsAsync(new Mock<ItemResponse<WorkspaceStateService.WorkspaceTabDurableDocument>>().Object);
        });

        var tab = MakeTab("tab-pin-1", TenantA, SessionId, SummaryData("body"), pinned: false, matterId: "matter-orig", matterName: "Original");
        await sut.UpsertTabAsync(TenantA, SessionId, tab);

        // Act — pin and attach a different matter
        await sut.PinTabAsync(TenantA, SessionId, "tab-pin-1", "matter-new");

        // Assert — Cosmos upsert was called with correct shape
        capturedDoc.Should().NotBeNull();
        capturedDoc!.Id.Should().Be($"workspace-tab_{TenantA}_tab-pin-1");
        capturedDoc.DocumentType.Should().Be("workspace-tab");
        capturedDoc.TenantId.Should().Be(TenantA);
        capturedDoc.SessionId.Should().Be(SessionId);
        capturedDoc.MatterId.Should().Be("matter-new");
        capturedDoc.Tab.Should().NotBeNull();
        capturedDoc.Tab!.IsPinned.Should().BeTrue();
        capturedDoc.Tab.MatterContext.MatterId.Should().Be("matter-new");
        capturedPk.Should().Be(new PartitionKey(TenantA));
    }

    [Fact]
    public async Task PinTabAsync_PreservesHotTierAfterPromotion()
    {
        // Arrange
        var (sut, cache, containerMock) = CreateSut(SetupCosmosUpsertAccepts);
        var tab = MakeTab("tab-pin-2", TenantA, SessionId, SummaryData());
        await sut.UpsertTabAsync(TenantA, SessionId, tab);

        // Act
        await sut.PinTabAsync(TenantA, SessionId, "tab-pin-2", "matter-X");

        // Assert — Redis row still present
        cache.Store.Should().ContainKey($"tenant:{TenantA}:workspace-state:{SessionId}:v1");
    }

    [Fact]
    public async Task PinTabAsync_ThrowsKeyNotFound_WhenTabNotPresent()
    {
        // Arrange
        var (sut, _, _) = CreateSut();

        // Act / Assert
        var act = () => sut.PinTabAsync(TenantA, SessionId, "missing-tab", "matter-X");
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // =========================================================================
    // (d) CloseTab removes from Redis only; does NOT touch Cosmos
    // =========================================================================

    [Fact]
    public async Task CloseTabAsync_RemovesFromRedis_DoesNotTouchCosmos()
    {
        // Arrange
        var (sut, cache, containerMock) = CreateSut();
        var tab = MakeTab("tab-close-1", TenantA, SessionId, SummaryData());
        await sut.UpsertTabAsync(TenantA, SessionId, tab);

        // Act
        await sut.CloseTabAsync(TenantA, SessionId, "tab-close-1");

        // Assert — Redis key was removed entirely (only tab in session)
        cache.Store.Should().NotContainKey($"tenant:{TenantA}:workspace-state:{SessionId}:v1");

        // No Cosmos delete / upsert calls
        containerMock.Verify(c => c.DeleteItemAsync<It.IsAnyType>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
        containerMock.Verify(c => c.UpsertItemAsync(
            It.IsAny<WorkspaceStateService.WorkspaceTabDurableDocument>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CloseTabAsync_PreservesOtherTabs_InSameSession()
    {
        // Arrange
        var (sut, cache, _) = CreateSut();
        await sut.UpsertTabAsync(TenantA, SessionId, MakeTab("tab-1", TenantA, SessionId, SummaryData("A")));
        await sut.UpsertTabAsync(TenantA, SessionId, MakeTab("tab-2", TenantA, SessionId, SummaryData("B")));

        // Act
        await sut.CloseTabAsync(TenantA, SessionId, "tab-1");

        // Assert — Redis key still present; tab-2 alive
        var key = $"tenant:{TenantA}:workspace-state:{SessionId}:v1";
        cache.Store.Should().ContainKey(key);
        var bytes = cache.Store[key].Value;
        var dict = JsonSerializer.Deserialize<Dictionary<string, WorkspaceTab>>(
            bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        dict.Should().NotBeNull();
        dict!.Should().HaveCount(1).And.ContainKey("tab-2");
    }

    [Fact]
    public async Task CloseTabAsync_IsIdempotent_WhenTabMissing()
    {
        // Arrange
        var (sut, _, _) = CreateSut();

        // Act / Assert — no throw, no side effect
        var act = () => sut.CloseTabAsync(TenantA, SessionId, "never-existed");
        await act.Should().NotThrowAsync();
    }

    // =========================================================================
    // (e) GetTabs merges hot + durable; hot wins on collision
    // =========================================================================

    [Fact]
    public async Task GetTabsAsync_MergesHotAndDurable_HotWinsOnIdCollision()
    {
        // Arrange — durable tier has tab-1 with stale body; hot tier has tab-1 with fresh body; durable has unique tab-2
        var staleTab = MakeTab("tab-1", TenantA, SessionId, SummaryData("STALE"), pinned: true);
        var durableDoc = new WorkspaceStateService.WorkspaceTabDurableDocument
        {
            Id = $"workspace-tab_{TenantA}_tab-1",
            DocumentType = "workspace-tab",
            TenantId = TenantA,
            SessionId = SessionId,
            MatterId = "matter-1",
            Tab = staleTab,
        };
        var pinnedOnlyTab = MakeTab("tab-2", TenantA, SessionId, SummaryData("PINNED-ONLY"), pinned: true);
        var pinnedOnlyDoc = new WorkspaceStateService.WorkspaceTabDurableDocument
        {
            Id = $"workspace-tab_{TenantA}_tab-2",
            DocumentType = "workspace-tab",
            TenantId = TenantA,
            SessionId = SessionId,
            MatterId = "matter-1",
            Tab = pinnedOnlyTab,
        };

        var (sut, _, containerMock) = CreateSut(c =>
            SetupCosmosQuery(c, new[] { durableDoc, pinnedOnlyDoc }));

        // Hot tier has fresh tab-1
        await sut.UpsertTabAsync(TenantA, SessionId, MakeTab("tab-1", TenantA, SessionId, SummaryData("FRESH"), pinned: true));

        // Act
        var tabs = await sut.GetTabsAsync(TenantA, SessionId);

        // Assert — 2 distinct tabs; tab-1 is FRESH (hot wins); tab-2 is PINNED-ONLY (durable surfaces through merge)
        tabs.Should().HaveCount(2);
        var byId = tabs.ToDictionary(t => t.Id);
        ((SummaryTabWidgetData)byId["tab-1"].WidgetData).Body.Should().Be("FRESH");
        ((SummaryTabWidgetData)byId["tab-2"].WidgetData).Body.Should().Be("PINNED-ONLY");
    }

    [Fact]
    public async Task GetTabsAsync_ReturnsEmpty_WhenNoTabsExist()
    {
        // Arrange
        var (sut, _, containerMock) = CreateSut(SetupEmptyCosmosQuery);

        // Act
        var tabs = await sut.GetTabsAsync(TenantA, SessionId);

        // Assert
        tabs.Should().BeEmpty();
    }

    // =========================================================================
    // (f) JSON polymorphism round-trips all 4 widget-data variants
    // =========================================================================

    [Fact]
    public async Task JsonPolymorphism_RoundTripsAllFourWidgetDataVariants()
    {
        var (sut, _, _) = CreateSut();

        // Summary
        var summary = MakeTab("t-sum", TenantA, SessionId, new SummaryTabWidgetData
        {
            Body = "summary-body",
            Tldr = "tldr",
            HasUserEdits = true,
        });

        // DocumentViewer
        var doc = MakeTab("t-doc", TenantA, SessionId, new DocumentViewerTabWidgetData
        {
            DocumentId = "doc-123",
            Filename = "engagement.docx",
            MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            SizeBytes = 4567,
            HasSelection = true,
            SelectionText = "important clause",
        });

        // Dashboard
        var dashboard = MakeTab("t-dash", TenantA, SessionId, new DashboardTabWidgetData
        {
            LayoutId = "layout-guid",
            DashboardName = "Corporate Workspace",
            LastViewedSection = "section-a",
        });

        // Table
        var table = MakeTab("t-tab", TenantA, SessionId, new TableTabWidgetData
        {
            RowCount = 42,
            SortColumn = "createdOn",
            SortDirection = "desc",
            FilteredColumns = new[] { "status", "priority" },
            SelectedRows = new[] { "r1", "r2" },
            DataSourceId = "ds-1",
        });

        await sut.UpsertTabAsync(TenantA, SessionId, summary);
        await sut.UpsertTabAsync(TenantA, SessionId, doc);
        await sut.UpsertTabAsync(TenantA, SessionId, dashboard);
        await sut.UpsertTabAsync(TenantA, SessionId, table);

        var (_, _, containerMock) = CreateSut();
        SetupEmptyCosmosQuery(containerMock);

        // Reload — round-trip through Redis JSON
        var roundTripped = await sut.GetTabsAsync(TenantA, SessionId);

        // Assert — concrete subtype preserved on each variant
        roundTripped.Should().HaveCount(4);
        var byId = roundTripped.ToDictionary(t => t.Id);

        byId["t-sum"].WidgetData.Should().BeOfType<SummaryTabWidgetData>()
            .Which.Body.Should().Be("summary-body");

        var docOut = byId["t-doc"].WidgetData.Should().BeOfType<DocumentViewerTabWidgetData>().Subject;
        docOut.DocumentId.Should().Be("doc-123");
        docOut.SizeBytes.Should().Be(4567);

        byId["t-dash"].WidgetData.Should().BeOfType<DashboardTabWidgetData>()
            .Which.DashboardName.Should().Be("Corporate Workspace");

        var tableOut = byId["t-tab"].WidgetData.Should().BeOfType<TableTabWidgetData>().Subject;
        tableOut.RowCount.Should().Be(42);
        tableOut.FilteredColumns.Should().BeEquivalentTo(new[] { "status", "priority" });
        tableOut.SelectedRows.Should().BeEquivalentTo(new[] { "r1", "r2" });
    }

    // =========================================================================
    // Cosmos mock helpers
    // =========================================================================

    private static void SetupCosmosUpsertAccepts(Mock<Container> containerMock)
    {
        containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<WorkspaceStateService.WorkspaceTabDurableDocument>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<ItemResponse<WorkspaceStateService.WorkspaceTabDurableDocument>>().Object);
    }

    private static void SetupEmptyCosmosQuery(Mock<Container> containerMock)
        => SetupCosmosQuery(containerMock, Array.Empty<WorkspaceStateService.WorkspaceTabDurableDocument>());

    private static void SetupCosmosQuery(
        Mock<Container> containerMock,
        IReadOnlyList<WorkspaceStateService.WorkspaceTabDurableDocument> results)
    {
        var iteratorMock = new Mock<FeedIterator<WorkspaceStateService.WorkspaceTabDurableDocument>>();
        var responseMock = new Mock<FeedResponse<WorkspaceStateService.WorkspaceTabDurableDocument>>();
        responseMock.Setup(r => r.GetEnumerator()).Returns(results.GetEnumerator());

        var sequence = iteratorMock.SetupSequence(i => i.HasMoreResults);
        if (results.Count > 0)
        {
            sequence = sequence.Returns(true);
        }
        sequence.Returns(false);

        iteratorMock
            .Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        containerMock
            .Setup(c => c.GetItemQueryIterator<WorkspaceStateService.WorkspaceTabDurableDocument>(
                It.IsAny<QueryDefinition>(),
                It.IsAny<string>(),
                It.IsAny<QueryRequestOptions>()))
            .Returns(iteratorMock.Object);
    }
}
