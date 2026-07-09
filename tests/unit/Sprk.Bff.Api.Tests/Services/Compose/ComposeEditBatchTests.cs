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
/// stateless text processing (ADR-013 facade boundary + ADR-010 minimalism). Every test below
/// exercises the REAL <see cref="ComposeEditValidator"/> (FR-19) to produce the
/// <see cref="BatchValidationResult"/> input — no hand-crafted verdicts, no
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration tests, no ctor null-check tests (all
/// banned per ADR-038 / <c>tests/CLAUDE.md</c>). This also proves genuine FR-19→FR-20
/// composition end to end, exactly as a real caller (a future endpoint, or FR-21's
/// <c>ComposeEditTransaction</c>) will use it.
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

    private static readonly ComposeEditValidator Validator = new();
    private static readonly ComposeEditBatch Sut = new();

    // ── Proof 1 equivalent — offset stability across non-overlapping, different-length edits ──

    [Fact]
    public void Apply_ThreeNonOverlappingEditsOfDifferentLengths_AppliesAllWithoutOffsetDrift()
    {
        var edits = new[]
        {
            new ProposedEdit("pay Rent monthly", "pay the Rent on the first day of each month", MatchMode.Strict), // grows
            new ProposedEdit("maintain the Premises", "keep the Premises in good repair", MatchMode.Strict),        // ~same length
            new ProposedEdit("provide access to the Premises", "grant entry", MatchMode.Strict),                   // shrinks
        };
        var validation = Validator.Validate(Document, edits);
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
            new ProposedEdit("pay Rent monthly", "pay the Rent on the first day of each month", MatchMode.Strict),
            new ProposedEdit("maintain the Premises", "keep the Premises in good repair", MatchMode.Strict),
            new ProposedEdit("provide access to the Premises", "grant entry", MatchMode.Strict),
            new ProposedEdit("indemnify the Guarantor", "hold harmless", MatchMode.Strict), // NOT in the document
        };
        var validation = Validator.Validate(Document, edits);
        validation.IsValid.Should().BeFalse("edit index 3's target_text does not exist in the document");

        var result = Sut.Apply(Document, edits, validation);

        result.Committed.Should().BeFalse("a single validation failure is FATAL to the whole batch (Spike 3 §2)");
        result.DocumentText.Should().Be(Document, "the untouched document must come back byte-identical — NONE of the 4 edits applied");
        result.Applied.Should().BeEmpty();
        result.Skipped.Should().BeEmpty("validation failure is a distinct, separate code path from overlap-skip");
        result.ValidationErrors.Should().ContainSingle()
            .Which.Should().Match<EditValidationError>(e => e.Kind == EditErrorKind.NoMatch);
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
            new ProposedEdit("shall maintain the Premises", "shall keep the Premises tidy", MatchMode.Strict), // [46,73)
            new ProposedEdit("maintain the Premises", "repair the Premises", MatchMode.Strict),                 // [52,73) — overlaps above
        };
        var validation = Validator.Validate(Document, edits);
        validation.IsValid.Should().BeFalse(
            "the VALIDATOR flags cross-edit overlap as a batch error (its own IsValid contract) — " +
            "but that is a DIFFERENT consumer's gate (the /edit-batch/validate dry-run UX), not this pipeline's");
        validation.BatchErrors.Should().ContainSingle().Which.Kind.Should().Be(EditErrorKind.Overlap);
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
        var validation = Validator.Validate(Document, edits);

        var result = Sut.Apply(Document, edits, validation);

        result.Committed.Should().BeTrue();
        result.DocumentText.Should().Be(Document);
        result.Applied.Should().BeEmpty();
        result.Skipped.Should().BeEmpty();
    }

    // ── EDGE-6: empty NewText is a pure deletion — degenerate (zero-length) replacement ─────

    [Fact]
    public void Apply_EmptyNewText_AppliesPureDeletionAtCorrectOffset()
    {
        var edits = new[] { new ProposedEdit(" monthly", "", MatchMode.Strict) }; // unique, [25,33)
        var validation = Validator.Validate(Document, edits);
        validation.IsValid.Should().BeTrue();

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
            new ProposedEdit("The Tenant shall pay", "The Tenant will pay", MatchMode.Strict),                                       // offset 0
            new ProposedEdit("The Landlord shall provide access to the Premises.", "The Landlord grants access to the Premises.", MatchMode.Strict), // ends at doc.Length
        };
        var validation = Validator.Validate(Document, edits);
        validation.IsValid.Should().BeTrue();

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
            new ProposedEdit("The Tenant", "The Client", MatchMode.First), // [0,10) — "The Tenant" recurs, resolve earliest
            new ProposedEdit(" shall pay", " must remit", MatchMode.Strict), // [10,20) — touches edit 0's end, does not overlap
        };
        var validation = Validator.Validate(Document, edits);
        validation.IsValid.Should().BeTrue();
        validation.BatchErrors.Should().BeEmpty("adjacent [0,10) and [10,20) spans touch but do not intersect");

        var result = Sut.Apply(Document, edits, validation);

        const string expected =
            "The Client must remit Rent monthly. The Tenant shall maintain the Premises. " +
            "The Landlord shall provide access to the Premises.";
        result.Committed.Should().BeTrue();
        result.Applied.Should().HaveCount(2);
        result.Skipped.Should().BeEmpty("touching, non-intersecting spans must NOT be treated as overlap");
        result.DocumentText.Should().Be(expected);
    }

    // ── EDGE-5: identical target spans — both edits resolve to the SAME [Start,End) ─────────

    [Fact]
    public void Apply_IdenticalTargetSpans_SkipsSecondOccurrenceAsOverlap()
    {
        var edits = new[]
        {
            new ProposedEdit("Rent", "the Rent sum", MatchMode.Strict),    // [21,25) — declared first, applies
            new ProposedEdit("Rent", "the Rent amount", MatchMode.Strict), // resolves to the SAME [21,25) — skipped
        };
        var validation = Validator.Validate(Document, edits);
        validation.Verdicts.Should().OnlyContain(v => v.IsValid, "each edit's OWN target_text is unique in the document (one match each)");

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
            new ProposedEdit("shall maintain the Premises", "shall keep the Premises tidy", MatchMode.Strict), // overlapped-away
            new ProposedEdit("maintain the Premises", "repair the Premises", MatchMode.Strict),                 // wins the overlap
            new ProposedEdit("pay Rent monthly", "pay the Rent promptly", MatchMode.Strict),                    // unrelated, unique
        };
        var validation = Validator.Validate(Document, edits);

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
        var edits = new[] { new ProposedEdit("Rent", "Fee", MatchMode.Strict) };
        var mismatchedValidation = Validator.Validate(Document, Array.Empty<ProposedEdit>()); // 0 verdicts, 1 edit

        var act = () => Sut.Apply(Document, edits, mismatchedValidation);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("validation");
    }
}
