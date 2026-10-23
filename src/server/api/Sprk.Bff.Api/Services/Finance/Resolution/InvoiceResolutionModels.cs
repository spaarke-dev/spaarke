using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Finance.Resolution;

// Input and output of Processing Pipeline step 2 (FR-23, D-47), see IInvoiceResolutionService.
// The input is this task's own record (task 043 is not a dependency): task 049 maps the extracted invoice into it.

/// <summary>The names used as keys of the per-field confidence dictionaries (they follow the FR-21 / task 043 field names).</summary>
public static class ResolutionFieldNames
{
    public const string FeesTotal = "FeesTotal";
    public const string ExpensesTotal = "ExpensesTotal";
    public const string AdjustmentsTotal = "AdjustmentsTotal";
    public const string TotalDue = "TotalDue";

    public const string Date = "Date";
    public const string TimekeeperAsBilled = "TimekeeperAsBilled";
    public const string Hours = "Hours";
    public const string Rate = "Rate";
    public const string Quantity = "Quantity";
    public const string UnitCost = "UnitCost";
    public const string Amount = "Amount";
}

/// <summary>The kind of core record an invoice is filed under (the three that can carry a rate card scope).</summary>
public enum CoreRecordKind
{
    Matter,
    Project,
    WorkAssignment,
}

/// <summary>The core record of the submission context (task 044), never read from extracted text.</summary>
public sealed record CoreRecordReference(CoreRecordKind Kind, Guid Id);

/// <summary>What the extraction reported about a value that has no resolution of its own: confidences only.</summary>
/// <param name="AnalyzerConfidence">The analyzer's confidence for the source span, 0 to 1; <c>null</c> = none returned (counts as 0).</param>
/// <param name="SelfReportConfidence">The model's own confidence, 0 to 1, when it gave one. A minor input (D-47).</param>
public sealed record FieldReading(decimal? AnalyzerConfidence, decimal? SelfReportConfidence = null);

/// <summary>A value as read, with the confidences that came with it. <typeparamref name="T"/> is <c>decimal?</c>, <c>DateOnly?</c> or <c>string?</c>.</summary>
public sealed record ResolutionField<T>(T Value, decimal? AnalyzerConfidence = null, decimal? SelfReportConfidence = null)
{
    internal FieldReading Reading => new(AnalyzerConfidence, SelfReportConfidence);
}

/// <summary>The header values resolution checks arithmetically, plus the confidences of every other header field.</summary>
public sealed record InvoiceResolutionHeader
{
    public ResolutionField<decimal?> FeesTotal { get; init; } = new(null);

    public ResolutionField<decimal?> ExpensesTotal { get; init; } = new(null);

    /// <summary>Negative for a reduction.</summary>
    public ResolutionField<decimal?> AdjustmentsTotal { get; init; } = new(null);

    public ResolutionField<decimal?> TotalDue { get; init; } = new(null);

    /// <summary>Confidences of the other header fields (invoice number, dates, PO, currency ...), keyed by field name. Their combined confidence is returned too.</summary>
    public IReadOnlyDictionary<string, FieldReading> OtherFields { get; init; } = new Dictionary<string, FieldReading>();
}

/// <summary>One fee or expense row as read. A fee row uses hours and rate; an expense row uses quantity and unit cost.</summary>
public sealed record InvoiceResolutionLine
{
    public required InvoiceLineType LineType { get; init; }

    public ResolutionField<DateOnly?> Date { get; init; } = new(null);

    /// <summary>The timekeeper exactly as the line prints it (a full name, "Last, First", initials ...). Fee rows only.</summary>
    public ResolutionField<string?> TimekeeperAsBilled { get; init; } = new(null);

    public ResolutionField<decimal?> Hours { get; init; } = new(null);

    public ResolutionField<decimal?> Rate { get; init; } = new(null);

    public ResolutionField<decimal?> Quantity { get; init; } = new(null);

    public ResolutionField<decimal?> UnitCost { get; init; } = new(null);

    public ResolutionField<decimal?> Amount { get; init; } = new(null);

    /// <summary>Confidences of the other fields of the row (title, narrative, codes ...), keyed by field name.</summary>
    public IReadOnlyDictionary<string, FieldReading> OtherFields { get; init; } = new Dictionary<string, FieldReading>();
}

/// <summary>Everything resolution needs: the submission context (firm, core record) and what was read.</summary>
public sealed record InvoiceResolutionInput
{
    /// <summary>The firm (<c>sprk_organization</c>) from the submission context. A timekeeper or rate card of any other firm is never used.</summary>
    public required Guid FirmId { get; init; }

    /// <summary>The core record of the submission; <c>null</c> = none, so only firm-wide rate cards apply.</summary>
    public CoreRecordReference? CoreRecord { get; init; }

    public required InvoiceResolutionHeader Header { get; init; }

    public required IReadOnlyList<InvoiceResolutionLine> Lines { get; init; }

    /// <summary>
    /// The full names printed in the invoice's own timekeeper summary table. They are the only source in the invoice that expands a
    /// line label that is initials only ("DL", "Lockhart, D.") into a full name; nothing is guessed from the roster.
    /// </summary>
    public IReadOnlyList<string> TimekeeperSummaryNames { get; init; } = [];
}

// ── Output ──────────────────────────────────────────────────────────────────────────────────────

/// <summary>Result of the arithmetic checks for one field. "Any failed check wins".</summary>
public enum ArithmeticOutcome
{
    /// <summary>No check involved this field (or the check lacked a value to run).</summary>
    NotApplicable,

    Passed,

    Failed,
}

/// <summary>Result of matching a field against reference data.</summary>
public enum ReferenceOutcome
{
    /// <summary>The field has no reference data to be matched against.</summary>
    NotApplicable,

    Matched,

    Missing,
}

/// <summary>The caps (D-47) that were in force for a field: its arithmetic failed, or its reference match is missing.</summary>
[Flags]
public enum ConfidenceCaps
{
    None = 0,
    ArithmeticFail = 1,
    ReferenceMissing = 2,
}

/// <summary>
/// The four signals of one field, as they went into <see cref="CombinedConfidenceCalculator"/>, plus the combined confidence (D-47).
/// Kept per field so the calibration report (task 161) can see which signal moved a score.
/// </summary>
public sealed record FieldConfidence
{
    /// <summary>The combined confidence, 0 to 1. This is the only score that gates rejection (FR-26).</summary>
    public required decimal Combined { get; init; }

    /// <summary>The analyzer signal after clamping to 0..1 (a missing confidence is 0).</summary>
    public required decimal AnalyzerSignal { get; init; }

    /// <summary>The self-report signal after clamping to 0..1 and to the analyzer signal; <c>null</c> when none was given.</summary>
    public decimal? SelfReportSignal { get; init; }

    public required ArithmeticOutcome Arithmetic { get; init; }

    public required ReferenceOutcome Reference { get; init; }

    /// <summary>The strength of the reference match (1 for an exact or normalized name, the similarity for an AI-assisted one); <c>null</c> unless matched.</summary>
    public decimal? ReferenceStrength { get; init; }

    public ConfidenceCaps CapsInForce { get; init; }
}

public enum TimekeeperMatchStage
{
    None,
    Exact,
    Normalized,
    AiAssisted,
}

/// <summary>Why a fee line's timekeeper stayed unmatched. Every value produces an ApprovedTimekeeper finding input.</summary>
public enum TimekeeperUnmatchedReason
{
    None,

    /// <summary>The line prints no timekeeper.</summary>
    NameMissing,

    /// <summary>The line has no date, so "approved on the line date" cannot be decided.</summary>
    LineDateMissing,

    /// <summary>The line prints initials only (or a single word) and the invoice's timekeeper summary does not explain them, so there is no name to match.</summary>
    NameIncomplete,

    /// <summary>No timekeeper of the firm has this name (the name may be misread, or the person may be unknown).</summary>
    NotOnRoster,

    /// <summary>A timekeeper of the firm has this name but is not Approved and active on the line date (Pending, Inactive or outside the effective dates).</summary>
    NotApprovedOnLineDate,

    /// <summary>More than one approved timekeeper fits the name, or the initials fit more than one summary name. None is picked.</summary>
    Ambiguous,

    /// <summary>The best AI-assisted candidate is below the reliability threshold.</summary>
    AiBelowThreshold,

    /// <summary>The best AI-assisted candidate is at or above the threshold but not clearly ahead of the second.</summary>
    AiNotClearlyAhead,

    /// <summary>The AI-assisted step could not run (AI off, or the embedding call failed). The line stays unmatched.</summary>
    AiUnavailable,
}

/// <summary>The roster match of one fee line (FR-23).</summary>
public sealed record TimekeeperResolution
{
    /// <summary>The firm's Approved timekeeper active on the line date, or <c>null</c> = unmatched. Never a timekeeper of another firm.</summary>
    public Guid? TimekeeperId { get; init; }

    public bool IsMatched => TimekeeperId.HasValue;

    public TimekeeperMatchStage Stage { get; init; }

    /// <summary>1 for an exact or normalized match, the embedding similarity for an AI-assisted one, 0 when unmatched.</summary>
    public decimal MatchConfidence { get; init; }

    public TimekeeperUnmatchedReason Reason { get; init; }

    /// <summary>The matched timekeeper's classification (<c>sprk_timekeeperrole</c> option value), when it has one.</summary>
    public int? Classification { get; init; }
}

public enum RateResolutionStatus
{
    /// <summary>The line is not a fee line, so no hourly rate applies.</summary>
    NotApplicable,

    Found,

    /// <summary>No timekeeper, date or Published rate in force: a reference-missing signal, not an error.</summary>
    NotFound,

    /// <summary>Several rates at the winning precedence tier disagree. None is picked.</summary>
    Ambiguous,
}

public enum RateNotFoundReason
{
    None,
    TimekeeperUnmatched,
    LineDateMissing,
    NoRateInForce,
}

public enum RateCardScope
{
    /// <summary>A card with no core record scope.</summary>
    Firm,

    /// <summary>A card scoped to the submission's core record.</summary>
    CoreRecord,
}

public enum RateBasis
{
    /// <summary>The rate row names this timekeeper.</summary>
    Timekeeper,

    /// <summary>The rate row names the timekeeper's classification.</summary>
    Classification,
}

/// <summary>The rate in force on a fee line's date (FR-23, FR-08).</summary>
public sealed record RateResolution
{
    public RateResolutionStatus Status { get; init; }

    public RateNotFoundReason Reason { get; init; }

    public decimal? ApprovedRate { get; init; }

    public decimal? RackRate { get; init; }

    public Guid? RateCardId { get; init; }

    public Guid? RateId { get; init; }

    public RateCardScope? Scope { get; init; }

    public RateBasis? Basis { get; init; }
}

public enum ArithmeticCheckKind
{
    /// <summary>Hours x rate (fee) or quantity x unit cost (expense) = amount.</summary>
    LineProduct,

    /// <summary>The fee lines add up to the fees total.</summary>
    FeesSection,

    /// <summary>The expense lines add up to the expenses total.</summary>
    ExpensesSection,

    /// <summary>Fees + expenses + adjustments = total due.</summary>
    TotalDue,
}

/// <summary>A field of the header (<see cref="LineIndex"/> null) or of a line.</summary>
public readonly record struct FieldRef(int? LineIndex, string Field);

/// <summary>One arithmetic check and the fields it involved.</summary>
public sealed record ArithmeticCheckResult(
    ArithmeticCheckKind Kind,
    bool Passed,
    decimal Expected,
    decimal Actual,
    IReadOnlyList<FieldRef> Fields);

/// <summary>An unmatched fee line, ready for the ApprovedTimekeeper rule (task 071).</summary>
public sealed record ApprovedTimekeeperFindingInput(
    int LineIndex,
    string? BilledName,
    DateOnly? LineDate,
    TimekeeperUnmatchedReason Reason);

/// <summary>One line after resolution. <see cref="LineIndex"/> is its position in the input.</summary>
public sealed record ResolvedInvoiceLine
{
    public required int LineIndex { get; init; }

    public required InvoiceLineType LineType { get; init; }

    /// <summary>The roster match of a fee line; <c>null</c> for an expense line.</summary>
    public TimekeeperResolution? Timekeeper { get; init; }

    public required RateResolution Rate { get; init; }

    /// <summary>The combined confidence of each field of the row, keyed by <see cref="ResolutionFieldNames"/> and the <c>OtherFields</c> keys.</summary>
    public required IReadOnlyDictionary<string, FieldConfidence> Fields { get; init; }
}

/// <summary>The output of <see cref="IInvoiceResolutionService.ResolveAsync"/>.</summary>
public sealed record InvoiceResolutionResult
{
    /// <summary>
    /// The combined confidence of each header field. Every field of the input has an entry; one the extraction did not return at all
    /// (no value, no confidence) scores 0 (fail closed), so a caller that needs to tell "absent" from "low confidence" reads the value.
    /// </summary>
    public required IReadOnlyDictionary<string, FieldConfidence> HeaderFields { get; init; }

    /// <summary>One entry per input line, in input order.</summary>
    public required IReadOnlyList<ResolvedInvoiceLine> Lines { get; init; }

    /// <summary>Every arithmetic check that ran, with its fields.</summary>
    public required IReadOnlyList<ArithmeticCheckResult> ArithmeticChecks { get; init; }

    /// <summary>One entry per unmatched fee line.</summary>
    public required IReadOnlyList<ApprovedTimekeeperFindingInput> ApprovedTimekeeperFindingInputs { get; init; }
}
