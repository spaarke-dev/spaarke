using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Insights.Mirror;

/// <summary>
/// Phase 1 no-op implementation of <see cref="IObservationMirror"/>. Logs an Information
/// event for each Observation that would be mirrored, but does NOT write to Dataverse.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1 role (post-task 051)</b>: this no-op is the dev/test default. When the
/// <c>InsightsMirrorOptions.InsightsObservationActionId</c> is unset (Empty Guid), the
/// real <c>DataverseObservationMirror</c> (task 051) cannot run safely, so the DI module
/// keeps this no-op registered. In environments where the configuration is populated, the
/// module swaps to <c>DataverseObservationMirror</c>.
/// </para>
/// <para>
/// <b>Why a no-op rather than commented-out call site</b>: the orchestrator (task 040) ships
/// with the mirror call wired up. Unit tests verify that the mirror is INVOKED — not that
/// a real Dataverse row is written. This matches the task 040 brief's scope clarification:
/// "test verifies the hook is INVOKED, not that a real Dataverse row is written."
/// </para>
/// <para>
/// <b>Structured logging</b>: stable <c>EventId(8041, "ObservationMirrorNoOp")</c> so
/// App Insights queries can confirm the mirror path is being exercised in pre-prod
/// before the real impl is enabled.
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
