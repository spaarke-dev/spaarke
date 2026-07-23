namespace Sprk.Bff.Api.Services.Ai.Chat.Middleware;

/// <summary>
/// Per-agent, per-turn holder for the safety-perimeter degraded signal (audit finding F-8 follow-up).
///
/// The PRODUCER is <see cref="PromptShieldChatMiddleware"/>: at the start of every turn it resets this
/// holder, runs the PromptShield scan, and writes THIS turn's fail-open verdict
/// (<see cref="Safety.PromptShieldResult.FailedOpen"/>) here. The CONSUMER is
/// <see cref="SideEffectGateAIFunction"/> via its <c>safetyPerimeterDegradedProbe</c>
/// (<c>() =&gt; signal.Degraded</c>) — Policy v2 overlay 2, which degrades gated WRITES to
/// confirm-required when the perimeter fails open (Tier ≤ 1 reads/drafts stay fail-open per D-F0(b)).
///
/// <para>
/// <b>Why a shared mutable holder is correct (not a global static):</b> exactly ONE instance is
/// created per <see cref="SprkChatAgentFactory.CreateAgentAsync"/> call and shared between that agent's
/// gate wrap-sites and its shield middleware. Chat turns on a session are SEQUENTIAL, so the middleware
/// resetting-then-setting the value at the top of each <c>SendMessageAsync</c> — before the LLM tool
/// loop that invokes the gate — guarantees the gate reads the current turn's verdict. It is never a
/// process-global; distinct agents (and distinct sessions) get distinct holders.
/// </para>
/// </summary>
public sealed class SafetyPerimeterSignal
{
    private volatile bool _degraded;

    /// <summary>True when the current turn's PromptShield scan FAILED OPEN (timeout / 429 / 5xx / auth / parse).</summary>
    public bool Degraded => _degraded;

    /// <summary>Sets the current turn's degraded verdict (idempotent; called by the shield producer).</summary>
    public void Set(bool degraded) => _degraded = degraded;
}
