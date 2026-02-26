using Microsoft.AspNetCore.Mvc;
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
    private static async Task<IResult> PostRecordSearch(
        [FromBody] RecordSearchRequest request,
        IRecordSearchService recordSearchService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("RecordSearchEndpoints");

        // Validate: Query not empty
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Query Required",
                Detail = "query is required and must not be empty.",
                Status = 400,
                Extensions = { ["code"] = SearchErrorCodes.QueryRequired }
            });
        }

        // Validate: RecordTypes not empty
        if (request.RecordTypes == null || request.RecordTypes.Count == 0)
        {
            return Results.BadRequest(new ProblemDetails
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
            return Results.BadRequest(new ProblemDetails
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

        try
        {
            logger.LogDebug(
                "Executing record search: recordTypes=[{RecordTypes}], mode={Mode}",
                string.Join(", ", request.RecordTypes),
                request.Options?.HybridMode ?? "rrf");

            var response = await recordSearchService.SearchAsync(request, cancellationToken);

            logger.LogInformation(
                "Record search completed: {ReturnedResults}/{TotalCount} results in {SearchTime}ms",
                response.Results?.Count ?? 0,
                response.Metadata?.TotalCount,
                response.Metadata?.SearchTime);

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Record search failed for recordTypes=[{RecordTypes}]",
                string.Join(", ", request.RecordTypes));

            return Results.Problem(
                title: "Record Search Failed",
                detail: ex.Message,
                statusCode: 500,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = SearchErrorCodes.RecordSearchFailed
                });
        }
    }
}
