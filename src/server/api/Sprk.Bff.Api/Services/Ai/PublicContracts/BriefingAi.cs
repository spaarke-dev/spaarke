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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        // Delegate to the underlying client with default model + token settings.
        // Per ADR-007, the facade does NOT layer additional defaults — those live in
        // OpenAiClient where they can be reasoned about in one place.
        return _openAi.GetCompletionAsync(prompt, cancellationToken: cancellationToken);
    }
}
