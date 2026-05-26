using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Provider-agnostic SSE event envelope emitted by <see cref="ISprkAgent"/> implementations.
///
/// This record defines the wire format used by the R2 agent pipeline. It is intentionally
/// decoupled from the R1 <c>ChatSseEvent</c> record (defined in <c>ChatEndpoints.cs</c>)
/// so that the <see cref="ISprkAgent"/> interface remains independent of the API surface layer.
///
/// Callers (e.g. ChatOrchestrationService) are responsible for translating these events
/// into the appropriate wire format when forwarding to SSE clients.
///
/// Type values: use constants from <c>ChatSseR2EventTypes</c> for R2 events, or standard
/// strings ("token", "done", "error") for R1-compatible events.
/// </summary>
/// <param name="Type">
/// Event type discriminator string. Consumers pattern-match on this value to determine
/// how to interpret <see cref="Data"/>.
/// </param>
/// <param name="Data">
/// Structured event payload. The schema is determined by <see cref="Type"/>. May be
/// <see cref="JsonValueKind.Null"/> for events that carry no structured payload (e.g. "done").
/// </param>
/// <param name="Timestamp">
/// UTC timestamp when this event was created by the agent. Used for latency tracing
/// and ordering across concurrent event sources.
/// </param>
public sealed record SseEvent(
    string Type,
    JsonElement Data,
    DateTimeOffset Timestamp)
{
    /// <summary>
    /// Creates an <see cref="SseEvent"/> with a null/empty JSON data payload.
    /// Convenience factory for events that carry no structured payload (e.g. "done").
    /// </summary>
    /// <param name="type">Event type discriminator string.</param>
    /// <returns>An <see cref="SseEvent"/> with <see cref="Data"/> set to a null JSON element.</returns>
    public static SseEvent Empty(string type) =>
        new(type, default, DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates an <see cref="SseEvent"/> from a string content value.
    /// Convenience factory for simple token/error events that carry plain text.
    /// </summary>
    /// <param name="type">Event type discriminator string (e.g. "token", "error").</param>
    /// <param name="content">Plain text content serialized as a JSON string element.</param>
    /// <returns>An <see cref="SseEvent"/> with <see cref="Data"/> containing the serialized string.</returns>
    public static SseEvent FromString(string type, string content)
    {
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(content);
        var element = JsonDocument.Parse(bytes).RootElement.Clone();
        return new(type, element, DateTimeOffset.UtcNow);
    }
}
