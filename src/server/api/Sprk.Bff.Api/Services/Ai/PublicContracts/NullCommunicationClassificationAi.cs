using Sprk.Bff.Api.Models.Ai.Communication;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Null-Object implementation of <see cref="ICommunicationClassificationAi"/> registered when the compound
/// AI kill-switch is OFF (<c>Analysis:Enabled=false</c> OR <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// Unlike the fail-fast Null facades (<see cref="NullInvoiceAi"/> throws <c>FeatureDisabledException</c>), the
/// sole consumer here is the Association Engine's rung 5 — a best-effort AI escalation whose contract is
/// explicitly "returns null → no signal" (NFR-06: association never fails the capture path). Returning
/// <c>null</c> is therefore the correct fail-safe: the rung emits no matches and the deterministic decision
/// stands. This is the ADR-032 P2 "graceful degradation" shape, not P3 fail-fast.
/// </para>
/// </remarks>
public sealed class NullCommunicationClassificationAi : ICommunicationClassificationAi
{
    /// <inheritdoc />
    public Task<CommunicationClassificationResult?> ClassifyAsync(
        string subject,
        string bodyText,
        CancellationToken ct = default)
        => Task.FromResult<CommunicationClassificationResult?>(null);
}
