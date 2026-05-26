namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Role of the participant in a conversation turn.
///
/// Used by <see cref="ConversationTurn"/> to indicate who produced a given message in
/// the conversation history passed to <see cref="ISprkAgent.ProcessAsync"/>.
/// </summary>
public enum AgentRole
{
    /// <summary>A message produced by the end user.</summary>
    User,

    /// <summary>A message produced by the AI assistant (agent response).</summary>
    Assistant,

    /// <summary>A system-level instruction (e.g. playbook system prompt).</summary>
    System
}

/// <summary>
/// A single turn in a conversation, representing one message from a participant.
///
/// Conversation history is passed to <see cref="ISprkAgent.ProcessAsync"/> via
/// <see cref="AgentRequest.ConversationHistory"/> so the agent can maintain context
/// across multiple user/assistant exchanges within a session.
///
/// This record is provider-agnostic and must not reference any Azure OpenAI or
/// Foundry SDK types (FR-701).
/// </summary>
/// <param name="Role">The role of the participant who produced this message.</param>
/// <param name="Content">The text content of the message.</param>
/// <param name="Timestamp">UTC timestamp when this turn occurred.</param>
public sealed record ConversationTurn(
    AgentRole Role,
    string Content,
    DateTimeOffset Timestamp);
