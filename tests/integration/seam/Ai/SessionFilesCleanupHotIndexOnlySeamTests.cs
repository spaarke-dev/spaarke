using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Tests.Mocks;
using StackExchange.Redis;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Seam.Ai;

/// <summary>
/// <c>tests/integration/seam/**</c> — vertical-slice seam for spaarkeai-compose-r8 FR-B03 (task 061):
/// running the real <see cref="SessionFilesCleanupJob"/> against a populated durable byte store must
/// evict the hot AI-Search index and leave every durable blob exactly where it was.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this test is worth writing even though the job never mentions the store.</b> The structural
/// half of FR-B03 is guarded by <c>tests/Spaarke.ArchTests/SessionFilesCleanupScopeTests.cs</c>, which
/// proves the call is absent. This half proves the OUTCOME: after a real eviction pass — both triggers,
/// scheduled and on-session-end — the bytes are still readable through the production store, by the
/// production API. A guard that only checks for an absent call would still pass if the durable copy were
/// destroyed by some route nobody thought to name.
/// </para>
/// <para>
/// <b>Real path, one boundary faked.</b> The job, its telemetry, its OData filter construction, its
/// batching and the <see cref="SessionFileBlobStore"/> are all production types. Only the Azure SDK
/// boundaries are substituted: a <see cref="SearchClient"/> mock standing in for the session-files index
/// and <see cref="InMemorySessionFileBlobGateway"/> standing in for Blob Storage. This session has
/// neither a search service nor a storage account.
/// </para>
/// <para>
/// <b>What was and was not observed failing.</b> The durable-survival assertion was verified
/// non-vacuous: clearing the gateway between the sweep and the readback turned
/// <see cref="ScheduledOrphanSweep_LeavesTheDurableCopyIntact"/> red on exactly the intended message,
/// while the hot-index assertions stayed green — the asymmetry that shows the index assertions alone
/// cannot tell a correct sweep from a destructive one. A PRODUCTION-side break (an actual durable delete
/// inside <c>EvictSessionAsync</c>) is <b>not constructible today</b>, because
/// <see cref="SessionFileBlobStore"/> deliberately exposes no delete at all — which is precisely the
/// state task 063 (GDPR erasure) changes. When it does, this suite is what turns the newly-reachable
/// delete into a failing test rather than a support ticket on day 60; the structural half
/// (<c>tests/Spaarke.ArchTests/SessionFilesCleanupScopeTests.cs</c>) WAS observed failing under a real
/// reach — three guards fired on a one-line <c>GetService&lt;SessionFileBlobStore&gt;()</c>.
/// </para>
/// </remarks>
public sealed class SessionFilesCleanupHotIndexOnlySeamTests
{
    private const string IndexName = "test-session-files-index";
    private const string TenantA = "00000000-0000-0000-0000-00000000aaaa";
    private const string TenantB = "ffffffff-eeee-dddd-cccc-bbbbbbbbbbbb";
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private const string FileId = "aaaaaaaabbbbccccddddeeeeeeeeeeee";
    private const string RedisInstancePrefix = "spaarke:";

    private static readonly byte[] DurableContent =
        System.Text.Encoding.UTF8.GetBytes("Ninety days of settlement terms. Must survive the 24h sweep.");

    private readonly Mock<SearchClient> _hotIndex = new();
    private readonly Mock<IConnectionMultiplexer> _multiplexer = new();
    private readonly Mock<IDatabase> _redis = new();
    private readonly InMemorySessionFileBlobGateway _durableBlobs = new();
    private readonly SessionFileBlobStore _durableStore;
    private readonly SessionFilesCleanupSignal _signal = new();

    private readonly List<IEnumerable<string>> _deletedFromHotIndex = new();

    public SessionFilesCleanupHotIndexOnlySeamTests()
    {
        _durableStore = new SessionFileBlobStore(_durableBlobs, NullLogger<SessionFileBlobStore>.Instance);
        _multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_redis.Object);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FR-B03 — both triggers evict the hot index and only the hot index.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnSessionEndSweep_LeavesTheDurableCopyIntact()
    {
        await SeedDurableCopyAsync(TenantA);
        SetupHotIndexHolding("chunk-0", "chunk-1");

        var job = CreateJob();
        _signal.SignalSessionEnded(TenantA, SessionId);

        await job.DrainPendingSignalsAsync(CancellationToken.None);

        // Positive control FIRST: without it, a sweep that did nothing at all would pass the durable
        // assertion trivially and this file would be asserting an absence of work.
        _deletedFromHotIndex.Should().ContainSingle(
            "the on-session-end trigger must actually evict the hot index — otherwise the durable " +
            "assertion below is vacuous");
        _deletedFromHotIndex[0].Should().BeEquivalentTo(new[] { "chunk-0", "chunk-1" });

        await AssertDurableCopySurvivedAsync(TenantA);
    }

    [Fact]
    public async Task ScheduledOrphanSweep_LeavesTheDurableCopyIntact()
    {
        await SeedDurableCopyAsync(TenantA);
        SetupIndexEnumerationReturning((TenantA, SessionId));
        SetupHotIndexHolding("chunk-0", "chunk-1");

        // The session's Redis key has expired on its 24h sliding TTL — i.e. the exact lifecycle mismatch
        // Track B exists to fix. The sweep sees an orphan and evicts.
        _redis.Setup(d => d.KeyExistsAsync(
                It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var job = CreateJob();

        await job.RunScheduledScanAsync(CancellationToken.None);

        _deletedFromHotIndex.Should().ContainSingle(
            "the scheduled sweep must actually evict the orphaned session's hot-index chunks");

        await AssertDurableCopySurvivedAsync(TenantA);
    }

    /// <summary>
    /// The strongest statement this suite can make about the sweep: run it for EVERY tenant that has
    /// durable content and none of it moves. A destructive edit scoped to "the session being cleaned"
    /// would be caught by the tests above; one scoped to "the container" would only be caught here.
    /// </summary>
    [Fact]
    public async Task SweepingOneTenantsSession_TouchesNoTenantsDurableBytes()
    {
        await SeedDurableCopyAsync(TenantA);
        await SeedDurableCopyAsync(TenantB);
        SetupIndexEnumerationReturning((TenantA, SessionId));
        SetupHotIndexHolding("chunk-0");
        _redis.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var before = _durableBlobs.BlobNames.ToList();
        before.Should().HaveCount(2, "positive control: both tenants' bytes are present before the sweep");

        var job = CreateJob();
        await job.RunScheduledScanAsync(CancellationToken.None);

        _deletedFromHotIndex.Should().ContainSingle("positive control: the sweep did run");
        _durableBlobs.BlobNames.Should().BeEquivalentTo(before,
            "a session-scoped hot-index eviction must not remove ANY durable blob, its own tenant's least " +
            "of all (FR-B03)");

        await AssertDurableCopySurvivedAsync(TenantA);
        await AssertDurableCopySurvivedAsync(TenantB);
    }

    /// <summary>
    /// Idempotency, preserved: a second pass finds nothing in the index and still must not reach for the
    /// durable copy as a "well, delete something" fallback.
    /// </summary>
    [Fact]
    public async Task RepeatedEviction_IsANoOpAndStillLeavesTheDurableCopyIntact()
    {
        await SeedDurableCopyAsync(TenantA);
        SetupHotIndexHolding("chunk-0");

        var job = CreateJob();
        (await job.EvictSessionAsync(TenantA, SessionId, "test", CancellationToken.None))
            .Should().Be(1, "positive control: the first pass evicts");

        SetupHotIndexHolding();  // index now empty

        (await job.EvictSessionAsync(TenantA, SessionId, "test", CancellationToken.None))
            .Should().Be(0, "the second pass is an idempotent no-op");

        await AssertDurableCopySurvivedAsync(TenantA);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private SessionFilesCleanupJob CreateJob()
    {
        var searchIndexClient = new Mock<SearchIndexClient>();
        searchIndexClient.Setup(c => c.GetSearchClient(IndexName)).Returns(_hotIndex.Object);

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
            .AddSingleton(services, searchIndexClient.Object);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
            .AddSingleton(services, Options.Create(new AiSearchOptions { SessionFilesIndexName = IndexName }));
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
            .AddSingleton(services, _multiplexer.Object);

        // NOTE: the durable store is deliberately NOT registered here. It could be — nothing stops a
        // production container from holding it, and it DOES hold it — but the job could not consume it
        // either way. Leaving it out would weaken the test into "we didn't offer it", so the sibling
        // registration below offers it and the sweep still cannot use it.
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
            .AddSingleton(services, _durableStore);

        var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
            .BuildServiceProvider(services);

        return new SessionFilesCleanupJob(
            provider,
            Options.Create(new SessionFilesCleanupOptions
            {
                IntervalHours = 6,
                DeleteBatchSize = 1000,
                MaxKeysPerScan = 10_000,
            }),
            Options.Create(new RedisOptions { InstanceName = RedisInstancePrefix }),
            _signal,
            NullLogger<SessionFilesCleanupJob>.Instance);
    }

    private async Task SeedDurableCopyAsync(string tenantId)
    {
        var outcome = await _durableStore.WriteAsync(
            tenantId, SessionId, FileId, BinaryData.FromBytes(DurableContent), "application/pdf");

        outcome.Should().Be(SessionFileStoreOutcome.Written,
            "the durable copy must actually exist before a test can claim the sweep spared it");
    }

    private async Task AssertDurableCopySurvivedAsync(string tenantId)
    {
        var readBack = await _durableStore.ReadAsync(tenantId, SessionId, FileId);

        readBack.Should().NotBeNull(
            "the 24h cleanup sweep must evict the HOT INDEX ONLY. The durable copy follows the session's " +
            "own 90-day retention (task 062), and a sweep keyed off a 24h Redis TTL has no business " +
            "deleting it — that is FR-B03, and it is the difference between a file the user can still " +
            "open on day 60 and the R7 UAT defect this track exists to close");
        readBack!.Content.ToArray().Should().BeEquivalentTo(DurableContent,
            "surviving means byte-identical, not merely present");
    }

    /// <summary>Makes the session-files index report exactly these chunk ids for the delete query.</summary>
    private void SetupHotIndexHolding(params string[] chunkIds)
    {
        var results = SearchModelFactory.SearchResults(
            values: chunkIds
                .Select(id => SearchModelFactory.SearchResult(
                    new SessionFilesCleanupKey { Id = id }, score: 1.0, highlights: null))
                .ToList(),
            totalCount: chunkIds.Length,
            facets: null,
            coverage: null,
            rawResponse: null!);

        _hotIndex
            .Setup(c => c.SearchAsync<SessionFilesCleanupKey>(
                It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(results, null!));

        _hotIndex
            .Setup(c => c.DeleteDocumentsAsync(
                "id", It.IsAny<IEnumerable<string>>(), It.IsAny<IndexDocumentsOptions?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, IEnumerable<string> keys, IndexDocumentsOptions? _, CancellationToken _)
                => _deletedFromHotIndex.Add(keys.ToList()))
            .ReturnsAsync(() => Response.FromValue(
                SearchModelFactory.IndexDocumentsResult(
                    chunkIds.Select(id => SearchModelFactory.IndexingResult(id, null, true, 200)).ToList()),
                null!));
    }

    /// <summary>Makes the scheduled scan's index enumeration report these (tenant, session) pairs.</summary>
    private void SetupIndexEnumerationReturning(params (string TenantId, string SessionId)[] pairs)
    {
        var refs = SearchModelFactory.SearchResults(
            values: pairs
                .Select(p => SearchModelFactory.SearchResult(
                    new SessionFilesCleanupRef { Id = $"{p.SessionId}_s_0", TenantId = p.TenantId, SessionId = p.SessionId },
                    score: 1.0, highlights: null))
                .ToList(),
            totalCount: pairs.Length,
            facets: null,
            coverage: null,
            rawResponse: null!);

        _hotIndex
            .Setup(c => c.SearchAsync<SessionFilesCleanupRef>(
                It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(refs, null!));
    }
}
