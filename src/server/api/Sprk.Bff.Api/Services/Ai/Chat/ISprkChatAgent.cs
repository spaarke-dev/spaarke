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
/// FR-P2-06 (task 035): the Phase-2F compound-intent pre-inspection member was
/// DELETED with the classifier stack (ADR-039 — one dispatch protocol).
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
    /// The agent-turn loop contract state (FR-P2-01, spaarke-ai-architecture-redesign-r1
    /// task 030): per-turn tool budget + the NFR-07-safe tool-call audit persisted to the
    /// session ledger as a <c>ToolChain</c> entry BEFORE rendering (ADR-040).
    /// Default implementation returns null so legacy implementations (Null objects,
    /// test doubles) compile unchanged; middleware wrappers MUST delegate to their
    /// inner agent so the endpoint can reach the contract through the pipeline.
    /// </summary>
    AgentTurnContract? TurnContract => null;

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
