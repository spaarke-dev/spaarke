using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>Why <see cref="DecisionCallerRights.CheckAsync"/> refused; <see cref="None"/> when the caller may decide.</summary>
internal enum DecisionCallerRefusal
{
    /// <summary>The caller holds every right the decision needs.</summary>
    None,

    /// <summary>The caller lacks AppendTo on the core record.</summary>
    CoreAppendToMissing,

    /// <summary>A Signal's pair id is set but its subject table cannot be derived (task 141 review F3): an unknown subject.</summary>
    SubjectMissing,

    /// <summary>The caller lacks Write on a Signal's subject.</summary>
    SubjectWriteMissing,

    /// <summary>A table's entity set could not be looked up (a metadata fault, not an answer about the caller).</summary>
    Unavailable,
}

/// <summary>
/// FR-51, the rights a person needs to decide on Signals, shared by the commit route (<see cref="DecisionCommitService"/>) and the human
/// billing decisions that close Signals through the app-identity writer (ebilling-r1 tasks 100 and 105: no impersonation, so the caller is
/// authorized here BEFORE any write). Moved verbatim out of <c>DecisionCommitService.CallerMayDecideAsync</c>; each caller maps
/// <see cref="DecisionCallerRefusal"/> to its own refusal, so the commit route's answers are unchanged.
/// </summary>
/// <remarks>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <c>DecisionCommitService.CallerMayDecideAsync</c> (private) and
/// <see cref="DecisionExec.CallerHoldsAsync"/> (one right on one record). Extension: the check could not be called from Finance while it was a
/// private member of the commit route; moving it is the extension, so there is one rule, not two copies. Cost of doing nothing: the billing
/// decisions would close Signals (as the app identity) for a caller who lacks Write on their subjects, or copy the rule and drift.</para>
/// </remarks>
internal static class DecisionCallerRights
{
    /// <summary>The <see cref="OperationAccessPolicy"/> key whose right is Write.</summary>
    internal const string WriteOperation = "write";

    /// <summary>FR-51: the caller must be able to WRITE each Signal's subject and APPEND TO the core record, asked as the caller
    /// through <paramref name="callerHolds"/> (the caller-rights probe). <see cref="DecisionCallerRefusal.None"/> when they may; a missing
    /// right refuses (every "could not answer" from the probe is a no); <see cref="DecisionCallerRefusal.Unavailable"/> when a table's
    /// entity set cannot be looked up. Each distinct subject is asked once; a Signal with no pair id has nothing beyond the core.</summary>
    /// <param name="callerHolds">(entity set, id, operation) → whether the caller holds the operation's right on the record.</param>
    internal static async Task<DecisionCallerRefusal> CheckAsync(
        IGenericEntityService entities,
        ILogger logger,
        Func<string, Guid, string, CancellationToken, Task<bool>> callerHolds,
        DecisionCoreRecord? decisionCore,
        IEnumerable<(string? SubjectEntity, Guid? SubjectId)> subjects,
        CancellationToken ct)
    {
        var sets = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (decisionCore is { } core)
        {
            if (await CallerHoldsOnAsync(entities, logger, callerHolds, sets, core.Entity, core.Id, DecisionExec.AppendToOperation, ct).ConfigureAwait(false) is not { } mayAppend)
                return DecisionCallerRefusal.Unavailable;
            if (!mayAppend)
                return DecisionCallerRefusal.CoreAppendToMissing;
        }

        var checkedSubjects = new HashSet<(string, Guid)>();
        foreach (var row in subjects)
        {
            if (row.SubjectId is not { } subjectId || subjectId == Guid.Empty)
                continue; // nothing beyond the core record to act on
            // Task 141 review F3: a pair id whose subject table cannot be derived (no subject lookup, or two, carry it; for
            // example the subject was deleted and its RemoveLink cascade cleared the lookup) is an unknown subject, never "no
            // subject": the caller's Write on it cannot be shown, so the decision is refused like an unreadable subject.
            if (row.SubjectEntity is not { Length: > 0 } subject)
                return DecisionCallerRefusal.SubjectMissing;
            if (!checkedSubjects.Add((subject, subjectId)))
                continue;
            if (await CallerHoldsOnAsync(entities, logger, callerHolds, sets, subject, subjectId, WriteOperation, ct).ConfigureAwait(false) is not { } mayWrite)
                return DecisionCallerRefusal.Unavailable;
            if (!mayWrite)
                return DecisionCallerRefusal.SubjectWriteMissing;
        }

        return DecisionCallerRefusal.None;
    }

    /// <summary>
    /// <see cref="DecisionExec.CallerHoldsAsync"/> for a caller known by its bearer token rather than its request (the billing services are
    /// handed the token by their routes). Every "could not answer" is a no. Not folded into the <c>HttpContext</c> form: that one reads the
    /// token inside its own catch and is left byte-identical for the executors.
    /// </summary>
    internal static async Task<bool> CallerHoldsAsync(
        CallerRecordAccessProbe probe, string? bearerToken, string entitySet, Guid id, string operation, CancellationToken ct)
    {
        try
        {
            var rights = await probe.GetCallerRightsAsync(bearerToken, entitySet, id, ct).ConfigureAwait(false);
            return OperationAccessPolicy.HasRequiredRights(rights, operation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>True/false: the caller holds / lacks <paramref name="operation"/> on the record. Null: the table's entity set
    /// could not be looked up (a metadata fault, not an answer about the caller). Set names are memoised per check.</summary>
    private static async Task<bool?> CallerHoldsOnAsync(
        IGenericEntityService entities, ILogger logger, Func<string, Guid, string, CancellationToken, Task<bool>> callerHolds,
        Dictionary<string, string?> sets, string table, Guid id, string operation, CancellationToken ct)
    {
        if (!sets.TryGetValue(table, out var set))
        {
            try
            {
                set = await entities.GetEntitySetNameAsync(table, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning("Caller rights: the entity set of {Table} could not be looked up ({ExceptionType}).", table, ex.GetType().Name);
                return null;
            }

            sets[table] = set;
        }

        return !string.IsNullOrEmpty(set) && await callerHolds(set, id, operation, ct).ConfigureAwait(false);
    }
}
