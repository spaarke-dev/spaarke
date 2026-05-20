using System.Text.Json;

namespace Sprk.Bff.Api.Infrastructure.Sse;

/// <summary>
/// Validates SSE tool output payloads against their registered structural contracts.
///
/// Implementations load schema definitions at construction time (startup) so that
/// per-event validation is allocation-light and synchronous.
///
/// Unknown event types (R1 events: token, done, error, etc.) always return
/// <see cref="SseEventValidationResult.Valid"/> — only registered R2 types are checked.
/// </summary>
public interface ISseEventValidator
{
    /// <summary>
    /// Validates <paramref name="payload"/> against the structural contract for
    /// <paramref name="eventType"/>.
    /// </summary>
    /// <param name="eventType">
    /// SSE event type discriminator string. Unknown types pass through unchanged.
    /// </param>
    /// <param name="payload">Parsed JSON payload to validate.</param>
    /// <returns>
    /// A <see cref="SseEventValidationResult"/> describing whether the payload satisfies
    /// the registered contract. When <see cref="SseEventValidationResult.IsValid"/> is
    /// <c>false</c>, <see cref="SseEventValidationResult.FallbackPayload"/> is populated
    /// with a safe generic_widget event the caller should emit instead.
    /// </returns>
    SseEventValidationResult Validate(string eventType, JsonElement payload);
}
