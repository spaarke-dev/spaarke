// ebilling-r1 task 103 (D-78, FR-11; ADR-036 A1, ADR-052; notes 103-approval-worklist.md).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — EligibleApproverResolver resolves and stores one portion's approvers; approve re-resolves live and GET rechecks on open.
//       Nothing re-resolves a portion nobody opens after an assignment field changes (no Dataverse change event reaches the BFF; ADR-002
//       rules out a plugin).
//   (2) Extension — the resolver is request-scoped and has no notion of "every pending portion"; the ADR-036 host already owns schedules,
//       leases, retries and heartbeats, so this is one IScheduledJob on it, not a new mechanism.
//   (3) Cost of doing nothing — after a re-assignment the "Invoices to review" view keeps listing the invoice for the old approver and never
//       for the new one until someone opens it (FR-11 "re-resolved when assignment fields or the role field change before a decision").
//
// Placement (bff-extensions.md): BFF, B2 Dataverse-heavy domain code on the ADR-036 host (ADR-052: no Function or Container Apps Job signal:
// short, idempotent, Dataverse-only). No endpoint, no package.

using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// D-78's refresh path: every 15 minutes, re-resolves the eligible approvers of every Pending approval portion whose invoice is In Review and
/// mirrors them onto the N:N (<see cref="EligibleApproverResolver.SyncAsync"/>), so the view follows assignment changes without a change event.
/// </summary>
/// <remarks>
/// <para><b>ADR-036 A1.</b> Registered through <c>AddScheduledJob</c> (rule 6); one dispatch per tick under the host's lease (rule 1); a scope per
/// run; one heartbeat per attempt (rule 5); it THROWS when no progress is possible (the portion query faults, rule 4) and otherwise counts unit
/// faults and completes. Rule 3: each unit's writes are a diff of the N:N plus the stamp, so a re-run converges and a run over unchanged data
/// writes nothing. The eBilling flag is read by the job itself (ADR-018).</para>
/// <para><b>Cost.</b> Per portion: one core-record read, one contact read and one N:N read, cached per core record within a run (several invoices
/// on one matter resolve once); a write only when the set changed. Approve never depends on this job: it resolves live.</para>
/// </remarks>
public sealed class EligibleApproverRefreshJob : IScheduledJob
{
    public const string JobIdConstant = "finance-eligible-approver-refresh";

    /// <summary>Every 15 minutes, off the quarter hour (D-78 cadence, notes 103).</summary>
    internal const string DefaultCronSchedule = "7,22,37,52 * * * *";

    /// <summary>The per-attempt heartbeat.</summary>
    public static readonly EventId Heartbeat = new(9103, "EligibleApproverRefreshHeartbeat");

    /// <summary>Portions per page (a test may lower it to exercise paging).</summary>
    internal int PageSize { get; init; } = 500;

    /// <summary>Most pages per run; later portions wait for the next run (a test may lower it).</summary>
    internal int MaxPages { get; init; } = 20;

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<EbillingOptions> _ebilling;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EligibleApproverRefreshJob> _logger;

    public EligibleApproverRefreshJob(
        IServiceScopeFactory scopeFactory, IOptions<EbillingOptions> ebilling, TimeProvider timeProvider, ILogger<EligibleApproverRefreshJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _ebilling = ebilling ?? throw new ArgumentNullException(nameof(ebilling));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobId => JobIdConstant;

    public string DisplayName => "Finance Eligible Approver Refresh";

    public string Description =>
        "Re-resolves the eligible approvers of every pending invoice approval from its core record's approver roles and assignment fields, and "
        + "updates the stored set the 'Invoices to review' view filters on (ebilling-r1 task 103, D-78).";

    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = _timeProvider.GetTimestamp();
        var counts = new RefreshCounts();

        if (!_ebilling.Value.Enabled)
        {
            counts.SkippedReason = "ebilling-disabled";
            LogHeartbeat(context, counts);
            return new JobRunResult(true, null, 0, _timeProvider.GetElapsedTime(started), Serialize(counts), Skipped: true);
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var entities = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();
            var resolver = scope.ServiceProvider.GetRequiredService<EligibleApproverResolver>();

            // A fault here means no progress is possible this attempt (A1-4: throw, the host retries).
            var portions = await ReadPendingPortionsAsync(entities, counts, cancellationToken).ConfigureAwait(false);
            var cache = new Dictionary<(string, Guid), EligibleApproverSet>();
            foreach (var portion in portions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RefreshOneAsync(resolver, portion, cache, counts, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            counts.Failed = true;
            LogHeartbeat(context, counts);
            throw;
        }

        LogHeartbeat(context, counts);
        return new JobRunResult(
            true,
            counts.UnitFaults > 0 ? $"{counts.UnitFaults} approvals faulted and were skipped; the next run revisits them." : null,
            counts.Changed,
            _timeProvider.GetElapsedTime(started),
            Serialize(counts));
    }

    private async Task RefreshOneAsync(
        EligibleApproverResolver resolver, Entity portion, Dictionary<(string, Guid), EligibleApproverSet> cache, RefreshCounts counts, CancellationToken ct)
    {
        counts.Examined++;
        if (CoreOf(portion) is not { } core)
        {
            // 013 guarantees exactly one core lookup per portion; a row without one cannot be resolved (and is never "anyone").
            counts.NoCoreRecord++;
            _logger.LogWarning("Eligible approver refresh: approval {ApprovalId} names no single core record; skipped.", portion.Id);
            return;
        }

        try
        {
            if (!cache.TryGetValue(core, out var set))
            {
                set = await resolver.ResolveAsync(core.Entity, core.Id, ct).ConfigureAwait(false);
                cache[core] = set;
            }

            if (set.NoEligibleApprover)
            {
                counts.NoEligibleApprover++;
            }

            var sync = await resolver.SyncAsync(portion.Id, set, portion.GetAttributeValue<DateTime?>(EligibleApproverResolver.ResolvedOnColumn), ct).ConfigureAwait(false);
            if (sync.Changed)
            {
                counts.Changed++;
            }
            else if (sync.Stamped)
            {
                counts.Stamped++;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            counts.UnitFaults++;
            _logger.LogWarning("Eligible approver refresh: approval {ApprovalId} faulted ({ExceptionType}); kept for the next run.", portion.Id, ex.GetType().Name);
        }
    }

    /// <summary>The portion's one core record (013: exactly one of the regarding trio).</summary>
    internal static (string Entity, Guid Id)? CoreOf(Entity portion)
    {
        var found = InvoiceFamilyCoreRecordGuard.CoreRecordLookups[EligibleApproverResolver.ApprovalEntity]
            .Select(l => (l.CoreEntity, Ref: portion.GetAttributeValue<EntityReference>(l.Column)))
            .Where(x => x.Ref is not null && x.Ref.Id != Guid.Empty)
            .ToList();
        return found.Count == 1 ? (found[0].CoreEntity, found[0].Ref!.Id) : null;
    }

    private async Task<List<Entity>> ReadPendingPortionsAsync(IGenericEntityService entities, RefreshCounts counts, CancellationToken ct)
    {
        var columns = InvoiceFamilyCoreRecordGuard.CoreRecordLookups[EligibleApproverResolver.ApprovalEntity].Select(l => l.Column)
            .Append(EligibleApproverResolver.ResolvedOnColumn)
            .ToArray();
        var query = new QueryExpression(EligibleApproverResolver.ApprovalEntity)
        {
            ColumnSet = new ColumnSet(columns),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 },
        };
        query.Criteria.AddCondition(EligibleApproverResolver.ApprovalStatus, ConditionOperator.Equal, InvoiceSubmissionPersister.ApprovalStatusPending);
        var invoice = query.AddLink(InvoiceReviewReadService.InvoiceEntity, EligibleApproverResolver.ApprovalInvoice, InvoiceReviewReadService.InvoiceEntity + "id");
        invoice.LinkCriteria.AddCondition("statuscode", ConditionOperator.Equal, InvoiceStatusCodes.InReview);
        query.AddOrder(EligibleApproverResolver.ApprovalEntity + "id", OrderType.Ascending);

        var rows = new List<Entity>();
        for (var page = 1; ; page++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            rows.AddRange(result.Entities);
            if (!result.MoreRecords)
            {
                break;
            }

            if (page >= MaxPages)
            {
                counts.Truncated = true;
                _logger.LogWarning("Eligible approver refresh read {Pages} pages of {PageSize} approvals and stopped; later approvals wait for the next run.", MaxPages, PageSize);
                break;
            }

            query.PageInfo.PageNumber = page + 1;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }

        return rows;
    }

    private void LogHeartbeat(JobRunContext context, RefreshCounts counts) =>
        _logger.LogInformation(
            Heartbeat,
            "Eligible approver refresh attempt {Attempt}: failed={Failed} skipped={Skipped} examined={Examined} changed={Changed} stamped={Stamped} "
            + "noEligible={NoEligible} noCore={NoCore} unitFaults={UnitFaults} truncated={Truncated} correlationId={CorrelationId}",
            context.Attempt, counts.Failed, counts.SkippedReason ?? "no", counts.Examined, counts.Changed, counts.Stamped,
            counts.NoEligibleApprover, counts.NoCoreRecord, counts.UnitFaults, counts.Truncated, context.CorrelationId);

    private static string Serialize(RefreshCounts counts) => JsonSerializer.Serialize(counts, ResultJsonOptions);

    /// <summary>The run's counts (ResultJson).</summary>
    internal sealed class RefreshCounts
    {
        public string? SkippedReason { get; set; }
        public bool Failed { get; set; }
        public int Examined { get; set; }
        public int Changed { get; set; }
        public int Stamped { get; set; }
        public int NoEligibleApprover { get; set; }
        public int NoCoreRecord { get; set; }
        public int UnitFaults { get; set; }
        public bool Truncated { get; set; }
    }
}
