using System.Text.Json;

namespace Sprk.Bff.Api.Infrastructure.Sse;

/// <summary>
/// Result returned by <see cref="SseEventSchemaValidator.ValidateAsync"/>.
/// </summary>
/// <param name="IsValid">True when the payload satisfies the structural contract for the given event type.</param>
/// <param name="Errors">Human-readable validation error messages. Empty when <see cref="IsValid"/> is true.</param>
public sealed record SseValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    /// <summary>Singleton result for a valid payload.</summary>
    public static readonly SseValidationResult Valid = new(true, Array.Empty<string>());

    /// <summary>Create an invalid result with one or more error messages.</summary>
    public static SseValidationResult Failure(params string[] errors) =>
        new(false, errors);

    /// <summary>Create an invalid result from a list of error messages.</summary>
    public static SseValidationResult Failure(IReadOnlyList<string> errors) =>
        new(false, errors);
}

/// <summary>
/// Structural validator for R2 SSE event payloads.
///
/// Validates a <see cref="JsonElement"/> against the expected shape for a given event type
/// (required fields, enum values, numeric ranges) without an external JSON Schema library.
/// This is intentionally lightweight — it is designed for use in integration tests and
/// optional debug middleware, not for hot-path production validation.
///
/// All seven R2 event types from FR-801 are supported:
///   workspace_widget, context_update, context_highlight, workspace_action,
///   suggestions, capability_change, safety_annotation
///
/// For the full JSON Schema draft-07 definitions, see:
///   infrastructure/contracts/sse-events/
/// </summary>
public static class SseEventSchemaValidator
{
    // ---------------------------------------------------------------------------
    // Known event type constants (mirrors ChatSseR2EventTypes)
    // ---------------------------------------------------------------------------

    /// <summary>All R2 event type strings recognised by this validator.</summary>
    public static readonly IReadOnlySet<string> KnownR2EventTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        ChatSseR2EventTypes.WorkspaceWidget,
        ChatSseR2EventTypes.ContextUpdate,
        ChatSseR2EventTypes.ContextHighlight,
        ChatSseR2EventTypes.WorkspaceAction,
        ChatSseR2EventTypes.Suggestions,
        ChatSseR2EventTypes.CapabilityChange,
        ChatSseR2EventTypes.SafetyAnnotation,
    };

    // ---------------------------------------------------------------------------
    // Public entry point
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Validates <paramref name="payload"/> against the structural contract for
    /// <paramref name="eventType"/>.
    ///
    /// Returns <see cref="SseValidationResult.Valid"/> immediately for unknown event types
    /// (R1 events such as 'token', 'done', 'error') so callers do not need to filter.
    /// </summary>
    /// <param name="eventType">The SSE event type string (value of the 'type' field).</param>
    /// <param name="payload">The parsed JSON payload to validate.</param>
    /// <param name="cancellationToken">Cancellation token (reserved for future async schema loading).</param>
    /// <returns>A <see cref="SseValidationResult"/> describing whether the payload is valid.</returns>
    public static ValueTask<SseValidationResult> ValidateAsync(
        string eventType,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(eventType))
        {
            return new ValueTask<SseValidationResult>(
                SseValidationResult.Failure("eventType must not be null or empty."));
        }

        var result = eventType switch
        {
            ChatSseR2EventTypes.WorkspaceWidget => ValidateWorkspaceWidget(payload),
            ChatSseR2EventTypes.ContextUpdate => ValidateContextUpdate(payload),
            ChatSseR2EventTypes.ContextHighlight => ValidateContextHighlight(payload),
            ChatSseR2EventTypes.WorkspaceAction => ValidateWorkspaceAction(payload),
            ChatSseR2EventTypes.Suggestions => ValidateSuggestions(payload),
            ChatSseR2EventTypes.CapabilityChange => ValidateCapabilityChange(payload),
            ChatSseR2EventTypes.SafetyAnnotation => ValidateSafetyAnnotation(payload),
            _ => SseValidationResult.Valid   // R1 / unknown — pass through
        };

        return new ValueTask<SseValidationResult>(result);
    }

    // ---------------------------------------------------------------------------
    // Per-event validators
    // ---------------------------------------------------------------------------

    private static SseValidationResult ValidateWorkspaceWidget(JsonElement el)
    {
        var errors = new List<string>();

        RequireString(el, "widgetId", errors);
        RequireEnum(el, "widgetType",
            new[] { "document-preview", "action-panel", "suggestion-list", "capability-status" },
            errors);
        RequireObject(el, "payload", errors);
        RequireIntegerInRange(el, "priority", 1, 10, errors);

        return ToResult(errors);
    }

    private static SseValidationResult ValidateContextUpdate(JsonElement el)
    {
        var errors = new List<string>();

        RequireEnum(el, "contextType",
            new[] { "document", "entity", "conversation", "user-intent" },
            errors);
        RequireString(el, "contextId", errors);
        RequireObject(el, "delta", errors);
        RequireNumberInRange(el, "confidence", 0.0, 1.0, errors);

        return ToResult(errors);
    }

    private static SseValidationResult ValidateContextHighlight(JsonElement el)
    {
        var errors = new List<string>();

        RequireString(el, "documentId", errors);
        RequireNonEmptyArray(el, "highlights", errors);
        RequireEnum(el, "highlightType",
            new[] { "relevant", "cited", "conflicting" },
            errors);

        // Validate each RangeHighlight has required fields
        if (el.TryGetProperty("highlights", out var highlights) &&
            highlights.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var highlight in highlights.EnumerateArray())
            {
                if (!highlight.TryGetProperty("startOffset", out _))
                    errors.Add($"highlights[{i}].startOffset is required.");
                if (!highlight.TryGetProperty("endOffset", out _))
                    errors.Add($"highlights[{i}].endOffset is required.");
                i++;
            }
        }

        return ToResult(errors);
    }

    private static SseValidationResult ValidateWorkspaceAction(JsonElement el)
    {
        var errors = new List<string>();

        RequireString(el, "actionId", errors);
        RequireEnum(el, "actionType",
            new[] { "navigate", "open-document", "run-playbook", "dismiss" },
            errors);
        RequireString(el, "label", errors);
        RequireBoolean(el, "requiresConfirmation", errors);

        return ToResult(errors);
    }

    private static SseValidationResult ValidateSuggestions(JsonElement el)
    {
        var errors = new List<string>();

        RequireNonEmptyArray(el, "suggestions", errors);
        RequireInteger(el, "maxSuggestions", errors);

        if (el.TryGetProperty("suggestions", out var suggestions) &&
            suggestions.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var suggestion in suggestions.EnumerateArray())
            {
                if (!suggestion.TryGetProperty("suggestionId", out _))
                    errors.Add($"suggestions[{i}].suggestionId is required.");
                if (!suggestion.TryGetProperty("text", out _))
                    errors.Add($"suggestions[{i}].text is required.");
                if (!suggestion.TryGetProperty("confidence", out _))
                    errors.Add($"suggestions[{i}].confidence is required.");
                if (suggestion.TryGetProperty("category", out var cat))
                {
                    var catStr = cat.GetString() ?? string.Empty;
                    if (catStr is not ("action" or "question" or "insight"))
                        errors.Add($"suggestions[{i}].category must be one of: action, question, insight.");
                }
                else
                {
                    errors.Add($"suggestions[{i}].category is required.");
                }
                i++;
            }

            // maxSuggestions must match array length
            if (el.TryGetProperty("maxSuggestions", out var maxProp) &&
                maxProp.ValueKind == JsonValueKind.Number &&
                maxProp.TryGetInt32(out var maxVal))
            {
                var actualCount = suggestions.GetArrayLength();
                if (maxVal != actualCount)
                    errors.Add($"maxSuggestions ({maxVal}) must equal suggestions array length ({actualCount}).");
            }
        }

        return ToResult(errors);
    }

    private static SseValidationResult ValidateCapabilityChange(JsonElement el)
    {
        var errors = new List<string>();

        RequireEnum(el, "capability",
            new[] { "search", "summarize", "cite", "memory", "safety", "playbook" },
            errors);
        RequireEnum(el, "status",
            new[] { "available", "degraded", "unavailable" },
            errors);

        // retryAfterSeconds should be present and >= 1 when status='degraded'
        if (el.TryGetProperty("status", out var statusProp) &&
            statusProp.GetString() == "degraded")
        {
            if (el.TryGetProperty("retryAfterSeconds", out var retryProp))
            {
                if (retryProp.ValueKind != JsonValueKind.Number ||
                    !retryProp.TryGetInt32(out var retryVal) ||
                    retryVal < 1)
                {
                    errors.Add("retryAfterSeconds must be an integer >= 1 when status='degraded'.");
                }
            }
            // retryAfterSeconds is optional (recommended but not required by schema)
        }

        return ToResult(errors);
    }

    private static SseValidationResult ValidateSafetyAnnotation(JsonElement el)
    {
        var errors = new List<string>();

        RequireEnum(el, "severity",
            new[] { "info", "warning", "blocked" },
            errors);
        RequireEnum(el, "category",
            new[] { "jailbreak", "indirect-attack", "groundedness", "content-policy" },
            errors);
        RequireEnum(el, "action",
            new[] { "logged", "filtered", "blocked" },
            errors);
        RequireString(el, "userMessage", errors);

        // groundedness object — required fields when present
        if (el.TryGetProperty("groundedness", out var groundedness))
        {
            if (groundedness.ValueKind != JsonValueKind.Object)
            {
                errors.Add("groundedness must be an object.");
            }
            else if (!groundedness.TryGetProperty("score", out var scoreProp) ||
                     scoreProp.ValueKind != JsonValueKind.Number)
            {
                errors.Add("groundedness.score is required and must be a number.");
            }
            else
            {
                var score = scoreProp.GetDouble();
                if (score < 0.0 || score > 1.0)
                    errors.Add("groundedness.score must be between 0.0 and 1.0.");
            }
        }

        // citations object — arrays may be empty but must be arrays when present
        if (el.TryGetProperty("citations", out var citations))
        {
            if (citations.ValueKind != JsonValueKind.Object)
            {
                errors.Add("citations must be an object.");
            }
            else
            {
                foreach (var arrayProp in new[] { "verified", "unverified", "partial" })
                {
                    if (citations.TryGetProperty(arrayProp, out var arr) &&
                        arr.ValueKind != JsonValueKind.Array)
                    {
                        errors.Add($"citations.{arrayProp} must be an array.");
                    }
                }
            }
        }

        return ToResult(errors);
    }

    // ---------------------------------------------------------------------------
    // Helper assertions
    // ---------------------------------------------------------------------------

    private static void RequireString(JsonElement el, string propertyName, List<string> errors)
    {
        if (!el.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(prop.GetString()))
        {
            errors.Add($"'{propertyName}' is required and must be a non-empty string.");
        }
    }

    private static void RequireEnum(JsonElement el, string propertyName, string[] allowedValues, List<string> errors)
    {
        if (!el.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind != JsonValueKind.String)
        {
            errors.Add($"'{propertyName}' is required and must be a string.");
            return;
        }

        var value = prop.GetString() ?? string.Empty;
        if (!allowedValues.Contains(value, StringComparer.Ordinal))
        {
            errors.Add($"'{propertyName}' must be one of: {string.Join(", ", allowedValues)}. Got: '{value}'.");
        }
    }

    private static void RequireObject(JsonElement el, string propertyName, List<string> errors)
    {
        if (!el.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"'{propertyName}' is required and must be an object.");
        }
    }

    private static void RequireBoolean(JsonElement el, string propertyName, List<string> errors)
    {
        if (!el.TryGetProperty(propertyName, out var prop) ||
            (prop.ValueKind != JsonValueKind.True && prop.ValueKind != JsonValueKind.False))
        {
            errors.Add($"'{propertyName}' is required and must be a boolean.");
        }
    }

    private static void RequireInteger(JsonElement el, string propertyName, List<string> errors)
    {
        if (!el.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind != JsonValueKind.Number ||
            !prop.TryGetInt32(out _))
        {
            errors.Add($"'{propertyName}' is required and must be an integer.");
        }
    }

    private static void RequireIntegerInRange(
        JsonElement el, string propertyName, int min, int max, List<string> errors)
    {
        if (!el.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind != JsonValueKind.Number ||
            !prop.TryGetInt32(out var value))
        {
            errors.Add($"'{propertyName}' is required and must be an integer.");
            return;
        }

        if (value < min || value > max)
            errors.Add($"'{propertyName}' must be between {min} and {max}. Got: {value}.");
    }

    private static void RequireNumberInRange(
        JsonElement el, string propertyName, double min, double max, List<string> errors)
    {
        if (!el.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind != JsonValueKind.Number)
        {
            errors.Add($"'{propertyName}' is required and must be a number.");
            return;
        }

        var value = prop.GetDouble();
        if (value < min || value > max)
            errors.Add($"'{propertyName}' must be between {min} and {max}. Got: {value}.");
    }

    private static void RequireNonEmptyArray(JsonElement el, string propertyName, List<string> errors)
    {
        if (!el.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"'{propertyName}' is required and must be an array.");
            return;
        }

        if (prop.GetArrayLength() == 0)
            errors.Add($"'{propertyName}' must contain at least one item.");
    }

    private static SseValidationResult ToResult(List<string> errors) =>
        errors.Count == 0 ? SseValidationResult.Valid : SseValidationResult.Failure(errors);
}

/// <summary>
/// SSE event type string constants for R2 events introduced in the AI Platform Unification R2 project.
///
/// R1 event types (token, done, error, citations, suggestions, plan_preview, etc.) remain in
/// ChatEndpoints.cs. This class covers only the net-new R2 types defined in FR-801.
///
/// See: infrastructure/contracts/sse-events/manifest.json
/// </summary>
public static class ChatSseR2EventTypes
{
    /// <summary>Instructs the AI shell to render or update a workspace widget.</summary>
    public const string WorkspaceWidget = "workspace_widget";

    /// <summary>Notifies the client that the agent's active context has changed.</summary>
    public const string ContextUpdate = "context_update";

    /// <summary>Instructs the source pane to highlight text ranges in a document.</summary>
    public const string ContextHighlight = "context_highlight";

    /// <summary>Asks the shell to perform a workspace-level action (navigate, open-document, run-playbook, dismiss).</summary>
    public const string WorkspaceAction = "workspace_action";

    /// <summary>
    /// R2 rich suggestions event. Delivers typed suggestions with confidence and category.
    /// Note: R1 'suggestions' event (string array) is handled separately in ChatEndpoints.cs.
    /// </summary>
    public const string Suggestions = "suggestions";

    /// <summary>Notifies the client that an AI capability's availability has changed.</summary>
    public const string CapabilityChange = "capability_change";

    /// <summary>Carries safety-layer annotations (groundedness, citation verification, content-policy).</summary>
    public const string SafetyAnnotation = "safety_annotation";
}
