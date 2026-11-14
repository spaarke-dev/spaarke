// ebilling-r1 task 095g (EB-D-224, EB-D-233..235; notes/095g-assign-firm.md "Design for uac-r2 review", uac-r2 R1/R2 and the Q2/Q3 conditions).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — the Matter Management form and the generic update tools write sprk_assignedlawfirm1/2 with no slot rules; the
//       assigned-access sync (POST /api/v1/external-access/assigned-access/sync) takes no input; InvoiceAssignFirmService sets an INVOICE's firm.
//   (2) Extension — none of them can carry the slot rules, the Restricted / Secure / Limited refusal, the law-firm rule or the revoke
//       confirmation without becoming a second owner of the slots; the sync and the materializer decide access, not the slots.
//   (3) Cost of doing nothing — the Outlook pane has no way to assign a firm (EB-D-224 cannot be built), and uac-r2 condition 4 (one
//       server-side writer of the slots for this act) is unmet.
//
// Placement (bff-extensions.md): BFF, Services/Finance (caller identity, live pane request that runs the BFF-owned materializer in-request).
// Concrete (ADR-010), scoped like its scoped dependencies. No package, no background work, no new column.

using System.Data;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The stable code of the "Law Firm" organization type (EB-D-234): <c>sprk_organizationtype_ref.sprk_organizationtypecode</c>. The ONE value
/// the server matches and <c>scripts/ebilling/Set-EbillingLawFirmTypeCode.ps1</c> seeds (pinned equal by a test). Never an id or a name.
/// </summary>
public static class LawFirmOrganizationType
{
    /// <summary>The code on the Law Firm type row.</summary>
    public const string Code = "LAWFIRM";

    internal const string TypeEntity = "sprk_organizationtype_ref";
    internal const string TypeIdColumn = "sprk_organizationtype_refid";
    internal const string TypeCodeColumn = "sprk_organizationtypecode";

    /// <summary><c>sprk_organization.sprk_organizationtype</c> (lookup to <see cref="TypeEntity"/>).</summary>
    internal const string OrganizationTypeColumn = "sprk_organizationtype";
}

/// <summary>Stable codes of the firm-assign routes (ADR-019). Every 409 and 422 is prefixed <c>ebilling.firm_assign.</c>.</summary>
public static class CoreRecordFirmCodes
{
    public const string RecordRestricted = "ebilling.firm_assign.record_restricted";
    public const string RecordSecure = "ebilling.firm_assign.record_secure";
    public const string RecordLimited = "ebilling.firm_assign.record_limited";
    public const string FirmInactive = "ebilling.firm_assign.firm_inactive";
    public const string NotALawFirm = "ebilling.firm_assign.not_a_law_firm";
    public const string FirmNoAccess = "ebilling.firm_assign.firm_no_access";
    public const string SlotsFull = "ebilling.firm_assign.slots_full";
    public const string FirmAlreadyAssigned = "ebilling.firm_assign.firm_already_assigned";
    public const string RecordChanged = "ebilling.firm_assign.record_changed";
    public const string RevokeConfirmationRequired = "ebilling.firm_assign.revoke_confirmation_required";

    /// <summary>503: a read or write could not be completed (the submission routes' read-fault code; nothing was written by this call).</summary>
    public const string Unavailable = InvoiceSubmissionErrors.CoreRecordUnavailable;
}

/// <summary>A firm by id and display name.</summary>
public sealed record FirmRef(Guid FirmId, string Name);

/// <summary>One filled slot: 1 (<c>sprk_assignedlawfirm1</c>) or 2 (<c>sprk_assignedlawfirm2</c>).</summary>
public sealed record AssignedFirmSlot(int Slot, Guid FirmId, string Name, bool Active);

/// <summary>What the access pass did: <c>applied</c> (complete) or <c>pending</c> (the slot write stands; a repeat finishes the pass).</summary>
public sealed record FirmAccessEffect(string State, IReadOnlyList<FirmRef> Granted, IReadOnlyList<FirmRef> Revoked);

/// <summary>
/// The 200 body of <c>POST /api/finance/core-records/{entityType}/{entityId}/firms</c>. <see cref="Firms"/> is the section 8 GET body after
/// the write (null when that read faulted after a successful write). <see cref="Notice"/> is the access effect of an add (uac-r2 R2, Q2).
/// </summary>
public sealed record CoreRecordFirmAssignResponse(
    bool Changed,
    IReadOnlyList<CoreRecordFirm>? Firms,
    IReadOnlyList<AssignedFirmSlot> Assigned,
    FirmAccessEffect Access,
    bool RecordActive,
    string? Notice);

/// <summary>One page of <c>GET /api/finance/firms</c>.</summary>
public sealed record FirmListResponse(IReadOnlyList<FirmRef> Items, int Page, bool HasMore);

/// <summary>The answer of <see cref="CoreRecordFirmAssignmentService.AssignFirmAsync"/>: a 200 body, or a refusal the endpoint renders.</summary>
public sealed record CoreRecordFirmAssignResult(
    int Status,
    string? Code,
    string? Detail,
    CoreRecordFirmAssignResponse? Body = null,
    IReadOnlyList<AssignedFirmSlot>? Assigned = null,
    IReadOnlyList<FirmRef>? Revokes = null)
{
    internal static CoreRecordFirmAssignResult Ok(CoreRecordFirmAssignResponse body) => new(StatusCodes.Status200OK, null, null, body);

    /// <summary>The uniform 404: unknown, gone or not available to the caller (one answer for all of them).</summary>
    internal static CoreRecordFirmAssignResult NotFound() => new(StatusCodes.Status404NotFound, null, null);

    internal static CoreRecordFirmAssignResult Unavailable() =>
        new(StatusCodes.Status503ServiceUnavailable, CoreRecordFirmCodes.Unavailable, CoreRecordFirmTexts.Unavailable);

    internal static CoreRecordFirmAssignResult Refused(int status, string code, string detail,
        IReadOnlyList<AssignedFirmSlot>? assigned = null, IReadOnlyList<FirmRef>? revokes = null) =>
        new(status, code, detail, null, assigned, revokes);
}

/// <summary>The answer of <see cref="CoreRecordFirmAssignmentService.ListAsync"/>: a page, or 503 (never an empty or partial list on a fault).</summary>
public sealed record FirmListResult(FirmListResponse? Page)
{
    public bool Unavailable => Page is null;
}

/// <summary>The user text of each refusal (contract section 7 of the 095g notes).</summary>
internal static class CoreRecordFirmTexts
{
    internal const string Unavailable = "Spaarke couldn't do this right now. Try again.";
    internal const string RecordRestricted = "This record is Restricted to internal users, so no firm can be given access to it.";
    internal const string RecordSecure = "This record is secure, so assigning a firm would give it no access. Use Manage Access to add named people.";
    internal const string RecordLimited = "This record is limited, so assigning a firm would give it no access. Use Manage Access to add named people.";
    internal const string FirmInactive = "This firm is inactive. Choose another firm.";
    internal const string NotALawFirm = "This organization is not a law firm. Choose a law firm.";
    internal const string FirmNoAccess = "This firm can't be given access to this record.";
    internal const string SlotsFull = "Two firms are already assigned. Replace one of them.";
    internal const string FirmAlreadyAssigned = "That firm is already assigned to this record.";
    internal const string RecordChanged = "The firms on this record changed. Check them and try again.";
    internal const string RevokeConfirmationRequired = "Removing this firm takes away the access this assignment gave its people. Confirm to continue.";

    /// <summary>uac-r2 R2: the access effect of an add on an active record.</summary>
    internal static string AddNotice(string firm) => $"{firm}'s people will get access to this record.";

    /// <summary>uac-r2 Q2 condition 1: on an inactive record the add grants nothing now; it never says they have access.</summary>
    internal static string AddNoticeWhenInactive(string firm) => $"{firm}'s people will get access to this record when it is active again.";
}

/// <summary>What <see cref="FirmSlotPlan.Decide"/> concluded.</summary>
internal enum FirmSlotPlanKind
{
    /// <summary>The stored slots already say what was asked (a repeat): nothing is written.</summary>
    NoOp,

    /// <summary>Write <see cref="FirmSlotPlan.Changes"/> in one conditional update.</summary>
    Write,

    /// <summary>Refused with <see cref="FirmSlotPlan.RefusalCode"/>; nothing is written.</summary>
    Refused,
}

/// <summary>
/// The pure slot rules (design section 2; EB-D-235 Q8): an add fills slot 1, then slot 2; a replace takes the slot that holds the firm it
/// replaces; a clear empties the slot and never moves the other firm up. Ids are firm ids, never slot numbers.
/// </summary>
internal sealed record FirmSlotPlan(FirmSlotPlanKind Kind, IReadOnlyDictionary<int, Guid?> Changes, Guid? Revokes, string? RefusalCode)
{
    private static readonly IReadOnlyDictionary<int, Guid?> None = new Dictionary<int, Guid?>();

    private static FirmSlotPlan NoOp() => new(FirmSlotPlanKind.NoOp, None, null, null);

    private static FirmSlotPlan Refuse(string code) => new(FirmSlotPlanKind.Refused, None, null, code);

    /// <summary>Decides the change from the STORED slots, so a repeat of a request that succeeded is a no-op.</summary>
    internal static FirmSlotPlan Decide(Guid? slot1, Guid? slot2, Guid? add, Guid? remove)
    {
        var slots = new[] { slot1, slot2 };
        var holdingRemove = remove is { } r ? Enumerable.Range(1, 2).Where(n => slots[n - 1] == r).ToList() : new List<int>();
        var addPresent = add is { } a && (slot1 == a || slot2 == a);

        if (add is { } adding && remove is null)
        {
            if (addPresent)
            {
                return NoOp();
            }

            var empty = Enumerable.Range(1, 2).FirstOrDefault(n => slots[n - 1] is null);
            return empty == 0
                ? Refuse(CoreRecordFirmCodes.SlotsFull)
                : new FirmSlotPlan(FirmSlotPlanKind.Write, new Dictionary<int, Guid?> { [empty] = adding }, null, null);
        }

        if (add is { } replacing && remove is { } replaced)
        {
            if (holdingRemove.Count == 0)
            {
                // The firm to replace is not on the record: a retry after success when the new firm is there, otherwise the record changed.
                return addPresent ? NoOp() : Refuse(CoreRecordFirmCodes.RecordChanged);
            }

            if (addPresent)
            {
                // A replace onto the firm in the other slot would hold one firm twice.
                return Refuse(CoreRecordFirmCodes.FirmAlreadyAssigned);
            }

            var changes = new Dictionary<int, Guid?> { [holdingRemove[0]] = replacing };
            foreach (var other in holdingRemove.Skip(1))
            {
                changes[other] = null; // the replaced firm leaves the record entirely; the other slot is not moved
            }

            return new FirmSlotPlan(FirmSlotPlanKind.Write, changes, replaced, null);
        }

        if (remove is { } clearing)
        {
            return holdingRemove.Count == 0
                ? NoOp()
                : new FirmSlotPlan(FirmSlotPlanKind.Write, holdingRemove.ToDictionary(n => n, _ => (Guid?)null), clearing, null);
        }

        return Refuse(CoreRecordFirmCodes.RecordChanged); // unreachable: the route refuses a body with neither (400)
    }

    /// <summary>Two plans are the same change (design 3c: a re-plan after a concurrency mismatch writes again only if identical).</summary>
    internal bool SameAs(FirmSlotPlan other) =>
        Kind == other.Kind && Revokes == other.Revokes && RefusalCode == other.RefusalCode
        && Changes.Count == other.Changes.Count && Changes.All(c => other.Changes.TryGetValue(c.Key, out var v) && v == c.Value);
}

/// <summary>
/// The ONE server-side writer of a core record's assigned law firms for the firm-assign route (ebilling-r1 task 095g; uac-r2 condition 4,
/// ADR-002). It reads app-only, decides, writes only <c>sprk_assignedlawfirm1</c> / <c>sprk_assignedlawfirm2</c> through one conditional
/// update, then runs the Assigned-To owner (<see cref="AssignedAccessMaterializer.AfterWriteAsync"/>) inline with the caller as grantor.
/// </summary>
/// <remarks>
/// <para>The caller's own rights on the record (Write, plus Append for an add) and step 0 are the route filter's, asked as the caller before
/// this runs. The caller's Read and AppendTo on the firm are asked through the delegate the endpoint passes, also as the caller.</para>
/// <para><b>Fails closed.</b> Any read fault is <see cref="CoreRecordFirmCodes.Unavailable"/> (503) with nothing written; an unknown record or
/// firm is the uniform 404, the same as a denied one. A refusal writes nothing.</para>
/// </remarks>
public sealed class CoreRecordFirmAssignmentService
{
    internal const string Slot1Column = "sprk_assignedlawfirm1";
    internal const string Slot2Column = "sprk_assignedlawfirm2";
    internal const string VersionColumn = "versionnumber";
    internal const string StateColumn = "statecode";
    internal const string OrganizationEntity = InvoiceSubmissionService.OrganizationEntity;
    internal const string OrganizationIdColumn = "sprk_organizationid";
    internal const string OrganizationNameColumn = "sprk_organizationname";

    /// <summary>The longest search text the list accepts.</summary>
    public const int SearchMaxLength = 100;

    /// <summary>The largest page the list returns.</summary>
    public const int PageSizeMax = 50;

    private static readonly string[] SlotColumns = [Slot1Column, Slot2Column];

    private readonly IGenericEntityService _entities;
    private readonly ExternalParticipationService _participations;
    private readonly IAccessibleRecordSetService _accessibleRecords;
    private readonly AssignedAccessMaterializer _materializer;
    private readonly ILogger<CoreRecordFirmAssignmentService> _logger;

    public CoreRecordFirmAssignmentService(
        IGenericEntityService entities,
        ExternalParticipationService participations,
        IAccessibleRecordSetService accessibleRecords,
        AssignedAccessMaterializer materializer,
        ILogger<CoreRecordFirmAssignmentService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _accessibleRecords = accessibleRecords ?? throw new ArgumentNullException(nameof(accessibleRecords));
        _materializer = materializer ?? throw new ArgumentNullException(nameof(materializer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GET /api/finance/firms
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One page of ACTIVE organizations of the Law Firm type (EB-D-234), app-only, sorted by name then id. A missing or ambiguous type code
    /// and any read fault answer 503 (<see cref="FirmListResult.Unavailable"/>), never an empty list.
    /// </summary>
    public async Task<FirmListResult> ListAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        try
        {
            if (await ReadLawFirmTypeIdAsync(ct).ConfigureAwait(false) is not { } typeId)
            {
                return new FirmListResult(null);
            }

            var query = new QueryExpression(OrganizationEntity)
            {
                ColumnSet = new ColumnSet(OrganizationNameColumn),
                PageInfo = new PagingInfo { Count = pageSize, PageNumber = page, ReturnTotalRecordCount = false },
            };
            query.Criteria.AddCondition(StateColumn, ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(LawFirmOrganizationType.OrganizationTypeColumn, ConditionOperator.Equal, typeId);
            if (!string.IsNullOrWhiteSpace(search))
            {
                // The user's %, _ and [ match literally (the one LIKE escape, RecordContainerResolver.EscapeForLike).
                query.Criteria.AddCondition(
                    OrganizationNameColumn, ConditionOperator.Like, "%" + RecordContainerResolver.EscapeForLike(search.Trim()) + "%");
            }

            // The ONE order of the list is the server's (name, then id), so pages join without a gap or a repeat. It is never re-sorted
            // here: an in-memory sort with another collation would reorder each page on its own (review 1, F2).
            query.AddOrder(OrganizationNameColumn, OrderType.Ascending);
            query.AddOrder(OrganizationIdColumn, OrderType.Ascending);

            var result = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            var items = result.Entities.Select(e => new FirmRef(e.Id, NameOf(e))).ToList();
            return new FirmListResult(new FirmListResponse(items, page, result.MoreRecords));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[FIRM-ASSIGN] The firm list could not be read");
            return new FirmListResult(null);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // POST /api/finance/core-records/{entityType}/{entityId}/firms
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds, replaces or clears a firm on <paramref name="record"/> (design section 2, steps 1 to 8). The route has already checked the caller
    /// (step 0, body shape, Write and, for an add, Append on the record, as the caller).
    /// </summary>
    /// <param name="callerOid">The token's own Entra oid (uac-r2 Q5): recorded as the grantor of every grant the access pass writes.</param>
    /// <param name="callerFirmRights">Whether the caller holds Read and AppendTo on the firm, asked as the caller: <c>true</c> held,
    /// <c>false</c> denied or unknown, <c>null</c> no answer (a fault).</param>
    public async Task<CoreRecordFirmAssignResult> AssignFirmAsync(
        InvoiceCoreRecord record,
        Guid? add,
        Guid? remove,
        Guid? confirmRevoke,
        string callerOid,
        Func<Guid, CancellationToken, Task<bool?>> callerFirmRights,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(callerFirmRights);
        var logical = record.EntityLogicalName;

        // ── Step 1: the record, app-only ──
        var state = await ReadRecordAsync(logical, record.RecordId, ct).ConfigureAwait(false);
        if (state.Result is { } recordRefusal)
        {
            return recordRefusal;
        }

        var current = state.Record!;
        FirmRef? addedFirm = null;

        if (add is { } firmId)
        {
            // ── Step 2: eligibility, from the EFFECTIVE flags (parents through the fold). A clear skips it: a revoke is never blocked. ──
            if (await EligibilityRefusalAsync(logical, record.RecordId, ct).ConfigureAwait(false) is { } eligibility)
            {
                return eligibility;
            }

            // ── Step 3: the caller's Read and AppendTo on the firm (unknown = denied: the uniform 404) ──
            bool? firmRights;
            try
            {
                firmRights = await callerFirmRights(firmId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[FIRM-ASSIGN] The caller's rights on firm {FirmId} could not be checked", firmId);
                firmRights = null;
            }

            if (firmRights is null)
            {
                return CoreRecordFirmAssignResult.Unavailable();
            }

            if (firmRights is false)
            {
                return CoreRecordFirmAssignResult.NotFound();
            }

            // ── Step 4: the firm row, app-only: active, and a law firm (EB-D-234) ──
            var firm = await ReadFirmAsync(firmId, ct).ConfigureAwait(false);
            if (firm.Result is { } firmRefusal)
            {
                return firmRefusal;
            }

            addedFirm = firm.Firm;

            // ── Step 5: the record's No Access list ──
            if (await NoAccessRefusalAsync(logical, record.RecordId, firmId, ct).ConfigureAwait(false) is { } noAccess)
            {
                return noAccess;
            }
        }

        // ── Steps 6 to 8: plan, confirm, write (one re-plan on a concurrency mismatch) ──
        var plan = FirmSlotPlan.Decide(current.Slot1, current.Slot2, add, remove);
        if (PlanRefusal(plan, current, confirmRevoke) is { } planRefusal)
        {
            return planRefusal;
        }

        var changed = false;
        if (plan.Kind == FirmSlotPlanKind.Write)
        {
            var write = await WriteAsync(logical, record.RecordId, current, plan, ct).ConfigureAwait(false);
            if (write == WriteOutcome.Faulted)
            {
                return CoreRecordFirmAssignResult.Unavailable();
            }

            if (write == WriteOutcome.Mismatch)
            {
                // Design 3c: re-read, re-plan once; write again only if the plan is identical, otherwise answer from the new state.
                var again = await ReadRecordAsync(logical, record.RecordId, ct).ConfigureAwait(false);
                if (again.Result is { } againRefusal)
                {
                    return againRefusal;
                }

                current = again.Record!;

                // Review 1 (F1): the record changed, so its flags may have too. An add is decided again from the effective flags before the
                // second write (a clear never is: a revoke is never blocked by flags).
                if (add is not null && await EligibilityRefusalAsync(logical, record.RecordId, ct).ConfigureAwait(false) is { } againEligibility)
                {
                    return againEligibility;
                }

                var replan = FirmSlotPlan.Decide(current.Slot1, current.Slot2, add, remove);
                if (PlanRefusal(replan, current, confirmRevoke) is { } replanRefusal)
                {
                    return replanRefusal;
                }

                if (replan.Kind == FirmSlotPlanKind.Write)
                {
                    if (!replan.SameAs(plan))
                    {
                        return RecordChanged(current);
                    }

                    write = await WriteAsync(logical, record.RecordId, current, replan, ct).ConfigureAwait(false);
                    if (write == WriteOutcome.Faulted)
                    {
                        return CoreRecordFirmAssignResult.Unavailable();
                    }

                    if (write == WriteOutcome.Mismatch)
                    {
                        return RecordChanged(current);
                    }

                    changed = true;
                }

                plan = replan;
            }
            else
            {
                changed = true;
            }
        }

        var after = changed ? current.With(plan.Changes) : current;
        if (changed)
        {
            foreach (var (slot, value) in plan.Changes)
            {
                // Q9: no record-level column; the audit line and sprk_grantedby on the grants record the person.
                _logger.LogInformation(
                    "[FIRM-ASSIGN] Caller {CallerOid} set {Entity} {RecordId} slot {Slot}: {OldFirm} -> {NewFirm}",
                    callerOid, logical, record.RecordId, slot, current.SlotValue(slot), value);
            }
        }

        // ── Access: the Assigned-To owner, inline (L1), after every successful plan, no-ops included (the pass is idempotent) ──
        var outcome = await _materializer.AfterWriteAsync(logical, record.RecordId, SlotColumns, callerOid, ct).ConfigureAwait(false);

        var names = current.Names.ToDictionary(n => n.Key, n => n.Value);
        if (addedFirm is not null)
        {
            names[addedFirm.FirmId] = (addedFirm.Name, true);
        }

        var assigned = AssignedOf(after, names);
        var access = AccessOf(outcome, add, plan.Revokes, names);
        var firms = await ReadFirmListAsync(logical, record.RecordId, ct).ConfigureAwait(false);
        return CoreRecordFirmAssignResult.Ok(new CoreRecordFirmAssignResponse(
            changed, firms, assigned, access, current.Active, NoticeOf(addedFirm, after, access, current.Active)));
    }

    // ── Step 1 ──

    /// <summary>The record's slots, version, state and the names of the firms it holds; or the uniform 404 / 503.</summary>
    private async Task<(RecordState? Record, CoreRecordFirmAssignResult? Result)> ReadRecordAsync(string logical, Guid recordId, CancellationToken ct)
    {
        Entity row;
        try
        {
            row = await _entities.RetrieveAsync(logical, recordId, [Slot1Column, Slot2Column, VersionColumn, StateColumn], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (RecordContainerResolver.IsRecordNotFound(ex) || ex.InnerException is { } inner && RecordContainerResolver.IsRecordNotFound(inner))
        {
            return (null, CoreRecordFirmAssignResult.NotFound());
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[FIRM-ASSIGN] {Entity} {RecordId} could not be read", logical, recordId);
            return (null, CoreRecordFirmAssignResult.Unavailable());
        }

        if (row is null)
        {
            return (null, CoreRecordFirmAssignResult.NotFound());
        }

        var version = row.RowVersion
            ?? (row.GetAttributeValue<long?>(VersionColumn) is { } number ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        if (string.IsNullOrWhiteSpace(version))
        {
            _logger.LogWarning("[FIRM-ASSIGN] {Entity} {RecordId} was read without a row version", logical, recordId);
            return (null, CoreRecordFirmAssignResult.Unavailable());
        }

        var slot1 = row.GetAttributeValue<EntityReference>(Slot1Column)?.Id;
        var slot2 = row.GetAttributeValue<EntityReference>(Slot2Column)?.Id;
        var active = row.GetAttributeValue<OptionSetValue>(StateColumn)?.Value == 0;

        var held = new[] { slot1, slot2 }.OfType<Guid>().Where(id => id != Guid.Empty).Distinct().ToList();
        var names = new Dictionary<Guid, (string Name, bool Active)>();
        if (held.Count > 0)
        {
            try
            {
                var query = new QueryExpression(OrganizationEntity) { ColumnSet = new ColumnSet(StateColumn, OrganizationNameColumn) };
                query.Criteria.AddCondition(OrganizationIdColumn, ConditionOperator.In, held.Cast<object>().ToArray());
                foreach (var firm in (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities)
                {
                    names[firm.Id] = (NameOf(firm), firm.GetAttributeValue<OptionSetValue>(StateColumn)?.Value == 0);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[FIRM-ASSIGN] The firms on {Entity} {RecordId} could not be read", logical, recordId);
                return (null, CoreRecordFirmAssignResult.Unavailable());
            }
        }

        return (new RecordState(slot1, slot2, version, active, names), null);
    }

    // ── Step 2 ──

    private async Task<CoreRecordFirmAssignResult?> EligibilityRefusalAsync(string logical, Guid recordId, CancellationToken ct)
    {
        RootRecordFlags flags;
        try
        {
            var effective = await _participations.GetEffectiveRootRecordFlagsAsync(logical, [recordId], ct).ConfigureAwait(false);
            if (effective is null || !effective.TryGetValue(recordId, out flags) || flags.IsUnreadable)
            {
                _logger.LogWarning("[FIRM-ASSIGN] The access flags of {Entity} {RecordId} could not be read", logical, recordId);
                return CoreRecordFirmAssignResult.Unavailable();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[FIRM-ASSIGN] The access flags of {Entity} {RecordId} could not be read", logical, recordId);
            return CoreRecordFirmAssignResult.Unavailable();
        }

        // The ONE rule (design 3f): the materializer's organization-grant skip. The record's own state is not part of it (EB-D-233).
        return EligibilityRefusal(AssignedAccessMaterializer.OrganizationGrantSkipReason(flags));
    }

    /// <summary>The route's refusal for a materializer skip reason; <c>null</c> when the flags allow an organization grant.</summary>
    internal static CoreRecordFirmAssignResult? EligibilityRefusal(string? skipReason) => skipReason switch
    {
        null => null,
        AssignedAccessReason.Restricted => CoreRecordFirmAssignResult.Refused(
            StatusCodes.Status409Conflict, CoreRecordFirmCodes.RecordRestricted, CoreRecordFirmTexts.RecordRestricted),
        AssignedAccessReason.OrganizationOnSecure => CoreRecordFirmAssignResult.Refused(
            StatusCodes.Status409Conflict, CoreRecordFirmCodes.RecordSecure, CoreRecordFirmTexts.RecordSecure),
        AssignedAccessReason.OrganizationOnLimited => CoreRecordFirmAssignResult.Refused(
            StatusCodes.Status409Conflict, CoreRecordFirmCodes.RecordLimited, CoreRecordFirmTexts.RecordLimited),
        _ => CoreRecordFirmAssignResult.Unavailable(), // a reason this route does not know: fail closed
    };

    // ── Step 4 ──

    private async Task<(FirmRef? Firm, CoreRecordFirmAssignResult? Result)> ReadFirmAsync(Guid firmId, CancellationToken ct)
    {
        Entity row;
        try
        {
            row = await _entities.RetrieveAsync(
                OrganizationEntity, firmId, [StateColumn, OrganizationNameColumn, LawFirmOrganizationType.OrganizationTypeColumn], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (RecordContainerResolver.IsRecordNotFound(ex) || ex.InnerException is { } inner && RecordContainerResolver.IsRecordNotFound(inner))
        {
            return (null, CoreRecordFirmAssignResult.NotFound());
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[FIRM-ASSIGN] Firm {FirmId} could not be read", firmId);
            return (null, CoreRecordFirmAssignResult.Unavailable());
        }

        if (row is null)
        {
            return (null, CoreRecordFirmAssignResult.NotFound());
        }

        // Q3: the law-firm type is required to exist on the environment, for every add (fail closed, never "every organization counts").
        Guid? typeId;
        try
        {
            typeId = await ReadLawFirmTypeIdAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[FIRM-ASSIGN] The Law Firm organization type could not be read");
            typeId = null;
        }

        if (typeId is null)
        {
            return (null, CoreRecordFirmAssignResult.Unavailable());
        }

        if (row.GetAttributeValue<OptionSetValue>(StateColumn)?.Value != 0)
        {
            return (null, CoreRecordFirmAssignResult.Refused(
                StatusCodes.Status422UnprocessableEntity, CoreRecordFirmCodes.FirmInactive, CoreRecordFirmTexts.FirmInactive));
        }

        if (row.GetAttributeValue<EntityReference>(LawFirmOrganizationType.OrganizationTypeColumn)?.Id != typeId)
        {
            return (null, CoreRecordFirmAssignResult.Refused(
                StatusCodes.Status422UnprocessableEntity, CoreRecordFirmCodes.NotALawFirm, CoreRecordFirmTexts.NotALawFirm));
        }

        return (new FirmRef(firmId, NameOf(row)), null);
    }

    /// <summary>
    /// The id of the ONE active type row whose code is <see cref="LawFirmOrganizationType.Code"/>; <c>null</c> (logged) when there is none or
    /// more than one. A read fault throws to the caller, which fails closed.
    /// </summary>
    private async Task<Guid?> ReadLawFirmTypeIdAsync(CancellationToken ct)
    {
        var query = new QueryExpression(LawFirmOrganizationType.TypeEntity) { ColumnSet = new ColumnSet(LawFirmOrganizationType.TypeIdColumn), TopCount = 2 };
        query.Criteria.AddCondition(LawFirmOrganizationType.TypeCodeColumn, ConditionOperator.Equal, LawFirmOrganizationType.Code);
        query.Criteria.AddCondition(StateColumn, ConditionOperator.Equal, 0);
        var rows = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;
        if (rows.Count != 1)
        {
            _logger.LogError(
                "[FIRM-ASSIGN] {Count} active organization type row(s) carry the code {Code}; exactly one is required. Firm lists and adds " +
                "are refused until the type code is set (scripts/ebilling/Set-EbillingLawFirmTypeCode.ps1).", rows.Count, LawFirmOrganizationType.Code);
            return null;
        }

        return rows[0].Id;
    }

    // ── Step 5 ──

    private async Task<CoreRecordFirmAssignResult?> NoAccessRefusalAsync(string logical, Guid recordId, Guid firmId, CancellationToken ct)
    {
        NoAccessCheckAnswer answer;
        try
        {
            answer = await _accessibleRecords.CheckGranteeNoAccessAsync(logical, recordId, null, [firmId], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[FIRM-ASSIGN] The No Access list of {Entity} {RecordId} could not be checked", logical, recordId);
            return CoreRecordFirmAssignResult.Unavailable();
        }

        return answer switch
        {
            NoAccessCheckAnswer.Allowed => null,
            NoAccessCheckAnswer.Denied => CoreRecordFirmAssignResult.Refused(
                StatusCodes.Status409Conflict, CoreRecordFirmCodes.FirmNoAccess, CoreRecordFirmTexts.FirmNoAccess),
            _ => CoreRecordFirmAssignResult.Unavailable(),
        };
    }

    // ── Steps 6 and 7 ──

    private static CoreRecordFirmAssignResult? PlanRefusal(FirmSlotPlan plan, RecordState current, Guid? confirmRevoke)
    {
        var assigned = AssignedOf(current, current.Names);
        if (plan.Kind == FirmSlotPlanKind.Refused)
        {
            return plan.RefusalCode switch
            {
                CoreRecordFirmCodes.SlotsFull => CoreRecordFirmAssignResult.Refused(
                    StatusCodes.Status409Conflict, CoreRecordFirmCodes.SlotsFull, CoreRecordFirmTexts.SlotsFull, assigned),
                CoreRecordFirmCodes.FirmAlreadyAssigned => CoreRecordFirmAssignResult.Refused(
                    StatusCodes.Status409Conflict, CoreRecordFirmCodes.FirmAlreadyAssigned, CoreRecordFirmTexts.FirmAlreadyAssigned),
                _ => RecordChanged(current),
            };
        }

        // Step 7: a write that removes a firm needs a confirmation that NAMES that firm, so a stale one cannot revoke a different firm.
        if (plan.Kind == FirmSlotPlanKind.Write && plan.Revokes is { } revoking && confirmRevoke != revoking)
        {
            return CoreRecordFirmAssignResult.Refused(
                StatusCodes.Status409Conflict, CoreRecordFirmCodes.RevokeConfirmationRequired, CoreRecordFirmTexts.RevokeConfirmationRequired,
                assigned, [new FirmRef(revoking, NameIn(current.Names, revoking))]);
        }

        return null;
    }

    private static CoreRecordFirmAssignResult RecordChanged(RecordState current) => CoreRecordFirmAssignResult.Refused(
        StatusCodes.Status409Conflict, CoreRecordFirmCodes.RecordChanged, CoreRecordFirmTexts.RecordChanged, AssignedOf(current, current.Names));

    // ── Step 8 ──

    private enum WriteOutcome
    {
        Written,
        Mismatch,
        Faulted,
    }

    private async Task<WriteOutcome> WriteAsync(string logical, Guid recordId, RecordState current, FirmSlotPlan plan, CancellationToken ct)
    {
        var fields = plan.Changes.ToDictionary(
            c => c.Key == 1 ? Slot1Column : Slot2Column,
            c => c.Value is { } firm ? (object)new EntityReference(OrganizationEntity, firm) : DBNull.Value);
        try
        {
            // Only the slot columns, app-only, conditional on the version read at step 1. No sprk_regardingrecordurl and no generic path.
            await _entities.UpdateIfRowVersionMatchesAsync(logical, recordId, current.Version, fields, ct).ConfigureAwait(false);
            return WriteOutcome.Written;
        }
        catch (DBConcurrencyException)
        {
            return WriteOutcome.Mismatch;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[FIRM-ASSIGN] The firms of {Entity} {RecordId} could not be written", logical, recordId);
            return WriteOutcome.Faulted;
        }
    }

    // ── The answer ──

    private static IReadOnlyList<AssignedFirmSlot> AssignedOf(RecordState state, IReadOnlyDictionary<Guid, (string Name, bool Active)> names)
    {
        var list = new List<AssignedFirmSlot>();
        foreach (var slot in new[] { 1, 2 })
        {
            if (state.SlotValue(slot) is { } firm)
            {
                var known = names.TryGetValue(firm, out var n);
                list.Add(new AssignedFirmSlot(slot, firm, known ? n.Name : firm.ToString(), known && n.Active));
            }
        }

        return list;
    }

    /// <summary>
    /// <c>granted</c>: the added firm when the pass left it with access through this assignment (Granted, or covered by access it already had);
    /// <c>revoked</c>: the removed firm when the pass took its automatic access away. An incomplete or missing pass is <c>pending</c>.
    /// </summary>
    private static FirmAccessEffect AccessOf(
        AssignedAccessOutcome? outcome, Guid? add, Guid? revokes, IReadOnlyDictionary<Guid, (string Name, bool Active)> names)
    {
        var state = outcome?.Complete == true ? "applied" : "pending";
        var entries = outcome?.Entries ?? [];
        var granted = add is { } a && entries.Any(e =>
                e.SubjectKind == "organization" && e.SubjectId == a
                && (e.State == AssignedAccessState.Granted.ToString() || e.State == AssignedAccessState.CoveredByExisting.ToString()
                    || e.Action == AssignedAccessAction.Granted || e.Action == AssignedAccessAction.Raised))
            ? new[] { new FirmRef(a, NameIn(names, a)) }
            : Array.Empty<FirmRef>();
        var revoked = revokes is { } r && entries.Any(e =>
                e.SubjectKind == "organization" && e.SubjectId == r && e.Action == AssignedAccessAction.Revoked)
            ? new[] { new FirmRef(r, NameIn(names, r)) }
            : Array.Empty<FirmRef>();
        return new FirmAccessEffect(state, granted, revoked);
    }

    /// <summary>
    /// uac-r2 R2 and Q2: the access effect of an add, on every add. On an inactive record it never says the firm's people have access now. On
    /// an active record it is stated when the pass granted it or is still running; otherwise (the pass decided no grant) there is no notice.
    /// </summary>
    private static string? NoticeOf(FirmRef? added, RecordState after, FirmAccessEffect access, bool recordActive)
    {
        if (added is null || (after.Slot1 != added.FirmId && after.Slot2 != added.FirmId))
        {
            return null;
        }

        if (!recordActive)
        {
            return CoreRecordFirmTexts.AddNoticeWhenInactive(added.Name);
        }

        return access.State == "pending" || access.Granted.Any(g => g.FirmId == added.FirmId)
            ? CoreRecordFirmTexts.AddNotice(added.Name)
            : null;
    }

    /// <summary>The section 8 GET body after the write; <c>null</c> when that read faulted (the pane then reloads it).</summary>
    private async Task<IReadOnlyList<CoreRecordFirm>?> ReadFirmListAsync(string logical, Guid recordId, CancellationToken ct)
    {
        try
        {
            var references = await _participations.GetReferencedOrganizationIdsAsync(logical, [recordId], ct).ConfigureAwait(false);
            if (references is null || !references.TryGetValue(recordId, out var refs) || refs.Unreadable || refs.OrganizationIds is null)
            {
                return null;
            }

            var named = await InvoiceSubmissionService.ReadActiveFirmNamesAsync(_entities, _logger, refs.OrganizationIds, ct).ConfigureAwait(false);
            return named is null ? null : CoreRecordFirm.Ordered(named);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[FIRM-ASSIGN] The firm list of {Entity} {RecordId} could not be read after the write", logical, recordId);
            return null;
        }
    }

    private static string NameOf(Entity firm) =>
        firm.GetAttributeValue<string>(OrganizationNameColumn) is { Length: > 0 } name && !string.IsNullOrWhiteSpace(name) ? name : firm.Id.ToString();

    private static string NameIn(IReadOnlyDictionary<Guid, (string Name, bool Active)> names, Guid firm) =>
        names.TryGetValue(firm, out var n) ? n.Name : firm.ToString();

    /// <summary>The record as read at step 1.</summary>
    private sealed record RecordState(Guid? Slot1, Guid? Slot2, string Version, bool Active, IReadOnlyDictionary<Guid, (string Name, bool Active)> Names)
    {
        public Guid? SlotValue(int slot) => slot == 1 ? Slot1 : Slot2;

        public RecordState With(IReadOnlyDictionary<int, Guid?> changes) => this with
        {
            Slot1 = changes.TryGetValue(1, out var one) ? one : Slot1,
            Slot2 = changes.TryGetValue(2, out var two) ? two : Slot2,
        };
    }
}
