using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Services.Ai.Telemetry;

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

    // ── Layer 0: voice command pre-pass (R6 Pillar 7 / task 069 / FR-47) ─────
    //
    // BEFORE the keyword classifier runs, we match three deterministic voice
    // patterns ("remember X", "forget X", "always X") and short-circuit to a
    // synthetic `manage_pinned_context` capability. This biases the LLM toward
    // invoking the ManagePinnedContextHandler tool (registered via the
    // sprk_analysistool seed row) for memory voice commands. Patterns are
    // anchored at the start of the message (with optional leading whitespace)
    // and required to be followed by a word boundary so words like "remembered"
    // and "forgetfulness" do not false-fire.
    //
    // ADR-015 audit: the pre-pass returns a SYNTHETIC capability name (a config
    // identifier — Tier-1 safe). It NEVER captures the matched user text into a
    // span tag, log line, or counter dimension.
    //
    // NFR-03 budget: three pre-compiled regex matches per turn — empirically
    // sub-millisecond on a 50-message corpus. The Layer 1 hard limit of 50ms
    // remains the binding budget; Layer 0 sits well inside it.

    /// <summary>Synthetic capability name returned when a voice command is recognised.</summary>
    internal const string VoiceMemoryCapabilityName = "manage_pinned_context";

    /// <summary>Decision discriminator emitted by the context.decision_made event for the Layer 0 short-circuit path.</summary>
    internal const string VoiceMemoryDecisionConfident = "voice_memory";

    // ── Layer 0.5: soft-slash command-intent pre-pass (R6 Pillar 8 / task 082 / FR-50) ──
    //
    // BEFORE the Layer 1 keyword classifier runs, we honour an optional
    // `intentHint` value supplied by the frontend `SoftSlashRouter`. The
    // closed Q6 vocabulary maps four soft slashes (`/summarize`, `/draft`,
    // `/extract-entities`, `/analyze`) → four synthetic capability names that
    // pre-select the correct route on FIRST try, satisfying spec FR-50
    // ("strong intent signal", "pre-selects route") + Phase D exit criterion 3.
    //
    // The mapping is deterministic + closed (Q6 — owner-bound vocabulary). The
    // pre-pass is a single dictionary lookup; well inside NFR-03 budget.
    //
    // ADR-015 audit: the keys in this table are config-side capability names
    // (Tier-1 safe). The values come from the client-supplied `intentHint`
    // field — which itself is a closed-vocabulary string set by
    // `SoftSlashRouter.decorateBody`, NEVER raw user text. The pre-pass emits
    // ONE context.decision_made event with the synthetic capability name; the
    // raw user message is never tagged or logged.
    //
    // ADR-013 audit: this is internal infrastructure, not a new public-contract
    // surface in `Services/Ai/PublicContracts/`. The IInvokePlaybookAi facade
    // is invoked downstream (after the agent selects the matched capability's
    // playbook); this pre-pass only HINTS the router.
    //
    // NFR-11 binding: when `intentHint` is null (the common path — natural
    // language, hard slashes, unrecognised slashes), this pre-pass falls
    // through to the existing Layer 0 voice memory check + Layer 1 keyword
    // classification UNCHANGED. Natural-language equivalents ("summarize this")
    // still route via Layer 1 keyword scoring.

    /// <summary>
    /// Synthetic capability names returned by the Layer 0.5 soft-slash pre-pass.
    /// The synthetic name is a deterministic config identifier; the manifest is
    /// NOT consulted at this layer (the agent's tool selection later resolves
    /// the playbook by name when needed). Wave-9 closed at exactly 4 per Q6.
    /// </summary>
    internal const string SoftSlashSummarizeCapabilityName = "invoke_playbook_summarize";
    internal const string SoftSlashDraftCapabilityName = "invoke_playbook_draft";
    internal const string SoftSlashExtractEntitiesCapabilityName = "invoke_handler_extract_entities";
    internal const string SoftSlashAnalyzeCapabilityName = "invoke_playbook_analyze";

    /// <summary>Decision discriminator emitted by the context.decision_made event for the Layer 0.5 short-circuit path.</summary>
    internal const string SoftSlashDecisionConfident = "soft_slash";

    /// <summary>
    /// Closed-vocabulary mapping from a frontend-supplied `intentHint` to its
    /// synthetic capability name. Vocabulary is owner-locked at exactly 4 per
    /// Q6 — do NOT extend without spec FR sign-off. Ordinal (case-sensitive)
    /// comparison: the client emits lowercase identifiers from a TypeScript
    /// `Record` mapping; mismatched case is treated as "unrecognised" and the
    /// pre-pass falls through to Layer 1 normally.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> SoftSlashIntentToCapabilityName =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["summarize"] = SoftSlashSummarizeCapabilityName,
            ["draft"] = SoftSlashDraftCapabilityName,
            ["extract-entities"] = SoftSlashExtractEntitiesCapabilityName,
            ["analyze"] = SoftSlashAnalyzeCapabilityName,
        };

    /// <summary>
    /// Compiled regex matching the three voice command openers ("remember", "forget", "always")
    /// at the start of the trimmed user message. Capture group 1 carries the trigger word so
    /// telemetry can record which voice opener fired without recording the user-supplied tail.
    /// </summary>
    private static readonly Regex VoiceMemoryRegex = new(
        @"^\s*(remember|forget|always)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    /// <summary>
    /// R6 Pillar 6c (FR-37 / task 063) — optional context.* event emitter for
    /// <c>context.decision_made</c> emission at Layer 1 / Layer 2 / Layer 3 outcomes.
    /// ADR-015 audit: payload carries only layer (enum-like), decision (enum-like),
    /// capability NAME (config identifier, Tier 1 safe), sessionId, tenantId. Never user
    /// message text. Optional so existing test fixtures construct cleanly.
    /// </summary>
    private readonly IContextEventEmitter? _contextEventEmitter;

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
        ILogger<CapabilityRouter> logger,
        IContextEventEmitter? contextEventEmitter = null)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _rawChatClient = rawChatClient;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contextEventEmitter = contextEventEmitter;
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
        : this(manifest, options, rawChatClient: null, logger, contextEventEmitter: null)
    {
    }

    // ── ICapabilityRouter ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public CapabilityRoutingResult RouteSync(string userMessage, string? activePlaybookName, string? intentHint = null)
    {
        // R6 Pillar 7 / task 069 / FR-47 — Layer 0 voice command pre-pass.
        //
        // Match BEFORE Layer 1 keyword scoring so the synthetic
        // manage_pinned_context capability is recognised for "remember X" /
        // "forget X" / "always X" regardless of the manifest's current
        // keyword-hint coverage. Layer 0 short-circuits with a Confident
        // result selecting only the voice-memory capability. ADR-015: the
        // matched user text is never logged or tagged; only the trigger word
        // (one of the three deterministic openers) appears.
        var voiceMemoryResult = TryClassifyVoiceMemory(userMessage);
        if (voiceMemoryResult is not null)
        {
            return voiceMemoryResult;
        }

        // R6 Pillar 8 / task 082 / FR-50 — Layer 0.5 soft-slash pre-pass.
        //
        // When the frontend `SoftSlashRouter` decorated the outbound payload
        // with a closed-vocabulary `intentHint`, the pre-pass deterministically
        // selects the matching synthetic capability — so the agent's tool
        // selection sees a Confident routing result on FIRST try. When
        // `intentHint` is null (natural language, hard slashes, unrecognised
        // slashes), this returns null and the existing Layer 1 keyword scoring
        // runs unchanged (NFR-11 binding).
        var softSlashResult = TryClassifySoftSlash(intentHint);
        if (softSlashResult is not null)
        {
            return softSlashResult;
        }

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

            // R6 task 042 (FR-30): emit the resolved playbook GUID when unambiguous, so
            // downstream observability can correlate a routing decision with the playbook
            // that drove the render destination. ADR-015 compliant — a deterministic ID,
            // not user content. Null-safe: only set when a single playbook was resolved.
            if (result.SelectedPlaybookId.HasValue)
            {
                activity?.SetTag("selected_playbook_id", result.SelectedPlaybookId.Value.ToString("D"));
            }

            // Emit metrics.
            Layer1LatencyHistogram.Record(sw.Elapsed.TotalMilliseconds);
            if (result.IsConfident)
            {
                Layer1HitCounter.Add(1);
            }

            // R6 Pillar 6c (FR-37 / task 063) — context.decision_made (Layer 1).
            // ADR-015 audit: layer = "layer1" (enum-like), decision = "confident" or
            // "uncertain" (enum-like), capabilityName = config identifier (Tier 1 safe).
            // No user message content, no scores, no payload.
            _contextEventEmitter?.DecisionMade(
                layer: "layer1",
                decision: result.IsConfident ? "confident" : "uncertain",
                capabilityName: result.SelectedCapabilities.Length > 0 ? result.SelectedCapabilities[0] : null,
                sessionId: null,
                tenantId: null);

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

    // ── Layer 0: voice command pre-pass (R6 Pillar 7 / task 069 / FR-47) ─────

    /// <summary>
    /// Layer 0 pre-pass for the "remember X" / "forget X" / "always X" voice memory
    /// commands. Returns a <see cref="CapabilityRoutingResult.Confident"/> result selecting
    /// the synthetic <see cref="VoiceMemoryCapabilityName"/> capability when the message
    /// starts with one of the three trigger words; returns <c>null</c> otherwise to let
    /// Layer 1 keyword scoring run normally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR-015 audit: the matched user-message text is NEVER captured into log lines,
    /// span tags, or counter dimensions. The only telemetry surface is (a) the synthetic
    /// capability name (a config identifier — Tier-1 safe) and (b) the
    /// <c>context.decision_made</c> emission with <c>decision = "voice_memory"</c> +
    /// <c>capabilityName = "manage_pinned_context"</c>. Both are deterministic strings.
    /// </para>
    /// <para>
    /// NFR-03 budget: one pre-compiled regex match per turn; well inside the 50ms hard
    /// limit. We deliberately do NOT start a Stopwatch / OTEL activity here — the
    /// downstream Layer 1 path picks up the latency budget when this returns null, and
    /// the short-circuit path emits a single decision_made event without per-call
    /// activity overhead.
    /// </para>
    /// </remarks>
    private CapabilityRoutingResult? TryClassifyVoiceMemory(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return null;
        }

        var match = VoiceMemoryRegex.Match(userMessage);
        if (!match.Success)
        {
            return null;
        }

        // Emit context.decision_made for the Layer 0 short-circuit. ADR-015 audit:
        // capabilityName = synthetic name (config identifier); decision =
        // "voice_memory" enum; no user message text. The session/tenant fields are
        // left null at this site — the router does not have them and the downstream
        // chat agent emits its own per-tool-call telemetry with full identity.
        _contextEventEmitter?.DecisionMade(
            layer: "layer0",
            decision: VoiceMemoryDecisionConfident,
            capabilityName: VoiceMemoryCapabilityName,
            sessionId: null,
            tenantId: null);

        _logger.LogDebug(
            "CapabilityRouter.Layer0: voice memory pre-pass matched — capability={Capability}, trigger={Trigger}.",
            VoiceMemoryCapabilityName,
            match.Groups[1].Value.ToLowerInvariant());

        return CapabilityRoutingResult.Confident(
            selectedCapabilities: new[] { VoiceMemoryCapabilityName },
            confidence: 1.0,
            layer: 0,
            latencyMs: 0,
            selectedPlaybookId: null);
    }

    // ── Layer 0.5: soft-slash command-intent pre-pass (R6 Pillar 8 / task 082 / FR-50) ──

    /// <summary>
    /// Layer 0.5 pre-pass for the four soft-slash command intents emitted by the
    /// frontend `SoftSlashRouter.decorateBody()`: <c>summarize</c>, <c>draft</c>,
    /// <c>extract-entities</c>, <c>analyze</c>. Returns a Confident result selecting
    /// the synthetic capability for the matched intent; returns <c>null</c> when
    /// <paramref name="intentHint"/> is null/whitespace OR not in the closed
    /// vocabulary (per Q6 — owner-locked at 4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR-015 audit: <paramref name="intentHint"/> is a closed-vocabulary
    /// identifier emitted by the client, NEVER raw user message text. The
    /// emitted telemetry surface is (a) the synthetic capability name (config
    /// identifier — Tier-1 safe) and (b) the <c>context.decision_made</c> event
    /// with <c>decision = "soft_slash"</c> + <c>capabilityName = <synthetic></c>.
    /// No user message content is captured.
    /// </para>
    /// <para>
    /// ADR-013 audit: this method is internal infrastructure; it does NOT add
    /// any new public-contract surface in <c>Services/Ai/PublicContracts/</c>.
    /// </para>
    /// <para>
    /// NFR-11 binding: when <paramref name="intentHint"/> is null the
    /// pre-pass returns null and downstream Layer 1 keyword classification runs
    /// UNCHANGED, preserving the natural-language path.
    /// </para>
    /// </remarks>
    private CapabilityRoutingResult? TryClassifySoftSlash(string? intentHint)
    {
        if (string.IsNullOrWhiteSpace(intentHint))
        {
            return null;
        }

        if (!SoftSlashIntentToCapabilityName.TryGetValue(intentHint, out var capabilityName))
        {
            // Unrecognised intent — fall through so Layer 1 keyword scoring still
            // has a chance to classify by literal command text in the message.
            _logger.LogDebug(
                "CapabilityRouter.Layer0.5: unrecognised intentHint — falling through to Layer 1.");
            return null;
        }

        // R6 hotfix 2026-06-21 (UAT #4): empty-manifest guard. Verify the synthetic
        // capability is actually present in the manifest with non-empty ToolNames.
        // When the sprk_aicapability table is not provisioned (or has been wiped),
        // a "Confident" routing result here selects a capability with zero tools,
        // and the LLM ends up with no invoke_playbook tool to call — manifesting as
        // /summarize producing a chat-only response with NO Workspace tab (the UAT
        // bug observed 2026-06-19). NL "summarize" worked because it falls all the
        // way to Layer 3, where Hotfix #3 emptied GeneralSupersetFallbackTools to
        // trigger the "empty allowed set → full capability-gated tool set" rescue
        // path. This guard makes the slash path behave the same: fall through so
        // the downstream rescue fires for both. Same defensive pattern as Hotfix #3.
        if (!_manifest.TryGet(capabilityName, out var manifestEntry)
            || manifestEntry is null
            || manifestEntry.ToolNames.Count == 0)
        {
            _logger.LogInformation(
                "CapabilityRouter.Layer0.5: synthetic capability '{CapabilityName}' " +
                "not in manifest or has no tools — falling through to Layer 1+ so " +
                "downstream fallback can deliver tools.",
                capabilityName);
            return null;
        }

        // R6 Pillar 6c (FR-37 / task 063) — context.decision_made (Layer 0.5 soft slash).
        // ADR-015 audit: capabilityName is a synthetic config identifier;
        // decision is the "soft_slash" enum string; no user message text.
        _contextEventEmitter?.DecisionMade(
            layer: "layer1",
            decision: SoftSlashDecisionConfident,
            capabilityName: capabilityName,
            sessionId: null,
            tenantId: null);

        _logger.LogDebug(
            "CapabilityRouter.Layer0.5: soft-slash pre-pass matched — capability={Capability}, intent={Intent}.",
            capabilityName,
            intentHint);

        return CapabilityRoutingResult.Confident(
            selectedCapabilities: new[] { capabilityName },
            confidence: 1.0,
            layer: 1,
            latencyMs: 0,
            selectedPlaybookId: null);
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

            // R6 task 042 (FR-30): When the resolution is UNAMBIGUOUS (exactly one
            // top-scoring capability) and the winning entry has a PlaybookId, propagate
            // it so the consumer (SprkChatAgentFactory) can dedup the render destination
            // by consulting the playbook's terminal node config. Ambiguous ties (multiple
            // capabilities at the top score) leave SelectedPlaybookId null — the
            // chat-agent falls through to conversational primacy (NFR-01 preserved).
            var selectedPlaybookId = selected.Length == 1 ? scores[0].Entry.PlaybookId : null;

            return CapabilityRoutingResult.Confident(
                selected, confidence, layer: 1, latencyMs: 0, selectedPlaybookId: selectedPlaybookId);
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
    /// "Matched" means the lowercased hint/word appears in <paramref name="lowerMessage"/>
    /// as a whole token bounded by word boundaries (regex <c>\b</c>). This eliminates
    /// the bigram-superstring false-positive class — e.g., hint <c>"case law"</c> no
    /// longer matches the message <c>"henderson case to urgent"</c> via substring
    /// equality, and the description word <c>"case"</c> still matches but only when
    /// it appears as a standalone token rather than being embedded in another word
    /// (e.g., <c>"cases"</c>, <c>"casework"</c>).
    ///
    /// RB-T053-01 fix (2026-06-01): swapped <c>String.Contains</c> for word-boundary
    /// regex matching to honour the documented zero-false-positive Layer-1 contract.
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
            if (TokenMatches(lowerMessage, hint))
            {
                matchedHints++;
            }
        }

        var hintScore = (double)matchedHints / totalHints;

        // RB-T053-01 Option B (2026-06-01): description-word scoring is DISABLED
        // (weight = 0.0) because the prior 0.2 weight produced confident false-positives
        // on the 105-message corpus. Empirically, common single description words like
        // `case` (in legal_research's description) matched messages such as "Henderson
        // case" — saturating confidence to ~1.0 even though the message intent was
        // unrelated to the capability. The ScoreDescription helper is preserved for
        // potential future re-enabling with a finer scoring rule (e.g., uniqueness-
        // weighted or co-occurrence-weighted), but the additive contribution is zero
        // for now. Per D-11 §B (owner decision 2026-06-01).
        const double descriptionScoreWeight = 0.0;
        var descScore = descriptionScoreWeight > 0.0
            ? ScoreDescription(lowerMessage, entry.Description)
            : 0.0;
        return hintScore + (descScore * descriptionScoreWeight);
    }

    /// <summary>
    /// Scores a capability's description against the user message by splitting the
    /// description into individual words (whitespace-split) and counting matches.
    /// Words shorter than 4 characters are skipped to ignore stop-words.
    ///
    /// Uses word-boundary matching so embedded substrings (<c>"casework"</c> ⊅
    /// <c>"case"</c>) do not produce false hits.
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
            if (TokenMatches(lowerMessage, word))
            {
                matched++;
            }
        }

        return scored == 0 ? 0.0 : (double)matched / scored;
    }

    // ── Word-boundary token matching (RB-T053-01 Option 1) ────────────────────

    /// <summary>
    /// Compiled regex cache keyed by the lowercased token. The Layer-1 hot path
    /// scores every capability hint and description word per turn — caching the
    /// compiled <see cref="Regex"/> keeps NFR-03 (<50ms classification) intact
    /// across the corpus of ~50 manifest entries × ~6 hints × 105 messages.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Regex> TokenRegexCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="token"/> appears in <paramref name="lowerMessage"/>
    /// as a whole token (bounded by word boundaries). Matching is case-insensitive
    /// because <paramref name="lowerMessage"/> is already lower-cased by the caller
    /// (<see cref="ClassifyLayer1"/>); the token is lower-cased here for symmetry.
    /// </summary>
    private static bool TokenMatches(string lowerMessage, string token)
    {
        if (string.IsNullOrEmpty(token)) return false;

        var lowerToken = token.ToLowerInvariant();
        var regex = TokenRegexCache.GetOrAdd(lowerToken, static t =>
            new Regex($@"\b{Regex.Escape(t)}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant));

        return regex.IsMatch(lowerMessage);
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
        CancellationToken ct = default,
        string? intentHint = null)
    {
        // ── Layer 1: synchronous keyword classifier (with Layer 0 + 0.5 pre-passes) ─
        var layer1Result = RouteSync(userMessage, activePlaybookName, intentHint);
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

            // R6 task 042 (FR-30): Layer 2 always returns a SINGLE top capability
            // (there is no tie semantics in the JSON-mode response), so propagate the
            // winning entry's PlaybookId when present. The lookup uses the candidate
            // list already snapshotted above so it stays O(N) over the small candidate
            // set and avoids re-touching the manifest.
            var topEntry = candidates.FirstOrDefault(
                c => string.Equals(c.CapabilityName, top.Name, StringComparison.Ordinal));
            var selectedPlaybookId = topEntry?.PlaybookId;

            RecordLayer2Otel(activity, latencyMs, matched: true, capabilityName: top.Name,
                promptTokens: response.Usage?.InputTokenCount,
                completionTokens: response.Usage?.OutputTokenCount);

            // R6 task 042 (FR-30): emit the resolved playbook GUID when present so
            // observability spans correlate with the playbook driving render destination
            // (ADR-015 compliant — deterministic ID, no user content).
            if (selectedPlaybookId.HasValue)
            {
                activity?.SetTag("selected_playbook_id", selectedPlaybookId.Value.ToString("D"));
            }

            Layer2LatencyHistogram.Record(latencyMs);
            Layer2HitCounter.Add(1);

            // R6 Pillar 6c (FR-37 / task 063) — context.decision_made (Layer 2 confident).
            _contextEventEmitter?.DecisionMade(
                layer: "layer2",
                decision: "confident",
                capabilityName: top.Name,
                sessionId: null,
                tenantId: null);

            return CapabilityRoutingResult.Confident(
                [top.Name],
                top.Confidence,
                layer: 2,
                latencyMs: latencyMs,
                selectedPlaybookId: selectedPlaybookId);
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

            // R6 Pillar 6c — context.decision_made (Layer 2 timeout).
            _contextEventEmitter?.DecisionMade(
                layer: "layer2", decision: "timeout", capabilityName: null, sessionId: null, tenantId: null);

            return null;
        }
        catch (Exception ex) when (IsRateLimitException(ex))
        {
            sw.Stop();
            _logger.LogWarning(
                "CapabilityRouter.Layer2: rate-limited (HTTP 429) — falling through to Layer 3.");
            Layer2LatencyHistogram.Record(sw.ElapsedMilliseconds);
            activity?.SetTag("rate_limited", true);

            // R6 Pillar 6c — context.decision_made (Layer 2 rate_limited).
            _contextEventEmitter?.DecisionMade(
                layer: "layer2", decision: "rate_limited", capabilityName: null, sessionId: null, tenantId: null);

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

            // R6 Pillar 6c (FR-37 / task 063) — context.decision_made (Layer 3 fallback).
            // ADR-015 audit: layer/decision are enum-like, no capabilityName (fallback path).
            _contextEventEmitter?.DecisionMade(
                layer: "layer3", decision: "fallback", capabilityName: null, sessionId: null, tenantId: null);

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
