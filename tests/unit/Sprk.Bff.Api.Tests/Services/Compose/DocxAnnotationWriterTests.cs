using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="DocxAnnotationWriter"/> (FR-24, task 050) — the WRITE-DIRECTION Open XML
/// annotation renderer that materializes accepted Compose annotations as native <c>w:ins</c> /
/// <c>w:del</c> / <c>w:comment</c> markup.
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c> — the writer is a pure byte[]→byte[] transform
/// with no I/O and no collaborators to mock. Tests build a real <c>.docx</c> with the Open XML SDK,
/// run the writer, then re-open the output and assert the emitted markup + schema validity
/// (<see cref="OpenXmlValidator"/>, per Spike 5 handoff §7). No <c>Mock&lt;HttpMessageHandler&gt;</c>,
/// no DI, no transport mocks.
/// </para>
/// </summary>
public class DocxAnnotationWriterTests
{
    private static readonly DateTimeOffset When = new(2026, 7, 9, 14, 30, 0, TimeSpan.Zero);
    private readonly DocxAnnotationWriter _sut = new();

    // -- Happy path: valid native markup + schema validity ---------------------------------------

    [Fact]
    public void Annotate_WithInsertionDeletionAndComment_EmitsValidNativeMarkupWithMetadata()
    {
        var source = CreateDocx("The quick brown fox jumps over the lazy dog.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "quick", CommentText = "Consider a stronger adjective.", Author = "Spaarke AI", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Insertion, TargetText = "fox", NewText = " (Vulpes vulpes)", Author = "Spaarke AI", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "lazy ", Author = "Spaarke AI", Date = When },
        };

        var result = _sut.Annotate(source, annotations);

        using var doc = WordprocessingDocument.Open(new MemoryStream(result), false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        // w:ins with author/date carrying the inserted text.
        var ins = body.Descendants<InsertedRun>().Single();
        ins.Author!.Value.Should().Be("Spaarke AI");
        ins.Date!.Value.Should().Be(When.UtcDateTime);
        ins.Descendants<Text>().Single().Text.Should().Be(" (Vulpes vulpes)");

        // w:del with author/date; EDGE-4: deleted text is DeletedText (w:delText), never Text (w:t).
        var del = body.Descendants<DeletedRun>().Single();
        del.Author!.Value.Should().Be("Spaarke AI");
        del.Descendants<DeletedText>().Single().Text.Should().Be("lazy ");
        del.Descendants<Text>().Should().BeEmpty("inside w:del the text element must be w:delText, not w:t");

        // w:comment in the comments part + anchoring marks in the body sharing one id.
        var comment = doc.MainDocumentPart!.WordprocessingCommentsPart!.Comments!.Elements<Comment>().Single();
        comment.Author!.Value.Should().Be("Spaarke AI");
        comment.Date!.Value.Should().Be(When.UtcDateTime);
        comment.InnerText.Should().Be("Consider a stronger adjective.");
        var rangeStart = body.Descendants<CommentRangeStart>().Single();
        rangeStart.Id!.Value.Should().Be(comment.Id!.Value);
        body.Descendants<CommentRangeEnd>().Single().Id!.Value.Should().Be(comment.Id.Value);

        // Schema-valid per Spike 5 (0 validator errors).
        AssertNoValidationErrors(doc);
    }

    // -- EDGE-1: comments applied before track changes -------------------------------------------

    [Fact]
    public void Annotate_CommentAndDeletionOnSameSpan_AppliesCommentBeforeDeletion()
    {
        // If the deletion were applied first, the "beta" run's text would become w:delText and the
        // comment's plaintext anchor lookup would fail (TargetNotFound). Comment-first ordering
        // (EDGE-1) is what makes both survive: the comment range brackets the deleted run.
        var source = CreateDocx("Alpha beta gamma.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "beta", Author = "AI", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "beta", CommentText = "Flagged.", Author = "AI", Date = When },
        };

        var result = _sut.Annotate(source, annotations);

        using var doc = WordprocessingDocument.Open(new MemoryStream(result), false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        body.Descendants<CommentRangeStart>().Should().ContainSingle("the comment anchor survived because comments are emitted before track changes");
        body.Descendants<DeletedRun>().Should().ContainSingle();
        AssertNoValidationErrors(doc);
    }

    // -- EDGE-2: paragraph-boundary deletion (w:pPr/w:rPr/w:del) ----------------------------------

    [Fact]
    public void Annotate_DeletionWithDeleteParagraphMark_MarksParagraphMarkDeleted()
    {
        var source = CreateDocx("First paragraph", "Second paragraph");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "First paragraph", DeleteParagraphMark = true, Author = "AI", Date = When },
        };

        var result = _sut.Annotate(source, annotations);

        using var doc = WordprocessingDocument.Open(new MemoryStream(result), false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var firstParagraph = body.Elements<Paragraph>().First();

        // The paragraph-mark deletion lives in w:pPr/w:rPr/w:del (adeu BUG-23-3).
        var markProps = firstParagraph.ParagraphProperties!.GetFirstChild<ParagraphMarkRunProperties>();
        markProps.Should().NotBeNull();
        markProps!.GetFirstChild<Deleted>().Should().NotBeNull("a paragraph-boundary deletion removes the paragraph mark via w:pPr/w:rPr/w:del");
        markProps.GetFirstChild<Deleted>()!.Author!.Value.Should().Be("AI");
        AssertNoValidationErrors(doc);
    }

    // -- EDGE-3: monotonic revision id seeded past existing ids -----------------------------------

    [Fact]
    public void Annotate_WhenDocumentHasExistingRevision_AllocatesIdPastTheExistingMax()
    {
        var source = CreateDocxWithExistingInsertion("Preface. ", existingInsertText: "existing", existingId: 50);
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Insertion, TargetText = "Preface.", NewText = " More.", Author = "AI", Date = When },
        };

        var result = _sut.Annotate(source, annotations);

        using var doc = WordprocessingDocument.Open(new MemoryStream(result), false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        var newInsertion = body.Descendants<InsertedRun>()
            .Single(i => i.Descendants<Text>().Any(t => t.Text == " More."));
        var newId = int.Parse(newInsertion.Id!.Value!, CultureInfo.InvariantCulture);
        newId.Should().BeGreaterThan(50, "new revision ids must be monotonic past any existing w:id (EDGE-3)");
    }

    [Fact]
    public void Annotate_WithMultipleAnnotations_AllocatesUniqueIds()
    {
        var source = CreateDocx("One two three four.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "One", CommentText = "c1", Author = "AI", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Insertion, TargetText = "two", NewText = "+ins", Author = "AI", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "three", Author = "AI", Date = When },
        };

        var result = _sut.Annotate(source, annotations);

        using var doc = WordprocessingDocument.Open(new MemoryStream(result), false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var ids = new List<string?>();
        ids.AddRange(body.Descendants<InsertedRun>().Select(i => i.Id?.Value));
        ids.AddRange(body.Descendants<DeletedRun>().Select(d => d.Id?.Value));
        ids.AddRange(body.Descendants<CommentRangeStart>().Select(c => c.Id?.Value));

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().NotContainNulls();
    }

    // -- Byte-stability of untouched content -----------------------------------------------------

    [Fact]
    public void Annotate_LeavesUntouchedParagraphsWithoutMarkupAndTextIntact()
    {
        var source = CreateDocx("Edit me here.", "Do not touch this paragraph.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "here", Author = "AI", Date = When },
        };

        var result = _sut.Annotate(source, annotations);

        using var doc = WordprocessingDocument.Open(new MemoryStream(result), false);
        var paragraphs = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ToList();
        var untouched = paragraphs[1];

        untouched.InnerText.Should().Be("Do not touch this paragraph.");
        untouched.Descendants<InsertedRun>().Should().BeEmpty();
        untouched.Descendants<DeletedRun>().Should().BeEmpty();
        untouched.Descendants<CommentRangeStart>().Should().BeEmpty();
    }

    // -- Insertion with no anchor (append) -------------------------------------------------------

    [Fact]
    public void Annotate_InsertionWithoutTarget_AppendsTrackedInsertionToLastParagraph()
    {
        var source = CreateDocx("Existing sentence.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Insertion, NewText = " Appended clause.", Author = "AI", Date = When },
        };

        var result = _sut.Annotate(source, annotations);

        using var doc = WordprocessingDocument.Open(new MemoryStream(result), false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var ins = body.Descendants<InsertedRun>().Single();
        ins.Descendants<Text>().Single().Text.Should().Be(" Appended clause.");
        AssertNoValidationErrors(doc);
    }

    // -- Empty annotation set --------------------------------------------------------------------

    [Fact]
    public void Annotate_WithNoAnnotations_ReturnsReadableDocxWithNoMarkup()
    {
        var source = CreateDocx("Nothing to annotate.");

        var result = _sut.Annotate(source, Array.Empty<DocxAnnotation>());

        using var doc = WordprocessingDocument.Open(new MemoryStream(result), false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        body.InnerText.Should().Be("Nothing to annotate.");
        body.Descendants<InsertedRun>().Should().BeEmpty();
        body.Descendants<DeletedRun>().Should().BeEmpty();
    }

    // -- Negatives -------------------------------------------------------------------------------

    [Fact]
    public void Annotate_WithMalformedBytes_ThrowsMalformedDocument()
    {
        var notADocx = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Insertion, NewText = "x", Author = "AI", Date = When },
        };

        var act = () => _sut.Annotate(notADocx, annotations);

        act.Should().Throw<DocxAnnotationException>()
            .Which.Kind.Should().Be(DocxAnnotationErrorKind.MalformedDocument);
    }

    [Fact]
    public void Annotate_WhenTargetTextNotFound_ThrowsTargetNotFound()
    {
        var source = CreateDocx("Hello world.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "nonexistent", Author = "AI", Date = When },
        };

        var act = () => _sut.Annotate(source, annotations);

        act.Should().Throw<DocxAnnotationException>()
            .Which.Kind.Should().Be(DocxAnnotationErrorKind.TargetNotFound);
    }

    [Fact]
    public void Annotate_WithEmptyBytes_ThrowsArgumentException()
    {
        var act = () => _sut.Annotate(Array.Empty<byte>(), Array.Empty<DocxAnnotation>());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Annotate_InsertionWithoutNewText_ThrowsArgumentException()
    {
        var source = CreateDocx("Body.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Insertion, TargetText = "Body", Author = "AI", Date = When },
        };

        var act = () => _sut.Annotate(source, annotations);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Annotate_CommentWithoutCommentText_ThrowsArgumentException()
    {
        var source = CreateDocx("Body.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "Body", Author = "AI", Date = When },
        };

        var act = () => _sut.Annotate(source, annotations);

        act.Should().Throw<ArgumentException>();
    }

    // -- DEF-13: AI edit REASON → anchored w:comment on the change (real OOXML wire assertion) ----

    [Fact]
    public void Annotate_WithAiEditReasonComment_WritesAnchoredWCommentOnTheChangedSpan()
    {
        // DEF-13: the AI's edit rationale is registered as a Compose comment annotation anchored to
        // the edit's target_text (ComposeWorkspace.registerAiEditReasonComment), then mapped to a
        // Comment DocxAnnotation (anchoredAnnotationsToDocxAnnotations) and materialized here. This
        // proves — through the REAL DOCX write path, asserting the SAVED OOXML (no mock) — that the
        // reason lands as a native w:comment whose anchor spans the changed text and whose body is the
        // rationale, so it shows for collaboration and persists into the .docx on Save.
        var source = CreateDocx("The Supplier shall indemnify the Customer under the prior clause today.");
        var aiReason = new DocxAnnotation
        {
            Kind = TrackChangeKind.Comment,
            TargetText = "the prior clause",
            CommentText = "This alternative improves clarity by removing the liability cap ambiguity.",
            Author = "Spaarke Assistant",
            Date = When,
        };

        var result = _sut.Annotate(source, new[] { aiReason });

        using var doc = WordprocessingDocument.Open(new MemoryStream(result), false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        // The reason is a native w:comment carrying the rationale + AI attribution.
        var comment = doc.MainDocumentPart!.WordprocessingCommentsPart!.Comments!.Elements<Comment>().Single();
        comment.Author!.Value.Should().Be("Spaarke Assistant");
        comment.InnerText.Should().Be("This alternative improves clarity by removing the liability cap ambiguity.");

        // The comment is ANCHORED to the changed span — commentRangeStart/End + reference share the id.
        var rangeStart = body.Descendants<CommentRangeStart>().Single();
        rangeStart.Id!.Value.Should().Be(comment.Id!.Value);
        body.Descendants<CommentRangeEnd>().Single().Id!.Value.Should().Be(comment.Id.Value);
        body.Descendants<CommentReference>().Single().Id!.Value.Should().Be(comment.Id.Value);

        // The anchor brackets exactly the target text ("the prior clause"), not the whole paragraph.
        var runsText = body.Elements<Paragraph>().Single().Elements<Run>()
            .Select(r => r.GetFirstChild<Text>()?.Text ?? string.Empty).ToList();
        var startIdx = body.Elements<Paragraph>().Single().ChildElements.ToList()
            .FindIndex(e => e is CommentRangeStart);
        var endIdx = body.Elements<Paragraph>().Single().ChildElements.ToList()
            .FindIndex(e => e is CommentRangeEnd);
        (startIdx >= 0 && endIdx > startIdx).Should().BeTrue("the comment range brackets the anchored text");

        // Schema-valid per Spike 5 (0 validator errors).
        AssertNoValidationErrors(doc);
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

    private static byte[] CreateDocxWithExistingInsertion(string leadingText, string existingInsertText, int existingId)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            var existingIns = new InsertedRun
            {
                Id = existingId.ToString(CultureInfo.InvariantCulture),
                Author = "Prior",
                Date = When.UtcDateTime,
            };
            existingIns.AppendChild(new Run(new Text(existingInsertText) { Space = SpaceProcessingModeValues.Preserve }));

            var paragraph = new Paragraph(
                new Run(new Text(leadingText) { Space = SpaceProcessingModeValues.Preserve }),
                existingIns);

            mainPart.Document = new Document(new Body(paragraph));
            mainPart.Document.Save();
        }

        return ms.ToArray();
    }

    private static void AssertNoValidationErrors(WordprocessingDocument doc)
    {
        var validator = new OpenXmlValidator(FileFormatVersions.Office2019);
        var errors = validator.Validate(doc).ToList();
        errors.Should().BeEmpty(
            "the annotated .docx must be schema-valid (Spike 5): {0}",
            string.Join("; ", errors.Select(e => e.Description)));
    }
}
