using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="AnnotationReanchorService"/> (FR-27, task 054) — the deterministic
/// RE-ANCHORING engine that scores prior Compose anchors against a reloaded Word document into
/// auto/review/orphan bands (+ the Spike-6 ambiguity guard).
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c> — the scoring surface is a pure
/// (anchors + paragraph strings) → summary transform with no I/O and no collaborators to mock, so
/// band-boundary + ambiguity cases are driven with plain strings (mirroring Spike 6's own scenario
/// harness). The Redis persistence surface is covered with a real <see cref="MemoryDistributedCache"/>
/// (same pattern as <c>SpeSyncOrchestratorTests</c>) — no <c>Mock&lt;HttpMessageHandler&gt;</c>, no
/// DI, no transport mocks.
/// </para>
/// </summary>
public class AnnotationReanchorServiceTests
{
    private static readonly DateTimeOffset When = new(2026, 7, 9, 15, 42, 0, TimeSpan.Zero);

    // Spike 6's base "services agreement" paragraphs — the re-anchor scoring corpus.
    private static readonly string[] BaseParagraphs =
    {
        "This Master Services Agreement (the \"Agreement\") is entered into between the parties identified in the signature block.",
        "Indemnification is capped at fees paid by Client in the twelve months preceding the claim, except for breaches of confidentiality.",
        "Each Party shall perform its obligations under this Agreement in a professional and workmanlike manner consistent with industry standards.",
        "Termination of this Agreement requires thirty (30) days written notice delivered to the other Party's designated contact.",
        "Confidential Information disclosed by either Party under this Agreement shall not be shared with third parties absent written consent.",
    };

    private const string IndemnificationAnchor =
        "Indemnification is capped at fees paid by Client in the twelve months preceding the claim, except for breaches of confidentiality.";
    private const string ProfessionalMannerAnchor =
        "Each Party shall perform its obligations under this Agreement in a professional and workmanlike manner consistent with industry standards.";
    private const string TerminationAnchor =
        "Termination of this Agreement requires thirty (30) days written notice delivered to the other Party's designated contact.";

    // -- Band boundary: AUTO (identical text, same position) -------------------------------------

    [Fact]
    public void Reanchor_IdenticalTextSamePosition_IsAutoBandWithFullConfidence()
    {
        var anchors = new[] { new PriorAnchor("a1", "comment", IndemnificationAnchor, ParagraphHint: 1) };

        var summary = AnnotationReanchorService.Reanchor(anchors, BaseParagraphs, "spe-1", When);

        var result = summary.Annotations.Should().ContainSingle().Subject;
        result.Band.Should().Be(ReanchorBand.Auto);
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReanchorBands.AutoThreshold);
        result.MatchedParagraphIndex.Should().Be(1);
        result.Ambiguous.Should().BeFalse();
        result.MatchedParagraphPreview.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Reanchor_IdenticalTextReflowedOneParagraph_StaysAuto()
    {
        // Spike 6 scenario C: a paragraph inserted above shifts the anchor down one position.
        // The 0.75/0.25 weighting must NOT drag an otherwise-perfect content match below AUTO.
        var current = new List<string> { "Preamble: illustration only; creates no binding obligation." };
        current.AddRange(BaseParagraphs);
        var anchors = new[] { new PriorAnchor("a1", "comment", ProfessionalMannerAnchor, ParagraphHint: 2) };

        var summary = AnnotationReanchorService.Reanchor(anchors, current, "spe-1", When);

        summary.Annotations.Single().Band.Should().Be(ReanchorBand.Auto);
    }

    // -- Band boundary: REVIEW (partial rewrite ~1/3 words changed) ------------------------------

    [Fact]
    public void Reanchor_PartialRewrite_IsReviewBand()
    {
        // Spike 6 scenario D (~0.81 combined): the clause's tail is deleted — meaningful but
        // recoverable drift → flag for review, not silent keep, not orphan.
        var current = (string[])BaseParagraphs.Clone();
        current[2] = "Each Party shall perform its obligations under this Agreement in a professional and workmanlike manner.";
        var anchors = new[] { new PriorAnchor("a1", "insertion-suggestion", ProfessionalMannerAnchor, ParagraphHint: 2) };

        var result = AnnotationReanchorService.Reanchor(anchors, current, "spe-1", When).Annotations.Single();

        result.Band.Should().Be(ReanchorBand.Review);
        result.Confidence.Should().BeInRange(ReanchorBands.ReviewThreshold, ReanchorBands.AutoThreshold);
        // A flagged anchor still carries where it best matched, for the resolution UX.
        result.MatchedParagraphIndex.Should().Be(2);
    }

    // -- Band boundary: ORPHAN (heavy rewrite) ---------------------------------------------------

    [Fact]
    public void Reanchor_HeavyRewrite_IsOrphanBandAndSurfaced()
    {
        // Spike 6 scenario E (~0.48 combined): the sentence is functionally gone. Orphan — never
        // silently mis-anchored to a loosely-related sentence.
        var current = (string[])BaseParagraphs.Clone();
        current[2] = "Both parties agree to act in good faith and to promptly escalate any performance concerns to the account manager.";
        var anchors = new[] { new PriorAnchor("a1", "comment", ProfessionalMannerAnchor, ParagraphHint: 2) };

        var result = AnnotationReanchorService.Reanchor(anchors, current, "spe-1", When).Annotations.Single();

        result.Band.Should().Be(ReanchorBand.Orphan);
        result.Confidence.Should().BeLessThan(ReanchorBands.ReviewThreshold);
        result.MatchedParagraphIndex.Should().Be(-1);
    }

    // -- Negative: no viable match anywhere → orphan, NEVER dropped (FR-27) -----------------------

    [Fact]
    public void Reanchor_NoViableMatch_OrphansAnchorButKeepsItInSummary()
    {
        // The anchor's paragraph was deleted and nothing similar remains — the document is now a
        // completely different, short text. The anchor MUST be surfaced as an orphan, not dropped.
        var current = new[] { "Unrelated single line.", "Another unrelated line." };
        var anchors = new[] { new PriorAnchor("orphan-1", "comment", ProfessionalMannerAnchor, ParagraphHint: 2, Preview: "Reviewer note text") };

        var summary = AnnotationReanchorService.Reanchor(anchors, current, "spe-1", When);

        summary.OrphanCount.Should().Be(1);
        var result = summary.Annotations.Should().ContainSingle().Subject; // present, not silently dropped
        result.Id.Should().Be("orphan-1");
        result.Band.Should().Be(ReanchorBand.Orphan);
        result.MatchedParagraphIndex.Should().Be(-1);
        result.Preview.Should().Be("Reviewer note text"); // resolution context preserved
    }

    // -- Spike 6 §5.3 ambiguity guard: near-tie demotes would-be AUTO to REVIEW ------------------

    [Fact]
    public void Reanchor_ByteIdenticalDuplicate_DemotesAutoToReviewAndFlagsAmbiguous()
    {
        // Spike 6 scenario G: a byte-identical clause now appears at TWO positions. The naive band
        // math scores a perfect 1.0 and would AUTO-anchor to whichever the scan found first — a
        // coin flip. The 4th rule must flag it ambiguous and demote AUTO → REVIEW.
        var current = new List<string>(BaseParagraphs);
        current.Insert(5, TerminationAnchor); // exact duplicate of paragraph index 3
        var anchors = new[] { new PriorAnchor("a1", "comment", TerminationAnchor, ParagraphHint: 3) };

        var result = AnnotationReanchorService.Reanchor(anchors, current, "spe-1", When).Annotations.Single();

        result.Ambiguous.Should().BeTrue();
        result.Band.Should().Be(ReanchorBand.Review); // NOT Auto — the guard demoted it
    }

    [Fact]
    public void Reanchor_NearDuplicateWithLargeMargin_StaysAutoAndUnambiguous()
    {
        // Spike 6 scenario H (control): the TRUE original is untouched (sim 1.0) and a near-duplicate
        // (a restated clause with an appended phrase, sim well below 0.95) sits elsewhere. The margin
        // between best and second-best is large → NOT ambiguous → stays AUTO.
        var current = new List<string>(BaseParagraphs);
        current.Insert(5, TerminationAnchor + " (restated for the renewal term with additional notice provisions and delivery terms).");
        var anchors = new[] { new PriorAnchor("a1", "comment", TerminationAnchor, ParagraphHint: 3) };

        var result = AnnotationReanchorService.Reanchor(anchors, current, "spe-1", When).Annotations.Single();

        result.Ambiguous.Should().BeFalse();
        result.Band.Should().Be(ReanchorBand.Auto);
    }

    // -- Summary counts across a mixed batch -----------------------------------------------------

    [Fact]
    public void Reanchor_MixedBatch_ReportsCorrectPerBandCounts()
    {
        var current = (string[])BaseParagraphs.Clone();
        current[2] = "Each Party shall perform its obligations under this Agreement in a professional and workmanlike manner."; // review (partial)
        var anchors = new[]
        {
            new PriorAnchor("auto-1", "comment", IndemnificationAnchor, ParagraphHint: 1),      // AUTO (unchanged)
            new PriorAnchor("review-1", "insertion-suggestion", ProfessionalMannerAnchor, 2),    // REVIEW (partial rewrite)
            new PriorAnchor("orphan-1", "comment", "A clause that never existed in this document at all.", 4), // ORPHAN
        };

        var summary = AnnotationReanchorService.Reanchor(anchors, current, "spe-1", When);

        summary.Total.Should().Be(3);
        summary.AutoCount.Should().Be(1);
        summary.ReviewCount.Should().Be(1);
        summary.OrphanCount.Should().Be(1);
        (summary.AutoCount + summary.ReviewCount + summary.OrphanCount).Should().Be(summary.Total);
    }

    [Fact]
    public void Reanchor_EmptyAnchorList_ReturnsAllZeroSummary_NotError()
    {
        var summary = AnnotationReanchorService.Reanchor(Array.Empty<PriorAnchor>(), BaseParagraphs, "spe-1", When);

        summary.Total.Should().Be(0);
        summary.AutoCount.Should().Be(0);
        summary.ReviewCount.Should().Be(0);
        summary.OrphanCount.Should().Be(0);
        summary.Annotations.Should().BeEmpty();
        summary.ComputedAtUtc.Should().Be(When);
    }

    // -- Paragraph extraction from real DOCX bytes -----------------------------------------------

    [Fact]
    public void ExtractParagraphTexts_RealDocx_ReturnsSettledParagraphTextInOrder()
    {
        var docx = CreateDocx(BaseParagraphs);

        var paragraphs = AnnotationReanchorService.ExtractParagraphTexts(docx);

        paragraphs.Should().HaveCount(BaseParagraphs.Length);
        paragraphs[0].Should().Be(BaseParagraphs[0]);
        paragraphs[3].Should().Be(BaseParagraphs[3]);
    }

    [Fact]
    public void ExtractParagraphTexts_MalformedBytes_ThrowsDocxAnnotationException()
    {
        var notADocx = new byte[] { 1, 2, 3, 4, 5 };

        var act = () => AnnotationReanchorService.ExtractParagraphTexts(notADocx);

        act.Should().Throw<DocxAnnotationException>()
            .Which.Kind.Should().Be(DocxAnnotationErrorKind.MalformedDocument);
    }

    // -- Redis persistence (ADR-009) round-trip + end-to-end compute+persist ---------------------

    [Fact]
    public async Task SaveThenGetSummaryAsync_RoundTripsThroughDistributedCache()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var service = new AnnotationReanchorService(cache);
        var summary = AnnotationReanchorService.Reanchor(
            new[] { new PriorAnchor("a1", "comment", IndemnificationAnchor, 1) },
            BaseParagraphs, "spe-42", When);

        await service.SaveSummaryAsync("spe-42", summary);
        var loaded = await service.GetSummaryAsync("spe-42");

        loaded.Should().NotBeNull();
        loaded!.DocumentSpeId.Should().Be("spe-42");
        loaded.Total.Should().Be(1);
        loaded.Annotations.Single().Band.Should().Be(ReanchorBand.Auto);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenNoneStored_ReturnsNull()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var service = new AnnotationReanchorService(cache);

        var loaded = await service.GetSummaryAsync("never-stored");

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task ComputeAndPersistAsync_ScoresRealDocxAndPersistsSummary()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var service = new AnnotationReanchorService(cache);
        var docx = CreateDocx(BaseParagraphs);
        var anchors = new[] { new PriorAnchor("a1", "comment", IndemnificationAnchor, 1) };

        var summary = await service.ComputeAndPersistAsync("spe-99", anchors, docx);

        summary.Total.Should().Be(1);
        summary.Annotations.Single().Band.Should().Be(ReanchorBand.Auto);

        // Persisted as a side effect — a later reader (banner on return) sees it.
        var loaded = await service.GetSummaryAsync("spe-99");
        loaded.Should().NotBeNull();
        loaded!.AutoCount.Should().Be(1);
    }

    // -- FR-11 paraId-primary anchoring (task 012) -----------------------------------------------

    // paraIds aligned 1:1 with BaseParagraphs (index i → BaseParaIds[i]).
    private static readonly string?[] BaseParaIds = { "0000000A", "0000000B", "0000000C", "0000000D", "0000000E" };

    [Fact]
    public void Reanchor_AnchorParaIdMatchesCurrentParagraph_ResolvesByParaIdOverContent()
    {
        // FR-11 acceptance (a): within our own round-trip the paraId is the PRIMARY anchor. The anchor's
        // paraId points at paragraph index 2, but its textPattern is byte-identical to paragraph 0 — so
        // ONLY paraId-primary lands it at 2. If the fuzzy scorer ran, it would (wrongly) pick 0.
        var anchors = new[]
        {
            new PriorAnchor("a1", "comment", BaseParagraphs[0], ParagraphHint: 0, Preview: null, ParaId: "0000000C"),
        };

        var result = AnnotationReanchorService
            .Reanchor(anchors, BaseParagraphs, "spe-1", When, BaseParaIds)
            .Annotations.Single();

        result.MatchedParagraphIndex.Should().Be(2, "the anchor's paraId identifies paragraph 2 — paraId beats the content match at 0");
        result.Band.Should().Be(ReanchorBand.Auto);
        result.Confidence.Should().Be(1.0, "an exact paraId hit is a definitive re-anchor");
        result.Ambiguous.Should().BeFalse();
    }

    [Fact]
    public void Reanchor_AnchorParaIdMatchesEvenWhenParagraphContentDrifted_StaysDefinitiveAuto()
    {
        // The paragraph the paraId points at was EDITED (content drift) but is still the same paragraph.
        // paraId-primary must re-anchor it definitively regardless of the content change (design §5.2).
        var current = (string[])BaseParagraphs.Clone();
        current[3] = "Termination now requires SIXTY (60) days written notice — substantially reworded during editing.";
        var anchors = new[]
        {
            new PriorAnchor("a1", "insertion-suggestion", TerminationAnchor, ParagraphHint: 3, Preview: null, ParaId: "0000000D"),
        };

        var result = AnnotationReanchorService
            .Reanchor(anchors, current, "spe-1", When, BaseParaIds)
            .Annotations.Single();

        result.MatchedParagraphIndex.Should().Be(3);
        result.Band.Should().Be(ReanchorBand.Auto);
        result.Confidence.Should().Be(1.0);
    }

    [Fact]
    public void Reanchor_AnchorParaIdAbsentFromDocument_FallsBackToFuzzyMatcher()
    {
        // FR-11 acceptance (b) / design §5.2: an EXTERNAL Word edit regenerated all paraIds (Open-XML-SDK
        // #925), so the anchor's paraId is nowhere in the current doc. The SAME anchor must re-anchor via
        // the RETAINED fuzzy matcher — landing on the paragraph its textPattern matches (index 1), NOT
        // orphaned. Regenerated current ids share nothing with the anchor's "0000000C".
        var regeneratedIds = new string?[] { "FFFF0001", "FFFF0002", "FFFF0003", "FFFF0004", "FFFF0005" };
        var anchors = new[]
        {
            new PriorAnchor("a1", "comment", IndemnificationAnchor, ParagraphHint: 1, Preview: null, ParaId: "0000000C"),
        };

        var result = AnnotationReanchorService
            .Reanchor(anchors, BaseParagraphs, "spe-1", When, regeneratedIds)
            .Annotations.Single();

        result.MatchedParagraphIndex.Should().Be(1, "the paraId is gone, so the retained textPattern+Levenshtein scorer re-anchors by content");
        result.Band.Should().Be(ReanchorBand.Auto);
    }

    [Fact]
    public void Reanchor_AnchorCarriesParaIdButNoIdMapProvided_UsesFuzzyPath()
    {
        // Additive/back-compat: a caller that never extracted the id map (currentParaIds omitted → null)
        // gets the pre-FR-11 behavior — the paraId is simply ignored and the fuzzy scorer runs. Proves
        // the paraId field does not change existing consumers' results.
        var anchors = new[]
        {
            new PriorAnchor("a1", "comment", IndemnificationAnchor, ParagraphHint: 1, Preview: null, ParaId: "0000000C"),
        };

        var result = AnnotationReanchorService.Reanchor(anchors, BaseParagraphs, "spe-1", When).Annotations.Single();

        result.MatchedParagraphIndex.Should().Be(1, "no id map → fuzzy content match, as before FR-11");
        result.Band.Should().Be(ReanchorBand.Auto);
    }

    [Fact]
    public void ExtractParaIds_RealDocxWithParaIds_ReturnsUppercasedIdsAlignedWithText()
    {
        // The id list must be index-aligned with ExtractParagraphTexts (same document-order walk) and
        // upper-cased; a paragraph without an id yields null at its slot.
        var docx = CreateDocxWithParaIds(
            (BaseParagraphs[0], "aaaa0001"),   // lowercase source id — must come back upper-cased
            (BaseParagraphs[1], null),          // no id → null slot
            (BaseParagraphs[2], "CCCC0003"));

        var ids = AnnotationReanchorService.ExtractParaIds(docx);
        var texts = AnnotationReanchorService.ExtractParagraphTexts(docx);

        ids.Should().HaveCount(3).And.HaveSameCount(texts);
        ids[0].Should().Be("AAAA0001");
        ids[1].Should().BeNull();
        ids[2].Should().Be("CCCC0003");
        texts[2].Should().Be(BaseParagraphs[2], "text and ids come from the same aligned walk");
    }

    [Fact]
    public async Task ComputeAndPersistAsync_WhenParaIdMatchesButContentDiffers_ReanchorsByParaIdDefinitively()
    {
        // End-to-end through the docx path: ComputeAndPersistAsync must extract the id map and resolve by
        // paraId. The anchor's textPattern matches NOTHING in the doc (would orphan on content alone), but
        // its paraId identifies paragraph 3 — so paraId-primary re-anchors it AUTO at 3.
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var service = new AnnotationReanchorService(cache);
        var docx = CreateDocxWithParaIds(
            (BaseParagraphs[0], "0000000A"),
            (BaseParagraphs[1], "0000000B"),
            (BaseParagraphs[2], "0000000C"),
            (BaseParagraphs[3], "0000000D"));
        var anchors = new[]
        {
            new PriorAnchor("a1", "comment", "A clause that appears nowhere in this document.", ParagraphHint: 9, Preview: null, ParaId: "0000000D"),
        };

        var summary = await service.ComputeAndPersistAsync("spe-paraid", anchors, docx);

        var result = summary.Annotations.Single();
        result.MatchedParagraphIndex.Should().Be(3, "the paraId identifies paragraph 3 even though the textPattern matches nothing");
        result.Band.Should().Be(ReanchorBand.Auto);
        result.Confidence.Should().Be(1.0);
        summary.AutoCount.Should().Be(1);
    }

    // -- Helpers ---------------------------------------------------------------------------------

    private static byte[] CreateDocxWithParaIds(params (string Text, string? ParaId)[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            foreach (var (text, paraId) in paragraphs)
            {
                var p = new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
                if (paraId is not null)
                {
                    p.ParagraphId = new HexBinaryValue(paraId);
                }
                body.AppendChild(p);
            }
            body.AppendChild(new SectionProperties());
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static byte[] CreateDocx(IReadOnlyList<string> paragraphs)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            foreach (var text in paragraphs)
            {
                body.AppendChild(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
            }
            body.AppendChild(new SectionProperties());
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }
}
