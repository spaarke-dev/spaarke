namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Null-Object implementation of <see cref="IEmailDraftAi"/> (ADR-032) registered when the
/// <c>AzureOpenAI</c> gate is off (no <c>AzureOpenAI:Endpoint</c>/<c>ChatModelName</c>). Returns
/// <c>null</c> so the compose sparkle degrades to a clean "AI drafting is unavailable" problem rather
/// than a DI resolution failure — the P2 graceful-degradation shape (same as <see cref="NullCommunicationProposeAi"/>).
/// </summary>
public sealed class NullEmailDraftAi : IEmailDraftAi
{
    /// <inheritdoc />
    public Task<string?> DraftAsync(EmailDraftAiRequest request, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
