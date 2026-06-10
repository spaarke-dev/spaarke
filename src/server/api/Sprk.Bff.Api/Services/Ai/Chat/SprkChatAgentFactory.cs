using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Sprk.Bff.Api.Services.Ai.Chat.Middleware;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Sprk.Bff.Api.Services.Ai.Export;
using Sprk.Bff.Api.Services.Ai.Foundry;
using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;
using Sprk.Bff.Api.Services.Ai.Safety.Citations;

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
///
/// Unseal note (task 011 Phase 1b Tier 3, D-09 §2 B2, 2026-06-01): class was `sealed`;
/// unsealed to permit <see cref="NullSprkChatAgentFactory"/> subclassing for the
/// kill-switch-OFF (compound AI disabled) DI state. Per ADR-010 (DI minimalism) the
/// concrete-class Null-Object is preferred over introducing an interface. Production
/// constructor and public methods unchanged; only the `sealed` keyword was removed
/// and the 4 publicly-overridable methods were marked `virtual`.
/// </summary>
public class SprkChatAgentFactory
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
    /// Protected constructor used only by <see cref="NullSprkChatAgentFactory"/> when the
    /// compound AI kill switch is OFF. The production singleton always uses the public ctor.
    /// </summary>
    /// <remarks>
    /// Task 011 Phase 1b Tier 3 (D-09 §2 B2, 2026-06-01). Per D-09 §8 Risks the cleanest
    /// path to support a Null-Object subclass without registering AI dependencies is a
    /// protected constructor that bypasses the AI-dep chain entirely. Public methods are
    /// `virtual` so the Null subclass can override every entry point with a feature-disabled
    /// throw — no base-class behavior runs in the kill-switch-OFF DI state.
    /// </remarks>
    protected SprkChatAgentFactory(ILogger<SprkChatAgentFactory> logger)
    {
        _chatClient = null!;
        _rawChatClient = null!;
        _serviceProvider = null!;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _capabilityRouter = null;
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
    /// <param name="uploadedFiles">
    /// R5 task 033: Optional manifest of files the end user uploaded into the current chat
    /// session (verbatim from <see cref="ChatSession.UploadedFiles"/>). Forwarded into
    /// <see cref="IChatContextProvider.GetContextAsync"/> so the returned
    /// <see cref="ChatContext.UploadedFiles"/> reflects session state, and surfaced as a
    /// compact "Session Files" manifest suffix on the system prompt so the LLM's tool-call
    /// reasoning sees that uploaded files exist and can correctly invoke the generic
    /// <c>invoke_playbook</c> chat tool with the chat-summarize playbook GUID (the Summarize
    /// convergence path — FR-01 + FR-08). R6 task 023 (Wave 10 / Pillar 3) replaced the
    /// specialized <c>InvokeSummarizePlaybookTool</c> bridge with the generic dispatcher.
    /// Manifest only (fileId + fileName); never carries extracted text (ADR-015).
    /// Default <c>null</c> for backward compatibility — pre-R5 sessions / call sites that
    /// omit the parameter behave exactly as before.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A fully configured <see cref="ISprkChatAgent"/> ready to receive messages.
    /// The returned agent is wrapped with the middleware pipeline (AIPL-057, AIPU-072):
    /// ContentSafety (innermost) -> CostControl -> Telemetry -> Routing (outermost).
    /// </returns>
    public virtual async Task<ISprkChatAgent> CreateAgentAsync(
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
        IReadOnlyList<ChatSessionFile>? uploadedFiles = null,
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

        // Load playbook context (system prompt, document summary, metadata).
        // R5 task 033: forward uploadedFiles so the provider surfaces them on the
        // returned ChatContext.UploadedFiles for the manifest-suffix step below.
        var context = await contextProvider.GetContextAsync(
            documentId,
            tenantId,
            playbookId,
            hostContext,
            additionalDocumentIds,
            uploadedFiles,
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

        // === R5 task 033 — Session Files manifest enrichment ====================
        // Surface uploaded session-file awareness (fileId + fileName) to the LLM so its
        // tool-call reasoning correctly invokes the generic `invoke_playbook` chat tool
        // (R6 task 023; the chat-summarize playbook is one of the playbooks listed in the
        // tool's dynamic description per task 022) when the user asks to summarize. Without
        // this signal the agent has historically (verbatim observed on Dev 2026-06-04)
        // declined: "I don't see the document uploaded yet".
        //
        // Constraints (R5 task 033 + ADR-015 + R5 CLAUDE.md §3.4):
        //   - Manifest only — fileId + fileName + count. NEVER include extracted text
        //     content, chunk text, or binary previews in the system prompt.
        //   - Compact — token budget matters (sits alongside playbook prompt + skills +
        //     reference materials + active capabilities + entity enrichment).
        //   - Additive — when no files uploaded, leaves the system prompt unchanged
        //     (zero behavior change for pre-R5 sessions and standalone chat).
        //   - Tool-name binding — names `invoke_playbook` explicitly so the LLM has the
        //     exact tool identifier to invoke (matches the AIFunction name registered for
        //     InvokePlaybookHandler via the SYS-Invoke Playbook seed row — R6 task 021).
        if (context.UploadedFiles is { Count: > 0 } files)
        {
            try
            {
                var manifestSuffix = BuildSessionFilesManifestSuffix(files);
                if (!string.IsNullOrEmpty(manifestSuffix))
                {
                    context = context with { SystemPrompt = context.SystemPrompt + manifestSuffix };
                    _logger.LogInformation(
                        "R5 task 033: appended Session Files manifest to system prompt — sessionId={SessionId}, fileCount={FileCount}",
                        sessionId, files.Count);
                }
            }
            catch (Exception ex)
            {
                // Soft failure — manifest enrichment is enhancing, not required.
                // The agent still works without the suffix; the LLM may simply decline
                // to invoke the summarize tool until the user re-prompts. Logged as
                // warning so operators see this in App Insights.
                _logger.LogWarning(ex,
                    "R5 task 033: failed to append Session Files manifest to system prompt — sessionId={SessionId}, continuing without",
                    sessionId);
            }
        }
        // === End R5 task 033 ====================================================

        // === R6 Hotfix Wave B-G10b — Compact-formatting directive (B12a) ========
        // The chat-pane LLM markdown renderer (SprkChat) uses Fluent markdown styles.
        // Without guidance, GPT models default to verbose markdown with many heading
        // levels, generous spacing, and deeply-nested bullets. This produces a
        // chat surface that feels document-like rather than conversational. The
        // user surfaced this in the Phase B re-walkthrough (B12, 2026-06-10) —
        // followup-card responses ("Explain the main conclusions") had ## /### /
        // numbered lists with 3-level nested bullets.
        //
        // This directive is presentation-only — does NOT change the LLM's actual
        // content. NFR-01 conversational primacy preserved.
        context = context with { SystemPrompt = context.SystemPrompt + BuildCompactFormattingDirective() };
        // === End R6 Hotfix Wave B-G10b =========================================

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

        // === R6 task 042 (FR-30) — CapabilityRouter dedup: one intent → one render =========
        // When the router resolved an UNAMBIGUOUS playbook (single confident capability with
        // a non-null PlaybookId), look up the playbook's terminal node `destination` per
        // NodeRoutingConfig (task 031 / FR-27). When the destination is NOT chat, append a
        // dedup directive to the system prompt so the LLM emits ONLY a single-sentence
        // acknowledgment for the `invoke_playbook` tool call — the playbook output renders
        // at the destination (workspace tab / form-prefill / side-effect) and the chat-agent's
        // parallel inline text would be a redundant render (R5 Gap A — path A vs path B
        // parallelism is a smell; structurally eliminated here).
        //
        // NFR-01 binding: conversational primacy preserved. The directive applies ONLY to the
        // `invoke_playbook` tool call response in THIS turn. Refinement, follow-up,
        // comparison, and context-injection turns are unaffected — the next turn's routing
        // resolves separately and only adds the directive when it again resolves to a
        // non-chat destination playbook.
        //
        // NFR-13 / NFR-07 / NFR-08 binding: safety pipeline, pre-fill flows, and node
        // executors are all UNCHANGED — the dedup is a system-prompt enrichment only.
        //
        // ADR-015 telemetry: log decision + playbookId + destination only; NEVER user content.
        //
        // Soft failure: if INodeService lookup fails (Dataverse outage, etc.), the directive
        // is NOT applied and the chat-agent emits inline text normally — degrades to current
        // (pre-task-042) behavior. NFR-01 conversational primacy is preserved unconditionally.
        if (routingResult is not null
            && routingResult.IsConfident
            && routingResult.SelectedPlaybookId.HasValue)
        {
            try
            {
                var resolvedPlaybookId = routingResult.SelectedPlaybookId.Value;
                var destination = await ResolvePlaybookTerminalDestinationAsync(
                    scope.ServiceProvider, resolvedPlaybookId, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "R6 task 042: CapabilityRouter dedup — session={SessionId} " +
                    "playbookId={PlaybookId} destination={Destination} " +
                    "directiveApplied={DirectiveApplied}",
                    sessionId,
                    resolvedPlaybookId,
                    destination?.ToString() ?? "(unresolved)",
                    destination.HasValue);

                if (destination.HasValue && destination.Value != Models.Ai.NodeDestination.Chat)
                {
                    var directive = BuildDedupDirective(destination.Value);
                    if (!string.IsNullOrEmpty(directive))
                    {
                        context = context with { SystemPrompt = context.SystemPrompt + directive };
                    }
                }
                else if (destination.HasValue && destination.Value == Models.Ai.NodeDestination.Chat)
                {
                    // === Hotfix Wave B-G9b (R6, 2026-06-10) — PDF hallucination fix ====================
                    // When the router resolves to a CHAT-destination playbook, the playbook itself
                    // produces the primary structured result (rendered into chat). Without a directive,
                    // the LLM may ALSO generate inline content in parallel. For PDFs (and any async-
                    // text-extraction format), the LLM sees an empty document body at invocation time
                    // and HALLUCINATES (e.g., "I can't extract this PDF") BEFORE the playbook's
                    // structured summary arrives.
                    //
                    // The fix: apply a SHORT acknowledgment directive (NFR-01-preserving — still
                    // conversational, single sentence — NOT silence) so the LLM emits a brief
                    // "Working on it…" instead of hallucinating about content it does not yet have.
                    //
                    // For .doc / .txt where text is synchronously available, this directive is still
                    // safe — the LLM gets a brief ack and the playbook produces the primary result.
                    //
                    // Wording is DISTINCT from the non-chat-destination directive (which forbids
                    // inline analysis content). For chat destination, the LLM still acknowledges,
                    // and the playbook output renders inline in the same chat surface.
                    var chatAckDirective = BuildChatDestinationAckDirective();
                    if (!string.IsNullOrEmpty(chatAckDirective))
                    {
                        context = context with { SystemPrompt = context.SystemPrompt + chatAckDirective };
                    }
                    // === End Hotfix Wave B-G9b =========================================================
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // ADR-015: log exception type + tenant only; never user content.
                _logger.LogWarning(ex,
                    "R6 task 042: CapabilityRouter dedup directive lookup failed " +
                    "(session={SessionId}, playbookId={PlaybookId}, exceptionType={ExceptionType}); " +
                    "continuing without dedup directive — NFR-01 conversational primacy preserved.",
                    sessionId, routingResult.SelectedPlaybookId, ex.GetType().Name);
            }
        }
        // === End R6 task 042 dedup ============================================================

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
        var tools = await ResolveTools(
            scope.ServiceProvider, tenantId, sessionId, context.KnowledgeScope, effectiveCapabilities,
            playbookId ?? Guid.Empty, documentId, analysisId, httpContext, sseWriter, citationContext,
            routingResult, cancellationToken).ConfigureAwait(false);

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
    public virtual async Task<PlaybookDispatcher> CreatePlaybookDispatcherAsync(
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
    public virtual DynamicCommandResolver CreateCommandResolver()
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
    public virtual PlaybookOutputHandler CreatePlaybookOutputHandler()
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
    /// Builds the compact "Session Files" manifest suffix appended to the system prompt
    /// when <see cref="ChatContext.UploadedFiles"/> is non-empty (R5 task 033).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Format (R6 task 023 update — names the generic `invoke_playbook` tool instead of
    /// the now-deleted `invoke_summarize_playbook` bridge):
    /// <code>
    /// Session Files: This chat session has {N} uploaded file(s) available for tool calls:
    /// {comma-separated fileNames}. When the user asks to summarize, invoke the
    /// `invoke_playbook` tool with the chat-summarize playbook ID (see the tool's
    /// description for tenant-accessible playbooks) and pass these file IDs in the
    /// parameters object: {comma-separated fileIds}.
    /// </code>
    /// </para>
    /// <para>
    /// ADR-015 invariant: only <see cref="ChatSessionFile.FileName"/> and
    /// <see cref="ChatSessionFile.FileId"/> are emitted — never extracted text, chunk
    /// content, MIME, or size beyond what the manifest already exposes.
    /// </para>
    /// <para>
    /// Tool name reference: the literal <c>invoke_playbook</c> matches the AIFunction name
    /// registered for <see cref="Sprk.Bff.Api.Services.Ai.Handlers.InvokePlaybookHandler"/>
    /// via the SYS-Invoke Playbook Dataverse seed row (R6 task 021). Per R6 task 022's
    /// dynamic invoke_playbook description (D-A-14), the tool description itself enumerates
    /// the tenant-accessible playbook GUIDs at request time so the LLM can pick the
    /// chat-summarize playbook without prior knowledge.
    /// </para>
    /// </remarks>
    /// <param name="uploadedFiles">Non-empty, non-null manifest list. Caller guarantees Count &gt; 0.</param>
    /// <returns>The suffix beginning with two newlines, ready to concatenate onto a system prompt. Empty string when the manifest yields no usable entries (defensive — should not happen for Count &gt; 0).</returns>
    internal static string BuildSessionFilesManifestSuffix(IReadOnlyList<ChatSessionFile> uploadedFiles)
    {
        if (uploadedFiles is null || uploadedFiles.Count == 0)
        {
            return string.Empty;
        }

        // Defensive: only include entries with non-blank FileId AND FileName. A blank
        // entry would confuse the LLM (tool call with empty fileId, or fileName like ", ,").
        var usable = uploadedFiles
            .Where(f => !string.IsNullOrWhiteSpace(f.FileId) && !string.IsNullOrWhiteSpace(f.FileName))
            .ToList();

        if (usable.Count == 0)
        {
            return string.Empty;
        }

        var fileNames = string.Join(", ", usable.Select(f => f.FileName));
        var fileIds = string.Join(", ", usable.Select(f => f.FileId));
        var pluralSuffix = usable.Count == 1 ? string.Empty : "s";

        // Two leading newlines isolate the suffix as its own paragraph so the LLM does
        // not blend it into the preceding "### Active Capabilities" or entity enrichment.
        // R6 task 023: names the generic `invoke_playbook` tool (replacing the deleted
        // `invoke_summarize_playbook` bridge). The chat-summarize playbook GUID is one of
        // the tenant-accessible playbooks enumerated in the tool's dynamic description
        // (R6 task 022 / D-A-14), so the LLM can resolve it without prior knowledge.
        return $"\n\nSession Files: This chat session has {usable.Count} uploaded file{pluralSuffix} available for tool calls: {fileNames}. " +
               $"When the user asks to summarize, invoke the `invoke_playbook` tool with the chat-summarize playbook ID (see the tool's description for tenant-accessible playbooks) and pass these file IDs in the parameters object: {fileIds}.";
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
    private async Task<IReadOnlyList<AIFunction>> ResolveTools(
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
        CapabilityRoutingResult? routingResult = null,
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
        // Plan-preview gate preservation (spec FR-11): WriteBackToWorkingDocument is still
        //   listed in CompoundIntentDetector.WriteBackToolNames — the gate fires by tool
        //   NAME, not by class, so the typed handler's "WriteBackToWorkingDocument" method
        //   name continues to trigger plan preview before execution.
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
        //   executeSummarizeIntent (which drives the direct endpoint). Slash now flows purely
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
                    Func<ChatInvocationContext> contextFactory = () => new ChatInvocationContext
                    {
                        ChatSessionId = sessionIdGuid,
                        TenantId = tenantId,
                        MatterId = TryParseMatterId(knowledgeScope),
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
                            var dynamicDescription = await BuildInvokePlaybookDescriptionAsync(
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
    /// R6 Pillar 2 / Task D-A-11. We read <c>ParentEntityType=='sprk_matter'</c> +
    /// <c>ParentEntityId</c> from the scope; non-matter contexts (e.g., chat from
    /// a project workspace) return null per the ChatInvocationContext contract.
    /// ADR-015: this is a deterministic id only — no user content is captured.
    /// </remarks>
    private static Guid? TryParseMatterId(ChatKnowledgeScope? knowledgeScope)
    {
        if (knowledgeScope is null) return null;
        if (!string.Equals(knowledgeScope.ParentEntityType, "sprk_matter", StringComparison.OrdinalIgnoreCase))
            return null;
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

    /// <summary>
    /// R6 task 042 (FR-30): Resolves the terminal node's render destination for the given
    /// playbook by reading <c>sprk_playbooknode.sprk_configjson</c> on the node with the
    /// highest <see cref="PlaybookNodeDto.ExecutionOrder"/> and parsing it as a
    /// <see cref="Models.Ai.NodeRoutingConfig"/>. Returns <c>null</c> when the lookup
    /// fails, the playbook has no nodes, or the terminal node's config does not parse —
    /// the caller (CreateAgentAsync) treats null as "no dedup directive" (preserves
    /// current behavior + NFR-01 conversational primacy).
    /// </summary>
    /// <param name="scopedProvider">
    /// The scoped DI provider for this chat-turn (used to resolve <see cref="INodeService"/>).
    /// </param>
    /// <param name="playbookId">
    /// Dataverse <c>sprk_analysisplaybook</c> ID of the playbook resolved by the router.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The terminal node's render destination, or <c>null</c> when unresolved (no nodes,
    /// no config blob, malformed JSON, transient lookup failure). Defaults of
    /// <see cref="Models.Ai.NodeDestination.Chat"/> from
    /// <see cref="Models.Ai.NodeRoutingConfig.Parse"/> are returned AS Chat so the caller
    /// can short-circuit the directive without invoking the soft-failure branch.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>NFR-13 / NFR-08 binding</b>: this helper consults <see cref="INodeService"/> only
    /// for read access; the 11 production node executors and the safety pipeline are NOT
    /// touched. The lookup runs once per chat-turn (per <c>CreateAgentAsync</c> invocation);
    /// typical latency is &lt;50 ms against Spaarke Dev. No per-turn cache is added —
    /// per-call materialization is sufficient at chat-turn cadence.
    /// </para>
    /// <para>
    /// <b>ADR-015 binding</b>: logs (in the caller) emit playbookId + destination only;
    /// NEVER user content. This helper itself emits no log output — the caller centralizes
    /// telemetry.
    /// </para>
    /// </remarks>
    private static async Task<Models.Ai.NodeDestination?> ResolvePlaybookTerminalDestinationAsync(
        IServiceProvider scopedProvider,
        Guid playbookId,
        CancellationToken cancellationToken)
    {
        var nodeService = scopedProvider.GetService<INodeService>();
        if (nodeService is null)
        {
            return null;
        }

        var nodes = await nodeService.GetNodesAsync(playbookId, cancellationToken).ConfigureAwait(false);
        if (nodes is null || nodes.Length == 0)
        {
            return null;
        }

        // Terminal node = highest ExecutionOrder. Per PlaybookExecutionEngine and the
        // DeliverOutputNodeExecutor contract, the last node in execution order is the
        // one whose ConfigJson carries the destination property (set by tasks 032/033/034/035).
        var terminal = nodes
            .OrderByDescending(n => n.ExecutionOrder)
            .First();

        if (string.IsNullOrWhiteSpace(terminal.ConfigJson))
        {
            // No config blob → NodeRoutingConfig.Parse would return default (Chat). Return
            // Chat explicitly so the caller's branch short-circuits without a directive.
            return Models.Ai.NodeDestination.Chat;
        }

        var routing = Models.Ai.NodeRoutingConfig.Parse(terminal.ConfigJson);
        return routing.Destination;
    }

    /// <summary>
    /// R6 task 042 (FR-30): Builds the system-prompt suffix that instructs the chat-agent
    /// LLM to emit ONLY a single-sentence acknowledgment when invoking
    /// <c>invoke_playbook</c> for an intent that routes to a non-chat destination. The
    /// playbook output renders elsewhere (workspace tab / form-prefill / side-effect); the
    /// chat-agent's parallel inline text would be a redundant render (R5 Gap A — path A vs
    /// path B parallelism eliminated structurally).
    /// </summary>
    /// <param name="destination">The terminal node's resolved render destination.</param>
    /// <returns>
    /// A non-empty directive string when the destination is workspace / form-prefill /
    /// side-effect; empty string when the destination is chat (caller should not invoke
    /// this for chat destinations — current behavior preserved).
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>NFR-01 binding</b>: the directive instructs the LLM to emit a SINGLE-SENTENCE
    /// acknowledgment — not silence. Conversational primacy is preserved (the LLM still
    /// talks, just briefly). Refinement / follow-up / comparison / context-injection turns
    /// are not affected because each turn's directive is re-evaluated against the current
    /// turn's router resolution; the directive does not "stick" across turns.
    /// </para>
    /// <para>
    /// <b>Format</b>: two-newline-prefixed paragraph so the directive is isolated from
    /// preceding system-prompt sections (Active Capabilities, Session Files manifest,
    /// entity enrichment). Names the literal <c>invoke_playbook</c> tool so the LLM has
    /// the exact tool identifier the directive applies to.
    /// </para>
    /// </remarks>
    internal static string BuildDedupDirective(Models.Ai.NodeDestination destination)
    {
        // The destination's user-facing surface determines the acknowledgment wording.
        var (surface, target) = destination switch
        {
            Models.Ai.NodeDestination.Workspace => ("workspace tab", "the workspace"),
            Models.Ai.NodeDestination.FormPrefill => ("form pre-fill", "the form"),
            Models.Ai.NodeDestination.SideEffect => ("background action", "the system"),
            _ => (string.Empty, string.Empty),
        };

        if (string.IsNullOrEmpty(surface))
        {
            // Chat destination (or unknown) — no directive; caller short-circuits.
            return string.Empty;
        }

        // Two leading newlines isolate the directive as its own paragraph. The exact
        // wording is calibrated to keep the LLM brief WITHOUT silencing it (NFR-01:
        // conversational primacy preserved — the LLM still acknowledges the intent).
        return $"\n\n## Render Routing Directive (R6 task 042 / FR-30, hardened B-G10)\n" +
               $"This user intent resolves to a playbook that renders its output to {target} " +
               $"({surface}). When you invoke the `invoke_playbook` tool for this intent, " +
               $"respond with a SINGLE-SENTENCE acknowledgment ONLY (e.g., " +
               $"\"Generating your result in {target}…\"). " +
               $"Do NOT emit the analysis content inline in this chat turn — the playbook " +
               $"output will render in {target}. This prevents a duplicate render " +
               $"(\"path A vs path B\" parallelism — R5 Gap A). " +
               $"In particular, do NOT speculate about whether the document is " +
               $"extractable / readable / contains text — the extraction pipeline runs " +
               $"asynchronously and the playbook handles it. This prevents hallucinated " +
               $"\"I attempted to retrieve\" / \"content not accessible\" messages on " +
               $"async-extracted formats (PDF, scanned images). " +
               $"The user's subsequent " +
               $"follow-up turns (refinement, comparison, context injection) are " +
               $"unaffected — respond conversationally as normal on those turns.";
    }

    /// <summary>
    /// Hotfix Wave B-G9b (R6, 2026-06-10) — builds the system-prompt suffix that instructs
    /// the chat-agent LLM to emit a SHORT acknowledgment when the router has resolved an
    /// intent to a CHAT-destination playbook. Distinct from
    /// <see cref="BuildDedupDirective(Models.Ai.NodeDestination)"/> (which targets non-chat
    /// destinations and forbids inline content); for chat-destination playbooks the
    /// playbook output renders inline in the same chat surface, so the directive only
    /// suppresses the LLM's parallel free-form generation that — for async-extracted formats
    /// like PDF — would otherwise hallucinate about content the LLM hasn't seen yet.
    /// </summary>
    /// <returns>
    /// A non-empty directive string instructing the LLM to emit a single brief acknowledgment
    /// for the <c>invoke_playbook</c> tool call and to NOT generate analysis content inline
    /// (the playbook will render the primary result in chat).
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Root cause</b>: chat-destination playbooks (e.g., <c>summarize-document-for-chat@v1</c>)
    /// produce the primary structured result via the playbook executor. For synchronous text
    /// formats (.doc, .txt) the LLM has the text at invocation time and would have responded
    /// fine on its own. For asynchronous formats (PDF needs Document Intelligence extraction),
    /// the LLM sees an empty/partial document body at invocation time and HALLUCINATES
    /// (e.g., "It appears the attached document does not contain extractable text") BEFORE
    /// the playbook's structured summary arrives. This directive prevents both the
    /// hallucinated message AND the duplicate inline render when the playbook does produce
    /// content.
    /// </para>
    /// <para>
    /// <b>NFR-01 binding</b>: the directive instructs a SHORT acknowledgment — NOT silence.
    /// Conversational primacy is preserved (the LLM still emits one acknowledgment sentence).
    /// This directive is ONLY applied when the router has resolved a confident playbook
    /// binding (<c>SelectedPlaybookId</c> != null); free-form / refinement / ambiguous turns
    /// see no directive and the LLM responds conversationally as normal.
    /// </para>
    /// <para>
    /// <b>R5 Gap A binding</b>: this is the chat-destination side of the same dedup pattern
    /// task 042 closed for non-chat destinations. Together, the two directives ensure that
    /// EVERY confident playbook-routed intent has ONE primary render path — never two
    /// parallel paths (LLM inline + playbook output).
    /// </para>
    /// <para>
    /// <b>ADR-013 binding</b>: directive lives inside <c>Services/Ai/Chat/</c> — does not
    /// widen the AI public-contracts surface.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Hotfix Wave B-G10b (R6, 2026-06-10) — builds the system-prompt suffix that
    /// instructs the LLM to use compact markdown formatting in chat-pane responses.
    /// Caps heading depth at one level, bullet nesting at two levels, and prefers
    /// short paragraphs over heavy structural markup. Presentation-only — does NOT
    /// affect the LLM's content. Applied to ALL chat turns.
    /// </summary>
    internal static string BuildCompactFormattingDirective()
    {
        return $"\n\n## Chat Response Formatting (Hotfix Wave B-G10b)\n" +
               $"Use COMPACT markdown for chat responses. Specific rules:\n" +
               $"- Prefer short paragraphs (2-4 sentences) over headings when possible.\n" +
               $"- Use AT MOST one heading level (e.g., '## Section'). Do NOT use '###' or deeper.\n" +
               $"- Cap bullet nesting at TWO levels (parent → child). Do NOT use 3+ level nesting.\n" +
               $"- Do NOT bold inside bullets unless naming a defined term.\n" +
               $"- For substantive responses (more than ~150 words), lead with a 2-3 sentence TL;DR " +
               $"paragraph before any headings or lists.\n" +
               $"- Skip blank lines between adjacent bullets in the same list.\n" +
               $"- Use prose where it flows naturally; reserve lists for genuinely enumerable items " +
               $"(3+ parallel items).\n" +
               $"- The chat surface is conversational — match the user's register.";
    }

    internal static string BuildChatDestinationAckDirective()
    {
        // The wording instructs a SHORT acknowledgment WITHOUT forbidding chat as a render
        // surface — distinct from BuildDedupDirective which routes to a NON-chat surface.
        // Here the playbook DOES render in chat, but the LLM's parallel free-form is
        // suppressed to prevent hallucination + duplicate-fire.
        return $"\n\n## Render Routing Directive (Hotfix Wave B-G9b)\n" +
               $"This user intent resolves to a playbook that will render its result inline " +
               $"in this chat conversation. When you invoke the `invoke_playbook` tool for " +
               $"this intent, respond with a SINGLE-SENTENCE acknowledgment ONLY (e.g., " +
               $"\"Working on that now…\" or \"I'll summarize that for you now.\"). " +
               $"Do NOT attempt to analyze, summarize, extract, or describe the document " +
               $"content yourself — the playbook will produce the structured result. " +
               $"In particular, do NOT speculate about whether the document is " +
               $"extractable / readable / contains text — the extraction pipeline runs " +
               $"asynchronously and the playbook handles it. This prevents hallucinated " +
               $"\"I can't read this\" messages on async-extracted formats (PDF, scanned " +
               $"images) and a duplicate inline render. The user's subsequent follow-up " +
               $"turns (refinement, comparison, context injection) are unaffected — " +
               $"respond conversationally as normal on those turns.";
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

    // ─────────────────────────────────────────────────────────────────────────────
    // R6 Pillar 3 / Task 022 (D-A-14) — Dynamic invoke_playbook tool description
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ADR-014 cache-key prefix for the per-tenant dynamic invoke_playbook tool description
    /// (R6 Pillar 3 / task 022). The <c>r6:</c> prefix scopes the cache to the R6 project
    /// per project memory; the <c>chat-tools:</c> infix scopes to chat-side tooling.
    /// </summary>
    internal const string InvokePlaybookDescriptionCacheKeyPrefix = "r6:chat-tools:invoke-playbook-description:";

    /// <summary>
    /// ADR-014 TTL for the dynamic invoke_playbook description cache. Short enough that a
    /// tenant admin adding/removing a playbook propagates to the LLM within minutes; long
    /// enough to amortize the Dataverse round-trip across multiple chat sessions per tenant.
    /// Matches the visibility-cache TTL used by <see cref="Handlers.InvokePlaybookHandler"/>.
    /// </summary>
    internal static readonly TimeSpan InvokePlaybookDescriptionCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// NFR-10 soft budget for the rendered invoke_playbook description. The 8K system-prompt
    /// budget is shared across several context-providers (persona + chat history + memory +
    /// knowledge retrieval + tool descriptions); allotting ~1500 chars (≈ 375 tokens) to this
    /// single tool's description leaves comfortable headroom. When the rendered list would
    /// exceed this budget, alphabetically-leading playbooks are listed in full and the rest
    /// are summarized as "...and N more (request by name to discover their IDs)."
    /// </summary>
    internal const int InvokePlaybookDescriptionBudgetChars = 1500;

    /// <summary>
    /// R6 Pillar 3 / task 022 (D-A-14) — generates the dynamic <c>invoke_playbook</c> tool
    /// description at chat-agent build time. Lists the tenant-accessible playbook IDs +
    /// names + short descriptions so the LLM can pick the correct <c>playbookId</c> at
    /// request time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ADR-014 caching</b>: keyed by <c>{prefix}{tenantId}</c> in
    /// <see cref="IMemoryCache"/> (per-process; cross-tenant isolation via the prefix).
    /// TTL <see cref="InvokePlaybookDescriptionCacheTtl"/>. Cache miss falls through to the
    /// Dataverse query; positive AND empty-list results are cached (LLM retries during a
    /// turn shouldn't re-query).
    /// </para>
    /// <para>
    /// <b>Tenant-accessible list</b>: mirrors the
    /// <c>GET /api/ai/chat/playbooks</c> endpoint's surface — combines
    /// <see cref="IPlaybookService.ListUserPlaybooksAsync"/> (when the http context carries
    /// an <c>oid</c> claim) with <see cref="IPlaybookService.ListPublicPlaybooksAsync"/>,
    /// deduplicated by ID. This is the canonical "what playbooks does this tenant see"
    /// definition used elsewhere in the chat layer.
    /// </para>
    /// <para>
    /// <b>NFR-10 budget</b>: render alphabetically-sorted entries until the running char
    /// count would exceed <see cref="InvokePlaybookDescriptionBudgetChars"/>; append a
    /// "...and N more (request by name to discover their IDs)" suffix when truncated. The
    /// LLM can still discover truncated playbooks by name via a natural-language refusal +
    /// follow-up — the description's purpose is to bias the LLM toward the most common
    /// playbooks, not to be exhaustive.
    /// </para>
    /// <para>
    /// <b>Empty list</b>: when no playbooks are tenant-accessible (rare but possible during
    /// initial tenant onboarding), the description explicitly says "no playbooks currently
    /// available" so the LLM doesn't invent fake GUIDs.
    /// </para>
    /// <para>
    /// <b>ADR-015 telemetry</b>: emits <c>playbookCount</c> + <c>tenantId</c> +
    /// <c>descriptionLengthChars</c> only. NEVER playbook names above Debug level — admin
    /// debugging may rely on Debug-level rendering of the description but production
    /// telemetry stays count-only.
    /// </para>
    /// </remarks>
    private async Task<string> BuildInvokePlaybookDescriptionAsync(
        IServiceProvider scopedProvider,
        string tenantId,
        HttpContext? httpContext,
        CancellationToken cancellationToken)
    {
        // ADR-014: per-tenant cache lookup before the Dataverse round-trip.
        var memoryCache = scopedProvider.GetService<IMemoryCache>();
        var cacheKey = $"{InvokePlaybookDescriptionCacheKeyPrefix}{tenantId}";

        if (memoryCache is not null
            && memoryCache.TryGetValue<string>(cacheKey, out var cachedDescription)
            && !string.IsNullOrEmpty(cachedDescription))
        {
            _logger.LogDebug(
                "[D-A-14] Dynamic invoke_playbook description served from cache for tenant={TenantId} (lengthChars={LengthChars})",
                tenantId, cachedDescription.Length);
            return cachedDescription;
        }

        // Cache miss — query the tenant's accessible playbook list. Same surface as
        // ChatEndpoints.ListPlaybooksAsync (the canonical "what playbooks does this tenant
        // see" definition): merge user-owned + public, dedupe by id.
        var playbookService = scopedProvider.GetService<IPlaybookService>();
        if (playbookService is null)
        {
            // Pre-AI-DI environment (Analysis disabled). Surface a neutral description so
            // the tool registration doesn't crash; the handler itself will refuse on every
            // dispatch in this state. Do NOT cache — DI state may change.
            _logger.LogDebug(
                "[D-A-14] IPlaybookService not registered; using fallback invoke_playbook description for tenant={TenantId}",
                tenantId);
            return BuildEmptyPlaybookDescription();
        }

        var playbooks = await LoadTenantAccessiblePlaybooksAsync(
            playbookService, httpContext, cancellationToken).ConfigureAwait(false);

        var description = RenderInvokePlaybookDescription(playbooks);

        // ADR-014: cache the result (including empty-list) under the per-tenant key so the
        // LLM's retries within a turn don't re-query Dataverse.
        if (memoryCache is not null)
        {
            memoryCache.Set(cacheKey, description, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = InvokePlaybookDescriptionCacheTtl,
                Size = 1
            });
        }

        // ADR-015 telemetry: count + tenant + length only. NEVER playbook names above Debug.
        _logger.LogInformation(
            "[D-A-14][ADR-015] Dynamic invoke_playbook description generated for tenant={TenantId} playbookCount={PlaybookCount} descriptionLengthChars={LengthChars}",
            tenantId, playbooks.Count, description.Length);

        return description;
    }

    /// <summary>
    /// Loads the tenant-accessible playbook list — owner playbooks (when an oid claim is
    /// present on the http context) merged with public playbooks, deduplicated by ID. Same
    /// definition as <c>ChatEndpoints.ListPlaybooksAsync</c> uses for the
    /// <c>GET /api/ai/chat/playbooks</c> endpoint.
    /// </summary>
    /// <remarks>
    /// Returns an alphabetically sorted (by Name) list so the rendered description is
    /// deterministic across chat sessions (helps the LLM pattern-match the tool description
    /// against earlier turns' descriptions). Sorting also ensures the NFR-10 truncation
    /// strategy is reproducible — "first N alphabetically" is a stable choice.
    /// </remarks>
    private async Task<IReadOnlyList<PlaybookSummary>> LoadTenantAccessiblePlaybooksAsync(
        IPlaybookService playbookService,
        HttpContext? httpContext,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<Guid>();
        var playbooks = new List<PlaybookSummary>();
        var query = new PlaybookQueryParameters { PageSize = 200 };

        // 1. User-owned playbooks (when oid claim is available — standalone chat without
        //    an authenticated user has no oid and gets the public-only list).
        Guid? userId = null;
        if (httpContext is not null)
        {
            var oid = httpContext.User?.FindFirst("oid")?.Value
                ?? httpContext.User?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
            if (Guid.TryParse(oid, out var parsedUserId))
            {
                userId = parsedUserId;
            }
        }

        if (userId.HasValue)
        {
            try
            {
                var userPlaybooks = await playbookService
                    .ListUserPlaybooksAsync(userId.Value, query, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var pb in userPlaybooks.Items)
                {
                    if (seen.Add(pb.Id))
                    {
                        playbooks.Add(pb);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Match the ChatEndpoints handler's resilience pattern — log + continue with
                // public-only. ADR-015: log exception type + userId only.
                _logger.LogWarning(ex,
                    "[D-A-14] Failed to load user playbooks for invoke_playbook description (userId={UserId} exceptionType={ExceptionType}); continuing with public-only",
                    userId, ex.GetType().Name);
            }
        }

        // 2. Public / shared playbooks (always queried regardless of user-id presence).
        try
        {
            var publicPlaybooks = await playbookService
                .ListPublicPlaybooksAsync(query, cancellationToken)
                .ConfigureAwait(false);
            foreach (var pb in publicPlaybooks.Items)
            {
                if (seen.Add(pb.Id))
                {
                    playbooks.Add(pb);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[D-A-14] Failed to load public playbooks for invoke_playbook description (exceptionType={ExceptionType}); returning whatever subset loaded",
                ex.GetType().Name);
        }

        // Alphabetical sort for deterministic rendering + reproducible NFR-10 truncation.
        playbooks.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return playbooks;
    }

    /// <summary>
    /// Renders the playbook list as a markdown-ish menu the LLM can consult when deciding
    /// which <c>playbookId</c> to pass to <c>invoke_playbook</c>. Respects the NFR-10
    /// budget (see <see cref="InvokePlaybookDescriptionBudgetChars"/>) — truncates with a
    /// "...and N more" suffix when the rendered list would exceed the soft cap.
    /// </summary>
    /// <remarks>
    /// Format:
    /// <code>
    /// Invoke any registered playbook by ID with parameters. Available playbooks for this tenant:
    /// - {guid}: {name} — {short description}
    /// - {guid}: {name} — {short description}
    /// ...
    /// Pass the playbookId field with one of the values above.
    /// </code>
    /// Per-entry description is truncated to <c>~120 chars</c> to keep the menu legible —
    /// the full description is available via the playbook's own metadata when the LLM
    /// invokes it.
    /// </remarks>
    internal static string RenderInvokePlaybookDescription(IReadOnlyList<PlaybookSummary> playbooks)
    {
        if (playbooks is null || playbooks.Count == 0)
        {
            return BuildEmptyPlaybookDescription();
        }

        const string header = "Invoke any registered playbook by ID with parameters. Available playbooks for this tenant:\n";
        const string trailer = "\nPass the playbookId field with one of the values above. The 'parameters' object carries optional template-substitution variables the playbook's nodes consume.";

        var sb = new System.Text.StringBuilder(header.Length + trailer.Length + playbooks.Count * 80);
        sb.Append(header);

        int includedCount = 0;
        int truncatedCount = 0;
        int currentLength = header.Length + trailer.Length;

        foreach (var pb in playbooks)
        {
            var entry = FormatPlaybookEntry(pb);
            // Reserve room for the suffix line in case we need to truncate later. The
            // "...and N more" suffix is bounded by a short max length (under 80 chars even
            // for very large remaining counts).
            const int reservedForSuffix = 80;
            if (currentLength + entry.Length + reservedForSuffix > InvokePlaybookDescriptionBudgetChars
                && includedCount > 0)
            {
                truncatedCount = playbooks.Count - includedCount;
                break;
            }
            sb.Append(entry);
            currentLength += entry.Length;
            includedCount++;
        }

        if (truncatedCount > 0)
        {
            sb.Append("- ...and ");
            sb.Append(truncatedCount);
            sb.Append(" more (request by name to discover their IDs).\n");
        }

        sb.Append(trailer);
        return sb.ToString();
    }

    /// <summary>
    /// Formats a single playbook as a menu line:
    /// <c>"- {id}: {name} — {short description}\n"</c>. The short description is truncated
    /// to ~120 chars to keep the rendered menu legible inside the NFR-10 budget.
    /// </summary>
    private static string FormatPlaybookEntry(PlaybookSummary pb)
    {
        const int shortDescriptionCap = 120;
        var name = pb.Name ?? "(unnamed)";
        var rawDescription = pb.Description ?? string.Empty;
        // Collapse newlines so each entry is exactly one line — critical for LLM parsing.
        var description = rawDescription
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
        if (description.Length > shortDescriptionCap)
        {
            description = description.Substring(0, shortDescriptionCap - 1) + "…";
        }

        if (string.IsNullOrEmpty(description))
        {
            return $"- {pb.Id:D}: {name}\n";
        }
        return $"- {pb.Id:D}: {name} — {description}\n";
    }

    /// <summary>
    /// Description for the zero-playbook case — used both when the tenant has no accessible
    /// playbooks and as the safe fallback when <see cref="IPlaybookService"/> is unavailable
    /// (AI feature disabled). The LLM still sees a coherent tool description; the
    /// <see cref="Handlers.InvokePlaybookHandler"/> refuses on dispatch in this state.
    /// </summary>
    internal static string BuildEmptyPlaybookDescription() =>
        "Invoke any registered playbook by ID with parameters. " +
        "No playbooks currently available for this tenant. " +
        "Use natural language to request analysis instead of calling this tool.";

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
