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

        // The filter's decision. Absent = the filter did not run: refuse rather than serve unfiltered.
        if (httpContext.Items[SemanticSearchAuthorization.HttpContextItemsKey]
            is not SemanticSearchAuthorization authorization)
        {
            logger.LogError(
                "Semantic search reached the handler with no authorization decision — "
                + "AddSemanticSearchAuthorizationFilter is not applied to this route. Refusing.");

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Authorization context not available.");
        }

        try
        {
            logger.LogDebug(
                "Executing semantic search for tenant {TenantId}, scope={Scope}, mode={Mode}",
                tenantId, request.Scope, request.Options?.HybridMode ?? "rrf");

            var response = await searchService.SearchAsync(
                NarrowToAuthorized(request, authorization), tenantId, cancellationToken);

            response = AuthorizeResults(response, authorization, logger);

            logger.LogInformation(
                "Semantic search completed for tenant {TenantId}: {ReturnedResults}/{TotalResults} results in {DurationMs}ms",
                tenantId, response.Metadata?.ReturnedResults, response.Metadata?.TotalResults, response.Metadata?.SearchDurationMs);

            return Results.Ok(response);
        }
        catch (Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException)
        {
            // multi-container-multi-index-r1 FR-BFF-07 (task 016) — rethrow so the
            // global `UseExceptionHandler` middleware (MiddlewarePipelineExtensions)
            // renders the canonical ProblemDetails JSON per ADR-019. Without this,
            // the generic `catch (Exception)` below would convert
            // `INDEX_NOT_ALLOWED` (statusCode 400) into a 500 response — breaking
            // NFR-08 (rejected index name MUST surface as ProblemDetails 400).
            throw;
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

        if (httpContext.Items[SemanticSearchAuthorization.HttpContextItemsKey]
            is not SemanticSearchAuthorization authorization)
        {
            logger.LogError(
                "Semantic search count reached the handler with no authorization decision — "
                + "AddSemanticSearchAuthorizationFilter is not applied to this route. Refusing.");

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Authorization context not available.");
        }

        try
        {
            logger.LogDebug(
                "Executing semantic search count for tenant {TenantId}, scope={Scope}",
                tenantId, request.Scope);

            // The count has no rows to post-filter, so narrowing the REQUEST is the whole enforcement
            // here: for scope=documentIds the query is reduced to the ids the caller may read, and for
            // scope=entity the parent was authorized by the filter and SearchFilterBuilder constrains
            // the query to it.
            var response = await searchService.CountAsync(
                NarrowToAuthorized(request, authorization), tenantId, cancellationToken);

            logger.LogInformation(
                "Semantic search count completed for tenant {TenantId}: {Count} documents",
                tenantId, response.Count);

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
            logger.LogError(ex, "Semantic search count failed for tenant {TenantId}", tenantId);
            return Results.Problem(
                title: "Count Failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Reduces the request to what the caller was actually authorized for, before the search runs.
    /// </summary>
    /// <remarks>
    /// For <c>scope=documentIds</c> the caller may name documents they cannot read; the filter worked
    /// out which subset is permitted, and this narrows the query to that subset so unauthorized ids
    /// never reach the index or Dataverse at all. For <c>scope=entity</c> nothing needs narrowing — the
    /// parent itself was authorized, and the existing filter expression already scopes to it.
    /// </remarks>
    private static SemanticSearchRequest NarrowToAuthorized(
        SemanticSearchRequest request, SemanticSearchAuthorization authorization)
    {
        // ADR-044 — canonicalize the ids at the boundary this filter now owns.
        //
        // The filter parses entityId with Guid.TryParse, which happily accepts `{ABC-...}` and uppercase
        // forms, and then discarded the parsed value. The RAW caller string flowed on to
        // SearchFilterBuilder, which emits `parentEntityId eq '<raw>'` — a CASE-SENSITIVE Edm.String
        // comparison. So a braced or upper-cased GUID from Xrm would pass authorization and then match
        // zero index rows: an authorization layer and a query layer disagreeing about identity. Writing
        // back the canonical form the filter already computed removes the disagreement.
        if (authorization.AuthorizedParentId is { } parentId)
        {
            return request with { EntityId = parentId.ToString() };
        }

        if (authorization.AuthorizedDocumentIds is not { } allowed)
        {
            return request;
        }

        return request with
        {
            DocumentIds = request.DocumentIds?
                .Where(allowed.Contains)
                .Select(id => Guid.TryParse(id, out var g) ? g.ToString() : id)
                .ToList() ?? []
        };
    }

    /// <summary>
    /// Enforces the authorization decision on the RESULTS, and strips SPE pointers from the response.
    /// </summary>
    /// <remarks>
    /// <para><b>Why results are re-checked when the query was already scoped.</b> The semantic path
    /// answers from an Azure AI Search index, which is a separate data plane carrying no ACL data and no
    /// guarantee of freshness. A filter expression there is a query predicate, not an authorization
    /// decision: if a document is reparented in Dataverse and the index still holds the old parent, a
    /// parent-scoped query returns a document that no longer belongs to the authorized parent. Checking
    /// each row's parent against the authorized parent closes that, and costs nothing — the value is
    /// already on the row, so there is no extra round trip.</para>
    ///
    /// <para><b>Counts are recomputed.</b> Leaving <c>totalResults</c> at the pre-filter number would
    /// report how many documents exist beyond what was returned — a smaller leak than the documents
    /// themselves, but the same kind.</para>
    ///
    /// <para><b>Pointers are stripped unconditionally.</b> Under the broker-only decision no client needs
    /// <c>driveId</c>/<c>speFileId</c>: file access goes through document-id-keyed BFF routes that carry
    /// the standard gate. Returning pointers invites clients to address SPE directly, which is the
    /// pattern that produced the ungated drive-keyed routes in the first place.</para>
    /// </remarks>
    private static SemanticSearchResponse AuthorizeResults(
        SemanticSearchResponse response,
        SemanticSearchAuthorization authorization,
        ILogger logger)
    {
        var permitted = response.Results.Where(r => IsPermitted(r, authorization)).ToList();

        var dropped = response.Results.Count - permitted.Count;
        if (dropped > 0)
        {
            // Not routine. Reaching here means the query returned rows outside the authorized scope,
            // which for scope=entity means the index disagrees with Dataverse about parentage.
            logger.LogWarning(
                "Semantic search dropped {Dropped} of {Total} results that fell outside the authorized "
                + "scope (scope={Scope}). Expected zero — investigate index/Dataverse divergence.",
                dropped, response.Results.Count, authorization.Scope);
        }

        // Strip SPE pointers from every surviving row.
        var sanitized = permitted
            .Select(r => r with { DriveId = null, SpeFileId = null })
            .ToList();

        return response with
        {
            Results = sanitized,
            Metadata = response.Metadata with
            {
                // Subtract what authorization removed — do NOT clamp to the page size.
                //
                // An earlier draft used Math.Min(TotalResults, sanitized.Count). That is leak-free but
                // arithmetically wrong wherever TotalResults is a CORPUS total rather than a page count:
                // the associated-only path reports the full match count, so a matter with 100 documents
                // and limit=20 would have reported 20 and quietly broken pagination. The AI-Search path
                // reports a page-level count, where both forms agree — which is why the existing tests
                // could not tell the two apart.
                TotalResults = Math.Max(0, response.Metadata.TotalResults - dropped),
                ReturnedResults = sanitized.Count
            }
        };
    }

    private static bool IsPermitted(SearchResult result, SemanticSearchAuthorization authorization)
    {
        if (authorization.AuthorizedDocumentIds is { } allowedDocuments)
        {
            return result.DocumentId is { } id && allowedDocuments.Contains(id);
        }

        if (authorization.AuthorizedParentId is { } parentId)
        {
            // Fail closed on a row with no parent: it cannot be shown to belong to the authorized
            // parent, and "unknown parentage" is exactly the case that must not be served.
            return Guid.TryParse(result.ParentEntityId, out var rowParent) && rowParent == parentId;
        }

        return false;
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
