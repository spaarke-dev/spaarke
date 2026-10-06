using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Compose;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding <see cref="ComposeDocumentAuthorizationFilter"/> to the Compose routes keyed by
/// <c>{documentSpeId}</c>.
/// </summary>
public static class ComposeDocumentAuthorizationFilterExtensions
{
    /// <summary>
    /// Requires <paramref name="operation"/> (<c>read</c> / <c>write</c>) on the <c>sprk_document</c> whose
    /// <c>sprk_graphitemid</c> is the route's <c>{documentSpeId}</c>, when such a row exists.
    /// </summary>
    public static TBuilder AddComposeDocumentAuthorizationFilter<TBuilder>(this TBuilder builder, string operation)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var filter = new ComposeDocumentAuthorizationFilter(
                services.GetRequiredService<AuthorizationService>(),
                services.GetRequiredService<IGenericEntityService>(),
                services.GetRequiredService<RecordContainerResolver>(),
                services.GetRequiredService<ILogger<ComposeDocumentAuthorizationFilter>>(),
                operation);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// ADR-008 gate that TIES a Compose route's client-chosen drive item to an authorized Dataverse record —
/// unified-access-control-r2 task 171 (owner round 69, broker-only; constraint "fix (tie it) before it goes app-only").
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> Until task 171 the Compose routes (load, save, apply-template, pull / reanchor annotations,
/// check-changes) carried no per-resource filter: SPE's own OBO answer for the caller was the whole decision. That made
/// them coarser than Dataverse (any container member could open any document in the container) and dead on a secure
/// record's container (no members by design). This filter finds the <c>sprk_document</c> row that names the route's
/// <c>{documentSpeId}</c> — by the SAME lookup the save path uses (<see cref="ComposeRecordResolution"/>, including its
/// duplicate self-heal) — binds it where <see cref="DocumentAuthorizationFilter"/> reads, and delegates the decision to
/// it (the composition <see cref="OfficeVersionSaveAuthorizationFilter"/> uses). When it allows, the row's storage
/// pointer is verified and the request is marked with <see cref="ComposeBrokeredDocument"/> — the row's OWN drive and
/// item — so <see cref="ComposeSpeAccess"/> reads and writes exactly that item app-only.</para>
/// <para><b>No row: Path B, deliberately unchanged.</b> A drive item with no <c>sprk_document</c> has no record to
/// authorize, so the request passes through unmarked and the Compose byte calls keep the caller's OBO identity — SPE
/// still decides, exactly as before (task 171 escalation trigger 2: reported to the owner, not converted).</para>
/// <para><b>Fail closed.</b> A lookup that faults is a 503 — never a pass-through to the unmarked path for a document
/// that may well have a row. A denied caller gets <see cref="DocumentAuthorizationFilter"/>'s 403; an unverifiable
/// pointer gets the resolver's 409 <c>document_storage_unverified</c>. Neither reaches Graph.</para>
/// </remarks>
public sealed class ComposeDocumentAuthorizationFilter : IEndpointFilter
{
    private const string DocumentSpeIdRouteKey = "documentSpeId";
    private const string DocumentIdRouteKey = "documentId";

    private readonly AuthorizationService _authorizationService;
    private readonly IGenericEntityService _entities;
    private readonly RecordContainerResolver _containerResolver;
    private readonly ILogger<ComposeDocumentAuthorizationFilter> _logger;
    private readonly string _operation;

    public ComposeDocumentAuthorizationFilter(
        AuthorizationService authorizationService,
        IGenericEntityService entities,
        RecordContainerResolver containerResolver,
        ILogger<ComposeDocumentAuthorizationFilter> logger,
        string operation)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operation = string.IsNullOrWhiteSpace(operation) ? throw new ArgumentException("An operation is required.", nameof(operation)) : operation;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var routeValues = httpContext.Request.RouteValues;
        var itemId = (routeValues.TryGetValue(DocumentSpeIdRouteKey, out var raw) ? raw as string : null)?.Trim();
        if (string.IsNullOrWhiteSpace(itemId))
        {
            // The handler answers its own 400 for a missing id; nothing to authorize.
            return await next(context);
        }

        // DocumentAuthorizationFilter reads "id" BEFORE "documentId": a route that already bound either would have the
        // WRONG document authorized, so refuse rather than guess (the task 012 / 023 guard).
        if (routeValues.ContainsKey("id") || routeValues.ContainsKey(DocumentIdRouteKey))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Authorization Misconfigured",
                detail: "The Compose document authorization filter cannot run on a route that binds a document id.");
        }

        Microsoft.Xrm.Sdk.Entity? row;
        try
        {
            row = await ComposeRecordResolution.ForDocumentLookups(_entities, _logger)
                .TryFindDocumentByGraphItemIdAsync(itemId, httpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[COMPOSE-AUTH] The document row for drive item {ItemId} could not be looked up; refusing (fail closed).", itemId);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Document access could not be determined",
                detail: "Whether you may open this document could not be determined just now. Try again shortly.");
        }

        if (row is null)
        {
            // Path B: no record stands behind this item, so SPE's OBO answer for the caller stays the decision.
            return await next(context);
        }

        var documentId = row.Id;
        var rowDrive = row.GetAttributeValue<string>(ComposeService.GraphDriveIdAttribute)?.Trim();

        // ADR-044: bare-lowercase "D" — the value becomes an OData key predicate in the access data source.
        routeValues[DocumentIdRouteKey] = documentId.ToString("D");

        return await new DocumentAuthorizationFilter(_authorizationService, _operation).InvokeAsync(context, async inner =>
        {
            if (!string.IsNullOrWhiteSpace(rowDrive))
            {
                // Followed AS THE APPLICATION from here, so the row's pointer must verify (409 otherwise, before Graph).
                await _containerResolver
                    .EnsureDocumentPointerContainerAsync(documentId, rowDrive, itemId, httpContext.RequestAborted)
                    .ConfigureAwait(false);

                httpContext.Items[ComposeBrokeredDocument.ItemKey] = new ComposeBrokeredDocument(documentId, rowDrive, itemId);
            }

            return await next(inner);
        });
    }
}
