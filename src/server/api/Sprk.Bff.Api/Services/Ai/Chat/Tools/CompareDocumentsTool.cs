using System.ComponentModel;
using System.Text;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

// ─── Output models ───────────────────────────────────────────────────────────

/// <summary>Change type for a single diff entry within a section.</summary>
public enum DiffChangeType
{
    /// <summary>Text exists in document B but not in document A.</summary>
    Addition,

    /// <summary>Text exists in document A but not in document B.</summary>
    Deletion,

    /// <summary>Text exists in both documents but the content differs.</summary>
    Modification,

    /// <summary>Text is identical in both documents (omitted from output to save tokens).</summary>
    Unchanged
}

/// <summary>
/// A single content change within one section of the document diff.
/// </summary>
public sealed record DiffChange(
    DiffChangeType ChangeType,
    string? OriginalText,
    string? ModifiedText,
    string? ChangeDescription);

/// <summary>
/// Diff result for a single named section.
/// Sections are identified by heading text or a generated positional label.
/// </summary>
public sealed record SectionDiff(
    string SectionTitle,
    DiffChangeType ChangeType,
    IReadOnlyList<DiffChange> Changes);

/// <summary>
/// Structured diff result produced by <see cref="CompareDocumentsTool.CompareDocumentsAsync"/>.
/// Suitable for direct rendering by the RedlineViewerWidget.
/// </summary>
public sealed record DocumentDiff(
    string DocumentId1,
    string DocumentId2,
    DateTimeOffset ComparedAt,
    int TotalSections,
    int TotalChanges,
    int Additions,
    int Deletions,
    int Modifications,
    IReadOnlyList<SectionDiff> Sections,
    bool IsError = false,
    string? ErrorMessage = null);

// ─── Tool ────────────────────────────────────────────────────────────────────

/// <summary>
/// AI tool that compares two documents from SharePoint Embedded and returns a
/// structured diff grouped by document section.
///
/// <b>Fetch strategy</b>: both documents are downloaded in parallel via
/// <see cref="Task.WhenAll"/> and their text is extracted using
/// <see cref="ITextExtractor"/>. If either document is inaccessible (null stream
/// from SpeFileStore, or failed extraction), the tool returns a structured error
/// result — it does NOT throw.
///
/// <b>Diff algorithm</b>: the extracted text is first split into sections (detected
/// from common heading markers; falls back to paragraph segmentation).  Sections are
/// aligned by title similarity and, within each matched section pair, a word-level
/// LCS diff classifies each block as <see cref="DiffChangeType.Addition"/>,
/// <see cref="DiffChangeType.Deletion"/>, or <see cref="DiffChangeType.Modification"/>.
/// Identical sections are recorded as <see cref="DiffChangeType.Unchanged"/> and
/// omitted from the section change list to keep the payload compact.
///
/// <b>No external NuGet packages</b>: the LCS implementation is self-contained
/// (task acceptance criterion: no diff NuGet dependency).
///
/// <b>ADR-007</b>: all SPE access is routed through <see cref="ISpeFileOperations"/>.
/// <see cref="ISpeFileOperations.DownloadFileAsync"/> is called with app-only auth;
/// OBO auth (DownloadFileAsUserAsync) is used when an <see cref="HttpContext"/> is
/// available. The tool never injects <see cref="Microsoft.Graph.GraphServiceClient"/>
/// directly.
///
/// <b>ADR-010</b>: not registered in DI — factory-instantiated by
/// <see cref="SprkChatAgentFactory.ResolveTools"/>.
/// </summary>
public sealed class CompareDocumentsTool
{
    private readonly IDocumentDataverseService _documentService;
    private readonly ISpeFileOperations _speFileStore;
    private readonly ITextExtractor _textExtractor;
    private readonly HttpContext? _httpContext;
    private readonly ILogger<CompareDocumentsTool> _logger;

    public CompareDocumentsTool(
        IDocumentDataverseService documentService,
        ISpeFileOperations speFileStore,
        ITextExtractor textExtractor,
        HttpContext? httpContext,
        ILogger<CompareDocumentsTool> logger)
    {
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _speFileStore    = speFileStore    ?? throw new ArgumentNullException(nameof(speFileStore));
        _textExtractor   = textExtractor   ?? throw new ArgumentNullException(nameof(textExtractor));
        _httpContext     = httpContext;
        _logger          = logger          ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Compares two documents and returns a structured section-by-section diff.
    ///
    /// Fetches both documents from SharePoint Embedded via the SpeFileStore facade,
    /// extracts their text content, and produces a <see cref="DocumentDiff"/> result
    /// that classifies changes as additions, deletions, or modifications per section.
    /// Identical sections are omitted from the output to keep the payload compact.
    ///
    /// Use this tool when the user asks to compare, redline, or identify differences
    /// between two versions of a document — for example:
    /// "What changed between draft 1 and draft 2?",
    /// "Show me the differences between these two contracts."
    ///
    /// Returns a structured error result (not an exception) when either document is
    /// inaccessible or its text cannot be extracted.
    /// </summary>
    /// <param name="documentId1">
    /// Dataverse sprk_document GUID for the first (original/baseline) document.
    /// Format: "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx".
    /// </param>
    /// <param name="documentId2">
    /// Dataverse sprk_document GUID for the second (revised/modified) document.
    /// Format: "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx".
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A JSON-serialisable <see cref="DocumentDiff"/> record containing per-section
    /// change lists, summary counts (additions, deletions, modifications), and the
    /// comparison timestamp. When a document cannot be accessed or read, the result
    /// has <c>IsError = true</c> and an <c>ErrorMessage</c> describing which document
    /// failed and why.
    /// </returns>
    public async Task<DocumentDiff> CompareDocumentsAsync(
        [Description("Dataverse sprk_document GUID for the first (original/baseline) document, e.g. \"a1b2c3d4-0000-0000-0000-000000000000\"")]
        string documentId1,
        [Description("Dataverse sprk_document GUID for the second (revised/modified) document, e.g. \"b2c3d4e5-0000-0000-0000-000000000001\"")]
        string documentId2,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId1, nameof(documentId1));
        ArgumentException.ThrowIfNullOrEmpty(documentId2, nameof(documentId2));

        _logger.LogInformation(
            "CompareDocumentsTool: comparing documentId1={DocumentId1} vs documentId2={DocumentId2}",
            documentId1, documentId2);

        // ── 1. Fetch both document metadata in parallel ───────────────────────
        DocumentEntity? meta1;
        DocumentEntity? meta2;

        try
        {
            (meta1, meta2) = await FetchMetadataInParallelAsync(documentId1, documentId2, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CompareDocumentsTool: failed to load document metadata");
            return ErrorResult(documentId1, documentId2, $"Failed to load document metadata: {ex.Message}");
        }

        if (meta1 == null)
        {
            return ErrorResult(documentId1, documentId2,
                $"Document '{documentId1}' was not found or is not accessible.");
        }

        if (meta2 == null)
        {
            return ErrorResult(documentId1, documentId2,
                $"Document '{documentId2}' was not found or is not accessible.");
        }

        // ── 2. Download and extract text from both documents in parallel ──────
        string? text1;
        string? text2;
        string? extractError;

        (text1, text2, extractError) = await ExtractBothInParallelAsync(
            meta1, meta2, cancellationToken);

        if (extractError != null)
        {
            return ErrorResult(documentId1, documentId2, extractError);
        }

        // ── 3. Segment text into sections ─────────────────────────────────────
        var sections1 = SegmentIntoSections(text1 ?? string.Empty);
        var sections2 = SegmentIntoSections(text2 ?? string.Empty);

        _logger.LogInformation(
            "CompareDocumentsTool: doc1 has {S1} sections, doc2 has {S2} sections",
            sections1.Count, sections2.Count);

        // ── 4. Diff sections ──────────────────────────────────────────────────
        var sectionDiffs = DiffSections(sections1, sections2);

        var additions    = sectionDiffs.Count(s => s.ChangeType == DiffChangeType.Addition);
        var deletions    = sectionDiffs.Count(s => s.ChangeType == DiffChangeType.Deletion);
        var modifications = sectionDiffs.Count(s => s.ChangeType == DiffChangeType.Modification);
        var totalChanges  = additions + deletions + modifications;

        _logger.LogInformation(
            "CompareDocumentsTool: diff complete — {Total} changes " +
            "({Additions} additions, {Deletions} deletions, {Modifications} modifications)",
            totalChanges, additions, deletions, modifications);

        return new DocumentDiff(
            DocumentId1:   documentId1,
            DocumentId2:   documentId2,
            ComparedAt:    DateTimeOffset.UtcNow,
            TotalSections: sectionDiffs.Count,
            TotalChanges:  totalChanges,
            Additions:     additions,
            Deletions:     deletions,
            Modifications: modifications,
            Sections:      sectionDiffs);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Private: metadata + extraction
    // ═════════════════════════════════════════════════════════════════════════

    private async Task<(DocumentEntity? Meta1, DocumentEntity? Meta2)> FetchMetadataInParallelAsync(
        string documentId1,
        string documentId2,
        CancellationToken cancellationToken)
    {
        var t1 = _documentService.GetDocumentAsync(documentId1, cancellationToken);
        var t2 = _documentService.GetDocumentAsync(documentId2, cancellationToken);
        await Task.WhenAll(t1, t2);
        return (await t1, await t2);
    }

    private async Task<(string? Text1, string? Text2, string? Error)> ExtractBothInParallelAsync(
        DocumentEntity meta1,
        DocumentEntity meta2,
        CancellationToken cancellationToken)
    {
        var t1 = TryExtractTextAsync(meta1, cancellationToken);
        var t2 = TryExtractTextAsync(meta2, cancellationToken);
        await Task.WhenAll(t1, t2);

        var (text1, err1) = await t1;
        var (text2, err2) = await t2;

        if (err1 != null)
            return (null, null, $"Could not read document '{meta1.Id}': {err1}");
        if (err2 != null)
            return (null, null, $"Could not read document '{meta2.Id}': {err2}");

        return (text1, text2, null);
    }

    private async Task<(string? Text, string? Error)> TryExtractTextAsync(
        DocumentEntity document,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
        {
            return (null, "Document has no SPE file reference (missing DriveId or ItemId).");
        }

        Stream? fileStream;
        try
        {
            fileStream = _httpContext != null
                ? await _speFileStore.DownloadFileAsUserAsync(
                    _httpContext,
                    document.GraphDriveId!,
                    document.GraphItemId!,
                    cancellationToken)
                : await _speFileStore.DownloadFileAsync(
                    document.GraphDriveId!,
                    document.GraphItemId!,
                    cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CompareDocumentsTool: failed to download document {DocumentId} from SPE",
                document.Id);
            return (null, $"Failed to download: {ex.Message}");
        }

        if (fileStream == null)
        {
            return (null, "Document stream was null — document may be inaccessible (403/404).");
        }

        using (fileStream)
        {
            var fileName = document.FileName ?? "document";
            var result   = await _textExtractor.ExtractAsync(fileStream, fileName, cancellationToken);

            if (!result.Success)
            {
                return (null, $"Text extraction failed: {result.ErrorMessage}");
            }

            return (result.Text, null);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Private: segmentation
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Splits document text into named sections.
    ///
    /// Detection order:
    ///   1. Numbered headings     — "1.", "2.1.", "III.", "Article 3."
    ///   2. ALL-CAPS lines        — entire non-blank line is uppercase (min 4 chars)
    ///   3. "Section X" prefix    — "Section 1", "SECTION TWO"
    ///
    /// Falls back to paragraph segmentation (double-newline split) when no headings
    /// are detected. Paragraphs are labelled "Paragraph 1", "Paragraph 2", …
    /// </summary>
    internal static IReadOnlyList<(string Title, string Body)> SegmentIntoSections(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<(string, string)>();

        var lines = text.Split('\n');

        // Collect heading-line indices
        var headingIndices = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (IsHeadingLine(lines[i]))
                headingIndices.Add(i);
        }

        if (headingIndices.Count == 0)
        {
            // Fall back to paragraph segmentation
            return SegmentByParagraph(text);
        }

        // Build sections from heading boundaries
        var sections = new List<(string Title, string Body)>();
        for (var h = 0; h < headingIndices.Count; h++)
        {
            var headingLine = headingIndices[h];
            var nextHeading = h + 1 < headingIndices.Count ? headingIndices[h + 1] : lines.Length;
            var title       = lines[headingLine].Trim();
            var bodyLines   = lines[(headingLine + 1)..nextHeading];
            var body        = string.Join('\n', bodyLines).Trim();
            sections.Add((title, body));
        }

        // If there is content before the first heading, prepend it as a preamble section
        if (headingIndices[0] > 0)
        {
            var preamble = string.Join('\n', lines[..headingIndices[0]]).Trim();
            if (!string.IsNullOrWhiteSpace(preamble))
                sections.Insert(0, ("Preamble", preamble));
        }

        return sections;
    }

    private static bool IsHeadingLine(string line)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        // Numbered heading: starts with digit(s)/roman/letter followed by dot(s)
        // e.g. "1.", "2.1.", "1.2.3", "III.", "A."
        if (System.Text.RegularExpressions.Regex.IsMatch(
                trimmed,
                @"^(?:[0-9]+(?:\.[0-9]+)*\.?|[IVXLCDM]+\.|[A-Z]\.)[ \t]",
                System.Text.RegularExpressions.RegexOptions.None))
        {
            return true;
        }

        // ALL-CAPS line (min 4 non-whitespace chars, no lowercase)
        var letters = trimmed.Where(char.IsLetter).ToList();
        if (letters.Count >= 4 && letters.All(char.IsUpper))
            return true;

        // "Section X" prefix (case-insensitive)
        if (System.Text.RegularExpressions.Regex.IsMatch(
                trimmed,
                @"^[Ss]ection\s+\S",
                System.Text.RegularExpressions.RegexOptions.None))
        {
            return true;
        }

        // "Article X" prefix
        if (System.Text.RegularExpressions.Regex.IsMatch(
                trimmed,
                @"^[Aa]rticle\s+\S",
                System.Text.RegularExpressions.RegexOptions.None))
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyList<(string Title, string Body)> SegmentByParagraph(string text)
    {
        // Split on one or more blank lines
        var paragraphs = System.Text.RegularExpressions.Regex
            .Split(text.Trim(), @"\n{2,}")
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();

        var result = new List<(string, string)>(paragraphs.Count);
        for (var i = 0; i < paragraphs.Count; i++)
        {
            // Use the first line as a title-like label, truncated to 60 chars
            var firstLine = paragraphs[i].Split('\n')[0].Trim();
            var label     = firstLine.Length > 60 ? firstLine[..60] + "…" : firstLine;
            result.Add(($"Paragraph {i + 1}: {label}", paragraphs[i]));
        }
        return result;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Private: section-level diff
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Aligns sections from document A and B by title similarity then diffs each pair.
    ///
    /// Matching algorithm:
    ///   1. Exact title match (case-insensitive).
    ///   2. Normalised containment: one title contains the other (after lowercasing
    ///      and stripping punctuation).
    ///   3. No match found → section is an Addition (exists only in B) or Deletion
    ///      (exists only in A).
    ///
    /// Unchanged sections (identical body text) are included in the output with
    /// <see cref="DiffChangeType.Unchanged"/> but have an empty change list.
    /// </summary>
    internal static IReadOnlyList<SectionDiff> DiffSections(
        IReadOnlyList<(string Title, string Body)> sectionsA,
        IReadOnlyList<(string Title, string Body)> sectionsB)
    {
        var result  = new List<SectionDiff>();
        var usedB   = new HashSet<int>();

        // For each section in A, try to find a match in B
        for (var a = 0; a < sectionsA.Count; a++)
        {
            var (titleA, bodyA) = sectionsA[a];
            var matchB          = FindBestMatch(titleA, sectionsB, usedB);

            if (matchB < 0)
            {
                // Section exists only in A → Deletion
                result.Add(new SectionDiff(
                    SectionTitle: titleA,
                    ChangeType:   DiffChangeType.Deletion,
                    Changes:
                    [
                        new DiffChange(DiffChangeType.Deletion, bodyA, null,
                            "Section exists in original document but not in revised document.")
                    ]));
            }
            else
            {
                usedB.Add(matchB);
                var (titleB, bodyB) = sectionsB[matchB];
                var title           = titleA; // keep A's title for baseline

                if (string.Equals(bodyA, bodyB, StringComparison.Ordinal))
                {
                    // Identical section
                    result.Add(new SectionDiff(
                        SectionTitle: title,
                        ChangeType:   DiffChangeType.Unchanged,
                        Changes:      []));
                }
                else
                {
                    // Modified section — compute word-level diff
                    var changes = DiffWords(bodyA, bodyB);
                    result.Add(new SectionDiff(
                        SectionTitle: title,
                        ChangeType:   DiffChangeType.Modification,
                        Changes:      changes));
                }
            }
        }

        // Sections in B that were never matched → Additions
        for (var b = 0; b < sectionsB.Count; b++)
        {
            if (usedB.Contains(b))
                continue;

            var (titleB, bodyB) = sectionsB[b];
            result.Add(new SectionDiff(
                SectionTitle: titleB,
                ChangeType:   DiffChangeType.Addition,
                Changes:
                [
                    new DiffChange(DiffChangeType.Addition, null, bodyB,
                        "Section exists in revised document but not in original document.")
                ]));
        }

        return result;
    }

    /// <summary>Returns the index into <paramref name="candidates"/> that best matches
    /// <paramref name="title"/>, or -1 when no suitable match is found.</summary>
    private static int FindBestMatch(
        string title,
        IReadOnlyList<(string Title, string Body)> candidates,
        IReadOnlySet<int> used)
    {
        // Pass 1: exact match (case-insensitive)
        for (var i = 0; i < candidates.Count; i++)
        {
            if (used.Contains(i)) continue;
            if (string.Equals(title, candidates[i].Title, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        // Pass 2: normalised containment
        var normA = NormaliseTitle(title);
        for (var i = 0; i < candidates.Count; i++)
        {
            if (used.Contains(i)) continue;
            var normB = NormaliseTitle(candidates[i].Title);
            if (normA.Contains(normB, StringComparison.Ordinal) ||
                normB.Contains(normA, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string NormaliseTitle(string title)
    {
        // Lowercase, remove punctuation, collapse whitespace
        var sb = new StringBuilder(title.Length);
        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == ' ')
                sb.Append(c);
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Private: word-level LCS diff (no external NuGet)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Produces a list of <see cref="DiffChange"/> entries by comparing
    /// <paramref name="textA"/> and <paramref name="textB"/> at word granularity.
    ///
    /// Uses the classic LCS (Longest Common Subsequence) algorithm on word tokens.
    /// Consecutive added/deleted words are merged into a single Modification change
    /// when they occur adjacent to each other; lone additions become Addition,
    /// lone deletions become Deletion.
    ///
    /// Time complexity: O(m*n) where m,n are word counts. Documents are truncated
    /// at <see cref="MaxDiffWords"/> words before comparison to bound memory usage.
    /// </summary>
    internal static IReadOnlyList<DiffChange> DiffWords(string textA, string textB)
    {
        var wordsA = TokeniseWords(textA);
        var wordsB = TokeniseWords(textB);

        // Guard: cap word count to avoid quadratic blowup on huge sections
        if (wordsA.Count > MaxDiffWords) wordsA = wordsA[..MaxDiffWords];
        if (wordsB.Count > MaxDiffWords) wordsB = wordsB[..MaxDiffWords];

        var m   = wordsA.Count;
        var n   = wordsB.Count;

        // Build LCS length table
        var dp = new int[m + 1, n + 1];
        for (var i = 1; i <= m; i++)
        for (var j = 1; j <= n; j++)
        {
            dp[i, j] = string.Equals(wordsA[i - 1], wordsB[j - 1], StringComparison.OrdinalIgnoreCase)
                ? dp[i - 1, j - 1] + 1
                : Math.Max(dp[i - 1, j], dp[i, j - 1]);
        }

        // Trace back the edit script
        var edits = new List<(EditKind Kind, string Word)>(m + n);
        var ia = m;
        var ib = n;

        while (ia > 0 || ib > 0)
        {
            if (ia > 0 && ib > 0 &&
                string.Equals(wordsA[ia - 1], wordsB[ib - 1], StringComparison.OrdinalIgnoreCase))
            {
                edits.Add((EditKind.Equal, wordsA[ia - 1]));
                ia--;
                ib--;
            }
            else if (ib > 0 && (ia == 0 || dp[ia, ib - 1] >= dp[ia - 1, ib]))
            {
                edits.Add((EditKind.Insert, wordsB[ib - 1]));
                ib--;
            }
            else
            {
                edits.Add((EditKind.Delete, wordsA[ia - 1]));
                ia--;
            }
        }

        edits.Reverse(); // traceback produces reverse order

        // Condense the flat edit list into DiffChange records
        return CondenseEdits(edits);
    }

    private enum EditKind { Equal, Insert, Delete }

    /// <summary>
    /// Maximum number of words per section body passed to the LCS algorithm.
    /// Bounds worst-case O(m*n) memory to ≈ 400K cells (≈ 1.6 MB of int).
    /// </summary>
    private const int MaxDiffWords = 2_000;

    /// <summary>Splits text into lowercase word tokens (letters/digits only).</summary>
    private static List<string> TokeniseWords(string text)
    {
        var tokens = new List<string>();
        var inWord = false;
        var start  = 0;

        for (var i = 0; i <= text.Length; i++)
        {
            var isWordChar = i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '\'');
            if (isWordChar && !inWord)
            {
                start  = i;
                inWord = true;
            }
            else if (!isWordChar && inWord)
            {
                tokens.Add(text[start..i]);
                inWord = false;
            }
        }

        return tokens;
    }

    /// <summary>
    /// Merges the flat edit-kind list into <see cref="DiffChange"/> records.
    /// Adjacent Insert+Delete (or Delete+Insert) pairs become Modification.
    /// </summary>
    private static IReadOnlyList<DiffChange> CondenseEdits(List<(EditKind Kind, string Word)> edits)
    {
        var changes = new List<DiffChange>();
        var i       = 0;

        while (i < edits.Count)
        {
            var (kind, word) = edits[i];

            if (kind == EditKind.Equal)
            {
                i++;
                continue; // unchanged words are not emitted
            }

            // Collect a run of consecutive non-Equal edits
            var deletedWords = new List<string>();
            var insertedWords = new List<string>();

            while (i < edits.Count && edits[i].Kind != EditKind.Equal)
            {
                if (edits[i].Kind == EditKind.Delete)
                    deletedWords.Add(edits[i].Word);
                else
                    insertedWords.Add(edits[i].Word);
                i++;
            }

            if (deletedWords.Count > 0 && insertedWords.Count > 0)
            {
                var original = string.Join(' ', deletedWords);
                var modified = string.Join(' ', insertedWords);
                changes.Add(new DiffChange(
                    DiffChangeType.Modification,
                    original,
                    modified,
                    $"Text changed from \"{Truncate(original, 80)}\" to \"{Truncate(modified, 80)}\"."));
            }
            else if (deletedWords.Count > 0)
            {
                var original = string.Join(' ', deletedWords);
                changes.Add(new DiffChange(
                    DiffChangeType.Deletion,
                    original,
                    null,
                    $"Removed: \"{Truncate(original, 80)}\"."));
            }
            else if (insertedWords.Count > 0)
            {
                var inserted = string.Join(' ', insertedWords);
                changes.Add(new DiffChange(
                    DiffChangeType.Addition,
                    null,
                    inserted,
                    $"Added: \"{Truncate(inserted, 80)}\"."));
            }
        }

        return changes;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Private: helpers
    // ═════════════════════════════════════════════════════════════════════════

    private static DocumentDiff ErrorResult(string documentId1, string documentId2, string message) =>
        new(
            DocumentId1:   documentId1,
            DocumentId2:   documentId2,
            ComparedAt:    DateTimeOffset.UtcNow,
            TotalSections: 0,
            TotalChanges:  0,
            Additions:     0,
            Deletions:     0,
            Modifications: 0,
            Sections:      [],
            IsError:       true,
            ErrorMessage:  message);

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "…";
}
