using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Production implementation of <see cref="IChatContextProvider"/> that resolves
/// context from the playbook's Action (ACT-*) record stored in Dataverse.
///
/// Pattern:
///   1. Call <see cref="IScopeResolverService.ResolvePlaybookScopesAsync"/> to obtain
///      the playbook's resolved scopes (Skills, Knowledge, Tools).
///   2. Load the primary Action via <see cref="IScopeResolverService.GetActionAsync"/>
///      using the first ActionId from the playbook.
///   3. Load the document summary from <see cref="IDataverseService"/> for inline context.
///   4. Compose and return a <see cref="ChatContext"/> with the system prompt and metadata.
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
        CancellationToken cancellationToken)
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

        // 3. Load document summary for inline context injection
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
            PlaybookId: playbookId);
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
