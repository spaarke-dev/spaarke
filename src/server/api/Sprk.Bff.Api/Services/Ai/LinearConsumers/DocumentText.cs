namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Extracted document text + minimal metadata returned by
/// <see cref="IDocumentTextSource"/>. Used as input to
/// <see cref="IActionRunner"/> JPS <c>{{document.extractedText}}</c> binding.
/// </summary>
/// <remarks>
/// R7 Wave 12 (2026-07-02). See
/// <c>docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md</c>.
/// </remarks>
public sealed record DocumentText
{
    /// <summary>
    /// Optional document row id. Populated when the source was a Dataverse
    /// document id (Doc Upload path); null for direct-file uploads (File
    /// Summarize path).
    /// </summary>
    public Guid? DocumentId { get; init; }

    /// <summary>
    /// File name for logging / display / search-profile enrichment. Never null;
    /// falls back to a sentinel like <c>"upload"</c> when no name is available.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// The extracted text content used as LLM input. Empty when the file type
    /// is unsupported or extraction failed — consumer services SHOULD treat
    /// empty as a hard error and short-circuit (no LLM call worth making).
    /// </summary>
    public required string ExtractedText { get; init; }

    /// <summary>
    /// Optional SPE drive id — populated for Dataverse-backed documents so
    /// downstream jobs (RAG indexing) can enqueue against the SPE object.
    /// </summary>
    public string? GraphDriveId { get; init; }

    /// <summary>
    /// Optional SPE item id — companion to <see cref="GraphDriveId"/>.
    /// </summary>
    public string? GraphItemId { get; init; }
}
