using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// The outcome of erasing one session's durable file bytes. Deliberately a tri-state, not a bool.
/// </summary>
/// <remarks>
/// An erasure that silently skipped bytes is an ADR-015 compliance failure that looks exactly like a
/// completed one — nothing in the product surfaces the gap, because the manifest, the UI and the hot
/// index are all gone by then. So "we called delete and nothing threw" is not allowed to be the
/// success condition: the states below distinguish CONFIRMED gone from UNANSWERABLE from NOT gone.
/// This is the mirror of task 062's <see cref="SessionRetentionState"/> tri-state, which exists
/// because a Cosmos outage must not read as "every session expired". Here the hazard runs the other
/// way: a transient failure must not read as "every byte erased".
/// </remarks>
public enum SessionFileErasureState
{
    /// <summary>
    /// Every durable byte under the session's prefix is gone, VERIFIED by re-enumerating the prefix
    /// after the deletes. Zero blobs to begin with is also <see cref="Erased"/> — the prefix is
    /// observably empty, which is the property that matters.
    /// </summary>
    Erased,

    /// <summary>
    /// This deployment has no durable store configured, so nothing could be enumerated and nothing
    /// could be deleted. NOT a claim that no bytes exist — see the disarm caveat on
    /// <see cref="SessionFileEraser"/>. Callers proceed; they must not report this as
    /// "durable bytes erased".
    /// </summary>
    StoreDisabled,

    /// <summary>
    /// Enumeration, a delete, or the verification pass failed. Bytes MAY remain. The caller MUST NOT
    /// report success. Re-issuing the erasure is safe and completes it (the prefix enumeration does
    /// not depend on any manifest, so a half-erased session is fully reachable on the retry).
    /// </summary>
    Incomplete
}

/// <summary>
/// Counts and a low-cardinality reason for one session-scoped durable erasure.
/// </summary>
/// <param name="State">The contract. Everything else is diagnostics.</param>
/// <param name="BlobsDeleted">Blobs this pass actually removed.</param>
/// <param name="BlobsAlreadyAbsent">Blobs listed but already gone when deleted — a concurrent erasure or retention pass. Not a failure.</param>
/// <param name="BlobsRemaining">Blobs the verification re-enumeration still found. Non-zero forces <see cref="SessionFileErasureState.Incomplete"/>.</param>
/// <param name="Failures">Delete, enumeration or verification operations that threw.</param>
/// <param name="Reason">One of the <c>Reason*</c> constants on <see cref="SessionFileEraser"/> — safe to log and to surface in an error body.</param>
/// <param name="FileIds">The file ids observed under the prefix. Used to evict the same bytes' 4-hour hot copies; empty when the store is disabled.</param>
public sealed record SessionFileErasureResult(
    SessionFileErasureState State,
    int BlobsDeleted,
    int BlobsAlreadyAbsent,
    int BlobsRemaining,
    int Failures,
    string Reason,
    IReadOnlyList<string> FileIds)
{
    /// <summary>True only when the durable bytes were observed gone. <c>false</c> for both other states.</summary>
    public bool BytesConfirmedGone => State == SessionFileErasureState.Erased;
}

/// <summary>
/// FR-B06 (spaarkeai-compose-r8 Track B, task 063) — removes every copy of a chat session's uploaded
/// file bytes when the session is deleted or a GDPR Art. 17 erasure is requested.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mirrors the <c>memory-items</c> erasability pattern, does not invent a second one.</b>
/// <see cref="Memory.MemoryItemStore.EraseSubjectAsync"/> is the precedent: enumerate the SUBJECT
/// PARTITION, delete every row in it, swallow the already-gone case so a repeat is a no-op, log
/// identifiers and a count. This is the same shape with the same three properties, over the store
/// whose partition happens to be a blob-name prefix rather than a Cosmos partition key. Nothing new
/// is introduced at the store: the two primitives (<see cref="SessionFileBlobStore.ListAsync"/> and
/// <see cref="SessionFileBlobStore.DeleteAsync"/>) were shaped for this by task 062, and this type
/// composes them — it adds no store surface.
/// </para>
/// <para>
/// <b>Enumeration is by PREFIX, never by walking the manifest — and that is load-bearing.</b> The
/// Cosmos <c>sessions</c> container carries <c>DefaultTimeToLive = 7776000</c> (90 days) while the
/// blobs carry no TTL at all, so the manifest can expire while the bytes persist. A manifest-driven
/// erasure would then name nothing, leave those bytes behind permanently, and make them invisible to
/// every FUTURE erasure as well. The same hole exists at a much shorter timescale: the durable write
/// lands BEFORE the (deliberately non-fatal) manifest write, so an upload whose manifest update failed
/// produces a blob no manifest mentions. Prefix enumeration reaches both; a manifest walk reaches
/// neither. (Task 060 notes, open item 6.)
/// </para>
/// <para>
/// <b>Tenant isolation.</b> The prefix always begins with the CALLING tenant
/// (<c>{tenantId}/session-files/{sessionId}/</c>), every row is re-checked against that tenant by the
/// store before it is yielded, and each delete is composed from the CALLER's tenant id rather than
/// from the listing row — so even a listing that somehow widened could not redirect a delete across
/// the boundary. Knowing another tenant's session id enumerates nothing and deletes nothing
/// (ADR-014 / ADR-015). Pinned by
/// <c>tests/integration/tenant/Ai/SessionFileBlobStoreTenantIsolationTests.cs</c>.
/// </para>
/// <para>
/// <b>Partial failure is reported as failure.</b> One delete that throws does not abandon the rest —
/// the pass continues so it erases as much as it can — but it fixes the outcome at
/// <see cref="SessionFileErasureState.Incomplete"/>, and a verification re-enumeration afterwards
/// turns "no delete threw" into "the prefix is observably empty". The caller
/// (<see cref="Chat.ChatSessionManager.DeleteSessionAsync"/>) then leaves the session RECORD intact,
/// so the failure is visible in the product and the natural retry converges.
/// </para>
/// <para>
/// <b>🔔 The one thing that is NOT synchronously erasable</b> (POML escalation trigger). While
/// <c>SessionFileStore:BlobEndpoint</c> is empty the store is inert, so this returns
/// <see cref="SessionFileErasureState.StoreDisabled"/> and no durable bytes are reached. For a
/// deployment that was never armed that is exactly correct — nothing was ever written either. For a
/// deployment that was armed and later DISARMED it is not: bytes written while it was enabled become
/// unreachable by erasure until it is re-armed. Disarming a live durable store is therefore an
/// operational decision with a compliance consequence, recorded in
/// <c>projects/spaarkeai-compose-r8/notes/track-b-erasure-surface.md</c>. Azure Blob soft-delete and
/// blob versioning are NOT enabled on the storage account, so a completed delete is immediate and
/// final — there is no retention window hiding a copy behind a successful erasure.
/// </para>
/// <para>
/// <b>Zero DI registrations.</b> A static composition, exactly like task 062's
/// <see cref="SessionFileRetentionPolicy"/>: its collaborators are already-registered singletons that
/// the one caller already holds. §11 gate — (1) <i>existing</i>: nothing erases durable session bytes
/// today; <see cref="SessionFileRetentionJob"/> deletes on AGE and only for definitively-absent
/// sessions, and <c>SessionFilesCleanupJob</c> is structurally forbidden from reaching this store
/// (task 061, enforced by <c>SessionFilesCleanupScopeTests</c>). (2) <i>extension</i>: this IS the
/// extension — it composes 062's list+delete rather than adding a bulk delete to the store, and it
/// hangs off the EXISTING <see cref="Chat.ChatSessionManager.DeleteSessionAsync"/> chokepoint rather
/// than adding an erasure endpoint. (3) <i>cost of doing nothing</i>: a user deletes a conversation,
/// the record disappears from History, and the original uploaded documents stay in blob storage
/// indefinitely — ADR-015's "MUST support user-initiated deletion in Tier 3" unmet, and the gate
/// holding <c>BlobEndpoint</c> empty could never be lifted.
/// </para>
/// </remarks>
public static class SessionFileEraser
{
    /// <summary>Reason: the prefix was enumerated, emptied, and verified empty.</summary>
    public const string ReasonComplete = "complete";

    /// <summary>Reason: no blob endpoint is configured for this deployment.</summary>
    public const string ReasonStoreDisabled = "store-disabled";

    /// <summary>Reason: the identifiers cannot form a blob name, so no durable copy can exist under them.</summary>
    public const string ReasonUnaddressableIdentifiers = "unaddressable-identifiers";

    /// <summary>Reason: the prefix could not be enumerated, so nothing is known about what remains.</summary>
    public const string ReasonEnumerationFailed = "enumeration-failed";

    /// <summary>Reason: at least one delete threw.</summary>
    public const string ReasonDeleteFailed = "delete-failed";

    /// <summary>Reason: the deletes reported success but the verification pass still found blobs.</summary>
    public const string ReasonResidualAfterDelete = "residual-after-delete";

    /// <summary>Reason: the verification re-enumeration itself failed, so emptiness is unproven.</summary>
    public const string ReasonVerificationFailed = "verification-failed";

    private static readonly IReadOnlyList<string> NoFileIds = Array.Empty<string>();

    /// <summary>
    /// Erases every durable byte copy belonging to one session of one tenant, then verifies the
    /// prefix is empty. Idempotent: a second call finds nothing and returns
    /// <see cref="SessionFileErasureState.Erased"/> with zero counts, and a call after a PARTIAL
    /// erasure finds and removes exactly the residue.
    /// </summary>
    /// <param name="durableStore">The durable store, or <c>null</c> when it is not registered.</param>
    /// <param name="tenantId">The CALLING tenant. Every enumeration and every delete is composed from this value.</param>
    /// <param name="sessionId">The session whose files are being erased.</param>
    /// <param name="logger">Logger. ADR-015: identifiers and counts only, never file names, never content.</param>
    /// <param name="ct">Cancellation token. Cancellation propagates — a cancelled erasure is not a completed one.</param>
    public static async Task<SessionFileErasureResult> EraseSessionFilesAsync(
        SessionFileBlobStore? durableStore,
        string tenantId,
        string sessionId,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (durableStore is null || !durableStore.IsEnabled)
        {
            logger.LogDebug(
                "Durable session-file erasure skipped for session {SessionId} (tenant={TenantId}) — " +
                "the durable store is not configured ('{Key}' is empty).",
                sessionId, tenantId, SessionFileBlobStore.BlobEndpointConfigKey);

            return new SessionFileErasureResult(
                SessionFileErasureState.StoreDisabled, 0, 0, 0, 0, ReasonStoreDisabled, NoFileIds);
        }

        // Enumerate the whole prefix BEFORE deleting anything, rather than deleting while iterating:
        // the set to erase is then a fact captured at one instant, and the verification pass below is
        // a genuinely independent second look rather than the tail of the same enumeration.
        List<SessionFileBlobRef> blobs;
        try
        {
            blobs = await ListPrefixAsync(durableStore, tenantId, sessionId, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            // The identifiers cannot form a blob name — so the write path could not have stored
            // anything under them either (it validates with the same rule and throws). There is
            // provably nothing to erase; say so explicitly rather than implying a pass happened.
            logger.LogWarning(ex,
                "Durable session-file erasure had nothing addressable to erase for session {SessionId} " +
                "(tenant={TenantId}): the identifiers are not valid blob-name segments, so no durable " +
                "copy can exist under them.",
                sessionId, tenantId);

            return new SessionFileErasureResult(
                SessionFileErasureState.Erased, 0, 0, 0, 0, ReasonUnaddressableIdentifiers, NoFileIds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Durable session-file erasure could not enumerate session {SessionId} (tenant={TenantId}). " +
                "Nothing is known about what remains, so this is reported as INCOMPLETE.",
                sessionId, tenantId);

            return new SessionFileErasureResult(
                SessionFileErasureState.Incomplete, 0, 0, 0, 1, ReasonEnumerationFailed, NoFileIds);
        }

        var deleted = 0;
        var alreadyAbsent = 0;
        var failures = 0;
        var fileIds = new List<string>(blobs.Count);

        foreach (var blob in blobs)
        {
            ct.ThrowIfCancellationRequested();
            fileIds.Add(blob.FileId);

            try
            {
                // tenantId is the CALLER's, not blob.TenantId: a listing bug must not be able to
                // redirect a delete across the tenant boundary. The store re-asserts partitioning on
                // the composed name before it issues the delete.
                if (await durableStore.DeleteAsync(tenantId, blob.SessionId, blob.FileId, ct).ConfigureAwait(false))
                {
                    deleted++;
                }
                else
                {
                    alreadyAbsent++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Keep going. One unreachable blob must not strand the other nineteen — erase as much
                // as possible, then report honestly that the pass did not complete.
                failures++;
                logger.LogError(ex,
                    "Durable session-file erasure failed to delete one copy. TenantId={TenantId}, " +
                    "SessionId={SessionId}, FileId={FileId}. The erasure is INCOMPLETE and must be retried.",
                    tenantId, blob.SessionId, blob.FileId);
            }
        }

        // Verification. Without it "erased" would mean "no delete threw", which is precisely the kind
        // of green that lets an incomplete erasure look finished.
        int remaining;
        try
        {
            remaining = (await ListPrefixAsync(durableStore, tenantId, sessionId, ct).ConfigureAwait(false)).Count;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Durable session-file erasure deleted {Deleted} copies for session {SessionId} " +
                "(tenant={TenantId}) but could not verify the prefix is empty. Reported as INCOMPLETE.",
                deleted, sessionId, tenantId);

            return new SessionFileErasureResult(
                SessionFileErasureState.Incomplete, deleted, alreadyAbsent, 0, failures + 1,
                ReasonVerificationFailed, fileIds);
        }

        var state = failures > 0 || remaining > 0
            ? SessionFileErasureState.Incomplete
            : SessionFileErasureState.Erased;

        var reason = state == SessionFileErasureState.Erased
            ? ReasonComplete
            : failures > 0 ? ReasonDeleteFailed : ReasonResidualAfterDelete;

        // ADR-015: identifiers and counts only. Logged at Information on the success path because a
        // deletion is an auditable event; at Error when bytes may remain, because that is a
        // compliance-bearing condition and not an operational curiosity.
        if (state == SessionFileErasureState.Erased)
        {
            logger.LogInformation(
                "Durable session-file erasure complete. TenantId={TenantId}, SessionId={SessionId}, " +
                "Deleted={Deleted}, AlreadyAbsent={AlreadyAbsent} (GDPR Art. 17).",
                tenantId, sessionId, deleted, alreadyAbsent);
        }
        else
        {
            logger.LogError(
                "Durable session-file erasure INCOMPLETE. TenantId={TenantId}, SessionId={SessionId}, " +
                "Deleted={Deleted}, AlreadyAbsent={AlreadyAbsent}, Remaining={Remaining}, " +
                "Failures={Failures}, Reason={Reason}. Durable bytes may still exist for this session.",
                tenantId, sessionId, deleted, alreadyAbsent, remaining, failures, reason);
        }

        return new SessionFileErasureResult(state, deleted, alreadyAbsent, remaining, failures, reason, fileIds);
    }

    /// <summary>
    /// Removes the 4-hour hot copies of the same bytes — the <c>doc-upload-*</c> Redis entries the
    /// upload endpoint writes alongside the durable copy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is here at all.</b> <c>doc-upload-binary</c> holds the ORIGINAL bytes and
    /// <c>doc-upload-text</c> the full extracted text. Session deletion removed the session's own
    /// Redis key and never these, so before this task a deleted session's file bytes survived in the
    /// cache for up to four hours. Bounded is not the same as erased.
    /// </para>
    /// <para>
    /// <b>Why it is BEST-EFFORT and does not change the erasure outcome.</b> These entries carry a
    /// 4-hour absolute TTL, so an unreachable Redis expires them on its own; failing a session delete
    /// (and stranding the durable erasure that already succeeded) because a cache eviction failed
    /// would trade a bounded residue for an unbounded one. Failures are logged, never swallowed
    /// silently.
    /// </para>
    /// <para>
    /// <b>File ids come from BOTH sources, deliberately.</b> The blob prefix names every file with a
    /// durable copy; the session manifest names every file uploaded while the store was disabled —
    /// which is every deployment until the operator arms it. Neither set is a superset of the other.
    /// </para>
    /// </remarks>
    /// <returns>The number of cache removals attempted and the number that threw.</returns>
    public static async Task<(int Attempted, int Failures)> EvictUploadCachesAsync(
        ITenantCache cache,
        string tenantId,
        string sessionId,
        IEnumerable<string> fileIds,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(fileIds);
        ArgumentNullException.ThrowIfNull(logger);

        var attempted = 0;
        var failures = 0;

        foreach (var fileId in fileIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            var cacheId = SessionUploadCacheKeys.CacheId(sessionId, fileId);

            foreach (var resource in SessionUploadCacheKeys.AllResources)
            {
                attempted++;

                try
                {
                    await cache.RemoveAsync(
                        tenantId, resource, cacheId, SessionUploadCacheKeys.Version, ct: ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures++;
                    logger.LogWarning(ex,
                        "Session-file cache eviction failed for one entry. TenantId={TenantId}, " +
                        "SessionId={SessionId}, FileId={FileId}, Resource={Resource}. The entry carries a " +
                        "4-hour absolute TTL, so it expires on its own; the durable erasure is unaffected.",
                        tenantId, sessionId, fileId, resource);
                }
            }
        }

        if (attempted > 0)
        {
            logger.LogInformation(
                "Session-file upload caches evicted. TenantId={TenantId}, SessionId={SessionId}, " +
                "Removals={Attempted}, Failures={Failures}.",
                tenantId, sessionId, attempted, failures);
        }

        return (attempted, failures);
    }

    private static async Task<List<SessionFileBlobRef>> ListPrefixAsync(
        SessionFileBlobStore durableStore,
        string tenantId,
        string sessionId,
        CancellationToken ct)
    {
        var blobs = new List<SessionFileBlobRef>();

        await foreach (var blob in durableStore.ListAsync(tenantId, sessionId, ct).ConfigureAwait(false))
        {
            blobs.Add(blob);
        }

        return blobs;
    }
}
