using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Cached lookup service for AI actions using portable alternate keys.
/// Minimizes Dataverse queries in high-volume playbook execution scenarios.
/// </summary>
/// <remarks>
/// Performance characteristics:
/// - First lookup: ~50-100ms (Dataverse query + cache write)
/// - Cached lookups: &lt;1ms (in-memory)
/// - Cache TTL: 1 hour (action configs rarely change)
/// - Memory usage: ~1KB per cached action (negligible)
///
/// SaaS multi-environment support:
/// Same code works in DEV/QA/PROD without config changes.
/// Alternate keys travel with solution imports, GUIDs regenerate.
/// </remarks>
public class ActionLookupService : IActionLookupService
{
    private readonly IDataverseService _dataverseService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<ActionLookupService> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private const string CacheKeyPrefix = "action:code:";

    public ActionLookupService(
        IDataverseService dataverseService,
        IMemoryCache memoryCache,
        ILogger<ActionLookupService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ActionResponse> GetByCodeAsync(string actionCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actionCode))
        {
            throw new ArgumentException("Action code cannot be null or empty", nameof(actionCode));
        }

        var cacheKey = GetCacheKey(actionCode);

        // Try cache first
        if (_memoryCache.TryGetValue<ActionResponse>(cacheKey, out var cachedAction))
        {
            _logger.LogDebug(
                "Action {ActionCode} retrieved from cache (ID: {ActionId})",
                actionCode,
                cachedAction?.Id);

            return cachedAction!;
        }

        // Cache miss - query Dataverse using alternate key
        _logger.LogInformation(
            "Action {ActionCode} not in cache, querying Dataverse by alternate key",
            actionCode);

        try
        {
            // Build alternate key lookup
            var alternateKeyValues = new KeyAttributeCollection
            {
                { "sprk_actioncode", actionCode }
            };

            // Columns needed to build ActionResponse
            var columns = new[]
            {
                "sprk_analysisactionid",
                "sprk_name",
                "sprk_description",
                "sprk_actioncode",
                "statecode",
                "statuscode"
            };

            // Retrieve using alternate key (indexed, fast)
            var entity = await _dataverseService.RetrieveByAlternateKeyAsync(
                "sprk_analysisaction",
                alternateKeyValues,
                columns,
                ct);

            // Map to ActionResponse
            var action = MapEntityToActionResponse(entity);

            // Cache for 1 hour
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(CacheDuration)
                .SetSize(1); // Each action = 1 size unit (~1KB)

            _memoryCache.Set(cacheKey, action, cacheOptions);

            _logger.LogInformation(
                "Action {ActionCode} retrieved from Dataverse and cached (ID: {ActionId})",
                actionCode,
                action.Id);

            return action;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            _logger.LogError(
                "Action not found with code '{ActionCode}'. " +
                "Verify alternate key exists and field sprk_actioncode is populated.",
                actionCode);

            throw new InvalidOperationException(
                $"Action with code '{actionCode}' not found. " +
                $"Verify alternate key configuration and data integrity.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                exception: ex,
                message: "Failed to retrieve action by code '{ActionCode}'",
                actionCode);

            throw new InvalidOperationException(
                $"Failed to retrieve action with code '{actionCode}': {ex.Message}", ex);
        }
    }

    public void ClearCache(string actionCode)
    {
        if (string.IsNullOrWhiteSpace(actionCode))
        {
            throw new ArgumentException("Action code cannot be null or empty", nameof(actionCode));
        }

        var cacheKey = GetCacheKey(actionCode);
        _memoryCache.Remove(cacheKey);

        _logger.LogInformation(
            "Cleared cache for action {ActionCode}",
            actionCode);
    }

    public void ClearAllCache()
    {
        // Note: IMemoryCache doesn't provide a built-in "clear all" method.
        // This implementation relies on cache expiration.
        // For immediate cache clear, would need to track cache keys separately.

        _logger.LogWarning(
            "ClearAllCache called, but IMemoryCache has no clear-all API. " +
            "Cache will expire naturally after {CacheDuration}. " +
            "For immediate clear, restart application or use ClearCache(code) per action.",
            CacheDuration);
    }

    private static string GetCacheKey(string actionCode)
    {
        return $"{CacheKeyPrefix}{actionCode.ToUpperInvariant()}";
    }

    private ActionResponse MapEntityToActionResponse(Entity entity)
    {
        var id = entity.GetAttributeValue<Guid>("sprk_analysisactionid");
        var name = entity.GetAttributeValue<string>("sprk_name") ?? string.Empty;
        var description = entity.GetAttributeValue<string>("sprk_description") ?? string.Empty;
        var actionCode = entity.GetAttributeValue<string>("sprk_actioncode") ?? string.Empty;
        var stateCode = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0;
        var statusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1;

        return new ActionResponse
        {
            Id = id,
            Name = name,
            Description = description,
            ActionCode = actionCode,
            IsActive = stateCode == 0, // 0 = Active, 1 = Inactive
            StatusCode = statusCode
        };
    }
}
