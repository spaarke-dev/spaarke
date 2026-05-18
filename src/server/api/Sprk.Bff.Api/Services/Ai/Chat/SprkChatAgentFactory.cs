using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Sprk.Bff.Api.Services.Ai.Chat.Middleware;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Export;
using Sprk.Bff.Api.Services.Ai.Safety.Citations;
using Sprk.Bff.Api.Services.Ai.Foundry;
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

    // ── AIPU2-061: Per-turn capability routing ────────────────────────────────
    // ICapabilityRouter is a singleton (in-memory keyword + LLM classifier).
    // Injected here so CreateAgentAsync can call RouteAsync before tool resolution.
    // When null (pre-AIPU2-010 environments), the factory falls back to the existing
    // static tool resolution path (backward-compatible).
    private readonly ICapabilityRouter? _capabilityRouter;

    public SprkChatAgentFactory(
        IChatClient chatClient,
        [FromKeyedServices("raw")] IChatClient rawChatClient,
        IServiceProvider serviceProvider,
        ILogger<SprkChatAgentFactory> logger,
        ICapabilityRouter? capabilityRouter = null)
    {
        _chatClient = chatClient;
        _rawChatClient = rawChatClient;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _capabilityRouter = capabilityRouter;
    }

    /// <summary>
    /// Creates a <see cref="SprkChatAgent"/> for the given session parameters.
    ///
    /// A new agent instance is returned on every call.  Callers (e.g. ChatSessionManager)
    /// are responsible for caching the agent for the duration of a session and replacing it
    /// when a context switch occurs (different document or playbook).
    ///
    /// AIPU2-061: Per-turn tool injection via CapabilityRouter.
    /// When <paramref name="latestUserMessage"/> is provided and <see cref="ICapabilityRouter"/> is
    /// registered, the factory calls <c>RouteAsync</c> to select the minimal tool set for the turn,
    /// then validates those capabilities via <see cref="ICapabilityValidator"/>.  Only tools whose
    /// capability appears in the validated set are injected into the agent.  If routing produces no
    /// confident result (Layer 3 fallback), the full backward-compatible tool set is used.
    /// A <c>capability_change</c> SSE event is emitted when the routed tool set differs from the
    /// <paramref name="previousTurnToolNames"/> set passed by the caller.
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
    /// Optional SSE writer delegate for out-of-band events (progress, document_replace,
    /// capability_change). Used by tools and by AIPU2-061 to emit <c>capability_change</c>
    /// events when the per-turn tool set differs from the previous turn.
    /// Null when SSE is not available.
    /// </param>
    /// <param name="latestUserMessage">
    /// The most recent user message text. Used for:
    ///   1. Conversation-aware document chunk re-selection (FR-03).
    ///   2. AIPU2-061: Per-turn capability routing — passed to CapabilityRouter.RouteAsync
    ///      to classify intent and select the minimal tool set for this turn.
    /// Null on initial session creation or when not applicable (falls back to full tool set).
    /// </param>
    /// <param name="previousTurnToolNames">
    /// AIPU2-061: Names of tools that were active in the previous turn (from the caller's
    /// session state). When provided, a <c>capability_change</c> SSE event is emitted if
    /// the current turn's routed tool set differs. Null on the first turn (no comparison).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A fully configured <see cref="ISprkChatAgent"/> ready to receive messages.
    /// The returned agent is wrapped with the middleware pipeline (AIPL-057, AIPU-072):
    /// ContentSafety (innermost) -> CostControl -> Telemetry -> Routing (outermost).
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
        IReadOnlyList<string>? previousTurnToolNames = null,
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
        // When no playbook is specified (generic/standalone chat mode), use core capabilities only.
        // This prevents tools with unconfigured dependencies (LegalResearch, CodeInterpreter)
        // from crashing the entire tool pipeline when their options aren't set.
        var capabilities = playbookId.HasValue
            ? await GetPlaybookCapabilitiesAsync(scope.ServiceProvider, playbookId.Value, cancellationToken)
            : (IReadOnlySet<string>)new HashSet<string>(PlaybookCapabilities.CoreCapabilities);

        // === AIPU2-061: Per-turn capability routing via CapabilityRouter ===
        // When a user message and the capability router are available, run the three-tier router
        // to select the minimal tool set for this specific turn rather than injecting the full
        // capability-gated set every time.  The routing result drives tool resolution below.
        //
        // Routing pipeline:
        //   1. RouteAsync(userMessage, playbookName, ct)   → CapabilityRoutingResult
        //   2. ICapabilityValidator.FilterAsync(candidates) → removes kill-switch / tenant / role
        //   3. ResolveTools with routing result            → only tools for this turn's capabilities
        //   4. Emit capability_change SSE if tool set differs from previous turn (FR-801)
        //
        // Fallback: when the router is unavailable or routing produces no tools (Layer 3 with
        // empty superset), fall back to the full playbook-capabilities-gated tool set so no
        // regression occurs on environments that have not yet deployed AiCapabilitiesModule.
        CapabilityRoutingResult? routingResult = null;
        IReadOnlySet<string>? routedCapabilities = null;

        if (_capabilityRouter is not null && !string.IsNullOrWhiteSpace(latestUserMessage))
        {
            try
            {
                // Derive the active playbook name from the context if available.
                // PlaybookChatContextProvider populates SystemPrompt with the playbook name
                // but there's no dedicated field — pass null when not resolvable.
                // Future: AIPU2-013/014 may add PlaybookName to ChatContext.
                var activePlaybookName = context.PlaybookId.HasValue
                    ? context.PlaybookId.Value.ToString("N")
                    : null;

                routingResult = await _capabilityRouter
                    .RouteAsync(latestUserMessage, activePlaybookName, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "AIPU2-061: CapabilityRouter result — session={SessionId}, layer={Layer}, " +
                    "confident={IsConfident}, capabilities=[{Capabilities}], toolNames=[{ToolNames}]",
                    sessionId,
                    routingResult.Layer,
                    routingResult.IsConfident,
                    string.Join(",", routingResult.SelectedCapabilities),
                    string.Join(",", routingResult.SelectedToolNames));

                // Validate the router-selected capabilities: apply kill-switch, tenant,
                // permission, and context checks via ICapabilityValidator.
                // ICapabilityValidator is scoped — resolve from the per-request scope.
                if (routingResult.SelectedCapabilities.Length > 0)
                {
                    var validator = scope.ServiceProvider.GetService<ICapabilityValidator>();
                    if (validator is not null)
                    {
                        var manifest = scope.ServiceProvider.GetService<ICapabilityManifest>();
                        if (manifest is not null)
                        {
                            // Build candidate list from router-selected capability names.
                            var candidates = routingResult.SelectedCapabilities
                                .Select(name =>
                                {
                                    manifest.TryGet(name, out var entry);
                                    return entry;
                                })
                                .OfType<CapabilityManifestEntry>()
                                .ToList();

                            if (candidates.Count > 0)
                            {
                                // Build validation context from available request data.
                                // ClaimsPrincipal is not available in the factory (factory is
                                // singleton; httpContext carries the principal per-request).
                                var principal = httpContext?.User
                                    ?? new System.Security.Claims.ClaimsPrincipal();
                                var tenantEnvUrl = $"https://{tenantId}.crm.dynamics.com";
                                var convContext = new Dictionary<string, string>(
                                    StringComparer.OrdinalIgnoreCase);

                                var validationCtx = new CapabilityValidationContext(
                                    User: principal,
                                    TenantEnvironmentUrl: tenantEnvUrl,
                                    ConversationContext: convContext);

                                var validated = await validator
                                    .FilterAsync(candidates, validationCtx, cancellationToken)
                                    .ConfigureAwait(false);

                                // Build the routed capability set intersected with the
                                // playbook capabilities (belt-and-suspenders security gate).
                                routedCapabilities = new HashSet<string>(
                                    validated.Select(e => e.CapabilityName)
                                             .Where(c => capabilities.Contains(c)),
                                    StringComparer.OrdinalIgnoreCase);

                                _logger.LogDebug(
                                    "AIPU2-061: validated routed capabilities=[{Capabilities}]",
                                    string.Join(",", routedCapabilities));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Soft failure — routing is enhancing, not required.
                // Fall through to the existing full-capability tool set.
                _logger.LogWarning(ex,
                    "AIPU2-061: CapabilityRouter failed for session={SessionId}; " +
                    "falling back to full playbook capability set",
                    sessionId);
                routingResult = null;
                routedCapabilities = null;
            }
        }
        // === End AIPU2-061 routing ===

        // Create a shared CitationContext for search tools to populate with source metadata.
        // This context is passed to DocumentSearchTools and KnowledgeRetrievalTools so they
        // can register citations during tool execution. The SprkChatAgent resets it before
        // each message to keep citation numbering scoped per assistant response.
        var citationContext = new CitationContext();

        // Extract analysisId from AnalysisMetadata for WorkingDocumentTools write-back.
        // This is the sprk_analysisoutput record GUID — populated when SprkChat is launched
        // from the Analysis Workspace with full context (task 002, task 020).
        var analysisId = context.AnalysisMetadata?.GetValueOrDefault("analysisId");

        // Resolve AIFunction tools.
        // AIPU2-061: when a validated routed capability set is available, pass the routing
        // result so ResolveTools restricts to only the capabilities selected for this turn.
        // Otherwise fall back to the full playbook capability set (backward compatible).
        var effectiveCapabilities = routedCapabilities ?? capabilities;
        var tools = ResolveTools(
            scope.ServiceProvider, tenantId, context.KnowledgeScope, effectiveCapabilities,
            playbookId ?? Guid.Empty, documentId, analysisId, httpContext, sseWriter, citationContext,
            routingResult);

        // === AIPU2-061: capability_change SSE event ===
        // Emit when the routed tool set for this turn differs from the previous turn's tool set.
        // This notifies the client (FR-801) that the active capability profile has changed so
        // the UI can update affordances (e.g., hide/show tool pills in the chat bar).
        if (sseWriter is not null && previousTurnToolNames is not null)
        {
            await EmitCapabilityChangesIfDifferentAsync(
                tools, previousTurnToolNames, sseWriter, cancellationToken)
                .ConfigureAwait(false);
        }

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

        // === Middleware pipeline (AIPL-057, AIPU-072) ===
        // Wrap order: ContentSafety (innermost) -> CostControl -> Telemetry -> Routing (outermost).
        // The outermost middleware (Routing) executes first on each call and decides which backend
        // handles the request before the inner pipeline ever sees the message.
        agent = WrapWithMiddleware(agent, tenantId);

        return agent;
    }

    /// <summary>
    /// Wraps the given agent with the middleware pipeline (AIPL-057, AIPU-072).
    ///
    /// Pipeline order (inside-out):
    ///   1. ContentSafety — filters PII from response tokens (innermost)
    ///   2. CostControl   — enforces session token budget
    ///   3. Telemetry      — logs metadata: latency, token count, playbook
    ///   4. Routing        — classifies intent and routes to Agent Service or direct pipeline (outermost)
    ///
    /// No new DI registrations are added (ADR-010 constraint: middleware is instantiated
    /// directly by the factory, same as tool classes).
    ///
    /// Routing middleware is only added when <see cref="AgentServiceClient"/> is resolvable
    /// from DI (i.e., when Analysis:Enabled = true in AnalysisServicesModule). When unavailable,
    /// the pipeline is identical to the pre-AIPU-072 pipeline.
    /// </summary>
    /// <param name="agent">The inner agent to wrap.</param>
    /// <param name="tenantId">Tenant ID for Agent Service thread scoping (ADR-014).</param>
    private ISprkChatAgent WrapWithMiddleware(ISprkChatAgent agent, string tenantId)
    {
        // 1. Content safety (innermost — filters before other middleware processes tokens)
        agent = new AgentContentSafetyMiddleware(
            agent,
            _logger);

        // 2. Cost control (checks budget, counts tokens)
        agent = new AgentCostControlMiddleware(
            agent,
            _logger);

        // 3. Telemetry (records total latency including all inner middleware)
        agent = new AgentTelemetryMiddleware(
            agent,
            _logger);

        // 4. Routing (outermost — intercepts each message first and decides which backend handles it)
        // Resolved lazily from IServiceProvider so that the factory remains constructible even
        // when AgentServiceClient is not registered (Analysis:Enabled = false).
        // ADR-010: factory-instantiated, no additional DI registration.
        // ADR-018: kill switch (AgentService:Enabled=false) causes silent fallback inside the middleware.
        var agentServiceClient = _serviceProvider.GetService<AgentServiceClient>();
        var agentServiceOptions = _serviceProvider.GetService<IOptions<AgentServiceOptions>>();
        if (agentServiceClient is not null && agentServiceOptions is not null)
        {
            agent = new AgentServiceRoutingMiddleware(
                agent,
                agentServiceClient,
                agentServiceOptions,
                _logger,
                tenantId);
        }

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
    ///
    /// AIPU2-061: When <paramref name="routingResult"/> is provided and confident (Layer 1 or 2),
    /// only tools whose names appear in the router-selected tool set are included. This implements
    /// the per-turn tool injection contract: the LLM sees only the minimal tool set for the
    /// classified intent, reducing token cost and hallucination risk.
    /// When <paramref name="routingResult"/> is null, uncertain, or a Layer 3 fallback, all tools
    /// enabled by <paramref name="capabilities"/> are included (backward-compatible behaviour).
    /// </summary>
    /// <param name="scopedProvider">The scoped DI provider for this agent creation call.</param>
    /// <param name="tenantId">Tenant ID from the authenticated session — injected into tool constructors (ADR-014).</param>
    /// <param name="knowledgeScope">
    /// Knowledge scope from the playbook, containing RAG source IDs for search filtering.
    /// Null when the playbook has no knowledge sources configured.
    /// </param>
    /// <param name="capabilities">
    /// Effective capability set for this turn: either the playbook capabilities (full set)
    /// or the router-validated subset (per-turn minimum). Tools gated behind a capability
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
    /// <param name="routingResult">
    /// AIPU2-061: Optional routing result from <see cref="ICapabilityRouter.RouteAsync"/>.
    /// When provided and confident (Layer 1 or 2), tools are post-filtered so that only those
    /// whose AIFunction name appears in <see cref="CapabilityRoutingResult.SelectedToolNames"/>
    /// or in the capabilities' tool name lists are included. Null = full set (backward compat).
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
        CitationContext? citationContext,
        CapabilityRoutingResult? routingResult = null)
    {
        // Resolve services that tool classes depend on from DI.
        // IRagService and IAnalysisOrchestrationService are registered in Program.cs.
        // IChatClient is registered in AiModule.cs (AIPL-050).
        var ragService = scopedProvider.GetService<IRagService>();
        var analysisService = scopedProvider.GetService<IAnalysisOrchestrationService>();

        var tools = new List<AIFunction>();

        // Per-tool error isolation (AIPU2-063): each tool group is wrapped in its own
        // try-catch so that a failure in one group (constructor throws, missing config,
        // transient dependency fault) never prevents other healthy tools from resolving.
        // Failed groups are logged as warnings and excluded from the returned tool list.
        // The agent executes normally with whatever subset of tools resolved successfully —
        // an empty tool list is a valid (if degraded) operating state.
        int attempted = 0;
        int resolved = 0;
        var failedTools = new List<string>();

        // --- DocumentSearchTools ---
        // Requires IRagService, accepts knowledge scope for domain filtering.
        // sseWriter is forwarded so both search methods can emit output_pane SSE events with
        // structured SearchResults widget data alongside their text responses (Gap 1 fix).
        attempted++;
        if (ragService != null)
        {
            try
            {
                var documentSearchTools = new DocumentSearchTools(ragService, tenantId, knowledgeScope, citationContext, sseWriter);
                tools.Add(AIFunctionFactory.Create(
                    documentSearchTools.SearchDocumentsAsync,
                    name: "SearchDocuments",
                    description: "Search the knowledge index for document content relevant to the user's query."));
                tools.Add(AIFunctionFactory.Create(
                    documentSearchTools.SearchDiscoveryAsync,
                    name: "SearchDiscovery",
                    description: "Perform a broad discovery search across all indexed documents for the tenant."));
                resolved++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve DocumentSearchTools — skipping");
                failedTools.Add(nameof(DocumentSearchTools));
            }
        }
        else
        {
            _logger.LogWarning("IRagService not available; DocumentSearchTools will not be registered");
            failedTools.Add(nameof(DocumentSearchTools));
        }

        // --- AnalysisQueryTools ---
        // Requires IAnalysisOrchestrationService.
        attempted++;
        if (analysisService != null)
        {
            try
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
                resolved++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve AnalysisQueryTools — skipping");
                failedTools.Add(nameof(AnalysisQueryTools));
            }
        }
        else
        {
            _logger.LogWarning("IAnalysisOrchestrationService not available; AnalysisQueryTools will not be registered");
            failedTools.Add(nameof(AnalysisQueryTools));
        }

        // --- KnowledgeRetrievalTools ---
        // Requires IRagService, accepts knowledge scope for domain filtering.
        // sseWriter is forwarded so GetKnowledgeSourceAsync can emit source_pane SSE events with
        // structured DocumentViewer widget data alongside the text response (Gap 1 fix).
        attempted++;
        if (ragService != null)
        {
            try
            {
                var knowledgeRetrievalTools = new KnowledgeRetrievalTools(ragService, tenantId, knowledgeScope, citationContext, sseWriter);
                tools.Add(AIFunctionFactory.Create(
                    knowledgeRetrievalTools.GetKnowledgeSourceAsync,
                    name: "GetKnowledgeSource",
                    description: "Retrieve all indexed content for a specific knowledge source by its ID."));
                tools.Add(AIFunctionFactory.Create(
                    knowledgeRetrievalTools.SearchKnowledgeBaseAsync,
                    name: "SearchKnowledgeBase",
                    description: "Search the knowledge base for reference information relevant to the query."));
                resolved++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve KnowledgeRetrievalTools — skipping");
                failedTools.Add(nameof(KnowledgeRetrievalTools));
            }
        }
        else
        {
            _logger.LogDebug("IRagService not available; KnowledgeRetrievalTools will not be registered");
            failedTools.Add(nameof(KnowledgeRetrievalTools));
        }

        // --- TextRefinementTools ---
        // Requires IChatClient (always available — constructor arg, not DI lookup).
        attempted++;
        try
        {
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
            resolved++;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve TextRefinementTools — skipping");
            failedTools.Add(nameof(TextRefinementTools));
        }

        // --- WorkingDocumentTools ---
        // Gated behind "write_back" capability (task 073).
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
            attempted++;
            try
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
                    resolved++;
                }
                else
                {
                    _logger.LogWarning("IWorkingDocumentService not available; WorkingDocumentTools will not be registered");
                    failedTools.Add(nameof(WorkingDocumentTools));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve WorkingDocumentTools — skipping");
                failedTools.Add(nameof(WorkingDocumentTools));
            }
        }

        // --- AnalysisExecutionTools ---
        // Gated behind "reanalyze" capability (task 079).
        // Requires IAnalysisOrchestrationService + IChatClient.
        // Only available when the playbook declares the "reanalyze" capability, preventing
        // re-analysis from appearing in lightweight playbooks (e.g., "Quick Q&A").
        // Task 080: Now wired with real orchestration — requires httpContext for OBO auth
        // and sseWriter for progress/document_replace SSE events during re-analysis.
        if (capabilities.Contains(PlaybookCapabilities.Reanalyze) && analysisService != null)
        {
            attempted++;
            try
            {
                var analysisExecutionTools = new AnalysisExecutionTools(
                    analysisService, _chatClient,
                    analysisId: null,
                    playbookId: playbookId,
                    documentId: documentId,
                    httpContext: httpContext,
                    sseWriter: sseWriter);
                tools.AddRange(analysisExecutionTools.GetTools());
                resolved++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve AnalysisExecutionTools — skipping");
                failedTools.Add(nameof(AnalysisExecutionTools));
            }
        }

        // --- WebSearchTools ---
        // Gated behind "web_search" capability (task 089).
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
            attempted++;
            try
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
                resolved++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve WebSearchTools — skipping");
                failedTools.Add(nameof(WebSearchTools));
            }
        }

        // --- CodeInterpreterTools ---
        // Gated behind "code_interpreter" capability (AIPU-070).
        // Only available when the playbook explicitly enables sandbox code execution.
        // Data governance (ADR-015): tools only accept caller-supplied data excerpts.
        // Kill switch (ADR-018): CodeInterpreterOptions.Enabled checked before every invocation.
        // Rate limiting (ADR-016): static SemaphoreSlim bounded by MaxConcurrency.
        // Factory-instantiated (ADR-010): CodeInterpreterBridge resolved from DI; no new registration.
        if (capabilities.Contains(PlaybookCapabilities.CodeInterpreter))
        {
            attempted++;
            try
            {
                var codeInterpreterBridge = scopedProvider.GetService<CodeInterpreterBridge>();
                var codeInterpreterOptions = scopedProvider.GetService<IOptions<CodeInterpreterOptions>>();
                if (codeInterpreterBridge != null && codeInterpreterOptions != null)
                {
                    var codeInterpreterTools = new CodeInterpreterTools(
                        codeInterpreterBridge, codeInterpreterOptions, _logger, citationContext);
                    tools.Add(AIFunctionFactory.Create(
                        codeInterpreterTools.AnalyzeDataAsync,
                        name: "AnalyzeData",
                        description: "Analyze tabular or CSV data to answer a specific question. Use when the user wants statistics, trends, or comparisons derived from structured data."));
                    tools.Add(AIFunctionFactory.Create(
                        codeInterpreterTools.GenerateChartAsync,
                        name: "GenerateChart",
                        description: "Generate a chart image (bar, line, or pie) from a JSON data series. Use when the user wants a visual chart from structured data."));
                    resolved++;
                }
                else
                {
                    _logger.LogWarning("CodeInterpreterBridge or CodeInterpreterOptions not available; CodeInterpreterTools will not be registered");
                    failedTools.Add(nameof(CodeInterpreterTools));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve CodeInterpreterTools — skipping");
                failedTools.Add(nameof(CodeInterpreterTools));
            }
        }

        // --- LegalResearchTools ---
        // Gated behind "legal_research" capability (AIPU-071).
        // Only available when the playbook explicitly enables legal research. Legal research
        // tools invoke Azure AI Foundry Bing Grounding (not the Bing Web Search REST API),
        // requiring both the AgentServiceClient and BingGroundingOptions to be configured.
        //
        // ADR-015 CRITICAL: Legal queries may contain client names, matter references, and PII.
        // QuerySanitizer.Sanitize() is applied inside each tool method before the query is
        // forwarded to Bing. Only query length, result count, and timing are logged.
        //
        // ADR-018: BingGroundingOptions.Enabled kill switch is checked inside each tool method;
        // when disabled, a user-readable string is returned immediately without any network call.
        //
        // Factory-instantiated (ADR-010): no new DI registration; AgentServiceClient and
        // IOptions<BingGroundingOptions> are resolved from the scoped DI provider.
        if (capabilities.Contains(PlaybookCapabilities.LegalResearch))
        {
            attempted++;
            try
            {
                var agentServiceClient = scopedProvider.GetService<AgentServiceClient>();
                var bingGroundingOptions = scopedProvider.GetService<IOptions<BingGroundingOptions>>();

                if (agentServiceClient != null && bingGroundingOptions != null)
                {
                    var legalResearchTools = new LegalResearchTools(
                        agentServiceClient,
                        bingGroundingOptions,
                        _logger,
                        citationContext);

                    tools.Add(AIFunctionFactory.Create(
                        legalResearchTools.ResearchLegalAsync,
                        name: "ResearchLegal",
                        description: "Research a broad legal topic, doctrine, statute, or regulatory requirement " +
                                     "using authoritative public legal sources. Do not include client names or matter references."));

                    tools.Add(AIFunctionFactory.Create(
                        legalResearchTools.LookupCaseAsync,
                        name: "LookupCase",
                        description: "Look up a specific legal case by its standard citation (e.g., 123 F.3d 456 " +
                                     "(9th Cir. 2020)). Returns the case holding and a source URL from an authoritative legal database."));
                    resolved++;
                }
                else
                {
                    _logger.LogWarning(
                        "LegalResearchTools requires AgentServiceClient and BingGroundingOptions — " +
                        "one or both are not registered. LegalResearchTools will not be available. " +
                        "Ensure AgentService and BingGrounding configuration sections are present.");
                    failedTools.Add(nameof(LegalResearchTools));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve LegalResearchTools — skipping");
                failedTools.Add(nameof(LegalResearchTools));
            }
        }

        // --- VerifyCitationsTool ---
        // Gated behind "verify_citations" capability (AIPU2-024).
        // Exposes the "verify_citations" AI function so the LLM can verify legal citations
        // when the user explicitly asks to check references, case validity, or regulatory
        // citations in a passage of text.
        //
        // The automatic post-LLM citation check (CitationSafetyCheck) runs unconditionally
        // after every response regardless of this capability — this gate only controls whether
        // the LLM can explicitly invoke the tool during a turn.
        //
        // Requires ICitationVerificationService (singleton registered in AiSafetyModule).
        // Factory-instantiated (ADR-010): no new DI registration.
        if (capabilities.Contains(PlaybookCapabilities.VerifyCitations))
        {
            attempted++;
            try
            {
                var citationVerificationService = scopedProvider.GetService<ICitationVerificationService>();
                if (citationVerificationService != null)
                {
                    var verifyCitationsTool = new VerifyCitationsTool(citationVerificationService, _logger);
                    tools.Add(AIFunctionFactory.Create(
                        verifyCitationsTool.VerifyCitationsAsync,
                        name: "verify_citations",
                        description: "Verifies legal citations found in the provided text against authoritative sources. " +
                                     "Returns verification status, confidence, and source URLs for each citation. " +
                                     "Use when the user asks to verify references, check case validity, or confirm " +
                                     "regulatory citations."));
                    resolved++;
                }
                else
                {
                    _logger.LogWarning(
                        "VerifyCitationsTool requires ICitationVerificationService — service not registered. " +
                        "Ensure AddAiSafetyModule is called before AddAiChatModule.");
                    failedTools.Add(nameof(VerifyCitationsTool));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve VerifyCitationsTool — skipping");
                failedTools.Add(nameof(VerifyCitationsTool));
            }
        }

        // Summary log: resolved vs. attempted so operators can detect partial degradation
        // without grepping individual warning entries.
        if (failedTools.Count > 0)
        {
            _logger.LogWarning(
                "Tool resolution partial: {ResolvedGroups}/{AttemptedGroups} tool groups resolved. " +
                "Failed groups: [{FailedTools}]. Agent will execute with {ToolCount} AIFunction(s).",
                resolved, attempted, string.Join(", ", failedTools), tools.Count);
        }
        else
        {
            _logger.LogDebug(
                "Tool resolution complete: {ResolvedGroups}/{AttemptedGroups} tool groups resolved, " +
                "{ToolCount} AIFunction(s) registered.",
                resolved, attempted, tools.Count);
        }

        // === AIPU2-061: Per-turn tool filtering by routing result ===
        // When the capability router produced a confident result (Layer 1 or 2), apply a
        // post-filter so the agent only receives the tools selected for this specific turn.
        //
        // Filtering uses the union of:
        //   (a) CapabilityRoutingResult.SelectedToolNames — explicit tool names from Layer 3
        //       superset (populated by Layer 3 only; Layers 1 and 2 leave this empty).
        //   (b) The tool names listed in each selected capability's manifest entry
        //       (populated by Layers 1 and 2 via SelectedCapabilities → ToolNames lookup).
        //
        // Layer 3 fallback (IsConfident = false, SelectedToolNames may be non-empty):
        //   SelectedToolNames carries the broad superset; filter by that list when non-empty.
        //   When SelectedToolNames is also empty (empty manifest), return full set unchanged.
        //
        // Backward compat: when routingResult is null, skip filtering entirely.
        if (routingResult is not null)
        {
            var allowedToolNames = BuildAllowedToolNames(routingResult, scopedProvider);
            if (allowedToolNames.Count > 0)
            {
                var filtered = tools
                    .Where(t => allowedToolNames.Contains(t.Name ?? string.Empty))
                    .ToList();

                _logger.LogDebug(
                    "AIPU2-061: per-turn tool filter applied — " +
                    "before={Before}, after={After}, layer={Layer}, confident={Confident}",
                    tools.Count, filtered.Count, routingResult.Layer, routingResult.IsConfident);

                tools = filtered;
            }
            else
            {
                // Empty allowed set means routing was uncertain (Layer 3 with empty manifest).
                // Return the full capability-gated set unchanged (backward compatible).
                _logger.LogDebug(
                    "AIPU2-061: routing produced empty tool filter — returning full capability set ({Count} tools)",
                    tools.Count);
            }
        }
        // === End AIPU2-061 ===

        return tools;
    }

    /// <summary>
    /// AIPU2-061: Builds the set of AIFunction tool names that are permitted for this turn
    /// based on the capability routing result.
    ///
    /// Resolution order:
    ///   1. If the routing result has <see cref="CapabilityRoutingResult.SelectedToolNames"/>
    ///      (Layer 3 superset), use those directly.
    ///   2. Otherwise expand <see cref="CapabilityRoutingResult.SelectedCapabilities"/> to tool
    ///      names by looking up each capability in the <see cref="ICapabilityManifest"/>.
    ///   3. If neither produces a non-empty set, return empty — caller uses full set.
    ///
    /// Returns an empty set when routing produced no confident tool selection (full-set fallback).
    /// </summary>
    private HashSet<string> BuildAllowedToolNames(
        CapabilityRoutingResult routingResult,
        IServiceProvider scopedProvider)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Layer 3 superset: SelectedToolNames is pre-computed by ComputeLayer3Superset.
        if (routingResult.SelectedToolNames.Length > 0)
        {
            foreach (var toolName in routingResult.SelectedToolNames)
            {
                if (!string.IsNullOrWhiteSpace(toolName))
                    allowed.Add(toolName);
            }
            return allowed;
        }

        // Layers 1 and 2: expand capability names to tool names via the manifest.
        if (routingResult.SelectedCapabilities.Length > 0)
        {
            var manifest = scopedProvider.GetService<ICapabilityManifest>();
            if (manifest is not null)
            {
                foreach (var capName in routingResult.SelectedCapabilities)
                {
                    if (manifest.TryGet(capName, out var entry) && entry is not null)
                    {
                        foreach (var toolName in entry.ToolNames)
                        {
                            if (!string.IsNullOrWhiteSpace(toolName))
                                allowed.Add(toolName);
                        }
                    }
                    else
                    {
                        _logger.LogDebug(
                            "AIPU2-061: routing selected capability '{CapabilityName}' " +
                            "not found in manifest — skipping tool name expansion.",
                            capName);
                    }
                }
            }
        }

        return allowed;
    }

    /// <summary>
    /// AIPU2-061: Emits <c>capability_change</c> SSE events when the current turn's tool set
    /// differs from the previous turn's tool set.
    ///
    /// Emits one event per tool that was added or removed:
    ///   - Added tool   → status "available"
    ///   - Removed tool → status "unavailable"
    ///
    /// This satisfies the FR-801 contract: clients can update affordances (tool pills, etc.)
    /// in real time when the active capability profile changes between turns.
    ///
    /// ADR-015: only tool names are emitted — no user message content.
    /// </summary>
    private async Task EmitCapabilityChangesIfDifferentAsync(
        IReadOnlyList<AIFunction> currentTools,
        IReadOnlyList<string> previousToolNames,
        Func<Api.Ai.ChatSseEvent, CancellationToken, Task> sseWriter,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentNames = new HashSet<string>(
                currentTools.Select(t => t.Name ?? string.Empty).Where(n => n.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            var previousNames = new HashSet<string>(
                previousToolNames.Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);

            if (currentNames.SetEquals(previousNames))
                return; // No change — skip event emission.

            _logger.LogDebug(
                "AIPU2-061: tool set changed between turns — emitting capability_change events. " +
                "Previous=[{Prev}], Current=[{Curr}]",
                string.Join(",", previousNames),
                string.Join(",", currentNames));

            // Emit "available" for tools newly present this turn.
            foreach (var added in currentNames.Except(previousNames, StringComparer.OrdinalIgnoreCase))
            {
                // Use anonymous object for the Data payload — ChatSseEvent.Data is object?.
                // The SSE serialiser (WriteChatSSEAsync in ChatEndpoints) serialises via
                // System.Text.Json which handles anonymous types correctly.
                var payload = new { capability = added, status = "available" };

                await sseWriter(
                    new Api.Ai.ChatSseEvent("capability_change", null, payload),
                    cancellationToken)
                    .ConfigureAwait(false);
            }

            // Emit "unavailable" for tools absent this turn.
            foreach (var removed in previousNames.Except(currentNames, StringComparer.OrdinalIgnoreCase))
            {
                var payload = new { capability = removed, status = "unavailable" };

                await sseWriter(
                    new Api.Ai.ChatSseEvent("capability_change", null, payload),
                    cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Soft failure — SSE event emission must never break agent creation.
            _logger.LogWarning(ex,
                "AIPU2-061: failed to emit capability_change SSE events; continuing without");
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
