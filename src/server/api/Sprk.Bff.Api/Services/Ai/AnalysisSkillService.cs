using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Focused service for AnalysisSkill CRUD operations against Dataverse.
/// Extracted from ScopeResolverService as part of god-class decomposition (PPI-054).
/// </summary>
public class AnalysisSkillService : DataverseHttpServiceBase
{
    public AnalysisSkillService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AnalysisSkillService> logger)
        : base(httpClient, configuration, logger)
    {
    }

    /// <summary>
    /// Get a skill by ID.
    /// </summary>
    public async Task<AnalysisSkill?> GetSkillAsync(Guid skillId, CancellationToken cancellationToken)
    {
        Logger.LogDebug("Loading skill {SkillId} from Dataverse", skillId);

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysisskills({skillId})?$expand=sprk_SkillTypeId($select=sprk_name)";
        var response = await Http.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogWarning("Skill {SkillId} not found in Dataverse", skillId);
            return null;
        }

        await EnsureSuccessWithDiagnosticsAsync(response, $"GetSkillAsync({skillId})", cancellationToken);

        var entity = await response.Content.ReadFromJsonAsync<SkillEntity>(cancellationToken);
        if (entity == null)
        {
            Logger.LogWarning("Failed to deserialize skill {SkillId} from Dataverse response", skillId);
            return null;
        }

        var skill = new AnalysisSkill
        {
            Id = entity.Id,
            Name = entity.Name ?? "Unnamed Skill",
            Description = entity.Description,
            PromptFragment = entity.PromptFragment ?? "",
            Category = entity.SkillTypeId?.Name ?? "General",
            OwnerType = ScopeOwnerType.System,
            IsImmutable = false
        };

        Logger.LogInformation("Loaded skill from Dataverse: {SkillName}", skill.Name);

        return skill;
    }

    /// <summary>
    /// Look up a skill by name.
    /// </summary>
    public async Task<AnalysisSkill?> GetSkillByNameAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        Logger.LogDebug("[GET SKILL BY NAME] Searching for skill with name '{SkillName}'", name);

        await EnsureAuthenticatedAsync(cancellationToken);

        var escapedName = name.Replace("'", "''");
        var url = $"sprk_analysisskills?$filter=sprk_name eq '{escapedName}'&$expand=sprk_SkillTypeId($select=sprk_name)&$top=1";

        var response = await Http.GetAsync(url, cancellationToken);
        await EnsureSuccessWithDiagnosticsAsync(response, $"GetSkillByNameAsync('{name}')", cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<SkillEntity>>(cancellationToken);
        var entity = result?.Value.FirstOrDefault();

        if (entity == null)
        {
            Logger.LogDebug("[GET SKILL BY NAME] No skill found with name '{SkillName}'", name);
            return null;
        }

        var skill = new AnalysisSkill
        {
            Id = entity.Id,
            Name = entity.Name ?? "Unnamed Skill",
            Description = entity.Description,
            PromptFragment = entity.PromptFragment ?? "",
            Category = entity.SkillTypeId?.Name ?? "General",
            OwnerType = ScopeOwnerType.System,
            IsImmutable = false
        };

        Logger.LogInformation("[GET SKILL BY NAME] Found skill '{SkillName}' (ID: {SkillId})",
            skill.Name, skill.Id);

        return skill;
    }

    /// <summary>
    /// List all available skills with pagination.
    /// </summary>
    public async Task<ScopeListResult<AnalysisSkill>> ListSkillsAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("[LIST SKILLS] Querying Dataverse: Page={Page}, PageSize={PageSize}, NameFilter={NameFilter}, CategoryFilter={CategoryFilter}",
            options.Page, options.PageSize, options.NameFilter, options.CategoryFilter);

        await EnsureAuthenticatedAsync(cancellationToken);

        var sortMappings = new Dictionary<string, string>
        {
            ["name"] = "sprk_name",
            ["category"] = "sprk_name"
        };

        var query = BuildODataQuery(
            options,
            selectFields: "sprk_analysisskillid,sprk_name,sprk_description,sprk_promptfragment",
            expandClause: "sprk_SkillTypeId($select=sprk_name)",
            nameFieldPath: "sprk_name",
            categoryFieldPath: null,
            sortFieldMappings: sortMappings);

        var url = $"sprk_analysisskills?{query}";
        Logger.LogDebug("[LIST SKILLS] Query URL: {Url}", url);

        var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<SkillEntity>>(cancellationToken);
        if (result == null)
        {
            Logger.LogWarning("[LIST SKILLS] Failed to deserialize response");
            return new ScopeListResult<AnalysisSkill>
            {
                Items = [],
                TotalCount = 0,
                Page = options.Page,
                PageSize = options.PageSize
            };
        }

        var skills = result.Value.Select(entity => new AnalysisSkill
        {
            Id = entity.Id,
            Name = entity.Name ?? "Unnamed Skill",
            Description = entity.Description,
            PromptFragment = entity.PromptFragment ?? "",
            Category = entity.SkillTypeId?.Name ?? "General",
            OwnerType = ScopeOwnerType.System,
            IsImmutable = false
        });

        // Apply category filter in memory (category is in expanded entity)
        if (!string.IsNullOrWhiteSpace(options.CategoryFilter))
        {
            skills = skills.Where(s => s.Category?.Equals(options.CategoryFilter, StringComparison.OrdinalIgnoreCase) == true);
        }

        var skillArray = skills.ToArray();

        Logger.LogInformation("[LIST SKILLS] Retrieved {Count} skills from Dataverse (Total: {TotalCount})",
            skillArray.Length, result.ODataCount ?? skillArray.Length);

        return new ScopeListResult<AnalysisSkill>
        {
            Items = skillArray,
            TotalCount = result.ODataCount ?? skillArray.Length,
            Page = options.Page,
            PageSize = options.PageSize
        };
    }

    /// <summary>
    /// Create a new analysis skill.
    /// </summary>
    public async Task<AnalysisSkill> CreateSkillAsync(CreateSkillRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request.Name));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PromptFragment, nameof(request.PromptFragment));

        var name = EnsureCustomerPrefix(request.Name);

        Logger.LogInformation("[CREATE SKILL] Creating skill '{SkillName}' in Dataverse", name);

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = name,
            ["sprk_description"] = request.Description,
            ["sprk_promptfragment"] = request.PromptFragment
        };

        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "sprk_analysisskills")
        {
            Content = httpContent
        };
        postRequest.Headers.Add("Prefer", "return=representation");

        var response = await Http.SendAsync(postRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<SkillEntity>(cancellationToken);
        if (entity == null)
        {
            throw new InvalidOperationException("Failed to deserialize created skill from Dataverse response");
        }

        var skill = new AnalysisSkill
        {
            Id = entity.Id,
            Name = entity.Name ?? name,
            Description = entity.Description ?? request.Description,
            PromptFragment = entity.PromptFragment ?? request.PromptFragment,
            Category = entity.SkillTypeId?.Name ?? request.Category ?? "General",
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false
        };

        Logger.LogInformation("[CREATE SKILL] Created skill '{SkillName}' with ID {SkillId}", skill.Name, skill.Id);

        return skill;
    }

    /// <summary>
    /// Update an existing analysis skill.
    /// </summary>
    public async Task<AnalysisSkill> UpdateSkillAsync(Guid id, UpdateSkillRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Logger.LogInformation("[UPDATE SKILL] Updating skill {SkillId} in Dataverse", id);

        var existing = await GetSkillAsync(id, cancellationToken);
        if (existing == null)
        {
            Logger.LogWarning("[UPDATE SKILL] Skill {SkillId} not found for update", id);
            throw new KeyNotFoundException($"Skill with ID '{id}' not found.");
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "update");

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new Dictionary<string, object?>();

        if (request.Name != null)
            payload["sprk_name"] = EnsureCustomerPrefix(request.Name);
        if (request.Description != null)
            payload["sprk_description"] = request.Description;
        if (request.PromptFragment != null)
            payload["sprk_promptfragment"] = request.PromptFragment;

        var url = $"sprk_analysisskills({id})";
        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = httpContent
        };

        var response = await Http.SendAsync(patchRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var updated = await GetSkillAsync(id, cancellationToken);
        if (updated == null)
        {
            throw new InvalidOperationException($"Failed to retrieve updated skill {id} from Dataverse");
        }

        Logger.LogInformation("[UPDATE SKILL] Updated skill '{SkillName}' (ID: {SkillId})", updated.Name, id);

        return updated;
    }

    /// <summary>
    /// Delete an analysis skill.
    /// </summary>
    public async Task<bool> DeleteSkillAsync(Guid id, CancellationToken cancellationToken)
    {
        Logger.LogInformation("[DELETE SKILL] Deleting skill {SkillId} from Dataverse", id);

        var existing = await GetSkillAsync(id, cancellationToken);
        if (existing == null)
        {
            Logger.LogWarning("[DELETE SKILL] Skill {SkillId} not found for deletion", id);
            return false;
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "delete");

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysisskills({id})";
        var response = await Http.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogWarning("[DELETE SKILL] Skill {SkillId} not found in Dataverse", id);
            return false;
        }

        response.EnsureSuccessStatusCode();

        Logger.LogInformation("[DELETE SKILL] Deleted skill '{SkillName}' (ID: {SkillId})", existing.Name, id);
        return true;
    }

    /// <summary>
    /// Create a copy of an existing skill with a new name.
    /// </summary>
    public async Task<AnalysisSkill> SaveAsSkillAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));

        Logger.LogInformation("Saving skill {SourceId} as '{NewName}'", sourceId, newName);

        var source = await GetSkillAsync(sourceId, cancellationToken);
        if (source == null)
        {
            Logger.LogWarning("Source skill {SourceId} not found for Save As", sourceId);
            throw new KeyNotFoundException($"Source skill with ID '{sourceId}' not found.");
        }

        var createRequest = new CreateSkillRequest
        {
            Name = newName,
            Description = source.Description,
            PromptFragment = source.PromptFragment,
            Category = source.Category
        };

        var copy = await CreateSkillAsync(createRequest, cancellationToken);

        Logger.LogInformation("[SAVE AS SKILL] Created skill copy '{Name}' (ID: {CopyId}) based on {SourceId}",
            copy.Name, copy.Id, sourceId);

        return copy with { BasedOnId = sourceId };
    }

    /// <summary>
    /// Create a child skill that extends the parent.
    /// </summary>
    public async Task<AnalysisSkill> ExtendSkillAsync(Guid parentId, string childName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childName, nameof(childName));

        Logger.LogInformation("[EXTEND SKILL] Extending skill {ParentId} with child '{ChildName}'", parentId, childName);

        var parent = await GetSkillAsync(parentId, cancellationToken);
        if (parent == null)
        {
            Logger.LogWarning("[EXTEND SKILL] Parent skill {ParentId} not found for Extend", parentId);
            throw new KeyNotFoundException($"Parent skill with ID '{parentId}' not found.");
        }

        var createRequest = new CreateSkillRequest
        {
            Name = childName,
            Description = parent.Description,
            PromptFragment = parent.PromptFragment,
            Category = parent.Category
        };

        var child = await CreateSkillAsync(createRequest, cancellationToken);

        Logger.LogInformation("[EXTEND SKILL] Created child skill '{Name}' (ID: {ChildId}) extending {ParentId}",
            child.Name, child.Id, parentId);

        return child with { ParentScopeId = parentId };
    }

    #region Private DTOs

    internal class SkillEntity
    {
        [JsonPropertyName("sprk_analysisskillid")]
        public Guid Id { get; set; }

        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }

        [JsonPropertyName("sprk_description")]
        public string? Description { get; set; }

        [JsonPropertyName("sprk_promptfragment")]
        public string? PromptFragment { get; set; }

        [JsonPropertyName("sprk_SkillTypeId")]
        public SkillTypeReference? SkillTypeId { get; set; }
    }

    internal class SkillTypeReference
    {
        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }
    }

    #endregion
}
