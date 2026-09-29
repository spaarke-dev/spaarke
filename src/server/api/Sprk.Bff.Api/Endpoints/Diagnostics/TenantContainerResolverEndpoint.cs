using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Spaarke.Core.Auth;

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
/// Error mapping: caller is not an allow-listed operator → 403; missing tenantId → 400;
/// tenant not served by this stamp → 400; resolver misconfiguration / unexpected failure
/// → 500; missing/invalid JWT → 401 via standard auth middleware.
/// </para>
///
/// <para><b>Authorization</b> (unified-access-control-r2 task 081 — READ BEFORE CHANGING):
/// this route lets its caller NAME AN ARBITRARY TENANT, which is a deliberate OPERATOR
/// capability (the L2 probe is one tenant's identity asking about another tenant's
/// resolution) and therefore cannot be secured by matching the caller's <c>tid</c> claim.
/// It is gated on a CONJUNCTION:
/// <list type="number">
///   <item>the caller is positively classified app-only by
///     <see cref="Spaarke.Core.Auth.CallerIdentity"/> — a token no interactive user can hold; AND</item>
///   <item>its <c>appid</c>/<c>azp</c> appears in
///     <see cref="AllowedOperatorAppIdsConfigKey"/>.</item>
/// </list>
/// Both halves are required. <c>appid</c> ALONE is not a caller kind — it names the client
/// application and is present in user-delegated tokens too, so an allow-list keyed on it alone
/// would admit any human signed into the L2 app registration. User principals are denied
/// outright: a provisioning invariant diagnostic has no end-user use case, and admitting them
/// "for their own tenant" would re-open the enumeration oracle below to every authenticated user.
/// </para>
///
/// <para><b>Why the previous auth statement was wrong.</b> This comment used to assert
/// "missing/invalid JWT → 401 via standard auth middleware (<c>RequireAuthorization</c> — parity
/// with all other BFF endpoints)". Every word of that was true and it was not the point:
/// <c>RequireAuthorization()</c> establishes THAT a caller is authenticated, never WHICH tenant's
/// data they may ask for. With only that in place, any authenticated Spaarke end user could pass
/// <c>?tenantId={someone-else}</c> and receive that tenant's SPE container id — and, because
/// "tenant not served by this stamp" answers 400 while a served tenant answers 200, could use the
/// STATUS CODE ALONE to enumerate which customers this stamp hosts. Denial now happens BEFORE any
/// resolver call, so served and not-served tenants are indistinguishable to a denied caller. A
/// PERMITTED operator still sees the 400-vs-200 split — distinguishing served from not-served is
/// the probe's entire purpose.
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

    /// <summary>
    /// Configuration key holding the app registration ids (<c>appid</c>/<c>azp</c>) of the operator
    /// service principals permitted to name an arbitrary <c>tenantId</c> on this route — in practice the
    /// L2 control-plane UAMI that runs the H13 I4 invariant probe.
    ///
    /// <para><b>ABSENT OR EMPTY DENIES EVERYONE.</b> This is load-bearing, not defensive boilerplate.
    /// "Empty means allow all" is the classic failure of this pattern and it fails OPEN on a freshly
    /// provisioned environment, where configuration has not been written yet and the route would
    /// otherwise be wide open during exactly the window nobody is watching it. There is no default
    /// value and no fallback: an unconfigured deployment answers 403 to every caller, which surfaces
    /// to the operator as the I4 probe reporting InfraFault rather than as a silent hole.</para>
    ///
    /// <para>Accepted shapes: a configuration array (JSON <c>[ "guid", … ]</c>, or App Service /
    /// container environment variables <c>…AllowedOperatorAppIds__0</c>, <c>__1</c>, …) and a single
    /// comma- or semicolon-separated scalar value, since flat app settings are how App Service is
    /// most often configured by hand. Comparison is case-insensitive; GUID casing is not semantic.</para>
    /// </summary>
    public const string AllowedOperatorAppIdsConfigKey =
        "Diagnostics:TenantContainerResolver:AllowedOperatorAppIds";

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
                             "tenantId is REQUIRED in the query string — there is no JWT tid fallback and no " +
                             "ambient-tenant resolution. Restricted to allow-listed operator service " +
                             "principals (app-only tokens); user-delegated callers are denied 403.")
            .Produces<TenantContainerResolverResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
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
        [FromServices] IConfiguration configuration,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var logger = loggerFactory.CreateLogger(typeof(TenantContainerResolverEndpoint).FullName!);
        var traceId = httpContext.TraceIdentifier;

        // ================================================================================
        // OPERATOR GATE — runs BEFORE the tenant id is even read, and therefore before any
        // resolver call. That ordering is what closes the tenant-enumeration oracle: a denied
        // caller cannot distinguish a served tenant from an unserved one, because nothing
        // tenant-specific has been consulted yet.
        //
        // The check is a CONJUNCTION, and both halves are required:
        //   (1) positively classified app-only  — a token shape no interactive user can hold;
        //   (2) appid ∈ the configured allow-list.
        // Dropping (1) and keeping (2) would admit any human signed into an allow-listed app
        // registration, because appid/azp names the CLIENT APPLICATION, not the caller's kind,
        // and is present in user-delegated tokens too. See CallerIdentity's TRAP note.
        // ================================================================================
        var caller = CallerIdentity.FromPrincipal(httpContext.User);
        var allowedOperatorAppIds = ReadAllowedOperatorAppIds(configuration);

        var isPermittedOperator =
            caller.IsApplication
            && !string.IsNullOrWhiteSpace(caller.ApplicationId)
            && allowedOperatorAppIds.Contains(caller.ApplicationId);

        if (!isPermittedOperator)
        {
            // Attribution is the reason option B (classify the caller) was chosen over an inbound
            // static API key: the log names WHICH principal attempted a cross-tenant read. Claim
            // VALUES beyond the app id are not logged, and none of this reaches the response body.
            logger.LogWarning(
                "TenantContainerResolver diagnostic DENIED: callerKind={CallerKind} appId={AppId} " +
                "determination={Determination} allowListCount={AllowListCount}. TraceId={TraceId}",
                caller.Kind, caller.ApplicationId ?? "(none)", caller.DeterminationReason,
                allowedOperatorAppIds.Count, traceId);

            return DeniedResult(traceId);
        }

        logger.LogInformation(
            "TenantContainerResolver diagnostic: permitted operator appId={AppId} " +
            "(determination={Determination}). TraceId={TraceId}",
            caller.ApplicationId, caller.DeterminationReason, traceId);

        // -------- Tenant id: REQUIRED in the query string (the probe's calling convention).
        //          There is deliberately NO fallback to the caller's JWT tid claim. For the only
        //          callers that reach this line — allow-listed operator service principals — the
        //          tid claim is the CONTROL-PLANE tenant, never the customer tenant being probed,
        //          so falling back to it would silently answer a different question than the one
        //          asked. Refusing is also what §4D I1/I4 require: no ambient-tenant resolution. --------
        var tenantId = httpContext.Request.Query["tenantId"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            logger.LogWarning(
                "TenantContainerResolver diagnostic: no tenantId in query string. TraceId={TraceId}",
                traceId);
            return Results.Problem(
                title: "Bad Request",
                detail: "tenantId is required — pass ?tenantId={entra-tenant-guid}. The diagnostic refuses " +
                        "ambient/default-tenant resolution and does not infer the tenant from the caller's " +
                        "token (§4D I1/I4).",
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

    /// <summary>
    /// The single denial response for this route. UNIFORM by design: every rejected caller — user
    /// principal, unclassifiable token, app-only caller not on the allow-list, allow-list absent,
    /// allow-list empty — receives a byte-identical 403 with the same errorCode and no hint of which
    /// condition fired. A denial that explained itself would re-open a smaller version of the
    /// enumeration oracle (a caller could learn whether the allow-list is configured, or whether its
    /// own token was classified app-only) which is information no denied caller is entitled to. The
    /// specifics go to the server log, keyed by correlationId, where the operator can read them.
    /// </summary>
    private static IResult DeniedResult(string traceId) =>
        Results.Problem(
            title: "Forbidden",
            detail: "This diagnostic is restricted to allow-listed operator service principals.",
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = "diagnostics.tenant_container_resolver.operator_only",
                ["correlationId"] = traceId,
            });

    /// <summary>
    /// Reads the operator allow-list. Returns an EMPTY set when the key is absent, when it is present
    /// but empty, and when every entry is blank — and an empty set denies every caller, because
    /// <c>HashSet.Contains</c> is false for everything. There is intentionally no branch that widens
    /// access when configuration is missing; see <see cref="AllowedOperatorAppIdsConfigKey"/> for why
    /// the absent case must not be treated as "unrestricted".
    /// </summary>
    private static IReadOnlySet<string> ReadAllowedOperatorAppIds(IConfiguration configuration)
    {
        var section = configuration.GetSection(AllowedOperatorAppIdsConfigKey);
        var appIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Array form: JSON array, or App Service / container env vars using the __0, __1 convention.
        foreach (var child in section.GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Value))
            {
                appIds.Add(child.Value.Trim());
            }
        }

        // Scalar form: one flat app setting holding a comma/semicolon-separated list.
        if (!string.IsNullOrWhiteSpace(section.Value))
        {
            foreach (var part in section.Value.Split(
                         [',', ';'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                appIds.Add(part);
            }
        }

        return appIds;
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
