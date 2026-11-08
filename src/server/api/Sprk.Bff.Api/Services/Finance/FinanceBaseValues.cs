using System.Collections.Frozen;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The ONE definition of how a Matter's or Project's spend, budget and budget utilization are computed from their base rows
/// (<c>sprk_invoice</c> and <c>sprk_budget</c>). <see cref="FinanceRollupService"/> uses it to compute what it stores on the parent;
/// the Signal evaluator uses it to judge a Threshold rule on those stored columns from the base rows instead of from the stored
/// copies (task 025b, owner decision D-122). Read-only: nothing here writes.
/// </summary>
/// <remarks>
/// <para><b>Why the evaluator does not read the stored columns.</b> <c>sprk_matter</c> and <c>sprk_project</c> hold
/// <c>sprk_totalspendtodate</c>, <c>sprk_totalbudget</c> and <c>sprk_budgetutilizationpercent</c> as PLAIN stored columns (live
/// <c>spaarkedev1</c> metadata, <c>SourceType</c> 0, read 2026-10-12: not calculated, not rollup). Their only writer is
/// <see cref="FinanceRollupService"/>, which runs on a form subgrid edit or a never-enqueued job, so a stored value can be
/// arbitrarily stale. A rule that asserts "utilization has reached 90 percent" must not rest on a copy nothing keeps current.
/// No nightly mass write is added to refresh them (D-61).</para>
/// <para><b>The rules</b> (copied from <see cref="FinanceRollupService"/>, which now calls this class): spend = the sum of each
/// invoice's <see cref="SpendAmount"/> (the approved total, else the billed total; owner decision D-130) over the parent's invoices
/// whose status is one of <see cref="CountedInvoiceStatuses"/> AND that have their approve Decision Record (task 165b-4, EB-D-208: decided only
/// from server-written sources, <see cref="ReadCountedInvoicesAsync"/>); budget = the sum of <c>sprk_budget.sprk_totalbudget</c> over the parent's budgets;
/// utilization = <c>spend / budget * 100</c> (a PERCENT: 100 means fully spent) when budget is above zero, otherwise undefined
/// (<see cref="FinanceRollupService"/> stores 0; the evaluator treats it as a missing input).</para>
/// <para>§11: <i>Existing</i> - the same sums and formula lived privately in <see cref="FinanceRollupService"/>. <i>Extension</i> -
/// extracted, not copied, so there is one formula. <i>Cost of doing nothing</i> - the evaluator would copy it, and a change to the
/// status set or the formula would make a Signal's number disagree with the matter form.</para>
/// </remarks>
public static class FinanceBaseValues
{
    public const string MatterEntity = "sprk_matter";
    public const string ProjectEntity = "sprk_project";

    public const string InvoiceEntity = "sprk_invoice";
    public const string InvoiceTotalAmount = "sprk_totalamount";
    /// <summary>The invoice's approved (payable) total: the billed total plus reviewer-added lines as the approved total counts them (EB-D-199), minus the reviewer's line and invoice adjustments. Negative
    /// on a credit memo (D-127). Empty until the totals service has computed it. Same column as <c>InvoiceTotalsService.InvoiceApprovedTotal</c>.</summary>
    public const string InvoiceApprovedTotal = "sprk_approvedtotal";
    /// <summary>The invoice status column: <c>statuscode</c>, never <c>statecode</c> (Paid is Inactive and still counts). The
    /// retired invoice-status choice column is gone (ebilling-r1 task 015, D-41).</summary>
    public const string InvoiceStatusCodeColumn = "statuscode";

    public const string BudgetEntity = "sprk_budget";
    public const string BudgetTotalBudget = "sprk_totalbudget";

    /// <summary>The parent columns this class derives (same names on <c>sprk_matter</c> and <c>sprk_project</c>).</summary>
    public const string SpendColumn = "sprk_totalspendtodate";
    public const string BudgetColumn = "sprk_totalbudget";
    public const string UtilizationColumn = "sprk_budgetutilizationpercent";

    /// <summary>The invoice <c>statuscode</c> values whose amount counts as spend: Approved, Sent to AP and Paid. In Review is NOT
    /// counted (it is pending, not actual spend), and neither are Rejected, Returned to Firm and Cancelled, so a Signal raised by an
    /// invoice that is later rejected can clear. Owner decision D-103 (ebilling-r1, 2026-10-17; the same decision as ontology-r1
    /// D-166), which supersedes D-98 and replaces ontology D-137 Q24 ("spend counts only Reviewed"). Paid is Inactive and still counts,
    /// which is why the filter is on <c>statuscode</c>. The numbers live once, in <see cref="InvoiceStatusCodes"/>.</summary>
    public static readonly IReadOnlyList<int> CountedInvoiceStatuses = new[]
    {
        InvoiceStatusCodes.Approved, InvoiceStatusCodes.SentToAp, InvoiceStatusCodes.Paid,
    };

    /// <summary>The invoice columns <see cref="SpendAmount"/> reads. A query that feeds <see cref="SpendAmount"/> must select both: a
    /// column left out reads as empty, and an empty approved total silently falls back to the billed total.</summary>
    public static readonly IReadOnlyList<string> SpendAmountColumns = new[] { InvoiceApprovedTotal, InvoiceTotalAmount };

    /// <summary>
    /// The ONE amount rule (owner decision D-130 (2); counted spend AND Pending Spend): an invoice's spend amount is its approved
    /// (payable) total, <c>sprk_approvedtotal</c>, when that has a value, otherwise its billed total, <c>sprk_totalamount</c>; when
    /// neither has a value it is 0. "Has a value" is not-null, so an approved total of 0 (every charge disallowed) counts as 0 and
    /// does NOT fall back to the billed total. Signed: a credit memo's negative approved total lowers spend (D-127). A reviewer's
    /// disallowances and reductions therefore lower spend, and reviewer-added lines raise it as the approved total counts them (EB-D-199, EB-D-205). Every spend reader applies it per row
    /// (task 165b-4: the grouped two-part sums are retired).
    /// </summary>
    public static decimal SpendAmount(Entity invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return invoice.GetAttributeValue<Money>(InvoiceApprovedTotal)?.Value
            ?? invoice.GetAttributeValue<Money>(InvoiceTotalAmount)?.Value
            ?? 0m;
    }

    /// <summary>The sum of <see cref="SpendAmount"/> over <paramref name="invoices"/>.</summary>
    public static decimal SumSpend(IEnumerable<Entity> invoices) => invoices.Sum(SpendAmount);

    /// <summary>The columns the derived Threshold reads, as (entity, column), stated ONCE so a guard can check them all. The
    /// evaluator reads these org-wide with its own identity, so a field-secured column would read as empty for it and silently fall
    /// back (the approved total) or read as 0 (the others). None may be secured unless it is in the reviewed exception list
    /// (<c>DerivedBaseColumnSecurityExceptions</c>, tests/integration/Shared), whose columns are secured on purpose and readable by the
    /// evaluator's identity. Checked live by
    /// <c>SignalPredicateTests.AuthorableColumns_ExistAndAreNotFieldSecured_OnTheLiveSchema</c> and offline against the SpaarkeMaster
    /// Entity.xml by <c>DerivedBaseColumnsFieldSecurityTests</c>. The submission columns are the unsecured hold sources pending spend reads.
    /// <para>It lists every attribute the spend and budget queries select, filter on, sum or join on (except each table's primary key), the
    /// parent lookups <c>sprk_matter</c> and <c>sprk_project</c> included: a secured parent lookup reads as null, the row is skipped and
    /// spend reads 0 with no guard failing (ontology-r1 re-check K1, 2026-10-19). Task 165b-4 adds the approve-record match
    /// (<see cref="InvoiceApproveRecord"/>): the Decision Record's invoice lookup, action code and key (<c>sprk_steps</c>), the portion's invoice
    /// lookup, and <c>createdby</c> / <c>createdonbehalfby</c> (listed for completeness). An unreadable one would drop every match and read
    /// counted spend as 0. The field-secured hold source is listed apart, in <see cref="HoldSourceColumns"/>. <c>SpendHeldAndPendingTests</c>
    /// builds the real queries and fails when a column they use is missing from both lists.</para></summary>
    public static readonly IReadOnlyList<(string Entity, string Column)> DerivedBaseColumns = new (string, string)[]
    {
        (InvoiceReviewReadService.DecisionRecordEntity, InvoiceApproveRecord.InvoiceLookup),
        (InvoiceReviewReadService.DecisionRecordEntity, InvoiceApproveRecord.ActionCodeColumn),
        (InvoiceReviewReadService.DecisionRecordEntity, InvoiceLineOrigin.RecordSteps),
        (InvoiceReviewReadService.DecisionRecordEntity, Signals.DecisionRecordWriter.CreatedByColumn),
        (InvoiceReviewReadService.DecisionRecordEntity, Signals.WriterCreatedRecord.CreatedOnBehalfByColumn),
        (EligibleApproverResolver.ApprovalEntity, EligibleApproverResolver.ApprovalInvoice),
        (InvoiceEntity, InvoiceTotalAmount),
        (InvoiceEntity, InvoiceApprovedTotal),
        (InvoiceEntity, InvoiceStatusCodeColumn),
        (InvoiceEntity, MatterEntity),
        (InvoiceEntity, ProjectEntity),
        (BudgetEntity, BudgetTotalBudget),
        (BudgetEntity, MatterEntity),
        (BudgetEntity, ProjectEntity),
        (SubmissionEntity, SubmissionStatusColumn),
        (SubmissionEntity, SubmissionReasonColumn),
        (SubmissionEntity, SubmissionInvoiceLookup),
    };

    /// <summary>Task 165b-4 (ontology-r1 O4): the field-secured hold sources pending spend reads, listed apart from
    /// <see cref="DerivedBaseColumns"/>: secured, and readable by the BFF application identity through its field-security profile
    /// (<see cref="InvoiceBlockedHold.HoldSourceColumns"/>, the one list).</summary>
    public static IReadOnlyList<(string Entity, string Column)> HoldSourceColumns => InvoiceBlockedHold.HoldSourceColumns;

    /// <summary>The parent column that stores Pending Spend (matter and project; ebilling-r1 owner decision D-104). Written only by
    /// <see cref="FinanceRollupService"/>. It is NOT in <see cref="IsDerived"/> yet: a Threshold cannot name it until ontology-r1
    /// adds it to its authorable columns, so the evaluator never asks <see cref="Needs"/> / <see cref="ValueOf"/> about it.</summary>
    public const string PendingSpendColumn = "sprk_pendingspend";

    /// <summary>The invoice <c>statuscode</c> values whose amount is PENDING spend (D-104): In Review. Pending spend is kept apart
    /// from actual spend (<see cref="CountedInvoiceStatuses"/>, D-103), so the two sets never overlap. A pending invoice that is
    /// HELD (see <see cref="HeldSubmissionConditions"/>) is left out of the sum.</summary>
    public static readonly IReadOnlyList<int> PendingInvoiceStatuses = new[] { InvoiceStatusCodes.InReview };

    /// <summary>The submission table a held invoice is read from (<c>sprk_invoicesubmission.sprk_invoice</c> names the invoice the
    /// submission created, D-101).</summary>
    public const string SubmissionEntity = "sprk_invoicesubmission";
    internal const string SubmissionInvoiceLookup = "sprk_invoice";
    internal const string SubmissionKeyColumn = "sprk_invoicesubmissionid";
    internal const string InvoiceKeyColumn = "sprk_invoiceid";
    internal const string SubmissionStatusColumn = "sprk_processingstatus";
    internal const string SubmissionReasonColumn = "sprk_outcomereasoncode";

    /// <summary>The outcome reason code of a submission blocked by a rule and awaiting Override / Return (<see cref="SubmissionReasonCodes.Blocked"/>).</summary>
    public const string BlockedReasonCode = SubmissionReasonCodes.Blocked;

    /// <summary>
    /// What makes an In Review invoice HELD (owner decisions D-101, D-102, D-104, D-130(a)): some submission linked to it
    /// (<c>sprk_invoicesubmission.sprk_invoice</c>) has <c>sprk_processingstatus</c> IN (Processing, Rejected) AND
    /// <c>sprk_outcomereasoncode</c> = <see cref="BlockedReasonCode"/> - a blocked internal submission awaiting Override or Return.
    /// Override releases the invoice by setting the submission to Accepted; Return moves the invoice out of In Review. No column is
    /// added to the invoice: the hold is the state the Override / Return flow already keeps on the submission. This is the ONE
    /// definition of the column check; <see cref="InvoiceBlockedHold"/> applies it (check 1) with its server-written sources, and pending
    /// spend leaves out what <see cref="InvoiceBlockedHold"/> holds (task 165b-4).
    /// </summary>
    /// <remarks>
    /// Why Processing counts as well as Rejected: the persist transaction (D-130(a)) writes the blocked reason code and moves the
    /// invoice to In Review, but the submission itself stays Processing until the after-steps' final write (and stays there for good
    /// if that write is poisoned). Only a committed Blocked outcome writes the code, so a Processing submission carrying it is
    /// already a blocked one, and the invoice must not count as pending spend in that window. Override moves the submission to
    /// Accepted, which leaves the set.
    /// Counted spend adds NO held predicate: a held invoice is In Review, which D-103 does not count. Only pending spend needs it.
    /// Pending spend reads each invoice once (a row read) and its linked submissions in one batch, so an invoice with several linked
    /// submissions (resubmissions) is never counted twice (task 165b-4: the earlier anti-join is retired).
    /// </remarks>
    public static IReadOnlyList<(string Column, IReadOnlyList<object> Values)> HeldSubmissionConditions { get; } =
        new (string, IReadOnlyList<object>)[]
        {
            (SubmissionStatusColumn, new object[] { (int)InvoiceSubmissionStatus.Processing, (int)InvoiceSubmissionStatus.Rejected }),
            (SubmissionReasonColumn, new object[] { BlockedReasonCode }),
        };

    /// <summary>Parent ids per grouped query. Matches <c>PredicateCompiler.MaxInListLength</c>: the size of an <c>in</c> list
    /// the Signal code already sends to Dataverse.</summary>
    public const int ParentChunkSize = 200;

    private static readonly FrozenSet<string> DerivedColumns =
        new[] { SpendColumn, BudgetColumn, UtilizationColumn }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>True for a column of a matter or project that is derived here (and so must not be read from its stored copy).</summary>
    public static bool IsDerived(string entity, string column) =>
        entity is MatterEntity or ProjectEntity && DerivedColumns.Contains(column);

    /// <summary>The lookup on <c>sprk_invoice</c> AND on <c>sprk_budget</c> that names the parent: both are called after the
    /// parent table (verified in <c>CoreAncestorResolver</c>: invoice and budget carry <c>sprk_matter</c> and
    /// <c>sprk_project</c>).</summary>
    public static string ParentLookup(string parentEntity) =>
        parentEntity is MatterEntity or ProjectEntity
            ? parentEntity
            : throw new ArgumentOutOfRangeException(nameof(parentEntity), parentEntity, "Only a matter or a project has invoices and budgets.");

    /// <summary>The sum of a Money column over <paramref name="rows"/>; a null amount counts as 0.</summary>
    public static decimal SumMoney(IEnumerable<Entity> rows, string column) =>
        rows.Sum(e => e.GetAttributeValue<Money>(column)?.Value ?? 0m);

    /// <summary><c>spend / budget * 100</c> when <paramref name="budget"/> is above zero; otherwise null (there is no utilization of
    /// a zero or negative budget). Unrounded: this is what <see cref="FinanceRollupService"/> writes (Dataverse rounds it to the
    /// column's precision on store).</summary>
    public static decimal? UtilizationPercent(decimal spend, decimal budget) => budget > 0 ? spend / budget * 100m : null;

    /// <summary>The value <c>sprk_budgetutilizationpercent</c> would hold after a recalculation: the percent rounded to the column's
    /// precision (2, live metadata), the way Dataverse stores it. The evaluator compares and states THIS number, so a Signal never
    /// shows a figure the column could not hold.</summary>
    public static decimal AsStoredPercent(decimal percent) => Math.Round(percent, 2, MidpointRounding.AwayFromZero);

    /// <summary>Which base rows a derived column needs.</summary>
    public static (bool Spend, bool Budget) Needs(string column) => column switch
    {
        SpendColumn => (true, false),
        BudgetColumn => (false, true),
        UtilizationColumn => (true, true),
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Not a derived finance column."),
    };

    /// <summary>The value of a derived column from the parent's spend and budget, or null with the reason it has none.</summary>
    public static decimal? ValueOf(string column, decimal spend, decimal budget, out string? missingReason)
    {
        missingReason = null;
        switch (column)
        {
            case SpendColumn:
                return spend;
            case BudgetColumn:
                return budget;
            case UtilizationColumn:
                if (UtilizationPercent(spend, budget) is { } percent)
                {
                    return AsStoredPercent(percent);
                }

                missingReason = "no budget above zero (budget " + budget.ToString(CultureInfo.InvariantCulture) + "): utilization is undefined";
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(column), column, "Not a derived finance column.");
        }
    }

    /// <summary>
    /// The base values of <paramref name="parentIds"/>, per chunk of <see cref="ParentChunkSize"/> parents, never one query per parent: spend
    /// (when needed) is the sum of <see cref="SpendAmount"/> per parent over <see cref="ReadCountedInvoicesAsync"/>'s rows (the counted invoices
    /// that have their approve record; one row read and two keyed reads), and budget (when needed) is one grouped aggregate over the budgets. A
    /// parent with no rows has spend/budget 0 (a sum of nothing, as the rollup computes). A Dataverse failure propagates: the caller must treat
    /// the value as unknown.
    /// </summary>
    /// <param name="writerUserId">Task 165b-4 (ontology-r1 O1; round 2): the provider of the writer identity whose approve records count
    /// (<c>OntologyWriterDataverseClient.GetWriterUserIdAsync</c>). Called lazily, at most once per call, and only when
    /// <paramref name="needSpend"/> and a chunk has counted invoice rows (<see cref="ReadCountedInvoicesAsync"/>): with none, spend is 0 and it
    /// is never called. A fault, or <see cref="Guid.Empty"/>, then throws (spend unknown, never "no record, so 0 counted spend").</param>
    public static async Task<IReadOnlyDictionary<Guid, (decimal Spend, decimal Budget)>> ReadAsync(
        IGenericEntityService entities, string parentEntity, IReadOnlyCollection<Guid> parentIds,
        bool needSpend, bool needBudget, Func<CancellationToken, Task<Guid>> writerUserId, CancellationToken ct, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(parentIds);
        ArgumentNullException.ThrowIfNull(writerUserId);

        // Once per call: a later chunk reuses the first answer.
        Guid? resolved = null;
        async Task<Guid> WriterOnce(CancellationToken token) => resolved ??= await writerUserId(token).ConfigureAwait(false);

        var result = new Dictionary<Guid, (decimal, decimal)>(parentIds.Count);
        foreach (var chunk in parentIds.Distinct().Chunk(ParentChunkSize))
        {
            var spend = needSpend
                ? SumSpendByParent(parentEntity, await ReadCountedInvoicesAsync(entities, parentEntity, chunk, WriterOnce, ct, logger: logger).ConfigureAwait(false))
                : new Dictionary<Guid, decimal>();
            var budget = needBudget
                ? ReadGroupedSums(await entities.RetrieveMultipleAsync(
                    new FetchExpression(BuildGroupedBudgetFetchXml(parentEntity, chunk)), ct).ConfigureAwait(false))
                : new Dictionary<Guid, decimal>();
            foreach (var id in chunk)
            {
                result[id] = (spend.GetValueOrDefault(id), budget.GetValueOrDefault(id));
            }
        }

        logger?.LogDebug("Read the finance base values of {ParentCount} {ParentEntity} parent(s).", result.Count, parentEntity);
        return result;
    }

    /// <summary>
    /// The live PENDING spend (D-104) of <paramref name="parentIds"/>: per chunk of <see cref="ParentChunkSize"/> parents, the sum of
    /// <see cref="SpendAmount"/> (D-130) over the In Review invoices that <see cref="InvoiceBlockedHold"/> does not hold. Read 1 is a paged row
    /// read of the In Review invoices (the snapshot); read 2 is ONE paged read of their linked submissions
    /// (<see cref="InvoiceBlockedHold.CheckBatchAsync"/>), and the hold rule is applied in memory, so the count does not grow with the invoices.
    /// Only <see cref="InvoiceBlockedHold"/>'s hold leaves pending (ontology-r1 Q5: a firm hold stays pending; <c>sprk_firmhold</c> is not read).
    /// An In Review invoice with no linked originating submission is held, so it leaves pending (EB-D-201); their count is logged. A parent with
    /// no such invoice has 0. A Dataverse failure, or a hold source this identity cannot read (the canary in
    /// <see cref="InvoiceBlockedHold.CheckBatchAsync"/>), propagates: pending spend is never read as zero by default.
    /// </summary>
    /// <remarks><b>Call it only with an application identity</b> (ontology-r1 review K2, 2026-10-19): the hold source
    /// <c>sprk_findingsjson</c> is field-secured to the BFF application identity (<see cref="InvoiceBlockedHold.HoldSourceColumns"/>), and row
    /// security applies to the submissions. The rollup is the production caller; it reads with the BFF application identity.</remarks>
    public static async Task<IReadOnlyDictionary<Guid, decimal>> ReadPendingSpendAsync(
        IGenericEntityService entities, string parentEntity, IReadOnlyCollection<Guid> parentIds, CancellationToken ct, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(parentIds);
        var result = new Dictionary<Guid, decimal>(parentIds.Count);
        var unlinked = 0;
        foreach (var chunk in parentIds.Distinct().Chunk(ParentChunkSize))
        {
            foreach (var id in chunk)
            {
                result[id] = 0m;
            }

            // Read 1: the snapshot of the In Review invoices. Read 2: their linked submissions, the hold decided in memory (InvoiceBlockedHold).
            var rows = Distinct(await InvoiceReviewReadService.ReadPagedAsync(
                entities, InvoiceRowQuery(parentEntity, PendingInvoiceStatuses, chunk), ct).ConfigureAwait(false));
            if (rows.Count == 0)
            {
                continue;
            }

            var verdicts = await InvoiceBlockedHold.CheckBatchAsync(
                entities, parentEntity, chunk, PendingInvoiceStatuses, rows.Select(r => r.Id).ToList(), ct).ConfigureAwait(false);
            var open = new List<Entity>(rows.Count);
            foreach (var row in rows)
            {
                var verdict = verdicts[row.Id];
                if (!verdict.Held)
                {
                    open.Add(row);
                }
                else if (verdict.ReasonCode == InvoiceBlockedHold.HeldOwnSubmissionUnlinked)
                {
                    unlinked++;
                }
            }

            foreach (var (parent, amount) in SumSpendByParent(parentEntity, open))
            {
                if (result.ContainsKey(parent))
                {
                    result[parent] += amount;
                }
            }
        }

        if (unlinked > 0)
        {
            // EB-D-201 (ontology-r1 Q7): an In Review invoice with no linked originating submission is held, so it leaves pending spend.
            logger?.LogWarning(
                "Pending spend left out {UnlinkedCount} In Review invoice(s) that have no linked originating submission ({ReasonCode}).",
                unlinked, InvoiceBlockedHold.HeldOwnSubmissionUnlinked);
        }

        return result;
    }

    /// <summary>
    /// The COUNTED invoices of <paramref name="parentIds"/> (task 165b-4, EB-D-208; ontology-r1 option (b)): the parents' invoices whose status is
    /// one of <see cref="CountedInvoiceStatuses"/> AND that have their approve Decision Record (<see cref="InvoiceApproveRecord"/>: writer-created,
    /// under the approval's own key). Every spend reader sums or buckets these rows (<see cref="ReadAsync"/>, <see cref="FinanceRollupService"/>,
    /// the finance tool), so the readers cannot disagree. Each row carries the parent lookup, the two <see cref="SpendAmountColumns"/> and
    /// <paramref name="extraColumns"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>One snapshot.</b> Per chunk of <see cref="ParentChunkSize"/> parents: read 1 is a paged row read of the invoices and is the
    /// snapshot every amount comes from (one row per invoice, so <see cref="SpendAmount"/> coalesces per row and an invoice with two approve
    /// records is counted once). Reads 2 and 3 (the portions and the writer's approve records, <see cref="InvoiceApproveRecord.MatchedInvoicesAsync(IGenericEntityService, string, IReadOnlyCollection{Guid}, IReadOnlyList{int}, IReadOnlySet{Guid}, Guid, CancellationToken)"/>)
    /// only FILTER it: an allow-list can remove an invoice from read 1, never add one. So no re-read check is needed.
    /// <b>K1 (known limit):</b> an invoice approved after read 1 is left out until the next run.</para>
    /// <para><b>Reads.</b> Three per chunk (read 2 and 3 are skipped when the chunk has no counted invoice; paging adds a read per 5,000 rows),
    /// never one per invoice, and the same number for 1 invoice as for 50.</para>
    /// <para><b>The writer id (round 2, ontology-r1).</b> <paramref name="writerUserId"/> is a provider, called lazily: only once read 1 has
    /// returned counted rows, and then once per call. With no counted rows counted spend is truthfully empty: no writer-id call and no read 2
    /// or 3. With counted rows and no usable writer id (the provider faults, for example because
    /// <see cref="OntologyWriterCredentialFactory.ConfigKey"/> is not set, or WhoAmI fails, or it answers
    /// <see cref="Guid.Empty"/>) counted spend is unknown: a Warning naming that setting is logged and the call throws, never "no record, so
    /// 0 counted spend".</para>
    /// <para>A read fault propagates.</para>
    /// </remarks>
    public static async Task<IReadOnlyList<Entity>> ReadCountedInvoicesAsync(
        IGenericEntityService entities, string parentEntity, IReadOnlyCollection<Guid> parentIds, Func<CancellationToken, Task<Guid>> writerUserId,
        CancellationToken ct, IReadOnlyCollection<string>? extraColumns = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(parentIds);
        ArgumentNullException.ThrowIfNull(writerUserId);

        var counted = new List<Entity>();
        Guid? writer = null;
        foreach (var chunk in parentIds.Distinct().Chunk(ParentChunkSize))
        {
            // Read 1: the snapshot.
            var rows = Distinct(await InvoiceReviewReadService.ReadPagedAsync(
                entities, InvoiceRowQuery(parentEntity, CountedInvoiceStatuses, chunk, extraColumns), ct).ConfigureAwait(false));
            if (rows.Count == 0)
            {
                continue;
            }

            // Reads 2 and 3: the keyed allow-list; they only filter read 1. The writer id is needed only from here.
            writer ??= await CountedSpendWriterAsync(writerUserId, rows.Count, parentEntity, logger, ct).ConfigureAwait(false);
            var matched = await InvoiceApproveRecord.MatchedInvoicesAsync(
                entities, parentEntity, chunk, CountedInvoiceStatuses, rows.Select(r => r.Id).ToHashSet(), writer.Value, ct).ConfigureAwait(false);
            counted.AddRange(rows.Where(r => matched.Contains(r.Id)));
        }

        return counted;
    }

    /// <summary>
    /// Task 165b-4 round 2: the writer id for counted rows that need it. A fault, or <see cref="Guid.Empty"/>, makes counted spend unknown: it is
    /// logged as a Warning that names <see cref="OntologyWriterCredentialFactory.ConfigKey"/> (an absent setting is the
    /// misconfiguration an operator can fix) and thrown. The caller's own cancellation is not logged.
    /// </summary>
    private static async Task<Guid> CountedSpendWriterAsync(
        Func<CancellationToken, Task<Guid>> writerUserId, int countedRows, string parentEntity, ILogger? logger, CancellationToken ct)
    {
        Guid writer;
        try
        {
            writer = await writerUserId(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger?.LogWarning(ex, WriterUnavailableMessage, countedRows, parentEntity, OntologyWriterCredentialFactory.ConfigKey);
            throw;
        }

        if (writer == Guid.Empty)
        {
            var empty = new InvalidOperationException("The writer identity is unknown (empty), so no invoice can be shown to have its approve record.");
            logger?.LogWarning(empty, WriterUnavailableMessage, countedRows, parentEntity, OntologyWriterCredentialFactory.ConfigKey);
            throw empty;
        }

        return writer;
    }

    private const string WriterUnavailableMessage =
        "Counted spend is unknown for {CountedRows} counted {ParentEntity} invoice(s): the Decision Record writer's identity is unavailable. "
        + "Check that {ConfigKey} is set on this host (and that the writer's connection and WhoAmI work); spend is not reported as 0.";

    /// <summary>
    /// The paged row read of the parents' invoices of <paramref name="statuses"/>: the invoice key, the parent lookup, the two
    /// <see cref="SpendAmountColumns"/> (a column left out would read as empty and the approved total would fall back to the billed total) and
    /// <paramref name="extraColumns"/>. Filtered on <c>statuscode</c> only (Paid is Inactive and still counts). Ordered for stable paging.
    /// </summary>
    internal static QueryExpression InvoiceRowQuery(
        string parentEntity, IReadOnlyList<int> statuses, IEnumerable<Guid> parentIds, IReadOnlyCollection<string>? extraColumns = null)
    {
        var lookup = ParentLookup(parentEntity);
        var query = new QueryExpression(InvoiceEntity)
        {
            ColumnSet = new ColumnSet(new[] { lookup }.Concat(SpendAmountColumns).Concat(extraColumns ?? []).Distinct(StringComparer.Ordinal).ToArray()),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 },
            NoLock = true,
        };
        query.Criteria.AddCondition(lookup, ConditionOperator.In, parentIds.Cast<object>().ToArray());
        query.Criteria.AddCondition(InvoiceStatusCodeColumn, ConditionOperator.In, statuses.Cast<object>().ToArray());
        query.AddOrder(InvoiceKeyColumn, OrderType.Ascending);
        return query;
    }

    /// <summary>One row per invoice id (a row without an id is dropped): an amount is never taken twice.</summary>
    private static List<Entity> Distinct(IEnumerable<Entity> rows)
    {
        var seen = new HashSet<Guid>();
        return rows.Where(r => r.Id != Guid.Empty && seen.Add(r.Id)).ToList();
    }

    /// <summary>The sum of <see cref="SpendAmount"/> per parent (the parent lookup of <paramref name="parentEntity"/>) over <paramref name="rows"/>.</summary>
    internal static Dictionary<Guid, decimal> SumSpendByParent(string parentEntity, IEnumerable<Entity> rows)
    {
        var lookup = ParentLookup(parentEntity);
        var sums = new Dictionary<Guid, decimal>();
        foreach (var row in rows)
        {
            if (row.GetAttributeValue<EntityReference>(lookup)?.Id is { } parent && parent != Guid.Empty)
            {
                sums[parent] = sums.GetValueOrDefault(parent) + SpendAmount(row);
            }
        }

        return sums;
    }

    internal const string ParentAlias = "parent";
    internal const string SumAlias = "total";

    /// <summary>The grouped aggregate query for the sum of the budget totals, grouped by the parent lookup, over the given parents.</summary>
    internal static string BuildGroupedBudgetFetchXml(string parentEntity, IEnumerable<Guid> parentIds)
    {
        var lookup = ParentLookup(parentEntity);
        var entity = new XElement("entity", new XAttribute("name", BudgetEntity),
            new XElement("attribute",
                new XAttribute("name", BudgetTotalBudget),
                new XAttribute("alias", SumAlias), new XAttribute("aggregate", "sum")),
            new XElement("attribute", new XAttribute("name", lookup), new XAttribute("alias", ParentAlias), new XAttribute("groupby", "true")),
            new XElement("filter", new XAttribute("type", "and"),
                new XElement("condition", new XAttribute("attribute", lookup), new XAttribute("operator", "in"),
                    parentIds.Select(id => new XElement("value", id.ToString("D", CultureInfo.InvariantCulture))))));
        return new XElement("fetch", new XAttribute("aggregate", "true"), entity).ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Parent id to summed amount, from the aliased columns of a grouped aggregate result.</summary>
    /// <exception cref="InvalidOperationException">A parent or sum value of a shape this reader does not know. Fails closed: a row
    /// read as "no spend" would let a Threshold Signal close as cleared on a number nobody computed. A NULL sum (a group of rows
    /// whose amounts are all null) is 0, as <see cref="SumMoney"/> treats a null amount; a NULL parent is a row of no parent
    /// (the filter names parents, so it is skipped).</exception>
    internal static Dictionary<Guid, decimal> ReadGroupedSums(EntityCollection rows)
    {
        var sums = new Dictionary<Guid, decimal>();
        foreach (var row in rows.Entities)
        {
            var parentValue = Unwrap(row.Attributes.TryGetValue(ParentAlias, out var p) ? p : null);
            var parent = parentValue switch
            {
                null => Guid.Empty,
                EntityReference reference => reference.Id,
                Guid id => id,
                _ => throw new InvalidOperationException(
                    "Grouped aggregate row has a parent value of unexpected type " + parentValue.GetType().Name + "."),
            };
            if (parent == Guid.Empty)
            {
                continue;
            }

            var sumValue = Unwrap(row.Attributes.TryGetValue(SumAlias, out var s) ? s : null);
            var amount = sumValue switch
            {
                null => 0m,
                Money money => money.Value,
                decimal value => value,
                _ => throw new InvalidOperationException(
                    "Grouped aggregate row has a sum value of unexpected type " + sumValue.GetType().Name + "."),
            };
            sums[parent] = sums.GetValueOrDefault(parent) + amount;
        }

        return sums;
    }

    private static object? Unwrap(object? value) => value is AliasedValue aliased ? aliased.Value : value;
}
