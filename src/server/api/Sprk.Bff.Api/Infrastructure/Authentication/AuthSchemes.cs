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
}
