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

        // 1. Candidate universe = the loop-projectable capability catalog, deterministically
        //    pre-filtered to this context type (ADR-039-permitted context scoping; reuses the
        //    existing query — no new Dataverse round-trip shape).
        IReadOnlyList<Binding> candidates;
        try
        {
            var projectable = await _consumerRouting
                .ListTextProjectableBindingsAsync(cancellationToken: ct)
                .ConfigureAwait(false);
            candidates = ConsumerRoutingService.FilterByContextType(projectable, contextType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Suggest skipped: candidate resolution failed (session={SessionId}, contextType={ContextType}).",
                sessionId, contextType);
            return Array.Empty<SuggestedChip>();
        }

        if (candidates.Count == 0)
        {
            _logger.LogDebug(
                "Suggest: no candidate capabilities for contextType={ContextType} (session={SessionId}).",
                contextType, sessionId);
            return Array.Empty<SuggestedChip>();
        }

        var boundedCandidates = candidates.Count > MaxCandidates
            ? candidates.Take(MaxCandidates).ToList()
            : candidates;
        var candidateIds = new HashSet<string>(
            boundedCandidates.Select(b => b.BindingId.ToString()),
            StringComparer.OrdinalIgnoreCase);

        // 2. The focused tab's compact content — server-derived (ADR-015), never client bytes.
        var activeTab = await BuildActiveTabAsync(tenantId, sessionId, activeTabId, ct).ConfigureAwait(false);

        // 3. Resolve the catalog SUGGEST-FOLLOWUPS Action (prompt + schema = maker-editable data).
        AnalysisAction action;
        try
        {
            action = await _actionResolver
                .ResolveAsync(ConsumerTypes.AssistantSuggest, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // NFR-04: AI disabled (NullActionResolver throws) or no Binding/Action routed yet ⇒ no
            // suggestions, never a throw into the tab-open path.
            _logger.LogWarning(ex,
                "Suggest skipped: SUGGEST-FOLLOWUPS Action could not be resolved for consumerType '{ConsumerType}'.",
                ConsumerTypes.AssistantSuggest);
            return Array.Empty<SuggestedChip>();
        }

        // 4. Compose the operand + run the ONE grounded turn.
        var operand = BuildInput(contextType, activeTab, boundedCandidates);
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

        JsonElement output;
        try
        {
            output = await _actionRunner.RunAsync(action, boundInputs, runContext, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Suggest skipped: SUGGEST-FOLLOWUPS completion failed (session={SessionId}, contextType={ContextType}).",
                sessionId, contextType);
            return Array.Empty<SuggestedChip>();
        }

        // 5. Parse + validate against the closed candidate set (drop hallucinated ids), cap at 3.
        var chips = ParseSuggestions(output, candidateIds);
        _logger.LogInformation(
            "Suggest resolved: session={SessionId}, contextType={ContextType}, candidateCount={CandidateCount}, " +
            "chipCount={ChipCount}, bindingIds={BindingIds}.",
            sessionId, contextType, boundedCandidates.Count, chips.Count,
            string.Join(",", chips.Select(c => c.TargetBindingId)));
        return chips;
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
    /// Build the Action's <c>{ contextType, activeTab, candidates }</c> operand as the structured
    /// <c>## Input</c> element. Mirrors the <c>input</c> section of
    /// <c>infra/dataverse/actions/suggest-followups.action.json</c> 1:1.
    /// </summary>
    private static JsonElement BuildInput(
        string contextType,
        ActiveTabInfo activeTab,
        IReadOnlyList<Binding> candidates)
    {
        var payload = new
        {
            contextType,
            activeTab = new
            {
                widgetType = activeTab.WidgetType,
                content = activeTab.Content,
            },
            candidates = candidates.Select(b => new
            {
                bindingId = b.BindingId.ToString(),
                description = b.ToolDescription ?? string.Empty,
            }).ToArray(),
        };

        using var doc = JsonSerializer.SerializeToDocument(payload, OperandJsonOptions);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Parse the Action's <c>{ suggestions: [...] }</c> output into validated chips. Enforces the
    /// closed-catalog contract: a suggestion is kept only when its <c>targetBindingId</c> is one of the
    /// supplied <paramref name="candidateIds"/> (ADR-039 — no uncataloged capability may be proposed);
    /// blank ids/labels and duplicate ids are dropped; the list is capped at
    /// <see cref="MaxSuggestions"/>. Malformed entries are skipped rather than throwing.
    /// </summary>
    private IReadOnlyList<SuggestedChip> ParseSuggestions(JsonElement output, HashSet<string> candidateIds)
    {
        if (output.ValueKind != JsonValueKind.Object
            || !output.TryGetProperty("suggestions", out var suggestions)
            || suggestions.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SuggestedChip>();
        }

        var list = new List<SuggestedChip>();
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

            var targetBindingId = GetString(item, "targetBindingId");
            var label = GetString(item, "label");
            if (string.IsNullOrWhiteSpace(targetBindingId) || string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            if (!candidateIds.Contains(targetBindingId))
            {
                // ADR-039 closed-catalog guard: the model named an id we did not offer. Drop it.
                _logger.LogDebug(
                    "Suggest: dropped suggestion with off-catalog targetBindingId={TargetBindingId}.",
                    targetBindingId);
                continue;
            }

            if (!seen.Add(targetBindingId))
            {
                continue; // dedup — one chip per capability.
            }

            list.Add(new SuggestedChip(targetBindingId!, label!.Trim(), GetString(item, "reason")));
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
}
