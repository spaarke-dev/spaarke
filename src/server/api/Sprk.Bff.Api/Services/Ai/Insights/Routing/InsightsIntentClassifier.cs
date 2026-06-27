using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.Insights.Routing;

/// <summary>
/// Phase 1.5 LLM-based implementation of <see cref="IInsightsIntentClassifier"/> (Wave E2 /
/// FR-05). Wraps <see cref="IOpenAiClient"/> with a JSON-schema-constrained classification
/// prompt and an <see cref="IMemoryCache"/>-backed per-query cache to keep p95 well under
/// the 500ms FR-05 budget on cache hits.
/// </summary>
/// <remarks>
/// <para>
/// <b>Prompt design (POML Step 1)</b>: short system message describing the two routes
/// (playbook vs RAG) + a few-shot demonstration block + JSON-schema-constrained output
/// shape (<see cref="ClassificationLlmResponse"/>). The schema names every field so the
/// model cannot omit required fields; <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/>
/// guarantees valid JSON via constrained decoding.
/// </para>
/// <para>
/// <b>Caching (POML Step 3)</b>: cache key = SHA-256 of normalized (trim + lowercase, capped
/// at 1024 chars) query + optional subject scheme. Per-entry size = 1; TTL = sliding
/// <see cref="InsightsIntentClassifierOptions.CacheTtlMinutes"/> minutes. The cache is
/// process-local (matches <c>InsightsActionRouter</c>'s pattern) — distributed cache is
/// unnecessary for classification because the LLM call is already idempotent + cheap and
/// the cache only exists to absorb hot-path traffic during a single SME prompt-iteration
/// soak.
/// </para>
/// <para>
/// <b>Threshold (POML Step 4)</b>: confidence below
/// <see cref="InsightsIntentClassifierOptions.ConfidenceThreshold"/> sets
/// <see cref="IntentClassificationResult.BelowThreshold"/> true; callers MUST fall back
/// to RAG. The original path/playbook hint is preserved on the result for observability
/// (near-miss tuning).
/// </para>
/// <para>
/// <b>ADR-013 / §3.5</b>: Zone A — freely imports <see cref="IOpenAiClient"/>. Consumed
/// from Zone B via the (future Wave E3) Spaarke Assistant integration; Wave E2 plumbs
/// the wire-level <c>forceMode</c> field through the <c>/ask</c> + <c>/search</c> DTOs
/// so callers can declare intent without invoking the classifier.
/// </para>
/// </remarks>
public sealed class InsightsIntentClassifier : IInsightsIntentClassifier
{
    /// <summary>JSON schema name forwarded to the LLM for constrained decoding.</summary>
    internal const string SchemaName = "InsightsIntentClassification";

    /// <summary>
    /// Cap on the normalized query length used for cache-key derivation. Beyond this the
    /// classification is still issued (no truncation of the query sent to the LLM) but the
    /// cache key uses only the first 1024 chars — extremely long queries collide gracefully
    /// rather than blowing the memory cache's size accounting.
    /// </summary>
    internal const int CacheKeyMaxQueryLength = 1024;

    private readonly IOpenAiClient _openAi;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<InsightsIntentClassifierOptions> _options;
    private readonly ILogger<InsightsIntentClassifier> _logger;

    /// <summary>
    /// JSON schema constraining the LLM output. Five fields: <c>path</c> (enum string),
    /// <c>playbookId</c> (nullable string), <c>confidence</c> (0..1 number), <c>reason</c>
    /// (string). <c>additionalProperties=false</c> + all fields required keeps the model
    /// honest under constrained decoding.
    /// </summary>
    private static readonly BinaryData ClassificationJsonSchema = BinaryData.FromString(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["path", "playbookId", "confidence", "reason"],
          "properties": {
            "path": {
              "type": "string",
              "enum": ["playbook", "rag"],
              "description": "Routing decision: 'playbook' (pre-authored playbook synthesis) or 'rag' (open-ended retrieval)."
            },
            "playbookId": {
              "type": ["string", "null"],
              "description": "Canonical playbook name (e.g., 'predict-matter-cost@v1') when path='playbook'; null when path='rag'."
            },
            "confidence": {
              "type": "number",
              "minimum": 0.0,
              "maximum": 1.0,
              "description": "Classifier confidence in the decision, 0.0 to 1.0."
            },
            "reason": {
              "type": "string",
              "maxLength": 240,
              "description": "Brief rationale (one short sentence) for the decision."
            }
          }
        }
        """);

    public InsightsIntentClassifier(
        IOpenAiClient openAi,
        IMemoryCache cache,
        IOptionsMonitor<InsightsIntentClassifierOptions> options,
        ILogger<InsightsIntentClassifier> logger)
    {
        _openAi = openAi ?? throw new ArgumentNullException(nameof(openAi));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IntentClassificationResult> ClassifyAsync(
        string query,
        IntentClassificationContext? context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var opts = _options.CurrentValue;
        var threshold = SanitizeThreshold(opts.ConfidenceThreshold);

        // ── Cache lookup ────────────────────────────────────────────────────────────
        var cacheKey = ComputeCacheKey(query, context?.SubjectScheme);
        if (opts.CacheTtlMinutes > 0 && _cache.TryGetValue(cacheKey, out IntentClassificationResult? cached) && cached is not null)
        {
            _logger.LogDebug(
                "InsightsIntentClassifier: cache HIT for queryLen={QueryLen} subject={SubjectScheme} path={Path} confidence={Confidence}",
                query.Length, context?.SubjectScheme, cached.Path, cached.Confidence);

            // Re-emit with CacheHit=true (the stored entry was created with CacheHit=false).
            return cached with { CacheHit = true };
        }

        // ── LLM call ────────────────────────────────────────────────────────────────
        var sw = Stopwatch.StartNew();
        ClassificationLlmResponse llmResponse;
        try
        {
            var prompt = BuildPrompt(query, context);
            var rawJson = await _openAi.GetStructuredCompletionRawAsync(
                prompt: prompt,
                jsonSchema: ClassificationJsonSchema,
                schemaName: SchemaName,
                model: opts.Model,
                maxOutputTokens: opts.MaxOutputTokens,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            llmResponse = ParseLlmResponse(rawJson);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Defensive fallback per FR-05 "no false-positive playbook dispatch": when the
            // classifier itself fails (LLM error, JSON malformed despite schema, parse error),
            // emit a low-confidence RAG fallback rather than propagating an exception that
            // would 500 the caller. Observability captures the failure for SME tuning. Note:
            // FeatureDisabledException is NOT caught here — that surfaces from the Null-Object
            // path only and must propagate to the caller's catch/503-conversion site.
            sw.Stop();
            _logger.LogWarning(ex,
                "InsightsIntentClassifier: LLM call or parse failed for queryLen={QueryLen} subject={SubjectScheme} elapsedMs={ElapsedMs}; falling back to RAG with confidence=0.0.",
                query.Length, context?.SubjectScheme, sw.ElapsedMilliseconds);

            var fallback = new IntentClassificationResult(
                Path: IntentPath.Rag,
                PlaybookId: null,
                Confidence: 0.0,
                BelowThreshold: true,
                Reason: "Classifier unavailable — defaulting to RAG (Phase 1.5 safety fallback).",
                CacheHit: false);

            // Do NOT cache fallbacks — the next call should retry the classifier.
            return fallback;
        }
        sw.Stop();

        // ── Build result ────────────────────────────────────────────────────────────
        var confidence = ClampConfidence(llmResponse.Confidence);
        var path = ParsePath(llmResponse.Path);
        var belowThreshold = confidence < threshold;

        // Normalize the playbookId for the RAG path (model may have echoed it even when path=rag).
        var playbookId = path == IntentPath.Playbook
            ? NormalizePlaybookId(llmResponse.PlaybookId)
            : null;

        var result = new IntentClassificationResult(
            Path: path,
            PlaybookId: playbookId,
            Confidence: confidence,
            BelowThreshold: belowThreshold,
            Reason: llmResponse.Reason ?? string.Empty,
            CacheHit: false);

        // ── Cache write ─────────────────────────────────────────────────────────────
        if (opts.CacheTtlMinutes > 0)
        {
            using var entry = _cache.CreateEntry(cacheKey);
            entry.Value = result;
            entry.SetSlidingExpiration(TimeSpan.FromMinutes(opts.CacheTtlMinutes));
            entry.SetSize(1);
        }

        _logger.LogInformation(
            "InsightsIntentClassifier: classified queryLen={QueryLen} subject={SubjectScheme} → path={Path} playbookId={PlaybookId} confidence={Confidence:0.00} belowThreshold={BelowThreshold} elapsedMs={ElapsedMs}",
            query.Length, context?.SubjectScheme, result.Path, result.PlaybookId,
            result.Confidence, result.BelowThreshold, sw.ElapsedMilliseconds);

        return result;
    }

    /// <summary>
    /// Build the classification prompt — short system instruction + few-shot demonstrations
    /// + the live query. Kept terse on purpose: the JSON schema does most of the structural
    /// work; the few-shots cover the canonical Phase 1.5 playbook (<c>predict-matter-cost@v1</c>)
    /// and a representative RAG case per practice area.
    /// </summary>
    /// <remarks>
    /// Few-shot examples live INLINE rather than in a separate prompt file so the classifier
    /// is a single deployable unit. As more playbooks ship in Phase 2 the few-shot block
    /// can be extracted to <c>sprk_analysisaction.sprk_systemprompt</c> per the project's
    /// "no .txt prompt files; prompts live in Dataverse" principle. For Phase 1.5's
    /// one-playbook scope, inline is the correct level of complexity.
    /// </remarks>
    internal static string BuildPrompt(string query, IntentClassificationContext? context)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an intent classifier for the Spaarke Insights Engine. Examine the user's natural-language Insights query and decide which dispatch path to use.");
        sb.AppendLine();
        sb.AppendLine("Available paths:");
        sb.AppendLine("- \"playbook\": a pre-authored Spaarke playbook that produces a structured, typed answer with evidence-sufficiency gating. Choose this ONLY when the query maps cleanly to one of the registered playbook names below.");
        sb.AppendLine("- \"rag\": open-ended retrieval-augmented generation over the spaarke-insights-index. Choose this for ad-hoc factual questions, summary requests, or anything that doesn't match a registered playbook.");
        sb.AppendLine();
        sb.AppendLine("Registered playbooks (Phase 1.5):");
        sb.AppendLine("- predict-matter-cost@v1 — predicts total cost of a matter from comparable past matters; ONLY when the query is about cost prediction / cost estimate on a SPECIFIC matter subject.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Return path=\"playbook\" ONLY if the query is a clean match for a registered playbook AND the subject scheme is compatible.");
        sb.AppendLine("- Return path=\"rag\" for everything else, including ambiguous matches, similar-but-not-exact playbook intents, and open-ended questions.");
        sb.AppendLine("- confidence reflects your certainty in the decision (0.0–1.0). A confidence below 0.7 means \"not sure\"; the caller will fall back to RAG regardless.");
        sb.AppendLine("- reason: one short sentence explaining the decision.");
        sb.AppendLine();
        sb.AppendLine("Examples:");
        sb.AppendLine("Q: What will this matter cost to complete?");
        sb.AppendLine("Subject scheme: matter");
        sb.AppendLine("→ {\"path\":\"playbook\",\"playbookId\":\"predict-matter-cost@v1\",\"confidence\":0.92,\"reason\":\"Direct cost-prediction question on a matter — clean playbook match.\"}");
        sb.AppendLine();
        sb.AppendLine("Q: Summarize the key risks in the latest deal documents");
        sb.AppendLine("Subject scheme: matter");
        sb.AppendLine("→ {\"path\":\"rag\",\"playbookId\":null,\"confidence\":0.88,\"reason\":\"Open-ended summary request; no registered playbook for risk summarization.\"}");
        sb.AppendLine();
        sb.AppendLine("Q: Predict cost for this project");
        sb.AppendLine("Subject scheme: project");
        sb.AppendLine("→ {\"path\":\"rag\",\"playbookId\":null,\"confidence\":0.81,\"reason\":\"Cost-prediction intent but predict-matter-cost@v1 is matter-scoped; no project-cost playbook exists yet.\"}");
        sb.AppendLine();
        sb.AppendLine("Q: How many invoices did we issue last quarter?");
        sb.AppendLine("Subject scheme: invoice");
        sb.AppendLine("→ {\"path\":\"rag\",\"playbookId\":null,\"confidence\":0.85,\"reason\":\"Aggregate factual question — RAG retrieval over the index.\"}");
        sb.AppendLine();
        sb.Append("Q: ").AppendLine(query);
        if (!string.IsNullOrWhiteSpace(context?.SubjectScheme))
        {
            sb.Append("Subject scheme: ").AppendLine(context.SubjectScheme);
        }
        sb.AppendLine("→");

        return sb.ToString();
    }

    /// <summary>
    /// Parse the JSON-schema-constrained LLM response. The schema guarantees the structure,
    /// but we defensively check for null/empty path so a malformed response is caught and
    /// surfaced via the caller's try/catch fallback rather than producing a nonsense result.
    /// </summary>
    internal static ClassificationLlmResponse ParseLlmResponse(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw new InvalidOperationException("Classifier LLM returned empty response.");
        }

        var response = JsonSerializer.Deserialize<ClassificationLlmResponse>(rawJson, JsonOptions);
        if (response is null || string.IsNullOrWhiteSpace(response.Path))
        {
            throw new InvalidOperationException(
                "Classifier LLM response missing required 'path' field — schema constraint violated.");
        }
        return response;
    }

    /// <summary>
    /// Compute the SHA-256 cache key for a (query, subjectScheme) pair. Normalization:
    /// trim + lowercase, truncate to <see cref="CacheKeyMaxQueryLength"/> chars to bound
    /// memory accounting. SubjectScheme is included so the same NL query on matter vs
    /// project gets a different cached answer (different prompt context).
    /// </summary>
    internal static string ComputeCacheKey(string query, string? subjectScheme)
    {
        var normalized = query.Trim().ToLowerInvariant();
        if (normalized.Length > CacheKeyMaxQueryLength)
        {
            normalized = normalized[..CacheKeyMaxQueryLength];
        }

        // Include a stable separator + subject scheme. Empty scheme is fine — same key as
        // the "no subject" caller.
        var input = $"q:{normalized}|s:{(subjectScheme ?? string.Empty).ToLowerInvariant()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return "insights.intent:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Clamp the threshold to (0.0, 1.0] — defensive against misconfiguration.</summary>
    private static double SanitizeThreshold(double t)
    {
        if (double.IsNaN(t) || double.IsInfinity(t)) return 0.7;
        if (t <= 0.0) return 0.0001; // Effectively "always above threshold" — caller wants no fallback.
        if (t > 1.0) return 1.0;
        return t;
    }

    /// <summary>Clamp confidence to [0.0, 1.0] — defensive against LLM out-of-range responses.</summary>
    private static double ClampConfidence(double c)
    {
        if (double.IsNaN(c) || double.IsInfinity(c)) return 0.0;
        if (c < 0.0) return 0.0;
        if (c > 1.0) return 1.0;
        return c;
    }

    /// <summary>
    /// Parse the LLM's <c>path</c> string into <see cref="IntentPath"/>. JSON schema guarantees
    /// the enum, but we defensively default to RAG on any unrecognized value (FR-05 safety).
    /// </summary>
    private static IntentPath ParsePath(string? raw)
    {
        if (string.Equals(raw, "playbook", StringComparison.OrdinalIgnoreCase))
            return IntentPath.Playbook;
        return IntentPath.Rag;
    }

    /// <summary>
    /// Normalize the playbookId returned by the LLM. Trims whitespace; returns null for
    /// empty/whitespace. The dispatcher (future E3) resolves the canonical name → Guid
    /// via <c>InsightsPlaybookNameMapOptions</c>; this method does NOT validate against
    /// the catalog because the catalog binding lives outside Zone A.
    /// </summary>
    private static string? NormalizePlaybookId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim();
    }

    /// <summary>
    /// JSON serializer options for parsing the constrained LLM response. Case-insensitive
    /// property matching is a minor safety net even though the schema fixes the casing.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>
    /// Raw LLM response shape (matches <see cref="ClassificationJsonSchema"/>). Internal —
    /// transformed into <see cref="IntentClassificationResult"/> before returning to the
    /// caller.
    /// </summary>
    internal sealed record ClassificationLlmResponse(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("playbookId")] string? PlaybookId,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("reason")] string? Reason);
}
