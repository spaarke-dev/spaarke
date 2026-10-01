using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using System.Security.Claims;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding VisualizationAuthorizationFilter to endpoints.
/// </summary>
public static class VisualizationAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds visualization authorization to an endpoint.
    /// Reads documentId from route parameter and verifies read access.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddVisualizationAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var authService = context.HttpContext.RequestServices.GetRequiredService<IAiAuthorizationService>();
            var logger = context.HttpContext.RequestServices.GetService<ILogger<VisualizationAuthorizationFilter>>();
            var filter = new VisualizationAuthorizationFilter(
                authService, logger, VisualizationAuthorizationSubject.SourceDocument);
            return await filter.InvokeAsync(context, next);
        });
    }

    /// <summary>
    /// Adds visualization authorization to a route whose subject is content the caller UPLOADS rather
    /// than a stored document: there is no prior resource to authorize, so the filter establishes the
    /// caller preconditions the row check depends on and publishes the same per-row obligation the
    /// document-keyed route publishes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this route needed one at all (word-add-in-r1 task 032, finding F-b).</b>
    /// <c>POST /api/ai/visualization/related-from-content</c> carried NO authorization filter — only the
    /// group's <c>RequireAuthorization()</c>, which answers "are you anyone?". It writes an embedding of
    /// the caller's file into the tenant's AI Search partition and mints a temporary documentId intended
    /// to be fed to <c>GET /related/{documentId}</c>, so it is the ENTRY to the similarity surface whose
    /// exit leaked rows. Its absence also kept the whole file out of the
    /// <c>RouteAuthorizationGuardTests</c> census: a file cannot be classified <c>RouteLevelGate</c>
    /// while one of its routes has no gate, so the gap concealed itself.
    /// </para>
    /// <para>
    /// <b>What it can and cannot decide.</b> Content creation has no pre-existing record — the same shape
    /// waived on <c>OBOEndpoints</c>'s upload trio and on Compose's <c>create-on-save</c>. So this filter
    /// decides the two things that ARE decidable, and that the row check would otherwise discover only as
    /// a uniform silent denial: a resolvable caller <c>oid</c>, and a bearer token, without which access
    /// can only be evaluated app-only — which
    /// <see cref="Spaarke.Core.Auth.AuthorizationService.GetCallerRecordAccessAsync"/> refuses by design.
    /// </para>
    /// </remarks>
    public static TBuilder AddVisualizationContentAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var authService = context.HttpContext.RequestServices.GetRequiredService<IAiAuthorizationService>();
            var logger = context.HttpContext.RequestServices.GetService<ILogger<VisualizationAuthorizationFilter>>();
            var filter = new VisualizationAuthorizationFilter(
                authService, logger, VisualizationAuthorizationSubject.UploadedContent);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// What a visualization route's filter is able to authorize BEFORE the handler runs.
/// </summary>
public enum VisualizationAuthorizationSubject
{
    /// <summary>A stored <c>sprk_document</c> named in the route, authorized before the handler runs.</summary>
    SourceDocument,

    /// <summary>A file the caller uploads. Nothing stored exists yet, so only caller preconditions are decidable.</summary>
    UploadedContent,
}

/// <summary>
/// The authorization decision for a visualization request, published for the ENDPOINT to enforce at ROW
/// level. Placed in <see cref="HttpContext.Items"/> under <see cref="HttpContextItemsKey"/>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the same shape as <see cref="RecordSearchAuthorization"/>, for the reason that record
/// search has it: a filter runs BEFORE the handler and so cannot authorize rows that do not exist yet.
/// The similarity surface's subject IS the result set — "which documents resemble this one" — and those
/// rows' readability can only be evaluated once the search has produced them.
/// </para>
/// <para>
/// The filter therefore authorizes what it can and carries the obligation forward. A route that receives
/// this decision and does not perform the row check MUST refuse rather than serve the unfiltered answer;
/// see the forcing function in <c>VisualizationEndpoints</c>.
/// </para>
/// </remarks>
public sealed record VisualizationAuthorization
{
    public static readonly object HttpContextItemsKey = new();

    /// <summary>
    /// Every document row returned MUST be authorized against its own record before it may be served.
    /// </summary>
    /// <remarks>
    /// An explicit flag rather than something inferred, so "permit" stays a positive assertion and a
    /// decision that carries nothing permits nothing. No branch sets it false: BOTH visualization routes
    /// publish the same obligation, on purpose, so they cannot drift apart the way they had before task
    /// 032 — one carried a source-document gate, the other carried no filter at all.
    /// </remarks>
    public bool RequiresPerRowDocumentAuthorization { get; init; }

    /// <summary>What the filter authorized up front, recorded for the handler's diagnostics.</summary>
    public required VisualizationAuthorizationSubject Subject { get; init; }
}

/// <summary>
/// Authorization filter for document visualization endpoints.
/// Validates user has read access to the source document before returning related documents.
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-008: Use endpoint filters for resource-level authorization.
/// Uses IAiAuthorizationService for FullUAC authorization via RetrievePrincipalAccess.
/// </para>
///
/// <para>
/// <strong>Authorization Flow:</strong>
/// <list type="number">
/// <item>Extract documentId from route parameter</item>
/// <item>Call IAiAuthorizationService.AuthorizeAsync to verify read access</item>
/// <item>Return 403 Forbidden if user lacks access</item>
/// <item>Proceed to endpoint if authorized</item>
/// </list>
/// </para>
///
/// <para>
/// <strong>Route Parameter:</strong>
/// Expects documentId as a Guid route parameter (e.g., /api/ai/visualization/related/{documentId})
/// </para>
/// </remarks>
public class VisualizationAuthorizationFilter : IEndpointFilter
{
    private readonly IAiAuthorizationService _authorizationService;
    private readonly ILogger<VisualizationAuthorizationFilter>? _logger;
    private readonly VisualizationAuthorizationSubject _subject;

    public VisualizationAuthorizationFilter(
        IAiAuthorizationService authorizationService,
        ILogger<VisualizationAuthorizationFilter>? logger = null,
        VisualizationAuthorizationSubject subject = VisualizationAuthorizationSubject.SourceDocument)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _logger = logger;
        _subject = subject;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        // Extract Azure AD Object ID from claims.
        // DataverseAccessDataSource requires the 'oid' claim to lookup user in Dataverse.
        // Fallback chain matches other authorization filters in the codebase.
        var userId = CallerResolution.ResolveObjectId(user);

        if (string.IsNullOrEmpty(userId))
        {
            _logger?.LogWarning("[VISUALIZATION-AUTH] Authorization failed: User identity not found in claims");
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // The caller's bearer token, checked HERE rather than left to fail downstream. Access is
        // evaluated AS THE CALLER; without a token AuthorizationService returns AccessRights.None by
        // design and never falls back to app-only, so a tokenless request would otherwise present as a
        // uniform "you may read nothing" rather than as the missing credential it is.
        if (string.IsNullOrEmpty(TokenHelper.ExtractBearerTokenOrNull(httpContext)))
        {
            _logger?.LogWarning(
                "[VISUALIZATION-AUTH] Authorization failed for caller {UserId}: no bearer token, so access "
                + "cannot be evaluated as the caller. Refusing rather than evaluating app-only.", userId);

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "A caller bearer token is required to evaluate access.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        if (_subject == VisualizationAuthorizationSubject.UploadedContent)
        {
            // Nothing stored exists yet to authorize against — see AddVisualizationContentAuthorizationFilter.
            // The obligation is published all the same, so this route's handler is bound by the same
            // fail-closed check as the document-keyed one and the two cannot drift.
            PublishRowObligation(httpContext);

            _logger?.LogInformation(
                "[VISUALIZATION-AUTH] Content upload authorized for caller {UserId}; any rows served from "
                + "the resulting similarity surface will be authorized per row.", userId);

            return await next(context);
        }

        // Extract documentId from route parameter
        var documentId = ExtractDocumentId(context);
        if (documentId == Guid.Empty)
        {
            _logger?.LogWarning("[VISUALIZATION-AUTH] Authorization failed: Invalid or missing documentId in route");
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Document identifier not found or invalid in request",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        try
        {
            var result = await _authorizationService.AuthorizeAsync(
                user,
                [documentId],
                httpContext,
                httpContext.RequestAborted);

            if (!result.Success)
            {
                _logger?.LogWarning(
                    "[VISUALIZATION-AUTH] Document access DENIED: DocumentId={DocumentId}, UserId={UserId}, Reason={Reason}",
                    documentId,
                    userId,
                    result.Reason);

                return Results.Problem(
                    statusCode: 403,
                    title: "Forbidden",
                    detail: result.Reason ?? "Access denied to document",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.3");
            }

            _logger?.LogDebug(
                "[VISUALIZATION-AUTH] Document access GRANTED: DocumentId={DocumentId}, UserId={UserId}",
                documentId,
                userId);

            // Read on the SOURCE says nothing about the neighbours the similarity search will return.
            // Publishing the obligation is what makes that gap the handler's problem instead of nobody's.
            PublishRowObligation(httpContext);

            return await next(context);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "[VISUALIZATION-AUTH] Authorization check failed with exception: DocumentId={DocumentId}, UserId={UserId}",
                documentId,
                userId);

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Authorization check failed",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Publishes the per-row obligation the endpoint's fail-closed check asserts on.
    /// </summary>
    private void PublishRowObligation(HttpContext httpContext) =>
        httpContext.Items[VisualizationAuthorization.HttpContextItemsKey] = new VisualizationAuthorization
        {
            RequiresPerRowDocumentAuthorization = true,
            Subject = _subject
        };

    /// <summary>
    /// Extract documentId from route parameter or endpoint arguments.
    /// </summary>
    private static Guid ExtractDocumentId(EndpointFilterInvocationContext context)
    {
        // Try route values first (e.g., /api/ai/visualization/related/{documentId})
        var routeValues = context.HttpContext.Request.RouteValues;
        if (routeValues.TryGetValue("documentId", out var routeValue) &&
            routeValue is string stringValue &&
            Guid.TryParse(stringValue, out var routeGuid))
        {
            return routeGuid;
        }

        // Try endpoint arguments (bound parameters)
        foreach (var argument in context.Arguments)
        {
            if (argument is Guid guid && guid != Guid.Empty)
            {
                return guid;
            }
        }

        return Guid.Empty;
    }
}
