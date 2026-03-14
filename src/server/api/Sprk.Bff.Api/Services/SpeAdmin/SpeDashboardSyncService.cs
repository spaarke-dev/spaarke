using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Spaarke.Dataverse;

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
    }

    // -------------------------------------------------------------------------
    // Internal Dataverse query model for sprk_specontainertypeconfigs
    // -------------------------------------------------------------------------

    private sealed class ContainerTypeConfigRecord
    {
        [JsonPropertyName("sprk_specontainertypeconfigid")]
        public string? Id { get; set; }

        [JsonPropertyName("sprk_containertypeid")]
        public string? ContainerTypeId { get; set; }

        [JsonPropertyName("sprk_clientid")]
        public string? ClientId { get; set; }

        [JsonPropertyName("sprk_tenantid")]
        public string? TenantId { get; set; }

        [JsonPropertyName("sprk_secretkeyvaultname")]
        public string? SecretKeyVaultName { get; set; }
    }

    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    /// <summary>Cache key where DashboardMetrics JSON is stored in IDistributedCache.</summary>
    public const string CacheKey = "sdap:spe:dashboard:metrics";

    private const string ContainerTypeConfigEntitySet = "sprk_specontainertypeconfigs";

    private const string ContainerTypeConfigSelect =
        "sprk_specontainertypeconfigid,sprk_containertypeid,sprk_clientid,sprk_tenantid,sprk_secretkeyvaultname";

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
        // 1. Load container type configs from Dataverse
        var configs = await LoadContainerTypeConfigsAsync(ct);

        if (configs.Count == 0)
        {
            _logger.LogWarning(
                "No container type configs found in Dataverse. Dashboard metrics will show zeros.");

            return new DashboardMetrics
            {
                TotalContainerCount = 0,
                TotalStorageUsedInBytes = 0,
                ContainerCountByConfig = new Dictionary<string, int>(),
                LastSyncedAt = DateTimeOffset.UtcNow,
                SyncSucceeded = true,
                SyncStatus = "No container type configs registered."
            };
        }

        // 2. Query Graph for containers per config
        var containerCountByConfig = new Dictionary<string, int>();
        long totalStorageBytes = 0;
        int totalContainerCount = 0;
        int failedConfigs = 0;

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

                _logger.LogDebug(
                    "Config {ConfigId}: {Count} containers, {StorageBytes} bytes reported",
                    config.ConfigId, containers.Count, containers.Sum(c => c.StorageUsedInBytes ?? 0));
            }
            catch (Exception ex)
            {
                failedConfigs++;
                _logger.LogError(ex,
                    "Failed to fetch containers for configId {ConfigId} (containerTypeId={ContainerTypeId}). Skipping.",
                    config.ConfigId, config.ContainerTypeId);
                containerCountByConfig[config.ConfigId.ToString()] = -1; // Signal error for this config
            }
        }

        // 3. Build summary
        var succeeded = failedConfigs == 0;
        var syncStatus = failedConfigs == 0
            ? $"Synced {configs.Count} config(s) successfully."
            : $"Synced {configs.Count - failedConfigs} of {configs.Count} config(s). {failedConfigs} failed.";

        return new DashboardMetrics
        {
            TotalContainerCount = totalContainerCount,
            TotalStorageUsedInBytes = totalStorageBytes,
            ContainerCountByConfig = containerCountByConfig,
            LastSyncedAt = DateTimeOffset.UtcNow,
            SyncSucceeded = succeeded,
            SyncStatus = syncStatus
        };
    }

    /// <summary>
    /// Reads all active container type configs from the sprk_specontainertypeconfigs Dataverse entity.
    /// Returns resolved <see cref="SpeAdminGraphService.ContainerTypeConfig"/> records.
    /// </summary>
    private async Task<IReadOnlyList<SpeAdminGraphService.ContainerTypeConfig>> LoadContainerTypeConfigsAsync(
        CancellationToken ct)
    {
        try
        {
            var records = await _dataverseClient.QueryAsync<ContainerTypeConfigRecord>(
                ContainerTypeConfigEntitySet,
                filter: "statecode eq 0", // Active records only
                select: ContainerTypeConfigSelect,
                cancellationToken: ct);

            var configs = new List<SpeAdminGraphService.ContainerTypeConfig>(records.Count);

            foreach (var record in records)
            {
                if (!Guid.TryParse(record.Id, out var configId)
                    || string.IsNullOrWhiteSpace(record.ContainerTypeId)
                    || string.IsNullOrWhiteSpace(record.ClientId)
                    || string.IsNullOrWhiteSpace(record.TenantId)
                    || string.IsNullOrWhiteSpace(record.SecretKeyVaultName))
                {
                    _logger.LogWarning(
                        "Skipping incomplete container type config record: id={Id}", record.Id);
                    continue;
                }

                configs.Add(new SpeAdminGraphService.ContainerTypeConfig(
                    ConfigId: configId,
                    ContainerTypeId: record.ContainerTypeId,
                    ClientId: record.ClientId,
                    TenantId: record.TenantId,
                    SecretKeyVaultName: record.SecretKeyVaultName));
            }

            _logger.LogDebug(
                "Loaded {Count} container type configs from Dataverse ({Total} records total)",
                configs.Count, records.Count);

            return configs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load container type configs from Dataverse.");
            return Array.Empty<SpeAdminGraphService.ContainerTypeConfig>();
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

        await _cache.SetStringAsync(CacheKey, json,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
            ct);

        _logger.LogDebug(
            "Dashboard metrics cached at key '{CacheKey}' with TTL {TtlMinutes} minutes.",
            CacheKey, ttl.TotalMinutes);
    }
}
