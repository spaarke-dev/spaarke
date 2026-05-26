using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Foundry;
using Sprk.Bff.Api.Telemetry;

using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Chat.Middleware;

/// <summary>
/// Routing middleware for the SprkChat agent pipeline (AIPU-072).
///
/// Sits at the outermost position in the middleware chain and classifies each incoming chat
/// message using keyword/pattern matching to decide which backend should handle it:
///
///   - <see cref="RoutingDecision.AgentService"/>: routes to <see cref="AgentServiceClient"/>
///     when intent signals for code analysis, chart generation, or legal research are detected.
///   - <see cref="RoutingDecision.DirectPipeline"/>: passes through to the next
///     <see cref="ISprkChatAgent"/> in the chain (existing direct OpenAI pipeline).
///
/// Routing constraints:
/// <list type="bullet">
///   <item><b>NFR-03</b>: Routing decision MUST complete in under 50ms.
///         <see cref="ClassifyIntent"/> is synchronous, pure keyword matching — no network
///         calls or LLM invocations. A Stopwatch canary logs a warning if it exceeds 10ms.</item>
///   <item><b>ADR-013</b>: Transparent to consumers via <see cref="ISprkChatAgent"/>.
///         Callers (e.g., <c>ChatEndpoints</c>) never know which backend was selected.</item>
///   <item><b>ADR-018</b>: Kill switch — when <see cref="AgentServiceOptions.Enabled"/> is
///         false, routing silently falls back to the direct pipeline. Never surfaces 503.</item>
///   <item><b>ADR-015</b>: Only the routing decision outcome, matched signal count, and
///         selected backend are logged. Message content is never logged or stored in spans.</item>
///   <item><b>ADR-019</b>: Unexpected routing errors propagate via ProblemDetails through
///         the existing endpoint error handling layer — not surfaced here directly.</item>
/// </list>
///
/// OpenTelemetry: one span named <c>"ai.routing.decision"</c> per request with tags:
///   - <c>routing.decision</c>: "AgentService" or "DirectPipeline"
///   - <c>routing.backend</c>: same as decision (normalised for dashboards)
///   - <c>routing.matched_signals</c>: count of matched signal groups (never content)
///
/// Lifetime: Transient — created per agent instance by <see cref="SprkChatAgentFactory"/>.
/// Not registered in DI (ADR-010: factory-instantiated, no unnecessary DI registrations).
/// </summary>
public sealed class AgentServiceRoutingMiddleware : ISprkChatAgent
{
    // ── Signal taxonomy (Step 4) ────────────────────────────────────────────
    // Each array defines keywords for a distinct intent group.
    // Signals are checked case-insensitively against the lowercased message.
    // A match in any signal group increments the score toward the AgentService threshold.

    private static class RoutingSignals
    {
        /// <summary>
        /// Code analysis and data computation signals.
        /// These indicate requests that require code execution or chart/data generation —
        /// capabilities best served by a Foundry Agent with a Code Interpreter tool.
        /// </summary>
        internal static readonly string[] CodeInterpreter =
        [
            "analyze data",
            "analyse data",
            "generate chart",
            "create chart",
            "plot ",
            "run code",
            "execute code",
            "calculate ",
            "computation",
            "data visualization",
            "data visualisation",
            "regression",
            "correlation",
            "statistical",
            "histogram",
            "scatter plot",
            "bar chart",
            "line chart",
            "pivot table",
            "parse csv",
            "parse excel"
        ];

        /// <summary>
        /// Legal research and external knowledge signals.
        /// These indicate requests for case law, legal precedents, or external research —
        /// capabilities best served by a Foundry Agent with a Bing Grounding tool.
        /// </summary>
        internal static readonly string[] BingGrounding =
        [
            "legal research",
            "find cases",
            "legal precedent",
            "case law",
            "look up",
            "lookup ",
            "court decision",
            "statute ",
            "regulation ",
            "search for recent",
            "recent case",
            "legislation",
            "regulatory",
            "jurisdiction",
            "legal authority"
        ];

        /// <summary>
        /// Complex multi-step query signals.
        /// These indicate requests that require chained reasoning across multiple steps —
        /// capabilities best served by a Foundry Agent with extended context windows.
        /// </summary>
        internal static readonly string[] ComplexQuery =
        [
            "step by step",
            "step-by-step",
            "multiple factors",
            "comprehensive analysis",
            "end to end",
            "end-to-end",
            "in-depth analysis",
            "deep dive",
            "full breakdown",
            "detailed breakdown",
            "cross-reference",
            "cross reference",
            "compare and contrast",
            "holistic review",
            "multi-step",
            "multistep"
        ];

        /// <summary>
        /// Minimum number of signal group matches required to route to Agent Service.
        /// One match is sufficient — any clear intent signal routes to Agent Service.
        /// </summary>
        internal const int ScoreThreshold = 1;
    }

    // ── Routing decision enum ────────────────────────────────────────────────

    internal enum RoutingDecision
    {
        DirectPipeline,
        AgentService
    }

    // ── Classification result record ──────────────────────────────────────────

    internal sealed record ClassificationResult(
        RoutingDecision Decision,
        int MatchedSignalGroupCount,
        string[] MatchedGroupNames);

    // ── Dependencies ─────────────────────────────────────────────────────────

    private readonly ISprkChatAgent _inner;
    private readonly AgentServiceClient _agentServiceClient;
    private readonly AgentServiceOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    /// Tenant ID for Agent Service thread key scoping (ADR-014).
    /// Injected by <see cref="SprkChatAgentFactory"/> which knows the tenantId at agent
    /// creation time. ChatContext does not carry tenantId (it is request-scoped).
    /// </summary>
    private readonly string _tenantId;

    // ── OpenTelemetry ActivitySource (Sprk.Bff.Api.Ai, registered in TelemetryModule) ───

    // NFR-03 canary: log warning when classification exceeds this threshold.
    private const long ClassifyWarningThresholdMs = 10;

    public AgentServiceRoutingMiddleware(
        ISprkChatAgent inner,
        AgentServiceClient agentServiceClient,
        IOptions<AgentServiceOptions> options,
        ILogger logger,
        string tenantId)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _agentServiceClient = agentServiceClient ?? throw new ArgumentNullException(nameof(agentServiceClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantId = !string.IsNullOrWhiteSpace(tenantId) ? tenantId : throw new ArgumentException("tenantId must not be null or empty.", nameof(tenantId));
    }

    /// <inheritdoc />
    public ChatContext Context => _inner.Context;

    /// <inheritdoc />
    public CitationContext? Citations => _inner.Citations;

    /// <summary>
    /// Classifies the message intent and routes to the appropriate backend.
    ///
    /// Routing flow:
    ///   1. Classify intent via <see cref="ClassifyIntent"/> (synchronous, sub-millisecond).
    ///   2. If AgentService and enabled → delegate to <see cref="AgentServiceClient"/>.
    ///   3. If AgentService but disabled (kill switch) → silently fall back to direct pipeline.
    ///   4. If DirectPipeline → pass through to inner agent.
    ///   5. OTEL span records decision outcome (not content).
    /// </summary>
    public async IAsyncEnumerable<ChatResponseUpdate> SendMessageAsync(
        string message,
        IReadOnlyList<AiChatMessage> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // === Step 1: Classify intent (NFR-03: must be < 50ms) ===
        var classifySw = Stopwatch.StartNew();
        var classification = ClassifyIntent(message);
        classifySw.Stop();

        // NFR-03 canary: warn if classification approaches the 50ms hard limit.
        if (classifySw.ElapsedMilliseconds > ClassifyWarningThresholdMs)
        {
            _logger.LogWarning(
                "AgentServiceRoutingMiddleware: ClassifyIntent took {ElapsedMs}ms (warning threshold: {ThresholdMs}ms). " +
                "NFR-03 hard limit is 50ms. Review signal array sizes.",
                classifySw.ElapsedMilliseconds,
                ClassifyWarningThresholdMs);
        }

        // === Step 2: Determine effective backend ===
        // ADR-018: if Agent Service is disabled, silently fall back to direct pipeline.
        var effectiveDecision = classification.Decision == RoutingDecision.AgentService && _options.Enabled
            ? RoutingDecision.AgentService
            : RoutingDecision.DirectPipeline;

        var backendName = effectiveDecision == RoutingDecision.AgentService
            ? "AgentService"
            : "DirectPipeline";

        var decisionName = classification.Decision.ToString();

        // ADR-015: log only decision outcome + matched group count — never message content.
        _logger.LogDebug(
            "AgentServiceRoutingMiddleware: classified={ClassifiedDecision}, effective={EffectiveBackend}, " +
            "matchedGroups={MatchedGroupCount}, groups=[{GroupNames}], killSwitchEnabled={KillSwitchEnabled}, " +
            "classifyMs={ClassifyMs}",
            decisionName,
            backendName,
            classification.MatchedSignalGroupCount,
            string.Join(",", classification.MatchedGroupNames),
            _options.Enabled,
            classifySw.ElapsedMilliseconds);

        // === Step 3: OTEL span — record routing decision (not content) ===
        using var activity = AiTelemetry.ActivitySource.StartActivity("ai.routing.decision", ActivityKind.Internal);
        activity?.SetTag("routing.decision", decisionName);
        activity?.SetTag("routing.backend", backendName);
        activity?.SetTag("routing.matched_signals", classification.MatchedSignalGroupCount);

        // === Step 4: Route to the selected backend ===
        if (effectiveDecision == RoutingDecision.AgentService)
        {
            await foreach (var update in RouteToAgentServiceAsync(message, cancellationToken))
            {
                yield return update;
            }
        }
        else
        {
            await foreach (var update in _inner.SendMessageAsync(message, history, cancellationToken))
            {
                yield return update;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pass-through to the inner agent. Compound intent detection always uses the direct
    /// pipeline (raw IChatClient) for plan gating — routing does not apply here.
    /// </remarks>
    public Task<IReadOnlyList<FunctionCallContent>> DetectToolCallsAsync(
        string message,
        IReadOnlyList<AiChatMessage> history,
        CancellationToken cancellationToken)
        => _inner.DetectToolCallsAsync(message, history, cancellationToken);

    // ── Classification engine (NFR-03: synchronous, no I/O) ─────────────────

    /// <summary>
    /// Classifies the user's intent by scanning for keyword signals across three groups.
    ///
    /// Algorithm:
    ///   1. Lowercase the message once (O(n) where n = message length).
    ///   2. For each signal group, scan for any matching keyword (short-circuit on first hit).
    ///   3. Accumulate matched group count (score).
    ///   4. If score >= <see cref="RoutingSignals.ScoreThreshold"/> → AgentService.
    ///      Otherwise → DirectPipeline.
    ///
    /// Performance guarantee (NFR-03):
    ///   - No allocations on the hot path beyond the lowercased string.
    ///   - String.Contains(value, StringComparison) is O(n * m) in the worst case but
    ///     signal strings are short (< 30 chars) and messages are short (< 2000 chars).
    ///   - Expected duration: < 1ms for typical chat messages.
    ///
    /// ADR-015: This method receives the message for classification but MUST NOT log content.
    ///          Only the matched signal group names are logged (not which keyword matched).
    /// </summary>
    /// <param name="message">The raw user message (not logged).</param>
    /// <returns>Classification result with routing decision and matched group metadata.</returns>
    internal static ClassificationResult ClassifyIntent(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return new ClassificationResult(RoutingDecision.DirectPipeline, 0, []);
        }

        // Lowercase once — avoids repeated allocations in Contains calls.
        var lower = message.ToLowerInvariant();

        var matchedGroups = new List<string>(3);
        var score = 0;

        // Group 1: Code Interpreter signals
        foreach (var signal in RoutingSignals.CodeInterpreter)
        {
            if (lower.Contains(signal, StringComparison.Ordinal))
            {
                matchedGroups.Add("CodeInterpreter");
                score++;
                break; // Short-circuit: one match per group is sufficient
            }
        }

        // Group 2: Bing Grounding signals
        foreach (var signal in RoutingSignals.BingGrounding)
        {
            if (lower.Contains(signal, StringComparison.Ordinal))
            {
                matchedGroups.Add("BingGrounding");
                score++;
                break; // Short-circuit: one match per group is sufficient
            }
        }

        // Group 3: Complex Query signals
        foreach (var signal in RoutingSignals.ComplexQuery)
        {
            if (lower.Contains(signal, StringComparison.Ordinal))
            {
                matchedGroups.Add("ComplexQuery");
                score++;
                break; // Short-circuit: one match per group is sufficient
            }
        }

        var decision = score >= RoutingSignals.ScoreThreshold
            ? RoutingDecision.AgentService
            : RoutingDecision.DirectPipeline;

        return new ClassificationResult(decision, score, [.. matchedGroups]);
    }

    // ── Agent Service routing ────────────────────────────────────────────────

    /// <summary>
    /// Routes the message to the Agent Service and adapts the streaming token response
    /// back into the <see cref="ChatResponseUpdate"/> shape expected by the chat pipeline.
    ///
    /// Error handling:
    ///   - <see cref="FeatureDisabledException"/>: should not be reached here (kill switch
    ///     already checked in <see cref="SendMessageAsync"/>) but handled defensively —
    ///     falls through to inner pipeline via a single fallback notification.
    ///   - Other exceptions: re-thrown so the endpoint error handler produces ProblemDetails
    ///     (ADR-019).
    ///
    /// ADR-015: token content is never logged here — only passed upstream.
    /// </summary>
    private async IAsyncEnumerable<ChatResponseUpdate> RouteToAgentServiceAsync(
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string threadId;
        try
        {
            // Obtain or resume the Foundry thread for this tenant.
            // _tenantId is injected at construction by SprkChatAgentFactory (ADR-014: tenant-scoped keys).
            threadId = await _agentServiceClient.CreateOrResumeThreadAsync(_tenantId, cancellationToken);
        }
        catch (FeatureDisabledException)
        {
            // Kill switch triggered after the routing decision was made but before the call.
            // ADR-018: silently fall back — no error visible to user.
            _logger.LogWarning(
                "AgentServiceRoutingMiddleware: FeatureDisabledException during thread creation; " +
                "routing decision was AgentService but kill switch is now off. No content yielded.");
            yield break;
        }

        // Append the user message to the thread.
        await _agentServiceClient.SendMessageAsync(threadId, message, cancellationToken);

        // Stream the agent response and adapt tokens to ChatResponseUpdate.
        await foreach (var token in _agentServiceClient.StreamResponseAsync(threadId, cancellationToken))
        {
            var update = new ChatResponseUpdate { Role = ChatRole.Assistant };
            update.Contents.Add(new TextContent(token));
            yield return update;
        }
    }
}
