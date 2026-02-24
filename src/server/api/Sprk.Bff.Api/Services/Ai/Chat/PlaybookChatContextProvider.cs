using System.Text;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;

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
///   5. Load the document summary from <see cref="IDataverseService"/> for inline context.
///   6. Return a <see cref="ChatContext"/> with enriched system prompt and knowledge scope.
/// </summary>
public class PlaybookChatContextProvider : IChatContextProvider
{
    private readonly IScopeResolverService _scopeResolver;
    private readonly IPlaybookService _playbookService;
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<PlaybookChatContextProvider> _logger;

    public PlaybookChatContextProvider(
        IScopeResolverService scopeResolver,
        IPlaybookService playbookService,
        IDataverseService dataverseService,
        ILogger<PlaybookChatContextProvider> logger)
    {
        _scopeResolver = scopeResolver;
        _playbookService = playbookService;
        _dataverseService = dataverseService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChatContext> GetContextAsync(
        string documentId,
        string tenantId,
        Guid playbookId,
        ChatHostContext? hostContext = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Building ChatContext for document {DocumentId}, tenant {TenantId}, playbook {PlaybookId}",
            documentId, tenantId, playbookId);

        // 1. Load playbook to get ActionIds
        var playbook = await _playbookService.GetPlaybookAsync(playbookId, cancellationToken);

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
            playbookId, documentId, hostContext, cancellationToken);

        // 4. Enrich system prompt with inline knowledge and skill instructions
        systemPrompt = EnrichSystemPrompt(systemPrompt, knowledgeScope);

        // 5. Load document summary for inline context injection
        string? documentSummary = null;
        IReadOnlyDictionary<string, string>? analysisMetadata = null;

        try
        {
            var document = await _dataverseService.GetDocumentAsync(documentId, cancellationToken);
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

            return new ChatKnowledgeScope(
                RagKnowledgeSourceIds: ragSourceIds,
                InlineContent: string.IsNullOrWhiteSpace(inlineContent) ? null : inlineContent,
                SkillInstructions: string.IsNullOrWhiteSpace(skillInstructions) ? null : skillInstructions,
                ActiveDocumentId: documentId,
                ParentEntityType: hostContext?.EntityType,
                ParentEntityId: hostContext?.EntityId);
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
