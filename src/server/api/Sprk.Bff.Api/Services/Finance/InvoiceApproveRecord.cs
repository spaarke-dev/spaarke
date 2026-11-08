// ebilling-r1 task 165b-4 (EB-D-208; ontology-r1 option (b), 2026-11-08): the one keyed match of an invoice to its approve Decision Record.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceApprovalService.InvoicesWithApproveRecordAsync (task 165b-3 part B), an invoice-id-scoped internal of a DI service,
//       used by approval, AP export and Mark Paid.
//   (2) Extension — the static spend reader FinanceBaseValues (ontology-r1's, O1: static and pure) cannot call into a DI service, and the
//       matcher had no parent scope. The body moves here unchanged, InvoicesWithApproveRecordAsync delegates to it, and a parent scope is added.
//   (3) Cost of doing nothing — spend would copy the key match, so approval, AP export and the three spend readers could disagree on which
//       invoice is approved.
//
// Placement (bff-extensions.md): BFF, Services/Finance (a pure static class: no DI registration, no endpoint, no package).

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The ONE rule for "this invoice has its approve Decision Record" (EB-D-208, uac-r2 F1 on 165b-3): a record the writer identity created
/// (<see cref="WriterCreatedRecord"/>), regarding the invoice, action <c>approve-invoice</c>, whose key (<see cref="InvoiceLineOrigin.ReviewIdOf"/>)
/// equals <see cref="BillingDecisionKey.IdOf"/>(<c>sprk_invoice</c>, invoice, <see cref="InvoiceApprovalService.DecisionIdFor"/>(a portion of that
/// invoice)). An invoice with no portion never matches (fail closed).
/// </summary>
/// <remarks>
/// <para><b>Callers.</b> Approval, AP export and Mark Paid (through <see cref="InvoiceApprovalService.InvoicesWithApproveRecordAsync"/>, invoice-id
/// scope) and the three spend readers (<see cref="FinanceBaseValues.ReadCountedInvoicesAsync"/>, parent scope): the evaluator's
/// <see cref="FinanceBaseValues.ReadAsync"/>, <see cref="FinanceRollupService"/> and the finance tool. None of them spells the key or the writer
/// conditions itself.</para>
/// <para><b>Reads.</b> Two per call (one per chunk of <see cref="FinanceBaseValues.ParentChunkSize"/> ids in the invoice-id scope), both paged and
/// app-only through the <see cref="IGenericEntityService"/> the caller passes: the portions, then the writer's approve records (skipped when no
/// candidate has a portion). Never one query per invoice. A read fault throws; callers fail their decision closed.</para>
/// <para><b>Allow-list.</b> The match only keeps candidates the caller already holds: a portion or record of another invoice is ignored, so the
/// reads can remove an invoice from the caller's set, never add one.</para>
/// </remarks>
public static class InvoiceApproveRecord
{
    /// <summary>The approve record's action code (the same constant approval writes the record with).</summary>
    internal const string ActionCode = InvoiceApprovalService.ApproveActionCode;

    /// <summary>The Decision Record action-code column.</summary>
    internal const string ActionCodeColumn = "sprk_actioncode";

    /// <summary>The approve record's invoice lookup, as the Decision Record writer files a record regarding an invoice.</summary>
    internal static readonly string InvoiceLookup =
        TodoRegardingBuilder.RegardingLookupsByHostTable[InvoiceReviewReadService.DecisionRecordEntity][InvoiceReviewReadService.InvoiceEntity];

    private const int PageSize = 5000;

    /// <summary>
    /// Invoice-id scope (task 165b-3 behaviour, unchanged): the invoices among <paramref name="invoiceIds"/> that have their approve record. Per
    /// chunk of <see cref="FinanceBaseValues.ParentChunkSize"/> invoices, at most TWO reads, each an <c>In</c> on the ids. The writer id is read
    /// once, before any read; a fault propagates.
    /// </summary>
    internal static async Task<IReadOnlySet<Guid>> MatchedInvoicesAsync(
        IGenericEntityService entities, IReadOnlyCollection<Guid> invoiceIds, Func<CancellationToken, Task<Guid>> writerUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(invoiceIds);
        ArgumentNullException.ThrowIfNull(writerUserId);
        var found = new HashSet<Guid>();
        if (invoiceIds.Count == 0)
        {
            return found;
        }

        var writer = await writerUserId(ct).ConfigureAwait(false);
        foreach (var chunk in invoiceIds.Distinct().Chunk(FinanceBaseValues.ParentChunkSize))
        {
            // (1) The approval keys: one per portion of each invoice of the chunk.
            var portionQuery = PortionQuery();
            portionQuery.Criteria.AddCondition(EligibleApproverResolver.ApprovalInvoice, ConditionOperator.In, chunk.Cast<object>().ToArray());
            var keys = Keys(await InvoiceReviewReadService.ReadPagedAsync(entities, portionQuery, ct).ConfigureAwait(false), chunk.ToHashSet());
            if (keys.Count == 0)
            {
                continue;
            }

            // (2) The writer-created approve records of the invoices that have a portion, counted only under the approval's own key.
            var recordQuery = RecordQuery(writer, new ConditionExpression(InvoiceLookup, ConditionOperator.In, keys.Keys.Cast<object>().ToArray()));
            found.UnionWith(Matched(keys, await InvoiceReviewReadService.ReadPagedAsync(entities, recordQuery, ct).ConfigureAwait(false)));
        }

        return found;
    }

    /// <summary>
    /// Parent scope (task 165b-4, the spend readers): the invoices of <paramref name="snapshot"/> that have their approve record. ONE chunk of
    /// parents (the caller chunks by <see cref="FinanceBaseValues.ParentChunkSize"/>); at most TWO reads, each scoped through an inner link to the
    /// parents' invoices of <paramref name="statuses"/>, so the count does not grow with the number of invoices (no <c>In</c> over invoice ids;
    /// paging adds a read per 5,000 rows only). The parent scope only narrows the candidates: matching stays by invoice id, key and writer
    /// conditions, and only an invoice in <paramref name="snapshot"/> can be returned.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="writerUserId"/> is <see cref="Guid.Empty"/> (<see cref="WriterCreatedRecord"/>).</exception>
    internal static async Task<IReadOnlySet<Guid>> MatchedInvoicesAsync(
        IGenericEntityService entities, string parentEntity, IReadOnlyCollection<Guid> parentIds, IReadOnlyList<int> statuses,
        IReadOnlySet<Guid> snapshot, Guid writerUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(parentIds);
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentNullException.ThrowIfNull(snapshot);
        // Built first: an unknown writer refuses here, before any read, so it never becomes "no record" (S4).
        var recordQuery = RecordQuery(writerUserId, invoiceCondition: null);
        if (snapshot.Count == 0 || parentIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var portionQuery = PortionQuery();
        AddInvoiceScope(portionQuery, EligibleApproverResolver.ApprovalInvoice, parentEntity, parentIds, statuses);
        var keys = Keys(await InvoiceReviewReadService.ReadPagedAsync(entities, portionQuery, ct).ConfigureAwait(false), snapshot);
        if (keys.Count == 0)
        {
            return new HashSet<Guid>();
        }

        AddInvoiceScope(recordQuery, InvoiceLookup, parentEntity, parentIds, statuses);
        return Matched(keys, await InvoiceReviewReadService.ReadPagedAsync(entities, recordQuery, ct).ConfigureAwait(false));
    }

    /// <summary>The portion read (<c>sprk_invoiceapproval</c>: its invoice lookup), paged, NoLock, ordered for stable paging. Scope is added by the caller.</summary>
    internal static QueryExpression PortionQuery()
    {
        var query = new QueryExpression(EligibleApproverResolver.ApprovalEntity)
        {
            ColumnSet = new ColumnSet(EligibleApproverResolver.ApprovalInvoice),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
            NoLock = true,
        };
        query.AddOrder(EligibleApproverResolver.ApprovalEntity + "id", OrderType.Ascending);
        return query;
    }

    /// <summary>
    /// The writer's approve-record read (<c>sprk_decisionrecord</c>: the invoice lookup and <c>sprk_steps</c>): <paramref name="invoiceCondition"/>
    /// first when given, then the action code and <see cref="WriterCreatedRecord"/>'s conditions. Paged, NoLock, ordered.
    /// </summary>
    internal static QueryExpression RecordQuery(Guid writerUserId, ConditionExpression? invoiceCondition)
    {
        var query = new QueryExpression(InvoiceReviewReadService.DecisionRecordEntity)
        {
            ColumnSet = new ColumnSet(InvoiceLookup, InvoiceLineOrigin.RecordSteps),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
            NoLock = true,
        };
        if (invoiceCondition is not null)
        {
            query.Criteria.AddCondition(invoiceCondition);
        }

        query.Criteria.AddCondition(ActionCodeColumn, ConditionOperator.Equal, ActionCode);
        WriterCreatedRecord.AddConditions(query.Criteria, writerUserId);
        query.AddOrder(InvoiceReviewReadService.DecisionRecordEntity + "id", OrderType.Ascending);
        return query;
    }

    /// <summary>The approval keys per invoice of <paramref name="candidates"/>: one per portion (pure). A portion of another invoice is ignored.</summary>
    internal static Dictionary<Guid, HashSet<Guid>> Keys(IEnumerable<Entity> portions, IReadOnlySet<Guid> candidates)
    {
        var keys = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var portion in portions)
        {
            if (portion.Id != Guid.Empty
                && portion.GetAttributeValue<EntityReference>(EligibleApproverResolver.ApprovalInvoice)?.Id is { } invoiceId
                && candidates.Contains(invoiceId))
            {
                if (!keys.TryGetValue(invoiceId, out var set))
                {
                    keys[invoiceId] = set = [];
                }

                set.Add(BillingDecisionKey.IdOf(InvoiceReviewReadService.InvoiceEntity, invoiceId, InvoiceApprovalService.DecisionIdFor(portion.Id)));
            }
        }

        return keys;
    }

    /// <summary>The invoices with a record under one of their own keys (pure). A record without a readable key, or under another key, never matches.</summary>
    internal static HashSet<Guid> Matched(IReadOnlyDictionary<Guid, HashSet<Guid>> keys, IEnumerable<Entity> records)
    {
        var found = new HashSet<Guid>();
        foreach (var row in records)
        {
            if (row.GetAttributeValue<EntityReference>(InvoiceLookup)?.Id is { } invoiceId
                && keys.TryGetValue(invoiceId, out var approvalKeys)
                && InvoiceLineOrigin.ReviewIdOf(row) is { } key
                && approvalKeys.Contains(key))
            {
                found.Add(invoiceId);
            }
        }

        return found;
    }

    /// <summary>Scopes <paramref name="query"/> to the parents' invoices of <paramref name="statuses"/>: an inner link from <paramref name="invoiceLookup"/>.</summary>
    private static void AddInvoiceScope(QueryExpression query, string invoiceLookup, string parentEntity, IReadOnlyCollection<Guid> parentIds, IReadOnlyList<int> statuses)
    {
        var link = query.AddLink(FinanceBaseValues.InvoiceEntity, invoiceLookup, FinanceBaseValues.InvoiceKeyColumn, JoinOperator.Inner);
        link.Columns = new ColumnSet(false);
        link.LinkCriteria.AddCondition(FinanceBaseValues.ParentLookup(parentEntity), ConditionOperator.In, parentIds.Cast<object>().ToArray());
        link.LinkCriteria.AddCondition(FinanceBaseValues.InvoiceStatusCodeColumn, ConditionOperator.In, statuses.Cast<object>().ToArray());
    }
}
