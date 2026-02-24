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
            cancellationToken);

        // Resolve registered AIFunction tools from DI, passing knowledge scope
        // so search tools can constrain queries to the playbook's knowledge sources.
        var tools = ResolveTools(scope.ServiceProvider, context.KnowledgeScope);

        _logger.LogInformation(
            "SprkChatAgent created: playbook={PlaybookId}, toolCount={ToolCount}, hasDocSummary={HasDocSummary}",
            playbookId, tools.Count, context.DocumentSummary != null);

        var agentLogger = scope.ServiceProvider.GetRequiredService<ILogger<SprkChatAgent>>();

        ISprkChatAgent agent = new SprkChatAgent(_chatClient, context, tools, agentLogger);

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
    /// </summary>
    /// <param name="scopedProvider">The scoped DI provider for this agent creation call.</param>
    /// <param name="knowledgeScope">
    /// Knowledge scope from the playbook, containing RAG source IDs for search filtering.
    /// Null when the playbook has no knowledge sources configured.
    /// </param>
    /// <returns>List of registered <see cref="AIFunction"/> instances, or empty list on failure.</returns>
    private IReadOnlyList<AIFunction> ResolveTools(
        IServiceProvider scopedProvider,
        ChatKnowledgeScope? knowledgeScope)
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
                var documentSearchTools = new DocumentSearchTools(ragService, knowledgeScope);
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
                var analysisQueryTools = new AnalysisQueryTools(analysisService);
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
                var knowledgeRetrievalTools = new KnowledgeRetrievalTools(ragService, knowledgeScope);
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

            _logger.LogDebug("Resolved {ToolCount} AIFunction tools for agent session", tools.Count);
            return tools;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve AIFunction tools; agent will run without tools");
            return [];
        }
    }
}
