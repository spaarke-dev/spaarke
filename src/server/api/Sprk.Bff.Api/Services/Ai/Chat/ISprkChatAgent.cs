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
/// </summary>
public interface ISprkChatAgent
{
    /// <summary>
    /// The current chat context (playbook, document summary, etc.).
    /// </summary>
    ChatContext Context { get; }

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
}
