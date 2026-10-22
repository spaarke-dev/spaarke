using System.Text.Json;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;

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
///   GET  /matters/{id}/events            — CALENDAR events for a matter (task 039; read only)
///   GET  /workassignments/{id}/events    — CALENDAR events for a work assignment (task 039; read only)
///   GET  /projects/{id}/contacts         — contacts with access to the project
///   GET  /projects/{id}/organizations    — organizations linked to project contacts
///   GET  /matters/{id}/todos             — to-dos for a matter (sprk_todo, regarding=matter)
///   POST /matters/{id}/todos             — create a new to-do regarding the matter
///   GET  /workassignments/{id}/todos     — to-dos for a work assignment
///   POST /workassignments/{id}/todos     — create a new to-do regarding the work assignment
///   PATCH /todos/{id}                    — update a to-do (scoped to its own accessible root)
///   GET  /{matters|workassignments|servicerequests|invoices}/{id}/documents                          — documents (task 031)
///   GET  /{matters|workassignments|servicerequests|invoices}/{id}/documents/{documentId}/content     — download
///   POST /{matters|workassignments|servicerequests|invoices}/{id}/documents                          — upload
///   GET  /{matters|workassignments|servicerequests|invoices}/{id}/documents/{documentId}/versions    — version history
///   DELETE /{documents|todos|events}/{id} — delete a record the caller created, with Read on its parent (task 032)
///
/// DOCUMENTS ON EVERY CORE RECORD (task 031 / FR-24): one list, download, versions and upload handler,
/// generalized over the document parent (ExternalDataService.DocumentParentKind). Project, matter and
/// work assignment rights come from RightsForRoot; a service request's from the submitter scope (the
/// Service Requests module's accessible set); an invoice's are inherited from its matter and project
/// (owner decision 2026-10-11, option A — the union, RightsForInvoiceAsync).
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
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadProjects)
            .WithSummary("List all Secure Projects the authenticated user can access")
            .Produces<ExternalCollectionResponse<ExternalProjectDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET /api/v1/external/projects/{id} — single project
        group.MapGet("/projects/{id:guid}", GetProjectById)
            .WithName("GetExternalProjectById")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadProjects)
            .WithSummary("Get a single Secure Project by ID")
            .Produces<ExternalProjectDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /api/v1/external/projects/{id}/documents
        group.MapGet("/projects/{id:guid}/documents", GetDocuments)
            .WithName("GetExternalProjectDocuments")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadProjectDocuments)
            .WithSummary("Get documents for a Secure Project")
            .Produces<ExternalDocumentListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET /api/v1/external/projects/{id}/documents/{documentId}/content — download document bytes.
        // Authz-before-stream (broker-only, app-only): Read on the project (task 136) + document->project
        // scoping are enforced BEFORE any SPE pointer resolution or Graph read (ADR-028 A1, NFR-03).
        group.MapGet("/projects/{id:guid}/documents/{documentId:guid}/content", DownloadDocumentContent)
            .WithName("DownloadExternalProjectDocument")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadProjectDocuments)
            .WithSummary("Download a Secure Project document's content (authz-before-stream, app-only)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // GET /api/v1/external/projects/{id}/todos
        group.MapGet("/projects/{id:guid}/todos", GetTodos)
            .WithName("GetExternalProjectTodos")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadProjectTodos)
            .WithSummary("Get to-dos for a Secure Project (sprk_todo records regarding the project)")
            .Produces<ExternalCollectionResponse<ExternalTodoDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // POST /api/v1/external/projects/{id}/todos — create a new to-do regarding the project
        group.MapPost("/projects/{id:guid}/todos", CreateTodo)
            .WithName("CreateExternalProjectTodo")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.CreateProjectTodo)
            .WithSummary("Create a new sprk_todo regarding a Secure Project (ADR-024 resolver fields applied)")
            .Produces<ExternalTodoDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict) // task 146: no owner resolvable from the root
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable); // task 036 fix round 4: Dataverse gave no answer

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
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadMatterTodos)
            .WithSummary("Get to-dos regarding an accessible matter (sprk_regardingmatter)")
            .Produces<ExternalCollectionResponse<ExternalTodoDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/matters/{id:guid}/todos", CreateMatterTodo)
            .WithName("CreateExternalMatterTodo")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.CreateMatterTodo)
            .WithSummary("Create a new sprk_todo regarding a matter (ADR-024 resolver fields applied)")
            .Produces<ExternalTodoDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict) // task 146
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable); // task 036 fix round 4: Dataverse gave no answer

        group.MapGet("/workassignments/{id:guid}/todos", GetWorkAssignmentTodos)
            .WithName("GetExternalWorkAssignmentTodos")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadWorkAssignmentTodos)
            .WithSummary("Get to-dos regarding an accessible work assignment (sprk_regardingworkassignment)")
            .Produces<ExternalCollectionResponse<ExternalTodoDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/workassignments/{id:guid}/todos", CreateWorkAssignmentTodo)
            .WithName("CreateExternalWorkAssignmentTodo")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.CreateWorkAssignmentTodo)
            .WithSummary("Create a new sprk_todo regarding a work assignment (ADR-024 resolver fields applied)")
            .Produces<ExternalTodoDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict) // task 146
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable); // task 036 fix round 4: Dataverse gave no answer

        // POST /api/v1/external/projects/{id}/documents — upload a file and create its document row.
        // Container is SERVER-DERIVED from the project (never client-named) and the upload uses
        // ConflictBehavior.Fail so a same-named file can never be overwritten by an external caller.
        group.MapPost("/projects/{id:guid}/documents", UploadDocument)
            .WithName("UploadExternalProjectDocument")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.UploadProjectDocument)
            .WithSummary("Upload a document to a Secure Project (server-derived container, app-only)")
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery() // multipart upload; auth is the JWT + participation gate, not a form token
            .Produces<ExternalDocumentDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // GET /api/v1/external/projects/{id}/documents/{documentId}/versions — SPE version history
        group.MapGet("/projects/{id:guid}/documents/{documentId:guid}/versions", GetDocumentVersions)
            .WithName("GetExternalDocumentVersions")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadProjectDocuments)
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
        // routes follow below (owner option A, 2026-10-11). The templates are literal, one registration
        // per route, so the route-authorization ArchTest reads each one.
        // ---------------------------------------------------------------------------------------

        group.MapGet("/matters/{id:guid}/documents", GetMatterDocuments)
            .WithName("GetExternalMatterDocuments")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadMatterDocuments)
            .WithSummary("Get documents for a matter the caller can read")
            .Produces<ExternalDocumentListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/matters/{id:guid}/documents/{documentId:guid}/content", DownloadMatterDocumentContent)
            .WithName("DownloadExternalMatterDocument")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadMatterDocuments)
            .WithSummary("Download a matter document's content (authz-before-stream, app-only)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/matters/{id:guid}/documents", UploadMatterDocument)
            .WithName("UploadExternalMatterDocument")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.UploadMatterDocument)
            .WithSummary("Upload a document to a matter (server-derived container, app-only)")
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery() // multipart upload; auth is the JWT + Tier-2 gate, not a form token
            .Produces<ExternalDocumentDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/matters/{id:guid}/documents/{documentId:guid}/versions", GetMatterDocumentVersions)
            .WithName("GetExternalMatterDocumentVersions")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadMatterDocuments)
            .WithSummary("Get SPE version history for a document on a matter")
            .Produces<ExternalDocumentVersionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/workassignments/{id:guid}/documents", GetWorkAssignmentDocuments)
            .WithName("GetExternalWorkAssignmentDocuments")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadWorkAssignmentDocuments)
            .WithSummary("Get documents for a work assignment the caller can read")
            .Produces<ExternalDocumentListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/workassignments/{id:guid}/documents/{documentId:guid}/content", DownloadWorkAssignmentDocumentContent)
            .WithName("DownloadExternalWorkAssignmentDocument")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadWorkAssignmentDocuments)
            .WithSummary("Download a work assignment document's content (authz-before-stream, app-only)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/workassignments/{id:guid}/documents", UploadWorkAssignmentDocument)
            .WithName("UploadExternalWorkAssignmentDocument")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.UploadWorkAssignmentDocument)
            .WithSummary("Upload a document to a work assignment (server-derived container, app-only)")
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery() // multipart upload; auth is the JWT + Tier-2 gate, not a form token
            .Produces<ExternalDocumentDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/workassignments/{id:guid}/documents/{documentId:guid}/versions", GetWorkAssignmentDocumentVersions)
            .WithName("GetExternalWorkAssignmentDocumentVersions")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadWorkAssignmentDocuments)
            .WithSummary("Get SPE version history for a document on a work assignment")
            .Produces<ExternalDocumentVersionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/servicerequests/{id:guid}/documents", GetServiceRequestDocuments)
            .WithName("GetExternalServiceRequestDocuments")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadServiceRequestDocuments)
            .WithSummary("Get documents for a service request the caller submitted")
            .Produces<ExternalDocumentListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/servicerequests/{id:guid}/documents/{documentId:guid}/content", DownloadServiceRequestDocumentContent)
            .WithName("DownloadExternalServiceRequestDocument")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadServiceRequestDocuments)
            .WithSummary("Download a service request document's content (authz-before-stream, app-only)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/servicerequests/{id:guid}/documents", UploadServiceRequestDocument)
            .WithName("UploadExternalServiceRequestDocument")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.UploadServiceRequestDocument)
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
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadServiceRequestDocuments)
            .WithSummary("Get SPE version history for a document on a service request the caller submitted")
            .Produces<ExternalDocumentVersionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Invoice documents (task 031, owner decision 2026-10-11, option A "inherit"): the same four handlers. An invoice
        // is not a root, so its rights are the caller's rights on its PARENT records — the union over the invoice's
        // sprk_matter and sprk_project (RightsForInvoiceAsync). Read lists, downloads and reads versions; Create uploads.
        group.MapGet("/invoices/{id:guid}/documents", GetInvoiceDocuments)
            .WithName("GetExternalInvoiceDocuments")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadInvoiceDocuments)
            .WithSummary("Get documents for an invoice whose matter or project the caller can read")
            .Produces<ExternalDocumentListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/invoices/{id:guid}/documents/{documentId:guid}/content", DownloadInvoiceDocumentContent)
            .WithName("DownloadExternalInvoiceDocument")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadInvoiceDocuments)
            .WithSummary("Download an invoice document's content (authz-before-stream, app-only)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/invoices/{id:guid}/documents", UploadInvoiceDocument)
            .WithName("UploadExternalInvoiceDocument")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.UploadInvoiceDocument)
            .WithSummary("Upload a document to an invoice (Create inherited from its matter or project; server-derived container, app-only)")
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery() // multipart upload; auth is the JWT + Tier-2 gate, not a form token
            .Produces<ExternalDocumentDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/invoices/{id:guid}/documents/{documentId:guid}/versions", GetInvoiceDocumentVersions)
            .WithName("GetExternalInvoiceDocumentVersions")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadInvoiceDocuments)
            .WithSummary("Get SPE version history for a document on an invoice")
            .Produces<ExternalDocumentVersionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // GET /api/v1/external/projects/{id}/events — calendar events for the project
        group.MapGet("/projects/{id:guid}/events", GetEvents)
            .WithName("GetExternalProjectEvents")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadProjectEvents)
            .WithSummary("Get calendar events for a Secure Project (sprk_event records regarding the project)")
            .Produces<ExternalCollectionResponse<ExternalEventDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Task 039 (FR-03): the same read for the other two roots that have events. The handler is ListEventsForRoot, the
        // project route's own, so the Read check and the DTO are identical. Read only: event CREATE stays project-only.
        group.MapGet("/matters/{id:guid}/events", GetMatterEvents)
            .WithName("GetExternalMatterEvents")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadMatterEvents)
            .WithSummary("Get calendar events regarding an accessible matter (sprk_event, sprk_regardingmatter)")
            .Produces<ExternalCollectionResponse<ExternalEventDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/workassignments/{id:guid}/events", GetWorkAssignmentEvents)
            .WithName("GetExternalWorkAssignmentEvents")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadWorkAssignmentEvents)
            .WithSummary("Get calendar events regarding an accessible work assignment (sprk_event, sprk_regardingworkassignment)")
            .Produces<ExternalCollectionResponse<ExternalEventDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // POST /api/v1/external/projects/{id}/events — create a calendar event on the project
        group.MapPost("/projects/{id:guid}/events", CreateEvent)
            .WithName("CreateExternalProjectEvent")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.CreateProjectEvent)
            .WithSummary("Create a new sprk_event regarding a Secure Project")
            .Produces<ExternalEventDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict) // task 146
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable); // task 036 fix round 4: Dataverse gave no answer

        // GET /api/v1/external/projects/{id}/contacts
        group.MapGet("/projects/{id:guid}/contacts", GetContacts)
            .WithName("GetExternalProjectContacts")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadProjectContacts)
            .WithSummary("Get contacts with access to a Secure Project")
            .Produces<ExternalCollectionResponse<ExternalContactDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET /api/v1/external/projects/{id}/organizations
        group.MapGet("/projects/{id:guid}/organizations", GetOrganizations)
            .WithName("GetExternalProjectOrganizations")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadProjectOrganizations)
            .WithSummary("Get organizations linked to contacts on a Secure Project")
            .Produces<ExternalCollectionResponse<ExternalOrganizationDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // PATCH /api/v1/external/todos/{id} — update a to-do
        group.MapPatch("/todos/{id:guid}", UpdateTodo)
            .WithName("UpdateExternalTodo")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.UpdateTodo)
            .WithSummary("Update a to-do (PATCH semantics — only provided fields are changed)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // ---------------------------------------------------------------------------------------
        // Delete-own (spaarke-SPA-external-access-platform-r3 task 032, FR-26) — the first DELETE routes on this plane.
        //
        // Hard delete of a document, to-do or event the CALLER created (the task-030 stamp), while the caller still holds
        // Read on every parent this plane authorizes (the modelled parent lookups; other lookups on the row are not
        // consulted). One check decides all three (AuthorizeDeleteOwnAsync); no access level
        // and no Dataverse role lets a caller delete someone else's row here. General delete stays a Dataverse role in the
        // model-driven app. There is no DELETE for messages (owner 2026-10-10: messages are not deletable). The service
        // request delete (owner 2026-10-11, option A) is the fourth route below.
        // ---------------------------------------------------------------------------------------

        group.MapDelete("/documents/{id:guid}", DeleteOwnDocument)
            .WithName("DeleteOwnExternalDocument")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.DeleteDocument)
            .WithSummary("Delete a document the caller created, and its file (creator stamp + Read on its parent)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapDelete("/todos/{id:guid}", DeleteOwnTodo)
            .WithName("DeleteOwnExternalTodo")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.DeleteTodo)
            .WithSummary("Delete a to-do the caller created (creator stamp + Read on its root)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapDelete("/events/{id:guid}", DeleteOwnEvent)
            .WithName("DeleteOwnExternalEvent")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.DeleteEvent)
            .WithSummary("Delete a calendar event the caller created (creator stamp + Read on its root)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Owner decision 2026-10-11 (task 032 follow-up, FR-26), option A: the REQUESTER may delete a service request that has
        // no related row at all; any related row (documents, messages, threads, events, to-dos, memos, analyses, signals)
        // is a 409 has_related_records, and the requester asks legal to recall it by posting on the request's thread
        // (POST /servicerequests/{id}/messages, task 034). Hard delete.
        group.MapDelete("/servicerequests/{id:guid}", DeleteOwnServiceRequest)
            .WithName("DeleteOwnExternalServiceRequest")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.DeleteServiceRequest)
            .WithSummary("Delete a service request the caller requested, while it is open and has no related records (else 409 service_request_closed or has_related_records)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
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
        CallerRecordAccessProbe probe,
        ILogger logger,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        // ADR-003: an inaccessible parent DENIES, never an empty list (task 136).
        // Task 036 fix round 1 (F4): the rights are confirmed against Dataverse exactly as the upload route confirms them, so
        // `canCreate` is the upload route's own answer for a caller whose access Dataverse decided (one probe for the record
        // in hand, plus the sprk_document Create privilege when Dataverse answers AppendTo). Read is unchanged: Dataverse
        // only widens a parent that already holds Read. Every other caller is asked nothing (WithDataverseRightsAsync).
        // An unanswered Dataverse question offers no upload (canCreate: false, a UI affordance only; fix round 4, F1).
        var (rights, upstreamFault, _) = await RightsForDocumentParentAsync(
            callerContext, parentKind, parentId, dataService, modules, ct, CallerRightsReader(probe, httpContext), logger,
            CallerPrivilegeReader(probe, httpContext));
        if (upstreamFault is not null) return upstreamFault;
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Read))
            return DenyDocumentParent(parentKind);

        var documents = await dataService.GetDocumentsAsync(parentKind, parentId, callerContext, ct);
        return Results.Ok(new ExternalDocumentListResponse
        {
            Value = documents.Value,
            Truncated = documents.Truncated,
            // The upload route's own Create check, on the rights just used for Read (task 054 review: a ViewOnly reader
            // must not be offered an upload that can only fail).
            CanCreate = rights.HasFlag(Spaarke.Dataverse.AccessRights.Create),
        });
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
        var (rights, upstreamFault, _) = await RightsForDocumentParentAsync(callerContext, parentKind, id, dataService, modules, ct);
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
        //     Fix round for task 039 (ExternalMultiRootRule, owner decision 2026-10-12, option B): a document also filed to
        //     a SECURE root the caller cannot read (or one whose secure state cannot be read) is withheld exactly as one not
        //     under this parent, with the SAME response, so the answer cannot be used to tell whether such a document
        //     exists. Without it a reader of project P who knew the id of a document filed to P and to secure matter M
        //     downloaded it through P's route.
        var document = await dataService.GetDocumentParentAndNameAsync(parentKind, documentId, ct);
        var documentName = document.DocumentName;
        if (!await IsDocumentReadableUnderAsync(document, documentId, id, callerContext, dataService, ct))
        {
            logger.LogWarning(
                "[EXT-DOWNLOAD] Contact {ContactId} denied — document {DocumentId} not in {ParentKind} {ParentId}, also filed to a secure record the caller cannot read, or hidden by the document narrowing",
                callerContext.ContactId, documentId, parentKind, id);
            return DenyDocumentNotUnderParent(parentKind);
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
        CallerRecordAccessProbe probe,
        ILogger logger,
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

        var todos = await dataService.GetTodosAsync(rootKind, rootId, callerContext, ct);
        if (!callerContext.DataverseConfirmsWrites)
            return Results.Ok(todos);

        // Task 036 fix round 1 (F4): a caller whose access Dataverse decided is told whether the to-do create route would
        // admit them on THIS root (its own answer: AppendTo on the root + prvCreatesprk_Todo), because their /me level only
        // says what they may see. One probe for the record in hand; every other caller's response is unchanged.
        var createRights = await RightsForRootWriteAsync(
            callerContext, rootKind, rootId, CallerRightsReader(probe, httpContext), logger, ct,
            TodoSourceAccessFilter.CreateTodoPrivilege, CallerPrivilegeReader(probe, httpContext));
        return Results.Ok(new ExternalCollectionResponse<ExternalTodoDto>
        {
            Value = todos.Value,
            Truncated = todos.Truncated,
            // An unanswered Dataverse question offers no create (a UI affordance only; fix round 4, F1).
            CanCreate = createRights.Rights.HasFlag(Spaarke.Dataverse.AccessRights.Create),
        });
    }

    private static async Task<IResult> CreateTodoForRoot(
        ExternalDataService.TodoRootKind rootKind,
        Guid rootId,
        CreateExternalTodoRequest request,
        HttpContext httpContext,
        ExternalDataService dataService,
        IRecordOwnershipResolver ownership,
        CallerRecordAccessProbe probe,
        ILogger logger,
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
        // Task 036 (FR-20): a caller whose access Dataverse decided is confirmed against Dataverse on the root (AppendTo) AND,
        // since fix round 1 (F1), on their own Create privilege on sprk_todo — the to-do is written app-only.
        // Fix round 4 (F1): Create missing only because Dataverse gave no answer is a retry-able 503, not a denial.
        var confirmed = await RightsForRootWriteAsync(
            callerContext, rootKind, rootId, CallerRightsReader(probe, httpContext), logger, ct,
            TodoSourceAccessFilter.CreateTodoPrivilege, CallerPrivilegeReader(probe, httpContext));
        if (confirmed.IsUnavailableFor(Spaarke.Dataverse.AccessRights.Create))
            return DataverseRightsUnavailable();
        if (!confirmed.Rights.HasFlag(Spaarke.Dataverse.AccessRights.Create))
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
    /// FR-20 (unified-access-control-r2 task 036, owner round 59 item 2): the ONE Dataverse confirmation of a write on this
    /// surface. For a caller whose own access Dataverse decided (<see cref="CallerPrincipal.DataverseConfirmsWrites"/>), the
    /// rights a write route checks are <paramref name="composed"/> widened by what Dataverse itself grants the caller on the
    /// target record, asked app-only naming the caller's resolved systemuser (<see cref="CallerRightsReader"/>; never OBO on
    /// this plane, task 036 option C, owner 2026-10-20). For every other caller it returns
    /// <paramref name="composed"/> unchanged and asks nothing, so their routes decide exactly as before.
    /// </summary>
    /// <remarks>
    /// <para><b>Why widen, not replace.</b> With the flag on, the caller's own term is Read only (the impersonated set
    /// confers Read). <paramref name="composed"/> can carry more only through the contact-grants union (a systemuser who is
    /// also a granted contact keeps those grants, B-17). So the write is allowed when the grants allow it OR Dataverse does,
    /// and never through a constant: no membership term reaches a flag-on composition.</para>
    /// <para><b>Read first.</b> Nothing is asked unless <paramref name="composed"/> holds Read: a record outside the caller's
    /// accessible set, or removed by the deny list or the Restricted veto, is refused by the route without a Dataverse
    /// call, so the vetoes stay in force.</para>
    /// <para><b>Fail closed (NFR-01), and a fault is not a denial</b> (fix round 4, F1). Dataverse's "no access" answer adds
    /// None, and the route refuses the write (403) unless the grants alone allow it. When Dataverse gives NO answer — the
    /// reader answers <see langword="null"/> (a timeout, a throttle or 5xx, an answer that is not about the user's access,
    /// no app-only client) or throws — it also adds None, and the rights it could not confirm are returned as
    /// <see cref="ConfirmedRights.Unanswered"/>: a gate that then lacks the right it needs answers a retry-able 503
    /// (<see cref="ConfirmedRights.IsUnavailableFor"/>) and writes nothing, rather than telling the user they have no
    /// access. No reader at all (a wiring defect) adds None as an answer. There is no fallback to membership, an app-only
    /// read or a constant. The caller's own cancellation is rethrown.</para>
    /// <para><b>A child create needs the child table's Create privilege too</b> (task 036 fix round 1, F1). The BFF writes
    /// the child app-only, so Dataverse never checks the caller's own Create privilege on the CHILD table. Dataverse adds
    /// <see cref="Spaarke.Dataverse.AccessRights.Create"/> here only when it answers AppendTo on the parent AND the caller
    /// holds <paramref name="childCreatePrivilege"/> (<see cref="CallerRecordAccessProbe.PrincipalHoldsPrivilegeAppOnlyAsync"/>,
    /// app-only naming the systemuser, uncached — the rule of the task 166 / G5 pairing of <c>TodoSourceAccessFilter</c> and
    /// <c>CommunicationRecordAuthorizationFilter</c>). The privilege is asked only when it can change the answer: Dataverse
    /// answered AppendTo and <paramref name="composed"/> does not already carry Create. No privilege named (a route that
    /// creates nothing — not a refusal, so nothing is logged), no reader or a false answer: Dataverse adds no Create
    /// (NFR-01). An unanswered privilege (<see langword="null"/> or a throw) adds no Create either, and marks Create
    /// <see cref="ConfirmedRights.Unanswered"/> (fix round 4, F1). Read, Write and Delete are unaffected, so the PATCH and
    /// delete-own confirmations (which name no child table) are unchanged.</para>
    /// <para>One Dataverse round trip per confirmed write (NFR-02), for the single record in hand; a child create that
    /// Dataverse answers with AppendTo adds the privilege read (<c>RetrieveUserSetOfPrivilegesByNames</c> on the
    /// systemuser).</para>
    /// </remarks>
    /// <param name="caller">The resolved principal.</param>
    /// <param name="composed">The rights the route computed from the principal.</param>
    /// <param name="entitySet">The target record's entity set (plural), as the probe needs it.</param>
    /// <param name="recordId">The target record.</param>
    /// <param name="readCallerRights">Asks Dataverse for the caller's rights on (entity set, id): the probe app-only, naming
    /// the caller's resolved systemuser (<see cref="CallerRightsReader"/>).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="ct">Cancellation.</param>
    /// <param name="childCreatePrivilege">The child table's Create privilege, by its live name (for example
    /// <c>prvCreatesprk_Todo</c>), on a route that creates a child under <paramref name="recordId"/>; <c>null</c> on a route
    /// that creates nothing (Dataverse then adds no Create).</param>
    /// <param name="readCallerPrivilege">Asks Dataverse whether the caller holds a named privilege
    /// (<see cref="CallerPrivilegeReader"/>).</param>
    internal static async Task<ConfirmedRights> WithDataverseRightsAsync(
        CallerPrincipal caller,
        Spaarke.Dataverse.AccessRights composed,
        string entitySet,
        Guid recordId,
        Func<string, Guid, CancellationToken, Task<Spaarke.Dataverse.AccessRights?>>? readCallerRights,
        ILogger logger,
        CancellationToken ct,
        string? childCreatePrivilege = null,
        Func<string, CancellationToken, Task<bool?>>? readCallerPrivilege = null)
    {
        if (!caller.DataverseConfirmsWrites || !composed.HasFlag(Spaarke.Dataverse.AccessRights.Read))
            return new ConfirmedRights(composed, Spaarke.Dataverse.AccessRights.None);

        var dataverse = Spaarke.Dataverse.AccessRights.None;
        var unanswered = Spaarke.Dataverse.AccessRights.None;
        if (readCallerRights is null)
        {
            logger.LogError(
                "[WF-AUTHZ] No Dataverse rights reader for systemuser {SystemUserId} on {EntitySet} {RecordId}: the write is " +
                "confirmed by nothing and adds no rights (fail closed, task 036).",
                caller.SystemUserId, entitySet, recordId);
        }
        else
        {
            Spaarke.Dataverse.AccessRights? answer;
            try
            {
                answer = await readCallerRights(entitySet, recordId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                logger.LogError(ex,
                    "[WF-AUTHZ] Dataverse's rights for systemuser {SystemUserId} on {EntitySet} {RecordId} could not be read: " +
                    "the write adds no rights and is unavailable, not refused (NFR-01, task 036 fix round 4, F1).",
                    caller.SystemUserId, entitySet, recordId);
                answer = null;
            }

            if (answer is { } rights)
            {
                dataverse = rights;
            }
            else
            {
                // Fix round 4 (F1): no answer is not "no access". Nothing is granted, and the rights Dataverse could have
                // added are marked unanswered, so a gate that lacks one answers "try again" (503), not a denial.
                unanswered = SurfaceWriteRights;
                logger.LogWarning(
                    "[WF-AUTHZ] Dataverse gave no answer for systemuser {SystemUserId} on {EntitySet} {RecordId} (a timeout, " +
                    "a fault, or an answer not about the user): the write adds no rights and is unavailable, not refused " +
                    "(task 036 fix round 4, F1).",
                    caller.SystemUserId, entitySet, recordId);
            }
        }

        var surface = SurfaceRightsFromDataverse(dataverse);
        if (surface.HasFlag(Spaarke.Dataverse.AccessRights.Create) && !composed.HasFlag(Spaarke.Dataverse.AccessRights.Create))
        {
            // A route that creates nothing (PATCH to-do, delete-own) names no child privilege: Dataverse's AppendTo simply
            // adds no Create there, and that is not a refusal worth a warning (fix round 2, F2).
            if (childCreatePrivilege is null)
            {
                surface &= ~Spaarke.Dataverse.AccessRights.Create;
            }
            else
            {
                var holds = await CallerHoldsChildCreateAsync(caller, childCreatePrivilege, readCallerPrivilege, logger, ct)
                    .ConfigureAwait(false);
                if (holds != true)
                    surface &= ~Spaarke.Dataverse.AccessRights.Create;
                if (holds is null)
                    unanswered |= Spaarke.Dataverse.AccessRights.Create;
            }
        }

        logger.LogInformation(
            "[WF-AUTHZ] Dataverse-confirmed write rights for systemuser {SystemUserId} on {EntitySet} {RecordId}: Dataverse " +
            "{Dataverse} -> {Surface} (composed {Composed}; unanswered {Unanswered}; answered via {Path}).",
            caller.SystemUserId, entitySet, recordId, dataverse, surface, composed, unanswered,
            CallerRecordAccessProbe.AppOnlyNamedPrincipalPath);
        return new ConfirmedRights(composed | surface, unanswered);
    }

    /// <summary>The rights a Dataverse answer can add on this surface (<see cref="SurfaceRightsFromDataverse"/>).</summary>
    private const Spaarke.Dataverse.AccessRights SurfaceWriteRights =
        Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Write
        | Spaarke.Dataverse.AccessRights.Delete | Spaarke.Dataverse.AccessRights.Create;

    /// <summary>
    /// <see cref="WithDataverseRightsAsync"/>'s answer (task 036 fix round 4, F1): the rights the write may use, and the
    /// rights Dataverse was asked about but gave no answer for.
    /// </summary>
    /// <param name="Rights">The composed rights widened by Dataverse's answer. Nothing unanswered is ever in here.</param>
    /// <param name="Unanswered">The rights Dataverse could have added but did not answer for (a timeout, a throttle or 5xx,
    /// an answer that is not about the user, no app-only client). Never a grant; only the reason a refusal is a 503.</param>
    internal readonly record struct ConfirmedRights(
        Spaarke.Dataverse.AccessRights Rights, Spaarke.Dataverse.AccessRights Unanswered)
    {
        /// <summary>
        /// Whether a gate that needs <paramref name="needed"/> must answer "try again" (a 503, nothing written) rather than
        /// "no access" (403): the right is missing, and only because Dataverse did not answer for it. A real denial, or a
        /// right the caller holds, is <c>false</c>.
        /// </summary>
        public bool IsUnavailableFor(Spaarke.Dataverse.AccessRights needed) =>
            !Rights.HasFlag(needed) && Unanswered.HasFlag(needed);
    }

    /// <summary>
    /// The reason code of a write refused because Dataverse could not confirm the caller's rights right now (task 036 fix
    /// round 4, F1): a timeout, a throttle or 5xx, or an answer that is not about the user. Retry-able; nothing was written.
    /// The PATCH and delete-own routes keep their own 503s (<see cref="TodoWriteUnavailableReasonCode"/>,
    /// <see cref="DeleteOwnUnavailableReasonCode"/>).
    /// </summary>
    internal const string DataverseUnavailableReasonCode = "sdap.access.dataverse_unavailable";

    /// <summary>
    /// The 503 of a create (to-do, event, document upload, message) whose rights Dataverse could not confirm right now
    /// (<see cref="ConfirmedRights.IsUnavailableFor"/>): the user is told to try again, never that they lack access.
    /// </summary>
    internal static IResult DataverseRightsUnavailable() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Access check unavailable",
            detail: "Your access could not be confirmed right now, and nothing was written. Try again shortly.",
            extensions: new Dictionary<string, object?> { ["reasonCode"] = DataverseUnavailableReasonCode });

    /// <summary>
    /// Whether the caller holds the child table's Create privilege (task 036 fix round 1, F1), for
    /// <see cref="WithDataverseRightsAsync"/>, on a route that names one. A blank name, no reader or a false answer is
    /// <c>false</c>; an unanswered read (<see langword="null"/> or a throw) is <see langword="null"/> (fix round 4, F1).
    /// Neither grants. Only the caller's own cancellation is rethrown.
    /// </summary>
    private static async Task<bool?> CallerHoldsChildCreateAsync(
        CallerPrincipal caller,
        string childCreatePrivilege,
        Func<string, CancellationToken, Task<bool?>>? readCallerPrivilege,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(childCreatePrivilege) || readCallerPrivilege is null)
        {
            logger.LogWarning(
                "[WF-AUTHZ] The child-table Create privilege {Privilege} cannot be confirmed for systemuser {SystemUserId} " +
                "(blank name or no privilege reader): Dataverse's AppendTo adds no Create (fail closed, task 036 F1).",
                childCreatePrivilege, caller.SystemUserId);
            return false;
        }

        bool? holds;
        try
        {
            holds = await readCallerPrivilege(childCreatePrivilege, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            logger.LogError(ex,
                "[WF-AUTHZ] Whether systemuser {SystemUserId} holds {Privilege} could not be read: Dataverse adds no Create, " +
                "and the create is unavailable, not refused (NFR-01, task 036 fix round 4, F1).",
                caller.SystemUserId, childCreatePrivilege);
            return null;
        }

        if (holds is null)
        {
            logger.LogWarning(
                "[WF-AUTHZ] Dataverse gave no answer whether systemuser {SystemUserId} holds {Privilege}: Dataverse adds no " +
                "Create, and the create is unavailable, not refused (task 036 fix round 4, F1).",
                caller.SystemUserId, childCreatePrivilege);
            return null;
        }

        logger.LogInformation(
            "[WF-AUTHZ] Systemuser {SystemUserId} holds {Privilege}: {Holds} (task 036 F1).",
            caller.SystemUserId, childCreatePrivilege, holds);
        return holds;
    }

    /// <summary>
    /// Dataverse's rights on a record, in this surface's vocabulary (task 036): Read, Write and Delete as Dataverse answers
    /// them; <see cref="Spaarke.Dataverse.AccessRights.Create"/> — "may add a child (a to-do, document, event or message)
    /// under this record" — from Dataverse's <see cref="Spaarke.Dataverse.AccessRights.AppendTo"/> on it.
    /// </summary>
    /// <remarks>
    /// AppendTo, not Dataverse's own CreateAccess, because RetrievePrincipalAccess's CreateAccess on an existing record
    /// speaks of the caller's create privilege on the record's TABLE, not of attaching to this record: a user who can never
    /// create a project but can file a to-do against one would be refused, and the reverse let through. Attaching to a
    /// record is AppendTo everywhere else in the BFF (<c>OperationAccessPolicy</c>: <c>entity.associate_document</c>,
    /// <c>event.attach_regarding</c>). Append, Share and Dataverse's CreateAccess carry nothing here.
    /// <para>The Create mapped here is then kept only when the caller also holds the child table's Create privilege
    /// (<see cref="WithDataverseRightsAsync"/>, task 036 fix round 1, F1): AppendTo says "may attach to this record", the
    /// privilege says "may create that kind of row", and Dataverse asks both of a user creating the child in MDA.</para>
    /// </remarks>
    internal static Spaarke.Dataverse.AccessRights SurfaceRightsFromDataverse(Spaarke.Dataverse.AccessRights dataverse) =>
        (dataverse & (Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Write
                      | Spaarke.Dataverse.AccessRights.Delete))
        | (dataverse.HasFlag(Spaarke.Dataverse.AccessRights.AppendTo)
            ? Spaarke.Dataverse.AccessRights.Create
            : Spaarke.Dataverse.AccessRights.None);

    /// <summary>
    /// The Dataverse rights reader for <see cref="WithDataverseRightsAsync"/>: the question asked APP-ONLY, naming the
    /// caller's resolved systemuser (<see cref="AppOnlyConfirmationPrincipal"/>,
    /// <see cref="CallerRecordAccessProbe.GetPrincipalRightsAppOnlyOrUnknownAsync"/>) — for every systemuser on this plane.
    /// <see langword="null"/> when Dataverse gave no answer (fix round 4, F1).
    /// </summary>
    /// <remarks>
    /// <para><b>No OBO on <c>/api/v1/external</c></b> (task 036 option C, owner 2026-10-20; ADR-028 A1–A3, A5). The
    /// caller's token is never exchanged here: the plane's read set and its write confirmation are both Dataverse's
    /// answer about the same resolved systemuser, asked app-only with <c>MSCRMCallerID</c> impersonation (ADR-028 A5's
    /// mechanism). The probe's OBO questions stay for the internal routes only.</para>
    /// <para>Enforced by: <c>tests/Spaarke.ArchTests/ExternalPlaneNoOboGuardTests.cs</c> (no token-taking probe call, OBO
    /// exchange or caller-token read in <c>Api/ExternalAccess/External*.cs</c> or in any filter or handler file the
    /// <c>/api/v1/external</c> mappings bind).</para>
    /// </remarks>
    internal static Func<string, Guid, CancellationToken, Task<Spaarke.Dataverse.AccessRights?>> CallerRightsReader(
        CallerRecordAccessProbe probe, HttpContext httpContext)
    {
        var systemUserId = AppOnlyConfirmationPrincipal(GetCallerPrincipal(httpContext));
        return (entitySet, recordId, ct) => probe.GetPrincipalRightsAppOnlyOrUnknownAsync(systemUserId, entitySet, recordId, ct);
    }

    /// <summary>
    /// The privilege reader for <see cref="WithDataverseRightsAsync"/> (task 036 fix round 1, F1): the privilege asked
    /// APP-ONLY, naming the caller's resolved systemuser
    /// (<see cref="CallerRecordAccessProbe.PrincipalHoldsPrivilegeAppOnlyOrUnknownAsync"/>) — for every systemuser on this
    /// plane, never OBO (task 036 option C; see <see cref="CallerRightsReader"/>). <see langword="null"/> when Dataverse gave
    /// no answer (fix round 4, F1).
    /// </summary>
    internal static Func<string, CancellationToken, Task<bool?>> CallerPrivilegeReader(
        CallerRecordAccessProbe probe, HttpContext httpContext)
    {
        var systemUserId = AppOnlyConfirmationPrincipal(GetCallerPrincipal(httpContext));
        return (privilege, ct) => probe.PrincipalHoldsPrivilegeAppOnlyOrUnknownAsync(systemUserId, privilege, ct);
    }

    /// <summary>
    /// The systemuser a write confirmation names app-only for <paramref name="caller"/>: the principal's resolved
    /// <see cref="CallerPrincipal.SystemUserId"/>, however the caller signed in (a Spaarke-tenant token or the WorkforceHome
    /// home link) — task 036 option C, owner 2026-10-20, widening decision (a) of 2026-10-18 from home-linked users to
    /// every systemuser. The id is the one the validated token was resolved to, never client input (ADR-028 A5's trust
    /// basis).
    /// </summary>
    /// <remarks>Fail closed: no principal, or a principal with no systemuser id (a contact, or a defaulted id), is
    /// <see cref="Guid.Empty"/>, which both app-only probe questions refuse with no Dataverse call (no rights). There is no
    /// other path to fall back to.</remarks>
    internal static Guid AppOnlyConfirmationPrincipal(CallerPrincipal? caller) =>
        caller?.SystemUserId ?? Guid.Empty;

    /// <summary>
    /// A root's rights for a write route: <see cref="RightsForRoot"/>, confirmed against Dataverse (task 036). A route that
    /// creates a child under the root names the child table's Create privilege (fix round 1, F1).
    /// </summary>
    private static Task<ConfirmedRights> RightsForRootWriteAsync(
        CallerPrincipal caller, ExternalDataService.TodoRootKind rootKind, Guid rootId,
        Func<string, Guid, CancellationToken, Task<Spaarke.Dataverse.AccessRights?>> readCallerRights,
        ILogger logger, CancellationToken ct,
        string? childCreatePrivilege = null,
        Func<string, CancellationToken, Task<bool?>>? readCallerPrivilege = null) =>
        WithDataverseRightsAsync(
            caller, RightsForRoot(caller, rootKind, rootId),
            ExternalDataService.TryGetRootBinding(rootKind)?.EntitySet ?? string.Empty, rootId, readCallerRights, logger, ct,
            childCreatePrivilege, readCallerPrivilege);

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
    /// <para><b>An invoice is not a root either</b> (owner decision 2026-10-11, option A): it INHERITS the caller's
    /// rights on its parent records — the union over its matter and project (<see cref="RightsForInvoiceAsync"/>). An
    /// invoice read that FAILS is a 503 <see cref="InvoiceUnreadableReasonCode"/>.</para>
    /// </remarks>
    /// <param name="confirmWrites">Task 036: set by the upload route and (fix round 1, F4) the document LIST, whose
    /// <c>canCreate</c> is the upload route's answer. Confirms the rights against Dataverse for a caller whose access
    /// Dataverse decided (<see cref="WithDataverseRightsAsync"/>) — on the root, or on the invoice before its secure-roots
    /// gate. It only widens a parent that already holds Read, so the list's Read gate is unchanged. A service request's
    /// requester scope is not confirmed (see the task 036 notes). Download and versions pass nothing and ask Dataverse
    /// nothing.</param>
    /// <param name="logger">Logger for the confirmation; required when <paramref name="confirmWrites"/> is set.</param>
    /// <param name="readCallerPrivilege">Fix round 1 (F1): with <paramref name="confirmWrites"/>, Dataverse's Create also
    /// needs the caller's <see cref="CommunicationRecordAuthorizationFilter.CreateDocumentPrivilege"/> (the document is
    /// written app-only).</param>
    /// <returns>The rights; a fault to answer instead (a 503); and, with <paramref name="confirmWrites"/>, the rights
    /// Dataverse gave no answer for (<see cref="ConfirmedRights.Unanswered"/>, fix round 4 F1: the upload answers a 503 when
    /// Create is missing only for that reason). Without <paramref name="confirmWrites"/> it is always None.</returns>
    private static async Task<(Spaarke.Dataverse.AccessRights Rights, IResult? Fault, Spaarke.Dataverse.AccessRights Unanswered)>
        RightsForDocumentParentAsync(
        CallerPrincipal caller,
        ExternalDataService.DocumentParentKind parentKind,
        Guid parentId,
        ExternalDataService dataService,
        ExternalModuleRegistry modules,
        CancellationToken ct,
        Func<string, Guid, CancellationToken, Task<Spaarke.Dataverse.AccessRights?>>? confirmWrites = null,
        ILogger? logger = null,
        Func<string, CancellationToken, Task<bool?>>? readCallerPrivilege = null)
    {
        const Spaarke.Dataverse.AccessRights none = Spaarke.Dataverse.AccessRights.None;
        switch (parentKind)
        {
            case ExternalDataService.DocumentParentKind.Project:
                return WithoutFault(await RootRightsForParentAsync(ExternalDataService.TodoRootKind.Project));
            case ExternalDataService.DocumentParentKind.Matter:
                return WithoutFault(await RootRightsForParentAsync(ExternalDataService.TodoRootKind.Matter));
            case ExternalDataService.DocumentParentKind.WorkAssignment:
                return WithoutFault(await RootRightsForParentAsync(ExternalDataService.TodoRootKind.WorkAssignment));
            case ExternalDataService.DocumentParentKind.ServiceRequest:
                try
                {
                    return ((await RightsForServiceRequestAsync(caller, parentId, dataService, modules, withState: false, ct)).Rights, null, none);
                }
                catch (ExternalDataService.DataverseUnavailableException)
                {
                    // The requester could not be READ — a fault, not an answer. "Could not tell" must never read as
                    // "no access" (a throttle would 403 the real requester), and it must not read as access either.
                    // Nothing has touched SPE or been written. The detail is generic, so it leaks nothing.
                    return (none, ServiceRequestUnreadableProblem(), none);
                }
            case ExternalDataService.DocumentParentKind.Invoice:
                try
                {
                    return WithoutFault(await RightsForInvoiceAsync(
                        caller, parentId, dataService, modules, ct, confirmWrites, logger, readCallerPrivilege));
                }
                catch (ExternalDataService.DataverseUnavailableException)
                {
                    // Same posture as the service request: an unreadable invoice is a fault (503), never "no access" and
                    // never access. Nothing has touched SPE or been written.
                    return (none, Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Invoice unavailable",
                        detail: "The invoice could not be checked right now. Try again shortly.",
                        extensions: new Dictionary<string, object?> { ["reasonCode"] = InvoiceUnreadableReasonCode }), none);
                }
            default:
                return (none, null, none);
        }

        static (Spaarke.Dataverse.AccessRights, IResult?, Spaarke.Dataverse.AccessRights) WithoutFault(ConfirmedRights confirmed) =>
            (confirmed.Rights, null, confirmed.Unanswered);

        Task<ConfirmedRights> RootRightsForParentAsync(ExternalDataService.TodoRootKind rootKind) =>
            confirmWrites is null
                ? Task.FromResult(new ConfirmedRights(RightsForRoot(caller, rootKind, parentId), none))
                : RightsForRootWriteAsync(caller, rootKind, parentId, confirmWrites,
                    logger ?? throw new ArgumentNullException(nameof(logger)), ct,
                    CommunicationRecordAuthorizationFilter.CreateDocumentPrivilege, readCallerPrivilege);
    }

    /// <summary>The 503 reason code when a service request's requester could not be read (task 031 review).</summary>
    internal const string ServiceRequestUnreadableReasonCode = "sdap.access.service_request_unreadable";

    /// <summary>The 503 answered when a service request's row could not be read (generic detail; leaks nothing).</summary>
    private static IResult ServiceRequestUnreadableProblem() =>
        Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Service request unavailable",
            detail: "The service request could not be checked right now. Try again shortly.",
            extensions: new Dictionary<string, object?> { ["reasonCode"] = ServiceRequestUnreadableReasonCode });

    /// <summary>The 503 reason code when an invoice's parents could not be read (task 031, owner option A).</summary>
    internal const string InvoiceUnreadableReasonCode = "sdap.access.invoice_unreadable";

    /// <summary>
    /// The caller's rights on an invoice's documents (task 031, owner decision 2026-10-11, option A "inherit"): the UNION
    /// of the caller's rights on the invoice's parent records, its <c>sprk_matter</c> and <c>sprk_project</c>, through
    /// the same <see cref="RightsForRoot"/> the matter and project routes use.
    /// </summary>
    /// <remarks>
    /// <para>The union is the rule the invoices module already applies to make an invoice visible (its matter OR its
    /// project in the caller's Read set) and the rule the message routes apply to an invoice post
    /// (<c>ExternalMessageEndpoints.RightsThroughParents</c>). So Read on either parent lists, downloads and reads
    /// versions; Create on either parent uploads; a ViewOnly parent alone gives Read without Create.</para>
    /// <para>A caller holding Read on no matter and no project is denied before any read (the read could not change the
    /// answer). A missing invoice and an invoice under records the caller cannot read deny identically (no oracle).</para>
    /// <para><b>Create passes the invoice gate</b> (unified-access-control-r2 task 187, fix round 2, F1). When the union
    /// carries Create, the invoice is put to the gate the invoice thread uses
    /// (<see cref="ExternalMessageEndpoints.GetRecordRightsDecisionAsync"/>: the rule attributes, the live parents, the
    /// secure flags, then the invoices module's <c>ScopeRows</c>, D4). An invoice whose live roots include a SECURE root the
    /// caller cannot read loses Create, so an upload is refused before SPE or Dataverse is touched (and the list offers no
    /// upload); without this a Create on ordinary matter M wrote the file into secure project X's container. Read is
    /// unchanged: the read routes withhold such an invoice's documents through the document walk (D4), with their own
    /// shapes. A gate read that could not decide throws <see cref="ExternalDataService.DataverseUnavailableException"/>,
    /// which <see cref="RightsForDocumentParentAsync"/> answers with the <see cref="InvoiceUnreadableReasonCode"/> 503.
    /// A caller without Create causes no gate read.</para>
    /// </remarks>
    private static async Task<ConfirmedRights> RightsForInvoiceAsync(
        CallerPrincipal caller,
        Guid invoiceId,
        ExternalDataService dataService,
        ExternalModuleRegistry modules,
        CancellationToken ct,
        Func<string, Guid, CancellationToken, Task<Spaarke.Dataverse.AccessRights?>>? confirmWrites = null,
        ILogger? logger = null,
        Func<string, CancellationToken, Task<bool?>>? readCallerPrivilege = null)
    {
        var denied = new ConfirmedRights(Spaarke.Dataverse.AccessRights.None, Spaarke.Dataverse.AccessRights.None);
        if (caller.GetAccessibleMatterIds().Count == 0 && !caller.GetAccessibleProjectIds().Any())
            return denied;

        var parents = await dataService.GetInvoiceParentsAsync(invoiceId, ct);
        if (parents is not { } invoiceParents)
            return denied;

        var rights = Spaarke.Dataverse.AccessRights.None;
        if (invoiceParents.MatterId is { } matterId)
            rights |= RightsForRoot(caller, ExternalDataService.TodoRootKind.Matter, matterId);
        if (invoiceParents.ProjectId is { } projectId)
            rights |= RightsForRoot(caller, ExternalDataService.TodoRootKind.Project, projectId);

        // Task 036 (FR-20): the upload confirms against Dataverse on the INVOICE itself (the target record), BEFORE the
        // secure-roots gate below, so a Dataverse-confirmed Create is put to that gate exactly as a grant-derived one is.
        var unanswered = Spaarke.Dataverse.AccessRights.None;
        if (confirmWrites is not null)
        {
            var confirmed = await WithDataverseRightsAsync(
                caller, rights,
                ExternalDataService.GetDocumentParentBinding(ExternalDataService.DocumentParentKind.Invoice).EntitySet,
                invoiceId, confirmWrites, logger ?? throw new ArgumentNullException(nameof(logger)), ct,
                CommunicationRecordAuthorizationFilter.CreateDocumentPrivilege, readCallerPrivilege);
            (rights, unanswered) = (confirmed.Rights, confirmed.Unanswered);
        }

        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Create))
            return new ConfirmedRights(rights, unanswered);

        var (gate, undecided) = await ExternalMessageEndpoints.GetRecordRightsDecisionAsync(
                ExternalMessageEndpoints.Roots["invoices"], invoiceId, caller, modules,
                (_, id, attributes, token) => dataService.GetInvoiceLookupsAsync(id, attributes ?? Array.Empty<string>(), token),
                dataService.Logger, ct, dataService.GetNonSecureRootsAsync, dataService.GetLiveParentRootsAsync)
            .ConfigureAwait(false);
        if (undecided)
            throw new ExternalDataService.DataverseUnavailableException("The invoice's secure roots could not be read.");

        // A secure-roots refusal is an ANSWER: Create is removed and is not "unanswered" (fix round 4, F1), so the upload is a
        // 403, never a 503.
        return gate.HasFlag(Spaarke.Dataverse.AccessRights.Read)
            ? new ConfirmedRights(rights, unanswered)
            : new ConfirmedRights(
                rights & ~Spaarke.Dataverse.AccessRights.Create, unanswered & ~Spaarke.Dataverse.AccessRights.Create);
    }

    /// <summary>
    /// The caller's rights on a service request (the requester gate) and, when <paramref name="withState"/> is set, its
    /// open / closed state from the SAME row read (the delete route: one read, not two). The state is
    /// <see cref="ExternalDataService.ServiceRequestOpenState.Unknown"/> unless it was asked for AND the caller holds
    /// rights: a caller without rights never has a state to use.
    /// </summary>
    private static async Task<(Spaarke.Dataverse.AccessRights Rights, ExternalDataService.ServiceRequestOpenState State)>
        RightsForServiceRequestAsync(
            CallerPrincipal caller,
            Guid serviceRequestId,
            ExternalDataService dataService,
            ExternalModuleRegistry modules,
            bool withState,
            CancellationToken ct)
    {
        const ExternalDataService.ServiceRequestOpenState noState = ExternalDataService.ServiceRequestOpenState.Unknown;
        var entity = ExternalDataService
            .GetDocumentParentBinding(ExternalDataService.DocumentParentKind.ServiceRequest).EntityLogicalName;
        var module = modules.FindByEntity(entity);
        if (module is null)
            return (Spaarke.Dataverse.AccessRights.None, noState);

        // The module grants this caller nothing at all (a CIAM partner with PartnerServiceRequestsEnabled off, or a
        // principal with no contact): deny BEFORE any Dataverse read. The read could not change the answer.
        if (!module.EffectiveDimensions.Any(d => d.AccessibleIds(caller).Count > 0))
            return (Spaarke.Dataverse.AccessRights.None, noState);

        // A genuine "no such request" is null (uniform 403, no oracle); a read that failed throws
        // DataverseUnavailableException, which the caller turns into 503.
        Guid? requester;
        var state = noState;
        if (withState)
        {
            var read = await dataService.GetServiceRequestRequesterAndStateAsync(serviceRequestId, ct);
            requester = read?.RequesterId;
            state = read?.State ?? noState;
        }
        else
        {
            requester = await dataService.GetServiceRequestRequesterAsync(serviceRequestId, ct);
        }

        return requester is { } requesterId && module.IsRecordAccessible(caller, requesterId)
            ? (Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Create, state)
            : (Spaarke.Dataverse.AccessRights.None, noState);
    }

    /// <summary>The noun a document parent is named by in a ProblemDetails detail.</summary>
    private static string DocumentParentNoun(ExternalDataService.DocumentParentKind parentKind) => parentKind switch
    {
        ExternalDataService.DocumentParentKind.Matter => "matter",
        ExternalDataService.DocumentParentKind.WorkAssignment => "work assignment",
        ExternalDataService.DocumentParentKind.ServiceRequest => "service request",
        ExternalDataService.DocumentParentKind.Invoice => "invoice",
        _ => "project",
    };

    private static IResult DenyDocumentParent(ExternalDataService.DocumentParentKind parentKind) =>
        Results.Problem(statusCode: 403, title: "Forbidden",
            detail: $"You do not have access to this {DocumentParentNoun(parentKind)}");

    /// <summary>
    /// The single-document check (download, versions): the document is under the route parent AND the caller holds Read on
    /// every SECURE root it is filed to (<see cref="ExternalMultiRootRule"/>, owner decision 2026-10-12, option B) AND
    /// (task 188, D7) the document narrowing does not hide it (<see cref="ExternalDataService.IsDocumentVisibleAsync"/>: own
    /// or ancestor Restricted, or an internal-only / non-Open communication on the bounded parent walk). The secure flags
    /// of its unreadable linked roots are read app-only, only once the document is known to be under the route parent; a
    /// document not under it is refused without that read, and one the root rule refuses without the narrowing read.
    /// </summary>
    private static async Task<bool> IsDocumentReadableUnderAsync(
        ExternalDataService.DocumentScopeRead document, Guid documentId, Guid routeParentId, CallerPrincipal caller,
        ExternalDataService dataService, CancellationToken ct)
    {
        if (!document.IsUnder(routeParentId))
            return false;
        // Task 187: the live roots of the document's other parents join its own root links (one bounded walk, then the one
        // secure-flag read over the union).
        var live = await dataService.ReadLiveParentRootsAsync(new[] { document.Roots.LiveParents }, ct);
        var roots = document.Roots.RuleInput(live);
        var flags = await dataService.ReadSecureRootFlagsAsync(
            caller, new[] { (IEnumerable<(ExternalGrantRootType, string?)>)roots }, ct);
        if (!document.IsReadableUnder(routeParentId, caller, flags, live))
            return false;
        // Task 188: the same refusal for a document the narrowing hides, so the answer cannot tell why.
        return await dataService.IsDocumentVisibleAsync(caller, documentId, document, ct);
    }

    /// <summary>
    /// The ONE response for "this document is not (readably) under this record": not filed to the route's parent, absent,
    /// (fix round for task 039) also filed to a secure root the caller cannot read, or (task 188) hidden by the document
    /// narrowing. A single body for all four, so the refusal cannot be used to probe whether a document exists, where else
    /// it is filed or why it is hidden.
    /// </summary>
    private static IResult DenyDocumentNotUnderParent(ExternalDataService.DocumentParentKind parentKind) =>
        Results.Problem(statusCode: 403, title: "Forbidden",
            detail: $"This document is not part of the requested {DocumentParentNoun(parentKind)}");

    // Route adapters (task 031). Each names its parent kind; the behaviour lives in the four *ForParent handlers.

    private static Task<IResult> GetDocuments(
        Guid id, HttpContext httpContext, ExternalDataService dataService, ExternalModuleRegistry modules,
        CallerRecordAccessProbe probe, ILogger<Program> logger, CancellationToken ct) =>
        ListDocumentsForParent(
            ExternalDataService.DocumentParentKind.Project, id, httpContext, dataService, modules, probe, logger, ct);

    private static Task<IResult> GetMatterDocuments(
        Guid id, HttpContext httpContext, ExternalDataService dataService, ExternalModuleRegistry modules,
        CallerRecordAccessProbe probe, ILogger<Program> logger, CancellationToken ct) =>
        ListDocumentsForParent(
            ExternalDataService.DocumentParentKind.Matter, id, httpContext, dataService, modules, probe, logger, ct);

    private static Task<IResult> GetWorkAssignmentDocuments(
        Guid id, HttpContext httpContext, ExternalDataService dataService, ExternalModuleRegistry modules,
        CallerRecordAccessProbe probe, ILogger<Program> logger, CancellationToken ct) =>
        ListDocumentsForParent(
            ExternalDataService.DocumentParentKind.WorkAssignment, id, httpContext, dataService, modules, probe, logger, ct);

    private static Task<IResult> GetServiceRequestDocuments(
        Guid id, HttpContext httpContext, ExternalDataService dataService, ExternalModuleRegistry modules,
        CallerRecordAccessProbe probe, ILogger<Program> logger, CancellationToken ct) =>
        ListDocumentsForParent(
            ExternalDataService.DocumentParentKind.ServiceRequest, id, httpContext, dataService, modules, probe, logger, ct);

    private static Task<IResult> GetInvoiceDocuments(
        Guid id, HttpContext httpContext, ExternalDataService dataService, ExternalModuleRegistry modules,
        CallerRecordAccessProbe probe, ILogger<Program> logger, CancellationToken ct) =>
        ListDocumentsForParent(
            ExternalDataService.DocumentParentKind.Invoice, id, httpContext, dataService, modules, probe, logger, ct);

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

    private static Task<IResult> DownloadInvoiceDocumentContent(
        Guid id, Guid documentId, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, IDocumentStorageResolver storageResolver, ISpeFileOperations fileStore,
        ILogger<Program> logger, CancellationToken ct) =>
        DownloadDocumentForParent(ExternalDataService.DocumentParentKind.Invoice, id, documentId, httpContext,
            dataService, modules, storageResolver, fileStore, logger, ct);

    private static Task<IResult> UploadDocument(
        Guid id, IFormFile file, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, RecordContainerResolver containerResolver, ISpeFileOperations fileStore,
        IRecordOwnershipResolver ownership, IPostUploadIndexingEnqueuer indexing, JobSubmissionService jobs,
        IConfiguration configuration, CallerRecordAccessProbe probe, ILogger<Program> logger, CancellationToken ct) =>
        UploadDocumentForParent(ExternalDataService.DocumentParentKind.Project, id, file, httpContext, dataService,
            modules, containerResolver, fileStore, ownership, indexing, jobs, configuration, probe, logger, ct);

    private static Task<IResult> UploadMatterDocument(
        Guid id, IFormFile file, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, RecordContainerResolver containerResolver, ISpeFileOperations fileStore,
        IRecordOwnershipResolver ownership, IPostUploadIndexingEnqueuer indexing, JobSubmissionService jobs,
        IConfiguration configuration, CallerRecordAccessProbe probe, ILogger<Program> logger, CancellationToken ct) =>
        UploadDocumentForParent(ExternalDataService.DocumentParentKind.Matter, id, file, httpContext, dataService,
            modules, containerResolver, fileStore, ownership, indexing, jobs, configuration, probe, logger, ct);

    private static Task<IResult> UploadWorkAssignmentDocument(
        Guid id, IFormFile file, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, RecordContainerResolver containerResolver, ISpeFileOperations fileStore,
        IRecordOwnershipResolver ownership, IPostUploadIndexingEnqueuer indexing, JobSubmissionService jobs,
        IConfiguration configuration, CallerRecordAccessProbe probe, ILogger<Program> logger, CancellationToken ct) =>
        UploadDocumentForParent(ExternalDataService.DocumentParentKind.WorkAssignment, id, file, httpContext, dataService,
            modules, containerResolver, fileStore, ownership, indexing, jobs, configuration, probe, logger, ct);

    private static Task<IResult> UploadServiceRequestDocument(
        Guid id, IFormFile file, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, RecordContainerResolver containerResolver, ISpeFileOperations fileStore,
        IRecordOwnershipResolver ownership, IPostUploadIndexingEnqueuer indexing, JobSubmissionService jobs,
        IConfiguration configuration, CallerRecordAccessProbe probe, ILogger<Program> logger, CancellationToken ct) =>
        UploadDocumentForParent(ExternalDataService.DocumentParentKind.ServiceRequest, id, file, httpContext, dataService,
            modules, containerResolver, fileStore, ownership, indexing, jobs, configuration, probe, logger, ct);

    private static Task<IResult> UploadInvoiceDocument(
        Guid id, IFormFile file, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, RecordContainerResolver containerResolver, ISpeFileOperations fileStore,
        IRecordOwnershipResolver ownership, IPostUploadIndexingEnqueuer indexing, JobSubmissionService jobs,
        IConfiguration configuration, CallerRecordAccessProbe probe, ILogger<Program> logger, CancellationToken ct) =>
        UploadDocumentForParent(ExternalDataService.DocumentParentKind.Invoice, id, file, httpContext, dataService,
            modules, containerResolver, fileStore, ownership, indexing, jobs, configuration, probe, logger, ct);

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

    private static Task<IResult> GetInvoiceDocumentVersions(
        Guid id, Guid documentId, HttpContext httpContext, ExternalDataService dataService,
        ExternalModuleRegistry modules, IDocumentStorageResolver storageResolver, ISpeFileOperations fileStore,
        Spaarke.Dataverse.IGenericEntityService entityService, ILogger<Program> logger, CancellationToken ct) =>
        ListDocumentVersionsForParent(ExternalDataService.DocumentParentKind.Invoice, id, documentId, httpContext,
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
        Guid id, HttpContext httpContext, ExternalDataService dataService, CallerRecordAccessProbe probe,
        ILogger<Program> logger, CancellationToken ct) =>
        ListTodosForRoot(ExternalDataService.TodoRootKind.Project, id, httpContext, dataService, probe, logger, ct);

    private static Task<IResult> GetMatterTodos(
        Guid id, HttpContext httpContext, ExternalDataService dataService, CallerRecordAccessProbe probe,
        ILogger<Program> logger, CancellationToken ct) =>
        ListTodosForRoot(ExternalDataService.TodoRootKind.Matter, id, httpContext, dataService, probe, logger, ct);

    private static Task<IResult> GetWorkAssignmentTodos(
        Guid id, HttpContext httpContext, ExternalDataService dataService, CallerRecordAccessProbe probe,
        ILogger<Program> logger, CancellationToken ct) =>
        ListTodosForRoot(ExternalDataService.TodoRootKind.WorkAssignment, id, httpContext, dataService, probe, logger, ct);

    private static Task<IResult> CreateTodo(
        Guid id, CreateExternalTodoRequest request, HttpContext httpContext,
        ExternalDataService dataService, IRecordOwnershipResolver ownership, CallerRecordAccessProbe probe,
        ILogger<Program> logger, CancellationToken ct) =>
        CreateTodoForRoot(ExternalDataService.TodoRootKind.Project, id, request, httpContext, dataService, ownership,
            probe, logger, ct);

    private static Task<IResult> CreateMatterTodo(
        Guid id, CreateExternalTodoRequest request, HttpContext httpContext,
        ExternalDataService dataService, IRecordOwnershipResolver ownership, CallerRecordAccessProbe probe,
        ILogger<Program> logger, CancellationToken ct) =>
        CreateTodoForRoot(ExternalDataService.TodoRootKind.Matter, id, request, httpContext, dataService, ownership,
            probe, logger, ct);

    private static Task<IResult> CreateWorkAssignmentTodo(
        Guid id, CreateExternalTodoRequest request, HttpContext httpContext,
        ExternalDataService dataService, IRecordOwnershipResolver ownership, CallerRecordAccessProbe probe,
        ILogger<Program> logger, CancellationToken ct) =>
        CreateTodoForRoot(ExternalDataService.TodoRootKind.WorkAssignment, id, request, httpContext, dataService, ownership,
            probe, logger, ct);

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
        IPostUploadIndexingEnqueuer indexing,
        JobSubmissionService jobs,
        IConfiguration configuration,
        CallerRecordAccessProbe probe,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        var noun = DocumentParentNoun(parentKind);
        var parentEntity = ExternalDataService.GetDocumentParentBinding(parentKind).EntityLogicalName;

        // ── AUTHORIZATION FIRST — nothing below touches SPE, or writes Dataverse, until both checks pass ──
        // (A service request's rights need one app-only read of its requester; nothing is written.)
        // Task 036 (FR-20): the rights are confirmed against Dataverse for a caller whose access Dataverse decided.
        var (rights, upstreamFault, unanswered) = await RightsForDocumentParentAsync(
            callerContext, parentKind, id, dataService, modules, ct, CallerRightsReader(probe, httpContext), logger,
            CallerPrivilegeReader(probe, httpContext));
        if (upstreamFault is not null) return upstreamFault;
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Read))
        {
            logger.LogWarning("[EXT-UPLOAD] Contact {ContactId} denied — no access to {ParentKind} {ParentId}",
                callerContext.ContactId, parentKind, id);
            return DenyDocumentParent(parentKind);
        }

        // Fix round 4 (F1): Create missing only because Dataverse gave no answer is a retry-able 503 — nothing touches SPE.
        if (new ConfirmedRights(rights, unanswered).IsUnavailableFor(Spaarke.Dataverse.AccessRights.Create))
        {
            logger.LogWarning(
                "[EXT-UPLOAD] Upload to {ParentKind} {ParentId} unavailable: Dataverse gave no answer for the caller's Create",
                parentKind, id);
            return DataverseRightsUnavailable();
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

        // One value feeds both the row and the post-upload index request, so the two cannot name different items.
        var pointers = new ExternalUploadedFilePointers(
            // FileHandleDto.DriveId is the drive resolved during the upload; fall back to the
            // container we resolved rather than persisting a null pointer.
            DriveId: uploaded.DriveId ?? decision.ContainerId,
            ItemId: uploaded.Id,
            FileName: uploaded.Name,
            FileSizeBytes: uploaded.Size,
            WebUrl: uploaded.WebUrl);

        var created = await dataService.CreateDocumentAsync(
            parentKind,
            id,
            pointers,
            owningTeamId,
            callerContext, // task 030 (FR-25): the creator stamp comes from the resolved principal
            ct);

        logger.LogInformation(
            "[EXT-UPLOAD] Contact {ContactId} uploaded '{FileName}' to {ParentKind} {ParentId} as document {DocumentId}",
            callerContext.ContactId, uploadPath, parentKind, id, created.SprkDocumentid);

        // Task 063 (owner decision 2026-10-13): index AND profile the new document, both app-only. Only reached once
        // the bytes and the row exist; neither queue can change the 201.
        await QueueIndexingAndProfileAsync(
            parentKind, id, created.SprkDocumentid, pointers, file.ContentType,
            indexing, jobs, configuration, httpContext.TraceIdentifier, logger);

        // Response carries the document row only — Graph pointers are never surfaced on this surface.
        return Results.Created(
            $"/api/v1/external/{DocumentParentSegment(parentKind)}/{id}/documents/{created.SprkDocumentid}", created);
    }

    /// <summary>
    /// Task 063 (owner decision 2026-10-13): queues app-only AI Search indexing and the document profile for a document an
    /// external caller just uploaded, so it is searchable and profiled like an MDA upload. Best-effort: never throws.
    /// </summary>
    /// <remarks>
    /// <para><b>App-only is correct for both.</b> The bytes were written by <see cref="ISpeFileOperations.UploadSmallAsync"/>
    /// (the BFF's app identity), so this is an MI-written file, the precondition of
    /// <see cref="IPostUploadIndexingEnqueuer.EnqueueAppOnlyIfApplicableAsync"/> (<c>.claude/constraints/auth.md</c>). The
    /// external plane has no user token to queue, and neither job needs one: the profile job
    /// (<see cref="AppOnlyDocumentAnalysisJobHandler"/>) reads the row and downloads the file app-only.</para>
    /// <para><b>Each queue is independent</b>: the two run side by side, and an indexing failure does not stop the
    /// profile. A failure is logged (identifiers and the queue's exception, no content — ADR-015) and never reaches the
    /// response.</para>
    /// <para><b>Bounded, and not cancelled with the request.</b> The bytes and the row are committed, so a caller that
    /// disconnects now must not leave the document un-indexed and un-profiled (the reasoning of <c>OfficeProfileQueue</c>).
    /// But the Service Bus client's default retry budget can hold a send for minutes when the namespace hangs; holding the
    /// 201 that long would time the SPA out on an upload that DID land, and its retry would then meet the route's 409.
    /// So both share one <see cref="PostUploadQueueBudget"/> of their own: a timeout is logged like any other queue
    /// failure, and the 201 goes out.</para>
    /// <para><b>Tenant</b> is the Spaarke tenant from configuration, as every app-only producer reads it — never the
    /// caller token's <c>tid</c>, which names a CIAM directory or a customer's home directory.</para>
    /// </remarks>
    private static async Task QueueIndexingAndProfileAsync(
        ExternalDataService.DocumentParentKind parentKind,
        Guid parentId,
        string? documentId,
        ExternalUploadedFilePointers pointers,
        string? contentType,
        IPostUploadIndexingEnqueuer indexing,
        JobSubmissionService jobs,
        IConfiguration configuration,
        string correlationId,
        ILogger logger)
    {
        using var budget = new CancellationTokenSource(PostUploadQueueBudget);
        await Task.WhenAll(QueueIndexingAsync(budget.Token), QueueProfileAsync(budget.Token));

        async Task QueueIndexingAsync(CancellationToken queueCt)
        {
            try
            {
                var result = await indexing.EnqueueAppOnlyIfApplicableAsync(
                    new PostUploadIndexingRequest(
                        TenantId: configuration["TENANT_ID"] ?? configuration["AzureAd:TenantId"] ?? "",
                        DriveId: pointers.DriveId,
                        ItemId: pointers.ItemId,
                        FileName: pointers.FileName,
                        FileSizeBytes: pointers.FileSizeBytes,
                        ContentType: contentType,
                        DocumentId: documentId,
                        ParentEntity: null, // the app-only handler resolves the governing parent from the document row
                        SearchIndexName: null, // the handler runs the ISearchIndexNameResolver chain
                        Source: ExternalUploadSource,
                        CorrelationId: correlationId),
                    queueCt);

                // The production enqueuer reports its own failure as a result rather than throwing; surface it here with the
                // document and parent ids so a failed enqueue is findable from the route's log. The 201 is unchanged.
                if (result.FailureReason is not null)
                {
                    logger.LogWarning(
                        "[EXT-UPLOAD] Queueing indexing for document {DocumentId} ({ParentKind} {ParentId}) failed ({FailureReason}); the upload stands",
                        documentId, parentKind, parentId, result.FailureReason);
                }
            }
            catch (Exception ex)
            {
                // Defensive: the enqueuer catches and logs its own failures; this keeps a throw from ever reaching the 201.
                logger.LogWarning(ex,
                    "[EXT-UPLOAD] Queueing indexing for document {DocumentId} ({ParentKind} {ParentId}) failed; the upload stands",
                    documentId, parentKind, parentId);
            }
        }

        async Task QueueProfileAsync(CancellationToken queueCt)
        {
            if (!Guid.TryParse(documentId, out var documentGuid))
            {
                logger.LogWarning(
                    "[EXT-UPLOAD] Not queueing the profile: the created document id '{DocumentId}' ({ParentKind} {ParentId}) is not a GUID",
                    documentId, parentKind, parentId);
                return;
            }

            try
            {
                await jobs.SubmitJobAsync(
                    new JobContract
                    {
                        JobId = Guid.NewGuid(),
                        JobType = AppOnlyDocumentAnalysisJobHandler.JobTypeName,
                        SubjectId = documentGuid.ToString(),
                        CorrelationId = correlationId,
                        IdempotencyKey = AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey(documentGuid),
                        Attempt = 1,
                        MaxAttempts = 3,
                        CreatedAt = DateTimeOffset.UtcNow,
                        Payload = JsonDocument.Parse(JsonSerializer.Serialize(new
                        {
                            DocumentId = documentGuid,
                            Source = ExternalUploadSource,
                            EnqueuedAt = DateTimeOffset.UtcNow,
                        })),
                    },
                    queueCt);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "[EXT-UPLOAD] Queueing the document profile for document {DocumentId} ({ParentKind} {ParentId}) failed; the upload stands",
                    documentId, parentKind, parentId);
            }
        }
    }

    /// <summary>The <c>Source</c> the external upload's index request and profile job carry (telemetry only).</summary>
    private const string ExternalUploadSource = "ExternalUpload";

    /// <summary>
    /// How long an external upload's response waits for its two post-upload queues (task 063). A healthy Service Bus send
    /// takes well under a second and the client's transient retries a few seconds; past this the namespace is degraded,
    /// and the upload's 201 must not wait on it.
    /// </summary>
    private static readonly TimeSpan PostUploadQueueBudget = TimeSpan.FromSeconds(15);

    /// <summary>The route segment of a document parent, for the Location of a created document.</summary>
    private static string DocumentParentSegment(ExternalDataService.DocumentParentKind parentKind) => parentKind switch
    {
        ExternalDataService.DocumentParentKind.Matter => "matters",
        ExternalDataService.DocumentParentKind.WorkAssignment => "workassignments",
        ExternalDataService.DocumentParentKind.ServiceRequest => "servicerequests",
        ExternalDataService.DocumentParentKind.Invoice => "invoices",
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
        var (rights, upstreamFault, _) = await RightsForDocumentParentAsync(callerContext, parentKind, id, dataService, modules, ct);
        if (upstreamFault is not null) return upstreamFault;
        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Read))
        {
            logger.LogWarning("[EXT-VERSIONS] Contact {ContactId} denied — no access to {ParentKind} {ParentId}",
                callerContext.ContactId, parentKind, id);
            return DenyDocumentParent(parentKind);
        }

        // Fix round for task 039 (ExternalMultiRootRule, owner option B): as the download route, a document also filed to a
        // SECURE root the caller cannot read is the same uniform 403 as one not under this parent.
        var document = await dataService.GetDocumentParentAndNameAsync(parentKind, documentId, ct);
        if (!await IsDocumentReadableUnderAsync(document, documentId, id, callerContext, dataService, ct))
        {
            logger.LogWarning(
                "[EXT-VERSIONS] Contact {ContactId} denied — document {DocumentId} not in {ParentKind} {ParentId}, also filed to a secure record the caller cannot read, or hidden by the document narrowing",
                callerContext.ContactId, documentId, parentKind, id);
            return DenyDocumentNotUnderParent(parentKind);
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
    /// The calendar events of one accessible root, shared by the project, matter and work-assignment routes (task 039,
    /// FR-03): ONE implementation, three thin adapters that name their root kind, as <see cref="ListTodosForRoot"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Gate:</b> Read on the route root from <see cref="RightsForRoot"/>, or 403 before any Dataverse read. A root
    /// the caller cannot reach, one that does not exist and one the caller holds nothing on are the same answer
    /// (<see cref="DenyRoot"/>), so an id cannot be probed. The rights come from the principal, so there is no Dataverse
    /// read in the decision and nothing here can be a 503; an unreadable events read is flagged <c>truncated</c> like
    /// every other list on this plane (task 105).</para>
    /// <para><b>The project route is the shipped one:</b> the same Read test (<see cref="HoldsReadOnProject"/> is
    /// <c>RightsForRoot(Project)</c>), the same 403 body, the same DTO.</para>
    /// <para><b>Secure records:</b> a secure matter or work assignment is reachable only through the rights the principal
    /// holds on it (a grant row with a level), exactly as for its to-dos and documents; nothing here widens that. An
    /// event also filed to a root the caller cannot read is withheld (<c>ExternalDataService.GetEventsAsync</c>), the same
    /// rule delete-own applies.</para>
    /// </remarks>
    private static async Task<IResult> ListEventsForRoot(
        ExternalDataService.TodoRootKind rootKind,
        Guid rootId,
        HttpContext httpContext,
        ExternalDataService dataService,
        CallerRecordAccessProbe probe,
        ILogger logger,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        // ADR-003: an out-of-scope root DENIES, never an empty list.
        if (!RightsForRoot(callerContext, rootKind, rootId).HasFlag(Spaarke.Dataverse.AccessRights.Read))
            return DenyRoot(rootKind);

        var events = await dataService.GetEventsAsync(rootKind, rootId, callerContext, ct);

        // Task 036 fix round 1 (F4): as the to-do list, but only a PROJECT has an event create route, so only a project's
        // list says, and only to a caller whose access Dataverse decided (AppendTo on the project + prvCreatesprk_Event).
        if (!callerContext.DataverseConfirmsWrites || rootKind != ExternalDataService.TodoRootKind.Project)
            return Results.Ok(events);

        var createRights = await RightsForRootWriteAsync(
            callerContext, rootKind, rootId, CallerRightsReader(probe, httpContext), logger, ct,
            Sprk.Bff.Api.Api.Events.EventEndpoints.CreateEventPrivilege, CallerPrivilegeReader(probe, httpContext));
        return Results.Ok(new ExternalCollectionResponse<ExternalEventDto>
        {
            Value = events.Value,
            Truncated = events.Truncated,
            // An unanswered Dataverse question offers no create (a UI affordance only; fix round 4, F1).
            CanCreate = createRights.Rights.HasFlag(Spaarke.Dataverse.AccessRights.Create),
        });
    }

    /// <summary>GET /api/v1/external/projects/{id}/events — calendar events for a project.</summary>
    private static Task<IResult> GetEvents(
        Guid id, HttpContext httpContext, ExternalDataService dataService, CallerRecordAccessProbe probe,
        ILogger<Program> logger, CancellationToken ct) =>
        ListEventsForRoot(ExternalDataService.TodoRootKind.Project, id, httpContext, dataService, probe, logger, ct);

    /// <summary>GET /api/v1/external/matters/{id}/events — calendar events for a matter (task 039).</summary>
    private static Task<IResult> GetMatterEvents(
        Guid id, HttpContext httpContext, ExternalDataService dataService, CallerRecordAccessProbe probe,
        ILogger<Program> logger, CancellationToken ct) =>
        ListEventsForRoot(ExternalDataService.TodoRootKind.Matter, id, httpContext, dataService, probe, logger, ct);

    /// <summary>GET /api/v1/external/workassignments/{id}/events — calendar events for a work assignment (task 039).</summary>
    private static Task<IResult> GetWorkAssignmentEvents(
        Guid id, HttpContext httpContext, ExternalDataService dataService, CallerRecordAccessProbe probe,
        ILogger<Program> logger, CancellationToken ct) =>
        ListEventsForRoot(ExternalDataService.TodoRootKind.WorkAssignment, id, httpContext, dataService, probe, logger, ct);

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
        CallerRecordAccessProbe probe,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (!callerContext.HasProjectAccess(id))
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have access to this project");

        // Require Create on THIS project (see CreateTodo — effective as of task 033). Task 036 (FR-20): confirmed against
        // Dataverse (AppendTo on the project, and since fix round 1 the caller's prvCreatesprk_Event) for a caller whose
        // access Dataverse decided.
        // Fix round 4 (F1): Create missing only because Dataverse gave no answer is a retry-able 503, not a denial.
        var confirmed = await RightsForRootWriteAsync(
            callerContext, ExternalDataService.TodoRootKind.Project, id, CallerRightsReader(probe, httpContext), logger, ct,
            Sprk.Bff.Api.Api.Events.EventEndpoints.CreateEventPrivilege, CallerPrivilegeReader(probe, httpContext));
        if (confirmed.IsUnavailableFor(Spaarke.Dataverse.AccessRights.Create))
            return DataverseRightsUnavailable();
        if (!confirmed.Rights.HasFlag(Spaarke.Dataverse.AccessRights.Create))
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
        CallerRecordAccessProbe probe,
        ILogger<Program> logger,
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

        // Task 036 (FR-20): for a caller whose access Dataverse decided, the write is confirmed against Dataverse on the
        // TARGET record — this to-do (Write on it), not its root. The root's Read (above) is still the scope check, so a
        // to-do under a root outside the set, or vetoed, asks Dataverse nothing.
        var confirmed = await WithDataverseRightsAsync(
            callerContext, rights,
            ExternalDataService.GetDeletableBinding(ExternalDataService.DeletableKind.Todo).EntitySet, id,
            CallerRightsReader(probe, httpContext), logger, ct);

        // Fix round 4 (F1): Write missing only because Dataverse gave no answer is the to-do PATCH's own retry-able 503
        // (nothing was changed), not a denial. Only an in-scope caller (Read on the root) ever asks Dataverse, so the 503
        // says nothing about a to-do outside the caller's set.
        if (confirmed.IsUnavailableFor(Spaarke.Dataverse.AccessRights.Write))
            return TodoWriteUnavailable();

        // Out-of-scope and insufficient-rights are ONE check: every accessor above returns None for a
        // record the caller cannot reach, so an absent record can never satisfy Write. Keeping them as
        // one expression means a future root type cannot be added to the scope branch while being
        // forgotten in the rights branch.
        if (!confirmed.Rights.HasFlag(Spaarke.Dataverse.AccessRights.Write))
            return DenyTodoWrite();

        // Task 187 (owner round 99, D-123; #1857): the to-do's root is its STAMP, a copy. A to-do whose live parent (the
        // communication, document, event, invoice ... it is filed under) sits under a SECURE root the caller cannot read is
        // behind that root's wall, so it is refused with the same response, and nothing is written. A read that could not
        // decide refuses too (fail closed), as a 503: a transient failure is not "no access" (fix round 1, R4).
        switch (await dataService.CheckLiveParentsAsync(ExternalDataService.DeletableKind.Todo, id, callerContext, ct))
        {
            case ExternalDataService.LiveParentDecision.Readable:
                break;
            case ExternalDataService.LiveParentDecision.Unavailable:
                return TodoWriteUnavailable();
            default:
                return DenyTodoWrite();
        }

        if (DueDateProblem(request.SprkDuedate) is { } badDueDate)
            return badDueDate;

        // Task 106: only a to-do status reason (its state is written with it — ExternalDataService.TodoStateCodeFor).
        if (request.Statuscode is { } status && ExternalDataService.TodoStateCodeFor(status) is null)
            return Results.Problem(statusCode: 400, title: "Bad Request",
                detail: "statuscode must be Open (1), In Progress (659490001), Completed (2) or Dismissed (659490002).");

        await dataService.UpdateTodoAsync(id, request, ct);
        return Results.NoContent();
    }

    /// <summary>The reason code when a to-do PATCH could not be checked right now (nothing was written): a live-parent read
    /// that could not decide (task 187), or Dataverse giving no answer for the caller's Write (task 036 fix round 4, F1).</summary>
    internal const string TodoWriteUnavailableReasonCode = "sdap.access.todo_write_unavailable";

    /// <summary>The ONE refusal of a to-do PATCH: out of scope, too few rights, or (task 187) behind a live parent's secure root.</summary>
    private static IResult DenyTodoWrite() =>
        Results.Problem(statusCode: 403, title: "Forbidden",
            detail: "You do not have access to this to-do",
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = "sdap.access.deny.insufficient_rights"
            });

    /// <summary>
    /// A to-do PATCH whose live parents could not be checked right now (task 187, fix round 1 R4): the delete-own 503's
    /// shape with its own reasonCode, so the SPA can say "try again" rather than "no access". Nothing was written.
    /// </summary>
    private static IResult TodoWriteUnavailable() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Update unavailable",
            detail: "The to-do could not be checked right now, and nothing was changed. Try again shortly.",
            extensions: new Dictionary<string, object?> { ["reasonCode"] = TodoWriteUnavailableReasonCode });

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
    /// <item><b>The caller still holds Read on EVERY parent this plane authorizes</b>, through the same
    /// <see cref="RightsForDocumentParentAsync"/> the list and download routes use (a project, matter or work assignment
    /// from the principal's rights; a service request through the requester scope). The parents consulted are the
    /// lookups <c>ExternalDataService</c> models for the kind: a document's <c>sprk_project</c>, <c>sprk_matter</c>,
    /// <c>sprk_workassignment</c>, <c>sprk_relatedservicerequest</c> and — since the owner's invoice decision of
    /// 2026-10-11 (option A) — <c>sprk_invoice</c>, whose Read is inherited from the invoice's matter and project; a
    /// to-do's or event's <c>sprk_regardingproject</c>, <c>sprk_regardingmatter</c> and
    /// <c>sprk_regardingworkassignment</c>. Other lookups on the row (for example <c>sprk_relatedinvoice</c>,
    /// <c>sprk_relatedagreement</c>, <c>sprk_relatedcontact</c>, <c>sprk_parentdocument</c>, <c>sprk_email</c>) are not
    /// consulted. A row filed to none of the modelled parents is refused. Every modelled
    /// parent, not any: a document also filed by staff to a modelled record the caller can no longer read is not the
    /// caller's to destroy.</item>
    /// </list>
    /// <para><b>No existence oracle.</b> A row that does not exist, someone else's row, an unstamped row and a row whose
    /// parent the caller lost all get the SAME 403 (<see cref="DeleteOwnDeniedReasonCode"/>). A read that FAILED is a 503
    /// (<see cref="DeleteOwnUnavailableReasonCode"/>, or the service-request 503 of
    /// <see cref="RightsForDocumentParentAsync"/>), never a 403 and never a delete.</para>
    /// <para>App-only, like every read on this plane: no OBO (ADR-028). Step (2b), for a caller whose access Dataverse
    /// decided (task 036), asks Dataverse about the caller app-only, naming their systemuser
    /// (<see cref="CallerRightsReader"/>).</para>
    /// </remarks>
    private static async Task<IResult?> AuthorizeDeleteOwnAsync(
        CallerPrincipal caller,
        ExternalDataService.DeletableKind kind,
        Guid id,
        ExternalDataService dataService,
        ExternalModuleRegistry modules,
        Func<string, Guid, CancellationToken, Task<Spaarke.Dataverse.AccessRights?>> readCallerRights,
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

        // (2) Read on every parent this plane authorizes (the modelled lookups only).
        if (target.Parents.Count == 0 || target.HasUnreadableParentLink)
        {
            logger.LogWarning("[EXT-DELETE] Refused {Kind} {Id}: filed to no parent this plane authorizes", kind, id);
            return DenyDeleteOwn();
        }

        foreach (var parent in target.Parents)
        {
            var (rights, fault, _) = await RightsForDocumentParentAsync(caller, parent.Kind, parent.Id, dataService, modules, ct);
            if (fault is not null)
                return fault;
            if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Read))
            {
                logger.LogWarning("[EXT-DELETE] Refused {Kind} {Id}: no Read on its {ParentKind} {ParentId}",
                    kind, id, parent.Kind, parent.Id);
                return DenyDeleteOwn();
            }
        }

        // (2b) Task 036 (FR-20, owner round 59 item 2): a caller whose access Dataverse decided must ALSO hold Delete on the
        //      row itself in Dataverse, asked as the caller. The creator stamp stays required (FR-26): Dataverse adds its own
        //      answer, it never stands in for the stamp. A question Dataverse gave no answer to (a timeout, a fault, an answer
        //      not about the user) is the 503 — a read that FAILED, never a 403 (fix round 4, F1) — and nothing is deleted.
        if (caller.DataverseConfirmsWrites)
        {
            var confirmed = await WithDataverseRightsAsync(
                caller, Spaarke.Dataverse.AccessRights.Read, ExternalDataService.GetDeletableBinding(kind).EntitySet, id,
                readCallerRights, logger, ct);
            if (confirmed.IsUnavailableFor(Spaarke.Dataverse.AccessRights.Delete))
            {
                logger.LogWarning(
                    "[EXT-DELETE] {Kind} {Id}: Dataverse gave no answer for the caller's Delete on it; nothing deleted", kind, id);
                return DeleteOwnUnavailable();
            }

            if (!confirmed.Rights.HasFlag(Spaarke.Dataverse.AccessRights.Delete))
            {
                logger.LogWarning("[EXT-DELETE] Refused {Kind} {Id}: Dataverse does not grant the caller Delete on it", kind, id);
                return DenyDeleteOwn();
            }
        }

        // (3) Task 187 (owner round 99, D-123): a to-do, event or (fix round 1, owner decision D3) document whose LIVE parent
        //     sits under a SECURE root the caller cannot read is behind that root's wall (the creator may have lost access
        //     through a re-file). Refused with the same 403; a read that could not decide is the 503, never a delete.
        if (kind is ExternalDataService.DeletableKind.Todo or ExternalDataService.DeletableKind.Event
            or ExternalDataService.DeletableKind.Document)
        {
            switch (await dataService.CheckLiveParentsAsync(kind, id, caller, ct))
            {
                case ExternalDataService.LiveParentDecision.Readable:
                    break;
                case ExternalDataService.LiveParentDecision.Unavailable:
                    logger.LogWarning("[EXT-DELETE] {Kind} {Id}: its live parents could not be checked; nothing deleted", kind, id);
                    return DeleteOwnUnavailable();
                default:
                    logger.LogWarning("[EXT-DELETE] Refused {Kind} {Id}: a live parent is under a secure record the caller cannot read",
                        kind, id);
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
    /// could not be deleted, this answers 500: the row then points at a deleted file. A retry deletes the row: the
    /// pointer check (<see cref="Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver.VerifyDocumentPointerForDeleteAsync"/>)
    /// reports an item that is gone from this stamp's container, under the pointer the BFF bound to the row, as already
    /// deleted (R3 ISS-004 review F1; this closed the task 032 known limit). An unbound, mismatched or unowned pointer is
    /// still refused (409), and a Graph or Dataverse fault in the check is a retryable 503.</para>
    /// <para><b>One problem key.</b> Every problem body on the delete routes (403, 409, 500, 503, including the
    /// service-request 503 of <see cref="RightsForDocumentParentAsync"/>) carries its code in <c>reasonCode</c>, so the
    /// SPA reads one key. The older <c>code</c> key of the upload and download routes is not used here.</para>
    /// </remarks>
    private static async Task<IResult> DeleteOwnDocument(
        Guid id,
        HttpContext httpContext,
        ExternalDataService dataService,
        ExternalModuleRegistry modules,
        DocumentCheckoutService documents,
        CallerRecordAccessProbe probe,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (await AuthorizeDeleteOwnAsync(callerContext, ExternalDataService.DeletableKind.Document, id, dataService,
                modules, CallerRightsReader(probe, httpContext), logger, ct) is { } refusal)
            return refusal;

        DeleteResult result;
        try
        {
            result = await documents.DeleteAsync(
                id, httpContext.TraceIdentifier, DocumentFileDeletePolicy.VerifiedFileFirstKeepRowOnFailure, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // DeleteAsync throws only from its token, document read and child-document read (task 188 slice D), before
            // anything is deleted. An HttpClient timeout
            // is a TaskCanceledException while ct is NOT cancelled: that is a failed read (503), not the caller leaving.
            logger.LogError(ex, "[EXT-DELETE] Document {DocumentId} could not be read for the delete", id);
            return DeleteOwnUnavailable();
        }

        switch (result)
        {
            case SuccessDeleteResult:
            case NotFoundDeleteResult: // gone between the check and the delete: the end state the caller asked for
                logger.LogInformation("[EXT-DELETE] Document {DocumentId} deleted by its creator (or already gone)", id);
                return Results.NoContent();

            case CheckedOutDeleteResult:
                // The lock holder's name is not shown on the external plane.
                return Results.Problem(statusCode: 409, title: "Document checked out",
                    detail: "This document is being edited, so it cannot be deleted now. Nothing was deleted.",
                    extensions: new Dictionary<string, object?> { ["reasonCode"] = "document_locked" });

            case HasChildDocumentsDeleteResult hasChildren:
                // Task 188 slice D: other documents name this one as their parent. No count on this plane: a child the
                // caller cannot see (D7) must not be counted to them. FileGone: Dataverse's Restrict refused the row
                // after the file went (a child created after the pre-check); the row is kept.
                logger.LogInformation(
                    "[EXT-DELETE] Document {DocumentId}: refused, it has child documents; file already gone: {FileGone}",
                    id, hasChildren.FileGone);
                return Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.DocumentHasChildDocuments(
                    childCount: null, fileGone: hasChildren.FileGone);

            case FileNotDeletedDeleteResult notDeleted:
                return Results.Problem(statusCode: notDeleted.FailureStatusCode, title: "Document not deleted",
                    detail: "The document's file could not be deleted, so the document was kept. Nothing was deleted.",
                    extensions: new Dictionary<string, object?> { ["reasonCode"] = notDeleted.Code });

            case FailedDeleteResult:
                // Under VerifiedFileFirstKeepRowOnFailure the file step ran first and succeeded, so a failed result is
                // the row delete failing after the file went.
                logger.LogError(
                    "[EXT-DELETE] Document {DocumentId}: the file was deleted but the row was not ({Result})",
                    id, result.GetType().Name);
                return Results.Problem(statusCode: 500, title: "Document not fully deleted",
                    detail: "The document's file was deleted, but its record could not be removed. Ask the record's "
                            + "owner to remove it.",
                    extensions: new Dictionary<string, object?> { ["reasonCode"] = "document_row_not_deleted" });

            default:
                // A result this route does not map. It makes no claim about what was deleted.
                logger.LogError(
                    "[EXT-DELETE] Document {DocumentId}: unmapped delete result {Result}", id, result.GetType().Name);
                return Results.Problem(statusCode: 500, title: "Document delete failed",
                    detail: "The document could not be deleted. Try again shortly.",
                    extensions: new Dictionary<string, object?> { ["reasonCode"] = "document_delete_failed" });
        }
    }

    /// <summary>DELETE /api/v1/external/todos/{id} — hard-delete a to-do the caller created (FR-26).</summary>
    private static async Task<IResult> DeleteOwnTodo(
        Guid id, HttpContext httpContext, ExternalDataService dataService, ExternalModuleRegistry modules,
        CallerRecordAccessProbe probe, ILogger<Program> logger, CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (await AuthorizeDeleteOwnAsync(callerContext, ExternalDataService.DeletableKind.Todo, id, dataService,
                modules, CallerRightsReader(probe, httpContext), logger, ct) is { } refusal)
            return refusal;

        return await DeleteAuthorizedRowAsync(ExternalDataService.DeletableKind.Todo, id, dataService, logger, ct);
    }

    /// <summary>DELETE /api/v1/external/events/{id} — hard-delete a calendar event the caller created (FR-26).</summary>
    private static async Task<IResult> DeleteOwnEvent(
        Guid id, HttpContext httpContext, ExternalDataService dataService, ExternalModuleRegistry modules,
        CallerRecordAccessProbe probe, ILogger<Program> logger, CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        if (await AuthorizeDeleteOwnAsync(callerContext, ExternalDataService.DeletableKind.Event, id, dataService,
                modules, CallerRightsReader(probe, httpContext), logger, ct) is { } refusal)
            return refusal;

        return await DeleteAuthorizedRowAsync(ExternalDataService.DeletableKind.Event, id, dataService, logger, ct);
    }

    /// <summary>The 409 reason code when a service request has related records (owner decision 2026-10-11, option A).</summary>
    internal const string HasRelatedRecordsReasonCode = "has_related_records";

    /// <summary>The 409 reason code when a service request is closed (owner decision 2026-10-12: no deleting once closed).</summary>
    internal const string ServiceRequestClosedReasonCode = "service_request_closed";

    /// <summary>
    /// DELETE /api/v1/external/servicerequests/{id} — hard-delete a service request the caller REQUESTED, while NOTHING is
    /// related to it (owner decision 2026-10-11, option A; FR-26).
    /// </summary>
    /// <remarks>
    /// <para><b>1. Requester only.</b> <c>sprk_requestedby</c> must be the caller's contact: the same requester scope the
    /// message and document routes use (<see cref="RightsForDocumentParentAsync"/>, the Service Requests module's accessible
    /// set), so the partner switch (<c>ExternalAccess:PartnerServiceRequestsEnabled</c>) is honoured. Absent, someone
    /// else's and a no-access caller get the SAME 403 as the other delete routes (<see cref="DeleteOwnDeniedReasonCode"/>);
    /// an unreadable requester is the existing 503 (<see cref="ServiceRequestUnreadableReasonCode"/>).</para>
    /// <para><b>2. Open only (owner decision 2026-10-12).</b> A closed (inactive) request is never deleted: 409
    /// <see cref="ServiceRequestClosedReasonCode"/>, decided BEFORE the related-records probe and before any delete. The state
    /// is <c>statecode</c> (0 open, 1 closed; the table has no other status), read in the SAME row read as the requester. A
    /// state that is not a readable 0 or 1 fails closed: refused with the retryable 503 <c>delete_own_unavailable</c>,
    /// nothing deleted.</para>
    /// <para><b>3. Option A.</b> If ANY row is related (every 1:N relationship from <c>sprk_servicerequest</c>:
    /// documents, messages, threads, events, to-dos, memos, analyses, signals - active or not), nothing is deleted and the
    /// answer is 409 <c>has_related_records</c>: all eight relationships are <c>RemoveLink</c>, so a delete would orphan
    /// staff records. The relationship probes are app-only; one that cannot be read is a 503 and nothing is deleted.</para>
    /// <para><b>4. Recall.</b> The 409 tells the user to ask legal to recall it. That request is a plain message posted on
    /// the request's own thread, <c>POST /api/v1/external/servicerequests/{id}/messages</c> (task 034, requester only, no
    /// email) - no endpoint or column of its own.</para>
    /// <para>Known limits: the state read, the probe and the delete are separate calls. A related row created between the
    /// probe and the delete (for example by staff) is unlinked, not deleted. A request closed by staff between the state
    /// read and the delete is still deleted (an <c>If-Match</c> delete would close that window; not done).</para>
    /// </remarks>
    private static async Task<IResult> DeleteOwnServiceRequest(
        Guid id, HttpContext httpContext, ExternalDataService dataService, ExternalModuleRegistry modules,
        ILogger<Program> logger, CancellationToken ct)
    {
        var callerContext = GetCallerPrincipal(httpContext);
        if (callerContext is null) return MissingContextResult();

        // ONE row read gives the requester and the state. The requester gate decides first (a non-requester never has the
        // state used); a read that fails is the existing 503 service_request_unreadable and nothing is deleted.
        Spaarke.Dataverse.AccessRights rights;
        ExternalDataService.ServiceRequestOpenState state;
        try
        {
            (rights, state) = await RightsForServiceRequestAsync(callerContext, id, dataService, modules, withState: true, ct);
        }
        catch (ExternalDataService.DataverseUnavailableException)
        {
            return ServiceRequestUnreadableProblem();
        }

        if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Read))
        {
            logger.LogWarning("[EXT-DELETE] Refused ServiceRequest {Id}: absent or not requested by the caller", id);
            return DenyDeleteOwn();
        }

        // Owner decision 2026-10-12: no deleting once the request is closed. Before the related-records probe and any delete.
        if (state == ExternalDataService.ServiceRequestOpenState.Closed)
        {
            logger.LogInformation("[EXT-DELETE] Refused ServiceRequest {Id}: it is closed", id);
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Service request is closed",
                detail: "This request is closed, so it can't be deleted.",
                extensions: new Dictionary<string, object?> { ["reasonCode"] = ServiceRequestClosedReasonCode });
        }

        if (state != ExternalDataService.ServiceRequestOpenState.Open)
        {
            // Fail closed: a state this check cannot read as Active is never treated as open.
            logger.LogWarning("[EXT-DELETE] Refused ServiceRequest {Id}: its state could not be read as open or closed", id);
            return DeleteOwnUnavailable();
        }

        bool hasRelated;
        try
        {
            hasRelated = await dataService.HasRelatedRecordsAsync(id, ct);
        }
        catch (ExternalDataService.DataverseUnavailableException)
        {
            return DeleteOwnUnavailable();
        }

        if (hasRelated)
        {
            logger.LogInformation("[EXT-DELETE] Refused ServiceRequest {Id}: it has related records", id);
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Service request has activity",
                detail: "This request already has activity (documents, messages or other records), so it can't be deleted. "
                        + "You can ask legal to recall it by sending a request to recall.",
                extensions: new Dictionary<string, object?> { ["reasonCode"] = HasRelatedRecordsReasonCode });
        }

        return await DeleteAuthorizedRowAsync(ExternalDataService.DeletableKind.ServiceRequest, id, dataService, logger, ct);
    }

    /// <summary>
    /// The to-do, event and service request row delete, app-only. Call ONLY after <see cref="AuthorizeDeleteOwnAsync"/>
    /// returned <see langword="null"/> (a service request: after <see cref="DeleteOwnServiceRequest"/>'s own checks).
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
