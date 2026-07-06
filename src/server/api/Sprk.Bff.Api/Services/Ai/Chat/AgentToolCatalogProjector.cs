using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// The agent-turn loop's tool-catalog resolution component (FR-P2-01,
/// spaarke-ai-architecture-redesign-r1 task 030). Owns what used to be
/// <c>SprkChatAgentFactory.ResolveTools</c> — the capability-gated legacy tool
/// groups plus the data-driven <c>sprk_analysistool</c> projection (FR-11 /
/// ToolHandlerToAIFunctionAdapter) — extracted verbatim so the factory shrinks
/// to prompt/context assembly while the closed-catalog tool projection gets a
/// single owned home (ADR-039: the catalogs are the ONLY tool source).
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-010</b>: factory-instantiated per agent creation (no DI registration);
/// all dependencies are resolved from the caller's scoped provider exactly as
/// before the extraction.
/// </para>
/// <para>
/// The moved implementation is byte-preserved apart from: (1) the method rename
/// <c>ResolveTools → ResolveToolsAsync</c>, (2) <c>_chatClient</c>/<c>_logger</c>
/// becoming ctor-injected fields, and (3) the dynamic invoke_playbook
/// description building via the <c>_invokePlaybookDescriptionFactory</c> delegate
/// (the D-A-14 tenant-menu rendering stays on the factory, which owns the
/// playbook-listing dependencies).
/// </para>
/// </remarks>
internal sealed class AgentToolCatalogProjector
{
    private readonly IChatClient _chatClient;
    private readonly ILogger _logger;
    private readonly Func<IServiceProvider, string, HttpContext?, CancellationToken, Task<string>> _invokePlaybookDescriptionFactory;

    public AgentToolCatalogProjector(
        IChatClient chatClient,
        ILogger logger,
        Func<IServiceProvider, string, HttpContext?, CancellationToken, Task<string>> invokePlaybookDescriptionFactory)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _invokePlaybookDescriptionFactory = invokePlaybookDescriptionFactory
            ?? throw new ArgumentNullException(nameof(invokePlaybookDescriptionFactory));
    }

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
    /// (DocumentSearchTools, KnowledgeRetrievalTools, TextRefinementTools) are registered based
    /// on service availability — task 047 will refactor these to be capability-gated as well.
    /// AnalysisQueryTools was migrated to typed handler AnalysisQueryHandler in R6 Wave 7
    /// (data-driven via the SYS-Analysis Query sprk_analysistool row + the FR-11 block below).
    ///
    /// FR-23 per-playbook tool filtering: the <paramref name="capabilities"/> set carries either
    /// the matched playbook's declared capabilities (playbookId resolved) or the always-on core
    /// capabilities (standalone conversational chat). Tools gated by capability are registered
    /// only when the gating capability is in the set.
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
    /// <returns>List of registered <see cref="AIFunction"/> instances, or empty list on failure.</returns>
    public async Task<IReadOnlyList<AIFunction>> ResolveToolsAsync(
        IServiceProvider scopedProvider,
        string tenantId,
        string sessionId,
        ChatKnowledgeScope? knowledgeScope,
        IReadOnlySet<string> capabilities,
        Guid playbookId,
        string documentId,
        string? analysisId,
        HttpContext? httpContext,
        Func<Api.Ai.ChatSseEvent, CancellationToken, Task>? sseWriter,
        CitationContext? citationContext,
        CancellationToken cancellationToken = default)
    {
        // Resolve services that tool classes depend on from DI.
        // IRagService and IAnalysisOrchestrationService are registered in Program.cs.
        // IChatClient is registered in AiModule.cs (AIPL-050).
        var ragService = scopedProvider.GetService<IRagService>();
        var analysisService = scopedProvider.GetService<IAnalysisOrchestrationService>();

        var tools = new List<AIFunction>();

        // ADR-033 (R6 Wave 9): hoisted document-stream SSE writer. Built ONCE per ResolveTools
        // call and consumed in two places:
        //   1. The legacy WorkingDocumentTools block below (which requires a non-null delegate,
        //      so we coalesce to a no-op when httpContext is unavailable). This block exits
        //      in Wave 9 Stage 4 once the typed WorkingDocumentHandler is the sole emitter.
        //   2. The data-driven adapter construction (FR-11 block ~line 1290) where the writer
        //      is passed to ToolHandlerToAIFunctionAdapter and forwarded onto each per-call
        //      ChatInvocationContext.DocumentStreamWriter so the typed WorkingDocumentHandler
        //      can emit Start → N×Token → End events directly during streaming.
        //
        // The adapter receives the NULLABLE variant (null when httpContext is unavailable)
        // per ADR-033 §3.1 — the typed handler checks for null and degrades gracefully via
        // ToolResult.Failure with a clear "no stream writer wired" message. The no-op
        // fallback below is specific to the LEGACY WorkingDocumentTools class which
        // requires a non-null delegate by ctor contract.
        var documentStreamWriter = httpContext != null
            ? Api.Ai.ChatEndpoints.CreateDocumentStreamSseWriter(httpContext.Response)
            : null;

        // ADR-033 Stage 4 (R6 Wave 9): parse the analysis id string carried on the chat
        // context's AnalysisMetadata into a Guid for the typed-handler path. The legacy
        // hardcoded WorkingDocumentTools block captures the string directly via ctor; the
        // typed WorkingDocumentHandler reads ChatInvocationContext.AnalysisId (Guid?) which
        // we forward through the adapter constructor below. Null when standalone chat
        // (no analysis bound) or when the string isn't a parseable Guid.
        Guid? analysisIdGuid = Guid.TryParse(analysisId, out var parsedAnalysisId) ? parsedAnalysisId : null;

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
        // REMOVED in R6 Wave 8 (Q9 chat-tool batch migration): replaced by the typed
        // DocumentSearchHandler (Services/Ai/Handlers/DocumentSearchHandler.cs) auto-discovered
        // via ToolFrameworkExtensions.AddToolHandlersFromAssembly (ADR-010) and surfaced to the
        // chat agent by the data-driven block below (FR-11) via two sprk_analysistool rows:
        //   - SYS-Document Search    (DOCUMENT-SEARCH)    → method=SearchDocuments (knowledge-scoped, MinScore=0.7, topK=5)
        //   - SYS-Document Discovery (DOCUMENT-DISCOVERY) → method=SearchDiscovery (tenant-wide, MinScore=0.5, topK=10)
        // Both rows set sprk_requiredcapability = null (always available — gating mirrors the
        // legacy `ragService != null` condition; handler's DI resolution is the runtime gate).
        // Citations + widget metadata + output_pane SSE events are returned via
        // ToolResult.Metadata and the adapter performs side effects (Wave 7b infrastructure).
        // Tenant isolation (ADR-014) preserved via ChatInvocationContext.TenantId.

        // --- AnalysisQueryTools (R6 Wave 7 — migrated to typed handler AnalysisQueryHandler) ---
        // The legacy hardcoded registration was removed in R6 Wave 7. The replacement
        // AnalysisQueryHandler (Services/Ai/Handlers/AnalysisQueryHandler.cs) is auto-discovered
        // via ToolFrameworkExtensions.AddToolHandlersFromAssembly and surfaced to the chat agent
        // by the data-driven block below (FR-11) once the SYS-Analysis Query sprk_analysistool
        // row is seeded (see infra/dataverse/sprk_analysistool-analysis-query-row.json +
        // scripts/Seed-TypedHandlers.ps1). One row + 'method' enum discriminator exposes
        // GetAnalysisResult vs GetAnalysisSummary as a single LLM tool with a method parameter.

        // --- KnowledgeRetrievalTools ---
        // REMOVED in R6 Wave 7c: replaced by the typed KnowledgeRetrievalHandler
        // (Services/Ai/Handlers/KnowledgeRetrievalHandler.cs) auto-discovered via
        // ToolFrameworkExtensions.AddToolHandlersFromAssembly (ADR-010) and surfaced to the
        // chat agent by the data-driven block below (FR-11) via two sprk_analysistool rows:
        //   - SYS-Knowledge Source Retrieval (KNOWLEDGE-SOURCE-GET) → method=GetKnowledgeSource
        //   - SYS-Knowledge Base Search      (KNOWLEDGE-BASE-SEARCH) → method=SearchKnowledgeBase
        // Citations + source_pane SSE events are returned via ToolResult.Metadata and the
        // adapter performs side effects (Wave 7b infrastructure). The ChatKnowledgeScope
        // forwards into ChatInvocationContext.KnowledgeScope so the handler can filter to
        // the playbook's knowledge sources.

        // --- TextRefinementTools ---
        // REMOVED in R6 Wave 7 (Q9 chat-tool batch migration): replaced by the typed
        // TextRefinementHandler (Services/Ai/Handlers/TextRefinementHandler.cs) registered
        // via three sprk_analysistool Dataverse rows (TEXT-REFINE / TEXT-KEYPOINTS /
        // TEXT-SUMMARY) sharing a method-discriminator in sprk_configuration. The chat
        // adapter (ToolHandlerToAIFunctionAdapter) exposes each row as a distinct
        // AIFunction to the LLM. The class TextRefinementTools is retained for
        // ChatEndpoints.RefineTextAsync (SSE streaming refine endpoint) which uses
        // BuildRefineMessages directly — that path is NOT an LLM tool call.

        // --- WorkingDocumentTools ---
        // REMOVED in R6 Wave 9 (Q9 chat-tool batch migration — closes Q9 at 10/10): replaced
        // by the typed WorkingDocumentHandler (Services/Ai/Handlers/WorkingDocumentHandler.cs)
        // auto-discovered via ToolFrameworkExtensions.AddToolHandlersFromAssembly (ADR-010)
        // and surfaced to the chat agent by the data-driven block below (FR-11) via three
        // sprk_analysistool rows sharing a method discriminator in sprk_configuration:
        //   - SYS-Working Document Edit          (WORKING-DOC-EDIT)           → method=EditWorkingDocument (streaming)
        //   - SYS-Working Document Append Section (WORKING-DOC-APPEND-SECTION) → method=AppendSection (streaming)
        //   - SYS-Working Document Write Back    (WORKING-DOC-WRITE-BACK)     → method=WriteBackToWorkingDocument (persistence; FR-12 safety)
        //
        // Capability gate preservation: sprk_requiredcapability = "write_back" on all 3 rows.
        // The data-driven block's IsCapabilityGateSatisfied replaces the hardcoded
        // `if (capabilities.Contains(PlaybookCapabilities.WriteBack))` check above.
        //
        // ADR-033 binding pattern (R6 Wave 9 — first invocation of the side-channel
        // operating principle):
        //   The hoisted `documentStreamWriter` above is forwarded to the adapter via the
        //   `documentStreamWriter:` parameter of `ToolHandlerToAIFunctionAdapter`. The
        //   adapter sets it on every per-call ChatInvocationContext.DocumentStreamWriter.
        //   The handler reads `context.DocumentStreamWriter` and emits DocumentStreamEvent
        //   Start → N×Token → End directly during streaming. Null → ToolResult.Failure with
        //   "no stream writer wired" diagnostic per ADR-033 §3.1.
        //
        //   The parsed `analysisIdGuid` above is forwarded to the adapter via the
        //   `analysisId:` parameter. The adapter sets it on every per-call
        //   ChatInvocationContext.AnalysisId. The handler reads it to fetch the current
        //   working document (EditWorkingDocument / AppendSection) and to target the
        //   write-back persistence (WriteBackToWorkingDocument).
        //
        // Plan-preview gate preservation (spec FR-11, rewired by FR-P2-02 / task 031): the
        //   gate fires on the row's DECLARED sprk_sideeffectclass (write) via
        //   PendingPlanManager.RequiresConfirmation — the pre-D12 hardcoded tool-name
        //   lists were deleted per ADR-039 (no gating by tool names).
        //
        // FR-12 safety preservation: the typed handler routes write-back EXCLUSIVELY through
        //   IWorkingDocumentService → IGenericEntityService (Dataverse); it NEVER calls
        //   SpeFileStore, GraphServiceClient writes, or any SPE/SharePoint write operation.
        //   WorkingDocumentHandlerTests asserts this via the explicit
        //   `WriteBack_Never_CallsIChatClient_FR12Safety` test.

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

        // --- InvokeSummarizePlaybookTool ---
        // REMOVED in R6 Wave 10 / task 023 (D-A-15, Pillar 3 cleanup): replaced by the
        // generic InvokePlaybookHandler (Services/Ai/Handlers/InvokePlaybookHandler.cs)
        // auto-discovered via ToolFrameworkExtensions.AddToolHandlersFromAssembly (ADR-010)
        // and surfaced to the chat agent by the data-driven block below (FR-11) via one
        // sprk_analysistool row:
        //   - SYS-Invoke Playbook (INVOKE-PLAYBOOK) → InvokePlaybookHandler (single function,
        //     no method discriminator). The LLM now calls invoke_playbook(playbookId,
        //     parameters) with the chat-summarize playbook GUID instead of
        //     invoke_summarize_playbook(fileIds, style).
        //
        // Capability gate preservation:
        //   The hardcoded `if (capabilities.Contains(PlaybookCapabilities.Summarize))` check
        //   is REMOVED. The generic invoke_playbook tool is unconditionally available (per
        //   the seed row's sprk_requiredcapability = null), but the per-playbook authorization
        //   is enforced by InvokePlaybookHandler.IsTenantVisibleAsync — only playbooks the
        //   tenant has access to via IPlaybookService can be dispatched. Per task 022's
        //   dynamic invoke_playbook description (D-A-14), the LLM sees the tenant's
        //   accessible playbook list rendered into the tool description at request time, so
        //   it can correctly choose the chat-summarize playbook GUID without prior knowledge.
        //
        // Engine divergence (documented; intentional post R6 Hotfix Wave B-G9c3):
        //   The two server-side entry points for chat-driven Summarize use DIFFERENT engine
        //   methods and produce materially different output:
        //
        //   1. Direct endpoint: POST /api/ai/chat/sessions/{id}/summarize →
        //      SessionSummarizeOrchestrator.SummarizeSessionFilesAsync →
        //      IPlaybookExecutionEngine.ExecuteChatSummarizeAsync (R6 task 025). Uses
        //      Temperature=0 (StreamStructuredCompletionAsync, OpenAiClient.cs line 816),
        //      the SUM-CHAT@v1 sprk_systemprompt loaded from sprk_analysisaction, and the
        //      DocumentSummary structured-output schema (tldr / summary / keywords /
        //      entities). Streams token-by-token as FieldDelta AnalysisChunk events. Intended
        //      for deterministic per-file summarization (e.g. the Document Profile context's
        //      "Summarize this only" affordance via FilePreviewContextWidget).
        //
        //   2. Tool-call path (InvokePlaybookHandler): SprkChatAgent (LLM) calls
        //      invoke_playbook(playbookId, parameters) → InvokePlaybookHandler.ExecuteChatAsync
        //      → IInvokePlaybookAi.InvokePlaybookAsync → IPlaybookOrchestrationService.ExecuteAsync
        //      (NOT ExecuteChatSummarizeAsync). Uses Temperature=0.3 (per-handler
        //      GetStructuredCompletionRawAsync / NodeExecutionContext default), the
        //      PromptSchemaRenderer-rendered prompts with template parameters
        //      (`includeSections`, `usePlainLanguage`, etc.), and per-handler schemas. Non-
        //      streaming whole-response delivery. Produces a richer, conversational output.
        //
        // Slash → NL rewire (R6 Hotfix Wave B-G9c3, 2026-06-10):
        //   The previous version of this comment claimed "Both end at the same engine methods"
        //   — that was documentation drift; the engine methods (ExecuteChatSummarizeAsync vs
        //   ExecuteAsync) and resulting LLM outputs are genuinely different. To make the
        //   Assistant chat experience consistent, the /summarize slash command in
        //   ConversationPane.handleBeforeSendMessage is now suppressed from firing
        //   the retired R5 client summarize orchestrator (which drove the direct
        //   endpoint; deleted by ai-architecture-redesign-r1 task 023). Slash now flows purely
        //   through SprkChatAgent → invoke_playbook → InvokePlaybookHandler →
        //   IPlaybookOrchestrationService.ExecuteAsync, matching natural-language
        //   "summarize this document" output. The direct endpoint
        //   (/api/ai/chat/sessions/{id}/summarize → ExecuteChatSummarizeAsync) is still
        //   exposed for the Document Profile context's "Summarize this only" per-file
        //   affordance (FilePreviewContextWidget) and the R5 task 036 deterministic NL pattern
        //   + button-id dispatches in the chat pane (where the operator-UX contract requires
        //   the structured streaming widget).

        // --- InvokeInsightsQueryTool ---
        // REMOVED in R6 Wave 10 / task 023 (D-A-15, Pillar 3 cleanup): replaced by the
        // generic InvokePlaybookHandler (Services/Ai/Handlers/InvokePlaybookHandler.cs)
        // auto-discovered via ToolFrameworkExtensions.AddToolHandlersFromAssembly (ADR-010)
        // and surfaced to the chat agent by the data-driven block below (FR-11) via the
        // same single SYS-Invoke Playbook row used to replace InvokeSummarizePlaybookTool.
        //
        // FR-24 InsightsIntentClassifier preserved:
        //   The InsightsIntentClassifier continues to handle playbook-vs-RAG routing
        //   internally (per FR-24 + docs/guides/INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md).
        //   When the LLM invokes invoke_playbook with an insights-scoped playbook ID, the
        //   orchestration layer's playbook engine dispatches through the same routing logic.
        //   For entity-scoped analytical questions, the tenant publishes an "insights query"
        //   playbook whose nodes invoke the IInsightsAi services (or the RAG fallback)
        //   internally — the chat tool surface is now uniform.
        //
        // Capability gate preservation:
        //   The hardcoded `if (capabilities.Contains(PlaybookCapabilities.InsightsQuery))`
        //   check is REMOVED. Like Summarize above, per-playbook authorization is enforced
        //   inside InvokePlaybookHandler.IsTenantVisibleAsync via IPlaybookService.
        //   The Insights endpoint's own kill-switches (503 ai.insights.disabled /
        //   ai.rag.disabled / ai.intent-classification.disabled) remain in force at the
        //   downstream service boundary — unchanged by this deletion.

        // --- WebSearchTools ---
        // REMOVED in R6 Wave 8 (Q9 chat-tool batch migration): replaced by the typed
        // WebSearchHandler (Services/Ai/Handlers/WebSearchHandler.cs) auto-discovered via
        // ToolFrameworkExtensions.AddToolHandlersFromAssembly (ADR-010) and surfaced to the
        // chat agent by the data-driven block below (FR-11) via one sprk_analysistool row:
        //   - SYS-Web Search (WEB-SEARCH) → WebSearchHandler (single function, no method discriminator)
        //
        // Capability gate preservation (Wave 7b infrastructure):
        //   The hardcoded `if (capabilities.Contains(PlaybookCapabilities.WebSearch))` check is
        //   replaced by sprk_requiredcapability = "web_search" on the row. The data-driven
        //   block's IsCapabilityGateSatisfied enforces the same admin-controlled boundary.
        //
        // Behavior preserved verbatim by the handler:
        //   - Static SemaphoreSlim(2,2) Bing concurrency gate (ADR-016)
        //   - 5s HTTP timeout, 10s semaphore acquire timeout
        //   - Graceful mock fallback when BingSearch:ApiKey is not configured
        //   - FR-10 scope-guided search via ChatInvocationContext.KnowledgeScope.ScopeSearchGuidance
        //   - ADR-015 telemetry: query length + result count + timing only; no result bodies above Debug
        // Citations returned via ToolResult.Metadata (Wave 7b infrastructure).

        // --- CodeInterpreterTools ---
        // REMOVED in R6 Wave 8 (Q9 chat-tool batch migration): replaced by the typed
        // CodeInterpreterHandler (Services/Ai/Handlers/CodeInterpreterHandler.cs) auto-discovered
        // via ToolFrameworkExtensions.AddToolHandlersFromAssembly (ADR-010) and surfaced to the
        // chat agent by the data-driven block below (FR-11) via two sprk_analysistool rows:
        //   - SYS-Code Analyze Data    (CODE-ANALYZE) → method=AnalyzeData
        //   - SYS-Code Generate Chart  (CODE-CHART)   → method=GenerateChart
        //
        // Capability gate preservation: sprk_requiredcapability = "code_interpreter" on both
        // rows; data-driven block's IsCapabilityGateSatisfied replaces the hardcoded check.
        //
        // Behavior preserved verbatim by the handler:
        //   - ADR-018 kill switch (CodeInterpreterOptions.Enabled) checked before every invocation
        //   - ADR-016 static SemaphoreSlim concurrency gate
        //   - ADR-015 data governance: only caller-supplied data excerpts; no external fetch
        //   - Chart bytes returned as base64 inside Metadata["widget"] (ChartViewer envelope)
        //     AND inline as markdown image data URI in the chat-visible text (dual rendering).

        // --- LegalResearchTools ---
        // REMOVED in R6 Wave 8 (Q9 chat-tool batch migration): replaced by the typed
        // LegalResearchHandler (Services/Ai/Handlers/LegalResearchHandler.cs) auto-discovered
        // via ToolFrameworkExtensions.AddToolHandlersFromAssembly (ADR-010) and surfaced to the
        // chat agent by the data-driven block below (FR-11) via two sprk_analysistool rows:
        //   - SYS-Legal Research      (LEGAL-RESEARCH)     → method=ResearchLegal
        //   - SYS-Legal Case Lookup   (LEGAL-CASE-LOOKUP)  → method=LookupCase
        //
        // Capability gate preservation: sprk_requiredcapability = "legal_research" on both
        // rows; data-driven block's IsCapabilityGateSatisfied replaces the hardcoded check.
        //
        // Behavior preserved verbatim by the handler:
        //   - ADR-015 PII sanitization (QuerySanitizer.Sanitize) before every Bing call
        //   - ADR-018 kill switch (BingGroundingOptions.Enabled) returns user-readable string when disabled
        //   - ADR-015 telemetry: query length + result count + timing only; no query text above Debug
        //   - Uses Azure AI Foundry Bing Grounding via AgentServiceClient (NOT Bing Web Search REST)
        //
        // Concurrency simplification (Wave 8): the legacy double-semaphore (handler-level
        // BingGroundingOptions.MaxConcurrency + SDK-level AgentServiceOptions.MaxConcurrency)
        // is collapsed to just the SDK gate. BingGroundingOptions.MaxConcurrency is no longer
        // consulted at runtime; the property is retained for now (unmodified) and may be pruned
        // in a follow-up. Concurrency-exhaustion still degrades gracefully via the SDK.

        // --- VerifyCitationsTool ---
        // REMOVED in R6 Wave 7c: replaced by the typed VerifyCitationsHandler
        // (Services/Ai/Handlers/VerifyCitationsHandler.cs) auto-discovered via
        // ToolFrameworkExtensions.AddToolHandlersFromAssembly (ADR-010) and surfaced to the
        // chat agent by the data-driven block below (FR-11) via one sprk_analysistool row:
        //   - SYS-Citation Verification (CITATION-VERIFY) → VerifyCitationsHandler
        //
        // Capability gate preservation (Wave 7b infrastructure):
        //   The hardcoded `if (capabilities.Contains(PlaybookCapabilities.VerifyCitations))`
        //   check that previously gated this block is replaced by the per-row
        //   `sprk_requiredcapability = "verify_citations"` column on the seeded row. The
        //   data-driven block's IsCapabilityGateSatisfied(row.RequiredCapability, capabilities)
        //   enforces the same security boundary at chat-session start. Standalone chat
        //   (capabilities = CoreCapabilities; "verify_citations" not included) continues to
        //   skip this tool exactly as before — preserving the pre-Wave-7c boundary.
        //
        // NFR-13 unchanged: the automatic post-LLM CitationSafetyCheck middleware
        // continues to run unconditionally after every response regardless of whether
        // VerifyCitationsHandler is exposed to the LLM for the current playbook.

        // === R6 Pillar 2 / Task D-A-11 (FR-11) — Data-Driven Tool Resolution =================
        // Append AIFunctions for `sprk_analysistool` rows whose
        // `AvailableInContexts` ∋ Chat (i.e. = Chat OR = Both). Each row is wrapped via
        // ToolHandlerToAIFunctionAdapter (task 010) using the IToolHandler whose HandlerId
        // matches the row's HandlerClass (looked up via IToolHandlerRegistry).
        //
        // STRATEGY: ADDITIVE during Q9 migration window (NFR-11 binding).
        //   Existing hardcoded tools above (DocumentSearch, AnalysisQuery, etc.) continue to
        //   work; data-driven tools are APPENDED. Task 012 (Q9 BIG-BANG) will remove the
        //   hardcoded registrations once each tool has a corresponding `sprk_analysistool`
        //   row with `sprk_handlerclass` populated. Until then, the two paths coexist.
        //
        // DEDUPLICATION: rows whose Name collides with an already-registered tool's Name are
        //   skipped (with a warning log) — defensive guard against double-registration when
        //   task 012 partially seeds a row before its hardcoded counterpart is removed. The
        //   hardcoded version wins; the data-driven row is skipped until the hardcoded path
        //   is removed.
        //
        // FALLBACK (FR-11 step 5): if the query yields ZERO chat-available rows (e.g., before
        //   task 012 seeds rows), this block contributes no AIFunctions and the agent
        //   continues with only the hardcoded set. Because the existing hardcoded tools are
        //   untouched, the chat agent remains operational with zero behavior change. The
        //   conversational ability (NFR-01) is preserved unconditionally — even a zero-tool
        //   list yields a working conversational agent.
        //
        // ADR-014 caching: the tool-list query happens at chat-session start (per-session,
        //   not per-message). At ~10 chat tools per tenant, the Dataverse round-trip is
        //   sub-100ms. Per task 011 POML notes ("don't over-engineer"), we DO NOT add a
        //   Redis cache layer here. Tenant scoping is achieved via the in-memory per-call
        //   materialization (every CreateAgentAsync invocation re-queries; no cross-tenant
        //   leakage is possible because the list lives only in the captured method stack).
        //   If session-start latency becomes measurable in production, an
        //   IDistributedCache layer keyed `r6:chat-tools:{tenantId}` with a short TTL can
        //   be inserted via the existing scopedProvider — but defer that to a follow-up.
        //
        // ADR-015 telemetry: log row-COUNT registered/skipped/failed + tenant id only.
        //   NEVER log JSON Schema content, tool descriptions, or handler config.
        //
        // ADR-013 facade boundary: AnalysisToolService and IToolHandlerRegistry are
        //   AI-internal services already registered in AnalysisServicesModule — no new
        //   PublicContracts surface needed.
        //
        // ADR-010: no new top-level DI registration. All dependencies resolved from
        //   the existing scoped provider.
        //
        // ADR-018: NO new feature flag — the additive strategy needs no kill-switch (the
        //   existing tools remain authoritative until task 012 explicitly retires them).
        var dataDrivenAttemptedRows = 0;
        var dataDrivenResolvedRows = 0;
        var dataDrivenSkippedDuplicates = 0;
        var dataDrivenSkippedCapability = 0;
        var dataDrivenFailedRows = new List<string>();
        try
        {
            var analysisToolService = scopedProvider.GetService<AnalysisToolService>();
            var toolHandlerRegistry = scopedProvider.GetService<IToolHandlerRegistry>();

            if (analysisToolService is null)
            {
                // Pre-AnalysisServicesModule.AddAnalysisOrchestrationServices environment
                // (Analysis:Enabled=false). Skip silently — data-driven discovery requires
                // AnalysisToolService which is gated by the same compound flag.
                _logger.LogDebug(
                    "[FR-11] AnalysisToolService not registered (Analysis:Enabled=false); " +
                    "skipping data-driven chat-tool discovery. Hardcoded tools continue to work.");
            }
            else if (toolHandlerRegistry is null)
            {
                _logger.LogWarning(
                    "[FR-11] IToolHandlerRegistry not registered; cannot resolve handlers for " +
                    "data-driven tools. Hardcoded tools continue to work.");
            }
            else
            {
                // Build the set of already-registered tool names so we can dedup. Comparison
                // is case-insensitive because LLM function-calling vendors vary in case
                // handling — better to be conservative.
                var existingToolNames = new HashSet<string>(
                    tools.Select(t => t.Name ?? string.Empty).Where(n => n.Length > 0),
                    StringComparer.OrdinalIgnoreCase);

                // Query Dataverse for chat-available tool rows. Paginated; we request a
                // generous page size (200) — chat tool registry is small (~10 in R6 batch).
                // No tenant filter on the query (rows are global SYS- / customer-prefixed
                // CUST-, scoped by name prefix not by lookup) — same semantics as existing
                // ListToolsAsync usages elsewhere in the codebase.
                var listOptions = new ScopeListOptions { Page = 1, PageSize = 200 };
                var listResult = await analysisToolService
                    .ListToolsAsync(listOptions, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var row in listResult.Items)
                {
                    // Filter to chat-available rows. Treat null AvailableInContexts as
                    // Playbook (backward-compat per FR-07 mapper) — those rows are skipped.
                    var availability = row.AvailableInContexts ?? ToolAvailabilityContext.Playbook;
                    var isChatAvailable =
                        availability == ToolAvailabilityContext.Chat ||
                        availability == ToolAvailabilityContext.Both;
                    if (!isChatAvailable)
                    {
                        continue;
                    }

                    dataDrivenAttemptedRows++;

                    // Dedup: if a hardcoded tool with this name is already in the list,
                    // keep the hardcoded one and skip the row. The migration cutover
                    // (task 012) removes the hardcoded registration once the row's
                    // handler-class wiring is verified.
                    if (existingToolNames.Contains(row.Name))
                    {
                        dataDrivenSkippedDuplicates++;
                        _logger.LogDebug(
                            "[FR-11] Skipping data-driven tool '{ToolName}' (id={ToolId}) — " +
                            "name collides with already-registered hardcoded tool. " +
                            "This is expected during Q9 migration; task 012 will remove the " +
                            "hardcoded version once the row's handler wiring is verified.",
                            row.Name, row.Id);
                        continue;
                    }

                    // R6 Wave 7b: per-playbook capability filter. When sprk_requiredcapability
                    // is set on a tool row, the row is only registered if the current
                    // playbook's capabilities (or CoreCapabilities in standalone-chat mode)
                    // include a CASE-INSENSITIVE match. This REPLACES the hardcoded
                    // `if (capabilities.Contains(PlaybookCapabilities.X))` gates removed in
                    // Waves 7c (VerifyCitations), 8 (LegalResearch / WebSearch /
                    // CodeInterpreter), and 9 (WorkingDocumentTools) — preserving today's
                    // security boundary for capability-gated tools.
                    //
                    // ADR-018 distinction: this is NOT a feature flag — it is per-tool
                    // authorization based on the current playbook's capability set
                    // (resolved earlier at ~line 287 from sprk_analysisplaybook.sprk_playbookcapabilities).
                    // The capability set is data-driven; the kill-switch surface remains
                    // unchanged (LegalResearch / CodeInterpreter / WebSearch ADR-018 flags
                    // continue to gate the underlying service registrations they always have).
                    //
                    // Tools with null sprk_requiredcapability bypass this gate (always-available),
                    // which is the default for existing pre-Wave-7b rows. Migrating chat tools
                    // (Waves 7c / 8 / 9) populate this field with their canonical
                    // PlaybookCapabilities constant (e.g., "verify_citations", "write_back").
                    if (!IsCapabilityGateSatisfied(row.RequiredCapability, capabilities))
                    {
                        dataDrivenSkippedCapability++;
                        _logger.LogDebug(
                            "[FR-11/Wave-7b] Skipping data-driven tool '{ToolName}' (id={ToolId}) — " +
                            "requires capability '{RequiredCapability}' not in current playbook's " +
                            "capability set. Tenant={TenantId}.",
                            row.Name, row.Id, row.RequiredCapability, tenantId);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(row.HandlerClass))
                    {
                        _logger.LogWarning(
                            "[FR-11] Tool row '{ToolName}' (id={ToolId}) has no HandlerClass — " +
                            "cannot resolve IToolHandler. Skipping.",
                            row.Name, row.Id);
                        dataDrivenFailedRows.Add(row.Name);
                        continue;
                    }

                    var handler = toolHandlerRegistry.GetHandler(row.HandlerClass);
                    if (handler is null)
                    {
                        _logger.LogWarning(
                            "[FR-11] Tool row '{ToolName}' (id={ToolId}) HandlerClass " +
                            "'{HandlerClass}' is not registered in IToolHandlerRegistry. " +
                            "Skipping — verify the handler is added to DI in " +
                            "AnalysisServicesModule.",
                            row.Name, row.Id, row.HandlerClass);
                        dataDrivenFailedRows.Add(row.Name);
                        continue;
                    }

                    // Build a context factory closure capturing the captured chat-session
                    // metadata. The adapter calls this per LLM invocation to get a fresh
                    // decision id (Guid.NewGuid per call).
                    var sessionIdGuid = TryParseChatSessionId(sessionId);
                    // R6 Pillar 7 / task 069 (FR-47) — capture the principal oid claim once at
                    // factory time and forward it through the per-call ChatInvocationContext so
                    // user-scoped chat handlers (ManagePinnedContextHandler) see the owning user.
                    // ADR-015: deterministic identifier only; never user message text. Null when
                    // standalone chat (no authenticated user) or when the oid claim is missing.
                    var oidClaim = httpContext?.User?.FindFirst("oid")?.Value
                        ?? httpContext?.User?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
                    Func<ChatInvocationContext> contextFactory = () => new ChatInvocationContext
                    {
                        ChatSessionId = sessionIdGuid,
                        TenantId = tenantId,
                        MatterId = TryParseMatterId(knowledgeScope),
                        UserId = string.IsNullOrWhiteSpace(oidClaim) ? null : oidClaim,
                        // R6 Wave 7c: forward the playbook's knowledge scope so chat-side
                        // handlers (KnowledgeRetrievalHandler etc.) can filter their queries
                        // to the playbook's knowledge sources without taking a separate DI
                        // dependency. ADR-014 per-tenant scope is preserved via TenantId above;
                        // the knowledge scope adds the playbook-level filter on top.
                        KnowledgeScope = knowledgeScope
                    };

                    // R6 Pillar 3 / task 022 (D-A-14) — dynamic invoke_playbook description.
                    // For the generic InvokePlaybookHandler row, override the static seed-row
                    // description with a tenant-specific menu of currently-accessible playbooks
                    // so the LLM sees the actual playbook IDs + names at request time. This is
                    // what makes the generic dispatcher safe to replace the specialized
                    // InvokeSummarize / InvokeInsightsQuery bridges (task 023): the LLM no
                    // longer has to "know" the IDs — they're in the tool description.
                    //
                    // ADR-014: cached per-tenant (5 min TTL) under
                    //   r6:chat-tools:invoke-playbook-description:{tenantId}
                    // ADR-015: telemetry emits count + tenantId + descriptionLengthChars only;
                    //   NEVER playbook names above Debug.
                    // NFR-10: ~1500 char soft cap; alphabetical truncation with "...and N more".
                    // Detection: HandlerClass == "InvokePlaybookHandler" (matches the seed row's
                    //   sprk_handlerclass; the canonical wiring discriminator).
                    var rowForAdapter = row;
                    if (string.Equals(row.HandlerClass, nameof(Sprk.Bff.Api.Services.Ai.Handlers.InvokePlaybookHandler), StringComparison.Ordinal))
                    {
                        try
                        {
                            var dynamicDescription = await _invokePlaybookDescriptionFactory(
                                scopedProvider, tenantId, httpContext, cancellationToken)
                                .ConfigureAwait(false);
                            rowForAdapter = row with { Description = dynamicDescription };
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            // Soft failure: keep the static seed-row description so the tool
                            // still registers (the static text already documents the contract).
                            // ADR-015: log type + tenant only; never playbook content.
                            _logger.LogWarning(ex,
                                "[D-A-14] Dynamic invoke_playbook description generation failed for tenant={TenantId} ({ExceptionType}); falling back to static seed-row description.",
                                tenantId, ex.GetType().Name);
                        }
                    }

                    try
                    {
                        // R6 Wave 7b: pass the per-chat-turn citationContext + sseWriter so
                        // handlers can return citations + widget metadata via ToolResult.Metadata
                        // and the adapter performs the side effects (accumulation + SSE emission).
                        // Both are nullable on the adapter ctor; the data-driven block forwards
                        // whatever this factory has in scope (citationContext is created above at
                        // ~line 407; sseWriter is the optional ChatEndpoints SSE writer arg).
                        //
                        // R6 Wave 9 (ADR-033): also forward the hoisted documentStreamWriter
                        // (null when httpContext is unavailable). The adapter sets it onto each
                        // per-call ChatInvocationContext.DocumentStreamWriter so the typed
                        // WorkingDocumentHandler can emit DocumentStreamEvent Start/Token/End
                        // directly during streaming. Handlers that don't stream simply ignore
                        // the context field; handlers that need it MUST null-check per
                        // ADR-033 §3.1.
                        //
                        // Task 022 (D-A-14): `rowForAdapter` may be the original row OR a
                        // `row with { Description = dynamicDescription }` copy when this is the
                        // InvokePlaybookHandler row — same record, override description only.
                        var adapter = new ToolHandlerToAIFunctionAdapter(
                            rowForAdapter,
                            handler,
                            contextFactory,
                            _logger,
                            citationAccumulator: citationContext,
                            sseWriter: sseWriter,
                            documentStreamWriter: documentStreamWriter,
                            analysisId: analysisIdGuid);
                        tools.Add(adapter);
                        existingToolNames.Add(row.Name);
                        dataDrivenResolvedRows++;
                    }
                    catch (ArgumentException ex)
                    {
                        // Bad schema or missing required AnalysisTool field. Log + skip
                        // rather than crash — resilient registration so other rows still
                        // expose. The adapter logs the row id; we add to failed list for
                        // the summary log below.
                        _logger.LogWarning(ex,
                            "[FR-11] Failed to wrap tool row '{ToolName}' (id={ToolId}) — " +
                            "adapter construction rejected the row. Skipping.",
                            row.Name, row.Id);
                        dataDrivenFailedRows.Add(row.Name);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Handler does not opt-in to chat invocation context — log + skip.
                        _logger.LogWarning(ex,
                            "[FR-11] Failed to wrap tool row '{ToolName}' (id={ToolId}) — " +
                            "handler '{HandlerClass}' does not support chat invocation. Skipping.",
                            row.Name, row.Id, row.HandlerClass);
                        dataDrivenFailedRows.Add(row.Name);
                    }
                }

                // ADR-015: count + outcome only. NEVER log row contents, schemas, descriptions.
                _logger.LogInformation(
                    "[FR-11] Data-driven chat-tool discovery: tenant={TenantId} " +
                    "attempted={AttemptedRows} resolved={ResolvedRows} " +
                    "skippedDuplicates={SkippedDuplicates} skippedCapability={SkippedCapability} " +
                    "failed={FailedRows}",
                    tenantId,
                    dataDrivenAttemptedRows,
                    dataDrivenResolvedRows,
                    dataDrivenSkippedDuplicates,
                    dataDrivenSkippedCapability,
                    dataDrivenFailedRows.Count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Propagate cancellation — caller may have aborted the chat creation.
            throw;
        }
        catch (Exception ex)
        {
            // Soft failure: data-driven discovery is additive. If the query fails (Dataverse
            // outage, transient auth failure, etc.) the chat agent still operates with the
            // hardcoded tools above. NFR-01 conversational primacy is preserved.
            _logger.LogWarning(ex,
                "[FR-11] Data-driven chat-tool discovery failed; hardcoded tools remain. " +
                "tenant={TenantId}",
                tenantId);
        }
        // === End R6 Pillar 2 / Task D-A-11 =====================================================

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

        // FR-23 per-playbook tool filtering: capability gating in the blocks above already
        // limits tools to the matched playbook's declared capabilities (or the always-on
        // core capabilities when no playbook is matched). No per-turn re-filter needed.

        return tools;
    }

    /// <summary>
    /// Best-effort parse of the opaque chat session id (which may not always be a GUID
    /// in legacy session formats) into a Guid for
    /// <see cref="ChatInvocationContext.ChatSessionId"/>. Falls back to
    /// <see cref="Guid.NewGuid"/> when the session id is not a valid Guid — the chat
    /// invocation still proceeds; the decision id remains unique per call.
    /// </summary>
    /// <remarks>
    /// R6 Pillar 2 / Task D-A-11. We do NOT throw on parse failure because the chat
    /// session identifier is opaque to the factory (per
    /// <see cref="CreateAgentAsync"/> contract) — some legacy or test session formats
    /// are non-GUID strings, and rejecting them would break NFR-11 backward compat for
    /// existing sessions.
    /// </remarks>
    private static Guid TryParseChatSessionId(string sessionId) =>
        Guid.TryParse(sessionId, out var parsed) ? parsed : Guid.NewGuid();

    /// <summary>
    /// Best-effort extraction of a matter id from the active
    /// <see cref="ChatKnowledgeScope"/> for
    /// <see cref="ChatInvocationContext.MatterId"/>. Returns null when the scope is null
    /// or does not carry a matter-shaped entity reference.
    /// </summary>
    /// <remarks>
    /// R6 Pillar 2 / Task D-A-11. We read the matter-shaped entity reference from
    /// the scope; non-matter contexts (e.g., chat from a project workspace) return
    /// null per the ChatInvocationContext contract. ADR-015: this is a deterministic
    /// id only — no user content is captured.
    /// <para>
    /// R7 Wave 12 task 150 (audit 120 Gap A): the scope's <c>ParentEntityType</c>
    /// is now BFF-boundary-normalized to the canonical short form (<c>matter</c>)
    /// via <see cref="EntityTypeNormalizer"/>. The legacy raw form
    /// (<c>sprk_matter</c>) is accepted for forward-compat with any session payloads
    /// that bypass <see cref="ChatHostContext"/> construction (none today; defensive).
    /// </para>
    /// </remarks>
    private static Guid? TryParseMatterId(ChatKnowledgeScope? knowledgeScope)
    {
        if (knowledgeScope is null) return null;

        var parentEntityType = knowledgeScope.ParentEntityType;
        if (string.IsNullOrWhiteSpace(parentEntityType)) return null;

        // Accept canonical "matter" (post-normalization) and raw "sprk_matter" (defensive).
        var isMatter = string.Equals(parentEntityType, "matter", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parentEntityType, "sprk_matter", StringComparison.OrdinalIgnoreCase);
        if (!isMatter) return null;

        return Guid.TryParse(knowledgeScope.ParentEntityId, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// R6 Wave 7b: Per-tool capability gate for the data-driven block of
    /// <see cref="ResolveTools"/>. Returns <c>true</c> when the tool's
    /// <see cref="AnalysisTool.RequiredCapability"/> is null/empty (always-available) OR
    /// the current playbook's capability set contains a case-insensitive match.
    /// Replaces the 6 hardcoded <c>if (capabilities.Contains(PlaybookCapabilities.X))</c>
    /// gates as their tools migrate to the data-driven path in Waves 7c / 8 / 9.
    /// </summary>
    /// <param name="requiredCapability">
    /// The canonical capability constant the tool requires (e.g.,
    /// <c>"verify_citations"</c>) or null when the tool has no capability gate.
    /// Whitespace-only values are treated as null (defensive: the
    /// <c>MapRequiredCapability</c> mapper already trims, but this helper does not
    /// assume the field has been pre-canonicalized).
    /// </param>
    /// <param name="capabilities">
    /// The effective capability set for this chat turn — either the playbook's
    /// capabilities or <c>CoreCapabilities</c> for standalone chat.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Case-insensitive matching</b>: canonical capability names are lowercase
    /// snake_case (e.g., <c>"verify_citations"</c>). Admins editing the column in
    /// Power Apps may type uppercase variants, so the comparator uses
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>.
    /// </para>
    /// <para>
    /// <b>ADR-018 distinction</b>: this is per-tool authorization, NOT a feature flag.
    /// Feature flags gate underlying service registrations (e.g., the LegalResearch
    /// Bing Grounding service has its own kill-switch); this helper gates only whether
    /// the chat agent is OFFERED the tool, complementing — not replacing — those flags.
    /// </para>
    /// </remarks>
    internal static bool IsCapabilityGateSatisfied(
        string? requiredCapability,
        IReadOnlySet<string> capabilities)
    {
        if (string.IsNullOrWhiteSpace(requiredCapability))
        {
            return true;
        }

        foreach (var capability in capabilities)
        {
            if (string.Equals(capability, requiredCapability, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
