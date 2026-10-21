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
    /// <para>This is the reconcile INSIDE the step after a re-file (<see cref="FilingReconcileHost.AfterRefileStep"/>): a join
    /// it makes runs no step of its own. The backfill sweep, which runs it on its own, calls
    /// <see cref="ReconcileThreadForFilingAsync(Guid, FilingReconcileHost, CancellationToken)"/>.</para>
    /// </summary>
    Task ReconcileThreadForFilingAsync(Guid communicationId, CancellationToken ct = default);

    /// <summary>
    /// <see cref="ReconcileThreadForFilingAsync(Guid, CancellationToken)"/>, saying whether it runs inside the step after a
    /// re-file or on its own (#2110, <see cref="FilingReconcileHost"/>) — which decides whether a join it makes, filing the
    /// email under a record it was not filed to, runs that step.
    /// </summary>
    Task ReconcileThreadForFilingAsync(Guid communicationId, FilingReconcileHost host, CancellationToken ct = default);

    /// <summary>
    /// <see cref="ReconcileThreadForFilingAsync(Guid, FilingReconcileHost, CancellationToken)"/> for a re-file a PERSON made
    /// (#2143 round 7, F1): <paramref name="secureExitCaller"/> is that re-file's
    /// <see cref="Sprk.Bff.Api.Services.Dataverse.RecordReparent.SecureExitCaller"/>, carried into the join that moves the email
    /// to its record's thread, so a move off a secure record's thread is decided on that person's F3 rights — the move the
    /// re-file was already permitted to make. <c>null</c> (the other overloads) acts for nobody: a move out of a secure
    /// record's thread is refused and the email keeps the thread's isolation.
    /// <para>#2143 round 8 (F1-A): <paramref name="beforeMessageGrant"/>, when given, runs once the join that moves the email
    /// to its record's thread has landed and BEFORE the email's #1629 grant to that record's members — the step after a re-file
    /// takes the email's secure-child mirror off there when that join took it out of isolation, so the mirror's removal (every
    /// direct share) never revokes the grant the move gives. <c>null</c>: nothing runs. Round 10 (F2-a): it runs once before the
    /// #1629 grant on EVERY path that gives one — that move, rule 2's whole-thread anchor (before its grants), and a threadless
    /// email's join (between the join and its grant).</para>
    /// </summary>
    Task ReconcileThreadForFilingAsync(
        Guid communicationId,
        FilingReconcileHost host,
        Sprk.Bff.Api.Services.Access.SecureRemovalCaller? secureExitCaller,
        Func<CancellationToken, Task>? beforeMessageGrant,
        CancellationToken ct = default);

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
    /// <item>the thread carries MORE than one typed regarding lookup (the anchor is then the first in priority order, which
    /// is not necessarily the record the thread is for) → <see cref="ThreadOwnerReconcileOutcome.SkippedAmbiguousAnchor"/>,
    /// the thread is left — a wrong re-own is worse than an unowned thread;</item>
    /// <item>already owned by that team → <see cref="ThreadOwnerReconcileOutcome.AlreadyCorrect"/>, no write;</item>
    /// <item>otherwise the owner is assigned → <see cref="ThreadOwnerReconcileOutcome.Assigned"/>, and the result carries the
    /// owner it replaced (<see cref="ThreadOwnerReconcileResult.PreviousOwnerType"/> /
    /// <see cref="ThreadOwnerReconcileResult.PreviousOwnerId"/>) so a wrong re-own can be reversed from the run history.</item>
    /// </list>
    /// With <paramref name="dryRun"/> the same answer is computed and NOTHING is written (<c>Assigned</c> then means "would
    /// be assigned"). Best-effort / non-fatal (NFR-02): never throws; a fault is logged and answers
    /// <see cref="ThreadOwnerReconcileOutcome.Failed"/>.
    /// </summary>
    Task<ThreadOwnerReconcileResult> ReconcileAnchoredThreadOwnerAsync(
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

/// <summary>
/// #2110 (ontology K2): what runs <see cref="IThreadResolver.ReconcileThreadForFilingAsync(Guid, FilingReconcileHost, CancellationToken)"/>,
/// which decides whether a join it makes runs the step after a re-file. Its one join that can file an email under a NEW record
/// is a threadless email joining the thread its conversation key finds — anchored to a record R2 the email is not filed to.
/// </summary>
/// <remarks>
/// Component Justification (CLAUDE.md §11): (1) Existing — the reconcile's internal joins ran no step, on the premise that the
/// step after a re-file was the one running it. (2) Extension — one argument of the existing reconcile, not a second one; the
/// join reuses <c>ThreadResolver.JoinThreadAsync</c>'s step. (3) Cost of doing nothing — the backfill sweep runs the reconcile on
/// its own, so its join under R2 re-owned the email (into a Secure record's isolation, say) with no mirror, no pass over what is
/// filed under it and no gate Decision Record re-own.
/// </remarks>
public enum FilingReconcileHost
{
    /// <summary>
    /// Inside <c>SecureChildReconciler.AfterRefileAsync</c> (every re-file writer): the step's mirror, its pass and its gate
    /// Decision Record step run AFTER the reconcile and see any join it made, so a join runs no step of its own.
    /// </summary>
    AfterRefileStep,

    /// <summary>
    /// On its own — the backfill sweep (<c>CommunicationThreadFilingSweepJob</c>). A join that files the email under a record
    /// it is NOT filed to runs the step after a re-file for that join, once (<c>RefileThreadStep.PlacedByWriter</c>); a join
    /// under a record it is already filed to adds no parent — the filing the sweep follows ran the step — and runs none.
    /// </summary>
    Standalone,
}

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

    /// <summary>
    /// More than one typed regarding lookup is populated on the thread, so which record it is "for" cannot be told; left as it
    /// is (never re-owned). Appended last so the values above keep their numbers.
    /// </summary>
    SkippedAmbiguousAnchor,
}

/// <summary>
/// The answer of <see cref="IThreadResolver.ReconcileAnchoredThreadOwnerAsync"/>: the <see cref="Outcome"/>, and for an
/// <see cref="ThreadOwnerReconcileOutcome.Assigned"/> thread the owner it replaced and the team it gets — the before-state a
/// wrong re-own is reversed from. Converts implicitly from a bare outcome (every other answer carries nothing more).
/// </summary>
/// <param name="Outcome">What was (or, on a dry run, would be) done.</param>
/// <param name="PreviousOwnerType">Logical name of the previous <c>ownerid</c> (<c>systemuser</c> / <c>team</c>); null when none was read.</param>
/// <param name="PreviousOwnerId">Id of the previous <c>ownerid</c>; null when none was read.</param>
/// <param name="NewOwnerTeamId">The team the thread is (or would be) given.</param>
public sealed record ThreadOwnerReconcileResult(
    ThreadOwnerReconcileOutcome Outcome,
    string? PreviousOwnerType = null,
    Guid? PreviousOwnerId = null,
    Guid? NewOwnerTeamId = null)
{
    /// <summary>A bare outcome with no before-state.</summary>
    public static implicit operator ThreadOwnerReconcileResult(ThreadOwnerReconcileOutcome outcome) => new(outcome);
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

    /// <summary>
    /// Owner decision D8 (external-access r3 task 065 fix round 6, 2026-10-14). True only for a partner's portal post. The
    /// fallback ladder's per-record (and per-user) default-thread lookup then joins a default thread ONLY when it is
    /// EXPLICITLY Open, and otherwise finds-or-creates a new Open default thread, so a post is never filed into a Private
    /// default thread the partner cannot read back. False (the default) keeps the lookup exactly as it was for every
    /// internal caller.
    /// </summary>
    public bool RequireOpenDefaultThread { get; init; }
}
