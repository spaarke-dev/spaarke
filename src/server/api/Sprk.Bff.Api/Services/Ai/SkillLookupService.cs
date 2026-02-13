using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Cached lookup service for AI skills using portable alternate keys.
/// Minimizes Dataverse queries in high-volume playbook execution scenarios.
/// </summary>
/// <remarks>
/// Performance characteristics:
/// - First lookup: ~50-100ms (Dataverse query + cache write)
/// - Cached lookups: &lt;1ms (in-memory)
/// - Cache TTL: 1 hour (skill configs rarely change)
/// - Memory usage: ~1KB per cached skill (negligible)
///
/// SaaS multi-environment support:
/// Same code works in DEV/QA/PROD without config changes.
/// Alternate keys travel with solution imports, GUIDs regenerate.
/// </remarks>
public class SkillLookupService : ISkillLookupService
{
    private readonly IDataverseService _dataverseService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<SkillLookupService> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private const string CacheKeyPrefix = "skill:code:";

    public SkillLookupService(
        IDataverseService dataverseService,
        IMemoryCache memoryCache,
        ILogger<SkillLookupService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SkillResponse> GetByCodeAsync(string skillCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(skillCode))
        {
            throw new ArgumentException("Skill code cannot be null or empty", nameof(skillCode));
        }

        var cacheKey = GetCacheKey(skillCode);

        // Try cache first
        if (_memoryCache.TryGetValue<SkillResponse>(cacheKey, out var cachedSkill))
        {
            _logger.LogDebug(
                "Skill {SkillCode} retrieved from cache (ID: {SkillId})",
                skillCode,
                cachedSkill?.Id);

            return cachedSkill!;
        }

        // Cache miss - query Dataverse using alternate key
        _logger.LogInformation(
            "Skill {SkillCode} not in cache, querying Dataverse by alternate key",
            skillCode);

        try
        {
            // Build alternate key lookup
            var alternateKeyValues = new KeyAttributeCollection
            {
                { "sprk_skillcode", skillCode }
            };

            // Columns needed to build SkillResponse
            var columns = new[]
            {
                "sprk_analysisskillid",
                "sprk_name",
                "sprk_description",
                "sprk_skillcode",
                "statecode",
                "statuscode"
            };

            // Retrieve using alternate key (indexed, fast)
            var entity = await _dataverseService.RetrieveByAlternateKeyAsync(
                "sprk_analysisskill",
                alternateKeyValues,
                columns,
                ct);

            // Map to SkillResponse
            var skill = MapEntityToSkillResponse(entity);

            // Cache for 1 hour
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(CacheDuration)
                .SetSize(1); // Each skill = 1 size unit (~1KB)

            _memoryCache.Set(cacheKey, skill, cacheOptions);

            _logger.LogInformation(
                "Skill {SkillCode} retrieved from Dataverse and cached (ID: {SkillId})",
                skillCode,
                skill.Id);

            return skill;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            _logger.LogError(
                "Skill not found with code '{SkillCode}'. " +
                "Verify alternate key exists and field sprk_skillcode is populated.",
                skillCode);

            throw new InvalidOperationException(
                $"Skill with code '{skillCode}' not found. " +
                $"Verify alternate key configuration and data integrity.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                exception: ex,
                message: "Failed to retrieve skill by code '{SkillCode}'",
                skillCode);

            throw new InvalidOperationException(
                $"Failed to retrieve skill with code '{skillCode}': {ex.Message}", ex);
        }
    }

    public void ClearCache(string skillCode)
    {
        if (string.IsNullOrWhiteSpace(skillCode))
        {
            throw new ArgumentException("Skill code cannot be null or empty", nameof(skillCode));
        }

        var cacheKey = GetCacheKey(skillCode);
        _memoryCache.Remove(cacheKey);

        _logger.LogInformation(
            "Cleared cache for skill {SkillCode}",
            skillCode);
    }

    public void ClearAllCache()
    {
        // Note: IMemoryCache doesn't provide a built-in "clear all" method.
        // This implementation relies on cache expiration.
        // For immediate cache clear, would need to track cache keys separately.

        _logger.LogWarning(
            "ClearAllCache called, but IMemoryCache has no clear-all API. " +
            "Cache will expire naturally after {CacheDuration}. " +
            "For immediate clear, restart application or use ClearCache(code) per skill.",
            CacheDuration);
    }

    private static string GetCacheKey(string skillCode)
    {
        return $"{CacheKeyPrefix}{skillCode.ToUpperInvariant()}";
    }

    private SkillResponse MapEntityToSkillResponse(Entity entity)
    {
        var id = entity.GetAttributeValue<Guid>("sprk_analysisskillid");
        var name = entity.GetAttributeValue<string>("sprk_name") ?? string.Empty;
        var description = entity.GetAttributeValue<string>("sprk_description") ?? string.Empty;
        var skillCode = entity.GetAttributeValue<string>("sprk_skillcode") ?? string.Empty;
        var stateCode = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0;
        var statusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1;

        return new SkillResponse
        {
            Id = id,
            Name = name,
            Description = description,
            SkillCode = skillCode,
            IsActive = stateCode == 0, // 0 = Active, 1 = Inactive
            StatusCode = statusCode
        };
    }
}
