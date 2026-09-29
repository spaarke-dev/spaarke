namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// What a retention probe learned about a session document. Deliberately THREE states, not a
/// nullable <see cref="StoredSession"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why "Indeterminate" has to exist.</b> <see cref="SessionPersistenceService"/>'s ordinary read
/// path (<c>LoadFromCosmosAsync</c>) catches EVERY exception and returns <c>null</c> — a throttled,
/// timed-out or misconfigured Cosmos read is indistinguishable from "this session expired". That is
/// harmless for a restore (the user sees "session not found") and catastrophic for retention: a
/// Cosmos outage would present as "every session has expired", and a sweep built on it would delete
/// every durable byte in the account. So retention MUST NOT consume a nullable-returning read.
/// <see cref="ISessionPersistenceService.ProbeSessionRetentionAsync"/> exists to return
/// <see cref="Indeterminate"/> where that read would have returned null, and
/// <see cref="SessionFileRetentionPolicy"/> treats it as RETAIN.
/// </para>
/// </remarks>
public enum SessionRetentionState
{
    /// <summary>The session document exists. Its own retention governs; the durable bytes ride along.</summary>
    Present,

    /// <summary>
    /// The session document is DEFINITIVELY gone — Cosmos answered 404 on a point read of
    /// <c>(id = sessionId, partition = tenantId)</c>. Either its TTL elapsed or it was erased.
    /// This is the only state that can lead to a delete.
    /// </summary>
    Absent,

    /// <summary>
    /// The question could not be answered (read failure, Cosmos not configured, a non-404 error).
    /// NEVER treated as expiry — absence of evidence is not evidence of absence.
    /// </summary>
    Indeterminate
}

/// <summary>
/// The answer to "does this session still exist, and under what retention?" — the input
/// <see cref="SessionFileRetentionPolicy"/> needs, and the ONLY place <see cref="StoredSession.Ttl"/>
/// crosses into the retention path.
/// </summary>
/// <param name="State">Present / Absent / Indeterminate — see <see cref="SessionRetentionState"/>.</param>
/// <param name="Ttl">
/// The document's per-item Cosmos <c>ttl</c> when <paramref name="State"/> is
/// <see cref="SessionRetentionState.Present"/>; otherwise <c>null</c>.
/// <see cref="StoredSession.NeverExpireTtl"/> (<c>-1</c>) means INDEFINITE, never "already expired".
/// </param>
public sealed record SessionRetentionProbe(SessionRetentionState State, int? Ttl)
{
    /// <summary>The session document was definitively not found.</summary>
    public static readonly SessionRetentionProbe Absent = new(SessionRetentionState.Absent, null);

    /// <summary>The question could not be answered. Callers MUST retain.</summary>
    public static readonly SessionRetentionProbe Indeterminate = new(SessionRetentionState.Indeterminate, null);

    /// <summary>The session document exists, carrying <paramref name="ttl"/> (null = rides the container default).</summary>
    public static SessionRetentionProbe Found(int? ttl) => new(SessionRetentionState.Present, ttl);
}

/// <summary>
/// The retention verdict for ONE durable session-file blob. Only <see cref="Expired"/> permits a
/// delete; every other value is a retain, and they are distinct so the sweep's telemetry says WHY.
/// </summary>
public enum SessionFileRetentionVerdict
{
    /// <summary>
    /// The session is FILED (<see cref="StoredSession.Ttl"/> = <see cref="StoredSession.NeverExpireTtl"/>).
    /// Its Cosmos document never expires, so its durable bytes never expire either. This verdict is the
    /// whole point of FR-B04.
    /// </summary>
    RetainIndefinitely,

    /// <summary>The session document exists and rides its own (container-default) retention. Retain.</summary>
    RetainWhileSessionLives,

    /// <summary>
    /// The session document is gone, but the blob is younger than the retention window — so it may be a
    /// blob whose manifest write has not landed yet (the durable write precedes it, and the manifest
    /// write is non-fatal by design). Retain until it ages past the window.
    /// </summary>
    RetainWithinRetentionWindow,

    /// <summary>The probe could not answer, or the blob has no creation timestamp to age. Retain.</summary>
    RetainIndeterminate,

    /// <summary>
    /// The session document is definitively gone AND the blob is older than the retention window.
    /// The ONLY deletable verdict.
    /// </summary>
    Expired
}

/// <summary>
/// spaarkeai-compose-r8 FR-B04 (task 062) — decides whether one durable session-file blob is still
/// covered by its SESSION's retention.
/// </summary>
/// <remarks>
/// <para>
/// <b>The fact this policy is built around.</b> The Cosmos <c>sessions</c> container carries
/// <c>DefaultTimeToLive = 7776000</c> (90 days) with a per-item override
/// (<see cref="StoredSession.Ttl"/>): <c>null</c> rides the 90-day default, <c>-1</c>
/// (<see cref="StoredSession.NeverExpireTtl"/>) is INDEFINITE and is set when a session is FILED to an
/// Analysis. <b>Blobs have no TTL of their own.</b> So the manifest can expire while the bytes persist,
/// and any retention rule keyed off the manifest ALONE orphans those bytes permanently — a
/// manifest-driven sweep cannot even see them, because the manifest is the thing that disappeared.
/// This policy is therefore driven from the BLOB side, and asks Cosmos about each session it finds.
/// </para>
/// <para>
/// <b>Why the session document's EXISTENCE is the retention signal.</b> Cosmos already implements this
/// project's retention rules — the 90-day default, the sliding refresh on every write, and the <c>-1</c>
/// indefinite override. Re-deriving expiry from timestamps in application code would mean maintaining a
/// second, divergent implementation of a rule the database already enforces. So the rule is:
/// <i>session document present ⇒ retain; definitively absent ⇒ the session's retention has ended.</i>
/// FR-B04's "90-day default for unfiled, indefinite for filed" falls out of that with no arithmetic.
/// </para>
/// <para>
/// <b>The sentinel, handled explicitly and FIRST.</b> <see cref="Evaluate"/> checks
/// <see cref="IsIndefiniteTtl"/> before ANY age comparison, so a filed session cannot reach an
/// arithmetic path at all. That is belt-and-braces on top of the existence rule (a filed document is
/// Present anyway), and it exists precisely because <c>-1</c> is the value a naive
/// <c>ttl &lt; elapsedSeconds</c> comparison reads as "expired 90 days ago". Deleting the files of
/// FILED matters is the silent, delayed, irreversible failure this task exists to prevent.
/// </para>
/// <para>
/// <b>Fail-closed by construction.</b> Only <see cref="SessionRetentionState.Absent"/> — a real Cosmos
/// 404 on a point read — combined with a blob older than <see cref="DefaultRetentionWindow"/> can
/// produce <see cref="SessionFileRetentionVerdict.Expired"/>. Every unknown (probe failure, missing
/// blob timestamp) retains. The asymmetry is deliberate: retaining bytes too long costs storage;
/// deleting them early is unrecoverable.
/// </para>
/// <para>
/// <b>Not a storage lifecycle policy.</b> An account- or container-level Azure Blob lifecycle rule
/// cannot express "indefinite for filed" — it can only age blobs by creation time, which would delete
/// filed matters' files on day 91. That is why this task adds no lifecycle delete rule anywhere
/// (<c>storage-account.bicep</c>'s only <c>ai-chunks</c> rule remains tier-to-Cool, and is gated off in
/// customer deployments).
/// </para>
/// <para>
/// Pure and static — no state, no I/O, no DI registration. The I/O lives in
/// <see cref="SessionFileRetentionJob"/>.
/// </para>
/// </remarks>
public static class SessionFileRetentionPolicy
{
    /// <summary>
    /// The <c>sessions</c> container's provisioned <c>DefaultTimeToLive</c>, in seconds (90 days).
    /// Mirrored here so the retention window and the session default cannot drift apart silently.
    /// </summary>
    public const int SessionsContainerDefaultTtlSeconds = 7776000;

    /// <summary>
    /// How long a durable blob whose session document is ABSENT is kept before it is treated as expired.
    /// </summary>
    /// <remarks>
    /// Deliberately a CONSTANT, not a configuration key. Its only correct value is the <c>sessions</c>
    /// container's own default TTL; a configurable copy would be a second source of truth for a number
    /// that is provisioned in Cosmos, and the failure mode of drift is silent early deletion. It serves
    /// two purposes at once: it bounds how long a genuinely orphaned blob survives (the durable write
    /// lands before the manifest write, and the manifest write is non-fatal), and it guarantees a blob
    /// written moments ago is never deleted merely because its session document has not been written yet.
    /// </remarks>
    public static readonly TimeSpan DefaultRetentionWindow = TimeSpan.FromSeconds(SessionsContainerDefaultTtlSeconds);

    /// <summary>
    /// True when <paramref name="ttl"/> means "never expire".
    /// </summary>
    /// <remarks>
    /// Cosmos defines exactly one such value: <see cref="StoredSession.NeverExpireTtl"/> (<c>-1</c>).
    /// This predicate deliberately accepts the whole non-positive range as a SUPERSET of it. A zero or
    /// negative TTL is never a legitimate short expiry (Cosmos rejects <c>0</c> outright), so widening
    /// the sentinel can only ever cause bytes to be KEPT — whereas narrowing it, or comparing the value
    /// numerically, deletes the files of filed matters. Given that asymmetry, the safe direction is the
    /// only defensible one.
    /// </remarks>
    public static bool IsIndefiniteTtl(int? ttl) => ttl.HasValue && ttl.Value <= 0;

    /// <summary>
    /// Decides the fate of one durable session-file blob.
    /// </summary>
    /// <param name="probe">What Cosmos said about the owning session. See <see cref="SessionRetentionProbe"/>.</param>
    /// <param name="blobCreatedOn">
    /// The blob's creation timestamp, or <c>null</c> when the listing did not carry one (which retains).
    /// </param>
    /// <param name="now">Current time (injected so a pass is testable without wall-clock waits).</param>
    /// <param name="retentionWindow">
    /// Override for <see cref="DefaultRetentionWindow"/>. Tests use it; production does not.
    /// </param>
    public static SessionFileRetentionVerdict Evaluate(
        SessionRetentionProbe probe,
        DateTimeOffset? blobCreatedOn,
        DateTimeOffset now,
        TimeSpan? retentionWindow = null)
    {
        ArgumentNullException.ThrowIfNull(probe);

        // ── The sentinel, before any arithmetic can touch it. ──────────────────────────────────
        // A FILED session carries Ttl == -1. Checked here, first, and unconditionally: no code path
        // below can reach an age comparison for a filed session, however the rest of this method is
        // later edited.
        if (IsIndefiniteTtl(probe.Ttl))
        {
            return SessionFileRetentionVerdict.RetainIndefinitely;
        }

        switch (probe.State)
        {
            // The question could not be answered — retain. Never infer expiry from a failed read.
            case SessionRetentionState.Indeterminate:
                return SessionFileRetentionVerdict.RetainIndeterminate;

            // The session lives; Cosmos owns when it stops living. When it does, the document
            // disappears and the next pass sees Absent.
            case SessionRetentionState.Present:
                return SessionFileRetentionVerdict.RetainWhileSessionLives;

            case SessionRetentionState.Absent:
                // No creation timestamp means the blob cannot be aged, so it cannot be SHOWN to be past
                // the window. Retain, and let the next listing (or an operator) resolve it.
                if (blobCreatedOn is null)
                {
                    return SessionFileRetentionVerdict.RetainIndeterminate;
                }

                var age = now - blobCreatedOn.Value;
                var window = retentionWindow ?? DefaultRetentionWindow;

                return age >= window
                    ? SessionFileRetentionVerdict.Expired
                    : SessionFileRetentionVerdict.RetainWithinRetentionWindow;

            default:
                // Unreachable today. A future enum member must not silently become deletable.
                return SessionFileRetentionVerdict.RetainIndeterminate;
        }
    }
}
