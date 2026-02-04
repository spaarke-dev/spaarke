namespace Sprk.Bff.Api.Api.Events.Dtos;

/// <summary>
/// Request model for creating a new Event record.
/// </summary>
/// <remarks>
/// Used by POST /api/v1/events endpoint.
/// Subject is always required; other fields may be required based on Event Type configuration.
/// </remarks>
/// <param name="Subject">Event subject/name (required).</param>
/// <param name="Description">Event description.</param>
/// <param name="EventTypeId">Reference to Event Type record.</param>
/// <param name="RegardingRecordId">ID of the regarding/associated record (GUID as string).</param>
/// <param name="RegardingRecordName">Display name of the regarding record.</param>
/// <param name="RegardingRecordType">Entity type of regarding record (0-7): Project (0), Matter (1), Invoice (2), Analysis (3), Account (4), Contact (5), Work Assignment (6), Budget (7).</param>
/// <param name="ScheduledStart">Scheduled start date/time.</param>
/// <param name="ScheduledEnd">Scheduled end date/time.</param>
/// <param name="DueDate">Due date for the event.</param>
/// <param name="Priority">Event priority: Low (0), Normal (1), High (2), Urgent (3).</param>
public record CreateEventRequest(
    string Subject,
    string? Description = null,
    Guid? EventTypeId = null,
    Guid? RegardingRecordId = null,
    string? RegardingRecordName = null,
    int? RegardingRecordType = null,
    DateTime? ScheduledStart = null,
    DateTime? ScheduledEnd = null,
    DateTime? DueDate = null,
    int? Priority = null
);
