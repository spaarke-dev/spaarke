using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// THE rule for a row on the external plane that can be filed to more than one core root (project, matter, work
/// assignment): it is listed to a caller only if the caller holds Read on EVERY root the row carries.
/// </summary>
/// <remarks>
/// <para><b>INTERIM main-session decision, pending the owner</b> (spaarke-SPA-external-access-platform-r3 task 039 fix
/// round). Without it a record the caller cannot read leaks through a second lookup: a to-do under an invoice of secure
/// matter M and project P is stamped with both (<c>CoreAncestorResolver</c>), so a reader of P alone was given M's to-do
/// by <c>GET /projects/P/todos</c>; the same holds for events, documents and message threads. This is the delete-own
/// rule (<c>AuthorizeDeleteOwnAsync</c>: "Read on EVERY parent") applied to the list, so one row is not readable on one
/// route and hidden on another.</para>
/// <para><b>Known cost.</b> The principal cannot tell a secure root from one that is merely not granted, so a row filed to
/// an ungranted NON-secure second root is also withheld. The owner may choose instead: <b>every-root</b> (this);
/// <b>secure-only</b> (withhold only when the unreadable root is secure; needs a secure check on each root); or
/// <b>union</b> (list when any root is readable). Every list on the plane calls <see cref="AllRootsReadable"/>, so
/// the choice is a change in this file alone.</para>
/// <para><b>Fail closed.</b> A lookup value that is not a GUID is unreadable. The rights are the principal's, through the
/// same <see cref="CallerPrincipal.RightsOn"/> dispatch the grant routes use; there is no Dataverse read.</para>
/// <para><b>Not consulted:</b> <c>sprk_regardingservicerequest</c>. A service request is reached through its requester,
/// not through the principal's root sets, and carries no secure flag; delete-own does not consult it either.</para>
/// </remarks>
internal static class ExternalMultiRootRule
{
    /// <summary>
    /// Whether the caller may see a row filed to the given root lookups: every populated lookup must be a root the caller
    /// holds Read on. A lookup that is null or blank is "not filed to this root" and is ignored; a row naming no root at
    /// all is unaffected.
    /// </summary>
    /// <param name="caller">The resolved principal.</param>
    /// <param name="roots">Each root lookup the row carries, as the raw <c>_..._value</c> GUID string.</param>
    public static bool AllRootsReadable(CallerPrincipal caller, params (ExternalGrantRootType Root, string? LookupValue)[] roots)
    {
        ArgumentNullException.ThrowIfNull(caller);
        foreach (var (root, lookupValue) in roots)
        {
            if (!RootReadable(caller, root, lookupValue))
                return false;
        }

        return true;
    }

    /// <summary>
    /// <see cref="AllRootsReadable"/> for the three stamp lookups every child row carries
    /// (<c>sprk_regardingproject</c>, <c>sprk_regardingmatter</c>, <c>sprk_regardingworkassignment</c>).
    /// </summary>
    public static bool AllRootsReadable(CallerPrincipal caller, string? project, string? matter, string? workAssignment) =>
        AllRootsReadable(
            caller,
            (ExternalGrantRootType.Project, project),
            (ExternalGrantRootType.Matter, matter),
            (ExternalGrantRootType.WorkAssignment, workAssignment));

    private static bool RootReadable(CallerPrincipal caller, ExternalGrantRootType rootType, string? lookupValue)
    {
        if (string.IsNullOrWhiteSpace(lookupValue))
            return true; // not filed to this root
        return Guid.TryParse(lookupValue, out var rootId)
               && caller.RightsOn(rootType, rootId).HasFlag(AccessRights.Read);
    }
}
