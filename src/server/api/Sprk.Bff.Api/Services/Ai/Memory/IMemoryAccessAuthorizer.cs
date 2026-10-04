using System.Security.Claims;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Resolves the caller's memory-governance subject (task AIR2-052, FR-B-03). There is NO parallel memory
/// ACL (operator ruling 2026-07-09): the user subject derives from the caller's own identity, so a caller
/// can only ever reach their OWN User-scope memory.
///
/// <para>
/// This is a thin PORT over existing BFF machinery (<c>NotificationService.ResolveSystemUserIdAsync</c>),
/// extracted as a testable seam (ADR-010 — interface allowed when a testing seam is needed) so the
/// endpoint's user-subject resolution is unit-testable without a live Dataverse. It does NOT introduce a
/// new permission store.
/// </para>
///
/// <para><b><c>CanCallerReadRecordAsync</c> was DELETED 2026-10-03</b> (unified-access-control-r2 task 166)
/// together with its only caller, <c>GET /api/memory/records/{entityLogicalName}/{id:guid}</c>. It answered
/// "may this caller read this RECORD's memory" with an ENTITY-TYPE Read privilege and ignored the record id,
/// which is a table check standing in for a row check — any caller with Read on <c>sprk_matter</c> passed
/// for every matter, secure ones included. A record-memory read that needs authorizing must ask the
/// caller's own rights on the exact record (<c>CallerRecordAccessProbe</c> /
/// <c>RecordRouteAccessAuthorizationFilter</c>), not reintroduce a table check here.</para>
/// </summary>
public interface IMemoryAccessAuthorizer
{
    /// <summary>
    /// Resolves the caller's canonical USER-scope memory subject key — the Dataverse
    /// <c>systemuserid</c> (operator ruling 2026-07-09 (c)) — from the token's AAD <c>oid</c> claim
    /// via the existing ADR-028 one-hop cross-reference. Returns <see langword="null"/> when the
    /// caller has no <c>oid</c> claim or no matching Dataverse user.
    /// </summary>
    Task<Guid?> ResolveCallerUserSubjectAsync(ClaimsPrincipal caller, CancellationToken ct);
}
