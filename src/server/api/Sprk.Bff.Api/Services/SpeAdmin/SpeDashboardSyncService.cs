using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.SpeAdmin;

/// <summary>
/// Background service that periodically syncs SPE container metrics (counts, storage usage,
/// container count by status) from the Graph API and caches them for the admin dashboard.
///
/// Implements ADR-001 BackgroundService pattern — no Azure Functions.
///
/// Sync flow:
///   1. Query sprk_specontainertypeconfigs from Dataverse (all active configs).
///   2. For each config, call SpeAdminGraphService.ListContainersAsync() via the appropriate
///      Graph client (resolved by SpeAdminGraphService.GetClientForConfigAsync).
///   3. Aggregate: total container count, total storage used, counts by status, per-config breakdown.
///   4. Store aggregated DashboardMetrics as JSON in IDistributedCache (key: sdap:spe:dashboard:metrics).
///   5. Wait for next interval (configurable, default 15 min) OR immediate signal via Channel.
///
/// On-demand refresh: POST /api/spe/dashboard/refresh writes to the refresh channel; the service
/// reads from it and executes an immediate sync without waiting for the periodic timer.
///
/// Error handling: Graph API errors are caught per-config and logged; the loop continues so a
/// single config failure never crashes the background service or stops other configs from syncing.
/// </summary>
public sealed class SpeDashboardSyncService : BackgroundService
{
    // -------------------------------------------------------------------------
    // Domain model — persisted to IDistributedCache
    // -------------------------------------------------------------------------

    /// <summary>
    /// Aggregated SPE dashboard metrics returned by GET /api/spe/dashboard/metrics.
    /// Cached at key <see cref="CacheKey"/> with TTL matching the sync interval.
    /// </summary>
    public sealed record DashboardMetrics
    {
        /// <summary>Total number of containers across all registered container types.</summary>
        [JsonPropertyName("totalContainerCount")]
        public int TotalContainerCount { get; init; }

        /// <summary>Total storage used in bytes across all containers that reported storage usage.</summary>
        [JsonPropertyName("totalStorageUsedInBytes")]
        public long TotalStorageUsedInBytes { get; init; }

        /// <summary>Number of containers per container type config ID (Guid.ToString()).</summary>
        [JsonPropertyName("containerCountByConfig")]
        public IReadOnlyDictionary<string, int> ContainerCountByConfig { get; init; }
            = new Dictionary<string, int>();

        /// <summary>UTC timestamp when these metrics were last successfully synced from Graph.</summary>
        [JsonPropertyName("lastSyncedAt")]
        public DateTimeOffset LastSyncedAt { get; init; }

        /// <summary>True if the most recent sync completed without errors; false if any config failed.</summary>
        [JsonPropertyName("syncSucceeded")]
        public bool SyncSucceeded { get; init; }

        /// <summary>
        /// Optional human-readable sync status message (e.g. "Synced 3 configs, 1 failed").
        /// </summary>
        [JsonPropertyName("syncStatus")]
        public string SyncStatus { get; init; } = string.Empty;

        /// <summary>
        /// Overall sync health, derived from <see cref="Concerns"/>. Never optimistic.
        /// </summary>
        [JsonPropertyName("syncHealth")]
        public SyncHealth SyncHealth { get; init; } = SyncHealth.Healthy;

        /// <summary>
        /// Per-concern outcome for every concern this sync pass attempted, in the order attempted.
        /// A concern that was attempted and failed appears here with its reason — this is what lets the
        /// dashboard NAME the failing concern instead of showing an opaque "Partial".
        /// </summary>
        [JsonPropertyName("concerns")]
        public IReadOnlyList<ConcernOutcome> Concerns { get; init; } = Array.Empty<ConcernOutcome>();
    }

    /// <summary>Overall sync health. Ordered least-to-most severe.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SyncHealth
    {
        /// <summary>Every attempted concern succeeded.</summary>
        Healthy,

        /// <summary>At least one concern succeeded and at least one failed.</summary>
        Degraded,

        /// <summary>Every attempted concern failed — the dashboard is showing nothing trustworthy.</summary>
        Failed
    }

    /// <summary>
    /// The outcome of one concern in a sync pass.
    /// </summary>
    /// <remarks>
    /// Added 2026-08-21 by <c>sdap-SPE-admin-app-r2</c> task 003 (spec FR-A03). Before this, a failed
    /// concern's reason existed only in the server log: the payload carried a bare count ("1 failed"), so
    /// an operator could see that something broke but not what or why.
    /// </remarks>
    public sealed record ConcernOutcome
    {
        /// <summary>What was attempted — e.g. "Dataverse container-type configs" or "Graph containers (config …)".</summary>
        [JsonPropertyName("concern")]
        public required string Concern { get; init; }

        [JsonPropertyName("succeeded")]
        public required bool Succeeded { get; init; }

        /// <summary>Redacted failure reason. Null when <see cref="Succeeded"/> is true.</summary>
        [JsonPropertyName("reason")]
        public string? Reason { get; init; }
    }

    /// <summary>Result of loading container-type configs — distinguishes "none registered" from "load failed".</summary>
    private sealed record ConfigLoadResult(
        IReadOnlyList<SpeAdminGraphService.ContainerTypeConfig> Configs,
        bool Succeeded,
        string? FailureReason,
        int SkippedIncompleteCount);

    // -------------------------------------------------------------------------
    // Internal Dataverse query model for sprk_specontainertypeconfigs
    // -------------------------------------------------------------------------

    private sealed class ContainerTypeConfigRecord
    {
        [JsonPropertyName("sprk_specontainertypeconfigid")]
        public string? Id { get; set; }

        [JsonPropertyName("sprk_containertypeid")]
        public string? ContainerTypeId { get; set; }

        [JsonPropertyName("sprk_owningappid")]
        public string? OwningAppId { get; set; }

        [JsonPropertyName("sprk_keyvaultsecretname")]
        public string? SecretKeyVaultName { get; set; }

        [JsonPropertyName("_sprk_environment_value")]
        public Guid? EnvironmentId { get; set; }
    }

    private sealed class EnvironmentRecord
    {
        [JsonPropertyName("sprk_tenantid")]
        public string? TenantId { get; set; }
    }

    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    /// <summary>Cache key where DashboardMetrics JSON is stored in IDistributedCache.</summary>
    public const string CacheKey = "sdap:spe:dashboard:metrics";

    private const string ContainerTypeConfigEntitySet = "sprk_specontainertypeconfigs";

    private const string ContainerTypeConfigSelect =
        "sprk_specontainertypeconfigid,sprk_containertypeid,sprk_owningappid,sprk_keyvaultsecretname,_sprk_environment_value";

    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly IDistributedCache _cache;
    private readonly SpeAdminGraphService _graphService;
    private readonly DataverseWebApiClient _dataverseClient;
    private readonly IOptions<SpeAdminOptions> _options;
    private readonly ILogger<SpeDashboardSyncService> _logger;

    /// <summary>
    /// Bounded channel used by POST /api/spe/dashboard/refresh to trigger an immediate sync.
    /// Capacity of 1 — multiple concurrent refresh requests coalesce into a single sync run.
    /// </summary>
    private readonly Channel<bool> _refreshChannel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public SpeDashboardSyncService(
        IDistributedCache cache,
        SpeAdminGraphService graphService,
        DataverseWebApiClient dataverseClient,
        IOptions<SpeAdminOptions> options,
        ILogger<SpeDashboardSyncService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _graphService = graphService ?? throw new ArgumentNullException(nameof(graphService));
        _dataverseClient = dataverseClient ?? throw new ArgumentNullException(nameof(dataverseClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =========================================================================
    // Public API — called by POST /api/spe/dashboard/refresh endpoint
    // =========================================================================

    /// <summary>
    /// Signals an immediate on-demand sync. Called by the POST /api/spe/dashboard/refresh endpoint.
    ///
    /// If a sync is already queued (channel is full), the request is dropped silently —
    /// the pending sync will serve the same purpose. Returns the updated metrics after the sync.
    /// </summary>
    public async Task<DashboardMetrics?> TriggerRefreshAsync(CancellationToken ct = default)
    {
        // Signal the background loop to run an immediate sync.
        // Channel capacity is 1 — DropWrite mode means duplicate requests coalesce.
        await _refreshChannel.Writer.WriteAsync(true, ct);

        _logger.LogInformation("Dashboard refresh triggered via on-demand request");

        // Wait briefly for the sync to complete (up to 30 seconds), then read from cache.
        // The background service processes the channel signal and updates the cache.
        // We poll the cache rather than using TaskCompletionSource to keep complexity low.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var previousMetrics = await ReadCachedMetricsAsync(ct);

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(500, ct);

            var metrics = await ReadCachedMetricsAsync(ct);

            // If we got a newer sync result, return it
            if (metrics != null &&
                (previousMetrics == null || metrics.LastSyncedAt > previousMetrics.LastSyncedAt))
            {
                return metrics;
            }
        }

        // Return whatever is cached (may be pre-existing data if sync is slow)
        return await ReadCachedMetricsAsync(ct);
    }

    // =========================================================================
    // BackgroundService — periodic sync loop
    // =========================================================================

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = _options.Value.DashboardSyncIntervalMinutes;
        var syncInterval = TimeSpan.FromMinutes(intervalMinutes);

        _logger.LogInformation(
            "SpeDashboardSyncService started. Sync interval: {IntervalMinutes} minutes.", intervalMinutes);

        // Run an initial sync on startup so the cache is populated before first request
        await RunSyncSafeAsync(stoppingToken);

        using var periodicTimer = new PeriodicTimer(syncInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for either the periodic timer tick OR an on-demand refresh signal
                var timerTask = periodicTimer.WaitForNextTickAsync(stoppingToken).AsTask();
                var refreshTask = _refreshChannel.Reader.WaitToReadAsync(stoppingToken).AsTask();

                var completed = await Task.WhenAny(timerTask, refreshTask);

                if (stoppingToken.IsCancellationRequested)
                    break;

                // Drain the refresh channel so a queued signal is consumed
                if (completed == refreshTask && _refreshChannel.Reader.TryRead(out _))
                {
                    _logger.LogInformation("SpeDashboardSyncService: on-demand refresh triggered");
                }

                await RunSyncSafeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error in SpeDashboardSyncService loop. Waiting 1 minute before retry.");

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("SpeDashboardSyncService stopped.");
    }

    // =========================================================================
    // Sync implementation
    // =========================================================================

    /// <summary>
    /// Runs a full sync cycle, catching all exceptions to prevent loop crashes.
    /// </summary>
    private async Task RunSyncSafeAsync(CancellationToken ct)
    {
        try
        {
            var metrics = await FetchAndAggregateDashboardMetricsAsync(ct);
            await WriteCachedMetricsAsync(metrics, ct);

            _logger.LogInformation(
                "Dashboard sync complete. Containers: {Total}, Storage: {StorageBytes} bytes. Status: {Status}",
                metrics.TotalContainerCount,
                metrics.TotalStorageUsedInBytes,
                metrics.SyncStatus);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Let the caller handle cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard sync failed. Metrics cache retains previous values.");
        }
    }

    /// <summary>
    /// Fetches all registered container type configs from Dataverse, then queries Graph for each.
    /// Aggregates results into a single <see cref="DashboardMetrics"/> snapshot.
    /// </summary>
    private async Task<DashboardMetrics> FetchAndAggregateDashboardMetricsAsync(CancellationToken ct)
    {
        var concerns = new List<ConcernOutcome>();

        // 1. Load container type configs from Dataverse.
        var load = await LoadContainerTypeConfigsAsync(ct);
        var configs = load.Configs;

        concerns.Add(new ConcernOutcome
        {
            Concern = "Dataverse container-type configs",
            Succeeded = load.Succeeded,
            Reason = load.FailureReason
        });

        if (load.SkippedIncompleteCount > 0)
        {
            // Skipped records used to be a LogWarning only — invisible to the operator looking at a
            // dashboard that silently covered fewer configs than they registered.
            concerns.Add(new ConcernOutcome
            {
                Concern = "Dataverse config completeness",
                Succeeded = false,
                Reason = $"{load.SkippedIncompleteCount} config record(s) skipped as incomplete "
                         + "(missing container type, owning app, secret name, or environment tenant)."
            });
        }

        // A Dataverse failure MUST NOT look like "nothing is registered". Before task 003 both paths
        // produced SyncSucceeded = true — a green dashboard over a broken app (spec §2.4).
        if (!load.Succeeded)
        {
            return Summarize(0, 0, new Dictionary<string, int>(), concerns,
                "Could not load container-type configs from Dataverse — container metrics are unavailable.");
        }

        if (configs.Count == 0)
        {
            _logger.LogWarning(
                "No container type configs found in Dataverse. Dashboard metrics will show zeros.");

            return Summarize(0, 0, new Dictionary<string, int>(), concerns,
                "No container type configs registered.");
        }

        // 2. Query Graph for containers per config
        var containerCountByConfig = new Dictionary<string, int>();
        long totalStorageBytes = 0;
        int totalContainerCount = 0;

        foreach (var config in configs)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var graphClient = await _graphService.GetClientForConfigAsync(config, ct);
                var containers = await _graphService.ListContainersAsync(
                    graphClient, config.ContainerTypeId, ct);

                containerCountByConfig[config.ConfigId.ToString()] = containers.Count;
                totalContainerCount += containers.Count;

                foreach (var container in containers)
                {
                    if (container.StorageUsedInBytes.HasValue)
                        totalStorageBytes += container.StorageUsedInBytes.Value;
                }

                // Evict expired Graph clients as a housekeeping step
                _graphService.EvictExpiredClients();

                concerns.Add(new ConcernOutcome
                {
                    Concern = $"Graph containers (config {config.ConfigId})",
                    Succeeded = true
                });

                _logger.LogDebug(
                    "Config {ConfigId}: {Count} containers, {StorageBytes} bytes reported",
                    config.ConfigId, containers.Count, containers.Sum(c => c.StorageUsedInBytes ?? 0));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Failed to fetch containers for configId {ConfigId} (containerTypeId={ContainerTypeId}). Skipping.",
                    config.ConfigId, config.ContainerTypeId);

                containerCountByConfig[config.ConfigId.ToString()] = -1; // Signal error for this config

                concerns.Add(new ConcernOutcome
                {
                    Concern = $"Graph containers (config {config.ConfigId})",
                    Succeeded = false,
                    Reason = ProblemDetailsHelper.Explain("Container list failed.", ex)
                });
            }
        }

        return Summarize(totalContainerCount, totalStorageBytes, containerCountByConfig, concerns, null);
    }

    /// <summary>
    /// The domain rule for dashboard sync health: a concern that failed can never report Healthy.
    /// </summary>
    /// <remarks>
    /// Public and pure because it IS the contract the Sync Status tile renders, and because it is the exact
    /// rule spec §2.4 exists to protect — the app reporting success while a concern is failing. Kept free of
    /// I/O so it can be tested directly (ADR-038 <c>tests/unit/domain/**</c>) rather than through a mocked
    /// Graph/Dataverse pair.
    /// <para>
    /// An empty concern list is <see cref="SyncHealth.Healthy"/>: no concern was attempted, so nothing
    /// failed. Callers that attempt work always record at least one concern.
    /// </para>
    /// </remarks>
    public static SyncHealth DeriveHealth(IReadOnlyList<ConcernOutcome> concerns)
    {
        ArgumentNullException.ThrowIfNull(concerns);

        var failedCount = concerns.Count(c => !c.Succeeded);

        if (failedCount == 0) return SyncHealth.Healthy;
        return failedCount == concerns.Count ? SyncHealth.Failed : SyncHealth.Degraded;
    }

    /// <summary>
    /// Derives overall health and the status line from the per-concern outcomes.
    /// </summary>
    /// <remarks>
    /// The single place a <see cref="DashboardMetrics"/> is constructed after a sync attempt, so health can
    /// never drift from the concerns that produced it. <c>SyncSucceeded</c> is kept as a derived mirror of
    /// <c>SyncHealth == Healthy</c> for existing clients.
    /// </remarks>
    private static DashboardMetrics Summarize(
        int totalContainerCount,
        long totalStorageBytes,
        IReadOnlyDictionary<string, int> containerCountByConfig,
        IReadOnlyList<ConcernOutcome> concerns,
        string? statusOverride)
    {
        var failed = concerns.Where(c => !c.Succeeded).ToList();
        var health = DeriveHealth(concerns);

        var status = statusOverride ?? (health switch
        {
            SyncHealth.Healthy => $"All {concerns.Count} concern(s) synced successfully.",
            // Name the failures — a bare count is what made the old status unactionable.
            _ => $"{failed.Count} of {concerns.Count} concern(s) failed: "
                 + string.Join("; ", failed.Select(f => f.Concern))
        });

        return new DashboardMetrics
        {
            TotalContainerCount = totalContainerCount,
            TotalStorageUsedInBytes = totalStorageBytes,
            ContainerCountByConfig = containerCountByConfig,
            LastSyncedAt = DateTimeOffset.UtcNow,
            SyncSucceeded = health == SyncHealth.Healthy,
            SyncHealth = health,
            SyncStatus = status,
            Concerns = concerns
        };
    }

    /// <summary>
    /// Reads all active container type configs from the sprk_specontainertypeconfigs Dataverse entity.
    /// Returns resolved <see cref="SpeAdminGraphService.ContainerTypeConfig"/> records.
    /// </summary>
    private async Task<ConfigLoadResult> LoadContainerTypeConfigsAsync(
        CancellationToken ct)
    {
        var skippedIncomplete = 0;

        try
        {
            var records = await _dataverseClient.QueryAsync<ContainerTypeConfigRecord>(
                ContainerTypeConfigEntitySet,
                filter: "statecode eq 0", // Active records only
                select: ContainerTypeConfigSelect,
                cancellationToken: ct);

            // Resolve unique environment IDs → tenant IDs in batch (one query per unique env).
            var envIds = records
                .Where(r => r.EnvironmentId.HasValue)
                .Select(r => r.EnvironmentId!.Value)
                .Distinct()
                .ToList();

            var tenantById = new Dictionary<Guid, string>();
            foreach (var envId in envIds)
            {
                try
                {
                    var env = await _dataverseClient.RetrieveAsync<EnvironmentRecord>(
                        "sprk_speenvironments", envId, "sprk_tenantid", ct);
                    if (env?.TenantId is not null)
                        tenantById[envId] = env.TenantId;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not resolve tenant ID for environment {EnvId}", envId);
                }
            }

            var configs = new List<SpeAdminGraphService.ContainerTypeConfig>(records.Count);

            foreach (var record in records)
            {
                if (!Guid.TryParse(record.Id, out var configId)
                    || string.IsNullOrWhiteSpace(record.ContainerTypeId)
                    || string.IsNullOrWhiteSpace(record.OwningAppId)
                    || string.IsNullOrWhiteSpace(record.SecretKeyVaultName)
                    || !record.EnvironmentId.HasValue
                    || !tenantById.TryGetValue(record.EnvironmentId.Value, out var tenantId))
                {
                    _logger.LogWarning(
                        "Skipping incomplete container type config record: id={Id}", record.Id);
                    skippedIncomplete++;
                    continue;
                }

                configs.Add(new SpeAdminGraphService.ContainerTypeConfig(
                    ConfigId: configId,
                    ContainerTypeId: record.ContainerTypeId,
                    ClientId: record.OwningAppId,
                    TenantId: tenantId,
                    SecretKeyVaultName: record.SecretKeyVaultName));
            }

            _logger.LogDebug(
                "Loaded {Count} container type configs from Dataverse ({Total} records total)",
                configs.Count, records.Count);

            return new ConfigLoadResult(configs, Succeeded: true, FailureReason: null, skippedIncomplete);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load container type configs from Dataverse.");

            // Returning an empty list with Succeeded: false is the point. Previously this returned a bare
            // empty array, which the caller could not distinguish from "no configs registered" — so a
            // Dataverse outage rendered as Sync Status "OK". That is spec §2.4's systemic defect exactly.
            return new ConfigLoadResult(
                Array.Empty<SpeAdminGraphService.ContainerTypeConfig>(),
                Succeeded: false,
                FailureReason: ProblemDetailsHelper.Explain("Dataverse query failed.", ex),
                skippedIncomplete);
        }
    }

    // =========================================================================
    // Cache helpers
    // =========================================================================

    /// <summary>
    /// Reads the cached dashboard metrics, returning null if no metrics are cached yet.
    /// </summary>
    public async Task<DashboardMetrics?> ReadCachedMetricsAsync(CancellationToken ct = default)
    {
        try
        {
            // SYSTEM-LEVEL EXCEPTION (NFR-08): SPE-dashboard metrics aggregate across all tenants/containers in the BFF org; cross-tenant aggregation is the intentional shape of the metric.
            var json = await _cache.GetStringAsync(CacheKey, ct);
            if (json == null) return null;

            return JsonSerializer.Deserialize<DashboardMetrics>(json, CacheJsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read dashboard metrics from cache.");
            return null;
        }
    }

    /// <summary>
    /// Writes dashboard metrics to the distributed cache with a TTL matching the sync interval.
    /// TTL is set to 2x the sync interval to ensure metrics survive a skipped cycle.
    /// </summary>
    private async Task WriteCachedMetricsAsync(DashboardMetrics metrics, CancellationToken ct)
    {
        var intervalMinutes = _options.Value.DashboardSyncIntervalMinutes;
        var ttl = TimeSpan.FromMinutes(intervalMinutes * 2);

        var json = JsonSerializer.Serialize(metrics, CacheJsonOptions);

        // SYSTEM-LEVEL EXCEPTION (NFR-08): SPE-dashboard metrics aggregate across all tenants/containers in the BFF org; cross-tenant aggregation is the intentional shape of the metric.
        await _cache.SetStringAsync(CacheKey, json,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
            ct);

        _logger.LogDebug(
            "Dashboard metrics cached at key '{CacheKey}' with TTL {TtlMinutes} minutes.",
            CacheKey, ttl.TotalMinutes);
    }
}
