// R3 Part 1 — User-Record Membership Resolution (response DTO)
// Task 034 (2026-06-21): The HTTP response shape returned by
// GET /api/users/me/memberships/{entityType} (FR-1A.9) — the membership
// endpoint authored by task 035. Locked per design.md Part 1 §
// Endpoint contract response JSON:
//
//   {
//     "entityType": "sprk_matter",
//     "personIdentity": { "systemUserId": "...", ... },
//     "ids": ["matter-guid-1", "matter-guid-2", ...],
//     "byRole": {
//       "owner": ["matter-guid-1"],
//       "owningTeam": ["matter-guid-1", "matter-guid-2"],
//       "assignedAttorney": ["matter-guid-3"],
//       ...
//     },
//     "count": 47,
//     "cacheExpiresAt": "2026-06-20T15:34:00Z",
//     "continuationToken": null
//   }
//
// Serialization contract:
//   - camelCase property names via JsonPropertyName attributes (independent of
//     the host's JsonSerializerOptions configuration so the contract is stable
//     even when MembershipResponse is serialized in a non-default context such
//     as a test or internal logger)
//   - Guids serialize as the System.Text.Json default GUID string form
//     ("D" — 32 hex digits with hyphens, no braces)
//   - DateTimeOffset serializes as ISO 8601 with timezone offset (the
//     System.Text.Json default round-trip "O" form)
//   - ContinuationToken is nullable and SHOULD round-trip as JSON null when
//     not provided (the design example shows `"continuationToken": null`).
//     We do NOT apply [JsonIgnore(WhenWritingNull)] so the property is always
//     emitted, matching the documented shape.
//
// Coordination with task 033 (MembershipResolverService, parallel group H):
//   At authoring time (2026-06-21) task 033 had NOT yet created this file,
//   so no reconciliation was required. The orchestration service authored by
//   task 033 is expected to construct MembershipResponse using this record
//   as the authoritative shape.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1A.9;
//            projects/spaarke-platform-foundations-r3/design.md Part 1 §
//            Endpoint contract; sibling record PersonIdentity.cs (task 031).

using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Membership.Models;

/// <summary>
/// The HTTP response payload for
/// <c>GET /api/users/me/memberships/{entityType}</c>. Returned to clients
/// as JSON with camelCase property names per the design.md endpoint contract.
/// </summary>
/// <param name="EntityType">
/// The Dataverse entity logical name the caller is enumerating memberships
/// for (e.g., <c>"sprk_matter"</c>). Echoes the route parameter so clients
/// can correlate responses with concurrent requests for different entity types.
/// </param>
/// <param name="PersonIdentity">
/// The full normalized identity that drove the resolution (resolved by
/// <see cref="IIdentityNormalizationService"/> — task 031). Included in the
/// response so clients and downstream node executors can introspect which
/// identity paths were considered without re-querying. Always populated.
/// </param>
/// <param name="Ids">
/// The de-duplicated, role-agnostic flat list of entity-instance ids the
/// caller has membership in (union of all roles in <see cref="ByRole"/>).
/// Optimized for the common consumer that only needs "the matters I'm on"
/// without per-role attribution. Always non-null; empty list when no
/// memberships resolve.
/// </param>
/// <param name="ByRole">
/// Per-role attribution map: role name (e.g., <c>"owner"</c>,
/// <c>"owningTeam"</c>, <c>"assignedAttorney"</c>, <c>"assignedLawFirm"</c>)
/// mapped to the entity-instance ids the user has that role on. Role names
/// follow the <see cref="MembershipOptions.RoleNameStrategy"/> resolution
/// (default <c>CamelCase</c>: <c>sprk_AssignedAttorney1</c> →
/// <c>assignedAttorney</c> per FR-1A.4). Empty lists for roles with no
/// matches MAY be present (helps the caller distinguish "role was queried,
/// no matches" from "role was not in the query"). Always non-null.
/// </param>
/// <param name="Count">
/// The cardinality of <see cref="Ids"/>. Provided as a separate field so
/// clients can branch on emptiness without materializing the list (relevant
/// for streaming/SSE consumers).
/// </param>
/// <param name="CacheExpiresAt">
/// The UTC timestamp at which the cached membership result for this user +
/// entityType combination will expire (per the 5-min per-user TTL in
/// FR-1A.8). Clients MAY use this to schedule revalidation. ISO 8601 with
/// timezone offset.
/// </param>
/// <param name="ContinuationToken">
/// Opaque pagination token. <c>null</c> when there are no further results
/// or when the result set fits within the requested <c>limit</c>. The
/// design example shows <c>"continuationToken": null</c> — we preserve that
/// shape by NOT applying <c>JsonIgnore(WhenWritingNull)</c>.
/// </param>
/// <param name="RelatedByRole">
/// R3 Part 1D / FR-1D.3 — per-related-entity nested role attribution map.
/// Outer key: related entity logical name (e.g., <c>"sprk_document"</c>,
/// <c>"sprk_event"</c>). Inner key: role on the related entity (e.g.,
/// <c>"owner"</c>, <c>"matter"</c>). Inner value: entity-instance ids of the
/// related entity that the user has that role on, reached transitively via
/// 1 hop from the primary entity. <c>null</c> (and omitted from JSON) when
/// the caller did NOT request <c>includeRelated</c>; populated (possibly with
/// empty inner maps) when the caller did request transitive expansion. The
/// 1-hop max enforced by spec.md FR-1D.2 (per owner clarification Q3,
/// 2026-06-20) guarantees this is at most one nesting level deep — deeper
/// requests are rejected with 400 BadRequest.
/// </param>
public sealed record MembershipResponse(
    [property: JsonPropertyName("entityType")] string EntityType,
    [property: JsonPropertyName("personIdentity")] PersonIdentity PersonIdentity,
    [property: JsonPropertyName("ids")] IReadOnlyList<Guid> Ids,
    [property: JsonPropertyName("byRole")] IReadOnlyDictionary<string, IReadOnlyList<Guid>> ByRole,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("cacheExpiresAt")] DateTimeOffset CacheExpiresAt,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken = null,
    [property: JsonPropertyName("relatedByRole"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Guid>>>? RelatedByRole = null);
