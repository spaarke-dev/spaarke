using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Manages CRUD operations for Dataverse scope entities.
/// Implements ownership validation and duplicate name handling.
/// </summary>
/// <remarks>
/// Phase 1 Scaffolding: Uses in-memory data until Dataverse entity operations are fully implemented.
/// Will be extended to use DataverseWebApiClient for actual API calls.
/// </remarks>
public class ScopeManagementService : IScopeManagementService
{
    private readonly IDataverseService _dataverseService;
    private readonly IScopeResolverService _scopeResolver;
    private readonly ILogger<ScopeManagementService> _logger;

    // Ownership prefixes
    private const string SystemPrefix = "SYS-";
    private const string CustomerPrefix = "CUST-";

    public ScopeManagementService(
        IDataverseService dataverseService,
        IScopeResolverService scopeResolver,
        ILogger<ScopeManagementService> logger)
    {
        _dataverseService = dataverseService;
        _scopeResolver = scopeResolver;
        _logger = logger;
    }

    // ========================================
    // Action CRUD
    // ========================================

    public async Task<AnalysisAction> CreateActionAsync(CreateActionRequest request, CancellationToken cancellationToken)
    {
        var name = EnsureCustomerPrefix(request.Name);
        name = await GenerateUniqueNameAsync(ScopeType.Action, name, cancellationToken);

        _logger.LogInformation("Creating action scope: {Name}", name);

        // TODO: Replace with actual Dataverse Web API call
        var action = new AnalysisAction
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = request.Description,
            SystemPrompt = request.SystemPrompt,
            SortOrder = request.SortOrder,
            ActionType = request.ActionType
        };

        _logger.LogInformation("Created action scope: {Id} - {Name}", action.Id, action.Name);

        return action;
    }

    public async Task<AnalysisAction> UpdateActionAsync(Guid id, UpdateActionRequest request, CancellationToken cancellationToken)
    {
        // Get existing action to check ownership
        var existing = await _scopeResolver.GetActionAsync(id, cancellationToken);
        if (existing == null)
        {
            throw new InvalidOperationException($"Action with ID {id} not found.");
        }

        ValidateCanModify(existing.Name, "update");

        // Handle name change with uniqueness check
        var name = existing.Name;
        if (request.Name != null && request.Name != existing.Name)
        {
            name = EnsureCustomerPrefix(request.Name);
            name = await GenerateUniqueNameAsync(ScopeType.Action, name, cancellationToken);
        }

        _logger.LogInformation("Updating action scope: {Id} - {Name}", id, name);

        // TODO: Replace with actual Dataverse Web API call
        var updated = existing with
        {
            Name = name,
            Description = request.Description ?? existing.Description,
            SystemPrompt = request.SystemPrompt ?? existing.SystemPrompt,
            SortOrder = request.SortOrder ?? existing.SortOrder,
            ActionType = request.ActionType ?? existing.ActionType
        };

        return updated;
    }

    public async Task DeleteActionAsync(Guid id, CancellationToken cancellationToken)
    {
        var existing = await _scopeResolver.GetActionAsync(id, cancellationToken);
        if (existing == null)
        {
            throw new InvalidOperationException($"Action with ID {id} not found.");
        }

        ValidateCanModify(existing.Name, "delete");

        _logger.LogInformation("Deleting action scope: {Id} - {Name}", id, existing.Name);

        // TODO: Replace with actual Dataverse Web API call
        await Task.CompletedTask;
    }

    // ========================================
    // Skill CRUD
    // ========================================

    public async Task<AnalysisSkill> CreateSkillAsync(CreateSkillRequest request, CancellationToken cancellationToken)
    {
        var name = EnsureCustomerPrefix(request.Name);
        name = await GenerateUniqueNameAsync(ScopeType.Skill, name, cancellationToken);

        _logger.LogInformation("Creating skill scope: {Name}", name);

        var skill = new AnalysisSkill
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = request.Description,
            PromptFragment = request.PromptFragment,
            Category = request.Category
        };

        _logger.LogInformation("Created skill scope: {Id} - {Name}", skill.Id, skill.Name);

        return skill;
    }

    public async Task<AnalysisSkill> UpdateSkillAsync(Guid id, UpdateSkillRequest request, CancellationToken cancellationToken)
    {
        var options = new ScopeListOptions { PageSize = 1000 };
        var result = await _scopeResolver.ListSkillsAsync(options, cancellationToken);
        var existing = result.Items.FirstOrDefault(s => s.Id == id);

        if (existing == null)
        {
            throw new InvalidOperationException($"Skill with ID {id} not found.");
        }

        ValidateCanModify(existing.Name, "update");

        var name = existing.Name;
        if (request.Name != null && request.Name != existing.Name)
        {
            name = EnsureCustomerPrefix(request.Name);
            name = await GenerateUniqueNameAsync(ScopeType.Skill, name, cancellationToken);
        }

        _logger.LogInformation("Updating skill scope: {Id} - {Name}", id, name);

        return existing with
        {
            Name = name,
            Description = request.Description ?? existing.Description,
            PromptFragment = request.PromptFragment ?? existing.PromptFragment,
            Category = request.Category ?? existing.Category
        };
    }

    public async Task DeleteSkillAsync(Guid id, CancellationToken cancellationToken)
    {
        var options = new ScopeListOptions { PageSize = 1000 };
        var result = await _scopeResolver.ListSkillsAsync(options, cancellationToken);
        var existing = result.Items.FirstOrDefault(s => s.Id == id);

        if (existing == null)
        {
            throw new InvalidOperationException($"Skill with ID {id} not found.");
        }

        ValidateCanModify(existing.Name, "delete");

        _logger.LogInformation("Deleting skill scope: {Id} - {Name}", id, existing.Name);

        await Task.CompletedTask;
    }

    // ========================================
    // Knowledge CRUD
    // ========================================

    public async Task<AnalysisKnowledge> CreateKnowledgeAsync(CreateKnowledgeRequest request, CancellationToken cancellationToken)
    {
        var name = EnsureCustomerPrefix(request.Name);
        name = await GenerateUniqueNameAsync(ScopeType.Knowledge, name, cancellationToken);

        _logger.LogInformation("Creating knowledge scope: {Name}", name);

        var knowledge = new AnalysisKnowledge
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = request.Description,
            Type = request.Type,
            Content = request.Content,
            DocumentId = request.DocumentId,
            DeploymentId = request.DeploymentId
        };

        _logger.LogInformation("Created knowledge scope: {Id} - {Name}", knowledge.Id, knowledge.Name);

        return knowledge;
    }

    public async Task<AnalysisKnowledge> UpdateKnowledgeAsync(Guid id, UpdateKnowledgeRequest request, CancellationToken cancellationToken)
    {
        var options = new ScopeListOptions { PageSize = 1000 };
        var result = await _scopeResolver.ListKnowledgeAsync(options, cancellationToken);
        var existing = result.Items.FirstOrDefault(k => k.Id == id);

        if (existing == null)
        {
            throw new InvalidOperationException($"Knowledge with ID {id} not found.");
        }

        ValidateCanModify(existing.Name, "update");

        var name = existing.Name;
        if (request.Name != null && request.Name != existing.Name)
        {
            name = EnsureCustomerPrefix(request.Name);
            name = await GenerateUniqueNameAsync(ScopeType.Knowledge, name, cancellationToken);
        }

        _logger.LogInformation("Updating knowledge scope: {Id} - {Name}", id, name);

        return existing with
        {
            Name = name,
            Description = request.Description ?? existing.Description,
            Type = request.Type ?? existing.Type,
            Content = request.Content ?? existing.Content,
            DocumentId = request.DocumentId ?? existing.DocumentId,
            DeploymentId = request.DeploymentId ?? existing.DeploymentId
        };
    }

    public async Task DeleteKnowledgeAsync(Guid id, CancellationToken cancellationToken)
    {
        var options = new ScopeListOptions { PageSize = 1000 };
        var result = await _scopeResolver.ListKnowledgeAsync(options, cancellationToken);
        var existing = result.Items.FirstOrDefault(k => k.Id == id);

        if (existing == null)
        {
            throw new InvalidOperationException($"Knowledge with ID {id} not found.");
        }

        ValidateCanModify(existing.Name, "delete");

        _logger.LogInformation("Deleting knowledge scope: {Id} - {Name}", id, existing.Name);

        await Task.CompletedTask;
    }

    // ========================================
    // Tool CRUD
    // ========================================

    public async Task<AnalysisTool> CreateToolAsync(CreateToolRequest request, CancellationToken cancellationToken)
    {
        var name = EnsureCustomerPrefix(request.Name);
        name = await GenerateUniqueNameAsync(ScopeType.Tool, name, cancellationToken);

        _logger.LogInformation("Creating tool scope: {Name}", name);

        var tool = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = request.Description,
            Type = request.Type,
            HandlerClass = request.HandlerClass,
            Configuration = request.Configuration
        };

        _logger.LogInformation("Created tool scope: {Id} - {Name}", tool.Id, tool.Name);

        return tool;
    }

    public async Task<AnalysisTool> UpdateToolAsync(Guid id, UpdateToolRequest request, CancellationToken cancellationToken)
    {
        var options = new ScopeListOptions { PageSize = 1000 };
        var result = await _scopeResolver.ListToolsAsync(options, cancellationToken);
        var existing = result.Items.FirstOrDefault(t => t.Id == id);

        if (existing == null)
        {
            throw new InvalidOperationException($"Tool with ID {id} not found.");
        }

        ValidateCanModify(existing.Name, "update");

        var name = existing.Name;
        if (request.Name != null && request.Name != existing.Name)
        {
            name = EnsureCustomerPrefix(request.Name);
            name = await GenerateUniqueNameAsync(ScopeType.Tool, name, cancellationToken);
        }

        _logger.LogInformation("Updating tool scope: {Id} - {Name}", id, name);

        return existing with
        {
            Name = name,
            Description = request.Description ?? existing.Description,
            Type = request.Type ?? existing.Type,
            HandlerClass = request.HandlerClass ?? existing.HandlerClass,
            Configuration = request.Configuration ?? existing.Configuration
        };
    }

    public async Task DeleteToolAsync(Guid id, CancellationToken cancellationToken)
    {
        var options = new ScopeListOptions { PageSize = 1000 };
        var result = await _scopeResolver.ListToolsAsync(options, cancellationToken);
        var existing = result.Items.FirstOrDefault(t => t.Id == id);

        if (existing == null)
        {
            throw new InvalidOperationException($"Tool with ID {id} not found.");
        }

        ValidateCanModify(existing.Name, "delete");

        _logger.LogInformation("Deleting tool scope: {Id} - {Name}", id, existing.Name);

        await Task.CompletedTask;
    }

    // ========================================
    // Output CRUD
    // ========================================

    public async Task<AnalysisOutput> CreateOutputAsync(CreateOutputRequest request, CancellationToken cancellationToken)
    {
        var name = EnsureCustomerPrefix(request.Name);
        name = await GenerateUniqueNameAsync(ScopeType.Output, name, cancellationToken);

        _logger.LogInformation("Creating output scope: {Name}", name);

        var output = new AnalysisOutput
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = request.Description,
            FieldName = request.FieldName,
            FieldType = request.FieldType,
            JsonPath = request.JsonPath
        };

        _logger.LogInformation("Created output scope: {Id} - {Name}", output.Id, output.Name);

        return output;
    }

    public async Task<AnalysisOutput> UpdateOutputAsync(Guid id, UpdateOutputRequest request, CancellationToken cancellationToken)
    {
        // TODO: Implement when output listing is available in ScopeResolverService
        _logger.LogWarning("UpdateOutputAsync not fully implemented - output listing not available");

        await Task.CompletedTask;

        throw new NotImplementedException("Output update requires output listing in ScopeResolverService");
    }

    public async Task DeleteOutputAsync(Guid id, CancellationToken cancellationToken)
    {
        // TODO: Implement when output listing is available in ScopeResolverService
        _logger.LogWarning("DeleteOutputAsync not fully implemented - output listing not available");

        await Task.CompletedTask;

        throw new NotImplementedException("Output delete requires output listing in ScopeResolverService");
    }

    // ========================================
    // N:N Link Operations
    // ========================================

    // N:N relationship table names in Dataverse
    private static readonly Dictionary<ScopeType, string> RelationshipTableNames = new()
    {
        { ScopeType.Action, "sprk_aianalysisplaybook_action" },
        { ScopeType.Skill, "sprk_aianalysisplaybook_skill" },
        { ScopeType.Knowledge, "sprk_aianalysisplaybook_knowledge" },
        { ScopeType.Tool, "sprk_aianalysisplaybook_tool" },
        { ScopeType.Output, "sprk_aianalysisplaybook_output" }
    };

    // In-memory link storage for scaffolding (will be replaced with Dataverse calls)
    // Key: (playbookId, scopeType) -> Set of scopeIds
    private static readonly Dictionary<(Guid PlaybookId, ScopeType ScopeType), HashSet<Guid>> _links = new();
    private static readonly object _lockObject = new();

    /// <inheritdoc />
    public async Task LinkScopeToPlaybookAsync(Guid playbookId, ScopeType scopeType, Guid scopeId, CancellationToken cancellationToken)
    {
        var relationshipTable = RelationshipTableNames[scopeType];

        // Check for duplicate link
        if (await IsLinkedAsync(playbookId, scopeType, scopeId, cancellationToken))
        {
            _logger.LogWarning(
                "Link already exists: Playbook {PlaybookId} -> {ScopeType} {ScopeId}",
                playbookId, scopeType, scopeId);
            throw new InvalidOperationException(
                $"Scope {scopeId} of type {scopeType} is already linked to playbook {playbookId}.");
        }

        _logger.LogInformation(
            "Linking scope to playbook via {RelationshipTable}: Playbook {PlaybookId} -> {ScopeType} {ScopeId}",
            relationshipTable, playbookId, scopeType, scopeId);

        // TODO: Replace with actual Dataverse Web API associate call
        // POST /api/data/v9.2/sprk_aianalysisplaybooks({playbookId})/{relationshipName}/$ref
        // Body: { "@odata.id": "sprk_aianalysisactions({scopeId})" }

        lock (_lockObject)
        {
            var key = (playbookId, scopeType);
            if (!_links.ContainsKey(key))
            {
                _links[key] = new HashSet<Guid>();
            }
            _links[key].Add(scopeId);
        }

        _logger.LogInformation(
            "Linked scope to playbook: Playbook {PlaybookId} -> {ScopeType} {ScopeId}",
            playbookId, scopeType, scopeId);

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task UnlinkScopeFromPlaybookAsync(Guid playbookId, ScopeType scopeType, Guid scopeId, CancellationToken cancellationToken)
    {
        var relationshipTable = RelationshipTableNames[scopeType];

        _logger.LogInformation(
            "Unlinking scope from playbook via {RelationshipTable}: Playbook {PlaybookId} -> {ScopeType} {ScopeId}",
            relationshipTable, playbookId, scopeType, scopeId);

        // TODO: Replace with actual Dataverse Web API disassociate call
        // DELETE /api/data/v9.2/sprk_aianalysisplaybooks({playbookId})/{relationshipName}({scopeId})/$ref

        bool removed;
        lock (_lockObject)
        {
            var key = (playbookId, scopeType);
            removed = _links.ContainsKey(key) && _links[key].Remove(scopeId);
        }

        if (removed)
        {
            _logger.LogInformation(
                "Unlinked scope from playbook: Playbook {PlaybookId} -> {ScopeType} {ScopeId}",
                playbookId, scopeType, scopeId);
        }
        else
        {
            _logger.LogDebug(
                "Link did not exist (idempotent unlink): Playbook {PlaybookId} -> {ScopeType} {ScopeId}",
                playbookId, scopeType, scopeId);
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsLinkedAsync(Guid playbookId, ScopeType scopeType, Guid scopeId, CancellationToken cancellationToken)
    {
        // TODO: Replace with actual Dataverse Web API query
        // GET /api/data/v9.2/sprk_aianalysisplaybooks({playbookId})/{relationshipName}?$filter=...

        bool exists;
        lock (_lockObject)
        {
            var key = (playbookId, scopeType);
            exists = _links.ContainsKey(key) && _links[key].Contains(scopeId);
        }

        return Task.FromResult(exists);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> GetLinkedScopeIdsAsync(Guid playbookId, ScopeType scopeType, CancellationToken cancellationToken)
    {
        // TODO: Replace with actual Dataverse Web API query
        // GET /api/data/v9.2/sprk_aianalysisplaybooks({playbookId})/{relationshipName}?$select=...

        IReadOnlyList<Guid> result;
        lock (_lockObject)
        {
            var key = (playbookId, scopeType);
            result = _links.ContainsKey(key) ? _links[key].ToList() : new List<Guid>();
        }

        _logger.LogDebug(
            "Retrieved {Count} linked {ScopeType} scopes for playbook {PlaybookId}",
            result.Count, scopeType, playbookId);

        return Task.FromResult(result);
    }

    // ========================================
    // Utility Methods
    // ========================================

    /// <inheritdoc />
    public bool IsSystemScope(string name)
    {
        return name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<string> GenerateUniqueNameAsync(ScopeType scopeType, string baseName, CancellationToken cancellationToken)
    {
        var existingNames = await GetExistingNamesAsync(scopeType, cancellationToken);

        if (!existingNames.Contains(baseName, StringComparer.OrdinalIgnoreCase))
        {
            return baseName;
        }

        // Add suffix (1), (2), etc.
        var counter = 1;
        string candidateName;
        do
        {
            candidateName = $"{baseName} ({counter})";
            counter++;
        } while (existingNames.Contains(candidateName, StringComparer.OrdinalIgnoreCase) && counter < 1000);

        return candidateName;
    }

    // ========================================
    // Private Helpers
    // ========================================

    private void ValidateCanModify(string name, string operation)
    {
        if (IsSystemScope(name))
        {
            throw new ScopeOwnershipException(name, operation);
        }
    }

    private static string EnsureCustomerPrefix(string name)
    {
        if (name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Cannot create scope with system prefix '{SystemPrefix}'.");
        }

        if (name.StartsWith(CustomerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return $"{CustomerPrefix}{name}";
    }

    private async Task<HashSet<string>> GetExistingNamesAsync(ScopeType scopeType, CancellationToken cancellationToken)
    {
        var options = new ScopeListOptions { PageSize = 1000 };

        return scopeType switch
        {
            ScopeType.Action => (await _scopeResolver.ListActionsAsync(options, cancellationToken))
                .Items.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ScopeType.Skill => (await _scopeResolver.ListSkillsAsync(options, cancellationToken))
                .Items.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ScopeType.Knowledge => (await _scopeResolver.ListKnowledgeAsync(options, cancellationToken))
                .Items.Select(k => k.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ScopeType.Tool => (await _scopeResolver.ListToolsAsync(options, cancellationToken))
                .Items.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ScopeType.Output => new HashSet<string>(), // Output listing not yet available
            _ => new HashSet<string>()
        };
    }
}
