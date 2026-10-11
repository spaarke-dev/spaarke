using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Dataverse;

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
///   GET  /matters/{id}/todos             — to-dos for a matter (sprk_todo, regarding=matter)
///   POST /matters/{id}/todos             — create a new to-do regarding the matter
///   GET  /workassignments/{id}/todos     — to-dos for a work assignment
///   POST /workassignments/{id}/todos     — create a new to-do regarding the work assignment
///   PATCH /todos/{id}                    — update a to-do (scoped to its own accessible root)
///   GET  /{matters|workassignments|servicerequests}/{id}/documents                          — documents (task 031)
///   GET  /{matters|workassignments|servicerequests}/{id}/documents/{documentId}/content     — download
///   POST /{matters|workassignments|servicerequests}/{id}/documents                          — upload
///   GET  /{matters|workassignments|servicerequests}/{id}/documents/{documentId}/versions    — version history
///   DELETE /{documents|todos|events}/{id} — delete a record the caller created, with Read on its parent (task 032)
///
/// DOCUMENTS ON EVERY CORE RECORD (task 031 / FR-24): one list, download, versions and upload handler,
/// generalized over the document parent (ExternalDataService.DocumentParentKind). Project, matter and
/// work assignment rights come from RightsForRoot; a service request's from the submitter scope (the
/// Service Requests module's accessible set). Invoice is not mapped: escalated, no Tier-2 answer yet.
///
/// READ routes verify the caller holds Read on the requested record (HoldsReadOnProject /
/// ListTodosForRoot, both through RightsForRoot). Returns 403 if not; NEVER an empty collection, which
/// would be indistinguishable from "this record has no children" (ADR-003). GET /projects lists only
/// projects the caller can read.
///
/// ⚠️ Until unified-access-control-r2 task 136 (defect C2, 2026-10-01) the paragraph above was FALSE for
/// every project read route except the to-do list: they asked HasProjectAccess, which was "the id is in
/// the caller's map", and two live paths put ids there with no rights — a Secure project reached only
/// through an organization grant, and a level-less grant row. Such a caller listed the project, its
/// documents, events, contacts and organizations, and streamed document content app-only. The old note
/// claimed "every participation carries a level", which stopped being true once the map carried the
/// evaluator's answer rather than grant rows.
///
/// MUTATING routes additionally require the specific right from the evaluator's answer FOR THAT
/// RECORD (unified-access-control-r2 task 033 / FR-19): Create on the POSTs, Write on the PATCH.
/// A View Only grant does not permit a write on any route, for ANY root type. These gates existed
/// before task 033 but could not fire on the workforce plane, which blanket-stamped Collaborate
/// over every accessible project; that stamp is deleted.
///
/// TO-DO PARITY (task 029 / FR-08): list, create and update all reach the same three accessible
/// roots — project, matter, work assignment. They previously did not: task 009 widened the PATCH
/// and left list/create project-only, so the WRITE surface was wider than the READ surface and a
/// caller could edit a to-do this plane refused to list. All three verbs now resolve a root's
/// rights through the single RightsForRoot helper, and the create's parent flows from the ROUTE
/// (the request DTO carries no regarding member, so a caller cannot name a parent it was not gated
/// against). The fourth root — service request — is task 028.
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
/// PATCH /events/{id} was NOT restored: its only client wrapper (web-api-client.updateEvent) had zero call sites
/// and was deleted (spaarke-ontology-platform-r1 task 097), so there is no consumer to justify the write surface
/// (CLAUDE.md §11).
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
        // Authz-before-stream (broker-only, app-only): Read on the project (task 136) + document->project
        // scoping are enforced BEFORE any SPE pointer resolution or Graph read (ADR-028 A1, NFR-03).
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
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict); // task 146: no owner resolvable from the root

        // ---------------------------------------------------------------------------------------
        // To-do parity for the other two accessible roots (task 029 / FR-08).
        //
        // Task 009 widened PATCH /todos/{id} to matter- and work-assignment-parented to-dos, and
        // left list + create on project alone. The plane's WRITE surface was therefore WIDER than
        // its READ surface: a caller could edit a to-do the same plane refused to list and could not
        // create. These four routes close that, additively — no shipped route's shape changes.
        //
        // Why routes and not a registered ExternalModuleDescriptor: the generic scoped-fetch plane
        // serves READS only, so a module could carry the list half and never the create half, and
        // moving the list there would change what the shipped SPA must call. Full reasoning in
        // projects/unified-access-control-r2/notes/task-029-external-todo-parity.md §2.
        // ---------------------------------------------------------------------------------------

        group.MapGet("/matters/{id:guid}/todos", GetMatterTodos)
            .WithName("GetExternalMatterTodos")
            .WithSummary("Get to-dos regarding an accessible matter (sprk_regardingmatter)")
            .Produces<ExternalCollectionResponse<ExternalTodoDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/matters/{id:guid}/todos", CreateMatterTodo)
            .WithName("CreateExternalMatterTodo")
            .WithSummary("Create a new sprk_todo regarding a matter (ADR-024 resolver fields applied)")
            .Produces<ExternalTodoDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict); // task 146

        group.MapGet("/workassignments/{id:guid}/todos", GetWorkAssignmentTodos)
            .WithName("GetExternalWorkAssignmentTodos")
            .WithSummary("Get to-dos regarding an accessible work assignment (sprk_regardingworkassignment)")
            .Produces<ExternalCollectionResponse<ExternalTodoDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/workassignments/{id:guid}/todos", CreateWorkAssignmentTodo)
            .WithName("CreateExternalWorkAssignmentTodo")
            .WithSummary("Create a new sprk_todo regarding a work assignment (ADR-024 resolver fields applied)")
            .Produces<ExternalTodoDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict); // task 146

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

        // ---------------------------------------------------------------------------------------
        // Documents on the other core records (spaarke-SPA-external-access-platform-r3 task 031, FR-24).
        //
        // The same four document routes as the project's, for matters, work assignments and service
        // requests, through the SAME four handlers (generalized over the document parent). Each checks
        // Tier-2 on the route record: Read to list, download and read versions, Create to upload. A
        // service request is checked through the submitter scope (the requester is the caller). Invoice
        // is not mapped: task 031 escalated it (no Tier-2 answer for an invoice id). The templates are
        // literal, one registration per route, so the route-authorization ArchTest reads each one.
        // ---------------------------------------------------------------------------------------

        group.MapGet("/matters/{id:guid}/documents", GetMatterDocuments)
            .WithName("GetExternalMatterDocuments")
            .WithSummary("Get documents for a matter the caller can read")
            .Produces<ExternalCollectionResponse<ExternalDocumentDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/matters/{id:guid}/documents/{documentId:guid}/content", DownloadMatterDocumentContent)
            .WithName("DownloadExternalMatterDocument")
            .WithSummary("Download a matter document's content (authz-before-stream, app-only)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/matters/{id:guid}/documents", UploadMatterDocument)
            .WithName("UploadExternalMatterDocument")
            .WithSummary("Upload a document to a matter (server-derived container, app-only)")
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery() // multipart upload; auth is the JWT + Tier-2 gate, not a form token
            .Produces<ExternalDocumentDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/matters/{id:guid}/documents/{documentId:guid}/versions", GetMatterDocumentVersions)
            .WithName("GetExternalMatterDocumentVersions")
            .WithSummary("Get SPE version history for a document on a matter")
            .Produces<ExternalDocumentVersionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/workassignments/{id:guid}/documents", GetWorkAssignmentDocuments)
            .WithName("GetExternalWorkAssignmentDocuments")
            .WithSummary("Get documents for a work assignment the caller can read")
            .Produces<ExternalCollectionResponse<ExternalDocumentDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/workassignments/{id:guid}/documents/{documentId:guid}/content", DownloadWorkAssignmentDocumentContent)
            .WithName("DownloadExternalWorkAssignmentDocument")
            .WithSummary("Download a work assignment document's content (authz-before-stream, app-only)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/workassignments/{id:guid}/documents", UploadWorkAssignmentDocument)
            .WithName("UploadExternalWorkAssignmentDocument")
            .WithSummary("Upload a document to a work assignment (server-derived container, app-only)")
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery() // multipart upload; auth is the JWT + Tier-2 gate, not a form token
            .Produces<ExternalDocumentDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/workassignments/{id:guid}/documents/{documentId:guid}/versions", GetWorkAssignmentDocumentVersions)
            .WithName("GetExternalWorkAssignmentDocumentVersions")
            .WithSummary("Get SPE version history for a document on a work assignment")
            .Produces<ExternalDocumentVersionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/servicerequests/{id:guid}/documents", GetServiceRequestDocuments)
            .WithName("GetExternalServiceRequestDocuments")
            .WithSummary("Get documents for a service request the caller submitted")
            .Produces<ExternalCollectionResponse<ExternalDocumentDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/servicerequests/{id:guid}/documents/{documentId:guid}/content", DownloadServiceRequestDocumentContent)
            .WithName("DownloadExternalServiceRequestDocument")
            .WithSummary("Download a service request document's content (authz-before-stream, app-only)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/servicerequests/{id:guid}/documents", UploadServiceRequestDocument)
            .WithName("UploadExternalServiceRequestDocument")
            .WithSummary("Upload a document to a service request the caller submitted (server-derived container, app-only)")
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery() // multipart upload; auth is the JWT + Tier-2 gate, not a form token
            .Produces<ExternalDocumentDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/servicerequests/{id:guid}/documents/{documentId:guid}/versions", GetServiceRequestDocumentVersions)
            .WithName("GetExternalServiceRequestDocumentVersions")
            .WithSummary("Get SPE version history for a document on a service request the caller submitted")
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
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict); // task 146

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

        // ---------------------------------------------------------------------------------------
        // Delete-own (spaarke-SPA-external-access-platform-r3 task 032, FR-26) — the first DELETE routes on this plane.
        //
        // Hard delete of a document, to-do or event the CALLER created (the task-030 stamp), while the caller still holds
        // Read on every parent the row is filed to. One check decides all three (AuthorizeDeleteOwnAsync); no access level
        // and no Dataverse role lets a caller delete someone else's row here. General delete stays a Dataverse role in the
        // model-driven app. There is no DELETE for messages (owner 2026-10-10: messages are not deletable), and none for
        // service requests yet (task 032 escalation: what happens to the staff records filed to one is an owner decision).
        // ---------------------------------------------------------------------------------------

        group.MapDelete("/documents/{id:guid}", DeleteOwnDocument)
            .WithName("DeleteOwnExternalDocument")
            .WithSummary("Delete a document the caller created, and its file (creator stamp + Read on its parent)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapDelete("/todos/{id:guid}", DeleteOwnTodo)
            .WithName("DeleteOwnExternalTodo")
            .WithSummary("Delete a to-do the caller created (creator stamp + Read on its root)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapDelete("/events/{id:guid}", DeleteOwnEvent)
            .WithName("DeleteOwnExternalEvent")
            .WithSummary("Delete a calendar event the caller created (creator stamp + Read on its root)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
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

        // Read-bearing projects only (task 136 · C2): GetAccessibleProjectIds is a Read-filtered view, so a
        // project the caller holds nothing on is never listed — its name and id are not disclosed.
        var projectIds = callerContext.GetAccessibleProjectIds().ToList();
        if (projectIds.Count == 0)
            return Results.Ok(new ExternalCollectionResponse<ExternalProjectDto>());

        // Task 105: the service's response carries `truncated` when the list was cut short (additive field).
        return Results.Ok(await dataService.GetProjectsAsync(projectIds, ct));
    }

    private static async Task<IResult> GetProjectById(
        Guid id,
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (!HoldsReadOnProject(callerContext, id))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");

        var project = await dataService.GetProjectByIdAsync(id, ct);
        return project is null ? Results.NotFound() : Results.Ok(project);
    }

    // -------------------------------------------------------------------------------------------
    // Documents on every core record (spaarke-SPA-external-access-platform-r3 task 031, FR-24).
    //
    // ONE implementation each of list, download, versions and upload, generalized over the document
    // parent (project, matter, work assignment, service request), as task 029 did for to-dos. The route
    // handlers are thin adapters that name their parent kind. The project routes keep their templates,
    // names, statuses and messages: "project" is now one row of the parent map, not a separate code path.
    // -------------------------------------------------------------------------------------------

    private static async Task<IResult> ListDocumentsForParent(
        ExternalDataService.DocumentParentKind parentKind,
        Guid parentId,
        HttpContext httpContext,
        ExternalDataService dataService,
        ExternalModuleRegistry modules,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        // ADR-003: an inaccessible parent DENIES, never an empty list (task 136).
        var (rights, upstreamFault) = await RightsForDocumentParentAsync(callerContext, parentKind, parentId, dataService, modules, ct);
        if (upstreamFault is not null) return upstreamFault;
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Read))
            return DenyDocumentParent(parentKind);

        return Results.Ok(await dataService.GetDocumentsAsync(parentKind, parentId, ct));
    }

    /// <summary>
    /// Streams a document's content to an authorized external caller.
    ///
    /// <b>Authz-before-stream (highest-consequence property).</b> Authorization — Read on the parent AND
    /// document→parent scoping — is fully enforced BEFORE any SPE pointer resolution or Graph read.
    /// An unauthorized caller receives 403 with NO bytes and NO Graph call. Broker-only: streaming uses
    /// the existing app-only <see cref="SpeFileStore.DownloadFileAsync(string, string, CancellationToken)"/>
    /// — NOT the OBO path — and no Graph pointer (driveId/itemId) is ever returned to the client.
    /// </summary>
    private static async Task<IResult> DownloadDocumentForParent(
        ExternalDataService.DocumentParentKind parentKind,
        Guid id,
        Guid documentId,
        HttpContext httpContext,
        ExternalDataService dataService,
        ExternalModuleRegistry modules,
        IDocumentStorageResolver storageResolver,
        ISpeFileOperations fileStore,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        // ── AUTHORIZATION FIRST — nothing below reads SPE/Graph until BOTH checks pass ──
        // (1) Read on the parent (task 136 · C2). Presence in the caller's map is NOT enough: a Secure
        //     project reached only through an organization grant used to sit there with no rights, and this
        //     route then streamed its documents app-only.
        var (rights, upstreamFault) = await RightsForDocumentParentAsync(callerContext, parentKind, id, dataService, modules, ct);
        if (upstreamFault is not null) return upstreamFault;
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Read))
        {
            logger.LogWarning("[EXT-DOWNLOAD] Contact {ContactId} denied — no access to {ParentKind} {ParentId}",
                callerContext.ContactId, parentKind, id);
            return DenyDocumentParent(parentKind);
        }

        // (2) Document→parent scoping — the requested document MUST belong to the requested record through
        //     that record type's own lookup. A mismatch OR a non-existent document is a uniform 403 (do not
        //     leak document existence). This is an app-only Dataverse authorization read — it resolves NO
        //     Graph pointer.
        var (documentParentId, documentName) =
            await dataService.GetDocumentParentAndNameAsync(parentKind, documentId, ct);
        if (documentParentId is null || documentParentId.Value != id)
        {
            logger.LogWarning(
                "[EXT-DOWNLOAD] Contact {ContactId} denied — document {DocumentId} not in {ParentKind} {ParentId}",
                callerContext.ContactId, documentId, parentKind, id);
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: $"This document is not part of the requested {DocumentParentNoun(parentKind)}");
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
                "[EXT-DOWNLOAD] Contact {ContactId} downloaded document {DocumentId} from {ParentKind} {ParentId}",
                callerContext.ContactId, documentId, parentKind, id);

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

    // -------------------------------------------------------------------------------------------
    // To-do list + create, shared across all three accessible roots (task 029 / FR-08).
    //
    // There is ONE implementation of list-a-to-do and ONE of create-a-to-do. The six route handlers
    // below are thin adapters that name their root kind. Three copies of an authorization sequence
    // is how the read plane and the write plane drifted apart in the first place.
    // -------------------------------------------------------------------------------------------

    private static async Task<IResult> ListTodosForRoot(
        ExternalDataService.TodoRootKind rootKind,
        Guid rootId,
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        // ADR-003: an out-of-scope root DENIES. It must never fall through to an empty collection —
        // "[]" is indistinguishable from "this root has no to-dos", so a denial rendered as an empty
        // list is a denial the caller (and any auditor) cannot see.
        //
        // Read is the gate. For a project this is exactly the membership test it replaces: every
        // participation carries a level, and the lowest (ViewOnly) already maps to Read — so no
        // caller who could list project to-dos before can be denied now.
        var rights = RightsForRoot(callerContext, rootKind, rootId);
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Read))
            return DenyRoot(rootKind);

        return Results.Ok(await dataService.GetTodosAsync(rootKind, rootId, ct));
    }

    private static async Task<IResult> CreateTodoForRoot(
        ExternalDataService.TodoRootKind rootKind,
        Guid rootId,
        CreateExternalTodoRequest request,
        HttpContext httpContext,
        ExternalDataService dataService,
        IRecordOwnershipResolver ownership,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        // 🔴 THE CREATE-SIDE LEVEL DECISION (task 029), made HERE and not left implicit.
        //
        // The task file framed this as a forced choice: either follow task 009's precedent that
        // "membership implies write" for matter/work assignment, or block create on those roots
        // entirely — because their accessible sets were bare id sets carrying no level.
        //
        // BOTH BRANCHES ARE OBSOLETE. Tasks 032 + 033 (FR-19) gave CallerPrincipal a per-record
        // (id -> AccessRights) map for matters and work assignments too; the id sets are DERIVED
        // from those maps. So all three roots are gated identically on Create, with no root for
        // which membership implies anything. A ViewOnly matter participant cannot create here, which
        // is the same answer a ViewOnly project participant has always got.
        //
        // ADR-003 fail closed: this runs BEFORE any Dataverse write, and RightsForRoot returns None
        // for a root the caller cannot reach — so out-of-scope and insufficient-rights are one
        // check, and a root type added to one branch cannot be forgotten in the other.
        var rights = RightsForRoot(callerContext, rootKind, rootId);
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Create))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "Your access level does not permit creating to-dos on this record",
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = "sdap.access.deny.insufficient_rights"
                });

        if (string.IsNullOrWhiteSpace(request.SprkName))
            return Results.Problem(statusCode: 400, title: "Bad Request",
                detail: "sprk_name is required");

        // Task 106: sprk_todo.sprk_duedate is a calendar date (Dataverse Date Only); refuse what is not one rather than
        // let Dataverse answer 400 behind a 500.
        if (DueDateProblem(request.SprkDuedate) is { } badDueDate)
            return badDueDate;

        // The parent flows from the ROUTE — the owner's "flows from the creation context". The
        // caller cannot name a parent in the body: CreateExternalTodoRequest is a closed DTO with no
        // regarding member, so the root gated above is necessarily the root written.
        //
        // Task 146: the to-do is owned by its ROOT's team — the named Secure team when the root is secure — resolved
        // before anything is written. A refusal is a ProblemDetails, and nothing is created.
        var rootEntity = ExternalDataService.TryGetRootBinding(rootKind)!.EntityLogicalName;
        var (owningTeamId, refusal) = await ResolveChildOwnerAsync(ownership, rootEntity, rootId, "to-do", ct);
        if (refusal is not null)
            return refusal;

        // Task 152 (#1044 split): the calling contact is the triggering person — it becomes Assigned To. A workforce
        // systemuser with no linked contact carries Guid.Empty here; the to-do is then created unassigned (logged).
        // Task 030 (FR-25): the resolved principal is also the creator stamp — never anything from the request body.
        var created = await dataService.CreateTodoAsync(
            rootKind,
            rootId,
            request,
            owningTeamId,
            callerContext.ContactId == Guid.Empty ? null : callerContext.ContactId,
            callerContext,
            ct);
        return Results.Created($"/api/v1/external/todos/{created.SprkTodoid}", created);
    }

    /// <summary>
    /// The owner of a child created on this surface (unified-access-control-r2 task 146, C10 part 2): resolved
    /// record-first from the ROOT the route names, through the ONE resolver — the named Secure team when the root is
    /// secure, the root's business-unit default team otherwise. Every create here is app-only, so without it
    /// Dataverse makes the BFF application user the owner, in the root business unit, readable by any root-BU user
    /// with ordinary depth: the hole that left a secure project's children unisolated.
    /// </summary>
    /// <remarks>
    /// <para><b>A refusal</b> (the root missing or unreadable, flagged secure but not isolated, its team missing) is
    /// a 409 ProblemDetails carrying the stable <see cref="RecordOwnerRefusal"/> code as <c>reasonCode</c> and a detail
    /// naming the reason. Nothing is written — the external upload resolves this BEFORE the bytes go to SPE.</para>
    /// <para><b>A Dataverse fault</b> during resolution is not a refusal: it propagates as the request's 5xx
    /// (PR #1045 F2), so a throttled read is retried rather than reported as a configuration problem.</para>
    /// <para>Contact access is untouched: contacts reach a root's children through these app-only routes, keyed on
    /// the root, whatever the child's owner.</para>
    /// </remarks>
    private static async Task<(Guid OwningTeamId, IResult? Refusal)> ResolveChildOwnerAsync(
        IRecordOwnershipResolver ownership, string rootEntityLogicalName, Guid rootId, string childNoun,
        CancellationToken ct)
    {
        var resolution = await ownership.ResolveOwnerAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = rootEntityLogicalName, TargetRecordId = rootId },
            ct);

        if (resolution.IsOwned)
            return (resolution.OwningTeamId!.Value, null);

        return (Guid.Empty, Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.RecordOwnerRefused(resolution, childNoun));
    }

    /// <summary>
    /// The caller's rights on one accessible root, whichever kind it is.
    /// </summary>
    /// <remarks>
    /// Hoisted out of <see cref="UpdateTodo"/> by task 029, which now shares it with list and create.
    /// Its own comment warned that "a future root type cannot be added to the scope branch while
    /// being forgotten in the rights branch" — three copies of the switch would have reintroduced
    /// exactly that risk one level up. Adding the fourth root (service request, task 028) is one arm
    /// here plus one row in <c>TodoRootBinding</c>.
    ///
    /// <para>None and Ambiguous return <see cref="Spaarke.Dataverse.AccessRights.None"/>, so they
    /// deny through the same expression as an out-of-scope root, and the caller cannot tell which
    /// happened.</para>
    /// </remarks>
    private static Spaarke.Dataverse.AccessRights RightsForRoot(
        CallerPrincipal caller, ExternalDataService.TodoRootKind rootKind, Guid rootId) => rootKind switch
        {
            ExternalDataService.TodoRootKind.Project => caller.GetEffectiveRights(rootId),
            ExternalDataService.TodoRootKind.Matter => caller.GetMatterRights(rootId),
            ExternalDataService.TodoRootKind.WorkAssignment => caller.GetWorkAssignmentRights(rootId),
            _ => Spaarke.Dataverse.AccessRights.None,
        };

    /// <summary>
    /// The read gate every project-scoped GET route uses (unified-access-control-r2 task 136 · defect C2):
    /// does the caller hold <see cref="Spaarke.Dataverse.AccessRights.Read"/> on the project?
    /// </summary>
    /// <remarks>
    /// The same test <see cref="ListTodosForRoot"/> already applied, through the same <see cref="RightsForRoot"/>
    /// expression. These routes previously asked <c>HasProjectAccess</c> — "is the id in the caller's map?" — and
    /// two live paths put ids there with no rights (a Secure project reached only through an organization grant;
    /// a level-less grant row), so a caller holding nothing listed the project's documents and streamed their
    /// content app-only. Absent and None-rights both deny here: <c>HasFlag(Read)</c> is false for None.
    /// </remarks>
    private static bool HoldsReadOnProject(CallerPrincipal caller, Guid projectId) =>
        RightsForRoot(caller, ExternalDataService.TodoRootKind.Project, projectId)
            .HasFlag(Spaarke.Dataverse.AccessRights.Read);

    /// <summary>
    /// The caller's rights on one document parent (task 031, FR-24): Read gates list, download and versions;
    /// Create gates upload. Fail-closed: a parent the caller cannot reach is <see cref="Spaarke.Dataverse.AccessRights.None"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Project, matter, work assignment</b> are accessible roots: their rights are the evaluator's answer on
    /// the principal, through the same <see cref="RightsForRoot"/> the to-do routes use.</para>
    /// <para><b>A service request is not a root.</b> It is authorized through the submitter scope, the Service
    /// Requests module's accessible set (<c>sprk_requestedby</c> = the caller's contact; CIAM partners only when
    /// <c>ExternalAccess:PartnerServiceRequestsEnabled</c> is on). The requester alone gets Read and Create; anyone
    /// else, and a request that does not exist, gets None. Both deny identically, so an id cannot be probed. A missing
    /// module descriptor, or a module that grants the caller nothing, denies before any read. A requester read that
    /// FAILS (throttle, outage) is a 503 <see cref="ServiceRequestUnreadableReasonCode"/>, never a 403.</para>
    /// </remarks>
    private static async Task<(Spaarke.Dataverse.AccessRights Rights, IResult? Fault)> RightsForDocumentParentAsync(
        CallerPrincipal caller,
        ExternalDataService.DocumentParentKind parentKind,
        Guid parentId,
        ExternalDataService dataService,
        ExternalModuleRegistry modules,
        CancellationToken ct)
    {
        switch (parentKind)
        {
            case ExternalDataService.DocumentParentKind.Project:
                return (RightsForRoot(caller, ExternalDataService.TodoRootKind.Project, parentId), null);
            case ExternalDataService.DocumentParentKind.Matter:
                return (RightsForRoot(caller, ExternalDataService.TodoRootKind.Matter, parentId), null);
            case ExternalDataService.DocumentParentKind.WorkAssignment:
                return (RightsForRoot(caller, ExternalDataService.TodoRootKind.WorkAssignment, parentId), null);
            case ExternalDataService.DocumentParentKind.ServiceRequest:
                try
                {
                    return (await RightsForServiceRequestAsync(caller, parentId, dataService, modules, ct), null);
                }
                catch (ExternalDataService.DataverseUnavailableException)
                {
                    // The requester could not be READ — a fault, not an answer. "Could not tell" must never read as
                    // "no access" (a throttle would 403 the real requester), and it must not read as access either.
                    // Nothing has touched SPE or been written. The detail is generic, so it leaks nothing.
                    return (Spaarke.Dataverse.AccessRights.None, Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Service request unavailable",
                        detail: "The service request could not be checked right now. Try again shortly.",
                        extensions: new Dictionary<string, object?> { ["reasonCode"] = ServiceRequestUnreadableReasonCode }));
                }
            default:
                return (Spaarke.Dataverse.AccessRights.None, null);
        }
    }

    /// <summary>The 503 reason code when a service request's requester could not be read (task 031 review).</summary>
    internal const string ServiceRequestUnreadableReasonCode = "sdap.access.service_request_unreadable";

    private static async Task<Spaarke.Dataverse.AccessRights> RightsForServiceRequestAsync(
        CallerPrincipal caller,
        Guid serviceRequestId,
        ExternalDataService dataService,
        ExternalModuleRegistry modules,
        CancellationToken ct)
    {
        var entity = ExternalDataService
            .GetDocumentParentBinding(ExternalDataService.DocumentParentKind.ServiceRequest).EntityLogicalName;
        var module = modules.FindByEntity(entity);
        if (module is null)
            return Spaarke.Dataverse.AccessRights.None;

        // The module grants this caller nothing at all (a CIAM partner with PartnerServiceRequestsEnabled off, or a
        // principal with no contact): deny BEFORE any Dataverse read. The read could not change the answer.
        if (!module.EffectiveDimensions.Any(d => d.AccessibleIds(caller).Count > 0))
            return Spaarke.Dataverse.AccessRights.None;

        // A genuine "no such request" is null (uniform 403, no oracle); a read that failed throws
        // DataverseUnavailableException, which the caller turns into 503.
        var requester = await dataService.GetServiceRequestRequesterAsync(serviceRequestId, ct);
        return requester is { } requesterId && module.IsRecordAccessible(caller, requesterId)
            ? Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Create
            : Spaarke.Dataverse.AccessRights.None;
    }

    /// <summary>The noun a document parent is named by in a ProblemDetails detail.</summary>
    private static string DocumentParentNoun(ExternalDataService.DocumentParentKind parentKind) => parentKind switch
    {
        ExternalDataService.DocumentParentKind.Matter => "matter",
        ExternalDataService.DocumentParentKind.WorkAssignment => "work assignment",
        ExternalDataService.DocumentParentKind.ServiceRequest => "service request",
        _ => "project",
    };

    private static IResult DenyDocumentParent(ExternalDataService.DocumentParentKind parentKind) =>
        Results.Problem(statusCode: 403, title: "Forbidden",
            detail: $"You do not have access to this {DocumentParentNoun(parentKind)}");

    // Route adapters (task 031). Each names its parent kind; the behaviour lives in the four *ForParent handlers.

    private static Task<IResult> GetDocuments(
        Guid id, HttpContext httpContext, ExternalDataService dataService, ExternalModuleRegistry modules,
        CancellationToken ct) =>
        ListDocumentsForParent(ExternalDataService.DocumentParentKind.Project, id, httpContext, dataService, modules, ct);

    private static Task<IResult> GetMatterDocuments(
        Guid id, HttpContext httpContext, ExternalDataService dataService, ExternalModuleRegistry modules,
        CancellationToken ct) =>
        ListDocumentsForParent(ExternalDataService.DocumentParentKind.Matter, id, httpContext, dataService, modules, ct);

    private static Task<IResult> GetWorkAssignmentDocuments(
        Guid id, HttpContext httpContext, ExternalDataService dataService, ExternalModuleRegistry modules,
        CancellationToken ct) =>
        ListDocumentsForParent(ExternalDataService.DocumentParentKind.WorkAssignment, id, httpContext, dataService, modules, ct);

    private static Task<IResult> GetServiceRequestDocuments(
        Guid id, HttpContext httpContext, ExternalDataService dataService, ExternalModuleRegistry modules,
        CancellationToken ct) =>
        ListDocumentsForParent(ExternalDataService.DocumentParentKind.ServiceRequest, id, httpContext, dataService, modules, ct);

    private static Task<IResult> DownloadDocumentContent(
        Guid id, Guid documentId, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, IDocumentStorageResolver storageResolver, ISpeFileOperations fileStore,
        ILogger<Program> logger, CancellationToken ct) =>
        DownloadDocumentForParent(ExternalDataService.DocumentParentKind.Project, id, documentId, httpContext,
            dataService, modules, storageResolver, fileStore, logger, ct);

    private static Task<IResult> DownloadMatterDocumentContent(
        Guid id, Guid documentId, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, IDocumentStorageResolver storageResolver, ISpeFileOperations fileStore,
        ILogger<Program> logger, CancellationToken ct) =>
        DownloadDocumentForParent(ExternalDataService.DocumentParentKind.Matter, id, documentId, httpContext,
            dataService, modules, storageResolver, fileStore, logger, ct);

    private static Task<IResult> DownloadWorkAssignmentDocumentContent(
        Guid id, Guid documentId, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, IDocumentStorageResolver storageResolver, ISpeFileOperations fileStore,
        ILogger<Program> logger, CancellationToken ct) =>
        DownloadDocumentForParent(ExternalDataService.DocumentParentKind.WorkAssignment, id, documentId, httpContext,
            dataService, modules, storageResolver, fileStore, logger, ct);

    private static Task<IResult> DownloadServiceRequestDocumentContent(
        Guid id, Guid documentId, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, IDocumentStorageResolver storageResolver, ISpeFileOperations fileStore,
        ILogger<Program> logger, CancellationToken ct) =>
        DownloadDocumentForParent(ExternalDataService.DocumentParentKind.ServiceRequest, id, documentId, httpContext,
            dataService, modules, storageResolver, fileStore, logger, ct);

    private static Task<IResult> UploadDocument(
        Guid id, IFormFile file, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, RecordContainerResolver containerResolver, ISpeFileOperations fileStore,
        IRecordOwnershipResolver ownership, ILogger<Program> logger, CancellationToken ct) =>
        UploadDocumentForParent(ExternalDataService.DocumentParentKind.Project, id, file, httpContext, dataService,
            modules, containerResolver, fileStore, ownership, logger, ct);

    private static Task<IResult> UploadMatterDocument(
        Guid id, IFormFile file, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, RecordContainerResolver containerResolver, ISpeFileOperations fileStore,
        IRecordOwnershipResolver ownership, ILogger<Program> logger, CancellationToken ct) =>
        UploadDocumentForParent(ExternalDataService.DocumentParentKind.Matter, id, file, httpContext, dataService,
            modules, containerResolver, fileStore, ownership, logger, ct);

    private static Task<IResult> UploadWorkAssignmentDocument(
        Guid id, IFormFile file, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, RecordContainerResolver containerResolver, ISpeFileOperations fileStore,
        IRecordOwnershipResolver ownership, ILogger<Program> logger, CancellationToken ct) =>
        UploadDocumentForParent(ExternalDataService.DocumentParentKind.WorkAssignment, id, file, httpContext, dataService,
            modules, containerResolver, fileStore, ownership, logger, ct);

    private static Task<IResult> UploadServiceRequestDocument(
        Guid id, IFormFile file, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, RecordContainerResolver containerResolver, ISpeFileOperations fileStore,
        IRecordOwnershipResolver ownership, ILogger<Program> logger, CancellationToken ct) =>
        UploadDocumentForParent(ExternalDataService.DocumentParentKind.ServiceRequest, id, file, httpContext, dataService,
            modules, containerResolver, fileStore, ownership, logger, ct);

    private static Task<IResult> GetDocumentVersions(
        Guid id, Guid documentId, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, IDocumentStorageResolver storageResolver, ISpeFileOperations fileStore,
        Spaarke.Dataverse.IGenericEntityService entityService, ILogger<Program> logger, CancellationToken ct) =>
        ListDocumentVersionsForParent(ExternalDataService.DocumentParentKind.Project, id, documentId, httpContext,
            dataService, modules, storageResolver, fileStore, entityService, logger, ct);

    private static Task<IResult> GetMatterDocumentVersions(
        Guid id, Guid documentId, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, IDocumentStorageResolver storageResolver, ISpeFileOperations fileStore,
        Spaarke.Dataverse.IGenericEntityService entityService, ILogger<Program> logger, CancellationToken ct) =>
        ListDocumentVersionsForParent(ExternalDataService.DocumentParentKind.Matter, id, documentId, httpContext,
            dataService, modules, storageResolver, fileStore, entityService, logger, ct);

    private static Task<IResult> GetWorkAssignmentDocumentVersions(
        Guid id, Guid documentId, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, IDocumentStorageResolver storageResolver, ISpeFileOperations fileStore,
        Spaarke.Dataverse.IGenericEntityService entityService, ILogger<Program> logger, CancellationToken ct) =>
        ListDocumentVersionsForParent(ExternalDataService.DocumentParentKind.WorkAssignment, id, documentId, httpContext,
            dataService, modules, storageResolver, fileStore, entityService, logger, ct);

    private static Task<IResult> GetServiceRequestDocumentVersions(
        Guid id, Guid documentId, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, IDocumentStorageResolver storageResolver, ISpeFileOperations fileStore,
        Spaarke.Dataverse.IGenericEntityService entityService, ILogger<Program> logger, CancellationToken ct) =>
        ListDocumentVersionsForParent(ExternalDataService.DocumentParentKind.ServiceRequest, id, documentId, httpContext,
            dataService, modules, storageResolver, fileStore, entityService, logger, ct);

    private static IResult DenyRoot(ExternalDataService.TodoRootKind rootKind)
    {
        var noun = rootKind switch
        {
            ExternalDataService.TodoRootKind.Matter => "matter",
            ExternalDataService.TodoRootKind.WorkAssignment => "work assignment",
            _ => "project",
        };

        return Results.Problem(statusCode: 403, title: "Forbidden",
            detail: $"You do not have access to this {noun}");
    }

    private static Task<IResult> GetTodos(
        Guid id, HttpContext httpContext, ExternalDataService dataService, CancellationToken ct) =>
        ListTodosForRoot(ExternalDataService.TodoRootKind.Project, id, httpContext, dataService, ct);

    private static Task<IResult> GetMatterTodos(
        Guid id, HttpContext httpContext, ExternalDataService dataService, CancellationToken ct) =>
        ListTodosForRoot(ExternalDataService.TodoRootKind.Matter, id, httpContext, dataService, ct);

    private static Task<IResult> GetWorkAssignmentTodos(
        Guid id, HttpContext httpContext, ExternalDataService dataService, CancellationToken ct) =>
        ListTodosForRoot(ExternalDataService.TodoRootKind.WorkAssignment, id, httpContext, dataService, ct);

    private static Task<IResult> CreateTodo(
        Guid id, CreateExternalTodoRequest request, HttpContext httpContext,
        ExternalDataService dataService, IRecordOwnershipResolver ownership, CancellationToken ct) =>
        CreateTodoForRoot(ExternalDataService.TodoRootKind.Project, id, request, httpContext, dataService, ownership, ct);

    private static Task<IResult> CreateMatterTodo(
        Guid id, CreateExternalTodoRequest request, HttpContext httpContext,
        ExternalDataService dataService, IRecordOwnershipResolver ownership, CancellationToken ct) =>
        CreateTodoForRoot(ExternalDataService.TodoRootKind.Matter, id, request, httpContext, dataService, ownership, ct);

    private static Task<IResult> CreateWorkAssignmentTodo(
        Guid id, CreateExternalTodoRequest request, HttpContext httpContext,
        ExternalDataService dataService, IRecordOwnershipResolver ownership, CancellationToken ct) =>
        CreateTodoForRoot(ExternalDataService.TodoRootKind.WorkAssignment, id, request, httpContext, dataService, ownership, ct);

    /// <summary>
    /// POST /api/v1/external/{projects|matters|workassignments|servicerequests}/{id}/documents — upload a file
    /// and create its <c>sprk_document</c> (task 031 generalized it from project to every document parent).
    /// </summary>
    /// <remarks>
    /// <para><b>Two-stage gate, mirroring <see cref="CreateEvent"/>:</b> Read on the parent, then the
    /// <c>Create</c> right (<see cref="RightsForDocumentParentAsync"/>). A View-Only participant can read
    /// documents but must never add one.</para>
    ///
    /// <para><b>The container is SERVER-DERIVED and the client cannot name it</b> (#858). The request
    /// carries only the file; the container comes from
    /// <c>RecordContainerResolver.ResolveForRecordAsync(parent entity, id)</c>, so the authorization key
    /// and the storage target both derive from the same record id and no code path lets them disagree.
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
    private static async Task<IResult> UploadDocumentForParent(
        ExternalDataService.DocumentParentKind parentKind,
        Guid id,
        IFormFile file,
        HttpContext httpContext,
        ExternalDataService dataService,
        ExternalModuleRegistry modules,
        RecordContainerResolver containerResolver,
        ISpeFileOperations fileStore,
        IRecordOwnershipResolver ownership,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        var noun = DocumentParentNoun(parentKind);
        var parentEntity = ExternalDataService.GetDocumentParentBinding(parentKind).EntityLogicalName;

        // ── AUTHORIZATION FIRST — nothing below touches SPE, or writes Dataverse, until both checks pass ──
        // (A service request's rights need one app-only read of its requester; nothing is written.)
        var (rights, upstreamFault) = await RightsForDocumentParentAsync(callerContext, parentKind, id, dataService, modules, ct);
        if (upstreamFault is not null) return upstreamFault;
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Read))
        {
            logger.LogWarning("[EXT-UPLOAD] Contact {ContactId} denied — no access to {ParentKind} {ParentId}",
                callerContext.ContactId, parentKind, id);
            return DenyDocumentParent(parentKind);
        }

        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Create))
        {
            logger.LogWarning(
                "[EXT-UPLOAD] Contact {ContactId} denied — access level lacks Create on {ParentKind} {ParentId}",
                callerContext.ContactId, parentKind, id);
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: $"Your access level does not permit uploading documents to this {noun}",
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = "sdap.access.deny.insufficient_rights"
                });
        }

        // FR-25 (task 030): the document row is stamped with its creator AFTER the SPE write, and the stamp fails closed
        // for an unattributable principal. Refuse here, before the bytes land, so that failure cannot leave an orphaned
        // file in the container (same rule StampCreator enforces).
        if (!ExternalDataService.CanStampCreator(callerContext))
        {
            logger.LogWarning(
                "[EXT-UPLOAD] Refused upload to {ParentKind} {ParentId}: the caller resolves to neither a systemuser nor a contact",
                parentKind, id);
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "Your identity could not be attributed, so the document cannot be uploaded");
        }

        if (file is null || file.Length == 0)
            return Results.Problem(statusCode: 400, title: "Bad Request",
                detail: "A non-empty file is required");

        // Named `uploadPath`, not `fileName`, because that is what the value IS: a client-supplied file
        // name becomes the upload path verbatim, and an unsanitized one containing "/" makes Graph
        // create folders (an operator found real folders minted from a typed date). Sanitizing collapses
        // it to a single bare segment. Both facts are enforced by SpeUploadPathIsFlatGuardTests.
        var uploadPath = SpeUploadPath.SanitizeFileName(file.FileName);

        // ── AUTHORIZED — resolve the document's OWNER from the parent BEFORE any write (task 146) ──
        // A document filed to a secure record is owned by the named Secure team. Resolved before the SPE upload so a
        // refusal leaves neither bytes nor a row behind.
        var (owningTeamId, ownerRefusal) = await ResolveChildOwnerAsync(ownership, parentEntity, id, "document", ct);
        if (ownerRefusal is not null)
        {
            logger.LogWarning(
                "[EXT-UPLOAD] Refused upload to {ParentKind} {ParentId}: no owner could be resolved for the document",
                parentKind, id);
            return ownerRefusal;
        }

        // ── Derive the container from the PARENT RECORD, never from the request ──
        ContainerDecision decision;
        try
        {
            decision = await containerResolver.ResolveForRecordAsync(parentEntity, id, ct);
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
            // record that substitution IS the leak this project exists to close.
            logger.LogWarning(
                "[EXT-UPLOAD] No usable container for {ParentKind} {ParentId} (outcome {Outcome}) — refusing upload",
                parentKind, id, decision.Outcome);
            return Results.Problem(statusCode: 422, title: "Storage not configured",
                detail: $"This {noun} has no storage container configured, so the document cannot be "
                        + $"uploaded. Please contact the {noun} owner.");
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
                "[EXT-UPLOAD] Contact {ContactId} hit a name collision on '{FileName}' in {ParentKind} {ParentId}",
                callerContext.ContactId, uploadPath, parentKind, id);
            return Results.Problem(statusCode: 409, title: "File already exists",
                detail: $"A file named '{uploadPath}' already exists in this {noun}. Nothing was uploaded "
                        + "or changed — rename the file and try again.");
        }

        if (uploaded is null)
        {
            logger.LogError("[EXT-UPLOAD] Upload returned no handle for {ParentKind} {ParentId}", parentKind, id);
            return Results.Problem(statusCode: 502, title: "Upload failed",
                detail: "The document could not be stored. Please try again.");
        }

        var created = await dataService.CreateDocumentAsync(
            parentKind,
            id,
            new ExternalUploadedFilePointers(
                // FileHandleDto.DriveId is the drive resolved during the upload; fall back to the
                // container we resolved rather than persisting a null pointer.
                DriveId: uploaded.DriveId ?? decision.ContainerId,
                ItemId: uploaded.Id,
                FileName: uploaded.Name,
                FileSizeBytes: uploaded.Size,
                WebUrl: uploaded.WebUrl),
            owningTeamId,
            callerContext, // task 030 (FR-25): the creator stamp comes from the resolved principal
            ct);

        logger.LogInformation(
            "[EXT-UPLOAD] Contact {ContactId} uploaded '{FileName}' to {ParentKind} {ParentId} as document {DocumentId}",
            callerContext.ContactId, uploadPath, parentKind, id, created.SprkDocumentid);

        // Response carries the document row only — Graph pointers are never surfaced on this surface.
        return Results.Created(
            $"/api/v1/external/{DocumentParentSegment(parentKind)}/{id}/documents/{created.SprkDocumentid}", created);
    }

    /// <summary>The route segment of a document parent, for the Location of a created document.</summary>
    private static string DocumentParentSegment(ExternalDataService.DocumentParentKind parentKind) => parentKind switch
    {
        ExternalDataService.DocumentParentKind.Matter => "matters",
        ExternalDataService.DocumentParentKind.WorkAssignment => "workassignments",
        ExternalDataService.DocumentParentKind.ServiceRequest => "servicerequests",
        _ => "projects",
    };

    /// <summary>
    /// GET /api/v1/external/{projects|matters|workassignments|servicerequests}/{id}/documents/{documentId}/versions
    /// — SPE version history.
    /// </summary>
    /// <remarks>
    /// Authorization is the SAME two-stage gate as <c>DownloadDocumentForParent</c>, deliberately
    /// mirrored rather than reinvented, and for the same reason: nothing touches SPE/Graph until BOTH
    /// checks pass.
    ///   (1) Read on the parent (task 136 — was project presence; task 031 — any document parent), and
    ///   (2) document→parent scoping — a mismatch OR a non-existent document is a UNIFORM 403, so
    ///       this route cannot be used to probe which document ids exist.
    /// Only then are the SPE pointers resolved and the version list read APP-ONLY. It must not use
    /// the OBO ListFileVersionsAsUserAsync path: an external CIAM contact is not a Dataverse
    /// principal and holds no delegated permission on the drive item.
    /// </remarks>
    private static async Task<IResult> ListDocumentVersionsForParent(
        ExternalDataService.DocumentParentKind parentKind,
        Guid id,
        Guid documentId,
        HttpContext httpContext,
        ExternalDataService dataService,
        ExternalModuleRegistry modules,
        IDocumentStorageResolver storageResolver,
        ISpeFileOperations fileStore,
        Spaarke.Dataverse.IGenericEntityService entityService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        // ── AUTHORIZATION FIRST — nothing below reads SPE/Graph until BOTH checks pass ──
        var (rights, upstreamFault) = await RightsForDocumentParentAsync(callerContext, parentKind, id, dataService, modules, ct);
        if (upstreamFault is not null) return upstreamFault;
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Read))
        {
            logger.LogWarning("[EXT-VERSIONS] Contact {ContactId} denied — no access to {ParentKind} {ParentId}",
                callerContext.ContactId, parentKind, id);
            return DenyDocumentParent(parentKind);
        }

        var (documentParentId, _) = await dataService.GetDocumentParentAndNameAsync(parentKind, documentId, ct);
        if (documentParentId is null || documentParentId.Value != id)
        {
            logger.LogWarning(
                "[EXT-VERSIONS] Contact {ContactId} denied — document {DocumentId} not in {ParentKind} {ParentId}",
                callerContext.ContactId, documentId, parentKind, id);
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: $"This document is not part of the requested {DocumentParentNoun(parentKind)}");
        }

        // ── AUTHORIZED — resolve SPE pointers server-side and read app-only ──
        try
        {
            var (driveId, itemId) = await storageResolver.GetSpePointersAsync(documentId, ct);

            var listed = await fileStore.ListFileVersionsAsync(driveId, itemId, ct);
            if (listed is null)
            {
                return Results.Problem(statusCode: 404, title: "Not Found",
                    detail: "Document content is not available.");
            }

            // unified-access-control-r2 task 166, owner round 45 item 1: a version a relocation replayed into a moved
            // file reports its ORIGINAL author and date, so the history an external participant sees is unchanged.
            var versions = await Sprk.Bff.Api.Services.Documents.RelocatedVersionHistory.WithOriginalAuthorshipAsync(
                entityService, documentId, itemId, listed, logger, ct);

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
                    // CreatedByName stays null on the EXTERNAL surface: an external participant has never been shown
                    // who wrote a version, and task 166 f1-v2 (round 45 item 1: "the history a user sees is unchanged")
                    // keeps it so — only the dates of replayed versions are the recorded originals. Showing internal
                    // authors to external contacts is a disclosure decision of its own, not a side effect of a move.
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
    /// Same gate as every other project-scoped read: the caller must hold Read on {id}
    /// (<see cref="HoldsReadOnProject"/>, task 136) or this returns 403 before any Dataverse read happens.
    /// </remarks>
    private static async Task<IResult> GetEvents(
        Guid id,
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (!HoldsReadOnProject(callerContext, id))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");

        return Results.Ok(await dataService.GetEventsAsync(id, ct));
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
        IRecordOwnershipResolver ownership,
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

        // Task 097 review F9: never write an arbitrary status integer. Draft or Open only; omitted ⇒ Open. Checked
        // before the owner is resolved, so a malformed body costs no Dataverse read.
        if (request.SprkStatus is { } status && !ExternalDataService.ExternalCreatableStatuses.Contains(status))
            return Results.Problem(statusCode: 400, title: "Bad Request",
                detail: "sprk_status must be Draft (1) or Open (659490001); omit it to create the event Open.");

        // Task 098: sprk_event.sprk_duedate is a calendar date (Dataverse Date Only); refuse what is not one rather than let
        // Dataverse answer 400 behind a 500.
        if (DueDateProblem(request.SprkDuedate) is { } badDueDate)
            return badDueDate;

        // Task 146: owned by the project's team (the named Secure team for a secure project), or refused.
        var (owningTeamId, ownerRefusal) = await ResolveChildOwnerAsync(ownership, "sprk_project", id, "event", ct);
        if (ownerRefusal is not null)
            return ownerRefusal;

        // Task 030 (FR-25): the creator stamp comes from the resolved principal.
        var created = await dataService.CreateEventAsync(id, request, owningTeamId, callerContext, ct);
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

        if (!HoldsReadOnProject(callerContext, id))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");

        return Results.Ok(await dataService.GetContactsAsync(id, ct));
    }

    private static async Task<IResult> GetOrganizations(
        Guid id,
        HttpContext httpContext,
        ExternalDataService dataService,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (!HoldsReadOnProject(callerContext, id))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");

        return Results.Ok(await dataService.GetOrganizationsAsync(id, ct));
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
        //
        // Task 029 hoisted this switch to RightsForRoot and now shares it with the list and create
        // handlers, so all three verbs resolve a root's rights through ONE expression. None (absent
        // parent, or one of the eleven non-scopeable regarding types) and Ambiguous (more than one
        // root lookup populated) both return None there, so they deny with the same response as an
        // out-of-scope root and the caller cannot infer WHY.
        var rights = RightsForRoot(callerContext, rootKind, rootId ?? Guid.Empty);

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

        if (DueDateProblem(request.SprkDuedate) is { } badDueDate)
            return badDueDate;

        // Task 106: only a to-do status reason (its state is written with it — ExternalDataService.TodoStateCodeFor).
        if (request.Statuscode is { } status && ExternalDataService.TodoStateCodeFor(status) is null)
            return Results.Problem(statusCode: 400, title: "Bad Request",
                detail: "statuscode must be Open (1), In Progress (659490001), Completed (2) or Dismissed (659490002).");

        await dataService.UpdateTodoAsync(id, request, ct);
        return Results.NoContent();
    }

    // -------------------------------------------------------------------------------------------
    // Delete-own (spaarke-SPA-external-access-platform-r3 task 032, FR-26)
    // -------------------------------------------------------------------------------------------

    /// <summary>The reason code of every delete-own refusal: one code, so a refusal says nothing about WHY.</summary>
    internal const string DeleteOwnDeniedReasonCode = "sdap.access.deny.delete_own";

    /// <summary>The reason code when the row could not be read or deleted right now (nothing was deleted).</summary>
    internal const string DeleteOwnUnavailableReasonCode = "sdap.access.delete_own_unavailable";

    /// <summary>
    /// THE delete-own check (FR-26): every external delete runs it, and nothing is deleted unless it returns
    /// <see langword="null"/>. Otherwise it returns the response to send.
    /// </summary>
    /// <remarks>
    /// <para><b>Both halves must hold</b> (project CLAUDE.md §2):</para>
    /// <list type="number">
    /// <item><b>The caller created the row</b>: <see cref="ExternalDataService.IsCreatedByCaller"/> over the row's
    /// task-030 stamps (owner decision 2026-10-11, option (b) widened). A row with no stamp is nobody's. No access level
    /// (FullAccess included) and no Dataverse role can stand in for the stamp.</item>
    /// <item><b>The caller still holds Read on EVERY parent the row is filed to</b>, through the same
    /// <see cref="RightsForDocumentParentAsync"/> the list and download routes use (a project, matter or work assignment
    /// from the principal's rights; a service request through the requester scope). A row filed to no parent this plane
    /// authorizes is refused. Every parent, not any: a document also filed by staff to a record the caller can no longer
    /// read is not the caller's to destroy.</item>
    /// </list>
    /// <para><b>No existence oracle.</b> A row that does not exist, someone else's row, an unstamped row and a row whose
    /// parent the caller lost all get the SAME 403 (<see cref="DeleteOwnDeniedReasonCode"/>). A read that FAILED is a 503
    /// (<see cref="DeleteOwnUnavailableReasonCode"/>, or the service-request 503 of
    /// <see cref="RightsForDocumentParentAsync"/>), never a 403 and never a delete.</para>
    /// <para>App-only, like every read on this plane: no OBO (ADR-028).</para>
    /// </remarks>
    private static async Task<IResult?> AuthorizeDeleteOwnAsync(
        CallerPrincipal caller,
        ExternalDataService.DeletableKind kind,
        Guid id,
        ExternalDataService dataService,
        ExternalModuleRegistry modules,
        ILogger logger,
        CancellationToken ct)
    {
        ExternalDataService.DeleteTarget? target;
        try
        {
            target = await dataService.GetDeleteTargetAsync(kind, id, ct);
        }
        catch (ExternalDataService.DataverseUnavailableException)
        {
            return DeleteOwnUnavailable();
        }

        // (1) The creator stamp. Absent and not-yours deny identically.
        if (target is null || !ExternalDataService.IsCreatedByCaller(target, caller))
        {
            logger.LogWarning("[EXT-DELETE] Refused {Kind} {Id}: absent or not created by the caller", kind, id);
            return DenyDeleteOwn();
        }

        // (2) Read on every parent the row is filed to.
        if (target.Parents.Count == 0)
        {
            logger.LogWarning("[EXT-DELETE] Refused {Kind} {Id}: filed to no parent this plane authorizes", kind, id);
            return DenyDeleteOwn();
        }

        foreach (var parent in target.Parents)
        {
            var (rights, fault) = await RightsForDocumentParentAsync(caller, parent.Kind, parent.Id, dataService, modules, ct);
            if (fault is not null)
                return fault;
            if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Read))
            {
                logger.LogWarning("[EXT-DELETE] Refused {Kind} {Id}: no Read on its {ParentKind} {ParentId}",
                    kind, id, parent.Kind, parent.Id);
                return DenyDeleteOwn();
            }
        }

        return null;
    }

    private static IResult DenyDeleteOwn() =>
        Results.Problem(statusCode: 403, title: "Forbidden",
            detail: "You can only delete a record you created, on a record you can still open.",
            extensions: new Dictionary<string, object?> { ["reasonCode"] = DeleteOwnDeniedReasonCode });

    private static IResult DeleteOwnUnavailable() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Delete unavailable",
            detail: "The record could not be checked or deleted right now, and nothing was deleted. Try again shortly.",
            extensions: new Dictionary<string, object?> { ["reasonCode"] = DeleteOwnUnavailableReasonCode });

    /// <summary>
    /// DELETE /api/v1/external/documents/{id} — hard-delete a document the caller created, and its SPE file.
    /// </summary>
    /// <remarks>
    /// <para>Authorized by <see cref="AuthorizeDeleteOwnAsync"/>, then deleted through the internal plane's delete,
    /// <see cref="DocumentCheckoutService.DeleteAsync(Guid, string, DocumentFileDeletePolicy, CancellationToken)"/>
    /// (reused, not forked): the checkout lock is honoured, then the file goes app-only through <see cref="SpeFileStore"/>
    /// (<c>SpeContainerOwnershipGuard</c>), then the row.</para>
    /// <para><b>Order and partial failure</b> (<see cref="DocumentFileDeletePolicy.VerifiedFileFirstKeepRowOnFailure"/>,
    /// ADR-007): the row's pointer is verified, the FILE is deleted first, and only then the row. If the file cannot be
    /// deleted the row is kept and this answers 409 (pointer refused) or 503 (try again). If the file went but the row
    /// could not be deleted, this answers 500: the row then points at a deleted file. A retry here is refused by the
    /// pointer check (the item no longer exists), so only a staff delete in the model-driven app clears it; the task 032
    /// notes record this known limit. A file that was already gone before the delete counts as deleted.</para>
    /// </remarks>
    private static async Task<IResult> DeleteOwnDocument(
        Guid id,
        HttpContext httpContext,
        ExternalDataService dataService,
        ExternalModuleRegistry modules,
        DocumentCheckoutService documents,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (await AuthorizeDeleteOwnAsync(callerContext, ExternalDataService.DeletableKind.Document, id, dataService,
                modules, logger, ct) is { } refusal)
            return refusal;

        DeleteResult result;
        try
        {
            result = await documents.DeleteAsync(
                id, httpContext.TraceIdentifier, DocumentFileDeletePolicy.VerifiedFileFirstKeepRowOnFailure, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // DeleteAsync throws only from its token and document read, before anything is deleted.
            logger.LogError(ex, "[EXT-DELETE] Document {DocumentId} could not be read for the delete", id);
            return DeleteOwnUnavailable();
        }

        switch (result)
        {
            case SuccessDeleteResult:
            case NotFoundDeleteResult: // gone between the check and the delete: the end state the caller asked for
                logger.LogInformation("[EXT-DELETE] Document {DocumentId} and its file deleted by its creator", id);
                return Results.NoContent();

            case CheckedOutDeleteResult:
                // The lock holder's name is not shown on the external plane.
                return Results.Problem(statusCode: 409, title: "Document checked out",
                    detail: "This document is being edited, so it cannot be deleted now. Nothing was deleted.",
                    extensions: new Dictionary<string, object?> { ["code"] = "document_locked" });

            case FileNotDeletedDeleteResult notDeleted:
                return Results.Problem(statusCode: notDeleted.FailureStatusCode, title: "Document not deleted",
                    detail: "The document's file could not be deleted, so the document was kept. Nothing was deleted.",
                    extensions: new Dictionary<string, object?> { ["code"] = notDeleted.Code });

            default:
                logger.LogError(
                    "[EXT-DELETE] Document {DocumentId}: the file was deleted but the row was not ({Result})",
                    id, result.GetType().Name);
                return Results.Problem(statusCode: 500, title: "Document not fully deleted",
                    detail: "The document's file was deleted, but its record could not be removed. Ask the record's "
                            + "owner to remove it.",
                    extensions: new Dictionary<string, object?> { ["code"] = "document_row_not_deleted" });
        }
    }

    /// <summary>DELETE /api/v1/external/todos/{id} — hard-delete a to-do the caller created (FR-26).</summary>
    private static async Task<IResult> DeleteOwnTodo(
        Guid id, HttpContext httpContext, ExternalDataService dataService, ExternalModuleRegistry modules,
        ILogger<Program> logger, CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (await AuthorizeDeleteOwnAsync(callerContext, ExternalDataService.DeletableKind.Todo, id, dataService,
                modules, logger, ct) is { } refusal)
            return refusal;

        return await DeleteAuthorizedRowAsync(ExternalDataService.DeletableKind.Todo, id, dataService, logger, ct);
    }

    /// <summary>DELETE /api/v1/external/events/{id} — hard-delete a calendar event the caller created (FR-26).</summary>
    private static async Task<IResult> DeleteOwnEvent(
        Guid id, HttpContext httpContext, ExternalDataService dataService, ExternalModuleRegistry modules,
        ILogger<Program> logger, CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (await AuthorizeDeleteOwnAsync(callerContext, ExternalDataService.DeletableKind.Event, id, dataService,
                modules, logger, ct) is { } refusal)
            return refusal;

        return await DeleteAuthorizedRowAsync(ExternalDataService.DeletableKind.Event, id, dataService, logger, ct);
    }

    /// <summary>
    /// The to-do and event row delete, app-only. Call ONLY after <see cref="AuthorizeDeleteOwnAsync"/> returned
    /// <see langword="null"/>.
    /// </summary>
    private static async Task<IResult> DeleteAuthorizedRowAsync(
        ExternalDataService.DeletableKind kind,
        Guid id,
        ExternalDataService dataService,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            // A row gone between the check and the delete (false) is the end state the caller asked for.
            await dataService.DeleteRecordAsync(kind, id, ct);
        }
        catch (ExternalDataService.DataverseUnavailableException)
        {
            return DeleteOwnUnavailable();
        }

        logger.LogInformation("[EXT-DELETE] {Kind} {Id} deleted by its creator", kind, id);
        return Results.NoContent();
    }

    /// <summary>
    /// Tasks 098 / 106: an event or to-do <c>sprk_duedate</c> (Dataverse Date Only) must be a calendar date
    /// (<c>yyyy-MM-dd</c>, or a timestamp from an earlier SPA build, read as its leading ten characters by
    /// <see cref="Spaarke.Dataverse.DataverseDateOnly"/>); anything else is a 400 here rather than Dataverse's 400 behind
    /// a 500. Null (not sent) passes.
    /// </summary>
    private static IResult? DueDateProblem(string? dueDate) =>
        dueDate is not null && !Spaarke.Dataverse.DataverseDateOnly.TryParse(dueDate, out _)
            ? Results.Problem(statusCode: 400, title: "Bad Request", detail: "sprk_duedate must be a calendar date (yyyy-MM-dd).")
            : null;

    // =========================================================================
    // Helpers
    // =========================================================================

    // Principal-agnostic caller (teams-app-r1 task 025): the CallerPrincipalAuthorizationFilter
    // (group-level) resolves EITHER a CIAM external contact OR a workforce user to a CallerPrincipal.
    // CallerPrincipal exposes the same record-scope surface the handlers use — HasProjectAccess,
    // GetAccessibleProjectIds, GetEffectiveRights — so every handler is plane-agnostic without change.
    // Since task 136 the first two require Read as well, so the mutating routes' presence check means
    // "holds Read" before they test Create.
    private static CallerPrincipal? GetCallerPrincipal(HttpContext httpContext) =>
        httpContext.Items[CallerPrincipal.HttpContextItemsKey] as CallerPrincipal;

    private static IResult MissingContextResult() =>
        Results.Problem(
            statusCode: 500,
            title: "Internal Server Error",
            detail: "Authentication context not available — ensure AddCallerPrincipalAuthorizationFilter is applied");
}
