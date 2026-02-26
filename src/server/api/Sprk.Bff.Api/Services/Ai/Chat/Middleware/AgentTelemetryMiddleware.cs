using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Models.Ai.Chat;

using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Chat.Middleware;

/// <summary>
/// Telemetry middleware for the SprkChat agent pipeline (AIPL-057).
///
/// Wraps <see cref="ISprkChatAgent"/> to record structured telemetry for every
/// <see cref="ISprkChatAgent.SendMessageAsync"/> invocation:
///
///   - Session ID and playbook ID (from <see cref="ChatContext"/>)
///   - Approximate token count (estimated as character count / 4)
///   - Response latency in milliseconds
///
/// Constraint: NEVER logs message content — only metadata (token counts, tool names, latency).
///
/// Intended to be the outermost middleware in the pipeline so it captures the full
/// latency including cost control and content safety processing.
///
/// Lifetime: Transient — created per agent instance by <see cref="SprkChatAgentFactory"/>.
/// </summary>
public sealed class AgentTelemetryMiddleware : ISprkChatAgent
{
    private readonly ISprkChatAgent _inner;
    private readonly ILogger _logger;

    public AgentTelemetryMiddleware(ISprkChatAgent inner, ILogger logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public ChatContext Context => _inner.Context;

    /// <inheritdoc />
    public CitationContext? Citations => _inner.Citations;

    /// <summary>
    /// Streams the agent response while recording telemetry metadata.
    ///
    /// Token count is estimated from cumulative character length / 4 (a standard
    /// approximation for English text with GPT-family tokenizers).
    /// </summary>
    public async IAsyncEnumerable<ChatResponseUpdate> SendMessageAsync(
        string message,
        IReadOnlyList<AiChatMessage> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var charCount = 0;

        await foreach (var update in _inner.SendMessageAsync(message, history, cancellationToken))
        {
            charCount += update.Text?.Length ?? 0;
            yield return update;
        }

        sw.Stop();

        // Estimate tokens: ~4 characters per token for English text
        var estimatedTokens = charCount / 4;

        _logger.LogInformation(
            "ChatAgent telemetry: playbook={PlaybookId}, tokens~={EstimatedTokenCount}, duration={DurationMs}ms, historyCount={HistoryCount}",
            _inner.Context.PlaybookId,
            estimatedTokens,
            sw.ElapsedMilliseconds,
            history.Count);
    }
}
