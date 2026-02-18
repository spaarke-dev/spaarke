namespace Sprk.Bff.Api.Api.Workspace.Models;

/// <summary>
/// Response DTO for the AI Summary endpoint.
/// </summary>
/// <param name="Analysis">Narrative analysis text produced by the AI Playbook.</param>
/// <param name="SuggestedActions">Array of suggested action strings derived from the analysis.</param>
/// <param name="Confidence">Confidence score from the AI Playbook, ranging 0.0 to 1.0.</param>
/// <param name="GeneratedAt">UTC timestamp when the analysis was produced.</param>
public record AiSummaryResponse(
    string Analysis,
    string[] SuggestedActions,
    double Confidence,
    DateTimeOffset GeneratedAt);
