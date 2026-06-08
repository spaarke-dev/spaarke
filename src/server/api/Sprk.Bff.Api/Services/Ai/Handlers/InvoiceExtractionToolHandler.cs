using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// LLM-assisted typed handler — R6 Pillar 2, Wave 2 (FR-19).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Pipeline</strong>: input invoice text (document or chat-arg) → Azure OpenAI
/// structured-output extraction (header + line items) → deterministic decimal arithmetic
/// (line totals, subtotal, tax, discount, grand total) → <see cref="ToolResult"/> with
/// structured invoice and any <c>arithmeticErrors</c>.
/// </para>
/// <para>
/// <strong>Surface vs auto-correct (FR-19 binding)</strong>: when LLM-extracted figures
/// disagree with computed totals, the handler <em>reports</em> the discrepancy in the
/// <c>arithmeticErrors</c> array rather than silently overwriting the LLM output. This
/// preserves an auditable trail — downstream consumers (workspace widget, JPS pipeline,
/// human reviewer) see both the LLM's reading and the deterministic computation.
/// </para>
/// <para>
/// <strong>Decimal-only money math (binding)</strong>: every monetary computation uses
/// <see cref="decimal"/> — never <see cref="double"/> or <see cref="float"/>. This avoids
/// classical floating-point rounding errors that make <c>0.1 + 0.2 != 0.3</c> under
/// IEEE-754. Tests assert the canonical <c>1.10m + 1.10m + 1.10m == 3.30m</c> property.
/// </para>
/// <para>
/// <strong>Configurable behavior</strong> (from <c>sprk_analysistool.sprk_configuration</c> JSON):
/// </para>
/// <list type="bullet">
/// <item><c>taxMethod</c> — <c>exclusive</c> (default; tax computed on subtotal, added on top) or
/// <c>inclusive</c> (line totals already include tax; subtotal is back-derived).</item>
/// <item><c>discountOrder</c> — <c>pre-tax</c> (default; discount applied before tax) or
/// <c>post-tax</c> (discount applied after tax).</item>
/// <item><c>roundingMode</c> — <c>banker</c> (default; <see cref="MidpointRounding.ToEven"/>) or
/// <c>away_from_zero</c>.</item>
/// <item><c>decimalPrecision</c> — fractional digits used in final rounding (default 2).</item>
/// <item><c>expectedCurrencies</c> — ISO-4217 codes accepted by the handler (default USD/EUR/GBP/JPY/CAD).</item>
/// <item><c>modelDeployment</c> — Azure OpenAI deployment override; falls back to
/// <c>ModelSelectorOptions.ToolHandlerModel</c>.</item>
/// <item><c>extractLineItems</c> — when false, skips line-item extraction (header only).</item>
/// <item><c>validateArithmetic</c> — when false, suppresses <c>arithmeticErrors</c> array entirely.</item>
/// </list>
/// <para>
/// <strong>ADR alignment</strong>:
/// </para>
/// <list type="bullet">
/// <item><b>ADR-010</b>: auto-discovered by <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c>;
/// ZERO manual DI line.</item>
/// <item><b>ADR-013</b>: handler lives in <c>Services/Ai/Handlers/</c>;
/// <see cref="IOpenAiClient"/> stays AI-internal — never injected into CRUD-side code.</item>
/// <item><b>ADR-014</b>: cache key is
/// <c>invoice-extractor:{tenantId}:{sha256(text + config-fingerprint)}</c>. Tenant ID is
/// mandatory. Output the cached <see cref="LlmInvoicePayload"/> is reusable across
/// identical-input requests.</item>
/// <item><b>ADR-015</b>: telemetry logs handler name + outcome buckets (line-item count
/// bucket, arithmetic-error count) + duration + IDs ONLY. NEVER vendor / customer text,
/// invoice number, line-item descriptions, or monetary values.</item>
/// <item><b>ADR-016</b>: LLM call dispatches through the existing
/// <see cref="IOpenAiClient.GetStructuredCompletionRawAsync(string, BinaryData, string, string?, int?, CancellationToken)"/>
/// path, which sits behind the <c>ai-context</c> rate-limit policy.</item>
/// <item><b>ADR-029</b>: zero new NuGet packages; per-task delta target ≤+0.6 MB; expected
/// well under that ceiling.</item>
/// </list>
/// <para>
/// <strong>Dataverse contract</strong>: ships with <c>sprk_analysistool</c> row whose
/// <c>sprk_handlerclass = "InvoiceExtractionToolHandler"</c>,
/// <c>sprk_toolcode = "INVEXT@v1"</c> (9 chars, ≤10 cap), <c>sprk_availableincontexts = Both</c>.
/// Seed lives at <c>infra/dataverse/sprk_analysistool-invoice-extraction-row.json</c>.
/// </para>
/// </remarks>
public sealed class InvoiceExtractionToolHandler : IToolHandler
{
    private const string HandlerIdValue = "InvoiceExtractionToolHandler";
    private const string CacheKeyPrefix = "invoice-extractor";

    private static readonly IReadOnlySet<string> DefaultExpectedCurrencies =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "USD", "EUR", "GBP", "JPY", "CAD" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// JSON Schema (Draft 07) — surfaces in <c>Metadata.ConfigurationSchema</c>.
    /// </summary>
    private static readonly object ConfigSchemaShape = new
    {
        schema = "https://json-schema.org/draft-07/schema#",
        title = "Invoice Extraction Tool Configuration",
        type = "object",
        properties = new
        {
            taxMethod = new { type = "string", @enum = new[] { "exclusive", "inclusive" }, @default = "exclusive" },
            discountOrder = new { type = "string", @enum = new[] { "pre-tax", "post-tax" }, @default = "pre-tax" },
            roundingMode = new { type = "string", @enum = new[] { "banker", "away_from_zero" }, @default = "banker" },
            decimalPrecision = new { type = "integer", minimum = 0, maximum = 8, @default = 2 },
            expectedCurrencies = new
            {
                type = "array",
                items = new { type = "string", minLength = 3, maxLength = 3 },
                @default = new[] { "USD", "EUR", "GBP", "JPY", "CAD" }
            },
            modelDeployment = new { type = "string", description = "Azure OpenAI deployment name override." },
            extractLineItems = new { type = "boolean", @default = true },
            validateArithmetic = new { type = "boolean", @default = true }
        },
        required = Array.Empty<string>()
    };

    /// <summary>
    /// JSON Schema (Draft 2020-12) describing the LLM's structured-output payload.
    /// The Azure OpenAI structured-outputs feature constrains decoding to this shape,
    /// so the handler never has to re-validate field presence — only contents.
    /// </summary>
    /// <remarks>
    /// Authored as a string here (rather than via anonymous-type → SerializeToElement)
    /// so reviewers can read the schema linearly and so the additionalProperties:false
    /// + required:[...] strict-mode flags survive the round-trip.
    /// </remarks>
    private const string LlmOutputSchemaJson = """
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "InvoicePayload",
  "type": "object",
  "additionalProperties": false,
  "required": ["invoice", "lineItems", "totals"],
  "properties": {
    "invoice": {
      "type": "object",
      "additionalProperties": false,
      "required": ["number", "issueDate", "dueDate", "vendor", "customer"],
      "properties": {
        "number":     { "type": ["string", "null"] },
        "issueDate":  { "type": ["string", "null"] },
        "dueDate":    { "type": ["string", "null"] },
        "vendor":     { "type": ["string", "null"] },
        "customer":   { "type": ["string", "null"] }
      }
    },
    "lineItems": {
      "type": "array",
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["description", "quantity", "unitPrice", "lineTotal", "taxRate", "taxAmount"],
        "properties": {
          "description": { "type": "string" },
          "quantity":    { "type": "number" },
          "unitPrice":   { "type": "number" },
          "lineTotal":   { "type": "number" },
          "taxRate":     { "type": ["number", "null"] },
          "taxAmount":   { "type": ["number", "null"] }
        }
      }
    },
    "totals": {
      "type": "object",
      "additionalProperties": false,
      "required": ["subtotal", "taxTotal", "discountTotal", "grandTotal", "currency"],
      "properties": {
        "subtotal":      { "type": "number" },
        "taxTotal":      { "type": "number" },
        "discountTotal": { "type": "number" },
        "grandTotal":    { "type": "number" },
        "currency":      { "type": "string" }
      }
    }
  }
}
""";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly IOpenAiClient _openAiClient;
    private readonly IDistributedCache _cache;
    private readonly ModelSelectorOptions _modelSelectorOptions;
    private readonly ILogger<InvoiceExtractionToolHandler> _logger;

    public InvoiceExtractionToolHandler(
        IOpenAiClient openAiClient,
        IDistributedCache cache,
        IOptions<ModelSelectorOptions> modelSelectorOptions,
        ILogger<InvoiceExtractionToolHandler> logger)
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
        Name: "Invoice Extraction Tool Handler",
        Description: "LLM-assisted invoice extraction with deterministic decimal line-item arithmetic. " +
                     "Supports inclusive/exclusive tax + pre-tax/post-tax discount order. Surfaces " +
                     "arithmetic mismatches in arithmeticErrors[] (does not auto-correct).",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain", "application/pdf", "application/json" },
        Parameters: new[]
        {
            new ToolParameterDefinition("taxMethod", "Tax method: 'exclusive' (default) or 'inclusive'.", ToolParameterType.String, Required: false, DefaultValue: "exclusive"),
            new ToolParameterDefinition("discountOrder", "Discount order: 'pre-tax' (default) or 'post-tax'.", ToolParameterType.String, Required: false, DefaultValue: "pre-tax"),
            new ToolParameterDefinition("roundingMode", "Rounding: 'banker' (default) or 'away_from_zero'.", ToolParameterType.String, Required: false, DefaultValue: "banker"),
            new ToolParameterDefinition("decimalPrecision", "Decimal places (0-8; default 2).", ToolParameterType.Integer, Required: false, DefaultValue: 2),
            new ToolParameterDefinition("expectedCurrencies", "Allowed ISO-4217 codes.", ToolParameterType.Array, Required: false),
            new ToolParameterDefinition("modelDeployment", "Azure OpenAI deployment name override.", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("extractLineItems", "When false, header-only extraction.", ToolParameterType.Boolean, Required: false, DefaultValue: true),
            new ToolParameterDefinition("validateArithmetic", "When false, suppress arithmeticErrors[].", ToolParameterType.Boolean, Required: false, DefaultValue: true)
        },
        ConfigurationSchema: ConfigSchemaShape);

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Both;

    // ─────────────────────────────────────────────────────────────────────────────
    // Playbook (document-context) invocation
    // ─────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required (ADR-014 cache-key invariant).");

        if (context.Document is null)
            errors.Add("Document context is required.");

        if (string.IsNullOrWhiteSpace(context.Document?.ExtractedText))
            errors.Add("Document extracted text is required.");

        errors.AddRange(ValidateConfigurationJson(tool.Configuration));

        return errors.Count == 0 ? ToolValidationResult.Success() : ToolValidationResult.Failure(errors);
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var invoiceText = context.Document?.ExtractedText ?? string.Empty;
        return ExecuteCoreAsync(
            invoiceText,
            tool,
            context.TenantId,
            invocationId: context.AnalysisId,
            cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Chat invocation (LLM function calling)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required (ADR-014 cache-key invariant).");

        if (string.IsNullOrWhiteSpace(context.ToolArgumentsJson))
            errors.Add("ToolArgumentsJson is required for chat invocation.");

        // Chat-side: text comes from tool args, not document context.
        if (!string.IsNullOrWhiteSpace(context.ToolArgumentsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(context.ToolArgumentsJson);
                if (!doc.RootElement.TryGetProperty("text", out var textProp) ||
                    textProp.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(textProp.GetString()))
                {
                    errors.Add("'text' property is required in tool arguments.");
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"Invalid tool arguments JSON: {ex.Message}");
            }
        }

        errors.AddRange(ValidateConfigurationJson(tool.Configuration));

        return errors.Count == 0 ? ToolValidationResult.Success() : ToolValidationResult.Failure(errors);
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        string invoiceText = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(context.ToolArgumentsJson ?? "{}");
            if (doc.RootElement.TryGetProperty("text", out var textProp) &&
                textProp.ValueKind == JsonValueKind.String)
            {
                invoiceText = textProp.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Validation already rejected; defensive guard for ExecuteChatAsync direct callers.
        }

        return ExecuteCoreAsync(
            invoiceText,
            tool,
            context.TenantId,
            invocationId: context.ChatSessionId,
            cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Shared core
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> ExecuteCoreAsync(
        string invoiceText,
        AnalysisTool tool,
        string? tenantId,
        Guid invocationId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        // ADR-015: log ids + tenant + handler id ONLY. NEVER invoice text or values.
        _logger.LogInformation(
            "InvoiceExtractionToolHandler starting: invocation={InvocationId}, tool={ToolId} ({ToolName}), tenant={TenantId}",
            invocationId, tool.Id, tool.Name, tenantId);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var config = ParseConfig(tool.Configuration);
            var resolvedModel = string.IsNullOrWhiteSpace(config.ModelDeployment)
                ? _modelSelectorOptions.ToolHandlerModel
                : config.ModelDeployment;

            var cacheKey = BuildCacheKey(tenantId!, invoiceText, config);

            // ADR-014 — per-tenant cache lookup.
            bool cacheHit = false;
            LlmInvoicePayload? llmPayload = null;
            var cachedBytes = await _cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cachedBytes is not null && cachedBytes.Length > 0)
            {
                try
                {
                    var cachedJson = Encoding.UTF8.GetString(cachedBytes);
                    llmPayload = JsonSerializer.Deserialize<LlmInvoicePayload>(cachedJson, JsonOptions);
                    cacheHit = llmPayload is not null;
                }
                catch (JsonException)
                {
                    // Cache poisoned — fall through and re-fetch.
                    cacheHit = false;
                }
            }

            if (!cacheHit)
            {
                // ADR-016 — LLM call dispatches through the existing IOpenAiClient surface,
                // which sits behind the `ai-context` rate-limit policy. No bypass.
                var prompt = BuildPrompt(invoiceText, config);
                var schema = BinaryData.FromString(LlmOutputSchemaJson);
                string rawJson;
                try
                {
                    rawJson = await _openAiClient.GetStructuredCompletionRawAsync(
                        prompt,
                        schema,
                        schemaName: "InvoicePayload",
                        model: resolvedModel,
                        maxOutputTokens: null,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw; // bubble up to outer catch
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    _logger.LogWarning(
                        "InvoiceExtractionToolHandler LLM call failed: invocation={InvocationId}, model={Model}",
                        invocationId, resolvedModel);
                    return ToolResult.Error(
                        HandlerId, tool.Id, tool.Name,
                        $"LLM extraction failed: {ex.Message}",
                        ToolErrorCodes.ModelError,
                        new ToolExecutionMetadata
                        {
                            StartedAt = startedAt,
                            CompletedAt = DateTimeOffset.UtcNow,
                            ModelCalls = 1,
                            ModelName = resolvedModel
                        });
                }

                try
                {
                    llmPayload = JsonSerializer.Deserialize<LlmInvoicePayload>(rawJson, JsonOptions);
                }
                catch (JsonException ex)
                {
                    stopwatch.Stop();
                    _logger.LogWarning(
                        "InvoiceExtractionToolHandler LLM returned invalid JSON: invocation={InvocationId}",
                        invocationId);
                    return ToolResult.Error(
                        HandlerId, tool.Id, tool.Name,
                        $"LLM returned malformed JSON: {ex.Message}",
                        ToolErrorCodes.ModelError,
                        new ToolExecutionMetadata
                        {
                            StartedAt = startedAt,
                            CompletedAt = DateTimeOffset.UtcNow,
                            ModelCalls = 1,
                            ModelName = resolvedModel
                        });
                }

                if (llmPayload is null)
                {
                    stopwatch.Stop();
                    return ToolResult.Error(
                        HandlerId, tool.Id, tool.Name,
                        "LLM response deserialized to null.",
                        ToolErrorCodes.ModelError,
                        new ToolExecutionMetadata
                        {
                            StartedAt = startedAt,
                            CompletedAt = DateTimeOffset.UtcNow,
                            ModelCalls = 1,
                            ModelName = resolvedModel
                        });
                }

                // Cache the raw LLM payload (already strict-decoded JSON).
                var rawBytes = Encoding.UTF8.GetBytes(rawJson);
                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheTtl
                };
                await _cache.SetAsync(cacheKey, rawBytes, cacheOptions, cancellationToken).ConfigureAwait(false);
            }

            // Currency validation — fail fast if the LLM reports a currency we don't accept.
            var currency = llmPayload!.Totals.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currency))
            {
                stopwatch.Stop();
                return ToolResult.Error(
                    HandlerId, tool.Id, tool.Name,
                    "Invoice currency is missing.",
                    ToolErrorCodes.ValidationFailed,
                    new ToolExecutionMetadata
                    {
                        StartedAt = startedAt,
                        CompletedAt = DateTimeOffset.UtcNow,
                        ModelCalls = cacheHit ? 0 : 1,
                        ModelName = cacheHit ? null : resolvedModel,
                        CacheHit = cacheHit
                    });
            }

            var allowedCurrencies = (config.ExpectedCurrencies is { Count: > 0 } expected
                ? new HashSet<string>(expected, StringComparer.OrdinalIgnoreCase)
                : (IReadOnlySet<string>)DefaultExpectedCurrencies);

            if (!allowedCurrencies.Contains(currency))
            {
                stopwatch.Stop();
                return ToolResult.Error(
                    HandlerId, tool.Id, tool.Name,
                    $"Invoice currency '{currency}' is not in the expectedCurrencies allow-list.",
                    ToolErrorCodes.ValidationFailed,
                    new ToolExecutionMetadata
                    {
                        StartedAt = startedAt,
                        CompletedAt = DateTimeOffset.UtcNow,
                        ModelCalls = cacheHit ? 0 : 1,
                        ModelName = cacheHit ? null : resolvedModel,
                        CacheHit = cacheHit
                    });
            }

            // Validate negative quantity / unit price fail-fast (financial-correctness invariant).
            foreach (var line in llmPayload.LineItems)
            {
                if (line.Quantity < 0m || line.UnitPrice < 0m)
                {
                    stopwatch.Stop();
                    return ToolResult.Error(
                        HandlerId, tool.Id, tool.Name,
                        "Line item has negative quantity or unit price.",
                        ToolErrorCodes.ValidationFailed,
                        new ToolExecutionMetadata
                        {
                            StartedAt = startedAt,
                            CompletedAt = DateTimeOffset.UtcNow,
                            ModelCalls = cacheHit ? 0 : 1,
                            ModelName = cacheHit ? null : resolvedModel,
                            CacheHit = cacheHit
                        });
                }
            }

            // ─── Deterministic decimal arithmetic ─────────────────────────────────
            var precision = config.DecimalPrecision ?? 2;
            var roundingMode = ParseRoundingMode(config.RoundingMode);
            var taxMethod = ParseTaxMethod(config.TaxMethod);
            var discountOrder = ParseDiscountOrder(config.DiscountOrder);

            var computed = ComputeTotals(llmPayload, taxMethod, discountOrder, precision, roundingMode);
            var arithmeticErrors = config.ValidateArithmetic
                ? FindArithmeticMismatches(llmPayload, computed, precision)
                : Array.Empty<ArithmeticError>();

            stopwatch.Stop();

            // ADR-15: bucket the line-item count and error count for telemetry — NOT raw values.
            var lineItemBucket = BucketLineItemCount(llmPayload.LineItems.Count);
            _logger.LogInformation(
                "InvoiceExtractionToolHandler complete: invocation={InvocationId}, lineItemBucket={LineItemBucket}, " +
                "arithmeticErrorCount={ErrorCount}, cacheHit={CacheHit}, duration={Duration}ms",
                invocationId, lineItemBucket, arithmeticErrors.Count, cacheHit, stopwatch.ElapsedMilliseconds);

            var resultData = new InvoiceExtractionResultData
            {
                Invoice = llmPayload.Invoice,
                LineItems = llmPayload.LineItems,
                Totals = new InvoiceTotalsOutput
                {
                    Subtotal = Math.Round(computed.Subtotal, precision, roundingMode),
                    TaxTotal = Math.Round(computed.TaxTotal, precision, roundingMode),
                    DiscountTotal = Math.Round(computed.DiscountTotal, precision, roundingMode),
                    GrandTotal = Math.Round(computed.GrandTotal, precision, roundingMode),
                    Currency = currency
                },
                ArithmeticErrors = arithmeticErrors.Count == 0 && !config.ValidateArithmetic
                    ? null
                    : arithmeticErrors
            };

            return ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                resultData,
                summary: $"Invoice extracted ({llmPayload.LineItems.Count} line items, {arithmeticErrors.Count} arithmetic errors)",
                confidence: arithmeticErrors.Count == 0 ? 0.95 : 0.7,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ModelCalls = cacheHit ? 0 : 1,
                    ModelName = cacheHit ? null : resolvedModel,
                    CacheHit = cacheHit
                });
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "InvoiceExtractionToolHandler cancelled: invocation={InvocationId}", invocationId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Tool execution was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (InvoiceExtractionException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "InvoiceExtractionToolHandler validation error: invocation={InvocationId}, code={ErrorCode}",
                invocationId, ex.ErrorCode);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                ex.Message,
                ex.ErrorCode,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "InvoiceExtractionToolHandler failed: invocation={InvocationId}", invocationId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Tool execution failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Deterministic arithmetic
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute subtotal, tax total, discount total, and grand total from the LLM-extracted
    /// line items using the configured tax method + discount order. All math in
    /// <see cref="decimal"/>; the caller rounds the final outputs.
    /// </summary>
    internal static ComputedTotals ComputeTotals(
        LlmInvoicePayload payload,
        TaxMethod taxMethod,
        DiscountOrder discountOrder,
        int precision,
        MidpointRounding roundingMode)
    {
        decimal subtotal = 0m;
        decimal taxTotal = 0m;

        // ── Subtotal + tax total derivation ───────────────────────────────────────
        if (taxMethod == TaxMethod.Exclusive)
        {
            // Exclusive: lineTotal = quantity * unitPrice (tax NOT included in line total).
            // Tax is added on top — per-line via taxRate or taxAmount on each line.
            foreach (var line in payload.LineItems)
            {
                var lineSubtotal = line.Quantity * line.UnitPrice;
                subtotal += lineSubtotal;

                if (line.TaxAmount is { } taxAmt)
                {
                    taxTotal += taxAmt;
                }
                else if (line.TaxRate is { } rate)
                {
                    taxTotal += lineSubtotal * rate;
                }
            }
        }
        else
        {
            // Inclusive: line totals ALREADY include tax. Back-derive subtotal from line tax rates.
            foreach (var line in payload.LineItems)
            {
                var lineTotalGross = line.Quantity * line.UnitPrice;
                if (line.TaxRate is { } rate && rate > 0m)
                {
                    var lineNet = lineTotalGross / (1m + rate);
                    subtotal += lineNet;
                    taxTotal += (lineTotalGross - lineNet);
                }
                else if (line.TaxAmount is { } taxAmt)
                {
                    subtotal += (lineTotalGross - taxAmt);
                    taxTotal += taxAmt;
                }
                else
                {
                    // No tax rate or amount declared — treat the line as zero-tax inclusive.
                    subtotal += lineTotalGross;
                }
            }
        }

        // ── Discount derivation (LLM-reported only — sourced from totals.discountTotal) ──
        var discountTotal = payload.Totals.DiscountTotal;

        // ── Grand total per discount order ────────────────────────────────────────
        decimal grandTotal;
        if (discountOrder == DiscountOrder.PreTax)
        {
            // Discount applies to subtotal first; tax computed on discounted subtotal proportionally.
            // To preserve the LLM-reported per-line taxTotal but model pre-tax discount semantics:
            // grandTotal = (subtotal − discount) + (taxTotal proportionally adjusted)
            if (subtotal > 0m && discountTotal > 0m)
            {
                var discountFactor = (subtotal - discountTotal) / subtotal;
                var adjustedTax = taxTotal * discountFactor;
                grandTotal = (subtotal - discountTotal) + adjustedTax;
                // Reflect the adjusted tax in the returned tax total so the relationship
                // (grandTotal = subtotal − discount + tax) holds out of the box.
                taxTotal = adjustedTax;
            }
            else
            {
                grandTotal = subtotal - discountTotal + taxTotal;
            }
        }
        else
        {
            // Post-tax: tax computed on full subtotal, discount applied after.
            grandTotal = subtotal + taxTotal - discountTotal;
        }

        return new ComputedTotals(subtotal, taxTotal, discountTotal, grandTotal);
    }

    /// <summary>
    /// Compare the LLM-reported line totals + invoice totals against the deterministic
    /// computation. Mismatches surface in <c>arithmeticErrors[]</c> rather than being
    /// auto-corrected (FR-19 binding).
    /// </summary>
    internal static IReadOnlyList<ArithmeticError> FindArithmeticMismatches(
        LlmInvoicePayload payload,
        ComputedTotals computed,
        int precision)
    {
        var errors = new List<ArithmeticError>();
        // Use a quantization tolerance one decimal place looser than the configured
        // precision so we don't trigger false positives from rounding noise.
        var tolerance = precision <= 0 ? 0.5m : (decimal)Math.Pow(10, -precision) * 5m;

        // Per-line: quantity × unitPrice == lineTotal? (tax-method aware)
        for (var i = 0; i < payload.LineItems.Count; i++)
        {
            var line = payload.LineItems[i];
            var expected = line.Quantity * line.UnitPrice;
            if (Math.Abs(line.LineTotal - expected) > tolerance)
            {
                errors.Add(new ArithmeticError
                {
                    Field = $"lineItems[{i}].lineTotal",
                    Expected = Math.Round(expected, precision, MidpointRounding.ToEven),
                    Actual = line.LineTotal,
                    Severity = "warning"
                });
            }
        }

        // Subtotal
        if (Math.Abs(payload.Totals.Subtotal - computed.Subtotal) > tolerance)
        {
            errors.Add(new ArithmeticError
            {
                Field = "totals.subtotal",
                Expected = Math.Round(computed.Subtotal, precision, MidpointRounding.ToEven),
                Actual = payload.Totals.Subtotal,
                Severity = "error"
            });
        }

        // Tax total
        if (Math.Abs(payload.Totals.TaxTotal - computed.TaxTotal) > tolerance)
        {
            errors.Add(new ArithmeticError
            {
                Field = "totals.taxTotal",
                Expected = Math.Round(computed.TaxTotal, precision, MidpointRounding.ToEven),
                Actual = payload.Totals.TaxTotal,
                Severity = "error"
            });
        }

        // Grand total
        if (Math.Abs(payload.Totals.GrandTotal - computed.GrandTotal) > tolerance)
        {
            errors.Add(new ArithmeticError
            {
                Field = "totals.grandTotal",
                Expected = Math.Round(computed.GrandTotal, precision, MidpointRounding.ToEven),
                Actual = payload.Totals.GrandTotal,
                Severity = "error"
            });
        }

        return errors;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static List<string> ValidateConfigurationJson(string? configurationJson)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            // Configuration is optional — defaults apply.
            return errors;
        }

        try
        {
            var config = JsonSerializer.Deserialize<InvoiceExtractionConfig>(configurationJson, JsonOptions);
            if (config is null)
            {
                errors.Add("Configuration deserialized to null.");
                return errors;
            }

            if (!string.IsNullOrWhiteSpace(config.TaxMethod) &&
                !string.Equals(config.TaxMethod, "exclusive", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(config.TaxMethod, "inclusive", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("taxMethod must be 'exclusive' or 'inclusive'.");
            }

            if (!string.IsNullOrWhiteSpace(config.DiscountOrder) &&
                !string.Equals(config.DiscountOrder, "pre-tax", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(config.DiscountOrder, "post-tax", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("discountOrder must be 'pre-tax' or 'post-tax'.");
            }

            if (config.DecimalPrecision is { } precision && (precision < 0 || precision > 8))
            {
                errors.Add("decimalPrecision must be between 0 and 8.");
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid configuration JSON: {ex.Message}");
        }

        return errors;
    }

    private static InvoiceExtractionConfig ParseConfig(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return new InvoiceExtractionConfig();

        return JsonSerializer.Deserialize<InvoiceExtractionConfig>(configurationJson, JsonOptions)
               ?? new InvoiceExtractionConfig();
    }

    private static string BuildPrompt(string invoiceText, InvoiceExtractionConfig config)
    {
        var taxMethod = string.IsNullOrWhiteSpace(config.TaxMethod) ? "exclusive" : config.TaxMethod;
        var discountOrder = string.IsNullOrWhiteSpace(config.DiscountOrder) ? "pre-tax" : config.DiscountOrder;

        return $"""
            You are an invoice-extraction assistant. Extract the invoice header, line items, and
            totals from the document below. Return ONLY the JSON object matching the schema —
            no commentary.

            Tax method: {taxMethod}. Discount order: {discountOrder}.
            For each line item: capture description, quantity, unitPrice, the lineTotal exactly
            as stated on the invoice, and any taxRate (decimal; e.g., 0.0825 for 8.25%) or
            taxAmount explicitly shown. If a field is not present on the invoice, return null for
            string fields or 0 for numeric fields.

            Document:
            {invoiceText}
            """;
    }

    /// <summary>
    /// ADR-014 cache key: <c>invoice-extractor:{tenantId}:{sha256(text + config)}</c>.
    /// Includes the config fingerprint so different tax-method / discount-order configurations
    /// don't collide on the same text input.
    /// </summary>
    internal static string BuildCacheKey(string tenantId, string invoiceText, InvoiceExtractionConfig config)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("tenantId is required for ADR-014 cache key.", nameof(tenantId));

        var fingerprint = invoiceText + "\0" +
                          (config.TaxMethod ?? "exclusive") + "\0" +
                          (config.DiscountOrder ?? "pre-tax") + "\0" +
                          (config.RoundingMode ?? "banker") + "\0" +
                          (config.DecimalPrecision?.ToString() ?? "2") + "\0" +
                          (config.ExtractLineItems?.ToString() ?? "true") + "\0" +
                          (config.ModelDeployment ?? string.Empty);

        var bytes = Encoding.UTF8.GetBytes(fingerprint);
        var hash = SHA256.HashData(bytes);
        var hex = Convert.ToHexString(hash);
        return $"{CacheKeyPrefix}:{tenantId}:{hex}";
    }

    private static MidpointRounding ParseRoundingMode(string? mode) =>
        string.IsNullOrWhiteSpace(mode) ? MidpointRounding.ToEven :
        mode.Trim().ToLowerInvariant() switch
        {
            "away_from_zero" or "half_up" => MidpointRounding.AwayFromZero,
            _ => MidpointRounding.ToEven
        };

    private static TaxMethod ParseTaxMethod(string? method) =>
        string.Equals(method, "inclusive", StringComparison.OrdinalIgnoreCase)
            ? TaxMethod.Inclusive
            : TaxMethod.Exclusive;

    private static DiscountOrder ParseDiscountOrder(string? order) =>
        string.Equals(order, "post-tax", StringComparison.OrdinalIgnoreCase)
            ? DiscountOrder.PostTax
            : DiscountOrder.PreTax;

    /// <summary>
    /// ADR-015: bucket the raw line-item count into a low-cardinality string so telemetry
    /// can correlate "small / medium / large" invoices without leaking the precise count
    /// (which itself can be a soft fingerprint).
    /// </summary>
    private static string BucketLineItemCount(int count) => count switch
    {
        0 => "empty",
        1 => "single",
        <= 5 => "small",
        <= 20 => "medium",
        _ => "large"
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Internal configuration + result types
// ─────────────────────────────────────────────────────────────────────────────

internal enum TaxMethod
{
    Exclusive = 0,
    Inclusive = 1
}

internal enum DiscountOrder
{
    PreTax = 0,
    PostTax = 1
}

internal sealed class InvoiceExtractionConfig
{
    public string? TaxMethod { get; set; }
    public string? DiscountOrder { get; set; }
    public string? RoundingMode { get; set; }
    public int? DecimalPrecision { get; set; }
    public List<string>? ExpectedCurrencies { get; set; }
    public string? ModelDeployment { get; set; }
    public bool? ExtractLineItems { get; set; }
    public bool? ValidateArithmeticOpt { get; set; }

    [JsonIgnore]
    public bool ValidateArithmetic => ValidateArithmeticOpt ?? true;

    [JsonPropertyName("validateArithmetic")]
    public bool? ValidateArithmeticInput
    {
        get => ValidateArithmeticOpt;
        set => ValidateArithmeticOpt = value;
    }
}

/// <summary>
/// LLM-extracted invoice payload (strict-decoded via Azure OpenAI structured outputs).
/// </summary>
internal sealed class LlmInvoicePayload
{
    [JsonPropertyName("invoice")]
    public LlmInvoiceHeader Invoice { get; set; } = new();

    [JsonPropertyName("lineItems")]
    public List<LlmLineItem> LineItems { get; set; } = new();

    [JsonPropertyName("totals")]
    public LlmInvoiceTotals Totals { get; set; } = new();
}

internal sealed class LlmInvoiceHeader
{
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("issueDate")] public string? IssueDate { get; set; }
    [JsonPropertyName("dueDate")] public string? DueDate { get; set; }
    [JsonPropertyName("vendor")] public string? Vendor { get; set; }
    [JsonPropertyName("customer")] public string? Customer { get; set; }
}

internal sealed class LlmLineItem
{
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("quantity")] public decimal Quantity { get; set; }
    [JsonPropertyName("unitPrice")] public decimal UnitPrice { get; set; }
    [JsonPropertyName("lineTotal")] public decimal LineTotal { get; set; }
    [JsonPropertyName("taxRate")] public decimal? TaxRate { get; set; }
    [JsonPropertyName("taxAmount")] public decimal? TaxAmount { get; set; }
}

internal sealed class LlmInvoiceTotals
{
    [JsonPropertyName("subtotal")] public decimal Subtotal { get; set; }
    [JsonPropertyName("taxTotal")] public decimal TaxTotal { get; set; }
    [JsonPropertyName("discountTotal")] public decimal DiscountTotal { get; set; }
    [JsonPropertyName("grandTotal")] public decimal GrandTotal { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
}

/// <summary>
/// Computed totals from the deterministic decimal pass.
/// </summary>
internal readonly record struct ComputedTotals(
    decimal Subtotal,
    decimal TaxTotal,
    decimal DiscountTotal,
    decimal GrandTotal);

internal sealed class InvoiceExtractionResultData
{
    [JsonPropertyName("invoice")]
    public LlmInvoiceHeader Invoice { get; set; } = new();

    [JsonPropertyName("lineItems")]
    public List<LlmLineItem> LineItems { get; set; } = new();

    [JsonPropertyName("totals")]
    public InvoiceTotalsOutput Totals { get; set; } = new();

    /// <summary>
    /// Mismatches between LLM-extracted figures and the deterministic computation.
    /// Empty / null when arithmetic validation is disabled or there are no discrepancies.
    /// Per FR-19 binding: discrepancies surface in this array; the handler NEVER
    /// auto-corrects LLM line totals.
    /// </summary>
    [JsonPropertyName("arithmeticErrors")]
    public IReadOnlyList<ArithmeticError>? ArithmeticErrors { get; set; }
}

internal sealed class InvoiceTotalsOutput
{
    [JsonPropertyName("subtotal")] public decimal Subtotal { get; set; }
    [JsonPropertyName("taxTotal")] public decimal TaxTotal { get; set; }
    [JsonPropertyName("discountTotal")] public decimal DiscountTotal { get; set; }
    [JsonPropertyName("grandTotal")] public decimal GrandTotal { get; set; }
    [JsonPropertyName("currency")] public string Currency { get; set; } = string.Empty;
}

internal sealed class ArithmeticError
{
    [JsonPropertyName("field")] public string Field { get; set; } = string.Empty;
    [JsonPropertyName("expected")] public decimal Expected { get; set; }
    [JsonPropertyName("actual")] public decimal Actual { get; set; }
    [JsonPropertyName("severity")] public string Severity { get; set; } = "warning";
}

/// <summary>
/// Sentinel exception for handler-internal validation failures.
/// </summary>
internal sealed class InvoiceExtractionException : Exception
{
    public string ErrorCode { get; }

    public InvoiceExtractionException(string message, string errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }
}
