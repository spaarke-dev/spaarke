using System.Text.Json;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for rendering and delivering final playbook outputs.
/// Assembles outputs from previous nodes into final deliverables.
/// </summary>
/// <remarks>
/// <para>
/// Delivery configuration is read from node.ConfigJson with structure:
/// </para>
/// <code>
/// {
///   "deliveryType": "json|text|html|markdown",
///   "template": "## Summary\n{{summarize.output.summary}}\n\n## Parties\n{{extract_entities.output.parties}}",
///   "outputFormat": {
///     "includeMetadata": true,
///     "includeSourceCitations": false,
///     "maxLength": 10000
///   }
/// }
/// </code>
/// <para>
/// This is typically the final node in a playbook that assembles
/// all analysis results into a user-facing deliverable.
/// </para>
/// </remarks>
public sealed class DeliverOutputNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ITemplateEngine _templateEngine;
    private readonly ILogger<DeliverOutputNodeExecutor> _logger;

    public DeliverOutputNodeExecutor(
        ITemplateEngine templateEngine,
        ILogger<DeliverOutputNodeExecutor> logger)
    {
        _templateEngine = templateEngine;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[]
    {
        ActionType.DeliverOutput
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        // When no explicit delivery config is present, default to auto-assembly mode
        var config = ParseConfigOrDefault(context.Node.ConfigJson);
        if (config == null)
            return NodeValidationResult.Success(); // auto-assembly mode

        var errors = new List<string>();

        if (!string.IsNullOrWhiteSpace(config.DeliveryType) && !IsValidDeliveryType(config.DeliveryType))
        {
            errors.Add($"Invalid delivery type: {config.DeliveryType}. Must be: json, text, html, or markdown");
        }

        return errors.Count > 0
            ? NodeValidationResult.Failure(errors.ToArray())
            : NodeValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogDebug(
            "Executing DeliverOutput node {NodeId} ({NodeName})",
            context.Node.Id,
            context.Node.Name);

        try
        {
            // Validate first
            var validation = Validate(context);
            if (!validation.IsValid)
            {
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    string.Join("; ", validation.Errors),
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Parse configuration (may be null/minimal — use auto-assembly defaults)
            var config = ParseConfigOrDefault(context.Node.ConfigJson)
                ?? new DeliveryNodeConfig { DeliveryType = "markdown" };

            var effectiveDeliveryType = string.IsNullOrWhiteSpace(config.DeliveryType)
                ? "markdown"
                : config.DeliveryType;

            // Build template context from previous outputs
            var templateContext = BuildTemplateContext(context);

            // Render output based on delivery type
            await Task.CompletedTask; // Placeholder for future async operations (e.g., file export)
            string textOutput;
            object structuredOutput;

            if (!string.IsNullOrWhiteSpace(config.Template))
            {
                // Explicit template provided — render it
                (textOutput, structuredOutput) = effectiveDeliveryType.ToLowerInvariant() switch
                {
                    "json" => RenderJsonOutput(context, config, templateContext),
                    "text" => RenderTextOutput(config, templateContext),
                    "html" => RenderHtmlOutput(config, templateContext),
                    "markdown" => RenderMarkdownOutput(config, templateContext),
                    _ => RenderMarkdownOutput(config, templateContext)
                };
            }
            else
            {
                // Auto-assembly: combine all previous outputs into markdown
                (textOutput, structuredOutput) = AutoAssembleOutputs(context);
            }

            // Apply output format constraints
            if (config.OutputFormat?.MaxLength.HasValue == true && textOutput.Length > config.OutputFormat.MaxLength)
            {
                textOutput = textOutput[..config.OutputFormat.MaxLength.Value] + "...(truncated)";
            }

            _logger.LogInformation(
                "DeliverOutput node {NodeId} completed - rendered {DeliveryType} output ({Length} chars)",
                context.Node.Id,
                effectiveDeliveryType,
                textOutput.Length);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                structuredOutput,
                textContent: textOutput,
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow))
                with { IsDeliverOutput = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "DeliverOutput node {NodeId} failed: {ErrorMessage}",
                context.Node.Id,
                ex.Message);

            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Failed to render output: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    private static bool IsValidDeliveryType(string deliveryType)
    {
        return deliveryType.ToLowerInvariant() switch
        {
            "json" or "text" or "html" or "markdown" => true,
            _ => false
        };
    }

    private (string textOutput, object structuredOutput) RenderJsonOutput(
        NodeExecutionContext context,
        DeliveryNodeConfig config,
        Dictionary<string, object?> templateContext)
    {
        // For JSON, assemble all previous outputs into a structured response
        var outputData = new Dictionary<string, object?>();

        // Include metadata if requested
        if (config.OutputFormat?.IncludeMetadata == true)
        {
            // Calculate overall confidence from all successful node outputs
            var confidences = context.PreviousOutputs
                .Where(kvp => kvp.Value.Success && kvp.Value.Confidence.HasValue)
                .Select(kvp => kvp.Value.Confidence!.Value)
                .ToList();

            var overallConfidence = confidences.Count > 0
                ? Math.Round(confidences.Average(), 2)
                : (double?)null;

            outputData["_metadata"] = new
            {
                playbookId = context.PlaybookId,
                runId = context.RunId,
                nodeId = context.Node.Id,
                generatedAt = DateTimeOffset.UtcNow,
                nodeCount = context.PreviousOutputs.Count,
                overallConfidence
            };
        }

        // Assemble outputs from all previous nodes
        foreach (var (varName, output) in context.PreviousOutputs)
        {
            if (output.Success && output.StructuredData.HasValue)
            {
                outputData[varName] = JsonSerializer.Deserialize<object>(output.StructuredData.Value.GetRawText());
            }
            else if (output.Success && !string.IsNullOrWhiteSpace(output.TextContent))
            {
                outputData[varName] = output.TextContent;
            }
        }

        // If a template is provided, use it to filter/transform the output
        if (!string.IsNullOrWhiteSpace(config.Template))
        {
            var rendered = _templateEngine.Render(config.Template, templateContext);
            try
            {
                // Try to parse as JSON for structured output
                var parsed = JsonSerializer.Deserialize<object>(rendered);
                return (rendered, parsed ?? outputData);
            }
            catch
            {
                // If not valid JSON, include as text
                outputData["_rendered"] = rendered;
            }
        }

        var jsonText = JsonSerializer.Serialize(outputData, new JsonSerializerOptions { WriteIndented = true });
        return (jsonText, outputData);
    }

    private (string textOutput, object structuredOutput) RenderTextOutput(
        DeliveryNodeConfig config,
        Dictionary<string, object?> templateContext)
    {
        var rendered = _templateEngine.Render(config.Template!, templateContext);
        return (rendered, new { content = rendered, format = "text" });
    }

    private (string textOutput, object structuredOutput) RenderHtmlOutput(
        DeliveryNodeConfig config,
        Dictionary<string, object?> templateContext)
    {
        var rendered = _templateEngine.Render(config.Template!, templateContext);
        return (rendered, new { content = rendered, format = "html" });
    }

    private (string textOutput, object structuredOutput) RenderMarkdownOutput(
        DeliveryNodeConfig config,
        Dictionary<string, object?> templateContext)
    {
        var rendered = _templateEngine.Render(config.Template!, templateContext);
        return (rendered, new { content = rendered, format = "markdown" });
    }

    /// <summary>
    /// Parses delivery config from ConfigJson, returning null if absent or unparseable.
    /// </summary>
    private static DeliveryNodeConfig? ParseConfigOrDefault(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            var config = JsonSerializer.Deserialize<DeliveryNodeConfig>(configJson, JsonOptions);
            // If deliveryType is missing, treat as unconfigured
            return config;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Auto-assembles all previous node outputs into a markdown document.
    /// Used when no explicit delivery template is configured.
    /// </summary>
    private static (string textOutput, object structuredOutput) AutoAssembleOutputs(NodeExecutionContext context)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var (varName, output) in context.PreviousOutputs)
        {
            if (!output.Success)
                continue;

            if (!string.IsNullOrWhiteSpace(output.TextContent))
            {
                sb.AppendLine(output.TextContent);
                sb.AppendLine();
            }
            else if (output.StructuredData.HasValue)
            {
                sb.AppendLine($"### {varName}");
                sb.AppendLine();
                sb.AppendLine(output.StructuredData.Value.GetRawText());
                sb.AppendLine();
            }
        }

        var text = sb.ToString().TrimEnd();
        return (text, new { content = text, format = "markdown" });
    }

    /// <summary>
    /// Builds template context dictionary from previous node outputs and execution metadata.
    /// </summary>
    private static Dictionary<string, object?> BuildTemplateContext(NodeExecutionContext context)
    {
        var templateContext = new Dictionary<string, object?>();

        // Add previous node outputs (e.g., {{analyze.text}}, {{analyze.output.summary}})
        foreach (var (varName, output) in context.PreviousOutputs)
        {
            templateContext[varName] = new
            {
                output = output.StructuredData.HasValue
                    ? TemplateEngine.ConvertJsonElement(output.StructuredData.Value)
                    : null,
                text = output.TextContent,
                success = output.Success
            };
        }

        // Add document context (e.g., {{document.id}}, {{document.name}})
        if (context.Document is not null)
        {
            templateContext["document"] = new
            {
                id = context.Document.DocumentId.ToString(),
                name = context.Document.Name,
                fileName = context.Document.FileName
            };
        }

        // Add run context (e.g., {{run.id}}, {{run.playbookId}})
        templateContext["run"] = new
        {
            id = context.RunId.ToString(),
            playbookId = context.PlaybookId.ToString(),
            tenantId = context.TenantId
        };

        return templateContext;
    }
}

/// <summary>
/// Configuration for DeliverOutput node from ConfigJson.
/// </summary>
internal sealed record DeliveryNodeConfig
{
    public string? DeliveryType { get; init; }
    public string? Template { get; init; }
    public DeliveryOutputFormat? OutputFormat { get; init; }

    // R2: Typed output dispatch fields
    public string? OutputType { get; init; }
    public string? TargetPage { get; init; }
    public Dictionary<string, string>? PrePopulateFields { get; init; }
    public bool? RequiresConfirmation { get; init; }
}

/// <summary>
/// Output format constraints for delivery node.
/// </summary>
internal sealed record DeliveryOutputFormat
{
    public bool IncludeMetadata { get; init; }
    public bool IncludeSourceCitations { get; init; }
    public int? MaxLength { get; init; }
}
