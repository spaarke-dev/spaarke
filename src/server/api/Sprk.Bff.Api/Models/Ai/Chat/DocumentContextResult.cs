using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Result of document context injection produced by
/// <see cref="Sprk.Bff.Api.Services.Ai.Chat.DocumentContextService"/>.
///
/// Contains the selected document chunks (within the 30K token budget),
/// total token usage, and truncation metadata. Used by
/// <see cref="Sprk.Bff.Api.Services.Ai.Chat.PlaybookChatContextProvider"/>
/// to append document content to the system prompt.
/// </summary>
/// <param name="DocumentId">
/// The Dataverse sprk_document ID of the source document.
/// </param>
/// <param name="DocumentName">
/// Display name of the document (for logging and context headers).
/// </param>
/// <param name="SelectedChunks">
/// Ordered list of document chunks selected for inclusion in the system prompt.
/// When the document fits within budget, all chunks are included.
/// When truncated, chunks are ranked by relevance to the latest user message.
/// </param>
/// <param name="TotalChunks">
/// Total number of chunks produced from the full document (before selection).
/// </param>
/// <param name="TotalTokensUsed">
/// Approximate token count of all selected chunks combined.
/// Always &lt;= <see cref="Sprk.Bff.Api.Services.Ai.Chat.DocumentContextService.MaxTokenBudget"/>.
/// </param>
/// <param name="WasTruncated">
/// True when the document exceeded the token budget and chunks were selectively
/// included based on relevance scoring. False when all chunks fit within budget.
/// </param>
/// <param name="TruncationReason">
/// Human-readable explanation of why truncation occurred, or null when
/// <see cref="WasTruncated"/> is false.
/// </param>
public sealed record DocumentContextResult(
    string DocumentId,
    string? DocumentName,
    IReadOnlyList<DocumentChunk> SelectedChunks,
    int TotalChunks,
    int TotalTokensUsed,
    bool WasTruncated,
    string? TruncationReason = null)
{
    /// <summary>
    /// Creates an empty result for cases where document content could not be loaded.
    /// </summary>
    public static DocumentContextResult Empty(string documentId, string? documentName = null) =>
        new(documentId, documentName, Array.Empty<DocumentChunk>(), 0, 0, false);

    /// <summary>
    /// Formats the selected chunks as a single string block suitable for
    /// injection into the system prompt's document context section.
    /// </summary>
    /// <remarks>
    /// Each chunk is separated by a blank line. When truncation occurred,
    /// a header is prepended informing the model that only relevant sections
    /// are shown.
    /// </remarks>
    public string FormatForSystemPrompt()
    {
        if (SelectedChunks.Count == 0)
            return string.Empty;

        var parts = new List<string>();

        if (WasTruncated)
        {
            parts.Add(
                $"[Showing {SelectedChunks.Count} of {TotalChunks} sections most relevant to the conversation. " +
                "Ask about a specific section to surface its content.]");
        }

        foreach (var chunk in SelectedChunks)
        {
            parts.Add(chunk.Content);
        }

        return string.Join("\n\n", parts);
    }
}
