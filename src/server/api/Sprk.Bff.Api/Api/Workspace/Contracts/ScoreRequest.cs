using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Api.Workspace.Contracts;

/// <summary>
/// Request DTO for a single event scoring operation.
/// The client assembles the scoring inputs from data it already holds
/// (Dataverse entity fields) and sends them to the BFF for calculation.
/// This avoids a server-side Dataverse round-trip in R1.
/// </summary>
/// <param name="EventId">Dataverse sprk_event GUID being scored.</param>
/// <param name="PriorityInput">Pre-assembled priority scoring factors.</param>
/// <param name="EffortInput">Pre-assembled effort scoring factors.</param>
public record ScoreRequest(
    Guid EventId,
    PriorityScoreInput PriorityInput,
    EffortScoreInput EffortInput);
