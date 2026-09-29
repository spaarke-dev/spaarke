// Task 044 (spaarkeai-compose-r8) — WHAT THE SAVE BANNER ACTUALLY SAYS.
//
// The owner keeps seeing "Some formatting was simplified when saving" in dev. Task 031's gate decision
// predicted why: the accept-flatten warning taxonomy predates the merge. A document with text boxes, fields
// or content controls warned because the PROJECTION flattens them for display — but since task 040 those
// blocks are CLONED VERBATIM on save and nothing is lost. Warning about a loss that no longer occurs is
// worse than not warning: it trains the reader to ignore the warnings that do matter.
//
// Measured before changed, like every other claim in this project.
//
// MAINTAIN-class (tests/integration/seam/** KEEP path per ADR-038).

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeSaveWarningTaxonomyTests
{
    private const string EditMarker = " [WARN-044]";

    private readonly ITestOutputHelper _output;

    public ComposeSaveWarningTaxonomyTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string> CorpusDocumentNames()
    {
        var data = new TheoryData<string>();
        foreach (var path in ComposeCorpusFixtureLocator.EnumerateDocumentPaths())
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    /// <summary>
    /// Records every degradation warning a one-paragraph edit produces on the RENDER path, per document.
    /// </summary>
    /// <remarks>
    /// Not a threshold assertion — the taxonomy decision belongs to the measurement, not the other way
    /// round. What IS asserted: the edit landed, so the run measured a real save.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CorpusDocumentNames))]
    public void SaveWarnings_AreMeasuredPerDocument(string fileName)
    {
        var source = LoadCorpus(fileName);
        var model = Project(source);
        var edited = EditFirstNonEmptyRun(model, out var applied);
        applied.Should().BeTrue($"[{fileName}] the corpus document must expose an editable run");

        var warnings = new List<ComposeProjectionWarning>();
        var stats = new ComposeMergeStats();
        var rendered = new ComposeDocumentRenderer().RenderIntoCarrier(
            source, edited, "seam-044", warnings, mergeUnchangedBlocks: true, mergeStats: stats);

        var codes = warnings.Select(w => $"{w.Code}×{w.Count}").OrderBy(c => c, StringComparer.Ordinal).ToList();
        _output.WriteLine(codes.Count == 0
            ? $"{fileName}: NO save warnings (cloned={stats.ClonedBlocks} rendered={stats.RenderedBlocks})"
            : $"{fileName}: {string.Join(", ", codes)} (cloned={stats.ClonedBlocks} rendered={stats.RenderedBlocks})");

        ExtractBodyText(rendered).Should().Contain(
            EditMarker, $"[{fileName}] the edit must land — otherwise this measures nothing");
    }

    /// <summary>
    /// The rule the merge makes true and this test pins: a document whose ONLY unmodeled constructs sit on
    /// UNTOUCHED blocks must produce no render-path degradation warning, because those blocks are cloned
    /// verbatim and nothing is simplified.
    /// </summary>
    [Theory]
    [InlineData("interior-text-boxes.docx", "text boxes")]
    [InlineData("content-controls-sdt.docx", "a content control")]
    [InlineData("footnote-references.docx", "footnote references")]
    [InlineData("ref-cross-references.docx", "bookmarks and cross-references")]
    public void SaveWarnings_DoNotFireForConstructsThatWereCloned(string fileName, string construct)
    {
        var source = LoadCorpus(fileName);
        var model = Project(source);
        var edited = EditFirstNonEmptyRun(model, out var applied);
        applied.Should().BeTrue();

        var warnings = new List<ComposeProjectionWarning>();
        var stats = new ComposeMergeStats();
        new ComposeDocumentRenderer().RenderIntoCarrier(
            source, edited, "seam-044", warnings, mergeUnchangedBlocks: true, mergeStats: stats);

        stats.ClonedBlocks.Should().BeGreaterThan(0,
            $"[{fileName}] must exercise the clone path, or this proves nothing about cloned constructs");

        warnings.Should().BeEmpty(
            $"[{fileName}] carries {construct} on blocks the user did not touch. Those blocks are CLONED " +
            "verbatim, so nothing about them is simplified — a warning here is FALSE, and a false warning " +
            "trains the reader to ignore the true ones");
    }

    /// <summary>
    /// The other half, and the one that matters more: suppression must not become silence.
    /// </summary>
    /// <remarks>
    /// <para>A construct on a block the user EDITED cannot be cloned — the block is genuinely different, so
    /// it is rebuilt from a model that cannot express soft breaks. That loss is real and must be reported.
    /// Before task 044 the corpus produced ZERO render-path warnings while this exact loss was happening.</para>
    ///
    /// <para>The first draft of this test asserted that DELETING the block carrying a content control must
    /// warn. That premise was wrong: a user deleting a paragraph intends the paragraph to go, and warning
    /// "content simplified" for an intentional deletion is the same false-signal problem from the other
    /// direction. The honest case is the edited block, which is what this asserts.</para>
    /// </remarks>
    [Fact]
    public void SaveWarnings_FireWhenAnEditedBlockLosesAConstructTheModelCannotHold()
    {
        // Task 046 re-levered this test. It used to edit "Engagement Letter.docx", whose loss was two
        // dropped soft breaks — and task 046 taught soft breaks to round-trip, so that document now
        // loses NOTHING and the test would have been asserting a loss that no longer happens. The lever
        // moved to a still-lossy construct rather than the assertion being weakened: a complex field,
        // which the content model genuinely could not hold.
        //
        // Task 049 re-levered it AGAIN — fields round-trip now too. The lever is a FOOTNOTE REFERENCE, an
        // accepted loss on the residual list with no scheduled carry, so it should not need moving a third
        // time. Both paragraphs that carry one are edited so the COUNT assertion below still measures
        // instances rather than degenerating to a single-item check that would pass for either meaning.

        var source = LoadCorpus("footnote-references.docx");
        var model = Project(source);
        // Blocks 1 and 2 each carry a w:footnoteReference; block 0 carries none.
        var edited = EditBlocks(model, new[] { 1, 2 }, out var applied);
        applied.Should().BeTrue();

        var warnings = new List<ComposeProjectionWarning>();
        var stats = new ComposeMergeStats();
        new ComposeDocumentRenderer().RenderIntoCarrier(
            source, edited, "seam-044", warnings, mergeUnchangedBlocks: true, mergeStats: stats);

        stats.RenderedBlocks.Should().Be(2, "exactly the two reference-bearing blocks were edited");
        _output.WriteLine($"warnings: {string.Join(", ", warnings.Select(w => $"{w.Code}×{w.Count}"))}");

        warnings.Should().Contain(w => w.Code == "unrepresented-footnote-reference",
            "the edited paragraphs each carry a w:footnoteReference that the content model cannot hold, so " +
            "it is genuinely lost — and a loss the save does not report is the failure this whole project " +
            "exists to end. Suppressing FALSE warnings is only safe if the TRUE ones fire in the same change");

        warnings.Where(w => w.Code == "unrepresented-footnote-reference").Sum(w => w.Count).Should().Be(2,
            "the count must be the number of things actually lost, not the number of KINDS of thing — a " +
            "banner that says 'something was simplified' without saying how much is barely a signal");
    }

    /// <summary>
    /// Deleting a block is INTENTIONAL, so its constructs going with it must not warn. The mirror image of
    /// the test above, and the reason that test's first draft was wrong.
    /// </summary>
    [Fact]
    public void SaveWarnings_DoNotFireWhenTheUserDeletesTheBlockEntirely()
    {
        var source = LoadCorpus("content-controls-sdt.docx");
        var model = Project(source);

        var blocks = model.Blocks.ToList();
        blocks.RemoveAt(0);

        var warnings = new List<ComposeProjectionWarning>();
        var rendered = new ComposeDocumentRenderer().RenderIntoCarrier(
            source, model with { Blocks = blocks }, "seam-044", warnings, mergeUnchangedBlocks: true);

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), false);
        doc.MainDocumentPart!.Document!.Body!.Descendants<SdtBlock>().Should().BeEmpty(
            "the user deleted the block that carried the control");

        warnings.Should().BeEmpty(
            "the deletion was the user's instruction, not a fidelity loss — warning 'content simplified' for " +
            "something the user deliberately removed is the same false signal as warning about a cloned block");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static ComposeContentModel Project(byte[] bytes)
    {
        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(bytes);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed, "the fixture must project");
        return projection.Model!;
    }

    private static byte[] LoadCorpus(string fileName)
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
        return ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
    }

    /// <summary>Task 049: edits the first text-bearing run of each NAMED block — for a fixture whose
    /// construct sits in more than one paragraph and where the warning COUNT is the thing under test.</summary>
    private static ComposeContentModel EditBlocks(ComposeContentModel model, int[] blockIndexes, out bool applied)
    {
        var blocks = model.Blocks.ToList();
        applied = true;

        foreach (var b in blockIndexes)
        {
            if (b < 0 || b >= blocks.Count)
            {
                applied = false;
                continue;
            }

            var runs = blocks[b].Runs;
            var editedThisBlock = false;
            for (var r = 0; r < runs.Count && !editedThisBlock; r++)
            {
                if (string.IsNullOrEmpty(runs[r].Text))
                {
                    continue;
                }

                var newRuns = runs.ToList();
                newRuns[r] = newRuns[r] with { Text = newRuns[r].Text + EditMarker };
                blocks[b] = blocks[b] with { Runs = newRuns };
                editedThisBlock = true;
            }

            applied &= editedThisBlock;
        }

        return model with { Blocks = blocks };
    }

    /// <summary>Edits the LAST block that has text — for fixtures whose construct is not in the first
    /// paragraph. Same single-run edit as the first-run variant, from the other end.</summary>
    private static ComposeContentModel EditLastNonEmptyRun(ComposeContentModel model, out bool applied)
    {
        var blocks = model.Blocks.ToList();
        for (var b = blocks.Count - 1; b >= 0; b--)
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

    private static string ExtractBodyText(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.InnerText;
    }
}
