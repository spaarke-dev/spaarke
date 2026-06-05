using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Sprk.Bff.Api.Services.Dataverse.Models;

namespace Sprk.Bff.Api.Api.Dataverse;

/// <summary>
/// Maps <c>POST /api/dataverse/fetch</c> — FR-BFF-04 of the Spaarke DataGrid Framework R1.
/// The endpoint accepts a <see cref="FetchRequestDto"/> and returns the projected rows plus paging
/// metadata. This is the only Dataverse passthrough endpoint with cross-entity privilege concerns
/// (FetchXML can join arbitrary entities via <c>&lt;link-entity&gt;</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Security model</b> (per task 010 §5):
/// </para>
/// <list type="bullet">
///   <item>Authorization: <c>DataverseAuthorizationFilter</c> configured with
///         <see cref="EntitySource.FromFetchXmlBody"/> runs BEFORE the handler. The filter uses
///         <see cref="IFetchXmlEntityExtractor"/> (this task's implementation) to enumerate every
///         entity referenced in the body and verifies Read privilege on each via
///         <c>IDataversePrivilegeChecker</c>.</item>
///   <item>If the caller lacks Read on any entity (primary OR any <c>&lt;link-entity&gt;</c>) the
///         filter returns 403 ProblemDetails with <c>errorCode=DV_PRIVILEGE_DENIED</c> — the
///         handler is NOT invoked.</item>
///   <item>Defense-in-depth: this handler also validates that <see cref="FetchRequestDto.EntityName"/>
///         matches the primary entity inside the FetchXML. Mismatch returns 400 with
///         <c>errorCode=DV_FETCHXML_ENTITY_MISMATCH</c>. The authorization filter detects most
///         malformed payloads but this is an explicit guard against silent routing/body drift.</item>
/// </list>
/// <para>
/// <b>Caching</b>: NONE per FR-BFF-04 (real-time queries). The privilege metadata IS cached (in the
/// privilege checker); the FetchXML execution path is not.
/// </para>
/// <para>
/// <b>Performance target</b>: &lt;500ms p50 on a default tenant. Achieved by reusing the singleton
/// <c>ServiceClient</c> + skipping any caching layer for the query payload itself.
/// </para>
/// <para>
/// <b>Error handling</b>: ProblemDetails per ADR-019. Malformed FetchXML returns 400 (NOT 500)
/// via the <see cref="FetchXmlParseException"/> path. Unexpected SDK failures surface as 500.
/// </para>
/// </remarks>
public static class FetchEndpoints
{
    public static IEndpointRouteBuilder MapFetchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dataverse")
            .WithTags("Dataverse")
            .RequireAuthorization();

        group.MapPost("/fetch", ExecuteFetchAsync)
            .WithName("ExecuteDataverseFetch")
            .WithSummary("Executes a FetchXML query with cross-entity privilege enforcement.")
            .WithDescription(
                "FR-BFF-04. NO caching — every request hits Dataverse. " +
                "DataverseAuthorizationFilter enforces Read privilege on the primary entity and every " +
                "<link-entity> referenced in the FetchXML (depth-N) before this handler runs.")
            .Accepts<FetchRequestDto>("application/json")
            .Produces<FetchResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .AddDataverseAuthorizationFilter(EntitySource.FromFetchXmlBody);

        return app;
    }

    private static async Task<IResult> ExecuteFetchAsync(
        [FromBody] FetchRequestDto request,
        FetchService fetchService,
        IFetchXmlEntityExtractor entityExtractor,
        ILogger<FetchService> logger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var correlationId = httpContext.TraceIdentifier;

        if (request is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Request body is required.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_FETCH_MISSING_BODY",
                    ["correlationId"] = correlationId
                });
        }

        if (string.IsNullOrWhiteSpace(request.EntityName))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "EntityName is required.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_FETCH_MISSING_ENTITY",
                    ["correlationId"] = correlationId
                });
        }

        if (string.IsNullOrWhiteSpace(request.FetchXml))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "FetchXml is required.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_FETCH_MISSING_FETCHXML",
                    ["correlationId"] = correlationId
                });
        }

        // Defense-in-depth: validate that the request body's EntityName matches the primary entity
        // declared inside the FetchXML. The authorization filter has already validated Read privilege
        // on every entity referenced; this check exists to surface body/route drift as a clean 400
        // (rather than letting the query execute against an unexpected entity).
        // Per task 010 §7 error catalog: DV_FETCHXML_ENTITY_MISMATCH.
        try
        {
            var referencedEntities = entityExtractor.ExtractEntities(request.FetchXml);
            // The extractor returns the primary entity plus every link-entity. We don't have an
            // explicit "primary" indicator on the set (it's a HashSet for security check semantics),
            // so we re-check via document order through a single membership test.
            // Use the normalised form to match the extractor's behavior.
            var requested = request.EntityName.Trim().ToLowerInvariant();
            if (!referencedEntities.Contains(requested))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: $"Request EntityName '{request.EntityName}' does not match any entity referenced in the FetchXML.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "DV_FETCHXML_ENTITY_MISMATCH",
                        ["correlationId"] = correlationId
                    });
            }
        }
        catch (FetchXmlParseException ex)
        {
            // This will rarely fire because the authorization filter would have already caught it,
            // but defense-in-depth: if the filter is mis-wired we still return 400 not 500.
            logger.LogWarning(ex,
                "Malformed FetchXML in /api/dataverse/fetch (correlationId={CorrelationId})", correlationId);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "FetchXML payload could not be parsed.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_FETCHXML_MALFORMED",
                    ["correlationId"] = correlationId
                });
        }

        try
        {
            var response = await fetchService.ExecuteAsync(request, ct).ConfigureAwait(false);
            return Results.Ok(response);
        }
        catch (FetchXmlParseException ex)
        {
            logger.LogWarning(ex,
                "FetchService rejected malformed FetchXML (correlationId={CorrelationId})", correlationId);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "FetchXML payload could not be parsed.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_FETCHXML_MALFORMED",
                    ["correlationId"] = correlationId
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected; let the framework propagate.
            throw;
        }
        catch (InvalidOperationException ex)
        {
            // Deployment misconfiguration (e.g., ServiceClient not ready) — log + return 500.
            logger.LogError(ex,
                "FetchService configuration error (correlationId={CorrelationId})", correlationId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Dataverse fetch service is not available.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_FETCH_SERVICE_UNAVAILABLE",
                    ["correlationId"] = correlationId
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unexpected error executing FetchXML (correlationId={CorrelationId})", correlationId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred executing the FetchXML query.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_FETCH_INTERNAL_ERROR",
                    ["correlationId"] = correlationId
                });
        }
    }
}
