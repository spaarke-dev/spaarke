using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Type of entity being resolved.
/// </summary>
public enum EntityType
{
    /// <summary>Canvas node reference.</summary>
    Node,

    /// <summary>Analysis scope (action, skill, knowledge, tool).</summary>
    Scope
}

/// <summary>
/// Scope subtype for resolution.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScopeCategory
{
    /// <summary>Analysis action.</summary>
    [JsonPropertyName("action")]
    Action,

    /// <summary>Analysis skill.</summary>
    [JsonPropertyName("skill")]
    Skill,

    /// <summary>Knowledge source.</summary>
    [JsonPropertyName("knowledge")]
    Knowledge,

    /// <summary>Analysis tool.</summary>
    [JsonPropertyName("tool")]
    Tool
}

/// <summary>
/// Result from entity resolution with matches and confidence.
/// </summary>
public record EntityResolutionResult
{
    /// <summary>Original reference text from user.</summary>
    public required string OriginalReference { get; init; }

    /// <summary>Type of entity resolved.</summary>
    public EntityType EntityType { get; init; }

    /// <summary>For scopes, the specific category.</summary>
    public ScopeCategory? ScopeCategory { get; init; }

    /// <summary>Whether resolution was successful with high confidence.</summary>
    public bool IsResolved => BestMatch != null && Confidence >= 0.80;

    /// <summary>Best matching entity (if any).</summary>
    public EntityMatch? BestMatch { get; init; }

    /// <summary>Confidence score of best match (0.0 to 1.0).</summary>
    public double Confidence { get; init; }

    /// <summary>All candidate matches when ambiguous (confidence &lt; 0.80).</summary>
    public EntityMatch[]? CandidateMatches { get; init; }

    /// <summary>Whether user selection is needed.</summary>
    public bool NeedsSelection => CandidateMatches != null && CandidateMatches.Length > 1 && Confidence < 0.80;

    /// <summary>Explanation of the resolution.</summary>
    public string? Reasoning { get; init; }
}

/// <summary>
/// A single matched entity with confidence.
/// </summary>
public record EntityMatch
{
    /// <summary>Entity identifier (node ID or scope GUID).</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Display label or name.</summary>
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    /// <summary>Entity type (node type or scope category).</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Match confidence score (0.0 to 1.0).</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>Why this entity matched.</summary>
    [JsonPropertyName("matchReason")]
    public string? MatchReason { get; init; }
}

/// <summary>
/// Request for resolving a node reference.
/// </summary>
public record NodeResolutionRequest
{
    /// <summary>User's reference text (e.g., "the TL;DR node").</summary>
    public required string Reference { get; init; }

    /// <summary>Canvas context with available nodes.</summary>
    public required ClassificationCanvasContext CanvasContext { get; init; }

    /// <summary>Currently selected node ID (for "this node" references).</summary>
    public string? SelectedNodeId { get; init; }
}

/// <summary>
/// Request for resolving a scope reference.
/// </summary>
public record ScopeResolutionRequest
{
    /// <summary>User's reference text (e.g., "standard compliance skill").</summary>
    public required string Reference { get; init; }

    /// <summary>Expected scope category (if known from intent).</summary>
    public ScopeCategory? ExpectedCategory { get; init; }

    /// <summary>Whether to include all categories in search.</summary>
    public bool SearchAllCategories { get; init; } = false;
}

/// <summary>
/// AI response for entity resolution.
/// </summary>
public record EntityResolutionAiResponse
{
    /// <summary>Best matching entity ID.</summary>
    [JsonPropertyName("matchedId")]
    public string? MatchedId { get; init; }

    /// <summary>Confidence in the match (0.0 to 1.0).</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>Ranked candidate matches.</summary>
    [JsonPropertyName("candidates")]
    public EntityMatchCandidate[]? Candidates { get; init; }

    /// <summary>Explanation of the matching decision.</summary>
    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; init; }
}

/// <summary>
/// Candidate match from AI response.
/// </summary>
public record EntityMatchCandidate
{
    /// <summary>Entity ID.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Match confidence.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>Why this candidate matches.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
