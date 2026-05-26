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

    // Wave 2b (task 109): include sprk_issystem so the service can:
    //  (a) tag DTOs with IsSystem=true when the Dataverse record is a seeded
    //      system layout (one of the 4 records POSTed by Wave 2a's seed script);
    //  (b) reject UPDATE / DELETE against any Dataverse record whose
    //      sprk_issystem column is true (defense-in-depth complement to the
    //      client-side disable affordance in WorkspacePaneMenu / ManageWorkspacesPane).
    private static readonly string[] SelectColumns =
    [
        "sprk_workspacelayoutid",
        "sprk_name",
        "sprk_layouttemplateid",
        "sprk_sectionsjson",
        "sprk_isdefault",
        "sprk_sortorder",
        "sprk_issystem",
        // R4 task 053 (B-4 / FR-07): surface Dataverse-maintained modifiedon
        // so the Manage Workspaces pane can render "Modified ..." per layout
        // and so the future PATCH/If-Match concurrency surface (B-5 / task 054)
        // can use it as a strong validator / ETag value.
        "modifiedon"
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
    /// Returns the union of (a) hard-coded system layouts from
    /// <see cref="SystemWorkspaceLayouts.All"/> ("Corporate Workspace"),
    /// (b) Dataverse layouts flagged <c>sprk_issystem=true</c> (seeded by
    /// <c>scripts/Deploy-SystemWorkspaceLayouts.ps1</c>), and (c) user-owned
    /// Dataverse layouts for the calling user. Wave 2b (task 109) — Option B
    /// architectural unity: all three sources flow through the same list
    /// endpoint and the same client pipeline so SpaarkeAi's Workspaces
    /// dropdown surfaces every workspace the user can open.
    /// </summary>
    /// <remarks>
    /// Order:
    ///   1. Hard-coded system layouts (in their static array order — single
    ///      entry today, "Corporate Workspace").
    ///   2. Dataverse system layouts (sprk_issystem=true), sorted by
    ///      <c>sprk_sortorder</c> ascending (Wave 2a seeded them as 0..3:
    ///      Daily Briefing, Smart To Do List, My Work, Documents).
    ///   3. User-owned layouts sorted by <c>sprk_sortorder</c> ascending.
    ///
    /// Every DTO in the returned list carries <see cref="WorkspaceLayoutDto.IsSystem"/>
    /// set correctly for its source (true for groups 1+2, false for group 3).
    /// The client uses this flag to disable Delete + route Edit through
    /// "save as" in <c>ManageWorkspacesPane.tsx</c>; the server enforces the
    /// same constraint in <see cref="UpdateLayoutAsync"/> + <see cref="DeleteLayoutAsync"/>.
    /// </remarks>
    /// <param name="userId">The Entra ID object ID of the authenticated user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Combined list of hard-coded + Dataverse-system + user layouts.</returns>
    public async Task<IReadOnlyList<WorkspaceLayoutDto>> GetLayoutsAsync(
        string userId,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Loading layouts for user {UserId}", userId);

        // Group 2 (Dataverse system) + Group 3 (user) — run in parallel to
        // halve the wall-clock of the round-trip cascade.
        var systemTask = QueryDataverseSystemLayoutsAsync(ct);
        var userTask = QueryUserLayoutsAsync(userId, ct);
        await Task.WhenAll(systemTask, userTask);
        var dataverseSystemLayouts = systemTask.Result;
        var userLayouts = userTask.Result;

        var result = new List<WorkspaceLayoutDto>(
            SystemWorkspaceLayouts.All.Count + dataverseSystemLayouts.Count + userLayouts.Count);

        // (1) Hard-coded system layouts (Corporate Workspace).
        result.AddRange(SystemWorkspaceLayouts.All);

        // (2) Dataverse system layouts, ordered by sortOrder ascending.
        result.AddRange(dataverseSystemLayouts.OrderBy(l => l.SortOrder ?? int.MaxValue));

        // (3) User layouts, ordered by sortOrder ascending.
        result.AddRange(userLayouts.OrderBy(l => l.SortOrder ?? int.MaxValue));

        _logger.LogDebug(
            "Returning {Total} layouts ({HardCoded} hard-coded system, {DvSystem} Dataverse system, {User} user) for user {UserId}",
            result.Count, SystemWorkspaceLayouts.All.Count, dataverseSystemLayouts.Count, userLayouts.Count, userId);

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
        // Check hard-coded system layouts first (no Dataverse query needed)
        var hardCodedSystem = SystemWorkspaceLayouts.GetById(id);
        if (hardCodedSystem is not null)
            return hardCodedSystem;

        _logger.LogDebug("Loading layout {LayoutId} for user {UserId}", id, userId);

        try
        {
            var entity = await _entityService.RetrieveAsync(EntityName, id, SelectColumns, ct);

            // Wave 2b (task 109): Dataverse system layouts (sprk_issystem=true)
            // are visible to ALL users regardless of ownership. User-owned
            // records remain isolated by ownerid as before.
            var isSystem = entity.GetAttributeValue<bool?>("sprk_issystem") ?? false;
            if (!isSystem)
            {
                var ownerId = entity.GetAttributeValue<EntityReference>("ownerid")?.Id;
                if (ownerId.HasValue && Guid.TryParse(userId, out var userGuid) && ownerId.Value != userGuid)
                {
                    _logger.LogWarning(
                        "User {UserId} attempted to access layout {LayoutId} owned by {OwnerId}",
                        userId, id, ownerId);
                    return null;
                }
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
    /// Returns the default workspace layout for the calling user using a
    /// four-step discovery cascade. Wave 2b (task 109) extension: the
    /// previous implementation only honored a per-user default and otherwise
    /// fell back to the hard-coded Corporate Workspace. After Wave 2a seeded
    /// system layouts in Dataverse, cold-load users with no per-user default
    /// should land on the globally-flagged Dataverse system default ("Daily
    /// Briefing" in dev, per the Wave 2a seed).
    /// </summary>
    /// <remarks>
    /// Cascade:
    ///   1. Per-user default — a layout owned by <paramref name="userId"/>
    ///      with <c>sprk_isdefault=true</c>. This honors any user-customized
    ///      default chosen via Manage Workspaces.
    ///   2. Dataverse system default — a layout with both
    ///      <c>sprk_issystem=true</c> AND <c>sprk_isdefault=true</c> (Wave 2a
    ///      seeded Daily Briefing in this slot). Cross-user; visible to
    ///      every authenticated user. <c>TopCount=1</c> with sortOrder
    ///      ascending for deterministic selection if multiple are ever flagged.
    ///   3. Hard-coded system layout — any entry in
    ///      <see cref="SystemWorkspaceLayouts.All"/> with
    ///      <see cref="WorkspaceLayoutDto.IsDefault"/> true (today
    ///      Corporate Workspace is seeded with IsDefault=false, so this step
    ///      typically yields nothing; preserved for forward compatibility if
    ///      a code constant is ever promoted to global default).
    ///   4. <c>null</c> — no default available; the client must not
    ///      auto-install a tab. Frontend behavior on null: render an empty
    ///      Workspace pane and let the user pick from the Workspaces dropdown
    ///      (see <c>WorkspacePane.tsx</c>'s default-install effect).
    ///
    /// Return type changed from <c>Task&lt;WorkspaceLayoutDto&gt;</c> to
    /// <c>Task&lt;WorkspaceLayoutDto?&gt;</c> to express step 4 — the endpoint
    /// handler converts null to a 200 with explicit null body so client code
    /// can distinguish "no default" from a fetch failure.
    /// </remarks>
    /// <param name="userId">The Entra ID object ID of the authenticated user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The default layout, or null if none is available.</returns>
    public async Task<WorkspaceLayoutDto?> GetDefaultLayoutAsync(
        string userId,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Loading default layout for user {UserId}", userId);

        // ──────────────────────────────────────────────────────────────────
        // Step 1 — Per-user default.
        //
        // We split the per-user query from the Dataverse-system default
        // query so step 2 can run even if the user has zero owned layouts
        // (the original query lumped both into one filter).
        // ──────────────────────────────────────────────────────────────────
        if (Guid.TryParse(userId, out var userGuid))
        {
            var userQuery = new QueryExpression(EntityName)
            {
                ColumnSet = new ColumnSet(SelectColumns),
                TopCount = 1
            };
            userQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            userQuery.Criteria.AddCondition("sprk_isdefault", ConditionOperator.Equal, true);
            userQuery.Criteria.AddCondition("ownerid", ConditionOperator.Equal, userGuid);
            // Per-user defaults never have sprk_issystem=true (the seed script
            // sets system records to be owned by the script-runner, but they
            // are surfaced via step 2's cross-user discovery; step 1 is for
            // user-customized defaults only).
            userQuery.Criteria.AddCondition("sprk_issystem", ConditionOperator.NotEqual, true);

            try
            {
                var userResults = await _entityService.RetrieveMultipleAsync(userQuery, ct);
                if (userResults.Entities.Count > 0)
                {
                    var dto = MapToDto(userResults.Entities[0]);
                    _logger.LogDebug(
                        "Found per-user default layout {LayoutId} for user {UserId}",
                        dto.Id, userId);
                    return dto;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to query per-user default for user {UserId}", userId);
                // Continue to step 2 — don't let a query failure prevent
                // discovery of the cross-user system default.
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Step 2 — Dataverse system default (cross-user).
        //
        // No ownerid filter — system layouts are visible to all users.
        // ──────────────────────────────────────────────────────────────────
        var systemQuery = new QueryExpression(EntityName)
        {
            ColumnSet = new ColumnSet(SelectColumns),
            TopCount = 1
        };
        systemQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        systemQuery.Criteria.AddCondition("sprk_issystem", ConditionOperator.Equal, true);
        systemQuery.Criteria.AddCondition("sprk_isdefault", ConditionOperator.Equal, true);
        systemQuery.AddOrder("sprk_sortorder", OrderType.Ascending);

        try
        {
            var systemResults = await _entityService.RetrieveMultipleAsync(systemQuery, ct);
            if (systemResults.Entities.Count > 0)
            {
                var dto = MapToDto(systemResults.Entities[0]);
                _logger.LogDebug(
                    "Found Dataverse system default layout {LayoutId} for user {UserId}",
                    dto.Id, userId);
                return dto;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query Dataverse system default for user {UserId}", userId);
            // Continue to step 3 — graceful degradation.
        }

        // ──────────────────────────────────────────────────────────────────
        // Step 3 — Hard-coded system layout flagged as global default.
        //
        // SystemWorkspaceLayouts.All today contains Corporate Workspace with
        // IsDefault=false, so this typically returns nothing. Preserved as a
        // forward-compat path so a future code constant can be promoted to
        // global default without changing the cascade structure.
        // ──────────────────────────────────────────────────────────────────
        var hardCodedDefault = SystemWorkspaceLayouts.All.FirstOrDefault(l => l.IsDefault);
        if (hardCodedDefault is not null)
        {
            _logger.LogDebug(
                "Returning hard-coded system default layout {LayoutId} for user {UserId}",
                hardCodedDefault.Id, userId);
            return hardCodedDefault;
        }

        // ──────────────────────────────────────────────────────────────────
        // Step 4 — No default available.
        //
        // The endpoint returns 200 with explicit null body. Frontend renders
        // an empty Workspace pane; user picks from Workspaces dropdown.
        // ──────────────────────────────────────────────────────────────────
        _logger.LogDebug("No default layout discovered for user {UserId} — returning null", userId);
        return null;
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
                IsSystem = false,
                // R4 task 053 (B-4 / FR-07): Dataverse stamps modifiedon at
                // create time; reflect that here without an extra round-trip.
                // The next GET will return the canonical Dataverse-stored value
                // (which may differ by ms but is semantically equivalent for
                // FR-07's "Modified ..." rendering). B-5 (task 054) may
                // round-trip to get the exact rowversion for ETag use.
                ModifiedOn = DateTimeOffset.UtcNow
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
        // Reject updates to hard-coded system layouts (Corporate Workspace)
        if (SystemWorkspaceLayouts.IsSystemLayout(id))
        {
            _logger.LogWarning(
                "User {UserId} attempted to update hard-coded system layout {LayoutId}",
                userId, id);
            return (null, "System workspaces cannot be modified.");
        }

        _logger.LogInformation(
            "Updating layout {LayoutId} for user {UserId} (isDefault={IsDefault})",
            id, userId, request.IsDefault);

        // Verify the layout exists and belongs to this user (or is a system record).
        var existing = await GetLayoutByIdAsync(id, userId, ct);
        if (existing is null)
        {
            return (null, "Workspace layout not found.");
        }

        // Wave 2b (task 109): reject updates to Dataverse system layouts too —
        // defense-in-depth complement to the client-side disable affordance.
        // The client's ManageWorkspacesPane already routes Edit through
        // "save as" for isSystem records; this guards against a crafted PUT
        // that bypasses the UI.
        if (existing.IsSystem)
        {
            _logger.LogWarning(
                "User {UserId} attempted to update Dataverse system layout {LayoutId}",
                userId, id);
            return (null, "System workspaces cannot be modified.");
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
                IsSystem = false,
                // R4 task 053 (B-4 / FR-07): Dataverse stamps modifiedon at
                // update time; reflect that here without an extra round-trip.
                // See CreateLayoutAsync remarks; B-5 (task 054) will likely
                // re-read post-write to obtain the exact rowversion for ETag use.
                ModifiedOn = DateTimeOffset.UtcNow
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
        // Reject deletion of hard-coded system layouts (Corporate Workspace)
        if (SystemWorkspaceLayouts.IsSystemLayout(id))
        {
            _logger.LogWarning(
                "User {UserId} attempted to delete hard-coded system layout {LayoutId}",
                userId, id);
            return "System workspaces cannot be deleted.";
        }

        _logger.LogInformation("Deleting layout {LayoutId} for user {UserId}", id, userId);

        // Verify the layout exists and belongs to this user (or is a system record).
        var existing = await GetLayoutByIdAsync(id, userId, ct);
        if (existing is null)
        {
            return "Workspace layout not found.";
        }

        // Wave 2b (task 109): reject deletion of Dataverse system layouts too —
        // defense-in-depth complement to the client-side disable affordance.
        // The client's ManageWorkspacesPane disables the Delete button for
        // isSystem records; this guards against a crafted DELETE that
        // bypasses the UI.
        if (existing.IsSystem)
        {
            _logger.LogWarning(
                "User {UserId} attempted to delete Dataverse system layout {LayoutId}",
                userId, id);
            return "System workspaces cannot be deleted.";
        }

        try
        {
            // Hard delete — the user confirmed "this cannot be undone"
            await _entityService.DeleteAsync(EntityName, id, ct);

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
    /// Queries all active USER-OWNED Dataverse layouts (not system layouts).
    /// Wave 2b (task 109): explicitly excludes <c>sprk_issystem=true</c> records
    /// so the user-layout slice of the merged list endpoint doesn't include
    /// system records (which are surfaced via <see cref="QueryDataverseSystemLayoutsAsync"/>
    /// instead). The user-isolation guarantee remains via the ownerid filter.
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

        // Wave 2b: exclude system records — they're returned by the
        // QueryDataverseSystemLayoutsAsync path instead so we don't double-
        // count them in the merged list.
        query.Criteria.AddCondition("sprk_issystem", ConditionOperator.NotEqual, true);

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
    /// Queries Dataverse-stored system workspace layouts (<c>sprk_issystem=true</c>).
    /// Wave 2b (task 109): these records are seeded by
    /// <c>scripts/Deploy-SystemWorkspaceLayouts.ps1</c> (the 4 Wave 2a layouts —
    /// Daily Briefing, Smart To Do List, My Work, Documents) and are visible to
    /// ALL authenticated users regardless of ownership. The ownerid is set to
    /// the seed-script runner but does not gate visibility — system records
    /// are global.
    /// </summary>
    private async Task<IReadOnlyList<WorkspaceLayoutDto>> QueryDataverseSystemLayoutsAsync(
        CancellationToken ct)
    {
        var query = new QueryExpression(EntityName)
        {
            ColumnSet = new ColumnSet(SelectColumns)
        };

        // Active records only
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

        // System layouts only (sprk_issystem=true)
        query.Criteria.AddCondition("sprk_issystem", ConditionOperator.Equal, true);

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
            _logger.LogError(ex, "Failed to query Dataverse system layouts");
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
    /// Maps a Dataverse entity to a WorkspaceLayoutDto. Wave 2b (task 109):
    /// <see cref="WorkspaceLayoutDto.IsSystem"/> now reflects the Dataverse
    /// <c>sprk_issystem</c> column instead of being hard-coded false. This
    /// lets the client distinguish Dataverse system records (seeded by Wave
    /// 2a's deploy script) from user-owned records so the UI can disable
    /// Edit/Delete and the server can reject mutating writes against them.
    ///
    /// R4 task 053 (B-4 / FR-07): also surfaces <c>modifiedon</c> as
    /// <see cref="WorkspaceLayoutDto.ModifiedOn"/>. Dataverse stores
    /// <c>modifiedon</c> as a UTC <c>DateTime</c> with <c>Kind=Utc</c>; we
    /// normalize it to a <see cref="DateTimeOffset"/> with zero offset. If the
    /// attribute is missing or unset (defensive — should never happen on a
    /// persisted record because Dataverse maintains the column automatically),
    /// we emit <see cref="DateTimeOffset.MinValue"/> so the JSON serialization
    /// is still well-formed ISO-8601 and downstream clients can treat it as
    /// "unknown" without breaking. A non-zero ModifiedOn is the expected case
    /// in production.
    /// </summary>
    private static WorkspaceLayoutDto MapToDto(Entity entity) => new()
    {
        Id = entity.Id,
        Name = entity.GetAttributeValue<string>("sprk_name") ?? string.Empty,
        LayoutTemplateId = entity.GetAttributeValue<string>("sprk_layouttemplateid") ?? string.Empty,
        SectionsJson = entity.GetAttributeValue<string>("sprk_sectionsjson") ?? string.Empty,
        IsDefault = entity.GetAttributeValue<bool?>("sprk_isdefault") ?? false,
        SortOrder = entity.GetAttributeValue<int?>("sprk_sortorder"),
        IsSystem = entity.GetAttributeValue<bool?>("sprk_issystem") ?? false,
        ModifiedOn = ToOffset(entity.GetAttributeValue<DateTime?>("modifiedon"))
    };

    /// <summary>
    /// Normalizes a Dataverse <c>DateTime</c> to a UTC-anchored
    /// <see cref="DateTimeOffset"/>. Dataverse columns of type "Date and Time"
    /// come back with <c>Kind=Utc</c> already; this helper is defensive against
    /// the <c>Kind=Unspecified</c> case (which can happen via test fakes) and
    /// against null. Null → <see cref="DateTimeOffset.MinValue"/> (see MapToDto
    /// remarks above for why null is "unknown" rather than "now").
    /// </summary>
    private static DateTimeOffset ToOffset(DateTime? value)
    {
        if (!value.HasValue)
            return DateTimeOffset.MinValue;
        var dt = value.Value;
        return dt.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(dt, TimeSpan.Zero),
            DateTimeKind.Local => new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero),
            _ => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero),
        };
    }

    #endregion
}
