using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="DocxTextExtractor"/> — spaarkeai-compose-r1 task 094
/// (Phase 8 SSE backend, per FR-S5).
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c> (unit-domain). The extractor is
/// a pure function of its input stream: it opens a DOCX package with DocumentFormat.OpenXml
/// (a real, in-process library — no I/O boundary is crossed at runtime) and walks the
/// document tree to produce a plain-text projection. Per <c>tests/CLAUDE.md</c> the
/// unit-domain bucket covers "pure domain logic — calculations, mappings, parsing,
/// serialization" and OpenXml parsing IS the parsing bucket.
/// </para>
///
/// <para>
/// <b>Fixture strategy</b>: DOCX fixtures are built in-memory using the OpenXml SDK
/// itself (see <see cref="Fixture"/> below). No binary test-fixture files are checked
/// in; every scenario is transparent from the test source.
/// </para>
///
/// <para>
/// <b>Mocking</b>: none. The SUT has no injectable dependencies — it takes a
/// <see cref="Stream"/> and returns a string. No <see cref="System.Net.Http.HttpMessageHandler"/>
/// mocks (B1 banned per ADR-038 §4). No DI-registration tests (B3 banned). No
/// constructor null-check tests (B4 banned). No mirror tests (B6 banned).
/// </para>
///
/// <para>
/// <b>Behavioral coverage</b>:
/// <list type="number">
///   <item><b>ExtractPlainTextAsync_WithEmptyDocx_ReturnsEmptyString</b> — DOCX with an
///   empty body returns <see cref="string.Empty"/>, NOT null; downstream consumers can
///   pass the result straight to <c>string.IsNullOrEmpty</c> checks.</item>
///   <item><b>ExtractPlainTextAsync_WithSimpleParagraphs_ConcatenatesWithParagraphBreaks</b>
///   — the happy path: 3 paragraphs → 3 lines joined by <c>\n</c> in document order.</item>
///   <item><b>ExtractPlainTextAsync_WithMultipleRunsPerParagraph_ConcatenatesInDocumentOrder</b>
///   — Word split-run semantics: a single paragraph can contain multiple runs
///   (e.g. formatting changes). All Text elements are concatenated in order within
///   the paragraph, then a paragraph break separates.</item>
///   <item><b>ExtractPlainTextAsync_WithHeadersAndFooters_SkipsThem</b> — headers +
///   footers live in separate document parts; iterating <c>Body.Descendants&lt;Paragraph&gt;()</c>
///   naturally excludes them. Codified here so a future refactor to broader iteration
///   would break the test loudly.</item>
///   <item><b>ExtractPlainTextAsync_WithCommentsPart_SkipsThem</b> — comments live in
///   <c>word/comments.xml</c> and are separate document parts. Same rationale.</item>
///   <item><b>ExtractPlainTextAsync_WhenTextExceedsMaxCharacters_TruncatesWithSuffix</b>
///   — truncation contract: buffer is capped at <c>maxCharacters</c>, followed by
///   <c>" [TRUNCATED — N characters more]"</c> where N is the total residue.</item>
///   <item><b>ExtractPlainTextAsync_WithMalformedDocxBytes_ThrowsInvalidDataException</b>
///   — invalid input surfaces a normalized <see cref="InvalidDataException"/> per the
///   interface contract (not the raw <see cref="System.IO.FileFormatException"/>).</item>
///   <item><b>ExtractPlainTextAsync_WithCancelledToken_ThrowsOperationCanceledException</b>
///   — cancellation is checked at the entry point and between paragraph reads.</item>
/// </list>
/// </para>
/// </summary>
public class DocxTextExtractorTests
{
    private readonly DocxTextExtractor _sut = new();

    [Fact]
    public async Task ExtractPlainTextAsync_WithEmptyDocx_ReturnsEmptyString()
    {
        // Arrange — DOCX with a body that contains a single empty paragraph.
        // (Word emits at least one paragraph even in an "empty" document.)
        await using var docx = Fixture.BuildDocx(paragraphs: new[] { string.Empty });

        // Act
        var result = await _sut.ExtractPlainTextAsync(docx);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractPlainTextAsync_WithSimpleParagraphs_ConcatenatesWithParagraphBreaks()
    {
        // Arrange
        await using var docx = Fixture.BuildDocx(paragraphs: new[]
        {
            "First paragraph.",
            "Second paragraph.",
            "Third paragraph."
        });

        // Act
        var result = await _sut.ExtractPlainTextAsync(docx);

        // Assert
        result.Should().Be("First paragraph.\nSecond paragraph.\nThird paragraph.");
    }

    [Fact]
    public async Task ExtractPlainTextAsync_WithMultipleRunsPerParagraph_ConcatenatesInDocumentOrder()
    {
        // Arrange — one paragraph with 3 runs (Word does this when formatting changes
        // mid-paragraph, e.g. bold in the middle). The extractor MUST concatenate
        // runs in document order so mid-sentence formatting doesn't split words.
        await using var docx = Fixture.BuildDocxWithSplitRuns(paragraphs: new[]
        {
            new[] { "Hello, ", "beautiful ", "world!" },
            new[] { "This is another paragraph." }
        });

        // Act
        var result = await _sut.ExtractPlainTextAsync(docx);

        // Assert
        result.Should().Be("Hello, beautiful world!\nThis is another paragraph.");
    }

    [Fact]
    public async Task ExtractPlainTextAsync_WithHeadersAndFooters_SkipsThem()
    {
        // Arrange — body has "Body text" and there are header + footer parts each with
        // separate prose. Only body text should appear in the output.
        await using var docx = Fixture.BuildDocxWithHeaderAndFooter(
            body: "Body text.",
            header: "HEADER NOISE.",
            footer: "FOOTER NOISE.");

        // Act
        var result = await _sut.ExtractPlainTextAsync(docx);

        // Assert
        result.Should().Be("Body text.");
        result.Should().NotContain("HEADER");
        result.Should().NotContain("FOOTER");
    }

    [Fact]
    public async Task ExtractPlainTextAsync_WithCommentsPart_SkipsThem()
    {
        // Arrange — body has one paragraph "Body prose."; a comments part is present
        // with the text "REVIEWER COMMENT". Only body prose should surface.
        await using var docx = Fixture.BuildDocxWithComment(
            body: "Body prose.",
            comment: "REVIEWER COMMENT");

        // Act
        var result = await _sut.ExtractPlainTextAsync(docx);

        // Assert
        result.Should().Be("Body prose.");
        result.Should().NotContain("REVIEWER");
    }

    [Fact]
    public async Task ExtractPlainTextAsync_WhenTextExceedsMaxCharacters_TruncatesWithSuffix()
    {
        // Arrange — 3 paragraphs of 50 characters each; total 150 characters plus
        // 2 newlines. Max = 80 → truncated at position 80, suffix conveys the residue.
        var p1 = new string('A', 50);
        var p2 = new string('B', 50);
        var p3 = new string('C', 50);
        await using var docx = Fixture.BuildDocx(paragraphs: new[] { p1, p2, p3 });

        // Act
        var result = await _sut.ExtractPlainTextAsync(docx, maxCharacters: 80);

        // Assert
        // Buffer up to position 80: p1 (50) + "\n" (1) + p2[0..29] (29) = 80 chars.
        result.Should().StartWith(p1 + "\n" + new string('B', 29));
        result.Should().Contain(" [TRUNCATED —");
        result.Should().EndWith(" characters more]");

        // Overshoot: 21 remaining B's + "\n" (1) + 50 C's = 72 characters more.
        result.Should().Contain(" [TRUNCATED — 72 characters more]");
    }

    [Fact]
    public async Task ExtractPlainTextAsync_WithMalformedDocxBytes_ThrowsInvalidDataException()
    {
        // Arrange — random bytes that aren't a valid ZIP / Open XML package.
        var garbage = Encoding.UTF8.GetBytes("this is definitely not a valid docx file, just random text");
        await using var stream = new MemoryStream(garbage);

        // Act
        var act = () => _sut.ExtractPlainTextAsync(stream);

        // Assert — normalized to InvalidDataException per the interface contract.
        await act.Should()
            .ThrowAsync<InvalidDataException>()
            .WithMessage("*not a valid Open XML*");
    }

    [Fact]
    public async Task ExtractPlainTextAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        await using var docx = Fixture.BuildDocx(paragraphs: new[] { "Any prose." });
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = () => _sut.ExtractPlainTextAsync(docx, cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExtractPlainTextAsync_WithMaxCharactersLessThanOne_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        await using var docx = Fixture.BuildDocx(paragraphs: new[] { "Any prose." });

        // Act
        var act = () => _sut.ExtractPlainTextAsync(docx, maxCharacters: 0);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // -----------------------------------------------------------------------
    // Fixture — builds DOCX packages in-memory via the OpenXml SDK.
    // Kept private and file-scoped so future tests can compose more scenarios
    // without touching the production surface.
    // -----------------------------------------------------------------------

    private static class Fixture
    {
        public static MemoryStream BuildDocx(IEnumerable<string> paragraphs)
        {
            var stream = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var mainPart = doc.AddMainDocumentPart();
                var body = new Body();
                foreach (var text in paragraphs)
                {
                    var paragraph = new Paragraph();
                    if (text.Length > 0)
                    {
                        var run = new Run(new Text(text));
                        paragraph.AppendChild(run);
                    }
                    body.AppendChild(paragraph);
                }
                mainPart.Document = new Document(body);
                mainPart.Document.Save();
            }
            stream.Position = 0;
            return stream;
        }

        public static MemoryStream BuildDocxWithSplitRuns(IEnumerable<string[]> paragraphs)
        {
            var stream = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var mainPart = doc.AddMainDocumentPart();
                var body = new Body();
                foreach (var runs in paragraphs)
                {
                    var paragraph = new Paragraph();
                    foreach (var runText in runs)
                    {
                        paragraph.AppendChild(new Run(new Text(runText)));
                    }
                    body.AppendChild(paragraph);
                }
                mainPart.Document = new Document(body);
                mainPart.Document.Save();
            }
            stream.Position = 0;
            return stream;
        }

        public static MemoryStream BuildDocxWithHeaderAndFooter(string body, string header, string footer)
        {
            var stream = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var mainPart = doc.AddMainDocumentPart();
                var docBody = new Body(new Paragraph(new Run(new Text(body))));
                mainPart.Document = new Document(docBody);

                var headerPart = mainPart.AddNewPart<HeaderPart>();
                headerPart.Header = new Header(new Paragraph(new Run(new Text(header))));

                var footerPart = mainPart.AddNewPart<FooterPart>();
                footerPart.Footer = new Footer(new Paragraph(new Run(new Text(footer))));

                mainPart.Document.Save();
            }
            stream.Position = 0;
            return stream;
        }

        public static MemoryStream BuildDocxWithComment(string body, string comment)
        {
            var stream = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var mainPart = doc.AddMainDocumentPart();
                var docBody = new Body(new Paragraph(new Run(new Text(body))));
                mainPart.Document = new Document(docBody);

                var commentsPart = mainPart.AddNewPart<WordprocessingCommentsPart>();
                commentsPart.Comments = new Comments(new Comment(new Paragraph(new Run(new Text(comment))))
                {
                    Id = "1",
                    Author = "Test",
                    Date = DateTime.UtcNow,
                });

                mainPart.Document.Save();
            }
            stream.Position = 0;
            return stream;
        }
    }
}
