namespace Sprk.Bff.Api.Services.Ai.PromptLibrary;

/// <summary>
/// CRUD operations and variable rendering for prompt templates across the four ownership tiers.
///
/// Storage routing:
///   - Personal + Team → Cosmos DB <c>prompts</c> container (partition key: <c>/ownerId</c>)
///   - Org + System    → Dataverse <c>sprk_prompttemplate</c> table (read-only; AIPU2 integration task)
///
/// All operations are tenant-scoped (ADR-015, NFR-09).
/// Write operations (Create/Update/Delete) are forbidden for Org and System templates
/// and will throw <see cref="InvalidOperationException"/> with HTTP 403 semantics.
/// </summary>
public interface IPromptLibraryService
{
    /// <summary>
    /// Returns all templates visible to the specified user:
    /// personal templates owned by <paramref name="userId"/>,
    /// team templates owned by any team the user belongs to (caller must supply team IDs),
    /// plus all Org and System templates (read from Dataverse — deferred, returns empty for now).
    /// </summary>
    /// <param name="tenantId">Azure AD tenant identifier.</param>
    /// <param name="userId">AAD object ID of the requesting user.</param>
    /// <param name="teamIds">Optional list of team IDs the user belongs to (for Team-tier visibility).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<PromptTemplate>> ListAsync(
        string tenantId,
        string userId,
        IReadOnlyList<string>? teamIds = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a single template by ID, or <c>null</c> when not found.
    /// Checks Cosmos (Personal + Team) then Dataverse (Org + System).
    /// </summary>
    /// <param name="tenantId">Azure AD tenant identifier.</param>
    /// <param name="templateId">Template identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PromptTemplate?> GetAsync(string tenantId, string templateId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new Personal or Team template in Cosmos DB.
    /// Throws <see cref="InvalidOperationException"/> when <see cref="CreatePromptRequest.Ownership"/>
    /// is <see cref="PromptOwnership.Organization"/> or <see cref="PromptOwnership.System"/>.
    /// </summary>
    /// <param name="tenantId">Azure AD tenant identifier.</param>
    /// <param name="userId">AAD object ID of the creating user (becomes OwnerId for Personal templates).</param>
    /// <param name="request">Template content and metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PromptTemplate> CreateAsync(
        string tenantId,
        string userId,
        CreatePromptRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Applies partial updates to a Personal or Team template in Cosmos DB.
    /// Throws <see cref="InvalidOperationException"/> when the resolved template belongs to the
    /// Org or System tier (HTTP 403 semantics).
    /// Throws <see cref="KeyNotFoundException"/> when the template does not exist.
    /// </summary>
    /// <param name="tenantId">Azure AD tenant identifier.</param>
    /// <param name="templateId">Template identifier.</param>
    /// <param name="request">Fields to update (null fields are ignored).</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(
        string tenantId,
        string templateId,
        UpdatePromptRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a Personal or Team template from Cosmos DB.
    /// Throws <see cref="InvalidOperationException"/> when the resolved template belongs to the
    /// Org or System tier (HTTP 403 semantics).
    /// Throws <see cref="KeyNotFoundException"/> when the template does not exist.
    /// </summary>
    /// <param name="tenantId">Azure AD tenant identifier.</param>
    /// <param name="templateId">Template identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(string tenantId, string templateId, CancellationToken ct = default);

    /// <summary>
    /// Renders a template by substituting <c>{{variableName}}</c> placeholders with the
    /// values from <paramref name="variables"/>.
    ///
    /// Validation: all variables declared as <see cref="TemplateVariable.Required"/> must be
    /// present in <paramref name="variables"/>; missing keys cause an
    /// <see cref="ArgumentException"/> listing the missing names.
    /// </summary>
    /// <param name="tenantId">Azure AD tenant identifier.</param>
    /// <param name="templateId">Template identifier.</param>
    /// <param name="variables">Map of placeholder name → resolved value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rendered prompt string.</returns>
    Task<string> RenderAsync(
        string tenantId,
        string templateId,
        Dictionary<string, string> variables,
        CancellationToken ct = default);
}
