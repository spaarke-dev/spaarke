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
        CancellationToken ct = default);

    Task<EventEntity?> GetEventAsync(Guid id, CancellationToken ct = default);
    Task<(Guid Id, DateTime CreatedOn)> CreateEventAsync(CreateEventRequest request, CancellationToken ct = default);
    Task UpdateEventAsync(Guid id, UpdateEventRequest request, CancellationToken ct = default);
    Task UpdateEventStatusAsync(Guid id, int statusCode, DateTime? completedDate = null, CancellationToken ct = default);
    Task<EventLogEntity[]> QueryEventLogsAsync(Guid eventId, CancellationToken ct = default);
    Task<Guid> CreateEventLogAsync(Guid eventId, int action, string? description, CancellationToken ct = default);
    Task<EventTypeEntity[]> GetEventTypesAsync(bool activeOnly = true, CancellationToken ct = default);
    Task<EventTypeEntity?> GetEventTypeAsync(Guid id, CancellationToken ct = default);
}
