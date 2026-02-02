namespace Sprk.Bff.Api.Api.Events.Dtos;

/// <summary>
/// Request model for creating an Event Log entry.
/// Used internally to track Event state transitions.
/// </summary>
/// <param name="EventId">The ID of the Event this log entry belongs to.</param>
/// <param name="PreviousStatus">The status before the transition (null for creation).</param>
/// <param name="NewStatus">The status after the transition.</param>
/// <param name="Notes">Optional notes about the change.</param>
public record CreateEventLogRequest(
    Guid EventId,
    string? PreviousStatus,
    string NewStatus,
    string? Notes
);
