using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Models.Ai.Chat;

// Explicit alias to resolve ChatMessage ambiguity between the Dataverse domain model
// (Sprk.Bff.Api.Models.Ai.Chat.ChatMessage) and the Agent Framework type
// (Microsoft.Extensions.AI.ChatMessage). Both namespaces are required:
// - Models.Ai.Chat provides ChatContext (domain model)
// - Microsoft.Extensions.AI provides IChatClient, ChatMessage, ChatRole for LLM calls
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Core Agent Framework agent for the SprkChat feature.
///
/// SprkChatAgent wraps <see cref="IChatClient"/> with:
/// - System prompt injection from the playbook's Action record (via <see cref="ChatContext"/>)
/// - Tool registration via <see cref="AIFunction"/> / <see cref="AIFunctionFactory"/>
/// - Streaming responses via <see cref="IChatClient.GetStreamingResponseAsync"/>
/// - Compound intent detection via <see cref="DetectToolCallsAsync"/> (task 071, Phase 2F)
///
/// Lifetime: Transient per chat session — created by <see cref="SprkChatAgentFactory"/>
/// on session creation and on context switch (new document or playbook).
///
/// Constraints (ADR-013, spec):
/// - Agent is created via factory — not directly constructed in endpoints
/// - System prompt MUST originate from the playbook's Action (ACT-*) record
/// - Agent supports context switching without creating a new session
///   (caller replaces context via factory.CreateAgentAsync and attaches existing history)
///
/// Phase 2F (task 071) additions:
/// - <see cref="DetectToolCallsAsync"/>: uses the raw (pre-function-invocation) client to get
///   the LLM's intended tool calls without executing them. Callers use this to check for
///   compound intent before deciding whether to gate execution behind plan_preview.
/// </summary>
public sealed class SprkChatAgent : ISprkChatAgent
{
    private readonly IChatClient _chatClient;
    private readonly IChatClient _rawChatClient;
    private readonly ChatContext _context;
    private readonly IReadOnlyList<AIFunction> _tools;
    private readonly CitationContext? _citationContext;
    private readonly CompoundIntentDetector _intentDetector;
    private readonly ILogger<SprkChatAgent> _logger;

    /// <summary>
    /// Exposes the current <see cref="ChatContext"/> so callers can detect when a
    /// context switch is needed (different document or playbook).
    /// </summary>
    public ChatContext Context => _context;

    /// <summary>
    /// Exposes the citation context so callers (e.g., SSE endpoints) can retrieve
    /// citations accumulated during the last message for rendering footnotes.
    /// May be null when no search tools are registered.
    /// </summary>
    public CitationContext? Citations => _citationContext;

    /// <summary>
    /// Creates a new SprkChatAgent.  Called exclusively by <see cref="SprkChatAgentFactory"/>.
    /// </summary>
    /// <param name="chatClient">Azure OpenAI IChatClient with UseFunctionInvocation (singleton from DI).</param>
    /// <param name="rawChatClient">
    /// Raw Azure OpenAI IChatClient WITHOUT UseFunctionInvocation.
    /// Used by <see cref="DetectToolCallsAsync"/> to inspect tool call intentions
    /// without executing them (task 071, Phase 2F compound intent detection).
    /// Registered in DI as keyed service "raw" (see AiModule.cs).
    /// </param>
    /// <param name="context">
    /// Context composed from the playbook's Action record (system prompt, document summary,
    /// analysis metadata, playbook ID).
    /// </param>
    /// <param name="tools">AI functions registered as tools for this agent session.</param>
    /// <param name="citationContext">
    /// Shared citation context populated by search tools during tool execution.
    /// Reset before each message to keep citation numbering per-response.
    /// May be null when no search tools are available.
    /// </param>
    /// <param name="intentDetector">
    /// Compound intent detector (task 071). Analyzes tool call lists to determine if
    /// plan_preview gating is required before execution.
    /// </param>
    /// <param name="logger">Logger.</param>
    public SprkChatAgent(
        IChatClient chatClient,
        IChatClient rawChatClient,
        ChatContext context,
        IReadOnlyList<AIFunction> tools,
        CitationContext? citationContext,
        CompoundIntentDetector intentDetector,
        ILogger<SprkChatAgent> logger)
    {
        _chatClient = chatClient;
        _rawChatClient = rawChatClient;
        _context = context;
        _tools = tools;
        _citationContext = citationContext;
        _intentDetector = intentDetector;
        _logger = logger;
    }

    /// <summary>
    /// Sends a user message and streams the agent's response.
    ///
    /// The method prepends the system prompt from <see cref="ChatContext.SystemPrompt"/> and
    /// optionally appends a document context block from <see cref="ChatContext.DocumentSummary"/>
    /// before forwarding the full conversation history to <see cref="IChatClient.GetStreamingResponseAsync"/>.
    ///
    /// Uses the function-invocation-enabled client so tool calls are automatically executed.
    /// Call <see cref="DetectToolCallsAsync"/> BEFORE this method when compound intent
    /// detection is required (i.e., when the caller must check for plan_preview gating).
    /// </summary>
    /// <param name="message">The user's chat message.</param>
    /// <param name="history">
    /// Prior messages in the session (user + assistant turns).
    /// The system message is NOT included in history — it is prepended by this method on every call.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An async enumerable of <see cref="ChatResponseUpdate"/> chunks.
    /// Callers must drain the enumerable to obtain the full response.
    /// </returns>
    public async IAsyncEnumerable<ChatResponseUpdate> SendMessageAsync(
        string message,
        IReadOnlyList<AiChatMessage> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));

        _logger.LogInformation(
            "SprkChatAgent.SendMessageAsync — playbook {PlaybookId}, historyCount={HistoryCount}, msgLen={MsgLen}",
            _context.PlaybookId, history.Count, message.Length);

        // Reset citation context for this message so citation numbering starts at [1].
        // Citations are scoped per assistant response, not accumulated across the conversation.
        _citationContext?.Reset();

        // Build the full message list: [system] + [history] + [user]
        var messages = BuildMessages(message, history);

        // Build completion options with registered tools
        var options = BuildOptions();

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
    }

    /// <summary>
    /// Performs a single LLM call using the raw (pre-function-invocation) client to determine
    /// what tools the model intends to call, WITHOUT executing them.
    ///
    /// This is the compound intent detection step (task 071, Phase 2F).
    /// Uses <see cref="_rawChatClient"/> which does NOT have UseFunctionInvocation, so
    /// tool calls are returned as <see cref="FunctionCallContent"/> items in the response
    /// without being automatically executed.
    ///
    /// The caller (<c>ChatEndpoints.SendMessageAsync</c>) uses the returned list to:
    ///   1. Check for compound intent via <see cref="CompoundIntentDetector.IsCompoundIntent"/>.
    ///   2. If compound intent: build + store a <see cref="Models.Ai.Chat.PendingPlan"/>,
    ///      emit a <c>plan_preview</c> SSE event, and halt execution.
    ///   3. If not compound intent: call <see cref="SendMessageAsync"/> normally.
    ///
    /// Performance note: This makes an additional LLM round-trip before the main streaming call.
    /// It only runs when tools are registered (returns empty list immediately when no tools).
    /// The extra latency (~200-400ms) is acceptable for the plan_preview gate.
    /// </summary>
    /// <param name="message">The user's chat message.</param>
    /// <param name="history">Prior messages in the session (user + assistant turns).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The list of <see cref="FunctionCallContent"/> items the LLM intends to call,
    /// or an empty list if the model does not intend to call any tools.
    /// </returns>
    public async Task<IReadOnlyList<FunctionCallContent>> DetectToolCallsAsync(
        string message,
        IReadOnlyList<AiChatMessage> history,
        CancellationToken cancellationToken)
    {
        // Early exit: if no tools are registered, there can be no tool calls
        if (_tools.Count == 0)
        {
            _logger.LogDebug(
                "DetectToolCallsAsync: no tools registered for playbook={PlaybookId}; skipping detection",
                _context.PlaybookId);
            return [];
        }

        _logger.LogDebug(
            "DetectToolCallsAsync: inspecting tool intent — playbook={PlaybookId}, historyCount={HistoryCount}",
            _context.PlaybookId, history.Count);

        var messages = BuildMessages(message, history);
        var options = BuildOptions();

        // Use the raw client (no function invocation) to get the LLM's tool call intentions.
        // The response will contain FunctionCallContent items for each tool call requested,
        // without executing any of them.
        var response = await _rawChatClient.GetResponseAsync(messages, options, cancellationToken);

        var toolCalls = new List<FunctionCallContent>();

        // ChatResponse.Messages contains all messages in the response (typically one assistant message).
        // Iterate over all messages and their content items to collect FunctionCallContent items.
        foreach (var msg in response.Messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent toolCall)
                {
                    toolCalls.Add(toolCall);
                }
            }
        }

        _logger.LogInformation(
            "DetectToolCallsAsync: detected {ToolCallCount} tool call(s) for playbook={PlaybookId}",
            toolCalls.Count, _context.PlaybookId);

        return toolCalls;
    }

    // === Private helpers ===

    /// <summary>
    /// Constructs the full message list to send to the LLM.
    ///
    /// Layout:
    ///   [0] System — system prompt + optional document context block
    ///   [1..N] History — prior user/assistant turns
    ///   [N+1] User — current message
    /// </summary>
    private List<AiChatMessage> BuildMessages(string userMessage, IReadOnlyList<AiChatMessage> history)
    {
        var systemContent = BuildSystemContent();
        var messages = new List<AiChatMessage>(capacity: history.Count + 2)
        {
            new AiChatMessage(ChatRole.System, systemContent)
        };

        messages.AddRange(history);
        messages.Add(new AiChatMessage(ChatRole.User, userMessage));

        return messages;
    }

    /// <summary>
    /// Composes the system message content from the system prompt, document identity,
    /// analysis metadata, and optional document summary.
    ///
    /// Always includes a "Current Document Context" block when an active document ID
    /// is available — even when DocumentSummary is null — so the LLM knows which
    /// document it's working with and can call tools like GetAnalysisResult.
    /// </summary>
    private string BuildSystemContent()
    {
        var sb = new System.Text.StringBuilder(_context.SystemPrompt);

        // Gather document identity from KnowledgeScope and AnalysisMetadata
        var activeDocumentId = _context.KnowledgeScope?.ActiveDocumentId;
        var documentName = _context.AnalysisMetadata?.GetValueOrDefault("documentName");
        var documentType = _context.AnalysisMetadata?.GetValueOrDefault("documentType");

        var hasDocumentContext = activeDocumentId != null
            || documentName != null
            || !string.IsNullOrWhiteSpace(_context.DocumentSummary);

        if (hasDocumentContext)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## Current Document Context");

            if (!string.IsNullOrWhiteSpace(documentName))
                sb.AppendLine($"**Document**: {documentName}");
            if (!string.IsNullOrWhiteSpace(activeDocumentId))
                sb.AppendLine($"**Document ID**: {activeDocumentId}");
            if (!string.IsNullOrWhiteSpace(documentType))
                sb.AppendLine($"**Type**: {documentType}");

            if (!string.IsNullOrWhiteSpace(_context.DocumentSummary))
            {
                sb.AppendLine();
                sb.AppendLine(_context.DocumentSummary);
            }
        }

        // Append additional document IDs so the agent knows which extra documents
        // are available for cross-referencing and can call tools to retrieve their content.
        var additionalDocs = _context.KnowledgeScope?.AdditionalDocumentIds;
        if (additionalDocs is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## Additional Documents");
            sb.AppendLine($"The user has pinned {additionalDocs.Count} additional document(s) for cross-referencing.");
            sb.AppendLine("You can use the SearchDocuments or GetAnalysisResult tools to retrieve their content.");
            sb.AppendLine();
            for (var i = 0; i < additionalDocs.Count; i++)
            {
                sb.AppendLine($"- **Additional Document {i + 1} ID**: {additionalDocs[i]}");
            }
        }

        // Citation instructions: instruct the AI to use citation markers when referencing
        // search results. Only added when citation context is available (search tools are registered).
        if (_citationContext is not null)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## Citation Guidelines");
            sb.AppendLine("When citing information from search results, include the citation marker [N] inline where N matches the source number provided in the search results. Place citations at the end of the sentence or clause that references the source.");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Creates <see cref="ChatOptions"/> with registered tools and tool choice mode.
    /// Returns null options when no tools are registered (avoids unnecessary overhead).
    /// </summary>
    private ChatOptions? BuildOptions()
    {
        if (_tools.Count == 0)
        {
            return null;
        }

        return new ChatOptions
        {
            Tools = [.. _tools],
            ToolMode = ChatToolMode.Auto
        };
    }
}
