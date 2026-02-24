using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Models.Ai.Chat;

using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Chat.Middleware;

/// <summary>
/// Cost control middleware for the SprkChat agent pipeline (AIPL-057).
///
/// Tracks cumulative token usage across all <see cref="ISprkChatAgent.SendMessageAsync"/>
/// invocations for a session and enforces a configurable token budget.
///
/// When the budget is exceeded, subsequent calls yield a polite limit message
/// instead of forwarding to the inner agent — preventing runaway token consumption.
///
/// Token budget is configurable per playbook via the <paramref name="maxTokenBudget"/>
/// constructor parameter (default: 10,000 tokens per session).
///
/// Lifetime: Transient — one instance per agent session, created by <see cref="SprkChatAgentFactory"/>.
/// </summary>
public sealed class AgentCostControlMiddleware : ISprkChatAgent
{
    /// <summary>Default token budget per session when not configured.</summary>
    public const int DefaultMaxTokenBudget = 10_000;

    /// <summary>
    /// Polite message returned when the session token budget is exceeded.
    /// </summary>
    internal const string BudgetExceededMessage =
        "I've reached the usage limit for this session. Please start a new session to continue our conversation.";

    private readonly ISprkChatAgent _inner;
    private readonly int _maxTokenBudget;
    private readonly ILogger _logger;
    private int _sessionTokenCount;

    public AgentCostControlMiddleware(
        ISprkChatAgent inner,
        ILogger logger,
        int maxTokenBudget = DefaultMaxTokenBudget)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxTokenBudget = maxTokenBudget > 0 ? maxTokenBudget : DefaultMaxTokenBudget;
    }

    /// <inheritdoc />
    public ChatContext Context => _inner.Context;

    /// <summary>
    /// Current cumulative token count for this session.
    /// Exposed for testing and monitoring.
    /// </summary>
    public int SessionTokenCount => _sessionTokenCount;

    /// <summary>
    /// Checks the cumulative token budget before forwarding to the inner agent.
    ///
    /// If the budget is already exceeded, yields a single polite limit message.
    /// Otherwise, streams the inner agent response and accumulates token counts.
    /// </summary>
    public async IAsyncEnumerable<ChatResponseUpdate> SendMessageAsync(
        string message,
        IReadOnlyList<AiChatMessage> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Check if budget is already exceeded before calling the inner agent
        if (_sessionTokenCount >= _maxTokenBudget)
        {
            _logger.LogWarning(
                "ChatAgent cost control: budget exceeded for playbook={PlaybookId}, sessionTokens={SessionTokenCount}, budget={MaxTokenBudget}",
                _inner.Context.PlaybookId, _sessionTokenCount, _maxTokenBudget);

            // ChatResponseUpdate.Text is read-only (computed from Contents);
            // add a TextContent to Contents instead.
            var budgetExceededUpdate = new ChatResponseUpdate { Role = ChatRole.Assistant };
            budgetExceededUpdate.Contents.Add(new TextContent(BudgetExceededMessage));
            yield return budgetExceededUpdate;
            yield break;
        }

        // Stream inner agent response and accumulate token count
        var charCount = 0;

        await foreach (var update in _inner.SendMessageAsync(message, history, cancellationToken))
        {
            charCount += update.Text?.Length ?? 0;
            yield return update;
        }

        // Estimate tokens and update session counter
        var estimatedTokens = charCount / 4;
        _sessionTokenCount += estimatedTokens;

        _logger.LogDebug(
            "ChatAgent cost control: added ~{EstimatedTokens} tokens, sessionTotal={SessionTokenCount}/{MaxTokenBudget}",
            estimatedTokens, _sessionTokenCount, _maxTokenBudget);
    }
}
