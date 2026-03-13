using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Focused service for AnalysisKnowledge CRUD operations against Dataverse.
/// Extracted from ScopeResolverService as part of god-class decomposition (PPI-054).
/// </summary>
public class AnalysisKnowledgeService : DataverseHttpServiceBase
{
    public AnalysisKnowledgeService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AnalysisKnowledgeService> logger)
        : base(httpClient, configuration, logger)
    {
    }

    /// <summary>
    /// Get a knowledge source by ID.
    /// </summary>
    public async Task<AnalysisKnowledge?> GetKnowledgeAsync(Guid knowledgeId, CancellationToken cancellationToken)
    {
        Logger.LogInformation("[GET KNOWLEDGE] Loading knowledge {KnowledgeId} from Dataverse", knowledgeId);

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysisknowledges({knowledgeId})?$expand=sprk_KnowledgeTypeId($select=sprk_name)";
        Logger.LogInformation("[GET KNOWLEDGE] URL: {Url}", url);
        var response = await Http.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogWarning("[GET KNOWLEDGE] Knowledge {KnowledgeId} not found in Dataverse", knowledgeId);
            return null;
        }

        await EnsureSuccessWithDiagnosticsAsync(response, $"GetKnowledgeAsync({knowledgeId})", cancellationToken);

        var entity = await response.Content.ReadFromJsonAsync<KnowledgeEntity>(cancellationToken);
        if (entity == null)
        {
            Logger.LogWarning("[GET KNOWLEDGE] Failed to deserialize knowledge {KnowledgeId}", knowledgeId);
            return null;
        }

        var knowledgeType = ResolveKnowledgeDeliveryType(entity);

        var knowledge = new AnalysisKnowledge
        {
            Id = entity.Id,
            Name = entity.Name ?? "Unnamed Knowledge",
            Description = entity.Description,
            Type = knowledgeType,
            Content = entity.Content,
            DeploymentId = entity.DeploymentId,
            OwnerType = ScopeOwnerType.System,
            IsImmutable = false
        };

        Logger.LogInformation("[GET KNOWLEDGE] Loaded knowledge from Dataverse: {KnowledgeName} (Type: {KnowledgeType})",
            knowledge.Name, knowledge.Type);

        return knowledge;
    }

    /// <summary>
    /// Look up a knowledge source by name.
    /// </summary>
    public async Task<AnalysisKnowledge?> GetKnowledgeByNameAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        Logger.LogDebug("[GET KNOWLEDGE BY NAME] Searching for knowledge with name '{KnowledgeName}'", name);

        await EnsureAuthenticatedAsync(cancellationToken);

        var escapedName = name.Replace("'", "''");
        var url = $"sprk_analysisknowledges?$filter=sprk_name eq '{escapedName}'&$expand=sprk_KnowledgeTypeId($select=sprk_name)&$top=1";

        var response = await Http.GetAsync(url, cancellationToken);
        await EnsureSuccessWithDiagnosticsAsync(response, $"GetKnowledgeByNameAsync('{name}')", cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<KnowledgeEntity>>(cancellationToken);
        var entity = result?.Value.FirstOrDefault();

        if (entity == null)
        {
            Logger.LogDebug("[GET KNOWLEDGE BY NAME] No knowledge found with name '{KnowledgeName}'", name);
            return null;
        }

        var knowledgeType = ResolveKnowledgeDeliveryType(entity);

        var knowledge = new AnalysisKnowledge
        {
            Id = entity.Id,
            Name = entity.Name ?? "Unnamed Knowledge",
            Description = entity.Description,
            Type = knowledgeType,
            Content = entity.Content,
            DeploymentId = entity.DeploymentId,
            OwnerType = ScopeOwnerType.System,
            IsImmutable = false
        };

        Logger.LogInformation("[GET KNOWLEDGE BY NAME] Found knowledge '{KnowledgeName}' (ID: {KnowledgeId})",
            knowledge.Name, knowledge.Id);

        return knowledge;
    }

    /// <summary>
    /// List all available knowledge sources with pagination.
    /// </summary>
    public async Task<ScopeListResult<AnalysisKnowledge>> ListKnowledgeAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("[LIST KNOWLEDGE] Querying Dataverse: Page={Page}, PageSize={PageSize}, NameFilter={NameFilter}",
            options.Page, options.PageSize, options.NameFilter);

        await EnsureAuthenticatedAsync(cancellationToken);

        var sortMappings = new Dictionary<string, string>
        {
            ["name"] = "sprk_name",
            ["type"] = "sprk_name"
        };

        var query = BuildODataQuery(
            options,
            selectFields: "sprk_analysisknowledgeid,sprk_name,sprk_description,sprk_content,sprk_deploymentid,sprk_knowledgedeliverytype",
            expandClause: "sprk_KnowledgeTypeId($select=sprk_name)",
            nameFieldPath: "sprk_name",
            categoryFieldPath: null,
            sortFieldMappings: sortMappings);

        var url = $"sprk_analysisknowledges?{query}";
        Logger.LogDebug("[LIST KNOWLEDGE] Query URL: {Url}", url);

        var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<KnowledgeEntity>>(cancellationToken);
        if (result == null)
        {
            Logger.LogWarning("[LIST KNOWLEDGE] Failed to deserialize response");
            return new ScopeListResult<AnalysisKnowledge>
            {
                Items = [],
                TotalCount = 0,
                Page = options.Page,
                PageSize = options.PageSize
            };
        }

        var knowledgeItems = result.Value.Select(entity =>
        {
            var knowledgeType = ResolveKnowledgeDeliveryType(entity);
            return new AnalysisKnowledge
            {
                Id = entity.Id,
                Name = entity.Name ?? "Unnamed Knowledge",
                Description = entity.Description,
                Type = knowledgeType,
                Content = entity.Content,
                DeploymentId = entity.DeploymentId,
                OwnerType = ScopeOwnerType.System,
                IsImmutable = false
            };
        }).ToArray();

        Logger.LogInformation("[LIST KNOWLEDGE] Retrieved {Count} knowledge items from Dataverse (Total: {TotalCount})",
            knowledgeItems.Length, result.ODataCount ?? knowledgeItems.Length);

        return new ScopeListResult<AnalysisKnowledge>
        {
            Items = knowledgeItems,
            TotalCount = result.ODataCount ?? knowledgeItems.Length,
            Page = options.Page,
            PageSize = options.PageSize
        };
    }

    /// <summary>
    /// Create a new analysis knowledge source.
    /// </summary>
    public async Task<AnalysisKnowledge> CreateKnowledgeAsync(CreateKnowledgeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request.Name));

        var name = EnsureCustomerPrefix(request.Name);

        Logger.LogInformation("[CREATE KNOWLEDGE] Creating knowledge '{KnowledgeName}' in Dataverse", name);

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = name,
            ["sprk_description"] = request.Description,
            ["sprk_content"] = request.Content
        };

        if (request.DeploymentId.HasValue)
        {
            payload["sprk_deploymentid"] = request.DeploymentId.Value.ToString();
        }

        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "sprk_analysisknowledges")
        {
            Content = httpContent
        };
        postRequest.Headers.Add("Prefer", "return=representation");

        var response = await Http.SendAsync(postRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<KnowledgeEntity>(cancellationToken);
        if (entity == null)
        {
            throw new InvalidOperationException("Failed to deserialize created knowledge from Dataverse response");
        }

        var knowledgeType = entity.DeliveryType.HasValue || entity.KnowledgeTypeId?.Name != null
            ? ResolveKnowledgeDeliveryType(entity)
            : request.Type;

        var knowledge = new AnalysisKnowledge
        {
            Id = entity.Id,
            Name = entity.Name ?? name,
            Description = entity.Description ?? request.Description,
            Type = knowledgeType,
            Content = entity.Content ?? request.Content,
            DeploymentId = entity.DeploymentId ?? request.DeploymentId,
            DocumentId = request.DocumentId,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false
        };

        Logger.LogInformation("[CREATE KNOWLEDGE] Created knowledge '{KnowledgeName}' with ID {KnowledgeId}", knowledge.Name, knowledge.Id);

        return knowledge;
    }

    /// <summary>
    /// Update an existing analysis knowledge source.
    /// </summary>
    public async Task<AnalysisKnowledge> UpdateKnowledgeAsync(Guid id, UpdateKnowledgeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Logger.LogInformation("[UPDATE KNOWLEDGE] Updating knowledge {KnowledgeId} in Dataverse", id);

        var existing = await GetKnowledgeAsync(id, cancellationToken);
        if (existing == null)
        {
            Logger.LogWarning("[UPDATE KNOWLEDGE] Knowledge {KnowledgeId} not found for update", id);
            throw new KeyNotFoundException($"Knowledge with ID '{id}' not found.");
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "update");

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new Dictionary<string, object?>();

        if (request.Name != null)
            payload["sprk_name"] = EnsureCustomerPrefix(request.Name);
        if (request.Description != null)
            payload["sprk_description"] = request.Description;
        if (request.Content != null)
            payload["sprk_content"] = request.Content;
        if (request.DeploymentId.HasValue)
            payload["sprk_deploymentid"] = request.DeploymentId.Value.ToString();

        var url = $"sprk_analysisknowledges({id})";
        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = httpContent
        };

        var response = await Http.SendAsync(patchRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var updated = await GetKnowledgeAsync(id, cancellationToken);
        if (updated == null)
        {
            throw new InvalidOperationException($"Failed to retrieve updated knowledge {id} from Dataverse");
        }

        Logger.LogInformation("[UPDATE KNOWLEDGE] Updated knowledge '{KnowledgeName}' (ID: {KnowledgeId})", updated.Name, id);

        return updated;
    }

    /// <summary>
    /// Delete an analysis knowledge source.
    /// </summary>
    public async Task<bool> DeleteKnowledgeAsync(Guid id, CancellationToken cancellationToken)
    {
        Logger.LogInformation("[DELETE KNOWLEDGE] Deleting knowledge {KnowledgeId} from Dataverse", id);

        var existing = await GetKnowledgeAsync(id, cancellationToken);
        if (existing == null)
        {
            Logger.LogWarning("[DELETE KNOWLEDGE] Knowledge {KnowledgeId} not found for deletion", id);
            return false;
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "delete");

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysisknowledges({id})";
        var response = await Http.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogWarning("[DELETE KNOWLEDGE] Knowledge {KnowledgeId} not found in Dataverse", id);
            return false;
        }

        response.EnsureSuccessStatusCode();

        Logger.LogInformation("[DELETE KNOWLEDGE] Deleted knowledge '{KnowledgeName}' (ID: {KnowledgeId})", existing.Name, id);
        return true;
    }

    /// <summary>
    /// Create a copy of an existing knowledge source with a new name.
    /// </summary>
    public async Task<AnalysisKnowledge> SaveAsKnowledgeAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));

        Logger.LogInformation("Saving knowledge {SourceId} as '{NewName}'", sourceId, newName);

        var source = await GetKnowledgeAsync(sourceId, cancellationToken);
        if (source == null)
        {
            Logger.LogWarning("Source knowledge {SourceId} not found for Save As", sourceId);
            throw new KeyNotFoundException($"Source knowledge with ID '{sourceId}' not found.");
        }

        var createRequest = new CreateKnowledgeRequest
        {
            Name = newName,
            Description = source.Description,
            Type = source.Type,
            Content = source.Content,
            DeploymentId = source.DeploymentId,
            DocumentId = source.DocumentId
        };

        var copy = await CreateKnowledgeAsync(createRequest, cancellationToken);

        Logger.LogInformation("[SAVE AS KNOWLEDGE] Created knowledge copy '{Name}' (ID: {CopyId}) based on {SourceId}",
            copy.Name, copy.Id, sourceId);

        return copy with { BasedOnId = sourceId };
    }

    /// <summary>
    /// Create a child knowledge source that extends the parent.
    /// </summary>
    public async Task<AnalysisKnowledge> ExtendKnowledgeAsync(Guid parentId, string childName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childName, nameof(childName));

        Logger.LogInformation("[EXTEND KNOWLEDGE] Extending knowledge {ParentId} with child '{ChildName}'", parentId, childName);

        var parent = await GetKnowledgeAsync(parentId, cancellationToken);
        if (parent == null)
        {
            Logger.LogWarning("[EXTEND KNOWLEDGE] Parent knowledge {ParentId} not found for Extend", parentId);
            throw new KeyNotFoundException($"Parent knowledge with ID '{parentId}' not found.");
        }

        var createRequest = new CreateKnowledgeRequest
        {
            Name = childName,
            Description = parent.Description,
            Type = parent.Type,
            Content = parent.Content,
            DeploymentId = parent.DeploymentId,
            DocumentId = parent.DocumentId
        };

        var child = await CreateKnowledgeAsync(createRequest, cancellationToken);

        Logger.LogInformation("[EXTEND KNOWLEDGE] Created child knowledge '{Name}' (ID: {ChildId}) extending {ParentId}",
            child.Name, child.Id, parentId);

        return child with { ParentScopeId = parentId };
    }

    #region Private Helpers

    /// <summary>
    /// Resolves the KnowledgeType for a knowledge entity.
    /// Prefers the explicit delivery type field when set;
    /// falls back to name-based mapping for backward compatibility.
    /// </summary>
    internal static KnowledgeType ResolveKnowledgeDeliveryType(KnowledgeEntity entity)
    {
        if (entity.DeliveryType.HasValue)
        {
            return entity.DeliveryType.Value switch
            {
                100000000 => KnowledgeType.Inline,
                100000001 => KnowledgeType.Document,
                100000002 => KnowledgeType.RagIndex,
                _ => KnowledgeType.Inline
            };
        }

        return entity.KnowledgeTypeId?.Name?.ToLowerInvariant() switch
        {
            "regulations" => KnowledgeType.RagIndex,
            "rag" => KnowledgeType.RagIndex,
            "document" => KnowledgeType.Document,
            _ => KnowledgeType.Inline
        };
    }

    #endregion

    #region Private DTOs

    internal class KnowledgeEntity
    {
        [JsonPropertyName("sprk_analysisknowledgeid")]
        public Guid Id { get; set; }

        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }

        [JsonPropertyName("sprk_description")]
        public string? Description { get; set; }

        [JsonPropertyName("sprk_content")]
        public string? Content { get; set; }

        [JsonPropertyName("sprk_deploymentid")]
        public Guid? DeploymentId { get; set; }

        [JsonPropertyName("sprk_KnowledgeTypeId")]
        public KnowledgeTypeReference? KnowledgeTypeId { get; set; }

        [JsonPropertyName("sprk_knowledgedeliverytype")]
        public int? DeliveryType { get; set; }
    }

    internal class KnowledgeTypeReference
    {
        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }
    }

    #endregion
}
