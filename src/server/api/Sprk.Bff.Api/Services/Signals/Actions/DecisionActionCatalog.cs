using System.Collections.Frozen;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Signals.Actions;

/// <summary>The two worklist lanes. Values are the live <c>sprk_signal.sprk_lane</c> option-set values.</summary>
public enum DecisionLane
{
    Decide = 100000000,
    Do = 100000001,
}

/// <summary>
/// The class of Decision Record an action produces when taken. Derived from the catalog, never chosen by the
/// user (reconciliation R-2; spec A-2). A <i>Dismissal</i> record (nothing taken) is not an action's class, so it
/// is not a member here. Any Next step on a decision makes the record <see cref="Judgement"/> (R-2).
/// </summary>
public enum DecisionRecordClass
{
    Routine,
    Judgement,
}

/// <summary>The input kinds a decision wizard step can render for an action parameter.</summary>
public enum DecisionParameterKind
{
    Text,
    TextArea,
    Choice,
    Money,
    Date,
    Person,
    Lookup,
}

/// <summary>
/// One input the wizard collects for an action. <see cref="LookupEntity"/> names the table when
/// <see cref="Kind"/> is <see cref="DecisionParameterKind.Lookup"/>. A <see cref="DecisionParameterKind.Choice"/> carries
/// EITHER a fixed <see cref="Options"/> list OR a named <see cref="OptionsSource"/> the wizard reads (never neither).
/// </summary>
/// <param name="OptionsSource">Task 167 (D-193): a named source the plan route resolves into <see cref="Options"/> from live data
/// (<c>metadata:{table}.{column}</c> for a picklist's current labels, <c>currency:active</c>), one resolver for every action
/// (<see cref="DecisionParameterSources"/>), so an owner's relabel shows up and nothing is hard-coded.</param>
/// <param name="DefaultSource">Task 167 (D-193): a named source of the parameter's starting value, resolved by the plan route (the
/// matter's business-unit currency). The wizard shows it as prefilled; the commit route resolves an omitted value to the same source.</param>
/// <param name="NotBefore">Task 167 (D-193): for a Date, the code of another Date of the same step it must not be earlier than.
/// The wizard flags it and the executor refuses it; equal dates are fine.</param>
/// <param name="MaxLength">Task 167 (D-193 review F4-4): for a Text, the most characters the target column holds. The wizard step flags a longer
/// value before Record decision, and the executor refuses it too. Null (every other parameter) means no declared limit.</param>
public sealed record DecisionActionParameter(
    string Code,
    string Label,
    DecisionParameterKind Kind,
    bool Required,
    string? Hint = null,
    string? LookupEntity = null,
    IReadOnlyList<DecisionParameterOption>? Options = null,
    string? OptionsSource = null,
    string? DefaultSource = null,
    string? NotBefore = null,
    int? MaxLength = null);

/// <summary>One fixed choice: the stable <see cref="Value"/> the commit route receives and the <see cref="Label"/> the
/// user sees.</summary>
public sealed record DecisionParameterOption(string Value, string Label);

/// <summary>
/// One entry of the closed action catalog (spec FR-50; reconciliation S-8, S-9).
/// </summary>
/// <param name="Code">The stable code a decision plan names, e.g. <c>send-budget-inquiry</c>.</param>
/// <param name="Label">The wizard step title.</param>
/// <param name="PlanLane">The lane whose plans may name this code as an action; <c>null</c> when it is a Next-step
/// creator only. (assign-work is a Decide-lane plan action AND a Next step, D-51.)</param>
/// <param name="IsNextStep">True when a plan's Next-steps set may name this code.</param>
/// <param name="WorkType">The Decide-lane work type (ask, fund, chase); <c>null</c> for Do-lane actions, whose work type
/// the rule itself declares, and for Next-step creators.</param>
/// <param name="Parameters">The inputs, in step order.</param>
/// <param name="EffectLines">What taking the action does, one line each; the wizard shows them as "What it does".</param>
/// <param name="Excludes">Codes whose steps are removed when this action is taken before them (v4 Z-4).</param>
/// <param name="RecordClass">The class the action gives the Decision Record.</param>
public sealed record DecisionActionDefinition(
    string Code,
    string Label,
    DecisionLane? PlanLane,
    bool IsNextStep,
    string? WorkType,
    IReadOnlyList<DecisionActionParameter> Parameters,
    IReadOnlyList<string> EffectLines,
    IReadOnlyList<string> Excludes,
    DecisionRecordClass RecordClass)
{
    /// <summary>The declared risk the commit route turns into the record's gate tier (task 043, D-52), through the AI
    /// PublicContracts pure function <see cref="DecisionGateTier"/>. Every catalog action declares one; the commit route refuses
    /// an action that does not rather than defaulting it.</summary>
    public DecisionGateFactors? Gate { get; init; }

    /// <summary>False for an intermediate step that leaves the Work Item open when it is the only thing taken (D-119: the budget
    /// inquiry; D-128 Q18: the Do-lane reminder). The Signal becomes Acknowledged and linked to the record, and the nightly
    /// ConditionCleared closes it. Every other action resolves the Signal when taken.</summary>
    public bool ResolvesSignal { get; init; } = true;

    /// <summary>True for an action that sends a message: the commit route runs those after every Dataverse write and Next step
    /// (D-17, email last), the inquiry last of all.</summary>
    public bool SendsMessage { get; init; }

    /// <summary>True for an action no rule author may name (task 161, D-180): it is offered only when the server swaps it into a
    /// resolved plan for the item's context (<c>create-budget</c> replaces <c>revise-budget</c> on a matter with no active budget,
    /// <see cref="ResolvedDecisionPlan.WithCreateBudgetInsteadOfRevise"/>). <see cref="DecisionPlanService.Resolve"/> refuses a stored plan
    /// that names it and the authoring catalog does not list it, so a published plan can never carry it (D-14).</summary>
    public bool SubstitutionOnly { get; init; }

    /// <summary>How the wizard renders the step's message fields (task 160, D-179): <see cref="DecisionActionCatalog.EmailComposer"/> for the
    /// actions whose <c>to</c>, <c>subject</c> and <c>body</c> the shared EmailComposer edits (compose-only: it never sends; the commit route
    /// does). <c>null</c>: one field per parameter. A presentation hint only; the parameters and what is committed are unchanged.</summary>
    public string? Composer { get; init; }
}

/// <summary>One closed dismissal reason. The stored form on a Signal is <c>"code - detail"</c>.</summary>
/// <param name="Code">Stable code stored with the dismissal.</param>
/// <param name="Label">What the user sees.</param>
/// <param name="CountsTowardSuppression">False for a reason that must never count toward the 3-dismissals-in-30-days
/// suppression rule (R-10: a misresolved matter says nothing about the rule).</param>
/// <param name="RequiresDetail">True when the user must type detail (<c>Other</c>, R-8).</param>
public sealed record DecisionDismissalReason(
    string Code,
    string Label,
    bool CountsTowardSuppression,
    bool RequiresDetail);

/// <summary>
/// The CLOSED action catalog and dismissal-reason lists the decision wizard and the commit route share
/// (spec FR-50, D-19; reconciliation C-4). It is code on purpose: R1 has no Action Engine (spec section 2.2), the
/// record-class mapping is code not configuration (spec A-2), and adding an action is a reviewed code change.
/// </summary>
/// <remarks>
/// <para><b>Out of the catalog by owner decision D-20:</b> escalate, extend-sla, close-inquiry (the inquiry SLA is
/// not in R1). <b>Record class:</b> Routine for reassign and extend-response-date (D-45, reconciliation C-3), mark-complete
/// and reschedule (A-2); Judgement for everything else, and every Next-step creator.</para>
/// <para><b>Excludes are MUTUAL.</b> v4 (Z-4) states them one way: <c>approve-variance</c> removes <c>revise-budget</c>;
/// <c>mark-complete</c> removes <c>reschedule</c> and <c>reassign</c>; <c>record-the-response</c> removes <c>send-reminder</c>
/// and <c>extend-response-date</c>. They are declared once below and made symmetric when the catalog is built, so a
/// pair can never both be taken whichever order the plan lists them in (the seeded POL-COMMIT-BUDGET v2 lists
/// <c>revise-budget</c> before <c>approve-variance</c>). <see cref="FirstConflict"/> is the check the commit route runs.</para>
/// </remarks>
public static class DecisionActionCatalog
{
    /// <summary>The <see cref="DecisionActionDefinition.Composer"/> value for the shared EmailComposer (task 160, D-179).</summary>
    public const string EmailComposer = "email";

    private const string ApproveVarianceEffect = "The Decision Record is the approval; nothing else is written";

    private static readonly DecisionActionParameter[] MessageParameters =
    [
        new("to", "To", DecisionParameterKind.Text, Required: true),
        new("subject", "Subject", DecisionParameterKind.Text, Required: true),
        new("body", "Message", DecisionParameterKind.TextArea, Required: true),
    ];

    private static readonly DecisionActionDefinition[] Declared =
    [
        // ---- Decide lane ----------------------------------------------------------------------------------
        new("send-budget-inquiry", "Send budget inquiry", DecisionLane.Decide, IsNextStep: false, WorkType: "ask",
            [
                .. MessageParameters,
                // Task 043 / 070: which of the matter's outside firms the inquiry is to (sprk_regardingorganization).
                new("outsideFirm", "Outside firm", DecisionParameterKind.Lookup, Required: false,
                    Hint: "The outside firm the inquiry is to; recorded on the inquiry so replies are tracked per firm",
                    LookupEntity: "sprk_organization"),
            ],
            [
                "Sends the message above to the recipient",
                "Records the inquiry against the matter",
                "The item stays open until the budget is revised or the variance approved",
            ],
            Excludes: [],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier4, Reversible: false, ExternallyVisible: true), ResolvesSignal = false, SendsMessage = true, Composer = EmailComposer },

        new("revise-budget", "Revise budget", DecisionLane.Decide, IsNextStep: false, WorkType: "fund",
            [
                new("budget", "Budget to revise", DecisionParameterKind.Lookup, Required: true,
                    Hint: "Pre-selected when the matter has one budget; the user picks one when it has several (D-18)",
                    LookupEntity: "sprk_budget"),
                new("amount", "New budget", DecisionParameterKind.Money, Required: true),
                new("reason", "Reason for revision", DecisionParameterKind.TextArea, Required: true),
            ],
            [
                "Records a budget revision on the matter",
                "Updates the amount of the budget you picked",
            ],
            Excludes: [],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier2b, Reversible: true, RecordOfTruthImpact: true) },

        // Task 161 (D-180, UAT round 1): revise-budget on a matter with NO active budget. Never authored (SubstitutionOnly): the plan and
        // commit routes swap it in for revise-budget, in place, when the matter has none. Same lane, work type, class, gate and
        // resolution as revise-budget; no "budget to revise" because there is none. The amount becomes the new budget's total.
        // Task 167 (D-193, UAT round 2): the budget also collects Category, Year, Start date, End date, Period and Currency, all optional.
        // Category and Period read their options live (an owner relabel shows up); Currency defaults from the matter's business unit.
        new("create-budget", "Create budget", DecisionLane.Decide, IsNextStep: false, WorkType: "fund",
            [
                new("amount", "Budget amount", DecisionParameterKind.Money, Required: true),
                new("category", "Category", DecisionParameterKind.Choice, Required: false,
                    OptionsSource: "metadata:sprk_budget.sprk_budgetcategory"),
                new("year", "Year", DecisionParameterKind.Text, Required: false,
                    Hint: "The budget year, up to four characters, for example 2026",
                    MaxLength: CreateBudgetExecutor.BudgetYearMaxLength),
                new("startDate", "Start date", DecisionParameterKind.Date, Required: false),
                new("endDate", "End date", DecisionParameterKind.Date, Required: false, NotBefore: "startDate"),
                new("period", "Period", DecisionParameterKind.Choice, Required: false,
                    OptionsSource: "metadata:sprk_budget.sprk_budgetperiod"),
                new("currency", "Currency", DecisionParameterKind.Choice, Required: false,
                    Hint: "Defaults to the matter's business unit currency",
                    OptionsSource: DecisionParameterSources.ActiveCurrencies,
                    DefaultSource: DecisionParameterSources.MatterCurrencyDefault),
                new("reason", "Reason for the budget", DecisionParameterKind.TextArea, Required: true),
            ],
            [
                "Creates the matter's first budget",
                "Records the budget and your reason on the Decision Record",
            ],
            Excludes: ["approve-variance"],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier2b, Reversible: true, RecordOfTruthImpact: true), SubstitutionOnly = true },

        new("approve-variance", "Approve variance", DecisionLane.Decide, IsNextStep: false, WorkType: "fund",
            [
                new("amount", "Approved overage", DecisionParameterKind.Money, Required: true),
                new("note", "Approval note", DecisionParameterKind.TextArea, Required: false),
            ],
            [ApproveVarianceEffect],
            Excludes: ["revise-budget"],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier2b, Reversible: true, RecordOfTruthImpact: true) },

        // D-178 (task 159): the no-budget decision creates a Task (a sprk_event of type Task), not a Work Assignment. A Decide-lane plan
        // action only: Add To Do and Assign Work are what the plan offers as its Next steps.
        new("assign-task", "Assign Task", DecisionLane.Decide, IsNextStep: false, WorkType: null,
            [
                new("assignee", "Assign to", DecisionParameterKind.Person, Required: true),
                new("description", "What to do", DecisionParameterKind.TextArea, Required: true),
                new("dueDate", "Due", DecisionParameterKind.Date, Required: false),
            ],
            ["Creates a task (an event of type Task) linked to the record, assigned to the person above, when the decision is recorded"],
            Excludes: [],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier3, Reversible: true, RecordOfTruthImpact: true) },

        // ---- Do lane --------------------------------------------------------------------------------------
        new("mark-complete", "Mark complete", DecisionLane.Do, IsNextStep: false, WorkType: null,
            [new("note", "Completion note", DecisionParameterKind.TextArea, Required: false)],
            ["Sets the item to Completed"],
            Excludes: ["reschedule", "reassign"],
            DecisionRecordClass.Routine)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier2b, Reversible: true, RecordOfTruthImpact: true) },

        new("reschedule", "Reschedule", DecisionLane.Do, IsNextStep: false, WorkType: null,
            [
                new("dueDate", "New due date", DecisionParameterKind.Date, Required: true),
                new("reason", "Reason", DecisionParameterKind.Text, Required: true),
            ],
            ["Moves the due date of the item"],
            Excludes: [],
            DecisionRecordClass.Routine)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier3, Reversible: true, DeadlineImpact: true, RecordOfTruthImpact: true) },

        new("reassign", "Reassign", DecisionLane.Do, IsNextStep: false, WorkType: null,
            [
                new("assignee", "Reassign to", DecisionParameterKind.Person, Required: true),
                new("note", "Note to them", DecisionParameterKind.TextArea, Required: false),
            ],
            ["Assigns the item to the person above"],
            Excludes: [],
            DecisionRecordClass.Routine)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier3, Reversible: true, RecordOfTruthImpact: true) },

        new("send-reminder", "Send a reminder", DecisionLane.Do, IsNextStep: false, WorkType: null,
            MessageParameters,
            ["Sends the message above to the recipient", "The response date is unchanged", "The item stays open until the work is done or its due date moves"],
            Excludes: [],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier4, Reversible: false, ExternallyVisible: true), ResolvesSignal = false, SendsMessage = true, Composer = EmailComposer },

        new("extend-response-date", "Extend response date", DecisionLane.Do, IsNextStep: false, WorkType: null,
            [
                new("responseDate", "New response date", DecisionParameterKind.Date, Required: true),
                new("reason", "Reason", DecisionParameterKind.Text, Required: true),
            ],
            ["Moves the response date of the work assignment"],
            Excludes: [],
            DecisionRecordClass.Routine)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier3, Reversible: true, DeadlineImpact: true, RecordOfTruthImpact: true) },

        new("record-the-response", "Record the response", DecisionLane.Do, IsNextStep: false, WorkType: null,
            [
                new("response", "Response", DecisionParameterKind.Choice, Required: true, Options:
                [
                    // D-58: the values of sprk_workassignment.sprk_responseoutcome (task 047).
                    new("received-outside-spaarke", "Received outside Spaarke"),
                    new("delivered-on-the-matter", "Delivered on the matter"),
                    new("no-longer-needed", "No longer needed"),
                ]),
                new("note", "Note", DecisionParameterKind.Text, Required: false),
            ],
            ["Records the response on the work assignment"],
            Excludes: ["send-reminder", "extend-response-date"],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier2b, Reversible: true, RecordOfTruthImpact: true) },

        // ---- Next-step creators (any Next step makes the record Judgement, R-2) ---------------------------
        new("add-todo", "Add To Do", PlanLane: null, IsNextStep: true, WorkType: null,
            [
                new("title", "To Do", DecisionParameterKind.Text, Required: true),
                new("assignee", "Assigned to", DecisionParameterKind.Person, Required: true),
                new("dueDate", "Due", DecisionParameterKind.Date, Required: true),
            ],
            ["Creates a To Do linked to the record, created when the decision is recorded"],
            Excludes: [],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier3, Reversible: true, DeadlineImpact: true, RecordOfTruthImpact: true) },

        new("create-event", "Create Event", PlanLane: null, IsNextStep: true, WorkType: null,
            [
                new("title", "Event", DecisionParameterKind.Text, Required: true),
                new("date", "Date", DecisionParameterKind.Date, Required: true),
                new("attendees", "Attendees", DecisionParameterKind.Text, Required: false),
            ],
            ["Creates an event linked to the record, created when the decision is recorded"],
            Excludes: [],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier3, Reversible: true, DeadlineImpact: true, RecordOfTruthImpact: true) },

        new("send-email", "Send Notification Email", PlanLane: null, IsNextStep: true, WorkType: null,
            MessageParameters,
            ["Sends the message above when the decision is recorded"],
            Excludes: [],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier4, Reversible: false, ExternallyVisible: true), SendsMessage = true, Composer = EmailComposer },

        // assign-work is both: a Next step on any plan, and a Decide-lane plan action the Know-promotion rule offers (D-51).
        new("assign-work", "Assign Work", DecisionLane.Decide, IsNextStep: true, WorkType: null,
            [
                new("assignee", "Assign to", DecisionParameterKind.Person, Required: true),
                new("description", "What to do", DecisionParameterKind.TextArea, Required: true),
            ],
            ["Creates a work assignment linked to the record, created when the decision is recorded"],
            Excludes: [],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier3, Reversible: true, RecordOfTruthImpact: true) },
    ];

    /// <summary>The catalog proper: <see cref="Declared"/> with every exclusion made mutual.</summary>
    private static readonly DecisionActionDefinition[] Definitions = Mutualize(Declared);

    private const string ReasonHint = "Shown with the decision; required";

    /// <summary>
    /// ebilling-r1 task 074 (spec FR-30, FR-19, FR-35 to FR-37; D-75): the billing decisions, a closed set of their own. They are
    /// recorded by the eBilling endpoints, never taken through the Signals commit route, so they are NOT in <see cref="All"/> and
    /// <see cref="TryGet"/> does not know them: a Signals plan naming one is refused as an unknown action, the commit route refuses
    /// it as unresolved, and no executor can be registered for one (<see cref="DecisionActionExecutors"/> refuses a code outside
    /// <see cref="All"/>). Line decisions are about the invoice line itself and invoice decisions about the invoice (D-75).
    /// <see cref="DecisionActionDefinition.WorkType"/> is null: no Signals plan offers them.
    /// </summary>
    private static readonly DecisionActionDefinition[] DeclaredBilling =
    [
        // ---- Line decisions (subject: the invoice line) ------------------------------------------------------
        new("accept-line", "Accept line", DecisionLane.Decide, IsNextStep: false, WorkType: null,
            [],
            ["Accepts the line at its billed amount"],
            Excludes: ["reduce-line", "disallow-line"],
            DecisionRecordClass.Routine)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier2b, Reversible: true, RecordOfTruthImpact: true) },

        new("reduce-line", "Reduce line", DecisionLane.Decide, IsNextStep: false, WorkType: null,
            [
                new("approvedAmount", "Approved amount", DecisionParameterKind.Money, Required: true),
                new("reason", "Reason for reduction", DecisionParameterKind.TextArea, Required: true, Hint: ReasonHint),
            ],
            ["Sets the line's approved amount below its billed amount", "Recomputes the invoice totals"],
            Excludes: ["disallow-line"],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier2b, Reversible: true, RecordOfTruthImpact: true) },

        new("disallow-line", "Disallow line", DecisionLane.Decide, IsNextStep: false, WorkType: null,
            [new("reason", "Reason for disallowing", DecisionParameterKind.TextArea, Required: true, Hint: ReasonHint)],
            ["Sets the line's approved amount to zero", "Recomputes the invoice totals"],
            Excludes: [],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier2b, Reversible: true, RecordOfTruthImpact: true) },

        // ---- Invoice decisions (subject: the invoice) --------------------------------------------------------
        new("approve-invoice", "Approve invoice", DecisionLane.Decide, IsNextStep: false, WorkType: null,
            [new("note", "Approval note", DecisionParameterKind.TextArea, Required: false)],
            ["Approves the invoice as payable", "Freezes the approved total"],
            Excludes: ["reject-invoice", "return-to-firm"],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier4, Reversible: false, RecordOfTruthImpact: true) },

        new("reject-invoice", "Reject invoice", DecisionLane.Decide, IsNextStep: false, WorkType: null,
            [new("reason", "Reason for rejection", DecisionParameterKind.TextArea, Required: true, Hint: ReasonHint)],
            ["Rejects the whole invoice; the rejection is final", "The firm sees the reason"],
            Excludes: ["return-to-firm"],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier4, Reversible: false, ExternallyVisible: true, RecordOfTruthImpact: true) },

        new("return-to-firm", "Return to firm", DecisionLane.Decide, IsNextStep: false, WorkType: null,
            [new("reason", "Reason for returning", DecisionParameterKind.TextArea, Required: true, Hint: ReasonHint)],
            ["Returns the invoice to the firm to correct and resubmit", "The firm sees the reason"],
            Excludes: [],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier4, Reversible: false, ExternallyVisible: true, RecordOfTruthImpact: true) },

        // ---- Override (D-24; subject: the invoice the accepted submission created) ----------------------------
        new("override-submission", "Override blocking failure", DecisionLane.Decide, IsNextStep: false, WorkType: null,
            [new("reason", "Reason for overriding", DecisionParameterKind.TextArea, Required: true, Hint: ReasonHint)],
            ["Accepts the submission despite its blocking failure", "The overridden finding stays visible to the reviewer"],
            Excludes: [],
            DecisionRecordClass.Judgement)
        { Gate = new DecisionGateFactors(GateRiskTier.Tier3, Reversible: true, RecordOfTruthImpact: true) },
    ];

    /// <summary>The billing set with every exclusion made mutual. Its codes never overlap <see cref="Definitions"/> (checked at static
    /// initialisation, so a code can never be both a Signals action and a billing action).</summary>
    private static readonly DecisionActionDefinition[] BillingDefinitions = Separate(Mutualize(DeclaredBilling), Definitions);

    /// <summary>Throws when a billing code is also a Signals catalog code.</summary>
    private static DecisionActionDefinition[] Separate(DecisionActionDefinition[] billing, DecisionActionDefinition[] signals)
    {
        foreach (var clash in billing.Where(b => signals.Any(s => s.Code == b.Code)))
        {
            throw new InvalidOperationException($"Action catalog: billing code '{clash.Code}' is also a Signals action.");
        }

        return billing;
    }

    private static readonly FrozenDictionary<string, DecisionActionDefinition> BillingByCode =
        BillingDefinitions.ToFrozenDictionary(d => d.Code, StringComparer.Ordinal);

    /// <summary>ebilling-r1 task 074: every billing action, in declaration order. Not part of <see cref="All"/>.</summary>
    public static IReadOnlyList<DecisionActionDefinition> BillingActions => BillingDefinitions;

    /// <summary>Resolves a billing code, exactly as <see cref="TryGet"/> resolves a Signals code: case-sensitive, never guessed.</summary>
    public static bool TryGetBilling(string? code, out DecisionActionDefinition definition)
    {
        if (code is not null && BillingByCode.TryGetValue(code, out var found))
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }

    /// <summary>The first pair of billing codes in <paramref name="takenCodes"/> that exclude each other, or null. The same check as
    /// <see cref="FirstConflict"/>, over the billing set.</summary>
    public static (string First, string Second)? FirstBillingConflict(IEnumerable<string> takenCodes)
    {
        var taken = takenCodes.ToList();
        foreach (var code in taken)
        {
            if (TryGetBilling(code, out var action) && taken.FirstOrDefault(other => action.Excludes.Contains(other)) is { } clash)
            {
                return (code, clash);
            }
        }

        return null;
    }

    /// <summary>
    /// Makes every declared exclusion mutual. Throws (at static initialisation, so the catalog cannot load) when an exclusion
    /// names a code that is not declared: a typo would otherwise silently drop the exclusion.
    /// </summary>
    internal static DecisionActionDefinition[] Mutualize(DecisionActionDefinition[] declared)
    {
        var codes = declared.Select(d => d.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var action in declared)
        {
            foreach (var excluded in action.Excludes.Where(e => !codes.Contains(e)))
            {
                throw new InvalidOperationException(
                    $"Action catalog: '{action.Code}' excludes '{excluded}', which is not a declared action.");
            }
        }

        return declared
            .Select(a => a with
            {
                Excludes = declared
                    .Where(b => b.Code != a.Code && (a.Excludes.Contains(b.Code) || b.Excludes.Contains(a.Code)))
                    .Select(b => b.Code)
                    .ToArray(),
            })
            .ToArray();
    }

    private static readonly FrozenDictionary<string, DecisionActionDefinition> ByCode =
        Definitions.ToFrozenDictionary(d => d.Code, StringComparer.Ordinal);

    private static readonly DecisionDismissalReason[] DecideReasons =
    [
        new("already-approved-offline", "Already approved offline", true, false),
        new("not-material", "Not material", true, false),
        // R-10: a misresolved matter says nothing about whether the rule is noisy, so it never counts.
        new("wrong-matter-misresolved", "Wrong matter - misresolved", false, false),
        new("duplicate", "Duplicate", true, false),
        new("handled-outside-spaarke", "Handled outside Spaarke", true, false),
        new("other", "Other", true, true),
    ];

    private static readonly DecisionDismissalReason[] DoReasons =
    [
        new("already-done-outside-spaarke", "Already done outside Spaarke", true, false),
        new("not-mine", "Not mine", true, false),
        new("no-longer-needed", "No longer needed", true, false),
        new("duplicate", "Duplicate", true, false),
        new("other", "Other", true, true),
    ];

    /// <summary>Every action in the catalog, in declaration order.</summary>
    public static IReadOnlyList<DecisionActionDefinition> All => Definitions;

    /// <summary>Resolves a plan code. Codes are case-sensitive and exact: a near-miss is unknown, never guessed.</summary>
    public static bool TryGet(string? code, out DecisionActionDefinition definition)
    {
        if (code is not null && ByCode.TryGetValue(code, out var found))
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }

    /// <summary>
    /// The first pair in <paramref name="takenCodes"/> that excludes each other, or null. Excludes are mutual, so the order
    /// the codes are given in does not matter. An unknown code is not a conflict here (the plan resolver refuses it).
    /// </summary>
    public static (string First, string Second)? FirstConflict(IEnumerable<string> takenCodes)
    {
        var taken = takenCodes.ToList();
        foreach (var code in taken)
        {
            if (TryGet(code, out var action) && taken.FirstOrDefault(other => action.Excludes.Contains(other)) is { } clash)
            {
                return (code, clash);
            }
        }

        return null;
    }

    /// <summary>The closed dismissal-reason list for a lane.</summary>
    public static IReadOnlyList<DecisionDismissalReason> DismissalReasons(DecisionLane lane) =>
        lane == DecisionLane.Decide ? DecideReasons : DoReasons;
}
