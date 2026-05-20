namespace Sprk.Bff.Api.Infrastructure.Sse;

/// <summary>
/// Result returned by <see cref="ISseEventValidator.Validate"/>.
/// </summary>
/// <param name="IsValid">
/// <c>true</c> when the payload satisfies the structural contract for the given event type,
/// or when the event type has no registered schema (unknown / R1 events pass through).
/// </param>
/// <param name="ValidationErrors">
/// Human-readable validation error messages. Empty when <see cref="IsValid"/> is <c>true</c>.
/// </param>
/// <param name="FallbackPayload">
/// A safe JSON string (a generic_widget event) to emit in place of the invalid payload.
/// <c>null</c> when <see cref="IsValid"/> is <c>true</c>.
/// </param>
public sealed record SseEventValidationResult(
    bool IsValid,
    IReadOnlyList<string> ValidationErrors,
    string? FallbackPayload)
{
    /// <summary>Singleton result for a valid (or unknown-type) payload.</summary>
    public static readonly SseEventValidationResult Valid =
        new(true, Array.Empty<string>(), null);

    /// <summary>
    /// Creates a failed validation result with the supplied error messages and a
    /// pre-built generic_widget fallback payload.
    /// </summary>
    public static SseEventValidationResult Failure(IReadOnlyList<string> errors, string fallbackPayload) =>
        new(false, errors, fallbackPayload);
}
