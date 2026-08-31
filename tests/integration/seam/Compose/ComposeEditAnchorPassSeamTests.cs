// Tasks 051 + 052 (spaarkeai-compose-r8, FR-C01/C02/C03/C04) — the ANCHOR-ONLY edit-resolution seam slice.
//
// The defect this pins: an AI edit used to name its target in PROSE (`target_text` + `match_mode`) and the
// apply path searched the whole document for the model's echoed wording — deterministic information that
// was available at capture time, re-derived by search (project invariant 7). These tests prove the
// replacement is genuinely deterministic, in BOTH directions:
//
//   POSITIVE — a paraId anchor (FR-C01/C03) and a legal citation (FR-C02) each resolve to the exact
//   paragraph of a REAL corpus document, through the REAL numbering engine's computed ParaIdMap.
//
//   NEGATIVE — the assertion that actually matters: an edit is NEVER placed by matching document prose.
//
// HOW THE NEGATIVE IS ENFORCED NOW (task 052 changed this, and it is a strengthening). Under task 051 the
// text leg still existed, so the guarantee needed a runtime tripwire: the pass was handed an
// `IComposeEditValidator` fake that THREW if called. Task 052 DELETED that leg, the validator, and the
// interface — so the tripwire is now the TYPE SYSTEM. `ComposeEditAnchorPass.Validate` accepts only
// (edits, referenceMap): it receives no document text and no text-searching collaborator, so there is
// nothing here that COULD search prose. The `ThrowIfTextSearched` / `RecordingTextValidator` fakes are gone
// because they are literally un-writable — there is no interface left to implement.
//
// What replaces them, so the negative stays a real constraint rather than an unenforced claim:
//   1. `AnchorPass_Signature_CannotSeeDocumentText` — reflection over the public signature. It fails if a
//      future change re-admits a document-text or text-validator parameter, which is the only way the
//      search could come back through this door.
//   2. The un-anchored case (`EditWithNoAnchor_...`) asserts the POSITIVE outcome of the removal: a
//      deterministic `NoAnchor` refusal naming the anchors the edit should have supplied — not a search,
//      and not a silent pass.
//   3. `VerdictAndRefusalShapes_CannotExpressATextSpan` (task 064) — the OUTPUT half of (1). See below.
//
// TASK 064 (owner decision 2026-08-25) UPDATE, and it cuts BOTH ways. The offset vocabulary this pass
// returned empty (`EditVerdict.Matches`, `EditValidationError.MatchCount`/`.Examples`, and the
// `ResolvedMatch`/`MatchExample` types themselves) was deleted with the text-OFFSET apply half
// (`ComposeEditBatch`/`ComposeEditTransaction`) it existed to feed. The three per-test "…and no span was
// reported" assertions that used to live here are therefore gone, replaced by ONE structural assertion on
// the shapes — a strengthening for the same reason (1) was: an empty-collection assertion only covers the
// paths a test happens to exercise, while an absent member covers every path at once.
//
// AND THE CAVEAT THAT MUST NOT BE LOST: task 064 also retired `POST /api/compose/edit-batch/validate`,
// which was this pass's ONLY production caller. These tests are now its only exercise. The pass was kept
// deliberately — the ADR-043/041 assessment (§7, C-7) designates it the Compose-owned home for closed-set
// validation — so this file is holding a designated-but-currently-unwired component honest, not a live
// request path. See projects/spaarkeai-compose-r8/notes/064-orphan-retirement-decisions.md §4.
//
// The seam: corpus bytes -> ComposeDocxProjectionBuilder -> real ParaIdMap (closed set + numbering)
//           -> ProposedEdit envelope -> ComposeEditAnchorPass -> EditVerdict.ResolvedParaId.
//
// KEEP-path classification (ADR-038 §"vertical-slice-seam"): tests/integration/seam/**. Drives the REAL
// projection builder over REAL corpus fixtures + the REAL CitationResolver/ComposeAnchorResolver. There are
// now NO hand-written collaborators at all — none of the ADR-038 banned shapes (Mock<HttpMessageHandler>,
// DI-registration, ctor-null) appear here.

using System.Reflection;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeEditAnchorPassSeamTests
{
    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // Fixtures
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    private static ComposeDocxProjection ProjectCorpus(string fileName)
    {
        var corpusDir = Path.GetDirectoryName(ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First())!;
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(Path.Combine(corpusDir, fileName));
        return new ComposeDocxProjectionBuilder().Build(bytes);
    }

    private static ProposedEdit Edit(
        string newText = "REPLACEMENT",
        string? paraId = null,
        string? reference = null)
        => new(newText, TargetParaId: paraId, TargetRef: reference);

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // FR-C04 — the structural guarantee: the pass CANNOT search document prose
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AnchorPass_Signature_CannotSeeDocumentText_OrAnyTextSearchingCollaborator()
    {
        // This replaces task 051's throwing test double. A runtime tripwire could only catch a leak on the
        // paths a test happens to exercise; the signature closes the door on every path at once. It fails
        // if anyone re-admits the document body (or a searcher for it) into anchor resolution — which is
        // exactly how the retired mechanism would come back.
        var validate = typeof(ComposeEditAnchorPass)
            .GetMethod(nameof(ComposeEditAnchorPass.Validate), BindingFlags.Public | BindingFlags.Static);

        validate.Should().NotBeNull();
        var parameters = validate!.GetParameters();

        parameters.Should().NotContain(
            p => p.ParameterType == typeof(string),
            "a string parameter here is document prose by another name — the anchor pass resolves ids, "
            + "not text (ADR-049 I-7)");

        parameters.Select(p => p.Name).Should().BeEquivalentTo(
            new[] { "edits", "referenceMap" },
            "the closed parameter set IS the guarantee: an edit's target comes from the reference map or "
            + "nowhere");
    }

    [Fact]
    public void VerdictAndRefusalShapes_CannotExpressATextSpan()
    {
        // The OUTPUT half of the same guarantee, and the replacement for the three per-test assertions
        // task 064 deleted (`verdict.Matches.Should().BeEmpty()`, `Error.MatchCount == 0`,
        // `Error.Examples.Should().BeEmpty()`). Those could only ever say "the paths this test exercises
        // returned no span"; pinning the SHAPES says no path could return one, because there is no member
        // to return it on and no type to return.
        //
        // It fails if anyone re-admits an offset vocabulary to this surface — which, together with the
        // signature assertion above, is the only way text-offset placement could come back through this
        // door (ADR-049 I-7).
        var composeTypes = typeof(ComposeEditAnchorPass).Assembly
            .GetTypes()
            .Where(t => t.Namespace == typeof(ComposeEditAnchorPass).Namespace)
            .Select(t => t.Name)
            .ToList();

        composeTypes.Should().NotContain("ResolvedMatch",
            "a character span into document prose is the address the paraId replaced (task 064)");
        composeTypes.Should().NotContain("MatchExample",
            "an example occurrence with a context window is a text-search artefact (task 064)");

        static IEnumerable<string> PublicProperties<T>() =>
            typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name);

        PublicProperties<EditVerdict>().Should().BeEquivalentTo(
            new[] { "EditIndex", "IsValid", "Error", "ResolvedParaId" },
            "a verdict reports WHICH PARAGRAPH an edit anchored to, never where in the prose it matched");

        PublicProperties<EditValidationError>().Should().BeEquivalentTo(
            new[] { "Kind", "Message", "ResolutionHint" },
            "a refusal names the anchor that would have worked; it cannot offer 'nearest' occurrences");

        PublicProperties<BatchValidationResult>().Should().BeEquivalentTo(
            new[] { "Verdicts", "IsValid" },
            "every failure this surface can report belongs to one edit — there is no batch-level channel, "
            + "because the span collisions it carried died with the apply side (task 064)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // FR-C01/C03 — an explicit paraId anchor resolves, with zero text matching
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParaIdAnchor_ResolvesToThatParagraph_WithoutInvokingTextSearch()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");
        var target = projection.ParaIdMap.Single(e => e.Index == 9).ParaId;

        var result = ComposeEditAnchorPass.Validate(
            edits: new[] { Edit(paraId: target) },
            referenceMap: projection.ParaIdMap);

        result.IsValid.Should().BeTrue();
        result.Verdicts.Should().ContainSingle()
            .Which.ResolvedParaId.Should().Be(target, "the anchor names the paragraph outright");

        // The companion "…and reports no text span" half of this assertion is no longer writable: task 064
        // deleted EditVerdict.Matches along with ResolvedMatch itself. It is now asserted structurally, once,
        // by VerdictAndRefusalShapes_CannotExpressATextSpan below.
    }

    [Fact]
    public void ParaIdAnchor_IsCaseInsensitive_AndEchoesTheReferenceMapsOwnSpelling()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");
        var canonical = projection.ParaIdMap.Single(e => e.Index == 9).ParaId;

        var result = ComposeEditAnchorPass.Validate(
            new[] { Edit(paraId: canonical.ToLowerInvariant()) },
            projection.ParaIdMap);

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
            new[] { Edit(reference: citation) },
            projection.ParaIdMap);

        result.IsValid.Should().BeTrue();
        result.Verdicts[0].ResolvedParaId.Should().Be(expected);
    }

    [Fact]
    public void CitationNamingSeveralClauses_IsRefused_NotNarrowedToTheFirst()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");

        var result = ComposeEditAnchorPass.Validate(
            new[] { Edit(reference: "Sections 1-9") },
            projection.ParaIdMap);

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
            new[] { Edit(paraId: "DEADBEEF") },
            projection.ParaIdMap);

        result.IsValid.Should().BeFalse();
        var verdict = result.Verdicts.Should().ContainSingle().Subject;
        verdict.ResolvedParaId.Should().BeNull();
        verdict.Error!.Kind.Should().Be(EditErrorKind.UnknownParaId);
        verdict.Error.ResolutionHint.Should().Contain("target_para_id",
            "the refusal must name the anchor that would have worked — that hint is what replaced the "
            + "match count and example spans task 064 deleted");
    }

    [Fact]
    public void UnresolvableCitation_IsRejected_NotDegradedToATextSearch()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");

        var result = ComposeEditAnchorPass.Validate(
            new[] { Edit(reference: "Section 4127") },
            projection.ParaIdMap);

        result.Verdicts[0].Error!.Kind.Should().Be(EditErrorKind.UnresolvedReference);
    }

    [Fact]
    public void AnchorWithNoReferenceMap_IsRefused_RatherThanSilentlyTextSearched()
    {
        // The dangerous degradation: an anchored edit arriving without a map, quietly falling through to
        // the very search path the anchor exists to replace. It is refused instead.
        var result = ComposeEditAnchorPass.Validate(
            new[] { Edit(paraId: "AAAA1111") },
            referenceMap: null);

        result.Verdicts[0].Error!.Kind.Should().Be(EditErrorKind.NoReferenceMap);
    }

    [Fact]
    public void TwoAnchorsNamingDifferentParagraphs_AreRefused_WithNeitherPreferred()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");
        var otherParaId = projection.ParaIdMap.Single(e => e.Index == 0).ParaId;

        var result = ComposeEditAnchorPass.Validate(
            new[] { Edit(paraId: otherParaId, reference: "Section 4.2") },
            projection.ParaIdMap);

        result.Verdicts[0].Error!.Kind.Should().Be(EditErrorKind.ConflictingAnchors);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // FR-C04 — the un-anchored edit: what USED TO reach the text search now has a defined outcome
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EditWithNoAnchorAtAll_IsRefusedDeterministically_InsteadOfBeingTextSearched()
    {
        // Task 051's version of this test asserted the opposite — that an un-anchored edit STILL reached
        // the legacy validator — because the leg was deliberately kept alive until every anchor source
        // shipped. This is the positive form of its retirement, and it is the assertion that proves the
        // removal actually landed rather than merely being unreachable.
        var projection = ProjectCorpus("heading-style-numbering.docx");

        var result = ComposeEditAnchorPass.Validate(
            new[] { Edit(newText: "The Receiving Party shall keep the information confidential.") },
            projection.ParaIdMap);

        result.IsValid.Should().BeFalse("an edit that names no target cannot be placed");
        var verdict = result.Verdicts.Should().ContainSingle().Subject;
        verdict.Error!.Kind.Should().Be(EditErrorKind.NoAnchor);
        verdict.ResolvedParaId.Should().BeNull();
        verdict.Error.ResolutionHint.Should().Contain("target_para_id")
            .And.Contain("target_ref", "the refusal must tell the caller which anchors would have worked");
    }

    [Fact]
    public void MixedBatch_DecidesEveryEditIndependently_AndKeepsRequestOrderIndices()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");
        var target = projection.ParaIdMap.Single(e => e.Index == 9).ParaId;

        var result = ComposeEditAnchorPass.Validate(
            new[]
            {
                Edit(newText: "A"),                           // 0 — no anchor  -> NoAnchor refusal
                Edit(newText: "B", paraId: target),           // 1 — anchored
                Edit(newText: "C", reference: "Section 4.2"), // 2 — anchored
                Edit(newText: "D"),                           // 3 — no anchor  -> NoAnchor refusal
            },
            projection.ParaIdMap);

        // Verdict indices must track the CALLER's positions, or "Edit N failed" names the wrong edit.
        result.Verdicts.Select(v => v.EditIndex).Should().Equal(0, 1, 2, 3);
        result.Verdicts[1].ResolvedParaId.Should().Be(target);
        result.Verdicts[2].ResolvedParaId.Should().Be(target);
        result.Verdicts[0].Error!.Kind.Should().Be(EditErrorKind.NoAnchor);
        result.Verdicts[3].Error!.Kind.Should().Be(EditErrorKind.NoAnchor);
        result.Verdicts[0].ResolvedParaId.Should().BeNull();
        result.Verdicts[3].ResolvedParaId.Should().BeNull();
        result.IsValid.Should().BeFalse("one refused edit is enough to fail the batch");
    }
}
