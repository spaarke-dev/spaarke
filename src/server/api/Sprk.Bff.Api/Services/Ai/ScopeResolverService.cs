using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Resolves analysis scopes from Dataverse entities.
/// Loads Skills, Knowledge, Tools by ID or from Playbook configuration.
/// </summary>
public class ScopeResolverService : IScopeResolverService
{
    private readonly IDataverseService _dataverseService;
    private readonly IPlaybookService _playbookService;
    private readonly ILogger<ScopeResolverService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly TokenCredential _credential;
    private AccessToken? _currentToken;

    public ScopeResolverService(
        IDataverseService dataverseService,
        IPlaybookService playbookService,
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ScopeResolverService> logger)
    {
        _dataverseService = dataverseService;
        _playbookService = playbookService;
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

            _logger.LogDebug("Refreshed Dataverse access token for ScopeResolverService");
        }
    }

    /// <inheritdoc />
    public Task<ResolvedScopes> ResolveScopesAsync(
        Guid[] skillIds,
        Guid[] knowledgeIds,
        Guid[] toolIds,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Resolving scopes: {SkillCount} skills, {KnowledgeCount} knowledge, {ToolCount} tools",
            skillIds.Length, knowledgeIds.Length, toolIds.Length);

        // Phase 1: Return empty scopes (actual resolution in Task 032)
        _logger.LogInformation("Phase 1: Returning empty scopes (Dataverse integration in Task 032)");

        return Task.FromResult(new ResolvedScopes([], [], []));
    }

    /// <inheritdoc />
    public async Task<ResolvedScopes> ResolvePlaybookScopesAsync(
        Guid playbookId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Resolving scopes from playbook {PlaybookId}", playbookId);

        try
        {
            // Load playbook to get N:N relationship IDs
            // PlaybookService queries sprk_playbook_tool N:N relationship
            var playbook = await _playbookService.GetPlaybookAsync(playbookId, cancellationToken);

            if (playbook == null)
            {
                _logger.LogWarning("Playbook {PlaybookId} not found", playbookId);
                return new ResolvedScopes([], [], []);
            }

            _logger.LogDebug(
                "Playbook '{PlaybookName}' has {ToolCount} tools, {SkillCount} skills, {KnowledgeCount} knowledge",
                playbook.Name, playbook.ToolIds.Length, playbook.SkillIds.Length, playbook.KnowledgeIds.Length);

            if (playbook.ToolIds.Length == 0)
            {
                _logger.LogWarning(
                    "Playbook '{PlaybookName}' (ID: {PlaybookId}) has no tools configured in N:N relationship",
                    playbook.Name, playbookId);
                return new ResolvedScopes([], [], []);
            }

            // Load full AnalysisTool entities for each tool ID
            _logger.LogInformation(
                "[RESOLVE SCOPES] Loading full tool entities for {Count} tool IDs: {ToolIds}",
                playbook.ToolIds.Length,
                string.Join(", ", playbook.ToolIds));

            var toolTasks = playbook.ToolIds.Select(toolId => GetToolAsync(toolId, cancellationToken));
            var toolResults = await Task.WhenAll(toolTasks);

            _logger.LogInformation(
                "[RESOLVE SCOPES] GetToolAsync returned {TotalCount} results, {NullCount} were null",
                toolResults.Length,
                toolResults.Count(t => t == null));

            var tools = toolResults
                .Where(t => t != null)
                .Cast<AnalysisTool>()
                .ToArray();

            _logger.LogInformation(
                "[RESOLVE SCOPES] After filtering nulls, {Count} valid tools remain",
                tools.Length);

            _logger.LogDebug(
                "Resolved {ToolCount} tools from playbook '{PlaybookName}' (ID: {PlaybookId}): {ToolNames}",
                tools.Length,
                playbook.Name,
                playbookId,
                string.Join(", ", tools.Select(t => t.Name)));

            // Return scopes with tools (Skills and Knowledge empty for now)
            // TODO: Load skills and knowledge when needed (FR-14, FR-15)
            return new ResolvedScopes([], [], tools);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to resolve scopes from playbook {PlaybookId}",
                playbookId);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<ResolvedScopes> ResolveNodeScopesAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Resolving scopes from node {NodeId}", nodeId);

        // Phase 1: Node scope resolution not yet implemented
        // In Task 032, this will:
        // 1. Query sprk_playbooknode_skill N:N relationship for skills
        // 2. Query sprk_playbooknode_knowledge N:N relationship for knowledge
        // 3. Query sprk_toolid lookup for the single tool
        _logger.LogWarning("Node scope resolution not yet implemented, returning empty scopes");

        return Task.FromResult(new ResolvedScopes([], [], []));
    }

    /// <inheritdoc />
    public async Task<AnalysisAction?> GetActionAsync(
        Guid actionId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[GET ACTION] Loading action {ActionId} from Dataverse", actionId);

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysisactions({actionId})?$expand=sprk_ActionTypeId($select=sprk_name)";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("[GET ACTION] Action {ActionId} not found in Dataverse", actionId);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<ActionEntity>(cancellationToken);
        if (entity == null)
        {
            _logger.LogWarning("[GET ACTION] Failed to deserialize action {ActionId}", actionId);
            return null;
        }

        // Extract SortOrder from type name prefix (e.g., "01 - Extraction" → 1)
        var sortOrder = ExtractSortOrderFromTypeName(entity.ActionTypeId?.Name);

        var action = new AnalysisAction
        {
            Id = entity.Id,
            Name = entity.Name ?? "Unnamed Action",
            Description = entity.Description,
            SystemPrompt = entity.SystemPrompt ?? "You are an AI assistant that analyzes documents.",
            SortOrder = sortOrder
        };

        _logger.LogInformation("[GET ACTION] Loaded action from Dataverse: {ActionName} (SortOrder: {SortOrder})",
            action.Name, action.SortOrder);

        return action;
    }

    private static int ExtractSortOrderFromTypeName(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return 0;

        // Pattern: "01 - Extraction" → extract "01" → parse to int 1
        var match = System.Text.RegularExpressions.Regex.Match(typeName, @"^(\d+)\s*-");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var sortOrder))
        {
            return sortOrder;
        }

        return 0;
    }

    /// <inheritdoc />
    public async Task<ScopeListResult<AnalysisSkill>> ListSkillsAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[LIST SKILLS] Querying Dataverse: Page={Page}, PageSize={PageSize}, NameFilter={NameFilter}, CategoryFilter={CategoryFilter}",
            options.Page, options.PageSize, options.NameFilter, options.CategoryFilter);

        await EnsureAuthenticatedAsync(cancellationToken);

        var sortMappings = new Dictionary<string, string>
        {
            ["name"] = "sprk_name",
            ["category"] = "sprk_name" // Sort by name when category requested (category is in related entity)
        };

        var query = BuildODataQuery(
            options,
            selectFields: "sprk_analysisskillid,sprk_name,sprk_description,sprk_promptfragment",
            expandClause: "sprk_SkillTypeId($select=sprk_name)",
            nameFieldPath: "sprk_name",
            categoryFieldPath: null, // Category filter needs special handling for lookup
            sortFieldMappings: sortMappings);

        var url = $"sprk_analysisskills?{query}";
        _logger.LogDebug("[LIST SKILLS] Query URL: {Url}", url);

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<SkillEntity>>(cancellationToken);
        if (result == null)
        {
            _logger.LogWarning("[LIST SKILLS] Failed to deserialize response");
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

        _logger.LogInformation("[LIST SKILLS] Retrieved {Count} skills from Dataverse (Total: {TotalCount})",
            skillArray.Length, result.ODataCount ?? skillArray.Length);

        return new ScopeListResult<AnalysisSkill>
        {
            Items = skillArray,
            TotalCount = result.ODataCount ?? skillArray.Length,
            Page = options.Page,
            PageSize = options.PageSize
        };
    }

    /// <inheritdoc />
    public async Task<ScopeListResult<AnalysisKnowledge>> ListKnowledgeAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[LIST KNOWLEDGE] Querying Dataverse: Page={Page}, PageSize={PageSize}, NameFilter={NameFilter}",
            options.Page, options.PageSize, options.NameFilter);

        await EnsureAuthenticatedAsync(cancellationToken);

        var sortMappings = new Dictionary<string, string>
        {
            ["name"] = "sprk_name",
            ["type"] = "sprk_name" // Sort by name when type requested (type is in related entity)
        };

        var query = BuildODataQuery(
            options,
            selectFields: "sprk_analysisknowledgeid,sprk_name,sprk_description,sprk_content,sprk_deploymentid",
            expandClause: "sprk_KnowledgeTypeId($select=sprk_name)",
            nameFieldPath: "sprk_name",
            categoryFieldPath: null, // Type filter needs special handling for lookup
            sortFieldMappings: sortMappings);

        var url = $"sprk_analysisknowledges?{query}";
        _logger.LogDebug("[LIST KNOWLEDGE] Query URL: {Url}", url);

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<KnowledgeEntity>>(cancellationToken);
        if (result == null)
        {
            _logger.LogWarning("[LIST KNOWLEDGE] Failed to deserialize response");
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
            var knowledgeType = MapKnowledgeTypeName(entity.KnowledgeTypeId?.Name);
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

        _logger.LogInformation("[LIST KNOWLEDGE] Retrieved {Count} knowledge items from Dataverse (Total: {TotalCount})",
            knowledgeItems.Length, result.ODataCount ?? knowledgeItems.Length);

        return new ScopeListResult<AnalysisKnowledge>
        {
            Items = knowledgeItems,
            TotalCount = result.ODataCount ?? knowledgeItems.Length,
            Page = options.Page,
            PageSize = options.PageSize
        };
    }

    /// <inheritdoc />
    public async Task<ScopeListResult<AnalysisTool>> ListToolsAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[LIST TOOLS] Querying Dataverse: Page={Page}, PageSize={PageSize}, NameFilter={NameFilter}",
            options.Page, options.PageSize, options.NameFilter);

        await EnsureAuthenticatedAsync(cancellationToken);

        var sortMappings = new Dictionary<string, string>
        {
            ["name"] = "sprk_name",
            ["type"] = "sprk_name" // Sort by name when type requested (type is in related entity)
        };

        var query = BuildODataQuery(
            options,
            selectFields: "sprk_analysistoolid,sprk_name,sprk_description,sprk_handlerclass,sprk_configuration",
            expandClause: "sprk_ToolTypeId($select=sprk_name)",
            nameFieldPath: "sprk_name",
            categoryFieldPath: null, // Type filter needs special handling for lookup
            sortFieldMappings: sortMappings);

        var url = $"sprk_analysistools?{query}";
        _logger.LogDebug("[LIST TOOLS] Query URL: {Url}", url);

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<ToolEntity>>(cancellationToken);
        if (result == null)
        {
            _logger.LogWarning("[LIST TOOLS] Failed to deserialize response");
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

        _logger.LogInformation("[LIST TOOLS] Retrieved {Count} tools from Dataverse (Total: {TotalCount})",
            tools.Length, result.ODataCount ?? tools.Length);

        return new ScopeListResult<AnalysisTool>
        {
            Items = tools,
            TotalCount = result.ODataCount ?? tools.Length,
            Page = options.Page,
            PageSize = options.PageSize
        };
    }

    /// <inheritdoc />
    public async Task<ScopeListResult<AnalysisAction>> ListActionsAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[LIST ACTIONS] Querying Dataverse: Page={Page}, PageSize={PageSize}, NameFilter={NameFilter}",
            options.Page, options.PageSize, options.NameFilter);

        await EnsureAuthenticatedAsync(cancellationToken);

        var sortMappings = new Dictionary<string, string>
        {
            ["name"] = "sprk_name",
            ["sortorder"] = "sprk_name" // Actions don't have a sort order field in Dataverse, use name
        };

        var query = BuildODataQuery(
            options,
            selectFields: "sprk_analysisactionid,sprk_name,sprk_description,sprk_systemprompt",
            expandClause: "sprk_ActionTypeId($select=sprk_name)",
            nameFieldPath: "sprk_name",
            categoryFieldPath: null, // Actions don't have a category field
            sortFieldMappings: sortMappings);

        var url = $"sprk_analysisactions?{query}";
        _logger.LogDebug("[LIST ACTIONS] Query URL: {Url}", url);

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<ActionEntity>>(cancellationToken);
        if (result == null)
        {
            _logger.LogWarning("[LIST ACTIONS] Failed to deserialize response");
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

        _logger.LogInformation("[LIST ACTIONS] Retrieved {Count} actions from Dataverse (Total: {TotalCount})",
            actions.Length, result.ODataCount ?? actions.Length);

        return new ScopeListResult<AnalysisAction>
        {
            Items = actions,
            TotalCount = result.ODataCount ?? actions.Length,
            Page = options.Page,
            PageSize = options.PageSize
        };
    }

    #region CRUD Operations (Task 002 Implementation)

    // Prefix constants for scope ownership
    private const string SystemPrefix = "SYS-";
    private const string CustomerPrefix = "CUST-";

    #region Private Helper Methods

    /// <summary>
    /// Validates that a scope is not system-owned (immutable).
    /// Throws ScopeOwnershipException if the scope has SYS- prefix or is marked immutable.
    /// </summary>
    /// <param name="name">Scope name to check.</param>
    /// <param name="isImmutable">Immutable flag from the record.</param>
    /// <param name="operation">Operation being attempted (for error message).</param>
    /// <exception cref="ScopeOwnershipException">Thrown if scope is system-owned.</exception>
    private void ValidateOwnership(string name, bool isImmutable, string operation)
    {
        if (isImmutable || name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Attempted to {Operation} system scope '{ScopeName}'", operation, name);
            throw new ScopeOwnershipException(name, operation);
        }
    }

    /// <summary>
    /// Determines if a name represents a system-owned scope.
    /// </summary>
    private static bool IsSystemScope(string name)
        => name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ensures a customer scope has the CUST- prefix.
    /// If the name already has SYS- or CUST- prefix, returns as-is.
    /// </summary>
    private static string EnsureCustomerPrefix(string name)
    {
        if (name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(CustomerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return $"{CustomerPrefix}{name}";
    }

    #endregion

    #region Action CRUD

    /// <inheritdoc />
    public async Task<AnalysisAction> CreateActionAsync(CreateActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request.Name));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SystemPrompt, nameof(request.SystemPrompt));

        // Ensure customer prefix for new actions
        var name = EnsureCustomerPrefix(request.Name);

        _logger.LogInformation("[CREATE ACTION] Creating action '{ActionName}' in Dataverse", name);

        await EnsureAuthenticatedAsync(cancellationToken);

        // Build payload for Dataverse
        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = name,
            ["sprk_description"] = request.Description,
            ["sprk_systemprompt"] = request.SystemPrompt
        };

        // Note: ActionTypeId lookup binding can be added when the property is added to CreateActionRequest
        // For now, the type is set via Dataverse default or subsequent update

        // POST with Prefer: return=representation to get created entity
        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "sprk_analysisactions")
        {
            Content = httpContent
        };
        postRequest.Headers.Add("Prefer", "return=representation");

        var response = await _httpClient.SendAsync(postRequest, cancellationToken);
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

        _logger.LogInformation("[CREATE ACTION] Created action '{ActionName}' with ID {ActionId}", action.Name, action.Id);

        return action;
    }

    /// <inheritdoc />
    public async Task<AnalysisAction> UpdateActionAsync(Guid id, UpdateActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation("[UPDATE ACTION] Updating action {ActionId} in Dataverse", id);

        // Load existing action to validate it exists and check ownership
        var existing = await GetActionAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("[UPDATE ACTION] Action {ActionId} not found for update", id);
            throw new KeyNotFoundException($"Action with ID '{id}' not found.");
        }

        // Validate ownership - reject if system-owned
        ValidateOwnership(existing.Name, existing.IsImmutable, "update");

        await EnsureAuthenticatedAsync(cancellationToken);

        // Build PATCH payload with only non-null fields (sparse update)
        var payload = new Dictionary<string, object?>();

        if (request.Name != null)
            payload["sprk_name"] = EnsureCustomerPrefix(request.Name);
        if (request.Description != null)
            payload["sprk_description"] = request.Description;
        if (request.SystemPrompt != null)
            payload["sprk_systemprompt"] = request.SystemPrompt;

        // PATCH to Dataverse
        var url = $"sprk_analysisactions({id})";
        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = httpContent
        };

        var response = await _httpClient.SendAsync(patchRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Re-fetch to get the updated entity
        var updated = await GetActionAsync(id, cancellationToken);
        if (updated == null)
        {
            throw new InvalidOperationException($"Failed to retrieve updated action {id} from Dataverse");
        }

        _logger.LogInformation("[UPDATE ACTION] Updated action '{ActionName}' (ID: {ActionId})", updated.Name, id);

        return updated;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteActionAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[DELETE ACTION] Deleting action {ActionId} from Dataverse", id);

        // Load existing action to validate ownership
        var existing = await GetActionAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("[DELETE ACTION] Action {ActionId} not found for deletion", id);
            return false;
        }

        // Validate ownership - reject if system-owned
        ValidateOwnership(existing.Name, existing.IsImmutable, "delete");

        await EnsureAuthenticatedAsync(cancellationToken);

        // DELETE from Dataverse
        var url = $"sprk_analysisactions({id})";
        var response = await _httpClient.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("[DELETE ACTION] Action {ActionId} not found in Dataverse", id);
            return false;
        }

        response.EnsureSuccessStatusCode();

        _logger.LogInformation("[DELETE ACTION] Deleted action '{ActionName}' (ID: {ActionId})", existing.Name, id);
        return true;
    }

    #endregion

    #region Skill CRUD

    /// <inheritdoc />
    public async Task<AnalysisSkill?> GetSkillAsync(Guid skillId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[GET SKILL] Loading skill {SkillId} from Dataverse", skillId);

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysisskills({skillId})?$expand=sprk_SkillTypeId($select=sprk_name)";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("[GET SKILL] Skill {SkillId} not found in Dataverse", skillId);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<SkillEntity>(cancellationToken);
        if (entity == null)
        {
            _logger.LogWarning("[GET SKILL] Failed to deserialize skill {SkillId}", skillId);
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

        _logger.LogInformation("[GET SKILL] Loaded skill from Dataverse: {SkillName} (Category: {Category})",
            skill.Name, skill.Category);

        return skill;
    }

    /// <inheritdoc />
    public async Task<AnalysisSkill> CreateSkillAsync(CreateSkillRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request.Name));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PromptFragment, nameof(request.PromptFragment));

        var name = EnsureCustomerPrefix(request.Name);

        _logger.LogInformation("[CREATE SKILL] Creating skill '{SkillName}' in Dataverse", name);

        await EnsureAuthenticatedAsync(cancellationToken);

        // Build payload for Dataverse
        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = name,
            ["sprk_description"] = request.Description,
            ["sprk_promptfragment"] = request.PromptFragment
        };

        // Note: SkillTypeId lookup binding can be added when the property is added to CreateSkillRequest
        // For now, the type is set via Dataverse default or subsequent update

        // POST with Prefer: return=representation to get created entity
        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "sprk_analysisskills")
        {
            Content = httpContent
        };
        postRequest.Headers.Add("Prefer", "return=representation");

        var response = await _httpClient.SendAsync(postRequest, cancellationToken);
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

        _logger.LogInformation("[CREATE SKILL] Created skill '{SkillName}' with ID {SkillId}", skill.Name, skill.Id);

        return skill;
    }

    /// <inheritdoc />
    public async Task<AnalysisSkill> UpdateSkillAsync(Guid id, UpdateSkillRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation("[UPDATE SKILL] Updating skill {SkillId} in Dataverse", id);

        var existing = await GetSkillAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("[UPDATE SKILL] Skill {SkillId} not found for update", id);
            throw new KeyNotFoundException($"Skill with ID '{id}' not found.");
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "update");

        await EnsureAuthenticatedAsync(cancellationToken);

        // Build PATCH payload with only non-null fields (sparse update)
        var payload = new Dictionary<string, object?>();

        if (request.Name != null)
            payload["sprk_name"] = EnsureCustomerPrefix(request.Name);
        if (request.Description != null)
            payload["sprk_description"] = request.Description;
        if (request.PromptFragment != null)
            payload["sprk_promptfragment"] = request.PromptFragment;

        // PATCH to Dataverse
        var url = $"sprk_analysisskills({id})";
        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = httpContent
        };

        var response = await _httpClient.SendAsync(patchRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Re-fetch to get the updated entity
        var updated = await GetSkillAsync(id, cancellationToken);
        if (updated == null)
        {
            throw new InvalidOperationException($"Failed to retrieve updated skill {id} from Dataverse");
        }

        _logger.LogInformation("[UPDATE SKILL] Updated skill '{SkillName}' (ID: {SkillId})", updated.Name, id);

        return updated;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteSkillAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[DELETE SKILL] Deleting skill {SkillId} from Dataverse", id);

        var existing = await GetSkillAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("[DELETE SKILL] Skill {SkillId} not found for deletion", id);
            return false;
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "delete");

        await EnsureAuthenticatedAsync(cancellationToken);

        // DELETE from Dataverse
        var url = $"sprk_analysisskills({id})";
        var response = await _httpClient.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("[DELETE SKILL] Skill {SkillId} not found in Dataverse", id);
            return false;
        }

        response.EnsureSuccessStatusCode();

        _logger.LogInformation("[DELETE SKILL] Deleted skill '{SkillName}' (ID: {SkillId})", existing.Name, id);
        return true;
    }

    #endregion

    #region Knowledge CRUD

    /// <inheritdoc />
    public async Task<AnalysisKnowledge?> GetKnowledgeAsync(Guid knowledgeId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[GET KNOWLEDGE] Loading knowledge {KnowledgeId} from Dataverse", knowledgeId);

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysisknowledges({knowledgeId})?$expand=sprk_KnowledgeTypeId($select=sprk_name)";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("[GET KNOWLEDGE] Knowledge {KnowledgeId} not found in Dataverse", knowledgeId);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<KnowledgeEntity>(cancellationToken);
        if (entity == null)
        {
            _logger.LogWarning("[GET KNOWLEDGE] Failed to deserialize knowledge {KnowledgeId}", knowledgeId);
            return null;
        }

        var knowledgeType = MapKnowledgeTypeName(entity.KnowledgeTypeId?.Name);

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

        _logger.LogInformation("[GET KNOWLEDGE] Loaded knowledge from Dataverse: {KnowledgeName} (Type: {KnowledgeType})",
            knowledge.Name, knowledge.Type);

        return knowledge;
    }

    private static KnowledgeType MapKnowledgeTypeName(string? typeName)
    {
        return typeName?.ToLowerInvariant() switch
        {
            "standards" => KnowledgeType.Inline,
            "regulations" => KnowledgeType.RagIndex,
            "rag" => KnowledgeType.RagIndex,
            "document" => KnowledgeType.Document,
            _ => KnowledgeType.Inline
        };
    }

    /// <inheritdoc />
    public async Task<AnalysisKnowledge> CreateKnowledgeAsync(CreateKnowledgeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request.Name));

        var name = EnsureCustomerPrefix(request.Name);

        _logger.LogInformation("[CREATE KNOWLEDGE] Creating knowledge '{KnowledgeName}' in Dataverse", name);

        await EnsureAuthenticatedAsync(cancellationToken);

        // Build payload for Dataverse
        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = name,
            ["sprk_description"] = request.Description,
            ["sprk_content"] = request.Content
        };

        // Add deployment ID if specified (for RAG index references)
        if (request.DeploymentId.HasValue)
        {
            payload["sprk_deploymentid"] = request.DeploymentId.Value.ToString();
        }

        // Note: KnowledgeTypeId lookup binding can be added when the property is added to CreateKnowledgeRequest
        // For now, the type is set via Dataverse default or subsequent update

        // POST with Prefer: return=representation to get created entity
        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "sprk_analysisknowledges")
        {
            Content = httpContent
        };
        postRequest.Headers.Add("Prefer", "return=representation");

        var response = await _httpClient.SendAsync(postRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<KnowledgeEntity>(cancellationToken);
        if (entity == null)
        {
            throw new InvalidOperationException("Failed to deserialize created knowledge from Dataverse response");
        }

        // Use type from entity if available, otherwise fall back to request type
        var knowledgeType = entity.KnowledgeTypeId?.Name != null
            ? MapKnowledgeTypeName(entity.KnowledgeTypeId.Name)
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

        _logger.LogInformation("[CREATE KNOWLEDGE] Created knowledge '{KnowledgeName}' with ID {KnowledgeId}", knowledge.Name, knowledge.Id);

        return knowledge;
    }

    /// <inheritdoc />
    public async Task<AnalysisKnowledge> UpdateKnowledgeAsync(Guid id, UpdateKnowledgeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation("[UPDATE KNOWLEDGE] Updating knowledge {KnowledgeId} in Dataverse", id);

        var existing = await GetKnowledgeAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("[UPDATE KNOWLEDGE] Knowledge {KnowledgeId} not found for update", id);
            throw new KeyNotFoundException($"Knowledge with ID '{id}' not found.");
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "update");

        await EnsureAuthenticatedAsync(cancellationToken);

        // Build PATCH payload with only non-null fields (sparse update)
        var payload = new Dictionary<string, object?>();

        if (request.Name != null)
            payload["sprk_name"] = EnsureCustomerPrefix(request.Name);
        if (request.Description != null)
            payload["sprk_description"] = request.Description;
        if (request.Content != null)
            payload["sprk_content"] = request.Content;
        if (request.DeploymentId.HasValue)
            payload["sprk_deploymentid"] = request.DeploymentId.Value.ToString();

        // PATCH to Dataverse
        var url = $"sprk_analysisknowledges({id})";
        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = httpContent
        };

        var response = await _httpClient.SendAsync(patchRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Re-fetch to get the updated entity
        var updated = await GetKnowledgeAsync(id, cancellationToken);
        if (updated == null)
        {
            throw new InvalidOperationException($"Failed to retrieve updated knowledge {id} from Dataverse");
        }

        _logger.LogInformation("[UPDATE KNOWLEDGE] Updated knowledge '{KnowledgeName}' (ID: {KnowledgeId})", updated.Name, id);

        return updated;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteKnowledgeAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[DELETE KNOWLEDGE] Deleting knowledge {KnowledgeId} from Dataverse", id);

        var existing = await GetKnowledgeAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("[DELETE KNOWLEDGE] Knowledge {KnowledgeId} not found for deletion", id);
            return false;
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "delete");

        await EnsureAuthenticatedAsync(cancellationToken);

        // DELETE from Dataverse
        var url = $"sprk_analysisknowledges({id})";
        var response = await _httpClient.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("[DELETE KNOWLEDGE] Knowledge {KnowledgeId} not found in Dataverse", id);
            return false;
        }

        response.EnsureSuccessStatusCode();

        _logger.LogInformation("[DELETE KNOWLEDGE] Deleted knowledge '{KnowledgeName}' (ID: {KnowledgeId})", existing.Name, id);
        return true;
    }

    #endregion

    #region Tool CRUD

    /// <inheritdoc />
    public async Task<AnalysisTool?> GetToolAsync(Guid toolId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[GET TOOL] Loading tool {ToolId} from Dataverse", toolId);

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysistools({toolId})?$expand=sprk_ToolTypeId($select=sprk_name)";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("[GET TOOL] Tool {ToolId} not found in Dataverse", toolId);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<ToolEntity>(cancellationToken);
        if (entity == null)
        {
            _logger.LogWarning("[GET TOOL] Failed to deserialize tool {ToolId}", toolId);
            return null;
        }

        // Map from HandlerClass if available, otherwise fall back to type name from lookup
        var toolType = !string.IsNullOrEmpty(entity.HandlerClass)
            ? MapHandlerClassToToolType(entity.HandlerClass)
            : MapToolTypeName(entity.ToolTypeId?.Name ?? "");

        var tool = new AnalysisTool
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

        var mappingSource = !string.IsNullOrEmpty(entity.HandlerClass) ? "HandlerClass" : "TypeName";
        _logger.LogInformation("[GET TOOL] Loaded tool from Dataverse: {ToolName} (Type: {ToolType}, MappedFrom: {MappingSource}, HandlerClass: {HandlerClass})",
            tool.Name, tool.Type, mappingSource, entity.HandlerClass ?? "null");

        return tool;
    }

    private static ToolType MapHandlerClassToToolType(string handlerClass)
    {
        // Map from handler class name (e.g., "SummaryHandler", "EntityExtractorHandler") to ToolType enum
        // This is the preferred mapping method as it's deterministic and matches implementation
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
            _ => ToolType.Custom // Default to Custom for unknown handler classes
        };
    }

    private static ToolType MapToolTypeName(string typeName)
    {
        // Fallback: Map from sprk_aitooltype.sprk_name to ToolType enum
        // Type names are like "01 - Entity Extraction", "02 - Classification", etc.
        // This is a fallback if HandlerClass is not populated
        return typeName switch
        {
            string s when s.Contains("Entity Extraction", StringComparison.OrdinalIgnoreCase) => ToolType.EntityExtractor,
            string s when s.Contains("Classification", StringComparison.OrdinalIgnoreCase) => ToolType.DocumentClassifier,
            string s when s.Contains("Analysis", StringComparison.OrdinalIgnoreCase) => ToolType.Summary,
            string s when s.Contains("Calculation", StringComparison.OrdinalIgnoreCase) => ToolType.FinancialCalculator,
            _ => ToolType.Custom // Default to Custom for unknown types
        };
    }

    /// <inheritdoc />
    public async Task<AnalysisTool> CreateToolAsync(CreateToolRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request.Name));

        var name = EnsureCustomerPrefix(request.Name);

        _logger.LogInformation("[CREATE TOOL] Creating tool '{ToolName}' in Dataverse", name);

        await EnsureAuthenticatedAsync(cancellationToken);

        // Build payload for Dataverse
        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = name,
            ["sprk_description"] = request.Description,
            ["sprk_handlerclass"] = request.HandlerClass,
            ["sprk_configuration"] = request.Configuration
        };

        // Note: ToolTypeId lookup binding can be added when the property is added to CreateToolRequest
        // For now, the type is set via Dataverse default or subsequent update

        // POST with Prefer: return=representation to get created entity
        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "sprk_analysistools")
        {
            Content = httpContent
        };
        postRequest.Headers.Add("Prefer", "return=representation");

        var response = await _httpClient.SendAsync(postRequest, cancellationToken);
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

        _logger.LogInformation("[CREATE TOOL] Created tool '{ToolName}' with ID {ToolId}", tool.Name, tool.Id);

        return tool;
    }

    /// <inheritdoc />
    public async Task<AnalysisTool> UpdateToolAsync(Guid id, UpdateToolRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation("[UPDATE TOOL] Updating tool {ToolId} in Dataverse", id);

        var existing = await GetToolAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("[UPDATE TOOL] Tool {ToolId} not found for update", id);
            throw new KeyNotFoundException($"Tool with ID '{id}' not found.");
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "update");

        await EnsureAuthenticatedAsync(cancellationToken);

        // Build PATCH payload with only non-null fields (sparse update)
        var payload = new Dictionary<string, object?>();

        if (request.Name != null)
            payload["sprk_name"] = EnsureCustomerPrefix(request.Name);
        if (request.Description != null)
            payload["sprk_description"] = request.Description;
        if (request.HandlerClass != null)
            payload["sprk_handlerclass"] = request.HandlerClass;
        if (request.Configuration != null)
            payload["sprk_configuration"] = request.Configuration;

        // PATCH to Dataverse
        var url = $"sprk_analysistools({id})";
        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = httpContent
        };

        var response = await _httpClient.SendAsync(patchRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Re-fetch to get the updated entity
        var updated = await GetToolAsync(id, cancellationToken);
        if (updated == null)
        {
            throw new InvalidOperationException($"Failed to retrieve updated tool {id} from Dataverse");
        }

        _logger.LogInformation("[UPDATE TOOL] Updated tool '{ToolName}' (ID: {ToolId})", updated.Name, id);

        return updated;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteToolAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[DELETE TOOL] Deleting tool {ToolId} from Dataverse", id);

        var existing = await GetToolAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("[DELETE TOOL] Tool {ToolId} not found for deletion", id);
            return false;
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "delete");

        await EnsureAuthenticatedAsync(cancellationToken);

        // DELETE from Dataverse
        var url = $"sprk_analysistools({id})";
        var response = await _httpClient.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("[DELETE TOOL] Tool {ToolId} not found in Dataverse", id);
            return false;
        }

        response.EnsureSuccessStatusCode();

        _logger.LogInformation("[DELETE TOOL] Deleted tool '{ToolName}' (ID: {ToolId})", existing.Name, id);
        return true;
    }

    #endregion

    #region Search Operations

    /// <inheritdoc />
    public async Task<ScopeSearchResult> SearchScopesAsync(ScopeSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        _logger.LogInformation("[SEARCH SCOPES] Searching Dataverse with text '{SearchText}', types: {ScopeTypes}, ownerType: {OwnerType}",
            query.SearchText, query.ScopeTypes, query.OwnerType);

        // Determine which scope types to search
        var typesToSearch = query.ScopeTypes.Length > 0
            ? query.ScopeTypes
            : new[] { ScopeType.Action, ScopeType.Skill, ScopeType.Knowledge, ScopeType.Tool };

        var actions = Array.Empty<AnalysisAction>();
        var skills = Array.Empty<AnalysisSkill>();
        var knowledge = Array.Empty<AnalysisKnowledge>();
        var tools = Array.Empty<AnalysisTool>();

        var countsByType = new Dictionary<ScopeType, int>();

        // Build list options from search query
        var listOptions = new ScopeListOptions
        {
            NameFilter = query.SearchText,
            CategoryFilter = query.Category,
            Page = query.Page,
            PageSize = query.PageSize
        };

        // Search Actions via List method
        if (typesToSearch.Contains(ScopeType.Action))
        {
            var result = await ListActionsAsync(listOptions, cancellationToken);
            actions = ApplySearchFilters(result.Items, query).ToArray();
            countsByType[ScopeType.Action] = actions.Length;
        }

        // Search Skills via List method
        if (typesToSearch.Contains(ScopeType.Skill))
        {
            var result = await ListSkillsAsync(listOptions, cancellationToken);
            skills = ApplySearchFilters(result.Items, query).ToArray();
            countsByType[ScopeType.Skill] = skills.Length;
        }

        // Search Knowledge via List method
        if (typesToSearch.Contains(ScopeType.Knowledge))
        {
            var result = await ListKnowledgeAsync(listOptions, cancellationToken);
            knowledge = ApplySearchFilters(result.Items, query).ToArray();
            countsByType[ScopeType.Knowledge] = knowledge.Length;
        }

        // Search Tools via List method
        if (typesToSearch.Contains(ScopeType.Tool))
        {
            var result = await ListToolsAsync(listOptions, cancellationToken);
            tools = ApplySearchFilters(result.Items, query).ToArray();
            countsByType[ScopeType.Tool] = tools.Length;
        }

        var totalCount = countsByType.Values.Sum();

        _logger.LogInformation("[SEARCH SCOPES] Search found {TotalCount} results across {TypeCount} types", totalCount, countsByType.Count);

        return new ScopeSearchResult
        {
            Actions = actions,
            Skills = skills,
            Knowledge = knowledge,
            Tools = tools,
            TotalCount = totalCount,
            CountsByType = countsByType
        };
    }

    /// <summary>
    /// Applies additional search filters that can't be done in OData query.
    /// </summary>
    private static T[] ApplySearchFilters<T>(T[] items, ScopeSearchQuery query) where T : class
    {
        IEnumerable<T> filtered = items;

        // Apply owner type filter
        if (query.OwnerType.HasValue)
        {
            filtered = filtered.Where(item =>
            {
                var ownerType = item switch
                {
                    AnalysisAction a => a.OwnerType,
                    AnalysisSkill s => s.OwnerType,
                    AnalysisKnowledge k => k.OwnerType,
                    AnalysisTool t => t.OwnerType,
                    _ => ScopeOwnerType.System
                };
                return ownerType == query.OwnerType.Value;
            });
        }

        // Apply editable-only filter
        if (query.EditableOnly)
        {
            filtered = filtered.Where(item =>
            {
                var (isImmutable, name) = item switch
                {
                    AnalysisAction a => (a.IsImmutable, a.Name),
                    AnalysisSkill s => (s.IsImmutable, s.Name),
                    AnalysisKnowledge k => (k.IsImmutable, k.Name),
                    AnalysisTool t => (t.IsImmutable, t.Name),
                    _ => (true, "")
                };
                return !isImmutable && !IsSystemScope(name);
            });
        }

        return filtered.ToArray();
    }

    #endregion

    #region Save As Operations

    /// <inheritdoc />
    public async Task<AnalysisAction> SaveAsActionAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));

        _logger.LogInformation("Saving action {SourceId} as '{NewName}'", sourceId, newName);

        // Load source action
        var source = await GetActionAsync(sourceId, cancellationToken);
        if (source == null)
        {
            _logger.LogWarning("Source action {SourceId} not found for Save As", sourceId);
            throw new KeyNotFoundException($"Source action with ID '{sourceId}' not found.");
        }

        // Create copy via Create method with BasedOnId tracking
        var createRequest = new CreateActionRequest
        {
            Name = newName,
            Description = source.Description,
            SystemPrompt = source.SystemPrompt,
            SortOrder = source.SortOrder,
            ActionType = source.ActionType
        };

        var copy = await CreateActionAsync(createRequest, cancellationToken);

        // Update with BasedOnId reference (created entity tracks lineage)
        _logger.LogInformation("[SAVE AS ACTION] Created action copy '{Name}' (ID: {CopyId}) based on {SourceId}",
            copy.Name, copy.Id, sourceId);

        return copy with { BasedOnId = sourceId };
    }

    /// <inheritdoc />
    public async Task<AnalysisSkill> SaveAsSkillAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));

        _logger.LogInformation("Saving skill {SourceId} as '{NewName}'", sourceId, newName);

        var source = await GetSkillAsync(sourceId, cancellationToken);
        if (source == null)
        {
            _logger.LogWarning("Source skill {SourceId} not found for Save As", sourceId);
            throw new KeyNotFoundException($"Source skill with ID '{sourceId}' not found.");
        }

        // Create copy via Create method with BasedOnId tracking
        var createRequest = new CreateSkillRequest
        {
            Name = newName,
            Description = source.Description,
            PromptFragment = source.PromptFragment,
            Category = source.Category
        };

        var copy = await CreateSkillAsync(createRequest, cancellationToken);

        _logger.LogInformation("[SAVE AS SKILL] Created skill copy '{Name}' (ID: {CopyId}) based on {SourceId}",
            copy.Name, copy.Id, sourceId);

        return copy with { BasedOnId = sourceId };
    }

    /// <inheritdoc />
    public async Task<AnalysisKnowledge> SaveAsKnowledgeAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));

        _logger.LogInformation("Saving knowledge {SourceId} as '{NewName}'", sourceId, newName);

        var source = await GetKnowledgeAsync(sourceId, cancellationToken);
        if (source == null)
        {
            _logger.LogWarning("Source knowledge {SourceId} not found for Save As", sourceId);
            throw new KeyNotFoundException($"Source knowledge with ID '{sourceId}' not found.");
        }

        // Create copy via Create method with BasedOnId tracking
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

        _logger.LogInformation("[SAVE AS KNOWLEDGE] Created knowledge copy '{Name}' (ID: {CopyId}) based on {SourceId}",
            copy.Name, copy.Id, sourceId);

        return copy with { BasedOnId = sourceId };
    }

    /// <inheritdoc />
    public async Task<AnalysisTool> SaveAsToolAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));

        _logger.LogInformation("Saving tool {SourceId} as '{NewName}'", sourceId, newName);

        var source = await GetToolAsync(sourceId, cancellationToken);
        if (source == null)
        {
            _logger.LogWarning("Source tool {SourceId} not found for Save As", sourceId);
            throw new KeyNotFoundException($"Source tool with ID '{sourceId}' not found.");
        }

        // Create copy via Create method with BasedOnId tracking
        var createRequest = new CreateToolRequest
        {
            Name = newName,
            Description = source.Description,
            Type = source.Type,
            HandlerClass = source.HandlerClass,
            Configuration = source.Configuration
        };

        var copy = await CreateToolAsync(createRequest, cancellationToken);

        _logger.LogInformation("[SAVE AS TOOL] Created tool copy '{Name}' (ID: {CopyId}) based on {SourceId}",
            copy.Name, copy.Id, sourceId);

        return copy with { BasedOnId = sourceId };
    }

    #endregion

    #region Extend Operations

    /// <inheritdoc />
    public async Task<AnalysisAction> ExtendActionAsync(Guid parentId, string childName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childName, nameof(childName));

        _logger.LogInformation("[EXTEND ACTION] Extending action {ParentId} with child '{ChildName}'", parentId, childName);

        // Load parent action
        var parent = await GetActionAsync(parentId, cancellationToken);
        if (parent == null)
        {
            _logger.LogWarning("[EXTEND ACTION] Parent action {ParentId} not found for Extend", parentId);
            throw new KeyNotFoundException($"Parent action with ID '{parentId}' not found.");
        }

        // Create child via Create method with ParentScopeId tracking
        var createRequest = new CreateActionRequest
        {
            Name = childName,
            Description = parent.Description,
            SystemPrompt = parent.SystemPrompt,
            SortOrder = parent.SortOrder,
            ActionType = parent.ActionType
        };

        var child = await CreateActionAsync(createRequest, cancellationToken);

        _logger.LogInformation("[EXTEND ACTION] Created child action '{Name}' (ID: {ChildId}) extending {ParentId}",
            child.Name, child.Id, parentId);

        return child with { ParentScopeId = parentId };
    }

    /// <inheritdoc />
    public async Task<AnalysisSkill> ExtendSkillAsync(Guid parentId, string childName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childName, nameof(childName));

        _logger.LogInformation("[EXTEND SKILL] Extending skill {ParentId} with child '{ChildName}'", parentId, childName);

        var parent = await GetSkillAsync(parentId, cancellationToken);
        if (parent == null)
        {
            _logger.LogWarning("[EXTEND SKILL] Parent skill {ParentId} not found for Extend", parentId);
            throw new KeyNotFoundException($"Parent skill with ID '{parentId}' not found.");
        }

        // Create child via Create method with ParentScopeId tracking
        var createRequest = new CreateSkillRequest
        {
            Name = childName,
            Description = parent.Description,
            PromptFragment = parent.PromptFragment,
            Category = parent.Category
        };

        var child = await CreateSkillAsync(createRequest, cancellationToken);

        _logger.LogInformation("[EXTEND SKILL] Created child skill '{Name}' (ID: {ChildId}) extending {ParentId}",
            child.Name, child.Id, parentId);

        return child with { ParentScopeId = parentId };
    }

    /// <inheritdoc />
    public async Task<AnalysisKnowledge> ExtendKnowledgeAsync(Guid parentId, string childName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childName, nameof(childName));

        _logger.LogInformation("[EXTEND KNOWLEDGE] Extending knowledge {ParentId} with child '{ChildName}'", parentId, childName);

        var parent = await GetKnowledgeAsync(parentId, cancellationToken);
        if (parent == null)
        {
            _logger.LogWarning("[EXTEND KNOWLEDGE] Parent knowledge {ParentId} not found for Extend", parentId);
            throw new KeyNotFoundException($"Parent knowledge with ID '{parentId}' not found.");
        }

        // Create child via Create method with ParentScopeId tracking
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

        _logger.LogInformation("[EXTEND KNOWLEDGE] Created child knowledge '{Name}' (ID: {ChildId}) extending {ParentId}",
            child.Name, child.Id, parentId);

        return child with { ParentScopeId = parentId };
    }

    /// <inheritdoc />
    public async Task<AnalysisTool> ExtendToolAsync(Guid parentId, string childName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childName, nameof(childName));

        _logger.LogInformation("[EXTEND TOOL] Extending tool {ParentId} with child '{ChildName}'", parentId, childName);

        var parent = await GetToolAsync(parentId, cancellationToken);
        if (parent == null)
        {
            _logger.LogWarning("[EXTEND TOOL] Parent tool {ParentId} not found for Extend", parentId);
            throw new KeyNotFoundException($"Parent tool with ID '{parentId}' not found.");
        }

        // Create child via Create method with ParentScopeId tracking
        var createRequest = new CreateToolRequest
        {
            Name = childName,
            Description = parent.Description,
            Type = parent.Type,
            HandlerClass = parent.HandlerClass,
            Configuration = parent.Configuration
        };

        var child = await CreateToolAsync(createRequest, cancellationToken);

        _logger.LogInformation("[EXTEND TOOL] Created child tool '{Name}' (ID: {ChildId}) extending {ParentId}",
            child.Name, child.Id, parentId);

        return child with { ParentScopeId = parentId };
    }

    #endregion

    #endregion

    #region Private DTOs

    /// <summary>
    /// DTO for deserializing sprk_analysistool entity from Dataverse Web API
    /// </summary>
    private class ToolEntity
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

    private class ToolTypeReference
    {
        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }
    }

    /// <summary>
    /// DTO for deserializing sprk_analysisskill entity from Dataverse Web API (Skills)
    /// </summary>
    private class SkillEntity
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

    private class SkillTypeReference
    {
        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }
    }

    /// <summary>
    /// DTO for deserializing sprk_analysisknowledge entity from Dataverse Web API (Knowledge)
    /// </summary>
    private class KnowledgeEntity
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
    }

    private class KnowledgeTypeReference
    {
        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }
    }

    /// <summary>
    /// DTO for deserializing sprk_analysisaction entity from Dataverse Web API (Actions)
    /// </summary>
    private class ActionEntity
    {
        [JsonPropertyName("sprk_analysisactionid")]
        public Guid Id { get; set; }

        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }

        [JsonPropertyName("sprk_description")]
        public string? Description { get; set; }

        [JsonPropertyName("sprk_systemprompt")]
        public string? SystemPrompt { get; set; }

        [JsonPropertyName("sprk_ActionTypeId")]
        public ActionTypeReference? ActionTypeId { get; set; }
    }

    private class ActionTypeReference
    {
        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }
    }

    /// <summary>
    /// Generic OData collection response with count support for pagination
    /// </summary>
    private class ODataCollectionResponse<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = new();

        [JsonPropertyName("@odata.count")]
        public int? ODataCount { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    #endregion

    #region OData Query Helpers

    /// <summary>
    /// Builds an OData query string from ScopeListOptions
    /// </summary>
    /// <param name="options">List options including pagination, filtering, sorting</param>
    /// <param name="selectFields">Fields to select</param>
    /// <param name="expandClause">Expand clause for related entities</param>
    /// <param name="nameFieldPath">Path to the name field for filtering (e.g., "sprk_name")</param>
    /// <param name="categoryFieldPath">Optional path to category/type field for filtering</param>
    /// <param name="sortFieldMappings">Dictionary mapping sort keys to field paths</param>
    /// <returns>OData query string (without leading ?)</returns>
    private static string BuildODataQuery(
        ScopeListOptions options,
        string selectFields,
        string expandClause,
        string nameFieldPath,
        string? categoryFieldPath,
        Dictionary<string, string> sortFieldMappings)
    {
        var queryParts = new List<string>();

        // Select
        if (!string.IsNullOrEmpty(selectFields))
        {
            queryParts.Add($"$select={selectFields}");
        }

        // Expand for related entities
        if (!string.IsNullOrEmpty(expandClause))
        {
            queryParts.Add($"$expand={expandClause}");
        }

        // Build filter clauses
        var filterClauses = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.NameFilter))
        {
            // OData contains function for text search
            var escapedName = options.NameFilter.Replace("'", "''");
            filterClauses.Add($"contains({nameFieldPath}, '{escapedName}')");
        }

        if (!string.IsNullOrWhiteSpace(options.CategoryFilter) && !string.IsNullOrEmpty(categoryFieldPath))
        {
            var escapedCategory = options.CategoryFilter.Replace("'", "''");
            filterClauses.Add($"{categoryFieldPath} eq '{escapedCategory}'");
        }

        if (filterClauses.Count > 0)
        {
            queryParts.Add($"$filter={string.Join(" and ", filterClauses)}");
        }

        // Sorting
        var sortField = sortFieldMappings.GetValueOrDefault(options.SortBy.ToLowerInvariant(), sortFieldMappings.Values.First());
        var sortDirection = options.SortDescending ? "desc" : "asc";
        queryParts.Add($"$orderby={sortField} {sortDirection}");

        // Pagination
        var skip = (options.Page - 1) * options.PageSize;
        if (skip > 0)
        {
            queryParts.Add($"$skip={skip}");
        }
        queryParts.Add($"$top={options.PageSize}");

        // Request count for pagination
        queryParts.Add("$count=true");

        return string.Join("&", queryParts);
    }

    #endregion
}
