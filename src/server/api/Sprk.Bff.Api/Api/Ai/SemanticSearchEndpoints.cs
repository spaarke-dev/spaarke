using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Semantic search endpoints for hybrid search (vector + keyword).
/// Follows ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// </summary>
/// <remarks>
/// <para>
/// Provides endpoints for:
/// - Hybrid semantic search (RRF, vector-only, keyword-only modes)
/// - Document count for pagination
/// </para>
/// <para>
/// Multi-tenant support via tenantId extracted from user's Azure AD token.
/// Scope-based authorization via SemanticSearchAuthorizationFilter.
/// </para>
/// </remarks>
public static class SemanticSearchEndpoints
{
    /// <summary>
    /// Maps semantic search endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapSemanticSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/search")
            .RequireAuthorization()
            .WithTags("AI Semantic Search");

        // POST /api/ai/search - Execute semantic search
        group.MapPost("/", Search)
            .AddSemanticSearchAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("SemanticSearch")
            .WithSummary("Execute hybrid semantic search")
            .WithDescription("Executes hybrid search combining vector and keyword search with optional semantic ranking. Supports RRF (default), vector-only, and keyword-only modes.")
            .Produces<SemanticSearchResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        // POST /api/ai/search/count - Get count of matching documents
        group.MapPost("/count", Count)
            .AddSemanticSearchAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("SemanticSearchCount")
            .WithSummary("Get count of matching documents")
            .WithDescription("Returns the count of documents matching the search criteria without retrieving full results. Useful for pagination UI.")
            .Produces<SemanticSearchCountResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Execute hybrid semantic search.
    /// </summary>
    private static async Task<IResult> Search(
        [FromBody] SemanticSearchRequest request,
        ISemanticSearchService searchService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("SemanticSearchEndpoints");

        // Extract tenant ID from Azure AD token
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity not found in authentication token");
        }

        // Validate scope
        var scopeValidation = ValidateScope(request);
        if (scopeValidation != null)
        {
            return scopeValidation;
        }

        // Validate filters
        var filtersValidation = ValidateFilters(request);
        if (filtersValidation != null)
        {
            return filtersValidation;
        }

        try
        {
            logger.LogDebug(
                "Executing semantic search for tenant {TenantId}, scope={Scope}, mode={Mode}",
                tenantId, request.Scope, request.Options?.HybridMode ?? "rrf");

            var response = await searchService.SearchAsync(request, tenantId, cancellationToken);

            logger.LogInformation(
                "Semantic search completed for tenant {TenantId}: {ReturnedResults}/{TotalResults} results in {DurationMs}ms",
                tenantId, response.Metadata?.ReturnedResults, response.Metadata?.TotalResults, response.Metadata?.SearchDurationMs);

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Semantic search failed for tenant {TenantId}", tenantId);
            return Results.Problem(
                title: "Search Failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Get count of documents matching search criteria.
    /// </summary>
    private static async Task<IResult> Count(
        [FromBody] SemanticSearchRequest request,
        ISemanticSearchService searchService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("SemanticSearchEndpoints");

        // Extract tenant ID from Azure AD token
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity not found in authentication token");
        }

        // Validate scope
        var scopeValidation = ValidateScope(request);
        if (scopeValidation != null)
        {
            return scopeValidation;
        }

        // Validate filters
        var filtersValidation = ValidateFilters(request);
        if (filtersValidation != null)
        {
            return filtersValidation;
        }

        try
        {
            logger.LogDebug(
                "Executing semantic search count for tenant {TenantId}, scope={Scope}",
                tenantId, request.Scope);

            var response = await searchService.CountAsync(request, tenantId, cancellationToken);

            logger.LogInformation(
                "Semantic search count completed for tenant {TenantId}: {Count} documents",
                tenantId, response.Count);

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Semantic search count failed for tenant {TenantId}", tenantId);
            return Results.Problem(
                title: "Count Failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Extract tenant ID from Azure AD token claims.
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
    {
        const string TidClaim = "tid";
        const string AltTidClaim = "http://schemas.microsoft.com/identity/claims/tenantid";

        return httpContext.User.FindFirst(TidClaim)?.Value
            ?? httpContext.User.FindFirst(AltTidClaim)?.Value;
    }

    /// <summary>
    /// Validate request scope.
    /// </summary>
    private static IResult? ValidateScope(SemanticSearchRequest request)
    {
        // Validate scope value
        if (!SearchScope.IsValid(request.Scope))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Scope",
                Detail = $"Invalid scope value '{request.Scope}'. Valid values: all, entity, documentIds.",
                Status = 400,
                Extensions = { ["code"] = SearchErrorCodes.InvalidScope }
            });
        }

        // Validate entity scope has required fields
        if (string.Equals(request.Scope, SearchScope.Entity, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.EntityType))
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Entity Type Required",
                    Detail = "entityType is required when scope=entity.",
                    Status = 400,
                    Extensions = { ["code"] = SearchErrorCodes.EntityTypeRequired }
                });
            }

            if (string.IsNullOrWhiteSpace(request.EntityId))
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Entity ID Required",
                    Detail = "entityId is required when scope=entity.",
                    Status = 400,
                    Extensions = { ["code"] = SearchErrorCodes.EntityIdRequired }
                });
            }
        }

        // Validate documentIds scope has required fields
        if (string.Equals(request.Scope, SearchScope.DocumentIds, StringComparison.OrdinalIgnoreCase))
        {
            if (request.DocumentIds == null || request.DocumentIds.Count == 0)
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Document IDs Required",
                    Detail = "documentIds is required and must not be empty when scope=documentIds.",
                    Status = 400,
                    Extensions = { ["code"] = SearchErrorCodes.DocumentIdsRequired }
                });
            }
        }

        return null; // Validation passed
    }

    /// <summary>
    /// Validate request filters (entityTypes values).
    /// </summary>
    private static IResult? ValidateFilters(SemanticSearchRequest request)
    {
        if (request.Filters?.EntityTypes is { Count: > 0 } entityTypes)
        {
            var invalidTypes = entityTypes
                .Where(t => !ValidEntityTypes.IsValid(t))
                .ToList();

            if (invalidTypes.Count > 0)
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Entity Types",
                    Detail = $"Invalid entityTypes value(s): {string.Join(", ", invalidTypes.Select(t => $"'{t}'"))}. Valid values: {string.Join(", ", ValidEntityTypes.All)}.",
                    Status = 400,
                    Extensions =
                    {
                        ["code"] = SearchErrorCodes.InvalidEntityTypes,
                        ["invalidValues"] = invalidTypes,
                        ["validValues"] = ValidEntityTypes.All
                    }
                });
            }
        }

        return null; // Validation passed
    }
}
