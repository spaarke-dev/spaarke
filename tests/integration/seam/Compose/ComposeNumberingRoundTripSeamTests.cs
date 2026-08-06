// Task 033 (spaarkeai-compose-fidelity-r4.5, WS-3 final task) — the round-trip AGREEMENT seam test between
// the write-side numbering AUTHOR (ComposeDocumentRenderer.cs — the born-in-editor OOXML authoring engine,
// task 026/027) and the read-side numbering COMPUTATION (ComposeDocxProjectionBuilder.cs's
// NumberingComputationEngine, tasks 030/031). Spec FR-15 / design §4 WS-3 "Reference authoring parity":
// the two numbering models MUST agree — a document AUTHORED by the renderer, then READ back through the
// WS-3 computation, must display the SAME legal-clause labels the renderer intended.
//
// ADR-038 seam / KEEP-path vertical slice: drives the two REAL production components end-to-end (real
// ComposeDocumentRenderer.SynthesizeDocument -> real .docx bytes -> real ComposeDocxProjectionBuilder.Build).
// No Mock<HttpMessageHandler>, no DI-registration test, no ctor-null test (B1/B3/B4).
//
// Escalation posture (binding per the task POML + root CLAUDE.md §6.5): if the write-side's AUTHORED intent
// and the read-side's COMPUTED label diverge on ANY paragraph, this file's tests are LEFT FAILING and the
// divergence is documented in projects/spaarkeai-compose-fidelity-r4.5/notes/task-033-numbering-roundtrip-notes.md
// rather than "fixed" by weakening an assertion — a green-but-wrong assertion here would hide a real
// NFR-02 numbering-exactness defect.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeNumberingRoundTripSeamTests
{
    private readonly ComposeDocumentRenderer _renderer = new();

    // ── model builders (mirrors ComposeDocumentRendererTests.cs's pattern) ───────────────────────────

    private static ComposeBlock Heading(int level, string text) => new()
    {
        Kind = ComposeBlockKind.Heading,
        Level = level,
        Runs = new[] { new ComposeInlineRun { Text = text } },
    };

    private static ComposeBlock Paragraph(string text) => new()
    {
        Kind = ComposeBlockKind.Paragraph,
        Runs = new[] { new ComposeInlineRun { Text = text } },
    };

    private static ComposeBlock ListItem(int level, bool ordered, string text, bool startsNewList = false) => new()
    {
        Kind = ComposeBlockKind.ListItem,
        Level = level,
        Ordered = ordered,
        StartsNewList = startsNewList,
        Runs = new[] { new ComposeInlineRun { Text = text } },
    };

    /// <summary>Authors <paramref name="model"/> via the REAL renderer, then reads the bytes back via the
    /// REAL WS-3 projection builder, returning the computed label for every paragraph the projection's
    /// <c>ParaIdMap</c> emitted (null for non-numbered paragraphs), in document order.</summary>
    private IReadOnlyList<string?> RoundTripComputedLabels(ComposeContentModel model)
    {
        var docx = _renderer.SynthesizeDocument(model, author: "Round-Trip Test");
        var projection = new ComposeDocxProjectionBuilder().Build(docx);
        projection.Status.Should().Be(ComposeProjectionStatus.Success, "the renderer-authored doc must project cleanly");
        return projection.ParaIdMap.Select(e => e.ComputedNumber).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. Multi-level + style-linked heading cascade (FR-27 keystone construct) — SINGLE numbering
    //    instance throughout (HeadingNumInstanceId is always 1), so this is the case the write side's
    //    own doc comment says is unambiguous: read and write MUST agree.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RoundTrip_MultiLevelStyleLinkedHeadingCascade_ReadSideLabelsMatchWriteSideIntent()
    {
        // The renderer authors Heading{level} paragraphs with NO direct numId (style-linked only, FR-27):
        // the label is entirely a function of the style-linked cascade "%1" / "%1.%2" / "%1.%2.%3" the
        // renderer's BuildHeadingAbstractNum authors, replayed by the read side's per-(abstractNumId,ilvl)
        // counters. This exercises 3 levels + a level-0 increment that RESETS a deeper level (Termination).
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                Heading(1, "Definitions"),                    // ilvl0 -> "1"
                Heading(1, "Term"),                            // ilvl0 -> "2"
                Heading(2, "Sub A"),                            // ilvl1 -> "2.1"
                Heading(2, "Sub B"),                            // ilvl1 -> "2.2"
                Heading(1, "Termination"),                      // ilvl0 -> "3" (resets deeper level)
                Heading(2, "Sub C"),                            // ilvl1 -> "3.1" (level-1 counter reset)
                Heading(3, "Sub Sub"),                          // ilvl2 -> "3.1.1"
            },
        };

        // The write-side's INTENDED labels — derived from the SAME cascade text the renderer authors
        // (BuildHeadingAbstractNum's "%1"/"%1.%2"/"%1.%2.%3" lvlText, joined by "."), not re-derived
        // independently, so this is a like-for-like round-trip assertion, not a parallel hand-computation.
        var writeSideIntendedLabels = new[] { "1", "2", "2.1", "2.2", "3", "3.1", "3.1.1" };

        var readSideComputedLabels = RoundTripComputedLabels(model);

        readSideComputedLabels.Should().Equal(writeSideIntendedLabels,
            "the read-side WS-3 computation must reproduce exactly the cascade the write-side renderer authored");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. A SINGLE continuous ordered list (one numId throughout, no interruption) — the write side's
    //    "sole" numId case. Matches the corpus-proven continuity scenario (nda-interrupted-clauses.docx).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RoundTrip_SingleContinuousOrderedList_ReadSideLabelsMatchWriteSideIntent()
    {
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                ListItem(0, ordered: true, "First"),   // numId N -> "1."
                ListItem(0, ordered: true, "Second"),  // numId N -> "2."
                ListItem(0, ordered: true, "Third"),   // numId N -> "3."
            },
        };

        var writeSideIntendedLabels = new[] { "1.", "2.", "3." };

        var readSideComputedLabels = RoundTripComputedLabels(model);

        readSideComputedLabels.Should().Equal(writeSideIntendedLabels,
            "a single uninterrupted ordered run uses one numId throughout — read and write must agree");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. TWO separate ordered lists, broken by an intervening paragraph — the write side's "restart"
    //    pattern (RenderBlocks: any non-ordered-list block resets currentOrderedNumId, so the NEXT
    //    ordered item allocates a FRESH w:num instance with a level-0 w:startOverride=1, intending the
    //    second list to display "1." again). This is a routine document shape (two numbered lists
    //    separated by prose), not a contrived edge case.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RoundTrip_TwoOrderedListsSeparatedByAParagraph_ReadSideLabelsMatchWriteSideRestartIntent()
    {
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                ListItem(0, ordered: true, "First A"),                 // list 1, numId N   -> "1."
                ListItem(0, ordered: true, "First B"),                 // list 1, numId N   -> "2."
                Paragraph("Some intervening prose that breaks the ordered run."),
                ListItem(0, ordered: true, "Second A"),                // list 2, FRESH numId (write side intends restart) -> "1."
                ListItem(0, ordered: true, "Second B"),                // list 2, same fresh numId -> "2."
            },
        };

        // The write-side's INTENDED labels, per ComposeDocumentRenderer.RenderBlocks: any non-ordered-list
        // block resets currentOrderedNumId to null, so "Second A" allocates plan.NewOrderedInstance() — a
        // BRAND NEW w:num instance carrying a level-0 w:startOverride=1 (AddNumberingDefinitions). The
        // renderer's OWN authored bytes therefore intend list 2 to restart at "1.".
        var writeSideIntendedLabels = new[] { "1.", "2.", null, "1.", "2." };

        var readSideComputedLabels = RoundTripComputedLabels(model);

        readSideComputedLabels.Should().Equal(writeSideIntendedLabels,
            "the write side authors a FRESH w:num instance + startOverride=1 for the second ordered list " +
            "(intending a restart at \"1.\"); the read side's NumberingComputationEngine counter is keyed " +
            "by (abstractNumId, level) ONLY (never numId) — if the two disagree here, the read-side counter " +
            "model has forked from what the write side actually authors for this routine two-list document " +
            "shape, and must be reconciled (root CLAUDE.md §6.5 / this task's escalation trigger), not papered " +
            "over by weakening this assertion.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. Determinism (NFR-06): identical author input -> identical read-side labels across repeated runs.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RoundTrip_SameModelBuiltTwice_ProducesIdenticalComputedLabelSequences()
    {
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                Heading(1, "Definitions"),
                Heading(2, "Sub A"),
                ListItem(0, ordered: true, "Item 1"),
                ListItem(0, ordered: true, "Item 2"),
                ListItem(0, ordered: false, "Bullet 1"),
            },
        };

        // Two independent SynthesizeDocument calls (fresh paraId RNG each time) + two independent Build
        // calls — the computed LABEL sequence must be identical even though paraIds differ run-to-run.
        var run1 = RoundTripComputedLabels(model);
        var run2 = RoundTripComputedLabels(model);

        run2.Should().Equal(run1, "identical author input must produce identical computed labels (NFR-06) " +
            "regardless of paraId minting, which is randomized per SynthesizeDocument call");
    }
}
