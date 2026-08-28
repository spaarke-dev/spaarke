namespace Spaarke.Dataverse;

/// <summary>
/// Data source for querying user access permissions.
/// Abstraction over authorization backends (Dataverse, SPE, Azure AD, etc.).
/// </summary>
public interface IAccessDataSource
{
    /// <summary>
    /// Gets user access permissions for a specific resource.
    /// </summary>
    /// <param name="userId">Azure AD Object ID (oid claim) of the user</param>
    /// <param name="resourceId">ID of the resource (e.g., document GUID)</param>
    /// <param name="userAccessToken">Optional user bearer token for OBO authentication. If null, uses service principal (app-only) authentication.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>AccessSnapshot with user's permissions</returns>
    /// <remarks>
    /// When userAccessToken is provided, the implementation should use On-Behalf-Of (OBO) flow
    /// to call the authorization backend as the user. This ensures permissions reflect the actual
    /// user's access, not the service principal's access.
    ///
    /// When userAccessToken is null, the implementation should use service principal (app-only)
    /// authentication. This is appropriate for background jobs, admin operations, or scenarios
    /// where no user context is available.
    /// </remarks>
    Task<AccessSnapshot> GetUserAccessAsync(
        string userId,
        string resourceId,
        string? userAccessToken = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the caller's access rights on a record of an ARBITRARY entity type — the same question
    /// <see cref="GetUserAccessAsync"/> answers, but not restricted to <c>sprk_document</c>.
    /// </summary>
    /// <param name="userId">Azure AD Object ID (<c>oid</c> claim) of the caller.</param>
    /// <param name="entitySetName">
    /// The Dataverse <b>entity set</b> (plural collection) name of the target record — e.g.
    /// <c>sprk_matters</c>, <c>sprk_projects</c>. Callers MUST pass a value from an explicit
    /// allow-list; this method does not pluralize, guess, or validate a singular logical name.
    /// </param>
    /// <param name="recordId">The target record's id.</param>
    /// <param name="userAccessToken">
    /// The caller's bearer token. Deliberately has <b>no default</b> — see the remarks.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The caller's snapshot for that record, or a snapshot carrying <see cref="AccessRights.None"/>
    /// when access could not be established. Never an app-only snapshot.
    /// </returns>
    /// <remarks>
    /// <para><b>Why this exists (unified-access-control-r2 task 070).</b>
    /// <see cref="GetUserAccessAsync"/> is document-only by construction: the implementation's
    /// <c>RetrievePrincipalAccess</c> call hard-codes its target as <c>sprk_documents({id})</c>. So the
    /// canonical seam could not answer "may this caller read this <i>matter</i>?" — which is precisely
    /// the question <c>POST /api/ai/search</c> with <c>scope=entity</c> must ask before returning that
    /// matter's documents. Asking it is what makes "access flows from the parent" enforceable.</para>
    ///
    /// <para><b>Why additive rather than a new parameter on the existing method.</b> Threading an entity
    /// type through <see cref="GetUserAccessAsync"/> would touch every
    /// <c>AuthorizationContext</c> construction site plus both implementations, and would change the
    /// meaning of <c>CachedAccessDataSource</c>'s <c>(userId, resourceId)</c> cache key — under which a
    /// document's cached snapshot could answer for a record of a different type. Adding a sibling
    /// method leaves all existing callers, and that cache key, untouched.</para>
    ///
    /// <para><b>Same policy, not a second one.</b> Implementations MUST answer from Dataverse's own
    /// <c>RetrievePrincipalAccess</c>, evaluated AS THE CALLER via the supplied token — the same
    /// authority <see cref="GetUserAccessAsync"/> uses. This is a widening of that policy's reach, not
    /// an alternative policy.</para>
    ///
    /// <para><b>Fail closed.</b> An absent token, an unresolvable caller, or any error yields
    /// <see cref="AccessRights.None"/>. Never fall back to app-only evaluation: on BFF-served surfaces
    /// reads are app-only, so Dataverse row-level security is inert and app-only answers "yes" — the
    /// disclosure this method exists to prevent (finding A-2).</para>
    /// </remarks>
    Task<AccessSnapshot> GetRecordAccessAsync(
        string userId,
        string entitySetName,
        Guid recordId,
        string? userAccessToken,
        CancellationToken ct = default);
}

/// <summary>
/// Snapshot of user access permissions for a specific resource.
/// Captures granular Dataverse permissions and organizational context.
/// </summary>
public class AccessSnapshot
{
    public required string UserId { get; init; }
    public required string ResourceId { get; init; }

    /// <summary>
    /// Granular access rights mapped from Dataverse permissions.
    /// Uses [Flags] pattern to support multiple simultaneous permissions.
    /// </summary>
    public AccessRights AccessRights { get; init; }

    public IEnumerable<string> TeamMemberships { get; init; } = Array.Empty<string>();
    public IEnumerable<string> Roles { get; init; } = Array.Empty<string>();
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Granular access rights matching Dataverse permission model.
/// Uses [Flags] pattern for bitwise combination of permissions.
/// Maps directly to Dataverse RetrievePrincipalAccess response.
/// </summary>
/// <remarks>
/// Dataverse Permission Mapping:
/// - ReadAccess → Read (can view record)
/// - WriteAccess → Write (can update record)
/// - DeleteAccess → Delete (can delete record)
/// - CreateAccess → Create (can create new records)
/// - AppendAccess → Append (can attach to other records)
/// - AppendToAccess → AppendTo (other records can attach to this)
/// - ShareAccess → Share (can share with others)
///
/// Example: User with "ReadAccess,WriteAccess,DeleteAccess" gets:
/// AccessRights.Read | AccessRights.Write | AccessRights.Delete
/// </remarks>
[Flags]
public enum AccessRights
{
    /// <summary>No access permissions</summary>
    None = 0,        // 0000000 - No access

    /// <summary>Can view/read the resource (preview only)</summary>
    Read = 1 << 0,   // 0000001 - Bit 0

    /// <summary>Can update/modify the resource (includes download for files)</summary>
    Write = 1 << 1,   // 0000010 - Bit 1

    /// <summary>Can delete the resource</summary>
    Delete = 1 << 2,   // 0000100 - Bit 2

    /// <summary>Can create new records of this type</summary>
    Create = 1 << 3,   // 0001000 - Bit 3

    /// <summary>Can attach this record to other records</summary>
    Append = 1 << 4,   // 0010000 - Bit 4

    /// <summary>Other records can be attached to this record</summary>
    AppendTo = 1 << 5,   // 0100000 - Bit 5

    /// <summary>Can share this record with other users</summary>
    Share = 1 << 6    // 1000000 - Bit 6
}
