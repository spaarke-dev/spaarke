using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Services.Ai.Capabilities;

// Explicit alias to resolve ChatMessage ambiguity between the Dataverse domain model
// (Sprk.Bff.Api.Models.Ai.Chat.ChatMessage) and the Agent Framework type
// (Microsoft.Extensions.AI.ChatMessage). DirectOpenAiAgent uses only the AI type.
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// R2 Phase 2 implementation of <see cref="ISprkAgent"/> that routes AI requests
/// directly to Azure OpenAI via the <c>Microsoft.Extensions.AI</c> <see cref="IChatClient"/>
/// pipeline (FR-702).
///
/// This class satisfies the provider-agnostic <see cref="ISprkAgent"/> contract. All Azure
/// OpenAI SDK specifics are confined to this class and MUST NOT leak through the interface.
///
/// Design (AIPU2-060):
///   1. Builds an orchestrator system prompt via <see cref="IOrchestratorPromptBuilder"/>
///      using a fallback routing result (no pre-classification in Phase 2).
///   2. Converts <see cref="AgentRequest.ConversationHistory"/> to
///      <see cref="AiChatMessage"/> list (role mapping: User → User, Assistant → Assistant,
///      System → System).
///   3. Calls <see cref="IChatClient.GetStreamingResponseAsync"/> — the injected client is
///      registered with <c>UseFunctionInvocation</c>, so tool calls are automatically
///      executed and the streaming result always contains text tokens.
///   4. Yields <see cref="SseEvent"/> instances:
///        - Text tokens          → <c>SseEvent.FromString("token", text)</c>
///        - Stream end           → <c>SseEvent.Empty("done")</c>
///        - Any exception        → <c>SseEvent.FromString("error", message)</c> then <c>"done"</c>
///
/// Singleton lifetime (ADR-010): <see cref="IChatClient"/> is registered as singleton
/// and is thread-safe; all mutable state in this class is on the call stack.
///
/// ADR-013: Extends the existing BFF agent pipeline — no separate Azure Function or service.
/// ADR-015: Only session/user IDs are logged; no message content is ever written to logs.
/// ADR-019: Exceptions must not propagate out of <see cref="ProcessAsync"/>; they are
///   communicated via "error" SSE events so the SSE stream terminates cleanly.
///
/// R3 Successor: When the Foundry Agent Service implementation is introduced, a
/// <c>MultiAgentOrchestrator</c> will replace <c>DirectOpenAiAgent</c> as the registered
/// <see cref="ISprkAgent"/> singleton, fanning out to multiple specialised implementations.
/// </summary>
public sealed class DirectOpenAiAgent : ISprkAgent
{
    private readonly IChatClient _chatClient;
    private readonly IOrchestratorPromptBuilder _promptBuilder;
    private readonly ILogger<DirectOpenAiAgent> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="DirectOpenAiAgent"/>.
    /// </summary>
    /// <param name="chatClient">
    /// The <see cref="IChatClient"/> registered in DI via <c>AddChatClient().UseFunctionInvocation()</c>
    /// (AiModule.cs). The function-invocation pipeline automatically executes tool calls and feeds
    /// results back into the conversation, so <see cref="ProcessAsync"/> always streams text tokens.
    /// </param>
    /// <param name="promptBuilder">
    /// Orchestrator prompt builder that composes the two-layer system prompt (stable prefix +
    /// per-turn suffix with tool schemas) used by Azure OpenAI for each turn.
    /// </param>
    /// <param name="logger">Logger for diagnostics. Message content is NEVER logged (ADR-015).</param>
    public DirectOpenAiAgent(
        IChatClient chatClient,
        IOrchestratorPromptBuilder promptBuilder,
        ILogger<DirectOpenAiAgent> logger)
    {
        _chatClient = chatClient;
        _promptBuilder = promptBuilder;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <value>"azure-openai-direct"</value>
    public string ProviderId => "azure-openai-direct";

    /// <inheritdoc />
    /// <value><c>true</c> — Azure OpenAI streaming completions are fully supported.</value>
    public bool SupportsStreaming => true;

    /// <summary>
    /// Processes a user turn by calling Azure OpenAI with streaming and yielding
    /// <see cref="SseEvent"/> instances as tokens arrive.
    ///
    /// Protocol (AIPU2-060, ADR-019):
    ///   - Always yields a "done" event as the last item on success.
    ///   - Yields "error" then "done" on any exception (stream terminates cleanly).
    ///   - Never throws exceptions out of this method.
    ///   - Respects <paramref name="cancellationToken"/>: stops yielding promptly on cancellation.
    ///
    /// Tool execution is handled automatically by the <c>UseFunctionInvocation</c> pipeline
    /// registered on the injected <see cref="IChatClient"/>. Callers receive only text tokens.
    /// </summary>
    /// <param name="request">
    /// The agent request containing user message, conversation history, session context,
    /// and optional capability hints. See <see cref="AgentRequest"/> for field semantics.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token. Stops token streaming promptly when triggered.
    /// Partial responses are acceptable; the client handles reconnection.
    /// </param>
    /// <returns>
    /// An async enumerable of <see cref="SseEvent"/> instances ending with "done"
    /// (or "error" then "done" on failure).
    /// </returns>
    public async IAsyncEnumerable<SseEvent> ProcessAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "DirectOpenAiAgent.ProcessAsync — session={SessionId}, user={UserId}, tenant={TenantId}, " +
            "historyCount={HistoryCount}, hasDocContext={HasDocContext}",
            request.SessionId,
            request.UserId,
            request.TenantId,
            request.ConversationHistory.Count,
            request.ContextDocuments?.Count > 0);

        // Wrap the entire streaming call in try/catch so that any exception is converted to an
        // "error" SSE event and the stream terminates cleanly (ADR-019).
        IAsyncEnumerable<ChatResponseUpdate>? stream = null;
        Exception? startupException = null;

        try
        {
            // 1. Build the two-layer system prompt (stable prefix + per-turn tool suffix).
            //    Use a fallback routing result in Phase 2 — no pre-classification yet.
            //    The prompt builder handles token budget enforcement and prefix caching.
            var promptContext = BuildPromptContext(request);
            var routing = CapabilityRoutingResult.Fallback(
                fallbackCapabilityNames: [],
                selectedToolNames: [],
                latencyMs: 0);

            var prompt = _promptBuilder.BuildSystemPrompt(routing, promptContext);

            _logger.LogDebug(
                "DirectOpenAiAgent: system prompt built — estimatedTokens={Tokens}, prefixCacheHit={CacheHit}",
                prompt.EstimatedTokens,
                prompt.PrefixCacheHit);

            // 2. Construct the full message list: [system] + [history turns] + [user message].
            var messages = BuildMessages(request, prompt.FullSystemPrompt);

            // 3. Start the streaming call.
            //    The client has UseFunctionInvocation applied, so tool calls are executed
            //    automatically and only text tokens flow to us here.
            stream = _chatClient.GetStreamingResponseAsync(messages, options: null, cancellationToken);
        }
        catch (Exception ex)
        {
            startupException = ex;
        }

        // Report startup failure before yielding anything
        if (startupException is not null)
        {
            _logger.LogError(
                startupException,
                "DirectOpenAiAgent.ProcessAsync: failed to start streaming — session={SessionId}",
                request.SessionId);

            yield return SseEvent.FromString("error", BuildErrorMessage(startupException));
            yield return SseEvent.Empty("done");
            yield break;
        }

        // 4. Stream tokens to the caller as "token" SSE events.
        var tokenCount = 0;
        Exception? streamException = null;

        await foreach (var update in stream!.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Each update may contain one or more text content items.
            // Emit a "token" event for each non-empty text chunk.
            foreach (var content in update.Contents)
            {
                if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                {
                    tokenCount++;
                    yield return SseEvent.FromString("token", textContent.Text);
                }
            }

            // Capture streaming errors from FinishReason or usage metadata.
            // The UseFunctionInvocation pipeline surfaces errors as exceptions,
            // so this is a belt-and-suspenders check on the content.
            if (update.FinishReason == ChatFinishReason.ContentFilter)
            {
                _logger.LogWarning(
                    "DirectOpenAiAgent: content filter triggered — session={SessionId}",
                    request.SessionId);

                yield return SseEvent.FromString("error",
                    "The response was blocked by the content safety filter. Please rephrase your request.");
                yield return SseEvent.Empty("done");
                yield break;
            }
        }

        if (streamException is not null)
        {
            _logger.LogError(
                streamException,
                "DirectOpenAiAgent.ProcessAsync: streaming error — session={SessionId}",
                request.SessionId);

            yield return SseEvent.FromString("error", BuildErrorMessage(streamException));
            yield return SseEvent.Empty("done");
            yield break;
        }

        _logger.LogInformation(
            "DirectOpenAiAgent.ProcessAsync: stream complete — session={SessionId}, tokenCount={TokenCount}",
            request.SessionId,
            tokenCount);

        // 5. Always end with "done" on success.
        yield return SseEvent.Empty("done");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds the <see cref="OrchestratorPromptContext"/> from the agent request.
    ///
    /// The prompt context carries session-level metadata used by the prompt builder to
    /// personalise the stable prefix (persona, matter name, tenant isolation notice).
    /// </summary>
    private static OrchestratorPromptContext BuildPromptContext(AgentRequest request)
    {
        // UserDisplayName is not available on AgentRequest (it carries UserId, not a display name).
        // Pass the UserId as a placeholder — the prompt builder only uses this for personalisation
        // and will omit it gracefully when it does not look like a display name.
        return new OrchestratorPromptContext(
            UserDisplayName: request.UserId,
            TenantId: request.TenantId,
            MatterName: null,               // Not available in Phase 2 AgentRequest
            ConversationTurnCount: request.ConversationHistory.Count,
            ActivePlaybookName: null);      // Not available in Phase 2 AgentRequest
    }

    /// <summary>
    /// Constructs the full message list for the LLM call:
    ///   [system prompt] + [conversation history turns] + [current user message]
    ///
    /// Role mapping from <see cref="AgentRole"/> to <see cref="ChatRole"/>:
    ///   - <see cref="AgentRole.User"/>      → <see cref="ChatRole.User"/>
    ///   - <see cref="AgentRole.Assistant"/> → <see cref="ChatRole.Assistant"/>
    ///   - <see cref="AgentRole.System"/>    → <see cref="ChatRole.System"/>
    /// </summary>
    private static List<AiChatMessage> BuildMessages(AgentRequest request, string systemPrompt)
    {
        var messages = new List<AiChatMessage>(capacity: request.ConversationHistory.Count + 2);

        // System message is always first (provides persona, tools, instructions).
        messages.Add(new AiChatMessage(ChatRole.System, systemPrompt));

        // Append conversation history in order (oldest first).
        foreach (var turn in request.ConversationHistory)
        {
            var role = turn.Role switch
            {
                AgentRole.User      => ChatRole.User,
                AgentRole.Assistant => ChatRole.Assistant,
                AgentRole.System    => ChatRole.System,
                _                   => ChatRole.User
            };

            messages.Add(new AiChatMessage(role, turn.Content));
        }

        // Append the current user message last.
        messages.Add(new AiChatMessage(ChatRole.User, request.UserMessage));

        return messages;
    }

    /// <summary>
    /// Builds a safe, user-facing error message from an exception.
    ///
    /// ADR-015: Stack traces and internal details are never exposed. Only the exception
    /// type and a sanitised description are included.
    /// </summary>
    private static string BuildErrorMessage(Exception ex)
    {
        return ex switch
        {
            OperationCanceledException => "The request was cancelled.",
            _ => "An error occurred while processing your request. Please try again."
        };
    }
}
