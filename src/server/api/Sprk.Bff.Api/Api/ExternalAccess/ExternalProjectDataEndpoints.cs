using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// Maps all project data endpoints for authenticated external users.
///
/// Routes (all under /api/v1/external — RequireAuthorization + CallerPrincipalAuthorizationFilter):
///   GET  /projects                       — list user's accessible projects
///   GET  /projects/{id}                  — single project by ID
///   GET  /projects/{id}/documents        — documents for a project
///   POST /projects/{id}/documents        — upload a file + create its sprk_document row
///   GET  /projects/{id}/todos            — to-dos for a project (sprk_todo, regarding=project)
///   POST /projects/{id}/todos            — create a new to-do regarding the project
///   GET  /projects/{id}/events           — CALENDAR events for a project (sprk_event)
///   POST /projects/{id}/events           — create a calendar event on the project
///   GET  /projects/{id}/contacts         — contacts with access to the project
///   GET  /projects/{id}/organizations    — organizations linked to project contacts
///   PATCH /todos/{id}                    — update a to-do (scoped to the caller's projects)
///
/// READ routes verify the caller has a participation record for the requested project via
/// CallerPrincipal.HasProjectAccess(). Returns 403 if no access.
///
/// MUTATING routes additionally require the specific right from the evaluator's answer FOR THAT
/// RECORD (unified-access-control-r2 task 033 / FR-19): Create on the three POSTs, Write on the
/// PATCH. A View Only grant does not permit a write on any route. These gates existed before task
/// 033 but could not fire on the workforce plane, which blanket-stamped Collaborate over every
/// accessible project; that stamp is deleted.
///
/// PATCH /todos/{id} takes a to-do id rather than a project id, so it resolves the to-do's root
/// first (ExternalDataService.GetTodoRootAsync — project, matter OR work assignment) and gates on
/// that root's rights — see FR-08 / finding A-7. All three root types are gated identically; the
/// former "matter/WA membership implies write" asymmetry is gone (register A-8).
///
/// smart-todo-decoupling-r3 (FR-29): Routes formerly exposed an event-based to-do model
/// (GET/POST /events, PATCH /events/{id}). Replaced with sprk_todo routes here. See
/// projects/smart-todo-decoupling-r3/notes/external-access-contract-change.md for the
/// breaking-contract migration guide consumed by the external-spa (task 008).
///
/// unified-access-control-r2 (2026-09-02): GET/POST /projects/{id}/events are BACK — but for
/// CALENDAR events only, and this is NOT a reversal of FR-29. FR-29 retired the event-AS-todo
/// model (sprk_event + sprk_todoflag standing in for a to-do); removing the routes also removed
/// the only way an external user could see genuine calendar events, which the external SPA's
/// EventsCalendar component still renders. The restored routes serve real sprk_event records and
/// deliberately do NOT select, accept, or return sprk_todoflag. To-dos remain exclusively on
/// sprk_todo via /todos. If a future change makes these two surfaces overlap again, that is the
/// regression FR-29 existed to prevent — keep them disjoint.
/// PATCH /events/{id} was NOT restored: the only client caller (web-api-client.updateEvent) has
/// zero call sites, so there is no consumer to justify the write surface (CLAUDE.md §11).
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Authorization applied via route group + CallerPrincipalAuthorizationFilter.
/// ADR-024: To-do regarding context applied via the four resolver fields + sprk_regardingproject lookup.
/// </summary>
public static class ExternalProjectDataEndpoints
{
    public static void MapExternalProjectDataEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/v1/external/projects — list all projects the user has access to
        group.MapGet("/projects", GetProjects)
            .WithName("GetExternalProjects")
            .WithSummary("List all Secure Projects the authenticated user can access")
            .Produces<ExternalCollectionResponse<ExternalProjectDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET /api/v1/external/projects/{id} — single project
        group.MapGet("/projects/{id:guid}", GetProjectById)
            .WithName("GetExternalProjectById")
            .WithSummary("Get a single Secure Project by ID")
            .Produces<ExternalProjectDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /api/v1/external/projects/{id}/documents
        group.MapGet("/projects/{id:guid}/documents", GetDocuments)
            .WithName("GetExternalProjectDocuments")
            .WithSummary("Get documents for a Secure Project")
            .Produces<ExternalCollectionResponse<ExternalDocumentDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET /api/v1/external/projects/{id}/documents/{documentId}/content — download document bytes.
        // Authz-before-stream (broker-only, app-only): HasProjectAccess + document->project scoping are
        // enforced BEFORE any SPE pointer resolution or Graph read (ADR-028 A1, NFR-03).
        group.MapGet("/projects/{id:guid}/documents/{documentId:guid}/content", DownloadDocumentContent)
            .WithName("DownloadExternalProjectDocument")
            .WithSummary("Download a Secure Project document's content (authz-before-stream, app-only)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // GET /api/v1/external/projects/{id}/todos
        group.MapGet("/projects/{id:guid}/todos", GetTodos)
            .WithName("GetExternalProjectTodos")
            .WithSummary("Get to-dos for a Secure Project (sprk_todo records regarding the project)")
            .Produces<ExternalCollectionResponse<ExternalTodoDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // POST /api/v1/external/projects/{id}/todos — create a new to-do regarding the project
        group.MapPost("/projects/{id:guid}/todos", CreateTodo)
            .WithName("CreateExternalProjectTodo")
            .WithSummary("Create a new sprk_todo regarding a Secure Project (ADR-024 resolver fields applied)")
            .Produces<ExternalTodoDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // POST /api/v1/external/projects/{id}/documents — upload a file and create its document row.
        // Container is SERVER-DERIVED from the project (never client-named) and the upload uses
        // ConflictBehavior.Fail so a same-named file can never be overwritten by an external caller.
        group.MapPost("/projects/{id:guid}/documents", UploadDocument)
            .WithName("UploadExternalProjectDocument")
            .WithSummary("Upload a document to a Secure Project (server-derived container, app-only)")
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery() // multipart upload; auth is the JWT + participation gate, not a form token
            .Produces<ExternalDocumentDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET /api/v1/external/projects/{id}/documents/{documentId}/versions — SPE version history
        group.MapGet("/projects/{id:guid}/documents/{documentId:guid}/versions", GetDocumentVersions)
            .WithName("GetExternalDocumentVersions")
            .WithSummary("Get SPE version history for a document in a Secure Project")
            .Produces<ExternalDocumentVersionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /api/v1/external/projects/{id}/events — calendar events for the project
        group.MapGet("/projects/{id:guid}/events", GetEvents)
            .WithName("GetExternalProjectEvents")
            .WithSummary("Get calendar events for a Secure Project (sprk_event records regarding the project)")
            .Produces<ExternalCollectionResponse<ExternalEventDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // POST /api/v1/external/projects/{id}/events — create a calendar event on the project
        group.MapPost("/projects/{id:guid}/events", CreateEvent)
            .WithName("CreateExternalProjectEvent")
            .WithSummary("Create a new sprk_event regarding a Secure Project")
            .Produces<ExternalEventDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET /api/v1/external/projects/{id}/contacts
        group.MapGet("/projects/{id:guid}/contacts", GetContacts)
            .WithName("GetExternalProjectContacts")
            .WithSummary("Get contacts with access to a Secure Project")
            .Produces<ExternalCollectionResponse<ExternalContactDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET /api/v1/external/projects/{id}/organizations
        group.MapGet("/projects/{id:guid}/organizations", GetOrganizations)
            .WithName("GetExternalProjectOrganizations")
            .WithSummary("Get organizations linked to contacts on a Secure Project")
            .Produces<ExternalCollectionResponse<ExternalOrganizationDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // PATCH /api/v1/external/todos/{id} — update a to-do
        group.MapPatch("/todos/{id:guid}", UpdateTodo)
            .WithName("UpdateExternalTodo")
            .WithSummary("Update a to-do (PATCH semantics — only provided fields are changed)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    // =========================================================================
    // Handlers
    // =========================================================================

    private static async Task<IResult> GetProjects(
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        var projectIds = callerContext.GetAccessibleProjectIds().ToList();
        if (projectIds.Count == 0)
            return Results.Ok(new ExternalCollectionResponse<ExternalProjectDto>());

        var projects = await dataService.GetProjectsAsync(projectIds, ct);
        return Results.Ok(new ExternalCollectionResponse<ExternalProjectDto> { Value = projects });
    }

    private static async Task<IResult> GetProjectById(
        Guid id,
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (!callerContext.HasProjectAccess(id))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");

        var project = await dataService.GetProjectByIdAsync(id, ct);
        return project is null ? Results.NotFound() : Results.Ok(project);
    }

    private static async Task<IResult> GetDocuments(
        Guid id,
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (!callerContext.HasProjectAccess(id))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");

        var documents = await dataService.GetDocumentsAsync(id, ct);
        return Results.Ok(new ExternalCollectionResponse<ExternalDocumentDto> { Value = documents });
    }

    /// <summary>
    /// Streams a document's content to an authorized external caller.
    ///
    /// <b>Authz-before-stream (highest-consequence property).</b> Authorization — project access AND
    /// document→project scoping — is fully enforced BEFORE any SPE pointer resolution or Graph read.
    /// An unauthorized caller receives 403 with NO bytes and NO Graph call. Broker-only: streaming uses
    /// the existing app-only <see cref="SpeFileStore.DownloadFileAsync(string, string, CancellationToken)"/>
    /// — NOT the OBO path — and no Graph pointer (driveId/itemId) is ever returned to the client.
    /// </summary>
    private static async Task<IResult> DownloadDocumentContent(
        Guid id,
        Guid documentId,
        HttpContext httpContext,
        ExternalDataService dataService,
        IDocumentStorageResolver storageResolver,
        ISpeFileOperations fileStore,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        // ── AUTHORIZATION FIRST — nothing below reads SPE/Graph until BOTH checks pass ──
        // (1) Project-level access from the caller's participation set.
        if (!callerContext.HasProjectAccess(id))
        {
            logger.LogWarning("[EXT-DOWNLOAD] Contact {ContactId} denied — no access to project {ProjectId}",
                callerContext.ContactId, id);
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");
        }

        // (2) Document→project scoping — the requested document MUST belong to the requested project.
        //     A mismatch OR a non-existent document is a uniform 403 (do not leak document existence).
        //     This is an app-only Dataverse authorization read — it resolves NO Graph pointer.
        var (documentProjectId, documentName) = await dataService.GetDocumentProjectAndNameAsync(documentId, ct);
        if (documentProjectId is null || documentProjectId.Value != id)
        {
            logger.LogWarning(
                "[EXT-DOWNLOAD] Contact {ContactId} denied — document {DocumentId} not in project {ProjectId}",
                callerContext.ContactId, documentId, id);
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "This document is not part of the requested project");
        }

        // ── AUTHORIZED — only now resolve SPE pointers (server-side) and stream app-only ──
        try
        {
            var (driveId, itemId) = await storageResolver.GetSpePointersAsync(documentId, ct);

            // App-only download (broker-only). MUST NOT be the OBO DownloadFileAsUserAsync path.
            var stream = await fileStore.DownloadFileAsync(driveId, itemId, ct);
            if (stream is null)
            {
                return Results.Problem(statusCode: 404, title: "Not Found",
                    detail: "Document content is not available.");
            }

            logger.LogInformation(
                "[EXT-DOWNLOAD] Contact {ContactId} downloaded document {DocumentId} from project {ProjectId}",
                callerContext.ContactId, documentId, id);

            // application/octet-stream + attachment: force download of untrusted external content
            // (no inline rendering). Pointers are never surfaced to the client.
            var downloadName = string.IsNullOrWhiteSpace(documentName) ? documentId.ToString() : documentName;
            return Results.File(stream, "application/octet-stream", fileDownloadName: downloadName);
        }
        catch (SdapProblemException ex)
        {
            // Post-authorization storage-state problems (document_not_found / no_file_attached /
            // mapping_missing_*) — surface the resolver's stable code + status.
            return Results.Problem(statusCode: ex.StatusCode, title: ex.Title, detail: ex.Detail,
                extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
        }
    }

    private static async Task<IResult> GetTodos(
        Guid id,
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (!callerContext.HasProjectAccess(id))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");

        var todos = await dataService.GetTodosAsync(id, ct);
        return Results.Ok(new ExternalCollectionResponse<ExternalTodoDto> { Value = todos });
    }

    private static async Task<IResult> CreateTodo(
        Guid id,
        CreateExternalTodoRequest request,
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (!callerContext.HasProjectAccess(id))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");

        // Require Create on THIS project. Effective as of task 033: the workforce plane no longer
        // blanket-stamps Collaborate, so a ViewOnly caller now actually fails here.
        var rights = callerContext.GetEffectiveRights(id);
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Create))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "Your access level does not permit creating to-dos on this project",
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = "sdap.access.deny.insufficient_rights"
                });

        if (string.IsNullOrWhiteSpace(request.SprkName))
            return Results.Problem(statusCode: 400, title: "Bad Request",
                detail: "sprk_name is required");

        var created = await dataService.CreateTodoAsync(id, request, ct);
        return Results.Created($"/api/v1/external/todos/{created.SprkTodoid}", created);
    }

    /// <summary>
    /// POST /api/v1/external/projects/{id}/documents — upload a file and create its <c>sprk_document</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Two-stage gate, mirroring <see cref="CreateEvent"/>:</b> project participation, then the
    /// <c>Create</c> right. A View-Only participant can read documents but must never add one.</para>
    ///
    /// <para><b>The container is SERVER-DERIVED and the client cannot name it</b> (#858). The request
    /// carries only the file; the container comes from
    /// <c>RecordContainerResolver.ResolveForRecordAsync("sprk_project", id)</c>, so the authorization key
    /// and the storage target both derive from the same project id and no code path lets them disagree.
    /// The old route this replaces (<c>POST /api/v1/external/documents/upload</c>) never existed
    /// server-side at all, so there is no prior contract to preserve here.</para>
    ///
    /// <para><b>Unresolved fails honestly.</b> A project with no resolvable container is a configuration
    /// state, not a licence to write somewhere else: it returns 422 rather than falling back to a shared
    /// container. <c>FailClosed</c> — a secure project whose own container is missing — is the case the
    /// resolver exists to refuse, and is likewise never substituted.</para>
    ///
    /// <para><b>Upload is app-only with <see cref="ConflictBehavior.Fail"/>.</b> App-only because an
    /// external CIAM contact is not a Dataverse principal and holds no delegated permission on the drive
    /// (same reason the versions and download routes are app-only). <c>Fail</c> because the alternative
    /// is letting an external participant overwrite an existing document by uploading a file of the same
    /// name — the collision returns 409 with the stored file untouched.</para>
    ///
    /// <para><b>The path is a BARE FILE NAME.</b> Any prefix would make Graph implicitly create folder
    /// segments — the phantom-folder defect. Enforced by <c>SpeUploadPathIsFlatGuardTests</c>.</para>
    ///
    /// <para><b>Ordering:</b> bytes first, then the Dataverse row. A failed row-create leaves an
    /// orphaned file in the container rather than a document record pointing at nothing — the recoverable
    /// direction, and the same order the wizard's pipeline uses.</para>
    /// </remarks>
    private static async Task<IResult> UploadDocument(
        Guid id,
        IFormFile file,
        HttpContext httpContext,
        ExternalDataService dataService,
        RecordContainerResolver containerResolver,
        ISpeFileOperations fileStore,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        // ── AUTHORIZATION FIRST — nothing below touches Dataverse or SPE until both checks pass ──
        if (!callerContext.HasProjectAccess(id))
        {
            logger.LogWarning("[EXT-UPLOAD] Contact {ContactId} denied — no access to project {ProjectId}",
                callerContext.ContactId, id);
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");
        }

        var rights = callerContext.GetEffectiveRights(id);
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Create))
        {
            logger.LogWarning(
                "[EXT-UPLOAD] Contact {ContactId} denied — access level lacks Create on project {ProjectId}",
                callerContext.ContactId, id);
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "Your access level does not permit uploading documents to this project",
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = "sdap.access.deny.insufficient_rights"
                });
        }

        if (file is null || file.Length == 0)
            return Results.Problem(statusCode: 400, title: "Bad Request",
                detail: "A non-empty file is required");

        // Named `uploadPath`, not `fileName`, because that is what the value IS: a client-supplied file
        // name becomes the upload path verbatim, and an unsanitized one containing "/" makes Graph
        // create folders (an operator found real folders minted from a typed date). Sanitizing collapses
        // it to a single bare segment. Both facts are enforced by SpeUploadPathIsFlatGuardTests.
        var uploadPath = SpeUploadPath.SanitizeFileName(file.FileName);

        // ── AUTHORIZED — derive the container from the PROJECT, never from the request ──
        ContainerDecision decision;
        try
        {
            decision = await containerResolver.ResolveForRecordAsync("sprk_project", id, ct);
        }
        catch (SdapProblemException ex)
        {
            return Results.Problem(statusCode: ex.StatusCode, title: ex.Title, detail: ex.Detail,
                extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
        }

        if (decision.Outcome is ContainerDecisionOutcome.Unresolved or ContainerDecisionOutcome.FailClosed
            || string.IsNullOrWhiteSpace(decision.ContainerId))
        {
            // Refuse rather than write into whatever container happens to be reachable. For a secure
            // project that substitution IS the leak this project exists to close.
            logger.LogWarning(
                "[EXT-UPLOAD] No usable container for project {ProjectId} (outcome {Outcome}) — refusing upload",
                id, decision.Outcome);
            return Results.Problem(statusCode: 422, title: "Storage not configured",
                detail: "This project has no storage container configured, so the document cannot be "
                        + "uploaded. Please contact the project owner.");
        }

        FileHandleDto? uploaded;
        try
        {
            await using var content = file.OpenReadStream();
            uploaded = await fileStore.UploadSmallAsync(
                decision.ContainerId, uploadPath, content, ConflictBehavior.Fail, ct);
        }
        catch (SpaarkeStorageException ex) when (ex.StatusCode == 409)
        {
            // Nothing was written — the existing document is intact. Stated plainly so the SPA can tell
            // the user to rename, rather than the upload silently replacing someone else's file.
            logger.LogInformation(
                "[EXT-UPLOAD] Contact {ContactId} hit a name collision on '{FileName}' in project {ProjectId}",
                callerContext.ContactId, uploadPath, id);
            return Results.Problem(statusCode: 409, title: "File already exists",
                detail: $"A file named '{uploadPath}' already exists in this project. Nothing was uploaded "
                        + "or changed — rename the file and try again.");
        }

        if (uploaded is null)
        {
            logger.LogError("[EXT-UPLOAD] Upload returned no handle for project {ProjectId}", id);
            return Results.Problem(statusCode: 502, title: "Upload failed",
                detail: "The document could not be stored. Please try again.");
        }

        var created = await dataService.CreateDocumentAsync(
            id,
            new ExternalUploadedFilePointers(
                // FileHandleDto.DriveId is the drive resolved during the upload; fall back to the
                // container we resolved rather than persisting a null pointer.
                DriveId: uploaded.DriveId ?? decision.ContainerId,
                ItemId: uploaded.Id,
                FileName: uploaded.Name,
                FileSizeBytes: uploaded.Size,
                WebUrl: uploaded.WebUrl),
            ct);

        logger.LogInformation(
            "[EXT-UPLOAD] Contact {ContactId} uploaded '{FileName}' to project {ProjectId} as document {DocumentId}",
            callerContext.ContactId, uploadPath, id, created.SprkDocumentid);

        // Response carries the document row only — Graph pointers are never surfaced on this surface.
        return Results.Created(
            $"/api/v1/external/projects/{id}/documents/{created.SprkDocumentid}", created);
    }

    /// <summary>
    /// GET /api/v1/external/projects/{id}/documents/{documentId}/versions — SPE version history.
    /// </summary>
    /// <remarks>
    /// Authorization is the SAME two-stage gate as <c>DownloadDocumentContent</c>, deliberately
    /// mirrored rather than reinvented, and for the same reason: nothing touches SPE/Graph until BOTH
    /// checks pass.
    ///   (1) project participation, and
    ///   (2) document→project scoping — a mismatch OR a non-existent document is a UNIFORM 403, so
    ///       this route cannot be used to probe which document ids exist.
    /// Only then are the SPE pointers resolved and the version list read APP-ONLY. It must not use
    /// the OBO ListFileVersionsAsUserAsync path: an external CIAM contact is not a Dataverse
    /// principal and holds no delegated permission on the drive item.
    /// </remarks>
    private static async Task<IResult> GetDocumentVersions(
        Guid id,
        Guid documentId,
        HttpContext httpContext,
        ExternalDataService dataService,
        IDocumentStorageResolver storageResolver,
        ISpeFileOperations fileStore,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        // ── AUTHORIZATION FIRST — nothing below reads SPE/Graph until BOTH checks pass ──
        if (!callerContext.HasProjectAccess(id))
        {
            logger.LogWarning("[EXT-VERSIONS] Contact {ContactId} denied — no access to project {ProjectId}",
                callerContext.ContactId, id);
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");
        }

        var (documentProjectId, _) = await dataService.GetDocumentProjectAndNameAsync(documentId, ct);
        if (documentProjectId is null || documentProjectId.Value != id)
        {
            logger.LogWarning(
                "[EXT-VERSIONS] Contact {ContactId} denied — document {DocumentId} not in project {ProjectId}",
                callerContext.ContactId, documentId, id);
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "This document is not part of the requested project");
        }

        // ── AUTHORIZED — resolve SPE pointers server-side and read app-only ──
        try
        {
            var (driveId, itemId) = await storageResolver.GetSpePointersAsync(documentId, ct);

            var versions = await fileStore.ListFileVersionsAsync(driveId, itemId, ct);
            if (versions is null)
            {
                return Results.Problem(statusCode: 404, title: "Not Found",
                    detail: "Document content is not available.");
            }

            // Project to the external contract. Graph pointers (driveId/itemId) are NEVER surfaced —
            // same rule the content-download route states explicitly.
            var dto = versions
                .Select(v => new ExternalDocumentVersionDto
                {
                    VersionId = v.Id,
                    // For a SharePoint driveItemVersion the id IS the human version label
                    // ("1.0", "2.0", …), so this is the same value, not a fabricated one.
                    VersionLabel = v.Id,
                    CreatedAt = v.LastModifiedDateTime.ToString("o"),
                    FileSizeBytes = v.Size,
                    // CreatedByName is intentionally left null: VersionInfoDto does not carry
                    // lastModifiedBy, and inventing an author is worse than omitting one. The client
                    // types it optional and renders a dash. Widening VersionInfoDto is a separate change.
                })
                .ToList();

            return Results.Ok(new ExternalDocumentVersionsResponse { Versions = dto });
        }
        catch (SdapProblemException ex)
        {
            return Results.Problem(statusCode: ex.StatusCode, title: ex.Title, detail: ex.Detail);
        }
    }

    /// <summary>
    /// GET /api/v1/external/projects/{id}/events — calendar events for a project.
    /// </summary>
    /// <remarks>
    /// Same participation gate as every other project-scoped read: the caller must hold a
    /// participation record for {id} (<see cref="CallerPrincipal.HasProjectAccess"/>) or this
    /// returns 403 before any Dataverse read happens.
    /// </remarks>
    private static async Task<IResult> GetEvents(
        Guid id,
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (!callerContext.HasProjectAccess(id))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");

        var events = await dataService.GetEventsAsync(id, ct);
        return Results.Ok(new ExternalCollectionResponse<ExternalEventDto> { Value = events });
    }

    /// <summary>
    /// POST /api/v1/external/projects/{id}/events — create a calendar event on a project.
    /// </summary>
    /// <remarks>
    /// Two-stage gate, mirroring <see cref="CreateTodo"/> exactly: project participation first,
    /// then the <c>Create</c> right. Read access alone must not permit a write — a View-Only
    /// external user can list events but never add one.
    /// </remarks>
    private static async Task<IResult> CreateEvent(
        Guid id,
        CreateExternalEventRequest request,
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (!callerContext.HasProjectAccess(id))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");

        // Require Create on THIS project (see CreateTodo — effective as of task 033).
        var rights = callerContext.GetEffectiveRights(id);
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Create))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "Your access level does not permit creating events on this project",
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = "sdap.access.deny.insufficient_rights"
                });

        if (string.IsNullOrWhiteSpace(request.SprkName))
            return Results.Problem(statusCode: 400, title: "Bad Request",
                detail: "sprk_name is required");

        var created = await dataService.CreateEventAsync(id, request, ct);
        return Results.Created($"/api/v1/external/projects/{id}/events/{created.SprkEventid}", created);
    }

    private static async Task<IResult> GetContacts(
        Guid id,
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (!callerContext.HasProjectAccess(id))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");

        var contacts = await dataService.GetContactsAsync(id, ct);
        return Results.Ok(new ExternalCollectionResponse<ExternalContactDto> { Value = contacts });
    }

    private static async Task<IResult> GetOrganizations(
        Guid id,
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (!callerContext.HasProjectAccess(id))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");

        var organizations = await dataService.GetOrganizationsAsync(id, ct);
        return Results.Ok(new ExternalCollectionResponse<ExternalOrganizationDto> { Value = organizations });
    }

    private static async Task<IResult> UpdateTodo(
        Guid id,
        UpdateExternalTodoRequest request,
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        // FR-08 / finding A-7 (task 009, 2026-08-24) — scope the write BEFORE mutating.
        //
        // This handler previously applied the PATCH with no record-scope check at all: any caller
        // who resolved to a CallerPrincipal could modify ANY to-do by GUID. The old comment
        // justified it as "low blast radius (only the authenticated user's linked data)" — which
        // was wrong: the route takes an arbitrary to-do id, not one derived from the caller.
        //
        // ADR-003 fail closed: an unreadable to-do, a to-do with no resolvable root, an ambiguous
        // root, and a root outside the caller's accessible set ALL deny. The PATCH is never applied
        // on a failed or ambiguous read. GetTodoRootAsync cannot distinguish "absent" from
        // "unreadable" (see its remarks) — both land in the deny paths below, which is the point.
        var (rootKind, rootId, todoName) = await dataService.GetTodoRootAsync(id, ct);

        if (todoName is null)
            return Results.Problem(statusCode: 404, title: "Not Found",
                detail: "To-do not found");

        // Scope on whichever of the three A-9 root sets the to-do is parented to. Owner decision
        // 2026-08-24: matter and work assignment get the same functionality as project.
        //
        // ✅ THE ASYMMETRY IS GONE (unified-access-control-r2 task 033 / FR-19 / register A-8).
        //
        // This block used to carry a long comment explaining that matter and work-assignment access
        // were "bare id sets with no level anywhere in the pipeline", so for those two root types
        // MEMBERSHIP IMPLIED WRITE — a caller who would have been ViewOnly on a project could edit
        // matter-parented to-dos. That was an accurate description of the code, and it was load-bearing
        // in the wrong direction: it read as a settled design decision, so the next reader honoured it
        // instead of fixing it (FAILURE-MODES AP-12).
        //
        // Tasks 032 + 033 removed the premise. Grant rows always carried sprk_accesslevel for all three
        // root types; the level was simply dropped at partitioning. CallerPrincipal now carries
        // (recordId -> AccessRights) for projects, matters AND work assignments, so all three are gated
        // identically below. There is no root type left for which membership implies write.
        var rights = rootKind switch
        {
            ExternalDataService.TodoRootKind.Project =>
                callerContext.GetEffectiveRights(rootId!.Value),
            ExternalDataService.TodoRootKind.Matter =>
                callerContext.GetMatterRights(rootId!.Value),
            ExternalDataService.TodoRootKind.WorkAssignment =>
                callerContext.GetWorkAssignmentRights(rootId!.Value),
            // None (absent parent, or one of the ten non-scopeable regarding types) and Ambiguous
            // (more than one root lookup populated) both deny. Same response as out-of-scope so the
            // caller cannot infer WHY.
            _ => Spaarke.Dataverse.AccessRights.None,
        };

        // Out-of-scope and insufficient-rights are ONE check: every accessor above returns None for a
        // record the caller cannot reach, so an absent record can never satisfy Write. Keeping them as
        // one expression means a future root type cannot be added to the scope branch while being
        // forgotten in the rights branch.
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Write))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this to-do",
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = "sdap.access.deny.insufficient_rights"
                });

        await dataService.UpdateTodoAsync(id, request, ct);
        return Results.NoContent();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    // Principal-agnostic caller (teams-app-r1 task 025): the CallerPrincipalAuthorizationFilter
    // (group-level) resolves EITHER a CIAM external contact OR a workforce user to a CallerPrincipal.
    // CallerPrincipal exposes the same record-scope surface the handlers use — HasProjectAccess,
    // GetAccessibleProjectIds, GetEffectiveRights — so every handler is plane-agnostic without change.
    private static CallerPrincipal? GetCallerPrincipal(HttpContext httpContext) =>
        httpContext.Items[CallerPrincipal.HttpContextItemsKey] as CallerPrincipal;

    private static IResult MissingContextResult() =>
        Results.Problem(
            statusCode: 500,
            title: "Internal Server Error",
            detail: "Authentication context not available — ensure AddCallerPrincipalAuthorizationFilter is applied");
}
