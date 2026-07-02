using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Cached projection of Dataverse <c>sprk_gridconfiguration</c> rows for the
/// WorkspaceLayoutWizard's per-section configId picker (DEF-002 of
/// <c>spaarke-dataset-grid-framework-r2</c>).
/// </summary>
/// <remarks>
/// <para>
/// Implements <c>GET /api/dataverse/gridconfigurations/{entityLogicalName}</c>: returns
/// active grid-configuration rows filtered by the section's target entity. The wizard uses
/// the list to populate its "Grid configuration" dropdown so makers can visually pick a
/// <c>sprk_gridconfiguration</c> record instead of pasting a GUID.
/// </para>
/// <para>
/// Structurally mirrors <see cref="SavedQueryService"/> (task 011 precedent) —
/// <c>IDistributedCache</c> with a 1-hour absolute TTL, cache key
/// <c>sdap:dv:gridconfigurations:{entityLogicalName}</c>, app-only <see cref="ServiceClient"/>
/// via <see cref="DataverseServiceClientImpl.OrganizationService"/>. Read privilege is enforced
/// by <see cref="DataverseAuthorizationFilter"/> at the endpoint level.
/// </para>
/// </remarks>
internal sealed class GridConfigurationService
{
    private readonly IDataverseService _dataverseService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<GridConfigurationService> _logger;

    // 1-hour TTL — matches SavedQueryService precedent. sprk_gridconfiguration rows are
    // maker-authored + change rarely; TTL trade-off matches wizard's picker-hydration cadence.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // sprk_gridconfiguration attribute names (documented in ConfigurationService.ts and
    // universal-dataset-grid-architecture.md).
    private const string EntityLogicalName = "sprk_gridconfiguration";
    private const string AttrId = "sprk_gridconfigurationid";
    private const string AttrName = "sprk_name";
    private const string AttrEntityLogicalName = "sprk_entitylogicalname";
    private const string AttrIsDefault = "sprk_isdefault";
    private const string AttrSortOrder = "sprk_sortorder";
    private const string AttrStateCode = "statecode";

    public GridConfigurationService(
        IDataverseService dataverseService,
        IDistributedCache cache,
        ILogger<GridConfigurationService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the list of ACTIVE <c>sprk_gridconfiguration</c> rows targeting the given entity,
    /// ordered by <c>sprk_sortorder</c>.
    /// </summary>
    /// <param name="entityLogicalName">The entity logical name (e.g., <c>sprk_matter</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of summaries; empty when the entity has no grid configurations or the
    /// underlying <c>sprk_gridconfiguration</c> entity is not deployed in the environment.</returns>
    public async Task<IReadOnlyList<GridConfigurationSummaryDto>> GetForEntityAsync(
        string entityLogicalName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return Array.Empty<GridConfigurationSummaryDto>();
        }

        var normalised = entityLogicalName.Trim().ToLowerInvariant();
        var cacheKey = $"sdap:dv:gridconfigurations:{normalised}";

        var cached = await TryGetFromCacheAsync<List<GridConfigurationSummaryDto>>(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug(
                "GridConfigurations cache HIT for entity {Entity} ({Count} items)",
                normalised, cached.Count);
            return cached;
        }

        _logger.LogDebug(
            "GridConfigurations cache MISS for entity {Entity}; fetching from Dataverse",
            normalised);

        var sw = Stopwatch.StartNew();
        var serviceClient = GetServiceClient();
        if (serviceClient is null)
        {
            return Array.Empty<GridConfigurationSummaryDto>();
        }

        var query = new QueryExpression(EntityLogicalName)
        {
            ColumnSet = new ColumnSet(AttrId, AttrName, AttrEntityLogicalName, AttrIsDefault, AttrSortOrder),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression(AttrEntityLogicalName, ConditionOperator.Equal, normalised),
                    new ConditionExpression(AttrStateCode, ConditionOperator.Equal, 0), // Active only
                }
            },
            Orders =
            {
                new OrderExpression(AttrSortOrder, OrderType.Ascending),
                new OrderExpression(AttrName, OrderType.Ascending),
            }
        };

        EntityCollection result;
        try
        {
            result = await serviceClient.RetrieveMultipleAsync(query, ct);
        }
        catch (Exception ex) when (IsEntityNotFoundException(ex))
        {
            // Graceful degradation: sprk_gridconfiguration may not be deployed in every
            // environment yet (per ConfigurationService.ts precedent). Return empty so the
            // wizard falls back to "None (use default)" without surfacing a 500.
            _logger.LogInformation(
                "sprk_gridconfiguration entity not present in environment; returning empty list");
            return Array.Empty<GridConfigurationSummaryDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error querying sprk_gridconfiguration for entity {Entity}", normalised);
            throw;
        }

        sw.Stop();

        var summaries = result.Entities
            .Select(e => new GridConfigurationSummaryDto(
                Id: e.Id,
                Name: e.GetAttributeValue<string>(AttrName) ?? string.Empty,
                EntityLogicalName: e.GetAttributeValue<string>(AttrEntityLogicalName) ?? string.Empty,
                IsDefault: e.GetAttributeValue<bool?>(AttrIsDefault) ?? false,
                SortOrder: e.GetAttributeValue<int?>(AttrSortOrder) ?? 100))
            .ToList();

        _logger.LogInformation(
            "Loaded {Count} sprk_gridconfiguration rows for entity {Entity} from Dataverse in {ElapsedMs}ms",
            summaries.Count, normalised, sw.ElapsedMilliseconds);

        await TrySetInCacheAsync(cacheKey, summaries, ct);

        return summaries;
    }

    private ServiceClient? GetServiceClient()
    {
        if (_dataverseService is not DataverseServiceClientImpl impl)
        {
            _logger.LogError(
                "GridConfigurationService: IDataverseService is not DataverseServiceClientImpl (actual: {Type})",
                _dataverseService.GetType().FullName);
            return null;
        }

        var client = impl.OrganizationService;
        if (client is null || !client.IsReady)
        {
            _logger.LogError("GridConfigurationService: ServiceClient is not ready");
            return null;
        }

        return client;
    }

    private async Task<T?> TryGetFromCacheAsync<T>(string cacheKey, CancellationToken ct) where T : class
    {
        try
        {
            var cached = await _cache.GetStringAsync(cacheKey, ct);
            if (cached is null)
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(cached, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read cache for key {Key}; falling back to Dataverse", cacheKey);
            return null;
        }
    }

    private async Task TrySetInCacheAsync<T>(string cacheKey, T value, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await _cache.SetStringAsync(
                cacheKey,
                json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                ct);

            _logger.LogDebug("Cached key {Key} with TTL {TtlHours}h", cacheKey, CacheTtl.TotalHours);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to write cache for key {Key}; continuing without cache", cacheKey);
        }
    }

    private static bool IsEntityNotFoundException(Exception ex)
    {
        // Mirrors SavedQueryService.IsNotFoundException + ConfigurationService.ts precedent.
        var message = ex.Message;
        return message.Contains("could not be found", StringComparison.OrdinalIgnoreCase)
               || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
               || message.Contains("not found", StringComparison.OrdinalIgnoreCase)
               || message.Contains("0x80040217", StringComparison.OrdinalIgnoreCase);
    }
}
