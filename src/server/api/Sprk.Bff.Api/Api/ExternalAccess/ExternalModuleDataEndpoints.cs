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
// No joins on the read seam (unified-access-control-r2 task 011 · spec FR-10 · finding A-17): the fetch
// guard admits a caller-submitted FetchXML only when it is a SINGLE-ENTITY read of the module's own
// entity with NO <link-entity> at any depth. Before FR-10 the guard tested only the SET OF ENTITY NAMES,
// which a SELF-join cannot perturb — so `<link-entity name='{module.RecordEntity}'>` passed, and because
// Tier-2 scoping filters PRIMARY rows only, its aliased columns carried OUT-OF-SCOPE rows of the same
// entity out to the client. Joins are now refused structurally, not scoped. See EvaluateFetchXmlGuard.
//
// Broker-only (ADR-028 A1/A2/A3 · NFR-02): all reads execute APP-ONLY via the existing Dataverse read
// services — no OBO / no caller-token exchange, no Graph pointer ever reaches the client, keyed on
// record id. ADR-008: authorization via the inherited group filter (no global middleware). ADR-019:
// ProblemDetails on every failure.

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

    /// <summary>Error code when the guard reaches an unmodelled verdict — fail-closed, never admitted.</summary>
    public const string ErrorFetchXmlGuardIndeterminate = "DV_FETCHXML_GUARD_INDETERMINATE";

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

        // SECURITY (broker over-read defense) — see EvaluateFetchXmlGuard for the full contract. The
        // app-only ServiceClient runs with full app privilege, so a caller-supplied FetchXML is admitted
        // ONLY when it is a single-entity read of the module's own entity with NO join of any kind.
        var guard = EvaluateFetchXmlGuard(request.FetchXml, module.RecordEntity, entityExtractor);
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
    }

    /// <summary>Guard outcome plus the entity names the FetchXML referenced (for logging only).</summary>
    internal readonly record struct FetchXmlGuardResult(
        FetchXmlGuardVerdict Verdict,
        IReadOnlySet<string> ReferencedEntities)
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
    /// Two independent refusal signals, both required, evaluated in this order:
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

        return ContainsLinkEntity(document)
            ? new FetchXmlGuardResult(FetchXmlGuardVerdict.LinkEntityNotPermitted, referenced)
            : new FetchXmlGuardResult(FetchXmlGuardVerdict.Allowed, referenced);
    }

    /// <summary>True when a <c>&lt;link-entity&gt;</c> element occurs at ANY depth (namespace- and case-agnostic).</summary>
    private static bool ContainsLinkEntity(XDocument document) =>
        document.Descendants().Any(element =>
            string.Equals(element.Name.LocalName, LinkEntityElementName, StringComparison.OrdinalIgnoreCase));

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
