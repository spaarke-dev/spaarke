using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for managing analysis playbooks in Dataverse.
/// Provides CRUD operations for playbook entities and their N:N relationships.
/// </summary>
public interface IPlaybookService
{
    /// <summary>
    /// Create a new playbook.
    /// </summary>
    /// <param name="request">Playbook creation request.</param>
    /// <param name="userId">ID of the user creating the playbook.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created playbook response.</returns>
    Task<PlaybookResponse> CreatePlaybookAsync(
        SavePlaybookRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing playbook.
    /// </summary>
    /// <param name="playbookId">Playbook ID to update.</param>
    /// <param name="request">Playbook update request.</param>
    /// <param name="userId">ID of the user updating the playbook.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated playbook response.</returns>
    Task<PlaybookResponse> UpdatePlaybookAsync(
        Guid playbookId,
        SavePlaybookRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a playbook by ID.
    /// </summary>
    /// <param name="playbookId">Playbook ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Playbook response or null if not found.</returns>
    Task<PlaybookResponse?> GetPlaybookAsync(
        Guid playbookId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if user has access to a playbook.
    /// User has access if they own the playbook or it's public.
    /// </summary>
    /// <param name="playbookId">Playbook ID.</param>
    /// <param name="userId">User ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if user has access.</returns>
    Task<bool> UserHasAccessAsync(
        Guid playbookId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate playbook configuration.
    /// </summary>
    /// <param name="request">Playbook request to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result.</returns>
    Task<PlaybookValidationResult> ValidateAsync(
        SavePlaybookRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List playbooks for a user (owned playbooks).
    /// </summary>
    /// <param name="userId">User ID to filter by owner.</param>
    /// <param name="query">Query parameters for filtering and pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of playbook summaries.</returns>
    Task<PlaybookListResponse> ListUserPlaybooksAsync(
        Guid userId,
        PlaybookQueryParameters query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List public playbooks (shared by other users).
    /// </summary>
    /// <param name="query">Query parameters for filtering and pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of public playbook summaries.</returns>
    Task<PlaybookListResponse> ListPublicPlaybooksAsync(
        PlaybookQueryParameters query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a playbook by name.
    /// </summary>
    /// <remarks>
    /// Used for system playbooks like "Document Profile" where lookup by name is more
    /// flexible than hardcoded IDs. Results are cached for performance.
    /// </remarks>
    /// <param name="name">Exact playbook name to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Playbook response.</returns>
    /// <exception cref="PlaybookNotFoundException">Thrown when playbook with specified name is not found.</exception>
    Task<PlaybookResponse> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get canvas layout for a playbook.
    /// </summary>
    /// <param name="playbookId">Playbook ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Canvas layout response with layout data, or null layout if not set.</returns>
    Task<CanvasLayoutResponse?> GetCanvasLayoutAsync(
        Guid playbookId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save canvas layout for a playbook.
    /// </summary>
    /// <param name="playbookId">Playbook ID.</param>
    /// <param name="layout">Canvas layout to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated canvas layout response.</returns>
    Task<CanvasLayoutResponse> SaveCanvasLayoutAsync(
        Guid playbookId,
        CanvasLayoutDto layout,
        CancellationToken cancellationToken = default);
}
