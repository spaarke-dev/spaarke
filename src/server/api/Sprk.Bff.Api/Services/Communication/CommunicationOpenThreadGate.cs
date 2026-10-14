using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Communication.Membership;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The read-side half of owner decision D7 (external-access r3 task 065 fix round 5, 2026-10-14): a partner only ever sees a
/// communication that sits in a thread that is EXPLICITLY Open at read time. This is applied on top of the
/// <c>sprk_isinternalonly</c> flag rule (<c>CommunicationAccessFilter</c>), never instead of it, and only for a caller who is
/// not an internal systemuser (a contact, or a systemuser flagged <c>sprk_isexternal</c>). Internal callers never reach it.
/// </summary>
/// <remarks>
/// <para><b>Why a read-time rule.</b> The write-side floor (<see cref="CommunicationInternalOnlyFloor"/>) only decides a
/// row's flag when the row is created. A thread switched to Private afterwards leaves its earlier rows flagged shareable, so
/// without this gate a partner would keep seeing them (ISS-009, #1859).</para>
/// <para><b>Hidden cases.</b> No thread on the row, a thread row that cannot be found or read, a missing or unreadable
/// privacy value, a Private thread. The "explicitly Open" test is <see cref="CommunicationInternalOnlyFloor.IsConfirmedOpen"/>
/// and the column is <see cref="CommunicationInternalOnlyFloor.ThreadPrivacyField"/>; neither is redefined here.</para>
/// <para><b>Cost.</b> One batched read of the DISTINCT thread ids of a page (chunks of <see cref="ChunkSize"/> in one OR
/// filter), never one read per row. A read fault propagates (the caller's read fails; nothing is returned), so a fault can
/// never turn into "show it".</para>
/// <para><b>Component Justification (CLAUDE.md §11):</b> (1) Existing — the floor's <c>IsConfirmedOpen</c> and the thread
/// privacy column, reused as they are; no read-side thread-privacy helper existed. (2) Extension — the floor is the
/// write-side rule set and takes an <c>IGenericEntityService</c>, not the read services' two query seams, so a read helper
/// over the seams the readers already hold sits beside it. (3) Cost of doing nothing — a thread made Private after a send
/// keeps its earlier messages visible to partners (ISS-009), which D7 forbids.</para>
/// </remarks>
internal static class CommunicationOpenThreadGate
{
    /// <summary>The thread entity set the batched privacy read targets.</summary>
    internal const string ThreadSet = "sprk_communicationthreads";

    /// <summary>The thread primary key.</summary>
    internal const string ThreadPkField = "sprk_communicationthreadid";

    /// <summary>The message → thread lookup, as it reads in an OData row.</summary>
    internal const string MessageThreadLookupValue = "_sprk_communicationthread_value";

    /// <summary>Thread ids per query: one OR filter over this many ids keeps the GET URL well inside the limit.</summary>
    internal const int ChunkSize = 50;

    /// <summary>
    /// The thread <c>$filter</c> clause an EXTERNAL caller's thread LIST query carries (fix round 6, F1): only threads whose
    /// privacy reads EXPLICITLY Open. It rides in the query itself, so paging and the next-page token are computed over
    /// threads the caller may see and a Private thread's name never leaves Dataverse. Built from the floor's column and the
    /// <see cref="ThreadPrivacyState"/> Open value; neither is redefined.
    /// </summary>
    internal static string OpenOnlyThreadFilterClause =>
        $"{CommunicationInternalOnlyFloor.ThreadPrivacyField} eq {(int)ThreadPrivacyState.Open}";

    /// <summary>Runs an OData query on <see cref="ThreadSet"/> under the caller's identity (app-only or impersonated).</summary>
    internal delegate Task<IReadOnlyList<Dictionary<string, JsonElement>>> ThreadRowQuery(string odata, CancellationToken ct);

    /// <summary>The columns every privacy read selects (pinned by a test: a dropped column would hide every row).</summary>
    internal static string SelectClause => $"{ThreadPkField},{CommunicationInternalOnlyFloor.ThreadPrivacyField}";

    /// <summary>The OData query for one chunk of thread ids.</summary>
    internal static string BuildQuery(IReadOnlyCollection<Guid> threadIds) =>
        $"$select={SelectClause}&$filter={string.Join(" or ", threadIds.Select(id => $"{ThreadPkField} eq {id}"))}&$top={threadIds.Count}";

    /// <summary>
    /// The subset of <paramref name="threadIds"/> whose thread reads EXPLICITLY Open. <see cref="Guid.Empty"/> and ids the
    /// query did not return are not in the set (hidden). An empty input makes no query.
    /// </summary>
    internal static async Task<HashSet<Guid>> ReadOpenThreadIdsAsync(
        IEnumerable<Guid> threadIds, ThreadRowQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(threadIds);
        ArgumentNullException.ThrowIfNull(query);

        var distinct = threadIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var open = new HashSet<Guid>();
        foreach (var chunk in distinct.Chunk(ChunkSize))
        {
            var rows = await query(BuildQuery(chunk), ct).ConfigureAwait(false);
            foreach (var row in rows)
            {
                if (TryGuid(row, ThreadPkField) is not { } id || !chunk.Contains(id))
                    continue;

                // The floor's own "explicitly Open" test, over a minimal entity carrying only the privacy value.
                var thread = new Entity(CommunicationInternalOnlyFloor.ThreadEntity, id);
                if (row.TryGetValue(CommunicationInternalOnlyFloor.ThreadPrivacyField, out var value)
                    && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var state))
                {
                    thread[CommunicationInternalOnlyFloor.ThreadPrivacyField] = new OptionSetValue(state);
                }

                if (CommunicationInternalOnlyFloor.IsConfirmedOpen(thread))
                    open.Add(id);
            }
        }

        return open;
    }

    private static Guid? TryGuid(Dictionary<string, JsonElement> row, string key) =>
        row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) ? g : null;
}
