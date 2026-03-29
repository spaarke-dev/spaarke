using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Workspace;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Handles Dataverse CRUD operations for the sprk_workspacelayout entity.
/// System layouts (e.g., "Corporate Workspace") are code constants and never stored in Dataverse.
/// </summary>
/// <remarks>
/// Follows ADR-001 (Minimal API patterns) and ADR-010 (DI minimalism — concrete type, no interface).
/// Registered as Scoped because it depends on per-request Dataverse query context.
///
/// Business rules:
///   - Max 10 user layouts per user (enforced on create)
///   - Single default per user (enforced on create/update)
///   - System layouts are read-only and non-deletable
///   - All Dataverse queries are filtered by ownerid for user isolation
/// </remarks>
public sealed class WorkspaceLayoutService
{
    private const string EntityName = "sprk_workspacelayout";
    private const int MaxUserLayouts = 10;

    private static readonly string[] SelectColumns =
    [
        "sprk_workspacelayoutid",
        "sprk_name",
        "sprk_layouttemplateid",
        "sprk_sectionsjson",
        "sprk_isdefault",
        "sprk_sortorder"
    ];

    private readonly IGenericEntityService _entityService;
    private readonly ILogger<WorkspaceLayoutService> _logger;

    public WorkspaceLayoutService(
        IGenericEntityService entityService,
        ILogger<WorkspaceLayoutService> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns all layouts for the specified user: system layouts first, then user layouts
    /// sorted by sortOrder.
    /// </summary>
    /// <param name="userId">The Entra ID object ID of the authenticated user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Combined list of system and user layouts.</returns>
    public async Task<IReadOnlyList<WorkspaceLayoutDto>> GetLayoutsAsync(
        string userId,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Loading layouts for user {UserId}", userId);

        var userLayouts = await QueryUserLayoutsAsync(userId, ct);

        // System layouts first, then user layouts sorted by sortOrder
        var result = new List<WorkspaceLayoutDto>(SystemWorkspaceLayouts.All.Count + userLayouts.Count);
        result.AddRange(SystemWorkspaceLayouts.All);
        result.AddRange(userLayouts.OrderBy(l => l.SortOrder ?? int.MaxValue));

        _logger.LogDebug(
            "Returning {Total} layouts ({System} system, {User} user) for user {UserId}",
            result.Count, SystemWorkspaceLayouts.All.Count, userLayouts.Count, userId);

        return result;
    }

    /// <summary>
    /// Returns a specific layout by ID. Checks system layouts first, then queries Dataverse.
    /// </summary>
    /// <param name="id">The layout ID.</param>
    /// <param name="userId">The Entra ID object ID of the authenticated user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The layout, or null if not found.</returns>
    public async Task<WorkspaceLayoutDto?> GetLayoutByIdAsync(
        Guid id,
        string userId,
        CancellationToken ct = default)
    {
        // Check system layouts first (no Dataverse query needed)
        var systemLayout = SystemWorkspaceLayouts.GetById(id);
        if (systemLayout is not null)
            return systemLayout;

        _logger.LogDebug("Loading layout {LayoutId} for user {UserId}", id, userId);

        try
        {
            var entity = await _entityService.RetrieveAsync(EntityName, id, SelectColumns, ct);

            // Verify ownership — the user can only see their own layouts
            var ownerId = entity.GetAttributeValue<EntityReference>("ownerid")?.Id;
            if (ownerId.HasValue && Guid.TryParse(userId, out var userGuid) && ownerId.Value != userGuid)
            {
                _logger.LogWarning(
                    "User {UserId} attempted to access layout {LayoutId} owned by {OwnerId}",
                    userId, id, ownerId);
                return null;
            }

            return MapToDto(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve layout {LayoutId} for user {UserId}", id, userId);
            return null;
        }
    }

    /// <summary>
    /// Returns the user's default layout. Falls back to the first system layout
    /// if no user default is set.
    /// </summary>
    /// <param name="userId">The Entra ID object ID of the authenticated user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The default layout.</returns>
    public async Task<WorkspaceLayoutDto> GetDefaultLayoutAsync(
        string userId,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Loading default layout for user {UserId}", userId);

        var query = new QueryExpression(EntityName)
        {
            ColumnSet = new ColumnSet(SelectColumns)
        };

        query.Criteria.AddCondition("sprk_isdefault", ConditionOperator.Equal, true);

        if (Guid.TryParse(userId, out var userGuid))
        {
            query.Criteria.AddCondition("ownerid", ConditionOperator.Equal, userGuid);
        }

        query.TopCount = 1;

        try
        {
            var results = await _entityService.RetrieveMultipleAsync(query, ct);

            if (results.Entities.Count > 0)
            {
                var dto = MapToDto(results.Entities[0]);
                _logger.LogDebug(
                    "Found user default layout {LayoutId} for user {UserId}",
                    dto.Id, userId);
                return dto;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query default layout for user {UserId}", userId);
        }

        // Fall back to system default
        _logger.LogDebug("No user default found, returning system default for user {UserId}", userId);
        return SystemWorkspaceLayouts.CorporateWorkspace;
    }

    /// <summary>
    /// Creates a new user layout. Enforces max 10 user layouts per user.
    /// If the new layout is set as default, clears isDefault on all other user layouts.
    /// </summary>
    /// <param name="request">The creation request.</param>
    /// <param name="userId">The Entra ID object ID of the authenticated user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created layout DTO, or an error message.</returns>
    public async Task<(WorkspaceLayoutDto? Layout, string? Error)> CreateLayoutAsync(
        CreateWorkspaceLayoutRequest request,
        string userId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Creating layout '{Name}' for user {UserId} (isDefault={IsDefault})",
            request.Name, userId, request.IsDefault);

        // Enforce max 10 user layouts
        var existingLayouts = await QueryUserLayoutsAsync(userId, ct);
        if (existingLayouts.Count >= MaxUserLayouts)
        {
            _logger.LogWarning(
                "User {UserId} has reached the maximum of {Max} layouts",
                userId, MaxUserLayouts);
            return (null, $"Maximum of {MaxUserLayouts} user workspaces reached. Delete an existing workspace to create a new one.");
        }

        // If setting as default, clear existing defaults first
        if (request.IsDefault)
        {
            await ClearUserDefaultsAsync(userId, existingLayouts, ct);
        }

        // Determine sort order (append after existing)
        var maxSortOrder = existingLayouts.Count > 0
            ? existingLayouts.Max(l => l.SortOrder ?? 0)
            : 0;

        var entity = new Entity(EntityName)
        {
            ["sprk_name"] = request.Name,
            ["sprk_layouttemplateid"] = request.LayoutTemplateId,
            ["sprk_sectionsjson"] = request.SectionsJson,
            ["sprk_isdefault"] = request.IsDefault,
            ["sprk_sortorder"] = maxSortOrder + 1
        };

        try
        {
            var id = await _entityService.CreateAsync(entity, ct);

            _logger.LogInformation(
                "Created layout {LayoutId} '{Name}' for user {UserId}",
                id, request.Name, userId);

            return (new WorkspaceLayoutDto
            {
                Id = id,
                Name = request.Name,
                LayoutTemplateId = request.LayoutTemplateId,
                SectionsJson = request.SectionsJson,
                IsDefault = request.IsDefault,
                SortOrder = maxSortOrder + 1,
                IsSystem = false
            }, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create layout for user {UserId}", userId);
            return (null, "Failed to create workspace layout. Please try again.");
        }
    }

    /// <summary>
    /// Updates an existing user layout. Rejects updates to system layouts.
    /// If setting as default, clears isDefault on all other user layouts.
    /// </summary>
    /// <param name="id">The layout ID to update.</param>
    /// <param name="request">The update request.</param>
    /// <param name="userId">The Entra ID object ID of the authenticated user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated layout DTO, or an error message.</returns>
    public async Task<(WorkspaceLayoutDto? Layout, string? Error)> UpdateLayoutAsync(
        Guid id,
        UpdateWorkspaceLayoutRequest request,
        string userId,
        CancellationToken ct = default)
    {
        // Reject updates to system layouts
        if (SystemWorkspaceLayouts.IsSystemLayout(id))
        {
            _logger.LogWarning(
                "User {UserId} attempted to update system layout {LayoutId}",
                userId, id);
            return (null, "System workspaces cannot be modified.");
        }

        _logger.LogInformation(
            "Updating layout {LayoutId} for user {UserId} (isDefault={IsDefault})",
            id, userId, request.IsDefault);

        // Verify the layout exists and belongs to this user
        var existing = await GetLayoutByIdAsync(id, userId, ct);
        if (existing is null)
        {
            return (null, "Workspace layout not found.");
        }

        // If setting as default, clear existing defaults first
        if (request.IsDefault)
        {
            var userLayouts = await QueryUserLayoutsAsync(userId, ct);
            await ClearUserDefaultsAsync(userId, userLayouts, ct, excludeId: id);
        }

        var fields = new Dictionary<string, object>
        {
            ["sprk_name"] = request.Name,
            ["sprk_layouttemplateid"] = request.LayoutTemplateId,
            ["sprk_sectionsjson"] = request.SectionsJson,
            ["sprk_isdefault"] = request.IsDefault
        };

        try
        {
            await _entityService.UpdateAsync(EntityName, id, fields, ct);

            _logger.LogInformation(
                "Updated layout {LayoutId} for user {UserId}",
                id, userId);

            return (new WorkspaceLayoutDto
            {
                Id = id,
                Name = request.Name,
                LayoutTemplateId = request.LayoutTemplateId,
                SectionsJson = request.SectionsJson,
                IsDefault = request.IsDefault,
                SortOrder = existing.SortOrder,
                IsSystem = false
            }, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update layout {LayoutId} for user {UserId}", id, userId);
            return (null, "Failed to update workspace layout. Please try again.");
        }
    }

    /// <summary>
    /// Deletes a user layout. Rejects deletion of system layouts.
    /// If the deleted layout was the default, no new default is auto-assigned.
    /// </summary>
    /// <param name="id">The layout ID to delete.</param>
    /// <param name="userId">The Entra ID object ID of the authenticated user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Null on success, or an error message.</returns>
    public async Task<string?> DeleteLayoutAsync(
        Guid id,
        string userId,
        CancellationToken ct = default)
    {
        // Reject deletion of system layouts
        if (SystemWorkspaceLayouts.IsSystemLayout(id))
        {
            _logger.LogWarning(
                "User {UserId} attempted to delete system layout {LayoutId}",
                userId, id);
            return "System workspaces cannot be deleted.";
        }

        _logger.LogInformation("Deleting layout {LayoutId} for user {UserId}", id, userId);

        // Verify the layout exists and belongs to this user
        var existing = await GetLayoutByIdAsync(id, userId, ct);
        if (existing is null)
        {
            return "Workspace layout not found.";
        }

        try
        {
            // Delete via update to deactivate (statecode = 1) — standard Dataverse soft delete
            var fields = new Dictionary<string, object>
            {
                ["statecode"] = 1,
                ["statuscode"] = 2
            };
            await _entityService.UpdateAsync(EntityName, id, fields, ct);

            _logger.LogInformation("Deleted layout {LayoutId} for user {UserId}", id, userId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete layout {LayoutId} for user {UserId}", id, userId);
            return "Failed to delete workspace layout. Please try again.";
        }
    }

    #region Private Helpers

    /// <summary>
    /// Queries all active user layouts from Dataverse filtered by ownerid.
    /// </summary>
    private async Task<IReadOnlyList<WorkspaceLayoutDto>> QueryUserLayoutsAsync(
        string userId,
        CancellationToken ct)
    {
        var query = new QueryExpression(EntityName)
        {
            ColumnSet = new ColumnSet(SelectColumns)
        };

        // Active records only
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

        // Owned by the specified user (user isolation)
        if (Guid.TryParse(userId, out var userGuid))
        {
            query.Criteria.AddCondition("ownerid", ConditionOperator.Equal, userGuid);
        }

        query.AddOrder("sprk_sortorder", OrderType.Ascending);

        try
        {
            var results = await _entityService.RetrieveMultipleAsync(query, ct);
            var layouts = new List<WorkspaceLayoutDto>(results.Entities.Count);

            foreach (var entity in results.Entities)
            {
                layouts.Add(MapToDto(entity));
            }

            return layouts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query layouts from Dataverse for user {UserId}", userId);
            return Array.Empty<WorkspaceLayoutDto>();
        }
    }

    /// <summary>
    /// Clears isDefault on all user layouts, optionally excluding a specific layout ID.
    /// </summary>
    private async Task ClearUserDefaultsAsync(
        string userId,
        IReadOnlyList<WorkspaceLayoutDto> userLayouts,
        CancellationToken ct,
        Guid? excludeId = null)
    {
        var defaultLayouts = userLayouts
            .Where(l => l.IsDefault && l.Id != excludeId)
            .ToList();

        if (defaultLayouts.Count == 0)
            return;

        _logger.LogDebug(
            "Clearing isDefault on {Count} layouts for user {UserId}",
            defaultLayouts.Count, userId);

        var updates = defaultLayouts
            .Select(l => (l.Id, new Dictionary<string, object> { ["sprk_isdefault"] = false }))
            .ToList();

        try
        {
            await _entityService.BulkUpdateAsync(EntityName, updates, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to clear defaults for user {UserId}. Proceeding with create/update.",
                userId);
        }
    }

    /// <summary>
    /// Maps a Dataverse entity to a WorkspaceLayoutDto.
    /// </summary>
    private static WorkspaceLayoutDto MapToDto(Entity entity) => new()
    {
        Id = entity.Id,
        Name = entity.GetAttributeValue<string>("sprk_name") ?? string.Empty,
        LayoutTemplateId = entity.GetAttributeValue<string>("sprk_layouttemplateid") ?? string.Empty,
        SectionsJson = entity.GetAttributeValue<string>("sprk_sectionsjson") ?? string.Empty,
        IsDefault = entity.GetAttributeValue<bool?>("sprk_isdefault") ?? false,
        SortOrder = entity.GetAttributeValue<int?>("sprk_sortorder"),
        IsSystem = false
    };

    #endregion
}
