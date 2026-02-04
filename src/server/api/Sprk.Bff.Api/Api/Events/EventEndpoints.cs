using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events.Dtos;

// Type aliases to resolve ambiguity between API DTOs and Dataverse models
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;
using ApiUpdateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.UpdateEventRequest;
using ApiRegardingRecordType = Sprk.Bff.Api.Api.Events.Dtos.RegardingRecordType;
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
    /// <param name="statusCode">Filter by status code.</param>
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
        [FromQuery] int? priority,
        [FromQuery] DateTime? dueDateFrom,
        [FromQuery] DateTime? dueDateTo,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        [AsParameters] IDataverseService dataverseService = null!,
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
        IDataverseService dataverseService,
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
    private static async Task<IResult> CreateEventAsync(
        [FromBody] ApiCreateEventRequest request,
        IDataverseService dataverseService,
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
            var (eventId, createdOn) = await CreateEventInDataverseAsync(
                dataverseService,
                request,
                ct);

            var response = new CreateEventResponse(eventId, request.Subject, createdOn);

            logger.LogInformation(
                "Event created successfully. EventId={EventId}, Subject={Subject}",
                eventId, request.Subject);

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
        IDataverseService dataverseService,
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

            await UpdateEventInDataverseAsync(
                dataverseService,
                id,
                request,
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
        IDataverseService dataverseService,
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

            await SoftDeleteEventAsync(dataverseService, id, ct);

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
    /// Queries events from Dataverse with filtering and pagination.
    /// </summary>
    private static async Task<(EventDto[] Items, int TotalCount)> QueryEventsAsync(
        IDataverseService dataverseService,
        int? regardingRecordType,
        string? regardingRecordId,
        Guid? eventTypeId,
        int? statusCode,
        int? priority,
        DateTime? dueDateFrom,
        DateTime? dueDateTo,
        int pageNumber,
        int pageSize,
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
            ct);

        // Map entities to DTOs
        var events = entities.Select(MapEntityToDto).ToArray();
        return (events, totalCount);
    }

    /// <summary>
    /// Gets a single event by ID from Dataverse.
    /// </summary>
    private static async Task<EventDto?> GetEventByIdFromDataverseAsync(
        IDataverseService dataverseService,
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
    /// Soft deletes an event by setting statuscode to Deleted (7).
    /// </summary>
    /// <remarks>
    /// Soft delete preserves the record in the database for audit trail.
    /// An Event Log entry is created to track the state transition.
    /// </remarks>
    private static async Task SoftDeleteEventAsync(
        IDataverseService dataverseService,
        Guid id,
        CancellationToken ct)
    {
        // Set statuscode to Deleted (7)
        await dataverseService.UpdateEventStatusAsync(id, EventStatusCode.Deleted, null, ct);

        // Create Event Log entry for "deleted" transition
        await dataverseService.CreateEventLogAsync(
            id,
            Spaarke.Dataverse.EventLogAction.Deleted,
            "Event was soft-deleted via API",
            ct);
    }

    /// <summary>
    /// Creates a new event in Dataverse.
    /// </summary>
    /// <remarks>
    /// Creates the event record and an Event Log entry for the creation.
    /// </remarks>
    private static async Task<(Guid Id, DateTime CreatedOn)> CreateEventInDataverseAsync(
        IDataverseService dataverseService,
        ApiCreateEventRequest request,
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
            RegardingRecordName = request.RegardingRecordName
        };

        // Create the event record
        var (id, createdOn) = await dataverseService.CreateEventAsync(dataverseRequest, ct);

        // Create Event Log entry for the creation
        await dataverseService.CreateEventLogAsync(
            id,
            Spaarke.Dataverse.EventLogAction.Created,
            "Event created via API",
            ct);

        return (id, createdOn);
    }

    /// <summary>
    /// Updates an existing event in Dataverse.
    /// </summary>
    /// <remarks>
    /// Only updates fields that are non-null in the request.
    /// </remarks>
    private static async Task UpdateEventInDataverseAsync(
        IDataverseService dataverseService,
        Guid id,
        ApiUpdateEventRequest request,
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
            RegardingRecordName = request.RegardingRecordName
        };

        // Update the event record
        await dataverseService.UpdateEventAsync(id, dataverseRequest, ct);

        // If status changed, create Event Log entry
        if (request.StatusCode.HasValue)
        {
            await dataverseService.CreateEventLogAsync(
                id,
                Spaarke.Dataverse.EventLogAction.Updated,
                $"Event status updated to {EventStatusCode.GetDisplayName(request.StatusCode.Value)}",
                ct);
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
    private static async Task<IResult> CompleteEventAsync(
        Guid id,
        IDataverseService dataverseService,
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
    private static async Task<IResult> CancelEventAsync(
        Guid id,
        IDataverseService dataverseService,
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
        IDataverseService dataverseService,
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
        IDataverseService dataverseService,
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
        IDataverseService dataverseService,
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
        IDataverseService dataverseService,
        Guid eventId,
        int action,
        string? description,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation(
            "[EventLog] Creating log entry. EventId={EventId}, Action={Action}, Description={Description}",
            eventId,
            EventLogAction.GetDisplayName(action),
            description ?? "(none)");

        try
        {
            await dataverseService.CreateEventLogAsync(eventId, action, description, ct);
        }
        catch (Exception ex)
        {
            // Log but don't fail the main operation if event log creation fails
            logger.LogWarning(ex, "Failed to create event log entry. EventId={EventId}, Action={Action}", eventId, action);
        }
    }
}
