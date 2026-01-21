namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Response model for the Office health check endpoint.
/// </summary>
public record OfficeHealthResponse
{
    /// <summary>
    /// Health status (e.g., "healthy", "degraded", "unhealthy").
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Service name.
    /// </summary>
    public required string Service { get; init; }

    /// <summary>
    /// API version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Timestamp of the health check.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }
}
