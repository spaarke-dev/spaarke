using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Direction-symmetric, channel-agnostic thread resolver (FR-06). Find-or-creates the
/// <c>sprk_communicationthread</c> grouping record for a persisted <c>sprk_communication</c> and stamps
/// the <c>sprk_communicationthread</c> lookup on it, so email reply-chains and chat conversations both
/// map onto ONE queryable thread record. Invoked from BOTH the inbound capture path AND the outbound send
/// path, for ALL channels — the thread analog of the direction-symmetric enrichment invocation (ADR-045
/// rule 3): one inbound call site (email = <see cref="IncomingCommunicationProcessor"/>, chat =
/// <see cref="Channels.MessagingIngestor"/>), one outbound (<see cref="CommunicationService"/>), one
/// channel-agnostic seam with a per-channel key strategy.
/// </summary>
/// <remarks>
/// <para>
/// <b>3-tier auto-threading (FR-09):</b> the resolver descends a strict ladder so a message NEVER orphans —
/// Tier 1 = the subject/conversation thread (JOIN by per-channel key, or CREATE a record-anchored / direct
/// thread), Tier 2 = the per-record default thread (ONE per regarding record, created lazily and marked
/// <c>sprk_isdefaultthread</c>), Tier 3 = the per-user master catch-all (ONE per owning user). Every
/// inbound/outbound message on a registered channel therefore resolves to a non-null thread; the only null
/// returns are a best-effort failure (see below) or the misconfiguration guard (no key strategy for the
/// channel).
/// </para>
/// <para>
/// <b>Best-effort / non-fatal (NFR-02):</b> a resolve/create failure MUST NOT fail the send or
/// inbound-capture path. The implementation logs and returns <c>null</c>; the message still persists
/// WITHOUT a <c>sprk_communicationthread</c>. Call sites also treat a null return as "no thread yet".
/// </para>
/// <para>
/// <b>Anchor reuses ADR-024:</b> when a NEW thread is created, its anchor is copied from the message's
/// resolved regarding (the denormalized <c>sprk_regardingrecord*</c> family, falling back to the typed
/// <c>sprk_regarding*</c> lookups via <see cref="Engine.RegardingFieldMap"/>) — NOT a second regarding
/// mechanism.
/// </para>
/// <para><b>Point-forward only (FR-06):</b> no historical backfill of pre-existing messages.</para>
/// </remarks>
public interface IThreadResolver
{
    /// <summary>
    /// Resolves (find-or-create) the thread for the persisted communication and assigns the
    /// <c>sprk_communicationthread</c> lookup. Returns the thread id, or <c>null</c> when no thread could
    /// be resolved/created (never throws to the caller — NFR-02).
    /// </summary>
    Task<Guid?> ResolveAndAssignThreadAsync(ThreadResolutionRequest request, CancellationToken ct = default);

    /// <summary>
    /// <see cref="ResolveAndAssignThreadAsync"/>, also returning WHO the message's read grant is for, as the thread was
    /// when the message joined it (#1629): the audience <see cref="Access.IDirectThreadAccessService.GrantMessageAccessAsync"/>
    /// grants. A thread's type and regarding lookups are columns its owner can rewrite between the join and the grant, so
    /// the grant never re-reads them. <c>null</c> when no thread was resolved (never throws, NFR-02).
    /// </summary>
    Task<ThreadAssignment?> ResolveAndAssignAsync(ThreadResolutionRequest request, CancellationToken ct = default);

    /// <summary>
    /// FR-07 marker-gated naming re-derive (task 071). Recomputes and updates <c>sprk_name</c> from the
    /// thread's CURRENT regarding ONLY while the task-002 <c>sprk_nameisautoderived</c> marker is Auto
    /// (<c>true</c>, or missing/null on a pre-002-schema thread — treated the same as Auto); a marker of
    /// Edited (<c>false</c>, set when a user renamed the thread) preserves the existing name untouched.
    /// Guards against ever resolving the Tier-3 per-user master thread's <c>"systemuser"</c> key as a
    /// regarding record (see <see cref="ThreadResolver"/> remarks). Best-effort/non-fatal (NFR-02): any
    /// read/update failure is logged and swallowed, never thrown to the caller.
    /// </summary>
    /// <remarks>
    /// <b>Trigger wiring is out of this task's scope.</b> The RegardingResolver PCF writes the thread's
    /// regarding directly via <c>Xrm.WebApi.updateRecord</c> (client-side, bypasses the BFF entirely — see
    /// <c>ResolverWriteHandler.applyRegardingSelection</c>/<c>clearRegarding</c>), and a user's direct edit
    /// of <c>sprk_name</c> on the form likewise never reaches the BFF. This method is the CAPABILITY those
    /// events should invoke once wired; the recommended integration is a Dataverse plugin on Update of
    /// <c>sprk_communicationthread</c> (see <see cref="ThreadResolver"/> XML doc for the full recommendation).
    /// </remarks>
    Task ReDeriveThreadNameAsync(Guid threadId, CancellationToken ct = default);

    /// <summary>
    /// spaarkeai-word-add-in-r1 task 129: after an EMAIL was re-filed (by <c>SecureChildReconciler.AfterRefileAsync</c>, the
    /// step every re-file writer shares, or by a writer that does not run it: the association engine's <c>ResolveAsync</c>
    /// and the playbook update node), its thread follows the filing so the email shows on the record's Messages panel, which
    /// lists threads by the THREAD's typed regarding. Let R be the email's primary typed regarding (ADR-024 priority) and T
    /// its thread:
    /// <list type="bullet">
    /// <item>not an email → nothing;</item>
    /// <item>no thread: filed → a thread is resolved for it (<see cref="ResolveAndAssignAsync"/>) and it is granted to the
    /// audience that join fixed; unfiled → nothing;</item>
    /// <item>unfiled (an unlink) and T is an Open thread anchored to a record → the email moves to the thread the unfiled
    /// ingest gives it (the fallback ladder: the per-user master keyed on the email's owner), with no grant; otherwise
    /// nothing;</item>
    /// <item>T is active and already anchored to ANY record the email is filed to (any of its typed regarding lookups, not
    /// only R), or T is not Open (a private thread keeps its isolation) → nothing;</item>
    /// <item>T is active, not a default thread (neither the per-user master nor a record's per-record default), every active message
    /// in T is an email, no OTHER active message in T is filed to a record other than R — whether T is unanchored or anchored
    /// to a previous record (owner clarification 2026-10-11) — and neither T's owner nor R's S6 owner is the Secure team
    /// (an undeterminable owner counts as Secure) → the whole of T follows: anchored to R (its typed regarding, R's primary
    /// name read server-side, a previous record's lookup and display fields cleared, Record-Anchored), owned by R's team (S6;
    /// a refusal, or a record that is not an ownership parent, leaves T and the email untouched), its name re-derived
    /// (marker-gated), and every active message in T filed to R is granted to R's members (unfiled messages are not);</item>
    /// <item>otherwise (T is inactive or a default thread, holds a non-email message or another active message filed to a
    /// different record, or T or R is secure) → only the email moves, to R's active per-record default thread (Tier 2)
    /// through the S6 join, and is granted to R's members.</item>
    /// </list>
    /// The grant audience is always R — the email's own regarding, just validated by the filing write — never a re-read of
    /// a thread column (#1629). Prior grants are not revoked. Best-effort / non-fatal (NFR-02): never throws; any failure is
    /// logged and the filing stands.
    /// </summary>
    Task ReconcileThreadForFilingAsync(Guid communicationId, CancellationToken ct = default);

    /// <summary>
    /// spaarkeai-word-add-in-r1 task 129 (backfill sweep): gives an ACTIVE, Open thread that is anchored to a record (a typed
    /// regarding lookup — the one anchor rule, <see cref="ThreadResolver"/>'s <c>ThreadAnchorOf</c>) the owner S6 gives such a
    /// thread: the anchor record's team, resolved by the SAME step the filing reconcile and the thread-create paths use. Fixes
    /// threads created before S6 reached them (owned by the BFF's app user in the root business unit, so a child-business-unit
    /// user could not read them). Changes the owner and nothing else.
    /// <list type="bullet">
    /// <item>the thread is unreadable, inactive, not Open, unanchored, or anchored to a record that owns no thread (a party:
    /// account / contact / organization) → <see cref="ThreadOwnerReconcileOutcome.NotApplicable"/>;</item>
    /// <item>S6 refuses the anchor record's owner → <see cref="ThreadOwnerReconcileOutcome.Refused"/>, the thread is left;</item>
    /// <item>the thread's current owner, or the owner S6 gives, is the Secure Record team — or that cannot be told (the same
    /// detection as the filing reconcile's whole-thread move: <c>SecureChildShareSynchronizer</c> / <c>IsSecureOwner</c>) →
    /// <see cref="ThreadOwnerReconcileOutcome.SkippedSecure"/>, the thread is left (a secure record's mirrored shares are not
    /// written here);</item>
    /// <item>already owned by that team → <see cref="ThreadOwnerReconcileOutcome.AlreadyCorrect"/>, no write;</item>
    /// <item>otherwise the owner is assigned → <see cref="ThreadOwnerReconcileOutcome.Assigned"/>.</item>
    /// </list>
    /// With <paramref name="dryRun"/> the same answer is computed and NOTHING is written (<c>Assigned</c> then means "would
    /// be assigned"). Best-effort / non-fatal (NFR-02): never throws; a fault is logged and answers
    /// <see cref="ThreadOwnerReconcileOutcome.Failed"/>.
    /// </summary>
    Task<ThreadOwnerReconcileOutcome> ReconcileAnchoredThreadOwnerAsync(
        Guid threadId, bool dryRun = false, CancellationToken ct = default);

    /// <summary>
    /// FR-17 rename (task 004): sets <c>sprk_name</c> AND flips the naming-edited marker
    /// (<c>sprk_nameisautoderived</c> → Edited/<c>false</c>) in ONE write, so a later
    /// <see cref="ReDeriveThreadNameAsync"/> short-circuits at the marker gate and never overwrites the
    /// user-chosen name (edit-preserve). Returns the persisted (trimmed + truncated) name. This BFF write is the
    /// ONLY marker-flip path — the rename routes through the BFF, NEVER a Dataverse plugin (hard MUST NOT).
    /// Unlike <see cref="ReDeriveThreadNameAsync"/> this is a user action and is NOT best-effort: a write failure
    /// propagates to the caller (the rename endpoint) rather than being swallowed. A blank name is rejected.
    /// </summary>
    Task<string> RenameThreadAsync(Guid threadId, string name, CancellationToken ct = default);

    /// <summary>
    /// FR-24 pin/unpin (task 041): sets <c>sprk_ispinned</c> in a single write. Mirrors
    /// <see cref="RenameThreadAsync"/>'s shape — a user action, NOT best-effort (a write failure propagates to the
    /// caller, i.e. the pin endpoint, rather than being swallowed like <see cref="ReDeriveThreadNameAsync"/>). This
    /// BFF write is the ONLY pin-state write path — no Dataverse plugin. Returns the persisted value (echoed by the
    /// endpoint).
    /// </summary>
    Task<bool> SetPinnedAsync(Guid threadId, bool pinned, CancellationToken ct = default);

    /// <summary>
    /// Creates a NEW named, record-anchored thread (R3 UAT 2026-07-23 item 9) OWNED by
    /// <paramref name="ownerSystemUserId"/> and anchored to <paramref name="regarding"/> (the denormalized
    /// <c>sprk_regardingrecord*</c> pointer — REUSE of the ADR-024 regarding family, not a second mechanism;
    /// <c>sprk_threadtype</c> = Record-Anchored). When <paramref name="name"/> is blank the name derives from
    /// the record name (marker Auto → re-derives on a later regarding change); a provided name is stamped
    /// Edited (<c>sprk_nameisautoderived=false</c>) so the auto re-derive never overwrites it. Owner = caller
    /// so the new (empty) thread is immediately visible in the caller's all-mode thread list. User action —
    /// NOT best-effort: a write failure propagates to the endpoint. Returns the new thread id.
    /// </summary>
    Task<Guid> CreateRecordThreadAsync(
        Guid ownerSystemUserId, string? name, RecordThreadAnchor regarding, CancellationToken ct = default);

    /// <summary>
    /// Round-7 item 7 soft-delete: DEACTIVATES a thread (<c>sprk_communicationthread</c>) by setting its
    /// <c>statecode</c>=Inactive (<c>statuscode</c>=Inactive) rather than physically deleting it — reversible +
    /// preserves audit/archive history (operator decision, round 7). The deactivated thread drops out of the
    /// impersonated list/read on the next load. User action, NOT best-effort (mirrors <see cref="SetPinnedAsync"/>):
    /// a write failure propagates to the endpoint. This BFF write is the only thread-deactivate path — no Dataverse
    /// plugin. The endpoint authorizes the caller against the thread (impersonated visibility) BEFORE calling this.
    /// </summary>
    Task DeactivateThreadAsync(Guid threadId, CancellationToken ct = default);

    /// <summary>
    /// Round-7 item 8 soft-delete: DEACTIVATES a single message (<c>sprk_communication</c>) by setting its
    /// <c>statecode</c>=Inactive (<c>statuscode</c>=Inactive) rather than physically deleting it — reversible +
    /// preserves the communication record (operator decision, round 7). The deactivated message drops out of the
    /// impersonated thread read on the next poll. User action, NOT best-effort. This lives on the thread-resolver
    /// as the single communication-write seam (alongside rename/pin/create). The endpoint authorizes the caller
    /// against the message (impersonated visibility) BEFORE calling this. Email-type deletion is NOT routed here —
    /// the client only offers this for message-type rows (email deletion is owned by the email surface).
    /// </summary>
    Task DeactivateMessageAsync(Guid communicationId, CancellationToken ct = default);
}

/// <summary>
/// The ADR-024 regarding record a new record-anchored thread is created against (item 9). Mirrors the
/// denormalized <c>sprk_regardingrecord*</c> pointer the resolver already stamps at thread create.
/// </summary>
public sealed record RecordThreadAnchor(string EntityType, string RecordId, string? RecordName);

/// <summary>What <see cref="IThreadResolver.ReconcileAnchoredThreadOwnerAsync"/> did (or, on a dry run, would do) for a thread.</summary>
public enum ThreadOwnerReconcileOutcome
{
    /// <summary>The owner was set to the anchor record's team (on a dry run: would be set).</summary>
    Assigned,

    /// <summary>The thread is already owned by the anchor record's team; nothing written.</summary>
    AlreadyCorrect,

    /// <summary>Its current owner or the owner S6 gives is the Secure Record team, or that could not be told; left as it is.</summary>
    SkippedSecure,

    /// <summary>Not a thread this applies to (unreadable, inactive, not Open, unanchored, or anchored to a party); left as it is.</summary>
    NotApplicable,

    /// <summary>S6 refused the anchor record's owner; left as it is.</summary>
    Refused,

    /// <summary>A fault (logged); left as it is.</summary>
    Failed,
}

/// <summary>Whose read grant a message gets from the thread it joined (#1629).</summary>
public enum ThreadGrantAudience
{
    /// <summary>Nobody: the per-user master catch-all (Tier 3), or a record thread no typed regarding lookup anchors.</summary>
    None,

    /// <summary>A Direct 1:1 thread's two-party list (its owner and the user it is shared to).</summary>
    DirectParticipants,

    /// <summary>
    /// The anchor record's members: the users its user lookups name, plus the members of every team a lookup names,
    /// EXCEPT the team that owns the MESSAGE (its members already read it). The anchor's own owner team is expanded when it
    /// is not the message's owner. A secure message is never granted.
    /// </summary>
    AnchorRecordMembers,
}

/// <summary>
/// A message's assignment to a thread, and the read-grant audience fixed AT THE JOIN (#1629): the topology and anchor the
/// join matched or read, carried into <see cref="Access.IDirectThreadAccessService.GrantMessageAccessAsync"/> so a thread
/// owner who flips its type or swaps its lookup after the join cannot redirect the grant. A per-record default (Tier 2)
/// carries the message's OWN regarding, never a re-read of the thread.
/// </summary>
public sealed record ThreadAssignment(
    Guid ThreadId,
    ThreadGrantAudience Audience,
    string? AnchorEntityType = null,
    Guid? AnchorRecordId = null)
{
    /// <summary>No grant from this thread.</summary>
    public static ThreadAssignment NoGrant(Guid threadId) => new(threadId, ThreadGrantAudience.None);

    /// <summary>A Direct 1:1 thread's two-party grant.</summary>
    public static ThreadAssignment Direct(Guid threadId) => new(threadId, ThreadGrantAudience.DirectParticipants);

    /// <summary>A record thread's grant, to the members of the record it was anchored to at the join.</summary>
    public static ThreadAssignment Anchored(Guid threadId, string anchorEntityType, Guid anchorRecordId) =>
        new(threadId, ThreadGrantAudience.AnchorRecordMembers, anchorEntityType, anchorRecordId);
}

/// <summary>
/// Channel-neutral input to <see cref="IThreadResolver.ResolveAndAssignThreadAsync"/>. The resolver
/// dispatches to a per-channel key strategy by <see cref="ChannelType"/> (email = <c>sprk_inreplyto</c>
/// ancestry root; <c>Message</c> = ACS <see cref="AcsThreadId"/>).
/// </summary>
public sealed record ThreadResolutionRequest
{
    /// <summary>The already-persisted <c>sprk_communication</c> whose thread is being resolved.</summary>
    public required Guid CommunicationId { get; init; }

    /// <summary>Channel dispatch key: <c>Email</c> → ancestry-root strategy; <c>Message</c> → ACS thread strategy.</summary>
    public required CommunicationType ChannelType { get; init; }

    /// <summary>Direction (recorded for symmetry/provenance; the resolver does not branch on it).</summary>
    public required CommunicationDirection Direction { get; init; }

    /// <summary>
    /// Normalized envelope — the email strategy reads <see cref="NormalizedMessage.InReplyTo"/> /
    /// <see cref="NormalizedMessage.References"/> and <see cref="NormalizedMessage.Subject"/>.
    /// </summary>
    public required NormalizedMessage Message { get; init; }

    /// <summary>
    /// ACS <c>ChatThreadId</c> for the <c>Message</c> channel — the <c>sprk_communicationchannelref</c>
    /// external key. Null for email and when unavailable (e.g. outbound chat in R1, where the authoritative
    /// ACS thread id is carried by the inbound echo path). A <c>Message</c> request with no thread id is a
    /// no-op (the resolver skips rather than creating an ungroupable thread).
    /// </summary>
    public string? AcsThreadId { get; init; }
}
