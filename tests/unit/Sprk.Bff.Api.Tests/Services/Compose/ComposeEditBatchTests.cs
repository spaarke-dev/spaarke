using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="ComposeEditBatch"/> — FR-20's deterministic 4-phase apply pipeline
/// (task 021): resolve → sort descending → skip overlap → apply bottom-up.
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c>. <see cref="ComposeEditBatch"/> is pure,
/// stateless text processing (ADR-013 facade boundary + ADR-010 minimalism).
/// </para>
///
/// <para>
/// <b>Why the verdicts are hand-built (task 052 / FR-C04 — this is an IMPROVEMENT, not a
/// concession).</b> This suite previously drove the real <c>ComposeEditValidator</c> to produce
/// its <see cref="BatchValidationResult"/> input. That validator was the whole-document
/// <c>target_text</c> search ADR-049 I-7 forbids, and task 052 deleted it. The verdicts are now
/// constructed directly — which is what ADR-038 wants anyway: <b>the subject under test is the
/// batch, not the producer of its input</b>. Coupling these assertions to a second component meant
/// a change in that component's matching behavior could move offsets here and make an
/// offset-stability test fail for a reason that has nothing to do with offset stability. The spans
/// below are still derived from the fixture document by an explicit ordinal
/// <see cref="string.IndexOf(string, System.StringComparison)"/> (see <see cref="Span"/>), so they
/// are computed rather than copied, and they are byte-identical to the offsets the previous
/// validator-driven version resolved ([46,73), [52,73), [25,33), [0,10), [10,20), [21,25)).
/// No <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration tests, no ctor null-check tests
/// (all banned per ADR-038 / <c>tests/CLAUDE.md</c>).
/// </para>
///
/// <para>
/// <b>Document fixture + Proof 2 / Proof 4 reproduction</b>: the document and several edit sets
/// below are the SAME ones used by the Spike 3 prototype
/// (<c>notes/spikes/spike-3-edit-batch.md</c> §4, <c>edit-batch-prototype.cs</c>), which was
/// actually compiled and run (<c>dotnet run</c>, exit 0, all 4 proofs PASS). The offsets asserted
/// here were independently recomputed (not copied blind) to confirm this production port matches
/// the prototype's empirically-observed behavior exactly, including WHICH of two overlapping
/// edits wins (Proof 4: the higher-offset span is claimed first under descending sort, so the
/// edit whose resolved span starts later in the document is the one that survives — see the
/// Proof 4 test below for the full derivation).
/// </para>
/// </summary>
public class ComposeEditBatchTests
{
    // The Spike 3 fixture document — offsets are non-trivial (recurring "shall" / "Premises").
    private const string Document =
        "The Tenant shall pay Rent monthly. The Tenant shall maintain the Premises. " +
        "The Landlord shall provide access to the Premises.";

    private static readonly ComposeEditBatch Sut = new();

    // ── Arrange helpers ─────────────────────────────────────────────────────────────────────

    /// <summary>An edit as the batch sees it: only <c>NewText</c> is read at apply time.</summary>
    private static ProposedEdit Edit(string newText) => new(newText);

    /// <summary>
    /// The span a given fixture substring occupies, computed ordinally from
    /// <see cref="Document"/> — never hand-typed, so a fixture edit cannot silently desync the
    /// asserted offsets.
    /// </summary>
    private static ResolvedMatch Span(string target)
    {
        var offset = Document.IndexOf(target, StringComparison.Ordinal);
        offset.Should().BeGreaterThanOrEqualTo(0, "the fixture substring '{0}' must exist in the test document", target);
        return new ResolvedMatch(offset, target.Length);
    }

    private static EditVerdict Resolved(int editIndex, params ResolvedMatch[] spans)
        => new(editIndex, IsValid: true, Matches: spans, Error: null);

    /// <summary>
    /// A refused verdict. The kind shown is a real post-FR-C04 refusal (an anchor the document does
    /// not have); the batch's fatal gate keys off <see cref="EditVerdict.IsValid"/>, and carries the
    /// error through verbatim.
    /// </summary>
    private static EditVerdict Refused(int editIndex, EditErrorKind kind = EditErrorKind.UnknownParaId)
        => new(
            editIndex,
            IsValid: false,
            Matches: Array.Empty<ResolvedMatch>(),
            Error: new EditValidationError(
                kind,
                $"Edit {editIndex + 1}: the anchor names no paragraph in this document.",
                MatchCount: 0,
                Examples: Array.Empty<MatchExample>(),
                ResolutionHint: "Re-issue the edit with a target_para_id drawn from the supplied set."));

    private static BatchValidationResult Validation(params EditVerdict[] verdicts)
        => new(verdicts, Array.Empty<EditValidationError>());

    // ── Proof 1 equivalent — offset stability across non-overlapping, different-length edits ──

    [Fact]
    public void Apply_ThreeNonOverlappingEditsOfDifferentLengths_AppliesAllWithoutOffsetDrift()
    {
        var edits = new[]
        {
            Edit("pay the Rent on the first day of each month"), // grows
            Edit("keep the Premises in good repair"),            // ~same length
            Edit("grant entry"),                                 // shrinks
        };
        var validation = Validation(
            Resolved(0, Span("pay Rent monthly")),
            Resolved(1, Span("maintain the Premises")),
            Resolved(2, Span("provide access to the Premises")));
        validation.IsValid.Should().BeTrue();

        var result = Sut.Apply(Document, edits, validation);

        const string expected =
            "The Tenant shall pay the Rent on the first day of each month. The Tenant shall keep the Premises in good repair. " +
            "The Landlord shall grant entry.";
        result.Committed.Should().BeTrue();
        result.Applied.Should().HaveCount(3);
        result.Skipped.Should().BeEmpty();
        result.DocumentText.Should().Be(expected, "bottom-up application must preserve every offset exactly");
    }

    // ── Proof 2 regression fixture — ONE invalid edit rolls back the WHOLE batch ────────────

    [Fact]
    public void Apply_OneInvalidEditAmongValidOnes_RollsBackEntireBatch_NoneApply()
    {
        var edits = new[]
        {
            Edit("pay the Rent on the first day of each month"),
            Edit("keep the Premises in good repair"),
            Edit("grant entry"),
            Edit("hold harmless"), // refused upstream — its anchor resolves to nothing
        };
        var validation = Validation(
            Resolved(0, Span("pay Rent monthly")),
            Resolved(1, Span("maintain the Premises")),
            Resolved(2, Span("provide access to the Premises")),
            Refused(3));
        validation.IsValid.Should().BeFalse("edit index 3 was refused upstream");

        var result = Sut.Apply(Document, edits, validation);

        result.Committed.Should().BeFalse("a single validation failure is FATAL to the whole batch (Spike 3 §2)");
        result.DocumentText.Should().Be(Document, "the untouched document must come back byte-identical — NONE of the 4 edits applied");
        result.Applied.Should().BeEmpty();
        result.Skipped.Should().BeEmpty("validation failure is a distinct, separate code path from overlap-skip");
        result.ValidationErrors.Should().ContainSingle()
            .Which.Should().Match<EditValidationError>(e => e.Kind == EditErrorKind.UnknownParaId,
                "the refusal must flow through verbatim, not be flattened to a generic failure");
    }

    // ── Proof 4 regression fixture — within-batch overlap is NON-FATAL ──────────────────────

    [Fact]
    public void Apply_OverlappingEditsInBatch_SkipsLaterClaimedSpanAndStillCommits()
    {
        // "shall maintain the Premises" resolves to [46,73); "maintain the Premises" (a suffix of
        // the first) resolves to [52,73) — both end at 73, so the higher-start span [52,73)
        // sorts FIRST under descending order and claims the range; the lower-start, longer span
        // [46,73) is then skipped as overlapping. This means edit index 1 (declared SECOND)
        // applies and edit index 0 (declared FIRST) is skipped — confirmed by independent offset
        // computation, not assumed from the prototype's prose.
        var edits = new[]
        {
            Edit("shall keep the Premises tidy"), // [46,73)
            Edit("repair the Premises"),          // [52,73) — overlaps above
        };

        // ARRANGE, deliberately: a caller that reports cross-edit span collision as a BATCH-level
        // error (the /edit-batch/validate dry-run UX). That makes BatchValidationResult.IsValid
        // false while every per-edit verdict is valid — which is the exact input shape this test
        // exists to pin, because ComposeEditBatch must IGNORE that batch error and apply its own
        // Phase-3 overlap semantics instead.
        var validation = new BatchValidationResult(
            new[] { Resolved(0, Span("shall maintain the Premises")), Resolved(1, Span("maintain the Premises")) },
            new[]
            {
                new EditValidationError(
                    EditErrorKind.Overlap,
                    "Edits 1 and 2 resolve to overlapping spans ([46,73) vs [52,73)).",
                    MatchCount: 2,
                    Examples: Array.Empty<MatchExample>(),
                    ResolutionHint: "Merge them into one edit, or make the spans disjoint."),
            });
        validation.IsValid.Should().BeFalse(
            "a batch-level overlap error makes the RESULT's own IsValid false — " +
            "but that is a DIFFERENT consumer's gate (the /edit-batch/validate dry-run UX), not this pipeline's");
        validation.Verdicts.Should().OnlyContain(v => v.IsValid, "each edit resolves unambiguously on its own; only the CROSS-edit span collides");

        var result = Sut.Apply(Document, edits, validation);

        const string expected =
            "The Tenant shall pay Rent monthly. The Tenant shall repair the Premises. " +
            "The Landlord shall provide access to the Premises.";
        result.Committed.Should().BeTrue("overlap is NON-FATAL — the batch still commits (Spike 3 §2)");
        result.Applied.Should().ContainSingle().Which.EditIndex.Should().Be(1);
        result.Skipped.Should().ContainSingle().Which.EditIndex.Should().Be(0);
        result.DocumentText.Should().Be(expected);
    }

    // ── Empty batch (acceptance criterion: empty edit list / degenerate span) ──────────────

    [Fact]
    public void Apply_EmptyEditList_ReturnsDocumentUnchanged()
    {
        var edits = Array.Empty<ProposedEdit>();
        var validation = Validation();

        var result = Sut.Apply(Document, edits, validation);

        result.Committed.Should().BeTrue();
        result.DocumentText.Should().Be(Document);
        result.Applied.Should().BeEmpty();
        result.Skipped.Should().BeEmpty();
    }

    // ── A valid verdict that resolved ZERO spans — the post-FR-C04 anchored shape ────────────

    [Fact]
    public void Apply_ValidVerdictsCarryingNoSpans_CommitsTheDocumentUnchanged()
    {
        // This is what EVERY verdict ComposeEditAnchorPass produces now looks like: valid, anchored
        // by paraId, and carrying an EMPTY Matches because the paraId IS the address. The batch has
        // nothing to splice, so it must commit a byte-identical document rather than treat the
        // absence of spans as a failure.
        var edits = new[] { Edit("replacement") };
        var validation = Validation(
            new EditVerdict(0, IsValid: true, Matches: Array.Empty<ResolvedMatch>(), Error: null, ResolvedParaId: "1A2B3C4D"));

        var result = Sut.Apply(Document, edits, validation);

        result.Committed.Should().BeTrue("a no-op batch is not a failure");
        result.DocumentText.Should().Be(Document);
        result.Applied.Should().BeEmpty();
        result.Skipped.Should().BeEmpty();
        result.ValidationErrors.Should().BeEmpty();
    }

    // ── EDGE-6: empty NewText is a pure deletion — degenerate (zero-length) replacement ─────

    [Fact]
    public void Apply_EmptyNewText_AppliesPureDeletionAtCorrectOffset()
    {
        var edits = new[] { Edit("") };
        var validation = Validation(Resolved(0, Span(" monthly"))); // unique, [25,33)

        var result = Sut.Apply(Document, edits, validation);

        const string expected =
            "The Tenant shall pay Rent. The Tenant shall maintain the Premises. " +
            "The Landlord shall provide access to the Premises.";
        result.Committed.Should().BeTrue();
        result.Applied.Should().ContainSingle().Which.NewText.Should().Be("");
        result.DocumentText.Should().Be(expected, "the matched span is removed and nothing is inserted in its place");
    }

    // ── Edit resolved at the document's start and at its tail ───────────────────────────────

    [Fact]
    public void Apply_EditsAtDocumentStartAndEnd_BothApplyCorrectly()
    {
        var edits = new[]
        {
            Edit("The Tenant will pay"),                            // offset 0
            Edit("The Landlord grants access to the Premises."),    // ends at doc.Length
        };
        var validation = Validation(
            Resolved(0, Span("The Tenant shall pay")),
            Resolved(1, Span("The Landlord shall provide access to the Premises.")));

        var result = Sut.Apply(Document, edits, validation);

        const string expected =
            "The Tenant will pay Rent monthly. The Tenant shall maintain the Premises. " +
            "The Landlord grants access to the Premises.";
        result.Committed.Should().BeTrue();
        result.Applied.Should().HaveCount(2);
        result.DocumentText.Should().Be(expected);
    }

    // ── EDGE-4: adjacent-but-not-overlapping spans (half-open boundary touches, doesn't intersect) ──

    [Fact]
    public void Apply_AdjacentButNonOverlappingEdits_BothApply()
    {
        var edits = new[]
        {
            Edit("The Client"),  // [0,10)
            Edit(" must remit"), // [10,20) — touches edit 0's end, does not overlap
        };
        var validation = Validation(
            Resolved(0, Span("The Tenant")),
            Resolved(1, Span(" shall pay")));

        var result = Sut.Apply(Document, edits, validation);

        const string expected =
            "The Client must remit Rent monthly. The Tenant shall maintain the Premises. " +
            "The Landlord shall provide access to the Premises.";
        result.Committed.Should().BeTrue();
        result.Applied.Should().HaveCount(2);
        // The half-open-interval rule lives in the SUT's own Phase 3; the previous version of this
        // test ALSO asserted it against the retired validator's duplicate detector, which is gone
        // with that validator. The assertion that matters is this one, against the pipeline itself.
        result.Skipped.Should().BeEmpty("touching, non-intersecting spans must NOT be treated as overlap");
        result.DocumentText.Should().Be(expected);
    }

    // ── EDGE-5: identical target spans — both edits resolve to the SAME [Start,End) ─────────

    [Fact]
    public void Apply_IdenticalTargetSpans_SkipsSecondOccurrenceAsOverlap()
    {
        var edits = new[]
        {
            Edit("the Rent sum"),    // [21,25) — declared first, applies
            Edit("the Rent amount"), // the SAME [21,25) — skipped
        };
        var validation = Validation(Resolved(0, Span("Rent")), Resolved(1, Span("Rent")));
        validation.Verdicts.Should().OnlyContain(v => v.IsValid, "each edit resolved on its own; only the CROSS-edit span is identical");

        var result = Sut.Apply(Document, edits, validation);

        const string expected =
            "The Tenant shall pay the Rent sum monthly. The Tenant shall maintain the Premises. " +
            "The Landlord shall provide access to the Premises.";
        result.Committed.Should().BeTrue();
        result.Applied.Should().ContainSingle().Which.EditIndex.Should().Be(0);
        result.Skipped.Should().ContainSingle().Which.EditIndex.Should().Be(1);
        result.DocumentText.Should().Be(expected);
    }

    // ── Report shape: applied vs skipped are always distinguishable, even in a mixed batch ──

    [Fact]
    public void Apply_MixedValidOverlapAndUniqueEdits_ReportDistinguishesAppliedFromSkipped()
    {
        var edits = new[]
        {
            Edit("shall keep the Premises tidy"), // overlapped-away
            Edit("repair the Premises"),          // wins the overlap
            Edit("pay the Rent promptly"),        // unrelated, unique
        };
        var validation = Validation(
            Resolved(0, Span("shall maintain the Premises")),
            Resolved(1, Span("maintain the Premises")),
            Resolved(2, Span("pay Rent monthly")));

        var result = Sut.Apply(Document, edits, validation);

        result.Committed.Should().BeTrue();
        result.Applied.Select(a => a.EditIndex).Should().BeEquivalentTo(new[] { 1, 2 });
        result.Skipped.Select(s => s.EditIndex).Should().BeEquivalentTo(new[] { 0 });
        result.Skipped.Single().Reason.Should().NotBeNullOrWhiteSpace();
    }

    // ── Contract integrity guard ─────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_ValidationVerdictCountDoesNotMatchEditCount_ThrowsArgumentException()
    {
        var edits = new[] { Edit("Fee") };
        var mismatchedValidation = Validation(); // 0 verdicts, 1 edit

        var act = () => Sut.Apply(Document, edits, mismatchedValidation);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("validation");
    }
}
