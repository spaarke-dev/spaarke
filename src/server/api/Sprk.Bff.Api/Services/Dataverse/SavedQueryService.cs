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
/// Cached projection of Dataverse <c>savedquery</c> rows for client consumption.
/// </summary>
/// <remarks>
/// <para>
/// Implements FR-BFF-01 and FR-BFF-02:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="GetSavedQueryAsync"/>: single savedquery payload (entity + FetchXML + LayoutXML + name).</description></item>
///   <item><description><see cref="GetSavedQueriesForEntityAsync"/>: list of user-visible saved queries for a given entity.</description></item>
/// </list>
/// <para>
/// Both methods cache via <see cref="IDistributedCache"/> (Redis in production) with a 1-hour absolute
/// TTL per spec.md FR-BFF-01/02. Cache keys per task 010 §6:
/// </para>
/// <list type="bullet">
///   <item><description><c>sdap:dv:savedquery:{savedQueryId}</c></description></item>
///   <item><description><c>sdap:dv:savedqueries:{entityLogicalName}</c></description></item>
/// </list>
/// <para>
/// Dataverse access uses the app-only <see cref="ServiceClient"/> exposed via
/// <see cref="DataverseServiceClientImpl.OrganizationService"/>. The authorization filter
/// validates Read privilege before the service is called.
/// </para>
/// </remarks>
internal sealed class SavedQueryService
{
    private readonly IDataverseService _dataverseService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<SavedQueryService> _logger;

    // 1-hour TTL per FR-BFF-01/02.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Dataverse savedquery attribute names.
    private const string AttrSavedQueryId = "savedqueryid";
    private const string AttrName = "name";
    private const string AttrReturnedTypeCode = "returnedtypecode";
    private const string AttrFetchXml = "fetchxml";
    private const string AttrLayoutXml = "layoutxml";
    private const string AttrIsDefault = "isdefault";
    private const string AttrQueryType = "querytype";
    private const string AttrStateCode = "statecode";

    public SavedQueryService(
        IDataverseService dataverseService,
        IDistributedCache cache,
        ILogger<SavedQueryService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the projected <see cref="SavedQueryDto"/> for the given saved query id.
    /// </summary>
    /// <param name="savedQueryId">The savedquery primary id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The projection, or <c>null</c> if the saved query does not exist.</returns>
    public async Task<SavedQueryDto?> GetSavedQueryAsync(Guid savedQueryId, CancellationToken ct)
    {
        var cacheKey = $"sdap:dv:savedquery:{savedQueryId:D}";

        var cached = await TryGetFromCacheAsync<SavedQueryDto>(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("SavedQuery cache HIT for {SavedQueryId}", savedQueryId);
            return cached;
        }

        _logger.LogDebug("SavedQuery cache MISS for {SavedQueryId}; fetching from Dataverse", savedQueryId);

        var sw = Stopwatch.StartNew();
        var serviceClient = GetServiceClient();
        if (serviceClient is null)
        {
            return null;
        }

        Entity? entity;
        try
        {
            entity = await serviceClient.RetrieveAsync(
                "savedquery",
                savedQueryId,
                new ColumnSet(AttrName, AttrReturnedTypeCode, AttrFetchXml, AttrLayoutXml),
                ct);
        }
        catch (Exception ex) when (IsNotFoundException(ex))
        {
            _logger.LogInformation("SavedQuery {SavedQueryId} not found in Dataverse", savedQueryId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving savedquery {SavedQueryId}", savedQueryId);
            throw;
        }

        sw.Stop();

        if (entity is null)
        {
            return null;
        }

        var dto = new SavedQueryDto(
            EntityName: entity.GetAttributeValue<string>(AttrReturnedTypeCode) ?? string.Empty,
            FetchXml: entity.GetAttributeValue<string>(AttrFetchXml) ?? string.Empty,
            LayoutXml: entity.GetAttributeValue<string>(AttrLayoutXml) ?? string.Empty,
            Name: entity.GetAttributeValue<string>(AttrName) ?? string.Empty);

        _logger.LogInformation(
            "Loaded savedquery {SavedQueryId} (entity={EntityName}) from Dataverse in {ElapsedMs}ms",
            savedQueryId, dto.EntityName, sw.ElapsedMilliseconds);

        await TrySetInCacheAsync(cacheKey, dto, ct);

        return dto;
    }

    /// <summary>
    /// Returns the list of active <c>querytype=0</c> (User Owned) saved queries for the given entity.
    /// </summary>
    /// <param name="entityLogicalName">The entity logical name (e.g., <c>sprk_matter</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of summaries; empty if the entity has no saved queries.</returns>
    public async Task<IReadOnlyList<SavedQuerySummaryDto>> GetSavedQueriesForEntityAsync(
        string entityLogicalName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return Array.Empty<SavedQuerySummaryDto>();
        }

        var normalised = entityLogicalName.Trim().ToLowerInvariant();
        var cacheKey = $"sdap:dv:savedqueries:{normalised}";

        var cached = await TryGetFromCacheAsync<List<SavedQuerySummaryDto>>(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("SavedQueries cache HIT for entity {Entity} ({Count} items)", normalised, cached.Count);
            return cached;
        }

        _logger.LogDebug("SavedQueries cache MISS for entity {Entity}; fetching from Dataverse", normalised);

        var sw = Stopwatch.StartNew();
        var serviceClient = GetServiceClient();
        if (serviceClient is null)
        {
            return Array.Empty<SavedQuerySummaryDto>();
        }

        var query = new QueryExpression("savedquery")
        {
            ColumnSet = new ColumnSet(AttrSavedQueryId, AttrName, AttrIsDefault, AttrQueryType, AttrReturnedTypeCode),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression(AttrReturnedTypeCode, ConditionOperator.Equal, normalised),
                    new ConditionExpression(AttrStateCode, ConditionOperator.Equal, 0),     // Active
                    new ConditionExpression(AttrQueryType, ConditionOperator.Equal, 0)      // User Owned
                }
            }
        };

        EntityCollection result;
        try
        {
            result = await serviceClient.RetrieveMultipleAsync(query, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying savedqueries for entity {Entity}", normalised);
            throw;
        }

        sw.Stop();

        var summaries = result.Entities
            .Select(e => new SavedQuerySummaryDto(
                Id: e.Id,
                Name: e.GetAttributeValue<string>(AttrName) ?? string.Empty,
                IsDefault: e.GetAttributeValue<bool?>(AttrIsDefault) ?? false,
                QueryType: e.GetAttributeValue<int?>(AttrQueryType) ?? 0))
            .ToList();

        _logger.LogInformation(
            "Loaded {Count} savedqueries for entity {Entity} from Dataverse in {ElapsedMs}ms",
            summaries.Count, normalised, sw.ElapsedMilliseconds);

        await TrySetInCacheAsync(cacheKey, summaries, ct);

        return summaries;
    }

    private ServiceClient? GetServiceClient()
    {
        if (_dataverseService is not DataverseServiceClientImpl impl)
        {
            _logger.LogError(
                "SavedQueryService: IDataverseService is not DataverseServiceClientImpl (actual: {Type})",
                _dataverseService.GetType().FullName);
            return null;
        }

        var client = impl.OrganizationService;
        if (client is null || !client.IsReady)
        {
            _logger.LogError("SavedQueryService: ServiceClient is not ready");
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
            _logger.LogWarning(ex, "Failed to read cache for key {Key}; falling back to Dataverse", cacheKey);
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
            _logger.LogWarning(ex, "Failed to write cache for key {Key}; continuing without cache", cacheKey);
        }
    }

    private static bool IsNotFoundException(Exception ex)
    {
        // Dataverse returns FaultException with code 0x80040217 (or similar) when the record doesn't exist.
        var message = ex.Message;
        return message.Contains("0x80040217", StringComparison.OrdinalIgnoreCase)
               || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
               || message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }
}
