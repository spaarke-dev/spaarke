using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Workspace;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Q4 hybrid persistence for R6 Pillar 6a workspace tabs:
/// Redis hot tier (24h TTL) + Cosmos durable tier (pin / matter-attach).
///
/// <para>
/// Redis key (post-task-014 migration): wrapper-produced
/// <c>tenant:{tenantId}:workspace-state:{sessionId}:v1</c> (ADR-014 + NFR-16 binding;
/// FR-05 tenant-scoping enforced by <see cref="ITenantCache"/>).
/// Value: a JSON dictionary mapping <c>tabId → WorkspaceTab</c>. Per-tab writes
/// perform a read-modify-write inside the JSON value.
/// </para>
///
/// <para>
/// Cosmos container: <c>memory</c> (reused — see placement justification note
/// <c>projects/spaarke-ai-platform-unification-r6/notes/task-051-placement-justification.md</c>).
/// Partition key <c>/tenantId</c>. Document discriminator <c>"workspace-tab"</c> co-exists
/// with the existing matter-memory documents on the same partition without conflict (id
/// prefix <c>workspace-tab_</c> guarantees no id collision with
/// the retired <c>MatterMemoryService</c>'s <c>{tenantId}_{matterId}</c>
/// format).
/// </para>
///
/// <para>
/// Placement (CLAUDE.md §10 / ADR-013): workspace-state plumbing only. ZERO AI-internal
/// constructor deps (<c>IOpenAiClient</c>, <c>IPlaybookService</c>, etc.).
/// </para>
///
/// <para>
/// Lifetime: Scoped — matches consumer endpoint scopes. <see cref="ITenantCache"/> and
/// <see cref="CosmosClient"/> are Singleton (injected); the scoped wrapper is stateless.
/// </para>
/// </summary>
public sealed class WorkspaceStateService : IWorkspaceStateService
{
    /// <summary>Cache resource name (per FR-05 tenant-scoped key — produces
    /// <c>tenant:{tenantId}:workspace-state:{sessionId}:v1</c>).</summary>
    internal const string CacheResource = "workspace-state";

    /// <summary>Cache schema version (per ADR-009 key versioning).</summary>
    internal const int CacheVersion = 1;

    /// <summary>Redis hot-tier TTL (24h per FR-32 / spec). Migrated from
    /// <c>SlidingExpiration</c> to <c>AbsoluteExpirationRelativeToNow</c> per ITenantCache wrapper
    /// contract (the wrapper only exposes absolute TTL; spec-required 24h preserved).</summary>
    internal static readonly TimeSpan RedisTtl = TimeSpan.FromHours(24);

    /// <summary>Cosmos container name (reused — see placement justification).</summary>
    internal const string CosmosContainerName = "memory";

    /// <summary>Cosmos document-id prefix that disambiguates from matter-memory docs.</summary>
    internal const string CosmosIdPrefix = "workspace-tab";

    /// <summary>Cosmos document discriminator field (mirrors id prefix for query convenience).</summary>
    internal const string CosmosDocumentTypeValue = "workspace-tab";

    /// <summary>
    /// JSON serialization options — System.Text.Json polymorphism reads/writes the <c>kind</c>
    /// discriminator on <see cref="WorkspaceTabWidgetData"/>. CamelCase property names are
    /// applied by the explicit <c>[JsonPropertyName]</c> attributes on the DTOs.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ITenantCache _cache;
    private readonly CosmosClient _cosmosClient;
    private readonly string _databaseName;
    private readonly ILogger<WorkspaceStateService> _logger;

    public WorkspaceStateService(
        ITenantCache cache,
        CosmosClient cosmosClient,
        IConfiguration configuration,
        ILogger<WorkspaceStateService> logger)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(cosmosClient);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _cache = cache;
        _cosmosClient = cosmosClient;
        _databaseName = configuration["CosmosPersistence:DatabaseName"]
            ?? throw new InvalidOperationException(
                "CosmosPersistence:DatabaseName is not configured. " +
                "Add this setting to appsettings.json or Azure App Service configuration.");
        _logger = logger;
    }

    // =========================================================================
    // IWorkspaceStateService
    // =========================================================================

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkspaceTab>> GetTabsAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        // Hot tier first.
        var hot = await LoadHotAsync(tenantId, sessionId, ct);

        // Durable tier — Cosmos query partitioned by tenantId, filtered on sessionId.
        var durable = await LoadDurableForSessionAsync(tenantId, sessionId, ct);

        // Merge: hot tier overrides durable on same tab id (most-recent wins).
        if (hot.Count == 0 && durable.Count == 0)
        {
            return Array.Empty<WorkspaceTab>();
        }

        var merged = new Dictionary<string, WorkspaceTab>(StringComparer.Ordinal);
        foreach (var tab in durable)
        {
            merged[tab.Id] = tab;
        }
        foreach (var (tabId, tab) in hot)
        {
            merged[tabId] = tab;
        }

        return merged.Values.ToList();
    }

    // =========================================================================
    // Redis helpers (via ITenantCache wrapper — FR-05 tenant-scoped keys)
    // =========================================================================

    /// <summary>
    /// Builds the (legacy) Redis hot-tier key — kept for documentation / test-name
    /// continuity. The wrapper produces <c>tenant:{tenantId}:workspace-state:{sessionId}:v1</c>
    /// on the wire; this helper is retained because external tests reference the legacy
    /// <c>workspace:{tenantId}:{sessionId}</c> shape.
    /// </summary>
    internal static string BuildRedisKey(string tenantId, string sessionId)
        => $"tenant:{tenantId}:{CacheResource}:{sessionId}:v{CacheVersion}";

    private async Task<Dictionary<string, WorkspaceTab>> LoadHotAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct)
    {
        try
        {
            var deserialized = await _cache.GetAsync<Dictionary<string, WorkspaceTab>>(
                tenantId, CacheResource, sessionId, CacheVersion, ct: ct);
            return deserialized ?? new Dictionary<string, WorkspaceTab>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WorkspaceStateService: Redis read failed for session {SessionId} (tenant={TenantId}) — returning empty hot tier",
                sessionId, tenantId);
            return new Dictionary<string, WorkspaceTab>(StringComparer.Ordinal);
        }
    }

    // =========================================================================
    // Cosmos helpers (durable tier — read only)
    // =========================================================================

    private Container GetContainer()
        => _cosmosClient.GetContainer(_databaseName, CosmosContainerName);

    private async Task<IReadOnlyList<WorkspaceTab>> LoadDurableForSessionAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct)
    {
        try
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.documentType = @type AND c.sessionId = @sessionId")
                .WithParameter("@type", CosmosDocumentTypeValue)
                .WithParameter("@sessionId", sessionId);

            var requestOptions = new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(tenantId),
            };

            using var iterator = GetContainer().GetItemQueryIterator<WorkspaceTabDurableDocument>(
                query, requestOptions: requestOptions);

            var results = new List<WorkspaceTab>();
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                foreach (var item in page)
                {
                    if (item.Tab is not null)
                    {
                        results.Add(item.Tab);
                    }
                }
            }
            return results;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<WorkspaceTab>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WorkspaceStateService: Cosmos durable read failed for session {SessionId} (tenant={TenantId}) — returning empty",
                sessionId, tenantId);
            return Array.Empty<WorkspaceTab>();
        }
    }

    /// <summary>
    /// Cosmos document envelope for durable workspace-tab rows. Co-exists with
    /// <c>MatterMemory</c> docs on the same container via the <c>documentType</c>
    /// discriminator and the <c>workspace-tab_</c> id prefix.
    /// </summary>
    internal sealed class WorkspaceTabDurableDocument
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        /// <summary>Document discriminator — <c>"workspace-tab"</c>.</summary>
        [JsonPropertyName("documentType")]
        public required string DocumentType { get; init; }

        /// <summary>Tenant — also Cosmos partition key /tenantId (ADR-015).</summary>
        [JsonPropertyName("tenantId")]
        public required string TenantId { get; init; }

        /// <summary>Owning chat session id (queryable index).</summary>
        [JsonPropertyName("sessionId")]
        public required string SessionId { get; init; }

        /// <summary>Matter id attached on pin (queryable index).</summary>
        [JsonPropertyName("matterId")]
        public required string MatterId { get; init; }

        /// <summary>Embedded canonical tab record.</summary>
        [JsonPropertyName("tab")]
        public WorkspaceTab? Tab { get; init; }
    }
}
