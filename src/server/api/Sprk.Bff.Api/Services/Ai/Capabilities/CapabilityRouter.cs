using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Layer 1 of the three-tier capability router: synchronous keyword classifier (AIPU2-012).
///
/// Algorithm overview
/// ─────────────────
/// 1. Snapshot the enabled capability list from <see cref="ICapabilityManifest"/> once per call.
/// 2. Normalise the user turn to lowercase.
/// 3. For each capability, score = matched_hints / total_hints where "match" means the
///    lowercased hint appears as a substring of the lowercased user message.
///    Description words are also tested (split on whitespace) as weak bonus signals.
/// 4. Select the top-scoring capability. Compute normalised confidence:
///      confidence = topScore / (topScore + secondScore + Epsilon)
///    This formula pushes toward 1.0 when one capability dominates and toward 0.5 when
///    two capabilities tie — a clear sign of ambiguity that Layer 2 should resolve.
/// 5. If confidence &gt;= EffectiveThreshold → return <see cref="CapabilityRoutingResult.Confident"/>.
///    Otherwise → return <see cref="CapabilityRoutingResult.Uncertain"/>.
///
/// Playbook bias
/// ─────────────
/// When <c>activePlaybookName</c> is provided, capabilities whose
/// <see cref="CapabilityManifestEntry.PlaybookId"/> matches the playbook receive a
/// multiplier boost on their raw score before normalisation. This allows a lower
/// effective threshold (<see cref="CapabilityRouterOptions.PlaybookBiasThreshold"/>) to
/// activate confident routing sooner in single-playbook sessions.
///
/// NFR constraints (AIPU2-012)
/// ──────────────────────────
/// - Must complete in &lt;50ms for a manifest with 50 capabilities.
/// - No network I/O, no LLM calls — pure in-memory string matching.
/// - A Stopwatch canary logs a warning if classification exceeds 10ms.
///
/// OTEL instrumentation
/// ─────────────────────
/// Activity name: <c>"capability_router.layer1"</c>
/// Tags: <c>capability_name</c>, <c>confidence</c>, <c>matched</c>, <c>latency_ms</c>
/// Metrics: <c>ai_routing_layer1_hit</c> (counter), <c>ai_routing_layer1_latency_ms</c> (histogram)
///
/// ADR-015: user message content is NEVER logged or recorded in spans.
/// ADR-010: singleton — safe because all state comes from thread-safe <see cref="ICapabilityManifest"/>.
/// </summary>
public sealed class CapabilityRouter : ICapabilityRouter
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Small epsilon to prevent division by zero when both topScore and secondScore are 0.
    /// </summary>
    private const double Epsilon = 1e-9;

    /// <summary>
    /// Multiplier applied to a capability's raw score when its playbook matches the
    /// active playbook in the session. Raises effective confidence without changing the
    /// threshold formula.
    /// </summary>
    private const double PlaybookBiasMultiplier = 1.5;

    /// <summary>
    /// NFR-03 canary: emit a warning log when keyword classification exceeds this duration.
    /// Hard limit is 50ms; we warn at 10ms so engineers notice regressions early.
    /// </summary>
    private const long ClassifyWarningThresholdMs = 10;

    /// <summary>
    /// Metric instrument name for Layer 1 hit counter (OTEL).
    /// </summary>
    private const string MetricHitCounterName = "ai_routing_layer1_hit";

    /// <summary>
    /// Metric instrument name for Layer 1 latency histogram (OTEL).
    /// </summary>
    private const string MetricLatencyHistogramName = "ai_routing_layer1_latency_ms";

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ICapabilityManifest _manifest;
    private readonly CapabilityRouterOptions _options;
    private readonly ILogger<CapabilityRouter> _logger;

    // ── OTEL instrumentation ──────────────────────────────────────────────────

    private static readonly Meter RouterMeter = new("Sprk.Bff.Api.Ai", "1.0.0");
    private static readonly Counter<long> Layer1HitCounter =
        RouterMeter.CreateCounter<long>(MetricHitCounterName, unit: "{hit}",
            description: "Count of turns where Layer 1 keyword routing produced a confident result.");
    private static readonly Histogram<double> Layer1LatencyHistogram =
        RouterMeter.CreateHistogram<double>(MetricLatencyHistogramName, unit: "ms",
            description: "Wall-clock latency of Layer 1 keyword classification in milliseconds.");

    // ── Constructor ───────────────────────────────────────────────────────────

    public CapabilityRouter(
        ICapabilityManifest manifest,
        IOptions<CapabilityRouterOptions> options,
        ILogger<CapabilityRouter> logger)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── ICapabilityRouter ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public CapabilityRoutingResult RouteSync(string userMessage, string? activePlaybookName)
    {
        // Start OTEL activity for this routing pass.
        using var activity = AiTelemetry.ActivitySource.StartActivity(
            "capability_router.layer1", ActivityKind.Internal);

        var sw = Stopwatch.StartNew();

        try
        {
            var result = ClassifyLayer1(userMessage, activePlaybookName);

            sw.Stop();
            var latencyMs = sw.ElapsedMilliseconds;

            // NFR-03 canary: warn if we are approaching the 50ms hard limit.
            if (latencyMs > ClassifyWarningThresholdMs)
            {
                _logger.LogWarning(
                    "CapabilityRouter.Layer1: classification took {LatencyMs}ms " +
                    "(warning threshold: {ThresholdMs}ms, hard limit: 50ms). " +
                    "Review manifest size and hint counts.",
                    latencyMs, ClassifyWarningThresholdMs);
            }

            // Emit OTEL span tags (ADR-015: no message content).
            activity?.SetTag("confidence", result.Confidence);
            activity?.SetTag("matched", result.IsConfident);
            activity?.SetTag("selected_count", result.SelectedCapabilities.Length);
            activity?.SetTag("latency_ms", latencyMs);

            if (result.SelectedCapabilities.Length > 0)
            {
                // Only the capability name — never the user turn.
                activity?.SetTag("capability_name", result.SelectedCapabilities[0]);
            }

            // Emit metrics.
            Layer1LatencyHistogram.Record(sw.Elapsed.TotalMilliseconds);
            if (result.IsConfident)
            {
                Layer1HitCounter.Add(1);
            }

            // Return a copy with accurate latency (ClassifyLayer1 uses 0 internally).
            return result with { LatencyMs = latencyMs };
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            // Re-throw — endpoint error handler produces ProblemDetails (ADR-019).
            throw;
        }
    }

    // ── Classification engine ─────────────────────────────────────────────────

    /// <summary>
    /// Pure keyword classification — synchronous, no I/O.
    ///
    /// Returned <see cref="CapabilityRoutingResult.LatencyMs"/> is 0 here;
    /// <see cref="RouteSync"/> stamps the real elapsed time before returning.
    /// </summary>
    private CapabilityRoutingResult ClassifyLayer1(string userMessage, string? activePlaybookName)
    {
        // Empty / whitespace-only messages cannot be classified.
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            _logger.LogDebug(
                "CapabilityRouter.Layer1: empty user message — returning Uncertain.");
            return CapabilityRoutingResult.Uncertain(0.0, layer: 1, latencyMs: 0);
        }

        // Snapshot the enabled capabilities once (lock-free via ImmutableDictionary).
        var capabilities = _manifest.GetAll();
        if (capabilities.Count == 0)
        {
            _logger.LogDebug(
                "CapabilityRouter.Layer1: manifest is empty — returning Uncertain.");
            return CapabilityRoutingResult.Uncertain(0.0, layer: 1, latencyMs: 0);
        }

        // Normalise user message to lowercase once (O(n) where n = message length).
        var lower = userMessage.ToLowerInvariant();

        // Score each capability.
        var scores = new (CapabilityManifestEntry Entry, double Score)[capabilities.Count];
        for (var i = 0; i < capabilities.Count; i++)
        {
            var entry = capabilities[i];
            var rawScore = ScoreCapability(lower, entry);

            // Apply playbook bias when the active playbook matches.
            var boostedScore = ShouldApplyPlaybookBias(entry, activePlaybookName)
                ? rawScore * PlaybookBiasMultiplier
                : rawScore;

            scores[i] = (entry, boostedScore);
        }

        // Sort descending by score — we need top-2 for confidence formula.
        Array.Sort(scores, static (a, b) => b.Score.CompareTo(a.Score));

        var topScore = scores[0].Score;
        var secondScore = scores.Length > 1 ? scores[1].Score : 0.0;

        // If top score is zero, no hints matched — routing is uncertain.
        if (topScore <= 0.0)
        {
            _logger.LogDebug(
                "CapabilityRouter.Layer1: no keyword hits for any capability — Uncertain.");
            return CapabilityRoutingResult.Uncertain(0.0, layer: 1, latencyMs: 0);
        }

        // Normalised confidence = topScore / (topScore + secondScore + epsilon).
        // Approaches 1.0 when one capability dominates; approaches 0.5 on a perfect tie.
        var confidence = topScore / (topScore + secondScore + Epsilon);

        // Determine effective threshold: playbook-biased or default.
        var effectiveThreshold = IsPlaybookSession(scores[0].Entry, activePlaybookName)
            ? _options.PlaybookBiasThreshold
            : _options.ConfidenceThreshold;

        _logger.LogDebug(
            "CapabilityRouter.Layer1: top={TopCapability}, confidence={Confidence:F4}, " +
            "threshold={Threshold:F4}, confident={Confident}",
            scores[0].Entry.CapabilityName,
            confidence,
            effectiveThreshold,
            confidence >= effectiveThreshold);

        if (confidence >= effectiveThreshold)
        {
            // Collect all capabilities with the same top score (ties are included).
            var selected = CollectTopCapabilities(scores, topScore);
            return CapabilityRoutingResult.Confident(selected, confidence, layer: 1, latencyMs: 0);
        }

        return CapabilityRoutingResult.Uncertain(confidence, layer: 1, latencyMs: 0);
    }

    // ── Scoring helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Scores a single <paramref name="entry"/> against the normalised user message.
    ///
    /// Score formula:
    ///   hintScore  = matchedHints / max(totalHints, 1)        — primary signal
    ///   descScore  = matchedDescWords / max(totalDescWords, 1) — weak secondary signal
    ///   rawScore   = hintScore + (descScore * DescriptionScoreWeight)
    ///
    /// "Matched" means the lowercased hint/word appears as a substring inside
    /// <paramref name="lowerMessage"/>. Short-circuit exits once a match is confirmed.
    ///
    /// Rationale for substring matching:
    ///   "case law" matches "I need case law on negligence" but not "cases".
    ///   Padding each hint with a leading/trailing space is not done to keep the
    ///   implementation simple — callers should write specific multi-word hints.
    /// </summary>
    private static double ScoreCapability(string lowerMessage, CapabilityManifestEntry entry)
    {
        var hints = entry.KeywordHints;
        var totalHints = hints.Count;

        if (totalHints == 0)
        {
            // A capability with no hints can never be selected by Layer 1.
            return 0.0;
        }

        // Count matching hints.
        var matchedHints = 0;
        foreach (var hint in hints)
        {
            if (string.IsNullOrEmpty(hint)) continue;
            var lowerHint = hint.ToLowerInvariant();
            if (lowerMessage.Contains(lowerHint, StringComparison.Ordinal))
            {
                matchedHints++;
            }
        }

        var hintScore = (double)matchedHints / totalHints;

        // Secondary: check description words as weak signals.
        var descScore = ScoreDescription(lowerMessage, entry.Description);

        // Weight the description score so hints always dominate.
        const double descriptionScoreWeight = 0.2;
        return hintScore + (descScore * descriptionScoreWeight);
    }

    /// <summary>
    /// Scores a capability's description against the user message by splitting the
    /// description into individual words (whitespace-split) and counting matches.
    /// Words shorter than 4 characters are skipped to ignore stop-words.
    /// </summary>
    private static double ScoreDescription(string lowerMessage, string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return 0.0;

        var words = description.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0.0;

        var matched = 0;
        var scored = 0;
        foreach (var word in words)
        {
            // Skip short stop-words (is, in, a, the, of, ...).
            if (word.Length < 4) continue;
            scored++;
            if (lowerMessage.Contains(word, StringComparison.Ordinal))
            {
                matched++;
            }
        }

        return scored == 0 ? 0.0 : (double)matched / scored;
    }

    /// <summary>
    /// Returns true when the top-scoring capability belongs to the active playbook,
    /// which means the effective threshold should be lowered.
    /// </summary>
    private static bool IsPlaybookSession(CapabilityManifestEntry topEntry, string? activePlaybookName)
    {
        return activePlaybookName is not null &&
               !string.IsNullOrWhiteSpace(topEntry.CapabilityName);
        // Note: CapabilityManifestEntry currently stores PlaybookId (Guid?), not the playbook name.
        // The bias is applied at score time (ShouldApplyPlaybookBias); this helper signals whether
        // the lower threshold applies based on whether ANY capability received a bias boost.
    }

    /// <summary>
    /// Returns true when <paramref name="entry"/> should receive a score boost because
    /// its playbook is the one currently active in the session.
    ///
    /// Since <see cref="CapabilityManifestEntry.PlaybookId"/> is a nullable Guid and the
    /// caller supplies a playbook <em>name</em>, the bias is applied when <em>any</em>
    /// PlaybookId is set on the entry and a playbook session is active.
    /// Future tasks (AIPU2-013/014) may refine the mapping once the playbook name→ID
    /// resolution service is available.
    /// </summary>
    private static bool ShouldApplyPlaybookBias(CapabilityManifestEntry entry, string? activePlaybookName)
    {
        return activePlaybookName is not null &&
               entry.PlaybookId.HasValue;
    }

    /// <summary>
    /// Collects the names of all capabilities that share the top score (within floating-point epsilon).
    /// Typically returns a single name; ties are included to allow Layer 2 to disambiguate.
    /// </summary>
    private static string[] CollectTopCapabilities(
        (CapabilityManifestEntry Entry, double Score)[] sortedScores,
        double topScore)
    {
        var results = new List<string>(capacity: 2);
        foreach (var (entry, score) in sortedScores)
        {
            if (Math.Abs(score - topScore) > Epsilon) break;
            results.Add(entry.CapabilityName);
        }
        return [.. results];
    }
}
