using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Insights;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Insights;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights;

/// <summary>
/// Unit tests for the per-topic TTL plumbing (r1 Insights Widgets task 052 / FR-21).
/// Verifies that <see cref="InsightsPlaybookExecutionCache"/> resolves TTL precedence as:
///   1. Per-call <c>request.Ttl</c> override (power-user / test path)
///   2. Per-topic TTL from <c>sprk_aitopicregistry.sprk_cachettlminutes</c> via
///      <see cref="TopicRegistryTtlLookup"/>
///   3. <see cref="InsightsPlaybookExecutionCache.DefaultTtl"/> (5 min)
/// </summary>
/// <remarks>
/// <para>
/// The lookup is exercised through the cache's public surface (no new interface — audit
/// DR-002). Dataverse reads are mocked via <see cref="IDataverseService"/> so no Dataverse
/// round-trip is incurred.
/// </para>
/// </remarks>
public class CacheTtlPerTopicTests
{
    private static readonly Guid MatterHealthPlaybookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnregisteredPlaybookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string MatterHealthCanonicalName = "matter-health-single";
    private const string TenantId = "tenant-acme";
    private const string Subject = "matter:M-1234";
    private const string ScopeHash = "alice-scope-v1";

    // FR-05 redis remediation r1: InsightsPlaybookExecutionCache now depends on ITenantCache.
    private readonly Mock<ITenantCache> _cacheMock = new();
    private readonly Mock<IDataverseService> _dataverseMock = new();

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static InsightArtifact MakeArtifact()
        => new InferenceArtifact
        {
            Id = "inf:M-1234:matterHealth",
            Subject = Subject,
            Predicate = "matterHealth",
            Value = new Value
            {
                Raw = JsonDocument.Parse("\"green\"").RootElement.Clone(),
                DisplayHint = "status"
            },
            Confidence = 0.82,
            AsOf = DateTimeOffset.UtcNow,
            ProducedBy = new ProducedBy { Kind = "agent", Id = "agent://insights-v1", Version = "v1" },
            Scope = new Scope { TenantId = TenantId, MatterId = "M-1234" },
            TenantId = TenantId,
            Reasoning = "All 5 health checks passed."
        };

    private static async IAsyncEnumerable<PlaybookStreamEvent> EngineStreamWith(
        InsightArtifact artifact,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var runId = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        yield return PlaybookStreamEvent.RunStarted(runId, pid, 1);
        await Task.Yield();
        yield return PlaybookStreamEvent.NodeCompleted(
            runId, pid, nodeId,
            InsightsPlaybookExecutionCache.ReturnInsightArtifactNodeName,
            NodeOutput.Ok(nodeId, outputVariable: "insightArtifact", data: artifact, textContent: null));
        yield return PlaybookStreamEvent.RunCompleted(runId, pid, new PlaybookRunMetrics());
    }

    private static IOptionsMonitor<InsightsPlaybookNameMapOptions> CreateNameMap(
        Dictionary<string, Guid> map)
    {
        var options = new InsightsPlaybookNameMapOptions { Map = map };
        return Mock.Of<IOptionsMonitor<InsightsPlaybookNameMapOptions>>(m =>
            m.CurrentValue == options);
    }

    /// <summary>
    /// Build an <see cref="EntityCollection"/> mimicking the Dataverse shape of one
    /// <c>sprk_aitopicregistry</c> row with the supplied playbook name + TTL minutes.
    /// </summary>
    private static EntityCollection MakeRegistryRows(
        params (string playbookName, int? cacheTtlMinutes)[] rows)
    {
        var collection = new EntityCollection();
        foreach (var (name, minutes) in rows)
        {
            var entity = new Entity("sprk_aitopicregistry", Guid.NewGuid());
            entity["sprk_playbookname"] = name;
            if (minutes.HasValue)
            {
                entity["sprk_cachettlminutes"] = minutes.Value;
            }
            collection.Entities.Add(entity);
        }
        return collection;
    }

    private TopicRegistryTtlLookup CreateLookup(
        Dictionary<string, Guid>? nameMap = null,
        params (string playbookName, int? cacheTtlMinutes)[] registryRows)
    {
        nameMap ??= new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            [MatterHealthCanonicalName] = MatterHealthPlaybookId,
        };

        _dataverseMock
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRegistryRows(registryRows));

        return new TopicRegistryTtlLookup(
            _dataverseMock.Object,
            CreateNameMap(nameMap),
            NullLogger<TopicRegistryTtlLookup>.Instance,
            timeProvider: null,
            refreshInterval: TimeSpan.FromMinutes(5));
    }

    private InsightsPlaybookExecutionCache CreateSut(TopicRegistryTtlLookup? lookup)
        => new(
            _cacheMock.Object,
            NullLogger<InsightsPlaybookExecutionCache>.Instance,
            metrics: null,
            registryTtl: lookup);

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrExecuteAsync_RegistryTtlApplied_WhenRequestOmitsTtlAndRegistryHasRow()
    {
        // FR-21: per-topic TTL from sprk_aitopicregistry.sprk_cachettlminutes (60 min for
        // matter-health-single per the seed row in task 013) must override the cache's
        // 5-minute DefaultTtl when request.Ttl is null.

        _cacheMock
            .Setup(c => c.GetAsync<InsightArtifact>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InsightArtifact?)null);

        var lookup = CreateLookup(
            registryRows: (MatterHealthCanonicalName, 60));
        var sut = CreateSut(lookup);

        var request = new InsightsPlaybookExecutionRequest(
            MatterHealthPlaybookId, Subject, null, ScopeHash, TenantId, Ttl: null);

        var artifact = MakeArtifact();

        await sut.GetOrExecuteAsync(request, ct => EngineStreamWith(artifact, ct));

        _cacheMock.Verify(
            c => c.SetAsync<InsightArtifact>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<InsightArtifact>(),
                It.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(60)),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "registry TTL (60 min) must be applied when request.Ttl is null");
    }

    [Fact]
    public async Task GetOrExecuteAsync_RequestTtlBeatsRegistry_WhenBothPresent()
    {
        // Precedence rule: per-call Ttl override (e.g., test, power-user invocation)
        // takes priority over the registry value. This preserves the existing public
        // contract on InsightsPlaybookExecutionRequest.Ttl.

        _cacheMock
            .Setup(c => c.GetAsync<InsightArtifact>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InsightArtifact?)null);

        var lookup = CreateLookup(
            registryRows: (MatterHealthCanonicalName, 60));
        var sut = CreateSut(lookup);

        var explicitTtl = TimeSpan.FromMinutes(7);
        var request = new InsightsPlaybookExecutionRequest(
            MatterHealthPlaybookId, Subject, null, ScopeHash, TenantId, Ttl: explicitTtl);

        await sut.GetOrExecuteAsync(request, ct => EngineStreamWith(MakeArtifact(), ct));

        _cacheMock.Verify(
            c => c.SetAsync<InsightArtifact>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<InsightArtifact>(),
                It.Is<TimeSpan?>(t => t == explicitTtl),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "per-call Ttl override must beat the registry value");
    }

    [Fact]
    public async Task GetOrExecuteAsync_DefaultTtlApplied_WhenPlaybookNotRegistered()
    {
        // Defensive: a Guid that is not registered in InsightsPlaybookNameMapOptions
        // (advanced direct-Guid path per InsightEndpoints contract) cannot be reverse-
        // mapped to a registry row → falls back to DefaultTtl.

        _cacheMock
            .Setup(c => c.GetAsync<InsightArtifact>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InsightArtifact?)null);

        var lookup = CreateLookup(
            registryRows: (MatterHealthCanonicalName, 60));
        var sut = CreateSut(lookup);

        var request = new InsightsPlaybookExecutionRequest(
            UnregisteredPlaybookId, Subject, null, ScopeHash, TenantId, Ttl: null);

        await sut.GetOrExecuteAsync(request, ct => EngineStreamWith(MakeArtifact(), ct));

        _cacheMock.Verify(
            c => c.SetAsync<InsightArtifact>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<InsightArtifact>(),
                It.Is<TimeSpan?>(t => t == InsightsPlaybookExecutionCache.DefaultTtl),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "unregistered playbook id must fall back to DefaultTtl");
    }

    [Fact]
    public async Task GetOrExecuteAsync_DefaultTtlApplied_WhenLookupNotInjected()
    {
        // Backward compatibility: the existing constructor used by other tests omits the
        // registryTtl param entirely. Behavior must remain identical to pre-task-052 —
        // DefaultTtl is used when request.Ttl is null.

        _cacheMock
            .Setup(c => c.GetAsync<InsightArtifact>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InsightArtifact?)null);

        // No lookup — exercises the (_registryTtl == null) branch
        var sut = CreateSut(lookup: null);

        var request = new InsightsPlaybookExecutionRequest(
            MatterHealthPlaybookId, Subject, null, ScopeHash, TenantId, Ttl: null);

        await sut.GetOrExecuteAsync(request, ct => EngineStreamWith(MakeArtifact(), ct));

        _cacheMock.Verify(
            c => c.SetAsync<InsightArtifact>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<InsightArtifact>(),
                It.Is<TimeSpan?>(t => t == InsightsPlaybookExecutionCache.DefaultTtl),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "when no TopicRegistryTtlLookup is injected, behavior must be unchanged from pre-052");
    }

    [Fact]
    public async Task TopicRegistryTtlLookup_OutOfBoundsTtl_ClampedToSchemaBounds()
    {
        // Defensive bounds: schema enforces 1..1440 per topic-registry-schema-design.md
        // §3.1 row 8, but a stale row might violate that. The lookup clamps to bounds
        // rather than passing an invalid TimeSpan to the cache.

        _cacheMock
            .Setup(c => c.GetAsync<InsightArtifact>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InsightArtifact?)null);

        var lookup = CreateLookup(
            registryRows: (MatterHealthCanonicalName, 5000));
        var sut = CreateSut(lookup);

        var request = new InsightsPlaybookExecutionRequest(
            MatterHealthPlaybookId, Subject, null, ScopeHash, TenantId, Ttl: null);

        await sut.GetOrExecuteAsync(request, ct => EngineStreamWith(MakeArtifact(), ct));

        _cacheMock.Verify(
            c => c.SetAsync<InsightArtifact>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<InsightArtifact>(),
                It.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(1440)),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "out-of-bounds TTL (5000 min) must be clamped to schema max (1440 min)");
    }

    [Fact]
    public async Task TopicRegistryTtlLookup_RefreshOnce_DataverseHitOnlyOnceForSameWindow()
    {
        // Avoid per-call Dataverse lookup (task 052 explicit constraint). Multiple calls
        // within the refresh interval must share the in-process snapshot — Dataverse is
        // hit AT MOST ONCE for N calls in the window.

        _cacheMock
            .Setup(c => c.GetAsync<InsightArtifact>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InsightArtifact?)null);

        var lookup = CreateLookup(
            registryRows: (MatterHealthCanonicalName, 60));
        var sut = CreateSut(lookup);

        var request = new InsightsPlaybookExecutionRequest(
            MatterHealthPlaybookId, Subject, null, ScopeHash, TenantId, Ttl: null);

        // 5 cache misses → 5 calls to the cache → all share the lookup snapshot
        for (int i = 0; i < 5; i++)
        {
            await sut.GetOrExecuteAsync(request, ct => EngineStreamWith(MakeArtifact(), ct));
        }

        _dataverseMock.Verify(
            d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "in-process mirror must refresh from Dataverse at most once per refresh window");
    }

    [Fact]
    public async Task TopicRegistryTtlLookup_DataverseFailure_FallsBackToDefaultTtl()
    {
        // Graceful degradation per ADR-009: a Dataverse failure must NOT prevent the cache
        // from operating. The lookup logs Warning and surfaces "no entry"; the cache uses
        // DefaultTtl.

        _cacheMock
            .Setup(c => c.GetAsync<InsightArtifact>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InsightArtifact?)null);

        _dataverseMock
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse transient failure"));

        var nameMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            [MatterHealthCanonicalName] = MatterHealthPlaybookId,
        };
        var lookup = new TopicRegistryTtlLookup(
            _dataverseMock.Object,
            CreateNameMap(nameMap),
            NullLogger<TopicRegistryTtlLookup>.Instance);

        var sut = CreateSut(lookup);

        var request = new InsightsPlaybookExecutionRequest(
            MatterHealthPlaybookId, Subject, null, ScopeHash, TenantId, Ttl: null);

        var act = async () => await sut.GetOrExecuteAsync(
            request, ct => EngineStreamWith(MakeArtifact(), ct));

        await act.Should().NotThrowAsync("Dataverse failure must not propagate to the cache caller");

        _cacheMock.Verify(
            c => c.SetAsync<InsightArtifact>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<InsightArtifact>(),
                It.Is<TimeSpan?>(t => t == InsightsPlaybookExecutionCache.DefaultTtl),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "Dataverse failure → fallback to DefaultTtl per ADR-009 graceful degradation");
    }

    [Fact]
    public async Task TopicRegistryTtlLookup_QueryFiltersEnabledAndActiveRowsOnly()
    {
        // Per topic-registry-schema-design.md §5.3 "Active topics" view: only enabled
        // (sprk_enabled=true) AND active (statecode=0) rows are eligible. The lookup
        // must apply both filters server-side so a disabled topic does NOT contribute
        // a stale TTL.

        QueryExpression? capturedQuery = null;
        _dataverseMock
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => capturedQuery = q)
            .ReturnsAsync(new EntityCollection());

        var nameMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            [MatterHealthCanonicalName] = MatterHealthPlaybookId,
        };
        var lookup = new TopicRegistryTtlLookup(
            _dataverseMock.Object,
            CreateNameMap(nameMap),
            NullLogger<TopicRegistryTtlLookup>.Instance);

        await lookup.TryGetTtlForPlaybookNameAsync(MatterHealthCanonicalName);

        capturedQuery.Should().NotBeNull();
        capturedQuery!.EntityName.Should().Be("sprk_aitopicregistry");

        var conditions = capturedQuery.Criteria.Conditions;
        conditions.Should().ContainSingle(c => c.AttributeName == "statecode"
            && c.Operator == ConditionOperator.Equal
            && (int)c.Values[0] == 0,
            "active rows only");
        conditions.Should().ContainSingle(c => c.AttributeName == "sprk_enabled"
            && c.Operator == ConditionOperator.Equal
            && (bool)c.Values[0],
            "enabled rows only (FR-09 SME toggle)");
    }
}
