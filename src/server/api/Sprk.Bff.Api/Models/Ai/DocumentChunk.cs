namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Represents a semantically coherent chunk of a document produced by
/// <see cref="Sprk.Bff.Api.Services.Ai.SemanticDocumentChunker"/>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the character-based <c>TextChunk</c> type produced by
/// <c>TextChunkingService</c>, a <c>DocumentChunk</c> is derived from the
/// structural layout output of the Azure Document Intelligence Layout model.
/// Boundaries are aligned to paragraph and section edges, and each chunk
/// carries its originating section title so that retrieval queries can
/// surface the full context in which a passage appears.
/// </para>
/// <para>
/// The <see cref="Content"/> property already includes the section prefix in
/// the form <c>"[Section: {SectionTitle}]\n{body}"</c> when
/// <see cref="ChunkOptions.IncludeSectionContext"/> is <see langword="true"/>.
/// </para>
/// </remarks>
/// <param name="Content">
/// The full text content of the chunk, including the optional section prefix.
/// </param>
/// <param name="SectionTitle">
/// The nearest enclosing section heading detected by the Layout model, or
/// <see langword="null"/> if none was found.
/// </param>
/// <param name="PageNumber">
/// The 1-based page number on which this chunk begins.
/// </param>
/// <param name="ChunkIndex">
/// The zero-based ordinal position of this chunk within the full sequence
/// produced for the document.
/// </param>
/// <param name="TokenCount">
/// The approximate number of tokens in <see cref="Content"/>, computed using
/// the <c>chars / 4</c> approximation.  Useful for enforcing embedding-model
/// token limits without adding a tokeniser dependency.
/// </param>
public sealed record DocumentChunk(
    string Content,
    string? SectionTitle,
    int PageNumber,
    int ChunkIndex,
    int TokenCount);
