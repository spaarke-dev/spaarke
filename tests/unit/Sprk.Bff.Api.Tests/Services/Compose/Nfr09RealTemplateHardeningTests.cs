// NFR-09 (task 003) — the real-firm-template hardening GATE for the E1 delta-save cutover (task 022).
// Runs the E1 redline engine on GENUINELY Word-authored legal templates (Common Paper Cloud Service
// Agreement + Mutual NDA — CC BY 4.0 public standards; see Fixtures/Compose/RealTemplates/README.md)
// rather than synthetic spike fixtures.
//
// GATE VERDICT: ✅ PASS (under Option C). The E1 engine is now ComposeParagraphRedlineSynthesizer
// (design §4.2, Option C) — it synthesizes the tracked-change redline directly from the paraId-keyed
// edits, touching only the changed paragraphs. On the real templates it PRESERVES every w14:paraId and
// every table (top-level + nested).
//
// History: the original plan ran Docxodus WmlComparer (tasks 001/020/021). This same harness proved on
// these same real templates that WmlComparer — in BOTH 6.4.0 (net8) and 7.1.0 (net10) — STRIPS
// w14:paraId (-> pt14:Unid) and DROPS an unchanged top-level table, failing FR-07/NFR-07. That verdict
// (and the empirical 7.1.0 probe) drove the pivot to Option C, which this harness now certifies. See
// notes/spikes/S1-nfr09-real-template-hardening-2026-07-17.md.
//
// KEEP-class ADR-038 fidelity test: real .docx round-trips through the real service; no transport mocks.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private static readonly DateTimeOffset Stamp = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly ComposeParagraphRedlineSynthesizer _sut = new();

    [Theory]
    [InlineData(Csa)]
    [InlineData(Nda)]
    public void RealTemplate_SynthesizedRedline_PreservesEveryParaIdAndTable_EmitsMinimalAuthoredRevisions(string fileName)
    {
        var original = LoadTemplate(fileName);
        var origIds = BodyParaIds(original);
        origIds.Should().NotBeEmpty("a real Word-authored template carries w14:paraId on body paragraphs");
        var before = StructuralParts(original);

        var targets = EditableParagraphs(original, minLen: 25).Take(3).ToList();
        targets.Should().HaveCountGreaterThanOrEqualTo(3);
        var edits = targets.Select(t => new ComposeEditedParagraph(t.ParaId, t.Text + " (as amended)")).ToList();

        var redline = _sut.SynthesizeRedline(original, edits, Author, Stamp);

        // === THE FIDELITY BAR WmlComparer FAILED — now PASSES under Option C ===
        BodyParaIds(redline).Should().BeEquivalentTo(origIds, "Option C preserves EVERY w14:paraId (only run content is rewritten; the w:p attribute survives)");
        Pt14UnidCount(redline).Should().Be(0, "no PowerTools involved — no pt14:Unid introduced");
        StructuralParts(redline).Should().BeEquivalentTo(before, "styles, numbering, footnotes, headers, footers, and every table (incl. nested) are preserved");

        // === correct, minimal, authored revisions ===
        var rev = ReadRevisions(redline);
        (rev.Ins + rev.Del).Should().BeGreaterThan(0, "the 3 edits synthesize tracked revisions");
        (rev.Ins + rev.Del).Should().BeLessThan(50, "minimal — only the 3 edited paragraphs carry revisions, not a whole-doc rewrite");
        rev.Authors.Should().OnlyContain(a => a == Author);

        // === untouched paragraphs pass through byte-identical ===
        var editedIds = edits.Select(e => e.ParaId.ToUpperInvariant()).ToHashSet();
        var untouched = origIds.First(id => !editedIds.Contains(id));
        ParagraphText(redline, untouched).Should().Be(ParagraphText(original, untouched), "untouched paragraphs are unchanged");
        ParagraphHasRevisions(redline, untouched).Should().BeFalse("untouched paragraphs carry no markup");
    }

    [Fact]
    public void RealTemplate_MixedWordEdit_EmitsBothInsertionAndDeletion_KeepingUnchangedWordsPlain()
    {
        var original = LoadTemplate(Csa);
        var target = EditableParagraphs(original, minLen: 60).First();
        var words = target.Text.Split(' ');

        // keep word0, INSERT after it, DELETE word1, keep the rest → forces equal + insert + delete.
        var newText = words[0] + " (as amended)" + string.Join(" ", words.Skip(2));
        var redline = _sut.SynthesizeRedline(original, new[] { new ComposeEditedParagraph(target.ParaId, newText) }, Author, Stamp);

        var rev = ReadRevisions(redline);
        rev.Ins.Should().BeGreaterThan(0);
        rev.Del.Should().BeGreaterThan(0);
        BodyParaIds(redline).Should().Contain(target.ParaId.ToUpperInvariant(), "the edited paragraph keeps its paraId");
    }

    // ── fixture + OOXML read helpers ─────────────────────────────────────────────────────────────

    private static byte[] LoadTemplate(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Compose", "RealTemplates", fileName);
        File.Exists(path).Should().BeTrue($"the real-template fixture '{fileName}' must be copied to the test output (see csproj Content Include)");
        return File.ReadAllBytes(path);
    }

    private static HashSet<string> BodyParaIds(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Select(p => p.ParagraphId?.Value).Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
    }

    private static int Pt14UnidCount(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Count(p => p.GetAttributes().Any(a => a.LocalName == "Unid" && a.NamespaceUri == Pt14Ns));
    }

    private sealed record Parts(bool HasStyles, bool HasNumbering, bool HasFootnotes, bool HasHeaders, bool HasFooters, int TotalTables, int TopLevelTables);

    private static Parts StructuralParts(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        var main = doc.MainDocumentPart!;
        var tables = main.Document!.Body!.Descendants<Table>().ToList();
        return new Parts(
            main.StyleDefinitionsPart is not null,
            main.NumberingDefinitionsPart is not null,
            main.FootnotesPart is not null,
            main.HeaderParts.Any(),
            main.FooterParts.Any(),
            tables.Count,
            tables.Count(t => !t.Ancestors<Table>().Any()));
    }

    private static List<(string ParaId, string Text)> EditableParagraphs(byte[] docx, int minLen)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
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

    private sealed record Rev(int Ins, int Del, IReadOnlyList<string> Authors);

    private static Rev ReadRevisions(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var ins = body.Descendants<InsertedRun>().ToList();
        var del = body.Descendants<DeletedRun>().ToList();
        var authors = ins.Select(i => i.Author?.Value).Concat(del.Select(d => d.Author?.Value))
            .Where(a => a is not null).Select(a => a!).Distinct().ToList();
        return new Rev(ins.Count, del.Count, authors);
    }

    private static string ParagraphText(byte[] docx, string paraId)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase)).InnerText;
    }

    private static bool ParagraphHasRevisions(byte[] docx, string paraId)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        var p = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(x => string.Equals(x.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase));
        return p.Descendants<InsertedRun>().Any() || p.Descendants<DeletedRun>().Any();
    }
}
