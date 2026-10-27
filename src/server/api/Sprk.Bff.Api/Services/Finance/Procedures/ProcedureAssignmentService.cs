using System.Data;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Services.Finance.Procedures;

/// <summary>
/// <c>sprk_procedureassignment</c>'s names and option values (ebilling-r1 task 080, read live on spaarkedev1 2026-10-27 through the dataverse MCP:
/// UserOwned, entity set <c>sprk_procedureassignments</c>, <c>sprk_name</c> required).
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

/// <summary>
/// A record-scoped rule set (ebilling-r1 task 080 / 089, model 6.3, ontology component model: "Record-scoped rule set"): the set that holds one
/// core record's changed billing rules. <c>sprk_scope</c> Record (all firms) or Firm record (one firm, with <c>sprk_firm</c>), and exactly one core
/// lookup. It is a secure child of that record (lineage <c>sprk_ruleset</c> → core record, <c>sprk_policy</c> → <c>sprk_ruleset</c>,
/// <c>sprk_policyversion</c> → <c>sprk_policy</c>), so only <see cref="ProcedureAssignmentService"/> writes it, routed through
/// <see cref="IRecordOwnershipResolver"/>; <c>RuleSetService</c> treats it as not found (it calls only <see cref="IsRecordScoped"/>).
/// </summary>
public static class RecordScopedRuleSet
{
    public const int ScopeRecord = 100000002;
    public const int ScopeFirmRecord = 100000003;

    /// <summary>
    /// The policy-code prefix of a record rule. A record rule has its own policy code, so its Signals start a new D-13 key
    /// (<c>{policycode}|{type}|{id}|{episode}</c>; ontology component model, record rule row). The format, <c>BRR-</c> plus the policy's id
    /// (32 hex, upper case; 36 characters in the 50-character column), is this task's choice and is flagged for ontology-r1 review.
    /// </summary>
    public const string CodePrefix = "BRR-";

    /// <summary>True for a Record or Firm record rule set (the scope alone decides; the core lookup is field-secured to the BFF).</summary>
    public static bool IsRecordScoped(Entity set) =>
        set.GetAttributeValue<OptionSetValue>("sprk_scope")?.Value is ScopeRecord or ScopeFirmRecord;

    /// <summary>The code of a record rule whose policy id is <paramref name="policyId"/>.</summary>
    public static string CodeFor(Guid policyId) => CodePrefix + policyId.ToString("N", CultureInfo.InvariantCulture).ToUpperInvariant();
}

/// <summary>Assign a Procedure to a record: <c>FollowLatest</c> (default), <c>Pinned</c> (with a version of that Procedure) or <c>Excluded</c>.</summary>
public sealed record ProcedureAssignmentRequest(Guid? ProcedureId, string? Mode, Guid? PinnedVersionId, string? Reason);

/// <summary>Exclude one rule for a record (for one firm's invoices when <see cref="FirmId"/> is set). The reason is required.</summary>
public sealed record RuleExcludeRequest(Guid? PolicyId, string? Reason, Guid? FirmId);

/// <summary>
/// Change one rule for a record: the edited <see cref="Parameters"/> (the rule body of the base rule's type) replace the base rule's, for every
/// firm or for <see cref="FirmId"/> only. The reason is required.
/// </summary>
public sealed record RuleChangeRequest(Guid? PolicyId, JsonObject? Parameters, string? Reason, Guid? FirmId);

/// <summary>One override row as written: a Procedure-level row, or a rule-level Exclude (with its record rule when the rule was changed).</summary>
public sealed record OverrideView(
    Guid Id, string Kind, string Mode, Guid? ProcedureId, Guid? PinnedVersionId, Guid? PolicyId, Guid? FirmId, string? Reason,
    Guid? RecordRulePolicyId = null, Guid? RecordRuleVersionId = null, Guid? RecordRuleSetId = null, string? RecordRuleCode = null);

/// <summary>
/// The read-only confirmation before an override is removed (model 15 answer 2). For a changed-rule override: the warning and the caller-scoped
/// count, both counted exactly as the Procedure retire counts them (null when the count cannot be told). For any other override nothing closes,
/// so there is no warning and no count.
/// </summary>
public sealed record OverrideRemovalPreview(Guid Id, string Kind, bool ClosesRule, string? Warning, string? AffectedText, int? VisibleOpenInvoices);

/// <summary>A removed override. <see cref="CloseComplete"/> is false when some open findings of the switched-off record rule could not be closed now.</summary>
public sealed record OverrideRemoved(
    Guid Id, string Kind, int RulesSwitchedOff, bool CloseComplete, string? AffectedText, int? VisibleOpenInvoices, string? Message);

/// <summary>What the route's Read check is asked about for an existing override: its Procedure, or the rule it excludes.</summary>
public sealed record OverrideTarget(string EntitySet, Guid Id);

/// <summary>
/// The assignment and override service (ebilling-r1 task 089, EB-D-148; procedure model 5.1, 5.2, 6.2, 6.3, 6.5 I-A1 and 15 answer 2): assign, pin
/// or exclude a Procedure for one core record, exclude a rule for it, change a rule for it (an Exclude row plus a record rule in the record's
/// record-scoped rule set), and remove an override behind a read-only, caller-scoped preview.
/// </summary>
/// <remarks>
/// <para><b>Identity.</b> The routes have already run step 0 and checked, as the caller: Write and AppendTo on the core record (the uniform 404),
/// Read on the Procedure or on the rule's policy, and the billing-admin privilege (<c>ProcedureAssignmentEndpoints</c>). The writes then run as
/// the BFF application user, because the Procedure tables carry no role privilege (uac-r2 2026-10-21) and the rule-set scope and core lookups are
/// field-secured to it. The core table is fixed per route from <see cref="InvoiceFamilyCoreRecordGuard.FilingCoreTables"/>, never from input.</para>
/// <para><b>Ownership (model 5.1).</b> Every row is owned through <see cref="IRecordOwnershipResolver"/> with the caller as RequestedBy, so the
/// creator person is the caller: an assignment row and a new record-scoped set with parents [the core record]; a record rule's policy with parents
/// [its set] when the set already exists. A row created in the SAME transaction as its parent (the policy of a new set, every policy version)
/// takes its parent's resolution, because the resolver cannot read a parent that does not exist yet and that resolution is exactly what it
/// answers for a child of a row it owns. A Secure record's rows are therefore owned by its Secure team. A refusal writes nothing.</para>
/// <para><b>I-A1 (one row per subject).</b> One row per (record, Procedure) and one per (record, rule), firm or not: a duplicate is 409
/// <c>assignment.duplicate</c>. The check is a read AND the row id: the id is derived from (core table, record, subject), so two racing creates
/// collide on the primary key and the second is refused too.</para>
/// <para><b>All or nothing.</b> Each operation is one <see cref="IGenericEntityService.ExecuteTransactionAsync"/> sent with
/// <see cref="CancellationToken.None"/>. The Signal close after a removal runs outside it, through 081's shared method; the switched-off record
/// rule is what the D-170 sweep closes if it fails.</para>
/// <para><b>Not here</b> (POML 089): the resolver that reads these rows (082), the read models (084), the O-17 sweep, delete code.</para>
/// <para>§11: <i>Existing</i> — <see cref="RuleSetService"/> writes rule sets without record scope behind admin routes; <see cref="ProcedureService"/>
/// writes Procedures. Neither is caller-checked against a core record. <i>Extension</i> — the validation (<see cref="RuleSetService.ValidateCopiedRule"/>),
/// the Signal selection and close (<see cref="SignalClosureService"/>) and the caller-scoped count (<see cref="AffectedInvoiceCounter"/>) are reused, not
/// copied. <i>Cost of doing nothing</i> — no record can be pinned to an earlier Procedure Version, waive a rule or change one (O-4, O-10), and a
/// record-specific rule on a Secure record would sit outside its wall (uac-r2 F1-a).</para>
/// </remarks>
public class ProcedureAssignmentService
{
    // Stable error codes (ADR-019).
    internal const string CodeInvalid = "assignment.invalid";
    internal const string CodeNotFound = "assignment.not_found";
    internal const string CodeDuplicate = "assignment.duplicate";
    internal const string CodeProcedureNotFound = "assignment.procedure_not_found";
    internal const string CodeProcedureRetired = "assignment.procedure_retired";
    internal const string CodeRuleNotFound = "assignment.rule_not_found";
    internal const string CodeRuleNotInForce = "assignment.rule_not_in_force";
    internal const string CodeSystemRule = "assignment.system_rule";
    internal const string CodeFirmMismatch = "assignment.firm_mismatch";
    internal const string CodeRuleSetUnavailable = "assignment.ruleset_unavailable";
    internal const string CodeConcurrentEdit = "assignment.concurrent_edit";
    internal const string CodeWriteFailed = "assignment.write_failed";

    /// <summary>The removal warning of a changed-rule override (model 15 answer 2), verbatim.</summary>
    internal const string RemoveChangedRuleWarning =
        "Removing this override will close the rule it changed and remove pending decisions on active invoices.";

    internal const string KindProcedure = "Procedure";
    internal const string KindRuleExcluded = "RuleExcluded";
    internal const string KindRuleChanged = "RuleChanged";

    private const int MaxNameChars = 200;
    private const int MaxReasonChars = 2000;
    private const int RuleSetPublished = RuleSetService.RuleSetPublished;
    private const string PolicyEntitySet = "sprk_policies";
    private const string BasedOnColumn = "sprk_basedonversion";

    // The tables this service creates rows of, named here so the ownership census resolves each create to its table in this file.
    private const string AssignmentTable = "sprk_procedureassignment";
    private const string RecordRuleSetTable = "sprk_ruleset";
    private const string RecordPolicyTable = "sprk_policy";
    private const string RecordVersionTable = "sprk_policyversion";

    /// <summary>The namespace of the derived row ids (version-5 UUIDs, as <see cref="RuleSetService.VersionIdFor"/>).</summary>
    internal static readonly Guid RowIdNamespace = new("a4c2e8f1-7b3d-4e59-9c06-1d8f2b7e3a90");

    private static readonly string[] AssignmentColumns =
    {
        ProcedureAssignmentEntity.NameColumn, ProcedureAssignmentEntity.ModeColumn, ProcedureAssignmentEntity.ReasonColumn,
        ProcedureAssignmentEntity.ProcedureColumn, ProcedureAssignmentEntity.PinnedVersionColumn, ProcedureAssignmentEntity.ExcludedPolicyColumn,
        ProcedureAssignmentEntity.FirmColumn, "sprk_matter", "sprk_project", "sprk_workassignment",
    };

    private readonly IGenericEntityService _entities;
    private readonly RuleSetService _ruleSets;
    private readonly SignalClosureService _closure;
    private readonly AffectedInvoiceCounter _counter;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly ILogger<ProcedureAssignmentService> _logger;

    public ProcedureAssignmentService(
        IGenericEntityService entities,
        RuleSetService ruleSets,
        SignalClosureService closure,
        AffectedInvoiceCounter counter,
        IRecordOwnershipResolver ownership,
        ILogger<ProcedureAssignmentService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _ruleSets = ruleSets ?? throw new ArgumentNullException(nameof(ruleSets));
        _closure = closure ?? throw new ArgumentNullException(nameof(closure));
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Procedure-level: assign, pin, exclude (model 6.2)
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns, pins or excludes a Procedure for one core record: one row owned through the resolver with parents [the record]. A pinned version
    /// must be a version of that Procedure that has been in force (Active or Superseded; EB-D-155: a pin grandfathers the record's invoices to it).
    /// </summary>
    public virtual async Task<RuleSetOutcome<OverrideView>> AssignProcedureAsync(
        string coreTable, Guid recordId, ProcedureAssignmentRequest request, Guid? callerObjectId, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        ArgumentNullException.ThrowIfNull(request);

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

        if (mode == ProcedureAssignmentEntity.ModePinned)
        {
            var version = await LoadByIdAsync(ProcedureEntity.VersionEntity, request.PinnedVersionId!.Value,
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

        if (await FindRowsAsync(coreTable, recordId, ProcedureAssignmentEntity.ProcedureColumn, procedureId, ct) > 0)
        {
            return Duplicate<OverrideView>();
        }

        var owner = await _ownership.ResolveOwnerAsync(OwnerContext(new RecordOwnershipParent(coreTable, recordId), callerObjectId), ct);
        if (!owner.IsOwned)
        {
            return RuleSetOutcome<OverrideView>.OwnerRefused(owner);
        }

        var id = AssignmentIdFor(coreTable, recordId, ProcedureAssignmentEntity.ProcedureColumn, procedureId);
        var row = new Entity(AssignmentTable, id)
        {
            [ProcedureAssignmentEntity.NameColumn] = Name(procedure.GetAttributeValue<string>(ProcedureEntity.NameColumn), ModeName(mode)),
            [ProcedureAssignmentEntity.ModeColumn] = new OptionSetValue(mode),
            [ProcedureAssignmentEntity.ProcedureColumn] = new EntityReference(ProcedureEntity.Entity, procedureId),
            [coreTable] = new EntityReference(coreTable, recordId),
        };
        if (mode == ProcedureAssignmentEntity.ModePinned)
        {
            row[ProcedureAssignmentEntity.PinnedVersionColumn] = new EntityReference(ProcedureEntity.VersionEntity, request.PinnedVersionId!.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            row[ProcedureAssignmentEntity.ReasonColumn] = request.Reason.Trim();
        }

        owner.ApplyTo(row);
        var requests = new List<OrganizationRequest> { new CreateRequest { Target = row } };
        return await SendCreateAsync(requests, id, "assign", () => new OverrideView(
            id, KindProcedure, ModeName(mode), procedureId, request.PinnedVersionId, null, null, row.GetAttributeValue<string>(ProcedureAssignmentEntity.ReasonColumn)));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Rule-level: exclude, change (model 6.3)
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Excludes one rule for the record (for one firm's invoices when a firm is named): one Exclude row, parents [the record].</summary>
    public virtual async Task<RuleSetOutcome<OverrideView>> ExcludeRuleAsync(
        string coreTable, Guid recordId, RuleExcludeRequest request, Guid? callerObjectId, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<RuleSetError>();
        if (request.PolicyId is not { } policyId || policyId == Guid.Empty)
        {
            errors.Add(Error("policyId", "policyId is required."));
            policyId = Guid.Empty;
        }

        CheckReason(errors, request.Reason, required: true);
        await CheckFirmAsync(errors, request.FirmId, ct);
        if (errors.Count > 0)
        {
            return RuleSetOutcome<OverrideView>.Invalid(CodeInvalid, errors);
        }

        var (rule, refusal) = await LoadBaseRuleAsync<OverrideView>(policyId, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        if (await FindRowsAsync(coreTable, recordId, ProcedureAssignmentEntity.ExcludedPolicyColumn, policyId, ct) > 0)
        {
            return Duplicate<OverrideView>();
        }

        var owner = await _ownership.ResolveOwnerAsync(OwnerContext(new RecordOwnershipParent(coreTable, recordId), callerObjectId), ct);
        if (!owner.IsOwned)
        {
            return RuleSetOutcome<OverrideView>.OwnerRefused(owner);
        }

        var id = AssignmentIdFor(coreTable, recordId, ProcedureAssignmentEntity.ExcludedPolicyColumn, policyId);
        var requests = new List<OrganizationRequest>();
        AddExcludeCreate(requests, id, coreTable, recordId, rule!.Policy, request.Reason!, request.FirmId, owner);
        return await SendCreateAsync(requests, id, "exclude rule", () => new OverrideView(
            id, KindRuleExcluded, ModeName(ProcedureAssignmentEntity.ModeExcluded), null, null, policyId, request.FirmId, request.Reason!.Trim()));
    }

    /// <summary>
    /// Changes one rule for the record, in ONE transaction: an Exclude row for the base rule, and a record rule — a copy of the base rule's current
    /// version with the edited parameters, validated (<see cref="RuleSetService.ValidateCopiedRule"/>) and published immutable — in the record's
    /// record-scoped set (created on first use: scope Record for every firm, Firm record with <c>sprk_firm</c> for one firm). The record rule
    /// stores "based on" = the base rule's policy version. A firm-named base rule is changed only for its own firm (400).
    /// </summary>
    public virtual async Task<RuleSetOutcome<OverrideView>> ChangeRuleAsync(
        string coreTable, Guid recordId, RuleChangeRequest request, Guid? callerObjectId, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<RuleSetError>();
        if (request.PolicyId is not { } policyId || policyId == Guid.Empty)
        {
            errors.Add(Error("policyId", "policyId is required."));
            policyId = Guid.Empty;
        }

        if (request.Parameters is null)
        {
            errors.Add(Error("parameters", "parameters (the changed rule's body) are required."));
        }

        CheckReason(errors, request.Reason, required: true);
        await CheckFirmAsync(errors, request.FirmId, ct);
        if (errors.Count > 0)
        {
            return RuleSetOutcome<OverrideView>.Invalid(CodeInvalid, errors);
        }

        var (loaded, refusal) = await LoadBaseRuleAsync<OverrideView>(policyId, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        var rule = loaded!;
        if (rule.Current is not { } current || !rule.InForce)
        {
            return Refused<OverrideView>(StatusCodes.Status409Conflict, CodeRuleNotInForce,
                "This rule is not in force (no published version, switched off, or its rule set is not published), so there is nothing to change. Exclude it instead.");
        }

        // O-20: a firm-named base rule changes only for its own firm (never for every firm, never for another firm).
        if (rule.SetFirmId is { } baseFirm && request.FirmId != baseFirm)
        {
            return RuleSetOutcome<OverrideView>.Invalid(CodeFirmMismatch, new[]
            {
                Error("firmId", "This rule applies to one firm only, so it can be changed only for that firm."),
            });
        }

        var copied = current with { RuleBody = request.Parameters!.ToJsonString() };
        var invalid = _ruleSets.ValidateCopiedRule(rule.Policy, current.RuleType?.ToString(CultureInfo.InvariantCulture), copied);
        if (invalid.Count > 0)
        {
            return RuleSetOutcome<OverrideView>.Invalid(CodeInvalid, invalid);
        }

        if (await FindRowsAsync(coreTable, recordId, ProcedureAssignmentEntity.ExcludedPolicyColumn, policyId, ct) > 0)
        {
            return Duplicate<OverrideView>();
        }

        var setId = RecordSetIdFor(coreTable, recordId, request.FirmId);
        var existingSet = await LoadRecordSetAsync(setId, ct);
        if (existingSet is not null && !IsUsableRecordSet(existingSet, coreTable, recordId, request.FirmId))
        {
            return Refused<OverrideView>(StatusCodes.Status409Conflict, CodeRuleSetUnavailable,
                "This record's rule changes cannot be added to right now. Contact support.");
        }

        var coreOwner = await _ownership.ResolveOwnerAsync(OwnerContext(new RecordOwnershipParent(coreTable, recordId), callerObjectId), ct);
        if (!coreOwner.IsOwned)
        {
            return RuleSetOutcome<OverrideView>.OwnerRefused(coreOwner);
        }

        // The record rule's policy is filed under its set: resolved against the stored set when it exists; created with the set, it takes the
        // set's own resolution (see the remarks). The version is always created with its policy, so it takes the policy's.
        var policyOwner = existingSet is null
            ? coreOwner
            : await _ownership.ResolveOwnerAsync(OwnerContext(new RecordOwnershipParent(RuleSetService.RuleSetEntity, setId), callerObjectId), ct);
        if (!policyOwner.IsOwned)
        {
            return RuleSetOutcome<OverrideView>.OwnerRefused(policyOwner);
        }

        var assignmentId = AssignmentIdFor(coreTable, recordId, ProcedureAssignmentEntity.ExcludedPolicyColumn, policyId);
        var plan = PlanChange(coreTable, recordId, request, rule, copied, setId, existingSet is null, assignmentId, coreOwner, policyOwner);
        return await SendCreateAsync(plan.Requests, assignmentId, "change rule", () => new OverrideView(
            assignmentId, KindRuleChanged, ModeName(ProcedureAssignmentEntity.ModeExcluded), null, null, policyId, request.FirmId,
            request.Reason!.Trim(), plan.PolicyId, plan.VersionId, setId, plan.Code));
    }

    internal sealed record ChangePlan(IReadOnlyList<OrganizationRequest> Requests, Guid PolicyId, Guid VersionId, string Code);

    /// <summary>
    /// The rows of one change, in transaction order: the record-scoped set (when new), the record rule's policy, its one Published version, the
    /// policy's current-version pointer, and the Exclude row. Every row is built, owned and handed to its create here, in one member.
    /// </summary>
    private static ChangePlan PlanChange(
        string coreTable, Guid recordId, RuleChangeRequest request, BaseRule rule, RuleSetService.VersionRow copied, Guid setId, bool newSet,
        Guid assignmentId, RecordOwnerResolution coreOwner, RecordOwnerResolution policyOwner)
    {
        var requests = new List<OrganizationRequest>();
        if (newSet)
        {
            var set = new Entity(RecordRuleSetTable, setId)
            {
                ["sprk_name"] = request.FirmId is null ? "Rule changes for one record" : "Rule changes for one record and firm",
                [RuleDomains.PolicyDomainColumn] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
                ["sprk_scope"] = new OptionSetValue(request.FirmId is null ? RecordScopedRuleSet.ScopeRecord : RecordScopedRuleSet.ScopeFirmRecord),
                [coreTable] = new EntityReference(coreTable, recordId),
                // Clause quotes of a record rule are not shown to firms (the firm-facing clause read treats a record-scoped set as not found).
                ["sprk_visibletofirms"] = false,
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(RuleSetPublished),
            };
            if (request.FirmId is { } firmId)
            {
                set["sprk_firm"] = new EntityReference(RuleSetService.FirmEntity, firmId);
            }

            coreOwner.ApplyTo(set);
            requests.Add(new CreateRequest { Target = set });
        }

        var policyId = Guid.NewGuid();
        var code = RecordScopedRuleSet.CodeFor(policyId);
        var policy = new Entity(RecordPolicyTable, policyId)
        {
            ["sprk_name"] = rule.Policy.Name,
            ["sprk_policycode"] = code,
            ["sprk_enabled"] = false, // switched on below, in the same transaction, once its version exists
            [RuleDomains.PolicyDomainColumn] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
            ["sprk_ruleset"] = new EntityReference(RuleSetService.RuleSetEntity, setId),
        };
        foreach (var (column, value) in rule.Copied)
        {
            policy[column] = value;
        }

        policyOwner.ApplyTo(policy);
        requests.Add(new CreateRequest { Target = policy });

        var versionId = Guid.NewGuid();
        var version = new Entity(RecordVersionTable, versionId)
        {
            ["sprk_name"] = $"{code} v1",
            ["sprk_policy"] = new EntityReference(RuleSetService.PolicyEntity, policyId),
            ["sprk_versionnumber"] = 1,
            ["sprk_ruletype"] = new OptionSetValue(copied.RuleType!.Value),
            ["sprk_rulebody"] = copied.RuleBody!,
            ["sprk_messagetemplate"] = copied.Template!,
            ["sprk_shortheadline"] = copied.Headline!,
            ["sprk_consequence"] = new OptionSetValue(copied.Consequence!.Value),
            ["sprk_proposedaction"] = RuleSetService.ProposedActionText(copied.Consequence),
            ["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Accepted),
            [RuleSetService.PublishStateColumn] = new OptionSetValue(RuleSetService.PublishStatePublished),
            ["sprk_inforcefrom"] = DateTime.UtcNow.Date,
            [BasedOnColumn] = new EntityReference(RuleSetService.VersionEntity, copied.Id),
        };
        if (copied.ClauseReference is not null)
        {
            version["sprk_clausereference"] = copied.ClauseReference;
        }

        if (copied.ClauseLocation is not null)
        {
            version["sprk_clauselocation"] = copied.ClauseLocation;
        }

        policyOwner.ApplyTo(version);
        requests.Add(new CreateRequest { Target = version });

        // The pointer and the switch: the record rule is in force from now (immutable: nothing edits a published version).
        requests.Add(SwitchOn(policyId, versionId));
        AddExcludeCreate(requests, assignmentId, coreTable, recordId, rule.Policy, request.Reason!, request.FirmId, coreOwner);
        return new ChangePlan(requests, policyId, versionId, code);
    }

    /// <summary>The record rule's current-version pointer and its switch On, sent after its version's create in the same transaction.</summary>
    private static UpdateRequest SwitchOn(Guid policyId, Guid versionId) => new()
    {
        Target = new Entity(RuleSetService.PolicyEntity, policyId)
        {
            ["sprk_currentversion"] = new EntityReference(RuleSetService.VersionEntity, versionId),
            ["sprk_enabled"] = true,
        },
    };

    /// <summary>
    /// Builds a rule-level Exclude row, writes its resolved owner and adds its create to <paramref name="requests"/> (one member, so the
    /// census sees the row, its owner write and its create together).
    /// </summary>
    private static void AddExcludeCreate(
        List<OrganizationRequest> requests, Guid id, string coreTable, Guid recordId, RuleSetService.PolicyRow policy, string reason, Guid? firmId,
        RecordOwnerResolution owner)
    {
        // A rule-level row never names a Procedure, so a rule that is gone can never read as a Procedure-level Exclude (model 9 schema review).
        var row = new Entity(AssignmentTable, id)
        {
            [ProcedureAssignmentEntity.NameColumn] = Name(policy.Name, "Excluded rule"),
            [ProcedureAssignmentEntity.ModeColumn] = new OptionSetValue(ProcedureAssignmentEntity.ModeExcluded),
            [ProcedureAssignmentEntity.ExcludedPolicyColumn] = new EntityReference(RuleSetService.PolicyEntity, policy.Id),
            [ProcedureAssignmentEntity.ReasonColumn] = reason.Trim(),
            [coreTable] = new EntityReference(coreTable, recordId),
        };
        if (firmId is { } firm)
        {
            row[ProcedureAssignmentEntity.FirmColumn] = new EntityReference(RuleSetService.FirmEntity, firm);
        }

        owner.ApplyTo(row);
        requests.Add(new CreateRequest { Target = row });
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Remove (model 6.3 remove, 15 answer 2)
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What the route's Read check asks about for override <paramref name="assignmentId"/> of the record: its Procedure, or the rule it excludes.
    /// Null when the override does not exist or belongs to another record (the route then answers the service's 404). A read fault throws.
    /// </summary>
    internal virtual async Task<OverrideTarget?> TargetOfAsync(string coreTable, Guid recordId, Guid assignmentId, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        var row = await LoadAssignmentAsync(coreTable, recordId, assignmentId, ct);
        if (row?.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.ProcedureColumn)?.Id is { } procedureId)
        {
            return new OverrideTarget(ProcedureEntity.EntitySet, procedureId);
        }

        return row?.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.ExcludedPolicyColumn)?.Id is { } policyId
            ? new OverrideTarget(PolicyEntitySet, policyId)
            : null;
    }

    /// <summary>
    /// The read-only removal confirmation. For a changed-rule override, the warning and how many open invoices THE CALLER can Read whose open
    /// findings of the record rule the removal would close: the selection the close uses (<see cref="SignalClosureService.SelectOpenSignalsOfPolicyAsync"/>),
    /// counted by <see cref="AffectedInvoiceCounter"/>; no count when it cannot be told. Any other override closes nothing. Writes nothing.
    /// </summary>
    public virtual async Task<RuleSetOutcome<OverrideRemovalPreview>> PreviewRemoveAsync(
        string coreTable, Guid recordId, Guid assignmentId, string? callerBearerToken, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        var row = await LoadAssignmentAsync(coreTable, recordId, assignmentId, ct);
        if (row is null)
        {
            return NotFound<OverrideRemovalPreview>();
        }

        var recordRules = await FindRecordRulesAsync(coreTable, recordId, row, ct);
        if (recordRules.Count == 0)
        {
            return RuleSetOutcome<OverrideRemovalPreview>.Done(new OverrideRemovalPreview(assignmentId, KindOf(row, recordRules), false, null, null, null));
        }

        int? count = null;
        try
        {
            var signals = new List<OpenPolicySignal>();
            foreach (var policy in recordRules)
            {
                signals.AddRange(await _closure.SelectOpenSignalsOfPolicyAsync(policy.Id, withBillingSubject: true, ct));
            }

            count = await _counter.CountVisibleOpenInvoicesAsync(signals, callerBearerToken, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Override {AssignmentId}: the removal preview could not select the open findings; the warning shows without a count", assignmentId);
        }

        return RuleSetOutcome<OverrideRemovalPreview>.Done(new OverrideRemovalPreview(
            assignmentId, KindRuleChanged, true, RemoveChangedRuleWarning, ProcedureService.AffectedText(count), count));
    }

    /// <summary>
    /// Removes an override (on confirm): the row is deleted and, for a changed-rule override, the record rule is switched Off in the SAME
    /// transaction; then its open Signals close <c>PolicyRetired</c> through <see cref="SignalClosureService.ClosePolicySignalsRetiredAsync"/> (the
    /// preview's selection, at confirm time). Any other removal closes nothing and re-checks nothing (model 8.2). The count is caller-scoped.
    /// </summary>
    public virtual async Task<RuleSetOutcome<OverrideRemoved>> RemoveAsync(
        string coreTable, Guid recordId, Guid assignmentId, string? callerBearerToken, CancellationToken ct)
    {
        RequireCore(coreTable, recordId);
        var row = await LoadAssignmentAsync(coreTable, recordId, assignmentId, ct);
        if (row is null)
        {
            return NotFound<OverrideRemoved>();
        }

        var recordRules = await FindRecordRulesAsync(coreTable, recordId, row, ct);
        var kind = KindOf(row, recordRules);
        var requests = new List<OrganizationRequest>
        {
            new DeleteRequest { Target = new EntityReference(ProcedureAssignmentEntity.Entity, assignmentId) },
        };
        requests.AddRange(recordRules.Select(p => (OrganizationRequest)new UpdateRequest
        {
            Target = new Entity(RuleSetService.PolicyEntity, p.Id) { ["sprk_enabled"] = false },
        }));

        switch (await SendAsync(requests, assignmentId, "remove"))
        {
            case SendResult.Done:
                break;
            case SendResult.Conflict:
                return Concurrent<OverrideRemoved>();
            case SendResult.Unknown:
                return WriteFailed<OverrideRemoved>("The override may or may not have been removed. Reload the record before trying again.");
            default:
                return WriteFailed<OverrideRemoved>("The override could not be removed and nothing was changed. Try again; if it keeps failing, contact support.");
        }

        if (recordRules.Count == 0)
        {
            _logger.LogInformation("Override {AssignmentId} ({Kind}) removed; nothing closed", assignmentId, kind);
            return RuleSetOutcome<OverrideRemoved>.Done(new OverrideRemoved(assignmentId, kind, 0, true, null, null, null));
        }

        var outcome = SignalClosureOutcome.None;
        var closed = new List<OpenPolicySignal>();
        foreach (var policy in recordRules)
        {
            try
            {
                var closure = await _closure.ClosePolicySignalsRetiredAsync(policy.Id, policy.Code, withBillingSubject: true, CancellationToken.None);
                outcome = outcome.Plus(closure.Outcome);
                closed.AddRange(closure.Closed);
            }
            catch (Exception ex)
            {
                // The record rule is already Off, so the nightly D-170 sweep closes what this could not.
                outcome = outcome with { PolicyFailures = outcome.PolicyFailures + 1 };
                _logger.LogError(ex, "Override {AssignmentId}: the open findings of record rule {PolicyCode} could not be closed now; the rule is Off, so the nightly run closes them",
                    assignmentId, policy.Code);
            }
        }

        int? count;
        try
        {
            count = await _counter.CountVisibleOpenInvoicesAsync(closed, callerBearerToken, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Override {AssignmentId} removed; the affected-invoice count could not be made, so none is shown", assignmentId);
            count = null;
        }

        _logger.LogInformation("Override {AssignmentId} removed: {Off} record rule(s) switched Off, {Closed} finding(s) closed, complete={Complete}",
            assignmentId, recordRules.Count, outcome.Closed, outcome.IsComplete);
        return RuleSetOutcome<OverrideRemoved>.Done(new OverrideRemoved(
            assignmentId, kind, recordRules.Count, outcome.IsComplete, ProcedureService.AffectedText(count), count,
            outcome.IsComplete ? null : "Some findings could not be closed now. The changed rule is off, so the nightly run closes the rest."));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Reads
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A base rule: the policy, its set's firm, the policy columns a copy carries, and its current version (null when none).</summary>
    /// <remarks><see cref="InForce"/>: the rule is switched On, and its set is a Procedure set (in force through its Procedure Version, 081) or a
    /// Published client or firm set. A change creates an in-force record rule, so it is refused for a rule that is not in force (it would revive
    /// a switched-off or retired rule on this record); an exclusion of such a rule is harmless and allowed.</remarks>
    private sealed record BaseRule(
        RuleSetService.PolicyRow Policy, Guid? SetFirmId, IReadOnlyList<(string Column, object Value)> Copied, RuleSetService.VersionRow? Current,
        bool InForce);

    /// <summary>
    /// The rule an override names: a live Billing policy in a Billing set that is NOT record-scoped (a record rule is removed, not overridden), and
    /// not the system rule (D-125). Read as the application; the route has already checked the caller's Read on the policy.
    /// </summary>
    private async Task<(BaseRule? Rule, RuleSetOutcome<T>? Refusal)> LoadBaseRuleAsync<T>(Guid policyId, CancellationToken ct) where T : class
    {
        var policyQuery = new QueryExpression(RuleSetService.PolicyEntity)
        {
            ColumnSet = new ColumnSet(RuleSetService.PolicyColumns.Concat(new[] { "sprk_priority", "sprk_lane", "sprk_severity" }).ToArray()),
            TopCount = 1,
        };
        policyQuery.Criteria.AddCondition("sprk_policyid", ConditionOperator.Equal, policyId);
        policyQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        RuleDomains.AddBillingFilter(policyQuery);
        var policy = (await _entities.RetrieveMultipleAsync(policyQuery, ct)).Entities.FirstOrDefault(p => p.Id == policyId && RuleDomains.IsBillingRow(p));
        var setId = policy?.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id;
        var set = setId is { } id ? await LoadByIdAsync(RuleSetService.RuleSetEntity, id, RuleSetService.RuleSetColumns, ct) : null;
        if (policy is null || set is null || !RuleDomains.IsBillingRow(set) || RecordScopedRuleSet.IsRecordScoped(set))
        {
            return (null, Refused<T>(StatusCodes.Status404NotFound, CodeRuleNotFound, "The rule was not found."));
        }

        RuleSetService.VersionRow? current = null;
        if (policy.GetAttributeValue<EntityReference>("sprk_currentversion")?.Id is { } currentId)
        {
            var version = await LoadByIdAsync(RuleSetService.VersionEntity, currentId, RuleSetService.VersionColumns, ct);
            if (version?.GetAttributeValue<EntityReference>("sprk_policy")?.Id == policyId)
            {
                current = RuleSetService.ToVersion(version);
            }
        }

        if (current?.RuleType == (int)RuleType.SubmissionIntegrity)
        {
            return (null, Refused<T>(StatusCodes.Status409Conflict, CodeSystemRule, "This is a system rule; it cannot be excluded or changed for a record."));
        }

        var copied = new List<(string, object)>();
        foreach (var column in new[] { "sprk_priority", "sprk_lane", "sprk_severity" })
        {
            if (policy.Contains(column) && policy[column] is { } value)
            {
                copied.Add((column, value));
            }
        }

        var row = new RuleSetService.PolicyRow(
            policy.Id,
            policy.GetAttributeValue<string>("sprk_policycode") ?? policy.Id.ToString(),
            policy.GetAttributeValue<string>("sprk_name") ?? policy.Id.ToString(),
            current?.Id,
            Domain: RuleDomains.PolicyDomainBilling);
        var inForce = policy.GetAttributeValue<bool?>("sprk_enabled") == true
            && (set.GetAttributeValue<EntityReference>(RuleSetService.ProcedureColumn) is not null
                || set.GetAttributeValue<OptionSetValue>("statuscode")?.Value == RuleSetPublished);
        return (new BaseRule(row, set.GetAttributeValue<EntityReference>("sprk_firm")?.Id, copied, current, inForce), null);
    }

    /// <summary>The override row, only when it names THIS record in the route's core column (else null: the uniform not found).</summary>
    private async Task<Entity?> LoadAssignmentAsync(string coreTable, Guid recordId, Guid assignmentId, CancellationToken ct)
    {
        if (assignmentId == Guid.Empty)
        {
            return null;
        }

        var row = await LoadByIdAsync(ProcedureAssignmentEntity.Entity, assignmentId, AssignmentColumns, ct);
        return row?.GetAttributeValue<EntityReference>(coreTable)?.Id == recordId ? row : null;
    }

    /// <summary>
    /// The record rules a rule-level Exclude row changed: in the record's record-scoped set for the row's firm (or every firm), the ENABLED
    /// policies whose current version is based on a version of the excluded rule. Empty for a Procedure-level row or a plain Exclude.
    /// </summary>
    private async Task<IReadOnlyList<RuleSetService.PolicyRow>> FindRecordRulesAsync(string coreTable, Guid recordId, Entity row, CancellationToken ct)
    {
        if (row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.ExcludedPolicyColumn)?.Id is not { } excludedPolicyId)
        {
            return Array.Empty<RuleSetService.PolicyRow>();
        }

        var firmId = row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.FirmColumn)?.Id;
        var setId = RecordSetIdFor(coreTable, recordId, firmId);
        if (await LoadRecordSetAsync(setId, ct) is not { } set || !IsUsableRecordSet(set, coreTable, recordId, firmId))
        {
            return Array.Empty<RuleSetService.PolicyRow>();
        }

        var (policies, truncated) = await _ruleSets.LoadPoliciesAsync(setId, ct);
        if (truncated)
        {
            throw new InvalidOperationException($"The record-scoped rule set {setId} has more policies than one page.");
        }

        var enabled = policies.Where(p => p.Enabled == true && p.CurrentVersionId is not null).ToList();
        if (enabled.Count == 0)
        {
            return Array.Empty<RuleSetService.PolicyRow>();
        }

        var currentQuery = new QueryExpression(RuleSetService.VersionEntity) { ColumnSet = new ColumnSet("sprk_policy", BasedOnColumn) };
        currentQuery.Criteria.AddCondition("sprk_policyversionid", ConditionOperator.In, enabled.Select(p => (object)p.CurrentVersionId!.Value).ToArray());
        var basedOn = (await _entities.RetrieveMultipleAsync(currentQuery, ct)).Entities
            .Where(v => v.GetAttributeValue<EntityReference>(BasedOnColumn) is not null)
            .ToDictionary(v => v.Id, v => v.GetAttributeValue<EntityReference>(BasedOnColumn).Id);
        if (basedOn.Count == 0)
        {
            return Array.Empty<RuleSetService.PolicyRow>();
        }

        var baseQuery = new QueryExpression(RuleSetService.VersionEntity) { ColumnSet = new ColumnSet("sprk_policy") };
        baseQuery.Criteria.AddCondition("sprk_policyversionid", ConditionOperator.In, basedOn.Values.Distinct().Select(id => (object)id).ToArray());
        var ofExcluded = (await _entities.RetrieveMultipleAsync(baseQuery, ct)).Entities
            .Where(v => v.GetAttributeValue<EntityReference>("sprk_policy")?.Id == excludedPolicyId)
            .Select(v => v.Id)
            .ToHashSet();

        return enabled
            .Where(p => basedOn.TryGetValue(p.CurrentVersionId!.Value, out var b) && ofExcluded.Contains(b))
            .ToList();
    }

    /// <summary>How many override rows of the record name <paramref name="subjectId"/> in <paramref name="subjectColumn"/> (I-A1).</summary>
    private async Task<int> FindRowsAsync(string coreTable, Guid recordId, string subjectColumn, Guid subjectId, CancellationToken ct)
    {
        var query = new QueryExpression(ProcedureAssignmentEntity.Entity) { ColumnSet = new ColumnSet(subjectColumn), TopCount = 1 };
        query.Criteria.AddCondition(coreTable, ConditionOperator.Equal, recordId);
        query.Criteria.AddCondition(subjectColumn, ConditionOperator.Equal, subjectId);
        return (await _entities.RetrieveMultipleAsync(query, ct)).Entities.Count;
    }

    private async Task<Entity?> LoadRecordSetAsync(Guid setId, CancellationToken ct) =>
        await LoadByIdAsync(RuleSetService.RuleSetEntity, setId,
            RuleSetService.RuleSetColumns.Concat(InvoiceFamilyCoreRecordGuard.FilingCoreTables).Append("statecode").ToArray(), ct);

    /// <summary>A record-scoped set this operation may use: Billing, Published and active, the scope and firm asked for, and THIS record only.</summary>
    private static bool IsUsableRecordSet(Entity set, string coreTable, Guid recordId, Guid? firmId) =>
        RuleDomains.IsBillingRow(set)
        && set.GetAttributeValue<OptionSetValue>("sprk_scope")?.Value == (firmId is null ? RecordScopedRuleSet.ScopeRecord : RecordScopedRuleSet.ScopeFirmRecord)
        && set.GetAttributeValue<EntityReference>("sprk_firm")?.Id == firmId
        && set.GetAttributeValue<OptionSetValue>("statuscode")?.Value == RuleSetPublished
        && (set.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0) == 0
        && InvoiceFamilyCoreRecordGuard.FilingCoreTables.All(core =>
            core == coreTable
                ? set.GetAttributeValue<EntityReference>(core)?.Id == recordId
                : set.GetAttributeValue<EntityReference>(core) is null);

    private async Task<Entity?> LoadByIdAsync(string entity, Guid id, string[] columns, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        var query = new QueryExpression(entity) { ColumnSet = new ColumnSet(columns), TopCount = 1 };
        query.Criteria.AddCondition(entity + "id", ConditionOperator.Equal, id);
        return (await _entities.RetrieveMultipleAsync(query, ct)).Entities.FirstOrDefault(r => r.Id == id);
    }

    private async Task CheckFirmAsync(List<RuleSetError> errors, Guid? firmId, CancellationToken ct)
    {
        if (firmId is not { } firm)
        {
            return;
        }

        if (firm == Guid.Empty || await LoadByIdAsync(RuleSetService.FirmEntity, firm, new[] { "sprk_name" }, ct) is null)
        {
            errors.Add(Error("firmId", "The firm was not found."));
        }
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Writes
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    private enum SendResult
    {
        Done,
        Conflict,
        Duplicate,
        Failed,
        Unknown,
    }

    private async Task<RuleSetOutcome<OverrideView>> SendCreateAsync(
        IReadOnlyList<OrganizationRequest> requests, Guid assignmentId, string what, Func<OverrideView> view) =>
        await SendAsync(requests, assignmentId, what) switch
        {
            SendResult.Done => RuleSetOutcome<OverrideView>.Done(view()),
            SendResult.Duplicate => Duplicate<OverrideView>(),
            SendResult.Conflict => Concurrent<OverrideView>(),
            // Never "nothing was saved" here: a retry could find its own row. The id lets the caller look before retrying.
            SendResult.Unknown => WriteFailed<OverrideView>(
                $"The override may or may not have been saved (id {assignmentId}). Reload the record before trying again."),
            _ => WriteFailed<OverrideView>("The override could not be saved and nothing was written. Try again; if it keeps failing, contact support."),
        };

    /// <summary>One transaction, all or nothing, with <see cref="CancellationToken.None"/>: a caller who disconnects never leaves half a change.</summary>
    private async Task<SendResult> SendAsync(IReadOnlyList<OrganizationRequest> requests, Guid assignmentId, string what)
    {
        try
        {
            await _entities.ExecuteTransactionAsync(requests, CancellationToken.None);
            return SendResult.Done;
        }
        catch (Exception ex) when (IsPrimaryKeyDuplicate(ex))
        {
            _logger.LogWarning("Override {AssignmentId}: the {What} was refused; a row with its derived id exists. Nothing was written", assignmentId, what);
            return SendResult.Duplicate;
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

    /// <summary>Dataverse's "Cannot insert duplicate key" (<c>0x80040237</c>) anywhere in the chain, as <c>TrackingNumberAllocator</c> reads it.</summary>
    private static bool IsPrimaryKeyDuplicate(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.ServiceModel.FaultException<OrganizationServiceFault> fault && fault.Detail?.ErrorCode == unchecked((int)0x80040237))
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

    /// <summary>The id of the record's one record-scoped set for every firm (no firm) or for one firm.</summary>
    internal static Guid RecordSetIdFor(string coreTable, Guid recordId, Guid? firmId) =>
        DerivedId($"sprk_ruleset:{coreTable}:{recordId:D}:{(firmId is { } f ? f.ToString("D") : "allfirms")}");

    /// <summary>A version-5 UUID (SHA-1) of <paramref name="name"/> in <see cref="RowIdNamespace"/>, as <see cref="RuleSetService.VersionIdFor"/>.</summary>
    private static Guid DerivedId(string name)
    {
        var bytes8 = System.Text.Encoding.UTF8.GetBytes(name);
        var input = new byte[16 + bytes8.Length];
        RowIdNamespace.ToByteArray(bigEndian: true).CopyTo(input, 0);
        bytes8.CopyTo(input, 16);

        var bytes = System.Security.Cryptography.SHA1.HashData(input)[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    private static RecordOwnershipContext OwnerContext(RecordOwnershipParent parent, Guid? callerObjectId) =>
        RecordOwnershipContext.ForParents(new[] { parent }, callerObjectId) with
        {
            RequestedBy = RecordRequester.Of(objectId: callerObjectId),
        };

    private static string KindOf(Entity row, IReadOnlyCollection<RuleSetService.PolicyRow> recordRules) =>
        row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.ProcedureColumn) is not null
            ? KindProcedure
            : recordRules.Count > 0 ? KindRuleChanged : KindRuleExcluded;

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

    /// <summary>"{subject} ({what})", the subject shortened so the whole fits the 200-character name column.</summary>
    internal static string Name(string? subject, string what)
    {
        var suffix = $" ({what})";
        var name = subject ?? string.Empty;
        return (name.Length + suffix.Length > MaxNameChars ? name[..(MaxNameChars - suffix.Length)].TrimEnd() : name) + suffix;
    }

    private static RuleSetError Error(string field, string message) => new(null, null, field, message, null);

    private static RuleSetOutcome<T> NotFound<T>() where T : class =>
        Refused<T>(StatusCodes.Status404NotFound, CodeNotFound, "The override was not found on this record.");

    private static RuleSetOutcome<T> Duplicate<T>() where T : class =>
        Refused<T>(StatusCodes.Status409Conflict, CodeDuplicate,
            "This record already has an override for it. Remove that override first, or change it.");

    private static RuleSetOutcome<T> Concurrent<T>() where T : class =>
        Refused<T>(StatusCodes.Status409Conflict, CodeConcurrentEdit,
            "The record's overrides changed while this was being saved, so nothing was written. Reload and try again.");

    private static RuleSetOutcome<T> WriteFailed<T>(string detail) where T : class =>
        Refused<T>(StatusCodes.Status500InternalServerError, CodeWriteFailed, detail);

    private static RuleSetOutcome<T> Refused<T>(int status, string code, string detail) where T : class =>
        RuleSetOutcome<T>.Refused(status, code, detail);
}
