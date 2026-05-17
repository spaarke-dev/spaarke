namespace Sprk.Bff.Api.Services.Ai.Foundry;

/// <summary>
/// Thrown when the <see cref="AgentServiceClient"/> cannot acquire the concurrency gate
/// within the allowed wait time (ADR-016 backpressure).
///
/// Callers should map this to HTTP 429 Too Many Requests.
/// </summary>
public sealed class ConcurrencyLimitExceededException : Exception
{
    public ConcurrencyLimitExceededException(string message) : base(message) { }
}
