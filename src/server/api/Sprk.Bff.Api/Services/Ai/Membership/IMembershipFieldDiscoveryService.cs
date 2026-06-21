// R3 Part 1 — User-Record Membership Resolution (discovery contract)
// Task 030 (2026-06-21): Public contract for the metadata-driven Lookup-field
// discovery service. Queries Dataverse EntityDefinitions API for an entity,
// filters its Lookup attributes to those whose Targets[] include a configured
// identity table, applies global + per-entity exclusions/overrides, derives
// role names via the configured strategy, and returns:
//
//   DiscoveredFields  — descriptors for fields that resolved to a known
//                       identity table (the actionable result)
//   ExcludedFields    — fields dropped because they matched a global
//                       exclusion list entry OR a per-entity ExcludedFields
//                       entry. Returned so operators can audit via the
//                       /api/admin/membership/discovered/{entityType} endpoint
//                       (task 036).
//   IgnoredFields     — Lookup fields whose target tables are NOT in the
//                       configured IncludedIdentityTables list. Useful for
//                       operator visibility (e.g., sprk_chartdefinition
//                       lookups on a custom entity).
//
// Per ADR-010 the interface exists as a testing seam — consumers
// (MembershipResolverService — task 033, MembershipAdminEndpoints — task 036)
// get the concrete via DI; unit tests substitute a mock.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1A.1
//            through FR-1A.4, FR-1A.7; design.md Part 1 §
//            "Discovery algorithm" + "Discovery Report endpoint".

using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Discovers the set of Lookup fields on a Dataverse entity that target a
/// configured person/organization identity table, applies configured
/// exclusions + overrides, derives role names, and caches the result per
/// entity type. Cache TTL configurable via
/// <c>MembershipOptions.MetadataCacheTtlMinutes</c> (default 60 minutes
/// per ADR-009).
/// </summary>
public interface IMembershipFieldDiscoveryService
{
    /// <summary>
    /// Discovers the membership-bearing Lookup fields for the requested entity.
    /// Cache hit returns immediately; cache miss issues a single Dataverse
    /// <c>RetrieveEntityRequest</c> for the entity's attributes metadata, then
    /// filters + maps according to the configured options.
    /// </summary>
    /// <param name="entityLogicalName">
    /// Dataverse entity logical name (case-insensitive — normalized to lowercase
    /// for cache-key consistency). MUST NOT be null/empty/whitespace.
    /// </param>
    /// <param name="ct">Cancellation token; honored across the metadata fetch.</param>
    /// <returns>
    /// A <see cref="DiscoveryResult"/> with the discovered, excluded, and
    /// ignored field sets. Always non-null. <see cref="DiscoveryResult.EntityType"/>
    /// equals the lowercased input.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="entityLogicalName"/> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the entity is not found in Dataverse metadata.
    /// </exception>
    Task<DiscoveryResult> DiscoverAsync(string entityLogicalName, CancellationToken ct);

    /// <summary>
    /// Invalidates the per-entity discovery cache for the
    /// <c>POST /api/admin/membership/refresh-metadata</c> admin endpoint
    /// (task 036). When <paramref name="entityLogicalName"/> is non-null/empty,
    /// invalidates only that entity's cached <see cref="DiscoveryResult"/>.
    /// When null/empty/whitespace, invalidates every entity-type cache entry
    /// the service has populated since process start (tracked internally via a
    /// thread-safe set of populated cache keys — works against both Redis and
    /// the in-memory distributed-cache fallback, neither of which exposes a
    /// portable "scan by prefix" API). The returned list contains the
    /// lowercase-normalized entity-type strings that were actually invalidated
    /// (after de-duplication), in arbitrary order.
    /// </summary>
    /// <param name="entityLogicalName">
    /// Optional. Single entity to invalidate (case-insensitive — normalized to
    /// lowercase to match <see cref="DiscoverAsync"/>'s cache-key convention).
    /// Null/empty/whitespace means "invalidate all known entries".
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The lowercase-normalized entity types that were invalidated. Empty when
    /// no matching cache entries had been populated yet (e.g., admin calls
    /// refresh-all on a cold process). Always non-null.
    /// </returns>
    Task<IReadOnlyList<string>> InvalidateCacheAsync(string? entityLogicalName, CancellationToken ct);
}

/// <summary>
/// Outcome of a single discovery pass for an entity. Mirrors the
/// <c>/api/admin/membership/discovered/{entityType}</c> response shape from
/// design.md Part 1 § "Discovery Report endpoint" so the admin endpoint
/// (task 036) can return this record directly.
/// </summary>
/// <param name="EntityType">Lowercased Dataverse entity logical name.</param>
/// <param name="DiscoveredAt">UTC timestamp at which discovery executed (per request).</param>
/// <param name="DiscoveredFields">
/// Ordered list of fields that resolved to a configured identity table.
/// Stable ordering by <c>Field</c> ascending to keep the admin-endpoint
/// response deterministic across cache hits.
/// </param>
/// <param name="ExcludedFields">
/// Fields removed by configured exclusions (global or per-entity).
/// </param>
/// <param name="IgnoredFields">
/// Lookup fields whose target tables are not in the configured identity-table list.
/// </param>
public sealed record DiscoveryResult(
    string EntityType,
    DateTimeOffset DiscoveredAt,
    IReadOnlyList<MembershipDescriptor> DiscoveredFields,
    IReadOnlyList<IgnoredField> ExcludedFields,
    IReadOnlyList<IgnoredField> IgnoredFields);

/// <summary>
/// A Lookup field that was either explicitly excluded by configuration or
/// implicitly ignored because its target table is not a configured identity
/// table. Returned to operators for audit + override-tuning purposes.
/// </summary>
/// <param name="Field">Dataverse logical attribute name.</param>
/// <param name="Reason">
/// Short tag describing why the field was excluded/ignored. One of:
/// <c>"global-exclusion"</c>, <c>"per-entity-exclusion"</c>,
/// <c>"target-table-not-in-identity-list"</c>.
/// </param>
/// <param name="Target">
/// Optional — the lookup's target table (when available). Populated for
/// <c>target-table-not-in-identity-list</c> so operators can see WHAT the
/// lookup was pointing at.
/// </param>
public sealed record IgnoredField(string Field, string Reason, string? Target = null);
