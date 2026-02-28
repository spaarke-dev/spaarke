using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for managing playbook nodes in Dataverse.
/// Uses Web API for CRUD operations and N:N relationship management.
/// </summary>
public class NodeService : INodeService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly TokenCredential _credential;
    private readonly ILogger<NodeService> _logger;
    private AccessToken? _currentToken;

    private const string EntitySetName = "sprk_playbooknodes";
    private const string EntityLogicalName = "sprk_playbooknode";

    // N:N relationship names
    private const string SkillRelationship = "sprk_playbooknode_skill";
    private const string KnowledgeRelationship = "sprk_playbooknode_knowledge";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public NodeService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<NodeService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");
        var tenantId = configuration["TENANT_ID"]
            ?? throw new InvalidOperationException("TENANT_ID configuration is required");
        var clientId = configuration["API_APP_ID"]
            ?? throw new InvalidOperationException("API_APP_ID configuration is required");
        var clientSecret = configuration["API_CLIENT_SECRET"]
            ?? throw new InvalidOperationException("API_CLIENT_SECRET configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/";
        _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);

        _httpClient.BaseAddress = new Uri(_apiUrl);
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogInformation("Initialized NodeService - DataverseUrl: {DataverseUrl}", dataverseUrl);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext([scope]),
                cancellationToken);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

            _logger.LogDebug("Refreshed Dataverse access token for NodeService");
        }
    }

    /// <inheritdoc />
    public async Task<PlaybookNodeDto[]> GetNodesAsync(
        Guid playbookId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var select = GetSelectFields();
        var filter = $"_sprk_playbookid_value eq {playbookId}";
        var orderBy = "sprk_executionorder asc";
        var url = $"{EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(JsonOptions, cancellationToken);
        if (result?.Value == null) return [];

        var nodes = new List<PlaybookNodeDto>();
        foreach (var element in result.Value)
        {
            var entity = JsonSerializer.Deserialize<NodeEntity>(element.GetRawText(), JsonOptions);
            if (entity != null)
            {
                var dto = await MapToDto(entity, cancellationToken);
                nodes.Add(dto);
            }
        }

        return nodes.ToArray();
    }

    /// <inheritdoc />
    public async Task<PlaybookNodeDto?> GetNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var select = GetSelectFields();
        var url = $"{EntitySetName}({nodeId})?$select={select}";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<NodeEntity>(JsonOptions, cancellationToken);
        if (entity == null) return null;

        return await MapToDto(entity, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PlaybookNodeDto> CreateNodeAsync(
        Guid playbookId,
        CreateNodeRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Creating node '{Name}' in playbook {PlaybookId}", request.Name, playbookId);

        // Determine execution order if not specified
        var executionOrder = request.ExecutionOrder ?? await GetNextExecutionOrderAsync(playbookId, cancellationToken);

        // Build entity payload
        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = request.Name,
            ["sprk_playbookid@odata.bind"] = $"/sprk_analysisplaybooks({playbookId})",
            ["sprk_actionid@odata.bind"] = $"/sprk_analysisactions({request.ActionId})",
            ["sprk_executionorder"] = executionOrder,
            ["sprk_outputvariable"] = request.OutputVariable,
            ["sprk_isactive"] = request.IsActive
        };

        // Optional lookups
        if (request.ToolId.HasValue)
        {
            payload["sprk_toolid@odata.bind"] = $"/sprk_analysistools({request.ToolId.Value})";
        }
        if (request.ModelDeploymentId.HasValue)
        {
            payload["sprk_modeldeploymentid@odata.bind"] = $"/sprk_aimodeldeployments({request.ModelDeploymentId.Value})";
        }

        // Optional fields
        if (request.DependsOn?.Length > 0)
        {
            payload["sprk_dependsonjson"] = JsonSerializer.Serialize(request.DependsOn);
        }
        if (!string.IsNullOrEmpty(request.ConditionJson))
        {
            payload["sprk_conditionjson"] = request.ConditionJson;
        }
        if (!string.IsNullOrEmpty(request.ConfigJson))
        {
            payload["sprk_configjson"] = request.ConfigJson;
        }
        if (request.TimeoutSeconds.HasValue)
        {
            payload["sprk_timeoutseconds"] = request.TimeoutSeconds.Value;
        }
        if (request.RetryCount.HasValue)
        {
            payload["sprk_retrycount"] = request.RetryCount.Value;
        }
        if (request.PositionX.HasValue)
        {
            payload["sprk_position_x"] = request.PositionX.Value;
        }
        if (request.PositionY.HasValue)
        {
            payload["sprk_position_y"] = request.PositionY.Value;
        }

        var response = await _httpClient.PostAsJsonAsync(EntitySetName, payload, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Extract ID from OData-EntityId header
        var entityIdHeader = response.Headers.GetValues("OData-EntityId").FirstOrDefault()
            ?? throw new InvalidOperationException("Failed to get entity ID from response");
        var nodeId = Guid.Parse(entityIdHeader.Split('(', ')')[1]);

        _logger.LogInformation("Created node {NodeId}: {Name}", nodeId, request.Name);

        // Associate N:N relationships
        await AssociateRelationshipsAsync(nodeId, request.SkillIds, request.KnowledgeIds, cancellationToken);

        // Return the created node
        return await GetNodeAsync(nodeId, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve created node");
    }

    /// <inheritdoc />
    public async Task<PlaybookNodeDto> UpdateNodeAsync(
        Guid nodeId,
        UpdateNodeRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Updating node {NodeId}", nodeId);

        var payload = new Dictionary<string, object?>();

        // Only include fields that are specified
        if (!string.IsNullOrEmpty(request.Name))
        {
            payload["sprk_name"] = request.Name;
        }
        if (request.ActionId.HasValue)
        {
            payload["sprk_actionid@odata.bind"] = $"/sprk_analysisactions({request.ActionId.Value})";
        }
        if (request.ToolId.HasValue)
        {
            payload["sprk_toolid@odata.bind"] = $"/sprk_analysistools({request.ToolId.Value})";
        }
        if (request.ModelDeploymentId.HasValue)
        {
            payload["sprk_modeldeploymentid@odata.bind"] = $"/sprk_aimodeldeployments({request.ModelDeploymentId.Value})";
        }
        if (request.DependsOn != null)
        {
            payload["sprk_dependsonjson"] = request.DependsOn.Length > 0
                ? JsonSerializer.Serialize(request.DependsOn)
                : null;
        }
        if (request.OutputVariable != null)
        {
            payload["sprk_outputvariable"] = request.OutputVariable;
        }
        if (request.ConditionJson != null)
        {
            payload["sprk_conditionjson"] = request.ConditionJson;
        }
        if (request.ConfigJson != null)
        {
            payload["sprk_configjson"] = request.ConfigJson;
        }
        if (request.TimeoutSeconds.HasValue)
        {
            payload["sprk_timeoutseconds"] = request.TimeoutSeconds.Value;
        }
        if (request.RetryCount.HasValue)
        {
            payload["sprk_retrycount"] = request.RetryCount.Value;
        }
        if (request.PositionX.HasValue)
        {
            payload["sprk_position_x"] = request.PositionX.Value;
        }
        if (request.PositionY.HasValue)
        {
            payload["sprk_position_y"] = request.PositionY.Value;
        }
        if (request.IsActive.HasValue)
        {
            payload["sprk_isactive"] = request.IsActive.Value;
        }

        if (payload.Count > 0)
        {
            var url = $"{EntitySetName}({nodeId})";
            var response = await _httpClient.PatchAsJsonAsync(url, payload, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        // Update N:N relationships if specified
        if (request.SkillIds != null || request.KnowledgeIds != null)
        {
            await ClearRelationshipsAsync(nodeId, cancellationToken);
            await AssociateRelationshipsAsync(nodeId, request.SkillIds, request.KnowledgeIds, cancellationToken);
        }

        _logger.LogInformation("Updated node {NodeId}", nodeId);

        return await GetNodeAsync(nodeId, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve updated node");
    }

    /// <inheritdoc />
    public async Task<bool> DeleteNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Deleting node {NodeId}", nodeId);

        var url = $"{EntitySetName}({nodeId})";
        var response = await _httpClient.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Deleted node {NodeId}", nodeId);
        return true;
    }

    /// <inheritdoc />
    public async Task ReorderNodesAsync(
        Guid playbookId,
        Guid[] nodeIds,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Reordering {Count} nodes in playbook {PlaybookId}", nodeIds.Length, playbookId);

        for (var i = 0; i < nodeIds.Length; i++)
        {
            var nodeId = nodeIds[i];
            var executionOrder = i + 1; // 1-based ordering

            var url = $"{EntitySetName}({nodeId})";
            var payload = new Dictionary<string, object?>
            {
                ["sprk_executionorder"] = executionOrder
            };

            var response = await _httpClient.PatchAsJsonAsync(url, payload, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Reordered nodes in playbook {PlaybookId}", playbookId);
    }

    /// <inheritdoc />
    public async Task<PlaybookNodeDto> UpdateNodeScopesAsync(
        Guid nodeId,
        NodeScopesRequest scopes,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Updating scopes for node {NodeId}", nodeId);

        // Clear existing and re-associate
        await ClearRelationshipsAsync(nodeId, cancellationToken);
        await AssociateRelationshipsAsync(nodeId, scopes.SkillIds, scopes.KnowledgeIds, cancellationToken);

        return await GetNodeAsync(nodeId, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve updated node");
    }

    /// <inheritdoc />
    public Task<NodeValidationResult> ValidateAsync(
        CreateNodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add("Name is required");
        }
        else if (request.Name.Length > 200)
        {
            errors.Add("Name cannot exceed 200 characters");
        }

        if (request.ActionId == Guid.Empty)
        {
            errors.Add("ActionId is required");
        }

        if (string.IsNullOrWhiteSpace(request.OutputVariable))
        {
            errors.Add("OutputVariable is required");
        }
        else if (request.OutputVariable.Length > 100)
        {
            errors.Add("OutputVariable cannot exceed 100 characters");
        }

        if (request.TimeoutSeconds.HasValue && request.TimeoutSeconds.Value <= 0)
        {
            errors.Add("TimeoutSeconds must be greater than 0");
        }

        if (request.RetryCount.HasValue && request.RetryCount.Value < 0)
        {
            errors.Add("RetryCount cannot be negative");
        }

        var result = errors.Count == 0
            ? NodeValidationResult.Success()
            : NodeValidationResult.Failure(errors.ToArray());

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public async Task SyncCanvasToNodesAsync(
        Guid playbookId,
        CanvasLayoutDto canvasLayout,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var nodes = canvasLayout.Nodes;
        var edges = canvasLayout.Edges;

        _logger.LogInformation(
            "Syncing canvas to nodes for playbook {PlaybookId}: {NodeCount} canvas nodes, {EdgeCount} edges",
            playbookId, nodes.Length, edges.Length);

        // Step 1: Load existing Dataverse node records for this playbook
        var existingEntities = await GetNodesRawAsync(playbookId, cancellationToken);
        var existingByCanvasId = BuildCanvasIdMap(existingEntities);

        _logger.LogDebug("Found {ExistingCount} existing nodes, {MappedCount} with canvas IDs",
            existingEntities.Length, existingByCanvasId.Count);

        // Step 2: Compute execution order via topological sort of canvas edges
        var executionOrders = ComputeExecutionOrders(nodes, edges);

        // Step 3: First pass — create or update each canvas node (without dependsOn yet)
        var canvasIdToNodeId = new Dictionary<string, Guid>();
        var processedCanvasIds = new HashSet<string>();

        // Seed mapping with existing canvas→Dataverse ID links
        foreach (var (canvasId, (nodeId, _)) in existingByCanvasId)
            canvasIdToNodeId[canvasId] = nodeId;

        foreach (var canvasNode in nodes)
        {
            processedCanvasIds.Add(canvasNode.Id);
            var order = executionOrders.GetValueOrDefault(canvasNode.Id, 0);

            try
            {
                if (existingByCanvasId.TryGetValue(canvasNode.Id, out var existing))
                {
                    await UpdateNodeFieldsFromCanvasAsync(existing.NodeId, canvasNode, order, cancellationToken);
                }
                else
                {
                    var newNodeId = await CreateNodeFieldsFromCanvasAsync(
                        playbookId, canvasNode, order, cancellationToken);
                    canvasIdToNodeId[canvasNode.Id] = newNodeId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync canvas node {CanvasNodeId} for playbook {PlaybookId}",
                    canvasNode.Id, playbookId);
            }
        }

        // Step 4: Second pass — set dependsOnJson now that all canvas IDs are mapped to GUIDs
        var incomingEdges = BuildIncomingEdgeMap(edges);

        foreach (var canvasNode in nodes)
        {
            if (!canvasIdToNodeId.TryGetValue(canvasNode.Id, out var nodeId)) continue;

            var dependsOnGuids = (incomingEdges.GetValueOrDefault(canvasNode.Id) ?? [])
                .Where(sourceId => canvasIdToNodeId.ContainsKey(sourceId))
                .Select(sourceId => canvasIdToNodeId[sourceId])
                .ToArray();

            try
            {
                await UpdateDependsOnAsync(nodeId, dependsOnGuids, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update dependsOn for node {NodeId}", nodeId);
            }
        }

        // Step 5: Sync N:N relationships for each canvas node
        foreach (var canvasNode in nodes)
        {
            if (!canvasIdToNodeId.TryGetValue(canvasNode.Id, out var nodeId)) continue;

            var skillIds = ExtractGuidArray(canvasNode.Data, "skillIds");
            var knowledgeIds = ExtractGuidArray(canvasNode.Data, "knowledgeIds");

            try
            {
                await ClearRelationshipsAsync(nodeId, cancellationToken);
                await AssociateRelationshipsAsync(nodeId,
                    skillIds.Length > 0 ? skillIds : null,
                    knowledgeIds.Length > 0 ? knowledgeIds : null,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync N:N relationships for node {NodeId}", nodeId);
            }
        }

        // Step 6: Delete orphaned nodes (exist in Dataverse but not in canvas)
        var deletedCount = 0;
        foreach (var entity in existingEntities)
        {
            var canvasId = ExtractCanvasNodeId(entity.ConfigJson);

            // Keep the node if its canvas ID is in the current canvas
            if (canvasId != null && processedCanvasIds.Contains(canvasId))
                continue;

            try
            {
                var url = $"{EntitySetName}({entity.Id})";
                var response = await _httpClient.DeleteAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    deletedCount++;
                    _logger.LogInformation("Deleted orphaned node {NodeId} (canvas ID: {CanvasId})",
                        entity.Id, canvasId ?? "(none)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete orphaned node {NodeId}", entity.Id);
            }
        }

        _logger.LogInformation(
            "Canvas sync complete for playbook {PlaybookId}: {Created} created, {Updated} updated, {Deleted} deleted",
            playbookId,
            nodes.Length - existingByCanvasId.Count(e => processedCanvasIds.Contains(e.Key)),
            existingByCanvasId.Count(e => processedCanvasIds.Contains(e.Key)),
            deletedCount);
    }

    #region Private Helper Methods

    private static string GetSelectFields()
    {
        return "sprk_playbooknodeid,sprk_name,_sprk_playbookid_value,_sprk_actionid_value,_sprk_toolid_value," +
               "_sprk_modeldeploymentid_value,sprk_executionorder,sprk_dependsonjson,sprk_outputvariable," +
               "sprk_conditionjson,sprk_configjson,sprk_timeoutseconds,sprk_retrycount," +
               "sprk_position_x,sprk_position_y,sprk_isactive,createdon,modifiedon";
    }

    private async Task<int> GetNextExecutionOrderAsync(Guid playbookId, CancellationToken cancellationToken)
    {
        var filter = $"_sprk_playbookid_value eq {playbookId}";
        var url = $"{EntitySetName}?$select=sprk_executionorder&$filter={Uri.EscapeDataString(filter)}&$orderby=sprk_executionorder desc&$top=1";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(JsonOptions, cancellationToken);
        if (result?.Value == null || result.Value.Length == 0)
        {
            return 1;
        }

        var maxOrder = result.Value[0].TryGetProperty("sprk_executionorder", out var orderProp) ? orderProp.GetInt32() : 0;
        return maxOrder + 1;
    }

    private async Task<PlaybookNodeDto> MapToDto(NodeEntity entity, CancellationToken cancellationToken)
    {
        // Load N:N relationships
        var skillIds = await GetRelatedIdsAsync(entity.Id, SkillRelationship, "sprk_analysisskillid", cancellationToken);
        var knowledgeIds = await GetRelatedIdsAsync(entity.Id, KnowledgeRelationship, "sprk_analysisknowledgeid", cancellationToken);

        // Parse depends on JSON
        var dependsOn = Array.Empty<Guid>();
        if (!string.IsNullOrEmpty(entity.DependsOnJson))
        {
            try
            {
                dependsOn = JsonSerializer.Deserialize<Guid[]>(entity.DependsOnJson) ?? [];
            }
            catch (JsonException)
            {
                _logger.LogWarning("Failed to parse DependsOnJson for node {NodeId}", entity.Id);
            }
        }

        return new PlaybookNodeDto
        {
            Id = entity.Id,
            PlaybookId = entity.PlaybookId ?? Guid.Empty,
            ActionId = entity.ActionId ?? Guid.Empty,
            ToolId = entity.ToolId,
            Name = entity.Name ?? string.Empty,
            ExecutionOrder = entity.ExecutionOrder ?? 0,
            DependsOn = dependsOn,
            OutputVariable = entity.OutputVariable ?? string.Empty,
            ConditionJson = entity.ConditionJson,
            ConfigJson = entity.ConfigJson,
            ModelDeploymentId = entity.ModelDeploymentId,
            TimeoutSeconds = entity.TimeoutSeconds,
            RetryCount = entity.RetryCount,
            PositionX = entity.PositionX,
            PositionY = entity.PositionY,
            IsActive = entity.IsActive ?? true,
            SkillIds = skillIds,
            KnowledgeIds = knowledgeIds,
            CreatedOn = entity.CreatedOn ?? DateTime.UtcNow,
            ModifiedOn = entity.ModifiedOn ?? DateTime.UtcNow
        };
    }

    private async Task AssociateRelationshipsAsync(
        Guid nodeId,
        Guid[]? skillIds,
        Guid[]? knowledgeIds,
        CancellationToken cancellationToken)
    {
        if (skillIds?.Length > 0)
        {
            foreach (var skillId in skillIds)
            {
                await AssociateAsync(nodeId, SkillRelationship, "sprk_analysisskills", skillId, cancellationToken);
            }
        }

        if (knowledgeIds?.Length > 0)
        {
            foreach (var knowledgeId in knowledgeIds)
            {
                await AssociateAsync(nodeId, KnowledgeRelationship, "sprk_analysisknowledges", knowledgeId, cancellationToken);
            }
        }
    }

    private async Task AssociateAsync(
        Guid nodeId,
        string relationshipName,
        string targetEntitySet,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        var url = $"{EntitySetName}({nodeId})/{relationshipName}/$ref";
        var payload = new Dictionary<string, string>
        {
            ["@odata.id"] = $"{_apiUrl}{targetEntitySet}({targetId})"
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();
            _logger.LogDebug("Associated {Relationship}: {NodeId} -> {TargetId}", relationshipName, nodeId, targetId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            _logger.LogWarning("Association may already exist: {Relationship} {NodeId} -> {TargetId}", relationshipName, nodeId, targetId);
        }
    }

    private async Task ClearRelationshipsAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        await ClearRelationshipAsync(nodeId, SkillRelationship, "sprk_analysisskillid", cancellationToken);
        await ClearRelationshipAsync(nodeId, KnowledgeRelationship, "sprk_analysisknowledgeid", cancellationToken);
    }

    private async Task ClearRelationshipAsync(
        Guid nodeId,
        string relationshipName,
        string targetIdField,
        CancellationToken cancellationToken)
    {
        var existingIds = await GetRelatedIdsAsync(nodeId, relationshipName, targetIdField, cancellationToken);

        foreach (var targetId in existingIds)
        {
            var url = $"{EntitySetName}({nodeId})/{relationshipName}({targetId})/$ref";
            try
            {
                var response = await _httpClient.DeleteAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to disassociate {Relationship}: {NodeId} -> {TargetId}", relationshipName, nodeId, targetId);
            }
        }
    }

    private async Task<Guid[]> GetRelatedIdsAsync(
        Guid nodeId,
        string relationshipName,
        string targetIdField,
        CancellationToken cancellationToken)
    {
        var url = $"{EntitySetName}({nodeId})/{relationshipName}?$select={targetIdField}";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(JsonOptions, cancellationToken);
            if (result?.Value == null) return [];

            return result.Value
                .Select(v => v.TryGetProperty(targetIdField, out var idElement) ? idElement.GetGuid() : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get related IDs for {Relationship}", relationshipName);
            return [];
        }
    }

    #region Canvas Sync Helpers

    /// <summary>
    /// Load all node entities for a playbook without resolving N:N relationships (fast path for sync).
    /// </summary>
    private async Task<NodeEntity[]> GetNodesRawAsync(Guid playbookId, CancellationToken cancellationToken)
    {
        var select = GetSelectFields();
        var filter = $"_sprk_playbookid_value eq {playbookId}";
        var url = $"{EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(JsonOptions, cancellationToken);
        if (result?.Value == null) return [];

        return result.Value
            .Select(e => JsonSerializer.Deserialize<NodeEntity>(e.GetRawText(), JsonOptions))
            .Where(e => e != null)
            .ToArray()!;
    }

    /// <summary>
    /// Build a lookup from canvas node ID → (Dataverse node ID, entity) by parsing __canvasNodeId from configJson.
    /// </summary>
    private static Dictionary<string, (Guid NodeId, NodeEntity Entity)> BuildCanvasIdMap(NodeEntity[] entities)
    {
        var map = new Dictionary<string, (Guid, NodeEntity)>();
        foreach (var entity in entities)
        {
            var canvasId = ExtractCanvasNodeId(entity.ConfigJson);
            if (canvasId != null)
            {
                map[canvasId] = (entity.Id, entity);
            }
        }
        return map;
    }

    /// <summary>
    /// Compute execution order for each canvas node via topological sort (Kahn's algorithm) of canvas edges.
    /// </summary>
    private static Dictionary<string, int> ComputeExecutionOrders(CanvasNodeDto[] nodes, CanvasEdgeDto[] edges)
    {
        var nodeIds = new HashSet<string>(nodes.Select(n => n.Id));
        var inDegree = nodes.ToDictionary(n => n.Id, _ => 0);
        var adjacency = nodes.ToDictionary(n => n.Id, _ => new List<string>());

        foreach (var edge in edges)
        {
            if (nodeIds.Contains(edge.Source) && nodeIds.Contains(edge.Target))
            {
                adjacency[edge.Source].Add(edge.Target);
                inDegree[edge.Target]++;
            }
        }

        // Kahn's algorithm
        var queue = new Queue<string>();
        foreach (var (id, degree) in inDegree)
        {
            if (degree == 0) queue.Enqueue(id);
        }

        var order = new Dictionary<string, int>();
        var currentOrder = 1;

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            order[nodeId] = currentOrder++;

            foreach (var target in adjacency[nodeId])
            {
                inDegree[target]--;
                if (inDegree[target] == 0) queue.Enqueue(target);
            }
        }

        // Nodes in cycles get order 0 (should not happen in a valid canvas)
        foreach (var node in nodes)
        {
            order.TryAdd(node.Id, 0);
        }

        return order;
    }

    /// <summary>
    /// Build a map of canvas node ID → list of source canvas node IDs (from incoming edges).
    /// </summary>
    private static Dictionary<string, List<string>> BuildIncomingEdgeMap(CanvasEdgeDto[] edges)
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var edge in edges)
        {
            if (!map.TryGetValue(edge.Target, out var sources))
            {
                sources = [];
                map[edge.Target] = sources;
            }
            sources.Add(edge.Source);
        }
        return map;
    }

    /// <summary>
    /// Create a new sprk_playbooknode record from canvas node data.
    /// </summary>
    private async Task<Guid> CreateNodeFieldsFromCanvasAsync(
        Guid playbookId, CanvasNodeDto canvasNode, int executionOrder, CancellationToken ct)
    {
        var data = canvasNode.Data;
        var name = ExtractStringValue(data, "label")
                   ?? ExtractStringValue(data, "name")
                   ?? canvasNode.Type;
        var actionId = ExtractGuidValue(data, "actionId");
        var toolId = ExtractGuidValue(data, "toolId");
        var modelDeploymentId = ExtractGuidValue(data, "modelDeploymentId");
        var outputVariable = ExtractStringValue(data, "outputVariable") ?? $"output_{canvasNode.Id}";
        var timeoutSeconds = ExtractIntValue(data, "timeoutSeconds");
        var retryCount = ExtractIntValue(data, "retryCount");
        var conditionJson = ExtractStringValue(data, "conditionJson");
        var isActive = ExtractBoolValue(data, "isActive") ?? true;
        var configJson = BuildConfigJson(canvasNode.Id, data);

        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = name,
            ["sprk_playbookid@odata.bind"] = $"/sprk_analysisplaybooks({playbookId})",
            ["sprk_executionorder"] = executionOrder,
            ["sprk_outputvariable"] = outputVariable,
            ["sprk_configjson"] = configJson,
            ["sprk_position_x"] = (int)canvasNode.X,
            ["sprk_position_y"] = (int)canvasNode.Y,
            ["sprk_isactive"] = isActive
        };

        if (actionId.HasValue)
            payload["sprk_actionid@odata.bind"] = $"/sprk_analysisactions({actionId.Value})";
        if (toolId.HasValue)
            payload["sprk_toolid@odata.bind"] = $"/sprk_analysistools({toolId.Value})";
        if (modelDeploymentId.HasValue)
            payload["sprk_modeldeploymentid@odata.bind"] = $"/sprk_aimodeldeployments({modelDeploymentId.Value})";
        if (timeoutSeconds.HasValue)
            payload["sprk_timeoutseconds"] = timeoutSeconds.Value;
        if (retryCount.HasValue)
            payload["sprk_retrycount"] = retryCount.Value;
        if (!string.IsNullOrEmpty(conditionJson))
            payload["sprk_conditionjson"] = conditionJson;

        var response = await _httpClient.PostAsJsonAsync(EntitySetName, payload, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var entityIdHeader = response.Headers.GetValues("OData-EntityId").FirstOrDefault()
            ?? throw new InvalidOperationException("Failed to get entity ID from create response");
        var nodeId = Guid.Parse(entityIdHeader.Split('(', ')')[1]);

        _logger.LogInformation("Created node {NodeId} from canvas node {CanvasNodeId}: {Name}",
            nodeId, canvasNode.Id, name);
        return nodeId;
    }

    /// <summary>
    /// Update an existing sprk_playbooknode record from canvas node data.
    /// </summary>
    private async Task UpdateNodeFieldsFromCanvasAsync(
        Guid nodeId, CanvasNodeDto canvasNode, int executionOrder, CancellationToken ct)
    {
        var data = canvasNode.Data;
        var name = ExtractStringValue(data, "label") ?? ExtractStringValue(data, "name");
        var actionId = ExtractGuidValue(data, "actionId");
        var toolId = ExtractGuidValue(data, "toolId");
        var modelDeploymentId = ExtractGuidValue(data, "modelDeploymentId");
        var outputVariable = ExtractStringValue(data, "outputVariable");
        var timeoutSeconds = ExtractIntValue(data, "timeoutSeconds");
        var retryCount = ExtractIntValue(data, "retryCount");
        var conditionJson = ExtractStringValue(data, "conditionJson");
        var isActive = ExtractBoolValue(data, "isActive");
        var configJson = BuildConfigJson(canvasNode.Id, data);

        var payload = new Dictionary<string, object?>
        {
            ["sprk_executionorder"] = executionOrder,
            ["sprk_configjson"] = configJson,
            ["sprk_position_x"] = (int)canvasNode.X,
            ["sprk_position_y"] = (int)canvasNode.Y
        };

        if (!string.IsNullOrEmpty(name))
            payload["sprk_name"] = name;
        if (actionId.HasValue)
            payload["sprk_actionid@odata.bind"] = $"/sprk_analysisactions({actionId.Value})";
        if (toolId.HasValue)
            payload["sprk_toolid@odata.bind"] = $"/sprk_analysistools({toolId.Value})";
        if (modelDeploymentId.HasValue)
            payload["sprk_modeldeploymentid@odata.bind"] = $"/sprk_aimodeldeployments({modelDeploymentId.Value})";
        if (!string.IsNullOrEmpty(outputVariable))
            payload["sprk_outputvariable"] = outputVariable;
        if (timeoutSeconds.HasValue)
            payload["sprk_timeoutseconds"] = timeoutSeconds.Value;
        if (retryCount.HasValue)
            payload["sprk_retrycount"] = retryCount.Value;
        if (conditionJson != null)
            payload["sprk_conditionjson"] = conditionJson;
        if (isActive.HasValue)
            payload["sprk_isactive"] = isActive.Value;

        var url = $"{EntitySetName}({nodeId})";
        var response = await _httpClient.PatchAsJsonAsync(url, payload, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogDebug("Updated node {NodeId} from canvas node {CanvasNodeId}", nodeId, canvasNode.Id);
    }

    /// <summary>
    /// Patch only the sprk_dependsonjson field on a node record.
    /// </summary>
    private async Task UpdateDependsOnAsync(Guid nodeId, Guid[] dependsOnGuids, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["sprk_dependsonjson"] = dependsOnGuids.Length > 0
                ? JsonSerializer.Serialize(dependsOnGuids)
                : null
        };

        var url = $"{EntitySetName}({nodeId})";
        var response = await _httpClient.PatchAsJsonAsync(url, payload, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Build a configJson string that includes the __canvasNodeId marker plus any unmapped data fields.
    /// </summary>
    private static string BuildConfigJson(string canvasNodeId, Dictionary<string, object?>? data)
    {
        var config = new Dictionary<string, object?> { ["__canvasNodeId"] = canvasNodeId };

        // Fields that are mapped to dedicated Dataverse columns (not stored in configJson)
        var mappedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "label", "name", "actionId", "toolId", "skillIds", "knowledgeIds",
            "outputVariable", "timeoutSeconds", "retryCount", "modelDeploymentId",
            "conditionJson", "isActive"
        };

        if (data != null)
        {
            foreach (var (key, value) in data)
            {
                if (!mappedKeys.Contains(key))
                    config[key] = value;
            }
        }

        return JsonSerializer.Serialize(config, JsonOptions);
    }

    /// <summary>
    /// Extract the __canvasNodeId value from a node's configJson.
    /// </summary>
    private static string? ExtractCanvasNodeId(string? configJson)
    {
        if (string.IsNullOrEmpty(configJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            return doc.RootElement.TryGetProperty("__canvasNodeId", out var prop)
                ? prop.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractStringValue(Dictionary<string, object?>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value) || value == null) return null;
        if (value is JsonElement je)
            return je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
        return value.ToString();
    }

    private static Guid? ExtractGuidValue(Dictionary<string, object?>? data, string key)
    {
        var str = ExtractStringValue(data, key);
        return Guid.TryParse(str, out var guid) ? guid : null;
    }

    private static int? ExtractIntValue(Dictionary<string, object?>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value) || value == null) return null;
        if (value is JsonElement je)
            return je.ValueKind == JsonValueKind.Number ? je.TryGetInt32(out var i) ? i : null : null;
        return value is int intVal ? intVal : int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static bool? ExtractBoolValue(Dictionary<string, object?>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value) || value == null) return null;
        if (value is JsonElement je)
            return je.ValueKind is JsonValueKind.True or JsonValueKind.False ? je.GetBoolean() : null;
        return value is bool b ? b : null;
    }

    private static Guid[] ExtractGuidArray(Dictionary<string, object?>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value) || value == null) return [];
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            return je.EnumerateArray()
                .Select(e => Guid.TryParse(e.GetString(), out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToArray();
        }
        return [];
    }

    #endregion

    #endregion

    #region Internal Types

    private class NodeEntity
    {
        [JsonPropertyName("sprk_playbooknodeid")]
        public Guid Id { get; set; }

        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }

        [JsonPropertyName("_sprk_playbookid_value")]
        public Guid? PlaybookId { get; set; }

        [JsonPropertyName("_sprk_actionid_value")]
        public Guid? ActionId { get; set; }

        [JsonPropertyName("_sprk_toolid_value")]
        public Guid? ToolId { get; set; }

        [JsonPropertyName("_sprk_modeldeploymentid_value")]
        public Guid? ModelDeploymentId { get; set; }

        [JsonPropertyName("sprk_executionorder")]
        public int? ExecutionOrder { get; set; }

        [JsonPropertyName("sprk_dependsonjson")]
        public string? DependsOnJson { get; set; }

        [JsonPropertyName("sprk_outputvariable")]
        public string? OutputVariable { get; set; }

        [JsonPropertyName("sprk_conditionjson")]
        public string? ConditionJson { get; set; }

        [JsonPropertyName("sprk_configjson")]
        public string? ConfigJson { get; set; }

        [JsonPropertyName("sprk_timeoutseconds")]
        public int? TimeoutSeconds { get; set; }

        [JsonPropertyName("sprk_retrycount")]
        public int? RetryCount { get; set; }

        [JsonPropertyName("sprk_position_x")]
        public int? PositionX { get; set; }

        [JsonPropertyName("sprk_position_y")]
        public int? PositionY { get; set; }

        [JsonPropertyName("sprk_isactive")]
        public bool? IsActive { get; set; }

        [JsonPropertyName("createdon")]
        public DateTime? CreatedOn { get; set; }

        [JsonPropertyName("modifiedon")]
        public DateTime? ModifiedOn { get; set; }
    }

    private class ODataCollectionResponse
    {
        [JsonPropertyName("value")]
        public JsonElement[]? Value { get; set; }
    }

    #endregion
}
