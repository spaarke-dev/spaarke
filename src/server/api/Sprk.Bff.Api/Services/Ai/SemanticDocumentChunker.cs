using System.Text;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Clause-aware document chunker that uses Azure Document Intelligence Layout model output
/// to split documents at semantically meaningful boundaries (paragraphs, sections).
/// </summary>
/// <remarks>
/// <para>
/// This service replaces the character-count approach of <see cref="TextChunkingService"/>
/// for RAG indexing scenarios.  Instead of slicing raw text at fixed character positions,
/// it works from the structural analysis produced by the Layout model so that chunks
/// always end on a paragraph boundary and never break in the middle of a clause.
/// </para>
/// <para>
/// <b>Chunking algorithm</b>:
/// <list type="ordered">
///   <item>Collect paragraphs from the <see cref="AnalyzeResult"/> in reading order.</item>
///   <item>Accumulate paragraphs into a working buffer until <see cref="ChunkOptions.MaxTokens"/> is
///         reached or a section heading is encountered.</item>
///   <item>When the budget is reached, flush the buffer as a <see cref="DocumentChunk"/> and
///         carry forward the last <see cref="ChunkOptions.OverlapTokens"/> worth of text to
///         maintain context across the chunk boundary.</item>
///   <item>Prefix each chunk with <c>[Section: {title}]</c> when
///         <see cref="ChunkOptions.IncludeSectionContext"/> is <see langword="true"/>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Token counting</b>: uses the simple approximation <c>tokens ≈ chars / 4</c>.
/// No external tokeniser library is required.
/// </para>
/// <para>
/// <b>ADR compliance</b>:
/// <list type="bullet">
///   <item>ADR-010: registered as concrete singleton (no interface — single implementation).</item>
///   <item>ADR-007: callers supply the already-retrieved <see cref="AnalyzeResult"/>; this
///         service never calls Graph or storage directly.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class SemanticDocumentChunker
{
    private readonly ILogger<SemanticDocumentChunker> _logger;

    // Role values set by the Layout model to identify section headings.
    // The SDK exposes them as string constants on DocumentParagraphRole.
    private static readonly IReadOnlySet<string> SectionRoles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "title",
            "sectionHeading"
        };

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticDocumentChunker"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    public SemanticDocumentChunker(ILogger<SemanticDocumentChunker> logger)
    {
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Splits a document into semantically coherent chunks based on the Layout
    /// model analysis result.
    /// </summary>
    /// <param name="layoutResult">
    /// The <see cref="AnalyzeResult"/> returned by calling the Azure Document
    /// Intelligence Layout model (<c>prebuilt-layout</c>).
    /// </param>
    /// <param name="options">
    /// Chunking configuration.  Use <see cref="ChunkOptions.ForKnowledgeIndex"/> or
    /// <see cref="ChunkOptions.ForDiscoveryIndex"/> for the standard configurations.
    /// </param>
    /// <returns>
    /// An ordered, read-only list of <see cref="DocumentChunk"/> records.  Returns an
    /// empty list when the document has no paragraphs.
    /// </returns>
    public IReadOnlyList<DocumentChunk> ChunkDocument(
        AnalyzeResult layoutResult,
        ChunkOptions options)
    {
        ArgumentNullException.ThrowIfNull(layoutResult);
        ArgumentNullException.ThrowIfNull(options);

        if (layoutResult.Paragraphs is not { Count: > 0 } paragraphs)
        {
            // Fall back to page-level line extraction when there are no paragraphs
            // (e.g. scanned documents analysed with an older model version).
            return ChunkFromLines(layoutResult, options);
        }

        _logger.LogDebug(
            "Chunking document with {ParagraphCount} paragraphs, MaxTokens={MaxTokens}, OverlapTokens={OverlapTokens}",
            paragraphs.Count,
            options.MaxTokens,
            options.OverlapTokens);

        return BuildChunks(paragraphs, layoutResult, options);
    }

    // -------------------------------------------------------------------------
    // Core chunking algorithm
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds chunks by accumulating paragraphs and flushing when the token
    /// budget is exhausted.
    /// </summary>
    private IReadOnlyList<DocumentChunk> BuildChunks(
        IReadOnlyList<DocumentParagraph> paragraphs,
        AnalyzeResult layoutResult,
        ChunkOptions options)
    {
        var chunks = new List<DocumentChunk>();
        var buffer = new StringBuilder();
        var currentSection = string.Empty;
        var chunkStartPage = 1;
        var chunkIndex = 0;

        // Overlap text carried forward from the previous chunk.
        var overlapCarry = string.Empty;

        for (var i = 0; i < paragraphs.Count; i++)
        {
            var paragraph = paragraphs[i];
            var paragraphText = paragraph.Content?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(paragraphText))
                continue;

            // Detect section headings — update tracking and optionally flush early.
            var isHeading = paragraph.Role != null &&
                            SectionRoles.Contains(paragraph.Role.ToString()!);

            if (isHeading)
            {
                // Flush the current buffer before starting a new section,
                // unless the buffer only contains carry-over overlap text.
                if (buffer.Length > 0 && TokenCount(buffer.ToString()) > options.OverlapTokens)
                {
                    chunkIndex = FlushChunk(
                        chunks, buffer, currentSection, chunkStartPage,
                        chunkIndex, options, out overlapCarry);

                    buffer.Clear();
                    if (!string.IsNullOrEmpty(overlapCarry))
                    {
                        buffer.Append(overlapCarry);
                    }
                }

                currentSection = paragraphText;
                chunkStartPage = GetPageNumber(paragraph, layoutResult);
                continue; // Heading itself is used as context prefix, not content.
            }

            // Determine page number for this paragraph (used for chunk metadata).
            var pageNum = GetPageNumber(paragraph, layoutResult);
            if (buffer.Length == 0)
            {
                chunkStartPage = pageNum;
                if (!string.IsNullOrEmpty(overlapCarry))
                {
                    buffer.Append(overlapCarry);
                }
            }

            // Append paragraph separator if buffer already has content.
            if (buffer.Length > 0)
            {
                buffer.Append('\n');
            }

            buffer.Append(paragraphText);

            // Check whether we have reached the token budget.
            var currentTokens = TokenCount(buffer.ToString());
            if (currentTokens >= options.MaxTokens)
            {
                chunkIndex = FlushChunk(
                    chunks, buffer, currentSection, chunkStartPage,
                    chunkIndex, options, out overlapCarry);

                buffer.Clear();
                if (!string.IsNullOrEmpty(overlapCarry))
                {
                    buffer.Append(overlapCarry);
                }

                chunkStartPage = pageNum;
            }
        }

        // Flush any remaining content as the final chunk.
        if (buffer.Length > 0)
        {
            FlushChunk(chunks, buffer, currentSection, chunkStartPage,
                chunkIndex, options, out _);
        }

        _logger.LogDebug(
            "Produced {ChunkCount} chunks from document",
            chunks.Count);

        return chunks.AsReadOnly();
    }

    /// <summary>
    /// Flushes the working buffer as a new <see cref="DocumentChunk"/>, computes the
    /// overlap text to carry forward, and returns the next chunk index.
    /// </summary>
    private static int FlushChunk(
        List<DocumentChunk> chunks,
        StringBuilder buffer,
        string sectionTitle,
        int pageNumber,
        int chunkIndex,
        ChunkOptions options,
        out string overlapCarry)
    {
        var bodyText = buffer.ToString().Trim();

        // Build the content string, optionally prefixed with section context.
        string content;
        if (options.IncludeSectionContext && !string.IsNullOrWhiteSpace(sectionTitle))
        {
            content = $"[Section: {sectionTitle}]\n{bodyText}";
        }
        else
        {
            content = bodyText;
        }

        var tokenCount = TokenCount(content);

        chunks.Add(new DocumentChunk(
            Content: content,
            SectionTitle: string.IsNullOrWhiteSpace(sectionTitle) ? null : sectionTitle,
            PageNumber: pageNumber,
            ChunkIndex: chunkIndex,
            TokenCount: tokenCount));

        // Compute overlap: take the last N tokens worth of characters from the body
        // (not including the section prefix) to carry forward.
        overlapCarry = ExtractOverlapText(bodyText, options.OverlapTokens);

        return chunkIndex + 1;
    }

    // -------------------------------------------------------------------------
    // Fallback: line-based chunking for documents without paragraph analysis
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fallback path used when the <see cref="AnalyzeResult"/> contains no
    /// <see cref="AnalyzeResult.Paragraphs"/> (e.g. scanned documents).  Extracts
    /// text line by line from each page.
    /// </summary>
    private IReadOnlyList<DocumentChunk> ChunkFromLines(
        AnalyzeResult layoutResult,
        ChunkOptions options)
    {
        _logger.LogDebug(
            "No paragraphs found in layout result; falling back to line-based chunking");

        if (layoutResult.Pages is not { Count: > 0 })
        {
            return Array.Empty<DocumentChunk>();
        }

        var chunks = new List<DocumentChunk>();
        var buffer = new StringBuilder();
        var chunkStartPage = 1;
        var chunkIndex = 0;
        var overlapCarry = string.Empty;

        foreach (var page in layoutResult.Pages)
        {
            var pageNumber = page.PageNumber;

            if (page.Lines is null)
                continue;

            foreach (var line in page.Lines)
            {
                var lineText = line.Content?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(lineText))
                    continue;

                if (buffer.Length == 0)
                {
                    chunkStartPage = pageNumber;
                    if (!string.IsNullOrEmpty(overlapCarry))
                    {
                        buffer.Append(overlapCarry);
                    }
                }

                if (buffer.Length > 0)
                    buffer.Append('\n');

                buffer.Append(lineText);

                if (TokenCount(buffer.ToString()) >= options.MaxTokens)
                {
                    chunkIndex = FlushChunk(
                        chunks, buffer, sectionTitle: string.Empty,
                        chunkStartPage, chunkIndex, options,
                        out overlapCarry);

                    buffer.Clear();
                    chunkStartPage = pageNumber;
                }
            }
        }

        if (buffer.Length > 0)
        {
            FlushChunk(chunks, buffer, sectionTitle: string.Empty,
                chunkStartPage, chunkIndex, options, out _);
        }

        return chunks.AsReadOnly();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Approximates the token count of <paramref name="text"/> using the
    /// <c>chars / 4</c> heuristic (no external tokeniser required).
    /// </summary>
    internal static int TokenCount(string text) =>
        string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / 4);

    /// <summary>
    /// Extracts the last <paramref name="overlapTokens"/> tokens' worth of text
    /// from <paramref name="text"/> to use as carry-forward overlap.
    /// </summary>
    private static string ExtractOverlapText(string text, int overlapTokens)
    {
        if (string.IsNullOrEmpty(text) || overlapTokens <= 0)
            return string.Empty;

        var overlapChars = overlapTokens * 4;
        if (text.Length <= overlapChars)
            return text;

        var startIndex = text.Length - overlapChars;

        // Snap to the next word boundary so the carry-over starts cleanly.
        var spaceIndex = text.IndexOf(' ', startIndex);
        if (spaceIndex > startIndex && spaceIndex < text.Length - 1)
            startIndex = spaceIndex + 1;

        return text[startIndex..].Trim();
    }

    /// <summary>
    /// Returns the 1-based page number for <paramref name="paragraph"/> by
    /// checking its bounding regions.  Defaults to 1 when the information is
    /// unavailable.
    /// </summary>
    private static int GetPageNumber(DocumentParagraph paragraph, AnalyzeResult layoutResult)
    {
        if (paragraph.BoundingRegions is { Count: > 0 })
        {
            return paragraph.BoundingRegions[0].PageNumber;
        }

        // Fallback: page 1 (safe default when bounding regions are absent).
        // layoutResult parameter retained for future use (e.g. page fallback logic).
        return 1;
    }
}
