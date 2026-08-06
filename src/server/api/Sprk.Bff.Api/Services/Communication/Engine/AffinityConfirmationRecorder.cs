using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// FR-A4 affinity confirmation-write orchestration (email-communication-intelligence-r2, R-1). Turns a HUMAN
/// association confirmation into affinity learning: reconstruct the stored communication's envelope, compute the
/// SAME signals the read rung uses (<see cref="AffinityRung.ExtractSignals"/> — read/write canonicalization
/// parity), and increment each (signal → target) confirmation count via <see cref="AffinityStore"/>.
/// </summary>
/// <remarks>
/// <para>Learns from HUMAN confirmations ONLY — the confirm surface calls this after the user's regarding write,
/// never the engine's deterministic auto-file path — so affinity does not self-reinforce.</para>
/// <para>Best-effort / non-fatal (NFR-04): an invalid/unmapped target, a tenant with affinity disabled, or any
/// store/reconstruction failure returns 0 WITHOUT throwing — the caller (endpoint) must never fail the user's
/// confirmation over a learning signal. Reads the envelope through the <see cref="ICommunicationEnvelopeReader"/>
/// test-seam (ADR-010) so this orchestration is unit-testable without the sealed <c>CommunicationService</c>.</para>
/// <para><b>§11:</b> Existing — none (no human-confirmation→affinity orchestration). Extension — not on
/// <see cref="AffinityStore"/> (which records ONE signal and knows nothing of envelope reconstruction) nor on the
/// rung (read-only). Cost-of-doing-nothing — the FR-A4 learning loop never accumulates rows; the affinity store
/// stays permanently empty and <see cref="AffinityRung"/> can never fire.</para>
/// </remarks>
public sealed class AffinityConfirmationRecorder
{
    private readonly ICommunicationEnvelopeReader _envelopeReader;
    private readonly AffinityStore _store;
    private readonly IOptionsMonitor<AffinityOptions> _options;
    private readonly ILogger<AffinityConfirmationRecorder> _logger;

    public AffinityConfirmationRecorder(
        ICommunicationEnvelopeReader envelopeReader,
        AffinityStore store,
        IOptionsMonitor<AffinityOptions> options,
        ILogger<AffinityConfirmationRecorder> logger)
    {
        _envelopeReader = envelopeReader;
        _store = store;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Records affinity for a human confirmation that communication <paramref name="communicationId"/> is
    /// regarding <paramref name="targetEntityType"/>:<paramref name="targetRecordId"/>. Returns the number of
    /// affinity signals incremented (0 on any best-effort no-op). Never throws.
    /// </summary>
    public async Task<int> RecordAsync(
        Guid communicationId, string? targetEntityType, string? targetRecordId, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(targetEntityType) || string.IsNullOrWhiteSpace(targetRecordId))
                return 0;

            // Only learn for a real ADR-024 regarding target with a valid id — mirror the read rung's guard.
            if (RegardingFieldMap.FieldFor(targetEntityType) is null
                || !Guid.TryParse(targetRecordId, out var targetGuid)
                || targetGuid == Guid.Empty)
            {
                _logger.LogDebug(
                    "Affinity confirm skipped — target {Entity}:{Id} is unmapped or not a valid id.",
                    targetEntityType, targetRecordId);
                return 0;
            }

            var opts = _options.CurrentValue;
            var (message, context) = await _envelopeReader.ReconstructEnvelopeAsync(communicationId, ct)
                .ConfigureAwait(false);

            if (!opts.IsEnabledFor(context.TenantKey))
            {
                _logger.LogDebug(
                    "Affinity confirm skipped — disabled via Communication:Affinity (tenant {TenantKey}).",
                    context.TenantKey);
                return 0;
            }

            var signals = AffinityRung.ExtractSignals(message, opts);
            foreach (var signal in signals)
            {
                await _store.RecordConfirmationAsync(
                    signal.Type, signal.Value, targetEntityType, targetRecordId, context.TenantKey, ct)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Affinity recorded {Count} signal(s) for confirmation of communication {Id} → {Entity}:{TargetId}.",
                signals.Count, communicationId, targetEntityType, targetRecordId);
            return signals.Count;
        }
        catch (Exception ex)
        {
            // NEVER fail the user's confirmation — affinity learning is a best-effort signal (NFR-04).
            _logger.LogWarning(ex, "Affinity confirmation-write failed (non-fatal) for communication {Id}.", communicationId);
            return 0;
        }
    }
}
