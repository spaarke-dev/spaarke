namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Provider-agnostic contract for all Spaarke AI agent implementations.
///
/// This interface defines the boundary between the chat orchestration layer and the
/// underlying AI provider. The orchestration layer (ChatOrchestrationService, endpoints)
/// depends only on this interface and must never reference Azure OpenAI SDK types,
/// Foundry Agent Service SDK types, or any other provider-specific types directly.
///
/// FR-701: The system must be able to route AI requests to different agent implementations
/// (Azure OpenAI direct, Foundry Agent Service, future providers) without the chat
/// orchestration layer knowing which provider is in use.
///
/// Implementations in R2:
///   - <see cref="DirectOpenAiAgent"/> — calls Azure OpenAI directly (FR-702, dev environment)
///
/// Planned implementations in R3:
///   - <c>FoundryAgent</c>         — routes through Azure AI Foundry Agent Service
///   - <c>MultiAgentOrchestrator</c> — fan-out to multiple specialized agents
///
/// Registration: <c>services.AddSingleton&lt;ISprkAgent, DirectOpenAiAgent&gt;()</c>
/// in <c>AiChatModule</c> (AIPU2-008).
/// </summary>
public interface ISprkAgent
{
    /// <summary>
    /// Identifies the agent implementation. Used for logging, telemetry, and routing decisions.
    ///
    /// Well-known values:
    ///   - "azure-openai-direct"  — <see cref="DirectOpenAiAgent"/> (R2 Phase 1)
    ///   - "foundry"              — Foundry Agent Service implementation (R3)
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Whether this agent implementation supports token-level streaming via
    /// <see cref="IAsyncEnumerable{T}"/>.
    ///
    /// When <c>false</c>, callers must accumulate all events before presenting the
    /// response to the client. When <c>true</c>, callers should forward events to
    /// the SSE stream as they arrive for real-time streaming.
    ///
    /// All production implementations in R2 return <c>true</c>.
    /// </summary>
    bool SupportsStreaming { get; }

    /// <summary>
    /// Processes a user message and produces a stream of SSE events representing the
    /// agent's response.
    ///
    /// Each yielded <see cref="SseEvent"/> represents one unit of agent output. Event types
    /// follow the R2 SSE event contract:
    ///   - "token"              — an incremental text token (streaming text response)
    ///   - "done"               — signals end of response (no payload)
    ///   - "error"              — signals a non-recoverable agent error
    ///   - R2 event types       — workspace_widget, context_update, etc. (FR-801)
    ///
    /// The method MUST yield a "done" event as the last item in the sequence on success.
    /// The method MUST yield an "error" event followed by "done" on failure (ADR-019).
    ///
    /// The caller is responsible for cancellation: when <paramref name="cancellationToken"/>
    /// is triggered, the implementation should stop yielding as soon as possible. Partial
    /// responses are acceptable; the client handles reconnection.
    ///
    /// Implementations MUST NOT throw exceptions out of this method — all errors must be
    /// communicated via "error" SSE events so the SSE stream terminates cleanly.
    /// </summary>
    /// <param name="request">
    /// The agent request containing the user message, conversation history, session context,
    /// and optional capability hints. See <see cref="AgentRequest"/> for field semantics.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token. Implementations should respect cancellation and stop yielding
    /// events promptly when triggered.
    /// </param>
    /// <returns>
    /// An async enumerable of <see cref="SseEvent"/> instances. The sequence always ends
    /// with a "done" event (or "error" then "done" on failure).
    /// </returns>
    IAsyncEnumerable<SseEvent> ProcessAsync(
        AgentRequest request,
        CancellationToken cancellationToken);
}
