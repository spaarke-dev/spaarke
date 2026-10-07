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
// reads (metadata, savedquery, savedqueries) return no record data and are app-only.
//
// View scope (unified-access-control-r2 task 157 · finding F1): the savedquery / savedqueries routes return ONLY the
// views an external module's grid is registered to use (ExternalModuleDescriptor.SavedQueryIds, the same module
// registry the column allow-lists live on), and 404 for every other view, the entity's internal MDA views included.
// An unregistered id, a registered id whose view belongs to another entity, an unknown id and an entity with no module
// all get the same 404. Owner decisions C6 / C9: a contact gets only what it is granted.
//
// No joins on the read seam (unified-access-control-r2 task 011 · spec FR-10 · finding A-17): the fetch
// guard admits a caller-submitted FetchXML only when it is a SINGLE-ENTITY read of the module's own
// entity with NO <link-entity> at any depth. Before FR-10 the guard tested only the SET OF ENTITY NAMES,
// which a SELF-join cannot perturb — so `<link-entity name='{module.RecordEntity}'>` passed, and because
// Tier-2 scoping filters PRIMARY rows only, its aliased columns carried OUT-OF-SCOPE rows of the same
// entity out to the client. Joins are now refused structurally, not scoped. See EvaluateFetchXmlGuard.
//
// Column scope (unified-access-control-r2 task 134 · defect C6): row scope alone was not enough. The
// caller authors the FetchXML / $select, and the reads execute app-only — which BYPASSES field-level
// security — so before task 134 a granted contact could project any column of an in-scope row, including
// the SPE pointers sprk_graphdriveid / sprk_graphitemid / sprk_filepath. Every module now declares a
// column allow-list (ExternalModuleDescriptor.ReadableColumns) and this file enforces it TWICE:
//   1. BEFORE execution — EvaluateFetchXmlGuard's third signal refuses all-attributes, any alias or
//      aggregate, and any non-allow-listed column in an attribute / condition / order position; the
//      /record handler refuses a $select naming one. Refused, never silently rewritten.
//   2. AFTER execution and after Tier-2 ScopeRows — StripToReadableColumns drops every key (and
//      @formattedValues entry) not on the list, as defence in depth against anything step 1 missed.
// The stripping lives HERE, not in FetchService / RecordService, because those are shared with the
// internal /api/dataverse surface.
//
// Broker-only (ADR-028 A1/A2/A3 · NFR-02): all reads execute APP-ONLY via the existing Dataverse read
// services — no OBO / no caller-token exchange, keyed on record id. No Graph pointer reaches the client
// BECAUSE of the column scope above: no module allow-list may contain a pointer column
// (ExternalModuleRegistry.PointerColumns, enforced at startup), so the guard refuses a request for one
// and the strip removes one Dataverse returns unasked. ADR-008: authorization via the inherited group
// filter (no global middleware). ADR-019: ProblemDetails on every failure.

using System.Xml;
using System.Xml.Linq;
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

    /// <summary>Error code when the caller-submitted FetchXML cannot be parsed (fail-closed).</summary>
    public const string ErrorFetchXmlMalformed = "DV_FETCHXML_MALFORMED";

    /// <summary>Error code when the FetchXML names an entity other than the module's own.</summary>
    public const string ErrorFetchXmlEntityMismatch = "DV_FETCHXML_ENTITY_MISMATCH";

    /// <summary>
    /// Error code when the FetchXML contains a <c>&lt;link-entity&gt;</c> join. Distinct from
    /// <see cref="ErrorFetchXmlEntityMismatch"/> because a SELF-join names no foreign entity yet is
    /// equally exfiltrating (finding A-17 / spec FR-10).
    /// </summary>
    public const string ErrorFetchXmlLinkEntityNotPermitted = "DV_FETCHXML_LINK_ENTITY_NOT_PERMITTED";

    /// <summary>
    /// Error code when the FetchXML reads, filters on or sorts by a column outside the module's
    /// <see cref="ExternalModuleDescriptor.ReadableColumns"/>, or uses <c>all-attributes</c>, an alias or an
    /// aggregate (task 134 / defect C6). Distinct from the entity and join codes: a different refusal reason.
    /// </summary>
    public const string ErrorFetchXmlColumnNotPermitted = "DV_FETCHXML_COLUMN_NOT_PERMITTED";

    /// <summary>
    /// Error code when a <c>/record</c> <c>$select</c> names a column outside the module's allow-list, or the
    /// module declares no allow-list (task 134). Same <c>*_COLUMN_NOT_PERMITTED</c> family as
    /// <see cref="ErrorFetchXmlColumnNotPermitted"/>.
    /// </summary>
    public const string ErrorRecordColumnNotPermitted = "DV_RECORD_COLUMN_NOT_PERMITTED";

    /// <summary>Error code when the guard reaches an unmodelled verdict — fail-closed, never admitted.</summary>
    public const string ErrorFetchXmlGuardIndeterminate = "DV_FETCHXML_GUARD_INDETERMINATE";

    /// <summary>
    /// The synthetic key FetchService adds to every projected row. Kept by the strip: ScopeRows needs it,
    /// and it carries the entity name the caller already asked for (no column data).
    /// </summary>
    internal const string LogicalNameKey = "@logicalName";

    /// <summary>The synthetic key under which FetchService nests each row's formatted (display) values.</summary>
    internal const string FormattedValuesKey = "@formattedValues";

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

        // GET /savedquery/{id} — saved query payload (view definition; no record data). App-only. Only a view a module
        // grid is registered to use (task 157, F1); 404 otherwise.
        data.MapGet("/savedquery/{savedQueryId:guid}", GetSavedQueryAsync)
            .WithName("ExternalModuleSavedQuery")
            .WithSummary("Saved query payload for a module DataGrid (a registered view only)")
            .Produces<SavedQueryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /savedqueries/{entity} — the entity's views that its module grid is registered to use (task 157, F1).
        // App-only. 404 when the entity has no module or its module registers no view.
        data.MapGet("/savedqueries/{entityLogicalName}", GetSavedQueriesAsync)
            .WithName("ExternalModuleSavedQueries")
            .WithSummary("Registered saved queries for a module entity (view definitions only)")
            .Produces<IReadOnlyList<SavedQuerySummaryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return externalGroup;
    }

    // =========================================================================
    // Handlers
    // =========================================================================

    private static Task<IResult> ExecuteScopedFetchAsync(
        [FromBody] FetchRequestDto request,
        HttpContext httpContext,
        ExternalModuleRegistry registry,
        FetchService fetchService,
        IFetchXmlEntityExtractor entityExtractor,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var principal = GetCallerPrincipal(httpContext);
        if (principal is null) return Task.FromResult(MissingContextResult());

        return ExecuteScopedFetchCoreAsync(
            request, principal, registry, entityExtractor, fetchService.ExecuteAsync, logger, ct);
    }

    /// <summary>
    /// The module fetch pipeline: module lookup → FetchXML guard (entity, join, COLUMN) → Tier-2 scope
    /// injection → app-only execution → <c>ScopeRows</c> → column strip. Separated from the route handler
    /// only so the pipeline can be driven with a test double for <paramref name="executeFetch"/> (the
    /// shared <see cref="FetchService"/> needs a live ServiceClient). The handler passes
    /// <c>FetchService.ExecuteAsync</c> unchanged; there is no other caller.
    /// </summary>
    internal static async Task<IResult> ExecuteScopedFetchCoreAsync(
        FetchRequestDto? request,
        CallerPrincipal principal,
        ExternalModuleRegistry registry,
        IFetchXmlEntityExtractor entityExtractor,
        Func<FetchRequestDto, CancellationToken, Task<FetchResponseDto>> executeFetch,
        ILogger logger,
        CancellationToken ct)
    {
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

        // SECURITY (broker over-read defense) — see EvaluateFetchXmlGuard for the full contract. The
        // app-only ServiceClient runs with full app privilege, so a caller-supplied FetchXML is admitted
        // ONLY when it is a single-entity read of the module's own entity with NO join of any kind that
        // reads, filters on and sorts by ONLY the module's allow-listed columns (task 134). It runs BEFORE
        // scoping, so every caller — including one with an empty accessible set — gets the same refusal.
        var guard = EvaluateFetchXmlGuard(
            request.FetchXml, module.RecordEntity, module.ReadableColumns, entityExtractor);
        if (!guard.IsAllowed)
        {
            return RejectFetchXml(guard, module, logger);
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
            // (defense-in-depth — the server-side filter above is the primary control). The column strip
            // runs AFTER ScopeRows, which needs the scope attributes (and @logicalName) still present.
            var result = await executeFetch(scopedRequest, ct).ConfigureAwait(false);
            var scoped = module.ScopeRows(principal, result.Entities);
            var projected = StripRowsToReadableColumns(scoped, module, logger);

            logger.LogInformation(
                "[EXT-MODULE] Fetch module={Module} entity={Entity}: {Returned}/{Total} rows after Tier-2 scope (server-side filtered).",
                module.Name, module.RecordEntity, projected.Count, result.Entities.Count);

            // Paging metadata intentionally NOT propagated: R1 BffDataverseClient sends pagingCookie
            // undefined and does not page this surface. With server-side scoping the returned page now
            // holds only accessible rows; per-module config `behavior.pageSize` is sized to cover a
            // realistic per-caller accessible set in one page. Documented in
            // notes/grid-widget-empty-diagnosis.md.
            return Results.Ok(new FetchResponseDto(projected, MoreRecords: false, PagingCookie: null));
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

    private static Task<IResult> GetScopedRecordAsync(
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
        if (principal is null) return Task.FromResult(MissingContextResult());

        return GetScopedRecordCoreAsync(
            entityLogicalName, id, select, principal, registry, recordService.GetRecordAsync, logger, ct);
    }

    /// <summary>
    /// The module single-record pipeline: module lookup → <c>$select</c> column check → Tier-2 record gate
    /// → app-only read → column strip. Separated from the route handler only so it can be driven with a
    /// test double for <paramref name="readRecord"/> (the shared <see cref="RecordService"/> needs a live
    /// ServiceClient). The handler passes <c>RecordService.GetRecordAsync</c> unchanged.
    /// </summary>
    internal static async Task<IResult> GetScopedRecordCoreAsync(
        string entityLogicalName,
        Guid id,
        string? select,
        CallerPrincipal principal,
        ExternalModuleRegistry registry,
        Func<string, Guid, string[]?, CancellationToken, Task<IReadOnlyDictionary<string, object?>>> readRecord,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName) || id == Guid.Empty)
        {
            return ProblemDetailsHelper.ValidationError("A valid entity logical name and record id are required.");
        }

        var module = registry.FindByEntity(entityLogicalName);
        if (module is null)
        {
            return ProblemDetailsHelper.Forbidden(DenyModuleNotRegistered);
        }

        // ── Column scope (task 134) — validated before the Tier-2 gate, mirroring /fetch's guard-before-
        // scoping order: a pure input check, identical for every caller, that reads nothing. REFUSED, never
        // silently narrowed: a dropped column would be indistinguishable from a null value to the caller.
        var selectFields = ParseSelect(select);
        var readable = module.ReadableColumns;
        var refused = readable is null || readable.Count == 0
            ? selectFields ?? new[] { "(default projection)" }
            : selectFields?.Where(f => !readable.Contains(f)).ToArray() ?? Array.Empty<string>();
        if (refused.Length > 0)
        {
            logger.LogWarning(
                "[EXT-MODULE] Record read denied for module {Module} ({Entity}) — $select names column(s) " +
                "outside the module allow-list: [{Columns}].",
                module.Name, module.RecordEntity, string.Join(",", refused));
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: $"Column(s) [{string.Join(",", refused)}] are not readable on '{module.RecordEntity}' " +
                        "through this surface.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorRecordColumnNotPermitted });
        }

        // ── Tier-2 gate — deny anything outside the caller's accessible set before any read ──
        if (!module.IsRecordAccessible(principal, id))
        {
            logger.LogWarning(
                "[EXT-MODULE] DENY record read module={Module} {Entity} {RecordId}: not in Tier-2 set.",
                module.Name, entityLogicalName, id);
            return ProblemDetailsHelper.Forbidden(DenyRecordNotAccessible);
        }

        try
        {
            // No $select ⇒ RecordService projects primary id + primary name, both on every module's list
            // (Register refuses a list missing the primary id or the declared PrimaryNameAttribute;
            // ExternalModuleColumnAllowListTests pins each declaration to live metadata). The strip is
            // defence in depth either way.
            var record = await readRecord(entityLogicalName, id, selectFields, ct).ConfigureAwait(false);
            return Results.Ok(StripToReadableColumns(record, module.ReadableColumns, keepLogicalName: false, out _));
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

    private static Task<IResult> GetSavedQueryAsync(
        Guid savedQueryId,
        HttpContext httpContext,
        ExternalModuleRegistry registry,
        SavedQueryService savedQueryService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (GetCallerPrincipal(httpContext) is null) return Task.FromResult(MissingContextResult());

        return GetSavedQueryCoreAsync(savedQueryId, registry, savedQueryService.GetSavedQueryAsync, logger, ct);
    }

    /// <summary>Error code for every refused or absent view on the external savedquery routes (one answer for all).</summary>
    internal const string ErrorSavedQueryNotFound = "DV_SAVEDQUERY_NOT_FOUND";

    /// <summary>
    /// The by-id view read (task 157, F1): only a view an external module grid is registered to use
    /// (<see cref="ExternalModuleDescriptor.SavedQueryIds"/>), checked BEFORE any Dataverse read, and only when the loaded
    /// view belongs to that module's entity. Every other outcome is the same 404. Separated from the route handler only
    /// so it can be driven with a test double for <paramref name="loadSavedQuery"/> (the shared
    /// <see cref="SavedQueryService"/> needs a live ServiceClient); the handler passes
    /// <c>SavedQueryService.GetSavedQueryAsync</c> unchanged.
    /// </summary>
    internal static async Task<IResult> GetSavedQueryCoreAsync(
        Guid savedQueryId,
        ExternalModuleRegistry registry,
        Func<Guid, CancellationToken, Task<SavedQueryDto?>> loadSavedQuery,
        ILogger logger,
        CancellationToken ct)
    {
        // A view no module grid is registered to use is not available here — the entity's internal MDA views
        // included. Refused before any read, with the same 404 as an absent id (an unknown and a denied id look alike).
        var module = registry.FindBySavedQueryId(savedQueryId);
        if (module is null)
        {
            logger.LogInformation(
                "[EXT-MODULE] Saved query {SavedQueryId} refused — no external module grid is registered to use it.",
                savedQueryId);
            return SavedQueryNotFound();
        }

        try
        {
            var dto = await loadSavedQuery(savedQueryId, ct).ConfigureAwait(false);
            if (dto is null)
            {
                return SavedQueryNotFound();
            }

            // Defence in depth against a mis-registered id: the view must target the registering module's own entity.
            if (!string.Equals(dto.EntityName, module.RecordEntity, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "[EXT-MODULE] Saved query {SavedQueryId} refused — registered by module {Module} ({ModuleEntity}) but " +
                    "it targets '{Entity}'. Fix the module's SavedQueryIds.",
                    savedQueryId, module.Name, module.RecordEntity, dto.EntityName);
                return SavedQueryNotFound();
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

    private static Task<IResult> GetSavedQueriesAsync(
        string entityLogicalName,
        HttpContext httpContext,
        ExternalModuleRegistry registry,
        SavedQueryService savedQueryService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (GetCallerPrincipal(httpContext) is null) return Task.FromResult(MissingContextResult());

        return GetSavedQueriesCoreAsync(
            entityLogicalName, registry, savedQueryService.GetSavedQueriesForEntityAsync, logger, ct);
    }

    /// <summary>
    /// The per-entity view list (task 157, F1): ONLY the entity's views that its module grid is registered to use. An
    /// entity with no module, or whose module registers no view (every module today: their grids are inline), gets a
    /// 404 with no Dataverse read. Separated from the route handler for the same reason as
    /// <see cref="GetSavedQueryCoreAsync"/>; the handler passes <c>SavedQueryService.GetSavedQueriesForEntityAsync</c>.
    /// </summary>
    internal static async Task<IResult> GetSavedQueriesCoreAsync(
        string entityLogicalName,
        ExternalModuleRegistry registry,
        Func<string, CancellationToken, Task<IReadOnlyList<SavedQuerySummaryDto>>> listSavedQueries,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return ProblemDetailsHelper.ValidationError("Entity logical name is required.");
        }

        var module = registry.FindByEntity(entityLogicalName);
        if (module is null || module.SavedQueryIds.Count == 0)
        {
            logger.LogInformation(
                "[EXT-MODULE] Saved-query list for '{Entity}' refused — no external module grid is registered to use a " +
                "view of it.", entityLogicalName);
            return SavedQueryNotFound();
        }

        try
        {
            var summaries = await listSavedQueries(entityLogicalName, ct).ConfigureAwait(false);
            IReadOnlyList<SavedQuerySummaryDto> registered = summaries
                .Where(summary => module.SavedQueryIds.Contains(summary.Id))
                .ToList();
            return Results.Ok(registered);
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

    /// <summary>The one 404 every refused, unregistered or absent view gets on the external savedquery routes.</summary>
    private static IResult SavedQueryNotFound() =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: "Saved query not found",
            extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorSavedQueryNotFound });

    // =========================================================================
    // FetchXML guard (spec FR-10 · finding A-17)
    // =========================================================================

    // Internal (not public) because IFetchXmlEntityExtractor is internal — CS0051 otherwise. The BFF
    // declares InternalsVisibleTo("Sprk.Bff.Api.Tests"), so tests call the real guard unchanged.
    /// <summary>Why the FetchXML guard admitted or refused a caller-submitted fetch.</summary>
    internal enum FetchXmlGuardVerdict
    {
        /// <summary>Single-entity read of the module's own entity, no joins. The ONLY admitting value.</summary>
        Allowed = 0,

        /// <summary>Unparseable / structurally invalid FetchXML — refused (ADR-003 fail-closed).</summary>
        Malformed = 1,

        /// <summary>References an entity other than the module's own — refused.</summary>
        EntityMismatch = 2,

        /// <summary>Contains a <c>&lt;link-entity&gt;</c> join (self-join included) — refused per FR-10.</summary>
        LinkEntityNotPermitted = 3,

        /// <summary>
        /// Reads, filters on or sorts by a column outside the module allow-list, or uses
        /// <c>all-attributes</c>, an alias, an aggregate or an unrecognised element — refused (task 134 / C6).
        /// </summary>
        ColumnNotPermitted = 4,
    }

    /// <summary>
    /// Guard outcome plus the entity names the FetchXML referenced (for logging only), and — for
    /// <see cref="FetchXmlGuardVerdict.ColumnNotPermitted"/> — which position and name tripped the column
    /// signal (echoes the caller's own input; carries no record data).
    /// </summary>
    internal readonly record struct FetchXmlGuardResult(
        FetchXmlGuardVerdict Verdict,
        IReadOnlySet<string> ReferencedEntities,
        string? ColumnViolation = null)
    {
        /// <summary>True ONLY for <see cref="FetchXmlGuardVerdict.Allowed"/> — every other verdict refuses.</summary>
        public bool IsAllowed => Verdict == FetchXmlGuardVerdict.Allowed;
    }

    private static readonly IReadOnlySet<string> NoReferencedEntities =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The FetchXML join element. Matched by LOCAL NAME, case-insensitively (see remarks).</summary>
    private const string LinkEntityElementName = "link-entity";

    /// <summary>
    /// The single authority deciding whether a caller-submitted FetchXML may execute on the external
    /// module read seam. Public (not a private lambda) so tests exercise the REAL decision rather than a
    /// transcription of it — a transcribed predicate cannot detect a change in what production does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three independent refusal signals, all required, evaluated in this order (the third added by
    /// task 134 — see <see cref="FindColumnViolation"/>):
    /// </para>
    /// <list type="number">
    ///   <item><b>Entity identity</b> (pre-existing): every entity named by the FetchXML must equal the
    ///   module's own <c>RecordEntity</c>. Unchanged so the cross-entity protection cannot regress, and
    ///   ordered FIRST so a cross-entity join keeps reporting
    ///   <see cref="ErrorFetchXmlEntityMismatch"/> exactly as before.</item>
    ///   <item><b>Structural join detection</b> (FR-10 / A-17, NEW): refuse when ANY
    ///   <c>&lt;link-entity&gt;</c> element is present at any depth. This is the signal the entity-name
    ///   set structurally cannot carry — a SELF-join contributes only the module's own name, so the
    ///   referenced set of an exfiltrating self-join is byte-identical to that of a benign single-entity
    ///   read. Tier-2 scoping filters PRIMARY rows only, so aliased columns pulled through a self-join
    ///   are extra attributes ON an in-scope row and are never scope-checked, and
    ///   <c>FetchService.ProjectEntity</c> serializes <c>AliasedValue</c> straight to the client.</item>
    ///   <item><b>Column scope</b> (task 134 / defect C6, NEW): every column the fetch READS
    ///   (<c>attribute name=</c>), FILTERS ON (<c>condition attribute=</c> / <c>valueof=</c> — a filter on
    ///   a hidden column is a value oracle) or SORTS BY (<c>order attribute=</c>) must be on
    ///   <paramref name="readableColumns"/>; <c>all-attributes</c>, any <c>alias</c> (it would carry a
    ///   column out under another key), any aggregate / group-by, and any element this guard does not
    ///   model are refused. Ordered LAST so a fetch that is also cross-entity or a join keeps reporting
    ///   the more fundamental refusal, and so this signal only ever inspects a join-free document.</item>
    /// </list>
    /// <para>
    /// <b>Posture: reject, do not scope</b> (FR-10 wording). No per-module join allow-list is offered —
    /// no consumer needs one today (root CLAUDE.md §11: new surface requires a concrete cost-of-doing-
    /// nothing), and a scoped join is materially harder to get right than a refusal. Adding one later is
    /// an additive change to this one method.
    /// </para>
    /// <para>
    /// <b>Join detection is deliberately broader than the extractor's.</b> It matches the element's
    /// LOCAL name, case-insensitively, ignoring XML namespace — so a hypothetical
    /// <c>&lt;Link-Entity&gt;</c> or namespace-qualified variant cannot slip past a guard that the
    /// extractor's exact-name <c>Descendants("link-entity")</c> lookup would miss. Strictly more
    /// conservative: any fetch Dataverse would itself reject is simply refused earlier, and no
    /// single-entity read is affected. Comments and text nodes are not elements, so a literal
    /// "link-entity" inside a comment or value is not a false positive.
    /// </para>
    /// <para>
    /// ADR-003 fail-closed: every parse failure, empty referenced set, and unmodelled state refuses.
    /// There is no permissive fallback anywhere in this method.
    /// </para>
    /// </remarks>
    internal static FetchXmlGuardResult EvaluateFetchXmlGuard(
        string? fetchXml,
        string moduleRecordEntity,
        IReadOnlySet<string>? readableColumns,
        IFetchXmlEntityExtractor entityExtractor)
    {
        ArgumentNullException.ThrowIfNull(entityExtractor);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleRecordEntity);

        if (string.IsNullOrWhiteSpace(fetchXml))
        {
            return new FetchXmlGuardResult(FetchXmlGuardVerdict.Malformed, NoReferencedEntities);
        }

        // ── (1) Entity identity — UNCHANGED predicate, so cross-entity rejection cannot regress ──
        IReadOnlySet<string> referenced;
        try
        {
            referenced = entityExtractor.ExtractEntities(fetchXml);
        }
        catch (FetchXmlParseException)
        {
            return new FetchXmlGuardResult(FetchXmlGuardVerdict.Malformed, NoReferencedEntities);
        }

        if (referenced.Count == 0 ||
            referenced.Any(e => !string.Equals(e, moduleRecordEntity, StringComparison.OrdinalIgnoreCase)))
        {
            return new FetchXmlGuardResult(FetchXmlGuardVerdict.EntityMismatch, referenced);
        }

        // ── (2) Structural join detection — the A-17 blind spot the name set cannot express ──
        XDocument document;
        try
        {
            // XDocument.Parse prohibits DTD processing by default on .NET, so no external-entity vector.
            document = XDocument.Parse(fetchXml);
        }
        catch (XmlException)
        {
            // Fail closed: the extractor parsed it but we cannot, so we cannot prove the fetch join-free.
            return new FetchXmlGuardResult(FetchXmlGuardVerdict.Malformed, referenced);
        }

        if (ContainsLinkEntity(document))
        {
            return new FetchXmlGuardResult(FetchXmlGuardVerdict.LinkEntityNotPermitted, referenced);
        }

        // ── (3) Column scope — task 134 / C6: rows are scoped above, columns here ──
        var columnViolation = FindColumnViolation(document, readableColumns);
        return columnViolation is null
            ? new FetchXmlGuardResult(FetchXmlGuardVerdict.Allowed, referenced)
            : new FetchXmlGuardResult(FetchXmlGuardVerdict.ColumnNotPermitted, referenced, columnViolation);
    }

    /// <summary>True when a <c>&lt;link-entity&gt;</c> element occurs at ANY depth (namespace- and case-agnostic).</summary>
    private static bool ContainsLinkEntity(XDocument document) =>
        document.Descendants().Any(element =>
            string.Equals(element.Name.LocalName, LinkEntityElementName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <c>&lt;attribute&gt;</c> XML attributes that make it an aggregate / group-by projection. Dataverse
    /// returns those under an alias key, never the column's own name, so they are refused outright.
    /// </summary>
    private static readonly string[] AggregateMarkers =
        { "aggregate", "groupby", "dategrouping", "rowaggregate", "distinct" };

    /// <summary>
    /// The column signal of <see cref="EvaluateFetchXmlGuard"/>: returns a description of the FIRST column
    /// violation in the (already join-free) document, or <c>null</c> when every element is modelled and
    /// every column it names is on <paramref name="readableColumns"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Elements are matched by LOCAL name, case-insensitively, for the same reason join detection is: a
    /// casing or namespace variant must not slip past. The walk is an ALLOW-list of element kinds —
    /// <c>fetch</c>, <c>entity</c>, <c>attribute</c>, <c>filter</c>, <c>condition</c>, <c>value</c>,
    /// <c>order</c> — so an element this method does not understand (including <c>all-attributes</c>) is
    /// refused rather than assumed harmless (ADR-003). XML attribute names (<c>name</c>, <c>attribute</c>,
    /// <c>alias</c>, …) are matched exactly, as Dataverse does; a misspelt one simply leaves the column
    /// unresolved, which is refused.
    /// </para>
    /// <para>
    /// A missing allow-list (<c>null</c> or empty) refuses every fetch: registration forbids it, and this
    /// method must not be the place a missing list turns into "all columns".
    /// </para>
    /// </remarks>
    private static string? FindColumnViolation(XDocument document, IReadOnlySet<string>? readableColumns)
    {
        if (readableColumns is null || readableColumns.Count == 0)
        {
            return "module declares no readable columns";
        }

        foreach (var element in document.Descendants())
        {
            var kind = element.Name.LocalName.ToLowerInvariant();
            switch (kind)
            {
                case "fetch":
                    if (string.Equals((string?)element.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase))
                    {
                        return "aggregate fetch (<fetch aggregate='true'>)";
                    }
                    break;

                case "entity":
                case "filter":
                case "value":
                    break;

                case "attribute":
                {
                    var name = (string?)element.Attribute("name");
                    if (element.Attribute("alias") is not null)
                    {
                        return $"aliased attribute '{name}'";
                    }
                    var marker = AggregateMarkers.FirstOrDefault(m => element.Attribute(m) is not null);
                    if (marker is not null)
                    {
                        return $"aggregate attribute '{name}' ({marker}=)";
                    }
                    if (!IsReadable(name, readableColumns))
                    {
                        return $"attribute '{name}'";
                    }
                    break;
                }

                case "condition":
                {
                    if (element.Attribute("entityname") is not null)
                    {
                        // Names a link-entity alias — there are none on this surface, so it is not modelled.
                        return "condition with entityname=";
                    }
                    var name = (string?)element.Attribute("attribute");
                    if (!IsReadable(name, readableColumns))
                    {
                        return $"condition on '{name}'";
                    }
                    var valueOf = element.Attribute("valueof");
                    if (valueOf is not null && !IsReadable(valueOf.Value, readableColumns))
                    {
                        return $"condition valueof '{valueOf.Value}'";
                    }
                    break;
                }

                case "order":
                {
                    if (element.Attribute("alias") is not null || element.Attribute("entityname") is not null)
                    {
                        return "order by alias or entityname";
                    }
                    var name = (string?)element.Attribute("attribute");
                    if (!IsReadable(name, readableColumns))
                    {
                        return $"order by '{name}'";
                    }
                    break;
                }

                case "all-attributes":
                    return "all-attributes";

                default:
                    return $"unrecognised element <{element.Name.LocalName}>";
            }
        }

        return null;
    }

    /// <summary>A column is readable only if it is named (non-blank) and on the module's allow-list.</summary>
    private static bool IsReadable(string? column, IReadOnlySet<string> readableColumns) =>
        !string.IsNullOrWhiteSpace(column) && readableColumns.Contains(column);

    /// <summary>
    /// Maps a refusing <see cref="FetchXmlGuardResult"/> to its ProblemDetails response (ADR-019).
    /// Every arm refuses; the <c>default</c> arm exists so that adding a verdict without updating this
    /// switch fails CLOSED with a 500 rather than falling through to an admit.
    /// </summary>
    private static IResult RejectFetchXml(
        FetchXmlGuardResult guard,
        ExternalModuleDescriptor module,
        ILogger logger)
    {
        switch (guard.Verdict)
        {
            case FetchXmlGuardVerdict.Malformed:
                logger.LogWarning(
                    "[EXT-MODULE] Fetch denied for module {Module} — FetchXML could not be parsed.",
                    module.Name);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                    detail: "FetchXML payload could not be parsed.",
                    extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorFetchXmlMalformed });

            case FetchXmlGuardVerdict.EntityMismatch:
                logger.LogWarning(
                    "[EXT-MODULE] Fetch denied — FetchXML for module {Module} references entities [{Entities}]; " +
                    "only '{ModuleEntity}' is permitted.",
                    module.Name, string.Join(",", guard.ReferencedEntities), module.RecordEntity);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                    detail: $"The module read may reference only entity '{module.RecordEntity}'. " +
                            "Cross-entity joins (<link-entity>) are not permitted on this surface.",
                    extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorFetchXmlEntityMismatch });

            case FetchXmlGuardVerdict.LinkEntityNotPermitted:
                // A-17: the referenced-entity set alone would have ADMITTED this fetch.
                logger.LogWarning(
                    "[EXT-MODULE] Fetch denied — FetchXML for module {Module} ({ModuleEntity}) contains a " +
                    "<link-entity> join. Joins are not permitted on the external read seam: Tier-2 scoping " +
                    "filters primary rows only, so aliased join columns would carry out-of-scope data.",
                    module.Name, module.RecordEntity);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                    detail: "Joins (<link-entity>) are not permitted on this surface, including a self-join " +
                            $"to '{module.RecordEntity}'. Submit a single-entity read.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = ErrorFetchXmlLinkEntityNotPermitted,
                    });

            case FetchXmlGuardVerdict.ColumnNotPermitted:
                // Task 134 / C6: app-only execution bypasses field-level security, so the column scope is
                // enforced here or nowhere.
                logger.LogWarning(
                    "[EXT-MODULE] Fetch denied — FetchXML for module {Module} ({ModuleEntity}) is outside the " +
                    "module column allow-list: {Violation}.",
                    module.Name, module.RecordEntity, guard.ColumnViolation);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                    detail: $"The module read may read, filter on and sort by only the readable columns of " +
                            $"'{module.RecordEntity}'; all-attributes, aliases and aggregates are not permitted. " +
                            $"Refused: {guard.ColumnViolation}.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = ErrorFetchXmlColumnNotPermitted,
                    });

            default:
                // Unreachable by construction (callers check IsAllowed first). Fail CLOSED anyway — a
                // permissive default is the exact failure mode this project keeps re-encountering.
                logger.LogError(
                    "[EXT-MODULE] Fetch denied for module {Module} — unmodelled guard verdict {Verdict}. " +
                    "Refusing (fail-closed).",
                    module.Name, guard.Verdict);
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error",
                    detail: "The module read could not be authorized.",
                    extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorFetchXmlGuardIndeterminate });
        }
    }

    // =========================================================================
    // Column strip (task 134 · defence in depth)
    // =========================================================================

    /// <summary>
    /// Strips every scoped fetch row to the module's readable columns, logging (Debug) when anything was
    /// removed. Runs AFTER <c>ScopeRows</c>, which still needs the scope attributes and <c>@logicalName</c>.
    /// </summary>
    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> StripRowsToReadableColumns(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        ExternalModuleDescriptor module,
        ILogger logger)
    {
        var projected = new List<IReadOnlyDictionary<string, object?>>(rows.Count);
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            projected.Add(StripToReadableColumns(row, module.ReadableColumns, keepLogicalName: true, out var dropped));
            removed.UnionWith(dropped);
        }

        if (removed.Count > 0)
        {
            // Debug, not Warning: Dataverse legitimately returns some columns unasked (e.g. a currency
            // lookup next to a money field). The guard is the control; this is the backstop.
            logger.LogDebug(
                "[EXT-MODULE] Fetch module={Module}: stripped non-readable key(s) [{Keys}] from the result.",
                module.Name, string.Join(",", removed));
        }

        return projected;
    }

    /// <summary>
    /// Returns a copy of <paramref name="row"/> holding ONLY keys on <paramref name="readableColumns"/>,
    /// plus the synthetic <c>@logicalName</c> when <paramref name="keepLogicalName"/> is set. The nested
    /// <c>@formattedValues</c> map is filtered the same way (a display value is a column value too) and
    /// dropped when nothing readable remains; any other shape under that key is dropped outright. Aliased
    /// keys are never columns, so they are always removed. A missing allow-list strips everything
    /// (fail-closed).
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> StripToReadableColumns(
        IReadOnlyDictionary<string, object?> row,
        IReadOnlySet<string>? readableColumns,
        bool keepLogicalName,
        out IReadOnlyList<string> removedKeys)
    {
        ArgumentNullException.ThrowIfNull(row);
        var kept = new Dictionary<string, object?>(row.Count, StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();

        foreach (var (key, value) in row)
        {
            if (keepLogicalName && string.Equals(key, LogicalNameKey, StringComparison.Ordinal))
            {
                kept[key] = value;
            }
            else if (string.Equals(key, FormattedValuesKey, StringComparison.Ordinal))
            {
                if (value is IEnumerable<KeyValuePair<string, string>> formatted && readableColumns is not null)
                {
                    var keptFormatted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (formattedKey, formattedValue) in formatted)
                    {
                        if (readableColumns.Contains(formattedKey))
                        {
                            keptFormatted[formattedKey] = formattedValue;
                        }
                        else
                        {
                            removed.Add($"{FormattedValuesKey}.{formattedKey}");
                        }
                    }
                    if (keptFormatted.Count > 0)
                    {
                        kept[key] = keptFormatted;
                    }
                }
                else
                {
                    removed.Add(key);
                }
            }
            else if (readableColumns is not null && readableColumns.Contains(key))
            {
                kept[key] = value;
            }
            else
            {
                removed.Add(key);
            }
        }

        removedKeys = removed;
        return kept;
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
