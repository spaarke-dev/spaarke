using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Models.Memory;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// R6 Pillar 7 (task 067, D-C-20) — hierarchical memory composition contract.
///
/// <para>
/// Orchestrates the three Pillar 7 primitives (compression / pinned-context / selective
/// recall) into a single tagged memory block consumed by the chat prompt-assembly path
/// (task 068). Layers per spec FR-44:
/// <list type="number">
///   <item><b>Recent verbatim</b> — last <em>N</em> turns as-is (default <em>N</em> = 10).</item>
///   <item><b>Compressed mid-distance</b> — turns <em>[length - MidWindowEnd, length - N)</em>
///   folded into a single LLM-generated summary via
///   <see cref="ISummarizationCompressionService"/>.</item>
///   <item><b>Retrieved old</b> — when the conversation has at least <em>MidWindowEnd</em> turns,
///   the most-relevant pins for the current user message (top-K by cosine similarity)
///   surfaced via <see cref="IPinnedContextRecallService"/>. Represents the "older context
///   surfaced by similarity" tier; reuses pinned-context as the substrate because no
///   independent chat-turn similarity primitive exists in R6.</item>
///   <item><b>Pinned context</b> — ALL pinned items for the (tenant, user, matter) tuple,
///   grouped by <see cref="PinType"/>, ALWAYS included. NEVER dropped by budget
///   enforcement per the spec FR-42 invariant.</item>
/// </list>
/// </para>
///
/// <para>
/// Budget enforcement (NFR-10): when the four layers' aggregate token estimate exceeds
/// the configured <c>TotalTokenBudget</c> (default 8K), layers are dropped in priority
/// order: retrieved-old → compressed-mid → recent-verbatim oldest-first. The pinned tier
/// is NEVER dropped; if the pinned tier alone exceeds the budget, the composed result
/// returns ONLY the pinned tier (the soft-fail posture preserves the FR-42 invariant —
/// task 068 / the chat prompt builder is responsible for any subsequent hard guard).
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary</b>: this service is internal to the chat memory pipeline. CRUD-side
/// code MUST NOT depend on it (ADR-013 §A.4 — no PublicContracts facade required because
/// the only consumer is the AI-internal chat prompt-assembly path / task 068). It lives
/// under <see cref="Sprk.Bff.Api.Services.Ai.Memory"/> per the Memory tree convention.
/// </para>
/// <para>
/// <b>ADR-014 invariant</b>: <paramref name="MemoryCompositionRequest.TenantId"/> flows
/// through to every <see cref="IPinnedContextRepository"/> + <see cref="IPinnedContextRecallService"/>
/// call as the Cosmos partition key. Cross-tenant reads are structurally impossible.
/// </para>
/// <para>
/// <b>ADR-015 invariant</b>: pin content and chat-message bodies are user-authored.
/// Implementations MUST NOT log content bodies — only deterministic identifiers
/// (tenantId, userId, matterId, message counts, pin counts) appear in telemetry.
/// </para>
/// <para>
/// <b>Soft-failure contract</b>: the service returns <see cref="MemoryComposition.Empty"/>
/// (NOT a thrown exception) when (a) the kill switch is off, (b) no conversation history
/// and no pins exist. Per-primitive soft-failures (compression returns null, recall
/// returns empty) degrade gracefully — the affected layer is omitted; the other layers
/// still compose.
/// </para>
/// </remarks>
public interface IMemoryCompositionService
{
    /// <summary>
    /// Compose the four-layer hierarchical memory block for the current chat turn.
    /// </summary>
    /// <param name="request">Composition inputs (tenant / user / matter / conversation /
    /// current message). See <see cref="MemoryCompositionRequest"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="MemoryComposition"/> with tagged layers. Each present layer carries
    /// its provenance ("recent" / "compressed" / "retrieved" / "pinned") so the prompt
    /// builder can render layer boundaries in the system prompt and surface them in
    /// telemetry. The pinned tier is ALWAYS present (FR-42 invariant) unless no pins
    /// exist for the (tenant, user, matter) tuple.
    /// </returns>
    Task<MemoryComposition> ComposeAsync(
        MemoryCompositionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Composition inputs for <see cref="IMemoryCompositionService.ComposeAsync"/>.
/// </summary>
/// <param name="TenantId">Tenant identifier — Cosmos partition key for both pin
/// retrieval primitives (ADR-014 binding). Must be non-empty.</param>
/// <param name="UserId">Owning user identifier — used to scope the pinned-context
/// retrieval (<see cref="IPinnedContextRepository.GetByUserAsync"/> semantics). Must be
/// non-empty.</param>
/// <param name="MatterId">Optional Dataverse <c>sprk_matter</c> id. When non-null,
/// matter-scoped pins are retrieved via <see cref="IPinnedContextRepository.GetByMatterAsync"/>;
/// when null, only user-scoped pins are retrieved. The selective-recall (retrieved-old)
/// tier is skipped entirely when this is null because
/// <see cref="IPinnedContextRecallService.RecallAsync"/> requires a non-empty matterId.</param>
/// <param name="Conversation">Ordered (oldest-first) conversation history for the
/// current session. Empty or null short-circuits to no recent / compressed layers.</param>
/// <param name="CurrentUserMessage">The user message text that drives the
/// selective-recall similarity ranking (retrieved-old tier). Empty or whitespace-only
/// skips the retrieved-old tier (nothing meaningful to compare against).</param>
public sealed record MemoryCompositionRequest(
    string TenantId,
    string UserId,
    string? MatterId,
    IReadOnlyList<ChatMessage> Conversation,
    string CurrentUserMessage);

/// <summary>
/// Composition output — the tagged four-layer memory block.
/// </summary>
/// <param name="RecentVerbatim">Last <em>N</em> messages of the conversation, verbatim.
/// Empty when conversation is empty or composition has run the recent-verbatim layer
/// dry under budget enforcement.</param>
/// <param name="CompressedMid">Optional single System-role <see cref="ChatMessage"/>
/// summarising the mid-distance window. <c>null</c> when the conversation is shorter
/// than the recent-verbatim cut-off, when compression returns null, or when the
/// compressed-mid layer was dropped by budget enforcement.</param>
/// <param name="RetrievedOld">Top-K pinned items surfaced by similarity to the current
/// user message. Empty when the conversation is shorter than the mid-window end, when
/// matterId is null, when recall returns empty, or when the retrieved-old layer was
/// dropped by budget enforcement.</param>
/// <param name="Pinned">ALL pinned items for the (tenant, user, matter) tuple,
/// grouped by <see cref="PinType"/>. NEVER dropped by budget enforcement (FR-42).
/// Empty only when no pins exist.</param>
/// <param name="EstimatedTokenCount">Aggregate token estimate of all present layers
/// (used by task 068 for budget arithmetic).</param>
/// <param name="DroppedLayers">Names of layers dropped by budget enforcement. Empty
/// when no drop occurred; ordered by drop priority. Telemetry-only.</param>
public sealed record MemoryComposition(
    IReadOnlyList<ChatMessage> RecentVerbatim,
    ChatMessage? CompressedMid,
    IReadOnlyList<PinnedContextItem> RetrievedOld,
    IReadOnlyDictionary<PinType, IReadOnlyList<PinnedContextItem>> Pinned,
    int EstimatedTokenCount,
    IReadOnlyList<string> DroppedLayers)
{
    /// <summary>
    /// Canonical "no composition" sentinel. Returned by
    /// <see cref="IMemoryCompositionService.ComposeAsync"/> when the kill switch is off
    /// or when there is nothing to compose. The caller treats this as
    /// "skip hierarchical memory — proceed with raw window only".
    /// </summary>
    public static MemoryComposition Empty { get; } = new(
        RecentVerbatim: Array.Empty<ChatMessage>(),
        CompressedMid: null,
        RetrievedOld: Array.Empty<PinnedContextItem>(),
        Pinned: new Dictionary<PinType, IReadOnlyList<PinnedContextItem>>(),
        EstimatedTokenCount: 0,
        DroppedLayers: Array.Empty<string>());

    /// <summary>
    /// True iff every layer is empty / null. Convenience for caller short-circuits in
    /// telemetry + prompt-assembly.
    /// </summary>
    public bool IsEmpty =>
        RecentVerbatim.Count == 0 &&
        CompressedMid is null &&
        RetrievedOld.Count == 0 &&
        Pinned.Count == 0;
}
