using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Focused service for AnalysisTool CRUD operations against Dataverse.
/// Extracted from ScopeResolverService as part of god-class decomposition (PPI-054).
/// </summary>
public class AnalysisToolService : DataverseHttpServiceBase
{
    public AnalysisToolService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AnalysisToolService> logger)
        : base(httpClient, configuration, logger)
    {
    }

    /// <summary>
    /// Get a tool by ID.
    /// </summary>
    public async Task<AnalysisTool?> GetToolAsync(Guid toolId, CancellationToken cancellationToken)
    {
        Logger.LogDebug("Loading tool {ToolId} from Dataverse", toolId);

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysistools({toolId})?$expand=sprk_ToolTypeId($select=sprk_name)";
        var response = await Http.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogWarning("Tool {ToolId} not found in Dataverse", toolId);
            return null;
        }

        await EnsureSuccessWithDiagnosticsAsync(response, $"GetToolAsync({toolId})", cancellationToken);

        var entity = await response.Content.ReadFromJsonAsync<ToolEntity>(cancellationToken);
        if (entity == null)
        {
            Logger.LogWarning("Failed to deserialize tool {ToolId} from Dataverse response", toolId);
            return null;
        }

        var handlerClass = !string.IsNullOrWhiteSpace(entity.HandlerClass)
            ? entity.HandlerClass
            : "GenericAnalysisHandler";

        var toolType = !string.IsNullOrEmpty(entity.HandlerClass)
            ? MapHandlerClassToToolType(entity.HandlerClass)
            : MapToolTypeName(entity.ToolTypeId?.Name ?? "");

        var tool = new AnalysisTool
        {
            Id = entity.Id,
            Name = entity.Name ?? "Unnamed Tool",
            Description = entity.Description,
            Type = toolType,
            HandlerClass = handlerClass,
            Configuration = entity.Configuration,
            OwnerType = ScopeOwnerType.System,
            IsImmutable = false
        };

        var mappingSource = !string.IsNullOrEmpty(entity.HandlerClass) ? "HandlerClass" : "GenericAnalysisHandler (fallback)";
        Logger.LogInformation("Loaded tool from Dataverse: {ToolName} (Type: {ToolType}, MappedFrom: {MappingSource}, HandlerClass: {HandlerClass})",
            tool.Name, tool.Type, mappingSource, handlerClass);

        return tool;
    }

    /// <summary>
    /// List all available tools with pagination.
    /// </summary>
    public async Task<ScopeListResult<AnalysisTool>> ListToolsAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("[LIST TOOLS] Querying Dataverse: Page={Page}, PageSize={PageSize}, NameFilter={NameFilter}",
            options.Page, options.PageSize, options.NameFilter);

        await EnsureAuthenticatedAsync(cancellationToken);

        var sortMappings = new Dictionary<string, string>
        {
            ["name"] = "sprk_name",
            ["type"] = "sprk_name"
        };

        var query = BuildODataQuery(
            options,
            selectFields: "sprk_analysistoolid,sprk_name,sprk_description,sprk_handlerclass,sprk_configuration",
            expandClause: "sprk_ToolTypeId($select=sprk_name)",
            nameFieldPath: "sprk_name",
            categoryFieldPath: null,
            sortFieldMappings: sortMappings);

        var url = $"sprk_analysistools?{query}";
        Logger.LogDebug("[LIST TOOLS] Query URL: {Url}", url);

        var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<ToolEntity>>(cancellationToken);
        if (result == null)
        {
            Logger.LogWarning("[LIST TOOLS] Failed to deserialize response");
            return new ScopeListResult<AnalysisTool>
            {
                Items = [],
                TotalCount = 0,
                Page = options.Page,
                PageSize = options.PageSize
            };
        }

        var tools = result.Value.Select(entity =>
        {
            var toolType = !string.IsNullOrEmpty(entity.HandlerClass)
                ? MapHandlerClassToToolType(entity.HandlerClass)
                : MapToolTypeName(entity.ToolTypeId?.Name ?? "");

            return new AnalysisTool
            {
                Id = entity.Id,
                Name = entity.Name ?? "Unnamed Tool",
                Description = entity.Description,
                Type = toolType,
                HandlerClass = entity.HandlerClass,
                Configuration = entity.Configuration,
                OwnerType = ScopeOwnerType.System,
                IsImmutable = false
            };
        }).ToArray();

        Logger.LogInformation("[LIST TOOLS] Retrieved {Count} tools from Dataverse (Total: {TotalCount})",
            tools.Length, result.ODataCount ?? tools.Length);

        return new ScopeListResult<AnalysisTool>
        {
            Items = tools,
            TotalCount = result.ODataCount ?? tools.Length,
            Page = options.Page,
            PageSize = options.PageSize
        };
    }

    /// <summary>
    /// Create a new analysis tool.
    /// </summary>
    public async Task<AnalysisTool> CreateToolAsync(CreateToolRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request.Name));

        var name = EnsureCustomerPrefix(request.Name);

        Logger.LogInformation("[CREATE TOOL] Creating tool '{ToolName}' in Dataverse", name);

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = name,
            ["sprk_description"] = request.Description,
            ["sprk_handlerclass"] = request.HandlerClass,
            ["sprk_configuration"] = request.Configuration
        };

        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "sprk_analysistools")
        {
            Content = httpContent
        };
        postRequest.Headers.Add("Prefer", "return=representation");

        var response = await Http.SendAsync(postRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<ToolEntity>(cancellationToken);
        if (entity == null)
        {
            throw new InvalidOperationException("Failed to deserialize created tool from Dataverse response");
        }

        var toolType = !string.IsNullOrEmpty(entity.HandlerClass)
            ? MapHandlerClassToToolType(entity.HandlerClass)
            : request.Type;

        var tool = new AnalysisTool
        {
            Id = entity.Id,
            Name = entity.Name ?? name,
            Description = entity.Description ?? request.Description,
            Type = toolType,
            HandlerClass = entity.HandlerClass ?? request.HandlerClass,
            Configuration = entity.Configuration ?? request.Configuration,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false
        };

        Logger.LogInformation("[CREATE TOOL] Created tool '{ToolName}' with ID {ToolId}", tool.Name, tool.Id);

        return tool;
    }

    /// <summary>
    /// Update an existing analysis tool.
    /// </summary>
    public async Task<AnalysisTool> UpdateToolAsync(Guid id, UpdateToolRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Logger.LogInformation("[UPDATE TOOL] Updating tool {ToolId} in Dataverse", id);

        var existing = await GetToolAsync(id, cancellationToken);
        if (existing == null)
        {
            Logger.LogWarning("[UPDATE TOOL] Tool {ToolId} not found for update", id);
            throw new KeyNotFoundException($"Tool with ID '{id}' not found.");
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "update");

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new Dictionary<string, object?>();

        if (request.Name != null)
            payload["sprk_name"] = EnsureCustomerPrefix(request.Name);
        if (request.Description != null)
            payload["sprk_description"] = request.Description;
        if (request.HandlerClass != null)
            payload["sprk_handlerclass"] = request.HandlerClass;
        if (request.Configuration != null)
            payload["sprk_configuration"] = request.Configuration;

        var url = $"sprk_analysistools({id})";
        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = httpContent
        };

        var response = await Http.SendAsync(patchRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var updated = await GetToolAsync(id, cancellationToken);
        if (updated == null)
        {
            throw new InvalidOperationException($"Failed to retrieve updated tool {id} from Dataverse");
        }

        Logger.LogInformation("[UPDATE TOOL] Updated tool '{ToolName}' (ID: {ToolId})", updated.Name, id);

        return updated;
    }

    /// <summary>
    /// Delete an analysis tool.
    /// </summary>
    public async Task<bool> DeleteToolAsync(Guid id, CancellationToken cancellationToken)
    {
        Logger.LogInformation("[DELETE TOOL] Deleting tool {ToolId} from Dataverse", id);

        var existing = await GetToolAsync(id, cancellationToken);
        if (existing == null)
        {
            Logger.LogWarning("[DELETE TOOL] Tool {ToolId} not found for deletion", id);
            return false;
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "delete");

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysistools({id})";
        var response = await Http.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogWarning("[DELETE TOOL] Tool {ToolId} not found in Dataverse", id);
            return false;
        }

        response.EnsureSuccessStatusCode();

        Logger.LogInformation("[DELETE TOOL] Deleted tool '{ToolName}' (ID: {ToolId})", existing.Name, id);
        return true;
    }

    /// <summary>
    /// Create a copy of an existing tool with a new name.
    /// </summary>
    public async Task<AnalysisTool> SaveAsToolAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));

        Logger.LogInformation("Saving tool {SourceId} as '{NewName}'", sourceId, newName);

        var source = await GetToolAsync(sourceId, cancellationToken);
        if (source == null)
        {
            Logger.LogWarning("Source tool {SourceId} not found for Save As", sourceId);
            throw new KeyNotFoundException($"Source tool with ID '{sourceId}' not found.");
        }

        var createRequest = new CreateToolRequest
        {
            Name = newName,
            Description = source.Description,
            Type = source.Type,
            HandlerClass = source.HandlerClass,
            Configuration = source.Configuration
        };

        var copy = await CreateToolAsync(createRequest, cancellationToken);

        Logger.LogInformation("[SAVE AS TOOL] Created tool copy '{Name}' (ID: {CopyId}) based on {SourceId}",
            copy.Name, copy.Id, sourceId);

        return copy with { BasedOnId = sourceId };
    }

    /// <summary>
    /// Create a child tool that extends the parent.
    /// </summary>
    public async Task<AnalysisTool> ExtendToolAsync(Guid parentId, string childName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childName, nameof(childName));

        Logger.LogInformation("[EXTEND TOOL] Extending tool {ParentId} with child '{ChildName}'", parentId, childName);

        var parent = await GetToolAsync(parentId, cancellationToken);
        if (parent == null)
        {
            Logger.LogWarning("[EXTEND TOOL] Parent tool {ParentId} not found for Extend", parentId);
            throw new KeyNotFoundException($"Parent tool with ID '{parentId}' not found.");
        }

        var createRequest = new CreateToolRequest
        {
            Name = childName,
            Description = parent.Description,
            Type = parent.Type,
            HandlerClass = parent.HandlerClass,
            Configuration = parent.Configuration
        };

        var child = await CreateToolAsync(createRequest, cancellationToken);

        Logger.LogInformation("[EXTEND TOOL] Created child tool '{Name}' (ID: {ChildId}) extending {ParentId}",
            child.Name, child.Id, parentId);

        return child with { ParentScopeId = parentId };
    }

    #region Private Helpers

    internal static ToolType MapHandlerClassToToolType(string handlerClass)
    {
        return handlerClass switch
        {
            string s when s.Contains("EntityExtractor", StringComparison.OrdinalIgnoreCase) => ToolType.EntityExtractor,
            string s when s.Contains("ClauseAnalyzer", StringComparison.OrdinalIgnoreCase) => ToolType.ClauseAnalyzer,
            string s when s.Contains("DocumentClassifier", StringComparison.OrdinalIgnoreCase) => ToolType.DocumentClassifier,
            string s when s.Contains("Summary", StringComparison.OrdinalIgnoreCase) => ToolType.Summary,
            string s when s.Contains("RiskDetector", StringComparison.OrdinalIgnoreCase) => ToolType.RiskDetector,
            string s when s.Contains("ClauseComparison", StringComparison.OrdinalIgnoreCase) => ToolType.ClauseComparison,
            string s when s.Contains("DateExtractor", StringComparison.OrdinalIgnoreCase) => ToolType.DateExtractor,
            string s when s.Contains("FinancialCalculator", StringComparison.OrdinalIgnoreCase) => ToolType.FinancialCalculator,
            _ => ToolType.Custom
        };
    }

    internal static ToolType MapToolTypeName(string typeName)
    {
        return typeName switch
        {
            string s when s.Contains("Entity Extraction", StringComparison.OrdinalIgnoreCase) => ToolType.EntityExtractor,
            string s when s.Contains("Classification", StringComparison.OrdinalIgnoreCase) => ToolType.DocumentClassifier,
            string s when s.Contains("Analysis", StringComparison.OrdinalIgnoreCase) => ToolType.Summary,
            string s when s.Contains("Calculation", StringComparison.OrdinalIgnoreCase) => ToolType.FinancialCalculator,
            _ => ToolType.Custom
        };
    }

    #endregion

    #region Private DTOs

    internal class ToolEntity
    {
        [JsonPropertyName("sprk_analysistoolid")]
        public Guid Id { get; set; }

        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }

        [JsonPropertyName("sprk_description")]
        public string? Description { get; set; }

        [JsonPropertyName("sprk_ToolTypeId")]
        public ToolTypeReference? ToolTypeId { get; set; }

        [JsonPropertyName("sprk_handlerclass")]
        public string? HandlerClass { get; set; }

        [JsonPropertyName("sprk_configuration")]
        public string? Configuration { get; set; }
    }

    internal class ToolTypeReference
    {
        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }
    }

    #endregion
}
