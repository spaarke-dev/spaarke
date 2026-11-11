using System.Data;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
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
            return RuleSetOutcome<OverrideView>.Invalid(CodeInvalid, errors);
        }

        var procedure = await LoadByIdAsync(ProcedureEntity.Entity, procedureId,
            new[] { ProcedureEntity.NameColumn, ProcedureEntity.StatusColumn }, ct);
        if (procedure is null)
        {
            return Refused<OverrideView>(StatusCodes.Status404NotFound, CodeProcedureNotFound, "The Procedure was not found.");
        }

        if (procedure.GetAttributeValue<OptionSetValue>(ProcedureEntity.StatusColumn)?.Value == ProcedureEntity.StatusRetired)
        {
            return Refused<OverrideView>(StatusCodes.Status409Conflict, CodeProcedureRetired, "A retired Procedure cannot be assigned, pinned or excluded.");
        }

        var pinned = mode == ProcedureAssignmentEntity.ModePinned ? request.PinnedVersionId : null;
        if (pinned is { } versionId)
        {
            var version = await LoadByIdAsync(ProcedureEntity.VersionEntity, versionId,
                new[] { ProcedureEntity.VersionProcedureColumn, ProcedureEntity.VersionStatusColumn }, ct);
            var status = version?.GetAttributeValue<OptionSetValue>(ProcedureEntity.VersionStatusColumn)?.Value;
            if (version?.GetAttributeValue<EntityReference>(ProcedureEntity.VersionProcedureColumn)?.Id != procedureId
                || status is not (ProcedureEntity.VersionStatusActive or ProcedureEntity.VersionStatusSuperseded))
            {
                return RuleSetOutcome<OverrideView>.Invalid(CodeInvalid, new[]
                {
                    Error("pinnedVersionId", "Pin a version of this Procedure that has been in force (Active or Superseded)."),
                });
            }
        }

        if (await HasRowAsync(coreTable, recordId, ProcedureAssignmentEntity.ProcedureColumn, procedureId, ct))
        {
            return Duplicate<OverrideView>();
        }

        var reason = Trimmed(request.Reason);
        var row = new AssignmentRow(
            AssignmentIdFor(coreTable, recordId, ProcedureAssignmentEntity.ProcedureColumn, procedureId),
            coreTable, recordId, Name(procedure.GetAttributeValue<string>(ProcedureEntity.NameColumn), ModeLabel(mode)), mode,
            ProcedureId: procedureId, PinnedVersionId: pinned, ExcludedPolicyId: null, FirmId: null, Reason: reason);
        return await CreateRowAsync(row, callerObjectId, "assign", ct) is { } refusal
            ? refusal
            : RuleSetOutcome<OverrideView>.Done(new OverrideView(row.Id, KindProcedure, ModeName(mode), procedureId, pinned, null, null, reason));
    }

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

        var errors = new List<RuleSetError>();
        if (request.PolicyId is not { } policyId || policyId == Guid.Empty)
        {
            errors.Add(Error("policyId", "policyId is required."));
            policyId = Guid.Empty;
        }

        CheckReason(errors, request.Reason, required: true);
        if (request.FirmId is { } firm
            && (firm == Guid.Empty || await LoadByIdAsync(RuleSetService.FirmEntity, firm, new[] { FirmIdColumn }, ct) is null))
        {
            errors.Add(Error("firmId", "The firm was not found."));
        }

        if (errors.Count > 0)
        {
            return RuleSetOutcome<OverrideView>.Invalid(CodeInvalid, errors);
        }

        var (rule, refusal) = await LoadRuleAsync<OverrideView>(policyId, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        if (await HasRowAsync(coreTable, recordId, ProcedureAssignmentEntity.ExcludedPolicyColumn, policyId, ct))
        {
            return Duplicate<OverrideView>();
        }

        var reason = Trimmed(request.Reason);
        var row = new AssignmentRow(
            AssignmentIdFor(coreTable, recordId, ProcedureAssignmentEntity.ExcludedPolicyColumn, policyId),
            coreTable, recordId, Name(rule!.Name, "Excluded rule"), ProcedureAssignmentEntity.ModeExcluded,
            ProcedureId: null, PinnedVersionId: null, ExcludedPolicyId: policyId, FirmId: request.FirmId, Reason: reason);
        return await CreateRowAsync(row, callerObjectId, "exclude rule", ct) is { } failed
            ? failed
            : RuleSetOutcome<OverrideView>.Done(new OverrideView(
                row.Id, KindRuleExcluded, ModeName(ProcedureAssignmentEntity.ModeExcluded), null, null, policyId, request.FirmId, reason));
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
            && (firm == Guid.Empty || await LoadByIdAsync(RuleSetService.FirmEntity, firm, new[] { FirmIdColumn }, ct) is null))
        {
            errors.Add(Error("firmId", "The firm was not found."));
        }

        if (errors.Count > 0)
        {
            return RuleSetOutcome<RuleChangedView>.Invalid(CodeInvalid, errors);
        }

        var (rule, refusal) = await LoadRuleAsync<RuleChangedView>(policyId, ct, readProcedure: true);
        if (refusal is not null)
        {
            return refusal;
        }

        var baseRule = rule!;

        // A rule of one firm's set checks only that firm's invoices: a change of it for another firm (or for every firm) would widen it.
        if (baseRule.FirmId is { } ruleFirm && request.FirmId != ruleFirm)
        {
            return RuleSetOutcome<RuleChangedView>.Invalid(CodeInvalid, new[]
            {
                Error("firmId", "This rule applies to one firm's invoices only; a change of it names that firm."),
            });
        }

        // In force = switched on, in a Published set, with a version of its own (089c review F1): retiring a Procedure switches its rules off
        // (sprk_enabled=false) but leaves statecode and the current version, so the version alone would copy a retired rule.
        // A retired owning Procedure ends it whatever the rule's flags say (pass 2): the retire writes in chunks, so a failed later chunk leaves
        // the Procedure retired with some rules still enabled.
        var current = baseRule is { Enabled: true, SetPublished: true, ProcedureRetired: false } && baseRule.CurrentVersionId is { } currentId
            ? await LoadByIdAsync(VersionTable, currentId, BaseVersionColumns, ct)
            : null;
        if (current is null || current.GetAttributeValue<EntityReference>("sprk_policy")?.Id != policyId)
        {
            return Refused<RuleChangedView>(StatusCodes.Status409Conflict, CodeRuleNotInForce,
                "The rule is not in force (switched off, its rule set not Published, or no version in force), so there is nothing to change. Exclude it instead.");
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
            return RuleSetOutcome<RuleChangedView>.Invalid(CodeInvalid, invalid);
        }

        // I-A1: one row per (record, rule). A plain Exclude and a change of the same rule are the same row.
        if (await HasRowAsync(coreTable, recordId, ProcedureAssignmentEntity.ExcludedPolicyColumn, policyId, ct))
        {
            return Duplicate<RuleChangedView>();
        }

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
        var number = versions.Count == 0 ? 1 : versions.Max(v => v.GetAttributeValue<int?>("sprk_versionnumber") ?? 0) + 1;
        var versionId = RuleSetService.VersionIdFor(recordRule!.Id, number);
        var code = RecordRuleCode(recordRule.Id);
        var reason = Trimmed(request.Reason);
        var exclude = new AssignmentRow(
            AssignmentIdFor(coreTable, recordId, ProcedureAssignmentEntity.ExcludedPolicyColumn, policyId),
            coreTable, recordId, Name(baseRule.Name, "Changed rule"), ProcedureAssignmentEntity.ModeExcluded,
            ProcedureId: null, PinnedVersionId: null, ExcludedPolicyId: policyId, FirmId: request.FirmId, Reason: reason);

        var excludeCreate = await AssignmentCreateAsync(exclude, callerObjectId, ct);
        if (excludeCreate.Refused is { } excludeOwner)
        {
            return RuleSetOutcome<RuleChangedView>.OwnerRefused(excludeOwner);
        }

        // The one instant the record rule's version is put in force by: the close below compares work dates with its UTC day.
        var inForceFrom = Clock.GetUtcNow().UtcDateTime;
        var versionCreate = await RecordRuleVersionCreateAsync(versionId, recordRule.Id, code, number, merged, baseVersion.Id, inForceFrom, callerObjectId, ct);
        if (versionCreate.Refused is { } versionOwner)
        {
            return RuleSetOutcome<RuleChangedView>.OwnerRefused(versionOwner);
        }

        if (ConditionalUpdate(PolicyTable, recordRule, new Dictionary<string, object>
        {
            ["sprk_enabled"] = true,
            ["sprk_currentversion"] = new EntityReference(VersionTable, versionId),
        }) is not { } switchOn)
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
            // d0 first: a fault here closes nothing (the outer catch).
            var mine = await InvoicesTakingTheChangeAsync(await RecordsInReviewInvoicesAsync(coreTable, recordId, firmId), inForceFrom);

            // d1 next: a fault, the bound or an unexpected row drops this side only; the record's own invoices still close.
            var children = TakingTheChange.None;
            var childrenRead = true;
            try
            {
                children = await InvoicesTakingTheChangeAsync(await ChildrensInReviewInvoicesAsync(coreTable, recordId, firmId, basePolicyId), inForceFrom);
            }
            catch (Exception ex)
            {
                childrenRead = false;
                if (ex is CoreRecordChainUnreadableException { CandidateCount: { } candidates })
                {
                    _logger.LogWarning(
                        "Override {AssignmentId}: rule changed; more than {MaxChildRecords} core records may be filed directly under {CoreTable} {RecordId} ({ChildCount} counted before the read stopped); the invoices of the records under it stay open for the reviewer",
                        assignmentId, MaxChildRecords, coreTable, recordId, candidates);
                }
                else
                {
                    _logger.LogError(
                        "Override {AssignmentId}: rule changed; the records directly under {CoreTable} {RecordId} could not be read ({ExceptionType}); the invoices of the records under it stay open for the reviewer",
                        assignmentId, coreTable, recordId, ex.GetType().Name);
                }
            }

            var invoices = mine.Invoices.Union(children.Invoices).ToHashSet();
            var lineInvoices = mine.LineInvoices.Concat(children.LineInvoices).ToDictionary(l => l.Key, l => l.Value);
            var open = invoices.Count == 0
                ? Array.Empty<OpenPolicySignal>()
                : await _closure.SelectOpenSignalsOfPolicyOnSubjectsAsync(basePolicyId, invoices, lineInvoices.Keys.ToList(), CancellationToken.None);
            Guid? InvoiceOf(OpenPolicySignal s) =>
                s.RegardingInvoiceId is { } invoice && invoice != Guid.Empty ? invoice
                : s.RegardingInvoiceLineId is { } line && lineInvoices.TryGetValue(line, out var ofLine) ? ofLine
                : null;

            var targets = open.Where(s => InvoiceOf(s) is { } invoice && invoices.Contains(invoice)).ToList();
            var heldBack = mine.HeldBack + children.HeldBack;
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
                assignmentId, outcome.Closed, targets.Count, baseCode, coreTable, recordId, children.Invoices.Count, recordRuleVersionId, heldBack, outcome.IsComplete && childrenRead,
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
    private async Task<TakingTheChange> InvoicesTakingTheChangeAsync(IReadOnlyDictionary<Guid, DateOnly?> invoiceDates, DateOnly inForceFrom)
    {
        if (invoiceDates.Count == 0)
        {
            return TakingTheChange.None;
        }

        var lines = await LinesOfInvoicesAsync(invoiceDates.Keys.ToList());
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
    private async Task<Dictionary<Guid, DateOnly?>> ChildrensInReviewInvoicesAsync(string coreTable, Guid recordId, Guid? firmId, Guid basePolicyId)
    {
        var children = await CoreRecordChain.ReadChildrenAsync(_entities, _logger, coreTable, recordId, MaxChildRecords, CancellationToken.None);
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

                var rows = await ReadAsync(query, CancellationToken.None);
                if (rows.MoreRecords)
                {
                    throw new InvalidOperationException("More In Review invoices on the records under the record than one page.");
                }

                var byChild = rows.Entities
                    .Where(r => lookups.Count(l => r.GetAttributeValue<EntityReference>(l.Column) is not null) == 1)
                    .ToLookup(r => r.GetAttributeValue<EntityReference>(coreColumn)?.Id ?? Guid.Empty);
                foreach (var chain in batch.Where(c => byChild.Contains(c[0].Id)))
                {
                    var overrides = await ProcedureResolver.ReadOverridesAsync(_entities, chain, CancellationToken.None);
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
    private async Task<List<InvoiceLine>> LinesOfInvoicesAsync(IReadOnlyCollection<Guid> invoiceIds)
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

                var rows = await ReadAsync(query, CancellationToken.None);
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
    private async Task<Dictionary<Guid, DateOnly?>> RecordsInReviewInvoicesAsync(string coreTable, Guid recordId, Guid? firmId)
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

        var rows = await ReadAsync(query, CancellationToken.None);
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
            var create = await RecordRuleSetCreateAsync(setId, coreTable, recordId, firmId, visibleToFirms, callerObjectId, ct);
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
    /// and every version it has is based on a version of THIS rule. Otherwise the change is refused 409.
    /// </summary>
    private async Task<(Entity? Policy, IReadOnlyList<Entity> Versions, RuleSetOutcome<RuleChangedView>? Refusal)> EnsureRecordRuleAsync(
        Guid setId, BaseRule baseRule, Guid? callerObjectId, CancellationToken ct)
    {
        var none = Array.Empty<Entity>();
        var recordRuleId = RecordRulePolicyIdFor(setId, baseRule.Id);
        var policy = await LoadByIdAsync(PolicyTable, recordRuleId, RecordRuleColumns, ct);
        if (policy is null)
        {
            var create = await RecordRuleCreateAsync(recordRuleId, setId, baseRule, callerObjectId, ct);
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

        if (policy is null
            || policy.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id != setId
            || !string.Equals(policy.GetAttributeValue<string>("sprk_policycode"), RecordRuleCode(recordRuleId), StringComparison.Ordinal)
            || (policy.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0) != 0
            || !RuleDomains.IsBillingRow(policy))
        {
            return (null, none, RecordRuleConflict<RuleChangedView>());
        }

        var query = new QueryExpression(VersionTable) { ColumnSet = new ColumnSet("sprk_versionnumber", BasedOnVersionColumn) };
        query.Criteria.AddCondition("sprk_policy", ConditionOperator.Equal, recordRuleId);
        var versions = (await ReadAsync(query, ct)).Entities.ToList();
        var basedOn = versions.Select(v => v.GetAttributeValue<EntityReference>(BasedOnVersionColumn)?.Id).ToList();
        if (basedOn.Any(id => id is null))
        {
            return (null, none, RecordRuleConflict<RuleChangedView>());
        }

        var baseIds = basedOn.Select(id => (object)id!.Value).Distinct().ToArray();
        if (baseIds.Length > 0)
        {
            var bases = new QueryExpression(VersionTable) { ColumnSet = new ColumnSet("sprk_policy") };
            bases.Criteria.AddCondition("sprk_policyversionid", ConditionOperator.In, baseIds);
            var found = (await ReadAsync(bases, ct)).Entities;
            if (found.Count != baseIds.Length || found.Any(v => v.GetAttributeValue<EntityReference>("sprk_policy")?.Id != baseRule.Id))
            {
                return (null, none, RecordRuleConflict<RuleChangedView>());
            }
        }

        return (policy, versions, null);
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
        var row = await LoadOverrideAsync(coreTable, recordId, overrideId, ct);
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
            var row = await LoadOverrideAsync(coreTable, recordId, overrideId, ct);
            if (row is null)
            {
                return OverrideNotFound<OverrideRemovalPreview>();
            }

            var changed = await ChangedRuleOfAsync(coreTable, recordId, row, ct);
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
            row = await LoadOverrideAsync(coreTable, recordId, overrideId, ct);
            if (row is null)
            {
                return OverrideNotFound<OverrideRemoved>();
            }

            changed = await ChangedRuleOfAsync(coreTable, recordId, row, ct);
        }
        catch (ReadFailedException ex)
        {
            return ReadFailed<OverrideRemoved>(ex, "remove");
        }

        var requests = new List<OrganizationRequest> { new DeleteRequest { Target = new EntityReference(AssignmentTable, row.Id) } };
        if (changed is not null)
        {
            if (ConditionalUpdate(PolicyTable, changed, new Dictionary<string, object> { ["sprk_enabled"] = false }) is not { } switchOff)
            {
                return Refused<OverrideRemoved>(StatusCodes.Status500InternalServerError, CodeWriteFailed,
                    "The override could not be removed and nothing was changed. Try again; if it keeps failing, contact support.");
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
    private async Task<Entity?> LoadOverrideAsync(string coreTable, Guid recordId, Guid overrideId, CancellationToken ct)
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
        return (await ReadAsync(query, ct)).Entities.FirstOrDefault(
            r => r.Id == overrideId && r.GetAttributeValue<EntityReference>(coreTable)?.Id == recordId);
    }

    /// <summary>
    /// The record rule an Exclude row changed: the policy derived from (the record's set for the row's firm, the excluded rule). It counts only
    /// when it exists in that set, is active and is switched on. An Off record rule (removed earlier, or left by a failed change) changes
    /// nothing, so its row is a plain Exclude.
    /// </summary>
    private async Task<Entity?> ChangedRuleOfAsync(string coreTable, Guid recordId, Entity row, CancellationToken ct)
    {
        if (row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.ExcludedPolicyColumn)?.Id is not { } basePolicyId)
        {
            return null;
        }

        var setId = RecordRuleSetIdFor(coreTable, recordId, row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.FirmColumn)?.Id);
        var policy = await LoadByIdAsync(PolicyTable, RecordRulePolicyIdFor(setId, basePolicyId), RecordRuleColumns, ct);
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
    private async Task<(BaseRule? Rule, RuleSetOutcome<T>? Refusal)> LoadRuleAsync<T>(Guid policyId, CancellationToken ct, bool readProcedure = false) where T : class
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
        var policy = (await ReadAsync(query, ct)).Entities.FirstOrDefault(p => p.Id == policyId && RuleDomains.IsBillingRow(p));
        // The system rule first, BEFORE a rule set is required: it belongs to no rule set (Seed-BillingSystemPolicy.ps1), so the set check
        // below would answer it "not found". The caller's Read on the rule has already been checked by the route.
        if (policy is not null && await IsSystemRuleAsync(policy, policyId, ct))
        {
            return (null, Refused<T>(StatusCodes.Status409Conflict, CodeSystemRule, "This is a system rule; it cannot be excluded or changed for a record."));
        }

        var set = policy?.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id is { } setId
            ? await LoadByIdAsync(RuleSetService.RuleSetEntity,
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
            var owner = await LoadByIdAsync(ProcedureEntity.Entity, owningProcedureId, new[] { ProcedureEntity.StatusColumn }, ct);
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
    private async Task<bool> IsSystemRuleAsync(Entity policy, Guid policyId, CancellationToken ct)
    {
        if (string.Equals(policy.GetAttributeValue<string>("sprk_policycode")?.Trim(), BillingRuleCatalogue.SystemPolicyCode, StringComparison.Ordinal))
        {
            return true;
        }

        return policy.GetAttributeValue<EntityReference>("sprk_currentversion")?.Id is { } currentId
            && await LoadByIdAsync(RuleSetService.VersionEntity, currentId, new[] { "sprk_policy", "sprk_ruletype" }, ct) is { } current
            && current.GetAttributeValue<EntityReference>("sprk_policy")?.Id == policyId
            && current.GetAttributeValue<OptionSetValue>("sprk_ruletype")?.Value == (int)RuleType.SubmissionIntegrity;
    }

    /// <summary>True when the record already holds an override row naming <paramref name="subjectId"/> in <paramref name="subjectColumn"/> (I-A1).</summary>
    private async Task<bool> HasRowAsync(string coreTable, Guid recordId, string subjectColumn, Guid subjectId, CancellationToken ct)
    {
        var query = new QueryExpression(ProcedureAssignmentEntity.Entity) { ColumnSet = new ColumnSet(subjectColumn), TopCount = 1 };
        query.Criteria.AddCondition(coreTable, ConditionOperator.Equal, recordId);
        query.Criteria.AddCondition(subjectColumn, ConditionOperator.Equal, subjectId);
        return (await ReadAsync(query, ct)).Entities.Count > 0;
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
    private async Task<EntityCollection> ReadAsync(QueryExpression query, CancellationToken ct)
    {
        try
        {
            return await _entities.RetrieveMultipleAsync(query, ct);
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

    private async Task<Entity?> LoadByIdAsync(string entity, Guid id, string[] columns, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        var query = new QueryExpression(entity) { ColumnSet = new ColumnSet(columns), TopCount = 1 };
        query.Criteria.AddCondition(entity + "id", ConditionOperator.Equal, id);
        return (await ReadAsync(query, ct)).Entities.FirstOrDefault(r => r.Id == id);
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
        var create = await AssignmentCreateAsync(spec, callerObjectId, ct);
        if (create.Refused is { } owner)
        {
            _logger.LogWarning("Override {AssignmentId}: the {What} was refused, no owner ({RefusalCode}); nothing was written",
                spec.Id, what, owner.RefusalCode);
            return RuleSetOutcome<OverrideView>.OwnerRefused(owner);
        }

        var sent = await SendAsync(new OrganizationRequest[] { create.Request! }, spec.Id, what);
        return sent == SendResult.Done ? null : WriteRefused<OverrideView>(sent, spec.Id);
    }

    /// <summary>A built create, owner applied, or the owner refusal (nothing was built to write).</summary>
    private sealed record BuiltCreate(CreateRequest? Request, RecordOwnerResolution? Refused);

    /// <summary>
    /// Builds the override row, asks its owner (the row's own question, with the caller as RequestedBy), writes the owner onto it and wraps
    /// it in its create, in one member, so the census sees the row, its owner write and its create together. The caller sends it.
    /// </summary>
    private async Task<BuiltCreate> AssignmentCreateAsync(AssignmentRow spec, Guid? callerObjectId, CancellationToken ct)
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
        Guid setId, string coreTable, Guid recordId, Guid? firmId, bool visibleToFirms, Guid? callerObjectId, CancellationToken ct)
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
    private async Task<BuiltCreate> RecordRuleCreateAsync(Guid policyId, Guid setId, BaseRule baseRule, Guid? callerObjectId, CancellationToken ct)
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
        Guid? callerObjectId, CancellationToken ct)
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
