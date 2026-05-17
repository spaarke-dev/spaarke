using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// A single structured fact stored in matter memory (ADR-015 Tier 3).
///
/// Facts are typed via <see cref="MemoryFactType"/> so the prompt serialiser can
/// group them under the correct section heading (Parties / Key Dates / Prior Analyses / Key Facts).
///
/// Design notes:
/// - <see cref="Key"/> is a short human-readable label (e.g. "Plaintiff", "Hearing Date").
/// - <see cref="Value"/> is the fact content (e.g. "Company X", "July 15, 2026").
/// - <see cref="Source"/> records how the fact was populated: "user", "ai-extraction", "import".
/// - <see cref="ConfirmedByUser"/> gates whether lower-confidence AI-extracted facts are injected
///   into the system prompt without explicit user acknowledgement.
/// - <see cref="Confidence"/> is used to drop the lowest-scoring facts when the prompt fragment
///   would exceed 500 tokens (truncation policy in ToSystemPromptFragmentAsync).
/// </summary>
public sealed class MemoryFact
{
    /// <summary>Cosmos DB sub-document id. Auto-generated on creation.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>Discriminator for grouping in the system prompt.</summary>
    [JsonPropertyName("type")]
    public required MemoryFactType Type { get; init; }

    /// <summary>
    /// Short label identifying the fact (e.g. "Plaintiff", "Markman Hearing", "Contract Value").
    /// Used as the bold prefix in the system prompt fragment.
    /// </summary>
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    /// <summary>
    /// The fact value (e.g. "Company X (plaintiff)", "July 15, 2026", "$2.4M").
    /// </summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>
    /// How this fact was populated: "user", "ai-extraction", "import", "admin".
    /// Stored for provenance; not used in prompt injection.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = "user";

    /// <summary>
    /// Whether the user has explicitly confirmed this fact.
    /// AI-extracted facts (<see cref="Source"/> == "ai-extraction") default to false until
    /// the user acknowledges them. Facts with <see cref="ConfirmedByUser"/> == false and
    /// <see cref="Confidence"/> below 0.7 are excluded from the system prompt fragment.
    /// </summary>
    [JsonPropertyName("confirmedByUser")]
    public bool ConfirmedByUser { get; init; }

    /// <summary>
    /// Confidence score in [0.0, 1.0]. 1.0 for user-entered facts; model-assigned for AI-extracted facts.
    /// Lower-confidence facts are truncated first when the prompt fragment exceeds 500 tokens.
    /// </summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } = 1.0;

    /// <summary>UTC timestamp when this fact was recorded.</summary>
    [JsonPropertyName("recordedAt")]
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;
}
