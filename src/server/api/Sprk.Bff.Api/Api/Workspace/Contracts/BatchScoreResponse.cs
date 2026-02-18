namespace Sprk.Bff.Api.Api.Workspace.Contracts;

/// <summary>
/// Response DTO for the batch scoring endpoint.
/// Contains scores for all events that were successfully calculated.
/// </summary>
/// <param name="Results">
/// Score results in the same order as the request items.
/// One <see cref="ScoreResponse"/> per input <see cref="ScoreRequest"/>.
/// </param>
public record BatchScoreResponse(IReadOnlyList<ScoreResponse> Results);
