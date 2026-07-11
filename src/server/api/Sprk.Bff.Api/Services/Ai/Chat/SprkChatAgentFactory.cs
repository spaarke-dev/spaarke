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
using Sprk.Bff.Api.Services.Ai.Chat.Middleware;
using Sprk.Bff.Api.Services.Ai.Export;
using Sprk.Bff.Api.Services.Ai.Foundry;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.Safety.Citations;
using Sprk.Bff.Api.Services.Workspace;

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
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SprkChatAgentFactory> _logger;

    /// <summary>
    /// G-P3 UAT round-1 H6 (2026-07-07) — the side-effect honesty contract appended to
    /// every tool-bearing session's system prompt. The model must never claim a side
    /// effect (record created, task saved, email sent/drafted) that no tool result in
    /// the current conversation confirms, and a user confirmation of a CONVERSATIONAL
    /// proposal still requires invoking the tool (which then presents the platform's
    /// own confirmation dialog). Deterministic constant text (NFR-04 cache stability).
    /// Exposed internal for the directive-presence tests.
    /// </summary>
    internal const string SideEffectHonestyDirective =
        "\n\n## Action Honesty (Spaarke platform contract)\n" +
        "You can only perform actions — creating, updating, saving, sending, or drafting records, " +
        "tasks, emails, or documents — by INVOKING one of your tools. You have no other way to " +
        "change anything.\n" +
        "- NEVER state that a record, task, draft, email, or any change was created, saved, sent, " +
        "completed, or executed unless a TOOL RESULT in this conversation explicitly confirms it. " +
        "There are no exceptions.\n" +
        "- When the user asks you to create or change something and a matching tool or capability " +
        "exists, INVOKE it. Do not describe, simulate, or role-play the flow instead of calling the tool.\n" +
        "- If the user confirms a proposal you made in conversation (\"yes\", \"create it\", \"go ahead\"), " +
        "that confirmation does NOT create anything by itself: you must still invoke the corresponding " +
        "tool. Side-effecting tools present their own confirmation dialog to the user — invoking them " +
        "is safe and required.\n" +
        // G-P3 UAT round-3 R3-1 (2026-07-07): the model asked the user to re-confirm a
        // drafted task FOUR times (re-invoking the drafting capability each time) and never
        // invoked the write tool. Pin the one-question ceiling + the confirmed→invoke bridge.
        "- Ask for confirmation in chat AT MOST ONCE per action. The moment the user has affirmed " +
        "(\"yes\", \"confirm\", \"create it\"), the ONLY correct next step is to IMMEDIATELY invoke the " +
        "corresponding write tool — the platform's CONFIRMATION DIALOG (shown when that tool suspends) " +
        "is the real approval step, not your chat question. NEVER ask the user to confirm again in " +
        "chat, and NEVER re-run a capability_* drafting tool instead of invoking the write tool.\n" +
        // G-P3 UAT round-3 R3-2 (2026-07-07): the model proposed a create with an unresolved
        // person lookup (no GUID) and the confirmed write failed. Resolve BEFORE proposing.
        "- Before proposing any record that references people or other records (lookup columns), " +
        "resolve each reference to its record GUID FIRST using the available search/read tools — in " +
        "the same turn you draft the proposal, not after the user confirms. Never submit a write " +
        "containing a lookup without its recordId GUID; if a reference cannot be resolved, omit that " +
        "column and put the name in a text field instead.\n" +
        "- If a tool reports it was SUSPENDED awaiting user confirmation, say exactly that. The action " +
        "has NOT happened yet.\n" +
        // G-P3 UAT round-2 R2-B (2026-07-07): every fabricated "has now been created" turn
        // correlated with a capability_* DRAFTING call — pin the generation/execution split.
        "- Tools whose names start with 'capability_' only GENERATE draft content (proposals, " +
        "summaries, drafts). Their success means content was drafted — NOT that a record, task, " +
        "or email was created, saved, or sent. Creating it still requires the separate write tool, " +
        "which asks the user to confirm.\n" +
        // G-P3 UAT round-2 R2-D (2026-07-07): the model claimed tabs/editors were opened
        // ("opened in a workspace tab titled …") without any tool result confirming it.
        "- The same applies to UI actions: NEVER state that a tab, view, editor, workspace, or " +
        "dialog was opened unless a tool result in this conversation explicitly confirms it. If no " +
        "available tool can open the requested surface, say so honestly and offer what you CAN do.\n" +
        // G-P3 UAT round-4 R4-3(b) (2026-07-07): asked "do you have a link?", the model
        // INVENTED a /WebResources/tables/… URL. Confirmed actions now carry a real
        // "[Open record](…)" markdown link in their ✅ outcome message — relay that.
        "- NEVER compose, guess, or reconstruct record URLs or deep links. Only relay links that " +
        "appear verbatim in tool results or earlier messages in this conversation (confirmed actions " +
        "include an '[Open record](…)' link in their ✅ outcome message). If no link was provided, " +
        "say you do not have one.\n" +
        // G-P3 UAT round-5 R5-E (2026-07-07): "create a record from this document" — the model
        // GUESSED sprk_document (the user meant a matter) and created an orphan fileless row.
        // Entity-ambiguous create requests are clarified, never guessed.
        "- When the user asks to create 'a record' (or 'a new record') WITHOUT naming the record " +
        "type, do NOT guess the table: ask which type they mean (a task, a matter, a project, a " +
        "document, …) in that same clarifying turn, then proceed once they answer.\n" +
        // spaarke-ai-architecture-redesign-r2 task 044 (gate G-R2-A, operator reframe 2026-07-09):
        // the confirmation gate handles RISK, but WRONG-CHOICE risk (an ambiguous instruction
        // dispatching the wrong capability) is caught HERE, in the agent turn — layer 1, ADR-039's
        // sanctioned intent decider. Asking one question when genuinely torn is CHEAPER than an
        // executed wrong choice + Undo.
        "- When a request could map to more than ONE of your capabilities and you are GENUINELY torn " +
        "about which the user means (e.g. \"create a to-do task\" could be a to-do OR a task; \"add a " +
        "note\" could be a note OR a comment), ask ONE short clarifying question naming the distinct " +
        "options — do NOT dispatch a guess. When the request is clear and names one capability, do NOT " +
        "ask: invoke it directly (the platform decides whether it auto-executes or shows its own " +
        "confirmation dialog).\n" +
        "- If no available tool can perform the requested action, say so honestly instead of pretending " +
        "it was done.";

    /// <summary>
    /// D-F0 Resourcefulness Doctrine (redesign-r2 task 030, spec FR-A1-01, design §7.1 D-F0(a)–(d)).
    /// The strategy-level judgment layer appended to every tool-bearing session's system prompt,
    /// EXTENDING <see cref="SideEffectHonestyDirective"/> (it is composed right after it at the
    /// same call site — there is NO second directive/steering mechanism; CLAUDE.md §11 reuse).
    ///
    /// R1's anti-fabrication hardening (three of six G-P3 UAT rounds) installed caution that
    /// generalized into passivity: the assistant refuses/hedges/asks where it should verify, act,
    /// approximate, or hand a working next step. This block fixes that WITHOUT reopening
    /// "never lie". Four components:
    ///   (a) strategy meta-prompt — decompose → inventory tools → VERIFY state before acting →
    ///       act or approximate → always deliver partial value + a concrete next step;
    ///   (b) read/write safety asymmetry — reads/searches/metadata-describes/verification are
    ///       ALWAYS free (use liberally, never ask permission / hedge / skip); only side effects
    ///       need care, and that care is the platform's confirmation gate, not model timidity;
    ///   (c) graceful-degradation ladder — full action → partial action → structured assistance
    ///       → refusal LAST, operating strictly BELOW the side-effect line;
    ///   (d) every refusal/block hands the user a concrete, working affordance (never a dead end).
    ///
    /// SAFETY (design §13 Risk row 2 — over-correction into fabrication): this block changes
    /// READ-side willingness only. It does NOT weaken any gate or hard block; the ladder never
    /// authorizes claiming an outcome/link/id/tool-call that did not happen — the Action Honesty
    /// rules above stay in force verbatim (no_fabrication remains a 100% floor). Side-effect
    /// caution is deferred to deterministic Confirmation Policy v2 (task 032), NOT to this prompt.
    /// ADR-039: reads stay free WITHIN the budget-8 loop bound — this changes willingness, not the
    /// bound. Deterministic constant text (NFR-04 prompt-cache stability). Exposed internal for
    /// the directive-presence tests + the task-031 resourcefulness eval family.
    /// </summary>
    internal const string ResourcefulnessDoctrineDirective =
        "\n\n## Being Resourceful (Spaarke platform strategy)\n" +
        "Your job is to HELP, not to hedge. Work every request as a strategy, not a single step:\n" +
        "1. DECOMPOSE the request into what it actually needs.\n" +
        "2. INVENTORY your tools — the read/search tools AND the action tools.\n" +
        "3. VERIFY state before you act or claim anything: run the relevant read, search, " +
        "duplicate-check, or metadata/schema lookup FIRST. Never assert that something exists, is " +
        "absent, or already happened without checking, and never reuse a stale answer from an " +
        "earlier turn when a tool can give you the current one.\n" +
        "4. Then ACT — or, when you cannot fully act, APPROXIMATE with what the tools do give you.\n" +
        "5. ALWAYS deliver partial value plus one concrete next step. A bare \"I can't do that\" " +
        "with no path forward is itself a failure.\n" +
        "### Reads are free — only side effects need care\n" +
        "Reads, searches, metadata/schema describes, and verification calls are ALWAYS safe and " +
        "ALWAYS allowed. Use them liberally and on your own initiative. NEVER ask permission to " +
        "read, search, or look something up; NEVER hedge with \"I could check…\" instead of just " +
        "checking; NEVER skip a read that would answer the question. Only side effects (creating, " +
        "updating, saving, sending, deleting) need care — and that care is the platform's job, " +
        "enforced deterministically by the confirmation gate, NOT something you guard against by " +
        "being timid or declining to help. When a side-effecting tool exists, invoking it is safe: " +
        "the platform decides whether to auto-execute or to show its own confirmation dialog.\n" +
        "### Degrade gracefully — refusal is the LAST resort\n" +
        "When you cannot perform the full action, walk DOWN this ladder and STOP at the first rung " +
        "you can reach — do not jump to the bottom:\n" +
        "1. FULL action — invoke the tool.\n" +
        "2. PARTIAL action — do the parts you can (e.g. create the records whose arguments are " +
        "complete; elicit only the one that is missing).\n" +
        "3. STRUCTURED assistance — hand back the values you actually extracted, the content you " +
        "actually drafted, and a pointer to the surface where the user can finish, carrying the " +
        "work as far as the available tools allow.\n" +
        "4. Honest refusal — LAST, and never a dead end.\n" +
        "This ladder operates ONLY BELOW the side-effect line. \"Partial value\" and \"approximate\" " +
        "mean real reads and real drafts — NEVER a claimed outcome. Degrading gracefully NEVER means " +
        "inventing a result, a link, an id, or a tool call: every Action Honesty rule above still " +
        "holds in full. A blocked or gated write is not a failure to hide — surface it honestly and " +
        "carry the work forward.\n" +
        "### Every refusal or block hands the user a way forward\n" +
        "Whenever you refuse, or a tool or gate blocks an action, you MUST give the user a concrete " +
        "next step — never a dead end. State exactly what they can do instead. When a tool result or " +
        "a block message already carries an affordance (a deep link, a prepared value, a named " +
        "surface), relay it verbatim so the user can act on it immediately — do not merely name a " +
        "wizard or page when the platform gave you a link to it. Consistent with the Action Honesty " +
        "rules: relay the links the platform hands you; never compose, guess, or reconstruct one " +
        "yourself.";

    // Task 053 (FR-B-04): BuildCurrentDateDirective moved to the shared
    // ContextSliceProducers.EnvironmentFactsProducer (the ONE source for this primitive — the interactive
    // append site above + the Context Binder's Workspace slice both call it). Byte-identical output.

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
        _serviceProvider = null!;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a <see cref="SprkChatAgent"/> for the given session parameters.
    ///
    /// A new agent instance is returned on every call.  Callers (e.g. ChatSessionManager)
    /// are responsible for caching the agent for the duration of a session and replacing it
    /// when a context switch occurs (different document or playbook).
    ///
    /// Per-playbook tool filtering (FR-23): when <paramref name="playbookId"/> is non-null, the
    /// tool set exposed to the LLM is gated by the playbook's declared capabilities (Action +
    /// Tool scopes). When <paramref name="playbookId"/> is null (standalone conversational chat),
    /// only the always-on core capabilities are exposed.
    /// A <c>capability_change</c> SSE event is emitted when the per-turn tool set differs from the
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
    /// HTTP context for OBO authentication — source of the principal's oid claim and of the
    /// document-stream SSE writer for catalog-projected handlers (e.g. the analysis-rerun
    /// handler's SPE file downloads via <see cref="IAnalysisOrchestrationService.ExecutePlaybookAsync"/>).
    /// May be null for non-streaming contexts (e.g., background processing).
    /// </param>
    /// <param name="sseWriter">
    /// Optional SSE writer delegate for out-of-band events (progress, document_replace,
    /// capability_change). Used by tools and to emit <c>capability_change</c> events when
    /// the per-turn tool set differs from the previous turn.
    /// Null when SSE is not available.
    /// </param>
    /// <param name="latestUserMessage">
    /// The most recent user message text. Used for conversation-aware document chunk
    /// re-selection (FR-03). Null on initial session creation or when not applicable.
    /// </param>
    /// <param name="previousTurnToolNames">
    /// Names of tools that were active in the previous turn (from the caller's session state).
    /// When provided, a <c>capability_change</c> SSE event is emitted if the current turn's
    /// tool set differs. Null on the first turn (no comparison).
    /// </param>
    /// <param name="uploadedFiles">
    /// R5 task 033: Optional manifest of files the end user uploaded into the current chat
    /// session (verbatim from <see cref="ChatSession.UploadedFiles"/>). Forwarded into
    /// <see cref="IChatContextProvider.GetContextAsync"/> so the returned
    /// <see cref="ChatContext.UploadedFiles"/> reflects session state, and surfaced as a
    /// compact "Session Files" manifest suffix on the system prompt so the LLM's tool-call
    /// reasoning sees that uploaded files exist and can pass the correct file IDs when
    /// invoking a Binding capability tool (the Summarize convergence path — FR-01 + FR-08;
    /// capability tools dispatch through the ONE seam per ADR-039 since FR-P2/P3).
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
        IReadOnlyList<SessionOutput>? ledgerOutputs = null,
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
        // F-1/F-2/F-7 envelope-convergence (D1): forward the session id + ledger outputs so the provider
        // performs the ONE per-turn ContextEnvelope bind (fingerprint write) and consumes the bound envelope
        // for host-identity / user-memory / record-memory. The endpoint's separate bind is retired.
        var context = await contextProvider.GetContextAsync(
            documentId,
            tenantId,
            playbookId,
            hostContext,
            additionalDocumentIds,
            uploadedFiles,
            sessionId,
            ledgerOutputs,
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
                // R6 task 068 — Active Capabilities block participates in the shared 8K
                // budget tracker. We resolve the tracker lazily here (it lives later in
                // the prompt-assembly path below; this is a no-op when null).
                var capabilitiesAddition = enrichedPrompt.Length > context.SystemPrompt.Length
                    ? enrichedPrompt[context.SystemPrompt.Length..]
                    : string.Empty;
                var lazyTracker = scope.ServiceProvider.GetService<IPromptBudgetTracker>();
                var lazySessionGuid = Guid.TryParse(sessionId, out var pg) ? pg : (Guid?)null;
                if (TryReservePromptBudget(
                        lazyTracker, "active-capabilities", capabilitiesAddition,
                        lazySessionGuid, tenantId))
                {
                    context = context with { SystemPrompt = enrichedPrompt };
                    _logger.LogDebug(
                        "Enriched system prompt with Active Capabilities section ({CommandCount} scope commands)",
                        commands.Count(c => !string.Equals(c.Category, "system", StringComparison.OrdinalIgnoreCase)
                                         && !string.Equals(c.Category, "playbook", StringComparison.OrdinalIgnoreCase)));
                }
                else
                {
                    _logger.LogWarning(
                        "R6 task 068: Active Capabilities block denied by shared prompt budget tracker (sessionId={SessionId}); omitting",
                        sessionId);
                }
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
        // tool-call reasoning passes the correct file IDs when invoking a Binding
        // capability tool (task 044: the former generic playbook dispatcher was deleted;
        // capability tools carry the file-id vocabulary via their input schemas). Without
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
        if (context.UploadedFiles is { Count: > 0 } files)
        {
            try
            {
                var manifestSuffix = BuildSessionFilesManifestSuffix(files);
                if (!string.IsNullOrEmpty(manifestSuffix))
                {
                    // R6 task 068 — session-files manifest participates in the shared 8K
                    // budget tracker (manifest only — fileId + fileName + count; ADR-015).
                    var lazyTracker = scope.ServiceProvider.GetService<IPromptBudgetTracker>();
                    var lazySessionGuid = Guid.TryParse(sessionId, out var pg) ? pg : (Guid?)null;
                    if (TryReservePromptBudget(
                            lazyTracker, "session-files-manifest", manifestSuffix,
                            lazySessionGuid, tenantId))
                    {
                        context = context with { SystemPrompt = context.SystemPrompt + manifestSuffix };
                        _logger.LogInformation(
                            "R5 task 033: appended Session Files manifest to system prompt — sessionId={SessionId}, fileCount={FileCount}",
                            sessionId, files.Count);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "R6 task 068: Session Files manifest denied by shared prompt budget tracker — sessionId={SessionId}, fileCount={FileCount}; omitting",
                            sessionId, files.Count);
                    }
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

        // === R6 task 068 — Shared prompt budget tracker (Pillar 7 / FR-46) =====
        // Resolve the shared 8K system-prompt budget tracker from the per-turn scope.
        // Scoped lifetime — one tracker per HTTP request / per chat turn — so accounting
        // reflects only this turn. When the tracker is unavailable (pre-task-068 envs),
        // the factory falls back to per-block local budget checks (workspace block already
        // does this via length-truncation in BuildWorkspaceStateBlock).
        //
        // ADR-015: tracker emits truncation telemetry with deterministic IDs only
        // (layer name, token counts, sessionId, tenantId, decision enum). Never fragment
        // bodies. The tracker's tag-set + log-prefix is `[ADR-015][memory.prompt_budget_*]`.
        var promptBudgetTracker = scope.ServiceProvider.GetService<IPromptBudgetTracker>();
        var sessionGuid = Guid.TryParse(sessionId, out var parsedSessionGuid)
            ? parsedSessionGuid
            : (Guid?)null;
        // === End R6 task 068 — tracker resolution ==============================

        // === R6 task 053 — Workspace State block (Pillar 6a / FR-34) ===========
        // Per-turn snapshot of currently open workspace tabs the user has marked
        // visible to the assistant. Lets the LLM answer questions like "what's
        // open in my workspace?" / "what file is on tab 2?". Pillar 9 (task 074)
        // refines this to schema-aware per-widget visible state.
        //
        // ADR-010: IWorkspaceStateService is Scoped; resolved from the same
        // per-turn scope as IChatContextProvider (factory is Singleton) — ZERO
        // new top-level DI registrations.
        // ADR-014: tenantId in the read path (cache key + Cosmos partition key).
        // ADR-015: block carries widget type + matterName + isPinned flags ONLY —
        // never raw user message text from prior turns.
        // NFR-10: workspace block truncates after ~500 chars to preserve the 8K
        // system prompt budget; truncation emits telemetry. R6 task 068 wires the
        // shared budget tracker so the workspace block participates in the same
        // 8K accounting as document context + knowledge + memory composition.
        try
        {
            var workspaceService = scope.ServiceProvider.GetService<IWorkspaceStateService>();
            if (workspaceService is not null)
            {
                var tabs = await workspaceService.GetTabsAsync(tenantId, sessionId, cancellationToken);
                var workspaceBlock = BuildWorkspaceStateBlock(tabs, sessionId);
                if (!string.IsNullOrEmpty(workspaceBlock))
                {
                    if (TryReservePromptBudget(
                            promptBudgetTracker, "workspace-state", workspaceBlock,
                            sessionGuid, tenantId))
                    {
                        context = context with { SystemPrompt = context.SystemPrompt + workspaceBlock };
                        _logger.LogDebug(
                            "R6 task 053: appended Workspace State block to system prompt — sessionId={SessionId}, blockLength={BlockLength}",
                            sessionId, workspaceBlock.Length);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "R6 task 068: workspace-state block denied by shared prompt budget tracker — sessionId={SessionId}, blockLength={BlockLength}; omitting",
                            sessionId, workspaceBlock.Length);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Soft failure — workspace state is enhancing, not required. The agent
            // still works without the block; it just can't answer workspace-aware
            // questions for this turn. Logged so operators see in App Insights.
            _logger.LogWarning(ex,
                "R6 task 053: failed to query workspace state for system prompt — sessionId={SessionId}, continuing without",
                sessionId);
        }
        // === End R6 task 053 ===================================================

        // Resolve playbook capabilities from Dataverse to determine which tools should be available.
        // When no playbook is specified (generic/standalone chat mode), use core capabilities only.
        // This prevents tools with unconfigured dependencies (LegalResearch, CodeInterpreter)
        // from crashing the entire tool pipeline when their options aren't set.
        var capabilities = playbookId.HasValue
            ? await GetPlaybookCapabilitiesAsync(scope.ServiceProvider, playbookId.Value, cancellationToken)
            : (IReadOnlySet<string>)new HashSet<string>(PlaybookCapabilities.CoreCapabilities);

        // === FR-24 (chat-routing-redesign-r1 task 141) — Render-routing dedup directive =========
        // When the dispatcher-resolved playbook (passed via the `playbookId` parameter) targets
        // a NON-chat terminal destination, append a dedup directive to the system prompt so the
        // LLM emits ONLY a single-sentence acknowledgment for the capability tool call —
        // the playbook output renders at the destination (workspace tab / form-prefill /
        // side-effect) and the chat-agent's parallel inline text would be a redundant render
        // (R5 Gap A — path A vs path B parallelism is a smell; structurally eliminated here).
        //
        // The resolved playbook ID arrives via the explicit `playbookId` parameter (resolved
        // upstream in ChatEndpoints). Semantics: when there is a
        // confident playbook resolution and its terminal destination is not chat, suppress
        // LLM inline analysis (R5 Gap A — path A vs path B parallelism is a smell;
        // structurally eliminated here).
        //
        // NFR-01 binding: conversational primacy preserved. The directive applies ONLY to the
        // capability tool call response in THIS turn. Refinement, follow-up, comparison,
        // and context-injection turns are unaffected — the next turn's routing resolves
        // separately and only adds the directive when it again resolves to a non-chat
        // destination playbook.
        //
        // NFR-13 / NFR-07 / NFR-08 binding: safety pipeline, pre-fill flows, and node
        // executors are all UNCHANGED — the dedup is a system-prompt enrichment only.
        //
        // ADR-015 telemetry: log decision + playbookId + destination only; NEVER user content.
        //
        // Soft failure: if INodeService lookup fails (Dataverse outage, etc.), the directive
        // is NOT applied and the chat-agent emits inline text normally — degrades to current
        // (pre-task-042) behavior. NFR-01 conversational primacy is preserved unconditionally.
        if (playbookId.HasValue)
        {
            try
            {
                var resolvedPlaybookId = playbookId.Value;
                var destination = await ResolvePlaybookTerminalDestinationAsync(
                    scope.ServiceProvider, resolvedPlaybookId, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "FR-24 render-routing dedup — session={SessionId} " +
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
                    // When the resolved playbook targets a CHAT destination, the playbook itself
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
                    "FR-24 render-routing dedup directive lookup failed " +
                    "(session={SessionId}, playbookId={PlaybookId}, exceptionType={ExceptionType}); " +
                    "continuing without dedup directive — NFR-01 conversational primacy preserved.",
                    sessionId, playbookId, ex.GetType().Name);
            }
        }
        // === End FR-24 dedup ============================================================

        // Create a shared CitationContext for search handlers to populate with source
        // metadata (via ToolResult.Metadata post-processing in the adapter). The
        // SprkChatAgent resets it before each message to keep citation numbering scoped
        // per assistant response.
        var citationContext = new CitationContext();

        // Extract analysisId from AnalysisMetadata for the write-back / analysis-refinement
        // handlers. This is the sprk_analysisoutput record GUID — populated when SprkChat is
        // launched from the Analysis Workspace with full context (task 002, task 020).
        var analysisId = context.AnalysisMetadata?.GetValueOrDefault("analysisId");

        // Resolve AIFunction tools. FR-23 per-playbook tool filtering is enforced via the
        // `capabilities` set above (matched-playbook capabilities OR always-on core capabilities).
        var catalogProjector = new AgentToolCatalogProjector(_logger);
        var tools = (await catalogProjector.ResolveToolsAsync(
            scope.ServiceProvider, tenantId, sessionId, context.KnowledgeScope, capabilities,
            playbookId ?? Guid.Empty, documentId, analysisId, httpContext, sseWriter, citationContext,
            cancellationToken).ConfigureAwait(false)).ToList();

        // === FR-P2-02 — the ONE confirmation gate at the loop's tool-invocation boundary =
        // Every projected typed-handler tool whose catalog row DECLARES a side-effecting
        // class (write / communicate per PendingPlanManager.RequiresConfirmation — ADR-039:
        // by declaration, never tool-name lists) is wrapped in SideEffectGateAIFunction.
        // When the LLM invokes a wrapped tool, the wrapper does NOT unconditionally suspend
        // (that was the pre-task-044 semantics). Instead it consults the ConfirmationPolicyEngine
        // via SideEffectGateAIFunction.EvaluatePolicy → the deterministic ConfirmationPolicyEngine
        // (the single live decider) — which, from the catalog/Binding risk data, returns one
        // GateOutcome: Execute / ExecuteWithUndo (auto-execute a tier-1/reversible side effect,
        // e.g. memory.write), Elicit (ask for missing args), ConfirmDialog (suspend into the
        // unified pending store — ledger Gate marker BEFORE any render, ADR-040 — for a
        // user-visible confirmation), or HonestBlock (fail-closed). This is the NFR-03 last line —
        // adversarial instructions in uploaded-document text or tool results can at worst be
        // routed to a suspended, user-visible confirmation or blocked, never silently executing
        // a high-risk side effect the policy would gate. Gate landed by task 037
        // (FR-P2-08); the always-suspend outcome was replaced by the policy-engine decision in
        // task 044. NFR-04: the wrapper preserves name/description/schema verbatim, so the
        // projected block is byte-identical.
        for (var i = 0; i < tools.Count; i++)
        {
            if (tools[i] is ToolHandlerToAIFunctionAdapter adapter
                && adapter.Tool.SideEffectClass is { } declaredClass
                && PendingPlanManager.RequiresConfirmation(declaredClass))
            {
                // dispatchUncertaintyProbe + safetyPerimeterDegradedProbe are left null (their
                // defaults): the gate HONORS a real signal when one is threaded, but no live producer
                // reaches this gate today. Ambiguity is covered by layer 1 (the agent asking). The
                // real safety-perimeter fail-open verdict lives in SafetyPipelineMiddleware, which is
                // NOT wired into WrapWithMiddleware on the live chat path (dropped by the R1
                // dispatcher-deletion, commit 26fde1f68) — activating that perimeter is a
                // security-posture change escalated for sign-off (F-8), so the probe stays null and
                // the overlay honestly stays unfired here rather than being hardcoded.
                tools[i] = new SideEffectGateAIFunction(
                    adapter, declaredClass, _serviceProvider, tenantId, sessionId, _logger, sseWriter);
            }
        }

        // === FR-P2-01 — capability-tools projection from the Binding catalog ============
        // Every enabled Binding row with a maker-authored sprk_tooldescription projects
        // into the loop's tool list as a capability tool (ADR-039 loop-as-dispatcher:
        // the model choosing a projected capability IS the text-path dispatch decision,
        // executed by Binding id through the same stack as the Click path). Soft failure:
        // projection is additive — a routing outage degrades to handler tools only.
        try
        {
            var routingService = scope.ServiceProvider.GetService<Sprk.Bff.Api.Services.Ai.PublicContracts.IConsumerRoutingService>();
            if (routingService is not null)
            {
                var bindings = await routingService
                    .ListTextProjectableBindingsAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var excludedInvalidSchemas = 0;
                foreach (var binding in bindings)
                {
                    // G-P3 UAT round-1 H1 (2026-07-07): validate the Action's declared input
                    // schema against the OpenAI function-parameters subset BEFORE projecting.
                    // Azure OpenAI validates EVERY tool schema in the request and 400-fails the
                    // WHOLE turn (invalid_function_parameters) when ANY one is invalid — the
                    // CREATE-TASK@v1 property-level "required": true took down every text-path
                    // turn at G-P3 UAT. One malformed catalog row must only ever cost its OWN
                    // tool. Exclusion is loud: Error log (NFR-07: identifiers + keyword-path
                    // error only), ai.tool.schema_invalid telemetry, and the
                    // RoutingConsumerTypeHealthCheck reports the row as a Degraded finding.
                    if (OpenAiFunctionSchemaValidator.FindFirstError(binding.InputSchemaJson) is { } schemaError)
                    {
                        excludedInvalidSchemas++;
                        _logger.LogError(
                            "[FR-P2-01][invalid-tool-schema] Binding EXCLUDED from tool projection — its " +
                            "sprk_inputschema would fail OpenAI function-parameters validation and 400 the " +
                            "whole turn. Fix the Action row's schema. binding={BindingId} " +
                            "consumerType={ConsumerType} tenant={TenantId} error={SchemaError}",
                            binding.BindingId, binding.ConsumerType, tenantId, schemaError);
                        scope.ServiceProvider.GetService<Sprk.Bff.Api.Telemetry.AiTelemetry>()
                            ?.RecordInvalidToolSchema("binding", binding.ConsumerType, tenantId);
                        continue;
                    }

                    // FR-P2-04: the tenant's no_match_handler Binding projects as the
                    // dedicated refusal tool (honest-refusal loop outcome — file-less
                    // prompted render + ledger write + dispatch_refused telemetry).
                    // Every other opted-in Binding projects as the generic capability
                    // tool (dispatch by id through SessionDispatchOrchestrator). The
                    // discriminator is CATALOG DATA (the row's consumer type), not a
                    // tool-name list (ADR-039).
                    if (string.Equals(binding.ConsumerType, Sprk.Bff.Api.Services.Ai.PublicContracts.ConsumerTypes.NoMatchHandler, StringComparison.OrdinalIgnoreCase))
                    {
                        tools.Add(new RefusalCapabilityTool(
                            binding, _serviceProvider, tenantId, sessionId, _logger));
                        continue;
                    }

                    // sseWriter rides along for the FR-P2-03 capture_mode: modal escape
                    // (elicitation_modal event); null on non-chat surfaces (degrades to
                    // loop elicitation inside the tool).
                    tools.Add(new BindingCapabilityTool(
                        binding, _serviceProvider, tenantId, sessionId, _logger, sseWriter));
                }

                _logger.LogInformation(
                    "[FR-P2-01] Binding capability-tools projected: count={BindingToolCount} " +
                    "excludedInvalidSchemas={ExcludedInvalidSchemas} tenant={TenantId}",
                    bindings.Count - excludedInvalidSchemas, excludedInvalidSchemas, tenantId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[FR-P2-01] Binding capability-tool projection failed; continuing with handler tools only. tenant={TenantId}",
                tenantId);
        }
        // === End FR-P2-01 capability projection ========================================

        // === FR-P2-01 — agent-turn loop contract finalization ==========================
        // (1) Per-turn tool budget (default 8; Ai:AgentTurn:ToolCallBudget platform
        //     setting per NFR-09/ADR-016). (2) Deterministic session-context pre-filter
        //     (catalog surfaces vs the assistant surface + structural session facts —
        //     filtering only, never intent detection; ADR-039). (3) Deterministic ordinal
        //     ordering so the projected tool block is prompt-cache-stable across turns
        //     (NFR-04) — the fingerprint below is the cache-stability evidence surface.
        //     (4) Budget wrap so every executed call is recorded on the turn contract for
        //     the ToolChain ledger write (ADR-040, persisted by the chat endpoint BEFORE
        //     rendering).
        var turnOptions = scope.ServiceProvider
            .GetService<IOptions<AgentTurnOptions>>()?.Value ?? new AgentTurnOptions();
        var turnContract = new AgentTurnContract(turnOptions.ToolCallBudget);
        var filterContext = new AgentToolFilterContext(
            Surface: AgentToolFilterContext.AssistantSurface,
            HasSessionFiles: context.UploadedFiles is { Count: > 0 },
            HasActiveDocument: !string.IsNullOrWhiteSpace(documentId),
            HasAnalysisBinding: !string.IsNullOrWhiteSpace(analysisId));
        var finalTools = AgentToolProjection.Finalize(
            tools, filterContext, turnContract, citationContext, _logger);

        _logger.LogInformation(
            "[FR-P2-01][NFR-04] tool projection finalized: toolCount={ToolCount} (raw={RawCount}) " +
            "budget={ToolCallBudget} fingerprint={ProjectionFingerprint}",
            finalTools.Count, tools.Count, turnOptions.ToolCallBudget,
            AgentToolProjection.ComputeProjectionFingerprint(finalTools));
        // === End FR-P2-01 finalization ==================================================

        // === FR-P2-04 — grounded-outcomes directive (honest refusal) ====================
        // When the tenant's no_match_handler Binding survived projection + pre-filter,
        // pin the G-P2 four-outcome contract in the system prompt so an off-catalog
        // utterance ends in the refusal TOOL — never a free-form apology or an
        // improvised answer (ADR-039 grounded outputs). The directive is loop-contract
        // instruction (like the Citation Guidelines block); the user-facing refusal
        // COPY stays catalog data (the Binding's REF-CHAT@v1 Action). Deterministic
        // constant text — prompt-cache-stable across turns (NFR-04).
        if (finalTools.Any(t => (t as BudgetedAIFunction)?.Inner is RefusalCapabilityTool || t is RefusalCapabilityTool))
        {
            var refusalToolName = BindingCapabilityTool.BuildFunctionName(
                Sprk.Bff.Api.Services.Ai.PublicContracts.ConsumerTypes.NoMatchHandler);
            context = context with
            {
                SystemPrompt = context.SystemPrompt +
                    "\n\n## Grounded Outcomes (Spaarke platform contract)\n" +
                    "Every reply must be one of: (1) invoking an available capability tool, " +
                    "(2) an answer grounded in tool results with [N] citations, " +
                    "(3) a clarifying question needed to invoke a capability, or " +
                    "(4) an honest refusal.\n" +
                    $"If the user's request matches no available tool or capability and cannot be answered " +
                    $"from the session's documents or connected records via the available read tools, call " +
                    $"`{refusalToolName}` with a short neutral `{RefusalCapabilityTool.UnsupportedRequestArgName}` " +
                    "label and relay its returned message verbatim. NEVER answer from general knowledge, " +
                    "NEVER invent or imply capabilities that are not in your tool list, and NEVER decline or " +
                    "apologize in your own words instead of calling that tool.",
            };
        }
        // === End FR-P2-04 directive ======================================================

        // === G-P3 UAT round-1 H6 — side-effect honesty directive (ADR-039) =============
        // Root incident (2026-07-07, session b3c5340c…): the model ROLE-PLAYED an entire
        // create-task flow — asked for due date + assignee, claimed "drafted", then on
        // "yes create it" claimed "has now been created" — WITHOUT ever invoking a tool.
        // No confirmation dialog rendered (SideEffectGateAIFunction never fired because
        // no tool call happened) and no record existed. This directive pins the
        // grounded-execution contract for actions: a claim of a performed side effect is
        // valid ONLY when a tool result in the current conversation confirms it.
        // Deterministic constant text, appended whenever ANY tools project —
        // prompt-cache-stable across turns (NFR-04).
        if (finalTools.Count > 0)
        {
            context = context with
            {
                SystemPrompt = context.SystemPrompt + SideEffectHonestyDirective,
            };
        }
        // === End H6 directive ============================================================

        // === D-F0 Resourcefulness Doctrine (task 030, spec FR-A1-01, design §7.1) ========
        // The strategy-level judgment layer EXTENDS the H6 honesty directive above — composed
        // at the SAME call site, through the SAME system-prompt suffix path (no second directive
        // mechanism; CLAUDE.md §11 reuse). Appended AFTER SideEffectHonestyDirective so the
        // "always help" framing lands with recency against the accreted "never lie" caution
        // (design §7.1: R1's honesty hardening generalized into passivity). Gated on the same
        // finalTools.Count > 0 condition: with no tools projected there is nothing to be
        // resourceful WITH, and the honesty floor is what matters. Deterministic constant text —
        // prompt-cache-stable across turns (NFR-04). SAFETY (Risk row 2): changes read-side
        // willingness only; never weakens a gate/hard block; the ladder stays below the
        // side-effect line and no_fabrication remains a 100% floor.
        if (finalTools.Count > 0)
        {
            context = context with
            {
                SystemPrompt = context.SystemPrompt + ResourcefulnessDoctrineDirective,
            };
        }
        // === End D-F0 doctrine ==========================================================

        // === G-P3 UAT round-5 R5-A — current-date context (2026-07-07) ==================
        // Incident: "due date tomorrow" produced 6/13/2024 — the model had NO current-date
        // context and hallucinated a date (wrong YEAR). Deterministic date line at a STABLE
        // position (end of prompt): it changes once per day, which costs one prompt-cache
        // rotation daily — accepted trade-off (NFR-04 note). The user's timezone is not
        // available server-side (JWT claims carry no tz), so the line is UTC + an explicit
        // near-midnight ambiguity instruction.
        var timeProvider = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        // F-1 envelope-convergence (D1): when the provider bound a per-turn envelope, RENDER the
        // environment (current-date) SUFFIX from that envelope's Workspace slice via the renderer — the
        // interactive prompt now CONSUMES the envelope for the date instead of calling the producer here.
        // Byte-identical: RenderEnvironmentSuffix returns the Workspace fragment, itself produced by the
        // SAME EnvironmentFactsProducer at bind time (day-granular, so stable across the sub-second gap).
        // Falls back to the direct producer call on the no-binder path (compound-OFF / legacy tests).
        var dateSuffix = context.BoundEnvelope is not null
            ? Context.ContextEnvelopeRenderer.RenderEnvironmentSuffix(context.BoundEnvelope)
            : Context.EnvironmentFactsProducer.BuildCurrentDateDirective(timeProvider.GetUtcNow());
        context = context with
        {
            SystemPrompt = context.SystemPrompt + dateSuffix,
        };
        // === End R5-A date context =======================================================

        // === capability_change SSE event ===
        // Emit when the per-turn tool set differs from the previous turn's tool set.
        // This notifies the client (FR-801) that the active tool profile has changed so
        // the UI can update affordances (e.g., hide/show tool pills in the chat bar).
        if (sseWriter is not null && previousTurnToolNames is not null)
        {
            await EmitCapabilityChangesIfDifferentAsync(
                finalTools, previousTurnToolNames, sseWriter, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "SprkChatAgent created: playbook={PlaybookId}, toolCount={ToolCount}, hasDocSummary={HasDocSummary}",
            playbookId, finalTools.Count, context.DocumentSummary != null);

        var agentLogger = scope.ServiceProvider.GetRequiredService<ILogger<SprkChatAgent>>();

        ISprkChatAgent agent = new SprkChatAgent(
            _chatClient,
            context,
            finalTools,
            citationContext,
            agentLogger,
            turnContract);

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

    // FR-P2-06 (task 035): the dispatcher factory method was DELETED with the dispatcher
    // stack (ADR-039 — the agent-turn loop is the ONE dispatch protocol).

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
        var cache = _serviceProvider.GetRequiredService<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>();
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();

        return new DynamicCommandResolver(
            entityService,
            cache,
            loggerFactory.CreateLogger<DynamicCommandResolver>());
    }

    // FR-P2-06 (task 035): the playbook-output-router factory method was DELETED — its sole caller was
    // the dead /api/ai/playbook-dispatch/execute click endpoint removed in the same task.

    // === Private helpers ===

    /// <summary>
    /// Builds the compact "Session Files" manifest suffix appended to the system prompt
    /// when <see cref="ChatContext.UploadedFiles"/> is non-empty (R5 task 033).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Format (task 044 update — tool-agnostic since the generic playbook dispatcher was
    /// deleted; capability tools carry the file-id vocabulary via their input schemas):
    /// <code>
    /// Session Files: This chat session has {N} uploaded file(s) available for tool calls:
    /// {comma-separated fileNames}. When a capability needs these files (e.g. summarize),
    /// pass their file IDs in the tool call's fileIds argument: {comma-separated fileIds}.
    /// </code>
    /// </para>
    /// <para>
    /// ADR-015 invariant: only <see cref="ChatSessionFile.FileName"/> and
    /// <see cref="ChatSessionFile.FileId"/> are emitted — never extracted text, chunk
    /// content, MIME, or size beyond what the manifest already exposes.
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
        // Task 044: tool-agnostic wording — the deleted generic playbook dispatcher is no
        // longer named; capability tools accept the session file IDs via their declared
        // fileIds argument (FR-08 default-all applies when omitted).
        return $"\n\nSession Files: This chat session has {usable.Count} uploaded file{pluralSuffix} available for tool calls: {fileNames}. " +
               $"When a capability needs these files (e.g. summarize), pass their file IDs in the tool call's fileIds argument: {fileIds}.";
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

        // Terminal node = highest ExecutionOrder. Per the frozen orchestration engine and the
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
    /// a capability tool for an intent that routes to a non-chat destination. The
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
    /// entity enrichment). Tool-agnostic wording since task 044 — the LLM applies it to
    /// whichever capability tool it invokes for this intent.
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
               $"({surface}). When you invoke a capability tool for this intent, " +
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
    /// for the capability tool call and to NOT generate analysis content inline
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
    /// This directive is ONLY applied when the dispatcher has resolved a confident playbook
    /// binding (the <c>playbookId</c> parameter is non-null); free-form / refinement /
    /// ambiguous turns see no directive and the LLM responds conversationally as normal.
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
    /// R6 Task 053 (Pillar 6a / FR-34) + Task 074 (Pillar 9 / FR-57/58/59) — builds the
    /// per-turn Workspace State block summarizing currently open tabs the user has marked
    /// visible to the assistant. Returns empty string when no tab is visible OR no visible
    /// tab has a derivable visible state — call site short-circuits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Privacy filter (FR-58 + FR-59 binding)</b>: a tab is INCLUDED iff
    /// <see cref="WorkspaceTab.VisibleToAssistant"/> is true AND
    /// <see cref="TryDeriveVisibleState"/> returns non-null for its widget data. Tabs
    /// whose <c>VisibleToAssistant</c> is false OR whose widget data lacks renderable
    /// visible state (e.g., Summary with no Tldr and empty Body) are filtered OUT.
    /// This is the BFF-side enforcement of Pillar 9's per-widget
    /// <c>getAgentVisibleState()</c> contract — server derives FR-57 shapes directly
    /// from the typed <see cref="WorkspaceTabWidgetData"/> polymorphic union so the
    /// closed 4-variant contract is structurally guaranteed.
    /// </para>
    /// <para>
    /// <b>FR-57 shapes per widget category</b>:
    /// <list type="bullet">
    ///   <item><c>Summary</c> → <c>{ widgetType, summary, tldr, hasUserEdits }</c>.</item>
    ///   <item><c>DocumentViewer</c> → <c>{ widgetType, filename, mimeType, sizeBytes,
    ///   hasSelection, selectionText? }</c> (selectionText capped at 200 chars).</item>
    ///   <item><c>Dashboard</c> → <c>{ widgetType, dashboardName, lastViewedSection }</c>
    ///   (NO chart data; payload minimization per NFR-10).</item>
    ///   <item><c>Table</c> → <c>{ widgetType, rowCount, sortColumn, filteredColumns,
    ///   selectedRows: number }</c> (count only, NOT row IDs — token economy; stricter
    ///   than POML which proposed <c>selectedRows[]</c>).</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>ADR-015 governance</b>: block carries the FR-57 deterministic fields ONLY.
    /// NEVER full widget bodies, NEVER raw user message text from prior turns. The
    /// Summary body is explicitly omitted; only the TL;DR + edit flag participate.
    /// DocumentViewer.selectionText is content-bearing but capped at 200 chars per the
    /// frontend contract (task 073) and the spec's payload-minimization principle.
    /// </para>
    /// <para>
    /// <b>NFR-10 budget</b>: each per-tab block is incrementally reserved against the
    /// shared <see cref="IPromptBudgetTracker"/> by the call site
    /// (<see cref="TryReservePromptBudget"/>). When the requested allocation is denied,
    /// the entire block is omitted (the tracker emits truncation telemetry on the
    /// <c>workspace-state</c> layer). The legacy <see cref="WorkspaceStateBlockMaxChars"/>
    /// is retained as a hard fallback ceiling for when the tracker is unavailable
    /// (pre-task-068 environments) — but is widened from 500 to
    /// <see cref="WorkspaceStateBlockMaxCharsRich"/> to fit the richer per-tab shapes.
    /// </para>
    /// <para>
    /// <b>Active tab convention</b>: the tab with the most recent <c>UpdatedAt</c>
    /// is labeled "(active)". Preserved from task 053.
    /// </para>
    /// </remarks>
    internal const int WorkspaceStateBlockMaxChars = 500;

    /// <summary>
    /// Hard fallback char ceiling for rich per-tab visible state when no
    /// <see cref="IPromptBudgetTracker"/> is wired. ~2 KB ≈ 500 tokens at the conservative
    /// 1.3× word-cost estimate — comfortably under the 8K NFR-10 budget. The tracker
    /// supersedes this when present.
    /// </summary>
    internal const int WorkspaceStateBlockMaxCharsRich = 2000;

    internal string BuildWorkspaceStateBlock(IReadOnlyList<WorkspaceTab> tabs, string sessionId)
    {
        // FR-58 + FR-59 BINDING: filter is `visibleToAssistant === true` AND widget has
        // derivable visible state. Both required. Privacy default — when EITHER condition
        // is unmet, the tab does NOT appear in the agent prompt.
        var visible = tabs
            .Where(t => t.VisibleToAssistant)
            .Select(t => (Tab: t, State: TryDeriveVisibleState(t)))
            .Where(p => p.State is not null)
            .ToList();

        if (visible.Count == 0) return string.Empty;

        // Most-recent UpdatedAt → "active" (preserved from task 053 v1 simplification;
        // explicit active-tab state from registry is a separate follow-up).
        var ordered = visible.OrderByDescending(p => p.Tab.UpdatedAt).ToList();

        var sb = new System.Text.StringBuilder();
        sb.Append("\n\n## Workspace State\n");
        sb.Append("Tabs the user has marked visible to the assistant. Per-tab fields are deterministic visible state only (ADR-015 — no raw user text, no widget bodies).\n");

        var truncatedAt = -1;
        for (var i = 0; i < ordered.Count; i++)
        {
            var (tab, state) = ordered[i];
            var activeMarker = i == 0 ? " (active)" : "";
            var pinnedMarker = tab.IsPinned ? " user-pinned" : "";
            var matterName = tab.MatterContext?.MatterName;
            var matterSuffix = string.IsNullOrWhiteSpace(matterName) ? "" : $" matter=\"{matterName}\"";

            // Header line + structured fields. Format chosen so the LLM can parse without
            // needing to validate a JSON envelope per tab while still treating each tab as
            // a discrete block.
            var header = $"- Tab {i + 1}{activeMarker}: widgetType={tab.WidgetType}{pinnedMarker}{matterSuffix}\n";
            var fields = FormatVisibleStateFields(state!);
            var block = header + fields;

            if (sb.Length + block.Length > WorkspaceStateBlockMaxCharsRich)
            {
                truncatedAt = i;
                break;
            }
            sb.Append(block);
        }

        if (truncatedAt >= 0)
        {
            _logger.LogInformation(
                "R6 task 074: Workspace State block truncated against fallback ceiling — sessionId={SessionId}, includedTabs={Included}, droppedTabs={Dropped}, charBudget={Budget}",
                sessionId, truncatedAt, ordered.Count - truncatedAt, WorkspaceStateBlockMaxCharsRich);
        }

        return sb.ToString();
    }

    /// <summary>
    /// R6 Task 074 (Pillar 9 / FR-57) — server-side derivation of the agent-visible state
    /// shape from a tab's typed <see cref="WorkspaceTabWidgetData"/>. Mirrors the frontend
    /// per-widget <c>getAgentVisibleState()</c> impls (task 073) so the BFF enforces the
    /// FR-57 contract structurally, not by trusting client serialization.
    /// </summary>
    /// <returns>
    /// A typed <see cref="WorkspaceTabVisibleState"/> instance when the widget has
    /// derivable visible state; <c>null</c> when the widget should NOT appear in the
    /// agent prompt (privacy default — e.g., Summary with no TL;DR and empty body).
    /// </returns>
    /// <remarks>
    /// <b>ADR-015 BINDING</b>: only the FR-57 deterministic fields are projected.
    /// Summary's <c>Body</c> is deliberately NOT projected — only TL;DR + edit flag.
    /// DocumentViewer's <c>SelectionText</c> is capped at <see cref="SelectionTextMaxChars"/>.
    /// Dashboard never projects chart data. Table never projects raw rows — only count.
    /// </remarks>
    internal const int SelectionTextMaxChars = 200;

    internal static WorkspaceTabVisibleState? TryDeriveVisibleState(WorkspaceTab tab)
    {
        // Closed-union switch over the polymorphic widget-data types. A new widget kind
        // cannot accidentally leak more than the FR-57 contract permits because the
        // compiler requires explicit handling here.
        return tab.WidgetData switch
        {
            SummaryTabWidgetData s when HasSummaryState(s) => new WorkspaceTabVisibleState.Summary(
                Tldr: s.Tldr,
                SummaryText: NormalizeBody(s.Body),
                HasUserEdits: s.HasUserEdits ?? false),

            DocumentViewerTabWidgetData d => new WorkspaceTabVisibleState.DocumentViewer(
                Filename: d.Filename,
                MimeType: d.MimeType,
                SizeBytes: d.SizeBytes,
                HasSelection: d.HasSelection ?? false,
                SelectionText: TruncateSelection(d.SelectionText, d.HasSelection ?? false)),

            DashboardTabWidgetData db => new WorkspaceTabVisibleState.Dashboard(
                DashboardName: db.DashboardName,
                LastViewedSection: db.LastViewedSection),

            TableTabWidgetData t => new WorkspaceTabVisibleState.Table(
                RowCount: t.RowCount,
                SortColumn: t.SortColumn,
                FilteredColumns: t.FilteredColumns,
                SelectedRows: t.SelectedRows?.Count ?? 0),

            // Unknown / null widget data → no visible state (privacy default).
            _ => null,
        };
    }

    /// <summary>Summary has visible state when EITHER a non-empty TL;DR OR a non-empty body exists.</summary>
    private static bool HasSummaryState(SummaryTabWidgetData s)
        => !string.IsNullOrWhiteSpace(s.Tldr) || !string.IsNullOrWhiteSpace(s.Body);

    /// <summary>
    /// Normalize the Summary body for the agent prompt — collapse whitespace + cap at a
    /// conservative limit. Body text DOES participate in the prompt (it is the agent-
    /// generated summary the user can quote), but we cap aggressively to honor NFR-10.
    /// </summary>
    private const int SummaryBodyMaxChars = 600;

    private static string? NormalizeBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var trimmed = body.Trim();
        if (trimmed.Length <= SummaryBodyMaxChars) return trimmed;
        return trimmed[..SummaryBodyMaxChars] + "…";
    }

    private static string? TruncateSelection(string? selection, bool hasSelection)
    {
        if (!hasSelection || string.IsNullOrWhiteSpace(selection)) return null;
        var trimmed = selection.Trim();
        if (trimmed.Length <= SelectionTextMaxChars) return trimmed;
        return trimmed[..SelectionTextMaxChars] + "…";
    }

    /// <summary>
    /// Format a derived <see cref="WorkspaceTabVisibleState"/> as the per-tab prompt
    /// fields. Indented 2 spaces under the tab header. ADR-015: only deterministic
    /// fields are emitted; selectionText is the only content-bearing field and respects
    /// the 200-char cap upstream.
    /// </summary>
    internal static string FormatVisibleStateFields(WorkspaceTabVisibleState state)
    {
        var sb = new System.Text.StringBuilder();
        switch (state)
        {
            case WorkspaceTabVisibleState.Summary s:
                if (!string.IsNullOrWhiteSpace(s.Tldr))
                    sb.Append($"  tldr: {s.Tldr}\n");
                if (!string.IsNullOrWhiteSpace(s.SummaryText))
                    sb.Append($"  summary: {s.SummaryText}\n");
                sb.Append($"  hasUserEdits: {(s.HasUserEdits ? "true" : "false")}\n");
                break;

            case WorkspaceTabVisibleState.DocumentViewer d:
                sb.Append($"  filename: {d.Filename}\n");
                sb.Append($"  mimeType: {d.MimeType}\n");
                sb.Append($"  sizeBytes: {d.SizeBytes}\n");
                sb.Append($"  hasSelection: {(d.HasSelection ? "true" : "false")}\n");
                if (!string.IsNullOrWhiteSpace(d.SelectionText))
                    sb.Append($"  selectionText: {d.SelectionText}\n");
                break;

            case WorkspaceTabVisibleState.Dashboard db:
                sb.Append($"  dashboardName: {db.DashboardName}\n");
                if (!string.IsNullOrWhiteSpace(db.LastViewedSection))
                    sb.Append($"  lastViewedSection: {db.LastViewedSection}\n");
                break;

            case WorkspaceTabVisibleState.Table t:
                sb.Append($"  rowCount: {t.RowCount}\n");
                if (!string.IsNullOrWhiteSpace(t.SortColumn))
                    sb.Append($"  sortColumn: {t.SortColumn}\n");
                if (t.FilteredColumns is { Count: > 0 })
                    sb.Append($"  filteredColumns: [{string.Join(", ", t.FilteredColumns)}]\n");
                sb.Append($"  selectedRows: {t.SelectedRows}\n");
                break;
        }
        return sb.ToString();
    }

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
               $"in this chat conversation. When you invoke a capability tool for " +
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
    /// Emits <c>capability_change</c> SSE events when the current turn's tool set differs
    /// from the previous turn's tool set.
    ///
    /// Emits one event per tool that was added or removed:
    ///   - Added tool   → status "available"
    ///   - Removed tool → status "unavailable"
    ///
    /// This satisfies the FR-801 contract: clients can update affordances (tool pills, etc.)
    /// in real time when the active tool profile changes between turns (e.g., when the
    /// dispatcher resolves a different playbook on a follow-up turn).
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
                "Tool set changed between turns — emitting capability_change events. " +
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
                "Failed to emit capability_change SSE events; continuing without");
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

    /// <summary>
    /// R6 task 068 (D-C-22 / FR-46) — shared-tracker budget-reservation helper. When
    /// <paramref name="tracker"/> is null (pre-task-068 environments), returns true so
    /// behaviour is unchanged. When wired, estimates token cost of
    /// <paramref name="fragment"/> and attempts to reserve via the tracker; truncation
    /// telemetry is emitted by the tracker on denial.
    /// </summary>
    /// <remarks>
    /// Uses the SAME conservative whitespace-word-count estimate as
    /// <see cref="PlaybookChatContextProvider"/>'s EstimateTokenCount: word_count * 1.3.
    /// Keeps accounting consistent across the four prompt-assembly subsystems.
    /// </remarks>
    internal static bool TryReservePromptBudget(
        Sprk.Bff.Api.Services.Ai.Memory.IPromptBudgetTracker? tracker,
        string layer,
        string fragment,
        Guid? sessionId,
        string? tenantId)
    {
        if (tracker is null)
        {
            return true;
        }

        if (string.IsNullOrEmpty(fragment))
        {
            return true;
        }

        var wordCount = fragment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var tokens = (int)Math.Ceiling(wordCount * 1.3);
        if (tokens <= 0)
        {
            return true;
        }

        return tracker.TryReserve(layer, tokens, sessionId, tenantId);
    }
}
