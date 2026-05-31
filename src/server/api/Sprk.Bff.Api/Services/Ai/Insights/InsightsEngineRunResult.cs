using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.Insights;

/// <summary>
/// Result of draining a single Insights-mode playbook engine stream. Carries exactly one
/// of <see cref="Artifact"/> (sufficient-evidence path produced a
/// <see cref="ReturnInsightArtifactNode"/> output) or <see cref="Decline"/>
/// (insufficient-evidence path produced a <see cref="DeclineToFindNode"/> output).
/// </summary>
/// <remarks>
/// <para>
/// <b>Introduced by task 071</b> (Wave 8.5 pre-deploy gap fix, 2026-05-29) to close the
/// gap where <see cref="InsightsPlaybookExecutionCache.DrainEngineStreamAsync"/> only
/// scanned for <see cref="ReturnInsightArtifactNode"/> NodeCompleted events and ignored
/// <see cref="DeclineToFindNode"/> events — causing <see cref="InsightsOrchestrator"/>
/// to return a scaffold "no-artifact-produced" decline on every insufficient-evidence
/// invocation instead of the real <see cref="DeclineResponse"/> with structured
/// <see cref="DeclineResponse.MinimumEvidenceNeeded"/> gap analysis.
/// </para>
/// <para>
/// <b>Invariant</b>: per the predict-matter-cost playbook's checkSufficiency branch logic,
/// exactly one of <see cref="Artifact"/> or <see cref="Decline"/> is non-null in a
/// well-formed engine run. Both null is a defensive scenario the orchestrator logs as
/// Warning and surfaces as a scaffold decline; both non-null (also defensive) prefers
/// <see cref="Artifact"/> ("sufficient path wins") because that's the consumer-facing
/// success contract.
/// </para>
/// <para>
/// <b>Zone A internal type</b> per SPEC §3.5 — lives under <c>Services/Ai/Insights/</c>
/// and is only visible to <see cref="IInsightsPlaybookExecutionCache"/> +
/// <see cref="InsightsOrchestrator"/>. Zone B callers see only
/// <see cref="Sprk.Bff.Api.Models.Ai.PublicContracts.InsightsAgentResult"/> via the
/// <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.IInsightsAi"/> facade.
/// </para>
/// </remarks>
/// <param name="Artifact">The synthesised <see cref="InsightArtifact"/> from a sufficient-evidence
/// run, or null if the engine took the decline path.</param>
/// <param name="Decline">The structured <see cref="DeclineResponse"/> from an insufficient-evidence
/// run, or null if the engine produced an artifact.</param>
public sealed record InsightsEngineRunResult(
    InsightArtifact? Artifact,
    DeclineResponse? Decline)
{
    /// <summary>
    /// True when the run produced an artifact (sufficient-evidence path).
    /// Mutually exclusive with <see cref="HasDecline"/> in well-formed runs.
    /// </summary>
    public bool HasArtifact => Artifact is not null;

    /// <summary>
    /// True when the run produced a structured decline (insufficient-evidence path).
    /// Mutually exclusive with <see cref="HasArtifact"/> in well-formed runs.
    /// </summary>
    public bool HasDecline => Decline is not null;

    /// <summary>
    /// True when the run produced neither artifact nor decline. Indicates a malformed
    /// playbook or engine error; <see cref="InsightsOrchestrator"/> logs a Warning and
    /// emits a scaffold decline so the facade contract's "exactly one of artifact/decline"
    /// invariant is preserved for Zone B callers.
    /// </summary>
    public bool IsEmpty => Artifact is null && Decline is null;

    /// <summary>Sentinel for the "engine produced nothing" defensive case.</summary>
    public static readonly InsightsEngineRunResult Empty = new(Artifact: null, Decline: null);

    /// <summary>Construct a result carrying an InsightArtifact (sufficient-evidence path).</summary>
    public static InsightsEngineRunResult FromArtifact(InsightArtifact artifact)
        => new(artifact ?? throw new ArgumentNullException(nameof(artifact)), null);

    /// <summary>Construct a result carrying a DeclineResponse (insufficient-evidence path).</summary>
    public static InsightsEngineRunResult FromDecline(DeclineResponse decline)
        => new(null, decline ?? throw new ArgumentNullException(nameof(decline)));
}
