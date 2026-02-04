namespace Sprk.Bff.Api.Api.Events.Dtos;

/// <summary>
/// Response model for listing events with pagination.
/// </summary>
/// <remarks>
/// Used by GET /api/v1/events endpoint.
/// Supports filtering and pagination per spec requirements.
/// </remarks>
public record EventListResponse
{
    /// <summary>
    /// List of events matching the query.
    /// </summary>
    public EventDto[] Items { get; init; } = [];

    /// <summary>
    /// Total count of events matching the filter criteria (before pagination).
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Number of items per page.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int PageNumber { get; init; }

    /// <summary>
    /// Total number of pages available.
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary>
    /// Whether there are more pages after the current one.
    /// </summary>
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary>
    /// Whether there are pages before the current one.
    /// </summary>
    public bool HasPreviousPage => PageNumber > 1;
}

/// <summary>
/// Query parameters for filtering and paginating events.
/// </summary>
public record EventQueryParams
{
    /// <summary>
    /// Filter by regarding record type (0-7).
    /// Values: Project (0), Matter (1), Invoice (2), Analysis (3), Account (4), Contact (5), Work Assignment (6), Budget (7)
    /// </summary>
    public int? RegardingRecordType { get; init; }

    /// <summary>
    /// Filter by specific regarding record ID.
    /// </summary>
    public string? RegardingRecordId { get; init; }

    /// <summary>
    /// Filter by event type ID.
    /// </summary>
    public Guid? EventTypeId { get; init; }

    /// <summary>
    /// Filter by status code.
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// Filter by priority (0=Low, 1=Normal, 2=High, 3=Urgent).
    /// </summary>
    public int? Priority { get; init; }

    /// <summary>
    /// Filter events with due date on or after this date.
    /// </summary>
    public DateTime? DueDateFrom { get; init; }

    /// <summary>
    /// Filter events with due date on or before this date.
    /// </summary>
    public DateTime? DueDateTo { get; init; }

    /// <summary>
    /// Page number (1-based). Defaults to 1.
    /// </summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>
    /// Page size. Defaults to 50, max 100.
    /// </summary>
    public int PageSize { get; init; } = 50;
}
