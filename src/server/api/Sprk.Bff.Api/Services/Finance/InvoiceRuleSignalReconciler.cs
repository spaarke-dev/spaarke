// ebilling-r1 task 164a-4b - the rule-Signal reconcile of pendingSignalChanges, moved out of InvoiceCorrectionReevaluation (101 B2b-2) so the
// correction's Signals step and Add line's Signals step (step 9) reconcile a recorded plan change through ONE implementation.
//
// Component Justification (CLAUDE.md section 11):
//   (1) Existing - InvoiceCorrectionReevaluation held this as private members (ReconcileRuleAsync, CloseClearedAsync, WriteAsync,
//       RecordWriteRefusals); SignalEpisodeLifecycle and SignalWriter write and close one Signal and are used unchanged.
//   (2) Extension - this IS that code moved, not new surface: a copy in the Add line service would let two reconciles of the same member drift.
//       It takes the writer calls as functions (the lifecycle's shape), so it needs no DI registration.
//   (3) Cost of doing nothing - Add line's plan changes (EB-D-188, R7) would stay unreconciled until the next correction, or need a second copy.

using System.Text.Json.Nodes;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using static Sprk.Bff.Api.Services.Finance.InvoiceFindingsDiff;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// One run's reconcile of <see cref="InvoiceCorrectionReevaluation.PendingSignalChangesMember"/> <c>rule</c> entries (101 B2b-2): a <c>rule</c> entry
/// closes the open Signal whose group is gone as ConditionCleared (record-free, <see cref="SignalWriter.ResolutionConditionCleared"/>) and writes the
/// current plan entry through <see cref="SignalEpisodeLifecycle"/> with <see cref="ResolvedEpisodeRule.ReopenAsNextEpisode"/> when
/// <c>raiseFingerprint</c> is set (equal fingerprints: a refresh, no close; another: Superseded, then episode n+1). Every close and write is
/// idempotent, so a repeat after any failure converges. Created per run: <see cref="Refused"/> collects the writer's permanent refusals of the run.
/// </summary>
internal sealed class InvoiceRuleSignalReconciler
{
    private readonly SignalEpisodeLifecycle _lifecycle;
    private readonly Func<Guid, string, int, string, CancellationToken, Task<SignalCloseOutcome>> _closeSignal;
    private readonly ILogger _logger;
    private readonly SignalLogContext _context;
    private readonly Guid _invoiceId;
    private readonly string? _field;

    /// <param name="field">The corrected field (a field name, never a value): the log lines read "Correction of {Field} on invoice ..." as before the
    /// move (164a fix round K3: the property stays <c>Field</c>). Null for Add line: "Add line on invoice ...".</param>
    internal InvoiceRuleSignalReconciler(
        SignalEpisodeLifecycle lifecycle,
        Func<Guid, string, int, string, CancellationToken, Task<SignalCloseOutcome>> closeSignal,
        ILogger logger,
        SignalLogContext context,
        Guid invoiceId,
        string? field)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _closeSignal = closeSignal ?? throw new ArgumentNullException(nameof(closeSignal));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _context = context;
        _invoiceId = invoiceId;
        _field = field;
    }

    /// <summary>The writer's permanent refusals of this run (049 F1-3): recorded in the findings by <see cref="RecordWriteRefusals"/>.</summary>
    internal List<(SignalWriteRequest Request, string Code)> Refused { get; } = [];

    /// <summary>One <c>rule</c> entry: no raise → close the latest open episode (ConditionCleared); a raise → write the plan entry of <c>raiseFingerprint</c>, which the lifecycle refreshes (same fingerprint) or supersedes (another). A raise with no matching plan entry closes the old episode instead.</summary>
    internal async Task ReconcileRuleAsync(JsonObject root, JsonObject change, CancellationToken ct)
    {
        if (Text(change, "policyCode") is not { Length: > 0 } policyCode || Text(change, "subjectEntity") is not { Length: > 0 } entity
            || !Guid.TryParse(Text(change, "subjectId"), out var subjectId))
        {
            if (_field is not null)
            {
                _logger.LogWarning("Correction of {Field} on invoice {InvoiceId}: a pending Signal change names no policy or subject; skipped.", _field, _invoiceId);
            }
            else
            {
                _logger.LogWarning("Add line on invoice {InvoiceId}: a pending Signal change names no policy or subject; skipped.", _invoiceId);
            }

            return;
        }

        var close = Text(change, "closeFingerprint");
        var raise = Text(change, "raiseFingerprint");
        if (close is not null && raise is null)
        {
            // Raise null: the group is gone, so the latest open episode is closed whatever its fingerprint. A member that survived a failed clear can
            // read close fp0 / raise null while the open episode already carries fp1 (a repeat wrote it); matching fp0 would leave that episode open.
            // Raise set (fp0 -> fp1 while it still fires): nothing is cleared here; the lifecycle write below supersedes the open fp0 episode and opens
            // the next, as SignalEpisodeLifecycle and the system path do (#2507 F1).
            await CloseClearedAsync(policyCode, entity, subjectId, fingerprint: null, ct).ConfigureAwait(false);
        }

        if (raise is null)
        {
            return;
        }

        var key = PlanKey(policyCode, entity, subjectId.ToString("D"));
        var entry = (root["signalPlan"] as JsonArray ?? []).OfType<JsonObject>()
            .FirstOrDefault(e => PlanKey(Text(e, "policyCode"), Text(e, "subjectEntity"), Text(e, "subjectId")) == key);
        if (entry is null || !string.Equals(Text(entry, "fingerprint"), raise, StringComparison.Ordinal))
        {
            // By construction the plan holds the entry B2b-1 recorded; anything else is not raised (nothing to raise it from).
            if (_field is not null)
            {
                _logger.LogWarning("Correction of {Field} on invoice {InvoiceId}: no plan entry of policy {PolicyCode} on {Subject} {SubjectId} matches the change; not raised.",
                    _field, _invoiceId, policyCode, entity, subjectId);
            }
            else
            {
                _logger.LogWarning("Add line on invoice {InvoiceId}: no plan entry of policy {PolicyCode} on {Subject} {SubjectId} matches the change; not raised.",
                    _invoiceId, policyCode, entity, subjectId);
            }

            // Nothing will supersede the old episode, so it is closed here (of the stored fingerprint only).
            if (close is not null && !string.Equals(close, raise, StringComparison.Ordinal))
            {
                await CloseClearedAsync(policyCode, entity, subjectId, close, ct).ConfigureAwait(false);
            }

            return;
        }

        var planned = InvoiceSubmissionAfterSteps.StoredFindings.ParsePlanEntry(entry);
        await WriteAsync(planned.Request, planned.Fingerprint, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes the latest episode of (policy, subject) as ConditionCleared (record-free, as <c>SignalReevaluationJob</c> closes a cleared condition) when
    /// it is open and, if <paramref name="fingerprint"/> is given, carries it. Already resolved, absent, or another fingerprint: nothing is written.
    /// </summary>
    internal async Task CloseClearedAsync(string policyCode, string entity, Guid subjectId, string? fingerprint, CancellationToken ct)
    {
        var latest = await _lifecycle.LatestEpisodeAsync(policyCode, entity, subjectId, ct).ConfigureAwait(false);
        if (latest is not { IsResolved: false } open
            || (fingerprint is not null && !string.Equals(open.Fingerprint, fingerprint, StringComparison.Ordinal)))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(open.RowVersion))
        {
            throw new InvalidOperationException("The open Signal was read without a row version.");
        }

        var outcome = await _closeSignal(open.Id, open.RowVersion, SignalWriter.ResolutionConditionCleared, policyCode, ct).ConfigureAwait(false);
        if (outcome != SignalCloseOutcome.Closed)
        {
            throw new InvalidOperationException("The Signal changed while it was being closed.");
        }
    }

    /// <summary>One write through the option-a lifecycle (B2b: a resolved latest episode reopens as n+1). A permanent refusal (049 F1-3) is recorded and the step goes on; a retryable failure fails the step.</summary>
    internal async Task WriteAsync(SignalWriteRequest request, string fingerprint, CancellationToken ct)
    {
        SignalStep step;
        try
        {
            step = await _lifecycle.WriteAsync(request, fingerprint, _context, _invoiceId, ResolvedEpisodeRule.ReopenAsNextEpisode, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when ((ex is not OperationCanceledException || !ct.IsCancellationRequested)
            && InvoiceSubmissionAfterSteps.PermanentWriteRefusal(request, ex) is { } code)
        {
            step = new SignalStep(false, null, code);
        }

        if (step.RefusedCode is { } refusedCode)
        {
            if (_field is not null)
            {
                _logger.LogError("Correction of {Field} on invoice {InvoiceId}: the Signal of policy {PolicyCode} on {Subject} {SubjectId} is refused permanently ({Code}); recorded in the findings.",
                    _field, _invoiceId, request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId, refusedCode);
            }
            else
            {
                _logger.LogError("Add line on invoice {InvoiceId}: the Signal of policy {PolicyCode} on {Subject} {SubjectId} is refused permanently ({Code}); recorded in the findings.",
                    _invoiceId, request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId, refusedCode);
            }
            Refused.Add((request, refusedCode));
        }

        if (step.FailureCode is { } failure)
        {
            throw new InvalidOperationException($"A Signal write did not complete ({failure}).");
        }
    }

    /// <summary>The writer's permanent refusals, in 049's <see cref="InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember"/> shape, once each.</summary>
    internal static void RecordWriteRefusals(JsonObject root, IReadOnlyList<(SignalWriteRequest Request, string Code)> refused)
    {
        if (refused.Count == 0)
        {
            return;
        }

        if (root[InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember] is not JsonArray stored)
        {
            root[InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember] = stored = [];
        }

        foreach (var (request, code) in refused)
        {
            var entry = new JsonObject
            {
                ["policyCode"] = request.PolicyCode,
                ["subject"] = new JsonObject { ["entity"] = request.SubjectEntityLogicalName, ["id"] = request.SubjectId.ToString("D") },
                ["code"] = code,
            };
            if (!stored.Any(e => JsonNode.DeepEquals(e, entry)))
            {
                stored.Add(entry);
            }
        }
    }
}
