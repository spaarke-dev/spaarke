using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events.Dtos;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
// Type aliases to resolve ambiguity between API DTOs and Dataverse models
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;
using ApiRegardingRecordType = Sprk.Bff.Api.Api.Events.Dtos.RegardingRecordType;
using ApiUpdateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.UpdateEventRequest;
using DataverseCreateEventRequest = Spaarke.Dataverse.CreateEventRequest;
using DataverseUpdateEventRequest = Spaarke.Dataverse.UpdateEventRequest;

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
            .WithName("GetEventById")
            .WithSummary("Get a single event by ID")
            .WithDescription("Returns the event with the specified ID. Returns 404 if the event does not exist.")
            .Produces<EventDto>(200)
            .Produces(401)  // Unauthorized
            .Produces(404)  // Not Found
            .Produces(500); // Internal Server Error

        // DELETE /api/v1/events/{id} - Soft delete (set status to Canceled)
        group.MapDelete("/{id:guid}", DeleteEventAsync)
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
            .WithName("CreateEvent")
            .WithSummary("Create a new event")
            .WithDescription("Creates a new Event record in Dataverse. Subject is required. " +
                "Returns 201 Created with the new event details on success.")
            .Produces<CreateEventResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict) // task 146: no owner resolvable
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // PUT /api/v1/events/{id} - Update an existing event
        group.MapPut("/{id:guid}", UpdateEventAsync)
            .WithName("UpdateEvent")
            .WithSummary("Update an existing event")
            .WithDescription("Updates an existing Event record in Dataverse. Only specified fields are updated. " +
                "Returns 200 OK with the updated event on success, 404 if not found.")
            .Produces<EventDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict) // task 146: re-file refused
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/v1/events/{id}/complete - Mark event as completed
        group.MapPost("/{id:guid}/complete", CompleteEventAsync)
            .WithName("CompleteEvent")
            .WithSummary("Mark an event as completed")
            .WithDescription("Changes the event status to Completed. " +
                "Can only complete events with status Draft (1), Planned (2), Open (3), or On Hold (4). " +
                "Returns 200 OK with action details on success, 400 if status transition is invalid, 404 if not found.")
            .Produces<EventActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/v1/events/{id}/cancel - Mark event as canceled
        group.MapPost("/{id:guid}/cancel", CancelEventAsync)
            .WithName("CancelEvent")
            .WithSummary("Mark an event as canceled")
            .WithDescription("Changes the event status to Cancelled. " +
                "Can only cancel events with status Draft (1), Planned (2), Open (3), or On Hold (4). " +
                "Returns 200 OK with action details on success, 400 if status transition is invalid, 404 if not found.")
            .Produces<EventActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/v1/events/{id}/logs - Get event log entries
        group.MapGet("/{id:guid}/logs", GetEventLogsAsync)
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
    /// <param name="statusCode">Filter by status code (3=Open, 5=Completed, 6=Cancelled).</param>
    /// <param name="status">String alias for statusCode: "open" (3), "completed" (5), "cancelled" (6). Takes precedence over statusCode.</param>
    /// <param name="priority">Filter by priority (0-3).</param>
    /// <param name="dueDateFrom">Filter events with due date on or after this date.</param>
    /// <param name="dueDateTo">Filter events with due date on or before this date.</param>
    /// <param name="pageNumber">Page number (1-based). Defaults to 1.</param>
    /// <param name="pageSize">Page size. Defaults to 50, max 100.</param>
    /// <param name="dataverseService">Dataverse service for querying.</param>
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

        // Map string status alias (used by Copilot) to Dataverse integer statusCode.
        // "open"→3, "completed"→5, "cancelled"→6. String wins over integer if both provided.
        if (!string.IsNullOrEmpty(status))
        {
            statusCode = status.ToLowerInvariant() switch
            {
                "open" => 3,
                "completed" => 5,
                "cancelled" or "canceled" => 6,
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
        if (priority.HasValue && (priority < 0 || priority > 3))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["priority"] = ["Priority must be between 0 (Low) and 3 (Urgent)."]
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
            // Map Entra OID → Dataverse systemuserid for owner-scoped queries.
            // _ownerid_value in Dataverse is the systemuserid GUID, not the Entra OID.
            Guid? ownerUserId = null;
            var oid = ExtractOid(httpContext);
            if (!string.IsNullOrEmpty(oid))
            {
                ownerUserId = await communicationService.QuerySystemUserByAzureAdOidAsync(oid, ct);
                logger.LogInformation(
                    "[EVENTS] OID {Oid} → Dataverse systemuserid {SystemUserId}",
                    oid, ownerUserId?.ToString() ?? "not found");
            }

            var events = await QueryEventsAsync(
                dataverseService,
                regardingRecordType,
                regardingRecordId,
                eventTypeId,
                statusCode,
                priority,
                dueDateFrom,
                dueDateTo,
                pageNumber,
                pageSize,
                ownerUserId,
                ct);

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
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        Spaarke.Dataverse.IGenericEntityService genericEntityService,
        Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver callerResolver,
        Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService identity,
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
        if (request.Priority.HasValue && (request.Priority < 0 || request.Priority > 3))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Priority"] = ["Priority must be between 0 (Low) and 3 (Urgent)."]
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
            // Task 146 (C10 part 2): the create is app-only, so the event is owned by the team the ONE resolver names —
            // the regarding record's team (the named Secure team for a secure record), else the caller's own business
            // unit team for an event regarding nothing. A refusal writes nothing and is a 409 with a stable reason
            // code; a Dataverse fault during resolution falls to the catch below as a 500 (a fault is not a refusal).
            var owner = await ownership.ResolveOwnerAsync(
                OwnershipContextFor(request.RegardingRecordType, request.RegardingRecordId, httpContext), ct);
            if (!owner.IsOwned)
            {
                logger.LogWarning(
                    "Refused event create: no owner resolved ({Code}: {Reason})", owner.RefusalCode, owner.Reason);
                return OwnerRefusalProblem(owner, "event");
            }

            // UAC-r2 task 152 / owner decision S1: the create is app-only, so Created By is the BFF application user
            // and cannot say who the event is FOR. The acting user's LINKED contact (task 141) is written to
            // sprk_assignedto — never an email match; no link → blank + todo_unassigned.
            var assignedToContactId = await ResolveActingUserContactAsync(
                callerResolver, identity, httpContext, logger, ct);

            var (eventId, createdOn) = await CreateEventInDataverseAsync(
                dataverseService,
                request,
                owner.OwningTeamId!.Value,
                assignedToContactId,
                owner.CreatedByPerson, // task 146 c1-r1 — the caller, recorded as the app-created event's creator person
                ct);

            var response = new CreateEventResponse(eventId, request.Subject, createdOn);

            logger.LogInformation(
                "Event created successfully. EventId={EventId}, Subject={Subject}",
                eventId, request.Subject);

            // R3 task 082 — FR-2P2.6 + Q2 fire-and-forget membership event, describing the row's REAL owner
            // (UAC-r2 task 152, ADR-034 A3). The create is app-only; the owner it wrote is the TEAM the resolver named
            // above (task 146), which the path already holds — so it is passed rather than read back, and the event
            // names that team. (Before task 152 this published the caller's AAD oid under a comment claiming
            // Dataverse defaulted the owner to the OBO caller; it never did.) When
            // MembershipEventPublisherOptions.Enabled=false (default), the Null peer logs + returns.
            _ = MembershipOwnerEvents.PublishOwnerAddedAsync(
                membershipEventPublisher,
                genericEntityService,
                "sprk_event",
                eventId,
                knownOwner: new Microsoft.Xrm.Sdk.EntityReference("team", owner.OwningTeamId!.Value),
                httpContext.TraceIdentifier,
                logger,
                ct);

            return TypedResults.Created($"/api/v1/events/{eventId}", response);
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
    private static async Task<IResult> UpdateEventAsync(
        Guid id,
        [FromBody] ApiUpdateEventRequest request,
        IEventDataverseService dataverseService,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        Spaarke.Core.Auth.AuthorizationService authorization,
        [FromServices] Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe callerAccessProbe,
        HttpContext httpContext,
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
        if (request.Priority.HasValue && (request.Priority < 0 || request.Priority > 3))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Priority"] = ["Priority must be between 0 (Low) and 3 (Urgent)."]
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

        // Validate statusCode if provided
        if (request.StatusCode.HasValue && (request.StatusCode < 1 || request.StatusCode > 7))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["StatusCode"] = ["Status code must be between 1 (Draft) and 7 (Deleted)."]
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
            var existingEntity = await dataverseService.GetEventAsync(id, ct);
            var existing = existingEntity is null ? null : MapEntityToDto(existingEntity);

            if (existing is null)
            {
                logger.LogDebug("Event not found for update. EventId={EventId}", id);

                return Results.Problem(
                    detail: $"Event with ID '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Event Not Found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            // Task 146: a change of regarding is a REPARENT. The owner is re-derived from the event's parents after
            // the change, BEFORE it is written, and reassigned (separately, read back) when it moves — into a secure
            // record's named team, or out of it. A refusal writes nothing.
            //
            // r1 (verifier item 2): the parent changes are EXACTLY the regarding lookups the PATCH writes
            // (UpdateEventRequest.RegardingLookupWrites — the new regarding, and a clear of the previous type's lookup),
            // so the owner can no longer follow a lookup that was never written.
            var dataverseRequest = ToDataverseUpdate(request, CurrentRegardingRecordType(existingEntity!));
            var parentChanges = ParentChangesFor(dataverseRequest);

            if (parentChanges.Count > 0)
            {
                // r1 (verifier items 1/18): a re-file changes who can READ the event (it moves it into or out of a
                // secure record's team), so it is authorized AS THE CALLER before anything is resolved or written:
                // Write on the event, and AppendTo on the record it is filed to. The group is authentication-only.
                var denial = await AuthorizeRefileAsync(authorization, httpContext, id, request, logger, ct);
                if (denial is not null)
                    return denial;
            }

            // The status log row (content of the event) is owned like the event. Its owner is resolved HERE, before
            // anything is written, so a refusal is a 409 with nothing changed (verifier item 9 — it used to be resolved
            // after the update had landed, answering 409 for a write that had already happened). A re-file below
            // replaces it with the event's new owner.
            var (statusLogOwner, statusLogPerson) = request.StatusCode.HasValue
                ? await ResolveEventLogOwnerAsync(ownership, id, httpContext, ct) // throws a refusal BEFORE any write → 409 below
                : ((Guid?)null, (Guid?)null);

            if (parentChanges.Count > 0)
            {
                var oid = CallerResolution.ResolveObjectId(httpContext.User);
                var reparent = await ownership.ReparentAsync(
                    new Sprk.Bff.Api.Services.Dataverse.RecordReparent
                    {
                        EntityLogicalName = "sprk_event",
                        RecordId = id,
                        ParentChanges = parentChanges,
                        CallerObjectId = Guid.TryParse(oid, out var callerOid) ? callerOid : null,
                        // Owner round 10 item 7 (task 146 c1): a change of regarding can move the event OUT of a secure
                        // root — an un-secure, decided by THIS caller's F3 rights before anything is written.
                        SecureExitCaller = Sprk.Bff.Api.Services.Access.SecureRemovalCaller.ForRequest(callerAccessProbe, httpContext),
                    },
                    token => dataverseService.UpdateEventAsync(id, dataverseRequest, token),
                    ct);

                if (reparent.IsRefused)
                {
                    logger.LogWarning(
                        "Refused event re-file. EventId={EventId} ({Code}: {Reason})",
                        id, reparent.RefusalCode, reparent.Reason);
                    return Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.RecordOwnerRefused(
                        reparent, "event", httpContext.TraceIdentifier);
                }

                // The event's owner after the re-file IS its log row's owner — no second resolution after the write.
                // (Unchanged: the re-file touched no ownership parent, so the owner resolved above still holds.)
                if (reparent.IsOwned)
                    statusLogOwner = reparent.OwningTeamId;
            }
            else
            {
                await dataverseService.UpdateEventAsync(id, dataverseRequest, ct);
            }

            // If status changed, create Event Log entry — after any reparent, so it is owned like the event now is.
            if (request.StatusCode.HasValue)
            {
                await dataverseService.CreateEventLogAsync(
                    id,
                    Spaarke.Dataverse.EventLogAction.Updated,
                    $"Event status updated to {EventStatusCode.GetDisplayName(request.StatusCode.Value)}",
                    statusLogOwner,
                    statusLogPerson,
                    ct);
            }

            // Fetch updated record to return
            var updated = await GetEventByIdFromDataverseAsync(dataverseService, id, ct);

            // Fallback to computed DTO if refetch fails
            var updatedDto = updated ?? CreateUpdatedEventDto(existing, request);

            logger.LogInformation(
                "Event updated successfully. EventId={EventId}, Subject={Subject}",
                id, updatedDto.Subject);

            return TypedResults.Ok(updatedDto);
        }
        catch (Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException refused)
        {
            // Task 146 r1 (verifier item 9): the status log row's owner is resolved BEFORE the update, so this refusal
            // means NOTHING was written — the event update and its log row both — and the client may retry safely.
            logger.LogWarning(refused, "Refused event update: its status log row has no owner. Nothing written. EventId={EventId}", id);
            return Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.RecordOwnerRefused(
                refused.RefusalCode, refused.Reason, "event");
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
    private static async Task<IResult> DeleteEventAsync(
        Guid id,
        IEventDataverseService dataverseService,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        ILogger<Program> logger,
        HttpContext httpContext,
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

            await SoftDeleteEventAsync(dataverseService, ownership, id, httpContext, ct);

            logger.LogInformation("Event soft deleted successfully. EventId={EventId}", id);

            return Results.NoContent();
        }
        catch (Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException refused)
        {
            // Task 146: the deletion's log row could not be owned like its event — nothing was written.
            logger.LogWarning(refused, "Refused event delete: no owner for its log row. EventId={EventId}", id);
            return Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.RecordOwnerRefused(
                refused.RefusalCode, refused.Reason, "event deletion");
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
    /// Queries events from Dataverse with filtering and pagination.
    /// </summary>
    private static async Task<(EventDto[] Items, int TotalCount)> QueryEventsAsync(
        IEventDataverseService dataverseService,
        int? regardingRecordType,
        string? regardingRecordId,
        Guid? eventTypeId,
        int? statusCode,
        int? priority,
        DateTime? dueDateFrom,
        DateTime? dueDateTo,
        int pageNumber,
        int pageSize,
        Guid? ownerUserId,
        CancellationToken ct)
    {
        // Calculate skip for pagination
        var skip = (pageNumber - 1) * pageSize;

        // Query events from Dataverse
        var (entities, totalCount) = await dataverseService.QueryEventsAsync(
            regardingRecordType,
            regardingRecordId,
            eventTypeId,
            statusCode,
            priority,
            dueDateFrom,
            dueDateTo,
            skip,
            pageSize,
            ownerUserId,
            ct);

        // Map entities to DTOs
        var events = entities.Select(MapEntityToDto).ToArray();
        return (events, totalCount);
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
    /// Extracts the user's Azure AD Object ID (OID) string from token claims.
    /// Returns null if not available (e.g., app-only token), which means no owner filter is applied.
    /// Note: OID is NOT the Dataverse systemuserid — callers must map via QuerySystemUserByAzureAdOidAsync.
    /// </summary>
    private static string? ExtractOid(HttpContext httpContext)
    {
        return CallerResolution.ResolveObjectId(httpContext.User);
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
    /// Soft deletes an event by setting statuscode to Deleted (7).
    /// </summary>
    /// <remarks>
    /// Soft delete preserves the record in the database for audit trail.
    /// An Event Log entry is created to track the state transition.
    /// </remarks>
    private static async Task SoftDeleteEventAsync(
        IEventDataverseService dataverseService,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        Guid id,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // Task 146: the log row's owner is resolved BEFORE anything is written, so a refusal changes nothing.
        var (logOwner, logPerson) = await ResolveEventLogOwnerAsync(ownership, id, httpContext, ct);

        // Set statuscode to Deleted (7)
        await dataverseService.UpdateEventStatusAsync(id, EventStatusCode.Deleted, null, ct);

        // Create Event Log entry for "deleted" transition
        await dataverseService.CreateEventLogAsync(
            id,
            Spaarke.Dataverse.EventLogAction.Deleted,
            "Event was soft-deleted via API",
            logOwner,
            logPerson,
            ct);
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

    /// <summary>
    /// UAC-r2 task 152: the acting user's LINKED contact (task 141 — <c>PersonIdentity.ContactId</c>), or null when the
    /// caller does not resolve to a systemuser, has no link, or the read fails. Never an email/UPN match.
    /// </summary>
    private static async Task<Guid?> ResolveActingUserContactAsync(
        Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver callerResolver,
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

    /// <summary>
    /// Creates a new event in Dataverse.
    /// </summary>
    /// <remarks>
    /// Creates the event record and an Event Log entry for the creation.
    /// </remarks>
    private static async Task<(Guid Id, DateTime CreatedOn)> CreateEventInDataverseAsync(
        IEventDataverseService dataverseService,
        ApiCreateEventRequest request,
        Guid owningTeamId,
        Guid? assignedToContactId,
        Guid? createdByPersonId,
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
            OwningTeamId = owningTeamId, // task 146 — resolved above; the seam refuses without it
            AssignedToContactId = assignedToContactId,
            CreatedByPersonId = createdByPersonId, // task 146 c1-r1 (owner round 13 item 9)
        };

        // Create the event record
        var (id, createdOn) = await dataverseService.CreateEventAsync(dataverseRequest, ct);

        // Create Event Log entry for the creation
        await dataverseService.CreateEventLogAsync(
            id,
            Spaarke.Dataverse.EventLogAction.Created,
            "Event created via API",
            owningTeamId, // task 146 — owned like the event it logs
            createdByPersonId,
            ct);

        return (id, createdOn);
    }

    /// <summary>
    /// The ownership question for an event filed (or not) to a regarding record (task 146): the regarding record is
    /// the parent when it is an ownership parent (a contact or an account is a relationship, not a parent); with no
    /// parent, the caller's own business unit (I-6).
    /// </summary>
    internal static Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext OwnershipContextFor(
        int? regardingRecordType, Guid? regardingRecordId, HttpContext httpContext)
    {
        var oid = CallerResolution.ResolveObjectId(httpContext.User);
        var parents = new List<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent>();
        if (regardingRecordType is { } type
            && Spaarke.Dataverse.RegardingRecordType.GetEntityLogicalName(type) is { } entity
            && regardingRecordId is { } recordId && recordId != Guid.Empty)
        {
            parents.Add(new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent(entity, recordId));
        }

        return new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext
        {
            Parents = parents,
            CallerObjectId = Guid.TryParse(oid, out var callerOid) ? callerOid : null,
            // Task 146 c1-r1 (owner round 13 item 9): the caller asked for the event the application creates.
            RequestedBy = Sprk.Bff.Api.Services.Dataverse.RecordRequester.OfObjectId(oid),
        };
    }

    /// <summary>
    /// The regarding change an update writes, as a reparent's parent changes (task 146): EXACTLY the entity-specific
    /// lookups the PATCH writes (<see cref="DataverseUpdateEventRequest.RegardingLookupWrites"/>) — the new regarding,
    /// and a clear of the previous type's lookup. Empty when the update does not touch the regarding.
    /// </summary>
    /// <remarks>r1 (verifier item 2): derived from the same request the PATCH is built from, so the owner can only be
    /// re-derived from lookups that are actually written.</remarks>
    internal static IReadOnlyDictionary<string, Microsoft.Xrm.Sdk.EntityReference?> ParentChangesFor(
        DataverseUpdateEventRequest update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var changes = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (recordType, recordId) in update.RegardingLookupWrites())
        {
            var lookup = Spaarke.Dataverse.RegardingRecordType.GetLookupFieldName(recordType)!;
            changes[lookup] = recordId is { } id
                && Spaarke.Dataverse.RegardingRecordType.GetEntityLogicalName(recordType) is { } entity
                    ? new Microsoft.Xrm.Sdk.EntityReference(entity, id)
                    : null;
        }

        return changes;
    }

    /// <summary>
    /// The Dataverse update for an API update request. <paramref name="previousRegardingRecordType"/> is the event's
    /// regarding type before the update (read by the handler), so a type change clears the old type's lookup.
    /// </summary>
    internal static DataverseUpdateEventRequest ToDataverseUpdate(
        ApiUpdateEventRequest request, int? previousRegardingRecordType) => new()
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
        PreviousRegardingRecordType = previousRegardingRecordType,
    };

    /// <summary>
    /// The event's regarding type as it stands: the denormalized <c>sprk_regardingrecordtype</c> when it reads, else the
    /// ONE entity-specific regarding lookup that is populated (null when none or several are — then no lookup is cleared,
    /// and the resolver still sees every lookup the row holds, which keeps a secure one secure: fail closed).
    /// </summary>
    internal static int? CurrentRegardingRecordType(Spaarke.Dataverse.EventEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.RegardingRecordType is { } type)
            return type;

        var populated = new (int Type, Guid? Id)[]
            {
                (Spaarke.Dataverse.RegardingRecordType.Project, entity.RegardingProjectId),
                (Spaarke.Dataverse.RegardingRecordType.Matter, entity.RegardingMatterId),
                (Spaarke.Dataverse.RegardingRecordType.Invoice, entity.RegardingInvoiceId),
                (Spaarke.Dataverse.RegardingRecordType.Analysis, entity.RegardingAnalysisId),
                (Spaarke.Dataverse.RegardingRecordType.Account, entity.RegardingAccountId),
                (Spaarke.Dataverse.RegardingRecordType.Contact, entity.RegardingContactId),
                (Spaarke.Dataverse.RegardingRecordType.WorkAssignment, entity.RegardingWorkAssignmentId),
                (Spaarke.Dataverse.RegardingRecordType.Budget, entity.RegardingBudgetId),
            }
            .Where(x => x.Id is { } id && id != Guid.Empty)
            .ToArray();
        return populated.Length == 1 ? populated[0].Type : null;
    }

    /// <summary>
    /// Authorizes a RE-FILE of an event AS THE CALLER (task 146 r1, verifier items 1 and 18): Write on the event, and —
    /// when the update names a record — AppendTo on that record (the right a lookup onto it costs, the same
    /// <c>entity.associate_document</c> key the record-keyed upload and Office save routes use). A re-file re-derives the
    /// event's owner (into or out of a secure record's named team), so on this authentication-only group an unchecked
    /// re-file would let any signed-in user move any event out of a secure record. <c>null</c> when allowed; otherwise
    /// a 403 ProblemDetails with a reason code. Fails closed: an unanswerable question or a fault denies.
    /// </summary>
    private static async Task<IResult?> AuthorizeRefileAsync(
        Spaarke.Core.Auth.AuthorizationService authorization,
        HttpContext httpContext,
        Guid eventId,
        ApiUpdateEventRequest request,
        ILogger logger,
        CancellationToken ct)
    {
        var userId = CallerResolution.ResolveObjectId(httpContext.User);
        var token = Sprk.Bff.Api.Infrastructure.Auth.TokenHelper.ExtractBearerTokenOrNull(httpContext);

        var checks = new List<(string EntitySet, Guid RecordId, string Operation)> { ("sprk_events", eventId, "write") };
        if (request.RegardingRecordType is { } type && request.RegardingRecordId is { } targetId && targetId != Guid.Empty)
        {
            if (Spaarke.Dataverse.RegardingRecordType.GetEntitySetName(type) is not { } targetSet)
            {
                return Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.Forbidden(
                    "sdap.access.deny.no_target", "The record the event would be filed to cannot be authorized.",
                    httpContext.TraceIdentifier);
            }

            checks.Add((targetSet, targetId, RefileTargetOperation));
        }

        foreach (var (entitySet, recordId, operation) in checks)
        {
            string? denyReason;
            try
            {
                var snapshot = string.IsNullOrEmpty(userId)
                    ? null
                    : await authorization.GetCallerRecordAccessAsync(userId, entitySet, recordId, token, ct);
                denyReason = snapshot is not null && Spaarke.Core.Auth.OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, operation)
                    ? null
                    : "sdap.access.deny.insufficient_rights";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Event re-file authorization faulted on {EntitySet}({RecordId}); denying", entitySet, recordId);
                denyReason = "sdap.access.error.system_failure";
            }

            if (denyReason is not null)
            {
                logger.LogWarning(
                    "Event re-file DENIED: caller may not '{Operation}' on {EntitySet}({RecordId}) ({Reason}). EventId={EventId}",
                    operation, entitySet, recordId, denyReason, eventId);
                return Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.Forbidden(
                    denyReason, "You do not have permission to file this event under that record.", httpContext.TraceIdentifier);
            }
        }

        return null;
    }

    /// <summary>The right a re-file costs on the record the event is filed to: AppendTo (<c>entity.associate_document</c>).</summary>
    internal const string RefileTargetOperation = "entity.associate_document";

    /// <summary>A 409 ProblemDetails for an owner refusal, carrying the stable reason code (task 146).</summary>
    private static IResult OwnerRefusalProblem(Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution refusal, string noun) =>
        Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.RecordOwnerRefused(refusal, noun);

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
    private static async Task<IResult> CompleteEventAsync(
        Guid id,
        IEventDataverseService dataverseService,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        ILogger<Program> logger,
        HttpContext httpContext,
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

            // Validate status transition: Can only complete if status is Draft, Planned, Open, or OnHold
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
    /// Marks an event as canceled.
    /// </summary>
    /// <param name="id">The event ID.</param>
    /// <param name="dataverseService">Dataverse service for querying and updating.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with action response on success, 400 if invalid transition, or 404 if not found.</returns>
    private static async Task<IResult> CancelEventAsync(
        Guid id,
        IEventDataverseService dataverseService,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        ILogger<Program> logger,
        HttpContext httpContext,
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

            // Validate status transition: Can only cancel if status is Draft, Planned, Open, or OnHold
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
            await CreateEventLogAsync(
                dataverseService, ownership, id, EventLogAction.Cancelled,
                $"Status changed from {previousStatus} to {newStatusDisplay}", logger, httpContext, ct);

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
    /// Valid transitions to Completed:
    /// - Draft (1) -> Completed
    /// - Planned (2) -> Completed
    /// - Open (3) -> Completed
    /// - OnHold (4) -> Completed
    ///
    /// Invalid transitions:
    /// - Completed (5) -> Completed (already completed)
    /// - Cancelled (6) -> Completed (cannot complete cancelled event)
    /// - Deleted (7) -> Completed (cannot complete deleted event)
    /// </remarks>
    private static bool CanCompleteEvent(int statusCode) =>
        statusCode is EventStatusCode.Draft
            or EventStatusCode.Planned
            or EventStatusCode.Open
            or EventStatusCode.OnHold;

    /// <summary>
    /// Checks if an event can be canceled based on its current status.
    /// </summary>
    /// <remarks>
    /// Valid transitions to Cancelled:
    /// - Draft (1) -> Cancelled
    /// - Planned (2) -> Cancelled
    /// - Open (3) -> Cancelled
    /// - OnHold (4) -> Cancelled
    ///
    /// Invalid transitions:
    /// - Completed (5) -> Cancelled (cannot cancel completed event)
    /// - Cancelled (6) -> Cancelled (already cancelled)
    /// - Deleted (7) -> Cancelled (cannot cancel deleted event)
    /// </remarks>
    private static bool CanCancelEvent(int statusCode) =>
        statusCode is EventStatusCode.Draft
            or EventStatusCode.Planned
            or EventStatusCode.Open
            or EventStatusCode.OnHold;

    /// <summary>
    /// Gets the list of valid statuses for completion as a display string.
    /// </summary>
    private static string GetValidStatusesForCompletion() =>
        $"{EventStatusCode.GetDisplayName(EventStatusCode.Draft)}, " +
        $"{EventStatusCode.GetDisplayName(EventStatusCode.Planned)}, " +
        $"{EventStatusCode.GetDisplayName(EventStatusCode.Open)}, " +
        $"{EventStatusCode.GetDisplayName(EventStatusCode.OnHold)}";

    /// <summary>
    /// Gets the list of valid statuses for cancellation as a display string.
    /// </summary>
    private static string GetValidStatusesForCancellation() =>
        $"{EventStatusCode.GetDisplayName(EventStatusCode.Draft)}, " +
        $"{EventStatusCode.GetDisplayName(EventStatusCode.Planned)}, " +
        $"{EventStatusCode.GetDisplayName(EventStatusCode.Open)}, " +
        $"{EventStatusCode.GetDisplayName(EventStatusCode.OnHold)}";

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
        catch (Exception ex)
        {
            // Log but don't fail the main operation if event log creation fails
            logger.LogWarning(ex, "Failed to create event log entry. EventId={EventId}, Action={Action}", eventId, action);
        }
    }
}
