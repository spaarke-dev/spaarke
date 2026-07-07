using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// The agent-turn loop's tool-catalog resolution component (FR-P2-01,
/// spaarke-ai-architecture-redesign-r1 task 030). Owns what used to be
/// <c>SprkChatAgentFactory.ResolveTools</c> — since FR-P2-07 (task 036) deleted the
/// last hardcoded legacy tool group, this is EXCLUSIVELY the data-driven
/// <c>sprk_analysistool</c> projection (FR-11 / ToolHandlerToAIFunctionAdapter);
/// the factory keeps prompt/context assembly while the closed-catalog tool
/// projection has a single owned home (ADR-039: the catalogs are the ONLY tool source).
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-010</b>: factory-instantiated per agent creation (no DI registration);
/// all dependencies are resolved from the caller's scoped provider exactly as
/// before the extraction.
/// </para>
/// </remarks>
internal sealed class AgentToolCatalogProjector
{
    private readonly ILogger _logger;

    public AgentToolCatalogProjector(
        ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates <see cref="AIFunction"/> tool instances for the agent session — EXCLUSIVELY
    /// from the closed <c>sprk_analysistool</c> catalog (ADR-039). FR-P2-07 (task 036)
    /// deleted the last hardcoded legacy tool group; every tool the loop sees is a catalog
    /// row wrapped via <see cref="ToolHandlerToAIFunctionAdapter"/>.
    ///
    /// FR-23 per-playbook tool filtering: the <paramref name="capabilities"/> set carries either
    /// the matched playbook's declared capabilities (playbookId resolved) or the always-on core
    /// capabilities (standalone conversational chat). Rows gated by <c>sprk_requiredcapability</c>
    /// are registered only when the gating capability is in the set.
    /// </summary>
    /// <param name="scopedProvider">The scoped DI provider for this agent creation call.</param>
    /// <param name="tenantId">Tenant ID from the authenticated session (ADR-014).</param>
    /// <param name="knowledgeScope">
    /// Knowledge scope from the playbook, containing RAG source IDs for search filtering.
    /// Null when the playbook has no knowledge sources configured. Forwarded onto each
    /// per-call <see cref="ChatInvocationContext.KnowledgeScope"/>.
    /// </param>
    /// <param name="capabilities">
    /// Effective capability set for this turn: either the playbook capabilities (full set)
    /// or the router-validated subset (per-turn minimum). See <see cref="PlaybookCapabilities"/>.
    /// </param>
    /// <param name="playbookId">The session's playbook ID — forwarded onto each per-call
    /// <see cref="ChatInvocationContext.PlaybookId"/> (consumed by the analysis-rerun handler).</param>
    /// <param name="documentId">The active document ID — forwarded onto each per-call
    /// <see cref="ChatInvocationContext.DocumentId"/>.</param>
    /// <param name="analysisId">
    /// Optional GUID string of the active <c>sprk_analysisoutput</c> record. Forwarded (parsed)
    /// to the adapter for write-back target resolution (spec FR-12) and analysis refinement.
    /// Null when SprkChat is not launched from the Analysis Workspace.
    /// </param>
    /// <param name="httpContext">HTTP context — source of the principal's oid claim for
    /// user-scoped handlers and of the hoisted document-stream writer.</param>
    /// <param name="sseWriter">SSE writer delegate — forwarded to the adapter for widget
    /// emission and onto each per-call <see cref="ChatInvocationContext.SseWriter"/> for
    /// mid-execution progress / document_replace events.</param>
    /// <param name="citationContext">
    /// Shared citation context populated with source metadata (chunk IDs, source names,
    /// excerpts) by the adapter's post-processing of handler citation metadata.
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
        var tools = new List<AIFunction>();

        // ADR-033 (R6 Wave 9): hoisted document-stream SSE writer. Built ONCE per
        // ResolveToolsAsync call and passed to ToolHandlerToAIFunctionAdapter, which forwards
        // it onto each per-call ChatInvocationContext.DocumentStreamWriter so typed handlers
        // can emit Start → N×Token → End events directly during streaming. The adapter
        // receives the NULLABLE variant (null when httpContext is unavailable) per ADR-033
        // §3.1 — consuming handlers check for null and degrade gracefully. NOTE (task 044):
        // the working-document handler family — the last emitter — was deleted with the F-1
        // legacy legs; the plumbing stays as loop infrastructure (Track-B orphan candidate
        // for the FR-P4-01 completion audit if no P3/P4 handler adopts it).
        var documentStreamWriter = httpContext != null
            ? Api.Ai.ChatEndpoints.CreateDocumentStreamSseWriter(httpContext.Response)
            : null;

        // ADR-033 Stage 4 (R6 Wave 9): parse the analysis id string carried on the chat
        // context's AnalysisMetadata into a Guid for the typed-handler path. The
        // analysis-execution handler reads ChatInvocationContext.AnalysisId (Guid?) which
        // we forward through the adapter constructor below. Null when standalone chat
        // (no analysis bound) or when the string isn't a parseable Guid.
        Guid? analysisIdGuid = Guid.TryParse(analysisId, out var parsedAnalysisId) ? parsedAnalysisId : null;

        // === Legacy hardcoded tool groups: ALL RETIRED (FR-P2-07 closes the set) ============
        // The pre-catalog hardcoded groups migrated to typed handlers + sprk_analysistool rows
        // across R6 Waves 7-9 and this project's P2; the FR-11 data-driven block below is now
        // the ONLY tool source (ADR-039 closed catalog). Row map for the surviving families:
        //   - Document search    → DocumentSearchHandler   (SYS-Document Search / SYS-Document Discovery)
        //   - Knowledge retrieval→ KnowledgeRetrievalHandler (SYS-Knowledge Source Retrieval / SYS-Knowledge Base Search)
        //   - Text refinement    → TextRefinementHandler   (SYS-Text Refinement / SYS-Text Key Points / SYS-Text Summary; text.* tool ids)
        //   - Analysis execution → AnalysisExecutionHandler (SYS-Analysis Rerun / SYS-Analysis Refine; analysis.* tool
        //                          ids; sprk_requiredcapability = "reanalyze" preserves the task-079 capability gate;
        //                          migrated by FR-P2-07 / task 036 as the last live group — session playbook/document
        //                          ids + the SSE writer flow through ChatInvocationContext.PlaybookId/DocumentId/SseWriter)
        //   - Web search / code interpreter / legal research / citation verification →
        //                          WebSearchHandler / CodeInterpreterHandler / LegalResearchHandler /
        //                          VerifyCitationsHandler (capability-gated per row: "web_search",
        //                          "code_interpreter", "legal_research", "verify_citations"; ADR-018
        //                          kill-switches remain at the underlying service registrations)
        // Citations + widget metadata are returned via ToolResult.Metadata and the adapter
        // performs the side effects (Wave 7b infrastructure).
        //
        // FR-P3-05 / audit F-1 closure (task 044, 2026-07-06): the three app-only legacy
        // tool-handler legs — the generic playbook dispatcher, the
        // analysis-query family, and the working-document family — were DELETED with their
        // catalog rows (grep-zero per NFR-08). The chat-summarize capability runs on the
        // Binding catalog via the ONE dispatch seam (SessionDispatchOrchestrator); the
        // /summarize direct endpoint converged onto the same seam.

        // NFR-13 unchanged: the automatic post-LLM CitationSafetyCheck middleware runs
        // unconditionally after every response regardless of whether VerifyCitationsHandler
        // is exposed to the LLM for the current playbook.

        // === R6 Pillar 2 / Task D-A-11 (FR-11) — Data-Driven Tool Resolution =================
        // Append AIFunctions for `sprk_analysistool` rows whose
        // `AvailableInContexts` ∋ Chat (i.e. = Chat OR = Both). Each row is wrapped via
        // ToolHandlerToAIFunctionAdapter (task 010) using the IToolHandler whose HandlerId
        // matches the row's HandlerClass (looked up via IToolHandlerRegistry).
        //
        // STRATEGY (post-FR-P2-07): the catalog is the ONLY tool source. The Q9 "additive"
        //   migration window closed when task 036 deleted the last hardcoded group — every
        //   chat tool is a catalog row resolved here.
        //
        // DEDUPLICATION: rows whose Name collides with an already-registered tool's Name are
        //   skipped (with a warning log) — defensive guard against duplicate active rows
        //   claiming the same tool name (the FR-P0-04 health check also reports duplicate
        //   sprk_toolid claims as Unhealthy).
        //
        // FALLBACK (FR-11 step 5): if the query yields ZERO chat-available rows, this block
        //   contributes no AIFunctions. The conversational ability (NFR-01) is preserved
        //   unconditionally — even a zero-tool list yields a working conversational agent.
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
        // ADR-018: NO feature flag — the closed-catalog projection needs no kill-switch
        //   (rows are deactivated in Dataverse to withdraw a tool).
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
                    "skipping data-driven chat-tool discovery.");
            }
            else if (toolHandlerRegistry is null)
            {
                _logger.LogWarning(
                    "[FR-11] IToolHandlerRegistry not registered; cannot resolve handlers for " +
                    "data-driven tools.");
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
                    // CodeInterpreter), and 9 (WorkingDocument chat-tools) — preserving today's
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
                        KnowledgeScope = knowledgeScope,
                        // FR-P2-07 (task 036): forward the session's playbook + active document
                        // ids so the migrated AnalysisExecutionHandler (method=rerun) can target
                        // the re-analysis. Deterministic identifiers only (ADR-015). Null/empty
                        // when the session carries no playbook/document.
                        PlaybookId = playbookId == Guid.Empty ? null : playbookId,
                        DocumentId = string.IsNullOrWhiteSpace(documentId) ? null : documentId
                    };

                    // FR-P3-05 (task 044): the D-A-14 dynamic tool-description override for the
                    // deleted generic playbook dispatcher was removed with its handler — every
                    // row now registers with its catalog-authored description verbatim.
                    var rowForAdapter = row;

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
                        // per-call ChatInvocationContext.DocumentStreamWriter so typed handlers
                        // can emit DocumentStreamEvent Start/Token/End directly during
                        // streaming. Handlers that don't stream simply ignore the context
                        // field; handlers that need it MUST null-check per ADR-033 §3.1.
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
            // Soft failure: if the catalog query fails (Dataverse outage, transient auth
            // failure, etc.) the chat agent still operates as a conversational agent with
            // an empty tool list. NFR-01 conversational primacy is preserved.
            _logger.LogWarning(ex,
                "[FR-11] Data-driven chat-tool discovery failed; the agent proceeds with zero tools (NFR-01 conversational primacy). " +
                "tenant={TenantId}",
                tenantId);
        }
        // === End R6 Pillar 2 / Task D-A-11 =====================================================

        // FR-23 per-playbook tool filtering: capability gating in the data-driven block above
        // already limits tools to the matched playbook's declared capabilities (or the
        // always-on core capabilities when no playbook is matched). No per-turn re-filter needed.

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
