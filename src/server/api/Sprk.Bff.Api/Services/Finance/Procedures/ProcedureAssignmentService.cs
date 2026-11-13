using System.Data;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Procedures;

/// <summary>
/// <c>sprk_procedureassignment</c>'s names and option values (ebilling-r1 task 080: user-owned, entity set <c>sprk_procedureassignments</c>,
/// <c>sprk_name</c> required; <c>sprk_mode</c> a local choice; <c>notes/080-procedure-schema.md</c> 2.2 and 2.3).
/// </summary>
public static class ProcedureAssignmentEntity
{
    public const string Entity = "sprk_procedureassignment";
    public const string EntitySet = "sprk_procedureassignments";
    public const string NameColumn = "sprk_name";
    public const string ModeColumn = "sprk_mode";
    public const string ReasonColumn = "sprk_reason";
    public const string ProcedureColumn = "sprk_procedure";
    public const string PinnedVersionColumn = "sprk_pinnedversion";
    public const string ExcludedPolicyColumn = "sprk_excludedpolicy";
    public const string FirmColumn = "sprk_firm";

    public const int ModeFollowLatest = 100000000;
    public const int ModePinned = 100000001;
    public const int ModeExcluded = 100000002;
}

/// <summary>Assign a Procedure to a record: <c>FollowLatest</c> (default), <c>Pinned</c> (with a version of that Procedure) or <c>Excluded</c>.</summary>
public sealed record ProcedureAssignmentRequest(Guid? ProcedureId, string? Mode, Guid? PinnedVersionId, string? Reason);

/// <summary>Exclude one rule for a record (for one firm's invoices when <see cref="FirmId"/> is set). The reason is required.</summary>
public sealed record RuleExcludeRequest(Guid? PolicyId, string? Reason, Guid? FirmId);

/// <summary>One override row as written: a Procedure-level row (<c>Procedure</c>) or a rule-level Exclude (<c>RuleExcluded</c>).</summary>
public sealed record OverrideView(
    Guid Id, string Kind, string Mode, Guid? ProcedureId, Guid? PinnedVersionId, Guid? PolicyId, Guid? FirmId, string? Reason);

/// <summary>
/// Change one rule for a record (task 089c, model 6.3): the rule is excluded for the record and a record rule takes its place, based on the
/// rule's version in force with these members changed (at least one). The reason is required. <see cref="FirmId"/> limits the change to one
/// firm's invoices, and must be the rule's own firm when the rule applies to one firm only.
/// </summary>
public sealed record RuleChangeRequest(
    Guid? PolicyId, string? Reason, Guid? FirmId, string? RuleBody, string? MessageTemplate, string? ShortHeadline, string? Consequence);

/// <summary>The record rule a change wrote: its record-scoped set, its policy, and the Published version based on the rule's version in force.</summary>
public sealed record RecordRuleView(
    Guid RuleSetId, Guid PolicyId, string Code, Guid VersionId, int VersionNumber, Guid BasedOnVersionId, string Scope, Guid? FirmId);

/// <summary>
/// A changed rule as written: the Exclude row (kind <c>RuleChanged</c>) and the record rule that replaces the rule for the record. Task 089e:
/// the base rule's open findings on the In Review invoices filed on the record, and (082c C19) on the records DIRECTLY under it where this
/// change decides the rule, then close <c>Superseded</c>. <see cref="CloseComplete"/> says whether that close finished, and
/// <see cref="VisibleOpenInvoices"/> / <see cref="AffectedText"/> how many of those invoices THE CALLER can Read (null when that cannot be
/// told). <see cref="Message"/> always says what the close covered: <see cref="ProcedureAssignmentService.ChangeCloseCompleteMessage"/> or
/// <see cref="ProcedureAssignmentService.ChangeCloseIncompleteMessage"/>.
/// </summary>
public sealed record RuleChangedView(
    OverrideView Override, RecordRuleView RecordRule, bool CloseComplete = true, string? AffectedText = null, int? VisibleOpenInvoices = null,
    string? Message = null);

/// <summary>
/// The read-only confirmation before an override is removed (task 089c, model 15 answer 2). Only the removal of a changed rule closes
/// anything. For that removal <see cref="Warning"/> is set, and <see cref="AffectedText"/> says how many open invoices THE CALLER can Read
/// whose open findings of the record rule would close (null when that cannot be told). A plain removal has no warning and no count.
/// </summary>
public sealed record OverrideRemovalPreview(
    Guid OverrideId, string Kind, bool ClosesRule, string? Warning, string? AffectedText, int? VisibleOpenInvoices);

/// <summary>What a removal did. The row is deleted. For a changed rule, the record rule is also Off and its open findings are closed <c>PolicyRetired</c>.</summary>
public sealed record OverrideRemoved(
    Guid OverrideId, string Kind, bool RuleSwitchedOff, bool CloseComplete, string? AffectedText, int? VisibleOpenInvoices, string? Message);

/// <summary>What a stored override row overrides (the Procedure, or the rule it excludes or changes), as the entity set the caller's Read is asked on.</summary>
public sealed record OverrideSubject(string EntitySet, Guid Id);

/// <summary>
/// ebilling-r1 task 084d-1: what one proposed override write would do to the four override tables, planned without writing
/// (<see cref="ProcedureAssignmentService"/>'s <c>Plan*Async</c>). <see cref="AfterImages"/> holds every row the write would create (owner
/// columns not yet applied: the dry run asks no owner) and every real row it would update, read whole with the update applied.
/// <see cref="Created"/> names the created ones (a subset of the images), and <see cref="Deleted"/> the rows it would delete.
/// <see cref="Write"/> is the write planned (<see cref="WriteAssign"/>, <see cref="WriteExcludeRule"/>, <see cref="WriteChangeRule"/> or
/// <see cref="WriteRemove"/>) and <see cref="Kind"/> the kind of override row it writes or removes.
/// </summary>
/// <remarks>Task 084d-2b: what the write would close. <see cref="SwitchedOffRuleId"/> is the record rule a changed-rule removal switches Off (its
/// open findings close, D-170); <see cref="Supersedes"/> is a change's close of its base rule's findings (089e). Both null: the write closes nothing.</remarks>
internal sealed record ProcedureOverridePlan(
    string Write, string Kind, IReadOnlyList<Entity> AfterImages, IReadOnlyList<EntityReference> Created, IReadOnlyList<EntityReference> Deleted,
    Guid? SwitchedOffRuleId = null, PlannedSupersede? Supersedes = null)
{
    internal const string WriteAssign = "Assign";
    internal const string WriteExcludeRule = "ExcludeRule";
    internal const string WriteChangeRule = "ChangeRule";
    internal const string WriteRemove = "Remove";
}

/// <summary>
/// Task 084d-5a: the Procedures an assign could be planned for on one record (<c>PlanAssignCatalogAsync</c>). <see cref="TooMany"/>: more candidates
/// than the bound, and nothing else is given. Otherwise <see cref="OwnRows"/> are the candidates the record already has an override row for (the
/// write's 409 <c>assignment.duplicate</c>), and <see cref="Plan"/> holds one Follow-latest row image per other candidate.
/// </summary>
internal sealed record AssignableCatalog(bool TooMany, IReadOnlyList<AssignableOwnRow> OwnRows, ProcedureOverridePlan? Plan)
{
    /// <summary>Past the bound: no candidates, no own rows, no plan.</summary>
    internal static AssignableCatalog Over { get; } = new(true, [], null);
}

/// <summary>Task 084d-5a: a candidate Procedure the record already has override rows for (<see cref="RowIds"/>, any state).</summary>
internal sealed record AssignableOwnRow(Guid ProcedureId, string? Name, IReadOnlyList<Guid> RowIds);

/// <summary>Task 084d-2b: the inputs of a planned change's close (<c>SupersedeBaseFindingsAsync</c>): the base rule, the change's firm, its Exclude row and the in-force day.</summary>
internal sealed record PlannedSupersede(Guid BasePolicyId, Guid? FirmId, Guid ExcludeId, DateOnly InForceFrom);

/// <summary>
/// Task 084d-2b: what a planned override would close, and how many open invoices the caller can see are affected (<c>null</c>: cannot be told).
/// <see cref="Kinds"/> is empty when it closes nothing. <see cref="Warning"/> is the D-170 removal warning; <see cref="Message"/> the change's
/// own close message (089e), unchanged.
/// </summary>
internal sealed record PlannedCloses(IReadOnlyList<string> Kinds, string? Warning, string? Message, int? VisibleOpenInvoices)
{
    internal const string ChangedRuleRemoved = "ChangedRuleRemoved";
    internal const string BaseRuleSuperseded = "BaseRuleSuperseded";

    internal static PlannedCloses None { get; } = new([], null, null, null);
}

/// <summary>
/// Task 084d-2b: an In Review invoice in the reach of a record's override (filed on the record or on a record directly under it): the core record
/// it is filed on, its firm, and every date the evaluator judges it on (the invoice date, and each Fee or Expense line's work date, else the invoice date).
/// <see cref="Judgeable"/> is false for an invoice that cannot be judged (filed on other than exactly one core record, FR-05, or with no date); it
/// carries no <see cref="JudgedOn"/>. The reach reads every such invoice app-only, BEFORE the caller's Read narrows them, so the flag is reported
/// and never thrown on: whether a count is possible is decided on the invoices the caller can Read only (fix round 1, F-2). <see cref="JudgedOn"/>
/// is empty until <see cref="ProcedureAssignmentService.JudgedOnAsync"/> fills it for the invoices that were kept.
/// </summary>
internal sealed record InvoiceInReach(
    Guid InvoiceId, string CoreTable, Guid CoreId, Guid? FirmId, bool Judgeable, DateOnly? InvoiceDate, IReadOnlyList<DateOnly> JudgedOn);

/// <summary>
/// ebilling-r1 task 089e: the record a record-scoped rule set is filed under (<see cref="RecordRuleSet.HomeOf"/>). <see cref="CoreTable"/> is
/// the core table's logical name (<c>sprk_matter</c>, <c>sprk_project</c>, <c>sprk_workassignment</c>), or null when the set carries no
/// single core-record lookup.
/// </summary>
public sealed record RecordRuleSetHome(string? CoreTable, Guid CoreId);

/// <summary>
/// A record-scoped rule set (task 080 schema, model 5.2 and 6.3): the set that holds one record's changed rules. Its scope values live here,
/// outside the waived <see cref="RuleSetService"/> file (089a K4: the census's record-scope markers fail a waived file that names one).
/// <see cref="RuleSetService"/> asks <see cref="IsRecordScoped"/> so it can refuse such a set.
/// </summary>
public static class RecordRuleSet
{
    /// <summary><c>sprk_ruleset.sprk_scope</c> Record (one record, every firm) and Firm record (one record, one firm).</summary>
    public const int ScopeRecord = 100000002;
    public const int ScopeFirmRecord = 100000003;

    /// <summary>The name every record-scoped set carries (the set is found by its derived id, never by name).</summary>
    internal const string Name = "Billing instructions for this record";

    private const string ScopeColumn = "sprk_scope";
    private const string FirmColumn = "sprk_firm";
    private const string ProcedureColumn = "sprk_procedure";

    /// <summary>
    /// True for a rule set that holds one record's rules: a record scope (Record or Firm record), or any core-record lookup. Either alone
    /// counts, so a set carrying a core lookup without its scope still reads as record-scoped (fail closed; the lineage files a set under its
    /// core lookup alone).
    /// </summary>
    internal static bool IsRecordScoped(Entity set) =>
        set.GetAttributeValue<OptionSetValue>(ScopeColumn)?.Value is ScopeRecord or ScopeFirmRecord
        || InvoiceFamilyCoreRecordGuard.FilingCoreTables.Any(core => set.GetAttributeValue<EntityReference>(core) is not null);

    /// <summary>
    /// ebilling-r1 task 089e: where a record-scoped set lives, for the rule-set by-id routes' authorization. Null for a set that is not
    /// record-scoped. A record-scoped set carrying exactly one core-record lookup names that record; any other shape (none, or more than
    /// one) has no core table, which the route denies (fail closed).
    /// </summary>
    internal static RecordRuleSetHome? HomeOf(Entity set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (!IsRecordScoped(set))
        {
            return null;
        }

        var cores = InvoiceFamilyCoreRecordGuard.FilingCoreTables
            .Select(core => (Core: core, Ref: set.GetAttributeValue<EntityReference>(core)))
            .Where(c => c.Ref is not null && c.Ref.Id != Guid.Empty)
            .ToList();
        return cores.Count == 1 ? new RecordRuleSetHome(cores[0].Core, cores[0].Ref!.Id) : new RecordRuleSetHome(null, Guid.Empty);
    }

    /// <summary>
    /// The write validator of every rule-set create (089a K1, <c>SecureChildLineage</c>): a set has one home.
    /// <list type="bullet">
    /// <item>A Procedure's set carries no core lookup and no record scope.</item>
    /// <item>A record-scoped set carries exactly one core lookup and a record scope, and it carries a firm exactly when its scope is Firm record.</item>
    /// <item>A legacy set carries neither.</item>
    /// </list>
    /// Anything else is a bug, and is thrown before the write.
    /// </summary>
    internal static void RequireOneHome(Entity set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var cores = InvoiceFamilyCoreRecordGuard.FilingCoreTables.Count(core => set.GetAttributeValue<EntityReference>(core) is not null);
        var scope = set.GetAttributeValue<OptionSetValue>(ScopeColumn)?.Value;
        var recordScope = scope is ScopeRecord or ScopeFirmRecord;
        var procedure = set.GetAttributeValue<EntityReference>(ProcedureColumn) is not null;
        var firm = set.GetAttributeValue<EntityReference>(FirmColumn) is not null;

        var problem = cores > 1 ? "more than one core-record lookup"
            : procedure && (cores > 0 || recordScope) ? "a Procedure and a record"
            : cores == 1 && !recordScope ? "a core-record lookup without a record scope"
            : cores == 0 && recordScope ? "a record scope without a core-record lookup"
            : recordScope && firm != (scope == ScopeFirmRecord) ? "a record scope that does not match its firm"
            : null;
        if (problem is not null)
        {
            throw new InvalidOperationException($"A rule set must have one home; this one carries {problem}. Nothing was written.");
        }
    }
}

/// <summary>
/// The assignment and override service (ebilling-r1 task 089b, EB-D-148; procedure model 5.1, 6.2, 6.3, 6.5 I-A1): assign, pin or exclude a
/// Procedure for one core record, and exclude one rule for it. Task 089c adds change rule and remove with preview to this service.
/// </summary>
/// <remarks>
/// <para><b>Identity.</b> The routes have already run step 0 and checked, as the caller: Write and AppendTo on the core record (the uniform
/// 404), Read on the Procedure or on the rule's policy, and the billing-admin privilege (<c>ProcedureAssignmentEndpoints</c>). The write then
/// runs as the BFF application user: no role holds a privilege on <c>sprk_procedureassignment</c> (uac-r2 2026-10-21, pinned by
/// <c>ProcedureRolePrivilegeGuardTests</c>), and its core lookups are field-secured to the BFF. The core table is fixed per route from
/// <see cref="InvoiceFamilyCoreRecordGuard.FilingCoreTables"/>, never from input.</para>
/// <para><b>Ownership (model 5.1, EB-D-168).</b> Each row is owned through <see cref="IRecordOwnershipResolver"/> by the row's own question
/// (<see cref="RecordOwnershipContext.ForChild"/>: its table and the lookups it holds; the Procedure, pinned version and excluded rule are
/// citations, so the core record is its one parent), with the caller as RequestedBy, so the creator person is the caller. The assignment is a
/// hard-deny table: an ordinary record's row goes to the memberless restricted-content owner team, a Secure record's to its Secure team. A
/// refusal writes nothing.</para>
/// <para><b>I-A1 (one row per subject).</b> One row per (record, Procedure) and one per (record, rule), firm or not: a second is 409
/// <c>assignment.duplicate</c>. The check is a read AND the row id: the id is derived from (core table, record, subject), so two racing
/// creates collide on the primary key and the second is refused too.</para>
/// <para><b>Change rule and remove (task 089c).</b> A change is an Exclude row of the rule (the SAME derived id as a plain Exclude, so the two
/// collide) plus a record rule. The record rule is a policy in the record's record-scoped set, with one Published version based on the
/// rule's version in force. Each of the set, the policy and the version is owned through the resolver by its own question (ForChild: the
/// set's parent is the core record, the policy's is the set, the version's is the policy), so each is written only after its parent exists.
/// The Exclude row, the version and the switch-on are one transaction. A removal deletes the row (I-A1's duplicate check ignores state, so a
/// deactivated row would block the subject for ever). For a changed rule, the same transaction switches the record rule Off, and its open
/// findings then close <c>PolicyRetired</c> through 081's shared close. The base rule's open findings are NOT closed by a change (the stop
/// in POML 089c; see the 089 notes).</para>
/// <para><b>Not here</b> (POML 089): the resolver that reads these rows (082), the read models (084), the O-17 sweep, delete code.</para>
/// <para>§11: <i>Existing</i> — <see cref="RuleSetService"/> writes rule sets without record scope behind admin routes and
/// <see cref="ProcedureService"/> writes Procedures; neither writes a row under a core record or is caller-checked against one.
/// <i>Extension</i> — neither can take these writes: their routes are checked against a Procedure or the admin marker only, and the rows here
/// are a core record's secure children. The ownership question, the outcome type and the transaction seam are reused. <i>Cost of doing
/// nothing</i> — no record can be pinned to an earlier Procedure Version, opt out of a Procedure or waive a rule (O-4, O-10).</para>
/// </remarks>
public class ProcedureAssignmentService
{
    // Stable error codes (ADR-019).
    internal const string CodeInvalid = "assignment.invalid";
    internal const string CodeDuplicate = "assignment.duplicate";
    internal const string CodeProcedureNotFound = "assignment.procedure_not_found";
    internal const string CodeProcedureRetired = "assignment.procedure_retired";
    internal const string CodeProcedureNotBilling = "assignment.procedure_not_billing";
    internal const string CodeRuleNotFound = "assignment.rule_not_found";
    internal const string CodeSystemRule = "assignment.system_rule";
    internal const string CodeConcurrentEdit = "assignment.concurrent_edit";
    internal const string CodeWriteFailed = "assignment.write_failed";
    internal const string CodeReadFailed = "assignment.read_failed";
    internal const string CodeRuleNotInForce = "assignment.rule_not_in_force";
    internal const string CodeRecordRuleConflict = "assignment.record_rule_conflict";
    internal const string CodeOverrideNotFound = "assignment.override_not_found";

    internal const string KindProcedure = "Procedure";
    internal const string KindRuleExcluded = "RuleExcluded";
    internal const string KindRuleChanged = "RuleChanged";

    internal const string DuplicateDetail = "This record already has an override for it. Remove that override first.";

    /// <summary>EB-D-228: the detail of <see cref="CodeProcedureNotBilling"/>.</summary>
    internal const string NotBillingDetail =
        "Only a Procedure whose Area is Billing can be assigned, pinned or excluded for a record; a Procedure of another area never applies to its invoices.";

    /// <summary>The confirmation of a changed rule's removal (model 15 answer 2, as the Procedure retire words it).</summary>
    internal const string RemoveChangedWarning =
        "Removing this override will close the rule it changed and remove pending decisions on active invoices.";

    private const int MaxNameChars = 200;
    private const int MaxReasonChars = 2000;
    private const int DefaultPriority = 500;

    // The tables this service creates rows of, named here so the ownership census resolves each create to its table in this file.
    private const string AssignmentTable = "sprk_procedureassignment";
    private const string RuleSetTable = "sprk_ruleset";
    private const string PolicyTable = "sprk_policy";
    private const string VersionTable = "sprk_policyversion";

    /// <summary><c>sprk_policyversion.sprk_basedonversion</c> (task 080): the base rule's version a record rule's version was made from.</summary>
    internal const string BasedOnVersionColumn = "sprk_basedonversion";

    /// <summary>The record-rule code prefix (POML 089 D-202: <c>BRR-{policyId:N}</c>, 36 characters).</summary>
    internal const string RecordRuleCodePrefix = "BRR-";

    // The firm existence check selects its key only, as RuleSetService.ExistsAsync does: sprk_organization's primary name is
    // sprk_organizationname, and a $select naming sprk_name fails (089b review 2 F1).
    private const string FirmIdColumn = "sprk_organizationid";

    /// <summary>The namespace of the derived row ids (version-5 UUIDs, as <see cref="RuleSetService.VersionIdFor"/>).</summary>
    internal static readonly Guid RowIdNamespace = new("a4c2e8f1-7b3d-4e59-9c06-1d8f2b7e3a90");

    private readonly IGenericEntityService _entities;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly RuleSetService _ruleSets;
    private readonly SignalClosureService _closure;
    private readonly AffectedInvoiceCounter _counter;
    private readonly ILogger<ProcedureAssignmentService> _logger;

    /// <summary>The clock a change's record-rule version is put in force by (<c>sprk_inforcefrom</c>) and its close dated by (082c C19 fix round 1).
    /// A test sets it; production uses the system clock.</summary>
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    public ProcedureAssignmentService(
        IGenericEntityService entities,
        IRecordOwnershipResolver ownership,
        RuleSetService ruleSets,
        SignalClosureService closure,
        AffectedInvoiceCounter counter,
        ILogger<ProcedureAssignmentService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _ruleSets = ruleSets ?? throw new ArgumentNullException(nameof(ruleSets));
        _closure = closure ?? throw new ArgumentNullException(nameof(closure));
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Procedure-level: assign, pin, exclude (model 6.2)
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns, pins or excludes a Procedure for one core record: one row, owned through the resolver with the core record as its parent. A
    /// pinned version must be a version of that Procedure that has been in force (Active or Superseded; EB-D-155: a pin grandfathers the
    /// record's invoices to it). A retired Procedure is refused.
    /// </summary>
    public virtual async Task<RuleSetOutcome<OverrideView>> AssignProcedureAsync(
        string coreTable, Guid recordId, ProcedureAssignmentRequest request, Guid? callerObjectId, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await AssignProcedureCoreAsync(coreTable, recordId, request, callerObjectId, ct);
        }
        catch (ReadFailedException ex)
        {
            return ReadFailed<OverrideView>(ex, "assign");
        }
    }

    private async Task<RuleSetOutcome<OverrideView>> AssignProcedureCoreAsync(
        string coreTable, Guid recordId, ProcedureAssignmentRequest request, Guid? callerObjectId, CancellationToken ct)
    {
        var (row, refusal) = await PlanAssignmentRowAsync<OverrideView>(_entities, coreTable, recordId, request, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        return await CreateRowAsync(row!, callerObjectId, "assign", ct) is { } failed
            ? failed
            : RuleSetOutcome<OverrideView>.Done(new OverrideView(
                row!.Id, KindProcedure, ModeName(row.Mode), row.ProcedureId, row.PinnedVersionId, null, null, row.Reason));
    }

    /// <summary>
    /// The read-and-validate half of an assign, pin or Procedure exclude (084d-1: shared by the write and its dry run, so both refuse alike):
    /// the row to create, or the refusal. Reads only, through <paramref name="entities"/>.
    /// </summary>
    private async Task<(AssignmentRow? Row, RuleSetOutcome<T>? Refusal)> PlanAssignmentRowAsync<T>(
        IGenericEntityService entities, string coreTable, Guid recordId, ProcedureAssignmentRequest request, CancellationToken ct) where T : class
    {
        var errors = new List<RuleSetError>();
        if (request.ProcedureId is not { } procedureId || procedureId == Guid.Empty)
        {
            errors.Add(Error("procedureId", "procedureId is required."));
            procedureId = Guid.Empty;
        }

        var mode = ProcedureAssignmentEntity.ModeFollowLatest;
        if (request.Mode is not null && !TryParseMode(request.Mode, out mode))
        {
            errors.Add(Error("mode", "mode must be FollowLatest, Pinned or Excluded."));
        }

        if (mode == ProcedureAssignmentEntity.ModePinned && (request.PinnedVersionId is null || request.PinnedVersionId == Guid.Empty))
        {
            errors.Add(Error("pinnedVersionId", "A pinned assignment names the Procedure Version it pins."));
        }
        else if (mode != ProcedureAssignmentEntity.ModePinned && request.PinnedVersionId is not null)
        {
            errors.Add(Error("pinnedVersionId", "Only a pinned assignment names a Procedure Version."));
        }

        CheckReason(errors, request.Reason, required: false);
        if (errors.Count > 0)
        {
            return (null, RuleSetOutcome<T>.Invalid(CodeInvalid, errors));
        }

        var procedure = await LoadByIdAsync(entities, ProcedureEntity.Entity, procedureId, AssignProcedureColumns, ct);
        if (AssignRefusalOf<T>(procedure) is { } refused)
        {
            return (null, refused);
        }

        var pinned = mode == ProcedureAssignmentEntity.ModePinned ? request.PinnedVersionId : null;
        if (pinned is { } versionId)
        {
            var version = await LoadByIdAsync(entities, ProcedureEntity.VersionEntity, versionId,
                new[] { ProcedureEntity.VersionProcedureColumn, ProcedureEntity.VersionStatusColumn }, ct);
            var status = version?.GetAttributeValue<OptionSetValue>(ProcedureEntity.VersionStatusColumn)?.Value;
            if (version?.GetAttributeValue<EntityReference>(ProcedureEntity.VersionProcedureColumn)?.Id != procedureId
                || status is not (ProcedureEntity.VersionStatusActive or ProcedureEntity.VersionStatusSuperseded))
            {
                return (null, RuleSetOutcome<T>.Invalid(CodeInvalid, new[]
                {
                    Error("pinnedVersionId", "Pin a version of this Procedure that has been in force (Active or Superseded)."),
                }));
            }
        }

        if (await HasRowAsync(entities, coreTable, recordId, ProcedureAssignmentEntity.ProcedureColumn, procedureId, ct))
        {
            return (null, Duplicate<T>());
        }

        return (ProcedureRowOf(coreTable, recordId, procedure!, mode, pinned, Trimmed(request.Reason)), null);
    }

    /// <summary>The Procedure columns an assign's checks and its row read (<see cref="AssignRefusalOf{T}"/>, <see cref="ProcedureRowOf"/>).</summary>
    private static readonly string[] AssignProcedureColumns = { ProcedureEntity.NameColumn, ProcedureEntity.StatusColumn, ProcedureEntity.AreaColumn };

    /// <summary>
    /// What an assign, pin or Procedure exclude refuses on the Procedure row alone, or null: no row (404 <c>procedure_not_found</c>), Retired
    /// (409 <c>procedure_retired</c>), or an Area that is not Billing (409 <c>procedure_not_billing</c>, EB-D-228). Shared by the write and the
    /// assignable list (task 084d-5a), so the list never offers what this refuses. Pure.
    /// </summary>
    private static RuleSetOutcome<T>? AssignRefusalOf<T>(Entity? procedure) where T : class
    {
        if (procedure is null)
        {
            return Refused<T>(StatusCodes.Status404NotFound, CodeProcedureNotFound, "The Procedure was not found.");
        }

        if (procedure.GetAttributeValue<OptionSetValue>(ProcedureEntity.StatusColumn)?.Value == ProcedureEntity.StatusRetired)
        {
            return Refused<T>(StatusCodes.Status409Conflict, CodeProcedureRetired, "A retired Procedure cannot be assigned, pinned or excluded.");
        }

        // EB-D-228 (O-24): the resolver reads Billing-area Procedures only (ProcedureResolver.ReadProceduresAsync), so a row for any other area
        // would never apply and would only look as if it did. A missing Area is not Billing (fail closed, as RuleSetService's draft check). The
        // versions are not read: a Procedure with only Draft versions is still accepted, ahead of publication.
        return procedure.GetAttributeValue<OptionSetValue>(ProcedureEntity.AreaColumn)?.Value != RuleDomains.PolicyDomainBilling
            ? Refused<T>(StatusCodes.Status409Conflict, CodeProcedureNotBilling, NotBillingDetail)
            : null;
    }

    /// <summary>The Procedure-level row an assign, pin or Procedure exclude creates, at its derived id: one builder for the write and the assignable list.</summary>
    private static AssignmentRow ProcedureRowOf(string coreTable, Guid recordId, Entity procedure, int mode, Guid? pinned, string? reason) => new(
        AssignmentIdFor(coreTable, recordId, ProcedureAssignmentEntity.ProcedureColumn, procedure.Id),
        coreTable, recordId, Name(procedure.GetAttributeValue<string>(ProcedureEntity.NameColumn), ModeLabel(mode)), mode,
        ProcedureId: procedure.Id, PinnedVersionId: pinned, ExcludedPolicyId: null, FirmId: null, Reason: reason);

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Rule-level: exclude (model 6.3)
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Excludes one rule for the record (for one firm's invoices when a firm is named): one Exclude row naming the rule and never a Procedure,
    /// owned through the resolver with the core record as its parent. The rule must be a live Billing rule outside any record-scoped set, and
    /// not the system rule.
    /// </summary>
    public virtual async Task<RuleSetOutcome<OverrideView>> ExcludeRuleAsync(
        string coreTable, Guid recordId, RuleExcludeRequest request, Guid? callerObjectId, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await ExcludeRuleCoreAsync(coreTable, recordId, request, callerObjectId, ct);
        }
        catch (ReadFailedException ex)
        {
            return ReadFailed<OverrideView>(ex, "exclude rule");
        }
    }

    private async Task<RuleSetOutcome<OverrideView>> ExcludeRuleCoreAsync(
        string coreTable, Guid recordId, RuleExcludeRequest request, Guid? callerObjectId, CancellationToken ct)
    {
        var (row, refusal) = await PlanExcludeRowAsync<OverrideView>(_entities, coreTable, recordId, request, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        return await CreateRowAsync(row!, callerObjectId, "exclude rule", ct) is { } failed
            ? failed
            : RuleSetOutcome<OverrideView>.Done(new OverrideView(
                row!.Id, KindRuleExcluded, ModeName(ProcedureAssignmentEntity.ModeExcluded), null, null, row.ExcludedPolicyId, row.FirmId, row.Reason));
    }

    /// <summary>The read-and-validate half of a rule Exclude (084d-1: shared by the write and its dry run). Reads only, through <paramref name="entities"/>.</summary>
    private async Task<(AssignmentRow? Row, RuleSetOutcome<T>? Refusal)> PlanExcludeRowAsync<T>(
        IGenericEntityService entities, string coreTable, Guid recordId, RuleExcludeRequest request, CancellationToken ct) where T : class
    {
        var errors = new List<RuleSetError>();
        if (request.PolicyId is not { } policyId || policyId == Guid.Empty)
        {
            errors.Add(Error("policyId", "policyId is required."));
            policyId = Guid.Empty;
        }

        CheckReason(errors, request.Reason, required: true);
        if (request.FirmId is { } firm
            && (firm == Guid.Empty || await LoadByIdAsync(entities, RuleSetService.FirmEntity, firm, new[] { FirmIdColumn }, ct) is null))
        {
            errors.Add(Error("firmId", "The firm was not found."));
        }

        if (errors.Count > 0)
        {
            return (null, RuleSetOutcome<T>.Invalid(CodeInvalid, errors));
        }

        var (rule, refusal) = await LoadRuleAsync<T>(entities, policyId, ct);
        if (refusal is not null)
        {
            return (null, refusal);
        }

        if (await HasRowAsync(entities, coreTable, recordId, ProcedureAssignmentEntity.ExcludedPolicyColumn, policyId, ct))
        {
            return (null, Duplicate<T>());
        }

        return (new AssignmentRow(
            AssignmentIdFor(coreTable, recordId, ProcedureAssignmentEntity.ExcludedPolicyColumn, policyId),
            coreTable, recordId, Name(rule!.Name, "Excluded rule"), ProcedureAssignmentEntity.ModeExcluded,
            ProcedureId: null, PinnedVersionId: null, ExcludedPolicyId: policyId, FirmId: request.FirmId, Reason: Trimmed(request.Reason)), null);
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Rule-level: change (model 6.3, task 089c)
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Changes one rule for the record (for one firm's invoices when a firm is named): the rule is excluded for the record, and a record rule
    /// takes its place. The record rule is based on the rule's version in force, with the edited members, and is validated by
    /// <see cref="PolicyVersionValidator"/> as every billing rule is. The rule must be one an Exclude accepts. A rule that applies to one firm
    /// only can be changed only for that firm (400).
    /// </summary>
    /// <remarks>
    /// <para>Three stages, each written only once its parent exists (the resolver refuses a parent that does not exist yet):</para>
    /// <list type="number">
    /// <item>The record's record-scoped set (derived id per record and firm), if it is missing. The core record is its parent. A second
    /// change of the record reuses it.</item>
    /// <item>The record rule's policy (derived id per set and rule, code <c>BRR-{id:N}</c>), Off and with no version, if it is missing.
    /// The set is its parent. A removed record rule is reused here, never created twice.</item>
    /// <item>ONE transaction: the Exclude row, the record rule's next version (Accepted, Published, based on the rule's version in force),
    /// and the switch-on (Enabled, current version), conditional on the policy's row version.</item>
    /// </list>
    /// <para>A failure in stage 3 leaves neither the Exclude row nor a rule in force. An Off policy and an empty set may stay behind, and
    /// the next change reuses them. A create that collides on a derived id or the code key is a retry only when the row it collided with
    /// is the same record's set, or a rule of that set based on THIS rule. Anything else is refused 409.</para>
    /// <para>Task 089e (owner option (b), 2026-11-04): after the commit, the open findings the BASE rule raised on the In Review invoices filed
    /// on this record and (082c C19) on the records directly under it that take the change (only that firm's, for a firm-scoped change) close
    /// <c>Superseded</c> (<see cref="SupersedeBaseFindingsAsync"/>).
    /// The close records the resolution only, not the record rule's version. The close is best effort: the change stays committed whatever it
    /// does, and the result says whether it completed, what it covered, and how many of those invoices the caller can see. A transaction that
    /// gives no answer (pass-1 K2) is read back: when the record rule's version row is there (stage 3 alone writes it; fix round 2), the change
    /// committed and the close runs.</para>
    /// </remarks>
    public virtual async Task<RuleSetOutcome<RuleChangedView>> ChangeRuleAsync(
        string coreTable, Guid recordId, RuleChangeRequest request, Guid? callerObjectId, string? callerBearerToken, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await ChangeRuleCoreAsync(coreTable, recordId, request, callerObjectId, callerBearerToken, ct);
        }
        catch (ReadFailedException ex)
        {
            return ReadFailed<RuleChangedView>(ex, "change rule");
        }
    }

    private async Task<RuleSetOutcome<RuleChangedView>> ChangeRuleCoreAsync(
        string coreTable, Guid recordId, RuleChangeRequest request, Guid? callerObjectId, string? callerBearerToken, CancellationToken ct)
    {
        var (basis, refusal) = await PlanChangeBasisAsync<RuleChangedView>(_entities, coreTable, recordId, request, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        var (policyId, baseRule, baseVersion, merged) = basis!;

        // Stage 1: the record's record-scoped set.
        var (setId, setRefusal) = await EnsureRecordRuleSetAsync(coreTable, recordId, request.FirmId, baseRule.VisibleToFirms, callerObjectId, ct);
        if (setRefusal is not null)
        {
            return setRefusal;
        }

        // Stage 2: the record rule's policy, Off and versionless until stage 3.
        var (recordRule, versions, ruleRefusal) = await EnsureRecordRuleAsync(setId, baseRule, callerObjectId, ct);
        if (ruleRefusal is not null)
        {
            return ruleRefusal;
        }

        // Stage 3, one transaction: the Exclude row, the record rule's next version, and the switch-on.
        var number = NextVersionNumber(versions);
        var versionId = RuleSetService.VersionIdFor(recordRule!.Id, number);
        var code = RecordRuleCode(recordRule.Id);
        var exclude = ChangeExcludeRow(coreTable, recordId, request, baseRule);
        var reason = exclude.Reason;

        var excludeCreate = await AssignmentCreateAsync(exclude, callerObjectId, imageOnly: false, ct);
        if (excludeCreate.Refused is { } excludeOwner)
        {
            return RuleSetOutcome<RuleChangedView>.OwnerRefused(excludeOwner);
        }

        // The one instant the record rule's version is put in force by: the close below compares work dates with its UTC day.
        var inForceFrom = Clock.GetUtcNow().UtcDateTime;
        var versionCreate = await RecordRuleVersionCreateAsync(
            versionId, recordRule.Id, code, number, merged, baseVersion.Id, inForceFrom, callerObjectId, imageOnly: false, ct);
        if (versionCreate.Refused is { } versionOwner)
        {
            return RuleSetOutcome<RuleChangedView>.OwnerRefused(versionOwner);
        }

        if (ConditionalUpdate(PolicyTable, recordRule, SwitchOn(versionId)) is not { } switchOn)
        {
            return StageFailed<RuleChangedView>();
        }

        var sent = await SendAsync(new OrganizationRequest[] { excludeCreate.Request!, versionCreate.Request!, switchOn }, exclude.Id, "change rule");
        if (sent == SendResult.Unknown && await CommittedAfterNoAnswerAsync(exclude.Id, versionId, recordRule.Id, number, merged, baseVersion.Id))
        {
            sent = SendResult.Done;
        }

        if (sent != SendResult.Done)
        {
            return WriteRefused<RuleChangedView>(sent, exclude.Id);
        }

        _logger.LogInformation("Override {AssignmentId}: rule {PolicyId} changed for {CoreTable} {RecordId}; record rule {RecordRuleId} v{Number} in force",
            exclude.Id, policyId, coreTable, recordId, recordRule.Id, number);

        // The change is committed and the record rule is in force: nothing from here on may turn that into an error (089e, as RemoveAsync).
        var close = await SupersedeBaseFindingsAsync(
            coreTable, recordId, request.FirmId, policyId, baseRule.Code, versionId, exclude.Id, ProcedureResolver.UtcDate(inForceFrom)!.Value);
        int? count;
        try
        {
            count = await _counter.CountVisibleOpenInvoicesAsync(close.Closed, callerBearerToken, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Override {AssignmentId}: rule changed; the affected-invoice count could not be made, so none is shown", exclude.Id);
            count = null;
        }

        return RuleSetOutcome<RuleChangedView>.Done(new RuleChangedView(
            new OverrideView(exclude.Id, KindRuleChanged, ModeName(ProcedureAssignmentEntity.ModeExcluded), null, null, policyId, request.FirmId, reason),
            new RecordRuleView(setId, recordRule.Id, code, versionId, number, baseVersion.Id, request.FirmId is null ? "Record" : "FirmRecord", request.FirmId),
            close.Outcome.IsComplete, ProcedureService.AffectedText(count), count, close.Message));
    }

    /// <summary>What a change is made from, once read and validated: the rule, its version in force, and the merged content of the record rule.</summary>
    private sealed record ChangeBasis(Guid PolicyId, BaseRule BaseRule, RuleSetService.VersionRow BaseVersion, RuleSetService.VersionRow Merged);

    /// <summary>The record rule's next version number: one more than the highest it has (1 for none).</summary>
    private static int NextVersionNumber(IReadOnlyList<Entity> versions) =>
        versions.Count == 0 ? 1 : versions.Max(v => v.GetAttributeValue<int?>("sprk_versionnumber") ?? 0) + 1;

    /// <summary>A change's Exclude row: the SAME derived id as a plain Exclude of the rule (I-A1).</summary>
    private static AssignmentRow ChangeExcludeRow(string coreTable, Guid recordId, RuleChangeRequest request, BaseRule baseRule) => new(
        AssignmentIdFor(coreTable, recordId, ProcedureAssignmentEntity.ExcludedPolicyColumn, baseRule.Id),
        coreTable, recordId, Name(baseRule.Name, "Changed rule"), ProcedureAssignmentEntity.ModeExcluded,
        ProcedureId: null, PinnedVersionId: null, ExcludedPolicyId: baseRule.Id, FirmId: request.FirmId, Reason: Trimmed(request.Reason));

    /// <summary>A removal's switch-off of the record rule a change wrote.</summary>
    private static Dictionary<string, object> SwitchOff() => new() { ["sprk_enabled"] = false };

    /// <summary>A removal of a changed rule whose record rule was read without its row version: nothing is sent.</summary>
    private static RuleSetOutcome<T> RemoveFailed<T>() where T : class =>
        Refused<T>(StatusCodes.Status500InternalServerError, CodeWriteFailed,
            "The override could not be removed and nothing was changed. Try again; if it keeps failing, contact support.");

    /// <summary>Stage 3's switch-on of the record rule: Enabled, with the new version current.</summary>
    private static Dictionary<string, object> SwitchOn(Guid versionId) => new()
    {
        ["sprk_enabled"] = true,
        ["sprk_currentversion"] = new EntityReference(VersionTable, versionId),
    };

    /// <summary>
    /// The read-and-validate half of a change, up to and including I-A1's duplicate check (084d-1: shared by the write and its dry run, so
    /// both refuse alike). Reads only, through <paramref name="entities"/>; the stages that write come after it.
    /// </summary>
    private async Task<(ChangeBasis? Basis, RuleSetOutcome<T>? Refusal)> PlanChangeBasisAsync<T>(
        IGenericEntityService entities, string coreTable, Guid recordId, RuleChangeRequest request, CancellationToken ct) where T : class
    {
        var errors = new List<RuleSetError>();
        if (request.PolicyId is not { } policyId || policyId == Guid.Empty)
        {
            errors.Add(Error("policyId", "policyId is required."));
            policyId = Guid.Empty;
        }

        CheckReason(errors, request.Reason, required: true);
        if (request is { RuleBody: null, MessageTemplate: null, ShortHeadline: null, Consequence: null })
        {
            errors.Add(Error("edit", "Nothing to change: name at least one of ruleBody, messageTemplate, shortHeadline or consequence."));
        }

        int? consequence = null;
        if (request.Consequence is not null)
        {
            if (RuleSetService.TryParseConsequence(request.Consequence, out var parsed))
            {
                consequence = (int)parsed;
            }
            else
            {
                errors.Add(Error("consequence", "consequence must be Reject, Flag or SuggestAdjustment."));
            }
        }

        if (request.FirmId is { } firm
            && (firm == Guid.Empty || await LoadByIdAsync(entities, RuleSetService.FirmEntity, firm, new[] { FirmIdColumn }, ct) is null))
        {
            errors.Add(Error("firmId", "The firm was not found."));
        }

        if (errors.Count > 0)
        {
            return (null, RuleSetOutcome<T>.Invalid(CodeInvalid, errors));
        }

        var (rule, refusal) = await LoadRuleAsync<T>(entities, policyId, ct, readProcedure: true);
        if (refusal is not null)
        {
            return (null, refusal);
        }

        var baseRule = rule!;

        // A rule of one firm's set checks only that firm's invoices: a change of it for another firm (or for every firm) would widen it.
        if (baseRule.FirmId is { } ruleFirm && request.FirmId != ruleFirm)
        {
            return (null, RuleSetOutcome<T>.Invalid(CodeInvalid, new[]
            {
                Error("firmId", "This rule applies to one firm's invoices only; a change of it names that firm."),
            }));
        }

        // In force = switched on, in a Published set, with a version of its own (089c review F1): retiring a Procedure switches its rules off
        // (sprk_enabled=false) but leaves statecode and the current version, so the version alone would copy a retired rule.
        // A retired owning Procedure ends it whatever the rule's flags say (pass 2): the retire writes in chunks, so a failed later chunk leaves
        // the Procedure retired with some rules still enabled.
        var current = baseRule is { Enabled: true, SetPublished: true, ProcedureRetired: false } && baseRule.CurrentVersionId is { } currentId
            ? await LoadByIdAsync(entities, VersionTable, currentId, BaseVersionColumns, ct)
            : null;
        if (current is null || current.GetAttributeValue<EntityReference>("sprk_policy")?.Id != policyId)
        {
            return (null, Refused<T>(StatusCodes.Status409Conflict, CodeRuleNotInForce,
                "The rule is not in force (switched off, its rule set not Published, or no version in force), so there is nothing to change. Exclude it instead."));
        }

        // The change is a copy of the version in force with the edited members, validated as every billing rule is (same type).
        var baseVersion = VersionOf(current);
        var merged = baseVersion with
        {
            RuleBody = request.RuleBody ?? baseVersion.RuleBody,
            Template = request.MessageTemplate ?? baseVersion.Template,
            Headline = request.ShortHeadline ?? baseVersion.Headline,
            Consequence = consequence ?? baseVersion.Consequence,
            State = (int)ProposalState.Accepted,
        };
        var invalid = _ruleSets.ValidateRule(
            new RuleSetService.PolicyRow(policyId, baseRule.Code, baseRule.Name, baseVersion.Id),
            merged.RuleType?.ToString(CultureInfo.InvariantCulture), merged);
        if (merged.RuleType is null)
        {
            invalid.Add(Error("ruleType", "The rule's version in force has no type."));
        }

        if (invalid.Count > 0)
        {
            return (null, RuleSetOutcome<T>.Invalid(CodeInvalid, invalid));
        }

        // I-A1: one row per (record, rule). A plain Exclude and a change of the same rule are the same row.
        if (await HasRowAsync(entities, coreTable, recordId, ProcedureAssignmentEntity.ExcludedPolicyColumn, policyId, ct))
        {
            return (null, Duplicate<T>());
        }

        return (new ChangeBasis(policyId, baseRule, baseVersion, merged), null);
    }

    /// <summary>
    /// Task 089e (pass-1 K2; fix round 2, F2; fix round 3, F2): after a stage-3 transaction that gave no answer, whether it committed. The
    /// transaction is all or nothing, so the row only stage 3 writes is the answer: the record rule's version at its derived id
    /// <paramref name="versionId"/>, under <paramref name="recordRuleId"/>. The Exclude row is not: a plain Exclude of the same rule writes a
    /// row at the same derived id (I-A1). Two concurrent changes of the same rule on the same record also derive the same version id, so the
    /// row's content must be the content this change wrote (<paramref name="merged"/>, <paramref name="number"/>,
    /// <paramref name="basedOnVersionId"/>): equal content is a truthful "committed" (the other change wrote what this one would have); a
    /// different content is the other change's row, so this one did not commit. The read runs on <see cref="CancellationToken.None"/>: a
    /// caller who went away must not turn a committed change into a 500. A read fault is no answer, so the change keeps its 500.
    /// </summary>
    private async Task<bool> CommittedAfterNoAnswerAsync(
        Guid excludeId, Guid versionId, Guid recordRuleId, int number, RuleSetService.VersionRow merged, Guid basedOnVersionId)
    {
        try
        {
            var version = await LoadByIdAsync(VersionTable, versionId, ReadBackVersionColumns, CancellationToken.None);
            var committed = version?.GetAttributeValue<EntityReference>("sprk_policy")?.Id == recordRuleId
                && VersionContentMatches(version, number, merged, basedOnVersionId);
            _logger.LogWarning("Override {AssignmentId}: the change rule transaction gave no answer; read back of version {VersionId}: {Committed}",
                excludeId, versionId, committed ? "committed" : "not committed");
            return committed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Override {AssignmentId}: the change rule transaction gave no answer, and the read back failed", excludeId);
            return false;
        }
    }

    /// <summary>The columns stage 3 writes to the record rule's version from the merged content (read back after a no-answer transaction).</summary>
    private static readonly string[] ReadBackVersionColumns =
    {
        "sprk_policy", "sprk_versionnumber", "sprk_ruletype", "sprk_rulebody", "sprk_messagetemplate", "sprk_shortheadline", "sprk_consequence",
        "sprk_proposalstatus", "sprk_clausereference", "sprk_clauselocation", BasedOnVersionColumn,
    };

    /// <summary>Whether a stored record-rule version holds the content stage 3 would have written (a missing and an empty text are the same).</summary>
    private static bool VersionContentMatches(Entity? v, int number, RuleSetService.VersionRow merged, Guid basedOnVersionId) =>
        v is not null
        && (v.GetAttributeValue<int?>("sprk_versionnumber") ?? 0) == number
        && v.GetAttributeValue<OptionSetValue>("sprk_ruletype")?.Value == merged.RuleType
        && SameText(v.GetAttributeValue<string>("sprk_rulebody"), merged.RuleBody)
        && SameText(v.GetAttributeValue<string>("sprk_messagetemplate"), merged.Template)
        && SameText(v.GetAttributeValue<string>("sprk_shortheadline"), merged.Headline)
        && v.GetAttributeValue<OptionSetValue>("sprk_consequence")?.Value == merged.Consequence
        && v.GetAttributeValue<OptionSetValue>("sprk_proposalstatus")?.Value == (int)ProposalState.Accepted
        && SameText(v.GetAttributeValue<string>("sprk_clausereference"), merged.ClauseReference)
        && SameText(v.GetAttributeValue<string>("sprk_clauselocation"), merged.ClauseLocation)
        && v.GetAttributeValue<EntityReference>(BasedOnVersionColumn)?.Id == basedOnVersionId;

    private static bool SameText(string? a, string? b) => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);

    /// <summary>The result message when the change's close of the base rule's findings completed (task 089e, pass-1 F2: it says what it covered).</summary>
    internal const string ChangeCloseCompleteMessage =
        "The rule was changed. Open findings of the rule it replaces were closed on invoices filed on this record and on the records directly under it whose work is all dated on or after the change, except where a record under it has its own change or exclusion of that rule. Invoices with work dated before the change keep their findings open for the reviewer, because the earlier rule still applies to that work. Records further down do not take this change, so their findings stay open for the reviewer.";

    /// <summary>The result message when the change's close of the base rule's findings did not complete (task 089e).</summary>
    internal const string ChangeCloseIncompleteMessage =
        "The rule was changed, but some findings of the rule it replaces could not be closed now. They stay open for the reviewer to resolve on the invoices of this record and of the records directly under it.";

    /// <summary>The result message when the record's own invoices were closed but the records directly under it could not be read (082c C19 fix round 1).</summary>
    internal const string ChangeCloseChildrenOpenMessage =
        "The rule was changed. Open findings of the rule it replaces were closed on invoices filed on this record whose work is all dated on or after the change, but the records directly under it could not be read now, so the invoices of those records stay open for the reviewer to resolve. Invoices with work dated before the change keep their findings open for the reviewer, because the earlier rule still applies to that work.";

    /// <summary>What the change's close did: the closure outcome (incomplete when the records under the record were not read), the findings closed, the reader's message.</summary>
    private sealed record SupersedeResult(SignalClosureOutcome Outcome, IReadOnlyList<OpenPolicySignal> Closed, string Message);

    /// <summary>
    /// Task 089e (owner option (b), 2026-11-04): closes as <c>Superseded</c> the open findings of the base rule <paramref name="basePolicyId"/>
    /// on the In Review invoices filed on this record and on the records directly under it that take the change (for a firm-scoped change,
    /// that firm's invoices only), through the existing
    /// close (<see cref="SignalClosureService.SupersedeInPassAsync"/>). The close records the resolution only: the record rule's version is
    /// passed as the current version, but with every target outside the base rule's scope it decides nothing, and no version is stored.
    /// The reads are bounded by the record (pass-1 K4), never org-wide:
    /// <list type="number">
    /// <item>this record's In Review invoices: one query on the core lookup, the state and, for a firm-scoped change, the firm;</item>
    /// <item>082c C19: the In Review invoices of the records DIRECTLY under it on which this change decides the rule
    /// (<see cref="ChildrensInReviewInvoicesAsync"/>: bounded, nearer rows win);</item>
    /// <item>only those invoices' lines;</item>
    /// <item>the base rule's open Signals regarding those invoices or lines, through the close's own selection
    /// (<see cref="SignalClosureService.SelectOpenSignalsOfPolicyOnSubjectsAsync"/>).</item>
    /// </list>
    /// A Signal's invoice is its own invoice when it names one, else its line's invoice (as before), and it is kept only when that is one of
    /// those In Review invoices. The invoice reads are app-only, as the counter's are; the caller-scoped count is made from what this
    /// returns. Never throws: a fault is counted as a policy failure, so the result is incomplete.
    /// </summary>
    /// <remarks>Run after the commit, as <see cref="RemoveAsync"/> runs its close: closing first would leave closed findings behind a change that
    /// failed. Unlike that close, nothing finishes this one later (the nightly run does not evaluate Billing rules, and the base rule stays on),
    /// so the result says the rest stay open for the reviewer. Distance 0 and 1 (082c C19, the pass-1 F2 hand-off): a record's direct
    /// children take its change; their own children do not (D-172), so the close never reaches them.
    /// <para><b>The effective date wins (owner, 2026-11-05; fix round 1).</b> The record rule's version runs only from its own
    /// <c>sprk_inforcefrom</c> (<paramref name="inForceFrom"/>, the UTC day of the instant the version is written with); the resolver runs
    /// the base rule on every earlier work date (<c>ProcedureResolver.RecordRuleVersionOn</c>). So an invoice is closed only when the resolver
    /// would run the record rule, not the base rule, on EVERY date it judges the invoice on (<see cref="TakesTheChange"/>): any earlier-dated
    /// line, or a date that cannot be told, keeps its findings open. Invoice-level: a finding of the rule on a line is closed with the
    /// invoice's other findings or not at all.</para>
    /// <para><b>Order and failure (ontology, fix round 1).</b> The record's own invoices are read first: a fault there closes nothing. The
    /// records under it are read next, and a fault, the bound or a row the BFF never writes drops that side only (all or nothing within it):
    /// the record's own invoices still close, the answer is incomplete, and the message says the invoices of the records under it stay open.</para>
    /// <para><b>Close only (uac-r2 K5).</b> The close is <see cref="SignalWriter.CloseAsync"/>: a conditional update of the Signal's status,
    /// resolution type and timestamps. It never reopens, creates, edits a finding's content or deletes, and it shows the caller nothing of a
    /// record the caller cannot read.</para>
    /// </remarks>
    /// <summary>The most closed Signal ids one completion log line names (ontology, C19: a bounded line); the rest are counted.</summary>
    internal const int MaxClosedSignalIdsLogged = 50;

    /// <summary>The closed Signal ids for the completion log line: ordered, at most <see cref="MaxClosedSignalIdsLogged"/>, then "+N more".</summary>
    internal static string ClosedSignalIdsForLog(IReadOnlyCollection<Guid> closedIds)
    {
        var shown = string.Join(",", closedIds.Order().Take(MaxClosedSignalIdsLogged));
        return closedIds.Count > MaxClosedSignalIdsLogged ? $"{shown} (+{closedIds.Count - MaxClosedSignalIdsLogged} more)" : shown;
    }

    private async Task<SupersedeResult> SupersedeBaseFindingsAsync(
        string coreTable, Guid recordId, Guid? firmId, Guid basePolicyId, string baseCode, Guid recordRuleVersionId, Guid assignmentId, DateOnly inForceFrom)
    {
        try
        {
            var selection = await SelectBaseFindingsAsync(_entities, coreTable, recordId, firmId, basePolicyId, inForceFrom);
            var (targets, childrenRead, heldBack, childInvoices) = (selection.Targets, selection.ChildrenFault is null, selection.HeldBack, selection.ChildInvoices);
            if (selection.ChildrenFault is CoreRecordChainUnreadableException { CandidateCount: { } candidates })
            {
                _logger.LogWarning(
                    "Override {AssignmentId}: rule changed; more than {MaxChildRecords} core records may be filed directly under {CoreTable} {RecordId} ({ChildCount} counted before the read stopped); the invoices of the records under it stay open for the reviewer",
                    assignmentId, MaxChildRecords, coreTable, recordId, candidates);
            }
            else if (selection.ChildrenFault is { } childrenFault)
            {
                _logger.LogError(
                    "Override {AssignmentId}: rule changed; the records directly under {CoreTable} {RecordId} could not be read ({ExceptionType}); the invoices of the records under it stay open for the reviewer",
                    assignmentId, coreTable, recordId, childrenFault.GetType().Name);
            }

            if (targets.Count == 0)
            {
                _logger.LogInformation(
                    "Override {AssignmentId}: no open finding of base rule {BaseCode} on {CoreTable} {RecordId}'s In Review invoices to close ({HeldBack} invoice(s) left open for work dated before the change)",
                    assignmentId, baseCode, coreTable, recordId, heldBack);
                return new SupersedeResult(
                    childrenRead ? SignalClosureOutcome.None : SignalClosureOutcome.None with { PolicyFailures = 1 },
                    Array.Empty<OpenPolicySignal>(),
                    childrenRead ? ChangeCloseCompleteMessage : ChangeCloseChildrenOpenMessage);
            }

            // Every target is outside the base rule's scope now (this record is excluded from it), whatever version it cites.
            // Close only: SupersedeInPassAsync -> SignalWriter.CloseAsync is a conditional update of status/resolution/timestamps on the Signal.
            var (after, outcome, _) = await _closure.SupersedeInPassAsync(
                baseCode, recordRuleVersionId, targets.Select(t => t.Signal).ToList(), CancellationToken.None, outsideScope: static _ => true);
            var closedIds = after.Where(s => s.Resolution == SignalWriter.ResolutionSuperseded).Select(s => s.Id).ToHashSet();
            // uac-r2 K5 (c): the close is system-attributed; this line joins the triggering rule-change row ({AssignmentId}, whose createdby and
            // time are the audit) with the Signals it closed. Operator log only; the caller's response carries caller-scoped counts.
            _logger.LogInformation(
                "Override {AssignmentId}: {Closed} of {Targeted} finding(s) of base rule {BaseCode} on {CoreTable} {RecordId}'s In Review invoices ({ChildInvoices} of records directly under it) closed Superseded by record rule version {RecordRuleVersionId}; {HeldBack} invoice(s) left open for work dated before the change; complete={Complete}; closed Signals {ClosedSignalIds}",
                assignmentId, outcome.Closed, targets.Count, baseCode, coreTable, recordId, childInvoices, recordRuleVersionId, heldBack, outcome.IsComplete && childrenRead,
                ClosedSignalIdsForLog(closedIds));
            var message = !outcome.IsComplete ? ChangeCloseIncompleteMessage : childrenRead ? ChangeCloseCompleteMessage : ChangeCloseChildrenOpenMessage;
            return new SupersedeResult(
                childrenRead ? outcome : outcome with { PolicyFailures = outcome.PolicyFailures + 1 },
                targets.Where(t => closedIds.Contains(t.Signal.Id)).ToList(),
                message);
        }
        catch (Exception ex)
        {
            // The type name only (brief C16): the exception text can hold a record id the caller cannot read.
            _logger.LogError("Override {AssignmentId}: rule changed; the open findings of base rule {BaseCode} on {CoreTable} {RecordId}'s invoices could not be closed now ({ExceptionType}). They stay open for the reviewer",
                assignmentId, baseCode, coreTable, recordId, ex.GetType().Name);
            return new SupersedeResult(SignalClosureOutcome.None with { PolicyFailures = 1 }, Array.Empty<OpenPolicySignal>(), ChangeCloseIncompleteMessage);
        }
    }

    /// <summary>
    /// Task 084d-2b: what <see cref="SelectBaseFindingsAsync"/> selected. <see cref="Targets"/> are the open findings the close acts on;
    /// <see cref="ChildrenFault"/> is set when the records directly under the record were not read (that side is dropped); <see cref="HeldBack"/>
    /// counts the invoices left open for work dated before the change; <see cref="ChildInvoices"/> the invoices of the records under it.
    /// </summary>
    internal sealed record SupersedeSelection(IReadOnlyList<OpenPolicySignal> Targets, Exception? ChildrenFault, int HeldBack, int ChildInvoices);

    /// <summary>
    /// Task 084d-2b: the selection half of <see cref="SupersedeBaseFindingsAsync"/>, pulled out so the write's close and the dry run share ONE
    /// selection (preview = sweep, model 15 answer 2). Reads only, through <paramref name="entities"/>: the write passes its own service after
    /// the commit; the dry run passes its After overlay, so the change's Exclude row decides on the records under the record as it will once
    /// written. A fault on the record's own invoices throws (the close closes nothing); a fault on the records under it drops that side only and is
    /// returned in <see cref="SupersedeSelection.ChildrenFault"/>. <paramref name="ct"/> is the dry run's request token (the write passes none, so its reads are unchanged).
    /// </summary>
    private async Task<SupersedeSelection> SelectBaseFindingsAsync(
        IGenericEntityService entities, string coreTable, Guid recordId, Guid? firmId, Guid basePolicyId, DateOnly inForceFrom, CancellationToken ct = default)
    {
        // d0 first: a fault here throws, so the close closes nothing.
        var mine = await InvoicesTakingTheChangeAsync(entities, await RecordsInReviewInvoicesAsync(entities, coreTable, recordId, firmId, ct), inForceFrom, ct);

        // d1 next: a fault, the bound or an unexpected row drops this side only; the record's own invoices still close.
        var children = TakingTheChange.None;
        Exception? childrenFault = null;
        try
        {
            children = await InvoicesTakingTheChangeAsync(
                entities, await ChildrensInReviewInvoicesAsync(entities, coreTable, recordId, firmId, basePolicyId, ct), inForceFrom, ct);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            childrenFault = ex;
        }

        var invoices = mine.Invoices.Union(children.Invoices).ToHashSet();
        var lineInvoices = mine.LineInvoices.Concat(children.LineInvoices).ToDictionary(l => l.Key, l => l.Value);
        var open = invoices.Count == 0
            ? Array.Empty<OpenPolicySignal>()
            : await _closure.SelectOpenSignalsOfPolicyOnSubjectsAsync(basePolicyId, invoices, lineInvoices.Keys.ToList(), ct);
        Guid? InvoiceOf(OpenPolicySignal s) =>
            s.RegardingInvoiceId is { } invoice && invoice != Guid.Empty ? invoice
            : s.RegardingInvoiceLineId is { } line && lineInvoices.TryGetValue(line, out var ofLine) ? ofLine
            : null;

        var targets = open.Where(s => InvoiceOf(s) is { } invoice && invoices.Contains(invoice)).ToList();
        return new SupersedeSelection(targets, childrenFault, mine.HeldBack + children.HeldBack, children.Invoices.Count);
    }

    /// <summary>One side of the close (the record's own invoices, or those of the records directly under it): the invoices the change decides the
    /// rule on for every date they are judged on, the lines of those invoices, and how many invoices were left open by a date.</summary>
    private sealed record TakingTheChange(HashSet<Guid> Invoices, Dictionary<Guid, Guid> LineInvoices, int HeldBack)
    {
        public static TakingTheChange None => new([], [], 0);
    }

    /// <summary>A line of an invoice as the close reads it: its invoice, cost type (the evaluator judges Fee and Expense lines only) and work date.</summary>
    private readonly record struct InvoiceLine(Guid LineId, Guid InvoiceId, int? CostType, DateOnly? Date);

    /// <summary>
    /// Of the In Review invoices <paramref name="invoiceDates"/> (invoice id and its judging date), the ones the change decides the base rule
    /// on for every date the resolver judges them on (<see cref="TakesTheChange"/>), with their lines. One read of the invoices' lines.
    /// </summary>
    private async Task<TakingTheChange> InvoicesTakingTheChangeAsync(IGenericEntityService entities, IReadOnlyDictionary<Guid, DateOnly?> invoiceDates, DateOnly inForceFrom, CancellationToken ct)
    {
        if (invoiceDates.Count == 0)
        {
            return TakingTheChange.None;
        }

        var lines = await LinesOfInvoicesAsync(entities, invoiceDates.Keys.ToList(), ct);
        var byInvoice = lines.ToLookup(l => l.InvoiceId);
        var taking = invoiceDates.Where(i => TakesTheChange(inForceFrom, i.Value, byInvoice[i.Key])).Select(i => i.Key).ToHashSet();
        return new TakingTheChange(taking, lines.Where(l => taking.Contains(l.InvoiceId)).ToDictionary(l => l.LineId, l => l.InvoiceId), invoiceDates.Count - taking.Count);
    }

    /// <summary>
    /// Whether the resolver, after the change, runs the record rule and not the base rule on EVERY date it judges the invoice on (owner
    /// 2026-11-05, "the effective date wins"). The dates are the evaluator's own (<c>InvoiceComplianceEvaluator</c>): a line is judged on
    /// its work date, else on the invoice date; the invoice itself is judged on the invoice date, which is the invoice date column, else the
    /// UTC day the invoice was created (<see cref="InvoiceAdjustmentService.InvoiceDateOf"/>, the evaluator's received-on fallback). Only
    /// Fee and Expense lines are judged (an adjustment line is not). The comparison with the version's in-force day is the resolver's own
    /// (<see cref="ProcedureResolution.InForceOn"/>, the expression <c>RecordRuleVersionOn</c> selects by): on the day itself the record rule runs.
    /// An invoice date that cannot be told leaves the invoice open: no date, no close.
    /// </summary>
    private static bool TakesTheChange(DateOnly inForceFrom, DateOnly? invoiceDate, IEnumerable<InvoiceLine> lines)
    {
        if (invoiceDate is not { } invoiced)
        {
            return false;
        }

        return ProcedureResolution.InForceOn(inForceFrom, invoiced)
            && lines
                .Where(l => l.CostType is (int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense)
                .All(l => ProcedureResolution.InForceOn(inForceFrom, l.Date ?? invoiced));
    }

    /// <summary>The most core records directly under a record the change's close reads (082c C19). Past it the close of the records under it is dropped.</summary>
    internal const int MaxChildRecords = 100;

    /// <summary>
    /// ebilling-r1 082c C19 (the 089e hand-off): the In Review invoices of the core records DIRECTLY under this record (D-172: distance 1
    /// only; a grandchild's chain does not reach this record, so it does not inherit the change) on which this change decides the base rule,
    /// with each invoice's judging date.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>The children: <see cref="CoreRecordChain.ReadChildrenAsync"/>, the inverse of the chain the resolver judges by, at most
    /// <see cref="MaxChildRecords"/>. Past the bound, or on any fault, it throws.</item>
    /// <item>Their open (Active, In Review) invoices, filed on exactly one core record, that child (FR-05: an invoice with two core lookups
    /// has no chain); for a firm-scoped change, of that firm. One query per child table and chunk; more than one page throws.</item>
    /// <item><b>Nearer wins</b>, by the resolver's own rule: the override rows on each child's chain
    /// (<see cref="ProcedureResolver.ReadOverridesAsync"/>, which also faults on a row the BFF never writes), and an invoice is kept only when
    /// THIS record is among the rows that decide the base rule for the invoice's firm (<see cref="ProcedureResolver.RuleOverrideDeciders"/>).
    /// A child with its own change or Exclude of the rule for that firm decides it nearer, so its invoices are not this change's.</item>
    /// </list>
    /// App-only reads, as the d0 selection's. Throws on any fault, so the caller drops this whole side.
    /// </remarks>
    private async Task<Dictionary<Guid, DateOnly?>> ChildrensInReviewInvoicesAsync(IGenericEntityService entities, string coreTable, Guid recordId, Guid? firmId, Guid basePolicyId, CancellationToken ct)
    {
        var children = await CoreRecordChain.ReadChildrenAsync(entities, _logger, coreTable, recordId, MaxChildRecords, ct);
        var kept = new Dictionary<Guid, DateOnly?>();
        if (children.Count == 0)
        {
            return kept;
        }

        var lookups = InvoiceFamilyCoreRecordGuard.CoreRecordLookups[AffectedInvoiceCounter.InvoiceEntity];
        var columns = lookups.Select(l => l.Column).Append(InvoiceFirmColumn).Append(InvoiceAdjustmentService.InvoiceDateColumn).Append("createdon").ToArray();
        foreach (var group in children.GroupBy(c => c[0].Table))
        {
            var coreColumn = lookups.Single(l => l.CoreEntity == group.Key).Column;
            foreach (var batch in group.Chunk(MaxInValues))
            {
                var query = new QueryExpression(AffectedInvoiceCounter.InvoiceEntity) { ColumnSet = new ColumnSet(columns) };
                query.Criteria.AddCondition(coreColumn, ConditionOperator.In, batch.Select(c => (object)c[0].Id).ToArray());
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, InvoiceStatusCodes.StateActive);
                query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, InvoiceStatusCodes.InReview);
                if (firmId is { } firm)
                {
                    query.Criteria.AddCondition(InvoiceFirmColumn, ConditionOperator.Equal, firm);
                }

                var rows = await ReadAsync(entities, query, ct);
                if (rows.MoreRecords)
                {
                    throw new InvalidOperationException("More In Review invoices on the records under the record than one page.");
                }

                var byChild = rows.Entities
                    .Where(r => lookups.Count(l => r.GetAttributeValue<EntityReference>(l.Column) is not null) == 1)
                    .ToLookup(r => r.GetAttributeValue<EntityReference>(coreColumn)?.Id ?? Guid.Empty);
                foreach (var chain in batch.Where(c => byChild.Contains(c[0].Id)))
                {
                    var overrides = await ProcedureResolver.ReadOverridesAsync(entities, chain, ct);
                    foreach (var invoice in byChild[chain[0].Id])
                    {
                        var invoiceFirm = invoice.GetAttributeValue<EntityReference>(InvoiceFirmColumn)?.Id;
                        if (ProcedureResolver.RuleOverrideDeciders(overrides, basePolicyId, invoiceFirm)
                            .Any(d => d.Table == coreTable && d.Id == recordId))
                        {
                            kept[invoice.Id] = InvoiceAdjustmentService.InvoiceDateOf(invoice);
                        }
                    }
                }
            }
        }

        return kept;
    }

    /// <summary>
    /// The lines of each of <paramref name="invoiceIds"/> (<c>sprk_billingevent</c>) with their invoice, cost type and work date
    /// (<c>sprk_eventdate</c>, as the corrections read it), app-only. Paged; more than <see cref="MaxLinePages"/> pages per batch throws (the
    /// close is then incomplete). A fault throws.
    /// </summary>
    private static async Task<List<InvoiceLine>> LinesOfInvoicesAsync(IGenericEntityService entities, IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct = default)
    {
        var lines = new List<InvoiceLine>();
        foreach (var batch in invoiceIds.Select(id => (object)id).Chunk(MaxInValues))
        {
            var query = new QueryExpression(AffectedInvoiceCounter.LineEntity)
            {
                ColumnSet = new ColumnSet(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, InvoiceSubmissionPersister.LineEventDate, InvoiceTotalsService.LineCostType),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 },
            };
            query.Criteria.AddCondition(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, ConditionOperator.In, batch);
            for (var page = 1; ; page++)
            {
                if (page > MaxLinePages)
                {
                    throw new InvalidOperationException($"More than {MaxLinePages} pages of invoice lines.");
                }

                var rows = await ReadAsync(entities, query, ct);
                foreach (var row in rows.Entities)
                {
                    if (row.GetAttributeValue<EntityReference>(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn)?.Id is { } invoice && invoice != Guid.Empty)
                    {
                        lines.Add(new InvoiceLine(
                            row.Id, invoice, row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value,
                            row.GetAttributeValue<DateTime?>(InvoiceSubmissionPersister.LineEventDate) is { } date ? DateOnly.FromDateTime(date) : null));
                    }
                }

                if (!rows.MoreRecords)
                {
                    break;
                }

                query.PageInfo.PageNumber = page + 1;
                query.PageInfo.PagingCookie = rows.PagingCookie;
            }
        }

        return lines;
    }

    /// <summary>The most pages of lines one batch of invoices is read for (5,000 rows a page).</summary>
    private const int MaxLinePages = 20;

    /// <summary>
    /// The open (Active, In Review) invoices filed under this record (its core lookup on <c>sprk_invoice</c>) and, when
    /// <paramref name="firmId"/> is set, of that firm (<c>sprk_vendororg</c>), each with its judging date: one query, app-only. More than one
    /// page throws (the close is then incomplete). A fault throws.
    /// </summary>
    private static async Task<Dictionary<Guid, DateOnly?>> RecordsInReviewInvoicesAsync(IGenericEntityService entities, string coreTable, Guid recordId, Guid? firmId, CancellationToken ct)
    {
        var coreColumn = InvoiceFamilyCoreRecordGuard.CoreRecordLookups[AffectedInvoiceCounter.InvoiceEntity].Single(l => l.CoreEntity == coreTable).Column;
        var query = new QueryExpression(AffectedInvoiceCounter.InvoiceEntity)
        {
            ColumnSet = new ColumnSet(coreColumn, InvoiceAdjustmentService.InvoiceDateColumn, "createdon"),
        };
        query.Criteria.AddCondition(coreColumn, ConditionOperator.Equal, recordId);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, InvoiceStatusCodes.StateActive);
        query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, InvoiceStatusCodes.InReview);
        if (firmId is { } firm)
        {
            query.Criteria.AddCondition(InvoiceFirmColumn, ConditionOperator.Equal, firm);
        }

        var rows = await ReadAsync(entities, query, ct);
        if (rows.MoreRecords)
        {
            throw new InvalidOperationException("More In Review invoices on the record than one page.");
        }

        return rows.Entities
            .Where(r => r.GetAttributeValue<EntityReference>(coreColumn)?.Id == recordId)
            .ToDictionary(r => r.Id, r => InvoiceAdjustmentService.InvoiceDateOf(r));
    }

    /// <summary><c>sprk_invoice.sprk_vendororg</c>: the invoice's firm (as <see cref="InvoiceContextService"/> names it).</summary>
    private const string InvoiceFirmColumn = InvoiceContextService.InvoiceFirm;

    /// <summary>The ids per <c>In</c> condition (as <see cref="AffectedInvoiceCounter"/>).</summary>
    private const int MaxInValues = 500;

    /// <summary>
    /// Stage 1: the record's record-scoped set (<see cref="RecordRuleSetIdFor"/>), created when missing. A create that collides on the id
    /// (another change got there first) reads the set back. The set must be THIS record's, of the right scope and firm, or the change is
    /// refused 409.
    /// </summary>
    private async Task<(Guid SetId, RuleSetOutcome<RuleChangedView>? Refusal)> EnsureRecordRuleSetAsync(
        string coreTable, Guid recordId, Guid? firmId, bool visibleToFirms, Guid? callerObjectId, CancellationToken ct)
    {
        var setId = RecordRuleSetIdFor(coreTable, recordId, firmId);
        var set = await LoadByIdAsync(RuleSetTable, setId, RecordSetColumns, ct);
        if (set is null)
        {
            var create = await RecordRuleSetCreateAsync(setId, coreTable, recordId, firmId, visibleToFirms, callerObjectId, imageOnly: false, ct);
            if (create.Refused is { } owner)
            {
                return (setId, RuleSetOutcome<RuleChangedView>.OwnerRefused(owner));
            }

            var sent = await SendAsync(new OrganizationRequest[] { create.Request! }, setId, "create the record's rule set");
            if (sent is not (SendResult.Done or SendResult.Duplicate))
            {
                return (setId, StageFailed<RuleChangedView>());
            }

            set = await LoadByIdAsync(RuleSetTable, setId, RecordSetColumns, ct);
        }

        return set is not null && IsTheRecordSet(set, coreTable, recordId, firmId)
            ? (setId, null)
            : (setId, RecordRuleConflict<RuleChangedView>());
    }

    /// <summary>
    /// Stage 2: the record rule's policy (<see cref="RecordRulePolicyIdFor"/>), created Off and versionless when missing, and its versions.
    /// An existing policy (a retry, a second change after a removal) is reused only when it is in THIS set, active, carries its derived code,
    /// and every version it has is based on a version of THIS rule (<see cref="IsReusableRecordRule"/>, <see cref="RecordRuleVersionsAsync"/>).
    /// Otherwise the change is refused 409.
    /// </summary>
    private async Task<(Entity? Policy, IReadOnlyList<Entity> Versions, RuleSetOutcome<RuleChangedView>? Refusal)> EnsureRecordRuleAsync(
        Guid setId, BaseRule baseRule, Guid? callerObjectId, CancellationToken ct)
    {
        var none = Array.Empty<Entity>();
        var recordRuleId = RecordRulePolicyIdFor(setId, baseRule.Id);
        var policy = await LoadByIdAsync(PolicyTable, recordRuleId, RecordRuleColumns, ct);
        if (policy is null)
        {
            var create = await RecordRuleCreateAsync(recordRuleId, setId, baseRule, callerObjectId, imageOnly: false, ct);
            if (create.Refused is { } owner)
            {
                return (null, none, RuleSetOutcome<RuleChangedView>.OwnerRefused(owner));
            }

            var sent = await SendAsync(new OrganizationRequest[] { create.Request! }, recordRuleId, "create the record rule");
            if (sent is not (SendResult.Done or SendResult.Duplicate))
            {
                return (null, none, StageFailed<RuleChangedView>());
            }

            policy = await LoadByIdAsync(PolicyTable, recordRuleId, RecordRuleColumns, ct);
        }

        if (policy is null || !IsReusableRecordRule(policy, setId, recordRuleId))
        {
            return (null, none, RecordRuleConflict<RuleChangedView>());
        }

        return await RecordRuleVersionsAsync(_entities, recordRuleId, baseRule.Id, ct) is { } versions
            ? (policy, versions, null)
            : (null, none, RecordRuleConflict<RuleChangedView>());
    }

    /// <summary>Stage 2's check of the record rule's policy: in THIS set, its derived code, active, Billing (the write and the dry run ask it alike).</summary>
    private static bool IsReusableRecordRule(Entity policy, Guid setId, Guid recordRuleId) =>
        policy.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id == setId
        && string.Equals(policy.GetAttributeValue<string>("sprk_policycode"), RecordRuleCode(recordRuleId), StringComparison.Ordinal)
        && (policy.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0) == 0
        && RuleDomains.IsBillingRow(policy);

    /// <summary>
    /// Stage 2's check of the record rule's versions: every one must name its base version, and every base version must be a version of THIS
    /// rule. Returns the versions, or null for a conflict. Reads only, through <paramref name="entities"/>.
    /// </summary>
    private async Task<IReadOnlyList<Entity>?> RecordRuleVersionsAsync(IGenericEntityService entities, Guid recordRuleId, Guid baseRuleId, CancellationToken ct)
    {
        // all versions: a record rule's versions (never a billing draft); every one must name its base version.
        var query = new QueryExpression(VersionTable) { ColumnSet = new ColumnSet("sprk_versionnumber", BasedOnVersionColumn) };
        query.Criteria.AddCondition("sprk_policy", ConditionOperator.Equal, recordRuleId);
        var versions = (await ReadAsync(entities, query, ct)).Entities.ToList();
        var basedOn = versions.Select(v => v.GetAttributeValue<EntityReference>(BasedOnVersionColumn)?.Id).ToList();
        if (basedOn.Any(id => id is null))
        {
            return null;
        }

        var baseIds = basedOn.Select(id => (object)id!.Value).Distinct().ToArray();
        if (baseIds.Length > 0)
        {
            var bases = new QueryExpression(VersionTable) { ColumnSet = new ColumnSet("sprk_policy") };
            bases.Criteria.AddCondition("sprk_policyversionid", ConditionOperator.In, baseIds);
            var found = (await ReadAsync(entities, bases, ct)).Entities;
            if (found.Count != baseIds.Length || found.Any(v => v.GetAttributeValue<EntityReference>("sprk_policy")?.Id != baseRuleId))
            {
                return null;
            }
        }

        return versions;
    }

    /// <summary>True when <paramref name="set"/> is the record-scoped Billing set of exactly this record and firm, and of no Procedure.</summary>
    private static bool IsTheRecordSet(Entity set, string coreTable, Guid recordId, Guid? firmId) =>
        RuleDomains.IsBillingRow(set)
        && set.GetAttributeValue<EntityReference>(coreTable)?.Id == recordId
        && InvoiceFamilyCoreRecordGuard.FilingCoreTables.Count(core => set.GetAttributeValue<EntityReference>(core) is not null) == 1
        && set.GetAttributeValue<OptionSetValue>("sprk_scope")?.Value == (firmId is null ? RecordRuleSet.ScopeRecord : RecordRuleSet.ScopeFirmRecord)
        && set.GetAttributeValue<EntityReference>("sprk_firm")?.Id == firmId
        && set.GetAttributeValue<EntityReference>(RuleSetService.ProcedureColumn) is null
        && (set.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0) == 0;

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Remove, with preview (model 6.3, 15 answer 2; task 089c)
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What the stored override row overrides, for the route's Read check before a removal or its preview: the Procedure, or the rule it
    /// excludes or changes. Null when the row is not this record's (the service then answers 404). A row that names neither is a bug, so
    /// this throws (and the route denies). A read fault throws.
    /// </summary>
    public virtual async Task<OverrideSubject?> SubjectOfOverrideAsync(string coreTable, Guid recordId, Guid overrideId, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        var row = await LoadOverrideAsync(_entities, coreTable, recordId, overrideId, ct);
        if (row is null)
        {
            return null;
        }

        return row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.ProcedureColumn)?.Id is { } procedureId
            ? new OverrideSubject(ProcedureEntitySet, procedureId)
            : row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.ExcludedPolicyColumn)?.Id is { } policyId
                ? new OverrideSubject(PolicyEntitySet, policyId)
                : throw new InvalidOperationException($"Override {overrideId} names neither a Procedure nor a rule.");
    }

    /// <summary>
    /// The read-only confirmation of a removal. A plain row closes nothing, so it has no warning and no count. A changed rule's removal
    /// switches the record rule Off and closes its open findings, so it shows <see cref="RemoveChangedWarning"/> and the caller-scoped count
    /// of the open invoices those findings are on. The count comes from the close's own selection
    /// (<see cref="SignalClosureService.SelectOpenSignalsOfPolicyAsync"/>) and is counted by <see cref="AffectedInvoiceCounter"/>. When the
    /// count cannot be told, the warning shows without it. Writes nothing.
    /// </summary>
    public virtual async Task<RuleSetOutcome<OverrideRemovalPreview>> PreviewRemovalAsync(
        string coreTable, Guid recordId, Guid overrideId, string? callerBearerToken, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        try
        {
            var row = await LoadOverrideAsync(_entities, coreTable, recordId, overrideId, ct);
            if (row is null)
            {
                return OverrideNotFound<OverrideRemovalPreview>();
            }

            var changed = await ChangedRuleOfAsync(_entities, coreTable, recordId, row, new ColumnSet(RecordRuleColumns), ct);
            if (changed is null)
            {
                return RuleSetOutcome<OverrideRemovalPreview>.Done(new OverrideRemovalPreview(row.Id, KindOf(row, changed: false), false, null, null, null));
            }

            int? count = null;
            try
            {
                var signals = await _closure.SelectOpenSignalsOfPolicyAsync(changed.Id, withBillingSubject: true, ct);
                count = await _counter.CountVisibleOpenInvoicesAsync(signals, callerBearerToken, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Override {AssignmentId}: the removal preview could not select the open findings; the warning shows without a count", row.Id);
            }

            return RuleSetOutcome<OverrideRemovalPreview>.Done(new OverrideRemovalPreview(
                row.Id, KindRuleChanged, true, RemoveChangedWarning, ProcedureService.AffectedText(count), count));
        }
        catch (ReadFailedException ex)
        {
            return ReadFailed<OverrideRemovalPreview>(ex, "removal preview");
        }
    }

    /// <summary>
    /// Removes the override (on confirm) by deleting the row, never by deactivating it (I-A1's duplicate check ignores state). For a changed
    /// rule, the same transaction switches the record rule Off, conditional on its row version. Its open findings then close
    /// <c>PolicyRetired</c> through <see cref="SignalClosureService.ClosePolicySignalsRetiredAsync"/>, the preview's selection run at
    /// confirm time. A close that fails leaves the rule Off, so the nightly sweep closes the rest, and the result says the close is
    /// incomplete. The result's count is the caller-scoped count of the invoices whose findings closed.
    /// </summary>
    public virtual async Task<RuleSetOutcome<OverrideRemoved>> RemoveAsync(
        string coreTable, Guid recordId, Guid overrideId, string? callerBearerToken, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        Entity? row;
        Entity? changed;
        try
        {
            row = await LoadOverrideAsync(_entities, coreTable, recordId, overrideId, ct);
            if (row is null)
            {
                return OverrideNotFound<OverrideRemoved>();
            }

            changed = await ChangedRuleOfAsync(_entities, coreTable, recordId, row, new ColumnSet(RecordRuleColumns), ct);
        }
        catch (ReadFailedException ex)
        {
            return ReadFailed<OverrideRemoved>(ex, "remove");
        }

        var requests = new List<OrganizationRequest> { new DeleteRequest { Target = new EntityReference(AssignmentTable, row.Id) } };
        if (changed is not null)
        {
            if (ConditionalUpdate(PolicyTable, changed, SwitchOff()) is not { } switchOff)
            {
                return RemoveFailed<OverrideRemoved>();
            }

            requests.Add(switchOff);
        }

        var sent = await SendAsync(requests, row.Id, "remove");
        if (sent == SendResult.Gone)
        {
            return OverrideNotFound<OverrideRemoved>();
        }

        if (sent != SendResult.Done)
        {
            return WriteRefused<OverrideRemoved>(sent, row.Id);
        }

        var kind = KindOf(row, changed is not null);
        if (changed is null)
        {
            _logger.LogInformation("Override {AssignmentId} removed ({Kind}); nothing to close", row.Id, kind);
            return RuleSetOutcome<OverrideRemoved>.Done(new OverrideRemoved(row.Id, kind, false, true, null, null, null));
        }

        // The removal is committed and the record rule is Off: nothing from here on may turn that into an error.
        var outcome = SignalClosureOutcome.None;
        var closed = new List<OpenPolicySignal>();
        try
        {
            var closure = await _closure.ClosePolicySignalsRetiredAsync(
                changed.Id, changed.GetAttributeValue<string>("sprk_policycode") ?? RecordRuleCode(changed.Id), withBillingSubject: true, CancellationToken.None);
            outcome = closure.Outcome;
            closed.AddRange(closure.Closed);
        }
        catch (Exception ex)
        {
            outcome = outcome with { PolicyFailures = outcome.PolicyFailures + 1 };
            _logger.LogError(ex, "Override {AssignmentId} removed; the open findings of record rule {RecordRuleId} could not be closed now. The rule is Off, so the nightly run closes them",
                row.Id, changed.Id);
        }

        int? count;
        try
        {
            count = await _counter.CountVisibleOpenInvoicesAsync(closed, callerBearerToken, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Override {AssignmentId} removed; the affected-invoice count could not be made, so none is shown", row.Id);
            count = null;
        }

        _logger.LogInformation("Override {AssignmentId} removed (changed rule): record rule {RecordRuleId} Off, {Closed} finding(s) closed, complete={Complete}",
            row.Id, changed.Id, outcome.Closed, outcome.IsComplete);
        return RuleSetOutcome<OverrideRemoved>.Done(new OverrideRemoved(
            row.Id, KindRuleChanged, true, outcome.IsComplete, ProcedureService.AffectedText(count), count,
            outcome.IsComplete ? null : "Some findings could not be closed now. The rule is off, so the nightly run closes the rest."));
    }

    /// <summary>This record's override row <paramref name="overrideId"/>, or null (another record's row, or none).</summary>
    private async Task<Entity?> LoadOverrideAsync(IGenericEntityService entities, string coreTable, Guid recordId, Guid overrideId, CancellationToken ct)
    {
        if (overrideId == Guid.Empty)
        {
            return null;
        }

        var query = new QueryExpression(AssignmentTable)
        {
            ColumnSet = new ColumnSet(
                coreTable, ProcedureAssignmentEntity.ProcedureColumn, ProcedureAssignmentEntity.ExcludedPolicyColumn,
                ProcedureAssignmentEntity.FirmColumn, ProcedureAssignmentEntity.ModeColumn),
            TopCount = 1,
        };
        query.Criteria.AddCondition("sprk_procedureassignmentid", ConditionOperator.Equal, overrideId);
        query.Criteria.AddCondition(coreTable, ConditionOperator.Equal, recordId);
        return (await ReadAsync(entities, query, ct)).Entities.FirstOrDefault(
            r => r.Id == overrideId && r.GetAttributeValue<EntityReference>(coreTable)?.Id == recordId);
    }

    /// <summary>
    /// The record rule an Exclude row changed: the policy derived from (the record's set for the row's firm, the excluded rule). It counts only
    /// when it exists in that set, is active and is switched on. An Off record rule (removed earlier, or left by a failed change) changes
    /// nothing, so its row is a plain Exclude.
    /// </summary>
    private async Task<Entity?> ChangedRuleOfAsync(IGenericEntityService entities, string coreTable, Guid recordId, Entity row, ColumnSet columns, CancellationToken ct)
    {
        if (row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.ExcludedPolicyColumn)?.Id is not { } basePolicyId)
        {
            return null;
        }

        var setId = RecordRuleSetIdFor(coreTable, recordId, row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.FirmColumn)?.Id);
        var policy = await LoadByIdAsync(entities, PolicyTable, RecordRulePolicyIdFor(setId, basePolicyId), columns, ct);
        return policy is not null
            && policy.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id == setId
            && policy.GetAttributeValue<bool?>("sprk_enabled") == true
            && (policy.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0) == 0
                ? policy
                : null;
    }

    private static string KindOf(Entity row, bool changed) =>
        row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.ProcedureColumn) is not null ? KindProcedure
        : changed ? KindRuleChanged
        : KindRuleExcluded;

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Dry-run planning (task 084d-1; ontology-r1 review F1)
    // ───────────────────────────────────────────────────────────────────────────────────────────────
    //
    // Each Plan*Async runs the write's own read-and-validate half (the same members the write runs, so the dry run refuses with the write's
    // codes) through a WriteRefusingEntityService, and then builds the rows the write would send with the row builders' image-only mode,
    // which returns before the owner question. Nothing here sends, asks an owner, closes a finding or counts an invoice. The change path is
    // where the write sends while still planning (stages 1 and 2 create the set and the policy, then read them back for their checks): the
    // plan stops before those sends and runs the same checks against the images instead.

    /// <summary>
    /// The rows an assign, pin or Procedure exclude would write (<see cref="AssignProcedureAsync"/>), without writing: one created assignment
    /// row. Refuses as the write does (400, 404, 409, 503 read failure).
    /// </summary>
    internal async Task<RuleSetOutcome<ProcedureOverridePlan>> PlanAssignProcedureAsync(
        WriteRefusingEntityService reads, string coreTable, Guid recordId, ProcedureAssignmentRequest request, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var (row, refusal) = await PlanAssignmentRowAsync<ProcedureOverridePlan>(reads, coreTable, recordId, request, ct);
            if (refusal is not null)
            {
                return refusal;
            }

            var image = (await AssignmentCreateAsync(row!, null, imageOnly: true, ct)).Image!;
            return Planned(ProcedureOverridePlan.WriteAssign, KindProcedure, created: new[] { image });
        }
        catch (ReadFailedException ex)
        {
            return ReadFailed<ProcedureOverridePlan>(ex, "assign dry run");
        }
    }

    /// <summary>Task 084d-5a: the longest name search the assignable list takes (after trimming).</summary>
    internal const int MaxNameSearchChars = 100;

    /// <summary>
    /// Task 084d-5a (round 1, item 5): the most Procedures the candidate query fetches, before the caller's Read filter. The fetch is never capped
    /// lower: rows the caller cannot Read would crowd out rows it can, and the <c>tooMany</c> boundary would tell it about them. A set past this is
    /// answered as <c>tooMany</c>. Known limit: that answer counts rows the caller cannot Read.
    /// </summary>
    internal const int MaxCandidateRows = 1000;

    /// <summary>
    /// Task 084d-5a (design 084d-5 §1, §4; EB-D-227, EB-D-229): the Procedures an assign of Follow latest could be planned for on one record,
    /// without writing, as ONE plan. Reads through <paramref name="reads"/> only.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><b>Candidates</b> (ontology-r1 Q1 and condition 1): Active (<c>sprk_status</c>), <c>statecode</c> 0, Area Billing, and Firm empty or
    /// <paramref name="firmId"/> (the list's firm; none: Firm empty only). The firm pre-filter mirrors the resolver's O-20 condition
    /// (<c>ProcedureResolution.FirmConditionHolds</c>) and the Area pre-filter its area re-check (<c>ProcedureResolver.ReadProceduresAsync</c>);
    /// the resolver still re-checks both. <paramref name="nameContains"/> narrows by name (Like wildcards escaped). Nothing else is filtered here:
    /// applicability, the published window and the version are the resolver's (the After read). Ordered by name, then id.</item>
    /// <item><b>Drop before judge</b> (uac-r2 ruling 2026-11-13, EB-D-218): the candidates not already on the record
    /// (<paramref name="onRecord"/>) go through <paramref name="readable"/>, the caller's batched Read check on the Procedure rows, and the ones
    /// the caller cannot Read are dropped HERE, before the bound, the own-row step and the resolver. An unreadable Procedure is then
    /// indistinguishable from one that does not exist (no 503, no <c>tooMany</c>, no <c>notOffered</c> entry). The check returning null (a
    /// refusal as a whole, or an answer it could not get) is 503 <c>assignment.read_failed</c>: never "drop all", never an empty 200. A fault in
    /// it propagates to the caller's 503. <paramref name="readable"/> is called for the own-row Procedures first, on every request (even with
    /// none: the caller is always resolved), then once per page of <see cref="RecordProcedureReadService.RightsPageSize"/> candidates.</item>
    /// <item><b>Own rows</b> (uac-r2 T1): a candidate the record already has an override row for, of any state (<see cref="OwnRowQuery"/>, the
    /// duplicate refusal's own predicate), is not planned; it is returned with its rows when the caller can Read its Procedure. Its id is taken
    /// from the record's own rows and set aside BEFORE the scan, so it never counts toward the stop or the bound.</item>
    /// <item><b>The bound and the stop</b> (EB-D-227): the other READABLE candidates number at most <paramref name="bound"/>, or the answer is
    /// <see cref="AssignableCatalog.TooMany"/> with nothing else: never a partial list. The scan stops after the page at which
    /// <paramref name="bound"/> + 1 of them are readable. The fetch is NOT capped before the Read filter (rows the caller cannot Read would crowd
    /// out rows it can, and the boundary would then tell it about them): it asks for every matching row up to <see cref="MaxCandidateRows"/>; a
    /// set past that ceiling is <c>tooMany</c> as well.</item>
    /// <item><b>The plan</b>: each other candidate that <see cref="AssignRefusalOf{T}"/> accepts gets the write's own Follow-latest row image
    /// (<see cref="ProcedureRowOf"/> and the image-only builder), all in ONE <see cref="ProcedureOverridePlan"/>.</item>
    /// </list>
    /// A read fault is 503 <c>assignment.read_failed</c>, as the write's.
    /// </remarks>
    internal async Task<RuleSetOutcome<AssignableCatalog>> PlanAssignCatalogAsync(
        WriteRefusingEntityService reads, string coreTable, Guid recordId, Guid? firmId, IReadOnlyCollection<Guid> onRecord, string? nameContains,
        int bound, Func<IReadOnlyList<Guid>, CancellationToken, Task<IReadOnlySet<Guid>?>> readable, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(onRecord);
        ArgumentNullException.ThrowIfNull(readable);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bound);
        try
        {
            var query = new QueryExpression(ProcedureEntity.Entity)
            {
                ColumnSet = new ColumnSet(AssignProcedureColumns),
                TopCount = MaxCandidateRows + 1, // one past the ceiling, to see that it was hit
                NoLock = true,
            };
            query.Criteria.AddCondition(ProcedureEntity.StatusColumn, ConditionOperator.Equal, ProcedureEntity.StatusActive);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(ProcedureEntity.AreaColumn, ConditionOperator.Equal, RuleDomains.PolicyDomainBilling);
            // The resolver's O-20 firm condition (FirmConditionHolds): no Firm, or the list's firm.
            var firm = new FilterExpression(LogicalOperator.Or);
            firm.AddCondition(ProcedureEntity.FirmColumn, ConditionOperator.Null);
            if (firmId is { } listFirm && listFirm != Guid.Empty)
            {
                firm.AddCondition(ProcedureEntity.FirmColumn, ConditionOperator.Equal, listFirm);
            }

            query.Criteria.AddFilter(firm);
            if (nameContains is not null)
            {
                query.Criteria.AddCondition(ProcedureEntity.NameColumn, ConditionOperator.Like, "%" + RecordContainerResolver.EscapeForLike(nameContains) + "%");
            }

            query.AddOrder(ProcedureEntity.NameColumn, OrderType.Ascending);
            query.AddOrder(ProcedureEntity.Entity + "id", OrderType.Ascending);

            var matching = (await ReadAsync(reads, query, ct)).Entities;
            if (matching.Count > MaxCandidateRows)
            {
                // Known limit (084d-5a round 1, item 5): the set cannot be judged within the ceiling, so it is answered as too many.
                return RuleSetOutcome<AssignableCatalog>.Done(AssignableCatalog.Over);
            }

            var offeredToTheCaller = matching.Where(p => !onRecord.Contains(p.Id)).ToList();

            // uac-r2 T1 (2026-11-13): the record's own-row Procedures are set aside BEFORE the scan, by id (the record's own assignment rows, any
            // state, are a small set that does not depend on the candidates), so they never count toward the stop or the bound.
            var own = await OwnRowsOfRecordAsync(reads, coreTable, recordId, ct);
            var ownCandidates = offeredToTheCaller.Where(p => own.ContainsKey(p.Id)).ToList();
            var scanned = offeredToTheCaller.Where(p => !own.ContainsKey(p.Id)).ToList();

            // The own-row Procedures get their own small check (one call), run first and ALWAYS, even with none: the caller is resolved on
            // every request, with or without candidates; only the per-row rights request is skipped for no ids.
            var visibleOwn = await readable(ownCandidates.Select(p => p.Id).ToList(), ct);
            if (visibleOwn is null)
            {
                return CandidateCheckRefused<AssignableCatalog>();
            }

            // The scan walks the rest in the list's order, one rights page at a time, and stops after the page at which bound + 1 offerable
            // readable candidates are in hand (more than the bound is tooMany whatever follows). Any page refused as a whole is 503, as before.
            var candidates = new List<Entity>();
            for (var asked = 0; asked < scanned.Count && candidates.Count <= bound; asked += RecordProcedureReadService.RightsPageSize)
            {
                var page = scanned.Skip(asked).Take(RecordProcedureReadService.RightsPageSize).ToList();
                var visible = await readable(page.Select(p => p.Id).ToList(), ct);
                if (visible is null)
                {
                    return CandidateCheckRefused<AssignableCatalog>();
                }

                candidates.AddRange(page.Where(p => visible.Contains(p.Id)));
            }

            if (candidates.Count > bound)
            {
                return RuleSetOutcome<AssignableCatalog>.Done(AssignableCatalog.Over);
            }

            var images = new List<Entity>();
            var ownRows = new List<AssignableOwnRow>();
            foreach (var candidate in ownCandidates.Where(p => visibleOwn.Contains(p.Id)))
            {
                // The write's 409 assignment.duplicate.
                ownRows.Add(new AssignableOwnRow(candidate.Id, candidate.GetAttributeValue<string>(ProcedureEntity.NameColumn), own[candidate.Id].Order().ToList()));
            }

            foreach (var candidate in candidates)
            {
                if (AssignRefusalOf<AssignableCatalog>(candidate) is null)
                {
                    var row = ProcedureRowOf(coreTable, recordId, candidate, ProcedureAssignmentEntity.ModeFollowLatest, null, null);
                    images.Add((await AssignmentCreateAsync(row, null, imageOnly: true, ct)).Image!);
                }
            }

            var plan = Planned(ProcedureOverridePlan.WriteAssign, KindProcedure, created: images).Value!;
            return RuleSetOutcome<AssignableCatalog>.Done(new AssignableCatalog(false, ownRows, plan));
        }
        catch (ReadFailedException ex)
        {
            return ReadFailed<AssignableCatalog>(ex, "assignable list");
        }
    }

    private RuleSetOutcome<T> CandidateCheckRefused<T>() where T : class
    {
        _logger.LogWarning("Override: the caller's Read check on the candidate Procedures was refused as a whole; nothing was listed");
        return Refused<T>(StatusCodes.Status503ServiceUnavailable, CodeReadFailed,
            "The override could not be checked right now and nothing was written. Try again; if it keeps failing, contact support.");
    }

    /// <summary>The record's own override rows by Procedure, any state (<see cref="OwnRowQuery"/> with no subject filter).</summary>
    private static async Task<Dictionary<Guid, List<Guid>>> OwnRowsOfRecordAsync(
        IGenericEntityService reads, string coreTable, Guid recordId, CancellationToken ct)
    {
        var own = new Dictionary<Guid, List<Guid>>();
        var query = OwnRowQuery(coreTable, recordId, ProcedureAssignmentEntity.ProcedureColumn, null);
        query.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
        var rows = await ReadAsync(reads, query, ct);
        if (rows.MoreRecords)
        {
            // Fails closed: own rows past one page would be dropped, and their Procedures offered as if the record had no row for them.
            throw new InvalidOperationException("More override rows on the record than one page.");
        }

        foreach (var row in rows.Entities)
        {
            if (row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.ProcedureColumn)?.Id is { } subject)
            {
                (own.TryGetValue(subject, out var ids) ? ids : own[subject] = new List<Guid>()).Add(row.Id);
            }
        }

        return own;
    }

    /// <summary>The rows a rule Exclude would write (<see cref="ExcludeRuleAsync"/>), without writing: one created Exclude row.</summary>
    internal async Task<RuleSetOutcome<ProcedureOverridePlan>> PlanExcludeRuleAsync(
        WriteRefusingEntityService reads, string coreTable, Guid recordId, RuleExcludeRequest request, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var (row, refusal) = await PlanExcludeRowAsync<ProcedureOverridePlan>(reads, coreTable, recordId, request, ct);
            if (refusal is not null)
            {
                return refusal;
            }

            var image = (await AssignmentCreateAsync(row!, null, imageOnly: true, ct)).Image!;
            return Planned(ProcedureOverridePlan.WriteExcludeRule, KindRuleExcluded, created: new[] { image });
        }
        catch (ReadFailedException ex)
        {
            return ReadFailed<ProcedureOverridePlan>(ex, "exclude rule dry run");
        }
    }

    /// <summary>
    /// The rows a rule change would write (<see cref="ChangeRuleAsync"/>), without writing, with its record rule in force from
    /// <paramref name="inForceFrom"/> (the dry run's "if saved now"):
    /// <list type="number">
    /// <item>Stage 1: the record's set, as an image when it does not exist yet. Either way it is checked as the write checks the set it reads
    /// back (<see cref="IsTheRecordSet"/>).</item>
    /// <item>Stage 2: the record rule's policy: an existing one is read whole; a missing one is the builder's image (Off, no version). Either way
    /// it gets the write's checks (<see cref="IsReusableRecordRule"/>, <see cref="RecordRuleVersionsAsync"/>).</item>
    /// <item>Stage 3: the Exclude row and the record rule's next version as images, and the switch-on applied to the policy's after-image. An
    /// existing policy read without its row version fails the plan as it fails the write's conditional switch-on.</item>
    /// </list>
    /// </summary>
    internal async Task<RuleSetOutcome<ProcedureOverridePlan>> PlanChangeRuleAsync(
        WriteRefusingEntityService reads, string coreTable, Guid recordId, RuleChangeRequest request, DateTime inForceFrom, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var (basis, refusal) = await PlanChangeBasisAsync<ProcedureOverridePlan>(reads, coreTable, recordId, request, ct);
            if (refusal is not null)
            {
                return refusal;
            }

            var (_, baseRule, baseVersion, merged) = basis!;
            var created = new List<Entity>();

            // Stage 1, planned.
            var setId = RecordRuleSetIdFor(coreTable, recordId, request.FirmId);
            var set = await LoadByIdAsync(reads, RuleSetTable, setId, RecordSetColumns, ct);
            if (set is null)
            {
                set = (await RecordRuleSetCreateAsync(setId, coreTable, recordId, request.FirmId, baseRule.VisibleToFirms, null, imageOnly: true, ct)).Image!;
                created.Add(set);
            }

            if (!IsTheRecordSet(set, coreTable, recordId, request.FirmId))
            {
                return RecordRuleConflict<ProcedureOverridePlan>();
            }

            // Stage 2, planned.
            var recordRuleId = RecordRulePolicyIdFor(setId, baseRule.Id);
            var policy = await LoadByIdAsync(reads, PolicyTable, recordRuleId, new ColumnSet(true), ct);
            var policyIsNew = policy is null;
            policy ??= (await RecordRuleCreateAsync(recordRuleId, setId, baseRule, null, imageOnly: true, ct)).Image!;
            if (!IsReusableRecordRule(policy, setId, recordRuleId)
                || await RecordRuleVersionsAsync(reads, recordRuleId, baseRule.Id, ct) is not { } versions)
            {
                return RecordRuleConflict<ProcedureOverridePlan>();
            }

            // Stage 3, planned.
            var number = NextVersionNumber(versions);
            var versionId = RuleSetService.VersionIdFor(recordRuleId, number);
            var exclude = ChangeExcludeRow(coreTable, recordId, request, baseRule);
            var switchOn = SwitchOn(versionId);
            if (!policyIsNew && ConditionalUpdate(PolicyTable, policy, switchOn) is null)
            {
                return StageFailed<ProcedureOverridePlan>();
            }

            var excludeImage = (await AssignmentCreateAsync(exclude, null, imageOnly: true, ct)).Image!;
            var versionImage = (await RecordRuleVersionCreateAsync(
                versionId, recordRuleId, RecordRuleCode(recordRuleId), number, merged, baseVersion.Id, inForceFrom, null, imageOnly: true, ct)).Image!;
            var policyAfter = Patched(policy, switchOn);
            if (policyIsNew)
            {
                created.Add(policyAfter);
            }

            created.Add(excludeImage);
            created.Add(versionImage);
            return Planned(
                ProcedureOverridePlan.WriteChangeRule, KindRuleChanged, created, updated: policyIsNew ? null : new[] { policyAfter },
                supersedes: new PlannedSupersede(baseRule.Id, request.FirmId, exclude.Id, ProcedureResolver.UtcDate(inForceFrom)!.Value));
        }
        catch (ReadFailedException ex)
        {
            return ReadFailed<ProcedureOverridePlan>(ex, "change rule dry run");
        }
    }

    /// <summary>
    /// The rows a removal would write (<see cref="RemoveAsync"/>), without writing: the override row deleted and, for a changed rule, the
    /// record rule read whole with the switch-off applied. Refuses as the write does (404 override not found, 500 when the record rule was
    /// read without its row version, 503 read failure).
    /// </summary>
    internal async Task<RuleSetOutcome<ProcedureOverridePlan>> PlanRemoveAsync(
        WriteRefusingEntityService reads, string coreTable, Guid recordId, Guid overrideId, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        ArgumentNullException.ThrowIfNull(reads);
        try
        {
            var row = await LoadOverrideAsync(reads, coreTable, recordId, overrideId, ct);
            if (row is null)
            {
                return OverrideNotFound<ProcedureOverridePlan>();
            }

            var changed = await ChangedRuleOfAsync(reads, coreTable, recordId, row, new ColumnSet(true), ct);
            var deleted = new[] { new EntityReference(AssignmentTable, row.Id) };
            if (changed is null)
            {
                return Planned(ProcedureOverridePlan.WriteRemove, KindOf(row, changed: false), Array.Empty<Entity>(), deleted: deleted);
            }

            var switchOff = SwitchOff();
            return ConditionalUpdate(PolicyTable, changed, switchOff) is null
                ? RemoveFailed<ProcedureOverridePlan>()
                : Planned(ProcedureOverridePlan.WriteRemove, KindRuleChanged, Array.Empty<Entity>(), new[] { Patched(changed, switchOff) }, deleted, switchedOff: changed.Id);
        }
        catch (ReadFailedException ex)
        {
            return ReadFailed<ProcedureOverridePlan>(ex, "remove dry run");
        }
    }

    private static RuleSetOutcome<ProcedureOverridePlan> Planned(
        string write, string kind, IReadOnlyList<Entity> created, IReadOnlyList<Entity>? updated = null, IReadOnlyList<EntityReference>? deleted = null,
        Guid? switchedOff = null, PlannedSupersede? supersedes = null) =>
        RuleSetOutcome<ProcedureOverridePlan>.Done(new ProcedureOverridePlan(
            write, kind,
            created.Concat(updated ?? Array.Empty<Entity>()).ToList(),
            created.Select(c => c.ToEntityReference()).ToList(),
            deleted ?? Array.Empty<EntityReference>(),
            switchedOff, supersedes));

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Dry-run impact (task 084d-2b; notes/084d-dry-run-design.md §4): selection only. Nothing here closes, writes or asks an owner: the
    // close's selection and the removal preview's selection are the write's own, and the count is AffectedInvoiceCounter's.
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Task 084d-2b: what <paramref name="plan"/> would close if saved now, and how many open invoices the caller can see are affected
    /// (findings closed). A changed-rule removal (D-170) selects as <see cref="PreviewRemovalAsync"/> does
    /// (<see cref="SignalClosureService.SelectOpenSignalsOfPolicyAsync"/>) and carries <see cref="RemoveChangedWarning"/>. A change selects
    /// through <see cref="SelectBaseFindingsAsync"/>, the write's own selection, over <paramref name="after"/> (the dry run's overlay), and carries
    /// the write's own message for that selection (<see cref="ChangeCloseCompleteMessage"/>, <see cref="ChangeCloseChildrenOpenMessage"/>, or
    /// <see cref="ChangeCloseIncompleteMessage"/> when the record's own invoices could not be read). The union of the selected findings is counted
    /// by <see cref="AffectedInvoiceCounter"/> unchanged; a selection that faults gives no count. <see cref="PlannedCloses.None"/> for a plan that
    /// closes nothing (a plain Exclude included: D-170 applies only when a rule is switched Off).
    /// </summary>
    internal async Task<PlannedCloses> PlanClosesAsync(
        WriteRefusingEntityService after, string coreTable, Guid recordId, ProcedureOverridePlan plan, string? callerBearerToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.SwitchedOffRuleId is null && plan.Supersedes is null)
        {
            return PlannedCloses.None;
        }

        var kinds = new List<string>();
        string? warning = null;
        string? message = null;
        var selected = new Dictionary<Guid, OpenPolicySignal>();
        var countable = true;
        if (plan.SwitchedOffRuleId is { } switchedOff)
        {
            kinds.Add(PlannedCloses.ChangedRuleRemoved);
            warning = RemoveChangedWarning;
            try
            {
                foreach (var signal in await _closure.SelectOpenSignalsOfPolicyAsync(switchedOff, withBillingSubject: true, ct))
                {
                    selected.TryAdd(signal.Signal.Id, signal);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                countable = false;
                _logger.LogWarning("Dry run: the open findings of a changed rule could not be selected ({ExceptionType}); no count is shown", ex.GetType().Name);
            }
        }

        if (plan.Supersedes is { } change)
        {
            kinds.Add(PlannedCloses.BaseRuleSuperseded);
            try
            {
                var selection = await SelectBaseFindingsAsync(after, coreTable, recordId, change.FirmId, change.BasePolicyId, change.InForceFrom, ct);
                message = selection.ChildrenFault is null ? ChangeCloseCompleteMessage : ChangeCloseChildrenOpenMessage;
                if (selection.ChildrenFault is not null)
                {
                    // F-1: the records under the record were not read (the bound, or a fault), so the selection is the record's own invoices only.
                    // The write closes just those, and says so (the message); a count of them would read as the whole. No number, never a partial one.
                    countable = false;
                    _logger.LogInformation("Dry run: the records under the record were not read ({ExceptionType}); no count is shown", selection.ChildrenFault.GetType().Name);
                }

                foreach (var signal in selection.Targets)
                {
                    selected.TryAdd(signal.Signal.Id, signal);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                countable = false;
                message = ChangeCloseIncompleteMessage;
                _logger.LogWarning("Dry run: the base rule's open findings could not be selected ({ExceptionType}); no count is shown", ex.GetType().Name);
            }
        }

        int? count = null;
        if (countable)
        {
            try
            {
                count = await _counter.CountVisibleOpenInvoicesAsync(selected.Values.ToList(), callerBearerToken, ct, AffectedInvoiceCounter.MaxDryRunInvoices);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Dry run: the affected-invoice count could not be made ({ExceptionType}); none is shown", ex.GetType().Name);
            }
        }

        return new PlannedCloses(kinds, warning, message, count);
    }

    /// <summary>
    /// Task 084d-2b: the In Review invoices in the reach of an override on <paramref name="coreTable"/>(<paramref name="recordId"/>): those filed on
    /// the record and on the records DIRECTLY under it (D-172: a grandchild's chain does not reach the record). App-only reads through
    /// <paramref name="reads"/>. More than <see cref="MaxChildRecords"/> records under it throws <see cref="CoreRecordChainUnreadableException"/>;
    /// more than one page of invoices, or any fault, throws. The dry run then shows no count, never a guess.
    /// </summary>
    /// <remarks>
    /// <b>No judgement here (fix round 1, F-2).</b> This is read before the caller's Read narrows the invoices, so it must not let an invoice the
    /// caller cannot Read change the answer: an invoice filed on other than exactly one core record (FR-05) or with no date is returned with
    /// <see cref="InvoiceInReach.Judgeable"/> false, not thrown on, and no lines are read (<see cref="JudgedOnAsync"/> reads them for the invoices
    /// that were kept). Known limit K-d: the page throw is on the total read, readable or not (see the notes).
    /// </remarks>
    internal async Task<IReadOnlyList<InvoiceInReach>> InReviewInvoicesInReachAsync(
        WriteRefusingEntityService reads, string coreTable, Guid recordId, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        ArgumentNullException.ThrowIfNull(reads);
        var records = new List<(string Table, Guid Id)> { (coreTable, recordId) };
        records.AddRange((await CoreRecordChain.ReadChildrenAsync(reads, _logger, coreTable, recordId, MaxChildRecords, ct)).Select(c => (c[0].Table, c[0].Id)));

        var lookups = InvoiceFamilyCoreRecordGuard.CoreRecordLookups[AffectedInvoiceCounter.InvoiceEntity];
        var columns = lookups.Select(l => l.Column).Append(InvoiceFirmColumn).Append(InvoiceAdjustmentService.InvoiceDateColumn).Append("createdon").ToArray();
        var result = new List<InvoiceInReach>();
        foreach (var group in records.GroupBy(r => r.Table))
        {
            var coreColumn = lookups.Single(l => l.CoreEntity == group.Key).Column;
            foreach (var batch in group.Chunk(MaxInValues))
            {
                var query = new QueryExpression(AffectedInvoiceCounter.InvoiceEntity) { ColumnSet = new ColumnSet(columns) };
                query.Criteria.AddCondition(coreColumn, ConditionOperator.In, batch.Select(r => (object)r.Id).ToArray());
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, InvoiceStatusCodes.StateActive);
                query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, InvoiceStatusCodes.InReview);
                var rows = await ReadAsync(reads, query, ct);
                if (rows.MoreRecords)
                {
                    throw new InvalidOperationException("More In Review invoices in the reach than one page.");
                }

                var ids = batch.Select(r => r.Id).ToHashSet();
                foreach (var row in rows.Entities)
                {
                    // FR-05: an invoice with more than one core lookup has no chain, so its resolution cannot be compared.
                    var filedOn = row.GetAttributeValue<EntityReference>(coreColumn)?.Id ?? Guid.Empty;
                    var invoiceDate = InvoiceAdjustmentService.InvoiceDateOf(row);
                    var judgeable = lookups.Count(l => row.GetAttributeValue<EntityReference>(l.Column) is not null) == 1
                        && ids.Contains(filedOn) && invoiceDate is not null;
                    result.Add(new InvoiceInReach(
                        row.Id, group.Key, filedOn, row.GetAttributeValue<EntityReference>(InvoiceFirmColumn)?.Id, judgeable, invoiceDate, Array.Empty<DateOnly>()));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Task 084d-2b fix round 1 (F-2): <paramref name="invoices"/> (the judgeable ones the caller can Read) with every date each is judged on: the
    /// invoice date, and each Fee or Expense line's work date, else the invoice date. App-only; one read of the lines of these invoices only, so
    /// the lines of an invoice the caller cannot Read are never read. A fault throws.
    /// </summary>
    internal async Task<IReadOnlyList<InvoiceInReach>> JudgedOnAsync(WriteRefusingEntityService reads, IReadOnlyList<InvoiceInReach> invoices)
    {
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(invoices);
        if (invoices.Count == 0)
        {
            return invoices;
        }

        var lines = (await LinesOfInvoicesAsync(reads, invoices.Select(i => i.InvoiceId).ToList())).ToLookup(l => l.InvoiceId);
        return invoices.Select(i => i with
        {
            JudgedOn = lines[i.InvoiceId]
                .Where(l => l.CostType is (int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense)
                .Select(l => l.Date ?? i.InvoiceDate!.Value)
                .Prepend(i.InvoiceDate!.Value)
                .Distinct()
                .Order()
                .ToList(),
        }).ToList();
    }

    /// <summary>Task 084d-2b: <see cref="AffectedInvoiceCounter.VisibleOpenInvoicesAsync"/>, the counter's own narrowing, for the dry run's count; more than <see cref="AffectedInvoiceCounter.MaxDryRunInvoices"/> invoices is null (K-5).</summary>
    internal Task<HashSet<Guid>?> VisibleOpenInvoicesAsync(IReadOnlyCollection<Guid> invoiceIds, string? callerBearerToken, CancellationToken ct) =>
        _counter.VisibleOpenInvoicesAsync(invoiceIds, callerBearerToken, ct, AffectedInvoiceCounter.MaxDryRunInvoices);

    /// <summary>A copy of <paramref name="row"/> (every column it was read with) with <paramref name="fields"/> applied: a real row's after-image.</summary>
    private static Entity Patched(Entity row, IReadOnlyDictionary<string, object> fields)
    {
        var after = new Entity(row.LogicalName, row.Id);
        foreach (var attribute in row.Attributes)
        {
            after[attribute.Key] = attribute.Value;
        }

        foreach (var (column, value) in fields)
        {
            after[column] = value;
        }

        return after;
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Reads
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The rule an Exclude or a change names, as read (the policy and its rule set).</summary>
    private sealed record BaseRule(
        Guid Id, string Name, string Code, Guid? FirmId, bool VisibleToFirms, Guid? CurrentVersionId, int? Priority, int? Lane, int? Severity,
        bool Enabled, bool SetPublished, bool ProcedureRetired);

    private static readonly string[] BaseVersionColumns =
    {
        "sprk_policy", "sprk_versionnumber", "sprk_ruletype", "sprk_rulebody", "sprk_messagetemplate", "sprk_shortheadline",
        "sprk_consequence", "sprk_proposalstatus", "sprk_clausereference", "sprk_clauselocation",
    };

    private static readonly string[] RecordSetColumns =
        new[] { "sprk_scope", "sprk_firm", "statecode", RuleDomains.PolicyDomainColumn, RuleSetService.ProcedureColumn }
            .Concat(InvoiceFamilyCoreRecordGuard.FilingCoreTables).ToArray();

    private static readonly string[] RecordRuleColumns =
        { "sprk_ruleset", "sprk_policycode", "sprk_enabled", "statecode", RuleDomains.PolicyDomainColumn, RuleSetService.RowVersionColumn };

    /// <summary>
    /// The rule an Exclude or a change names: an active Billing policy in a Billing rule set that is NOT record-scoped (no record scope and no
    /// core lookup: a record rule is removed, not overridden), and not the system rule (D-125; refused 409 before a rule set is required, as
    /// it has none). Read as the application; the route has already checked the caller's Read on the policy. Returns the rule, or the refusal.
    /// </summary>
    private async Task<(BaseRule? Rule, RuleSetOutcome<T>? Refusal)> LoadRuleAsync<T>(IGenericEntityService entities, Guid policyId, CancellationToken ct, bool readProcedure = false) where T : class
    {
        var query = new QueryExpression(RuleSetService.PolicyEntity)
        {
            ColumnSet = new ColumnSet(
                "sprk_name", "sprk_policycode", "sprk_ruleset", "sprk_currentversion", RuleDomains.PolicyDomainColumn,
                "sprk_priority", "sprk_lane", "sprk_severity", "sprk_enabled"),
            TopCount = 1,
        };
        query.Criteria.AddCondition("sprk_policyid", ConditionOperator.Equal, policyId);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        RuleDomains.AddBillingFilter(query);
        var policy = (await ReadAsync(entities, query, ct)).Entities.FirstOrDefault(p => p.Id == policyId && RuleDomains.IsBillingRow(p));
        // The system rule first, BEFORE a rule set is required: it belongs to no rule set (Seed-BillingSystemPolicy.ps1), so the set check
        // below would answer it "not found". The caller's Read on the rule has already been checked by the route.
        if (policy is not null && await IsSystemRuleAsync(entities, policy, policyId, ct))
        {
            return (null, Refused<T>(StatusCodes.Status409Conflict, CodeSystemRule, "This is a system rule; it cannot be excluded or changed for a record."));
        }

        var set = policy?.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id is { } setId
            ? await LoadByIdAsync(entities, RuleSetService.RuleSetEntity,
                setId, new[] { "sprk_scope", "sprk_firm", "sprk_visibletofirms", "statuscode", RuleDomains.PolicyDomainColumn, RuleSetService.ProcedureColumn }
                    .Concat(InvoiceFamilyCoreRecordGuard.FilingCoreTables).ToArray(), ct)
            : null;
        if (policy is null || set is null || !RuleDomains.IsBillingRow(set) || RecordRuleSet.IsRecordScoped(set))
        {
            return (null, Refused<T>(StatusCodes.Status404NotFound, CodeRuleNotFound, "The rule was not found."));
        }

        // The change reads the owning Procedure's status too, by the constant Assign refuses a retired Procedure with. A read fault here throws
        // ReadFailedException like every other read, answered 503 by the public method.
        var procedureRetired = false;
        if (readProcedure && set.GetAttributeValue<EntityReference>(RuleSetService.ProcedureColumn)?.Id is { } owningProcedureId)
        {
            var owner = await LoadByIdAsync(entities, ProcedureEntity.Entity, owningProcedureId, new[] { ProcedureEntity.StatusColumn }, ct);
            procedureRetired = owner?.GetAttributeValue<OptionSetValue>(ProcedureEntity.StatusColumn)?.Value == ProcedureEntity.StatusRetired;
        }

        var code = policy.GetAttributeValue<string>("sprk_policycode") ?? policyId.ToString();
        return (new BaseRule(
            policyId,
            policy.GetAttributeValue<string>("sprk_name") ?? code,
            code,
            set.GetAttributeValue<EntityReference>("sprk_firm")?.Id,
            set.GetAttributeValue<bool?>("sprk_visibletofirms") ?? false,
            policy.GetAttributeValue<EntityReference>("sprk_currentversion")?.Id,
            policy.GetAttributeValue<int?>("sprk_priority"),
            policy.GetAttributeValue<OptionSetValue>("sprk_lane")?.Value,
            policy.GetAttributeValue<OptionSetValue>("sprk_severity")?.Value,
            policy.GetAttributeValue<bool?>("sprk_enabled") == true,
            set.GetAttributeValue<OptionSetValue>("statuscode")?.Value == RuleSetService.RuleSetPublished,
            procedureRetired), null);
    }

    /// <summary>A policy version row as the rule-set service's validator reads it.</summary>
    private static RuleSetService.VersionRow VersionOf(Entity v) => new(
        v.Id,
        v.GetAttributeValue<int?>("sprk_versionnumber") ?? 0,
        v.GetAttributeValue<OptionSetValue>("sprk_ruletype")?.Value,
        v.GetAttributeValue<string>("sprk_rulebody"),
        v.GetAttributeValue<string>("sprk_messagetemplate"),
        v.GetAttributeValue<string>("sprk_shortheadline"),
        v.GetAttributeValue<OptionSetValue>("sprk_consequence")?.Value,
        v.GetAttributeValue<OptionSetValue>("sprk_proposalstatus")?.Value,
        v.GetAttributeValue<string>("sprk_clausereference"),
        v.GetAttributeValue<string>("sprk_clauselocation"),
        null,
        null);

    /// <summary>
    /// True for the system rule (D-125): the policy that carries <see cref="BillingRuleCatalogue.SystemPolicyCode"/> (as
    /// <c>InvoiceSubmissionAfterSteps</c> finds it), or one whose version in force is of the SubmissionIntegrity type (as
    /// <c>RuleSetService</c> refuses it). The system policy has no rule set, so this cannot wait for one.
    /// </summary>
    private async Task<bool> IsSystemRuleAsync(IGenericEntityService entities, Entity policy, Guid policyId, CancellationToken ct)
    {
        if (string.Equals(policy.GetAttributeValue<string>("sprk_policycode")?.Trim(), BillingRuleCatalogue.SystemPolicyCode, StringComparison.Ordinal))
        {
            return true;
        }

        return policy.GetAttributeValue<EntityReference>("sprk_currentversion")?.Id is { } currentId
            && await LoadByIdAsync(entities, RuleSetService.VersionEntity, currentId, new[] { "sprk_policy", "sprk_ruletype" }, ct) is { } current
            && current.GetAttributeValue<EntityReference>("sprk_policy")?.Id == policyId
            && current.GetAttributeValue<OptionSetValue>("sprk_ruletype")?.Value == (int)RuleType.SubmissionIntegrity;
    }

    /// <summary>True when the record already holds an override row naming <paramref name="subjectId"/> in <paramref name="subjectColumn"/> (I-A1).</summary>
    private async Task<bool> HasRowAsync(IGenericEntityService entities, string coreTable, Guid recordId, string subjectColumn, Guid subjectId, CancellationToken ct)
    {
        var query = OwnRowQuery(coreTable, recordId, subjectColumn, new[] { subjectId });
        query.TopCount = 1;
        return (await ReadAsync(entities, query, ct)).Entities.Count > 0;
    }

    /// <summary>
    /// The record's own override rows naming any of <paramref name="subjectIds"/> in <paramref name="subjectColumn"/>: the record lookup and the
    /// subject, with NO state filter (an inactive row still holds the derived id, so the write would collide on it). One predicate for the
    /// duplicate refusal (<see cref="HasRowAsync"/>, one id) and the assignable list's <c>OwnRow</c> (task 084d-5a). A null
    /// <paramref name="subjectIds"/> asks for every row of the record, whatever it names (the assignable list's own-row set, uac-r2 T1).
    /// </summary>
    internal static QueryExpression OwnRowQuery(string coreTable, Guid recordId, string subjectColumn, IReadOnlyCollection<Guid>? subjectIds)
    {
        var query = new QueryExpression(ProcedureAssignmentEntity.Entity) { ColumnSet = new ColumnSet(subjectColumn) };
        query.Criteria.AddCondition(coreTable, ConditionOperator.Equal, recordId);
        if (subjectIds is null)
        {
            // No subject condition: the record's own rows, whichever Procedure they name.
        }
        else if (subjectIds.Count == 1)
        {
            query.Criteria.AddCondition(subjectColumn, ConditionOperator.Equal, subjectIds.First());
        }
        else
        {
            query.Criteria.AddCondition(subjectColumn, ConditionOperator.In, subjectIds.Cast<object>().ToArray());
        }

        return query;
    }

    /// <summary>A Dataverse read before the write failed (a fault or a timeout); thrown by <see cref="ReadAsync"/>, answered by the public methods as 503.</summary>
    private sealed class ReadFailedException(string entity, Exception inner)
        : Exception($"Reading {entity} failed ({inner.GetType().Name}).", inner)
    {
        public string Entity { get; } = entity;
    }

    /// <summary>
    /// Every read before the write goes through here. A fault becomes <see cref="ReadFailedException"/> (stable 503, ADR-019) instead of an
    /// unhandled 500; the caller's own cancellation still propagates. The transaction write is not read here: its fault mapping is
    /// <see cref="SendAsync"/>.
    /// </summary>
    private Task<EntityCollection> ReadAsync(QueryExpression query, CancellationToken ct) => ReadAsync(_entities, query, ct);

    /// <summary><see cref="ReadAsync(QueryExpression, CancellationToken)"/> through <paramref name="entities"/> (the write's service, or a dry run's planning service).</summary>
    private static async Task<EntityCollection> ReadAsync(IGenericEntityService entities, QueryExpression query, CancellationToken ct)
    {
        try
        {
            return await entities.RetrieveMultipleAsync(query, ct);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            throw new ReadFailedException(query.EntityName, ex);
        }
    }

    private RuleSetOutcome<T> ReadFailed<T>(ReadFailedException ex, string what) where T : class
    {
        _logger.LogError(ex.InnerException, "Override: the {What} could not read {Entity} ({ExceptionType}); nothing was written",
            what, ex.Entity, ex.InnerException?.GetType().Name);
        return Refused<T>(StatusCodes.Status503ServiceUnavailable, CodeReadFailed,
            "The override could not be checked right now and nothing was written. Try again; if it keeps failing, contact support.");
    }

    private Task<Entity?> LoadByIdAsync(string entity, Guid id, string[] columns, CancellationToken ct) =>
        LoadByIdAsync(_entities, entity, id, new ColumnSet(columns), ct);

    private static Task<Entity?> LoadByIdAsync(IGenericEntityService entities, string entity, Guid id, string[] columns, CancellationToken ct) =>
        LoadByIdAsync(entities, entity, id, new ColumnSet(columns), ct);

    private static async Task<Entity?> LoadByIdAsync(IGenericEntityService entities, string entity, Guid id, ColumnSet columns, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        var query = new QueryExpression(entity) { ColumnSet = columns, TopCount = 1 };
        query.Criteria.AddCondition(entity + "id", ConditionOperator.Equal, id);
        return (await ReadAsync(entities, query, ct)).Entities.FirstOrDefault(r => r.Id == id);
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // The write
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The columns of one override row (a Procedure-level row names the Procedure; a rule-level row names the rule, never both).</summary>
    private sealed record AssignmentRow(
        Guid Id, string CoreTable, Guid RecordId, string Name, int Mode, Guid? ProcedureId, Guid? PinnedVersionId, Guid? ExcludedPolicyId,
        Guid? FirmId, string? Reason);

    /// <summary>
    /// Creates one override row in its own transaction (assign, exclude rule). Returns null when the row was created, else the refusal. A
    /// refused owner writes nothing.
    /// </summary>
    private async Task<RuleSetOutcome<OverrideView>?> CreateRowAsync(AssignmentRow spec, Guid? callerObjectId, string what, CancellationToken ct)
    {
        var create = await AssignmentCreateAsync(spec, callerObjectId, imageOnly: false, ct);
        if (create.Refused is { } owner)
        {
            _logger.LogWarning("Override {AssignmentId}: the {What} was refused, no owner ({RefusalCode}); nothing was written",
                spec.Id, what, owner.RefusalCode);
            return RuleSetOutcome<OverrideView>.OwnerRefused(owner);
        }

        var sent = await SendAsync(new OrganizationRequest[] { create.Request! }, spec.Id, what);
        return sent == SendResult.Done ? null : WriteRefused<OverrideView>(sent, spec.Id);
    }

    /// <summary>
    /// A built create, owner applied, or the owner refusal (nothing was built to write). 084d-1: with <c>imageOnly</c> the builders return the
    /// row as built (<see cref="Image"/>), before the owner question, and nothing else.
    /// </summary>
    private sealed record BuiltCreate(CreateRequest? Request, RecordOwnerResolution? Refused, Entity? Image = null)
    {
        public static BuiltCreate ImageOf(Entity row) => new(null, null, row);
    }

    /// <summary>
    /// Builds the override row, asks its owner (the row's own question, with the caller as RequestedBy), writes the owner onto it and wraps
    /// it in its create, in one member, so the census sees the row, its owner write and its create together. The caller sends it.
    /// </summary>
    private async Task<BuiltCreate> AssignmentCreateAsync(AssignmentRow spec, Guid? callerObjectId, bool imageOnly, CancellationToken ct)
    {
        var row = new Entity(AssignmentTable, spec.Id)
        {
            [ProcedureAssignmentEntity.NameColumn] = spec.Name,
            [ProcedureAssignmentEntity.ModeColumn] = new OptionSetValue(spec.Mode),
            [spec.CoreTable] = new EntityReference(spec.CoreTable, spec.RecordId),
        };
        if (spec.ProcedureId is { } procedureId)
        {
            row[ProcedureAssignmentEntity.ProcedureColumn] = new EntityReference(ProcedureEntity.Entity, procedureId);
        }

        if (spec.PinnedVersionId is { } versionId)
        {
            row[ProcedureAssignmentEntity.PinnedVersionColumn] = new EntityReference(ProcedureEntity.VersionEntity, versionId);
        }

        if (spec.ExcludedPolicyId is { } policyId)
        {
            row[ProcedureAssignmentEntity.ExcludedPolicyColumn] = new EntityReference(RuleSetService.PolicyEntity, policyId);
        }

        if (spec.FirmId is { } firmId)
        {
            row[ProcedureAssignmentEntity.FirmColumn] = new EntityReference(RuleSetService.FirmEntity, firmId);
        }

        if (spec.Reason is not null)
        {
            row[ProcedureAssignmentEntity.ReasonColumn] = spec.Reason;
        }

        // 084d-1: a dry run takes the row as built, before the owner question (it asks no owner and writes nothing).
        if (imageOnly)
        {
            return BuiltCreate.ImageOf(row);
        }
        // EB-D-168: the row's own question — its table (a hard-deny table) and the lookups it holds; the citations are dropped, so the core
        // record is the one parent. The caller is RequestedBy, so ApplyTo stamps them as the creator person.
        var owner = await _ownership.ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(row) with
            {
                CallerObjectId = callerObjectId,
                RequestedBy = RecordRequester.Of(objectId: callerObjectId),
            },
            ct);
        if (!owner.IsOwned)
        {
            return new BuiltCreate(null, owner);
        }

        owner.ApplyTo(row);
        return new BuiltCreate(new CreateRequest { Target = row }, null);
    }

    /// <summary>
    /// Builds the record's record-scoped set (stage 1 of a change): Billing, Published, the record scope (Firm record with the firm, else
    /// Record), the core-record lookup, and the clause-visibility flag of the rule it first takes. Checked by
    /// <see cref="RecordRuleSet.RequireOneHome"/>, and owned by its own question (ForChild: the core record is its one parent; the firm is no
    /// ownership parent) with the caller as RequestedBy. The caller sends it.
    /// </summary>
    private async Task<BuiltCreate> RecordRuleSetCreateAsync(
        Guid setId, string coreTable, Guid recordId, Guid? firmId, bool visibleToFirms, Guid? callerObjectId, bool imageOnly, CancellationToken ct)
    {
        var set = new Entity(RuleSetTable, setId)
        {
            ["sprk_name"] = RecordRuleSet.Name,
            [RuleDomains.PolicyDomainColumn] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
            ["sprk_scope"] = new OptionSetValue(firmId is null ? RecordRuleSet.ScopeRecord : RecordRuleSet.ScopeFirmRecord),
            [coreTable] = new EntityReference(coreTable, recordId),
            ["sprk_visibletofirms"] = visibleToFirms,
            ["sprk_effectivefrom"] = DateTime.UtcNow.Date,
            ["statecode"] = new OptionSetValue(0),
            ["statuscode"] = new OptionSetValue(RuleSetService.RuleSetPublished),
        };
        if (firmId is { } firm)
        {
            set["sprk_firm"] = new EntityReference(RuleSetService.FirmEntity, firm);
        }

        RecordRuleSet.RequireOneHome(set);
        // 084d-1: a dry run takes the row as built, before the owner question (it asks no owner and writes nothing).
        if (imageOnly)
        {
            return BuiltCreate.ImageOf(set);
        }
        var owner = await _ownership.ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(set) with
            {
                CallerObjectId = callerObjectId,
                RequestedBy = RecordRequester.Of(objectId: callerObjectId),
            },
            ct);
        if (!owner.IsOwned)
        {
            return new BuiltCreate(null, owner);
        }

        owner.ApplyTo(set);
        return new BuiltCreate(new CreateRequest { Target = set }, null);
    }

    /// <summary>
    /// Builds the record rule's policy (stage 2 of a change): Off and with no version (stage 3 puts one in force), the derived code, the base
    /// rule's name, priority, lane and severity, Billing, in the record's set. Owned by its own question (ForChild: the set is its parent)
    /// with the caller as RequestedBy. The caller sends it.
    /// </summary>
    private async Task<BuiltCreate> RecordRuleCreateAsync(Guid policyId, Guid setId, BaseRule baseRule, Guid? callerObjectId, bool imageOnly, CancellationToken ct)
    {
        var policy = new Entity(PolicyTable, policyId)
        {
            ["sprk_name"] = baseRule.Name,
            ["sprk_policycode"] = RecordRuleCode(policyId),
            ["sprk_enabled"] = false,
            ["sprk_priority"] = baseRule.Priority ?? DefaultPriority,
            ["sprk_lane"] = new OptionSetValue(baseRule.Lane ?? (int)DecisionLane.Decide),
            [RuleDomains.PolicyDomainColumn] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
            ["sprk_ruleset"] = new EntityReference(RuleSetTable, setId),
        };
        if (baseRule.Severity is { } severity)
        {
            policy["sprk_severity"] = new OptionSetValue(severity);
        }

        // 084d-1: a dry run takes the row as built, before the owner question (it asks no owner and writes nothing).
        if (imageOnly)
        {
            return BuiltCreate.ImageOf(policy);
        }
        var owner = await _ownership.ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(policy) with
            {
                CallerObjectId = callerObjectId,
                RequestedBy = RecordRequester.Of(objectId: callerObjectId),
            },
            ct);
        if (!owner.IsOwned)
        {
            return new BuiltCreate(null, owner);
        }

        owner.ApplyTo(policy);
        return new BuiltCreate(new CreateRequest { Target = policy }, null);
    }

    /// <summary>
    /// Builds the record rule's next version (stage 3 of a change): the edited copy of the base rule's version in force, Accepted and
    /// Published, in force from <paramref name="inForceFrom"/> (the instant the change's close dates by), with <c>sprk_basedonversion</c> naming that version. Owned by its own question (ForChild: the record
    /// rule is its parent; the base version is no ownership parent) with the caller as RequestedBy. The caller sends it.
    /// </summary>
    private async Task<BuiltCreate> RecordRuleVersionCreateAsync(
        Guid versionId, Guid policyId, string code, int number, RuleSetService.VersionRow content, Guid basedOnVersionId, DateTime inForceFrom,
        Guid? callerObjectId, bool imageOnly, CancellationToken ct)
    {
        var version = new Entity(VersionTable, versionId)
        {
            ["sprk_name"] = $"{code} v{number.ToString(CultureInfo.InvariantCulture)}",
            ["sprk_policy"] = new EntityReference(PolicyTable, policyId),
            ["sprk_versionnumber"] = number,
            ["sprk_ruletype"] = new OptionSetValue(content.RuleType!.Value),
            ["sprk_rulebody"] = content.RuleBody!,
            ["sprk_messagetemplate"] = content.Template!,
            ["sprk_shortheadline"] = content.Headline!,
            ["sprk_consequence"] = new OptionSetValue(content.Consequence!.Value),
            ["sprk_proposedaction"] = RuleSetService.ProposedActionText(content.Consequence),
            ["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Accepted),
            ["sprk_inforcefrom"] = inForceFrom,
            [RuleSetService.PublishStateColumn] = new OptionSetValue(RuleSetService.PublishStatePublished),
            [BasedOnVersionColumn] = new EntityReference(VersionTable, basedOnVersionId),
        };
        if (content.ClauseReference is not null)
        {
            version["sprk_clausereference"] = content.ClauseReference;
        }

        if (content.ClauseLocation is not null)
        {
            version["sprk_clauselocation"] = content.ClauseLocation;
        }

        // 084d-1: a dry run takes the row as built, before the owner question (it asks no owner and writes nothing).
        if (imageOnly)
        {
            return BuiltCreate.ImageOf(version);
        }
        var owner = await _ownership.ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(version) with
            {
                CallerObjectId = callerObjectId,
                RequestedBy = RecordRequester.Of(objectId: callerObjectId),
            },
            ct);
        if (!owner.IsOwned)
        {
            return new BuiltCreate(null, owner);
        }

        owner.ApplyTo(version);
        return new BuiltCreate(new CreateRequest { Target = version }, null);
    }

    /// <summary>An update of <paramref name="row"/> conditional on its row version as read; null when the row was read without one.</summary>
    private static UpdateRequest? ConditionalUpdate(string entity, Entity row, IReadOnlyDictionary<string, object> fields)
    {
        if (row.GetAttributeValue<long?>(RuleSetService.RowVersionColumn) is not { } rowVersion)
        {
            return null;
        }

        var target = new Entity(entity, row.Id) { RowVersion = rowVersion.ToString(CultureInfo.InvariantCulture) };
        foreach (var (column, value) in fields)
        {
            target[column] = value;
        }

        return new UpdateRequest { Target = target, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches };
    }

    /// <summary>The refusal of a write that did not commit, by what the transaction answered.</summary>
    private static RuleSetOutcome<T> WriteRefused<T>(SendResult sent, Guid id) where T : class => sent switch
    {
        SendResult.Duplicate => Duplicate<T>(),
        SendResult.Conflict => Refused<T>(StatusCodes.Status409Conflict, CodeConcurrentEdit,
            "The record's overrides changed while this was being saved, so nothing was written. Reload and try again."),
        // Never "nothing was saved" here: a retry could find its own row. The id lets the caller look before retrying.
        SendResult.Unknown => Refused<T>(StatusCodes.Status500InternalServerError, CodeWriteFailed,
            $"The override may or may not have been saved (id {id}). Reload the record before trying again."),
        _ => Refused<T>(StatusCodes.Status500InternalServerError, CodeWriteFailed,
            "The override could not be saved and nothing was written. Try again; if it keeps failing, contact support."),
    };

    /// <summary>A change's stage 1 or 2 did not complete. Nothing is in force yet, so how the record's invoices are checked has not changed.</summary>
    private static RuleSetOutcome<T> StageFailed<T>() where T : class =>
        Refused<T>(StatusCodes.Status500InternalServerError, CodeWriteFailed,
            "The rule could not be changed, and how this record's invoices are checked has not changed. Try again; if it keeps failing, contact support.");

    private static RuleSetOutcome<T> RecordRuleConflict<T>() where T : class =>
        Refused<T>(StatusCodes.Status409Conflict, CodeRecordRuleConflict,
            "This record's rule data is not in the expected state, so the rule was not changed. Contact support.");

    // Also the route's answer for an override whose subject the caller cannot Read (089c review K1): the two must not differ.
    internal static RuleSetOutcome<T> OverrideNotFound<T>() where T : class =>
        Refused<T>(StatusCodes.Status404NotFound, CodeOverrideNotFound, "The override was not found on this record.");

    private enum SendResult
    {
        Done,
        Conflict,
        Duplicate,
        Gone,
        Failed,
        Unknown,
    }

    /// <summary>One transaction, all or nothing, with <see cref="CancellationToken.None"/>: a caller who disconnects never leaves half a change.</summary>
    private async Task<SendResult> SendAsync(IReadOnlyList<OrganizationRequest> requests, Guid assignmentId, string what)
    {
        try
        {
            await _entities.ExecuteTransactionAsync(requests, CancellationToken.None);
            return SendResult.Done;
        }
        catch (Exception ex) when (HasFaultCode(ex, DuplicatePrimaryKey) || HasFaultCode(ex, DuplicateAlternateKey))
        {
            _logger.LogWarning("Override {AssignmentId}: the {What} was refused; a row with its derived id or key exists. Nothing was written", assignmentId, what);
            return SendResult.Duplicate;
        }
        catch (Exception ex) when (HasFaultCode(ex, DoesNotExist))
        {
            _logger.LogWarning("Override {AssignmentId}: the {What} was refused; a row it names no longer exists. Nothing was written", assignmentId, what);
            return SendResult.Gone;
        }
        catch (Exception ex) when (ex is DBConcurrencyException || SignalWriter.IsConcurrencyVersionMismatch(ex))
        {
            _logger.LogWarning("Override {AssignmentId}: the {What} was refused; a row changed after it was read. Nothing was written", assignmentId, what);
            return SendResult.Conflict;
        }
        catch (DataverseTransactionException ex) when (ex.OutcomeUnknown)
        {
            _logger.LogError(ex, "Override {AssignmentId}: the {What} transaction returned no answer; it may have committed", assignmentId, what);
            return SendResult.Unknown;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Override {AssignmentId}: the {What} transaction timed out; it may have committed", assignmentId, what);
            return SendResult.Unknown;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Override {AssignmentId}: the {What} failed ({ExceptionType}); nothing was written", assignmentId, what, ex.GetType().Name);
            return SendResult.Failed;
        }
    }

    // Dataverse fault codes: "Cannot insert duplicate key", a matching alternate key (sprk_policy's code key), "Does Not Exist".
    private const int DuplicatePrimaryKey = unchecked((int)0x80040237);
    private const int DuplicateAlternateKey = unchecked((int)0x80060892);
    private const int DoesNotExist = unchecked((int)0x80040217);

    /// <summary>The Dataverse fault <paramref name="code"/> anywhere in the chain, as <c>TrackingNumberAllocator</c> reads it.</summary>
    private static bool HasFaultCode(Exception ex, int code)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.ServiceModel.FaultException<OrganizationServiceFault> fault && fault.Detail?.ErrorCode == code)
            {
                return true;
            }
        }

        return false;
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The core table is the route's constant from <see cref="InvoiceFamilyCoreRecordGuard.FilingCoreTables"/>; anything else is a bug.</summary>
    private static void RequireCore(string coreTable, Guid recordId)
    {
        if (!InvoiceFamilyCoreRecordGuard.FilingCoreTables.Contains(coreTable, StringComparer.Ordinal))
        {
            throw new ArgumentException($"'{coreTable}' is not a core-record type.", nameof(coreTable));
        }

        if (recordId == Guid.Empty)
        {
            throw new ArgumentException("The record id is empty.", nameof(recordId));
        }
    }

    /// <summary>The id of the one override row of (record, subject): I-A1's second guard, after the read.</summary>
    internal static Guid AssignmentIdFor(string coreTable, Guid recordId, string subjectColumn, Guid subjectId) =>
        DerivedId($"sprk_procedureassignment:{coreTable}:{recordId:D}:{subjectColumn}:{subjectId:D}");

    /// <summary>The id of the record's record-scoped set: one per (core table, record, firm or none).</summary>
    internal static Guid RecordRuleSetIdFor(string coreTable, Guid recordId, Guid? firmId) =>
        DerivedId($"sprk_ruleset:record:{coreTable}:{recordId:D}:{(firmId is { } f ? f.ToString("D") : "all-firms")}");

    /// <summary>The id of the record rule of <paramref name="basePolicyId"/> in the record's set (POML 089 D-202: <c>IdOf("ebilling-record-rule-{set:D}-{base:D}")</c>).</summary>
    internal static Guid RecordRulePolicyIdFor(Guid recordRuleSetId, Guid basePolicyId) =>
        InvoiceSubmissionPersister.IdOf($"ebilling-record-rule-{recordRuleSetId:D}-{basePolicyId:D}");

    /// <summary>The record rule's code: <c>BRR-</c> and its id as 32 lower-case hex digits (36 characters; unique by <c>sprk_policycodekey</c>).</summary>
    internal static string RecordRuleCode(Guid recordRulePolicyId) => RecordRuleCodePrefix + recordRulePolicyId.ToString("N");

    // The entity sets the route's Read before a removal is asked on (the same sets the assign and exclude routes check).
    private const string ProcedureEntitySet = "sprk_procedures";
    private const string PolicyEntitySet = "sprk_policies";

    /// <summary>A version-5 UUID (SHA-1) of <paramref name="name"/> in <see cref="RowIdNamespace"/>, as <see cref="RuleSetService.VersionIdFor"/>.</summary>
    private static Guid DerivedId(string name)
    {
        var text = System.Text.Encoding.UTF8.GetBytes(name);
        var input = new byte[16 + text.Length];
        RowIdNamespace.ToByteArray(bigEndian: true).CopyTo(input, 0);
        text.CopyTo(input, 16);

        var bytes = System.Security.Cryptography.SHA1.HashData(input)[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    internal static bool TryParseMode(string? text, out int mode)
    {
        mode = text?.Trim().ToLowerInvariant() switch
        {
            "followlatest" => ProcedureAssignmentEntity.ModeFollowLatest,
            "pinned" => ProcedureAssignmentEntity.ModePinned,
            "excluded" => ProcedureAssignmentEntity.ModeExcluded,
            _ => 0,
        };
        return mode != 0;
    }

    internal static string ModeName(int mode) => mode switch
    {
        ProcedureAssignmentEntity.ModePinned => "Pinned",
        ProcedureAssignmentEntity.ModeExcluded => "Excluded",
        _ => "FollowLatest",
    };

    private static string ModeLabel(int mode) => mode switch
    {
        ProcedureAssignmentEntity.ModePinned => "Pinned",
        ProcedureAssignmentEntity.ModeExcluded => "Excluded",
        _ => "Follow latest",
    };

    private static void CheckReason(List<RuleSetError> errors, string? reason, bool required)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (required && trimmed.Length == 0)
        {
            errors.Add(Error("reason", "A reason is required."));
        }
        else if (trimmed.Length > MaxReasonChars)
        {
            errors.Add(Error("reason", $"reason may be at most {MaxReasonChars} characters."));
        }
    }

    private static string? Trimmed(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>"{subject} ({what})", the subject shortened so the whole fits the 200-character name column.</summary>
    internal static string Name(string? subject, string what)
    {
        var suffix = $" ({what})";
        var name = subject ?? string.Empty;
        return (name.Length + suffix.Length > MaxNameChars ? name[..(MaxNameChars - suffix.Length)].TrimEnd() : name) + suffix;
    }

    private static RuleSetError Error(string field, string message) => new(null, null, field, message, null);

    private static RuleSetOutcome<T> Duplicate<T>() where T : class =>
        Refused<T>(StatusCodes.Status409Conflict, CodeDuplicate, DuplicateDetail);

    private static RuleSetOutcome<T> Refused<T>(int status, string code, string detail) where T : class =>
        RuleSetOutcome<T>.Refused(status, code, detail);
}
