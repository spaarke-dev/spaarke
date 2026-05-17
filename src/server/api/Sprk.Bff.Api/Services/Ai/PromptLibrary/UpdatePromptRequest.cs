namespace Sprk.Bff.Api.Services.Ai.PromptLibrary;

/// <summary>
/// Request body for updating an existing prompt template.
/// All fields are optional — omitted fields are left unchanged.
/// </summary>
/// <param name="Name">New display name, or <c>null</c> to leave unchanged.</param>
/// <param name="Body">New template body, or <c>null</c> to leave unchanged.</param>
/// <param name="Description">New description, or <c>null</c> to leave unchanged.</param>
/// <param name="Tags">Replacement tag list, or <c>null</c> to leave unchanged.</param>
/// <param name="Variables">Replacement variable list, or <c>null</c> to leave unchanged.</param>
public record UpdatePromptRequest(
    string? Name = null,
    string? Body = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<TemplateVariable>? Variables = null);
