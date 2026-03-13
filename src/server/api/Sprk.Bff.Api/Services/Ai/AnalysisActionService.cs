using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Focused service for AnalysisAction CRUD operations against Dataverse.
/// Extracted from ScopeResolverService as part of god-class decomposition (PPI-054).
/// </summary>
public class AnalysisActionService : DataverseHttpServiceBase
{
    public AnalysisActionService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AnalysisActionService> logger)
        : base(httpClient, configuration, logger)
    {
    }

    /// <summary>
    /// Get action definition by ID.
    /// </summary>
    public async Task<AnalysisAction?> GetActionAsync(
        Guid actionId,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug("Loading action {ActionId} from Dataverse", actionId);

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysisactions({actionId})?$expand=sprk_ActionTypeId($select=sprk_name)";
        var response = await Http.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogWarning("Action {ActionId} not found in Dataverse", actionId);
            return null;
        }

        await EnsureSuccessWithDiagnosticsAsync(response, $"GetActionAsync({actionId})", cancellationToken);

        var entity = await response.Content.ReadFromJsonAsync<ActionEntity>(cancellationToken);
        if (entity == null)
        {
            Logger.LogWarning("Failed to deserialize action {ActionId} from Dataverse response", actionId);
            return null;
        }

        var sortOrder = ExtractSortOrderFromTypeName(entity.ActionTypeId?.Name);

        var actionType = entity.ActionTypeValue.HasValue
            ? (Nodes.ActionType)entity.ActionTypeValue.Value
            : Nodes.ActionType.AiAnalysis;

        var action = new AnalysisAction
        {
            Id = entity.Id,
            Name = entity.Name ?? "Unnamed Action",
            Description = entity.Description,
            SystemPrompt = entity.SystemPrompt ?? "You are an AI assistant that analyzes documents.",
            SortOrder = sortOrder,
            ActionType = actionType,
            OwnerType = ScopeOwnerType.System,
            IsImmutable = false
        };

        Logger.LogInformation("Loaded action from Dataverse: {ActionName}", action.Name);

        return action;
    }

    /// <summary>
    /// List all available actions with pagination.
    /// </summary>
    public async Task<ScopeListResult<AnalysisAction>> ListActionsAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("[LIST ACTIONS] Querying Dataverse: Page={Page}, PageSize={PageSize}, NameFilter={NameFilter}",
            options.Page, options.PageSize, options.NameFilter);

        await EnsureAuthenticatedAsync(cancellationToken);

        var sortMappings = new Dictionary<string, string>
        {
            ["name"] = "sprk_name",
            ["sortorder"] = "sprk_name"
        };

        var query = BuildODataQuery(
            options,
            selectFields: "sprk_analysisactionid,sprk_name,sprk_description,sprk_systemprompt",
            expandClause: "sprk_ActionTypeId($select=sprk_name)",
            nameFieldPath: "sprk_name",
            categoryFieldPath: null,
            sortFieldMappings: sortMappings);

        var url = $"sprk_analysisactions?{query}";
        Logger.LogDebug("[LIST ACTIONS] Query URL: {Url}", url);

        var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<ActionEntity>>(cancellationToken);
        if (result == null)
        {
            Logger.LogWarning("[LIST ACTIONS] Failed to deserialize response");
            return new ScopeListResult<AnalysisAction>
            {
                Items = [],
                TotalCount = 0,
                Page = options.Page,
                PageSize = options.PageSize
            };
        }

        var actions = result.Value.Select(entity =>
        {
            var sortOrder = ExtractSortOrderFromTypeName(entity.ActionTypeId?.Name);
            return new AnalysisAction
            {
                Id = entity.Id,
                Name = entity.Name ?? "Unnamed Action",
                Description = entity.Description,
                SystemPrompt = entity.SystemPrompt ?? "You are an AI assistant that analyzes documents.",
                SortOrder = sortOrder,
                OwnerType = ScopeOwnerType.System,
                IsImmutable = false
            };
        }).ToArray();

        Logger.LogInformation("[LIST ACTIONS] Retrieved {Count} actions from Dataverse (Total: {TotalCount})",
            actions.Length, result.ODataCount ?? actions.Length);

        return new ScopeListResult<AnalysisAction>
        {
            Items = actions,
            TotalCount = result.ODataCount ?? actions.Length,
            Page = options.Page,
            PageSize = options.PageSize
        };
    }

    /// <summary>
    /// Create a new analysis action.
    /// </summary>
    public async Task<AnalysisAction> CreateActionAsync(CreateActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request.Name));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SystemPrompt, nameof(request.SystemPrompt));

        var name = EnsureCustomerPrefix(request.Name);

        Logger.LogInformation("[CREATE ACTION] Creating action '{ActionName}' in Dataverse", name);

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = name,
            ["sprk_description"] = request.Description,
            ["sprk_systemprompt"] = request.SystemPrompt
        };

        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "sprk_analysisactions")
        {
            Content = httpContent
        };
        postRequest.Headers.Add("Prefer", "return=representation");

        var response = await Http.SendAsync(postRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<ActionEntity>(cancellationToken);
        if (entity == null)
        {
            throw new InvalidOperationException("Failed to deserialize created action from Dataverse response");
        }

        var sortOrder = entity.ActionTypeId?.Name != null
            ? ExtractSortOrderFromTypeName(entity.ActionTypeId?.Name)
            : request.SortOrder;

        var action = new AnalysisAction
        {
            Id = entity.Id,
            Name = entity.Name ?? name,
            Description = entity.Description ?? request.Description,
            SystemPrompt = entity.SystemPrompt ?? request.SystemPrompt,
            SortOrder = sortOrder,
            ActionType = request.ActionType,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false
        };

        Logger.LogInformation("[CREATE ACTION] Created action '{ActionName}' with ID {ActionId}", action.Name, action.Id);

        return action;
    }

    /// <summary>
    /// Update an existing analysis action.
    /// </summary>
    public async Task<AnalysisAction> UpdateActionAsync(Guid id, UpdateActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Logger.LogInformation("[UPDATE ACTION] Updating action {ActionId} in Dataverse", id);

        var existing = await GetActionAsync(id, cancellationToken);
        if (existing == null)
        {
            Logger.LogWarning("[UPDATE ACTION] Action {ActionId} not found for update", id);
            throw new KeyNotFoundException($"Action with ID '{id}' not found.");
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "update");

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new Dictionary<string, object?>();

        if (request.Name != null)
            payload["sprk_name"] = EnsureCustomerPrefix(request.Name);
        if (request.Description != null)
            payload["sprk_description"] = request.Description;
        if (request.SystemPrompt != null)
            payload["sprk_systemprompt"] = request.SystemPrompt;

        var url = $"sprk_analysisactions({id})";
        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = httpContent
        };

        var response = await Http.SendAsync(patchRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var updated = await GetActionAsync(id, cancellationToken);
        if (updated == null)
        {
            throw new InvalidOperationException($"Failed to retrieve updated action {id} from Dataverse");
        }

        Logger.LogInformation("[UPDATE ACTION] Updated action '{ActionName}' (ID: {ActionId})", updated.Name, id);

        return updated;
    }

    /// <summary>
    /// Delete an analysis action.
    /// </summary>
    public async Task<bool> DeleteActionAsync(Guid id, CancellationToken cancellationToken)
    {
        Logger.LogInformation("[DELETE ACTION] Deleting action {ActionId} from Dataverse", id);

        var existing = await GetActionAsync(id, cancellationToken);
        if (existing == null)
        {
            Logger.LogWarning("[DELETE ACTION] Action {ActionId} not found for deletion", id);
            return false;
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "delete");

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysisactions({id})";
        var response = await Http.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogWarning("[DELETE ACTION] Action {ActionId} not found in Dataverse", id);
            return false;
        }

        response.EnsureSuccessStatusCode();

        Logger.LogInformation("[DELETE ACTION] Deleted action '{ActionName}' (ID: {ActionId})", existing.Name, id);
        return true;
    }

    /// <summary>
    /// Create a copy of an existing action with a new name.
    /// </summary>
    public async Task<AnalysisAction> SaveAsActionAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));

        Logger.LogInformation("Saving action {SourceId} as '{NewName}'", sourceId, newName);

        var source = await GetActionAsync(sourceId, cancellationToken);
        if (source == null)
        {
            Logger.LogWarning("Source action {SourceId} not found for Save As", sourceId);
            throw new KeyNotFoundException($"Source action with ID '{sourceId}' not found.");
        }

        var createRequest = new CreateActionRequest
        {
            Name = newName,
            Description = source.Description,
            SystemPrompt = source.SystemPrompt,
            SortOrder = source.SortOrder,
            ActionType = source.ActionType
        };

        var copy = await CreateActionAsync(createRequest, cancellationToken);

        Logger.LogInformation("[SAVE AS ACTION] Created action copy '{Name}' (ID: {CopyId}) based on {SourceId}",
            copy.Name, copy.Id, sourceId);

        return copy with { BasedOnId = sourceId };
    }

    /// <summary>
    /// Create a child action that extends the parent.
    /// </summary>
    public async Task<AnalysisAction> ExtendActionAsync(Guid parentId, string childName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childName, nameof(childName));

        Logger.LogInformation("[EXTEND ACTION] Extending action {ParentId} with child '{ChildName}'", parentId, childName);

        var parent = await GetActionAsync(parentId, cancellationToken);
        if (parent == null)
        {
            Logger.LogWarning("[EXTEND ACTION] Parent action {ParentId} not found for Extend", parentId);
            throw new KeyNotFoundException($"Parent action with ID '{parentId}' not found.");
        }

        var createRequest = new CreateActionRequest
        {
            Name = childName,
            Description = parent.Description,
            SystemPrompt = parent.SystemPrompt,
            SortOrder = parent.SortOrder,
            ActionType = parent.ActionType
        };

        var child = await CreateActionAsync(createRequest, cancellationToken);

        Logger.LogInformation("[EXTEND ACTION] Created child action '{Name}' (ID: {ChildId}) extending {ParentId}",
            child.Name, child.Id, parentId);

        return child with { ParentScopeId = parentId };
    }

    #region Private Helpers

    internal static int ExtractSortOrderFromTypeName(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return 0;

        var match = System.Text.RegularExpressions.Regex.Match(typeName, @"^(\d+)\s*-");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var sortOrder))
        {
            return sortOrder;
        }

        return 0;
    }

    #endregion

    #region Private DTOs

    internal class ActionEntity
    {
        [JsonPropertyName("sprk_analysisactionid")]
        public Guid Id { get; set; }

        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }

        [JsonPropertyName("sprk_description")]
        public string? Description { get; set; }

        [JsonPropertyName("sprk_systemprompt")]
        public string? SystemPrompt { get; set; }

        [JsonPropertyName("sprk_actiontype")]
        public int? ActionTypeValue { get; set; }

        [JsonPropertyName("sprk_ActionTypeId")]
        public ActionTypeReference? ActionTypeId { get; set; }
    }

    internal class ActionTypeReference
    {
        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }
    }

    #endregion
}
