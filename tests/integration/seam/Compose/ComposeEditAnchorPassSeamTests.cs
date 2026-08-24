// Task 051 (spaarkeai-compose-r8, FR-C01/C02/C03) — the ANCHOR-FIRST edit-resolution seam slice.
//
// The defect this pins: an AI edit used to name its target in PROSE (`target_text` + `match_mode`) and the
// apply path searched the whole document for the model's echoed wording — deterministic information that
// was available at capture time, re-derived by search (project invariant 7). These tests prove the
// replacement is genuinely deterministic, in BOTH directions:
//
//   POSITIVE — a paraId anchor (FR-C01/C03) and a legal citation (FR-C02) each resolve to the exact
//   paragraph of a REAL corpus document, through the REAL numbering engine's computed ParaIdMap.
//
//   NEGATIVE — the assertion that actually matters, and the one a shape-only test cannot make: the text
//   search is NEVER INVOKED for an anchored edit. This is enforced structurally, not by inspecting output:
//   the pass is handed a text validator that THROWS if called, so an anchored edit that leaked into the
//   legacy leg fails the test loudly instead of quietly returning a plausible-looking span. The
//   un-anchored case then proves the same validator IS still reached, so the negative is a real
//   constraint rather than an unreachable branch.
//
// The seam: corpus bytes -> ComposeDocxProjectionBuilder -> real ParaIdMap (closed set + numbering)
//           -> ProposedEdit envelope -> ComposeEditAnchorPass -> EditVerdict.ResolvedParaId.
//
// KEEP-path classification (ADR-038 §"vertical-slice-seam"): tests/integration/seam/**. Drives the REAL
// projection builder over REAL corpus fixtures + the REAL CitationResolver/ComposeAnchorResolver. The one
// hand-written collaborator is a first-party IComposeEditValidator fake used as a TRIPWIRE, not as a
// stand-in for behavior under test — none of the ADR-038 banned shapes (Mock<HttpMessageHandler>,
// DI-registration, ctor-null) appear here.

using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeEditAnchorPassSeamTests
{
    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // Fixtures
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The tripwire. Any call means an edit that carried a deterministic anchor still fell through to the
    /// text-search leg — the exact regression FR-C01/C02 exists to prevent, so it fails the test rather
    /// than returning something plausible.
    /// </summary>
    private sealed class ThrowIfTextSearched : IComposeEditValidator
    {
        public BatchValidationResult Validate(string documentText, IReadOnlyList<ProposedEdit> edits)
            => throw new InvalidOperationException(
                $"Text search was invoked for {edits.Count} edit(s) that carried a deterministic anchor. "
                + "An anchored edit must resolve through the reference map and never reach target_text.");
    }

    /// <summary>Records that the legacy leg WAS reached, so the un-anchored path can be proven live.</summary>
    private sealed class RecordingTextValidator : IComposeEditValidator
    {
        private readonly ComposeEditValidator _real = new();
        public List<ProposedEdit> Seen { get; } = new();

        public BatchValidationResult Validate(string documentText, IReadOnlyList<ProposedEdit> edits)
        {
            Seen.AddRange(edits);
            return _real.Validate(documentText, edits);
        }
    }

    private static ComposeDocxProjection ProjectCorpus(string fileName)
    {
        var corpusDir = Path.GetDirectoryName(ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First())!;
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(Path.Combine(corpusDir, fileName));
        return new ComposeDocxProjectionBuilder().Build(bytes);
    }

    private static ProposedEdit Edit(
        string newText = "REPLACEMENT",
        string targetText = "",
        string? paraId = null,
        string? reference = null)
        => new(targetText, newText, MatchMode.Strict, TargetParaId: paraId, TargetRef: reference);

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // FR-C01/C03 — an explicit paraId anchor resolves, with zero text matching
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParaIdAnchor_ResolvesToThatParagraph_WithoutInvokingTextSearch()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");
        var target = projection.ParaIdMap.Single(e => e.Index == 9).ParaId;

        var result = ComposeEditAnchorPass.Validate(
            documentText: "irrelevant — an anchored edit must never read this",
            edits: new[] { Edit(paraId: target) },
            referenceMap: projection.ParaIdMap,
            textValidator: new ThrowIfTextSearched());

        result.IsValid.Should().BeTrue();
        result.Verdicts.Should().ContainSingle()
            .Which.ResolvedParaId.Should().Be(target, "the anchor names the paragraph outright");
        result.Verdicts[0].Matches.Should().BeEmpty(
            "an anchored edit is addressed by paraId; there is no text span to report");
    }

    [Fact]
    public void ParaIdAnchor_IsCaseInsensitive_AndEchoesTheReferenceMapsOwnSpelling()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");
        var canonical = projection.ParaIdMap.Single(e => e.Index == 9).ParaId;

        var result = ComposeEditAnchorPass.Validate(
            "irrelevant",
            new[] { Edit(paraId: canonical.ToLowerInvariant()) },
            projection.ParaIdMap,
            new ThrowIfTextSearched());

        // Downstream paraId comparisons are ordinal, so echoing the caller's casing would silently fail
        // to match later. The set's own spelling is what comes back.
        result.Verdicts[0].ResolvedParaId.Should().Be(canonical);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // FR-C02 — a legal citation resolves through CitationResolver + the numbering engine
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Section 4.2")]
    [InlineData("clause 4.2")]
    [InlineData("§ 4.2")]
    [InlineData("4.2")]
    public void CitationAnchor_ResolvesThroughTheNumberingEngine_WithoutInvokingTextSearch(string citation)
    {
        // heading-style-numbering.docx ordinal 9 carries computed label "4.2" / ListPath [4,2] — the same
        // corpus row ComposeCitationResolverSeamTests pins. Nothing here re-derives the number; the
        // projection computed it, and the citation is matched against that.
        var projection = ProjectCorpus("heading-style-numbering.docx");
        var expected = projection.ParaIdMap.Single(e => e.Index == 9).ParaId;

        var result = ComposeEditAnchorPass.Validate(
            "irrelevant — a citation-anchored edit must never read this",
            new[] { Edit(reference: citation) },
            projection.ParaIdMap,
            new ThrowIfTextSearched());

        result.IsValid.Should().BeTrue();
        result.Verdicts[0].ResolvedParaId.Should().Be(expected);
    }

    [Fact]
    public void CitationNamingSeveralClauses_IsRefused_NotNarrowedToTheFirst()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");

        var result = ComposeEditAnchorPass.Validate(
            "irrelevant",
            new[] { Edit(reference: "Sections 1-9") },
            projection.ParaIdMap,
            new ThrowIfTextSearched());

        // Picking matches[0] would be exactly the silently-wrong-target failure this task removes.
        result.IsValid.Should().BeFalse();
        result.Verdicts[0].Error!.Kind.Should().Be(EditErrorKind.AmbiguousReference);
        result.Verdicts[0].ResolvedParaId.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // FR-C03 — an id outside the closed set is rejected LOUDLY, never repaired or searched for
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParaIdOutsideTheClosedSet_IsRejectedLoudly_AndNeverFallsBackToSearching()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");

        var result = ComposeEditAnchorPass.Validate(
            "irrelevant",
            new[] { Edit(paraId: "DEADBEEF") },
            projection.ParaIdMap,
            new ThrowIfTextSearched()); // reaching the text leg on a bad anchor would throw

        result.IsValid.Should().BeFalse();
        var verdict = result.Verdicts.Should().ContainSingle().Subject;
        verdict.ResolvedParaId.Should().BeNull();
        verdict.Error!.Kind.Should().Be(EditErrorKind.UnknownParaId);
        verdict.Error.MatchCount.Should().Be(0);
        verdict.Error.Examples.Should().BeEmpty("an anchor refusal has no occurrences to show; inventing 'nearest' ones would be guessing");
    }

    [Fact]
    public void UnresolvableCitation_IsRejected_NotDegradedToATextSearch()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");

        var result = ComposeEditAnchorPass.Validate(
            "irrelevant",
            new[] { Edit(reference: "Section 4127") },
            projection.ParaIdMap,
            new ThrowIfTextSearched());

        result.Verdicts[0].Error!.Kind.Should().Be(EditErrorKind.UnresolvedReference);
    }

    [Fact]
    public void AnchorWithNoReferenceMap_IsRefused_RatherThanSilentlyTextSearched()
    {
        // The dangerous degradation: an anchored edit arriving without a map, quietly falling through to
        // the very search path the anchor exists to replace. It is refused instead.
        var result = ComposeEditAnchorPass.Validate(
            "irrelevant",
            new[] { Edit(paraId: "AAAA1111") },
            referenceMap: null,
            new ThrowIfTextSearched());

        result.Verdicts[0].Error!.Kind.Should().Be(EditErrorKind.NoReferenceMap);
    }

    [Fact]
    public void TwoAnchorsNamingDifferentParagraphs_AreRefused_WithNeitherPreferred()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");
        var otherParaId = projection.ParaIdMap.Single(e => e.Index == 0).ParaId;

        var result = ComposeEditAnchorPass.Validate(
            "irrelevant",
            new[] { Edit(paraId: otherParaId, reference: "Section 4.2") },
            projection.ParaIdMap,
            new ThrowIfTextSearched());

        result.Verdicts[0].Error!.Kind.Should().Be(EditErrorKind.ConflictingAnchors);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // The legacy leg is still REACHABLE (so the negatives above are a real constraint), and mixed
    // batches keep their verdict indices — task 052 retires this leg, task 051 must not break it.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnanchoredEdit_StillReachesTheTextValidator()
    {
        var recorder = new RecordingTextValidator();

        var result = ComposeEditAnchorPass.Validate(
            "The Receiving Party shall keep the information confidential.",
            new[] { Edit(targetText: "confidential") },
            referenceMap: null,
            recorder);

        recorder.Seen.Should().ContainSingle("nothing anchored it, so the legacy path is still the only answer");
        result.IsValid.Should().BeTrue();
        result.Verdicts[0].ResolvedParaId.Should().BeNull();
    }

    [Fact]
    public void MixedBatch_SendsOnlyUnanchoredEditsToTheTextLeg_AndKeepsRequestOrderIndices()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");
        var target = projection.ParaIdMap.Single(e => e.Index == 9).ParaId;
        var recorder = new RecordingTextValidator();

        var result = ComposeEditAnchorPass.Validate(
            "The Receiving Party shall keep the information confidential.",
            new[]
            {
                Edit(newText: "A", targetText: "confidential"), // 0 — legacy
                Edit(newText: "B", paraId: target),             // 1 — anchored
                Edit(newText: "C", reference: "Section 4.2"),   // 2 — anchored
                Edit(newText: "D", targetText: "Receiving"),    // 3 — legacy
            },
            projection.ParaIdMap,
            recorder);

        recorder.Seen.Select(e => e.NewText).Should().Equal(
            new[] { "A", "D" }, "only the un-anchored edits may reach the text leg");

        // The validator numbered its own subset 0..1; those verdicts must come back re-keyed onto the
        // caller's positions, or "Edit N failed" would name the wrong edit.
        result.Verdicts.Select(v => v.EditIndex).Should().Equal(0, 1, 2, 3);
        result.Verdicts[1].ResolvedParaId.Should().Be(target);
        result.Verdicts[2].ResolvedParaId.Should().Be(target);
        result.Verdicts[0].ResolvedParaId.Should().BeNull();
        result.Verdicts[3].ResolvedParaId.Should().BeNull();
    }
}
