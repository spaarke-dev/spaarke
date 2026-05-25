namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IBriefingAi"/>: a thin wrapper around
/// <see cref="IOpenAiClient.GetCompletionAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Introduces zero behavior change vs. direct <c>IOpenAiClient</c> injection. The value
/// of the facade is structural (boundary enforcement per refined ADR-013) — see ADR-007
/// for the canonical "facade-over-internal-SDK" rationale.
/// </para>
/// <para>
/// No retry / circuit-breaker logic is added here on purpose: those concerns belong
/// inside <see cref="OpenAiClient"/> (where they already exist) so that all AI callers
/// — facade or direct — get the same resilience semantics.
/// </para>
/// </remarks>
public sealed class BriefingAi : IBriefingAi
{
    private readonly IOpenAiClient _openAi;

    public BriefingAi(IOpenAiClient openAi)
    {
        _openAi = openAi ?? throw new ArgumentNullException(nameof(openAi));
    }

    /// <inheritdoc />
    public Task<string> GenerateNarrativeAsync(
        string prompt,
        int? maxOutputTokens = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        // Delegate to the underlying client. Per ADR-007, the facade does NOT layer
        // additional defaults — model selection and other token settings live in
        // OpenAiClient where they can be reasoned about in one place. Token-cap, however,
        // is a per-call concern the caller already controls today (daily-briefing passes
        // 300/500; matter-summary passes null), so it's surfaced on the facade.
        return _openAi.GetCompletionAsync(
            prompt,
            maxOutputTokens: maxOutputTokens,
            cancellationToken: cancellationToken);
    }
}
