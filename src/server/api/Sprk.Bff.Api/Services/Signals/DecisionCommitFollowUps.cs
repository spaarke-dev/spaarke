using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// What the decision commit route does AFTER its writes, none of which may fail the commit (task 043): queue the budget-revision
/// evaluation (task 032) and apply dismissal suppression at once (task 034). Each is logged and counted when it does not run; the
/// nightly pass is the backstop for both.
/// </summary>
/// <remarks>
/// <para><b>Budget revision (032 review of #1660).</b> The commit route, not the executor, queues the BudgetRevision trigger, with
/// no delay: AFTER step 6 (the Signal is Resolved Acted, the record written), so the trigger meets a closed Signal and leaves it
/// alone; and on the failure path once a revision has landed but the Signal was not closed, so the condition is re-evaluated
/// (the revision cleared it).</para>
/// <para><b>Suppression (034 hand-off).</b> After a commit resolves Signals as Dismissed, <c>SignalSuppressionService.ApplyAsync</c>
/// runs for each affected (policy, core record) grain, or (policy, item) for a no-core item, keyed as 034 keys it.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: 032's <see cref="SignalEvaluationTrigger"/> and 034's
/// <see cref="SignalSuppressionService"/> do the work; this class only decides what to hand them from a commit's outcome.
/// Extension: it is the commit route's own after-record step, split out so the route's tests can observe it. Cost of doing
/// nothing: a revision recorded through a decision clears its Work Item only at the next nightly pass, and a third dismissal
/// does not suppress until then.</para>
/// </remarks>
public class DecisionCommitFollowUps
{
    private const string RevisionTable = "sprk_budgetrevision";
    private const string ReviseBudget = "revise-budget";

    private readonly SignalEvaluationTrigger _trigger;
    private readonly SignalSuppressionService _suppression;
    private readonly IGenericEntityService _entities;
    private readonly ILogger<DecisionCommitFollowUps> _logger;

    public DecisionCommitFollowUps(
        SignalEvaluationTrigger trigger,
        SignalSuppressionService suppression,
        IGenericEntityService entities,
        ILogger<DecisionCommitFollowUps> logger)
    {
        _trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
        _suppression = suppression ?? throw new ArgumentNullException(nameof(suppression));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>After a recorded commit: the budget-revision trigger for each revision written, then suppression for each
    /// dismissed Signal's grain. Returns warning codes for what did not run (never throws, except cancellation).</summary>
    public virtual async Task<IReadOnlyList<string>> AfterRecordedAsync(
        Guid? matterId, IReadOnlyList<DecisionCommitStep> executed, IReadOnlyList<DecisionCommitService.SignalRow> dismissed, CancellationToken ct)
    {
        var warnings = new List<string>();
        if (!await EnqueueRevisionsAsync(matterId, executed, ct).ConfigureAwait(false))
            warnings.Add("budget_revision_trigger_not_queued");

        foreach (var (policyId, grain) in Grains(dismissed))
        {
            try
            {
                var policy = await _entities.RetrieveAsync("sprk_policy", policyId, ["sprk_policycode"], ct).ConfigureAwait(false);
                if (policy.GetAttributeValue<string>("sprk_policycode") is not { Length: > 0 } code)
                {
                    _logger.LogWarning(DecisionCommitEvents.AfterRecordFailed,
                        "Decision commit: policy {PolicyId} has no code, so suppression was not applied at once.", policyId);
                    OntologyWriterTelemetry.RecordFailure("decision_commit_suppression_failed");
                    warnings.Add("suppression_not_applied");
                    continue;
                }

                await _suppression.ApplyAsync(policyId, code, grain, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Non-fatal (034 hand-off): the record is written; the nightly pass re-applies.
                _logger.LogWarning(DecisionCommitEvents.AfterRecordFailed, ex,
                    "Decision commit: suppression could not be applied at once for policy {PolicyId}; the nightly pass will.", policyId);
                OntologyWriterTelemetry.RecordFailure("decision_commit_suppression_failed");
                warnings.Add("suppression_not_applied");
            }
        }

        return warnings;
    }

    /// <summary>After a commit that failed before closing its Signal: the budget-revision trigger for any revision that landed,
    /// so the condition the revision cleared is re-evaluated now. Never throws, except cancellation.</summary>
    public virtual async Task AfterFailedCommitAsync(Guid? matterId, IReadOnlyList<DecisionCommitStep> executed, CancellationToken ct) =>
        await EnqueueRevisionsAsync(matterId, executed, ct).ConfigureAwait(false);

    /// <summary>The revisions a commit's steps wrote (the revise-budget step's <c>sprk_budgetrevision</c> row), landed or not.</summary>
    internal static IReadOnlyList<Guid> LandedRevisions(IReadOnlyList<DecisionCommitStep> executed) =>
        executed.Where(s => s.Code == ReviseBudget)
            .SelectMany(s => s.Written)
            .Where(w => w.Entity == RevisionTable)
            .Select(w => w.Id)
            .Distinct()
            .ToList();

    /// <summary>The suppression grains of the dismissed Signals, as 034 keys them: (policy, core record) when the Signal has one,
    /// (policy, item) from its dedupe key otherwise. A Signal without a policy or a readable key has no grain.</summary>
    internal static IReadOnlyList<(Guid PolicyId, SuppressionGrain Grain)> Grains(IReadOnlyList<DecisionCommitService.SignalRow> dismissed)
    {
        var grains = new List<(Guid, SuppressionGrain)>();
        foreach (var row in dismissed)
        {
            if (row.PolicyId is not { } policyId || policyId == Guid.Empty)
                continue;
            if (row.HasCore)
                grains.Add((policyId, SuppressionGrain.ForCore(row.CoreTypeId, row.CoreId!.Value)));
            else if (SignalWriter.TryParseDedupeKey(row.DedupeKey, out _, out var entity, out var subjectId, out _))
                grains.Add((policyId, SuppressionGrain.ForItem(entity, subjectId)));
        }

        return grains.Distinct().ToList();
    }

    private async Task<bool> EnqueueRevisionsAsync(Guid? matterId, IReadOnlyList<DecisionCommitStep> executed, CancellationToken ct)
    {
        var revisions = LandedRevisions(executed);
        if (revisions.Count == 0)
            return true;
        if (matterId is not { } matter || matter == Guid.Empty)
            return false;

        var all = true;
        foreach (var revision in revisions)
        {
            // SignalEvaluationTrigger never throws (it logs and returns false); a false is a delay, not a lost event.
            if (!await _trigger.EnqueueAsync(SignalTriggerKind.BudgetRevision, "sprk_matter", matter, revision, ct: ct).ConfigureAwait(false))
            {
                _logger.LogWarning(DecisionCommitEvents.AfterRecordFailed,
                    "Decision commit: the budget-revision trigger for revision {RevisionId} was not queued; the nightly pass will evaluate the matter.",
                    revision);
                all = false;
            }
        }

        return all;
    }
}
