using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Infrastructure.Sse;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Scoped guard that validates SSE tool output payloads before they are written to the
/// HTTP response stream, and substitutes a safe fallback event when validation fails.
///
/// This prevents invalid tool output from reaching the PCF AI shell and causing UI crashes.
///
/// Behaviour:
///   - Valid payload for a known R2 event type  → returned unchanged.
///   - Invalid payload for a known R2 event type → replaced with a generic_widget fallback
///     event (<c>SSE_SCHEMA_VIOLATION</c>) and a Warning log is emitted.
///   - Unknown event type (R1 events: token, done, error, etc.) → passed through unchanged.
///
/// ADR-015 compliance: validation failure logs include the event type and validation error
/// strings, but NEVER the original payload content (which may contain governed data).
///
/// Registration: scoped in <see cref="Infrastructure.DI.AiSafetyModule"/>.
/// </summary>
public sealed class SseOutputGuard
{
    private readonly ISseEventValidator _validator;
    private readonly SseValidationTelemetry _telemetry;
    private readonly ILogger<SseOutputGuard> _logger;

    public SseOutputGuard(
        ISseEventValidator validator,
        SseValidationTelemetry telemetry,
        ILogger<SseOutputGuard> logger)
    {
        _validator = validator;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <summary>
    /// Validates the tool output <paramref name="payload"/> for the given
    /// <paramref name="eventType"/> and returns the event to emit.
    ///
    /// If the payload is valid (or the event type is unknown / R1), the original
    /// <paramref name="eventType"/> and <paramref name="payload"/> are returned.
    ///
    /// If the payload fails validation, a generic_widget fallback <see cref="SseEvent"/>
    /// is returned. The original payload is never forwarded.
    /// </summary>
    /// <param name="eventType">SSE event type string.</param>
    /// <param name="payload">Parsed JSON payload from the tool output.</param>
    /// <returns>
    /// The <see cref="SseEvent"/> to emit — either the original or a safe fallback.
    /// </returns>
    public SseEvent ValidateAndFallback(string eventType, JsonElement payload)
    {
        var result = _validator.Validate(eventType, payload);

        if (result.IsValid)
        {
            return new SseEvent(eventType, payload, DateTimeOffset.UtcNow);
        }

        // ADR-015: log event type + error strings only; NEVER log the payload.
        _logger.LogWarning(
            "SSE payload validation failed for event type '{EventType}'. Substituting generic_widget fallback. Errors: {Errors}",
            eventType,
            string.Join("; ", result.ValidationErrors));

        _telemetry.RecordValidationFailure(eventType);

        // Parse the fallback JSON into a JsonElement for the SseEvent envelope.
        var fallbackElement = JsonDocument.Parse(result.FallbackPayload!).RootElement.Clone();
        return new SseEvent("generic_widget", fallbackElement, DateTimeOffset.UtcNow);
    }
}
