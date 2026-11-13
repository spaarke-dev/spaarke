using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Procedures;

// ebilling-r1 task 084 Part A (notes/procedure-and-file-model.md 3.2, 6.1-6.6, 7.1; EB-D-218, EB-D-219; notes/084-procedure-review-ui.md):
// the two read models of one core record, as the routes of RecordProcedureEndpoints.cs return them.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — no route lists a record's Procedures or its rules in force. ProcedureResolver (082) computes both for evaluation and
//       RuleSetEndpoints lists rule sets for billing admins without a record. AffectedInvoiceCounter + CallerRecordAccessProbe already own
//       the "caller's Read on a batch of records, unknown kept apart from no" question.
//   (2) Extension — this is a thin reader over the resolver's own output (one ReadRowsAsync + RulesOn; no second selection) plus the
//       probe's batched method. A new class rather than a method on the resolver because ProcedureResolver.cs is ontology-r1-reviewed code
//       and 084 changes none of it; rows are reached through the resolver's internal reader.
//   (3) Cost of doing nothing — O-3 and O-9 fail: the rules that judge a record are neither visible nor reviewable, so an override cannot
//       be checked before an invoice is judged, and 084a-084d have nothing to render.
//
// Placement (bff-extensions.md): BFF, Services/Finance/Procedures, beside ProcedureAssignmentService. A scoped concrete class (ADR-010);
// one DI registration in FinanceModule; no package, no background work; writes nothing.

/// <summary>Whether a read model was produced.</summary>
public enum RecordProcedureReadStatus
{
    /// <summary>The answer is complete and the caller can Read every row in it.</summary>
    Done,

    /// <summary>The core record is gone (it disappeared between the route's Read check and this read): the uniform 404.</summary>
    RecordNotFound,

    /// <summary>
    /// Nothing is returned: a row read faulted, the resolver could not resolve the rows, the caller could not be established, a rights
    /// question was not answered, or the caller cannot Read a row the answer would expose (EB-D-218: never a partial list). The route
    /// answers 503 <c>procedures.unavailable</c>.
    /// </summary>
    Unavailable,

    /// <summary>Task 084d-2a: the dry run's planning refused the proposed override with the write's own status and code.</summary>
    PlanRefused,
}

// ── Dry run (task 084d-2a; notes/084d-dry-run-design.md §2-§4) ────────────────────────────────────────────────────

/// <summary>
/// A proposed override for the dry run: one of the four writes (<see cref="Kind"/>) with that write's own request body, and optionally the
/// override it replaces (<see cref="ReplacesOverrideId"/>: removed first, as the wizards' two-call plans do). <see cref="OverrideId"/> is the
/// override a <c>Remove</c> removes. Only the body of the named kind is read.
/// </summary>
public sealed record ProcedureDryRunRequest(
    string? Kind, Guid? ReplacesOverrideId = null, Guid? OverrideId = null,
    ProcedureAssignmentRequest? Assign = null, RuleExcludeRequest? ExcludeRule = null, RuleChangeRequest? ChangeRule = null)
{
    /// <summary>Assign, follow, pin or exclude a Procedure (<c>procedure-assignments</c>).</summary>
    public const string KindAssign = "Assign";

    /// <summary>Exclude a rule (<c>rule-overrides/exclude</c>).</summary>
    public const string KindExcludeRule = "ExcludeRule";

    /// <summary>Change a rule (<c>rule-overrides/change</c>).</summary>
    public const string KindChangeRule = "ChangeRule";

    /// <summary>Remove an override (<c>DELETE overrides/{id}</c>).</summary>
    public const string KindRemove = "Remove";
}

/// <summary>
/// What a proposed override would show. <see cref="Now"/> is the two lists as they read now; <see cref="After"/> is the same two lists read
/// with the proposed rows in place, labelled <see cref="AfterLabel"/> ("If saved now"). After is never a promise that the save will succeed:
/// <see cref="Disclosure"/> says what the preview cannot know.
/// </summary>
/// <remarks>Task 084d-2b: <see cref="Closes"/> says what the write would close, and <see cref="AffectedOpenInvoices"/> how many open invoices the
/// caller can see it affects.</remarks>
public sealed record ProcedureDryRunView(
    ChainRecordView Record, DateOnly Date, FirmView? Firm, string FirmSource, ProcedureDryRunProposalView Proposal,
    ProcedureDryRunSideView Now, ProcedureDryRunSideView After, string AfterLabel, ProcedureDryRunDisclosureView Disclosure,
    ProcedureDryRunClosesView Closes, ProcedureDryRunAffectedView AffectedOpenInvoices);

/// <summary>
/// Task 084d-2b: what the write would close. <see cref="Kinds"/>: <c>ChangedRuleRemoved</c> (removing a changed rule switches its record rule
/// Off and closes its open findings: <see cref="D170"/> true, <see cref="Warning"/> the removal warning) and <c>BaseRuleSuperseded</c> (a change
/// closes its base rule's open findings Superseded: <see cref="Message"/> is the change's own close message, which states its limits). Empty when
/// nothing closes: a plain Exclude, an assignment and a plain removal close nothing.
/// </summary>
public sealed record ProcedureDryRunClosesView(bool D170, IReadOnlyList<string> Kinds, string? Warning, string? Message);

/// <summary>
/// Task 084d-2b: how many open (In Review) invoices the caller can Read the proposal affects. <see cref="Basis"/> <c>FindingsClosed</c>: the
/// invoices whose open findings would close (the write's own selection). <c>ResolutionChanged</c>: the invoices filed on the record or directly
/// under it whose Procedure resolution differs between Now and After (model 8.2's comparison). <see cref="Count"/> is null when it cannot be
/// told (a fault, a bound, an unanswered rights question): never a guess.
/// </summary>
public sealed record ProcedureDryRunAffectedView(int? Count, string? Text, string Basis);

/// <summary>
/// The proposal as planned: the write (<see cref="Kind"/>), the kind of override row it writes or removes (<see cref="OverrideKind"/>), the
/// override it removes or replaces, and <see cref="ProposedRowIds"/>, the rows the write would create (their derived ids).
/// </summary>
public sealed record ProcedureDryRunProposalView(
    string Kind, string OverrideKind, Guid? ReplacesOverrideId, Guid? OverrideId, IReadOnlyList<Guid> ProposedRowIds);

/// <summary>One side of a dry run: 084's two lists. Their <c>permissions</c> are not computed in a dry run (all false).</summary>
public sealed record ProcedureDryRunSideView(RecordProceduresView Procedures, RecordRulesInForceView Rules);

/// <summary>
/// What the preview does not know (uac-r2 R5, R6, R10; ontology-r1 K1, K2), as fixed flags and fixed text.
/// <see cref="PredictsOwnerRefusal"/> is always false: the preview asks no owner, so a save can still be refused for ownership (R5).
/// <see cref="ReadAtOneInstant"/> is always false: Now and After are separate reads (R10). <see cref="RequestFirmAsked"/> is true when the
/// request names a firm: it was asked of the caller as a query firm, so a firm the caller cannot Read gives 503, not a preview (R6).
/// </summary>
public sealed record ProcedureDryRunDisclosureView(
    bool PredictsOwnerRefusal, bool ReadAtOneInstant, bool RequestFirmAsked, IReadOnlyList<string> Notes);

/// <summary>A dry run's answer: the view, or why there is none (<see cref="Refusal"/> when the planning refused).</summary>
internal sealed record RecordProcedureDryRunResult(RecordProcedureReadStatus Status, ProcedureDryRunView? Value, RuleSetOutcome<ProcedureOverridePlan>? Refusal = null);

// ── Assignable Procedures (task 084d-5a; notes/084d-dry-run-design.md "084d-5 design", EB-D-227..230) ──────────────

/// <summary>
/// The Procedures a billing admin could assign (Follow latest) to one core record for a firm and a date, each as it would show "if assigned now"
/// (<see cref="Label"/>). <see cref="Procedures"/> is null exactly when <see cref="TooMany"/>: more than <see cref="Bound"/> candidates, so the
/// caller narrows by name (<see cref="Q"/>); it is never a partial list (EB-D-227). <see cref="NotOffered"/> are the candidates the record already
/// has an override row for (reason <c>OwnRow</c>: the write would answer 409 <c>assignment.duplicate</c>). A Procedure already on the record (any
/// status in its Procedures list) is neither offered nor listed here: the client holds that list. A Procedure the resolver would not apply
/// (no published version, another firm, another area) is absent with no reason.
/// </summary>
public sealed record RecordAssignableProceduresView(
    ChainRecordView Record, DateOnly Date, FirmView? Firm, string FirmSource, string? Q, int Bound, bool TooMany,
    IReadOnlyList<AssignableProcedureItem>? Procedures, IReadOnlyList<NotOfferedProcedureView> NotOffered, string Label);

/// <summary>
/// One Procedure that can be assigned: a projection of its "if assigned" <see cref="ProcedureListItem"/>, fields copied, never recomputed.
/// <see cref="StatusIfAssigned"/>: <c>InForce</c>, <c>Gap</c> (with <see cref="Gap"/>, EB-D-230) or <c>NotYetInForce</c>. Pin versions are not
/// here: the Procedure read (<c>GET /api/finance/procedures/{id}</c>) lists them.
/// </summary>
public sealed record AssignableProcedureItem(
    Guid Id, string Name, string? Type, string AppliesTo, FirmView? Firm, string StatusIfAssigned, ProcedureVersionView2? VersionInForce,
    AssignableGapView? Gap);

/// <summary>EB-D-230: why an offered Procedure would be a Gap on the date (<see cref="Kind"/>, <see cref="Text"/> as the list words it) and what
/// assigning it does (<see cref="Warning"/>, O-22).</summary>
public sealed record AssignableGapView(string Kind, string Text, string Warning);

/// <summary>A candidate that is not offered, and why (<see cref="Reason"/>: <c>OwnRow</c>).</summary>
public sealed record NotOfferedProcedureView(Guid Id, string? Name, string Reason);

/// <summary>A read model, or why there is none.</summary>
public sealed record RecordProcedureReadResult<T>(RecordProcedureReadStatus Status, T? Value) where T : class
{
    internal static RecordProcedureReadResult<T> Of(T value) => new(RecordProcedureReadStatus.Done, value);

    internal static RecordProcedureReadResult<T> NotFound() => new(RecordProcedureReadStatus.RecordNotFound, null);

    internal static RecordProcedureReadResult<T> Unavailable() => new(RecordProcedureReadStatus.Unavailable, null);
}

// ── Shared shapes ───────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>A record on the chain: the route's record (distance 0) or a direct parent (distance 1). <see cref="Table"/> is the logical name.</summary>
public sealed record ChainRecordView(string Table, Guid Id, string? Name, int Distance);

/// <summary>A firm (<c>sprk_organization</c>) by id and name.</summary>
public sealed record FirmView(Guid Id, string? Name);

/// <summary>Who did something and when: the row's creator person (<c>sprk_createdbyperson</c>) and <c>createdon</c> (UTC). Either part is null when the row carries none.</summary>
public sealed record StampView(Guid? ById, string? ByName, DateTime? On);

/// <summary>A Procedure Version: number, status name and half-open window <c>[From, To)</c> (<c>To</c> null = open-ended).</summary>
public sealed record ProcedureVersionView2(Guid Id, int Number, string Status, DateOnly? From, DateOnly? To);

/// <summary>A policy version by id and number (the rule's own version chain).</summary>
public sealed record PolicyVersionRefView(Guid Id, int Number);

/// <summary>
/// Task 084g (EB-D-221): the caller's write permissions on the record, one per write of ProcedureAssignmentEndpoints:
/// <see cref="AssignOrExclude"/> (procedure-assignments and rule-overrides/exclude), <see cref="Remove"/> (removal-preview and DELETE
/// overrides/{id}) and <see cref="Change"/> (rule-overrides/change). Each is computed for the caller from that route's own record and
/// privilege declarations, without its per-subject Read. A hint for the client only: the write routes still decide. False when a check is
/// denied, faulted or not answered.
/// </summary>
public sealed record RecordWritePermissions(bool AssignOrExclude, bool Remove, bool Change)
{
    /// <summary>Every flag false: the fail-closed answer.</summary>
    public static readonly RecordWritePermissions None = new(false, false, false);
}

// ── Procedures list (model 6.6, questions a-e) ──────────────────────────────────────────────────────────────────

/// <summary>
/// The Procedures one core record is subject to for a firm and a date. <see cref="FirmSource"/> says where the firm came from:
/// <c>Query</c>, <c>Record</c> (the record's <c>sprk_billingfirm</c>) or <c>None</c> (then only Procedures with no Firm apply).
/// <see cref="Gaps"/> are the blocking gaps (O-22), a separate array as well as each row's <see cref="ProcedureListItem.Status"/>.
/// </summary>
public sealed record RecordProceduresView(
    ChainRecordView Record, DateOnly Date, FirmView? Firm, string FirmSource,
    IReadOnlyList<ProcedureListItem> Procedures, IReadOnlyList<ProcedureGapView> Gaps)
{
    /// <summary>Task 084g: the caller's write permissions on the record (set by the route; all false until then).</summary>
    public RecordWritePermissions Permissions { get; init; } = RecordWritePermissions.None;
}

/// <summary>
/// One Procedure on the record. <see cref="Status"/>: <c>InForce</c> (a version applies; <see cref="VersionInForce"/> says which and whether
/// it is pinned), <c>Excluded</c> (returned, not applied: the Exclude is in <see cref="Overrides"/>), <c>Gap</c> (applies but no version can
/// be chosen: the invoice is held, see <see cref="ProcedureGapView"/>) or <c>NotYetInForce</c> (the date is before its first version opened:
/// not applicable, not a gap). <see cref="Level"/> is the highest source's level (<c>AllRecords</c>, <c>Firm</c>, <c>ManualParent</c>,
/// <c>ManualOwn</c>); <see cref="Sources"/> lists EVERY matching source, highest first, the winning one marked.
/// </summary>
public sealed record ProcedureListItem(
    Guid Id, string Name, string Area, string? Type, string AppliesTo, FirmView? Firm, string Status, string Level,
    IReadOnlyList<ProcedureSourceView> Sources, ProcedureVersionView2? VersionInForce, bool Pinned, ProcedureVersionView2? NewerVersion,
    IReadOnlyList<ProcedureOverrideView> Overrides);

/// <summary>
/// One way a Procedure reaches the record. <see cref="Kind"/>: <c>AllRecords</c>, <c>Firm</c>, <c>ManualOwn</c> (an assignment row on this
/// record) or <c>ManualParent</c> (on a direct parent, <see cref="Via"/>). A manual source carries its <see cref="Mode"/>
/// (<c>FollowLatest</c> or <c>Pinned</c>) and the row.
/// </summary>
public sealed record ProcedureSourceView(string Kind, bool Winning, int Distance, ChainRecordView? Via, string? Mode, Guid? RowId);

/// <summary>
/// A Procedure-level assignment row on the chain (any mode). <see cref="Decisive"/>: the row (or one of the rows) that decided the
/// Procedure on this record (nearer wins, 6.5). A pinned row carries <see cref="PinnedVersion"/> and the <see cref="NewerVersion"/> it
/// skips (the newest version in force that is newer than the pin), when there is one.
/// </summary>
public sealed record ProcedureOverrideView(
    Guid RowId, string Mode, bool Decisive, int Distance, ChainRecordView ChainRecord, string? Reason,
    ProcedureVersionView2? PinnedVersion, ProcedureVersionView2? NewerVersion, StampView Stamp);

/// <summary>
/// An applicable Procedure with no version for the date (model 7.1). <see cref="Kind"/> is a <see cref="ProcedureGapKind"/> name;
/// <see cref="Text"/> is a readable sentence; <see cref="Overrides"/> are the rows that decided (for <c>ConflictingParents</c>, the
/// parents' conflicting pins).
/// </summary>
public sealed record ProcedureGapView(
    Guid ProcedureId, string ProcedureName, string Kind, DateOnly WorkDate, string Text, IReadOnlyList<ProcedureOverrideView> Overrides);

// ── Rules in force (model 6.6, questions f-g) ───────────────────────────────────────────────────────────────────

/// <summary>
/// The rules in force on the record for a firm and a date, as <c>ProcedureResolution.RulesOn</c> returns them: <see cref="Rules"/> holds
/// every rule that runs (<see cref="RuleInForceView.InForce"/> true, one per resolved rule, the resolver's order) and then every rule that
/// does not (excluded, or a record rule whose base rule is not in force on the date), each with its marks.
/// </summary>
public sealed record RecordRulesInForceView(
    ChainRecordView Record, DateOnly Date, FirmView? Firm, string FirmSource,
    IReadOnlyList<RuleInForceView> Rules, IReadOnlyList<ProcedureGapView> Gaps)
{
    /// <summary>Task 084g: the caller's write permissions on the record (set by the route; all false until then).</summary>
    public RecordWritePermissions Permissions { get; init; } = RecordWritePermissions.None;
}

/// <summary>A Procedure by id and name, with the Procedure Version that supplied a rule and whether it is pinned.</summary>
public sealed record RuleProcedureView(Guid Id, string Name, ProcedureVersionView2? Version, bool Pinned);

/// <summary>
/// One rule. <see cref="InForce"/> false: the rule does not run on this date (<c>excluded</c>, or <c>baseRetired</c>). A record rule
/// (<see cref="RecordRule"/>) is a change made for this record: <see cref="PolicyId"/> is the record rule's, its <see cref="Code"/> the
/// BASE rule's catalogue code (uac-r2 option (a)), and its <c>changed</c> mark names the base rule. <see cref="FirmScope"/> null: all firms.
/// <see cref="Level"/> and <see cref="Distance"/> are the source Procedure's rank. <see cref="ParameterSummary"/> is the rule body in
/// plain language (null when the describer refuses it).
/// <para>084c fix round 1 (F1-2, item 6): <see cref="RuleBody"/> is the body of <see cref="PolicyVersionId"/> (the version shown) as a JSON
/// object, so the Change form starts from every published value, optional ones included; <see cref="Consequence"/> is that version's
/// consequence (<c>Reject</c>, <c>Flag</c>, <c>SuggestAdjustment</c>). Both are null on a row that shows no version (a base-retired record
/// rule), and the body is null when the stored text is not a JSON object. Both are read from the version row, which the answer asks of the
/// caller (EB-D-218).</para>
/// </summary>
public sealed record RuleInForceView(
    Guid PolicyId, Guid? PolicyVersionId, string Code, string? Name, string? RuleType, string? ParameterSummary, int? VersionNumber,
    DateOnly? InForceFrom, bool InForce, bool RecordRule, Guid? RuleSetId, RuleProcedureView? Procedure, string? Level, int? Distance,
    FirmView? FirmScope, IReadOnlyList<RuleMarkView> Marks, string? Consequence = null, JsonElement? RuleBody = null);

/// <summary>
/// A mark on a rule. <see cref="Kind"/>: <c>Excluded</c> (the rule was excluded for this record: reason, optional firm, distance, who and
/// when), <c>Changed</c> (a record rule runs in its slot: <see cref="RecordRuleId"/>, the version it is <see cref="BasedOn"/>, the base
/// rule), <c>Stale</c> (BasedOn is not the base rule's version in force on the date: <see cref="CurrentBase"/>) or <c>BaseRetired</c> (the
/// base rule is not in force on the date, so the record rule does not run). Task 084e (EB-D-219, ontology-r1 R1-R6): <c>Outranked</c> (a
/// Replace key: a higher-ranked rule of the same key passes at record level, so on an invoice this rule runs only if every one of
/// <see cref="By"/> is refused) and <c>CapOutranked</c> (ExpenseRule: the same for its cap only; its code check still runs), each with
/// <see cref="Text"/> ("Outranked by X" / "Cap outranked by X") and the fixed <see cref="Note"/>. The set is open: a reader must ignore a kind
/// it does not know. Members a kind does not use are null.
/// <para>084c fix round 1 (F1-1, F1-2): a <c>Changed</c> mark also names <see cref="BasePolicyId"/>, the base rule the change was made on (the
/// policy the change route takes, so a rule changed here can be changed again: remove this change, then change the base rule), and, while the
/// base rule is in force, <see cref="BaseRuleBody"/>: the body of its version in force (<see cref="CurrentBase"/>) as a JSON object, for "Start
/// from the published values". Both rows are asked of the caller (EB-D-218).</para>
/// </summary>
public sealed record RuleMarkView(
    string Kind, string? Reason = null, FirmView? Firm = null, int? Distance = null, ChainRecordView? ChainRecord = null, StampView? Stamp = null,
    Guid? OverrideRowId = null, Guid? RecordRuleId = null, PolicyVersionRefView? BasedOn = null, PolicyVersionRefView? CurrentBase = null,
    string? BaseCode = null, string? BaseParameterSummary = null, string? Text = null, string? Note = null, IReadOnlyList<OutrankingRuleView>? By = null,
    Guid? BasePolicyId = null, JsonElement? BaseRuleBody = null);

/// <summary>
/// 084e: one rule that outranks another on the record (R3: every such rule at the top rank is listed). <see cref="Source"/> is
/// <c>Procedure</c> (a rule of an applied Procedure, or a record rule in its base's slot; <see cref="ProcedureId"/> set) or <c>Legacy</c> (a
/// rule of a published Client default or Firm rule set with no Procedure). <see cref="Level"/> and <see cref="Distance"/> are its rank.
/// <see cref="Code"/> is the reader code (a record rule shows its base rule's code).
/// </summary>
public sealed record OutrankingRuleView(
    Guid PolicyId, Guid PolicyVersionId, string Code, string? Name, int VersionNumber, string Source, Guid? ProcedureId, string Level, int Distance);

/// <summary>
/// Reads the two lists of model 6.6 for a core record (task 084 Part A). <b>Read model, EB-D-218 option A:</b> the rows are read ONCE,
/// app-only, through the existing <see cref="IGenericEntityService"/> and <see cref="ProcedureResolver"/> (no caller-identity entity
/// service, no impersonation); then ONE batched caller-rights check covers EVERY row the answer exposes. Any row the caller cannot Read,
/// a rights question that was not answered, a caller that cannot be established, a faulted row read or a resolver fault is
/// <see cref="RecordProcedureReadStatus.Unavailable"/>: no list, never a partial one. Writes nothing.
/// </summary>
/// <remarks>
/// <para><b>One resolution.</b> Both lists come from <c>new ProcedureResolution(rows).RulesOn(date)</c>, the evaluator's own output. Marks
/// are computed from that output and the rows already read, at read time; nothing is stamped. The rules list's in-force members equal
/// <c>RulesOn(date).Rules</c> one for one. The Procedures list adds only what the resolver's output does not carry (every source, the Type,
/// who and when), read once, and the Procedures that are "not yet in force" on the date: the resolver neither applies nor gaps those, so
/// they are found by asking it for a date on which every Procedure that ever opened a window has opened (still its own output).</para>
/// <para><b>No firm.</b> The resolver refuses an empty firm. With no query firm and no <c>sprk_billingfirm</c>, <see cref="NoFirm"/> (a
/// fixed id that names no firm) is passed, so only Procedures with no Firm apply, only rule-level rows with no Firm count, and no
/// Firm-record set is read. The id never leaves this class.</para>
/// <para><b>Known cost.</b> The caller's rights are asked per distinct row (the probe runs 4 at a time), so a record with many rules
/// asks many questions; the ids are de-duplicated and paged (<see cref="RightsPageSize"/>).</para>
/// </remarks>
public class RecordProcedureReadService
{
    /// <summary>The 503 <c>errorCode</c> of every refusal that returns no list.</summary>
    public const string UnavailableErrorCode = "procedures.unavailable";

    /// <summary>
    /// The firm id passed to the resolver when the answer has no firm. It is a fixed value that no <c>sprk_organization</c> row can have
    /// (version 4 GUID with the reserved nil-prefix pattern), so no Procedure, rule row or record set matches it.
    /// </summary>
    internal static readonly Guid NoFirm = new("00000000-0000-4000-8000-00000000f1f0");

    /// <summary>The rows per batched rights call (a page), as <see cref="AffectedInvoiceCounter.RightsPageSize"/>.</summary>
    internal const int RightsPageSize = 250;

    private const int MaxInValues = 500;
    private const int PageSize = 5000;
    private const string QueryFirmSource = "Query";
    private const string BillingFirmColumn = "sprk_billingfirm";
    private const string CreatedByPersonColumn = "sprk_createdbyperson";
    private const string RuleSetSet = "sprk_rulesets";
    private const string PolicySet = "sprk_policies";
    private const string PolicyVersionSet = "sprk_policyversions";
    private const string OrganizationSet = FinanceAuthorizationFilter.VendorOrganizationEntitySet;
    private const string SystemUserSet = "systemusers";

    // 084e fix round 1 (ontology-r1 R4 wording, binding): the note of an Outranked / CapOutranked mark sits on the OUTRANKED row. {0} is the
    // outranking rules' codes as JoinCodes words them; the plural form is used when more than one version displaces at the top rank.

    /// <summary>The note of an <c>Outranked</c> mark, one outranking rule (<c>{0}</c> is its code).</summary>
    internal const string OutrankedNoteSingular =
        "does not run while {0} is in force; it can run if {0} is refused when an invoice is checked; a line can differ (dates, fallback)";

    /// <summary>The note of an <c>Outranked</c> mark, several outranking rules (<c>{0}</c> is "X and Y" / "X, Y and Z").</summary>
    internal const string OutrankedNotePlural =
        "does not run while {0} are in force; it can run if all of them are refused when an invoice is checked; a line can differ (dates, fallback)";

    /// <summary>The note of a <c>CapOutranked</c> mark, one outranking rule (<c>{0}</c> is its code).</summary>
    internal const string CapOutrankedNoteSingular =
        "its code check always runs; its cap yields to {0}'s and can apply if {0} is refused when an invoice is checked; a line can differ (dates, fallback)";

    /// <summary>The note of a <c>CapOutranked</c> mark, several outranking rules (<c>{0}</c> is "X and Y" / "X, Y and Z").</summary>
    internal const string CapOutrankedNotePlural =
        "its code check always runs; its cap yields to those of {0} and can apply if all of them are refused when an invoice is checked; a line can differ (dates, fallback)";

    private readonly IGenericEntityService _entities;
    private readonly CallerRecordAccessProbe _probe;
    private readonly RuleBodyDescriber _describer;
    private readonly BillingFactLoader _legacyRules;
    private readonly PolicyVersionValidator _gate;
    private readonly ILogger<RecordProcedureReadService> _logger;

    /// <summary>The clock for the default date ("today", UTC). A test seam; production uses the system clock.</summary>
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <param name="legacyRules">084e (R6): the evaluator's own legacy-rule read (<see cref="BillingFactLoader.LoadLegacyRulesAsync"/>, app-only).</param>
    /// <param name="gate">084e (R1): the billing gate, so a rule refused by it takes no part in precedence, as in the evaluator.</param>
    public RecordProcedureReadService(
        IGenericEntityService entities, CallerRecordAccessProbe probe, RuleBodyDescriber describer, BillingFactLoader legacyRules,
        PolicyVersionValidator gate, ILogger<RecordProcedureReadService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _describer = describer ?? throw new ArgumentNullException(nameof(describer));
        _legacyRules = legacyRules ?? throw new ArgumentNullException(nameof(legacyRules));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Entry points
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The Procedures <paramref name="coreTable"/>(<paramref name="recordId"/>) is subject to (model 6.6, first list).</summary>
    public virtual async Task<RecordProcedureReadResult<RecordProceduresView>> ListProceduresAsync(
        string coreTable, Guid recordId, Guid? firmId, DateOnly? date, string? callerBearerToken, CancellationToken ct)
    {
        try
        {
            var reading = await ReadAsync(coreTable, recordId, firmId, date, probeFutureDate: true, readLegacyRules: false, ct).ConfigureAwait(false);
            if (reading is null)
            {
                return RecordProcedureReadResult<RecordProceduresView>.NotFound();
            }

            var view = BuildProcedures(reading);
            return await CallerCanReadAsync(reading, callerBearerToken, ct).ConfigureAwait(false)
                ? RecordProcedureReadResult<RecordProceduresView>.Of(view)
                : RecordProcedureReadResult<RecordProceduresView>.Unavailable();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogUnavailable(ex, coreTable);
            return RecordProcedureReadResult<RecordProceduresView>.Unavailable();
        }
    }

    /// <summary>The rules in force on <paramref name="coreTable"/>(<paramref name="recordId"/>) (model 6.6, second list).</summary>
    public virtual async Task<RecordProcedureReadResult<RecordRulesInForceView>> ListRulesInForceAsync(
        string coreTable, Guid recordId, Guid? firmId, DateOnly? date, string? callerBearerToken, CancellationToken ct)
    {
        try
        {
            var reading = await ReadAsync(coreTable, recordId, firmId, date, probeFutureDate: false, readLegacyRules: true, ct).ConfigureAwait(false);
            if (reading is null)
            {
                return RecordProcedureReadResult<RecordRulesInForceView>.NotFound();
            }

            var view = await BuildRulesAsync(reading, ct).ConfigureAwait(false);
            return await CallerCanReadAsync(reading, callerBearerToken, ct).ConfigureAwait(false)
                ? RecordProcedureReadResult<RecordRulesInForceView>.Of(view)
                : RecordProcedureReadResult<RecordRulesInForceView>.Unavailable();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogUnavailable(ex, coreTable);
            return RecordProcedureReadResult<RecordRulesInForceView>.Unavailable();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Dry run (task 084d-2a; notes/084d-dry-run-design.md §2-§4, uac-r2 and ontology-r1 build conditions)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The label of the After side (ontology-r1 K1): the in-force date of a change comes from this request's clock.</summary>
    internal const string AfterLabel = "If saved now";

    /// <summary>R5 (uac-r2): the preview asks no owner.</summary>
    internal const string OwnerNote =
        "This preview does not check who will own the new rows, so saving can still be refused for that reason.";

    /// <summary>R10 (uac-r2), K2 (ontology-r1): Now and After are separate reads.</summary>
    internal const string SeparateReadsNote =
        "Now and After are read separately; a change someone else saves in between shows as part of the difference.";

    /// <summary>"If saved now" is what the lists would show; it is not a promise that saving will succeed.</summary>
    internal const string NotAPromiseNote =
        "After shows what the lists would show if this change were saved now. It does not mean that saving will succeed.";

    /// <summary>Test seam (084d-2a): told each time a dry run exempts synthetic rows. Production leaves it null.</summary>
    internal Action<IReadOnlyCollection<(string Set, Guid Id)>>? OnExempt { get; set; }

    /// <summary>
    /// Task 084d-2a: the two lists of <paramref name="coreTable"/>(<paramref name="recordId"/>) now, and as they would read with one proposed
    /// override written ("if saved now"), without writing. Planned by <paramref name="planner"/>'s <c>Plan*Async</c> through a
    /// <see cref="WriteRefusingEntityService"/> (a replaced override is planned first, and the create is planned over its overlay); After is
    /// read through a per-request <see cref="ProcedureRowOverlay"/> of the plan, by the same reader as Now.
    /// </summary>
    /// <remarks>
    /// <para><b>EB-D-218.</b> ONE batched caller check over Now ∪ After. The only rows not asked are the synthetic ones: the rows the plan
    /// creates whose by-id load, app-only on the real service, found nothing (exact (entity set, id) pairs). That load runs before any
    /// exemption, and a fault in it is 503 <c>procedures.unavailable</c> with nothing exempted (uac-r2 condition 1). A created id that exists
    /// already (a switch or a re-change keeps the derived id) is real, so it is asked (condition 2). A firm the request names is asked as a query
    /// firm (R6). Proposed rows have no creator in After (ontology-r1 F3).</para>
    /// <para><b>Refusals.</b> Planning refuses with the write's own status and code (<see cref="RecordProcedureReadStatus.PlanRefused"/>); a
    /// gone record is the uniform 404; anything else is <see cref="RecordProcedureReadStatus.Unavailable"/>.</para>
    /// </remarks>
    internal virtual async Task<RecordProcedureDryRunResult> DryRunAsync(
        ProcedureAssignmentService planner, string coreTable, Guid recordId, ProcedureDryRunRequest request, Guid? firmId, DateOnly? date,
        string? callerBearerToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            // One instant per request: the created rows' stamp and a change's in-force date ("if saved now").
            var now = Clock.GetUtcNow().UtcDateTime;
            var planned = await PlanAsync(planner, coreTable, recordId, request, now, ct).ConfigureAwait(false);
            if (planned.Kind != RuleSetOutcomeKind.Done)
            {
                return new RecordProcedureDryRunResult(RecordProcedureReadStatus.PlanRefused, null, planned);
            }

            var plan = planned.Value!;

            // uac-r2 condition 1: decided app-only on the real service BEFORE anything is exempted; a fault throws to the 503 below.
            var synthetic = await SyntheticRowsAsync(plan, ct).ConfigureAwait(false);

            var nowReading = await ReadAsync(coreTable, recordId, firmId, date, probeFutureDate: true, readLegacyRules: true, ct).ConfigureAwait(false);
            if (nowReading is null)
            {
                return new RecordProcedureDryRunResult(RecordProcedureReadStatus.RecordNotFound, null);
            }

            var proposed = plan.Created.Select(c => c.Id).ToList();
            var overlay = new ProcedureRowOverlay(_entities, plan, now);
            var afterReading = await ReadAsync(
                coreTable, recordId, firmId, date, probeFutureDate: true, readLegacyRules: true, ct,
                rowSource: overlay, legacyRules: nowReading.LegacyRules, proposedRowIds: proposed).ConfigureAwait(false);
            if (afterReading is null)
            {
                return new RecordProcedureDryRunResult(RecordProcedureReadStatus.RecordNotFound, null);
            }

            if (synthetic.Count > 0)
            {
                // Safe only because ReadAsync never calls Expose: every exposure happens in BuildProcedures / BuildRulesAsync, after this Exempt.
                afterReading.Exempt(synthetic);
                OnExempt?.Invoke(synthetic);
            }

            var nowSide = new ProcedureDryRunSideView(BuildProcedures(nowReading), await BuildRulesAsync(nowReading, ct).ConfigureAwait(false));
            var afterSide = new ProcedureDryRunSideView(BuildProcedures(afterReading), await BuildRulesAsync(afterReading, ct).ConfigureAwait(false));

            // R6: a firm the request names has nothing vouching for it, so it is asked as a query firm is.
            var requestFirm = RequestFirmOf(request);
            if (requestFirm is { } named)
            {
                afterReading.Expose(OrganizationSet, named);
            }

            nowReading.Expose(SetOf(coreTable), recordId);

            // EB-D-218: the proposal names the override a removal removes, or a create replaces, and its kind. Both come from an app-only read of
            // the stored row, and the row can sit in no list (a Follow row of a Retired Procedure shows in no view), so no view asked about it.
            // It is a real row, never exempt.
            if ((request.Kind == ProcedureDryRunRequest.KindRemove ? request.OverrideId : request.ReplacesOverrideId) is { } overrideRow)
            {
                nowReading.Expose(ProcedureAssignmentEntity.EntitySet, overrideRow);
            }

            // 084d-2b: what the write would close (selection only), and the affected count. A closing write counts the invoices whose findings
            // close (the write's own selection, counted by AffectedInvoiceCounter); any other counts the invoices whose resolution changes, and
            // every row that comparison read is asked in the ONE check below (EB-D-218), except the synthetic rows.
            var closes = await planner.PlanClosesAsync(overlay, coreTable, recordId, plan, callerBearerToken, ct).ConfigureAwait(false);
            ProcedureDryRunAffectedView affected;
            var exposed = nowReading.Exposed.Concat(afterReading.Exposed).ToHashSet();
            if (closes.Kinds.Count > 0)
            {
                affected = new ProcedureDryRunAffectedView(
                    closes.VisibleOpenInvoices, ProcedureService.AffectedText(closes.VisibleOpenInvoices), AffectedBasisFindingsClosed);
            }
            else
            {
                var (count, compared) = await ResolutionChangedAsync(planner, overlay, coreTable, recordId, callerBearerToken, ct).ConfigureAwait(false);
                compared.ExceptWith(synthetic);
                exposed.UnionWith(compared);
                affected = new ProcedureDryRunAffectedView(count, ProcedureService.AffectedText(count), AffectedBasisResolutionChanged);
            }

            if (!await CallerCanReadAllAsync(exposed, callerBearerToken, ct).ConfigureAwait(false))
            {
                return new RecordProcedureDryRunResult(RecordProcedureReadStatus.Unavailable, null);
            }

            var removes = request.Kind == ProcedureDryRunRequest.KindRemove ? request.OverrideId : null;
            var view = new ProcedureDryRunView(
                RecordView(nowReading, recordId), nowReading.Date, nowSide.Procedures.Firm, nowReading.FirmSource,
                new ProcedureDryRunProposalView(request.Kind!, plan.Kind, request.ReplacesOverrideId, removes, proposed),
                nowSide, afterSide, AfterLabel,
                new ProcedureDryRunDisclosureView(
                    PredictsOwnerRefusal: false, ReadAtOneInstant: false, RequestFirmAsked: requestFirm is not null,
                    [NotAPromiseNote, OwnerNote, SeparateReadsNote]),
                new ProcedureDryRunClosesView(
                    closes.Kinds.Contains(PlannedCloses.ChangedRuleRemoved), closes.Kinds, closes.Warning, closes.Message),
                affected);
            return new RecordProcedureDryRunResult(RecordProcedureReadStatus.Done, view);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogUnavailable(ex, coreTable);
            return new RecordProcedureDryRunResult(RecordProcedureReadStatus.Unavailable, null);
        }
    }

    /// <summary>
    /// The plan of the proposal: a removal (or the override a create replaces) first, through the planning service; then the create, through
    /// an overlay of that removal, so it is checked against the world after the removal (a switch re-creates the same derived id). The two are
    /// merged: the create's image of a row wins over the removal's (a re-change switches back On the policy the removal switched Off).
    /// </summary>
    private async Task<RuleSetOutcome<ProcedureOverridePlan>> PlanAsync(
        ProcedureAssignmentService planner, string coreTable, Guid recordId, ProcedureDryRunRequest request, DateTime now, CancellationToken ct)
    {
        var reads = new WriteRefusingEntityService(_entities);
        var removeId = request.Kind == ProcedureDryRunRequest.KindRemove ? request.OverrideId : request.ReplacesOverrideId;
        ProcedureOverridePlan? removal = null;
        if (removeId is { } id)
        {
            var removed = await planner.PlanRemoveAsync(reads, coreTable, recordId, id, ct).ConfigureAwait(false);
            if (removed.Kind != RuleSetOutcomeKind.Done || request.Kind == ProcedureDryRunRequest.KindRemove)
            {
                return removed;
            }

            removal = removed.Value!;
        }

        WriteRefusingEntityService createReads = removal is null ? reads : new ProcedureRowOverlay(_entities, removal, now);
        var created = request.Kind switch
        {
            ProcedureDryRunRequest.KindAssign => await planner.PlanAssignProcedureAsync(createReads, coreTable, recordId, request.Assign!, ct).ConfigureAwait(false),
            ProcedureDryRunRequest.KindExcludeRule => await planner.PlanExcludeRuleAsync(createReads, coreTable, recordId, request.ExcludeRule!, ct).ConfigureAwait(false),
            ProcedureDryRunRequest.KindChangeRule => await planner.PlanChangeRuleAsync(createReads, coreTable, recordId, request.ChangeRule!, now, ct).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Not a dry-run kind."),
        };
        if (created.Kind != RuleSetOutcomeKind.Done || removal is null)
        {
            return created;
        }

        var plan = created.Value!;
        var images = removal.AfterImages.ToDictionary(i => (i.LogicalName, i.Id));
        foreach (var image in plan.AfterImages)
        {
            images[(image.LogicalName, image.Id)] = image;
        }

        // 084d-2b: the removal's switch-off closes the removed changed rule's findings (the two-call plan's first call), whatever the create does.
        return RuleSetOutcome<ProcedureOverridePlan>.Done(
            plan with { AfterImages = images.Values.ToList(), Deleted = removal.Deleted, SwitchedOffRuleId = removal.SwitchedOffRuleId });
    }

    /// <summary>
    /// The synthetic rows of a plan: the created rows that do not exist yet, by an app-only by-id load on the REAL service (never the overlay),
    /// as (entity set, id). A created id that is found is real and is not returned (uac-r2 condition 2). A fault throws: it never reads as
    /// "found nothing" (condition 1).
    /// </summary>
    private async Task<HashSet<(string Set, Guid Id)>> SyntheticRowsAsync(ProcedureOverridePlan plan, CancellationToken ct)
    {
        var synthetic = new HashSet<(string Set, Guid Id)>();
        foreach (var table in plan.Created.GroupBy(c => c.LogicalName, StringComparer.Ordinal))
        {
            var found = (await ReadByIdsAsync(table.Key, table.Select(c => c.Id), [table.Key + "id"], ct).ConfigureAwait(false))
                .Select(r => r.Id).ToHashSet();
            foreach (var row in table.Where(c => !found.Contains(c.Id)))
            {
                synthetic.Add((OverrideTableSetOf(table.Key), row.Id));
            }
        }

        return synthetic;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Assignable Procedures (task 084d-5a; notes/084d-dry-run-design.md "084d-5 design" and its binding conditions)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>EB-D-227: the most Procedures the assignable list offers; past it the list is null and <c>tooMany</c> is set.</summary>
    internal const int AssignableBound = 50;

    /// <summary>The label of the assignable list (ontology-r1 K1: "if assigned now", from this request's clock).</summary>
    internal const string AssignableLabel = "If assigned now";

    /// <summary>The reason of a <see cref="NotOfferedProcedureView"/>: the record already has an override row for the Procedure.</summary>
    internal const string OwnRowReason = "OwnRow";

    /// <summary>EB-D-230 (O-22): what assigning a Procedure that would be a Gap on the date does.</summary>
    internal const string GapWarning =
        "No version of this Procedure would apply on this date. If you assign it, invoices checked from then on are held until a version applies.";

    /// <summary>
    /// Task 084d-5a: the Procedures <paramref name="coreTable"/>(<paramref name="recordId"/>) could be assigned (Follow latest), each as it would
    /// show if assigned now, for the lists' firm and date. Nothing is written.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><b>Now</b>: the record's Procedures list, read and built exactly as <see cref="ListProceduresAsync"/> does. Every Procedure in it, of
    /// any status, is on the record.</item>
    /// <item><b>Candidates and own rows</b>: <see cref="ProcedureAssignmentService.PlanAssignCatalogAsync"/> through a
    /// <see cref="WriteRefusingEntityService"/>, minus the Procedures on the record, then minus the ones the caller cannot Read (dropped BEFORE
    /// the bound and the resolver, <see cref="ReadableProceduresAsync"/>), bounded (<see cref="AssignableBound"/>).</item>
    /// <item><b>After</b>: the same reader over a per-request <see cref="ProcedureRowOverlay"/> of the catalog's ONE plan (each candidate's
    /// Follow-latest row image), with the plan's rows as proposed rows (ontology-r1 F3). <b>Offered</b> = the After items not in Now: the
    /// resolver decides applicability, firm, the published window and the version (an unpublished Procedure drops out there).</item>
    /// </list>
    /// <para><b>EB-D-218, uac-r2 conditions 1-2 and notes 1-2.</b> The synthetic set is decided app-only on the real service before anything is
    /// exempted (a fault is 503); a colliding real id is asked. ONE batched caller check over Now's exposed rows (the record and a query firm
    /// among them), After's, and each <c>OwnRow</c> entry's Procedure and own rows (inactive included). It runs on the <c>tooMany</c> path too, over
    /// Now's. Any refusal, unanswered row or fault is <see cref="RecordProcedureReadStatus.Unavailable"/>: never a partial list.</para>
    /// <para><b>Candidates (uac-r2 ruling 2026-11-13, replacing 503-on-an-unreadable-candidate).</b> The candidate query is app-only, so the
    /// caller's Read on each candidate Procedure is asked first, in its own batch (<see cref="ReadableProceduresAsync"/>), and the unreadable
    /// ones are dropped before the resolver, the bound and <c>tooMany</c>: the answer is the same as if they did not exist, so neither the status
    /// nor the list nor <c>tooMany</c> can reveal one. That check refused as a whole or faulting is 503, never "drop all". Now's, own rows' and
    /// the firm's rows stay all-or-nothing, as above. Accepted limit: the candidate batch adds one Read question per candidate (at most
    /// <see cref="ProcedureAssignmentService.MaxCandidateRows"/>) to the request's time.</para>
    /// </remarks>
    internal virtual async Task<RecordProcedureReadResult<RecordAssignableProceduresView>> ListAssignableAsync(
        ProcedureAssignmentService planner, string coreTable, Guid recordId, Guid? firmId, DateOnly? date, string? nameContains,
        string? callerBearerToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(planner);
        try
        {
            var stamp = Clock.GetUtcNow().UtcDateTime;
            var nowReading = await ReadAsync(coreTable, recordId, firmId, date, probeFutureDate: true, readLegacyRules: false, ct).ConfigureAwait(false);
            if (nowReading is null)
            {
                return RecordProcedureReadResult<RecordAssignableProceduresView>.NotFound();
            }

            var nowView = BuildProcedures(nowReading);
            nowReading.Expose(SetOf(coreTable), recordId);
            var onRecord = nowView.Procedures.Select(p => p.Id).ToHashSet();

            var catalog = await planner.PlanAssignCatalogAsync(
                new WriteRefusingEntityService(_entities), coreTable, recordId, nowReading.FirmId, onRecord, nameContains, AssignableBound,
                (ids, token) => ReadableProceduresAsync(ids, callerBearerToken, token), ct).ConfigureAwait(false);
            if (catalog.Kind != RuleSetOutcomeKind.Done)
            {
                _logger.LogWarning("Record procedures: the assignable Procedures of a {CoreTable} could not be read ({ErrorCode}); 503 {Unavailable}",
                    coreTable, catalog.ErrorCode, UnavailableErrorCode);
                return RecordProcedureReadResult<RecordAssignableProceduresView>.Unavailable();
            }

            var exposed = nowReading.Exposed.ToHashSet();
            var found = catalog.Value!;
            IReadOnlyList<AssignableProcedureItem>? offered = null;
            var notOffered = new List<NotOfferedProcedureView>();
            if (!found.TooMany)
            {
                // uac-r2 note 2: the own row decides the refusal, so it is asked, inactive or not, with its Procedure.
                foreach (var own in found.OwnRows)
                {
                    exposed.Add((ProcedureEntity.EntitySet, own.ProcedureId));
                    exposed.UnionWith(own.RowIds.Select(id => (ProcedureAssignmentEntity.EntitySet, id)));
                    notOffered.Add(new NotOfferedProcedureView(own.ProcedureId, own.Name, OwnRowReason));
                }

                offered = [];
                if (found.Plan is { Created.Count: > 0 } plan)
                {
                    // uac-r2 condition 1: decided app-only on the real service BEFORE anything is exempted; a fault throws to the 503 below.
                    var synthetic = await SyntheticRowsAsync(plan, ct).ConfigureAwait(false);
                    var afterReading = await ReadAsync(
                        coreTable, recordId, firmId, date, probeFutureDate: true, readLegacyRules: false, ct,
                        rowSource: new ProcedureRowOverlay(_entities, plan, stamp), proposedRowIds: plan.Created.Select(c => c.Id).ToList()).ConfigureAwait(false);
                    if (afterReading is null)
                    {
                        return RecordProcedureReadResult<RecordAssignableProceduresView>.NotFound();
                    }

                    if (synthetic.Count > 0)
                    {
                        // Safe only because ReadAsync never calls Expose: every exposure happens in BuildProcedures, after this Exempt.
                        afterReading.Exempt(synthetic);
                        OnExempt?.Invoke(synthetic);
                    }

                    var afterView = BuildProcedures(afterReading);
                    exposed.UnionWith(afterReading.Exposed);
                    var gaps = afterView.Gaps.ToDictionary(g => g.ProcedureId);
                    offered = afterView.Procedures
                        .Where(p => !onRecord.Contains(p.Id))
                        .Select(p => new AssignableProcedureItem(
                            p.Id, p.Name, p.Type, p.AppliesTo, p.Firm, p.Status, p.VersionInForce,
                            gaps.TryGetValue(p.Id, out var gap) ? new AssignableGapView(gap.Kind, gap.Text, GapWarning) : null))
                        .OrderBy(p => p.Name, StringComparer.Ordinal)
                        .ThenBy(p => p.Id)
                        .ToList();
                }
            }

            // ONE batched check (EB-D-218): on the tooMany path it covers Now's rows, the record and a query firm among them (uac-r2 note 1).
            if (!await CallerCanReadAllAsync(exposed, callerBearerToken, ct).ConfigureAwait(false))
            {
                return RecordProcedureReadResult<RecordAssignableProceduresView>.Unavailable();
            }

            return RecordProcedureReadResult<RecordAssignableProceduresView>.Of(new RecordAssignableProceduresView(
                RecordView(nowReading, recordId), nowReading.Date, nowView.Firm, nowReading.FirmSource, nameContains, AssignableBound, found.TooMany,
                offered, notOffered.OrderBy(n => n.Name, StringComparer.Ordinal).ThenBy(n => n.Id).ToList(), AssignableLabel));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogUnavailable(ex, coreTable);
            return RecordProcedureReadResult<RecordAssignableProceduresView>.Unavailable();
        }
    }

    /// <summary>084d-2b: the affected count's basis when the write closes findings.</summary>
    internal const string AffectedBasisFindingsClosed = "FindingsClosed";

    /// <summary>084d-2b: the affected count's basis when the write closes nothing.</summary>
    internal const string AffectedBasisResolutionChanged = "ResolutionChanged";

    /// <summary>
    /// 084d-2b: the most resolutions (core record and firm pairs, each read twice: Now and After) the affected count compares. Past it the count
    /// is null. The records under the record are bounded separately (<see cref="ProcedureAssignmentService.MaxChildRecords"/>).
    /// </summary>
    internal const int MaxComparedResolutions = 10;

    /// <summary>
    /// Task 084d-2b: for a write that closes nothing, how many open invoices the caller can Read whose Procedure resolution differs between Now
    /// and After, and the rows that comparison read (the caller must be able to Read each: they join the ONE check). Model 8.2's comparison: an
    /// invoice differs when, on any date it is judged on, the Procedures applied and their versions, the rules in force and their versions, the
    /// exclusions, the gaps, or the override rows that decided them differ.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>The In Review invoices filed on the record or directly under it (<see cref="ProcedureAssignmentService.InReviewInvoicesInReachAsync"/>;
    /// a grandchild's chain does not reach the record, D-172).</item>
    /// <item>Narrowed FIRST to the ones the caller can Read (<see cref="AffectedInvoiceCounter.VisibleOpenInvoicesAsync"/>): an invoice the caller
    /// cannot Read is never judged, so nothing about it (or the record it is filed on) reaches the count or the check.</item>
    /// <item>Only then is anything judged: a VISIBLE invoice that cannot be judged (no date, or not filed on exactly one core record) makes the count
    /// null; one the caller cannot Read is dropped like any other, and its lines are never read (<see cref="ProcedureAssignmentService.JudgedOnAsync"/>).</item>
    /// <item>One resolution per (core record, firm) on each side, at most <see cref="MaxComparedResolutions"/>; the After side reads through the
    /// request's overlay.</item>
    /// </list>
    /// Null, with no rows, when the reach cannot be read, a bound is passed (the reach's invoices past <see cref="AffectedInvoiceCounter.MaxDryRunInvoices"/>,
    /// or the resolutions past <see cref="MaxComparedResolutions"/>), a visible invoice has no firm or cannot be judged, or a resolution faults.
    /// </remarks>
    private async Task<(int? Count, HashSet<(string Set, Guid Id)> Compared)> ResolutionChangedAsync(
        ProcedureAssignmentService planner, ProcedureRowOverlay after, string coreTable, Guid recordId, string? callerBearerToken, CancellationToken ct)
    {
        var compared = new HashSet<(string Set, Guid Id)>();
        try
        {
            var reads = new WriteRefusingEntityService(_entities);
            var reach = await planner.InReviewInvoicesInReachAsync(reads, coreTable, recordId, ct).ConfigureAwait(false);
            if (reach.Count == 0)
            {
                return (0, compared);
            }

            if (await planner.VisibleOpenInvoicesAsync(reach.Select(i => i.InvoiceId).ToList(), callerBearerToken, ct).ConfigureAwait(false) is not { } visible)
            {
                return (null, compared);
            }

            var kept = reach.Where(i => visible.Contains(i.InvoiceId)).ToList();
            if (kept.Any(i => !i.Judgeable))
            {
                // F-2: decided only now, among the invoices the caller can Read. An invoice the caller cannot Read was dropped above, judgeable or not.
                _logger.LogInformation("Dry run: an invoice the caller can read cannot be judged (no date, or not filed on one core record); no count is shown");
                return (null, compared);
            }

            var judged = (await planner.JudgedOnAsync(reads, kept).ConfigureAwait(false)).ToList();
            var groups = judged.GroupBy(i => (i.CoreTable, i.CoreId, i.FirmId)).ToList();
            if (judged.Any(i => i.FirmId is null || i.FirmId == Guid.Empty) || groups.Count > MaxComparedResolutions)
            {
                _logger.LogInformation("Dry run: {Groups} resolution(s) to compare (bound {Bound}), or an invoice with no firm; no count is shown",
                    groups.Count, MaxComparedResolutions);
                return (null, compared);
            }

            var differs = 0;
            foreach (var group in groups)
            {
                var core = new CoreRecordReference(KindOf(group.Key.CoreTable), group.Key.CoreId);
                var before = await ProcedureResolver.ReadRowsAsync(_entities, _logger, core, group.Key.FirmId!.Value, ct).ConfigureAwait(false);
                var afterRows = await ProcedureResolver.ReadRowsAsync(after, _logger, core, group.Key.FirmId!.Value, ct).ConfigureAwait(false);
                AddRowsOf(compared, before);
                AddRowsOf(compared, afterRows);
                var now = new ProcedureResolution(before);
                var then = new ProcedureResolution(afterRows);
                differs += group.Count(i => i.JudgedOn.Any(d => OutcomeOf(now.RulesOn(d)) != OutcomeOf(then.RulesOn(d))));
            }

            return (differs, compared);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Dry run: the invoices whose resolution changes could not be told ({ExceptionType}); no count is shown", ex.GetType().Name);
            return (null, new HashSet<(string Set, Guid Id)>());
        }
    }

    /// <summary>Every row a resolution was built from, as (entity set, id): what the caller must be able to Read for the count to show.</summary>
    private static void AddRowsOf(HashSet<(string Set, Guid Id)> rows, ProcedureRows read)
    {
        rows.UnionWith(read.Chain.Select(l => (SetOf(l.Table), l.Id)));
        rows.UnionWith(read.Overrides.Select(o => (ProcedureAssignmentEntity.EntitySet, o.RowId)));
        rows.UnionWith(read.Procedures.Select(p => (ProcedureEntity.EntitySet, p.Id)));
        rows.UnionWith(read.Versions.Select(v => (ProcedureEntity.VersionEntitySet, v.Id)));
        rows.UnionWith(read.Sets.Select(s => (RuleSetSet, s.Id)));
        rows.UnionWith(read.Policies.Select(p => (PolicySet, p.Id)));
        rows.UnionWith(read.PolicyVersions.Select(v => (PolicyVersionSet, v.PolicyVersionId)));
    }

    /// <summary>
    /// Model 8.2's comparison of one judged date, as one text: the Procedures applied (version, pin), the rules in force (policy version, the
    /// record rule's Exclude row), the excluded rules and Procedures, the gaps, and the override rows that decided each (row, mode, subject, pin,
    /// firm). Ordered, so equal outcomes give equal text.
    /// </summary>
    private static string OutcomeOf(ProcedureRulesOnDate d)
    {
        static string Rows(IEnumerable<ProcedureOverrideRow> rows) =>
            string.Join(",", rows.Select(o => $"{o.RowId}/{o.Mode}/{o.ChainRecord.Id}/{o.ProcedureId}/{o.PinnedVersionId}/{o.PolicyId}/{o.FirmId}").Order(StringComparer.Ordinal));

        return string.Join("|",
            d.Procedures.Select(p => $"P{p.ProcedureId}:{p.ProcedureVersionId}:{p.Pinned}:{Rows(p.Overrides)}")
                .Concat(d.Rules.Select(r => $"R{r.Version.PolicyVersionId}:{r.RecordRule?.BasePolicyVersionId}:{(r.RecordRule?.Exclude is { } x ? Rows([x]) : "")}"))
                .Concat(d.ExcludedRules.Select(e => $"X{e.Version.PolicyVersionId}:{e.ReplacedByRecordRule}:{Rows(e.Overrides)}"))
                .Concat(d.ExcludedProcedures.Select(e => $"E{e.ProcedureId}:{Rows(e.Overrides)}"))
                .Concat(d.Gaps.Select(g => $"G{g.ProcedureId}:{g.Kind}:{Rows(g.Overrides)}"))
                .Order(StringComparer.Ordinal));
    }

    /// <summary>The firm the request body names (a rule Exclude's or a change's <c>firmId</c>), for the kind it names.</summary>
    private static Guid? RequestFirmOf(ProcedureDryRunRequest request) => request.Kind switch
    {
        ProcedureDryRunRequest.KindExcludeRule => request.ExcludeRule?.FirmId,
        ProcedureDryRunRequest.KindChangeRule => request.ChangeRule?.FirmId,
        _ => null,
    } is { } firm && firm != Guid.Empty ? firm : null;

    /// <summary>The entity set of one of the four override tables (the sets <see cref="Reading.Expose"/> uses for them).</summary>
    private static string OverrideTableSetOf(string table) => table switch
    {
        ProcedureAssignmentEntity.Entity => ProcedureAssignmentEntity.EntitySet,
        BillingFactLoader.RuleSetEntity => RuleSetSet,
        BillingFactLoader.PolicyEntity => PolicySet,
        BillingFactLoader.VersionEntity => PolicyVersionSet,
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, "A dry run creates rows of the four override tables only."),
    };

    private void LogUnavailable(Exception ex, string coreTable)
    {
        // K2 (ProcedureResolutionFaultException): its message names row ids for operators, so it is logged and never returned.
        // The 503 is the same for every fault; the level is not: a fault the code expects (bad data, a Dataverse or HTTP fault) is a
        // warning, anything else is a defect an operator must see.
        if (IsExpectedFault(ex))
        {
            _logger.LogWarning(ex, "Record procedures: the answer for a {CoreTable} could not be built; 503 {ErrorCode}", coreTable, UnavailableErrorCode);
        }
        else
        {
            _logger.LogError(ex, "Record procedures: unexpected fault building the answer for a {CoreTable}; 503 {ErrorCode}", coreTable, UnavailableErrorCode);
        }
    }

    /// <summary>The faults this reader expects: the resolver's own (bad rows), a Dataverse service fault or an HTTP fault (rights or WhoAmI call).</summary>
    private static bool IsExpectedFault(Exception ex) =>
        ex is ProcedureResolutionFaultException or System.ServiceModel.FaultException or HttpRequestException;

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // One reading: the rows (once), the resolution (once), the names
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    private sealed class Reading
    {
        public required string CoreTable { get; init; }
        public required Guid RecordId { get; init; }
        public required DateOnly Date { get; init; }
        public required Guid? FirmId { get; init; }
        public required string FirmSource { get; init; }
        public required ProcedureRows Rows { get; init; }
        public required ProcedureRulesOnDate OnDate { get; init; }
        public ProcedureRulesOnDate? OnFutureDate { get; init; }
        public required IReadOnlyDictionary<Guid, string?> RecordNames { get; init; }
        public required IReadOnlyDictionary<Guid, string?> FirmNames { get; init; }
        public required IReadOnlyDictionary<Guid, int?> ProcedureTypes { get; init; }
        public required IReadOnlyDictionary<Guid, Guid?> RowCreators { get; init; }
        public required IReadOnlyDictionary<Guid, string?> UserNames { get; init; }

        /// <summary>084e: the legacy rules (every version that can be in force), as the evaluator's loader reads them. Empty on the Procedures route.</summary>
        public IReadOnlyList<BillingRuleCandidate> LegacyRules { get; init; } = [];

        /// <summary>Every row the answer exposes, as (entity set, id): what the caller must be able to Read (EB-D-218).</summary>
        public HashSet<(string Set, Guid Id)> Exposed { get; } = new();

        /// <summary>Task 084d-2a: the exempt rows the answer exposes (kept apart for tests; never asked). Empty unless <see cref="Exempt"/> ran.</summary>
        public HashSet<(string Set, Guid Id)> ExposedExempt { get; } = new();

        private readonly HashSet<(string Set, Guid Id)> _exempt = new();

        /// <summary>
        /// Task 084d-2a (EB-D-218, uac-r2 conditions 1 and 2): the dry run's synthetic rows, by exact (entity set, id), never a pattern. A row
        /// the planned write would create and that does not exist yet cannot be asked of Dataverse, so it is recorded in
        /// <see cref="ExposedExempt"/> instead of <see cref="Exposed"/>. Every other row, a real row the overlay patches included, is asked.
        /// </summary>
        public void Exempt(IEnumerable<(string Set, Guid Id)> rows) => _exempt.UnionWith(rows);

        public Guid Expose(string set, Guid id)
        {
            (_exempt.Contains((set, id)) ? ExposedExempt : Exposed).Add((set, id));
            return id;
        }
    }

    /// <param name="rowSource">The resolver's row source: the application service, or (084d-2a) a dry run's per-request overlay.</param>
    /// <param name="legacyRules">084d-2a: legacy rules already read for this request (the dry run reads them once for both sides).</param>
    /// <param name="proposedRowIds">084d-2a (ontology-r1 F3): rows the dry run proposes. They have no creator yet: a replaced row keeps its id, so
    /// the creator read by id would name the OLD row's creator; it is dropped, so the stamp says nobody and no person is exposed.</param>
    private async Task<Reading?> ReadAsync(
        string coreTable, Guid recordId, Guid? firmId, DateOnly? date, bool probeFutureDate, bool readLegacyRules, CancellationToken ct,
        IGenericEntityService? rowSource = null, IReadOnlyList<BillingRuleCandidate>? legacyRules = null, IReadOnlyCollection<Guid>? proposedRowIds = null)
    {
        var kind = KindOf(coreTable);
        var core = (await ReadByIdsAsync(coreTable, [recordId], [NameColumnOf(coreTable), BillingFirmColumn], ct).ConfigureAwait(false))
            .FirstOrDefault();
        if (core is null)
        {
            return null;
        }

        var billingFirm = core.GetAttributeValue<EntityReference>(BillingFirmColumn)?.Id;
        var (firm, source) = firmId is { } chosen && chosen != Guid.Empty ? (chosen, QueryFirmSource)
            : billingFirm is { } own && own != Guid.Empty ? (own, "Record")
            : ((Guid?)null, "None");

        var rows = await ProcedureResolver.ReadRowsAsync(
            rowSource ?? _entities, _logger, new CoreRecordReference(kind, recordId), firm ?? NoFirm, ct).ConfigureAwait(false);
        var resolution = new ProcedureResolution(rows);
        var workDate = date ?? DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);
        var onDate = resolution.RulesOn(workDate);

        // A Procedure whose first version opens after the date is neither applied nor a gap (F2-d), so the date's output does not list it.
        // The same resolution, asked for a date on which every Procedure that ever opened a window has opened, names every candidate.
        ProcedureRulesOnDate? onFuture = null;
        if (probeFutureDate)
        {
            var latestFrom = rows.Versions.Where(v => v.From is not null).Select(v => v.From!.Value).DefaultIfEmpty(workDate).Max();
            onFuture = latestFrom > workDate ? resolution.RulesOn(latestFrom) : onDate;
        }

        // 084e (R6): the rules route's "Outranked" mark groups what the evaluator groups, legacy rule sets included, so the legacy stream is read
        // with the evaluator's own reader (app-only, for the same firm) and checked disjoint from the Procedure stream as the loader checks it.
        IReadOnlyList<BillingRuleCandidate> legacy = [];
        if (readLegacyRules)
        {
            legacy = legacyRules ?? await _legacyRules.LoadLegacyRulesAsync(firm ?? NoFirm, ct).ConfigureAwait(false);
            BillingFactLoader.RequireDisjoint(legacy, resolution);
        }

        // The names and stamps the resolver's rows do not carry: one read per table, of ids the rows already hold.
        var recordNames = new Dictionary<Guid, string?> { [recordId] = NameOf(core, coreTable) };
        foreach (var group in rows.Chain.Where(l => l.Id != recordId).GroupBy(l => l.Table))
        {
            foreach (var row in await ReadByIdsAsync(group.Key, group.Select(l => l.Id), [NameColumnOf(group.Key)], ct).ConfigureAwait(false))
            {
                recordNames[row.Id] = NameOf(row, group.Key);
            }
        }

        var firmIds = rows.Procedures.Where(p => p.FirmId is not null).Select(p => p.FirmId!.Value)
            .Concat(rows.Overrides.Where(o => o.FirmId is not null).Select(o => o.FirmId!.Value))
            .Concat(rows.Sets.Where(s => s.FirmId is not null).Select(s => s.FirmId!.Value))
            .Concat(firm is { } f ? [f] : []);
        var firmNames = (await ReadByIdsAsync("sprk_organization", firmIds, ["sprk_organizationname"], ct).ConfigureAwait(false))
            .ToDictionary(r => r.Id, r => (string?)r.GetAttributeValue<string>("sprk_organizationname"));

        var types = (await ReadByIdsAsync(
                ProcedureEntity.Entity, rows.Procedures.Select(p => p.Id), [ProcedureEntity.TypeColumn], ct).ConfigureAwait(false))
            .ToDictionary(r => r.Id, r => r.GetAttributeValue<OptionSetValue>(ProcedureEntity.TypeColumn)?.Value);

        var creators = (await ReadByIdsAsync(
                ProcedureAssignmentEntity.Entity, rows.Overrides.Select(o => o.RowId), [CreatedByPersonColumn], ct).ConfigureAwait(false))
            .ToDictionary(r => r.Id, r => r.GetAttributeValue<EntityReference>(CreatedByPersonColumn)?.Id);
        foreach (var proposed in proposedRowIds ?? [])
        {
            creators.Remove(proposed);
        }

        var userNames = (await ReadByIdsAsync("systemuser", creators.Values.OfType<Guid>(), ["fullname"], ct).ConfigureAwait(false))
            .ToDictionary(r => r.Id, r => (string?)r.GetAttributeValue<string>("fullname"));

        return new Reading
        {
            CoreTable = coreTable,
            RecordId = recordId,
            Date = workDate,
            FirmId = firm,
            FirmSource = source,
            Rows = rows,
            OnDate = onDate,
            OnFutureDate = onFuture,
            RecordNames = recordNames,
            FirmNames = firmNames,
            ProcedureTypes = types.ToDictionary(kv => kv.Key, kv => kv.Value),
            RowCreators = creators,
            UserNames = userNames,
            LegacyRules = legacy,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The caller's Read on every row the answer exposes (EB-D-218)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ONE batched rights check, as <see cref="AffectedInvoiceCounter"/> asks it: the caller first (an unestablished caller gives every
    /// entry "unknown"), then <see cref="CallerRecordAccessProbe.GetCallerRightsForRecordsOrUnknownAsync"/> in pages. True only when every
    /// row was answered and carries Read. "Dataverse said nothing" is a refusal here, never a quietly shorter answer.
    /// </summary>
    private Task<bool> CallerCanReadAsync(Reading reading, string? callerBearerToken, CancellationToken ct)
    {
        // The route's own record is always part of what is exposed, so the set is never empty and the caller is always established.
        reading.Expose(SetOf(reading.CoreTable), reading.RecordId);
        return CallerCanReadAllAsync(reading.Exposed, callerBearerToken, ct);
    }

    /// <summary>The batched check over <paramref name="exposed"/> (the dry run passes Now ∪ After: one check for both sides).</summary>
    private async Task<bool> CallerCanReadAllAsync(IReadOnlyCollection<(string Set, Guid Id)> exposed, string? callerBearerToken, CancellationToken ct)
    {
        var rights = await RightsOfAsync(exposed, callerBearerToken, ct).ConfigureAwait(false);
        if (rights is null)
        {
            return false;
        }

        if (rights.Any(r => (r.Value & AccessRights.Read) != AccessRights.Read))
        {
            // Which row it was is not logged with an id: the caller may not be allowed to know it exists (K2).
            _logger.LogInformation("Record procedures: the caller cannot Read a row the answer would expose; no list is returned");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Task 084d-5a (round 1, item 5; uac-r2 ruling 2026-11-13): of the candidate Procedures <paramref name="ids"/>, the ones the caller can Read,
    /// by the same batched check (<see cref="RightsOfAsync"/>). The assignable list drops the others BEFORE it judges anything, so an unreadable
    /// Procedure leaves no trace in the answer. Null when the check could not be answered as a whole (the caller not established, a row with no
    /// answer): the list is then 503, never "drop all". A fault propagates to the list's 503.
    /// </summary>
    private async Task<IReadOnlySet<Guid>?> ReadableProceduresAsync(IReadOnlyList<Guid> ids, string? callerBearerToken, CancellationToken ct)
    {
        var asked = ids.Select(id => (ProcedureEntity.EntitySet, id)).ToList();
        var rights = await RightsOfAsync(asked, callerBearerToken, ct).ConfigureAwait(false);
        if (rights is null)
        {
            return null;
        }

        return rights.Where(r => (r.Value & AccessRights.Read) == AccessRights.Read).Select(r => r.Row.Id).ToHashSet();
    }

    /// <summary>
    /// The one rights question behind both checks: the caller first (unestablished: null), then the rows in pages. One entry per row, with the
    /// caller's rights, or null when any row went unanswered ("Dataverse said nothing" is never read as "cannot Read" or as "can").
    /// </summary>
    private async Task<List<((string Set, Guid Id) Row, AccessRights Value)>?> RightsOfAsync(
        IReadOnlyCollection<(string Set, Guid Id)> rows, string? callerBearerToken, CancellationToken ct)
    {
        if (await _probe.GetCallerSystemUserIdAsync(callerBearerToken, ct).ConfigureAwait(false) is not { } caller || caller == Guid.Empty)
        {
            _logger.LogWarning("Record procedures: the caller could not be established; no list is returned");
            return null;
        }

        var answered = new List<((string Set, Guid Id) Row, AccessRights Value)>(rows.Count);
        foreach (var page in rows.OrderBy(t => t.Set, StringComparer.Ordinal).ThenBy(t => t.Id).Chunk(RightsPageSize))
        {
            var rights = await _probe.GetCallerRightsForRecordsOrUnknownAsync(callerBearerToken, page.ToList(), ct).ConfigureAwait(false);
            if (rights is null || rights.Count != page.Length || rights.Any(r => r is null))
            {
                _logger.LogWarning("Record procedures: the caller's rights on {Count} row(s) were not answered; no list is returned", page.Length);
                return null;
            }

            for (var i = 0; i < page.Length; i++)
            {
                answered.Add((page[i], rights[i]!.Value));
            }
        }

        return answered;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The Procedures list
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    private RecordProceduresView BuildProcedures(Reading r)
    {
        var onDate = r.OnDate;
        var applied = onDate.Procedures.ToDictionary(p => p.ProcedureId);
        var gaps = onDate.Gaps.ToDictionary(g => g.ProcedureId);
        var excluded = onDate.ExcludedProcedures.ToDictionary(e => e.ProcedureId);
        var candidates = (r.OnFutureDate ?? onDate).Procedures.Select(p => p.ProcedureId)
            .Concat((r.OnFutureDate ?? onDate).Gaps.Select(g => g.ProcedureId))
            .Concat(excluded.Keys)
            .Concat(applied.Keys).Concat(gaps.Keys)
            .Distinct().ToList();

        var items = new List<ProcedureListItem>();
        foreach (var id in candidates)
        {
            var procedure = r.Rows.Procedures.First(p => p.Id == id);
            var status = excluded.ContainsKey(id) ? "Excluded"
                : applied.ContainsKey(id) ? "InForce"
                : gaps.ContainsKey(id) ? "Gap"
                : "NotYetInForce";
            var decisive = applied.TryGetValue(id, out var a) ? a.Overrides
                : gaps.TryGetValue(id, out var g) ? g.Overrides
                : excluded.TryGetValue(id, out var e) ? e.Overrides
                : NearestRows(r.Rows.Overrides, id);
            items.Add(BuildProcedure(r, procedure, status, applied.GetValueOrDefault(id), decisive));
        }

        var ordered = items
            .OrderBy(i => StatusOrder(i.Status))
            .ThenByDescending(i => Enum.Parse<ProcedureLevel>(i.Level))
            .ThenBy(i => i.Name, StringComparer.Ordinal)
            .ThenBy(i => i.Id)
            .ToList();
        var gapViews = onDate.Gaps.Select(g => BuildGap(r, g)).ToList();
        return new RecordProceduresView(
            RecordView(r, r.RecordId), r.Date, ResolvedFirmOf(r), r.FirmSource, ordered, gapViews);
    }

    private static int StatusOrder(string status) => status switch { "InForce" => 0, "Gap" => 1, "NotYetInForce" => 2, _ => 3 };

    /// <summary>The Procedure-level rows at the nearest distance: the rows that decide, for a Procedure the resolver does not report (not yet in force).</summary>
    private static IReadOnlyList<ProcedureOverrideRow> NearestRows(IReadOnlyList<ProcedureOverrideRow> rows, Guid procedureId)
    {
        var own = rows.Where(o => o.ProcedureId == procedureId).ToList();
        return own.Count == 0 ? [] : own.Where(o => o.Distance == own.Min(x => x.Distance)).ToList();
    }

    private ProcedureListItem BuildProcedure(
        Reading r, ProcedureRow procedure, string status, AppliedProcedure? applied, IReadOnlyList<ProcedureOverrideRow> decisive)
    {
        r.Expose(ProcedureEntity.EntitySet, procedure.Id);
        ExposeVersionsOf(r, procedure.Id);
        var sources = SourcesOf(r, procedure);
        var level = sources.Count == 0 ? ProcedureLevel.AllRecords : LevelOf(sources[0]);

        ProcedureVersionView2? versionInForce = null;
        ProcedureVersionView2? newer = null;
        if (applied is not null)
        {
            var chosen = r.Rows.Versions.FirstOrDefault(v => v.Id == applied.ProcedureVersionId);
            versionInForce = VersionView(r, chosen);
            newer = applied.Pinned ? NewerThan(r, procedure.Id, applied.ProcedureVersionNumber) : null;
        }

        var decisiveIds = decisive.Select(d => d.RowId).ToHashSet();
        var overrides = r.Rows.Overrides.Where(o => o.ProcedureId == procedure.Id)
            .OrderBy(o => o.Distance).ThenBy(o => o.RowId)
            .Select(o => OverrideView(r, o, decisiveIds.Contains(o.RowId)))
            .ToList();
        return new ProcedureListItem(
            procedure.Id, procedure.Name, "Billing", TypeName(r, procedure.Id), procedure.AppliesTo.ToString(), FirmOf(r, procedure.FirmId, FirmOrigin.Lookup),
            status, level.ToString(), sources, versionInForce, applied?.Pinned ?? false, newer, overrides);
    }

    private static ProcedureLevel LevelOf(ProcedureSourceView source) => Enum.Parse<ProcedureLevel>(source.Kind);

    private static string? TypeName(Reading r, Guid procedureId) =>
        r.ProcedureTypes.GetValueOrDefault(procedureId) is { } value ? Enum.IsDefined((ProcedureType)value) ? ((ProcedureType)value).ToString() : value.ToString() : null;

    /// <summary>Every source of <paramref name="procedure"/> on the record, highest first, the highest marked winning (6.1, 6.5).</summary>
    private List<ProcedureSourceView> SourcesOf(Reading r, ProcedureRow procedure)
    {
        var found = new List<(ProcedureLevel Level, int Distance, ProcedureSourceView View)>();
        if (procedure.AppliesTo == ProcedureAppliesTo.AllRecords)
        {
            found.Add((ProcedureLevel.AllRecords, 0, new ProcedureSourceView(nameof(ProcedureLevel.AllRecords), false, 0, null, null, null)));
        }

        if (procedure.AppliesTo == ProcedureAppliesTo.Firm && procedure.FirmId == r.Rows.FirmId)
        {
            found.Add((ProcedureLevel.Firm, 0, new ProcedureSourceView(nameof(ProcedureLevel.Firm), false, 0, null, null, null)));
        }

        foreach (var row in r.Rows.Overrides.Where(o => o.ProcedureId == procedure.Id && o.Mode != ProcedureOverrideMode.Excluded))
        {
            var level = row.Distance == 0 ? ProcedureLevel.ManualOwn : ProcedureLevel.ManualParent;
            found.Add((level, row.Distance, new ProcedureSourceView(
                level.ToString(), false, row.Distance, row.Distance == 0 ? null : ChainRecordOf(r, row.ChainRecord), row.Mode.ToString(),
                r.Expose(ProcedureAssignmentEntity.EntitySet, row.RowId))));
        }

        var ordered = found.OrderByDescending(x => x.Level).ThenBy(x => x.Distance).ThenBy(x => x.View.RowId).ToList();
        if (ordered.Count == 0)
        {
            return [];
        }

        var top = ordered[0];
        return ordered.Select(x => x.View with { Winning = x.Level == top.Level && x.Distance == top.Distance }).ToList();
    }

    private ProcedureOverrideView OverrideView(Reading r, ProcedureOverrideRow row, bool decisive)
    {
        ProcedureVersionView2? pinned = null;
        ProcedureVersionView2? newer = null;
        if (row.Mode == ProcedureOverrideMode.Pinned && r.Rows.Versions.FirstOrDefault(v => v.Id == row.PinnedVersionId) is { } pin
            && pin.ProcedureId == row.ProcedureId)
        {
            pinned = VersionView(r, pin);
            newer = NewerThan(r, pin.ProcedureId, pin.Number);
        }

        r.Expose(ProcedureAssignmentEntity.EntitySet, row.RowId);
        return new ProcedureOverrideView(
            row.RowId, row.Mode.ToString(), decisive, row.Distance, ChainRecordOf(r, row.ChainRecord), row.Reason, pinned, newer, StampOf(r, row));
    }

    /// <summary>
    /// Exposes EVERY version of a Procedure the answer shows (fix round 3, F-B). What the answer says about a Procedure derives from its whole
    /// version set, not from the version it picked: <c>NotYetInForce</c>, the gap kinds <c>HoleBetweenVersions</c> and <c>AfterLastVersion</c>,
    /// an absent <c>NewerVersion</c> and the version chosen for a date are each decided by versions that are not shown.
    /// </summary>
    private static void ExposeVersionsOf(Reading r, Guid procedureId)
    {
        foreach (var version in r.Rows.Versions.Where(v => v.ProcedureId == procedureId))
        {
            r.Expose(ProcedureEntity.VersionEntitySet, version.Id);
        }
    }

    /// <summary>The newest version in force (Active or Superseded, with a window) that is newer than version <paramref name="number"/>, or null: what a pin skips.</summary>
    private ProcedureVersionView2? NewerThan(Reading r, Guid procedureId, int number) =>
        VersionView(r, r.Rows.Versions
            .Where(v => v.ProcedureId == procedureId && v.IsInForce && v.Number > number)
            .OrderByDescending(v => v.Number).FirstOrDefault());

    private ProcedureVersionView2? VersionView(Reading r, ProcedureVersionRow? version)
    {
        if (version is null)
        {
            return null;
        }

        r.Expose(ProcedureEntity.VersionEntitySet, version.Id);
        return new ProcedureVersionView2(version.Id, version.Number, VersionStatusName(version.Status), version.From, version.To);
    }

    private static string VersionStatusName(int status) => status switch
    {
        ProcedureEntity.VersionStatusDraft => "Draft",
        ProcedureEntity.VersionStatusActive => "Active",
        ProcedureEntity.VersionStatusSuperseded => "Superseded",
        ProcedureEntity.VersionStatusRetired => "Retired",
        _ => status.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private ProcedureGapView BuildGap(Reading r, ProcedureGap gap)
    {
        r.Expose(ProcedureEntity.EntitySet, gap.ProcedureId);
        ExposeVersionsOf(r, gap.ProcedureId);
        var decisiveIds = gap.Overrides.Select(o => o.RowId).ToHashSet();
        return new ProcedureGapView(
            gap.ProcedureId, gap.ProcedureName, gap.Kind.ToString(), gap.WorkDate, GapText(gap),
            gap.Overrides.OrderBy(o => o.Distance).ThenBy(o => o.RowId).Select(o => OverrideView(r, o, decisiveIds.Contains(o.RowId))).ToList());
    }

    private static string GapText(ProcedureGap gap) => gap.Kind switch
    {
        ProcedureGapKind.HoleBetweenVersions => $"No version of this Procedure is in force on {gap.WorkDate:yyyy-MM-dd}: the date falls between two versions.",
        ProcedureGapKind.AfterLastVersion => $"No version of this Procedure is in force on {gap.WorkDate:yyyy-MM-dd}: its last version has ended.",
        ProcedureGapKind.PinnedVersionRetired => "The pinned version of this Procedure is retired.",
        ProcedureGapKind.PinnedVersionNotInForce => "The pinned version of this Procedure is not in force.",
        ProcedureGapKind.ConflictingParents => "The records this one is filed under choose different versions of this Procedure.",
        _ => "No version of this Procedure can be chosen.",
    };

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The rules in force
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    private async Task<RecordRulesInForceView> BuildRulesAsync(Reading r, CancellationToken ct)
    {
        var onDate = r.OnDate;
        var views = new List<RuleInForceView>();
        var outranked = OutrankedMarks(r);

        // The rules that run, one for one, in the resolver's order.
        foreach (var rule in onDate.Rules)
        {
            views.Add(await InForceRuleAsync(r, rule, outranked.GetValueOrDefault(rule.Version.PolicyVersionId), ct).ConfigureAwait(false));
        }

        // The rules a plain Exclude removed (a changed rule's base is shown by its record rule's "changed" mark).
        foreach (var excluded in onDate.ExcludedRules.Where(e => !e.ReplacedByRecordRule))
        {
            views.Add(await ExcludedRuleAsync(r, excluded, ct).ConfigureAwait(false));
        }

        // Record rules whose base rule is not in force on the date: the resolver gives them no slot, so they do not run.
        views.AddRange(await BaseRetiredRulesAsync(r, ct).ConfigureAwait(false));

        // What is absent is derived from rows too: a Procedure a Procedure-level Exclude removed lists none of its rules, and a base rule a record
        // rule replaced is listed only through the record rule's mark. The rows that decided that are part of what the caller must be able to Read.
        foreach (var excludedProcedure in onDate.ExcludedProcedures)
        {
            ExposeOverrides(r, excludedProcedure.Overrides);
        }

        foreach (var replaced in onDate.ExcludedRules.Where(e => e.ReplacedByRecordRule))
        {
            ExposeOverrides(r, replaced.Overrides);
        }

        return new RecordRulesInForceView(
            RecordView(r, r.RecordId), r.Date, ResolvedFirmOf(r), r.FirmSource, views, onDate.Gaps.Select(g => BuildGap(r, g)).ToList());
    }

    /// <summary>Exposes override rows that decided something the answer shows (or omits), and the chain record each sits on.</summary>
    private void ExposeOverrides(Reading r, IEnumerable<ProcedureOverrideRow> rows)
    {
        foreach (var row in rows)
        {
            r.Expose(ProcedureAssignmentEntity.EntitySet, row.RowId);
            ChainRecordOf(r, row.ChainRecord);
        }
    }

    /// <summary>
    /// 084e (EB-D-219; ontology-r1 R1-R6, O-6): the <c>Outranked</c> / <c>CapOutranked</c> mark of each in-force rule, by policy version id. The group
    /// is exactly what the evaluator groups for a LINE on the date (R6): the legacy rules, one version per policy picked by the evaluator's own
    /// <see cref="InvoiceComplianceEvaluator.SelectVersion"/>, then the resolver's <c>Rules</c> (not <c>RulesWithFallback</c>, the invoice
    /// subject's fallback), system rules excluded, each through the billing gate, grouped by <see cref="BillingRuleDefinition.PrecedenceKey"/>.
    /// Precedence is <see cref="RulePrecedence.Resolve{T}"/>, the evaluator's own function (R5: no rank loop here), with the record-level
    /// predicate (R1): gate-accepted (a refused version is in no group) and not downgraded. What a record read cannot know (the handler's
    /// answer on an invoice's facts) is said by the note (R4). A mark only on an outcome other than Applies; "by" is every displacing version at
    /// the top rank (R3). EB-D-218: every version at or above the top rank of a marked key (the rows that decided the mark and its "by" list),
    /// with its policy and rule set, a legacy one with every version of its policy, a Procedure one with its Procedure and that Procedure's
    /// versions, is part of what the caller must be able to Read.
    /// </summary>
    private Dictionary<Guid, RuleMarkView> OutrankedMarks(Reading r)
    {
        var legacy = r.LegacyRules
            .Where(c => c.CanBeInForce && !InvoiceComplianceEvaluator.IsSystemRule(c))
            .OrderBy(c => c.PolicyCode, StringComparer.Ordinal).ThenBy(c => c.PolicyId).ThenBy(c => c.VersionNumber).ThenBy(c => c.PolicyVersionId)
            .GroupBy(c => c.PolicyId)
            .Select(g => InvoiceComplianceEvaluator.SelectVersion(g.ToList(), r.Date))
            .OfType<BillingRuleCandidate>();
        var procedureRules = new Dictionary<Guid, ResolvedRule>();
        var candidates = new List<BillingRuleCandidate>(legacy);
        foreach (var rule in r.OnDate.Rules)
        {
            var candidate = BillingFactLoader.ProcedureCandidate(rule);
            if (!InvoiceComplianceEvaluator.IsSystemRule(candidate) && procedureRules.TryAdd(candidate.PolicyVersionId, rule))
            {
                candidates.Add(candidate);
            }
        }

        // The gate, as the evaluator's step 2 asks it: a consequence the type may not carry is not shown to the gate (the version is downgraded, not refused).
        var accepted = new Dictionary<Guid, BillingRuleDefinition>();
        var downgraded = new HashSet<Guid>();
        foreach (var candidate in candidates)
        {
            var disallowed = InvoiceComplianceEvaluator.DisallowedConsequenceType(candidate);
            var snapshot = new PolicyVersionSnapshot(
                candidate.PolicyVersionId, candidate.PolicyCode, candidate.RuleType, candidate.RuleBody, candidate.MessageTemplate,
                disallowed is null ? candidate.Consequence : null);
            // The read-side entry (084e fix round 1, ontology-r1): the same decision as the evaluator's gate, without its error log and meter.
            if (_gate.TryPrepareBillingRuleForRead(snapshot, out var definition))
            {
                accepted[candidate.PolicyVersionId] = definition;
                if (disallowed is not null)
                {
                    downgraded.Add(candidate.PolicyVersionId);
                }
            }
        }

        var marks = new Dictionary<Guid, RuleMarkView>();
        foreach (var key in candidates.Where(c => accepted.ContainsKey(c.PolicyVersionId)).GroupBy(c => accepted[c.PolicyVersionId].PrecedenceKey))
        {
            var resolved = RulePrecedence.Resolve(
                BillingRuleCatalogue.PrecedenceOf(key.Key.RuleType), key.ToList(), c => c.Rank,
                c => accepted[c.PolicyVersionId].MaxAmountPerItem is not null, c => !downgraded.Contains(c.PolicyVersionId));
            var marked = resolved.Outcomes.Where(o => o.Outcome != PrecedenceOutcome.Applies && procedureRules.ContainsKey(o.Version.PolicyVersionId)).ToList();
            if (marked.Count == 0 || resolved.Top is not { } top)
            {
                continue;
            }

            foreach (var decided in key.Where(c => c.Rank.CompareTo(top) >= 0))
            {
                ExposeCandidate(r, decided);
            }

            var by = resolved.Displacing.Select(d => OutrankingView(d, procedureRules.GetValueOrDefault(d.PolicyVersionId))).ToList();
            foreach (var (version, outcome) in marked)
            {
                marks[version.PolicyVersionId] = OutrankedMark(outcome, by);
            }
        }

        return marks;
    }

    /// <summary>
    /// 084e fix round 1: the mark of one outranked version. <see cref="RuleMarkView.Text"/> is "Outranked by X" / "Cap outranked by X"; the note is
    /// ontology-r1's wording for the kind, singular for one displacing version at the top rank, plural for more (<paramref name="by"/> in order).
    /// </summary>
    internal static RuleMarkView OutrankedMark(PrecedenceOutcome outcome, IReadOnlyList<OutrankingRuleView> by)
    {
        var codes = JoinCodes(by.Select(b => b.Code).ToList());
        var plural = by.Count > 1;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return outcome == PrecedenceOutcome.DisplacedOutright
            ? new RuleMarkView("Outranked", Text: "Outranked by " + codes,
                Note: string.Format(inv, plural ? OutrankedNotePlural : OutrankedNoteSingular, codes), By: by)
            : new RuleMarkView("CapOutranked", Text: "Cap outranked by " + codes,
                Note: string.Format(inv, plural ? CapOutrankedNotePlural : CapOutrankedNoteSingular, codes), By: by);
    }

    /// <summary>The join rule of the outranking codes: "X"; "X and Y"; "X, Y and Z" for three or more (no serial comma).</summary>
    internal static string JoinCodes(IReadOnlyList<string> codes) =>
        codes.Count <= 1 ? string.Concat(codes) : string.Join(", ", codes.Take(codes.Count - 1)) + " and " + codes[^1];

    /// <summary>A rule that decided an outranked mark: its policy, version and rule set; a legacy rule's every version (they decide which one is
    /// in force on the date, as F-B for a Procedure's); a Procedure rule's Procedure and all its versions.</summary>
    private static void ExposeCandidate(Reading r, BillingRuleCandidate candidate)
    {
        r.Expose(PolicySet, candidate.PolicyId);
        r.Expose(PolicyVersionSet, candidate.PolicyVersionId);
        r.Expose(RuleSetSet, candidate.RuleSetId);
        if (candidate.Source == BillingRuleSource.Legacy)
        {
            foreach (var version in r.LegacyRules.Where(v => v.PolicyId == candidate.PolicyId))
            {
                r.Expose(PolicyVersionSet, version.PolicyVersionId);
            }
        }
        else if (candidate.ProcedureId is { } procedure)
        {
            r.Expose(ProcedureEntity.EntitySet, procedure);
            ExposeVersionsOf(r, procedure);
        }
    }

    private OutrankingRuleView OutrankingView(BillingRuleCandidate candidate, ResolvedRule? rule) => new(
        candidate.PolicyId, candidate.PolicyVersionId,
        rule is not null ? ProcedureRulesOnDate.ReaderCodeOf(rule, _logger) : ProcedureRulesOnDate.ReaderCodeOf(candidate.PolicyCode, null, _logger),
        candidate.ShortHeadline, candidate.VersionNumber, candidate.Source.ToString(), candidate.ProcedureId, candidate.Rank.Level.ToString(),
        candidate.Rank.Distance);

    private async Task<RuleInForceView> InForceRuleAsync(Reading r, ResolvedRule rule, RuleMarkView? outranked, CancellationToken ct)
    {
        var marks = new List<RuleMarkView>();
        if (rule.RecordRule is { } slot)
        {
            var basedOn = PolicyVersionRef(r, rule.Version.BasedOnVersionId);
            var current = PolicyVersionRef(r, slot.BasePolicyVersionId);
            var baseVersion = r.Rows.PolicyVersions.FirstOrDefault(v => v.PolicyVersionId == slot.BasePolicyVersionId);
            var exclude = slot.Exclude;
            // 084c fix round 1: the base rule's id is in the answer (F1-1), and so is its body in force (F1-2): both through Expose (EB-D-218).
            marks.Add(new RuleMarkView(
                "Changed", exclude?.Reason, FirmOf(r, exclude?.FirmId, FirmOrigin.Lookup), exclude?.Distance ?? slot.ChainRecord.Distance, ChainRecordOf(r, slot.ChainRecord),
                exclude is null ? null : StampOf(r, exclude), exclude?.RowId, rule.Version.PolicyId, basedOn, current, slot.BasePolicyCode,
                baseVersion is null ? null : await SummaryAsync(r, baseVersion, ct).ConfigureAwait(false),
                BasePolicyId: r.Expose(PolicySet, slot.BasePolicyId), BaseRuleBody: baseVersion is null ? null : BodyOf(r, baseVersion)));
            if (exclude is not null)
            {
                r.Expose(ProcedureAssignmentEntity.EntitySet, exclude.RowId);
            }

            if (basedOn is null || current is null || basedOn.Id != current.Id)
            {
                marks.Add(new RuleMarkView("Stale", RecordRuleId: rule.Version.PolicyId, BasedOn: basedOn, CurrentBase: current));
            }
        }

        if (outranked is not null)
        {
            marks.Add(outranked);
        }

        return await RuleViewAsync(
            r, rule.Version, rule.Procedure, ProcedureRulesOnDate.ReaderCodeOf(rule, _logger), inForce: true, rule.RecordRule is not null, marks, ct).ConfigureAwait(false);
    }

    private async Task<RuleInForceView> ExcludedRuleAsync(Reading r, ExcludedRule excluded, CancellationToken ct)
    {
        var marks = excluded.Overrides.OrderBy(o => o.Distance).ThenBy(o => o.RowId).Select(o =>
        {
            r.Expose(ProcedureAssignmentEntity.EntitySet, o.RowId);
            return new RuleMarkView(
                "Excluded", o.Reason, FirmOf(r, o.FirmId, FirmOrigin.Lookup), o.Distance, ChainRecordOf(r, o.ChainRecord), StampOf(r, o), o.RowId);
        }).ToList();
        return await RuleViewAsync(
            r, excluded.Version, excluded.Procedure, excluded.Version.PolicyCode, inForce: false, recordRule: false, marks, ct).ConfigureAwait(false);
    }

    private async Task<RuleInForceView> RuleViewAsync(
        Reading r, ResolvedPolicyVersion version, AppliedProcedure applied, string code, bool inForce, bool recordRule,
        IReadOnlyList<RuleMarkView> marks, CancellationToken ct)
    {
        var set = r.Rows.Sets.FirstOrDefault(s => s.Id == version.RuleSetId);
        var procedure = r.Rows.Procedures.FirstOrDefault(p => p.Id == applied.ProcedureId);
        var firm = set?.Record is not null ? set.FirmId : procedure?.FirmId;
        r.Expose(PolicySet, version.PolicyId);
        r.Expose(PolicyVersionSet, version.PolicyVersionId);
        r.Expose(RuleSetSet, version.RuleSetId);
        r.Expose(ProcedureEntity.EntitySet, applied.ProcedureId);
        ExposeVersionsOf(r, applied.ProcedureId);
        // Pinned, Level and Distance below derive from the rows that decided the Procedure (EB-D-218: the caller must be able to Read them).
        ExposeOverrides(r, applied.Overrides);
        var procedureVersion = r.Rows.Versions.FirstOrDefault(v => v.Id == applied.ProcedureVersionId);
        return new RuleInForceView(
            version.PolicyId, version.PolicyVersionId,
            code, version.ShortHeadline, RuleTypeName(version.RuleType), await SummaryAsync(r, version, ct).ConfigureAwait(false), version.VersionNumber,
            version.InForceFrom, inForce, recordRule, version.RuleSetId,
            new RuleProcedureView(applied.ProcedureId, applied.ProcedureName, VersionView(r, procedureVersion), applied.Pinned),
            applied.Rank.Level.ToString(), applied.Rank.Distance, FirmOf(r, firm, FirmOrigin.Lookup), marks,
            ConsequenceName(version.Consequence), BodyOf(r, version));
    }

    /// <summary>
    /// 084c fix round 1 (F1-2): a version's rule body as a JSON object, for the Change form to start from (every published value, optional
    /// ones included). The body is read from the version row, so the row is part of what the caller must be able to Read (EB-D-218). Null
    /// when the stored text is empty or is not a JSON object: the form then says it cannot start, never sends a body built without them.
    /// </summary>
    private static JsonElement? BodyOf(Reading r, ResolvedPolicyVersion version)
    {
        r.Expose(PolicyVersionSet, version.PolicyVersionId);
        if (string.IsNullOrWhiteSpace(version.RuleBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(version.RuleBody);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>084c fix round 1 (item 6): a version's consequence by name (<see cref="BillingConsequence"/>), the number for a value it does not define, null when unset.</summary>
    private static string? ConsequenceName(int? value) =>
        value is not { } number ? null
        : Enum.IsDefined(typeof(BillingConsequence), number) ? ((BillingConsequence)number).ToString()
        : number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Record rules on the chain whose base rule is NOT among the rules the resolver resolved for the date (its Procedure retired, the rule
    /// dropped by the version in force, or the Procedure not applicable): the resolver gives such a record rule no slot, so it does not run
    /// (089c K1 / K3, ontology-r1). Found from the rows already read: a rule-level Exclude row whose derived record rule exists in a
    /// record-scoped set on the chain. The base rule's code and the version the record rule was based on need two small reads of ids those
    /// rows name.
    /// </summary>
    private async Task<List<RuleInForceView>> BaseRetiredRulesAsync(Reading r, CancellationToken ct)
    {
        var resolvedBases = r.OnDate.Rules.Select(x => x.RecordRule?.BasePolicyId ?? x.Version.PolicyId)
            .Concat(r.OnDate.ExcludedRules.Select(x => x.Version.PolicyId)).ToHashSet();
        var orphans = new List<(ProcedureOverrideRow Exclude, Guid RecordRuleId, Guid SetId, PolicyRow Rule)>();
        foreach (var row in r.Rows.Overrides.Where(o => o.PolicyId is { } id && !resolvedBases.Contains(id) && ProcedureResolver.IsRuleOverrideFor(o, id, r.Rows.FirmId)))
        {
            var setId = ProcedureAssignmentService.RecordRuleSetIdFor(row.ChainRecord.Table, row.ChainRecord.Id, row.FirmId);
            var recordRuleId = ProcedureAssignmentService.RecordRulePolicyIdFor(setId, row.PolicyId!.Value);
            if (r.Rows.Policies.FirstOrDefault(p => p.Id == recordRuleId && p.RuleSetId == setId) is { } rule
                && r.Rows.PolicyVersions.Any(v => v.PolicyId == recordRuleId))
            {
                orphans.Add((row, recordRuleId, setId, rule));
            }
        }

        if (orphans.Count == 0)
        {
            return [];
        }

        var baseCodes = (await ReadByIdsAsync(BillingFactLoader.PolicyEntity, orphans.Select(o => o.Exclude.PolicyId!.Value), ["sprk_policycode"], ct).ConfigureAwait(false))
            .ToDictionary(x => x.Id, x => x.GetAttributeValue<string>("sprk_policycode"));
        var latest = orphans.ToDictionary(o => o.RecordRuleId, o => CurrentEpisodeVersion(r.Rows.PolicyVersions, r.Date, o.RecordRuleId, o.Exclude));
        var basedOnIds = latest.Values.Where(v => v.BasedOnVersionId is not null).Select(v => v.BasedOnVersionId!.Value);
        var basedOnNumbers = (await ReadByIdsAsync(BillingFactLoader.VersionEntity, basedOnIds, ["sprk_versionnumber"], ct).ConfigureAwait(false))
            .ToDictionary(x => x.Id, x => x.GetAttributeValue<int?>("sprk_versionnumber") ?? 0);

        var views = new List<RuleInForceView>();
        foreach (var (exclude, recordRuleId, setId, rule) in orphans.OrderBy(o => o.RecordRuleId))
        {
            var basePolicy = exclude.PolicyId!.Value;
            var version = latest[recordRuleId];
            // basedOn (shown in both marks) is read from this version row, so the row itself is part of what the caller must be able to Read (fix round 3, F-A).
            r.Expose(PolicyVersionSet, version.PolicyVersionId);
            PolicyVersionRefView? basedOn = version.BasedOnVersionId is { } b && basedOnNumbers.TryGetValue(b, out var number)
                ? new PolicyVersionRefView(r.Expose(PolicyVersionSet, b), number) : null;
            // The base rule's own catalogue code (fail closed to blank if it is missing or itself a record-rule code), as a reader sees a record rule.
            var code = ProcedureRulesOnDate.ReaderCodeOf(ProcedureAssignmentService.RecordRuleCodePrefix, baseCodes.GetValueOrDefault(basePolicy), _logger);
            r.Expose(PolicySet, basePolicy);
            r.Expose(PolicySet, recordRuleId);
            r.Expose(RuleSetSet, setId);
            r.Expose(ProcedureAssignmentEntity.EntitySet, exclude.RowId);
            var stamp = StampOf(r, exclude);
            var chainRecord = ChainRecordOf(r, exclude.ChainRecord);
            var firmView = FirmOf(r, exclude.FirmId, FirmOrigin.Lookup);
            views.Add(new RuleInForceView(
                recordRuleId, null, code, null, null, null, null, null, InForce: false, RecordRule: true, setId, null, null, null, firmView,
                [
                    // 084c fix round 1 (F1-1): the base rule's id (asked above: the code is read from it). Its body is not given: it is not in force.
                    new RuleMarkView("Changed", exclude.Reason, firmView, exclude.Distance, chainRecord, stamp, exclude.RowId, recordRuleId, basedOn, null, code,
                        BasePolicyId: basePolicy),
                    new RuleMarkView("BaseRetired", RecordRuleId: recordRuleId, BasedOn: basedOn, BaseCode: code),
                ]));
        }

        return views;
    }

    /// <summary>
    /// The version of a record rule the resolver would count, as <c>ProcedureResolution.RecordRuleVersionOn</c> counts it (fix round 3, K2): only the
    /// versions of the CURRENT episode, by the resolver's own filter (<see cref="ProcedureResolution.CurrentEpisodeVersions"/>, 084e: one
    /// implementation), then the latest in force on the date (effective-from, then number, then id).
    /// A record rule keeps its policy across a removal and a new change, so an older episode's version must never be shown. When the change is
    /// not yet in force on the date (no version of the episode is), its first version names what it was based on. A known boundary that no
    /// version reaches is the resolver's own fault state and is a fault here too (503, never a guess).
    /// </summary>
    internal static ResolvedPolicyVersion CurrentEpisodeVersion(IReadOnlyList<ResolvedPolicyVersion> versions, DateOnly date, Guid recordRuleId, ProcedureOverrideRow exclude)
    {
        var all = versions.Where(v => v.PolicyId == recordRuleId).ToList();
        var episode = ProcedureResolution.CurrentEpisodeVersions(recordRuleId, all, exclude);
        if (episode.Count == 0)
        {
            throw new ProcedureResolutionFaultException(
                $"Record rule {recordRuleId} has {all.Count} Published version(s) but none was created on or after its change's Exclude row {exclude.RowId}.");
        }

        return episode.Where(v => ProcedureResolution.InForceOn(v.InForceFrom, date))
                   .OrderByDescending(v => v.InForceFrom).ThenByDescending(v => v.VersionNumber).ThenByDescending(v => v.PolicyVersionId)
                   .FirstOrDefault()
               ?? episode.OrderBy(v => v.InForceFrom).ThenBy(v => v.VersionNumber).ThenBy(v => v.PolicyVersionId).First();
    }

    private PolicyVersionRefView? PolicyVersionRef(Reading r, Guid? versionId)
    {
        if (versionId is not { } id || r.Rows.PolicyVersions.FirstOrDefault(v => v.PolicyVersionId == id) is not { } version)
        {
            return null;
        }

        r.Expose(PolicyVersionSet, id);
        return new PolicyVersionRefView(id, version.VersionNumber);
    }

    private async Task<string?> SummaryAsync(Reading r, ResolvedPolicyVersion version, CancellationToken ct)
    {
        r.Expose(PolicyVersionSet, version.PolicyVersionId);
        var result = await _describer.DescribeAsync(version.RuleBody, ct).ConfigureAwait(false);
        return result.Description is { } description ? string.Join(" ", description.Clauses.Select(c => c.Text)) : null;
    }

    private static string? RuleTypeName(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number)
        && Enum.IsDefined((RuleType)number) ? ((RuleType)number).ToString() : value;

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Views of shared parts (each records what it exposes)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static ChainRecordView RecordView(Reading r, Guid id) =>
        new(r.CoreTable, id, r.RecordNames.GetValueOrDefault(id), 0);

    private ChainRecordView ChainRecordOf(Reading r, CoreRecordLink link)
    {
        r.Expose(SetOf(link.Table), link.Id);
        return new ChainRecordView(link.Table, link.Id, r.RecordNames.GetValueOrDefault(link.Id), link.Distance);
    }

    /// <summary>Where a firm id in the answer came from (EB-D-218, fix round 3 F-D): this decides whether the caller's Read on the firm is asked.</summary>
    private enum FirmOrigin
    {
        /// <summary>A lookup value on a row that is already in the checked set (the record's billing firm, <c>sprk_firm</c> on a Procedure, an override row or a rule set): its id and name come with that row's own read, so the organization is not asked.</summary>
        Lookup,

        /// <summary>The caller's <c>?firmId=</c>: nothing else vouches for it, so asking about the organization is the only thing that stops the parameter being a firm-existence and firm-name oracle.</summary>
        Query,
    }

    /// <summary>The route's own firm: asked when the caller named it (<see cref="FirmOrigin.Query"/>), not when it is the record's billing firm.</summary>
    private FirmView? ResolvedFirmOf(Reading r) =>
        FirmOf(r, r.FirmId, r.FirmSource == QueryFirmSource ? FirmOrigin.Query : FirmOrigin.Lookup);

    private FirmView? FirmOf(Reading r, Guid? id, FirmOrigin origin)
    {
        if (id is not { } firm || firm == Guid.Empty)
        {
            return null;
        }

        if (origin == FirmOrigin.Query)
        {
            r.Expose(OrganizationSet, firm);
        }

        return new FirmView(firm, r.FirmNames.GetValueOrDefault(firm));
    }

    private StampView StampOf(Reading r, ProcedureOverrideRow row)
    {
        var by = r.RowCreators.GetValueOrDefault(row.RowId);
        if (by is { } person)
        {
            r.Expose(SystemUserSet, person);
        }

        return new StampView(by, by is { } id ? r.UserNames.GetValueOrDefault(id) : null, row.CreatedOn);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Reads and table names
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The rows of <paramref name="entity"/> with these ids, app-only, ids chunked and every query paged. A fault throws (the answer is then unavailable).</summary>
    private async Task<List<Entity>> ReadByIdsAsync(string entity, IEnumerable<Guid> ids, string[] columns, CancellationToken ct)
    {
        var rows = new List<Entity>();
        foreach (var chunk in ids.Where(id => id != Guid.Empty).Distinct().Order().Chunk(MaxInValues))
        {
            var query = new QueryExpression(entity)
            {
                ColumnSet = new ColumnSet(columns),
                NoLock = true,
                PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 },
            };
            query.Criteria.AddCondition(entity + "id", ConditionOperator.In, chunk.Cast<object>().ToArray());
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var page = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
                rows.AddRange(page.Entities.Where(e => chunk.Contains(e.Id)));
                if (!page.MoreRecords)
                {
                    break;
                }

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }

        return rows;
    }

    private static CoreRecordKind KindOf(string coreTable) => coreTable switch
    {
        SecureRootInheritance.Matter => CoreRecordKind.Matter,
        SecureRootInheritance.Project => CoreRecordKind.Project,
        SecureRootInheritance.WorkAssignment => CoreRecordKind.WorkAssignment,
        _ => throw new ArgumentOutOfRangeException(nameof(coreTable), coreTable, "Not a core record table."),
    };

    private static string SetOf(string table) => table switch
    {
        SecureRootInheritance.Matter => FinanceAuthorizationFilter.MatterEntitySet,
        SecureRootInheritance.Project => FinanceAuthorizationFilter.ProjectEntitySet,
        SecureRootInheritance.WorkAssignment => FinanceAuthorizationFilter.WorkAssignmentEntitySet,
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Not a core record table."),
    };

    // The name column of each core table (the same three SecureRecordRoot uses): a matter has no sprk_name.
    private static string NameColumnOf(string table) => table switch
    {
        SecureRootInheritance.Matter => "sprk_mattername",
        SecureRootInheritance.Project => "sprk_projectname",
        _ => "sprk_name",
    };

    private static string? NameOf(Entity row, string table) => row.GetAttributeValue<string>(NameColumnOf(table));
}
