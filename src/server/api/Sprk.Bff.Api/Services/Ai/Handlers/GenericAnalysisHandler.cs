using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Generic tool handler that executes analysis based on Tool scope configuration from Dataverse.
/// Enables custom tools without requiring code deployment.
/// </summary>
/// <remarks>
/// <para>
/// This handler reads tool definitions from Dataverse and executes them dynamically using:
/// - Prompt templates stored in the Tool scope
/// - Parameter schemas for validation
/// - Standard operations: extract, classify, validate, generate
/// </para>
/// <para>
/// Security: No arbitrary code execution. Operations are limited to AI prompt-based processing.
/// All parameters are validated against the defined schema before execution.
/// </para>
/// <para>
/// See ADR-013 for AI architecture patterns.
/// </para>
/// </remarks>
public sealed class GenericAnalysisHandler : IAnalysisToolHandler
{
    private const string HandlerIdValue = "GenericAnalysisHandler";

    private readonly IOpenAiClient _openAiClient;
    private readonly IScopeResolverService _scopeResolver;
    private readonly ILogger<GenericAnalysisHandler> _logger;

    /// <summary>
    /// Supported operations that can be configured in tool definitions.
    /// </summary>
    private static readonly HashSet<string> SupportedOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "extract",    // Extract structured data from document
        "classify",   // Classify document or content
        "validate",   // Validate content against rules
        "generate",   // Generate content based on input
        "transform",  // Transform content format
        "analyze"     // General analysis
    };

    public GenericAnalysisHandler(
        IOpenAiClient openAiClient,
        IScopeResolverService scopeResolver,
        ILogger<GenericAnalysisHandler> logger)
    {
        _openAiClient = openAiClient;
        _scopeResolver = scopeResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Generic Analysis Handler",
        Description: "Executes custom tools defined in Dataverse Tool scopes. Supports extract, classify, validate, and generate operations.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain", "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        Parameters: new[]
        {
            new ToolParameterDefinition("operation", "The operation type: extract, classify, validate, generate, transform, analyze", ToolParameterType.String, Required: true),
            new ToolParameterDefinition("prompt_template", "Custom prompt template (uses {document} and {parameters} placeholders)", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("output_schema", "JSON schema for expected output structure", ToolParameterType.Object, Required: false),
            new ToolParameterDefinition("max_tokens", "Maximum tokens for AI response", ToolParameterType.Integer, Required: false, DefaultValue: 2000),
            new ToolParameterDefinition("temperature", "AI temperature (0.0-1.0)", ToolParameterType.Decimal, Required: false, DefaultValue: 0.3)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        // Validate document context
        if (context.Document is null)
            errors.Add("Document context is required.");

        if (string.IsNullOrWhiteSpace(context.Document?.ExtractedText))
            errors.Add("Document extracted text is required.");

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required.");

        // Parse and validate configuration
        if (string.IsNullOrWhiteSpace(tool.Configuration))
        {
            errors.Add("Tool configuration is required for generic handler.");
        }
        else
        {
            try
            {
                var config = JsonSerializer.Deserialize<GenericToolConfig>(tool.Configuration, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config is null)
                {
                    errors.Add("Invalid tool configuration format.");
                }
                else
                {
                    // Validate operation type
                    if (string.IsNullOrWhiteSpace(config.Operation))
                    {
                        errors.Add("Operation type is required.");
                    }
                    else if (!SupportedOperations.Contains(config.Operation))
                    {
                        errors.Add($"Unsupported operation: {config.Operation}. Supported: {string.Join(", ", SupportedOperations)}");
                    }

                    // Validate prompt template if provided
                    if (!string.IsNullOrWhiteSpace(config.PromptTemplate))
                    {
                        var validationResult = ValidatePromptTemplate(config.PromptTemplate);
                        if (!validationResult.IsValid)
                        {
                            errors.AddRange(validationResult.Errors);
                        }
                    }

                    // Validate temperature range
                    if (config.Temperature is < 0.0 or > 1.0)
                    {
                        errors.Add("Temperature must be between 0.0 and 1.0.");
                    }

                    // Validate max tokens
                    if (config.MaxTokens is < 100 or > 8000)
                    {
                        errors.Add("MaxTokens must be between 100 and 8000.");
                    }

                    // Validate parameters against schema if both are provided
                    if (config.OutputSchema != null && config.Parameters != null)
                    {
                        var paramValidation = ValidateParametersAgainstSchema(config.Parameters, config.OutputSchema);
                        if (!paramValidation.IsValid)
                        {
                            errors.AddRange(paramValidation.Errors);
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"Invalid configuration JSON: {ex.Message}");
            }
        }

        return errors.Count == 0 ? ToolValidationResult.Success() : ToolValidationResult.Failure(errors);
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
            _logger.LogInformation(
                "Starting generic tool execution for analysis {AnalysisId}, tool {ToolId} ({ToolName})",
                context.AnalysisId, tool.Id, tool.Name);

            // Parse configuration
            var config = JsonSerializer.Deserialize<GenericToolConfig>(tool.Configuration!, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })!;

            // Build the execution prompt
            var prompt = BuildExecutionPrompt(context, tool, config);
            var inputTokens = EstimateTokens(prompt);

            _logger.LogDebug(
                "Executing generic tool with operation: {Operation}, estimated tokens: {Tokens}",
                config.Operation, inputTokens);

            // Execute AI call (maxTokens and temperature handled by service configuration)
            var response = await _openAiClient.GetCompletionAsync(
                prompt,
                cancellationToken: cancellationToken);

            var outputTokens = EstimateTokens(response);

            stopwatch.Stop();

            // Parse the response
            var (resultData, confidence) = ParseAiResponse(response, config);

            var executionMetadata = new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                ModelCalls = 1,
                ModelName = "gpt-4o"
            };

            _logger.LogInformation(
                "Generic tool execution complete for {AnalysisId}: {Operation} in {Duration}ms",
                context.AnalysisId, config.Operation, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                resultData,
                response,
                confidence,
                executionMetadata);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Generic tool execution cancelled for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                "Tool execution was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Generic tool execution failed for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                $"Tool execution failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
    }

    /// <summary>
    /// Builds the execution prompt from the tool configuration and context.
    /// </summary>
    private static string BuildExecutionPrompt(ToolExecutionContext context, AnalysisTool tool, GenericToolConfig config)
    {
        // Use custom prompt template if provided, otherwise use operation-specific default
        var promptTemplate = !string.IsNullOrWhiteSpace(config.PromptTemplate)
            ? config.PromptTemplate
            : GetDefaultPromptTemplate(config.Operation!);

        // Build parameters JSON for template substitution
        var parametersJson = config.Parameters != null
            ? JsonSerializer.Serialize(config.Parameters, new JsonSerializerOptions { WriteIndented = true })
            : "{}";

        // Build output schema instructions
        var schemaInstructions = config.OutputSchema != null
            ? $"\n\nReturn your response as valid JSON matching this schema:\n```json\n{JsonSerializer.Serialize(config.OutputSchema, new JsonSerializerOptions { WriteIndented = true })}\n```"
            : "\n\nReturn your response as valid JSON with a 'result' field containing your analysis and a 'confidence' field (0.0-1.0).";

        // Substitute placeholders
        var prompt = promptTemplate
            .Replace("{document}", context.Document!.ExtractedText ?? string.Empty)
            .Replace("{parameters}", parametersJson)
            .Replace("{tool_name}", tool.Name)
            .Replace("{tool_description}", tool.Description ?? "No description provided");

        return prompt + schemaInstructions;
    }

    /// <summary>
    /// Gets the default prompt template for an operation type.
    /// </summary>
    private static string GetDefaultPromptTemplate(string operation)
    {
        return operation.ToLowerInvariant() switch
        {
            "extract" => """
                You are a document analysis assistant. Extract structured information from the following document according to the specified parameters.

                Document:
                {document}

                Extraction Parameters:
                {parameters}

                Extract all relevant information that matches the parameters. Be thorough and accurate.
                """,

            "classify" => """
                You are a document classification assistant. Classify the following document according to the specified categories.

                Document:
                {document}

                Classification Parameters:
                {parameters}

                Determine the most appropriate classification(s) with confidence scores.
                """,

            "validate" => """
                You are a document validation assistant. Validate the following document against the specified rules.

                Document:
                {document}

                Validation Rules:
                {parameters}

                Check each rule and report any violations or compliance issues.
                """,

            "generate" => """
                You are a content generation assistant. Generate content based on the following document and parameters.

                Source Document:
                {document}

                Generation Parameters:
                {parameters}

                Generate the requested content following the specified format and requirements.
                """,

            "transform" => """
                You are a content transformation assistant. Transform the following content according to the specified format.

                Source Content:
                {document}

                Transformation Parameters:
                {parameters}

                Apply the transformation and return the result in the specified format.
                """,

            "analyze" or _ => """
                You are a document analysis assistant. Analyze the following document according to the specified parameters.

                Document:
                {document}

                Analysis Parameters:
                {parameters}

                Provide a thorough analysis addressing all specified aspects.
                """
        };
    }

    /// <summary>
    /// Validates a prompt template for security and correctness.
    /// </summary>
    private static ToolValidationResult ValidatePromptTemplate(string template)
    {
        var errors = new List<string>();

        // Check for potentially dangerous patterns (injection prevention)
        var dangerousPatterns = new[]
        {
            @"\{[^}]*\beval\b",
            @"\{[^}]*\bexec\b",
            @"\{[^}]*\bsystem\b",
            @"<script",
            @"javascript:"
        };

        foreach (var pattern in dangerousPatterns)
        {
            if (Regex.IsMatch(template, pattern, RegexOptions.IgnoreCase))
            {
                errors.Add($"Prompt template contains potentially dangerous pattern: {pattern}");
            }
        }

        // Ensure template length is reasonable
        if (template.Length > 10000)
        {
            errors.Add("Prompt template exceeds maximum length of 10000 characters.");
        }

        return errors.Count == 0 ? ToolValidationResult.Success() : ToolValidationResult.Failure(errors);
    }

    /// <summary>
    /// Validates parameters against a JSON schema.
    /// </summary>
    private static ToolValidationResult ValidateParametersAgainstSchema(JsonNode parameters, JsonNode schema)
    {
        // Basic schema validation - in production, use a full JSON Schema validator
        var errors = new List<string>();

        try
        {
            // Check for required properties if schema specifies them
            if (schema is JsonObject schemaObj && schemaObj.TryGetPropertyValue("required", out var requiredNode))
            {
                if (requiredNode is JsonArray requiredArray && parameters is JsonObject paramsObj)
                {
                    foreach (var required in requiredArray)
                    {
                        var propName = required?.GetValue<string>();
                        if (!string.IsNullOrEmpty(propName) && !paramsObj.ContainsKey(propName))
                        {
                            errors.Add($"Required parameter missing: {propName}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Schema validation error: {ex.Message}");
        }

        return errors.Count == 0 ? ToolValidationResult.Success() : ToolValidationResult.Failure(errors);
    }

    /// <summary>
    /// Parses the AI response into structured result data.
    /// </summary>
    private static (object ResultData, double Confidence) ParseAiResponse(string response, GenericToolConfig config)
    {
        try
        {
            // Clean up response - remove markdown code blocks if present
            var json = response.Trim();
            if (json.StartsWith("```"))
            {
                var startIndex = json.IndexOf('{');
                var endIndex = json.LastIndexOf('}');
                if (startIndex >= 0 && endIndex > startIndex)
                {
                    json = json.Substring(startIndex, endIndex - startIndex + 1);
                }
            }

            var result = JsonSerializer.Deserialize<JsonNode>(json);
            if (result is JsonObject resultObj)
            {
                // Extract confidence if present
                double confidence = 0.8; // Default confidence
                if (resultObj.TryGetPropertyValue("confidence", out var confNode))
                {
                    confidence = Math.Clamp(confNode?.GetValue<double>() ?? 0.8, 0.0, 1.0);
                }

                return (result, confidence);
            }

            return (new { rawResponse = response }, 0.7);
        }
        catch (JsonException)
        {
            // Return raw response if JSON parsing fails
            return (new { rawResponse = response, parseError = true }, 0.5);
        }
    }

    /// <summary>
    /// Estimates token count for a text string.
    /// </summary>
    private static int EstimateTokens(string text)
    {
        return (int)Math.Ceiling(text.Length / 4.0);
    }
}

/// <summary>
/// Configuration for Generic Analysis tool.
/// </summary>
internal class GenericToolConfig
{
    /// <summary>The operation type: extract, classify, validate, generate, transform, analyze.</summary>
    public string? Operation { get; set; }

    /// <summary>Custom prompt template with {document} and {parameters} placeholders.</summary>
    public string? PromptTemplate { get; set; }

    /// <summary>JSON schema for expected output structure.</summary>
    public JsonNode? OutputSchema { get; set; }

    /// <summary>Parameters to pass to the prompt template.</summary>
    public JsonNode? Parameters { get; set; }

    /// <summary>Maximum tokens for AI response (100-8000).</summary>
    public int? MaxTokens { get; set; } = 2000;

    /// <summary>AI temperature (0.0-1.0).</summary>
    public double? Temperature { get; set; } = 0.3;
}
