namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Per-request context that flows through Linear AI Consumer primitives.
/// Constructed once per request; passed to every primitive on the pipeline.
/// </summary>
/// <remarks>
/// <para>
/// R7 Wave 12 (2026-07-02) — the two-path architecture split: linear AI
/// consumers (Doc Upload, File Summarize, Prefills, Doc Create Profile) run
/// through this record + the LinearConsumers primitives rather than through
/// <see cref="IPlaybookOrchestrationService"/>. See
/// <c>docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md</c>.
/// </para>
/// <para>
/// Deliberately minimal: only carries values needed by primitives (LLM +
/// Dataverse writes + job enqueue). Consumer services may hold richer
/// per-consumer state as instance fields; this record is the shared surface.
/// </para>
/// </remarks>
public sealed record LinearRunContext
{
    /// <summary>
    /// Stable consumer-type key (e.g., <c>document-profile</c>,
    /// <c>summarize-file</c>). Used by <see cref="IActionResolver"/> to look up
    /// the Action row + by observability code for tagging.
    /// </summary>
    public required string ConsumerType { get; init; }

    /// <summary>
    /// Optional correlation id — the endpoint's trace identifier passed through
    /// so logs stitch cleanly. Null when no HTTP context is available (e.g.,
    /// background invocation).
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Tenant identifier resolved from the caller's JWT (<c>tid</c> claim). Used
    /// for tenant-scoped cache keys + telemetry. Null for background/system
    /// runs.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// ai-advanced-capabilities-agreements-r1 task 021 (FR-08 pack binding): an optional scope for
    /// <see cref="ActionRunner.RetrieveReferenceGroundingAsync"/>'s reference-knowledge search
    /// (<c>spaarke-rag-references</c> knowledge-source ids, e.g. <c>["KNW-011"]</c> for the NDA
    /// standard). Null (the default) preserves the pre-021 behavior EXACTLY — an unscoped, whole-corpus
    /// search (every existing caller omits this property). Populated only by
    /// <see cref="Sprk.Bff.Api.Services.Ai.Chat.SessionDispatchOrchestrator"/> when the dispatch args
    /// carry a <c>subDomain</c> resolved against the <c>sprk_agreementtype</c> registry's
    /// <c>sprk_knowledgepackref</c> column — so a classified review run is grounded on the matched
    /// type's own pack instead of being crowded out by the whole reference corpus (the gap task 003
    /// flagged: <c>ActionRunner</c> never set <c>KnowledgeSourceIds</c> before this).
    /// </summary>
    public IReadOnlyList<string>? KnowledgeSourceIds { get; init; }
}
