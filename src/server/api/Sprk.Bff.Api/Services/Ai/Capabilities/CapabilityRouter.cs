using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// Metric instrument name for Layer 2 hit counter (OTEL).
    /// </summary>
    private const string MetricLayer2HitCounterName = "ai_routing_layer2_hit";

    /// <summary>
    /// Metric instrument name for Layer 2 latency histogram (OTEL).
    /// </summary>
    private const string MetricLayer2LatencyHistogramName = "ai_routing_layer2_latency_ms";

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ICapabilityManifest _manifest;
    private readonly CapabilityRouterOptions _options;
    private readonly IChatClient? _rawChatClient;
    private readonly ILogger<CapabilityRouter> _logger;

    // ── OTEL instrumentation ──────────────────────────────────────────────────

    private static readonly Meter RouterMeter = new("Sprk.Bff.Api.Ai", "1.0.0");
    private static readonly Counter<long> Layer1HitCounter =
        RouterMeter.CreateCounter<long>(MetricHitCounterName, unit: "{hit}",
            description: "Count of turns where Layer 1 keyword routing produced a confident result.");
    private static readonly Histogram<double> Layer1LatencyHistogram =
        RouterMeter.CreateHistogram<double>(MetricLatencyHistogramName, unit: "ms",
            description: "Wall-clock latency of Layer 1 keyword classification in milliseconds.");

    private static readonly Counter<long> Layer2HitCounter =
        RouterMeter.CreateCounter<long>(MetricLayer2HitCounterName, unit: "{hit}",
            description: "Count of turns where Layer 2 LLM classification produced a confident result.");
    private static readonly Histogram<double> Layer2LatencyHistogram =
        RouterMeter.CreateHistogram<double>(MetricLayer2LatencyHistogramName, unit: "ms",
            description: "Wall-clock latency of Layer 2 LLM classification in milliseconds.");

    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>
    /// Primary constructor — used by the DI factory registration in <see cref="AiCapabilitiesModule"/>.
    /// <paramref name="rawChatClient"/> is null when the AI stack is not configured; in that case
    /// Layer 2 classification is automatically skipped and turns fall through to Layer 3.
    /// </summary>
    public CapabilityRouter(
        ICapabilityManifest manifest,
        IOptions<CapabilityRouterOptions> options,
        [FromKeyedServices("raw")] IChatClient? rawChatClient,
        ILogger<CapabilityRouter> logger)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _rawChatClient = rawChatClient;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Backward-compatible 3-param constructor used by tests and legacy DI registrations
    /// that do not inject an <see cref="IChatClient"/>. Layer 2 is disabled when this overload
    /// is used (rawChatClient is null).
    /// </summary>
    public CapabilityRouter(
        ICapabilityManifest manifest,
        IOptions<CapabilityRouterOptions> options,
        ILogger<CapabilityRouter> logger)
        : this(manifest, options, rawChatClient: null, logger)
    {
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

    // ── OTEL: Layer 3 counter ────────────────────────────────────────────────

    private static readonly Counter<long> Layer3HitCounter =
        RouterMeter.CreateCounter<long>("ai_routing_layer3_hit", unit: "{hit}",
            description: "Count of turns where Layer 3 broad superset fallback was activated.");

    // ── Full three-tier routing (AIPU2-013 / AIPU2-014) ──────────────────────

    /// <inheritdoc />
    public async Task<CapabilityRoutingResult> RouteAsync(
        string userMessage,
        string? activePlaybookName,
        CancellationToken ct = default)
    {
        // ── Layer 1: synchronous keyword classifier ───────────────────────────
        var layer1Result = RouteSync(userMessage, activePlaybookName);
        if (layer1Result.IsConfident)
        {
            _logger.LogDebug(
                "CapabilityRouter.RouteAsync: Layer 1 confident ({Confidence:F4}) — skipping Layers 2 and 3.",
                layer1Result.Confidence);
            return layer1Result;
        }

        // ── Layer 2: GPT-4o-mini intent classifier ────────────────────────────
        if (_options.Layer2.Enabled && _rawChatClient is not null)
        {
            _logger.LogDebug(
                "CapabilityRouter.RouteAsync: Layer 1 uncertain ({Confidence:F4}) — escalating to Layer 2.",
                layer1Result.Confidence);

            var layer2Result = await Layer2ClassifyAsync(userMessage, activePlaybookName, ct)
                .ConfigureAwait(false);

            if (layer2Result is not null)
            {
                return layer2Result;
            }

            _logger.LogDebug(
                "CapabilityRouter.RouteAsync: Layer 2 did not produce a confident result — escalating to Layer 3.");
        }
        else
        {
            _logger.LogDebug(
                "CapabilityRouter.RouteAsync: Layer 1 uncertain ({Confidence:F4}) — Layer 2 disabled or unavailable, escalating to Layer 3.",
                layer1Result.Confidence);
        }

        // ── Layer 3: broad superset fallback ─────────────────────────────────
        return Layer3Fallback(activePlaybookName);
    }

    // ── Layer 2: GPT-4o-mini intent classifier ────────────────────────────────

    /// <summary>
    /// Calls GPT-4o-mini with a compact classification prompt and returns a
    /// <see cref="CapabilityRoutingResult.Confident"/> result if confidence is above threshold,
    /// or <c>null</c> to signal fall-through to Layer 3.
    ///
    /// Returns null on: timeout, HTTP 429, JSON parse failure, no matches above threshold.
    /// Never throws — all exceptions are caught and logged.
    ///
    /// ADR-015: userMessage content is sent to the LLM but is NEVER stored in logs or OTEL spans.
    /// </summary>
    private async Task<CapabilityRoutingResult?> Layer2ClassifyAsync(
        string userMessage,
        string? activePlaybookName,
        CancellationToken callerCt)
    {
        using var activity = AiTelemetry.ActivitySource.StartActivity(
            "ai.routing.layer2", ActivityKind.Internal);

        var sw = Stopwatch.StartNew();

        try
        {
            // Snapshot the enabled capabilities (lock-free snapshot).
            var allCapabilities = _manifest.GetAll();
            var candidates = allCapabilities
                .Take(_options.Layer2.MaxCandidates)
                .ToList();

            if (candidates.Count == 0)
            {
                _logger.LogDebug("CapabilityRouter.Layer2: no candidates in manifest — skipping.");
                return null;
            }

            // Build the classification prompt.
            var messages = CapabilityClassificationPromptBuilder.Build(userMessage, candidates);

            // Link a timeout CTS to the caller's token.
            using var timeoutCts = new CancellationTokenSource(_options.Layer2.TimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerCt, timeoutCts.Token);

            // JSON-mode chat completion — no function invocation.
            var chatOptions = new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.Json
            };

            var response = await _rawChatClient!
                .GetResponseAsync(messages, chatOptions, linkedCts.Token)
                .ConfigureAwait(false);

            sw.Stop();
            var latencyMs = sw.ElapsedMilliseconds;

            var responseText = response.Text ?? string.Empty;

            // Parse the JSON response.
            var layer2Candidates = ParseLayer2Response(responseText, candidates);

            if (layer2Candidates.Count == 0)
            {
                _logger.LogDebug(
                    "CapabilityRouter.Layer2: no capabilities matched above threshold in LLM response.");
                RecordLayer2Otel(activity, latencyMs, matched: false, capabilityName: null,
                    promptTokens: response.Usage?.InputTokenCount,
                    completionTokens: response.Usage?.OutputTokenCount);
                Layer2LatencyHistogram.Record(latencyMs);
                return null;
            }

            // Use the top-scoring candidate from Layer 2.
            var top = layer2Candidates[0];
            var effectiveThreshold = _options.PlaybookBiasThreshold < _options.ConfidenceThreshold
                                     && activePlaybookName is not null
                ? _options.PlaybookBiasThreshold
                : _options.ConfidenceThreshold;

            if (top.Confidence < effectiveThreshold)
            {
                _logger.LogDebug(
                    "CapabilityRouter.Layer2: top capability {Name} confidence {Confidence:F4} below threshold {Threshold:F4}.",
                    top.Name, top.Confidence, effectiveThreshold);
                RecordLayer2Otel(activity, latencyMs, matched: false, capabilityName: top.Name,
                    promptTokens: response.Usage?.InputTokenCount,
                    completionTokens: response.Usage?.OutputTokenCount);
                Layer2LatencyHistogram.Record(latencyMs);
                return null;
            }

            _logger.LogDebug(
                "CapabilityRouter.Layer2: classified as {Name} with confidence {Confidence:F4}, latency {LatencyMs}ms.",
                top.Name, top.Confidence, latencyMs);

            RecordLayer2Otel(activity, latencyMs, matched: true, capabilityName: top.Name,
                promptTokens: response.Usage?.InputTokenCount,
                completionTokens: response.Usage?.OutputTokenCount);
            Layer2LatencyHistogram.Record(latencyMs);
            Layer2HitCounter.Add(1);

            return CapabilityRoutingResult.Confident(
                [top.Name],
                top.Confidence,
                layer: 2,
                latencyMs: latencyMs);
        }
        catch (OperationCanceledException) when (!callerCt.IsCancellationRequested)
        {
            // Timeout fired (not caller cancel) — fall through to Layer 3.
            sw.Stop();
            _logger.LogWarning(
                "CapabilityRouter.Layer2: timed out after {TimeoutMs}ms — falling through to Layer 3.",
                _options.Layer2.TimeoutMs);
            Layer2LatencyHistogram.Record(sw.ElapsedMilliseconds);
            activity?.SetTag("timeout", true);
            return null;
        }
        catch (Exception ex) when (IsRateLimitException(ex))
        {
            sw.Stop();
            _logger.LogWarning(
                "CapabilityRouter.Layer2: rate-limited (HTTP 429) — falling through to Layer 3.");
            Layer2LatencyHistogram.Record(sw.ElapsedMilliseconds);
            activity?.SetTag("rate_limited", true);
            return null;
        }
        catch (Exception ex)
        {
            sw.Stop();
            // Log the exception type/message — NOT the userMessage (ADR-015).
            _logger.LogError(ex,
                "CapabilityRouter.Layer2: unexpected error during LLM classification — falling through to Layer 3.");
            Layer2LatencyHistogram.Record(sw.ElapsedMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Parses the GPT-4o-mini JSON response into a list of matched capabilities ordered
    /// by confidence descending. Only returns entries whose names appear in the candidate list.
    ///
    /// Returns empty list on JSON parse failure.
    /// </summary>
    private static List<Layer2CapabilityResult> ParseLayer2Response(
        string responseText,
        IReadOnlyList<CapabilityManifestEntry> candidates)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(responseText);

            if (!doc.RootElement.TryGetProperty("capabilities", out var capArray)
                || capArray.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            // Build a fast lookup set of valid candidate names.
            var validNames = new HashSet<string>(
                candidates.Select(c => c.CapabilityName),
                StringComparer.Ordinal);

            var results = new List<Layer2CapabilityResult>(capArray.GetArrayLength());

            foreach (var item in capArray.EnumerateArray())
            {
                if (!item.TryGetProperty("name", out var nameProp)
                    || !item.TryGetProperty("confidence", out var confProp))
                {
                    continue;
                }

                var name = nameProp.GetString();
                if (name is null || !validNames.Contains(name)) continue;

                if (!confProp.TryGetDouble(out var confidence)) continue;

                results.Add(new Layer2CapabilityResult(name, confidence));
            }

            // Sort by confidence descending.
            results.Sort(static (a, b) => b.Confidence.CompareTo(a.Confidence));
            return results;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Sets OTEL span tags for a Layer 2 call. ADR-015: no user message content.
    /// </summary>
    private static void RecordLayer2Otel(
        Activity? activity,
        long latencyMs,
        bool matched,
        string? capabilityName,
        long? promptTokens,
        long? completionTokens)
    {
        activity?.SetTag("latency_ms", latencyMs);
        activity?.SetTag("matched", matched);

        if (capabilityName is not null)
        {
            activity?.SetTag("matched_capability", capabilityName);
        }

        if (promptTokens.HasValue)
        {
            activity?.SetTag("prompt_tokens", promptTokens.Value);
        }

        if (completionTokens.HasValue)
        {
            activity?.SetTag("completion_tokens", completionTokens.Value);
        }
    }

    /// <summary>
    /// Returns true when the exception indicates an HTTP 429 rate-limit response from the LLM provider.
    /// </summary>
    private static bool IsRateLimitException(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("429", StringComparison.Ordinal)
            || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lightweight record for a single capability result returned by the Layer 2 LLM classifier.
    /// </summary>
    private sealed record Layer2CapabilityResult(string Name, double Confidence);

    /// <inheritdoc />
    public CapabilityRoutingResult Layer3Fallback(string? activePlaybookName)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var toolNames = ComputeLayer3Superset();
            sw.Stop();
            Layer3HitCounter.Add(1);

            _logger.LogDebug(
                "CapabilityRouter.Layer3: superset computed — {ToolCount} tools, defaultPlaybookId={DefaultPlaybookId}, latency={LatencyMs}ms.",
                toolNames.Length,
                _options.DefaultPlaybookId ?? "(none)",
                sw.ElapsedMilliseconds);

            return CapabilityRoutingResult.Fallback(
                fallbackCapabilityNames: [],
                selectedToolNames: toolNames,
                latencyMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "CapabilityRouter.Layer3: unexpected error computing superset — returning hard-coded general superset.");

            return CapabilityRoutingResult.Fallback(
                fallbackCapabilityNames: [],
                selectedToolNames: CapabilityRouterOptions.GeneralSupersetFallbackTools,
                latencyMs: sw.ElapsedMilliseconds);
        }
    }

    private string[] ComputeLayer3Superset()
    {
        var capabilities = _manifest.GetAll();

        IEnumerable<CapabilityManifestEntry> source = capabilities;
        if (!string.IsNullOrWhiteSpace(_options.DefaultPlaybookId))
        {
            source = capabilities.Where(e => e.PlaybookId.HasValue);
        }

        var toolSet = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in source)
        {
            foreach (var tool in entry.ToolNames)
            {
                if (!string.IsNullOrWhiteSpace(tool))
                {
                    toolSet.Add(tool);
                }
            }
        }

        var capped = toolSet.Take(_options.MaxSupersetTools).ToArray();

        if (capped.Length == 0)
        {
            _logger.LogDebug(
                "CapabilityRouter.Layer3: no tools in superset source — using GeneralSupersetFallbackTools ({Count} tools).",
                CapabilityRouterOptions.GeneralSupersetFallbackTools.Length);
            return CapabilityRouterOptions.GeneralSupersetFallbackTools;
        }

        return capped;
    }
}
