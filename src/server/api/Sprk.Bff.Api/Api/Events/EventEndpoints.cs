using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Services.Workspace;
// Type aliases to resolve ambiguity between API DTOs and Dataverse models
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;
using DataverseCreateEventRequest = Spaarke.Dataverse.CreateEventRequest;
using DataverseRegardingRecordType = Spaarke.Dataverse.RegardingRecordType;

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
/// </remarks>
public static class EventEndpoints
{
    /// <summary><c>sprk_event</c>'s entity SET (live EntityDefinitions, spaarkedev1 2026-10-03; task 159 notes §0.2).</summary>
    internal const string EventEntitySet = "sprk_events";

    /// <summary>
    /// The Create privilege on <c>sprk_event</c>, by its live name (spaarkedev1 <c>privileges</c>, 2026-10-03 — the table's
    /// schema name is <c>sprk_Event</c>, so the privilege is NOT all lower case; the
    /// <c>FinanceAuthorizationFilter.CreateInvoicePrivilege</c> precedent). A misspelt name answers "not held" for every
    /// caller and denies every create, which is why it is pinned by a test.
    /// </summary>
    internal const string CreateEventPrivilege = "prvCreatesprk_Event";

    /// <summary>The list's refusal for a caller who does not resolve to a Dataverse systemuser.</summary>
    internal const string CallerUnresolvedReasonCode = "sdap.access.deny.caller_unresolved";

    /// <summary>A create whose FR-26 core-ancestor stamp could not be derived: nothing is written.</summary>
    internal const string RegardingStampFailedErrorCode = "events.regarding_stamp_failed";

    /// <summary>The 400 text for a priority outside the live <c>sprk_priority</c> option set (task 097).</summary>
    internal static readonly string PriorityValidationMessage =
        "Priority must be a sprk_priority value: " +
        string.Join(", ", EventPriority.All.Select(p => $"{p.Label} ({p.Value})")) + ".";

    /// <summary>Meter for the events API (registered in TelemetryModule).</summary>
    public const string MeterName = "Sprk.Bff.Api.Events";

    private static readonly System.Diagnostics.Metrics.Meter AuditMeter = new(MeterName);

    private static readonly System.Diagnostics.Metrics.Counter<long> AuditLogWriteFailures =
        AuditMeter.CreateCounter<long>(
            name: "event_audit_log_write_failures_total",
            unit: "{write}",
            description: "sprk_eventlog audit rows that could not be written after the event write committed, by action.");

    /// <summary>Stable log event id for an audit-log write failure (alert on it).</summary>
    internal static readonly EventId AuditLogWriteFailedEventId = new(9701, "EventAuditLogWriteFailed");

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
            .AddRecordRouteAccessAuthorizationFilter("read", EventEntitySet, "id")
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
            .AddRecordRouteAccessAuthorizationFilter("event.attach_regarding", ResolveCreateRegardingTargetAsync, CreateEventPrivilege)
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
            .AddRecordRouteAccessAuthorizationFilter("write", EventEntitySet, "id")
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

    /// <summary>True when the request names a regarding record (validation guarantees type and id come together).</summary>
    internal static bool CarriesRegarding(int? regardingRecordType, Guid? regardingRecordId) =>
        regardingRecordType.HasValue || (regardingRecordId is { } id && id != Guid.Empty);

    internal static async ValueTask<object?> ValidateCreateEventRequestAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<ApiCreateEventRequest>().FirstOrDefault();
        if (request is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = ["A request body is required."]
            });
        }

        return ValidateCreateEventRequest(request) ?? await next(context);
    }

    /// <summary>The create request's shape rules (moved here from the handler by task 159; same messages).</summary>
    internal static IResult? ValidateCreateEventRequest(ApiCreateEventRequest request)
    {
        // Validate required fields (Subject is always required)
        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Subject"] = ["Subject is required."]
            });
        }

        return ValidateCommonFields(
            request.Priority, request.RegardingRecordType, request.RegardingRecordId,
            request.ScheduledStart, request.ScheduledEnd);
    }

    private static IResult? ValidateCommonFields(
        int? priority, int? regardingRecordType, Guid? regardingRecordId, DateOnly? scheduledStart, DateOnly? scheduledEnd)
    {
        // Validate priority if provided — against the LIVE sprk_priority option set (task 097: the former 0..3 range is
        // not an option of the column, so every create carrying a priority was a Dataverse 400).
        if (priority.HasValue && !EventPriority.IsDefined(priority.Value))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Priority"] = [PriorityValidationMessage]
            });
        }

        // Validate regardingRecordType if provided
        if (regardingRecordType.HasValue && (regardingRecordType < 0 || regardingRecordType > 7))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["RegardingRecordType"] = ["Regarding record type must be between 0 and 7."]
            });
        }

        // Task 159: a regarding is a (type, id) pair — the type names the lookup, the id the record. Half of one
        // cannot be authorized or written, so it is a shape error, not a 403.
        var hasId = regardingRecordId is { } id && id != Guid.Empty;
        if (regardingRecordType.HasValue != hasId)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["RegardingRecordId"] = ["RegardingRecordType and RegardingRecordId must be given together, or not at all."]
            });
        }

        // Validate date range if both scheduled dates provided
        if (scheduledStart.HasValue && scheduledEnd.HasValue && scheduledStart > scheduledEnd)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ScheduledEnd"] = ["Scheduled end date must be on or after scheduled start date."]
            });
        }

        return null;
    }

    // =============================================================================================================
    // Authorization targets — read from the ALREADY-BOUND request argument, never the body stream
    // =============================================================================================================

    /// <summary>POST /: the body's regarding record, in the entity set Dataverse's metadata names for its type.</summary>
    internal static ValueTask<(string EntitySet, Guid RecordId)?> ResolveCreateRegardingTargetAsync(
        EndpointFilterInvocationContext context)
    {
        var request = context.Arguments.OfType<ApiCreateEventRequest>().SingleOrDefault()
            ?? throw new InvalidOperationException("The create request was not bound; refusing to authorize nothing.");
        return ResolveRegardingTargetAsync(context, request.RegardingRecordType, request.RegardingRecordId);
    }

    /// <summary>
    /// The regarding record as (entity set, id). The set comes from <see cref="IGenericEntityService.GetEntitySetNameAsync"/>
    /// — live metadata, never a pluralized logical name — and the handler binds the SAME set, so the record authorized
    /// is the record bound. A fault here is a denial (the filter's try).
    /// </summary>
    private static async ValueTask<(string EntitySet, Guid RecordId)?> ResolveRegardingTargetAsync(
        EndpointFilterInvocationContext context, int? regardingRecordType, Guid? regardingRecordId)
    {
        if (!CarriesRegarding(regardingRecordType, regardingRecordId))
        {
            return null; // the request names no regarding record: there is nothing to attach to
        }

        if (regardingRecordType is not { } type || regardingRecordId is not { } id || id == Guid.Empty)
        {
            // Unreachable after validation; refuse rather than skip the check.
            throw new InvalidOperationException("A regarding record needs both its type and its id.");
        }

        var logicalName = DataverseRegardingRecordType.GetEntityLogicalName(type)
            ?? throw new InvalidOperationException($"Unknown regarding record type {type}.");

        var entities = context.HttpContext.RequestServices.GetRequiredService<IGenericEntityService>();
        var entitySet = await entities.GetEntitySetNameAsync(logicalName, context.HttpContext.RequestAborted);
        return (entitySet, id);
    }

    // =============================================================================================================
    // Handlers
    // =============================================================================================================

    /// <summary>
    /// Gets a paginated list of the events the CALLER may read, with optional filtering.
    /// </summary>
    /// <param name="regardingRecordType">Filter by regarding record type (0-7).</param>
    /// <param name="regardingRecordId">Filter by specific regarding record ID (a GUID; anything else is a 400).</param>
    /// <param name="eventTypeId">Filter by event type ID.</param>
    /// <param name="statusCode">Filter by a live status code (e.g. Open 659490001, Completed 659490002, Cancelled 659490004).</param>
    /// <param name="status">String alias for statusCode: "open", "completed", "cancelled". Takes precedence over statusCode.</param>
    /// <param name="priority">Filter by priority (0-3).</param>
    /// <param name="dueDateFrom">Filter events with due date on or after this date.</param>
    /// <param name="dueDateTo">Filter events with due date on or before this date.</param>
    /// <param name="pageNumber">Page number (1-based). Defaults to 1.</param>
    /// <param name="pageSize">Page size. Defaults to 50, max 100.</param>
    /// <param name="httpContext">The request (caller principal, correlation id).</param>
    /// <param name="dataverseService">Dataverse service for querying.</param>
    /// <param name="communicationService">Resolves the regarding type's sprk_recordtype_ref row.</param>
    /// <param name="callerResolver">Resolves the caller's Dataverse systemuserid (the identity the query runs as).</param>
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
        // ── (1) Request shape, no I/O ────────────────────────────────────────────────────────────────────────
        // Validate pagination parameters
        if (pageNumber < 1)
        {
            pageNumber = 1;
        }

        if (pageSize < 1)
        {
            pageSize = 50;
        }
        else if (pageSize > 100)
        {
            pageSize = 100;
        }

        // Map string status alias (used by Copilot) to the live Dataverse statusCode. String wins over integer.
        if (!string.IsNullOrEmpty(status))
        {
            statusCode = status.ToLowerInvariant() switch
            {
                "open" => EventStatusCode.Open,
                "completed" => EventStatusCode.Completed,
                "cancelled" or "canceled" => EventStatusCode.Cancelled,
                _ => statusCode // unknown string → fall through to integer param
            };
        }

        // Validate regardingRecordType if provided
        if (regardingRecordType.HasValue && (regardingRecordType < 0 || regardingRecordType > 7))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["regardingRecordType"] = ["Regarding record type must be between 0 and 7."]
            });
        }

        // Task 159 (OData filter injection, DataverseWebApiService.cs:331): the id is a GUID or the request is
        // refused, so no caller text can reach $filter.
        Guid? regardingId = null;
        if (!string.IsNullOrEmpty(regardingRecordId))
        {
            if (!Guid.TryParse(regardingRecordId, out var parsed) || parsed == Guid.Empty)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["regardingRecordId"] = ["Regarding record id must be a GUID."]
                });
            }

            regardingId = parsed;
        }

        // Validate priority if provided — live sprk_priority values (task 097; 0..3 never matched a row).
        if (priority.HasValue && !EventPriority.IsDefined(priority.Value))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["priority"] = [PriorityValidationMessage]
            });
        }

        // The Web API has no $skip; the page window is read as the first skip + top rows (DataverseWebApiService).
        var skip = (long)(pageNumber - 1) * pageSize;
        if (skip + pageSize > 5000)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pageNumber"] = ["pageNumber × pageSize must not exceed 5000."]
            });
        }

        logger.LogInformation(
            "Retrieving events. RegardingType={RegardingType}, RegardingId={RegardingId}, EventTypeId={EventTypeId}, " +
            "StatusCode={StatusCode}, Priority={Priority}, DueDateFrom={DueDateFrom}, DueDateTo={DueDateTo}, " +
            "Page={Page}, PageSize={PageSize}",
            regardingRecordType, regardingId, eventTypeId, statusCode, priority,
            dueDateFrom, dueDateTo, pageNumber, pageSize);

        // ── (2) The caller the query runs AS. Fail closed: no Dataverse user → no trimmed answer to give ──────
        //    (the word-add-in-r1 task 062 shape, OfficeEndpoints /search/entities). No app-only fallback.
        CallerSystemUserResolution caller;
        try
        {
            caller = await callerResolver.ResolveAsync(httpContext.User, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Event list: the caller's systemuser lookup threw; refusing (fail closed)");
            caller = CallerSystemUserResolution.Unresolved("lookup-threw");
        }

        if (!caller.IsResolved
            || !Guid.TryParse(caller.SystemUserId, out var callerSystemUserId)
            || callerSystemUserId == Guid.Empty)
        {
            logger.LogWarning(
                "Event list refused: the caller has no resolvable Dataverse systemuserid ({Reason}) — refusing rather "
                + "than serving a security-untrimmed app-only list (fail closed).",
                caller.UnresolvedReason ?? "unparseable-systemuserid");

            return Results.Problem(
                title: "Forbidden",
                detail: "The caller could not be resolved to a Dataverse user, so the event list cannot be scoped to their access.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = CallerUnresolvedReasonCode,
                    ["correlationId"] = httpContext.TraceIdentifier,
                });
        }

        try
        {
            // ── (3) A type WITHOUT an id filters on the record-type LOOKUP: resolve this environment's row ──────
            Guid? regardingTypeRefId = null;
            if (regardingRecordType is { } typeOnly && regardingId is null)
            {
                var logicalName = DataverseRegardingRecordType.GetEntityLogicalName(typeOnly)!;
                var row = await communicationService.QueryRecordTypeRefAsync(logicalName, ct);
                if (row is null || row.Id == Guid.Empty)
                {
                    logger.LogWarning(
                        "Event list: no sprk_recordtype_ref row for '{Entity}', so no event can carry that regarding "
                        + "type — returning an empty page without querying.", logicalName);

                    return TypedResults.Ok(new EventListResponse
                    {
                        Items = [],
                        TotalCount = 0,
                        PageSize = pageSize,
                        PageNumber = pageNumber
                    });
                }

                regardingTypeRefId = row.Id;
            }

            // ── (4) The query, AS the caller, narrowed to "my events" (OWNER DECISION B, task 097): owned by the caller,
            //    OR assigned to their linked contact, OR created by them (sprk_createdbyperson). A BFF-created event is
            //    owned by a business-unit TEAM (task 146 / I-6), so owner alone would hide it from its own creator.
            Guid? linkedContact = null;
            try
            {
                linkedContact = (await identity.ResolveAsync(callerSystemUserId, ct)).ContactId;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Event list: the caller's linked contact could not be resolved; listing owned/created events only.");
            }

            var (entities, totalCount) = await dataverseService.QueryEventsAsCallerAsync(
                callerSystemUserId,
                regardingRecordType,
                regardingId,
                regardingTypeRefId,
                eventTypeId,
                statusCode,
                priority,
                dueDateFrom,
                dueDateTo,
                (int)skip,
                pageSize,
                mine: MyEventsScope(callerSystemUserId, linkedContact),
                ct: ct);

            var response = new EventListResponse
            {
                Items = entities.Select(MapEntityToDto).ToArray(),
                TotalCount = totalCount,
                PageSize = pageSize,
                PageNumber = pageNumber
            };

            logger.LogDebug(
                "Returning {Count} events (page {Page} of {TotalPages}, total {TotalCount})",
                response.Items.Length, pageNumber, response.TotalPages, totalCount);

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving events");

            return Results.Problem(
                detail: "An error occurred while retrieving events",
                statusCode: 500,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Gets a single event by its ID. The filter has established Read on sprk_events({id}).
    /// </summary>
    internal static async Task<IResult> GetEventByIdAsync( // internal: EventDateOnlyTests executes the real handler (task 098)
        Guid id,
        HttpContext httpContext,
        IEventDataverseService dataverseService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("Retrieving event. EventId={EventId}", id);

        try
        {
            var eventDto = await GetEventByIdFromDataverseAsync(dataverseService, id, ct);

            if (eventDto is null)
            {
                logger.LogDebug("Event not found. EventId={EventId}", id);
                return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
            }

            logger.LogDebug(
                "Returning event. EventId={EventId}, Subject={Subject}",
                eventDto.Id, eventDto.Subject);

            return TypedResults.Ok(eventDto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving event. EventId={EventId}", id);

            return Results.Problem(
                detail: "An error occurred while retrieving the event",
                statusCode: 500,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Creates a new event. The filters have validated the shape and established, AS THE CALLER, the Create privilege
    /// on sprk_event and AppendTo on the regarding record (route-level authorization, ADR-008, unchanged); the row create
    /// is the ONE server create, <see cref="RecordCreationService"/>, as the caller (field-mapping-server-write-path-r1
    /// task 031, FR-19).
    /// </summary>
    /// <remarks>
    /// <para>Internal (not private) so the test assembly (InternalsVisibleTo) runs the real handler.</para>
    /// <para><b>What the service now owns</b> (ADR-008 path A): the caller checks on the merged payload, field mapping
    /// (when the table's profile applies), the record-first owner (the regarding record's team — the named Secure team
    /// for a secure record — else the caller's own business-unit team), the I-1 core stamps and I-16 Access Permission
    /// derived from what the event is filed under (task 042b), the creator person, the one app-only create, and the owner
    /// membership event (ADR-034 A3). The endpoint no longer resolves the owner, derives the core stamps or publishes.</para>
    /// <para><b>What stays here</b>: the request's own values; the ADR-024 regarding denormalised fields (the record-type
    /// row, the target's name, number and URL — read from the catalog and the target, which the service does not read);
    /// the acting user's linked contact for <c>sprk_assignedto</c> (owner S1; a server-set lookup naming the caller's own
    /// contact, so it costs no AppendTo — as the chat create's "for" person); and the <c>sprk_eventlog</c> audit row after
    /// a successful create.</para>
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
        logger.LogInformation(
            "Creating event. Subject={Subject}, EventTypeId={EventTypeId}, RegardingRecordType={RegardingRecordType}",
            request.Subject, request.EventTypeId, request.RegardingRecordType);

        try
        {
            // The ADR-024 denormalised regarding fields, read here (the catalog and the target are not the service's reads).
            // Runs only after the filters authorized the caller (AppendTo on the regarding record), so the server-side name
            // read is not a disclosure path. The I-1 core stamps and the owner are the service's, from the lookups below.
            RegardingFields? regarding = null;
            if (CarriesRegarding(request.RegardingRecordType, request.RegardingRecordId))
            {
                regarding = await ResolveRegardingFieldsAsync(
                    request.RegardingRecordType!.Value, request.RegardingRecordId!.Value, request.RegardingRecordName,
                    genericEntityService, recordTypes, logger, ct);
            }

            // UAC-r2 task 152 / owner decision S1: the create is app-only, so Created By is the BFF application user
            // and cannot say who the event is FOR. The acting user's LINKED contact (task 141) is written to
            // sprk_assignedto — never an email match; no link → blank + todo_unassigned.
            var callerSystemUserId = await ResolveCallerSystemUserIdAsync(callerResolver, httpContext, logger, ct);
            var assignedToContactId = await ResolveActingUserContactAsync(
                callerResolver, identity, httpContext, logger, ct);

            var outcome = await recordCreation.CreateAsync(
                new RecordCreationRequest(
                    EventTable,
                    new RecordCreationPayload.WebApi(
                        System.Text.Json.JsonSerializer.SerializeToElement(
                            BuildCreatePayload(request, regarding, assignedToContactId))))
                {
                    CorrelationId = httpContext.TraceIdentifier,
                    // FR-19 / FR-03: the mapping profile of what the event is filed under (Matter to Event, ...), the source
                    // inferred from the regarding record and read as the caller first (FR-04). An event regarding nothing has
                    // no source: a silent no-op. Fill-only: every value of the request wins.
                    Mapping = regarding is null ? null : CallerMappingOf(httpContext, logger),
                    // The linked contact names the caller themselves: a server-set lookup, no AppendTo (see remarks).
                    ServerSetLookupColumns = assignedToContactId is null ? null : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AssignedToColumn },
                },
                RecordCreationPrincipal.ForCaller(user, CallerObjectId(httpContext)),
                ct);

            if (outcome.Refusal is { } refusal)
            {
                logger.LogWarning(
                    "Refused event create: {Kind} {Code} (clientStatus={ClientStatus}, correlationId={CorrelationId})",
                    refusal.Kind, refusal.Code, refusal.ClientFailure?.StatusCode, outcome.CorrelationId);
                return RefusalResult(refusal, outcome.Steps, httpContext);
            }

            if (outcome.CreatedId is not { } eventId)
            {
                // Neither a row nor a refusal: never a 201 (fail closed, NFR-01).
                logger.LogError("Event create ended without a decision (correlationId={CorrelationId})", outcome.CorrelationId);
                return GenericCreateFailure();
            }

            var createdOn = DateTime.UtcNow;
            var response = new CreateEventResponse(eventId, request.Subject, createdOn);

            logger.LogInformation(
                "Event created successfully. EventId={EventId}, Subject={Subject}, Warnings={Warnings}",
                eventId, request.Subject, outcome.Warnings.Count);

            // Event Log entry for the creation. Task 097 review F1: the event row is already committed, so an audit
            // failure must never become a 500 (a client retry would create a SECOND event) — it is an Error with a stable
            // event id and a counter (AuditLogWriteFailed), never swallowed silently. Written only after a successful create.
            await WriteCreatedAuditLogAsync(dataverseService, eventId, outcome.OwningTeamId, callerSystemUserId, logger, ct);

            // The owner membership event (R3 task 082, ADR-034 A3, fire-and-forget) is published by the service
            // (RecordCreationService step 12, with the owner team it wrote): none here.
            return TypedResults.Created($"/api/v1/events/{eventId}", response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating event. Subject={Subject}", request.Subject);

            return GenericCreateFailure();
        }
    }

    /// <summary>
    /// The caller's field mapping for a create (FR-03, FR-04): the source is inferred from what the row is filed under and
    /// the caller's own Read on it is probed before it is read. <c>null</c> (no mapping, logged) when the probe is not
    /// registered: a mapping that cannot be made as the caller is never made another way, and never refuses the create.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="logger">The route's logger.</param>
    /// <param name="documentShown">Task 043: the route's answer to the external document narrowing for a document source
    /// (<see cref="RecordCreationMapping.ForCaller"/>); the event route passes none.</param>
    internal static RecordCreationMapping? CallerMappingOf(
        HttpContext httpContext, ILogger logger, Func<Guid, CancellationToken, Task<bool>>? documentShown = null)
    {
        var probe = httpContext.RequestServices?.GetService<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe>();
        if (probe is null)
        {
            logger.LogError("Field mapping was not asked for: no caller-access probe is registered (the create proceeds unmapped)");
            return null;
        }

        return RecordCreationMapping.ForCaller(
            probe, Sprk.Bff.Api.Infrastructure.Auth.TokenHelper.ExtractBearerTokenOrNull(httpContext), documentShown: documentShown);
    }

    /// <summary>The route's existing generic failure: a 500 that says nothing about why.</summary>
    private static IResult GenericCreateFailure() =>
        Results.Problem(
            detail: "An error occurred while creating the event",
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error",
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");

    /// <summary>
    /// A <see cref="RecordCreationService"/> refusal in THIS route's contract (NFR-07, ADR-019). Every refusal the route gave
    /// before it moved onto the service keeps its status, its words and its <c>errorCode</c> / <c>reasonCode</c>:
    /// <list type="bullet">
    /// <item>no owner: the 409 <see cref="ProblemDetailsHelper.RecordOwnerRefused(RecordOwnerResolution, string, string?)"/>
    /// ("The event was not saved: …", the resolver's <c>reasonCode</c>), body unchanged;</item>
    /// <item>an I-1 stamp that cannot be derived (a data condition or a fault): the 500 <see cref="RegardingStampRefused"/>
    /// with <see cref="RegardingStampFailedErrorCode"/>, nothing written. That includes a derived stamp the table's metadata
    /// cannot bind (task 031 fix round 1, F1-low): the service refuses it as the generic non-retryable
    /// <see cref="RecordCreationRefusal.NoDecisionCode"/>, told apart only by the step that refused it — the last step record
    /// is <see cref="RecordCreationStep.StampAndInheritance"/>, <see cref="RecordCreationStepState.Refused"/>
    /// (<paramref name="steps"/>). The same code at any other step stays the generic failure.</item>
    /// </list>
    /// Any other refusal is one the route never gave before (a caller check that raced the route filter, a faulted check):
    /// it fails closed to the route's existing generic failure — a 500 that names no record and writes nothing — with the
    /// service's kind and code in the log only.
    /// </summary>
    internal static IResult RefusalResult(
        RecordCreationRefusal refusal, IReadOnlyList<RecordCreationStepRecord> steps, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        ArgumentNullException.ThrowIfNull(steps);
        return refusal switch
        {
            { Kind: RecordCreationRefusalKind.OwnerUnresolved, OwnerDecision: { } decision } =>
                OwnerRefusalProblem(decision, "event"),
            { Kind: RecordCreationRefusalKind.OwnerUnresolved } =>
                ProblemDetailsHelper.RecordOwnerRefused(refusal.Code, refusal.Detail, "event"),
            { Code: RecordCreationRefusal.StampUnderivableCode or RecordCreationRefusal.StampUnavailableCode } =>
                RegardingStampRefused(httpContext),
            { Kind: RecordCreationRefusalKind.Undecided, Code: RecordCreationRefusal.NoDecisionCode } when RefusedAtTheStamp(steps) =>
                RegardingStampRefused(httpContext),
            _ => GenericCreateFailure(),
        };
    }

    /// <summary>Whether the create was refused at step 10, the I-1 stamp bind (the service's own step record).</summary>
    private static bool RefusedAtTheStamp(IReadOnlyList<RecordCreationStepRecord> steps) =>
        steps.Count > 0 && steps[^1] is { Step: RecordCreationStep.StampAndInheritance, State: RecordCreationStepState.Refused };

    private static Guid? CallerObjectId(HttpContext httpContext) =>
        Guid.TryParse(CallerResolution.ResolveObjectId(httpContext.User), out var oid) && oid != Guid.Empty ? oid : null;

    /// <summary>
    /// The caller's systemuserid (the person the audit row records), or null when it does not resolve. Never throws but a
    /// cancellation: the audit row's person is best-effort, as it was when it came from the owner resolution.
    /// </summary>
    private static async Task<Guid?> ResolveCallerSystemUserIdAsync(
        ICallerSystemUserResolver callerResolver, HttpContext httpContext, ILogger logger, CancellationToken ct)
    {
        try
        {
            var resolution = await callerResolver.ResolveAsync(httpContext.User, ct);
            return resolution.IsResolved && Guid.TryParse(resolution.SystemUserId, out var id) && id != Guid.Empty ? id : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Event create: the caller's system user could not be resolved for the audit row");
            return null;
        }
    }

    /// <summary>The <c>sprk_event</c> table.</summary>
    internal const string EventTable = "sprk_event";

    /// <summary><c>sprk_event.sprk_assignedto</c>: the person the event is FOR (a contact lookup).</summary>
    internal const string AssignedToColumn = "sprk_assignedto";

    /// <summary>A lookup value in the service's Web API payload: the target table and the record (no entity set to guess).</summary>
    private static Dictionary<string, object?> LookupValue(string relatedTable, Guid recordId) => new(StringComparer.Ordinal)
    {
        ["relatedTable"] = relatedTable,
        ["recordId"] = recordId.ToString("D"),
    };

    /// <summary>
    /// The Web API payload of the new event, in column logical names (what <c>DataverseWebApiService.BuildCreateEventPayload</c>
    /// wrote, minus the columns the service owns: the owner, the creator person, the I-1 stamps and the Access Permission).
    /// Open / Active and Source = User are the route's, as before: the platform default for a new row is Draft.
    /// </summary>
    internal static Dictionary<string, object?> BuildCreatePayload(
        ApiCreateEventRequest request, RegardingFields? regarding, Guid? assignedToContactId)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sprk_eventname"] = request.Subject,
            // Live sprk_event option set (task 159, notes §0.2 (e)): Open = 659490001, statecode 0 (Active).
            ["statuscode"] = EventStatusCode.Open,
            ["statecode"] = EventStatusCode.GetStateCode(EventStatusCode.Open),
            ["sprk_source"] = 0, // User
        };

        if (request.Description is not null)
            payload["sprk_description"] = request.Description;
        if (request.EventTypeId is { } eventTypeId)
            payload["sprk_eventtype_ref"] = LookupValue("sprk_eventtype_ref", eventTypeId);
        if (request.ScheduledStart is { } baseDate)
            payload["sprk_basedate"] = DataverseDateOnly.Format(baseDate);
        if (request.DueDate is { } dueDate)
            payload["sprk_duedate"] = DataverseDateOnly.Format(dueDate);
        if (request.Priority is { } priority)
            payload["sprk_priority"] = priority;
        if (assignedToContactId is { } assignedTo && assignedTo != Guid.Empty)
            payload[AssignedToColumn] = LookupValue("contact", assignedTo);

        if (regarding is not null)
        {
            var logicalName = DataverseRegardingRecordType.GetEntityLogicalName(regarding.RegardingType)
                ?? throw new InvalidOperationException($"Unknown regarding record type {regarding.RegardingType}.");
            var lookup = DataverseRegardingRecordType.GetLookupFieldName(regarding.RegardingType)
                ?? throw new InvalidOperationException($"No regarding lookup is known for record type {regarding.RegardingType}.");

            // (i) the target's own typed lookup; (ii) the record type, a lookup to the environment's sprk_recordtype_ref row
            // (left out when it has none); (iii) the denormalised resolver fields.
            payload[lookup] = LookupValue(logicalName, regarding.RegardingId);
            if (regarding.RecordTypeRefId is { } refId && refId != Guid.Empty)
                payload["sprk_regardingrecordtype"] = LookupValue("sprk_recordtype_ref", refId);
            payload["sprk_regardingrecordid"] = regarding.RegardingId.ToString("D");
            payload["sprk_regardingrecordname"] = Truncate(regarding.Name, EventRegardingRecordNameMaxLength);
            payload["sprk_regardingrecordurl"] = regarding.Url;
            if (!string.IsNullOrEmpty(regarding.Number))
                payload["sprk_regardingrecordnumber"] = Truncate(regarding.Number, EventRegardingRecordNumberMaxLength);
        }

        return payload;
    }

    /// <summary><c>sprk_event.sprk_regardingrecordname</c> length (live metadata, 2026-10-03).</summary>
    internal const int EventRegardingRecordNameMaxLength = 1000;

    /// <summary><c>sprk_event.sprk_regardingrecordnumber</c> length (live metadata, 2026-10-03).</summary>
    internal const int EventRegardingRecordNumberMaxLength = 100;

    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: > 0 } && value.Length > maxLength ? value[..maxLength] : value;

    /// <summary>
    /// Gets a single event by ID from Dataverse.
    /// </summary>
    private static async Task<EventDto?> GetEventByIdFromDataverseAsync(
        IEventDataverseService dataverseService,
        Guid id,
        CancellationToken ct)
    {
        var entity = await dataverseService.GetEventAsync(id, ct);
        if (entity == null)
            return null;

        return MapEntityToDto(entity);
    }

    /// <summary>
    /// Maps a Dataverse EventEntity to an EventDto.
    /// </summary>
    private static EventDto MapEntityToDto(Spaarke.Dataverse.EventEntity entity)
    {
        return new EventDto
        {
            Id = entity.Id,
            Subject = entity.Name,
            Description = entity.Description,
            EventTypeId = entity.EventTypeId,
            EventTypeName = entity.EventTypeName,
            RegardingRecordId = entity.RegardingRecordId,
            RegardingRecordName = entity.RegardingRecordName,
            RegardingRecordType = entity.RegardingRecordType,
            RegardingRecordTypeName = entity.RegardingRecordType.HasValue
                ? Dtos.RegardingRecordType.GetDisplayName(entity.RegardingRecordType.Value)
                : null,
            BaseDate = entity.BaseDate,
            DueDate = entity.DueDate,
            FinalDueDate = entity.FinalDueDate,
            CompletedDate = entity.CompletedDate,
            ApprovedDate = entity.ApprovedDate,
            MeetingDate = entity.MeetingDate,
            StateCode = entity.StateCode,
            StatusCode = entity.StatusCode,
            Status = EventStatusCode.GetDisplayName(entity.StatusCode),
            Priority = entity.Priority,
            PriorityName = entity.Priority.HasValue
                ? EventPriority.GetDisplayName(entity.Priority.Value)
                : null,
            Source = entity.Source,
            CreatedOn = entity.CreatedOn,
            ModifiedOn = entity.ModifiedOn
        };
    }

    /// <summary>
    /// UAC-r2 task 152: the acting user's LINKED contact (task 141 — <c>PersonIdentity.ContactId</c>), or null when the
    /// caller does not resolve to a systemuser, has no link, or the read fails. Never an email/UPN match.
    /// </summary>
    internal static async Task<Guid?> ResolveActingUserContactAsync(
        ICallerSystemUserResolver callerResolver,
        Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService identity,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var resolution = await callerResolver.ResolveAsync(httpContext.User, ct);
            if (resolution.IsResolved
                && Guid.TryParse(resolution.SystemUserId, out var systemUserId)
                && systemUserId != Guid.Empty)
            {
                var person = await identity.ResolveAsync(systemUserId, ct);
                if (person.ContactId is { } contactId && contactId != Guid.Empty)
                {
                    return contactId;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Event create: the acting user's linked contact could not be resolved");
        }

        logger.LogWarning(
            "todo_unassigned: entity=sprk_event parentEntity={ParentEntity} parentId={ParentId} reason={Reason} — the acting "
            + "user has no linked contact; sprk_assignedto left blank (never a team, never an email match)",
            "none", (Guid?)null, "acting_user_has_no_linked_contact");
        return null;
    }

    // =============================================================================================================
    // The ADR-024 regarding write set (task 159, #1098)
    // =============================================================================================================

    /// <summary>
    /// The ADR-024 denormalised regarding values the create writes beside the typed lookup (the record-type row, the target's
    /// name, its number and its URL). The I-1 core stamps and the Access Permission are NOT here: the service derives them.
    /// </summary>
    internal sealed record RegardingFields(
        int RegardingType,
        Guid RegardingId,
        Guid? RecordTypeRefId,
        string? Name,
        string Url,
        string? Number);

    /// <summary>Pause before the one catalog retry: long enough for a throttle window to pass, short for a request.</summary>
    internal static readonly TimeSpan CatalogRetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The type's <c>sprk_recordtype_ref</c> row, read once more after a TRANSIENT fault (task 097 round 11). The event
    /// create has no catalog cache, and since round 9 the regarding NUMBER column comes from this row — so one timeout
    /// would otherwise leave the number empty where the old hard-coded map (matter / project / invoice) still wrote it.
    /// A second fault, or a permanent one, propagates to the caller's handler: the number is left empty and the create
    /// still succeeds.
    /// </summary>
    internal static async Task<Microsoft.Xrm.Sdk.Entity?> ReadRecordTypeRefWithOneRetryAsync(
        ICommunicationDataverseService recordTypes, string logicalName, ILogger logger, CancellationToken ct)
    {
        try
        {
            return await recordTypes.QueryRecordTypeRefAsync(logicalName, ct);
        }
        catch (Exception ex) when (IsTransientCatalogFault(ex, ct))
        {
            logger.LogWarning(ex, "sprk_recordtype_ref read for '{Entity}' hit a transient fault; retrying once.", logicalName);
            await Task.Delay(CatalogRetryDelay, ct);
            return await recordTypes.QueryRecordTypeRefAsync(logicalName, ct);
        }
    }

    // Dataverse service-protection (throttling) fault codes — the same set GrantExpiryReminderJob retries on.
    // https://learn.microsoft.com/power-apps/developer/data-platform/api-limits
    private static readonly HashSet<int> ThrottlingErrorCodes = new() { -2147015902, -2147015903, -2147015898 };

    /// <summary>
    /// A timeout or a throttle — the faults a second attempt can clear. The caller's own cancellation is not one; neither
    /// is a rejection (privilege, a missing column), which would fail the same way again.
    /// </summary>
    internal static bool IsTransientCatalogFault(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return false;

        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case TimeoutException or TaskCanceledException:
                    return true;
                case HttpRequestException
                {
                    StatusCode: System.Net.HttpStatusCode.TooManyRequests
                    or System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.GatewayTimeout
                }:
                    return true;
                case System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> fault:
                    return ThrottlingErrorCodes.Contains(fault.Detail?.ErrorCode ?? 0);
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the ADR-024 denormalised regarding fields for <paramref name="regardingType"/>/<paramref name="regardingId"/>:
    /// the record-type row, the target's own name and number, and its URL. The owner, the I-1 core stamps and the Access
    /// Permission are no longer resolved here: <see cref="RecordCreationService"/> derives them from the lookups the create binds
    /// (task 031), and a stamp that cannot be derived refuses there (<see cref="RefusalResult"/>).
    /// </summary>
    /// <remarks>
    /// Runs only after the filters authorized the caller (AppendTo on the regarding record), so the server-side name
    /// read is not a disclosure path. The pieces and their posture, after TodoRegardingBuilder:
    /// <list type="bullet">
    ///   <item>record-type row — <see cref="ICommunicationDataverseService.QueryRecordTypeRefAsync"/>; absent or faulted →
    ///   the record-type lookup is left unset with a warning (non-fatal, as TodoRegardingBuilder);</item>
    ///   <item>name/number — the target's own columns: the name from the one display-name map, the number from the column the
    ///   record-type row names (non-fatal; the request's name otherwise, no number).</item>
    /// </list>
    /// </remarks>
    private static async Task<RegardingFields> ResolveRegardingFieldsAsync(
        int regardingType,
        Guid regardingId,
        string? requestName,
        IGenericEntityService entities,
        ICommunicationDataverseService recordTypes,
        ILogger logger,
        CancellationToken ct)
    {
        var logicalName = DataverseRegardingRecordType.GetEntityLogicalName(regardingType)
            ?? throw new InvalidOperationException($"Unknown regarding record type {regardingType}.");

        Guid? recordTypeRefId = null;
        string? numberField = null;
        try
        {
            var row = await ReadRecordTypeRefWithOneRetryAsync(recordTypes, logicalName, logger, ct);
            if (row is not null && row.Id != Guid.Empty)
            {
                recordTypeRefId = row.Id;
                // Task 097 round 9: the type's reference-number column, as its catalog row names it (invoice, analysis,
                // account, work assignment and budget had no number before — the hard-coded map knew matter/project only).
                numberField = DataverseRegardingRecordType.RecordNumberFieldOf(row);
            }
            else
            {
                logger.LogWarning(
                    "sprk_recordtype_ref not found for entity '{Entity}'. The event's record-type lookup is left unset.",
                    logicalName);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex,
                "sprk_recordtype_ref lookup failed for entity '{Entity}'. The event's record-type lookup is left unset.",
                logicalName);
        }

        var name = requestName;
        string? number = null;
        var nameField = DataverseRegardingRecordType.GetPrimaryNameField(logicalName);
        if (nameField is not null || numberField is not null)
        {
            try
            {
                var columns = new[] { nameField, numberField }.OfType<string>().ToArray();
                var record = await entities.RetrieveAsync(logicalName, regardingId, columns, ct);
                if (nameField is not null && record.GetAttributeValue<string>(nameField) is { Length: > 0 } serverName)
                {
                    name = serverName;
                }

                if (numberField is not null)
                {
                    number = record.GetAttributeValue<string>(numberField);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex,
                    "Regarding name/number read failed for {Entity}; keeping the request's name.", logicalName);

                // Task 097 round 10: a catalog row can name a number column this environment lacks — the combined read
                // then faults. Read the name again on its own so a bad number column never costs the server's name.
                if (nameField is not null && numberField is not null)
                {
                    try
                    {
                        var nameOnly = await entities.RetrieveAsync(logicalName, regardingId, new[] { nameField }, ct);
                        if (nameOnly.GetAttributeValue<string>(nameField) is { Length: > 0 } serverName)
                        {
                            name = serverName;
                        }
                    }
                    catch (Exception retry) when (retry is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        logger.LogDebug(retry, "Regarding name read failed for {Entity}; keeping the request's name.", logicalName);
                    }
                }
            }
        }

        return new RegardingFields(
            regardingType,
            regardingId,
            recordTypeRefId,
            name,
            TodoRegardingBuilder.BuildRecordUrl(logicalName, regardingId.ToString("D")),
            number);
    }

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
    /// The Event Log entry for a successful create. Task 097 review F1: the event row is already committed, so an audit
    /// failure must never become a 500 (a client retry would create a SECOND event) — it is an Error with a stable event id
    /// and a counter (AuditLogWriteFailed), never swallowed silently.
    /// </summary>
    private static async Task WriteCreatedAuditLogAsync(
        IEventDataverseService dataverseService, Guid eventId, Guid? owningTeamId, Guid? createdByPersonId, ILogger logger,
        CancellationToken ct)
    {
        try
        {
            await dataverseService.CreateEventLogAsync(
                eventId,
                Spaarke.Dataverse.EventLogAction.Created,
                "Event created via API",
                owningTeamId, // task 146 — owned like the event it logs
                createdByPersonId,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            ReportAuditLogFailure(logger, ex, eventId, Spaarke.Dataverse.EventLogAction.Created);
        }
    }

    /// <summary>
    /// Task 146: an event log row is content of its event — owned like it (the named Secure team's for a secure
    /// event); <c>Team</c> is <c>null</c> when the event is not team-owned (the row keeps its creator, as the event did).
    /// c1-r1 (owner round 13 item 9): <c>Person</c> is the caller whose change the row logs, recorded because the create
    /// is app-only.
    /// </summary>
    /// <exception cref="Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException">No owner resolves.</exception>
    private static async Task<(Guid? Team, Guid? Person)> ResolveEventLogOwnerAsync(
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership, Guid eventId, HttpContext httpContext,
        CancellationToken ct)
    {
        var owner = await ownership.ResolveOwnerAsync(
            Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.ContentOf("sprk_event", eventId) with
            {
                RequestedBy = Sprk.Bff.Api.Services.Dataverse.RecordRequester.OfObjectId(
                    CallerResolution.ResolveObjectId(httpContext.User)),
            },
            ct);
        if (owner.IsRefused)
            throw new Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException("sprk_eventlog", owner);
        return (owner.IsOwned ? owner.OwningTeamId : null, owner.CreatedByPerson);
    }

    /// <summary>A 409 ProblemDetails for an owner refusal, carrying the stable reason code (task 146).</summary>
    private static IResult OwnerRefusalProblem(Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution refusal, string noun) =>
        Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.RecordOwnerRefused(refusal, noun);

    /// <summary>
    /// Marks an event as completed. The filter has established Write; the status-transition check runs AFTER it,
    /// because it would otherwise disclose the state of an event the caller may not see.
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
        logger.LogInformation("Completing event. EventId={EventId}", id);

        try
        {
            // Check if event exists
            var existing = await GetEventByIdFromDataverseAsync(dataverseService, id, ct);

            if (existing is null)
            {
                logger.LogDebug("Event not found for complete action. EventId={EventId}", id);
                return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
            }

            // Validate status transition: only from open work (EventStatusCode.IsOpenWork — OWNER DECISION A).
            if (!CanCompleteEvent(existing.StatusCode))
            {
                var validStatuses = GetValidStatusesForCompletion();
                logger.LogWarning(
                    "Invalid status transition for complete. EventId={EventId}, CurrentStatus={CurrentStatus}, ValidStatuses={ValidStatuses}",
                    id, existing.Status, validStatuses);

                return Results.Problem(
                    detail: $"Cannot complete event with status '{existing.Status}'. " +
                            $"Event can only be completed when status is one of: {validStatuses}.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Status Transition",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
            }

            var previousStatus = existing.Status;
            var actionTimestamp = DateTime.UtcNow;

            // Task 098: sprk_completeddate is a calendar date — today in the completing user's own time zone, not the
            // UTC date (a 23:49 Eastern completion was dated the next day). See EventCompletionDate.
            var completedDate = await EventCompletionDate.ForCallerAsync(
                httpContext.User, callerResolver, genericEntityService,
                httpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System, logger, ct);

            // Update status to Completed and set completed date
            await UpdateEventStatusAsync(dataverseService, id, EventStatusCode.Completed, completedDate, ct);

            var newStatusDisplay = EventStatusCode.GetDisplayName(EventStatusCode.Completed);

            // Create Event Log entry for the state transition
            await CreateEventLogAsync(
                dataverseService, ownership, id, EventLogAction.Completed,
                $"Status changed from {previousStatus} to {newStatusDisplay}", logger, httpContext, ct);

            var response = new EventActionResponse(
                Id: id,
                PreviousStatus: previousStatus,
                NewStatus: newStatusDisplay,
                ActionTimestamp: actionTimestamp);

            logger.LogInformation(
                "Event completed successfully. EventId={EventId}, PreviousStatus={PreviousStatus}, NewStatus={NewStatus}",
                id, previousStatus, response.NewStatus);

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error completing event. EventId={EventId}", id);

            return Results.Problem(
                detail: "An error occurred while completing the event",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Checks if an event can be completed based on its current status.
    /// </summary>
    /// <remarks>
    /// OWNER DECISION A (2026-10-06, task 097): an event is completable exactly when it is open work —
    /// <see cref="EventStatusCode.IsOpenWork"/> (Draft, Open, On Hold, Reassigned), the SAME predicate the To Do
    /// generation rules use. Completed, Closed, Cancelled, Transferred and No Further Action are refused.
    /// </remarks>
    internal static bool CanCompleteEvent(int statusCode) => EventStatusCode.IsOpenWork(statusCode);

    /// <summary>
    /// Gets the list of valid statuses for completion as a display string.
    /// </summary>
    private static string GetValidStatusesForCompletion() =>
        string.Join(", ", EventStatusCode.All.Where(s => EventStatusCode.IsOpenWork(s.Value)).Select(s => s.Label));

    /// <summary>"My events" (owner decision B): owner OR assigned contact OR created-by person — the caller each time.</summary>
    internal static EventOwnershipScope MyEventsScope(Guid callerSystemUserId, Guid? linkedContactId) =>
        new(callerSystemUserId, linkedContactId is { } c && c != Guid.Empty ? c : null, callerSystemUserId);

    /// <summary>
    /// Updates an event's status in Dataverse.
    /// </summary>
    /// <remarks>
    /// Updates the statuscode field and, for completion, the completeddate the caller resolved
    /// (<see cref="EventCompletionDate"/>, task 098).
    /// </remarks>
    private static Task UpdateEventStatusAsync(
        IEventDataverseService dataverseService,
        Guid id,
        int newStatusCode,
        DateOnly? completedDate,
        CancellationToken ct) =>
        dataverseService.UpdateEventStatusAsync(id, newStatusCode, completedDate, ct);

    /// <summary>
    /// Creates an Event Log entry for a state transition.
    /// </summary>
    /// <param name="dataverseService">Dataverse service for record creation.</param>
    /// <param name="eventId">The event ID.</param>
    /// <param name="action">The action type (Created, Updated, Completed, Cancelled, Deleted).</param>
    /// <param name="description">Description of the change.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    private static async Task CreateEventLogAsync(
        IEventDataverseService dataverseService,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        Guid eventId,
        int action,
        string? description,
        ILogger<Program> logger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        logger.LogInformation(
            "[EventLog] Creating log entry. EventId={EventId}, Action={Action}, Description={Description}",
            eventId,
            EventLogAction.GetDisplayName(action),
            description ?? "(none)");

        try
        {
            // Task 146: owned like its event. A refusal is caught below like any other log failure (best-effort).
            var (logOwner, logPerson) = await ResolveEventLogOwnerAsync(ownership, eventId, httpContext, ct);
            await dataverseService.CreateEventLogAsync(eventId, action, description, logOwner, logPerson, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Don't fail the main operation (it already committed) — but never silently (task 097 review F1).
            ReportAuditLogFailure(logger, ex, eventId, action);
        }
    }

    /// <summary>
    /// An audit-log write that failed AFTER its event write committed: an Error with the stable
    /// <see cref="AuditLogWriteFailedEventId"/> and one <c>event_audit_log_write_failures_total</c> increment, tagged with
    /// the action — alertable, never a 500 that would invite a retry of the committed write.
    /// </summary>
    private static void ReportAuditLogFailure(ILogger logger, Exception ex, Guid eventId, int action)
    {
        AuditLogWriteFailures.Add(1, new KeyValuePair<string, object?>("action", EventLogAction.GetDisplayName(action)));
        logger.LogError(AuditLogWriteFailedEventId, ex,
            "Event audit-log row could not be written after the event write committed. EventId={EventId}, Action={Action}",
            eventId, EventLogAction.GetDisplayName(action));
    }
}
