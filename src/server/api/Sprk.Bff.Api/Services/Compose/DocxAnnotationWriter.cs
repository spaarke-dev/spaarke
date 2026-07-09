using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-24 (task 050) — deterministic WRITE-DIRECTION renderer that materializes accepted Compose
/// annotations (track-change insertions/deletions + user/AI comments) into a <c>.docx</c> as
/// NATIVE Open XML <c>&lt;w:ins&gt;</c> / <c>&lt;w:del&gt;</c> / <c>&lt;w:comment&gt;</c> markup, so
/// Word for Web renders them as first-class revisions/comments (Accept/Reject, Resolve/Reply).
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure — <c>byte[]</c> in / <c>byte[]</c> out (no I/O, no AI, no DI graph reach)</b>. The SPE
/// hop stays in the endpoint/service layer via the <c>SpeFileStore</c> facade (ADR-007); this class
/// never sees a <c>Microsoft.Graph</c> type, an <c>IOpenAiClient</c>, an executor, or a routing type
/// (ADR-013 / NFR-05 — Tier-1 NetArchTest enforces). It is the WRITE counterpart of the
/// read-direction <see cref="CriticMarkupRenderer"/> (FR-22) and reuses its
/// <see cref="TrackChangeKind"/> vocabulary rather than inventing a parallel kind axis.
/// </para>
/// <para>
/// <b>Zero package delta (NFR-01 / ADR-029)</b>: <c>DocumentFormat.OpenXml</c> 3.4.1 is ALREADY a
/// BFF dependency (<c>Sprk.Bff.Api.csproj</c>) — Spike 5 (task 005) validated this element structure
/// against a real <c>.docx</c> at 0 <c>OpenXmlValidator</c> errors and confirmed no new package is
/// required (<c>Codeuctivity.OpenXmlPowerTools</c> is NOT needed for the writer path). This task adds
/// no <c>&lt;PackageReference&gt;</c>.
/// </para>
/// <para>
/// <b>Thread-safe singleton</b>: the public surface is stateless; all mutable per-call state
/// (monotonic revision-id counter, the open package) lives in a per-invocation
/// <see cref="AnnotationSession"/>, so the DI-registered singleton is safe under concurrency
/// (ADR-010).
/// </para>
/// <para>
/// <b>Edge-case wisdom (design.md §6.2; Spike 5)</b> is encoded as <c>// EDGE-N:</c> comments:
/// EDGE-1 comments-before-track-changes pipeline ordering; EDGE-2 paragraph-boundary deletion via
/// <c>w:pPr/w:rPr/w:del</c>; EDGE-3 monotonic per-document revision id seeded past any existing id;
/// EDGE-4 <c>w:del</c> text is <c>DeletedText</c> (<c>w:delText</c>), never <c>Text</c> (<c>w:t</c>).
/// </para>
/// </remarks>
public sealed class DocxAnnotationWriter
{
    /// <summary>
    /// Renders <paramref name="annotations"/> onto <paramref name="sourceDocx"/> as native Open XML
    /// revision + comment markup and returns the rewritten <c>.docx</c> bytes. Untouched runs and
    /// paragraphs are left structurally intact (no markup is added to content no annotation targets).
    /// </summary>
    /// <param name="sourceDocx">Source <c>.docx</c> bytes (a valid WordprocessingML OPC package).</param>
    /// <param name="annotations">Accepted annotations to materialize. Empty is valid (returns a
    /// structural round-trip of the input with no markup added).</param>
    /// <returns>The annotated <c>.docx</c> bytes.</returns>
    /// <exception cref="ArgumentException"><paramref name="sourceDocx"/> is null/empty, or an
    /// individual annotation is internally inconsistent (e.g. an insertion with no text).</exception>
    /// <exception cref="DocxAnnotationException"><paramref name="sourceDocx"/> is not a readable
    /// DOCX (<see cref="DocxAnnotationErrorKind.MalformedDocument"/>), or an annotation's
    /// <c>target_text</c> was not found in the document
    /// (<see cref="DocxAnnotationErrorKind.TargetNotFound"/>). Nothing is partially written — the
    /// throw happens before any bytes are returned.</exception>
    public byte[] Annotate(byte[] sourceDocx, IReadOnlyList<DocxAnnotation> annotations)
    {
        if (sourceDocx is null || sourceDocx.Length == 0)
        {
            throw new ArgumentException("sourceDocx is required and must be non-empty.", nameof(sourceDocx));
        }

        ArgumentNullException.ThrowIfNull(annotations);
        foreach (var annotation in annotations)
        {
            annotation.Validate();
        }

        // Own a private, resizable copy of the bytes so WordprocessingDocument can edit in place and
        // we can hand back the flushed result. Do NOT open over the caller's array.
        using var buffer = new MemoryStream();
        buffer.Write(sourceDocx, 0, sourceDocx.Length);
        buffer.Position = 0;

        WordprocessingDocument doc;
        try
        {
            doc = WordprocessingDocument.Open(buffer, isEditable: true);
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

            var session = new AnnotationSession(mainPart, body);
            session.Apply(annotations);

            mainPart.Document.Save();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Per-invocation mutable state + the OOXML mutation logic. Instantiated once per
    /// <see cref="Annotate"/> call so <see cref="DocxAnnotationWriter"/> stays a thread-safe
    /// stateless singleton.
    /// </summary>
    private sealed class AnnotationSession
    {
        private readonly MainDocumentPart _mainPart;
        private readonly Body _body;
        private int _idCounter;
        private WordprocessingCommentsPart? _commentsPart;

        public AnnotationSession(MainDocumentPart mainPart, Body body)
        {
            _mainPart = mainPart;
            _body = body;
            _idCounter = SeedRevisionId(mainPart, body);
        }

        public void Apply(IReadOnlyList<DocxAnnotation> annotations)
        {
            // EDGE-1: comments are emitted BEFORE track changes. A comment anchors an existing
            // run range; if a deletion converted that range to <w:del> first, the comment anchor
            // would attach to now-deleted content (orphaned balloon). Applying every comment first
            // guarantees comment anchors land on still-live runs (adeu; pandoc #9833).
            foreach (var annotation in annotations.Where(a => a.Kind == TrackChangeKind.Comment))
            {
                ApplyComment(annotation);
            }

            foreach (var annotation in annotations.Where(a => a.Kind != TrackChangeKind.Comment))
            {
                switch (annotation.Kind)
                {
                    case TrackChangeKind.Insertion:
                        ApplyInsertion(annotation);
                        break;
                    case TrackChangeKind.Deletion:
                        ApplyDeletion(annotation);
                        break;
                }
            }

            // Persist the comments part only if we actually added a comment.
            _commentsPart?.Comments?.Save();
        }

        // -- Comment ----------------------------------------------------------------------------

        private void ApplyComment(DocxAnnotation annotation)
        {
            var (paragraph, matchStart) = LocateTarget(annotation.TargetText!);
            var matched = IsolateRange(paragraph, matchStart, annotation.TargetText!.Length);
            var first = matched[0];
            var last = matched[^1];

            var id = NextId();

            // Three-part comment invariant (Spike 5 §3c): matching w:id across
            // commentRangeStart/End/Reference in the body, a <w:comment> of the same id in
            // comments.xml, and the WordprocessingCommentsPart relationship (AddNewPart wires
            // [Content_Types].xml + document.xml.rels automatically).
            first.InsertBeforeSelf(new CommentRangeStart { Id = id });
            var endMark = new CommentRangeEnd { Id = id };
            last.InsertAfterSelf(endMark);
            endMark.InsertAfterSelf(new Run(new CommentReference { Id = id }));

            var commentsPart = EnsureCommentsPart();
            var comment = new Comment
            {
                Id = id,
                Author = annotation.Author,
                Initials = annotation.Initials ?? DeriveInitials(annotation.Author),
                Date = annotation.Date.UtcDateTime,
            };
            comment.AppendChild(new Paragraph(new Run(new Text(annotation.CommentText!)
            {
                Space = SpaceProcessingModeValues.Preserve,
            })));
            commentsPart.Comments!.AppendChild(comment);
        }

        // -- Insertion --------------------------------------------------------------------------

        private void ApplyInsertion(DocxAnnotation annotation)
        {
            // <w:ins> WRAPS normal runs (Spike 5 §3a). Author/Date/Id are attributes on the w:ins.
            var ins = new InsertedRun
            {
                Id = NextId(),
                Author = annotation.Author,
                Date = annotation.Date.UtcDateTime,
            };
            ins.AppendChild(new Run(new Text(annotation.NewText!)
            {
                Space = SpaceProcessingModeValues.Preserve,
            }));

            if (string.IsNullOrEmpty(annotation.TargetText))
            {
                // No anchor: append the tracked insertion at the end of the last body paragraph
                // (draft-into-editor "insert at cursor/append" — ComposeDraftPayload match_mode
                // "insert"). Create a paragraph if the body has none.
                var target = _body.Elements<Paragraph>().LastOrDefault();
                if (target is null)
                {
                    target = new Paragraph();
                    _body.AppendChild(target);
                }

                target.AppendChild(ins);
                return;
            }

            // Anchored insertion: place the tracked insertion immediately AFTER the located anchor.
            var (paragraph, matchStart) = LocateTarget(annotation.TargetText);
            var matched = IsolateRange(paragraph, matchStart, annotation.TargetText.Length);
            matched[^1].InsertAfterSelf(ins);
        }

        // -- Deletion ---------------------------------------------------------------------------

        private void ApplyDeletion(DocxAnnotation annotation)
        {
            var (paragraph, matchStart) = LocateTarget(annotation.TargetText!);
            var matched = IsolateRange(paragraph, matchStart, annotation.TargetText!.Length);

            foreach (var run in matched)
            {
                var text = run.GetFirstChild<Text>();
                if (text is null)
                {
                    continue;
                }

                // EDGE-4: inside <w:del> the text element MUST be DeletedText (w:delText), NOT
                // Text (w:t). Using Text produces a file Word treats as corrupt (Spike 5 §3b).
                var innerRun = new Run();
                var runProps = run.GetFirstChild<RunProperties>();
                if (runProps is not null)
                {
                    innerRun.AppendChild((RunProperties)runProps.CloneNode(true));
                }

                innerRun.AppendChild(new DeletedText(text.Text)
                {
                    Space = SpaceProcessingModeValues.Preserve,
                });

                var del = new DeletedRun
                {
                    Id = NextId(),
                    Author = annotation.Author,
                    Date = annotation.Date.UtcDateTime,
                };
                del.AppendChild(innerRun);

                run.Parent!.ReplaceChild(del, run);
            }

            if (annotation.DeleteParagraphMark)
            {
                // EDGE-2: a paragraph-boundary deletion also removes the paragraph mark itself,
                // represented by a <w:del> inside the paragraph mark's run properties
                // (w:pPr/w:rPr/w:del) — adeu BUG-23-3. Without this, deleting to end-of-paragraph
                // leaves a dangling empty paragraph instead of merging with the next.
                var paragraphProps = paragraph.GetFirstChild<ParagraphProperties>();
                if (paragraphProps is null)
                {
                    paragraphProps = new ParagraphProperties();
                    paragraph.PrependChild(paragraphProps);
                }

                var markProps = paragraphProps.GetFirstChild<ParagraphMarkRunProperties>();
                if (markProps is null)
                {
                    markProps = new ParagraphMarkRunProperties();
                    // w:rPr (paragraph mark run props) sits near the END of w:pPr's schema sequence,
                    // so append rather than prepend to stay valid when the paragraph already has a
                    // style / other pPr children.
                    paragraphProps.AppendChild(markProps);
                }

                markProps.PrependChild(new Deleted
                {
                    Id = NextId(),
                    Author = annotation.Author,
                    Date = annotation.Date.UtcDateTime,
                });
            }
        }

        // -- Location + run isolation -----------------------------------------------------------

        /// <summary>
        /// Finds the first body paragraph (document order) whose concatenated run text contains
        /// <paramref name="target"/>, returning that paragraph and the character offset of the
        /// match within the paragraph's run text. Upstream FR-19 validation guarantees an
        /// unambiguous target; this writer resolves the first occurrence deterministically.
        /// </summary>
        private (Paragraph Paragraph, int MatchStart) LocateTarget(string target)
        {
            foreach (var paragraph in _body.Descendants<Paragraph>())
            {
                var text = GetParagraphText(paragraph);
                var index = text.IndexOf(target, StringComparison.Ordinal);
                if (index >= 0)
                {
                    return (paragraph, index);
                }
            }

            throw new DocxAnnotationException(
                DocxAnnotationErrorKind.TargetNotFound,
                $"Annotation target text was not found in the document: \"{Truncate(target)}\".");
        }

        /// <summary>
        /// Splits the paragraph's runs so that the character range
        /// <c>[matchStart, matchStart+length)</c> is covered by whole runs, and returns those runs
        /// in document order. Only runs carrying a single <c>w:t</c> participate in the character
        /// accounting (comment marks / already-tracked runs contribute no plain-text length), which
        /// keeps the offset model identical to <see cref="GetParagraphText"/>.
        /// </summary>
        private static List<Run> IsolateRange(Paragraph paragraph, int matchStart, int length)
        {
            var matchEnd = matchStart + length;
            SplitRunAtOffset(paragraph, matchStart);
            SplitRunAtOffset(paragraph, matchEnd);

            var result = new List<Run>();
            var pos = 0;
            foreach (var run in paragraph.Elements<Run>())
            {
                var text = run.GetFirstChild<Text>()?.Text;
                var len = text?.Length ?? 0;
                if (len == 0)
                {
                    continue;
                }

                var runStart = pos;
                var runEnd = pos + len;
                if (runStart >= matchStart && runEnd <= matchEnd)
                {
                    result.Add(run);
                }

                pos = runEnd;
            }

            return result;
        }

        /// <summary>
        /// Ensures a run boundary exists at character offset <paramref name="offset"/> by splitting
        /// the containing run (preserving its <c>w:rPr</c>) when the offset falls strictly inside a
        /// run. A no-op when <paramref name="offset"/> is already on a run boundary.
        /// </summary>
        private static void SplitRunAtOffset(Paragraph paragraph, int offset)
        {
            var pos = 0;
            foreach (var run in paragraph.Elements<Run>().ToList())
            {
                var textEl = run.GetFirstChild<Text>();
                var len = textEl?.Text.Length ?? 0;
                if (len == 0)
                {
                    continue;
                }

                var runStart = pos;
                var runEnd = pos + len;
                if (offset > runStart && offset < runEnd)
                {
                    var splitAt = offset - runStart;
                    var leftText = textEl!.Text.Substring(0, splitAt);
                    var rightText = textEl.Text.Substring(splitAt);

                    var rightRun = (Run)run.CloneNode(true);
                    rightRun.GetFirstChild<Text>()!.Text = rightText;
                    rightRun.GetFirstChild<Text>()!.Space = SpaceProcessingModeValues.Preserve;

                    textEl.Text = leftText;
                    textEl.Space = SpaceProcessingModeValues.Preserve;

                    run.InsertAfterSelf(rightRun);
                    return;
                }

                pos = runEnd;
            }
        }

        private static string GetParagraphText(Paragraph paragraph)
        {
            // Only direct-child runs' first Text elements — matches the accounting in
            // IsolateRange / SplitRunAtOffset so offsets are consistent.
            var sb = new System.Text.StringBuilder();
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

        // -- Ids + parts ------------------------------------------------------------------------

        private WordprocessingCommentsPart EnsureCommentsPart()
        {
            if (_commentsPart is not null)
            {
                return _commentsPart;
            }

            _commentsPart = _mainPart.GetPartsOfType<WordprocessingCommentsPart>().FirstOrDefault();
            if (_commentsPart is null)
            {
                _commentsPart = _mainPart.AddNewPart<WordprocessingCommentsPart>();
                _commentsPart.Comments = new Comments();
            }
            else
            {
                _commentsPart.Comments ??= new Comments();
            }

            return _commentsPart;
        }

        private string NextId() => (++_idCounter).ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// EDGE-3: seed the revision-id counter PAST the maximum numeric <c>w:id</c> already present
        /// on any <c>&lt;w:ins&gt;</c> / <c>&lt;w:del&gt;</c> / <c>&lt;w:comment&gt;</c> /
        /// <c>commentRangeStart</c> so newly-emitted ids are unique + monotonic within the document
        /// (duplicate ids make Word drop revisions/comments silently).
        /// </summary>
        private static int SeedRevisionId(MainDocumentPart mainPart, Body body)
        {
            var max = 0;
            foreach (var id in EnumerateExistingIds(mainPart, body))
            {
                if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > max)
                {
                    max = value;
                }
            }

            return max;
        }

        private static IEnumerable<string> EnumerateExistingIds(MainDocumentPart mainPart, Body body)
        {
            foreach (var ins in body.Descendants<InsertedRun>())
            {
                if (ins.Id?.Value is { } v)
                {
                    yield return v;
                }
            }

            foreach (var del in body.Descendants<DeletedRun>())
            {
                if (del.Id?.Value is { } v)
                {
                    yield return v;
                }
            }

            foreach (var crs in body.Descendants<CommentRangeStart>())
            {
                if (crs.Id?.Value is { } v)
                {
                    yield return v;
                }
            }

            var commentsPart = mainPart.GetPartsOfType<WordprocessingCommentsPart>().FirstOrDefault();
            if (commentsPart?.Comments is { } comments)
            {
                foreach (var comment in comments.Elements<Comment>())
                {
                    if (comment.Id?.Value is { } v)
                    {
                        yield return v;
                    }
                }
            }
        }

        private static string DeriveInitials(string author)
        {
            var parts = author.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "?";
            }

            var initials = string.Concat(parts.Take(3).Select(p => char.ToUpperInvariant(p[0])));
            return initials.Length == 0 ? "?" : initials;
        }

        private static string Truncate(string value) =>
            value.Length <= 40 ? value : value[..40] + "…";
    }
}

/// <summary>
/// One accepted Compose annotation to materialize as native Open XML markup by
/// <see cref="DocxAnnotationWriter"/>. Reuses the read-direction <see cref="TrackChangeKind"/>
/// vocabulary and the compose-disposition edit-payload field names (<c>target_text</c> /
/// <c>new_text</c>, per <c>ComposeDraftPayload</c> / <c>ProposedEdit</c>) rather than inventing a
/// parallel schema. BFF-authored + client-facing (the Compose frontend assembles accepted edits +
/// comments), so JSON is camelCase per the <c>ComposeEndpoints</c> convention.
/// </summary>
public sealed record DocxAnnotation
{
    /// <summary>Which kind of native markup to emit: insertion (<c>w:ins</c>), deletion
    /// (<c>w:del</c>), or comment (<c>w:comment</c>).</summary>
    [JsonPropertyName("kind")]
    public required TrackChangeKind Kind { get; init; }

    /// <summary>
    /// The existing document text the annotation targets — the deleted span (Deletion), the
    /// anchored span (Comment), or the anchor a tracked insertion is placed AFTER (Insertion,
    /// optional). Null/empty on an Insertion means "insert at end / append" (draft-into-editor).
    /// Mirrors <c>target_text</c> in the compose edit payload.
    /// </summary>
    [JsonPropertyName("targetText")]
    public string? TargetText { get; init; }

    /// <summary>The inserted text for an Insertion. Mirrors <c>new_text</c>. Required for Insertion,
    /// ignored otherwise.</summary>
    [JsonPropertyName("newText")]
    public string? NewText { get; init; }

    /// <summary>The comment body for a Comment. Required for Comment, ignored otherwise.</summary>
    [JsonPropertyName("commentText")]
    public string? CommentText { get; init; }

    /// <summary>Revision/comment author (surfaces as Word's attribution). Required.</summary>
    [JsonPropertyName("author")]
    public required string Author { get; init; }

    /// <summary>Optional author initials for the comment balloon; derived from
    /// <see cref="Author"/> when omitted.</summary>
    [JsonPropertyName("initials")]
    public string? Initials { get; init; }

    /// <summary>Revision/comment timestamp (serialized UTC ISO-8601 in the <c>w:date</c> attribute).</summary>
    [JsonPropertyName("date")]
    public required DateTimeOffset Date { get; init; }

    /// <summary>
    /// EDGE-2: when true on a Deletion, also delete the paragraph mark (<c>w:pPr/w:rPr/w:del</c>) so
    /// the paragraph merges with the next on Accept — a paragraph-boundary deletion. Default false.
    /// </summary>
    [JsonPropertyName("deleteParagraphMark")]
    public bool DeleteParagraphMark { get; init; }

    /// <summary>Guards internal consistency before any package mutation (fail-before-write).</summary>
    /// <exception cref="ArgumentException">The annotation is missing a field its kind requires.</exception>
    public void Validate()
    {
        switch (Kind)
        {
            case TrackChangeKind.Insertion:
                if (string.IsNullOrEmpty(NewText))
                {
                    throw new ArgumentException("An Insertion annotation requires non-empty newText.");
                }

                break;

            case TrackChangeKind.Deletion:
                if (string.IsNullOrEmpty(TargetText))
                {
                    throw new ArgumentException("A Deletion annotation requires non-empty targetText.");
                }

                break;

            case TrackChangeKind.Comment:
                if (string.IsNullOrEmpty(TargetText))
                {
                    throw new ArgumentException("A Comment annotation requires non-empty targetText to anchor to.");
                }

                if (string.IsNullOrEmpty(CommentText))
                {
                    throw new ArgumentException("A Comment annotation requires non-empty commentText.");
                }

                break;

            default:
                throw new ArgumentException($"Unknown annotation kind: {Kind}.");
        }

        if (string.IsNullOrWhiteSpace(Author))
        {
            throw new ArgumentException("An annotation requires a non-empty author.");
        }
    }
}

/// <summary>The category of a <see cref="DocxAnnotationException"/>.</summary>
public enum DocxAnnotationErrorKind
{
    /// <summary>The supplied bytes are not a readable DOCX package → HTTP 400.</summary>
    MalformedDocument,

    /// <summary>An annotation's <c>target_text</c> was not found in the document → HTTP 422.</summary>
    TargetNotFound,
}

/// <summary>
/// A structured, mappable failure from <see cref="DocxAnnotationWriter.Annotate"/>. Distinct from a
/// bare exception so the endpoint can turn it into the right ProblemDetails status
/// (<see cref="DocxAnnotationErrorKind.MalformedDocument"/> → 400,
/// <see cref="DocxAnnotationErrorKind.TargetNotFound"/> → 422) instead of an opaque 500.
/// </summary>
public sealed class DocxAnnotationException : Exception
{
    public DocxAnnotationException(DocxAnnotationErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    /// <summary>The failure category, used by the endpoint to select the ProblemDetails status.</summary>
    public DocxAnnotationErrorKind Kind { get; }
}
