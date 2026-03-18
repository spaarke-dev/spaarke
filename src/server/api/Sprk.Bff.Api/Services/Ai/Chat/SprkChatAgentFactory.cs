using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat.Middleware;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Export;
using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;

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
    private readonly IChatClient _rawChatClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SprkChatAgentFactory> _logger;

    public SprkChatAgentFactory(
        IChatClient chatClient,
        [FromKeyedServices("raw")] IChatClient rawChatClient,
        IServiceProvider serviceProvider,
        ILogger<SprkChatAgentFactory> logger)
    {
        _chatClient = chatClient;
        _rawChatClient = rawChatClient;
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
    /// <param name="latestUserMessage">
    /// The most recent user message text for conversation-aware document chunk re-selection (FR-03).
    /// When provided, <see cref="DocumentContextService"/> uses embedding similarity to select
    /// the most relevant document chunks for this specific question rather than defaulting to
    /// position-based selection. Null on initial session creation or when not applicable.
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
        Guid? playbookId,
        string tenantId,
        ChatHostContext? hostContext = null,
        IReadOnlyList<string>? additionalDocumentIds = null,
        HttpContext? httpContext = null,
        Func<Api.Ai.ChatSseEvent, CancellationToken, Task>? sseWriter = null,
        string? latestUserMessage = null,
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

        // === Document context injection (R2-011, R2-012) ===
        // Factory-instantiate DocumentContextService (ADR-010: NOT DI-registered) and enrich
        // the ChatContext with full document content within the 30K token budget.
        // When multiple document IDs are present (primary + additional), use multi-document
        // aggregation with proportional budget allocation (FR-12).
        // When the document exceeds the budget, conversation-aware re-selection uses
        // embedding similarity to the latest user message (FR-03).
        context = await EnrichWithDocumentContextAsync(
            scope.ServiceProvider, context, documentId, additionalDocumentIds,
            httpContext, latestUserMessage, cancellationToken);

        // === Active Capabilities enrichment (R2-021, FR-11) ===
        // Resolve the command catalog from DynamicCommandResolver and append an
        // "### Active Capabilities" section to the system prompt so the AI model
        // is aware of scope-contributed slash commands.
        try
        {
            var commandResolver = CreateCommandResolver();
            var commands = await commandResolver.ResolveCommandsAsync(
                tenantId, hostContext, cancellationToken);

            var enrichedPrompt = PlaybookChatContextProvider.AppendActiveCapabilities(
                context.SystemPrompt, commands);

            if (!ReferenceEquals(enrichedPrompt, context.SystemPrompt))
            {
                context = context with { SystemPrompt = enrichedPrompt };
                _logger.LogDebug(
                    "Enriched system prompt with Active Capabilities section ({CommandCount} scope commands)",
                    commands.Count(c => !string.Equals(c.Category, "system", StringComparison.OrdinalIgnoreCase)
                                     && !string.Equals(c.Category, "playbook", StringComparison.OrdinalIgnoreCase)));
            }
        }
        catch (Exception ex)
        {
            // Soft failure — Active Capabilities is enhancing, not required
            _logger.LogWarning(ex,
                "Failed to enrich system prompt with Active Capabilities; continuing without");
        }

        // Resolve playbook capabilities from Dataverse to determine which tools should be available.
        // When no playbook is specified (generic chat mode), use all capabilities as default.
        var capabilities = playbookId.HasValue
            ? await GetPlaybookCapabilitiesAsync(scope.ServiceProvider, playbookId.Value, cancellationToken)
            : (IReadOnlySet<string>)new HashSet<string>(PlaybookCapabilities.All);

        // Create a shared CitationContext for search tools to populate with source metadata.
        // This context is passed to DocumentSearchTools and KnowledgeRetrievalTools so they
        // can register citations during tool execution. The SprkChatAgent resets it before
        // each message to keep citation numbering scoped per assistant response.
        var citationContext = new CitationContext();

        // Extract analysisId from AnalysisMetadata for WorkingDocumentTools write-back.
        // This is the sprk_analysisoutput record GUID — populated when SprkChat is launched
        // from the Analysis Workspace with full context (task 002, task 020).
        var analysisId = context.AnalysisMetadata?.GetValueOrDefault("analysisId");

        // Resolve registered AIFunction tools from DI, passing tenant ID, knowledge scope,
        // and playbook capabilities so tools are gated to only those the playbook declares.
        var tools = ResolveTools(
            scope.ServiceProvider, tenantId, context.KnowledgeScope, capabilities,
            playbookId ?? Guid.Empty, documentId, analysisId, httpContext, sseWriter, citationContext);

        _logger.LogInformation(
            "SprkChatAgent created: playbook={PlaybookId}, toolCount={ToolCount}, hasDocSummary={HasDocSummary}",
            playbookId, tools.Count, context.DocumentSummary != null);

        var agentLogger = scope.ServiceProvider.GetRequiredService<ILogger<SprkChatAgent>>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var intentLogger = loggerFactory.CreateLogger<CompoundIntentDetector>();

        ISprkChatAgent agent = new SprkChatAgent(
            _chatClient,
            _rawChatClient,
            context,
            tools,
            citationContext,
            new CompoundIntentDetector(intentLogger),
            agentLogger);

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

    /// <summary>
    /// Factory-instantiates a <see cref="PlaybookDispatcher"/> for the given tenant.
    ///
    /// ADR-010: PlaybookDispatcher is NOT registered in DI — it is created here with
    /// resolved dependencies from the scoped service provider.
    ///
    /// Dependencies resolved from DI:
    ///   - <see cref="SearchIndexClient"/> (singleton) — for PlaybookEmbeddingService
    ///   - <see cref="IOpenAiClient"/> (singleton) — for PlaybookEmbeddingService
    ///   - <see cref="INodeService"/> (scoped) — for output node metadata lookup
    ///   - <see cref="IDistributedCache"/> (singleton) — for result caching (ADR-009)
    /// </summary>
    /// <param name="tenantId">Tenant ID for cache key scoping (ADR-014).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A configured <see cref="PlaybookDispatcher"/> instance.</returns>
    public async Task<PlaybookDispatcher> CreatePlaybookDispatcherAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        // Resolve dependencies for PlaybookEmbeddingService (factory-instantiated)
        var searchIndexClient = scope.ServiceProvider.GetRequiredService<SearchIndexClient>();
        var openAiClient = scope.ServiceProvider.GetRequiredService<IOpenAiClient>();
        var embeddingService = new PlaybookEmbeddingService(
            searchIndexClient,
            openAiClient,
            loggerFactory.CreateLogger<PlaybookEmbeddingService>());

        // Resolve remaining dependencies
        var nodeService = scope.ServiceProvider.GetRequiredService<INodeService>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

        return new PlaybookDispatcher(
            embeddingService,
            _rawChatClient,
            nodeService,
            cache,
            tenantId,
            loggerFactory.CreateLogger<PlaybookDispatcher>());
    }

    /// <summary>
    /// Factory-instantiates a <see cref="DynamicCommandResolver"/> for the given tenant.
    ///
    /// ADR-010: DynamicCommandResolver is NOT registered in DI — it is created here with
    /// resolved dependencies from the scoped service provider.
    ///
    /// Dependencies resolved from DI:
    ///   - <see cref="IGenericEntityService"/> (singleton) — for Dataverse queries
    ///   - <see cref="IDistributedCache"/> (singleton) — for Redis caching (ADR-009)
    /// </summary>
    /// <returns>A configured <see cref="DynamicCommandResolver"/> instance.</returns>
    public DynamicCommandResolver CreateCommandResolver()
    {
        var entityService = _serviceProvider.GetRequiredService<IGenericEntityService>();
        var cache = _serviceProvider.GetRequiredService<IDistributedCache>();
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();

        return new DynamicCommandResolver(
            entityService,
            cache,
            loggerFactory.CreateLogger<DynamicCommandResolver>());
    }

    /// <summary>
    /// Factory-instantiates a <see cref="PlaybookOutputHandler"/> for routing typed playbook outputs.
    ///
    /// ADR-010: PlaybookOutputHandler is NOT registered in DI — it is created here with
    /// resolved dependencies from the scoped service provider.
    ///
    /// Dependencies:
    ///   - <see cref="CompoundIntentDetector"/> (stateless, instantiated directly)
    ///   - <see cref="DocxExportService"/> (resolved from DI via <see cref="IExportService"/>)
    /// </summary>
    /// <returns>A configured <see cref="PlaybookOutputHandler"/> instance.</returns>
    public PlaybookOutputHandler CreatePlaybookOutputHandler()
    {
        using var scope = _serviceProvider.CreateScope();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        var intentDetector = new CompoundIntentDetector(
            loggerFactory.CreateLogger<CompoundIntentDetector>());

        var docxExport = scope.ServiceProvider.GetRequiredService<DocxExportService>();

        return new PlaybookOutputHandler(
            intentDetector,
            docxExport,
            loggerFactory.CreateLogger<PlaybookOutputHandler>());
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
    /// <param name="analysisId">
    /// Optional GUID string of the active <c>sprk_analysisoutput</c> record.
    /// Passed to <see cref="WorkingDocumentTools"/> for write-back target resolution (spec FR-12).
    /// Null when SprkChat is not launched from the Analysis Workspace.
    /// </param>
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
        string? analysisId,
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

            // WorkingDocumentTools — gated behind "write_back" capability (task 073).
            // Requires IAnalysisOrchestrationService + IWorkingDocumentService + IChatClient.
            // Only available when the playbook declares the "write_back" capability, preventing
            // document mutation tools from appearing in read-only playbooks.
            //
            // WriteBackToWorkingDocumentAsync is included here and is listed in
            // CompoundIntentDetector.WriteBackToolNames, ensuring it always triggers the
            // plan preview gate before execution (spec FR-11, FR-12).
            //
            // Note: The document stream SSE writer is stubbed as a no-op for this task (073).
            // Streaming token delivery for EditWorkingDocument and AppendSection will be wired
            // in a follow-up task when the SSE plumbing for DocumentStreamEvent is connected.
            if (capabilities.Contains(PlaybookCapabilities.WriteBack) && analysisService != null)
            {
                var workingDocumentService = scopedProvider.GetService<IWorkingDocumentService>();
                if (workingDocumentService != null)
                {
                    // R2-023: Wire real document stream SSE writer for streaming write-back
                    // content to the client (spec FR-04). The delegate writes DocumentStreamEvent
                    // objects as SSE frames via ChatEndpoints.WriteDocumentStreamSSEAsync.
                    // Falls back to no-op when httpContext is unavailable (background processing).
                    Func<Models.Ai.Chat.DocumentStreamEvent, CancellationToken, Task> documentSSE =
                        httpContext != null
                            ? Api.Ai.ChatEndpoints.CreateDocumentStreamSseWriter(httpContext.Response)
                            : (_, _) => Task.CompletedTask;

                    var workingDocumentTools = new WorkingDocumentTools(
                        _chatClient,
                        documentSSE,
                        analysisService,
                        workingDocumentService,
                        _logger,
                        analysisId);
                    tools.AddRange(workingDocumentTools.GetTools());
                }
                else
                {
                    _logger.LogWarning("IWorkingDocumentService not available; WorkingDocumentTools will not be registered");
                }
            }

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
            //
            // R2-017: Real Bing Web Search v7 API integration with scope-guided search (FR-10),
            // citation generation, and graceful mock fallback when API key is not configured.
            // Factory-instantiated (ADR-010): reads config directly, no new DI registration.
            if (capabilities.Contains(PlaybookCapabilities.WebSearch))
            {
                var httpClientFactory = scopedProvider.GetRequiredService<IHttpClientFactory>();
                var configuration = scopedProvider.GetRequiredService<IConfiguration>();
                var bingApiKey = configuration["BingSearch:ApiKey"];
                var bingEndpoint = configuration["BingSearch:Endpoint"];
                var bingMaxResults = 10;
                if (int.TryParse(configuration["BingSearch:MaxResults"], out var parsedMax))
                    bingMaxResults = parsedMax;

                // ScopeSearchGuidance from ChatKnowledgeScope — populated by R2-020
                // (AnalysisChatContextResolver). Null until R2-020 is complete.
                var scopeSearchGuidance = knowledgeScope?.ScopeSearchGuidance;

                var webSearchTools = new WebSearchTools(
                    _logger, httpClientFactory, citationContext,
                    bingApiKey, bingEndpoint, bingMaxResults, scopeSearchGuidance);
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

    /// <summary>
    /// Factory-instantiates <see cref="DocumentContextService"/> and enriches the
    /// <see cref="ChatContext"/> with full document content within the 30K token budget.
    ///
    /// When <paramref name="additionalDocumentIds"/> is non-empty, uses multi-document
    /// aggregation (R2-012) with proportional budget allocation across all documents.
    /// Otherwise, uses single-document injection (R2-011).
    ///
    /// ADR-010: DocumentContextService is NOT registered in DI — instantiated here with
    /// resolved dependencies from the scoped service provider.
    ///
    /// ADR-007: Document retrieval uses <see cref="ISpeFileOperations"/> facade.
    ///
    /// ADR-015: Document content is NOT logged — only metadata (chunk counts, token usage).
    /// </summary>
    /// <param name="serviceProvider">Scoped DI provider for dependency resolution.</param>
    /// <param name="context">The existing ChatContext to enrich.</param>
    /// <param name="documentId">Dataverse document ID (primary).</param>
    /// <param name="additionalDocumentIds">
    /// Optional additional document IDs for multi-document mode.
    /// When non-empty, all documents (primary + additional) share the 30K token budget.
    /// </param>
    /// <param name="httpContext">HTTP context for OBO auth (may be null).</param>
    /// <param name="latestUserMessage">
    /// The most recent user message for conversation-aware chunk re-selection (FR-03).
    /// Null on initial session creation (position-based selection used).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enriched ChatContext with document content in DocumentSummary, or unchanged on failure.</returns>
    private async Task<ChatContext> EnrichWithDocumentContextAsync(
        IServiceProvider serviceProvider,
        ChatContext context,
        string documentId,
        IReadOnlyList<string>? additionalDocumentIds,
        HttpContext? httpContext,
        string? latestUserMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var documentService = serviceProvider.GetRequiredService<IDocumentDataverseService>();
            var speFileStore = serviceProvider.GetRequiredService<ISpeFileOperations>();
            var textExtractor = serviceProvider.GetRequiredService<ITextExtractor>();
            var openAiClient = serviceProvider.GetRequiredService<IOpenAiClient>();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            var documentContextService = new DocumentContextService(
                documentService,
                speFileStore,
                textExtractor,
                openAiClient,
                loggerFactory.CreateLogger<DocumentContextService>());

            // Multi-document mode: primary + additional documents share the 30K budget
            if (additionalDocumentIds is { Count: > 0 })
            {
                return await EnrichWithMultiDocumentContextAsync(
                    documentContextService, context, documentId, additionalDocumentIds,
                    httpContext, latestUserMessage, cancellationToken);
            }

            // Single-document mode (R2-011)
            var result = await documentContextService.InjectDocumentContextAsync(
                documentId, httpContext, latestUserMessage, cancellationToken);

            if (result.SelectedChunks.Count == 0)
            {
                _logger.LogDebug(
                    "No document content available for {DocumentId}; using existing context",
                    documentId);
                return context;
            }

            // Format document chunks and prepend to existing DocumentSummary.
            // The existing summary (if any) is a short TL;DR — the full document content
            // from DocumentContextService provides much richer context.
            var documentContent = result.FormatForSystemPrompt();
            var enrichedSummary = !string.IsNullOrWhiteSpace(context.DocumentSummary)
                ? $"{documentContent}\n\n---\n**Summary**: {context.DocumentSummary}"
                : documentContent;

            _logger.LogInformation(
                "Enriched context for {DocumentId}: {ChunkCount} chunks, {TokensUsed}/{Budget} tokens, truncated={Truncated}",
                documentId, result.SelectedChunks.Count, result.TotalTokensUsed,
                DocumentContextService.MaxTokenBudget, result.WasTruncated);

            return context with { DocumentSummary = enrichedSummary };
        }
        catch (Exception ex)
        {
            // Soft failure — document context enrichment is enhancing, not required.
            // The agent will still work with the existing playbook context and summary.
            _logger.LogWarning(ex,
                "Failed to enrich context with document content for {DocumentId}; continuing with existing context",
                documentId);
            return context;
        }
    }

    /// <summary>
    /// Enriches the <see cref="ChatContext"/> using multi-document aggregation (R2-012).
    /// Combines the primary document and additional documents into a single list and
    /// delegates to <see cref="DocumentContextService.InjectMultiDocumentContextAsync"/>.
    /// </summary>
    private async Task<ChatContext> EnrichWithMultiDocumentContextAsync(
        DocumentContextService documentContextService,
        ChatContext context,
        string documentId,
        IReadOnlyList<string> additionalDocumentIds,
        HttpContext? httpContext,
        string? latestUserMessage,
        CancellationToken cancellationToken)
    {
        // Combine primary document + additional documents into a single list
        var allDocumentIds = new List<string> { documentId };
        allDocumentIds.AddRange(additionalDocumentIds.Where(id => !string.IsNullOrWhiteSpace(id)));

        _logger.LogInformation(
            "Multi-document context enrichment: {DocumentCount} documents (primary={PrimaryDocId})",
            allDocumentIds.Count, documentId);

        var result = await documentContextService.InjectMultiDocumentContextAsync(
            allDocumentIds, httpContext, latestUserMessage, cancellationToken);

        if (result.MergedChunks.Count == 0)
        {
            _logger.LogDebug(
                "No content available from {DocumentCount} documents; using existing context",
                allDocumentIds.Count);
            return context;
        }

        // Format multi-document chunks with attribution headers
        var documentContent = result.FormatForSystemPrompt();
        var enrichedSummary = !string.IsNullOrWhiteSpace(context.DocumentSummary)
            ? $"{documentContent}\n\n---\n**Summary**: {context.DocumentSummary}"
            : documentContent;

        _logger.LogInformation(
            "Multi-document enrichment complete: {DocumentCount} documents, " +
            "{MergedChunkCount} merged chunks, {TokensUsed}/{Budget} tokens, anyTruncated={AnyTruncated}",
            result.DocumentGroups.Count, result.MergedChunks.Count, result.TotalTokensUsed,
            DocumentContextService.MaxTokenBudget, result.AnyTruncated);

        return context with { DocumentSummary = enrichedSummary };
    }
}
