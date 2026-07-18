namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// Owns the Direct 1:1 thread topology's access mechanics (task 043 / FR-09): find-or-create the
/// <c>sprk_communicationthread</c> for an ordered participant pair, read a Direct thread's explicit
/// two-party participant list, and grant a Direct-thread message the Read access it needs to be visible
/// through the impersonated read path (task 050).
/// </summary>
/// <remarks>
/// <para><b>Mechanism (owner decision 2026-07-16, <c>notes/access-model-decision.md</c>): narrow Dataverse
/// OWNERSHIP + "Manage access" (POA) shares — NOT a new grant table.</b> A Direct thread is owned
/// (<c>ownerid</c>) by the participant who started it; the other participant is added via the OOB
/// GrantAccess/POA mechanism (the SAME mechanism <c>PlaybookSharingService</c> already uses for playbook
/// team-sharing). "Who are this Direct thread's two participants" is therefore always exactly
/// <c>{ownerid} ∪ {POA-shared systemuser principals}</c> — no second source of truth, no new schema.</para>
/// <para><b>Why per-MESSAGE grants too (the CRITICAL RISK this task closes).</b>
/// <c>sprk_communication</c> Read is User-level and every <c>sprk_communication</c> row is created by the
/// app-only Dataverse identity (neither the thread owner nor the counterparty is the row's <c>ownerid</c>),
/// so a Direct-thread message is invisible to BOTH participants under impersonation unless explicitly
/// shared. <see cref="GrantMessageAccessAsync"/> is the fix: called once, right after a message is
/// persisted into a Direct-topology thread (both the outbound send path and the inbound ingest path),
/// it grants Read access on that ONE message to the thread's current explicit participants.</para>
/// <para><b>Component Justification (CLAUDE.md §11).</b> (1) Existing — the GrantAccess/POA pattern is
/// proven in this codebase (<c>PlaybookSharingService</c>); a generic Web API singleton
/// (<c>DataverseWebApiService</c>) already hosts the messaging read path's HTTP plumbing. (2) Extension —
/// this service is the ONE place the "who are this Direct thread's two participants" computation lives;
/// it is consumed by <see cref="Membership.IThreadExplicitParticipantReader"/> (ACS reconcile projection),
/// the find-or-create endpoint (dedup), AND the message-grant hook — one component, three call sites, no
/// duplicated POA logic. (3) Cost-of-doing-nothing — without it, a Direct thread has no membership source
/// (FR-09 fails) and a sent Direct-thread message is unreadable by its own participants (the CRITICAL RISK
/// task 043 was scoped to close).</para>
/// </remarks>
public interface IDirectThreadAccessService
{
    /// <summary>
    /// Finds an existing Direct thread between <paramref name="callerSystemUserId"/> and
    /// <paramref name="otherSystemUserId"/> (either direction — ownership is not order-significant for
    /// reuse) or creates a new one (owned by the caller, no ADR-024 regarding anchor, shared to the other
    /// participant via GrantAccess). Starting a 1:1 with the same person twice reuses the SAME thread — no
    /// duplicate.
    /// </summary>
    Task<Guid> FindOrCreateDirectThreadAsync(
        Guid callerSystemUserId,
        Guid otherSystemUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the thread's explicit Direct-topology participants (<c>ownerid</c> ∪ POA-shared systemuser
    /// principals). Returns EMPTY for a non-Direct (or unknown) thread — this is the topology gate that
    /// keeps Open/record-anchored threads unaffected (they derive membership from ADR-034, not this seam).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetParticipantSystemUserIdsAsync(Guid threadId, CancellationToken ct = default);

    /// <summary>
    /// Grants Read access on <paramref name="communicationId"/> so its thread's authorized principals can
    /// see it under the impersonated read (task 050). Topology decides the principal source (task 052 / FR-11):
    /// <b>Direct</b> → <paramref name="threadId"/>'s current explicit two-party list (<c>ownerid</c> ∪
    /// POA-shared principal, UNCHANGED from task 043). <b>Open / record-anchored</b> → the task-041
    /// <c>IThreadMembershipDerivationService.DeriveAuthorizedSetAsync</c> systemuser participants (contacts
    /// skipped — R2 scope). A no-op when the thread has no derivable membership either way. Best-effort /
    /// non-fatal (NFR-02): failures are logged and swallowed — the message already persisted; a missed
    /// grant degrades to "not yet visible", never fails the send/ingest.
    /// </summary>
    Task GrantMessageAccessAsync(Guid communicationId, Guid threadId, CancellationToken ct = default);
}
