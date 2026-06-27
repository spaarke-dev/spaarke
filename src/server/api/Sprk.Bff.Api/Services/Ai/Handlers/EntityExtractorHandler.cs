using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// LLM-assisted Named Entity Recognition (NER) handler with code-based validation +
/// normalization (R6 Pillar 2, FR-13, Wave 2).
/// </summary>
/// <remarks>
/// <para>
/// Pipeline: input text → per-tenant cache check → LLM structured-output call (constrained
/// JSON schema; <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/>) → deterministic
/// post-processing (validate confidence ≥ threshold, regex-validate format-tight types,
/// normalize per type, dedupe) → structured <see cref="ToolResult"/> with extracted entities.
/// </para>
/// <para>
/// <strong>FR-13 binding</strong>: LLM-assisted (not pure deterministic). Uses Azure OpenAI
/// via <see cref="IOpenAiClient"/>. Configurable via
/// <c>sprk_analysistool.sprk_configuration</c> JSON:
/// </para>
/// <list type="bullet">
/// <item><c>entityTypes</c> (string[], optional): subset filter — e.g.,
/// <c>["organization","person","location","date","money","email","phone","url"]</c>. Default = all.</item>
/// <item><c>confidenceThreshold</c> (number, optional, 0.0–1.0): filters out low-confidence
/// matches. Defaults to 0.6.</item>
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
/// <c>entity-extractor:{tenantId}:{sha256(input+entityTypes+threshold)}</c>. NEVER cross-tenant.</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + outcome + counts +
/// type-distribution buckets + duration ONLY. NEVER input text, extracted entity TEXT values,
/// or raw LLM responses. Entity TYPE counts are deterministic identifiers, safe to log.</item>
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
public sealed class EntityExtractorHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(EntityExtractorHandler);
    // ITenantCache resource name (FR-05 redis remediation r1). Final on-wire key:
    // spaarke:tenant:{tenantId}:entity-extractor:{hash}:v1
    private const string CacheResource = "entity-extractor";
    private const int CacheVersion = 1;
    private const string SchemaName = "EntityExtractionResult";
    private const double DefaultConfidenceThreshold = 0.6;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Canonical entity types this handler supports. Output entities are normalized to
    /// lowercase to match. The LLM schema's <c>type</c> enum is built from this set.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedEntityTypes = new[]
    {
        "organization", "person", "location", "date", "money", "email", "phone", "url"
    };

    private static readonly HashSet<string> SupportedEntityTypeSet = new(
        SupportedEntityTypes, StringComparer.OrdinalIgnoreCase);

    private readonly IOpenAiClient _openAiClient;
    private readonly ITenantCache _cache;
    private readonly ModelSelectorOptions _modelSelectorOptions;
    private readonly ILogger<EntityExtractorHandler> _logger;

    public EntityExtractorHandler(
        IOpenAiClient openAiClient,
        ITenantCache cache,
        IOptions<ModelSelectorOptions> modelSelectorOptions,
        ILogger<EntityExtractorHandler> logger)
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
        Name: "Entity Extractor",
        Description: "LLM-assisted Named Entity Recognition (NER) with code-based validation " +
                     "and normalization. Extracts organizations, persons, locations, dates, " +
                     "money, emails, phone numbers, and URLs from free text. Configurable type " +
                     "filter and confidence threshold.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition("text", "Input text to scan for entities.", ToolParameterType.String, Required: true),
            new ToolParameterDefinition("entityTypes", "Optional subset of entity types to extract. Defaults to all supported types.", ToolParameterType.Array, Required: false),
            new ToolParameterDefinition("confidenceThreshold", "Minimum LLM confidence to include in results (0.0-1.0). Defaults to 0.6.", ToolParameterType.Decimal, Required: false, DefaultValue: DefaultConfidenceThreshold)
        },
        ConfigurationSchema: ConfigSchema);

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.EntityExtractor };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Both;

    /// <summary>
    /// JSON Schema (Draft 07) describing the handler's configuration JSON (stored in
    /// <c>sprk_analysistool.sprk_configuration</c>).
    /// </summary>
    private static readonly object ConfigSchema = new
    {
        schema = "https://json-schema.org/draft-07/schema#",
        title = "Entity Extractor Handler Configuration",
        type = "object",
        properties = new
        {
            entityTypes = new
            {
                type = "array",
                items = new
                {
                    type = "string",
                    @enum = SupportedEntityTypes.ToArray()
                },
                description = "Subset of entity types to extract. When omitted, all supported types are extracted."
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
                "EntityExtractorHandler executing for analysis {AnalysisId}, tool {ToolId}",
                context.AnalysisId, tool.Id);

            cancellationToken.ThrowIfCancellationRequested();

            var config = ParseConfiguration(tool.Configuration);
            var inputText = context.Document!.ExtractedText ?? string.Empty;

            return await ExtractAndReturnAsync(
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
                "EntityExtractorHandler cancelled for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Entity extraction was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            // ADR-015: log only the exception type — no input/response echo
            _logger.LogError(
                ex,
                "EntityExtractorHandler failed for analysis {AnalysisId}: {ErrorType}",
                context.AnalysisId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Entity extraction failed: {ex.Message}",
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
                "EntityExtractorHandler chat-invocation for session {ChatSessionId}, decision {DecisionId}",
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

            return await ExtractAndReturnAsync(
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
                "EntityExtractorHandler chat cancelled for session {ChatSessionId}, decision {DecisionId}",
                context.ChatSessionId, context.DecisionId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Entity extraction was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "EntityExtractorHandler chat failed for session {ChatSessionId}, decision {DecisionId}: {ErrorType}",
                context.ChatSessionId, context.DecisionId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Entity extraction failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Core extraction pipeline (cache → LLM → validation → normalization)
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> ExtractAndReturnAsync(
        string inputText,
        AnalysisTool tool,
        EntityExtractorConfig config,
        string tenantId,
        float temperature,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        // Resolve effective entity types (config filter or all supported)
        var effectiveTypes = ResolveEffectiveEntityTypes(config);
        var confidenceThreshold = config.ConfidenceThreshold ?? DefaultConfidenceThreshold;

        // ADR-014 / FR-05: per-tenant cache id — ITenantCache wraps tenant scoping.
        var cacheId = BuildCacheId(inputText, effectiveTypes, confidenceThreshold);

        // Cache lookup (best-effort — failures fall through to LLM call)
        var cached = await TryGetFromCacheAsync(tenantId, cacheId, cancellationToken);
        if (cached is not null)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "EntityExtractorHandler cache hit ({Correlation}): {EntityCount} entities, {TypeBucket} type-count bucket in {Duration}ms",
                correlationLogId,
                cached.Stats.TotalCount,
                BucketCount(cached.Stats.TotalCount),
                stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                data: cached,
                summary: $"Extracted {cached.Stats.TotalCount} entity reference(s) (cache hit).",
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
        var schemaBinaryData = BinaryData.FromString(BuildLlmOutputSchema(effectiveTypes));
        var actionModel = config.ModelDeployment ?? _modelSelectorOptions.ToolHandlerModel;
        var prompt = BuildPrompt(inputText, effectiveTypes, confidenceThreshold);

        var rawJson = await _openAiClient.GetStructuredCompletionRawAsync(
            prompt,
            schemaBinaryData,
            SchemaName,
            model: actionModel,
            maxOutputTokens: null,
            temperature: temperature,
            cancellationToken: cancellationToken);

        // Parse LLM response — schema-constrained, but defensively handle parse error
        List<RawLlmEntity> rawEntities;
        try
        {
            rawEntities = ParseLlmResponse(rawJson);
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "EntityExtractorHandler LLM returned malformed JSON ({Correlation}): {ErrorType} in {Duration}ms",
                correlationLogId, ex.GetType().Name, stopwatch.ElapsedMilliseconds);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Entity extraction failed: LLM response was not valid JSON.",
                ToolErrorCodes.ModelError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ModelCalls = 1,
                    ModelName = actionModel
                });
        }

        // Code-side validation + normalization + dedupe
        var validatedEntities = ValidateAndNormalize(
            rawEntities, inputText, effectiveTypes, confidenceThreshold);

        var stats = BuildStats(validatedEntities);
        var result = new EntityExtractionResult
        {
            Entities = validatedEntities,
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

        // ADR-015: log count + type-distribution buckets only — NEVER entity values
        _logger.LogInformation(
            "EntityExtractorHandler complete ({Correlation}): {EntityCount} entities, {TypeBucket} type-count bucket, {TypeCount} type(s) found, in {Duration}ms",
            correlationLogId,
            stats.TotalCount,
            BucketCount(stats.TotalCount),
            stats.ByType.Count,
            stopwatch.ElapsedMilliseconds);

        return ToolResult.Ok(
            HandlerId,
            tool.Id,
            tool.Name,
            data: result,
            summary: $"Extracted {stats.TotalCount} entity reference(s) across {stats.ByType.Count} type(s).",
            confidence: validatedEntities.Count == 0 ? 0.0 : validatedEntities.Average(e => e.Confidence),
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
            var config = JsonSerializer.Deserialize<EntityExtractorConfig>(configurationJson, JsonOpts);
            if (config is null)
                return ToolValidationResult.Failure("Invalid configuration JSON.");

            var errors = new List<string>();

            if (config.EntityTypes is not null)
            {
                foreach (var t in config.EntityTypes)
                {
                    if (string.IsNullOrWhiteSpace(t))
                    {
                        errors.Add("entityTypes contains an empty value.");
                        continue;
                    }
                    if (!SupportedEntityTypeSet.Contains(t))
                    {
                        errors.Add($"entityTypes '{t}' is not supported. Supported: {string.Join(", ", SupportedEntityTypes)}.");
                    }
                }
            }

            if (config.ConfidenceThreshold is < 0.0 or > 1.0)
                errors.Add($"confidenceThreshold {config.ConfidenceThreshold} must be between 0.0 and 1.0.");

            return errors.Count == 0
                ? ToolValidationResult.Success()
                : ToolValidationResult.Failure(errors);
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Failure($"Configuration JSON parse error: {ex.Message}");
        }
    }

    private static EntityExtractorConfig ParseConfiguration(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return new EntityExtractorConfig();

        try
        {
            return JsonSerializer.Deserialize<EntityExtractorConfig>(configurationJson, JsonOpts)
                   ?? new EntityExtractorConfig();
        }
        catch (JsonException)
        {
            return new EntityExtractorConfig();
        }
    }

    private static IReadOnlyList<string> ResolveEffectiveEntityTypes(EntityExtractorConfig config)
    {
        if (config.EntityTypes is null || config.EntityTypes.Count == 0)
            return SupportedEntityTypes;

        var filtered = config.EntityTypes
            .Where(t => !string.IsNullOrWhiteSpace(t) && SupportedEntityTypeSet.Contains(t))
            .Select(t => t.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return filtered.Count == 0 ? SupportedEntityTypes : filtered;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-014: per-tenant cache key
    // ─────────────────────────────────────────────────────────────────────────────

    // Builds the cache "id" component (content hash) consumed by ITenantCache.
    private static string BuildCacheId(
        string inputText,
        IReadOnlyList<string> entityTypes,
        double confidenceThreshold)
    {
        var typesPart = string.Join(",", entityTypes.OrderBy(t => t, StringComparer.Ordinal));
        var rawComposite = $"{inputText}|types={typesPart}|threshold={confidenceThreshold.ToString("F2", CultureInfo.InvariantCulture)}";
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(rawComposite));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private async Task<EntityExtractionResult?> TryGetFromCacheAsync(
        string tenantId, string cacheId, CancellationToken cancellationToken)
    {
        try
        {
            return await _cache.GetAsync<EntityExtractionResult>(
                tenantId, CacheResource, cacheId, CacheVersion, ct: cancellationToken);
        }
        catch (Exception ex)
        {
            // Cache failures must NEVER block extraction — log + fall through to LLM
            _logger.LogWarning(
                "EntityExtractorHandler cache read failed: {ErrorType}", ex.GetType().Name);
            return null;
        }
    }

    private async Task TrySetCacheAsync(
        string tenantId, string cacheId, EntityExtractionResult value, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SetAsync(
                tenantId, CacheResource, cacheId, CacheVersion, value, CacheTtl, ct: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "EntityExtractorHandler cache write failed: {ErrorType}", ex.GetType().Name);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // LLM prompt + schema
    // ─────────────────────────────────────────────────────────────────────────────

    private static string BuildPrompt(
        string inputText, IReadOnlyList<string> effectiveTypes, double confidenceThreshold)
    {
        var typesList = string.Join(", ", effectiveTypes);
        return $$"""
            You are an expert Named Entity Recognition (NER) system. Identify entities in the
            following input text. Only emit entities of these types: {{typesList}}.

            For each entity:
            - Set "text" to the entity span as it appears in the input.
            - Set "type" to one of: {{typesList}}.
            - Set "normalizedValue" to a canonical form:
              - date → ISO 8601 (YYYY-MM-DD) when possible.
              - money → "<amount> <ISO-4217-currency>" (e.g., "1000 USD"); preserve precision.
              - email → lowercased.
              - phone → digits with leading "+" if international; no spaces or punctuation.
              - url → as-is.
              - organization, person, location → trimmed, original casing.
            - Set "confidence" to a value in [0.0, 1.0] reflecting your certainty.
            - Set "span.start" and "span.end" to the half-open character offsets [start, end)
              into the input text where this entity appears.

            Include only entities with confidence >= {{confidenceThreshold.ToString("F2", CultureInfo.InvariantCulture)}}.
            Do NOT emit duplicates. Do NOT emit entities of unsupported types.

            Input text:
            ----- BEGIN -----
            {{inputText}}
            ----- END -----
            """;
    }

    private static string BuildLlmOutputSchema(IReadOnlyList<string> effectiveTypes)
    {
        // Structured-output schema constraining the LLM's response shape.
        // Using ordered string literal so the schema is stable across runs (deterministic
        // cache hashing for same effective types).
        var typesArray = string.Join(",", effectiveTypes.Select(t => $"\"{t}\""));
        return $$"""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "title": "EntityExtractionResult",
              "type": "object",
              "required": ["entities"],
              "additionalProperties": false,
              "properties": {
                "entities": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "required": ["text", "type", "normalizedValue", "confidence", "span"],
                    "additionalProperties": false,
                    "properties": {
                      "text": { "type": "string", "minLength": 1 },
                      "type": { "type": "string", "enum": [{{typesArray}}] },
                      "normalizedValue": { "type": "string" },
                      "confidence": { "type": "number", "minimum": 0.0, "maximum": 1.0 },
                      "span": {
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

    private static List<RawLlmEntity> ParseLlmResponse(string rawJson)
    {
        var doc = JsonNode.Parse(rawJson);
        if (doc is not JsonObject root)
            throw new JsonException("LLM response root was not a JSON object.");

        if (!root.TryGetPropertyValue("entities", out var entitiesNode) || entitiesNode is not JsonArray entities)
            return new List<RawLlmEntity>();

        var result = new List<RawLlmEntity>(entities.Count);
        foreach (var node in entities)
        {
            if (node is not JsonObject eo) continue;
            var raw = eo.Deserialize<RawLlmEntity>(JsonOpts);
            if (raw is null) continue;
            result.Add(raw);
        }
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Code-side validation + normalization + dedupe
    // ─────────────────────────────────────────────────────────────────────────────

    // Regex patterns for format-tight types. Compiled for repeat use.
    private static readonly Regex EmailRegex = new(
        @"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$",
        RegexOptions.Compiled);

    private static readonly Regex UrlRegex = new(
        @"^https?://[^\s/$.?#].[^\s]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Phone: strip non-digit (preserve leading +), then require 7-15 digits.
    private static readonly Regex PhoneSanitizeRegex = new(
        @"[^\d+]",
        RegexOptions.Compiled);

    // ISO 8601 date (YYYY-MM-DD).
    private static readonly Regex Iso8601DateRegex = new(
        @"^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$",
        RegexOptions.Compiled);

    // Money: "<decimal> <ISO-4217 3-letter currency>" — case-insensitive.
    private static readonly Regex MoneyRegex = new(
        @"^-?\d+(\.\d+)?\s+[A-Za-z]{3}$",
        RegexOptions.Compiled);

    private static List<ExtractedEntity> ValidateAndNormalize(
        IReadOnlyList<RawLlmEntity> rawEntities,
        string inputText,
        IReadOnlyList<string> effectiveTypes,
        double confidenceThreshold)
    {
        var effectiveTypeSet = new HashSet<string>(effectiveTypes, StringComparer.OrdinalIgnoreCase);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal); // dedupe key: type|normalizedValue|start
        var results = new List<ExtractedEntity>(rawEntities.Count);

        foreach (var raw in rawEntities)
        {
            if (raw is null) continue;
            if (string.IsNullOrWhiteSpace(raw.Text)) continue;
            if (string.IsNullOrWhiteSpace(raw.Type)) continue;

            var type = raw.Type.Trim().ToLowerInvariant();

            // Filter: type allowed?
            if (!effectiveTypeSet.Contains(type)) continue;

            // Filter: confidence threshold
            if (raw.Confidence < confidenceThreshold) continue;

            // Normalize per type
            var normalized = NormalizeByType(type, raw);
            if (normalized is null) continue; // failed type-specific validation

            // Span sanity check (clamp to input bounds)
            var (startIdx, endIdx) = ClampSpan(raw.Span, inputText.Length);

            var dedupeKey = $"{type}|{normalized}|{startIdx}";
            if (!seenKeys.Add(dedupeKey)) continue;

            results.Add(new ExtractedEntity
            {
                Text = raw.Text.Trim(),
                Type = type,
                NormalizedValue = normalized,
                Confidence = Math.Clamp(raw.Confidence, 0.0, 1.0),
                Span = new EntitySpan { Start = startIdx, End = endIdx }
            });
        }

        // Deterministic ordering for stable cache / dedupe semantics
        return results
            .OrderBy(e => e.Span.Start)
            .ThenBy(e => e.Type, StringComparer.Ordinal)
            .ThenBy(e => e.NormalizedValue, StringComparer.Ordinal)
            .ToList();
    }

    private static string? NormalizeByType(string type, RawLlmEntity raw)
    {
        var rawNormalized = raw.NormalizedValue ?? raw.Text ?? string.Empty;
        rawNormalized = rawNormalized.Trim();

        return type switch
        {
            "email" => NormalizeEmail(rawNormalized),
            "phone" => NormalizePhone(rawNormalized),
            "url" => NormalizeUrl(rawNormalized),
            "date" => NormalizeDate(rawNormalized),
            "money" => NormalizeMoney(rawNormalized),
            // For organization/person/location — trim whitespace, preserve original casing.
            "organization" or "person" or "location" =>
                string.IsNullOrWhiteSpace(rawNormalized) ? null : rawNormalized,
            _ => null
        };
    }

    private static string? NormalizeEmail(string s)
    {
        var lower = s.ToLowerInvariant().Trim();
        return EmailRegex.IsMatch(lower) ? lower : null;
    }

    private static string? NormalizePhone(string s)
    {
        var sanitized = PhoneSanitizeRegex.Replace(s, string.Empty);

        // Preserve only a single leading '+'; strip any other '+'
        if (sanitized.Length > 1)
        {
            var head = sanitized[0];
            var tail = sanitized.Substring(1).Replace("+", string.Empty);
            sanitized = (head == '+' ? "+" : head.ToString()) + tail;
        }

        var digitCount = sanitized.Count(char.IsDigit);
        if (digitCount < 7 || digitCount > 15) return null;
        return sanitized;
    }

    private static string? NormalizeUrl(string s)
    {
        var trimmed = s.Trim();
        if (!UrlRegex.IsMatch(trimmed)) return null;
        return trimmed;
    }

    private static string? NormalizeDate(string s)
    {
        var trimmed = s.Trim();
        if (Iso8601DateRegex.IsMatch(trimmed)) return trimmed;

        // Try permissive parse → format as ISO 8601
        if (DateTime.TryParse(
                trimmed, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        return null;
    }

    private static string? NormalizeMoney(string s)
    {
        var trimmed = s.Trim();
        if (!MoneyRegex.IsMatch(trimmed)) return null;

        // Uppercase currency code
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;
        return $"{parts[0]} {parts[1].ToUpperInvariant()}";
    }

    private static (int Start, int End) ClampSpan(EntitySpan? span, int inputLength)
    {
        var start = Math.Clamp(span?.Start ?? 0, 0, inputLength);
        var end = Math.Clamp(span?.End ?? start, start, inputLength);
        return (start, end);
    }

    private static EntityStats BuildStats(IReadOnlyList<ExtractedEntity> entities)
    {
        var byType = entities
            .GroupBy(e => e.Type, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return new EntityStats
        {
            TotalCount = entities.Count,
            ByType = byType
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-015 telemetry helpers — bucket counts so deterministic IDs don't leak ranges
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bucket entity counts to "0", "1", "2-5", "6-20", "21+" so telemetry conveys volume
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
    internal sealed class EntityExtractorConfig
    {
        [JsonPropertyName("entityTypes")]
        public List<string>? EntityTypes { get; set; }

        [JsonPropertyName("confidenceThreshold")]
        public double? ConfidenceThreshold { get; set; }

        [JsonPropertyName("modelDeployment")]
        public string? ModelDeployment { get; set; }
    }

    /// <summary>
    /// Raw single entity as the LLM emits it (pre-validation).
    /// </summary>
    internal sealed class RawLlmEntity
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("normalizedValue")]
        public string? NormalizedValue { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("span")]
        public EntitySpan? Span { get; set; }
    }

    /// <summary>
    /// Structured output: list of validated + normalized entities + summary stats.
    /// Returned in <see cref="ToolResult.Data"/>.
    /// </summary>
    public sealed class EntityExtractionResult
    {
        [JsonPropertyName("entities")]
        public IReadOnlyList<ExtractedEntity> Entities { get; set; } = Array.Empty<ExtractedEntity>();

        [JsonPropertyName("stats")]
        public EntityStats Stats { get; set; } = new();
    }

    /// <summary>
    /// Single validated entity in the structured output.
    /// </summary>
    public sealed class ExtractedEntity
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("normalizedValue")]
        public string NormalizedValue { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("span")]
        public EntitySpan Span { get; set; } = new();
    }

    /// <summary>Half-open character span [start, end) in the input text.</summary>
    public sealed class EntitySpan
    {
        [JsonPropertyName("start")]
        public int Start { get; set; }

        [JsonPropertyName("end")]
        public int End { get; set; }
    }

    /// <summary>Aggregate counts over the extracted entity set.</summary>
    public sealed class EntityStats
    {
        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }

        [JsonPropertyName("byType")]
        public Dictionary<string, int> ByType { get; set; } = new(StringComparer.Ordinal);
    }
}
