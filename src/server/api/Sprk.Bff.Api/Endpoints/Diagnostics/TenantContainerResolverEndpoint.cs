using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Sprk.Bff.Api.Endpoints.Diagnostics;

/// <summary>
/// <c>GET /api/diagnostics/tenant-container-resolver</c> — READ-ONLY diagnostic
/// endpoint the L2 H13 I4 invariant probe (<c>SpeContainerResolverInvariantProbe</c>)
/// calls to verify the customer BFF resolves its SPE container id from tenant-scoped
/// configuration, never from a hardcoded literal / fallback default (spec.md FR-31 /
/// design.md §4D I4). Without this endpoint, live H13 runs park I4 at InfraFault
/// (HTTP 404 branch) and Ready is unreachable.
///
/// <para><b>Contract</b> (locked by the L2 probe — see its file header):
/// <code>
/// GET /api/diagnostics/tenant-container-resolver?tenantId={tenantId}
/// Authorization: Bearer {aad-token scoped to this BFF app}
/// →
/// 200 OK application/json
/// {
///   "tenantId": "&lt;echoed request tenant id&gt;",
///   "containerId": "b!&lt;real Graph container id&gt;",
///   "resolverSource": "options" | "kv" | "env",
///   "resolvedFromLiteral": false,
///   "resolvedAt": "&lt;ISO-8601 UTC timestamp&gt;"
/// }
/// </code>
/// The probe requires <c>tenantId</c> + <c>containerId</c>; <c>resolvedFromLiteral</c> is
/// optional-but-honored (TRUE → CATASTROPHIC verdict); <c>resolvedAt</c> is informational.
/// Error mapping: missing tenantId → 400; tenant not served by this stamp → 400; resolver
/// misconfiguration / unexpected failure → 500; missing/invalid JWT → 401 via standard
/// auth middleware (<c>RequireAuthorization</c> — parity with all other BFF endpoints).
/// </para>
///
/// <para><b>Placement Justification</b> (CLAUDE.md §10 / bff-extensions.md §A.1): this
/// MUST live in the BFF — the invariant under test IS the deployed BFF's own container
/// resolution; probing it anywhere else would attest nothing. Read-only, no writes, no
/// side effects, no AI-internal types (ADR-013), no new packages. Mirrors the
/// Endpoints/Onboarding/ convention from the same project (task 042).
/// </para>
/// </summary>
public static class TenantContainerResolverEndpoint
{
    /// <summary>Endpoint route — MUST equal the L2 probe's <c>DiagnosticEndpointPath</c> constant.</summary>
    public const string Route = "/api/diagnostics/tenant-container-resolver";

    /// <summary>Maps <c>GET /api/diagnostics/tenant-container-resolver</c> onto the application.</summary>
    public static IEndpointRouteBuilder MapTenantContainerResolverEndpoint(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(Route, HandleAsync)
            .RequireAuthorization()
            .RequireRateLimiting("graph-read")
            .WithName("TenantContainerResolverDiagnostic")
            .WithTags("Diagnostics")
            .WithSummary("I4 diagnostic — resolves the SPE container id for the tenant via ITenantContainerResolver.")
            .WithDescription("READ-ONLY. Called by the L2 H13 I4 invariant probe to verify tenant-scoped SPE " +
                             "container resolution (no hardcoded literals / fallback defaults). " +
                             "tenantId comes from the query string, falling back to the caller's JWT tid claim.")
            .Produces<TenantContainerResolverResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// Endpoint handler. Exposed <c>public</c> for direct unit-test invocation
    /// (parity with <c>ConsentCallbackEndpoint.HandleAsync</c>).
    /// </summary>
    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        [FromServices] ITenantContainerResolver resolver,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var logger = loggerFactory.CreateLogger(typeof(TenantContainerResolverEndpoint).FullName!);
        var traceId = httpContext.TraceIdentifier;

        // -------- Tenant id: query string first (the probe's calling convention),
        //          falling back to the caller's JWT tid claim. --------
        var tenantId = httpContext.Request.Query["tenantId"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            tenantId = httpContext.User?.FindFirst("tid")?.Value
                ?? httpContext.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            logger.LogWarning(
                "TenantContainerResolver diagnostic: no tenantId in query string or JWT tid claim. TraceId={TraceId}",
                traceId);
            return Results.Problem(
                title: "Bad Request",
                detail: "tenantId is required — pass ?tenantId={entra-tenant-guid} or call with a JWT carrying a " +
                        "tid claim. The diagnostic refuses ambient/default-tenant resolution (§4D I1/I4).",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "diagnostics.tenant_container_resolver.missing_tenant_id",
                    ["correlationId"] = traceId,
                });
        }

        // -------- Live resolution (READ-ONLY; contract obligation: a real resolver
        //          call, never a canned mirror of inputs). --------
        TenantContainerResolutionResult result;
        try
        {
            result = await resolver.ResolveAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "TenantContainerResolver diagnostic: resolver threw unexpectedly for tenant {TenantId}. TraceId={TraceId}",
                tenantId, traceId);
            return Results.Problem(
                title: "Internal Server Error",
                detail: $"Tenant-container resolution failed unexpectedly: {ex.GetType().Name}. " +
                        "See server logs for details.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "diagnostics.tenant_container_resolver.resolver_exception",
                    ["correlationId"] = traceId,
                });
        }

        if (!result.Succeeded)
        {
            var failureCode = result.FailureCode!.Value;
            var statusCode = failureCode == TenantContainerResolutionFailureCode.TenantNotServed
                ? StatusCodes.Status400BadRequest       // client asked for a tenant this stamp doesn't serve
                : StatusCodes.Status500InternalServerError; // deployment misconfiguration

            logger.LogWarning(
                "TenantContainerResolver diagnostic: resolution failed with {FailureCode} for tenant {TenantId}. " +
                "TraceId={TraceId}",
                failureCode, tenantId, traceId);
            return Results.Problem(
                title: statusCode == StatusCodes.Status400BadRequest ? "Bad Request" : "Internal Server Error",
                detail: result.Diagnostic,
                statusCode: statusCode,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = failureCode switch
                    {
                        TenantContainerResolutionFailureCode.TenantNotServed =>
                            "diagnostics.tenant_container_resolver.tenant_not_served",
                        TenantContainerResolutionFailureCode.TenantScopeNotPinned =>
                            "diagnostics.tenant_container_resolver.tenant_scope_not_pinned",
                        TenantContainerResolutionFailureCode.ContainerNotConfigured =>
                            "diagnostics.tenant_container_resolver.container_not_configured",
                        _ => "diagnostics.tenant_container_resolver.resolution_failed",
                    },
                    ["correlationId"] = traceId,
                });
        }

        var resolution = result.Resolution!;
        logger.LogInformation(
            "TenantContainerResolver diagnostic: resolved tenant {TenantId} via {ResolverSource}. TraceId={TraceId}",
            resolution.TenantId, resolution.ResolverSource, traceId);

        // Minimal-API default web JSON options serialize this camelCase:
        // { tenantId, containerId, resolverSource, resolvedFromLiteral, resolvedAt } —
        // exactly the property names the L2 probe parses.
        return Results.Ok(new TenantContainerResolverResponse(
            TenantId: resolution.TenantId,
            ContainerId: resolution.ContainerId,
            ResolverSource: resolution.ResolverSource,
            ResolvedFromLiteral: resolution.ResolvedFromLiteral,
            ResolvedAt: DateTimeOffset.UtcNow.ToString("O")));
    }
}

/// <summary>
/// 200-OK response body. Property names (camelCased on the wire) are LOCKED by the L2
/// probe contract — <c>tenantId</c> + <c>containerId</c> required, <c>resolvedFromLiteral</c>
/// honored when present, <c>resolverSource</c> + <c>resolvedAt</c> informational.
/// </summary>
public sealed record TenantContainerResolverResponse(
    string TenantId,
    string ContainerId,
    string ResolverSource,
    bool ResolvedFromLiteral,
    string ResolvedAt);
