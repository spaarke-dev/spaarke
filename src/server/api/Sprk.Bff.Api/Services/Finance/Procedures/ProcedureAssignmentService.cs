using System.Data;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
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
/// <para><b>Not here</b> (POML 089): change rule and remove (089c), the resolver that reads these rows (082), the read models (084), the
/// O-17 sweep, delete code.</para>
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

    internal const string KindProcedure = "Procedure";
    internal const string KindRuleExcluded = "RuleExcluded";

    internal const string DuplicateDetail = "This record already has an override for it. Remove that override first.";

    private const int MaxNameChars = 200;
    private const int MaxReasonChars = 2000;

    /// <summary><c>sprk_ruleset.sprk_scope</c> Record and Firm record (task 080): a record-scoped set, whose rules are removed, not overridden.</summary>
    private const int ScopeRecord = 100000002;
    private const int ScopeFirmRecord = 100000003;

    // The table this service creates rows of, named here so the ownership census resolves the create to its table in this file.
    private const string AssignmentTable = "sprk_procedureassignment";

    /// <summary>The namespace of the derived row ids (version-5 UUIDs, as <see cref="RuleSetService.VersionIdFor"/>).</summary>
    internal static readonly Guid RowIdNamespace = new("a4c2e8f1-7b3d-4e59-9c06-1d8f2b7e3a90");

    private readonly IGenericEntityService _entities;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly ILogger<ProcedureAssignmentService> _logger;

    public ProcedureAssignmentService(
        IGenericEntityService entities,
        IRecordOwnershipResolver ownership,
        ILogger<ProcedureAssignmentService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
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

        var errors = new List<RuleSetError>();
        if (request.PolicyId is not { } policyId || policyId == Guid.Empty)
        {
            errors.Add(Error("policyId", "policyId is required."));
            policyId = Guid.Empty;
        }

        CheckReason(errors, request.Reason, required: true);
        if (request.FirmId is { } firm
            && (firm == Guid.Empty || await LoadByIdAsync(RuleSetService.FirmEntity, firm, new[] { "sprk_name" }, ct) is null))
        {
            errors.Add(Error("firmId", "The firm was not found."));
        }

        if (errors.Count > 0)
        {
            return RuleSetOutcome<OverrideView>.Invalid(CodeInvalid, errors);
        }

        var (ruleName, refusal) = await LoadRuleAsync(policyId, ct);
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
            coreTable, recordId, Name(ruleName, "Excluded rule"), ProcedureAssignmentEntity.ModeExcluded,
            ProcedureId: null, PinnedVersionId: null, ExcludedPolicyId: policyId, FirmId: request.FirmId, Reason: reason);
        return await CreateRowAsync(row, callerObjectId, "exclude rule", ct) is { } failed
            ? failed
            : RuleSetOutcome<OverrideView>.Done(new OverrideView(
                row.Id, KindRuleExcluded, ModeName(ProcedureAssignmentEntity.ModeExcluded), null, null, policyId, request.FirmId, reason));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Reads
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The rule an Exclude names: an active Billing policy in a Billing rule set that is NOT record-scoped (no record scope and no core lookup:
    /// a record rule is removed, not overridden), and not the system rule (D-125; refused 409 before a rule set is required, as it has none). Read as the application; the route has already checked the
    /// caller's Read on the policy. Returns the rule's name, or the refusal.
    /// </summary>
    private async Task<(string? Name, RuleSetOutcome<OverrideView>? Refusal)> LoadRuleAsync(Guid policyId, CancellationToken ct)
    {
        var query = new QueryExpression(RuleSetService.PolicyEntity)
        {
            ColumnSet = new ColumnSet("sprk_name", "sprk_policycode", "sprk_ruleset", "sprk_currentversion", RuleDomains.PolicyDomainColumn),
            TopCount = 1,
        };
        query.Criteria.AddCondition("sprk_policyid", ConditionOperator.Equal, policyId);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        RuleDomains.AddBillingFilter(query);
        var policy = (await _entities.RetrieveMultipleAsync(query, ct)).Entities.FirstOrDefault(p => p.Id == policyId && RuleDomains.IsBillingRow(p));
        // The system rule first, BEFORE a rule set is required: it belongs to no rule set (Seed-BillingSystemPolicy.ps1), so the set check
        // below would answer it "not found". The caller's Read on the rule has already been checked by the route.
        if (policy is not null && await IsSystemRuleAsync(policy, policyId, ct))
        {
            return (null, Refused<OverrideView>(StatusCodes.Status409Conflict, CodeSystemRule, "This is a system rule; it cannot be excluded for a record."));
        }

        var set = policy?.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id is { } setId
            ? await LoadByIdAsync(RuleSetService.RuleSetEntity,
                setId, new[] { "sprk_scope", RuleDomains.PolicyDomainColumn }.Concat(InvoiceFamilyCoreRecordGuard.FilingCoreTables).ToArray(), ct)
            : null;
        if (policy is null || set is null || !RuleDomains.IsBillingRow(set) || IsRecordScoped(set))
        {
            return (null, Refused<OverrideView>(StatusCodes.Status404NotFound, CodeRuleNotFound, "The rule was not found."));
        }

        return (policy.GetAttributeValue<string>("sprk_name") ?? policy.GetAttributeValue<string>("sprk_policycode") ?? policyId.ToString(), null);
    }

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

    /// <summary>
    /// True for a rule set that holds one record's rules: a record scope (Record or Firm record), or any core-record lookup. Either alone
    /// counts, so a set carrying a core lookup without its scope still reads as record-scoped (fail closed; the lineage files a set under its
    /// core lookup alone).
    /// </summary>
    private static bool IsRecordScoped(Entity set) =>
        set.GetAttributeValue<OptionSetValue>("sprk_scope")?.Value is ScopeRecord or ScopeFirmRecord
        || InvoiceFamilyCoreRecordGuard.FilingCoreTables.Any(core => set.GetAttributeValue<EntityReference>(core) is not null);

    /// <summary>True when the record already holds an override row naming <paramref name="subjectId"/> in <paramref name="subjectColumn"/> (I-A1).</summary>
    private async Task<bool> HasRowAsync(string coreTable, Guid recordId, string subjectColumn, Guid subjectId, CancellationToken ct)
    {
        var query = new QueryExpression(ProcedureAssignmentEntity.Entity) { ColumnSet = new ColumnSet(subjectColumn), TopCount = 1 };
        query.Criteria.AddCondition(coreTable, ConditionOperator.Equal, recordId);
        query.Criteria.AddCondition(subjectColumn, ConditionOperator.Equal, subjectId);
        return (await _entities.RetrieveMultipleAsync(query, ct)).Entities.Count > 0;
    }

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

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // The write
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The columns of one override row (a Procedure-level row names the Procedure; a rule-level row names the rule, never both).</summary>
    private sealed record AssignmentRow(
        Guid Id, string CoreTable, Guid RecordId, string Name, int Mode, Guid? ProcedureId, Guid? PinnedVersionId, Guid? ExcludedPolicyId,
        Guid? FirmId, string? Reason);

    /// <summary>
    /// Builds the row, asks its owner (the row's own question, with the caller as RequestedBy), writes the owner onto it and creates it, in
    /// one member so the census sees the row, its owner write and its create together. Returns null when the row was created, else the
    /// refusal. A refused owner writes nothing.
    /// </summary>
    private async Task<RuleSetOutcome<OverrideView>?> CreateRowAsync(AssignmentRow spec, Guid? callerObjectId, string what, CancellationToken ct)
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
            _logger.LogWarning("Override {AssignmentId}: the {What} was refused, no owner ({RefusalCode}); nothing was written",
                spec.Id, what, owner.RefusalCode);
            return RuleSetOutcome<OverrideView>.OwnerRefused(owner);
        }

        owner.ApplyTo(row);
        return await SendAsync(new OrganizationRequest[] { new CreateRequest { Target = row } }, spec.Id, what) switch
        {
            SendResult.Done => null,
            SendResult.Duplicate => Duplicate<OverrideView>(),
            SendResult.Conflict => Refused<OverrideView>(StatusCodes.Status409Conflict, CodeConcurrentEdit,
                "The record's overrides changed while this was being saved, so nothing was written. Reload and try again."),
            // Never "nothing was saved" here: a retry could find its own row. The id lets the caller look before retrying.
            SendResult.Unknown => Refused<OverrideView>(StatusCodes.Status500InternalServerError, CodeWriteFailed,
                $"The override may or may not have been saved (id {spec.Id}). Reload the record before trying again."),
            _ => Refused<OverrideView>(StatusCodes.Status500InternalServerError, CodeWriteFailed,
                "The override could not be saved and nothing was written. Try again; if it keeps failing, contact support."),
        };
    }

    private enum SendResult
    {
        Done,
        Conflict,
        Duplicate,
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
