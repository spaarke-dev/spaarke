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
