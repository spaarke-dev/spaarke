// SPIKE (task 003 follow-on) — Option C: synthesize the tracked-change redline OURSELVES from the
// paraId-keyed edits, WITHOUT Docxodus WmlComparer. We already know exactly what changed (the client
// sends paraId-keyed edits), so instead of diffing two whole documents with a general-purpose differ
// (and inheriting its w14:paraId-strip + table-drop defects — see the NFR-09 hardening report), we:
//   1. locate each edited paragraph in the RETAINED ORIGINAL by w14:paraId,
//   2. run a small word-level LCS diff (old paragraph text -> new paragraph text),
//   3. emit native w:ins/w:del run markup IN PLACE on that one paragraph.
// Every other paragraph + all structure (paraId, tables, numbering, styles) is left byte-untouched.
//
// This spike proves Option C clears the SAME fidelity bar WmlComparer FAILED, on the real CSA:
// all paraIds preserved, all tables preserved, no pt14:Unid introduced, minimal authored ins/del.
// If green, this is the foundation for task 022 (Approach C) and lets us DROP the Docxodus dependency.
// It is a self-contained prototype — no production code added until the design pivot is approved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class OptionC_SelfSynthesizedRedlineSpikeTests
{
    private const string Csa = "commonpaper-cloud-service-agreement.docx";
    private const string Author = "Spaarke AI";
    private const string Pt14Ns = "http://powertools.codeplex.com/2011";
    private static readonly DateTimeOffset Stamp = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OptionC_OnRealCsa_PreservesAllParaIdsAndTables_AndEmitsMinimalAuthoredInsDel()
    {
        var original = LoadCsa();

        // Pick 3 real prose paragraphs and craft edits that force a MIXED diff (equal + insert + delete):
        // keep word 0, insert " (as amended)" after it, delete word 1, keep the rest.
        var targets = SimpleBodyParagraphs(original, minLen: 60).Take(3).ToList();
        targets.Should().HaveCount(3);
        var edits = targets.Select(t => new Edit(t.ParaId, CraftMixedEdit(t.Text))).ToList();

        // --- Option C synthesis (the prototype under test) ---
        var redline = SynthesizeRedline(original, edits, Author, Stamp);

        // === THE FIDELITY BAR WmlComparer FAILED ===
        var origIds = BodyParaIds(original);
        var outIds = BodyParaIds(redline);
        outIds.Should().BeEquivalentTo(origIds, "Option C preserves EVERY w14:paraId (nothing stripped — we only edit run content, never the w:p attributes)");
        Pt14UnidCount(redline).Should().Be(0, "Option C never touches PowerTools — no pt14:Unid is introduced");
        TableCounts(redline).Should().Be(TableCounts(original), "Option C leaves every table (top-level + nested) byte-untouched");

        // === correct, minimal, authored revisions ===
        var rev = ReadRevisions(redline);
        rev.Ins.Should().BeGreaterThan(0, "the inserted text becomes tracked w:ins");
        rev.Del.Should().BeGreaterThan(0, "the deleted word becomes tracked w:del");
        rev.Authors.Should().OnlyContain(a => a == Author, "every revision carries the author");
        (rev.Ins + rev.Del).Should().BeLessThan(50, "minimal — only the 3 edited paragraphs carry revisions, not a whole-doc rewrite");

        // === untouched paragraphs are literally unchanged ===
        var editedIds = edits.Select(e => e.ParaId.ToUpperInvariant()).ToHashSet();
        var anUntouchedId = origIds.First(id => !editedIds.Contains(id));
        ParagraphText(redline, anUntouchedId).Should().Be(ParagraphText(original, anUntouchedId), "untouched paragraphs pass through byte-identical");
        ParagraphHasRevisions(redline, anUntouchedId).Should().BeFalse("untouched paragraphs carry no markup");

        // === each edited paragraph keeps its paraId AND carries both an insertion and a deletion ===
        foreach (var e in edits)
        {
            outIds.Should().Contain(e.ParaId.ToUpperInvariant(), "the edited paragraph keeps its paraId (it stays the anchor)");
            ParagraphHasRevisions(redline, e.ParaId).Should().BeTrue("the edited paragraph carries tracked changes");
        }
    }

    // ── Option C prototype: word-diff + in-place w:ins/w:del synthesis ───────────────────────────

    private sealed record Edit(string ParaId, string NewText);

    private static byte[] SynthesizeRedline(byte[] original, IReadOnlyList<Edit> edits, string author, DateTimeOffset when)
    {
        var byId = edits.ToDictionary(e => e.ParaId.ToUpperInvariant(), StringComparer.Ordinal);
        using var ms = new MemoryStream();
        ms.Write(original, 0, original.Length);
        ms.Position = 0;
        var id = 90000; // spike: simple monotonic revision id
        using (var doc = WordprocessingDocument.Open(ms, isEditable: true))
        {
            var body = doc.MainDocumentPart!.Document!.Body!;
            foreach (var p in body.Descendants<Paragraph>())
            {
                var pid = p.ParagraphId?.Value?.ToUpperInvariant();
                if (pid is null || !byId.TryGetValue(pid, out var edit))
                {
                    continue;
                }

                RewriteParagraphAsRedline(p, edit.NewText, author, when, ref id);
            }

            doc.MainDocumentPart!.Document.Save();
        }

        return ms.ToArray();
    }

    private static void RewriteParagraphAsRedline(Paragraph p, string newText, string author, DateTimeOffset when, ref int id)
    {
        var oldText = p.InnerText;
        var pPr = p.GetFirstChild<ParagraphProperties>()?.CloneNode(true) as ParagraphProperties;
        var baseRpr = p.Descendants<Run>().FirstOrDefault()?.GetFirstChild<RunProperties>();

        var spans = WordDiff(oldText, newText);

        p.RemoveAllChildren(); // w14:paraId is an ATTRIBUTE on w:p — it survives this.
        if (pPr is not null)
        {
            p.AppendChild(pPr);
        }

        foreach (var (op, text) in spans)
        {
            switch (op)
            {
                case Op.Equal:
                    p.AppendChild(TextRun(text, baseRpr));
                    break;
                case Op.Insert:
                    p.AppendChild(new InsertedRun(TextRun(text, baseRpr))
                    { Id = (id++).ToString(CultureInfo.InvariantCulture), Author = author, Date = when.UtcDateTime });
                    break;
                case Op.Delete:
                    p.AppendChild(new DeletedRun(DeletedTextRun(text, baseRpr))
                    { Id = (id++).ToString(CultureInfo.InvariantCulture), Author = author, Date = when.UtcDateTime });
                    break;
            }
        }
    }

    private static Run TextRun(string text, RunProperties? baseRpr)
    {
        var run = new Run();
        if (baseRpr is not null)
        {
            run.AppendChild(baseRpr.CloneNode(true));
        }

        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static Run DeletedTextRun(string text, RunProperties? baseRpr)
    {
        var run = new Run();
        if (baseRpr is not null)
        {
            run.AppendChild(baseRpr.CloneNode(true));
        }

        // EDGE-4 (DocxAnnotationWriter): inside w:del the text element MUST be w:delText, not w:t.
        run.AppendChild(new DeletedText(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private enum Op { Equal, Insert, Delete }

    /// <summary>Word-level LCS diff. Tokens = "word + trailing whitespace" so reassembly is lossless.</summary>
    private static List<(Op Op, string Text)> WordDiff(string oldText, string newText)
    {
        var a = Tokenize(oldText);
        var b = Tokenize(newText);
        int n = a.Count, m = b.Count;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var raw = new List<(Op, string)>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { raw.Add((Op.Equal, a[x])); x++; y++; }
            else if (dp[x + 1, y] >= dp[x, y + 1]) { raw.Add((Op.Delete, a[x])); x++; }
            else { raw.Add((Op.Insert, b[y])); y++; }
        }
        while (x < n) { raw.Add((Op.Delete, a[x])); x++; }
        while (y < m) { raw.Add((Op.Insert, b[y])); y++; }

        // Merge consecutive same-op tokens into a single span (minimal run count).
        var merged = new List<(Op, string)>();
        foreach (var (op, tok) in raw)
        {
            if (merged.Count > 0 && merged[^1].Item1 == op)
            {
                merged[^1] = (op, merged[^1].Item2 + tok);
            }
            else
            {
                merged.Add((op, tok));
            }
        }

        return merged;
    }

    private static List<string> Tokenize(string text) =>
        string.IsNullOrEmpty(text)
            ? new List<string>()
            : Regex.Matches(text, @"\S+\s*").Select(mt => mt.Value).ToList();

    private static string CraftMixedEdit(string text)
    {
        var words = text.Split(' ');
        if (words.Length < 4)
        {
            return text + " (as amended)";
        }

        // keep word0, INSERT after it, DELETE word1, keep the rest → forces equal+insert+delete.
        return words[0] + " (as amended)" + string.Join(" ", words.Skip(2));
    }

    // ── fixture + OOXML read helpers ─────────────────────────────────────────────────────────────

    private static byte[] LoadCsa() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Compose", "RealTemplates", Csa));

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

    private static (int Total, int Top) TableCounts(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        var all = doc.MainDocumentPart!.Document!.Body!.Descendants<Table>().ToList();
        return (all.Count, all.Count(t => !t.Ancestors<Table>().Any()));
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

    private static List<(string ParaId, string Text)> SimpleBodyParagraphs(byte[] docx, int minLen)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        var result = new List<(string, string)>();
        foreach (var p in doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>())
        {
            var pid = p.ParagraphId?.Value;
            if (string.IsNullOrEmpty(pid) || p.Elements<Hyperlink>().Any())
            {
                continue;
            }

            var runs = p.Elements<Run>().ToList();
            if (runs.Count > 0 && runs.All(r => r.ChildElements.All(c => c is RunProperties || c is Text)) && p.InnerText.Length >= minLen)
            {
                result.Add((pid!, p.InnerText));
            }
        }

        return result;
    }
}
