using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// LLM-assisted risk identification handler with code-based deterministic severity scoring
/// (R6 Pillar 2, FR-15, Wave 2).
/// </summary>
/// <remarks>
/// <para>
/// Pipeline: input text → per-tenant cache check → LLM structured-output call
/// (<see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/>; constrained JSON schema)
/// → deterministic code-side severity scoring (category-weight × LLM confidence → bucket)
/// → structured <see cref="ToolResult"/> with risks + severity distribution stats.
/// </para>
/// <para>
/// <strong>FR-15 binding — Hybrid LLM identification + deterministic scoring</strong>: The LLM
/// emits risks with raw confidence values. The handler then computes the FINAL severity bucket
/// using a deterministic, configurable rubric:
/// <c>finalScore = categoryWeight × llmConfidence</c>, then bucketed into
/// <c>low (&lt;0.25), medium (&lt;0.5), high (&lt;0.75), critical (≥0.75)</c>.
/// This guarantees that legal-review-relevant severities are reproducible + auditable across
/// LLM model upgrades — the LLM does NOT pick the final severity bucket directly.
/// </para>
/// <para>
/// <strong>Configuration (sprk_analysistool.sprk_configuration JSON)</strong>:
/// </para>
/// <list type="bullet">
/// <item><c>riskCategories</c> (string[], optional): subset filter — e.g.,
/// <c>["legal","financial","compliance","data-privacy","operational","reputational","contract"]</c>.
/// Default = all supported categories.</item>
/// <item><c>severityLevels</c> (string[], optional): allow-list of severity buckets that may
/// appear in the output. Default = <c>["low","medium","high","critical"]</c>. Restricting (e.g.,
/// to <c>["high","critical"]</c>) filters out risks below the threshold.</item>
/// <item><c>categoryWeights</c> (object {string→number 0.0–1.0}, optional): per-category
/// severity-scoring weight overrides. Defaults: legal=1.0, financial=0.9, compliance=0.95,
/// data-privacy=1.0, contract=0.8, operational=0.7, reputational=0.7.</item>
/// <item><c>confidenceThreshold</c> (number, optional, 0.0–1.0): minimum LLM confidence to
/// include in results. Defaults to 0.6.</item>
/// <item><c>modelDeployment</c> (string, optional): per-row model override; else
/// <see cref="ModelSelectorOptions.ToolHandlerModel"/>.</item>
/// </list>
/// <para>
/// <strong>Invocation contexts</strong>: Supports <see cref="InvocationContextKind.Both"/>
/// (Playbook + Chat). Playbook reads input from <see cref="ToolExecutionContext.Document"/>'s
/// extracted text; chat reads input from <c>text</c> property of
/// <see cref="ChatInvocationContext.ToolArgumentsJson"/>.
/// </para>
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered via assembly scan; ZERO manual DI line.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; CRUD-side code
/// routes through <c>PublicContracts</c> facades, never directly into this handler.</item>
/// <item><strong>ADR-014</strong>: per-tenant cache key
/// <c>risk-detector:{tenantId}:{sha256(text+riskCategories+severityLevels+weights+threshold)}</c>.
/// NEVER cross-tenant.</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + outcome + risk-count bucket +
/// severity-distribution bucket + duration ONLY. NEVER input text, risk DESCRIPTIONS, or
/// evidence-span CONTENT. Risk-category names + severity-bucket names are deterministic
/// identifiers, safe to log.</item>
/// <item><strong>ADR-016</strong>: LLM call goes through <see cref="IOpenAiClient"/> which
/// applies the per-tenant <c>ai-context</c> rate limit. No bypass.</item>
/// <item><strong>ADR-029</strong>: implementation uses BCL + existing in-app dependencies;
/// per-handler publish-size delta ≤+0.6 MB target.</item>
/// </list>
/// <para>
/// <strong>NFR-13 safety pipeline</strong>: LLM calls flow through existing
/// <c>SafetyPipelineMiddleware</c> chain (PromptShield + Groundedness + Citations + Privilege
/// + Cross-matter). This handler does NOT bypass the middleware — it calls
/// <see cref="IOpenAiClient"/> like every other LLM-assisted handler.
/// </para>
/// </remarks>
public sealed class RiskDetectorHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(RiskDetectorHandler);
    // ITenantCache resource name (FR-05 redis remediation r1). Final on-wire key:
    // spaarke:tenant:{tenantId}:risk-detector:{hash}:v1
    private const string CacheResource = "risk-detector";
    private const int CacheVersion = 1;
    private const string SchemaName = "RiskDetectionResult";
    private const double DefaultConfidenceThreshold = 0.6;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Canonical risk categories this handler supports. Output risks are normalized to
    /// lowercase to match. The LLM schema's <c>category</c> enum is built from this set.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedRiskCategories = new[]
    {
        "legal", "financial", "operational", "reputational", "compliance", "data-privacy", "contract"
    };

    private static readonly HashSet<string> SupportedRiskCategorySet = new(
        SupportedRiskCategories, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Canonical severity-bucket order, lowest first.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedSeverityLevels = new[]
    {
        "low", "medium", "high", "critical"
    };

    private static readonly HashSet<string> SupportedSeveritySet = new(
        SupportedSeverityLevels, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Default per-category severity-scoring weights. Weight ∈ [0.0, 1.0]; higher weight
    /// pushes the score (and therefore the severity bucket) upward for that category.
    /// Defaults reflect legal-domain priorities — data-privacy + legal at top, operational
    /// and reputational lower.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> DefaultCategoryWeights =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["legal"] = 1.0,
            ["financial"] = 0.9,
            ["operational"] = 0.7,
            ["reputational"] = 0.7,
            ["compliance"] = 0.95,
            ["data-privacy"] = 1.0,
            ["contract"] = 0.8
        };

    private readonly IOpenAiClient _openAiClient;
    private readonly ITenantCache _cache;
    private readonly ModelSelectorOptions _modelSelectorOptions;
    private readonly ILogger<RiskDetectorHandler> _logger;

    public RiskDetectorHandler(
        IOpenAiClient openAiClient,
        ITenantCache cache,
        IOptions<ModelSelectorOptions> modelSelectorOptions,
        ILogger<RiskDetectorHandler> logger)
    {
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _modelSelectorOptions = modelSelectorOptions?.Value
            ?? throw new ArgumentNullException(nameof(modelSelectorOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Risk Detector",
        Description: "LLM-assisted risk identification with deterministic code-based severity " +
                     "scoring. Identifies legal, financial, operational, reputational, compliance, " +
                     "data-privacy, and contract risks. The LLM identifies the risks + raw confidence; " +
                     "code assigns the final severity bucket (low/medium/high/critical) using a " +
                     "configurable per-category weight × confidence formula. Severity assignment is " +
                     "deterministic — same LLM output + same config → same severity bucket.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition("text", "Input text to scan for risks (contract section, email thread, transaction summary).", ToolParameterType.String, Required: true),
            new ToolParameterDefinition("riskCategories", "Optional subset of risk categories to detect. Defaults to all supported categories.", ToolParameterType.Array, Required: false),
            new ToolParameterDefinition("severityLevels", "Optional allow-list of severity buckets in the output. Defaults to all (low, medium, high, critical).", ToolParameterType.Array, Required: false),
            new ToolParameterDefinition("confidenceThreshold", "Minimum LLM confidence to include in results (0.0-1.0). Defaults to 0.6.", ToolParameterType.Decimal, Required: false, DefaultValue: DefaultConfidenceThreshold)
        },
        ConfigurationSchema: ConfigSchema);

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.RiskDetector };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Both;

    /// <summary>
    /// JSON Schema (Draft 07) describing the handler's configuration JSON (stored in
    /// <c>sprk_analysistool.sprk_configuration</c>).
    /// </summary>
    private static readonly object ConfigSchema = new
    {
        schema = "https://json-schema.org/draft-07/schema#",
        title = "Risk Detector Handler Configuration",
        type = "object",
        properties = new
        {
            riskCategories = new
            {
                type = "array",
                items = new
                {
                    type = "string",
                    @enum = SupportedRiskCategories.ToArray()
                },
                description = "Subset of risk categories to detect. When omitted, all supported categories are detected."
            },
            severityLevels = new
            {
                type = "array",
                items = new
                {
                    type = "string",
                    @enum = SupportedSeverityLevels.ToArray()
                },
                description = "Allow-list of severity buckets in the output. When omitted, all four buckets are emitted."
            },
            categoryWeights = new
            {
                type = "object",
                description = "Per-category severity-scoring weight override. Each weight ∈ [0.0, 1.0]; multiplies LLM confidence to produce the final score that selects the severity bucket. When omitted, defaults to the canonical legal-domain weights (legal/data-privacy=1.0, compliance=0.95, financial=0.9, contract=0.8, operational/reputational=0.7)."
            },
            confidenceThreshold = new
            {
                type = "number",
                minimum = 0.0,
                maximum = 1.0,
                @default = DefaultConfidenceThreshold,
                description = "Minimum LLM-reported confidence (0.0-1.0) to include in results."
            },
            modelDeployment = new
            {
                type = "string",
                description = "Optional Azure OpenAI deployment override. When null, uses ModelSelectorOptions.ToolHandlerModel."
            }
        },
        required = Array.Empty<string>()
    };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        if (context.Document is null)
            return ToolValidationResult.Failure("Document context is required.");

        if (string.IsNullOrWhiteSpace(context.Document.ExtractedText))
            return ToolValidationResult.Failure("Document extracted text is required.");

        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required (ADR-014 cache-key invariant).");

        return ValidateConfiguration(tool.Configuration);
    }

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required (ADR-014 cache-key invariant).");

        if (string.IsNullOrWhiteSpace(context.ToolArgumentsJson))
            return ToolValidationResult.Failure("Tool arguments JSON is required for chat invocation.");

        try
        {
            using var doc = JsonDocument.Parse(context.ToolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ToolValidationResult.Failure("Tool arguments must be a JSON object.");

            if (!doc.RootElement.TryGetProperty("text", out var textProp) ||
                textProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(textProp.GetString()))
            {
                return ToolValidationResult.Failure("Tool arguments must include a non-empty 'text' string field.");
            }
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Failure($"Tool arguments JSON is malformed: {ex.Message}");
        }

        return ValidateConfiguration(tool.Configuration);
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            // ADR-015: log handler name + IDs + outcome only — never the input text body
            _logger.LogInformation(
                "RiskDetectorHandler executing for analysis {AnalysisId}, tool {ToolId}",
                context.AnalysisId, tool.Id);

            cancellationToken.ThrowIfCancellationRequested();

            var config = ParseConfiguration(tool.Configuration);
            var inputText = context.Document!.ExtractedText ?? string.Empty;

            return await DetectAndReturnAsync(
                inputText,
                tool,
                config,
                tenantId: context.TenantId,
                temperature: (float)context.Temperature,
                startedAt: startedAt,
                stopwatch: stopwatch,
                correlationLogId: context.AnalysisId.ToString(),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "RiskDetectorHandler cancelled for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Risk detection was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            // ADR-015: log only the exception type — no input/response echo
            _logger.LogError(
                ex,
                "RiskDetectorHandler failed for analysis {AnalysisId}: {ErrorType}",
                context.AnalysisId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Risk detection failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            _logger.LogInformation(
                "RiskDetectorHandler chat-invocation for session {ChatSessionId}, decision {DecisionId}",
                context.ChatSessionId, context.DecisionId);

            cancellationToken.ThrowIfCancellationRequested();

            var config = ParseConfiguration(tool.Configuration);

            string inputText;
            using (var doc = JsonDocument.Parse(context.ToolArgumentsJson ?? "{}"))
            {
                inputText = doc.RootElement.TryGetProperty("text", out var textProp)
                    ? textProp.GetString() ?? string.Empty
                    : string.Empty;
            }

            return await DetectAndReturnAsync(
                inputText,
                tool,
                config,
                tenantId: context.TenantId,
                temperature: (float)context.Temperature,
                startedAt: startedAt,
                stopwatch: stopwatch,
                correlationLogId: $"session={context.ChatSessionId},decision={context.DecisionId}",
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "RiskDetectorHandler chat cancelled for session {ChatSessionId}, decision {DecisionId}",
                context.ChatSessionId, context.DecisionId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Risk detection was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "RiskDetectorHandler chat failed for session {ChatSessionId}, decision {DecisionId}: {ErrorType}",
                context.ChatSessionId, context.DecisionId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Risk detection failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Core detection pipeline (cache → LLM → deterministic scoring)
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> DetectAndReturnAsync(
        string inputText,
        AnalysisTool tool,
        RiskDetectorConfig config,
        string tenantId,
        float temperature,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        var effectiveCategories = ResolveEffectiveCategories(config);
        var effectiveSeverities = ResolveEffectiveSeverities(config);
        var effectiveWeights = ResolveEffectiveWeights(config);
        var confidenceThreshold = config.ConfidenceThreshold ?? DefaultConfidenceThreshold;

        // ADR-014 + FR-05: per-tenant cache id — ITenantCache wraps tenant scoping.
        var cacheId = BuildCacheId(
            inputText, effectiveCategories, effectiveSeverities,
            effectiveWeights, confidenceThreshold);

        // Cache lookup (best-effort — failures fall through to LLM call)
        var cached = await TryGetFromCacheAsync(tenantId, cacheId, cancellationToken);
        if (cached is not null)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "RiskDetectorHandler cache hit ({Correlation}): {RiskCount} risks, " +
                "{CountBucket} count-bucket, {SeverityBucket} severity-bucket in {Duration}ms",
                correlationLogId,
                cached.Stats.TotalCount,
                BucketCount(cached.Stats.TotalCount),
                BucketSeverityDistribution(cached.Stats.BySeverity),
                stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                data: cached,
                summary: $"Identified {cached.Stats.TotalCount} risk(s) (cache hit).",
                confidence: cached.Stats.TotalCount == 0 ? 0.0 : 1.0,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    CacheHit = true,
                    ModelCalls = 0,
                    ModelName = null
                });
        }

        // LLM call — Azure OpenAI structured outputs (json_schema constrained)
        var schemaBinaryData = BinaryData.FromString(BuildLlmOutputSchema(effectiveCategories));
        var actionModel = config.ModelDeployment ?? _modelSelectorOptions.ToolHandlerModel;
        var prompt = BuildPrompt(inputText, effectiveCategories, confidenceThreshold);

        var rawJson = await _openAiClient.GetStructuredCompletionRawAsync(
            prompt,
            schemaBinaryData,
            SchemaName,
            model: actionModel,
            maxOutputTokens: null,
            temperature: temperature,
            cancellationToken: cancellationToken);

        // Parse LLM response — schema-constrained, but defensively handle parse error
        List<RawLlmRisk> rawRisks;
        try
        {
            rawRisks = ParseLlmResponse(rawJson);
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "RiskDetectorHandler LLM returned malformed JSON ({Correlation}): {ErrorType} in {Duration}ms",
                correlationLogId, ex.GetType().Name, stopwatch.ElapsedMilliseconds);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Risk detection failed: LLM response was not valid JSON.",
                ToolErrorCodes.ModelError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ModelCalls = 1,
                    ModelName = actionModel
                });
        }

        // Code-side deterministic severity scoring + category/severity filtering + dedupe.
        // This is the FR-15 binding step — LLM identifies risks; code assigns severity.
        var scoredRisks = ScoreAndFilterRisks(
            rawRisks, inputText, effectiveCategories, effectiveSeverities,
            effectiveWeights, confidenceThreshold);

        var stats = BuildStats(scoredRisks);
        var result = new RiskDetectionResult
        {
            Risks = scoredRisks,
            Stats = stats
        };

        // Cache store (best-effort)
        await TrySetCacheAsync(tenantId, cacheId, result, cancellationToken);

        stopwatch.Stop();

        var executionMetadata = new ToolExecutionMetadata
        {
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            CacheHit = false,
            ModelCalls = 1,
            ModelName = actionModel,
            InputTokens = EstimateTokens(prompt),
            OutputTokens = EstimateTokens(rawJson)
        };

        // ADR-015: log counts + severity-distribution bucket only — NEVER risk descriptions
        _logger.LogInformation(
            "RiskDetectorHandler complete ({Correlation}): {RiskCount} risks, " +
            "{CountBucket} count-bucket, {SeverityBucket} severity-bucket, {CategoryCount} category(ies), " +
            "in {Duration}ms",
            correlationLogId,
            stats.TotalCount,
            BucketCount(stats.TotalCount),
            BucketSeverityDistribution(stats.BySeverity),
            stats.ByCategory.Count,
            stopwatch.ElapsedMilliseconds);

        return ToolResult.Ok(
            HandlerId,
            tool.Id,
            tool.Name,
            data: result,
            summary: $"Identified {stats.TotalCount} risk(s) across {stats.ByCategory.Count} category(ies).",
            confidence: scoredRisks.Count == 0 ? 0.0 : scoredRisks.Average(r => r.Confidence),
            execution: executionMetadata);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Configuration parsing + validation
    // ─────────────────────────────────────────────────────────────────────────────

    private static ToolValidationResult ValidateConfiguration(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return ToolValidationResult.Success(); // configuration is optional

        try
        {
            var config = JsonSerializer.Deserialize<RiskDetectorConfig>(configurationJson, JsonOpts);
            if (config is null)
                return ToolValidationResult.Failure("Invalid configuration JSON.");

            var errors = new List<string>();

            if (config.RiskCategories is not null)
            {
                foreach (var c in config.RiskCategories)
                {
                    if (string.IsNullOrWhiteSpace(c))
                    {
                        errors.Add("riskCategories contains an empty value.");
                        continue;
                    }
                    if (!SupportedRiskCategorySet.Contains(c))
                    {
                        errors.Add($"riskCategories '{c}' is not supported. Supported: {string.Join(", ", SupportedRiskCategories)}.");
                    }
                }
            }

            if (config.SeverityLevels is not null)
            {
                foreach (var s in config.SeverityLevels)
                {
                    if (string.IsNullOrWhiteSpace(s))
                    {
                        errors.Add("severityLevels contains an empty value.");
                        continue;
                    }
                    if (!SupportedSeveritySet.Contains(s))
                    {
                        errors.Add($"severityLevels '{s}' is not supported. Supported: {string.Join(", ", SupportedSeverityLevels)}.");
                    }
                }
            }

            if (config.ConfidenceThreshold is < 0.0 or > 1.0)
                errors.Add($"confidenceThreshold {config.ConfidenceThreshold} must be between 0.0 and 1.0.");

            if (config.CategoryWeights is not null)
            {
                foreach (var (cat, weight) in config.CategoryWeights)
                {
                    if (!SupportedRiskCategorySet.Contains(cat))
                    {
                        errors.Add($"categoryWeights key '{cat}' is not a supported risk category.");
                    }
                    if (weight is < 0.0 or > 1.0)
                    {
                        errors.Add($"categoryWeights['{cat}'] = {weight} must be between 0.0 and 1.0.");
                    }
                }
            }

            return errors.Count == 0
                ? ToolValidationResult.Success()
                : ToolValidationResult.Failure(errors);
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Failure($"Configuration JSON parse error: {ex.Message}");
        }
    }

    private static RiskDetectorConfig ParseConfiguration(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return new RiskDetectorConfig();

        try
        {
            return JsonSerializer.Deserialize<RiskDetectorConfig>(configurationJson, JsonOpts)
                   ?? new RiskDetectorConfig();
        }
        catch (JsonException)
        {
            return new RiskDetectorConfig();
        }
    }

    private static IReadOnlyList<string> ResolveEffectiveCategories(RiskDetectorConfig config)
    {
        if (config.RiskCategories is null || config.RiskCategories.Count == 0)
            return SupportedRiskCategories;

        var filtered = config.RiskCategories
            .Where(t => !string.IsNullOrWhiteSpace(t) && SupportedRiskCategorySet.Contains(t))
            .Select(t => t.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        return filtered.Count == 0 ? SupportedRiskCategories : filtered;
    }

    private static IReadOnlyList<string> ResolveEffectiveSeverities(RiskDetectorConfig config)
    {
        if (config.SeverityLevels is null || config.SeverityLevels.Count == 0)
            return SupportedSeverityLevels;

        var filtered = config.SeverityLevels
            .Where(t => !string.IsNullOrWhiteSpace(t) && SupportedSeveritySet.Contains(t))
            .Select(t => t.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => SeverityRank(t))
            .ToList();

        return filtered.Count == 0 ? SupportedSeverityLevels : filtered;
    }

    /// <summary>
    /// Build the effective per-category weight table by overlaying caller-supplied overrides
    /// onto the defaults. Deterministic ordering — iteration over the dictionary is stable
    /// across runs because we hash-key on the canonical lowercased category and the cache-key
    /// builder iterates in sorted order.
    /// </summary>
    private static IReadOnlyDictionary<string, double> ResolveEffectiveWeights(RiskDetectorConfig config)
    {
        if (config.CategoryWeights is null || config.CategoryWeights.Count == 0)
            return DefaultCategoryWeights;

        var weights = new Dictionary<string, double>(DefaultCategoryWeights, StringComparer.OrdinalIgnoreCase);
        foreach (var (cat, weight) in config.CategoryWeights)
        {
            if (!SupportedRiskCategorySet.Contains(cat)) continue;
            if (weight is < 0.0 or > 1.0) continue;
            weights[cat.ToLowerInvariant()] = weight;
        }
        return weights;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-014: per-tenant cache key
    // ─────────────────────────────────────────────────────────────────────────────

    // Builds the cache "id" component (content hash) consumed by ITenantCache.
    private static string BuildCacheId(
        string inputText,
        IReadOnlyList<string> categories,
        IReadOnlyList<string> severities,
        IReadOnlyDictionary<string, double> weights,
        double confidenceThreshold)
    {
        var categoriesPart = string.Join(",", categories.OrderBy(c => c, StringComparer.Ordinal));
        var severitiesPart = string.Join(",", severities.OrderBy(SeverityRank));
        var weightsPart = string.Join(",", weights
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value.ToString("F4", CultureInfo.InvariantCulture)}"));
        var rawComposite =
            $"{inputText}|cats={categoriesPart}|sevs={severitiesPart}|" +
            $"weights={weightsPart}|threshold={confidenceThreshold.ToString("F2", CultureInfo.InvariantCulture)}";

        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(rawComposite));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private async Task<RiskDetectionResult?> TryGetFromCacheAsync(
        string tenantId, string cacheId, CancellationToken cancellationToken)
    {
        try
        {
            return await _cache.GetAsync<RiskDetectionResult>(
                tenantId, CacheResource, cacheId, CacheVersion, ct: cancellationToken);
        }
        catch (Exception ex)
        {
            // Cache failures must NEVER block detection — log + fall through to LLM
            _logger.LogWarning(
                "RiskDetectorHandler cache read failed: {ErrorType}", ex.GetType().Name);
            return null;
        }
    }

    private async Task TrySetCacheAsync(
        string tenantId, string cacheId, RiskDetectionResult value, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SetAsync(
                tenantId, CacheResource, cacheId, CacheVersion, value, CacheTtl, ct: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "RiskDetectorHandler cache write failed: {ErrorType}", ex.GetType().Name);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // LLM prompt + schema
    // ─────────────────────────────────────────────────────────────────────────────

    private static string BuildPrompt(
        string inputText, IReadOnlyList<string> effectiveCategories, double confidenceThreshold)
    {
        var categoriesList = string.Join(", ", effectiveCategories);
        return $$"""
            You are an expert legal-risk-review assistant. Identify risks in the following input
            text. Only emit risks falling into these categories: {{categoriesList}}.

            For each risk:
            - Set "description" to a concise 1-2 sentence summary of the risk (do NOT quote the
              source verbatim; paraphrase).
            - Set "category" to one of: {{categoriesList}}.
            - Set "confidence" to a value in [0.0, 1.0] reflecting your certainty THAT this is
              a genuine risk. Do NOT pick a final severity bucket — that is computed downstream
              from your confidence + category weights.
            - Set "evidenceSpan.start" and "evidenceSpan.end" to the half-open character offsets
              [start, end) into the input text where the supporting evidence appears.

            Include only risks with confidence >= {{confidenceThreshold.ToString("F2", CultureInfo.InvariantCulture)}}.
            Do NOT emit duplicates. Do NOT emit risks of unsupported categories. Do NOT include
            severity in your output — the consumer assigns severity deterministically.

            Input text:
            ----- BEGIN -----
            {{inputText}}
            ----- END -----
            """;
    }

    private static string BuildLlmOutputSchema(IReadOnlyList<string> effectiveCategories)
    {
        // Structured-output schema constraining the LLM's response shape.
        // Note: severity is intentionally NOT in the schema — code assigns it deterministically.
        var categoriesArray = string.Join(",", effectiveCategories.Select(c => $"\"{c}\""));
        return $$"""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "title": "RiskDetectionResult",
              "type": "object",
              "required": ["risks"],
              "additionalProperties": false,
              "properties": {
                "risks": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "required": ["description", "category", "confidence", "evidenceSpan"],
                    "additionalProperties": false,
                    "properties": {
                      "description": { "type": "string", "minLength": 1 },
                      "category": { "type": "string", "enum": [{{categoriesArray}}] },
                      "confidence": { "type": "number", "minimum": 0.0, "maximum": 1.0 },
                      "evidenceSpan": {
                        "type": "object",
                        "required": ["start", "end"],
                        "additionalProperties": false,
                        "properties": {
                          "start": { "type": "integer", "minimum": 0 },
                          "end":   { "type": "integer", "minimum": 0 }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // LLM response parsing
    // ─────────────────────────────────────────────────────────────────────────────

    private static List<RawLlmRisk> ParseLlmResponse(string rawJson)
    {
        var doc = JsonNode.Parse(rawJson);
        if (doc is not JsonObject root)
            throw new JsonException("LLM response root was not a JSON object.");

        if (!root.TryGetPropertyValue("risks", out var risksNode) || risksNode is not JsonArray risks)
            return new List<RawLlmRisk>();

        var result = new List<RawLlmRisk>(risks.Count);
        foreach (var node in risks)
        {
            if (node is not JsonObject ro) continue;
            var raw = ro.Deserialize<RawLlmRisk>(JsonOpts);
            if (raw is null) continue;
            result.Add(raw);
        }
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FR-15 binding — deterministic code-side severity scoring + filtering + dedupe
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply the deterministic severity-scoring pipeline:
    /// 1. Drop risks with category not in the configured allow-list.
    /// 2. Drop risks with confidence below the threshold.
    /// 3. Compute final score = categoryWeight × confidence.
    /// 4. Bucket score into low (&lt;0.25), medium (&lt;0.5), high (&lt;0.75), critical (≥0.75).
    /// 5. Drop risks whose final severity is not in the configured severityLevels allow-list.
    /// 6. Dedupe by (category, normalized description, span).
    /// 7. Return in deterministic order (descending severity rank, then start span).
    /// </summary>
    private static List<ScoredRisk> ScoreAndFilterRisks(
        IReadOnlyList<RawLlmRisk> rawRisks,
        string inputText,
        IReadOnlyList<string> effectiveCategories,
        IReadOnlyList<string> effectiveSeverities,
        IReadOnlyDictionary<string, double> effectiveWeights,
        double confidenceThreshold)
    {
        var effectiveCategorySet = new HashSet<string>(effectiveCategories, StringComparer.OrdinalIgnoreCase);
        var effectiveSeveritySet = new HashSet<string>(effectiveSeverities, StringComparer.OrdinalIgnoreCase);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal); // dedupe key: category|normalizedDesc|start
        var results = new List<ScoredRisk>(rawRisks.Count);

        foreach (var raw in rawRisks)
        {
            if (raw is null) continue;
            if (string.IsNullOrWhiteSpace(raw.Description)) continue;
            if (string.IsNullOrWhiteSpace(raw.Category)) continue;

            var category = raw.Category.Trim().ToLowerInvariant();

            // Filter: category allowed?
            if (!effectiveCategorySet.Contains(category)) continue;

            // Filter: confidence threshold
            if (raw.Confidence < confidenceThreshold) continue;

            // Span sanity check (clamp to input bounds)
            var (startIdx, endIdx) = ClampSpan(raw.EvidenceSpan, inputText.Length);

            // Code-side deterministic severity scoring (FR-15 binding).
            var weight = effectiveWeights.TryGetValue(category, out var w) ? w : 0.5;
            var clampedConfidence = Math.Clamp(raw.Confidence, 0.0, 1.0);
            var finalScore = Math.Clamp(weight * clampedConfidence, 0.0, 1.0);
            var severity = BucketSeverity(finalScore);

            // Filter: severity allow-list
            if (!effectiveSeveritySet.Contains(severity)) continue;

            var normalizedDesc = raw.Description.Trim();
            var dedupeKey = $"{category}|{normalizedDesc}|{startIdx}";
            if (!seenKeys.Add(dedupeKey)) continue;

            results.Add(new ScoredRisk
            {
                Category = category,
                Description = normalizedDesc,
                EvidenceSpan = new RiskSpan { Start = startIdx, End = endIdx },
                Severity = severity,
                Score = finalScore,
                Confidence = clampedConfidence
            });
        }

        // Deterministic ordering: severity descending (critical → low), then start span ascending,
        // then category ordinal — guarantees byte-identical output across runs with identical input.
        return results
            .OrderByDescending(r => SeverityRank(r.Severity))
            .ThenBy(r => r.EvidenceSpan.Start)
            .ThenBy(r => r.Category, StringComparer.Ordinal)
            .ThenBy(r => r.Description, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// FR-15 binding — deterministic severity-bucket function.
    /// </summary>
    /// <remarks>
    /// Bucket boundaries are chosen so that:
    /// <list type="bullet">
    /// <item>critical (≥0.75): only categories with weight ≥0.75 AND high LLM confidence reach this bucket.</item>
    /// <item>high (0.5–0.75): meaningful concerns for legal review.</item>
    /// <item>medium (0.25–0.5): worth surfacing; lower urgency.</item>
    /// <item>low (&lt;0.25): minor noise; usually filtered out by callers.</item>
    /// </list>
    /// </remarks>
    internal static string BucketSeverity(double score) => score switch
    {
        >= 0.75 => "critical",
        >= 0.50 => "high",
        >= 0.25 => "medium",
        _ => "low"
    };

    /// <summary>
    /// Rank ordering used by both the cache-key builder and the deterministic output sort
    /// so severity ordering is identical wherever it appears.
    /// </summary>
    internal static int SeverityRank(string severity) => severity?.ToLowerInvariant() switch
    {
        "critical" => 4,
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0
    };

    private static (int Start, int End) ClampSpan(RiskSpan? span, int inputLength)
    {
        var start = Math.Clamp(span?.Start ?? 0, 0, inputLength);
        var end = Math.Clamp(span?.End ?? start, start, inputLength);
        return (start, end);
    }

    private static RiskStats BuildStats(IReadOnlyList<ScoredRisk> risks)
    {
        var byCategory = risks
            .GroupBy(r => r.Category, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var bySeverity = risks
            .GroupBy(r => r.Severity, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return new RiskStats
        {
            TotalCount = risks.Count,
            ByCategory = byCategory,
            BySeverity = bySeverity
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-015 telemetry helpers — bucket counts so deterministic IDs don't leak ranges
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bucket risk counts to "0", "1", "2-5", "6-20", "21+" so telemetry conveys volume
    /// without leaking exact counts that could be correlated with input content (ADR-015).
    /// </summary>
    private static string BucketCount(int count) => count switch
    {
        0 => "0",
        1 => "1",
        <= 5 => "2-5",
        <= 20 => "6-20",
        _ => "21+"
    };

    /// <summary>
    /// Render the severity distribution as a deterministic string like
    /// "critical=2,high=1,medium=0,low=0". Safe to log (counts only, no content; severity
    /// bucket names are deterministic identifiers).
    /// </summary>
    private static string BucketSeverityDistribution(IReadOnlyDictionary<string, int> bySeverity)
    {
        // Emit in fixed canonical order so log output is stable.
        var sb = new StringBuilder();
        var first = true;
        foreach (var sev in SupportedSeverityLevels)
        {
            if (!first) sb.Append(',');
            first = false;
            var count = bySeverity.TryGetValue(sev, out var n) ? n : 0;
            sb.Append(sev).Append('=').Append(count);
        }
        return sb.ToString();
    }

    private static int EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ─────────────────────────────────────────────────────────────────────────────
    // DTOs — config + raw LLM shape + public output shape
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Configuration JSON shape stored in <c>sprk_analysistool.sprk_configuration</c>.
    /// </summary>
    internal sealed class RiskDetectorConfig
    {
        [JsonPropertyName("riskCategories")]
        public List<string>? RiskCategories { get; set; }

        [JsonPropertyName("severityLevels")]
        public List<string>? SeverityLevels { get; set; }

        [JsonPropertyName("categoryWeights")]
        public Dictionary<string, double>? CategoryWeights { get; set; }

        [JsonPropertyName("confidenceThreshold")]
        public double? ConfidenceThreshold { get; set; }

        [JsonPropertyName("modelDeployment")]
        public string? ModelDeployment { get; set; }
    }

    /// <summary>
    /// Raw single risk as the LLM emits it (pre-scoring).
    /// </summary>
    internal sealed class RawLlmRisk
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("evidenceSpan")]
        public RiskSpan? EvidenceSpan { get; set; }
    }

    /// <summary>
    /// Structured output: list of scored risks + summary stats. Returned in
    /// <see cref="ToolResult.Data"/>.
    /// </summary>
    public sealed class RiskDetectionResult
    {
        [JsonPropertyName("risks")]
        public IReadOnlyList<ScoredRisk> Risks { get; set; } = Array.Empty<ScoredRisk>();

        [JsonPropertyName("stats")]
        public RiskStats Stats { get; set; } = new();
    }

    /// <summary>
    /// Single scored risk in the structured output. <c>Severity</c> is assigned
    /// deterministically by the handler — NOT by the LLM.
    /// </summary>
    public sealed class ScoredRisk
    {
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("evidenceSpan")]
        public RiskSpan EvidenceSpan { get; set; } = new();

        /// <summary>Final severity bucket: low / medium / high / critical (assigned by code, not LLM).</summary>
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = string.Empty;

        /// <summary>Final score: categoryWeight × LLM confidence, clamped to [0.0, 1.0].</summary>
        [JsonPropertyName("score")]
        public double Score { get; set; }

        /// <summary>Original LLM-emitted confidence value (pre-scoring), clamped to [0.0, 1.0].</summary>
        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }
    }

    /// <summary>Half-open character span [start, end) in the input text.</summary>
    public sealed class RiskSpan
    {
        [JsonPropertyName("start")]
        public int Start { get; set; }

        [JsonPropertyName("end")]
        public int End { get; set; }
    }

    /// <summary>Aggregate counts over the scored risk set.</summary>
    public sealed class RiskStats
    {
        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }

        [JsonPropertyName("byCategory")]
        public Dictionary<string, int> ByCategory { get; set; } = new(StringComparer.Ordinal);

        [JsonPropertyName("bySeverity")]
        public Dictionary<string, int> BySeverity { get; set; } = new(StringComparer.Ordinal);
    }
}
