using System.Security.Claims;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Resolves the caller's memory-governance authorization context (task AIR2-052, FR-B-03) — the
/// STRUCTURAL alignment between memory access and the caller's OWN Dataverse access. There is NO
/// parallel memory ACL (operator ruling 2026-07-09): both methods derive from the caller's identity
/// and Dataverse permissions, so memory access can never diverge from record access.
///
/// <para>
/// This is a thin authorization PORT over existing BFF machinery
/// (<c>IDataversePrivilegeChecker</c> + <c>NotificationService.ResolveSystemUserIdAsync</c>), extracted
/// as a testable seam (ADR-010 — interface allowed when a testing seam is needed) so the endpoint
/// allow/deny + user-subject-resolution logic is unit-testable without a live Dataverse. It does NOT
/// introduce a new permission store.
/// </para>
/// </summary>
public interface IMemoryAccessAuthorizer
{
    /// <summary>
    /// True iff the caller may read RECORD memory for <c>(entityLogicalName, recordId)</c> — aligned to
    /// the caller's own Dataverse read access (FR-B-03). A caller who cannot read the record's entity
    /// gets <see langword="false"/>, so they can never read that record's memory.
    /// </summary>
    /// <remarks>
    /// GRANULARITY (documented deferral 2026-07-09): this evaluates the caller's <b>entity-type Read
    /// privilege</b> via the impersonated <c>RetrieveUserPrivileges</c> path — the only GENERIC,
    /// reliable, caller-derived Dataverse authorization primitive in the BFF today (per-record OBO
    /// access checks are wired only for <c>sprk_documents</c>). True per-<b>row</b> ethical-wall
    /// enforcement is a hard-governance rule DEFERRED to the governance project. The signature already
    /// carries <paramref name="recordId"/> so a future row-level OBO implementation is a drop-in
    /// replacement with no caller change.
    /// </remarks>
    Task<bool> CanCallerReadRecordAsync(ClaimsPrincipal caller, string entityLogicalName, Guid recordId, CancellationToken ct);

    /// <summary>
    /// Resolves the caller's canonical USER-scope memory subject key — the Dataverse
    /// <c>systemuserid</c> (operator ruling 2026-07-09 (c)) — from the token's AAD <c>oid</c> claim
    /// via the existing ADR-028 one-hop cross-reference. Returns <see langword="null"/> when the
    /// caller has no <c>oid</c> claim or no matching Dataverse user.
    /// </summary>
    Task<Guid?> ResolveCallerUserSubjectAsync(ClaimsPrincipal caller, CancellationToken ct);
}
