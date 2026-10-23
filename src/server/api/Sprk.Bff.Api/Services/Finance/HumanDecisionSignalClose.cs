using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The human-decision Signal close shared by the line decisions (task 100) and the override / return of a blocked submission (task 105),
/// in the binding shape agreed with ontology-r1: no impersonation (the app-identity <see cref="SignalWriter"/> closes), the caller
/// authorized first by the commit route's own rule (<see cref="DecisionCallerRights"/>), every Signal closed as Acted naming the decider,
/// and a failed close only warns.
/// </summary>
/// <remarks>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="DecisionCallerRights"/> (the rule) and
/// <see cref="SignalWriter.CloseActedByDecisionAsync"/> (one close); 049's close loop retries instead of warning. Extension: both are called
/// as they are; this only holds the two callers' identical loop and the token form of the rights question. Cost of doing nothing: the two
/// services each copy the loop and the warning rule, which then drift.</para>
/// </remarks>
internal static class HumanDecisionSignalClose
{
    /// <summary>The close the services call: <see cref="SignalWriter.CloseActedByDecisionAsync"/> (tests pass a fake).</summary>
    internal delegate Task<SignalAutomatedCloseOutcome> CloseActed(
        Guid signalId, Guid decisionRecordId, DateTime setReadAtUtc, string policyCode, Guid? decidedByUserId, CancellationToken ct);

    /// <summary>
    /// FR-51 for a human billing decision that closes Signals: AppendTo on <paramref name="core"/> and Write on every subject, asked as the
    /// caller (<paramref name="callerBearerToken"/>) through the caller-rights probe. Every "could not answer" is a no.
    /// <para><b>A line subject is asked on its parent invoice</b> (ontology-r1 2026-10-20: line subject → Write on the parent invoice, D-90).
    /// Core User and Basic User hold only Read on <c>sprk_billingevent</c> (the application writes lines, D-90), so Write on a line would
    /// refuse every ordinary reviewer; the routes already require Write on the invoice (D-77). Every <c>sprk_billingevent</c> subject is
    /// asked as (<c>sprk_invoice</c>, <paramref name="invoiceId"/>); AppendTo on the core and Write on any other subject are unchanged. The
    /// caller PROVES each line is a line of <paramref name="invoiceId"/> before calling (100: the decided lines are read by the invoice; 105:
    /// row-derived subjects checked against the invoice's lines). <see cref="DecisionCallerRights"/> itself is not changed: the commit route
    /// still asks Write on every subject as it did.</para>
    /// </summary>
    internal static Task<DecisionCallerRefusal> AuthorizeAsync(
        IGenericEntityService entities,
        CallerRecordAccessProbe probe,
        string? callerBearerToken,
        DecisionCoreRecord? core,
        Guid invoiceId,
        IEnumerable<(string SubjectEntity, Guid SubjectId)> subjects,
        ILogger logger,
        CancellationToken ct) =>
        DecisionCallerRights.CheckAsync(
            entities,
            logger,
            (set, id, operation, token) => DecisionCallerRights.CallerHoldsAsync(probe, callerBearerToken, set, id, operation, token),
            core,
            subjects.Select(s => SubjectAskedOf(s, invoiceId)),
            ct);

    /// <summary>The record whose Write a Signal subject needs: a line's is its parent invoice's (ontology-r1 2026-10-20, D-90); any other
    /// subject's is its own. With no invoice id the line itself is asked (fails closed: an empty id would otherwise ask nothing).</summary>
    internal static (string? SubjectEntity, Guid? SubjectId) SubjectAskedOf((string SubjectEntity, Guid SubjectId) subject, Guid invoiceId) =>
        invoiceId != Guid.Empty && string.Equals(subject.SubjectEntity, InvoiceSubmissionPersister.LineEntity, StringComparison.Ordinal)
            ? (InvoiceReviewService.InvoiceEntity, invoiceId)
            : (subject.SubjectEntity, subject.SubjectId);

    /// <summary>
    /// Closes every Signal of the stored <paramref name="set"/> as Acted by <paramref name="recordId"/>, naming <paramref name="decider"/>.
    /// Closed and AlreadyClosedByThisRecord (a retry) are done; a Signal that no longer exists has nothing to close; LeftAsIs (resolved
    /// otherwise) is one Information line. A ChangedSinceRead or any fault is ONE Warning and the next Signal is still attempted: the
    /// decision stands, and a retry of it re-runs this over the same stored set. Returns how many were not closed for a fault or a change.
    /// Ids and codes only in the logs.
    /// </summary>
    internal static async Task<int> CloseSetAsync(
        CloseActed close,
        InvoiceSubmissionAfterSteps.ResolvedSignalSet set,
        Guid recordId,
        Guid decider,
        ILogger logger,
        CancellationToken ct)
    {
        var failed = 0;
        foreach (var signal in set.Signals)
        {
            SignalAutomatedCloseOutcome outcome;
            try
            {
                outcome = await close(signal.Id, recordId, set.SetReadAtUtc, signal.PolicyCode, decider, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNotFound(ex))
            {
                logger.LogInformation("Signal {SignalId} (policy {PolicyCode}) listed in Decision Record {RecordId} no longer exists; nothing to close.",
                    signal.Id, signal.PolicyCode, recordId);
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failed++;
                logger.LogWarning("Signal {SignalId} (policy {PolicyCode}) was not closed by Decision Record {RecordId} ({ExceptionType}); the decision stands, a retry closes it.",
                    signal.Id, signal.PolicyCode, recordId, ex.GetType().Name);
                continue;
            }

            switch (outcome)
            {
                case SignalAutomatedCloseOutcome.LeftAsIs:
                    logger.LogInformation("Signal {SignalId} (policy {PolicyCode}) was resolved otherwise; it stays listed in Decision Record {RecordId}.",
                        signal.Id, signal.PolicyCode, recordId);
                    break;
                case SignalAutomatedCloseOutcome.ChangedSinceRead:
                    failed++;
                    logger.LogWarning("Signal {SignalId} (policy {PolicyCode}) kept changing and was not closed by Decision Record {RecordId}; the decision stands, a retry closes it.",
                        signal.Id, signal.PolicyCode, recordId);
                    break;
            }
        }

        return failed;
    }

    private static bool IsNotFound(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver.IsRecordNotFound(e))
            {
                return true;
            }
        }

        return false;
    }
}
