using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Sprk.Bff.Api.Infrastructure.Authentication;

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
        AuthorizationService authorizationService,
        HttpContext httpContext,
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

        // Was `FindFirst("oid") ?? FindFirst(ClaimTypes.NameIdentifier)` until 2026-08-27. Inbound claim
        // mapping (ON) renames `oid` to the .../objectidentifier schema URI, so the first term was always
        // null and this resolved `sub` — pairwise per user+app, matching no Dataverse systemuser. It
        // reaches the per-row authorization check at :280, so this was not a logging concern. Must stay
        // identical to the filter's resolution; CallerResolution is what keeps the two halves in step.
        var callerObjectId = CallerResolution.ResolveObjectId(httpContext.User);
        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);

        if (string.IsNullOrEmpty(callerObjectId) || string.IsNullOrEmpty(callerToken))
        {
            // The filter proved both present before permitting the request; if either is missing now,
            // the pipeline changed underneath us.
            logger.LogError(
                "Record search reached the handler without a caller identity or bearer token, which the "
                + "authorization filter guarantees. Refusing.");

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Caller context not available.");
        }

        try
        {
            logger.LogDebug(
                "Executing record search: recordTypes=[{RecordTypes}], mode={Mode}",
                string.Join(", ", request.RecordTypes),
                request.Options?.HybridMode ?? "rrf");

            var response = await recordSearchService.SearchAsync(request, cancellationToken);

            response = await AuthorizeRowsAsync(
                response, callerObjectId, callerToken, authorizationService, logger, cancellationToken);

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
                detail: ex.Message,
                statusCode: 500,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = SearchErrorCodes.RecordSearchFailed
                });
        }
    }

    /// <summary>
    /// Upper bound on record-access checks spent assembling one page.
    /// </summary>
    /// <remarks>
    /// Set to the maximum page size the request DTO permits (<c>RecordSearchOptions.Limit</c> is
    /// <c>[Range(1, 50)]</c>), deliberately, so a full page is always fully evaluated. Document search
    /// can afford a budget below its page size because twenty documents often share one parent — one
    /// check covers many rows. Here the row IS the record, so there is no sharing to exploit and a
    /// budget under 50 would silently truncate a legitimate full page. Repeats across requests are
    /// absorbed by <c>CachedAccessDataSource</c>'s entity-set-qualified 60 s key.
    /// </remarks>
    private const int MaxRecordAuthorizationChecks = 50;

    /// <summary>
    /// Authorizes every returned row against its own record and drops what the caller cannot read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The row is the subject.</b> Document search authorizes a document's PARENT (access flows from
    /// the parent per the build plan's invariant 2). A record search result IS a matter/project/invoice,
    /// so the check is directly against that record — no parent hop, and no grouping win either: N rows
    /// cost up to N checks, deduplicated by (type, id).
    /// </para>
    /// <para>
    /// <b>Fail closed per row (ADR-003).</b> A row is dropped unless its <c>RecordType</c> resolves
    /// through the single shared allow-list AND its <c>RecordId</c> parses as a non-empty GUID AND
    /// Dataverse says the caller holds Read. The filter already refused unauthorizable types for the
    /// whole request, so an unresolvable type here means the SERVICE returned a row of a type the caller
    /// never asked for — which is a reason to drop it, not to trust it.
    /// </para>
    /// <para>
    /// <b>Sequential</b>, for the reason documented on the document-search equivalent:
    /// <c>DataverseAccessDataSource</c> assigns <c>_httpClient.DefaultRequestHeaders.Authorization</c>
    /// before issuing its request and that client is shared within one request scope, so concurrent
    /// checks would mutate the header mid-send. <c>HttpHeaders</c> is not thread-safe and the resulting
    /// failure is an intermittent phantom denial.
    /// </para>
    /// <para>
    /// <b>Known limitation (task 077 follow-up F-4).</b> Rows are authorized after the index ranks them,
    /// so a caller entitled to few records can have their matches ranked below the fetched page and
    /// receive a short page that is indistinguishable from "nothing matched". Document search announces
    /// that with a <c>PARTIAL_RESULTS</c> warning; <see cref="RecordSearchMetadata"/> has no warnings
    /// channel, and adding one is a response-contract change that does not belong inside a security fix.
    /// Recorded rather than silently accepted.
    /// </para>
    /// </remarks>
    private static async Task<RecordSearchResponse> AuthorizeRowsAsync(
        RecordSearchResponse response,
        string callerObjectId,
        string callerToken,
        AuthorizationService authorizationService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (response.Results is not { Count: > 0 })
        {
            return response;
        }

        var permitted = new List<RecordSearchResult>(response.Results.Count);
        var decisions = new Dictionary<(string EntitySet, Guid RecordId), bool>();

        var checksSpent = 0;
        var unresolvable = 0;
        var budgetExhausted = false;

        foreach (var row in response.Results)
        {
            if (!SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet(
                    row.RecordType, out var entitySetName)
                || !Guid.TryParse(row.RecordId, out var recordId)
                || recordId == Guid.Empty)
            {
                unresolvable++;
                continue;
            }

            var key = (entitySetName, recordId);
            if (!decisions.TryGetValue(key, out var readable))
            {
                if (checksSpent >= MaxRecordAuthorizationChecks)
                {
                    budgetExhausted = true;
                    break;
                }

                checksSpent++;

                var snapshot = await authorizationService.GetCallerRecordAccessAsync(
                    callerObjectId, entitySetName, recordId, callerToken, cancellationToken);

                readable = snapshot.AccessRights.HasFlag(AccessRights.Read);
                decisions[key] = readable;
            }

            if (readable)
            {
                permitted.Add(row);
            }
        }

        var dropped = response.Results.Count - permitted.Count;
        if (dropped > 0)
        {
            logger.LogInformation(
                "Record search authorization dropped {Dropped} of {Total} rows the caller cannot read "
                + "({Checks} record checks, {Unresolvable} unresolvable rows, budgetExhausted={Budget})",
                dropped, response.Results.Count, checksSpent, unresolvable, budgetExhausted);
        }

        return response with
        {
            Results = permitted,
            Metadata = response.Metadata with
            {
                // The count of rows in THIS response. Leaving the pre-filter total would report how many
                // records exist beyond what was returned — a smaller leak than the records themselves,
                // but the same kind, and on this route the count alone reveals how many matters match a
                // counterparty's name.
                TotalCount = permitted.Count
            }
        };
    }
}
