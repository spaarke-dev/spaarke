namespace Sprk.Bff.Api.Infrastructure.Authentication;

/// <summary>
/// Canonical names for non-default authentication schemes registered in
/// <c>AuthorizationModule.AddAuthorizationModule</c> (task AUTHV2-045).
/// Centralizing the strings prevents typos between scheme registration, policy wiring, and
/// endpoint <c>.RequireAuthorization</c> calls.
/// </summary>
public static class AuthSchemes
{
    // BuilderAdminApiKey scheme removed 2026-07-07 (redesign-r1 task 050) with the
    // /api/admin/builder-scopes/* endpoints (AiPlaybookBuilder estate retirement).

    /// <summary>
    /// API key scheme guarding RAG bulk indexing webhook endpoint
    /// (<c>/api/ai/rag/enqueue-indexing</c>).
    /// Backed by configuration key <c>Rag:ApiKey</c>.
    /// </summary>
    public const string RagApiKey = "RagApiKey";

    /// <summary>
    /// JwtBearer scheme validating Entra External ID (CIAM) tokens for external users
    /// (external-access-platform-r1 task 020 · ADR-028 Amendment A1). Authority is the
    /// CIAM tenant (<c>*.ciamlogin.com</c>) — a distinct issuer from the workforce
    /// <see cref="Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme"/>.
    /// Appended to the existing workforce authentication builder, so it does NOT change the
    /// default scheme. Pinned onto the <c>/api/v1/external</c> endpoint group in task 021.
    /// </summary>
    public const string Ciam = "Ciam";

    /// <summary>
    /// JwtBearer scheme validating a workforce contact's token issued by their CUSTOMER'S HOME DIRECTORY
    /// (ADR-028 Amendment A9 · external-access-platform-r3 task 045). Multitenant signing metadata
    /// (<c>organizations</c>) on the stamp's own BFF app id, then the A9 allowlist (<c>tid</c> listed in
    /// <c>WorkforceIdentity:CustomerTenantIds</c>, exact v2 <c>iss</c>, user token with
    /// <c>user_impersonation</c>, <c>acct</c> = 0). Named by the <see cref="AuthPolicies.ExternalCollaboration"/>
    /// policy and by NO other (no OBO on the external plane); <c>AuthenticationSchemeCensusTests</c> pins that.
    /// The <see cref="System.Security.Claims.ClaimsIdentity.AuthenticationType"/> of the identity it produces is
    /// this name, so principal resolution can branch on the authenticating scheme rather than on <c>iss</c>.
    /// </summary>
    public const string WorkforceHome = "WorkforceHome";
}
