using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="ComposeEditValidator"/> — FR-19's deterministic edit-validation
/// engine (task 020).
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c>. <see cref="ComposeEditValidator"/> is
/// pure, dependency-free text processing (ADR-013 facade boundary + ADR-010 minimalism — no
/// constructor, nothing to mock). Every test below exercises the real class through its public
/// <see cref="IComposeEditValidator.Validate"/> contract with real strings; no
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration tests, no ctor null-check tests
/// (all banned per ADR-038 / <c>tests/CLAUDE.md</c>).
/// </para>
///
/// <para>
/// The document fixture and the offsets asserted below are the SAME ones empirically verified
/// by the Spike 2 prototype run (<c>notes/spikes/spike-2-edit-validator.md</c> §4) —
/// <c>edit-validator-prototype.cs</c> was executed (<c>dotnet run</c>) against this exact text
/// and these are the real, captured offsets, not hand-derived guesses.
/// </para>
/// </summary>
public class ComposeEditValidatorTests
{
    // The Spike 2 fixture document (415 chars, deliberate repetition of "the Agreement" / "Party").
    private const string Document =
        "This Master Services Agreement (the \"Agreement\") is entered into by the parties.\n" +
        "The Agreement governs all Statements of Work. Each Party shall perform under the Agreement.\n" +
        "Termination of the Agreement requires thirty (30) days notice. The Effective Date is the date\n" +
        "on which the Agreement is signed by the last Party to sign. Confidential Information means any\n" +
        "information disclosed by a Party under the Agreement.";

    private static readonly IComposeEditValidator Sut = new ComposeEditValidator();

    // ── S1: unambiguous / strict ────────────────────────────────────────────────────

    [Fact]
    public void Validate_StrictWithUniqueTarget_ResolvesSingleSpanWithCorrectOffset()
    {
        var edit = new ProposedEdit("thirty (30) days notice", "sixty (60) days' written notice", MatchMode.Strict);

        var result = Sut.Validate(Document, new[] { edit });

        result.IsValid.Should().BeTrue();
        var verdict = result.Verdicts.Single();
        verdict.IsValid.Should().BeTrue();
        verdict.Matches.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ResolvedMatch(211, edit.TargetText.Length));
    }

    // ── S2: ambiguous / strict — the core safety refusal ────────────────────────────

    [Fact]
    public void Validate_StrictWithMultipleMatches_ReturnsAmbiguousErrorWithExamplesAndResolutionHint()
    {
        var edit = new ProposedEdit("the Agreement", "this Agreement", MatchMode.Strict);

        var result = Sut.Validate(Document, new[] { edit });

        result.IsValid.Should().BeFalse();
        var verdict = result.Verdicts.Single();
        verdict.IsValid.Should().BeFalse();
        verdict.Matches.Should().BeEmpty();

        var error = verdict.Error!;
        error.Kind.Should().Be(EditErrorKind.Ambiguous);
        error.MatchCount.Should().Be(4);
        error.Examples.Should().HaveCount(4);
        error.Examples.Select(e => e.Offset).Should().Equal(158, 188, 276, 401);
        error.Examples.Should().OnlyContain(e => e.Matched == "the Agreement");

        // The resolution hint must be a copy-pasteable escape hatch naming all three options —
        // this is the load-bearing UX that stops the LLM's refine-forever loop (spike-2 §3).
        error.ResolutionHint.Should().Contain("\"match_mode\":\"all\"");
        error.ResolutionHint.Should().Contain("\"match_mode\":\"first\"");
        error.ResolutionHint.Should().Contain("Extend target_text");
    }

    // ── S4: multi-match / first ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_FirstOnMultiMatchTarget_ResolvesEarliestOccurrenceOnly()
    {
        var edit = new ProposedEdit("Party", "party", MatchMode.First);

        var result = Sut.Validate(Document, new[] { edit });

        result.IsValid.Should().BeTrue();
        var verdict = result.Verdicts.Single();
        verdict.Matches.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ResolvedMatch(132, 5));
    }

    // ── S5: multi-match / all ────────────────────────────────────────────────────────

    [Fact]
    public void Validate_AllOnMultiMatchTarget_ResolvesEveryOccurrence()
    {
        var edit = new ProposedEdit("the Agreement", "this Agreement", MatchMode.All);

        var result = Sut.Validate(Document, new[] { edit });

        result.IsValid.Should().BeTrue();
        var verdict = result.Verdicts.Single();
        verdict.Matches.Select(m => m.Offset).Should().Equal(158, 188, 276, 401);
        verdict.Matches.Should().OnlyContain(m => m.Length == "the Agreement".Length);
    }

    // ── S6: empty / whitespace-only target_text ─────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void Validate_EmptyOrWhitespaceOnlyTarget_ReturnsEmptyTargetError(string targetText)
    {
        var edit = new ProposedEdit(targetText, "x", MatchMode.Strict);

        var result = Sut.Validate(Document, new[] { edit });

        result.IsValid.Should().BeFalse();
        var error = result.Verdicts.Single().Error!;
        error.Kind.Should().Be(EditErrorKind.EmptyTarget);
        error.MatchCount.Should().Be(0);
        error.Examples.Should().BeEmpty();
    }

    // ── S3 + boundary findings: zero-match / no-match, with nearest-candidate hints ─

    [Fact]
    public void Validate_ZeroMatchTarget_ReturnsNoMatchErrorWithGenericHintWhenNoNearVariantExists()
    {
        // A genuine misspelling ("agreemnt") — fuzzy/typo matching is Phase-2 DEFERRED
        // (spike-2 §5.1); the validator falls through to the generic fallback hint, NOT a
        // "did you mean" suggestion.
        var edit = new ProposedEdit("the agreemnt", "the Agreement", MatchMode.Strict);

        var result = Sut.Validate(Document, new[] { edit });

        result.IsValid.Should().BeFalse();
        var error = result.Verdicts.Single().Error!;
        error.Kind.Should().Be(EditErrorKind.NoMatch);
        error.MatchCount.Should().Be(0);
        error.ResolutionHint.Should().Be("No near match found. Re-read the current document window and copy an exact span verbatim.");
    }

    [Fact]
    public void Validate_ZeroMatchTarget_ReturnsWhitespaceTrimHint_WhenTrimmedVariantExists()
    {
        var edit = new ProposedEdit("  thirty (30) days notice  ", "x", MatchMode.Strict);

        var result = Sut.Validate(Document, new[] { edit });

        result.IsValid.Should().BeFalse();
        var error = result.Verdicts.Single().Error!;
        error.Kind.Should().Be(EditErrorKind.NoMatch);
        error.ResolutionHint.Should().Contain("extra whitespace");
    }

    [Fact]
    public void Validate_ZeroMatchTarget_ReturnsCaseInsensitiveHint_WhenCasingVariantExists()
    {
        var edit = new ProposedEdit("AGREEMENT", "x", MatchMode.Strict);

        var result = Sut.Validate(Document, new[] { edit });

        result.IsValid.Should().BeFalse();
        var error = result.Verdicts.Single().Error!;
        error.Kind.Should().Be(EditErrorKind.NoMatch);
        error.ResolutionHint.Should().Contain("case-insensitive match exists");
        error.ResolutionHint.Should().Contain("case-SENSITIVE");
    }

    // ── S7: cross-edit overlap within one batch ─────────────────────────────────────

    [Fact]
    public void Validate_TwoEditsWithOverlappingResolvedSpans_FlagsBatchOverlapErrorInsteadOfConflictingResolutions()
    {
        var edits = new[]
        {
            new ProposedEdit("Confidential Information means any\ninformation", "CI means all information", MatchMode.Strict),
            new ProposedEdit("means any\ninformation disclosed", "covers disclosures", MatchMode.Strict),
        };

        var result = Sut.Validate(Document, edits);

        // Each edit resolves fine on its own (both are individually unique, unambiguous
        // matches) — the overlap is a BATCH-level concern, not a per-edit ambiguity.
        result.Verdicts.Should().OnlyContain(v => v.IsValid);
        result.IsValid.Should().BeFalse();
        result.BatchErrors.Should().ContainSingle();

        var overlap = result.BatchErrors.Single();
        overlap.Kind.Should().Be(EditErrorKind.Overlap);
        overlap.Examples.Should().HaveCount(2);
        overlap.ResolutionHint.Should().Contain("Merge them into one edit");
    }

    [Fact]
    public void Validate_TwoEditsWithDisjointResolvedSpans_BothResolveWithNoBatchErrors()
    {
        var edits = new[]
        {
            new ProposedEdit("thirty (30) days notice", "sixty (60) days notice", MatchMode.Strict),
            new ProposedEdit("Party", "party", MatchMode.First),
        };

        var result = Sut.Validate(Document, edits);

        result.IsValid.Should().BeTrue();
        result.Verdicts.Should().HaveCount(2);
        result.Verdicts.Should().OnlyContain(v => v.IsValid);
        result.BatchErrors.Should().BeEmpty();
    }
}
