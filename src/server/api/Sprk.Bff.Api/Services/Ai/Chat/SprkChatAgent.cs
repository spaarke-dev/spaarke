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
///
/// Lifetime: Transient per chat session — created by <see cref="SprkChatAgentFactory"/>
/// on session creation and on context switch (new document or playbook).
///
/// Constraints (ADR-013, spec):
/// - Agent is created via factory — not directly constructed in endpoints
/// - System prompt MUST originate from the playbook's Action (ACT-*) record
/// - Agent supports context switching without creating a new session
///   (caller replaces context via factory.CreateAgentAsync and attaches existing history)
/// </summary>
public sealed class SprkChatAgent : ISprkChatAgent
{
    private readonly IChatClient _chatClient;
    private readonly ChatContext _context;
    private readonly IReadOnlyList<AIFunction> _tools;
    private readonly ILogger<SprkChatAgent> _logger;

    /// <summary>
    /// Exposes the current <see cref="ChatContext"/> so callers can detect when a
    /// context switch is needed (different document or playbook).
    /// </summary>
    public ChatContext Context => _context;

    /// <summary>
    /// Creates a new SprkChatAgent.  Called exclusively by <see cref="SprkChatAgentFactory"/>.
    /// </summary>
    /// <param name="chatClient">Azure OpenAI IChatClient (singleton from DI).</param>
    /// <param name="context">
    /// Context composed from the playbook's Action record (system prompt, document summary,
    /// analysis metadata, playbook ID).
    /// </param>
    /// <param name="tools">AI functions registered as tools for this agent session.</param>
    /// <param name="logger">Logger.</param>
    public SprkChatAgent(
        IChatClient chatClient,
        ChatContext context,
        IReadOnlyList<AIFunction> tools,
        ILogger<SprkChatAgent> logger)
    {
        _chatClient = chatClient;
        _context = context;
        _tools = tools;
        _logger = logger;
    }

    /// <summary>
    /// Sends a user message and streams the agent's response.
    ///
    /// The method prepends the system prompt from <see cref="ChatContext.SystemPrompt"/> and
    /// optionally appends a document context block from <see cref="ChatContext.DocumentSummary"/>
    /// before forwarding the full conversation history to <see cref="IChatClient.GetStreamingResponseAsync"/>.
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

        // Build the full message list: [system] + [history] + [user]
        var messages = BuildMessages(message, history);

        // Build completion options with registered tools
        var options = BuildOptions();

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
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
