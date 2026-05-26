using System.Security.Claims;

namespace Sprk.Bff.Api.Infrastructure.Logging;

/// <summary>
/// Audit enrichment middleware (task AUTHV2-048 / Workstream C8).
///
/// Pushes a standard set of identity fields into the structured-logging scope for every
/// AUTHENTICATED request. Once the scope is active, all subsequent log entries emitted
/// during the request automatically inherit these fields, which makes downstream SIEM
/// integration (Sentinel, Log Analytics, generic JSON sinks) mechanical for customers
/// piping our logs via Azure Monitor Diagnostic Settings.
///
/// <para>Fields emitted (when authenticated):</para>
/// <list type="bullet">
///   <item><c>oid</c>            — Azure AD object ID (canonical, immutable per user per tenant).</item>
///   <item><c>appid</c>          — Calling app registration (azp/appid claim).</item>
///   <item><c>obo</c>            — True when this is a delegated (OBO-capable) user token; false for app-only.</item>
///   <item><c>tenantId</c>       — Issuing Azure AD tenant (tid claim).</item>
///   <item><c>correlationId</c>  — Request correlation ID (HttpContext.TraceIdentifier — W3C TraceParent).</item>
/// </list>
///
/// <para>Security &amp; PII rules (enforced):</para>
/// <list type="bullet">
///   <item>NEVER logs raw JWT, secrets, or the Authorization header.</item>
///   <item>NEVER logs UPN / email / display name. Canonical identity is <c>oid</c>.</item>
///   <item>Anonymous requests (e.g. <c>/healthz</c>) get no scope and pass through untouched.</item>
/// </list>
///
/// <para>Pipeline placement (see <see cref="DI.MiddlewarePipelineExtensions"/>):</para>
/// Must be registered AFTER <c>UseAuthentication</c> + <c>UseAuthorization</c> so that
/// <c>HttpContext.User</c> is populated, and BEFORE endpoint execution so the scope wraps
/// the entire request handler.
///
/// <para>Field semantics rationale:</para>
/// See <c>.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md</c> §G (audit trail integrity) and
/// §5 row C8 (audit logging middleware). Standardising on <c>oid</c> over <c>upn</c>/<c>email</c>
/// aligns with task D3 (identity claims hardening).
/// </summary>
public sealed class AuditEnrichmentMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditEnrichmentMiddleware> _logger;

    public AuditEnrichmentMiddleware(RequestDelegate next, ILogger<AuditEnrichmentMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Anonymous requests get no enrichment — pass straight through.
        // This keeps health probes, /ping, and unauthenticated diagnostic endpoints
        // out of the audit scope (and out of customer SIEM noise).
        var user = context.User;
        if (user?.Identity is null || !user.Identity.IsAuthenticated)
        {
            await _next(context);
            return;
        }

        // Extract the canonical identity fields. Use null (not empty string) for "missing"
        // so log sinks can distinguish "present but empty" from "absent".
        var oid = ResolveOid(user);
        var appId = ResolveAppId(user);
        var tenantId = ResolveTenantId(user);
        var isObo = IsOnBehalfOfFlow(user);
        var correlationId = context.TraceIdentifier;

        // Build the scope dictionary. ILogger<T>.BeginScope(IDictionary) is the
        // documented contract for structured-logging providers (Application Insights,
        // OpenTelemetry, Serilog) to materialise these as top-level log properties.
        var scope = new Dictionary<string, object?>
        {
            ["oid"] = oid,
            ["appid"] = appId,
            ["obo"] = isObo,
            ["tenantId"] = tenantId,
            ["correlationId"] = correlationId
        };

        using (_logger.BeginScope(scope))
        {
            await _next(context);
        }
    }

    /// <summary>
    /// Resolves the Azure AD object ID claim. Tries the short claim name first
    /// (Microsoft.Identity.Web does NOT map this by default in newer versions),
    /// then the long URI form for compatibility with older claim mappings.
    /// </summary>
    private static string? ResolveOid(ClaimsPrincipal user) =>
        user.FindFirst("oid")?.Value
        ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

    /// <summary>
    /// Resolves the calling-app registration ID. Azure AD v2 tokens emit <c>azp</c>
    /// (authorized party); v1 tokens emit <c>appid</c>. Either is the calling app.
    /// </summary>
    private static string? ResolveAppId(ClaimsPrincipal user) =>
        user.FindFirst("appid")?.Value
        ?? user.FindFirst("azp")?.Value;

    /// <summary>
    /// Resolves the issuing Azure AD tenant ID. Tries the short claim, then the long URI,
    /// then the alternative <c>tenant_id</c> emitted by some federated issuers.
    /// </summary>
    private static string? ResolveTenantId(ClaimsPrincipal user) =>
        user.FindFirst("tid")?.Value
        ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
        ?? user.FindFirst("tenant_id")?.Value;

    /// <summary>
    /// Determines whether the incoming token represents a delegated (OBO-capable) user
    /// flow vs an app-only flow.
    ///
    /// Per Azure AD docs:
    /// <list type="bullet">
    ///   <item>User-delegated tokens carry the <c>scp</c> (scope) claim with delegated permissions.</item>
    ///   <item>App-only tokens carry the <c>roles</c> claim (application permissions) and no <c>scp</c>.</item>
    ///   <item>The <c>idtyp</c> claim (when present) explicitly says <c>app</c> for app-only.</item>
    /// </list>
    /// We treat the request as OBO-eligible when a delegated <c>scp</c> claim is present
    /// AND a user identity (<c>oid</c>) is present. This matches the contract the BFF
    /// applies when exchanging the inbound token via the On-Behalf-Of flow.
    /// </summary>
    private static bool IsOnBehalfOfFlow(ClaimsPrincipal user)
    {
        // Explicit app-only signal wins if present.
        var idtyp = user.FindFirst("idtyp")?.Value;
        if (string.Equals(idtyp, "app", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hasDelegatedScope =
            user.HasClaim(c => c.Type == "scp")
            || user.HasClaim(c => c.Type == "http://schemas.microsoft.com/identity/claims/scope");

        var hasUserOid = !string.IsNullOrEmpty(ResolveOid(user));

        return hasDelegatedScope && hasUserOid;
    }
}

/// <summary>
/// Extension methods for registering <see cref="AuditEnrichmentMiddleware"/> in the
/// HTTP request pipeline.
/// </summary>
public static class AuditEnrichmentMiddlewareExtensions
{
    /// <summary>
    /// Adds the audit-enrichment middleware to the request pipeline. Call AFTER
    /// <see cref="Microsoft.AspNetCore.Builder.AuthAppBuilderExtensions.UseAuthentication"/>
    /// and <see cref="Microsoft.AspNetCore.Builder.AuthorizationAppBuilderExtensions.UseAuthorization(Microsoft.AspNetCore.Builder.IApplicationBuilder)"/>
    /// so that <c>HttpContext.User</c> is populated.
    /// </summary>
    public static IApplicationBuilder UseAuditEnrichment(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<AuditEnrichmentMiddleware>();
    }
}
