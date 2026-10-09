using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.SpeAdmin;

/// <summary>
/// Scheduled job (<see cref="IScheduledJob"/>, ADR-036) that syncs SPE container metrics (counts, storage usage) from
/// the Graph API and caches them for the admin dashboard, attributing every container to the config of the business
/// unit that owns it.
///
/// Where it runs is ADR-052: in the BFF, under <c>ScheduledJobHost</c>'s distributed lease — one run per schedule
/// across instances. Migrated from a hand-rolled <c>PeriodicTimer</c> <c>BackgroundService</c> by
/// unified-access-control-r2 task 165, which changed its behaviour (owner round 25 item 5: per-config storage), and
/// ADR-052 §1 migrates a timer service when it is next touched.
///
/// Sync flow:
///   1. Query sprk_specontainertypeconfigs from Dataverse (all active configs, with their business units) and the
///      business-unit hierarchy.
///   2. For each config, list its container type's containers with the config's type-wide Graph client, keep only this
///      stamp's (SpeAdminGraphService.FilterOwnedAsync — task 227d: the listing holds every customer's containers), and
///      read each kept container's business-unit binding (<see cref="SpeContainerBusinessUnitStamp"/> — Graph returns it
///      on a single-container read only).
///   3. Attribute each container ONCE (<see cref="AttributeContainer"/>): to the config carrying its container type
///      whose business unit is the nearest ancestor-or-self of the container's stamped unit. One container type can
///      serve several customers (Model 1), so "the containers of this config's type" is NOT "this config's
///      containers".
///   4. Store per-config counts and storage, plus the containers bound to a unit of THIS environment under which no
///      config of their type sits as a separate AGGREGATE "unattributed" figure (no container id or name), shown only in
///      a platform operator's full view. UNBOUND (or malformed-stamp) containers are counted NOWHERE — owner round 41
///      item 2: under Model 1 the root admin of ANY environment is that environment's platform operator, so an
///      aggregate over the unbound containers of a shared type would count other customers' containers. Their alarm is
///      the backfill's -Verify (it lists every unbound container to the operator who runs it) and the pre-deploy /
///      onboarding gate (round 35 item 2). A container bound to a unit this environment does not know (another
///      environment's, in a shared Model 1 consuming tenant) is counted nowhere either.
///
/// On-demand refresh: <c>POST /api/spe/dashboard/refresh</c> triggers a run through <c>ScheduledJobHost.TriggerNowAsync</c>
/// and waits for the cache to advance (<see cref="WaitForMetricsNewerThanAsync"/>).
///
/// Error handling: Graph API errors are caught per config and recorded as a failed concern; a binding that cannot be
/// read excludes its container and records a failed per-config concern (fail closed: an unverified container is
/// counted for nobody).
/// </summary>
public sealed class SpeDashboardSyncService : IScheduledJob
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
        /// <summary>
        /// Total number of containers: every config's attributed containers plus the unattributed ones (never an unbound
        /// container — owner round 41 item 2).
        /// </summary>
        [JsonPropertyName("totalContainerCount")]
        public int TotalContainerCount { get; init; }

        /// <summary>Total storage used in bytes across all containers that reported storage usage.</summary>
        /// <remarks>
        /// Read together with <see cref="StorageReportingContainerCount"/>. Before 2026-08-24 every
        /// container reported null (the value was fetched from Graph and discarded), so this summed
        /// to <b>0</b> and the dashboard rendered a confident "0 B" — the purest instance of the
        /// systemic defect this project exists to remove. A partial sum presented as a total is the
        /// same defect in miniature, which is why the contributing count travels with it.
        /// </remarks>
        [JsonPropertyName("totalStorageUsedInBytes")]
        public long TotalStorageUsedInBytes { get; init; }

        /// <summary>
        /// How many containers actually reported a storage figure, out of
        /// <see cref="TotalContainerCount"/>.
        /// </summary>
        /// <remarks>
        /// Graph returns consumption only on the beta LIST surface (task 020), so coverage can be
        /// partial. When this is below the total, the sum is a floor rather than a total and the UI
        /// must say so instead of presenting it as complete.
        /// </remarks>
        [JsonPropertyName("storageReportingContainerCount")]
        public int StorageReportingContainerCount { get; init; }

        /// <summary>
        /// Number of containers ATTRIBUTED to each config (Guid.ToString()); -1 when the config's own container
        /// list failed.
        /// </summary>
        [JsonPropertyName("containerCountByConfig")]
        public IReadOnlyDictionary<string, int> ContainerCountByConfig { get; init; }
            = new Dictionary<string, int>();

        /// <summary>
        /// Storage used by each config's attributed containers (owner round 25 item 5) — what lets a partial
        /// (leaf-admin) view report its own configs' storage instead of nothing.
        /// </summary>
        [JsonPropertyName("storageUsedInBytesByConfig")]
        public IReadOnlyDictionary<string, long> StorageUsedInBytesByConfig { get; init; }
            = new Dictionary<string, long>();

        /// <summary>How many of each config's attributed containers reported a storage figure.</summary>
        [JsonPropertyName("storageReportingContainerCountByConfig")]
        public IReadOnlyDictionary<string, int> StorageReportingContainerCountByConfig { get; init; }
            = new Dictionary<string, int>();

        /// <summary>
        /// Containers bound to a unit of this environment under which no config of their type sits. Shown only in the
        /// platform operator's full view. Unbound containers are NOT counted here or anywhere (owner round 41 item 2).
        /// </summary>
        [JsonPropertyName("unattributedContainerCount")]
        public int UnattributedContainerCount { get; init; }

        /// <summary>Storage used by the unattributed containers.</summary>
        [JsonPropertyName("unattributedStorageUsedInBytes")]
        public long UnattributedStorageUsedInBytes { get; init; }

        /// <summary>How many unattributed containers reported a storage figure.</summary>
        [JsonPropertyName("unattributedStorageReportingContainerCount")]
        public int UnattributedStorageReportingContainerCount { get; init; }

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
        IReadOnlyList<LoadedConfig> Configs,
        bool Succeeded,
        string? FailureReason,
        int SkippedIncompleteCount);

    /// <summary>A complete config the sync can list containers for, with the business unit it belongs to.</summary>
    private sealed record LoadedConfig(SpeAdminGraphService.ContainerTypeConfig Config, Guid? BusinessUnitId);

    // -------------------------------------------------------------------------
    // Attribution (pure; unified-access-control-r2 task 165)
    // -------------------------------------------------------------------------

    /// <summary>A config as attribution sees it.</summary>
    internal sealed record AttributableConfig(Guid ConfigId, string ContainerTypeId, Guid? BusinessUnitId);

    /// <summary>Where a container's figures are counted.</summary>
    internal enum ContainerAttributionKind
    {
        /// <summary>Counted under <see cref="ContainerAttribution.ConfigId"/>.</summary>
        Config,

        /// <summary>
        /// Counted in the aggregate unattributed figure, shown only in a platform operator's full view: bound to a unit of
        /// this environment under which no config of the container's type sits.
        /// </summary>
        Unattributed,

        /// <summary>
        /// Counted nowhere: UNBOUND or malformed (owner round 41 item 2 — it may be another customer's), bound to a unit
        /// this environment does not know, or not judgeable.
        /// </summary>
        Excluded
    }

    /// <summary>The attribution of one container.</summary>
    internal readonly record struct ContainerAttribution(ContainerAttributionKind Kind, Guid? ConfigId)
    {
        public static ContainerAttribution To(Guid configId) => new(ContainerAttributionKind.Config, configId);
        public static ContainerAttribution Unattributed => new(ContainerAttributionKind.Unattributed, null);
        public static ContainerAttribution Excluded => new(ContainerAttributionKind.Excluded, null);
    }

    /// <summary>
    /// Attributes one container to the config that owns it (owner round 25 item 5, consistent with the per-container
    /// rule of round 20 item 2).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>Bound to a unit this environment knows: the config carrying the container's type whose business unit is
    ///   the NEAREST ancestor-or-self of that unit (ties: the lowest config id) — an admin who reaches that config
    ///   reaches the container. No such config: unattributed.</item>
    ///   <item>Bound to a unit the hierarchy does not contain: excluded — another environment's container in a shared
    ///   Model 1 consuming tenant, which no administrator here reaches. When the hierarchy could not be read
    ///   (<paramref name="hierarchy"/> null), only an exact unit match is attributed and the rest is excluded
    ///   (fail closed).</item>
    ///   <item>Unbound or malformed: EXCLUDED — counted in no view, the platform operator's aggregate included (owner
    ///   round 41 item 2: under Model 1 an unbound container of a shared type may be another customer's; its alarm is the
    ///   backfill's -Verify, which lists it).</item>
    /// </list>
    /// A config with no business unit (the compatibility rule: visible to every admin) never receives a bound
    /// container — that would show it to everyone.
    /// </remarks>
    internal static ContainerAttribution AttributeContainer(
        SpeContainerBinding binding,
        string containerTypeId,
        IReadOnlyList<AttributableConfig> configs,
        IReadOnlyDictionary<Guid, Guid?>? hierarchy)
    {
        if (binding.BusinessUnitId is not { } unit)
        {
            return ContainerAttribution.Excluded;
        }

        AttributableConfig? ConfigAt(Guid businessUnit) => configs
            .Where(c => c.BusinessUnitId == businessUnit && SpeAdminTenantScope.SameGuid(c.ContainerTypeId, containerTypeId))
            .OrderBy(c => c.ConfigId)
            .FirstOrDefault();

        if (hierarchy is null)
        {
            return ConfigAt(unit) is { } exact ? ContainerAttribution.To(exact.ConfigId) : ContainerAttribution.Excluded;
        }

        if (!hierarchy.ContainsKey(unit))
        {
            return ContainerAttribution.Excluded;
        }

        var visited = new HashSet<Guid>();
        Guid? current = unit;
        while (current is { } businessUnit && visited.Add(businessUnit))
        {
            if (ConfigAt(businessUnit) is { } owner)
            {
                return ContainerAttribution.To(owner.ConfigId);
            }

            current = hierarchy.TryGetValue(businessUnit, out var parent) ? parent : null;
        }

        return ContainerAttribution.Unattributed;
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

        [JsonPropertyName("sprk_owningappid")]
        public string? OwningAppId { get; set; }

        [JsonPropertyName("sprk_keyvaultsecretname")]
        public string? SecretKeyVaultName { get; set; }

        [JsonPropertyName("_sprk_environment_value")]
        public Guid? EnvironmentId { get; set; }

        [JsonPropertyName("_sprk_businessunit_value")]
        public Guid? BusinessUnitId { get; set; }
    }

    private sealed class EnvironmentRecord
    {
        [JsonPropertyName("sprk_tenantid")]
        public string? TenantId { get; set; }
    }

    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    /// <summary>The scheduled job id (ADR-036).</summary>
    public const string JobIdConstant = "spe-dashboard-sync";

    /// <summary>Cache key where DashboardMetrics JSON is stored in IDistributedCache.</summary>
    public const string CacheKey = "sdap:spe:dashboard:metrics";

    /// <summary>The concern recorded when the business-unit hierarchy cannot be read.</summary>
    internal const string HierarchyConcern = "Dataverse business units";

    /// <summary>The per-config concern prefix for container business-unit binding reads.</summary>
    internal const string BindingConcernPrefix = "Container business-unit bindings (config ";

    private const string ContainerTypeConfigEntitySet = "sprk_specontainertypeconfigs";

    private const string ContainerTypeConfigSelect =
        "sprk_specontainertypeconfigid,sprk_containertypeid,sprk_owningappid,sprk_keyvaultsecretname," +
        "_sprk_environment_value,_sprk_businessunit_value";

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
    // IScheduledJob (ADR-036)
    // =========================================================================

    /// <inheritdoc/>
    public string JobId => JobIdConstant;

    /// <inheritdoc/>
    public string DisplayName => "SPE Dashboard Sync";

    /// <inheritdoc/>
    public string Description =>
        "Lists every SPE container-type config's containers, reads each container's business-unit binding, and caches "
        + "per-config counts and storage for the SPE admin dashboard (unified-access-control-r2 task 165).";

    /// <summary>
    /// The cron schedule <see cref="SpeAdminOptions.DashboardSyncIntervalMinutes"/> compiles to: every N minutes below
    /// an hour (<c>*/N * * * *</c>), every N whole hours from an hour up (<c>0 */H * * *</c>), daily at 00:00 UTC from a
    /// day up. Compiled once at startup; a change needs a restart.
    /// </summary>
    public static string BuildCronSchedule(SpeAdminOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var minutes = options.DashboardSyncIntervalMinutes <= 0 ? 15 : options.DashboardSyncIntervalMinutes;

        if (minutes < 60)
        {
            return $"*/{minutes} * * * *";
        }

        var hours = minutes / 60;
        return hours >= 24 ? "0 0 * * *" : $"0 */{hours} * * *";
    }

    /// <inheritdoc/>
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        var metrics = await FetchAndAggregateDashboardMetricsAsync(cancellationToken).ConfigureAwait(false);
        await WriteCachedMetricsAsync(metrics, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Dashboard sync complete. Containers: {Total}, Storage: {StorageBytes} bytes. Status: {Status}. CorrelationId={CorrelationId}",
            metrics.TotalContainerCount, metrics.TotalStorageUsedInBytes, metrics.SyncStatus, context.CorrelationId);

        var failed = metrics.SyncHealth == SyncHealth.Failed;
        return new JobRunResult(
            Success: !failed,
            ErrorMessage: failed ? metrics.SyncStatus : null,
            ProcessedItems: metrics.TotalContainerCount,
            Duration: Stopwatch.GetElapsedTime(started));
    }

    // =========================================================================
    // Public API — the dashboard endpoints
    // =========================================================================

    /// <summary>
    /// Waits (polling the cache) until metrics newer than <paramref name="since"/> are cached, or the timeout passes,
    /// and returns whatever is cached then. Used by <c>POST /api/spe/dashboard/refresh</c> after it triggers a run.
    /// </summary>
    public async Task<DashboardMetrics?> WaitForMetricsNewerThanAsync(
        DateTimeOffset? since,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var metrics = await ReadCachedMetricsAsync(ct).ConfigureAwait(false);
            if (metrics != null && (since is null || metrics.LastSyncedAt > since.Value))
            {
                return metrics;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return await ReadCachedMetricsAsync(ct).ConfigureAwait(false);
    }

    // =========================================================================
    // Sync implementation
    // =========================================================================

    /// <summary>
    /// Fetches every registered config and the business-unit hierarchy from Dataverse, lists each config's containers
    /// and reads their bindings from Graph, and aggregates per-config figures.
    /// </summary>
    internal async Task<DashboardMetrics> FetchAndAggregateDashboardMetricsAsync(CancellationToken ct)
    {
        var concerns = new List<ConcernOutcome>();

        // 1. Load container type configs from Dataverse.
        var load = await LoadContainerTypeConfigsAsync(ct).ConfigureAwait(false);
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
            return Summarize(Aggregate.Empty, concerns,
                "Could not load container-type configs from Dataverse — container metrics are unavailable.");
        }

        if (configs.Count == 0)
        {
            _logger.LogWarning(
                "No container type configs found in Dataverse. Dashboard metrics will show zeros.");

            return Summarize(Aggregate.Empty, concerns, "No container type configs registered.");
        }

        // 2. The business-unit hierarchy — attribution walks it. Unreadable: attribute exact matches only.
        IReadOnlyDictionary<Guid, Guid?>? hierarchy = null;
        try
        {
            hierarchy = await SpeAdminTenantScope.LoadBusinessUnitHierarchyAsync(_dataverseClient, ct).ConfigureAwait(false);
            concerns.Add(new ConcernOutcome { Concern = HierarchyConcern, Succeeded = true });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Dashboard sync: the business-unit hierarchy could not be read; attributing exact matches only.");
            concerns.Add(new ConcernOutcome
            {
                Concern = HierarchyConcern,
                Succeeded = false,
                Reason = ProblemDetailsHelper.Explain("Business-unit hierarchy read failed.", ex)
            });
        }

        // 3. List each config's containers and read each container's binding once.
        var seen = new Dictionary<string, SeenContainer>(StringComparer.Ordinal);
        var listedBy = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        var failedConfigs = new HashSet<Guid>();

        foreach (var loaded in configs)
        {
            ct.ThrowIfCancellationRequested();
            var config = loaded.Config;

            Microsoft.Graph.GraphServiceClient graphClient;
            IReadOnlyList<SpeAdminGraphService.SpeContainerSummary> containers;
            try
            {
                graphClient = await _graphService.GetTypeWideClientForConfigAsync(config, ct).ConfigureAwait(false);
                // Type-wide listing returns every customer's containers; keep only this stamp's before any per-container
                // read (task 227d, owner D29).
                containers = await _graphService.FilterOwnedAsync(
                    await _graphService.ListContainersAsync(graphClient, config.ContainerTypeId, ct).ConfigureAwait(false),
                    c => c.Id,
                    ct).ConfigureAwait(false);

                concerns.Add(new ConcernOutcome
                {
                    Concern = $"Graph containers (config {config.ConfigId})",
                    Succeeded = true
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Failed to fetch containers for configId {ConfigId} (containerTypeId={ContainerTypeId}). Skipping.",
                    config.ConfigId, config.ContainerTypeId);

                failedConfigs.Add(config.ConfigId);
                concerns.Add(new ConcernOutcome
                {
                    Concern = $"Graph containers (config {config.ConfigId})",
                    Succeeded = false,
                    Reason = ProblemDetailsHelper.Explain("Container list failed.", ex)
                });
                continue;
            }

            foreach (var container in containers)
            {
                if (string.IsNullOrWhiteSpace(container.Id))
                {
                    continue;
                }

                if (!listedBy.TryGetValue(container.Id, out var listers))
                {
                    listedBy[container.Id] = listers = new List<Guid>();
                }

                listers.Add(config.ConfigId);

                // Configs sharing a type list the same containers: each container's binding is read ONCE.
                if (seen.ContainsKey(container.Id))
                {
                    continue;
                }

                var containerType = string.IsNullOrWhiteSpace(container.ContainerTypeId)
                    ? config.ContainerTypeId
                    : container.ContainerTypeId;

                try
                {
                    var read = await _graphService.GetContainerBindingAsync(graphClient, container.Id, deleted: false, ct)
                        .ConfigureAwait(false);

                    // Listed a moment ago and gone now: nothing to count.
                    seen[container.Id] = read is null
                        ? new SeenContainer(containerType, container.StorageUsedInBytes, Binding: null, Gone: true)
                        : new SeenContainer(containerType, container.StorageUsedInBytes, read.Binding, Gone: false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Dashboard sync: the business-unit binding of container {ContainerId} could not be read — it is " +
                        "counted for nobody.", container.Id);
                    seen[container.Id] = new SeenContainer(containerType, container.StorageUsedInBytes, Binding: null, Gone: false);
                }
            }

            _logger.LogDebug(
                "Config {ConfigId}: {Count} containers listed", config.ConfigId, containers.Count);
        }

        // A binding that could not be read leaves every config that LISTED the container possibly incomplete. No count in
        // the reason: the containers of a shared type may be other customers' (Model 1).
        var incompleteConfigs = seen
            .Where(kv => kv.Value is { Binding: null, Gone: false })
            .SelectMany(kv => listedBy[kv.Key])
            .ToHashSet();

        foreach (var config in configs.Where(c => incompleteConfigs.Contains(c.Config.ConfigId)))
        {
            concerns.Add(new ConcernOutcome
            {
                Concern = $"{BindingConcernPrefix}{config.Config.ConfigId})",
                Succeeded = false,
                Reason = "One or more containers of this configuration's container type could not be attributed to a " +
                         "business unit, so counts and storage may be incomplete."
            });
        }

        // 4. Attribute each container once.
        var attributable = configs
            .Select(c => new AttributableConfig(c.Config.ConfigId, c.Config.ContainerTypeId, c.BusinessUnitId))
            .ToList();

        var aggregate = new Aggregate();
        foreach (var config in configs)
        {
            aggregate.Counts[config.Config.ConfigId.ToString()] = failedConfigs.Contains(config.Config.ConfigId) ? -1 : 0;
        }

        foreach (var container in seen.Values)
        {
            if (container.Binding is not { } binding)
            {
                continue;   // unreadable (or gone) — counted for nobody
            }

            var attribution = AttributeContainer(binding, container.ContainerTypeId, attributable, hierarchy);
            switch (attribution.Kind)
            {
                case ContainerAttributionKind.Config when attribution.ConfigId is { } ownerId:
                    var key = ownerId.ToString();
                    if (aggregate.Counts.TryGetValue(key, out var count) && count >= 0)
                    {
                        aggregate.Counts[key] = count + 1;
                        if (container.StorageUsedInBytes is { } bytes)
                        {
                            aggregate.Storage[key] = aggregate.Storage.GetValueOrDefault(key) + bytes;
                            aggregate.Reporting[key] = aggregate.Reporting.GetValueOrDefault(key) + 1;
                        }
                    }

                    break;

                case ContainerAttributionKind.Unattributed:
                    aggregate.UnattributedCount++;
                    if (container.StorageUsedInBytes is { } unattributedBytes)
                    {
                        aggregate.UnattributedStorage += unattributedBytes;
                        aggregate.UnattributedReporting++;
                    }

                    break;
            }
        }

        return Summarize(aggregate, concerns, null);
    }

    /// <summary>One listed container, before attribution. A null binding = the read failed, or (Gone) it vanished.</summary>
    private sealed record SeenContainer(string ContainerTypeId, long? StorageUsedInBytes, SpeContainerBinding? Binding, bool Gone);

    /// <summary>The per-config figures a sync pass accumulates.</summary>
    private sealed class Aggregate
    {
        public static Aggregate Empty => new();

        public Dictionary<string, int> Counts { get; } = new();
        public Dictionary<string, long> Storage { get; } = new();
        public Dictionary<string, int> Reporting { get; } = new();
        public int UnattributedCount { get; set; }
        public long UnattributedStorage { get; set; }
        public int UnattributedReporting { get; set; }
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
    /// Derives overall health and the status line from the per-concern outcomes, and totals from the per-config and
    /// unattributed figures.
    /// </summary>
    /// <remarks>
    /// The single place a <see cref="DashboardMetrics"/> is constructed after a sync attempt, so health can
    /// never drift from the concerns that produced it. <c>SyncSucceeded</c> is kept as a derived mirror of
    /// <c>SyncHealth == Healthy</c> for existing clients.
    /// </remarks>
    private static DashboardMetrics Summarize(
        Aggregate aggregate,
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

        var countedConfigs = aggregate.Counts.Where(kv => kv.Value >= 0).Select(kv => kv.Key).ToHashSet();

        return new DashboardMetrics
        {
            TotalContainerCount = countedConfigs.Sum(k => aggregate.Counts[k]) + aggregate.UnattributedCount,
            TotalStorageUsedInBytes = countedConfigs.Sum(k => aggregate.Storage.GetValueOrDefault(k)) + aggregate.UnattributedStorage,
            StorageReportingContainerCount = countedConfigs.Sum(k => aggregate.Reporting.GetValueOrDefault(k)) + aggregate.UnattributedReporting,
            ContainerCountByConfig = new Dictionary<string, int>(aggregate.Counts),
            StorageUsedInBytesByConfig = countedConfigs.ToDictionary(k => k, k => aggregate.Storage.GetValueOrDefault(k)),
            StorageReportingContainerCountByConfig = countedConfigs.ToDictionary(k => k, k => aggregate.Reporting.GetValueOrDefault(k)),
            UnattributedContainerCount = aggregate.UnattributedCount,
            UnattributedStorageUsedInBytes = aggregate.UnattributedStorage,
            UnattributedStorageReportingContainerCount = aggregate.UnattributedReporting,
            LastSyncedAt = DateTimeOffset.UtcNow,
            SyncSucceeded = health == SyncHealth.Healthy,
            SyncHealth = health,
            SyncStatus = status,
            Concerns = concerns
        };
    }

    /// <summary>
    /// Reads all active container type configs from the sprk_specontainertypeconfigs Dataverse entity, each with the
    /// business unit it belongs to.
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

            var configs = new List<LoadedConfig>(records.Count);

            foreach (var record in records)
            {
                // The Key Vault secret name is deliberately NOT required: since 2026-10-04 container work
                // runs as the BFF's own identity and no credential is read from the config. Requiring it
                // here would silently drop every secret-free config from the dashboard.
                if (!Guid.TryParse(record.Id, out var configId)
                    || string.IsNullOrWhiteSpace(record.ContainerTypeId)
                    || string.IsNullOrWhiteSpace(record.OwningAppId)
                    || !record.EnvironmentId.HasValue
                    || !tenantById.TryGetValue(record.EnvironmentId.Value, out var tenantId))
                {
                    _logger.LogWarning(
                        "Skipping incomplete container type config record: id={Id}", record.Id);
                    skippedIncomplete++;
                    continue;
                }

                configs.Add(new LoadedConfig(
                    new SpeAdminGraphService.ContainerTypeConfig(
                        ConfigId: configId,
                        ContainerTypeId: record.ContainerTypeId,
                        ClientId: record.OwningAppId,
                        TenantId: tenantId,
                        SecretKeyVaultName: record.SecretKeyVaultName ?? string.Empty),
                    record.BusinessUnitId is { } unit && unit != Guid.Empty ? unit : null));            }

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
                Array.Empty<LoadedConfig>(),
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
