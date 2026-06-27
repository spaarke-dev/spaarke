// R3 Part 1 — User-Record Membership Resolution (discovery implementation)
// Task 030 (2026-06-21): Metadata-driven Lookup-field discovery.
//
//   1. Cache lookup (Redis via IDistributedCache, key "membership:discovery:{entityType}").
//      TTL = MembershipOptions.MetadataCacheTtlMinutes (default 60 min, ADR-009).
//      Cache failures are graceful — fall through to live metadata fetch.
//   2. On cache miss, fetch entity metadata via the existing canonical pattern
//      from Sprk.Bff.Api.Services.Dataverse.MetadataService (RetrieveEntityRequest
//      with EntityFilters.Attributes, routed through DataverseServiceClientImpl
//      .OrganizationService.Execute) — protected virtual seam allows unit-test
//      subclasses to bypass Dataverse without inventing a new interface.
//   3. Classify each Lookup attribute:
//        a. Targets[]-intersects-configured-identity-tables → keep
//        b. matches GlobalFieldExclusions → ExcludedField (reason="global-exclusion")
//           (unless per-entity IncludedFields force-includes it — reason="override")
//        c. matches per-entity ExcludedFields → ExcludedField (reason="per-entity-exclusion")
//        d. otherwise (target not in identity list) → IgnoredField
//           (reason="target-table-not-in-identity-list", carries Target name)
//   4. Derive role name via CamelCase strategy (strip sprk_ prefix + strip
//      trailing digits + camelCase), OR use FieldRoleOverrides verbatim when
//      configured for that field (Source="override" in that case).
//   5. Derive identity-type by lookup of TargetTable in IncludedIdentityTables.
//   6. Cache the DiscoveryResult + return.
//
// Per Q4 owner clarification (2026-06-20): sprk_assignedlawfirm1/2 target
// sprk_organization (NOT contact). The identity-type for those fields therefore
// resolves to "Organization" — handled correctly by the generic target-table-
// to-identity-type map, no special-casing required.
//
// ADR-013 (placement under Services/Ai/Membership/); ADR-010 (concrete +
// interface as testing seam); ADR-009 (Redis cache 1h TTL configurable).
// bff-extensions.md §A pre-merge checklist applied in
// notes/bff-publish-size-task030.md.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1A.1
//            through FR-1A.4, FR-1A.7; design.md Part 1 § "Discovery algorithm".

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Metadata-driven Lookup-field discovery for the membership-resolution feature
/// (R3 Part 1). Implements <see cref="IMembershipFieldDiscoveryService"/>.
/// Caches per-entity results in Redis with TTL from
/// <see cref="MembershipOptions.MetadataCacheTtlMinutes"/> (default 60 min).
/// </summary>
public class MembershipFieldDiscoveryService : IMembershipFieldDiscoveryService
{
    /// <summary>
    /// Cache resource label (per ITenantCache contract). On-wire key becomes
    /// <c>tenant:{tenantId}:membership-discovery:{entityType}:v1</c>.
    /// </summary>
    /// <remarks>
    /// Phase 2 invalidation (FR-2P2.8): admin <c>refresh-metadata</c> endpoint
    /// (task 036) wipes all entries by entity. NOTE (NFR-08 SYSTEM-LEVEL exception
    /// candidate, inventory group 017): the underlying field-discovery catalog
    /// is Dataverse-entity-schema metadata — effectively org-wide. Per inventory
    /// recommendation, migrated as tenant-scoped because one BFF == one tenant
    /// (Q5 audit 2026-05-27); within a tenant, the data is org-wide.
    /// </remarks>
    internal const string CacheResource = "membership-discovery";

    /// <summary>Cache schema version per ADR-009.</summary>
    private const int CacheVersion = 1;

    // Strip trailing decimal digits from a logical name suffix, e.g.,
    // "assignedattorney1" → "assignedattorney". Matches the CamelCase strategy
    // documented in design.md Part 1 § Discovery algorithm step 5.
    private static readonly Regex TrailingDigitsRegex = new(
        @"\d+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IDataverseService _dataverse;
    private readonly ITenantCache _cache;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly MembershipOptions _options;
    private readonly ILogger<MembershipFieldDiscoveryService> _logger;

    // Tracks the set of lowercase-normalized entity-type strings whose cache
    // entries this service has populated since process start. Backs the
    // "refresh-all" code-path in InvalidateCacheAsync (task 036) since neither
    // IDistributedCache (Redis or in-memory) exposes a portable "scan by
    // prefix" API. Used as a Set — value is irrelevant; we choose byte.
    private readonly ConcurrentDictionary<string, byte> _populatedEntityKeys = new(StringComparer.OrdinalIgnoreCase);

    public MembershipFieldDiscoveryService(
        IDataverseService dataverse,
        ITenantCache cache,
        IOptions<MembershipOptions> options,
        ILogger<MembershipFieldDiscoveryService> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _dataverse = dataverse;
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the tenant ID for tenant-scoped cache keys (FR-05).
    /// Reads the AAD <c>tid</c> claim from the current HttpContext per ADR-028;
    /// falls back to <c>"anonymous"</c> when no HttpContext is available (e.g.,
    /// admin / background-job invocations of <c>InvalidateCacheAsync</c>).
    /// </summary>
    private string GetTenantId()
        => _httpContextAccessor?.HttpContext?.User?.FindFirst("tid")?.Value
            ?? _httpContextAccessor?.HttpContext?.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
            ?? "anonymous";

    /// <inheritdoc/>
    public async Task<DiscoveryResult> DiscoverAsync(string entityLogicalName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            throw new ArgumentException(
                "entityLogicalName must not be null, empty, or whitespace.",
                nameof(entityLogicalName));
        }

        ct.ThrowIfCancellationRequested();

        var normalizedName = entityLogicalName.Trim().ToLowerInvariant();
        var tenantId = GetTenantId();

        // ── 1. Cache lookup ────────────────────────────────────────────────
        var cached = await TryGetFromCacheAsync(tenantId, normalizedName, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogDebug(
                "MembershipFieldDiscoveryService cache HIT for entity={EntityType}",
                normalizedName);
            return cached;
        }

        var sw = Stopwatch.StartNew();
        _logger.LogDebug(
            "MembershipFieldDiscoveryService cache MISS for entity={EntityType} — fetching metadata",
            normalizedName);

        // ── 2. Live metadata fetch (virtual seam — unit tests override) ────
        var lookups = await FetchLookupAttributesAsync(normalizedName, ct).ConfigureAwait(false);

        // ── 3-5. Classify, filter, derive role + identity-type ─────────────
        var result = BuildDiscoveryResult(normalizedName, lookups);

        // ── 6. Cache + return ──────────────────────────────────────────────
        await TrySetCacheAsync(tenantId, normalizedName, result, ct).ConfigureAwait(false);

        sw.Stop();
        _logger.LogInformation(
            "MembershipFieldDiscoveryService resolved entity={EntityType} in {ElapsedMs}ms " +
            "(discovered={DiscoveredCount}, excluded={ExcludedCount}, ignored={IgnoredCount})",
            normalizedName,
            sw.ElapsedMilliseconds,
            result.DiscoveredFields.Count,
            result.ExcludedFields.Count,
            result.IgnoredFields.Count);

        return result;
    }

    // ── Classification + role-derivation core ──────────────────────────────
    internal DiscoveryResult BuildDiscoveryResult(
        string normalizedEntityName,
        IReadOnlyList<LookupAttributeRow> lookups)
    {
        // Pre-compute identity-table → identity-type lookup for O(1) per-field
        // resolution. Configured identity-table names compared case-insensitively
        // (Dataverse logical names are canonically lowercase, but operator config
        // may use any casing).
        var identityTypeByTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cfg in _options.IncludedIdentityTables ?? Enumerable.Empty<IdentityTableConfig>())
        {
            if (string.IsNullOrWhiteSpace(cfg?.Table) || string.IsNullOrWhiteSpace(cfg.IdentityType))
            {
                continue;
            }
            identityTypeByTable[cfg.Table.Trim()] = cfg.IdentityType.Trim();
        }

        var globalExclusions = new HashSet<string>(
            (_options.GlobalFieldExclusions ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);

        EntityOverride? entityOverride = null;
        if (_options.EntityOverrides is { } overrides &&
            overrides.TryGetValue(normalizedEntityName, out var matchedOverride) &&
            matchedOverride is not null)
        {
            entityOverride = matchedOverride;
        }

        var perEntityExclusions = new HashSet<string>(
            (entityOverride?.ExcludedFields ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var perEntityIncludes = new HashSet<string>(
            (entityOverride?.IncludedFields ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var fieldRoleOverrides = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var kv in entityOverride?.FieldRoleOverrides ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value))
            {
                continue;
            }
            fieldRoleOverrides[kv.Key.Trim()] = kv.Value.Trim();
        }

        var discoveredFields = new List<MembershipDescriptor>();
        var excludedFields = new List<IgnoredField>();
        var ignoredFields = new List<IgnoredField>();

        foreach (var lookup in lookups)
        {
            if (string.IsNullOrWhiteSpace(lookup.LogicalName))
            {
                continue;
            }

            var field = lookup.LogicalName.Trim();

            // Per-entity exclusion is unconditional — takes precedence even
            // over per-entity IncludedFields force-include (operators who set
            // both have a configuration conflict; the safer interpretation is
            // "I really do not want this field").
            if (perEntityExclusions.Contains(field))
            {
                excludedFields.Add(new IgnoredField(field, "per-entity-exclusion"));
                continue;
            }

            var isGloballyExcluded = globalExclusions.Contains(field);
            var isForceIncluded = perEntityIncludes.Contains(field);

            if (isGloballyExcluded && !isForceIncluded)
            {
                excludedFields.Add(new IgnoredField(field, "global-exclusion"));
                continue;
            }

            // Find the FIRST target that matches a configured identity table.
            // Most lookups have exactly one target; polymorphic lookups (e.g.,
            // customerid → account/contact) are rare on membership-bearing
            // fields and an operator-curated identity list should disambiguate.
            string? matchedTarget = null;
            string? matchedIdentityType = null;
            foreach (var target in lookup.Targets ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }
                if (identityTypeByTable.TryGetValue(target.Trim(), out var idType))
                {
                    matchedTarget = target.Trim();
                    matchedIdentityType = idType;
                    break;
                }
            }

            if (matchedTarget is null || matchedIdentityType is null)
            {
                // Capture the first concrete target (if any) for operator visibility.
                var visibleTarget = (lookup.Targets ?? Array.Empty<string>())
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
                    ?.Trim();
                ignoredFields.Add(new IgnoredField(
                    field,
                    "target-table-not-in-identity-list",
                    visibleTarget));
                continue;
            }

            // Derive role name. Override wins over auto-derivation; provenance
            // is "override" when EITHER a role-name override applied OR the
            // field was force-included via IncludedFields (i.e., it would have
            // been globally excluded but the per-entity override resurrected it).
            string role;
            string source;
            if (fieldRoleOverrides.TryGetValue(field, out var roleOverride))
            {
                role = roleOverride;
                source = "override";
            }
            else
            {
                role = DeriveRoleNameCamelCase(field);
                source = isForceIncluded ? "override" : "auto";
            }

            discoveredFields.Add(new MembershipDescriptor(
                Field: field,
                Role: role,
                IdentityType: matchedIdentityType,
                TargetTable: matchedTarget,
                Source: source));
        }

        // Stable ordering for deterministic admin-endpoint output. By field
        // ascending — matches the design.md report-endpoint example shape.
        discoveredFields.Sort((a, b) => string.CompareOrdinal(a.Field, b.Field));
        excludedFields.Sort((a, b) => string.CompareOrdinal(a.Field, b.Field));
        ignoredFields.Sort((a, b) => string.CompareOrdinal(a.Field, b.Field));

        return new DiscoveryResult(
            EntityType: normalizedEntityName,
            DiscoveredAt: DateTimeOffset.UtcNow,
            DiscoveredFields: discoveredFields,
            ExcludedFields: excludedFields,
            IgnoredFields: ignoredFields);
    }

    /// <summary>
    /// CamelCase role-name strategy per design.md Part 1 § Discovery algorithm
    /// step 5: strip <c>sprk_</c> prefix (case-insensitive), strip trailing
    /// numeric digits (e.g., <c>1</c> from <c>assignedattorney1</c>), convert
    /// to camelCase (first character lowercased). Empty/numeric-only/sprk-only
    /// inputs fall through to the trimmed original so callers always see a
    /// non-empty role name.
    /// </summary>
    internal static string DeriveRoleNameCamelCase(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return field ?? string.Empty;
        }

        var trimmed = field.Trim();
        var working = trimmed;

        // Strip case-insensitive sprk_ prefix
        const string prefix = "sprk_";
        if (working.Length > prefix.Length &&
            working.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            working = working.Substring(prefix.Length);
        }

        // Strip trailing digits (e.g., "assignedattorney1" → "assignedattorney")
        var stripped = TrailingDigitsRegex.Replace(working, string.Empty);

        // If stripping digits left an empty string (e.g., field was "sprk_1"),
        // fall back to the pre-strip working value so we never emit "".
        if (stripped.Length == 0)
        {
            stripped = working;
        }

        if (stripped.Length == 0)
        {
            return trimmed;
        }

        // CamelCase: first character lowercased; rest of string preserved as-is.
        // Dataverse logical names are canonically lowercase, so this almost
        // always produces "assignedattorney" not "assignedAttorney" — but if
        // a SchemaName-style "AssignedAttorney" sneaks in we still emit
        // "assignedAttorney" correctly.
        var first = char.ToLower(stripped[0], CultureInfo.InvariantCulture);
        return stripped.Length == 1
            ? first.ToString(CultureInfo.InvariantCulture)
            : first + stripped.Substring(1);
    }

    // ── Metadata fetch (virtual seam for tests) ────────────────────────────
    /// <summary>
    /// Fetches the entity's Lookup attributes via Dataverse SDK. Protected
    /// virtual so unit-test subclasses can return canned data without standing
    /// up a ServiceClient mock. Production path mirrors the canonical pattern
    /// used by <c>Services.Dataverse.MetadataService</c>:
    /// <c>RetrieveEntityRequest</c> with <c>EntityFilters.Attributes</c>,
    /// routed through <c>DataverseServiceClientImpl.OrganizationService.Execute</c>.
    /// </summary>
    protected virtual async Task<IReadOnlyList<LookupAttributeRow>> FetchLookupAttributesAsync(
        string entityLogicalName,
        CancellationToken ct)
    {
        var serviceClient = GetServiceClient();

        var request = new RetrieveEntityRequest
        {
            LogicalName = entityLogicalName,
            EntityFilters = EntityFilters.Attributes,
            RetrieveAsIfPublished = true
        };

        try
        {
            // ServiceClient.Execute is synchronous; wrap with the cancellation
            // token. Matches MetadataService.FetchEntityMetadataAsync precedent.
            var response = (RetrieveEntityResponse)await Task.Run(
                () => serviceClient.Execute(request), ct).ConfigureAwait(false);

            var attributes = response.EntityMetadata?.Attributes ?? Array.Empty<AttributeMetadata>();
            var rows = new List<LookupAttributeRow>();
            foreach (var attr in attributes.OfType<LookupAttributeMetadata>())
            {
                if (string.IsNullOrWhiteSpace(attr.LogicalName))
                {
                    continue;
                }

                rows.Add(new LookupAttributeRow(
                    LogicalName: attr.LogicalName,
                    Targets: attr.Targets ?? Array.Empty<string>()));
            }
            return rows;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex.Message.Contains("Could not find", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex,
                "MembershipFieldDiscoveryService: entity '{EntityLogicalName}' not found in Dataverse metadata",
                entityLogicalName);
            throw new InvalidOperationException(
                $"Entity '{entityLogicalName}' not found in Dataverse metadata.", ex);
        }
    }

    /// <summary>
    /// Unwrap the underlying <see cref="ServiceClient"/> from
    /// <see cref="IDataverseService"/>. Follows the same pattern used by
    /// <c>Services.Dataverse.MetadataService</c> +
    /// <c>Services.Finance.SpendSnapshotService</c>.
    /// </summary>
    private ServiceClient GetServiceClient()
    {
        if (_dataverse is DataverseServiceClientImpl impl)
        {
            return impl.OrganizationService;
        }

        throw new InvalidOperationException(
            $"MembershipFieldDiscoveryService requires IDataverseService to be backed by " +
            $"DataverseServiceClientImpl. Actual type: {_dataverse?.GetType().Name ?? "null"}.");
    }

    // ── Cache helpers ──────────────────────────────────────────────────────
    private async Task<DiscoveryResult?> TryGetFromCacheAsync(
        string tenantId,
        string entityType,
        CancellationToken ct)
    {
        try
        {
            return await _cache.GetAsync<DiscoveryResult>(
                tenantId,
                CacheResource,
                entityType,
                CacheVersion,
                ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Cache failure must NOT break discovery — fall through to live fetch.
            _logger.LogWarning(ex,
                "MembershipFieldDiscoveryService failed to read cache for tenant={TenantId} entity={EntityType}; " +
                "falling through to live metadata fetch",
                tenantId, entityType);
            return null;
        }
    }

    private async Task TrySetCacheAsync(
        string tenantId,
        string entityType,
        DiscoveryResult result,
        CancellationToken ct)
    {
        try
        {
            var ttlMinutes = _options.MetadataCacheTtlMinutes > 0
                ? _options.MetadataCacheTtlMinutes
                : 60;
            await _cache.SetAsync(
                tenantId,
                CacheResource,
                entityType,
                CacheVersion,
                result,
                TimeSpan.FromMinutes(ttlMinutes),
                ct: ct).ConfigureAwait(false);

            // Track the lowercase-normalized entity-type so InvalidateCacheAsync(null, ...)
            // can enumerate populated entries (ITenantCache exposes no portable scan-by-prefix API).
            // Cache write succeeded — recording on success preserves the invariant that the
            // tracking set never references keys that don't exist.
            if (!string.IsNullOrWhiteSpace(entityType))
            {
                _populatedEntityKeys.TryAdd(entityType, 0);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MembershipFieldDiscoveryService failed to write cache for tenant={TenantId} entity={EntityType}; " +
                "next call will re-resolve (no functional impact)",
                tenantId, entityType);
        }
    }

    // ── Admin cache invalidation (task 036) ────────────────────────────────
    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> InvalidateCacheAsync(
        string? entityLogicalName,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var tenantId = GetTenantId();

        // Single-entity path: targeted invalidation. We invalidate regardless of
        // whether the entity is in the tracked set — the operator may know about a
        // cache entry the service never populated (e.g., set by an older process
        // instance that wrote to the same Redis instance). Returning the requested
        // entity-type lets the admin response show what was acted on.
        if (!string.IsNullOrWhiteSpace(entityLogicalName))
        {
            var normalized = entityLogicalName.Trim().ToLowerInvariant();

            try
            {
                await _cache.RemoveAsync(tenantId, CacheResource, normalized, CacheVersion, ct: ct)
                    .ConfigureAwait(false);
                _populatedEntityKeys.TryRemove(normalized, out _);
                _logger.LogInformation(
                    "MembershipFieldDiscoveryService invalidated cache for entity={EntityType}",
                    normalized);
                return new[] { normalized };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "MembershipFieldDiscoveryService failed to invalidate cache for entity={EntityType}; " +
                    "next call will re-resolve (no functional impact)",
                    normalized);
                // Still report the entity as "refreshed" — the next DiscoverAsync()
                // for it will go to live metadata regardless of whether the explicit
                // RemoveAsync succeeded (worst case: a stale entry expires per TTL).
                return new[] { normalized };
            }
        }

        // Refresh-all path: enumerate everything this service has populated since
        // process start. Snapshot the keys first to avoid mutation during enumeration.
        var keysToInvalidate = _populatedEntityKeys.Keys.ToArray();
        if (keysToInvalidate.Length == 0)
        {
            _logger.LogInformation(
                "MembershipFieldDiscoveryService refresh-all invoked but no discovery cache entries are tracked (cold process or already invalidated)");
            return Array.Empty<string>();
        }

        var invalidated = new List<string>(keysToInvalidate.Length);
        foreach (var entityType in keysToInvalidate)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _cache.RemoveAsync(tenantId, CacheResource, entityType, CacheVersion, ct: ct)
                    .ConfigureAwait(false);
                _populatedEntityKeys.TryRemove(entityType, out _);
                invalidated.Add(entityType);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "MembershipFieldDiscoveryService failed to invalidate cache for entity={EntityType} during refresh-all; " +
                    "continuing with remaining entries",
                    entityType);
                // Still include it in the invalidated list — see single-entity rationale.
                invalidated.Add(entityType);
            }
        }

        _logger.LogInformation(
            "MembershipFieldDiscoveryService refresh-all invalidated {Count} cache entr(ies)",
            invalidated.Count);
        return invalidated;
    }

    // ── R3 Part 1D — transitive Lookup discovery (task 054) ─────────────────
    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> DiscoverLookupsTargetingAsync(
        string sourceEntity,
        string targetEntity,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceEntity))
        {
            throw new ArgumentException(
                "sourceEntity must not be null, empty, or whitespace.",
                nameof(sourceEntity));
        }
        if (string.IsNullOrWhiteSpace(targetEntity))
        {
            throw new ArgumentException(
                "targetEntity must not be null, empty, or whitespace.",
                nameof(targetEntity));
        }

        ct.ThrowIfCancellationRequested();

        var normalizedSource = sourceEntity.Trim().ToLowerInvariant();
        var normalizedTarget = targetEntity.Trim().ToLowerInvariant();

        // Re-use the metadata-fetch seam. Per-call cost is one metadata fetch
        // for sourceEntity; production Dataverse cache (60-min TTL within the
        // SDK ServiceClient) keeps repeated calls inexpensive. We intentionally
        // do NOT add a dedicated Redis cache for this method — the existing
        // discovery cache covers same-entity calls; transitive callers run
        // immediately after the primary DiscoverAsync() so the metadata for
        // sourceEntity is typically already warmed in the SDK layer.
        var lookups = await FetchLookupAttributesAsync(normalizedSource, ct)
            .ConfigureAwait(false);

        var matches = new List<string>();
        foreach (var lookup in lookups)
        {
            if (string.IsNullOrWhiteSpace(lookup.LogicalName))
            {
                continue;
            }
            if (lookup.Targets is null || lookup.Targets.Count == 0)
            {
                continue;
            }
            foreach (var target in lookup.Targets)
            {
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }
                if (string.Equals(target.Trim(), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(lookup.LogicalName.Trim().ToLowerInvariant());
                    break;
                }
            }
        }

        // Stable ordering — deterministic FetchXml emitted by callers.
        matches.Sort(StringComparer.Ordinal);
        return matches;
    }

    /// <summary>
    /// Internal projection of a Dataverse <see cref="LookupAttributeMetadata"/>
    /// down to just the fields the discovery algorithm needs. Decoupling from
    /// the SDK type makes unit-test subclasses much easier — they can build
    /// <see cref="LookupAttributeRow"/> instances directly without constructing
    /// (sealed/internal-init) SDK metadata objects.
    /// </summary>
    /// <param name="LogicalName">Dataverse logical attribute name (lowercase).</param>
    /// <param name="Targets">
    /// The lookup's target entity logical names. May be empty for system /
    /// virtual lookups; the classifier treats empty Targets as
    /// "target-table-not-in-identity-list".
    /// </param>
    protected internal sealed record LookupAttributeRow(
        string LogicalName,
        IReadOnlyList<string> Targets);
}
