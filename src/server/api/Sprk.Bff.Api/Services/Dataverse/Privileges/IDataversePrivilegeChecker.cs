namespace Sprk.Bff.Api.Services.Dataverse.Privileges;

/// <summary>
/// Cached privilege checker for Dataverse entity-level Read privileges.
/// </summary>
/// <remarks>
/// <para>
/// Implementations cache the caller's full effective privilege set keyed by Azure AD object id
/// (the token <c>oid</c> claim). Per task 010 design (Q1 resolution), the cache uses a
/// 6-hour sliding TTL with a 24-hour absolute maximum and is backed by the shared
/// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/> (Redis in production).
/// </para>
/// <para>
/// The implementation calls Dataverse <c>RetrieveUserPrivileges</c> via app-only
/// <c>ServiceClient</c> with <c>CallerId</c> impersonation (NOT OBO) — matching the
/// <see cref="Spaarke.Dataverse.DataverseAccessDataSource"/> precedent and avoiding per-user
/// OBO token-cache pressure on the privilege-check path.
/// </para>
/// <para>
/// Consumed by <c>DataverseAuthorizationFilter</c> (single privilege checks) and the
/// FetchXML endpoint filter path (batch checks via <see cref="GetReadableEntitiesAsync"/>).
/// </para>
/// <para>
/// unified-access-control-r2 task 099b review round 2: also answers whether an OWNER TEAM holds Read on a table
/// (<see cref="TeamHasReadPrivilegeAsync"/>) — the grant routes and the secure-transition child pass ask before a row of
/// <c>sprk_externalrecordaccess</c> is created for, or assigned to, a team. Public (was internal) because the Assigned-To
/// materializer and the secure child reconciler, public types with public constructors, take it.
/// </para>
/// </remarks>
public interface IDataversePrivilegeChecker
{
    /// <summary>
    /// Returns <c>true</c> if the caller has Read privilege on the specified entity.
    /// </summary>
    /// <param name="userOid">Azure AD object id of the caller (from <c>oid</c> claim).</param>
    /// <param name="entityLogicalName">Logical name of the Dataverse entity (e.g., <c>sprk_matter</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if Read privilege is granted; <c>false</c> if denied or entity is unknown.</returns>
    /// <remarks>
    /// Cache miss triggers a single Dataverse round-trip that hydrates the user's full privilege set.
    /// Subsequent calls for any entity within the TTL are served in-process.
    /// On Dataverse failure the implementation MUST fail closed (return <c>false</c>) and log a warning.
    /// </remarks>
    Task<bool> HasReadPrivilegeAsync(Guid userOid, string entityLogicalName, CancellationToken ct);

    /// <summary>
    /// Returns the set of entity logical names the caller has Read privilege on, as a case-insensitive set.
    /// </summary>
    /// <param name="userOid">Azure AD object id of the caller.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A case-insensitive set of entity logical names with Read privilege.
    /// Returns an empty set on fail-closed (Dataverse unreachable, no privileges, unknown user).
    /// </returns>
    /// <remarks>
    /// Used by the FetchXML cross-entity check path to perform a single privilege fetch and then
    /// validate every link-entity in-process via set membership — avoids N round-trips for queries
    /// referencing many entities.
    /// </remarks>
    Task<IReadOnlySet<string>> GetReadableEntitiesAsync(Guid userOid, CancellationToken ct);

    /// <summary>
    /// Task 099b review round 2: whether the TEAM <paramref name="teamId"/> holds Read (at any depth) on
    /// <paramref name="entityLogicalName"/> through its own security roles — Dataverse's "Read Privilege Check For Owner": a
    /// team without it cannot own a row of that table.
    /// </summary>
    /// <returns><c>true</c> when one of the team's roles grants Read on the table; <c>false</c> when none does.</returns>
    /// <param name="teamId">The team asked about.</param>
    /// <param name="entityLogicalName">The table.</param>
    /// <param name="fresh">
    /// <c>true</c> reads Dataverse even when a remembered YES exists — for a caller that must not act on a privilege an
    /// administrator has since removed (the grant core's classification of a refused create, review round 3, K2).
    /// <c>false</c> may be answered YES from a short-lived per-team cache; a NO is never answered from it.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="Exception">
    /// UNLIKE the user methods, a Dataverse fault is NOT answered <c>false</c>: it propagates, so a caller can tell "the team
    /// lacks the privilege" from "it could not be read" (503) — and a fault is never cached. Only a YES is cached, per team,
    /// for a short time, so an administrator's fix is seen on the next question.
    /// </exception>
    /// <remarks>App-only: <c>RetrieveTeamPrivileges</c> through the same application <c>ServiceClient</c> the ownership
    /// resolver reads through; no impersonation.</remarks>
    Task<bool> TeamHasReadPrivilegeAsync(Guid teamId, string entityLogicalName, bool fresh, CancellationToken ct);
}
