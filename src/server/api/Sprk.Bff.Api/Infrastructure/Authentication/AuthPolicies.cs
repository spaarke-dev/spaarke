namespace Sprk.Bff.Api.Infrastructure.Authentication;

/// <summary>
/// Canonical names for authorization policies that bind to non-default authentication schemes
/// (task AUTHV2-045). Used by endpoints via <c>.RequireAuthorization(AuthPolicies.X)</c>.
/// </summary>
public static class AuthPolicies
{
    // BuilderAdminApiKey + BuilderAdminOrOAuth policies removed 2026-07-07 (redesign-r1
    // task 050) with the /api/admin/builder-scopes/* endpoints (AiPlaybookBuilder estate).

    /// <summary>
    /// API-key-only policy for the RAG scheme. Use for webhook/background-job indexing
    /// endpoints that must be invoked by automation only.
    /// </summary>
    public const string RagApiKey = "RagApiKey";

    /// <summary>
    /// CIAM-only policy for the external Secure Project Workspace surface (task 021 · ADR-028
    /// Amendment A1). Binds the "Ciam" JwtBearer scheme (<see cref="AuthSchemes.Ciam"/>, task 020)
    /// so ONLY Entra External ID (CIAM) tokens authenticate on the <c>/api/v1/external</c> group;
    /// a workforce (default-scheme) token does NOT authenticate there. Pinned via
    /// <c>.RequireAuthorization(AuthPolicies.CiamExternal)</c>. The internal
    /// <c>/api/v1/external-access</c> management group stays on the workforce default scheme.
    /// </summary>
    /// <remarks>
    /// Superseded on the <c>/api/v1/external</c> collaboration group by
    /// <see cref="ExternalCollaboration"/> (teams-app-r1 task 025 · R2 FR-22). Retained for reference
    /// / any future CIAM-pin need.
    /// </remarks>
    public const string CiamExternal = "CiamExternal";

    /// <summary>
    /// PRINCIPAL-AGNOSTIC policy for the <c>/api/v1/external</c> collaboration group (teams-app-r1
    /// task 025 · R2 FR-22 · ADR-028 A1+A2). Accepts BOTH the CIAM "Ciam" scheme
    /// (<see cref="AuthSchemes.Ciam"/>) AND the workforce default JwtBearer scheme, so a CIAM external
    /// contact (the standalone SPA) and a workforce user (the Teams host) both authenticate on ONE
    /// endpoint set. The <c>CallerPrincipalAuthorizationFilter</c> then resolves either token to a
    /// plane-agnostic principal + its Tier-2 record scope. A token validates against exactly one
    /// authority, so exactly one scheme succeeds per request; the CIAM path is unchanged (FR-15).
    /// </summary>
    public const string ExternalCollaboration = "ExternalCollaboration";
}
