namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Shared helper for applying the layered prompt context model to tool handler prompts.
/// Implements "Action = what to do, Tool = how to do it" separation.
/// </summary>
/// <remarks>
/// Prompt priority:
/// 1. ActionSystemPrompt (from sprk_analysisaction) — replaces handler's default template when present
/// 2. Handler's default template — used when no ActionSystemPrompt is configured
/// 3. SkillContext (from sprk_analysisskill.sprk_promptfragment) — always appended
/// 4. KnowledgeContext (from linked knowledge docs) — always appended
/// </remarks>
internal static class PromptContextHelper
{
    /// <summary>
    /// Builds a prompt using the layered context model.
    /// When ActionSystemPrompt is set, it replaces the handler's default prompt.
    /// SkillContext and KnowledgeContext are always appended when present.
    /// </summary>
    /// <param name="defaultPrompt">The handler's hardcoded default prompt (includes document text).</param>
    /// <param name="context">The tool execution context (may be null for backward compat).</param>
    /// <param name="documentText">The document text, used for {document} placeholder substitution.</param>
    /// <returns>The final composed prompt.</returns>
    public static string ApplyContext(string defaultPrompt, ToolExecutionContext? context, string? documentText = null)
    {
        if (context is null)
            return defaultPrompt;

        string basePrompt;

        if (!string.IsNullOrWhiteSpace(context.ActionSystemPrompt))
        {
            // Action prompt is the primary instruction — replace the handler's default template
            basePrompt = context.ActionSystemPrompt;

            // Substitute the {document} placeholder if present
            if (documentText != null && basePrompt.Contains("{document}"))
            {
                basePrompt = basePrompt.Replace("{document}", documentText);
            }
            // If the Action prompt doesn't include document text via placeholder, append it
            else if (documentText != null)
            {
                basePrompt += $"\n\n## Document\n\n{documentText}";
            }
        }
        else
        {
            // No Action prompt configured — use the handler's default template
            basePrompt = defaultPrompt;
        }

        // Append SkillContext (additional analysis instructions from skill prompt fragments)
        if (!string.IsNullOrWhiteSpace(context.SkillContext))
        {
            basePrompt += $"\n\n## Additional Analysis Instructions\n\n{context.SkillContext}";
        }

        // Append KnowledgeContext (reference material from linked knowledge docs)
        if (!string.IsNullOrWhiteSpace(context.KnowledgeContext))
        {
            basePrompt += $"\n\n## Reference Knowledge\n\n{context.KnowledgeContext}";
        }

        return basePrompt;
    }
}
