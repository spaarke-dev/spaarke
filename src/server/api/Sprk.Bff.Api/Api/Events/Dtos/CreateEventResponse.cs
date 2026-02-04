namespace Sprk.Bff.Api.Api.Events.Dtos;

/// <summary>
/// Response model for successfully created Event record.
/// </summary>
/// <remarks>
/// Used by POST /api/v1/events endpoint.
/// Returns the created event's ID, subject, and creation timestamp.
/// </remarks>
/// <param name="Id">Unique identifier of the created event (sprk_eventid).</param>
/// <param name="Subject">Event subject/name as provided in the request.</param>
/// <param name="CreatedOn">Timestamp when the event was created.</param>
public record CreateEventResponse(
    Guid Id,
    string Subject,
    DateTime CreatedOn
);
