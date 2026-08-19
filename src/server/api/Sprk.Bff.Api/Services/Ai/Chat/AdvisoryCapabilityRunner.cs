using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sprk.Bff.Api.Models.Ai.Chat;

using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// FR-01/FR-02 (spaarkeai-assistant-enhancements-r4 task 012): the executor for an ADVISORY
/// grounded-recommend capability — the E1 "grounded-recommend tier" runner that turns the ack-only
/// <c>list-tasks</c> Action into a grounded, cited task-agenda summary + prioritized recommendation
/// (the P1 UAT defect fix).
/// </summary>
/// <remarks>
/// <para>
/// <b>Where it sits (ADR-039 one-decider, ADR-040 store-before-render)</b>: an advisory Action is
/// selected DETERMINISTICALLY by binding id at the dispatch seam
/// (<see cref="SessionDispatchOrchestrator"/>), exactly like the Click path — the ONE probabilistic
/// dispatch decider stays the top-level Text-path agent turn. When the resolved
/// <see cref="AnalysisAction.GroundedToolAllowList"/> is non-empty, the orchestrator routes THIS runner
/// INSTEAD of the single-completion <see cref="LinearConsumers.IActionRunner"/>. The runner builds a
/// NESTED bounded agent turn (<see cref="SprkChatAgentFactory.CreateAgentAsync"/> advisory overload)
/// that mounts ONLY the Action's allow-listed grounded READ tools (task-011
/// <see cref="AgentToolProjection.PreFilter"/> narrowing drops every <see cref="BindingCapabilityTool"/>
/// + <see cref="RefusalCapabilityTool"/>, so the nested turn structurally CANNOT dispatch a second
/// capability), runs it over the caller's OBO identity (the grounded handlers resolve OBO ambiently
/// from <c>IHttpContextAccessor</c> — this runner executes inline within the top-level chat request's
/// async context), drains the streamed narration, and assembles it into ONE <see cref="JsonElement"/>.
/// </para>
/// <para>
/// <b>Why the drained narration is assembled, never streamed</b>: ADR-040 forbids rendering advisory
/// output before it is stored (<c>ProgressiveRenderGuard.EnsureStored</c> at the dispatch tail). So the
/// nested turn's tokens are accumulated here and returned as the dispatch <c>output</c>
/// <see cref="JsonElement"/> — the orchestrator's <see cref="IOutputRouter"/> stores it FIRST, then the
/// terminal chunk renders FROM the stored entry. The stored payload is
/// <c>{ "acknowledgement": &lt;narration&gt; }</c>: it preserves the shipped <c>list-tasks</c> wire
/// contract (the client already renders <c>acknowledgement</c>), so the P1 fix — a rich cited summary
/// instead of a thin one-line ack — needs no client change. The Action's outputSchema is NOT
/// constrained-decode-enforced on this path (the nested function-calling turn produces free narration);
/// it documents the stored payload shape.
/// </para>
/// <para>
/// <b>Grounding (ADR-039 advisory mode)</b>: the nested turn runs the Action's advisory system prompt
/// (the ADVISORY GROUNDING RULES — call the grounded tools, cite every count/name/date, never
/// fabricate, never ask the user's identity). The nested <see cref="SprkChatAgent"/> also runs the
/// per-turn citation enforcer, so a turn that consumed read-tool results but rendered no <c>[N]</c>
/// marker is repaired with a deterministic sources block before it is drained here.
/// </para>
/// <para>
/// <b>ADR-010</b>: concrete class, no runner-authored interface beyond the one seam the orchestrator
/// consumes (<see cref="IAdvisoryCapabilityRunner"/>) — needed so the orchestrator can take an OPTIONAL
/// dependency (null in hand-built test constructions → the orchestrator falls back to the linear
/// ActionRunner; ADR-032 Null-Object-friendly guard). Registered unconditionally in the compound-ON
/// block alongside the real <see cref="SprkChatAgentFactory"/> it depends on.
/// </para>
/// </remarks>
public interface IAdvisoryCapabilityRunner
{
    /// <summary>
    /// Run the advisory nested bounded agent turn for the resolved advisory <paramref name="action"/>
    /// and return the drained, cited narration assembled into the stored-payload
    /// <see cref="JsonElement"/> (<c>{ "acknowledgement": &lt;narration&gt; }</c>).
    /// </summary>
    Task<JsonElement> RunAsync(
        AnalysisAction action,
        ChatSession session,
        SessionDispatchRequest request,
        CancellationToken cancellationToken);
}

/// <inheritdoc cref="IAdvisoryCapabilityRunner" />
public sealed class AdvisoryCapabilityRunner : IAdvisoryCapabilityRunner
{
    /// <summary>The stored-payload field the shipped <c>list-tasks</c> wire contract renders.</summary>
    internal const string AcknowledgementField = "acknowledgement";

    /// <summary>
    /// The nested turn's user message when the dispatch args carry no structured operand text. The
    /// Action's advisory system prompt is authoritative (it fully instructs the tool calls + narration),
    /// so this is only a non-empty trigger for <see cref="SprkChatAgent.SendMessageAsync"/> (which
    /// rejects a blank message).
    /// </summary>
    internal const string DefaultAdvisoryMessage =
        "Summarize my open assigned tasks and tell me what to prioritize.";

    private readonly SprkChatAgentFactory _agentFactory;
    private readonly ILogger<AdvisoryCapabilityRunner> _logger;

    public AdvisoryCapabilityRunner(
        SprkChatAgentFactory agentFactory,
        ILogger<AdvisoryCapabilityRunner> logger)
    {
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<JsonElement> RunAsync(
        AnalysisAction action,
        ChatSession session,
        SessionDispatchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var allowList = action.GroundedToolAllowList;
        if (allowList is null || allowList.Count == 0)
        {
            // Defensive: the orchestrator only routes here when the allow-list is non-empty. A caller
            // that reaches this with an empty list is a wiring error — fail loudly rather than build a
            // nested turn with zero tools (which the fail-closed PreFilter would mount as no tools).
            throw new InvalidOperationException(
                "AdvisoryCapabilityRunner requires a non-empty GroundedToolAllowList (the advisory " +
                "routing signal). The orchestrator must not route a fact-tier Action here.");
        }

        // Build the NESTED bounded agent turn. The advisory overload threads the allow-list into the
        // AgentToolFilterContext (task-011 PreFilter keeps ONLY these grounded tools, drops every
        // capability/refusal tool) and REPLACES the system prompt with the Action's advisory prompt.
        // We deliberately pass NO sseWriter (no capability_change leak to the client), NO ledger /
        // uploaded-files / live-tabs / active-item (the nested turn is a one-shot grounded summary; the
        // prompt override discards workspace-state enrichment anyway), and force the Action's ModelTier
        // (Reasoning) via modelTierOverride. documentId / playbookId mirror the session (the same
        // catalog the top-level turn projected these grounded tools from — a null-playbook empty-document
        // assistant turn is the shipped no-doc path). OBO flows ambiently (IHttpContextAccessor) — no
        // token threading needed.
        var agent = await _agentFactory
            .CreateAgentAsync(
                sessionId: request.SessionId,
                documentId: session.DocumentId ?? string.Empty,
                playbookId: session.PlaybookId,
                tenantId: request.TenantId,
                hostContext: session.HostContext,
                modelTierOverride: action.ModelTier,
                advisoryToolAllowList: allowList,
                advisorySystemPrompt: action.SystemPrompt,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var userMessage = ExtractOperandText(request.Args) ?? DefaultAdvisoryMessage;

        // Drain the streamed narration (mirror SprkChatAgent's own accumulate loop): the
        // UseFunctionInvocation client auto-executes the two grounded tool calls; update.Text carries the
        // assistant's narrated summary + recommendation (+ any citation-enforcer repair suffix).
        var narration = new StringBuilder();
        await foreach (var update in agent
            .SendMessageAsync(userMessage, Array.Empty<AiChatMessage>(), cancellationToken)
            .ConfigureAwait(false))
        {
            if (update.Text is { Length: > 0 } text)
            {
                narration.Append(text);
            }
        }

        var answer = narration.ToString().Trim();
        if (answer.Length == 0)
        {
            // Honest fallback (ADR-039): the tools failed or returned nothing usable. The Tasks tab still
            // opens (surface_launch), so tell the user plainly rather than fabricate.
            answer =
                "I couldn't retrieve your tasks right now — your live task list is in the tab that just opened.";
        }

        _logger.LogInformation(
            "[advisory-capability][FR-01] nested advisory turn drained — tenant={TenantId} session={SessionId} " +
            "allowListCount={AllowListCount} narrationLen={NarrationLen}",
            request.TenantId, request.SessionId, allowList.Count, answer.Length);

        return BuildAcknowledgementPayload(answer);
    }

    /// <summary>
    /// Assemble the stored-payload <see cref="JsonElement"/> the <see cref="IOutputRouter"/> stores and
    /// the terminal chunk renders (<c>{ "acknowledgement": &lt;narration&gt; }</c>). Built from a
    /// <see cref="JsonObject"/> so the field name is literal (no naming-policy dependency); the returned
    /// element is detached (its own backing document) — the router clones it regardless.
    /// </summary>
    internal static JsonElement BuildAcknowledgementPayload(string acknowledgement)
    {
        var node = new JsonObject { [AcknowledgementField] = acknowledgement };
        return JsonSerializer.SerializeToElement(node);
    }

    /// <summary>
    /// Read the dispatch args' structured operand text (the <c>documentText</c> the advisory Action
    /// declares) so the nested turn's user message echoes the caller's request. Returns null when absent
    /// or blank → the caller uses <see cref="DefaultAdvisoryMessage"/>.
    /// </summary>
    internal static string? ExtractOperandText(JsonElement? args)
    {
        if (args is not { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }

        if (element.TryGetProperty("documentText", out var operand) &&
            operand.ValueKind == JsonValueKind.String)
        {
            var text = operand.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        return null;
    }
}
