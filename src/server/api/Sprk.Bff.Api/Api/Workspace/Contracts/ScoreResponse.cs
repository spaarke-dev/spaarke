using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Api.Workspace.Contracts;

/// <summary>
/// Response DTO containing both priority and effort scores for a single event.
/// Includes full factor breakdowns so the UI can render the scoring grid.
/// </summary>
/// <param name="EventId">Dataverse sprk_event GUID that was scored.</param>
/// <param name="PriorityScore">Deterministic priority score (0-100).</param>
/// <param name="PriorityLevel">Human-readable level: Critical, High, Medium, Low.</param>
/// <param name="PriorityFactors">Per-factor breakdown with points and explanation.</param>
/// <param name="PriorityReason">Compact human-readable reason string.</param>
/// <param name="EffortScore">Effort score (0-100, capped).</param>
/// <param name="EffortLevel">Human-readable level: High, Med, Low.</param>
/// <param name="BaseEffort">Base effort before multipliers are applied.</param>
/// <param name="EffortMultipliers">Complexity multipliers that were applied.</param>
/// <param name="EffortReason">Compact human-readable reason string.</param>
public record ScoreResponse(
    Guid EventId,
    int PriorityScore,
    string PriorityLevel,
    IReadOnlyList<PriorityFactorResult> PriorityFactors,
    string PriorityReason,
    int EffortScore,
    string EffortLevel,
    int BaseEffort,
    IReadOnlyList<AppliedMultiplier> EffortMultipliers,
    string EffortReason);
