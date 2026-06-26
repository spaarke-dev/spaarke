// R3 Part 1 — User-Record Membership Resolution (orchestration implementation)
// Task 033 (2026-06-21): Implements IMembershipResolverService.
//
// Pipeline:
//   1. Cache lookup — key "membership:resolved:{systemUserId:D}:{entityType}:{optionsHash}"
//      TTL 5 min (FR-1A.8 Phase 1A; Phase 2 task 086 extends TTL + pub/sub invalidation).
//   2. Cache MISS:
//      a. IMembershipFieldDiscoveryService.DiscoverAsync(entityType) → descriptors
//      b. Filter descriptors by options.Roles + options.IdentityTypes (case-insensitive)
//      c. IIdentityNormalizationService.ResolveAsync(systemUserId) → PersonIdentity
//      d. Build a single FetchXml query against entityType — top-level <filter type="or">
//         with one <condition> per (descriptor, identity-value) pair. Use the
//         descriptor's IdentityType to select which PersonIdentity field(s) to bind
//         (e.g., SystemUser → SystemUserId; Team → each TeamId; Account → AccountId).
//      e. Execute via IGenericEntityService.RetrieveMultipleAsync(FetchExpression).
//      f. Materialize: dedupe ids, sort ascending, build byRole map by re-classifying
//         each result row against descriptors (a single Fetch returns the row's id +
//         all participating lookup fields; we read each descriptor's field and add
//         the row's id to that role's bucket if non-empty).
//      g. Apply paging: if matches > options.Limit, truncate ids[] and emit a
//         continuationToken (encoded skip + take using the deterministic sort).
//      h. Build MembershipResponse, cache it (5-min TTL), return.
//
// Phase 1D (includeRelated) is ACCEPTED-BUT-IGNORED here — task 054 implements
// transitive expansion as a downstream layer that calls this service per parent +
// merges results.
//
// Failure isolation:
//   - DiscoverAsync throws (entity not found) → propagate (caller's input is invalid).
//   - IdentityNormalizationService throws OnlyOperationCanceledException; other
//     failures yield empty fields per the task 031 contract.
//   - Fetch query failure throws; the caller sees a 500/ProblemDetails from the
//     endpoint layer (task 035). No retries here — IRequestSender resiliency is
//     a deeper concern (ADR-016).
//   - Cache read/write failures fail-open (warn + continue).
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1A.5 through
//            FR-1A.9; design.md Part 1 § "Endpoint contract"; ADR-009 (Redis 5-min
//            TTL Phase 1A); ADR-010 (interface as testing seam); ADR-013
//            (lives under Services/Ai/Membership/); ADR-016 (cache + retry pattern);
//            bff-extensions.md §A (BFF pre-merge checklist).

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Default <see cref="IMembershipResolverService"/> implementation. Orchestrates
/// discovery + identity normalization + a single OR-joined FetchXml query to
/// resolve the rows of <c>entityType</c> a user is a member of, grouped by role.
/// Per-user results cached in Redis (5-min TTL, FR-1A.8 Phase 1A).
/// </summary>
public sealed class MembershipResolverService : IMembershipResolverService
{
    /// <summary>
    /// Redis cache key prefix for per-user resolved membership results.
    /// </summary>
    /// <remarks>
    /// Format: <c>membership:resolved:{systemUserId:D}:{entityType}:{optionsHash}</c>.
    /// The <c>membership:</c> namespace prefix aligns with the Phase 2 invalidation
    /// channel (FR-2P2.8) — a future per-user invalidation can wipe entries under
    /// <c>membership:resolved:{systemUserId:D}:*</c> without affecting other Redis
    /// namespaces.
    /// </remarks>
    internal const string CacheKeyPrefix = "membership:resolved:";

    /// <summary>Phase 1A per-user cache TTL (FR-1A.8).</summary>
    internal static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IMembershipFieldDiscoveryService _discovery;
    private readonly IIdentityNormalizationService _identity;
    private readonly IDataverseService _dataverse;
    private readonly IDistributedCache _cache;
    private readonly ILogger<MembershipResolverService> _logger;

    public MembershipResolverService(
        IMembershipFieldDiscoveryService discovery,
        IIdentityNormalizationService identity,
        IDataverseService dataverse,
        IDistributedCache cache,
        IOptions<MembershipOptions> options,
        ILogger<MembershipResolverService> logger)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _discovery = discovery;
        _identity = identity;
        _dataverse = dataverse;
        _cache = cache;
        _logger = logger;
        _ = options.Value; // Reserved for future tuning (Phase 1D depth, paging
                           // strategy, etc.). Resolving here surfaces binding
                           // errors at construction rather than first call.
    }

    /// <inheritdoc/>
    public async Task<MembershipResponse> ResolveAsync(
        Guid systemUserId,
        string entityType,
        MembershipResolveOptions? options,
        CancellationToken ct)
    {
        if (systemUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "systemUserId must not be Guid.Empty.",
                nameof(systemUserId));
        }
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException(
                "entityType must not be null, empty, or whitespace.",
                nameof(entityType));
        }

        ct.ThrowIfCancellationRequested();

        var normalizedEntity = entityType.Trim().ToLowerInvariant();
        var effectiveOptions = options ?? new MembershipResolveOptions();

        // ── R3 Part 1D — pre-validate includeRelated for >1-hop syntax ─────
        // FR-1D.2 / Q3 (owner 2026-06-20): max chain depth is 1 hop. Reject
        // explicit chain syntax (e.g., "documents.events") at the front door
        // before any I/O — depth violations are caller errors, not server
        // errors, so the endpoint surfaces them as 400 BadRequest.
        if (effectiveOptions.IncludeRelated is { Count: > 0 } preflight)
        {
            foreach (var entry in preflight)
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }
                var trimmed = entry.Trim();
                if (trimmed.Contains('.', StringComparison.Ordinal) ||
                    trimmed.Contains('/', StringComparison.Ordinal))
                {
                    throw new MembershipDepthExceededException(
                        offendingEntry: trimmed,
                        reasonTag: "explicit-chain-syntax",
                        message: $"includeRelated entry '{trimmed}' uses chain syntax that " +
                                 "exceeds the 1-hop maximum. Each entry must name a single " +
                                 "related entity (e.g., 'sprk_document'); chained syntax " +
                                 "such as 'sprk_document.sprk_event' is not supported.");
                }
            }
        }

        // ── Cache lookup ────────────────────────────────────────────────────
        var cacheKey = BuildCacheKey(systemUserId, normalizedEntity, effectiveOptions);
        var cached = await TryGetFromCacheAsync(cacheKey, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogDebug(
                "MembershipResolverService cache HIT for systemUserId={SystemUserId} entity={EntityType}",
                systemUserId, normalizedEntity);
            return cached;
        }

        var sw = Stopwatch.StartNew();
        _logger.LogDebug(
            "MembershipResolverService cache MISS for systemUserId={SystemUserId} entity={EntityType} — resolving",
            systemUserId, normalizedEntity);

        // ── R3 Part 1D — log requested transitive expansion ────────────────
        // The actual resolution happens after the primary resolution below so
        // we have the matched primary ids[] to join from. Validation already
        // ran above (explicit chain syntax → MembershipDepthExceededException).
        var includeRelatedList = effectiveOptions.IncludeRelated;
        if (includeRelatedList is { Count: > 0 } requested)
        {
            _logger.LogDebug(
                "MembershipResolverService: includeRelated={IncludeRelated} requested for systemUserId={SystemUserId} entity={EntityType} (Phase 1D transitive expansion)",
                string.Join(",", requested), systemUserId, normalizedEntity);
        }

        // ── a) Discovery ────────────────────────────────────────────────────
        var discovery = await _discovery.DiscoverAsync(normalizedEntity, ct).ConfigureAwait(false);
        var descriptors = FilterDescriptors(discovery.DiscoveredFields, effectiveOptions);

        // ── c) Identity normalization (started in parallel with discovery
        //     would also be valid, but discovery is typically cache-hot;
        //     sequential keeps the failure-flow simpler).
        var identity = await _identity.ResolveAsync(systemUserId, ct).ConfigureAwait(false);

        // Empty descriptors → no matching rows (return empty response, NOT error).
        if (descriptors.Count == 0)
        {
            _logger.LogDebug(
                "MembershipResolverService: no membership-bearing fields after filtering for " +
                "entity={EntityType} (descriptors={DescriptorCount}, roles={RolesFilter}, " +
                "identityTypes={IdentityTypesFilter}) — returning empty response",
                normalizedEntity,
                discovery.DiscoveredFields.Count,
                effectiveOptions.Roles is null ? "all" : string.Join(",", effectiveOptions.Roles),
                effectiveOptions.IdentityTypes is null ? "all" : string.Join(",", effectiveOptions.IdentityTypes));

            // No primary ids → transitive map is also empty (but shape-preserved
            // so clients see "requested but none matched"). Validation still
            // runs so unknown related entities are reported even when primary
            // resolution returns nothing.
            var emptyTransitive = includeRelatedList is { Count: > 0 }
                ? await ResolveTransitiveAsync(
                        normalizedEntity,
                        primaryIds: Array.Empty<Guid>(),
                        includeRelated: includeRelatedList,
                        ct).ConfigureAwait(false)
                : null;
            var emptyResponse = BuildEmptyResponse(normalizedEntity, identity, descriptors, emptyTransitive);
            await TrySetCacheAsync(cacheKey, emptyResponse, ct).ConfigureAwait(false);
            return emptyResponse;
        }

        // ── d) Build FetchXml ──────────────────────────────────────────────
        // Strategy: <fetch top='{limit + 1}' distinct='true'> select id + each
        // descriptor's lookup field, OR-joined conditions over (field, identity-value).
        // top = limit + 1 lets us detect "has more" without a separate count query.
        var effectiveLimit = ClampLimit(effectiveOptions.Limit);
        var fetchSkip = DecodeContinuationSkip(effectiveOptions.ContinuationToken);
        var (fetchXml, fetchSummary) = BuildFetchXml(
            normalizedEntity,
            descriptors,
            identity,
            effectiveLimit,
            fetchSkip,
            systemUserId);

        if (fetchSummary.ConditionCount == 0)
        {
            // No identity values matched the descriptor identity-types (e.g., user
            // has no contact, no teams, no organizations and all descriptors target
            // Contact/Team/Organization). Empty response — not an error.
            _logger.LogDebug(
                "MembershipResolverService: zero conditions built for entity={EntityType} " +
                "(descriptors={DescriptorCount}, but no identity values matched) — returning empty",
                normalizedEntity, descriptors.Count);

            var emptyTransitive = includeRelatedList is { Count: > 0 }
                ? await ResolveTransitiveAsync(
                        normalizedEntity,
                        primaryIds: Array.Empty<Guid>(),
                        includeRelated: includeRelatedList,
                        ct).ConfigureAwait(false)
                : null;
            var emptyResponse = BuildEmptyResponse(normalizedEntity, identity, descriptors, emptyTransitive);
            await TrySetCacheAsync(cacheKey, emptyResponse, ct).ConfigureAwait(false);
            return emptyResponse;
        }

        // ── e) Execute query ────────────────────────────────────────────────
        var fetch = new FetchExpression(fetchXml);
        var entityCollection = await _dataverse
            .RetrieveMultipleAsync(fetch, ct)
            .ConfigureAwait(false);

        // ── f) Materialize: dedupe + sort + byRole map ──────────────────────
        var (ids, byRole, hasMore) = MaterializeResults(
            entityCollection,
            descriptors,
            effectiveLimit);

        // ── g) Paging — emit continuationToken if more rows exist ──────────
        string? nextToken = null;
        if (hasMore)
        {
            nextToken = EncodeContinuationSkip(fetchSkip + effectiveLimit);
        }

        // ── R3 Part 1D — transitive expansion (only when requested) ────────
        // Validation + per-related-entity FetchXml join. Throws
        // MembershipDepthExceededException if a requested related entity has
        // no 1-hop Lookup back to the primary entity (FR-1D.2 / Q3).
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Guid>>>? relatedByRole = null;
        if (includeRelatedList is { Count: > 0 })
        {
            relatedByRole = await ResolveTransitiveAsync(
                normalizedEntity,
                primaryIds: ids,
                includeRelated: includeRelatedList,
                ct).ConfigureAwait(false);
        }

        // ── h) Build + cache response ───────────────────────────────────────
        var expiresAt = DateTimeOffset.UtcNow.Add(CacheTtl);
        var response = new MembershipResponse(
            EntityType: normalizedEntity,
            PersonIdentity: identity,
            Ids: ids,
            ByRole: byRole,
            Count: ids.Count,
            CacheExpiresAt: expiresAt,
            ContinuationToken: nextToken,
            RelatedByRole: relatedByRole);

        await TrySetCacheAsync(cacheKey, response, ct).ConfigureAwait(false);

        sw.Stop();
        _logger.LogInformation(
            "MembershipResolverService resolved systemUserId={SystemUserId} entity={EntityType} " +
            "in {ElapsedMs}ms (descriptors={DescriptorCount}, conditions={ConditionCount}, " +
            "rows={RowCount}, roles={RoleCount}, hasMore={HasMore}, relatedEntities={RelatedCount})",
            systemUserId, normalizedEntity, sw.ElapsedMilliseconds,
            descriptors.Count, fetchSummary.ConditionCount, ids.Count, byRole.Count, hasMore,
            relatedByRole?.Count ?? 0);

        return response;
    }

    // ── Descriptor filtering ───────────────────────────────────────────────
    private static IReadOnlyList<MembershipDescriptor> FilterDescriptors(
        IReadOnlyList<MembershipDescriptor> all,
        MembershipResolveOptions options)
    {
        if (all.Count == 0)
        {
            return Array.Empty<MembershipDescriptor>();
        }

        var roleFilter = ToFilterSet(options.Roles);
        var identityTypeFilter = ToFilterSet(options.IdentityTypes);

        if (roleFilter is null && identityTypeFilter is null)
        {
            return all;
        }

        var filtered = new List<MembershipDescriptor>(all.Count);
        foreach (var d in all)
        {
            if (roleFilter is not null && !roleFilter.Contains(d.Role))
            {
                continue;
            }
            if (identityTypeFilter is not null && !identityTypeFilter.Contains(d.IdentityType))
            {
                continue;
            }
            filtered.Add(d);
        }
        return filtered;
    }

    private static HashSet<string>? ToFilterSet(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                set.Add(v.Trim());
            }
        }
        return set.Count == 0 ? null : set;
    }

    // ── FetchXml construction ──────────────────────────────────────────────
    /// <summary>
    /// Builds the OR-joined FetchXml against <paramref name="entityType"/>.
    /// Each descriptor contributes one OR more conditions depending on its
    /// IdentityType:
    ///   SystemUser     → 1 condition (eq systemUserId)
    ///   Contact        → 1 condition (eq contactId) IF contactId present
    ///   Team           → N conditions (in teamIds[])
    ///   BusinessUnit   → 1 condition (eq businessUnitId) IF present
    ///   Account        → 1 condition (eq accountId) IF present
    ///   Organization   → N conditions (in organizationIds[])
    /// Returns the FetchXml text + a small summary used for logging / empty-detection.
    /// </summary>
    private (string FetchXml, FetchSummary Summary) BuildFetchXml(
        string entityType,
        IReadOnlyList<MembershipDescriptor> descriptors,
        PersonIdentity identity,
        int limit,
        int skip,
        Guid systemUserId)
    {
        // The set of attributes we need to project — entity id + each descriptor's
        // field — so the materialization step can attribute rows to roles.
        var projectAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder(512);
        sb.Append("<fetch distinct='true' top='").Append(limit + 1).Append('\'');
        if (skip > 0)
        {
            // FetchXml paging via 'page' attribute — page is 1-based with fixed
            // 'count'. Convert skip → page (skip MUST be a multiple of limit).
            var page = (skip / Math.Max(1, limit)) + 1;
            sb.Append(" page='").Append(page).Append("' count='").Append(limit + 1).Append('\'');
        }
        sb.Append("><entity name='").Append(EscapeXml(entityType)).Append("'>");

        // No <attribute name='primarykey' /> — Fetch automatically returns the
        // entity id. We project each descriptor's lookup field so we can split
        // rows by role in MaterializeResults.
        foreach (var d in descriptors)
        {
            if (projectAttributes.Add(d.Field))
            {
                sb.Append("<attribute name='").Append(EscapeXml(d.Field)).Append("' />");
            }
        }

        // OR-joined filter over (field, identity-value) pairs.
        sb.Append("<filter type='or'>");
        var conditionCount = 0;

        foreach (var d in descriptors)
        {
            switch (d.IdentityType)
            {
                case "SystemUser":
                    conditionCount += AppendCondition(sb, d.Field, identity.SystemUserId);
                    break;

                case "Contact":
                    if (identity.ContactId is { } cid)
                    {
                        conditionCount += AppendCondition(sb, d.Field, cid);
                    }
                    else
                    {
                        // R4 spec FR-11 / AC-11: Contact-typed membership descriptor whose
                        // user has no Contact cross-ref via azureactivedirectoryobjectid
                        // (per ADR-028 canonical mapping) is silently skipped today. Emit a
                        // structured `member_skipped` warning so Application Insights can
                        // pivot on it (traces | where message contains "member_skipped").
                        //
                        // Field bindings (per FR-11):
                        //   - MatterId  → entityType (the entity being resolved; sprk_matter in
                        //                 the canonical assignedAttorney case)
                        //   - ContactId → systemUserId (the caller whose Contact cross-ref is
                        //                 missing; ContactId itself is NULL — that IS the
                        //                 trigger — so we emit the SystemUserId for traceability)
                        //   - Role      → descriptor.Role (e.g., "assignedAttorney")
                        //   - Reason    → literal "no_systemuser_mapping"
                        //   - Field     → descriptor.Field (e.g., "sprk_assignedattorney1") —
                        //                 extra diagnostic context, not required by FR-11
                        //
                        // The literal token "member_skipped" stays in the message template so
                        // App Insights field-extraction captures the named columns.
                        _logger.LogWarning(
                            "member_skipped: matter={MatterId} contact={ContactId} role={Role} reason={Reason} field={Field}",
                            entityType,
                            systemUserId,
                            d.Role,
                            "no_systemuser_mapping",
                            d.Field);
                    }
                    break;

                case "Team":
                    foreach (var tid in identity.TeamIds)
                    {
                        conditionCount += AppendCondition(sb, d.Field, tid);
                    }
                    break;

                case "BusinessUnit":
                    if (identity.BusinessUnitId is { } buid)
                    {
                        conditionCount += AppendCondition(sb, d.Field, buid);
                    }
                    break;

                case "Account":
                    if (identity.AccountId is { } aid)
                    {
                        conditionCount += AppendCondition(sb, d.Field, aid);
                    }
                    break;

                case "Organization":
                    foreach (var oid in identity.OrganizationIds)
                    {
                        conditionCount += AppendCondition(sb, d.Field, oid);
                    }
                    break;

                default:
                    // Unknown identity-type — operator misconfigured IncludedIdentityTables.
                    // Skip this descriptor; the discovery service already logged on first build.
                    break;
            }
        }

        sb.Append("</filter></entity></fetch>");

        var summary = new FetchSummary(ConditionCount: conditionCount);
        return (sb.ToString(), summary);
    }

    private static int AppendCondition(StringBuilder sb, string field, Guid value)
    {
        if (value == Guid.Empty)
        {
            return 0;
        }
        // FetchXml condition: <condition attribute='X' operator='eq' value='Y' />
        // Guid serialized as "D" format (canonical 32 hex digits with hyphens, no braces).
        sb.Append("<condition attribute='")
          .Append(EscapeXml(field))
          .Append("' operator='eq' value='")
          .Append(value.ToString("D", CultureInfo.InvariantCulture))
          .Append("' />");
        return 1;
    }

    private static string EscapeXml(string value)
    {
        // FetchXml uses single-quoted attribute values; escape '<', '>', '&', '\''.
        // Field names + entity logical names are restricted Dataverse identifiers
        // (alphanumeric + underscore), so this is defense-in-depth.
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }

    // ── Result materialization ─────────────────────────────────────────────
    /// <summary>
    /// Walks the EntityCollection and produces:
    ///   - distinct, sorted ids[] (truncated to limit)
    ///   - byRole map: role → list of ids the user has that role on
    ///   - hasMore: true when more rows exist beyond the requested limit
    /// </summary>
    private static (IReadOnlyList<Guid> Ids,
                    IReadOnlyDictionary<string, IReadOnlyList<Guid>> ByRole,
                    bool HasMore) MaterializeResults(
        EntityCollection entityCollection,
        IReadOnlyList<MembershipDescriptor> descriptors,
        int limit)
    {
        // Initialize byRole with every role as an empty list — empty buckets help
        // clients distinguish "queried, no matches" from "not in the query".
        var byRoleAccum = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        foreach (var d in descriptors)
        {
            if (!byRoleAccum.ContainsKey(d.Role))
            {
                byRoleAccum[d.Role] = new HashSet<Guid>();
            }
        }

        var allIds = new HashSet<Guid>();

        foreach (var row in entityCollection.Entities)
        {
            if (row.Id == Guid.Empty)
            {
                continue;
            }
            allIds.Add(row.Id);

            // Attribute the row to each role whose descriptor field carries a non-empty
            // EntityReference on this row. Multiple roles per row are valid + expected
            // (e.g., the user is both owner AND assignedAttorney on the same matter).
            foreach (var d in descriptors)
            {
                if (RowMatchesDescriptor(row, d))
                {
                    byRoleAccum[d.Role].Add(row.Id);
                }
            }
        }

        // Detect "has more" using the top=limit+1 sentinel.
        var hasMore = allIds.Count > limit;

        // Sort ids ascending for deterministic output, then truncate to limit.
        var sortedIds = allIds.OrderBy(g => g).Take(limit).ToList();
        var keptSet = new HashSet<Guid>(sortedIds);

        // Truncate byRole buckets to only the ids we kept in sortedIds.
        var byRoleFinal = new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal);
        foreach (var (role, set) in byRoleAccum)
        {
            var kept = set.Where(keptSet.Contains).OrderBy(g => g).ToList();
            byRoleFinal[role] = kept;
        }

        return (sortedIds, byRoleFinal, hasMore);
    }

    /// <summary>
    /// True when the row's <paramref name="descriptor"/>.Field is populated (as
    /// either an <see cref="EntityReference"/> or a <see cref="Guid"/>) and is non-empty.
    /// The row already matched the OR-filter, so we only need to find WHICH descriptor(s)
    /// match — we don't re-compare values to the identity here.
    /// </summary>
    private static bool RowMatchesDescriptor(Entity row, MembershipDescriptor descriptor)
    {
        if (!row.Contains(descriptor.Field))
        {
            return false;
        }
        var value = row[descriptor.Field];
        return value switch
        {
            EntityReference er => er.Id != Guid.Empty,
            Guid g => g != Guid.Empty,
            _ => false
        };
    }

    // ── Empty response helper ──────────────────────────────────────────────
    private static MembershipResponse BuildEmptyResponse(
        string entityType,
        PersonIdentity identity,
        IReadOnlyList<MembershipDescriptor> descriptors,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Guid>>>? relatedByRole = null)
    {
        var emptyByRole = new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal);
        foreach (var d in descriptors)
        {
            emptyByRole[d.Role] = Array.Empty<Guid>();
        }
        return new MembershipResponse(
            EntityType: entityType,
            PersonIdentity: identity,
            Ids: Array.Empty<Guid>(),
            ByRole: emptyByRole,
            Count: 0,
            CacheExpiresAt: DateTimeOffset.UtcNow.Add(CacheTtl),
            ContinuationToken: null,
            RelatedByRole: relatedByRole);
    }

    // ── R3 Part 1D — transitive expansion (task 054) ───────────────────────
    /// <summary>
    /// Resolves transitive memberships per FR-1D.1 / FR-1D.2 / FR-1D.3. For
    /// each entry in <paramref name="includeRelated"/>:
    ///   1. Discover Lookup fields on the related entity whose Targets[] include
    ///      <paramref name="primaryEntity"/> (1-hop verification per Q3).
    ///   2. If no such Lookups exist → throw <see cref="MembershipDepthExceededException"/>
    ///      with reason "not-a-direct-lookup-target" — the caller's request cannot
    ///      be satisfied within the 1-hop budget.
    ///   3. Build a single FetchXml query against the related entity:
    ///        - Filter type='or' over (lookupField, primaryId) pairs using
    ///          the <c>in</c> operator so all primary ids are joined in one
    ///          condition per lookup field (efficient — single round trip).
    ///        - Project the related entity's id + each back-reference lookup
    ///          field so MaterializeResults can attribute rows to per-lookup
    ///          "roles" (e.g., a document's <c>sprk_matter</c> back-reference).
    ///   4. Materialize: dedupe ids, sort, build per-role buckets keyed by
    ///      derived role name (CamelCase strategy mirrors the primary resolver).
    ///
    /// Empty <paramref name="primaryIds"/> short-circuits the FetchXml — the
    /// 1-hop discovery still runs (so unknown-entity validation surfaces) but
    /// the result is the empty inner map (role keys → empty id lists).
    ///
    /// Throws <see cref="MembershipDepthExceededException"/> for any entry whose
    /// related entity cannot be resolved (unknown-entity) OR has no 1-hop Lookup
    /// back to the primary entity (not-a-direct-lookup-target). The endpoint
    /// layer maps this to 400 BadRequest per the 1-hop binding in FR-1D.2.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Guid>>>>
        ResolveTransitiveAsync(
            string primaryEntity,
            IReadOnlyList<Guid> primaryIds,
            IReadOnlyList<string> includeRelated,
            CancellationToken ct)
    {
        // Outer dict: related entity → per-role inner map. Ordinal keys
        // (Dataverse logical names are lowercase ASCII) so output is stable
        // across .NET releases.
        var outer = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Guid>>>(
            StringComparer.Ordinal);

        // De-dup + normalize the requested entries (the endpoint may already
        // do this, but the resolver is the policy enforcement point — defense
        // in depth).
        var requested = new List<string>(includeRelated.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in includeRelated)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }
            var normalized = entry.Trim().ToLowerInvariant();
            if (seen.Add(normalized))
            {
                requested.Add(normalized);
            }
        }

        foreach (var relatedEntity in requested)
        {
            ct.ThrowIfCancellationRequested();

            // ── 1-hop verification via metadata discovery ──────────────────
            IReadOnlyList<string> backRefLookups;
            try
            {
                backRefLookups = await _discovery
                    .DiscoverLookupsTargetingAsync(relatedEntity, primaryEntity, ct)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                // Unknown related entity (metadata fetch failed). Surface as a
                // 1-hop-not-possible error so the endpoint returns 400, not 500.
                throw new MembershipDepthExceededException(
                    offendingEntry: relatedEntity,
                    reasonTag: "unknown-entity",
                    message: $"includeRelated entry '{relatedEntity}' could not be resolved: " +
                             $"{ex.Message}",
                    inner: ex);
            }

            if (backRefLookups.Count == 0)
            {
                throw new MembershipDepthExceededException(
                    offendingEntry: relatedEntity,
                    reasonTag: "not-a-direct-lookup-target",
                    message: $"includeRelated entry '{relatedEntity}' has no Lookup field " +
                             $"targeting '{primaryEntity}'. The 1-hop max enforced by " +
                             $"FR-1D.2 requires a direct relationship; transitive expansion " +
                             $"beyond a single hop is not supported.");
            }

            // ── No primary ids → empty inner map but preserve role keys ────
            if (primaryIds.Count == 0)
            {
                var emptyInner = new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal);
                foreach (var field in backRefLookups)
                {
                    emptyInner[DeriveRoleFromField(field)] = Array.Empty<Guid>();
                }
                outer[relatedEntity] = emptyInner;
                continue;
            }

            // ── Build FetchXml: OR-joined `in` conditions per back-ref lookup
            // Strategy: single Fetch projects (id + each back-ref lookup field);
            // filter type='or' contains one <condition operator='in'> per lookup
            // field with all primary ids as <value> children. Materialization
            // mirrors MaterializeResults' descriptor-based row attribution.
            var fetchXml = BuildTransitiveFetchXml(relatedEntity, backRefLookups, primaryIds);
            var fetch = new FetchExpression(fetchXml);
            var entityCollection = await _dataverse
                .RetrieveMultipleAsync(fetch, ct)
                .ConfigureAwait(false);

            var inner = MaterializeTransitiveResults(entityCollection, backRefLookups);
            outer[relatedEntity] = inner;
        }

        return outer;
    }

    /// <summary>
    /// Builds the FetchXml for a single related-entity transitive query.
    /// Projects entity id + each back-ref lookup; filter type='or' contains
    /// one <c>in</c> condition per back-ref lookup with all primary ids as
    /// value children. The <c>in</c> operator handles up to 500 values per
    /// condition in standard Dataverse — primary ids[] is already bounded by
    /// the resolver's effectiveLimit (default 500, max 5000).
    /// </summary>
    private static string BuildTransitiveFetchXml(
        string relatedEntity,
        IReadOnlyList<string> backRefLookups,
        IReadOnlyList<Guid> primaryIds)
    {
        // Cap to MaxLimit defensively — the primary resolver already truncates
        // ids[] to effectiveLimit (<= MaxLimit), but a future caller of this
        // helper might not.
        var capped = primaryIds.Count <= MembershipResolveOptions.MaxLimit
            ? primaryIds
            : primaryIds.Take(MembershipResolveOptions.MaxLimit).ToList();

        var sb = new StringBuilder(512);
        // top is generous — transitive results are 1-hop only and naturally
        // bounded by the related-entity volume. We use the primary MaxLimit
        // as a sane ceiling.
        sb.Append("<fetch distinct='true' top='").Append(MembershipResolveOptions.MaxLimit).Append('\'');
        sb.Append("><entity name='").Append(EscapeXml(relatedEntity)).Append("'>");

        // Project each back-ref lookup field so MaterializeTransitiveResults
        // can attribute rows to roles.
        foreach (var field in backRefLookups)
        {
            sb.Append("<attribute name='").Append(EscapeXml(field)).Append("' />");
        }

        sb.Append("<filter type='or'>");
        foreach (var field in backRefLookups)
        {
            sb.Append("<condition attribute='").Append(EscapeXml(field)).Append("' operator='in'>");
            foreach (var id in capped)
            {
                if (id == Guid.Empty)
                {
                    continue;
                }
                sb.Append("<value>")
                  .Append(id.ToString("D", CultureInfo.InvariantCulture))
                  .Append("</value>");
            }
            sb.Append("</condition>");
        }
        sb.Append("</filter></entity></fetch>");

        return sb.ToString();
    }

    /// <summary>
    /// Materializes a transitive-query EntityCollection into the inner
    /// <c>role → ids</c> map. Mirrors <see cref="MaterializeResults"/> but
    /// scoped to back-reference lookups (no IdentityType branching needed —
    /// every Lookup is treated as one role).
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<Guid>> MaterializeTransitiveResults(
        EntityCollection entityCollection,
        IReadOnlyList<string> backRefLookups)
    {
        var byRoleAccum = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        foreach (var field in backRefLookups)
        {
            byRoleAccum[DeriveRoleFromField(field)] = new HashSet<Guid>();
        }

        foreach (var row in entityCollection.Entities)
        {
            if (row.Id == Guid.Empty)
            {
                continue;
            }
            foreach (var field in backRefLookups)
            {
                if (!row.Contains(field))
                {
                    continue;
                }
                var value = row[field];
                var matches = value switch
                {
                    EntityReference er => er.Id != Guid.Empty,
                    Guid g => g != Guid.Empty,
                    _ => false
                };
                if (matches)
                {
                    byRoleAccum[DeriveRoleFromField(field)].Add(row.Id);
                }
            }
        }

        var inner = new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal);
        foreach (var (role, set) in byRoleAccum)
        {
            inner[role] = set.OrderBy(g => g).ToList();
        }
        return inner;
    }

    /// <summary>
    /// Derive a public-facing role name from a back-reference Lookup field
    /// name using the same CamelCase strategy as the primary discovery service
    /// (strip <c>sprk_</c> prefix + strip trailing digits + first char lowercase).
    /// Kept as a local helper rather than calling into
    /// <see cref="MembershipFieldDiscoveryService.DeriveRoleNameCamelCase"/>
    /// directly to keep the transitive code self-contained (avoids reaching
    /// across a sibling internal API).
    /// </summary>
    private static string DeriveRoleFromField(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return field ?? string.Empty;
        }

        var trimmed = field.Trim();
        var working = trimmed;

        const string prefix = "sprk_";
        if (working.Length > prefix.Length &&
            working.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            working = working.Substring(prefix.Length);
        }

        // Strip trailing digits.
        var endTrim = working.Length;
        while (endTrim > 0 && char.IsDigit(working[endTrim - 1]))
        {
            endTrim--;
        }
        if (endTrim > 0)
        {
            working = working.Substring(0, endTrim);
        }

        if (working.Length == 0)
        {
            return trimmed;
        }

        var first = char.ToLower(working[0], CultureInfo.InvariantCulture);
        return working.Length == 1
            ? first.ToString(CultureInfo.InvariantCulture)
            : first + working.Substring(1);
    }

    // ── Paging helpers ─────────────────────────────────────────────────────
    private static int ClampLimit(int requested)
    {
        if (requested <= 0)
        {
            return MembershipResolveOptions.DefaultLimit;
        }
        return Math.Min(requested, MembershipResolveOptions.MaxLimit);
    }

    /// <summary>
    /// Encodes the skip-count as a base64url continuation token. Opaque to
    /// callers — they round-trip the value via
    /// <see cref="MembershipResolveOptions.ContinuationToken"/>.
    /// </summary>
    private static string EncodeContinuationSkip(int skip)
    {
        var bytes = BitConverter.GetBytes(skip);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Decodes a previously-emitted continuation token back to a skip-count.
    /// Returns 0 for null/empty/invalid tokens (i.e., treat as "first page").
    /// </summary>
    private static int DecodeContinuationSkip(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return 0;
        }
        try
        {
            var normalized = token.Replace('-', '+').Replace('_', '/');
            switch (normalized.Length % 4)
            {
                case 2: normalized += "=="; break;
                case 3: normalized += "="; break;
            }
            var bytes = Convert.FromBase64String(normalized);
            if (bytes.Length != sizeof(int))
            {
                return 0;
            }
            var skip = BitConverter.ToInt32(bytes, 0);
            return skip < 0 ? 0 : skip;
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    // ── Cache helpers ──────────────────────────────────────────────────────
    private string BuildCacheKey(Guid systemUserId, string entityType, MembershipResolveOptions options)
    {
        // Options hash — deterministic across equivalent option values regardless of
        // ordering. Includes Roles, IdentityTypes, IncludeRelated, Limit, and the
        // ContinuationToken (so paging requests cache per-page).
        var sb = new StringBuilder(64);
        sb.Append("r:").Append(HashSorted(options.Roles)).Append('|');
        sb.Append("i:").Append(HashSorted(options.IdentityTypes)).Append('|');
        sb.Append("x:").Append(HashSorted(options.IncludeRelated)).Append('|');
        sb.Append("l:").Append(options.Limit).Append('|');
        sb.Append("c:").Append(options.ContinuationToken ?? string.Empty);

        var hashInput = sb.ToString();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        // First 8 bytes → 16 hex chars. Collision risk negligible at this scope.
        var optionsHash = Convert.ToHexString(hashBytes, 0, 8).ToLowerInvariant();

        return $"{CacheKeyPrefix}{systemUserId:D}:{entityType}:{optionsHash}";
    }

    private static string HashSorted(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return "*";
        }
        var sorted = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLowerInvariant())
            .Distinct()
            .OrderBy(v => v, StringComparer.Ordinal);
        return string.Join(",", sorted);
    }

    private async Task<MembershipResponse?> TryGetFromCacheAsync(string cacheKey, CancellationToken ct)
    {
        try
        {
            var bytes = await _cache.GetAsync(cacheKey, ct).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                return null;
            }
            return JsonSerializer.Deserialize<MembershipResponse>(bytes, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Cache failure must NOT break resolution — fall through to re-resolve.
            _logger.LogWarning(ex,
                "MembershipResolverService failed to read cache for {CacheKey}; " +
                "falling through to live resolve",
                cacheKey);
            return null;
        }
    }

    private async Task TrySetCacheAsync(string cacheKey, MembershipResponse response, CancellationToken ct)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
            await _cache.SetAsync(
                cacheKey,
                bytes,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MembershipResolverService failed to write cache for {CacheKey}; " +
                "next call will re-resolve (no functional impact)",
                cacheKey);
        }
    }

    /// <summary>Small summary used for logging + empty-detection.</summary>
    private readonly record struct FetchSummary(int ConditionCount);
}
