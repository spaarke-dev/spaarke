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
///   <item>
///     <b>Full Binding contract (FR-P0-03, spaarke-ai-architecture-redesign-r1
///     task 004)</b>: the query projects every §6.2 Binding column plus a
///     left-outer link to <c>sprk_analysisaction</c> for the §6.1 execution
///     fields, and each row maps to a full <see cref="Binding"/> record.
///     <see cref="ResolveAsync"/> / <see cref="ResolveActionAsync"/> keep
///     their Guid contracts unchanged by extracting from the same resolved
///     Binding; <see cref="ResolveBindingAsync"/> exposes the whole contract.
///     Legacy rows (null new columns) map to documented safe defaults —
///     see <see cref="Binding"/>.
///   </item>
/// </list>
/// </remarks>
public sealed class ConsumerRoutingService : IConsumerRoutingService
{
    private const string EntityLogicalName = "sprk_playbookconsumer";
    private const string ActionEntityLogicalName = "sprk_analysisaction";
    private const string PlaybookLookupColumn = "sprk_playbook";
    private const string ActionLookupColumn = "sprk_action";
    private const string ActionLinkAlias = "action";
    private const string CacheKeyPrefix = "consumer-routing:";
    private const string ActionCacheKeyPrefix = "consumer-routing-action:";
    private const string BindingCacheKeyPrefix = "consumer-routing-binding:";
    private const string BindingByIdCacheKeyPrefix = "consumer-routing-binding-id:";
    private const string BindingByPlaybookCacheKeyPrefix = "consumer-routing-binding-playbook:";
    private const string EventBindingsCacheKeyPrefix = "consumer-routing-event:";
    private const string TextProjectableCacheKeyPrefix = "consumer-routing-text-projectable:";
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
        PlaybookLookupColumn,
        ActionLookupColumn,
        // FR-P0-03 full Binding contract columns (task 003 schema extension):
        "sprk_ucid",
        "sprk_tooldescription",
        "sprk_disposition",
        "sprk_chiptransitions",
        "sprk_risk",
        "sprk_capturemode",
        "sprk_oneventbindings",
        "sprk_surfaces",
        "sprk_modeltieroverride",
        // FR-H1 grounding predicate (task 003/044): host-context precondition read by the PreFilter.
        "sprk_requiresnoattachedrecord",
    };

    /// <summary>
    /// Action-side execution fields (canonical §6.1) projected through a
    /// left-outer link so pure-engine legacy rows (no <c>sprk_action</c>) still
    /// resolve. Aliased as <c>action.*</c> on the returned entities.
    /// </summary>
    private static readonly string[] ActionColumns =
    {
        "sprk_kind",
        "sprk_workflowclass",
        "sprk_inputschema",
        "sprk_modeltier",
    };

    private static readonly JsonSerializerOptions MatchConditionsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions BindingJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
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
        var binding = await ResolveBindingCoreAsync(
                consumerType, consumerCode, context, environment,
                targetKind: LookupTarget.Playbook,
                cancellationToken)
            .ConfigureAwait(false);
        return binding?.PlaybookId;
    }

    /// <inheritdoc />
    public async Task<Guid?> ResolveActionAsync(
        string consumerType,
        string? consumerCode = "default",
        IRoutingContext? context = null,
        string? environment = null,
        CancellationToken cancellationToken = default)
    {
        var binding = await ResolveBindingCoreAsync(
                consumerType, consumerCode, context, environment,
                targetKind: LookupTarget.Action,
                cancellationToken)
            .ConfigureAwait(false);
        return binding?.ActionId;
    }

    /// <inheritdoc />
    public Task<Binding?> ResolveBindingAsync(
        string consumerType,
        string? consumerCode = "default",
        IRoutingContext? context = null,
        string? environment = null,
        CancellationToken cancellationToken = default)
        => ResolveBindingCoreAsync(
            consumerType, consumerCode, context, environment,
            targetKind: LookupTarget.Binding,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Binding>> ResolveEventBindingsAsync(
        string eventName,
        string? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            throw new ArgumentException("eventName is required.", nameof(eventName));
        }

        var normalizedEnv = string.IsNullOrWhiteSpace(environment)
            ? _hostEnvironment.EnvironmentName?.ToLowerInvariant() ?? "*"
            : environment.ToLowerInvariant();
        var cacheKey = $"{EventBindingsCacheKeyPrefix}{eventName}:{normalizedEnv}";

        if (_cache.TryGetValue<IReadOnlyList<Binding>>(cacheKey, out var cached) && cached is not null)
        {
            _logger.LogDebug(
                "ConsumerRoutingService event-bindings cache hit (event={Event}, env={Env}, memberCount={MemberCount}).",
                eventName, normalizedEnv, cached.Count);
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<Binding> resolved;
        try
        {
            resolved = SelectEventMembers(
                await QueryEventCandidatesAsync(cancellationToken).ConfigureAwait(false),
                eventName,
                normalizedEnv);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Routing must NEVER throw to the consumer (same contract as the
            // per-consumer resolves) — the Event path graceful-degrades to "no rule".
            _logger.LogError(ex,
                "ConsumerRoutingService failed to resolve event bindings (event={Event}, env={Env}). Returning empty.",
                eventName, normalizedEnv);
            return Array.Empty<Binding>();
        }
        finally
        {
            stopwatch.Stop();
        }

        _cache.Set(cacheKey, resolved, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
        });

        _logger.LogInformation(
            "ConsumerRoutingService resolved event bindings (event={Event}, env={Env}, memberCount={MemberCount}, " +
            "memberBindingIds={MemberBindingIds}, cacheHit=false, durationMs={DurationMs}).",
            eventName, normalizedEnv, resolved.Count,
            string.Join(",", resolved.Select(b => b.BindingId)), stopwatch.ElapsedMilliseconds);

        return resolved;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Binding>> ListTextProjectableBindingsAsync(
        string? environment = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedEnv = string.IsNullOrWhiteSpace(environment)
            ? _hostEnvironment.EnvironmentName?.ToLowerInvariant() ?? "*"
            : environment.ToLowerInvariant();
        var cacheKey = $"{TextProjectableCacheKeyPrefix}{normalizedEnv}";

        if (_cache.TryGetValue<IReadOnlyList<Binding>>(cacheKey, out var cached) && cached is not null)
        {
            _logger.LogDebug(
                "ConsumerRoutingService text-projectable cache hit (env={Env}, count={Count}).",
                normalizedEnv, cached.Count);
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<Binding> resolved;
        try
        {
            resolved = SelectTextProjectable(
                await QueryTextProjectableCandidatesAsync(cancellationToken).ConfigureAwait(false),
                normalizedEnv);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Routing must NEVER throw to the consumer (same contract as the other
            // resolves) — the loop projection graceful-degrades to handler tools only.
            _logger.LogError(ex,
                "ConsumerRoutingService failed to list text-projectable bindings (env={Env}). Returning empty.",
                normalizedEnv);
            return Array.Empty<Binding>();
        }
        finally
        {
            stopwatch.Stop();
        }

        _cache.Set(cacheKey, resolved, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
        });

        _logger.LogInformation(
            "ConsumerRoutingService listed text-projectable bindings (env={Env}, count={Count}, " +
            "consumerTypes={ConsumerTypes}, cacheHit=false, durationMs={DurationMs}).",
            normalizedEnv, resolved.Count,
            string.Join(",", resolved.Select(b => b.ConsumerType)), stopwatch.ElapsedMilliseconds);

        return resolved;
    }

    /// <summary>
    /// Deterministic environment filter + ordering for the loop projection
    /// (FR-P2-01 / NFR-04): consumer type ordinal, then binding id — same inputs
    /// always produce the same ordered list. Exposed for direct testability.
    /// </summary>
    internal static IReadOnlyList<Binding> SelectTextProjectable(
        IReadOnlyList<Binding> candidates,
        string environment)
    {
        return candidates
            .Where(b => !string.IsNullOrWhiteSpace(b.ToolDescription)
                        && MatchesEnvironment(b.Environment, environment))
            .OrderBy(b => b.ConsumerType, StringComparer.Ordinal)
            .ThenBy(b => b.BindingId)
            .ToList();
    }

    /// <summary>
    /// Query every enabled row with a non-null <c>sprk_tooldescription</c>
    /// (the catalog opt-in to loop projection), with the same left-outer Action
    /// link + <see cref="MapBinding"/> mapping as every other resolve path.
    /// </summary>
    private async Task<IReadOnlyList<Binding>> QueryTextProjectableCandidatesAsync(
        CancellationToken cancellationToken)
    {
        var query = new QueryExpression(EntityLogicalName)
        {
            ColumnSet = new ColumnSet(Columns),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("sprk_enabled", ConditionOperator.Equal, true);
        query.Criteria.AddCondition("sprk_tooldescription", ConditionOperator.NotNull);
        query.AddOrder("sprk_priority", OrderType.Ascending);

        var actionLink = query.AddLink(
            ActionEntityLogicalName,
            ActionLookupColumn,
            "sprk_analysisactionid",
            JoinOperator.LeftOuter);
        actionLink.EntityAlias = ActionLinkAlias;
        actionLink.Columns = new ColumnSet(ActionColumns);

        var result = await _entityService
            .RetrieveMultipleAsync(query, cancellationToken)
            .ConfigureAwait(false);

        if (result?.Entities is null || result.Entities.Count == 0)
        {
            return Array.Empty<Binding>();
        }

        var candidates = new List<Binding>(result.Entities.Count);
        foreach (var entity in result.Entities)
        {
            var playbookLookup = entity.GetAttributeValue<EntityReference>(PlaybookLookupColumn);
            var actionLookup = entity.GetAttributeValue<EntityReference>(ActionLookupColumn);
            if ((playbookLookup is null || playbookLookup.Id == Guid.Empty)
                && (actionLookup is null || actionLookup.Id == Guid.Empty))
            {
                continue; // admin error — same skip rule as the other query paths.
            }

            candidates.Add(MapBinding(entity, playbookLookup, actionLookup));
        }

        return candidates;
    }

    /// <inheritdoc />
    public async Task<Binding?> GetBindingByIdAsync(
        Guid bindingId,
        CancellationToken cancellationToken = default)
    {
        if (bindingId == Guid.Empty)
        {
            throw new ArgumentException("bindingId is required.", nameof(bindingId));
        }

        var cacheKey = $"{BindingByIdCacheKeyPrefix}{bindingId}";
        if (_cache.TryGetValue<Binding?>(cacheKey, out var cached))
        {
            _logger.LogDebug(
                "ConsumerRoutingService binding-by-id cache hit (bindingId={BindingId}, resolved={Resolved}).",
                bindingId, cached is not null);
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();
        Binding? resolved = null;
        try
        {
            resolved = await QueryBindingByIdAsync(bindingId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Don't poison the cache on cancellation — let next call retry.
            throw;
        }
        catch (Exception ex)
        {
            // Routing must NEVER throw to the consumer (same contract as the other
            // resolves) — the Click path rejects the dispatch as "unknown binding"
            // (ADR-039: clean error, no fallback). ADR-015: identifiers only.
            _logger.LogError(ex,
                "ConsumerRoutingService failed to resolve binding by id (bindingId={BindingId}). Returning null.",
                bindingId);
            return null;
        }
        finally
        {
            stopwatch.Stop();
        }

        // Cache hits AND misses (same policy as the per-consumer resolves).
        _cache.Set(cacheKey, resolved, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
        });

        _logger.LogInformation(
            "ConsumerRoutingService resolved binding by id (bindingId={BindingId}, resolved={Resolved}, " +
            "consumerType={ConsumerType}, actionId={ActionId}, cacheHit=false, durationMs={DurationMs}).",
            bindingId, resolved is not null, resolved?.ConsumerType, resolved?.ActionId,
            stopwatch.ElapsedMilliseconds);

        return resolved;
    }

    /// <summary>
    /// Primary-key query for one enabled <c>sprk_playbookconsumer</c> row with the
    /// same left-outer Action link + <see cref="MapBinding"/> mapping as every other
    /// resolve path. Disabled rows and rows with neither target lookup populated
    /// (admin errors) return null.
    /// </summary>
    private async Task<Binding?> QueryBindingByIdAsync(Guid bindingId, CancellationToken cancellationToken)
    {
        var query = new QueryExpression(EntityLogicalName)
        {
            ColumnSet = new ColumnSet(Columns),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition("sprk_playbookconsumerid", ConditionOperator.Equal, bindingId);
        query.Criteria.AddCondition("sprk_enabled", ConditionOperator.Equal, true);

        var actionLink = query.AddLink(
            ActionEntityLogicalName,
            ActionLookupColumn,
            "sprk_analysisactionid",
            JoinOperator.LeftOuter);
        actionLink.EntityAlias = ActionLinkAlias;
        actionLink.Columns = new ColumnSet(ActionColumns);

        var result = await _entityService
            .RetrieveMultipleAsync(query, cancellationToken)
            .ConfigureAwait(false);

        var entity = result?.Entities is { Count: > 0 } ? result.Entities[0] : null;
        if (entity is null)
        {
            return null;
        }

        var playbookLookup = entity.GetAttributeValue<EntityReference>(PlaybookLookupColumn);
        var actionLookup = entity.GetAttributeValue<EntityReference>(ActionLookupColumn);
        if ((playbookLookup is null || playbookLookup.Id == Guid.Empty)
            && (actionLookup is null || actionLookup.Id == Guid.Empty))
        {
            return null; // admin error — same skip rule as QueryCandidatesAsync.
        }

        return MapBinding(entity, playbookLookup, actionLookup);
    }

    /// <inheritdoc />
    public async Task<Binding?> GetBindingByPlaybookIdAsync(
        Guid playbookId,
        string? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (playbookId == Guid.Empty)
        {
            throw new ArgumentException("playbookId is required.", nameof(playbookId));
        }

        var normalizedEnv = string.IsNullOrWhiteSpace(environment)
            ? _hostEnvironment.EnvironmentName?.ToLowerInvariant() ?? "*"
            : environment.ToLowerInvariant();
        var cacheKey = $"{BindingByPlaybookCacheKeyPrefix}{playbookId}:{normalizedEnv}";

        if (_cache.TryGetValue<Binding?>(cacheKey, out var cached))
        {
            _logger.LogDebug(
                "ConsumerRoutingService binding-by-playbook cache hit (playbookId={PlaybookId}, env={Env}, resolved={Resolved}).",
                playbookId, normalizedEnv, cached is not null);
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();
        Binding? resolved = null;
        try
        {
            resolved = await QueryBindingByPlaybookIdAsync(playbookId, normalizedEnv, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Don't poison the cache on cancellation — let next call retry.
            throw;
        }
        catch (Exception ex)
        {
            // Routing must NEVER throw to the consumer (same contract as the other
            // resolves) — reverse-lookup callers keep their documented fallbacks
            // (interim ledger identity / TTL not-found). ADR-015: identifiers only.
            _logger.LogError(ex,
                "ConsumerRoutingService failed to resolve binding by playbook id (playbookId={PlaybookId}, env={Env}). Returning null.",
                playbookId, normalizedEnv);
            return null;
        }
        finally
        {
            stopwatch.Stop();
        }

        // Cache hits AND misses (same policy as the per-consumer resolves).
        _cache.Set(cacheKey, resolved, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
        });

        _logger.LogInformation(
            "ConsumerRoutingService resolved binding by playbook id (playbookId={PlaybookId}, env={Env}, resolved={Resolved}, " +
            "bindingId={BindingId}, consumerType={ConsumerType}, cacheHit=false, durationMs={DurationMs}).",
            playbookId, normalizedEnv, resolved is not null, resolved?.BindingId, resolved?.ConsumerType,
            stopwatch.ElapsedMilliseconds);

        return resolved;
    }

    /// <summary>
    /// Reverse-lookup query: enabled rows whose <c>sprk_playbook</c> targets the
    /// playbook, with the same left-outer Action link + <see cref="MapBinding"/>
    /// mapping as every other resolve path. Environment filtering + deterministic
    /// best-match selection (specific env over wildcard, then priority, then row id)
    /// happen in memory — a playbook is targeted by at most a handful of rows.
    /// </summary>
    private async Task<Binding?> QueryBindingByPlaybookIdAsync(
        Guid playbookId,
        string normalizedEnv,
        CancellationToken cancellationToken)
    {
        var query = new QueryExpression(EntityLogicalName)
        {
            ColumnSet = new ColumnSet(Columns),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(PlaybookLookupColumn, ConditionOperator.Equal, playbookId);
        query.Criteria.AddCondition("sprk_enabled", ConditionOperator.Equal, true);
        query.AddOrder("sprk_priority", OrderType.Ascending);

        var actionLink = query.AddLink(
            ActionEntityLogicalName,
            ActionLookupColumn,
            "sprk_analysisactionid",
            JoinOperator.LeftOuter);
        actionLink.EntityAlias = ActionLinkAlias;
        actionLink.Columns = new ColumnSet(ActionColumns);

        var result = await _entityService
            .RetrieveMultipleAsync(query, cancellationToken)
            .ConfigureAwait(false);

        if (result?.Entities is null || result.Entities.Count == 0)
        {
            return null;
        }

        var candidates = new List<Binding>(result.Entities.Count);
        foreach (var entity in result.Entities)
        {
            var playbookLookup = entity.GetAttributeValue<EntityReference>(PlaybookLookupColumn);
            var actionLookup = entity.GetAttributeValue<EntityReference>(ActionLookupColumn);
            if (playbookLookup is null || playbookLookup.Id == Guid.Empty)
            {
                continue; // defensive — the query condition already requires the lookup.
            }
            candidates.Add(MapBinding(entity, playbookLookup, actionLookup));
        }

        return candidates
            .Where(b => MatchesEnvironment(b.Environment, normalizedEnv))
            .OrderByDescending(b => IsSpecificEnvironment(b.Environment, normalizedEnv))
            .ThenBy(b => b.Priority)
            .ThenBy(b => b.BindingId)
            .FirstOrDefault();
    }

    /// <summary>
    /// Query every enabled row that declares ANY event membership
    /// (<c>sprk_oneventbindings</c> not null), across all consumer types, with the
    /// same left-outer Action link + <see cref="MapBinding"/> mapping as the
    /// per-consumer path. Event-membership cardinality is tiny (a handful of rows
    /// platform-wide), so per-event filtering happens in memory.
    /// </summary>
    private async Task<IReadOnlyList<Binding>> QueryEventCandidatesAsync(CancellationToken cancellationToken)
    {
        var query = new QueryExpression(EntityLogicalName)
        {
            ColumnSet = new ColumnSet(Columns),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("sprk_enabled", ConditionOperator.Equal, true);
        query.Criteria.AddCondition("sprk_oneventbindings", ConditionOperator.NotNull);
        query.AddOrder("sprk_priority", OrderType.Ascending);

        var actionLink = query.AddLink(
            ActionEntityLogicalName,
            ActionLookupColumn,
            "sprk_analysisactionid",
            JoinOperator.LeftOuter);
        actionLink.EntityAlias = ActionLinkAlias;
        actionLink.Columns = new ColumnSet(ActionColumns);

        var result = await _entityService
            .RetrieveMultipleAsync(query, cancellationToken)
            .ConfigureAwait(false);

        if (result?.Entities is null || result.Entities.Count == 0)
        {
            return Array.Empty<Binding>();
        }

        var candidates = new List<Binding>(result.Entities.Count);
        foreach (var entity in result.Entities)
        {
            var playbookLookup = entity.GetAttributeValue<EntityReference>(PlaybookLookupColumn);
            var actionLookup = entity.GetAttributeValue<EntityReference>(ActionLookupColumn);
            if ((playbookLookup is null || playbookLookup.Id == Guid.Empty)
                && (actionLookup is null || actionLookup.Id == Guid.Empty))
            {
                continue; // admin error — same skip rule as QueryCandidatesAsync.
            }
            candidates.Add(MapBinding(entity, playbookLookup, actionLookup));
        }
        return candidates;
    }

    /// <summary>
    /// Filter candidates to members of <paramref name="eventName"/> in the given
    /// environment and order by the membership's declared <c>order</c> (ascending;
    /// ties: <c>sprk_priority</c> then BindingId for determinism). Event tokens
    /// compare ordinal-case-insensitively; rows whose JSON degraded to an empty
    /// membership list (malformed maker JSON) simply don't match.
    /// </summary>
    internal static IReadOnlyList<Binding> SelectEventMembers(
        IReadOnlyList<Binding> candidates,
        string eventName,
        string environment)
    {
        return candidates
            .Select(binding => (binding, membership: binding.OnEventBindings.FirstOrDefault(m =>
                string.Equals(m.Event, eventName, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.membership is not null && MatchesEnvironment(x.binding.Environment, environment))
            .OrderBy(x => x.membership!.Order)
            .ThenBy(x => x.binding.Priority)
            .ThenBy(x => x.binding.BindingId)
            .Select(x => x.binding)
            .ToList();
    }

    /// <summary>
    /// Shared implementation for all three resolve methods. Same query +
    /// selection algorithm; the <paramref name="targetKind"/> controls which
    /// rows qualify (playbook-target, action-target, or either) and namespaces
    /// the cache key. The resolved value cached is the full immutable
    /// <see cref="Binding"/> record (nulls — no-match — are cached too, to
    /// absorb chatty lookups).
    /// </summary>
    private async Task<Binding?> ResolveBindingCoreAsync(
        string consumerType,
        string? consumerCode,
        IRoutingContext? context,
        string? environment,
        LookupTarget targetKind,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(consumerType))
        {
            throw new ArgumentException("consumerType is required.", nameof(consumerType));
        }

        var normalizedCode = string.IsNullOrWhiteSpace(consumerCode) ? "default" : consumerCode;
        var normalizedEnv = string.IsNullOrWhiteSpace(environment)
            ? _hostEnvironment.EnvironmentName?.ToLowerInvariant() ?? "*"
            : environment.ToLowerInvariant();

        var cacheKey = BuildCacheKey(consumerType, normalizedCode, normalizedEnv, context, targetKind);

        if (_cache.TryGetValue<Binding?>(cacheKey, out var cached))
        {
            _logger.LogDebug(
                "ConsumerRoutingService cache hit (target={Target}, consumerType={ConsumerType}, consumerCode={ConsumerCode}, env={Env}, resolvedBindingId={ResolvedBindingId}).",
                targetKind,
                consumerType,
                normalizedCode,
                normalizedEnv,
                cached?.BindingId);
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();
        Binding? resolved = null;

        try
        {
            var candidates = await QueryCandidatesAsync(consumerType, cancellationToken).ConfigureAwait(false);
            resolved = SelectBestMatch(candidates, normalizedCode, normalizedEnv, context, targetKind);
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
                "ConsumerRoutingService failed to resolve (target={Target}, consumerType={ConsumerType}, consumerCode={ConsumerCode}, env={Env}). Returning null.",
                targetKind,
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
            "ConsumerRoutingService resolved (target={Target}, consumerType={ConsumerType}, consumerCode={ConsumerCode}, env={Env}, resolvedBindingId={ResolvedBindingId}, playbookId={PlaybookId}, actionId={ActionId}, cacheHit=false, durationMs={DurationMs}).",
            targetKind,
            consumerType,
            normalizedCode,
            normalizedEnv,
            resolved?.BindingId,
            resolved?.PlaybookId,
            resolved?.ActionId,
            stopwatch.ElapsedMilliseconds);

        return resolved;
    }

    /// <summary>
    /// Query enabled <c>sprk_playbookconsumer</c> rows for the given
    /// <paramref name="consumerType"/>, left-joined to the target
    /// <c>sprk_analysisaction</c> for the §6.1 execution fields, and map each
    /// row to the full <see cref="Binding"/> contract. The query is narrow on
    /// purpose: per-type cardinality is expected to be small (≤20 records), so
    /// the post-filter cost in memory is negligible while keeping the
    /// Dataverse query trivially indexable.
    /// </summary>
    private async Task<IReadOnlyList<Binding>> QueryCandidatesAsync(
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

        // Left-outer join so pure-engine legacy rows (sprk_action null) still
        // return; their Action-side fields map to safe defaults.
        var actionLink = query.AddLink(
            ActionEntityLogicalName,
            ActionLookupColumn,
            "sprk_analysisactionid",
            JoinOperator.LeftOuter);
        actionLink.EntityAlias = ActionLinkAlias;
        actionLink.Columns = new ColumnSet(ActionColumns);

        var result = await _entityService
            .RetrieveMultipleAsync(query, cancellationToken)
            .ConfigureAwait(false);

        if (result?.Entities is null || result.Entities.Count == 0)
        {
            return Array.Empty<Binding>();
        }

        var candidates = new List<Binding>(result.Entities.Count);
        foreach (var entity in result.Entities)
        {
            var playbookLookup = entity.GetAttributeValue<EntityReference>(PlaybookLookupColumn);
            var actionLookup = entity.GetAttributeValue<EntityReference>(ActionLookupColumn);

            // Rows with neither target populated are admin errors — skip. Callers
            // land on "no match" and can graceful-degrade or surface an error.
            if ((playbookLookup is null || playbookLookup.Id == Guid.Empty)
                && (actionLookup is null || actionLookup.Id == Guid.Empty))
            {
                continue;
            }

            candidates.Add(MapBinding(entity, playbookLookup, actionLookup));
        }

        return candidates;
    }

    /// <summary>
    /// Map one queried row to the full <see cref="Binding"/> contract.
    /// Legacy-row tolerance: every task-003 column may be null (pre-backfill
    /// rows) and maps to the documented safe default; unknown future choice
    /// values fall back the same way; malformed maker JSON degrades to empty
    /// lists. Mapping MUST NOT throw — routing never throws to the consumer.
    /// </summary>
    private static Binding MapBinding(
        Entity entity,
        EntityReference? playbookLookup,
        EntityReference? actionLookup)
    {
        return new Binding
        {
            BindingId = entity.GetAttributeValue<Guid>("sprk_playbookconsumerid"),
            ConsumerType = entity.GetAttributeValue<string>("sprk_consumertype") ?? string.Empty,
            ConsumerCode = entity.GetAttributeValue<string>("sprk_consumercode"),
            Environment = entity.GetAttributeValue<string>("sprk_environment"),
            Priority = entity.GetAttributeValue<int?>("sprk_priority") ?? 500,
            MatchConditionsJson = entity.GetAttributeValue<string>("sprk_matchconditions"),
            PlaybookId = playbookLookup is { Id: var p } && p != Guid.Empty ? p : null,
            ActionId = actionLookup is { Id: var a } && a != Guid.Empty ? a : null,

            // task-003 Binding columns (§6.2)
            Ucid = entity.GetAttributeValue<string>("sprk_ucid"),
            ToolDescription = entity.GetAttributeValue<string>("sprk_tooldescription"),
            Disposition = MapChoice(
                entity.GetAttributeValue<OptionSetValue>("sprk_disposition"),
                BindingDisposition.Informational),
            ChipTransitions = ParseJsonList<ChipTransition>(
                entity.GetAttributeValue<string>("sprk_chiptransitions")),
            Risk = MapChoice(
                entity.GetAttributeValue<OptionSetValue>("sprk_risk"),
                BindingRisk.None),
            CaptureMode = MapChoice(
                entity.GetAttributeValue<OptionSetValue>("sprk_capturemode"),
                BindingCaptureMode.LoopElicitation),
            OnEventBindings = ParseJsonList<OnEventBinding>(
                entity.GetAttributeValue<string>("sprk_oneventbindings")),
            Surfaces = ParseSurfaces(entity.GetAttributeValue<string>("sprk_surfaces")),
            ModelTierOverride = MapNullableChoice<AiModelTier>(
                entity.GetAttributeValue<OptionSetValue>("sprk_modeltieroverride")),
            // FR-H1 grounding predicate (task 044): BIT column; null/unset → false (offered regardless).
            RequiresNoAttachedRecord = entity.GetAttributeValue<bool>("sprk_requiresnoattachedrecord"),

            // Action-side execution fields (§6.1, aliased via the left-outer link)
            ActionKind = MapChoice(
                GetAliasedValue<OptionSetValue>(entity, "sprk_kind"),
                ActionKind.Prompted),
            WorkflowClass = GetAliasedValue<string>(entity, "sprk_workflowclass"),
            InputSchemaJson = GetAliasedValue<string>(entity, "sprk_inputschema"),
            ActionModelTier = MapNullableChoice<AiModelTier>(
                GetAliasedValue<OptionSetValue>(entity, "sprk_modeltier")),
        };
    }

    /// <summary>
    /// Read an aliased attribute projected by the <c>action</c> link entity
    /// (e.g. <c>action.sprk_kind</c>). Returns null when the link produced no
    /// row (legacy pure-engine binding) or the column is null.
    /// </summary>
    private static T? GetAliasedValue<T>(Entity entity, string attributeName) where T : class
        => entity.GetAttributeValue<AliasedValue>($"{ActionLinkAlias}.{attributeName}")?.Value as T;

    /// <summary>
    /// Map a Dataverse choice to its enum (enum values ARE the option-set
    /// values). Null or unknown (future) option values fall back to
    /// <paramref name="fallback"/> — legacy rows must resolve, never throw.
    /// </summary>
    private static TEnum MapChoice<TEnum>(OptionSetValue? value, TEnum fallback)
        where TEnum : struct, Enum
        => value is not null && Enum.IsDefined(typeof(TEnum), value.Value)
            ? (TEnum)Enum.ToObject(typeof(TEnum), value.Value)
            : fallback;

    /// <summary>
    /// Map an OPTIONAL Dataverse choice (null means "not set" is semantically
    /// meaningful — e.g. model tiers where null = use default). Unknown values
    /// also map to null (fail-safe).
    /// </summary>
    private static TEnum? MapNullableChoice<TEnum>(OptionSetValue? value)
        where TEnum : struct, Enum
        => value is not null && Enum.IsDefined(typeof(TEnum), value.Value)
            ? (TEnum)Enum.ToObject(typeof(TEnum), value.Value)
            : null;

    /// <summary>
    /// Deserialize a JSON-in-memo array column into a typed list. Null/empty
    /// and malformed JSON both degrade to an empty list — maker data-entry
    /// errors must not break routing (the row still resolves; the Binding just
    /// carries no chips / event memberships).
    /// </summary>
    private static IReadOnlyList<T> ParseJsonList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<T>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<T>>(json, BindingJsonOptions);
            return parsed is { Count: > 0 } ? parsed : Array.Empty<T>();
        }
        catch (JsonException)
        {
            return Array.Empty<T>();
        }
    }

    /// <summary>
    /// Parse the comma-separated <c>sprk_surfaces</c> tokens. Null/empty maps
    /// to an empty list, which means "offered on ALL surfaces" per the column
    /// dictionary.
    /// </summary>
    private static IReadOnlyList<string> ParseSurfaces(string? surfaces)
        => string.IsNullOrWhiteSpace(surfaces)
            ? Array.Empty<string>()
            : surfaces.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// FR-1R-03 selection algorithm. Filters by consumer-code and environment,
    /// applies the JSON match-conditions predicate, then picks the highest-
    /// priority record. Tiebreaks: lower priority wins; specific consumer-code
    /// beats <c>"default"</c>; specific environment beats wildcard.
    /// </summary>
    private static Binding? SelectBestMatch(
        IReadOnlyList<Binding> candidates,
        string consumerCode,
        string environment,
        IRoutingContext? context,
        LookupTarget targetKind)
    {
        Binding? best = null;

        foreach (var c in candidates)
        {
            // Skip candidates whose target lookup for this call kind is empty.
            // e.g., a row with only sprk_playbook populated is invisible to
            // ResolveActionAsync (LookupTarget.Action) callers. For
            // LookupTarget.Binding, EITHER target qualifies (rows with neither
            // were already skipped at mapping).
            var qualifies = targetKind switch
            {
                LookupTarget.Action => c.ActionId is not null,
                LookupTarget.Playbook => c.PlaybookId is not null,
                _ => true,
            };
            if (!qualifies)
            {
                continue;
            }

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

        return best;
    }

    /// <summary>Discriminates the three resolve methods' target semantics.</summary>
    private enum LookupTarget
    {
        Playbook,
        Action,
        Binding,
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
    private static int CompareCandidates(Binding left, Binding right, string consumerCode, string environment)
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
        IRoutingContext? context,
        LookupTarget targetKind)
    {
        var mime = context?.MimeType ?? string.Empty;
        var docType = context?.DocumentType ?? string.Empty;
        var prefix = targetKind switch
        {
            LookupTarget.Action => ActionCacheKeyPrefix,
            LookupTarget.Binding => BindingCacheKeyPrefix,
            _ => CacheKeyPrefix,
        };
        return $"{prefix}{consumerType}:{consumerCode}:{environment}:{mime}:{docType}";
    }
}
