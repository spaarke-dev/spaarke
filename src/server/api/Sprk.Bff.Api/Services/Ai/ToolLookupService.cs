using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Cached lookup service for AI tools using portable alternate keys.
/// Minimizes Dataverse queries in high-volume playbook execution scenarios.
/// </summary>
/// <remarks>
/// Performance characteristics:
/// - First lookup: ~50-100ms (Dataverse query + cache write)
/// - Cached lookups: &lt;1ms (in-memory)
/// - Cache TTL: 1 hour (tool configs rarely change)
/// - Memory usage: ~1KB per cached tool (negligible)
///
/// SaaS multi-environment support:
/// Same code works in DEV/QA/PROD without config changes.
/// Alternate keys travel with solution imports, GUIDs regenerate.
/// </remarks>
public class ToolLookupService : IToolLookupService
{
    private readonly IDataverseService _dataverseService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<ToolLookupService> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private const string CacheKeyPrefix = "tool:code:";

    public ToolLookupService(
        IDataverseService dataverseService,
        IMemoryCache memoryCache,
        ILogger<ToolLookupService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ToolResponse> GetByCodeAsync(string toolCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toolCode))
        {
            throw new ArgumentException("Tool code cannot be null or empty", nameof(toolCode));
        }

        var cacheKey = GetCacheKey(toolCode);

        // Try cache first
        if (_memoryCache.TryGetValue<ToolResponse>(cacheKey, out var cachedTool))
        {
            _logger.LogDebug(
                "Tool {ToolCode} retrieved from cache (ID: {ToolId})",
                toolCode,
                cachedTool?.Id);

            return cachedTool!;
        }

        // Cache miss - query Dataverse using alternate key
        _logger.LogInformation(
            "Tool {ToolCode} not in cache, querying Dataverse by alternate key",
            toolCode);

        try
        {
            // Build alternate key lookup
            var alternateKeyValues = new KeyAttributeCollection
            {
                { "sprk_toolcode", toolCode }
            };

            // Columns needed to build ToolResponse
            var columns = new[]
            {
                "sprk_analysistoolid",
                "sprk_name",
                "sprk_description",
                "sprk_toolcode",
                "statecode",
                "statuscode"
            };

            // Retrieve using alternate key (indexed, fast)
            var entity = await _dataverseService.RetrieveByAlternateKeyAsync(
                "sprk_analysistool",
                alternateKeyValues,
                columns,
                ct);

            // Map to ToolResponse
            var tool = MapEntityToToolResponse(entity);

            // Cache for 1 hour
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(CacheDuration)
                .SetSize(1); // Each tool = 1 size unit (~1KB)

            _memoryCache.Set(cacheKey, tool, cacheOptions);

            _logger.LogInformation(
                "Tool {ToolCode} retrieved from Dataverse and cached (ID: {ToolId})",
                toolCode,
                tool.Id);

            return tool;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            _logger.LogError(
                "Tool not found with code '{ToolCode}'. " +
                "Verify alternate key exists and field sprk_toolcode is populated.",
                toolCode);

            throw new InvalidOperationException(
                $"Tool with code '{toolCode}' not found. " +
                $"Verify alternate key configuration and data integrity.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                exception: ex,
                message: "Failed to retrieve tool by code '{ToolCode}'",
                toolCode);

            throw new InvalidOperationException(
                $"Failed to retrieve tool with code '{toolCode}': {ex.Message}", ex);
        }
    }

    public void ClearCache(string toolCode)
    {
        if (string.IsNullOrWhiteSpace(toolCode))
        {
            throw new ArgumentException("Tool code cannot be null or empty", nameof(toolCode));
        }

        var cacheKey = GetCacheKey(toolCode);
        _memoryCache.Remove(cacheKey);

        _logger.LogInformation(
            "Cleared cache for tool {ToolCode}",
            toolCode);
    }

    public void ClearAllCache()
    {
        // Note: IMemoryCache doesn't provide a built-in "clear all" method.
        // This implementation relies on cache expiration.
        // For immediate cache clear, would need to track cache keys separately.

        _logger.LogWarning(
            "ClearAllCache called, but IMemoryCache has no clear-all API. " +
            "Cache will expire naturally after {CacheDuration}. " +
            "For immediate clear, restart application or use ClearCache(code) per tool.",
            CacheDuration);
    }

    private static string GetCacheKey(string toolCode)
    {
        return $"{CacheKeyPrefix}{toolCode.ToUpperInvariant()}";
    }

    private ToolResponse MapEntityToToolResponse(Entity entity)
    {
        var id = entity.GetAttributeValue<Guid>("sprk_analysistoolid");
        var name = entity.GetAttributeValue<string>("sprk_name") ?? string.Empty;
        var description = entity.GetAttributeValue<string>("sprk_description") ?? string.Empty;
        var toolCode = entity.GetAttributeValue<string>("sprk_toolcode") ?? string.Empty;
        var stateCode = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0;
        var statusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1;

        return new ToolResponse
        {
            Id = id,
            Name = name,
            Description = description,
            ToolCode = toolCode,
            IsActive = stateCode == 0, // 0 = Active, 1 = Inactive
            StatusCode = statusCode
        };
    }
}
