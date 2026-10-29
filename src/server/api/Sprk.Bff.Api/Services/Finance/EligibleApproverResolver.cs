// ebilling-r1 task 103 (FR-11, FR-14, FR-35; D-33, D-40, D-54, D-78; notes 103-approval-worklist.md).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — EbillingSettingsReader holds the default approver roles per core type (025); InvoiceSubmissionPersister creates the
//       approval portion with no approvers (049, "the approvers are task 103's"); nothing reads sprk_billingapproverrole or writes the
//       sprk_invoiceapproval_eligibleapprover_contact relationship (grep: no other reader or writer).
//   (2) Extension — the settings reader is a cached singleton of one row and the persister is the one transactional creator of an
//       invoice; neither reads core records or contacts. Approve (live check), GET (recheck), the refresh job and 049's acceptance
//       hook all need the same resolution, so it lives once, here.
//   (3) Cost of doing nothing — nobody is eligible to approve an invoice (FR-35 fails closed for everyone) and the "Invoices to review"
//       view, which filters on the N:N, lists no invoice for anyone (FR-11).
//
// Placement (bff-extensions.md): BFF, Services/Finance (B1 caller identity at approve, B4 live request; the job is B2 Dataverse-heavy domain
// code on the ADR-036 host). No endpoint, no package, no AI call.

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>One eligible approver: the contact in the assignment field and the user it is linked to.</summary>
public sealed record EligibleApprover(Guid ContactId, Guid SystemUserId, string? Name);

/// <summary>
/// The eligible approvers of one core record, resolved live (FR-11, FR-14). <see cref="Approvers"/> is empty when no assignment field named
/// by the roles holds an active contact linked to a user: nobody may approve (never "anyone").
/// </summary>
/// <param name="CoreEntity">The core record's table (<c>sprk_matter</c>, <c>sprk_project</c> or <c>sprk_workassignment</c>).</param>
/// <param name="CoreId">The core record's id.</param>
/// <param name="Roles">The roles used: the record's own, or the settings default for its type when <paramref name="FromDefault"/>.</param>
/// <param name="FromDefault">The record's <c>sprk_billingapproverrole</c> was empty (D-54).</param>
/// <param name="Approvers">Ordered by name, then contact id. A person in two fields appears once.</param>
public sealed record EligibleApproverSet(
    string CoreEntity, Guid CoreId, IReadOnlyList<BillingApproverRole> Roles, bool FromDefault, IReadOnlyList<EligibleApprover> Approvers)
{
    /// <summary>No one may approve: logged by the resolver and surfaced by GET as a state flag (FR-14).</summary>
    public bool NoEligibleApprover => Approvers.Count == 0;

    /// <summary>The caller's entry (the first by contact id when two contacts link to the same user); null when the user is not eligible.</summary>
    public EligibleApprover? For(Guid systemUserId) =>
        systemUserId == Guid.Empty ? null : Approvers.Where(a => a.SystemUserId == systemUserId).OrderBy(a => a.ContactId).FirstOrDefault();
}

/// <summary>What a store of the set changed on the approval portion.</summary>
public sealed record EligibleApproverSync(int Added, int Removed, bool Stamped)
{
    public bool Changed => Added > 0 || Removed > 0;
}

/// <summary>
/// Resolves who may approve an invoice (D-33 / D-40 / D-54) and keeps the approval portion's stored set current for the view (D-78).
/// </summary>
/// <remarks>
/// <para><b>Resolution.</b> Core record → its <c>sprk_billingapproverrole</c> choices (multi-select; a single value is read too) → when empty,
/// the settings default for that core type (025) → the contact in each assignment field the roles name → active contacts with a
/// <c>sprk_systemuser</c>. A field that does not exist on the type ("Assigned To" on Matter and Project, DEF-011), an empty field, an inactive
/// contact and a contact with no user each contribute nobody. An unknown core type resolves nobody (fail closed). A read fault throws: the
/// caller decides (approve answers 503, GET reports "unavailable", the job counts it).</para>
/// <para><b>The stored set never grants.</b> The N:N exists for the "Invoices to review" view (029). Approve resolves live every time
/// (<see cref="ResolveAsync"/>); <see cref="SyncAsync"/> only mirrors the result.</para>
/// <para><b>Sync.</b> The N:N is diffed against the live set: only the missing contacts are associated and only the stale ones
/// disassociated, with <c>sprk_eligibleresolvedon</c>, in ONE transaction. <c>sprk_eligibleresolvedon</c> is written when the set changed or
/// was never stamped, so a refresh run that finds nothing new writes nothing.</para>
/// </remarks>
public sealed class EligibleApproverResolver
{
    internal const string ApprovalEntity = InvoiceSubmissionPersister.ApprovalEntity;
    internal const string ApprovalInvoice = InvoiceSubmissionPersister.ApprovalInvoice;
    internal const string ApprovalStatus = InvoiceSubmissionPersister.ApprovalStatus;
    internal const string ResolvedOnColumn = "sprk_eligibleresolvedon";

    /// <summary>The N:N (and its intersect table, which has the same name).</summary>
    public const string EligibleRelationship = "sprk_invoiceapproval_eligibleapprover_contact";

    internal const string IntersectApprovalColumn = "sprk_invoiceapprovalid";
    internal const string IntersectContactColumn = "contactid";
    internal const string RoleColumn = "sprk_billingapproverrole";
    internal const string ContactEntity = "contact";
    internal const string ContactSystemUser = "sprk_systemuser";
    internal const string ContactName = "fullname";

    private const int ContactStateActive = 0;

    /// <summary>The assignment field each role names (024's notes; all target <c>contact</c>, 021's pre-check).</summary>
    internal static readonly IReadOnlyDictionary<BillingApproverRole, string> RoleColumns = new Dictionary<BillingApproverRole, string>
    {
        [BillingApproverRole.AssignedAttorney1] = "sprk_assignedattorney1",
        [BillingApproverRole.AssignedAttorney2] = "sprk_assignedattorney2",
        [BillingApproverRole.AssignedParalegal1] = "sprk_assignedparalegal1",
        [BillingApproverRole.AssignedParalegal2] = "sprk_assignedparalegal2",
        [BillingApproverRole.AssignedToInternal] = "sprk_assignedtointernal",
        [BillingApproverRole.AssignedToExternal] = "sprk_assignedtoexternal",
        [BillingApproverRole.AssignedTo] = "sprk_assignedto",
    };

    /// <summary>
    /// The roles whose field exists on each core type (021's metadata check): <c>sprk_assignedto</c> exists on Work Assignment only, so
    /// "Assigned To" on a Matter or a Project resolves nobody (DEF-011).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<BillingApproverRole>> RolesOnCore =
        new Dictionary<string, IReadOnlyList<BillingApproverRole>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = RoleColumns.Keys.Where(r => r != BillingApproverRole.AssignedTo).ToArray(),
            ["sprk_project"] = RoleColumns.Keys.Where(r => r != BillingApproverRole.AssignedTo).ToArray(),
            ["sprk_workassignment"] = RoleColumns.Keys.ToArray(),
        };

    private readonly IGenericEntityService _entities;
    private readonly IEbillingSettingsReader _settings;
    private readonly ILogger<EligibleApproverResolver> _logger;
    private readonly TimeProvider _time;

    public EligibleApproverResolver(IGenericEntityService entities, IEbillingSettingsReader settings, ILogger<EligibleApproverResolver> logger)
        : this(entities, settings, logger, null)
    {
    }

    internal EligibleApproverResolver(
        IGenericEntityService entities, IEbillingSettingsReader settings, ILogger<EligibleApproverResolver> logger, TimeProvider? time)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The eligible approvers of the core record, read live. Throws on a read fault.</summary>
    public async Task<EligibleApproverSet> ResolveAsync(string coreEntity, Guid coreId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coreEntity);
        if (!RolesOnCore.TryGetValue(coreEntity, out var available) || coreId == Guid.Empty)
        {
            _logger.LogWarning("Eligible approvers: {CoreEntity} is not a core record type; nobody is eligible.", coreEntity);
            return new EligibleApproverSet(coreEntity, coreId, [], FromDefault: false, []);
        }

        var query = new QueryExpression(coreEntity)
        {
            ColumnSet = new ColumnSet([RoleColumn, .. available.Select(r => RoleColumns[r])]),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition(coreEntity + "id", ConditionOperator.Equal, coreId);
        var core = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        if (core is null)
        {
            _logger.LogWarning("Eligible approvers: {CoreEntity} {CoreId} could not be read; nobody is eligible.", coreEntity, coreId);
            return new EligibleApproverSet(coreEntity, coreId, [], FromDefault: false, []);
        }

        var (roles, fromDefault) = await RolesOfAsync(core, coreEntity, ct).ConfigureAwait(false);

        // Each role's contact, where the type has that field. A role whose field the type lacks contributes nobody.
        var contactRoles = new Dictionary<Guid, List<BillingApproverRole>>();
        foreach (var role in roles)
        {
            if (!available.Contains(role) || core.GetAttributeValue<EntityReference>(RoleColumns[role]) is not { } reference || reference.Id == Guid.Empty)
            {
                continue;
            }

            if (!contactRoles.TryGetValue(reference.Id, out var list))
            {
                contactRoles[reference.Id] = list = [];
            }

            list.Add(role);
        }

        var approvers = contactRoles.Count == 0 ? [] : await ReadApproversAsync(contactRoles.Keys, ct).ConfigureAwait(false);
        if (approvers.Count == 0)
        {
            // FR-14: logged and surfaced (the GET flag), never a fallback to "anyone".
            _logger.LogWarning(
                "Eligible approvers: {CoreEntity} {CoreId} has no eligible approver (roles {Roles}, default {FromDefault}, {Assigned} assigned contacts); nobody may approve.",
                coreEntity, coreId, string.Join(",", roles), fromDefault, contactRoles.Count);
        }

        return new EligibleApproverSet(coreEntity, coreId, roles, fromDefault, approvers);
    }

    /// <summary>
    /// Mirrors <paramref name="set"/> on the approval portion: associates the missing contacts, disassociates the stale ones and stamps
    /// <c>sprk_eligibleresolvedon</c> (when the set changed or was never stamped), in one transaction. Throws on a fault.
    /// </summary>
    public async Task<EligibleApproverSync> SyncAsync(Guid approvalId, EligibleApproverSet set, DateTime? resolvedOn, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(set);
        var stored = await ReadStoredAsync(approvalId, ct).ConfigureAwait(false);
        var desired = set.Approvers.Select(a => a.ContactId).ToHashSet();
        var add = desired.Where(c => !stored.Contains(c)).OrderBy(c => c).ToList();
        var remove = stored.Where(c => !desired.Contains(c)).OrderBy(c => c).ToList();
        var stamp = add.Count > 0 || remove.Count > 0 || resolvedOn is null;
        if (!stamp)
        {
            return new EligibleApproverSync(0, 0, Stamped: false);
        }

        var target = new EntityReference(ApprovalEntity, approvalId);
        var relationship = new Relationship(EligibleRelationship);
        var requests = new List<OrganizationRequest>(3);
        if (remove.Count > 0)
        {
            requests.Add(new DisassociateRequest
            {
                Target = target,
                Relationship = relationship,
                RelatedEntities = new EntityReferenceCollection(remove.Select(c => new EntityReference(ContactEntity, c)).ToList()),
            });
        }

        if (add.Count > 0)
        {
            requests.Add(new AssociateRequest
            {
                Target = target,
                Relationship = relationship,
                RelatedEntities = new EntityReferenceCollection(add.Select(c => new EntityReference(ContactEntity, c)).ToList()),
            });
        }

        requests.Add(new UpdateRequest
        {
            Target = new Entity(ApprovalEntity, approvalId) { [ResolvedOnColumn] = _time.GetUtcNow().UtcDateTime },
        });

        await _entities.ExecuteTransactionAsync(requests, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Eligible approvers of approval {ApprovalId} stored: {Added} added, {Removed} removed, {Count} eligible.",
            approvalId, add.Count, remove.Count, desired.Count);
        return new EligibleApproverSync(add.Count, remove.Count, Stamped: true);
    }

    /// <summary>
    /// 049's acceptance step (D-78 "resolved at acceptance"): resolves and stores the eligible approvers of the invoice's Pending portion
    /// while the invoice is In Review. Nothing to do (no portion, already decided, not In Review) returns false. Throws on a fault.
    /// </summary>
    public async Task<bool> RefreshInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        var invoiceQuery = new QueryExpression(InvoiceReviewReadService.InvoiceEntity)
        {
            ColumnSet = new ColumnSet("statuscode", "sprk_matter", "sprk_project", "sprk_regardingworkassignment"),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        invoiceQuery.Criteria.AddCondition(InvoiceReviewReadService.InvoiceEntity + "id", ConditionOperator.Equal, invoiceId);
        var invoice = (await _entities.RetrieveMultipleAsync(invoiceQuery, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        if (invoice?.GetAttributeValue<OptionSetValue>("statuscode")?.Value != InvoiceStatusCodes.InReview
            || InvoiceLineDecisionService.CoreOf(invoice) is not { } core)
        {
            return false;
        }

        var portions = await ReadPortionsAsync(invoiceId, ct).ConfigureAwait(false);
        if (portions.Count != 1
            || portions[0].GetAttributeValue<OptionSetValue>(ApprovalStatus)?.Value != InvoiceSubmissionPersister.ApprovalStatusPending)
        {
            return false;
        }

        var set = await ResolveAsync(core.Entity, core.Id, ct).ConfigureAwait(false);
        await SyncAsync(portions[0].Id, set, portions[0].GetAttributeValue<DateTime?>(ResolvedOnColumn), ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>The approval portions of an invoice (R1: one per invoice).</summary>
    internal async Task<IReadOnlyList<Entity>> ReadPortionsAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(ApprovalEntity)
        {
            ColumnSet = new ColumnSet(PortionColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 2,
        };
        query.Criteria.AddCondition(ApprovalInvoice, ConditionOperator.Equal, invoiceId);
        return (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.ToList();
    }

    /// <summary>The portion columns every reader of a portion needs.</summary>
    internal static readonly string[] PortionColumns =
    [
        ApprovalStatus, ResolvedOnColumn, "sprk_approvedamount", "sprk_decidedon", "sprk_decidedby", "sprk_decisionrecord",
        ApprovalInvoice, "versionnumber",
    ];

    private async Task<(IReadOnlyList<BillingApproverRole> Roles, bool FromDefault)> RolesOfAsync(Entity core, string coreEntity, CancellationToken ct)
    {
        var raw = core.Contains(RoleColumn) ? core[RoleColumn] : null;
        var values = raw switch
        {
            OptionSetValueCollection many => many.Select(v => v.Value).ToList(),
            OptionSetValue one => [one.Value],
            _ => new List<int>(),
        };

        if (values.Count == 0)
        {
            var settings = await _settings.GetAsync(ct).ConfigureAwait(false);
            var defaults = coreEntity.ToLowerInvariant() switch
            {
                "sprk_matter" => settings.MatterApproverRoles,
                "sprk_project" => settings.ProjectApproverRoles,
                _ => settings.WorkAssignmentApproverRoles,
            };
            return (defaults.Distinct().ToList(), true);
        }

        var known = values.Where(v => Enum.IsDefined(typeof(BillingApproverRole), v)).Select(v => (BillingApproverRole)v).Distinct().ToList();
        if (known.Count != values.Count)
        {
            // A value the global choice does not define names no field: it contributes nobody (never the default).
            _logger.LogWarning("Eligible approvers: {CoreEntity} {CoreId} holds {Unknown} unknown approver role values; they name no field.",
                coreEntity, core.Id, values.Count - known.Count);
        }

        return (known, false);
    }

    private async Task<IReadOnlyList<EligibleApprover>> ReadApproversAsync(IEnumerable<Guid> contactIds, CancellationToken ct)
    {
        var query = new QueryExpression(ContactEntity)
        {
            ColumnSet = new ColumnSet(ContactName, ContactSystemUser),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(ContactEntity + "id", ConditionOperator.In, contactIds.Cast<object>().ToArray());
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, ContactStateActive);
        var rows = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;

        return rows
            .Select(r => (Contact: r, User: r.GetAttributeValue<EntityReference>(ContactSystemUser)))
            .Where(x => x.User is { } user && user.Id != Guid.Empty)
            .Select(x => new EligibleApprover(x.Contact.Id, x.User!.Id, x.Contact.GetAttributeValue<string>(ContactName)))
            .DistinctBy(a => a.ContactId)
            .OrderBy(a => a.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.ContactId)
            .ToList();
    }

    private async Task<HashSet<Guid>> ReadStoredAsync(Guid approvalId, CancellationToken ct)
    {
        var query = new QueryExpression(EligibleRelationship)
        {
            ColumnSet = new ColumnSet(IntersectContactColumn),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(IntersectApprovalColumn, ConditionOperator.Equal, approvalId);
        var rows = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;
        return rows
            .Select(r => r.Contains(IntersectContactColumn) && r[IntersectContactColumn] is Guid id ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
    }
}
