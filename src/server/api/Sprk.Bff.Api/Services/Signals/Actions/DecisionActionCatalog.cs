using System.Collections.Frozen;

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
/// <see cref="Kind"/> is <see cref="DecisionParameterKind.Lookup"/>.
/// </summary>
public sealed record DecisionActionParameter(
    string Code,
    string Label,
    DecisionParameterKind Kind,
    bool Required,
    string? Hint = null,
    string? LookupEntity = null);

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
    DecisionRecordClass RecordClass);

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
/// <para><b>Excludes</b> are as stated in v4 (Z-4): taking <c>approve-variance</c> removes <c>revise-budget</c>;
/// taking <c>mark-complete</c> removes <c>reschedule</c> and <c>reassign</c>; taking <c>record-the-response</c> removes
/// <c>send-reminder</c> and <c>extend-response-date</c>. The relation is directional ("an earlier action removes the later
/// step"), so a plan author who wants the exclusion to bite orders the excluding action first.</para>
/// </remarks>
public static class DecisionActionCatalog
{
    private const string ApproveVarianceEffect = "The Decision Record is the approval; nothing else is written";

    private static readonly DecisionActionParameter[] MessageParameters =
    [
        new("to", "To", DecisionParameterKind.Text, Required: true),
        new("subject", "Subject", DecisionParameterKind.Text, Required: true),
        new("body", "Message", DecisionParameterKind.TextArea, Required: true),
    ];

    private static readonly DecisionActionDefinition[] Definitions =
    [
        // ---- Decide lane ----------------------------------------------------------------------------------
        new("send-budget-inquiry", "Send budget inquiry", DecisionLane.Decide, IsNextStep: false, WorkType: "ask",
            [
                .. MessageParameters,
                new("replyWithin", "Reply expected within", DecisionParameterKind.Choice, Required: true),
            ],
            [
                "Sends the message above to the recipient",
                "Records the inquiry against the matter",
            ],
            Excludes: [],
            DecisionRecordClass.Judgement),

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
            DecisionRecordClass.Judgement),

        new("approve-variance", "Approve variance", DecisionLane.Decide, IsNextStep: false, WorkType: "fund",
            [
                new("amount", "Approved overage", DecisionParameterKind.Money, Required: true),
                new("note", "Approval note", DecisionParameterKind.TextArea, Required: false),
            ],
            [ApproveVarianceEffect],
            Excludes: ["revise-budget"],
            DecisionRecordClass.Judgement),

        // ---- Do lane --------------------------------------------------------------------------------------
        new("mark-complete", "Mark complete", DecisionLane.Do, IsNextStep: false, WorkType: null,
            [new("note", "Completion note", DecisionParameterKind.TextArea, Required: false)],
            ["Sets the item to Completed"],
            Excludes: ["reschedule", "reassign"],
            DecisionRecordClass.Routine),

        new("reschedule", "Reschedule", DecisionLane.Do, IsNextStep: false, WorkType: null,
            [
                new("dueDate", "New due date", DecisionParameterKind.Date, Required: true),
                new("reason", "Reason", DecisionParameterKind.Text, Required: true),
            ],
            ["Moves the due date of the item (sprk_duedate)"],
            Excludes: [],
            DecisionRecordClass.Routine),

        new("reassign", "Reassign", DecisionLane.Do, IsNextStep: false, WorkType: null,
            [
                new("assignee", "Reassign to", DecisionParameterKind.Person, Required: true),
                new("note", "Note to them", DecisionParameterKind.TextArea, Required: false),
            ],
            ["Assigns the item to the person above"],
            Excludes: [],
            DecisionRecordClass.Routine),

        new("send-reminder", "Send a reminder", DecisionLane.Do, IsNextStep: false, WorkType: null,
            MessageParameters,
            ["Sends the message above to the recipient", "The response date is unchanged"],
            Excludes: [],
            DecisionRecordClass.Judgement),

        new("extend-response-date", "Extend response date", DecisionLane.Do, IsNextStep: false, WorkType: null,
            [
                new("responseDate", "New response date", DecisionParameterKind.Date, Required: true),
                new("reason", "Reason", DecisionParameterKind.Text, Required: true),
            ],
            ["Moves the response date of the work assignment"],
            Excludes: [],
            DecisionRecordClass.Routine),

        new("record-the-response", "Record the response", DecisionLane.Do, IsNextStep: false, WorkType: null,
            [
                new("response", "Response", DecisionParameterKind.Choice, Required: true),
                new("note", "Note", DecisionParameterKind.Text, Required: false),
            ],
            ["Records the response on the work assignment"],
            Excludes: ["send-reminder", "extend-response-date"],
            DecisionRecordClass.Judgement),

        // ---- Next-step creators (any Next step makes the record Judgement, R-2) ---------------------------
        new("add-todo", "Add To Do", PlanLane: null, IsNextStep: true, WorkType: null,
            [
                new("title", "To Do", DecisionParameterKind.Text, Required: true),
                new("assignee", "Assigned to", DecisionParameterKind.Person, Required: true),
                new("dueDate", "Due", DecisionParameterKind.Date, Required: true),
            ],
            ["Creates a To Do linked to the record, created when the decision is recorded"],
            Excludes: [],
            DecisionRecordClass.Judgement),

        new("create-event", "Create Event", PlanLane: null, IsNextStep: true, WorkType: null,
            [
                new("title", "Event", DecisionParameterKind.Text, Required: true),
                new("date", "Date", DecisionParameterKind.Date, Required: true),
                new("attendees", "Attendees", DecisionParameterKind.Text, Required: false),
            ],
            ["Creates an event linked to the record, created when the decision is recorded"],
            Excludes: [],
            DecisionRecordClass.Judgement),

        new("send-email", "Send Notification Email", PlanLane: null, IsNextStep: true, WorkType: null,
            MessageParameters,
            ["Sends the message above when the decision is recorded"],
            Excludes: [],
            DecisionRecordClass.Judgement),

        // assign-work is both: a Next step on any plan, and a Decide-lane plan action the Know-promotion rule offers (D-51).
        new("assign-work", "Assign Work", DecisionLane.Decide, IsNextStep: true, WorkType: null,
            [
                new("assignee", "Assign to", DecisionParameterKind.Person, Required: true),
                new("description", "What to do", DecisionParameterKind.TextArea, Required: true),
            ],
            ["Creates a work assignment linked to the record, created when the decision is recorded"],
            Excludes: [],
            DecisionRecordClass.Judgement),
    ];

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

    /// <summary>The closed dismissal-reason list for a lane.</summary>
    public static IReadOnlyList<DecisionDismissalReason> DismissalReasons(DecisionLane lane) =>
        lane == DecisionLane.Decide ? DecideReasons : DoReasons;
}
