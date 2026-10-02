namespace Spaarke.Dataverse;

/// <summary>
/// Event management, event logs, and event type operations.
/// Part of the IDataverseService composite (ISP segregation).
/// </summary>
public interface IEventDataverseService
{
    Task<(EventEntity[] Items, int TotalCount)> QueryEventsAsync(
        int? regardingRecordType = null,
        string? regardingRecordId = null,
        Guid? eventTypeId = null,
        int? statusCode = null,
        int? priority = null,
        DateTime? dueDateFrom = null,
        DateTime? dueDateTo = null,
        int skip = 0,
        int top = 50,
        Guid? ownerUserId = null,
        CancellationToken ct = default);

    Task<EventEntity?> GetEventAsync(Guid id, CancellationToken ct = default);
    Task<(Guid Id, DateTime CreatedOn)> CreateEventAsync(CreateEventRequest request, CancellationToken ct = default);
    Task UpdateEventAsync(Guid id, UpdateEventRequest request, CancellationToken ct = default);
    Task UpdateEventStatusAsync(Guid id, int statusCode, DateTime? completedDate = null, CancellationToken ct = default);
    Task<EventLogEntity[]> QueryEventLogsAsync(Guid eventId, CancellationToken ct = default);
    /// <param name="owningTeamId">
    /// The owner team the caller resolved from the event (unified-access-control-r2 task 146: a log row is content of
    /// its event, owned like it). <c>null</c> ONLY when the resolver answered "unchanged" (an event that is not
    /// team-owned) and the row keeps its creator. Required positionally so every caller decides.
    /// </param>
    Task<Guid> CreateEventLogAsync(Guid eventId, int action, string? description, Guid? owningTeamId, CancellationToken ct = default);
    Task<EventTypeEntity[]> GetEventTypesAsync(bool activeOnly = true, CancellationToken ct = default);
    Task<EventTypeEntity?> GetEventTypeAsync(Guid id, CancellationToken ct = default);
}
