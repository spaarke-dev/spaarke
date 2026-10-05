namespace Sprk.Bff.Api.Api.Events.Dtos;

/// <summary>
/// DTO representing an Event record from Dataverse.
/// </summary>
/// <remarks>
/// Maps to sprk_event entity in Dataverse.
/// Used by GET /api/v1/events and GET /api/v1/events/{id} endpoints.
/// </remarks>
public record EventDto
{
    /// <summary>
    /// Unique identifier of the event (sprk_eventid).
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Event subject/name (sprk_eventname).
    /// </summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>
    /// Event description (sprk_description).
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Event Type ID (sprk_eventtype_ref lookup).
    /// </summary>
    public Guid? EventTypeId { get; init; }

    /// <summary>
    /// Event Type display name.
    /// </summary>
    public string? EventTypeName { get; init; }

    /// <summary>
    /// ID of the regarding record (sprk_regardingrecordid).
    /// </summary>
    public string? RegardingRecordId { get; init; }

    /// <summary>
    /// Display name of the regarding record (sprk_regardingrecordname).
    /// </summary>
    public string? RegardingRecordName { get; init; }

    /// <summary>
    /// Entity type of the regarding record (sprk_regardingrecordtype option set value).
    /// Values: Project (0), Matter (1), Invoice (2), Analysis (3), Account (4), Contact (5), Work Assignment (6), Budget (7)
    /// </summary>
    public int? RegardingRecordType { get; init; }

    /// <summary>
    /// Entity type name for display purposes.
    /// </summary>
    public string? RegardingRecordTypeName { get; init; }

    /// <summary>
    /// Base date of the event (sprk_basedate).
    /// </summary>
    public DateTime? BaseDate { get; init; }

    /// <summary>
    /// Due date of the event (sprk_duedate).
    /// </summary>
    public DateTime? DueDate { get; init; }

    /// <summary>
    /// Completion date of the event (sprk_completeddate).
    /// </summary>
    public DateTime? CompletedDate { get; init; }

    /// <summary>
    /// Event status: Active (0), Inactive (1).
    /// </summary>
    public int StateCode { get; init; }

    /// <summary>
    /// Status reason (statuscode) — live values in <see cref="Spaarke.Dataverse.EventStatusCode"/>:
    /// Draft (1), Open (659490001), Completed (659490002), Closed (659490003), Cancelled (659490004),
    /// Transferred (659490005), On Hold (659490006), Reassigned (659490007), No Further Action (2).
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>
    /// Status display name.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Event priority: Low (0), Normal (1), High (2), Urgent (3).
    /// </summary>
    public int? Priority { get; init; }

    /// <summary>
    /// Priority display name.
    /// </summary>
    public string? PriorityName { get; init; }

    /// <summary>
    /// Event source: User (0), System (1), Workflow (2), External (3).
    /// </summary>
    public int? Source { get; init; }

    /// <summary>
    /// When the event record was created.
    /// </summary>
    public DateTime CreatedOn { get; init; }

    /// <summary>
    /// When the event record was last modified.
    /// </summary>
    public DateTime ModifiedOn { get; init; }
}

// Status reason values: Spaarke.Dataverse.EventStatusCode (task 097 — the former fictional 1..7 set that lived here
// was rejected by Dataverse; the live set has one home, next to the payload builders that write it).

/// <summary>
/// Priority values for Event records.
/// </summary>
public static class EventPriority
{
    public const int Low = 0;
    public const int Normal = 1;
    public const int High = 2;
    public const int Urgent = 3;

    /// <summary>
    /// Converts priority value to display name.
    /// </summary>
    public static string GetDisplayName(int priority) => priority switch
    {
        Low => "Low",
        Normal => "Normal",
        High => "High",
        Urgent => "Urgent",
        _ => "Normal"
    };
}

/// <summary>
/// Regarding record type values (matches sprk_regardingrecordtype option set).
/// </summary>
public static class RegardingRecordType
{
    public const int Project = 0;
    public const int Matter = 1;
    public const int Invoice = 2;
    public const int Analysis = 3;
    public const int Account = 4;
    public const int Contact = 5;
    public const int WorkAssignment = 6;
    public const int Budget = 7;

    /// <summary>
    /// Converts type value to display name.
    /// </summary>
    public static string GetDisplayName(int type) => type switch
    {
        Project => "Project",
        Matter => "Matter",
        Invoice => "Invoice",
        Analysis => "Analysis",
        Account => "Account",
        Contact => "Contact",
        WorkAssignment => "Work Assignment",
        Budget => "Budget",
        _ => "Unknown"
    };
}
