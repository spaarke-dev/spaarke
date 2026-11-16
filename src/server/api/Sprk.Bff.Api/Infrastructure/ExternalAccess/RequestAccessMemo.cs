using System.Collections.Concurrent;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

// GitHub #2845 — the per-REQUEST memo of the access evaluator's repeated reads.
//
// Every /api/v1/external request composes the caller three times (project, matter, work assignment), and each composition
// re-read the same subject-level answers: the contact's state, its removed marker, its grant set, its organization
// memberships, and the deny-list rows for the same (subject, organization) pairs. This memo lets the three compositions
// share those answers inside ONE request. It is the generalization of the contact-state memo task 137 kept in
// HttpContext.Items, which it replaces (Items is a plain Dictionary and the compositions now run concurrently).
//
// The three rules it exists to keep:
//   1. LIVE PER REQUEST, NEVER ACROSS REQUESTS. It lives in the request's HttpContext.Items, so it dies with the request
//      (NoAccessListReader / AccessibleRecordSetService: an access answer is never cached across requests). Outside a
//      request (a scheduled job, a queue handler) there is no memo and every read is live, as before.
//   2. A FAULT IS NEVER REMEMBERED. Each caller says which answers may be remembered (a proven read); a thrown read, a
//      cancelled read and an "unknown" answer that a fail-closed veto is built from are removed before anyone else can be
//      served them. A caller that was waiting on a read which turned out to be a fault asks again itself.
//   3. A WRITE FORGETS. Clear() is called by every in-request writer of a remembered answer (the grant-cache invalidation
//      routine every grant writer calls, and the removed-marker writes), so a read after the write goes back to Dataverse.
//
// Round 2 (GetOrReadManyAsync): the BATCH reads are memoized PER ID — a record's own flags, its referenced organizations, the
// filing walk's row and parent reads, and the systemuser veto's secure-record subjects — so a project's or work assignment's
// secure ancestor (a matter the matter composition already read) is not read again. Only the read-time compositions use
// them (AccessibleRecordSetService / EffectiveRootFlags); the write-time checks read live, as before. Rule 2 applies per id:
// a batch that throws is remembered for no id; a batch that answers is remembered for each id whose own answer is proven.

/// <summary>
/// The per-request memo of the access evaluator's subject-level reads (#2845). Thread-safe: the three per-type compositions
/// of one request share it concurrently. A read is shared only once it is known to be an answer; faults are never shared.
/// </summary>
internal sealed class RequestAccessMemo
{
    /// <summary>The <c>HttpContext.Items</c> key the request's memo is kept under.</summary>
    internal const string HttpContextItemsKey = "uac:request-access-memo";

    private readonly ConcurrentDictionary<string, object> _entries = new(StringComparer.Ordinal);

    internal RequestAccessMemo(DataverseConcurrencyGate? dataverseGate = null) => DataverseGate = dataverseGate;

    /// <summary>
    /// #2845 F1: the process-wide gate on concurrent app-only Dataverse reads (<see cref="DataverseConcurrencyGate"/>), for
    /// the reads made through this memo that do not go through the gated Web API clients (the filing walk's
    /// <c>RetrieveMultipleAsync</c>). <c>null</c> when the request has no service provider (a test): those reads are ungated.
    /// </summary>
    internal DataverseConcurrencyGate? DataverseGate { get; }

    /// <summary>
    /// The memo of the request <paramref name="context"/> belongs to, created on first use; <c>null</c> when there is no
    /// request (no memo: every read is live).
    /// </summary>
    /// <remarks>
    /// <b>Safe on a first touch from any thread (#2851, enforced by #2845 F-R2).</b> The <c>HttpContext.Items</c> getter is
    /// itself lazy (the items feature and its dictionary are created on first touch, unsynchronized), so two concurrent FIRST
    /// touches on a request where nothing has touched <c>Items</c> yet could each get their own dictionary: a lost memo, or a
    /// corrupted feature collection that turns a read into "unknown" and the request into a 503. This method therefore locks
    /// the request itself before it touches <c>Items</c> (then <c>Items</c>, the order <c>CallerWhoAmIMemo</c> uses), so the
    /// first touch is single-threaded whoever calls first. The pre-creates before each fan-out (the caller-principal
    /// strategies, each <see cref="AccessibleRecordSetService"/> entry, the deny-list reader) remain, and are harmless.
    /// </remarks>
    internal static RequestAccessMemo? For(HttpContext? context)
    {
        if (context is null)
        {
            return null;
        }

        // #2845 F-R2: lock the REQUEST first, then Items — the same order as CallerWhoAmIMemo. The request lock makes the
        // first touch of the lazy Items getter single-threaded whoever calls first (the pre-creates before each fan-out stay,
        // harmless); the Items lock excludes the other writers of the dictionary. Items is a plain dictionary: the only Items
        // writers reachable while the compositions run (this method, CallerWhoAmIMemo) lock it; other filters write Items
        // outside the compositions. Any new Items writer reachable from a composition must take the same two locks.
        lock (context)
        {
            var items = context.Items;
            lock (items)
            {
                if (items.TryGetValue(HttpContextItemsKey, out var existing) && existing is RequestAccessMemo memo)
                {
                    return memo;
                }

                // #2845 F1: the process-wide Dataverse read gate travels with the memo to the filing walk's reads.
                var created = new RequestAccessMemo(context.RequestServices?.GetService<DataverseConcurrencyGate>());
                items[HttpContextItemsKey] = created;
                return created;
            }
        }
    }

    /// <summary>The number of remembered (or in-flight) reads. Diagnostics and tests only.</summary>
    internal int Count => _entries.Count;

    /// <summary>
    /// Forgets every remembered read. Called by each in-request writer of a remembered answer (contract: a read after a
    /// write in the same request goes back to Dataverse). A read in flight when this runs is not served to anyone who asks
    /// after it.
    /// </summary>
    internal void Clear()
    {
        _entries.Clear();
        RecordTypes.Clear();
    }

    /// <summary>
    /// The answer to <paramref name="key"/>: a remembered one when this request already read it, otherwise
    /// <paramref name="read"/>'s. Concurrent callers of the same key share one read. The answer is remembered only when it
    /// completes and <paramref name="remember"/> accepts it; a thrown, cancelled or rejected answer is forgotten before it
    /// is returned, and a caller that waited on such a read asks again itself rather than inherit it.
    /// </summary>
    /// <param name="key">Every input of the read (reader, principal, ids, filter). Keys of different readers never meet:
    /// each starts with its reader's prefix.</param>
    /// <param name="read">The live read.</param>
    /// <param name="remember">Whether an answer is a proven read that may be shared. Return false for every "unknown"
    /// answer a fail-closed decision is built from.</param>
    /// <param name="ct">The caller's cancellation.</param>
    internal async Task<T> GetOrReadAsync<T>(
        string key, Func<CancellationToken, Task<T>> read, Func<T, bool> remember, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(remember);

        while (true)
        {
            var mine = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entry = _entries.GetOrAdd(key, mine.Task);

            if (ReferenceEquals(entry, mine.Task))
            {
                // This caller reads. Whatever happens, the entry is decided BEFORE the shared task completes, so a waiter
                // that retries never finds the faulted entry again.
                T value;
                try
                {
                    value = await read(ct).ConfigureAwait(false);
                }
                catch
                {
                    Forget(key, mine.Task);
                    // Waiters see a cancelled task (no unobserved exception) and ask again themselves.
                    mine.TrySetCanceled();
                    throw;
                }

                if (!remember(value))
                {
                    Forget(key, mine.Task);
                }

                mine.TrySetResult(value);
                return value;
            }

            if (entry is not Task<T> shared)
            {
                // Two readers used one key for different answer types: a caller bug. Never serve a mismatched answer.
                throw new InvalidOperationException($"Request access memo key '{key}' is used for more than one answer type.");
            }

            T answer;
            try
            {
                answer = await shared.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // The read this caller waited on faulted or was cancelled by its own caller: ask again.
                continue;
            }

            if (remember(answer))
            {
                return answer;
            }

            // An unknown answer is not shared, even with a caller that waited on it: ask again.
        }
    }

    /// <summary>
    /// #2845 round 2: the per-ID form of <see cref="GetOrReadAsync{T}"/> for a batch read (one query answers many ids). Each
    /// id's answer is remembered on its own under <paramref name="readerKey"/>, so a later batch that overlaps an earlier one
    /// reads only the ids not yet answered, and joins any id another caller is reading right now.
    /// </summary>
    /// <remarks>
    /// <para><b>A batch that throws (or is cancelled) is remembered for NO id</b>: every id it owned is forgotten, and a
    /// caller that was waiting on one of those ids reads it again itself. <b>A batch that answers</b> is remembered PER ID
    /// (#2845 F-R1): each id answered with a value <paramref name="remember"/> accepts is remembered; an id the read left
    /// out, or answered with a value <paramref name="remember"/> refuses (a chunk that failed closed, an unreadable row), is
    /// forgotten on its own, and a caller waiting on it reads only that id again. The caller that made the read is answered
    /// with exactly what the read returned (its own fail-closed values included), as it would be without the memo.</para>
    /// <para>No deadlock: a caller makes its own read before it waits on ids other callers own, so every owned read
    /// completes without waiting on anyone.</para>
    /// </remarks>
    /// <param name="readerKey">The reader and every input of the read other than the ids: the reader name and the table,
    /// plus the principal when the answer depends on who asks. Built by the <c>*Reader</c> key methods below.</param>
    /// <param name="ids">The ids to answer. Duplicates are asked once.</param>
    /// <param name="readMissing">The live batch read of the ids no one has answered or is reading.</param>
    /// <param name="remember">Whether one id's answer is a proven read that may be shared.</param>
    /// <param name="ct">The caller's cancellation.</param>
    /// <returns>Each asked id's answer that the reads returned (an id the live read left out is absent, as it would be).</returns>
    internal async Task<IReadOnlyDictionary<Guid, T>> GetOrReadManyAsync<T>(
        string readerKey,
        IReadOnlyCollection<Guid> ids,
        Func<IReadOnlyList<Guid>, CancellationToken, Task<IReadOnlyDictionary<Guid, T>>> readMissing,
        Func<T, bool> remember,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(readerKey);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(readMissing);
        ArgumentNullException.ThrowIfNull(remember);

        var answers = new Dictionary<Guid, T>();
        var pending = ids.Distinct().ToList();
        while (pending.Count > 0)
        {
            var owned = new List<(Guid Id, string Key, TaskCompletionSource<T> Answer)>();
            var joined = new List<(Guid Id, Task<T> Answer)>();
            foreach (var id in pending)
            {
                var key = PerIdKey(readerKey, id);
                var mine = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                var entry = _entries.GetOrAdd(key, mine.Task);
                if (ReferenceEquals(entry, mine.Task))
                {
                    owned.Add((id, key, mine));
                }
                else if (entry is Task<T> shared)
                {
                    joined.Add((id, shared));
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Request access memo key '{key}' is used for more than one answer type.");
                }
            }

            if (owned.Count > 0)
            {
                IReadOnlyDictionary<Guid, T> read;
                try
                {
                    read = await readMissing(owned.Select(o => o.Id).ToList(), ct).ConfigureAwait(false);
                }
                catch
                {
                    foreach (var (_, key, answer) in owned)
                    {
                        Forget(key, answer.Task);
                        answer.TrySetCanceled();
                    }

                    throw;
                }

                // #2845 F-R1: the batch did not throw, so each id stands on its own answer: an id answered with a provable
                // value is remembered; an id left out, or answered with a value `remember` refuses, is forgotten (and only
                // that id is read again by whoever asks next).
                foreach (var (id, key, answer) in owned)
                {
                    if (read is not null && read.TryGetValue(id, out var value))
                    {
                        answers[id] = value;
                        if (remember(value))
                        {
                            answer.TrySetResult(value);
                            continue;
                        }
                    }

                    // Decided BEFORE the shared task completes, so a waiter that retries never finds this entry again.
                    Forget(key, answer.Task);
                    answer.TrySetCanceled();
                }
            }

            var again = new List<Guid>();
            foreach (var (id, answer) in joined)
            {
                try
                {
                    answers[id] = await answer.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // The batch this id was in faulted, was partial, or was cancelled by its own caller: ask again.
                    again.Add(id);
                }
            }

            pending = again;
        }

        return answers;
    }

    /// <summary>
    /// #2845 round 2: the request's cache of <c>sprk_recordtype_ref</c> logical names for the filing walk
    /// (<c>SecureRootInheritance</c>'s pair-type read), shared by the compositions of one request. The walk stores only a
    /// completed read (a fault throws before it stores), exactly as its per-call cache did. Emptied by <see cref="Clear"/>.
    /// </summary>
    internal ConcurrentDictionary<Guid, string?> RecordTypes { get; } = new();

    private static string PerIdKey(string readerKey, Guid id) => $"{readerKey}#{id:N}";

    private void Forget(string key, object entry)
        => _entries.TryRemove(new KeyValuePair<string, object>(key, entry));

    // ── Keys: one prefix per reader ─────────────────────────────────────────────────────────────────

    /// <summary>App-only read of root records' own Secure / Access Permission / state columns
    /// (<c>ExternalParticipationService.GetRootRecordFlagsAsync</c>). Not principal-dependent.</summary>
    internal static string RootFlagsReader(string entityType) => $"root-flags:{entityType}";

    /// <summary>App-only read of root records' org-typed lookups (<c>GetReferencedOrganizationIdsAsync</c>). Not
    /// principal-dependent.</summary>
    internal static string ReferencedOrganizationsReader(string entityType) => $"referenced-orgs:{entityType}";

    /// <summary>App-only read of a row's filing (the filing walk's facts read). Not principal-dependent.</summary>
    internal static string FilingFactsReader(string table) => $"filing-facts:{table}";

    /// <summary>App-only read of a matter's or project's flag, name and Access Permission (the filing walk's parent read).
    /// Not principal-dependent.</summary>
    internal static string FilingParentReader(string table) => $"filing-parent:{table}";

    /// <summary>The systemuser-plane veto's subjects for a secure record (<c>SecureShareNoAccessGuard.ResolveSubjectsAsync</c>):
    /// principal-dependent, so every input is in the key — the systemuser, its derived contact and that contact's wall
    /// organizations.</summary>
    internal static string SecureVetoSubjectsKey(Guid systemUserId, Guid? linkedContactId, IEnumerable<Guid>? linkedContactOrganizations)
        => $"secure-veto-subjects:{systemUserId:N}:{linkedContactId:N}:" +
           string.Join(",", (linkedContactOrganizations ?? Array.Empty<Guid>()).Distinct().OrderBy(id => id).Select(id => id.ToString("N")));

    internal static string ContactStateKey(Guid contactId) => $"contact-state:{contactId:N}";

    internal static string RemovedMarkerKey(Guid contactId) => $"removed-marker:{contactId:N}";

    internal static string OrganizationMembershipsKey(Guid contactId) => $"org-memberships:{contactId:N}";

    internal static string GrantSetKey(Guid contactId) => $"grant-set:{contactId:N}";

    /// <summary>One deny-list query: the subject filter and the object filter are the whole query (the reader adds only
    /// <c>statecode eq 0</c>), so together they are every input.</summary>
    internal static string NoAccessChunkKey(string subjectFilter, string objectFilter)
        => $"no-access-chunk:{subjectFilter}\n{objectFilter}";
}
