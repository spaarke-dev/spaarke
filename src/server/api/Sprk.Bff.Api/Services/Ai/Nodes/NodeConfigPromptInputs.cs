using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Reads the two prompt inputs a prompt-driven node carries in its (Layer-1-rendered) ConfigJson:
/// <c>templateParameters</c> ({{key}} substitution inside the Action's JPS instruction) and
/// <c>inputBinding</c> (the structured <c>## Input</c> section). Shared by
/// <see cref="AiCompletionNodeExecutor"/> and <see cref="AgentServiceNodeExecutor"/>, which both render the
/// linked Action's JPS prompt through <see cref="PromptSchemaRenderer"/>.
/// </summary>
internal static class NodeConfigPromptInputs
{
    /// <summary>
    /// The <c>templateParameters</c> object as a dictionary, or null when ConfigJson is missing, malformed, or
    /// has no object-valued <c>templateParameters</c>. Non-scalar values are passed as their raw JSON text.
    /// </summary>
    public static Dictionary<string, object?>? ExtractTemplateParameters(string? configJson, ILogger logger, string executorName)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("templateParameters", out var paramsElement))
                return null;

            if (paramsElement.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning(
                    "{Executor} node ConfigJson templateParameters is not an object (found {ValueKind}); ignoring",
                    executorName, paramsElement.ValueKind);
                return null;
            }

            var result = new Dictionary<string, object?>();
            foreach (var prop in paramsElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => prop.Value.GetRawText()
                };
            }

            return result.Count > 0 ? result : null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "{Executor} node failed to parse templateParameters from ConfigJson; using null fallback", executorName);
            return null;
        }
    }

    /// <summary>
    /// A detached clone of the <c>inputBinding</c> object, or null when ConfigJson is missing, malformed, or has
    /// no object-valued <c>inputBinding</c> (the renderer then emits no <c>## Input</c> section).
    /// </summary>
    public static JsonElement? ExtractInputBinding(string? configJson, ILogger logger, string executorName)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("inputBinding", out var bindingElement))
                return null;

            if (bindingElement.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning(
                    "{Executor} node ConfigJson inputBinding is not an object (found {ValueKind}); ignoring",
                    executorName, bindingElement.ValueKind);
                return null;
            }

            return bindingElement.Clone();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "{Executor} node failed to parse inputBinding from ConfigJson; using null fallback", executorName);
            return null;
        }
    }
}
