using System.Text;
using System.Text.Json.Serialization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-25 (task 051) — deterministic READ-DIRECTION parser that extracts every native Open XML
/// comment (<c>&lt;w:comment&gt;</c> + <c>CommentRangeStart/End</c>) and tracked
/// insertion/deletion (<c>&lt;w:ins&gt;</c> / <c>&lt;w:del&gt;</c>) from a <c>.docx</c> into a
/// structured payload. This is the read counterpart of <see cref="DocxAnnotationWriter"/> (task
/// 050) — a Word-for-Web session (a real human editor, not "Spaarke AI") produces exactly this
/// markup shape, and this reader recovers it with author/date fidelity so task 054's re-anchoring
/// has an input to work against.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure — <c>byte[]</c> in / structured record out (no I/O, no AI, no DI graph reach)</b>. The
/// SPE download hop stays in the endpoint layer via the <c>SpeFileStore</c>/<c>ISpeFileOperations</c>
/// facade (ADR-007); this class never sees a <c>Microsoft.Graph</c> type, an <c>IOpenAiClient</c>,
/// an executor, or a routing type (ADR-013 / NFR-05 — Tier-1 NetArchTest enforces).
/// </para>
/// <para>
/// <b>Grounded in Spike 6</b> (<c>notes/spikes/spike-6-word-roundtrip.md</c>, task 006): a
/// genuinely Word-authored comment + tracked insertion + tracked deletion round-tripped with
/// 100% author/date fidelity using the query shapes below. Gotchas encoded as
/// <c>// EDGE-R{N}:</c> comments:
/// EDGE-R1 <c>w:del</c> text is <c>DeletedText</c> (<c>w:delText</c>), never <c>Text</c>
/// (<c>w:t</c>) — mirrors the writer's EDGE-4, confirmed on the read side too;
/// EDGE-R2 <c>w:date</c> reads as an already-parsed <c>DateTime</c> with
/// <c>Kind == Unspecified</c> — must be normalized to UTC explicitly, not re-parsed from a
/// string;
/// EDGE-R3 a comment's anchored range text requires a second cross-reference (walking body
/// content between <c>CommentRangeStart</c>/<c>CommentRangeEnd</c> sharing the comment's
/// <c>w:id</c>) — the comment's own body text (in <c>comments.xml</c>) is a separate thing;
/// EDGE-R4 the anchor-range walk spans paragraph boundaries (Word allows a multi-paragraph
/// comment range) — flagged by spike 6 as NOT solved by its prototype; this reader fixes it by
/// walking <see cref="Body.Descendants()"/> (document order, flattened across paragraphs)
/// rather than one paragraph's direct children.
/// </para>
/// <para>
/// <b>Zero package delta (NFR-01 / ADR-029)</b>: <c>DocumentFormat.OpenXml</c> 3.4.1 is ALREADY a
/// BFF dependency (added for task 050 / Spike 5). This task adds no <c>&lt;PackageReference&gt;</c>.
/// </para>
/// <para>
/// <b>Thread-safe stateless singleton-shaped</b>: no mutable instance state; safe to share a
/// single instance across concurrent requests (ADR-010), though callers may also construct a
/// fresh instance per call (used directly, not DI-registered, per this task's write-boundary
/// scope — see class remarks on <c>ComposeEndpoints.PullAnnotations</c>).
/// </para>
/// </remarks>
public sealed class DocxAnnotationReader
{
    /// <summary>
    /// Parses <paramref name="docx"/> for every <c>w:comment</c> and tracked
    /// insertion/deletion, returning a structured, stable payload. A document with zero
    /// annotations returns an empty (not null, not thrown) payload — the "no error on empty"
    /// contract (FR-25 acceptance criterion).
    /// </summary>
    /// <param name="docx">Source <c>.docx</c> bytes (a valid WordprocessingML OPC package).</param>
    /// <returns>A <see cref="DocxAnnotationReadResult"/> carrying every recovered comment +
    /// revision, in document order.</returns>
    /// <exception cref="ArgumentException"><paramref name="docx"/> is null or empty.</exception>
    /// <exception cref="DocxAnnotationException"><paramref name="docx"/> is not a readable DOCX
    /// (<see cref="DocxAnnotationErrorKind.MalformedDocument"/>).</exception>
    public DocxAnnotationReadResult Read(byte[] docx)
    {
        if (docx is null || docx.Length == 0)
        {
            throw new ArgumentException("docx is required and must be non-empty.", nameof(docx));
        }

        using var buffer = new MemoryStream();
        buffer.Write(docx, 0, docx.Length);
        buffer.Position = 0;

        WordprocessingDocument doc;
        try
        {
            // Read-only open — the reader never mutates the source package.
            doc = WordprocessingDocument.Open(buffer, isEditable: false);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException or ArgumentOutOfRangeException)
        {
            throw new DocxAnnotationException(
                DocxAnnotationErrorKind.MalformedDocument,
                "The supplied bytes are not a readable .docx (WordprocessingML) package.",
                ex);
        }

        using (doc)
        {
            var mainPart = doc.MainDocumentPart
                ?? throw new DocxAnnotationException(
                    DocxAnnotationErrorKind.MalformedDocument,
                    "The .docx has no main document part.");

            var body = mainPart.Document?.Body
                ?? throw new DocxAnnotationException(
                    DocxAnnotationErrorKind.MalformedDocument,
                    "The .docx main document part has no body.");

            // Materialize paragraph document-order once; both comment + revision extraction
            // resolve a ParagraphHint (index) against this same list, matching the re-anchor
            // scorer's `bestParagraphIndex` structural-proximity input (Spike 6 §5.1).
            var paragraphs = body.Descendants<Paragraph>().ToList();

            var comments = ReadComments(mainPart, body, paragraphs);
            var revisions = ReadRevisions(body, paragraphs);

            return new DocxAnnotationReadResult(comments, revisions);
        }
    }

    // -- Comments -------------------------------------------------------------------------------

    private static IReadOnlyList<RecoveredComment> ReadComments(
        MainDocumentPart mainPart, Body body, List<Paragraph> paragraphs)
    {
        var commentsPart = mainPart.WordprocessingCommentsPart;
        if (commentsPart?.Comments is null)
        {
            // No comments part at all → empty, not an error (FR-25 empty-document criterion).
            return Array.Empty<RecoveredComment>();
        }

        var results = new List<RecoveredComment>();
        foreach (var comment in commentsPart.Comments.Elements<Comment>())
        {
            var id = comment.Id?.Value;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var (anchorText, paragraphHint) = ExtractAnchorRange(body, paragraphs, id);

            results.Add(new RecoveredComment(
                Id: id,
                Author: comment.Author?.Value ?? string.Empty,
                Date: NormalizeUtc(comment.Date?.Value),
                CommentText: comment.InnerText,
                AnchorText: anchorText,
                ParagraphHint: paragraphHint));
        }

        return results;
    }

    /// <summary>
    /// EDGE-R3/EDGE-R4: walks <paramref name="body"/> in document order (flattened via
    /// <see cref="OpenXmlElement.Descendants()"/>, which spans paragraph boundaries) collecting
    /// live (non-deleted) run text between the <c>CommentRangeStart</c>/<c>CommentRangeEnd</c>
    /// markers sharing <paramref name="commentId"/> — this is the "what the comment is about"
    /// text, distinct from the comment's own body (which lives in <c>comments.xml</c> and is
    /// read directly via <c>Comment.InnerText</c>). This is the re-anchor <c>textPattern</c>
    /// input (design.md §3 <c>AnchoredAnnotation.anchor</c>) task 054 will match against a
    /// reloaded document. Returns ("", -1) if the range markers are missing (malformed/edited
    /// comments part) — a soft-fail rather than throwing, consistent with "empty payload, not
    /// error" for degraded input.
    /// </summary>
    private static (string AnchorText, int ParagraphHint) ExtractAnchorRange(
        Body body, List<Paragraph> paragraphs, string commentId)
    {
        var sb = new StringBuilder();
        var inRange = false;
        var paragraphHint = -1;

        foreach (var element in body.Descendants())
        {
            if (!inRange && element is CommentRangeStart start && string.Equals(start.Id?.Value, commentId, StringComparison.Ordinal))
            {
                inRange = true;
                var paragraph = start.Ancestors<Paragraph>().FirstOrDefault();
                paragraphHint = paragraph is null ? -1 : paragraphs.IndexOf(paragraph);
                continue;
            }

            if (inRange && element is CommentRangeEnd end && string.Equals(end.Id?.Value, commentId, StringComparison.Ordinal))
            {
                break;
            }

            // Only live text (w:t under a plain Run) counts as anchor content — deleted text
            // (w:delText) inside the range is no longer "what the comment is about" going
            // forward, mirroring the writer's own live/tracked distinction.
            if (inRange && element is Text text && element.Parent is Run)
            {
                sb.Append(text.Text);
            }
        }

        return (sb.ToString(), paragraphHint);
    }

    // -- Revisions (insertions + deletions) ------------------------------------------------------

    private static IReadOnlyList<RecoveredRevision> ReadRevisions(Body body, List<Paragraph> paragraphs)
    {
        var results = new List<RecoveredRevision>();

        // Single document-order walk so insertions and deletions interleave in the order Word
        // actually produced them, rather than being grouped by kind.
        foreach (var element in body.Descendants())
        {
            switch (element)
            {
                case InsertedRun ins:
                    results.Add(BuildRevision(
                        RecoveredAnnotationKind.Insertion,
                        ins.Id?.Value,
                        ins.Author?.Value,
                        ins.Date?.Value,
                        // Live inserted text is plain w:t inside the w:ins wrapper.
                        string.Concat(ins.Descendants<Text>().Select(t => t.Text)),
                        ins,
                        paragraphs));
                    break;

                case DeletedRun del:
                    results.Add(BuildRevision(
                        RecoveredAnnotationKind.Deletion,
                        del.Id?.Value,
                        del.Author?.Value,
                        del.Date?.Value,
                        // EDGE-R1: deleted text is DeletedText (w:delText), never Text (w:t) —
                        // querying Descendants<Text>() on a DeletedRun returns nothing.
                        string.Concat(del.Descendants<DeletedText>().Select(t => t.Text)),
                        del,
                        paragraphs));
                    break;
            }
        }

        return results;
    }

    private static RecoveredRevision BuildRevision(
        RecoveredAnnotationKind kind,
        string? id,
        string? author,
        DateTime? date,
        string text,
        OpenXmlElement element,
        List<Paragraph> paragraphs)
    {
        var paragraph = element.Ancestors<Paragraph>().FirstOrDefault();
        var paragraphHint = paragraph is null ? -1 : paragraphs.IndexOf(paragraph);

        // AnchorText for a revision is the paragraph's own LIVE (non-tracked) text — stable
        // context to re-locate the paragraph on reload regardless of the tracked change itself,
        // same convention DocxAnnotationWriter.GetParagraphText uses (direct-child runs only).
        var anchorText = paragraph is null ? string.Empty : GetParagraphText(paragraph);

        return new RecoveredRevision(
            Kind: kind,
            Id: id ?? string.Empty,
            Author: author ?? string.Empty,
            Date: NormalizeUtc(date),
            Text: text,
            AnchorText: anchorText,
            ParagraphHint: paragraphHint);
    }

    /// <summary>
    /// Direct-child-run plain text only (mirrors <c>DocxAnnotationWriter.GetParagraphText</c>'s
    /// accounting) — runs wrapped in <c>w:ins</c>/<c>w:del</c>/comment marks are one level
    /// deeper and are deliberately excluded, so the anchor reflects only content Word treats as
    /// "settled" (not itself mid-revision).
    /// </summary>
    private static string GetParagraphText(Paragraph paragraph)
    {
        var sb = new StringBuilder();
        foreach (var run in paragraph.Elements<Run>())
        {
            var text = run.GetFirstChild<Text>()?.Text;
            if (!string.IsNullOrEmpty(text))
            {
                sb.Append(text);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// EDGE-R2: <c>w:date</c> reads as an already-parsed <see cref="DateTime"/> with
    /// <c>Kind == Unspecified</c> — normalize explicitly to UTC (matches the writer's own
    /// convention of always emitting UTC into <c>w:date</c>, and Word's own convention).
    /// </summary>
    private static DateTimeOffset NormalizeUtc(DateTime? value)
    {
        if (value is null)
        {
            return default;
        }

        var utc = DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
        return new DateTimeOffset(utc);
    }
}

/// <summary>The kind of recovered annotation — mirrors <see cref="TrackChangeKind"/>'s
/// vocabulary (Insertion/Deletion) plus a distinct Comment kind for the comment-specific fields
/// (<see cref="RecoveredComment"/> is a separate record; this enum tags
/// <see cref="RecoveredRevision.Kind"/> only, so it is intentionally a 2-value subset here).</summary>
[JsonConverter(typeof(CamelCaseStringEnumConverter))]
public enum RecoveredAnnotationKind
{
    /// <summary>A tracked insertion (<c>&lt;w:ins&gt;</c>).</summary>
    Insertion,

    /// <summary>A tracked deletion (<c>&lt;w:del&gt;</c>).</summary>
    Deletion,
}

/// <summary>
/// One recovered native comment (<c>&lt;w:comment&gt;</c>), read back with full author/date
/// fidelity (FR-25 acceptance criterion). <see cref="AnchorText"/> is the anchored document range
/// (what the comment is about), NOT the comment's own body — that is
/// <see cref="CommentText"/>. <see cref="ParagraphHint"/> is the 0-based document-order index of
/// the paragraph the comment's range starts in — the structural half of task 054's re-anchor
/// score (Spike 6 §5.1 <c>structuralProximity</c>).
/// </summary>
public sealed record RecoveredComment(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("date")] DateTimeOffset Date,
    [property: JsonPropertyName("commentText")] string CommentText,
    [property: JsonPropertyName("anchorText")] string AnchorText,
    [property: JsonPropertyName("paragraphHint")] int ParagraphHint);

/// <summary>
/// One recovered tracked insertion or deletion, read back with full author/date fidelity (FR-25
/// acceptance criterion). <see cref="Text"/> is the inserted (or deleted) text itself;
/// <see cref="AnchorText"/> is the containing paragraph's settled (non-tracked) text, giving
/// task 054's re-anchor scorer stable context independent of the tracked change.
/// </summary>
public sealed record RecoveredRevision(
    [property: JsonPropertyName("kind")] RecoveredAnnotationKind Kind,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("date")] DateTimeOffset Date,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("anchorText")] string AnchorText,
    [property: JsonPropertyName("paragraphHint")] int ParagraphHint);

/// <summary>
/// The full structured payload <see cref="DocxAnnotationReader.Read"/> returns — every recovered
/// comment + revision, in document order within each list. Empty lists (not null, not an
/// exception) represent a document with no annotations (FR-25 empty-document criterion).
/// </summary>
public sealed record DocxAnnotationReadResult(
    [property: JsonPropertyName("comments")] IReadOnlyList<RecoveredComment> Comments,
    [property: JsonPropertyName("revisions")] IReadOnlyList<RecoveredRevision> Revisions);
