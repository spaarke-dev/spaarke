using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat.Middleware;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Factory that creates configured <see cref="SprkChatAgent"/> instances.
///
/// Registered as singleton (ADR-010, task constraint).  The singleton holds references
/// to <see cref="IChatClient"/> (singleton) and <see cref="IChatContextProvider"/> (scoped,
/// resolved via IServiceProvider to avoid captive-dependency anti-pattern).
///
/// Responsibilities:
///   1. Resolve <see cref="IChatContextProvider"/> from a scoped DI scope so that
///      each agent creation gets a fresh scoped context (avoids captive dependency).
///   2. Load document/playbook context via <see cref="IChatContextProvider.GetContextAsync"/>.
///   3. Resolve registered <see cref="AIFunction"/> tools from DI.
///   4. Construct and return a fully configured <see cref="SprkChatAgent"/>.
///
/// Constraint (ADR-013): Agents MUST be created via this factory — not constructed
/// directly in endpoints or session managers.
///
/// Constraint (spec): Factory supports context switching — callers create a new agent
/// with a new context but attach the existing chat history from the session.
/// </summary>
public sealed class SprkChatAgentFactory
{
    private readonly IChatClient _chatClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SprkChatAgentFactory> _logger;

    public SprkChatAgentFactory(
        IChatClient chatClient,
        IServiceProvider serviceProvider,
        ILogger<SprkChatAgentFactory> logger)
    {
        _chatClient = chatClient;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Creates a <see cref="SprkChatAgent"/> for the given session parameters.
    ///
    /// A new agent instance is returned on every call.  Callers (e.g. ChatSessionManager)
    /// are responsible for caching the agent for the duration of a session and replacing it
    /// when a context switch occurs (different document or playbook).
    /// </summary>
    /// <param name="sessionId">Opaque session identifier (used for logging/tracing).</param>
    /// <param name="documentId">Dataverse sprk_document ID for the active document.</param>
    /// <param name="playbookId">Playbook governing the agent's system prompt and tools.</param>
    /// <param name="tenantId">Tenant ID extracted from the user's JWT claims.</param>
    /// <param name="hostContext">Optional host context describing where SprkChat is embedded.</param>
    /// <param name="additionalDocumentIds">
    /// Optional list of additional document IDs (max 5) pinned to the conversation for
    /// cross-referencing. Propagated to <see cref="ChatKnowledgeScope.AdditionalDocumentIds"/>.
    /// </param>
    /// <param name="httpContext">
    /// HTTP context for OBO authentication. Required by <see cref="AnalysisExecutionTools"/> to call
    /// <see cref="IAnalysisOrchestrationService.ExecutePlaybookAsync"/> which downloads files from SPE.
    /// May be null for non-streaming contexts (e.g., background processing).
    /// </param>
    /// <param name="sseWriter">
    /// Optional SSE writer delegate for out-of-band events (progress, document_replace).
    /// Used by <see cref="AnalysisExecutionTools.RerunAnalysisAsync"/> to emit progress and
    /// document replacement events during re-analysis. Null when SSE is not available.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A fully configured <see cref="ISprkChatAgent"/> ready to receive messages.
    /// The returned agent is wrapped with the middleware pipeline (AIPL-057):
    /// ContentSafety (innermost) -> CostControl -> Telemetry (outermost).
    /// </returns>
    public async Task<ISprkChatAgent> CreateAgentAsync(
        string sessionId,
        string documentId,
        Guid playbookId,
        string tenantId,
        ChatHostContext? hostContext = null,
        IReadOnlyList<string>? additionalDocumentIds = null,
        HttpContext? httpContext = null,
        Func<Api.Ai.ChatSseEvent, CancellationToken, Task>? sseWriter = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating SprkChatAgent for session={SessionId}, document={DocumentId}, playbook={PlaybookId}, tenant={TenantId}",
            sessionId, documentId, playbookId, tenantId);

        // Resolve IChatContextProvider from a fresh scope to avoid captive dependency.
        // IChatContextProvider is registered as scoped (per-request); the factory is a singleton,
        // so we must create a scope here rather than capturing a scoped instance in the ctor.
        await using var scope = _serviceProvider.CreateAsyncScope();
        var contextProvider = scope.ServiceProvider.GetRequiredService<IChatContextProvider>();

        // Load playbook context (system prompt, document summary, metadata)
        var context = await contextProvider.GetContextAsync(
            documentId,
            tenantId,
            playbookId,
            hostContext,
            additionalDocumentIds,
            cancellationToken);

        // Resolve playbook capabilities from Dataverse to determine which tools should be available.
        var capabilities = await GetPlaybookCapabilitiesAsync(
            scope.ServiceProvider, playbookId, cancellationToken);

        // Create a shared CitationContext for search tools to populate with source metadata.
        // This context is passed to DocumentSearchTools and KnowledgeRetrievalTools so they
        // can register citations during tool execution. The SprkChatAgent resets it before
        // each message to keep citation numbering scoped per assistant response.
        var citationContext = new CitationContext();

        // Resolve registered AIFunction tools from DI, passing tenant ID, knowledge scope,
        // and playbook capabilities so tools are gated to only those the playbook declares.
        var tools = ResolveTools(
            scope.ServiceProvider, tenantId, context.KnowledgeScope, capabilities,
            playbookId, documentId, httpContext, sseWriter, citationContext);

        _logger.LogInformation(
            "SprkChatAgent created: playbook={PlaybookId}, toolCount={ToolCount}, hasDocSummary={HasDocSummary}",
            playbookId, tools.Count, context.DocumentSummary != null);

        var agentLogger = scope.ServiceProvider.GetRequiredService<ILogger<SprkChatAgent>>();

        ISprkChatAgent agent = new SprkChatAgent(_chatClient, context, tools, citationContext, agentLogger);

        // === Middleware pipeline (AIPL-057) ===
        // Wrap order: ContentSafety (innermost) -> CostControl -> Telemetry (outermost).
        // The outermost middleware executes first on each call and records total latency.
        agent = WrapWithMiddleware(agent);

        return agent;
    }

    /// <summary>
    /// Wraps the given agent with the middleware pipeline (AIPL-057).
    ///
    /// Pipeline order (inside-out):
    ///   1. ContentSafety — filters PII from response tokens (innermost)
    ///   2. CostControl   — enforces session token budget
    ///   3. Telemetry      — logs metadata: latency, token count, playbook (outermost)
    ///
    /// No new DI registrations are added (ADR-010 constraint: middleware is instantiated
    /// directly by the factory, same as tool classes).
    /// </summary>
    private ISprkChatAgent WrapWithMiddleware(ISprkChatAgent agent)
    {
        // 1. Content safety (innermost — filters before other middleware processes tokens)
        agent = new AgentContentSafetyMiddleware(
            agent,
            _logger);

        // 2. Cost control (checks budget, counts tokens)
        agent = new AgentCostControlMiddleware(
            agent,
            _logger);

        // 3. Telemetry (outermost — records total latency including all middleware)
        agent = new AgentTelemetryMiddleware(
            agent,
            _logger);

        return agent;
    }

    // === Private helpers ===

    /// <summary>
    /// Creates <see cref="AIFunction"/> tool instances for the agent session.
    ///
    /// Tool classes are instantiated directly (not resolved from DI) per the AIPL-053 design:
    /// this keeps tool class lifetimes scoped to a single agent session and avoids registering
    /// them in the DI container (ADR-010: no unnecessary DI registrations).
    ///
    /// Required services (IRagService, IAnalysisOrchestrationService, IChatClient) are already
    /// registered in DI and are resolved here from <paramref name="scopedProvider"/>.
    ///
    /// Tools gated by playbook capabilities (AnalysisExecutionTools, WebSearchTools) are only
    /// included when the playbook declares the corresponding capability. Ungated tools
    /// (DocumentSearchTools, AnalysisQueryTools, KnowledgeRetrievalTools, TextRefinementTools)
    /// are registered based on service availability — task 047 will refactor these to be
    /// capability-gated as well.
    /// </summary>
    /// <param name="scopedProvider">The scoped DI provider for this agent creation call.</param>
    /// <param name="tenantId">Tenant ID from the authenticated session — injected into tool constructors (ADR-014).</param>
    /// <param name="knowledgeScope">
    /// Knowledge scope from the playbook, containing RAG source IDs for search filtering.
    /// Null when the playbook has no knowledge sources configured.
    /// </param>
    /// <param name="capabilities">
    /// Playbook capabilities governing which tools are available. Tools gated behind a capability
    /// are only registered when the capability is present in this set. See <see cref="PlaybookCapabilities"/>.
    /// </param>
    /// <param name="playbookId">The playbook ID — passed to AnalysisExecutionTools for re-analysis.</param>
    /// <param name="documentId">The active document ID — passed to AnalysisExecutionTools for re-analysis.</param>
    /// <param name="httpContext">HTTP context for OBO auth — passed to AnalysisExecutionTools for re-analysis.</param>
    /// <param name="sseWriter">SSE writer delegate — passed to AnalysisExecutionTools for progress/document_replace events.</param>
    /// <param name="citationContext">
    /// Shared citation context for search tools to populate with source metadata (chunk IDs, source names, excerpts).
    /// Passed to DocumentSearchTools and KnowledgeRetrievalTools so they register citations during execution.
    /// </param>
    /// <returns>List of registered <see cref="AIFunction"/> instances, or empty list on failure.</returns>
    private IReadOnlyList<AIFunction> ResolveTools(
        IServiceProvider scopedProvider,
        string tenantId,
        ChatKnowledgeScope? knowledgeScope,
        IReadOnlySet<string> capabilities,
        Guid playbookId,
        string documentId,
        HttpContext? httpContext,
        Func<Api.Ai.ChatSseEvent, CancellationToken, Task>? sseWriter,
        CitationContext? citationContext)
    {
        try
        {
            // Resolve services that tool classes depend on from DI.
            // IRagService and IAnalysisOrchestrationService are registered in Program.cs.
            // IChatClient is registered in AiModule.cs (AIPL-050).
            var ragService = scopedProvider.GetService<IRagService>();
            var analysisService = scopedProvider.GetService<IAnalysisOrchestrationService>();

            var tools = new List<AIFunction>();

            // DocumentSearchTools — requires IRagService, accepts knowledge scope for domain filtering
            if (ragService != null)
            {
                var documentSearchTools = new DocumentSearchTools(ragService, tenantId, knowledgeScope, citationContext);
                tools.Add(AIFunctionFactory.Create(
                    documentSearchTools.SearchDocumentsAsync,
                    name: "SearchDocuments",
                    description: "Search the knowledge index for document content relevant to the user's query."));
                tools.Add(AIFunctionFactory.Create(
                    documentSearchTools.SearchDiscoveryAsync,
                    name: "SearchDiscovery",
                    description: "Perform a broad discovery search across all indexed documents for the tenant."));
            }
            else
            {
                _logger.LogWarning("IRagService not available; DocumentSearchTools will not be registered");
            }

            // AnalysisQueryTools — requires IAnalysisOrchestrationService
            if (analysisService != null)
            {
                var analysisQueryTools = new AnalysisQueryTools(analysisService, tenantId);
                tools.Add(AIFunctionFactory.Create(
                    analysisQueryTools.GetAnalysisResultAsync,
                    name: "GetAnalysisResult",
                    description: "Retrieve full analysis results for a specific document by analysis ID."));
                tools.Add(AIFunctionFactory.Create(
                    analysisQueryTools.GetAnalysisSummaryAsync,
                    name: "GetAnalysisSummary",
                    description: "Retrieve the executive summary of a document analysis."));
            }
            else
            {
                _logger.LogWarning("IAnalysisOrchestrationService not available; AnalysisQueryTools will not be registered");
            }

            // KnowledgeRetrievalTools — requires IRagService, accepts knowledge scope for domain filtering
            if (ragService != null)
            {
                var knowledgeRetrievalTools = new KnowledgeRetrievalTools(ragService, tenantId, knowledgeScope, citationContext);
                tools.Add(AIFunctionFactory.Create(
                    knowledgeRetrievalTools.GetKnowledgeSourceAsync,
                    name: "GetKnowledgeSource",
                    description: "Retrieve all indexed content for a specific knowledge source by its ID."));
                tools.Add(AIFunctionFactory.Create(
                    knowledgeRetrievalTools.SearchKnowledgeBaseAsync,
                    name: "SearchKnowledgeBase",
                    description: "Search the knowledge base for reference information relevant to the query."));
            }

            // TextRefinementTools — requires IChatClient
            var textRefinementTools = new TextRefinementTools(_chatClient);
            tools.Add(AIFunctionFactory.Create(
                textRefinementTools.RefineTextAsync,
                name: "RefineText",
                description: "Reformat or improve the clarity of a text passage per the given instruction."));
            tools.Add(AIFunctionFactory.Create(
                textRefinementTools.ExtractKeyPointsAsync,
                name: "ExtractKeyPoints",
                description: "Extract the most important key points from a text passage."));
            tools.Add(AIFunctionFactory.Create(
                textRefinementTools.GenerateSummaryAsync,
                name: "GenerateSummary",
                description: "Generate a concise summary of text in bullet, paragraph, or tldr format."));

            // AnalysisExecutionTools — gated behind "reanalyze" capability (task 079).
            // Requires IAnalysisOrchestrationService + IChatClient.
            // Only available when the playbook declares the "reanalyze" capability, preventing
            // re-analysis from appearing in lightweight playbooks (e.g., "Quick Q&A").
            // Task 080: Now wired with real orchestration — requires httpContext for OBO auth
            // and sseWriter for progress/document_replace SSE events during re-analysis.
            if (capabilities.Contains(PlaybookCapabilities.Reanalyze) && analysisService != null)
            {
                var analysisExecutionTools = new AnalysisExecutionTools(
                    analysisService, _chatClient,
                    analysisId: null,
                    playbookId: playbookId,
                    documentId: documentId,
                    httpContext: httpContext,
                    sseWriter: sseWriter);
                tools.AddRange(analysisExecutionTools.GetTools());
            }

            // WebSearchTools — gated behind "web_search" capability (task 089).
            // Only available when the playbook explicitly enables web search. Many playbooks
            // deal with confidential internal documents and should not reach out to the public
            // internet. The "web_search" capability provides admin control over which contexts
            // allow external web queries (ADR-015: external content governance).
            if (capabilities.Contains(PlaybookCapabilities.WebSearch))
            {
                var webSearchTools = new WebSearchTools(_logger);
                tools.Add(AIFunctionFactory.Create(
                    webSearchTools.SearchWebAsync,
                    name: "SearchWeb",
                    description: "Search the web for information relevant to the user's query. Use when the question cannot be answered from internal documents alone."));
            }

            _logger.LogDebug("Resolved {ToolCount} AIFunction tools for agent session", tools.Count);
            return tools;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve AIFunction tools; agent will run without tools");
            return [];
        }
    }

    /// <summary>
    /// Returns the set of capabilities for a given playbook by querying Dataverse.
    ///
    /// Reads the <c>sprk_playbookcapabilities</c> multi-select choice field from the playbook
    /// record. If the field is empty or the playbook is not found, falls back to all capabilities
    /// (permissive default for backwards compatibility).
    /// </summary>
    /// <param name="serviceProvider">Scoped service provider to resolve IPlaybookService.</param>
    /// <param name="playbookId">The playbook ID to look up capabilities for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A set of capability strings from <see cref="PlaybookCapabilities"/>.</returns>
    private async Task<IReadOnlySet<string>> GetPlaybookCapabilitiesAsync(
        IServiceProvider serviceProvider,
        Guid playbookId,
        CancellationToken cancellationToken)
    {
        try
        {
            var playbookService = serviceProvider.GetRequiredService<IPlaybookService>();
            var playbook = await playbookService.GetPlaybookAsync(playbookId, cancellationToken);

            if (playbook?.Capabilities is { Length: > 0 })
            {
                _logger.LogInformation(
                    "Playbook {PlaybookId} capabilities from Dataverse: [{Capabilities}]",
                    playbookId, string.Join(", ", playbook.Capabilities));
                return new HashSet<string>(playbook.Capabilities);
            }

            _logger.LogInformation(
                "Playbook {PlaybookId} has no capabilities set in Dataverse; using all capabilities as default",
                playbookId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to load capabilities for playbook {PlaybookId}; falling back to all capabilities",
                playbookId);
        }

        return new HashSet<string>(PlaybookCapabilities.All);
    }
}
