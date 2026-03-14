using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.SpeAdmin;

/// <summary>
/// Represents the Microsoft Secure Score for the tenant, surfaced via the
/// Microsoft Graph Security API (GET /security/secureScores).
///
/// Provides administrators with a quantified measure of their organization's
/// security posture relative to the maximum achievable score and to comparable
/// organizations (averageComparativeScores).
///
/// ADR-007: No Graph SDK types in public API surface — only domain model fields.
/// </summary>
public sealed class SecureScoreDto
{
    /// <summary>
    /// The achieved secure score points (e.g., 85.0).
    /// </summary>
    [JsonPropertyName("currentScore")]
    public double? CurrentScore { get; init; }

    /// <summary>
    /// Maximum number of points achievable given the tenant's licenses and
    /// enabled controls (e.g., 200.0).
    /// </summary>
    [JsonPropertyName("maxScore")]
    public double? MaxScore { get; init; }

    /// <summary>
    /// Benchmark scores from similar organizations for comparison.
    /// Each entry contains a basis (e.g., "AllTenants", "TotalSeats") and an
    /// average score for that peer group.
    /// May be null or empty when the Graph API does not return comparison data.
    /// </summary>
    [JsonPropertyName("averageComparativeScores")]
    public List<AverageComparativeScoreDto>? AverageComparativeScores { get; init; }
}

/// <summary>
/// A single comparative score entry from the Microsoft Secure Score response.
/// Describes the average score for a specific peer group (e.g., "AllTenants").
/// </summary>
public sealed class AverageComparativeScoreDto
{
    /// <summary>
    /// The basis for comparison (e.g., "AllTenants", "TotalSeats",
    /// "IndustryTypes").
    /// </summary>
    [JsonPropertyName("basis")]
    public string? Basis { get; init; }

    /// <summary>Average score of organizations in this peer group.</summary>
    [JsonPropertyName("averageScore")]
    public double? AverageScore { get; init; }
}
