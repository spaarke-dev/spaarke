namespace Sprk.Bff.Api.Api.Events.Dtos;

/// <summary>
/// Response DTO for event action operations (complete, cancel).
/// </summary>
/// <remarks>
/// Returned by POST /api/v1/events/{id}/complete and POST /api/v1/events/{id}/cancel endpoints.
/// Contains the event ID, previous status, new status, and timestamp of the action.
/// </remarks>
/// <param name="Id">The event ID.</param>
/// <param name="PreviousStatus">The status before the action (e.g., "Open", "On Hold").</param>
/// <param name="NewStatus">The status after the action (e.g., "Completed", "Cancelled").</param>
/// <param name="ActionTimestamp">When the action was performed.</param>
public record EventActionResponse(
    Guid Id,
    string PreviousStatus,
    string NewStatus,
    DateTime ActionTimestamp
);
