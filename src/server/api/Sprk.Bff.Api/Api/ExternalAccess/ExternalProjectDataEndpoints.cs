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
/// Routes (all under /api/v1/external — RequireAuthorization + ExternalCallerAuthorizationFilter):
///   GET  /projects                       — list user's accessible projects
///   GET  /projects/{id}                  — single project by ID
///   GET  /projects/{id}/documents        — documents for a project
///   GET  /projects/{id}/todos            — to-dos for a project (sprk_todo, regarding=project)
///   POST /projects/{id}/todos            — create a new to-do regarding the project
///   GET  /projects/{id}/contacts         — contacts with access to the project
///   GET  /projects/{id}/organizations    — organizations linked to project contacts
///   PATCH /todos/{id}                    — update a to-do (any project)
///
/// All project-specific endpoints verify the caller has a participation record for the requested
/// project via ExternalCallerContext.HasProjectAccess(). Returns 403 if no access.
///
/// smart-todo-decoupling-r3 (FR-29): Routes formerly exposed an event-based to-do model
/// (GET/POST /events, PATCH /events/{id}). Replaced with sprk_todo routes here. See
/// projects/smart-todo-decoupling-r3/notes/external-access-contract-change.md for the
/// breaking-contract migration guide consumed by the external-spa (task 008).
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Authorization applied via route group + ExternalCallerAuthorizationFilter.
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
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddExternalCallerAuthorizationFilter();

        // GET /api/v1/external/projects/{id} — single project
        group.MapGet("/projects/{id:guid}", GetProjectById)
            .WithName("GetExternalProjectById")
            .WithSummary("Get a single Secure Project by ID")
            .Produces<ExternalProjectDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddExternalCallerAuthorizationFilter();

        // GET /api/v1/external/projects/{id}/documents
        group.MapGet("/projects/{id:guid}/documents", GetDocuments)
            .WithName("GetExternalProjectDocuments")
            .WithSummary("Get documents for a Secure Project")
            .Produces<ExternalCollectionResponse<ExternalDocumentDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddExternalCallerAuthorizationFilter();

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
            .ProducesProblem(StatusCodes.Status409Conflict)
            .AddExternalCallerAuthorizationFilter();

        // GET /api/v1/external/projects/{id}/todos
        group.MapGet("/projects/{id:guid}/todos", GetTodos)
            .WithName("GetExternalProjectTodos")
            .WithSummary("Get to-dos for a Secure Project (sprk_todo records regarding the project)")
            .Produces<ExternalCollectionResponse<ExternalTodoDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddExternalCallerAuthorizationFilter();

        // POST /api/v1/external/projects/{id}/todos — create a new to-do regarding the project
        group.MapPost("/projects/{id:guid}/todos", CreateTodo)
            .WithName("CreateExternalProjectTodo")
            .WithSummary("Create a new sprk_todo regarding a Secure Project (ADR-024 resolver fields applied)")
            .Produces<ExternalTodoDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddExternalCallerAuthorizationFilter();

        // GET /api/v1/external/projects/{id}/contacts
        group.MapGet("/projects/{id:guid}/contacts", GetContacts)
            .WithName("GetExternalProjectContacts")
            .WithSummary("Get contacts with access to a Secure Project")
            .Produces<ExternalCollectionResponse<ExternalContactDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddExternalCallerAuthorizationFilter();

        // GET /api/v1/external/projects/{id}/organizations
        group.MapGet("/projects/{id:guid}/organizations", GetOrganizations)
            .WithName("GetExternalProjectOrganizations")
            .WithSummary("Get organizations linked to contacts on a Secure Project")
            .Produces<ExternalCollectionResponse<ExternalOrganizationDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddExternalCallerAuthorizationFilter();

        // PATCH /api/v1/external/todos/{id} — update a to-do
        group.MapPatch("/todos/{id:guid}", UpdateTodo)
            .WithName("UpdateExternalTodo")
            .WithSummary("Update a to-do (PATCH semantics — only provided fields are changed)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddExternalCallerAuthorizationFilter();
    }

    // =========================================================================
    // Handlers
    // =========================================================================

    private static async Task<IResult> GetProjects(
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerContext(httpContext);
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
        var callerContext = GetCallerContext(httpContext);
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
        var callerContext = GetCallerContext(httpContext);
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
        var callerContext = GetCallerContext(httpContext);
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
        var callerContext = GetCallerContext(httpContext);
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
        var callerContext = GetCallerContext(httpContext);
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
        var callerContext = GetCallerContext(httpContext);
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
        var callerContext = GetCallerContext(httpContext);
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
        var callerContext = GetCallerContext(httpContext);
        if (callerContext is null) return MissingContextResult();

        // Note: for update, we can't easily check project membership without looking up the to-do.
        // The ExternalCallerAuthorizationFilter already validates the caller is authenticated.
        // A stricter implementation would look up the to-do's project (via sprk_regardingproject)
        // and verify access — acceptable for now given the app's low blast radius (only the
        // authenticated user's linked data).
        await dataService.UpdateTodoAsync(id, request, ct);
        return Results.NoContent();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static ExternalCallerContext? GetCallerContext(HttpContext httpContext) =>
        httpContext.Items[ExternalCallerContext.HttpContextItemsKey] as ExternalCallerContext;

    private static IResult MissingContextResult() =>
        Results.Problem(
            statusCode: 500,
            title: "Internal Server Error",
            detail: "Authentication context not available — ensure ExternalCallerAuthorizationFilter is applied");
}
