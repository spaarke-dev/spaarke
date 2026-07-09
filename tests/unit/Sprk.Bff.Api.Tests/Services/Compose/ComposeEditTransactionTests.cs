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
/// pure, stateless wrapper logic (ADR-013 facade boundary + ADR-010 minimalism). Every test
/// below exercises the REAL <see cref="ComposeEditValidator"/> (FR-19) and
/// <see cref="ComposeEditBatch"/> (FR-20) to produce the transaction's inputs and outputs — no
/// hand-crafted verdicts, no <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration tests, no
/// ctor null-check tests (all banned per ADR-038 / <c>tests/CLAUDE.md</c>). This proves genuine
/// FR-19 → FR-20 → FR-21 composition end to end, exactly as a real caller will use it.
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

    private static readonly ComposeEditValidator Validator = new();
    private static readonly ComposeEditTransaction Sut = new(new ComposeEditBatch());

    // ── Acceptance criterion 1 — all edits valid and apply: commits transformed document ────

    [Fact]
    public void Execute_AllEditsValid_CommitsTransformedDocument()
    {
        var edits = new[]
        {
            new ProposedEdit("pay Rent monthly", "pay the Rent on the first day of each month", MatchMode.Strict),
            new ProposedEdit("maintain the Premises", "keep the Premises in good repair", MatchMode.Strict),
        };
        var validation = Validator.Validate(Document, edits);
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
            new ProposedEdit("pay Rent monthly", "pay the Rent on the first day of each month", MatchMode.Strict),
            new ProposedEdit("maintain the Premises", "keep the Premises in good repair", MatchMode.Strict),
            new ProposedEdit("indemnify the Guarantor", "hold harmless", MatchMode.Strict), // NOT in the document
        };
        var validation = Validator.Validate(Document, edits);
        validation.IsValid.Should().BeFalse("edit index 2's target_text does not exist in the document");

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
        var edits = new[]
        {
            new ProposedEdit("indemnify the Guarantor", "hold harmless", MatchMode.Strict),   // not in document
            new ProposedEdit("terminate for cause", "terminate for convenience", MatchMode.Strict), // not in document
        };
        var validation = Validator.Validate(Document, edits);
        validation.IsValid.Should().BeFalse();

        var result = Sut.Execute(Document, edits, validation);

        result.Committed.Should().BeFalse();
        result.DocumentText.Should().Be(Document);
        result.Batch.ValidationErrors.Should().HaveCount(2, "every failing edit must be enumerated in the failure report");
        result.Batch.ValidationErrors.Should().OnlyContain(e => e.Kind == EditErrorKind.NoMatch);
    }

    // ── Acceptance criterion 4 — empty batch commits the unchanged document (NEGATIVE) ───────

    [Fact]
    public void Execute_EmptyBatch_CommitsUnchangedDocument()
    {
        var edits = Array.Empty<ProposedEdit>();
        var validation = Validator.Validate(Document, edits);

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
        var edits = new[]
        {
            new ProposedEdit("pay Rent monthly", "pay the Rent promptly", MatchMode.Strict),
        };
        var validation = Validator.Validate(Document, edits);
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
        var edits = new[]
        {
            new ProposedEdit("indemnify the Guarantor", "hold harmless", MatchMode.Strict), // not in document -> fatal
        };
        var validation = Validator.Validate(Document, edits);
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
            new ProposedEdit("shall maintain the Premises", "shall keep the Premises tidy", MatchMode.Strict),
            new ProposedEdit("maintain the Premises", "repair the Premises", MatchMode.Strict),
        };
        var validation = Validator.Validate(Document, edits);

        var result = Sut.Execute(Document, edits, validation);

        result.Committed.Should().BeTrue("within-batch overlap is NON-FATAL — must not be conflated with the transaction's fatal rollback path");
        result.Batch.Applied.Should().ContainSingle();
        result.Batch.Skipped.Should().ContainSingle();
    }
}
