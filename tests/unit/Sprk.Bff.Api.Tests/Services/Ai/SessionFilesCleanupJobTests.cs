using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Chat;
using StackExchange.Redis;
using Xunit;
using AzureIndexingResult = Azure.Search.Documents.Models.IndexingResult;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for <see cref="SessionFilesCleanupJob"/> (R5 task 007 / D1-07).
///
/// Acceptance criteria (from task 007 POML §10):
///   1. EvictSession with zero matching docs is a zero-count no-op (idempotency).
///   2. EvictSession emits r5.session_files_cleanup.run telemetry with trigger + counts.
///   3. EvictSession filter includes BOTH tenantId AND sessionId (ADR-014).
///   4. EvictSession invoked twice for same session is idempotent.
///   5. OnSignalReceived drains all pending signals in single batch.
///   6. ScheduledScan evicts only orphans not in active Redis set.
///   7. ScheduledScan enforces per-tenant isolation (no cross-tenant delete).
/// </summary>
public class SessionFilesCleanupJobTests
{
    private const string SessionFilesIndexName = "test-session-files-index";
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string SessionId1 = "session-001";
    private const string SessionId2 = "session-002";

    private readonly Mock<SearchIndexClient> _searchIndexClientMock;
    private readonly Mock<SearchClient> _sessionFilesClientMock;
    private readonly Mock<IConnectionMultiplexer> _multiplexerMock;
    private readonly Mock<IDatabase> _redisDatabaseMock;
    private readonly Mock<ILogger<SessionFilesCleanupJob>> _loggerMock;
    private readonly SessionFilesCleanupSignal _signal;
    private readonly IOptions<SessionFilesCleanupOptions> _options;
    private readonly IOptions<AiSearchOptions> _aiSearchOptions;

    public SessionFilesCleanupJobTests()
    {
        _searchIndexClientMock = new Mock<SearchIndexClient>();
        _sessionFilesClientMock = new Mock<SearchClient>();
        _multiplexerMock = new Mock<IConnectionMultiplexer>();
        _redisDatabaseMock = new Mock<IDatabase>();
        _loggerMock = new Mock<ILogger<SessionFilesCleanupJob>>();
        _signal = new SessionFilesCleanupSignal();
        _options = Options.Create(new SessionFilesCleanupOptions
        {
            IntervalHours = 6,
            DeleteBatchSize = 1000,
            MaxKeysPerScan = 10_000,
        });
        _aiSearchOptions = Options.Create(new AiSearchOptions
        {
            SessionFilesIndexName = SessionFilesIndexName,
        });

        _searchIndexClientMock
            .Setup(x => x.GetSearchClient(SessionFilesIndexName))
            .Returns(_sessionFilesClientMock.Object);

        _multiplexerMock
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_redisDatabaseMock.Object);
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    private SessionFilesCleanupJob CreateJob(IServiceProvider services) =>
        new SessionFilesCleanupJob(
            services,
            _options,
            _signal,
            _loggerMock.Object);

    private IServiceProvider BuildScopedServices(bool includeMultiplexer = true)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton(_searchIndexClientMock.Object);
        sc.AddSingleton(_aiSearchOptions);
        if (includeMultiplexer)
        {
            sc.AddSingleton(_multiplexerMock.Object);
        }
        return sc.BuildServiceProvider();
    }

    // -------------------------------------------------------------------------
    // Helper: mock Azure Search "search returns N matching documents"
    // -------------------------------------------------------------------------

    private void SetupSearchReturning(IReadOnlyList<string> docIds)
    {
        var searchResults = docIds
            .Select(id => SearchModelFactory.SearchResult(
                new SessionFilesCleanupKey { Id = id },
                score: 1.0, highlights: null))
            .ToList();

        var results = SearchModelFactory.SearchResults<SessionFilesCleanupKey>(
            values: searchResults,
            totalCount: docIds.Count,
            facets: null,
            coverage: null,
            rawResponse: null!);

        _sessionFilesClientMock
            .Setup(x => x.SearchAsync<SessionFilesCleanupKey>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(results, null!));
    }

    private void SetupDeleteDocsSucceeding(IReadOnlyList<string> docIds)
    {
        var indexingResults = docIds
            .Select(id => SearchModelFactory.IndexingResult(id, null, true, 200))
            .ToList();
        var result = SearchModelFactory.IndexDocumentsResult(indexingResults);

        _sessionFilesClientMock
            .Setup(x => x.DeleteDocumentsAsync(
                "id",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(result, null!));
    }

    // -------------------------------------------------------------------------
    // Test 1: EvictSession_with_no_matching_docs_emits_zero_count_no_op
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EvictSessionAsync_NoMatchingDocs_IsZeroCountNoOp()
    {
        // Arrange — search returns empty results
        SetupSearchReturning(Array.Empty<string>());

        var services = BuildScopedServices();
        var job = CreateJob(services);

        // Act
        var deleted = await job.EvictSessionAsync(
            services, TenantA, SessionId1,
            SessionFilesCleanupJob.TriggerOnSessionEnd, CancellationToken.None);

        // Assert
        deleted.Should().Be(0, "no documents matched the (tenantId, sessionId) filter");

        // DeleteDocumentsAsync must NOT be invoked on a zero-results path
        _sessionFilesClientMock.Verify(x => x.DeleteDocumentsAsync(
            It.IsAny<string>(),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<IndexDocumentsOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Never,
            "the idempotent no-op path skips the delete batch");
    }

    // -------------------------------------------------------------------------
    // Test 2: EvictSession_emits_telemetry_with_trigger_and_counts
    // (smoke — verify the method completes with no exception when telemetry is emitted)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EvictSessionAsync_HappyPath_CompletesAndEmitsTelemetry()
    {
        // Arrange — search returns 3 documents, delete succeeds.
        var docIds = new List<string> { "doc-1", "doc-2", "doc-3" };
        SetupSearchReturning(docIds);
        SetupDeleteDocsSucceeding(docIds);

        var services = BuildScopedServices();
        var job = CreateJob(services);

        // Act
        var deleted = await job.EvictSessionAsync(
            services, TenantA, SessionId1,
            SessionFilesCleanupJob.TriggerOnSessionEnd, CancellationToken.None);

        // Assert — return value matches deleted-doc count
        deleted.Should().Be(3);

        // Delete batch was invoked exactly once
        _sessionFilesClientMock.Verify(x => x.DeleteDocumentsAsync(
            "id",
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<IndexDocumentsOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // The trigger constant we expect to surface on telemetry is the canonical "on_session_end"
        SessionFilesCleanupJob.TriggerOnSessionEnd.Should().Be("on_session_end",
            "task 008 dashboards lock the trigger enum to this exact string");
        SessionFilesCleanupJob.TriggerScheduled.Should().Be("scheduled",
            "task 008 dashboards lock the trigger enum to this exact string");
    }

    // -------------------------------------------------------------------------
    // Test 3: EvictSession_filter_includes_both_tenantId_and_sessionId (ADR-014)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EvictSessionAsync_Filter_Includes_Both_TenantId_And_SessionId_AdrR14()
    {
        // Arrange — capture the SearchOptions filter string passed to SearchAsync.
        string? capturedFilter = null;
        var emptyResults = SearchModelFactory.SearchResults<SessionFilesCleanupKey>(
            values: new List<SearchResult<SessionFilesCleanupKey>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        _sessionFilesClientMock
            .Setup(x => x.SearchAsync<SessionFilesCleanupKey>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SearchOptions, CancellationToken>(
                (_, opts, _) => capturedFilter = opts.Filter)
            .ReturnsAsync(Response.FromValue(emptyResults, null!));

        var services = BuildScopedServices();
        var job = CreateJob(services);

        // Act
        await job.EvictSessionAsync(
            services, TenantA, SessionId1,
            SessionFilesCleanupJob.TriggerOnSessionEnd, CancellationToken.None);

        // Assert — filter shape exactly per ADR-014
        capturedFilter.Should().NotBeNull();
        capturedFilter.Should().Contain($"tenantId eq '{TenantA}'",
            "ADR-014 binds the tenantId predicate to every session-files delete filter");
        capturedFilter.Should().Contain($"sessionId eq '{SessionId1}'",
            "R5 FR-09 binds the sessionId predicate to every session-files delete filter");
        capturedFilter.Should().Contain(" and ",
            "both predicates must be AND-joined — never OR-joined");
    }

    // -------------------------------------------------------------------------
    // Test 4: EvictSession_invoked_twice_for_same_session_is_idempotent
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EvictSessionAsync_InvokedTwiceForSameSession_IsIdempotent()
    {
        // Arrange — first call returns 2 docs; second call returns 0.
        var docIds = new List<string> { "doc-1", "doc-2" };

        // Use a sequence of return values: first call has results, second call empty.
        var callCount = 0;
        _sessionFilesClientMock
            .Setup(x => x.SearchAsync<SessionFilesCleanupKey>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    var results = SearchModelFactory.SearchResults<SessionFilesCleanupKey>(
                        values: docIds.Select(id => SearchModelFactory.SearchResult(
                            new SessionFilesCleanupKey { Id = id }, 1.0, null)).ToList(),
                        totalCount: docIds.Count, facets: null, coverage: null, rawResponse: null!);
                    return Response.FromValue(results, null!);
                }
                else
                {
                    var emptyResults = SearchModelFactory.SearchResults<SessionFilesCleanupKey>(
                        values: new List<SearchResult<SessionFilesCleanupKey>>(),
                        totalCount: 0, facets: null, coverage: null, rawResponse: null!);
                    return Response.FromValue(emptyResults, null!);
                }
            });

        SetupDeleteDocsSucceeding(docIds);

        var services = BuildScopedServices();
        var job = CreateJob(services);

        // Act — invoke twice
        var firstDeleted = await job.EvictSessionAsync(
            services, TenantA, SessionId1,
            SessionFilesCleanupJob.TriggerOnSessionEnd, CancellationToken.None);
        var secondDeleted = await job.EvictSessionAsync(
            services, TenantA, SessionId1,
            SessionFilesCleanupJob.TriggerOnSessionEnd, CancellationToken.None);

        // Assert — first pass deletes 2, second pass is no-op (idempotent contract)
        firstDeleted.Should().Be(2, "first call finds and deletes both documents");
        secondDeleted.Should().Be(0, "second call finds nothing — idempotent no-op");

        // Delete batch only fires for the first invocation
        _sessionFilesClientMock.Verify(x => x.DeleteDocumentsAsync(
            "id",
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<IndexDocumentsOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Once,
            "idempotent re-run must NOT issue a second delete batch when nothing matches");
    }

    // -------------------------------------------------------------------------
    // Test 5: OnSignalReceived_drains_all_pending_signals_in_single_batch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DrainPendingSignalsAsync_Drains_All_Pending_Signals_In_Single_Batch()
    {
        // Arrange — pre-load the channel with three signals across two tenants.
        _signal.SignalSessionEnded(TenantA, SessionId1);
        _signal.SignalSessionEnded(TenantA, SessionId2);
        _signal.SignalSessionEnded(TenantB, SessionId1);

        // Each search call returns one matching doc to make the delete path observable.
        var callIndex = 0;
        _sessionFilesClientMock
            .Setup(x => x.SearchAsync<SessionFilesCleanupKey>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var id = $"doc-{++callIndex}";
                var results = SearchModelFactory.SearchResults<SessionFilesCleanupKey>(
                    values: new List<SearchResult<SessionFilesCleanupKey>>
                    {
                        SearchModelFactory.SearchResult(new SessionFilesCleanupKey { Id = id }, 1.0, null),
                    },
                    totalCount: 1, facets: null, coverage: null, rawResponse: null!);
                return Response.FromValue(results, null!);
            });

        var indexingResults = new List<AzureIndexingResult>
        {
            SearchModelFactory.IndexingResult("doc", null, true, 200),
        };
        _sessionFilesClientMock
            .Setup(x => x.DeleteDocumentsAsync(
                "id",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(
                SearchModelFactory.IndexDocumentsResult(indexingResults), null!));

        var services = BuildScopedServices();
        var job = CreateJob(services);

        // Act — drain all pending signals
        await job.DrainPendingSignalsAsync(CancellationToken.None);

        // Assert — three search calls (one per signal) and three deletes (one per signal)
        _sessionFilesClientMock.Verify(x => x.SearchAsync<SessionFilesCleanupKey>(
            It.IsAny<string>(),
            It.IsAny<SearchOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "each pending signal triggers a search query");

        _sessionFilesClientMock.Verify(x => x.DeleteDocumentsAsync(
            "id",
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<IndexDocumentsOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "each non-empty result set triggers a delete batch");
    }

    // -------------------------------------------------------------------------
    // Test 6: ScheduledScan_evicts_only_orphans_not_in_active_set
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunScheduledScanAsync_Evicts_Only_Orphans_Not_In_Active_Set()
    {
        // Arrange:
        //   Indexed sessions in spaarke-session-files: (TenantA, SessionId1), (TenantA, SessionId2)
        //   Active Redis keys: only chat:session:TenantA:SessionId1 exists
        //   → orphan: (TenantA, SessionId2) — should be evicted
        var indexedDocs = new List<SearchResult<SessionFilesCleanupRef>>
        {
            SearchModelFactory.SearchResult(
                new SessionFilesCleanupRef { Id = "d1", TenantId = TenantA, SessionId = SessionId1 },
                1.0, null),
            SearchModelFactory.SearchResult(
                new SessionFilesCleanupRef { Id = "d2", TenantId = TenantA, SessionId = SessionId2 },
                1.0, null),
        };
        var indexedResults = SearchModelFactory.SearchResults<SessionFilesCleanupRef>(
            values: indexedDocs,
            totalCount: indexedDocs.Count, facets: null, coverage: null, rawResponse: null!);

        _sessionFilesClientMock
            .Setup(x => x.SearchAsync<SessionFilesCleanupRef>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(indexedResults, null!));

        // Redis: only SessionId1 exists for TenantA
        _redisDatabaseMock
            .Setup(d => d.KeyExistsAsync(
                It.Is<RedisKey>(k => k == (RedisKey)$"chat:session:{TenantA}:{SessionId1}"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _redisDatabaseMock
            .Setup(d => d.KeyExistsAsync(
                It.Is<RedisKey>(k => k == (RedisKey)$"chat:session:{TenantA}:{SessionId2}"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Orphan eviction: search for SessionId2 docs returns one doc; delete succeeds.
        SetupSearchReturning(new List<string> { "orphan-doc-1" });
        SetupDeleteDocsSucceeding(new List<string> { "orphan-doc-1" });

        var services = BuildScopedServices(includeMultiplexer: true);
        var job = CreateJob(services);

        // Act
        await job.RunScheduledScanAsync(CancellationToken.None);

        // Assert — SessionId2 (orphan) was queried + deleted
        _sessionFilesClientMock.Verify(x => x.DeleteDocumentsAsync(
            "id",
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<IndexDocumentsOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Once,
            "exactly one orphan session is evicted by the scheduled scan");

        // Active SessionId1 is NOT evicted — the absence of a second delete call confirms isolation.
        // Active sessions are checked via Redis KeyExistsAsync; verify both keys were checked.
        _redisDatabaseMock.Verify(d => d.KeyExistsAsync(
            It.Is<RedisKey>(k => k == (RedisKey)$"chat:session:{TenantA}:{SessionId1}"),
            It.IsAny<CommandFlags>()),
            Times.Once);
        _redisDatabaseMock.Verify(d => d.KeyExistsAsync(
            It.Is<RedisKey>(k => k == (RedisKey)$"chat:session:{TenantA}:{SessionId2}"),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // Test 7: ScheduledScan_per_tenant_isolation_no_cross_tenant_delete
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunScheduledScanAsync_Per_Tenant_Isolation_No_Cross_Tenant_Delete()
    {
        // Arrange:
        //   Indexed sessions:
        //     (TenantA, SessionId1) — orphan (Redis empty)
        //     (TenantB, SessionId1) — orphan (Redis empty)
        //
        //   Capture every delete filter and assert each carries its own tenantId.
        var indexedDocs = new List<SearchResult<SessionFilesCleanupRef>>
        {
            SearchModelFactory.SearchResult(
                new SessionFilesCleanupRef { Id = "d1", TenantId = TenantA, SessionId = SessionId1 },
                1.0, null),
            SearchModelFactory.SearchResult(
                new SessionFilesCleanupRef { Id = "d2", TenantId = TenantB, SessionId = SessionId1 },
                1.0, null),
        };
        var indexedResults = SearchModelFactory.SearchResults<SessionFilesCleanupRef>(
            values: indexedDocs,
            totalCount: indexedDocs.Count, facets: null, coverage: null, rawResponse: null!);

        _sessionFilesClientMock
            .Setup(x => x.SearchAsync<SessionFilesCleanupRef>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(indexedResults, null!));

        _redisDatabaseMock
            .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false); // No active Redis keys → both sessions are orphans.

        // Capture every search filter issued for the orphan eviction passes.
        var capturedFilters = new List<string>();
        _sessionFilesClientMock
            .Setup(x => x.SearchAsync<SessionFilesCleanupKey>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SearchOptions, CancellationToken>(
                (_, opts, _) =>
                {
                    if (!string.IsNullOrEmpty(opts.Filter)) capturedFilters.Add(opts.Filter);
                })
            .ReturnsAsync(Response.FromValue(
                SearchModelFactory.SearchResults<SessionFilesCleanupKey>(
                    values: new List<SearchResult<SessionFilesCleanupKey>>(),
                    totalCount: 0, facets: null, coverage: null, rawResponse: null!),
                null!));

        var services = BuildScopedServices(includeMultiplexer: true);
        var job = CreateJob(services);

        // Act
        await job.RunScheduledScanAsync(CancellationToken.None);

        // Assert — exactly two orphan eviction filters issued, one per tenant.
        capturedFilters.Should().HaveCount(2,
            "two distinct (tenantId, sessionId) orphans → two eviction filters");

        // Each filter carries its own tenant, and there is NO mention of the other tenant.
        var tenantAFilter = capturedFilters.Single(f => f.Contains($"tenantId eq '{TenantA}'"));
        tenantAFilter.Should().NotContain($"tenantId eq '{TenantB}'",
            "ADR-014 + NFR-03: per-tenant filters MUST NOT reference another tenant's ID");

        var tenantBFilter = capturedFilters.Single(f => f.Contains($"tenantId eq '{TenantB}'"));
        tenantBFilter.Should().NotContain($"tenantId eq '{TenantA}'",
            "ADR-014 + NFR-03: per-tenant filters MUST NOT reference another tenant's ID");
    }

    // -------------------------------------------------------------------------
    // Extra: Signal contract — null/empty input is a silent no-op
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionFilesCleanupSignal_SignalSessionEnded_NullEmptyInputs_NoOp()
    {
        // Arrange
        var signal = new SessionFilesCleanupSignal();

        // Act + Assert — fire-and-forget contract: null/empty inputs MUST silently no-op.
        signal.SignalSessionEnded(null!, "sess");
        signal.SignalSessionEnded("", "sess");
        signal.SignalSessionEnded("tenant", null!);
        signal.SignalSessionEnded("tenant", "");

        // No signals should be available on the reader.
        // Note: ChannelReader<T>.Count is not supported on unbounded channels; assert via TryRead
        // without a reason string (FluentAssertions otherwise inspects subject properties for the
        // failure message and triggers get_Count).
        var couldReadInvalid = signal.Reader.TryRead(out _);
        couldReadInvalid.Should().BeFalse();

        // Valid input enqueues exactly one signal.
        signal.SignalSessionEnded("tenant", "sess");
        var couldReadValid = signal.Reader.TryRead(out var read);
        couldReadValid.Should().BeTrue();
        read.TenantId.Should().Be("tenant");
        read.SessionId.Should().Be("sess");
    }
}
