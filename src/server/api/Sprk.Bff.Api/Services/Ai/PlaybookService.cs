using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for managing analysis playbooks in Dataverse.
/// Uses Web API for CRUD operations and N:N relationship management.
/// </summary>
public class PlaybookService : IPlaybookService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly TokenCredential _credential;
    private readonly ILogger<PlaybookService> _logger;
    private readonly IDistributedCache? _cache;
    private AccessToken? _currentToken;

    private const string EntitySetName = "sprk_analysisplaybooks";
    private const string EntityLogicalName = "sprk_analysisplaybook";

    // N:N relationship names (from Task 020 verification)
    private const string ActionRelationship = "sprk_analysisplaybook_action";
    private const string SkillRelationship = "sprk_playbook_skill";
    private const string KnowledgeRelationship = "sprk_playbook_knowledge";
    private const string ToolRelationship = "sprk_playbook_tool";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PlaybookService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<PlaybookService> logger,
        IDistributedCache? cache = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");
        var tenantId = configuration["TENANT_ID"]
            ?? throw new InvalidOperationException("TENANT_ID configuration is required");
        var clientId = configuration["API_APP_ID"]
            ?? throw new InvalidOperationException("API_APP_ID configuration is required");
        var clientSecret = configuration["API_CLIENT_SECRET"] // Same app registration as Graph and DataverseAccessDataSource
            ?? throw new InvalidOperationException("API_CLIENT_SECRET configuration is required");

        // IMPORTANT: BaseAddress must end with trailing slash, otherwise relative URLs replace the last segment
        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/";
        _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);

        _httpClient.BaseAddress = new Uri(_apiUrl);
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogInformation("Initialized PlaybookService - DataverseUrl: {DataverseUrl}, Constructed ApiUrl: {ApiUrl}, BaseAddress: {BaseAddress}",
            dataverseUrl, _apiUrl, _httpClient.BaseAddress);
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

            _logger.LogDebug("Refreshed Dataverse access token for PlaybookService");
        }
    }

    /// <inheritdoc />
    public async Task<PlaybookResponse> CreatePlaybookAsync(
        SavePlaybookRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Creating playbook: {Name}", request.Name);

        // Create playbook entity
        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = request.Name,
            ["sprk_description"] = request.Description,
            ["sprk_ispublic"] = request.IsPublic,
            ["sprk_istemplate"] = request.IsTemplate
        };

        // Add output type lookup if provided
        if (request.OutputTypeId.HasValue)
        {
            payload["sprk_OutputTypeId@odata.bind"] = $"/sprk_aioutputtypes({request.OutputTypeId.Value})";
        }

        var response = await _httpClient.PostAsJsonAsync(EntitySetName, payload, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Extract ID from OData-EntityId header
        var entityIdHeader = response.Headers.GetValues("OData-EntityId").FirstOrDefault()
            ?? throw new InvalidOperationException("Failed to get entity ID from response");
        var playbookId = Guid.Parse(entityIdHeader.Split('(', ')')[1]);

        _logger.LogInformation("Created playbook {Id}: {Name}", playbookId, request.Name);

        // Associate N:N relationships
        await AssociateRelationshipsAsync(playbookId, request, cancellationToken);

        // Return the created playbook
        return await GetPlaybookAsync(playbookId, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve created playbook");
    }

    /// <inheritdoc />
    public async Task<PlaybookResponse> UpdatePlaybookAsync(
        Guid playbookId,
        SavePlaybookRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Updating playbook {Id}: {Name}", playbookId, request.Name);

        // Update playbook entity
        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = request.Name,
            ["sprk_description"] = request.Description,
            ["sprk_ispublic"] = request.IsPublic,
            ["sprk_istemplate"] = request.IsTemplate
        };

        // Add output type lookup if provided
        if (request.OutputTypeId.HasValue)
        {
            payload["sprk_OutputTypeId@odata.bind"] = $"/sprk_aioutputtypes({request.OutputTypeId.Value})";
        }

        var url = $"{EntitySetName}({playbookId})";
        var response = await _httpClient.PatchAsJsonAsync(url, payload, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Updated playbook {Id}", playbookId);

        // Update N:N relationships (clear existing and re-associate)
        await ClearRelationshipsAsync(playbookId, cancellationToken);
        await AssociateRelationshipsAsync(playbookId, request, cancellationToken);

        // Return the updated playbook
        return await GetPlaybookAsync(playbookId, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve updated playbook");
    }

    /// <inheritdoc />
    public async Task<PlaybookResponse?> GetPlaybookAsync(
        Guid playbookId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        // NOTE: OutputTypeId field removed - output types are N:N relationship, not lookup
        var select = "sprk_analysisplaybookid,sprk_name,sprk_description,sprk_ispublic,sprk_istemplate,_ownerid_value,createdon,modifiedon";
        var url = $"{EntitySetName}({playbookId})?$select={select}";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<PlaybookEntity>(JsonOptions, cancellationToken);
        if (entity == null) return null;

        // Load N:N relationships
        var actionIds = await GetRelatedIdsAsync(playbookId, ActionRelationship, "sprk_analysisactions", "sprk_analysisactionid", cancellationToken);
        var skillIds = await GetRelatedIdsAsync(playbookId, SkillRelationship, "sprk_analysisskills", "sprk_analysisskillid", cancellationToken);
        var knowledgeIds = await GetRelatedIdsAsync(playbookId, KnowledgeRelationship, "sprk_analysisknowledges", "sprk_analysisknowledgeid", cancellationToken);
        var toolIds = await GetRelatedIdsAsync(playbookId, ToolRelationship, "sprk_analysistools", "sprk_analysistoolid", cancellationToken);

        return new PlaybookResponse
        {
            Id = entity.Id,
            Name = entity.Name ?? string.Empty,
            Description = entity.Description,
            OutputTypeId = entity.OutputTypeId,
            IsPublic = entity.IsPublic ?? false,
            IsTemplate = entity.IsTemplate ?? false,
            OwnerId = entity.OwnerId ?? Guid.Empty,
            ActionIds = actionIds,
            SkillIds = skillIds,
            KnowledgeIds = knowledgeIds,
            ToolIds = toolIds,
            CreatedOn = entity.CreatedOn ?? DateTime.UtcNow,
            ModifiedOn = entity.ModifiedOn ?? DateTime.UtcNow
        };
    }

    /// <inheritdoc />
    public async Task<bool> UserHasAccessAsync(
        Guid playbookId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"{EntitySetName}({playbookId})?$select=sprk_ispublic,_ownerid_value";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<PlaybookEntity>(JsonOptions, cancellationToken);
        if (entity == null) return false;

        // User has access if they own the playbook or it's public
        return entity.OwnerId == userId || (entity.IsPublic ?? false);
    }

    /// <inheritdoc />
    public Task<PlaybookValidationResult> ValidateAsync(
        SavePlaybookRequest request,
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

        if (request.Description?.Length > 4000)
        {
            errors.Add("Description cannot exceed 4000 characters");
        }

        // Validate that at least one action or tool is specified
        var hasActions = request.ActionIds?.Length > 0;
        var hasTools = request.ToolIds?.Length > 0;
        if (!hasActions && !hasTools)
        {
            errors.Add("At least one action or tool must be specified");
        }

        var result = errors.Count == 0
            ? PlaybookValidationResult.Success()
            : PlaybookValidationResult.Failure(errors.ToArray());

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public async Task<PlaybookListResponse> ListUserPlaybooksAsync(
        Guid userId,
        PlaybookQueryParameters query,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var pageSize = query.GetNormalizedPageSize();
        var skip = query.GetSkip();

        // Build OData filter for user-owned playbooks
        var filter = $"_ownerid_value eq {userId}";
        if (!string.IsNullOrWhiteSpace(query.NameFilter))
        {
            filter += $" and contains(sprk_name, '{EscapeODataString(query.NameFilter)}')";
        }
        // NOTE: OutputTypeId filter removed - output types are N:N relationship, not lookup
        // Filtering by output type would require a separate N:N query
        if (query.OutputTypeId.HasValue)
        {
            _logger.LogWarning("OutputTypeId filtering not supported - output types use N:N relationship");
        }

        return await ExecuteListQueryAsync(filter, query, pageSize, skip, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PlaybookListResponse> ListPublicPlaybooksAsync(
        PlaybookQueryParameters query,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var pageSize = query.GetNormalizedPageSize();
        var skip = query.GetSkip();

        // Build OData filter for public playbooks
        var filter = "sprk_ispublic eq true";
        if (!string.IsNullOrWhiteSpace(query.NameFilter))
        {
            filter += $" and contains(sprk_name, '{EscapeODataString(query.NameFilter)}')";
        }
        // NOTE: OutputTypeId filter removed - output types are N:N relationship, not lookup
        // Filtering by output type would require a separate N:N query
        if (query.OutputTypeId.HasValue)
        {
            _logger.LogWarning("OutputTypeId filtering not supported - output types use N:N relationship");
        }

        return await ExecuteListQueryAsync(filter, query, pageSize, skip, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PlaybookResponse> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Cache key following SDAP naming convention
        var cacheKey = $"sdap:playbook:name:{name}";

        // Try cache first (if available)
        if (_cache != null)
        {
            var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (cached != null)
            {
                try
                {
                    var cachedPlaybook = JsonSerializer.Deserialize<PlaybookResponse>(cached, JsonOptions);
                    if (cachedPlaybook != null)
                    {
                        _logger.LogDebug("[PLAYBOOK] Cache HIT for playbook name '{Name}'", name);
                        return cachedPlaybook;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "[PLAYBOOK] Cache deserialization failed for '{Name}'", name);
                }
            }
        }

        _logger.LogDebug("[PLAYBOOK] Cache MISS for playbook name '{Name}', querying Dataverse", name);

        await EnsureAuthenticatedAsync(cancellationToken);

        // Query by name - exact match, case-insensitive per Dataverse default
        // NOTE: OutputTypeId field removed - output types are N:N relationship, not lookup
        var select = "sprk_analysisplaybookid,sprk_name,sprk_description,sprk_ispublic,sprk_istemplate,_ownerid_value,createdon,modifiedon";
        var filter = $"sprk_name eq '{EscapeODataString(name)}'";
        var url = $"{EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";

        _logger.LogInformation("[PLAYBOOK] Querying Dataverse for playbook: {Name}", name);
        _logger.LogInformation("[PLAYBOOK] BaseAddress: {BaseAddress}", _httpClient.BaseAddress);
        _logger.LogInformation("[PLAYBOOK] Relative URL: {RelativeUrl}", url);
        _logger.LogInformation("[PLAYBOOK] Full URL will be: {FullUrl}", new Uri(_httpClient.BaseAddress!, url));

        var response = await _httpClient.GetAsync(url, cancellationToken);

        _logger.LogInformation("[PLAYBOOK] Query response: StatusCode={StatusCode}, ReasonPhrase={ReasonPhrase}",
            response.StatusCode, response.ReasonPhrase);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(JsonOptions, cancellationToken);
        if (result?.Value == null || result.Value.Length == 0)
        {
            _logger.LogWarning("[PLAYBOOK] Playbook '{Name}' not found in Dataverse", name);
            throw PlaybookNotFoundException.ByName(name);
        }

        var entity = JsonSerializer.Deserialize<PlaybookEntity>(result.Value[0].GetRawText(), JsonOptions);
        if (entity == null)
        {
            throw PlaybookNotFoundException.ByName(name);
        }

        // Load N:N relationships
        var playbookId = entity.Id;
        _logger.LogInformation("[PLAYBOOK] Loading N:N relationships for playbook ID: {PlaybookId}", playbookId);

        var actionIds = await GetRelatedIdsAsync(playbookId, ActionRelationship, "sprk_analysisactions", "sprk_analysisactionid", cancellationToken);
        _logger.LogInformation("[PLAYBOOK] Loaded {Count} actions", actionIds.Length);

        var skillIds = await GetRelatedIdsAsync(playbookId, SkillRelationship, "sprk_analysisskills", "sprk_analysisskillid", cancellationToken);
        _logger.LogInformation("[PLAYBOOK] Loaded {Count} skills", skillIds.Length);

        var knowledgeIds = await GetRelatedIdsAsync(playbookId, KnowledgeRelationship, "sprk_analysisknowledges", "sprk_analysisknowledgeid", cancellationToken);
        _logger.LogInformation("[PLAYBOOK] Loaded {Count} knowledge sources", knowledgeIds.Length);

        var toolIds = await GetRelatedIdsAsync(playbookId, ToolRelationship, "sprk_analysistools", "sprk_analysistoolid", cancellationToken);
        _logger.LogInformation("[PLAYBOOK] Loaded {Count} tools", toolIds.Length);

        var playbook = new PlaybookResponse
        {
            Id = entity.Id,
            Name = entity.Name ?? string.Empty,
            Description = entity.Description,
            OutputTypeId = entity.OutputTypeId,
            IsPublic = entity.IsPublic ?? false,
            IsTemplate = entity.IsTemplate ?? false,
            OwnerId = entity.OwnerId ?? Guid.Empty,
            ActionIds = actionIds,
            SkillIds = skillIds,
            KnowledgeIds = knowledgeIds,
            ToolIds = toolIds,
            CreatedOn = entity.CreatedOn ?? DateTime.UtcNow,
            ModifiedOn = entity.ModifiedOn ?? DateTime.UtcNow
        };

        // Cache result for 24 hours (playbooks are relatively stable)
        if (_cache != null)
        {
            try
            {
                var serialized = JsonSerializer.Serialize(playbook, JsonOptions);
                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                };
                await _cache.SetStringAsync(cacheKey, serialized, cacheOptions, cancellationToken);
                _logger.LogDebug("[PLAYBOOK] Cached playbook '{Name}' for 24 hours", name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PLAYBOOK] Failed to cache playbook '{Name}'", name);
            }
        }

        _logger.LogInformation("[PLAYBOOK] Retrieved playbook '{Name}' (ID: {Id})", name, playbook.Id);
        return playbook;
    }

    private async Task<PlaybookListResponse> ExecuteListQueryAsync(
        string filter,
        PlaybookQueryParameters query,
        int pageSize,
        int skip,
        CancellationToken cancellationToken)
    {
        // Get total count first
        var countUrl = $"{EntitySetName}/$count?$filter={Uri.EscapeDataString(filter)}";
        var countResponse = await _httpClient.GetAsync(countUrl, cancellationToken);
        countResponse.EnsureSuccessStatusCode();
        var totalCount = int.Parse(await countResponse.Content.ReadAsStringAsync(cancellationToken));

        // Build order by
        var orderBy = GetOrderByClause(query.SortBy, query.SortDescending);

        // Get paginated results
        // NOTE: OutputTypeId field removed - output types are N:N relationship, not lookup
        var select = "sprk_analysisplaybookid,sprk_name,sprk_description,sprk_ispublic,sprk_istemplate,_ownerid_value,modifiedon";
        var url = $"{EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}&$top={pageSize}&$skip={skip}";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(JsonOptions, cancellationToken);
        var items = result?.Value?.Select(MapToPlaybookSummary).ToArray() ?? [];

        return new PlaybookListResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = pageSize
        };
    }

    private static PlaybookSummary MapToPlaybookSummary(JsonElement element)
    {
        return new PlaybookSummary
        {
            Id = element.TryGetProperty("sprk_analysisplaybookid", out var idProp) ? idProp.GetGuid() : Guid.Empty,
            Name = element.TryGetProperty("sprk_name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty,
            Description = element.TryGetProperty("sprk_description", out var descProp) ? descProp.GetString() : null,
            // NOTE: OutputTypeId always null - output types are N:N relationship, not lookup
            OutputTypeId = null,
            IsPublic = element.TryGetProperty("sprk_ispublic", out var publicProp) && publicProp.GetBoolean(),
            IsTemplate = element.TryGetProperty("sprk_istemplate", out var templateProp) && templateProp.GetBoolean(),
            OwnerId = element.TryGetProperty("_ownerid_value", out var ownerProp) ? ownerProp.GetGuid() : Guid.Empty,
            ModifiedOn = element.TryGetProperty("modifiedon", out var modProp) ? modProp.GetDateTime() : DateTime.UtcNow
        };
    }

    private static string GetOrderByClause(string sortBy, bool descending)
    {
        // Map friendly names to Dataverse column names
        var column = sortBy.ToLowerInvariant() switch
        {
            "name" => "sprk_name",
            "createdon" => "createdon",
            "modifiedon" => "modifiedon",
            _ => "modifiedon"
        };

        return descending ? $"{column} desc" : column;
    }

    private static string EscapeODataString(string value)
    {
        // Escape single quotes for OData filter
        return value.Replace("'", "''");
    }

    /// <inheritdoc />
    public async Task<CanvasLayoutResponse?> GetCanvasLayoutAsync(
        Guid playbookId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"{EntitySetName}({playbookId})?$select=sprk_canvaslayoutjson,modifiedon";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        CanvasLayoutDto? layout = null;
        if (root.TryGetProperty("sprk_canvaslayoutjson", out var layoutProp) &&
            layoutProp.ValueKind != JsonValueKind.Null)
        {
            var layoutJson = layoutProp.GetString();
            if (!string.IsNullOrEmpty(layoutJson))
            {
                try
                {
                    layout = JsonSerializer.Deserialize<CanvasLayoutDto>(layoutJson, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize canvas layout for playbook {PlaybookId}", playbookId);
                }
            }
        }

        DateTime? modifiedOn = null;
        if (root.TryGetProperty("modifiedon", out var modProp) &&
            modProp.ValueKind != JsonValueKind.Null)
        {
            modifiedOn = modProp.GetDateTime();
        }

        return new CanvasLayoutResponse
        {
            PlaybookId = playbookId,
            Layout = layout,
            ModifiedOn = modifiedOn
        };
    }

    /// <inheritdoc />
    public async Task<CanvasLayoutResponse> SaveCanvasLayoutAsync(
        Guid playbookId,
        CanvasLayoutDto layout,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Saving canvas layout for playbook {PlaybookId}", playbookId);

        // Serialize the layout to JSON
        var layoutJson = JsonSerializer.Serialize(layout, JsonOptions);

        // Update the playbook's canvas layout field
        var payload = new Dictionary<string, object?>
        {
            ["sprk_canvaslayoutjson"] = layoutJson
        };

        var url = $"{EntitySetName}({playbookId})";
        var response = await _httpClient.PatchAsJsonAsync(url, payload, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Saved canvas layout for playbook {PlaybookId}", playbookId);

        // Return the saved layout
        return new CanvasLayoutResponse
        {
            PlaybookId = playbookId,
            Layout = layout,
            ModifiedOn = DateTime.UtcNow
        };
    }

    /// <inheritdoc />
    public async Task<PlaybookListResponse> ListTemplatesAsync(
        PlaybookQueryParameters query,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var pageSize = query.GetNormalizedPageSize();
        var skip = query.GetSkip();

        // Build OData filter for template playbooks
        var filter = "sprk_istemplate eq true";
        if (!string.IsNullOrWhiteSpace(query.NameFilter))
        {
            filter += $" and contains(sprk_name, '{EscapeODataString(query.NameFilter)}')";
        }

        _logger.LogInformation("Listing template playbooks with filter: {Filter}", filter);

        return await ExecuteListQueryAsync(filter, query, pageSize, skip, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PlaybookResponse> ClonePlaybookAsync(
        Guid sourcePlaybookId,
        Guid userId,
        string? newName = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Cloning playbook {SourceId} for user {UserId}", sourcePlaybookId, userId);

        // Get the source playbook with all relationships
        var source = await GetPlaybookAsync(sourcePlaybookId, cancellationToken)
            ?? throw PlaybookNotFoundException.ById(sourcePlaybookId);

        // Generate name for clone
        var cloneName = newName ?? $"{source.Name} (Copy)";

        // Get canvas layout from source
        var canvasLayout = await GetCanvasLayoutAsync(sourcePlaybookId, cancellationToken);

        // Create the clone request
        var cloneRequest = new SavePlaybookRequest
        {
            Name = cloneName,
            Description = source.Description,
            OutputTypeId = source.OutputTypeId,
            IsPublic = false, // Clones are private by default
            IsTemplate = false, // Clones are not templates
            ActionIds = source.ActionIds,
            SkillIds = source.SkillIds,
            KnowledgeIds = source.KnowledgeIds,
            ToolIds = source.ToolIds
        };

        // Create the new playbook
        var clonedPlaybook = await CreatePlaybookAsync(cloneRequest, userId, cancellationToken);

        _logger.LogInformation("Created clone {CloneId} from source {SourceId}", clonedPlaybook.Id, sourcePlaybookId);

        // Copy canvas layout if present
        if (canvasLayout?.Layout != null)
        {
            await SaveCanvasLayoutAsync(clonedPlaybook.Id, canvasLayout.Layout, cancellationToken);
            _logger.LogInformation("Copied canvas layout to clone {CloneId}", clonedPlaybook.Id);
        }

        // Return the cloned playbook with updated data
        return await GetPlaybookAsync(clonedPlaybook.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve cloned playbook");
    }

    #region Private Helper Methods

    private async Task AssociateRelationshipsAsync(
        Guid playbookId,
        SavePlaybookRequest request,
        CancellationToken cancellationToken)
    {
        // Associate actions
        if (request.ActionIds?.Length > 0)
        {
            foreach (var actionId in request.ActionIds)
            {
                await AssociateAsync(playbookId, ActionRelationship, "sprk_analysisactions", actionId, cancellationToken);
            }
        }

        // Associate skills
        if (request.SkillIds?.Length > 0)
        {
            foreach (var skillId in request.SkillIds)
            {
                await AssociateAsync(playbookId, SkillRelationship, "sprk_analysisskills", skillId, cancellationToken);
            }
        }

        // Associate knowledge
        if (request.KnowledgeIds?.Length > 0)
        {
            foreach (var knowledgeId in request.KnowledgeIds)
            {
                await AssociateAsync(playbookId, KnowledgeRelationship, "sprk_analysisknowledges", knowledgeId, cancellationToken);
            }
        }

        // Associate tools
        if (request.ToolIds?.Length > 0)
        {
            foreach (var toolId in request.ToolIds)
            {
                await AssociateAsync(playbookId, ToolRelationship, "sprk_analysistools", toolId, cancellationToken);
            }
        }
    }

    private async Task AssociateAsync(
        Guid playbookId,
        string relationshipName,
        string targetEntitySet,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        var url = $"{EntitySetName}({playbookId})/{relationshipName}/$ref";
        var payload = new Dictionary<string, string>
        {
            ["@odata.id"] = $"{_apiUrl}/{targetEntitySet}({targetId})"
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();
            _logger.LogDebug("Associated {Relationship}: {PlaybookId} -> {TargetId}", relationshipName, playbookId, targetId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // Association may already exist - log and continue
            _logger.LogWarning("Association may already exist: {Relationship} {PlaybookId} -> {TargetId}", relationshipName, playbookId, targetId);
        }
    }

    private async Task ClearRelationshipsAsync(Guid playbookId, CancellationToken cancellationToken)
    {
        // Clear all N:N relationships for the playbook
        await ClearRelationshipAsync(playbookId, ActionRelationship, "sprk_analysisactions", "sprk_analysisactionid", cancellationToken);
        await ClearRelationshipAsync(playbookId, SkillRelationship, "sprk_analysisskills", "sprk_analysisskillid", cancellationToken);
        await ClearRelationshipAsync(playbookId, KnowledgeRelationship, "sprk_analysisknowledges", "sprk_analysisknowledgeid", cancellationToken);
        await ClearRelationshipAsync(playbookId, ToolRelationship, "sprk_analysistools", "sprk_analysistoolid", cancellationToken);
    }

    private async Task ClearRelationshipAsync(
        Guid playbookId,
        string relationshipName,
        string targetEntitySet,
        string targetIdField,
        CancellationToken cancellationToken)
    {
        // Get existing related IDs
        var existingIds = await GetRelatedIdsAsync(playbookId, relationshipName, targetEntitySet, targetIdField, cancellationToken);

        // Disassociate each
        foreach (var targetId in existingIds)
        {
            var url = $"{EntitySetName}({playbookId})/{relationshipName}({targetId})/$ref";
            try
            {
                var response = await _httpClient.DeleteAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to disassociate {Relationship}: {PlaybookId} -> {TargetId}", relationshipName, playbookId, targetId);
            }
        }
    }

    private async Task<Guid[]> GetRelatedIdsAsync(
        Guid playbookId,
        string relationshipName,
        string targetEntitySet,
        string targetIdField,
        CancellationToken cancellationToken)
    {
        var url = $"{EntitySetName}({playbookId})/{relationshipName}?$select={targetIdField}";

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

    private class PlaybookEntity
    {
        [JsonPropertyName("sprk_analysisplaybookid")]
        public Guid Id { get; set; }

        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }

        [JsonPropertyName("sprk_description")]
        public string? Description { get; set; }

        [JsonPropertyName("sprk_ispublic")]
        public bool? IsPublic { get; set; }

        [JsonPropertyName("sprk_istemplate")]
        public bool? IsTemplate { get; set; }

        [JsonPropertyName("_sprk_outputtypeid_value")]
        public Guid? OutputTypeId { get; set; }

        [JsonPropertyName("_ownerid_value")]
        public Guid? OwnerId { get; set; }

        [JsonPropertyName("createdon")]
        public DateTime? CreatedOn { get; set; }

        [JsonPropertyName("modifiedon")]
        public DateTime? ModifiedOn { get; set; }
    }

    private class ODataCollectionResponse
    {
        [JsonPropertyName("value")]
        public JsonElement[]? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    #endregion
}
