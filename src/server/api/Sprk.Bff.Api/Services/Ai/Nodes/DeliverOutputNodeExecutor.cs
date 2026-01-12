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
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.Node.ConfigJson))
        {
            errors.Add("DeliverOutput node requires configuration (ConfigJson)");
            return NodeValidationResult.Failure(errors.ToArray());
        }

        try
        {
            var config = JsonSerializer.Deserialize<DeliveryNodeConfig>(context.Node.ConfigJson, JsonOptions);
            if (config is null)
            {
                errors.Add("Failed to parse delivery configuration");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(config.DeliveryType))
                {
                    errors.Add("Delivery type is required (json, text, html, markdown)");
                }
                else if (!IsValidDeliveryType(config.DeliveryType))
                {
                    errors.Add($"Invalid delivery type: {config.DeliveryType}. Must be: json, text, html, or markdown");
                }

                // Template is optional for JSON delivery type (auto-assembles from outputs)
                if (config.DeliveryType != "json" && string.IsNullOrWhiteSpace(config.Template))
                {
                    errors.Add("Template is required for non-JSON delivery types");
                }
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid delivery configuration JSON: {ex.Message}");
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

            // Parse configuration
            var config = JsonSerializer.Deserialize<DeliveryNodeConfig>(context.Node.ConfigJson!, JsonOptions)!;

            // Build template context from previous outputs
            var templateContext = BuildTemplateContext(context);

            // Render output based on delivery type
            await Task.CompletedTask; // Placeholder for future async operations (e.g., file export)
            var (textOutput, structuredOutput) = config.DeliveryType!.ToLowerInvariant() switch
            {
                "json" => RenderJsonOutput(context, config, templateContext),
                "text" => RenderTextOutput(config, templateContext),
                "html" => RenderHtmlOutput(config, templateContext),
                "markdown" => RenderMarkdownOutput(config, templateContext),
                _ => throw new InvalidOperationException($"Unsupported delivery type: {config.DeliveryType}")
            };

            // Apply output format constraints
            if (config.OutputFormat?.MaxLength.HasValue == true && textOutput.Length > config.OutputFormat.MaxLength)
            {
                textOutput = textOutput[..config.OutputFormat.MaxLength.Value] + "...(truncated)";
            }

            _logger.LogInformation(
                "DeliverOutput node {NodeId} completed - rendered {DeliveryType} output ({Length} chars)",
                context.Node.Id,
                config.DeliveryType,
                textOutput.Length);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                structuredOutput,
                textContent: textOutput,
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
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
            outputData["_metadata"] = new
            {
                playbookId = context.PlaybookId,
                runId = context.RunId,
                nodeId = context.Node.Id,
                generatedAt = DateTimeOffset.UtcNow,
                nodeCount = context.PreviousOutputs.Count
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
    /// Builds template context dictionary from previous node outputs.
    /// </summary>
    private static Dictionary<string, object?> BuildTemplateContext(NodeExecutionContext context)
    {
        var templateContext = new Dictionary<string, object?>();

        foreach (var (varName, output) in context.PreviousOutputs)
        {
            templateContext[varName] = new
            {
                output = output.StructuredData.HasValue
                    ? JsonSerializer.Deserialize<object>(output.StructuredData.Value.GetRawText())
                    : null,
                text = output.TextContent,
                success = output.Success
            };
        }

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
