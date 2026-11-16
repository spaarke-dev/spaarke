using System.Collections.Concurrent;

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

/// <summary>
/// The per-request memo of the access evaluator's subject-level reads (#2845). Thread-safe: the three per-type compositions
/// of one request share it concurrently. A read is shared only once it is known to be an answer; faults are never shared.
/// </summary>
internal sealed class RequestAccessMemo
{
    /// <summary>The <c>HttpContext.Items</c> key the request's memo is kept under.</summary>
    internal const string HttpContextItemsKey = "uac:request-access-memo";

    private readonly ConcurrentDictionary<string, object> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// The memo of the request <paramref name="context"/> belongs to, created on first use; <c>null</c> when there is no
    /// request (no memo: every read is live).
    /// </summary>
    internal static RequestAccessMemo? For(HttpContext? context)
    {
        if (context is null)
        {
            return null;
        }

        // Items is a plain dictionary. Items access from the concurrent compositions is safe because the only Items writers
        // reachable while they run (RequestAccessMemo.For, CallerWhoAmIMemo) lock context.Items; other filters write Items
        // outside the compositions. Any new Items writer reachable from a composition must lock context.Items.
        lock (context.Items)
        {
            if (context.Items.TryGetValue(HttpContextItemsKey, out var existing) && existing is RequestAccessMemo memo)
            {
                return memo;
            }

            var created = new RequestAccessMemo();
            context.Items[HttpContextItemsKey] = created;
            return created;
        }
    }

    /// <summary>The number of remembered (or in-flight) reads. Diagnostics and tests only.</summary>
    internal int Count => _entries.Count;

    /// <summary>
    /// Forgets every remembered read. Called by each in-request writer of a remembered answer (contract: a read after a
    /// write in the same request goes back to Dataverse). A read in flight when this runs is not served to anyone who asks
    /// after it.
    /// </summary>
    internal void Clear() => _entries.Clear();

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

    private void Forget(string key, object entry)
        => _entries.TryRemove(new KeyValuePair<string, object>(key, entry));

    // ── Keys: one prefix per reader ─────────────────────────────────────────────────────────────────

    internal static string ContactStateKey(Guid contactId) => $"contact-state:{contactId:N}";

    internal static string RemovedMarkerKey(Guid contactId) => $"removed-marker:{contactId:N}";

    internal static string OrganizationMembershipsKey(Guid contactId) => $"org-memberships:{contactId:N}";

    internal static string GrantSetKey(Guid contactId) => $"grant-set:{contactId:N}";

    /// <summary>One deny-list query: the subject filter and the object filter are the whole query (the reader adds only
    /// <c>statecode eq 0</c>), so together they are every input.</summary>
    internal static string NoAccessChunkKey(string subjectFilter, string objectFilter)
        => $"no-access-chunk:{subjectFilter}\n{objectFilter}";
}
