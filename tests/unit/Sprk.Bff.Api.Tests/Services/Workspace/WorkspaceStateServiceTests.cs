using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
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
/// AIR2-075 (2026-07-10): the tab WRITE path (upsert / pin / close) was retired with the
/// orphaned Get/Update/Close Workspace Tab tools + the SendWorkspaceArtifact legacy variants.
/// The service is now READ-ONLY; only the read-path acceptance criteria remain:
///   (a) Per-tenant cache-key isolation — two tenants, same sessionId → different keys.
///   (e) GetTabs merges hot (Redis) + durable (Cosmos) rows; hot wins on tab-id collision.
///   (f) JSON polymorphism round-trips each of the 4 widget-data variants through the hot tier.
///
/// The hot tier is seeded DIRECTLY through the in-memory <see cref="ITenantCache"/> (the write
/// path that used to seed it via UpsertTabAsync is retired). Cosmos interactions are verified
/// via the Moq <see cref="Container"/> + injected <see cref="CosmosClient"/>.
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

    /// <summary>
    /// Seed the Redis hot tier directly (the retired write path used to do this via
    /// UpsertTabAsync). Writes a <c>tabId → WorkspaceTab</c> dictionary under the canonical
    /// workspace-state cache key so <see cref="WorkspaceStateService.GetTabsAsync"/> reads it.
    /// </summary>
    private static Task SeedHotAsync(FakeTenantCache cache, string tenantId, string sessionId, params WorkspaceTab[] tabs)
        => cache.SetAsync(
            tenantId,
            WorkspaceStateService.CacheResource,
            sessionId,
            WorkspaceStateService.CacheVersion,
            tabs.ToDictionary(t => t.Id, t => t, StringComparer.Ordinal),
            TimeSpan.FromHours(24));

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
    public async Task GetTabsAsync_ReturnsOnlyOwnTenantData()
    {
        // Arrange
        var (sut, cache, containerMock) = CreateSut();
        SetupEmptyCosmosQuery(containerMock);

        await SeedHotAsync(cache, TenantA, SessionId, MakeTab("tab-1", TenantA, SessionId, SummaryData("for-A")));
        await SeedHotAsync(cache, TenantB, SessionId, MakeTab("tab-1", TenantB, SessionId, SummaryData("for-B")));

        // Act
        var tabsA = await sut.GetTabsAsync(TenantA, SessionId);
        var tabsB = await sut.GetTabsAsync(TenantB, SessionId);

        // Assert — each tenant's hot key is isolated; no cross-tenant bleed.
        tabsA.Should().HaveCount(1);
        ((SummaryTabWidgetData)tabsA[0].WidgetData).Body.Should().Be("for-A");
        tabsA[0].TenantId.Should().Be(TenantA);

        tabsB.Should().HaveCount(1);
        ((SummaryTabWidgetData)tabsB[0].WidgetData).Body.Should().Be("for-B");
        tabsB[0].TenantId.Should().Be(TenantB);

        // Two distinct tenant-scoped keys exist.
        cache.Store.Should().ContainKey($"tenant:{TenantA}:workspace-state:{SessionId}:v1");
        cache.Store.Should().ContainKey($"tenant:{TenantB}:workspace-state:{SessionId}:v1");
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

        var (sut, cache, _) = CreateSut(c =>
            SetupCosmosQuery(c, new[] { durableDoc, pinnedOnlyDoc }));

        // Hot tier has fresh tab-1
        await SeedHotAsync(cache, TenantA, SessionId, MakeTab("tab-1", TenantA, SessionId, SummaryData("FRESH"), pinned: true));

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
    // (f) JSON polymorphism round-trips all 4 widget-data variants through the hot tier
    // =========================================================================

    [Fact]
    public async Task JsonPolymorphism_RoundTripsAllFourWidgetDataVariants()
    {
        var (sut, cache, containerMock) = CreateSut(SetupEmptyCosmosQuery);

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

        await SeedHotAsync(cache, TenantA, SessionId, summary, doc, dashboard, table);

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
