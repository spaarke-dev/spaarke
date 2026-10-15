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
/// <param name="ScheduledStart">Base date (sprk_basedate) as a calendar date, <c>yyyy-MM-dd</c> — a timestamp is refused with 400 (task 098: the column is Date Only).</param>
/// <param name="ScheduledEnd">Scheduled end as a calendar date, <c>yyyy-MM-dd</c>; validated against ScheduledStart, not stored.</param>
/// <param name="DueDate">Due date (sprk_duedate) as a calendar date, <c>yyyy-MM-dd</c> — a timestamp is refused with 400 (task 098: Dataverse itself refuses one for a Date Only column, and converting it would pick a time zone for the caller).</param>
/// <param name="Priority">Event priority — a live sprk_priority value (Spaarke.Dataverse.EventPriority): Low (100000000), Normal (100000001), High (100000002), Urgent (100000003).</param>
public record CreateEventRequest(
    string Subject,
    string? Description = null,
    Guid? EventTypeId = null,
    Guid? RegardingRecordId = null,
    string? RegardingRecordName = null,
    int? RegardingRecordType = null,
    DateOnly? ScheduledStart = null,
    DateOnly? ScheduledEnd = null,
    DateOnly? DueDate = null,
    int? Priority = null
);
