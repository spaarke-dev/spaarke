using System.Security.Claims;

namespace Sprk.Bff.Api.Services.Ai.Security;

/// <summary>
/// Resolves the Azure AD group IDs that the calling user belongs to.
/// Used by privilege-aware RAG retrieval to build the privilege_group_ids OData security filter.
/// </summary>
/// <remarks>
/// Resolution strategy (in order):
/// 1. Read from JWT "groups" claim if present and not in overage state.
/// 2. If groups claim is absent or the token is in overage (200+ groups), call
///    Microsoft Graph GET /me/memberOf to resolve transitively.
/// Results are cached in IMemoryCache per user OID for 5 minutes (ADR-009).
///
/// Fail-closed contract: callers MUST treat an empty return list as "no access" —
/// they must NOT fall back to unfiltered search when group resolution fails.
/// </remarks>
public interface IPrivilegeGroupResolver
{
    /// <summary>
    /// Resolve the Azure AD group IDs the user belongs to.
    /// Returns an empty list when the user has no group memberships or when
    /// group resolution fails (fail-closed — callers must not issue unfiltered queries).
    /// </summary>
    /// <param name="user">The <see cref="ClaimsPrincipal"/> from the current HTTP request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A read-only list of Azure AD group object IDs (GUID strings).
    /// Empty when the user has no groups or when a resolution error occurs.
    /// </returns>
    Task<IReadOnlyList<string>> ResolveGroupIdsAsync(ClaimsPrincipal user, CancellationToken ct = default);
}
