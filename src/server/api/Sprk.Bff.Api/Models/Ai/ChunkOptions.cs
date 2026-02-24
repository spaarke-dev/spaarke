namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Configuration options for <see cref="Sprk.Bff.Api.Services.Ai.SemanticDocumentChunker"/>.
/// </summary>
/// <remarks>
/// <para>
/// Two static factory methods cover the two standard configurations defined in
/// the AI Platform spec (FR-A06):
/// <list type="bullet">
///   <item><see cref="ForKnowledgeIndex"/> — 512-token curated knowledge index.</item>
///   <item><see cref="ForDiscoveryIndex"/> — 1024-token auto-populated discovery index.</item>
/// </list>
/// </para>
/// <para>
/// Token counts are approximated as <c>characters / 4</c>.
/// No external tokeniser library is required (see spec constraint: no external token-counting
/// library; use simple approximation).
/// </para>
/// </remarks>
public sealed record ChunkOptions
{
    /// <summary>
    /// Maximum number of tokens per chunk (approximate, using chars / 4).
    /// </summary>
    public int MaxTokens { get; init; } = 512;

    /// <summary>
    /// Number of tokens to overlap between consecutive chunks for context continuity.
    /// </summary>
    public int OverlapTokens { get; init; } = 50;

    /// <summary>
    /// When <see langword="true"/>, each chunk is prefixed with
    /// <c>[Section: {SectionTitle}]</c> to improve retrieval quality by embedding
    /// section context directly into the chunk text.
    /// </summary>
    public bool IncludeSectionContext { get; init; } = true;

    // -------------------------------------------------------------------------
    // Static factory methods (spec-defined configurations)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Options for the curated knowledge index: 512-token chunks, 50-token overlap.
    /// </summary>
    /// <remarks>
    /// Matches spec FR-A06 "knowledge index (512-token chunks, curated)".
    /// </remarks>
    public static ChunkOptions ForKnowledgeIndex() => new()
    {
        MaxTokens = 512,
        OverlapTokens = 50,
        IncludeSectionContext = true
    };

    /// <summary>
    /// Options for the auto-populated discovery index: 1024-token chunks, 100-token overlap.
    /// </summary>
    /// <remarks>
    /// Matches spec FR-A06 "discovery index (1024-token chunks, auto-populated)".
    /// </remarks>
    public static ChunkOptions ForDiscoveryIndex() => new()
    {
        MaxTokens = 1024,
        OverlapTokens = 100,
        IncludeSectionContext = true
    };
}
