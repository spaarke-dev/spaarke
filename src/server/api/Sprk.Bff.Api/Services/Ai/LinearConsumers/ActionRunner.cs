using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Ai.Schemas;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// <see cref="IActionRunner"/> implementation that wraps
/// <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered as Singleton — stateless, dependencies are Singleton-safe.
/// </para>
/// <para>
/// Uses the same <see cref="PromptSchemaRenderer"/> the Playbook Engine's
/// <c>AiCompletionNodeExecutor</c> uses so JPS-format Action prompts render
/// identically on both paths (natural-language sections vs raw JSON-as-prompt).
/// Plain-text prompts pass through unchanged.
/// </para>
/// </remarks>
public sealed class ActionRunner : IActionRunner
{
    private readonly IOpenAiClient _openAi;
    private readonly PromptSchemaRenderer _promptRenderer;
    private readonly IOptions<LinearConsumersOptions> _options;
    private readonly ILogger<ActionRunner> _logger;

    private const string PlaceholderExtractedText = "{{document.extractedText}}";

    public ActionRunner(
        IOpenAiClient openAi,
        PromptSchemaRenderer promptRenderer,
        IOptions<LinearConsumersOptions> options,
        ILogger<ActionRunner> logger)
    {
        _openAi = openAi;
        _promptRenderer = promptRenderer;
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

        var prompt = BuildPrompt(action.SystemPrompt, documentText);
        var jsonSchema = BinaryData.FromString(action.OutputSchemaJson);
        var schemaName = SanitizeSchemaName(action.Name);
        var temperature = (float?)action.Temperature;

        _options.Value.TryGetModelDeployment(context.ConsumerType, out var modelDeployment);
        var hasMaxTokens = _options.Value.TryGetMaxOutputTokens(context.ConsumerType, out var maxTokens);
        int? maxOutputTokens = hasMaxTokens ? maxTokens : null;

        _logger.LogInformation(
            "Linear run: consumer={ConsumerType} action={ActionName} promptLen={PromptLen} model={Model} temp={Temperature} maxTokens={MaxTokens}",
            context.ConsumerType, action.Name, prompt.Length,
            modelDeployment ?? "(default)", temperature,
            maxOutputTokens?.ToString() ?? "(default)");

        var rawJson = await _openAi.GetStructuredCompletionRawAsync(
            prompt,
            jsonSchema,
            schemaName,
            model: modelDeployment,
            maxOutputTokens: maxOutputTokens,
            temperature: temperature,
            cancellationToken: cancellationToken);

        using var doc = JsonDocument.Parse(rawJson);
        // Clone so the JsonElement remains valid after `doc` is disposed.
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Build the prompt. Two paths:
    /// 1. JPS (JSON Prompt Schema) — Action's SystemPrompt starts with a <c>{</c>
    ///    and contains <c>$schema</c>: render via <see cref="PromptSchemaRenderer"/>
    ///    (natural-language sections, embedded document text in the "## Input"
    ///    section). Matches the Playbook Engine's AiCompletion executor behavior.
    /// 2. Flat text — plain prompt: fall back to the simple single-placeholder
    ///    <c>{{document.extractedText}}</c> substitution (or append the text after
    ///    a "## Input" separator when no placeholder is present).
    /// </summary>
    private string BuildPrompt(string systemPrompt, DocumentText documentText)
    {
        var rendered = _promptRenderer.Render(
            rawPrompt: systemPrompt,
            skillContext: null,
            knowledgeContext: null,
            documentText: documentText.ExtractedText,
            templateParameters: null,
            downstreamNodes: null);

        if (rendered.Format == PromptFormat.JsonPromptSchema && !string.IsNullOrWhiteSpace(rendered.PromptText))
        {
            return rendered.PromptText;
        }

        // Flat-text path — original single-placeholder substitution.
        if (!systemPrompt.Contains(PlaceholderExtractedText, StringComparison.Ordinal))
        {
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
