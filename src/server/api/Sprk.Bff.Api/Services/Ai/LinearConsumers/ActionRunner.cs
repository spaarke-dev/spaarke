using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// <see cref="IActionRunner"/> implementation that wraps
/// <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/>.
/// </summary>
/// <remarks>
/// Registered as Singleton — stateless, dependencies are Singleton-safe.
/// </remarks>
public sealed class ActionRunner : IActionRunner
{
    private readonly IOpenAiClient _openAi;
    private readonly IOptions<LinearConsumersOptions> _options;
    private readonly ILogger<ActionRunner> _logger;

    private const string PlaceholderExtractedText = "{{document.extractedText}}";

    public ActionRunner(
        IOpenAiClient openAi,
        IOptions<LinearConsumersOptions> options,
        ILogger<ActionRunner> logger)
    {
        _openAi = openAi;
        _options = options;
        _logger = logger;
    }

    public async Task<JsonElement> RunAsync(
        AnalysisAction action,
        DocumentText documentText,
        LinearRunContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.SystemPrompt))
        {
            throw new InvalidOperationException(
                $"Action {action.Id} ({action.Name}) has empty SystemPrompt.");
        }

        if (string.IsNullOrWhiteSpace(action.OutputSchemaJson))
        {
            throw new InvalidOperationException(
                $"Action {action.Id} ({action.Name}) has empty OutputSchemaJson; " +
                $"linear consumers require a constrained-decoding schema.");
        }

        if (string.IsNullOrWhiteSpace(documentText.ExtractedText))
        {
            throw new InvalidOperationException(
                $"DocumentText for {documentText.FileName} is empty; nothing to send to the LLM.");
        }

        var prompt = BindPrompt(action.SystemPrompt, documentText);
        var jsonSchema = BinaryData.FromString(action.OutputSchemaJson);
        var schemaName = SanitizeSchemaName(action.Name);
        var temperature = (float?)action.Temperature;

        _options.Value.TryGetModelDeployment(context.ConsumerType, out var modelDeployment);

        _logger.LogInformation(
            "Linear run: consumer={ConsumerType} action={ActionName} promptLen={PromptLen} model={Model} temp={Temperature}",
            context.ConsumerType, action.Name, prompt.Length, modelDeployment ?? "(default)", temperature);

        var rawJson = await _openAi.GetStructuredCompletionRawAsync(
            prompt,
            jsonSchema,
            schemaName,
            model: modelDeployment,
            maxOutputTokens: null,
            temperature: temperature,
            cancellationToken: cancellationToken);

        using var doc = JsonDocument.Parse(rawJson);
        // Clone so the JsonElement remains valid after `doc` is disposed.
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Replace the single supported placeholder <c>{{document.extractedText}}</c>
    /// with the extracted text. Kept intentionally simple — the linear path has
    /// no template engine.
    /// </summary>
    private static string BindPrompt(string systemPrompt, DocumentText documentText)
    {
        if (!systemPrompt.Contains(PlaceholderExtractedText, StringComparison.Ordinal))
        {
            // No placeholder in the Action's SystemPrompt — append the text after a
            // separator. Preserves compat with Actions whose author expected an
            // engine to append the extracted text as an input section.
            return systemPrompt +
                "\n\n## Input\n\n" +
                "Document: " + documentText.FileName + "\n\n" +
                documentText.ExtractedText;
        }

        return systemPrompt.Replace(PlaceholderExtractedText, documentText.ExtractedText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Azure OpenAI structured-output schema names must be alphanumeric +
    /// underscores. Sanitize the Action name into a valid identifier.
    /// </summary>
    private static string SanitizeSchemaName(string actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName)) return "linear_action";
        var chars = actionName
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        return new string(chars).Trim('_');
    }
}
