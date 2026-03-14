using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Resolves analysis scopes from Dataverse entities.
/// After PPI-054 decomposition, this service focuses on scope resolution orchestration,
/// lookup queries, and search — delegating entity CRUD to focused services:
/// <see cref="AnalysisActionService"/>, <see cref="AnalysisSkillService"/>,
/// <see cref="AnalysisKnowledgeService"/>, <see cref="AnalysisToolService"/>.
/// </summary>
public class ScopeResolverService : IScopeResolverService
{
    private readonly IPlaybookService _playbookService;
    private readonly AnalysisActionService _actionService;
    private readonly AnalysisSkillService _skillService;
    private readonly AnalysisKnowledgeService _knowledgeService;
    private readonly AnalysisToolService _toolService;
    private readonly ILogger<ScopeResolverService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly TokenCredential _credential;
    private AccessToken? _currentToken;

    public ScopeResolverService(
        IPlaybookService playbookService,
        AnalysisActionService actionService,
        AnalysisSkillService skillService,
        AnalysisKnowledgeService knowledgeService,
        AnalysisToolService toolService,
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ScopeResolverService> logger)
    {
        _playbookService = playbookService;
        _actionService = actionService;
        _skillService = skillService;
        _knowledgeService = knowledgeService;
        _toolService = toolService;
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

    private async Task EnsureSuccessWithDiagnosticsAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "[DATAVERSE-ERROR] {Operation} failed with {StatusCode}. Response body: {Body}",
                operation, response.StatusCode, body);
            throw new HttpRequestException(
                $"{operation} failed: {(int)response.StatusCode} {response.ReasonPhrase}. " +
                $"Dataverse error: {(body.Length > 500 ? body[..500] : body)}",
                null,
                response.StatusCode);
        }
    }

    #region Scope Resolution (Core responsibility)

    /// <inheritdoc />
    public async Task<ResolvedScopes> ResolveScopesAsync(
        Guid[] skillIds,
        Guid[] knowledgeIds,
        Guid[] toolIds,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Resolving scopes: {SkillCount} skills, {KnowledgeCount} knowledge, {ToolCount} tools",
            skillIds.Length, knowledgeIds.Length, toolIds.Length);

        if (skillIds.Length == 0 && knowledgeIds.Length == 0 && toolIds.Length == 0)
        {
            _logger.LogDebug("No scope IDs provided, returning empty scopes");
            return new ResolvedScopes([], [], []);
        }

        try
        {
            // Start all three sub-loads concurrently — they are independent Dataverse reads
            var skillsTask = skillIds.Length > 0
                ? Task.WhenAll(skillIds.Select(id => _skillService.GetSkillAsync(id, cancellationToken)))
                : Task.FromResult(Array.Empty<AnalysisSkill?>());

            var knowledgeTask = knowledgeIds.Length > 0
                ? Task.WhenAll(knowledgeIds.Select(id => _knowledgeService.GetKnowledgeAsync(id, cancellationToken)))
                : Task.FromResult(Array.Empty<AnalysisKnowledge?>());

            var toolsTask = toolIds.Length > 0
                ? Task.WhenAll(toolIds.Select(id => _toolService.GetToolAsync(id, cancellationToken)))
                : Task.FromResult(Array.Empty<AnalysisTool?>());

            await Task.WhenAll(skillsTask, knowledgeTask, toolsTask);

            var skills = (await skillsTask).Where(s => s != null).Cast<AnalysisSkill>().ToArray();
            var knowledge = (await knowledgeTask).Where(k => k != null).Cast<AnalysisKnowledge>().ToArray();
            var tools = (await toolsTask).Where(t => t != null).Cast<AnalysisTool>().ToArray();

            _logger.LogInformation(
                "Scope resolution complete: {SkillCount} skills, {KnowledgeCount} knowledge, {ToolCount} tools",
                skills.Length, knowledge.Length, tools.Length);

            return new ResolvedScopes(skills, knowledge, tools);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to resolve scopes ({SkillCount} skills, {KnowledgeCount} knowledge, {ToolCount} tools)",
                skillIds.Length, knowledgeIds.Length, toolIds.Length);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ResolvedScopes> ResolvePlaybookScopesAsync(
        Guid playbookId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Resolving scopes from playbook {PlaybookId}", playbookId);

        try
        {
            var playbook = await _playbookService.GetPlaybookAsync(playbookId, cancellationToken);

            if (playbook == null)
            {
                _logger.LogWarning("Playbook {PlaybookId} not found", playbookId);
                return new ResolvedScopes([], [], []);
            }

            _logger.LogDebug(
                "Playbook '{PlaybookName}' has {ToolCount} tools, {SkillCount} skills, {KnowledgeCount} knowledge",
                playbook.Name, playbook.ToolIds.Length, playbook.SkillIds.Length, playbook.KnowledgeIds.Length);

            if (playbook.ToolIds.Length == 0 && playbook.SkillIds.Length == 0 && playbook.KnowledgeIds.Length == 0)
            {
                _logger.LogWarning(
                    "Playbook '{PlaybookName}' (ID: {PlaybookId}) has no scopes configured",
                    playbook.Name, playbookId);
                return new ResolvedScopes([], [], []);
            }

            var tools = Array.Empty<AnalysisTool>();
            if (playbook.ToolIds.Length > 0)
            {
                _logger.LogDebug(
                    "Loading {Count} tool entities for playbook '{PlaybookName}'",
                    playbook.ToolIds.Length, playbook.Name);

                var toolTasks = playbook.ToolIds.Select(toolId => _toolService.GetToolAsync(toolId, cancellationToken));
                var toolResults = await Task.WhenAll(toolTasks);

                tools = toolResults
                    .Where(t => t != null)
                    .Cast<AnalysisTool>()
                    .ToArray();
            }

            _logger.LogDebug(
                "Resolved {ToolCount} tools from playbook '{PlaybookName}' (ID: {PlaybookId}): {ToolNames}",
                tools.Length,
                playbook.Name,
                playbookId,
                string.Join(", ", tools.Select(t => t.Name)));

            var skills = Array.Empty<AnalysisSkill>();
            if (playbook.SkillIds.Length > 0)
            {
                _logger.LogDebug(
                    "Loading {Count} skill entities for playbook '{PlaybookName}'",
                    playbook.SkillIds.Length, playbook.Name);

                var skillTasks = playbook.SkillIds.Select(id => _skillService.GetSkillAsync(id, cancellationToken));
                var skillResults = await Task.WhenAll(skillTasks);
                skills = skillResults.Where(s => s != null).Cast<AnalysisSkill>().ToArray();

                _logger.LogDebug(
                    "Resolved {SkillCount} skills from playbook '{PlaybookName}': {SkillNames}",
                    skills.Length, playbook.Name,
                    string.Join(", ", skills.Select(s => s.Name)));
            }

            var knowledge = Array.Empty<AnalysisKnowledge>();
            if (playbook.KnowledgeIds.Length > 0)
            {
                _logger.LogDebug(
                    "Loading {Count} knowledge entities for playbook '{PlaybookName}'",
                    playbook.KnowledgeIds.Length, playbook.Name);

                var knowledgeTasks = playbook.KnowledgeIds.Select(id => _knowledgeService.GetKnowledgeAsync(id, cancellationToken));
                var knowledgeResults = await Task.WhenAll(knowledgeTasks);
                knowledge = knowledgeResults.Where(k => k != null).Cast<AnalysisKnowledge>().ToArray();

                _logger.LogDebug(
                    "Resolved {KnowledgeCount} knowledge sources from playbook '{PlaybookName}': {KnowledgeNames}",
                    knowledge.Length, playbook.Name,
                    string.Join(", ", knowledge.Select(k => k.Name)));
            }

            return new ResolvedScopes(skills, knowledge, tools);
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
    public async Task<ResolvedScopes> ResolveNodeScopesAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Resolving scopes from node {NodeId}", nodeId);

        await EnsureAuthenticatedAsync(cancellationToken);

        try
        {
            var skillIds = await QueryNodeRelatedIdsAsync(
                nodeId, "sprk_playbooknode_skill", "sprk_analysisskillid", cancellationToken);

            var knowledgeIds = await QueryNodeRelatedIdsAsync(
                nodeId, "sprk_playbooknode_knowledge", "sprk_analysisknowledgeid", cancellationToken);

            var toolIds = await QueryNodeRelatedIdsAsync(
                nodeId, "sprk_playbooknode_tool", "sprk_analysistoolid", cancellationToken);

            _logger.LogDebug(
                "Node {NodeId} relationships: {SkillCount} skills, {KnowledgeCount} knowledge, {ToolCount} tools",
                nodeId, skillIds.Length, knowledgeIds.Length, toolIds.Length);

            var tools = Array.Empty<AnalysisTool>();
            if (toolIds.Length > 0)
            {
                var toolTasks = toolIds.Select(id => _toolService.GetToolAsync(id, cancellationToken));
                var toolResults = await Task.WhenAll(toolTasks);
                tools = toolResults.Where(t => t != null).Cast<AnalysisTool>().ToArray();
            }

            var skills = Array.Empty<AnalysisSkill>();
            if (skillIds.Length > 0)
            {
                var skillTasks = skillIds.Select(id => _skillService.GetSkillAsync(id, cancellationToken));
                var skillResults = await Task.WhenAll(skillTasks);
                skills = skillResults.Where(s => s != null).Cast<AnalysisSkill>().ToArray();
            }

            var knowledge = Array.Empty<AnalysisKnowledge>();
            if (knowledgeIds.Length > 0)
            {
                var knowledgeTasks = knowledgeIds.Select(id => _knowledgeService.GetKnowledgeAsync(id, cancellationToken));
                var knowledgeResults = await Task.WhenAll(knowledgeTasks);
                knowledge = knowledgeResults.Where(k => k != null).Cast<AnalysisKnowledge>().ToArray();
            }

            _logger.LogInformation(
                "Resolved node {NodeId} scopes: {SkillCount} skills, {KnowledgeCount} knowledge, {ToolCount} tools",
                nodeId, skills.Length, knowledge.Length, tools.Length);

            return new ResolvedScopes(skills, knowledge, tools);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve scopes from node {NodeId}", nodeId);
            throw;
        }
    }

    private async Task<Guid[]> QueryNodeRelatedIdsAsync(
        Guid nodeId,
        string relationshipName,
        string targetIdField,
        CancellationToken cancellationToken)
    {
        var url = $"sprk_playbooknodes({nodeId})/{relationshipName}?$select={targetIdField}";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to query {Relationship} for node {NodeId}: {StatusCode}",
                    relationshipName, nodeId, response.StatusCode);
                return [];
            }

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("value", out var valueArray) ||
                valueArray.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return valueArray.EnumerateArray()
                .Select(e => e.TryGetProperty(targetIdField, out var idProp) && idProp.ValueKind != JsonValueKind.Null
                    ? Guid.TryParse(idProp.GetString(), out var id) ? id : Guid.Empty
                    : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query {Relationship} for node {NodeId}", relationshipName, nodeId);
            return [];
        }
    }

    #endregion

    #region Delegated CRUD Operations

    /// <inheritdoc />
    public Task<AnalysisAction?> GetActionAsync(Guid actionId, CancellationToken cancellationToken)
        => _actionService.GetActionAsync(actionId, cancellationToken);

    /// <inheritdoc />
    public Task<ScopeListResult<AnalysisAction>> ListActionsAsync(ScopeListOptions options, CancellationToken cancellationToken)
        => _actionService.ListActionsAsync(options, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisAction> CreateActionAsync(CreateActionRequest request, CancellationToken cancellationToken)
        => _actionService.CreateActionAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisAction> UpdateActionAsync(Guid id, UpdateActionRequest request, CancellationToken cancellationToken)
        => _actionService.UpdateActionAsync(id, request, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteActionAsync(Guid id, CancellationToken cancellationToken)
        => _actionService.DeleteActionAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisAction> SaveAsActionAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
        => _actionService.SaveAsActionAsync(sourceId, newName, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisAction> ExtendActionAsync(Guid parentId, string childName, CancellationToken cancellationToken)
        => _actionService.ExtendActionAsync(parentId, childName, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisSkill?> GetSkillAsync(Guid skillId, CancellationToken cancellationToken)
        => _skillService.GetSkillAsync(skillId, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisSkill?> GetSkillByNameAsync(string name, CancellationToken cancellationToken)
        => _skillService.GetSkillByNameAsync(name, cancellationToken);

    /// <inheritdoc />
    public Task<ScopeListResult<AnalysisSkill>> ListSkillsAsync(ScopeListOptions options, CancellationToken cancellationToken)
        => _skillService.ListSkillsAsync(options, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisSkill> CreateSkillAsync(CreateSkillRequest request, CancellationToken cancellationToken)
        => _skillService.CreateSkillAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisSkill> UpdateSkillAsync(Guid id, UpdateSkillRequest request, CancellationToken cancellationToken)
        => _skillService.UpdateSkillAsync(id, request, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteSkillAsync(Guid id, CancellationToken cancellationToken)
        => _skillService.DeleteSkillAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisSkill> SaveAsSkillAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
        => _skillService.SaveAsSkillAsync(sourceId, newName, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisSkill> ExtendSkillAsync(Guid parentId, string childName, CancellationToken cancellationToken)
        => _skillService.ExtendSkillAsync(parentId, childName, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisKnowledge?> GetKnowledgeAsync(Guid knowledgeId, CancellationToken cancellationToken)
        => _knowledgeService.GetKnowledgeAsync(knowledgeId, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisKnowledge?> GetKnowledgeByNameAsync(string name, CancellationToken cancellationToken)
        => _knowledgeService.GetKnowledgeByNameAsync(name, cancellationToken);

    /// <inheritdoc />
    public Task<ScopeListResult<AnalysisKnowledge>> ListKnowledgeAsync(ScopeListOptions options, CancellationToken cancellationToken)
        => _knowledgeService.ListKnowledgeAsync(options, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisKnowledge> CreateKnowledgeAsync(CreateKnowledgeRequest request, CancellationToken cancellationToken)
        => _knowledgeService.CreateKnowledgeAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisKnowledge> UpdateKnowledgeAsync(Guid id, UpdateKnowledgeRequest request, CancellationToken cancellationToken)
        => _knowledgeService.UpdateKnowledgeAsync(id, request, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteKnowledgeAsync(Guid id, CancellationToken cancellationToken)
        => _knowledgeService.DeleteKnowledgeAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisKnowledge> SaveAsKnowledgeAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
        => _knowledgeService.SaveAsKnowledgeAsync(sourceId, newName, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisKnowledge> ExtendKnowledgeAsync(Guid parentId, string childName, CancellationToken cancellationToken)
        => _knowledgeService.ExtendKnowledgeAsync(parentId, childName, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisTool?> GetToolAsync(Guid toolId, CancellationToken cancellationToken)
        => _toolService.GetToolAsync(toolId, cancellationToken);

    /// <inheritdoc />
    public Task<ScopeListResult<AnalysisTool>> ListToolsAsync(ScopeListOptions options, CancellationToken cancellationToken)
        => _toolService.ListToolsAsync(options, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisTool> CreateToolAsync(CreateToolRequest request, CancellationToken cancellationToken)
        => _toolService.CreateToolAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisTool> UpdateToolAsync(Guid id, UpdateToolRequest request, CancellationToken cancellationToken)
        => _toolService.UpdateToolAsync(id, request, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteToolAsync(Guid id, CancellationToken cancellationToken)
        => _toolService.DeleteToolAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisTool> SaveAsToolAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
        => _toolService.SaveAsToolAsync(sourceId, newName, cancellationToken);

    /// <inheritdoc />
    public Task<AnalysisTool> ExtendToolAsync(Guid parentId, string childName, CancellationToken cancellationToken)
        => _toolService.ExtendToolAsync(parentId, childName, cancellationToken);

    #endregion

    #region Search Operations

    /// <inheritdoc />
    public async Task<ScopeSearchResult> SearchScopesAsync(ScopeSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        _logger.LogInformation("[SEARCH SCOPES] Searching Dataverse with text '{SearchText}', types: {ScopeTypes}, ownerType: {OwnerType}",
            query.SearchText, query.ScopeTypes, query.OwnerType);

        var typesToSearch = query.ScopeTypes.Length > 0
            ? query.ScopeTypes
            : new[] { ScopeType.Action, ScopeType.Skill, ScopeType.Knowledge, ScopeType.Tool };

        var actions = Array.Empty<AnalysisAction>();
        var skills = Array.Empty<AnalysisSkill>();
        var knowledge = Array.Empty<AnalysisKnowledge>();
        var tools = Array.Empty<AnalysisTool>();

        var countsByType = new Dictionary<ScopeType, int>();

        var listOptions = new ScopeListOptions
        {
            NameFilter = query.SearchText,
            CategoryFilter = query.Category,
            Page = query.Page,
            PageSize = query.PageSize
        };

        if (typesToSearch.Contains(ScopeType.Action))
        {
            var result = await _actionService.ListActionsAsync(listOptions, cancellationToken);
            actions = ApplySearchFilters(result.Items, query).ToArray();
            countsByType[ScopeType.Action] = actions.Length;
        }

        if (typesToSearch.Contains(ScopeType.Skill))
        {
            var result = await _skillService.ListSkillsAsync(listOptions, cancellationToken);
            skills = ApplySearchFilters(result.Items, query).ToArray();
            countsByType[ScopeType.Skill] = skills.Length;
        }

        if (typesToSearch.Contains(ScopeType.Knowledge))
        {
            var result = await _knowledgeService.ListKnowledgeAsync(listOptions, cancellationToken);
            knowledge = ApplySearchFilters(result.Items, query).ToArray();
            countsByType[ScopeType.Knowledge] = knowledge.Length;
        }

        if (typesToSearch.Contains(ScopeType.Tool))
        {
            var result = await _toolService.ListToolsAsync(listOptions, cancellationToken);
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

    private static T[] ApplySearchFilters<T>(T[] items, ScopeSearchQuery query) where T : class
    {
        IEnumerable<T> filtered = items;

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

    private static bool IsSystemScope(string name)
        => name.StartsWith("SYS-", StringComparison.OrdinalIgnoreCase);

    #endregion

    #region Lookup Queries

    /// <inheritdoc />
    public async Task<string[]> QueryLookupValuesAsync(
        string entitySetName,
        string fieldName,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var safeEntitySet = entitySetName.Replace("'", "").Replace("/", "");
        var safeField = fieldName.Replace("'", "").Replace("/", "");

        var url = $"{safeEntitySet}?$select={safeField}&$orderby={safeField} asc&$top=200&$filter=statecode eq 0";

        _logger.LogDebug(
            "[LOOKUP CHOICES] Querying {EntitySet}.{Field}",
            safeEntitySet, safeField);

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            await EnsureSuccessWithDiagnosticsAsync(response, $"QueryLookupValues({safeEntitySet}.{safeField})", cancellationToken);

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("value", out var valueArray) ||
                valueArray.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var values = new List<string>();
            foreach (var item in valueArray.EnumerateArray())
            {
                if (item.TryGetProperty(safeField, out var fieldValue) &&
                    fieldValue.ValueKind == JsonValueKind.String)
                {
                    var val = fieldValue.GetString();
                    if (!string.IsNullOrWhiteSpace(val))
                        values.Add(val);
                }
            }

            _logger.LogDebug(
                "[LOOKUP CHOICES] Found {Count} values in {EntitySet}.{Field}",
                values.Count, safeEntitySet, safeField);

            return values.ToArray();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "[LOOKUP CHOICES] Failed to query {EntitySet}.{Field}",
                safeEntitySet, safeField);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<string[]> QueryOptionSetLabelsAsync(
        string entityLogicalName,
        string attributeLogicalName,
        bool isMultiSelect,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var safeEntity = entityLogicalName.Replace("'", "").Replace("/", "");
        var safeAttribute = attributeLogicalName.Replace("'", "").Replace("/", "");

        var metadataType = isMultiSelect
            ? "Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata"
            : "Microsoft.Dynamics.CRM.PicklistAttributeMetadata";

        var url = $"EntityDefinitions(LogicalName='{safeEntity}')" +
                  $"/Attributes(LogicalName='{safeAttribute}')/{metadataType}" +
                  $"?$select=LogicalName&$expand=OptionSet($select=Options)";

        _logger.LogDebug(
            "[OPTIONSET CHOICES] Querying {Entity}.{Attribute} (multiSelect={MultiSelect})",
            safeEntity, safeAttribute, isMultiSelect);

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            await EnsureSuccessWithDiagnosticsAsync(
                response, $"QueryOptionSetLabels({safeEntity}.{safeAttribute})", cancellationToken);

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            var root = doc.RootElement;

            if (!root.TryGetProperty("OptionSet", out var optionSet) ||
                !optionSet.TryGetProperty("Options", out var options) ||
                options.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning(
                    "[OPTIONSET CHOICES] No OptionSet.Options found for {Entity}.{Attribute}",
                    safeEntity, safeAttribute);
                return [];
            }

            var labels = new List<string>();
            foreach (var option in options.EnumerateArray())
            {
                var label = ExtractOptionLabel(option);
                if (!string.IsNullOrWhiteSpace(label))
                    labels.Add(label);
            }

            _logger.LogDebug(
                "[OPTIONSET CHOICES] Found {Count} options for {Entity}.{Attribute}",
                labels.Count, safeEntity, safeAttribute);

            return labels.ToArray();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "[OPTIONSET CHOICES] Failed to query {Entity}.{Attribute}",
                safeEntity, safeAttribute);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<string[]> QueryBooleanLabelsAsync(
        string entityLogicalName,
        string attributeLogicalName,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var safeEntity = entityLogicalName.Replace("'", "").Replace("/", "");
        var safeAttribute = attributeLogicalName.Replace("'", "").Replace("/", "");

        var url = $"EntityDefinitions(LogicalName='{safeEntity}')" +
                  $"/Attributes(LogicalName='{safeAttribute}')/Microsoft.Dynamics.CRM.BooleanAttributeMetadata" +
                  $"?$select=LogicalName&$expand=OptionSet($select=TrueOption,FalseOption)";

        _logger.LogDebug(
            "[BOOLEAN CHOICES] Querying {Entity}.{Attribute}",
            safeEntity, safeAttribute);

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            await EnsureSuccessWithDiagnosticsAsync(
                response, $"QueryBooleanLabels({safeEntity}.{safeAttribute})", cancellationToken);

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            var root = doc.RootElement;

            if (!root.TryGetProperty("OptionSet", out var optionSet))
            {
                _logger.LogWarning(
                    "[BOOLEAN CHOICES] No OptionSet found for {Entity}.{Attribute}",
                    safeEntity, safeAttribute);
                return [];
            }

            var labels = new List<string>(2);

            if (optionSet.TryGetProperty("TrueOption", out var trueOption))
            {
                var trueLabel = ExtractOptionLabel(trueOption);
                if (!string.IsNullOrWhiteSpace(trueLabel))
                    labels.Add(trueLabel);
            }

            if (optionSet.TryGetProperty("FalseOption", out var falseOption))
            {
                var falseLabel = ExtractOptionLabel(falseOption);
                if (!string.IsNullOrWhiteSpace(falseLabel))
                    labels.Add(falseLabel);
            }

            _logger.LogDebug(
                "[BOOLEAN CHOICES] Found labels [{Labels}] for {Entity}.{Attribute}",
                string.Join(", ", labels), safeEntity, safeAttribute);

            return labels.ToArray();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "[BOOLEAN CHOICES] Failed to query {Entity}.{Attribute}",
                safeEntity, safeAttribute);
            return [];
        }
    }

    private static string? ExtractOptionLabel(JsonElement option)
    {
        if (option.TryGetProperty("Label", out var labelObj) &&
            labelObj.TryGetProperty("UserLocalizedLabel", out var userLabel) &&
            userLabel.TryGetProperty("Label", out var labelValue) &&
            labelValue.ValueKind == JsonValueKind.String)
        {
            return labelValue.GetString();
        }

        return null;
    }

    #endregion
}
