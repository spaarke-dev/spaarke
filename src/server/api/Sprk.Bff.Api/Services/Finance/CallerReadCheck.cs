using Spaarke.Core.Auth;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>What the CALLER's rights on one record came to.</summary>
internal enum CallerReadOutcome
{
    /// <summary>Dataverse says the caller holds the required right (a degraded, probe-derived Read counts: it is still "the caller can retrieve this row").</summary>
    Readable,

    /// <summary>Dataverse answered and the caller does not hold the right. The record is left out, silently.</summary>
    Refused,

    /// <summary>No answer: the lookup faulted without granting the right, or threw. Never treated as readable.</summary>
    Failed,
}

/// <summary>
/// The caller-Read check the finance read APIs share (ebilling-r1 tasks 106 and 107): "may THIS CALLER Read these other records?", asked per
/// record through <see cref="AuthorizationService.GetCallerRecordAccessAsync"/>, at most <see cref="Parallelism"/> lookups at a time. The
/// project's fail-closed rule lives here once: a lookup that faulted without granting the right, or threw, is <see cref="CallerReadOutcome.Failed"/>,
/// and callers turn any <c>Failed</c> into an unavailable section, never into the unfiltered list. Cancellation propagates.
/// </summary>
internal static class CallerReadCheck
{
    /// <summary>The most rights lookups in flight at once.</summary>
    public const int Parallelism = 8;

    /// <summary>The most records asked about per round of <see cref="ScanAsync{T}"/>.</summary>
    public const int BatchSize = 50;

    /// <summary>What the caller can Read among the rows that were examined.</summary>
    /// <param name="Readable">The readable rows, in the order they were given.</param>
    /// <param name="Failed">True when the caller's rights could not be determined for at least one examined row; <paramref name="Readable"/> is then empty and must not be used.</param>
    /// <param name="AllExamined">True when every given row was asked about (the scan was not stopped by <c>stopAtReadable</c> or <c>maxChecks</c>).</param>
    /// <param name="Checked">How many rows were asked about.</param>
    internal sealed record Scan<T>(IReadOnlyList<T> Readable, bool Failed, bool AllExamined, int Checked);

    /// <summary>Asks about one record. <paramref name="operation"/> is an <see cref="OperationAccessPolicy"/> key (<c>read</c>, <c>finance.read</c>).</summary>
    public static async Task<CallerReadOutcome> CheckAsync(
        AuthorizationService authorization,
        InvoiceContextService.InvoiceCaller caller,
        string entitySet,
        string operation,
        Guid recordId,
        ILogger logger,
        string logContext,
        CancellationToken ct)
    {
        try
        {
            var snapshot = await authorization.GetCallerRecordAccessAsync(
                caller.UserId, entitySet, recordId, caller.AccessToken, ct);

            if (OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, operation))
            {
                return CallerReadOutcome.Readable;
            }

            // No right: Dataverse's answer, unless the lookup itself faulted - then it is not an answer.
            return snapshot.Faulted ? CallerReadOutcome.Failed : CallerReadOutcome.Refused;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "{Context}: the rights check on {EntitySet} {RecordId} faulted", logContext, entitySet, recordId);
            return CallerReadOutcome.Failed;
        }
    }

    /// <summary>
    /// Walks <paramref name="rows"/> in the order given, <see cref="BatchSize"/> at a time, and keeps those the caller can Read. Stops once
    /// <paramref name="stopAtReadable"/> readable rows are in hand or <paramref name="maxChecks"/> rows have been asked about. Any
    /// <see cref="CallerReadOutcome.Failed"/> fails the whole scan. The rows to ask about are the caller's to order (newest first), so the
    /// rows not reached are the oldest.
    /// </summary>
    public static async Task<Scan<T>> ScanAsync<T>(
        AuthorizationService authorization,
        InvoiceContextService.InvoiceCaller caller,
        string entitySet,
        string operation,
        IReadOnlyList<T> rows,
        Func<T, Guid> idOf,
        int stopAtReadable,
        int maxChecks,
        ILogger logger,
        string logContext,
        CancellationToken ct)
    {
        var readable = new List<T>();
        var asked = 0;

        while (asked < rows.Count && asked < maxChecks && readable.Count < stopAtReadable)
        {
            var batch = rows.Skip(asked).Take(Math.Min(BatchSize, maxChecks - asked)).ToList();
            var outcomes = new CallerReadOutcome[batch.Count];

            using var gate = new SemaphoreSlim(Parallelism);
            await Task.WhenAll(batch.Select(async (row, index) =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    outcomes[index] = await CheckAsync(authorization, caller, entitySet, operation, idOf(row), logger, logContext, ct);
                }
                finally
                {
                    gate.Release();
                }
            }));

            if (outcomes.Contains(CallerReadOutcome.Failed))
            {
                logger.LogWarning(
                    "{Context}: the caller's rights could not be determined for {Failed} of {Checked} {EntitySet} records; nothing is shown",
                    logContext, outcomes.Count(o => o == CallerReadOutcome.Failed), batch.Count, entitySet);
                return new Scan<T>([], true, false, asked + batch.Count);
            }

            readable.AddRange(batch.Where((_, index) => outcomes[index] == CallerReadOutcome.Readable));
            asked += batch.Count;
        }

        return new Scan<T>(readable, false, asked >= rows.Count, asked);
    }
}
