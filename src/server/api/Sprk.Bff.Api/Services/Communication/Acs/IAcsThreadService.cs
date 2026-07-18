using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Acs;

/// <summary>
/// The ACS thread + membership plane (server-side only, FR-15 / NFR-04). The BFF is the ACS "trusted
/// service": it creates chat threads (with a 30-day auto-delete retention set at create time) and mutates
/// participant membership on ACS's behalf. Clients hold NO ACS admin capability and receive NO token in R1
/// (NFR-04) — this surface is never exposed to a client.
/// </summary>
/// <remarks>
/// <para>This is a genuine seam (ADR-010): consumed by the messaging channel sender (020), the
/// membership-reconcile job (041), and the outbound-send path (051), and mocked in unit tests. It sits
/// behind the Communication service boundary and keeps every <c>Azure.Communication.*</c> type inside
/// <c>Services/Communication/</c> (ADR-045) — no ACS type leaks to the API layer.</para>
/// <para><b>Scope (task 011):</b> ACS-side thread + participant <i>mechanics</i> ONLY. It does NOT decide
/// <i>who</i> should be a member (041 derives the authorized set from Dataverse via ADR-034), does NOT
/// persist any <c>sprk_communication</c> row (020/051), and does NOT capture inbound events (030/031).</para>
/// <para><b>Identities:</b> participants are addressed by ACS <c>communicationUserId</c> (MRI), minted by
/// task 010's <see cref="IAcsIdentityService.EnsureIdentityAsync"/>. Callers MUST resolve a
/// <c>ParticipantReference</c> to its <c>communicationUserId</c> via the identity plane BEFORE calling
/// these methods — a raw Entra id is rejected (never passed to ACS).</para>
/// <para><b>Rate limits:</b> ACS allows 10 participant-mutations/10s + 30/min per thread. The batch-add
/// shape lets the 041 reconcile chunk + throttle; a 429 surfaces as a retryable
/// <see cref="AcsRateLimitException"/>.</para>
/// </remarks>
public interface IAcsThreadService
{
    /// <summary>
    /// Creates an ACS chat thread with a 30-day auto-delete retention policy set at create time (FR-15;
    /// design §8.7) and returns its <c>ChatThreadId</c>. The initial participants are added atomically as
    /// part of the create call.
    /// </summary>
    /// <param name="topic">Human-readable thread topic (e.g. the matter/thread title).</param>
    /// <param name="initialParticipantCommunicationUserIds">
    /// ACS <c>communicationUserId</c> MRIs of the initial members (already resolved via task 010's
    /// identity plane). May be empty.
    /// </param>
    /// <param name="retention">
    /// Auto-delete window. Defaults to 30 days when null; clamped to the ACS-supported 30–90 day range.
    /// </param>
    Task<AcsThreadCreateResult> CreateThreadAsync(
        string topic,
        IEnumerable<string> initialParticipantCommunicationUserIds,
        TimeSpan? retention = null,
        CancellationToken ct = default);

    /// <summary>
    /// Adds participants to an existing thread by ACS <c>communicationUserId</c>. Idempotent — participants
    /// already in the thread are safe no-ops. Batch-friendly: the set is chunked into calls of at most
    /// <see cref="AcsThreadService.MaxParticipantsPerBatch"/> so task 041 can throttle at the batch
    /// boundary to stay within the per-thread mutation budget. A 429 surfaces as
    /// <see cref="AcsRateLimitException"/> (retryable).
    /// </summary>
    Task<AcsMembershipChange> AddParticipantsAsync(
        string chatThreadId,
        IEnumerable<string> communicationUserIds,
        CancellationToken ct = default);

    /// <summary>
    /// Removes a participant from a thread by ACS <c>communicationUserId</c>. Idempotent — removing a
    /// participant already absent is a safe no-op. A 429 surfaces as <see cref="AcsRateLimitException"/>
    /// (retryable).
    /// </summary>
    Task<AcsMembershipChange> RemoveParticipantAsync(
        string chatThreadId,
        string communicationUserId,
        CancellationToken ct = default);

    // ── task 041: list current participants (the reconcile's remove-leg needs the current ACS set) ──

    /// <summary>
    /// Lists the ACS <c>communicationUserId</c> MRIs currently in the thread. Added for the
    /// membership-reconcile job (task 041): the reconcile computes <c>desired</c> from Dataverse-derived
    /// access and must know the <c>current</c> ACS set to compute the remove-leg
    /// (<c>Remove = current \ desired</c>) — the over-exposure guard that enforces the projection invariant
    /// "ACS membership ⊆ Dataverse-derived access". A 429 surfaces as
    /// <see cref="AcsRateLimitException"/> (retryable). Returns an empty set when the thread has no
    /// participants; the underlying enumeration pages internally.
    /// </summary>
    Task<IReadOnlyList<string>> ListParticipantsAsync(
        string chatThreadId,
        CancellationToken ct = default);
}
