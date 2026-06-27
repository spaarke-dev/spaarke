using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Cached lookup service for playbooks using the stable-ID alternate key
/// (<c>sprk_playbookid</c>) per Q&amp;A 2026-06-22 Q1.
/// Minimizes Dataverse queries in high-volume scenarios (1000+ lookups/hour).
/// </summary>
/// <remarks>
/// Performance characteristics:
/// - First lookup: ~50-100ms (Dataverse query + cache write)
/// - Cached lookups: &lt;1ms (in-memory)
/// - Cache TTL: 1 hour (playbook configs rarely change)
/// - Memory usage: ~1KB per cached playbook (negligible)
///
/// SaaS multi-environment support:
/// Same code works in DEV/QA/PROD without config changes. The stable-ID alt-key
/// <c>sprk_playbookid</c> mirrors the row's <c>sprk_analysisplaybookid</c> PK and
/// is immutable across environments (admin-facing slug <c>sprk_playbookcode</c> is
/// NOT used by code per Q&amp;A 2026-06-22 Q1).
/// </remarks>
public class PlaybookLookupService : IPlaybookLookupService
{
    private readonly IGenericEntityService _genericEntityService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<PlaybookLookupService> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private const string CacheKeyPrefix = "playbook:id:";

    public PlaybookLookupService(
        IGenericEntityService genericEntityService,
        IMemoryCache memoryCache,
        ILogger<PlaybookLookupService> logger)
    {
        _genericEntityService = genericEntityService ?? throw new ArgumentNullException(nameof(genericEntityService));
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PlaybookResponse> GetByIdAsync(string playbookId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(playbookId))
        {
            throw new ArgumentException("Playbook id cannot be null or empty", nameof(playbookId));
        }

        var cacheKey = GetCacheKey(playbookId);

        // Try cache first
        if (_memoryCache.TryGetValue<PlaybookResponse>(cacheKey, out var cachedPlaybook))
        {
            _logger.LogDebug(
                "Playbook {PlaybookId} retrieved from cache (rowId: {RowId})",
                playbookId,
                cachedPlaybook?.Id);

            return cachedPlaybook!;
        }

        // Cache miss - query Dataverse using alternate key
        _logger.LogInformation(
            "Playbook {PlaybookId} not in cache, querying Dataverse by alternate key",
            playbookId);

        try
        {
            // Build alternate key lookup on the stable-ID column per Q&A 2026-06-22 Q1.
            var alternateKeyValues = new KeyAttributeCollection
            {
                { "sprk_playbookid", playbookId }
            };

            // Columns needed to build PlaybookResponse
            var columns = new[]
            {
                "sprk_analysisplaybookid",
                "sprk_name",
                "sprk_description",
                "sprk_configjson",
                "sprk_playbookcode",
                "sprk_playbookid",
                "statecode",
                "statuscode"
            };

            // Retrieve using alternate key (indexed, fast)
            var entity = await _genericEntityService.RetrieveByAlternateKeyAsync(
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
                "Playbook {PlaybookId} retrieved from Dataverse and cached (rowId: {RowId})",
                playbookId,
                playbook.Id);

            return playbook;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            _logger.LogError(
                "Playbook not found with id '{PlaybookId}'. " +
                "Verify alternate key exists and field sprk_playbookid is populated.",
                playbookId);

            throw new PlaybookNotFoundException(
                $"Playbook with id '{playbookId}' not found. " +
                $"Verify alternate key configuration and data integrity.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                exception: ex,
                message: "Failed to retrieve playbook by id '{PlaybookId}'",
                playbookId);

            throw new InvalidOperationException(
                $"Failed to retrieve playbook with id '{playbookId}': {ex.Message}", ex);
        }
    }

    public void ClearCache(string playbookId)
    {
        if (string.IsNullOrWhiteSpace(playbookId))
        {
            throw new ArgumentException("Playbook id cannot be null or empty", nameof(playbookId));
        }

        var cacheKey = GetCacheKey(playbookId);
        _memoryCache.Remove(cacheKey);

        _logger.LogInformation(
            "Cleared cache for playbook {PlaybookId}",
            playbookId);
    }

    public void ClearAllCache()
    {
        // Note: IMemoryCache doesn't provide a built-in "clear all" method.
        // This implementation relies on cache expiration.
        // For immediate cache clear, would need to track cache keys separately.

        _logger.LogWarning(
            "ClearAllCache called, but IMemoryCache has no clear-all API. " +
            "Cache will expire naturally after {CacheDuration}. " +
            "For immediate clear, restart application or use ClearCache(id) per playbook.",
            CacheDuration);
    }

    private static string GetCacheKey(string playbookId)
    {
        return $"{CacheKeyPrefix}{playbookId.ToUpperInvariant()}";
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
