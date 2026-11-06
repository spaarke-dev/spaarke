// ebilling-r1 task 095d-2b-2 (ontology D-201 095d-2 rulings Q1 and Q2, 2026-11-05; EB-D-186, EB-D-187; notes/095d2-brief.md section 3 step 4):
// the re-run's after-commit Signal steps.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceSubmissionAfterSteps writes an intake's Signals (its plan and its system findings, through SignalEpisodeLifecycle);
//       InvoiceCorrectionReevaluation re-maps one corrected field's Signals (ReconcileSystemAsync, CloseClearedAsync); SignalWriter writes,
//       closes and links one Signal. None of them re-maps a re-run's stored outcome or closes the shared hold Signal when the last hold reason
//       is gone.
//   (2) Extension — this is a new step of the existing re-run (a partial of InvoiceSubmissionRerun, which owns the assignFirmRerun state it moves
//       on), built from those parts unchanged: SignalEpisodeLifecycle for every write (Q2), InvoiceOpenHolds.ReasonsAsync for the close condition,
//       SignalWriter.CloseAsync and LinkConditionClearedRecordAsync for the close and its record link (Q1). The intake after-steps cannot run it:
//       they finish a Processing submission (terminal status last), and the re-run's submission is already terminal.
//   (3) Cost of doing nothing — after the re-run commits, the shared BILL-SYSTEM-SUBMISSION Signal still says "held for its firm" though the
//       hold is gone, and the re-run's new findings (a Procedure gap, a rule finding once the firm's rules run) raise no Signal at all.
//
// Placement (bff-extensions.md): BFF, Services/Finance/Submission, beside the commit it follows; no DI registration (the re-run is registered),
// no endpoint, no package, no AI call.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>What one run of <see cref="InvoiceSubmissionRerun.RemapSignalsAsync"/> did (task 095d-2b-2).</summary>
public enum InvoiceSubmissionRerunSignalsStatus
{
    /// <summary>The Signals were re-mapped and the state moved to <see cref="InvoiceSubmissionRerun.StateDone"/>.</summary>
    Done,

    /// <summary>The state was already <see cref="InvoiceSubmissionRerun.StateDone"/>: nothing was read beyond the submission, nothing written.</summary>
    AlreadyDone,

    /// <summary>A Signal step did not complete (a fault, a Signal that changed under its close, the assign-firm record not found yet): the
    /// state stays committed and a redelivery runs the steps again. Every step is idempotent.</summary>
    Retry,

    /// <summary>Permanent: the stored state cannot be read as a committed re-run of this submission. Nothing was written (Permanent=true).</summary>
    Refused,
}

/// <summary>The typed outcome of <see cref="InvoiceSubmissionRerun.RemapSignalsAsync"/>. Counts and codes only (never row data).</summary>
public sealed record InvoiceSubmissionRerunSignalsResult(
    InvoiceSubmissionRerunSignalsStatus Status, string Code, int Written = 0, int Refused = 0, bool ConditionCleared = false);

/// <summary>Task 095d-2b-2: the after-commit Signal steps (see <see cref="RemapSignalsAsync"/>).</summary>
public sealed partial class InvoiceSubmissionRerun
{
    /// <summary><see cref="StateMember"/>'s state once the after-commit Signal steps finished (095d-2b-2): a later delivery does nothing.</summary>
    public const string StateDone = "done";

    /// <summary><see cref="StateMember"/>'s member that records the shared Signal this re-run decided to close as ConditionCleared, written
    /// BEFORE the close (ontology Q1): a retry links exactly that Signal to the assign-firm record, and only a Signal this re-run decided to
    /// close is ever linked.</summary>
    internal const string ConditionClearedMember = "conditionCleared";

    /// <summary>The Signal steps' own codes (logs and the job; never shown to a partner).</summary>
    internal const string SignalsDoneCode = "ebilling.rerun.signals_done";
    internal const string SignalsAlreadyDoneCode = "ebilling.rerun.signals_already_done";
    internal const string SignalsUnavailableCode = "ebilling.rerun.signals_unavailable";

    /// <summary>The assign-firm Decision Record (<see cref="BillingDecisionKey"/>: <c>sprk_invoice</c>, the invoice,
    /// <see cref="InvoiceAssignFirmService.DecisionIdFor"/>) was not found: the close waits for it (brief R10: no record, a retry).</summary>
    internal const string AssignFirmRecordMissingCode = "ebilling.rerun.assign_firm_record_missing";

    /// <summary>The submission changed between the Signal steps' read and their state write (the row version moved): a retry.</summary>
    internal const string SignalsStateChangedCode = "ebilling.rerun.signals_state_changed";

    private static readonly string[] SignalStepSubmissionColumns =
    [
        InvoiceSubmissionPersister.SubmissionInvoice, InvoiceSubmissionPersister.SubmissionFindingsJson,
        InvoiceSubmissionPersister.SubmissionAdminAttention, InvoiceTotalsService.VersionNumber,
    ];

    /// <summary>
    /// Task 095d-2b-2: the re-run's after-commit Signal steps, repeatable from the stored state (the job runs them after the commit and
    /// after the totals, and again on every redelivery while <see cref="StateMember"/> is <see cref="StateCommitted"/>). In order:
    /// <list type="number">
    /// <item><b>Rule Signals</b>: each entry of the committed <c>signalPlan</c> through <see cref="SignalEpisodeLifecycle"/> (D-13 key, D-12
    /// lifecycle, EB-D-187: a new hold raises its Signal like any other). A would-be rejection held for review (EB-D-187) is written as any
    /// held finding: no Decision Record is written or claimed, and no Signal is closed as Acted.</item>
    /// <item><b>System Signals on the lines</b>: the committed system findings re-mapped under <c>BILL-SYSTEM-SUBMISSION</c> (049's read of the
    /// policy); each line's Signal written through the lifecycle (Q2: a changed reason set is Superseded, record-free, and episode n+1 opens;
    /// an unchanged one is refreshed, only <c>sprk_lastevaluated</c>); an open system Signal on a line that has no finding left is closed
    /// ConditionCleared, record-free.</item>
    /// <item><b>The shared hold Signal</b> (<c>BILL-SYSTEM-SUBMISSION</c> on the invoice): when this submission's invoice-level group is not
    /// empty, a lifecycle write (Q2). When it is empty, the Signal is closed ConditionCleared ONLY when <see cref="InvoiceOpenHolds.ReasonsAsync"/>
    /// over every linked submission is empty too (T19); otherwise it is left as it is (a reason of another linked submission keeps it open:
    /// fail toward open). A close is linked to the assign-firm Decision Record with <see cref="SignalWriter.LinkConditionClearedRecordAsync"/>
    /// (Q1: <c>resolvedBy</c> = the assign-firm decider, <c>expectedPriorRecordId</c> = null; the record's <c>ResolvedSignalIds</c> stays
    /// empty, so the link is the Signal pointing to the record). The Signal is recorded in <see cref="ConditionClearedMember"/> before the
    /// close, so a Signal another path clears is never linked, and a retry after the close links exactly the one this re-run closed.
    /// It is never closed as Acted.</item>
    /// <item><b>Last</b>, one conditional write of the submission: <see cref="StateMember"/> = <see cref="StateDone"/>, with the writer's
    /// permanent refusals (<see cref="InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember"/>) and <c>sprk_adminattention</c> (D-133).</item>
    /// </list>
    /// <para><b>Never a hold write.</b> These steps write Signals and the submission's findings and attention text only: never the invoice, a
    /// line, the reason code or the firm hold, so a Signal fault can neither lift nor add a hold or the partner withhold. A retryable failure
    /// leaves the state committed (the job retries; on the final delivery it dead-letters for a replay); a permanent writer refusal is
    /// recorded and the run goes on (049 F1-3).</para>
    /// <para><b>No sprk_signal successor link</b> (ontology R16): a superseded episode is closed Superseded, record-free, and the next episode
    /// is found by its dedupe key.</para>
    /// <para><b>Logs (ADR-015):</b> ids, counts and codes only.</para>
    /// </summary>
    public async Task<InvoiceSubmissionRerunSignalsResult> RemapSignalsAsync(Guid submissionId, CancellationToken ct = default)
    {
        var seams = _signals ?? throw new InvalidOperationException("The re-run was built without its Signal seams.");
        var invoiceId = InvoiceSubmissionPersister.InvoiceIdFor(submissionId);

        Entity? submission;
        try
        {
            submission = await ReadSubmissionForSignalsAsync(submissionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return SignalsRetry(submissionId, SignalsUnavailableCode, "read", ex);
        }

        if (submission is null || submission.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice)?.Id != invoiceId)
        {
            return SignalsRefuse(submissionId, InvoiceSubmissionRerunCodes.NotLinked);
        }

        var json = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
        JsonObject root;
        AssignFirmStoredDecision? decision;
        try
        {
            root = (string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject) ?? throw new FormatException("The findings are not an object.");
            decision = AssignFirmStoredDecision.Read(root);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            return SignalsRefuse(submissionId, InvoiceSubmissionRerunCodes.FindingsUnreadable);
        }

        var state = root[StateMember] as JsonObject;
        var stateValue = StoredSubmissionRead.Text(state, "state");
        if (string.Equals(stateValue, StateDone, StringComparison.Ordinal))
        {
            _logger.LogInformation("Re-run of submission {SubmissionId}: the Signal steps already finished; nothing written.", submissionId);
            return new InvoiceSubmissionRerunSignalsResult(InvoiceSubmissionRerunSignalsStatus.AlreadyDone, SignalsAlreadyDoneCode);
        }

        // Only a committed re-run of THIS decision is re-mapped: the stored decision and the state name the decision Set firm derives.
        var decisionId = InvoiceAssignFirmService.DecisionIdFor(submissionId);
        if (!string.Equals(stateValue, StateCommitted, StringComparison.Ordinal)
            || !Guid.TryParse(StoredSubmissionRead.Text(state, "decisionId"), out var stateDecision) || stateDecision != decisionId)
        {
            return SignalsRefuse(submissionId, InvoiceSubmissionRerunCodes.FindingsUnreadable);
        }

        if (decision is null || decision.DecisionId != decisionId
            || !string.Equals(decision.RulesRerun, InvoiceAssignFirmService.RulesRerunCompleted, StringComparison.Ordinal))
        {
            return SignalsRefuse(submissionId, InvoiceSubmissionRerunCodes.StoredDecisionMissing);
        }

        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(json);
        if (stored is null || stored.InvoiceId != invoiceId || !stored.HasSignalPlan)
        {
            return SignalsRefuse(submissionId, InvoiceSubmissionRerunCodes.FindingsUnreadable);
        }

        var lifecycle = new SignalEpisodeLifecycle(
            _entities, seams.WriteSignal, (id, rowVersion, policyCode, token) => seams.CloseSignal(id, rowVersion, SignalWriter.ResolutionSuperseded, policyCode, token), _logger);
        var run = new SignalRun(submissionId, invoiceId, lifecycle) { ClearedSignalId = RecordedClearedSignal(state) };
        string? policyNote = null;
        try
        {
            // ── 1. Rule Signals: the committed plan (EB-D-187: a new hold raises its Signal through the writer, D-13 key) ──
            foreach (var entry in stored.Plan)
            {
                await WriteSignalAsync(run, entry.Request, entry.Fingerprint, ct).ConfigureAwait(false);
            }

            // ── 2 and 3. The system Signals (049's policy read) ──
            var (rule, policyState) = await seams.SystemPolicy(stored.ReceivedOn, ct).ConfigureAwait(false);
            if (rule is null)
            {
                // 049 wrote no system Signal under an unusable policy, so there is nothing to re-map or close (fail toward open); a
                // misconfiguration (not "disabled") is told to the administrator, as 049 does (R5, D-133).
                _logger.LogWarning("Re-run of submission {SubmissionId}: system billing policy {PolicyCode} is {State}; no system Signal is re-mapped.",
                    submissionId, BillingRuleCatalogue.SystemPolicyCode, policyState);
                policyNote = AdminAttentionText.PolicyNote(BillingRuleCatalogue.SystemPolicyCode, policyState);
            }
            else
            {
                await RemapSystemSignalsAsync(run, stored, rule, ct).ConfigureAwait(false);
                if (run.Failure is null && !run.InvoiceGroupWritten)
                {
                    await CloseSharedSignalIfClearedAsync(run, seams, root, decision, rule.PolicyCode, submission, ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            run.Failure ??= SignalsUnavailableCode;
            _logger.LogWarning("Re-run of submission {SubmissionId}: a Signal step faulted ({FaultType}); the state stays committed for a retry.",
                submissionId, ex.GetType().Name);
        }

        // ── 4. Last: the state (done only when every step completed), the permanent refusals and the attention text, in one conditional write ──
        try
        {
            if (!await WriteSignalStateAsync(run, done: run.Failure is null, policyNote, ct).ConfigureAwait(false))
            {
                return SignalsRetry(submissionId, SignalsStateChangedCode, "state", null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return SignalsRetry(submissionId, SignalsUnavailableCode, "state", ex);
        }

        if (run.Failure is { } failure)
        {
            _logger.LogWarning("Re-run of submission {SubmissionId}: {Written} Signals written, {Refused} refused; a step did not complete ({Code}), the state stays committed.",
                submissionId, run.Written, run.Refused.Count, failure);
            return new InvoiceSubmissionRerunSignalsResult(InvoiceSubmissionRerunSignalsStatus.Retry, failure, run.Written, run.Refused.Count, run.ConditionCleared);
        }

        _logger.LogInformation("Re-run of submission {SubmissionId}: Signals re-mapped ({Written} written, {Refused} refused, shared Signal cleared: {Cleared}); state done.",
            submissionId, run.Written, run.Refused.Count, run.ConditionCleared);
        return new InvoiceSubmissionRerunSignalsResult(InvoiceSubmissionRerunSignalsStatus.Done, SignalsDoneCode, run.Written, run.Refused.Count, run.ConditionCleared);
    }

    /// <summary>One run's progress: what was written, refused and closed, and the first retryable failure.</summary>
    private sealed class SignalRun(Guid submissionId, Guid invoiceId, SignalEpisodeLifecycle lifecycle)
    {
        public Guid SubmissionId { get; } = submissionId;
        public Guid InvoiceId { get; } = invoiceId;
        public SignalEpisodeLifecycle Lifecycle { get; } = lifecycle;
        public int Written { get; set; }
        public List<(SignalWriteRequest Request, string Code)> Refused { get; } = [];
        public string? Failure { get; set; }

        /// <summary>This submission's invoice-level system group is not empty and was written (Q2), so the shared Signal is not closed.</summary>
        public bool InvoiceGroupWritten { get; set; }

        public bool ConditionCleared { get; set; }

        /// <summary>The <see cref="ConditionClearedMember"/> the state should carry after this run (null: none).</summary>
        public Guid? ClearedSignalId { get; set; }
    }

    /// <summary>One write through the option-a lifecycle (049's rule: a resolved Signal with the same reason set is not raised again). A
    /// permanent refusal is recorded and the run goes on; a retryable failure is remembered and the remaining Signals are still attempted.</summary>
    private async Task WriteSignalAsync(SignalRun run, SignalWriteRequest request, string fingerprint, CancellationToken ct)
    {
        SignalStep step;
        try
        {
            step = await run.Lifecycle.WriteAsync(request, fingerprint, SignalLogContext.Submission, run.SubmissionId, ResolvedEpisodeRule.SkipWhenSameFingerprint, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when ((ex is not OperationCanceledException || !ct.IsCancellationRequested)
            && InvoiceSubmissionAfterSteps.PermanentWriteRefusal(request, ex) is { } code)
        {
            step = new SignalStep(false, null, code);
        }

        if (step.Wrote)
        {
            run.Written++;
        }

        if (step.RefusedCode is { } refused)
        {
            _logger.LogError("Re-run of submission {SubmissionId}: the Signal of policy {PolicyCode} on {Subject} {SubjectId} is refused permanently ({Code}); recorded in the findings.",
                run.SubmissionId, request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId, refused);
            run.Refused.Add((request, refused));
        }

        run.Failure ??= step.FailureCode;
    }

    /// <summary>
    /// The committed system findings under <paramref name="rule"/>, one Signal per (policy, subject) (<see cref="BillingFindingSignalMapper.MapAll(IReadOnlyList{BillingFinding}, IReadOnlyDictionary{Guid, BillingRuleCandidate}, BillingSignalSubjects, IReadOnlyList{FindingDecision}, SubmissionEvaluationKind?)"/>,
    /// the subjects as 049 names them). Each line's is written; an open system Signal on a line of the invoice with no finding left is closed
    /// ConditionCleared (record-free, as <c>SignalReevaluationJob</c> and 101 close a cleared condition). The invoice's group is written when
    /// it is not empty; when it is empty the shared Signal is left to <see cref="CloseSharedSignalIfClearedAsync"/>.
    /// </summary>
    private async Task RemapSystemSignalsAsync(SignalRun run, InvoiceSubmissionAfterSteps.StoredFindings stored, BillingRuleCandidate rule, CancellationToken ct)
    {
        var findings = stored.SystemFindings(new BillingSystemPolicyVersion(rule.PolicyId, rule.PolicyCode, rule.PolicyVersionId));
        var lines = findings.Where(f => f.Subject.LineIndex is not null).Select(f => f.Subject.LineIndex!.Value).Distinct().ToList();
        var subjects = new BillingSignalSubjects(
            run.InvoiceId,
            stored.InvoiceName,
            lines.ToDictionary(i => i, i => InvoiceSubmissionPersister.LineIdFor(run.SubmissionId, i)),
            lines.ToDictionary(i => i, i => InvoiceSubmissionPersister.LineNameFor(stored.TrackingNumber, i)));
        var mappings = BillingFindingSignalMapper.MapAll(findings, new Dictionary<Guid, BillingRuleCandidate> { [rule.PolicyVersionId] = rule }, subjects);

        var mappedLines = new HashSet<Guid>();
        foreach (var mapping in mappings)
        {
            if (mapping.Request.SubjectEntityLogicalName == BillingFindingSignalMapper.InvoiceEntity)
            {
                run.InvoiceGroupWritten = true;
            }
            else
            {
                mappedLines.Add(mapping.Request.SubjectId);
            }

            await WriteSignalAsync(run, mapping.Request, mapping.Fingerprint, ct).ConfigureAwait(false);
        }

        // A line whose system findings the re-run cleared (a firm code now counts, a code set now resolves): its open Signal is closed. Only
        // THIS submission's lines (their ids derive from it; a line another linked submission added keeps its Signal: fail toward open). One
        // read of their open Signals (049's D-122 read), then the latest episode of each stale one (its row version).
        var lineQuery = new QueryExpression(InvoiceSubmissionPersister.LineEntity) { ColumnSet = new ColumnSet(false) };
        lineQuery.Criteria.AddCondition(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, ConditionOperator.Equal, run.InvoiceId);
        var rows = (await _entities.RetrieveMultipleAsync(lineQuery, ct).ConfigureAwait(false)).Entities.Select(r => r.Id).ToHashSet();
        var ownLines = Enumerable.Range(0, rows.Count).Select(i => InvoiceSubmissionPersister.LineIdFor(run.SubmissionId, i)).Where(rows.Contains).ToList();
        var open = await InvoiceSubmissionAfterSteps.ReadOpenSignalsAsync(_entities, null, ownLines, _logger, ct).ConfigureAwait(false);
        foreach (var (_, entity, subjectId) in open
            .Where(o => o.SubjectEntity == BillingFindingSignalMapper.LineEntity && o.Signal.PolicyCode == rule.PolicyCode && !mappedLines.Contains(o.SubjectId))
            .DistinctBy(o => o.SubjectId))
        {
            await CloseConditionClearedAsync(run, rule.PolicyCode, entity, subjectId, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Closes the latest episode of (policy, subject) as ConditionCleared when it is open (record-free). Already resolved or absent:
    /// nothing. A close answered ChangedSinceRead is a retry.</summary>
    private async Task<SignalEpisodeLifecycle.ExistingEpisode?> CloseConditionClearedAsync(
        SignalRun run, string policyCode, string entity, Guid subjectId, CancellationToken ct)
    {
        var latest = await run.Lifecycle.LatestEpisodeAsync(policyCode, entity, subjectId, ct).ConfigureAwait(false);
        if (latest is not { IsResolved: false } open)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(open.RowVersion)
            || await _signals!.CloseSignal(open.Id, open.RowVersion, SignalWriter.ResolutionConditionCleared, policyCode, ct).ConfigureAwait(false) != SignalCloseOutcome.Closed)
        {
            run.Failure ??= SignalEpisodeLifecycle.SignalChangedCode;
            return null;
        }

        return open;
    }

    /// <summary>
    /// The shared hold Signal when this submission's invoice-level group is empty (brief step 4; ontology Q1; T19): closed ConditionCleared only
    /// when <see cref="InvoiceOpenHolds.ReasonsAsync"/> finds no reason on the invoice over every linked submission, then linked to the
    /// assign-firm Decision Record. The Signal is recorded in the state before the close; a retry with it recorded links it (a close another
    /// path made is never linked, because only a Signal this re-run decided to close is recorded).
    /// </summary>
    private async Task CloseSharedSignalIfClearedAsync(
        SignalRun run, RerunSignalSeams seams, JsonObject root, AssignFirmStoredDecision decision, string policyCode, Entity submission, CancellationToken ct)
    {
        var recorded = RecordedClearedSignal(root[StateMember] as JsonObject);

        var latest = await run.Lifecycle.LatestEpisodeAsync(policyCode, FinanceBaseValues.InvoiceEntity, run.InvoiceId, ct).ConfigureAwait(false);
        if (latest is null)
        {
            return;
        }

        var ours = recorded == latest.Id;
        if (!latest.IsResolved)
        {
            // T19: never a ConditionCleared close while a hold reason remains, whoever's it is (fail toward open).
            var reasons = await OpenHoldReasonsAsync(run.InvoiceId, ct).ConfigureAwait(false);
            if (reasons.Count > 0)
            {
                _logger.LogInformation("Re-run of submission {SubmissionId}: {Count} hold reasons remain on invoice {InvoiceId}; the shared Signal stays open.",
                    run.SubmissionId, reasons.Count, run.InvoiceId);
                return;
            }
        }
        else if (!ours)
        {
            // Resolved by another path (a person, an Override, a later system close): never linked to the assign-firm record (Q1 must-not-fire).
            return;
        }

        // The record exists before the re-run is enqueued (Set firm records, then enqueues); not found yet is a retry (brief R10).
        if (await seams.FindAssignFirmRecord(run.InvoiceId, decision.DecisionId, ct).ConfigureAwait(false) is not { } recordId)
        {
            _logger.LogWarning("Re-run of submission {SubmissionId}: the assign-firm Decision Record of invoice {InvoiceId} was not found; the shared Signal waits for it.",
                run.SubmissionId, run.InvoiceId);
            run.Failure ??= AssignFirmRecordMissingCode;
            return;
        }

        if (!latest.IsResolved)
        {
            if (!ours)
            {
                // Recorded BEFORE the close, conditional on the version read: a retry then links exactly this Signal.
                if (!await WriteClearedIntentAsync(run, latest.Id, submission, ct).ConfigureAwait(false))
                {
                    run.Failure ??= SignalsStateChangedCode;
                    return;
                }

                run.ClearedSignalId = latest.Id;
            }

            if (string.IsNullOrWhiteSpace(latest.RowVersion)
                || await seams.CloseSignal(latest.Id, latest.RowVersion, SignalWriter.ResolutionConditionCleared, policyCode, ct).ConfigureAwait(false) != SignalCloseOutcome.Closed)
            {
                // Changed since the read (another path resolved it, or it was refreshed): withdrawn, so a retry never links a close it did not make.
                run.ClearedSignalId = null;
                run.Failure ??= SignalEpisodeLifecycle.SignalChangedCode;
                return;
            }
        }

        // Q1: resolvedBy = the assign-firm decider, no expected prior record. NotApplicable (a person's decision re-closed it) and ForeignLink
        // (another record is linked; the writer logs and meters it) leave the Signal as it is and are final.
        var link = await seams.LinkRecord(latest.Id, recordId, null, decision.DeciderUserId, policyCode, ct).ConfigureAwait(false);
        if (link == SignalRecordLinkOutcome.ChangedSinceRead)
        {
            run.Failure ??= SignalEpisodeLifecycle.SignalChangedCode;
            return;
        }

        run.ConditionCleared = true;
        _logger.LogInformation("Re-run of submission {SubmissionId}: shared Signal {SignalId} closed ConditionCleared; assign-firm record {RecordId} link: {Link}.",
            run.SubmissionId, latest.Id, recordId, link);
    }

    /// <summary>The shared Signal this re-run recorded before its ConditionCleared close (<see cref="ConditionClearedMember"/>), or null.</summary>
    private static Guid? RecordedClearedSignal(JsonObject? state) =>
        Guid.TryParse(StoredSubmissionRead.Text(state, ConditionClearedMember), out var id) && id != Guid.Empty ? id : null;

    /// <summary><see cref="InvoiceOpenHolds.ReasonsAsync"/> over the invoice as stored now (app-only).</summary>
    private async Task<IReadOnlySet<string>> OpenHoldReasonsAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(FinanceBaseValues.InvoiceEntity) { ColumnSet = new ColumnSet(InvoiceSubmissionPersister.InvoiceVendorOrg), TopCount = 1 };
        query.Criteria.AddCondition(FinanceBaseValues.InvoiceKeyColumn, ConditionOperator.Equal, invoiceId);
        var invoice = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault()
            ?? throw new InvalidOperationException("The invoice could not be read.");
        return await InvoiceOpenHolds.ReasonsAsync(_entities, invoice, ct).ConfigureAwait(false);
    }

    private async Task<Entity?> ReadSubmissionForSignalsAsync(Guid submissionId, CancellationToken ct)
    {
        var query = new QueryExpression(FinanceBaseValues.SubmissionEntity) { ColumnSet = new ColumnSet(SignalStepSubmissionColumns), TopCount = 1 };
        query.Criteria.AddCondition($"{FinanceBaseValues.SubmissionEntity}id", ConditionOperator.Equal, submissionId);
        return (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
    }

    /// <summary>
    /// One conditional write of the submission's findings (re-read with its version first): <see cref="StateMember"/> with
    /// <see cref="ConditionClearedMember"/> as <paramref name="run"/> has it and, when <paramref name="done"/>, <see cref="StateDone"/>; the
    /// permanent refusals; and <c>sprk_adminattention</c> merged (D-133). Nothing is written when nothing changes. False when the row
    /// changed under the write (a retry). Never the reason code, the status or a hold.
    /// </summary>
    private async Task<bool> WriteSignalStateAsync(SignalRun run, bool done, string? policyNote, CancellationToken ct)
    {
        var submission = await ReadSubmissionForSignalsAsync(run.SubmissionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The submission could not be read.");
        return await WriteSignalStateAsync(run, submission, done, policyNote, ct).ConfigureAwait(false);
    }

    /// <summary>The intent write before the shared Signal's close: <see cref="ConditionClearedMember"/> = <paramref name="cleared"/>.</summary>
    private async Task<bool> WriteClearedIntentAsync(SignalRun run, Guid cleared, Entity submissionAtRead, CancellationToken ct)
    {
        var submission = await ReadSubmissionForSignalsAsync(run.SubmissionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The submission could not be read.");

        // The steps decided on the findings read at their start: a change since (a correction re-mapped them) is a retry, never a close over it.
        if (!string.Equals(VersionOf(submission), VersionOf(submissionAtRead), StringComparison.Ordinal))
        {
            return false;
        }

        var previous = run.ClearedSignalId;
        run.ClearedSignalId = cleared;
        var wrote = await WriteSignalStateAsync(run, submission, done: false, policyNote: null, ct).ConfigureAwait(false);
        if (!wrote)
        {
            run.ClearedSignalId = previous;
        }

        return wrote;
    }

    private async Task<bool> WriteSignalStateAsync(SignalRun run, Entity submission, bool done, string? policyNote, CancellationToken ct)
    {
        if (VersionOf(submission) is not { } version
            || submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson) is not { } json
            || JsonNode.Parse(json) is not JsonObject root || root[StateMember] is not JsonObject state)
        {
            throw new InvalidOperationException("The submission's re-run state could not be read back.");
        }

        if (string.Equals(StoredSubmissionRead.Text(state, "state"), StateDone, StringComparison.Ordinal))
        {
            return true; // another delivery finished first
        }

        var before = root.ToJsonString();
        if (run.ClearedSignalId is { } cleared)
        {
            state[ConditionClearedMember] = cleared.ToString("D");
        }
        else
        {
            state.Remove(ConditionClearedMember);
        }

        if (done)
        {
            state["state"] = StateDone;
            state["signalsOn"] = _time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        InvoiceCorrectionReevaluation.RecordWriteRefusals(root, run.Refused);
        var fields = new Dictionary<string, object>();
        var after = root.ToJsonString();
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            if (after.Length > InvoiceSubmissionPersister.FindingsJsonMaxLength)
            {
                throw new InvalidOperationException("The findings do not fit the column.");
            }

            fields[InvoiceSubmissionPersister.SubmissionFindingsJson] = after;
        }

        var current = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention);
        var hasRefusals = AdminAttentionText.FromFindings(root) is not null;
        if ((policyNote is not null || hasRefusals)
            && AdminAttentionText.Merge(current, hasRefusals ? root : null, policyNote) is { } merged
            && !string.Equals(merged, current, StringComparison.Ordinal))
        {
            fields[InvoiceSubmissionPersister.SubmissionAdminAttention] = merged;
        }

        if (fields.Count == 0)
        {
            return true;
        }

        try
        {
            await _entities.UpdateIfRowVersionMatchesAsync(FinanceBaseValues.SubmissionEntity, run.SubmissionId, version, fields, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (IsVersionConflict(ex))
        {
            return false;
        }
    }

    private InvoiceSubmissionRerunSignalsResult SignalsRefuse(Guid submissionId, string code)
    {
        _logger.LogError("Re-run of submission {SubmissionId}: the Signal steps are refused ({Code}); nothing is written (Permanent={Permanent}).",
            submissionId, code, true);
        return new InvoiceSubmissionRerunSignalsResult(InvoiceSubmissionRerunSignalsStatus.Refused, code);
    }

    private InvoiceSubmissionRerunSignalsResult SignalsRetry(Guid submissionId, string code, string step, Exception? fault)
    {
        _logger.LogWarning("Re-run of submission {SubmissionId}: the Signal steps did not complete at {Step} ({Code}, {FaultType}); the state stays committed for a retry.",
            submissionId, step, code, fault?.GetType().Name);
        return new InvoiceSubmissionRerunSignalsResult(InvoiceSubmissionRerunSignalsStatus.Retry, code);
    }
}

/// <summary>
/// The writer calls the re-run's Signal steps use (the ontology-owned writer and recorder are concrete, so they are passed as their calls;
/// the public constructor binds them to <see cref="SignalWriter"/> and <see cref="BillingDecisionRecorder"/>, unchanged).
/// </summary>
/// <param name="WriteSignal"><see cref="SignalWriter.WriteAsync"/>.</param>
/// <param name="CloseSignal"><see cref="SignalWriter.CloseAsync"/>: a system close (Superseded for the lifecycle, ConditionCleared), record-free.</param>
/// <param name="LinkRecord"><see cref="SignalWriter.LinkConditionClearedRecordAsync"/> (signal, record, expected prior record, resolved by, policy).</param>
/// <param name="FindAssignFirmRecord">The assign-firm Decision Record's id of (invoice, decision), or null when none exists
/// (<see cref="BillingDecisionRecorder.FindStoredResolvedSetAsync"/>, read only).</param>
/// <param name="SystemPolicy">049's read of <c>BILL-SYSTEM-SUBMISSION</c> (<see cref="InvoiceSubmissionAfterSteps.ReadSystemPolicyAsync(IGenericEntityService, DateOnly?, CancellationToken)"/>).</param>
internal sealed record RerunSignalSeams(
    Func<SignalWriteRequest, CancellationToken, Task<SignalWriteResult>> WriteSignal,
    Func<Guid, string, int, string, CancellationToken, Task<SignalCloseOutcome>> CloseSignal,
    Func<Guid, Guid, Guid?, Guid?, string, CancellationToken, Task<SignalRecordLinkOutcome>> LinkRecord,
    Func<Guid, Guid, CancellationToken, Task<Guid?>> FindAssignFirmRecord,
    Func<DateOnly?, CancellationToken, Task<(BillingRuleCandidate? Rule, string State)>> SystemPolicy)
{
    /// <summary>The production calls: the writer and the recorder as they are.</summary>
    internal static RerunSignalSeams Of(IGenericEntityService entities, SignalWriter signals, BillingDecisionRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(recorder);
        return new RerunSignalSeams(
            signals.WriteAsync,
            (id, rowVersion, resolution, policyCode, ct) => signals.CloseAsync(id, rowVersion, resolution, policyCode, ct),
            signals.LinkConditionClearedRecordAsync,
            async (invoiceId, decisionId, ct) =>
                (await recorder.FindStoredResolvedSetAsync(InvoiceAssignFirmService.AssignFirmActionCode, invoiceId, decisionId, ct).ConfigureAwait(false))?.RecordId,
            (receivedOn, ct) => InvoiceSubmissionAfterSteps.ReadSystemPolicyAsync(entities, receivedOn, ct));
    }
}
