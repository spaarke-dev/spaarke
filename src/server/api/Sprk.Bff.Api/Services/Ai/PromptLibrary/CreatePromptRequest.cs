namespace Sprk.Bff.Api.Services.Ai.PromptLibrary;

/// <summary>
/// Request body for creating a new prompt template.
/// </summary>
/// <param name="Name">Required. Short display name.</param>
/// <param name="Body">Required. Template text with optional <c>{{variable}}</c> placeholders.</param>
/// <param name="Ownership">
/// Tier for the new template.
/// Only <see cref="PromptOwnership.Personal"/> and <see cref="PromptOwnership.Team"/> are accepted
/// via the API — Org and System templates are managed outside the API.
/// </param>
/// <param name="OwnerId">
/// Required when <paramref name="Ownership"/> is <see cref="PromptOwnership.Team"/>.
/// Identifies the team that owns the template.
/// </param>
/// <param name="Description">Optional longer description.</param>
/// <param name="Tags">Optional list of searchable labels.</param>
/// <param name="Variables">Declared variables for the template body.</param>
public record CreatePromptRequest(
    string Name,
    string Body,
    PromptOwnership Ownership = PromptOwnership.Personal,
    string? OwnerId = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<TemplateVariable>? Variables = null);
