namespace Sprk.Bff.Api.Api.Events.Dtos;

/// <summary>
/// Data transfer object for Event Log records.
/// Represents an audit entry for Event state transitions.
/// </summary>
/// <param name="Id">The Event Log record ID.</param>
/// <param name="EventId">The ID of the Event this log entry belongs to.</param>
/// <param name="PreviousStatus">The status before the transition (null for creation).</param>
/// <param name="NewStatus">The status after the transition.</param>
/// <param name="ChangedBy">The user who made the change.</param>
/// <param name="ChangedOn">When the change occurred.</param>
/// <param name="Notes">Optional notes about the change.</param>
public record EventLogDto(
    Guid Id,
    Guid EventId,
    string? PreviousStatus,
    string NewStatus,
    string? ChangedBy,
    DateTime ChangedOn,
    string? Notes
);
