namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Builds prompts by combining Action, Scopes, and document content.
/// Implements the prompt construction patterns from SPEC.md Section 6.
/// </summary>
public interface IAnalysisContextBuilder
{
    /// <summary>
    /// Build system prompt from Action and Skills.
    /// Combines action's system prompt with skill prompt fragments.
    /// </summary>
    /// <param name="action">The analysis action definition.</param>
    /// <param name="skills">Applied skills with prompt fragments.</param>
    /// <returns>Complete system prompt string.</returns>
    string BuildSystemPrompt(
        AnalysisAction action,
        AnalysisSkill[] skills);

    /// <summary>
    /// Build user prompt with document content and Knowledge grounding.
    /// Includes RAG retrieval for index-based knowledge sources.
    /// </summary>
    /// <param name="documentText">Extracted document text.</param>
    /// <param name="knowledge">Knowledge sources for grounding.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Complete user prompt with document and context.</returns>
    Task<string> BuildUserPromptAsync(
        string documentText,
        AnalysisKnowledge[] knowledge,
        CancellationToken cancellationToken);

    /// <summary>
    /// Build continuation prompt with chat history.
    /// Used for conversational refinement of analysis.
    /// </summary>
    /// <param name="history">Previous chat messages.</param>
    /// <param name="userMessage">New user message.</param>
    /// <param name="currentWorkingDocument">Current state of working document.</param>
    /// <returns>Complete continuation prompt.</returns>
    string BuildContinuationPrompt(
        ChatMessageModel[] history,
        string userMessage,
        string currentWorkingDocument);
}
