// spaarke-SPA-external-access-platform-r2 Task 015 (2026-08-06) — FR-22 BffDataverseClient widget-data seam.
//
// The per-module READ-DATA endpoint group that satisfies the client-side read-only BffDataverseClient
// (IDataverseClient) contract for the dual-plane external module-host platform (ADR-028 A3). It is
// mounted UNDER the shared /api/v1/external group, so it inherits — unchanged — the ExternalCollaboration
// dual-scheme policy AND the group-level CallerPrincipalAuthorizationFilter (the generalized resolver).
// Every handler therefore receives a plane-agnostic CallerPrincipal on HttpContext.Items and NEVER
// branches on plane / scheme / iss / tid.
//
// The routes carry the /api/dataverse/* suffix BffDataverseClient appends to its bffBaseUrl, so a widget
// consumes this group by pointing its BffDataverseClient at bffBaseUrl = {host}/api/v1/external — with
// NO fork of the client (R2 FR-22 / task 015 acceptance criterion #4).
//
// Tier-2 scoping (R2 NFR-08): the data-returning reads (fetch, record) are scoped by the requested
// module's registered Tier-2 predicate (ExternalModuleRegistry). A non-participant caller receives an
// empty result (fetch) or 403 (record) — being authenticated does NOT reveal all records. Schema/view
// reads (metadata, savedquery, savedqueries) return no record data and are app-only passthrough.
//
// Broker-only (ADR-028 A1/A2/A3 · NFR-02): all reads execute APP-ONLY via the existing Dataverse read
// services — no OBO / no caller-token exchange, no Graph pointer ever reaches the client, keyed on
// record id. ADR-008: authorization via the inherited group filter (no global middleware). ADR-019:
// ProblemDetails on every failure.

using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Sprk.Bff.Api.Services.Dataverse.Models;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// Maps the per-module read-data endpoints consumed by the read-only <c>BffDataverseClient</c> over the
/// generalized module-host read seam (FR-22 · ADR-028 A3). See file header for the full contract.
/// </summary>
public static class ExternalModuleDataEndpoints
{
    /// <summary>Deny code when the requested entity has no registered external module (fail-closed).</summary>
    public const string DenyModuleNotRegistered = "sdap.external.module.not_registered";

    /// <summary>Deny code when a record-scoped read targets a record outside the module's Tier-2 set.</summary>
    public const string DenyRecordNotAccessible = "sdap.external.module.record_not_in_accessible_set";

    /// <summary>
    /// Registers the module read-data endpoints on the shared external collaboration group. The group's
    /// dual-scheme policy + CallerPrincipalAuthorizationFilter are inherited; this method adds routes only
    /// (no handler or filter changes elsewhere).
    /// </summary>
    public static RouteGroupBuilder MapExternalModuleDataEndpoints(this RouteGroupBuilder externalGroup)
    {
        // Full path prefix: /api/v1/external/api/dataverse — the /api/dataverse/* suffix is what
        // BffDataverseClient appends to its bffBaseUrl, so a widget with bffBaseUrl={host}/api/v1/external
        // consumes this group unchanged (no client fork).
        var data = externalGroup.MapGroup("/api/dataverse").WithTags("External Module Data");

        // POST /fetch — Tier-2-scoped FetchXML read (rows). App-only; result filtered to the module's
        // accessible record set. Non-participant → empty result.
        data.MapPost("/fetch", ExecuteScopedFetchAsync)
            .WithName("ExternalModuleFetch")
            .WithSummary("Execute a module read (FetchXML), Tier-2-scoped to the caller's accessible records")
            .Produces<FetchResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /record/{entity}/{id} — single-record read, Tier-2-gated (record ∈ accessible set) BEFORE
        // any Dataverse read. App-only.
        data.MapGet("/record/{entityLogicalName}/{id:guid}", GetScopedRecordAsync)
            .WithName("ExternalModuleRecord")
            .WithSummary("Read one module record by id, denied unless it is in the caller's Tier-2 set")
            .Produces<IReadOnlyDictionary<string, object?>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /metadata/{entity} — projected entity metadata (schema; no record data). App-only passthrough.
        data.MapGet("/metadata/{entityLogicalName}", GetMetadataAsync)
            .WithName("ExternalModuleMetadata")
            .WithSummary("Projected entity metadata for a module DataGrid (schema only)")
            .Produces<EntityMetadataDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /savedquery/{id} — saved query payload (view definition; no record data). App-only passthrough.
        data.MapGet("/savedquery/{savedQueryId:guid}", GetSavedQueryAsync)
            .WithName("ExternalModuleSavedQuery")
            .WithSummary("Saved query payload for a module DataGrid (view definition only)")
            .Produces<SavedQueryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /savedqueries/{entity} — saved queries for an entity (view definitions). App-only passthrough.
        data.MapGet("/savedqueries/{entityLogicalName}", GetSavedQueriesAsync)
            .WithName("ExternalModuleSavedQueries")
            .WithSummary("Saved queries for a module entity (view definitions only)")
            .Produces<IReadOnlyList<SavedQuerySummaryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return externalGroup;
    }

    // =========================================================================
    // Handlers
    // =========================================================================

    private static async Task<IResult> ExecuteScopedFetchAsync(
        [FromBody] FetchRequestDto request,
        HttpContext httpContext,
        ExternalModuleRegistry registry,
        FetchService fetchService,
        IFetchXmlEntityExtractor entityExtractor,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var principal = GetCallerPrincipal(httpContext);
        if (principal is null) return MissingContextResult();

        if (request is null || string.IsNullOrWhiteSpace(request.EntityName) ||
            string.IsNullOrWhiteSpace(request.FetchXml))
        {
            return ProblemDetailsHelper.ValidationError("EntityName and FetchXml are required.");
        }

        var module = registry.FindByEntity(request.EntityName);
        if (module is null)
        {
            // No module owns this entity → the caller may not read it via this seam. Fail-closed.
            logger.LogWarning(
                "[EXT-MODULE] Fetch denied — no external module registered for entity {Entity}.",
                request.EntityName);
            return ProblemDetailsHelper.Forbidden(DenyModuleNotRegistered);
        }

        // SECURITY (broker over-read defense): the app-only ServiceClient runs with full app privilege,
        // so the caller-supplied FetchXML MUST reference ONLY the module's own entity. Any other entity —
        // including a <link-entity> join — is rejected, because Tier-2 row-scoping filters the PRIMARY
        // rows by id but cannot vet joined columns (e.g. a join to systemuser/contact would ride internal
        // data out on an accessible project row). A per-module link-entity allow-list is the documented
        // future extension seam; R1 is single-entity reads (lookup labels come from formatted values).
        IReadOnlySet<string> referenced;
        try
        {
            referenced = entityExtractor.ExtractEntities(request.FetchXml);
        }
        catch (FetchXmlParseException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "FetchXML payload could not be parsed.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "DV_FETCHXML_MALFORMED" });
        }

        if (referenced.Count == 0 ||
            referenced.Any(e => !string.Equals(e, module.RecordEntity, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogWarning(
                "[EXT-MODULE] Fetch denied — FetchXML for module {Module} references entities [{Entities}]; " +
                "only '{ModuleEntity}' is permitted (no link-entities on the external read seam).",
                module.Name, string.Join(",", referenced), module.RecordEntity);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: $"The module read may reference only entity '{module.RecordEntity}'. " +
                        "Cross-entity joins (<link-entity>) are not permitted on this surface.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "DV_FETCHXML_ENTITY_MISMATCH" });
        }

        // Compute the caller's Tier-2 accessible sets for this module's scope dimensions ONCE (a pure
        // read of the already-resolved principal — task 028 polymorphic OR scoping). A child module has
        // one dimension per typed parent lookup (project/matter/work-assignment); a root/config module
        // has one. Keep only the non-empty dimensions. If EVERY dimension is empty the caller can see
        // nothing in this module: return 0 rows WITHOUT querying Dataverse (matches ScopeRows' fail-closed
        // contract, and avoids emitting an invalid empty `IN ()` condition).
        var scopeDimensions = module.EffectiveDimensions
            .Select(d => new Tier2ScopeFilterInjector.ScopeFilterDimension(d.Attribute, d.AccessibleIds(principal)))
            .Where(d => d.AccessibleIds.Count > 0)
            .ToList();
        if (scopeDimensions.Count == 0)
        {
            logger.LogInformation(
                "[EXT-MODULE] Fetch module={Module} entity={Entity}: caller has empty accessible set (all dimensions) — 0 rows (no query).",
                module.Name, module.RecordEntity);
            return Results.Ok(new FetchResponseDto(
                Array.Empty<IReadOnlyDictionary<string, object?>>(), MoreRecords: false, PagingCookie: null));
        }

        try
        {
            // Push the Tier-2 record scope INTO the FetchXML as a server-side <filter type='or'> across
            // the non-empty scope dimensions BEFORE execution, so Dataverse returns ONLY rows that roll
            // up to an accessible root. This replaces the prior "fetch one unfiltered page, then drop
            // non-matching rows in memory" approach, which silently returned 0 rows whenever the
            // accessible records fell outside the first page of a large/sparse table (e.g. sprk_document:
            // 49 project-linked of 828 total → page 1 was almost all null-project rows). See
            // notes/grid-widget-empty-diagnosis.md. ScopeRows below is retained as defense-in-depth.
            var scopedFetchXml = Tier2ScopeFilterInjector.Inject(request.FetchXml, scopeDimensions);
            var scopedRequest = request with { FetchXml = scopedFetchXml };

            // App-only execution (broker-only, no OBO), then Tier-2 scope the rows to the caller's set
            // (defense-in-depth — the server-side filter above is the primary control).
            var result = await fetchService.ExecuteAsync(scopedRequest, ct).ConfigureAwait(false);
            var scoped = module.ScopeRows(principal, result.Entities);

            logger.LogInformation(
                "[EXT-MODULE] Fetch module={Module} entity={Entity}: {Returned}/{Total} rows after Tier-2 scope (server-side filtered).",
                module.Name, module.RecordEntity, scoped.Count, result.Entities.Count);

            // Paging metadata intentionally NOT propagated: R1 BffDataverseClient sends pagingCookie
            // undefined and does not page this surface. With server-side scoping the returned page now
            // holds only accessible rows; per-module config `behavior.pageSize` is sized to cover a
            // realistic per-caller accessible set in one page. Documented in
            // notes/grid-widget-empty-diagnosis.md.
            return Results.Ok(new FetchResponseDto(scoped, MoreRecords: false, PagingCookie: null));
        }
        catch (FetchXmlParseException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "FetchXML payload could not be parsed.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "DV_FETCHXML_MALFORMED" });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EXT-MODULE] Fetch failed for module {Module}.", module.Name);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error",
                detail: "An unexpected error occurred executing the module read.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "DV_FETCH_INTERNAL_ERROR" });
        }
    }

    private static async Task<IResult> GetScopedRecordAsync(
        string entityLogicalName,
        Guid id,
        [FromQuery(Name = "$select")] string? select,
        HttpContext httpContext,
        ExternalModuleRegistry registry,
        RecordService recordService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var principal = GetCallerPrincipal(httpContext);
        if (principal is null) return MissingContextResult();

        if (string.IsNullOrWhiteSpace(entityLogicalName) || id == Guid.Empty)
        {
            return ProblemDetailsHelper.ValidationError("A valid entity logical name and record id are required.");
        }

        var module = registry.FindByEntity(entityLogicalName);
        if (module is null)
        {
            return ProblemDetailsHelper.Forbidden(DenyModuleNotRegistered);
        }

        // ── Tier-2 gate FIRST — deny anything outside the caller's accessible set before any read ──
        if (!module.IsRecordAccessible(principal, id))
        {
            logger.LogWarning(
                "[EXT-MODULE] DENY record read module={Module} {Entity} {RecordId}: not in Tier-2 set.",
                module.Name, entityLogicalName, id);
            return ProblemDetailsHelper.Forbidden(DenyRecordNotAccessible);
        }

        try
        {
            var selectFields = ParseSelect(select);
            var record = await recordService
                .GetRecordAsync(entityLogicalName, id, selectFields, ct)
                .ConfigureAwait(false);
            return Results.Ok(record);
        }
        catch (RecordNotFoundException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: ex.Message,
                extensions: new Dictionary<string, object?> { ["errorCode"] = "DV_RECORD_NOT_FOUND" });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EXT-MODULE] Record read failed for module {Module}.", module.Name);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error",
                detail: "An unexpected error occurred reading the record.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "DV_RECORD_INTERNAL_ERROR" });
        }
    }

    private static async Task<IResult> GetMetadataAsync(
        string entityLogicalName,
        HttpContext httpContext,
        ExternalModuleRegistry registry,
        MetadataService metadataService,
        CancellationToken ct)
    {
        if (GetCallerPrincipal(httpContext) is null) return MissingContextResult();
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return ProblemDetailsHelper.ValidationError("Entity logical name is required.");
        }

        // Fail-closed like fetch/record: schema is only readable for an entity that has a registered
        // module — an external caller cannot enumerate metadata for arbitrary Dataverse entities.
        if (registry.FindByEntity(entityLogicalName) is null)
        {
            return ProblemDetailsHelper.Forbidden(DenyModuleNotRegistered);
        }

        try
        {
            var dto = await metadataService.GetMetadataAsync(entityLogicalName, ct).ConfigureAwait(false);
            return Results.Ok(dto);
        }
        catch (InvalidOperationException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound, title: "Not Found",
                detail: $"Entity '{entityLogicalName}' was not found in Dataverse metadata.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "DV_METADATA_ENTITY_NOT_FOUND" });
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: ex.Message,
                extensions: new Dictionary<string, object?> { ["errorCode"] = "DV_METADATA_INVALID_ENTITY" });
        }
    }

    private static async Task<IResult> GetSavedQueryAsync(
        Guid savedQueryId,
        HttpContext httpContext,
        ExternalModuleRegistry registry,
        SavedQueryService savedQueryService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (GetCallerPrincipal(httpContext) is null) return MissingContextResult();

        try
        {
            var dto = await savedQueryService.GetSavedQueryAsync(savedQueryId, ct).ConfigureAwait(false);
            if (dto is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: "Saved query not found",
                    extensions: new Dictionary<string, object?> { ["errorCode"] = "DV_SAVEDQUERY_NOT_FOUND" });
            }

            // Fail-closed: a saved query is only readable when its target entity has a registered module —
            // an external caller cannot pull view definitions (FetchXML/LayoutXML) for arbitrary entities.
            if (string.IsNullOrWhiteSpace(dto.EntityName) || registry.FindByEntity(dto.EntityName) is null)
            {
                logger.LogWarning(
                    "[EXT-MODULE] Saved query {SavedQueryId} denied — entity '{Entity}' has no registered module.",
                    savedQueryId, dto.EntityName);
                return ProblemDetailsHelper.Forbidden(DenyModuleNotRegistered);
            }

            return Results.Ok(dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EXT-MODULE] Failed to load savedquery {SavedQueryId}.", savedQueryId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error",
                detail: "Failed to load saved query",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "DV_INTERNAL_ERROR" });
        }
    }

    private static async Task<IResult> GetSavedQueriesAsync(
        string entityLogicalName,
        HttpContext httpContext,
        ExternalModuleRegistry registry,
        SavedQueryService savedQueryService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (GetCallerPrincipal(httpContext) is null) return MissingContextResult();
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return ProblemDetailsHelper.ValidationError("Entity logical name is required.");
        }

        // Fail-closed: list saved queries only for an entity with a registered module.
        if (registry.FindByEntity(entityLogicalName) is null)
        {
            return ProblemDetailsHelper.Forbidden(DenyModuleNotRegistered);
        }

        try
        {
            var summaries = await savedQueryService
                .GetSavedQueriesForEntityAsync(entityLogicalName, ct)
                .ConfigureAwait(false);
            return Results.Ok(summaries);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EXT-MODULE] Failed to list savedqueries for {Entity}.", entityLogicalName);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error",
                detail: "Failed to list saved queries",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "DV_INTERNAL_ERROR" });
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    // Principal-agnostic caller (teams-app-r1 task 025 · ADR-028 A3): the group-level
    // CallerPrincipalAuthorizationFilter resolves EITHER plane to a CallerPrincipal on HttpContext.Items.
    private static CallerPrincipal? GetCallerPrincipal(HttpContext httpContext) =>
        httpContext.Items[CallerPrincipal.HttpContextItemsKey] as CallerPrincipal;

    private static IResult MissingContextResult() =>
        Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error",
            detail: "Authentication context not available — ensure AddCallerPrincipalAuthorizationFilter is applied");

    private static string[]? ParseSelect(string? select)
    {
        if (string.IsNullOrWhiteSpace(select))
        {
            return null;
        }

        var fields = select
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToArray();

        return fields.Length == 0 ? null : fields;
    }
}
