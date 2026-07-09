using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="DocxAnnotationReader"/> (FR-25, task 051) — the READ-DIRECTION Open
/// XML annotation parser that recovers native <c>w:comment</c> / <c>w:ins</c> / <c>w:del</c>
/// markup as a structured payload.
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c> — the reader is a pure byte[]-in /
/// record-out transform with no I/O and no collaborators to mock. Tests build a real
/// <c>.docx</c> with the Open XML SDK (reusing <see cref="DocxAnnotationWriter"/> where that
/// mirrors real Word output, and hand-built packages for reader-specific shapes like
/// multi-paragraph comment ranges), run the reader, then assert the recovered payload. No
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI, no transport mocks — same shape as the sibling
/// <see cref="DocxAnnotationWriterTests"/>.
/// </para>
/// </summary>
public class DocxAnnotationReaderTests
{
    private static readonly DateTime When = new(2026, 7, 9, 15, 42, 0, DateTimeKind.Utc);
    private readonly DocxAnnotationReader _sut = new();
    private readonly DocxAnnotationWriter _writer = new();

    // -- Happy path: comment + insertion + deletion, real Word-shaped markup ---------------------

    [Fact]
    public void Read_WithWordAuthoredCommentAndTrackChanges_RecoversCorrectAuthorAndDate()
    {
        // Build a Word-shaped edited document the same way DocxAnnotationWriter produces one —
        // a real human author ("Jordan Ellis"), not "Spaarke AI" (Spike 6's reverse-path point:
        // recovering a USER's edits, not re-reading the AI's own writes).
        var source = CreateDocx("The quick brown fox jumps over the lazy dog.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "quick", CommentText = "Consider a stronger adjective.", Author = "Jordan Ellis", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Insertion, TargetText = "fox", NewText = " (Vulpes vulpes)", Author = "Jordan Ellis", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "lazy ", Author = "Jordan Ellis", Date = When },
        };
        var edited = _writer.Annotate(source, annotations);

        var result = _sut.Read(edited);

        // Comment: author/date/id + comment body + anchored range text.
        var comment = result.Comments.Should().ContainSingle().Subject;
        comment.Author.Should().Be("Jordan Ellis");
        comment.Date.UtcDateTime.Should().Be(When);
        comment.CommentText.Should().Be("Consider a stronger adjective.");
        comment.AnchorText.Should().Be("quick");
        comment.ParagraphHint.Should().Be(0);

        // Insertion: author/date/text.
        var insertion = result.Revisions.Should().ContainSingle(r => r.Kind == RecoveredAnnotationKind.Insertion).Subject;
        insertion.Author.Should().Be("Jordan Ellis");
        insertion.Date.UtcDateTime.Should().Be(When);
        insertion.Text.Should().Be(" (Vulpes vulpes)");
        insertion.ParagraphHint.Should().Be(0);

        // Deletion: EDGE-R1 — recovered via DeletedText, author/date/text still correct.
        var deletion = result.Revisions.Should().ContainSingle(r => r.Kind == RecoveredAnnotationKind.Deletion).Subject;
        deletion.Author.Should().Be("Jordan Ellis");
        deletion.Date.UtcDateTime.Should().Be(When);
        deletion.Text.Should().Be("lazy ");
    }

    // -- EDGE-R2: w:date normalized to UTC -------------------------------------------------------

    [Fact]
    public void Read_RecoveredDates_AreNormalizedToUtcKind()
    {
        var source = CreateDocx("Body text here.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Insertion, TargetText = "Body", NewText = " (edit)", Author = "AI", Date = When },
        };
        var edited = _writer.Annotate(source, annotations);

        var result = _sut.Read(edited);

        result.Revisions.Single().Date.Offset.Should().Be(TimeSpan.Zero, "w:date has no timezone info and must be normalized to UTC (EDGE-R2)");
    }

    // -- Comment anchored-range extraction (separate from comment body) --------------------------

    [Fact]
    public void Read_CommentAnchorText_IsDistinctFromCommentBody()
    {
        var source = CreateDocx("Alpha beta gamma delta.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "beta gamma", CommentText = "This is the reviewer's note, not document text.", Author = "AI", Date = When },
        };
        var edited = _writer.Annotate(source, annotations);

        var result = _sut.Read(edited);

        var comment = result.Comments.Single();
        comment.AnchorText.Should().Be("beta gamma", "anchor text is the document range the comment is about");
        comment.CommentText.Should().Be("This is the reviewer's note, not document text.");
    }

    // -- EDGE-R4: multi-paragraph comment range (Spike 6 flagged gap; hand-built package) ---------

    [Fact]
    public void Read_CommentSpanningMultipleParagraphs_RecoversFullAnchorText()
    {
        var source = CreateMultiParagraphCommentRangeDocx(
            firstParagraphText: "First paragraph tail.",
            secondParagraphText: "Second paragraph head.",
            commentId: "1",
            commentAuthor: "Jordan Ellis",
            commentDate: When,
            commentBody: "Spans two paragraphs.");

        var result = _sut.Read(source);

        var comment = result.Comments.Should().ContainSingle().Subject;
        comment.Author.Should().Be("Jordan Ellis");
        comment.CommentText.Should().Be("Spans two paragraphs.");
        comment.AnchorText.Should().Be(
            "First paragraph tail.Second paragraph head.",
            "the anchor walk must flatten across paragraph boundaries, not stop at the first paragraph (Spike 6 gotcha)");
    }

    // -- Overlapping annotation types in one paragraph (Spike 6 flagged as untested composition) --

    [Fact]
    public void Read_CommentAndDeletionOnSameSpan_RecoversBothIndependently()
    {
        var source = CreateDocx("Alpha beta gamma.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "beta", Author = "AI", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "beta", CommentText = "Flagged.", Author = "AI", Date = When },
        };
        var edited = _writer.Annotate(source, annotations);

        var result = _sut.Read(edited);

        result.Comments.Should().ContainSingle(c => c.CommentText == "Flagged.");
        result.Revisions.Should().ContainSingle(r => r.Kind == RecoveredAnnotationKind.Deletion && r.Text == "beta");
    }

    // -- Multiple annotations preserve document order --------------------------------------------

    [Fact]
    public void Read_WithMultipleRevisions_PreservesDocumentOrder()
    {
        var source = CreateDocx("One two three four.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Insertion, TargetText = "two", NewText = "+ins", Author = "AI", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "three", Author = "AI", Date = When },
        };
        var edited = _writer.Annotate(source, annotations);

        var result = _sut.Read(edited);

        result.Revisions.Should().HaveCount(2);
        result.Revisions[0].Kind.Should().Be(RecoveredAnnotationKind.Insertion);
        result.Revisions[1].Kind.Should().Be(RecoveredAnnotationKind.Deletion);
    }

    // -- Negative: empty (no-annotation) document → empty payload, not an error ------------------

    [Fact]
    public void Read_WithNoAnnotations_ReturnsEmptyPayload()
    {
        var source = CreateDocx("Nothing to see here.");

        var result = _sut.Read(source);

        result.Comments.Should().BeEmpty();
        result.Revisions.Should().BeEmpty();
    }

    // -- Negative: malformed / non-DOCX bytes ----------------------------------------------------

    [Fact]
    public void Read_WithMalformedBytes_ThrowsMalformedDocument()
    {
        var notADocx = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        var act = () => _sut.Read(notADocx);

        act.Should().Throw<DocxAnnotationException>()
            .Which.Kind.Should().Be(DocxAnnotationErrorKind.MalformedDocument);
    }

    [Fact]
    public void Read_WithEmptyBytes_ThrowsArgumentException()
    {
        var act = () => _sut.Read(Array.Empty<byte>());

        act.Should().Throw<ArgumentException>();
    }

    // -- Helpers ---------------------------------------------------------------------------------

    private static byte[] CreateDocx(params string[] paragraphs)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var text in paragraphs)
            {
                body.AppendChild(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
            }

            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Hand-builds a document whose single comment's <c>CommentRangeStart</c>/<c>CommentRangeEnd</c>
    /// markers straddle TWO paragraphs — a shape <see cref="DocxAnnotationWriter"/> cannot produce
    /// (its <c>ApplyComment</c> anchors within one located paragraph), but one Word itself allows
    /// and Spike 6 explicitly flagged as unexercised. Exercises EDGE-R4 (the anchor-range walk must
    /// flatten across paragraph boundaries).
    /// </summary>
    private static byte[] CreateMultiParagraphCommentRangeDocx(
        string firstParagraphText, string secondParagraphText, string commentId,
        string commentAuthor, DateTime commentDate, string commentBody)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();

            var firstParagraph = new Paragraph(
                new CommentRangeStart { Id = commentId },
                new Run(new Text(firstParagraphText) { Space = SpaceProcessingModeValues.Preserve }));

            var secondParagraph = new Paragraph(
                new Run(new Text(secondParagraphText) { Space = SpaceProcessingModeValues.Preserve }),
                new CommentRangeEnd { Id = commentId },
                new Run(new CommentReference { Id = commentId }));

            var body = new Body(firstParagraph, secondParagraph);
            mainPart.Document = new Document(body);
            mainPart.Document.Save();

            var commentsPart = mainPart.AddNewPart<WordprocessingCommentsPart>();
            var comment = new Comment
            {
                Id = commentId,
                Author = commentAuthor,
                Date = commentDate,
            };
            comment.AppendChild(new Paragraph(new Run(new Text(commentBody) { Space = SpaceProcessingModeValues.Preserve })));
            commentsPart.Comments = new Comments(comment);
            commentsPart.Comments.Save();
        }

        return ms.ToArray();
    }
}
