using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Services.Ai; // DocumentContext (task 095 widening)

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade for the generic <c>invoke_playbook</c> capability (R6 Pillar 3, Q11).
/// Wraps <see cref="IPlaybookOrchestrationService"/> so CRUD-side callers — the chat-tool
/// dispatch path (task 021), the M365 Copilot agent gateway, and future R7+ consumers —
/// never inject the orchestration service or any other AI-internal type directly.
/// </summary>
/// <remarks>
/// <para>
/// Per refined ADR-013 (2026-05-20) + project CLAUDE.md §"MUST NOT" rule: external CRUD
/// code MUST NOT inject <see cref="IPlaybookOrchestrationService"/>,
/// <see cref="IPlaybookExecutionEngine"/>, <see cref="IOpenAiClient"/>, or any other
/// AI-internal type. This facade is the canonical bridge — the implementation lives in
/// Zone A and is free to use AI internals; CRUD-side callers see only the domain-shape
/// <see cref="PlaybookInvocationResult"/>.
/// </para>
/// <para>
/// <b>Non-streaming surface intent</b>: this facade returns a single typed
/// <see cref="PlaybookInvocationResult"/> rather than the SSE event stream that
/// <see cref="IWorkspacePrefillAi.ExecutePlaybookAsync"/> exposes. The intended consumers
/// (LLM function-call dispatch, M365 Copilot adapter) need a single tool-call response,
/// not progressive UX updates. Internally, the implementation consumes the underlying
/// SSE stream and aggregates terminal node outputs + citation metadata into the
/// domain-shape result.
/// </para>
/// <para>
/// <b>Naming convention</b>: <see cref="InvokePlaybookAsync"/> reflects the playbook-by-id
/// invocation semantic the consumer expresses (task 021 <c>invoke_playbook</c> tool). The
/// method name intentionally does NOT mention orchestration, nodes, or execution graphs —
/// those are AI-internal implementation details behind the boundary.
/// </para>
/// <para>
/// <b>Mirrors the canonical facade pattern</b> from ADR-007 (<see cref="SpeFileStore"/>),
/// <see cref="IBriefingAi"/>, and <see cref="IInsightsAi"/>: narrow surface (only what
/// real consumers call today), SDAP-domain DTOs (no <c>PlaybookStreamEvent</c>,
/// <c>NodeOutput</c>, or <c>ChatMessage</c> leaked to callers), single concrete
/// implementation behind the interface.
/// </para>
/// <para>
/// <b>Phase 1 consumer (R6 Pillar 3)</b>:
/// <list type="bullet">
///   <item>Task 021 — <c>InvokePlaybookHandler</c> (chat-side <see cref="IToolHandler"/>
///   implementation) translates the LLM's <c>invoke_playbook(playbookId, parameters)</c>
///   tool call into a single call to <see cref="InvokePlaybookAsync"/>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Phase 2 consumer (spaarkeai-compose-r1 task 095 — document-context widening)</b>:
/// The Compose dispatch path (<c>POST /api/compose/action/{consumerType}</c>) invokes
/// document-scoped playbooks (Summarize / Rewrite / Find Similar / etc.) that require
/// the extracted plain text of the source DOCX. This widens the facade with the
/// optional <c>userContext</c> + <c>document</c> parameters that forward to
/// <see cref="PlaybookRunRequest.UserContext"/> and <see cref="PlaybookRunRequest.Document"/>.
/// The M365 Copilot Agent path (Phase 1) continues to work unchanged because Copilot
/// supplies document context via conversation attachments OUTSIDE the facade.
/// Formalized as the Path B ADR-013 amendment (per CLAUDE.md §6.5) — see task 102.
/// </para>
/// </remarks>
public interface IInvokePlaybookAi
{
    /// <summary>
    /// Invoke a playbook by ID with caller-supplied parameters and (optionally) pre-loaded
    /// document context, returning a single aggregated result. Internally executes the
    /// playbook through <see cref="IPlaybookOrchestrationService.ExecuteAsync"/>, consumes
    /// the SSE event stream, and projects terminal node outputs + citation metadata into
    /// the domain-shape <see cref="PlaybookInvocationResult"/>.
    /// </summary>
    /// <param name="playbookId">Identifier of the playbook to invoke. Required.</param>
    /// <param name="parameters">Caller-supplied parameter map for template substitution in
    /// node prompts. Keys + values are expected to be deterministic identifiers / display
    /// values — ADR-015 binding: callers MUST NOT pass raw user message content here.
    /// May be null when the playbook has no parameters.</param>
    /// <param name="context">Invocation context carrying tenant id, correlation id, and
    /// HTTP context for OBO authentication. See <see cref="PlaybookInvocationContext"/>
    /// for field semantics. Required.</param>
    /// <param name="userContext">
    /// Optional user-context string forwarded to <see cref="PlaybookRunRequest.UserContext"/>.
    /// Consumers use this to ship free-form guidance to the playbook (e.g. the user's
    /// selection text in a Compose "Summarize selection" flow). Null when no user context
    /// applies. **Widened surface, spaarkeai-compose-r1 task 095 — see ADR-013 amendment 2026-07-01.**
    /// </param>
    /// <param name="document">
    /// Optional pre-loaded document context forwarded to <see cref="PlaybookRunRequest.Document"/>.
    /// When provided, the orchestration layer sets it on <c>PlaybookRunContext.Document</c>
    /// so every downstream node shares the same extracted text without re-downloading from SPE.
    /// The Compose consumer builds this via <c>IDocxTextExtractor</c> (task 094). Null when
    /// the playbook is not document-scoped (e.g. the M365 Copilot parameter-only path).
    /// **Widened surface, spaarkeai-compose-r1 task 095 — see ADR-013 amendment 2026-07-01.**
    /// </param>
    /// <param name="cancellationToken">Cancellation token. Pair with a timeout suited to
    /// the playbook — chat-tool consumers typically cap at the chat-turn budget; M365
    /// Copilot consumers cap at the agent-turn budget. Internal default is no timeout
    /// (consumer-owned).</param>
    /// <returns>The aggregated <see cref="PlaybookInvocationResult"/>. Always non-null;
    /// success/failure is communicated via <see cref="PlaybookInvocationResult.Success"/>.</returns>
    /// <exception cref="ArgumentException">When <paramref name="playbookId"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">When <paramref name="context"/> is null.</exception>
    /// <exception cref="Configuration.FeatureDisabledException">When the AI kill-switch is OFF
    /// (propagated unchanged from <c>NullInvokePlaybookAi</c> per ADR-032 P3). The chat-tool
    /// adapter converts to a <c>ToolResult.Error</c>; the M365 Copilot adapter converts to
    /// 503 ProblemDetails.</exception>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/>
    /// is signalled.</exception>
    /// <remarks>
    /// <para>
    /// <b>Backward compatibility</b>: the existing 4-arg call shape
    /// <c>InvokePlaybookAsync(playbookId, parameters, context, cancellationToken)</c> continues
    /// to compile unchanged — the C# compiler fills null defaults for the new
    /// <paramref name="userContext"/> + <paramref name="document"/> parameters. Phase 1
    /// consumers (chat-tool adapter, M365 Copilot gateway) require no code changes.
    /// </para>
    /// </remarks>
    Task<PlaybookInvocationResult> InvokePlaybookAsync(
        Guid playbookId,
        IReadOnlyDictionary<string, string>? parameters,
        PlaybookInvocationContext context,
        CancellationToken cancellationToken = default,
        string? userContext = null,
        DocumentContext? document = null);
}

/// <summary>
/// Caller-supplied invocation context for <see cref="IInvokePlaybookAi.InvokePlaybookAsync"/>.
/// Carries identity / correlation information + the HTTP context required for OBO auth.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-015 binding</b>: every field is a deterministic identifier or an ASP.NET request
/// primitive. This DTO MUST NOT be extended to carry raw user message content, prompt text,
/// or any payload that crosses the data-governance boundary. Telemetry from the facade
/// emits <c>playbookId</c> + decision + timestamp only.
/// </para>
/// <para>
/// <b>Why HttpContext is on this DTO</b>: the underlying
/// <see cref="IPlaybookOrchestrationService.ExecuteAsync"/> requires
/// <see cref="HttpContext"/> for OBO token exchange in the playbook node executors that
/// hit Graph / Dataverse. <see cref="HttpContext"/> is an ASP.NET primitive (not an
/// AI-internal type per ADR-013), so it is acceptable on the facade surface — the existing
/// <see cref="IWorkspacePrefillAi.ExecutePlaybookAsync"/> facade applies the same pattern.
/// </para>
/// </remarks>
public sealed record PlaybookInvocationContext
{
    /// <summary>
    /// Tenant identifier for multi-tenant isolation. Deterministic id only (typically
    /// the Azure AD tenant GUID). Required.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// HTTP context for OBO authentication inside the orchestration path. Required —
    /// playbook node executors invoke Graph / Dataverse on behalf of the caller.
    /// </summary>
    /// <remarks>
    /// ASP.NET primitive (not an AI-internal type per ADR-013) — same pattern as
    /// <see cref="IWorkspacePrefillAi.ExecutePlaybookAsync"/>.
    /// </remarks>
    public required HttpContext HttpContext { get; init; }

    /// <summary>
    /// Optional correlation id for cross-cutting trace stitching. When null, the facade
    /// implementation MAY emit a deterministic id derived from the run id; consumers
    /// SHOULD propagate <see cref="System.Diagnostics.Activity.Current"/>'s trace id
    /// when one is available.
    /// </summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Aggregated result returned from <see cref="IInvokePlaybookAi.InvokePlaybookAsync"/>.
/// Domain-shape projection of the playbook run — does NOT leak
/// <see cref="PlaybookStreamEvent"/>, <see cref="NodeOutput"/>,
/// <see cref="PlaybookRunMetrics"/>, or any other AI-internal type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Field semantics for chat-tool consumers (task 021)</b>: the
/// <c>InvokePlaybookHandler</c> projects this result into a <see cref="ToolResult"/> as
/// follows:
/// <list type="bullet">
///   <item><see cref="Success"/> → <see cref="ToolResult.Success"/></item>
///   <item><see cref="TextContent"/> → <see cref="ToolResult.Summary"/></item>
///   <item><see cref="StructuredData"/> → <see cref="ToolResult.Data"/></item>
///   <item><see cref="Citations"/> → <see cref="ToolResult.Metadata"/> under
///   <see cref="ToolResultMetadataKeys.Citations"/> (Wave 7b citation accumulation
///   infrastructure)</item>
///   <item><see cref="Confidence"/> → <see cref="ToolResult.Confidence"/></item>
///   <item><see cref="ErrorMessage"/> → <see cref="ToolResult.ErrorMessage"/></item>
/// </list>
/// </para>
/// </remarks>
public sealed record PlaybookInvocationResult
{
    /// <summary>
    /// Run identifier assigned by the orchestration layer. Deterministic GUID; safe to
    /// log + surface to telemetry per ADR-015.
    /// </summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// Whether the playbook completed successfully (all required nodes succeeded).
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Aggregated plain-text content from the terminal output node(s). Suitable for
    /// rendering as the chat-tool response body. Null when the playbook produced no
    /// text output (e.g., side-effect-only playbook).
    /// </summary>
    public string? TextContent { get; init; }

    /// <summary>
    /// Aggregated structured data from the terminal output node(s), serialized as a
    /// JSON element. Schema varies by playbook. Null when no structured output.
    /// </summary>
    public System.Text.Json.JsonElement? StructuredData { get; init; }

    /// <summary>
    /// Citation envelopes extracted from terminal node outputs / RAG retrievals along
    /// the playbook path. Each envelope carries deterministic identifiers only — ADR-015
    /// binding: NEVER user message content. Wave 7b shape — same envelope the chat-tool
    /// adapter forwards into the per-chat-turn <see cref="CitationContext"/>.
    /// </summary>
    public IReadOnlyList<ToolResultCitation> Citations { get; init; } = Array.Empty<ToolResultCitation>();

    /// <summary>
    /// Overall confidence score (0.0 - 1.0) when the playbook reports one. May be
    /// aggregated from individual node confidences or surfaced from the terminal node.
    /// Null when the playbook does not surface confidence.
    /// </summary>
    public double? Confidence { get; init; }

    /// <summary>
    /// Total wall-clock duration of the playbook run. Useful for telemetry; never
    /// contains user content. Defaults to <see cref="TimeSpan.Zero"/> when not measured.
    /// </summary>
    public TimeSpan Duration { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Error message when <see cref="Success"/> is false. Surfaces the orchestration-layer
    /// failure reason in a form safe to render to chat-tool callers. Null on success.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Stable error code when <see cref="Success"/> is false. Mirrors the orchestration
    /// layer's <see cref="NodeErrorCodes"/> when the failure originated in a node; may be
    /// <c>PLAYBOOK_INVOCATION_FAILED</c> when the failure was at the run level.
    /// </summary>
    public string? ErrorCode { get; init; }
}
