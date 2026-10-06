using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
// Type aliases to resolve ambiguity between API DTOs and Dataverse models
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;
using ApiRegardingRecordType = Sprk.Bff.Api.Api.Events.Dtos.RegardingRecordType;
using ApiUpdateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.UpdateEventRequest;
using DataverseCreateEventRequest = Spaarke.Dataverse.CreateEventRequest;
using DataverseUpdateEventRequest = Spaarke.Dataverse.UpdateEventRequest;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.Events;

/// <summary>
/// API endpoints for Event entity operations.
/// Used by PCF controls and external integrations to query and manage events.
/// </summary>
/// <remarks>
/// Follows ADR-001: Minimal API pattern (no controllers).
/// Follows ADR-008: Endpoint filters for authorization.
/// Follows ADR-019: ProblemDetails for error responses.
/// </remarks>
public static class EventEndpoints
{
    /// <summary>Task 097: priority is validated against the live sprk_priority option set (the former 0..3 range was
    /// rejected by Dataverse with "outside the valid range").</summary>
    private static readonly string PriorityValidationMessage =
        "Priority must be a sprk_event priority: " +
        string.Join(", ", EventPriority.All.Select(p => $"{p.Label} ({p.Value})")) + ".";

    /// <summary>Meter for the event audit-log failure counter (registered in TelemetryModule).</summary>
    internal const string MeterName = "Sprk.Bff.Api.Events";

    private static readonly System.Diagnostics.Metrics.Meter AuditMeter = new(MeterName, "1.0.0");

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

        // GET /api/v1/events - List events with filtering and pagination
        group.MapGet("/", GetEventsAsync)
            .WithName("GetEvents")
            .WithSummary("Get events with optional filtering")
            .WithDescription("Returns paginated events with optional filters for regarding record, event type, status, priority, and due date range. " +
                "Default page size is 50, maximum is 100.")
            .Produces<EventListResponse>(200)
            .Produces(401)  // Unauthorized
            .Produces(500); // Internal Server Error

        // GET /api/v1/events/{id} - Get single event by ID
        group.MapGet("/{id:guid}", GetEventByIdAsync)
            .AddEventRecordAccessFilter("read")
            .WithName("GetEventById")
            .WithSummary("Get a single event by ID")
            .WithDescription("Returns the event with the specified ID. Returns 404 if the event does not exist.")
            .Produces<EventDto>(200)
            .Produces(401)  // Unauthorized
            .Produces(404)  // Not Found
            .Produces(500); // Internal Server Error

        // DELETE /api/v1/events/{id} - Soft delete (set status to Canceled)
        group.MapDelete("/{id:guid}", DeleteEventAsync)
            .AddEventRecordAccessFilter("write")
            .WithName("DeleteEvent")
            .WithSummary("Soft delete an event")
            .WithDescription("Soft deletes the event by setting its status to Canceled. " +
                "The record is not physically deleted to preserve audit trail. Returns 204 on success, 404 if not found.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/v1/events - Create a new event
        group.MapPost("/", CreateEventAsync)
            .AddEventCreatePrivilegeFilter()
            .AddEventParentAccessFilter()
            .WithName("CreateEvent")
            .WithSummary("Create a new event")
            .WithDescription("Creates a new Event record in Dataverse. Subject is required. " +
                "Returns 201 Created with the new event details on success.")
            .Produces<CreateEventResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // PUT /api/v1/events/{id} - Update an existing event
        group.MapPut("/{id:guid}", UpdateEventAsync)
            .AddEventRecordAccessFilter("write")
            .AddEventParentAccessFilter()
            .WithName("UpdateEvent")
            .WithSummary("Update an existing event")
            .WithDescription("Updates an existing Event record in Dataverse. Only specified fields are updated. " +
                "Returns 200 OK with the updated event on success, 404 if not found.")
            .Produces<EventDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/v1/events/{id}/complete - Mark event as completed
        group.MapPost("/{id:guid}/complete", CompleteEventAsync)
            .AddEventRecordAccessFilter("write")
            .WithName("CompleteEvent")
            .WithSummary("Mark an event as completed")
            .WithDescription("Changes the event status to Completed (659490002, which stays Active). " +
                "Can only complete events with status Draft (1), Open (659490001), On Hold (659490006) or Reassigned (659490007). " +
                "Returns 200 OK with action details on success, 400 if status transition is invalid, 404 if not found.")
            .Produces<EventActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/v1/events/{id}/cancel - Mark event as canceled
        group.MapPost("/{id:guid}/cancel", CancelEventAsync)
            .AddEventRecordAccessFilter("write")
            .WithName("CancelEvent")
            .WithSummary("Mark an event as canceled")
            .WithDescription("Changes the event status to Cancelled (659490004, Inactive). " +
                "Can only cancel events with status Draft (1), Open (659490001), On Hold (659490006) or Reassigned (659490007). " +
                "Returns 200 OK with action details on success, 400 if status transition is invalid, 404 if not found.")
            .Produces<EventActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/v1/events/{id}/logs - Get event log entries
        group.MapGet("/{id:guid}/logs", GetEventLogsAsync)
            .AddEventRecordAccessFilter("read")
            .WithName("GetEventLogs")
            .WithSummary("Get event log entries")
            .WithDescription("Returns all log entries for the specified event, tracking state transitions. " +
                "Includes created, completed, cancelled, and deleted actions.")
            .Produces<EventLogListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// Gets a paginated list of events with optional filtering.
    /// </summary>
    /// <param name="regardingRecordType">Filter by regarding record type (0-7).</param>
    /// <param name="regardingRecordId">Filter by specific regarding record ID.</param>
    /// <param name="eventTypeId">Filter by event type ID.</param>
    /// <param name="statusCode">Filter by a live status reason (<see cref="EventStatusCode"/>), e.g. 659490001=Open,
    /// 659490002=Completed, 659490004=Cancelled.</param>
    /// <param name="status">String alias for statusCode: "open", "completed", "cancelled". Takes precedence over statusCode.</param>
    /// <param name="priority">Filter by a live sprk_priority value (<see cref="EventPriority"/>): 100000000 Low … 100000003 Urgent.</param>
    /// <param name="dueDateFrom">Filter events with due date on or after this date.</param>
    /// <param name="dueDateTo">Filter events with due date on or before this date.</param>
    /// <param name="pageNumber">Page number (1-based). Defaults to 1.</param>
    /// <param name="pageSize">Page size. Defaults to 50, max 100.</param>
    /// <param name="dataverseService">Dataverse service for querying.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paginated list of events.</returns>
    internal static async Task<IResult> GetEventsAsync(
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
        Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver callerResolver = null!,
        Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService identity = null!,
        ILogger<Program> logger = null!,
        CancellationToken ct = default)
    {
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

        // Task 097 review F4: Dataverse has no $skip; pages are cut from the first pageNumber*pageSize rows, which may not
        // exceed its $top ceiling. Refuse a page beyond that window explicitly rather than return an empty page.
        if ((long)pageNumber * pageSize > DataverseWebApiService.MaxEventQueryRows)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pageNumber"] = [$"pageNumber × pageSize must not exceed {DataverseWebApiService.MaxEventQueryRows}; narrow the filter instead."]
            });
        }

        // Map string status alias (used by Copilot) to the live Dataverse statusCode. String wins over integer if both
        // provided. Task 097: the former 3/5/6 do not exist in the option set, so these filters never matched a row.
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

        // Validate priority if provided
        if (priority.HasValue && !EventPriority.IsDefined(priority.Value))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["priority"] = [PriorityValidationMessage]
            });
        }

        logger.LogInformation(
            "Retrieving events. RegardingType={RegardingType}, RegardingId={RegardingId}, EventTypeId={EventTypeId}, " +
            "StatusCode={StatusCode}, Priority={Priority}, DueDateFrom={DueDateFrom}, DueDateTo={DueDateTo}, " +
            "Page={Page}, PageSize={PageSize}",
            regardingRecordType, regardingRecordId, eventTypeId, statusCode, priority,
            dueDateFrom, dueDateTo, pageNumber, pageSize);

        try
        {
            // Security review: the list is the caller's view, not the application's. Resolve the caller's
            // systemuser, then run the query IMPERSONATED as them (ADR-028 A5 — the workforce list seam, the same one
            // PortfolioService and OfficeSearchService use), so Dataverse itself drops every event they may not read.
            // Round 6 (F6): the CallerResolution contract — no oid → 401; an oid that is no systemuser → 403. Never an
            // app-only list.
            var caller = await ResolveCallerAsync(callerResolver, httpContext, "list events", logger, ct);
            if (caller.Refusal is not null)
                return caller.Refusal;
            var systemUserId = caller.SystemUserId;

            // "Mine": owned by the caller OR assigned to the caller's linked contact. BFF-created events are owned by a
            // business-unit TEAM (I-6, round 6), never by the person, so the person is found through sprk_assignedto
            // (task 152 S1). Known gap: a caller with no linked contact does not see their own BFF-created events here
            // (needs sprk_createdbyperson — UAC-r2 task 146).
            Guid? linkedContact = null;
            try
            {
                linkedContact = (await identity.ResolveAsync(systemUserId, ct)).ContactId;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[EVENTS] Linked contact for {SystemUserId} could not be resolved; listing owned events only.", systemUserId);
            }

            var skip = (pageNumber - 1) * pageSize;
            var (entities, totalCount) = await dataverseService.QueryEventsAsync(new EventQueryFilter
            {
                RegardingRecordType = regardingRecordType,
                RegardingRecordId = regardingRecordId,
                EventTypeId = eventTypeId,
                StatusCode = statusCode,
                Priority = priority,
                DueDateFrom = dueDateFrom,
                DueDateTo = dueDateTo,
                Skip = skip,
                Top = pageSize,
                OwnerUserId = systemUserId,
                AssignedToContactId = linkedContact is { } c && c != Guid.Empty ? c : null,
                ImpersonateSystemUserId = systemUserId,
            }, ct);
            var events = (Items: entities.Select(MapEntityToDto).ToArray(), TotalCount: totalCount);
            var response = new EventListResponse
            {
                Items = events.Items,
                TotalCount = events.TotalCount,
                PageSize = pageSize,
                PageNumber = pageNumber
            };

            logger.LogDebug(
                "Returning {Count} events (page {Page} of {TotalPages}, total {TotalCount})",
                events.Items.Length, pageNumber, response.TotalPages, events.TotalCount);

            return TypedResults.Ok(response);
        }
        catch (EventRegardingResolutionException rex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["regardingRecordType"] = [rex.Message] });
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
    /// Gets a single event by its ID.
    /// </summary>
    /// <param name="id">The event ID.</param>
    /// <param name="dataverseService">Dataverse service for querying.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The event if found, or 404 ProblemDetails if not found.</returns>
    private static async Task<IResult> GetEventByIdAsync(
        Guid id,
        IEventDataverseService dataverseService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("Retrieving event. EventId={EventId}", id);

        try
        {
            var eventDto = await GetEventByIdFromDataverseAsync(
                dataverseService,
                id,
                ct);

            if (eventDto is null)
            {
                logger.LogDebug("Event not found. EventId={EventId}", id);

                return Results.Problem(
                    detail: $"Event with ID '{id}' was not found.",
                    statusCode: 404,
                    title: "Event Not Found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
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
    /// Creates a new event.
    /// </summary>
    /// <param name="request">The create event request.</param>
    /// <param name="dataverseService">Dataverse service for creating records.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 Created with event details on success, or 400 ProblemDetails if validation fails.</returns>
    /// <remarks>
    /// Internal (not private) so the test assembly (InternalsVisibleTo) runs the real handler: the S1 Assigned-To
    /// default and the owner-event publish are pinned by executing this site, not by reading its source (task 152
    /// verifier round 1, items 2 and 7).
    /// </remarks>
    internal static async Task<IResult> CreateEventAsync(
        [FromBody] ApiCreateEventRequest request,
        IEventDataverseService dataverseService,
        IMembershipEventPublisher membershipEventPublisher,
        Spaarke.Dataverse.IGenericEntityService genericEntityService,
        Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver callerResolver,
        Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService identity,
        Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver coreAncestors,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownershipResolver,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // Validate required fields (Subject is always required)
        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Subject"] = ["Subject is required."]
            });
        }

        // Validate priority if provided
        if (request.Priority.HasValue && !EventPriority.IsDefined(request.Priority.Value))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Priority"] = [PriorityValidationMessage]
            });
        }

        // Validate regardingRecordType if provided
        if (request.RegardingRecordType.HasValue && (request.RegardingRecordType < 0 || request.RegardingRecordType > 7))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["RegardingRecordType"] = ["Regarding record type must be between 0 and 7."]
            });
        }

        // Task 097 review F3d: the type and the id travel together — one without the other cannot be written consistently.
        if (request.RegardingRecordType.HasValue != request.RegardingRecordId.HasValue)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Regarding"] = ["RegardingRecordType and RegardingRecordId must be supplied together."]
            });
        }

        // Validate date range if both scheduled dates provided
        if (request.ScheduledStart.HasValue && request.ScheduledEnd.HasValue &&
            request.ScheduledStart > request.ScheduledEnd)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ScheduledEnd"] = ["Scheduled end date must be on or after scheduled start date."]
            });
        }

        logger.LogInformation(
            "Creating event. Subject={Subject}, EventTypeId={EventTypeId}, RegardingRecordType={RegardingRecordType}",
            request.Subject, request.EventTypeId, request.RegardingRecordType);

        try
        {
            // UAC-r2 task 152 / owner decision S1: the create is app-only, so Created By is the BFF application user
            // and cannot say who the event is FOR. The acting user's LINKED contact (task 141) is written to
            // sprk_assignedto — never an email match; no link → blank + todo_unassigned.
            // Round 6 (F6): the CallerResolution contract — no oid → 401; an oid that is no systemuser → 403. The
            // create needs the caller for Assigned To (S1) AND for the owner when no parent is named (I-6).
            var caller = await ResolveCallerAsync(callerResolver, httpContext, "create an event", logger, ct);
            if (caller.Refusal is not null)
                return caller.Refusal;

            var assignedToContactId = await ResolveActingUserContactAsync(identity, caller.SystemUserId, logger, ct);

            // Review H2 (registry I-1): the FR-26 core-ancestor stamp, derived BEFORE the write; fail closed.
            var stamps = await DeriveAncestorStampsAsync(coreAncestors, request.RegardingRecordType, request.RegardingRecordId, logger, ct);

            // Round 6 (F1, registry I-6): the owner, from the I-6 owner (RecordOwnershipResolver) — record-first: the
            // regarding parent's business unit, else the acting user's. Without it this app-only create is owned by
            // the application user in the ROOT business unit, where the child-BU user who created it cannot read it.
            // Unresolved → refuse; never app-owned.
            var ownerTeamId = await ownershipResolver.ResolveOwningTeamAsync(
                new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext
                {
                    TargetEntityLogicalName = request.RegardingRecordType is { } parentType
                        ? Spaarke.Dataverse.RegardingRecordType.GetEntityLogicalName(parentType)
                        : null,
                    TargetRecordId = request.RegardingRecordId,
                    CallerSystemUserId = caller.SystemUserId,
                },
                ct);
            if (ownerTeamId is null || ownerTeamId == Guid.Empty)
            {
                logger.LogWarning("Refusing event create: no owner team resolved (I-6). Regarding={Type}/{Id}",
                    request.RegardingRecordType, request.RegardingRecordId);
                const string ownerUnresolved = Sprk.Bff.Api.Api.Office.Errors.OfficeErrorCodes.RecordOwnerUnresolved;
                return TypedResults.Problem(
                    statusCode: Sprk.Bff.Api.Api.Office.Errors.OfficeErrorCodes.GetStatusCode(ownerUnresolved),
                    type: Sprk.Bff.Api.Api.Office.Errors.OfficeErrorCodes.GetTypeUri(ownerUnresolved),
                    title: Sprk.Bff.Api.Api.Office.Errors.OfficeErrorCodes.GetTitle(ownerUnresolved),
                    detail: "The event could not be assigned to a business unit's team, so it was not created.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = ownerUnresolved,
                        ["traceId"] = httpContext.TraceIdentifier
                    });
            }

            var (eventId, createdOn) = await CreateEventInDataverseAsync(
                dataverseService,
                request,
                assignedToContactId,
                stamps,
                ownerTeamId.Value,
                logger,
                ct);

            var response = new CreateEventResponse(eventId, request.Subject, createdOn);

            logger.LogInformation(
                "Event created successfully. EventId={EventId}, Subject={Subject}",
                eventId, request.Subject);

            // R3 task 082 — FR-2P2.6 + Q2 fire-and-forget membership event, describing the row's REAL owner
            // (UAC-r2 task 152, ADR-034 A3): the create set ownerid to the I-6 team resolved above, so the event is
            // PersonIdType=Team, PersonId=that team — the key MembershipReconciliationJob builds for this row (the
            // POST /api/v1/documents shape). When MembershipEventPublisherOptions.Enabled=false (default), the Null
            // peer logs + returns.
            _ = MembershipOwnerEvents.PublishOwnerAddedAsync(
                membershipEventPublisher,
                genericEntityService,
                "sprk_event",
                eventId,
                new Microsoft.Xrm.Sdk.EntityReference("team", ownerTeamId.Value),
                httpContext.TraceIdentifier,
                logger,
                ct);

            return TypedResults.Created($"/api/v1/events/{eventId}", response);
        }
        catch (AncestorDerivationFailedException adx)
        {
            logger.LogError("Event create refused: core-ancestor derivation failed (FR-26 / NFR-01): {Error}", adx.Message);
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Core-ancestor derivation failed",
                detail: "The event was not created: the records it refers to could not be resolved to their matter/project.");
        }
        catch (EventRegardingResolutionException rex)
        {
            // Task 097 review F3c: an unresolvable regarding parent is a client error and nothing was written.
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["Regarding"] = [rex.Message] });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating event. Subject={Subject}", request.Subject);

            return Results.Problem(
                detail: "An error occurred while creating the event",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Updates an existing event.
    /// </summary>
    /// <param name="id">The event ID.</param>
    /// <param name="request">The update event request.</param>
    /// <param name="dataverseService">Dataverse service for updating records.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated event on success, 404 if not found, or 400 if validation fails.</returns>
    internal static async Task<IResult> UpdateEventAsync(
        Guid id,
        [FromBody] ApiUpdateEventRequest request,
        IEventDataverseService dataverseService,
        Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver coreAncestors,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // Validate Subject if provided (cannot be empty)
        if (request.Subject is not null && string.IsNullOrWhiteSpace(request.Subject))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Subject"] = ["Subject cannot be empty."]
            });
        }

        // Validate priority if provided
        if (request.Priority.HasValue && !EventPriority.IsDefined(request.Priority.Value))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Priority"] = [PriorityValidationMessage]
            });
        }

        // Validate regardingRecordType if provided
        if (request.RegardingRecordType.HasValue && (request.RegardingRecordType < 0 || request.RegardingRecordType > 7))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["RegardingRecordType"] = ["Regarding record type must be between 0 and 7."]
            });
        }

        // Task 097 review F3d: the type and the id travel together — one without the other cannot be written consistently.
        if (request.RegardingRecordType.HasValue != request.RegardingRecordId.HasValue)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Regarding"] = ["RegardingRecordType and RegardingRecordId must be supplied together."]
            });
        }

        // Validate statusCode if provided — against the live option set (task 097), not a 1..7 range.
        if (request.StatusCode.HasValue && !EventStatusCode.IsDefined(request.StatusCode.Value))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["StatusCode"] = [
                    "Status code must be a sprk_event status reason: " +
                    string.Join(", ", EventStatusCode.All.Select(s => $"{s.Label} ({s.Value})")) + "."]
            });
        }

        // Validate date range if both scheduled dates provided
        if (request.ScheduledStart.HasValue && request.ScheduledEnd.HasValue &&
            request.ScheduledStart > request.ScheduledEnd)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ScheduledEnd"] = ["Scheduled end date must be on or after scheduled start date."]
            });
        }

        logger.LogInformation("Updating event. EventId={EventId}", id);

        try
        {
            // Check if event exists
            var existing = await GetEventByIdFromDataverseAsync(dataverseService, id, ct);

            if (existing is null)
            {
                logger.LogDebug("Event not found for update. EventId={EventId}", id);

                return Results.Problem(
                    detail: $"Event with ID '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Event Not Found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            // Review H2: a re-parent derives the NEW parent's core ancestors before the write; fail closed.
            var stamps = await DeriveAncestorStampsAsync(coreAncestors, request.RegardingRecordType, request.RegardingRecordId, logger, ct);

            await UpdateEventInDataverseAsync(
                dataverseService,
                id,
                request,
                stamps,
                logger,
                ct);

            // Fetch updated record to return
            var updated = await GetEventByIdFromDataverseAsync(dataverseService, id, ct);

            // Fallback to computed DTO if refetch fails
            var updatedDto = updated ?? CreateUpdatedEventDto(existing, request);

            logger.LogInformation(
                "Event updated successfully. EventId={EventId}, Subject={Subject}",
                id, updatedDto.Subject);

            return TypedResults.Ok(updatedDto);
        }
        catch (AncestorDerivationFailedException adx)
        {
            logger.LogError("Event update refused: core-ancestor derivation failed (FR-26 / NFR-01): {Error}", adx.Message);
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Core-ancestor derivation failed",
                detail: "The event was not changed: the new parent could not be resolved to its matter/project.");
        }
        catch (EventRegardingResolutionException rex)
        {
            // Task 097 review F3c: refused before the write — the old regarding stays intact, never half-replaced.
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["Regarding"] = [rex.Message] });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating event. EventId={EventId}", id);

            return Results.Problem(
                detail: "An error occurred while updating the event",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Soft deletes an event by setting its status to Canceled.
    /// </summary>
    /// <param name="id">The event ID.</param>
    /// <param name="dataverseService">Dataverse service for querying and updating.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content on success, or 404 ProblemDetails if not found.</returns>
    internal static async Task<IResult> DeleteEventAsync(
        Guid id,
        IEventDataverseService dataverseService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("Deleting event (soft delete). EventId={EventId}", id);

        try
        {
            // Check if event exists
            var existing = await GetEventByIdFromDataverseAsync(dataverseService, id, ct);

            if (existing is null)
            {
                logger.LogDebug("Event not found for delete. EventId={EventId}", id);

                return Results.Problem(
                    detail: $"Event with ID '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Event Not Found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            await SoftDeleteEventAsync(dataverseService, id, logger, ct);

            logger.LogInformation("Event soft deleted successfully. EventId={EventId}", id);

            return Results.NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting event. EventId={EventId}", id);

            return Results.Problem(
                detail: "An error occurred while deleting the event",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

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
            CompletedDate = entity.CompletedDate,
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
    /// Soft deletes an event by setting its status to Cancelled — what the DELETE route's own description promises.
    /// </summary>
    /// <remarks>
    /// Soft delete preserves the record in the database for audit trail.
    /// An Event Log entry (action Deleted) is created to track the state transition.
    /// Task 097: the live option set has no "Deleted" status reason; the former value 7 was rejected by Dataverse,
    /// so this route could never succeed.
    /// </remarks>
    private static async Task SoftDeleteEventAsync(
        IEventDataverseService dataverseService,
        Guid id,
        ILogger logger,
        CancellationToken ct)
    {
        await dataverseService.UpdateEventStatusAsync(id, EventStatusCode.Cancelled, null, ct);

        await WriteAuditLogAsync(dataverseService, id, Spaarke.Dataverse.EventLogAction.Deleted,
            "Event was soft-deleted via API", logger, ct);
    }

    /// <summary>
    /// UAC-r2 task 152: the acting user's LINKED contact (task 141 — <c>PersonIdentity.ContactId</c>), or null when the
    /// user has no link or the read fails. Never an email/UPN match.
    /// </summary>
    private static async Task<Guid?> ResolveActingUserContactAsync(
        Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService identity,
        Guid systemUserId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var person = await identity.ResolveAsync(systemUserId, ct);
            if (person.ContactId is { } contactId && contactId != Guid.Empty)
            {
                return contactId;
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

    /// <summary><see cref="Sprk.Bff.Api.Services.Ai.Context.CallerSystemUserResolver"/>'s reason for a FAULTED lookup.</summary>
    internal const string CallerLookupFailedReason = "lookup-failed";

    /// <summary>The caller's systemuser, or the refusal to return instead (round 6, review F6).</summary>
    internal readonly record struct CallerOutcome(Guid SystemUserId, IResult? Refusal);

    /// <summary>
    /// The CallerResolution contract (<see cref="CallerResolution.ResolveObjectId"/> — FinanceAuthorizationFilter,
    /// CreateDocumentAsync): a caller with NO resolvable <c>oid</c> is unauthenticated for this purpose → <b>401</b>;
    /// an <c>oid</c> that maps to no Dataverse systemuser → <b>403</b>; a lookup that FAULTED → <b>503</b> (retryable) —
    /// fail closed either way, never an app-only fallback. The systemuser comes from the one injectable resolver (<see cref="Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver"/>),
    /// used by both the list and the create.
    /// </summary>
    internal static async Task<CallerOutcome> ResolveCallerAsync(
        Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver callerResolver,
        HttpContext httpContext,
        string purpose,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(CallerResolution.ResolveObjectId(httpContext.User)))
        {
            return new CallerOutcome(Guid.Empty, Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1"));
        }

        var resolution = await callerResolver.ResolveAsync(httpContext.User, ct);
        if (resolution.IsResolved && Guid.TryParse(resolution.SystemUserId, out var systemUserId) && systemUserId != Guid.Empty)
        {
            return new CallerOutcome(systemUserId, null);
        }

        // Round 7 (R4): a FAULTED lookup is not the answer "you are no Dataverse user". It is retryable — 503 — the same
        // answer-versus-fault split RecordOwnershipResolver makes (a throttled read must not become a refusal that tells
        // the user to fix their account). Still fail closed: nothing is read or written.
        if (resolution.UnresolvedReason == CallerLookupFailedReason)
        {
            logger.LogWarning("[EVENTS] The caller's systemuser lookup faulted; answering 503 to {Purpose}.", purpose);
            return new CallerOutcome(Guid.Empty, Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Service Unavailable",
                detail: "Your account could not be checked right now. Try again shortly.",
                type: "https://tools.ietf.org/html/rfc9110#section-15.6.4"));
        }

        logger.LogWarning("[EVENTS] Caller does not resolve to a systemuser ({Reason}); refusing to {Purpose} (fail closed).",
            resolution.UnresolvedReason, purpose);
        return new CallerOutcome(Guid.Empty, ProblemDetailsHelper.Forbidden(
            EventAccessFilter.DeniedReasonCode,
            $"Your account could not be matched to a Dataverse user, so you cannot {purpose}.",
            httpContext.TraceIdentifier));
    }
    /// <summary>
    /// Creates a new event in Dataverse.
    /// </summary>
    /// <remarks>
    /// Creates the event record and an Event Log entry for the creation.
    /// </remarks>
    private static async Task<(Guid Id, DateTime CreatedOn)> CreateEventInDataverseAsync(
        IEventDataverseService dataverseService,
        ApiCreateEventRequest request,
        Guid? assignedToContactId,
        IReadOnlyList<EventAncestorStamp>? ancestorStamps,
        Guid ownerTeamId,
        ILogger logger,
        CancellationToken ct)
    {
        // Map API request to Dataverse request
        var dataverseRequest = new DataverseCreateEventRequest
        {
            Name = request.Subject,
            Description = request.Description,
            EventTypeId = request.EventTypeId,
            BaseDate = request.ScheduledStart,
            DueDate = request.DueDate,
            Priority = request.Priority,
            RegardingRecordType = request.RegardingRecordType,
            RegardingRecordId = request.RegardingRecordId?.ToString(),
            RegardingRecordName = request.RegardingRecordName,
            AssignedToContactId = assignedToContactId,
            AncestorStamps = ancestorStamps,
            OwnerTeamId = ownerTeamId,
        };

        // Create the event record
        var (id, createdOn) = await dataverseService.CreateEventAsync(dataverseRequest, ct);

        await WriteAuditLogAsync(dataverseService, id, Spaarke.Dataverse.EventLogAction.Created,
            "Event created via API", logger, ct);

        return (id, createdOn);
    }

    /// <summary>
    /// Updates an existing event in Dataverse.
    /// </summary>
    /// <remarks>
    /// Only updates fields that are non-null in the request.
    /// </remarks>
    private static async Task UpdateEventInDataverseAsync(
        IEventDataverseService dataverseService,
        Guid id,
        ApiUpdateEventRequest request,
        IReadOnlyList<EventAncestorStamp>? ancestorStamps,
        ILogger logger,
        CancellationToken ct)
    {
        // Map API request to Dataverse request
        var dataverseRequest = new DataverseUpdateEventRequest
        {
            Name = request.Subject,
            Description = request.Description,
            EventTypeId = request.EventTypeId,
            BaseDate = request.ScheduledStart,
            DueDate = request.DueDate,
            Priority = request.Priority,
            StatusCode = request.StatusCode,
            RegardingRecordType = request.RegardingRecordType,
            RegardingRecordId = request.RegardingRecordId?.ToString(),
            RegardingRecordName = request.RegardingRecordName,
            AncestorStamps = ancestorStamps,
        };

        // Update the event record
        await dataverseService.UpdateEventAsync(id, dataverseRequest, ct);

        // If status changed, create Event Log entry
        if (request.StatusCode.HasValue)
        {
            await WriteAuditLogAsync(dataverseService, id, Spaarke.Dataverse.EventLogAction.Updated,
                $"Event status updated to {EventStatusCode.GetDisplayName(request.StatusCode.Value)}", logger, ct);
        }
    }

    /// <summary>
    /// Creates an updated EventDto by merging existing data with update request.
    /// Used to build the response DTO after Dataverse update completes.
    /// </summary>
    private static EventDto CreateUpdatedEventDto(EventDto existing, ApiUpdateEventRequest request)
    {
        var newStatusCode = request.StatusCode ?? existing.StatusCode;
        var newPriority = request.Priority ?? existing.Priority;
        var newRegardingType = request.RegardingRecordType ?? existing.RegardingRecordType;

        return new EventDto
        {
            Id = existing.Id,
            Subject = request.Subject ?? existing.Subject,
            Description = request.Description ?? existing.Description,
            EventTypeId = request.EventTypeId ?? existing.EventTypeId,
            EventTypeName = existing.EventTypeName, // Cannot update via this request
            RegardingRecordId = request.RegardingRecordId?.ToString() ?? existing.RegardingRecordId,
            RegardingRecordName = request.RegardingRecordName ?? existing.RegardingRecordName,
            RegardingRecordType = newRegardingType,
            RegardingRecordTypeName = newRegardingType.HasValue ? ApiRegardingRecordType.GetDisplayName(newRegardingType.Value) : null,
            BaseDate = existing.BaseDate,
            DueDate = request.DueDate ?? existing.DueDate,
            CompletedDate = existing.CompletedDate,
            StateCode = existing.StateCode,
            StatusCode = newStatusCode,
            Status = EventStatusCode.GetDisplayName(newStatusCode),
            Priority = newPriority,
            PriorityName = newPriority.HasValue ? EventPriority.GetDisplayName(newPriority.Value) : null,
            Source = existing.Source,
            CreatedOn = existing.CreatedOn,
            ModifiedOn = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Marks an event as completed.
    /// </summary>
    /// <param name="id">The event ID.</param>
    /// <param name="dataverseService">Dataverse service for querying and updating.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with action response on success, 400 if invalid transition, or 404 if not found.</returns>
    internal static async Task<IResult> CompleteEventAsync(
        Guid id,
        IEventDataverseService dataverseService,
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

                return Results.Problem(
                    detail: $"Event with ID '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Event Not Found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            // Validate status transition: only from open work (EventStatusCode.IsOpenWork: Draft, Open, On Hold, Reassigned)
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

            // Update status to Completed and set completed date
            await UpdateEventStatusAsync(dataverseService, id, EventStatusCode.Completed, ct);

            var newStatusDisplay = EventStatusCode.GetDisplayName(EventStatusCode.Completed);

            // Create Event Log entry for the state transition
            await WriteAuditLogAsync(
                dataverseService, id, EventLogAction.Completed,
                $"Status changed from {previousStatus} to {newStatusDisplay}", logger, ct);

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
    /// Marks an event as canceled.
    /// </summary>
    /// <param name="id">The event ID.</param>
    /// <param name="dataverseService">Dataverse service for querying and updating.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with action response on success, 400 if invalid transition, or 404 if not found.</returns>
    internal static async Task<IResult> CancelEventAsync(
        Guid id,
        IEventDataverseService dataverseService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("Canceling event. EventId={EventId}", id);

        try
        {
            // Check if event exists
            var existing = await GetEventByIdFromDataverseAsync(dataverseService, id, ct);

            if (existing is null)
            {
                logger.LogDebug("Event not found for cancel action. EventId={EventId}", id);

                return Results.Problem(
                    detail: $"Event with ID '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Event Not Found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            // Validate status transition: only from open work (EventStatusCode.IsOpenWork: Draft, Open, On Hold, Reassigned)
            if (!CanCancelEvent(existing.StatusCode))
            {
                var validStatuses = GetValidStatusesForCancellation();
                logger.LogWarning(
                    "Invalid status transition for cancel. EventId={EventId}, CurrentStatus={CurrentStatus}, ValidStatuses={ValidStatuses}",
                    id, existing.Status, validStatuses);

                return Results.Problem(
                    detail: $"Cannot cancel event with status '{existing.Status}'. " +
                            $"Event can only be canceled when status is one of: {validStatuses}.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Status Transition",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
            }

            var previousStatus = existing.Status;
            var actionTimestamp = DateTime.UtcNow;

            // Update status to Cancelled
            await UpdateEventStatusAsync(dataverseService, id, EventStatusCode.Cancelled, ct);

            var newStatusDisplay = EventStatusCode.GetDisplayName(EventStatusCode.Cancelled);

            // Create Event Log entry for the state transition
            await WriteAuditLogAsync(
                dataverseService, id, EventLogAction.Cancelled,
                $"Status changed from {previousStatus} to {newStatusDisplay}", logger, ct);

            var response = new EventActionResponse(
                Id: id,
                PreviousStatus: previousStatus,
                NewStatus: newStatusDisplay,
                ActionTimestamp: actionTimestamp);

            logger.LogInformation(
                "Event canceled successfully. EventId={EventId}, PreviousStatus={PreviousStatus}, NewStatus={NewStatus}",
                id, previousStatus, response.NewStatus);

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error canceling event. EventId={EventId}", id);

            return Results.Problem(
                detail: "An error occurred while canceling the event",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Checks if an event can be completed based on its current status.
    /// </summary>
    /// <remarks>
    /// Valid from the open-work statuses (<see cref="EventStatusCode.IsOpenWork"/>): Draft, Open, On Hold,
    /// Reassigned. Every other status (Completed, Closed, Cancelled, Transferred, No Further Action) is refused.
    /// Task 097: the former gate allowed the fictional {1,2,3,4}, so a live Open (659490001) event was refused here.
    /// </remarks>
    private static bool CanCompleteEvent(int statusCode) => EventStatusCode.IsOpenWork(statusCode);

    /// <summary>
    /// Checks if an event can be canceled based on its current status.
    /// </summary>
    /// <remarks>
    /// Same open-work set as <see cref="CanCompleteEvent"/>: Draft, Open, On Hold, Reassigned.
    /// </remarks>
    private static bool CanCancelEvent(int statusCode) => EventStatusCode.IsOpenWork(statusCode);

    /// <summary>
    /// The open-work statuses an event may be completed or cancelled from, as a display string.
    /// </summary>
    private static string GetValidStatusesForCompletion() =>
        string.Join(", ", EventStatusCode.All.Where(s => EventStatusCode.IsOpenWork(s.Value)).Select(s => s.Label));

    /// <summary>
    /// Gets the list of valid statuses for cancellation as a display string.
    /// </summary>
    private static string GetValidStatusesForCancellation() => GetValidStatusesForCompletion();

    /// <summary>
    /// Updates an event's status in Dataverse.
    /// </summary>
    /// <remarks>
    /// Updates the statuscode field and, for completion, sets the completeddate.
    /// </remarks>
    private static async Task UpdateEventStatusAsync(
        IEventDataverseService dataverseService,
        Guid id,
        int newStatusCode,
        CancellationToken ct)
    {
        // Set completed date for completion status
        DateTime? completedDate = newStatusCode == EventStatusCode.Completed
            ? DateTime.UtcNow
            : null;

        await dataverseService.UpdateEventStatusAsync(id, newStatusCode, completedDate, ct);
    }

    /// <summary>
    /// Gets all log entries for a specific event.
    /// </summary>
    /// <param name="id">The event ID.</param>
    /// <param name="dataverseService">Dataverse service for querying.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of event log entries, or 404 if event not found.</returns>
    private static async Task<IResult> GetEventLogsAsync(
        Guid id,
        IEventDataverseService dataverseService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("Retrieving event logs. EventId={EventId}", id);

        try
        {
            // Check if event exists first
            var existing = await GetEventByIdFromDataverseAsync(dataverseService, id, ct);

            if (existing is null)
            {
                logger.LogDebug("Event not found for log retrieval. EventId={EventId}", id);

                return Results.Problem(
                    detail: $"Event with ID '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Event Not Found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            // Query event logs from Dataverse
            var logs = await QueryEventLogsAsync(dataverseService, id, ct);

            var response = new EventLogListResponse
            {
                Items = logs,
                TotalCount = logs.Length
            };

            logger.LogDebug("Returning {Count} event log entries. EventId={EventId}", logs.Length, id);

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving event logs. EventId={EventId}", id);

            return Results.Problem(
                detail: "An error occurred while retrieving event logs",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Queries event log entries from Dataverse.
    /// </summary>
    private static async Task<EventLogDto[]> QueryEventLogsAsync(
        IEventDataverseService dataverseService,
        Guid eventId,
        CancellationToken ct)
    {
        var logs = await dataverseService.QueryEventLogsAsync(eventId, ct);

        return logs.Select(log => new EventLogDto(
            Id: log.Id,
            EventId: log.EventId,
            PreviousStatus: GetPreviousStatusFromAction(log.Action),
            NewStatus: EventLogAction.GetDisplayName(log.Action),
            ChangedBy: log.CreatedByName ?? "system",
            ChangedOn: log.CreatedOn,
            Notes: log.Description
        )).ToArray();
    }

    /// <summary>
    /// Derives the previous status display name from the action type.
    /// </summary>
    /// <remarks>
    /// For Created action, there's no previous status.
    /// For other actions, we infer a generic previous state.
    /// </remarks>
    private static string? GetPreviousStatusFromAction(int action) =>
        action == EventLogAction.Created ? null : "(previous)";  // Note: actual previous status tracking would require storing it in the log

    /// <summary>
    /// Writes the <c>sprk_eventlog</c> audit row for a committed event write — the ONE path every handler uses
    /// (create, PUT-with-status, DELETE, complete, cancel). Task 097 review F1.
    /// </summary>
    /// <remarks>
    /// <para><b>Failure policy (explicit and uniform).</b> The event write has already committed when this runs, so an
    /// audit-log failure must NOT turn it into a 500 — a client retry would then duplicate the event (POST) or repeat
    /// the transition. The failure is logged at <b>Error</b> with the stable <see cref="AuditLogWriteFailedEventId"/>
    /// and counted on <c>event_audit_log_write_failures_total</c> (meter <see cref="MeterName"/>), so it is alertable
    /// and never silent. Cancellation still propagates.</para>
    /// <para>Before task 097 three handlers called the log write directly (so a failed audit row became a 500 after the
    /// event was written) and two swallowed it at Warning; every write failed live because the payload named a
    /// <c>sprk_description</c> column sprk_eventlog does not have.</para>
    /// </remarks>
    internal static async Task WriteAuditLogAsync(
        IEventDataverseService dataverseService,
        Guid eventId,
        int action,
        string? description,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            await dataverseService.CreateEventLogAsync(eventId, action, description, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AuditLogWriteFailures.Add(1,
                new KeyValuePair<string, object?>("action", EventLogAction.GetDisplayName(action)));
            logger.LogError(AuditLogWriteFailedEventId, ex,
                "Event audit log write failed AFTER the event write committed. EventId={EventId}, Action={Action}",
                eventId, EventLogAction.GetDisplayName(action));
        }
    }

    /// <summary>
    /// FR-26 core-ancestor stamps for an event's regarding parent (task 097 review H2, registry I-1) — the same call
    /// <c>TaskActionCore</c> makes, through the shared <see cref="Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver"/>,
    /// for writers whose payload is a JSON body (<c>DeriveForHostAsync</c>). Null when no parent is named. A failed
    /// derivation THROWS: an unstamped event would be invisible to everyone whose access comes from the parent's
    /// matter/project (NFR-01 fail closed) — so the write does not happen.
    /// </summary>
    internal static async Task<IReadOnlyList<EventAncestorStamp>?> DeriveAncestorStampsAsync(
        Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver coreAncestors,
        int? regardingRecordType,
        Guid? regardingRecordId,
        ILogger logger,
        CancellationToken ct)
    {
        if (regardingRecordType is not { } type || regardingRecordId is not { } parentId || parentId == Guid.Empty
            || Spaarke.Dataverse.RegardingRecordType.GetEntityLogicalName(type) is not { } parentLogicalName)
            return null;

        var outcome = await coreAncestors.DeriveForHostAsync("sprk_event", parentLogicalName, parentId, ct);
        if (!outcome.Succeeded)
            throw new AncestorDerivationFailedException(outcome.Error ?? "Core-ancestor derivation failed.");

        if (outcome.Unstampable.Count > 0)
            logger.LogWarning("sprk_event cannot carry derived ancestor lookup(s) {Lookups} (FR-26 gap).",
                string.Join(", ", outcome.Unstampable));

        return outcome.Stamps
            .Select(s => new EventAncestorStamp(s.LookupAttribute, s.EntityType, s.RecordId))
            .ToList();
    }

    /// <summary>Core-ancestor derivation failed; the event write is refused (fail closed).</summary>
    internal sealed class AncestorDerivationFailedException : Exception
    {
        public AncestorDerivationFailedException(string message) : base(message) { }
    }
}
