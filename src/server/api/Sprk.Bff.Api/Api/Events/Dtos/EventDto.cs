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
    /// Entity type of the regarding record, as the API's integer (derived from the typed regarding lookup — see
    /// <see cref="Dtos.RegardingRecordType"/>; <c>sprk_regardingrecordtype</c> itself is a lookup, not this value).
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
    /// Event priority (sprk_priority) — live <see cref="Spaarke.Dataverse.EventPriority"/> value: Low (100000000),
    /// Normal (100000001), High (100000002), Urgent (100000003).
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

// Status reason values: Spaarke.Dataverse.EventStatusCode — ONE home, next to the payload builders that write them
// (task 097; unified-access-control-r2 task 159 fixed the same values independently in a copy here — merged into one).

// Priority values: Spaarke.Dataverse.EventPriority (task 097 — the former 0..3 set that lived here was rejected by
// Dataverse; live sprk_priority is Low 100000000 … Urgent 100000003).

/// <summary>
/// The API's regarding record type values (0-7). These are NOT Dataverse values: <c>sprk_regardingrecordtype</c> is a
/// LOOKUP to <c>sprk_recordtype_ref</c>, not an option set (task 159, #1098). The API's integer names which typed
/// regarding lookup an event uses; the server binds the record-type lookup to the environment's
/// <c>sprk_recordtype_ref</c> row and derives this integer on read from the typed lookup whose value equals
/// <c>sprk_regardingrecordid</c>.
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
