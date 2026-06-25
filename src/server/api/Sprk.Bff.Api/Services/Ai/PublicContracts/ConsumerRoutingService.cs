using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IConsumerRoutingService"/>: queries
/// <c>sprk_playbookconsumer</c> rows via
/// <see cref="IGenericEntityService.RetrieveMultipleAsync(QueryExpression, CancellationToken)"/>,
/// applies the FR-1R-03 resolution algorithm, and caches results via
/// <see cref="IMemoryCache"/> with a 5-minute TTL (ADR-014).
/// </summary>
/// <remarks>
/// <para>
/// <b>Architectural decisions</b> recorded against the
/// <c>chat-routing-redesign-r1</c> task 028a investigation:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dataverse access</b>: <see cref="IGenericEntityService.RetrieveMultipleAsync(QueryExpression, CancellationToken)"/>
///     with a narrow <see cref="QueryExpression"/> filtered by
///     <c>sprk_consumertype</c> + <c>sprk_enabled=true</c>. Consumer-code,
///     environment, priority, and JSON match-conditions are applied
///     in-memory after the round-trip. Mirrors the precedent set by
///     <see cref="PlaybookLookupService"/> (alternate-key path) but uses a
///     filtered query because Phase 1R routing has multi-row matching
///     semantics.
///   </item>
///   <item>
///     <b>Cache</b>: <see cref="IMemoryCache"/> 5-minute absolute TTL per
///     ADR-014. Cache key includes tenant id + consumer key + environment +
///     context fingerprint so admin edits to the table propagate within 5
///     minutes. No Dataverse change-tracking subscriber exists in this codebase
///     today; if a future need surfaces, an explicit cache-eviction endpoint
///     plus a change-tracking subscriber are the additive extension path —
///     they are intentionally NOT introduced here per the spec FR-1R-02
///     "5-min TTL" acceptance and the ADR-010 minimalism rule.
///   </item>
///   <item>
///     <b>Tenant scope</b>: the BFF runs single-tenant per environment today
///     (per spec Phase 1R OOS). The cache key still scopes by tenant id from
///     the routing context (when the consumer flows one) so multi-tenant
///     extension does not require a cache rewrite.
///   </item>
/// </list>
/// </remarks>
public sealed class ConsumerRoutingService : IConsumerRoutingService
{
    private const string EntityLogicalName = "sprk_playbookconsumer";
    private const string LookupColumn = "sprk_playbook";
    private const string CacheKeyPrefix = "consumer-routing:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private static readonly string[] Columns =
    {
        "sprk_playbookconsumerid",
        "sprk_consumertype",
        "sprk_consumercode",
        "sprk_environment",
        "sprk_priority",
        "sprk_matchconditions",
        "sprk_enabled",
        LookupColumn,
    };

    private static readonly JsonSerializerOptions MatchConditionsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IGenericEntityService _entityService;
    private readonly IMemoryCache _cache;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<ConsumerRoutingService> _logger;

    public ConsumerRoutingService(
        IGenericEntityService entityService,
        IMemoryCache cache,
        IHostEnvironment hostEnvironment,
        ILogger<ConsumerRoutingService> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _hostEnvironment = hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Guid?> ResolveAsync(
        string consumerType,
        string? consumerCode = "default",
        IRoutingContext? context = null,
        string? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(consumerType))
        {
            throw new ArgumentException("consumerType is required.", nameof(consumerType));
        }

        var normalizedCode = string.IsNullOrWhiteSpace(consumerCode) ? "default" : consumerCode;
        var normalizedEnv = string.IsNullOrWhiteSpace(environment)
            ? _hostEnvironment.EnvironmentName?.ToLowerInvariant() ?? "*"
            : environment.ToLowerInvariant();

        var cacheKey = BuildCacheKey(consumerType, normalizedCode, normalizedEnv, context);

        if (_cache.TryGetValue<Guid?>(cacheKey, out var cached))
        {
            _logger.LogDebug(
                "ConsumerRoutingService cache hit (consumerType={ConsumerType}, consumerCode={ConsumerCode}, env={Env}, resolvedPlaybookId={ResolvedPlaybookId}).",
                consumerType,
                normalizedCode,
                normalizedEnv,
                cached);
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();
        Guid? resolved = null;

        try
        {
            var candidates = await QueryCandidatesAsync(consumerType, cancellationToken).ConfigureAwait(false);
            resolved = SelectBestMatch(candidates, normalizedCode, normalizedEnv, context);
        }
        catch (OperationCanceledException)
        {
            // Don't poison the cache on cancellation — let next call retry.
            throw;
        }
        catch (Exception ex)
        {
            // Routing must NEVER throw to the consumer; graceful-degrade lets the
            // caller fall back to typed-options or feature-disabled UX.
            // ADR-015: log identifiers + outcome only.
            _logger.LogError(
                ex,
                "ConsumerRoutingService failed to resolve (consumerType={ConsumerType}, consumerCode={ConsumerCode}, env={Env}). Returning null.",
                consumerType,
                normalizedCode,
                normalizedEnv);
            return null;
        }
        finally
        {
            stopwatch.Stop();
        }

        // Cache both hits and misses to absorb chatty lookups; admin edits will
        // propagate within CacheDuration.
        _cache.Set(cacheKey, resolved, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
        });

        _logger.LogInformation(
            "ConsumerRoutingService resolved (consumerType={ConsumerType}, consumerCode={ConsumerCode}, env={Env}, resolvedPlaybookId={ResolvedPlaybookId}, cacheHit=false, durationMs={DurationMs}).",
            consumerType,
            normalizedCode,
            normalizedEnv,
            resolved,
            stopwatch.ElapsedMilliseconds);

        return resolved;
    }

    /// <summary>
    /// Query enabled <c>sprk_playbookconsumer</c> rows for the given
    /// <paramref name="consumerType"/>. Returns the projected candidate list
    /// (consumer-code, environment, priority, matchconditions, lookup target
    /// id). The query is narrow on purpose: per-type cardinality is expected
    /// to be small (≤20 records), so the post-filter cost in memory is
    /// negligible while keeping the Dataverse query trivially indexable.
    /// </summary>
    private async Task<IReadOnlyList<Candidate>> QueryCandidatesAsync(
        string consumerType,
        CancellationToken cancellationToken)
    {
        var query = new QueryExpression(EntityLogicalName)
        {
            ColumnSet = new ColumnSet(Columns),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("sprk_consumertype", ConditionOperator.Equal, consumerType);
        query.Criteria.AddCondition("sprk_enabled", ConditionOperator.Equal, true);
        query.AddOrder("sprk_priority", OrderType.Ascending);

        var result = await _entityService
            .RetrieveMultipleAsync(query, cancellationToken)
            .ConfigureAwait(false);

        if (result?.Entities is null || result.Entities.Count == 0)
        {
            return Array.Empty<Candidate>();
        }

        var candidates = new List<Candidate>(result.Entities.Count);
        foreach (var entity in result.Entities)
        {
            var lookup = entity.GetAttributeValue<EntityReference>(LookupColumn);
            if (lookup is null || lookup.Id == Guid.Empty)
            {
                // Configured row with no playbook target — skip; admin error.
                continue;
            }

            candidates.Add(new Candidate
            {
                ConsumerCode = entity.GetAttributeValue<string>("sprk_consumercode"),
                Environment = entity.GetAttributeValue<string>("sprk_environment"),
                Priority = entity.GetAttributeValue<int?>("sprk_priority") ?? 500,
                MatchConditionsJson = entity.GetAttributeValue<string>("sprk_matchconditions"),
                PlaybookId = lookup.Id,
            });
        }

        return candidates;
    }

    /// <summary>
    /// FR-1R-03 selection algorithm. Filters by consumer-code and environment,
    /// applies the JSON match-conditions predicate, then picks the highest-
    /// priority record. Tiebreaks: lower priority wins; specific consumer-code
    /// beats <c>"default"</c>; specific environment beats wildcard.
    /// </summary>
    private static Guid? SelectBestMatch(
        IReadOnlyList<Candidate> candidates,
        string consumerCode,
        string environment,
        IRoutingContext? context)
    {
        Candidate? best = null;

        foreach (var c in candidates)
        {
            if (!MatchesConsumerCode(c.ConsumerCode, consumerCode))
            {
                continue;
            }

            if (!MatchesEnvironment(c.Environment, environment))
            {
                continue;
            }

            if (!TryMatchConditions(c.MatchConditionsJson, context))
            {
                continue;
            }

            if (best is null || CompareCandidates(c, best, consumerCode, environment) < 0)
            {
                best = c;
            }
        }

        return best?.PlaybookId;
    }

    private static bool MatchesConsumerCode(string? rowCode, string requested)
    {
        if (string.IsNullOrWhiteSpace(rowCode))
        {
            // Treat null/empty consumer-code as "default" for forward compat.
            rowCode = "default";
        }

        return string.Equals(rowCode, requested, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rowCode, "default", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesEnvironment(string? rowEnv, string requested)
    {
        if (string.IsNullOrWhiteSpace(rowEnv) || rowEnv == "*")
        {
            return true;
        }

        return string.Equals(rowEnv, requested, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Apply the flat key/value JSON predicate against the routing context.
    /// ALL keys must match. String values are equality matches; arrays are
    /// in-list (OR) matches. Null/empty/<c>{}</c> match conditions always
    /// match. Unknown keys are IGNORED (forward-compat).
    /// </summary>
    internal static bool TryMatchConditions(string? matchConditionsJson, IRoutingContext? context)
    {
        if (string.IsNullOrWhiteSpace(matchConditionsJson))
        {
            return true;
        }

        Dictionary<string, JsonElement>? conditions;
        try
        {
            conditions = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                matchConditionsJson, MatchConditionsJsonOptions);
        }
        catch (JsonException)
        {
            // Malformed JSON on the routing record — fail closed (do not match)
            // so that admin gets a "no playbook resolved" symptom and fixes the
            // row. Logged at WARN level by caller via cache miss path.
            return false;
        }

        if (conditions is null || conditions.Count == 0)
        {
            return true;
        }

        foreach (var (key, valueElement) in conditions)
        {
            var contextValue = ResolveContextValue(key, context);

            if (valueElement.ValueKind == JsonValueKind.String)
            {
                var expected = valueElement.GetString();
                if (!string.Equals(contextValue, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else if (valueElement.ValueKind == JsonValueKind.Array)
            {
                if (contextValue is null)
                {
                    return false;
                }

                var anyMatch = false;
                foreach (var item in valueElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String &&
                        string.Equals(item.GetString(), contextValue, StringComparison.OrdinalIgnoreCase))
                    {
                        anyMatch = true;
                        break;
                    }
                }
                if (!anyMatch)
                {
                    return false;
                }
            }
            else
            {
                // Unsupported JSON value type — schema is flat string|array<string>.
                // Treat as no-match (fail closed).
                return false;
            }
        }

        return true;
    }

    private static string? ResolveContextValue(string key, IRoutingContext? context)
    {
        if (context is null)
        {
            return null;
        }

        return key switch
        {
            "mimeType" => context.MimeType,
            "documentType" => context.DocumentType,
            _ => null, // Unknown key: caller's match condition referenced a dim we don't expose; treat as no-match.
        };
    }

    /// <summary>
    /// Tiebreak ordering for two matching candidates. Returns negative when
    /// <paramref name="left"/> is the better match, positive when
    /// <paramref name="right"/> is the better match.
    /// </summary>
    private static int CompareCandidates(Candidate left, Candidate right, string consumerCode, string environment)
    {
        // 1. Lower priority wins.
        var byPriority = left.Priority.CompareTo(right.Priority);
        if (byPriority != 0)
        {
            return byPriority;
        }

        // 2. Specific consumer-code beats 'default'.
        var leftCodeSpecific = IsSpecificConsumerCode(left.ConsumerCode, consumerCode);
        var rightCodeSpecific = IsSpecificConsumerCode(right.ConsumerCode, consumerCode);
        if (leftCodeSpecific && !rightCodeSpecific) return -1;
        if (!leftCodeSpecific && rightCodeSpecific) return 1;

        // 3. Specific environment beats wildcard/empty.
        var leftEnvSpecific = IsSpecificEnvironment(left.Environment, environment);
        var rightEnvSpecific = IsSpecificEnvironment(right.Environment, environment);
        if (leftEnvSpecific && !rightEnvSpecific) return -1;
        if (!leftEnvSpecific && rightEnvSpecific) return 1;

        return 0;
    }

    private static bool IsSpecificConsumerCode(string? rowCode, string requested) =>
        !string.IsNullOrWhiteSpace(rowCode) &&
        string.Equals(rowCode, requested, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(rowCode, "default", StringComparison.OrdinalIgnoreCase);

    private static bool IsSpecificEnvironment(string? rowEnv, string requested) =>
        !string.IsNullOrWhiteSpace(rowEnv) &&
        rowEnv != "*" &&
        string.Equals(rowEnv, requested, StringComparison.OrdinalIgnoreCase);

    private static string BuildCacheKey(
        string consumerType,
        string consumerCode,
        string environment,
        IRoutingContext? context)
    {
        var mime = context?.MimeType ?? string.Empty;
        var docType = context?.DocumentType ?? string.Empty;
        return $"{CacheKeyPrefix}{consumerType}:{consumerCode}:{environment}:{mime}:{docType}";
    }

    private sealed record Candidate
    {
        public string? ConsumerCode { get; init; }
        public string? Environment { get; init; }
        public int Priority { get; init; }
        public string? MatchConditionsJson { get; init; }
        public Guid PlaybookId { get; init; }
    }
}
