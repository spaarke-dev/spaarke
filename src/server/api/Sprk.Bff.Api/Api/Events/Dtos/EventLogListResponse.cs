namespace Sprk.Bff.Api.Api.Events.Dtos;

/// <summary>
/// Response model for listing Event Log entries.
/// </summary>
public record EventLogListResponse
{
    /// <summary>
    /// The log entries for the event.
    /// </summary>
    public required EventLogDto[] Items { get; init; }

    /// <summary>
    /// Total number of log entries for this event.
    /// </summary>
    public required int TotalCount { get; init; }
}
