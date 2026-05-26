namespace Sprk.Bff.Api.Infrastructure.Authentication;

/// <summary>
/// Canonical names for authorization policies that bind to non-default authentication schemes
/// (task AUTHV2-045). Used by endpoints via <c>.RequireAuthorization(AuthPolicies.X)</c>.
/// </summary>
public static class AuthPolicies
{
    /// <summary>
    /// API-key-only policy for the BuilderAdmin scheme. Use when an endpoint must NOT accept
    /// OAuth bearer tokens (e.g., to prevent interactive users from invoking destructive
    /// admin operations).
    /// </summary>
    public const string BuilderAdminApiKey = "BuilderAdminApiKey";

    /// <summary>
    /// API-key-only policy for the RAG scheme. Use for webhook/background-job indexing
    /// endpoints that must be invoked by automation only.
    /// </summary>
    public const string RagApiKey = "RagApiKey";

    /// <summary>
    /// Composite policy that accepts EITHER Azure AD JWT (OAuth bearer) OR the BuilderAdmin
    /// API key. Mirrors the prior dual-auth behavior of the builder-scope import endpoints,
    /// which support both interactive (Dataverse/PCF) and automation (CLI/script) callers.
    /// </summary>
    public const string BuilderAdminOrOAuth = "BuilderAdminOrOAuth";
}
