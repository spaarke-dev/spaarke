using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
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
    /// How many candidate rows to draw from the index per permitted row the caller asked for, when
    /// <c>scope=all</c> results must be authorized row by row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cross-record results are authorized AFTER the index ranks them, so a caller entitled to few
    /// records can have every one of their matches ranked below the page boundary. Fetching exactly
    /// <c>limit</c> rows and then filtering would return an empty page while matches existed further
    /// down — an over-filtering failure that is indistinguishable from "nothing matched". Drawing a
    /// deeper candidate pool is what makes the acceptance criterion ("entitled to 3 of 50 → gets
    /// exactly 3") reachable at all.
    /// </para>
    /// <para>
    /// Bounded at 3×, not more: every fetched row is enriched from Dataverse by
    /// <c>EnrichResultsWithDataverseMetadataAsync</c> before it reaches here, so the candidate pool is
    /// not free — a large factor buys recall with latency the caller pays on every keystroke-driven
    /// search.
    /// </para>
    /// </remarks>
    private const int CrossRecordOverFetchFactor = 3;

    /// <summary>Absolute ceiling on the candidate pool, whatever the requested limit.</summary>
    private const int CrossRecordOverFetchCap = 150;

    /// <summary>
    /// Upper bound on DISTINCT parent records this endpoint will evaluate access for while assembling
    /// one cross-record page.
    /// </summary>
    /// <remarks>
    /// The cost that matters is distinct parents, not rows: twenty documents from one matter cost ONE
    /// Dataverse round trip, and repeats inside the window are absorbed by <c>CachedAccessDataSource</c>.
    /// The typical page touches one to three parents. This budget exists for the pathological page where
    /// every row has a different parent — 25 sequential checks is roughly two seconds worst case, which
    /// is a slow search; 150 would be a broken one. Exhausting the budget does NOT relax the decision:
    /// unexamined rows are dropped and the response is marked incomplete, so the ceiling can only ever
    /// cost recall, never authorization.
    /// </remarks>
    private const int MaxParentAuthorizationChecks = 25;

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
        AuthorizationService authorizationService,
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

            var callerLimit = request.Options?.Limit ?? 20;

            // scope=all draws a deeper candidate pool so that row-level authorization can still fill a
            // page when the caller's readable records rank below the page boundary. Every other scope
            // was already constrained by the filter and is fetched exactly as asked.
            var searchRequest = NarrowToAuthorized(request, authorization);
            if (authorization.RequiresPerRowParentAuthorization)
            {
                searchRequest = WithCrossRecordCandidatePool(searchRequest, callerLimit);
            }

            var response = await searchService.SearchAsync(searchRequest, tenantId, cancellationToken);

            if (authorization.RequiresPerRowParentAuthorization)
            {
                // The filter proved both of these present before it permitted the request; if either is
                // missing now, the pipeline changed underneath us. Refuse rather than serve the
                // unfiltered cross-record answer — that answer IS the disclosure task 070 closed.
                var callerObjectId = ExtractCallerObjectId(httpContext);
                var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);

                if (string.IsNullOrEmpty(callerObjectId) || string.IsNullOrEmpty(callerToken))
                {
                    logger.LogError(
                        "Cross-record search reached the handler without a caller identity or bearer "
                        + "token, which the authorization filter guarantees. Refusing.");

                    return Results.Problem(
                        statusCode: 500,
                        title: "Internal Server Error",
                        detail: "Caller context not available.");
                }

                response = await AuthorizeRowsByParentAsync(
                    response, callerLimit, callerObjectId, callerToken,
                    authorizationService, logger, cancellationToken);
            }
            else
            {
                response = AuthorizeResults(response, authorization, logger);
            }

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

        // scope=all is permitted on /search but REFUSED here, and the asymmetry is the point.
        //
        // /search enforces cross-record access by dropping rows the caller may not read. A COUNT has no
        // rows to drop: the only thing it can return is a number derived from the unfiltered corpus,
        // which discloses how many documents exist tenant-wide. That is a smaller leak than the
        // documents themselves but the same kind, and it is exactly what task 070 closed.
        //
        // Counting only what the caller may read would mean authorizing the whole matching corpus rather
        // than a page — unbounded work for a number, and it needs the accessible-record-set enumeration
        // that task 031 has not delivered. Refusing is honest; a filtered-looking count would not be.
        if (authorization.RequiresPerRowParentAuthorization)
        {
            logger.LogWarning(
                "Semantic search count REFUSED: scope=all cannot be counted without disclosing the "
                + "unfiltered corpus size. Caller should count within a parent record or document set.");

            return Results.Problem(
                statusCode: 403,
                title: "Forbidden",
                detail: "scope=all is not supported for count. Count within a specific parent record "
                        + "(scope=entity) or a specific set of documents (scope=documentIds).",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = SearchErrorCodes.ScopeAllNotPermitted,
                    ["code"] = SearchErrorCodes.ScopeAllNotPermitted,
                    ["correlationId"] = httpContext.TraceIdentifier
                });
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
    /// Widens the index fetch for a cross-record search so row-level authorization has candidates to
    /// work with. See <see cref="CrossRecordOverFetchFactor"/> for why this is necessary and bounded.
    /// </summary>
    /// <remarks>
    /// The DTO's <c>[Range(1, 50)]</c> on <see cref="SearchOptions.Limit"/> has already been enforced
    /// against the CALLER's value by model validation; this is a server-computed internal value that
    /// deliberately exceeds it, and it is never echoed back to the caller.
    /// </remarks>
    private static SemanticSearchRequest WithCrossRecordCandidatePool(
        SemanticSearchRequest request, int callerLimit)
    {
        var poolSize = CrossRecordCandidatePoolSize(callerLimit);
        var options = request.Options ?? new SearchOptions();

        return request with { Options = options with { Limit = poolSize } };
    }

    private static int CrossRecordCandidatePoolSize(int callerLimit) =>
        Math.Min(Math.Max(callerLimit, 1) * CrossRecordOverFetchFactor, CrossRecordOverFetchCap);

    /// <summary>
    /// Authorizes a cross-record (<c>scope=all</c>) result set ROW BY ROW against each document's parent
    /// record, and returns at most <paramref name="callerLimit"/> rows the caller may actually read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Grouped by distinct parent, evaluated in relevance order, lazily.</b> The unit of cost is the
    /// distinct parent record, not the row: a page of twenty documents belonging to one matter is ONE
    /// Dataverse round trip. Rows are walked in the order the index ranked them and evaluation stops as
    /// soon as the caller's page is full, so the common case spends one to three checks. Repeat parents
    /// within a request hit the local memo; repeats across requests hit
    /// <c>CachedAccessDataSource</c>'s entity-set-qualified 60 s key.
    /// </para>
    /// <para>
    /// <b>Fail closed per row (ADR-003).</b> A row is dropped unless its parent type resolves to an
    /// authorizable table AND its parent id parses as a non-empty GUID AND Dataverse says the caller
    /// holds Read. "Unknown parentage" is the case that must not be served, so it is never a skip.
    /// </para>
    /// <para>
    /// <b>Sequential, deliberately</b> — the same constraint documented on task 070's documentIds loop.
    /// <c>DataverseAccessDataSource</c> assigns <c>_httpClient.DefaultRequestHeaders.Authorization</c>
    /// before issuing its request, and that client instance is shared within one HTTP request scope;
    /// running these checks concurrently would mutate that header while siblings were mid-send.
    /// <c>HttpHeaders</c> is not thread-safe, and the resulting failure is an intermittent phantom
    /// denial — the kind of flakiness that gets an authorization gate disabled rather than debugged.
    /// </para>
    /// <para>
    /// <b>Why a short page is announced.</b> Because rows are authorized after the index ranks them, a
    /// caller entitled to few records can have their matches ranked below the candidate pool, or below
    /// the check budget. The result is a page shorter than requested — which is indistinguishable, by
    /// inspection, from "there simply are not many matches". That is the failure mode this whole task has
    /// to defeat: it looks like success. A <c>PARTIAL_RESULTS</c> warning is what makes it legible.
    /// </para>
    /// </remarks>
    private static async Task<SemanticSearchResponse> AuthorizeRowsByParentAsync(
        SemanticSearchResponse response,
        int callerLimit,
        string callerObjectId,
        string callerToken,
        AuthorizationService authorizationService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var permitted = new List<SearchResult>(callerLimit);
        var decisions = new Dictionary<(string EntitySet, Guid ParentId), bool>();

        var examined = 0;
        var checksSpent = 0;
        var unresolvableParents = 0;
        var budgetExhausted = false;

        foreach (var row in response.Results)
        {
            if (permitted.Count >= callerLimit)
            {
                break;
            }

            examined++;

            if (!SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet(
                    row.ParentEntityType, out var entitySetName)
                || !Guid.TryParse(row.ParentEntityId, out var parentId)
                || parentId == Guid.Empty)
            {
                unresolvableParents++;
                continue;
            }

            var key = (entitySetName, parentId);
            if (!decisions.TryGetValue(key, out var readable))
            {
                if (checksSpent >= MaxParentAuthorizationChecks)
                {
                    // Stop spending. Everything not yet evaluated stays unserved.
                    budgetExhausted = true;
                    examined--;
                    break;
                }

                checksSpent++;

                var snapshot = await authorizationService.GetCallerRecordAccessAsync(
                    callerObjectId, entitySetName, parentId, callerToken, cancellationToken);

                readable = snapshot.AccessRights.HasFlag(AccessRights.Read);
                decisions[key] = readable;
            }

            if (readable)
            {
                permitted.Add(row);
            }
        }

        // Pointers stripped for the same reason as every other scope: under the broker-only decision no
        // client needs driveId/speFileId, and returning them invites clients to address SPE directly.
        var sanitized = permitted
            .Select(r => r with { DriveId = null, SpeFileId = null })
            .ToList();

        var droppedForAccess = examined - permitted.Count;

        // "This page may be missing documents you are entitled to see." True in exactly two cases:
        //
        //   1. The parent-check budget ran out, so rows were left unevaluated. Always incomplete.
        //   2. The candidate pool came back SATURATED — the index had at least as many rows as we were
        //      willing to draw — and we still could not fill the caller's page. The shortfall might have
        //      been fillable from rows we never fetched.
        //
        // Deliberately NOT `droppedForAccess > 0`, which was the first cut: that fires whenever ANY row
        // was withheld, including the common case where the pool was not saturated and we therefore
        // examined every matching document there was. That warning would be present on nearly every
        // filtered cross-record search, and a warning that always fires is noise — which gets ignored,
        // which restores exactly the blindness it exists to cure.
        //
        // A full page is not incomplete: the caller got what they asked for, and matches ranked below it
        // are a paging question, not an authorization one (see the paging contract, task 080 notes §1).
        var poolWasSaturated = response.Results.Count >= CrossRecordCandidatePoolSize(callerLimit);
        var incomplete = budgetExhausted || (poolWasSaturated && sanitized.Count < callerLimit);

        var warnings = response.Metadata.Warnings is { } existing
            ? new List<SearchWarning>(existing)
            : new List<SearchWarning>();

        if (incomplete)
        {
            warnings.Add(new SearchWarning
            {
                Code = SearchWarningCode.PartialResults,
                Message = "Some matching documents were withheld because you do not have access to their "
                          + "parent records, and further matches may exist beyond the results examined. "
                          + "Search within a specific record for a complete list."
            });
        }

        logger.LogInformation(
            "Cross-record search authorized for caller {CallerId}: {Permitted} of {Examined} examined rows "
            + "permitted ({Checks} distinct parents evaluated, {Unresolvable} rows had unauthorizable "
            + "parentage, budgetExhausted={BudgetExhausted}, incomplete={Incomplete})",
            callerObjectId, sanitized.Count, examined, checksSpent, unresolvableParents,
            budgetExhausted, incomplete);

        return response with
        {
            Results = sanitized,
            Metadata = response.Metadata with
            {
                // The count of rows in THIS response — the paging contract in the task 080 notes §1.
                //
                // Deliberately NOT `TotalResults - dropped`, which is what the request-scoped path above
                // does. That subtraction is right there because the pre-filter total describes the same
                // page being filtered. Here the pre-filter total describes the OVER-FETCHED candidate
                // pool, so subtracting drops from it would report a number drawn from a page the caller
                // never sees. What the caller can actually reach is exactly what is returned.
                TotalResults = sanitized.Count,
                ReturnedResults = sanitized.Count,
                Warnings = warnings.Count > 0 ? warnings : null
            }
        };
    }

    /// <summary>
    /// Extract the caller's object id from Azure AD token claims. Mirrors the filter's extraction so both
    /// halves of the decision identify the caller identically.
    /// </summary>
    private static string? ExtractCallerObjectId(HttpContext httpContext) =>
        httpContext.User.FindFirst("oid")?.Value
        ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

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
