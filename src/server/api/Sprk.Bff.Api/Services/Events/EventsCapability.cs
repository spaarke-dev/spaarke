using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Api.Events.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Workspace;
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;
using DataverseRegardingRecordType = Spaarke.Dataverse.RegardingRecordType;

namespace Sprk.Bff.Api.Services.Events;

/// <summary>
/// The events capability (<see cref="IEventsCapability"/>): the event list, get, create and complete rules, moved here unchanged
/// from <c>Api/Events/EventEndpoints.cs</c> (spaarkeai-mcp-bff-api-r2 task 006), and their authorization.
/// </summary>
/// <remarks>
/// <para><b>Two entry points, one implementation (ADR-008; decision recorded in the project's notes/decisions.md).</b></para>
/// <list type="bullet">
///   <item>The <b>static cores</b> (<see cref="ListCoreAsync"/>, <see cref="GetCoreAsync"/>, <see cref="CreateCoreAsync"/>,
///   <see cref="CompleteCoreAsync"/>) hold the rules. The REST handlers call them after the route's own gates
///   (<c>RequireAuthorization</c> and the endpoint filters) have run, so a REST request makes the same Dataverse calls, in the same
///   order, as before the extraction, and answers the same bytes.</item>
///   <item>The <b>instance methods</b> are the entry point for a caller with no endpoint filter (an MCP tool): they run the same
///   gates first, through the SAME methods the filters call (<see cref="RecordRouteAccessAuthorizationFilter.DecideRouteRecordAsync"/>,
///   <see cref="RecordRouteAccessAuthorizationFilter.DecideDeclaredTargetAsync"/>, <see cref="CreateShapeProblem"/>,
///   <see cref="RegardingTargetAsync"/>), then the cores.</item>
/// </list>
/// <para><b>Logs (ADR-015).</b> Event ids, caller-resolution outcomes, counts and reason codes only; never the subject, the
/// description or a regarding record's name.</para>
/// </remarks>
internal sealed class EventsCapability : IEventsCapability
{
    /// <summary><c>sprk_event</c>'s entity SET (live EntityDefinitions, spaarkedev1 2026-10-03; task 159 notes §0.2).</summary>
    internal const string EventEntitySet = "sprk_events";

    /// <summary>
    /// The Create privilege on <c>sprk_event</c>, by its live name (spaarkedev1 <c>privileges</c>, 2026-10-03 — the table's
    /// schema name is <c>sprk_Event</c>, so the privilege is NOT all lower case; the
    /// <c>prvCreatesprk_Invoice</c> precedent). A misspelt name answers "not held" for every
    /// caller and denies every create, which is why it is pinned by a test.
    /// </summary>
    internal const string CreateEventPrivilege = "prvCreatesprk_Event";

    /// <summary>The create's regarding check: AppendTo on the record the event is filed under (OperationAccessPolicy).</summary>
    internal const string AttachRegardingOperation = "event.attach_regarding";

    /// <summary>The get's check on <c>sprk_events({id})</c> (OperationAccessPolicy "read"); the REST filter names the same constant.</summary>
    internal const string ReadOperation = "read";

    /// <summary>The complete's check on <c>sprk_events({id})</c> (OperationAccessPolicy "write"); the REST filter names the same constant.</summary>
    internal const string WriteOperation = "write";

    /// <summary>The list's refusal for a caller who does not resolve to a Dataverse systemuser.</summary>
    internal const string CallerUnresolvedReasonCode = "sdap.access.deny.caller_unresolved";

    /// <summary>A create whose FR-26 core-ancestor stamp could not be derived: nothing is written.</summary>
    internal const string RegardingStampFailedErrorCode = "events.regarding_stamp_failed";

    /// <summary>The <c>sprk_event</c> table.</summary>
    internal const string EventTable = "sprk_event";

    /// <summary><c>sprk_event.sprk_assignedto</c>: the person the event is FOR (a contact lookup).</summary>
    internal const string AssignedToColumn = "sprk_assignedto";

    /// <summary><c>sprk_event.sprk_regardingrecordname</c> length (live metadata, 2026-10-03).</summary>
    internal const int EventRegardingRecordNameMaxLength = 1000;

    /// <summary><c>sprk_event.sprk_regardingrecordnumber</c> length (live metadata, 2026-10-03).</summary>
    internal const int EventRegardingRecordNumberMaxLength = 100;

    /// <summary>The 400 text for a priority outside the live <c>sprk_priority</c> option set (task 097).</summary>
    internal static readonly string PriorityValidationMessage =
        "Priority must be a sprk_priority value: " +
        string.Join(", ", EventPriority.All.Select(p => $"{p.Label} ({p.Value})")) + ".";

    /// <summary>Meter for the events API (registered in TelemetryModule through <c>EventEndpoints.MeterName</c>).</summary>
    internal const string MeterName = "Sprk.Bff.Api.Events";

    private static readonly System.Diagnostics.Metrics.Meter AuditMeter = new(MeterName);

    private static readonly System.Diagnostics.Metrics.Counter<long> AuditLogWriteFailures =
        AuditMeter.CreateCounter<long>(
            name: "event_audit_log_write_failures_total",
            unit: "{write}",
            description: "sprk_eventlog audit rows that could not be written after the event write committed, by action.");

    /// <summary>Stable log event id for an audit-log write failure (alert on it).</summary>
    internal static readonly EventId AuditLogWriteFailedEventId = new(9701, "EventAuditLogWriteFailed");

    private readonly IEventDataverseService _events;
    private readonly ICommunicationDataverseService _recordTypes;
    private readonly ICallerSystemUserResolver _callerResolver;
    private readonly IIdentityNormalizationService _identity;
    private readonly IGenericEntityService _entities;
    private readonly RecordCreationService _recordCreation;
    private readonly IDataverseUserClient _user;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly CallerRecordAccessProbe _probe;
    private readonly ILogger<EventsCapability> _logger;

    public EventsCapability(
        IEventDataverseService events,
        ICommunicationDataverseService recordTypes,
        ICallerSystemUserResolver callerResolver,
        IIdentityNormalizationService identity,
        IGenericEntityService entities,
        RecordCreationService recordCreation,
        IDataverseUserClient user,
        IRecordOwnershipResolver ownership,
        CallerRecordAccessProbe probe,
        ILogger<EventsCapability> logger)
    {
        _events = events;
        _recordTypes = recordTypes;
        _callerResolver = callerResolver;
        _identity = identity;
        _entities = entities;
        _recordCreation = recordCreation;
        _user = user;
        _ownership = ownership;
        _probe = probe;
        _logger = logger;
    }

    // =============================================================================================================
    // Instance entry points — the gates a filtered REST route gets from its filters, then the cores
    // =============================================================================================================

    /// <inheritdoc />
    public async Task<EventCapabilityResult<EventListResponse>> ListAsync(
        HttpContext? httpContext, EventListQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!DecisionCapability.HasSignedInCaller(httpContext?.User))
        {
            return Refuse<EventListResponse>("list");
        }

        // The impersonated query IS the list's gate (there is no record to check before it): the core runs it AS the caller.
        return await ListCoreAsync(httpContext!, query, _events, _recordTypes, _callerResolver, _identity, _logger, ct);
    }

    /// <inheritdoc />
    public async Task<EventCapabilityResult<EventDto>> GetAsync(HttpContext? httpContext, Guid eventId, CancellationToken ct)
    {
        if (!DecisionCapability.HasSignedInCaller(httpContext?.User))
        {
            return Refuse<EventDto>("get");
        }

        var gate = await RouteRecordGateAsync<EventDto>(httpContext!, ReadOperation, eventId, ct);
        return gate ?? await GetCoreAsync(eventId, _events, _logger, ct);
    }

    /// <inheritdoc />
    public async Task<EventCapabilityResult<CreateEventResponse>> CreateAsync(
        HttpContext? httpContext, ApiCreateEventRequest? request, CancellationToken ct)
    {
        if (!DecisionCapability.HasSignedInCaller(httpContext?.User))
        {
            return Refuse<CreateEventResponse>("create");
        }

        // (1) The shape, no I/O — the same rules as the route's first filter (a 400 is never turned into a 403).
        if (CreateShapeProblem(request) is { } shape)
        {
            return EventCapabilityResult<CreateEventResponse>.InvalidField(shape.Field, shape.Message);
        }

        var valid = request!; // CreateShapeProblem refuses a null body

        // (2) The caller's Create privilege on sprk_event and AppendTo on the regarding record — the route filter's decision.
        var access = await RecordRouteAccessAuthorizationFilter.DecideDeclaredTargetAsync(
            httpContext!,
            _probe,
            AttachRegardingOperation,
            () => RegardingTargetAsync(() => _entities, valid.RegardingRecordType, valid.RegardingRecordId, ct),
            CreateEventPrivilege,
            _logger,
            ct);
        if (access != RecordRouteAccessAuthorizationFilter.DeclaredTargetAccess.Allowed)
        {
            // InsufficientPrivilege keeps its own answer; InsufficientRights, and any answer not known to allow, is the one
            // rights refusal (fail closed), as the REST filter maps it.
            var refusal = access == RecordRouteAccessAuthorizationFilter.DeclaredTargetAccess.InsufficientPrivilege
                ? EventCapabilityResult<CreateEventResponse>.Of(EventCapabilityStatus.InsufficientPrivilege)
                : EventCapabilityResult<CreateEventResponse>.Of(EventCapabilityStatus.Forbidden);
            _logger.LogWarning("Events capability: create refused ({ReasonCode})", refusal.ReasonCode);
            return refusal;
        }

        // (3) The create itself.
        return await CreateCoreAsync(
            valid, httpContext!, _recordCreation, _user, _events, _entities, _callerResolver, _identity, _recordTypes, _logger, ct);
    }

    /// <inheritdoc />
    public async Task<EventCapabilityResult<EventActionResponse>> CompleteAsync(
        HttpContext? httpContext, Guid eventId, CancellationToken ct)
    {
        if (!DecisionCapability.HasSignedInCaller(httpContext?.User))
        {
            return Refuse<EventActionResponse>("complete");
        }

        var gate = await RouteRecordGateAsync<EventActionResponse>(httpContext!, WriteOperation, eventId, ct);
        return gate ?? await CompleteCoreAsync(eventId, httpContext!, _events, _ownership, _callerResolver, _entities, _logger, ct);
    }

    private EventCapabilityResult<T> Refuse<T>(string operation)
        where T : class
    {
        _logger.LogWarning("Events capability: {Operation} refused ({ReasonCode}): no signed-in caller; nothing read",
            operation, DataverseUserClientErrorCodes.UserContextRequired);
        return EventCapabilityResult<T>.Of(EventCapabilityStatus.UserContextRequired);
    }

    /// <summary>
    /// The route filter's fixed-entity-set decision on <c>sprk_events(<paramref name="eventId"/>)</c>: <c>null</c> when allowed,
    /// else the refusal (no Read: the uniform not-found; Read without the right: forbidden).
    /// </summary>
    private async Task<EventCapabilityResult<T>?> RouteRecordGateAsync<T>(
        HttpContext httpContext, string operation, Guid eventId, CancellationToken ct)
        where T : class
    {
        if (eventId == Guid.Empty)
        {
            return EventCapabilityResult<T>.Of(EventCapabilityStatus.NotFound);
        }

        var decision = await RecordRouteAccessAuthorizationFilter.DecideRouteRecordAsync(
            httpContext, _probe, operation, EventEntitySet, eventId, _logger, ct);
        return decision.Access switch
        {
            RecordRouteAccessAuthorizationFilter.RouteRecordAccess.Allowed => null,
            RecordRouteAccessAuthorizationFilter.RouteRecordAccess.InsufficientRights =>
                EventCapabilityResult<T>.Of(EventCapabilityStatus.Forbidden),
            // NoRead, and any answer not known to allow: the uniform not-found (fail closed), as the REST filter maps it.
            _ => EventCapabilityResult<T>.Of(EventCapabilityStatus.NotFound),
        };
    }

    // =============================================================================================================
    // Request shape (no I/O) and the create's authorization target — shared with the route's filters
    // =============================================================================================================

    /// <summary>True when the request names a regarding record (validation guarantees type and id come together).</summary>
    internal static bool CarriesRegarding(int? regardingRecordType, Guid? regardingRecordId) =>
        regardingRecordType.HasValue || (regardingRecordId is { } id && id != Guid.Empty);

    /// <summary>
    /// The create request's shape rules (moved from the handler by task 159; same messages): the field REST names in its 400 and
    /// the message, or <c>null</c> when the shape is valid.
    /// </summary>
    internal static (string Field, string Message)? CreateShapeProblem(ApiCreateEventRequest? request)
    {
        if (request is null)
        {
            return ("body", "A request body is required.");
        }

        // Validate required fields (Subject is always required)
        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            return ("Subject", "Subject is required.");
        }

        // Validate priority if provided — against the LIVE sprk_priority option set (task 097: the former 0..3 range is
        // not an option of the column, so every create carrying a priority was a Dataverse 400).
        if (request.Priority.HasValue && !EventPriority.IsDefined(request.Priority.Value))
        {
            return ("Priority", PriorityValidationMessage);
        }

        // Validate regardingRecordType if provided
        if (request.RegardingRecordType.HasValue && (request.RegardingRecordType < 0 || request.RegardingRecordType > 7))
        {
            return ("RegardingRecordType", "Regarding record type must be between 0 and 7.");
        }

        // Task 159: a regarding is a (type, id) pair — the type names the lookup, the id the record. Half of one
        // cannot be authorized or written, so it is a shape error, not a 403.
        var hasId = request.RegardingRecordId is { } id && id != Guid.Empty;
        if (request.RegardingRecordType.HasValue != hasId)
        {
            return ("RegardingRecordId", "RegardingRecordType and RegardingRecordId must be given together, or not at all.");
        }

        // Validate date range if both scheduled dates provided
        if (request.ScheduledStart.HasValue && request.ScheduledEnd.HasValue && request.ScheduledStart > request.ScheduledEnd)
        {
            return ("ScheduledEnd", "Scheduled end date must be on or after scheduled start date.");
        }

        return null;
    }

    /// <summary>
    /// The regarding record as (entity set, id), or <c>null</c> when the request names none. The set comes from
    /// <see cref="IGenericEntityService.GetEntitySetNameAsync"/> — live metadata, never a pluralized logical name — and the create
    /// binds the SAME set, so the record authorized is the record bound. A fault here is a denial (the decision's try). The entity
    /// service is asked for only when there is a regarding record to resolve.
    /// </summary>
    internal static async ValueTask<(string EntitySet, Guid RecordId)?> RegardingTargetAsync(
        Func<IGenericEntityService> entities, int? regardingRecordType, Guid? regardingRecordId, CancellationToken ct)
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

        var entitySet = await entities().GetEntitySetNameAsync(logicalName, ct);
        return (entitySet, id);
    }

    // =============================================================================================================
    // List
    // =============================================================================================================

    /// <summary>
    /// The events the CALLER may read, with optional filtering: the request shape (no I/O), then the caller the query runs AS
    /// (fail closed: no Dataverse user, no answer), then the impersonated query narrowed to "my events".
    /// </summary>
    internal static async Task<EventCapabilityResult<EventListResponse>> ListCoreAsync(
        HttpContext httpContext,
        EventListQuery query,
        IEventDataverseService dataverseService,
        ICommunicationDataverseService communicationService,
        ICallerSystemUserResolver callerResolver,
        IIdentityNormalizationService identity,
        ILogger logger,
        CancellationToken ct)
    {
        // ── (1) Request shape, no I/O ────────────────────────────────────────────────────────────────────────
        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize < 1 ? 50 : query.PageSize > 100 ? 100 : query.PageSize;
        var statusCode = query.StatusCode;
        var regardingRecordType = query.RegardingRecordType;

        // Map string status alias (used by Copilot) to the live Dataverse statusCode. String wins over integer.
        if (!string.IsNullOrEmpty(query.Status))
        {
            statusCode = query.Status.ToLowerInvariant() switch
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
            return EventCapabilityResult<EventListResponse>.InvalidField(
                "regardingRecordType", "Regarding record type must be between 0 and 7.");
        }

        // Task 159 (OData filter injection, DataverseWebApiService.cs:331): the id is a GUID or the request is
        // refused, so no caller text can reach $filter.
        Guid? regardingId = null;
        if (!string.IsNullOrEmpty(query.RegardingRecordId))
        {
            if (!Guid.TryParse(query.RegardingRecordId, out var parsed) || parsed == Guid.Empty)
            {
                return EventCapabilityResult<EventListResponse>.InvalidField(
                    "regardingRecordId", "Regarding record id must be a GUID.");
            }

            regardingId = parsed;
        }

        // Validate priority if provided — live sprk_priority values (task 097; 0..3 never matched a row).
        if (query.Priority.HasValue && !EventPriority.IsDefined(query.Priority.Value))
        {
            return EventCapabilityResult<EventListResponse>.InvalidField("priority", PriorityValidationMessage);
        }

        // The Web API has no $skip; the page window is read as the first skip + top rows (DataverseWebApiService).
        var skip = (long)(pageNumber - 1) * pageSize;
        if (skip + pageSize > 5000)
        {
            return EventCapabilityResult<EventListResponse>.InvalidField(
                "pageNumber", "pageNumber × pageSize must not exceed 5000.");
        }

        logger.LogInformation(
            "Retrieving events. RegardingType={RegardingType}, RegardingId={RegardingId}, EventTypeId={EventTypeId}, " +
            "StatusCode={StatusCode}, Priority={Priority}, DueDateFrom={DueDateFrom}, DueDateTo={DueDateTo}, " +
            "Page={Page}, PageSize={PageSize}",
            regardingRecordType, regardingId, query.EventTypeId, statusCode, query.Priority,
            query.DueDateFrom, query.DueDateTo, pageNumber, pageSize);

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

            return EventCapabilityResult<EventListResponse>.Of(EventCapabilityStatus.CallerUnresolved);
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

                    return EventCapabilityResult<EventListResponse>.Ok(new EventListResponse
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
                query.EventTypeId,
                statusCode,
                query.Priority,
                query.DueDateFrom,
                query.DueDateTo,
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

            return EventCapabilityResult<EventListResponse>.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving events");
            return EventCapabilityResult<EventListResponse>.Of(EventCapabilityStatus.Failed);
        }
    }

    /// <summary>"My events" (owner decision B): owner OR assigned contact OR created-by person — the caller each time.</summary>
    internal static EventOwnershipScope MyEventsScope(Guid callerSystemUserId, Guid? linkedContactId) =>
        new(callerSystemUserId, linkedContactId is { } c && c != Guid.Empty ? c : null, callerSystemUserId);

    // =============================================================================================================
    // Get
    // =============================================================================================================

    /// <summary>
    /// One event by its id. The caller's Read on <c>sprk_events({id})</c> has been established (the route filter, or
    /// <see cref="GetAsync"/>); the read itself is app-only, as it always was (the G5 pattern).
    /// </summary>
    internal static async Task<EventCapabilityResult<EventDto>> GetCoreAsync(
        Guid id, IEventDataverseService dataverseService, ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("Retrieving event. EventId={EventId}", id);

        try
        {
            var eventDto = await GetEventByIdFromDataverseAsync(dataverseService, id, ct);

            if (eventDto is null)
            {
                logger.LogDebug("Event not found. EventId={EventId}", id);
                return EventCapabilityResult<EventDto>.Of(EventCapabilityStatus.NotFound);
            }

            logger.LogDebug("Returning event. EventId={EventId}", eventDto.Id);
            return EventCapabilityResult<EventDto>.Ok(eventDto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving event. EventId={EventId}", id);
            return EventCapabilityResult<EventDto>.Of(EventCapabilityStatus.Failed);
        }
    }

    /// <summary>Gets a single event by ID from Dataverse.</summary>
    private static async Task<EventDto?> GetEventByIdFromDataverseAsync(
        IEventDataverseService dataverseService, Guid id, CancellationToken ct)
    {
        var entity = await dataverseService.GetEventAsync(id, ct);
        if (entity == null)
            return null;

        return MapEntityToDto(entity);
    }

    /// <summary>Maps a Dataverse EventEntity to an EventDto.</summary>
    private static EventDto MapEntityToDto(EventEntity entity)
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
                ? Sprk.Bff.Api.Api.Events.Dtos.RegardingRecordType.GetDisplayName(entity.RegardingRecordType.Value)
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

    // =============================================================================================================
    // Create
    // =============================================================================================================

    /// <summary>
    /// Creates a new event. The shape is valid and the caller's Create privilege on sprk_event and AppendTo on the regarding
    /// record are established (the route filters, or <see cref="CreateAsync"/>); the row create is the ONE server create,
    /// <see cref="RecordCreationService"/>, as the caller (field-mapping-server-write-path-r1 task 031, FR-19).
    /// </summary>
    /// <remarks>
    /// <para><b>What the service owns</b> (ADR-008 path A): the caller checks on the merged payload, field mapping (when the
    /// table's profile applies), the record-first owner, the I-1 core stamps and I-16 Access Permission derived from what the
    /// event is filed under (task 042b), the creator person, the one app-only create, and the owner membership event (ADR-034
    /// A3).</para>
    /// <para><b>What stays here</b>: the request's own values; the ADR-024 regarding denormalised fields (the record-type row, the
    /// target's name, number and URL — read from the catalog and the target, which the service does not read); the acting
    /// user's linked contact for <c>sprk_assignedto</c> (owner S1; a server-set lookup naming the caller's own contact, so it
    /// costs no AppendTo); and the <c>sprk_eventlog</c> audit row after a successful create.</para>
    /// <para><b>Refusals (NFR-07).</b> A refusal is <see cref="EventCapabilityStatus.Refused"/> with the service's refusal and
    /// step records (the REST route renders them with <c>EventEndpoints.RefusalResult</c>); nothing is written and no audit row
    /// is logged.</para>
    /// </remarks>
    internal static async Task<EventCapabilityResult<CreateEventResponse>> CreateCoreAsync(
        ApiCreateEventRequest request,
        HttpContext httpContext,
        RecordCreationService recordCreation,
        IDataverseUserClient user,
        IEventDataverseService dataverseService,
        IGenericEntityService genericEntityService,
        ICallerSystemUserResolver callerResolver,
        IIdentityNormalizationService identity,
        ICommunicationDataverseService recordTypes,
        ILogger logger,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Creating event. EventTypeId={EventTypeId}, RegardingRecordType={RegardingRecordType}",
            request.EventTypeId, request.RegardingRecordType);

        try
        {
            // The ADR-024 denormalised regarding fields, read here (the catalog and the target are not the service's reads).
            // Runs only after the caller was authorized AppendTo on the regarding record. AppendTo does NOT imply Read, so the
            // app-only name/number read below can copy a record's name onto an event a caller without Read then reads back:
            // a pre-existing disclosure carried over unchanged from REST (task 006 tenant-isolation review F1, filed in
            // notes/defer-issues.md; the fix changes who sees what and needs an owner decision). The I-1 core stamps and the
            // owner are the service's, from the lookups below.
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
                return new EventCapabilityResult<CreateEventResponse>(EventCapabilityStatus.Refused)
                {
                    Refusal = refusal,
                    Steps = outcome.Steps,
                };
            }

            if (outcome.CreatedId is not { } eventId)
            {
                // Neither a row nor a refusal: never a success (fail closed, NFR-01).
                logger.LogError("Event create ended without a decision (correlationId={CorrelationId})", outcome.CorrelationId);
                return EventCapabilityResult<CreateEventResponse>.Of(EventCapabilityStatus.Failed);
            }

            var createdOn = DateTime.UtcNow;
            var response = new CreateEventResponse(eventId, request.Subject, createdOn);

            logger.LogInformation(
                "Event created successfully. EventId={EventId}, Warnings={Warnings}",
                eventId, outcome.Warnings.Count);

            // Event Log entry for the creation. Task 097 review F1: the event row is already committed, so an audit
            // failure must never become a failure (a client retry would create a SECOND event) — it is an Error with a stable
            // event id and a counter (AuditLogWriteFailed), never swallowed silently. Written only after a successful create.
            await WriteCreatedAuditLogAsync(dataverseService, eventId, outcome.OwningTeamId, callerSystemUserId, logger, ct);

            // The owner membership event (R3 task 082, ADR-034 A3, fire-and-forget) is published by the service
            // (RecordCreationService step 12, with the owner team it wrote): none here.
            return EventCapabilityResult<CreateEventResponse>.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating event. EventTypeId={EventTypeId}", request.EventTypeId);
            return EventCapabilityResult<CreateEventResponse>.Of(EventCapabilityStatus.Failed);
        }
    }

    /// <summary>How a <see cref="RecordCreationService"/> refusal reads in the event contract (NFR-07).</summary>
    internal enum CreateRefusalShape
    {
        /// <summary>No owner resolves: the 409 and its owner reason code.</summary>
        OwnerUnresolved,

        /// <summary>An I-1 stamp that cannot be derived or bound: <see cref="RegardingStampFailedErrorCode"/>, nothing written.</summary>
        StampFailed,

        /// <summary>A refusal the route never gave before: the generic failure, which names no record.</summary>
        Generic,
    }

    /// <summary>
    /// The one classification of a create refusal, for REST (<c>EventEndpoints.RefusalResult</c>) and for the capability's
    /// <see cref="EventCapabilityResult{T}.ReasonCode"/>. A stamp refusal includes a derived stamp the table's metadata cannot
    /// bind (task 031 fix round 1, F1-low): the generic non-retryable <see cref="RecordCreationRefusal.NoDecisionCode"/>, told
    /// apart only by the step that refused it — the last step record is <see cref="RecordCreationStep.StampAndInheritance"/>,
    /// <see cref="RecordCreationStepState.Refused"/>. The same code at any other step stays generic.
    /// </summary>
    internal static CreateRefusalShape ClassifyRefusal(RecordCreationRefusal refusal, IReadOnlyList<RecordCreationStepRecord> steps)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        ArgumentNullException.ThrowIfNull(steps);
        return refusal switch
        {
            { Kind: RecordCreationRefusalKind.OwnerUnresolved } => CreateRefusalShape.OwnerUnresolved,
            { Code: RecordCreationRefusal.StampUnderivableCode or RecordCreationRefusal.StampUnavailableCode } =>
                CreateRefusalShape.StampFailed,
            { Kind: RecordCreationRefusalKind.Undecided, Code: RecordCreationRefusal.NoDecisionCode } when RefusedAtTheStamp(steps) =>
                CreateRefusalShape.StampFailed,
            _ => CreateRefusalShape.Generic,
        };
    }

    /// <summary>The reason code REST carries for a create refusal (<see cref="ClassifyRefusal"/>), or <c>null</c> for the generic one.</summary>
    internal static string? RefusalReasonCode(RecordCreationRefusal refusal, IReadOnlyList<RecordCreationStepRecord> steps) =>
        ClassifyRefusal(refusal, steps) switch
        {
            CreateRefusalShape.OwnerUnresolved =>
                refusal.OwnerDecision?.RefusalCode ?? refusal.Code ?? RecordOwnerRefusal.NoOwnerSource,
            CreateRefusalShape.StampFailed => RegardingStampFailedErrorCode,
            _ => null,
        };

    /// <summary>Whether the create was refused at step 10, the I-1 stamp bind (the service's own step record).</summary>
    private static bool RefusedAtTheStamp(IReadOnlyList<RecordCreationStepRecord> steps) =>
        steps.Count > 0 && steps[^1] is { Step: RecordCreationStep.StampAndInheritance, State: RecordCreationStepState.Refused };

    /// <summary>
    /// The caller's field mapping for a create (FR-03, FR-04): the source is inferred from what the row is filed under and
    /// the caller's own Read on it is probed before it is read. <c>null</c> (no mapping, logged) when the probe is not
    /// registered: a mapping that cannot be made as the caller is never made another way, and never refuses the create.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="logger">The caller's logger.</param>
    /// <param name="documentShown">Task 043: the route's answer to the external document narrowing for a document source
    /// (<see cref="RecordCreationMapping.ForCaller"/>); the event create passes none.</param>
    internal static RecordCreationMapping? CallerMappingOf(
        HttpContext httpContext, ILogger logger, Func<Guid, CancellationToken, Task<bool>>? documentShown = null)
    {
        var probe = httpContext.RequestServices?.GetService<CallerRecordAccessProbe>();
        if (probe is null)
        {
            logger.LogError("Field mapping was not asked for: no caller-access probe is registered (the create proceeds unmapped)");
            return null;
        }

        return RecordCreationMapping.ForCaller(
            probe, Sprk.Bff.Api.Infrastructure.Auth.TokenHelper.ExtractBearerTokenOrNull(httpContext), documentShown: documentShown);
    }

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

    /// <summary>
    /// UAC-r2 task 152: the acting user's LINKED contact (task 141 — <c>PersonIdentity.ContactId</c>), or null when the
    /// caller does not resolve to a systemuser, has no link, or the read fails. Never an email/UPN match.
    /// </summary>
    internal static async Task<Guid?> ResolveActingUserContactAsync(
        ICallerSystemUserResolver callerResolver,
        IIdentityNormalizationService identity,
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

    /// <summary>A lookup value in the service's Web API payload: the target table and the record (no entity set to guess).</summary>
    private static Dictionary<string, object?> LookupValue(string relatedTable, Guid recordId) => new(StringComparer.Ordinal)
    {
        ["relatedTable"] = relatedTable,
        ["recordId"] = recordId.ToString("D"),
    };

    /// <summary>
    /// The Web API payload of the new event, in column logical names (what <c>DataverseWebApiService.BuildCreateEventPayload</c>
    /// wrote, minus the columns the service owns: the owner, the creator person, the I-1 stamps and the Access Permission).
    /// Open / Active and Source = User are the create's, as before: the platform default for a new row is Draft.
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

    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: > 0 } && value.Length > maxLength ? value[..maxLength] : value;

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
    /// A second fault, or a permanent one, propagates to the caller: the number is left empty and the create still succeeds.
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
    /// Permission are not resolved here: <see cref="RecordCreationService"/> derives them from the lookups the create binds
    /// (task 031), and a stamp that cannot be derived refuses there (<see cref="ClassifyRefusal"/>).
    /// </summary>
    /// <remarks>
    /// Runs only after the caller was authorized AppendTo on the regarding record. AppendTo does not imply Read, so the
    /// app-only name/number read is a known pre-existing disclosure (task 006 tenant-isolation review F1, filed in
    /// notes/defer-issues.md). The pieces and their posture, after TodoRegardingBuilder:
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

    /// <summary>
    /// The Event Log entry for a successful create. Task 097 review F1: the event row is already committed, so an audit
    /// failure must never become a failure (a client retry would create a SECOND event) — it is an Error with a stable event id
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
                EventLogAction.Created,
                "Event created via API",
                owningTeamId, // task 146 — owned like the event it logs
                createdByPersonId,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            ReportAuditLogFailure(logger, ex, eventId, EventLogAction.Created);
        }
    }

    // =============================================================================================================
    // Complete
    // =============================================================================================================

    /// <summary>
    /// Marks an event as completed. The caller's Write on <c>sprk_events({id})</c> has been established (the route filter, or
    /// <see cref="CompleteAsync"/>); the status-transition check runs AFTER it, because it would otherwise disclose the state of
    /// an event the caller may not see. The read and the write are app-only, as they always were (the G5 pattern).
    /// </summary>
    internal static async Task<EventCapabilityResult<EventActionResponse>> CompleteCoreAsync(
        Guid id,
        HttpContext httpContext,
        IEventDataverseService dataverseService,
        IRecordOwnershipResolver ownership,
        ICallerSystemUserResolver callerResolver,
        IGenericEntityService genericEntityService,
        ILogger logger,
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
                return EventCapabilityResult<EventActionResponse>.Of(EventCapabilityStatus.NotFound);
            }

            // Validate status transition: only from open work (EventStatusCode.IsOpenWork — OWNER DECISION A).
            if (!CanCompleteEvent(existing.StatusCode))
            {
                var validStatuses = GetValidStatusesForCompletion();
                logger.LogWarning(
                    "Invalid status transition for complete. EventId={EventId}, CurrentStatus={CurrentStatus}, ValidStatuses={ValidStatuses}",
                    id, existing.Status, validStatuses);

                return new EventCapabilityResult<EventActionResponse>(EventCapabilityStatus.InvalidTransition)
                {
                    Message = $"Cannot complete event with status '{existing.Status}'. " +
                              $"Event can only be completed when status is one of: {validStatuses}.",
                };
            }

            var previousStatus = existing.Status;
            var actionTimestamp = DateTime.UtcNow;

            // Task 098: sprk_completeddate is a calendar date — today in the completing user's own time zone, not the
            // UTC date (a 23:49 Eastern completion was dated the next day). See EventCompletionDate.
            var completedDate = await EventCompletionDate.ForCallerAsync(
                httpContext.User, callerResolver, genericEntityService,
                httpContext.RequestServices?.GetService<TimeProvider>() ?? TimeProvider.System, logger, ct);

            // Update status to Completed and set completed date
            await dataverseService.UpdateEventStatusAsync(id, EventStatusCode.Completed, completedDate, ct);

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

            return EventCapabilityResult<EventActionResponse>.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error completing event. EventId={EventId}", id);
            return EventCapabilityResult<EventActionResponse>.Of(EventCapabilityStatus.Failed);
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

    /// <summary>Gets the list of valid statuses for completion as a display string.</summary>
    private static string GetValidStatusesForCompletion() =>
        string.Join(", ", EventStatusCode.All.Where(s => EventStatusCode.IsOpenWork(s.Value)).Select(s => s.Label));

    /// <summary>Creates an Event Log entry for a state transition.</summary>
    private static async Task CreateEventLogAsync(
        IEventDataverseService dataverseService,
        IRecordOwnershipResolver ownership,
        Guid eventId,
        int action,
        string? description,
        ILogger logger,
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
    /// Task 146: an event log row is content of its event — owned like it (the named Secure team's for a secure
    /// event); <c>Team</c> is <c>null</c> when the event is not team-owned (the row keeps its creator, as the event did).
    /// c1-r1 (owner round 13 item 9): <c>Person</c> is the caller whose change the row logs, recorded because the write
    /// is app-only.
    /// </summary>
    /// <exception cref="RecordOwnerUnresolvedException">No owner resolves.</exception>
    private static async Task<(Guid? Team, Guid? Person)> ResolveEventLogOwnerAsync(
        IRecordOwnershipResolver ownership, Guid eventId, HttpContext httpContext, CancellationToken ct)
    {
        var owner = await ownership.ResolveOwnerAsync(
            RecordOwnershipContext.ContentOf("sprk_event", eventId) with
            {
                RequestedBy = RecordRequester.OfObjectId(CallerResolution.ResolveObjectId(httpContext.User)),
            },
            ct);
        if (owner.IsRefused)
            throw new RecordOwnerUnresolvedException("sprk_eventlog", owner);
        return (owner.IsOwned ? owner.OwningTeamId : null, owner.CreatedByPerson);
    }

    /// <summary>
    /// An audit-log write that failed AFTER its event write committed: an Error with the stable
    /// <see cref="AuditLogWriteFailedEventId"/> and one <c>event_audit_log_write_failures_total</c> increment, tagged with
    /// the action — alertable, never a failure that would invite a retry of the committed write.
    /// </summary>
    private static void ReportAuditLogFailure(ILogger logger, Exception ex, Guid eventId, int action)
    {
        AuditLogWriteFailures.Add(1, new KeyValuePair<string, object?>("action", EventLogAction.GetDisplayName(action)));
        logger.LogError(AuditLogWriteFailedEventId, ex,
            "Event audit-log row could not be written after the event write committed. EventId={EventId}, Action={Action}",
            eventId, EventLogAction.GetDisplayName(action));
    }
}
