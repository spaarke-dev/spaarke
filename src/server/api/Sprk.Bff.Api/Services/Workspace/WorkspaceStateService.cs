using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Models.Workspace;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Q4 hybrid persistence for R6 Pillar 6a workspace tabs:
/// Redis hot tier (24h TTL) + Cosmos durable tier (pin / matter-attach).
///
/// <para>
/// Redis key: <c>workspace:{tenantId}:{sessionId}</c> (ADR-014 + NFR-16 binding).
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
/// <see cref="Sprk.Bff.Api.Services.Ai.Memory.MatterMemoryService"/>'s <c>{tenantId}_{matterId}</c>
/// format).
/// </para>
///
/// <para>
/// Placement (CLAUDE.md §10 / ADR-013): workspace-state plumbing only. ZERO AI-internal
/// constructor deps (<c>IOpenAiClient</c>, <c>IPlaybookService</c>, etc.).
/// </para>
///
/// <para>
/// Lifetime: Scoped — matches consumer endpoint scopes. <see cref="IDistributedCache"/> and
/// <see cref="CosmosClient"/> are Singleton (injected); the scoped wrapper is stateless.
/// </para>
/// </summary>
public sealed class WorkspaceStateService : IWorkspaceStateService
{
    /// <summary>Redis cache-key prefix per NFR-16 (binding).</summary>
    internal const string RedisKeyPrefix = "workspace";

    /// <summary>Redis hot-tier TTL (24h per FR-32 / spec).</summary>
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

    private readonly IDistributedCache _cache;
    private readonly CosmosClient _cosmosClient;
    private readonly string _databaseName;
    private readonly ILogger<WorkspaceStateService> _logger;

    public WorkspaceStateService(
        IDistributedCache cache,
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

    /// <inheritdoc/>
    public async Task UpsertTabAsync(
        string tenantId,
        string sessionId,
        WorkspaceTab tab,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(tab);

        if (!string.Equals(tab.TenantId, tenantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Tenant mismatch: tab.TenantId='{tab.TenantId}' does not match arg tenantId='{tenantId}' (NFR-16 isolation).");
        }
        if (!string.Equals(tab.SessionId, sessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Session mismatch: tab.SessionId='{tab.SessionId}' does not match arg sessionId='{sessionId}'.");
        }

        var hot = await LoadHotAsync(tenantId, sessionId, ct);
        hot[tab.Id] = tab;
        await WriteHotAsync(tenantId, sessionId, hot, ct);

        _logger.LogDebug(
            "WorkspaceStateService: UpsertTab id={TabId} session={SessionId} tenant={TenantId} pinned={IsPinned}",
            tab.Id, sessionId, tenantId, tab.IsPinned);
    }

    /// <inheritdoc/>
    public async Task PinTabAsync(
        string tenantId,
        string sessionId,
        string tabId,
        string matterId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        ArgumentException.ThrowIfNullOrWhiteSpace(matterId);

        var hot = await LoadHotAsync(tenantId, sessionId, ct);
        if (!hot.TryGetValue(tabId, out var existing))
        {
            throw new KeyNotFoundException(
                $"Tab '{tabId}' not found in session '{sessionId}' (tenant '{tenantId}'). Pin requires an existing hot-tier row.");
        }

        // Promote: flip isPinned + attach matterId (preserve matterName if matterId matches).
        var matterName = string.Equals(existing.MatterContext.MatterId, matterId, StringComparison.Ordinal)
            ? existing.MatterContext.MatterName
            : matterId; // Fallback when matter changes — endpoint layer enriches name lookup.

        var promoted = new WorkspaceTab
        {
            Id = existing.Id,
            WidgetType = existing.WidgetType,
            WidgetData = existing.WidgetData,
            SessionId = existing.SessionId,
            TenantId = existing.TenantId,
            VisibleToAssistant = existing.VisibleToAssistant,
            SourceProvenance = existing.SourceProvenance,
            MatterContext = new WorkspaceTabMatterContext
            {
                MatterId = matterId,
                MatterName = matterName,
            },
            IsPinned = true,
            CanEdit = existing.CanEdit,
            LastUserEditAt = existing.LastUserEditAt,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o"),
        };

        // Refresh Redis hot tier.
        hot[tabId] = promoted;
        await WriteHotAsync(tenantId, sessionId, hot, ct);

        // Write-through to Cosmos durable.
        await WriteDurableAsync(promoted, ct);

        _logger.LogInformation(
            "WorkspaceStateService: PinTab id={TabId} session={SessionId} tenant={TenantId} matterId={MatterId}",
            tabId, sessionId, tenantId, matterId);
    }

    /// <inheritdoc/>
    public async Task CloseTabAsync(
        string tenantId,
        string sessionId,
        string tabId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);

        var hot = await LoadHotAsync(tenantId, sessionId, ct);
        if (!hot.Remove(tabId))
        {
            // Idempotent — tab not present is OK.
            _logger.LogDebug(
                "WorkspaceStateService: CloseTab no-op (id={TabId} not in session={SessionId} tenant={TenantId})",
                tabId, sessionId, tenantId);
            return;
        }

        if (hot.Count == 0)
        {
            await RemoveHotKeyAsync(tenantId, sessionId, ct);
        }
        else
        {
            await WriteHotAsync(tenantId, sessionId, hot, ct);
        }

        _logger.LogDebug(
            "WorkspaceStateService: CloseTab id={TabId} session={SessionId} tenant={TenantId}",
            tabId, sessionId, tenantId);
    }

    // =========================================================================
    // Redis helpers
    // =========================================================================

    /// <summary>
    /// Builds the Redis hot-tier key — <c>workspace:{tenantId}:{sessionId}</c>.
    /// Per-tenant isolation per ADR-014 + NFR-16 (binding).
    /// </summary>
    internal static string BuildRedisKey(string tenantId, string sessionId)
        => $"{RedisKeyPrefix}:{tenantId}:{sessionId}";

    private async Task<Dictionary<string, WorkspaceTab>> LoadHotAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct)
    {
        try
        {
            var key = BuildRedisKey(tenantId, sessionId);
            var bytes = await _cache.GetAsync(key, ct);
            if (bytes is null || bytes.Length == 0)
            {
                return new Dictionary<string, WorkspaceTab>(StringComparer.Ordinal);
            }

            var deserialized = JsonSerializer.Deserialize<Dictionary<string, WorkspaceTab>>(bytes, JsonOpts);
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

    private async Task WriteHotAsync(
        string tenantId,
        string sessionId,
        Dictionary<string, WorkspaceTab> tabs,
        CancellationToken ct)
    {
        var key = BuildRedisKey(tenantId, sessionId);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(tabs, JsonOpts);

        // 24h TTL per FR-32. SlidingExpiration keeps actively-touched sessions alive,
        // AbsoluteExpirationRelativeToNow caps total lifetime so abandoned sessions decay.
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = RedisTtl,
        };

        try
        {
            await _cache.SetAsync(key, bytes, options, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WorkspaceStateService: Redis write failed for session {SessionId} (tenant={TenantId})",
                sessionId, tenantId);
        }
    }

    private async Task RemoveHotKeyAsync(string tenantId, string sessionId, CancellationToken ct)
    {
        try
        {
            await _cache.RemoveAsync(BuildRedisKey(tenantId, sessionId), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WorkspaceStateService: Redis remove failed for session {SessionId} (tenant={TenantId})",
                sessionId, tenantId);
        }
    }

    // =========================================================================
    // Cosmos helpers (durable tier)
    // =========================================================================

    /// <summary>
    /// Cosmos document id for a durable workspace-tab row:
    /// <c>workspace-tab_{tenantId}_{tabId}</c>. The prefix disambiguates from
    /// <c>MatterMemoryService</c>'s <c>{tenantId}_{matterId}</c> docs on the same container.
    /// </summary>
    internal static string BuildCosmosId(string tenantId, string tabId)
        => $"{CosmosIdPrefix}_{tenantId}_{tabId}";

    private Container GetContainer()
        => _cosmosClient.GetContainer(_databaseName, CosmosContainerName);

    private async Task WriteDurableAsync(WorkspaceTab tab, CancellationToken ct)
    {
        try
        {
            var doc = new WorkspaceTabDurableDocument
            {
                Id = BuildCosmosId(tab.TenantId, tab.Id),
                DocumentType = CosmosDocumentTypeValue,
                TenantId = tab.TenantId,
                SessionId = tab.SessionId,
                MatterId = tab.MatterContext.MatterId,
                Tab = tab,
            };

            await GetContainer().UpsertItemAsync(
                item: doc,
                partitionKey: new PartitionKey(tab.TenantId),
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WorkspaceStateService: Cosmos durable write failed for tab {TabId} (session={SessionId}, tenant={TenantId})",
                tab.Id, tab.SessionId, tab.TenantId);
        }
    }

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
