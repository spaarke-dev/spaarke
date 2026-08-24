using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// Maps all project data endpoints for authenticated external users.
///
/// Routes (all under /api/v1/external — RequireAuthorization + CallerPrincipalAuthorizationFilter):
///   GET  /projects                       — list user's accessible projects
///   GET  /projects/{id}                  — single project by ID
///   GET  /projects/{id}/documents        — documents for a project
///   GET  /projects/{id}/todos            — to-dos for a project (sprk_todo, regarding=project)
///   POST /projects/{id}/todos            — create a new to-do regarding the project
///   GET  /projects/{id}/contacts         — contacts with access to the project
///   GET  /projects/{id}/organizations    — organizations linked to project contacts
///   PATCH /todos/{id}                    — update a to-do (scoped to the caller's projects)
///
/// All project-specific endpoints verify the caller has a participation record for the requested
/// project via CallerPrincipal.HasProjectAccess(). Returns 403 if no access.
///
/// PATCH /todos/{id} takes a to-do id rather than a project id, so it resolves the to-do's
/// regarding-project first (ExternalDataService.GetTodoProjectAsync) and scopes on that — see
/// FR-08 / finding A-7. Writes additionally require the Write right, mirroring the Create right
/// that POST /projects/{id}/todos requires.
///
/// smart-todo-decoupling-r3 (FR-29): Routes formerly exposed an event-based to-do model
/// (GET/POST /events, PATCH /events/{id}). Replaced with sprk_todo routes here. See
/// projects/smart-todo-decoupling-r3/notes/external-access-contract-change.md for the
/// breaking-contract migration guide consumed by the external-spa (task 008).
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

        // Require at least Collaborate access to create to-dos
        var rights = callerContext.GetEffectiveRights(id);
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Create))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "Your access level does not permit creating to-dos on this project");

        if (string.IsNullOrWhiteSpace(request.SprkName))
            return Results.Problem(statusCode: 400, title: "Bad Request",
                detail: "sprk_name is required");

        var created = await dataService.CreateTodoAsync(id, request, ct);
        return Results.Created($"/api/v1/external/todos/{created.SprkTodoid}", created);
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
        // ADR-003 fail closed: an unreadable to-do, a to-do with no resolvable project root, and
        // a project outside the caller's accessible set ALL deny. The PATCH is never applied on a
        // failed or ambiguous read. GetTodoProjectAsync cannot distinguish "absent" from
        // "unreadable" (see its remarks) — both land in the deny paths below, which is the point.
        var (projectId, todoName) = await dataService.GetTodoProjectAsync(id, ct);

        if (todoName is null)
            return Results.Problem(statusCode: 404, title: "Not Found",
                detail: "To-do not found");

        // No resolvable project root — includes to-dos parented to any of the other 12 regarding
        // lookups (matter, work assignment, document, …). Denied so the WRITE surface is never
        // wider than the project-scoped READ surface. Same response as out-of-scope: do not leak
        // WHY the caller was denied.
        if (projectId is null || !callerContext.HasProjectAccess(projectId.Value))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this to-do");

        // Mirror CreateTodo's rights gate (which requires Create) — a PATCH requires Write.
        var rights = callerContext.GetEffectiveRights(projectId.Value);
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Write))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "Your access level does not permit updating to-dos on this project");

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
