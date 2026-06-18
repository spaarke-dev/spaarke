namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// R6 Pillar 7 (task 068, D-C-22 / FR-46) — shared per-turn token budget tracker that
/// centralises the NFR-10 8K system-prompt budget across the four chat prompt-assembly
/// subsystems: factory system-prompt blocks, document context, knowledge retrieval, and
/// hierarchical memory composition.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime</b>: Scoped (per HTTP request / per chat turn). Each turn gets a fresh
/// tracker so accounting reflects only the current turn. Singleton lifetime would leak
/// budget across requests and is structurally wrong.
/// </para>
/// <para>
/// <b>Layer names</b>: subsystems identify themselves with stable enum-like layer tags
/// so truncation telemetry carries deterministic identifiers only (ADR-015 BINDING):
/// <list type="bullet">
///   <item><c>persona</c> — persona / system prompt opening (factory + provider)</item>
///   <item><c>knowledge-inline</c> — playbook inline knowledge fragments</item>
///   <item><c>skill-instructions</c> — composed skill prompt fragments</item>
///   <item><c>entity-enrichment</c> — host-context entity metadata block</item>
///   <item><c>active-capabilities</c> — scope-contributed slash commands block</item>
///   <item><c>session-files-manifest</c> — uploaded files manifest (fileId + name only)</item>
///   <item><c>workspace-state</c> — per-turn workspace tab snapshot (task 053)</item>
///   <item><c>memory-composition</c> — hierarchical 4-layer memory block (task 067)</item>
///   <item><c>matter-memory</c> — cross-session structured matter facts</item>
///   <item><c>compact-formatting-directive</c> — Fluent markdown style directive</item>
///   <item><c>dedup-directive</c> — CapabilityRouter dedup directive (task 042)</item>
///   <item><c>chat-ack-directive</c> — chat-ack directive (B-G9b hotfix)</item>
/// </list>
/// </para>
/// <para>
/// <b>Usage pattern</b>: subsystems call <see cref="TryReserve"/> BEFORE appending their
/// fragment. <see cref="TryReserve"/> returns the granted allocation. When the requested
/// allocation cannot fit, the granted result is <c>false</c> and the subsystem MUST omit
/// the fragment (telemetry is emitted by the tracker; the caller logs a soft-fail warning).
/// Subsystems that want all-or-nothing budget enforcement use <see cref="TryReserve"/>;
/// subsystems that want best-effort partial-fragment behaviour estimate locally and use
/// <see cref="Remaining"/> as a soft guide before composing.
/// </para>
/// <para>
/// <b>ADR-015 BINDING</b>: truncation telemetry payloads contain deterministic identifiers
/// only — layer tag, requested tokens, granted tokens, sessionId, tenantId, decision
/// (granted / truncated / over-budget). NEVER user-message text, fragment bodies, retrieved
/// chunk text, or LLM-response text.
/// </para>
/// <para>
/// <b>NFR-10 invariant</b>: <see cref="TotalBudget"/> is the binding 8K ceiling per the
/// spec. Implementations clamp at construction; misconfiguration must degrade gracefully
/// (composition is on the chat hot path).
/// </para>
/// <para>
/// <b>ADR-010</b>: registered as scoped inside an existing DI module (
/// <see cref="Sprk.Bff.Api.Infrastructure.DI.AnalysisServicesModule"/>). ZERO new
/// Program.cs lines.
/// </para>
/// </remarks>
public interface IPromptBudgetTracker
{
    /// <summary>
    /// The binding NFR-10 system-prompt token budget (default 8K). Clamped at
    /// construction by the implementation.
    /// </summary>
    int TotalBudget { get; }

    /// <summary>
    /// The current sum of all granted reservations across all layers. Strictly
    /// monotonically non-decreasing within a single tracker instance (per-turn scope).
    /// </summary>
    int UsedBudget { get; }

    /// <summary>
    /// Remaining budget headroom. Equals <see cref="TotalBudget"/> minus
    /// <see cref="UsedBudget"/>; never negative (the implementation clamps at zero
    /// when a layer overshoots).
    /// </summary>
    int Remaining { get; }

    /// <summary>
    /// Attempts to reserve <paramref name="requestedTokens"/> of budget for the
    /// named <paramref name="layer"/>. Returns <c>true</c> on success (the caller may
    /// append its full fragment), <c>false</c> on truncation/over-budget (the caller
    /// MUST omit the fragment to preserve the NFR-10 invariant).
    /// </summary>
    /// <param name="layer">
    /// Stable enum-like short identifier of the requesting subsystem (see layer-name
    /// list above). ADR-015 BINDING: this string is config-shape, never user content.
    /// </param>
    /// <param name="requestedTokens">
    /// Caller's estimate of the token cost of its fragment. Must be &gt; 0; calls with
    /// non-positive values return <c>true</c> with zero usage (no-op).
    /// </param>
    /// <param name="sessionId">
    /// Optional chat session GUID for telemetry correlation (may be null outside chat
    /// or on cold paths).
    /// </param>
    /// <param name="tenantId">
    /// Opaque tenant identifier (ADR-014 cache-key partition; ADR-015 deterministic ID).
    /// </param>
    /// <returns>
    /// <c>true</c> if the reservation was granted and the caller may append its
    /// fragment; <c>false</c> if granting the reservation would exceed
    /// <see cref="TotalBudget"/> — caller MUST omit the fragment. Either way, a
    /// truncation telemetry event is emitted on the <c>false</c> path.
    /// </returns>
    bool TryReserve(string layer, int requestedTokens, System.Guid? sessionId, string? tenantId);
}
