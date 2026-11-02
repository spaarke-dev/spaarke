using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// Which of the correcting submissions the CALLER may read, by submission id. Each status surface supplies its own: it is the same rule that
/// surface already applies to the submission itself (the internal route's Read on the submission, the external routes' firm, core record and
/// (a') decision). An id missing from the answer is not named.
/// </summary>
internal delegate Task<IReadOnlySet<Guid>> ReplacementReadGate(IReadOnlyList<InvoiceSubmissionView> replacements, CancellationToken ct);

/// <summary>A correcting submission and its invoice, as <see cref="SupersededSubmissionNote.FindReplacementAsync"/> chose them.</summary>
internal sealed record FoundReplacement(Entity Submission, Entity Invoice);

/// <summary>
/// ebilling-r1 task 097f (EB-D-180): the read-time note on an Accepted submission whose own invoice a correction Superseded. The submission
/// stays Accepted and nothing is written; its status view answers <see cref="SubmissionReasonCodes.ReplacedByCorrection"/> as the outcome
/// reason and, when the caller may read it, names the correcting submission (<see cref="InvoiceSubmissionView.ReplacedBy"/>).
/// </summary>
/// <remarks>
/// <para><b>One lookup.</b> <see cref="FindReplacementAsync"/> is the lookup from an old submission to its replacing one, shared with the
/// correction prefill (task 097e, contract section 10), so the two surfaces choose the same replacement.</para>
/// <para><b>Deliberate: the note is enrichment.</b> A fault finding which invoices are Superseded, finding the replacement, or deciding the caller's
/// right to read it never fails the call (no 503): the first leaves the answer exactly as it was before this task (the stored status is still
/// true), the others leave <see cref="InvoiceSubmissionView.ReplacedBy"/> null. Each logs a Warning (ids only, ADR-015). A replacement the
/// caller may not read is never named, so the answer reveals nothing about it beyond the reason code.</para>
/// <para><b>App-only reads.</b> Every read here goes through <see cref="IGenericEntityService"/>: the replacement is found by
/// <c>sprk_resubmissionof</c>, which is field-secured so that only the BFF application user reads or writes it
/// (<c>ResubmissionOfAppOnlyWriteGuardTests</c>). The caller's own right is asked afterwards, by <see cref="ReplacementReadGate"/>.</para>
/// </remarks>
internal static class SupersededSubmissionNote
{
    /// <summary>
    /// The DIRECT replacement of the submission <paramref name="ownSubmissionId"/> (no chain is followed): the invoice-bearing submission whose
    /// <c>sprk_resubmissionof</c> is it. Several: the one whose invoice is not itself Superseded; still not exactly one: null (a Warning). A
    /// replacement whose invoice is gone is not a replacement. Throws on a read fault; the caller decides what that means.
    /// </summary>
    /// <param name="invoiceColumns">The columns to read of each replacement invoice, beside its <c>statuscode</c>.</param>
    internal static async Task<FoundReplacement?> FindReplacementAsync(
        IGenericEntityService entities, ILogger logger, Guid ownSubmissionId, IEnumerable<string> invoiceColumns, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceSubmissionService.SubmissionEntity)
        {
            ColumnSet = new ColumnSet(InvoiceSubmissionService.ViewColumns),
        };
        query.Criteria.AddCondition(InvoiceSubmissionService.ResubmissionOfColumn, ConditionOperator.Equal, ownSubmissionId);

        var byInvoice = new Dictionary<Guid, Entity>();
        foreach (var submission in (await entities.RetrieveMultipleAsync(query, ct)).Entities)
        {
            if (submission.GetAttributeValue<EntityReference>(InvoiceSubmissionService.InvoiceColumn)?.Id is { } invoiceId && invoiceId != Guid.Empty)
            {
                byInvoice.TryAdd(invoiceId, submission);
            }
        }

        var columns = invoiceColumns.Append("statuscode").Distinct().ToArray();
        var found = new List<FoundReplacement>();
        foreach (var (invoiceId, submission) in byInvoice)
        {
            try
            {
                var row = await entities.RetrieveAsync(FinanceBaseValues.InvoiceEntity, invoiceId, columns, ct);
                if (row is not null)
                {
                    found.Add(new FoundReplacement(submission, row));
                }
            }
            catch (Exception ex) when (InvoiceSubmissionService.IsNotFound(ex))
            {
                // A replacement that is gone is not a replacement.
            }
        }

        if (found.Count <= 1)
        {
            return found.SingleOrDefault();
        }

        var live = found.Where(f => f.Invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value != InvoiceStatusCodes.Superseded).ToList();
        if (live.Count == 1)
        {
            return live[0];
        }

        logger.LogWarning("The replacement of a superseded invoice is ambiguous ({Count} submissions name it); it is not named", found.Count);
        return null;
    }

    /// <summary>
    /// <paramref name="views"/> with the note on each Accepted submission whose invoice is Superseded; every other view is the SAME instance, so
    /// an answer with nothing Superseded is exactly what it was. One read for the page decides which invoices are Superseded; the lookup of the
    /// replacement runs only for those.
    /// </summary>
    internal static async Task<IReadOnlyList<InvoiceSubmissionView>> ApplyAsync(
        IGenericEntityService entities, ILogger logger, IReadOnlyList<InvoiceSubmissionView> views, ReplacementReadGate mayRead, CancellationToken ct)
    {
        var accepted = views
            .Where(v => v.Status == nameof(InvoiceSubmissionStatus.Accepted) && v.InvoiceId is { } id && id != Guid.Empty)
            .ToList();
        if (accepted.Count == 0)
        {
            return views;
        }

        HashSet<Guid> superseded;
        try
        {
            superseded = await SupersededInvoicesAsync(entities, accepted.Select(v => v.InvoiceId!.Value).Distinct().ToList(), ct);
        }
        catch (Exception ex) when (IsFault(ex, ct))
        {
            logger.LogWarning(ex, "Which invoices are superseded could not be read for {Count} submission(s); their answers carry no replacement note", accepted.Count);
            return views;
        }

        var noted = accepted.Where(v => superseded.Contains(v.InvoiceId!.Value)).ToList();
        if (noted.Count == 0)
        {
            return views;
        }

        var replacements = new Dictionary<Guid, InvoiceSubmissionView>();
        foreach (var view in noted)
        {
            try
            {
                if (await FindReplacementAsync(entities, logger, view.SubmissionId, [], ct) is { } found)
                {
                    replacements[view.SubmissionId] = InvoiceSubmissionService.ToView(found.Submission);
                }
            }
            catch (Exception ex) when (IsFault(ex, ct))
            {
                logger.LogWarning(ex, "The replacement of submission {SubmissionId} could not be read; replacedBy is null", view.SubmissionId);
            }
        }

        IReadOnlySet<Guid> readable = new HashSet<Guid>();
        if (replacements.Count > 0)
        {
            try
            {
                readable = await mayRead(replacements.Values.ToList(), ct);
            }
            catch (Exception ex) when (IsFault(ex, ct))
            {
                logger.LogWarning(ex, "The caller's right to read {Count} replacing submission(s) could not be decided; replacedBy is null", replacements.Count);
            }
        }

        var shown = noted.Select(v => v.SubmissionId).ToHashSet();
        return views
            .Select(v => !shown.Contains(v.SubmissionId)
                ? v
                : v with
                {
                    OutcomeReasonCode = SubmissionReasonCodes.ReplacedByCorrection,
                    ReplacedBy = replacements.TryGetValue(v.SubmissionId, out var replacement) && readable.Contains(replacement.SubmissionId)
                        ? new SubmissionReplacement(replacement.SubmissionId, replacement.TrackingNumber)
                        : null,
                })
            .ToList();
    }

    /// <summary>Of <paramref name="invoiceIds"/>, those whose status is Superseded. Throws on a read fault.</summary>
    private static async Task<HashSet<Guid>> SupersededInvoicesAsync(
        IGenericEntityService entities, IReadOnlyList<Guid> invoiceIds, CancellationToken ct)
    {
        var found = new HashSet<Guid>();
        foreach (var chunk in invoiceIds.Chunk(InvoiceSubmissionService.MaxInValues))
        {
            var query = new QueryExpression(FinanceBaseValues.InvoiceEntity) { ColumnSet = new ColumnSet("statuscode") };
            query.Criteria.AddCondition(FinanceBaseValues.InvoiceKeyColumn, ConditionOperator.In, chunk.Cast<object>().ToArray());
            query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, InvoiceStatusCodes.Superseded);
            foreach (var row in (await entities.RetrieveMultipleAsync(query, ct)).Entities)
            {
                found.Add(row.Id);
            }
        }

        return found;
    }

    private static bool IsFault(Exception ex, CancellationToken ct) => ex is not OperationCanceledException || !ct.IsCancellationRequested;
}
