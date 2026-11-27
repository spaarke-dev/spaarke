using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Events;
using Sprk.Bff.Api.Services.RecordCreation;
// Type aliases to resolve ambiguity between API DTOs and Dataverse models
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;

namespace Sprk.Bff.Api.Api.Events;

/// <summary>
/// API endpoints for Event entity operations.
/// Used by PCF controls and external integrations to query and manage events.
/// </summary>
/// <remarks>
/// <para>Follows ADR-001: Minimal API pattern (no controllers).
/// Follows ADR-008: Endpoint filters for authorization.
/// Follows ADR-019: ProblemDetails for error responses.</para>
/// <para><b>Authorization (unified-access-control-r2 task 159, #1098).</b> The Dataverse client behind
/// <see cref="IEventDataverseService"/> authenticates as the BFF's own identity, so Dataverse row security never
/// applies to it by itself. Every route therefore asks Dataverse, AS THE CALLER, about the exact record it touches
/// before any Dataverse read or write, and fails closed:</para>
/// <list type="bullet">
///   <item><c>GET /</c> runs its query IMPERSONATED as the caller, so the list, every page and the count are only
///   what the caller may read; a caller with no Dataverse user is refused.</item>
///   <item><c>GET /{id}</c> and <c>POST /{id}/complete</c> carry <c>RecordRouteAccessAuthorizationFilter</c> on
///   <c>sprk_events({id})</c>: no Read → the uniform 404 (also the answer for an id that does not exist); Read
///   without the route's right → 403.</item>
///   <item><c>POST /</c> checks the caller's Create privilege on <c>sprk_event</c> and AppendTo on the regarding
///   record, then writes app-only (the G5 pattern, owner round 3b / round 7 item 3).</item>
/// </list>
/// <para>Request-shape validation (no I/O) runs FIRST, as its own endpoint filter on POST /, so a 400 is never
/// turned into a 403 and discloses nothing about any record.</para>
/// <para><b>Deleted routes (owner round 10 item 1).</b> <c>PUT /{id}</c>, <c>DELETE /{id}</c>,
/// <c>POST /{id}/cancel</c> and <c>GET /{id}/logs</c> had no caller in the repository and are in no published API
/// description (the Copilot plugin publishes list, get, create and complete only), so task 159 deleted them rather
/// than gating them. Re-adding any of them needs a caller, a gate, and a row in the route authorization ledger.</para>
/// <para><b>The re-file (task 147 r1c, owner round 36).</b> <c>PATCH /{id}/filing</c> is the event's ONE re-file route
/// (the browser's: the regarding picker on a saved event, the event side pane's regarding lookups). It changes only the
/// filing — the regarding lookups and their ADR-024 resolver fields — behind a shape filter and
/// <c>RecordRouteAccessAuthorizationFilter("write")</c> on <c>sprk_events({id})</c>; the re-file core then asks AppendTo
/// on every new parent and F3 on a move out of a secure record, assigns the owner, re-stamps, mirrors and moves what is
/// filed under the event (<see cref="ChildRecordEndpoints.UpdateAsync"/>). Every other change to an event stays the
/// caller's own Dataverse update.</para>
/// <para><b>The events capability (spaarkeai-mcp-bff-api-r2 task 006).</b> The list, get, create and complete RULES live in
/// <see cref="EventsCapability"/>, which an MCP tool calls through <see cref="IEventsCapability"/>. These four handlers are thin
/// adapters: they bind the request, call the capability's static core after this file's own gates (the group's
/// <c>RequireAuthorization</c> and the endpoint filters, which call the SAME decision methods the capability calls), and map the
/// outcome to the route's unchanged response.</para>
/// </remarks>
public static class EventEndpoints
{
    /// <summary><c>sprk_event</c>'s entity SET (<see cref="EventsCapability.EventEntitySet"/>).</summary>
    internal const string EventEntitySet = EventsCapability.EventEntitySet;

    /// <summary>The Create privilege on <c>sprk_event</c>, by its live name (<see cref="EventsCapability.CreateEventPrivilege"/>).</summary>
    internal const string CreateEventPrivilege = EventsCapability.CreateEventPrivilege;

    /// <summary>The list's refusal for a caller who does not resolve to a Dataverse systemuser.</summary>
    internal const string CallerUnresolvedReasonCode = EventsCapability.CallerUnresolvedReasonCode;

    /// <summary>A create whose FR-26 core-ancestor stamp could not be derived: nothing is written.</summary>
    internal const string RegardingStampFailedErrorCode = EventsCapability.RegardingStampFailedErrorCode;

    /// <summary>Meter for the events API (registered in TelemetryModule).</summary>
    public const string MeterName = EventsCapability.MeterName;

    /// <summary>
    /// Registers event endpoints with the application.
    /// </summary>
    public static void MapEventEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/events")
            .WithTags("Events")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization(); // All endpoints require authentication

        // GET /api/v1/events - List events with filtering and pagination. The query runs AS the caller (no filter:
        // there is no record to check before the query — the impersonated query IS the boundary).
        group.MapGet("/", GetEventsAsync)
            .WithName("GetEvents")
            .WithSummary("Get events with optional filtering")
            .WithDescription("Returns paginated events the caller may read, with optional filters for regarding record, event type, status, priority, and due date range. " +
                "Default page size is 50, maximum is 100.")
            .Produces<EventListResponse>(200)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(401)  // Unauthorized
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(500); // Internal Server Error

        // GET /api/v1/events/{id} - Get single event by ID
        group.MapGet("/{id:guid}", GetEventByIdAsync)
            .AddRecordRouteAccessAuthorizationFilter(EventsCapability.ReadOperation, EventEntitySet, "id")
            .WithName("GetEventById")
            .WithSummary("Get a single event by ID")
            .WithDescription("Returns the event with the specified ID. Returns 404 if the event does not exist or the caller may not read it.")
            .Produces<EventDto>(200)
            .Produces(401)  // Unauthorized
            .Produces(404)  // Not Found
            .Produces(500); // Internal Server Error

        // POST /api/v1/events - Create a new event. Shape validation first; then the caller's Create privilege on
        // sprk_event and AppendTo on the regarding record, AS THE CALLER; then the app-only write.
        group.MapPost("/", CreateEventAsync)
            .AddEndpointFilter(ValidateCreateEventRequestAsync)
            .AddRecordRouteAccessAuthorizationFilter(EventsCapability.AttachRegardingOperation, ResolveCreateRegardingTargetAsync, CreateEventPrivilege)
            .WithName("CreateEvent")
            .WithSummary("Create a new event")
            .WithDescription("Creates a new Event record in Dataverse. Subject is required. " +
                "Returns 201 Created with the new event details on success.")
            .Produces<CreateEventResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict) // task 146: no owner resolvable
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // PATCH /api/v1/events/{id}/filing - Re-file the event (unified-access-control-r2 task 147 r1c; owner round 28 item 1
        // and round 36). Task 159 deleted the general PUT /{id} (round 10 item 1: no caller), so the event's ONE re-file route
        // lives here, in 159's family ("add the route there if the family lacks it; never a second one"). It changes ONLY the
        // filing — the regarding lookups and their ADR-024 resolver fields: the shape filter first (no I/O, so a 400 never
        // becomes a 403); then 159's as-caller check on the event itself (no Read → the uniform 404; Read without Write →
        // 403); then the ONE re-file core (OwnedChildWrite.RefileAsync): AppendTo on every record the event is moved under,
        // 146's F3 on a move out of a secure record, the owner decided before the caller's own PATCH and assigned after it,
        // read back; then 156's core-ancestor re-stamp; then the row's mirror and 148's pass over everything filed under it
        // when it moved under, out of or between secure records (SecureChildReconciler.AfterRefileAsync).
        group.MapPatch("/{id:guid}/filing", RefileEventAsync)
            .AddEndpointFilter(ValidateFilingRequestAsync)
            .AddRecordRouteAccessAuthorizationFilter("write", EventEntitySet, "id")
            .WithName("RefileEvent")
            .WithSummary("Re-file an event (change only what it is filed under)")
            .WithDescription("Takes the Web API payload of the event's regarding lookups and regarding fields, and nothing else. " +
                "The caller needs Write on the event, AppendTo on every record it is moved under and, to move it out of a " +
                "secure record, Full Access on that record or to be the event's creator; the owner is re-derived and assigned, " +
                "the core-ancestor stamp re-derived, and the records filed under the event follow it. An event the caller " +
                "cannot read gets the same 404 as one that does not exist.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // POST /api/v1/events/{id}/complete - Mark event as completed
        group.MapPost("/{id:guid}/complete", CompleteEventAsync)
            .AddRecordRouteAccessAuthorizationFilter(EventsCapability.WriteOperation, EventEntitySet, "id")
            .WithName("CompleteEvent")
            .WithSummary("Mark an event as completed")
            .WithDescription("Changes the event status to Completed. " +
                "Can only complete events with status Draft, Open, On Hold or Reassigned. " +
                "Returns 200 OK with action details on success, 400 if status transition is invalid, 404 if not found.")
            .Produces<EventActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // PATCH /api/v1/events/{id}/due-assignee - the event's due date and/or assignee (ontology platform R1 task 044; #29).
        // The ONE narrow event write, in #1312's one-route-per-table family: after task 159 deleted the general PUT nothing
        // wrote sprk_duedate or sprk_assignedto. Shape first (no I/O); then the as-caller Write on the event; then the write
        // itself runs AS THE CALLER (EventDueAssigneeWrite). Never sprk_finalduedate (D-27).
        group.MapPatch("/{id:guid}/due-assignee", UpdateDueAssigneeAsync)
            .AddEndpointFilter(ValidateDueAssigneeRequestAsync)
            .AddRecordRouteAccessAuthorizationFilter("write", EventEntitySet, "id")
            .WithName("UpdateEventDueAssignee")
            .WithSummary("Reschedule and/or reassign an event")
            .WithDescription("Writes the event's due date (sprk_duedate) and/or its assignee contact (sprk_assignedto, statuscode " +
                "Reassigned, reassigned-by) as the caller. Only open work can be changed. An event the caller cannot read gets " +
                "the same 404 as one that does not exist; Read without Write is a 403.")
            .Produces<UpdateEventDueAssigneeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    // =============================================================================================================
    // PATCH /{id}/due-assignee — the event's due date and assignee (task 044)
    // =============================================================================================================

    /// <summary>The request's SHAPE (no I/O), BEFORE the authorization filter, so a 400 never becomes a 403.</summary>
    internal static async ValueTask<object?> ValidateDueAssigneeRequestAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<UpdateEventDueAssigneeRequest>().FirstOrDefault();
        return EventDueAssigneeWrite.ShapeProblem(request) is { } problem
            ? Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = [problem] })
            : await next(context);
    }

    /// <summary>PATCH /{id}/due-assignee. The filter has established Write; the write is the caller's own.</summary>
    internal static async Task<IResult> UpdateDueAssigneeAsync(
        Guid id,
        [FromBody] UpdateEventDueAssigneeRequest request,
        [FromServices] Sprk.Bff.Api.Infrastructure.Dataverse.IDataverseUserClient user,
        ICallerSystemUserResolver callerResolver,
        Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService identity,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var callerContact = await ResolveActingUserContactAsync(callerResolver, identity, httpContext, logger, ct);
        var result = await EventDueAssigneeWrite.ApplyAsync(
            user, id, request, callerContact,
            (httpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow(), ct);

        switch (result.Outcome)
        {
            case EventDueAssigneeOutcome.Written:
                logger.LogInformation(
                    "Event due date/assignee written as the caller. EventId={EventId}, Rescheduled={Rescheduled}, Reassigned={Reassigned}",
                    id, request.DueDate is not null, request.AssigneeContactId is not null);
                return TypedResults.Ok(new UpdateEventDueAssigneeResponse(
                    id, request.DueDate, request.AssigneeContactId, request.AssigneeContactId is not null));
            case EventDueAssigneeOutcome.NotFound:
                return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
            case EventDueAssigneeOutcome.InvalidState:
                return Results.Problem(detail: result.Detail, statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Status Transition", type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
            case EventDueAssigneeOutcome.Denied:
                return ProblemDetailsHelper.Forbidden(
                    RecordRouteAccessAuthorizationFilter.InsufficientRightsReasonCode,
                    RecordRouteAccessAuthorizationFilter.InsufficientRightsDetail, httpContext.TraceIdentifier);
            default:
                logger.LogError("Event due date/assignee write failed. EventId={EventId}, Status={Status}", id, result.DataverseStatus);
                return Results.Problem(detail: "An error occurred while updating the event",
                    statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    // =============================================================================================================
    // PATCH /{id}/filing — the event's re-file (task 147 r1c, round 36)
    // =============================================================================================================

    /// <summary>
    /// The filing route's request shape (no I/O), BEFORE the authorization filter: only the regarding lookups and the ADR-024
    /// resolver fields (<see cref="ChildRecordEndpoints.FilingShapeProblem"/>).
    /// </summary>
    internal static async ValueTask<object?> ValidateFilingRequestAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var body = context.Arguments.OfType<System.Text.Json.JsonElement>().FirstOrDefault();
        return ChildRecordEndpoints.FilingShapeProblem(body, "sprk_event") ?? await next(context);
    }

    /// <summary>PATCH /api/v1/events/{id}/filing — the ONE browser re-file core, filing columns only.</summary>
    internal static Task<IResult> RefileEventAsync(
        Guid id,
        [FromBody] System.Text.Json.JsonElement body,
        [FromServices] Sprk.Bff.Api.Infrastructure.Dataverse.IDataverseUserClient user,
        [FromServices] Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        [FromServices] Sprk.Bff.Api.Services.Dataverse.CoreAncestorRestamper restamper,
        [FromServices] Sprk.Bff.Api.Services.Access.SecureChildReconciler children,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct) =>
        ChildRecordEndpoints.UpdateAsync(
            "sprk_event", id, body, user, ownership, restamper, children, httpContext, logger, ct, filingOnly: true);

    // =============================================================================================================
    // Request-shape validation (no I/O) — runs BEFORE the authorization filter on POST /
    // =============================================================================================================

    /// <summary>POST /'s first filter: the create request's shape (<see cref="EventsCapability.CreateShapeProblem"/>), as a 400.</summary>
    internal static async ValueTask<object?> ValidateCreateEventRequestAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<ApiCreateEventRequest>().FirstOrDefault();
        return EventsCapability.CreateShapeProblem(request) is { } problem
            ? ValidationProblemOf(problem.Field, problem.Message)
            : await next(context);
    }

    // =============================================================================================================
    // Authorization targets — read from the ALREADY-BOUND request argument, never the body stream
    // =============================================================================================================

    /// <summary>POST /: the body's regarding record, in the entity set Dataverse's metadata names for its type
    /// (<see cref="EventsCapability.RegardingTargetAsync"/>).</summary>
    internal static ValueTask<(string EntitySet, Guid RecordId)?> ResolveCreateRegardingTargetAsync(
        EndpointFilterInvocationContext context)
    {
        var request = context.Arguments.OfType<ApiCreateEventRequest>().SingleOrDefault()
            ?? throw new InvalidOperationException("The create request was not bound; refusing to authorize nothing.");
        var httpContext = context.HttpContext;
        return EventsCapability.RegardingTargetAsync(
            () => httpContext.RequestServices.GetRequiredService<IGenericEntityService>(),
            request.RegardingRecordType, request.RegardingRecordId, httpContext.RequestAborted);
    }

    // =============================================================================================================
    // Handlers — thin adapters over EventsCapability's cores (bind, call, map)
    // =============================================================================================================

    /// <summary>
    /// Gets a paginated list of the events the CALLER may read, with optional filtering
    /// (<see cref="EventsCapability.ListCoreAsync"/>: the query runs impersonated AS the caller).
    /// </summary>
    /// <param name="regardingRecordType">Filter by regarding record type (0-7).</param>
    /// <param name="regardingRecordId">Filter by specific regarding record ID (a GUID; anything else is a 400).</param>
    /// <param name="eventTypeId">Filter by event type ID.</param>
    /// <param name="statusCode">Filter by a live status code (e.g. Open 659490001, Completed 659490002, Cancelled 659490004).</param>
    /// <param name="status">String alias for statusCode: "open", "completed", "cancelled". Takes precedence over statusCode.</param>
    /// <param name="priority">Filter by priority (a live sprk_priority value).</param>
    /// <param name="dueDateFrom">Filter events with due date on or after this date.</param>
    /// <param name="dueDateTo">Filter events with due date on or before this date.</param>
    /// <param name="pageNumber">Page number (1-based). Defaults to 1.</param>
    /// <param name="pageSize">Page size. Defaults to 50, max 100.</param>
    /// <param name="httpContext">The request (caller principal, correlation id).</param>
    /// <param name="dataverseService">Dataverse service for querying.</param>
    /// <param name="communicationService">Resolves the regarding type's sprk_recordtype_ref row.</param>
    /// <param name="callerResolver">Resolves the caller's Dataverse systemuserid (the identity the query runs as).</param>
    /// <param name="identity">Resolves the caller's linked contact ("my events").</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paginated list of events.</returns>
    private static async Task<IResult> GetEventsAsync(
        [FromQuery] int? regardingRecordType,
        [FromQuery] string? regardingRecordId,
        [FromQuery] Guid? eventTypeId,
        [FromQuery] int? statusCode,
        [FromQuery] string? status,
        [FromQuery] int? priority,
        [FromQuery] DateTime? dueDateFrom,
        [FromQuery] DateTime? dueDateTo,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        HttpContext httpContext = null!,
        IEventDataverseService dataverseService = null!,
        ICommunicationDataverseService communicationService = null!,
        ICallerSystemUserResolver callerResolver = null!,
        Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService identity = null!,
        ILogger<Program> logger = null!,
        CancellationToken ct = default)
    {
        var query = new EventListQuery
        {
            RegardingRecordType = regardingRecordType,
            RegardingRecordId = regardingRecordId,
            EventTypeId = eventTypeId,
            StatusCode = statusCode,
            Status = status,
            Priority = priority,
            DueDateFrom = dueDateFrom,
            DueDateTo = dueDateTo,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };

        var result = await EventsCapability.ListCoreAsync(
            httpContext, query, dataverseService, communicationService, callerResolver, identity, logger, ct);

        return result.Status switch
        {
            EventCapabilityStatus.Ok => TypedResults.Ok(result.Value!),
            EventCapabilityStatus.Invalid => ValidationProblemOf(result.Field!, result.Message!),
            EventCapabilityStatus.CallerUnresolved => Results.Problem(
                title: "Forbidden",
                detail: "The caller could not be resolved to a Dataverse user, so the event list cannot be scoped to their access.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = CallerUnresolvedReasonCode,
                    ["correlationId"] = httpContext.TraceIdentifier,
                }),
            _ => InternalServerError("An error occurred while retrieving events"),
        };
    }

    /// <summary>
    /// Gets a single event by its ID (<see cref="EventsCapability.GetCoreAsync"/>). The filter has established Read on
    /// sprk_events({id}).
    /// </summary>
    internal static async Task<IResult> GetEventByIdAsync( // internal: EventDateOnlyTests executes the real handler (task 098)
        Guid id,
        HttpContext httpContext,
        IEventDataverseService dataverseService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var result = await EventsCapability.GetCoreAsync(id, dataverseService, logger, ct);

        return result.Status switch
        {
            EventCapabilityStatus.Ok => TypedResults.Ok(result.Value!),
            EventCapabilityStatus.NotFound => ProblemDetailsHelper.UniformRecordNotFound(httpContext),
            _ => InternalServerError("An error occurred while retrieving the event"),
        };
    }

    /// <summary>
    /// Creates a new event (<see cref="EventsCapability.CreateCoreAsync"/>). The filters have validated the shape and
    /// established, AS THE CALLER, the Create privilege on sprk_event and AppendTo on the regarding record (route-level
    /// authorization, ADR-008, unchanged); the row create is the ONE server create, <see cref="RecordCreationService"/>.
    /// </summary>
    /// <remarks>
    /// <para>Internal (not private) so the test assembly (InternalsVisibleTo) runs the real handler.</para>
    /// <para><b>Refusals (NFR-07).</b> The owner refusal keeps its 409 and body; a stamp that cannot be derived keeps its
    /// 500 <see cref="RegardingStampFailedErrorCode"/> (both rendered by <see cref="RefusalResult"/>). A create that
    /// refuses writes nothing and logs no audit row.</para>
    /// </remarks>
    internal static async Task<IResult> CreateEventAsync(
        [FromBody] ApiCreateEventRequest request,
        [FromServices] RecordCreationService recordCreation,
        [FromServices] IDataverseUserClient user,
        IEventDataverseService dataverseService,
        Spaarke.Dataverse.IGenericEntityService genericEntityService,
        ICallerSystemUserResolver callerResolver,
        Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService identity,
        ICommunicationDataverseService recordTypes,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var result = await EventsCapability.CreateCoreAsync(
            request, httpContext, recordCreation, user, dataverseService, genericEntityService, callerResolver, identity,
            recordTypes, logger, ct);

        return result.Status switch
        {
            EventCapabilityStatus.Ok => TypedResults.Created($"/api/v1/events/{result.Value!.Id}", result.Value),
            EventCapabilityStatus.Refused => RefusalResult(result.Refusal!, result.Steps, httpContext),
            _ => GenericCreateFailure(),
        };
    }

    /// <summary>
    /// The caller's field mapping for a create (FR-03, FR-04; <see cref="EventsCapability.CallerMappingOf"/>), kept here for
    /// the analysis create route, which shares it.
    /// </summary>
    internal static RecordCreationMapping? CallerMappingOf(
        HttpContext httpContext, ILogger logger, Func<Guid, CancellationToken, Task<bool>>? documentShown = null) =>
        EventsCapability.CallerMappingOf(httpContext, logger, documentShown);

    /// <summary>The route's existing generic failure: a 500 that says nothing about why.</summary>
    private static IResult GenericCreateFailure() => InternalServerError("An error occurred while creating the event");

    /// <summary>
    /// A <see cref="RecordCreationService"/> refusal in THIS route's contract (NFR-07, ADR-019), as
    /// <see cref="EventsCapability.ClassifyRefusal"/> classifies it. Every refusal the route gave before it moved onto the
    /// service keeps its status, its words and its <c>errorCode</c> / <c>reasonCode</c>:
    /// <list type="bullet">
    /// <item>no owner: the 409 <see cref="ProblemDetailsHelper.RecordOwnerRefused(RecordOwnerResolution, string, string?)"/>
    /// ("The event was not saved: …", the resolver's <c>reasonCode</c>), body unchanged;</item>
    /// <item>an I-1 stamp that cannot be derived or bound: the 500 <see cref="RegardingStampRefused"/> with
    /// <see cref="RegardingStampFailedErrorCode"/>, nothing written.</item>
    /// </list>
    /// Any other refusal is one the route never gave before (a caller check that raced the route filter, a faulted check):
    /// it fails closed to the route's existing generic failure — a 500 that names no record and writes nothing — with the
    /// service's kind and code in the log only.
    /// </summary>
    internal static IResult RefusalResult(
        RecordCreationRefusal refusal, IReadOnlyList<RecordCreationStepRecord> steps, HttpContext httpContext) =>
        EventsCapability.ClassifyRefusal(refusal, steps) switch
        {
            EventsCapability.CreateRefusalShape.OwnerUnresolved when refusal.OwnerDecision is { } decision =>
                ProblemDetailsHelper.RecordOwnerRefused(decision, "event"),
            EventsCapability.CreateRefusalShape.OwnerUnresolved =>
                ProblemDetailsHelper.RecordOwnerRefused(refusal.Code, refusal.Detail, "event"),
            EventsCapability.CreateRefusalShape.StampFailed => RegardingStampRefused(httpContext),
            _ => GenericCreateFailure(),
        };

    /// <summary>The refusal when a create cannot be stamped: a 500 that names no record and writes nothing.</summary>
    private static IResult RegardingStampRefused(HttpContext httpContext) =>
        Results.Problem(
            title: "Internal Server Error",
            detail: "The event's regarding record could not be resolved for access inheritance, so nothing was written.",
            statusCode: StatusCodes.Status500InternalServerError,
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = RegardingStampFailedErrorCode,
                ["correlationId"] = httpContext.TraceIdentifier,
            });

    /// <summary>
    /// UAC-r2 task 152: the acting user's LINKED contact (<see cref="EventsCapability.ResolveActingUserContactAsync"/>), kept
    /// here for the due-assignee route and the decision route cores, which share it.
    /// </summary>
    internal static Task<Guid?> ResolveActingUserContactAsync(
        ICallerSystemUserResolver callerResolver,
        Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService identity,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct) =>
        EventsCapability.ResolveActingUserContactAsync(callerResolver, identity, httpContext, logger, ct);

    /// <summary>A timeout or a throttle on the record-type catalog read (<see cref="EventsCapability.IsTransientCatalogFault"/>).</summary>
    internal static bool IsTransientCatalogFault(Exception ex, CancellationToken ct) => EventsCapability.IsTransientCatalogFault(ex, ct);

    /// <summary>
    /// Marks an event as completed (<see cref="EventsCapability.CompleteCoreAsync"/>). The filter has established Write; the
    /// status-transition check runs AFTER it, because it would otherwise disclose the state of an event the caller may not see.
    /// </summary>
    internal static async Task<IResult> CompleteEventAsync(
        Guid id,
        HttpContext httpContext,
        IEventDataverseService dataverseService,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        ICallerSystemUserResolver callerResolver,
        Spaarke.Dataverse.IGenericEntityService genericEntityService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var result = await EventsCapability.CompleteCoreAsync(
            id, httpContext, dataverseService, ownership, callerResolver, genericEntityService, logger, ct);

        return result.Status switch
        {
            EventCapabilityStatus.Ok => TypedResults.Ok(result.Value!),
            EventCapabilityStatus.NotFound => ProblemDetailsHelper.UniformRecordNotFound(httpContext),
            EventCapabilityStatus.InvalidTransition => Results.Problem(
                detail: result.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Status Transition",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1"),
            _ => InternalServerError("An error occurred while completing the event"),
        };
    }

    /// <summary>Whether an event can be completed: open work (<see cref="EventsCapability.CanCompleteEvent"/>, OWNER DECISION A).</summary>
    internal static bool CanCompleteEvent(int statusCode) => EventsCapability.CanCompleteEvent(statusCode);

    /// <summary>"My events" (owner decision B; <see cref="EventsCapability.MyEventsScope"/>).</summary>
    internal static EventOwnershipScope MyEventsScope(Guid callerSystemUserId, Guid? linkedContactId) =>
        EventsCapability.MyEventsScope(callerSystemUserId, linkedContactId);

    /// <summary>A request-shape 400: one member, one message (the shape every events 400 has).</summary>
    private static IResult ValidationProblemOf(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>The routes' generic 500, which says nothing about why.</summary>
    private static IResult InternalServerError(string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error",
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
}
