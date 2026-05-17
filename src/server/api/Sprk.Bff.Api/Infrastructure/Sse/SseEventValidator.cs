using System.Text.Json;

namespace Sprk.Bff.Api.Infrastructure.Sse;

/// <summary>
/// Singleton implementation of <see cref="ISseEventValidator"/>.
///
/// Delegates structural validation to <see cref="SseEventSchemaValidator"/> (the static
/// validator built in AIPU2-007) and wraps the result as a <see cref="SseEventValidationResult"/>,
/// populating a safe generic_widget fallback payload on failure.
///
/// All seven R2 event types from FR-801 are validated:
///   workspace_widget, context_update, context_highlight, workspace_action,
///   suggestions, capability_change, safety_annotation.
///
/// Unknown event types (R1 events: token, done, error, citations, plan_preview, etc.)
/// pass through as <see cref="SseEventValidationResult.Valid"/> without inspection.
///
/// Schema definitions are intrinsic to <see cref="SseEventSchemaValidator"/> (loaded at
/// compile time), satisfying the acceptance criterion: "Schema files are loaded at startup
/// (not per-request)".
///
/// Thread-safety: stateless — safe for singleton DI lifetime.
/// </summary>
public sealed class SseEventValidator : ISseEventValidator
{
    // ---------------------------------------------------------------------------
    // Fallback payload template
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Safe fallback JSON string emitted when a payload fails schema validation.
    ///
    /// Shape is intentionally minimal — the PCF AI shell handles any object that
    /// carries type="generic_widget" with a non-null body string without crashing.
    /// </summary>
    private const string FallbackJson =
        "{\"type\":\"generic_widget\",\"title\":\"Response\","
        + "\"body\":\"An AI tool returned an unexpected response format.\","
        + "\"errorCode\":\"SSE_SCHEMA_VIOLATION\"}";

    // ---------------------------------------------------------------------------
    // ISseEventValidator
    // ---------------------------------------------------------------------------

    /// <inheritdoc/>
    public SseEventValidationResult Validate(string eventType, JsonElement payload)
    {
        // Delegate to the existing static structural validator.
        // ValidateAsync is a ValueTask but always completes synchronously (no actual
        // async I/O — the async signature was reserved for future schema file loading).
        var validationResult = SseEventSchemaValidator
            .ValidateAsync(eventType, payload)
            .GetAwaiter()
            .GetResult();

        if (validationResult.IsValid)
            return SseEventValidationResult.Valid;

        return SseEventValidationResult.Failure(validationResult.Errors, FallbackJson);
    }
}
