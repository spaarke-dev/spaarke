namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// Outcome of a Prompt Shields scan.
/// </summary>
/// <param name="IsBlocked">
/// True when an attack was detected and the request MUST be blocked before reaching the LLM.
/// False when safe to proceed, or when the Content Safety service was unavailable (fail-open).
/// </param>
/// <param name="BlockReason">
/// The category of attack detected, or <see cref="PromptShieldBlockReason.None"/> when not blocked.
/// </param>
/// <param name="DetectedAttackType">
/// The raw attack type string returned by the API (e.g. "UserPromptAttack", "DocumentAttack").
/// Null when not blocked or when the service was unavailable.
/// </param>
/// <param name="BlockedDocumentIndexes">
/// Zero-based indexes into <see cref="PromptShieldRequest.Documents"/> that contained an attack.
/// Empty when the block reason is not <see cref="PromptShieldBlockReason.DocumentInjection"/>.
/// </param>
/// <param name="LatencyMs">
/// Wall-clock time (milliseconds) spent calling the Content Safety API.
/// Populated regardless of whether the request was blocked, to drive OTEL histograms.
/// </param>
public sealed record PromptShieldResult(
    bool IsBlocked,
    PromptShieldBlockReason BlockReason,
    string? DetectedAttackType,
    IReadOnlyList<int> BlockedDocumentIndexes,
    double LatencyMs)
{
    /// <summary>
    /// Convenience factory: safe result (not blocked).
    /// </summary>
    public static PromptShieldResult Safe(double latencyMs) =>
        new(false, PromptShieldBlockReason.None, null, [], latencyMs);

    /// <summary>
    /// Convenience factory: fail-open result when the service is unavailable.
    /// Callers MUST log a warning before returning this result.
    /// </summary>
    public static PromptShieldResult FailOpen(double latencyMs) =>
        new(false, PromptShieldBlockReason.None, null, [], latencyMs);
}

/// <summary>
/// Identifies which class of prompt injection attack was detected.
/// </summary>
public enum PromptShieldBlockReason
{
    /// <summary>No attack detected — request is safe to forward to the LLM.</summary>
    None = 0,

    /// <summary>
    /// A direct jailbreak attempt was found in the user's chat message
    /// (maps to <c>userPromptAttack</c> in the Prompt Shields API).
    /// </summary>
    UserInjection = 1,

    /// <summary>
    /// An indirect prompt injection attack was found in one or more retrieved document passages
    /// (maps to <c>documentAttack</c> in the Prompt Shields API).
    /// </summary>
    DocumentInjection = 2,
}
