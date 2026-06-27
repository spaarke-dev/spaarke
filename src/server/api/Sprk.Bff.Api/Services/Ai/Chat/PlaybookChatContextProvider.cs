using System.Linq;
using System.Text;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;
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
    /// </summary>
    internal const int MaxEnrichmentTokens = 100;

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

    private readonly IScopeResolverService _scopeResolver;
    private readonly IPlaybookService _playbookService;
    private readonly IDocumentDataverseService _documentService;
    private readonly ILogger<PlaybookChatContextProvider> _logger;

    // R6 Pillar 7 (task 068, D-C-21) — cross-session matter-memory activation. When the
    // host context identifies a matter, the per-matter structured fragment is appended
    // to the system prompt under the shared budget tracker. Registered unconditionally
    // in AiPersistenceModule, so required (non-nullable) here.
    private readonly IMatterMemoryService _matterMemoryService;

    // R6 Pillar 7 (task 068, D-C-22) — shared 8K system-prompt budget tracker. Remains
    // nullable because the tracker registration is gated by the compound
    // (Analysis:Enabled && DocumentIntelligence:Enabled) flag in AnalysisServicesModule
    // while this provider is registered unconditionally in AiModule. When the AI gate is
    // off the chat factory becomes NullSprkChatAgentFactory so this provider is never
    // resolved in practice — but the nullable shape keeps the DI graph honest.
    private readonly IPromptBudgetTracker? _promptBudgetTracker;

    public PlaybookChatContextProvider(
        IScopeResolverService scopeResolver,
        IPlaybookService playbookService,
        IDocumentDataverseService documentService,
        ILogger<PlaybookChatContextProvider> logger,
        IMatterMemoryService matterMemoryService,
        IPromptBudgetTracker? promptBudgetTracker = null)
    {
        _scopeResolver = scopeResolver;
        _playbookService = playbookService;
        _documentService = documentService;
        _logger = logger;
        _matterMemoryService = matterMemoryService;
        _promptBudgetTracker = promptBudgetTracker;
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
    /// <b>FR-45 binding invariant</b>: this method MUST call
    /// <see cref="IMatterMemoryService.ToSystemPromptFragmentAsync"/> via the
    /// <see cref="AppendMatterMemoryAsync"/> helper for both the generic (no-playbook) and
    /// playbook paths. As of architecture §11.1 the invocation was at line 627; after the
    /// task-078 MVP XML-doc additions on 2026-06-23 the invocation site shifted to
    /// <see cref="AppendMatterMemoryAsync"/>'s try-block (currently ~line 679, line number
    /// is NOT load-bearing — the test asserts the call exists, not its position). Do NOT
    /// regress this wiring — task 080 (binding regression test) enforces it.
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

            // 6. Append entity metadata enrichment (generic mode)
            defaultPrompt = AppendEntityEnrichment(defaultPrompt, hostContext);

            // 7. R6 task 068 (D-C-21 / FR-45) — append cross-session matter memory fragment
            // when the host context identifies a matter and the IMatterMemoryService is wired.
            // ADR-015: fragment may carry user-authored facts (parties / dates / analyses);
            // it lives in the prompt by design, not in logs. Soft-fails to no-op.
            defaultPrompt = await AppendMatterMemoryAsync(
                defaultPrompt, tenantId, hostContext, cancellationToken);

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
            // (entityType + entityId), provide a minimal KnowledgeScope so that DocumentSearchTools
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
                UploadedFiles: normalizedUploadedFiles);
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

        // 5. Append entity metadata enrichment (after all other sections)
        systemPrompt = AppendEntityEnrichment(systemPrompt, hostContext);

        // 5b. R6 task 068 (D-C-21 / FR-45) — append cross-session matter memory fragment
        // when the host context identifies a matter and the IMatterMemoryService is wired.
        // Same soft-fail posture + budget gating as the generic-mode path.
        systemPrompt = await AppendMatterMemoryAsync(
            systemPrompt, tenantId, hostContext, cancellationToken);

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
            UploadedFiles: normalizedUploadedFiles);
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
    /// Appends an entity metadata enrichment block to the system prompt when
    /// the host context provides a valid EntityName and PageType.
    /// </summary>
    /// <remarks>
    /// Guards:
    /// - EntityName must be non-null/non-empty
    /// - PageType must be non-null/non-empty and not "unknown"
    /// - PageType must map to a known human-readable label
    /// - Enrichment block must be ≤ <see cref="MaxEnrichmentTokens"/> tokens
    /// - Total system prompt must not exceed <see cref="MaxSystemPromptTokenBudget"/> tokens
    /// </remarks>
    private string AppendEntityEnrichment(string systemPrompt, ChatHostContext? hostContext)
    {
        // Guard: no host context at all
        if (hostContext is null)
            return systemPrompt;

        // Guard: EntityName must be present
        if (string.IsNullOrWhiteSpace(hostContext.EntityName))
            return systemPrompt;

        // Guard: PageType must be present and not "unknown"
        if (string.IsNullOrWhiteSpace(hostContext.PageType) ||
            string.Equals(hostContext.PageType, "unknown", StringComparison.OrdinalIgnoreCase))
            return systemPrompt;

        // Guard: PageType must map to a known label
        if (!PageTypeLabels.TryGetValue(hostContext.PageType, out var humanReadablePageType))
        {
            _logger.LogDebug(
                "Unmapped page type '{PageType}'; skipping entity enrichment",
                hostContext.PageType);
            return systemPrompt;
        }

        // Build the enrichment block
        var enrichmentBlock =
            $"Context: You are assisting with {hostContext.EntityType} record '{hostContext.EntityName}'. " +
            $"The user is viewing the {humanReadablePageType}.";

        // Guard: enrichment block itself must be ≤ MaxEnrichmentTokens
        var enrichmentTokenEstimate = EstimateTokenCount(enrichmentBlock);
        if (enrichmentTokenEstimate > MaxEnrichmentTokens)
        {
            _logger.LogWarning(
                "Entity enrichment block exceeds token cap ({EstimatedTokens} > {MaxTokens}); skipping enrichment",
                enrichmentTokenEstimate, MaxEnrichmentTokens);
            return systemPrompt;
        }

        // Guard: total system prompt budget. When the shared per-turn tracker is wired,
        // it owns the authoritative accounting + truncation telemetry (R6 task 068).
        // Fallback: when the tracker is absent (legacy tests, pre-task-068 environments),
        // we re-estimate locally against the static MaxSystemPromptTokenBudget so behaviour
        // is unchanged on those paths.
        if (_promptBudgetTracker is not null)
        {
            if (!_promptBudgetTracker.TryReserve(
                    "entity-enrichment",
                    enrichmentTokenEstimate,
                    sessionId: null,
                    tenantId: null))
            {
                // Tracker emits truncation telemetry; we log soft-fail rationale here.
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

    /// <summary>
    /// R6 Pillar 7 (task 068, D-C-21 / FR-45) — activates the existing production
    /// <see cref="IMatterMemoryService"/> into the chat system-prompt assembly. When
    /// the host context identifies a matter (EntityType == "matter") and the memory
    /// service is wired (post-R6 environments), the per-matter structured fragment
    /// (parties / key dates / prior analyses / key facts) is appended to the system
    /// prompt so cross-session same-matter conversations are coherent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Invariant</b>: this method does NOT modify
    /// <see cref="MatterMemoryService"/>. The service's production code (Cosmos-backed
    /// read + 500-token render budget + confidence filtering) is consumed unchanged.
    /// </para>
    /// <para>
    /// <b>Budget</b>: the rendered fragment is accounted via the shared
    /// <see cref="IPromptBudgetTracker"/> when present. The tracker emits truncation
    /// telemetry on denial. The fragment itself is bounded to ~500 tokens by
    /// <see cref="MatterMemoryService"/>; this method just contributes that token
    /// cost to the shared 8K budget tracker on behalf of the matter-memory layer.
    /// </para>
    /// <para>
    /// <b>ADR-015</b>: the fragment may contain user-authored matter facts (parties,
    /// dates). It is part of the LLM prompt by design; it is NOT logged. This method
    /// logs only (matterId, tenantId, fragmentLength) — deterministic identifiers and
    /// counts only.
    /// </para>
    /// <para>
    /// <b>Soft failure</b>: any exception path (Cosmos outage, ETag conflict, etc.)
    /// degrades to "no matter memory this turn"; the rest of the prompt assembly
    /// continues. Matches the soft-failure posture of the surrounding subsystems.
    /// </para>
    /// </remarks>
    private async Task<string> AppendMatterMemoryAsync(
        string systemPrompt,
        string tenantId,
        ChatHostContext? hostContext,
        CancellationToken cancellationToken)
    {
        // Guard: service not wired (legacy tests, pre-task-068 envs)
        if (_matterMemoryService is null)
        {
            return systemPrompt;
        }

        // Guard: host context must identify a matter
        if (hostContext is null
            || string.IsNullOrWhiteSpace(hostContext.EntityType)
            || string.IsNullOrWhiteSpace(hostContext.EntityId)
            || !string.Equals(hostContext.EntityType, "matter", StringComparison.OrdinalIgnoreCase))
        {
            return systemPrompt;
        }

        // Guard: tenant required for Cosmos partition key
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return systemPrompt;
        }

        try
        {
            var fragment = await _matterMemoryService.ToSystemPromptFragmentAsync(
                tenantId, hostContext.EntityId, cancellationToken);

            if (string.IsNullOrWhiteSpace(fragment))
            {
                _logger.LogDebug(
                    "R6 task 068: matter memory empty for matter={MatterId} tenant={TenantId}; no fragment appended",
                    hostContext.EntityId, tenantId);
                return systemPrompt;
            }

            var fragmentTokens = EstimateTokenCount(fragment);

            // Budget gate via shared tracker when present
            if (_promptBudgetTracker is not null)
            {
                if (!_promptBudgetTracker.TryReserve(
                        "matter-memory",
                        fragmentTokens,
                        sessionId: null,
                        tenantId: tenantId))
                {
                    _logger.LogWarning(
                        "R6 task 068: matter memory fragment denied by shared prompt budget tracker (requested={Tokens}, remaining={Remaining}, matter={MatterId}); skipping",
                        fragmentTokens, _promptBudgetTracker.Remaining, hostContext.EntityId);
                    return systemPrompt;
                }
            }
            else
            {
                var currentTokenEstimate = EstimateTokenCount(systemPrompt);
                if (currentTokenEstimate + fragmentTokens > MaxSystemPromptTokenBudget)
                {
                    _logger.LogWarning(
                        "R6 task 068: matter memory fragment would exceed system prompt budget ({Current} + {Fragment} > {Budget}); skipping",
                        currentTokenEstimate, fragmentTokens, MaxSystemPromptTokenBudget);
                    return systemPrompt;
                }
            }

            _logger.LogInformation(
                "R6 task 068: appended matter memory fragment to system prompt — matter={MatterId} tenant={TenantId} fragmentTokens={Tokens} fragmentLength={Length}",
                hostContext.EntityId, tenantId, fragmentTokens, fragment.Length);

            return systemPrompt + "\n\n" + fragment;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Soft failure — matter memory is enhancing, not required. Cross-session
            // coherence degrades for this turn; the rest of the prompt assembly continues.
            _logger.LogWarning(ex,
                "R6 task 068: failed to load matter memory for matter={MatterId} tenant={TenantId}; continuing without",
                hostContext.EntityId, tenantId);
            return systemPrompt;
        }
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
