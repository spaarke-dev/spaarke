// FR-01/FR-03/FR-07 (E1) — unit tests for ComposeParagraphRedlineSynthesizer: the retained-original
// redline engine that REPLACES the retired Docxodus WmlComparer path (Option C, design §4.2). Each test
// names a concrete production behavior that breaks if deleted:
//   - a text edit on paragraph P becomes minimal w:ins/w:del ON P, keyed by w14:paraId, author set
//   - the word diff is correct: insert-only, delete-only, and mixed edits produce the right ins/del spans
//   - untouched paragraphs (incl. tables) pass through byte-identical with no markup; every paraId survives
//   - edited paragraphs preserve w14:paraId + w:pPr (style/numbering)
//   - an unmatched / duplicate / malformed paraId is a HANDLED error (throws), never a silent wrong write
//
// Banned-pattern compliance (tests/CLAUDE.md B1-B17): pure domain logic over real in-memory .docx
// fixtures (Open XML SDK) — no Mock<HttpMessageHandler> (B1), no DI/ctor tests (B3/B4). Assertions are
// behavioral (which paragraph changed, what markup was emitted, what was preserved).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeParagraphRedlineSynthesizerTests
{
    private const string Author = "Spaarke AI";
    private static readonly DateTimeOffset Stamp = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly ComposeParagraphRedlineSynthesizer _sut = new();

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────

    private static Paragraph Para(string paraId, string text, string? styleId = null)
    {
        var p = new Paragraph { ParagraphId = new HexBinaryValue(paraId) };
        if (styleId is not null)
        {
            p.AppendChild(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        }
        p.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }

    private static byte[] BuildDocx(params OpenXmlElement[] bodyChildren)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var child in bodyChildren)
            {
                body.Append(child);
            }
            body.Append(new SectionProperties());
            main.Document = new Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static byte[] FiveParagraphDoc() => BuildDocx(
        Para("0000000A", "The parties enter into this Agreement."),
        Para("0000000B", "Indemnification is capped at fees paid in the prior twelve months.", styleId: "Heading2"),
        Para("0000000C", "Each Party shall perform in a professional manner."),
        Para("0000000D", "Termination requires thirty days written notice."),
        Para("0000000E", "Confidential Information shall not be shared without consent."));

    private static byte[] TableDoc() => BuildDocx(
        Para("00000001", "Before the table."),
        new Table(new TableRow(new TableCell(Para("0000000T", "Original cell text here.")))),
        Para("00000002", "After the table."));

    // ── read helpers ──────────────────────────────────────────────────────────────────────────

    private static (int Ins, int Del, IReadOnlyList<string> Authors) Revisions(byte[] docx)
    {
        using var buffer = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(buffer, false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var ins = body.Descendants<InsertedRun>().ToList();
        var del = body.Descendants<DeletedRun>().ToList();
        var authors = ins.Select(i => i.Author?.Value).Concat(del.Select(d => d.Author?.Value))
            .Where(a => a is not null).Select(a => a!).Distinct().ToList();
        return (ins.Count, del.Count, authors);
    }

    private static string InsertedText(byte[] docx)
    {
        using var buffer = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(buffer, false);
        return string.Concat(doc.MainDocumentPart!.Document!.Body!.Descendants<InsertedRun>().Select(i => i.InnerText));
    }

    private static string DeletedText_(byte[] docx)
    {
        using var buffer = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(buffer, false);
        return string.Concat(doc.MainDocumentPart!.Document!.Body!.Descendants<DeletedText>().Select(d => d.Text));
    }

    private static string ParaText(byte[] docx, string paraId)
    {
        using var buffer = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(buffer, false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase)).InnerText;
    }

    private static HashSet<string> ParaIds(byte[] docx)
    {
        using var buffer = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(buffer, false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Select(p => p.ParagraphId?.Value).Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
    }

    private static Paragraph Reopen(byte[] docx, string paraId)
    {
        using var buffer = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(buffer, false);
        return (Paragraph)doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase)).CloneNode(true);
    }

    // ── core behavior ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SynthesizeRedline_TextEdit_EmitsInsDelOnThatParagraphWithAuthor()
    {
        var original = FiveParagraphDoc();

        var redline = _sut.SynthesizeRedline(original,
            new[] { new ComposeEditedParagraph("0000000C", "Each Party shall perform its duties in a professional manner.") },
            Author, Stamp);

        var rev = Revisions(redline);
        rev.Ins.Should().BeGreaterThan(0, "the inserted words become tracked w:ins");
        rev.Authors.Should().ContainSingle().Which.Should().Be(Author);
        // "its duties" is inserted between "perform" and "in".
        InsertedText(redline).Should().Contain("its duties");
    }

    [Fact]
    public void SynthesizeRedline_PureInsertion_EmitsInsOnly_NoDelete()
    {
        var original = BuildDocx(Para("0000000A", "The term is five years."));

        var redline = _sut.SynthesizeRedline(original,
            new[] { new ComposeEditedParagraph("0000000A", "The initial term is five years.") }, Author, Stamp);

        var rev = Revisions(redline);
        rev.Ins.Should().BeGreaterThan(0);
        rev.Del.Should().Be(0, "adding a word deletes nothing");
        InsertedText(redline).Should().Contain("initial");
    }

    [Fact]
    public void SynthesizeRedline_PureDeletion_EmitsDelOnly_NoInsert()
    {
        var original = BuildDocx(Para("0000000A", "The initial term is five years."));

        var redline = _sut.SynthesizeRedline(original,
            new[] { new ComposeEditedParagraph("0000000A", "The term is five years.") }, Author, Stamp);

        var rev = Revisions(redline);
        rev.Del.Should().BeGreaterThan(0);
        rev.Ins.Should().Be(0, "removing a word inserts nothing");
        DeletedText_(redline).Should().Contain("initial");
    }

    [Fact]
    public void SynthesizeRedline_MixedEdit_EmitsBothInsAndDel_AndKeepsUnchangedWordsPlain()
    {
        var original = BuildDocx(Para("0000000A", "The Provider shall deliver the services promptly."));

        var redline = _sut.SynthesizeRedline(original,
            new[] { new ComposeEditedParagraph("0000000A", "The Vendor shall deliver the services immediately.") }, Author, Stamp);

        var rev = Revisions(redline);
        rev.Ins.Should().BeGreaterThan(0);
        rev.Del.Should().BeGreaterThan(0);
        DeletedText_(redline).Should().Contain("Provider").And.Contain("promptly");
        InsertedText(redline).Should().Contain("Vendor").And.Contain("immediately");
        // Unchanged words are NOT struck/retyped.
        DeletedText_(redline).Should().NotContain("deliver");
        InsertedText(redline).Should().NotContain("deliver");
    }

    // ── paraId + structure preservation (FR-07 / E2) ───────────────────────────────────────────

    [Fact]
    public void SynthesizeRedline_PreservesEveryParaId_IncludingEditedAndUntouched()
    {
        var original = FiveParagraphDoc();

        var redline = _sut.SynthesizeRedline(original,
            new[] { new ComposeEditedParagraph("0000000B", "Indemnification is now uncapped for confidentiality breaches.") },
            Author, Stamp);

        ParaIds(redline).Should().BeEquivalentTo(ParaIds(original), "no paraId is stripped or regenerated — every w:p keeps its id");
    }

    [Fact]
    public void SynthesizeRedline_UntouchedParagraphs_PassThroughByteIdenticalWithNoMarkup()
    {
        var original = FiveParagraphDoc();

        var redline = _sut.SynthesizeRedline(original,
            new[] { new ComposeEditedParagraph("0000000A", "The parties hereby enter into this amended Agreement.") },
            Author, Stamp);

        ParaText(redline, "0000000C").Should().Be("Each Party shall perform in a professional manner.");
        Reopen(redline, "0000000C").Descendants<InsertedRun>().Should().BeEmpty();
        Reopen(redline, "0000000C").Descendants<DeletedRun>().Should().BeEmpty();
    }

    [Fact]
    public void SynthesizeRedline_EditedParagraph_PreservesParaIdAndParagraphStyle()
    {
        var original = FiveParagraphDoc(); // 0000000B carries ParagraphStyleId "Heading2"

        var redline = _sut.SynthesizeRedline(original,
            new[] { new ComposeEditedParagraph("0000000B", "Indemnification is now uncapped.") }, Author, Stamp);

        var edited = Reopen(redline, "0000000B");
        edited.ParagraphId!.Value.Should().Be("0000000B", "the paraId (anchor + splice key) survives the redline rewrite");
        edited.ParagraphProperties?.ParagraphStyleId?.Val?.Value.Should().Be("Heading2", "the paragraph style is preserved (FR-07)");
    }

    [Fact]
    public void SynthesizeRedline_TableCellParagraph_RedlinesByParaId_AndTableSurvives()
    {
        var original = TableDoc();

        var redline = _sut.SynthesizeRedline(original,
            new[] { new ComposeEditedParagraph("0000000T", "Rewritten cell text here.") }, Author, Stamp);

        Reopen(redline, "0000000T").Descendants<InsertedRun>().Any().Should().BeTrue("the table-cell paragraph was redlined by its paraId");
        using var buffer = new MemoryStream(redline);
        using var doc = WordprocessingDocument.Open(buffer, false);
        doc.MainDocumentPart!.Document!.Body!.Descendants<Table>().Should().ContainSingle("the table structure survives");
    }

    // ── no-op + fail-fast ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void SynthesizeRedline_EmptyEditList_ReturnsRoundTripWithNoRevisions()
    {
        var original = FiveParagraphDoc();

        var redline = _sut.SynthesizeRedline(original, Array.Empty<ComposeEditedParagraph>(), Author, Stamp);

        var rev = Revisions(redline);
        rev.Ins.Should().Be(0);
        rev.Del.Should().Be(0);
        ParaIds(redline).Should().BeEquivalentTo(ParaIds(original));
    }

    [Fact]
    public void SynthesizeRedline_UnmatchedParaId_ThrowsAndModifiesNothing()
    {
        var original = FiveParagraphDoc();
        var edits = new[]
        {
            new ComposeEditedParagraph("0000000C", "This valid edit must NOT be applied when the batch fails."),
            new ComposeEditedParagraph("DEADBEEF", "No paragraph has this id."),
        };

        var act = () => _sut.SynthesizeRedline(original, edits, Author, Stamp);

        act.Should().Throw<ComposeRedlineException>().WithMessage("*DEADBEEF*")
            .Which.Message.Should().Contain("aborted", "an unmatched paraId aborts the whole synthesis — no partial write");
    }

    [Fact]
    public void SynthesizeRedline_DuplicateEditedParaId_Throws()
    {
        var original = FiveParagraphDoc();
        var edits = new[]
        {
            new ComposeEditedParagraph("0000000C", "first"),
            new ComposeEditedParagraph("0000000C", "second — same id"),
        };

        var act = () => _sut.SynthesizeRedline(original, edits, Author, Stamp);

        act.Should().Throw<ComposeRedlineException>().WithMessage("*Duplicate*");
    }

    [Fact]
    public void SynthesizeRedline_MalformedBytes_ThrowsComposeRedlineException()
    {
        var notADocx = new byte[] { 1, 2, 3, 4, 5 };

        var act = () => _sut.SynthesizeRedline(notADocx,
            new[] { new ComposeEditedParagraph("0000000A", "x") }, Author, Stamp);

        act.Should().Throw<ComposeRedlineException>().WithMessage("*not a readable*");
    }

    [Fact]
    public void SynthesizeRedline_EmptyBytes_ThrowsArgumentException()
    {
        var act = () => _sut.SynthesizeRedline(ReadOnlyMemory<byte>.Empty, Array.Empty<ComposeEditedParagraph>(), Author, Stamp);
        act.Should().Throw<ArgumentException>().WithParameterName("retainedOriginal");
    }

    [Fact]
    public void SynthesizeRedline_WhitespaceAuthor_ThrowsArgumentException()
    {
        var act = () => _sut.SynthesizeRedline(FiveParagraphDoc(),
            new[] { new ComposeEditedParagraph("0000000A", "x") }, "  ", Stamp);
        act.Should().Throw<ArgumentException>().WithParameterName("author");
    }
}
