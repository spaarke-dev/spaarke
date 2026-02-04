namespace Sprk.Bff.Api.Api.Events.Dtos;

/// <summary>
/// Request model for updating an existing Event record.
/// </summary>
/// <remarks>
/// Used by PUT /api/v1/events/{id} endpoint.
/// All fields are optional - only specified fields will be updated.
/// </remarks>
/// <param name="Subject">Updated event subject/name.</param>
/// <param name="Description">Updated event description.</param>
/// <param name="EventTypeId">Updated reference to Event Type record.</param>
/// <param name="RegardingRecordId">Updated ID of the regarding/associated record (GUID as string).</param>
/// <param name="RegardingRecordName">Updated display name of the regarding record.</param>
/// <param name="RegardingRecordType">Updated entity type of regarding record (0-7): Project (0), Matter (1), Invoice (2), Analysis (3), Account (4), Contact (5), Work Assignment (6), Budget (7).</param>
/// <param name="ScheduledStart">Updated scheduled start date/time.</param>
/// <param name="ScheduledEnd">Updated scheduled end date/time.</param>
/// <param name="DueDate">Updated due date for the event.</param>
/// <param name="Priority">Updated event priority: Low (0), Normal (1), High (2), Urgent (3).</param>
/// <param name="StatusCode">Updated status code: Draft (1), Planned (2), Open (3), On Hold (4), Completed (5), Cancelled (6), Deleted (7).</param>
public record UpdateEventRequest(
    string? Subject = null,
    string? Description = null,
    Guid? EventTypeId = null,
    Guid? RegardingRecordId = null,
    string? RegardingRecordName = null,
    int? RegardingRecordType = null,
    DateTime? ScheduledStart = null,
    DateTime? ScheduledEnd = null,
    DateTime? DueDate = null,
    int? Priority = null,
    int? StatusCode = null
);
