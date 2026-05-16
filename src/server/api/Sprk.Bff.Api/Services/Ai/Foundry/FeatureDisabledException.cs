namespace Sprk.Bff.Api.Services.Ai.Foundry;

/// <summary>
/// Thrown when an <see cref="AgentServiceClient"/> operation is attempted
/// while <see cref="AgentServiceOptions.Enabled"/> is <c>false</c> (ADR-018 kill switch).
///
/// Callers should map this to HTTP 503 Service Unavailable.
/// </summary>
public sealed class FeatureDisabledException : InvalidOperationException
{
    public FeatureDisabledException(string message) : base(message) { }
}
