namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade (ADR-013) that CAPTURES a per-user standing <c>Preference</c> memory item — the
/// E3 feedback→memory loop (FR-08). It is the CRUD-safe seam through which
/// <see cref="Sprk.Bff.Api.Services.Ai.Feedback.FeedbackService"/> (the one-way thumbs/comment sink)
/// persists a governed User-scope preference into the SHARED structured-memory store WITHOUT ever
/// touching the memory internals.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this facade exists (FR-08).</b> Today <c>FeedbackService.SubmitAsync</c> writes thumbs±comment
/// to the <c>feedback</c> Cosmos container and NOTHING consumes it into memory — the P3 learning loop
/// never closes. This facade is the missing pipeline: an explicit standing directive ("do this every
/// time") — or, inferred, a thumbs-down+comment — persists a <see cref="Memory.MemoryFactType.Preference"/>
/// item (added in task 030) tied to that user, which task 030's <c>ToUserPromptFragmentAsync</c> already
/// recalls into the "About You" fragment and task 032's governed producer consumes.
/// </para>
/// <para>
/// <b>ADR-013 — CRUD stays clean.</b> <c>FeedbackService</c> injects ONLY this facade — never
/// <see cref="Memory.IMemoryItemStore"/> or any other memory-internal type. The implementation
/// (<see cref="PreferenceMemoryCapture"/>) lives under <c>Services/Ai/</c> (the sanctioned AI zone) and
/// delegates to the SHARED <see cref="Memory.IMemoryItemStore"/> — no forked logic, no second memory
/// write path, no parallel feedback store (§11 default-to-reuse). Same discipline as
/// <see cref="IComposeMemoryCapture"/>.
/// </para>
/// <para>
/// <b>Per-user ONLY — canonical keying (ADR-042 / ADR-039).</b> A preference is written at
/// <see cref="MemoryScope.User"/> keyed by the caller's canonical Dataverse <c>systemuserid</c> — the
/// implementation resolves the supplied AAD <c>oid</c> to the <c>systemuserid</c> via the existing
/// ADR-028 one-hop machinery so the item recalls under the SAME key chat-side memory uses. It NEVER
/// mutates the global ADR-039 capability catalog (that stays HITL) and never promotes a per-user
/// preference into shared/global state. When the caller's identity cannot be resolved to a canonical
/// user, the capture is SKIPPED (never guessed onto a wrong partition).
/// </para>
/// <para>
/// <b>Governance envelope is METADATA, not a gate (ADR-042 FR-B-08).</b> <c>Source</c> (user vs
/// ai-derived) / <c>SessionId</c> DESCRIBE the item; they do not gate the write. The FR-B-03 user
/// review/delete surface is the control. <c>TrustLevel</c> is carried INERT (written null, never acted
/// on) — the untrusted-origin / trustLevel-enforcement / memory-poisoning governance is DEFERRED to the
/// security project (#616); adopting it later requires no reshaping of this contract.
/// </para>
/// <para>
/// <b>Best-effort:</b> every failure mode returns a <see cref="PreferenceMemoryCaptureOutcome"/> (never
/// throws, except an honoured <see cref="OperationCanceledException"/> on request cancellation) so a
/// caller's feedback submission is NEVER blocked or failed by preference capture.
/// </para>
/// </remarks>
public interface IPreferenceMemoryCapture
{
    /// <summary>
    /// Captures the supplied <paramref name="input"/> as a governed per-user
    /// <see cref="Memory.MemoryFactType.Preference"/> memory item (User scope), delegating to the shared
    /// memory store. Resolves the AAD oid to the canonical Dataverse <c>systemuserid</c> before keying.
    /// Best-effort: never throws (except an honoured cancellation).
    /// </summary>
    /// <param name="input">The preference to persist (user identity + directive + provenance).</param>
    /// <param name="ct">Request-scope cancellation token.</param>
    /// <returns>A best-effort <see cref="PreferenceMemoryCaptureOutcome"/> — advisory only.</returns>
    Task<PreferenceMemoryCaptureOutcome> CapturePreferenceAsync(
        PreferenceCaptureInput input,
        CancellationToken ct = default);
}

/// <summary>
/// A neutral, feedback-agnostic per-user preference-capture request. Kept free of Feedback/AI-internal
/// types so PublicContracts never depends on them. The CALLER decides the trigger + provenance
/// (<paramref name="Origin"/> = <c>user</c> for an explicit standing directive; <c>ai-derived</c> where
/// the pipeline INFERS the preference — e.g. from a thumbs-down comment) and the confirmation semantics
/// (<paramref name="ConfirmedByUser"/> = true only for an explicit user directive).
/// </summary>
/// <param name="TenantId">Tenant identifier (ADR-015 Tier 3 isolation). Stored as metadata on the item.</param>
/// <param name="UserAadObjectId">The caller's Azure AD <c>oid</c>. Resolved to the canonical Dataverse
/// <c>systemuserid</c> by the implementation before the User-scope item is keyed (ADR-028 / ADR-042).</param>
/// <param name="Key">Short stable label identifying the preference (the directive text) — the supersession
/// identity: a repeated capture of the same directive UPDATES rather than duplicates.</param>
/// <param name="Value">The preference content (the standing directive as stated).</param>
/// <param name="Origin">Provenance origin class — a <see cref="MemoryOrigin"/> value (<c>user</c> |
/// <c>ai-derived</c>). Metadata; does NOT gate the write.</param>
/// <param name="ConfirmedByUser">True only when the trigger is an EXPLICIT user directive; false for an
/// inferred (ai-derived) preference pending the FR-B-03 review surface.</param>
/// <param name="SessionId">Provenance: originating chat/analysis session id, when known.</param>
public sealed record PreferenceCaptureInput(
    string TenantId,
    string UserAadObjectId,
    string Key,
    string Value,
    string Origin,
    bool ConfirmedByUser,
    string? SessionId);

/// <summary>
/// Outcome of a preference-capture attempt. Mirrors the best-effort shape of
/// <see cref="ComposeMemoryCaptureOutcome"/> so callers can treat it as advisory. Never carries an
/// exception — a thrown store failure degrades to <see cref="PreferenceMemoryCaptureStatus.Failed"/>.
/// </summary>
public sealed record PreferenceMemoryCaptureOutcome
{
    /// <summary>Terminal disposition of the capture attempt.</summary>
    public required PreferenceMemoryCaptureStatus Status { get; init; }

    /// <summary>The persisted item id (deterministic over scope+factType+key) when captured; else null.</summary>
    public string? ItemId { get; init; }

    /// <summary>Human-readable reason for a Skipped / Failed outcome. Null when captured.</summary>
    public string? Reason { get; init; }

    /// <summary>The preference was persisted (see <see cref="ItemId"/>).</summary>
    public static PreferenceMemoryCaptureOutcome Captured(string itemId) =>
        new() { Status = PreferenceMemoryCaptureStatus.Captured, ItemId = itemId };

    /// <summary>Nothing was persisted (unresolvable user, empty directive, etc.) — a no-op, not a failure.</summary>
    public static PreferenceMemoryCaptureOutcome Skipped(string reason) =>
        new() { Status = PreferenceMemoryCaptureStatus.Skipped, Reason = reason };

    /// <summary>The store threw unexpectedly — swallowed here so the caller's submission is unaffected.</summary>
    public static PreferenceMemoryCaptureOutcome Failed(string reason) =>
        new() { Status = PreferenceMemoryCaptureStatus.Failed, Reason = reason };
}

/// <summary>Terminal disposition of a <see cref="PreferenceMemoryCaptureOutcome"/>.</summary>
public enum PreferenceMemoryCaptureStatus
{
    /// <summary>The preference item was persisted (see <see cref="PreferenceMemoryCaptureOutcome.ItemId"/>).</summary>
    Captured = 0,

    /// <summary>Nothing to do (unresolvable user / empty directive). Not a failure.</summary>
    Skipped = 1,

    /// <summary>The store threw unexpectedly; capture was abandoned (best-effort, caller unaffected).</summary>
    Failed = 2,
}
