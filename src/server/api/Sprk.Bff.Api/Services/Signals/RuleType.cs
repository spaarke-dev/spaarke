namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The closed set of <c>sprk_policyversion.sprk_ruletype</c> values (spec FR-05; design.md CM-7;
/// component model decision 7).
/// </summary>
/// <remarks>
/// <para>
/// <b>Mirrors the Dataverse choice column exactly</b> — <c>sprk_policyversion.sprk_ruletype</c> is a Choice
/// column with options <c>Threshold</c> (100000000), <c>Switch</c> (100000001), <c>Existence</c> (100000002)
/// (confirmed via <c>DESCRIBE TABLE sprk_policyversion</c>, 2026-10-03). The three numeric values below are
/// the Dataverse option-set values, not arbitrary — do not renumber.
/// </para>
/// <para>
/// <b>Closed by design, not data-driven.</b> Per design.md CM-7/CM-8 and project CLAUDE.md §3.4, this is
/// deliberately a plain C# enum rather than a lookup table or a generic "rule type registry" — adding a
/// fourth member (e.g. <c>Transition</c> or <c>Trend</c>, both explicitly deferred) is a code change with
/// review, never a configuration row. <see cref="RuleBodySchemaValidator"/> switches on this enum exhaustively;
/// extending the set means extending that switch, not adding data.
/// </para>
/// <para>
/// <b>Grouped by domain (ebilling-r1 task 070, spec FR-24, D-23 / D-27).</b> The members below
/// <see cref="Existence"/> are the eleven billing rule types of design.md 6.1, in FR-24 order, with option values
/// 100000003 to 100000013: catalogue position N (FR-26's "1, 2, 6-10") is option value 100000002 + N. Which domain
/// a member belongs to is <see cref="RuleDomains.DomainOf"/>, a switch with a throwing default (an unmapped new member compiles
/// and throws at runtime; a test pins every member's domain). An Ontology consumer
/// (<see cref="PredicateCompiler"/>, <see cref="PolicyVersionValidator.TryPrepareForEvaluation"/>, the nightly job)
/// refuses every Billing member; the billing gate (<see cref="PolicyVersionValidator.TryPrepareBillingRule"/>)
/// refuses every Ontology member. Mirrors the same Dataverse choice: do not renumber.
/// </para>
/// </remarks>
public enum RuleType
{
    /// <summary>A numeric comparison against a computed/field value (e.g. budget utilization ratio).</summary>
    Threshold = 100000000,

    /// <summary>A boolean kill-switch, e.g. suppressing a signal type tenant-wide.</summary>
    Switch = 100000001,

    /// <summary>
    /// An EXISTS / NOT EXISTS assertion over a related entity, expressed as independent ANDed clauses with
    /// no cross-clause variable binding (design.md §8.0.1(a); CM-7, added 2026-09-30). Without this type the
    /// project's one differentiated predicate — a classified communication AND no budget revision in the
    /// same window — has no body shape it can be saved as.
    /// </summary>
    Existence = 100000002,

    // ── Billing domain (ebilling-r1 task 070; design.md 6.1, FR-24 order) ─────────────────────────────────────

    /// <summary>1. The line's timekeeper is approved for the firm on the line date (the roster has no core-record scope). No parameters.</summary>
    ApprovedTimekeeper = 100000003,

    /// <summary>2. Billed rate is at most the approved rate for the timekeeper or classification, plus a tolerance.</summary>
    ApprovedRate = 100000004,

    /// <summary>3. Hours are a multiple of the billing increment.</summary>
    TimeIncrement = 100000005,

    /// <summary>4. Hours per timekeeper per day are at most a cap (within the invoice in R1).</summary>
    DailyHoursCap = 100000006,

    /// <summary>5. The line's activity / task code is not in a list of non-billable codes.</summary>
    NonBillableActivity = 100000007,

    /// <summary>6. An expense code is not reimbursable, or an expense item is over a cap.</summary>
    ExpenseRule = 100000008,

    /// <summary>7. The line date is not more than N days before the invoice date.</summary>
    InvoiceAge = 100000009,

    /// <summary>8. The line date is inside the core record's open / close dates (plus grace days).</summary>
    CoreRecordWindow = 100000010,

    /// <summary>9. The invoice number or a line is not a duplicate on the configured match fields (D-88).</summary>
    Duplicate = 100000011,

    /// <summary>10. Required header or line data is present.</summary>
    RequiredData = 100000012,

    /// <summary>11. An AI observation (block billing, vague description) is present at or above a confidence. Flag only (I-5).</summary>
    AiObservation = 100000013,
}
