namespace Spaarke.Dataverse;

/// <summary>
/// Event management and event log operations.
/// Part of the IDataverseService composite (ISP segregation).
/// </summary>
public interface IEventDataverseService
{
    /// <summary>
    /// APP-ONLY event query — for background callers that legitimately act as the application
    /// (<c>TodoGenerationService</c>). An HTTP endpoint answering a caller MUST use
    /// <see cref="QueryEventsAsCallerAsync"/> instead (unified-access-control-r2 task 159): this method has no
    /// caller, so Dataverse row security never applies to its result.
    /// </summary>
    /// <remarks>
    /// <paramref name="regardingRecordType"/> narrows a <paramref name="regardingRecordId"/> filter to that type's
    /// typed lookup; on its own it needs the type's <c>sprk_recordtype_ref</c> row, which only the caller-scoped
    /// overload takes, so a type without an id is refused here.
    /// </remarks>
    Task<(EventEntity[] Items, int TotalCount)> QueryEventsAsync(
        int? regardingRecordType = null,
        Guid? regardingRecordId = null,
        Guid? eventTypeId = null,
        int? statusCode = null,
        int? priority = null,
        DateTime? dueDateFrom = null,
        DateTime? dueDateTo = null,
        int skip = 0,
        int top = 50,
        Guid? ownerUserId = null,
        IReadOnlyCollection<int>? excludeStatusCodes = null,
        CancellationToken ct = default);

    /// <summary>
    /// The event query run AS the caller (<c>MSCRMCallerID</c> impersonation), so Dataverse applies ownership, role
    /// depth, business unit, teams, sharing and hierarchy inside the query and the count covers only the rows the
    /// caller may read (unified-access-control-r2 task 159, #1098 — the trimmed <c>GET /api/v1/events</c>).
    /// </summary>
    /// <param name="callerSystemUserId">
    /// The caller's Dataverse <c>systemuserid</c>. REQUIRED and non-nullable on purpose: an endpoint cannot reach the
    /// app-only query by omitting it. <see cref="Guid.Empty"/> is refused before anything is sent.
    /// </param>
    /// <param name="regardingRecordTypeRefId">
    /// The <c>sprk_recordtype_ref</c> row of <paramref name="regardingRecordType"/>, required when the type is given
    /// without a <paramref name="regardingRecordId"/> (the type filter is a lookup, not an option set).
    /// </param>
    /// <param name="mine">
    /// The "my events" narrowing (owner decision B, task 097): owner OR assigned contact OR <c>sprk_createdbyperson</c>.
    /// Null = no narrowing (the impersonated query alone trims).
    /// </param>
    Task<(EventEntity[] Items, int TotalCount)> QueryEventsAsCallerAsync(
        Guid callerSystemUserId,
        int? regardingRecordType = null,
        Guid? regardingRecordId = null,
        Guid? regardingRecordTypeRefId = null,
        Guid? eventTypeId = null,
        int? statusCode = null,
        int? priority = null,
        DateTime? dueDateFrom = null,
        DateTime? dueDateTo = null,
        int skip = 0,
        int top = 50,
        EventOwnershipScope? mine = null,
        CancellationToken ct = default);

    Task<EventEntity?> GetEventAsync(Guid id, CancellationToken ct = default);
    Task<(Guid Id, DateTime CreatedOn)> CreateEventAsync(CreateEventRequest request, CancellationToken ct = default);
    Task UpdateEventStatusAsync(Guid id, int statusCode, DateOnly? completedDate = null, CancellationToken ct = default);
    /// <param name="owningTeamId">
    /// The owner team the caller resolved from the event (unified-access-control-r2 task 146: a log row is content of
    /// its event, owned like it). <c>null</c> ONLY when the resolver answered "unchanged" (an event that is not
    /// team-owned) and the row keeps its creator. Required positionally so every caller decides.
    /// </param>
    /// <param name="description">
    /// Not a column: <c>sprk_eventlog</c> has none. It is folded into <c>sprk_eventlogname</c> (cut to 850 characters) by
    /// <c>DataverseWebApiService.BuildCreateEventLogPayload</c> (task 097 review F1).
    /// </param>
    /// <param name="createdByPersonId">
    /// The person whose change the log records (task 146 c1-r1, owner round 13 item 9) — written as
    /// <see cref="RecordCreatorPersonColumn.NavigationProperty"/> because the create is app-only; <c>null</c> when there is
    /// no person.
    /// </param>
    Task<Guid> CreateEventLogAsync(Guid eventId, int action, string? description, Guid? owningTeamId, Guid? createdByPersonId = null, CancellationToken ct = default);
}
