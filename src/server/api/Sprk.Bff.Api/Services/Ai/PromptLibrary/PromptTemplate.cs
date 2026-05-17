namespace Sprk.Bff.Api.Services.Ai.PromptLibrary;

/// <summary>
/// A reusable prompt template with optional variable placeholders.
///
/// Template body uses <c>{{variableName}}</c> syntax.  Variables are substituted at render time
/// via <see cref="IPromptLibraryService.RenderAsync"/>.
///
/// Partition key in Cosmos DB is <c>/ownerId</c> (userId for Personal, teamId for Team).
/// Org and System templates are stored in Dataverse and surfaced read-only by the API.
/// </summary>
/// <param name="Id">Unique identifier (GUID string).</param>
/// <param name="TenantId">Azure AD tenant identifier — all records are tenant-scoped (ADR-015, NFR-09).</param>
/// <param name="Name">Short display name shown in the template picker.</param>
/// <param name="Description">Optional longer description explaining the template's purpose.</param>
/// <param name="Body">The template text with <c>{{variable}}</c> placeholders.</param>
/// <param name="Ownership">Ownership tier that determines storage and visibility rules.</param>
/// <param name="OwnerId">User ID (Personal) or Team ID (Team). Null for Org/System tiers.</param>
/// <param name="Tags">Searchable labels (e.g. "contract", "discovery").</param>
/// <param name="Variables">Declared variables — defines the placeholder schema for the UI.</param>
/// <param name="CreatedAt">UTC creation timestamp.</param>
/// <param name="UpdatedAt">UTC last-update timestamp.</param>
public record PromptTemplate(
    string Id,
    string TenantId,
    string Name,
    string? Description,
    string Body,
    PromptOwnership Ownership,
    string? OwnerId,
    IReadOnlyList<string> Tags,
    IReadOnlyList<TemplateVariable> Variables,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
