// -----------------------------------------------------------------------------
// AuditLogMiddleware.cs
//
// L2 CONTROL-PLANE AUDITABLE OPERATOR ACTION middleware (task 039).
//
// SPEC / ADR references:
//   - spec.md NFR-11:     "Auditable operator action — Claude Code auths via
//                         operator's own AAD identity (`az login`), NOT a
//                         service principal (§4.3a.2). Every mutating L2
//                         endpoint audit-logs to App Insights with actor
//                         `tid`."
//   - spec.md §4.3a.2:    Operator's own AAD identity flows into the L2 JWT.
//                         The `tid` (tenant), `oid` (object id), and `roles`
//                         (app-role assignments) claims are the auditable-
//                         identity dimensions.
//   - ADR-028:            JWT bearer via Microsoft.Identity.Web
//                         (AuthModule.AddMicrosoftIdentityWebApi). Standard
//                         claim keys per Microsoft.Identity.Model:
//                           tid   = tenant id      (ClaimTypes.TenantId /
//                                                   "http://schemas.microsoft.com/identity/claims/tenantid"
//                                                   or short "tid")
//                           oid   = object id      ("http://schemas.microsoft.com/identity/claims/objectidentifier"
//                                                   or short "oid")
//                           roles = app-role names (ClaimTypes.Role after MSAL
//                                                   maps the JWT `roles` array)
//                         The middleware extracts BOTH long + short forms
//                         because Microsoft.Identity.Web's default JwtBearer
//                         wiring emits `tid`/`oid` short forms whereas older
//                         handlers used the long schema URIs; carrying both
//                         paths avoids silent-null on future JWT lib updates.
//   - ADR-032:            Unconditional registration — the middleware runs
//                         every request; no feature-gate branch. Emitter is
//                         `ILogger.LogInformation` which flows through OTel
//                         Logs into Azure Monitor `traces` (see
//                         TelemetryModule.cs SDK note).
//
// EMITTER MECHANISM (D-039-1 deviation carried forward from task 036 D-036-1):
//   The POML step 4 named `TelemetryClient.TrackEvent("AuditableAction", ...)`
//   from the classic Microsoft.ApplicationInsights.AspNetCore SDK. L2 (and the
//   modern BFF) use `Azure.Monitor.OpenTelemetry.AspNetCore` — the classic
//   `TelemetryClient` is NOT registered and calling it would be a runtime
//   no-op (per BFF's AgentTelemetry.cs comment referencing dotnet-10-upgrade-r1
//   task 014 / FR-06). The Path-A exception: emit via structured `ILogger`
//   with a stable message prefix `"AuditableAction:"` — the OTel Logs pipeline
//   flows this into Azure Monitor `traces` with the structured properties
//   preserved in `customDimensions`. Kusto query stays clean:
//
//     traces
//     | where message startswith "AuditableAction"
//     | project timestamp,
//               method   = tostring(customDimensions.Method),
//               path     = tostring(customDimensions.Path),
//               status   = toint   (customDimensions.StatusCode),
//               actorTid = tostring(customDimensions.ActorTid),
//               actorOid = tostring(customDimensions.ActorOid),
//               actorRoles = tostring(customDimensions.ActorAppRoles),
//               traceId  = tostring(customDimensions.TraceId)
//
// PIPELINE ORDER (Program.cs):
//   UseAuthentication()          <- populates HttpContext.User claims
//   UseMiddleware<AuditLogMiddleware>()   <- WRAPS the rest so 401/403 from
//                                            UseAuthorization() are also
//                                            audited (POML acceptance #4:
//                                            "unauthenticated POST that fails
//                                             at 401 IS emitted with StatusCode=401
//                                             and Actor.Tid null")
//   UseAuthorization()
//   endpoints
//
// SCOPE (this middleware):
//   - Fires on POST/PUT/PATCH/DELETE ONLY (mutating verbs).
//   - Skips GET/HEAD/OPTIONS silently — /ping is GET, /swagger is GET, neither
//     is audit-worthy per POML acceptance #3.
//   - Reads Actor.Tid / Actor.Oid / Actor.AppRoles from HttpContext.User; all
//     three MAY be null for unauthenticated / anonymous mutating requests
//     (POML acceptance #4 requires the record to still emit).
//   - Never reads Request/Response bodies (POML constraint: "no request/response
//     body content"). Only the route path + method + status + trace id +
//     actor claims are logged.
//   - Runs unconditionally (no feature-gate); trivial cost when the pipeline
//     is unwired (Development env without conn string) — ILogger writes still
//     land in the default console/debug providers.
// -----------------------------------------------------------------------------

using System.Security.Claims;

namespace Sprk.Provisioning.ControlPlane.Middleware;

/// <summary>
/// Middleware that emits an <c>AuditableAction</c> structured-log record for
/// every mutating (POST/PUT/PATCH/DELETE) L2 endpoint request, per spec NFR-11.
/// The record carries actor <c>tid</c>, <c>oid</c>, and <c>roles</c> extracted
/// from the validated JWT (or <see langword="null"/> for unauthenticated calls),
/// the HTTP method + path + status code, and the distributed-trace id — all
/// via <see cref="ILogger.LogInformation(string, object?[])"/> so the OpenTelemetry
/// -&gt; Azure Monitor pipeline (see
/// <see cref="Modules.TelemetryModule"/>) forwards the record into App Insights
/// <c>traces</c> with structured <c>customDimensions</c>.
/// </summary>
/// <remarks>
/// <para>
/// Placed AFTER <c>UseAuthentication()</c> (so <c>HttpContext.User</c> is
/// populated before the audit read) and BEFORE <c>UseAuthorization()</c> (so
/// short-circuit 401 / 403 responses are captured — the middleware wraps the
/// downstream pipeline and inspects <c>Response.StatusCode</c> after
/// <c>_next()</c> returns).
/// </para>
/// <para>
/// Never reads Request or Response bodies. Never logs raw JWTs. Only:
/// method, path, status code, trace id, actor tid, actor oid, actor role list.
/// </para>
/// </remarks>
public sealed class AuditLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLogMiddleware> _logger;

    /// <summary>
    /// Stable event-name prefix on the log message so Kusto queries filter
    /// audit records cleanly via <c>traces | where message startswith "AuditableAction"</c>.
    /// Do NOT change without a coordinated Kusto-query update.
    /// </summary>
    public const string AuditEventName = "AuditableAction";

    /// <summary>
    /// Convention-based middleware constructor invoked by
    /// <c>UseMiddleware&lt;AuditLogMiddleware&gt;()</c>.
    /// </summary>
    public AuditLogMiddleware(RequestDelegate next, ILogger<AuditLogMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Middleware entry point. Awaits the downstream pipeline for mutating
    /// verbs then emits the audit record; passes through immediately for
    /// non-mutating verbs.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var method = context.Request.Method;

        if (!IsMutatingMethod(method))
        {
            // Non-mutating (GET/HEAD/OPTIONS): pass through with no audit
            // overhead. /ping (GET) + /swagger (GET) fall through this branch
            // per POML acceptance criterion #3.
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Snapshot the path BEFORE _next() runs. If a downstream middleware
        // rewrites HttpContext.Request.Path (redirect middleware, route
        // rewrites, etc.), the audit record should reflect the operator's
        // originally-requested surface, not the rewritten target.
        var path = context.Request.Path.HasValue
            ? context.Request.Path.Value ?? string.Empty
            : string.Empty;

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch
        {
            // Kestrel writes the actual 500 to the socket only AFTER the
            // middleware chain fully unwinds — at this point in the `catch`
            // (before re-throwing) `context.Response.StatusCode` is still the
            // pipeline default (`200 OK`). Assigning 500 here BEFORE the emit
            // ensures the audit record reflects the failure. Guarded on
            // `HasStarted` because an upstream `UseExceptionHandler` (or any
            // other middleware that already wrote to the response) would have
            // set the status code already, and mutating a started response is
            // illegal in ASP.NET Core.
            if (!context.Response.HasStarted &&
                context.Response.StatusCode == StatusCodes.Status200OK)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
            throw;
        }
        finally
        {
            // Emit AFTER downstream completion so Response.StatusCode reflects
            // the final outcome (200 success, 401/403 auth failure, 5xx server
            // failure — including the 500 assigned in the catch above for
            // unhandled downstream exceptions). `finally` ensures the record
            // is emitted even if the downstream throws — an unhandled
            // exception in a handler is audit-worthy per NFR-11.
            EmitAudit(context, method, path);
        }
    }

    private void EmitAudit(HttpContext context, string method, string path)
    {
        // Actor claims. All three MAY be null:
        //   - unauthenticated POST (POML acceptance #4: emit with nulls)
        //   - authenticated JWT without one of the claims (edge; would trip
        //     escalation trigger #1 on the POML but the middleware itself is
        //     defensive — the operator-facing surface is the Kusto query
        //     alerting on `actorTid == ""`)
        var actorTid = ExtractTenantId(context.User);
        var actorOid = ExtractObjectId(context.User);
        var actorRoles = ExtractAppRoles(context.User);

        var statusCode = context.Response.StatusCode;

        // Distributed-trace id. Prefer HttpContext.TraceIdentifier (ASP.NET Core
        // request id) — TraceContext (Activity.Current?.TraceId) is populated by
        // OTel but may be absent when the exporter is unwired (Development env
        // without conn string). The pair below gives the correlation surface
        // consumers expect regardless of exporter state:
        //   - RequestId    -> HttpContext.TraceIdentifier  (always populated)
        //   - TraceId      -> Activity.Current?.TraceId    (populated when OTel is wired)
        var requestId = context.TraceIdentifier;
        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty;

        // Structured template — every named placeholder becomes a key in
        // Azure Monitor `traces.customDimensions`. Keys stay stable across
        // future refactors so Kusto queries + alerts do not break.
        //
        // Message prefix "AuditableAction:" is the load-bearing filter for
        // the compliance Kusto query — sourced from the `AuditEventName`
        // const below so a rename cascades through both consumers (the
        // emitter here and the unit-test assertion in
        // AuditLogMiddlewareTests.MutatingRequest_WithOperatorPrincipal_EmitsFullAuditRecord).
        // Do NOT change without a coordinated Kusto-query update.
        _logger.LogInformation(
            AuditEventName + ": Method={Method} Path={Path} StatusCode={StatusCode} " +
            "ActorTid={ActorTid} ActorOid={ActorOid} ActorAppRoles={ActorAppRoles} " +
            "RequestId={RequestId} TraceId={TraceId}",
            method, path, statusCode,
            actorTid ?? string.Empty,
            actorOid ?? string.Empty,
            actorRoles,
            requestId,
            traceId);
    }

    /// <summary>
    /// True for HTTP verbs classified as mutating per POML acceptance criterion
    /// #2 (POST, PUT, PATCH, DELETE). Case-insensitive per RFC 9110 §9.1
    /// (methods are case-sensitive on the wire but the framework normalises
    /// on ingest — the case-insensitive compare is defensive).
    /// </summary>
    internal static bool IsMutatingMethod(string method)
    {
        if (string.IsNullOrEmpty(method))
        {
            return false;
        }

        return string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, "PATCH", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the tenant-id claim. Microsoft.Identity.Web's default
    /// JwtBearer handler emits both the short JWT form (<c>tid</c>) and the
    /// long WS-* schema URI
    /// (<c>http://schemas.microsoft.com/identity/claims/tenantid</c>); we
    /// check the long form first (canonical for
    /// <see cref="ClaimsPrincipal"/> in Microsoft.Identity.Web) and fall back
    /// to the short form so hand-crafted test tokens (short-form only) still
    /// audit correctly.
    /// </summary>
    internal static string? ExtractTenantId(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        return user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
            ?? user.FindFirst("tid")?.Value;
    }

    /// <summary>
    /// Extracts the object-id (oid) claim. See
    /// <see cref="ExtractTenantId"/> for the dual-key rationale.
    /// </summary>
    internal static string? ExtractObjectId(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        return user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst("oid")?.Value;
    }

    /// <summary>
    /// Extracts the app-role assignments as a comma-separated string. Roles
    /// live under <see cref="ClaimTypes.Role"/> once Microsoft.Identity.Web
    /// has mapped the JWT <c>roles</c> array (Operator, Reader, ...). Empty
    /// string when the principal has no roles (unauthenticated / plain user).
    /// </summary>
    internal static string ExtractAppRoles(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return string.Empty;
        }

        // Read BOTH the WS-* Role claim (post-Microsoft.Identity.Web mapping)
        // AND the raw JWT `roles` claim, deduplicating — handles the case where
        // the mapping is disabled (`MapInboundClaims=false`, Microsoft.Identity.Web
        // v3+ default) as well as the mapped default.
        var mapped = user.FindAll(ClaimTypes.Role).Select(c => c.Value);
        var raw = user.FindAll("roles").Select(c => c.Value);

        var distinct = mapped
            .Concat(raw)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return string.Join(",", distinct);
    }
}
