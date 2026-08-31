using System.Text.Json;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// One proposed proactive follow-on chip: a cataloged capability the Assistant
/// suggests for the focused tab. <see cref="TargetBindingId"/> is always one of the
/// pre-filtered candidate Binding ids (hallucinated ids are dropped by
/// <see cref="AssistantSuggestionService"/>). The chip rides the EXISTING deterministic
/// Click path when the user clicks it — this record is a proposal, never a dispatch.
/// </summary>
/// <param name="TargetBindingId">The chosen capability's <c>sprk_playbookconsumer</c> id (a supplied candidate).</param>
/// <param name="Label">Short, content-specific chip label to render.</param>
/// <param name="Reason">Developer-facing one-line rationale (dev-visible selection trace, FR-B6/task 024). Never shown to the end user.</param>
public sealed record SuggestedChip(string TargetBindingId, string Label, string? Reason);

/// <summary>
/// spaarkeai-assistant-enhancements-r4 task 021a (FR-04) — the two honest kinds of follow-on
/// suggestion the grounded proposer may emit for the CONVERSATIONAL (after-a-response) moment.
/// The <c>kind</c> is STRUCTURAL, not a keyword heuristic: a suggestion is a capability iff it
/// carries a real, model-selected <c>targetBindingId</c>; otherwise it is a question.
/// </summary>
public enum SuggestedFollowupKind
{
    /// <summary>"Do something": carries a real <c>targetBindingId</c> the model SELECTED from the
    /// supplied candidate menu; clicking dispatches that exact Binding via the existing Click path —
    /// guaranteed to work (a dead-end is structurally impossible).</summary>
    Capability,

    /// <summary>"Ask the assistant something": carries only text; clicking re-enters the grounded
    /// agent loop, which is safe by construction (ADR-039 grounded outcomes — dispatch, a cited
    /// answer, a clarifying question, or an honest refusal, never a hard dead-end).</summary>
    Question,
}

/// <summary>
/// One typed follow-on suggestion (task 021a / FR-04). A <see cref="SuggestedFollowupKind.Capability"/>
/// followup carries a non-null <see cref="TargetBindingId"/> that is ALWAYS one of the supplied
/// candidate Binding ids (off-catalog ids are dropped by <see cref="AssistantSuggestionService"/>);
/// a <see cref="SuggestedFollowupKind.Question"/> followup carries only <see cref="Label"/> (the
/// question text) and no binding id. The proposer authors the words; it never authors a capability's
/// routing target — that is a real id it selected.
/// </summary>
/// <param name="Kind">Capability (has a targetBindingId) or Question (text only).</param>
/// <param name="TargetBindingId">The selected candidate Binding id for a capability; <c>null</c> for a question.</param>
/// <param name="Label">The chip text: an imperative phrase for a capability, an interrogative for a question.</param>
/// <param name="Reason">Developer-facing one-line rationale (dev-visible selection trace; never shown to the end user or emitted on the wire).</param>
public sealed record SuggestedFollowup(
    SuggestedFollowupKind Kind,
    string? TargetBindingId,
    string Label,
    string? Reason);

/// <summary>
/// FR-B3/B5 (spaarkeai-assistant-enhancements-r2 task 022) — the proactive-suggestion facade behind
/// <c>POST /api/ai/chat/sessions/{id}/suggest</c>: resolves + runs the catalog-authored
/// <c>SUGGEST-FOLLOWUPS</c> Action (<c>sprk_consumertype = "assistant-suggest"</c>) via the SAME
/// Linear AI Consumer primitives (<see cref="IActionResolver"/> / <see cref="IActionRunner"/>) that
/// <see cref="CommunicationProposeAi"/> uses — no <c>SprkChat</c> fork, no new dispatch protocol,
/// no new store.
/// </summary>
/// <remarks>
/// <para>
/// <b>Concrete-by-default (ADR-010)</b>. Registered + injected as a concrete class, NOT behind an
/// interface: it has exactly one implementation and no Null-Object variant is needed (unlike
/// <see cref="CommunicationProposeAi"/>, whose interface exists solely for its kill-switch Null swap).
/// The kill-switch-OFF path is handled by graceful degradation — its Linear-consumer deps resolve to
/// their Null variants, whose throw the facade's best-effort try/catch turns into an empty result.
/// </para>
/// <para>
/// <b>ADR-039 posture</b>. The context-type narrowing is a DETERMINISTIC pre-filter
/// (<see cref="ConsumerRoutingService.FilterByContextType"/>) over the closed catalog — the only
/// permitted aid. The ONE grounded turn is the sole decider of WHICH ≤3 capabilities to propose; it
/// is a PROPOSER, not a dispatcher (the chips ride the existing Click path on user click). Model
/// output is constrained to the supplied candidate set: <see cref="ParseSuggestions"/> DROPS any
/// suggestion whose id is not a supplied candidate, so no uncataloged capability can be proposed.
/// </para>
/// <para>
/// <b>ADR-040 posture</b>. The suggestion turn consumes NO tool reads — it reasons only over the
/// pre-filtered candidates + the focused tab's compact server-derived visible state — so there is no
/// tool chain to ledger and no new persistence store (store-before-render is vacuously satisfied). The
/// suggestion is ephemeral UI, like the reactive chip surface it feeds.
/// </para>
/// <para>
/// <b>ADR-015 posture</b>. The tab content passed to the model is derived SERVER-SIDE from persisted
/// workspace state (<see cref="IWorkspaceStateService.GetTabsAsync"/> →
/// <see cref="SprkChatAgentFactory.TryDeriveVisibleState"/> →
/// <see cref="SprkChatAgentFactory.FormatVisibleStateFields"/> with <c>contentVisible: true</c>) — the
/// SAME bounded compact-ambient shape the chat turn uses. Client-supplied content is never trusted, and
/// content is derived only for a tab the user has marked <see cref="WorkspaceTab.VisibleToAssistant"/>.
/// </para>
/// <para>
/// <b>Best-effort (NFR-04 style)</b>. Every failure mode (AI disabled / no Action routed / no
/// candidates / completion failure / malformed output) is caught + logged; the method returns an empty
/// list rather than throwing — a silent proactive surface must never break the chat turn.
/// </para>
/// </remarks>
public sealed class AssistantSuggestionService
{
    /// <summary>Hard cap on returned suggestions (FR-B5 — ≤3 chips).</summary>
    internal const int MaxSuggestions = 3;

    /// <summary>
    /// Upper bound on how many pre-filtered candidates are handed to the model, to bound prompt size.
    /// The candidate list is already deterministically ordered by
    /// <see cref="ConsumerRoutingService.SelectTextProjectable"/>, so the take is deterministic.
    /// </summary>
    internal const int MaxCandidates = 25;

    /// <summary>Clip cap (chars) on the user half of the conversation tail (task 021a). Bounds prompt size.</summary>
    internal const int MaxTailUserChars = 800;

    /// <summary>Clip cap (chars) on the assistant half of the conversation tail (task 021a). Bounds prompt size.</summary>
    internal const int MaxTailAssistantChars = 1600;

    private static readonly JsonSerializerOptions OperandJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IActionResolver _actionResolver;
    private readonly IActionRunner _actionRunner;
    private readonly IConsumerRoutingService _consumerRouting;
    private readonly IWorkspaceStateService _workspaceState;
    private readonly ILogger<AssistantSuggestionService> _logger;

    public AssistantSuggestionService(
        IActionResolver actionResolver,
        IActionRunner actionRunner,
        IConsumerRoutingService consumerRouting,
        IWorkspaceStateService workspaceState,
        ILogger<AssistantSuggestionService> logger)
    {
        _actionResolver = actionResolver ?? throw new ArgumentNullException(nameof(actionResolver));
        _actionRunner = actionRunner ?? throw new ArgumentNullException(nameof(actionRunner));
        _consumerRouting = consumerRouting ?? throw new ArgumentNullException(nameof(consumerRouting));
        _workspaceState = workspaceState ?? throw new ArgumentNullException(nameof(workspaceState));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Run the ONE grounded suggestion turn for a focused workspace tab and return the ≤3
    /// content-specific follow-on chips it selected + phrased. Best-effort: never throws; returns an
    /// empty list on any failure, when the AI feature is disabled, when no candidate capabilities match
    /// the context type, or when the model proposes nothing.
    /// </summary>
    /// <param name="sessionId">Chat session id (the focused tab lives in this session's workspace state).</param>
    /// <param name="tenantId">Caller tenant (workspace-state read + Action run scope).</param>
    /// <param name="contextType">The focused tab's context type (closed FR-B1 set). Required — an empty/blank value yields no suggestions.</param>
    /// <param name="activeTabId">The focused tab's stable id (client focus-stamp <c>tabId</c>); identifies which tab to ground in.</param>
    public async Task<IReadOnlyList<SuggestedChip>> SuggestAsync(
        string sessionId,
        string tenantId,
        string contextType,
        string? activeTabId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contextType))
        {
            // No focused context type ⇒ nothing to scope suggestions to. Silent no-op.
            return Array.Empty<SuggestedChip>();
        }

        // Candidate universe = the loop-projectable capability catalog, deterministically pre-filtered
        // to this ONE focused context type (ADR-039-permitted context scoping).
        var resolved = await ResolveCandidatesAsync(
            projectable => ConsumerRoutingService.FilterByContextType(projectable, contextType),
            sessionId, contextType, ct).ConfigureAwait(false);
        if (resolved is null)
        {
            return Array.Empty<SuggestedChip>();
        }

        var (candidates, candidateIds) = resolved.Value;

        // The focused tab's compact content — server-derived (ADR-015), never client bytes.
        var activeTab = await BuildActiveTabAsync(tenantId, sessionId, activeTabId, ct).ConfigureAwait(false);
        var operand = BuildInput(contextType, activeTab, candidates, conversationTail: null);

        var output = await RunSuggestActionAsync(operand, tenantId, sessionId, contextType, ct).ConfigureAwait(false);
        if (output is null)
        {
            return Array.Empty<SuggestedChip>();
        }

        // Proactive contract (r2 FR-B3/B5): capability chips ONLY — the tab-open moment has no
        // conversation to ask a question about, so any question kind the model emits is dropped here
        // to keep the /suggest endpoint's SuggestedChip contract stable.
        var chips = ParseFollowups(output.Value, candidateIds)
            .Where(f => f.Kind == SuggestedFollowupKind.Capability && f.TargetBindingId is not null)
            .Select(f => new SuggestedChip(f.TargetBindingId!, f.Label, f.Reason))
            .ToList();
        _logger.LogInformation(
            "Suggest resolved (proactive): session={SessionId}, contextType={ContextType}, candidateCount={CandidateCount}, " +
            "chipCount={ChipCount}, bindingIds={BindingIds}.",
            sessionId, contextType, candidates.Count, chips.Count,
            string.Join(",", chips.Select(c => c.TargetBindingId)));
        return chips;
    }

    /// <summary>
    /// spaarkeai-assistant-enhancements-r4 task 021a (FR-04) — run the ONE grounded suggestion turn for
    /// the CONVERSATIONAL (after-a-chat-response) moment and return the ≤3 TYPED two-kind followups it
    /// selected + phrased. This is the generalization of <see cref="SuggestAsync"/> from the focused-tab
    /// moment to the after-a-response moment: the candidate menu is scoped by the UNION of the open-tab
    /// context-types (+ the active tab's context type), and the operand additionally carries a bounded
    /// conversation tail (the just-completed user message + assistant response) so the labels are
    /// relevant to what was just said — not the tab alone.
    /// </summary>
    /// <remarks>
    /// Same ADR posture as <see cref="SuggestAsync"/> — one grounded PROPOSER (not a decider), a
    /// deterministic context PRE-FILTER as the only aid, the closed-catalog guard (a capability whose
    /// <c>targetBindingId</c> is not a supplied candidate is dropped), no new store (ADR-040 vacuous).
    /// Best-effort: never throws; returns an empty list on any failure, when the AI feature is disabled,
    /// or when the model proposes nothing. Cadence is structural: this method has NO response-length gate
    /// — the caller runs it once per turn, so absence means "nothing relevant", never a hidden skip.
    /// </remarks>
    /// <param name="sessionId">Chat session id.</param>
    /// <param name="tenantId">Caller tenant (candidate + Action run scope).</param>
    /// <param name="userMessage">The user's just-sent message (the conversation tail's user half).</param>
    /// <param name="assistantResponse">The assistant's just-completed response (the conversation tail's assistant half).</param>
    /// <param name="activeContextType">The focused tab's context type (closed FR-B1 set), if any — folded into the candidate scope + operand.</param>
    /// <param name="activeTabId">The focused tab's stable id, for server-derived content grounding (ADR-015).</param>
    /// <param name="openTabContextTypes">The union of the open tabs' context-types (task 030 <see cref="WidgetContextTypeResolver.ResolveOpenTabContextTypes"/>); scopes the candidate menu. Empty ⇒ no narrowing (still capped).</param>
    public async Task<IReadOnlyList<SuggestedFollowup>> SuggestForConversationAsync(
        string sessionId,
        string tenantId,
        string userMessage,
        string assistantResponse,
        string? activeContextType,
        string? activeTabId,
        IReadOnlyCollection<string>? openTabContextTypes,
        CancellationToken ct = default)
    {
        // Candidate scope = UNION of the open-tab context-types + the active tab's context-type. The
        // session/host scope surfaces naturally: empty-tag Bindings match ANY context (per
        // FilterByContextTypes), so a session-wide capability is always a candidate. An empty union ⇒
        // no narrowing (parity with the proactive blank-passthrough) — a conversation with no open tab
        // still gets followups from the full projectable catalog (capped).
        var scopeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (openTabContextTypes is not null)
        {
            foreach (var t in openTabContextTypes)
            {
                if (!string.IsNullOrWhiteSpace(t))
                {
                    scopeTypes.Add(t);
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(activeContextType))
        {
            scopeTypes.Add(activeContextType);
        }

        var resolved = await ResolveCandidatesAsync(
            projectable => ConsumerRoutingService.FilterByContextTypes(projectable, scopeTypes),
            sessionId, scopeTypes.Count == 0 ? "(session-wide)" : string.Join("+", scopeTypes), ct)
            .ConfigureAwait(false);
        if (resolved is null)
        {
            return Array.Empty<SuggestedFollowup>();
        }

        var (candidates, candidateIds) = resolved.Value;

        var activeTab = await BuildActiveTabAsync(tenantId, sessionId, activeTabId, ct).ConfigureAwait(false);
        var conversationTail = BuildConversationTail(userMessage, assistantResponse);
        var operand = BuildInput(activeContextType ?? string.Empty, activeTab, candidates, conversationTail);

        var output = await RunSuggestActionAsync(operand, tenantId, sessionId, "conversation", ct).ConfigureAwait(false);
        if (output is null)
        {
            return Array.Empty<SuggestedFollowup>();
        }

        var followups = ParseFollowups(output.Value, candidateIds);
        _logger.LogInformation(
            "Suggest resolved (conversational): session={SessionId}, candidateCount={CandidateCount}, " +
            "followupCount={FollowupCount}, capabilities={Capabilities}, questions={Questions}.",
            sessionId, candidates.Count, followups.Count,
            followups.Count(f => f.Kind == SuggestedFollowupKind.Capability),
            followups.Count(f => f.Kind == SuggestedFollowupKind.Question));
        return followups;
    }

    /// <summary>
    /// Resolve + deterministically pre-filter the candidate capability menu, then bound it to
    /// <see cref="MaxCandidates"/> and precompute the id set for the closed-catalog guard. Returns
    /// <c>null</c> (best-effort) when resolution fails or no candidate matches — the caller degrades to
    /// an empty suggestion list. <paramref name="filter"/> is the ADR-039-permitted deterministic aid
    /// (single-type or union context pre-filter); it is the ONLY narrowing applied.
    /// </summary>
    private async Task<(IReadOnlyList<Binding> Candidates, HashSet<string> Ids)?> ResolveCandidatesAsync(
        Func<IReadOnlyList<Binding>, IReadOnlyList<Binding>> filter,
        string sessionId,
        string contextLabel,
        CancellationToken ct)
    {
        IReadOnlyList<Binding> candidates;
        try
        {
            var projectable = await _consumerRouting
                .ListTextProjectableBindingsAsync(cancellationToken: ct)
                .ConfigureAwait(false);
            candidates = filter(projectable);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Suggest skipped: candidate resolution failed (session={SessionId}, context={Context}).",
                sessionId, contextLabel);
            return null;
        }

        if (candidates.Count == 0)
        {
            _logger.LogDebug(
                "Suggest: no candidate capabilities for context={Context} (session={SessionId}).",
                contextLabel, sessionId);
            return null;
        }

        var bounded = candidates.Count > MaxCandidates
            ? candidates.Take(MaxCandidates).ToList()
            : candidates;
        var ids = new HashSet<string>(
            bounded.Select(b => b.BindingId.ToString()),
            StringComparer.OrdinalIgnoreCase);
        return (bounded, ids);
    }

    /// <summary>
    /// Resolve the catalog SUGGEST-FOLLOWUPS Action and run the ONE grounded turn over
    /// <paramref name="operand"/>. Returns <c>null</c> (best-effort, NFR-04) when the AI feature is
    /// disabled (Null resolver throws), no Action is routed, or the completion fails — so a suggestion
    /// failure never breaks the chat turn.
    /// </summary>
    private async Task<JsonElement?> RunSuggestActionAsync(
        JsonElement operand,
        string tenantId,
        string sessionId,
        string contextLabel,
        CancellationToken ct)
    {
        AnalysisAction action;
        try
        {
            action = await _actionResolver
                .ResolveAsync(ConsumerTypes.AssistantSuggest, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Suggest skipped: SUGGEST-FOLLOWUPS Action could not be resolved for consumerType '{ConsumerType}'.",
                ConsumerTypes.AssistantSuggest);
            return null;
        }

        var boundInputs = new BoundInputs
        {
            Context = ContextEnvelopeReferenceProducer.Assemble(),
            Operand = new ResolvedOperand
            {
                Channel = OperandChannel.Input,
                Kind = OperandKind.PreResolved,
                Input = operand,
            },
        };
        var runContext = new LinearRunContext
        {
            ConsumerType = ConsumerTypes.AssistantSuggest,
            TenantId = tenantId,
        };

        try
        {
            return await _actionRunner.RunAsync(action, boundInputs, runContext, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Suggest skipped: SUGGEST-FOLLOWUPS completion failed (session={SessionId}, context={Context}).",
                sessionId, contextLabel);
            return null;
        }
    }

    /// <summary>
    /// Build the bounded conversation tail for the operand: the just-completed user message + assistant
    /// response, each clipped to a modest cap so the input stays generous-but-bounded (the model needs
    /// the turn to be relevant, not the whole transcript).
    /// </summary>
    private static ConversationTail BuildConversationTail(string? userMessage, string? assistantResponse) =>
        new(Clip(userMessage, MaxTailUserChars), Clip(assistantResponse, MaxTailAssistantChars));

    private static string Clip(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxChars ? text : text[..maxChars] + "…";
    }

    /// <summary>
    /// Resolve the focused tab's compact, server-derived visible state (ADR-015 Path A). Returns a
    /// minimal identity block (empty content) when the tab cannot be found or is not
    /// <see cref="WorkspaceTab.VisibleToAssistant"/> — the suggestion still runs (candidates are
    /// context-scoped) but with no content-bearing fields. Never throws.
    /// </summary>
    private async Task<ActiveTabInfo> BuildActiveTabAsync(
        string tenantId,
        string sessionId,
        string? activeTabId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(activeTabId))
        {
            return ActiveTabInfo.Empty;
        }

        try
        {
            var tabs = await _workspaceState.GetTabsAsync(tenantId, sessionId, ct).ConfigureAwait(false);
            var tab = tabs.FirstOrDefault(t => string.Equals(t.Id, activeTabId, StringComparison.Ordinal));
            if (tab is null)
            {
                return ActiveTabInfo.Empty;
            }

            var content = string.Empty;
            if (tab.VisibleToAssistant)
            {
                var state = SprkChatAgentFactory.TryDeriveVisibleState(tab);
                if (state is not null)
                {
                    // Same bounded compact-ambient rendering the chat turn's active tab uses.
                    content = SprkChatAgentFactory
                        .FormatVisibleStateFields(state, contentVisible: true)
                        .Trim();
                }
            }

            return new ActiveTabInfo(tab.WidgetType ?? string.Empty, content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Suggest: active-tab visible-state derivation failed (session={SessionId}, tabId={TabId}); proceeding content-less.",
                sessionId, activeTabId);
            return ActiveTabInfo.Empty;
        }
    }

    /// <summary>
    /// Build the Action's operand as the structured <c>## Input</c> element. The PROACTIVE moment
    /// (<paramref name="conversationTail"/> == null) emits the r2 <c>{ contextType, activeTab, candidates }</c>
    /// shape byte-for-byte (so the /suggest path's operand is unchanged); the CONVERSATIONAL moment
    /// (task 021a) additionally carries <c>conversationTail</c> so the model can phrase labels relevant
    /// to the just-completed turn. The <c>## Input</c> section renders this element verbatim
    /// (<see cref="Sprk.Bff.Api.Services.Ai.PromptInputSection"/>), so the operand keys ARE the model's
    /// input — the Action's <c>input</c> schema is descriptive.
    /// </summary>
    private static JsonElement BuildInput(
        string contextType,
        ActiveTabInfo activeTab,
        IReadOnlyList<Binding> candidates,
        ConversationTail? conversationTail)
    {
        var activeTabPayload = new
        {
            widgetType = activeTab.WidgetType,
            content = activeTab.Content,
        };
        var candidatePayload = candidates.Select(b => new
        {
            bindingId = b.BindingId.ToString(),
            description = b.ToolDescription ?? string.Empty,
        }).ToArray();

        object payload = conversationTail is null
            ? new
            {
                contextType,
                activeTab = activeTabPayload,
                candidates = candidatePayload,
            }
            : new
            {
                contextType,
                conversationTail = new
                {
                    userMessage = conversationTail.Value.UserMessage,
                    assistantResponse = conversationTail.Value.AssistantResponse,
                },
                activeTab = activeTabPayload,
                candidates = candidatePayload,
            };

        using var doc = JsonSerializer.SerializeToDocument(payload, OperandJsonOptions);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Parse the Action's <c>{ suggestions: [...] }</c> output into validated TYPED followups
    /// (task 021a). Each item resolves to a <see cref="SuggestedFollowupKind.Capability"/> (it carries a
    /// <c>targetBindingId</c>, or its <c>kind</c> is "capability") or a
    /// <see cref="SuggestedFollowupKind.Question"/> (kind "question", or a blank targetBindingId). Kind
    /// is inferred when absent for tolerance. Enforces the closed-catalog contract on capabilities ONLY:
    /// a capability is kept only when its <c>targetBindingId</c> is one of the supplied
    /// <paramref name="candidateIds"/> (ADR-039 — no uncataloged capability may be proposed); an
    /// off-catalog id is dropped. A question carries no id and re-enters the grounded loop (safe by
    /// construction), so it is not catalog-checked. Blank labels and duplicates (by binding id for
    /// capabilities, by label for questions) are dropped; the list is capped at
    /// <see cref="MaxSuggestions"/>. Malformed entries are skipped rather than throwing.
    /// </summary>
    private IReadOnlyList<SuggestedFollowup> ParseFollowups(JsonElement output, HashSet<string> candidateIds)
    {
        if (output.ValueKind != JsonValueKind.Object
            || !output.TryGetProperty("suggestions", out var suggestions)
            || suggestions.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SuggestedFollowup>();
        }

        var list = new List<SuggestedFollowup>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in suggestions.EnumerateArray())
        {
            if (list.Count >= MaxSuggestions)
            {
                break;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var label = GetString(item, "label");
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            var targetBindingId = GetString(item, "targetBindingId");
            var kindRaw = GetString(item, "kind");
            var reason = GetString(item, "reason");

            // Kind is STRUCTURAL: an explicit "question" kind OR a blank targetBindingId ⇒ question;
            // anything else ⇒ capability (kind inferred when absent, for tolerance to cached outputs).
            var isQuestion = string.Equals(kindRaw, "question", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(targetBindingId);

            if (isQuestion)
            {
                // A question re-enters the grounded agent loop (ADR-039 — safe by construction). No
                // binding id, no catalog check; dedup by normalized label.
                if (!seen.Add("q:" + label!.Trim().ToLowerInvariant()))
                {
                    continue;
                }
                list.Add(new SuggestedFollowup(SuggestedFollowupKind.Question, null, label!.Trim(), reason));
                continue;
            }

            if (!candidateIds.Contains(targetBindingId!))
            {
                // ADR-039 closed-catalog guard: the model named a capability id we did not offer. Drop it.
                _logger.LogDebug(
                    "Suggest: dropped capability suggestion with off-catalog targetBindingId={TargetBindingId}.",
                    targetBindingId);
                continue;
            }

            if (!seen.Add("c:" + targetBindingId!.ToLowerInvariant()))
            {
                continue; // dedup — one chip per capability.
            }

            list.Add(new SuggestedFollowup(SuggestedFollowupKind.Capability, targetBindingId, label!.Trim(), reason));
        }

        return list;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// The focused tab's widget type + bounded compact content for the suggestion operand.
    /// <see cref="Content"/> is the server-derived visible-state block (empty when the tab is not
    /// visible to the assistant or has no derivable content). The tab has no separate title field
    /// (the identifying fields — e.g. a document filename — are carried inside <see cref="Content"/>).
    /// </summary>
    private readonly record struct ActiveTabInfo(string WidgetType, string Content)
    {
        public static ActiveTabInfo Empty { get; } = new(string.Empty, string.Empty);
    }

    /// <summary>
    /// The bounded conversation tail carried in the CONVERSATIONAL operand (task 021a): the just-completed
    /// user message + assistant response, each clipped (<see cref="MaxTailUserChars"/> /
    /// <see cref="MaxTailAssistantChars"/>). This is the "what was just said" grounding that lets the
    /// proposer phrase followups relevant to the turn — distinct from the active tab's visible state.
    /// </summary>
    private readonly record struct ConversationTail(string UserMessage, string AssistantResponse);
}
