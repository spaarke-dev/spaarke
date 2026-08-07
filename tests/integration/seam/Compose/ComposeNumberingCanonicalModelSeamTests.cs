// Task 021 (spaarkeai-compose-r6, FR-03/FR-04) — NUMBERING/LISTS THROUGH THE CANONICAL MODEL: the
// golden-label parity slice. The model carries each list item's SOURCE numbering-instance identity
// (ComposeBlock.NumId, captured by ComposeDocxProjectionBuilder.BuildContentModel); the renderer
// references the carrier's own w:num instance through it (ComposeDocumentRenderer.RenderIntoCarrier),
// so Word's per-instance counters — replayed read-side by the REUSED R4.5 NumberingComputationEngine,
// never re-implemented — reproduce the source labels exactly.
//
// THE ORACLE (task 021 acceptance; owed since the 020/011 deviation): for every §1.5 legal-numbering
// exemplar (tests/fixtures/compose-corpus/corpus-manifest.md), the computed-label SEQUENCE of the
// carrier-rendered round-trip equals the R4.5 GOLDEN Word labels — the same
// ParaIdMapEntry.ComputedNumber field the live R4.5 exactness Theory
// (ComposeReadFidelityHarnessSeamTests.NumberingExactness_...) already anchors to the manifest, so
// read-side and round-trip parity share one computation, one golden source of truth.
//
// Also proven here:
//   - numbering.xml BYTE-IDENTITY on a fully carrier-referencing round-trip (a strictly stronger
//     preserve-parts claim than ComposeCarrierRenderSeamTests' "numbering.xml may be merged" carve-out);
//   - interruption-CONTINUITY survives even a BLANK-PACKAGE render (SynthesizeDocument): distinct
//     source numIds map per-identity to allocated instances (ListRenderState), so clauses 4-6 after a
//     heading/prose/table interruption keep counting 4., 5., 6. (review finding 020-R1 closed);
//   - the projector captures NumId + first-appearance StartsNewList and no longer emits the retired
//     ordered-list-continuity-lost warning;
//   - a nested bullet inside an ordered run no longer restarts the parent list (the live-client bug the
//     020-R1 contract fix removes).
//
// NEGATIVE (ADR-038 banned patterns): NO Mock<HttpMessageHandler>, NO DI-registration test, NO
// ctor-null test anywhere in this file.

using DocumentFormat.OpenXml.Packaging;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeNumberingCanonicalModelSeamTests
{
    private readonly ComposeDocxProjectionBuilder _builder = new();
    private readonly ComposeDocumentRenderer _renderer = new();

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static string CorpusPathOf(string fileName)
    {
        var corpusDir = Path.GetDirectoryName(ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First())!;
        return Path.Combine(corpusDir, fileName);
    }

    private static byte[] LoadExemplar(string fileName) =>
        ComposeCorpusFixtureLocator.LoadVerifiedBytes(CorpusPathOf(fileName));

    /// <summary>The document-order sequence of computed numbering labels (non-numbered paragraphs
    /// excluded) — read through the REAL projection, i.e. the reused R4.5 NumberingComputationEngine.</summary>
    private IReadOnlyList<string> ComputedLabelSequence(byte[] docx) =>
        _builder.Build(docx).ParaIdMap
            .Where(e => e.ComputedNumber is not null)
            .Select(e => e.ComputedNumber!)
            .ToList();

    /// <summary>docx → canonical model (asserting the projection did not fail-close).</summary>
    private ComposeContentModel ProjectModel(byte[] docx, string docName)
    {
        var projection = _builder.BuildContentModel(docx);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed, $"'{docName}' must project into the canonical model");
        return projection.Model;
    }

    private static byte[]? NumberingPartBytes(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        var part = doc.MainDocumentPart?.NumberingDefinitionsPart;
        if (part is null)
        {
            return null;
        }
        using var stream = part.GetStream();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static ComposeBlock OrderedItem(string text, bool startsNewList = false, int? numId = null, int level = 0) => new()
    {
        Kind = ComposeBlockKind.ListItem,
        Level = level,
        Ordered = true,
        StartsNewList = startsNewList,
        NumId = numId,
        Runs = new[] { new ComposeInlineRun { Text = text } },
    };

    private static ComposeBlock BulletItem(string text, int level = 0, int? numId = null) => new()
    {
        Kind = ComposeBlockKind.ListItem,
        Level = level,
        Ordered = false,
        NumId = numId,
        Runs = new[] { new ComposeInlineRun { Text = text } },
    };

    private static ComposeBlock Prose(string text) => new()
    {
        Kind = ComposeBlockKind.Paragraph,
        Runs = new[] { new ComposeInlineRun { Text = text } },
    };

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. THE GOLDEN ORACLE — carrier round-trip label parity vs the manifest §1.5 golden sequences.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> GoldenLabelSequences()
    {
        // Full document-order golden label sequences, transcribed from corpus-manifest.md §1.5 (the
        // hand-verified Word-numbering simulation). A SEQUENCE assertion is deliberately stronger than
        // the per-paragraph R4.5 Theory: it also proves no numbered paragraph appears or disappears.
        yield return new object[]
        {
            "nda-interrupted-clauses.docx", // row 9 — interrupted single-numId run: CONTINUOUS 1..6
            new[] { "1.", "2.", "3.", "4.", "5.", "6." },
        };
        yield return new object[]
        {
            "heading-style-numbering.docx", // row 10 — style-linked Heading1/Heading2 (zero direct numPr)
            new[] { "1", "2", "3", "4", "4.1", "4.2" },
        };
        yield return new object[]
        {
            "multilevel-1-1-1.docx", // row 11 — one numId, ilvl 0/1/2, reset-on-higher-increment
            new[] { "1.", "1.1.", "1.1.1.", "1.1.2.", "1.2.", "2.", "2.1." },
        };
        yield return new object[]
        {
            "line-numbered-pleading.docx", // row 13 — 12 clauses continuous across 4 headed sections
            new[] { "1.", "2.", "3.", "4.", "5.", "6.", "7.", "8.", "9.", "10.", "11.", "12." },
        };
    }

    [Theory]
    [MemberData(nameof(GoldenLabelSequences))]
    public void CarrierRoundTrip_OnLegalNumberingExemplars_ComputedLabelSequenceMatchesGoldenWordLabels(
        string docFileName, string[] goldenSequence)
    {
        var source = LoadExemplar(docFileName);

        // Anchor: the SOURCE reads to the golden sequence (ties this oracle to the same manifest §1.5
        // ground truth the R4.5 exactness Theory pins per-paragraph).
        ComputedLabelSequence(source).Should().Equal(goldenSequence,
            $"'{docFileName}' source labels are the manifest §1.5 golden ground truth");

        // THE task-021 acceptance: load → model → render-into-carrier → reopen computes the SAME golden
        // labels — numbering/lists round-trip through the canonical model with Word-identical labels.
        var model = ProjectModel(source, docFileName);
        var rendered = _renderer.RenderIntoCarrier(source, model, author: "seam-test");

        ComputedLabelSequence(rendered).Should().Equal(goldenSequence,
            $"'{docFileName}' must keep its golden Word labels through the canonical-model round-trip " +
            "(ComposeBlock.NumId carrier-direct reference + reused NumberingComputationEngine)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. Label-sequence STABILITY across ALL §1.5 exemplars — including symbol-section-mark.docx, whose
    //    Wingdings-bullet marker deliberately has NO golden Unicode value (manifest row 12): source and
    //    rendered must agree on whatever lvlText glyph the carrier scheme defines.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("nda-interrupted-clauses.docx")]
    [InlineData("heading-style-numbering.docx")]
    [InlineData("multilevel-1-1-1.docx")]
    [InlineData("line-numbered-pleading.docx")]
    [InlineData("symbol-section-mark.docx")]
    public void CarrierRoundTrip_OnEveryNumberingExemplar_ComputedLabelSequenceIsStable(string docFileName)
    {
        var source = LoadExemplar(docFileName);
        var sourceLabels = ComputedLabelSequence(source);
        sourceLabels.Should().NotBeEmpty($"'{docFileName}' is a numbering exemplar — it must carry computed labels");

        var rendered = _renderer.RenderIntoCarrier(source, ProjectModel(source, docFileName), author: "seam-test");

        ComputedLabelSequence(rendered).Should().Equal(sourceLabels,
            $"'{docFileName}' computed-label sequence must survive the canonical-model round-trip unchanged");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. numbering.xml BYTE-IDENTITY — every exemplar's list identity is carrier-known, so the render
    //    allocates nothing and never touches the numbering part (upgrades ComposeCarrierRenderSeamTests'
    //    "numbering.xml may be merged" carve-out to full preserve-parts for the pure round-trip).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("nda-interrupted-clauses.docx")]
    [InlineData("heading-style-numbering.docx")]
    [InlineData("multilevel-1-1-1.docx")]
    [InlineData("line-numbered-pleading.docx")]
    [InlineData("symbol-section-mark.docx")]
    public void CarrierRoundTrip_OnEveryNumberingExemplar_LeavesNumberingPartByteIdentical(string docFileName)
    {
        var source = LoadExemplar(docFileName);
        var sourceNumbering = NumberingPartBytes(source);
        sourceNumbering.Should().NotBeNull($"'{docFileName}' is a §1.5 exemplar — it carries word/numbering.xml");

        var rendered = _renderer.RenderIntoCarrier(source, ProjectModel(source, docFileName), author: "seam-test");
        var renderedNumbering = NumberingPartBytes(rendered);

        renderedNumbering.Should().NotBeNull($"'{docFileName}' rendered output must keep its numbering part");
        renderedNumbering!.AsSpan().SequenceEqual(sourceNumbering!).Should().BeTrue(
            $"'{docFileName}' — every list identity is carrier-known, so the render must reference (never " +
            "merge/rewrite) word/numbering.xml; a diff means the part was touched (autoSave normalization " +
            "or an unnecessary allocation)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. Interruption-continuity WITHOUT the carrier — the identity map. A blank-package synthesize of
    //    the interrupted-clauses model must still count 1..6 continuously: all six clauses share one
    //    source numId, which maps to ONE allocated instance regardless of the heading/prose/table
    //    interruptions (review finding 020-R1 closed; the old renderer restarted at 4 -> "1.").
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SynthesizeDocument_FromInterruptedClausesModel_KeepsContinuousNumberingViaIdentityMap()
    {
        var source = LoadExemplar("nda-interrupted-clauses.docx");
        var model = ProjectModel(source, "nda-interrupted-clauses.docx");

        var rendered = _renderer.SynthesizeDocument(model, author: "seam-test");

        // The renderer's own ordered scheme is decimal "%1." — identical label shape to the source, so
        // the clause labels hold even though the carrier scheme was not available to reference. The extra
        // "1" mid-sequence is the interrupting Heading1: SynthesizeDocument's OWN style catalog numbers
        // headings style-linked BY DESIGN (FR-27 born-in-editor clause scheme) — a documented divergence
        // from carrier mode, where the carrier's unnumbered Heading1 style governs (see the carrier
        // golden Theory above, whose sequence has no heading label).
        ComputedLabelSequence(rendered).Should().Equal(new[] { "1.", "2.", "3.", "1", "4.", "5.", "6." },
            "one source numId ⇒ one allocated instance ⇒ the CLAUSE count continues 4., 5., 6. across the " +
            "heading/prose/table interruptions (the heading's own '1' is the synthesize scheme's FR-27 style-linked number)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 5. Projector capture — NumId + first-appearance StartsNewList; retired warning stays retired.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildContentModel_OnInterruptedClauses_CapturesSourceNumIdWithFirstAppearanceStartsNewList()
    {
        var projection = _builder.BuildContentModel(LoadExemplar("nda-interrupted-clauses.docx"));

        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed);
        var items = projection.Model.Blocks.Where(b => b.Kind == ComposeBlockKind.ListItem).ToList();
        items.Should().HaveCount(6, "the exemplar has 6 numbered clauses");
        items.Should().OnlyContain(i => i.Ordered && i.NumId != null,
            "every clause carries its source numbering-instance identity");
        items.Select(i => i.NumId).Distinct().Should().HaveCount(1,
            "all six clauses belong to ONE source list (one numId) — that identity IS the continuity");
        items[0].StartsNewList.Should().BeTrue("the first appearance of the numId starts the list");
        items.Skip(1).Should().OnlyContain(i => !i.StartsNewList,
            "re-appearances after the interruption are continuations, not restarts");

        projection.Warnings.Should().NotContain(w => w.Code == "ordered-list-continuity-lost",
            "task 021 retired the warning — continuity is now carried by NumId, not lost");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 6. Born-in-editor contract — a nested bullet no longer restarts the parent ordered run. The live
    //    client mapper flattens [orderedList > listItem > bulletList] into
    //    [ordered(SNL=true), bullet(level 1), ordered(SNL=false)]; the old clear-on-every-non-ordered
    //    renderer restarted the parent at 1 after the bullet — the contract fix continues it.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SynthesizeDocument_NestedBulletInsideOrderedList_DoesNotRestartTheParentList()
    {
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                OrderedItem("Parent one", startsNewList: true),
                BulletItem("Nested detail", level: 1),
                OrderedItem("Parent two"),
                Prose("Intervening prose."),
                OrderedItem("Parent three"),
            },
        };

        var labels = ComputedLabelSequence(_renderer.SynthesizeDocument(model, author: "seam-test"));

        // Sequence: "1.", <bullet marker glyph>, "2.", "3." — the prose paragraph carries no marker.
        labels.Should().HaveCount(4, "ordered + bullet items carry a computed marker; prose does not");
        labels[0].Should().Be("1.");
        labels[2].Should().Be("2.", "the nested bullet must not break the parent ordered run");
        labels[3].Should().Be("3.", "StartsNewList=false continues the run across intervening prose (020-R1 contract)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 7. Step-9.5 fix F1 — the nested-SIBLING shapes the flattened TipTap model conveys only through
    //    LEVEL transitions (the live mapper never flags nested lists StartsNewList).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SynthesizeDocument_NestedSiblingOrderedListsUnderBullets_EachRestartsAtOne()
    {
        // TipTap: bulletList > [item(+nested ol×2), item(+nested ol×2)] — flattened, the two nested
        // ordered lists are separated only by the parent bullet. Each must render as a DISTINCT list
        // restarting at 1 (matching the editor display) — under a naive no-clear contract the second
        // would continue the first's counter ("3., 4.").
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                BulletItem("Parent A"),
                OrderedItem("A sub one", level: 1),
                OrderedItem("A sub two", level: 1),
                BulletItem("Parent B"),
                OrderedItem("B sub one", level: 1),
                OrderedItem("B sub two", level: 1),
            },
        };

        var labels = ComputedLabelSequence(_renderer.SynthesizeDocument(model, author: "seam-test"));

        labels.Should().HaveCount(6);
        labels[1].Should().Be("1.");
        labels[2].Should().Be("2.");
        labels[4].Should().Be("1.", "a parent bullet closes the nested ordered run — the second nested list restarts");
        labels[5].Should().Be("2.");
    }

    [Fact]
    public void SynthesizeDocument_NestedOrderedInsideOrderedList_SharesParentInstanceAndResetsOnParentIncrement()
    {
        // TipTap: orderedList > [item(+nested ol), item(+nested ol)] — the nested items inherit the
        // parent's instance at a deeper ilvl (Word's multi-level idiom), so the parent CONTINUES after
        // the nested list ("2.") and the re-entered nested list restarts by Word's own deeper-level
        // reset ("1.") — no explicit boundary flag needed.
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                OrderedItem("Parent one", startsNewList: true),
                OrderedItem("Sub a", level: 1),
                OrderedItem("Sub b", level: 1),
                OrderedItem("Parent two"),
                OrderedItem("Sub c", level: 1),
            },
        };

        var labels = ComputedLabelSequence(_renderer.SynthesizeDocument(model, author: "seam-test"));

        labels.Should().Equal(new[] { "1.", "1.", "2.", "2.", "1." },
            "nested ordered items share the parent instance at deeper ilvl; the parent increment resets the deeper counter");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 8. Step-9.5 fix F2 — foreign-carrier kind guard: a source NumId that coincidentally exists in the
    //    carrier but classifies as the WRONG KIND at the item's level must fall back to allocation, not
    //    bind the item to a scheme that would render a glyph where the source showed a number (or vice
    //    versa).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RenderIntoCarrier_CoincidentNumIdOfWrongKind_FallsBackToAllocationInsteadOfBindingTheWrongScheme()
    {
        // multilevel-1-1-1.docx's single instance is ORDERED (decimal at every level). A BULLET item
        // carrying that same numId is a kind mismatch — it must NOT reference the carrier instance.
        var carrier = LoadExemplar("multilevel-1-1-1.docx");
        int carrierNumId;
        using (var doc = WordprocessingDocument.Open(new MemoryStream(carrier, writable: false), isEditable: false))
        {
            carrierNumId = doc.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!
                .Elements<DocumentFormat.OpenXml.Wordprocessing.NumberingInstance>()
                .First().NumberID!.Value;
        }

        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                BulletItem("wrong-kind bullet", numId: carrierNumId),
                OrderedItem("right-kind ordered", numId: carrierNumId),
            },
        };

        var rendered = _renderer.RenderIntoCarrier(carrier, model, author: "seam-test");

        using var renderedDoc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var renderedNumIds = renderedDoc.MainDocumentPart!.Document!.Body!
            .Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
            .Select(p => p.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value)
            .Where(id => id is not null)
            .ToList();

        renderedNumIds.Should().HaveCount(2);
        renderedNumIds[0].Should().NotBe(carrierNumId,
            "a bullet item must not bind an ordered carrier scheme — the kind guard falls back to the renderer's bullet instance");
        renderedNumIds[0].Should().BeGreaterThan(carrierNumId, "fallback instances allocate above the carrier's max numId");
        renderedNumIds[1].Should().Be(carrierNumId, "the kind-compatible ordered item still references the carrier instance directly");
    }
}
