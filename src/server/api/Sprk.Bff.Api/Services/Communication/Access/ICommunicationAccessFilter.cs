using Microsoft.Xrm.Sdk;

namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// The BFF read-path enforcement filter for message/thread PRIVACY, INTERNAL-ONLY, and PRIVILEGE (FR-08 / NFR-06).
/// A DEFAULT-DENY composition control: a <c>sprk_communication</c> row is visible only if it passes EVERY dimension —
///
/// <list type="number">
///   <item><b>Internal-only</b> — an <c>sprk_isinternalonly</c> message is hidden from non-internal callers (D-05).</item>
///   <item>
///     <b>Privacy (membership ∪ grant, point-forward)</b> — an OPEN message needs ADR-034 open-thread membership on the
///     anchor record; a PRIVATE message needs an active overlay grant whose <c>EffectiveFrom</c> covers the message's
///     <c>createdon</c> (point-forward — prior private messages stay restricted).
///   </item>
///   <item>
///     <b>Privilege</b> — classification metadata composed onto the decision; it NEVER gates the read on its own and the
///     filter NEVER calls AI at read time (ADR-015 — the AI may FLAG upstream; a human owns the field; enforcement reads it).
///   </item>
/// </list>
///
/// Enforcement COMPOSES FROM existing Dataverse record security (ADR-034 membership + the existing per-record overlay-grant
/// pattern) — it is NOT a new authorization engine (design §5, root §11). The filter's visible set is always a SUBSET of
/// Dataverse-derived access; it agrees with task 041's ACS-membership projection (both derive from
/// <c>MembershipResolver(anchor) ∪ activeOverlayGrants(thread)</c>; neither may exceed it).
/// </summary>
public interface ICommunicationAccessFilter
{
    /// <summary>
    /// Resolves the per-(thread, caller) access basis ONCE (thread privacy snapshot + open membership + active grants),
    /// so a whole thread's messages can be decided without re-querying Dataverse per row. Fail-closed: if the thread
    /// cannot be loaded the basis denies every message. This is the shared derivation task 041 projects onto ACS.
    /// </summary>
    Task<ThreadAccessBasis> ResolveThreadAccessBasisAsync(
        CommunicationAccessContext caller,
        Guid threadId,
        CancellationToken ct = default);

    /// <summary>
    /// Decides a single message against a pre-resolved <see cref="ThreadAccessBasis"/>. Pure, synchronous logic wrapped
    /// in a task for symmetry — makes NO I/O and NO AI call. The unit of the security tests.
    /// </summary>
    CommunicationAccessDecision EvaluateMessage(
        CommunicationAccessContext caller,
        ThreadAccessBasis basis,
        Entity message);

    /// <summary>
    /// The endpoint entry point (task 050): resolves the basis, then filters a thread's message rows to the caller's
    /// visible subset (default-deny) and returns every per-row decision (reused for the access-filtered unread count).
    /// </summary>
    /// <param name="messages">
    /// <c>sprk_communication</c> rows for the thread, carrying at least <c>sprk_isprivate</c>, <c>sprk_isinternalonly</c>,
    /// <c>sprk_privilegeclassification</c>, and <c>createdon</c>.
    /// </param>
    Task<CommunicationAccessResult> FilterThreadMessagesAsync(
        CommunicationAccessContext caller,
        Guid threadId,
        IReadOnlyCollection<Entity> messages,
        CancellationToken ct = default);
}
