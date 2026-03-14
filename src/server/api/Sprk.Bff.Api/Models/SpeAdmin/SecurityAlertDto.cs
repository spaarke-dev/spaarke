using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.SpeAdmin;

/// <summary>
/// Represents a single security alert from the Microsoft Graph Security API
/// (GET /security/alerts_v2).
///
/// Surfaces suspicious activities, policy violations, and other security-relevant
/// events in the SharePoint Embedded environment for administrator review.
///
/// ADR-007: No Graph SDK types in public API surface — only domain model fields.
/// </summary>
public sealed class SecurityAlertDto
{
    /// <summary>Unique identifier for the alert (Graph alert ID).</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Short human-readable title describing the alert type.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>
    /// Alert severity: informational, low, medium, high, or unknownFutureValue.
    /// </summary>
    [JsonPropertyName("severity")]
    public string? Severity { get; init; }

    /// <summary>
    /// Current status of the alert: unknown, newAlert, inProgress, resolved,
    /// dismissed, or unknownFutureValue.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>UTC date-time when the alert was first created by Graph.</summary>
    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; init; }

    /// <summary>Human-readable description of what triggered the alert.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}
