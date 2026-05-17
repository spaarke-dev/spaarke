using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// R2 Phase 1 stub implementation of <see cref="ISprkAgent"/> that routes AI requests
/// directly to Azure OpenAI (FR-702).
///
/// This class satisfies the provider-agnostic <see cref="ISprkAgent"/> contract and
/// establishes the DI registration and constructor shape for the full implementation.
///
/// Phase 1 (AIPU2-008): Stub only — <see cref="ProcessAsync"/> throws
/// <see cref="NotImplementedException"/>. This ensures the interface, DI registration,
/// and build pipeline are all validated before the LLM integration work begins.
///
/// Phase 2 (AIPU2-060+): Full implementation will be added here. The constructor will be
/// extended to accept <c>IChatClient</c> (from <c>Microsoft.Extensions.AI</c>) — the
/// existing Azure OpenAI IChatClient registered in <c>AiModule</c>. The Azure OpenAI SDK
/// types (AzureOpenAIClient, ChatCompletionOptions, etc.) are intentionally absent from
/// the <see cref="ISprkAgent"/> interface signature and must remain confined to this class.
///
/// R3 Successor: When the Foundry Agent Service implementation is introduced, a
/// <c>MultiAgentOrchestrator</c> will replace <c>DirectOpenAiAgent</c> as the registered
/// <see cref="ISprkAgent"/> singleton, fanning out to multiple specialized implementations.
/// </summary>
public sealed class DirectOpenAiAgent : ISprkAgent
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DirectOpenAiAgent> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="DirectOpenAiAgent"/>.
    ///
    /// Phase 1 constructor accepts <c>IConfiguration</c> and <c>ILogger</c> as placeholders.
    /// Phase 2 will extend this to accept <c>IChatClient</c> (keyed or unkeyed) from the DI
    /// container so the agent can issue streaming chat completions to Azure OpenAI.
    /// </summary>
    /// <param name="configuration">
    /// Application configuration. Used in Phase 2 to read Azure OpenAI endpoint, deployment
    /// name, and model options. Currently unused in Phase 1.
    /// </param>
    /// <param name="logger">Logger for diagnostics, token counting, and latency tracing.</param>
    public DirectOpenAiAgent(
        IConfiguration configuration,
        ILogger<DirectOpenAiAgent> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <value>"azure-openai-direct"</value>
    public string ProviderId => "azure-openai-direct";

    /// <inheritdoc />
    /// <value><c>true</c> — Azure OpenAI streaming completions are fully supported.</value>
    public bool SupportsStreaming => true;

    /// <summary>
    /// Phase 1 stub: always throws <see cref="NotImplementedException"/>.
    ///
    /// Full implementation is deferred to Phase 2 (AIPU2-060). When implemented, this method
    /// will:
    ///   1. Map <see cref="AgentRequest.ConversationHistory"/> to <c>ChatMessage</c> list.
    ///   2. Inject the system prompt from the active playbook context.
    ///   3. Resolve and bind AI tools from <see cref="AgentRequest.RequestedCapabilities"/>.
    ///   4. Call <c>IChatClient.CompleteStreamingAsync</c> and yield "token" events per chunk.
    ///   5. Yield a "done" event at stream completion.
    ///   6. Yield "error" then "done" on any exception (ADR-019).
    /// </summary>
    /// <param name="request">The agent request (unused in Phase 1).</param>
    /// <param name="cancellationToken">Cancellation token (unused in Phase 1).</param>
    /// <returns>Never returns — always throws in Phase 1.</returns>
    /// <exception cref="NotImplementedException">
    /// Always thrown in Phase 1. Full implementation arrives in Phase 2 (AIPU2-060).
    /// </exception>
#pragma warning disable CS1998 // Async method lacks 'await' operators — intentional for Phase 1 stub
    public async IAsyncEnumerable<SseEvent> ProcessAsync(
        AgentRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "DirectOpenAiAgent.ProcessAsync called for session={SessionId} but is not yet implemented (Phase 1 stub). " +
            "Full implementation arrives in Phase 2 (AIPU2-060).",
            request.SessionId);

        throw new NotImplementedException(
            "DirectOpenAiAgent.ProcessAsync will be implemented in Phase 2 (AIPU2-060). " +
            "This is the Phase 1 stub registered by AIPU2-008.");

#pragma warning disable CS0162 // Unreachable code — required to satisfy IAsyncEnumerable return type
        yield break;
#pragma warning restore CS0162
    }
#pragma warning restore CS1998
}
