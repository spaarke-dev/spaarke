namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Null-Object implementation of <see cref="ICommunicationProposeAi"/> registered when the compound AI
/// kill-switch is OFF (<c>Analysis:Enabled=false</c> OR <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// Same P2 "graceful degradation" shape as <see cref="NullCommunicationTriageAi"/> (ADR-032), NOT the P3
/// fail-fast shape: the sole consumer is the Communication enrichment path's best-effort Job B propose step
/// (NFR-04) — a disabled/unavailable Action MUST degrade to "no proposals" rather than throw, so
/// capture/enrichment always completes.
/// </remarks>
public sealed class NullCommunicationProposeAi : ICommunicationProposeAi
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ProposedFieldCandidate>?> ProposeAsync(
        CommunicationProposeRequest request,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ProposedFieldCandidate>?>(null);
}
