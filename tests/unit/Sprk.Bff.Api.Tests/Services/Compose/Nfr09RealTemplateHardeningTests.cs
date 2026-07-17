// NFR-09 (task 003) — the real-firm-template hardening GATE for the E1 delta-save cutover (task 022).
// Re-runs the S1/S1b Docxodus WmlComparer fidelity harness on GENUINELY Word-authored legal templates
// (Common Paper Cloud Service Agreement + Mutual NDA — CC BY 4.0 public standards; see
// Fixtures/Compose/RealTemplates/README.md) rather than the synthetic spike fixtures, exercising the
// REAL production pipeline: ComposeParagraphSpliceService (task 020) -> ComposeRedlineComparerService
// (task 021).
//
// GATE VERDICT: **FAIL** — see notes/spikes/S1-nfr09-real-template-hardening-2026-07-17.md. On real
// templates the shipped Docxodus **6.4.0** WmlComparer (a) STRIPS w14:paraId from every paragraph
// (replacing it with a leftover internal pt14:Unid), and (b) DROPS an unchanged top-level table. S1
// validated paraId + structural preservation on **7.1.0** (net10) — which the net8 BFF cannot use.
// These two defects GATE task 022; the escalation (root CLAUDE.md §6.5) proposes Approach B (graft the
// comparer's w:ins/w:del back onto the retained original) or a Codeuctivity-fork evaluation.
//
// This suite is GREEN and HONEST: it proves what 6.4.0 DOES do correctly (runs without exception on
// real docs; emits minimal authored ins/del; format-change not del+ins; delete/split no-throw) AND
// PINS the two gate-blocking defects as Feathers-style characterization tests. If a future Docxodus
// version fixes either defect, the corresponding characterization test flips red — the signal to
// revisit the gate + unblock task 022 under Approach A.
//
// KEEP-class ADR-038 fidelity test: real .docx round-trips through real services; no transport mocks,
// no DI/ctor tests.

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

public sealed class Nfr09RealTemplateHardeningTests
{
    private const string Author = "Spaarke AI";
    private const string Csa = "commonpaper-cloud-service-agreement.docx";
    private const string Nda = "commonpaper-mutual-nda.docx";
    private const string Pt14Ns = "http://powertools.codeplex.com/2011";
    private static readonly DateTimeOffset RevisionStamp = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly ComposeParagraphSpliceService _splicer = new();
    private readonly ComposeRedlineComparerService _sut = new();

    // ── what 6.4.0 DOES do correctly on real templates (these PASS) ─────────────────────────────

    [Theory]
    [InlineData(Csa)]
    [InlineData(Nda)]
    public void RealTemplate_TextEditsViaSpliceAndComparer_RunWithoutException_AndEmitMinimalAuthoredRevisions(string fileName)
    {
        var original = LoadTemplate(fileName);

        // Premise: a genuinely Word-authored template stamps w14:paraId on its body paragraphs.
        var originalParaIds = BodyParaIds(original);
        originalParaIds.Should().NotBeEmpty("a real Word-authored template carries w14:paraId on body paragraphs");

        // Edit 3 real paragraphs (by paraId) through the real task-020 splice.
        var targets = EditableParagraphs(original, minLen: 25).Take(3).ToList();
        targets.Should().HaveCountGreaterThanOrEqualTo(3, "the real template has editable prose paragraphs to exercise");
        var edits = targets.Select(t => new ComposeEditedParagraph(t.ParaId, t.Text + " (revised)")).ToList();
        var edited = _splicer.SpliceEditedParagraphs(original, edits);

        // A throw here would fail the gate outright — the comparer must run on real docs.
        var redline = _sut.SynthesizeRedline(original, edited, Author, RevisionStamp);

        var outParas = ParagraphCount(redline);
        outParas.Should().BeGreaterThan(0, "the comparer produced a valid document");

        var rev = ReadRevisions(redline);
        (rev.InsCount + rev.DelCount).Should().BeGreaterThan(0, "the 3 text edits synthesize tracked revisions");
        // Minimal, not a whole-body rewrite: revisions are a tiny fraction of the ~hundreds of paragraphs.
        (rev.InsCount + rev.DelCount).Should().BeLessThan(outParas / 4 + 10, "the redline is minimal, not a full-document rewrite");
        rev.Authors.Should().OnlyContain(a => a == Author, "every emitted revision carries the supplied author attribution");
    }

    [Theory]
    [InlineData(Csa)]
    [InlineData(Nda)]
    public void RealTemplate_NonTableStructuralParts_ArePreserved(string fileName)
    {
        var original = LoadTemplate(fileName);
        var before = StructuralParts(original);

        var targets = EditableParagraphs(original, minLen: 25).Take(2).ToList();
        var edited = _splicer.SpliceEditedParagraphs(original,
            targets.Select(t => new ComposeEditedParagraph(t.ParaId, t.Text + " (revised)")).ToList());
        var redline = _sut.SynthesizeRedline(original, edited, Author, RevisionStamp);

        var after = StructuralParts(redline);
        after.HasStyles.Should().BeTrue("styles part preserved");
        if (before.HasNumbering) after.HasNumbering.Should().BeTrue("numbering part preserved (deep multi-level numbering survives)");
        if (before.HasFootnotes) after.HasFootnotes.Should().BeTrue("footnotes part preserved");
        if (before.HasHeaders) after.HasHeaders.Should().BeTrue("header parts preserved");
        if (before.HasFooters) after.HasFooters.Should().BeTrue("footer parts preserved");
        // NOTE: tables are NOT asserted here — see the table-drop characterization test below (gate blocker).
    }

    [Theory]
    [InlineData(Csa)]
    [InlineData(Nda)]
    public void RealTemplate_BoldingAWordInARealParagraph_EmitsFormatChangeNotDeleteReinsert(string fileName)
    {
        var original = LoadTemplate(fileName);
        var target = SimpleBodyParagraphs(original, minLen: 30).First();

        // Edited copy: SAME text, but the target paragraph's runs are now bold (format-only change).
        var edited = Mutate(original, body =>
        {
            var p = body.Descendants<Paragraph>().First(x => Eq(x.ParagraphId?.Value, target.ParaId));
            foreach (var run in p.Elements<Run>())
            {
                var rpr = run.GetFirstChild<RunProperties>() ?? run.PrependChild(new RunProperties());
                if (rpr.GetFirstChild<Bold>() is null)
                {
                    rpr.PrependChild(new Bold());
                }
            }
        });

        var redline = _sut.SynthesizeRedline(original, edited, Author, RevisionStamp);

        var rev = ReadRevisions(redline);
        (rev.RunFormatChanges + rev.ParagraphFormatChanges).Should()
            .BeGreaterThan(0, "an inline run-format edit on a real paragraph is a Format-Change (rPr/pPrChange), not del+ins (FR-05/D4)");

        var reformattedWord = target.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).First(w => w.Length > 3);
        DeletedTextValues(redline).Should().NotContain(t => t.Contains(reformattedWord, StringComparison.Ordinal),
            "a bold-only change must not delete+re-insert the run text (the D4 regression this guards)");
    }

    [Fact]
    public void RealTemplate_WholeParagraphDelete_DoesNotThrowAndEmitsDeletion()
    {
        var original = LoadTemplate(Csa);
        var target = EditableParagraphs(original, minLen: 40).First();

        var edited = Mutate(original, body =>
            body.Descendants<Paragraph>().First(x => Eq(x.ParagraphId?.Value, target.ParaId)).Remove());

        var redline = _sut.SynthesizeRedline(original, edited, Author, RevisionStamp);

        ReadRevisions(redline).DelCount.Should().BeGreaterThan(0, "the removed real paragraph surfaces as tracked deletion markup (S1b)");
    }

    [Fact]
    public void RealTemplate_ParagraphSplit_DoesNotThrowAndProducesValidDocument()
    {
        var original = LoadTemplate(Csa);
        var target = SimpleBodyParagraphs(original, minLen: 60)
            .First(p => p.Text.IndexOf(". ", 10, StringComparison.Ordinal) > 0);
        var freshId = FreshParaId(BodyParaIds(original));

        var edited = Mutate(original, body =>
        {
            var p = body.Elements<Paragraph>().First(x => Eq(x.ParagraphId?.Value, target.ParaId));
            var text = p.InnerText;
            var at = text.IndexOf(". ", 10, StringComparison.Ordinal) + 1;
            var pPr = p.GetFirstChild<ParagraphProperties>();
            var p1 = MakeParagraph(target.ParaId, text[..at], pPr);
            var p2 = MakeParagraph(freshId, text[at..].TrimStart(), pPr);
            p.InsertAfterSelf(p2);
            p.InsertAfterSelf(p1);
            p.Remove();
        });

        // A throw here fails the gate (S1b: WmlComparer is robust on a paragraph split).
        var redline = _sut.SynthesizeRedline(original, edited, Author, RevisionStamp);
        ParagraphCount(redline).Should().BeGreaterThan(0, "the split produces a valid comparison document");
    }

    // ── the two GATE-BLOCKING defects, pinned as characterization tests ─────────────────────────
    //
    // These assert the CURRENT (defective) 6.4.0 behavior. They are GREEN today because the defects
    // are real; if a future Docxodus version fixes either, the test flips RED — the signal to revisit
    // the NFR-09 gate and unblock task 022 under Approach A. See the hardening report for the verdict.

    [Fact]
    public void Comparer6_4_0_StripsW14ParaId_ReplacingWithInternalUnid_GATE_BLOCKER_Task022()
    {
        var original = LoadTemplate(Csa);
        BodyParaIds(original).Should().NotBeEmpty("the real template carries w14:paraId on input");

        var targets = EditableParagraphs(original, minLen: 25).Take(3).ToList();
        var edited = _splicer.SpliceEditedParagraphs(original,
            targets.Select(t => new ComposeEditedParagraph(t.ParaId, t.Text + " (revised)")).ToList());
        var redline = _sut.SynthesizeRedline(original, edited, Author, RevisionStamp);

        // DEFECT 1 (gate blocker): every w14:paraId is GONE from the comparer output, replaced by a
        // leftover internal pt14:Unid. This defeats paraId-primary re-anchoring across a save under
        // Approach A (S1 saw the OPPOSITE on 7.1.0). If this ever preserves w14:paraId, flip the gate.
        BodyParaIds(redline).Should().BeEmpty("REGRESSION-PIN: Docxodus 6.4.0 WmlComparer strips w14:paraId on all paragraphs");
        Pt14UnidCount(redline).Should().BeGreaterThan(0, "REGRESSION-PIN: 6.4.0 leaves its internal pt14:Unid correlation attribute in the output");
    }

    [Fact]
    public void Comparer6_4_0_DropsAnUnchangedTopLevelTable_GATE_BLOCKER_Task022()
    {
        var original = LoadTemplate(Csa); // 3 top-level + 3 nested tables; NONE are edited below.
        var targets = EditableParagraphs(original, minLen: 25).Take(3).ToList();
        var edited = _splicer.SpliceEditedParagraphs(original,
            targets.Select(t => new ComposeEditedParagraph(t.ParaId, t.Text + " (revised)")).ToList());

        // The splice preserves every table (task 020 is clean).
        TopLevelTableCount(edited).Should().Be(TopLevelTableCount(original), "the splice preserves all top-level tables");

        var redline = _sut.SynthesizeRedline(original, edited, Author, RevisionStamp);

        // DEFECT 2 (gate blocker): the comparer drops an UNCHANGED top-level table (NFR-07 fidelity
        // violation). If this ever stops dropping, flip the gate.
        TopLevelTableCount(redline).Should().BeLessThan(TopLevelTableCount(edited),
            "REGRESSION-PIN: Docxodus 6.4.0 WmlComparer drops an unchanged top-level table on the real CSA");
    }

    // ── fixture + OOXML helpers ──────────────────────────────────────────────────────────────────

    private static byte[] LoadTemplate(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Compose", "RealTemplates", fileName);
        File.Exists(path).Should().BeTrue($"the real-template fixture '{fileName}' must be copied to the test output (see csproj Content Include)");
        return File.ReadAllBytes(path);
    }

    private static bool Eq(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Uppercased set of every body paragraph's w14:paraId (recursive — incl. table cells).</summary>
    private static HashSet<string> BodyParaIds(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!
            .Descendants<Paragraph>()
            .Select(p => p.ParagraphId?.Value)
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }

    private static int ParagraphCount(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().Count();
    }

    /// <summary>Count of paragraphs carrying the PowerTools internal pt14:Unid attribute (leftover in 6.4.0 output).</summary>
    private static int Pt14UnidCount(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!
            .Descendants<Paragraph>()
            .Count(p => p.GetAttributes().Any(a => a.LocalName == "Unid" && a.NamespaceUri == Pt14Ns));
    }

    /// <summary>Top-level tables = tables with no Table ancestor (excludes nested tables).</summary>
    private static int TopLevelTableCount(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!
            .Descendants<Table>()
            .Count(t => !t.Ancestors<Table>().Any());
    }

    private sealed record Parts(bool HasStyles, bool HasNumbering, bool HasFootnotes, bool HasHeaders, bool HasFooters);

    private static Parts StructuralParts(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var main = doc.MainDocumentPart!;
        return new Parts(
            main.StyleDefinitionsPart is not null,
            main.NumberingDefinitionsPart is not null,
            main.FootnotesPart is not null,
            main.HeaderParts.Any(),
            main.FooterParts.Any());
    }

    /// <summary>Any body paragraph (recursive, incl. table cells) with a paraId + text — safe to text-edit via the splice.</summary>
    private static List<(string ParaId, string Text)> EditableParagraphs(byte[] docx, int minLen)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var result = new List<(string, string)>();
        foreach (var p in doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>())
        {
            var pid = p.ParagraphId?.Value;
            var text = p.InnerText;
            if (!string.IsNullOrEmpty(pid) && text.Length >= minLen)
            {
                result.Add((pid!, text));
            }
        }

        return result;
    }

    /// <summary>Direct body-child paragraphs (paraId + text) whose runs are simple w:t runs — safe to bold/split.</summary>
    private static List<(string ParaId, string Text)> SimpleBodyParagraphs(byte[] docx, int minLen)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var result = new List<(string, string)>();
        foreach (var p in doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>())
        {
            var pid = p.ParagraphId?.Value;
            if (string.IsNullOrEmpty(pid) || p.Elements<Hyperlink>().Any())
            {
                continue;
            }

            var runs = p.Elements<Run>().ToList();
            if (runs.Count == 0 || !runs.All(r => r.ChildElements.All(c => c is RunProperties || c is Text)))
            {
                continue;
            }

            if (p.InnerText.Length >= minLen)
            {
                result.Add((pid!, p.InnerText));
            }
        }

        return result;
    }

    private static Paragraph MakeParagraph(string paraId, string text, ParagraphProperties? pPr)
    {
        var p = new Paragraph { ParagraphId = new HexBinaryValue(paraId) };
        if (pPr is not null)
        {
            p.AppendChild(pPr.CloneNode(true));
        }

        p.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }

    private static string FreshParaId(HashSet<string> existing)
    {
        // Deterministic (no RNG in tests), OOXML-valid (0 < x < 0x80000000), collision-checked.
        for (var candidate = 0x0F0F0F0F; ; candidate++)
        {
            var hex = candidate.ToString("X8");
            if (!existing.Contains(hex))
            {
                return hex;
            }
        }
    }

    private static byte[] Mutate(byte[] original, Action<Body> mutate)
    {
        using var ms = new MemoryStream();
        ms.Write(original, 0, original.Length);
        ms.Position = 0;
        using (var doc = WordprocessingDocument.Open(ms, isEditable: true))
        {
            mutate(doc.MainDocumentPart!.Document!.Body!);
            doc.MainDocumentPart!.Document.Save();
        }

        return ms.ToArray();
    }

    private sealed record Revisions(int InsCount, int DelCount, IReadOnlyList<string> Authors, int RunFormatChanges, int ParagraphFormatChanges);

    private static Revisions ReadRevisions(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var ins = body.Descendants<InsertedRun>().ToList();
        var del = body.Descendants<DeletedRun>().ToList();
        var authors = ins.Select(i => i.Author?.Value)
            .Concat(del.Select(d => d.Author?.Value))
            .Where(a => a is not null).Select(a => a!).Distinct().ToList();
        return new Revisions(
            ins.Count, del.Count, authors,
            body.Descendants<RunPropertiesChange>().Count(),
            body.Descendants<ParagraphPropertiesChange>().Count());
    }

    private static IReadOnlyList<string> DeletedTextValues(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!
            .Descendants<DeletedText>()
            .Select(d => d.Text)
            .ToList();
    }
}
