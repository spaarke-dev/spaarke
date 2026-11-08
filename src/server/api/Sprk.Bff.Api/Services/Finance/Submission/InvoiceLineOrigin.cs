// ebilling-r1 task 165b item 1 (EB-D-200 (4); uac-r2 2026-11-06, part 164a-4a): approval refuses a line of unknown origin.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceSubmissionPersister.LineIdFor (the intake line id), ExtractionProvenance.AddedLineIdFor / IsReviewerAdded (the added line
//       id), the re-run's own-line check (InvoiceSubmissionRerun A1, an inline copy of the intake rule) and the Decision Record writer's
//       "created by the writer identity" filter. Nothing decides whether a Fee or Expense line on an invoice has a known origin.
//   (2) Extension — the rule combines the persister's id, the Add line id and the add-line Decision Record; giving it to any one of their owners
//       would make that owner read the other two. One static reader in the InvoiceBlockedHold shape; the re-run's A1 check calls its intake rule
//       instead of keeping its copy.
//   (3) Cost of doing nothing — approval would approve a line that neither intake nor Add line wrote (task 164 criterion, moved to 165b).
//
// Placement (bff-extensions.md): BFF, Services/Finance/Submission (a pure derivation and one app-only read; no DI registration, no endpoint,
// no package).

using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// Where a Fee or Expense line on an invoice came from (task 165b, EB-D-200 (4)). A line has a known origin when it is
/// (i) the invoice's own submission's intake line (<see cref="IsIntakeLineOf"/>), or (ii) a line Add line created on THIS invoice
/// (<see cref="ExtractionProvenance.AddedLineDecisionIdOf"/>) that is the subject of an <c>add-line</c> Decision Record the writer identity wrote,
/// whose key names this line and that decision and whose facts name this invoice and this line. The reviewer-added marker alone is not an
/// exemption. Adjustment lines are not in scope (they are decided by their own service and never carry a firm's charge).
/// </summary>
public static class InvoiceLineOrigin
{
    /// <summary>The refusal code: a Fee or Expense line of unknown origin (logged with the table and row id only).</summary>
    public const string UnknownOriginCode = "ebilling.approve.line_origin_unknown";

    private const string DecisionRecordEntity = "sprk_decisionrecord";
    private const string RecordLineLookup = "sprk_regardinginvoiceline";
    private const string RecordActionCode = "sprk_actioncode";
    private const string RecordSteps = "sprk_steps";
    private const string RecordFacts = "sprk_factsnapshot";

    /// <summary>
    /// True when <paramref name="line"/> is line <c>sequence</c> of the submission <paramref name="submissionId"/>'s intake: its id is
    /// <see cref="InvoiceSubmissionPersister.LineIdFor"/>(submission, sequence - 1). The one definition; the re-run's own-line check uses it.
    /// </summary>
    public static bool IsIntakeLineOf(Entity line, Guid submissionId)
    {
        ArgumentNullException.ThrowIfNull(line);
        return submissionId != Guid.Empty
            && line.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence) is { } sequence && sequence >= 1
            && line.Id == InvoiceSubmissionPersister.LineIdFor(submissionId, sequence - 1);
    }

    /// <summary>True for a Fee or Expense line (the lines whose origin is checked).</summary>
    public static bool IsChargeLine(Entity line) =>
        line.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value is (int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense;

    /// <summary>
    /// The Fee and Expense lines among <paramref name="lines"/> (the invoice's lines, read with <c>sprk_linesequence</c>,
    /// <c>sprk_extractionconfidence</c> and <c>sprk_invoice</c>) whose origin is not known, in the order given. Reads the <c>add-line</c> records
    /// app-only through <paramref name="entities"/>, only when a line passes the Add line id rule; a record counts only when
    /// <c>createdby</c> is <paramref name="writerUserId"/>. Throws on a read fault (callers refuse).
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> UnknownOriginLinesAsync(
        IGenericEntityService entities, Guid invoiceId, Guid? ownSubmissionId, IReadOnlyList<Entity> lines, Func<CancellationToken, Task<Guid>> writerUserId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(writerUserId);

        var unknown = new List<Guid>();
        var added = new Dictionary<Guid, Guid>(); // line id -> its Add line decision id
        foreach (var line in lines.Where(IsChargeLine))
        {
            if (ownSubmissionId is { } own && IsIntakeLineOf(line, own))
            {
                continue;
            }

            if (line.GetAttributeValue<EntityReference>(InvoiceTotalsService.LineInvoiceLookup)?.Id == invoiceId
                && ExtractionProvenance.AddedLineDecisionIdOf(line) is { } decisionId)
            {
                added[line.Id] = decisionId;
                continue;
            }

            unknown.Add(line.Id);
        }

        if (added.Count > 0)
        {
            var recorded = await RecordedAddedLinesAsync(entities, invoiceId, added, await writerUserId(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            unknown.AddRange(added.Keys.Where(id => !recorded.Contains(id)));
        }

        var order = lines.Select((l, i) => (l.Id, i)).ToDictionary(p => p.Id, p => p.i);
        return unknown.OrderBy(id => order[id]).ToList();
    }

    /// <summary>The lines of <paramref name="added"/> that have their <c>add-line</c> record (see the type remarks).</summary>
    private static async Task<HashSet<Guid>> RecordedAddedLinesAsync(
        IGenericEntityService entities, Guid invoiceId, IReadOnlyDictionary<Guid, Guid> added, Guid writerUserId, CancellationToken ct)
    {
        var query = new QueryExpression(DecisionRecordEntity)
        {
            ColumnSet = new ColumnSet(RecordLineLookup, RecordSteps, RecordFacts),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 },
            NoLock = true,
        };
        query.Criteria.AddCondition(RecordLineLookup, ConditionOperator.In, added.Keys.Cast<object>().ToArray());
        query.Criteria.AddCondition(RecordActionCode, ConditionOperator.Equal, InvoiceLineAddService.ActionCode);
        query.Criteria.AddCondition(DecisionRecordWriter.CreatedByColumn, ConditionOperator.Equal, writerUserId);
        query.AddOrder(DecisionRecordEntity + "id", OrderType.Ascending);

        var found = new HashSet<Guid>();
        foreach (var row in await InvoiceReviewReadService.ReadPagedAsync(entities, query, ct).ConfigureAwait(false))
        {
            if (row.GetAttributeValue<EntityReference>(RecordLineLookup)?.Id is { } lineId
                && added.TryGetValue(lineId, out var decisionId)
                && ReviewIdOf(row) == BillingDecisionKey.IdOf(InvoiceTotalsService.LineEntity, lineId, decisionId)
                && FactNames(row, "invoiceId", invoiceId)
                && FactNames(row, "lineId", lineId))
            {
                found.Add(lineId);
            }
        }

        return found;
    }

    /// <summary>The record's key (<c>sprk_steps.reviewId</c>); null when unreadable.</summary>
    private static Guid? ReviewIdOf(Entity row)
    {
        try
        {
            using var steps = JsonDocument.Parse(row.GetAttributeValue<string>(RecordSteps) ?? "{}");
            return steps.RootElement.ValueKind == JsonValueKind.Object
                && steps.RootElement.TryGetProperty("reviewId", out var id) && id.ValueKind == JsonValueKind.String
                && Guid.TryParse(id.GetString(), out var key)
                    ? key
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>True when the record's facts (<c>sprk_factsnapshot.facts</c>) carry <paramref name="name"/> = <paramref name="expected"/>.</summary>
    private static bool FactNames(Entity row, string name, Guid expected)
    {
        try
        {
            using var snapshot = JsonDocument.Parse(row.GetAttributeValue<string>(RecordFacts) ?? "{}");
            return snapshot.RootElement.ValueKind == JsonValueKind.Object
                && snapshot.RootElement.TryGetProperty("facts", out var facts) && facts.ValueKind == JsonValueKind.Object
                && facts.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && Guid.TryParse(value.GetString(), out var named) && named == expected;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
