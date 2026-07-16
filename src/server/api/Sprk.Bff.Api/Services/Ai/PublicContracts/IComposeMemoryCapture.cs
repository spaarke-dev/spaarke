namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade (ADR-013) that CAPTURES durable Record-scope knowledge distilled from a Compose
/// session into the shared structured-memory store — the compose-r2 FR-30 durable-memory CAPTURE seam
/// (deferral <c>#629</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this facade exists (spaarkeai-compose-r2, FR-30 #629):</b> when Compose promotes an ephemeral
/// document to a saved <c>sprk_document</c> record, the session may carry AI/human-derived durable facts
/// (defined terms today; more distillations later) that should survive the session as Record-scope
/// memory keyed by the new document. This facade is the CRUD-safe seam that lets <c>ComposeService</c>
/// (CRUD code) persist those facts without ever touching the memory internals.
/// </para>
/// <para>
/// <b>ADR-013:</b> <c>ComposeService</c> injects ONLY this facade — never <see cref="Memory.IMemoryItemStore"/>,
/// <c>DataverseFieldMirrorGuard</c>, or any other memory-internal type. The implementation
/// (<see cref="ComposeMemoryCapture"/>) lives under <c>Services/Ai/</c> (the sanctioned AI zone) and
/// delegates to the SHARED <see cref="Memory.IMemoryItemStore"/> — no forked logic, no parallel store.
/// The Tier-1 NetArchTest facade rule stays green.
/// </para>
/// <para>
/// <b>Compose-agnostic contract.</b> The input DTO (<see cref="ComposeInsight"/>) is a neutral,
/// Compose-independent shape so PublicContracts never depends on Compose types. The CALLER distills its
/// domain objects (defined terms, etc.) into <see cref="ComposeInsight"/>s; this facade only maps them
/// onto the governed <c>MemoryItem</c> envelope + persists.
/// </para>
/// <para>
/// <b>Ownership — CANONICAL (compose-r2 FR-30, deferral #629):</b> this is the platform's canonical
/// Compose→Record-memory capture facade, NOT a temporary stand-in. It carries the provenance envelope as
/// METADATA (FR-B-08): <c>Source</c>, <c>BindingId</c>, <c>LedgerRef</c>, <c>SessionId</c>. The
/// UNTRUSTED-ORIGIN / memory-poisoning GATE (<c>TrustLevel</c> enforcement) is DEFERRED to the separate
/// memory-governance project tracked by <c>#629</c>: <c>TrustLevel</c> is carried INERT here — written as
/// null, never acted on — so adopting the gate later requires no reshaping of this contract.
/// </para>
/// <para>
/// <b>Best-effort:</b> every failure mode returns a <see cref="ComposeMemoryCaptureOutcome"/> (never
/// throws, except an honoured <see cref="OperationCanceledException"/> on request cancellation) so a
/// caller's Save is NEVER blocked or failed by memory capture.
/// </para>
/// </remarks>
public interface IComposeMemoryCapture
{
    /// <summary>
    /// Captures the given <paramref name="insights"/> as Record-scope <c>MemoryItem</c>s keyed by
    /// <c>(subjectType, subjectId)</c> (e.g. <c>("sprk_document", documentId)</c>), delegating to the
    /// shared memory store. Best-effort: never throws (except an honoured cancellation).
    /// </summary>
    /// <param name="subjectType">Record-scope subject entity type (e.g. <c>sprk_document</c>).</param>
    /// <param name="subjectId">Record-scope subject id (the saved record's id).</param>
    /// <param name="insights">The distilled durable facts to persist. Empty → a skipped outcome.</param>
    /// <param name="provenance">Tenant + session provenance stamped onto each captured item.</param>
    /// <param name="ct">Request-scope cancellation token.</param>
    /// <returns>A best-effort <see cref="ComposeMemoryCaptureOutcome"/> — advisory only.</returns>
    Task<ComposeMemoryCaptureOutcome> CaptureRecordInsightsAsync(
        string subjectType,
        string subjectId,
        IReadOnlyList<ComposeInsight> insights,
        ComposeMemoryProvenance provenance,
        CancellationToken ct = default);
}

/// <summary>
/// A neutral, Compose-agnostic durable-insight DTO — the CALLER's distilled unit of durable knowledge
/// (a defined term, an extracted fact, etc.). Kept free of Compose types so PublicContracts never depends
/// on Compose. <paramref name="Origin"/> is a <see cref="MemoryOrigin"/> value
/// (<c>user</c> | <c>ai-derived</c> | <c>insights-engine</c>).
/// </summary>
/// <param name="FactType">Descriptive fact category (e.g. <c>defined-term</c>). Mapped to the coarse
/// <c>MemoryFactType</c> enum by the implementation.</param>
/// <param name="Key">Short human-readable label for the fact (e.g. the term text).</param>
/// <param name="Value">The fact content (e.g. the term's definition).</param>
/// <param name="Origin">Provenance origin class — a <see cref="MemoryOrigin"/> value.</param>
/// <param name="Confidence">Optional confidence in [0.0, 1.0]. Null → the store's default is used.</param>
/// <param name="BindingId">Provenance: the Binding whose invocation produced this insight, when AI-derived.</param>
/// <param name="LedgerRef">Provenance: the ADR-040 ledger ref (<c>{bindingId}@t{n}</c>) it originated from, when applicable.</param>
public sealed record ComposeInsight(
    string FactType,
    string Key,
    string Value,
    string Origin,
    double? Confidence,
    string? BindingId,
    string? LedgerRef);

/// <summary>
/// Tenant + session provenance for a capture batch. Stamped onto each persisted <c>MemoryItem</c>'s
/// envelope. Compose-agnostic (no Compose types).
/// </summary>
public sealed record ComposeMemoryProvenance
{
    /// <summary>Tenant identifier (ADR-015 Tier 3 isolation). Stored as metadata on each item.</summary>
    public required string TenantId { get; init; }

    /// <summary>Originating session id, when captured during a session. Null when unknown.</summary>
    public string? SessionId { get; init; }

    /// <summary>Identity that created the items (user id / system principal), when known.</summary>
    public string? CreatedBy { get; init; }
}

/// <summary>
/// Outcome of a Compose memory-capture attempt. Mirrors the best-effort shape of
/// <see cref="DocumentProfileOutcome"/> so callers can treat it as advisory. Never carries an exception —
/// a thrown store failure degrades to <see cref="ComposeMemoryCaptureStatus.Failed"/>.
/// </summary>
public sealed record ComposeMemoryCaptureOutcome
{
    /// <summary>Terminal disposition of the capture attempt.</summary>
    public required ComposeMemoryCaptureStatus Status { get; init; }

    /// <summary>Number of insights actually persisted (0 for Skipped / Failed).</summary>
    public int Count { get; init; }

    /// <summary>Human-readable reason for a Skipped / Failed outcome. Null when captured.</summary>
    public string? Reason { get; init; }

    /// <summary>Some (or zero) insights were persisted successfully.</summary>
    public static ComposeMemoryCaptureOutcome Captured(int count) =>
        new() { Status = ComposeMemoryCaptureStatus.Captured, Count = count };

    /// <summary>Nothing to capture (empty batch) — a no-op, not a failure.</summary>
    public static ComposeMemoryCaptureOutcome Skipped(string reason) =>
        new() { Status = ComposeMemoryCaptureStatus.Skipped, Reason = reason };

    /// <summary>The store threw unexpectedly — swallowed here so the caller's Save is unaffected.</summary>
    public static ComposeMemoryCaptureOutcome Failed(string reason) =>
        new() { Status = ComposeMemoryCaptureStatus.Failed, Reason = reason };
}

/// <summary>Terminal disposition of a <see cref="ComposeMemoryCaptureOutcome"/>.</summary>
public enum ComposeMemoryCaptureStatus
{
    /// <summary>Insights were persisted (see <see cref="ComposeMemoryCaptureOutcome.Count"/>).</summary>
    Captured = 0,

    /// <summary>Nothing to do (empty batch). Not a failure.</summary>
    Skipped = 1,

    /// <summary>The store threw unexpectedly; capture was abandoned (best-effort, caller unaffected).</summary>
    Failed = 2,
}
