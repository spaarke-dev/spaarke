using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// R6 Pillar 7 (task 068, D-C-22 / FR-46) — production implementation of
/// <see cref="IPromptBudgetTracker"/>. In-flight accounting for the NFR-10 8K
/// system-prompt budget shared by the four chat prompt-assembly subsystems
/// (factory blocks, document context, knowledge retrieval, memory composition).
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime</b>: Scoped — one instance per chat turn (per HTTP request). Singleton
/// lifetime would leak budget across requests and is structurally wrong.
/// </para>
/// <para>
/// <b>ADR-010</b>: registered in the existing
/// <see cref="Sprk.Bff.Api.Infrastructure.DI.AnalysisServicesModule"/>. ZERO new
/// Program.cs lines. Interface seam justified by ADR-010's "interface required for
/// genuine substitution" carve-out — unit tests substitute the impl.
/// </para>
/// <para>
/// <b>ADR-013</b>: this service lives in <see cref="Memory"/>. Consumers are all
/// AI-internal (chat factory + context provider + memory composition + knowledge
/// inline content path) — no <c>PublicContracts</c> facade per the refined 2026-05-20
/// ADR-013 boundary rule.
/// </para>
/// <para>
/// <b>ADR-015 BINDING</b>: every log payload below is built from typed enumerated
/// fields ONLY (layer enum-string, integer token counts, sessionId/tenantId
/// deterministic IDs, decision enum-string). NEVER fragment bodies, user message
/// text, retrieved chunk text, or LLM response text. The <see cref="TryReserve"/>
/// signature is structurally constrained — it accepts no <c>object</c>,
/// <c>JsonElement</c>, or free-form content parameters.
/// </para>
/// <para>
/// <b>NFR-10 invariant</b>: <see cref="TotalBudget"/> defaults to 8K; the constructor
/// clamps to the documented [1024, 32_000] band matching
/// <see cref="MemoryCompositionOptions.TotalTokenBudget"/>.
/// </para>
/// <para>
/// <b>Thread-safety</b>: not required — Scoped per HTTP request, and the chat-turn
/// prompt-assembly path is single-threaded sequential. The internal counter is a
/// plain int; concurrent reservation within a single turn is not a supported scenario.
/// </para>
/// <para>
/// <b>Telemetry</b>: a Meter counter (<c>memory.prompt_budget_truncated</c>) tracks
/// truncation events; a structured log entry per event carries
/// <c>[ADR-015][memory.prompt_budget_truncated]</c> prefix and deterministic
/// identifiers only. Pattern matches the <c>IContextEventEmitter</c> implementation
/// established by task 063.
/// </para>
/// </remarks>
public sealed class PromptBudgetTracker : IPromptBudgetTracker, IDisposable
{
    /// <summary>Meter name (canonical) — exposed for test <c>MeterListener</c> subscription.</summary>
    public const string MeterName = "Sprk.Bff.Api.Ai.PromptBudget";

    /// <summary>Counter name for budget-truncated events.</summary>
    public const string TruncatedCounterName = "memory.prompt_budget_truncated";

    /// <summary>Counter name for granted reservations (useful for budget-utilisation telemetry).</summary>
    public const string GrantedCounterName = "memory.prompt_budget_granted";

    /// <summary>
    /// Decision enum values surfaced in telemetry. Used by both the meter tag and the
    /// structured-log enum field. ADR-015 BINDING: these are config-shape enum strings,
    /// never user content.
    /// </summary>
    public static class Decision
    {
        public const string Granted = "granted";
        public const string Truncated = "truncated";
        public const string NoOp = "noop";
    }

    private readonly int _totalBudget;
    private readonly Meter _meter;
    private readonly Counter<long> _truncatedCounter;
    private readonly Counter<long> _grantedCounter;
    private readonly ILogger<PromptBudgetTracker> _logger;
    private int _usedBudget;

    /// <summary>
    /// Production constructor — pulls the budget ceiling from
    /// <see cref="MemoryCompositionOptions.TotalTokenBudget"/> so the shared tracker
    /// uses the SAME 8K ceiling as the hierarchical composition tier. This is
    /// deliberate: the composition tier and the system-prompt budget share the same
    /// physical 8K ceiling per NFR-10.
    /// </summary>
    public PromptBudgetTracker(
        IOptions<MemoryCompositionOptions> compositionOptions,
        ILogger<PromptBudgetTracker> logger)
    {
        ArgumentNullException.ThrowIfNull(compositionOptions);
        ArgumentNullException.ThrowIfNull(logger);

        var rawBudget = compositionOptions.Value?.TotalTokenBudget ?? 8000;
        _totalBudget = Math.Clamp(rawBudget, 1024, 32_000);
        _logger = logger;
        _usedBudget = 0;

        _meter = new Meter(MeterName, "1.0.0");
        _truncatedCounter = _meter.CreateCounter<long>(
            TruncatedCounterName,
            unit: "{event}",
            description: "memory.prompt_budget_truncated — a prompt-assembly layer was denied its requested budget reservation (deterministic identifiers + decision only).");
        _grantedCounter = _meter.CreateCounter<long>(
            GrantedCounterName,
            unit: "{event}",
            description: "memory.prompt_budget_granted — a prompt-assembly layer's budget reservation was granted (deterministic identifiers + token counts only).");
    }

    /// <summary>
    /// Test-only constructor for budget customisation without binding the full
    /// <see cref="MemoryCompositionOptions"/> stack. Internal so unit tests in the
    /// same assembly access it; <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/>
    /// (already configured on the BFF for the Tests project) exposes it to the test
    /// assembly.
    /// </summary>
    internal PromptBudgetTracker(int totalBudget, ILogger<PromptBudgetTracker> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _totalBudget = Math.Clamp(totalBudget, 1024, 32_000);
        _logger = logger;
        _usedBudget = 0;

        _meter = new Meter(MeterName, "1.0.0");
        _truncatedCounter = _meter.CreateCounter<long>(TruncatedCounterName, unit: "{event}");
        _grantedCounter = _meter.CreateCounter<long>(GrantedCounterName, unit: "{event}");
    }

    /// <inheritdoc />
    public int TotalBudget => _totalBudget;

    /// <inheritdoc />
    public int UsedBudget => _usedBudget;

    /// <inheritdoc />
    public int Remaining => Math.Max(0, _totalBudget - _usedBudget);

    /// <inheritdoc />
    public bool TryReserve(string layer, int requestedTokens, Guid? sessionId, string? tenantId)
    {
        // Normalise layer — empty / whitespace becomes "unknown" so telemetry stays
        // well-formed under misconfigured callers. ADR-015 still holds (config-shape only).
        var safeLayer = string.IsNullOrWhiteSpace(layer) ? "unknown" : layer;

        // Non-positive request → no-op (caller has nothing to append). Telemetry-free path.
        if (requestedTokens <= 0)
        {
            return true;
        }

        // Headroom check. The first reservation that would exceed the ceiling is denied
        // entirely (all-or-nothing per layer). Subsystems wanting partial-fragment
        // behaviour estimate locally and use Remaining as a guide before composing.
        if (_usedBudget + requestedTokens > _totalBudget)
        {
            // Truncation telemetry. ADR-015: deterministic IDs + decision enum + counts only.
            var truncTags = new System.Diagnostics.TagList
            {
                { "layer", safeLayer },
                { "decision", Decision.Truncated },
                { "sessionId", sessionId?.ToString("N") ?? string.Empty },
                { "tenantId", tenantId ?? string.Empty },
            };
            _truncatedCounter.Add(1, truncTags);

            _logger.LogWarning(
                "[ADR-015][memory.prompt_budget_truncated] layer={Layer} decision={Decision} " +
                "requestedTokens={RequestedTokens} grantedTokens={GrantedTokens} " +
                "usedBudget={UsedBudget} remaining={Remaining} totalBudget={TotalBudget} " +
                "sessionId={SessionId} tenantId={TenantId} timestamp={Timestamp:o}",
                safeLayer, Decision.Truncated, requestedTokens, 0,
                _usedBudget, Remaining, _totalBudget,
                sessionId?.ToString("N"), tenantId, DateTimeOffset.UtcNow);

            return false;
        }

        // Grant. ADR-015: deterministic IDs + decision + counts only.
        _usedBudget += requestedTokens;

        var grantedTags = new System.Diagnostics.TagList
        {
            { "layer", safeLayer },
            { "decision", Decision.Granted },
            { "sessionId", sessionId?.ToString("N") ?? string.Empty },
            { "tenantId", tenantId ?? string.Empty },
        };
        _grantedCounter.Add(1, grantedTags);

        _logger.LogDebug(
            "[ADR-015][memory.prompt_budget_granted] layer={Layer} decision={Decision} " +
            "requestedTokens={RequestedTokens} grantedTokens={GrantedTokens} " +
            "usedBudget={UsedBudget} remaining={Remaining} totalBudget={TotalBudget} " +
            "sessionId={SessionId} tenantId={TenantId} timestamp={Timestamp:o}",
            safeLayer, Decision.Granted, requestedTokens, requestedTokens,
            _usedBudget, Remaining, _totalBudget,
            sessionId?.ToString("N"), tenantId, DateTimeOffset.UtcNow);

        return true;
    }

    public void Dispose()
    {
        _meter?.Dispose();
    }
}
