using System.Text;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Builds prompts by combining Action, Scopes, and document content.
/// Implements the prompt construction patterns from SPEC.md Section 6.
/// </summary>
public class AnalysisContextBuilder : IAnalysisContextBuilder
{
    private readonly AnalysisOptions _options;
    private readonly ILogger<AnalysisContextBuilder> _logger;

    public AnalysisContextBuilder(
        IOptions<AnalysisOptions> options,
        ILogger<AnalysisContextBuilder> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string BuildSystemPrompt(AnalysisAction action, AnalysisSkill[] skills)
    {
        var sb = new StringBuilder();

        // Action's system prompt
        sb.AppendLine(action.SystemPrompt);
        sb.AppendLine();

        // Skills as instructions
        if (skills.Length > 0)
        {
            sb.AppendLine("## Instructions");
            sb.AppendLine();

            foreach (var skill in skills)
            {
                sb.AppendLine($"- {skill.PromptFragment}");
            }

            sb.AppendLine();
        }

        // Output format instruction
        sb.AppendLine("## Output Format");
        sb.AppendLine();
        sb.AppendLine("Provide your analysis in Markdown format with appropriate headings and structure.");

        _logger.LogDebug("Built system prompt with {SkillCount} skills, {PromptLength} chars",
            skills.Length, sb.Length);

        return sb.ToString();
    }

    /// <inheritdoc />
    public async Task<string> BuildUserPromptAsync(
        string documentText,
        AnalysisKnowledge[] knowledge,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();

        // Document to analyze
        sb.AppendLine("# Document to Analyze");
        sb.AppendLine();
        sb.AppendLine(documentText);
        sb.AppendLine();

        // Add knowledge context
        if (knowledge.Length > 0)
        {
            var inlineKnowledge = knowledge.Where(k => k.Type == KnowledgeType.Inline).ToArray();
            var ragKnowledge = knowledge.Where(k => k.Type == KnowledgeType.RagIndex).ToArray();

            // Inline knowledge as reference materials
            if (inlineKnowledge.Length > 0)
            {
                sb.AppendLine("# Reference Materials");
                sb.AppendLine();

                foreach (var k in inlineKnowledge)
                {
                    sb.AppendLine($"## {k.Name}");
                    sb.AppendLine(k.Content);
                    sb.AppendLine();
                }
            }

            // RAG knowledge - would require async search
            if (ragKnowledge.Length > 0)
            {
                // TODO: Implement RAG retrieval via Azure AI Search
                _logger.LogDebug("RAG knowledge sources specified but retrieval not yet implemented");
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Please analyze the document above according to the instructions.");

        _logger.LogDebug("Built user prompt with {KnowledgeCount} knowledge sources, {PromptLength} chars",
            knowledge.Length, sb.Length);

        await Task.CompletedTask;
        return sb.ToString();
    }

    /// <inheritdoc />
    public string BuildContinuationPrompt(
        ChatMessageModel[] history,
        string userMessage,
        string currentWorkingDocument)
    {
        var sb = new StringBuilder();

        // Current analysis
        sb.AppendLine("# Current Analysis");
        sb.AppendLine();
        sb.AppendLine(currentWorkingDocument);
        sb.AppendLine();

        // Conversation history (respect max messages limit)
        var messagesToInclude = history
            .OrderByDescending(m => m.Timestamp)
            .Take(_options.MaxChatHistoryMessages)
            .Reverse()
            .ToArray();

        if (messagesToInclude.Length > 0)
        {
            sb.AppendLine("# Conversation History");
            sb.AppendLine();

            foreach (var msg in messagesToInclude)
            {
                var roleLabel = msg.Role == "user" ? "User" : "Assistant";
                sb.AppendLine($"{roleLabel}: {msg.Content}");
                sb.AppendLine();
            }
        }

        // New request
        sb.AppendLine("# New Request");
        sb.AppendLine();
        sb.AppendLine($"User: {userMessage}");
        sb.AppendLine();
        sb.AppendLine("Please update the analysis based on this feedback. Provide the complete updated analysis, not just the changes.");

        _logger.LogDebug("Built continuation prompt with {HistoryCount} messages, {PromptLength} chars",
            messagesToInclude.Length, sb.Length);

        return sb.ToString();
    }

    /// <inheritdoc />
    public string BuildContinuationPromptWithContext(
        string? systemPrompt,
        string? documentText,
        ChatMessageModel[] history,
        string userMessage,
        string currentWorkingDocument)
    {
        var sb = new StringBuilder();

        // System prompt section (if available)
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            sb.AppendLine("# System Instructions");
            sb.AppendLine();
            sb.AppendLine(systemPrompt);
            sb.AppendLine();
        }

        // Original document context (truncated if needed)
        if (!string.IsNullOrWhiteSpace(documentText))
        {
            sb.AppendLine("# Original Document");
            sb.AppendLine();

            // Truncate document text if it exceeds configured max length
            var maxLength = _options.MaxDocumentContextLength;
            if (documentText.Length > maxLength)
            {
                sb.AppendLine(documentText[..maxLength]);
                sb.AppendLine();
                sb.AppendLine($"[Document truncated - showing first {maxLength:N0} of {documentText.Length:N0} characters]");
            }
            else
            {
                sb.AppendLine(documentText);
            }
            sb.AppendLine();
        }

        // Current working document / analysis output
        if (!string.IsNullOrWhiteSpace(currentWorkingDocument))
        {
            sb.AppendLine("# Current Analysis Output");
            sb.AppendLine();
            sb.AppendLine(currentWorkingDocument);
            sb.AppendLine();
        }

        // Conversation history (respect max messages limit)
        var messagesToInclude = history
            .OrderByDescending(m => m.Timestamp)
            .Take(_options.MaxChatHistoryMessages)
            .Reverse()
            .ToArray();

        if (messagesToInclude.Length > 0)
        {
            sb.AppendLine("# Conversation History");
            sb.AppendLine();

            foreach (var msg in messagesToInclude)
            {
                var roleLabel = msg.Role == "user" ? "User" : "Assistant";
                sb.AppendLine($"{roleLabel}: {msg.Content}");
                sb.AppendLine();
            }
        }

        // New user request
        sb.AppendLine("# New Request");
        sb.AppendLine();
        sb.AppendLine($"User: {userMessage}");
        sb.AppendLine();
        sb.AppendLine("Please update the analysis based on this feedback. Use the original document content and current analysis to provide accurate, document-specific responses. Provide the complete updated analysis, not just the changes.");

        _logger.LogDebug(
            "Built continuation prompt with context: SystemPrompt={HasSystem}, DocumentText={HasDoc} ({DocLength} chars), History={HistoryCount}, Total={TotalLength} chars",
            !string.IsNullOrWhiteSpace(systemPrompt),
            !string.IsNullOrWhiteSpace(documentText),
            documentText?.Length ?? 0,
            messagesToInclude.Length,
            sb.Length);

        return sb.ToString();
    }
}
