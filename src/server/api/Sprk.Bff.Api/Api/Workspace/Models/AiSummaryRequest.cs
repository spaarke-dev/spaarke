namespace Sprk.Bff.Api.Api.Workspace.Models;

/// <summary>
/// Request DTO for the AI Summary endpoint.
/// </summary>
/// <param name="EntityType">Dataverse entity logical name (e.g., "sprk_event", "sprk_matter").</param>
/// <param name="EntityId">Primary key of the entity to summarize.</param>
/// <param name="Context">Optional caller-supplied context string (e.g., "This is a to-do item flagged for priority review").</param>
public record AiSummaryRequest(
    string EntityType,
    Guid EntityId,
    string? Context);
