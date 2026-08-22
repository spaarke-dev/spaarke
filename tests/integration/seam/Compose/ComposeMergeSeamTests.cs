// Task 040 (spaarkeai-compose-r8) — THE MERGE, in production form. ADR-049 R8 third amendment.
//
// The task-030 prototype measured ONE scenario: edit one paragraph, change nothing else. That is the
// scenario the gate needed and it is not the scenario users produce. These tests cover what the prototype
// did not reach — insert, delete, property inheritance on the edited block, list continuity across cloned
// blocks, and relationship survival — plus the negatives the amendment turns on.
//
// MAINTAIN-class (tests/integration/seam/** KEEP path per ADR-038). No Mock<HttpMessageHandler>, no
// DI-registration test, no ctor-null test, no reflection over privates: every assertion here is made
// against real .docx bytes produced by the real renderer.

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeMergeSeamTests
{
    private const string EditMarker = " [MERGE-040]";
    private const string ExternalUrl = "https://example.com/clause-library";

    private readonly ITestOutputHelper _output;

    public ComposeMergeSeamTests(ITestOutputHelper output) => _output = output;

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. INSERT and DELETE — the cases positional pairing cannot survive.
    //
    // Index-for-index pairing collapses to ZERO preservation the moment a block is inserted or deleted:
    // every subsequent index shifts by one and nothing matches. "The user added a paragraph" is the most
    // common edit there is, and the prototype never met it — it only ever measured single-run edits at a
    // fixed block count. These two tests are the reason the production merge aligns by longest common
    // subsequence instead.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Merge_ParagraphInsertedAtTheTop_StillClonesEveryUntouchedBlock()
    {
        var source = BuildFormattedSource();
        var model = Project(source);
        var originalCount = model.Blocks.Count;

        var withInsert = model with
        {
            Blocks = new[] { NewParagraph("A brand new opening paragraph.") }
                .Concat(model.Blocks)
                .ToList(),
        };

        var rendered = Render(source, withInsert, out var stats);

        stats.ClonedBlocks.Should().Be(originalCount,
            "every pre-existing block is unchanged and must be cloned — an inserted block shifts indices, " +
            "and an alignment that cannot absorb the shift would re-render (and damage) the entire document");
        stats.RenderedBlocks.Should().Be(1, "only the inserted block has no base counterpart");
        stats.RenderedWithoutCounterpart.Should().Be(1, "an inserted block genuinely has no base side");

        BodyText(rendered).Should().Contain("A brand new opening paragraph.");
        AssertIndentAndSpacingSurvive(rendered, "the formatted clause");
    }

    [Fact]
    public void Merge_ParagraphDeleted_StillClonesEveryRemainingBlock()
    {
        var source = BuildFormattedSource();
        var model = Project(source);
        model.Blocks.Count.Should().BeGreaterThan(2, "the fixture needs a block to delete and blocks to keep");

        var kept = model.Blocks.Skip(1).ToList();
        var rendered = Render(source, model with { Blocks = kept }, out var stats);

        stats.ClonedBlocks.Should().Be(kept.Count, "every surviving block is unchanged and must be cloned");
        stats.RenderedBlocks.Should().Be(0, "a deletion re-renders nothing — it just stops cloning one block");
        AssertIndentAndSpacingSurvive(rendered, "the formatted clause");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. FR-A04 — the EDITED block must not collapse to Normal.
    //
    // This is the one the gate does NOT measure: the oracle excludes the edited block by construction, so
    // a merge could score 100% while still destroying the only paragraph the user actually touched. That
    // is what users report as "it destroyed my document".
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Merge_EditedBlock_InheritsBaseParagraphAndRunProperties()
    {
        var source = BuildFormattedSource();
        var model = Project(source);

        var edited = EditFirstRunOf(model, "the formatted clause");
        var rendered = Render(source, edited, out var stats);

        stats.RenderedBlocks.Should().Be(1, "exactly one block changed");
        stats.RenderedWithoutCounterpart.Should().Be(0,
            "an EDITED block still has a base counterpart — inheritance depends on the alignment finding it");

        var paragraph = ParagraphContaining(rendered, EditMarker);
        var pPr = paragraph.ParagraphProperties;
        pPr.Should().NotBeNull("the edited paragraph must carry inherited properties, not an empty pPr");

        pPr!.Indentation?.Left?.Value.Should().Be("720",
            "the base paragraph's indent is not in the content model, so it can only survive by inheritance");
        pPr.SpacingBetweenLines?.Before?.Value.Should().Be("240", "same for spacing");

        var run = paragraph.Elements<Run>().First(r => r.InnerText.Contains(EditMarker, StringComparison.Ordinal));
        var rPr = run.RunProperties;
        rPr.Should().NotBeNull("the edited run must inherit the base paragraph's dominant run properties");
        rPr!.RunFonts?.Ascii?.Value.Should().Be("Garamond", "font is not in the content model");
        rPr.FontSize?.Val?.Value.Should().Be("28", "size is not in the content model");
        rPr.Color?.Val?.Value.Should().Be("C00000", "colour is not in the content model");
    }

    [Fact]
    public void Merge_EditedBlock_DoesNotInheritStyleOrNumberingTheModelDetermines()
    {
        var source = BuildFormattedSource();
        var model = Project(source);

        // Turn the HEADING into a plain paragraph — the user demoted it. Inheriting w:pStyle would put the
        // heading straight back and silently undo the edit.
        var blocks = model.Blocks.ToList();
        var headingIndex = blocks.FindIndex(b => b.Kind == ComposeBlockKind.Heading);
        headingIndex.Should().BeGreaterThanOrEqualTo(0, "the fixture must contain a heading");
        blocks[headingIndex] = blocks[headingIndex] with { Kind = ComposeBlockKind.Paragraph, Level = 0 };

        var rendered = Render(source, model with { Blocks = blocks }, out _);

        var paragraph = ParagraphContaining(rendered, "Master Services Agreement");
        paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value
            .Should().NotStartWith("Heading", "the user demoted this block; inheritance must not restore the style");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. List continuity across cloned blocks — the prototype's limitation 3.
    //
    // A cloned list item never passed through the renderer's ordered-run bookkeeping, so a rendered item
    // following clones computed continuity against a cursor that had never seen them and restarted at 1.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Merge_RenderedListItemAfterClonedListItems_ContinuesTheClonedList()
    {
        var source = BuildNumberedListSource();
        var model = Project(source);
        var listBlocks = model.Blocks.Where(b => b.Kind == ComposeBlockKind.ListItem).ToList();
        listBlocks.Should().HaveCountGreaterThan(1, "the fixture must contain a real numbered list");

        var sourceNumId = listBlocks[0].NumId;
        sourceNumId.Should().NotBeNull("the projection must carry the carrier's numId for an imported list");

        // The user adds an item to the end of an imported list. A born-in-editor item carries NO NumId —
        // its continuity comes entirely from the run cursor, which is what cloned items must now advance.
        var appended = model.Blocks.Append(new ComposeBlock
        {
            Kind = ComposeBlockKind.ListItem,
            Ordered = true,
            Level = 0,
            StartsNewList = false,
            NumId = null,
            Runs = new[] { new ComposeInlineRun { Text = "A newly typed fourth item." } },
        }).ToList();

        var rendered = Render(source, model with { Blocks = appended }, out var stats);

        stats.ClonedBlocks.Should().Be(model.Blocks.Count, "every original block is untouched");
        stats.RenderedBlocks.Should().Be(1, "only the appended item is rendered");

        var newItem = ParagraphContaining(rendered, "A newly typed fourth item.");
        var newNumId = newItem.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
        newNumId.Should().Be(sourceNumId,
            "a typed continuation of a CLONED list must join that list's own w:num instance — otherwise it " +
            "starts a fresh instance and Word restarts the numbering at 1");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. Cloned blocks carry relationship references. Those must still resolve.
    //
    // Asserted rather than assumed: a clone keeps the carrier's own r:id, and the carrier's package is the
    // one being written, so the relationship is still there — but "should be" is not evidence, and a
    // dangling r:id is a document Word refuses to open.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Merge_ClonedBlock_KeepsAResolvableHyperlinkRelationship()
    {
        var source = BuildHyperlinkSource();
        var model = Project(source);

        // Edit a DIFFERENT paragraph so the hyperlink paragraph is cloned.
        var edited = EditFirstRunOf(model, "Unrelated opening prose.");
        var rendered = Render(source, edited, out var stats);

        stats.ClonedBlocks.Should().BeGreaterThan(0, "the hyperlink paragraph must be cloned, not re-rendered");

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var main = doc.MainDocumentPart!;
        var hyperlink = main.Document!.Body!.Descendants<Hyperlink>()
            .Single(h => h.InnerText.Contains("clause library", StringComparison.Ordinal));

        main.HyperlinkRelationships.Should().Contain(r => r.Id == hyperlink.Id!.Value,
            "a cloned hyperlink's r:id must still resolve — a dangling relationship is a document Word refuses to open");
        main.HyperlinkRelationships.Single(r => r.Id == hyperlink.Id!.Value).Uri.ToString().Should().Be(ExternalUrl);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 5. FR-A03 on the corpus — constructs the content model cannot represent survive an unrelated edit
    //    because nothing interprets them.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    // hasNearTier is declared PER FIXTURE rather than inferred, so that a document which loses its
    // near-tier constructs (or gains them) fails this test instead of quietly reporting "not measured".
    // An empty denominator reports null, never 100 — "nothing to measure" and "measured, nothing lost"
    // must never be the same value (task 020).
    [Theory]
    [InlineData("footnote-references.docx", true)]
    [InlineData("court-filing-spacing.docx", true)]
    [InlineData("content-controls-sdt.docx", true)]
    [InlineData("interior-text-boxes.docx", false)]
    public void Merge_UnmodeledConstructs_SurviveAnUnrelatedEdit(string fileName, bool hasNearTier)
    {
        var source = LoadCorpus(fileName);
        var model = Project(source);
        var edited = EditFirstNonEmptyRun(model, out var applied);
        applied.Should().BeTrue($"[{fileName}] the corpus document must expose an editable run");

        var rendered = Render(source, edited, out var stats);

        var report = ComposeBlockPreservationOracle.Compare(
            source, rendered, EditMarker, ComposeBlockPreservationOracle.ComparisonLevel.Lenient);

        _output.WriteLine(
            $"{fileName}: overall={report.OverallPreservationPercent:F2} nearTier={report.NearTierPreservationPercent:F2} " +
            $"cloned={stats.ClonedBlocks} rendered={stats.RenderedBlocks}");

        report.OverallPreservationPercent.Should().Be(100d,
            $"[{fileName}] every untouched block survives by being cloned, not by per-construct preservation logic");

        if (hasNearTier)
        {
            report.NearTierRelevantCount.Should().BeGreaterThan(0,
                $"[{fileName}] is declared to carry near-tier constructs — a zero denominator here means the " +
                "fixture changed and the near-tier assertion below would be vacuous");
            report.NearTierPreservationPercent.Should().Be(100d,
                $"[{fileName}] every near-tier construct on an untouched block survives by being cloned");
        }
        else
        {
            report.NearTierPreservationPercent.Should().BeNull(
                $"[{fileName}] carries no near-tier construct, so the oracle must report NOT MEASURED rather " +
                "than a vacuous 100%");
        }
        report.EditedBlockIndex.Should().BeGreaterThanOrEqualTo(0,
            $"[{fileName}] the oracle must have located and excluded the edited block — a report that excluded " +
            "NOTHING would be measuring a merge that cloned the user's edit away");
        BodyText(rendered).Should().Contain(EditMarker, $"[{fileName}] the user's edit must be in the output");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 6. NEGATIVES — the amendment's invariant 1. Nothing here may produce a refusal.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Merge_EmptyCarrierBody_FailsOpenAndStillSaves()
    {
        var source = BuildEmptyBodySource();
        var model = new ComposeContentModel
        {
            Blocks = new[] { NewParagraph("Content typed into an empty carrier.") },
        };

        var stats = new ComposeMergeStats();
        var rendered = new ComposeDocumentRenderer().RenderIntoCarrier(
            source, model, "seam-test", degradations: null, mergeUnchangedBlocks: true, mergeStats: stats);

        stats.BaselineUnavailable.Should().BeTrue("a carrier with no body blocks has no base side to merge against");
        stats.ClonedBlocks.Should().Be(0);
        rendered.Should().NotBeEmpty(
            "an unavailable base side must FAIL OPEN to a plain render — a save is never refused because the " +
            "merge could not run (ADR-049 invariant 1)");
        BodyText(rendered).Should().Contain("Content typed into an empty carrier.");
    }

    [Fact]
    public void Merge_EveryPostedBlockIsAccountedFor()
    {
        var source = BuildFormattedSource();
        var model = Project(source);
        var edited = EditFirstRunOf(model, "the formatted clause");

        Render(source, edited, out var stats);

        stats.TotalBlocks.Should().Be(model.Blocks.Count,
            "every posted block is either cloned or rendered — a block that is neither has been silently dropped");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static byte[] Render(byte[] carrier, ComposeContentModel model, out ComposeMergeStats stats)
    {
        stats = new ComposeMergeStats();
        return new ComposeDocumentRenderer().RenderIntoCarrier(
            carrier, model, "seam-test", degradations: null, mergeUnchangedBlocks: true, mergeStats: stats);
    }

    private static ComposeContentModel Project(byte[] bytes)
    {
        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(bytes);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed, "the fixture must project");
        return projection.Model!;
    }

    private static ComposeBlock NewParagraph(string text) => new()
    {
        Kind = ComposeBlockKind.Paragraph,
        Runs = new[] { new ComposeInlineRun { Text = text } },
    };

    private static ComposeContentModel EditFirstRunOf(ComposeContentModel model, string containing)
    {
        var blocks = model.Blocks.ToList();
        var index = blocks.FindIndex(b => b.Runs.Any(r => r.Text?.Contains(containing, StringComparison.Ordinal) == true));
        index.Should().BeGreaterThanOrEqualTo(0, $"the fixture must contain a run with '{containing}'");

        var runs = blocks[index].Runs.ToList();
        var runIndex = runs.FindIndex(r => r.Text?.Contains(containing, StringComparison.Ordinal) == true);
        runs[runIndex] = runs[runIndex] with { Text = runs[runIndex].Text + EditMarker };
        blocks[index] = blocks[index] with { Runs = runs };
        return model with { Blocks = blocks };
    }

    private static ComposeContentModel EditFirstNonEmptyRun(ComposeContentModel model, out bool applied)
    {
        var blocks = model.Blocks.ToList();
        for (var b = 0; b < blocks.Count; b++)
        {
            var runs = blocks[b].Runs;
            for (var r = 0; r < runs.Count; r++)
            {
                if (string.IsNullOrEmpty(runs[r].Text))
                {
                    continue;
                }

                var newRuns = runs.ToList();
                newRuns[r] = newRuns[r] with { Text = newRuns[r].Text + EditMarker };
                blocks[b] = blocks[b] with { Runs = newRuns };
                applied = true;
                return model with { Blocks = blocks };
            }
        }

        applied = false;
        return model;
    }

    private static byte[] LoadCorpus(string fileName)
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
        return ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
    }

    private static string BodyText(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.InnerText;
    }

    private static Paragraph ParagraphContaining(byte[] docx, string text)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => p.InnerText.Contains(text, StringComparison.Ordinal))
            .CloneNode(true) as Paragraph
            ?? throw new InvalidOperationException($"No paragraph containing '{text}'.");
    }

    private static void AssertIndentAndSpacingSurvive(byte[] rendered, string marker)
    {
        var paragraph = ParagraphContaining(rendered, marker);
        paragraph.ParagraphProperties?.Indentation?.Left?.Value.Should().Be("720",
            "an untouched block is cloned, so its indent survives without any per-property code");
        paragraph.ParagraphProperties?.SpacingBetweenLines?.Before?.Value.Should().Be("240");
    }

    // ── fixtures ─────────────────────────────────────────────────────────────────────────────────

    private static byte[] BuildFormattedSource() => BuildDocx(body =>
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
            new Run(new Text("Master Services Agreement"))));

        body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new Indentation { Left = "720" },
                new SpacingBetweenLines { Before = "240", After = "240" }),
            new Run(
                new RunProperties(
                    new RunFonts { Ascii = "Garamond", HighAnsi = "Garamond" },
                    new FontSize { Val = "28" },
                    new Color { Val = "C00000" }),
                new Text("This is the formatted clause the parties negotiated at length."))));

        body.AppendChild(new Paragraph(
            new ParagraphProperties(new Indentation { Left = "720" }, new SpacingBetweenLines { Before = "240" }),
            new Run(new Text("A second formatted clause that nobody touches."))));
    });

    private static byte[] BuildHyperlinkSource() => BuildDocx((body, main) =>
    {
        body.AppendChild(new Paragraph(new Run(new Text("Unrelated opening prose."))));

        var rel = main.AddHyperlinkRelationship(new Uri(ExternalUrl), isExternal: true);
        body.AppendChild(new Paragraph(
            new Run(new Text("See the ")),
            new Hyperlink(new Run(new Text("clause library"))) { Id = rel.Id },
            new Run(new Text(" for precedent."))));
    });

    private static byte[] BuildNumberedListSource() => BuildDocx((body, main) =>
    {
        var numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
        numberingPart.Numbering = new Numbering(
            new AbstractNum(
                new Level(
                    new NumberingFormat { Val = NumberFormatValues.Decimal },
                    new LevelText { Val = "%1." },
                    new StartNumberingValue { Val = 1 })
                { LevelIndex = 0 })
            { AbstractNumberId = 7 },
            new NumberingInstance(new AbstractNumId { Val = 7 }) { NumberID = 5 });
        numberingPart.Numbering.Save();

        foreach (var text in new[] { "First imported item.", "Second imported item.", "Third imported item." })
        {
            body.AppendChild(new Paragraph(
                new ParagraphProperties(
                    new ParagraphStyleId { Val = "ListParagraph" },
                    new NumberingProperties(
                        new NumberingLevelReference { Val = 0 },
                        new NumberingId { Val = 5 })),
                new Run(new Text(text))));
        }
    });

    private static byte[] BuildEmptyBodySource() => BuildDocx(_ => { });

    private static byte[] BuildDocx(Action<Body> populate) => BuildDocx((body, _) => populate(body));

    private static byte[] BuildDocx(Action<Body, MainDocumentPart> populate)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            main.Document = new Document(body);
            populate(body, main);
            body.AppendChild(new SectionProperties(new PageSize { Width = 12240u, Height = 15840u }));
            main.Document.Save();
        }

        return stream.ToArray();
    }
}
