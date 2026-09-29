using Spaarke.Core.Auth;

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
///
/// <para><b>Caller-kind classification lives in <see cref="CallerIdentity"/></b>
/// (unified-access-control-r2 task 081). This middleware used to read <c>appid</c>/<c>azp</c>/
/// <c>idtyp</c>/<c>scp</c>/<c>oid</c> itself, in <c>private static</c> helpers that no authorization
/// path could reach. That logic was PROMOTED into <c>Spaarke.Core.Auth.CallerIdentity</c> so the
/// authorization gate on <c>GET /api/diagnostics/tenant-container-resolver</c> and this logging scope
/// share ONE classifier rather than drifting apart. Per CLAUDE.md §11 there must be exactly one place
/// that decides caller kind from a <c>ClaimsPrincipal</c>; this file is a CONSUMER of it, not a second
/// implementation.</para>
///
/// <para><b>Deliberate asymmetry — logging is two-valued, authorization is three-valued.</b>
/// <c>CallerIdentity</c> answers <c>UserDelegated</c> / <c>Application</c> / <c>Indeterminate</c>. The
/// <c>obo</c> log field projects that onto a bool as <c>Kind == UserDelegated</c>, so an
/// <c>Indeterminate</c> token logs <c>obo=false</c> (not-OBO) — preserving this middleware's previous
/// behaviour. An authorization site facing the SAME <c>Indeterminate</c> value must DENY. These are not
/// inconsistent: for a log field the conservative answer is "no user was proven present", and for an
/// access decision the conservative answer is "no". Both read the identical classification; only the
/// projection differs.</para>
///
/// <para>One shape classifies differently than it did before task 081: a token carrying BOTH
/// <c>idtyp=app</c> and a delegated <c>scp</c> claim now reports <c>UserDelegated</c> (so
/// <c>obo=true</c>) where the old helper returned not-OBO. Entra does not issue that combination — the
/// new ordering checks the delegated-scope claim first on purpose, because for an authorization gate
/// the fail-closed reading of a contradictory token is "there may be a user behind this".</para>
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

        // Extract the canonical identity fields from the ONE classifier (task 081). It reads every
        // claim in both its short JWT form and its mapped WS-Fed URI form, which is what the private
        // helpers this replaced were doing by hand. Null (not empty string) means "missing", so log
        // sinks can still distinguish "present but empty" from "absent".
        var caller = CallerIdentity.FromPrincipal(user);
        var oid = caller.ObjectId;
        var appId = caller.ApplicationId;
        var tenantId = caller.TenantId;

        // Two-valued projection of a three-valued classification — see the asymmetry note on the class.
        var isObo = caller.IsUserDelegated;
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

    // NOTE (task 081): ResolveOid / ResolveAppId / ResolveTenantId / IsOnBehalfOfFlow used to live here
    // as private static claim readers. They were promoted verbatim-in-behaviour into
    // Spaarke.Core.Auth.CallerIdentity so that the authorization path can reach them. Do NOT reintroduce
    // a local claim reader in this file — CLAUDE.md §11 and task 081's acceptance criteria require
    // exactly ONE place that decides caller kind from a ClaimsPrincipal.
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
