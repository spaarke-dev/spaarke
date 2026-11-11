using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Services.Finance.Procedures;

/// <summary>
/// The ONE count of "open invoices you can see" behind a rule-closing action (ebilling-r1 task 081; procedure model section 15 answer 2):
/// from a set of open billing Signals, the invoices they are about that are still open (In Review), narrowed to the ones the CALLER can Read.
/// The Procedure retire preview and result use it, and task 089's override-removal preview and result call it unchanged.
/// </summary>
/// <remarks>
/// <para><b>Counted as the caller, never app-only (uac-r2, ontology-r1 v5.9).</b> The Signal set and the invoice states are read as the
/// application (they are the action's own scope); the number is narrowed by the caller's own rights, asked of Dataverse as the caller in one
/// batched <see cref="CallerRecordAccessProbe.GetCallerRightsForRecordsOrUnknownAsync"/> call per page of invoices. The result never says how many
/// invoices were left out. An app-only count would tell a billing admin how many invoices exist under Secure or Restricted records they cannot
/// read.</para>
/// <para><b>Fail closed to "no count".</b> If the caller cannot be established (<see cref="CallerRecordAccessProbe.GetCallerSystemUserIdAsync"/>
/// answers null: no token, OBO failure, WhoAmI empty), if the rights call throws, or if any app read of the invoices faults, the answer is
/// <c>null</c>: the caller sees the warning with no number, never the app-only one. So is ANY record whose rights question was not
/// answered (unknown: a 429, a 5xx, a failed exchange or <c>WhoAmI</c>; uac-r2 review F1, 2026-10-22): a number is shown only when every
/// invoice was answered, so the count is never lower than what the caller can see because Dataverse was throttling. A 403 is an answer
/// (not visible).</para>
/// <para><b>Open invoice.</b> <c>statecode</c> Active and <c>statuscode</c> In Review (<see cref="InvoiceStatusCodes.InReview"/>): the invoices
/// the D-170 close acts on (model 15 answer 2 "on In Review invoices"). A pending invoice has no evaluation yet, so it has no billing Signal.
/// A Signal about an invoice LINE (<c>sprk_regardinginvoiceline</c>) counts its line's invoice (<c>sprk_billingevent.sprk_invoice</c>); an
/// invoice is counted once however many of its Signals are in the set.</para>
/// <para>§11: <i>Existing</i> — the probe answers rights per record; nothing maps Signals to open invoices. <i>Extension</i> — the probe is
/// extended by one batched method that keeps "unknown" apart from "no access" (uac-r2 F1; the existing method is unchanged); this is
/// the counting rule the model asks to be shared by 081 and 089, so it is one class, not two copies. <i>Cost of doing nothing</i>
/// — the retire confirmation would have no count, or 089 would copy the rule and the two could disagree.</para>
/// </remarks>
public class AffectedInvoiceCounter
{
    /// <summary>The invoices per batched rights call (a page): bounded so one call never fans out unboundedly (the probe runs 4 at a time).</summary>
    internal const int RightsPageSize = 250;

    internal const string InvoiceEntity = "sprk_invoice";
    internal const string LineEntity = "sprk_billingevent";
    private const int MaxInValues = 500;

    private readonly IGenericEntityService _entities;
    private readonly CallerRecordAccessProbe _probe;
    private readonly ILogger<AffectedInvoiceCounter> _logger;

    public AffectedInvoiceCounter(IGenericEntityService entities, CallerRecordAccessProbe probe, ILogger<AffectedInvoiceCounter> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// How many open invoices the Signals in <paramref name="signals"/> are about that the caller (<paramref name="callerBearerToken"/>) can Read;
    /// <c>null</c> when that cannot be told (see the remarks: the caller then sees no number). Reads only.
    /// </summary>
    internal virtual async Task<int?> CountVisibleOpenInvoicesAsync(
        IReadOnlyCollection<OpenPolicySignal> signals, string? callerBearerToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signals);

        HashSet<Guid> open;
        try
        {
            var invoices = signals.Where(s => s.RegardingInvoiceId is { } id && id != Guid.Empty).Select(s => s.RegardingInvoiceId!.Value).ToHashSet();
            var lines = signals.Where(s => s.RegardingInvoiceLineId is { } id && id != Guid.Empty).Select(s => s.RegardingInvoiceLineId!.Value).Distinct().ToList();
            invoices.UnionWith(await InvoicesOfLinesAsync(lines, ct).ConfigureAwait(false));
            open = await OpenInvoicesAsync(invoices, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Affected-invoice count: the invoices of {SignalCount} Signal(s) could not be read; no count is shown",
                signals.Count);
            return null;
        }

        if (open.Count == 0)
        {
            return 0;
        }

        // The caller first: a caller who cannot be established would get None for every record, which must read as "no count", not as 0.
        if (await _probe.GetCallerSystemUserIdAsync(callerBearerToken, ct).ConfigureAwait(false) is not { } caller || caller == Guid.Empty)
        {
            _logger.LogWarning("Affected-invoice count: the caller could not be established; no count is shown");
            return null;
        }

        var visible = 0;
        try
        {
            foreach (var page in open.OrderBy(id => id).Chunk(RightsPageSize))
            {
                var rights = await _probe.GetCallerRightsForRecordsOrUnknownAsync(
                    callerBearerToken, page.Select(id => (FinanceAuthorizationFilter.InvoiceEntitySet, id)).ToList(), ct).ConfigureAwait(false);
                if (rights.Count != page.Length || rights.Any(r => r is null))
                {
                    _logger.LogWarning("Affected-invoice count: the caller's rights on {Unknown} of {PageCount} invoice(s) were not answered; no count is shown",
                        rights.Count(r => r is null) + Math.Max(0, page.Length - rights.Count), page.Length);
                    return null;
                }

                visible += rights.Count(r => (r!.Value & AccessRights.Read) == AccessRights.Read);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Affected-invoice count: the caller's rights could not be read; no count is shown");
            return null;
        }

        return visible;
    }

    /// <summary>The invoice of each line (<c>sprk_billingevent.sprk_invoice</c>), app-only. A fault throws.</summary>
    private async Task<IEnumerable<Guid>> InvoicesOfLinesAsync(IReadOnlyList<Guid> lineIds, CancellationToken ct)
    {
        var invoices = new List<Guid>();
        foreach (var batch in lineIds.Select(id => (object)id).Chunk(MaxInValues))
        {
            var query = new QueryExpression(LineEntity) { ColumnSet = new ColumnSet("sprk_invoice") };
            query.Criteria.AddCondition("sprk_billingeventid", ConditionOperator.In, batch);
            var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            if (rows.MoreRecords)
            {
                throw new InvalidOperationException("More invoice lines than one page.");
            }

            invoices.AddRange(rows.Entities.Select(r => r.GetAttributeValue<EntityReference>("sprk_invoice")?.Id).OfType<Guid>().Where(id => id != Guid.Empty));
        }

        return invoices;
    }

    /// <summary>The ids among <paramref name="invoiceIds"/> of invoices that are open (Active, In Review), app-only. A fault throws.</summary>
    private async Task<HashSet<Guid>> OpenInvoicesAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct)
    {
        var open = new HashSet<Guid>();
        foreach (var batch in invoiceIds.Select(id => (object)id).Chunk(MaxInValues))
        {
            var query = new QueryExpression(InvoiceEntity) { ColumnSet = new ColumnSet("statuscode") };
            query.Criteria.AddCondition("sprk_invoiceid", ConditionOperator.In, batch);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, InvoiceStatusCodes.StateActive);
            query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, InvoiceStatusCodes.InReview);
            var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            if (rows.MoreRecords)
            {
                throw new InvalidOperationException("More invoices than one page.");
            }

            open.UnionWith(rows.Entities.Where(r => invoiceIds.Contains(r.Id)).Select(r => r.Id));
        }

        return open;
    }
}
