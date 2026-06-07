namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Shared base for tool invocation contexts.
/// Captures the fields that are common to BOTH playbook-driven and chat-driven invocation paths:
/// correlation identity, tenant isolation, LLM parameters, and creation timestamp.
/// </summary>
/// <remarks>
/// <para>
/// Introduced by R6 Pillar 2 task D-A-09 (FR-09). Two derived types exist:
/// </para>
/// <list type="bullet">
/// <item><see cref="ToolExecutionContext"/> — playbook-node invocation (existing, retains all playbook fields).</item>
/// <item><see cref="ChatInvocationContext"/> — chat-driven invocation (new in R6 Pillar 2).</item>
/// </list>
/// <para>
/// Handlers receive the appropriate derived type. The existing 4 handlers (GenericAnalysisHandler,
/// DocumentClassifierHandler, SummaryHandler, SemanticSearchToolHandler) continue to receive
/// <see cref="ToolExecutionContext"/> unchanged. The chat-driven path (task 010 adapter) uses
/// <see cref="ChatInvocationContext"/>.
/// </para>
/// <para>
/// Field names are preserved on the derived <see cref="ToolExecutionContext"/> so source compatibility
/// is unaffected — handlers that previously read <c>context.TenantId</c>, <c>context.MaxTokens</c>,
/// etc. continue to compile and behave identically.
/// </para>
/// <para>
/// Per ADR-013, this type is AI-internal — not exposed through the
/// <c>Services/Ai/PublicContracts/</c> facade.
/// </para>
/// </remarks>
public abstract record ToolInvocationContextBase
{
    /// <summary>
    /// Unique correlation identifier for this invocation.
    /// For playbook invocations this is the AnalysisId.
    /// For chat invocations this is a per-tool-call decision id.
    /// Used for log/trace correlation and caching.
    /// </summary>
    /// <remarks>
    /// Not marked <c>required</c> on the base; derived types expose strongly-named
    /// required correlation fields (<see cref="ToolExecutionContext.AnalysisId"/>,
    /// <see cref="ChatInvocationContext.DecisionId"/>) that delegate to this storage.
    /// This pattern preserves source compatibility with existing handlers that read
    /// <c>context.AnalysisId</c> while still requiring callers to set a correlation id.
    /// </remarks>
    public Guid InvocationId { get; init; }

    /// <summary>
    /// Tenant identifier for multi-tenant isolation.
    /// All operations must be scoped to this tenant.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// User-provided context or instructions for this invocation.
    /// In playbook flow this is the analysis-session UserContext; in chat flow this is
    /// the chat-session-level user context (NOT the raw user message body — see ADR-015).
    /// </summary>
    public string? UserContext { get; init; }

    /// <summary>
    /// Maximum tokens to use for AI model calls.
    /// Handlers should respect this limit.
    /// </summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Temperature setting for AI model calls (0.0 - 1.0).
    /// Lower values are more deterministic.
    /// </summary>
    public double Temperature { get; init; } = 0.3;

    /// <summary>
    /// AI model deployment ID override for this invocation.
    /// If null, uses the default model deployment.
    /// </summary>
    public Guid? ModelDeploymentId { get; init; }

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Timestamp when this invocation context was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
