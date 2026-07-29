namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Null-Object implementation of <see cref="ICommunicationTriageAi"/> registered when the compound
/// AI kill-switch is OFF (<c>Analysis:Enabled=false</c> OR <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// Same P2 "graceful degradation" shape as <see cref="NullCommunicationClassificationAi"/> (ADR-032),
/// NOT the P3 fail-fast shape used by the other PublicContracts facades: the sole consumer is the
/// Communication enrichment path's best-effort triage trigger (NFR-04) — a disabled/unavailable Action
/// MUST degrade to "no triage result" rather than throw, so capture/enrichment always completes.
/// </remarks>
public sealed class NullCommunicationTriageAi : ICommunicationTriageAi
{
    /// <inheritdoc />
    public Task<CommunicationTriageResult?> TriageAsync(
        CommunicationTriageRequest request,
        CancellationToken ct = default)
        => Task.FromResult<CommunicationTriageResult?>(null);
}
