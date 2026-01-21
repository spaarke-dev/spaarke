using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for Office endpoint rate limiting.
/// Uses sliding window algorithm with per-user limits stored in Redis for distributed environments.
/// </summary>
/// <remarks>
/// <para>
/// Per spec.md rate limiting requirements:
/// - POST /office/save: 10 requests per minute per user
/// - POST /office/quickcreate/*: 5 requests per minute per user
/// - GET /office/search/*: 30 requests per minute per user
/// - GET /office/jobs/*: 60 requests per minute per user
/// - POST /office/share/*: 20 requests per minute per user
/// </para>
/// <para>
/// Rate limit state is stored in Redis (when enabled) for distributed consistency
/// across multiple API instances. Falls back to in-memory storage in development.
/// </para>
/// </remarks>
public class OfficeRateLimitOptions
{
    public const string SectionName = "OfficeRateLimit";

    /// <summary>
    /// Enable rate limiting for Office endpoints.
    /// Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Window size in seconds for sliding window algorithm.
    /// Default: 60 (1 minute)
    /// </summary>
    [Range(10, 600)]
    public int WindowSizeSeconds { get; set; } = 60;

    /// <summary>
    /// Number of segments in the sliding window for granularity.
    /// More segments = smoother limiting but more storage.
    /// Default: 6 (10-second segments for 60-second window)
    /// </summary>
    [Range(2, 60)]
    public int SegmentsPerWindow { get; set; } = 6;

    /// <summary>
    /// Redis key prefix for rate limit counters.
    /// Default: "office:ratelimit:"
    /// </summary>
    public string KeyPrefix { get; set; } = "office:ratelimit:";

    /// <summary>
    /// Rate limits for specific endpoint categories.
    /// </summary>
    public EndpointLimits Limits { get; set; } = new();
}

/// <summary>
/// Rate limits for specific Office endpoint categories.
/// </summary>
public class EndpointLimits
{
    /// <summary>
    /// POST /office/save - Requests per minute per user.
    /// Default: 10
    /// </summary>
    [Range(1, 1000)]
    public int SaveRequestsPerMinute { get; set; } = 10;

    /// <summary>
    /// POST /office/quickcreate/* - Requests per minute per user.
    /// Default: 5
    /// </summary>
    [Range(1, 1000)]
    public int QuickCreateRequestsPerMinute { get; set; } = 5;

    /// <summary>
    /// GET /office/search/* - Requests per minute per user.
    /// Default: 30
    /// </summary>
    [Range(1, 1000)]
    public int SearchRequestsPerMinute { get; set; } = 30;

    /// <summary>
    /// GET /office/jobs/* - Requests per minute per user.
    /// Default: 60
    /// </summary>
    [Range(1, 1000)]
    public int JobsRequestsPerMinute { get; set; } = 60;

    /// <summary>
    /// POST /office/share/* - Requests per minute per user.
    /// Default: 20
    /// </summary>
    [Range(1, 1000)]
    public int ShareRequestsPerMinute { get; set; } = 20;

    /// <summary>
    /// GET /office/recent - Requests per minute per user.
    /// Default: 30
    /// </summary>
    [Range(1, 1000)]
    public int RecentRequestsPerMinute { get; set; } = 30;
}
