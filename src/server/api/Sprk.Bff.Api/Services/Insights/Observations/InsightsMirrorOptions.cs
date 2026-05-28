namespace Sprk.Bff.Api.Services.Insights.Observations;

/// <summary>
/// Configuration for the D-P11 Observation mirror sync (task 051) — controls the Zone B
/// <see cref="DataverseObservationMirror"/> behavior when projecting emitted Observations
/// to <c>sprk_analysis</c> rows for the model-driven review surface (task 052).
/// </summary>
/// <remarks>
/// <para>
/// <b>Bound at</b>: <c>Insights:Mirror</c> in <c>appsettings.json</c> /
/// <c>appsettings.{Environment}.json</c> + Key Vault overrides.
/// </para>
/// <para>
/// <b>Deployment prerequisite</b> (per <c>projects/.../notes/sprk-analysis-polymorphic-confirmation.md</c>):
/// before <see cref="InsightsObservationActionId"/> can be populated, a deployment-team
/// step must create a dedicated <c>sprk_analysisaction</c> row representing the
/// "Insights Observation Mirror" semantic. The new row's GUID is then captured here.
/// </para>
/// <para>
/// <b>Dev-safe default</b>: <see cref="InsightsObservationActionId"/> defaults to
/// <see cref="Guid.Empty"/>. When unset, <c>DataverseObservationMirror</c> safely falls back
/// to no-op behavior (matching <c>NoOpObservationMirror</c>) with a one-time Warning log,
/// so dev/test environments without the prerequisite row do not write malformed rows.
/// </para>
/// </remarks>
public sealed class InsightsMirrorOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Insights:Mirror";

    /// <summary>
    /// GUID of the <c>sprk_analysisaction</c> row representing the
    /// "Insights Observation Mirror" semantic (suggested <c>sprk_actioncode = INS-OBS</c>).
    /// Required for <see cref="DataverseObservationMirror"/> to write rows; when
    /// <see cref="Guid.Empty"/> the mirror logs a Warning and skips writes (no-op).
    /// </summary>
    public Guid InsightsObservationActionId { get; init; } = Guid.Empty;

    /// <summary>
    /// Kill switch — when <c>false</c>, the mirror never writes (logs at Information level
    /// and returns successfully). Defaults to <c>true</c>. Operators may toggle off if a
    /// runaway ingest is producing pathological Dataverse load.
    /// </summary>
    public bool EnableMirror { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, the mirror queries for an existing row with the same idempotency
    /// key before writing (additional Dataverse roundtrip). Defaults to <c>true</c> per the
    /// task 051 idempotency contract. Operators may disable in environments where the
    /// orchestrator is guaranteed not to re-run.
    /// </summary>
    public bool EnableIdempotencyCheck { get; init; } = true;

    /// <summary>
    /// Probability in [0.0, 1.0] that a mirrored Observation row is marked
    /// <c>sprk_disposition = PendingReview (100000000)</c> for the model-driven review queue
    /// surfaced by task 052 (D-P11). When the random draw falls within the sampling band,
    /// the row carries the PendingReview disposition (visible in the "Insights Observations
    /// — Review Queue" view); otherwise <c>sprk_disposition</c> is left null and the row is
    /// invisible to reviewers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Phase 1 default = 1.0 (100% sampling)</b> — the calibration period requires every
    /// Observation be reviewable so SMEs can establish ground-truth disposition rates per
    /// predicate / per Layer. Once threshold-drift data is available (Phase 1.5+), operators
    /// may lower to 0.10 (initial steady-state) then 0.01–0.02 (long-run QA sampling).
    /// </para>
    /// <para>
    /// <b>Values outside [0.0, 1.0]</b> are clamped at the mirror callsite: ≤0 disables
    /// sampling (no row ever marked PendingReview), ≥1 enables 100% sampling.
    /// </para>
    /// </remarks>
    public double SamplingPercentage { get; init; } = 1.0;
}
