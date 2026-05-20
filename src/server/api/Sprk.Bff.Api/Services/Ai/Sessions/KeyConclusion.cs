using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// A single structured legal conclusion extracted from a summarized chat session.
///
/// Each conclusion captures a discrete legal finding, decision, or obligation
/// identified during the conversation, along with its confidence and source reference.
/// These are extracted as a structured list during summarization (AIPU2-032) so that
/// downstream consumers (e.g., matter memory, audit trail) can parse them without
/// relying on free-text parsing.
/// </summary>
/// <param name="Topic">Short label for the legal area (e.g., "Indemnification", "Governing Law").</param>
/// <param name="Conclusion">
/// The specific legal conclusion, decision, or obligation identified.
/// Must preserve exact qualifications — e.g., "except in cases of gross negligence".
/// </param>
/// <param name="Confidence">
/// Confidence level: "high", "medium", or "low".
/// Reflects how clearly the conclusion was stated in the conversation.
/// </param>
/// <param name="SourceReference">
/// Optional citation or document reference that supports the conclusion
/// (e.g., "Section 4.2 of the MSA", "Smith v. Jones, 123 F.3d 456").
/// Null when no explicit reference was provided in the conversation.
/// </param>
public record KeyConclusion(
    [property: JsonPropertyName("topic")] string Topic,
    [property: JsonPropertyName("conclusion")] string Conclusion,
    [property: JsonPropertyName("confidence")] string Confidence,
    [property: JsonPropertyName("sourceReference")] string? SourceReference);
