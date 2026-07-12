using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Memory;

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
///   5. Load the document summary from <see cref="IDocumentDataverseService"/> for inline context.
///   6. Return a <see cref="ChatContext"/> with enriched system prompt and knowledge scope.
/// </summary>
public class PlaybookChatContextProvider : IChatContextProvider
{
    /// <summary>
    /// Maximum token budget for the system prompt. Enrichment is skipped if appending
    /// would push the total past this limit. Rough estimate: 1 token ≈ 4 characters.
    /// </summary>
    internal const int MaxSystemPromptTokenBudget = 8_000;

    /// <summary>
    /// Maximum token count for the entity enrichment block itself.
    /// Raised 100 → 150 by the G-P3 UAT round-1 H7 fix (2026-07-07): the block now
    /// carries the record id + the "this record" binding instruction (~95 tokens with
    /// a typical display name); at the old cap a long matter name silently dropped the
    /// whole block — recreating exactly the host-context blindness H7 fixes.
    /// </summary>
    internal const int MaxEnrichmentTokens = 150;

    /// <summary>
    /// Maps raw page type values to human-readable labels for the enrichment block.
    /// Unknown or unmapped page types result in enrichment being skipped.
    /// </summary>
    private static readonly Dictionary<string, string> PageTypeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["entityrecord"] = "main form view",
        ["entitylist"] = "list view",
        ["dashboard"] = "dashboard view",
        ["webresource"] = "workspace view",
        ["custom"] = "custom page view"
    };

    /// <summary>
    /// R7 Wave 12 task 152 (audit 120 Gap C) — default <see cref="ChatHostContext.PageType"/>
    /// applied at the entity-enrichment guard when the client did not populate one.
    /// <para>
    /// Choice rationale: chat requests carrying an EntityType + EntityId (e.g. SpaarkeAi
    /// launched from a matter form) are by definition on an entity-form surface. The
    /// Dynamics "entityrecord" page type is the most-common case for that scenario and
    /// maps to the human-readable label "main form view" — the same label every existing
    /// matter / project / invoice form would surface today. Clients MAY override by sending
    /// an explicit <c>PageType</c>; the default only fires when the client omits the field
    /// or sends null/whitespace.
    /// </para>
    /// <para>
    /// "unknown" inputs are still treated as missing (no default substituted) because that
    /// is the client's explicit "I don't know" signal — overriding it would mask
    /// a real misconfiguration upstream. Default substitution only applies to
    /// null/empty/whitespace.
    /// </para>
    /// </summary>
    internal const string DefaultPageType = "entityrecord";

    private readonly IScopeResolverService _scopeResolver;
    private readonly IPlaybookService _playbookService;
    private readonly IDocumentDataverseService _documentService;
    private readonly ILogger<PlaybookChatContextProvider> _logger;

    // R6 Pillar 7 (task 068, D-C-21) — cross-session record-memory activation, GENERALIZED
    // by task 050 (FR-B-01): when the host context identifies ANY record (matter, project,
    // invoice, ...), the per-record structured fragment is appended to the system prompt
    // under the shared budget tracker. Reads the subject-partitioned IMemoryItemStore
    // (matter-only IMatterMemoryService retired). Registered unconditionally in
    // AiPersistenceModule, so required (non-nullable) here.
    private readonly IMemoryItemStore _memoryItemStore;

    // R6 Pillar 7 (task 068, D-C-22) — shared 8K system-prompt budget tracker. Remains
    // nullable because the tracker registration is gated by the compound
    // (Analysis:Enabled && DocumentIntelligence:Enabled) flag in AnalysisServicesModule
    // while this provider is registered unconditionally in AiModule. When the AI gate is
    // off the chat factory becomes NullSprkChatAgentFactory so this provider is never
    // resolved in practice — but the nullable shape keeps the DI graph honest.
    private readonly IPromptBudgetTracker? _promptBudgetTracker;

    // R7 Wave 12 task 151 (audit 120 Gap B) — server-side EntityName lazy-fetch. The
    // SpaarkeAi client does not populate EntityName in ChatHostContext, which would cause
    // AppendEntityEnrichment to short-circuit even with a normalized EntityType (T150 fix
    // for Gap A). When EntityName is missing but EntityType + EntityId are present, we
    // retrieve the entity's primary-name attribute (sprk_name) from Dataverse via
    // IGenericEntityService. Nullable to preserve backward-compat with existing test
    // fixtures that pre-date T151; production DI always supplies it (forwarded from
    // IDataverseService composite per GraphModule.AddSingleton<IGenericEntityService>).
    private readonly IGenericEntityService? _entityService;

    // R7 Wave 12 task 151 — per-request memoization for lazy EntityName fetches. The
    // provider is Scoped (per AiModule line 233), so this Dictionary is naturally
    // request-scoped: it is created fresh for each request and disposed with the scope.
    // Keyed on "{logicalName}:{entityIdGuid}" (case-normalized). A null cached value
    // means "fetch failed previously this request — do not retry".
    private readonly Dictionary<string, string?> _entityNameCache =
        new(StringComparer.OrdinalIgnoreCase);

    // F-1/F-2/F-7 envelope-convergence task (D1) — the interactive prompt now CONSUMES the per-turn
    // ContextEnvelope via the Context Binder (the SAME seam the dispatch path uses): host-identity
    // (Business), user-memory (User, F-2 recall), and record-memory fragments are produced ONCE by the
    // Binder and consumed here through ContextEnvelopeRenderer / BoundInputs — the direct producer-append
    // sites retire on the live path. OPTIONAL: registered only in the compound-ON path (AnalysisServicesModule);
    // when null (compound-OFF / the many legacy provider tests + BusinessSliceDeterminismContractTests,
    // which construct this provider WITHOUT a binder) the provider falls back to the shipped direct-append
    // path — byte-identical output either way (proven by the renderer-vs-legacy parity pins).
    private readonly IContextBinder? _contextBinder;

    public PlaybookChatContextProvider(
        IScopeResolverService scopeResolver,
        IPlaybookService playbookService,
        IDocumentDataverseService documentService,
        ILogger<PlaybookChatContextProvider> logger,
        IMemoryItemStore memoryItemStore,
        IPromptBudgetTracker? promptBudgetTracker = null,
        IGenericEntityService? entityService = null,
        IContextBinder? contextBinder = null)
    {
        _scopeResolver = scopeResolver;
        _playbookService = playbookService;
        _documentService = documentService;
        _logger = logger;
        _memoryItemStore = memoryItemStore;
        _promptBudgetTracker = promptBudgetTracker;
        _entityService = entityService;
        _contextBinder = contextBinder;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>FR-27 single-pipeline contract (chat-routing-redesign-r1, task 078 MVP audit, 2026-06-23)</b>:
    /// this method is the single per-turn system-prompt composition seam for chat. There is
    /// exactly one composition flow: persona/action resolution → knowledge scope enrichment
    /// → entity enrichment → matter-memory append (FR-45) → document-summary load → return
    /// <see cref="ChatContext"/>. The chat factory (<see cref="SprkChatAgentFactory"/>)
    /// then appends suffix blocks (Active Capabilities, Session Files manifest, formatting
    /// directive, Workspace State) onto the returned <c>SystemPrompt</c> under the shared
    /// <see cref="IPromptBudgetTracker"/>. NO other component composes the per-turn prompt
    /// — there is no parallel composer in <see cref="SprkChatAgentFactory"/>, no early-return
    /// path that bypasses this method, and no production caller of
    /// <see cref="Sprk.Bff.Api.Services.Ai.Memory.IMemoryCompositionService.ComposeAsync"/>
    /// today. Future wave 4-E tasks (076 layered context cards, 077 trust-frame injector,
    /// 079 composition target) will plug into this method at the documented insertion points
    /// below — they MUST NOT introduce a second composition seam in
    /// <see cref="SprkChatAgentFactory"/>.
    /// </para>
    /// <para>
    /// <b>FR-45 binding invariant (generalized by task 050 / FR-B-01)</b>: this method MUST call
    /// <see cref="IMemoryItemStore.ToRecordPromptFragmentAsync"/> via the
    /// <see cref="AppendRecordMemoryAsync"/> helper for both the generic (no-playbook) and
    /// playbook paths. As of architecture §11.1 the invocation was at line 627; after the
    /// task-078 MVP XML-doc additions on 2026-06-23 the invocation site shifted to
    /// <see cref="AppendRecordMemoryAsync"/>'s try-block (line number is NOT load-bearing —
    /// the test asserts the call exists, not its position). Do NOT regress this wiring —
    /// task 080 (binding regression test, retargeted by task 050) enforces it.
    /// </para>
    /// <para>
    /// <b>Future plug-in points (deferred — MVP Q5b cut)</b>:
    /// <list type="bullet">
    /// <item><description>
    /// <b>Trust-frame instruction injection (task 077)</b>: will plug in here AFTER persona
    /// resolution and BEFORE knowledge-scope enrichment (i.e., between the persona prompt
    /// assignment and <see cref="EnrichSystemPrompt"/>). Belongs to the static-prefix tier
    /// per architecture §6.2 (cacheable across turns within a session).
    /// </description></item>
    /// <item><description>
    /// <b>Layered context cards (task 076)</b>: will plug in here AFTER knowledge-scope
    /// enrichment and BEFORE entity enrichment (i.e., between <see cref="EnrichSystemPrompt"/>
    /// and <see cref="AppendEntityEnrichment"/>). Also belongs to the static-prefix tier.
    /// </description></item>
    /// <item><description>
    /// <b>Dynamic suffix via <see cref="IMemoryCompositionService.ComposeAsync"/> (task 079)</b>:
    /// will plug in here AFTER all static-prefix layers and AFTER matter-memory append, before
    /// the final <c>ChatContext</c> return. The composer's 4-layer output (recent-verbatim,
    /// compressed-mid, retrieved-old, pinned) joins the system prompt as the per-turn dynamic
    /// suffix. Pinned-tier FR-42 invariant is owned by the composer itself.
    /// </description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public async Task<ChatContext> GetContextAsync(
        string documentId,
        string tenantId,
        Guid? playbookId,
        ChatHostContext? hostContext = null,
        IReadOnlyList<string>? additionalDocumentIds = null,
        IReadOnlyList<ChatSessionFile>? uploadedFiles = null,
        string? sessionId = null,
        IReadOnlyList<SessionOutput>? ledgerOutputs = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Building ChatContext for document {DocumentId}, tenant {TenantId}, playbook {PlaybookId}, uploadedFileCount={UploadedFileCount}",
            documentId, tenantId, playbookId, uploadedFiles?.Count ?? 0);

        // R5 task 033 — normalize uploadedFiles to null-or-non-empty so all downstream
        // consumers (factory system-prompt suffix, tool reasoning) can use a single
        // `is { Count: > 0 }` check. ADR-015: manifest only — never enrich here.
        var normalizedUploadedFiles = uploadedFiles is { Count: > 0 } ? uploadedFiles : null;

        // When no playbook is specified (generic chat mode), return a default context
        // with no playbook-specific scoping. The agent will use a generic system prompt.
        if (playbookId is null)
        {
            _logger.LogInformation(
                "No playbook specified for document {DocumentId}; using generic chat context",
                documentId);

            // R6 Pillar 1 (task 005, D-A-05): data-driven persona resolution replaces the
            // hardcoded BuildDefaultSystemPrompt(null) call site. The resolver returns the
            // most-specific-wins persona (global SYS- < tenant CUST- < playbook-attached per
            // FR-03 / Q1). With no tenant CUST- override and no playbook bound, the seeded
            // SYS-DEFAULT row (task 004, sprk_aipersonaid=4fe49430-aa62-f111-ab0c-70a8a58ae145
            // on Spaarke Dev) returns the byte-identical text the legacy BuildDefaultSystemPrompt(null)
            // produced — preserving today's behavior per FR-04 binding.
            //
            // NFR-01 binding: persona augments the LLM but never replaces conversational
            // ability. The returned SystemPrompt is composed verbatim as the prompt opening;
            // the safety pipeline + memory + capability routing layers remain unchanged.
            //
            // Failure mode: when the resolver throws InvalidOperationException (catastrophic
            // SYS- seed-data failure per task 003's contract), we fall back to the legacy
            // hardcoded text exactly once with a CRITICAL log so operators see the seed gap
            // immediately. This is a defense-in-depth null-safety assertion — production
            // deployments MUST keep the SYS-DEFAULT row seeded (task 004 owns the seed).
            string defaultPrompt;
            try
            {
                var persona = await _scopeResolver.ResolvePersonaForChatAsync(
                    tenantId, playbookId, cancellationToken);

                // Defense-in-depth: contract for ResolvePersonaForChatAsync is to throw
                // InvalidOperationException on missing SYS-DEFAULT (catastrophic seed failure
                // per task 003). A null return is contract-violating but possible (test doubles,
                // mocks, fault injection). Treat null and missing SystemPrompt the same way as
                // the exception path — fall back to legacy text + CRITICAL log.
                if (persona is null || string.IsNullOrEmpty(persona.SystemPrompt))
                {
                    _logger.LogCritical(
                        "R6 Pillar 1 persona resolver returned null/empty for tenant {TenantId} — " +
                        "contract violation (expected InvalidOperationException). Falling back to " +
                        "legacy hardcoded prompt to preserve chat availability. Operator action " +
                        "required: verify task 004 SYS-DEFAULT row is seeded.",
                        tenantId);
                    defaultPrompt = BuildDefaultSystemPrompt(null);
                }
                else
                {
                    defaultPrompt = persona.SystemPrompt;
                    _logger.LogDebug(
                        "Resolved standalone-mode persona '{Name}' (scope={ScopeType}) for tenant {TenantId}",
                        persona.Name, persona.ScopeType, tenantId);
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogCritical(ex,
                    "R6 Pillar 1 persona resolver returned no SYS- default for tenant {TenantId} — " +
                    "catastrophic seed-data failure. Falling back to legacy hardcoded prompt to " +
                    "preserve chat availability. Operator action required: deploy task 004 SYS-DEFAULT row.",
                    tenantId);
                defaultPrompt = BuildDefaultSystemPrompt(null);
            }

            // 6 + 7. F-1/F-2/F-7 envelope-convergence (D1): the host-identity (Business), user-memory
            // (User, F-2 recall), and cross-session record-memory sections are now produced ONCE by the
            // Context Binder and consumed here via ContextEnvelopeRenderer / BoundInputs (single source —
            // the SAME seam the dispatch path uses). Falls back to the shipped direct-append path when no
            // binder is registered (compound-OFF / legacy tests) — byte-identical output either way.
            var boundGeneric = await ApplyBoundContextAsync(
                defaultPrompt, tenantId, hostContext, sessionId, ledgerOutputs, cancellationToken);
            defaultPrompt = boundGeneric.SystemPrompt;
            var defaultBoundEnvelope = boundGeneric.Envelope;

            // Still load document summary for inline context (skipped when documentId is null/empty)
            string? defaultDocSummary = null;
            IReadOnlyDictionary<string, string>? defaultMetadata = null;
            if (!string.IsNullOrEmpty(documentId))
            {
                try
                {
                    var doc = await _documentService.GetDocumentAsync(documentId, cancellationToken);
                    if (doc != null)
                    {
                        defaultDocSummary = doc.Summary ?? doc.Tldr;
                        var meta = new Dictionary<string, string>();
                        if (!string.IsNullOrWhiteSpace(doc.DocumentType))
                            meta["documentType"] = doc.DocumentType;
                        if (!string.IsNullOrWhiteSpace(doc.Name))
                            meta["documentName"] = doc.Name;
                        if (meta.Count > 0)
                            defaultMetadata = meta;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to load document summary for {DocumentId} in generic chat mode; continuing without",
                        documentId);
                }
            }

            // Standalone mode: when the chat has no document but has a valid host context
            // (entityType + entityId), provide a minimal KnowledgeScope so that DocumentSearch chat-tools
            // can entity-scope its discovery search. RagKnowledgeSourceIds is empty, meaning
            // SearchDocumentsAsync runs tenant-wide (no knowledge source filter) while
            // SearchDiscoveryAsync is constrained to the entity boundary via ParentEntityType/Id.
            ChatKnowledgeScope? defaultKnowledgeScope = null;
            if (!string.IsNullOrWhiteSpace(hostContext?.EntityType) &&
                !string.IsNullOrWhiteSpace(hostContext?.EntityId))
            {
                _logger.LogInformation(
                    "Standalone chat mode with host context ({EntityType}/{EntityId}); " +
                    "building entity-scoped knowledge scope for RAG search",
                    hostContext.EntityType, hostContext.EntityId);

                defaultKnowledgeScope = new ChatKnowledgeScope(
                    RagKnowledgeSourceIds: [],
                    InlineContent: null,
                    SkillInstructions: null,
                    ActiveDocumentId: string.IsNullOrEmpty(documentId) ? null : documentId,
                    ParentEntityType: hostContext.EntityType,
                    ParentEntityId: hostContext.EntityId);
            }

            return new ChatContext(
                SystemPrompt: defaultPrompt,
                DocumentSummary: defaultDocSummary,
                AnalysisMetadata: defaultMetadata,
                PlaybookId: null,
                KnowledgeScope: defaultKnowledgeScope,
                UploadedFiles: normalizedUploadedFiles,
                BoundEnvelope: defaultBoundEnvelope);
        }

        // 1. Load playbook to get ActionIds
        var playbook = await _playbookService.GetPlaybookAsync(playbookId.Value, cancellationToken);

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
            playbookId.Value, documentId, hostContext, additionalDocumentIds, cancellationToken);

        // 4. Enrich system prompt with inline knowledge and skill instructions
        systemPrompt = EnrichSystemPrompt(systemPrompt, knowledgeScope);

        // 5 + 5b. F-1/F-2/F-7 envelope-convergence (D1): host-identity (Business), user-memory (User,
        // F-2 recall), and cross-session record-memory are produced ONCE by the Context Binder and consumed
        // here via ContextEnvelopeRenderer / BoundInputs (single source — same seam as dispatch). Falls back
        // to the shipped direct-append path when no binder is registered — byte-identical output either way.
        var boundPlaybook = await ApplyBoundContextAsync(
            systemPrompt, tenantId, hostContext, sessionId, ledgerOutputs, cancellationToken);
        systemPrompt = boundPlaybook.SystemPrompt;
        var playbookBoundEnvelope = boundPlaybook.Envelope;

        // 6. Load document summary for inline context injection
        string? documentSummary = null;
        IReadOnlyDictionary<string, string>? analysisMetadata = null;

        try
        {
            var document = await _documentService.GetDocumentAsync(documentId, cancellationToken);
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
            KnowledgeScope: knowledgeScope,
            UploadedFiles: normalizedUploadedFiles,
            BoundEnvelope: playbookBoundEnvelope);
    }

    /// <summary>
    /// Resolves knowledge scope from the playbook's N:N relationships.
    /// Partitions knowledge sources by type and composes skill instructions.
    /// </summary>
    private async Task<ChatKnowledgeScope?> ResolveKnowledgeScopeAsync(
        Guid playbookId,
        string documentId,
        ChatHostContext? hostContext,
        IReadOnlyList<string>? additionalDocumentIds,
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

            // Normalize additional document IDs: remove nulls/blanks and enforce max cap
            var normalizedAdditionalDocs = additionalDocumentIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .Take(ChatKnowledgeScope.MaxAdditionalDocuments)
                .ToList();

            if (normalizedAdditionalDocs is { Count: > 0 })
            {
                _logger.LogInformation(
                    "Including {AdditionalDocCount} additional document(s) in knowledge scope for playbook {PlaybookId}",
                    normalizedAdditionalDocs.Count, playbookId);
            }

            return new ChatKnowledgeScope(
                RagKnowledgeSourceIds: ragSourceIds,
                InlineContent: string.IsNullOrWhiteSpace(inlineContent) ? null : inlineContent,
                SkillInstructions: string.IsNullOrWhiteSpace(skillInstructions) ? null : skillInstructions,
                ActiveDocumentId: documentId,
                ParentEntityType: hostContext?.EntityType,
                ParentEntityId: hostContext?.EntityId,
                AdditionalDocumentIds: normalizedAdditionalDocs is { Count: > 0 } ? normalizedAdditionalDocs : null);
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
    /// <remarks>
    /// R6 task 068 (D-C-22): when the shared <see cref="IPromptBudgetTracker"/> is wired,
    /// each fragment (knowledge-inline, skill-instructions) reserves budget independently
    /// — denial logs truncation telemetry via the tracker and omits the fragment. Behaviour
    /// is unchanged when the tracker is null (legacy tests + pre-task-068 environments).
    /// </remarks>
    private string EnrichSystemPrompt(string systemPrompt, ChatKnowledgeScope? scope)
    {
        if (scope is null)
            return systemPrompt;

        var sb = new StringBuilder(systemPrompt);

        if (!string.IsNullOrWhiteSpace(scope.InlineContent))
        {
            if (TryReservePromptBudget("knowledge-inline", scope.InlineContent))
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine("## Reference Materials");
                sb.AppendLine(scope.InlineContent);
            }
        }

        if (!string.IsNullOrWhiteSpace(scope.SkillInstructions))
        {
            if (TryReservePromptBudget("skill-instructions", scope.SkillInstructions))
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine("## Specialized Instructions");
                sb.AppendLine(scope.SkillInstructions);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// R6 task 068 (D-C-22) — shared-tracker budget reservation helper. When the tracker
    /// is null (legacy path), returns true unconditionally so behaviour is unchanged.
    /// When the tracker is wired, attempts to reserve the estimated tokens for the named
    /// layer; on denial, the tracker emits truncation telemetry and this method returns
    /// false so the caller omits its fragment.
    /// </summary>
    private bool TryReservePromptBudget(string layer, string fragment)
    {
        if (_promptBudgetTracker is null)
        {
            return true;
        }

        var tokens = EstimateTokenCount(fragment);
        if (tokens <= 0)
        {
            return true;
        }

        return _promptBudgetTracker.TryReserve(layer, tokens, sessionId: null, tenantId: null);
    }

    /// <summary>
    /// Appends the host-record identity block to the system prompt when the host
    /// context provides EntityType + EntityId (G-P3 UAT round-1 H7, 2026-07-07:
    /// the record IDENTITY — type, id, and the "this record" binding instruction —
    /// always renders; the display-name and page-type sentences degrade individually).
    /// </summary>
    /// <remarks>
    /// <para>Guards:</para>
    /// <list type="bullet">
    ///   <item><description>EntityType + EntityId must be present (the record identity) —
    ///     the ONLY hard guards since H7. Pre-H7, an unresolvable EntityName or an
    ///     unmapped PageType silently dropped the WHOLE block, leaving the loop blind
    ///     to its host record.</description></item>
    ///   <item><description>EntityName: client-supplied or server-side lazy-fetched
    ///     (R7 Wave 12 task 151 / audit 120 Gap B); unresolvable → id-only phrasing.</description></item>
    ///   <item><description>PageType: defaulted (task 152) / mapped to a label; missing,
    ///     "unknown", or unmapped → the page sentence is omitted.</description></item>
    ///   <item><description>Enrichment block must be ≤ <see cref="MaxEnrichmentTokens"/> tokens</description></item>
    ///   <item><description>Total system prompt must not exceed <see cref="MaxSystemPromptTokenBudget"/> tokens</description></item>
    /// </list>
    /// </remarks>
    private async Task<string> AppendEntityEnrichmentAsync(
        string systemPrompt,
        ChatHostContext? hostContext,
        CancellationToken cancellationToken)
    {
        // Direct-append path (the no-binder fallback, retained so BusinessSliceDeterminismContractTests —
        // constructed WITHOUT a binder — stays green UNEDITED, and compound-OFF is unaffected). Resolves
        // the host-identity inputs, builds the SHARED HostIdentityProducer block, and applies the guards.
        var inputs = await ResolveEntityEnrichmentInputsAsync(hostContext, cancellationToken);
        if (inputs is null)
        {
            return systemPrompt;
        }

        var enrichmentBlock = HostIdentityProducer.BuildEnrichmentBlock(
            inputs.Value.EntityType, inputs.Value.EntityId, inputs.Value.EntityName, inputs.Value.HumanReadablePageType);

        return AppendEnrichmentBlock(systemPrompt, enrichmentBlock);
    }

    /// <summary>
    /// Resolves the host-record identity inputs (guards → server-side name lazy-fetch → page-type mapping)
    /// — the TOP of the R1 entity-enrichment logic, extracted so BOTH the direct-append fallback
    /// (<see cref="AppendEntityEnrichmentAsync"/>) and the D1 Context-Binder path resolve them identically
    /// (feeding the SAME name/page into the Binder's Business fragment → byte-identical output). Returns
    /// null when there is no host record (the H7 guard). NFR-04: deterministic — no timestamp/GUID minted.
    /// </summary>
    private async Task<(string EntityType, string EntityId, string? EntityName, string? HumanReadablePageType)?>
        ResolveEntityEnrichmentInputsAsync(ChatHostContext? hostContext, CancellationToken cancellationToken)
    {
        // Guard: no host context / no record identity at all (G-P3 UAT round-1 H7): EntityType + EntityId
        // are the ONLY hard requirements; name + page-type sentences degrade individually below.
        if (hostContext is null ||
            string.IsNullOrWhiteSpace(hostContext.EntityType) ||
            string.IsNullOrWhiteSpace(hostContext.EntityId))
        {
            return null;
        }

        // Resolve EntityName — populated from client OR server-side lazy-fetch (T151). Optional since H7:
        // an unresolvable name degrades to the id-only identity line.
        var entityName = hostContext.EntityName;
        if (string.IsNullOrWhiteSpace(entityName))
        {
            entityName = await TryResolveEntityNameAsync(
                hostContext.EntityType, hostContext.EntityId, cancellationToken);
        }

        // R7 Wave 12 task 152 (audit 120 Gap C): apply DefaultPageType when client omitted the field.
        // "unknown" is the client's explicit not-known signal and is NOT defaulted. Since H7 the page
        // sentence is OPTIONAL — missing/unknown/unmapped page types drop only the sentence.
        var pageType = string.IsNullOrWhiteSpace(hostContext.PageType)
            ? DefaultPageType
            : hostContext.PageType;
        string? humanReadablePageType = null;
        if (!string.IsNullOrWhiteSpace(pageType) &&
            !string.Equals(pageType, "unknown", StringComparison.OrdinalIgnoreCase) &&
            !PageTypeLabels.TryGetValue(pageType, out humanReadablePageType))
        {
            _logger.LogDebug(
                "Unmapped page type '{PageType}'; omitting the page sentence from entity enrichment",
                pageType);
        }

        return (hostContext.EntityType, hostContext.EntityId, entityName, humanReadablePageType);
    }

    /// <summary>
    /// Applies the host-identity block's budget guards + append — the TAIL of the R1 entity-enrichment
    /// logic, extracted so the direct-append fallback and the D1 Binder path (which passes the byte-identical
    /// <c>envelope.Business.Fragment</c>) share ONE guard/append implementation. Returns <paramref name="systemPrompt"/>
    /// unchanged when the block is empty, over the ≤<see cref="MaxEnrichmentTokens"/> cap, or denied by the
    /// shared budget tracker — byte-identical semantics to the pre-refactor inline tail.
    /// </summary>
    private string AppendEnrichmentBlock(string systemPrompt, string? enrichmentBlock)
    {
        if (string.IsNullOrEmpty(enrichmentBlock))
        {
            return systemPrompt;
        }

        // Guard: enrichment block itself must be ≤ MaxEnrichmentTokens
        var enrichmentTokenEstimate = EstimateTokenCount(enrichmentBlock);
        if (enrichmentTokenEstimate > MaxEnrichmentTokens)
        {
            _logger.LogWarning(
                "Entity enrichment block exceeds token cap ({EstimatedTokens} > {MaxTokens}); skipping enrichment",
                enrichmentTokenEstimate, MaxEnrichmentTokens);
            return systemPrompt;
        }

        // Guard: total system prompt budget. When the shared per-turn tracker is wired, it owns the
        // authoritative accounting + truncation telemetry (R6 task 068). Fallback: when the tracker is
        // absent (legacy tests, pre-task-068 environments), re-estimate locally against the static
        // MaxSystemPromptTokenBudget so behaviour is unchanged on those paths.
        if (_promptBudgetTracker is not null)
        {
            if (!_promptBudgetTracker.TryReserve(
                    "entity-enrichment",
                    enrichmentTokenEstimate,
                    sessionId: null,
                    tenantId: null))
            {
                _logger.LogWarning(
                    "R6 task 068: entity enrichment denied by shared prompt budget tracker (requested={EnrichmentTokens}, remaining={Remaining}); skipping enrichment",
                    enrichmentTokenEstimate, _promptBudgetTracker.Remaining);
                return systemPrompt;
            }
        }
        else
        {
            var currentTokenEstimate = EstimateTokenCount(systemPrompt);
            if (currentTokenEstimate + enrichmentTokenEstimate > MaxSystemPromptTokenBudget)
            {
                _logger.LogWarning(
                    "System prompt token budget would be exceeded ({CurrentTokens} + {EnrichmentTokens} > {Budget}); skipping entity enrichment",
                    currentTokenEstimate, enrichmentTokenEstimate, MaxSystemPromptTokenBudget);
                return systemPrompt;
            }
        }

        return systemPrompt + "\n\n" + enrichmentBlock;
    }

    // ── F-1/F-2/F-7 envelope-convergence (D1): the ONE per-turn bind + consumption of the bound envelope
    //    for the interactive prompt's host-identity (Business), user-memory (User), and record-memory
    //    sections. When no binder is registered, the shipped direct-append path runs (byte-identical). ──

    private async Task<(string SystemPrompt, PublicContracts.ContextEnvelope? Envelope)> ApplyBoundContextAsync(
        string systemPrompt,
        string tenantId,
        ChatHostContext? hostContext,
        string? sessionId,
        IReadOnlyList<SessionOutput>? ledgerOutputs,
        CancellationToken cancellationToken)
    {
        // No binder (compound-OFF / the many legacy provider tests + BusinessSliceDeterminismContractTests):
        // fall back to the shipped direct-append path — the byte-identical legacy behaviour.
        if (_contextBinder is null)
        {
            systemPrompt = await AppendEntityEnrichmentAsync(systemPrompt, hostContext, cancellationToken);
            systemPrompt = await AppendRecordMemoryAsync(systemPrompt, tenantId, hostContext, cancellationToken);
            return (systemPrompt, null);
        }

        // Resolve the host-identity inputs ONCE so the Binder's Business fragment is byte-identical to the
        // legacy append (same resolved name + page label feed HostIdentityProducer on both sides).
        var enrichmentInputs = await ResolveEntityEnrichmentInputsAsync(hostContext, cancellationToken);

        BoundInputs? bound = null;
        try
        {
            var contextTurn = (ledgerOutputs is { Count: > 0 } ? ledgerOutputs.Max(o => o.Turn) : 0) + 1;
            bound = await _contextBinder.BindAsync(
                new ContextBindingRequest
                {
                    // Feed the resolved (lazily-fetched) name + page label so the envelope's Business
                    // fragment matches the interactive prompt byte-for-byte (D2 parity).
                    HostEntityType = enrichmentInputs?.EntityType ?? hostContext?.EntityType,
                    HostEntityId = enrichmentInputs?.EntityId ?? hostContext?.EntityId,
                    HostEntityName = enrichmentInputs?.EntityName ?? hostContext?.EntityName,
                    HostPageTypeLabel = enrichmentInputs?.HumanReadablePageType,
                    ConversationTail = Context.ConversationContextProducer.BuildConversationTail(ledgerOutputs),
                    LedgerOutputs = ledgerOutputs,
                    TenantId = tenantId,
                    SessionId = sessionId,
                    Turn = contextTurn,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Soft-fail: a bind failure must NEVER fail the message turn (envelope/fingerprint are
            // telemetry-grade). Fall back to the direct-append path so the prompt still assembles. NFR-07.
            _logger.LogWarning(ex,
                "F-1 (D1): interactive Context Binder bind failed — falling back to the direct-append path " +
                "for this turn (the message turn is never failed by context assembly). session={SessionId}",
                sessionId);
            systemPrompt = await AppendEntityEnrichmentAsync(systemPrompt, hostContext, cancellationToken);
            systemPrompt = await AppendRecordMemoryAsync(systemPrompt, tenantId, hostContext, cancellationToken);
            return (systemPrompt, null);
        }

        // D1 live path — CONSUME the bound envelope via the renderer / BoundInputs (single source):
        // (a) F-2 user-memory recall fragment (NEW, additive) — stable prefix, User before Business;
        if (bound.Context.User?.Fragment is { Length: > 0 } userFragment)
        {
            systemPrompt = AppendUserMemoryFragment(systemPrompt, userFragment);
        }

        // (b) host-identity (Business) — the SAME guards/budget as the legacy append, byte-identical block;
        systemPrompt = AppendEnrichmentBlock(systemPrompt, bound.Context.Business?.Fragment);

        // (c) cross-session record-memory — the Binder-produced fragment (byte-identical), same budget guards.
        systemPrompt = AppendRecordMemoryFragmentGuarded(
            systemPrompt, tenantId, hostContext, bound.RecordMemoryFragment);

        return (systemPrompt, bound.Context);
    }

    /// <summary>
    /// Appends the F-2 USER-scope memory recall fragment (D3) to the stable prompt prefix (User before
    /// Business), under the shared budget tracker. Additive — absent for callers/fixtures with no user
    /// memory, so the prompt bytes are unchanged there (the negative parity pin).
    /// </summary>
    private string AppendUserMemoryFragment(string systemPrompt, string userFragment)
    {
        var fragmentTokens = EstimateTokenCount(userFragment);
        if (_promptBudgetTracker is not null)
        {
            if (!_promptBudgetTracker.TryReserve("user-memory", fragmentTokens, sessionId: null, tenantId: null))
            {
                _logger.LogWarning(
                    "F-2: user-memory recall fragment denied by shared prompt budget tracker (requested={Tokens}, remaining={Remaining}); skipping",
                    fragmentTokens, _promptBudgetTracker.Remaining);
                return systemPrompt;
            }
        }
        else
        {
            var currentTokenEstimate = EstimateTokenCount(systemPrompt);
            if (currentTokenEstimate + fragmentTokens > MaxSystemPromptTokenBudget)
            {
                _logger.LogWarning(
                    "F-2: user-memory recall fragment would exceed system prompt budget ({Current} + {Fragment} > {Budget}); skipping",
                    currentTokenEstimate, fragmentTokens, MaxSystemPromptTokenBudget);
                return systemPrompt;
            }
        }

        _logger.LogInformation(
            "F-2: appended user-memory recall fragment to system prompt (fragmentTokens={Tokens} fragmentLength={Length})",
            fragmentTokens, userFragment.Length);

        return systemPrompt + "\n\n" + userFragment;
    }

    /// <summary>
    /// R7 Wave 12 task 151 (audit 120 Gap B) — server-side lazy-fetch of the entity's
    /// display name when the client did not populate <see cref="ChatHostContext.EntityName"/>.
    /// Result is memoized per-request to avoid duplicate Dataverse round-trips on the same
    /// entity within the same chat session-message turn (which composes the prompt twice
    /// in some code paths — generic + playbook).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Soft-fail posture: any failure path (no <see cref="IGenericEntityService"/> wired,
    /// invalid GUID, unknown entity type, Dataverse outage, missing <c>sprk_name</c> attribute)
    /// returns null, which causes <see cref="AppendEntityEnrichmentAsync"/> to skip enrichment
    /// — preserving the pre-T151 guard behaviour. Chat requests are NEVER failed by this method.
    /// </para>
    /// <para>
    /// Cache scope is per-request: <see cref="_entityNameCache"/> is an instance field of the
    /// Scoped <see cref="PlaybookChatContextProvider"/>, so it is created fresh per request and
    /// disposed with the DI scope. A null cached value means "fetch failed previously this
    /// request; do not retry" — avoids retry storms on persistent Dataverse outages.
    /// </para>
    /// </remarks>
    /// <param name="entityType">Normalized entity type from <see cref="ChatHostContext"/>
    /// (post-T150 boundary normalization, e.g. <c>"matter"</c>) or a raw Dataverse logical
    /// name (e.g. <c>"sprk_matter"</c>). Both forms map to the same logical name.</param>
    /// <param name="entityId">String GUID of the entity record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The display name, or null when unfetchable / unavailable.</returns>
    private async Task<string?> TryResolveEntityNameAsync(
        string entityType,
        string entityId,
        CancellationToken ct)
    {
        if (_entityService is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId))
        {
            return null;
        }

        if (!Guid.TryParse(entityId, out var entityGuid))
        {
            _logger.LogDebug(
                "T151 EntityName lazy-fetch: EntityId '{EntityId}' is not a valid GUID; skipping",
                entityId);
            return null;
        }

        var logicalName = ToDataverseLogicalName(entityType);
        var cacheKey = $"{logicalName}:{entityGuid:N}";

        // Per-request memoization. A cached null means "previous fetch failed; do not retry".
        if (_entityNameCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var entity = await _entityService.RetrieveAsync(
                logicalName,
                entityGuid,
                new[] { "sprk_name" },
                ct);

            var name = entity?.GetAttributeValue<string>("sprk_name");
            _entityNameCache[cacheKey] = name; // also caches null when name missing

            if (string.IsNullOrWhiteSpace(name))
            {
                _logger.LogDebug(
                    "T151 EntityName lazy-fetch: '{LogicalName}' {EntityId} has no sprk_name; skipping enrichment",
                    logicalName, entityGuid);
            }
            else
            {
                _logger.LogDebug(
                    "T151 EntityName lazy-fetch: resolved '{LogicalName}' {EntityId} → '{Name}'",
                    logicalName, entityGuid, name);
            }

            return name;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancellation is a normal control-flow signal; surface to caller.
            throw;
        }
        catch (Exception ex)
        {
            // Soft failure — log + cache null to avoid retry storm + return null so caller
            // skips enrichment per original guard behaviour. Chat request continues normally.
            _entityNameCache[cacheKey] = null;
            _logger.LogWarning(ex,
                "T151 EntityName lazy-fetch failed for '{LogicalName}' {EntityId}; continuing without entity enrichment",
                logicalName, entityGuid);
            return null;
        }
    }

    /// <summary>
    /// Maps either a T150-normalized canonical entity type (e.g. <c>"matter"</c>) or a raw
    /// Dataverse logical name (e.g. <c>"sprk_matter"</c>) to the raw logical name needed
    /// by <see cref="IGenericEntityService.RetrieveAsync"/>. Pass-through for unknown
    /// types so future entity additions don't require a code change here.
    /// </summary>
    private static string ToDataverseLogicalName(string entityType)
    {
        return entityType.ToLowerInvariant() switch
        {
            "matter" => "sprk_matter",
            "project" => "sprk_project",
            "invoice" => "sprk_invoice",
            _ => entityType
        };
    }

    /// <summary>
    /// R6 Pillar 7 (task 068, D-C-21 / FR-45), GENERALIZED by task 050 (FR-B-01) — appends the
    /// cross-session RECORD memory fragment to the chat system-prompt assembly. When the host
    /// context identifies ANY record (matter, project, invoice, work assignment, ...) and the
    /// store is wired, the per-record structured fragment (parties / key dates / prior analyses /
    /// key facts) is appended so cross-session same-record conversations are coherent. Matter
    /// hosts render a byte-identical heading to the pre-generalization fragment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Invariant</b>: this method does NOT modify <see cref="IMemoryItemStore"/>. The store's
    /// production code (Cosmos-backed subject-partitioned read + 500-token render budget +
    /// confidence filtering, ported verbatim from the retired matter-only service) is consumed
    /// unchanged.
    /// </para>
    /// <para>
    /// <b>Budget</b>: the rendered fragment is accounted via the shared
    /// <see cref="IPromptBudgetTracker"/> when present. The tracker emits truncation
    /// telemetry on denial. The fragment itself is bounded to ~500 tokens by the store;
    /// this method just contributes that token cost to the shared 8K budget tracker on
    /// behalf of the record-memory layer.
    /// </para>
    /// <para>
    /// <b>ADR-015</b>: the fragment may contain user-authored record facts (parties,
    /// dates). It is part of the LLM prompt by design; it is NOT logged. This method
    /// logs only (entityType, entityId, tenantId, fragmentLength) — deterministic
    /// identifiers and counts only.
    /// </para>
    /// <para>
    /// <b>Soft failure</b>: any exception path (Cosmos outage, ETag conflict, etc.)
    /// degrades to "no record memory this turn"; the rest of the prompt assembly
    /// continues. Matches the soft-failure posture of the surrounding subsystems.
    /// </para>
    /// </remarks>
    private async Task<string> AppendRecordMemoryAsync(
        string systemPrompt,
        string tenantId,
        ChatHostContext? hostContext,
        CancellationToken cancellationToken)
    {
        // Guard: store not wired (legacy tests, pre-task-068 envs)
        if (_memoryItemStore is null)
        {
            return systemPrompt;
        }

        // Guard: host context must identify a record — ANY entity type (task 050 generalization;
        // the matter-only check was the FR-B-01 regression to remove).
        if (hostContext is null
            || string.IsNullOrWhiteSpace(hostContext.EntityType)
            || string.IsNullOrWhiteSpace(hostContext.EntityId))
        {
            return systemPrompt;
        }

        try
        {
            var fragment = await _memoryItemStore.ToRecordPromptFragmentAsync(
                hostContext.EntityType, hostContext.EntityId, cancellationToken);

            return AppendRecordMemoryFragmentGuarded(systemPrompt, tenantId, hostContext, fragment);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Soft failure — record memory is enhancing, not required. Cross-session
            // coherence degrades for this turn; the rest of the prompt assembly continues.
            _logger.LogWarning(ex,
                "R6 task 068 / task 050: failed to load record memory for {EntityType}={EntityId} tenant={TenantId}; continuing without",
                hostContext.EntityType, hostContext.EntityId, tenantId);
            return systemPrompt;
        }
    }

    /// <summary>
    /// Applies the record-memory fragment's budget guards + append — the TAIL of the R1 record-memory
    /// logic, extracted so the direct-append fallback (<see cref="AppendRecordMemoryAsync"/>) and the D1
    /// Binder path (which passes the byte-identical <see cref="BoundInputs.RecordMemoryFragment"/> produced
    /// from the SAME <see cref="IMemoryItemStore.ToRecordPromptFragmentAsync"/>) share ONE guard/append
    /// implementation. Returns <paramref name="systemPrompt"/> unchanged when the fragment is empty or the
    /// budget denies it — byte-identical semantics to the pre-refactor inline tail. <paramref name="hostContext"/>
    /// is non-null on every call site (identifiers-only logging, NFR-07).
    /// </summary>
    private string AppendRecordMemoryFragmentGuarded(
        string systemPrompt, string tenantId, ChatHostContext? hostContext, string? fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            _logger.LogDebug(
                "R6 task 068 / task 050: record memory empty for {EntityType}={EntityId} tenant={TenantId}; no fragment appended",
                hostContext?.EntityType, hostContext?.EntityId, tenantId);
            return systemPrompt;
        }

        var fragmentTokens = EstimateTokenCount(fragment);

        // Budget gate via shared tracker when present
        if (_promptBudgetTracker is not null)
        {
            if (!_promptBudgetTracker.TryReserve(
                    "record-memory",
                    fragmentTokens,
                    sessionId: null,
                    tenantId: tenantId))
            {
                _logger.LogWarning(
                    "R6 task 068 / task 050: record memory fragment denied by shared prompt budget tracker (requested={Tokens}, remaining={Remaining}, {EntityType}={EntityId}); skipping",
                    fragmentTokens, _promptBudgetTracker.Remaining, hostContext?.EntityType, hostContext?.EntityId);
                return systemPrompt;
            }
        }
        else
        {
            var currentTokenEstimate = EstimateTokenCount(systemPrompt);
            if (currentTokenEstimate + fragmentTokens > MaxSystemPromptTokenBudget)
            {
                _logger.LogWarning(
                    "R6 task 068 / task 050: record memory fragment would exceed system prompt budget ({Current} + {Fragment} > {Budget}); skipping",
                    currentTokenEstimate, fragmentTokens, MaxSystemPromptTokenBudget);
                return systemPrompt;
            }
        }

        _logger.LogInformation(
            "R6 task 068 / task 050: appended record memory fragment to system prompt — {EntityType}={EntityId} tenant={TenantId} fragmentTokens={Tokens} fragmentLength={Length}",
            hostContext?.EntityType, hostContext?.EntityId, tenantId, fragmentTokens, fragment.Length);

        return systemPrompt + "\n\n" + fragment;
    }

    /// <summary>
    /// Rough token estimate for English text: word_count * 1.3.
    /// Uses whitespace splitting for word count.
    /// </summary>
    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var wordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return (int)Math.Ceiling(wordCount * 1.3);
    }

    /// <summary>
    /// Appends an <c>### Active Capabilities</c> section to the system prompt listing
    /// scope-contributed commands so the AI model is aware of available slash commands.
    ///
    /// Called by <see cref="SprkChatAgentFactory"/> after resolving the command catalog
    /// from <see cref="DynamicCommandResolver"/>. Only scope-category commands are included
    /// (system and playbook commands are not repeated here).
    ///
    /// ADR-015: Only capability labels and trigger strings are included — no scope
    /// configuration data (descriptions, internal IDs) is exposed in the prompt.
    ///
    /// Budget: kept under ~200 tokens to avoid crowding the 8K system prompt budget.
    /// </summary>
    /// <param name="systemPrompt">The current system prompt to append to.</param>
    /// <param name="commands">The full command catalog from <see cref="DynamicCommandResolver"/>.</param>
    /// <returns>The enriched system prompt with an Active Capabilities section, or unchanged if no scope commands.</returns>
    internal static string AppendActiveCapabilities(
        string systemPrompt,
        IReadOnlyList<CommandEntry>? commands)
    {
        if (commands is null || commands.Count == 0)
        {
            return systemPrompt;
        }

        // Filter to scope-contributed commands only (Category is not "system" or "playbook")
        var scopeCommands = commands
            .Where(c => !string.Equals(c.Category, "system", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(c.Category, "playbook", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (scopeCommands.Count == 0)
        {
            return systemPrompt;
        }

        var sb = new StringBuilder(systemPrompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("### Active Capabilities");
        sb.AppendLine("The following scope-contributed commands are available. You may suggest these to the user when relevant:");

        foreach (var cmd in scopeCommands)
        {
            sb.AppendLine($"- {cmd.Trigger}: {cmd.Description}");
        }

        // Guard: ensure the capabilities section stays within ~200 tokens
        var capabilitiesSection = sb.ToString()[systemPrompt.Length..];
        var capabilityTokens = EstimateTokenCount(capabilitiesSection);

        if (capabilityTokens > 200)
        {
            // Truncate to first N commands that fit within budget
            sb = new StringBuilder(systemPrompt);
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("### Active Capabilities");

            var tokenCount = EstimateTokenCount("### Active Capabilities\n");
            foreach (var cmd in scopeCommands)
            {
                var line = $"- {cmd.Trigger}: {cmd.Description}\n";
                var lineTokens = EstimateTokenCount(line);
                if (tokenCount + lineTokens > 190) // Leave margin
                {
                    break;
                }
                tokenCount += lineTokens;
                sb.Append(line);
            }
        }

        var result = sb.ToString();
        var totalTokens = EstimateTokenCount(result);
        if (totalTokens > MaxSystemPromptTokenBudget)
        {
            // If adding capabilities would exceed the total budget, skip them
            return systemPrompt;
        }

        return result;
    }

    /// <summary>
    /// Builds a sensible default system prompt when the playbook Action is unavailable.
    /// </summary>
    private static string BuildDefaultSystemPrompt(string? playbookName)
    {
        if (!string.IsNullOrWhiteSpace(playbookName))
        {
            return $"""
                You are an AI assistant configured for the '{playbookName}' workflow, helping users understand and analyze documents.

                ## Instructions
                - Provide helpful, accurate responses grounded in the document content
                - If asked to locate specific information, cite relevant sections
                - Format responses in clear, readable Markdown

                ## Output Format
                Provide your response in Markdown with appropriate headings and structure.
                """;
        }

        // Standalone mode — comprehensive prompt for Spaarke AI without a specific playbook.
        // This prompt guides the model to use its available tools proactively.
        return """
            You are Spaarke AI, an intelligent assistant for legal professionals using the Spaarke platform.
            You help with document analysis, matter management, legal research, financial analysis, and general questions about the user's work.

            ## Your Capabilities
            You have access to powerful tools — use them proactively:

            - **SearchDocuments**: Search the document index to find relevant content. Use this when the user asks about documents, contracts, agreements, filings, or any content stored in Spaarke.
            - **SearchDiscovery**: Broad discovery search across all indexed documents. Use this when the user asks to find matters, projects, documents, or explore what's available.
            - **GetKnowledgeSource**: Retrieve full content from a specific knowledge source. Use after SearchDocuments identifies a relevant source.
            - **SearchKnowledgeBase**: Search the knowledge base for reference information, policies, and best practices.
            - **GetAnalysisResult** / **GetAnalysisSummary**: Retrieve prior analysis results for documents that have been analyzed.
            - **RefineText**: Help the user improve, rewrite, or restructure text.

            ## Instructions
            - When the user asks about their matters, projects, or documents, **always use SearchDiscovery or SearchDocuments first** — don't say you can't access their data.
            - When you find relevant documents, summarize what you found and offer to analyze further.
            - If the user asks to analyze a document but none is loaded, suggest they upload one or help them search for it.
            - Cite sources and document names when referencing search results.
            - Be proactive — if a search returns relevant results, highlight key findings.
            - Format responses in clear, readable Markdown with headings and structure.

            ## What You Know About
            - Legal documents (contracts, agreements, court filings, memos, briefs)
            - Matter management (case details, timelines, budgets, parties)
            - Financial data (budgets, invoices, billing, cost analysis)
            - Document comparison and review workflows
            - Legal research and case law (when Bing Grounding is available)
            """;
    }
}
