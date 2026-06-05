namespace Sprk.Bff.Api.Models.Ai.PublicContracts;

/// <summary>
/// Single Server-Sent-Events frame emitted from
/// <see cref="Services.Ai.PublicContracts.IInsightsAi.AssistantQueryStreamAsync"/> —
/// the streaming counterpart to <see cref="AssistantQueryFacadeResult"/> (Wave F task 051 /
/// FR-05 v1.1 contract).
/// </summary>
/// <remarks>
/// <para>
/// <b>Shape per Wave F1 spike Section A</b>: a discriminated record carrying one of four
/// chunk types (<c>progress</c>, <c>delta</c>, <c>result</c>, <c>error</c>). Mirrors the R5
/// <c>AnalysisChunk</c> shape for protocol parity. Each chunk maps 1:1 to an SSE frame.
/// </para>
/// <para>
/// <b>Type matrix</b>:
/// <list type="bullet">
///   <item><c>"progress"</c> — pipeline-phase transition. <see cref="Step"/> carries the
///   phase label (e.g., <c>classifier_started</c>, <c>rag_search_complete</c>,
///   <c>node_complete</c>, <c>cache_hit</c>). <see cref="Content"/> optionally carries step
///   detail (e.g., the playbook id or node name). No delta/result fields populated.</item>
///   <item><c>"delta"</c> — LLM token chunk on the RAG synthesis path. <see cref="Path"/>
///   is the field being streamed (always <c>"answer"</c> in v1.1). <see cref="Content"/>
///   carries the token text. <see cref="Sequence"/> is the 1-based delta index for ordering.</item>
///   <item><c>"result"</c> — final v1.0-shape canonical response. <see cref="Result"/> carries
///   the full <see cref="AssistantQueryFacadeResult"/> for client-side state finalization.
///   Always the last non-terminal chunk before <c>[DONE]</c>.</item>
///   <item><c>"error"</c> — mid-stream error. <see cref="Error"/> carries
///   ProblemDetails-shaped envelope. Always followed by <c>[DONE]</c>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Wire format</b>: each chunk is serialized to JSON (camelCase) and written as an SSE
/// frame: <c>event: {Type}\ndata: {json}\n\n</c>. Stream terminates with the literal
/// <c>data: [DONE]\n\n</c> sentinel per R5 §2.2.
/// </para>
/// <para>
/// <b>Zone B-importable DTO</b> per SPEC §3.5 — primitives + the existing facade-result
/// type only. No AI internals leak. Safe for the <c>POST /api/insights/assistant/query</c>
/// SSE handler to project to the wire.
/// </para>
/// <para>
/// <b>Cancellation semantics</b>: <see cref="IAsyncEnumerable{T}"/> honors
/// <see cref="System.Threading.CancellationToken"/> per-await; consumer aborts (client
/// disconnect) terminate the upstream stream cleanly through ASP.NET's
/// <c>HttpContext.RequestAborted</c> propagation.
/// </para>
/// </remarks>
public sealed record AssistantQueryChunk
{
    /// <summary>Chunk discriminator: <c>"progress"</c> | <c>"delta"</c> | <c>"result"</c> |
    /// <c>"error"</c>. Used as the SSE <c>event:</c> field.</summary>
    public required string Type { get; init; }

    /// <summary>For <c>progress</c>: the pipeline-phase label (e.g.,
    /// <c>classifier_started</c>, <c>classifier_complete</c>, <c>rag_search_started</c>,
    /// <c>rag_search_complete</c>, <c>llm_synthesis_started</c>, <c>playbook_started</c>,
    /// <c>node_complete</c>, <c>cache_hit</c>). Null on non-progress chunks.</summary>
    public string? Step { get; init; }

    /// <summary>For <c>delta</c>: JSON path of the field being streamed. Always
    /// <c>"answer"</c> in v1.1. Reserved for future structured-output streaming.</summary>
    public string? Path { get; init; }

    /// <summary>For <c>delta</c>: the token text. For <c>progress</c>: optional step detail
    /// (e.g., the dispatched playbook canonical name, node name, or classifier hit count).
    /// For <c>error</c>: short error message. Null when not applicable.</summary>
    public string? Content { get; init; }

    /// <summary>For <c>delta</c>: 1-based sequence number for token ordering. Null on
    /// non-delta chunks.</summary>
    public int? Sequence { get; init; }

    /// <summary>For <c>result</c>: the canonical v1.0-shaped final response — same shape
    /// the synchronous <see cref="Services.Ai.PublicContracts.IInsightsAi.AssistantQueryAsync"/>
    /// method returns. Null on non-result chunks.</summary>
    public AssistantQueryFacadeResult? Result { get; init; }

    /// <summary>For <c>error</c>: ProblemDetails-shaped envelope carrying the stable
    /// <see cref="AssistantQueryError.ErrorCode"/> + opaque <see cref="AssistantQueryError.Detail"/>.
    /// Null on non-error chunks.</summary>
    public AssistantQueryError? Error { get; init; }
}

/// <summary>
/// Mid-stream error envelope carried in <see cref="AssistantQueryChunk.Error"/> when
/// <see cref="AssistantQueryChunk.Type"/> = <c>"error"</c>. Mirrors the stable
/// <c>errorCode</c> field shape from ADR-019 ProblemDetails.
/// </summary>
/// <param name="ErrorCode">Stable error code (e.g., <c>ai.rag.disabled</c>,
/// <c>INSIGHTS_ASSISTANT_STREAM_ERROR</c>) the Assistant uses to drive UI behavior.</param>
/// <param name="Detail">Opaque detail string — short, non-leaking. Never contains internal
/// stack traces, prompt content, or model output per ADR-019.</param>
public sealed record AssistantQueryError(string ErrorCode, string Detail);
