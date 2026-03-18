using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Builds a metadata-driven command catalog from three sources: system commands,
/// Dataverse playbooks, and scope capabilities — with tenant-scoped Redis caching.
///
/// <b>Factory-instantiated</b> — NOT registered in DI (ADR-010: DI budget at 16).
/// Created by <see cref="SprkChatAgentFactory.CreateCommandResolverAsync"/> with resolved
/// dependencies from the scoped service provider.
///
/// <b>Command sources</b> (no static relationship tables — FR-17):
///   1. <b>System</b>: hardcoded base commands (/help, /clear, /export) — always present.
///   2. <b>Playbook</b>: <c>sprk_analysisplaybook</c> records filtered by
///      <c>sprk_recordtype = entityType</c>. Each playbook's <c>sprk_triggerphrases</c>
///      (newline-delimited) provides the first phrase as a slash command trigger.
///   3. <b>Scope capability</b>: active scopes with <c>sprk_capabilities</c> multi-select
///      option set values, mapped to command entries via <see cref="CapabilityCommandMap"/>.
///
/// <b>Caching</b> (ADR-009, ADR-014):
///   - Key: <c>cmd-catalog:{tenantId}:{entityType}</c> (tenant-scoped, not user-scoped)
///   - TTL: 5 minutes (absolute)
///   - On cache miss: query Dataverse, build catalog, store in Redis
///   - On cache hit: return cached catalog directly
///
/// <b>NFR-03</b>: Full resolution must complete within 3 seconds on cache hit.
/// </summary>
public sealed class DynamicCommandResolver
{
    /// <summary>Cache key prefix for command catalog entries.</summary>
    internal const string CacheKeyPrefix = "cmd-catalog:";

    /// <summary>Absolute TTL for command catalog cache entries (ADR-009).</summary>
    internal static readonly TimeSpan CatalogCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>Maximum length for a generated trigger slug.</summary>
    private const int MaxTriggerLength = 50;

    private readonly IGenericEntityService _entityService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<DynamicCommandResolver> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Hardcoded system commands — always present regardless of entity type or tenant.
    /// </summary>
    internal static readonly IReadOnlyList<CommandEntry> SystemCommands =
    [
        new("help", "Help", "Show available commands and usage", "/help", "system", null),
        new("clear", "Clear", "Clear the current conversation", "/clear", "system", null),
        new("export", "Export", "Export the conversation to a document", "/export", "system", null)
    ];

    /// <summary>
    /// Static mapping from <c>sprk_capabilities</c> option set integer values to
    /// <see cref="CommandEntry"/> records for scope-contributed commands.
    ///
    /// Values sourced from the Dataverse global choice definition:
    ///   100000000 = search
    ///   100000001 = analyze
    ///   100000002 = web_search
    ///   100000003 = write_back
    ///   100000004 = summarize
    /// </summary>
    internal static readonly IReadOnlyDictionary<int, CommandEntry> CapabilityCommandMap =
        new Dictionary<int, CommandEntry>
        {
            [100000000] = new("search", "Search", "Search knowledge sources", "/search", "scope", null),
            [100000001] = new("analyze", "Analyze Document", "Analyze a document with AI", "/analyze", "scope", null),
            [100000002] = new("web-search", "Search the Web", "Search the web for information", "/web-search", "scope", null),
            [100000003] = new("write-back", "Write to Document", "Write AI content to document", "/write-back", "scope", null),
            [100000004] = new("summarize", "Summarize", "Summarize content", "/summarize", "scope", null),
        };

    public DynamicCommandResolver(
        IGenericEntityService entityService,
        IDistributedCache cache,
        ILogger<DynamicCommandResolver> logger)
    {
        _entityService = entityService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the full command catalog for the given host context.
    ///
    /// On cache hit (Redis, ADR-009), returns immediately (&lt;100ms target).
    /// On cache miss, queries Dataverse for playbooks and scopes, builds the catalog,
    /// caches it with a 5-minute TTL, and returns.
    ///
    /// The catalog is tenant-scoped, not user-scoped (ADR-014) — commands are based on
    /// configuration/metadata, not user permissions.
    /// </summary>
    /// <param name="tenantId">Tenant ID for cache key scoping (ADR-014).</param>
    /// <param name="hostContext">
    /// Optional host context with <c>EntityType</c> for filtering playbooks by record type.
    /// When null, only system commands and scope commands are included.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Ordered list of <see cref="CommandEntry"/> records: system first, then playbook, then scope.
    /// </returns>
    public async Task<IReadOnlyList<CommandEntry>> ResolveCommandsAsync(
        string tenantId,
        ChatHostContext? hostContext,
        CancellationToken cancellationToken = default)
    {
        var entityType = hostContext?.EntityType;
        var cacheKey = BuildCacheKey(tenantId, entityType);

        // Hot path: Redis cache check (ADR-009 — Redis first)
        try
        {
            var cachedJson = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (cachedJson is not null)
            {
                _logger.LogDebug(
                    "Cache HIT for command catalog (tenant={TenantId}, entityType={EntityType})",
                    tenantId, entityType ?? "(none)");

                var cached = JsonSerializer.Deserialize<List<CommandEntry>>(cachedJson, JsonOptions);
                if (cached is not null)
                {
                    return cached;
                }
            }
        }
        catch (Exception ex)
        {
            // Redis failure should not block command resolution — degrade gracefully
            _logger.LogWarning(ex,
                "Redis cache read failed for command catalog (tenant={TenantId}); falling through to Dataverse",
                tenantId);
        }

        // Cold path: build catalog from Dataverse metadata
        _logger.LogDebug(
            "Cache MISS for command catalog (tenant={TenantId}, entityType={EntityType}) — building from Dataverse",
            tenantId, entityType ?? "(none)");

        var catalog = await BuildCatalogAsync(tenantId, entityType, cancellationToken);

        // Store in Redis with 5-minute absolute TTL (ADR-009, ADR-014)
        try
        {
            var json = JsonSerializer.Serialize(catalog, JsonOptions);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CatalogCacheTtl
            };
            await _cache.SetStringAsync(cacheKey, json, options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Redis cache write failed for command catalog (tenant={TenantId}); result will not be cached",
                tenantId);
        }

        return catalog;
    }

    /// <summary>
    /// Builds the cache key for a command catalog lookup.
    /// Format: <c>cmd-catalog:{tenantId}:{entityType}</c>
    /// When entityType is null, uses "global" as the suffix.
    /// </summary>
    internal static string BuildCacheKey(string tenantId, string? entityType)
        => $"{CacheKeyPrefix}{tenantId}:{entityType ?? "global"}";

    // =========================================================================
    // Private: Catalog Assembly
    // =========================================================================

    /// <summary>
    /// Assembles the full command catalog from all three sources.
    ///
    /// Deduplication (FR-11): When a scope and a playbook contribute a command with the
    /// same <see cref="CommandEntry.Id"/>, the scope command wins. This ensures scope
    /// capabilities remain visible regardless of active playbook.
    /// Priority order: system > scope > playbook (scope overrides playbook for same ID).
    /// </summary>
    private async Task<List<CommandEntry>> BuildCatalogAsync(
        string tenantId,
        string? entityType,
        CancellationToken ct)
    {
        var catalog = new List<CommandEntry>();

        // 1. System commands — always present
        catalog.AddRange(SystemCommands);

        // 2. Playbook commands — filtered by entityType (if provided)
        List<CommandEntry> playbookCommands = [];
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            playbookCommands = await GetPlaybookCommandsAsync(entityType, ct);
        }

        // 3. Scope capability commands — from active scopes (independent of playbook per FR-11)
        var scopeCommands = await GetScopeCapabilityCommandsAsync(ct);

        // Deduplicate: scope commands win over playbook commands for same ID.
        // System commands are already in the catalog and always win (added first).
        var seenIds = new HashSet<string>(catalog.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);

        // Add scope commands first (higher priority than playbook)
        foreach (var cmd in scopeCommands)
        {
            if (seenIds.Add(cmd.Id))
            {
                catalog.Add(cmd);
            }
            else
            {
                _logger.LogDebug(
                    "Scope command '{CommandId}' deduplicated (already present from system commands)",
                    cmd.Id);
            }
        }

        // Add playbook commands (lower priority — skipped if scope already contributed same ID)
        foreach (var cmd in playbookCommands)
        {
            if (seenIds.Add(cmd.Id))
            {
                catalog.Add(cmd);
            }
            else
            {
                _logger.LogDebug(
                    "Playbook command '{CommandId}' deduplicated (scope or system already contributed this ID)",
                    cmd.Id);
            }
        }

        _logger.LogInformation(
            "Built command catalog: {Total} commands (system={System}, playbook={Playbook}, scope={Scope}) for tenant={TenantId}, entityType={EntityType}",
            catalog.Count,
            SystemCommands.Count,
            playbookCommands.Count,
            scopeCommands.Count,
            tenantId,
            entityType ?? "(none)");

        return catalog;
    }

    /// <summary>
    /// Queries <c>sprk_analysisplaybook</c> records filtered by <c>sprk_recordtype</c>
    /// matching the given entity type. Extracts the first trigger phrase from
    /// <c>sprk_triggerphrases</c> (newline-delimited) and converts it to a slash command.
    ///
    /// Only active playbooks (statecode = 0) are included.
    /// </summary>
    private async Task<List<CommandEntry>> GetPlaybookCommandsAsync(
        string entityType,
        CancellationToken ct)
    {
        var commands = new List<CommandEntry>();

        try
        {
            var query = new QueryExpression("sprk_analysisplaybook")
            {
                ColumnSet = new ColumnSet(
                    "sprk_name",
                    "sprk_description",
                    "sprk_triggerphrases",
                    "sprk_analysisplaybookid"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression("sprk_recordtype", ConditionOperator.Equal, entityType),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active only
                    }
                }
            };

            var results = await _entityService.RetrieveMultipleAsync(query, ct);

            foreach (var entity in results.Entities)
            {
                var name = entity.GetAttributeValue<string>("sprk_name");
                var description = entity.GetAttributeValue<string>("sprk_description");
                var triggerPhrases = entity.GetAttributeValue<string>("sprk_triggerphrases");
                var playbookId = entity.Id.ToString();

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                // Use first trigger phrase for the slash command; fall back to playbook name
                var primaryPhrase = GetFirstTriggerPhrase(triggerPhrases) ?? name;
                var slug = ToSlug(primaryPhrase);

                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                commands.Add(new CommandEntry(
                    Id: slug,
                    Label: name,
                    Description: description ?? $"Run {name} playbook",
                    Trigger: $"/{slug}",
                    Category: "playbook",
                    Source: playbookId));
            }

            _logger.LogDebug(
                "Found {Count} playbook commands for entityType={EntityType}",
                commands.Count, entityType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to query playbook commands for entityType={EntityType}; playbook commands will be empty",
                entityType);
        }

        return commands;
    }

    /// <summary>
    /// Queries active <c>sprk_scope</c> records for <c>sprk_capabilities</c> multi-select
    /// option set values and maps them to <see cref="CommandEntry"/> records via
    /// <see cref="CapabilityCommandMap"/>.
    ///
    /// Only active scopes (statecode = 0) are included. Duplicate capabilities across
    /// scopes are deduplicated (first occurrence wins).
    /// </summary>
    private async Task<List<CommandEntry>> GetScopeCapabilityCommandsAsync(CancellationToken ct)
    {
        var commands = new List<CommandEntry>();
        var seenCapabilities = new HashSet<int>();

        try
        {
            var query = new QueryExpression("sprk_scope")
            {
                ColumnSet = new ColumnSet("sprk_capabilities", "sprk_name"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active only
                    }
                }
            };

            var results = await _entityService.RetrieveMultipleAsync(query, ct);

            foreach (var entity in results.Entities)
            {
                var scopeName = entity.GetAttributeValue<string>("sprk_name");
                var scopeId = entity.Id.ToString();

                // sprk_capabilities is a multi-select choice field.
                // In the SDK it surfaces as an OptionSetValueCollection.
                // In some OData responses it can also arrive as a comma-delimited string.
                var capabilityValues = ExtractCapabilityValues(entity);

                foreach (var capValue in capabilityValues)
                {
                    if (seenCapabilities.Contains(capValue))
                    {
                        continue; // Deduplicate across scopes
                    }

                    seenCapabilities.Add(capValue);

                    if (CapabilityCommandMap.TryGetValue(capValue, out var template))
                    {
                        // Clone template with scope source attribution and scope-qualified
                        // category label: "{ScopeName} — {DefaultLabel}" (FR-11, ADR-015:
                        // uses only capability labels, not full scope configuration).
                        var categoryLabel = !string.IsNullOrWhiteSpace(scopeName)
                            ? $"{scopeName} \u2014 {template.Label}"
                            : "scope";

                        commands.Add(template with
                        {
                            Source = scopeId,
                            Category = categoryLabel
                        });
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Unknown capability value {CapabilityValue} on scope {ScopeName}; skipping",
                            capValue, scopeName);
                    }
                }
            }

            _logger.LogDebug("Found {Count} scope capability commands", commands.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to query scope capabilities; scope commands will be empty");
        }

        return commands;
    }

    // =========================================================================
    // Private: Helpers
    // =========================================================================

    /// <summary>
    /// Extracts the first non-empty trigger phrase from a newline-delimited string.
    /// Returns null if the input is null/empty or contains no non-empty lines.
    /// </summary>
    internal static string? GetFirstTriggerPhrase(string? triggerPhrases)
    {
        if (string.IsNullOrWhiteSpace(triggerPhrases))
        {
            return null;
        }

        var lines = triggerPhrases.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 0 ? lines[0] : null;
    }

    /// <summary>
    /// Converts a phrase to a URL-safe slug suitable for a slash command trigger.
    ///
    /// Transformation: lowercase → replace spaces with hyphens → strip non-alphanumeric
    /// characters except hyphens → collapse multiple hyphens → trim hyphens → cap at 50 chars.
    ///
    /// Example: "Send analysis by email" → "send-analysis-by-email"
    /// </summary>
    internal static string ToSlug(string phrase)
    {
        var slug = phrase.ToLowerInvariant().Trim();

        // Replace spaces with hyphens
        slug = slug.Replace(' ', '-');

        // Strip non-alphanumeric characters except hyphens
        slug = Regex.Replace(slug, @"[^a-z0-9\-]", string.Empty);

        // Collapse multiple consecutive hyphens
        slug = Regex.Replace(slug, @"-{2,}", "-");

        // Trim leading/trailing hyphens
        slug = slug.Trim('-');

        // Cap length
        if (slug.Length > MaxTriggerLength)
        {
            slug = slug[..MaxTriggerLength].TrimEnd('-');
        }

        return slug;
    }

    /// <summary>
    /// Extracts integer capability values from a <c>sprk_capabilities</c> attribute.
    ///
    /// Handles two runtime shapes:
    ///   - <see cref="OptionSetValueCollection"/> (SDK typed query)
    ///   - <c>string</c> (comma-delimited integers from OData JSON)
    /// </summary>
    private static IReadOnlyList<int> ExtractCapabilityValues(Entity entity)
    {
        if (!entity.Contains("sprk_capabilities"))
        {
            return [];
        }

        var raw = entity["sprk_capabilities"];

        // SDK typed: OptionSetValueCollection
        if (raw is OptionSetValueCollection collection)
        {
            return collection.Select(osv => osv.Value).ToList();
        }

        // OData fallback: comma-delimited string (e.g., "100000000,100000002,100000004")
        if (raw is string csvString && !string.IsNullOrWhiteSpace(csvString))
        {
            var values = new List<int>();
            foreach (var part in csvString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var intVal))
                {
                    values.Add(intVal);
                }
            }
            return values;
        }

        return [];
    }
}
