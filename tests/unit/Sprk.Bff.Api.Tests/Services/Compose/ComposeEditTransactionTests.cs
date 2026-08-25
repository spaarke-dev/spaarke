using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="ComposeEditTransaction"/> — FR-21's snapshot/rollback wrapper
/// (task 022) over <see cref="ComposeEditBatch"/> (FR-20).
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c>. <see cref="ComposeEditTransaction"/> is
/// pure, stateless wrapper logic (ADR-013 facade boundary + ADR-010 minimalism). It composes the
/// REAL <see cref="ComposeEditBatch"/> (FR-20) — the collaborator that actually matters here.
/// </para>
///
/// <para>
/// <b>Why the verdicts are hand-built (task 052 / FR-C04 — this is an IMPROVEMENT, not a
/// concession).</b> This suite previously also drove the real <c>ComposeEditValidator</c> to
/// produce its <see cref="BatchValidationResult"/> input. That validator was the whole-document
/// <c>target_text</c> search ADR-049 I-7 forbids, and task 052 deleted it. Verdicts are now
/// constructed directly, which is what ADR-038 wants anyway: <b>the subject under test is the
/// transaction (and, through it, the batch) — not the producer of its input</b>. The genuine
/// composition still under test is FR-20 → FR-21; only the retired FR-19 hop is gone. Spans are
/// derived from the fixture document by an explicit ordinal
/// <see cref="string.IndexOf(string, System.StringComparison)"/>, so they are computed rather than
/// copied. No <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration tests, no ctor null-check
/// tests (all banned per ADR-038 / <c>tests/CLAUDE.md</c>).
/// </para>
///
/// <para>
/// <b>Document fixture</b>: the same Spike 3 / <c>ComposeEditBatchTests</c> fixture document, so
/// the atomicity property (Proof 2) and the non-fatal-overlap property (Proof 4) are exercised
/// here at the transaction boundary too, plus the transaction-only capabilities (explicit
/// snapshot exposure, caller-requested <see cref="ComposeEditTransaction.Rollback"/>).
/// </para>
/// </summary>
public class ComposeEditTransactionTests
{
    private const string Document =
        "The Tenant shall pay Rent monthly. The Tenant shall maintain the Premises. " +
        "The Landlord shall provide access to the Premises.";

    private static readonly ComposeEditTransaction Sut = new(new ComposeEditBatch());

    // ── Arrange helpers (mirror ComposeEditBatchTests) ──────────────────────────────────────

    private static ProposedEdit Edit(string newText) => new(newText);

    private static ResolvedMatch Span(string target)
    {
        var offset = Document.IndexOf(target, StringComparison.Ordinal);
        offset.Should().BeGreaterThanOrEqualTo(0, "the fixture substring '{0}' must exist in the test document", target);
        return new ResolvedMatch(offset, target.Length);
    }

    private static EditVerdict Resolved(int editIndex, ResolvedMatch span)
        => new(editIndex, IsValid: true, Matches: new[] { span }, Error: null);

    /// <summary>A refused verdict — a real post-FR-C04 anchor refusal.</summary>
    private static EditVerdict Refused(int editIndex)
        => new(
            editIndex,
            IsValid: false,
            Matches: Array.Empty<ResolvedMatch>(),
            Error: new EditValidationError(
                EditErrorKind.UnknownParaId,
                $"Edit {editIndex + 1}: the anchor names no paragraph in this document.",
                MatchCount: 0,
                Examples: Array.Empty<MatchExample>(),
                ResolutionHint: "Re-issue the edit with a target_para_id drawn from the supplied set."));

    private static BatchValidationResult Validation(params EditVerdict[] verdicts)
        => new(verdicts, Array.Empty<EditValidationError>());

    // ── Acceptance criterion 1 — all edits valid and apply: commits transformed document ────

    [Fact]
    public void Execute_AllEditsValid_CommitsTransformedDocument()
    {
        var edits = new[]
        {
            Edit("pay the Rent on the first day of each month"),
            Edit("keep the Premises in good repair"),
        };
        var validation = Validation(
            Resolved(0, Span("pay Rent monthly")),
            Resolved(1, Span("maintain the Premises")));
        validation.IsValid.Should().BeTrue();

        var result = Sut.Execute(Document, edits, validation);

        const string expected =
            "The Tenant shall pay the Rent on the first day of each month. The Tenant shall keep the Premises in good repair. " +
            "The Landlord shall provide access to the Premises.";
        result.Committed.Should().BeTrue();
        result.DocumentText.Should().Be(expected);
        result.Snapshot.Should().Be(Document, "the snapshot must always reflect the pre-batch state, even after a commit");
        result.Batch.Applied.Should().HaveCount(2);
    }

    // ── Acceptance criterion 2 — one failing edit applies NONE (NEGATIVE: partial-apply) ─────

    [Fact]
    public void Execute_OneInvalidEditAmongValidOnes_AppliesNoneAndReturnsByteIdenticalSnapshot()
    {
        var edits = new[]
        {
            Edit("pay the Rent on the first day of each month"),
            Edit("keep the Premises in good repair"),
            Edit("hold harmless"), // refused upstream
        };
        var validation = Validation(
            Resolved(0, Span("pay Rent monthly")),
            Resolved(1, Span("maintain the Premises")),
            Refused(2));
        validation.IsValid.Should().BeFalse("edit index 2 was refused upstream");

        var result = Sut.Execute(Document, edits, validation);

        result.Committed.Should().BeFalse("a single validation failure is FATAL to the whole transaction");
        result.DocumentText.Should().Be(Document, "the returned document must be byte-identical to the pre-batch snapshot");
        result.DocumentText.Should().BeSameAs(result.Snapshot, "no new string should be allocated on the fatal rollback path");
        result.Batch.Applied.Should().BeEmpty();
    }

    // ── Acceptance criterion 3 — all-failing batch: original document + full failure report ──

    [Fact]
    public void Execute_AllEditsFailValidation_ReturnsOriginalDocumentAndFailureReportForEachEdit()
    {
        var edits = new[] { Edit("hold harmless"), Edit("terminate for convenience") };
        var validation = Validation(Refused(0), Refused(1));
        validation.IsValid.Should().BeFalse();

        var result = Sut.Execute(Document, edits, validation);

        result.Committed.Should().BeFalse();
        result.DocumentText.Should().Be(Document);
        result.Batch.ValidationErrors.Should().HaveCount(2, "every failing edit must be enumerated in the failure report");
        result.Batch.ValidationErrors.Should().OnlyContain(e => e.Kind == EditErrorKind.UnknownParaId,
            "each refusal must flow through with its own kind, not be flattened to a generic failure");
    }

    // ── Acceptance criterion 4 — empty batch commits the unchanged document (NEGATIVE) ───────

    [Fact]
    public void Execute_EmptyBatch_CommitsUnchangedDocument()
    {
        var edits = Array.Empty<ProposedEdit>();
        var validation = Validation();

        var result = Sut.Execute(Document, edits, validation);

        result.Committed.Should().BeTrue("a degenerate empty transaction is not a failure");
        result.DocumentText.Should().Be(Document);
        result.Snapshot.Should().Be(Document);
        result.Batch.Applied.Should().BeEmpty();
    }

    // ── Transaction-only capability: caller-requested Rollback after a clean commit ──────────

    [Fact]
    public void Rollback_AfterCleanCommit_RevertsToByteIdenticalSnapshot()
    {
        var edits = new[] { Edit("pay the Rent promptly") };
        var validation = Validation(Resolved(0, Span("pay Rent monthly")));
        var committed = Sut.Execute(Document, edits, validation);
        committed.Committed.Should().BeTrue();
        committed.DocumentText.Should().NotBe(Document, "sanity check — the commit really did transform the document");

        var rolledBack = Sut.Rollback(committed);

        rolledBack.Committed.Should().BeFalse();
        rolledBack.DocumentText.Should().Be(Document, "caller-requested rollback must restore the pre-batch snapshot byte-for-byte");
        rolledBack.Snapshot.Should().Be(committed.Snapshot, "the snapshot itself never changes across rollback");
        rolledBack.Batch.Should().BeSameAs(committed.Batch, "the underlying batch report (applied/skipped) is preserved for audit even after rollback");
    }

    // ── Rollback is idempotent on an already-rolled-back transaction ─────────────────────────

    [Fact]
    public void Rollback_OnAlreadyRolledBackTransaction_IsIdempotentNoOp()
    {
        var edits = new[] { Edit("hold harmless") };
        var validation = Validation(Refused(0)); // fatal
        var fatal = Sut.Execute(Document, edits, validation);
        fatal.Committed.Should().BeFalse();

        var result = Sut.Rollback(fatal);

        result.Should().BeSameAs(fatal, "rolling back a transaction that is already rolled back must be a no-op");
    }

    // ── Overlap remains non-fatal at the transaction boundary too (Spike 3 §2 distinction) ───

    [Fact]
    public void Execute_OverlappingEditsInBatch_StillCommitsWithSkippedReport()
    {
        var edits = new[]
        {
            Edit("shall keep the Premises tidy"), // [46,73)
            Edit("repair the Premises"),          // [52,73) — overlaps above
        };
        var validation = Validation(
            Resolved(0, Span("shall maintain the Premises")),
            Resolved(1, Span("maintain the Premises")));

        var result = Sut.Execute(Document, edits, validation);

        result.Committed.Should().BeTrue("within-batch overlap is NON-FATAL — must not be conflated with the transaction's fatal rollback path");
        result.Batch.Applied.Should().ContainSingle();
        result.Batch.Skipped.Should().ContainSingle();
    }
}
