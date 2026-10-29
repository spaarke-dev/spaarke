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

    /// <summary>
    /// The CEILING for a workforce-contact caller (unified-access-control-r2 task 186, ADR-028 A9, owner round 101 option
    /// (a)): the table × operation privileges of the Dataverse role <see cref="UserPrivilegeChecker.WorkforceRoleName"/>,
    /// read app-only with <c>RetrieveRolePrivilegesRole</c>.
    /// </summary>
    /// <remarks>
    /// <para>Depth is collapsed: a privilege held at ANY depth counts, and none means none. The ceiling never widens record
    /// scope; a caller is allowed an operation only when the ceiling holds it AND UAC's record scope allows it.</para>
    /// <para>Fail closed: a role that is missing, ambiguous (more than one root role with the name) or unreadable answers
    /// <see cref="WorkforceRoleCeiling.IsAvailable"/> = <see langword="false"/>, which denies every check. Only a READ
    /// role is cached (about 5 minutes, so the owner's privilege edits take effect within that); a fault is never
    /// cached.</para>
    /// </remarks>
    Task<WorkforceRoleCeiling> GetWorkforceRoleCeilingAsync(CancellationToken ct);
}

/// <summary>
/// The privileges of the Workforce role as a table → operations map (task 186). See
/// <see cref="IDataversePrivilegeChecker.GetWorkforceRoleCeilingAsync"/>.
/// </summary>
/// <remarks>
/// CLAUDE.md §11. <b>Existing:</b> <see cref="IDataversePrivilegeChecker"/> answers Read per systemuser only (keyed by
/// oid, impersonated); nothing reads a ROLE's privileges for a caller that is not a systemuser. <b>Extension:</b> this is
/// the extension: a value type on the same checker, not a second reader. <b>Cost of doing nothing:</b> a self-registered
/// workforce contact could create, read and delete service requests whatever the owner's role says (the owner's round-101
/// decision has no enforcement point).
/// </remarks>
public sealed class WorkforceRoleCeiling
{
    private static readonly IReadOnlyDictionary<string, Spaarke.Dataverse.AccessRights> NoPrivileges =
        new Dictionary<string, Spaarke.Dataverse.AccessRights>(StringComparer.OrdinalIgnoreCase);

    private WorkforceRoleCeiling(
        bool isAvailable, IReadOnlyDictionary<string, Spaarke.Dataverse.AccessRights> privileges, string? unavailableReason)
    {
        IsAvailable = isAvailable;
        Privileges = privileges;
        UnavailableReason = unavailableReason;
    }

    /// <summary>True when the role was read; false when it is missing, ambiguous or unreadable (deny everything).</summary>
    public bool IsAvailable { get; }

    /// <summary>Table logical name → the operations the role holds on it (any depth). Empty when unavailable.</summary>
    public IReadOnlyDictionary<string, Spaarke.Dataverse.AccessRights> Privileges { get; }

    /// <summary>Why the role is unavailable (for logs only); null when available.</summary>
    public string? UnavailableReason { get; }

    /// <summary>A role that was read. The map is copied with a case-insensitive comparer.</summary>
    public static WorkforceRoleCeiling Available(IReadOnlyDictionary<string, Spaarke.Dataverse.AccessRights> privileges)
    {
        ArgumentNullException.ThrowIfNull(privileges);
        var copy = new Dictionary<string, Spaarke.Dataverse.AccessRights>(StringComparer.OrdinalIgnoreCase);
        foreach (var (table, rights) in privileges)
            copy[table] = copy.TryGetValue(table, out var existing) ? existing | rights : rights;
        return new WorkforceRoleCeiling(true, copy, null);
    }

    /// <summary>A role that could not be used: every check against it denies.</summary>
    public static WorkforceRoleCeiling Unavailable(string reason) => new(false, NoPrivileges, reason);

    /// <summary>
    /// True only when the role was read AND holds every operation in <paramref name="required"/> on
    /// <paramref name="tableLogicalName"/>. <see cref="Spaarke.Dataverse.AccessRights.None"/> required is never "held":
    /// a check that names no operation is a programming error and fails closed.
    /// </summary>
    public bool Holds(string tableLogicalName, Spaarke.Dataverse.AccessRights required)
    {
        if (!IsAvailable || required == Spaarke.Dataverse.AccessRights.None || string.IsNullOrWhiteSpace(tableLogicalName))
            return false;

        return Privileges.TryGetValue(tableLogicalName, out var held) && (held & required) == required;
    }
}
