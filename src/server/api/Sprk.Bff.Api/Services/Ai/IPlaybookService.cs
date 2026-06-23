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

    /// <summary>
    /// List template playbooks available for cloning.
    /// Templates are standard playbooks marked with IsTemplate = true.
    /// </summary>
    /// <param name="query">Query parameters for filtering and pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of template playbook summaries.</returns>
    Task<PlaybookListResponse> ListTemplatesAsync(
        PlaybookQueryParameters query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clone a playbook (typically a template) to create a new playbook owned by the user.
    /// </summary>
    /// <param name="sourcePlaybookId">ID of the playbook to clone.</param>
    /// <param name="userId">ID of the user who will own the cloned playbook.</param>
    /// <param name="newName">Optional new name for the cloned playbook. If null, uses "[SourceName] (Copy)".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created cloned playbook.</returns>
    Task<PlaybookResponse> ClonePlaybookAsync(
        Guid sourcePlaybookId,
        Guid userId,
        string? newName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerate ALL active (<c>statecode == 0</c>) playbooks across the tenant for
    /// chat-routing-redesign-r1 FR-13 drift detection. Pages through Dataverse 100 rows
    /// at a time and yields each <see cref="PlaybookResponse"/> as it is materialized.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-tenant scoping flows via <c>JobContract.SubjectId</c> upstream — this
    /// method intentionally returns all Active rows visible to the BFF's Dataverse
    /// application user.
    /// </para>
    /// <para>
    /// N:N relationship collections (<c>ActionIds</c>, <c>SkillIds</c>, etc.) are NOT
    /// populated because drift detection only requires the embed-input fields plus the
    /// tracking fields (<c>sprk_indexstatus</c>, <c>sprk_indexhash</c>, <c>sprk_lastindexedat</c>).
    /// Callers requiring relationships should call <see cref="GetPlaybookAsync"/> by ID
    /// once a row of interest is found.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token. Honored between pages and
    /// between yielded rows.</param>
    /// <returns>Async enumerable of active playbooks.</returns>
    IAsyncEnumerable<PlaybookResponse> ListAllActivePlaybooksAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <c>sprk_indexstatus</c> (and optionally <c>sprk_indexhash</c>,
    /// <c>sprk_lastindexedat</c>, <c>sprk_lastindexerror</c>) on the playbook row for
    /// chat-routing-redesign-r1 FR-13 indexing-state tracking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Behavior matrix:
    /// <list type="bullet">
    ///   <item><description><c>sprk_indexstatus</c> is always set to <paramref name="statusCode"/>.</description></item>
    ///   <item><description><c>sprk_indexhash</c> is set to <paramref name="indexHash"/> (null clears the field).</description></item>
    ///   <item><description><c>sprk_lastindexedat</c> is stamped to <see cref="DateTime.UtcNow"/>
    ///   ONLY when <paramref name="statusCode"/> indicates successful indexing (100000002 = Indexed).
    ///   On Stale / Failed transitions the previous timestamp is intentionally preserved.</description></item>
    ///   <item><description><c>sprk_lastindexerror</c> is set to <paramref name="lastError"/>
    ///   (null clears to empty string).</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// ADR-015: this method MUST NOT log <paramref name="lastError"/> content or
    /// <paramref name="indexHash"/> content. Only playbook ID and status code may
    /// be surfaced in BFF logs. Error content flows to the Dataverse column for
    /// admin visibility but never to logs.
    /// </para>
    /// </remarks>
    /// <param name="playbookId">Playbook ID to update.</param>
    /// <param name="statusCode">New <c>sprk_indexstatus</c> numeric option-set code.
    /// Allowed: 100000000 (NotIndexed), 100000001 (Pending), 100000002 (Indexed),
    /// 100000003 (Stale), 100000004 (Failed).</param>
    /// <param name="indexHash">SHA-256 hex digest to store, or null to clear.</param>
    /// <param name="lastError">Error message to store, or null to clear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateIndexStatusAsync(
        Guid playbookId,
        int statusCode,
        string? indexHash,
        string? lastError,
        CancellationToken cancellationToken = default);
}
