using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Insights.Routing;

/// <summary>
/// Runtime routing for universal-ingest@v1's Layer 1 (classify) + Layer 2 (extract)
/// nodes. Resolves the per-(practice-area) variant for Layer 1 and the
/// per-(practice-area, document-type) variant for Layer 2 by consulting Dataverse:
/// <list type="bullet">
///   <item>L1 — <c>sprk_analysisaction</c> alternate-key lookup of
///   <c>INS-L1C-&lt;AREA&gt;@v1</c>; fallback to generic <c>INS-L1C@v1</c>.</item>
///   <item>L2 — <c>sprk_practicearea_documenttype</c> N:N matrix lookup by
///   <c>(practiceAreaCode, documentTypeCode)</c>; reads
///   <c>sprk_layer2actioncode</c>. NULL ⇒ structured gate-fail (Layer 1 only).
///   Non-NULL ⇒ resolve that action via <see cref="IScopeResolverService"/>.
///   No row ⇒ fallback to generic <c>INS-L2X@v1</c>.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Design references</b>:
/// <list type="bullet">
///   <item>design-a3-2d-taxonomy.md §2.5 — runtime routing flow</item>
///   <item>design-a5-universal-ingest-jps.md §6 — parameterSchema (practiceAreaHint, documentTypeHint)</item>
///   <item>universal-ingest.playbook.json — 6-node coalescence</item>
/// </list>
/// </para>
/// <para>
/// <b>Zone A placement</b>: lives under <c>Services/Ai/Insights/Routing/</c>;
/// freely imports <see cref="IScopeResolverService"/> for action lookup. Plugged
/// into <c>PlaybookOrchestrationService.ExecuteNodeAsync</c> between action
/// resolution and node-context creation, parallel to <c>ApplyConfigJsonTemplates</c>.
/// </para>
/// <para>
/// <b>Caching</b> — per-request in-memory cache for area→action and (area,type)→action
/// resolutions. Reference data changes rarely; a fresh cache per playbook run is
/// sufficient. Cross-request caching can be added (per-router instance with TTL)
/// if production observability shows hot-path Dataverse traffic; the interface
/// is stable across that change.
/// </para>
/// <para>
/// <b>Fallback invariant</b> — universal-ingest's "every matter classifies" promise
/// requires that an unrecognized area or (area, type) pair degrades gracefully to
/// the generic action row. The router NEVER throws on a missing per-area variant;
/// it logs a warning and returns the generic fallback. The ONLY routing outcome
/// that intentionally fails Layer 2 is the structured NULL gate-fail.
/// </para>
/// </remarks>
public interface IInsightsActionRouter
{
    /// <summary>
    /// Resolve the Layer 1 classify action for the matter's practice area.
    /// Returns the per-area action when one exists (e.g., <c>INS-L1C-CTRNS@v1</c>);
    /// otherwise returns <paramref name="defaultAction"/> (the generic <c>INS-L1C@v1</c>
    /// already resolved by the orchestrator).
    /// </summary>
    /// <param name="practiceAreaCode">
    /// Practice area code from <c>parameters.practiceAreaHint</c> (e.g., "CTRNS").
    /// When null/empty, this returns <paramref name="defaultAction"/> immediately —
    /// no Dataverse round-trip.
    /// </param>
    /// <param name="defaultAction">
    /// The action the orchestrator already resolved from the node's <c>sprk_actionid</c>
    /// FK (typically generic <c>INS-L1C@v1</c>). Returned on every fallback path so
    /// callers can swap <c>action</c> unconditionally without null-checking.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The per-area action, or <paramref name="defaultAction"/> on fallback.</returns>
    Task<AnalysisAction> ResolveLayer1ActionAsync(
        string? practiceAreaCode,
        AnalysisAction defaultAction,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolve the Layer 2 extract action for the (practice area, document type) pair.
    /// </summary>
    /// <param name="practiceAreaCode">
    /// Practice area code from <c>parameters.practiceAreaHint</c> (e.g., "CTRNS").
    /// When null/empty, this returns <see cref="InsightsLayer2RoutingResult.PassThrough"/>
    /// — the orchestrator runs the generic Layer 2 action unchanged.
    /// </param>
    /// <param name="documentTypeCode">
    /// Document type code derived from Layer 1's classification output (e.g.,
    /// <c>CTRNS_CLOSING_STATEMENT</c>). When null/empty, returns
    /// <see cref="InsightsLayer2RoutingResult.PassThrough"/>.
    /// </param>
    /// <param name="defaultAction">
    /// The action the orchestrator already resolved from the node's <c>sprk_actionid</c>
    /// FK (typically generic <c>INS-L2X@v1</c>). Returned on PassThrough / fallback.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="InsightsLayer2RoutingResult"/> describing the outcome:
    /// <list type="bullet">
    ///   <item><see cref="InsightsLayer2RoutingDecision.UsePerPairAction"/> —
    ///   matrix row found with non-NULL <c>sprk_layer2actioncode</c>; the per-pair
    ///   action is resolved and returned in <c>Action</c>.</item>
    ///   <item><see cref="InsightsLayer2RoutingDecision.GateFailNullActionCode"/> —
    ///   matrix row found but <c>sprk_layer2actioncode IS NULL</c> (e.g., CTRNS × NDA
    ///   by design). Caller MUST skip the Layer 2 LLM call and emit a Layer-1-only
    ///   Observation. <c>Action</c> is the default action (unused).</item>
    ///   <item><see cref="InsightsLayer2RoutingDecision.FallbackToGeneric"/> —
    ///   no matrix row exists for this (area, type) pair (unmapped). Run the generic
    ///   <c>INS-L2X@v1</c> action (returned in <c>Action</c>).</item>
    ///   <item><see cref="InsightsLayer2RoutingDecision.PassThrough"/> —
    ///   inputs were null/empty; orchestrator behaves exactly as it does today.</item>
    /// </list>
    /// </returns>
    Task<InsightsLayer2RoutingResult> ResolveLayer2ActionAsync(
        string? practiceAreaCode,
        string? documentTypeCode,
        AnalysisAction defaultAction,
        CancellationToken cancellationToken);
}

/// <summary>
/// Routing decision categories for Layer 2 dispatch. See
/// <see cref="InsightsLayer2RoutingResult"/> for usage.
/// </summary>
public enum InsightsLayer2RoutingDecision
{
    /// <summary>Inputs missing or empty; orchestrator runs the default action.</summary>
    PassThrough = 0,

    /// <summary>Per-pair Layer 2 action row exists and was resolved.</summary>
    UsePerPairAction = 1,

    /// <summary>
    /// Matrix row exists but <c>sprk_layer2actioncode IS NULL</c> — structured
    /// gate-fail per design-a3 §2.5 step 4. Caller must skip Layer 2 LLM.
    /// </summary>
    GateFailNullActionCode = 2,

    /// <summary>
    /// No matrix row for (area, type); orchestrator falls back to generic
    /// <c>INS-L2X@v1</c> per design-a3 §2.5 step 5.
    /// </summary>
    FallbackToGeneric = 3
}

/// <summary>
/// Outcome of a Layer 2 routing decision. Carries the resolved action plus the
/// decision category so the orchestrator can branch (skip vs run, with which prompt).
/// </summary>
/// <param name="Decision">The routing decision category.</param>
/// <param name="Action">
/// The action the orchestrator should bind to the node. For
/// <see cref="InsightsLayer2RoutingDecision.GateFailNullActionCode"/>, this is the
/// default action (unused — the node is skipped).
/// </param>
/// <param name="MatrixRowId">
/// The <c>sprk_practicearea_documenttypeid</c> Guid of the matrix row consulted,
/// when found. Carried for observability/logging.
/// </param>
/// <param name="ResolvedActionCode">
/// The action code used for this routing decision (e.g., <c>INS-L2X-CTRNS-CLOSING@v1</c>),
/// when applicable. Null on PassThrough or GateFailNullActionCode.
/// </param>
public sealed record InsightsLayer2RoutingResult(
    InsightsLayer2RoutingDecision Decision,
    AnalysisAction Action,
    Guid? MatrixRowId = null,
    string? ResolvedActionCode = null)
{
    /// <summary>
    /// Build a PassThrough result wrapping the default action — no routing applied.
    /// </summary>
    public static InsightsLayer2RoutingResult PassThrough(AnalysisAction defaultAction)
        => new(InsightsLayer2RoutingDecision.PassThrough, defaultAction);
}
