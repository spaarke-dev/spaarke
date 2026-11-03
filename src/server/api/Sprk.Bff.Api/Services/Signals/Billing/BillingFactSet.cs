using System.Collections.Frozen;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;

namespace Sprk.Bff.Api.Services.Signals.Billing;

// The in-memory invoice a billing rule handler judges (ebilling-r1 task 071; spec FR-24, FR-25, D-23). Task 072 assembles one per
// submission from the resolution output (046), the coding output (047) and the reference data (017 / 018 / 019); the handlers only
// read it. Everything a handler needs is in here, including the evaluation inputs a clock or a query would otherwise supply (the
// reliability threshold), so a handler needs no I/O and the same fact set always gives the same findings.
//
// Reuse (CLAUDE.md section 11): the roster match, the approved-rate lookup and the field references are 046's own types
// (TimekeeperResolution, RateResolution, FieldRef); an AI observation is the IInvoiceAi contract's InvoiceLineObservation. The
// handlers do not repeat the roster or rate-card logic: they judge what 046 resolved.

/// <summary>
/// The names that key the per-field confidence dictionaries and the <see cref="FieldRef"/> of a finding's read fields. The line
/// names that 046 already defines are re-exported from <see cref="ResolutionFieldNames"/>, so there is one spelling; task 049 /
/// 072 map the combined confidence of each field (046) and the coding confidence (047) into these keys.
/// </summary>
public static class BillingFieldNames
{
    // Header (046 returns these in HeaderFields; the "other" ones are keyed by the caller, task 049, with these spellings).
    public const string FirmInvoiceNumber = "FirmInvoiceNumber";
    public const string InvoiceDate = "InvoiceDate";
    public const string BillingPeriodStart = "BillingPeriodStart";
    public const string BillingPeriodEnd = "BillingPeriodEnd";
    public const string Currency = "Currency";

    // Line, as 046 names them.
    public const string Date = ResolutionFieldNames.Date;
    public const string TimekeeperAsBilled = ResolutionFieldNames.TimekeeperAsBilled;
    public const string Hours = ResolutionFieldNames.Hours;
    public const string Rate = ResolutionFieldNames.Rate;
    public const string Amount = ResolutionFieldNames.Amount;

    // Line, new in 071.
    public const string Narrative = "Narrative";

    /// <summary>The AI-assigned task code (the coding confidence, 047). A rule that reads it carries that confidence (NFR-05).</summary>
    public const string TaskCode = "TaskCode";

    /// <summary>The AI-assigned activity code (the coding confidence, 047).</summary>
    public const string ActivityCode = "ActivityCode";

    /// <summary>The AI-assigned expense code (the coding confidence, 047).</summary>
    public const string ExpenseCode = "ExpenseCode";

    /// <summary>The task code as the firm billed it (an extracted field).</summary>
    public const string BilledTaskCode = "BilledTaskCode";

    public const string BilledActivityCode = "BilledActivityCode";

    public const string BilledExpenseCode = "BilledExpenseCode";

    /// <summary>The prefix of every AI observation field: the whole family is AI-assigned (<see cref="IsAiAssigned"/>).</summary>
    public const string ObservationPrefix = "Observation.";

    /// <summary>An AI observation of block billing; its confidence is the observation's own.</summary>
    public const string ObservationBlockBilling = ObservationPrefix + "BlockBilling";

    public const string ObservationVagueDescription = ObservationPrefix + "VagueDescription";

    /// <summary>
    /// The fields whose value the AI assigned rather than read from the invoice (073 review F1-a): the coding Action's task, activity
    /// and expense codes (047) and, by <see cref="ObservationPrefix"/>, every AI observation. Their confidence is the model's own
    /// report, not a D-47 combined score, so a finding that read one can never reject a submission (I-5); it holds as a Flag.
    /// The firm-billed codes (<see cref="BilledTaskCode"/> and the others) are extracted fields and are not in this set.
    /// </summary>
    public static IReadOnlySet<string> AiAssigned { get; } =
        new[] { TaskCode, ActivityCode, ExpenseCode }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>True for a field in <see cref="AiAssigned"/> or under <see cref="ObservationPrefix"/> (ordinal).</summary>
    public static bool IsAiAssigned(string? field) =>
        field is not null && (AiAssigned.Contains(field) || field.StartsWith(ObservationPrefix, StringComparison.Ordinal));
}

/// <summary>Everything one billing rule is judged against: one submission, its lines and the reference data they need.</summary>
public sealed record BillingFactSet
{
    public required BillingInvoiceFacts Invoice { get; init; }

    /// <summary>The invoice's lines in invoice order. A finding names a line by <see cref="BillingLineFacts.Index"/>.</summary>
    public required IReadOnlyList<BillingLineFacts> Lines { get; init; }

    /// <summary>
    /// The core record's dates and state, or <c>null</c> when none was loaded. A rule that needs it (CoreRecordWindow) judges
    /// nothing without it; task 072 does not call it for a submission with no core record.
    /// </summary>
    public BillingCoreRecordFacts? CoreRecord { get; init; }

    /// <summary>
    /// The firm's earlier submissions that Duplicate compares against (task 072 chooses and loads them: this firm, this
    /// submission excluded). Attempts that never produced a payable invoice and are not Rejected or Returned (a Failed
    /// processing run, a Cancelled invoice) should not be loaded at all.
    /// <para><b>Priors come from both sides of the secure wall (D-112).</b> Restricting the load to the invoice's own side would
    /// let double billing across the wall go unflagged, so 072 loads secure and ordinary priors and sets each prior's
    /// <see cref="BillingPriorSubmission.SecureRoots"/> (and the invoice's <see cref="BillingInvoiceFacts.SecureRoots"/>) app-only
    /// from <c>SecureChildShareSynchronizer</c>'s secure-root walk. The walk starts from CHILD rows, never a root table (it throws for
    /// a root table such as <c>sprk_matter</c>, so the core record is never passed itself): a prior's <c>sprk_invoice</c> and
    /// <c>sprk_invoicesubmission</c> rows, their sets unioned, and the judged invoice as an unsaved <c>sprk_invoice</c> probe carrying
    /// the core lookup. The walk's outcomes are mapped by uac-r2's calling conditions: Failed (an ambiguous Secure owner team) and a
    /// fault leave every set unresolved; NotApplicable (no Secure owner team) gives every set empty; an Undetermined row, or a
    /// Secure-owned row whose lookups reach no secure root, leaves its side unresolved. Duplicate then shows a
    /// prior's tracking number and line detail only when both sets are resolved and the prior's roots are a subset of the
    /// invoice's; in every other case the finding still fires but withholds them (fail closed). 071 does not call the
    /// synchronizer.</para>
    /// </summary>
    public IReadOnlyList<BillingPriorSubmission> PriorSubmissions { get; init; } = [];

    /// <summary>
    /// The reliability threshold (spec FR-26, default 0.90, the eBilling settings record). A handler uses it only to mark a
    /// read value as below it (NFR-05); it never decides a rejection with it (task 073 does).
    /// </summary>
    public required decimal ReliabilityThreshold { get; init; }
}

/// <summary>The header of the submission.</summary>
public sealed record BillingInvoiceFacts
{
    /// <summary>
    /// This submission's id, so Duplicate never counts the submission against itself. Required: a fact set without it would
    /// compare the submission against its own earlier attempt row and flag it as its own duplicate (task 072 always has the id).
    /// </summary>
    public required Guid SubmissionId { get; init; }

    /// <summary>
    /// The ids of the secure roots above this submission (D-112), walked from the child rows (see <see cref="BillingFactSet.PriorSubmissions"/>):
    /// every Secure record is its own wall. An ordinary
    /// record has the empty set. <c>null</c> = the set could not be read (unresolved), which Duplicate treats as unknown and fails
    /// closed on: no prior's detail is shown. Required, so a caller states which of the three it means.
    /// </summary>
    public required IReadOnlySet<Guid>? SecureRoots { get; init; }

    /// <summary>This submission's tracking number (<c>{invoice}.{n}</c>, task 045), when allocated.</summary>
    public string? TrackingNumber { get; init; }

    public string? FirmInvoiceNumber { get; init; }

    public DateOnly? InvoiceDate { get; init; }

    public DateOnly? BillingPeriodStart { get; init; }

    public DateOnly? BillingPeriodEnd { get; init; }

    /// <summary>The currency as read (an ISO code). R1 is USD only (D-20).</summary>
    public string? Currency { get; init; }

    /// <summary>
    /// The combined confidence (046) of each header field, keyed by <see cref="BillingFieldNames"/>. A missing key means no
    /// confidence is known: the value counts as below the reliability threshold.
    /// </summary>
    public IReadOnlyDictionary<string, decimal> FieldConfidence { get; init; } = EmptyConfidence.Instance;
}

/// <summary>One fee or expense line, with what 046 resolved for it and the codes and observations 047 produced.</summary>
public sealed record BillingLineFacts
{
    /// <summary>The position of the line in the invoice, the same index 046 uses.</summary>
    public required int Index { get; init; }

    public required InvoiceLineType LineType { get; init; }

    public DateOnly? Date { get; init; }

    /// <summary>The timekeeper exactly as the line prints it.</summary>
    public string? TimekeeperName { get; init; }

    /// <summary>The roster match of a fee line (046). <c>null</c> on a fee line is treated as unmatched.</summary>
    public TimekeeperResolution? Timekeeper { get; init; }

    /// <summary>The approved rate in force on the line date (046, fee lines).</summary>
    public RateResolution? ApprovedRate { get; init; }

    public decimal? Hours { get; init; }

    /// <summary>The hourly rate the line bills (an extracted field; fee lines).</summary>
    public decimal? BilledRate { get; init; }

    public decimal? Amount { get; init; }

    public string? Narrative { get; init; }

    /// <summary>The AI-assigned task code, validated against the line's code set (047). Its confidence is the coding confidence.</summary>
    public string? TaskCode { get; init; }

    /// <summary>The AI-assigned activity code (047).</summary>
    public string? ActivityCode { get; init; }

    /// <summary>The AI-assigned expense code (047).</summary>
    public string? ExpenseCode { get; init; }

    /// <summary>The task code as the firm billed it. RequiredData reads the firm's codes; NonBillableActivity reads the AI's.</summary>
    public string? BilledTaskCode { get; init; }

    public string? BilledActivityCode { get; init; }

    public string? BilledExpenseCode { get; init; }

    /// <summary>The AI observations on the line (047): block billing, vague description, each with its own confidence.</summary>
    public IReadOnlyList<InvoiceLineObservation> Observations { get; init; } = [];

    /// <summary>
    /// The combined confidence (046) of each extracted field of the line, plus the coding confidence of the AI codes (047),
    /// keyed by <see cref="BillingFieldNames"/>. A missing key means no confidence is known.
    /// </summary>
    public IReadOnlyDictionary<string, decimal> FieldConfidence { get; init; } = EmptyConfidence.Instance;
}

/// <summary>The core record's reference data for CoreRecordWindow (spec FR-24).</summary>
public sealed record BillingCoreRecordFacts
{
    /// <summary><c>sprk_openeddate</c> (sprk_matter, sprk_project and sprk_workassignment).</summary>
    public DateOnly? OpenDate { get; init; }

    /// <summary><c>sprk_closeddate</c> (sprk_matter, sprk_project and sprk_workassignment).</summary>
    public DateOnly? CloseDate { get; init; }

    /// <summary>The core record is closed (inactive, <c>statecode</c>), whether or not it has a close date. Read only when
    /// <see cref="BillingStatus"/> is empty (D-111); a billing status decides on its own (D-116).</summary>
    public bool IsClosed { get; init; }

    /// <summary>
    /// <c>sprk_billingstatus</c> (D-116): one of <see cref="CoreRecordBillingStatus"/>, or <c>null</c> when the row has none (it existed
    /// before the column), in which case D-111 applies. A value outside the set is treated as none.
    /// </summary>
    public int? BillingStatus { get; init; }
}

/// <summary>
/// The values of the global choice <c>sprk_billingstatus</c> on sprk_matter, sprk_project and sprk_workassignment (D-116; verified
/// live on spaarkedev1 2026-10-18; form default Open, not required). The one place the option values are named: the labels and values
/// may change, so code refers to them only through these constants.
/// </summary>
public static class CoreRecordBillingStatus
{
    /// <summary>Open: billing is allowed. Only the opened date is checked; the close date and the closed state are ignored.</summary>
    public const int Open = 100000000;

    /// <summary>Final: only work dated on or before the close date is billable; with no close date nothing is (fail safe).</summary>
    public const int Final = 100000001;

    /// <summary>Closed: closed to billing; every new charge line is flagged.</summary>
    public const int Closed = 100000002;

    /// <summary>The three values.</summary>
    public static IReadOnlySet<int> All { get; } = new[] { Open, Final, Closed }.ToFrozenSet();

    /// <summary>The value's label as text for a finding's reference value, or <c>null</c> when there is none.</summary>
    internal static string? Label(int? value) => value switch
    {
        null => null,
        Open => "Open",
        Final => "Final",
        Closed => "Closed",
        _ => value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

/// <summary>What an earlier attempt came to, as far as Duplicate cares (spec FR-36, D-37).</summary>
public enum BillingPriorStatus
{
    /// <summary>In progress, accepted, approved, sent to AP or paid: the firm's number is in use.</summary>
    Active,

    /// <summary>Rejected (by the rules or by a reviewer). Duplicate ignores it.</summary>
    Rejected,

    /// <summary>Returned to the firm. Duplicate ignores it.</summary>
    ReturnedToFirm,
}

/// <summary>One earlier submission by the firm, with its lines.</summary>
public sealed record BillingPriorSubmission
{
    public required Guid SubmissionId { get; init; }

    /// <summary>The tracking number (<c>{invoice}.{n}</c>), cited in a Duplicate finding unless the prior is across the wall (D-112).</summary>
    public required string TrackingNumber { get; init; }

    /// <summary>The attempt number <c>n</c>; the lowest active one is the "original" a duplicate is reported against.</summary>
    public int Sequence { get; init; }

    public string? FirmInvoiceNumber { get; init; }

    /// <summary>
    /// The earlier <c>sprk_invoice</c> this submission belongs to (the submission's invoice lookup); <c>null</c> when it has none.
    /// A visible Duplicate finding carries it as the <c>duplicateOf.invoiceId</c> evidence reference (D-176 / #2118), so a reader can test
    /// its access to the earlier invoice. Evidence only: never a template fact, never in the fact snapshot.
    /// </summary>
    public Guid? InvoiceId { get; init; }

    public BillingPriorStatus Status { get; init; }

    /// <summary>
    /// The secure roots above the prior's invoice and submission rows (D-112, walked from those child rows, never from a root table);
    /// the empty set for an ordinary record; <c>null</c> = unresolved.
    /// Its tracking number and line detail are shown on a Duplicate finding only when this set and the invoice's are both
    /// resolved and this one is a subset of the invoice's. Required, so a caller states which of the three it means.
    /// </summary>
    public required IReadOnlySet<Guid>? SecureRoots { get; init; }

    public IReadOnlyList<BillingPriorLine> Lines { get; init; } = [];
}

/// <summary>The fields of an earlier line that Duplicate can compare.</summary>
public sealed record BillingPriorLine
{
    public DateOnly? Date { get; init; }

    /// <summary>The timekeeper as billed.</summary>
    public string? TimekeeperName { get; init; }

    /// <summary>The roster timekeeper the line was resolved to, when it was.</summary>
    public Guid? TimekeeperId { get; init; }

    public decimal? Hours { get; init; }

    public string? Narrative { get; init; }

    public decimal? Amount { get; init; }
}

internal static class EmptyConfidence
{
    internal static readonly IReadOnlyDictionary<string, decimal> Instance = new Dictionary<string, decimal>();
}
