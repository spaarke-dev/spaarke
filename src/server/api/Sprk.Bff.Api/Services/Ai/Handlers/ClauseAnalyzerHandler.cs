using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// LLM-assisted contract clause structuring handler with structural span validation
/// (R6 Pillar 2, FR-14, Wave 2).
/// </summary>
/// <remarks>
/// <para>
/// Pipeline: input contract text → per-tenant cache check → LLM structured-output call
/// (constrained JSON schema; <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/>) →
/// deterministic post-processing (validate types against allow-list, validate non-overlapping
/// spans, clamp to input bounds, dedupe) → structured <see cref="ToolResult"/> with the
/// per-clause structured representation plus a list of unresolved (overlapping / out-of-bounds)
/// spans.
/// </para>
/// <para>
/// <strong>FR-14 binding</strong>: LLM-assisted (not pure deterministic). Distinct from
/// <c>ClauseComparisonHandler</c> (task 103, FR-16): comparison DIFFS two clauses
/// deterministically; this handler STRUCTURES a single contract text into typed clauses.
/// Configurable via <c>sprk_analysistool.sprk_configuration</c> JSON:
/// </para>
/// <list type="bullet">
/// <item><c>clauseTypes</c> (string[], optional): subset filter — e.g.,
/// <c>["indemnity","liability","termination","payment","ip-assignment","confidentiality","governing-law"]</c>.
/// Default = all supported types.</item>
/// <item><c>options.extractObligations</c> (bool, optional): emit per-clause obligation
/// bullets. Defaults to <c>false</c>.</item>
/// <item><c>options.extractParties</c> (bool, optional): emit per-clause party list.
/// Defaults to <c>false</c>.</item>
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
/// <c>clause-analyzer:{tenantId}:{sha256(text+clauseTypes+options)}</c>. NEVER cross-tenant.</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + outcome + per-type bucket
/// counts + duration ONLY. NEVER clause TEXT, structured-field VALUES, party NAMES, or raw
/// LLM responses. Clause TYPE counts are deterministic identifiers, safe to log.</item>
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
public sealed class ClauseAnalyzerHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(ClauseAnalyzerHandler);
    // ITenantCache resource name (FR-05 redis remediation r1). Final on-wire key:
    // spaarke:tenant:{tenantId}:clause-analyzer:{hash}:v1
    private const string CacheResource = "clause-analyzer";
    private const int CacheVersion = 1;
    private const string SchemaName = "ClauseAnalysisResult";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Canonical clause types this handler supports. Output clauses are normalized to
    /// lowercase to match. The LLM schema's <c>type</c> enum is built from this set.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedClauseTypes = new[]
    {
        "indemnity", "liability", "termination", "payment",
        "ip-assignment", "confidentiality", "governing-law", "warranty",
        "force-majeure", "assignment"
    };

    private static readonly HashSet<string> SupportedClauseTypeSet = new(
        SupportedClauseTypes, StringComparer.OrdinalIgnoreCase);

    private readonly IOpenAiClient _openAiClient;
    private readonly ITenantCache _cache;
    private readonly ModelSelectorOptions _modelSelectorOptions;
    private readonly ILogger<ClauseAnalyzerHandler> _logger;

    public ClauseAnalyzerHandler(
        IOpenAiClient openAiClient,
        ITenantCache cache,
        IOptions<ModelSelectorOptions> modelSelectorOptions,
        ILogger<ClauseAnalyzerHandler> logger)
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
        Name: "Clause Analyzer",
        Description: "LLM-assisted contract clause structuring with structural span validation. " +
                     "Identifies typed clauses (indemnity, liability, termination, payment, IP assignment, " +
                     "confidentiality, governing-law, warranty, force-majeure, assignment) and returns " +
                     "per-clause structured fields plus span boundaries. Configurable type filter and " +
                     "obligation / party extraction.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition("text", "Input contract text to structure.", ToolParameterType.String, Required: true),
            new ToolParameterDefinition("clauseTypes", "Optional subset of clause types to extract. Defaults to all supported types.", ToolParameterType.Array, Required: false),
            new ToolParameterDefinition("options", "Optional extraction toggles: { extractObligations: bool, extractParties: bool }.", ToolParameterType.Object, Required: false)
        },
        ConfigurationSchema: ConfigSchema);

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.ClauseAnalyzer };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Both;

    /// <summary>
    /// JSON Schema (Draft 07) describing the handler's configuration JSON (stored in
    /// <c>sprk_analysistool.sprk_configuration</c>).
    /// </summary>
    private static readonly object ConfigSchema = new
    {
        schema = "https://json-schema.org/draft-07/schema#",
        title = "Clause Analyzer Handler Configuration",
        type = "object",
        properties = new
        {
            clauseTypes = new
            {
                type = "array",
                items = new
                {
                    type = "string",
                    @enum = SupportedClauseTypes.ToArray()
                },
                description = "Subset of clause types to extract. When omitted, all supported types are extracted."
            },
            options = new
            {
                type = "object",
                additionalProperties = false,
                properties = new
                {
                    extractObligations = new { type = "boolean", @default = false, description = "Emit per-clause obligation bullets." },
                    extractParties = new { type = "boolean", @default = false, description = "Emit per-clause party list." }
                }
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
            return ToolValidationResult.Failure("TenantId is required.");

        return ValidateConfiguration(tool.Configuration);
    }

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

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
                "ClauseAnalyzerHandler executing for analysis {AnalysisId}, tool {ToolId}",
                context.AnalysisId, tool.Id);

            cancellationToken.ThrowIfCancellationRequested();

            var config = ParseConfiguration(tool.Configuration);
            var inputText = context.Document!.ExtractedText ?? string.Empty;

            return await AnalyzeAndReturnAsync(
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
                "ClauseAnalyzerHandler cancelled for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Clause analysis was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            // ADR-015: log only the exception type — no input/response echo
            _logger.LogError(
                ex,
                "ClauseAnalyzerHandler failed for analysis {AnalysisId}: {ErrorType}",
                context.AnalysisId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Clause analysis failed: {ex.Message}",
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
                "ClauseAnalyzerHandler chat-invocation for session {ChatSessionId}, decision {DecisionId}",
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

            return await AnalyzeAndReturnAsync(
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
                "ClauseAnalyzerHandler chat cancelled for session {ChatSessionId}, decision {DecisionId}",
                context.ChatSessionId, context.DecisionId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Clause analysis was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ClauseAnalyzerHandler chat failed for session {ChatSessionId}, decision {DecisionId}: {ErrorType}",
                context.ChatSessionId, context.DecisionId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Clause analysis failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Core analysis pipeline (cache → LLM → validation → span resolution)
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> AnalyzeAndReturnAsync(
        string inputText,
        AnalysisTool tool,
        ClauseAnalyzerConfig config,
        string tenantId,
        float temperature,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        var effectiveTypes = ResolveEffectiveClauseTypes(config);
        var options = config.Options ?? new ClauseAnalyzerOptions();

        // ADR-014 / FR-05: per-tenant cache id — ITenantCache wraps tenant scoping.
        var cacheId = BuildCacheId(inputText, effectiveTypes, options);

        // Cache lookup (best-effort — failures fall through to LLM call)
        var cached = await TryGetFromCacheAsync(tenantId, cacheId, cancellationToken);
        if (cached is not null)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "ClauseAnalyzerHandler cache hit ({Correlation}): {ClauseCount} clauses, {ClauseBucket} count bucket, " +
                "{UnresolvedCount} unresolved span(s) in {Duration}ms",
                correlationLogId,
                cached.Stats.TotalCount,
                BucketCount(cached.Stats.TotalCount),
                cached.UnresolvedSpans.Count,
                stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                data: cached,
                summary: $"Structured {cached.Stats.TotalCount} clause(s) across {cached.Stats.ByType.Count} type(s) (cache hit).",
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
        var schemaBinaryData = BinaryData.FromString(BuildLlmOutputSchema(effectiveTypes, options));
        var actionModel = config.ModelDeployment ?? _modelSelectorOptions.ToolHandlerModel;
        var prompt = BuildPrompt(inputText, effectiveTypes, options);

        var rawJson = await _openAiClient.GetStructuredCompletionRawAsync(
            prompt,
            schemaBinaryData,
            SchemaName,
            model: actionModel,
            maxOutputTokens: null,
            temperature: temperature,
            cancellationToken: cancellationToken);

        // Parse LLM response — schema-constrained, but defensively handle parse error
        List<RawLlmClause> rawClauses;
        try
        {
            rawClauses = ParseLlmResponse(rawJson);
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "ClauseAnalyzerHandler LLM returned malformed JSON ({Correlation}): {ErrorType} in {Duration}ms",
                correlationLogId, ex.GetType().Name, stopwatch.ElapsedMilliseconds);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Clause analysis failed: LLM response was not valid JSON.",
                ToolErrorCodes.ModelError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ModelCalls = 1,
                    ModelName = actionModel
                });
        }

        // Code-side validation + span resolution + dedupe
        var (validatedClauses, unresolvedSpans) = ValidateAndResolveSpans(
            rawClauses, inputText, effectiveTypes, options);

        var stats = BuildStats(validatedClauses);
        var result = new ClauseAnalysisResult
        {
            Clauses = validatedClauses,
            UnresolvedSpans = unresolvedSpans,
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

        // ADR-015: log count + per-type bucket + unresolved bucket only — NEVER clause text values
        _logger.LogInformation(
            "ClauseAnalyzerHandler complete ({Correlation}): {ClauseCount} clauses, {ClauseBucket} count bucket, " +
            "{TypeCount} type(s) found, {UnresolvedCount} unresolved span(s), in {Duration}ms",
            correlationLogId,
            stats.TotalCount,
            BucketCount(stats.TotalCount),
            stats.ByType.Count,
            unresolvedSpans.Count,
            stopwatch.ElapsedMilliseconds);

        return ToolResult.Ok(
            HandlerId,
            tool.Id,
            tool.Name,
            data: result,
            summary: $"Structured {stats.TotalCount} clause(s) across {stats.ByType.Count} type(s).",
            confidence: validatedClauses.Count == 0 ? 0.0 : validatedClauses.Average(c => c.Confidence),
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
            var config = JsonSerializer.Deserialize<ClauseAnalyzerConfig>(configurationJson, JsonOpts);
            if (config is null)
                return ToolValidationResult.Failure("Invalid configuration JSON.");

            var errors = new List<string>();

            if (config.ClauseTypes is not null)
            {
                foreach (var t in config.ClauseTypes)
                {
                    if (string.IsNullOrWhiteSpace(t))
                    {
                        errors.Add("clauseTypes contains an empty value.");
                        continue;
                    }
                    if (!SupportedClauseTypeSet.Contains(t))
                    {
                        errors.Add($"clauseTypes '{t}' is not supported. Supported: {string.Join(", ", SupportedClauseTypes)}.");
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

    private static ClauseAnalyzerConfig ParseConfiguration(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return new ClauseAnalyzerConfig();

        try
        {
            return JsonSerializer.Deserialize<ClauseAnalyzerConfig>(configurationJson, JsonOpts)
                   ?? new ClauseAnalyzerConfig();
        }
        catch (JsonException)
        {
            return new ClauseAnalyzerConfig();
        }
    }

    private static IReadOnlyList<string> ResolveEffectiveClauseTypes(ClauseAnalyzerConfig config)
    {
        if (config.ClauseTypes is null || config.ClauseTypes.Count == 0)
            return SupportedClauseTypes;

        var filtered = config.ClauseTypes
            .Where(t => !string.IsNullOrWhiteSpace(t) && SupportedClauseTypeSet.Contains(t))
            .Select(t => t.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return filtered.Count == 0 ? SupportedClauseTypes : filtered;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-014: per-tenant cache key
    // ─────────────────────────────────────────────────────────────────────────────

    // Builds the cache "id" component (content hash) consumed by ITenantCache.
    // ITenantCache prepends "tenant:{tenantId}:{resource}:" and appends ":v{version}".
    private static string BuildCacheId(
        string inputText,
        IReadOnlyList<string> clauseTypes,
        ClauseAnalyzerOptions options)
    {
        var typesPart = string.Join(",", clauseTypes.OrderBy(t => t, StringComparer.Ordinal));
        var optionsPart = $"obl={options.ExtractObligations},par={options.ExtractParties}";
        var rawComposite = $"{inputText}|types={typesPart}|options={optionsPart}";
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(rawComposite));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private async Task<ClauseAnalysisResult?> TryGetFromCacheAsync(
        string tenantId, string cacheId, CancellationToken cancellationToken)
    {
        try
        {
            return await _cache.GetAsync<ClauseAnalysisResult>(
                tenantId, CacheResource, cacheId, CacheVersion, ct: cancellationToken);
        }
        catch (Exception ex)
        {
            // Cache failures must NEVER block analysis — log + fall through to LLM
            _logger.LogWarning(
                "ClauseAnalyzerHandler cache read failed: {ErrorType}", ex.GetType().Name);
            return null;
        }
    }

    private async Task TrySetCacheAsync(
        string tenantId, string cacheId, ClauseAnalysisResult value, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SetAsync(
                tenantId, CacheResource, cacheId, CacheVersion, value, CacheTtl, ct: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "ClauseAnalyzerHandler cache write failed: {ErrorType}", ex.GetType().Name);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // LLM prompt + schema
    // ─────────────────────────────────────────────────────────────────────────────

    private static string BuildPrompt(
        string inputText, IReadOnlyList<string> effectiveTypes, ClauseAnalyzerOptions options)
    {
        var typesList = string.Join(", ", effectiveTypes);
        var optionsList = new List<string>();
        if (options.ExtractObligations) optionsList.Add("- Emit per-clause \"obligations\" as a list of short bullet strings.");
        if (options.ExtractParties) optionsList.Add("- Emit per-clause \"parties\" as a list of party identifiers (strings).");
        var optionsBlock = optionsList.Count == 0 ? string.Empty : "\n" + string.Join("\n", optionsList);

        return $$"""
            You are an expert contract-clause structuring system. Identify discrete clauses in
            the input contract text. Only emit clauses of these types: {{typesList}}.

            For each clause:
            - Set "type" to one of: {{typesList}}.
            - Set "text" to the clause text exactly as it appears in the input.
            - Set "startSpan" and "endSpan" to half-open character offsets [start, end) into
              the input text where the clause appears. The offsets MUST be the canonical
              boundaries of the clause as it appears in the input.
            - Set "structuredFields" to an object capturing the key data points of the clause,
              shaped per clause type (e.g., for "payment": amount, currency, dueDate; for
              "termination": noticeDays, terminationCause; for "indemnity": indemnitor,
              indemnitee, scope). Use plain string / number values; nested objects allowed.
            - Set "confidence" to a value in [0.0, 1.0] reflecting your certainty.{{optionsBlock}}

            Clauses MUST NOT overlap. If a span overlaps another or extends beyond the input,
            emit it but it will be filtered to the "unresolvedSpans" output by the post-processor.

            Do NOT emit duplicates. Do NOT emit clauses of unsupported types.

            Input contract text:
            ----- BEGIN -----
            {{inputText}}
            ----- END -----
            """;
    }

    private static string BuildLlmOutputSchema(
        IReadOnlyList<string> effectiveTypes, ClauseAnalyzerOptions options)
    {
        // Structured-output schema constraining the LLM's response shape.
        // Stable ordering so equal effective inputs yield identical cache keys.
        var typesArray = string.Join(",", effectiveTypes.Select(t => $"\"{t}\""));
        var partiesField = options.ExtractParties
            ? @",""parties"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }"
            : string.Empty;
        var obligationsField = options.ExtractObligations
            ? @",""obligations"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }"
            : string.Empty;

        return $$"""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "title": "ClauseAnalysisResult",
              "type": "object",
              "required": ["clauses"],
              "additionalProperties": false,
              "properties": {
                "clauses": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "required": ["type", "text", "startSpan", "endSpan", "structuredFields", "confidence"],
                    "additionalProperties": false,
                    "properties": {
                      "type": { "type": "string", "enum": [{{typesArray}}] },
                      "text": { "type": "string", "minLength": 1 },
                      "startSpan": { "type": "integer", "minimum": 0 },
                      "endSpan": { "type": "integer", "minimum": 0 },
                      "structuredFields": { "type": "object" },
                      "confidence": { "type": "number", "minimum": 0.0, "maximum": 1.0 }{{partiesField}}{{obligationsField}}
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

    private static List<RawLlmClause> ParseLlmResponse(string rawJson)
    {
        var doc = JsonNode.Parse(rawJson);
        if (doc is not JsonObject root)
            throw new JsonException("LLM response root was not a JSON object.");

        if (!root.TryGetPropertyValue("clauses", out var clausesNode) || clausesNode is not JsonArray clauses)
            return new List<RawLlmClause>();

        var result = new List<RawLlmClause>(clauses.Count);
        foreach (var node in clauses)
        {
            if (node is not JsonObject co) continue;
            var raw = co.Deserialize<RawLlmClause>(JsonOpts);
            if (raw is null) continue;
            result.Add(raw);
        }
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Code-side validation + non-overlapping span resolution + dedupe
    // ─────────────────────────────────────────────────────────────────────────────

    private static (List<StructuredClause> Validated, List<UnresolvedSpan> Unresolved)
        ValidateAndResolveSpans(
            IReadOnlyList<RawLlmClause> rawClauses,
            string inputText,
            IReadOnlyList<string> effectiveTypes,
            ClauseAnalyzerOptions options)
    {
        var effectiveTypeSet = new HashSet<string>(effectiveTypes, StringComparer.OrdinalIgnoreCase);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<StructuredClause>(rawClauses.Count);
        var unresolved = new List<UnresolvedSpan>();
        var inputLen = inputText.Length;

        foreach (var raw in rawClauses)
        {
            if (raw is null) continue;
            if (string.IsNullOrWhiteSpace(raw.Type)) continue;
            var type = raw.Type.Trim().ToLowerInvariant();

            // Filter: type allowed?
            if (!effectiveTypeSet.Contains(type)) continue;

            // Span sanity: must be within input bounds and start < end
            var start = raw.StartSpan;
            var end = raw.EndSpan;
            if (start < 0 || end <= start || end > inputLen)
            {
                unresolved.Add(new UnresolvedSpan
                {
                    Type = type,
                    StartSpan = start,
                    EndSpan = end,
                    Reason = "out-of-bounds-or-empty"
                });
                continue;
            }

            // Dedupe key: type|start|end
            var dedupeKey = $"{type}|{start}|{end}";
            if (!seenKeys.Add(dedupeKey)) continue;

            candidates.Add(new StructuredClause
            {
                Type = type,
                Text = raw.Text ?? string.Empty,
                StartSpan = start,
                EndSpan = end,
                StructuredFields = raw.StructuredFields ?? new JsonObject(),
                Parties = options.ExtractParties ? (raw.Parties ?? new List<string>()) : null,
                Obligations = options.ExtractObligations ? (raw.Obligations ?? new List<string>()) : null,
                Confidence = Math.Clamp(raw.Confidence, 0.0, 1.0)
            });
        }

        // Sort by start span, then resolve overlaps deterministically: keep the first
        // (earliest start, then highest confidence on tie); push later overlapping clauses
        // to unresolved.
        var ordered = candidates
            .OrderBy(c => c.StartSpan)
            .ThenByDescending(c => c.Confidence)
            .ThenBy(c => c.Type, StringComparer.Ordinal)
            .ToList();

        var accepted = new List<StructuredClause>(ordered.Count);
        var lastAcceptedEnd = -1;
        foreach (var c in ordered)
        {
            if (c.StartSpan >= lastAcceptedEnd)
            {
                accepted.Add(c);
                lastAcceptedEnd = c.EndSpan;
            }
            else
            {
                unresolved.Add(new UnresolvedSpan
                {
                    Type = c.Type,
                    StartSpan = c.StartSpan,
                    EndSpan = c.EndSpan,
                    Reason = "overlapping-span"
                });
            }
        }

        return (accepted, unresolved);
    }

    private static ClauseStats BuildStats(IReadOnlyList<StructuredClause> clauses)
    {
        var byType = clauses
            .GroupBy(c => c.Type, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return new ClauseStats
        {
            TotalCount = clauses.Count,
            ByType = byType
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-015 telemetry helpers — bucket counts so deterministic IDs don't leak ranges
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bucket clause counts to "0", "1", "2-5", "6-20", "21+" so telemetry conveys volume
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
    internal sealed class ClauseAnalyzerConfig
    {
        [JsonPropertyName("clauseTypes")]
        public List<string>? ClauseTypes { get; set; }

        [JsonPropertyName("options")]
        public ClauseAnalyzerOptions? Options { get; set; }

        [JsonPropertyName("modelDeployment")]
        public string? ModelDeployment { get; set; }
    }

    /// <summary>
    /// Per-extraction toggles. Stable defaults: both off.
    /// </summary>
    internal sealed class ClauseAnalyzerOptions
    {
        [JsonPropertyName("extractObligations")]
        public bool ExtractObligations { get; set; }

        [JsonPropertyName("extractParties")]
        public bool ExtractParties { get; set; }
    }

    /// <summary>
    /// Raw single clause as the LLM emits it (pre-validation).
    /// </summary>
    internal sealed class RawLlmClause
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("startSpan")]
        public int StartSpan { get; set; }

        [JsonPropertyName("endSpan")]
        public int EndSpan { get; set; }

        [JsonPropertyName("structuredFields")]
        public JsonNode? StructuredFields { get; set; }

        [JsonPropertyName("parties")]
        public List<string>? Parties { get; set; }

        [JsonPropertyName("obligations")]
        public List<string>? Obligations { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }
    }

    /// <summary>
    /// Structured output: list of validated + span-resolved clauses + unresolved-span list +
    /// summary stats. Returned in <see cref="ToolResult.Data"/>.
    /// </summary>
    public sealed class ClauseAnalysisResult
    {
        [JsonPropertyName("clauses")]
        public IReadOnlyList<StructuredClause> Clauses { get; set; } = Array.Empty<StructuredClause>();

        [JsonPropertyName("unresolvedSpans")]
        public IReadOnlyList<UnresolvedSpan> UnresolvedSpans { get; set; } = Array.Empty<UnresolvedSpan>();

        [JsonPropertyName("stats")]
        public ClauseStats Stats { get; set; } = new();
    }

    /// <summary>
    /// A single validated, span-resolved clause in the structured output.
    /// </summary>
    public sealed class StructuredClause
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("startSpan")]
        public int StartSpan { get; set; }

        [JsonPropertyName("endSpan")]
        public int EndSpan { get; set; }

        [JsonPropertyName("structuredFields")]
        public JsonNode? StructuredFields { get; set; }

        [JsonPropertyName("parties")]
        public IReadOnlyList<string>? Parties { get; set; }

        [JsonPropertyName("obligations")]
        public IReadOnlyList<string>? Obligations { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }
    }

    /// <summary>
    /// A clause the LLM produced but that failed span resolution (overlapping span or
    /// out-of-bounds). Surfaced to the caller so the playbook / chat surface can decide
    /// whether to re-prompt.
    /// </summary>
    public sealed class UnresolvedSpan
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("startSpan")]
        public int StartSpan { get; set; }

        [JsonPropertyName("endSpan")]
        public int EndSpan { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>Aggregate counts over the extracted clause set.</summary>
    public sealed class ClauseStats
    {
        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }

        [JsonPropertyName("byType")]
        public Dictionary<string, int> ByType { get; set; } = new(StringComparer.Ordinal);
    }
}
