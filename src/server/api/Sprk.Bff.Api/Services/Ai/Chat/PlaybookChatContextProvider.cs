using System.Text;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;
using System.Linq;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Production implementation of <see cref="IChatContextProvider"/> that resolves
/// context from the playbook's Action (ACT-*) record stored in Dataverse.
///
/// Pattern:
///   1. Load the playbook and resolve its primary Action for the system prompt.
///   2. Resolve the playbook's scopes (Skills, Knowledge, Tools) via
///      <see cref="IScopeResolverService.ResolvePlaybookScopesAsync"/>.
///   3. Partition knowledge sources by type: Inline → system prompt, RagIndex → search scope.
///   4. Compose skill PromptFragment values into specialized instructions.
///   5. Load the document summary from <see cref="IDocumentDataverseService"/> for inline context.
///   6. Return a <see cref="ChatContext"/> with enriched system prompt and knowledge scope.
/// </summary>
public class PlaybookChatContextProvider : IChatContextProvider
{
    /// <summary>
    /// Maximum token budget for the system prompt. Enrichment is skipped if appending
    /// would push the total past this limit. Rough estimate: 1 token ≈ 4 characters.
    /// </summary>
    internal const int MaxSystemPromptTokenBudget = 8_000;

    /// <summary>
    /// Maximum token count for the entity enrichment block itself.
    /// </summary>
    internal const int MaxEnrichmentTokens = 100;

    /// <summary>
    /// Maps raw page type values to human-readable labels for the enrichment block.
    /// Unknown or unmapped page types result in enrichment being skipped.
    /// </summary>
    private static readonly Dictionary<string, string> PageTypeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["entityrecord"] = "main form view",
        ["entitylist"] = "list view",
        ["dashboard"] = "dashboard view",
        ["webresource"] = "workspace view",
        ["custom"] = "custom page view"
    };

    private readonly IScopeResolverService _scopeResolver;
    private readonly IPlaybookService _playbookService;
    private readonly IDocumentDataverseService _documentService;
    private readonly ILogger<PlaybookChatContextProvider> _logger;

    public PlaybookChatContextProvider(
        IScopeResolverService scopeResolver,
        IPlaybookService playbookService,
        IDocumentDataverseService documentService,
        ILogger<PlaybookChatContextProvider> logger)
    {
        _scopeResolver = scopeResolver;
        _playbookService = playbookService;
        _documentService = documentService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChatContext> GetContextAsync(
        string documentId,
        string tenantId,
        Guid? playbookId,
        ChatHostContext? hostContext = null,
        IReadOnlyList<string>? additionalDocumentIds = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Building ChatContext for document {DocumentId}, tenant {TenantId}, playbook {PlaybookId}",
            documentId, tenantId, playbookId);

        // When no playbook is specified (generic chat mode), return a default context
        // with no playbook-specific scoping. The agent will use a generic system prompt.
        if (playbookId is null)
        {
            _logger.LogInformation(
                "No playbook specified for document {DocumentId}; using generic chat context",
                documentId);

            var defaultPrompt = BuildDefaultSystemPrompt(null);

            // 6. Append entity metadata enrichment (generic mode)
            defaultPrompt = AppendEntityEnrichment(defaultPrompt, hostContext);

            // Still load document summary for inline context
            string? defaultDocSummary = null;
            IReadOnlyDictionary<string, string>? defaultMetadata = null;
            try
            {
                var doc = await _documentService.GetDocumentAsync(documentId, cancellationToken);
                if (doc != null)
                {
                    defaultDocSummary = doc.Summary ?? doc.Tldr;
                    var meta = new Dictionary<string, string>();
                    if (!string.IsNullOrWhiteSpace(doc.DocumentType))
                        meta["documentType"] = doc.DocumentType;
                    if (!string.IsNullOrWhiteSpace(doc.Name))
                        meta["documentName"] = doc.Name;
                    if (meta.Count > 0)
                        defaultMetadata = meta;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to load document summary for {DocumentId} in generic chat mode; continuing without",
                    documentId);
            }

            return new ChatContext(
                SystemPrompt: defaultPrompt,
                DocumentSummary: defaultDocSummary,
                AnalysisMetadata: defaultMetadata,
                PlaybookId: null,
                KnowledgeScope: null);
        }

        // 1. Load playbook to get ActionIds
        var playbook = await _playbookService.GetPlaybookAsync(playbookId.Value, cancellationToken);

        // 2. Resolve the system prompt from the playbook's primary Action record
        string systemPrompt;
        if (playbook?.ActionIds?.Length > 0)
        {
            var actionId = playbook.ActionIds[0];
            var action = await _scopeResolver.GetActionAsync(actionId, cancellationToken);

            if (action != null && !string.IsNullOrWhiteSpace(action.SystemPrompt))
            {
                systemPrompt = action.SystemPrompt;
                _logger.LogInformation(
                    "Loaded system prompt from action '{ActionName}' (ID: {ActionId}) for playbook {PlaybookId}",
                    action.Name, actionId, playbookId);
            }
            else
            {
                _logger.LogWarning(
                    "Action {ActionId} has no system prompt; using default for playbook {PlaybookId}",
                    actionId, playbookId);
                systemPrompt = BuildDefaultSystemPrompt(playbook.Name);
            }
        }
        else
        {
            _logger.LogWarning(
                "Playbook {PlaybookId} has no actions configured; using default system prompt",
                playbookId);
            systemPrompt = BuildDefaultSystemPrompt(playbook?.Name);
        }

        // 3. Resolve playbook scopes (Skills, Knowledge, Tools)
        var knowledgeScope = await ResolveKnowledgeScopeAsync(
            playbookId.Value, documentId, hostContext, additionalDocumentIds, cancellationToken);

        // 4. Enrich system prompt with inline knowledge and skill instructions
        systemPrompt = EnrichSystemPrompt(systemPrompt, knowledgeScope);

        // 5. Append entity metadata enrichment (after all other sections)
        systemPrompt = AppendEntityEnrichment(systemPrompt, hostContext);

        // 6. Load document summary for inline context injection
        string? documentSummary = null;
        IReadOnlyDictionary<string, string>? analysisMetadata = null;

        try
        {
            var document = await _documentService.GetDocumentAsync(documentId, cancellationToken);
            if (document != null)
            {
                // Use the TL;DR or Summary field if populated from prior analysis
                documentSummary = document.Summary ?? document.Tldr;

                if (!string.IsNullOrWhiteSpace(documentSummary))
                {
                    _logger.LogDebug(
                        "Loaded document summary ({Length} chars) for document {DocumentId}",
                        documentSummary.Length, documentId);
                }

                // Collect lightweight metadata for context
                var metadata = new Dictionary<string, string>();
                if (!string.IsNullOrWhiteSpace(document.DocumentType))
                    metadata["documentType"] = document.DocumentType;
                if (!string.IsNullOrWhiteSpace(document.Name))
                    metadata["documentName"] = document.Name;
                if (metadata.Count > 0)
                    analysisMetadata = metadata;
            }
        }
        catch (Exception ex)
        {
            // Soft failure — document context is optional; agent can still run without it
            _logger.LogWarning(ex,
                "Failed to load document summary for {DocumentId}; continuing without document context",
                documentId);
        }

        return new ChatContext(
            SystemPrompt: systemPrompt,
            DocumentSummary: documentSummary,
            AnalysisMetadata: analysisMetadata,
            PlaybookId: playbookId,
            KnowledgeScope: knowledgeScope);
    }

    /// <summary>
    /// Resolves knowledge scope from the playbook's N:N relationships.
    /// Partitions knowledge sources by type and composes skill instructions.
    /// </summary>
    private async Task<ChatKnowledgeScope?> ResolveKnowledgeScopeAsync(
        Guid playbookId,
        string documentId,
        ChatHostContext? hostContext,
        IReadOnlyList<string>? additionalDocumentIds,
        CancellationToken cancellationToken)
    {
        try
        {
            var scopes = await _scopeResolver.ResolvePlaybookScopesAsync(playbookId, cancellationToken);

            if (scopes.Knowledge.Length == 0 && scopes.Skills.Length == 0)
            {
                _logger.LogDebug(
                    "Playbook {PlaybookId} has no knowledge sources or skills; skipping scope resolution",
                    playbookId);
                return null;
            }

            // Partition knowledge by type
            var ragSourceIds = scopes.Knowledge
                .Where(k => k.Type == KnowledgeType.RagIndex)
                .Select(k => k.Id.ToString())
                .ToList();

            var inlineContent = string.Join(
                "\n\n",
                scopes.Knowledge
                    .Where(k => k.Type == KnowledgeType.Inline && !string.IsNullOrWhiteSpace(k.Content))
                    .Select(k => $"### {k.Name}\n{k.Content}"));

            // Compose skill prompt fragments
            var skillInstructions = string.Join(
                "\n\n",
                scopes.Skills
                    .Where(s => !string.IsNullOrWhiteSpace(s.PromptFragment))
                    .Select(s => s.PromptFragment));

            _logger.LogInformation(
                "Resolved knowledge scope for playbook {PlaybookId}: " +
                "{RagCount} RAG sources, {InlineCount} inline sources, {SkillCount} skill fragments",
                playbookId,
                ragSourceIds.Count,
                scopes.Knowledge.Count(k => k.Type == KnowledgeType.Inline),
                scopes.Skills.Count(s => !string.IsNullOrWhiteSpace(s.PromptFragment)));

            // Normalize additional document IDs: remove nulls/blanks and enforce max cap
            var normalizedAdditionalDocs = additionalDocumentIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .Take(ChatKnowledgeScope.MaxAdditionalDocuments)
                .ToList();

            if (normalizedAdditionalDocs is { Count: > 0 })
            {
                _logger.LogInformation(
                    "Including {AdditionalDocCount} additional document(s) in knowledge scope for playbook {PlaybookId}",
                    normalizedAdditionalDocs.Count, playbookId);
            }

            return new ChatKnowledgeScope(
                RagKnowledgeSourceIds: ragSourceIds,
                InlineContent: string.IsNullOrWhiteSpace(inlineContent) ? null : inlineContent,
                SkillInstructions: string.IsNullOrWhiteSpace(skillInstructions) ? null : skillInstructions,
                ActiveDocumentId: documentId,
                ParentEntityType: hostContext?.EntityType,
                ParentEntityId: hostContext?.EntityId,
                AdditionalDocumentIds: normalizedAdditionalDocs is { Count: > 0 } ? normalizedAdditionalDocs : null);
        }
        catch (Exception ex)
        {
            // Soft failure — knowledge scope is enhancing, not required
            _logger.LogWarning(ex,
                "Failed to resolve knowledge scope for playbook {PlaybookId}; continuing without scoping",
                playbookId);
            return null;
        }
    }

    /// <summary>
    /// Enriches the system prompt with inline knowledge and skill instructions
    /// from the resolved knowledge scope.
    /// </summary>
    private static string EnrichSystemPrompt(string systemPrompt, ChatKnowledgeScope? scope)
    {
        if (scope is null)
            return systemPrompt;

        var sb = new StringBuilder(systemPrompt);

        if (!string.IsNullOrWhiteSpace(scope.InlineContent))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## Reference Materials");
            sb.AppendLine(scope.InlineContent);
        }

        if (!string.IsNullOrWhiteSpace(scope.SkillInstructions))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## Specialized Instructions");
            sb.AppendLine(scope.SkillInstructions);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Appends an entity metadata enrichment block to the system prompt when
    /// the host context provides a valid EntityName and PageType.
    /// </summary>
    /// <remarks>
    /// Guards:
    /// - EntityName must be non-null/non-empty
    /// - PageType must be non-null/non-empty and not "unknown"
    /// - PageType must map to a known human-readable label
    /// - Enrichment block must be ≤ <see cref="MaxEnrichmentTokens"/> tokens
    /// - Total system prompt must not exceed <see cref="MaxSystemPromptTokenBudget"/> tokens
    /// </remarks>
    private string AppendEntityEnrichment(string systemPrompt, ChatHostContext? hostContext)
    {
        // Guard: no host context at all
        if (hostContext is null)
            return systemPrompt;

        // Guard: EntityName must be present
        if (string.IsNullOrWhiteSpace(hostContext.EntityName))
            return systemPrompt;

        // Guard: PageType must be present and not "unknown"
        if (string.IsNullOrWhiteSpace(hostContext.PageType) ||
            string.Equals(hostContext.PageType, "unknown", StringComparison.OrdinalIgnoreCase))
            return systemPrompt;

        // Guard: PageType must map to a known label
        if (!PageTypeLabels.TryGetValue(hostContext.PageType, out var humanReadablePageType))
        {
            _logger.LogDebug(
                "Unmapped page type '{PageType}'; skipping entity enrichment",
                hostContext.PageType);
            return systemPrompt;
        }

        // Build the enrichment block
        var enrichmentBlock =
            $"Context: You are assisting with {hostContext.EntityType} record '{hostContext.EntityName}'. " +
            $"The user is viewing the {humanReadablePageType}.";

        // Guard: enrichment block itself must be ≤ MaxEnrichmentTokens
        var enrichmentTokenEstimate = EstimateTokenCount(enrichmentBlock);
        if (enrichmentTokenEstimate > MaxEnrichmentTokens)
        {
            _logger.LogWarning(
                "Entity enrichment block exceeds token cap ({EstimatedTokens} > {MaxTokens}); skipping enrichment",
                enrichmentTokenEstimate, MaxEnrichmentTokens);
            return systemPrompt;
        }

        // Guard: total system prompt budget
        var currentTokenEstimate = EstimateTokenCount(systemPrompt);
        if (currentTokenEstimate + enrichmentTokenEstimate > MaxSystemPromptTokenBudget)
        {
            _logger.LogWarning(
                "System prompt token budget would be exceeded ({CurrentTokens} + {EnrichmentTokens} > {Budget}); skipping entity enrichment",
                currentTokenEstimate, enrichmentTokenEstimate, MaxSystemPromptTokenBudget);
            return systemPrompt;
        }

        return systemPrompt + "\n\n" + enrichmentBlock;
    }

    /// <summary>
    /// Rough token estimate for English text: word_count * 1.3.
    /// Uses whitespace splitting for word count.
    /// </summary>
    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var wordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return (int)Math.Ceiling(wordCount * 1.3);
    }

    /// <summary>
    /// Appends an <c>### Active Capabilities</c> section to the system prompt listing
    /// scope-contributed commands so the AI model is aware of available slash commands.
    ///
    /// Called by <see cref="SprkChatAgentFactory"/> after resolving the command catalog
    /// from <see cref="DynamicCommandResolver"/>. Only scope-category commands are included
    /// (system and playbook commands are not repeated here).
    ///
    /// ADR-015: Only capability labels and trigger strings are included — no scope
    /// configuration data (descriptions, internal IDs) is exposed in the prompt.
    ///
    /// Budget: kept under ~200 tokens to avoid crowding the 8K system prompt budget.
    /// </summary>
    /// <param name="systemPrompt">The current system prompt to append to.</param>
    /// <param name="commands">The full command catalog from <see cref="DynamicCommandResolver"/>.</param>
    /// <returns>The enriched system prompt with an Active Capabilities section, or unchanged if no scope commands.</returns>
    internal static string AppendActiveCapabilities(
        string systemPrompt,
        IReadOnlyList<CommandEntry>? commands)
    {
        if (commands is null || commands.Count == 0)
        {
            return systemPrompt;
        }

        // Filter to scope-contributed commands only (Category is not "system" or "playbook")
        var scopeCommands = commands
            .Where(c => !string.Equals(c.Category, "system", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(c.Category, "playbook", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (scopeCommands.Count == 0)
        {
            return systemPrompt;
        }

        var sb = new StringBuilder(systemPrompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("### Active Capabilities");
        sb.AppendLine("The following scope-contributed commands are available. You may suggest these to the user when relevant:");

        foreach (var cmd in scopeCommands)
        {
            sb.AppendLine($"- {cmd.Trigger}: {cmd.Description}");
        }

        // Guard: ensure the capabilities section stays within ~200 tokens
        var capabilitiesSection = sb.ToString()[systemPrompt.Length..];
        var capabilityTokens = EstimateTokenCount(capabilitiesSection);

        if (capabilityTokens > 200)
        {
            // Truncate to first N commands that fit within budget
            sb = new StringBuilder(systemPrompt);
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("### Active Capabilities");

            var tokenCount = EstimateTokenCount("### Active Capabilities\n");
            foreach (var cmd in scopeCommands)
            {
                var line = $"- {cmd.Trigger}: {cmd.Description}\n";
                var lineTokens = EstimateTokenCount(line);
                if (tokenCount + lineTokens > 190) // Leave margin
                {
                    break;
                }
                tokenCount += lineTokens;
                sb.Append(line);
            }
        }

        var result = sb.ToString();
        var totalTokens = EstimateTokenCount(result);
        if (totalTokens > MaxSystemPromptTokenBudget)
        {
            // If adding capabilities would exceed the total budget, skip them
            return systemPrompt;
        }

        return result;
    }

    /// <summary>
    /// Builds a sensible default system prompt when the playbook Action is unavailable.
    /// </summary>
    private static string BuildDefaultSystemPrompt(string? playbookName)
    {
        var context = !string.IsNullOrWhiteSpace(playbookName)
            ? $" configured for the '{playbookName}' workflow"
            : string.Empty;

        return $"""
            You are an AI assistant{context} helping users understand and analyze documents.

            ## Instructions
            - Provide helpful, accurate responses grounded in the document content
            - If asked to locate specific information, cite relevant sections
            - Format responses in clear, readable Markdown

            ## Output Format
            Provide your response in Markdown with appropriate headings and structure.
            """;
    }
}
