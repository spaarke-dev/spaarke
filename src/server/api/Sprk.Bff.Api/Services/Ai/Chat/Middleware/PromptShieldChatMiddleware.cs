using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Safety;

using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Chat.Middleware;

/// <summary>
/// Pre-LLM prompt-injection perimeter for the LIVE interactive chat pipeline (audit finding F-8
/// follow-up). Wraps an inner <see cref="ISprkChatAgent"/> and, on every turn BEFORE the inner
/// agent's LLM tool loop runs:
/// <list type="number">
///   <item>Resets the shared per-turn <see cref="SafetyPerimeterSignal"/>.</item>
///   <item>Runs <see cref="IPromptShieldService.ScanAsync"/> over the user message (+ any turn-scoped
///   citation excerpts). The service enforces its own 100 ms hard deadline and fails open internally
///   — this middleware adds NO new timeout.</item>
///   <item>If an injection is DETECTED → HARD BLOCK: yields one coherent assistant message through the
///   normal token channel and <c>yield break</c>s, so the inner agent (LLM + tool loop) never runs.</item>
///   <item>Otherwise records THIS turn's <see cref="PromptShieldResult.FailedOpen"/> verdict on the
///   shared signal so the confirmation gate's overlay-2 probe sees a degraded perimeter and gates
///   WRITES to confirm-required (reads/drafts stay fail-open per D-F0(b)).</item>
/// </list>
///
/// <para>
/// <b>§11 Component Justification.</b>
/// (1) <i>Existing</i> — <see cref="SafetyPipelineMiddleware"/> already contains this pre-LLM scan, but
/// it is a two-phase perimeter that ALSO runs post-LLM groundedness + citation verification + confidence
/// scoring + compliance audit on every turn. F-8 needs ONLY the pre-LLM injection block + the fail-open
/// signal. (2) <i>Extension</i> — that class is sealed and hardcodes the post-LLM phase; adding a
/// "pre-LLM-only" mode would entangle two orthogonal contracts, and activating its post-LLM machinery on
/// the hot path is a broader security-posture change escalated separately (044-signal-flow-trace §8.3).
/// (3) <i>Cost of doing nothing</i> — wiring the full two-phase middleware would emit a new
/// <c>safety_annotation</c> SSE + an audit write on EVERY healthy turn, violating the F-8 "behavior
/// unchanged on a healthy scan" contract and risking the golden-utterance eval gate. This focused
/// component is the minimal surface that closes F-8 while keeping healthy turns byte-identical.
/// </para>
///
/// <para>
/// <b>Captive-dependency discipline.</b> <see cref="IPromptShieldService"/> is registered SCOPED, but the
/// agent-creation scope is disposed long before the LLM streams. This middleware therefore captures only
/// the ROOT provider (a singleton) and resolves the scoped shield from a FRESH scope INSIDE each turn's
/// <see cref="SendMessageAsync"/> — never a captured scoped instance. Same discipline as
/// <see cref="SideEffectGateAIFunction"/> / <c>BindingCapabilityTool</c>. A single agent instance can
/// therefore serve many sequential turns with no <see cref="ObjectDisposedException"/>.
/// </para>
///
/// <para>
/// <b>Wiring.</b> Added OUTERMOST by <see cref="SprkChatAgentFactory.WrapWithMiddleware"/> ONLY when
/// <see cref="IsChatPipelineEnabled"/> is true (config <c>AiSafety:PromptShield:ChatPipelineEnabled</c>,
/// default false). When disabled the middleware is not constructed at all and the gate probe stays null
/// — byte-identical to pre-F-8 behavior.
/// </para>
///
/// <para><b>ADR-015</b>: logs identifiers + outcome flags only — never the user message or document text.</para>
/// </summary>
public sealed class PromptShieldChatMiddleware : ISprkChatAgent
{
    /// <summary>Config key that opts the shield onto the interactive chat pipeline. Default false = feature off.</summary>
    public const string ChatPipelineEnabledConfigKey = "AiSafety:PromptShield:ChatPipelineEnabled";

    /// <summary>
    /// User-facing message emitted when an injection is detected. ADR-015: must not disclose which
    /// document / pattern triggered the block. Mirrors <see cref="SafetyPipelineMiddleware"/>'s copy.
    /// </summary>
    internal const string InjectionBlockedMessage =
        "Your message could not be processed due to a security policy violation. " +
        "Please rephrase and try again.";

    private readonly ISprkChatAgent _inner;
    private readonly IServiceProvider _rootServices;
    private readonly SafetyPerimeterSignal _signal;
    private readonly string _sessionId;
    private readonly ILogger _logger;

    /// <param name="inner">The wrapped agent (rest of the middleware pipeline + core agent).</param>
    /// <param name="rootServices">ROOT provider — a fresh scope is created per turn (never a captured Scoped dep).</param>
    /// <param name="signal">Shared per-turn holder written here and read by the gate's overlay-2 probe.</param>
    /// <param name="sessionId">Opaque session id (logging correlation only).</param>
    /// <param name="logger">Logger (ADR-015: identifiers + outcome only, never content).</param>
    public PromptShieldChatMiddleware(
        ISprkChatAgent inner,
        IServiceProvider rootServices,
        SafetyPerimeterSignal signal,
        string sessionId,
        ILogger logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _rootServices = rootServices ?? throw new ArgumentNullException(nameof(rootServices));
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Whether the shield is wired onto the interactive chat pipeline. True only when
    /// <see cref="ChatPipelineEnabledConfigKey"/> parses to <c>true</c>. Absent / unparseable / "false"
    /// ⇒ false (feature off = byte-identical to pre-F-8: no scan, no block, gate probe unfed). DEGRADED
    /// (configured-but-failing) is a RUNTIME outcome of an enabled shield, NOT this switch.
    /// </summary>
    public static bool IsChatPipelineEnabled(IConfiguration? configuration)
    {
        var raw = configuration?[ChatPipelineEnabledConfigKey];
        return bool.TryParse(raw, out var enabled) && enabled;
    }

    /// <inheritdoc />
    public ChatContext Context => _inner.Context;

    /// <inheritdoc />
    public CitationContext? Citations => _inner.Citations;

    /// <inheritdoc />
    public AgentTurnContract? TurnContract => _inner.TurnContract;

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> SendMessageAsync(
        string message,
        IReadOnlyList<AiChatMessage> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Per-turn reset FIRST so the gate always reads THIS turn's verdict, never a stale one from a
        // prior turn on the same (reused) agent instance.
        _signal.Set(false);

        // Scan happens in a helper (no yield in its try/catch). ScanAsync fails open internally, so a
        // null result here means "no scan available" (service missing) — treated as not-blocked, not-degraded.
        var shieldResult = await ScanAsync(message, cancellationToken).ConfigureAwait(false);

        if (shieldResult is { IsBlocked: true })
        {
            _logger.LogWarning(
                "[F-8][prompt-shield] injection BLOCKED (hard block) — session={SessionId} reason={Reason} attackType={AttackType} latencyMs={LatencyMs:F1}",
                _sessionId, shieldResult.BlockReason, shieldResult.DetectedAttackType, shieldResult.LatencyMs);

            // Coherent user-facing response via the NORMAL token channel: the endpoint's stream loop
            // renders update.Text as a `token` SSE event, so the user sees an ordinary assistant message
            // — never a protocol-level error frame or a silent hang. The inner LLM/tool loop is skipped.
            var blockUpdate = new ChatResponseUpdate { Role = ChatRole.Assistant };
            blockUpdate.Contents.Add(new TextContent(InjectionBlockedMessage));
            yield return blockUpdate;
            yield break;
        }

        var degraded = shieldResult?.FailedOpen ?? false;
        _signal.Set(degraded);

        if (degraded)
        {
            _logger.LogWarning(
                "[F-8][prompt-shield] perimeter DEGRADED (fail-open) — session={SessionId}; gated WRITES will require confirmation this turn (overlay 2).",
                _sessionId);
        }
        else if (shieldResult is not null)
        {
            _logger.LogInformation(
                "[F-8][prompt-shield] scan PASSED — session={SessionId} latencyMs={LatencyMs:F1}",
                _sessionId, shieldResult.LatencyMs);
        }

        // Pass every token through UNCHANGED (the middleware never mutates the inner stream).
        await foreach (var update in _inner.SendMessageAsync(message, history, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <summary>
    /// Runs the shield in a FRESH per-turn DI scope (captive-dependency-safe). Returns the scan result,
    /// or <c>null</c> when no <see cref="IPromptShieldService"/> is registered (treated as no-scan /
    /// not-degraded). Any scope/resolution fault fails OPEN (returns a FailedOpen result) so a shield
    /// infrastructure error can never silently drop the turn.
    /// </summary>
    private async Task<PromptShieldResult?> ScanAsync(string message, CancellationToken ct)
    {
        try
        {
            // Fresh scope, disposed as soon as the scan completes — the shield is never held across the
            // (long) inner streaming phase, and the agent-creation scope is not touched.
            using var scope = _rootServices.CreateScope();
            var shield = scope.ServiceProvider.GetService<IPromptShieldService>();
            if (shield is null)
            {
                _logger.LogWarning(
                    "[F-8][prompt-shield] IPromptShieldService not registered — scan skipped (fail-open), session={SessionId}",
                    _sessionId);
                return null;
            }

            var docs = ExtractSourceDocuments();
            var request = new PromptShieldRequest(message, docs.Count > 0 ? docs : null);
            return await shield.ScanAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // ScanAsync already fails open on API errors; this guards scope/resolution faults.
            _logger.LogWarning(ex,
                "[F-8][prompt-shield] scan threw unexpectedly — failing OPEN (perimeter degraded), session={SessionId}",
                _sessionId);
            return PromptShieldResult.FailOpen(0);
        }
    }

    /// <summary>
    /// Turn-scoped document passages available on the inner agent's <see cref="CitationContext"/>.
    /// On the loop-based agent these are populated by search handlers DURING the turn, so at pre-LLM time
    /// the list is typically empty and the scan covers the USER MESSAGE (the primary F-8 concern —
    /// direct injection in the user turn). ADR-015: excerpts go to the Content Safety API, never to logs.
    /// </summary>
    private IReadOnlyList<string> ExtractSourceDocuments()
    {
        var citations = _inner.Citations;
        if (citations is null)
        {
            return [];
        }

        return citations.GetCitations()
            .Select(c => c.Excerpt)
            .Where(excerpt => !string.IsNullOrWhiteSpace(excerpt))
            .Select(excerpt => excerpt!)
            .ToList()
            .AsReadOnly();
    }
}
