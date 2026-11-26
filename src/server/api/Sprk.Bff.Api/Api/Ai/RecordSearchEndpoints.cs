using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.RecordSearch;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Record search endpoints for hybrid search against Dataverse entity records.
/// Follows ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// </summary>
/// <remarks>
/// <para>
/// Provides endpoint for:
/// - Hybrid semantic search across Dataverse records (Matters, Projects, Invoices)
/// </para>
/// <para>
/// Authorization via RecordSearchAuthorizationFilter (ADR-008).
/// Rate limiting via "ai-batch" policy (ADR-016).
/// </para>
/// </remarks>
public static class RecordSearchEndpoints
{
    /// <summary>
    /// Maps record search endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapRecordSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/search")
            .RequireAuthorization()
            .WithTags("AI Record Search");

        // POST /api/ai/search/records - Execute record search
        group.MapPost("/records", PostRecordSearch)
            .AddRecordSearchAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("PostRecordSearch")
            .WithTags("AI")
            .WithSummary("Execute hybrid record search")
            .WithDescription("Searches Dataverse entity records (Matters, Projects, Invoices) using hybrid semantic + keyword search against the spaarke-records-index.")
            .Produces<RecordSearchResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Execute hybrid record search.
    /// </summary>
    // `internal`, not `private` (spaarkeai-word-add-in-r1 task 077): so a test can drive the SHIPPED
    // handler — its fail-closed check and its per-row trim — rather than a re-implementation of them.
    // Same reason, and same precedent, as VisualizationEndpoints.GetRelatedDocuments. Task 077 relies on
    // this route's per-row authorization for Find's records half, and until now nothing tested it.
    internal static async Task<IResult> PostRecordSearch(
        [FromBody] RecordSearchRequest request,
        IRecordSearchService recordSearchService,
        AuthorizationService authorizationService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("RecordSearchEndpoints");

        // Validate: Query not empty
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Results.Problem(new ProblemDetails
            {
                Title = "Query Required",
                Detail = "query is required and must not be empty.",
                Status = 400,
                Extensions = { ["code"] = SearchErrorCodes.QueryRequired }
            });
        }

        // Validate: bounds. The [StringLength]/[Range] attributes on the DTOs are metadata only (no
        // validation filter runs on this route), so enforce them here, from the same constants the
        // attributes use. Without this, limit:0 / offset:-1 reached Azure AI Search and came back 500.
        if (request.Query.Length > RecordSearchRequest.MaxQueryLength)
        {
            return BadRequest(
                "Query Too Long",
                $"query must not exceed {RecordSearchRequest.MaxQueryLength} characters.",
                SearchErrorCodes.QueryTooLong);
        }

        if (request.Options is { } options)
        {
            if (options.Limit is < RecordSearchOptions.MinLimit or > RecordSearchOptions.MaxLimit)
            {
                return BadRequest(
                    "Invalid Limit",
                    $"options.limit must be between {RecordSearchOptions.MinLimit} and {RecordSearchOptions.MaxLimit}.",
                    SearchErrorCodes.InvalidLimit);
            }

            if (options.Offset is < RecordSearchOptions.MinOffset or > RecordSearchOptions.MaxOffset)
            {
                return BadRequest(
                    "Invalid Offset",
                    $"options.offset must be between {RecordSearchOptions.MinOffset} and {RecordSearchOptions.MaxOffset}.",
                    SearchErrorCodes.InvalidOffset);
            }
        }

        // Validate: RecordTypes not empty
        if (request.RecordTypes == null || request.RecordTypes.Count == 0)
        {
            return Results.Problem(new ProblemDetails
            {
                Title = "Record Types Required",
                Detail = "recordTypes is required and must contain at least one record type.",
                Status = 400,
                Extensions = { ["code"] = SearchErrorCodes.InvalidRecordTypes }
            });
        }

        // Validate: RecordTypes values are valid
        var invalidTypes = request.RecordTypes
            .Where(t => !RecordEntityType.IsValid(t))
            .ToList();

        if (invalidTypes.Count > 0)
        {
            return Results.Problem(new ProblemDetails
            {
                Title = "Invalid Record Types",
                Detail = $"Invalid recordTypes value(s): {string.Join(", ", invalidTypes.Select(t => $"'{t}'"))}. Valid values: {string.Join(", ", RecordEntityType.ValidTypes)}.",
                Status = 400,
                Extensions =
                {
                    ["code"] = SearchErrorCodes.InvalidRecordTypes,
                    ["invalidValues"] = invalidTypes,
                    ["validValues"] = RecordEntityType.ValidTypes
                }
            });
        }

        // The filter's decision. Absent = the filter did not run: refuse rather than serve unfiltered.
        // This is the forcing function — if someone detaches AddRecordSearchAuthorizationFilter, this
        // route stops answering instead of quietly reverting to the tenant-wide enumeration it used to be.
        if (httpContext.Items[RecordSearchAuthorization.HttpContextItemsKey]
            is not RecordSearchAuthorization authorization
            || !authorization.RequiresPerRowRecordAuthorization)
        {
            logger.LogError(
                "Record search reached the handler with no authorization decision — "
                + "AddRecordSearchAuthorizationFilter is not applied to this route. Refusing.");

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Authorization context not available.");
        }

        try
        {
            logger.LogDebug(
                "Executing record search: recordTypes=[{RecordTypes}], mode={Mode}",
                string.Join(", ", request.RecordTypes),
                request.Options?.HybridMode ?? "rrf");

            // The per-row record authorization the filter's decision obliges has ONE owner, shared with the MCP record
            // search tool (spaarkeai-mcp-bff-api-r2 task 027): it reads the caller's identity and token before the search
            // runs, then checks every row as the caller. This handler binds and maps.
            var access = await RecordSearchAuthorizationFilter.SearchAndAuthorizeRowsAsync(
                httpContext, request, recordSearchService.SearchAsync, authorizationService, logger, cancellationToken);

            if (access is not { Outcome: RecordSearchAccessOutcome.Allowed, Response: { } response })
            {
                // CallerContextUnavailable: no caller identity or bearer token after the filter had seen both. The
                // search did not run.
                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "Caller context not available.");
            }

            logger.LogInformation(
                "Record search completed: {ReturnedResults}/{TotalCount} results in {SearchTime}ms",
                response.Results?.Count ?? 0,
                response.Metadata?.TotalCount,
                response.Metadata?.SearchTime);

            return Results.Ok(response);
        }
        catch (Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException)
        {
            // multi-container-multi-index-r1 FR-BFF-07 (task 016) — rethrow so the
            // global `UseExceptionHandler` middleware renders the canonical
            // ProblemDetails JSON per ADR-019 (e.g., 400 INDEX_NOT_ALLOWED).
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Record search failed for recordTypes=[{RecordTypes}]",
                string.Join(", ", request.RecordTypes));

            return Results.Problem(
                title: "Record Search Failed",
                // Fixed text, never ex.Message: exception text can carry endpoints, index names and
                // query fragments. The exception is logged above.
                detail: "The record search could not be completed. Try again, and contact support if it persists.",
                statusCode: 500,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = SearchErrorCodes.RecordSearchFailed
                });
        }
    }

    private static IResult BadRequest(string title, string detail, string code) =>
        Results.Problem(new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = 400,
            Extensions = { ["code"] = code }
        });
}
