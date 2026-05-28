using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.Insights.Mirror;

/// <summary>
/// Phase 1 no-op implementation of <see cref="IObservationMirror"/>. Logs an Information
/// event for each Observation that would be mirrored, but does NOT write to Dataverse.
/// </summary>
/// <remarks>
/// <para>
/// <b>Swap path</b>: task 051 (D-P11 mirror sync) ships <c>DataverseObservationMirror</c>
/// which performs the real <c>sprk_analysis</c> upsert via <c>IDataverseService</c>. The
/// DI registration in <c>InsightsIngestModule</c> switches from this no-op to the real
/// impl when 051 lands — no orchestrator changes required.
/// </para>
/// <para>
/// <b>Why a no-op rather than commented-out call site</b>: the orchestrator (task 040) ships
/// today with the mirror call wired up. The acceptance test verifies that the mirror is
/// INVOKED — not that a real Dataverse row is written. This matches the task brief's
/// scope clarification: "test verifies the hook is INVOKED, not that a real Dataverse
/// row is written."
/// </para>
/// <para>
/// <b>Structured logging</b>: stable <c>EventId(8041, "ObservationMirrorNoOp")</c> so
/// App Insights queries can confirm the mirror path is being exercised in pre-prod
/// before the real impl lands.
/// </para>
/// </remarks>
internal sealed class NoOpObservationMirror : IObservationMirror
{
    private static readonly EventId MirrorNoOpEvent = new(8041, "ObservationMirrorNoOp");

    private readonly ILogger<NoOpObservationMirror> _logger;

    public NoOpObservationMirror(ILogger<NoOpObservationMirror> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task MirrorAsync(ObservationArtifact observation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ct.ThrowIfCancellationRequested();

        _logger.Log(
            LogLevel.Information,
            MirrorNoOpEvent,
            "NoOpObservationMirror invoked (Phase 1 scaffold; task 051 will swap in DataverseObservationMirror): observationId={ObservationId} subject={Subject} predicate={Predicate} producedBy={ProducedById}@{ProducedByVersion} tenantId={TenantId}",
            observation.Id,
            observation.Subject,
            observation.Predicate,
            observation.ProducedBy.Id,
            observation.ProducedBy.Version,
            observation.TenantId);

        return Task.CompletedTask;
    }
}
