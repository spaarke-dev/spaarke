namespace Sprk.Bff.Api.Api.Workspace.Contracts;

/// <summary>
/// Request DTO for batch scoring of multiple events in a single API call.
/// Used on initial page load to calculate scores for all visible to-do items
/// at once, reducing N individual API round-trips to 1.
/// </summary>
/// <param name="Items">
/// List of individual score requests. Maximum 50 items per batch.
/// Each item carries pre-assembled scoring inputs the client already has.
/// </param>
public record BatchScoreRequest(IReadOnlyList<ScoreRequest> Items);
