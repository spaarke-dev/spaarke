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
}
