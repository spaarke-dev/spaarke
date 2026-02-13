using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Cached lookup service for playbooks using portable alternate keys.
/// Minimizes Dataverse queries in high-volume scenarios (1000+ lookups/hour).
/// </summary>
/// <remarks>
/// Performance characteristics:
/// - First lookup: ~50-100ms (Dataverse query + cache write)
/// - Cached lookups: <1ms (in-memory)
/// - Cache TTL: 1 hour (playbook configs rarely change)
/// - Memory usage: ~1KB per cached playbook (negligible)
///
/// SaaS multi-environment support:
/// Same code works in DEV/QA/PROD without config changes.
/// Alternate keys travel with solution imports, GUIDs regenerate.
/// </remarks>
public class PlaybookLookupService : IPlaybookLookupService
{
    private readonly IDataverseService _dataverseService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<PlaybookLookupService> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private const string CacheKeyPrefix = "playbook:code:";

    public PlaybookLookupService(
        IDataverseService dataverseService,
        IMemoryCache memoryCache,
        ILogger<PlaybookLookupService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PlaybookResponse> GetByCodeAsync(string playbookCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(playbookCode))
        {
            throw new ArgumentException("Playbook code cannot be null or empty", nameof(playbookCode));
        }

        var cacheKey = GetCacheKey(playbookCode);

        // Try cache first
        if (_memoryCache.TryGetValue<PlaybookResponse>(cacheKey, out var cachedPlaybook))
        {
            _logger.LogDebug(
                "Playbook {PlaybookCode} retrieved from cache (ID: {PlaybookId})",
                playbookCode,
                cachedPlaybook?.Id);

            return cachedPlaybook!;
        }

        // Cache miss - query Dataverse using alternate key
        _logger.LogInformation(
            "Playbook {PlaybookCode} not in cache, querying Dataverse by alternate key",
            playbookCode);

        try
        {
            // Build alternate key lookup
            var alternateKeyValues = new KeyAttributeCollection
            {
                { "sprk_playbookcode", playbookCode }
            };

            // Columns needed to build PlaybookResponse
            var columns = new[]
            {
                "sprk_analysisplaybookid",
                "sprk_name",
                "sprk_description",
                "sprk_configjson",
                "sprk_playbookcode",
                "statecode",
                "statuscode"
            };

            // Retrieve using alternate key (indexed, fast)
            var entity = await _dataverseService.RetrieveByAlternateKeyAsync(
                "sprk_analysisplaybook",
                alternateKeyValues,
                columns,
                ct);

            // Map to PlaybookResponse
            var playbook = MapEntityToPlaybookResponse(entity);

            // Cache for 1 hour
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(CacheDuration)
                .SetSize(1); // Each playbook = 1 size unit (~1KB)

            _memoryCache.Set(cacheKey, playbook, cacheOptions);

            _logger.LogInformation(
                "Playbook {PlaybookCode} retrieved from Dataverse and cached (ID: {PlaybookId})",
                playbookCode,
                playbook.Id);

            return playbook;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            _logger.LogError(
                "Playbook not found with code '{PlaybookCode}'. " +
                "Verify alternate key exists and field sprk_playbookcode is populated.",
                playbookCode);

            throw new PlaybookNotFoundException(
                $"Playbook with code '{playbookCode}' not found. " +
                $"Verify alternate key configuration and data integrity.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                exception: ex,
                message: "Failed to retrieve playbook by code '{PlaybookCode}'",
                playbookCode);

            throw new InvalidOperationException(
                $"Failed to retrieve playbook with code '{playbookCode}': {ex.Message}", ex);
        }
    }

    public void ClearCache(string playbookCode)
    {
        if (string.IsNullOrWhiteSpace(playbookCode))
        {
            throw new ArgumentException("Playbook code cannot be null or empty", nameof(playbookCode));
        }

        var cacheKey = GetCacheKey(playbookCode);
        _memoryCache.Remove(cacheKey);

        _logger.LogInformation(
            "Cleared cache for playbook {PlaybookCode}",
            playbookCode);
    }

    public void ClearAllCache()
    {
        // Note: IMemoryCache doesn't provide a built-in "clear all" method.
        // This implementation relies on cache expiration.
        // For immediate cache clear, would need to track cache keys separately.

        _logger.LogWarning(
            "ClearAllCache called, but IMemoryCache has no clear-all API. " +
            "Cache will expire naturally after {CacheDuration}. " +
            "For immediate clear, restart application or use ClearCache(code) per playbook.",
            CacheDuration);
    }

    private static string GetCacheKey(string playbookCode)
    {
        return $"{CacheKeyPrefix}{playbookCode.ToUpperInvariant()}";
    }

    private PlaybookResponse MapEntityToPlaybookResponse(Entity entity)
    {
        var id = entity.GetAttributeValue<Guid>("sprk_analysisplaybookid");
        var name = entity.GetAttributeValue<string>("sprk_name") ?? string.Empty;
        var description = entity.GetAttributeValue<string>("sprk_description") ?? string.Empty;
        var configJson = entity.GetAttributeValue<string>("sprk_configjson") ?? "{}";
        var playbookCode = entity.GetAttributeValue<string>("sprk_playbookcode") ?? string.Empty;
        var stateCode = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0;
        var statusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1;

        return new PlaybookResponse
        {
            Id = id,
            Name = name,
            Description = description,
            ConfigJson = configJson,
            PlaybookCode = playbookCode,
            IsActive = stateCode == 0, // 0 = Active, 1 = Inactive
            StatusCode = statusCode,
            // Other fields would come from IPlaybookService.GetPlaybookAsync if needed
            CreatedOn = entity.GetAttributeValue<DateTime?>("createdon") ?? DateTime.UtcNow,
            ModifiedOn = entity.GetAttributeValue<DateTime?>("modifiedon") ?? DateTime.UtcNow,
            OwnerId = entity.GetAttributeValue<EntityReference>("ownerid")?.Id ?? Guid.Empty
        };
    }
}
