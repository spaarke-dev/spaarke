using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Models.Ai.Chat;

using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Interface for the SprkChat agent, enabling the decorator (middleware) pattern.
///
/// Both <see cref="SprkChatAgent"/> (the core agent) and middleware wrappers
/// (telemetry, cost control, content safety) implement this interface.
///
/// Introduced by AIPL-057 to support the agent middleware pipeline without
/// changing the <see cref="SprkChatAgent"/> constructor or endpoint signatures.
///
/// Updated by task 071 (Phase 2F) to add <see cref="DetectToolCallsAsync"/> for
/// compound intent detection before tool execution.
/// </summary>
public interface ISprkChatAgent
{
    /// <summary>
    /// The current chat context (playbook, document summary, etc.).
    /// </summary>
    ChatContext Context { get; }

    /// <summary>
    /// Citation metadata accumulated by search tools during the last message.
    /// Reset before each new message. May be null when no search tools are registered.
    /// Callers (e.g., SSE endpoints) can read citations after streaming completes
    /// to render footnotes.
    /// </summary>
    CitationContext? Citations { get; }

    /// <summary>
    /// Sends a user message and streams the agent's response.
    /// </summary>
    /// <param name="message">The user's chat message.</param>
    /// <param name="history">Prior messages in the session (user + assistant turns).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of <see cref="ChatResponseUpdate"/> chunks.</returns>
    IAsyncEnumerable<ChatResponseUpdate> SendMessageAsync(
        string message,
        IReadOnlyList<AiChatMessage> history,
        CancellationToken cancellationToken);

    /// <summary>
    /// Performs a single LLM call using the raw (pre-function-invocation) client to determine
    /// what tools the model intends to call for the given user message, WITHOUT executing them.
    ///
    /// This is the compound intent detection step (task 071, Phase 2F).
    /// Called BEFORE <see cref="SendMessageAsync"/> to check if the intended tool chain
    /// requires user approval via a <c>plan_preview</c> SSE event.
    ///
    /// Callers (e.g., <c>ChatEndpoints.SendMessageAsync</c>) should:
    ///   1. Call this method to get the proposed tool calls.
    ///   2. Pass the list to <see cref="CompoundIntentDetector.IsCompoundIntent"/>.
    ///   3. If compound intent: store plan, emit <c>plan_preview</c>, halt.
    ///   4. If not compound intent: call <see cref="SendMessageAsync"/> normally.
    ///
    /// Returns an empty list if the model does not intend to call any tools.
    /// Returns the list of <see cref="FunctionCallContent"/> if the model wants tools.
    ///
    /// Note: This uses the raw client that does NOT have <c>UseFunctionInvocation</c>,
    /// so tool calls are returned as content items without being executed.
    /// </summary>
    /// <param name="message">The user's chat message.</param>
    /// <param name="history">Prior messages in the session (user + assistant turns).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The list of tool calls the LLM intends to make, or an empty list if no tools are needed.
    /// </returns>
    Task<IReadOnlyList<FunctionCallContent>> DetectToolCallsAsync(
        string message,
        IReadOnlyList<AiChatMessage> history,
        CancellationToken cancellationToken);
}
